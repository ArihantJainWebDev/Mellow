using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace MellowDesktop {
 public sealed partial class Ghost {
    RenderTargetBitmap GripFrame(){
        var content=(FrameworkElement)window.Content;
        var image=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);
        image.Render(content);return image;
    }
    // Identify each sleeve in the real composited render, including occlusion,
    // viewport clipping, window padding and the host's screen coordinate transform.
    double PaintedGripGap(double boundary,bool ceiling,int hand,byte[] pixels,int width,int height) {
        var content=(FrameworkElement)window.Content;
        var toDip=PresentationSource.FromVisual(content).CompositionTarget.TransformFromDevice;
        Point originOnScreen=toDip.Transform(content.PointToScreen(new Point()));
        Point expected=ViewOrigin()+ (Vector)palms[hand];
        double gap=double.PositiveInfinity;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++){
            int k=4*(y*width+x);
            if(pixels[k+3]<96 || pixels[k]>60)continue;
            if(hand==0 ? pixels[k+1]<160 || pixels[k+2]>70 : pixels[k+2]<160 || pixels[k+1]>70)continue;
            double sx=originOnScreen.X+x+.5,sy=originOnScreen.Y+y+.5;
            if(Math.Abs(sx-expected.X)>size*.09 || Math.Abs(sy-expected.Y)>size*.09)continue;
            if(sx<0 || sx>SystemParameters.PrimaryScreenWidth || sy<0)continue;
            gap=Math.Min(gap,Math.Abs((ceiling?sy:sx)-boundary));
        }
        return gap;
    }
    RenderTargetBitmap MarkedGripFrame() {
        var pieces=new GeometryModel3D[6];
        var colors=new Material[6];
        try {
            for(int i=0;i<2;i++){
                Brush color=i==0?Brushes.Lime:Brushes.Red;
                var marker=new MaterialGroup();
                marker.Children.Add(new DiffuseMaterial(color));marker.Children.Add(new EmissiveMaterial(color));
                for(int j=0;j<3;j++){
                    int k=3*i+j;
                    var piece=(GeometryModel3D)(j==0?gripRig.Children[i]:mitts[i].Children[j-1]);
                    pieces[k]=piece;colors[k]=piece.Material;piece.Material=piece.BackMaterial=marker;
                }
            }
            return GripFrame();
        } finally {
            for(int k=0;k<pieces.Length;k++)if(pieces[k]!=null)pieces[k].Material=pieces[k].BackMaterial=colors[k];
        }
    }
    void VerifyFrameGrip(double clock,double step,double weight,double direction) {
        // Allow the reach to settle, then inspect a real painted hold on the fixture frame.
        for(int j=0;j<5;j++)ApplyPose(clock,.08,false,step,weight,direction);
        var frame=MarkedGripFrame();
        var pixels=new byte[frame.PixelWidth*frame.PixelHeight*4];
        frame.CopyPixels(pixels,frame.PixelWidth*4,0);
        double wall=leftEdge?previousLedge.Left:previousLedge.Right;
        bool held=false;
        for(int i=0;i<2;i++)if(planted[i])
            held|=PaintedGripGap(wall,false,i,pixels,frame.PixelWidth,frame.PixelHeight)<=2.5;
        if(!held)throw new InvalidOperationException("Climbing sleeve missed the selected window frame.");
    }
    void AddClimbFrame(GifBitmapEncoder gif){
        var image=GripFrame();int width=368,height=224;
        var content=(FrameworkElement)window.Content;
        var transform=PresentationSource.FromVisual(content).CompositionTarget.TransformFromDevice;
        Point at=transform.Transform(content.PointToScreen(new Point()));
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()){
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(25,29,39)),null,new Rect(0,0,width,height));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(62,68,83)),null,new Rect(0,0,7,height));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(62,68,83)),null,new Rect(0,0,width,7));
            dc.PushClip(new RectangleGeometry(new Rect(7,7,width-7,height-7)));
            dc.DrawImage(image,new Rect(7+at.X,7+at.Y,image.Width,image.Height));
            dc.Pop();
        }
        var scene=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);scene.Render(visual);
        var meta=new BitmapMetadata("gif");meta.SetQuery("/grctlext/Delay",(ushort)8);
        gif.Frames.Add(BitmapFrame.Create(scene,null,meta,null));
    }
    void VerifyEdgeGrips(){
        double initialSize=size,maxGap=0;
        int contacts=0,handoffs=0;
        double maxSlip=0;
        bool record=preview!=null && preview.EndsWith("-climb.gif",StringComparison.OrdinalIgnoreCase);
        var gif=record?new GifBitmapEncoder():null;

        foreach(int scale in new[]{82,116,156})for(int edge=0;edge<2;edge++){
            size=scale;LayoutAvatar();window.UpdateLayout();
            edgeMove=EdgeMove.None;gripWeight=overhead=0;landing=descending=moving=false;
            var b=Bounds();window.Left=edge==0?b.Left+8:b.Right-8;window.Top=b.Bottom;
            RequestEdgeClimb();
            double s,w,d;
            walkTime=departure+travel;Navigate(walkTime,out s,out w,out d);
            walkTime=holdUntil+.001;Navigate(walkTime,out s,out w,out d);
            if(edgeMove!=EdgeMove.Ascend)throw new InvalidOperationException("Climb command missed the side.");
            // Sample the catch, then the last body length, transfer, traverse and release.
            bool vertical=false,top=false,across=false,drop=false;
            double testTime=walkTime;
            var lastAnchor=new Point[2];var locked=new bool[2];var lastAxis=new bool[2];
            for(int frame=0;frame<320;frame++){
                if(frame==12)testTime=Math.Max(testTime,departure+travel-2.6);
                walkTime=testTime;Navigate(walkTime,out s,out w,out d);ApplyPose(testTime,.08,false,s,w,d);
                bool supported=edgeMove==EdgeMove.Ascend || edgeMove==EdgeMove.Traverse;
                if(supported && gripWeight>.99){
                    int mask=(planted[0]?1:0)+(planted[1]?2:0);
                    if(mask==0)throw new InvalidOperationException(String.Format("Both grips released: {0}, wall={1}, time={2:F3}/{3:F3}, top={4}, rim={5:F3}/{6:F3}",edgeMove,gripWall,walkTime-departure,travel,window.Top,rimBlend[0],rimBlend[1]));
                    var image=MarkedGripFrame();int width=image.PixelWidth,height=image.PixelHeight;
                    var data=new byte[width*height*4];image.CopyPixels(data,width*4,0);
                    for(int i=0;i<2;i++){
                        if(!planted[i]){locked[i]=false;continue;}
                        bool ceiling=ViewOrigin().Y+palms[i].Y<2;
                        double gap=PaintedGripGap(ceiling || edge==0?0:SystemParameters.PrimaryScreenWidth,ceiling,i,data,width,height);

                        if(gap>2.5)throw new InvalidOperationException(String.Format(
                            "Visible hand missed {0} edge: size={1}, hand={2}, stage={3}, gap={4}, contact={5}, viewport={6}",
                            ceiling?"top":"side",size,i,edgeMove,gap,palms[i],ViewOrigin()));
                        Point anchor=ViewOrigin()+(Vector)palms[i];
                        if(locked[i] && lastAxis[i]==ceiling && frame!=12) {
                            double slip=(anchor-lastAnchor[i]).Length;
                            maxSlip=Math.Max(maxSlip,slip);
                            if(slip>2)throw new InvalidOperationException("Loaded hand slid between frames: "+slip);
                        }
                        lastAnchor[i]=anchor;lastAxis[i]=ceiling;locked[i]=true;
                        contacts++;maxGap=Math.Max(maxGap,gap);
                        if(ceiling)top=true;else vertical=true;
                        if(ceiling && edgeMove==EdgeMove.Traverse && !onFoot){across=true;handoffs|=1<<i;}
                    }
                }
                if(gif!=null && scale==116 && edge==0 && ViewOrigin().Y<205 && frame>=12) AddClimbFrame(gif);
                if(landing)drop=true;
                if(drop && !landing)break;
                testTime+=.08;
            }
            Console.WriteLine("Tested {0}-DIP avatar on {1} edge",scale,edge==0?"left":"right");
            if(!vertical || !top || !across || !drop || landing || Math.Abs(window.Top-Bounds().Bottom)>1.5)
                throw new InvalidOperationException("Incomplete screen-edge route.");
        }
        if(handoffs!=3)throw new InvalidOperationException("Overhead route did not load both hands.");
        if(gif!=null)using(var file=File.Create(preview))gif.Save(file);
        Console.WriteLine("Rendered hand contacts={0}, maximum visible edge gap={1:F2} DIP, maximum planted anchor drift={2:F2} DIP (left, right, ceiling; 3 sizes)",contacts,maxGap,maxSlip);
        size=initialSize;LayoutAvatar();window.UpdateLayout();edgeMove=EdgeMove.None;
    }
 }
}
