using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace SpectrumKlinePlayer;

public sealed record PlayerWindowSnapshot(string PlayerId, string? Title, double Position, double Duration,
    bool? Playing, string? Lyric, long Tick);

// One outstanding accessibility read: an unresponsive player never blocks SMTC polling.
public static class PlayerWindowCapture
{
    private static Task<PlayerWindowSnapshot?>? pending;
    private static string pendingKey = "";
    private static PlayerWindowSnapshot? latest;
    private static long nextScan;
    private static readonly object gate = new();

    public static PlayerWindowSnapshot? Poll(MusicPlayerProfile player, string? expectedTitle)
    {
        lock (gate)
        {
            if (pending is { IsCompleted: true })
            {
                try { latest = pending.GetAwaiter().GetResult(); } catch { latest = null; }
                pending = null;
            }
            string key = player.Id + "|" + expectedTitle;
            long now = Stopwatch.GetTimestamp();
            if (pending is null && (key != pendingKey || now >= nextScan))
            {
                pendingKey = key;
                nextScan = now + Stopwatch.Frequency / 2;
                pending = Task.Run(() => Read(player, expectedTitle));
            }
            return latest is { } value && value.PlayerId == player.Id
                && Stopwatch.GetElapsedTime(value.Tick).TotalSeconds < 2
                && (expectedTitle is null || MusicPlayerProfiles.Key(value.Title) == MusicPlayerProfiles.Key(expectedTitle)) ? value : null;
        }
    }

