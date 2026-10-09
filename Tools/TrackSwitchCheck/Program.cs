using SpectrumKlinePlayer;
using System.Reflection;
using System.Diagnostics;
using System.IO;
class Check
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form1(); form.Show();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object Get(string name) => typeof(Form1).GetField(name, flags)!.GetValue(form)!;
        var watch = Stopwatch.StartNew(); int printed = -1; bool next = false, previous = false;
        string? initialTitle = null, nextTitle = null; double nextSent = 0, previousSent = 0;
        bool nextVerified = false, previousVerified = false;
        using var log = new StreamWriter("Docs/TrackSwitchLiveCheck.log", false);
        void Write(string message) { Console.WriteLine(message); log.WriteLine(message); log.Flush(); }
        while (watch.Elapsed.TotalSeconds < 16)
        {
            Application.DoEvents();
            if (args.Contains("--switch") && watch.Elapsed.TotalSeconds > 3 && !next)
            {
                next = true; initialTitle = (string)Get("currentTrackTitle"); nextSent = watch.Elapsed.TotalSeconds;
                typeof(Form1).GetMethod("RunPlayerCommand", flags)!.Invoke(form, [PlayerCommand.Next]); Write("COMMAND next");
            }
            if (args.Contains("--switch") && watch.Elapsed.TotalSeconds > 9 && !previous)
            {
                previous = true; previousSent = watch.Elapsed.TotalSeconds;
                // Send this one directly to SMTC to verify changes outside our button path.
                WindowsMediaSessionCapture.ControlAsync(PlayerCommand.Previous).GetAwaiter().GetResult(); Write("COMMAND previous (external session)");
            }
            int tick = (int)(watch.Elapsed.TotalSeconds * 4);
            if (tick != printed)
            {
                printed = tick;
                string window = KugouPlaybackTimeCapture.TryCaptureTitle() ?? "";
                Write($"{watch.Elapsed.TotalSeconds:F2}s window={window}; UI={Get("currentTrackTitle")}; lines={((TimedLyricLine[])Get("lyricTimeline")).Length}; index={Get("activeLyricIndex")}; position={((LyricPlaybackClock)Get("lyricClock")).Position:F2}; status={Get("lyricTimingStatus")}; lyric={Get("currentLyric")}");
                bool synced = (string)Get("currentTrackTitle") == window && ((TimedLyricLine[])Get("lyricTimeline")).Length > 0
                    && ((string)Get("lyricTimingStatus")).Contains("真实播放时间");
                if (next && !previous && !nextVerified && window != initialTitle && synced)
                { nextTitle = window; nextVerified = true; Write($"PASS next title, document and actual clock aligned after {watch.Elapsed.TotalSeconds-nextSent:F2}s"); }
                if (previous && !previousVerified && window != nextTitle && synced)
                { previousVerified = true; Write($"PASS external previous aligned after {watch.Elapsed.TotalSeconds-previousSent:F2}s"); }
            }
            Thread.Sleep(15);
        }
        form.Close();
        if (args.Contains("--switch") && (!nextVerified || !previousVerified)) throw new Exception("Switch did not align with actual clock and lyrics");
    }
}
