using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MellowDesktop {
 public sealed partial class Ghost {
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr handle);
    IntPtr previousApp;
    Activity composerTarget;
    bool ComposerFocused {get{return chatWindow!=null && chatWindow.IsVisible &&
        GetForegroundWindow()==new System.Windows.Interop.WindowInteropHelper(chatWindow).Handle;}}
    Popup replyPopup;
    TextBlock pageText,pageNumber;
    Button nextPage,prevPage;
    readonly StringBuilder reply=new StringBuilder();
    readonly StringBuilder commandOutput=new StringBuilder();
    readonly List<int> starts=new List<int>();
    int page, visibleEnd, pageEnd, dialogueVersion;
    bool reading, finished, submitted, streamed, dismissedReply;
    double nextLetter;
    static SolidColorBrush Ink(byte r, byte g, byte b){return new SolidColorBrush(Color.FromRgb(r,g,b));}
    static Button Choice(string label, Action act){
        var b=new Button{Content=label,Focusable=false,Margin=new Thickness(2),Padding=new Thickness(5,2,5,2),
            Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand};
        b.Click+=delegate{act();};return b;
    }
    void CreateComposer(){
        input=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinLines=1,MaxLines=6,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FontSize=16,Foreground=Brushes.White,
            Background=Brushes.Transparent,CaretBrush=Brushes.White,BorderThickness=new Thickness(0),
            Margin=new Thickness(0,6,0,10),IsEnabled=ready};
        var body=new StackPanel();
        body.Children.Add(new TextBlock{Text="Ask Mellow",FontWeight=FontWeights.SemiBold,Foreground=Ink(213,200,235)});
        body.Children.Add(input);
        var row=new DockPanel();
        var close=Choice("Close",HideComposer);close.Foreground=Brushes.White;DockPanel.SetDock(close,Dock.Right);row.Children.Add(close);
        var send=Choice("Send ↑",delegate{if(ready && input.Text.Trim().Length>0)SendInput();});
        send.Foreground=Brushes.White;DockPanel.SetDock(send,Dock.Right);row.Children.Add(send);
        var notice=Choice("Notice",delegate{
            if(!input.Text.StartsWith("/notice",StringComparison.OrdinalIgnoreCase))input.Text="/notice "+input.Text;
            input.CaretIndex=input.Text.Length;input.Focus();
        });
        notice.Foreground=Brushes.White;DockPanel.SetDock(notice,Dock.Right);row.Children.Add(notice);
        row.Children.Add(new TextBlock{Text="Shift+Enter for a new line   ·   Esc to close",VerticalAlignment=VerticalAlignment.Center,Foreground=Ink(194,191,202)});
        body.Children.Add(row);
        chatWindow=new Window{Title="Ask Mellow",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,Topmost=true,
            SizeToContent=SizeToContent.Height,Content=new Border{CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1),
            BorderBrush=Ink(91,85,105),Background=new SolidColorBrush(Color.FromArgb(244,36,36,45)),
            Padding=new Thickness(18,12,18,9),Child=body}};
        chatWindow.Closing+=(s,e)=>{if(!quitting){e.Cancel=true;HideComposer();}};
        chatWindow.PreviewKeyDown+=(s,e)=>{
            if(e.Key==Key.Escape){e.Handled=true;HideComposer();}
            else if(e.Key==Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift)==0){
                e.Handled=true;if(ready && input.Text.Trim().Length>0)SendInput();
            }
        };
    }
    void HideComposer(){
        chatWindow.Hide();
        if(previousApp!=IntPtr.Zero)SetForegroundWindow(previousApp);
    }
    static TextBlock PageLabel(){
        return new TextBlock{MaxWidth=256,TextWrapping=TextWrapping.Wrap,FontSize=15,FontFamily=new FontFamily("Segoe UI"),
            LineHeight=20,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,Foreground=Ink(43,34,58)};
    }
    StackPanel setupChoices;
    void PaintSetupChoices(){
        if(setupChoices!=null)setupChoices.Visibility=
            proposed!=null && dialogueVersion==offerDialogue && DateTime.UtcNow<offerExpires?Visibility.Visible:Visibility.Collapsed;
    }
    void CreateReader(){
        pageText=PageLabel();
        pageNumber=new TextBlock{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,10,0)};
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,0)};
        prevPage=Choice("Back",PreviousPage);
        nextPage=Choice("Next",AdvancePage);
        controls.Children.Add(pageNumber);controls.Children.Add(prevPage);controls.Children.Add(nextPage);
        controls.Children.Add(Choice("⧉",delegate{try{Clipboard.SetText(reply.ToString());}catch(System.Runtime.InteropServices.ExternalException){}}));
        controls.Children.Add(Choice("×",DismissReply));
        ((Button)controls.Children[3]).ToolTip="Copy reply";((Button)controls.Children[4]).ToolTip="Dismiss reply";
        var body=new StackPanel();body.Children.Add(pageText);body.Children.Add(controls);
        setupChoices=new StackPanel{Orientation=Orientation.Horizontal,Visibility=Visibility.Collapsed};
        setupChoices.Children.Add(Choice("Restore setup",delegate{if(chatWindow!=null && chatWindow.IsActive)HideComposer();RestoreProposed();}));
        setupChoices.Children.Add(Choice("Skip",delegate{proposed=null;DismissReply();}));
        body.Children.Add(setupChoices);
        var shell=new StackPanel();
        var tint=new LinearGradientBrush(Color.FromRgb(255,251,255),Color.FromRgb(233,219,250),90);
        shell.Children.Add(new Border{Background=tint,BorderBrush=Ink(199,182,211),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(19,19,16,12),Padding=new Thickness(12,7,10,4),Child=body});
        shell.Children.Add(new System.Windows.Shapes.Polygon{Fill=Ink(233,219,250),Width=16,Height=8,
            Points=new PointCollection{new Point(0,0),new Point(16,0),new Point(6,8)}});
        replyPopup=new Popup{Placement=PlacementMode.AbsolutePoint,StaysOpen=true,AllowsTransparency=true,
            Focusable=false,Child=shell};
    }
    void DismissReply(){
        if(dialogueVersion==offerDialogue)proposed=null;
        dismissedReply=true;reading=false;if(replyPopup!=null)replyPopup.IsOpen=false;
        if(state=="speak"||state=="read")Signal("idle","",0);
    }
    void ResetReply(){
        dialogueVersion++;DismissReply();showingScreenReply=false;dismissedReply=false;reply.Clear();starts.Clear();starts.Add(0);
        formattedSource=null;paintedStart=-1;manualPage=false;advanceAt=double.NaN;page=visibleEnd=pageEnd=0;finished=false;nextLetter=0;
    }
    void AddReply(string text){
        if(text.Length==0)return;
        reply.Append(text);
        if(replyPopup==null)CreateReader();
        reading=!dismissedReply;
    }
    string formattedSource;
    MatchCollection marks;
    int paintedStart=-1,paintedEnd=-1,paintedLength=-1;
    double advanceAt=double.NaN;
    bool manualPage;
    static readonly Regex markup=new Regex(@"(?<tag>`|\*\*|\*|__)(?!\s)(?<content>.+?)(?<!\s)\k<tag>",RegexOptions.Singleline);
    void Format(TextBlock label,int start,int end){
        if(formattedSource==null || formattedSource.Length!=reply.Length){
            formattedSource=reply.ToString();marks=markup.Matches(formattedSource);
        }
        label.Inlines.Clear();
        int cursor=start;
        foreach(Match mark in marks){
            if(mark.Index>=end)break;
            int after=mark.Index+mark.Length;
            if(after<=start)continue;
            if(mark.Index>cursor)label.Inlines.Add(new Run(formattedSource.Substring(cursor,mark.Index-cursor)));
            Group content=mark.Groups["content"];
            int from=Math.Max(start,content.Index),to=Math.Min(end,content.Index+content.Length);
            if(to>from){
                var run=new Run(formattedSource.Substring(from,to-from));
                string tag=mark.Groups["tag"].Value;
                if(tag=="`")run.FontFamily=new FontFamily("Consolas");
                else if(tag.Length==2)run.FontWeight=FontWeights.Bold;
                else run.FontStyle=FontStyles.Italic;
                label.Inlines.Add(run);
            }
            cursor=Math.Min(after,end);
        }
        if(end>cursor)label.Inlines.Add(new Run(formattedSource.Substring(cursor,end-cursor)));
    }
    // Measure with the same font, width, wrapping and line spacing as the actual cloud.
    int FitPage(){
        int start=starts[page];
        if(page+1<starts.Count)return starts[page+1];
        int count=Math.Min(512,reply.Length-start);
        if(count==0)return start;
        string segment=reply.ToString(start,count);
        int[] letters=StringInfo.ParseCombiningCharacters(segment);
        var label=PageLabel();int lo=1,hi=letters.Length,fit=0;
        while(lo<=hi){
            int mid=(lo+hi)/2,end=mid==letters.Length?count:letters[mid];
            Format(label,start,start+end);label.Measure(new Size(256,double.PositiveInfinity));
            if(label.DesiredSize.Height<=80.1){fit=end;lo=mid+1;}else hi=mid-1;
        }
        if(start+fit<reply.Length && fit>0 && !char.IsWhiteSpace(reply[start+fit])){
            int boundary=fit;
            while(boundary>0 && !char.IsWhiteSpace(segment[boundary-1]))boundary--;
            if(boundary>0 && segment.Substring(0,boundary).Trim().Length>0)fit=boundary;
        }
        return start+Math.Max(1,fit);
    }
    void PaintPage(){
        if(paintedStart!=starts[page] || paintedEnd!=visibleEnd || paintedLength!=reply.Length){
            Format(pageText,starts[page],visibleEnd);paintedStart=starts[page];paintedEnd=visibleEnd;paintedLength=reply.Length;
        }
        PaintSetupChoices();
        pageNumber.Text=(page+1).ToString(CultureInfo.InvariantCulture);
        var paging=page>0 || pageEnd<reply.Length?Visibility.Visible:Visibility.Collapsed;
        pageNumber.Visibility=prevPage.Visibility=nextPage.Visibility=paging;
        prevPage.IsEnabled=page>0;
        nextPage.IsEnabled=visibleEnd<pageEnd||pageEnd<reply.Length;
        nextPage.Content=visibleEnd<pageEnd?"Reveal":"Next";
    }
    void PreviousPage(){
        if(page==0)return;
        page--;pageEnd=visibleEnd=starts[page+1];manualPage=true;advanceAt=double.NaN;state="read";PaintPage();
    }
    void AdvancePage(){
        manualPage=true;advanceAt=double.NaN;
        if(visibleEnd<pageEnd){visibleEnd=pageEnd;}
        else if(pageEnd<reply.Length){
            if(page+1==starts.Count)starts.Add(pageEnd);
            page++;visibleEnd=pageEnd=FitPage();
        }
        state="read";PaintPage();
    }
    int measuredLength=-1,measuredPage=-1;
    void TickReply(double now){
        if(!reading)return;
        bool show=!quiet && window.IsVisible;
        if(!show){replyPopup.IsOpen=false;if(state=="speak")state="read";return;}
        var bounds=SystemParameters.WorkArea;
        RevealAt(now);
        replyPopup.Child.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        var extent=replyPopup.Child.DesiredSize;
        double left=window.Left+window.Width/2-extent.Width/2;
        double top=window.Top+window.Height-size-extent.Height+3;
        if(top<bounds.Top)top=window.Top+window.Height;
        replyPopup.HorizontalOffset=Clamp(left,bounds.Left,Math.Max(bounds.Left,bounds.Right-extent.Width));
        replyPopup.VerticalOffset=Clamp(top,bounds.Top,Math.Max(bounds.Top,bounds.Bottom-extent.Height));
        replyPopup.IsOpen=true;bubbleStack.Visibility=Visibility.Collapsed;
    }
    void RevealAt(double now){
        if(measuredLength!=reply.Length || measuredPage!=page || pageEnd==starts[page]){
            pageEnd=FitPage();measuredLength=reply.Length;measuredPage=page;
        }
        visibleEnd=manualPage?pageEnd:Math.Min(visibleEnd,pageEnd);
        if(visibleEnd==pageEnd && pageEnd<reply.Length){
            if(double.IsNaN(advanceAt))advanceAt=now+Math.Max(3,(pageEnd-starts[page])*.065);
            if(now>=advanceAt){
                if(page+1==starts.Count)starts.Add(pageEnd);
                page++;pageEnd=FitPage();visibleEnd=manualPage?pageEnd:starts[page];advanceAt=double.NaN;
            }
        }else advanceAt=double.NaN;
        if(visibleEnd<pageEnd && now>=nextLetter){
            string tail=reply.ToString(visibleEnd,Math.Min(12,pageEnd-visibleEnd));
            var glyphs=StringInfo.ParseCombiningCharacters(tail);
            int step=glyphs.Length>2?glyphs[2]:tail.Length;
            visibleEnd+=step;
            nextLetter=now+(",.!?;".IndexOf(tail[step-1])>=0?.14:.032);
            state="speak";
        }else if(now>=nextLetter || visibleEnd==pageEnd)state="read";
        until=now+2;PaintPage();
    }
    public static void VerifyDialogue(){
        VerifyAwareness();
        var ghost=new Ghost(true,null,false,true);
        ghost.CreateComposer();
        if(!ghost.input.AcceptsReturn || ghost.chatWindow.ShowInTaskbar)throw new InvalidOperationException("Composer configuration");
        ghost.ResetReply();ghost.AddReply("Hi");ghost.RevealAt(0);
        ghost.replyPopup.Child.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        if(ghost.replyPopup.Child.DesiredSize.Width>120 || ghost.replyPopup.Child.DesiredSize.Height>82 ||
           ghost.nextPage.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Oversized greeting");
        string unit="A long reply with emoji 🌿, हिन्दी, code and a line break.\n";
        var originalText=new StringBuilder();for(int i=0;i<20;i++)originalText.Append(unit);
        string original=originalText.ToString();
        ghost.ResetReply();ghost.AddReply(original.Substring(0,36));ghost.RevealAt(1);
        if(ghost.state!="speak" || ghost.visibleEnd>=36)throw new InvalidOperationException("Progressive reveal");
        ghost.AddReply(original.Substring(36));ghost.finished=true;
        var seen=new StringBuilder();int count=0;
        while(++count<100){
            ghost.RevealAt(2+count);
            if(ghost.visibleEnd<ghost.pageEnd)ghost.AdvancePage();
            ghost.RevealAt(3+count);
            if(ghost.state!="read")throw new InvalidOperationException("Speech continued while waiting");
            var label=PageLabel();label.Text=ghost.pageText.Text;label.Measure(new Size(256,double.PositiveInfinity));
            if(label.DesiredSize.Height>80.1)throw new InvalidOperationException("Page clipped");
            seen.Append(label.Text);
            if(ghost.pageEnd==original.Length)break;
            ghost.AdvancePage();
        }
        if(count<2 || seen.ToString()!=original)throw new InvalidOperationException("Lost reply text");
        ghost.ResetReply();ghost.AddReply("**Bold** *italic* "+original);ghost.RevealAt(0);ghost.AdvancePage();
        if(!ghost.pageText.Text.StartsWith("Bold italic ") || ghost.pageText.Inlines.FirstInline.FontWeight!=FontWeights.Bold)
            throw new InvalidOperationException("Markdown emphasis");
        if(!char.IsWhiteSpace(ghost.reply[ghost.pageEnd-1]) && !char.IsWhiteSpace(ghost.reply[ghost.pageEnd]))
            throw new InvalidOperationException("Word split at page break");
        ghost.RevealAt(1);ghost.RevealAt(100);
        if(ghost.page!=1)throw new InvalidOperationException("Automatic page advance");
        ghost.PreviousPage();ghost.AdvancePage();ghost.RevealAt(101);
        if(ghost.visibleEnd!=ghost.pageEnd || ghost.state=="speak")throw new InvalidOperationException("Replayed page");
        ghost.DismissReply();ghost.AddReply("late chunk");
        if(ghost.reading)throw new InvalidOperationException("Dismissal reopened the cloud");
        if(DesktopIntent("Maybe open notepad later")!="Maybe open notepad later" ||
            DesktopIntent("Open Calculator")!="/open calculator" ||
            DesktopIntent("Look at my screen: Which error?")!="/screen ask Which error?")
            throw new InvalidOperationException("Explicit desktop intent");
        foreach(string phrase in new[]{"What am I doing right now.","  WHAT  I AM currently doing?!  ",
            "Please tell me what am I working on right now please."})
            if(DesktopIntent(phrase)!="/activity")throw new InvalidOperationException("Activity wording: "+phrase);
        foreach(string phrase in new[]{"What's playing?","What am I listening to right now.","what i ma listening to", "What music am I listening to?!"})
            if(DesktopIntent(phrase)!="/music current")throw new InvalidOperationException("Media wording: "+phrase);
        foreach(string phrase in new[]{"Can you see my screen.","What's on my screen?","What do you see on my screen?!"})
            if(!DesktopIntent(phrase).StartsWith("/screen ask ",StringComparison.Ordinal))throw new InvalidOperationException("Screen wording: "+phrase);
        foreach(string phrase in new[]{"Maybe open notepad later","Why is screen sharing risky?","What should I be working on?",
            "If I ask what am I doing, can you answer?","What is the best way to capture what's on my screen?","Would you skip this song?"})
            if(DesktopIntent(phrase)!=phrase)throw new InvalidOperationException("Unexpected local action: "+phrase);
        Uri url;
        if(!WebAddress("https://example.com/",out url)||WebAddress("file:///C:/test",out url)||WebAddress("https://user:pass@example.com",out url))
            throw new InvalidOperationException("Browser URL handling");
        Console.WriteLine("Desktop checks passed: composer, streaming, {0} measured pages, silent waits, dismissal and URL validation.",count);
    }
    void LocalReply(string message){
        ResetReply();AddReply(message);finished=true;
    }
 }
}