    public static MusicPlayerProfile? FindRunningPlayer()
    {
        MusicPlayerProfile? result = null;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var profile = Profile(h);
            if (profile is { Id: not "kugou" }) { result = profile; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static MusicPlayerProfile? Profile(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint id);
        if (id == Environment.ProcessId) return null;
        try { using var p = Process.GetProcessById((int)id); return MusicPlayerProfiles.Find(p.ProcessName); }
        catch { return null; }
    }

    private static PlayerWindowSnapshot? Read(MusicPlayerProfile player, string? expectedTitle)
    {
        var windows = new List<IntPtr>();
        EnumWindows((h, _) => { if (Profile(h)?.Id == player.Id) windows.Add(h); return true; }, IntPtr.Zero);
        PlayerWindowSnapshot? best = null;
        foreach (var h in windows)
        {
            try
            {
                string nativeBefore = Text(h);
                string? nativeTitle = MusicPlayerProfiles.CleanWindowTitle(nativeBefore);
                bool desktop = Regex.IsMatch(nativeBefore + " " + Class(h), "桌面歌词|歌词秀|lyric", RegexOptions.IgnoreCase);
                if (expectedTitle is not null && nativeTitle is not null
                    && !MusicPlayerProfiles.SameTrack(nativeTitle, expectedTitle)
                    && MusicPlayerProfiles.Key(nativeTitle) != MusicPlayerProfiles.Key(Regex.Split(expectedTitle, @"\s+[-–—]\s+").Last())) continue;
                var root = AutomationElement.FromHandle(h);
                var entries = new List<(string Text, string Id, ControlType Type)>();
                var stack = new Stack<(AutomationElement Element, int Depth)>();
                stack.Push((root, 0));
                var timer = Stopwatch.StartNew();
                while (stack.Count > 0 && entries.Count < 450 && timer.Elapsed.TotalSeconds < 1.5)
                {
                    var (node, depth) = stack.Pop();
                    var state = node.Current;
                    string text = state.Name;
                    if (node.TryGetCurrentPattern(ValuePattern.Pattern, out var v) && v is ValuePattern vp)
                        text += " " + vp.Current.Value;
                    entries.Add((text.Trim(), state.AutomationId, state.ControlType));
                    if (depth < 10)
                        foreach (AutomationElement child in node.FindAll(TreeScope.Children, Condition.TrueCondition)) stack.Push((child, depth + 1));
                }
                string? discovered = nativeTitle;
                if (discovered is null && expectedTitle is null && !desktop)
                {
                    discovered = entries.Where(e => Regex.IsMatch(e.Id, "song.?title|track.?title|song.?name|music.?name", RegexOptions.IgnoreCase))
                        .Select(e => MusicPlayerProfiles.CleanWindowTitle(e.Text)).FirstOrDefault(t => t is not null);
                }
                string? title = expectedTitle ?? discovered;
                if (title is null || Text(h) != nativeBefore) continue;
                var time = ParseProgress(entries.Select(e => e.Text));
                if (time is null && !desktop && GetForegroundWindow() == h && !IsIconic(h) && GetWindowRect(h, out var mainRect))
                {
                    var strip = Rectangle.Intersect(Rectangle.FromLTRB(mainRect.Left, Math.Max(mainRect.Top, mainRect.Bottom - 150), mainRect.Right, mainRect.Bottom), SystemInformation.VirtualScreen);
                    if (strip.Width is >= 100 and <= 2200 && strip.Height >= 20)
                    {
                        using var image = new Bitmap(strip.Width, strip.Height);
                        using (var g = Graphics.FromImage(image)) g.CopyFromScreen(strip.Location, Point.Empty, strip.Size);
                        time = ParseProgress(KugouScreenLyricCapture.ReadBitmapText(image));
                    }
                }
                bool? playing = entries.Any(e => e.Type == ControlType.Button && Regex.IsMatch(e.Text.Trim(), @"^(?:暂停(?:播放)?|Pause)$", RegexOptions.IgnoreCase)) ? true
                    : entries.Any(e => e.Type == ControlType.Button && Regex.IsMatch(e.Text.Trim(), @"^(?:播放|Play)$", RegexOptions.IgnoreCase)) ? false : null;
                string? lyric = desktop ? entries.Where(e => e.Type == ControlType.Text)
                    .Select(e => CleanLyric(e.Text, title)).FirstOrDefault(e => e is not null) : null;
                // Self-drawn desktop lyrics expose no text: OCR only the player's visible lyric window.
                if (desktop && lyric is null && IsWindowVisible(h) && !IsIconic(h) && GetWindowRect(h, out var r))
                {
                    var bounds = Rectangle.Intersect(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), SystemInformation.VirtualScreen);
                    if (bounds.Width is >= 100 and <= 2200 && bounds.Height is >= 20 and <= 320)
                    {
                        using var image = new Bitmap(bounds.Width, bounds.Height);
                        using (var g = Graphics.FromImage(image)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                        lyric = KugouScreenLyricCapture.ReadBitmapText(image).Select(t => CleanLyric(t, title)).FirstOrDefault(t => t is not null);
                    }
                }
                // Native title validated again after slow UI/OCR reads.
                if (Text(h) != nativeBefore) continue;
                var value = new PlayerWindowSnapshot(player.Id, title, time?.Position ?? -1, time?.Duration ?? -1, playing, lyric, Stopwatch.GetTimestamp());
                if (best is null) best = value;
                else if (!MusicPlayerProfiles.SameTrack(best.Title, value.Title)) continue;
                else best = best with { Position = value.Position >= 0 ? value.Position : best.Position,
                    Duration = value.Duration >= 2 ? value.Duration : best.Duration,
                    Playing = value.Playing ?? best.Playing, Lyric = value.Lyric ?? best.Lyric, Tick = value.Tick };
            }
            catch { /* The player may close or invalidate an automation node. */ }
        }
        return best;
    }

    public static (double Position, double Duration)? ParseProgress(IEnumerable<string> texts)
    {
        string[] values = texts.ToArray();
        foreach (string text in values)
            if (KugouScreenLyricCapture.TryParseProgress(text) is { } time) return time;
        // QQ/NetEase/Soda may expose current and total as separate controls.
        var seconds = values.Where(t => Regex.IsMatch(t.Trim(), @"^\d{1,3}[:：]\d{2}$"))
            .Select(t => t.Trim().Replace('：', ':').Split(':')).Select(p => (M: int.Parse(p[0]), S: int.Parse(p[1])))
            .Where(p => p.S < 60).Select(p => p.M * 60.0 + p.S).ToArray();
        return seconds.Length == 2 && seconds.Max() >= 2 ? (seconds.Min(), seconds.Max()) : null;
    }

    public static string? CleanLyric(string text, string title)
    {
        text = text.Trim();
        return text.Length is >= 2 and <= 100 && !MusicPlayerProfiles.IsGeneric(text)
            && MusicPlayerProfiles.Key(text) != MusicPlayerProfiles.Key(title)
            && !Regex.Split(title, @"\s+[-–—]\s+").Any(p => MusicPlayerProfiles.Key(p) == MusicPlayerProfiles.Key(text))
            && !Regex.IsMatch(text, @"^(?:关闭|锁定|解锁|设置|上一首|下一首|播放|暂停|音量|字体|颜色|翻译|原文|桌面歌词|\d{1,3}:\d{2})$") ? text : null;
    }

    private static string Text(IntPtr h) { var b = new StringBuilder(512); GetWindowText(h, b, b.Capacity); return b.ToString(); }
    private static string Class(IntPtr h) { var b = new StringBuilder(256); GetClassName(h, b, b.Capacity); return b.ToString(); }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool Callback(IntPtr h, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder b, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder b, int count);
}
