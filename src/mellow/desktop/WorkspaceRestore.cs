using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace MellowDesktop {
 public sealed partial class Ghost {
    static bool ProfileName(string text){
        return Regex.IsMatch(text,@"^[\w][\w .-]{0,47}$");
    }
    static string KnownExecutable(string app){
        string[] candidates;
        switch(app.ToLowerInvariant()){
            case "notepad":return Path.Combine(Environment.SystemDirectory,"notepad.exe");
            case "calculator":case "calculatorapp":return Path.Combine(Environment.SystemDirectory,"calc.exe");
            case "code":candidates=new[]{"Programs/Microsoft VS Code/Code.exe","Microsoft VS Code/Code.exe"};break;
            case "opera":candidates=new[]{"Programs/Opera/launcher.exe","Programs/Opera/opera.exe","Opera/launcher.exe","Opera/opera.exe"};break;
            case "chrome":candidates=new[]{"Google/Chrome/Application/chrome.exe"};break;
            case "msedge":candidates=new[]{"Microsoft/Edge/Application/msedge.exe"};break;
            case "brave":candidates=new[]{"BraveSoftware/Brave-Browser/Application/brave.exe"};break;
            case "vivaldi":candidates=new[]{"Vivaldi/Application/vivaldi.exe"};break;
            case "firefox":candidates=new[]{"Mozilla Firefox/firefox.exe"};break;
            default:return null;
        }
        foreach(var root in new[]{Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.ProgramFilesX86}){
            string folder=Environment.GetFolderPath(root);
            if(folder.Length==0)continue;
            foreach(string candidate in candidates){
                string path=Path.GetFullPath(Path.Combine(folder,candidate));
                if(File.Exists(path))return path;
            }
        }
        return null;
    }
    static List<Activity> PlanWorkspace(List<Activity> saved,List<Activity> live,List<string> missing){
        var targets=new List<Activity>();
        var launching=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in saved){
            if(IsLink(entry)){
                Uri url;
                if(WebAddress(entry.Title,out url)){
                    var opened=live.Find(x=>x.Address==url.AbsoluteUri && (entry.App=="@link" || entry.App=="@link:"+x.App));
                    if(opened!=null){if(!targets.Exists(t=>t.Handle==opened.Handle))targets.Add(opened);}
                    else targets.Add(new Activity{App=entry.App,Title=url.AbsoluteUri});
                }
                else missing.Add("Invalid saved web address");
                continue;
            }
            var exact=live.FindAll(x=>String.Equals(x.App,entry.App,StringComparison.OrdinalIgnoreCase) && x.Title==entry.Title);
            if(exact.Count==1){if(!targets.Exists(t=>t.Handle==exact[0].Handle))targets.Add(exact[0]);continue;}
            bool appOpen=live.Exists(x=>String.Equals(x.App,entry.App,StringComparison.OrdinalIgnoreCase));
            if(!appOpen && KnownExecutable(entry.App)!=null){
                if(launching.Add(entry.App))targets.Add(new Activity{App=entry.App,Title="Open application"});
            }else missing.Add(entry.Label);
        }
        return targets;
    }
    static bool IsLink(Activity t){return t.App=="@link" || t.App.StartsWith("@link:",StringComparison.Ordinal);}
    static string LinkBrowser(Activity t){
        if(!t.App.StartsWith("@link:",StringComparison.Ordinal))return "";
        string id=t.App.Substring(6);
        return Array.IndexOf(new[]{"opera","chrome","msedge","firefox","brave","vivaldi"},id)>=0 && KnownExecutable(id)!=null?id:"";
    }
    static void OpenWorkspaceLink(Activity t){
        Uri url;if(!WebAddress(t.Title,out url))throw new InvalidOperationException("Invalid link");
        string browser=LinkBrowser(t);
        if(browser.Length==0)Process.Start(new ProcessStartInfo(url.AbsoluteUri){UseShellExecute=true});
        else Process.Start(new ProcessStartInfo(KnownExecutable(browser),url.AbsoluteUri){UseShellExecute=false});
    }
    static void MatchOpenLinks(List<Activity> windows,List<Activity> saved){
        int checkedWindows=0;
        foreach(var item in windows){
            if(Array.IndexOf(new[]{"opera","chrome","msedge","firefox","brave","vivaldi"},item.App.ToLowerInvariant())<0 ||
               !saved.Exists(t=>IsLink(t) && (t.App=="@link" || t.App=="@link:"+item.App)))continue;
            if(++checkedWindows>4)break;
            try{item.Address=AddressFromChrome(AutomationElement.FromHandle(item.Handle));}
            catch(System.Runtime.InteropServices.COMException){}catch(InvalidOperationException){}catch(UnauthorizedAccessException){}
        }
    }
    static string PlanLabel(Activity t){
        if(t.Handle!=IntPtr.Zero)return "Restore   "+t.Label;
        if(IsLink(t)){
            string browser=LinkBrowser(t);
            return "Open tab"+(browser.Length>0?" in "+FriendlyApp(browser):" in default browser")+"   "+t.Title;
        }
        return "Launch   "+FriendlyApp(t.App);
    }
    static string AddressFromChrome(AutomationElement root){
        var walk=TreeWalker.ControlViewWalker;
        var queue=new Queue<Tuple<AutomationElement,bool>>();queue.Enqueue(Tuple.Create(root,false));
        var timer=Stopwatch.StartNew();int inspected=0,queued=1;
        while(queue.Count>0 && inspected++<360 && timer.ElapsedMilliseconds<1500){
            var pair=queue.Dequeue();var item=pair.Item1;var kind=item.Current.ControlType;
            if(kind==ControlType.Document)continue;
            bool toolbar=pair.Item2 || kind==ControlType.ToolBar;
            if(toolbar && kind==ControlType.Edit && !item.Current.IsPassword && !item.Current.IsOffscreen){
                string name=item.Current.Name??"",id=item.Current.AutomationId??"";
                bool address=id.Equals("addressEditBox",StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(name,@"^(Address and search bar|Address bar|Search or enter web address|Search with .+ or enter address)$",RegexOptions.IgnoreCase);
                object raw;
                if(address && item.TryGetCurrentPattern(ValuePattern.Pattern,out raw)){
                    string value=((ValuePattern)raw).Current.Value;Uri url;
                    if(value!=null && value.Length<=1800 && WebAddress(value,out url) && url.Fragment.Length==0 &&
                       (url.Query.Length==0 || SongLink(url.AbsoluteUri)))return url.AbsoluteUri;
                    return null;
                }
            }
            for(var child=walk.GetFirstChild(item);child!=null && queued<360;child=walk.GetNextSibling(child)){
                queue.Enqueue(Tuple.Create(child,toolbar));queued++;
            }
        }
        return null;
    }
    bool pinBusy;
    async void PinBrowserPage(string profile){
        if(!ProfileName(profile)){LocalReply("Choose a short setup name.");return;}
        if(pinBusy){LocalReply("Already checking the address bar.");return;}
        var target=ReadWindow(previousApp,true);
        if(target==null || Array.IndexOf(new[]{"chrome","msedge","opera","brave","firefox","vivaldi"},target.App.ToLowerInvariant())<0){
            LocalReply("Summon Mellow over the browser page, or paste: pin HTTPS-LINK to NAME setup.");return;
        }
        pinBusy=true;int version=dialogueVersion;
        try{
            string url=await Task.Run(()=>AddressFromChrome(AutomationElement.FromHandle(target.Handle)));
            if(quitting || dialogueVersion!=version)return;
            if(url==null || !target.Same(ReadWindow(target.Handle,true))){
                LocalReply("I couldn’t verify a plain HTTPS address in that browser toolbar. Paste the exact link to pin it.");return;
            }
            SavedSetup("pin",profile,url,target.App.ToLowerInvariant());
        }catch(Exception){if(!quitting && version==dialogueVersion)LocalReply("The browser did not expose its address. You can pin a pasted link.");}
        finally{pinBusy=false;}
    }
    bool SetupCommand(string message){
        string text=message.Trim();
        if(text.Equals("list my setups",StringComparison.OrdinalIgnoreCase) || text=="/setups"){
            SavedSetup("list","default","");return true;
        }
        var named=Regex.Match(text,@"^(remember my setup as|restore setup|show setup|forget setup)\s+(.+)$",RegexOptions.IgnoreCase);
        if(named.Success){
            string verb=named.Groups[1].Value.ToLowerInvariant();
            string action=verb=="remember my setup as"?"save":verb=="restore setup"?"load":verb=="show setup"?"show":"forget";
            SavedSetup(action,named.Groups[2].Value.Trim(),"");return true;
        }
        var page=Regex.Match(text,@"^pin this page to (.+?) setup$",RegexOptions.IgnoreCase);
        if(page.Success){PinBrowserPage(page.Groups[1].Value.Trim());return true;}
        var link=Regex.Match(text,@"^(pin|unpin)\s+(\S+)\s+(?:to|from)\s+(.+?)\s+setup$",RegexOptions.IgnoreCase);
        if(link.Success){
            SavedSetup(link.Groups[1].Value.ToLowerInvariant(),link.Groups[3].Value.Trim(),link.Groups[2].Value);
            return true;
        }
        return false;
    }
 }
}
