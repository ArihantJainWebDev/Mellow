using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace MellowDesktop {
 public sealed partial class Ghost {
    Grid offscreenHost;
    Viewport3D ReviewViewport(){
        var review=new Viewport3D{Camera=view.Camera};
        review.Children.Add(new ModelVisual3D{Content=((ModelVisual3D)view.Children[0]).Content});
        return review;
    }
    public static void ReviewRoute(string preview,bool classic){
        var ghost=new Ghost(true,preview,classic,true);
        var cameraView=ghost.ReviewViewport();
        cameraView.HorizontalAlignment=HorizontalAlignment.Center;cameraView.VerticalAlignment=VerticalAlignment.Bottom;
        ghost.offscreenHost=new Grid();
        ghost.offscreenHost.Children.Add(cameraView);ghost.placed=true;ghost.floorY=SystemParameters.PrimaryScreenHeight;
        ghost.VerifyEdgeGrips();
    }
    // Use the production camera, geometry, animation and route planner in an
    // unshown visual tree. No tray, input sampling, chat or live window fixture.
    public static void PoseReview(string path,bool classic) {
        if(String.IsNullOrEmpty(path))throw new ArgumentException("Supply a PNG path with -Preview.");
        var ghost=new Ghost(true,null,classic,true);

        ghost.DrawPoseReview(path);
        ghost.CheckGestureTransitions();
    }
    void DrawPoseReview(string path) {
        var review=ReviewViewport();
        string[] names={"At rest","Walking","Pointing","Speaking","Side grip","One-hand hang","Umbrella","Landing"};
        const int tileWidth=174,tileHeight=184;
        var panel=new DrawingVisual();int samples=0,grips=0;
        const int footerHeight=192;
        using(var dc=panel.RenderOpen()){
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28,30,39)),null,new Rect(0,0,tileWidth*4,tileHeight*2+footerHeight));
            foreach(int scale in new[]{82,116,156}){
                size=scale;LayoutAvatar();
                review.Measure(new Size(size,size));review.Arrange(new Rect(0,0,size,size));review.UpdateLayout();
                for(int pose=0;pose<names.Length;pose++){
                    double now=pose*2,weight=0,dir=1,step=0;
                    edgeMove=EdgeMove.None;gripWeight=overhead=looseArm=hangAmount=glideAmount=0;
                    armLeft=armRight=lean=yaw=0;onFoot=false;climb=false;reactionAt=-10;reaction=0;
                    window.Left=30;window.Top=60;floorY=500;placed=true;holdUntil=double.PositiveInfinity;
                    state=pose==2?"point":pose==3?"speak":"idle";pointingLeft=false;
                    if(pose==1){
                        stepCount=6;onFoot=true;step=SetFootprints(.392,size*.84);weight=.85;
                    }
                    if(pose==4 || pose==5){
                        gripWall=1;StartEdgeLeg(EdgeMove.Ascend,now);
                        // Use an actual vertical route from the side, then sample its transfer.
                        window.Left=destination.X;origin.X=destination.X;
                        walkTime=pose==4?departure+travel*.63:departure+travel+1.65;
                        Navigate(walkTime,out step,out weight,out dir);
                    }
                    if(pose==6)glideAmount=1;
                    for(int frame=0;frame<17;frame++){
                        double clock=now+frame*.04;
                        if(pose==7 && frame==12){reactionAt=clock;reaction=1;}
                        ApplyPose(clock,.04,false,step,weight,dir);
                        if(clothGhost!=null)clothGhost.VerifySleeves();
                        samples++;
                    }
                    var bitmap=new RenderTargetBitmap(scale,scale,96,96,PixelFormats.Pbgra32);bitmap.Render(review);
                    var pixels=new byte[scale*scale*4];bitmap.CopyPixels(pixels,scale*4,0);
                    int visible=0;for(int k=3;k<pixels.Length;k+=4)if(pixels[k]>=96)visible++;
                    if(visible<scale*scale*.08)throw new InvalidOperationException("Empty or clipped pose: "+names[pose]+" ("+visible+" pixels)");
                    if(pose==4 || pose==5){
                        var marked=MarkedGripFrame(delegate {
                            var mask=new RenderTargetBitmap(scale,scale,96,96,PixelFormats.Pbgra32);mask.Render(review);return mask;
                        });
                        marked.CopyPixels(pixels,scale*4,0);
                        for(int hand=0;hand<2;hand++)if(planted[hand]){
                            double gap=double.PositiveInfinity;
                            for(int py=0;py<scale;py++)for(int px=0;px<scale;px++){
                                int k=4*(py*scale+px);
                                if(pixels[k+3]<96 || pixels[k]>60 ||
                                    (hand==0?pixels[k+1]<160 || pixels[k+2]>70:pixels[k+2]<160 || pixels[k+1]>70))continue;
                                gap=Math.Min(gap,(new Point(px+.5,py+.5)-palms[hand]).Length);
                            }
                            if(gap>2.5)throw new InvalidOperationException("Painted grip missed its target: "+gap);
                            grips++;
                        }
                    }
                    if(scale!=156)continue;
                    int x=pose%4*tileWidth,y=pose/4*tileHeight;
                    var rect=new Rect(x+9,y+3,scale,scale);
                    double floor=rect.Top+size-FootInset;
                    var rule=new Pen(new SolidColorBrush(Color.FromRgb(103,102,122)),1);
                    if(pose<4 || pose==7)dc.DrawLine(rule,new Point(x+11,floor+.5),new Point(x+tileWidth-11,floor+.5));
                    var clip=rect;
                    if(pose==4 || pose==5){
                        var at=ViewOrigin();
                        double edgeX=Math.Min(rect.Right,rect.Left+SystemParameters.PrimaryScreenWidth-at.X);
                        double edgeY=Math.Max(rect.Top,rect.Top-at.Y);
                        clip=new Rect(rect.Left,edgeY,edgeX-rect.Left,rect.Bottom-edgeY);
                        dc.DrawLine(rule,new Point(edgeX+.5,edgeY),new Point(edgeX+.5,rect.Bottom));
                        if(pose==5)dc.DrawLine(rule,new Point(rect.Left,edgeY-.5),new Point(edgeX,edgeY-.5));
                    }
                    dc.PushClip(new RectangleGeometry(clip));dc.DrawImage(bitmap,rect);dc.Pop();
                    var label=new FormattedText(names[pose],CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),13,Brushes.Gainsboro,1.0);
                    dc.DrawText(label,new Point(x+(tileWidth-label.Width)/2,y+158));
                }
            }
            DrawCaptionReview(dc,review,tileWidth*4,tileHeight*2,footerHeight);
        }
        var output=new RenderTargetBitmap(tileWidth*4,tileHeight*2+footerHeight,96,96,PixelFormats.Pbgra32);output.Render(panel);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(output));using(var file=File.Create(path))png.Save(file);
        if(grips<6)throw new InvalidOperationException("Missing rendered grip samples.");
        Console.WriteLine("{0} pose frames, {1} painted grip samples and two caption layouts checked offscreen.",samples,grips);
    }
    void CheckGestureTransitions(){
        // Exercise the state boundaries as well as the single frames on the sheet.
        if(clothGhost==null)return;
        edgeMove=EdgeMove.None;onFoot=false;
        for(int frame=0;frame<120;frame++){
            state=frame<24?"privacy":frame<52?"point":frame<83?"speak":"idle";
            pointingLeft=true;glideAmount=frame<90?0:Math.Sin(Math.PI*(frame-90)/29);
            ApplyPose(9+frame*.04,.04,false,0,0,-1);clothGhost.VerifySleeves();
        }
        for(int frame=0;frame<=150;frame++){
            clothGhost.Animate(frame*.1);clothGhost.VerifySleeves();
        }
        Console.WriteLine("Privacy, left-point, speech, release and the complete 15-second prop routine checked.");
    }
    void DrawCaptionReview(DrawingContext dc,Viewport3D review,int width,int y,int height){
        ((Grid)window.Content).Children.Remove(bubbleStack);
        var card=new Grid();card.Children.Add(review);card.Children.Add(bubbleStack);
        size=116;LayoutAvatar();review.Width=size;review.Height=size;
        review.VerticalAlignment=VerticalAlignment.Bottom;review.HorizontalAlignment=HorizontalAlignment.Center;
        edgeMove=EdgeMove.None;gripWeight=overhead=glideAmount=0;
        for(int i=0;i<2;i++){
            card.Background=new SolidColorBrush(i==0?Color.FromRgb(50,44,63):Color.FromRgb(224,216,226));
            Signal(i==0?"think":"speak",i==0?"Checking the selected line…":"Here’s the missing file.",3);
            bubbleStack.BeginAnimation(UIElement.OpacityProperty,null);
            for(int k=0;k<14;k++)ApplyPose(k*.04,.04,false,0,0,1);
            card.Measure(new Size(width/2,height));card.Arrange(new Rect(0,0,width/2,height));card.UpdateLayout();
            var sample=new RenderTargetBitmap(width/2,height,96,96,PixelFormats.Pbgra32);sample.Render(card);
            if(bubbleStack.ActualHeight<=10 || bubbleStack.ActualHeight>height-size*.87)
                throw new InvalidOperationException("Caption did not fit.");
            dc.DrawImage(sample,new Rect(i*width/2,y,width/2,height));
        }
    }
 }
}
