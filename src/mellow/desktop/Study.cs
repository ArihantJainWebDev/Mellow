using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MellowDesktop {
 public sealed partial class Ghost {
    public sealed class StudyTrack {public long at; public string title,artist;}
    public sealed class StudyDue {public int id;public string title,due;}
    public sealed class StudyMoment {public int id;public long at;public string name,app,title,description,track,artist,mix;}
    public sealed class PastStudy {public long started,ended;public string topic;}
    public sealed class StudyView {
        public string error,note,launch,mix,app,cue,source,topic;
        public PastStudy[] sessions;
        public int session,auto_skip; public long started,ended; public bool active,avoid;
        public string[] mixes;public StudyTrack[] tracks;public StudyDue[] due;public StudyMoment[] moments;
    }
    public sealed class MediaView {public bool available,playing,changed;public string source,title,artist,message;}
    StudyView study;
    bool studyBusy,mediaBusy,studyCueLatched;
    DateTime cueSince,lastCueTick,nextMediaCheck;
    string lastRecordedTitle="",lastRecordedArtist="",lastRecordedSource="";
    int lastRecordedSession;
    DateTime lastAutomaticSkip;
    static readonly DateTime Epoch=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc);

    static T ReadFramed<T>(ProcessStartInfo info,string payload,int timeout){
        info.UseShellExecute=false;info.CreateNoWindow=true;
        info.RedirectStandardInput=true;info.RedirectStandardOutput=true;
        using(var proc=Process.Start(info)){
            var read=proc.StandardOutput.ReadToEndAsync();
            proc.StandardInput.WriteLine(payload);proc.StandardInput.Close();
            if(!proc.WaitForExit(timeout)){try{proc.Kill();}catch(InvalidOperationException){}throw new IOException("Helper timed out");}
            if(proc.ExitCode!=0)throw new IOException("Helper unavailable");
            string json=DecodeField(read.GetAwaiter().GetResult().Trim());
            return new JavaScriptSerializer().Deserialize<T>(json);
        }
    }
    static StudyView StudyStore(string action,Dictionary<string,object> fields){
        fields["action"]=action;
        var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("MELLOW_PYTHON"),"-m mellow.study");
        return ReadFramed<StudyView>(info,EncodeField(new JavaScriptSerializer().Serialize(fields)),12000);
    }
    static MediaView MediaRequest(string action,string source,string expectedTitle="",string expectedArtist=""){
        string path=Path.Combine(Environment.GetEnvironmentVariable("MELLOW_DESKTOP_DIR"),"media.ps1");
        var info=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+path+"\" -Action "+action);
        return ReadFramed<MediaView>(info,EncodeField(source??"")+"\n"+EncodeField(expectedTitle)+"\n"+EncodeField(expectedArtist??""),14000);
    }
    static bool SongLink(string value){
        Uri url;
        return value!=null && WebAddress(value,out url) &&
            Regex.IsMatch(url.AbsoluteUri,@"^https://music\.youtube\.com/(watch\?v=[A-Za-z0-9_-]{11}|playlist\?list=[A-Za-z0-9_-]{5,120})$");
    }
    static string StudySummary(StudyView s){
        var lines=new List<string>();
        if(s.active){
            int minutes=(int)Math.Max(0,(DateTime.UtcNow-Epoch.AddSeconds(s.started)).TotalMinutes);
            lines.Add("Studying · "+minutes+" min");
        }else lines.Add("Study desk · ready");
        if(s.active && !String.IsNullOrEmpty(s.mix))lines.Add("Selected music: "+s.mix);
        if(s.due!=null && s.due.Length>0)lines.Add("Next due: "+s.due[0].title+" · "+s.due[0].due);
        if(s.tracks!=null && s.tracks.Length>0)lines.Add("Last heard: "+TrackLabel(s.tracks[0]));
        return String.Join("\n",lines.ToArray());
    }
    static string TrackLabel(StudyTrack t){return t.title+(String.IsNullOrEmpty(t.artist)?"":" · "+t.artist);}
    void StudyFeedback(string text,bool bubble){
        if(deskNote!=null)deskNote.Text=text;
        if(bubble && !quitting)LocalReply(text);
    }
    async void StudyAction(string action,Dictionary<string,object> fields=null,bool bubble=true,bool automatic=false){
        if(studyBusy){if(bubble)StudyFeedback("The study desk is finishing a request. Try again in a moment.",true);return;}
        if(fields==null)fields=new Dictionary<string,object>();
        int generation=watchGeneration,version=dialogueVersion;
        studyBusy=true;
        try{
            StudyView result=await Task.Run(()=>StudyStore(action,fields));
            if(quitting)return;
            bool stillReading=version==dialogueVersion;
            if(!String.IsNullOrEmpty(result.error)){StudyFeedback(result.error,bubble && stillReading);return;}
            study=result;
            string note=result.note??"";
            bool currentCue=!automatic || (watchBadge!=null && generation==watchGeneration && CueMatches(ReadWindow(GetForegroundWindow(),true),result) && CanObserveFocus());
            if(!String.IsNullOrEmpty(result.launch) && currentCue){
                if(!SongLink(result.launch))throw new IOException("Invalid stored music link");
                try{
                    Process.Start(new ProcessStartInfo(result.launch){UseShellExecute=true});
                    note+=" Opened YouTube Music.";
                }catch(System.ComponentModel.Win32Exception){note+=" Windows could not open the music link.";}
            }
            if(action=="end")studyCueLatched=true;
            if(action=="status" && bubble)note=StudySummary(result);
            if(action=="sessions")note=PastSessions(result);
            if(action=="history")note=TrackHistory(result);
            if(action=="moment-search")note=bubble?MomentResults(result.moments):result.moments.Length+" matching moments.";
            if(note.Length>0 && currentCue)StudyFeedback(note,bubble && stillReading);
            RefreshDesk();
        }catch(Exception){if(!quitting)StudyFeedback("Could not reach the local study desk.",bubble && version==dialogueVersion);}
        finally{studyBusy=false;}
    }
    static string PastSessions(StudyView s){
        if(s.sessions==null || s.sessions.Length==0)return "No finished sessions yet.";
        return String.Join("\n",Array.ConvertAll(s.sessions,x=>Epoch.AddSeconds(x.started).ToLocalTime().ToString("ddd d MMM HH:mm")+
            " · "+Math.Max(1,(x.ended-x.started)/60)+" min"+(String.IsNullOrEmpty(x.topic)?"":" · "+x.topic)));
    }
    static string TrackHistory(StudyView s){
        if(s.tracks==null || s.tracks.Length==0)return "No tracks observed yet. Link your player in the study desk and play music during a session.";
        return String.Join("\n",Array.ConvertAll(s.tracks,t=>Epoch.AddSeconds(t.at).ToLocalTime().ToString("ddd HH:mm")+"  "+TrackLabel(t)));
    }
    static bool CueMatches(Activity item,StudyView s){
        return item!=null && s!=null && !String.IsNullOrEmpty(s.cue) &&
            String.Equals(item.App,s.app,StringComparison.OrdinalIgnoreCase) &&
            item.Title.IndexOf(s.cue,StringComparison.OrdinalIgnoreCase)>=0;
    }
    void PollStudy(Activity item,DateTime now){
        if(study==null)return;
        if(!CueMatches(item,study)){cueSince=DateTime.MinValue;studyCueLatched=false;}
        else{
            if(cueSince==DateTime.MinValue || (now-lastCueTick).TotalSeconds>3)cueSince=now;
            if(!study.active && !studyBusy && !studyCueLatched && (now-cueSince).TotalSeconds>=45){
                studyCueLatched=true;StudyAction("auto-start",new Dictionary<string,object>{{"app",study.app},{"cue",study.cue}},true,true);
            }
        }
        lastCueTick=now;
    }
    void PollStudyMusic(DateTime now){
        if(watchBadge==null || study==null)return;
        if(study.active && !String.IsNullOrEmpty(study.source) && !mediaBusy && !studyBusy && now>=nextMediaCheck){
            nextMediaCheck=now.AddSeconds(15);StudyMedia("sample",false);
        }
    }
    async void StudyMedia(string action,bool bubble=true){
        if(mediaBusy){if(bubble)StudyFeedback("Checking the player. Try again in a moment.",true);return;}
        mediaBusy=true;int generation=watchGeneration,version=dialogueVersion;
        try{
            if(study==null || action=="sample"){
                var persisted=await Task.Run(()=>StudyStore("status",new Dictionary<string,object>()));
                if(quitting || generation!=watchGeneration || studyBusy || persisted.error!=null)return;
                study=persisted;
            }
            if(action=="sample" && (!study.active || String.IsNullOrEmpty(study.source)))return;
            string source=study.source??"";
            int session=study.session;
            if(action!="bind" && action!="current" && source.Length==0){StudyFeedback("Start a song, then link the current player in Study desk.",bubble);return;}
            string request=action=="bind"?"current":(action=="sample" || action=="keep")?"inspect":action;
            MediaView m=await Task.Run(()=>MediaRequest(request,source));
            if(quitting || generation!=watchGeneration)return;
            bool show=bubble && version==dialogueVersion;
            if(!m.available){if(action!="sample")StudyFeedback(m.message??"No active player found.",show);return;}
            if(action=="bind"){
                StudyAction("bind",new Dictionary<string,object>{{"source",m.source}},show);return;
            }
            if(action=="current"){
                StudyFeedback((m.playing?"Playing: ":"Selected: ")+(m.title??"Unknown title")+
                    (String.IsNullOrEmpty(m.artist)?"":" · "+m.artist),show);return;
            }
            if(action=="keep" || (action=="next" && m.changed && study.active)){
                StudyAction(action=="keep"?"song-keep":"song-skip",
                    new Dictionary<string,object>{{"session",session},{"source",source},{"title",m.title??""},{"artist",m.artist??""}},show);
                return;
            }
            if(action!="sample"){StudyFeedback(m.changed?"Player accepted the "+action+" request.":"The player declined "+action+".",show);return;}
            if(!m.playing || String.IsNullOrEmpty(m.title) || watchBadge==null || !study.active || session!=study.session)return;
            if(lastRecordedSession==session && m.source==lastRecordedSource && m.title==lastRecordedTitle && m.artist==lastRecordedArtist)return;
            var values=new Dictionary<string,object>{{"session",session},{"source",m.source},{"title",m.title},{"artist",m.artist??""}};
            var result=await Task.Run(()=>StudyStore("track",values));
            if(!quitting && result.error==null && generation==watchGeneration && result.active && result.session==session){
                lastRecordedSession=session;lastRecordedSource=m.source;lastRecordedTitle=m.title;lastRecordedArtist=m.artist;
                if(!studyBusy){study=result;RefreshDesk();}
                if(result.avoid && !studyBusy && !String.IsNullOrEmpty(m.title) && (DateTime.UtcNow-lastAutomaticSkip).TotalSeconds>=90){
                    lastAutomaticSkip=DateTime.UtcNow;
                    var skipped=await Task.Run(()=>MediaRequest("next",source,m.title,m.artist));
                    if(!quitting && watchBadge!=null && generation==watchGeneration && skipped.changed && !quiet && !reading)
                        Signal("offer","Skipped a song you passed on in two study sessions.",4);
                }
            }
        }catch(Exception){if(!quitting && action!="sample")StudyFeedback("Windows could not reach the player.",bubble && version==dialogueVersion);}
        finally{mediaBusy=false;}
    }
    static string MomentResults(StudyMoment[] matches){
        if(matches==null || matches.Length==0)return "No matching moments.";
        var lines=new List<string>();
        foreach(var m in matches){
            lines.Add("#"+m.id+"  "+m.name+" · "+Epoch.AddSeconds(m.at).ToLocalTime().ToString("ddd d MMM, HH:mm"));
            lines.Add(FriendlyApp(m.app)+" · "+m.title);
            if(!String.IsNullOrEmpty(m.description))lines.Add(m.description);
            if(!String.IsNullOrEmpty(m.track))lines.Add("Playing: "+m.track+(String.IsNullOrEmpty(m.artist)?"":" · "+m.artist));
            else if(!String.IsNullOrEmpty(m.mix))lines.Add("Selected mix: "+m.mix);
        }
        return String.Join("\n",lines.ToArray());
    }
    async void RememberMoment(string name){
        if(watchBadge==null || current==null || !current.Same(ReadWindow(previousApp,true))){
            LocalReply("Enable awareness, then summon me over the window you want to remember.");return;
        }
        var item=current;int generation=watchGeneration,version=dialogueVersion;
        var values=new Dictionary<string,object>{{"name",name},{"app",item.App},{"title",item.Title},
            {"description",(DateTime.Now-observedAt).TotalSeconds<45?observation:""}};
        if(study!=null && study.active && !String.IsNullOrEmpty(study.source) && !mediaBusy){
            string source=study.source;int session=study.session;
            try{
                mediaBusy=true;var media=await Task.Run(()=>MediaRequest("inspect",source));
                if(media.available && media.playing){
                    values["source"]=source;values["session"]=session;values["title_of_track"]=media.title??"";values["artist"]=media.artist??"";
                }
            }catch(Exception){}finally{mediaBusy=false;}
        }
        if(quitting || version!=dialogueVersion || generation!=watchGeneration)return;
        if(!item.Same(ReadWindow(item.Handle,true))){LocalReply("That window changed before I could bookmark it.");return;}
        StudyAction("moment-save",values);
    }
    bool StudyCommand(string message){
        string text=message.Trim().TrimEnd('?');
        const string specific="start studying ";
        if(text.StartsWith(specific,StringComparison.OrdinalIgnoreCase)){StudyAction("start",new Dictionary<string,object>{{"topic",text.Substring(specific.Length)}});return true;}
        if(text.Equals("what did I study",StringComparison.OrdinalIgnoreCase)){StudyAction("sessions");return true;}
        var moment=Regex.Match(text,@"^(remember this moment as|find moment|forget moment) (.+)$",RegexOptions.IgnoreCase);
        if(moment.Success){
            string verb=moment.Groups[1].Value.ToLowerInvariant(),value=moment.Groups[2].Value.Trim();
            if(verb=="remember this moment as")RememberMoment(value);
            else if(verb=="find moment")StudyAction("moment-search",new Dictionary<string,object>{{"query",value}});
            else StudyAction("moment-delete",new Dictionary<string,object>{{"id",value}});
            return true;
        }
        switch(text.ToLowerInvariant()){
            case "show my moments":StudyAction("moment-search");return true;
            case "study desk":case "/study": OpenStudyDesk();return true;
            case "i'm studying":case "i’m studying":case "start study session":StudyAction("start");return true;
            case "finish studying":case "end study session":StudyAction("end");return true;
            case "study status":StudyAction("status");return true;
            case "i like this mix":StudyAction("like");return true;
            case "open my study mix":StudyAction("open");return true;
            case "next study mix":StudyAction("next");return true;
            case "what did i listen to":StudyAction("history");return true;
            case "what's playing":case "what’s playing":
            case "what am i listening to":case "what am i listening to right now":
            case "what music am i listening to":StudyMedia("current");return true;
            case "link my study player":StudyMedia("bind");return true;
            case "pause study music":StudyMedia("pause");return true;
            case "resume study music":StudyMedia("play");return true;
            case "keep this song":case "i like this song":StudyMedia("keep");return true;
            case "avoid songs i repeatedly skip":StudyAction("enable-skip");return true;
            case "stop automatic song skips":StudyAction("disable-skip");return true;
            case "skip this song":StudyMedia("next");return true;
            case "disable study cue":StudyAction("uncue");return true;
        }
        const string cue="study when the title contains ";
        if(text.StartsWith(cue,StringComparison.OrdinalIgnoreCase)){
            SaveStudyCue(text.Substring(cue.Length));return true;
        }
        var add=Regex.Match(message,@"^add study music (.+?)\s+(https://\S+)$",RegexOptions.IgnoreCase);
        if(add.Success){
            StudyAction("add",new Dictionary<string,object>{{"label",add.Groups[1].Value},{"url",add.Groups[2].Value}});return true;
        }
        return false;
    }
    void SaveStudyCue(string phrase){
        if(watchBadge==null || current==null || current.Title.IndexOf(phrase.Trim(),StringComparison.OrdinalIgnoreCase)<0){
            StudyFeedback("Open the study material with awareness enabled, then use words from its window title.",true);return;
        }
        StudyAction("cue",new Dictionary<string,object>{{"app",current.App},{"cue",phrase.Trim()}});
    }
 }
}
