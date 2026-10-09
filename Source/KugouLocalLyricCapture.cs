using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace SpectrumKlinePlayer;

public static partial class KugouLocalLyricCapture
{
    private static readonly byte[] KrcKey =
    [
        0x40, 0x47, 0x61, 0x77, 0x5E, 0x32, 0x74, 0x47,
        0x51, 0x36, 0x31, 0x2D, 0xCE, 0xD2, 0x6E, 0x69
    ];

    private static readonly object sync = new();
    private static readonly List<string> lyricFiles = new();
    private static DateTime nextFileScanUtc;
    private static string currentTrackKey = string.Empty;
    private static string currentTrackTitle = string.Empty;
    private static List<KrcLine>? currentLines;
    private static DateTime nextTrackRetryUtc;

    public static TimedLyricLine[] GetTimeline(string trackTitle)
    {
        lock (sync)
        {
            EnsureTrack(trackTitle);
            return currentLines?.Select(line => new TimedLyricLine(line.StartMs / 1000.0, line.Text)).ToArray() ?? [];
        }
    }
    private static string lastLiveLookupKey = string.Empty;
    private static DateTime nextLiveLookupUtc;
    private static LiveLyricMatch? lastLiveMatch;

    public static LyricFrame? TryCapture(
        MediaTrackInfo? mediaTrack = null,
        string? liveLyricText = null,
        string? trackTitleOverride = null)
    {
        lock (sync)
        {
            string normalizedLiveText = NormalizeLyricText(liveLyricText);
            string? trackTitle = !string.IsNullOrWhiteSpace(trackTitleOverride)
                ? trackTitleOverride
                : mediaTrack is { } info && !string.IsNullOrWhiteSpace(info.Title)
                    ? info.Title
                    : FindCurrentTrackTitle();
            if (string.IsNullOrWhiteSpace(trackTitle))
            {
                if (normalizedLiveText.Length < 2
                    || TryLoadByLiveText(normalizedLiveText) is not { } liveOnlyMatch)
                {
                    return null;
                }

                ApplyLiveMatch(liveOnlyMatch);
                string liveOnlyTimeTag = TimeSpan.FromMilliseconds(Math.Max(0, liveOnlyMatch.Line.StartMs)).ToString(@"mm\:ss");
                return new LyricFrame(liveOnlyMatch.Line.Text, liveOnlyTimeTag, $"酷狗本地KRC/{currentTrackTitle}");
            }

            EnsureTrack(trackTitle);

            double positionMs = mediaTrack is { } currentInfo && currentInfo.PositionSeconds >= 0
                ? currentInfo.PositionSeconds * 1000.0
                : -1;

            KrcLine? active = null;
            bool matchedLiveText = false;
            if (normalizedLiveText.Length >= 2)
            {
                if (currentLines is { Count: > 0 })
                {
                    active = FindLiveLine(currentLines, normalizedLiveText);
                    matchedLiveText = active is not null;
                }

                if (!matchedLiveText && TryLoadByLiveText(normalizedLiveText, trackTitle) is { } liveMatch)
                {
                    ApplyLiveMatch(liveMatch);
                    active = liveMatch.Line;
                    matchedLiveText = true;
                }
            }

            if (active is null && positionMs >= 0)
            {
                active = currentLines is { Count: > 0 }
                    ? currentLines.LastOrDefault(line => line.StartMs <= positionMs)
                    : null;
            }

            if (active is not { } line || string.IsNullOrWhiteSpace(line.Text))
            {
                return null;
            }

            double displayMs = matchedLiveText ? line.StartMs : positionMs;
            string timeTag = TimeSpan.FromMilliseconds(Math.Max(0, displayMs)).ToString(@"mm\:ss");
            return new LyricFrame(line.Text, timeTag, $"酷狗本地KRC/{currentTrackTitle}");
        }
    }

    public static double? TryGetDurationSeconds(string? trackTitle = null)
    {
        lock (sync)
        {
            string? title = string.IsNullOrWhiteSpace(trackTitle) ? FindCurrentTrackTitle() : trackTitle;
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            EnsureTrack(title);
            if (currentLines is not { Count: > 0 })
            {
                return null;
            }

            return (currentLines[^1].StartMs + 5000) / 1000.0;
        }
    }

    public static double? TryGetLineStartSeconds(string trackTitle, string liveText, double? expectedSeconds = null)
    {
        lock (sync)
        {
            EnsureTrack(trackTitle);
            string normalized = NormalizeLyricText(liveText);
            if (normalized.Length < 2 || currentLines is not { Count: > 0 })
            {
                return null;
            }

            var candidates = expectedSeconds is { } position
                ? currentLines.OrderBy(line => Math.Abs(line.StartMs / 1000.0 - position)).ToArray()
                : currentLines.ToArray();
            return FindLiveLine(candidates, normalized) is { } line
                ? line.StartMs / 1000.0
                : null;
        }
    }

