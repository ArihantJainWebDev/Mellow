using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Threading;

namespace MellowDesktop {
 public sealed partial class Ghost {
    Window studyWindow;
    TextBlock deskNote,deskClock,deskSession,deskMix,deskLibrary,deskCue,deskPlayer,deskDue,deskHistory;
    TextBox deskLabel,deskUrl,deskCueInput,deskSearch;
    TextBlock deskMoments,deskPast;
    Expander journalDetails,libraryDetails;
    bool deskInitial;
    Button deskStart,deskLike,deskNext,deskSkipRule;
    DispatcherTimer deskTimer;
    static readonly Brush DeskInk=Ink(47,37,60),DeskMuted=Ink(100,89,111),DeskPaper=Ink(250,247,252);
    static TextBlock DeskText(string text,double points,Brush color,bool strong=false){
        return new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,FontSize=points,Foreground=color,
            FontFamily=new FontFamily("Segoe UI"),FontWeight=strong?FontWeights.SemiBold:FontWeights.Normal};
    }
    static Border DeskCard(UIElement content,Brush fill,int pad){
        return new Border{CornerRadius=new CornerRadius(14),Padding=new Thickness(pad),Background=fill,Child=content};
    }
    static Button DeskButton(string label,Action click,bool primary=false){
        var button=new Button{Content=label,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,6,4),
            Background=primary?Ink(99,66,133):Ink(238,230,245),Foreground=primary?Brushes.White:DeskInk,
            BorderThickness=new Thickness(0),FontFamily=new FontFamily("Segoe UI"),Cursor=Cursors.Hand};
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        border.AppendChild(content);button.Template=new ControlTemplate(typeof(Button)){VisualTree=border};
        button.IsEnabledChanged+=delegate{button.Opacity=button.IsEnabled?1:.43;};
        button.MouseEnter+=delegate{if(button.IsEnabled)button.Opacity=.84;};
        button.MouseLeave+=delegate{button.Opacity=button.IsEnabled?1:.43;};
        button.Click+=delegate{click();};return button;
    }
    static TextBox DeskInput(StackPanel parent,string label){
        var caption=DeskText(label,12,DeskMuted,true);caption.Margin=new Thickness(0,6,0,2);parent.Children.Add(caption);
        var input=new TextBox{Padding=new Thickness(8,5,8,5),FontSize=14,MaxLength=1800,BorderBrush=Ink(210,197,220)};
        parent.Children.Add(input);return input;
    }
    static void AddButtons(StackPanel parent,params Button[] buttons){
        var row=new WrapPanel{Margin=new Thickness(0,7,0,0)};
        foreach(var button in buttons)row.Children.Add(button);parent.Children.Add(row);
    }
    static Expander DeskDetails(string title,UIElement content){
        return new Expander{Header=title,Content=content,FontSize=14,Margin=new Thickness(0,9,0,1),
            Foreground=DeskInk,Padding=new Thickness(7)};
    }
    StackPanel BuildStudyDesk(){
        var body=new StackPanel{Margin=new Thickness(22,16,22,14)};
        var head=new DockPanel();
        var close=DeskButton("Close",delegate{studyWindow.Close();});DockPanel.SetDock(close,Dock.Right);head.Children.Add(close);
        head.Children.Add(DeskText("Study desk",26,DeskInk,true));body.Children.Add(head);
        var hero=new StackPanel();
        deskSession=DeskText("Session",13,Ink(222,208,237),true);hero.Children.Add(deskSession);
        deskClock=DeskText("00:00",40,Brushes.White,true);hero.Children.Add(deskClock);
        deskMix=DeskText("",14,Ink(236,224,245));hero.Children.Add(deskMix);
        deskStart=DeskButton("Start session",delegate{StudyAction(study!=null && study.active?"end":"start",null,false);});
        deskLike=DeskButton("Like mix",delegate{StudyAction("like",null,false);});
        deskNext=DeskButton("Next mix",delegate{StudyAction("next",null,false);});
        AddButtons(hero,deskStart,deskLike,deskNext);
        var sceneRow=new Grid();
        sceneRow.ColumnDefinitions.Add(new ColumnDefinition());
        sceneRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        sceneRow.Children.Add(hero);
        var portrait=ReviewViewport();portrait.Width=105;portrait.Height=115;portrait.IsHitTestVisible=false;
        Grid.SetColumn(portrait,1);sceneRow.Children.Add(portrait);
        var heroCard=DeskCard(sceneRow,new LinearGradientBrush(Color.FromRgb(52,39,66),Color.FromRgb(89,65,105),18),17);heroCard.Margin=new Thickness(0,9,0,10);body.Children.Add(heroCard);
        var columns=new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());columns.ColumnDefinitions.Add(new ColumnDefinition());
        var due=new StackPanel();due.Children.Add(DeskText("Next deadlines",15,DeskInk,true));deskDue=DeskText("",13,DeskMuted);
        deskDue.Margin=new Thickness(0,7,0,0);due.Children.Add(deskDue);
        var dueCard=DeskCard(due,Brushes.White,12);dueCard.Margin=new Thickness(0,0,5,0);columns.Children.Add(dueCard);
        var heard=new StackPanel();heard.Children.Add(DeskText("Recently heard",15,DeskInk,true));deskHistory=DeskText("",13,DeskMuted);
        deskHistory.Margin=new Thickness(0,7,0,0);heard.Children.Add(deskHistory);
        var heardCard=DeskCard(heard,Brushes.White,12);heardCard.Margin=new Thickness(5,0,0,0);Grid.SetColumn(heardCard,1);columns.Children.Add(heardCard);
        body.Children.Add(columns);
        var journal=new StackPanel();
        deskMoments=DeskText("",13,DeskMuted);journal.Children.Add(deskMoments);
        deskSearch=DeskInput(journal,"Search moments by note, window or music");
        AddButtons(journal,DeskButton("Find moments",delegate{StudyAction("moment-search",new Dictionary<string,object>{{"query",deskSearch.Text}},false);}));
        deskPast=DeskText("",13,DeskMuted);journal.Children.Add(deskPast);
        journalDetails=DeskDetails("Moments and past sessions",journal);body.Children.Add(journalDetails);
        var music=new StackPanel();deskLibrary=DeskText("",13,DeskMuted);music.Children.Add(deskLibrary);
        deskLabel=DeskInput(music,"Mix or song name");deskUrl=DeskInput(music,"YouTube Music share link");
        AddButtons(music,DeskButton("Save music",delegate{StudyAction("add",new Dictionary<string,object>{{"label",deskLabel.Text},{"url",deskUrl.Text}},false);},true),
            DeskButton("Remove by name",delegate{StudyAction("remove",new Dictionary<string,object>{{"label",deskLabel.Text}},false);}));
        libraryDetails=DeskDetails("Music library",music);body.Children.Add(libraryDetails);
        deskInitial=true;
        var settings=new StackPanel();
        deskCue=DeskText("",13,DeskMuted);settings.Children.Add(deskCue);
        deskCueInput=DeskInput(settings,"Words in your study window’s title");
        AddButtons(settings,DeskButton("Use this cue",delegate{SaveStudyCue(deskCueInput.Text);}),DeskButton("Disable cue",delegate{StudyAction("uncue",null,false);}));
        deskPlayer=DeskText("",13,DeskMuted);settings.Children.Add(deskPlayer);
        AddButtons(settings,DeskButton("Link current player",delegate{StudyMedia("bind",false);}),DeskButton("Unlink",delegate{StudyAction("unbind",null,false);}));
        deskSkipRule=DeskButton("Enable repeat-skip rule",delegate{StudyAction(study!=null && study.auto_skip!=0?"disable-skip":"enable-skip",null,false);});
        AddButtons(settings,deskSkipRule,DeskButton("Keep current song",delegate{StudyMedia("keep",false);}));
        body.Children.Add(DeskDetails("Automatic start and listening history",settings));
        deskNote=DeskText("",13,Ink(91,63,119));deskNote.Margin=new Thickness(0,10,0,0);body.Children.Add(deskNote);
        return body;
    }
    public static void VerifyStudy(string preview){
        var ghost=new Ghost(true,null,false,true);
        var fields=new Dictionary<string,object>();
        StudyView fixture=StudyStore("status",fields);
        if(fixture==null || fixture.error!=null || !fixture.active || fixture.topic!="Linear algebra" ||
           fixture.tracks.Length!=1 || fixture.moments.Length!=1 || fixture.due.Length!=1)
            throw new InvalidOperationException("Run through tools/verify_study.py with its isolated data.");
        if(!SongLink("https://music.youtube.com/watch?v=abcdefghijk") || SongLink("https://example.com/watch?v=abcdefghijk"))
            throw new InvalidOperationException("Music destination validation");
        var app=new Activity{App="opera",Title="Lecture · Linear algebra"};
        if(!CueMatches(app,fixture) || CueMatches(new Activity{App="other",Title=app.Title},fixture))
            throw new InvalidOperationException("App-specific study cue");
        IntPtr toolbar=new IntPtr(Int64.Parse(Environment.GetEnvironmentVariable("MELLOW_FIXTURE_HWND")));
        string address=AddressFromChrome(System.Windows.Automation.AutomationElement.FromHandle(toolbar));
        if(address!="https://example.org/course")throw new InvalidOperationException("Expected the address from the native toolbar.");
        var saved=new List<Activity>{new Activity{App="notepad",Title="Closed notes"},
            new Activity{App="@link",Title=address},new Activity{App="unregistered",Title="Unknown tool"}};
        var missing=new List<string>();var plan=PlanWorkspace(saved,new List<Activity>(),missing);
        if(plan.Count!=2 || missing.Count!=1 || plan[0].Handle!=IntPtr.Zero ||
            !PlanLabel(plan[0]).StartsWith("Launch") || !PlanLabel(plan[1]).StartsWith("Open tab"))
            throw new InvalidOperationException("Closed application and pinned tab restore plan");
        ghost.study=fixture;
        for(int frame=0;frame<16;frame++)ghost.ApplyPose(2+frame*.04,.04,false,0,0,1);
        ghost.studyWindow=new Window();var body=ghost.BuildStudyDesk();ghost.RefreshDesk();
        if(!ghost.deskDue.Text.Contains("Problem set 4") || !ghost.deskHistory.Text.Contains("Evening train") ||
           !ghost.deskMoments.Text.Contains("Eigenvectors") || !ghost.deskLike.IsEnabled)
            throw new InvalidOperationException("Study fixture did not reach the dashboard");
        if(!String.IsNullOrEmpty(preview)){
            var frame=new Border{Background=DeskPaper,Child=body,Width=590,MinHeight=440};
            frame.Measure(new Size(590,double.PositiveInfinity));frame.Arrange(new Rect(frame.DesiredSize));frame.UpdateLayout();
            var bitmap=new RenderTargetBitmap(590,(int)Math.Ceiling(frame.ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(frame);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
            using(var output=File.Create(preview))png.Save(output);
        }
        Console.WriteLine("Study desk passed: Python bridge, cue, local music destination, deadlines, listening history, moments, WPF layout.");
    }
    void OpenStudyDesk(){
        if(chatWindow!=null && chatWindow.IsVisible)HideComposer();
        if(studyWindow!=null){studyWindow.Activate();StudyAction("status",null,false);return;}
        var area=SystemParameters.WorkArea;
        studyWindow=new Window{Title="Mellow · Study desk",Width=Math.Min(590,area.Width),Height=Math.Min(675,area.Height),
            MinWidth=Math.Min(365,area.Width),WindowStartupLocation=WindowStartupLocation.CenterScreen,
            Background=DeskPaper,FontFamily=new FontFamily("Segoe UI")};
        studyWindow.Content=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=BuildStudyDesk()};
        studyWindow.Closed+=delegate{
            if(deskTimer!=null)deskTimer.Stop();
            studyWindow=null;deskNote=null;
        };
        studyWindow.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape){e.Handled=true;studyWindow.Close();}};
        deskTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};deskTimer.Tick+=delegate{UpdateDeskClock();};deskTimer.Start();
        studyWindow.Show();RefreshDesk();StudyAction("status",null,false);
    }
    void UpdateDeskClock(){
        if(study==null || studyWindow==null)return;
        long until=study.active?(long)(DateTime.UtcNow-Epoch).TotalSeconds:study.ended;
        var elapsed=TimeSpan.FromSeconds(Math.Max(0,until-study.started));
        deskClock.Text=(elapsed.TotalHours>=1?((int)elapsed.TotalHours)+":":"")+elapsed.ToString(@"mm\:ss");
    }
    void RefreshDesk(){
        if(studyWindow==null || study==null)return;
        var s=study;UpdateDeskClock();
        if(deskInitial){
            journalDetails.IsExpanded=s.moments.Length>0;
            libraryDetails.IsExpanded=s.mixes.Length==0 && !journalDetails.IsExpanded;
            deskInitial=false;
        }
        deskSession.Text=s.active?(String.IsNullOrEmpty(s.topic)?"Studying now":s.topic):s.ended>0?"Last session":"Ready to study";
        deskMix.Text=s.active && !String.IsNullOrEmpty(s.mix)?"Selected · "+s.mix:"";
        deskStart.Content=s.active?"Finish session":"Start session";
        deskLike.IsEnabled=s.active && !String.IsNullOrEmpty(s.mix);
        deskNext.IsEnabled=s.active && s.mixes!=null && (s.mixes.Length>1 || (s.mixes.Length==1 && String.IsNullOrEmpty(s.mix)));
        deskMoments.Text=s.moments.Length>0?MomentResults(s.moments):"No matching moments. Say “remember this moment as …” over a project window.";
        deskPast.Text=s.sessions.Length==0?"":"Finished sessions\n"+PastSessions(s);
        deskLibrary.Text=s.mixes.Length>0?String.Join(" · ",s.mixes):"Save a favorite song or playlist for study sessions.";
        deskSkipRule.Content=s.auto_skip!=0?"Disable repeat-skip rule":"Enable repeat-skip rule";
        deskCue.Text=s.cue.Length>0?"Auto-start after 45 seconds in "+FriendlyApp(s.app)+" · "+s.cue:"Auto-start is off.";
        deskPlayer.Text=String.IsNullOrEmpty(s.source)?"Link a playing media source to keep a local listening history.":"Linked media source: "+s.source;
        deskDue.Text=s.due.Length==0?"No open assignments.":String.Join("\n",Array.ConvertAll(s.due,x=>x.title+" · "+x.due));
        var last=new List<string>();foreach(var t in s.tracks){if(last.Count==3)break;last.Add(TrackLabel(t));}
        deskHistory.Text=last.Count==0?"No observed tracks yet.":String.Join("\n",last.ToArray());
    }
 }
}
