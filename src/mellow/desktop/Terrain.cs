using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace MellowDesktop {
 public sealed partial class Ghost {
    [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int left,top,right,bottom;}
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out NativeRect rect);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string name,string title);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(System.Drawing.Point p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flag);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
    IntPtr perch, closeHandle;
    uint closePid;
    AutomationElement target;
    Point targetCenter;
    string pointCaption="Here";
    double sampledAt=-1, reactionAt=-10, reaction,glideAmount,hangAmount;
    double floorY=double.NaN,perchOffset;
    Rect previousLedge;
    int rises;
    double floorSupportReaction(double age){return reaction*Math.Exp(-age*4)*Math.Sin(age*15);}
    bool quickDrop, descending;
    double lastRise=-10, nextDodge;
    double FootInset {get{return size*soleInsetFraction;}}
    Rect RectFor(IntPtr h){
        NativeRect r;
        var src=PresentationSource.FromVisual(window);
        if(h==IntPtr.Zero||src==null||!IsWindowVisible(h)||IsIconic(h)||!GetWindowRect(h,out r))return Rect.Empty;
        var m=src.CompositionTarget.TransformFromDevice;
        return new Rect(m.Transform(new Point(r.left,r.top)),m.Transform(new Point(r.right,r.bottom)));
    }
    void SenseTerrain(double now){
        if(now-sampledAt<.10)return;
        sampledAt=now;
        double bottom=SystemParameters.PrimaryScreenHeight;
        Rect bar=RectFor(FindWindow("Shell_TrayWnd",null));
        double y=(!bar.IsEmpty && bar.Width>bar.Height && bar.Top>bottom*.55 && bottom-bar.Top>3)?Math.Min(bottom,bar.Top):bottom;
        if(!double.IsNaN(floorY) && Math.Abs(y-floorY)>.4){
            bool grounded=placed && !moving && !guiding && perch==IntPtr.Zero &&
                Math.Abs(window.Top+window.Height-FootInset-floorY)<8;
            if(grounded){
                if(now-reactionAt>.35){
                    quickDrop=y>floorY && now-lastRise<1.4;
                    if(y<floorY){rises++;lastRise=now;}
                    if(y<floorY && rises<=6 && !verify)using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Prefs))key.SetValue("TaskbarRises",rises);
                    reactionAt=now;reaction=quickDrop?1.25:rises>=3?.75:1;
                }
                double delta=y-floorY;origin.Y+=delta;destination.Y+=delta;
            }
        }
        floorY=y;
        if(perch!=IntPtr.Zero){
            Rect r=RectFor(perch);
            if(r.IsEmpty || r.Top<window.Height-FootInset || !window.IsVisible){LeavePerch();return;}
            Point pos=new Point(Clamp(r.Left+perchOffset,0,SystemParameters.PrimaryScreenWidth-window.Width),r.Top-window.Height+FootInset);
            if(!previousLedge.IsEmpty && !guiding && (r.Location-previousLedge.Location).Length>20){
                reactionAt=now;reaction=1;quickDrop=false;
            }
            if(!guiding){origin=destination=pos;travel=0;holdUntil=walkTime+3;}
            else if((pos-destination).Length>5){Travel(pos,walkTime,false);holdUntil=walkTime+travel+.2;}
            if(guiding && walkTime>=departure+travel){guiding=false;Signal("rest","",2);}
            previousLedge=r;
        }
        if(target!=null) RefreshTarget();
        UpdateProgress(now);
        if(!moving && !guiding && now>=nextDodge && state!="privacy" &&
           Forms.Control.MouseButtons==Forms.MouseButtons.Left && view.IsMouseOver){
            nextDodge=now+2;LeavePerch();
            var b=Bounds();double side=window.Left<b.Left+b.Width/2?1:-1;
            Travel(new Point(window.Left+side*size,window.Top),walkTime,false);
            holdUntil=walkTime+travel+2;Signal("idle","",0);
        }
    }
    AutomationElement progressElement;
    double progressStarted, lastProgress, nextProgressCheck;
    bool dismissed, progressDone;
    void WatchProgress(){
        try {
            var p=Forms.Cursor.Position;
            var element=AutomationElement.FromPoint(new Point(p.X,p.Y));
            for(int i=0;element!=null && i<6;i++,element=TreeWalker.ControlViewWalker.GetParent(element)){
                if(element.Current.ControlType!=ControlType.ProgressBar)continue;
                object pattern;
                if(!element.TryGetCurrentPattern(RangeValuePattern.Pattern,out pattern))break;
                progressElement=element;progressStarted=time.Elapsed.TotalSeconds;lastProgress=-1;
                progressDone=false;dismissed=false;Signal("listen","Watching this progress bar",2);return;
            }
        }catch(ElementNotAvailableException){}catch(COMException){}
        Signal("offer","Choose a progress bar that exposes its value.",3);
    }
    void UpdateProgress(double now){
        if(progressElement==null || now<nextProgressCheck)return;
        nextProgressCheck=now+.6;
        try {
            if(progressElement.Current.IsOffscreen)throw new InvalidOperationException();
            var range=((RangeValuePattern)progressElement.GetCurrentPattern(RangeValuePattern.Pattern)).Current;
            if(range.Maximum<=range.Minimum)throw new InvalidOperationException();
            progressDone=range.Value>=range.Maximum;
            if(progressDone){
                if(!dismissed && now>attentiveUntil && state=="idle"){
                    Signal("offer","Progress reached 100%. Check the result?",5);progressElement=null;
                }
            }else if(range.Value!=lastProgress){lastProgress=range.Value;progressStarted=now;}
            else if(now-progressStarted>8 && state=="idle")Signal("rest","",2);
        }catch(ElementNotAvailableException){progressElement=null;}catch(InvalidOperationException){progressElement=null;}catch(COMException){progressElement=null;}
    }
    void LeavePerch(){perch=IntPtr.Zero;guiding=false;holdUntil=walkTime;previousLedge=Rect.Empty;}
    void Later(Action work){
        Signal("think","Hover over the target window…",2);
        var delay=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        delay.Tick+=delegate{delay.Stop();if(!quitting && window.IsVisible)work();};delay.Start();
    }
    void SelectWindow(bool close){
        IntPtr h=GetAncestor(WindowFromPoint(Forms.Cursor.Position),2);
        uint pid;GetWindowThreadProcessId(h,out pid);
        Rect r=RectFor(h);
        if(r.IsEmpty||pid==(uint)System.Diagnostics.Process.GetCurrentProcess().Id||h==FindWindow("Shell_TrayWnd",null)
           ||h==FindWindow("Progman",null)){Signal("offer","Choose an application window.",3);return;}
        if(close){
            try {
            var app=AutomationElement.FromHandle(h);
            var button=app.FindFirst(TreeScope.Descendants,new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                new PropertyCondition(AutomationElement.AutomationIdProperty,"Close")));
            if(button==null){Signal("offer","No supported Close button found.",3);return;}
            if(MessageBox.Show("Close "+app.Current.Name+" when Mellow reaches its Close button?",
                "Mellow",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            closeHandle=h;closePid=pid;target=button;perch=IntPtr.Zero;BeginTarget();return;
            }catch(ElementNotAvailableException){}catch(InvalidOperationException){}catch(COMException){}
            CancelTarget();Signal("offer","Window controls unavailable.",3);return;
        }
        if(r.Top<window.Height-FootInset){Signal("offer","That title bar has no space above it.",3);return;}
        CancelTarget();perch=h;perchOffset=Clamp(r.Width*.5-window.Width*.5,0,r.Width);
        previousLedge=r;guiding=true;Travel(new Point(r.Left+perchOffset,r.Top-window.Height+FootInset),walkTime,false);
        holdUntil=departure+travel+.2;Signal("guide","",travel+.2);
    }
    void CancelTarget(){target=null;closeHandle=IntPtr.Zero;guiding=false;}
    void BeginTarget(){pointCaption="Here";guiding=true;targetCenter=new Point(-10000,-10000);RefreshTarget();}
    void RefreshTarget(){
        try{
            Rect r=target.Current.BoundingRectangle;
            if(r.IsEmpty || target.Current.IsOffscreen || !target.Current.IsEnabled)throw new InvalidOperationException();
            var m=PresentationSource.FromVisual(window).CompositionTarget.TransformFromDevice;
            var center=m.Transform(new Point(r.Left+r.Width/2,r.Top+r.Height/2));
            if(!new Rect(0,0,SystemParameters.PrimaryScreenWidth,SystemParameters.PrimaryScreenHeight).Contains(center))
                throw new InvalidOperationException();
            if((targetCenter-center).Length>3){
                pointingLeft=center.X<SystemParameters.PrimaryScreenWidth*.5;
                double gap=closeHandle==IntPtr.Zero?Math.Min(80,m.Transform(r.BottomRight).X-center.X):0;
                Travel(new Point(center.X-window.Width/2+(pointingLeft?1:-1)*(size*.36+gap),
                    center.Y-window.Height+size*.47),walkTime,false);
                targetCenter=center;holdUntil=departure+travel+4;Signal("guide","",travel+.3);
            }
            if(closeHandle!=IntPtr.Zero && walkTime>=departure+travel+.55 && state=="point"){
                uint pid;
                IntPtr hit=GetAncestor(WindowFromPoint(new System.Drawing.Point((int)(r.Left+r.Width/2),(int)(r.Top+r.Height/2))),2);
                GetWindowThreadProcessId(closeHandle,out pid);
                if(hit!=closeHandle||pid!=closePid||!IsWindowVisible(closeHandle))throw new InvalidOperationException();
                bool sent=PostMessage(closeHandle,0x10,IntPtr.Zero,IntPtr.Zero);CancelTarget();
                Signal(sent?"listen":"offer",sent?"Close requested":"Windows declined the close request.",2);
            }
        }catch(Exception ex){
            if(!(ex is ElementNotAvailableException || ex is InvalidOperationException || ex is COMException || ex is UnauthorizedAccessException))throw;
            CancelTarget();Signal("idle","",0);holdUntil=walkTime+.7;
        }
    }
 }
}
