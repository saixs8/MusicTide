using SpectrumKlinePlayer;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
class Diagnostics {
 delegate bool Callback(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)] struct Rect{public int L,T,R,B;}
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback c,IntPtr p);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder t,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder t,int n);
 [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [STAThread] static void Main(){
 System.IO.Directory.CreateDirectory("Docs/Diagnostics");
 var t=WindowsMediaSessionCapture.TryCapture();Console.WriteLine(t); Console.WriteLine("UIAutomation: "+MediaTrackCapture.TryCapture());
 EnumWindows((h,p)=>{GetWindowThreadProcessId(h,out uint id);try{if(!Process.GetProcessById((int)id).ProcessName.Contains("kugou",StringComparison.OrdinalIgnoreCase))return true;}catch{return true;}
 GetWindowRect(h,out Rect r); if(h.ToInt64()==265574){using var img=new Bitmap(1060,720);using(var gx=Graphics.FromImage(img)){var dc=gx.GetHdc();Console.WriteLine("PRINT "+PrintWindow(h,dc,2));gx.ReleaseHdc(dc);}img.Save("Docs/Diagnostics/Minimized.png");}var text=new StringBuilder(1000);var cls=new StringBuilder(256);GetWindowText(h,text,1000);GetClassName(h,cls,256);
 if(text.ToString().Contains("桌面歌词")){using var lyricImage=new Bitmap(r.R-r.L,r.B-r.T);using(var lx=Graphics.FromImage(lyricImage)){var dc=lx.GetHdc();Console.WriteLine("HIDDEN LYRIC PRINT "+PrintWindow(h,dc,2));lx.ReleaseHdc(dc);}lyricImage.Save("Docs/Diagnostics/HiddenLyric.png");}
 Console.WriteLine($"WINDOW {h} visible={IsWindowVisible(h)} class={cls} rect={r.L},{r.T},{r.R},{r.B} title={text}");
 if(IsWindowVisible(h)&&r.R-r.L>150&&r.B-r.T>25&&r.R-r.L<2200&&r.B-r.T<1400){var bounds=Rectangle.Intersect(Rectangle.FromLTRB(r.L,r.T,r.R,r.B),SystemInformation.VirtualScreen);if(bounds.Width<150||bounds.Height<25)return true;
 var read=typeof(KugouScreenLyricCapture).GetMethod("ReadText",BindingFlags.Static|BindingFlags.NonPublic)!;try{foreach(string line in (IEnumerable<string>)read.Invoke(null,new object[]{bounds})!)Console.WriteLine("OCR RAW "+line);}catch(Exception ex){Console.WriteLine(ex.InnerException?.Message??ex.Message);}
 using var image=new Bitmap(bounds.Width,bounds.Height);using(var g=Graphics.FromImage(image))g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);image.Save($"Docs/Diagnostics/window_{h}.png");}
 return true;},IntPtr.Zero);
 }
}