    public static string? TryGetCurrentTrackTitle()
    {
        lock (sync)
        {
            return currentLines is { Count: > 0 } && !string.IsNullOrWhiteSpace(currentTrackTitle)
                ? currentTrackTitle
                : null;
        }
    }

    private static void EnsureTrack(string trackTitle)
    {
        string trackKey = Normalize(trackTitle);
        if (string.Equals(trackKey, currentTrackKey, StringComparison.Ordinal)
            && (currentLines is { Count: > 0 } || DateTime.UtcNow < nextTrackRetryUtc))
        {
            return;
        }

        bool trackChanged = !string.Equals(trackKey, currentTrackKey, StringComparison.Ordinal);
        currentTrackKey = trackKey;
        currentTrackTitle = trackTitle;
        // A newly selected song may have just downloaded its cache. Do not wait
        // for the previous song's 10-second directory scan to expire.
        if (trackChanged) nextFileScanUtc = DateTime.MinValue;
        currentLines = LoadMatchingLyric(trackTitle);
        nextTrackRetryUtc = DateTime.UtcNow.AddSeconds(1);
    }

    private static List<KrcLine>? LoadMatchingLyric(string trackTitle)
    {
        RefreshFileList();
        string songPart = ExtractSongPart(trackTitle);
        string normalizedSong = Normalize(songPart);
        string normalizedTitle = Normalize(trackTitle);
        if (normalizedSong.Length < 2)
        {
            return null;
        }

        string? bestFile = null;
        int bestScore = 0;
        DateTime bestTime = DateTime.MinValue;
        foreach (string file in lyricFiles)
        {
            string name = Normalize(Path.GetFileNameWithoutExtension(file));
            int score = name.Contains(normalizedSong, StringComparison.Ordinal) ? normalizedSong.Length * 3 : 0;
            if (normalizedTitle.Length >= 2 && name.Contains(normalizedTitle, StringComparison.Ordinal))
            {
                score += normalizedTitle.Length * 4;
            }
            if (score == 0)
            {
                continue;
            }

            DateTime writeTime = File.GetLastWriteTimeUtc(file);
            if (score > bestScore || score == bestScore && writeTime > bestTime)
            {
                bestFile = file;
                bestScore = score;
                bestTime = writeTime;
            }
        }

        return bestFile is null ? null : DecodeLyricFile(bestFile);
    }

    private static LiveLyricMatch? TryLoadByLiveText(string normalizedLiveText, string? preferredTrackTitle = null)
    {
        if (normalizedLiveText.Length < 2)
        {
            return null;
        }

        string lookupKey = $"{Normalize(preferredTrackTitle ?? string.Empty)}|{normalizedLiveText}";
        if (string.Equals(lookupKey, lastLiveLookupKey, StringComparison.Ordinal)
            && DateTime.UtcNow < nextLiveLookupUtc)
        {
            return lastLiveMatch;
        }

        RefreshFileList();
        string preferredSong = string.IsNullOrWhiteSpace(preferredTrackTitle)
            ? string.Empty
            : Normalize(ExtractSongPart(preferredTrackTitle));
        LiveLyricMatch? best = null;
        int bestScore = 0;
        DateTime bestTime = DateTime.MinValue;

        foreach (string file in lyricFiles)
        {
            if (preferredSong.Length >= 2 && !Normalize(Path.GetFileNameWithoutExtension(file)).Contains(preferredSong, StringComparison.Ordinal)) continue;
            List<KrcLine>? lines = DecodeLyricFile(file);
            if (lines is null || lines.Count == 0)
            {
                continue;
            }

            KrcLine? line = FindLiveLine(lines, normalizedLiveText);
            if (line is not { } matchedLine)
            {
                continue;
            }

            string fileTitle = Path.GetFileNameWithoutExtension(file);
            string normalizedFileTitle = Normalize(fileTitle);
            int score = NormalizeLyricText(matchedLine.Text) == normalizedLiveText ? 1000 : 700;
            if (preferredSong.Length >= 2 && normalizedFileTitle.Contains(preferredSong, StringComparison.Ordinal))
            {
                score += preferredSong.Length * 4;
            }

            DateTime writeTime = File.GetLastWriteTimeUtc(file);
            if (score > bestScore || score == bestScore && writeTime > bestTime)
            {
                best = new LiveLyricMatch(fileTitle, lines, matchedLine);
                bestScore = score;
                bestTime = writeTime;
            }
        }

        lastLiveLookupKey = lookupKey;
        lastLiveMatch = best;
        nextLiveLookupUtc = DateTime.UtcNow.AddSeconds(best is null ? 2 : 8);
        return best;
    }

