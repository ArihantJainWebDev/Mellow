using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MellowDesktop {
 public sealed partial class Ghost {
    Window checklist;
    void OpenCheckpoint(){
        var file=new OpenFileDialog{Filter="Project checklist (*.md;*.txt)|*.md;*.txt",Title="Choose a project checkpoint"};
        if(file.ShowDialog()!=true)return;
        try {
            string item=null;int number=0;
            using(var reader=new StreamReader(file.FileName)){
                if(reader.BaseStream.Length>1048576){Signal("offer","Choose a checklist smaller than 1 MB.",4);return;}
                while(!reader.EndOfStream){string line=reader.ReadLine();number++;
                    var match=Regex.Match(line,@"^\s*[-*]\s+\[ \]\s+(.+)$");
                    if(match.Success){item=match.Groups[1].Value;break;}}
            }
            if(item==null){Signal("offer","No unfinished Markdown checkbox found.",4);return;}
            if(checklist!=null)checklist.Close();
            var label=new TextBlock{Text=item,Margin=new Thickness(18),FontSize=16,TextWrapping=TextWrapping.Wrap};
            checklist=new Window{Title=Path.GetFileName(file.FileName)+" • line "+number,Width=420,
                SizeToContent=SizeToContent.Height,Content=label,WindowStartupLocation=WindowStartupLocation.CenterScreen};
            checklist.Closed+=delegate{checklist=null;};
            checklist.Show();checklist.UpdateLayout();
            var point=PresentationSource.FromVisual(window).CompositionTarget.TransformFromDevice.Transform(
                label.PointToScreen(new Point(0,label.ActualHeight*.5)));
            CancelTarget();perch=IntPtr.Zero;guiding=true;pointingLeft=false;pointCaption="First unfinished item";
            Travel(new Point(point.X-window.Width/2-size*.36,point.Y-window.Height+size*.47),walkTime,false);
            holdUntil=departure+travel+4;
        }catch(IOException){Signal("offer","Could not read that checkpoint.",3);}
         catch(UnauthorizedAccessException){Signal("offer","Could not access that checkpoint.",3);}
    }
 }
}
