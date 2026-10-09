using Windows.Media.Control;
using System.Diagnostics;

namespace SpectrumKlinePlayer;

public static class WindowsMediaSessionCapture
{
    private static GlobalSystemMediaTransportControlsSessionManager? manager;
    private static DateTime nextManagerAttemptUtc;
    private static string estimatedTitle = string.Empty;
    private static double estimatedPosition;
    private static long lastSampleTick;
    public static bool UsesEstimatedTimeline { get; private set; }
    public static double CurrentEstimatedPositionSeconds => estimatedPosition;
    public static bool IsAlignedToObservedLyric { get; private set; }
    private static string lastObservedLine = string.Empty;
    public static bool IsPlaying { get; private set; }
    private static DateTimeOffset lastTimelineUpdate;
    private static double lastTimelinePosition = -1;
    private static bool previousPlaying;
    public static GlobalSystemMediaTransportControlsSession? ActiveSession { get; private set; }
    public static string? ActiveSourceApp => ActiveSession?.SourceAppUserModelId;
    public static string ActiveSong { get; private set; } = "";
    public static string ActiveArtist { get; private set; } = "";

    public static async Task<bool> ControlAsync(PlayerCommand command)
    {
        var session = ActiveSession;
        if (session is null) return false;
        try
        {
            return command switch
            {
                PlayerCommand.Previous => await session.TrySkipPreviousAsync(),
                PlayerCommand.Next => await session.TrySkipNextAsync(),
                PlayerCommand.Toggle => await session.TryTogglePlayPauseAsync(),
                PlayerCommand.Stop => await session.TryStopAsync(),
                _ => false
            };
        }
        catch { return false; }
    }

    public static void AlignToObservedLyric(string title, double positionSeconds)
    {
        if (UsesEstimatedTimeline && string.Equals(title, estimatedTitle, StringComparison.Ordinal)
            && positionSeconds >= 0)
        {
            estimatedPosition = positionSeconds;
            lastSampleTick = Stopwatch.GetTimestamp();
            IsAlignedToObservedLyric = true;
        }
    }

    // Called by the lyric worker, never by the UI thread.
    public static MediaTrackInfo? TryCapture()
    {
        try { return CaptureAsync().GetAwaiter().GetResult(); }
        catch
        {
            manager = null;
            nextManagerAttemptUtc = DateTime.UtcNow.AddSeconds(3);
            return null;
        }
    }

    private static async Task<MediaTrackInfo?> CaptureAsync()
    {
        if (manager is null)
        {
            if (DateTime.UtcNow < nextManagerAttemptUtc) return null;
            nextManagerAttemptUtc = DateTime.UtcNow.AddSeconds(3);
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }

        GlobalSystemMediaTransportControlsSession? selected = null;
        int bestScore = -1;
        GlobalSystemMediaTransportControlsSession? current = manager.GetCurrentSession();
        foreach (GlobalSystemMediaTransportControlsSession session in manager.GetSessions())
        {
            int score = ScoreApp(session.SourceAppUserModelId);
            if (score == 0) continue;
            if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) score += 100;
            if (ReferenceEquals(session, current)) score += 5;
            if (score > bestScore)
            {
                selected = session;
                bestScore = score;
            }
        }

        ActiveSession = selected is not null && ScoreApp(selected.SourceAppUserModelId) >= 80 ? selected : null;
        selected = ActiveSession;
        if (selected is null) { ActiveSong = ActiveArtist = ""; return null; }
        var media = await selected.TryGetMediaPropertiesAsync();
        if (string.IsNullOrWhiteSpace(media.Title)) return null;

        ActiveSong = MusicPlayerProfiles.IsGeneric(media.Title) && !string.IsNullOrWhiteSpace(media.AlbumTitle)
            ? media.AlbumTitle : media.Title;
        ActiveArtist = media.Artist ?? "";
        if (MusicPlayerProfiles.IsGeneric(ActiveSong)) return null;
        string title = string.IsNullOrWhiteSpace(ActiveArtist) ? ActiveSong : $"{ActiveArtist} - {ActiveSong}";
        var timeline = selected.GetTimelineProperties();
        double duration = timeline.EndTime.TotalSeconds;
        bool playing = selected.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        IsPlaying = playing;
        long sampleTick = Stopwatch.GetTimestamp();
        if (!string.Equals(title, estimatedTitle, StringComparison.Ordinal))
        {
            estimatedTitle = title;
            estimatedPosition = 0;
            lastSampleTick = sampleTick;
            IsAlignedToObservedLyric = false;
            lastObservedLine = string.Empty;
            lastTimelinePosition = -1;
            lastTimelineUpdate = default;
            previousPlaying = false;
        }

        // Some players publish metadata and playback status but leave the SMTC timeline
        // at its WinRT zero value (1601-01-01). Keep the lyrics moving in that case.
        bool hasTimeline = timeline.LastUpdatedTime.Year >= 2000
            && (duration > 0 || timeline.Position.TotalSeconds > 0);
        UsesEstimatedTimeline = !hasTimeline;
        if (hasTimeline)
        {
            bool changed = timeline.LastUpdatedTime != lastTimelineUpdate
                || Math.Abs(timeline.Position.TotalSeconds - lastTimelinePosition) > 0.001;
            if (changed)
            {
                estimatedPosition = timeline.Position.TotalSeconds;
                if (playing)
                    estimatedPosition += Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds)
                        * (selected.GetPlaybackInfo().PlaybackRate ?? 1.0);
            }
            else if (playing && previousPlaying && lastSampleTick != 0)
                estimatedPosition += Stopwatch.GetElapsedTime(lastSampleTick, sampleTick).TotalSeconds
                    * (selected.GetPlaybackInfo().PlaybackRate ?? 1.0);
            lastTimelineUpdate = timeline.LastUpdatedTime;
            lastTimelinePosition = timeline.Position.TotalSeconds;
        }
        else if (playing && previousPlaying && lastSampleTick != 0)
        {
            estimatedPosition += Stopwatch.GetElapsedTime(lastSampleTick, sampleTick).TotalSeconds;
        }
        lastSampleTick = sampleTick;
        previousPlaying = playing;

        double position = Math.Max(0, estimatedPosition);

        return new MediaTrackInfo(title, duration >= 2 ? duration : -1,
            position >= 0 ? Math.Min(position, duration > 0 ? duration : position) : -1, playing);
    }

    public static void ObserveLyric(string title, LiveLyricPosition observed)
    {
        if (!UsesEstimatedTimeline) return;
        string key = $"{title}|{observed.PositionSeconds:0.000}";
        // Re-reading an unchanged line must not pin the clock to its start.
        if (key == lastObservedLine) return;
        AlignToObservedLyric(title, observed.PositionSeconds);
        lastObservedLine = key;
    }

    private static int ScoreApp(string app)
    {
        return MusicPlayerProfiles.Find(app) is { } player ? player.Id == "kugou" ? 100 : 80 : 0;
    }
}

public enum PlayerCommand { Previous, Toggle, Stop, Next }
