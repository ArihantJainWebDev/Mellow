using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace MellowDesktop {
 public sealed class HatTheatre {
    const double P=Math.PI;
    readonly Window window=new Window();
    readonly Viewport3D view=new Viewport3D();
    readonly Model3DGroup actor=new Model3DGroup(), face=new Model3DGroup(), hat=new Model3DGroup(), umbrella=new Model3DGroup();
    readonly Model3DGroup[] feet={new Model3DGroup(),new Model3DGroup()};
    readonly Model3DGroup canopy=new Model3DGroup();
    readonly Stopwatch watch=Stopwatch.StartNew();
    readonly DispatcherTimer timer=new DispatcherTimer();
    readonly OrthographicCamera camera=new OrthographicCamera();
    MeshGeometry3D sheet;
    MeshGeometry3D brim;
    Point3D[] clothRest, brimRest;
    double phase, zoomAngle;
    bool paused, small;
    static double Smooth(double x){x=Math.Max(0,Math.Min(1,x));return x*x*(3-2*x);}
    static double Beat(double t,double a,double b){return Smooth((t-a)/(b-a));}
    static double Bell(double x){return Math.Exp(-x*x);}
    static Transform3D Pose(double x,double y,double z,double pitch,double roll,double scale){
        var g=new Transform3DGroup();
        g.Children.Add(new ScaleTransform3D(scale,scale,scale));
        g.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,0),pitch)));
        g.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),roll)));
        g.Children.Add(new TranslateTransform3D(x,y,z));return g;
    }
    static Material Fabric(byte r,byte g,byte b,int seed){
        const int n=128;
        var pixels=new byte[n*n*4];var rng=new Random(seed);
        for(int i=0;i<n*n;i++){
            double tone=.98+.034*rng.NextDouble()+.008*Math.Sin((i%n)*P/2);
            pixels[i*4]=(byte)Math.Min(255,b*tone);pixels[i*4+1]=(byte)Math.Min(255,g*tone);
            pixels[i*4+2]=(byte)Math.Min(255,r*tone);pixels[i*4+3]=255;
        }
        var img=BitmapSource.Create(n,n,96,96,PixelFormats.Bgra32,null,pixels,n*4);
        var brush=new ImageBrush(img){TileMode=TileMode.Tile,Viewport=new Rect(0,0,.12,.18)};
        brush.Freeze();return new DiffuseMaterial(brush);
    }
    static Material ColorMat(string color){return new DiffuseMaterial(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));}
    delegate Point3D Shape(double u,double a);
    static MeshGeometry3D Surface(Model3DGroup group,Material material,int rows,int cols,Shape shape){
        var mesh=new MeshGeometry3D();
        for(int i=0;i<=rows;i++)for(int j=0;j<=cols;j++){
            double u=(double)i/rows,v=(double)j/cols;
            mesh.Positions.Add(shape(u,v*2*P));mesh.TextureCoordinates.Add(new Point(v,u));
            if(i==rows||j==cols)continue;
            int k=i*(cols+1)+j,l=k+cols+1;
            foreach(int index in new[]{k,k+1,l,k+1,l+1,l})mesh.TriangleIndices.Add(index);
        }
        group.Children.Add(new GeometryModel3D(mesh,material){BackMaterial=material});return mesh;
    }
    static void Oval(Model3DGroup group,Material material,double x,double y,double z,double rx,double ry,double rz){
        Surface(group,material,18,32,(u,a)=>new Point3D(x+rx*Math.Sin(P*u)*Math.Cos(a),y+ry*Math.Cos(P*u),z+rz*Math.Sin(P*u)*Math.Sin(a)));
    }
    static Point3D Cloth(double h,double a){
        double r=h<.34?.48*Math.Sin(h/.34*P/2):.48+.27*Smooth((h-.34)/.62);
        double y=h<.34?.64+.51*Math.Cos(h/.34*P/2):.64-1.34*(h-.34);
        double folds=Smooth((h-.3)/.66);
        r+=folds*(.054*Math.Cos(7*a+.55*h)+.026*Math.Sin(4*a-.5*h));
        y+=folds*.063*Math.Cos(7*a+.55*h);
        double sleeve=Math.Pow(Math.Abs(Math.Cos(a)),8)*Bell((h-.68)/.145);
        if(h>.955){double edge=(h-.955)/.045;r*=1-.16*Smooth(edge);y+=.046*edge*edge;}
        return new Point3D((r+.245*sleeve)*Math.Cos(a),y+.048*sleeve,.77*r*Math.Sin(a)+.065*sleeve);
    }
    static double Sleeve(double h,double a){return Math.Pow(Math.Abs(Math.Cos(a)),4)*Bell((h-.68)/.20);}
    void Build(){
        var silk=Fabric(242,226,239,27);var felt=Fabric(105,55,102,54);
        sheet=Surface(actor,silk,44,76,Cloth);clothRest=new Point3D[sheet.Positions.Count];sheet.Positions.CopyTo(clothRest,0);
        actor.Children.Add(face);
        var ink=ColorMat("#352038");
        for(int i=0;i<2;i++){
            double side=i*2-1;
            Oval(face,ink,side*.175,.65,.351,.066,.142,.032);
            actor.Children.Add(feet[i]);Oval(feet[i],Fabric(190,155,193,9),side*.208,-.245,.045,.114,.115,.14);
        }
        Surface(face,ink,18,12,(u,a)=>new Point3D((u-.5)*.146,.469+.045*Math.Pow(2*u-1,2)+.013*Math.Sin(P*u)*Math.Cos(a),.389+.013*Math.Sin(P*u)*Math.Sin(a)));
        actor.Children.Add(hat);
        // Hollow crown, with a soft bend toward its narrow tip.
        Surface(hat,felt,36,64,(u,a)=>{
            double r=.56*Math.Pow(1-u,.79);
            return new Point3D(.55*u*u*u+r*Math.Cos(a),.69*Math.Sin(u*P*.84)+.08*u+r*.27*u*Math.Cos(a),.87*r*Math.Sin(a));
        });
        // Rounded closed brim cross-section leaves an opening into the crown.
        brim=Surface(hat,felt,22,76,(u,a)=>{
            double r=.72+.23*Math.Cos(P*2*u);
            return new Point3D(r*Math.Cos(a),.035*Math.Sin(P*2*u)+.092*Math.Cos(2*a+.65)*r*r+.065*r*Math.Cos(a),r*.84*Math.Sin(a));
        });
        brimRest=new Point3D[brim.Positions.Count];brim.Positions.CopyTo(brimRest,0);
        Surface(hat,Fabric(208,164,98,11),6,64,(v,a)=>{
            double u=.064+.085*v,r=.565*Math.Pow(1-u,.79);
            return new Point3D(.55*u*u*u+r*Math.Cos(a),.69*Math.Sin(u*P*.84)+.08*u+r*.27*u*Math.Cos(a),.87*r*Math.Sin(a));
        });
        actor.Children.Add(canopy);
        Surface(canopy,Fabric(56,135,137,18),12,48,(u,a)=>{
            double radius=.68*u;
            return new Point3D(radius*Math.Cos(a),.30*(1-u*u)-.046*Math.Cos(8*a)*u*u,radius*Math.Sin(a));
        });
        Oval(canopy,ColorMat("#956943"),0,-.30,0,.018,.34,.018);
        actor.Children.Add(umbrella);
        var wood=ColorMat("#976940");var teal=Fabric(56,135,137,8);
        Surface(umbrella,teal,24,48,(u,a)=>{
            double r=.012+.145*Math.Pow(u,.65)*(1+.12*Math.Cos(8*a));
            return new Point3D(r*Math.Cos(a),u*.60,r*Math.Sin(a));
        });
        Oval(umbrella,wood,0,.60,0,.023,.125,.023);
        Surface(umbrella,wood,24,12,(u,a)=>{
            double ang=P*u;
            return new Point3D(.074-.074*Math.Cos(ang)+.017*Math.Cos(ang)*Math.Cos(a),.70+.074*Math.Sin(ang)-.017*Math.Sin(ang)*Math.Cos(a),.017*Math.Sin(a));
        });
        Surface(umbrella,ColorMat("#C29960"),3,32,(u,a)=>new Point3D(.11*Math.Cos(a),.35+.032*u,.11*Math.Sin(a)));
    }
    internal Model3DGroup[] Feet {get {return feet;}}
    internal Model3DGroup Model {get {return actor;}}
    internal void Animate(double t,double? desktopStep=null,double left=0,double right=0,double blink=1,double glide=0,double cover=0){
        // Return through the pickup poses at the end so the loop joins cleanly.
        double s=t<=9?t:Math.Max(0,9-(t-9)*1.5);
        double walk=Beat(s,0,2),step=desktopStep??Math.Sin(4*P*walk),bob=.044*Math.Abs(step);
        double reach=Beat(s,2,3.1),lift=Beat(s,3.1,4.25),lower=Beat(s,4.4,5.9);
        double dig=Beat(s,5.9,6.7),reveal=Beat(s,6.8,7.95);
        double settle=Math.Sin((s-7.9)*12)*Math.Exp(-Math.Max(0,s-7.9)*4)*Beat(s,7.9,8.1);
        double sway=.055*step-.075*dig+.15*reveal;
        double hx=.01+.48*lower,hy=.964+.53*lift-1.19*lower,hz=.035+.50*lower;
        double pitch=-9+165*lower,roll=6*step-12*lift+16*lower+3*settle;
        hat.Transform=Pose(hx,hy,hz,pitch,roll,1);
        canopy.Transform=Pose(.55,1.52,.13,0,-10,glide);
        var fabric=new Point3DCollection(clothRest.Length);
        for(int k=0;k<clothRest.Length;k++){
            double h=(double)(k/77)/44,a=(k%77)*2*P/76;
            Point3D p=clothRest[k];double side=Math.Cos(a)>=0?1:-1,w=Sleeve(h,a);
            Point3D grip=hat.Transform.Transform(new Point3D(side*.815,.0,.13));
            double reachX=grip.X-side*.93,reachY=grip.Y-.23,reachZ=grip.Z-.05;
            if(side<0){
                reachX+=dig*(.45-.96*reveal);reachY+=dig*(.01+.81*reveal);reachZ+=dig*.11;
            }
            double hem=Smooth((h-.48)/.52),knee=Math.Max(0,side*step);
            p.X+=w*reach*reachX+sway*(1-h)+.029*hem*Math.Sin(5*a-10*walk)*Math.Abs(step);
            p.Y+=w*reach*reachY+hem*.115*knee*Bell((Math.Sin(a)-.55)/.5);
            p.Z+=w*reach*reachZ+hem*.07*knee*Math.Max(0,Math.Sin(a));
            double gesture=(side<0?left:right)/120;
            p.X+=side*w*.18*gesture;p.Y+=w*.55*gesture;
            p.X-=side*w*.64*cover;p.Y+=w*.41*cover;p.Z+=w*.42*cover;
            fabric.Add(p);
        }
        sheet.Positions=fabric;
        var bent=new Point3DCollection(brimRest.Length);
        foreach(var original in brimRest){
            var p=original;p.Y+=.044*Math.Sin(p.X*4+.3)*reach+.032*p.X*step;bent.Add(p);
        }
        brim.Positions=bent;
        face.Transform=Pose(sway*.77,-.035*dig+.025*reveal,.003,4*dig,-4*step-4*reveal,1);
        for(int i=0;i<2;i++){
            double stride=(2*i-1)*step;
            feet[i].Transform=Pose(.085*stride,.092*Math.Max(0,stride)-bob,.05*stride,10*stride,0,1);
        }
        var hand=hat.Transform.Transform(new Point3D(-.815,0,.13));
        umbrella.Transform=Pose(hand.X+.45-.96*reveal,hand.Y+.01+.81*reveal,hand.Z+.11,-14,-22,.85*Beat(s,6.85,7.55));
        actor.Transform=Pose(desktopStep.HasValue?0:-.18+.34*walk,bob+.015*settle,0,0,1.6*step+2*reveal,1);
        for(int i=0;i<2;i++)((GeometryModel3D)face.Children[i]).Transform=new ScaleTransform3D(1,blink*(1-.89*Bell((s-5.66)/.085)),1,0,.65,0);
    }
    void Camera(){
        double a=zoomAngle*P/180;camera.Position=new Point3D(4*Math.Sin(a),1.16,4*Math.Cos(a));
        camera.LookDirection=new Vector3D(-4*Math.Sin(a),-.38,-4*Math.Cos(a));camera.UpDirection=new Vector3D(0,1,0);camera.Width=3.65;
    }
    internal HatTheatre(bool modelOnly=false){
        Build();if(modelOnly)return;var scene=new Model3DGroup();scene.Children.Add(actor);
        // Feathered contact shadow grounds the little feet throughout the steps.
        var shadowBrush=new RadialGradientBrush(Color.FromArgb(115,21,14,22),Color.FromArgb(0,21,14,22));
        var floor=new MeshGeometry3D();
        foreach(var pt in new[]{new Point3D(-1,-.364,-.66),new Point3D(1,-.364,-.66),new Point3D(1,-.364,.66),new Point3D(-1,-.364,.66)})floor.Positions.Add(pt);
        foreach(var uv in new[]{new Point(0,0),new Point(1,0),new Point(1,1),new Point(0,1)})floor.TextureCoordinates.Add(uv);
        foreach(int index in new[]{0,2,1,0,3,2})floor.TriangleIndices.Add(index);
        scene.Children.Add(new GeometryModel3D(floor,new DiffuseMaterial(shadowBrush)));
        scene.Children.Add(new AmbientLight(Color.FromRgb(103,89,103)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(220,206,196),new Vector3D(.62,-.9,-1)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(101,94,119),new Vector3D(-1,.12,-.28)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(116,91,104),new Vector3D(.2,-.6,.9)));
        view.Camera=camera;Camera();view.Children.Add(new ModelVisual3D{Content=scene});
        window.Title="Mellow • Hat trick";window.Width=520;window.Height=480;
        var grid=new Grid{Background=new SolidColorBrush(Color.FromRgb(55,48,58))};
        grid.Children.Add(view);window.Content=grid;
        window.ToolTip="Space pauses • R replays • arrows rotate • Tab changes size • Esc exits";
        window.PreviewKeyDown+=(o,e)=>{
            switch(e.Key){
                case Key.Space:paused=!paused;break;
                case Key.R:phase=0;paused=false;break;
                case Key.Left:zoomAngle-=15;Camera();break;
                case Key.Right:zoomAngle+=15;Camera();break;
                case Key.Tab:small=!small;window.Width=small?144:520;window.Height=small?166:480;break;
                case Key.Escape:window.Close();break;
                default:return;
            }e.Handled=true;
        };
        double previous=0;timer.Interval=TimeSpan.FromMilliseconds(33);
        timer.Tick+=delegate{double now=watch.Elapsed.TotalSeconds;if(!paused){phase=(phase+now-previous)%15;Animate(phase);}previous=now;};
        window.Closed+=delegate{timer.Stop();};
    }
    void Snapshot(string path){
        int cell=420;var dv=new DrawingVisual();
        using(var dc=dv.RenderOpen()){
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(55,48,58)),null,new Rect(0,0,cell*4,cell));
            double[] beats={.72,4.23,6.59,8.28};
            for(int i=0;i<4;i++){
                Animate(beats[i]);view.Measure(new Size(cell,cell));view.Arrange(new Rect(0,0,cell,cell));view.UpdateLayout();
                var tile=new RenderTargetBitmap(cell,cell,96,96,PixelFormats.Pbgra32);tile.Render(view);
                dc.DrawImage(tile,new Rect(i*cell,0,cell,cell));
            }
        }
        var frame=new RenderTargetBitmap(cell*4,cell,96,96,PixelFormats.Pbgra32);frame.Render(dv);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(frame));using(var f=File.Create(path))png.Save(f);
    }
    public static void Run(string preview){
        var app=new HatTheatre();app.Animate(0);
        if(!String.IsNullOrEmpty(preview)){app.Snapshot(preview);return;}
        var closeName=Environment.GetEnvironmentVariable("MELLOW_CLOSE_EVENT");
        using(var signal=String.IsNullOrEmpty(closeName)?null:System.Threading.EventWaitHandle.OpenExisting(closeName)){
            if(signal!=null)app.timer.Tick+=delegate{if(signal.WaitOne(0))app.window.Close();};
            app.timer.Start();app.window.ShowDialog();
        }
    }
 }
}