using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MellowDesktop {
 public sealed partial class Ghost {
    enum EdgeMove { None, Approach, Ascend, Traverse }
    EdgeMove edgeMove;
    double gripWall, gripWeight, overhead;
    readonly Point[] palms=new Point[2]; // In viewport coordinates, in display units.
    readonly double[] rimBlend=new double[2];
    readonly Model3DGroup[] mitts={new Model3DGroup(),new Model3DGroup()};
    double looseArm;
    readonly bool[] planted=new bool[2];
    readonly Model3DGroup gripRig=new Model3DGroup();
    readonly MeshGeometry3D[] sleeves=new MeshGeometry3D[2];
    const int sleeveRows=17, sleeveSides=12;

    bool climbAfterLanding;
    int lastClimbSide;
    void RequestEdgeClimb(){
        if(edgeMove==EdgeMove.Ascend || edgeMove==EdgeMove.Traverse)return;
        LeavePerch();CancelTarget();
        if(window.Top<Bounds().Bottom-2) {
            ReleaseEdge(walkTime);climbAfterLanding=true;return;
        }
        pathStage=1;
        StartEdgeLeg(EdgeMove.Approach,walkTime,true);
        Signal("idle","",0);
    }
    void StartEdgeLeg(EdgeMove leg,double clock,bool nearest=false){
        edgeMove=leg;
        var b=Bounds();
        if(leg==EdgeMove.Approach && window.Top<b.Bottom-2){
            ReleaseEdge(clock);climbAfterLanding=true;return;
        }
        if(leg==EdgeMove.Approach){
            int near=window.Left+window.Width*.5<SystemParameters.PrimaryScreenWidth*.5?-1:1;
            lastClimbSide=nearest || lastClimbSide==0?near:-lastClimbSide;
            gripWall=lastClimbSide;
        }
        double x=gripWall<0?b.Left:b.Right;
        var next=new Point(x,leg==EdgeMove.Approach?b.Bottom:b.Top);
        if(leg==EdgeMove.Traverse) {
            double direction=-gripWall;
            next.X=Clamp(window.Left+direction*size*1.65,b.Left,b.Right);
        }
        Travel(next,clock,leg==EdgeMove.Ascend,false,leg);
        holdUntil=clock+travel+(leg==EdgeMove.Ascend?2.8:leg==EdgeMove.Traverse?1.7:.22);
    }
    void ReleaseEdge(double clock){
        var floor=SystemParameters.WorkArea;
        double x=Clamp(window.Left-gripWall*size*.65,floor.Left,floor.Right-window.Width);
        descending=true;pathStage=0;
        Travel(new Point(x,Bounds().Bottom),clock,false,true);
        holdUntil=double.PositiveInfinity;Signal("idle","",0);
    }
    Point ViewOrigin(){
        var source=PresentationSource.FromVisual(view);
        if(source!=null && view.IsArrangeValid)
            return source.CompositionTarget.TransformFromDevice.Transform(view.PointToScreen(new Point()));
        return new Point(window.Left+(window.Width-size)*.5,window.Top+window.Height-size);
    }
    static double SmoothRange(double x){return Ease(Clamp(x,0,1));}
    void UpdateGrips(double now,double dt){
        bool frame=ClimbingFrame || (perch!=IntPtr.Zero && ledgeLeg==LedgeLeg.Mount);
        double elapsed=walkTime-departure;
        double want=frame || edgeMove==EdgeMove.Ascend || edgeMove==EdgeMove.Traverse?1:
            edgeMove==EdgeMove.Approach?SmoothRange(1-(travel-elapsed)/.35):0;
        if(perch!=IntPtr.Zero && ledgeLeg==LedgeLeg.Mount)want=1-SmoothRange(elapsed/Math.Max(.01,travel));
        planted[0]=planted[1]=false;
        gripWeight+=Clamp(want-gripWeight,-dt*4.5,dt*5.5);
        if(want<=0)return;
        Point start=ViewOrigin();
        if(frame)gripWall=wallDirection;
        double wall=frame?(leftEdge?previousLedge.Left:previousLedge.Right):
            (gripWall<0?0:SystemParameters.PrimaryScreenWidth);
        double pitch=size*.145;
        double risen=edgeMove==EdgeMove.Ascend || ClimbingFrame?Math.Max(0,origin.Y-window.Top):0;
        if(risen>0) {
            double height=Math.Abs(destination.Y-origin.Y);
            pitch=height/Math.Max(1,Math.Round(height/pitch));
        }
        double q=risen/pitch;
        // Window origins round to display pixels. Finish the last reach on its
        // rung before transferring load at the corner, even at fractional sizes.
        if(edgeMove==EdgeMove.Ascend && elapsed>=travel) q=Math.Round(Math.Abs(destination.Y-origin.Y)/pitch);
        int stroke=(int)Math.Floor(q);
        double p=q-stroke;
        double transfer=edgeMove==EdgeMove.Ascend?elapsed-travel:edgeMove==EdgeMove.Traverse?2:0;
        overhead=0;looseArm=0;
        for(int i=0;i<2;i++){
            double side=2*i-1;
            bool swing=(stroke%2)==i;
            double mark=swing?stroke-1+2*SmoothRange(p):stroke;
            double curl=swing?Math.Pow(Math.Sin(Math.PI*p),2):0;
            double x=wall-start.X-gripWall*(1+size*.085*curl);
            double y=size*.43+risen-mark*pitch;
            if(frame)y=Clamp(start.Y+y,previousLedge.Top+1,previousLedge.Bottom-1)-start.Y;
            double over=SmoothRange((transfer-(side==gripWall?.58:0))/.55);
            double topX=size*(.5+side*.32),topY=.8-start.Y;
            bool topSwing=false;
            if(edgeMove==EdgeMove.Traverse) {
                double distance=destination.X-origin.X;
                int count=Math.Max(1,(int)Math.Ceiling(Math.Abs(distance)/pitch));
                double travelled=Math.Abs(window.Left-origin.X);
                double station=Clamp(travelled/Math.Max(.001,Math.Abs(distance))*count,0,count);
                int k=Math.Min(count,(int)Math.Floor(station+.5));
                double a=Math.Max(0,k-.5),b=Math.Min(count,k+.5);
                double t=Clamp((station-a)/(b-a),0,1);
                topSwing=(k%2)==i;
                double from=Math.Max(0,k-1),to=Math.Min(count,k+1);
                double anchor=topSwing?from+(to-from)*SmoothRange(t):k;
                topX=origin.X+window.Width*.5+side*.32*size+distance*anchor/count-start.X;
                if(topSwing)topY+=size*.105*Math.Pow(Math.Sin(Math.PI*t),2);
            }
            // Settle the weight on the inside hand. The outside hand drops for
            // a beat, then catches the rim again before the first traverse step.
            double rest=edgeMove==EdgeMove.Ascend?elapsed-travel:elapsed-travel+1.12;
            double loose=side==gripWall && over==1?SmoothRange((rest-1.12)/.32)*
                (edgeMove==EdgeMove.Ascend?1-SmoothRange((rest-2.22)/.44):1):0;
            looseArm+=loose;
            topX+=(size*(.5+side*.32)-topX)*loose;
            topY+=(size*(.64+.025*Math.Sin(now*3.2))-topY)*loose;
            palms[i]=new Point(x+(topX-x)*over,y+(topY-y)*over);
            rimBlend[i]=over*(1-loose);
            overhead+=over*.5;
            planted[i]=gripWeight>.99 && ((over==1 && loose==0 && (!topSwing || elapsed>=travel)) || (over==0 && (!swing || curl<.001)));
        }
    }

    void CreateGripRig(Model3DGroup scene){
        if(clothGhost!=null)return;
        var silk=new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(235,215,237)));
        for(int hand=0;hand<2;hand++){
            var mesh=new MeshGeometry3D();sleeves[hand]=mesh;
            for(int i=0;i<sleeveRows;i++)for(int j=0;j<sleeveSides;j++){
                int a=i*sleeveSides+j,b=i*sleeveSides+(j+1)%sleeveSides;
                foreach(int n in new[]{a,b,a+sleeveSides,b,b+sleeveSides,a+sleeveSides})mesh.TriangleIndices.Add(n);
            }
            gripRig.Children.Add(new GeometryModel3D(mesh,silk){BackMaterial=silk});
        }
        for(int i=0;i<2;i++){
            // A soft folded paw presents a broad contact patch across the lip.
            Oval(mitts[i],"#EAD8E9",0,0,0,.136,.075,.089);
            Oval(mitts[i],"#E5CFE4",(2*i-1)*.076,-.054,.038,.067,.053,.065);
            gripRig.Children.Add(mitts[i]);
        }
        gripRig.Transform=new ScaleTransform3D(0,0,0);
        scene.Children.Add(gripRig);
    }
    void RenderGrips(double now){
        if(gripWeight<.001){
            if(gripRig.Transform==Transform3D.Identity)gripRig.Transform=new ScaleTransform3D(0,0,0);
            return;
        }
        gripRig.Transform=Transform3D.Identity;
        var cam=(OrthographicCamera)view.Camera;
        var up=Vector3D.CrossProduct(Vector3D.CrossProduct(cam.LookDirection,cam.UpDirection),cam.LookDirection);up.Normalize();
        double units=cam.Width/size;
        double axis=Vector3D.DotProduct((Vector3D)cam.Position,up);
        var torso=body.Transform.Value;torso.Append(parts.Transform.Value);
        for(int i=0;i<2;i++){
            double side=i*2-1;
            Point3D root=torso.Transform(new Point3D(side*.49,.45,.17));
            double z=clothGhost==null?(side==gripWall?.84:.76):(side==gripWall?.67:.32);
            double x=(palms[i].X-size*.5)*units;
            double y=(axis+(.5*size-palms[i].Y)*units-up.Z*z)/up.Y;
            if(clothGhost!=null){
                // Contacts live in screen space. Undo every animated parent, including cloth bob.
                var actorToWorld=clothGhost.Model.Transform.Value;actorToWorld.Append(torso);actorToWorld.Invert();
                // Curl the pad over the boundary so its rounded end bears on the lip.
                var contact=new Point3D(x+1.6*units*gripWall*(1-rimBlend[i]),y+1.6*units*rimBlend[i]/up.Y,z);
                clothGhost.BendSleeve(i,actorToWorld.Transform(contact),gripWeight,rimBlend[i]);
            } else {
                Point3D tucked=torso.Transform(new Point3D(side*.73,.06,.21));
                Point3D tip=tucked+(new Point3D(x,y,z)-tucked)*gripWeight;
                double rim=rimBlend[i];
                double fold=gripWeight;
                Point3D wrist=tip+new Vector3D(-gripWall*.035*(1-rim),-.025,.03)*fold;
                Point3D elbow=root+(wrist-root)*.44+new Vector3D(side*(.22+.33*rim),-.31,.12);
                var paw=new Transform3DGroup();
                paw.Children.Add(new ScaleTransform3D(fold,fold,fold));
                paw.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),gripWall*78*(1-rim))));
                paw.Children.Add(new TranslateTransform3D(tip.X-gripWall*.036*(1-rim),tip.Y-.032*rim,tip.Z+.025));
                mitts[i].Transform=paw;
                var points=new Point3DCollection((sleeveRows+1)*sleeveSides);
                for(int row=0;row<=sleeveRows;row++){
                    double t=(double)row/sleeveRows;
                    var first=root+(elbow-root)*t;var second=elbow+(wrist-elbow)*t;
                    Point3D centre=first+(second-first)*t;
                    var tangent=second-first;tangent.Normalize();
                    var normal=Vector3D.CrossProduct(tangent,new Vector3D(0,0,1));normal.Normalize();
                    var depth=Vector3D.CrossProduct(normal,tangent);
                    // A continuous tapered sleeve swells into a soft mitten at the grip.
                    double radius=(.145-.06*t+.023*Math.Exp(-Math.Pow((t-.82)/.15,2)))*
                        Math.Min(1,Math.Sqrt(Math.Max(0,1-t)*10))*gripWeight;
                    for(int j=0;j<sleeveSides;j++){
                        double a=j*2*Pi/sleeveSides;
                        points.Add(centre+normal*(radius*Math.Cos(a))+depth*(radius*Math.Sin(a)));
                    }
                }
                sleeves[i].Positions=points;
                arms[i].Transform=new ScaleTransform3D(1-gripWeight,1-gripWeight,1-gripWeight,side*.55,.32,0);
            }
            var shoes=clothGhost==null?feet:clothGhost.Feet;
            double swing=Math.Sin(now*3.2-.65*i)*overhead;
            var legs=new Transform3DGroup();legs.Children.Add(shoes[i].Transform);
            legs.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),
                gripWeight*(side*10+11*swing)),0,.05,0));
            legs.Children.Add(new TranslateTransform3D(gripWeight*(gripWall*.14*(1-overhead)+.08*swing),
                gripWeight*(.045+.085*(1-overhead)*Math.Max(0,side*gripWall)),0));
            shoes[i].Transform=legs;
        }
    }
 }
}
