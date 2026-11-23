using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Threading.Tasks;
using Drawing=System.Drawing;

namespace MellowDesktop {
 public sealed partial class Ghost {
    bool inspecting;
    async void InspectRegion(string task){
        if(inspecting)return;
        inspecting=true;
        var picker=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            Left=0,Top=0,Width=SystemParameters.PrimaryScreenWidth,Height=SystemParameters.PrimaryScreenHeight,
            AllowsTransparency=true,Background=new SolidColorBrush(Color.FromArgb(45,40,28,60)),Topmost=true,
            ShowInTaskbar=false,Cursor=Cursors.Cross};
        var canvas=new Canvas();picker.Content=canvas;
        canvas.Children.Add(new TextBlock{Text="Drag over the area to send once to local LM Studio • Esc cancels",
            Background=Brushes.White,Foreground=Brushes.Indigo,Padding=new Thickness(8)});
        var outline=new Border{BorderBrush=Brushes.White,BorderThickness=new Thickness(2)};
        canvas.Children.Add(outline);Point start=new Point();Rect area=Rect.Empty;
        picker.MouseLeftButtonDown+=(s,e)=>{start=e.GetPosition(canvas);canvas.CaptureMouse();};
        picker.MouseMove+=(s,e)=>{if(!canvas.IsMouseCaptured)return;
            var rect=new Rect(start,e.GetPosition(canvas));Canvas.SetLeft(outline,rect.Left);Canvas.SetTop(outline,rect.Top);
            outline.Width=rect.Width;outline.Height=rect.Height;};
        picker.MouseLeftButtonUp+=(s,e)=>{if(!canvas.IsMouseCaptured)return;
            var rect=new Rect(start,e.GetPosition(canvas));canvas.ReleaseMouseCapture();
            if(rect.Width>=12 && rect.Height>=12)area=new Rect(canvas.PointToScreen(rect.TopLeft),canvas.PointToScreen(rect.BottomRight));
            picker.Close();};
        picker.KeyDown+=(s,e)=>{if(e.Key==Key.Escape)picker.Close();};
        try {
            window.Hide();picker.ShowDialog();
            if(area.IsEmpty)return;
            await Task.Delay(160);
            if(quitting)return;
            byte[] bytes=CaptureArea(area);
            window.Show();Signal("think","Checking your selection locally…",35);
            string[] lines=await Task.Run(()=>AskVision(bytes,task));
            if(quitting)return;
            string answer=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(lines[0]));
            if(lines.Length>1){
                var coord=lines[1].Split(' ');
                var pt=new Point(area.X+area.Width*double.Parse(coord[0],CultureInfo.InvariantCulture),
                    area.Y+area.Height*double.Parse(coord[1],CultureInfo.InvariantCulture));
                pt=PresentationSource.FromVisual(window).CompositionTarget.TransformFromDevice.Transform(pt);
                CancelTarget();perch=IntPtr.Zero;guiding=true;pointCaption=answer;pointingLeft=pt.X<SystemParameters.PrimaryScreenWidth/2;
                Travel(new Point(pt.X-window.Width/2+(pointingLeft?1:-1)*size*.36,pt.Y-window.Height+size*.47),walkTime,false);
                holdUntil=departure+travel+4;
            }
            Signal("offer",answer,9);
            LocalReply(answer);
        } catch(Exception){if(!quitting)Signal("offer","Local vision failed. Check the LM Studio vision model.",5);}
        finally{inspecting=false;if(!quitting)window.Show();}
    }
    static string[] AskVision(byte[] bytes,string task,string question=""){
        var model=Environment.GetEnvironmentVariable("MELLOW_VISION_MODEL")??"qwen3-vl-4b-instruct";
        if(!System.Text.RegularExpressions.Regex.IsMatch(model,@"^[a-zA-Z0-9_./:-]+$"))throw new ArgumentException();
        var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("MELLOW_PYTHON"),
            "-m mellow.local_vision "+task+" "+model){UseShellExecute=false,RedirectStandardInput=true,
            RedirectStandardOutput=true,CreateNoWindow=true};
        using(var proc=Process.Start(info)){
            proc.StandardInput.WriteLine(Convert.ToBase64String(bytes));proc.StandardInput.WriteLine(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(question)));proc.StandardInput.Close();
            var text=proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();if(proc.ExitCode!=0)throw new IOException();
            return text.Trim().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
        }
    }
 }
}
