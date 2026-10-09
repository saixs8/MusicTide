using SpectrumKlinePlayer;
using System.Reflection;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
class LiveCheck
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    static IntPtr Location(Point p)=>(IntPtr)((p.Y&65535)<<16|(p.X&65535));
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form1();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object Get(string name) => typeof(Form1).GetField(name, flags)!.GetValue(form)!;
        object? Call(string name, params object?[] values) => typeof(Form1).GetMethods(flags).Single(m => m.Name == name && m.DeclaringType == typeof(Form1)).Invoke(form, values);
        form.Show(); var watch = Stopwatch.StartNew(); int printed = -1;
        var previous = new float[((float[])Get("energyLevels")).Length]; float maxChange = 0;
        bool navigate = args.Contains("--navigate"); int navigationCount = 0; double nextNavigation = 1;
        while (watch.Elapsed.TotalSeconds < 7)
        {
            Application.DoEvents();
            if (navigate && watch.Elapsed.TotalSeconds >= nextNavigation)
            {
                nextNavigation += .2;
                var plot = (Rectangle)Get("terminalPlot");
                var point = new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);
                var stage = (Control)Get("stage"); stage.Focus();
                SendMessage(stage.Handle,0x20A,(IntPtr)(((navigationCount%2==0?120:-120)<<16)|8),Location(stage.PointToScreen(point)));
                SendMessage(stage.Handle,0x201,(IntPtr)1,Location(point));
                var moved=new Point(point.X+(navigationCount%2==0?60:-60),point.Y);
                SendMessage(stage.Handle,0x200,(IntPtr)1,Location(moved));
                SendMessage(stage.Handle,0x202,IntPtr.Zero,Location(moved));
                Call("OnKeyDown", stage, new KeyEventArgs(navigationCount % 2 == 0 ? Keys.Left : Keys.End));
                navigationCount++;
            }
            var levels = (float[])Get("energyLevels");
            if (watch.Elapsed.TotalSeconds > 1)
                maxChange = Math.Max(maxChange, levels.Zip(previous, (a, b) => Math.Abs(a - b)).Max());
            Array.Copy(levels, previous, levels.Length);
            int second = (int)watch.Elapsed.TotalSeconds;
            if (second != printed && second % 2 == 0)
            {
                printed = second;
                Console.WriteLine($"{second}s track={Get("currentTrackTitle")} lines={((TimedLyricLine[])Get("lyricTimeline")).Length} active={Get("activeLyricIndex")} position={((LyricPlaybackClock)Get("lyricClock")).Position:0.00} status={Get("lyricTimingStatus")} volume={Get("volumeAvailable")}/{Get("playerVolume")} source={WindowsMediaSessionCapture.ActiveSourceApp}");
                Console.WriteLine($"audio packets={Get("audioBufferCount")} rms={Get("latestAudioRms")} maxBar={levels.Max():F3} modeSimulation={Get("simulationMode")} capture={Get("captureStatus")}");
            }
            Thread.Sleep(15);
        }
        Console.WriteLine($"Observed spectrum change per sample: {maxChange:F4} (zero is valid if silent)");
        if (navigate) Console.WriteLine($"Navigation cycles during live capture: {navigationCount}; native window messages for Ctrl wheel and left/right drag, navigation keys; playback controls unchanged.");
        using var bmp = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
        bmp.Save("Docs/UiPreview/Terminal_LiveLyrics.png", ImageFormat.Png); form.Close();
    }
}
