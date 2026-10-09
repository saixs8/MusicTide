using System.Diagnostics;

namespace SpectrumKlinePlayer;

public readonly record struct TimedLyricLine(double StartSeconds, string Text);

// UI time continues between player samples, even while OCR is busy.
public sealed class LyricPlaybackClock
{
    private double position;
    private long sampleTick;
    private bool playing;
    public bool HasPosition => sampleTick != 0;
    public double Position => position + (playing && sampleTick != 0
        ? Stopwatch.GetElapsedTime(sampleTick).TotalSeconds : 0);

    public void Sample(double seconds, bool isPlaying)
    {
        // KuGou reports whole seconds. Repeating that value must not undo interpolation.
        if (sampleTick != 0 && Math.Abs(seconds - position) < 0.001 && playing == isPlaying) return;
        position = Math.Max(0, seconds);
        playing = isPlaying;
        sampleTick = Stopwatch.GetTimestamp();
    }

    public void Reset() { position = 0; sampleTick = 0; playing = false; }

    public static int FindActive(IReadOnlyList<TimedLyricLine> lines, double seconds)
    {
        int low = 0, high = lines.Count - 1, active = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (lines[mid].StartSeconds <= seconds) { active = mid; low = mid + 1; }
            else high = mid - 1;
        }
        return active;
    }
}
