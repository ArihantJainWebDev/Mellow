using System;
using System.Diagnostics;
using System.IO;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Media.Animation;
using Polygon = System.Windows.Shapes.Polygon;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MellowDesktop {
    public sealed partial class Ghost {
        const string Prefs=@"Software\Mellow\Avatar";
        const string Startup=@"Software\Microsoft\Windows\CurrentVersion\Run";
        const int Hotkey=0x4D45;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hwnd,int id);
        Forms.NotifyIcon tray;
        Forms.ContextMenuStrip menu;
        Window chatWindow;
        TextBox input;
        Process backend;
        Border bubble;
        StackPanel bubbleStack;
        TextBlock bubbleText;
        Grid bubbleTail;
        FrameworkElement speechTip,thoughtDots;
        Canvas cloud;
        Grid bubbleFace;
        bool dragConsumed, passThrough;
        bool quiet, moving, quitting, ready, hotkeyRegistered;
        double offset=30, lift=0, size=116, until, walkTime, lastTick;
        readonly string verifyChat=Environment.GetEnvironmentVariable("MELLOW_VERIFY_CHAT");
        int verifyStage;
        string state="idle";
        static int Pref(string key,int fallback) {
            object v=Registry.GetValue(@"HKEY_CURRENT_USER\"+Prefs,key,fallback);
            return v is int ? (int)v : fallback;
        }
        void SavePosition() {
            using(var key=Registry.CurrentUser.CreateSubKey(Prefs)) {
                key.SetValue("Offset",(int)offset); key.SetValue("Lift",(int)lift);
                key.SetValue("Size",(int)size);
            }
        }
        void LayoutAvatar() {
            window.Width=Math.Max(180,size+70); window.Height=size+86;
            view.Width=size; view.Height=size;
            view.VerticalAlignment=VerticalAlignment.Bottom;
            view.HorizontalAlignment=HorizontalAlignment.Center;
        }
        void InitDesktop() {
            if(!verify) {
                rises=Math.Min(6,Math.Max(0,Pref("TaskbarRises",0)));
                size=Math.Max(80,Math.Min(176,Pref("Size",116)));
                offset=Pref("Offset",30); lift=Pref("Lift",0);
            }
            window.Content=null;
            var grid=new Grid(); grid.Children.Add(view);
            bubbleText=new TextBlock{TextWrapping=TextWrapping.Wrap,MaxHeight=51,LineHeight=17,
                FontFamily=new FontFamily("Segoe UI"),FontSize=13,Foreground=Brushes.MidnightBlue,
                TextTrimming=TextTrimming.CharacterEllipsis};
            var pearl=new SolidColorBrush(Color.FromRgb(250,244,252));
            bubble=new Border{Background=pearl,Padding=new Thickness(13,7,13,7),Child=bubbleText};
            bubbleTail=new Grid{Height=10,Width=23,HorizontalAlignment=HorizontalAlignment.Center};
            speechTip=new Polygon{Fill=pearl,Points=new PointCollection{new Point(2,0),new Point(16,0),new Point(14,8)}};
            var dots=new Canvas();
            var big=new Ellipse{Fill=pearl,Width=7,Height=5};dots.Children.Add(big);
            var little=new Ellipse{Fill=pearl,Width=4,Height=3};Canvas.SetTop(little,6);Canvas.SetLeft(little,8);dots.Children.Add(little);
            thoughtDots=dots;bubbleTail.Children.Add(speechTip);bubbleTail.Children.Add(dots);
            bubbleStack=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom,MaxWidth=178,
                IsHitTestVisible=false,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,size*.87),Visibility=Visibility.Collapsed};
            bubbleStack.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Color.FromRgb(46,31,58),BlurRadius=6,ShadowDepth=1.5,Opacity=.30};
            bubbleFace=new Grid();
            cloud=new Canvas{IsHitTestVisible=false};
            bubbleFace.Children.Add(cloud);bubbleFace.Children.Add(bubble);
            bubbleFace.SizeChanged+=delegate {
                double width=bubbleFace.ActualWidth;
                cloud.Children.Clear();
                for(int i=0;i<5;i++){
                    double diameter=Math.Min(26,width/4);
                    var puff=new Ellipse{Width=diameter,Height=diameter,Fill=pearl};
                    Canvas.SetLeft(puff,4+(width-diameter-8)*i/4);
                    Canvas.SetTop(puff,-3-(i%2)*3);cloud.Children.Add(puff);
                }
            };
            bubbleStack.Children.Add(bubbleFace); bubbleStack.Children.Add(bubbleTail);
            grid.Children.Add(bubbleStack); window.Content=grid; LayoutAvatar();
            view.Cursor=System.Windows.Input.Cursors.Hand;
            window.MouseRightButtonUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e) {
                if(menu==null) return;
                e.Handled=true;
                ShowGhostMenu();
            };
            window.MouseLeftButtonUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e) {
                if(moving || dragConsumed || menu==null) {dragConsumed=false;return;}
                e.Handled=true;
                OpenChat();
            };
            window.MouseLeftButtonDown+=delegate {
                dragConsumed=false;
                if(!moving) return;
                dragConsumed=true;
                window.DragMove();
                var bounds=Bounds();
                placed=false;guiding=false;pathStage=0;travel=0; offset=bounds.Right-window.Left; lift=Math.Max(0,bounds.Bottom-window.Top);
                moving=false; SetPassive(passThrough); SavePosition();
            };
            if(offscreenReview || (verify && verifyChat!="1")) return;
            menu=new Forms.ContextMenuStrip();
            if(clothGhost!=null) menu.Items.Add("Perform hat trick",null,delegate {trickStart=time.Elapsed.TotalSeconds;Signal("perform","",15);});
            var visionMenu=new Forms.ToolStripMenuItem("Inspect selected region locally");
            visionMenu.DropDownItems.Add("Show me the error",null,delegate {InspectRegion("error");});
            visionMenu.DropDownItems.Add("Compare visible products",null,delegate {InspectRegion("compare");});
            visionMenu.DropDownItems.Add("Show unfinished checklist item",null,delegate {InspectRegion("checkpoint");});
            menu.Items.Add(visionMenu);
            menu.Items.Add("Watch primary screen locally",null,delegate{StartWatching();});
            menu.Items.Add("Stop screen observation",null,delegate{StopWatching();});
            menu.Items.Add("Open project checkpoint…",null,delegate {OpenCheckpoint();});
            menu.Items.Add("Perch on a window",null,delegate {Later(delegate {SelectWindow(false);});});
            menu.Items.Add("Watch a progress bar",null,delegate {Later(WatchProgress);});
            menu.Items.Add("Dismiss / stop watching",null,delegate {StopWatching();dismissed=true;progressElement=null;if(perch!=IntPtr.Zero) DropFromPerch();else if(edgeMove!=EdgeMove.None) ReleaseEdge(walkTime);CancelTarget();Signal("idle","",0);});
            menu.Items.Add("Leave ledge",null,delegate {if(perch!=IntPtr.Zero) DropFromPerch();else if(edgeMove!=EdgeMove.None)ReleaseEdge(walkTime);});
            menu.Items.Add("Close a window…",null,delegate {Later(delegate {SelectWindow(true);});});
            menu.Items.Add("Chat with Mellow",null,delegate {OpenChat();});
            menu.Items.Add("Move ghost (drag it once)",null,delegate {window.Show();LeavePerch();CancelTarget(); moving=true; SetPassive(false);});
            var pass=new Forms.ToolStripMenuItem("Click through ghost (restore from tray)"){CheckOnClick=true};
            pass.CheckedChanged+=delegate {passThrough=pass.Checked;SetPassive(passThrough);};
            menu.Items.Add(pass);
            menu.Items.Add("Point at a button or link (hover, Ctrl+Alt+P)",null,delegate {
                Signal("think","Hover over a button or link…",2);
                var delay=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
                delay.Tick+=delegate {delay.Stop();if(!quitting)PointAtControl();};delay.Start();
            });
            menu.Items.Add("Walk along the floor",null,delegate {
                LeavePerch();CancelTarget();descending=false;
                var b=Bounds();double sign=window.Left<b.Left+b.Width/2?1:-1;
                Travel(new Point(window.Left+sign*size*1.8,b.Bottom),walkTime,false);
                holdUntil=walkTime+travel+3;Signal("idle","",0);
            });
            menu.Items.Add("Climb to the top edge",null,delegate {
                RequestEdgeClimb();
            });
            window.Loaded+=delegate {if(!verify) Signal("offer","Click to chat. Right-click for controls.",7);};
            foreach(int n in new int[]{82,116,156}) {
                int pixels=n;
                menu.Items.Add("Size "+pixels,null,delegate {var oldFloor=Bounds().Bottom;
                    bool standing=Math.Abs(window.Top-oldFloor)<1 && !guiding && perch==IntPtr.Zero;
                    double oldWidth=window.Width;
                    size=pixels;LayoutAvatar();
                    if(standing){double delta=Bounds().Bottom-oldFloor;origin.Y+=delta;destination.Y+=delta;window.Top+=delta;}
                    if(edgeMove==EdgeMove.Approach){
                        var b=Bounds();
                        Travel(new Point(gripWall<0?b.Left:b.Right,b.Bottom),walkTime,false,edge:EdgeMove.Approach);
                        holdUntil=walkTime+travel+.22;
                    }
                    if(edgeMove==EdgeMove.Ascend || edgeMove==EdgeMove.Traverse){
                        var b=Bounds();
                        double dx=edgeMove==EdgeMove.Traverse?(oldWidth-window.Width)*.5:
                            (gripWall<0?b.Left:b.Right)-window.Left;
                        origin.X+=dx;destination.X+=dx;window.Left+=dx;
                        double dy=b.Top-destination.Y;
                        destination.Y+=dy;
                        if(edgeMove==EdgeMove.Traverse || walkTime>=departure+travel){origin.Y+=dy;window.Top+=dy;}
                    }
                    SavePosition();});
            }
            var mute=new Forms.ToolStripMenuItem("Quiet (hide bubbles)"){CheckOnClick=true};
            mute.CheckedChanged+=delegate {quiet=mute.Checked; if(quiet) Signal("idle","",0);};
            menu.Items.Add(mute);
            menu.Items.Add("Hide / show ghost",null,delegate {if(window.IsVisible){StopWatching();window.Hide();}else window.Show();});
            var previews=new Forms.ToolStripMenuItem("Preview a pose");
            foreach(string name in new string[]{"listen","think","point","offer","speak","rest","privacy"}) {
                string pose=name;
                previews.DropDownItems.Add(name,null,delegate {Signal(pose,"Pose preview: "+pose,4);});
            }
            menu.Items.Add(previews);
            var startup=new Forms.ToolStripMenuItem("Launch at sign-in");
            using(var key=Registry.CurrentUser.OpenSubKey(Startup))
                startup.Checked=key!=null && key.GetValue("MellowAvatar")!=null;
            startup.Click+=delegate {
                using(var key=Registry.CurrentUser.CreateSubKey(Startup)){
                    if(startup.Checked) key.DeleteValue("MellowAvatar",false);
                    else {
                        var python=Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("MELLOW_PYTHON")),"pythonw.exe");
                        key.SetValue("MellowAvatar","\""+python+"\" -m mellow.avatar --home \""+Environment.CurrentDirectory+"\"");
                    }
                    startup.Checked=!startup.Checked;
                }
            };
            menu.Items.Add(startup);
            menu.Items.Add("Exit Mellow",null,delegate {window.Close();});
            tray=new Forms.NotifyIcon{Text="Mellow desktop",Icon=System.Drawing.SystemIcons.Information,
                Visible=true,ContextMenuStrip=menu};
            tray.DoubleClick+=delegate {OpenChat();};
            window.SourceInitialized+=delegate {
                var h=new WindowInteropHelper(window).Handle;
                HwndSource.FromHwnd(h).AddHook(Hook);
                hotkeyRegistered=RegisterHotKey(h,Hotkey,0x4003,0x4D);
                guideKey=RegisterHotKey(h,Hotkey+1,0x4003,0x50);
                if(!hotkeyRegistered) tray.Text="Mellow (shortcut busy; double-click to chat)";
            };
            window.Closed+=delegate {
                quitting=true;StopWatching();DismissReply();
                if(checklist!=null)checklist.Close();
                if(guideKey) UnregisterHotKey(new WindowInteropHelper(window).Handle,Hotkey+1);
                if(hotkeyRegistered) UnregisterHotKey(new WindowInteropHelper(window).Handle,Hotkey);
                if(backend!=null) {
                    if(!backend.HasExited) {try {backend.StandardInput.Close();} catch(IOException) {} if(!backend.WaitForExit(2000)) backend.Kill();}
                    backend.Dispose();
                }
                if(chatWindow!=null) chatWindow.Close();
                tray.Dispose(); menu.Dispose();
            };
        }
        IntPtr Hook(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled) {
            if(msg==0x0312 && w.ToInt32()==Hotkey+1){PointAtControl();handled=true;}
            if(msg==0x0312 && w.ToInt32()==Hotkey) {OpenChat();handled=true;}
            return IntPtr.Zero;
        }
        MenuItem GhostMenuItem(Forms.ToolStripMenuItem source){
            var item=new MenuItem{Header=source.Text,IsEnabled=source.Enabled,IsChecked=source.Checked};
            foreach(Forms.ToolStripItem child in source.DropDownItems){
                var action=child as Forms.ToolStripMenuItem;
                if(action!=null)item.Items.Add(GhostMenuItem(action));
            }
            if(item.Items.Count==0)item.Click+=delegate{source.PerformClick();};
            return item;
        }
        void ShowGhostMenu(){
            var popup=new ContextMenu{PlacementTarget=view,Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                StaysOpen=false,MaxHeight=SystemParameters.WorkArea.Height-12};
            foreach(Forms.ToolStripItem node in menu.Items){
                var action=node as Forms.ToolStripMenuItem;
                if(action!=null)popup.Items.Add(GhostMenuItem(action));
            }
            popup.Items.Add(new Separator());
            popup.Items.Add(new MenuItem{Header="Dismiss menu"});
            popup.IsOpen=true;
            popup.Focus();
        }
        void SetPassive(bool value) {
            var h=new WindowInteropHelper(window).Handle;
            int style=GetWindowLong(h,-20);
            SetWindowLong(h,-20,value?style|0x20:style&~0x20);
        }
        void Signal(string next,string text,double seconds) {
            if(next=="privacy")StopWatching();
            state=next; until=time.Elapsed.TotalSeconds+seconds;
            bubbleText.Text=text;
            bool speaking=next=="speak"||next=="offer";
            cloud.Visibility=speaking?Visibility.Collapsed:Visibility.Visible;
            speechTip.Visibility=speaking?Visibility.Visible:Visibility.Collapsed;
            thoughtDots.Visibility=speaking?Visibility.Collapsed:Visibility.Visible;
            bubble.CornerRadius=new CornerRadius(speaking?12:19);
            bubbleStack.Margin=new Thickness(0,0,0,size*.87);
            bool show=!quiet && text.Length>0;
            if(show && bubbleStack.Visibility!=Visibility.Visible)
                bubbleStack.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(120)){FillBehavior=FillBehavior.Stop});
            bubbleStack.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        }
        void OpenChat() {
            if(!window.IsVisible) window.Show();
            if(chatWindow==null)CreateComposer();
            if(!chatWindow.IsActive)previousApp=GetForegroundWindow();
            var area=SystemParameters.WorkArea;
            chatWindow.Width=Math.Min(620,area.Width-24);
            chatWindow.Left=area.Left+(area.Width-chatWindow.Width)/2;
            chatWindow.Top=area.Top+18;
            chatWindow.Show();chatWindow.Activate();
            if(backend==null||backend.HasExited)StartChat();
            input.Focus();
        }
        void SendInput(){
            string msg=input.Text.Trim();input.Clear();
            string intent=DesktopIntent(msg);
            if(intent.StartsWith("/browser ",StringComparison.OrdinalIgnoreCase) ||
               intent.StartsWith("/screen watch",StringComparison.OrdinalIgnoreCase) ||
               intent.StartsWith("/screen ask ",StringComparison.OrdinalIgnoreCase) ||
               intent=="/point" || intent=="/close-window")HideComposer();
            ResetReply();
            if(WorkspaceCommand(msg))return;
            commandOutput.Clear();submitted=true;streamed=false;ready=false;input.IsEnabled=false;
            try {backend.StandardInput.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(msg)));}
            catch(IOException){submitted=false;LocalReply("Chat disconnected. Reopen the composer to retry.");}
        }
        void StartChat() {
            if(backend!=null) backend.Dispose();
            commandOutput.Clear();streamed=false;submitted=false;
            ready=false;input.IsEnabled=false;
            var proc=new Process{StartInfo=new ProcessStartInfo(
                Environment.GetEnvironmentVariable("MELLOW_PYTHON"),"-u -m mellow.desktop_chat"){
                UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,CreateNoWindow=true}};
            backend=proc;
            proc.OutputDataReceived+=(s,e)=>{
                if(e.Data==null)return;
                string line=e.Data;
                window.Dispatcher.BeginInvoke(new Action(()=>{if(!quitting && backend==proc)HandleLine(line);}));
            };
            proc.EnableRaisingEvents=true;
            proc.Exited+=delegate {window.Dispatcher.BeginInvoke(new Action(()=>{
                if(!quitting && backend==proc){ready=false;input.IsEnabled=false;}
            }));};
            proc.Start();proc.BeginOutputReadLine();
        }
        void HandleLine(string line) {
            int split=line.IndexOf(' '); if(split<0)return;
            string kind=line.Substring(0,split);
            string text=Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(split+1)));
            if(kind=="text"){if(!streamed)commandOutput.Append(text);}
            else if(kind=="ready"){
                if(submitted && !streamed && commandOutput.Length>0)LocalReply(commandOutput.ToString().TrimEnd());
                commandOutput.Clear();submitted=false;ready=true;input.IsEnabled=true;if(chatWindow.IsActive)input.Focus();
            }
            else if(kind=="think"){streamed=true;commandOutput.Clear();Signal("think",text,35);}
            else if(kind=="chunk"){AddReply(text);}
            else if(kind=="done"){
                if(text!="ok")AddReply("\n[Reply interrupted. Try again.]");
                finished=true;
            }
            else if(kind=="end"){
                ready=false;input.IsEnabled=false;
                if(text!="0")LocalReply(commandOutput.Length>0?commandOutput.ToString().Trim():"Chat stopped. Check settings.");
                commandOutput.Clear();
            }
        }
    }
}
