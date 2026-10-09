using System.Diagnostics;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using System.Text;

namespace SpectrumKlinePlayer;

// Bind the clock label to its owning KuGou window; its own text has no song title.
public static class KugouPlaybackTimeCapture
{
    private static AutomationElement? clockElement;
    private static IntPtr clockWindow;
    private static string lastTitle = string.Empty;
    private static long lastSuccessTick;
    private static readonly object sync = new();
    private static Task<MediaTrackInfo?>? pending;
    private static MediaTrackInfo? latest;
    public static string LastStatus { get; private set; } = "等待播放器时间";
    public static bool HasRecentClock(string title) => title == lastTitle && lastSuccessTick != 0
        && Stopwatch.GetElapsedTime(lastSuccessTick).TotalSeconds < 4;

    public static MediaTrackInfo? TryCapture(string title, bool playing)
    {
        lock (sync)
        {
            if (pending is { IsCompleted: true })
            {
                latest = pending.GetAwaiter().GetResult();
                pending = null;
            }
            // Accessibility providers can block, especially while hidden or changing
            // songs. A single outstanding read must never hold up metadata/lyrics.
            if (pending is null) pending = Task.Run(() =>
            {
                try { return CaptureClock(title, playing); }
                catch { return null; }
            });
            return latest is { } value && value.Title == title && HasRecentClock(title)
                ? value with { IsPlaying = playing } : null;
        }
    }

    public static string? TryCaptureTitle()
    {
        return FindWindows().Select(window => ReadTitle(window)).FirstOrDefault(title => title is not null);
    }

    private static MediaTrackInfo? CaptureClock(string title, bool playing)
    {
        foreach (IntPtr window in FindWindows())
        {
                try
                {
                    if (ReadTitle(window) != title) continue;
                    // The clock control normally survives a track change in the same
                    // window. Keep it, while validating the current owning song.
                    if (clockWindow != window) clockElement = null;
                    clockWindow = window;
                    if (clockElement is not null)
                    {
                        try
                        {
                            if (Parse(clockElement.Current.Name) is { } cached) return cached;
                        }
                        catch { clockElement = null; }
                    }
                    var root = AutomationElement.FromHandle(window);
                    var request = new CacheRequest();
                    request.Add(AutomationElement.NameProperty);
                    request.TreeScope = TreeScope.Element;
                    var nodes = new Stack<(AutomationElement Node, int Depth)>();
                    nodes.Push((root, 0));
                    var scanClock = Stopwatch.StartNew();
                    int visited = 0;
                    using (request.Activate())
                    {
                        // Walk the bottom control strip first and stop at the clock.
                        // FindAll(Descendants) also walks the embedded browser and can
                        // take minutes when the player is hidden in the tray.
                        while (nodes.Count > 0 && visited < 400 && scanClock.Elapsed.TotalSeconds < 1.5)
                        {
                            var (node, depth) = nodes.Pop();
                            foreach (AutomationElement child in node.FindAll(TreeScope.Children, Condition.TrueCondition))
                            {
                                visited++;
                                if (Parse(child.Cached.Name) is { } actual)
                                {
                                    clockElement = child;
                                    return actual;
                                }
                                if (depth < 10) nodes.Push((child, depth + 1));
                            }
                        }
                    }

                    MediaTrackInfo? Parse(string text)
                    {
                        if (KugouScreenLyricCapture.TryParseProgress(text) is not { } time) return null;
                        // A song can change while accessibility data is being read.
                        if (ReadTitle(window) != title) return null;
                        lastTitle = title;
                        lastSuccessTick = Stopwatch.GetTimestamp();
                        LastStatus = $"酷狗真实进度 {TimeSpan.FromSeconds(time.Position):mm\\:ss}";
                        return new MediaTrackInfo(title, time.Duration, time.Position, playing);
                    }
                }
                catch { clockElement = null; }
        }
        LastStatus = "等待酷狗播放时间";
        return null;
    }

    private static List<IntPtr> FindWindows()
    {
        var ids = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("KuGou"))
            using (process) ids.Add((uint)process.Id);
        var windows = new List<IntPtr>();
        // MainWindowHandle excludes windows hidden to the tray. Enumerate native
        // top-level handles without changing visibility or activating the player.
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint id);
            if (ids.Contains(id) && ReadTitle(window) is not null) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static string? ReadTitle(IntPtr window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        string raw = text.ToString().Trim();
        const string suffix = " - 酷狗音乐";
        if (!raw.EndsWith(suffix, StringComparison.Ordinal) || raw.StartsWith("桌面歌词", StringComparison.Ordinal)) return null;
        string title = raw[..^suffix.Length].Trim();
        return title.Contains(" - ", StringComparison.Ordinal) ? title : null;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
}
