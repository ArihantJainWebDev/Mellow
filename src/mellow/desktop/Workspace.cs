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
    Task<VisionRead> screenCheck;
    int screenQuestion=-1;
    string screenIssue="";
    bool ScreenQuestionPending {get{return screenQuestion==dialogueVersion;}}

    void StartWatching(){
        if(watchBadge!=null){LocalReply("Primary screen observation is already on.");return;}
        watchGeneration++;screenIssue="";ClearActivity();
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(8,2,4,2)};
        watchLabel=new TextBlock{Text="Screen on · local",VerticalAlignment=VerticalAlignment.Center};
        bar.Children.Add(watchLabel);InitActivityOffer(bar);
        setupHint=Choice("Workspace?",delegate{
            if(waitingSetup!=null && DateTime.UtcNow<offerExpires)
                PresentSetup(waitingSetup,"Restore these familiar windows?","");
        });
        setupHint.Visibility=Visibility.Collapsed;bar.Children.Add(setupHint);
        bar.Children.Add(Choice("Recent",delegate{DescribeActivity(true);}));
        bar.Children.Add(Choice("Stop",StopWatching));
        var area=SystemParameters.WorkArea;
        watchBadge=new Window{Title="Mellow screen observation",ShowActivated=false,ShowInTaskbar=false,
            Topmost=true,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            Background=Ink(245,237,251),SizeToContent=SizeToContent.WidthAndHeight,Left=area.Left+8,Top=area.Top+8,Content=bar};
        watchBadge.Closed+=delegate{
            watchGeneration++;watchBadge=null;ClearActivity();
            if(showingScreenReply)ResetReply();
            if(watchTimer!=null){watchTimer.Stop();watchTimer=null;}
            SystemEvents.SessionSwitch-=OnSessionChange;
        };
        watchBadge.Show();StudyAction("status",null,false);
        if(!offscreenReview)SaveAwarenessPreference(1);
        SystemEvents.SessionSwitch+=OnSessionChange;
        watchTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        watchTimer.Tick+=delegate{PollActivity();PollStudyMusic(DateTime.UtcNow);};watchTimer.Start();PollActivity();
    }
    void OnSessionChange(object sender,SessionSwitchEventArgs e){
        if(e.Reason==SessionSwitchReason.SessionLock || e.Reason==SessionSwitchReason.SessionLogoff ||
           e.Reason==SessionSwitchReason.RemoteDisconnect || e.Reason==SessionSwitchReason.ConsoleDisconnect)
            window.Dispatcher.BeginInvoke(new Action(StopWatching));
    }
    static void SaveAwarenessPreference(int enabled){
        try{using(var key=Registry.CurrentUser.CreateSubKey(Prefs))key.SetValue("DesktopAwareness",enabled);}
        catch(UnauthorizedAccessException){}catch(System.Security.SecurityException){}
    }
    void StopWatching(){
        if(watchBadge!=null){if(!quitting && !offscreenReview)SaveAwarenessPreference(0);watchBadge.Close();}
    }
    static bool CanObserveFocus(){
        try{
            var element=AutomationElement.FocusedElement;
            if(element==null)return false;
            object password=element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty,true);
            uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);
            return password is bool && !(bool)password && pid!=0 && element.Current.ProcessId==(int)pid;
        }catch(System.Runtime.InteropServices.COMException){return false;}
         catch(ElementNotAvailableException){return false;}
         catch(InvalidOperationException){return false;}
         catch(UnauthorizedAccessException){return false;}
    }
    bool CaptureFocus(Activity target,int revision){
        return !inspecting && (chatWindow==null || !chatWindow.IsVisible) &&
            target!=null && GetForegroundWindow()==target.Handle && CanObserveFocus() &&
            SameCapture(target,ReadWindow(target.Handle,true),revision,sceneVersion);
    }
    async void SampleScreen(){
        if(quitting || watchBadge==null || sampling || ScreenQuestionPending || inspecting ||
           (chatWindow!=null && chatWindow.IsVisible))return;
        if(!CanObserveFocus()){InvalidateScene();watchLabel.Text="Screen paused · protected or unavailable focus";return;}
        var target=ReadWindow(GetForegroundWindow(),true);
        if(target==null)return;
        Track(target);
        await RunScreenCheck(current,"",watchGeneration,sceneVersion);
    }
    async Task<VisionRead> RunScreenCheck(Activity target,string question,int generation,int revision){
        var pending=CaptureScreen(target,question,generation,revision);
        screenCheck=pending;
        try{return await pending;}
        finally{if(screenCheck==pending)screenCheck=null;}
    }
    async Task<VisionRead> CaptureScreen(Activity target,string question,int generation,int revision){
        if(watchBadge==null || generation!=watchGeneration || !CaptureFocus(target,revision))return null;
        sampling=true;screenIssue="";nextSnapshot=DateTime.UtcNow.AddSeconds(12);watchLabel.Text="Screen on · checking locally";
        try{
            NativeRect frame;
            if(!GetWindowRect(target.Handle,out frame))throw new IOException();
            var b=Drawing.Rectangle.Intersect(Forms.Screen.FromHandle(target.Handle).Bounds,
                Drawing.Rectangle.FromLTRB(frame.left,frame.top,frame.right,frame.bottom));
            if(b.Width<8 || b.Height<8)throw new IOException();
            DateTime captured=DateTime.UtcNow;
            byte[] image=CaptureArea(new Rect(b.X,b.Y,b.Width,b.Height));
            if(!CaptureFocus(target,revision))return null;
            string digest;
            using(var hash=System.Security.Cryptography.SHA256.Create())digest=Convert.ToBase64String(hash.ComputeHash(image));
            VisionRead result;
            if(question.Length==0 && FreshVision().Length>0 && digest==target.ImageDigest)
                result=new VisionRead{summary=target.Visual,observation=target.Visual};
            else result=await Task.Run(()=>AskVision(image,"screen",question));
            if(watchBadge==null || generation!=watchGeneration || quitting)return null;
            if(!CaptureFocus(target,revision))return null;
            current.Visual=result.observation??"";current.VisualAt=captured;current.ImageDigest=question.Length==0?digest:"";
            watchLabel.Text="Watching "+FriendlyApp(target.App)+" · "+captured.ToLocalTime().ToString("HH:mm:ss");
            return result;
        }catch(Exception){
            if(watchBadge!=null && generation==watchGeneration && revision==sceneVersion){
                current.Visual="";current.ImageDigest="";current.VisualAt=DateTime.MinValue;
                screenIssue="I couldn’t read the screen locally. Check the vision model in LM Studio.";
                watchLabel.Text="Screen on · local vision unavailable";
            }
            return null;
        }finally{sampling=false;}
    }
    async void AnswerScreen(string question,bool activity=false,bool explain=false){
        if(watchBadge==null){LocalReply("Desktop awareness is off. Say “start watching my screen” to enable it.");return;}
        int generation=watchGeneration,dialog=dialogueVersion;
        bool fromComposer=ComposerFocused;
        var target=fromComposer?composerTarget:CanObserveFocus()?ReadWindow(GetForegroundWindow(),true):null;
        if(target==null){LocalReply("I can’t inspect this focus safely. Open a non-sensitive app, then summon me over it.");return;}
        screenQuestion=dialog;
        try{
            if(fromComposer)HideComposer();
            else if(chatWindow!=null && chatWindow.IsVisible)chatWindow.Hide();
            await Task.Delay(180);
            if(quitting || generation!=watchGeneration || dialog!=dialogueVersion)return;
            if(GetForegroundWindow()!=target.Handle || !target.Same(ReadWindow(target.Handle,true))){
                LocalReply("Windows didn’t return to that app. Focus it and summon me again.");return;
            }
            if(!CanObserveFocus()){InvalidateScene();watchLabel.Text="Screen paused · protected or unavailable focus";
                LocalReply("I won’t capture a protected or unavailable focus. Move to a non-sensitive window first.");return;}
            Track(ReadWindow(target.Handle,true));int revision=sceneVersion;
            Signal("think","Checking your screen locally…",35);
            if(screenCheck!=null && !screenCheck.IsCompleted){
                watchLabel.Text="Screen on · waiting for local check";await screenCheck;
            }
            if(quitting || generation!=watchGeneration || dialog!=dialogueVersion)return;
            if(!CaptureFocus(target,revision)){LocalReply("The focused window changed. Ask again over the app you mean.");return;}
            var result=await RunScreenCheck(current,question,generation,revision);
            if(quitting || generation!=watchGeneration || dialog!=dialogueVersion)return;
            if(!CaptureFocus(target,revision)){LocalReply("The focused window changed during the check. I discarded that answer.");return;}
            if(activity){await ReadActivity(explain?"why":"read","",screenIssue);return;}
            LocalReply(result!=null?result.summary:screenIssue.Length>0?screenIssue:"I couldn’t safely capture that window.");
            showingScreenReply=true;
        }finally{if(screenQuestion==dialog)screenQuestion=-1;}
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
        string answer=AskVision(image,"screen","Which word comes after Practice quiz?").summary;
        if(answer.IndexOf("bluebird",StringComparison.OrdinalIgnoreCase)<0)
            throw new InvalidOperationException("The local model did not identify the fixture word.");
        Console.WriteLine("Local screen question passed using a synthetic card.");
    }
    public static string VerifyLiveAwareness(){
        var dispatcher=Dispatcher.CurrentDispatcher;
        var frame=new DispatcherFrame();
        var previous=System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try{
            var check=VerifyLiveAwarenessAsync();
            check.ContinueWith(_=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)));
            Dispatcher.PushFrame(frame);
            check.GetAwaiter().GetResult();
            return "Live Windows awareness passed: screen and task answers, unavailable vision, current track, foreground switch and protected focus.";
        }finally{System.Threading.SynchronizationContext.SetSynchronizationContext(previous);}
    }
    static async Task VerifyLiveAwarenessAsync(){
        var ghost=new Ghost(true,null,false,true);
        var target=new IntPtr(long.Parse(Environment.GetEnvironmentVariable("MELLOW_FIXTURE_HWND")));
        var locked=new IntPtr(long.Parse(Environment.GetEnvironmentVariable("MELLOW_PROTECTED_HWND")));
        if(Caption(target)!="Linear algebra quiz - Awareness verification" ||
           Caption(locked)!="Mellow protected focus fixture")throw new InvalidOperationException("Synthetic windows required.");
        string model=Environment.GetEnvironmentVariable("MELLOW_VISION_MODEL");
        Exception failure=null;
        try{
            SetForegroundWindow(target);
            var startDeadline=DateTime.UtcNow.AddSeconds(2);
            while((GetForegroundWindow()!=target || !CanObserveFocus()) && DateTime.UtcNow<startDeadline)await Task.Delay(50);
            if(GetForegroundWindow()!=target || !CanObserveFocus())throw new InvalidOperationException("Could not focus the synthetic quiz window safely.");
            ghost.StartWatching();ghost.watchTimer.Stop();
            ghost.SampleScreen();
            ghost.SubmitAwarenessFixture(target,"Look at my screen: Which word comes after Practice quiz?");
            await ghost.ExpectAwarenessFixture("bluebird");
            ghost.SubmitAwarenessFixture(target,"What am I doing right now.");
            await ghost.ExpectAwarenessFixture("linear algebra");
            if(ghost.reply.ToString().IndexOf("Other open apps",StringComparison.OrdinalIgnoreCase)>=0)
                throw new InvalidOperationException("Activity answer included an app inventory.");
            Console.WriteLine("Foreground composer: screen question and current task answered from the quiz fixture.");

            Environment.SetEnvironmentVariable("MELLOW_VISION_MODEL","invalid model fixture");
            ghost.SubmitAwarenessFixture(target,"Can you see my screen.");
            await ghost.ExpectAwarenessFixture("couldn’t read the screen locally");
            ghost.SubmitAwarenessFixture(target,"What am I doing?");
            await ghost.ExpectAwarenessFixture("linear algebra","vision model");
            Console.WriteLine("Unavailable vision: screen check reports failure and activity falls back to the current title.");

            ghost.SubmitAwarenessFixture(target,"what i ma listening to.");
            var mediaDeadline=DateTime.UtcNow.AddSeconds(16);
            while(!ghost.finished && DateTime.UtcNow<mediaDeadline)await Task.Delay(50);
            string mediaAnswer=ghost.reply.ToString();
            if(!ghost.finished || (!mediaAnswer.StartsWith("Playing:") && !mediaAnswer.StartsWith("Selected (not playing):") &&
                !mediaAnswer.StartsWith("Windows is not reporting a current media session.")))
                throw new InvalidOperationException("Composer did not report current Windows media availability.");
            Console.WriteLine("Read-only current track query: "+(mediaAnswer.StartsWith("Windows is not reporting")?"no Windows session":"Windows returned a track"));
            ghost.HideComposer();await Task.Delay(180);
            Environment.SetEnvironmentVariable("MELLOW_VISION_MODEL",model);

            ghost.SubmitAwarenessFixture(target,"Can you see my screen?");
            var switchDeadline=DateTime.UtcNow.AddSeconds(3);
            while(!ghost.sampling && DateTime.UtcNow<switchDeadline)await Task.Delay(20);
            if(!ghost.sampling || GetForegroundWindow()!=target || !PostMessage(target,0x8001,new IntPtr(24843),IntPtr.Zero))
                throw new InvalidOperationException("Could not request a fixture switch during inference.");
            while(GetForegroundWindow()==target && DateTime.UtcNow<switchDeadline)await Task.Delay(20);
            if(GetForegroundWindow()!=locked)throw new InvalidOperationException("Could not switch to the protected fixture.");
            await ghost.ExpectAwarenessFixture("changed");
            await Task.Delay(150);
            if(GetForegroundWindow()!=locked || CanObserveFocus())throw new InvalidOperationException("Password focus was not protected.");
            int revision=ghost.sceneVersion;
            ghost.SubmitAwarenessFixture(locked,"What's on my screen?");
            await ghost.ExpectAwarenessFixture("safely");
            if(ghost.sampling || ghost.sceneVersion==revision || ghost.FreshVision().Length>0)
                throw new InvalidOperationException("Sensitive focus retained capture evidence.");
            Console.WriteLine("Foreground switch discarded the pending answer; the password field stayed uncaptured.");
        }catch(Exception error){failure=error;}
        finally{
            Environment.SetEnvironmentVariable("MELLOW_VISION_MODEL",model);
            ghost.StopWatching();ghost.quitting=true;
            if(ghost.chatWindow!=null)ghost.chatWindow.Close();
            if(ghost.replyPopup!=null)ghost.replyPopup.IsOpen=false;
            ghost.window.Close();
        }
        if(ghost.screenCheck!=null)await ghost.screenCheck;
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    void SubmitAwarenessFixture(IntPtr target,string question){
        if(GetForegroundWindow()!=target)throw new InvalidOperationException("Foreground left the synthetic fixture.");
        OpenChat();
        if(!ComposerFocused || !input.IsKeyboardFocused)throw new InvalidOperationException("Composer did not take keyboard focus.");
        input.Text=question;SendInput();
    }
    async Task ExpectAwarenessFixture(params string[] parts){
        var deadline=DateTime.UtcNow.AddSeconds(75);
        while(!finished && DateTime.UtcNow<deadline)await Task.Delay(50);
        foreach(string part in parts)
            if(!finished || reply.ToString().IndexOf(part,StringComparison.OrdinalIgnoreCase)<0){
                IntPtr foreground=GetForegroundWindow();uint pid;GetWindowThreadProcessId(foreground,out pid);
                var app=IdentifyWindow(foreground,pid,false);
                throw new InvalidOperationException("Expected fixture reply containing "+part+"; got: "+reply+
                    " (fixture foreground: "+(foreground==previousApp)+", protected or unknown focus: "+!CanObserveFocus()+
                    ", foreground app: "+(app==null?"unknown":app.App)+")");
            }
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
        string text=message.Trim().TrimEnd('?','.','!');
        string question=System.Text.RegularExpressions.Regex.Replace(message.Replace('’','\'').Trim().TrimEnd('?','.','!'),@"\s+"," ").ToLowerInvariant();
        question=System.Text.RegularExpressions.Regex.Replace(question,@"^(?:please |can you tell me |could you tell me |tell me )+","");
        question=System.Text.RegularExpressions.Regex.Replace(question,@"(?: right now| now| at the moment| currently| rn| on my computer| on my laptop| please)+$","");
        question=question.Replace("i'm","i am").Replace("what's","what is");
        if(System.Text.RegularExpressions.Regex.IsMatch(question,@"^(?:(?:(?:can you tell|do you know) )?what (?:am i|i am|i ma|i) (?:currently )?(?:doing|working on)|what is my current (?:activity|task)|what are we working on)$"))return "/activity";
        if(System.Text.RegularExpressions.Regex.IsMatch(question,@"^(?:what (?:am i|i am|i ma|i) (?:currently )?listening to|what (?:music|song|track) am i (?:currently )?listening to|what (?:is )?playing|(?:what|which) (?:song|music|track) is (?:this|playing))$"))return "/music current";
        if(System.Text.RegularExpressions.Regex.IsMatch(question,@"^(?:can you see (?:my screen|what i am doing)|what (?:can|do) you see (?:on )?(?:my|the) screen|what does my screen show|describe (?:my|the) (?:screen|focused window)|what is on (?:my|the) screen)$"))
            return "/screen ask Describe the visible activity and task, using only legible evidence.";
        if(System.Text.RegularExpressions.Regex.IsMatch(question,@"^(?:what (?:apps|applications|windows) (?:do i have open|are open)|what do i have open)$"))return "/open-apps";
        if(question=="why do you think that" || question=="why do you think i am doing that" ||
           question=="what am i doing, and what makes you think that" || question=="what am i doing, and why")return "/activity why";
        const string screenPrefix="look at my screen: ";
        if(message.TrimStart().StartsWith(screenPrefix,StringComparison.OrdinalIgnoreCase))return "/screen ask "+message.TrimStart().Substring(screenPrefix.Length);
        if(System.Text.RegularExpressions.Regex.IsMatch(question,@"^(?:(?:can|could) you )?(?:look at|check|read) (?:my|the) screen(?:[,:] .+| and .+| for .+)$") ||
           System.Text.RegularExpressions.Regex.IsMatch(question,@"^(?:what|which) (?:\w+ ){1,4}(?:is|are|do you see) (?:visible |shown |displayed )?(?:on (?:my|the) screen|in (?:my|the) focused window)$"))return "/screen ask "+message.Trim();
        if(text.Equals("where was I",StringComparison.OrdinalIgnoreCase))return "/recent-apps";
        if(text.Equals("take me back",StringComparison.OrdinalIgnoreCase))return "/back-to";
        const string back="take me back to ";
        if(text.StartsWith(back,StringComparison.OrdinalIgnoreCase))return "/back-to "+text.Substring(back.Length).Trim();
        if(text.Equals("remember my setup",StringComparison.OrdinalIgnoreCase))return "/save-setup";
        if(text.Equals("restore my setup",StringComparison.OrdinalIgnoreCase))return "/restore-setup";
        if(text.Equals("skip this setup",StringComparison.OrdinalIgnoreCase))return "/skip-setup";
        if(text.Equals("start watching my screen",StringComparison.OrdinalIgnoreCase))return "/screen watch";
        if(text.Equals("stop watching my screen",StringComparison.OrdinalIgnoreCase))return "/screen stop";
        if(text.Equals("refresh this tab",StringComparison.OrdinalIgnoreCase))return "/browser refresh";
        if(text.Equals("go back in my browser",StringComparison.OrdinalIgnoreCase))return "/browser back";
        if(text.Equals("go forward in my browser",StringComparison.OrdinalIgnoreCase))return "/browser forward";
        if(text.Equals("open notepad",StringComparison.OrdinalIgnoreCase))return "/open notepad";
        if(text.Equals("open calculator",StringComparison.OrdinalIgnoreCase))return "/open calculator";
        const string search="search the web for ";
        if(message.StartsWith(search,StringComparison.OrdinalIgnoreCase))return "/search "+message.Substring(search.Length);
        if(message.StartsWith("open https://",StringComparison.OrdinalIgnoreCase))return "/open "+message.Substring(5);
        return message;
    }
    bool WorkspaceCommand(string message){
        message=DesktopIntent(message);
        if(RecognitionCommand(message) || SetupCommand(message) || StudyCommand(message))return true;
        string[] parts=message.Split(new[]{' '},2);
        string key=parts[0].ToLowerInvariant(),arg=parts.Length>1?parts[1].Trim():"";
        if(key=="/activity"){AnswerScreen("",true,arg=="why");return true;}
        if(key=="/recent-apps"){DescribeActivity(true);return true;}
        if(key=="/music" && arg=="current"){StudyMedia("current");return true;}
        if(key=="/open-apps"){
            if(watchBadge==null){LocalReply("Desktop awareness is off. Say “start watching my screen” to enable it.");return true;}
            LocalReply(BackgroundApps(current));showingScreenReply=true;return true;
        }
        if(key=="/back-to"){RewindTo(arg);return true;}
        if(key=="/save-setup"){SavedSetup("save","default","");return true;}
        if(key=="/restore-setup"){
            SavedSetup("load","default","");
            return true;
        }
        if(key=="/skip-setup"){proposed=null;LocalReply("Okay, carry on.");return true;}
        if(key=="/screen"){
            if(key=="/screen" && arg.StartsWith("ask ",StringComparison.OrdinalIgnoreCase)){
                string question=arg.Substring(4).Trim();
                if(watchBadge==null)LocalReply("Use /screen watch first. The screen badge stays visible while observation is on.");
                else if(question.Length==0 || question.Length>500)LocalReply("Use a screen question of 1 to 500 characters.");
                else AnswerScreen(question);
                return true;
            }
            if(arg=="watch"){StartWatching();return true;}
            if(arg=="stop"){StopWatching();LocalReply("Screen observation stopped. The cached description was cleared.");return true;}
            if(arg=="status"){
                string visual=FreshVision();
                LocalReply(watchBadge==null?"Desktop awareness is off.":visual.Length>0?
                    "Last local screen check ("+current.VisualAt.ToLocalTime().ToString("HH:mm:ss")+"): "+visual:watchLabel.Text+". No fresh screen description.");
                showingScreenReply=watchBadge!=null;return true;
            }
            AnswerScreen("Describe the visible activity and task, using only legible evidence.");return true;
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
            if(key=="/desktop"){LocalReply("What am I doing? · What am I listening to? · Where was I?\nTake me back · Remember my setup · Restore my setup\n/screen watch · /screen · /screen status · /screen ask QUESTION · /screen stop\n/open https://example.com\n/search terms\n/browser back, forward or refresh\n/open notepad or calculator\n/point · /close-window");return true;}
        }catch(Exception){LocalReply("Windows could not complete that action.");return true;}
        return false;
    }
 }
}
