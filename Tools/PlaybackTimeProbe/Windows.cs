using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using SpectrumKlinePlayer;
class WindowProbe
{
    delegate bool Callback(IntPtr hwnd, IntPtr arg);
    [DllImport("user32.dll")] static extern bool EnumWindows(Callback cb, IntPtr arg);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int count);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    static void ScanClock(AutomationElement root) { var watch=Stopwatch.StartNew(); var stack=new Stack<(AutomationElement Node,int Depth)>(); stack.Push((root,0)); int count=0; var cache=new CacheRequest(); cache.Add(AutomationElement.NameProperty); cache.Add(AutomationElement.AutomationIdProperty); cache.Add(AutomationElement.ControlTypeProperty); using(cache.Activate()){while(stack.Count>0&&count<400&&watch.Elapsed.TotalSeconds<10){var (node,depth)=stack.Pop(); var children=node.FindAll(TreeScope.Children,Condition.TrueCondition); foreach(AutomationElement child in children){string name=child.Cached.Name; count++; if(count<40||KugouScreenLyricCapture.TryParseProgress(name)!=null)Console.WriteLine($"    {depth} {child.Cached.ControlType.ProgrammaticName} id={child.Cached.AutomationId} name={name}"); if(KugouScreenLyricCapture.TryParseProgress(name)!=null){Console.WriteLine($"FOUND clock after {watch.Elapsed.TotalSeconds:F2}s, {count} nodes");return;} if(depth<8)stack.Push((child,depth+1));}}} Console.WriteLine($"SCANNED {count} nodes {watch.Elapsed.TotalSeconds:F2}s"); }
    public static void Run()
    {
        var ids=Process.GetProcessesByName("KuGou").Select(p=>{using(p)return (uint)p.Id;}).ToHashSet();
        EnumWindows((hwnd,_)=>{
            GetWindowThreadProcessId(hwnd,out uint pid); if(!ids.Contains(pid))return true;
            var text=new StringBuilder(512);GetWindowText(hwnd,text,text.Capacity);
            var cls=new StringBuilder(256);GetClassName(hwnd,cls,cls.Capacity);
            Console.WriteLine($"Window={hwnd} PID={pid} Visible={IsWindowVisible(hwnd)} Class={cls} Title={text}");
            if(text.ToString().Contains(" - "))
            {
                try{
                    var cache=new CacheRequest();cache.Add(AutomationElement.NameProperty);
                    using(cache.Activate()){
                        Console.WriteLine("  Before root"); var root=AutomationElement.FromHandle(hwnd); Console.WriteLine("  Before children"); var children=root.FindAll(TreeScope.Children,Condition.TrueCondition);
                        Console.WriteLine($"  Children={children.Count}");
                        foreach(AutomationElement element in children){string name=element.Cached.Name;Console.WriteLine($"  CHILD {name}"); ScanClock(element);}
                    }
                }catch(Exception e){Console.WriteLine(e.Message);}
            }
            return true;
        },IntPtr.Zero);
    }
}
