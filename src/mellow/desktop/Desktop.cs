using System;
using System.Diagnostics;
using System.IO;
using Ellipse = System.Windows.Shapes.Ellipse;
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
        TextBox transcript, input;
        Process backend;
        Border bubble;
        StackPanel bubbleStack;
        TextBlock bubbleText, tail;
        Canvas cloud;
        Grid bubbleFace;
        bool quiet, moving, quitting, ready, hotkeyRegistered;
        double offset=30, lift=0, size=116, until, walkTime, lastTick;
        readonly string verifyChat=Environment.GetEnvironmentVariable("MELLOW_VERIFY_CHAT");
        int verifyStage;
        string state="idle";
        string answer="";
        DateTime lastPaint;
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
                size=Math.Max(80,Math.Min(176,Pref("Size",116)));
                offset=Pref("Offset",30); lift=Pref("Lift",0);
            }
            window.Content=null;
            var grid=new Grid(); grid.Children.Add(view);
            bubbleText=new TextBlock{TextWrapping=TextWrapping.Wrap,MaxHeight=53,
                FontSize=13,Foreground=Brushes.Indigo,TextTrimming=TextTrimming.CharacterEllipsis};
            bubble=new Border{Background=Brushes.Lavender,Padding=new Thickness(12,6,12,6),Child=bubbleText};
            tail=new TextBlock{Foreground=Brushes.MediumPurple,Margin=new Thickness(12,-3,0,0)};
            bubbleStack=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom,MaxWidth=178,
                IsHitTestVisible=false,Visibility=Visibility.Collapsed};
            bubbleFace=new Grid();
            cloud=new Canvas{IsHitTestVisible=false};
            bubbleFace.Children.Add(cloud);bubbleFace.Children.Add(bubble);
            bubbleFace.SizeChanged+=delegate {
                double width=bubbleFace.ActualWidth;
                cloud.Children.Clear();
                for(int i=0;i<5;i++){
                    double diameter=Math.Min(26,width/4);
                    var puff=new Ellipse{Width=diameter,Height=diameter,Fill=Brushes.Thistle};
                    Canvas.SetLeft(puff,4+(width-diameter-8)*i/4);
                    Canvas.SetTop(puff,-3-(i%2)*3);cloud.Children.Add(puff);
                }
            };
            bubbleStack.Children.Add(bubbleFace); bubbleStack.Children.Add(tail);
            grid.Children.Add(bubbleStack); window.Content=grid; LayoutAvatar();
            window.MouseLeftButtonDown+=delegate {
                if(!moving) return;
                window.DragMove();
                var r=SystemParameters.WorkArea;
                walkTime=0; offset=r.Right-window.Width-window.Left; lift=r.Bottom-window.Height+4-window.Top;
                moving=false; SetPassive(true); SavePosition();
            };
            if(verify && verifyChat!="1") return;
            menu=new Forms.ContextMenuStrip();
            menu.Items.Add("Chat with Mellow",null,delegate {OpenChat();});
            menu.Items.Add("Move ghost (drag it once)",null,delegate {window.Show(); moving=true; SetPassive(false);});
            foreach(int n in new int[]{82,116,156}) {
                int pixels=n;
                menu.Items.Add("Size "+pixels,null,delegate {size=pixels;LayoutAvatar();SavePosition();});
            }
            var mute=new Forms.ToolStripMenuItem("Quiet (hide bubbles)"){CheckOnClick=true};
            mute.CheckedChanged+=delegate {quiet=mute.Checked; if(quiet) Signal("idle","",0);};
            menu.Items.Add(mute);
            menu.Items.Add("Hide / show ghost",null,delegate {if(window.IsVisible) window.Hide(); else window.Show();});
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
                if(!hotkeyRegistered) tray.Text="Mellow (shortcut busy; double-click to chat)";
            };
            window.Closed+=delegate {
                quitting=true;
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
            if(msg==0x0312 && w.ToInt32()==Hotkey) {OpenChat();handled=true;}
            return IntPtr.Zero;
        }
        void SetPassive(bool value) {
            var h=new WindowInteropHelper(window).Handle;
            int style=GetWindowLong(h,-20);
            SetWindowLong(h,-20,value?style|0x20:style&~0x20);
        }
        void Signal(string next,string text,double seconds) {
            state=next; until=time.Elapsed.TotalSeconds+seconds;
            bubbleText.Text=text;
            bool speaking=next=="speak"||next=="offer";
            cloud.Visibility=speaking?Visibility.Collapsed:Visibility.Visible;
            tail.Text=speaking?"▾":"● ·";
            bubble.CornerRadius=new CornerRadius(speaking?9:20);
            bubble.Background=speaking?Brushes.Lavender:Brushes.Thistle;
            bubble.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=4,ShadowDepth=1,Opacity=.24};
            bubbleStack.Margin=new Thickness(0,0,0,size*.86);
            bubbleStack.Visibility=quiet||text.Length==0?Visibility.Collapsed:Visibility.Visible;
        }
        void OpenChat() {
            if(!window.IsVisible) window.Show();
            if(chatWindow==null) {
                transcript=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
                input=new TextBox{Margin=new Thickness(0,6,0,0),IsEnabled=false};
                var panel=new DockPanel{Margin=new Thickness(10)};
                DockPanel.SetDock(input,Dock.Bottom);panel.Children.Add(input);panel.Children.Add(transcript);
                chatWindow=new Window{Title="Mellow chat",Width=460,Height=340,Content=panel};
                chatWindow.Closing+=(s,e)=>{if(!quitting){e.Cancel=true;chatWindow.Hide();}};
                input.KeyDown+=(s,e)=>{
                    if(e.Key!=System.Windows.Input.Key.Enter||!ready||input.Text.Trim().Length==0)return;
                    e.Handled=true;
                    SendInput();
                };
                input.TextChanged+=delegate {if(ready && input.Text.Length>0)Signal("listen","",3);};
            }
            chatWindow.Show();chatWindow.Activate();
            if(backend==null||backend.HasExited) StartChat();
            input.Focus();
        }
        void SendInput(){
                    string msg=input.Text.Trim();input.Clear();ready=false;input.IsEnabled=false;answer="";
                    transcript.AppendText("\nYou: "+msg+"\n");
                    try {backend.StandardInput.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(msg)));}
                    catch(IOException){transcript.AppendText("Chat disconnected. Reopen from the tray.\n");}
        }
        void StartChat() {
            if(backend!=null) backend.Dispose();
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
            if(kind=="text"){transcript.AppendText(text);transcript.ScrollToEnd();}
            else if(kind=="ready"){ready=true;input.IsEnabled=true;if(chatWindow.IsActive)input.Focus();}
            else if(kind=="think"){Signal("think",text,35);}
            else if(kind=="chunk"){
                if(answer.Length<120) answer+=text.Substring(0,Math.Min(120-answer.Length,text.Length));
                if(state!="speak" || (DateTime.UtcNow-lastPaint).TotalMilliseconds>80) {
                    Signal("speak",answer.Substring(0,Math.Min(answer.Length,120)),5);lastPaint=DateTime.UtcNow;
                }
            }
            else if(kind=="done"){Signal(text=="ok"?"speak":"idle",text=="ok"?answer.Substring(0,Math.Min(120,answer.Length)):"",4);}
            else if(kind=="end"){ready=false;input.IsEnabled=false;Signal("idle","",0);}
        }
    }
}
