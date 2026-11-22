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
            double top=a.Top-(window.Height-size);
            return new Rect(a.Left,top,Math.Max(0,a.Width-window.Width),Math.Max(0,(double.IsNaN(floorY)?a.Bottom:floorY)-window.Height+FootInset-top));
        }
        void Travel(Point next,double clock,bool vertical) {
            var b=Bounds();origin=new Point(window.Left,window.Top);
            destination=new Point(Clamp(next.X,b.Left,b.Right),Clamp(next.Y,b.Top,b.Bottom));
            travel=Math.Max(.45,(destination-origin).Length/(vertical?115:155));
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
            if(!moving && clock>=holdUntil){
                if(guiding){CancelTarget();pathStage=0;}
                Point next;
                descending=false;
                switch(pathStage++%5){
                    case 0: next=new Point(destination.X<b.Left+b.Width/2?b.Left:b.Right,destination.Y);break;
                    case 1: next=new Point(destination.X,b.Top);break;
                    case 2: next=new Point(b.Left+b.Width*(.15+.7*random.NextDouble()),b.Top);break;
                    case 3: descending=true;next=new Point(b.Left+b.Width*(.25+.5*random.NextDouble()),b.Bottom);break;
                    default: next=new Point(b.Right-offset,b.Bottom-lift);break;
                }
                Travel(next,clock,pathStage%5==2);
                holdUntil=clock+travel+2+random.NextDouble()*4;
            }
            if(moving){step=weight=0;direction=1;return;}
            double u=travel<=0?1:Clamp((clock-departure)/travel,0,1);
            var pos=origin+(destination-origin)*Ease(u);
            if(descending && !guiding)pos.X+=Math.Sin(u*4*Math.PI)*Math.Sin(u*Math.PI)*18;
            pos.X=Clamp(pos.X,b.Left,b.Right);pos.Y=Clamp(pos.Y,b.Top,b.Bottom);
            double blend=.18;
            glideAmount+=((descending && !guiding && u>0 && u<1?1:0)-glideAmount)*blend;
            hangAmount+=((climb && u>=.96 && !guiding?1:0)-hangAmount)*blend;
            if(!moving){window.Left=pos.X;window.Top=pos.Y;}
            if(guiding && perch==IntPtr.Zero && u>=1 && state!="point")Signal("point",pointCaption,4);
            weight=Math.Sin(Math.PI*u);
            step=Math.Sin((clock-departure)*8)*weight;
            direction=destination.X<origin.X?-1:1;
        }
    }
}
