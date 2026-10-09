using System.Diagnostics;

namespace SpectrumKlinePlayer;

// Keep optional OCR/desktop scans outside the metadata and clock polling path.
public static class KugouLyricFallback
{
    private static Task<(string Title, LiveLyricPosition? Position, LyricFrame? Frame, long Tick)>? pending;
    private static (string Title, LiveLyricPosition? Position, LyricFrame? Frame, long Tick) latest;
    private static long nextAttempt;

    public static (LiveLyricPosition? Position, LyricFrame? Frame) TryCapture(string title, double? expectedPosition, bool needFrame)
    {
        if (pending is { IsCompleted: true })
        {
            latest = pending.GetAwaiter().GetResult();
            pending = null;
        }
        long now = Stopwatch.GetTimestamp();
        if (pending is null && now >= nextAttempt)
        {
            nextAttempt = now + Stopwatch.Frequency;
            pending = Task.Run(() =>
            {
                try
                {
                    string? nativeBefore = KugouPlaybackTimeCapture.TryCaptureTitle();
                    if (nativeBefore is not null && nativeBefore != title) return (title, (LiveLyricPosition?)null, (LyricFrame?)null, now);
                    var position = KugouScreenLyricCapture.TryCapture(title, expectedPosition);
                    var frame = needFrame ? KugouDesktopLyricCapture.TryCapture() : null;
                    string? nativeAfter = KugouPlaybackTimeCapture.TryCaptureTitle();
                    if (nativeAfter is not null && nativeAfter != title) return (title, (LiveLyricPosition?)null, (LyricFrame?)null, now);
                    return (title, position, frame, Stopwatch.GetTimestamp());
                }
                catch { return (title, (LiveLyricPosition?)null, (LyricFrame?)null, now); }
            });
        }
        return latest.Title == title && latest.Tick != 0 && Stopwatch.GetElapsedTime(latest.Tick).TotalSeconds < 2
            ? (latest.Position, latest.Frame) : (null, null);
    }
}
