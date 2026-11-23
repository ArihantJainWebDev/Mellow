using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MellowDesktop {
 public sealed partial class Ghost {
    enum LedgeLeg { ToFloor, Approach, Catch, Climb, Mount, Cross, Rest }
    LedgeLeg ledgeLeg;
    bool leftEdge, landing;
    double wallDirection;
    readonly uint avatarPid=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
    IntPtr perchForeground;
    uint perchPid;
    bool ClimbingFrame {get{return perch!=IntPtr.Zero && ledgeLeg==LedgeLeg.Climb;}}

    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]
    static extern int FrameBounds(IntPtr hwnd,int attribute,out NativeRect value,int bytes);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]
    static extern int CloakState(IntPtr hwnd,int attribute,out int value,int bytes);

    Rect VisibleFrame(IntPtr handle) {
        Rect r=RectFor(handle);
        if(r.IsEmpty) return r;
        int cloaked;
        if(CloakState(handle,14,out cloaked,4)==0 && cloaked!=0) return Rect.Empty;
        NativeRect frame;
        if(FrameBounds(handle,9,out frame,16)==0) {
            var transform=PresentationSource.FromVisual(window).CompositionTarget.TransformFromDevice;
            r=new Rect(transform.Transform(new Point(frame.left,frame.top)),transform.Transform(new Point(frame.right,frame.bottom)));
        }
        return r;
    }
    bool HasLedge(Rect r) {
        return !r.IsEmpty && r.Width>size*.55 && r.Height>4 &&
            r.Top-window.Height+FootInset>=Bounds().Top && r.Top<floorY &&
            r.Right>size*.3 && r.Left<SystemParameters.PrimaryScreenWidth-size*.3;
    }
    double LedgeX(Rect r) {
        double mid=(Math.Max(0,r.Left)+Math.Min(SystemParameters.PrimaryScreenWidth,r.Right))*.5;
        return mid-window.Width*.5;
    }
    double WallX(Rect r) {
        double edge=leftEdge?r.Left:r.Right;
        // Leave room for the sleeve on the frame; climb inside a screen-flush frame.
        double reach=size*.22, width=SystemParameters.PrimaryScreenWidth;
        double centre=edge+(leftEdge?-reach:reach);
        if(centre<reach || centre>width-reach) centre=edge+(leftEdge?reach:-reach);
        wallDirection=centre<edge?1:-1;
        return centre-window.Width*.5;
    }
    bool StartPerch(IntPtr handle) {
        Rect r=VisibleFrame(handle);
        if(!HasLedge(r)){Signal("offer","Choose a visible window with room above its title bar.",3);return false;}
        CancelTarget();LeavePerch();
        perch=handle;GetWindowThreadProcessId(handle,out perchPid);
        perchForeground=GetForegroundWindow();previousLedge=r;
        double center=window.Left+window.Width*.5;
        bool leftVisible=r.Left>=0, rightVisible=r.Right<=SystemParameters.PrimaryScreenWidth;
        leftEdge=leftVisible && (!rightVisible || Math.Abs(center-r.Left)<=Math.Abs(center-r.Right));
        if(!leftVisible && !rightVisible){DropFromPerch();Signal("offer","Neither side of that window is visible.",3);return false;}
        ledgeLeg=window.Top<Bounds().Bottom-2?LedgeLeg.ToFloor:LedgeLeg.Approach;
        guiding=true;
        SetLedgeLeg(r);
        Signal("guide","",1);
        return true;
    }
    Point LegEnd(Rect r) {
        double side=WallX(r), top=r.Top-window.Height+FootInset;
        switch(ledgeLeg) {
            case LedgeLeg.ToFloor:return new Point(window.Left,Bounds().Bottom);
            case LedgeLeg.Approach:return new Point(side,Bounds().Bottom);
            case LedgeLeg.Catch:return new Point(side,Math.Min(Bounds().Bottom,r.Bottom-window.Height+FootInset+size*.28));
            case LedgeLeg.Climb:return new Point(side,Math.Min(Bounds().Bottom,top+size*.32));
            case LedgeLeg.Mount:return new Point((leftEdge?r.Left+size*.26:r.Right-size*.26)-window.Width*.5,top);
            default:return new Point(LedgeX(r),top);
        }
    }
    void SetLedgeLeg(Rect r) {
        Point end=LegEnd(r);
        var bounds=Bounds();
        end=new Point(Clamp(end.X,bounds.Left,bounds.Right),Clamp(end.Y,bounds.Top,bounds.Bottom));
        Travel(end,walkTime,ClimbingFrame);
        holdUntil=double.PositiveInfinity;
        if(ledgeLeg==LedgeLeg.Catch) travel=Clamp(Math.Abs(destination.Y-origin.Y)/420,.24,.9);
        if(ledgeLeg==LedgeLeg.Mount) travel=.55;
        if((destination-origin).Length<1) travel=0;
    }
    bool SupportExposed(Rect r,IntPtr fg) {
        uint pid;GetWindowThreadProcessId(fg,out pid);
        if(fg!=IntPtr.Zero && pid!=avatarPid) {
            if(fg!=perchForeground && fg!=perch)return false;
            perchForeground=fg;
        }
        double x=leftEdge?r.Left+12:r.Right-12;
        if(ledgeLeg==LedgeLeg.Rest) x=LedgeX(r)+window.Width*.5;
        if(ledgeLeg==LedgeLeg.Cross) x=window.Left+window.Width*.5+r.Left-previousLedge.Left;
        var pixels=PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;
        IntPtr overlay=new WindowInteropHelper(window).Handle;
        // If a sleeve covers the probe, sample just below or beside it as well.
        for(int i=0;i<3;i++){
            var p=new Point(Clamp(x+(i==1?-10:i==2?10:0),r.Left+2,r.Right-2),
                r.Top+Math.Min(i==0?6:16,r.Height/2));
            var px=pixels.Transform(p);
            var hit=GetAncestor(WindowFromPoint(new System.Drawing.Point((int)Math.Round(px.X),(int)Math.Round(px.Y))),2);
            if(hit!=overlay) return hit==perch;
        }
        return true;
    }
    void UpdatePerch(double now,IntPtr foregroundWindow) {
        Rect r=VisibleFrame(perch);
        uint pid;GetWindowThreadProcessId(perch,out pid);
        if(pid!=perchPid || !HasLedge(r) || !SupportExposed(r,foregroundWindow)){DropFromPerch();return;}
        Point end=LegEnd(r);
        var b=Bounds();end.X=Clamp(end.X,b.Left,b.Right);end.Y=Clamp(end.Y,b.Top,b.Bottom);
        if(ledgeLeg==LedgeLeg.Rest) {
            if((r.Location-previousLedge.Location).Length>20){reactionAt=now;reaction=1;quickDrop=false;}
            origin=destination=end;travel=0;
        } else {
            if(ledgeLeg>=LedgeLeg.Climb) {
                var delta=r.Location-previousLedge.Location;
                origin+=delta;destination+=delta;window.Left+=delta.X;window.Top+=delta.Y;
            }
            if((end-destination).Length>2) SetLedgeLeg(r);
            if(walkTime>=departure+travel) {
                window.Left=destination.X;window.Top=destination.Y;
                ledgeLeg++;
                if(ledgeLeg==LedgeLeg.Rest) {
                    guiding=false;origin=destination=end;travel=0;Signal("rest","",2);
                } else SetLedgeLeg(r);
            }
        }
        previousLedge=r;
    }
    void DropFromPerch() {
        LeavePerch();CancelTarget();descending=true;pathStage=0;
        Travel(new Point(window.Left,Bounds().Bottom),walkTime,false,true);
        holdUntil=double.PositiveInfinity;
        Signal("idle","",0);
    }
    void Land(double clock) {
        landing=descending=false;climb=false;travel=0;
        origin=destination=new Point(window.Left,Bounds().Bottom);
        window.Top=destination.Y;holdUntil=clock+2;reactionAt=lastTick;reaction=.65;quickDrop=false;
    }
 }
}
