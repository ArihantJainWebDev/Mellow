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
    public sealed class Ghost {
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
            Mesh(body,"#E8DAF4",delegate(double a,double b) {
                double h=(1-Math.Cos(a))/2, r=Math.Sin(a)*(.58+.36*h);
                // Gather the lower skirt into six soft folds and scallop the hem.
                double fold=Math.Pow(h,3)*Math.Cos(6*b);
                return new Point3D(r*(1+.20*fold)*Math.Cos(b),1.17-1.52*h-.20*fold*Math.Sin(a),
                    .83*r*(1+.20*fold)*Math.Sin(b));
            });
            Mesh(body,"#E8DAF4",delegate(double a,double b) {
                double t=a/Pi, r=.19*Math.Sin(a);
                return new Point3D(.02+.62*t,1.01+.22*t-.12*Math.Sin(a)+r*Math.Cos(b),
                    -.05+r*Math.Sin(b));
            });
            body.Children.Add(eyes);
            for(int i=0;i<2;i++) {
                int side=i*2-1;
                Oval(eyes,"#292238",side*.225,.59,.552,.082,.117,.046);
                Oval(eyes,"#FAF5FF",side*.225-.019,.626,.590,.018,.023,.012);
                arms[i]=Oval(body,"#DDCBEF",side*.675,.12,.22,.148,.214,.16);
                feet[i]=Oval(parts,"#CFBAE2",side*.245,-.43,.17,.164,.145,.205);
            }
            var scene=new Model3DGroup();
            scene.Children.Add(new AmbientLight(Color.FromRgb(141,130,151)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(255,247,255),new Vector3D(.4,-.8,-1)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(173,160,210),new Vector3D(-.7,.1,.6)));
            scene.Children.Add(parts);
            view.Camera=new OrthographicCamera(new Point3D(0,.35,5),new Vector3D(0,0,-1),new Vector3D(0,1,0),2.1);
            view.Children.Add(new ModelVisual3D{Content=scene});
            window=new Window{Width=116,Height=116,WindowStyle=WindowStyle.None,
                AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,
                ShowActivated=false,Topmost=true,ResizeMode=ResizeMode.NoResize,Content=view,
                Focusable=false,WindowStartupLocation=WindowStartupLocation.Manual};
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
            double cycle=Math.Floor(t/16), phase=t%16;
            double u=Math.Max(0,Math.Min(1,(phase-8)/4));
            double ease=u*u*(3-2*u), direction=cycle%2==0?-1:1;
            double stride=Math.Sin(4*Pi*u), weight=Math.Sin(Pi*u);
            var area=SystemParameters.WorkArea;
            double distance=Math.Min(132,area.Width*.18);
            window.Left=Math.Max(area.Left,area.Right-window.Width-30-distance*(direction<0?ease:1-ease));
            window.Top=area.Bottom-window.Height+4;
            var turn=new Transform3DGroup();
            turn.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),direction*56*weight-9)));
            turn.Children.Add(new TranslateTransform3D(0,.013*Math.Sin(t*2)+.044*Math.Abs(stride),0));
            parts.Transform=turn;
            for(int i=0;i<2;i++) {
                double step=stride*(2*i-1);
                feet[i].Transform=new TranslateTransform3D(direction*.11*step,.077*Math.Max(0,step),.04*step);
                arms[i].Transform=new TranslateTransform3D(0,.012*Math.Sin(t*2),-.085*step);
            }
            double blink=Math.Max(.09,Math.Min(1,Math.Abs(t%4.7-3.9)*8));
            eyes.Transform=new ScaleTransform3D(1,blink,1,0,.59,0);
        }
        void Frame() {
            frames++;
            Pose(time.Elapsed.TotalSeconds*(verify?6:1));
            if(verify) keptFocus &= foreground==GetForegroundWindow();
            if(verify && frames==12 && !String.IsNullOrEmpty(preview)) {
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(116,116,96,96,PixelFormats.Pbgra32);
                bitmap.Render(view);
                var png=new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using(var stream=System.IO.File.Create(preview)) png.Save(stream);
            }
            if(verify && time.Elapsed.TotalSeconds>2.6) window.Close();
        }
        public static void Run(bool verify,string preview) {
            var ghost=new Ghost(verify,preview);
            ghost.foreground=GetForegroundWindow();
            ghost.Pose(0);
            ghost.window.Show();
            ghost.time.Start();
            ghost.timer.Start();
            ghost.window.Closed+=delegate {Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);};
            Dispatcher.Run();
            if(verify) {
                Console.WriteLine("Frames={0} passiveWindow={1} focusPreserved={2}",ghost.frames,ghost.clickThrough,ghost.keptFocus);
                if(!ghost.clickThrough || !ghost.keptFocus || ghost.frames<12) throw new InvalidOperationException("Avatar smoke check failed.");
            }
        }
    }
}

