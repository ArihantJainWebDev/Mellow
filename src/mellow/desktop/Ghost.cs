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
        readonly Model3DGroup[] feet = new Model3DGroup[2];
        readonly Model3DGroup[] arms = new Model3DGroup[2];
        readonly Window window;
        readonly Stopwatch time = new Stopwatch();
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly bool verify,offscreenReview;
        readonly string preview;
        IntPtr foreground;
        int frames;
        bool passiveStyle, keptFocus = true;

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
        readonly double soleInsetFraction;

        // The idle feet are untransformed meshes in model space. Project their
        // lowest visible point onto the vertical axis of the square viewport.
        static double SoleInset(OrthographicCamera camera, Model3DGroup[] soles) {
            var right=Vector3D.CrossProduct(camera.LookDirection,camera.UpDirection);
            var up=Vector3D.CrossProduct(right,camera.LookDirection);
            up.Normalize();
            double lowest=double.PositiveInfinity;
            foreach(var sole in soles)
                foreach(GeometryModel3D mesh in sole.Children)
                    foreach(var point in ((MeshGeometry3D)mesh.Geometry).Positions)
                        lowest=Math.Min(lowest,Vector3D.DotProduct(point-camera.Position,up));
            return .5+lowest/camera.Width;
        }
        readonly HatTheatre clothGhost;
        double privacyCover,pointBlend;
        double trickStart=double.NegativeInfinity;
        public Ghost(bool verify,string preview,bool classic,bool offscreenReview=false) {
            this.offscreenReview=offscreenReview;
            this.verify=verify;
            this.preview=preview;
            parts.Children.Add(body);
            if(classic) Sculpt();
            else {clothGhost=new HatTheatre(true);body.Children.Add(clothGhost.Model);}
            var scene=new Model3DGroup();
            scene.Children.Add(new AmbientLight(Color.FromRgb(103,92,118)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(187,184,196),new Vector3D(.55,-.85,-.72)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(66,59,79),new Vector3D(-.8,.1,-.5)));
            scene.Children.Add(parts);
            CreateGripRig(scene);
            view.Camera=new OrthographicCamera(new Point3D(0,.35,5),new Vector3D(0,0,-1),new Vector3D(0,1,0),2.1);
            view.Children.Add(new ModelVisual3D{Content=scene});
            if(!classic) view.Camera=new OrthographicCamera(new Point3D(0,.89,5),new Vector3D(0,-.06,-1),new Vector3D(0,1,0),2.75);
            soleInsetFraction=SoleInset((OrthographicCamera)view.Camera,classic?boots:clothGhost.Feet);
            window=new Window{Title="Mellow desktop ghost",Width=116,Height=116,WindowStyle=WindowStyle.None,
                AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,
                ShowActivated=false,Topmost=true,ResizeMode=ResizeMode.NoResize,Content=view,
                Focusable=false,WindowStartupLocation=WindowStartupLocation.Manual};
            InitDesktop();
            window.SourceInitialized+=delegate {
                IntPtr h=new WindowInteropHelper(window).Handle;
                const int flags=0x08000000|0x80; // NOACTIVATE and TOOLWINDOW; painted pixels receive clicks.
                SetWindowLong(h,-20,(GetWindowLong(h,-20)|flags)&~0x20);
                passiveStyle=(GetWindowLong(h,-20)&flags)==flags;
            };
            window.Closed+=delegate {timer.Stop();};
            timer.Interval=TimeSpan.FromMilliseconds(40);
            timer.Tick+=delegate {Frame();};
        }
        readonly Random random=new Random();
        void Pose(double now) {
            double dt=Clamp(now-lastTick,0,.08);
            lastTick=now;
            if(state!="idle" && time.Elapsed.TotalSeconds>=until) Signal("idle","",0);
            NoticeActivity(now);
            SenseTerrain(now);
            bool attending=state=="idle" && now<attentiveUntil;
            bool advance=edgeMove!=EdgeMove.None || landing || perch!=IntPtr.Zero || guiding || now<nextDodge || (!view.IsMouseOver && state=="idle" && !attending);
            if(advance) pauseAt=double.NaN;
            else if(double.IsNaN(pauseAt)) pauseAt=onFoot?nextFootfall:walkTime;
            if(!moving) walkTime=advance?walkTime+dt:Math.Min(walkTime+dt,Math.Max(walkTime,pauseAt));
            double stride,weight,direction;
            Navigate(walkTime,out stride,out weight,out direction);
            if(!landing && !onFoot && !guiding && now>=nextDodge && (state!="idle" || attending)) {stride=0;weight=0;}
            TickReply(now);
            ApplyPose(now,dt,attending,stride,weight,direction);
        }
        void ApplyPose(double now,double dt,bool attending,double stride,double weight,double direction) {
            double blend=1-Math.Exp(-dt*6);
            // A fresh typing burst gets one small acknowledgement, then he settles upright.
            double attentionAge=now-attentionStarted;
            double nod=attending && attentionAge<.6?3*Math.Sin(Math.PI*attentionAge/.6):0;
            double targetLean=state=="listen"?-8:state=="think"?10:ClimbingFrame?wallDirection*weight*9:climb?weight*9:nod;
            UpdateGrips(now,dt);
            targetLean=targetLean*(1-gripWeight)+gripWeight*(gripWall*7*(1-overhead)+overhead*(4*Math.Sin(now*3.2)+3*stride+looseArm*gripWall*12));
            lean+=(targetLean-lean)*blend;
            TurnHead(lean,now);
            double targetYaw=state=="privacy"?166:state=="point"?(pointingLeft?-22:22):ClimbingFrame?wallDirection*52:direction*(onFoot?55*Math.Min(1,weight*4):42*weight);
            targetYaw=targetYaw*(1-gripWeight)+gripWeight*gripWall*((clothGhost==null?27:51)*(1-overhead)+6*overhead);
            double pivot=targetYaw-yaw;
            yaw+=pivot*blend;
            if(state=="privacy" || Math.Abs(pivot)>70) stride+=Math.Sin(yaw*Math.PI/36)*Math.Min(1,Math.Abs(pivot)/35);
            double age=Math.Max(0,now-reactionAt);
            double spring=floorSupportReaction(age);
            double hop=rises>=3 && age<.65?Math.Sin(age/.65*Math.PI)*.18:0;
            mouthOpen+=((state=="speak"?.72+.27*Math.Sin(now*13):attending?.52:.38)-mouthOpen)*blend;
            if(clothGhost==null) lip.Transform=new ScaleTransform3D(1,mouthOpen,1,0,.404,.518);
            var turn=new Transform3DGroup();
            turn.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),yaw)));
            turn.Children.Add(new ScaleTransform3D(1+spring*.065,1-spring*.16,1,0,-.34,0));
            turn.Children.Add(new TranslateTransform3D(0,hop+(clothGhost==null && !onFoot?.044*Math.Abs(stride):0),0));
            turn.Children.Add(new TranslateTransform3D(overhead*(.08*Math.Sin(now*3.2)-.14*gripWall*looseArm)*gripWeight,
                -overhead*.07*gripWeight,0));
            parts.Transform=turn;
            for(int i=0;i<2;i++) {
                double step=stride*(2*i-1);
                if(clothGhost==null) feet[i].Transform=new TranslateTransform3D(direction*.11*step,.077*Math.Max(0,step),.04*step);
                var arm=new Transform3DGroup();
                bool active=(state=="point" && pointingLeft)?i==0:i==1;
                double goal=active?(state=="offer"?119:state=="point"?83:state=="think"?48:
                    state=="speak"?31+12*Math.Sin(now*4):0):0;
                if(i==1) goal=Math.Max(goal,144*Math.Max(hangAmount,glideAmount));
                if(state=="privacy")goal=Math.Max(goal,125*Math.Min(1,Math.Abs(yaw)/165));
                if(onFoot && state=="idle") goal+=16*Math.Max(0,-step);
                goal+=Math.Abs(spring)*(quickDrop?65:42);
                if(perch!=IntPtr.Zero && (ledgeLeg==LedgeLeg.Mount || ledgeLeg==LedgeLeg.Catch))
                    goal+=((i==1)==(wallDirection>0)?100:50)*weight;
                else if(ClimbingFrame)goal+=68+45*step;
                else if(climb && weight>.02)goal+=(48+37*step)*weight;
                double angle=i==0?armLeft:armRight;
                angle+=(goal-angle)*blend;
                if(i==0)armLeft=angle;else armRight=angle;
                double side=i*2-1;
                arm.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),side*angle),side*.608,.23,.18));
                arm.Children.Add(new TranslateTransform3D(side*.009*angle/120,.012*Math.Sin(now*2),.06*Math.Abs(step)));
                if(clothGhost==null) arms[i].Transform=arm;
            }
            if(clothGhost==null) Flex(dt,stride);
            double blink=Math.Max(.09,Math.Min(1,Math.Abs(now%4.7-3.9)*8));
            var gaze=new Transform3DGroup();
            gaze.Children.Add(new ScaleTransform3D(1,state=="rest"?.15:blink,1,0,.59,0));
            gaze.Children.Add(new TranslateTransform3D(direction*.022*weight,0,0));
            eyes.Transform=gaze;
            if(clothGhost!=null){
                privacyCover+=((state=="privacy" && yaw>110?1:0)-privacyCover)*blend;
                double elapsed=time.Elapsed.TotalSeconds-trickStart;
                if(gripWeight<.001 && elapsed>=0 && elapsed<15) clothGhost.Animate(elapsed);
                else {
                    pointBlend+=((state=="point"?(pointingLeft?-1:1):0)-pointBlend)*blend;
                    double look=state=="point"?(pointingLeft?-1:1):direction*weight;
                    clothGhost.Animate(0,stride,armLeft,armRight,state=="rest"?.15:blink,glideAmount,privacyCover,onFoot,gripWeight,
                        state=="speak"?mouthOpen:0,look*(1-gripWeight)+gripWall*gripWeight,
                        gripWeight+ (state=="think"?.55:0),spring,now,dt,pointBlend);
                }
            }
            RenderGrips(now);
            if(onFoot && !moving && state!="privacy" && time.Elapsed.TotalSeconds-trickStart>=15)
                PlantFeet(hop);
        }

        void PlantFeet(double hop) {
            // Express the two contacts in the camera plane. Undo the torso's
            // lean, yaw and bounce on the shoes so a planted sole cannot skate.
            var camera=(OrthographicCamera)view.Camera;
            double units=camera.Width/size;
            var up=Vector3D.CrossProduct(Vector3D.CrossProduct(camera.LookDirection,camera.UpDirection),camera.LookDirection);
            up.Normalize();
            var parent=Matrix3D.Identity;
            if(clothGhost!=null) {
                parent.Append(clothGhost.Model.Transform.Value);
                parent.Append(body.Transform.Value);
            }
            parent.Append(parts.Transform.Value);
            parent.Invert();
            var shoes=clothGhost==null?feet:clothGhost.Feet;
            for(int i=0;i<2;i++) {
                if(clothGhost==null) boots[i].Transform=Transform3D.Identity;
                var m=Matrix3D.Identity;
                m.Translate(new Vector3D(footOffset[i]*units,0,0)+up*(hop+(i==swingFoot?swingHeight*units:0)));
                m.Append(parent);
                shoes[i].Transform=new MatrixTransform3D(m);
            }
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
                        
                        dc.DrawRectangle(new VisualBrush(content),null,new Rect(x+35,2,width,height));
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
        void VerifyWalk() {
            var bounds=Bounds();
            double startX=bounds.Left+bounds.Width*.4, distance=Math.Min(size*1.04,bounds.Right-startX);
            if(distance<size*.4) throw new InvalidOperationException("Walk check needs more desktop width.");
            var content=(FrameworkElement)window.Content;
            int width=(int)content.ActualWidth,height=(int)content.ActualHeight;
            bool capture=preview!=null && preview.EndsWith("-walk.gif",StringComparison.OrdinalIgnoreCase);
            var gif=capture?new System.Windows.Media.Imaging.GifBitmapEncoder():null;
            int samples=0;
            double maxGap=0,maxSlide=0;
            guiding=descending=false;state="idle";origin=destination=new Point(startX,bounds.Bottom);
            window.Left=startX;window.Top=bounds.Bottom;
            for(int pass=0;pass<2;pass++) {
                Travel(new Point(startX+(pass==0?distance:0),bounds.Bottom),0,false);
                holdUntil=travel+1;
                int lastFoot=-1;
                double firstContact=0;
                for(int frame=0;frame<=60;frame++) {
                    double clock=travel*frame/60,step,weight,direction;
                    Navigate(clock,out step,out weight,out direction);
                    ApplyPose(clock,travel/60,false,step,weight,direction);
                    var image=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
                    image.Render(content);
                    var data=new byte[width*height*4];
                    image.CopyPixels(data,width*4,0);
                    if(onFoot && swingHeight>size*.026) {
                        int y=height-1,sum=0,count=0;
                        for(;y>=0;y--) {
                            for(int x=0;x<width;x++) if(data[(y*width+x)*4+3]>=96){sum+=x;count++;}
                            if(count>0) break;
                        }
                        if(count==0) throw new InvalidOperationException("Walking avatar disappeared.");
                        var pixel=content.PointToScreen(new Point((double)sum/count,y+1));
                        var sole=PresentationSource.FromVisual(content).CompositionTarget.TransformFromDevice.Transform(pixel);
                        maxGap=Math.Max(maxGap,Math.Abs(sole.Y-floorY));
                        if(lastFoot==swingFoot) {
                            maxSlide=Math.Max(maxSlide,Math.Abs(sole.X-firstContact));
                            samples++;
                        } else firstContact=sole.X;
                        lastFoot=swingFoot;
                    } else lastFoot=-1;
                    if(gif!=null) {
                        int canvasWidth=width+(int)Math.Ceiling(distance)+12,canvasHeight=(int)size+12;
                        var drawing=new DrawingVisual();
                        using(var dc=drawing.RenderOpen()) {
                            dc.DrawRectangle(Brushes.MidnightBlue,null,new Rect(0,0,canvasWidth,canvasHeight));
                            double baseline=canvasHeight-9;
                            dc.DrawLine(new Pen(Brushes.LightSteelBlue,1),new Point(0,baseline+.5),new Point(canvasWidth,baseline+.5));
                            for(int x=8;x<canvasWidth;x+=16)dc.DrawLine(new Pen(Brushes.SlateGray,1),new Point(x,baseline+1),new Point(x,baseline+4));
                            dc.DrawImage(image,new Rect(6+window.Left-startX,baseline-(height-FootInset),width,height));
                        }
                        var scene=new System.Windows.Media.Imaging.RenderTargetBitmap(canvasWidth,canvasHeight,96,96,PixelFormats.Pbgra32);
                        scene.Render(drawing);
                        var meta=new System.Windows.Media.Imaging.BitmapMetadata("gif");
                        meta.SetQuery("/grctlext/Delay",(ushort)Math.Max(2,Math.Round(travel*100/60)));
                        gif.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scene,null,meta,null));
                    }
                }
            }
            if(gif!=null)using(var file=System.IO.File.Create(preview))gif.Save(file);
            Console.WriteLine("Walk: {0} stance comparisons, maximum gap {1:F2} DIP, planted sole drift {2:F2} DIP",samples,maxGap,maxSlide);
            if(samples<12 || maxGap>1.5 || maxSlide>2)
                throw new InvalidOperationException("Rendered feet lost contact during walking.");
        }
        void VerifyStandingPose() {
            // Render the full viewport, preserving the transparent space beneath the soles.
            var content=(FrameworkElement)window.Content;
            int width=(int)content.ActualWidth,height=(int)content.ActualHeight;
            var image=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            image.Render(content);
            var data=new byte[width*height*4];
            image.CopyPixels(data,width*4,0);
            for(int y=height-1;y>=0;y--)
                for(int x=0;x<width;x++)
                    if(data[(y*width+x)*4+3]>=96) {
                        var screen=content.PointToScreen(new Point(0,y+1));
                        var source=PresentationSource.FromVisual(content);
                        double bottom=source.CompositionTarget.TransformFromDevice.Transform(screen).Y;
                        double gap=floorY-bottom;
                        Console.WriteLine("Standing pose: floor gap {0:F2} DIP, yaw {1:F1}, lean {2:F1}",gap,yaw,lean);
                        if(Math.Abs(gap)>1.5 || Math.Abs(yaw)>.01 || Math.Abs(lean)>.01)
                            throw new InvalidOperationException("Standing pose missed the floor or neutral orientation.");
                        return;
                    }
            throw new InvalidOperationException("Standing pose did not render.");
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
            if(verify && frames==12 && preview!=null && preview.EndsWith("-sheet.png")) RenderCharacterSheet(preview);
            else if(verify && frames==12 && !String.IsNullOrEmpty(preview) && preview.EndsWith(".gif",StringComparison.OrdinalIgnoreCase)) {
                if(!preview.EndsWith("-walk.gif",StringComparison.OrdinalIgnoreCase) && !preview.EndsWith("-climb.gif",StringComparison.OrdinalIgnoreCase)) CaptureMotion(preview);
            } else if(verify && frames==12 && !String.IsNullOrEmpty(preview)) {
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(116,116,96,96,PixelFormats.Pbgra32);
                var drawing=new DrawingVisual();
                using(var dc=drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(view),null,new Rect(0,0,116,116));
                bitmap.Render(drawing);
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
                    if(!reply.ToString().Contains("MELLOWOK") || !finished) throw new InvalidOperationException("Chat acceptance failed.");
                    verifyStage=3; chatWindow.Hide(); Signal("offer","Preview: time for a break?",4);
                }
                if(verifyStage==3 || time.Elapsed.TotalSeconds>65) window.Close();
            } else if(verify && time.Elapsed.TotalSeconds>2.6) {
                if(verifyChat!="1" && !moving){
                    VerifyWalk();
                    VerifyPerch();
                    VerifyEdgeGrips();
                    // Exercise an actual vertical route through the same interpolator as the timer.
                    var bounds=Bounds();
                    Travel(new Point(window.Left,bounds.Top),0,true);
                    holdUntil=travel+1;
                    double step,weight,direction;
                    Navigate(travel/2,out step,out weight,out direction);
                    double mid=window.Top;
                    Navigate(travel,out step,out weight,out direction);
                    if(mid<=bounds.Top || Math.Abs(window.Top-bounds.Top)>1)
                        throw new InvalidOperationException("Climb did not reach the upper edge.");
                }
                window.Close();
            }
        }
        public static void Run(bool verify,string preview,System.Threading.EventWaitHandle closeSignal,bool classic) {
            var ghost=new Ghost(verify,preview,classic);
            ghost.closeSignal=closeSignal;
            ghost.foreground=GetForegroundWindow();
            ghost.Pose(0);
            ghost.window.Show();
            if(verify) {
                ghost.window.UpdateLayout();
                // The real taskbar rectangle becomes available once the overlay has a window handle.
                ghost.sampledAt=-1;
                ghost.Pose(0);
                ghost.VerifyStandingPose();
            }
            ghost.time.Start();
            ghost.timer.Start();
            ghost.window.Closed+=delegate {Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);};
            Dispatcher.Run();
            if(verify) {
                Console.WriteLine("Frames={0} passiveWindow={1} focusPreserved={2}",ghost.frames,ghost.passiveStyle,ghost.keptFocus);
                if(!ghost.passiveStyle || (ghost.verifyChat=="1" ? ghost.verifyStage!=3 : !ghost.keptFocus || ghost.frames<12)) throw new InvalidOperationException("Avatar smoke check failed.");
            }
        }
    }
}

