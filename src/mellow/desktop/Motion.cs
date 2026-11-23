using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Forms=System.Windows.Forms;

namespace MellowDesktop {
    public sealed partial class Ghost {
        [StructLayout(LayoutKind.Sequential)]
        struct InputStamp {public uint size, tick;}
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref InputStamp stamp);
        uint previousInput;
        System.Drawing.Point previousCursor;
        double attentiveUntil, attentionStarted;
        Point origin, destination;
        bool placed, climb, guiding, pointingLeft, guideKey;
        int pathStage;
        double departure, travel, holdUntil;
        double lean, yaw, armLeft, armRight, mouthOpen=.35;
        GeometryModel3D lip;
        static double Clamp(double x,double min,double max){return Math.Max(min,Math.Min(max,x));}
        static double Ease(double t){return t*t*(3-2*t);}
        double pauseAt=double.NaN;
        void NoticeActivity(double now) {
            var stamp=new InputStamp{size=8};
            var cursor=Forms.Cursor.Position;
            if(GetLastInputInfo(ref stamp) && previousInput!=0 && stamp.tick!=previousInput &&
               cursor==previousCursor) {
                if(now>=attentiveUntil) attentionStarted=now;
                attentiveUntil=now+1.8;
            }
            previousCursor=cursor;previousInput=stamp.tick;
        }
        Rect Bounds() {
            var a=SystemParameters.WorkArea;
            // The transparent caption space sits above the model's crown.
            double top=(edgeMove==EdgeMove.None?a.Top:-size*.055)-(window.Height-size);
            double margin=perch==IntPtr.Zero && !landing && edgeMove==EdgeMove.None?0:Math.Max(0,window.Width*.5-size*(edgeMove==EdgeMove.None?.22:.36));
            double left=edgeMove==EdgeMove.None?a.Left:0, width=edgeMove==EdgeMove.None?a.Width:SystemParameters.PrimaryScreenWidth;
            return new Rect(left-margin,top,Math.Max(0,width-window.Width+2*margin),Math.Max(0,(double.IsNaN(floorY)?a.Bottom:floorY)-window.Height+FootInset-top));
        }
        double walkDuration, riseDuration;
        int stepCount, swingFoot;
        bool onFoot;
        double nextFootfall;
        double swingHeight;
        readonly double[] footOffset=new double[2];

