using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace SpectrumKlinePlayer;

public static partial class KugouDesktopLyricCapture
{
    private static readonly string[] ProcessKeywords =
    [
        "kugou", "kgmusic", "kugoumusic", "qqmusic", "cloudmusic", "kuwo"
    ];

    private static readonly string[] TextKeywords =
    [
        "\u9177\u72d7", "\u684c\u9762\u6b4c\u8bcd", "\u6b4c\u8bcd", "lyrics", "lyric", "kugou", "kg"
    ];

    private static readonly string[] RejectKeywords =
    [
        "\u6309\u94ae", "\u83dc\u5355", "\u641c\u7d22", "\u8bbe\u7f6e", "\u5173\u95ed", "\u6700\u5c0f\u5316", "\u6700\u5927\u5316",
        "\u64ad\u653e", "\u6682\u505c", "\u4e0a\u4e00\u9996", "\u4e0b\u4e00\u9996", "\u97f3\u91cf", "USB", "\u9891\u8c31", "K\u7ebf", "\u6d4b\u8bd5"
    ];

    public static LyricFrame? TryCapture()
    {
        try
        {
            if (TryCaptureWin32() is { } nativeFrame)
            {
                return nativeFrame;
            }

            AutomationElementCollection windows = AutomationElement.RootElement
                .FindAll(TreeScope.Children, Condition.TrueCondition);
            Candidate? best = null;

            foreach (AutomationElement window in windows)
            {
                int windowScore = ScoreWindow(window);
                string windowName = SafeGet(() => window.Current.Name, string.Empty);
                string windowClass = SafeGet(() => window.Current.ClassName, string.Empty);
                int windowProcessId = SafeGet(() => window.Current.ProcessId);
                string windowProcessName = GetProcessName((uint)Math.Max(0, windowProcessId));
                if (windowScore <= 0 || !IsMusicProcess(windowProcessName)
                    || !IsLyricWindow(windowName, windowClass))
                {
                    continue;
                }

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
                    AddCandidate(SafeGet(() => child.Current.Name, string.Empty), windowScore, child);
                    if (TryGetValuePatternText(child) is { Length: > 0 } valueText)
                    {
                        AddCandidate(valueText, windowScore + 10, child);
                    }
                    if (TryGetTextPatternText(child) is { Length: > 0 } textPatternText)
                    {
                        AddCandidate(textPatternText, windowScore + 14, child);
                    }
                }
            }

            return best is { } result
                ? new LyricFrame(result.Text, result.TimeTag, result.Source)
                : null;

            void AddCandidate(string? raw, int baseScore, AutomationElement element)
            {
                string? cleaned = CleanLyricText(raw);
                if (cleaned is null || IsGenericWindowText(cleaned))
                {
                    return;
                }

                int score = baseScore + ScoreText(cleaned);
                string? timeTag = ExtractTimeTag(raw ?? string.Empty);
                if (best is null || score > best.Value.Score)
                {
                    best = new Candidate(cleaned, timeTag, BuildSource(element), score);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    private static LyricFrame? TryCaptureWin32()
    {
        LyricFrame? found = null;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint processId);
            string processName = GetProcessName(processId);
            string title = GetWindowTextSafe(hwnd);
            string className = GetClassNameSafe(hwnd);
            if (!IsMusicProcess(processName))
            {
                return true;
            }
            if (!IsLyricWindow(title, className))
            {
                return true;
            }

            EnumChildWindows(hwnd, (child, _) =>
            {
                string raw = GetWindowTextSafe(child);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    raw = TryGetAutomationText(child) ?? string.Empty;
                }
                string? text = CleanLyricText(raw);
                if (text is not null && !IsGenericWindowText(text))
                {
                    string? timeTag = ExtractTimeTag(raw);
                    found = new LyricFrame(text, timeTag, $"酷狗桌面歌词窗口/{processName}");
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            if (found is null)
            {
                string? automationText = TryGetAutomationText(hwnd);
                if (automationText is not null)
                {
                    string? automationCandidate = CleanLyricText(automationText);
                    if (automationCandidate is not null && !IsGenericWindowText(automationCandidate))
                    {
                        found = new LyricFrame(automationCandidate, ExtractTimeTag(automationText), $"桌面歌词窗口/{processName}");
                    }
                }

                // A player title identifies the track; it is not the current lyric line.
            }

            return found is null;
        }, IntPtr.Zero);

        return found;
    }

    private static string GetProcessName(uint processId)
    {
        if (processId == 0)
        {
            return string.Empty;
        }

        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int ScoreWindow(string title, string className, string processName)
    {
        string haystack = $"{title} {className} {processName}";
        int score = 0;
        foreach (string keyword in TextKeywords)
        {
            if (haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 55;
            }
        }

        return score + (processName.Contains("kugou", StringComparison.OrdinalIgnoreCase) ? 90 : 0);
    }

    private static int ScoreWindow(AutomationElement element)
    {
        string name = SafeGet(() => element.Current.Name, string.Empty);
        string className = SafeGet(() => element.Current.ClassName, string.Empty);
        int processId = SafeGet(() => element.Current.ProcessId);
        string processName = string.Empty;

        if (processId > 0)
        {
            try
            {
                processName = Process.GetProcessById(processId).ProcessName;
            }
            catch
            {
            }
        }

        string haystack = $"{name} {className} {processName}";
        int score = 0;
        foreach (string keyword in ProcessKeywords)
        {
            if (processName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 90;
            }
        }

        foreach (string keyword in TextKeywords)
        {
            if (haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 55;
            }
        }

        return score;
    }

    private static int ScoreText(string text)
    {
        int score = ChineseText().IsMatch(text) ? 90 : 10;
        if (text.Length is >= 4 and <= 40)
        {
            score += 35;
        }
        else if (text.Length <= 70)
        {
            score += 10;
        }

        if (TimeTag().IsMatch(text))
        {
            score += 15;
        }

        foreach (string reject in RejectKeywords)
        {
            if (text.Contains(reject, StringComparison.OrdinalIgnoreCase))
            {
                score -= 70;
            }
        }

        return score;
    }

    private static string? CleanLyricText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string text = Regex.Replace(raw.Trim(), @"\s+", " ");
        text = TimeTag().Replace(text, "").Trim();
        text = text.Replace("\u9177\u72d7\u97f3\u4e50", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\u684c\u9762\u6b4c\u8bcd", "", StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '-', '|', ':', '\uFF1A');

        if (text.Length < 1 || text.Length > 70)
        {
            return null;
        }

        int rejectCount = RejectKeywords.Count(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        return rejectCount >= 2 ? null : text;
    }

    private static bool IsGenericWindowText(string text)
    {
        return text.Equals("酷狗音乐", StringComparison.OrdinalIgnoreCase)
            || text.Equals("桌面歌词", StringComparison.OrdinalIgnoreCase)
            || text.Equals("歌词", StringComparison.OrdinalIgnoreCase)
            || text.Contains(" - 酷狗音乐", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLyricWindow(string title, string className)
    {
        string identity = $"{title} {className}";
        return identity.Contains("桌面歌词", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("lyric", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("歌词秀", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMusicProcess(string processName)
    {
        return ProcessKeywords.Any(keyword => processName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractTimeTag(string raw)
    {
        Match match = TimeTag().Match(raw);
        return match.Success ? match.Value.Trim('[', ']') : null;
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

    private static string BuildSource(AutomationElement element)
    {
        int processId = SafeGet(() => element.Current.ProcessId);
        if (processId > 0)
        {
            try
            {
                return $"\u684c\u9762\u6b4c\u8bcd\u6743\u9650/{Process.GetProcessById(processId).ProcessName}";
            }
            catch
            {
            }
        }

        return "\u684c\u9762\u6b4c\u8bcd\u6743\u9650/UIAutomation";
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

    private static string? TryGetTextPatternText(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object pattern) && pattern is TextPattern textPattern)
            {
                return textPattern.DocumentRange.GetText(-1);
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryGetAutomationText(IntPtr hwnd)
    {
        try
        {
            AutomationElement element = AutomationElement.FromHandle(hwnd);
            string name = SafeGet(() => element.Current.Name, string.Empty);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            string? valueText = TryGetValuePatternText(element);
            if (!string.IsNullOrWhiteSpace(valueText))
            {
                return valueText;
            }

            return TryGetTextPatternText(element);
        }
        catch
        {
            return null;
        }
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

    private readonly record struct Candidate(string Text, string? TimeTag, string Source, int Score);

    [GeneratedRegex(@"\[[0-9]{1,2}:[0-9]{2}(?:[.:][0-9]{1,3})?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"[\u4e00-\u9fff]")]
    private static partial Regex ChineseText();

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

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

public readonly record struct LyricFrame(string Text, string? TimeTag, string Source);
