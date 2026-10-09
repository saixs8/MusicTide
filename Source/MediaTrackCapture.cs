using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace SpectrumKlinePlayer;

public static partial class MediaTrackCapture
{
    private static readonly string[] ProcessKeywords =
    [
        "kugou", "kgmusic", "qqmusic", "cloudmusic", "netease", "kuwo", "spotify"
    ];

    private static readonly string[] WindowKeywords =
    [
        "\u9177\u72d7", "\u684c\u9762\u6b4c\u8bcd", "QQ\u97f3\u4e50",
        "\u7f51\u6613\u4e91\u97f3\u4e50", "\u7f51\u6613\u4e91",
        "\u9177\u6211\u97f3\u4e50", "spotify"
    ];

    private static readonly string[] GenericTitles =
    [
        "\u9177\u72d7\u97f3\u4e50", "QQ\u97f3\u4e50", "\u7f51\u6613\u4e91\u97f3\u4e50",
        "\u9177\u6211\u97f3\u4e50", "\u684c\u9762\u6b4c\u8bcd", "\u6b4c\u8bcd", "Spotify"
    ];

    public static MediaTrackInfo? TryCapture()
    {
        Candidate? best = null;

        try
        {
            ScanWin32(AddText);
            ScanAutomation(AddText);
        }
        catch
        {
            return best is { } fallback
                ? new MediaTrackInfo(fallback.Title, fallback.DurationSeconds, fallback.PositionSeconds)
                : null;
        }

        return best is { } result
            ? new MediaTrackInfo(result.Title, result.DurationSeconds, result.PositionSeconds)
            : null;

        void AddText(string raw, int baseScore, string processName, bool isMainWindow)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            Match durationMatch = DurationPattern().Match(raw);
            if (!durationMatch.Success)
            {
                return;
            }

            double position = ToSeconds(durationMatch.Groups["pm"].Value, durationMatch.Groups["ps"].Value);
            double duration = ToSeconds(durationMatch.Groups["dm"].Value, durationMatch.Groups["ds"].Value);
            if (duration < 2 || duration > 24 * 60 * 60)
            {
                return;
            }

            string? title = CleanTrackTitle(RemoveTimeText(raw));
            if (title is null && isMainWindow)
            {
                title = CleanTrackTitle(raw);
            }

            if (title is null)
            {
                return;
            }

            int score = baseScore + 80 + ScoreTitleShape(title) + (isMainWindow ? 35 : 0);
            if (best is null || score > best.Value.Score)
            {
                best = new Candidate(title, duration, position, score);
            }
        }
    }

    public static string? TryCaptureTitle()
    {
        TitleCandidate? best = null;

        try
        {
            ScanWin32(AddTitle);
            ScanAutomation(AddTitle);
        }
        catch
        {
            return best?.Title;
        }

        return best?.Title;

        void AddTitle(string raw, int baseScore, string processName, bool isMainWindow)
        {
            string? title = CleanTrackTitle(raw);
            if (title is null)
            {
                return;
            }

            bool hasDuration = DurationPattern().IsMatch(raw);
            bool trackShaped = LooksLikeTrackTitle(title);
            if (!isMainWindow && !hasDuration && !trackShaped)
            {
                return;
            }

            int score = baseScore + ScoreTitleShape(title) + (isMainWindow ? 60 : 0) + (hasDuration ? 70 : 0);
            if (best is null || score > best.Value.Score)
            {
                best = new TitleCandidate(title, score);
            }
        }
    }

    private static void ScanWin32(Action<string, int, string, bool> addText)
    {
        uint currentProcessId = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == currentProcessId)
            {
                return true;
            }

            string processName = GetProcessName((int)processId);
            string title = GetWindowTextSafe(hwnd);
            string className = GetClassNameSafe(hwnd);
            int windowScore = ScoreWindow(title, className, processName);
            if (windowScore <= 0)
            {
                return true;
            }

            addText(title, windowScore + 40, processName, true);
            EnumChildWindows(hwnd, (child, _) =>
            {
                string childText = GetWindowTextSafe(child);
                if (!string.IsNullOrWhiteSpace(childText))
                {
                    addText(childText, windowScore, processName, false);
                }

                return true;
            }, IntPtr.Zero);

            return true;
        }, IntPtr.Zero);
    }

    private static void ScanAutomation(Action<string, int, string, bool> addText)
    {
        AutomationElementCollection windows = AutomationElement.RootElement
            .FindAll(TreeScope.Children, Condition.TrueCondition);

        foreach (AutomationElement window in windows)
        {
            string name = SafeGet(() => window.Current.Name, string.Empty);
            string className = SafeGet(() => window.Current.ClassName, string.Empty);
            string processName = GetProcessName(SafeGet(() => window.Current.ProcessId));
            int score = ScoreWindow(name, className, processName);
            if (score <= 0)
            {
                continue;
            }

            addText(name, score + 30, processName, true);

            AutomationElementCollection children;
            try
            {
                children = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            }
            catch
            {
                continue;
            }

            foreach (AutomationElement child in children)
            {
                addText(SafeGet(() => child.Current.Name, string.Empty), score, processName, false);
                if (TryGetValuePatternText(child) is { Length: > 0 } valueText)
                {
                    addText(valueText, score + 8, processName, false);
                }
            }
        }
    }

    private static int ScoreWindow(string name, string className, string processName)
    {
        int score = 0;
        foreach (string keyword in ProcessKeywords)
        {
            if (processName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }
        }

        string haystack = $"{name} {className}";
        foreach (string keyword in WindowKeywords)
        {
            if (haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 55;
            }
        }

        return score;
    }

    private static string GetProcessName(int processId)
    {
        if (processId <= 0)
        {
            return string.Empty;
        }

        try
        {
            return Process.GetProcessById(processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static double ToSeconds(string minutes, string seconds)
    {
        return int.TryParse(minutes, out int m) && int.TryParse(seconds, out int s)
            ? m * 60 + s
            : 0;
    }

    private static string RemoveTimeText(string raw)
    {
        string title = DurationPattern().Replace(raw, "");
        title = Regex.Replace(title, @"[\[\]\\|/]+", " ");
        return Regex.Replace(title, @"\s+", " ").Trim(' ', '-', ':', '\uFF1A');
    }

    private static string? CleanTrackTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string title = Regex.Replace(raw.Replace('\0', ' ').Trim(), @"\s+", " ");
        title = RemoveTimeText(title);
        title = Regex.Replace(
            title,
            @"\s*-\s*(?:\u9177\u72d7\u97f3\u4e50|QQ\u97f3\u4e50|\u7f51\u6613\u4e91\u97f3\u4e50|\u9177\u6211\u97f3\u4e50|Spotify)\s*$",
            "",
            RegexOptions.IgnoreCase).Trim();

        title = Regex.Replace(title, @"^\s*(?:\u6b63\u5728\u64ad\u653e|Now Playing)\s*[:\uFF1A-]\s*", "", RegexOptions.IgnoreCase).Trim();
        title = title.Trim(' ', '-', '|', ':', '\uFF1A', '\u300A', '\u300B');
        if (title.Length is < 2 or > 120)
        {
            return null;
        }

        foreach (string generic in GenericTitles)
        {
            if (title.Equals(generic, StringComparison.OrdinalIgnoreCase)
                || title.Contains(generic, StringComparison.OrdinalIgnoreCase) && title.Length <= generic.Length + 4)
            {
                return null;
            }
        }

        return title;
    }

    private static int ScoreTitleShape(string title)
    {
        int score = 0;
        if (title.Length is >= 4 and <= 80)
        {
            score += 35;
        }

        if (LooksLikeTrackTitle(title))
        {
            score += 80;
        }

        if (ChineseText().IsMatch(title))
        {
            score += 20;
        }

        return score;
    }

    private static bool LooksLikeTrackTitle(string title)
    {
        return title.Contains(" - ", StringComparison.Ordinal)
            || title.Contains(" – ", StringComparison.Ordinal)
            || title.Contains(" — ", StringComparison.Ordinal)
            || title.Contains("《", StringComparison.Ordinal)
            || title.Contains("》", StringComparison.Ordinal);
    }

    private static string GetWindowTextSafe(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowTextNative(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassNameSafe(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string? TryGetValuePatternText(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern) && pattern is ValuePattern valuePattern)
            {
                return valuePattern.Current.Value;
            }
        }
        catch
        {
        }

        return null;
    }

    private static T SafeGet<T>(Func<T> getter, T fallback = default!)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

    private readonly record struct Candidate(string Title, double DurationSeconds, double PositionSeconds, int Score);
    private readonly record struct TitleCandidate(string Title, int Score);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [GeneratedRegex(@"(?<pm>\d{1,3})\s*:\s*(?<ps>\d{2})\s*(?:/|\uFF0F)\s*(?<dm>\d{1,3})\s*:\s*(?<ds>\d{2})")]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"[\u4e00-\u9fff]")]
    private static partial Regex ChineseText();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwnd, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowTextNative(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
}

public readonly record struct MediaTrackInfo(string Title, double DurationSeconds, double PositionSeconds, bool IsPlaying = true);
