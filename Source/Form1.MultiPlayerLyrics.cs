namespace SpectrumKlinePlayer;

public partial class Form1
{
    private string observedOtherLine = "";
    private string otherClockKey = "";
    private readonly LyricPlaybackClock otherClock = new();
    private double lastOtherWindowPosition = -1;
    private long lastOtherWindowTick;
    private long lastOtherAdvanceTick;

    private (MediaTrackInfo? Track, string? Title, TimedLyricLine[] Lines, LyricFrame? Frame, string Status)
        CaptureOtherPlayer(MusicPlayerProfile player, MediaTrackInfo? session, CancellationToken token)
    {
        var window = PlayerWindowCapture.Poll(player, session?.Title);
        string? title = session?.Title ?? window?.Title;
        if (title is null) return (null, null, [], null, $"{player.Name} · 等待歌曲信息");
        string clockKey = player.Id + "|" + title;
        if (clockKey != otherClockKey)
        {
            otherClockKey = clockKey; otherClock.Reset(); observedOtherLine = "";
            lastOtherWindowPosition = -1; lastOtherWindowTick = lastOtherAdvanceTick = 0;
        }
        if (window is { Position: >= 0 } w && w.Tick != lastOtherWindowTick)
        {
            if (lastOtherWindowPosition >= 0 && w.Position > lastOtherWindowPosition)
                lastOtherAdvanceTick = System.Diagnostics.Stopwatch.GetTimestamp();
            lastOtherWindowPosition = w.Position; lastOtherWindowTick = w.Tick;
        }
        bool inferredPlaying = lastOtherAdvanceTick != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(lastOtherAdvanceTick).TotalSeconds < 1.6;
        bool playing = session?.IsPlaying ?? window?.Playing ?? inferredPlaying;
        bool realSession = session is { PositionSeconds: >= 0 } && !WindowsMediaSessionCapture.UsesEstimatedTimeline;
        bool realWindow = window is { Position: >= 0, Duration: >= 2 };
        double position = realSession ? session!.Value.PositionSeconds : realWindow ? window!.Position
            : session?.PositionSeconds ?? (otherClock.HasPosition ? otherClock.Position : -1);
        double duration = realSession ? session!.Value.DurationSeconds : realWindow ? window!.Duration : session?.DurationSeconds ?? -1;
        string artist = session is not null ? WindowsMediaSessionCapture.ActiveArtist : "";
        string song = session is not null ? WindowsMediaSessionCapture.ActiveSong : "";
        var document = CrossPlayerLyricCapture.Poll(title, artist, song, duration, token);
        var lines = document?.Lines ?? [];
        if (duration < 2 && document is { Duration: >= 2 }) duration = document.Duration;
        bool aligned = session is not null ? WindowsMediaSessionCapture.IsAlignedToObservedLyric : otherClock.HasPosition;
        if (!realSession && !realWindow && window?.Lyric is { } lyric && lines.Length > 0)
        {
            var observed = MatchObservedLine(lines, lyric, aligned ? position : null);
            string observationKey = player.Id + "|" + title + "|" + observed?.StartSeconds;
            if (observed is { } line && observationKey != observedOtherLine)
            {
                observedOtherLine = observationKey;
                position = line.StartSeconds;
                aligned = true;
                if (session is not null)
                    WindowsMediaSessionCapture.ObserveLyric(title, new LiveLyricPosition(lyric, position));
                else otherClock.Sample(position, playing);
            }
        }
        LyricFrame? frame = null;
        string status = realSession || realWindow ? $"{player.Name} · 真实进度同步"
            : aligned ? $"{player.Name} · 桌面歌词校时" : $"{player.Name} · 估算进度，等待桌面歌词校时";
        if (lines.Length > 0 && position >= 0)
        {
            int index = LyricPlaybackClock.FindActive(lines, position);
            frame = new LyricFrame(index >= 0 ? lines[index].Text : "♪ 前奏", TimeSpan.FromSeconds(Math.Max(0, position)).ToString(@"mm\:ss"),
                $"{player.Name} / {document!.Source} / {status}");
        }
        else if (window?.Lyric is { } live)
            frame = new LyricFrame(live, position >= 0 ? TimeSpan.FromSeconds(position).ToString(@"mm\:ss") : null, $"{player.Name}桌面歌词");
        else status += " / " + CrossPlayerLyricCapture.Status;
        if (session is null && position >= 0) otherClock.Sample(position, playing);
        return (new MediaTrackInfo(title, duration, position, playing), title, lines, frame, status);
    }

    public static TimedLyricLine? MatchObservedLine(TimedLyricLine[] lines, string text, double? expected)
    {
        string key = MusicPlayerProfiles.Key(text);
        if (key.Length < 2) return null;
        var matches = lines.Where(l => MusicPlayerProfiles.Key(l.Text) == key).ToArray();
        // Repeated choruses are ambiguous on mid-song startup without an independent clock.
        if (expected is null) return matches.Length == 1 ? matches[0] : null;
        return matches.OrderBy(l => Math.Abs(l.StartSeconds - expected.Value)).Cast<TimedLyricLine?>().FirstOrDefault();
    }
}
