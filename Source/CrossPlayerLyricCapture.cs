using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpectrumKlinePlayer;

public sealed record PlayerLyricDocument(string Key, string Source, double Duration, TimedLyricLine[] Lines);

// Lyrics and clock are separate: SMTC provides metadata/time, not the lyric text.
public static class CrossPlayerLyricCapture
{
    private static readonly object gate = new();
    private static string currentKey = "";
    private static PlayerLyricDocument? current;
    private static Task<PlayerLyricDocument?>? pending;
    private static CancellationTokenSource? requestCts;
    private static DateTime nextRetry;
    private static readonly HttpClient http = CreateClient();
    public static string Status { get; private set; } = "等待歌曲信息";
    public static string CacheDirectory => Path.Combine(AppContext.BaseDirectory, "Data", "LyricCache");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MusicTide/0.9.7");
        return client;
    }

    public static PlayerLyricDocument? Poll(string title, string artist, string song, double duration, CancellationToken token)
    {
        string key = MusicPlayerProfiles.Key(title) + "|" + (duration >= 2 ? Math.Round(duration).ToString(CultureInfo.InvariantCulture) : "unknown");
        lock (gate)
        {
            if (key != currentKey)
            {
                requestCts?.Cancel(); requestCts?.Dispose(); requestCts = null;
                currentKey = key; current = null; pending = null; nextRetry = DateTime.MinValue;
            }
            if (pending is { IsCompleted: true })
            {
                try { current = pending.GetAwaiter().GetResult(); } catch { current = null; }
                pending = null;
                nextRetry = DateTime.UtcNow.AddMinutes(2);
                Status = current is null ? "未找到匹配的时间轴 · 可开启桌面歌词" : current.Source;
            }
            if (current is null && pending is null && DateTime.UtcNow >= nextRetry)
            {
                requestCts?.Dispose();
                requestCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestCts.CancelAfter(TimeSpan.FromSeconds(25));
                var ct = requestCts.Token;
                Status = "正在匹配歌词时间轴";
                pending = Task.Run(() => LoadAsync(key, title, artist, song, duration, ct), ct);
            }
            return current?.Key == key ? current : null;
        }
    }

    public static async Task<PlayerLyricDocument?> LoadAsync(string key, string title, string artist, string song, double duration, CancellationToken token)
    {
        string path = Path.Combine(CacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length < 2 * 1024 * 1024)
            {
                var saved = JsonSerializer.Deserialize<PlayerLyricDocument>(await File.ReadAllTextAsync(path, token));
                if (saved?.Key == key && ValidLines(saved.Lines)) return saved;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        var local = LoadLocal(key, title, artist, song, duration);
        if (local is not null) return local;
        bool nativeOrientationUnknown = string.IsNullOrWhiteSpace(song);
        if (string.IsNullOrWhiteSpace(song))
        {
            // Native window titles vary between artist-song and song-artist.
            string[] parts = Regex.Split(title, @"\s+[-–—]\s+");
            song = parts.Length == 2 ? parts[1] : title;
            artist = parts.Length == 2 ? parts[0] : "";
        }
        foreach (var provider in new Func<Task<PlayerLyricDocument?>>[]
                 { () => NeteaseAsync(key, song, artist, duration, token),
                   () => nativeOrientationUnknown && artist.Length > 0 ? NeteaseAsync(key, artist, song, duration, token) : Task.FromResult<PlayerLyricDocument?>(null),
                   () => LrcLibAsync(key, song, artist, duration, token),
                   () => nativeOrientationUnknown && artist.Length > 0 ? LrcLibAsync(key, artist, song, duration, token) : Task.FromResult<PlayerLyricDocument?>(null) })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (await provider() is { } result)
                {
                    try
                    {
                        Directory.CreateDirectory(CacheDirectory);
                        string temp = path + ".tmp";
                        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(result), token);
                        File.Move(temp, path, true);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    return result;
                }
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or FormatException or InvalidOperationException)
            { if (token.IsCancellationRequested) throw; }
        }
        return null;
    }

    private static PlayerLyricDocument? LoadLocal(string key, string title, string artist, string song, double duration)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] folders = [Path.Combine(AppContext.BaseDirectory, "Data", "Lyrics"),
            Path.Combine(docs, "QQMusic", "Lyric"), Path.Combine(roaming, "Tencent", "QQMusic", "Lyric"),
            Path.Combine(local, "NetEase", "CloudMusic", "webdata", "lyric"), Path.Combine(docs, "CloudMusic", "Lyric")];
        foreach (string folder in folders)
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder, "*.lrc", SearchOption.TopDirectoryOnly).Take(500))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string normalized = MusicPlayerProfiles.Key(name);
                    string[] titleParts = Regex.Split(title, @"\s+[-–—]\s+");
                    if (normalized != MusicPlayerProfiles.Key(title)
                        && !(titleParts.Length == 2 && normalized == MusicPlayerProfiles.Key(titleParts[1] + titleParts[0]))
                        && normalized != MusicPlayerProfiles.Key(song + artist)
                        && normalized != MusicPlayerProfiles.Key(artist + song)
                        && !(artist.Length == 0 && normalized == MusicPlayerProfiles.Key(song))) continue;
                    if (new FileInfo(file).Length > 2 * 1024 * 1024) continue;
                    var lines = ParseLrc(File.ReadAllText(file));
                    if (lines.Length > 0) return new(key, "本地 LRC", duration, lines);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private static async Task<PlayerLyricDocument?> NeteaseAsync(string key, string song, string artist, double duration, CancellationToken token)
    {
        string url = "https://music.163.com/api/search/get?type=1&limit=15&s=" + Uri.EscapeDataString(song + " " + artist);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri("https://music.163.com/");
        using var response = await http.SendAsync(request, token); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!json.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("songs", out var songs)) return null;
        var matches = new List<(long Id, double Duration, double Score)>();
        foreach (var item in songs.EnumerateArray())
        {
            string name = String(item, "name");
            string singers = item.TryGetProperty("artists", out var a) ? string.Join(" / ", a.EnumerateArray().Select(x => String(x, "name"))) : "";
            double length = Number(item, "duration") / 1000;
            double score = MatchScore(song, artist, duration, name, singers, length);
            if (score >= 0 && item.TryGetProperty("id", out var id)) matches.Add((id.GetInt64(), length, score));
        }
        foreach (var item in matches.OrderByDescending(m => m.Score).Take(2))
        {
            using var lyricResponse = await http.GetAsync($"https://music.163.com/api/song/lyric?id={item.Id}&lv=-1&kv=-1&tv=-1", token);
            lyricResponse.EnsureSuccessStatusCode();
            using var lyrics = JsonDocument.Parse(await lyricResponse.Content.ReadAsStringAsync(token));
            if (!lyrics.RootElement.TryGetProperty("lrc", out var lrc)) continue;
            var lines = ParseLrc(String(lrc, "lyric"));
            if (lines.Length > 0) return new(key, "网易云歌词源 · 匹配时间轴", item.Duration, lines);
        }
        return null;
    }

    private static async Task<PlayerLyricDocument?> LrcLibAsync(string key, string song, string artist, double duration, CancellationToken token)
    {
        using var response = await http.GetAsync("https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(song)
            + "&artist_name=" + Uri.EscapeDataString(artist), token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var candidates = json.RootElement.EnumerateArray().Select(item => new
        { Item = item, Score = MatchScore(song, artist, duration, String(item, "trackName"), String(item, "artistName"), Number(item, "duration")) });
        foreach (var match in candidates.Where(c => c.Score >= 0).OrderByDescending(c => c.Score))
        {
            var lines = ParseLrc(String(match.Item, "syncedLyrics"));
            if (lines.Length > 0) return new(key, "LRCLIB · 匹配时间轴", Number(match.Item, "duration"), lines);
        }
        return null;
    }

    // Exact title and artist plus duration guard prevent covers/live versions lending a wrong clock.
    public static double MatchScore(string song, string artist, double duration, string candidateSong, string candidateArtist, double candidateDuration)
    {
        if (artist.Length == 0 && duration < 2) return -1;
        if (MusicPlayerProfiles.Key(song) != MusicPlayerProfiles.Key(candidateSong)) return -1;
        if (artist.Length > 0 && !Regex.Split(candidateArtist, @"\s*[/、,&;]\s*")
            .Any(a => MusicPlayerProfiles.Key(a) == MusicPlayerProfiles.Key(artist))) return -1;
        if (duration >= 2 && (candidateDuration < 2 || Math.Abs(duration - candidateDuration) > 4)) return -1;
        return 100 - (duration >= 2 ? Math.Abs(duration - candidateDuration) : 0);
    }

    public static TimedLyricLine[] ParseLrc(string text)
    {
        if (text.Length > 2 * 1024 * 1024) return [];
        text = WebUtility.HtmlDecode(text);
        var offset = Regex.Match(text, @"\[offset:\s*(-?\d+)\]", RegexOptions.IgnoreCase);
        double shift = offset.Success && double.TryParse(offset.Groups[1].Value, out var ms) ? ms / 1000 : 0;
        var result = new List<TimedLyricLine>();
        foreach (string raw in text.Split('\n').Take(20000))
        {
            var tags = Regex.Matches(raw, @"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]");
            if (tags.Count == 0) continue;
            string line = Regex.Replace(raw, @"\[[^\]]*\]|<\d+:\d+(?:\.\d+)?>", "").Trim();
            foreach (Match tag in tags)
            {
                int sec = int.Parse(tag.Groups[2].Value);
                if (sec >= 60) continue;
                double fraction = tag.Groups[3].Success ? int.Parse(tag.Groups[3].Value) / Math.Pow(10, tag.Groups[3].Length) : 0;
                double start = int.Parse(tag.Groups[1].Value) * 60 + sec + fraction - shift;
                if (start <= 86400) result.Add(new(Math.Max(0, start), line));
            }
        }
        // Keep empty timed lines: an instrumental section must clear the previous lyric.
        return result.OrderBy(l => l.StartSeconds).GroupBy(l => l.StartSeconds)
            .Select(g => new TimedLyricLine(g.Key, string.Join(" / ", g.Select(l => l.Text).Where(t => t.Length > 0).Distinct())))
            .Take(20000).ToArray();
    }

    private static bool ValidLines(TimedLyricLine[]? lines) => lines is { Length: > 0 and <= 20000 }
        && lines.All(l => double.IsFinite(l.StartSeconds) && l.StartSeconds is >= 0 and <= 86400 && l.Text is { Length: <= 2000 })
        && lines.Zip(lines.Skip(1)).All(p => p.First.StartSeconds <= p.Second.StartSeconds);
    private static string String(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static double Number(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d) ? d : -1;
}
