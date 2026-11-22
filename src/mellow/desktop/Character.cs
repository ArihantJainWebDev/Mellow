using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using System.IO;

namespace MellowDesktop {
    public sealed partial class Ghost {
        readonly Model3DGroup crown=new Model3DGroup();
        readonly Model3DGroup[] wrists={new Model3DGroup(),new Model3DGroup()};
        readonly Model3DGroup[] boots={new Model3DGroup(),new Model3DGroup()};
        double tuftLag;
        // One continuous shell from crown to tucked-under hem. Cubic tangents
        // follow the sculpted profile without a joint at the head/skirt boundary.
        static readonly Point[] profile={
            new Point(0,1.14),new Point(.29,1.08),new Point(.49,.90),
            new Point(.575,.66),new Point(.60,.42),new Point(.655,.16),
            new Point(.716,-.065),new Point(.715,-.17),new Point(.65,-.225),
            new Point(.50,-.17),new Point(.25,-.135),new Point(0,-.13)};
        static Point Profile(double h){
            double s=h*(profile.Length-1);
            int i=Math.Min((int)s,profile.Length-2);double t=s-i,t2=t*t,t3=t2*t;
            Point a=profile[i],b=profile[i+1];
            Vector m=(b-profile[Math.Max(0,i-1)])*.5,n=(profile[Math.Min(profile.Length-1,i+2)]-a)*.5;
            return a+(b-a)*(3*t2-2*t3)+m*(t3-2*t2+t)+n*(t3-t2);
        }
        void Sculpt(){
            Mesh(body,"#E5DBF0",delegate(double a,double b){
                double h=a/Pi;Point p=Profile(h);
                double hem=Math.Exp(-Math.Pow((h-.72)/.15,2));
                double fold=Math.Cos(5*b+.35)+.17*Math.Cos(3*b-.6);
                double r=p.X*(1+.031*fold*hem);
                return new Point3D(r*Math.Cos(b),p.Y-.074*fold*hem,.82*r*Math.Sin(b));
            });
            body.Children.Add(crown);
            for(int strand=0;strand<2;strand++){
                int layer=strand;
                Mesh(crown,layer==0?"#E2D6EF":"#D7C7E8",delegate(double a,double b){
                    double t=(1-Math.Cos(a))/2,r=Math.Sin(a)*(1-.52*t);
                    return new Point3D(-.23+.95*t-.075*layer,
                        1.005+.095*Math.Sin(2*Pi*t)+.122*t*t+.148*r*Math.Cos(b)-.034*layer,
                        -.045-.12*layer+.147*r*Math.Sin(b));
                });
            }
            lip=Mesh(body,"#65415A",delegate(double a,double b){
                double t=a/Pi, radius=.038*Math.Sin(a);
                return new Point3D((t-.5)*.147,.399+.027*Math.Pow(2*t-1,2)+radius*Math.Cos(b),
                    .51+.67*radius*Math.Sin(b));
            });
            body.Children.Add(eyes);
            for(int i=0;i<2;i++){
                int side=i*2-1;double x=side*.204;
                Oval(eyes,"#30243B",x,.592,.448,.094,.139,.064);
                Oval(eyes,"#FFF4FF",x-.023,.640,.501,.023,.030,.011);
                Oval(eyes,"#B5A1CC",x+.024,.552,.503,.011,.013,.007);
                Mesh(body,"#69526D",delegate(double a,double b){
                    double t=a/Pi, thickness=.015*Math.Sin(a);
                    return new Point3D(x+(t-.5)*.155,.772+.033*Math.Sin(a)-side*.016*(t-.5)+thickness*Math.Cos(b),
                        .397-side*(t-.5)*.046+thickness*Math.Sin(b));
                });
                var shoulder=new Model3DGroup();arms[i]=shoulder;body.Children.Add(shoulder);
                Oval(shoulder,"#DCCEEC",side*.681,.166,.16,.112,.18,.115);
                shoulder.Children.Add(wrists[i]);
                Oval(wrists[i],"#E0D1EC",side*.724,.035,.23,.12,.148,.125);
                Oval(wrists[i],"#E8DAF1",side*.642,.058,.31,.065,.085,.064);
                feet[i]=new Model3DGroup();parts.Children.Add(feet[i]);feet[i].Children.Add(boots[i]);
                Oval(boots[i],"#CDBBD9",side*.251,-.24,.045,.106,.115,.103);
                Oval(boots[i],"#D6C6E1",side*.252,-.334,.132,.154,.103,.205);

            }
        }
        void Flex(double dt,double step){
            tuftLag+=(lean-tuftLag)*(1-Math.Exp(-dt*3));
            crown.Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),
                .46*(tuftLag-lean)+2.8*step),-.18,1.02,0);
            for(int i=0;i<2;i++){
                double side=i*2-1;
                double angle=i==0?armLeft:armRight;
                wrists[i].Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,0),
                    -angle*.32+step*side*10),side*.70,.13,.20);
                boots[i].Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,0),
                    side*step*18),side*.25,-.25,.06);
            }
        }
        void RenderCharacterSheet(string path){
            const int cell=360;
            var sheet=new DrawingVisual();
            using(var dc=sheet.RenderOpen()){
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(46,44,59)),null,new Rect(0,0,cell*3,cell));
                for(int i=0;i<3;i++){
                    state=i==0?"idle":i==1?"offer":"privacy";
                    for(int f=0;f<36;f++){until=1000;Pose(lastTick+.04);}
                    var materialView=new Viewport3D();
                    materialView.Camera=new OrthographicCamera(new Point3D(0,.36,5),new Vector3D(0,0,-1),new Vector3D(0,1,0),2.36);
                    materialView.Children.Add(new ModelVisual3D{Content=((ModelVisual3D)view.Children[0]).Content});
                    materialView.Measure(new Size(cell,cell));materialView.Arrange(new Rect(0,0,cell,cell));materialView.UpdateLayout();
                    var tile=new RenderTargetBitmap(cell,cell,96,96,PixelFormats.Pbgra32);
                    tile.Render(materialView);
                    dc.DrawImage(tile,new Rect(i*cell,0,cell,cell));
                }
            }
            var result=new RenderTargetBitmap(cell*3,cell,96,96,PixelFormats.Pbgra32);result.Render(sheet);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(result));
            using(var stream=File.Create(path))encoder.Save(stream);
        }
    }
}