using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;
using Microsoft.Win32;

namespace MellowDesktop {
 public sealed partial class Ghost {
    Window watchBadge;
    TextBlock watchLabel;
    DispatcherTimer watchTimer;
    int watchGeneration;
    bool sampling,showingScreenReply;
    string observation="";
    DateTime observedAt;

    void StartWatching(){
        if(watchBadge!=null){LocalReply("Primary screen observation is already on.");return;}
        watchGeneration++;observation="";
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(8,2,4,2)};
        watchLabel=new TextBlock{Text="Screen on · local",VerticalAlignment=VerticalAlignment.Center};
        bar.Children.Add(watchLabel);
        bar.Children.Add(Choice("Stop",StopWatching));
        var area=SystemParameters.WorkArea;
        watchBadge=new Window{Title="Mellow screen observation",ShowActivated=false,ShowInTaskbar=false,
            Topmost=true,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            Background=Ink(245,237,251),SizeToContent=SizeToContent.WidthAndHeight,Left=area.Left+8,Top=area.Top+8,Content=bar};
        watchBadge.Closed+=delegate{
            watchGeneration++;watchBadge=null;observation="";
            if(showingScreenReply)ResetReply();
            if(watchTimer!=null){watchTimer.Stop();watchTimer=null;}
            SystemEvents.SessionSwitch-=OnSessionChange;
        };
        watchBadge.Show();
        SystemEvents.SessionSwitch+=OnSessionChange;
        watchTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(12)};
        watchTimer.Tick+=delegate{SampleScreen();};watchTimer.Start();SampleScreen();
    }
    void OnSessionChange(object sender,SessionSwitchEventArgs e){
        if(e.Reason==SessionSwitchReason.SessionLock || e.Reason==SessionSwitchReason.SessionLogoff ||
           e.Reason==SessionSwitchReason.RemoteDisconnect || e.Reason==SessionSwitchReason.ConsoleDisconnect)
            window.Dispatcher.BeginInvoke(new Action(StopWatching));
    }
    void StopWatching(){if(watchBadge!=null)watchBadge.Close();}
    static bool CanObserveFocus(){
        try{
            var element=AutomationElement.FocusedElement;
            if(element==null)return false;
            object password=element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty,true);
            return password is bool && !(bool)password;
        }catch(System.Runtime.InteropServices.COMException){return false;}
         catch(InvalidOperationException){return false;}
         catch(UnauthorizedAccessException){return false;}
    }
    async void SampleScreen(string question=""){
        if(quitting || watchBadge==null)return;
        if(sampling){
            if(question.Length>0)LocalReply("A local screen check is running. Try your question again in a moment.");
            return;
        }
        if(inspecting || (chatWindow!=null && chatWindow.IsVisible) || !CanObserveFocus()){
            observation="";watchLabel.Text="Screen paused · focus";if(question.Length>0)LocalReply("Screen paused for this focus. Return to a supported app and try again.");return;
        }
        int generation=watchGeneration,requestedDuring=dialogueVersion;
        sampling=true;watchLabel.Text="Screen on · checking";
        try{
            // Copy the current primary display once; inference is serialized, on loopback.
            var b=Forms.Screen.PrimaryScreen.Bounds;
            DateTime captured=DateTime.Now;
            byte[] image=CaptureArea(new Rect(b.X,b.Y,b.Width,b.Height));
            string[] lines=await Task.Run(()=>AskVision(image,"screen",question));
            if(watchBadge==null || generation!=watchGeneration || quitting)return;
            observation=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(lines[0]));
            if(question.Length>0 && requestedDuring==dialogueVersion){LocalReply(observation);showingScreenReply=true;}
            observedAt=captured;watchLabel.Text="Screen on · "+observedAt.ToString("HH:mm:ss");
        }catch(Exception){
            if(watchBadge!=null && generation==watchGeneration){observation="";watchLabel.Text="Screen on · model unavailable";if(question.Length>0 && requestedDuring==dialogueVersion)LocalReply("Screen check failed. Check the vision model in LM Studio.");}
        }finally{sampling=false;}
    }
    static byte[] CaptureArea(Rect area){
        using(var capture=new Drawing.Bitmap((int)area.Width,(int)area.Height)){
            using(var g=Drawing.Graphics.FromImage(capture))g.CopyFromScreen((int)area.X,(int)area.Y,0,0,capture.Size);
            double scale=Math.Min(1,1100.0/Math.Max(capture.Width,capture.Height));
            using(var reduced=new Drawing.Bitmap(capture,Math.Max(1,(int)(capture.Width*scale)),Math.Max(1,(int)(capture.Height*scale))))
            using(var output=new MemoryStream()){reduced.Save(output,Drawing.Imaging.ImageFormat.Png);return output.ToArray();}
        }
    }
    public static void VerifyScreen(){
        byte[] image;
        using(var bitmap=new Drawing.Bitmap(680,160))
        using(var g=Drawing.Graphics.FromImage(bitmap))
        using(var font=new Drawing.Font("Arial",32))
        using(var memory=new MemoryStream()){
            g.Clear(Drawing.Color.White);
            g.DrawString("Practice quiz: BLUEBIRD",font,Drawing.Brushes.Black,16,42);
            bitmap.Save(memory,Drawing.Imaging.ImageFormat.Png);image=memory.ToArray();
        }
        string[] lines=AskVision(image,"screen","Which word comes after Practice quiz?");
        string answer=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(lines[0]));
        if(answer.IndexOf("bluebird",StringComparison.OrdinalIgnoreCase)<0)
            throw new InvalidOperationException("The local model did not identify the fixture word.");
        Console.WriteLine("Local screen question passed using a synthetic card.");
    }
    static bool WebAddress(string value,out Uri uri){
        return Uri.TryCreate(value,UriKind.Absolute,out uri) && uri.Scheme=="https" && uri.Host.Length>0 &&
            uri.UserInfo.Length==0 && !value.Contains("\\") && !value.Contains("\"") &&
            value.IndexOfAny(new[]{'\r','\n','\t',' '})<0;
    }
    string NavigateBrowser(string action){
        int cmd=action=="back"?1:action=="forward"?2:action=="refresh"?3:0;
        if(cmd==0)return "Use /browser back, forward or refresh.";
        var h=GetForegroundWindow();uint pid;
        if(h==IntPtr.Zero || h!=previousApp)return "Open the composer over the browser you want to control.";
        GetWindowThreadProcessId(h,out pid);
        using(var proc=Process.GetProcessById((int)pid)){
            if(Array.IndexOf(new[]{"msedge","chrome","firefox","brave","vivaldi","opera"},proc.ProcessName.ToLowerInvariant())<0)
                return "Bring your browser forward and open Mellow with Ctrl+Alt+M.";
        }
        return PostMessage(h,0x0319,h,new IntPtr(cmd<<16))?"Browser command sent: "+action+".":"Windows declined the browser command.";
    }
    static string DesktopIntent(string message){
        string text=message.Trim().TrimEnd('?');
        if(text.Equals("start watching my screen",StringComparison.OrdinalIgnoreCase))return "/screen watch";
        if(text.Equals("stop watching my screen",StringComparison.OrdinalIgnoreCase))return "/screen stop";
        if(text.Equals("refresh this tab",StringComparison.OrdinalIgnoreCase))return "/browser refresh";
        if(text.Equals("go back in my browser",StringComparison.OrdinalIgnoreCase))return "/browser back";
        if(text.Equals("go forward in my browser",StringComparison.OrdinalIgnoreCase))return "/browser forward";
        if(text.Equals("open notepad",StringComparison.OrdinalIgnoreCase))return "/open notepad";
        if(text.Equals("open calculator",StringComparison.OrdinalIgnoreCase))return "/open calculator";
        const string search="search the web for ",ask="look at my screen: ";
        if(message.StartsWith(ask,StringComparison.OrdinalIgnoreCase))return "/screen ask "+message.Substring(ask.Length);
        if(message.StartsWith(search,StringComparison.OrdinalIgnoreCase))return "/search "+message.Substring(search.Length);
        if(message.StartsWith("open https://",StringComparison.OrdinalIgnoreCase))return "/open "+message.Substring(5);
        return message;
    }
    bool WorkspaceCommand(string message){
        message=DesktopIntent(message);
        string[] parts=message.Split(new[]{' '},2);
        string key=parts[0].ToLowerInvariant(),arg=parts.Length>1?parts[1].Trim():"";
        if(key=="/screen" || message.TrimEnd('?').Equals("what's on my screen",StringComparison.OrdinalIgnoreCase)){
            if(key=="/screen" && arg.StartsWith("ask ",StringComparison.OrdinalIgnoreCase)){
                string question=arg.Substring(4).Trim();
                if(watchBadge==null)LocalReply("Use /screen watch first. The screen badge stays visible while observation is on.");
                else if(question.Length==0 || question.Length>500)LocalReply("Use a screen question of 1 to 500 characters.");
                else SampleScreen(question);
                return true;
            }
            if(arg=="watch"){StartWatching();return true;}
            if(arg=="stop"){StopWatching();LocalReply("Screen observation stopped. The cached description was cleared.");return true;}
            LocalReply(watchBadge==null?"Screen observation is off. Use /screen watch to sample your primary display locally every 12 seconds.":
                observation.Length>0?"Last screen check ("+observedAt.ToString("HH:mm:ss")+"): "+observation:
                watchLabel.Text+". No description yet.");showingScreenReply=watchBadge!=null;return true;
        }
        try{
            if(key=="/browser"){LocalReply(NavigateBrowser(arg.ToLowerInvariant()));return true;}
            if(key=="/open" || key=="/search"){
                Uri url;
                if(key=="/search" && arg.Length>0 && arg.Length<2000)
                    url=new Uri("https://www.google.com/search?q="+Uri.EscapeDataString(arg));
                else if(key=="/open" && (arg=="notepad" || arg=="calculator")){
                    Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,arg=="notepad"?"notepad.exe":"calc.exe")){UseShellExecute=false});
                    LocalReply("Requested "+arg+".");return true;
                }
                else if(key!="/open" || !WebAddress(arg,out url)){
                    LocalReply("Use /open https://example.com, /open notepad, /open calculator or /search words.");return true;
                }
                Process.Start(new ProcessStartInfo(url.AbsoluteUri){UseShellExecute=true});
                LocalReply("Opened "+url.Host+" in your default browser.");return true;
            }
            if(key=="/point"){Later(PointAtControl);return true;}
            if(key=="/close-window"){Later(delegate{SelectWindow(true);});return true;}
            if(key=="/desktop"){LocalReply("/screen watch · /screen · /screen ask QUESTION · /screen stop\n/open https://example.com\n/search terms\n/browser back, forward or refresh\n/open notepad or calculator\n/point · /close-window");return true;}
        }catch(Exception){LocalReply("Windows could not complete that action.");return true;}
        return false;
    }
 }
}
