using SpectrumKlinePlayer;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

class Verify
{
    static int count;
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); count++; }
    static void Hold(ClimaxMarket m, double energy, int frames = 100) { for (int i = 0; i < frames; i++) m.Observe(energy, .03); }
    [STAThread] static void Main()
    {
        ApplicationConfiguration.Initialize();
        var m = new ClimaxMarket(new Random(45)) { Settings = new(TriggerProbability: 1, OneWordProbability: 1, MinimumBoards: 3, MaximumBoards: 3, BreakProbabilityPerSecond: 0) };
        Hold(m, .5); Check(m.BeginBar(0) is null, "normal energy does not trigger");
        Hold(m, .8); var first = m.BeginBar(0)!;
        Check(first.Number == 1 && first.Total == 3 && first.OneWord, "high energy starts configured three-board streak");
        Check(Math.Abs(Math.Exp(first.Limit - first.PreviousClose) - (first.Direction > 0 ? 1.1 : .9)) < 1e-8, "limit uses previous close and precise positive price ratio");
        var second = m.BeginBar(first.Limit)!; var third = m.BeginBar(second.Limit)!;
        Check(second.Direction == first.Direction && third.Direction == first.Direction && third.Number == 3, "streak direction stays consistent and counts up");
        Check(m.BeginBar(third.Limit) is null, "streak stops and cooldown prevents immediate restart");
        Hold(m, .8, 1000);
        Check(Enumerable.Range(0, 300).All(_ => m.BeginBar(0) is null), "long high-energy section never starts a second group in the same song");
        m.Cancel(); Hold(m, .8);
        Check(m.HasTriggered && m.BeginBar(0) is null, "seek or settings cancellation preserves used song quota");
        m.Settings = m.Settings with { Enabled = false }; Hold(m, .8);
        m.Settings = m.Settings with { Enabled = true }; Hold(m, .8); m.QueueSingleBoard(-1);
        Check(m.BeginBar(0) is null, "toggle and blind-box board cannot bypass song quota");
        m.Reset(); Check(m.Board is null && m.HighSeconds == 0 && !m.Broken, "song reset removes streak and energy timers");
        var directions = new HashSet<int>();
        for (int i = 0; i < 60; i++) { m.Reset(); Hold(m, .8); directions.Add(m.BeginBar(0)!.Direction); }
        Check(directions.SetEquals(new[] { -1, 1 }), "random generator produces both up and down streaks");
        m.Settings = m.Settings with { OneWordProbability = 0 }; m.Reset(); Hold(m, .8); var gap = m.BeginBar(0)!;
        Check(!gap.OneWord && Math.Abs(gap.Open) > Math.Abs(gap.Limit) * .54 && Math.Abs(gap.Open) < Math.Abs(gap.Limit), "large-gap opening stays inside limit");
        m.Settings = m.Settings with { OneWordProbability = 1, ExtremeSeconds = 3 };
        m.Reset(); Hold(m, .8, 60); m.BeginBar(0); Hold(m, .8, 20); Check(!m.Broken, "one-word board remains locked without base hazard before extreme timeout");
        Hold(m, .8, 5000); Check(m.Broken, "prolonged high energy raises hazard and can break lock");
        m.Reset(); Hold(m, .8, 60); m.BeginBar(0); Hold(m, .1, 5000); Check(m.Broken, "prolonged low energy can also break lock");
        m.Settings = m.Settings with { Enabled = false }; m.Observe(.8, .1); Check(m.BeginBar(0) is null, "feature off disables streaks");
        m.Settings = m.Settings with { Enabled = true, TriggerProbability = 0 }; m.Reset(); Hold(m, .8);
        Check(m.BeginBar(0) is null, "zero configured trigger probability suppresses climax event");
        m.QueueSingleBoard(-1);
        Check(m.BeginBar(0)?.Direction == -1, "blind box uses one shared song allowance");
        m.QueueSingleBoard(1); Hold(m, .8, 1000);
        Check(m.BeginBar(0) is null, "blind box cannot continuously refill its board queue");
        var samples = Enumerable.Range(0, 120).Select(i => new SongSample(i * .5 + 30, 0, .2f, -.2f, (float)Math.Sin(i * .08) * .2f,
            (float)(.45 + .3 * Math.Sin(i * .05)), i is >= 45 and < 60 ? "涨停 1/3 · 一字板" : "", 0)).ToArray();
        var a = new SongRecording("verification-a", "演示歌曲 A（验证数据）", 120, DateTime.Now, true, true, samples, [new(0, "等待旋律"), new(45, "这一刻进入高潮"), new(75, "慢慢回落")]);
        var b = a with { Id = "verification-b", Title = "演示歌曲 B（验证数据）", Duration = 180,
            Samples = samples.Select(s => s with { Seconds = s.Seconds * 1.5, Close = -s.Close * .6f }).ToArray() };
        string directory = Path.Combine(AppContext.BaseDirectory, "VerificationRecords");
        SongRecordingStore.Save(directory, a); SongRecordingStore.Save(directory, b);
        var loaded = SongRecordingStore.Load(directory);
        Check(loaded.Length == 2 && loaded.Any(r => r.Samples.Length == 120 && r.Lyrics.Length == 3), "recording save/load preserves samples and timed lyrics");
        Check(SongRecordingStore.At(a, 12) is null && SongRecordingStore.At(a, 110) is null && SongRecordingStore.At(a, 45)?.Seconds == 45, "unrecorded start/end remain empty and cursor selects actual sample");
        using (var compare = new SongComparisonForm(loaded))
        {
            compare.Show(); Application.DoEvents();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var cursor = (TrackBar)typeof(SongComparisonForm).GetField("cursor", flags)!.GetValue(compare)!;
            cursor.Value = 500;
            string output = Path.GetFullPath("Docs/UiPreview"); Directory.CreateDirectory(output);
            foreach (var size in new[] { new Size(1200, 760), new Size(900, 600) })
            {
                compare.Size = size; compare.PerformLayout(); Application.DoEvents();
                using var bmp = new Bitmap(compare.Width, compare.Height); compare.DrawToBitmap(bmp, new(Point.Empty, bmp.Size));
                bmp.Save(Path.Combine(output, $"SongComparison_{size.Width}x{size.Height}.png"), ImageFormat.Png);
            }
            var align = (CheckBox)typeof(SongComparisonForm).GetField("progressAlignment", flags)!.GetValue(compare)!;
            cursor.Value = 500; // Window resize can legitimately deliver a mouse move.
            align.Checked = false; Application.DoEvents();
            Check(cursor.Value == 500, "comparison cursor survives seconds/progress alignment toggle");
            compare.Close();
        }
        using (var form = new Form1())
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo F(string n) => typeof(Form1).GetField(n, flags)!;
            void Set(string n, object v) => F(n).SetValue(form, v);
            object? Call(string n, params object?[] args) => typeof(Form1).GetMethods(flags).Single(x => x.Name == n && x.DeclaringType == typeof(Form1)).Invoke(form, args);
            Call("OnFormClosing", null, new FormClosingEventArgs(CloseReason.None, false)); Set("closing", false); Set("saveIndicatorSettings", false);
            void Feed(float amplitude, float frequency)
            {
                var samples = Enumerable.Range(0, 8192).Select(i => amplitude * MathF.Sin(2 * MathF.PI * frequency * i / 48000)).ToArray();
                var pointer = Marshal.AllocHGlobal(samples.Length * sizeof(float));
                try { Marshal.Copy(samples, 0, pointer, samples.Length); Call("ProcessCapturedBuffer", pointer, samples.Length, new WaveFormatInfo { SampleRate = 48000, Channels = 1, BitsPerSample = 32, IsFloat = true }, false); }
                finally { Marshal.FreeHGlobal(pointer); }
                Call("AnalyzeAudio");
            }
            float[] Meter(string name) => (float[])F(name).GetValue(form)!;
            Check(Meter("energyTargets").Length == 100 && Meter("energyLevels").Length == 100 && Meter("bandTargets").Length == 100 && ((Array)F("candles").GetValue(form)!).Length == 100, "all spectrum and market arrays contain 100 bands");
            Check((int[])Call("QuoteBands")! is { Length: 100 } bands && bands[^1] == 99, "quote list exposes all 100 bands including final twenty");
            Feed(.2f, 14000);
            Check(Array.IndexOf(Meter("energyTargets"), Meter("energyTargets").Max()) >= 80, "real high frequency PCM drives newly added bands");
            Feed(.002f, 250); float quiet = Meter("energyTargets").Max();
            Feed(.2f, 250); float loud = Meter("energyTargets").Max();
            Check(loud > quiet + .4f, "absolute spectrum meter reacts to input loudness before market normalization");
            int bassPeak = Array.IndexOf(Meter("energyTargets"), loud);
            Feed(.2f, 4000); int treblePeak = Array.IndexOf(Meter("energyTargets"), Meter("energyTargets").Max());
            Check(treblePeak > bassPeak + 20, "spectrum bars move with frequency rather than candlestick state");
            var meterClock = (Stopwatch)F("clock").GetValue(form)!;
            Set("lastSpectrumAt", meterClock.Elapsed.TotalSeconds - .1); Call("UpdateSpectrumLevels"); float beforeSilence = Meter("energyLevels").Max();
            Set("lastAudioPacketAt", Environment.TickCount64 - 1000); Call("AnalyzeAudio");
            for (int i = 0; i < 12; i++) { Set("lastSpectrumAt", meterClock.Elapsed.TotalSeconds - .1); Call("UpdateSpectrumLevels"); }
            Check(Meter("energyTargets").All(v => v == 0) && Meter("energyLevels").Max() < beforeSilence * .01f, "missing packets decay meter to silence instead of reusing old FFT");
            Feed(0, 250);
            Check(Meter("energyTargets").All(v => v == 0), "silent PCM produces zero spectrum targets");
            Set("simulationMode", true);
            var market = (ClimaxMarket)F("climaxMarket").GetValue(form)!;
            market.Settings = new(TriggerProbability: 1, OneWordProbability: 1, MinimumBoards: 3, MaximumBoards: 3, BreakProbabilityPerSecond: 0);
            Call("ResetSongChart", 180d, "连板演示（验证数据）"); Set("musicEnergy", .8f); Hold(market, .8);
            Call("UpdateDayKRolling");
            var clock = (Stopwatch)F("clock").GetValue(form)!;
            Set("dayKCandleStart", clock.Elapsed.TotalSeconds - 1.25); Call("UpdateDayKRolling");
            var candles = (Array)F("timelineCandles").GetValue(form)!;
            var c = candles.GetValue((int)F("timelineCandleCount").GetValue(form)! - 1)!; var ct = c.GetType();
            float V(string n) => (float)ct.GetField(n)!.GetValue(c)!;
            Check(market.Board is not null && V("Open") == V("High") && V("Low") == V("Close") && V("Open") == V("Close"), "live one-word candle has exactly equal OHLC without phantom wick");
            for (int i = 0; i < 20; i++) Call("UpdateDayKRolling");
            c = candles.GetValue((int)F("timelineCandleCount").GetValue(form)! - 1)!;
            Check(V("Open") == V("High") && V("Low") == V("Close"), "normal audio drift cannot alter locked one-word candle");
            market.Reset(); market.Settings = market.Settings with { OneWordProbability = 0 }; Hold(market, .8);
            Set("dayKCandleStart", clock.Elapsed.TotalSeconds - 1.25); Call("UpdateDayKRolling");
            var liveGap = market.Board!;
            c = candles.GetValue((int)F("timelineCandleCount").GetValue(form)! - 1)!;
            Check(Math.Abs(V("Open") - liveGap.Open) < 1e-6 && Math.Abs(V("Open") - liveGap.Limit) > .001, "live gap candle preserves large opening separate from limit");
            Set("dayKCandleStart", clock.Elapsed.TotalSeconds - 1.25); Call("UpdateDayKRolling");
            var history = (List<IndicatorBar>)F("indicatorHistory").GetValue(form)!;
            Check(Math.Abs(history[^1].Close - (double)F("songOpeningPrice").GetValue(form)! * Math.Exp(liveGap.Limit)) < 1e-4, "completed gap board closes at limit and flows into indicator history");
            market.Settings = market.Settings with { OneWordProbability = 1 };
            Set("lastTrackPositionSeconds", 45d); Set("songPositionSeconds", 45d); Call("SampleSongRecording");
            Check(((List<SongSample>)F("recordingSamples").GetValue(form)!).Count == 1, "named current song produces recording sample");
            Set("trackIsPlaying", false); int before = (int)F("timelineCandleCount").GetValue(form)!;
            Set("dayKCandleStart", clock.Elapsed.TotalSeconds - 3); Call("UpdateDayKRolling"); Call("SampleSongRecording");
            Check((int)F("timelineCandleCount").GetValue(form)! == before && ((List<SongSample>)F("recordingSamples").GetValue(form)!).Count == 1, "pause stops new candles and recording samples");
            Set("trackIsPlaying", true); Set("songPositionSeconds", 10d); Call("SampleSongRecording");
            Check(((List<SongSample>)F("recordingSamples").GetValue(form)!)[^1].Segment == 1 && market.Board is null, "backward seek starts a new segment and clears old streak");
            Check(market.HasTriggered, "actual backward seek does not restore climax allowance");
            Hold(market, .8); Set("dayKCandleStart", clock.Elapsed.TotalSeconds - 1.25); Call("UpdateDayKRolling");
            form.Show(); Application.DoEvents();
            using (var bmp = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bmp, new(Point.Empty, bmp.Size)); bmp.Save(Path.GetFullPath("Docs/UiPreview/Terminal_Climax.png"), ImageFormat.Png); }
            Call("ResetSongChart", 200d, "下一首（验证数据）");
            Check(((List<SongSample>)F("recordingSamples").GetValue(form)!).Count == 0 && market.Board is null, "song switch saves previous recording and resets streak/sample state");
            Check(!market.HasTriggered, "actual song switch grants the next song one fresh allowance");
            Check(SongRecordingStore.Load(Path.Combine(AppContext.BaseDirectory, "Data", "SongRecordings")).Any(r => r.Title == "连板演示（验证数据）"), "previous song remains available after switch");
            form.Close();
        }
        Console.WriteLine($"Completed {count} checks.");
    }
}
