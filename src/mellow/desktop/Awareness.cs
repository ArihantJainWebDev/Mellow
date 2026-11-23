using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace MellowDesktop {
 public sealed partial class Ghost {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd,StringBuilder buffer,int length);
    delegate bool WindowVisitor(IntPtr handle,IntPtr unused);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor visitor,IntPtr unused);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr handle,int command);

    sealed class Activity {
        internal IntPtr Handle;
        internal uint Pid;
        internal string App, Title, Address, Visual;
        internal DateTime At;
        internal string Label {get{return FriendlyApp(App)+(Title.Length==0?"":" · "+Title);}}
        internal bool Same(Activity other){
            return other!=null && Handle==other.Handle && Pid==other.Pid && Title==other.Title;
        }
    }
    readonly List<Activity> visits=new List<Activity>();
    Activity current;
    Activity[] proposed,waitingSetup;
    System.Windows.Controls.Button setupHint;
    double aim,aimY,eyeAim,eyeAimY,headAim;
    System.Drawing.Point lastAttentionPointer;
    DateTime pointerMoved;
    int sceneVersion;
    DateTime nextSnapshot,offerExpires;
    string lastOffer="";
    static string Caption(IntPtr h){
        var title=new StringBuilder(200);GetWindowText(h,title,title.Capacity);
        return title.ToString().Replace('\r',' ').Replace('\n',' ').Trim();
    }
    Activity ReadWindow(IntPtr h,bool readTitle){
        if(h==IntPtr.Zero || !IsWindowVisible(h) || h==GetShellWindow() || h==FindWindow("Shell_TrayWnd",null))return null;
        uint pid;GetWindowThreadProcessId(h,out pid);
        if(pid==0 || pid==ownPid)return null;
        return IdentifyWindow(h,pid,readTitle);
    }
    static Activity IdentifyWindow(IntPtr h,uint pid,bool readTitle){
        try{
            using(var process=Process.GetProcessById((int)pid)){
                string title=readTitle?Caption(h):"";
                return new Activity{Handle=h,Pid=pid,App=process.ProcessName,Title=title,At=DateTime.Now};
            }
        }catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
         catch(System.ComponentModel.Win32Exception){return null;}
    }
    static readonly int ownPid=CurrentPid();
    static int CurrentPid(){using(var self=Process.GetCurrentProcess())return self.Id;}
    void InvalidateScene(){
        evidenceStart=DateTime.Now;ClearActivityOffer();current=null;observation="";sceneVersion++;aim=aimY=0;
    }
    void ClearActivity(){
        InvalidateScene();visits.Clear();proposed=waitingSetup=null;lastOffer="";
        if(setupHint!=null)setupHint.Visibility=Visibility.Collapsed;eyeAim=eyeAimY=headAim=0;
    }
    void Track(Activity item){
        if(item.Same(current))return;
        ClearActivityOffer();lastChange=DateTime.UtcNow;current=item;sceneVersion++;observation="";nextSnapshot=DateTime.UtcNow.AddSeconds(2);
        if(visits.Count>0 && item.Same(visits[visits.Count-1]))return;
        visits.Add(item);
        if(visits.Count>48)visits.RemoveAt(0);
    }
    // Recognize A-B-C, A-B-C, then A. Require three distinct windows and recent evidence.
    Activity[] RepeatedSetup(){
        int n=visits.Count;if(n<7)return null;
        var anchor=visits[n-1];
        if(anchor.At-visits[n-7].At>TimeSpan.FromHours(2))return null;
        for(int i=n-7;i<n-3;i++)if(!visits[i].Same(visits[i+3]))return null;
        var b=visits[n-3];var c=visits[n-2];
        if(anchor.Handle==b.Handle || b.Handle==c.Handle || anchor.Handle==c.Handle)return null;
        return new[]{anchor,b,c};
    }
    void OfferSetup(){
        var setup=RepeatedSetup();
        if(setup==null || proposed!=null)return;
        string key=setup[0].Label+"|"+setup[1].Label+"|"+setup[2].Label;
        if(key==lastOffer)return;
        lastOffer=key;
        if(reading || quiet || state!="idle"){
            waitingSetup=setup;offerExpires=DateTime.UtcNow.AddMinutes(2);
            if(setupHint!=null)setupHint.Visibility=Visibility.Visible;
            return;
        }
        PresentSetup(setup,"You keep coming back to these windows. Restore this setup?","");
    }
    void PollActivity(){
        if(watchBadge==null || quitting)return;
        if(waitingSetup!=null && DateTime.UtcNow>=offerExpires){
            waitingSetup=null;if(setupHint!=null)setupHint.Visibility=Visibility.Collapsed;
        }
        if(proposed!=null && DateTime.UtcNow>=offerExpires){
            proposed=null;if(reading && dialogueVersion==offerDialogue)DismissReply();
        }
        if(inspecting || (studyWindow!=null && studyWindow.IsActive) || (chatWindow!=null && chatWindow.IsActive)){
            aim=aimY=0;return;
        }
        if(!CanObserveFocus()){
            if(current!=null)InvalidateScene();
            watchLabel.Text="Screen paused · focus";return;
        }
        var item=ReadWindow(GetForegroundWindow(),true);
        if(item==null){if(current!=null)InvalidateScene();return;}
        bool changed=!item.Same(current);
        Track(item);PollStudy(item,DateTime.UtcNow);ConsiderRecognition();
        Rect r=RectFor(item.Handle);
        if(!r.IsEmpty){
            var focus=new Point(r.Left+r.Width/2,r.Top+r.Height/2);
            var raw=System.Windows.Forms.Cursor.Position;
            if(raw!=lastAttentionPointer){lastAttentionPointer=raw;pointerMoved=DateTime.UtcNow;}
            var source=PresentationSource.FromVisual(window);
            if(source!=null && (DateTime.UtcNow-pointerMoved).TotalSeconds<3){
                Point pointer=source.CompositionTarget.TransformFromDevice.Transform(new Point(raw.X,raw.Y));
                if(r.Contains(pointer))focus=pointer;
            }
            aim=Clamp((focus.X-window.Left-window.Width/2)/Math.Max(1,SystemParameters.PrimaryScreenWidth*.32),-1,1);
            aimY=Clamp((window.Top+window.Height-size*.65-focus.Y)/Math.Max(1,SystemParameters.PrimaryScreenHeight*.55),-1,1);
        }
        if(changed){watchLabel.Text="Noticing "+item.App;OfferSetup();}
        else if(!sampling && observation.Length==0 && DateTime.UtcNow<nextSnapshot && watchLabel.Text=="Screen paused · focus")
            watchLabel.Text="Noticing "+item.App;
        if(!sampling && DateTime.UtcNow>=nextSnapshot){
            nextSnapshot=DateTime.UtcNow.AddSeconds(12);SampleScreen();
        }
    }
    string BackgroundApps(Activity foreground){
        var names=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EnumWindows(delegate(IntPtr h,IntPtr unused){
            if(h==foreground.Handle || !IsWindowVisible(h) || (GetWindowLong(h,-20)&0x80)!=0)return true;
            var other=ReadWindow(h,false);
            if(other!=null && other.App!=foreground.App && seen.Add(other.App))names.Add(other.App);
            return names.Count<12;
        },IntPtr.Zero);
        return names.Count==0?"":"\nOther open apps: "+String.Join(", ",names.ToArray())+".";
    }
    void DescribeActivity(bool recent){
        if(watchBadge==null){LocalReply("Desktop awareness is off. Say “start watching my screen” to enable it.");return;}
        if(recent){
            if(visits.Count==0){LocalReply("I haven’t observed an app yet.");return;}
            var lines=new List<string>();
            for(int i=Math.Max(0,visits.Count-5);i<visits.Count;i++)lines.Add(visits[i].At.ToString("HH:mm")+"  "+visits[i].Label);
            LocalReply("Your recent windows\n"+String.Join("\n",lines.ToArray()));showingScreenReply=true;return;
        }
        Recognize("read","");
    }
    bool setupIO;
    int offerDialogue;
    static string EncodeField(string value){return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));}
    static string DecodeField(string value){return Encoding.UTF8.GetString(Convert.FromBase64String(value));}
    static List<Activity> SetupStore(string action,List<Activity> items,string profile){
        var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("MELLOW_PYTHON"),"-m mellow.workspace_memory "+action+" "+EncodeField(profile)){
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true};
        using(var proc=Process.Start(info)){
            foreach(var item in items)proc.StandardInput.WriteLine(EncodeField(item.App)+" "+EncodeField(item.Title));
            proc.StandardInput.Close();
            var saved=new List<Activity>();string line;
            while((line=proc.StandardOutput.ReadLine())!=null){
                string[] fields=line.Split(' ');
                saved.Add(new Activity{App=DecodeField(fields[0]),Title=DecodeField(fields[1])});
            }
            proc.WaitForExit();if(proc.ExitCode!=0)throw new IOException();
            return saved;
        }
    }
    List<Activity> RecentWorkspace(){
        var selected=new List<Activity>();var seen=new HashSet<IntPtr>();
        for(int i=visits.Count-1;i>=0 && selected.Count<3;i--){
            var item=visits[i];
            if(DateTime.Now-item.At>TimeSpan.FromMinutes(45))break;
            if(item.Title.Length>0 && seen.Add(item.Handle) && item.Same(ReadWindow(item.Handle,true)))selected.Add(item);
        }
        return selected;
    }
    async void SavedSetup(string action,string profile,string link,string browser=""){
        if(setupIO){LocalReply("One workspace request is already running.");return;}
        if(!ProfileName(profile)){LocalReply("Use a workspace name of 1–48 letters, numbers, spaces, dots or dashes.");return;}
        if(watchBadge==null && (action=="save" || action=="load")){LocalReply("Enable desktop awareness first.");return;}
        var items=action=="save"?RecentWorkspace():new List<Activity>();
        if(action=="save" && items.Count==0){LocalReply("Visit your project windows first.");return;}
        if(action=="pin" || action=="unpin"){
            Uri url;
            if(link.Length>1800 || !WebAddress(link,out url)){LocalReply("Use an HTTPS link without a username or password.");return;}
            items.Add(new Activity{App=browser.Length>0?"@link:"+browser:"@link",Title=url.AbsoluteUri});
        }
        int generation=watchGeneration,version=dialogueVersion;setupIO=true;
        try{
            var saved=await Task.Run(()=>SetupStore(action=="show"?"load":action,items,profile));
            if(quitting || watchGeneration!=generation || version!=dialogueVersion)return;
            string label=profile=="default"?"Default workspace":profile;
            string content=String.Join("\n",saved.ConvertAll(x=>IsLink(x)?x.Title:x.Label).ToArray());
            if(action=="list"){LocalReply(saved.Count==0?"No saved setups.":String.Join("\n",saved.ConvertAll(x=>x.Title).ToArray()));return;}
            if(action=="forget"){LocalReply("Cleared setup: "+label+".");return;}
            if(action!="load"){LocalReply(label+"\n"+(content.Length>0?content:"No saved entries."));showingScreenReply=true;return;}
            if(saved.Count==0){LocalReply("No saved entries for "+label+".");return;}
            var live=new List<Activity>();
            EnumWindows(delegate(IntPtr h,IntPtr unused){
                var item=ReadWindow(h,true);if(item!=null)live.Add(item);return true;
            },IntPtr.Zero);
            if(saved.Exists(IsLink)){
                await Task.Run(()=>MatchOpenLinks(live,saved));
                if(quitting || watchGeneration!=generation || version!=dialogueVersion)return;
            }
            var missing=new List<string>();
            var matches=PlanWorkspace(saved,live,missing);
            string unavailable=missing.Count>0?"\nUnavailable or ambiguous:\n"+String.Join("\n",missing.ToArray()):"";
            if(matches.Count==0){LocalReply("No available targets."+unavailable);showingScreenReply=true;return;}
            PresentSetup(matches.ToArray(),"Bring back "+label+"?",unavailable);
        }catch(Exception){if(!quitting && generation==watchGeneration)LocalReply("Workspace update failed. Check the saved data and the eight-link limit.");}
        finally{setupIO=false;}
    }
    void PresentSetup(Activity[] setup,string heading,string suffix){
        waitingSetup=null;if(setupHint!=null)setupHint.Visibility=Visibility.Collapsed;
        proposed=setup;offerExpires=DateTime.UtcNow.AddMinutes(2);
        var names=Array.ConvertAll(setup,PlanLabel);
        LocalReply(heading+"\n"+String.Join("\n",names)+suffix);
        showingScreenReply=true;offerDialogue=dialogueVersion;PaintSetupChoices();
    }
    static string FriendlyApp(string app){
        switch(app.ToLowerInvariant()){
            case "code":return "VS Code";
            case "msedge":return "Microsoft Edge";
            case "opera":return "Opera";
            case "chrome":return "Chrome";
            case "chatgpt":return "ChatGPT";
            default:return app;
        }
    }
    void RewindTo(string name){
        if(watchBadge==null){LocalReply("Enable awareness and use a few windows first.");return;}
        for(int i=visits.Count-1;i>=0;i--){
            Activity target=visits[i];
            if(name.Length==0 && target.Handle==previousApp)continue;
            if(name.Length>0 && !String.Equals(target.App,name,StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(FriendlyApp(target.App),name,StringComparison.OrdinalIgnoreCase))continue;
            if(!target.Same(ReadWindow(target.Handle,true)))continue;
            if(IsIconic(target.Handle))ShowWindowAsync(target.Handle,9);
            bool accepted=SetForegroundWindow(target.Handle);
            LocalReply(accepted?"Back in "+target.Label+".":"Windows kept the current app in front.");
            showingScreenReply=true;return;
        }
        LocalReply("I couldn’t find a matching recent window that is still open with the same title.");
    }
    void RestoreProposed(){
        var targets=proposed;proposed=null;
        if(targets==null || DateTime.UtcNow>=offerExpires){LocalReply("There is no current workspace offer.");return;}
        foreach(var t in targets){
            Uri link;
            bool valid=t.Handle!=IntPtr.Zero?t.Same(ReadWindow(t.Handle,true)):
                IsLink(t)?WebAddress(t.Title,out link):KnownExecutable(t.App)!=null;
            if(!valid){LocalReply("A proposed target changed. Ask me to preview the workspace again.");return;}
        }
        int opened=0,windows=0;var failures=new List<string>();
        foreach(var t in targets){
            try{
                if(t.Handle==IntPtr.Zero){
                    if(IsLink(t))OpenWorkspaceLink(t);
                    else Process.Start(new ProcessStartInfo(KnownExecutable(t.App)){UseShellExecute=false});
                    opened++;
                }else{
                    if(IsIconic(t.Handle))ShowWindowAsync(t.Handle,9);
                    if(SetForegroundWindow(t.Handle))windows++;else failures.Add(FriendlyApp(t.App)+" could not take focus");
                }
            }catch(Exception){failures.Add("Could not open "+t.Label);}
        }
        if(targets[0].Handle!=IntPtr.Zero)SetForegroundWindow(targets[0].Handle);
        LocalReply("Workspace requested. Windows focused: "+windows+". Apps or tabs requested: "+opened+"."+
            (failures.Count>0?"\n"+String.Join("\n",failures.ToArray()):""));
    }
    static bool SameCapture(Activity started,Activity ended,int version,int liveVersion){
        return started!=null && started.Same(ended) && version==liveVersion;
    }
    static void VerifyAwareness(){
        var ghost=new Ghost(true,null,false,true);
        Activity[] apps=new Activity[3];
        for(int i=0;i<3;i++)apps[i]=new Activity{Handle=new IntPtr(i+1),Pid=(uint)i+10,App="fixture"+i,Title="Project "+i,At=DateTime.Now};
        for(int i=0;i<6;i++)ghost.Track(apps[i%3]);
        if(ghost.RepeatedSetup()!=null)throw new InvalidOperationException("Premature setup offer");
        ghost.Track(apps[0]);
        if(ghost.RepeatedSetup()==null || SameCapture(apps[0],apps[1],1,1) || SameCapture(apps[0],apps[0],1,3))
            throw new InvalidOperationException("Workspace sequence or stale image guard");
        ghost.ClearActivity();
        if(ghost.visits.Count!=0 || ghost.current!=null)throw new InvalidOperationException("Activity retained after Stop");
        // Check the native identity path with a disposable window outside the virtual desktop.
        var fixture=TestLedge(new Rect(SystemParameters.VirtualScreenLeft-200,SystemParameters.VirtualScreenTop-200,80,50));
        try{
            var h=new System.Windows.Interop.WindowInteropHelper(fixture).Handle;
            uint pid;GetWindowThreadProcessId(h,out pid);
            var before=IdentifyWindow(h,pid,true);
            if(before==null || before.Title!=fixture.Title || ghost.ReadWindow(h,true)!=null)
                throw new InvalidOperationException("Native window identity or self filtering");
            fixture.Title="A different project";
            if(SameCapture(before,IdentifyWindow(h,pid,true),1,1))
                throw new InvalidOperationException("Renamed window reused an earlier result");
        }finally{fixture.Close();}
        Console.WriteLine("Awareness checks passed: repeated workspace, stale capture rejection, stop cleanup.");
    }
 }
}