    private static void ApplyLiveMatch(LiveLyricMatch match)
    {
        currentTrackTitle = match.TrackTitle;
        currentTrackKey = Normalize(match.TrackTitle);
        currentLines = match.Lines;
    }

    private static List<KrcLine>? DecodeLyricFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".lrc" => DecodeLrc(path),
            ".krc" => DecodeKrc(path),
            _ => DecodeLrc(path) ?? DecodeKrc(path)
        };
    }

    private static List<KrcLine>? DecodeKrc(string path)
    {
        try
        {
            byte[] encoded = File.ReadAllBytes(path);
            if (encoded.Length < 8 || Encoding.ASCII.GetString(encoded, 0, 4) != "krc1")
            {
                return null;
            }

            for (int i = 4; i < encoded.Length; i++)
            {
                encoded[i] ^= KrcKey[(i - 4) % KrcKey.Length];
            }

            byte[] plainBytes = TryDecompress(encoded, 4, encoded.Length - 4, zlib: true)
                ?? TryDecompress(encoded, 4, encoded.Length - 4, zlib: false)
                ?? (encoded.Length > 14 ? TryDecompress(encoded, 6, encoded.Length - 10) : null)
                ?? throw new InvalidDataException("KRC compression format is not supported");
            string text = Encoding.UTF8.GetString(plainBytes);
            var lines = new List<KrcLine>();
            foreach (string rawLine in text.Split('\n'))
            {
                Match match = KrcLinePattern().Match(rawLine.Trim('\r'));
                if (!match.Success || !int.TryParse(match.Groups["start"].Value, out int startMs))
                {
                    continue;
                }

                string lyric = Regex.Replace(match.Groups["text"].Value, @"<[^>]*>", "").Trim();
                lyric = Regex.Replace(lyric, @"\s+", " ");
                if (lyric.Length > 0 && !lyric.StartsWith("[", StringComparison.Ordinal))
                {
                    lines.Add(new KrcLine(startMs, lyric));
                }
            }

            return lines.OrderBy(line => line.StartMs).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static List<KrcLine>? DecodeLrc(string path)
    {
        try
        {
            string text = ReadTextBestEffort(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var lines = new List<KrcLine>();
            foreach (string rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                MatchCollection matches = LrcTimestampPattern().Matches(line);
                string lyric = Regex.Replace(line, @"(?:\[[0-9]{1,2}:[0-9]{2}(?:[.:][0-9]{1,3})?\])+", "").Trim();
                lyric = Regex.Replace(lyric, @"<[^>]*>", "").Trim();
                lyric = Regex.Replace(lyric, @"\s+", " ");

                if (matches.Count == 0)
                {
                    if (lyric.Length > 0 && !lyric.StartsWith("[", StringComparison.Ordinal))
                    {
                        lines.Add(new KrcLine(0, lyric));
                    }
                    continue;
                }

                if (lyric.Length == 0 || lyric.StartsWith("[", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in matches)
                {
                    if (!int.TryParse(match.Groups["min"].Value, out int min)
                        || !int.TryParse(match.Groups["sec"].Value, out int sec))
                    {
                        continue;
                    }

                    int frac = 0;
                    string fracText = match.Groups["frac"].Value;
                    if (fracText.Length > 0 && int.TryParse(fracText.PadRight(3, '0'), out int parsedFrac))
                    {
                        frac = parsedFrac;
                    }

                    int startMs = min * 60_000 + sec * 1_000 + frac;
                    lines.Add(new KrcLine(startMs, lyric));
                }
            }

            return lines.OrderBy(line => line.StartMs).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? TryDecompress(byte[] encoded, int offset, int count, bool zlib = false)
    {
        try
        {
            using var compressed = new MemoryStream(encoded, offset, count, writable: false);
            using Stream decoder = zlib
                ? new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: false)
                : new DeflateStream(compressed, CompressionMode.Decompress, leaveOpen: false);
            using var plain = new MemoryStream();
            decoder.CopyTo(plain);
            return plain.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string ReadTextBestEffort(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding[] encodings =
        [
            new UTF8Encoding(false, true),
            Encoding.Unicode,
            Encoding.BigEndianUnicode,
            Encoding.Default
        ];

        foreach (Encoding encoding in encodings)
        {
            try
            {
                return encoding.GetString(bytes);
            }
            catch
            {
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static void RefreshFileList()
    {
        if (DateTime.UtcNow < nextFileScanUtc)
        {
            return;
        }

        lyricFiles.Clear();
        foreach (string root in GetLyricRoots())
        {
            try
            {
                if (Directory.Exists(root))
                {
                    lyricFiles.AddRange(Directory.EnumerateFiles(root, "*.krc", SearchOption.TopDirectoryOnly));
                    lyricFiles.AddRange(Directory.EnumerateFiles(root, "*.lrc", SearchOption.TopDirectoryOnly));
                }
            }
            catch
            {
            }
        }

        nextFileScanUtc = DateTime.UtcNow.AddSeconds(1);
    }

    private static IEnumerable<string> GetLyricRoots()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? configuredPath = ReadConfiguredLyricPath(Path.Combine(roaming, "KuGou8", "KuGou.ini"));
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }
        yield return Path.Combine(roaming, "KuGou8", "Lyric");
        yield return Path.Combine(roaming, "KuGou", "Lyric");
        yield return Path.Combine(local, "KuGou8", "Lyric");
        yield return Path.Combine(local, "KuGou", "Lyric");
    }

    private static string? ReadConfiguredLyricPath(string iniPath)
    {
        try
        {
            foreach (string line in File.ReadLines(iniPath))
            {
                if (line.StartsWith("LyricPath=", StringComparison.OrdinalIgnoreCase))
                {
                    string path = line["LyricPath=".Length..].Trim().Trim('"');
                    return Path.IsPathFullyQualified(path) ? path : null;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static string? FindCurrentTrackTitle()
    {
        string? best = null;
        int bestScore = 0;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint processId);
            string processName;
            try
            {
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch
            {
                return true;
            }

            if (!processName.Contains("kugou", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string title = GetWindowText(hwnd);
            if (string.IsNullOrWhiteSpace(title) || title.Contains("桌面歌词", StringComparison.Ordinal))
            {
                return true;
            }

            title = Regex.Replace(title, @"\s*-\s*(?:酷狗音乐|QQ音乐|网易云音乐|酷我音乐)\s*$", "", RegexOptions.IgnoreCase).Trim();
            int score = title.Equals("酷狗音乐", StringComparison.OrdinalIgnoreCase)
                ? 0
                : title.Contains(" - ", StringComparison.Ordinal) ? 100 : title.Length;
            if (title.Length >= 2 && score > bestScore)
            {
                best = title;
                bestScore = score;
            }

            return true;
        }, IntPtr.Zero);

        return best;
    }

    private static string ExtractSongPart(string title)
    {
        string[] parts = title.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string song = parts.Length >= 2 ? parts[^1] : title;
        return Regex.Replace(song, @"\s*[\(（【\[].*?[\)）】\]]\s*$", "").Trim();
    }

    private static string Normalize(string value)
    {
        return Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{Nd}\u4e00-\u9fff]", "");
    }

    private static string NormalizeLyricText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : Normalize(value);
    }

    private static KrcLine? FindLiveLine(IReadOnlyList<KrcLine> lines, string normalizedLiveText)
    {
        foreach (KrcLine line in lines)
        {
            if (NormalizeLyricText(line.Text) == normalizedLiveText)
            {
                return line;
            }
        }

        foreach (KrcLine line in lines)
        {
            string candidate = NormalizeLyricText(line.Text);
            if (candidate.Length >= 4
                && (candidate.Contains(normalizedLiveText, StringComparison.Ordinal)
                    || normalizedLiveText.Contains(candidate, StringComparison.Ordinal)))
            {
                return line;
            }
        }

        // Chinese OCR occasionally changes one glyph. Accept a close match only
        // within the already identified track, never across the whole cache.
        if (normalizedLiveText.Length >= 6)
        {
            KrcLine? best = null;
            int bestDistance = int.MaxValue;
            foreach (KrcLine line in lines)
            {
                string candidate = NormalizeLyricText(line.Text);
                if (Math.Abs(candidate.Length - normalizedLiveText.Length) > 2) continue;
                int distance = EditDistance(candidate, normalizedLiveText);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = line;
                }
            }
            if (bestDistance <= Math.Max(1, normalizedLiveText.Length / 8)) return best;
        }

        return null;
    }

    private static int EditDistance(string left, string right)
    {
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        int[] current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private static string GetWindowText(IntPtr hwnd)
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

    private readonly record struct KrcLine(int StartMs, string Text);
    private readonly record struct LiveLyricMatch(string TrackTitle, List<KrcLine> Lines, KrcLine Line);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [GeneratedRegex(@"^\[(?<start>\d+)(?:,\d+)?\].*?(?<text>[^\r\n]+)$")]
    private static partial Regex KrcLinePattern();

    [GeneratedRegex(@"\[(?<min>\d{1,2}):(?<sec>\d{2})(?:[.:](?<frac>\d{1,3}))?\]")]
    private static partial Regex LrcTimestampPattern();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowTextNative(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowTextLength(IntPtr hwnd);
}
