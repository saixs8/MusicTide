using System.IO;
using System.Text.Json;

namespace SpectrumKlinePlayer;

public sealed record SongSample(double Seconds, float Open, float High, float Low,
    float Close, float Energy, string Event, int Segment, int BarIndex = -1, double Volume = 0, double Amount = 0);
public sealed record SongBar(float Open, float High, float Low, float Close, int Index);
public sealed record LibrarySettings(int MaxSongs = 20);
public sealed record LibraryEntry(int Plays, DateTime LastPlayed, string LastSessionId = "");
public sealed record SongRecording(string Id, string Title, double Duration,
    DateTime RecordedAt, bool Simulated, bool AuthoritativeTime, SongSample[] Samples,
    TimedLyricLine[] Lyrics, string Personality = "", PlayEvent[]? Gameplay = null, double OpeningPrice = 100, SongBar[]? Bars = null, bool Completed = false, SongHeat? Heat = null, string SecurityCode = "", int SampleBand = -1, int ComparisonBand = -1)
{
    public override string ToString() => $"{Title} · {RecordedAt:MM-dd HH:mm:ss} · {Samples.Length}点 · {(Completed ? "已听完" : "片段")}{(Simulated ? " · 模拟" : "")}{(!AuthoritativeTime ? " · 估算时间" : "")}";
}

public static class SongRecordingStore
{
    public static void Save(string directory, SongRecording recording)
    {
        if (recording.Samples.Length == 0) return;
        Directory.CreateDirectory(directory);
        if (Path.GetFileName(recording.Id) != recording.Id || !System.Text.RegularExpressions.Regex.IsMatch(recording.Id, @"^[A-Za-z0-9_-]+$")) throw new IOException("Invalid recording ID");
        string path = Path.Combine(directory, recording.Id + ".json");
        bool fresh = !File.Exists(path);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(recording));
        File.Move(temporary, path, true);
        if (fresh)
        {
            var entries = ReadIndex(directory); string key = Key(recording.Title);
            entries.TryGetValue(key, out var old);
            int priorCount = old?.Plays ?? ReadAll(directory).Count(r => Key(r.Title) == key && r.Id != recording.Id);
            entries[key] = new(priorCount + (old?.LastSessionId == recording.Id ? 0 : 1), recording.RecordedAt, recording.Id);
            WriteAtomic(Path.Combine(directory, "library-index.json"), entries);
        }
        EnforceLimits(directory);
    }
    public static SongRecording[] Load(string directory) => Retained(ReadAll(directory), ReadIndex(directory), Settings(directory).MaxSongs).OrderByDescending(r => r.RecordedAt).ToArray();
    private static SongRecording[] ReadAll(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var result = new List<SongRecording>();
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Where(f => f.Name != "library-index.json" && f.Name != "library-settings.json").Take(10000))
        {
            try
            {
                if (file.Length > 32 * 1024 * 1024) continue;
                var item = JsonSerializer.Deserialize<SongRecording>(File.ReadAllText(file.FullName));
                if (item is { Samples.Length: > 0 and <= 20000, Lyrics: not null, Title: not null } && double.IsFinite(item.OpeningPrice) && item.OpeningPrice > 0 && double.IsFinite(item.Duration) && item.Duration > 0
                    && item.Samples.All(s => s is not null && double.IsFinite(s.Seconds) && s.Seconds >= 0 && float.IsFinite(s.Open) && float.IsFinite(s.High) && float.IsFinite(s.Low) && float.IsFinite(s.Close) && float.IsFinite(s.Energy) && double.IsFinite(s.Volume) && s.Volume >= 0 && double.IsFinite(s.Amount) && s.Amount >= 0 && s.Event is not null)
                    && item.Lyrics.All(l => double.IsFinite(l.StartSeconds) && l.Text is not null)
                    && (item.Bars is null || item.Bars.All(b => b is not null && float.IsFinite(b.Open) && float.IsFinite(b.High) && float.IsFinite(b.Low) && float.IsFinite(b.Close))))
                    result.Add(item with { Lyrics = item.Lyrics.OrderBy(l => l.StartSeconds).ToArray() });
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(r => r.RecordedAt).ToArray();
    }
    public static string Key(string title) => title.Trim().ToUpperInvariant();
    public static LibrarySettings Settings(string directory)
    {
        try { var s = JsonSerializer.Deserialize<LibrarySettings>(File.ReadAllText(Path.Combine(directory, "library-settings.json"))); return s is { MaxSongs: >= 1 and <= 1000 } ? s : new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public static Dictionary<string, LibraryEntry> ReadIndex(string directory)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, LibraryEntry>>(File.ReadAllText(Path.Combine(directory, "library-index.json"))) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    private static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value)); File.Move(path + ".tmp", path, true);
    }
    public static void Configure(string directory, int maxSongs)
    {
        WriteAtomic(Path.Combine(directory, "library-settings.json"), new LibrarySettings(Math.Clamp(maxSongs, 1, 1000)));
        EnforceLimits(directory);
    }
    public static SongRecording[] Retained(IEnumerable<SongRecording> items, Dictionary<string, LibraryEntry> index, int maxSongs) =>
        items.GroupBy(r => Key(r.Title)).OrderByDescending(g => Math.Max(g.Count(), index.TryGetValue(g.Key, out var entry) ? entry.Plays : 0))
            .ThenByDescending(g => g.Max(r => r.RecordedAt)).Take(maxSongs).SelectMany(g => g.OrderByDescending(r => r.RecordedAt).Take(5)).ToArray();
    private static void EnforceLimits(string directory)
    {
        var all = ReadAll(directory); var keep = Retained(all, ReadIndex(directory), Settings(directory).MaxSongs).Select(r => r.Id).ToHashSet();
        // Keep a recovery archive for evicted versions; only active library entries count towards limits.
        string archive = Path.GetFullPath(Path.Combine(directory, "Retired"));
        foreach (var item in all.Where(r => !keep.Contains(r.Id)))
        {
            string path = Path.GetFullPath(Path.Combine(directory, item.Id + ".json"));
            if (Path.GetDirectoryName(path) != Path.GetFullPath(directory) || Path.GetFileNameWithoutExtension(path) != item.Id) continue;
            Directory.CreateDirectory(archive); File.Move(path, Path.Combine(archive, Path.GetFileName(path)), true);
        }
    }
    public static SongSample? At(SongRecording recording, double seconds) =>
        recording.Samples.Where(s => s.Seconds <= seconds && seconds - s.Seconds <= 1.2).LastOrDefault();
}
