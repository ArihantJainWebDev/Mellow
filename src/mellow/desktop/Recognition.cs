using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace MellowDesktop {
 public sealed partial class Ghost {
    public sealed class ActivityRead {public string answer,activity,setup;public bool study;}
    StackPanel recognitionActions;
    Button acceptActivity,acceptMusic;
    readonly Dictionary<string,DateTime> activityCooldown=new Dictionary<string,DateTime>();
    DateTime lastChange, nextRead;
    bool passiveRead;
    ActivityRead pendingActivity;
    int offerScene,correctionEpoch,autoReadScene=-1;
    string autoReadVision=null;
    void InitActivityOffer(StackPanel bar){
        recognitionActions=new StackPanel{Orientation=Orientation.Horizontal,Visibility=Visibility.Collapsed};
        acceptActivity=Choice("Open setup",delegate{AcceptActivity(false);});
        acceptMusic=Choice("With music",delegate{AcceptActivity(true);});
        recognitionActions.Children.Add(acceptActivity);recognitionActions.Children.Add(acceptMusic);
        recognitionActions.Children.Add(Choice("Skip",ClearActivityOffer));
        bar.Children.Add(recognitionActions);
    }
    void ClearActivityOffer(){
        pendingActivity=null;
        if(recognitionActions!=null)recognitionActions.Visibility=Visibility.Collapsed;
    }
    void AcceptActivity(bool music){
        var offer=pendingActivity;ClearActivityOffer();
        if(offer==null || watchBadge==null || current==null || offerScene!=sceneVersion ||
           (GetForegroundWindow()!=current.Handle && GetForegroundWindow()!=new System.Windows.Interop.WindowInteropHelper(watchBadge).Handle) ||
           !current.Same(ReadWindow(current.Handle,true)))return;
        if(!String.IsNullOrEmpty(offer.setup))SavedSetup("load",offer.setup,"");
        else if(offer.study)StudyAction(music?"start":"quiet-start");
    }
    void ConsiderRecognition(){
        if(watchBadge==null || current==null || quiet || passiveRead || proposed!=null ||
           DateTime.UtcNow<nextRead || (DateTime.UtcNow-lastChange).TotalSeconds<5)return;
        string visual=FreshVision();
        if(autoReadScene==sceneVersion && autoReadVision==visual)return;
        autoReadScene=sceneVersion;autoReadVision=visual;
        nextRead=DateTime.UtcNow.AddSeconds(5);Recognize("auto","");
    }
    string FreshVision(){return (DateTime.Now-observedAt).TotalSeconds<=45?observation:"";}
    Dictionary<string,string> Scene(Activity a,string visual){
        return new Dictionary<string,string>{{"app",a.App},{"title",a.Title},{"vision",visual??""}};
    }
    async void Recognize(string action,string label){
        bool ambient=action=="auto";
        if(current==null || watchBadge==null){
            if(!ambient)LocalReply("Enable desktop awareness and open Mellow over the activity first.");
            return;
        }
        var anchor=current;
        if(!anchor.Same(ReadWindow(ambient?GetForegroundWindow():previousApp,true))){
            if(!ambient)LocalReply("That window changed. Summon me again over the activity.");return;
        }
        var scenes=new List<Dictionary<string,string>>();
        foreach(var item in visits){
            if(Object.ReferenceEquals(item,anchor) || item.At<evidenceStart || (DateTime.Now-item.At).TotalMinutes>4)continue;
            scenes.Add(Scene(item,item.Visual));
        }
        scenes.Add(Scene(anchor,FreshVision()));
        int revision=sceneVersion,generation=watchGeneration,dialog=dialogueVersion;
        if(action=="correct" || action=="forget"){correctionEpoch++;ClearActivityOffer();autoReadScene=-1;}
        int epoch=correctionEpoch;
        string payload=EncodeField(new JavaScriptSerializer().Serialize(new {action=action,label=label,scenes=scenes}));
        if(ambient)passiveRead=true;
        try{
            var result=await Task.Run(()=>ReadFramed<ActivityRead>(new ProcessStartInfo(
                Environment.GetEnvironmentVariable("MELLOW_PYTHON"),"-m mellow.activity"),payload,10000));
            if(quitting || generation!=watchGeneration || revision!=sceneVersion || epoch!=correctionEpoch)return;
            if(!ambient){
                if(dialog!=dialogueVersion)return;
                string others=action=="read"?BackgroundApps(anchor):"";
                LocalReply(result.answer+others);showingScreenReply=true;return;
            }
            if(quiet || !anchor.Same(ReadWindow(GetForegroundWindow(),true)))return;
            if(String.IsNullOrEmpty(result.setup) && (!result.study || (study!=null && study.active))){ClearActivityOffer();return;}
            string key=!String.IsNullOrEmpty(result.setup)?"setup:"+result.setup:"study";
            DateTime until;
            if(activityCooldown.TryGetValue(key,out until) && until>DateTime.UtcNow)return;
            activityCooldown[key]=DateTime.UtcNow.AddMinutes(30);
            offerScene=revision;pendingActivity=result;
            acceptActivity.Content=key=="study"?"Start studying?":"Preview "+result.setup+"?";
            acceptMusic.Visibility=key=="study"?Visibility.Visible:Visibility.Collapsed;
            recognitionActions.Visibility=Visibility.Visible;
        }catch(Exception){if(!ambient && !quitting && dialog==dialogueVersion)LocalReply("Could not read local activity evidence.");}
        finally{if(ambient)passiveRead=false;}
    }
    DateTime evidenceStart;
    bool RecognitionCommand(string input){
        string text=input.Trim().TrimEnd('?','.');
        foreach(string prefix in new[]{"actually, that was ","actually that was ","label this activity as ","forget activity label "}){
            if(text.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){
                Recognize(prefix.StartsWith("forget")?"forget":"correct",text.Substring(prefix.Length).Trim());return true;
            }
        }
        if(text.Equals("why do you think that",StringComparison.OrdinalIgnoreCase) ||
           text.Equals("why do you think i'm doing that",StringComparison.OrdinalIgnoreCase) ||
           text.Equals("what am i doing, and what makes you think that",StringComparison.OrdinalIgnoreCase) ||
           text.Equals("what am i doing, and why",StringComparison.OrdinalIgnoreCase)){
            Recognize("read","");return true;
        }
        return false;
    }
 }
}
