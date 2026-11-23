using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace MellowDesktop {
 public sealed partial class Ghost {
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    static Window TestLedge(Rect rect) {
        var w=new Window{Title="Mellow verification fixture",WindowStartupLocation=WindowStartupLocation.Manual,
            WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,
            Topmost=true,ShowActivated=false,Focusable=false,Background=Brushes.SlateGray,
            Left=rect.Left,Top=rect.Top,Width=rect.Width,Height=rect.Height};
        w.Show();w.UpdateLayout();return w;
    }
    void VerifyPerch() {
        // Disposable native windows exercise DWM geometry and actual hit testing.
        var fixture=new Rect(SystemParameters.PrimaryScreenWidth*.32,floorY*.42,
            Math.Min(450,SystemParameters.PrimaryScreenWidth*.32),floorY*.33);
        for(int trial=0;trial<4;trial++) {
            Window w=TestLedge(fixture),cover=null;
            try {
                var handle=new WindowInteropHelper(w).Handle;
                var b=Bounds();
                window.Left=Clamp((trial==1?fixture.Right+size:fixture.Left-size)-window.Width*.5,b.Left,b.Right);
                window.Top=b.Bottom;
                if(!StartPerch(handle))throw new InvalidOperationException("Fixture did not expose a ledge.");
                bool sawClimb=false,sawTopWalk=false,midClimb=false;
                for(int i=0;i<1200 && ledgeLeg!=LedgeLeg.Rest && perch!=IntPtr.Zero;i++){
                    walkTime+=.04;UpdatePerch(walkTime,GetForegroundWindow());
                    double step,weight,direction;
                    Navigate(walkTime,out step,out weight,out direction);
                    if(ClimbingFrame && weight>.4) {
                        if(!sawClimb)VerifyFrameGrip(walkTime,step,weight,direction);
                        sawClimb=true;
                        if(Math.Abs(window.Left-WallX(VisibleFrame(handle)))>2)
                            throw new InvalidOperationException("Climb left the side of the window.");
                        if(trial==2){midClimb=true;break;}
                    }
                    if(ledgeLeg==LedgeLeg.Cross && onFoot)sawTopWalk=true;
                }
                if(perch==IntPtr.Zero || !sawClimb || (!midClimb && (!sawTopWalk || ledgeLeg!=LedgeLeg.Rest)))
                    throw new InvalidOperationException("Perch route missed the climb or title-bar walk.");
                if(trial==0){
                    w.Left+=22;w.Top-=12;w.UpdateLayout();UpdatePerch(walkTime,GetForegroundWindow());
                    double s,a,d;Navigate(walkTime,out s,out a,out d);
                    if(Math.Abs(window.Top+window.Height-FootInset-VisibleFrame(handle).Top)>2)
                        throw new InvalidOperationException("Moved window lost foot contact.");
                    w.Close();w=null;
                } else if(trial==1) {
                    cover=TestLedge(fixture);
                } else if(trial==2) w.WindowState=WindowState.Minimized;
                // Supply the shell as the next foreground sample without activating
                // any window on the owner's desktop. The ledge itself stays exposed.
                if(trial==3)perchForeground=handle;
                UpdatePerch(walkTime,trial==3?GetShellWindow():GetForegroundWindow());
                if(!landing || perch!=IntPtr.Zero)throw new InvalidOperationException("Lost ledge did not start a descent: "+trial);
                // Hold ordinary attention throughout; recovery must still reach the floor.
                attentiveUntil=double.PositiveInfinity;
                for(int i=0;i<500 && landing;i++)Pose(lastTick+.04);
                attentiveUntil=0;
                if(landing || Math.Abs(window.Top-Bounds().Bottom)>1.5)
                    throw new InvalidOperationException("Lost ledge stranded the avatar.");
            } finally {if(cover!=null)cover.Close();if(w!=null)w.Close();}
        }
        Console.WriteLine("Perch: both edges, moved, closed, covered and minimized mid-climb, simulated focus switch; returned to floor.");
    }
 }
}
