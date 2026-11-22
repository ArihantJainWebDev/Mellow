using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MellowDesktop {
    public sealed partial class Ghost {
        const double Pi = Math.PI;
        readonly Model3DGroup parts = new Model3DGroup();
        readonly Model3DGroup body = new Model3DGroup();
        readonly Model3DGroup eyes = new Model3DGroup();
        readonly Viewport3D view = new Viewport3D();
        readonly GeometryModel3D[] feet = new GeometryModel3D[2];
        readonly GeometryModel3D[] arms = new GeometryModel3D[2];
        readonly Window window;
        readonly Stopwatch time = new Stopwatch();
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly bool verify;
        readonly string preview;
        IntPtr foreground;
        int frames;
        bool clickThrough, keptFocus = true;

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);

        delegate Point3D Surface(double lat, double lon);
        static GeometryModel3D Mesh(Model3DGroup parent, string color, Surface surface) {
            var mesh = new MeshGeometry3D();
            const int rings=32, slices=64;
            for (int i=0; i<=rings; i++) {
                for (int j=0; j<=slices; j++) {
                    mesh.Positions.Add(surface(Pi*i/rings, 2*Pi*j/slices));
                    if (i==rings || j==slices) continue;
                    int a=i*(slices+1)+j, b=a+slices+1;
                    foreach(int k in new int[]{a,a+1,b,a+1,b+1,b}) mesh.TriangleIndices.Add(k);
                }
            }
            mesh.Freeze();
            var material=new DiffuseMaterial(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
            var model=new GeometryModel3D(mesh,material);
            model.BackMaterial=material;
            parent.Children.Add(model);
            return model;
        }
        static GeometryModel3D Oval(Model3DGroup parent,string color,double x,double y,double z,double rx,double ry,double rz) {
            return Mesh(parent,color,delegate(double a,double b) {
                return new Point3D(x+rx*Math.Sin(a)*Math.Cos(b), y+ry*Math.Cos(a), z+rz*Math.Sin(a)*Math.Sin(b));
            });
        }
        public Ghost(bool verify,string preview) {
            this.verify=verify;
            this.preview=preview;
            parts.Children.Add(body);
            Mesh(body,"#E4DBEF",delegate(double a,double b) {
                // A domed head eases into a flared skirt, then curls underneath.
                double h=a/Pi;
                double r=h<.5 ? .62*Math.Sin(a) : .62+.13*Math.Sin((h-.5)*Pi);
                double underside=Math.Max(0,(h-.82)/.18);
                r*=Math.Cos(underside*Pi/2);
                double y=h<.5 ? .48+.65*Math.Cos(a) : .48-1.68*(h-.5);
                y-=.066*Math.Cos(5*b)*Math.Exp(-Math.Pow((h-.82)/.12,2));
                return new Point3D(r*Math.Cos(b),y,.79*r*Math.Sin(b));
            });
            // Side-swept lock: round the root and curl the end back upward.
            Mesh(body,"#E4DBEF",delegate(double a,double b) {
                double t=(1-Math.Cos(a))/2;
                double r=.156*Math.Sin(a)*(1-.64*t);
                return new Point3D(-.17+.90*t,1.06+.07*Math.Sin(2*Pi*t)+.085*t*t+r*Math.Cos(b),
                    -.045+r*Math.Sin(b));
            });
            body.Children.Add(eyes);
            for(int i=0;i<2;i++) {
                int side=i*2-1;
                double x=side*.205;
                Oval(eyes,"#282333",x,.57,.456,.085,.126,.052);
                Oval(eyes,"#F6EEFF",x-.018,.614,.503,.023,.028,.012);
                var brow=Oval(body,"#66576D",x,.747,.421,.072,.019,.021);
                brow.Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),-side*9),x,.761,.411);
                arms[i]=Oval(body,"#D7CBE5",side*.655,.112,.16,.108,.193,.12);
                feet[i]=Oval(parts,"#C6B7D3",side*.255,-.336,.13,.151,.125,.204);
            }
            var scene=new Model3DGroup();
            scene.Children.Add(new AmbientLight(Color.FromRgb(136,127,151)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(220,208,215),new Vector3D(.55,-.85,-.72)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(85,74,103),new Vector3D(-.8,.1,-.5)));
            scene.Children.Add(parts);
            view.Camera=new OrthographicCamera(new Point3D(0,.35,5),new Vector3D(0,0,-1),new Vector3D(0,1,0),2.1);
            view.Children.Add(new ModelVisual3D{Content=scene});
            window=new Window{Width=116,Height=116,WindowStyle=WindowStyle.None,
                AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,
                ShowActivated=false,Topmost=true,ResizeMode=ResizeMode.NoResize,Content=view,
                Focusable=false,WindowStartupLocation=WindowStartupLocation.Manual};
            InitDesktop();
            window.SourceInitialized+=delegate {
                IntPtr h=new WindowInteropHelper(window).Handle;
                const int flags=0x08000000|0x20|0x80; // NOACTIVATE, TRANSPARENT, TOOLWINDOW
                SetWindowLong(h,-20,GetWindowLong(h,-20)|flags);
                clickThrough=(GetWindowLong(h,-20)&flags)==flags;
            };
            window.Closed+=delegate {timer.Stop();};
            timer.Interval=TimeSpan.FromMilliseconds(40);
            timer.Tick+=delegate {Frame();};
        }
        void Pose(double t) {
            // Each 16-second cycle contains a long rest, a short walk and another rest.
            double now=t;
            if(state!="idle" && time.Elapsed.TotalSeconds>=until) Signal("idle","",0);
            if(!moving && state=="idle") walkTime+=Math.Max(0,t-lastTick);
            lastTick=t;t=walkTime;
            double cycle=Math.Floor(t/16), phase=t%16;
            double u=Math.Max(0,Math.Min(1,(phase-8)/4));
            double ease=u*u*(3-2*u), direction=cycle%2==0?-1:1;
            double stride=Math.Sin(4*Pi*u), weight=Math.Sin(Pi*u);
            var area=SystemParameters.WorkArea;
            double distance=Math.Min(132,area.Width*.18);
            if(!moving) {
                window.Left=Math.Min(area.Right-window.Width,Math.Max(area.Left,area.Right-window.Width-offset-distance*(direction<0?ease:1-ease)));
                window.Top=Math.Min(area.Bottom-window.Height+4,Math.Max(area.Top,area.Bottom-window.Height+4-lift));
            }
            if(state!="idle") {stride=0;weight=0;}
            double attention=state=="listen"?-12:state=="think"?9:state=="rest"?6:0;
            TurnHead(attention,now);
            var turn=new Transform3DGroup();
            turn.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),state=="privacy"?174:direction*56*weight-9)));
            turn.Children.Add(new TranslateTransform3D(0,.013*Math.Sin(t*2)+.044*Math.Abs(stride),0));
            parts.Transform=turn;
            for(int i=0;i<2;i++) {
                double step=stride*(2*i-1);
                feet[i].Transform=new TranslateTransform3D(direction*.11*step,.077*Math.Max(0,step),.04*step);
                var arm=new Transform3DGroup();
                double gesture=state=="offer"?-110:state=="point"?-78:state=="think"?-35:state=="speak"?-20-10*Math.Sin(now*5):0;
                if(i==1) arm.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,-1),-gesture),.64,.24,.16));
                arm.Children.Add(new TranslateTransform3D(0,(i==1 && gesture!=0?.16:0)+.012*Math.Sin(now*2),-.085*step));
                arms[i].Transform=arm;
            }
            double blink=Math.Max(.09,Math.Min(1,Math.Abs(now%4.7-3.9)*8));
            var gaze=new Transform3DGroup();
            gaze.Children.Add(new ScaleTransform3D(1,state=="rest"?.15:blink,1,0,.59,0));
            gaze.Children.Add(new TranslateTransform3D(direction*.022*Math.Max(0,1-Math.Abs(phase-8)),0,0));
            eyes.Transform=gaze;
        }
        void CaptureMotion(string path) {
            var gif=new System.Windows.Media.Imaging.GifBitmapEncoder();
            for(int frame=0;frame<60;frame++){
                double seconds=frame*.17;
                if(frame==0) Signal("idle","",0);
                if(frame==25) Signal("think","Preparing a reply…",60);
                if(frame==38) Signal("offer","A quick reminder?",60);
                if(frame==50) Signal("rest","",60);
                Pose(seconds+6);
                double width=window.Width,height=window.Height;
                var content=(FrameworkElement)window.Content;
                content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));
                content.UpdateLayout();
                var drawing=new DrawingVisual();
                using(var dc=drawing.RenderOpen()){
                    for(int side=0;side<2;side++){
                        double x=side*260;
                        dc.DrawRectangle(side==0?Brushes.WhiteSmoke:Brushes.MidnightBlue,null,new Rect(x,0,260,height+8));
                        double drift=window.Left-SystemParameters.WorkArea.Right+width+30;
                        dc.DrawRectangle(new VisualBrush(content),null,new Rect(x+58+drift*.4,2,width,height));
                    }
                }
                var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap(520,(int)height+8,96,96,PixelFormats.Pbgra32);
                bmp.Render(drawing);
                var meta=new System.Windows.Media.Imaging.BitmapMetadata("gif");
                meta.SetQuery("/grctlext/Delay",(ushort)17);
                gif.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp,null,meta,null));
            }
            using(var output=System.IO.File.Create(path)) gif.Save(output);
        }
        void TurnHead(double degrees,double now) {
            body.Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),
                degrees+(state=="speak"?2*Math.Sin(now*5):0)),0,.12,0);
        }
        System.Threading.EventWaitHandle closeSignal;
        void Frame() {
            if(closeSignal!=null && closeSignal.WaitOne(0)){window.Close();return;}
            frames++;
            Pose(time.Elapsed.TotalSeconds*(verify?6:1));
            if(verify) keptFocus &= foreground==GetForegroundWindow();
            if(verify && frames==12 && !String.IsNullOrEmpty(preview) && preview.EndsWith(".gif",StringComparison.OrdinalIgnoreCase)) {
                CaptureMotion(preview);
            } else if(verify && frames==12 && !String.IsNullOrEmpty(preview)) {
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(116,116,96,96,PixelFormats.Pbgra32);
                bitmap.Render(view);
                var png=new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using(var stream=System.IO.File.Create(preview)) png.Save(stream);
            }
            if(verify && verifyChat!="1" && frames==16) Signal("offer","Preview: time for a break?",1);
            if(verify && verifyChat=="1") {
                if(verifyStage==0 && frames>=4) {OpenChat();verifyStage=1;}
                if(verifyStage==1 && ready) {
                    verifyStage=2;input.Text="Respond with just MELLOWOK.";SendInput();
                }
                if(verifyStage==2 && ready) {
                    if(!transcript.Text.Contains("MELLOWOK") || state!="speak") throw new InvalidOperationException("Chat acceptance failed.");
                    verifyStage=3; chatWindow.Hide(); Signal("offer","Preview: time for a break?",4);
                }
                if(verifyStage==3 || time.Elapsed.TotalSeconds>65) window.Close();
            } else if(verify && time.Elapsed.TotalSeconds>2.6) window.Close();
        }
        public static void Run(bool verify,string preview,System.Threading.EventWaitHandle closeSignal) {
            var ghost=new Ghost(verify,preview);
            ghost.closeSignal=closeSignal;
            ghost.foreground=GetForegroundWindow();
            ghost.Pose(0);
            ghost.window.Show();
            ghost.time.Start();
            ghost.timer.Start();
            ghost.window.Closed+=delegate {Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);};
            Dispatcher.Run();
            if(verify) {
                Console.WriteLine("Frames={0} passiveWindow={1} focusPreserved={2}",ghost.frames,ghost.clickThrough,ghost.keptFocus);
                if(!ghost.clickThrough || (ghost.verifyChat=="1" ? ghost.verifyStage!=3 : !ghost.keptFocus || ghost.frames<12)) throw new InvalidOperationException("Avatar smoke check failed.");
            }
        }
    }
}