        // Each half-stride leaves one footprint fixed in desktop coordinates.
        // The first and last half-steps start and finish with both feet under him.
        double SetFootprints(double progress,double distance) {
            double q=progress*stepCount;
            int anchor=Math.Min(stepCount,(int)Math.Floor(q+.5));
            double start=Math.Max(0,anchor-.5), end=Math.Min(stepCount,anchor+.5);
            double t=Clamp((q-start)/(end-start),0,1);
            double stepLength=distance/stepCount;
            swingFoot=(anchor+(distance>0?1:0))%2;
            int from=Math.Max(0,anchor-1), to=Math.Min(stepCount,anchor+1);
            footOffset[1-swingFoot]=(anchor-q)*stepLength;
            footOffset[swingFoot]=(from+(to-from)*Ease(t)-q)*stepLength;
            // Interior footprints narrow as the body turns side-on. Move each
            // foot into the narrower track only while it is in the air.
            double inset=(swingFoot==0?1:-1)*size*.039;
            double startInset=from==0?0:inset, endInset=to==stepCount?0:inset;
            footOffset[swingFoot]+=startInset+(endInset-startInset)*Ease(t);
            if(anchor>0 && anchor<stepCount) footOffset[1-swingFoot]-=inset;
            double arc=Math.Pow(Math.Sin(Math.PI*t),2)*Math.Min(1,Math.Abs(stepLength)/(size*.12));
            swingHeight=size*.055*arc;
            // Allow an interrupted walk to finish this step before pausing.
            double landing=end/stepCount;
            // Invert the smoothstep used to position the body along this leg.
            double landingTime=.5-Math.Sin(Math.Asin(1-2*landing)/3);
            double downFirst=destination.Y>origin.Y?riseDuration:0;
            nextFootfall=departure+(downFirst+landingTime*walkDuration)*travel/(walkDuration+riseDuration);
            return (swingFoot==1?1:-1)*arc;
        }
        void Travel(Point next,double clock,bool vertical,bool falling=false,EdgeMove edge=EdgeMove.None) {
            edgeMove=edge;climbAfterLanding=false;
            if(landing && !falling) descending=false;
            landing=falling;
            var b=Bounds();origin=new Point(window.Left,window.Top);
            destination=new Point(Clamp(next.X,b.Left,b.Right),Clamp(next.Y,b.Top,b.Bottom));
            double horizontal=Math.Abs(destination.X-origin.X);
            stepCount=Math.Max(1,(int)Math.Ceiling(horizontal/(size*.145)));
            walkDuration=horizontal<.01?0:Math.Max(.34,horizontal/(size*.49));
            riseDuration=Math.Abs(destination.Y-origin.Y)/115;
            travel=descending && !guiding?Math.Max(.45,(destination-origin).Length/155):Math.Max(.45,walkDuration+riseDuration);
            if(edge==EdgeMove.Ascend || ClimbingFrame) {
                riseDuration=Math.Abs(destination.Y-origin.Y)/(size*.53);
                travel=Math.Max(.55,.30+riseDuration);
            }
            if(edge==EdgeMove.Traverse)travel=.45+horizontal/(size*.48);
            departure=clock;climb=vertical;
        }
        void PointAtControl() {
            try {
                var cursor=Forms.Cursor.Position;
                var element=AutomationElement.FromPoint(new Point(cursor.X,cursor.Y));
                for(int i=0; element!=null && i<5;i++,element=TreeWalker.ControlViewWalker.GetParent(element)){
                    var type=element.Current.ControlType;
                    if(type!=ControlType.Button && type!=ControlType.Hyperlink)continue;
                    var rect=element.Current.BoundingRectangle;
                    if(rect.IsEmpty || rect.Width<=0 || rect.Height<=0)break;
                    perch=IntPtr.Zero;CancelTarget();target=element;BeginTarget();return;
                }
            } catch(ElementNotAvailableException) {} catch(UnauthorizedAccessException) {}
              catch(InvalidOperationException) {} catch(COMException) {}
            Signal("offer","Hover over a button or link, then try again.",3);
        }
        void Navigate(double clock,out double step,out double weight,out double direction) {
            var b=Bounds();
            if(!placed){
                window.Left=Clamp(b.Right-offset,b.Left,b.Right);window.Top=Clamp(b.Bottom-lift,b.Top,b.Bottom);
                origin=destination=new Point(window.Left,window.Top);placed=true;holdUntil=clock+6;
            }
            if(landing) destination.Y=b.Bottom;
            if(!moving && !landing && perch==IntPtr.Zero && clock>=holdUntil){
                if(guiding){CancelTarget();pathStage=0;}
                Point next;
                descending=false;
                int stage=pathStage++%5;
                if(stage==0 || stage==1 || stage==2) {
                    StartEdgeLeg(stage==0?EdgeMove.Approach:stage==1?EdgeMove.Ascend:EdgeMove.Traverse,clock);
                    b=Bounds();
                } else if(stage==3) {
                    ReleaseEdge(clock);
                    b=Bounds();
                } else {
                    next=new Point(b.Right-offset,b.Bottom);
                    Travel(next,clock,false);
                    holdUntil=clock+travel+2+random.NextDouble()*4;
                }
            }
            onFoot=false;
            if(moving){step=weight=0;direction=1;return;}
            double u=travel<=0?1:Clamp((clock-departure)/travel,0,1);
            var pos=origin+(destination-origin)*Ease(u);
            double horizontalProgress=0;
            bool umbrellaRide=descending && !guiding;
            if(!umbrellaRide && travel>0 && walkDuration>0) {
                // Walk on the lower ledge. Change elevation separately, without paddling the feet.
                double elapsed=u*(walkDuration+riseDuration);
                bool goingDown=destination.Y>origin.Y;
                double walkClock=elapsed-(goingDown?riseDuration:0);
                double verticalClock=elapsed-(goingDown?0:walkDuration);
                horizontalProgress=Ease(Clamp(walkClock/walkDuration,0,1));
                double verticalProgress=riseDuration==0?1:Ease(Clamp(verticalClock/riseDuration,0,1));
                pos=new Point(origin.X+(destination.X-origin.X)*horizontalProgress,
                    origin.Y+(destination.Y-origin.Y)*verticalProgress);
                onFoot=walkClock>=0 && walkClock<walkDuration && horizontalProgress<1;
            }
            if(perch!=IntPtr.Zero && (ledgeLeg==LedgeLeg.Catch || ledgeLeg==LedgeLeg.Mount)){
                // A quick push off, then a pull over the lip before the soles settle.
                double pull=Clamp((u-.16)/.84,0,1);
                double reach=1-Math.Pow(1-pull,2);
                pos=new Point(origin.X+(destination.X-origin.X)*Ease(pull),
                    origin.Y+(destination.Y-origin.Y)*reach-size*.065*Math.Sin(Math.PI*pull));
                onFoot=false;
            }
            if((edgeMove==EdgeMove.Ascend || ClimbingFrame) && travel>0) {
                double pulls=Math.Max(1,Math.Round(Math.Abs(destination.Y-origin.Y)/(size*.17)));
                double phase=Clamp((clock-departure-.30)/Math.Max(.01,travel-.30),0,1)*pulls,stroke=phase-Math.Floor(phase);
                pos.Y=origin.Y+(destination.Y-origin.Y)*(Math.Floor(phase)+Ease(stroke))/pulls;
            }
            if(edgeMove==EdgeMove.Traverse){
                double swing=Clamp((clock-departure-.45)/Math.Max(.01,travel-.45),0,1);
                pos.X=origin.X+(destination.X-origin.X)*Ease(swing);
                onFoot=false;
            }
            if(umbrellaRide && !landing)pos.X+=Math.Sin(u*4*Math.PI)*Math.Sin(u*Math.PI)*18;
            pos.X=Clamp(pos.X,b.Left,b.Right);pos.Y=Clamp(pos.Y,b.Top,b.Bottom);
            double blend=.18;
            glideAmount+=((descending && !guiding && u>0 && u<1?1:0)-glideAmount)*blend;
            hangAmount+=((climb && u>=.96 && !guiding?1:0)-hangAmount)*blend;
            if(!moving){window.Left=pos.X;window.Top=pos.Y;}
            if(landing && u>=1)Land(clock);
            if(guiding && perch==IntPtr.Zero && u>=1 && state!="point")Signal("point",pointCaption,4);
            weight=Math.Sin(Math.PI*u);
            step=onFoot?SetFootprints(horizontalProgress,destination.X-origin.X):
                ClimbingFrame?Math.Sin(Math.PI*(origin.Y-pos.Y)/(size*.17)):
                climb?Math.Sin((clock-departure)*8)*weight:0;
            direction=ClimbingFrame?wallDirection:destination.X<origin.X?-1:1;
        }
    }
}
