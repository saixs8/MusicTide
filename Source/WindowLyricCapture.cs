using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SpectrumKlinePlayer;

public static partial class WindowLyricCapture
{
    private static readonly string[] MusicKeywords =
    [
        "\u9177\u72d7", "kugou", "QQ\u97f3\u4e50", "qqmusic", "\u7f51\u6613\u4e91", "cloudmusic", "kuwo",
        "\u9177\u6211", "foobar", "music", "\u6b4c\u8bcd", "lyric", "lyrics", "spotify"
    ];

    private static readonly string[] RejectKeywords =
    [
        "\u6309\u94ae", "button", "\u83dc\u5355", "menu", "\u8bbe\u7f6e", "\u641c\u7d22", "\u64ad\u653e", "\u6682\u505c",
        "\u4e0a\u4e00\u9996", "\u4e0b\u4e00\u9996", "\u5173\u95ed", "\u6700\u5c0f\u5316", "\u6700\u5927\u5316", "usb", "\u9891\u8c31", "k\u7ebf", "\u6d4b\u8bd5"
    ];

    public static string? TryCapture(IntPtr ownWindow)
    {
        Candidate? best = null;

        EnumWindows((hwnd, _) =>
        {
            if (hwnd == ownWindow || !IsWindowVisible(hwnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint processId);
            string processName = GetProcessName(processId);
            string title = GetWindowTextSafe(hwnd);
            string className = GetClassNameSafe(hwnd);
            int windowScore = ScoreWindow(title, className, processName);
            AddCandidate(title, windowScore + 15);

            EnumChildWindows(hwnd, (child, _) =>
            {
                AddCandidate(GetWindowTextSafe(child), windowScore);
                return true;
            }, IntPtr.Zero);

            return true;
        }, IntPtr.Zero);

        return best?.Text;

        void AddCandidate(string raw, int baseScore)
        {
            string? text = CleanCandidate(raw);
            if (text is null)
            {
                return;
            }

            int score = baseScore + ScoreText(text);
            if (best is null || score > best.Value.Score)
            {
                best = new Candidate(text, score);
            }
        }
    }

    private static int ScoreWindow(string title, string className, string processName)
    {
        string haystack = $"{title} {className} {processName}";
        int score = 0;
        foreach (string keyword in MusicKeywords)
        {
            if (haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 45;
            }
        }

        return score;
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

    private static int ScoreText(string text)
    {
        int score = ChineseText().IsMatch(text) ? 60 : 0;
        if (text.Length is >= 5 and <= 36)
        {
            score += 25;
        }
        else if (text.Length <= 60)
        {
            score += 10;
        }

        foreach (string reject in RejectKeywords)
        {
            if (text.Contains(reject, StringComparison.OrdinalIgnoreCase))
            {
                score -= 45;
            }
        }

        return score;
    }

    private static string? CleanCandidate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string text = Regex.Replace(raw.Trim(), @"\s+", " ");
        text = Regex.Replace(text, @"^\[[^\]]+\]\s*", "");
        text = text.Replace(" - \u9177\u72d7\u97f3\u4e50", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" - QQ\u97f3\u4e50", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" - \u7f51\u6613\u4e91\u97f3\u4e50", "", StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '-', '|', ':', '\uFF1A');

        return text.Length is >= 1 and <= 70 ? text : null;
    }

    private static string GetWindowTextSafe(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassNameSafe(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private readonly record struct Candidate(string Text, int Score);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [GeneratedRegex(@"[\u4e00-\u9fff]")]
    private static partial Regex ChineseText();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
