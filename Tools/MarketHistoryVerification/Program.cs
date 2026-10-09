using SpectrumKlinePlayer;
using System.IO;
using System.Reflection;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Runtime.InteropServices;

class Verify
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int message, IntPtr w, IntPtr l);
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Preview(Form form, string name)
    {
        form.Show(); Application.DoEvents();
        using var bmp = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bmp, new(Point.Empty, bmp.Size)); bmp.Save("Docs/UiPreview/" + name + ".png", ImageFormat.Png);
    }
    [STAThread] static void Main()
    {
        ApplicationConfiguration.Initialize();
        var axis = MarketAxis.Fit([40d, 42, 45], 40);
        Check(axis.Minimum > 35 && axis.Maximum < 50, "visible price axis fits only window extrema without zero-centered padding");
        Check(axis.Percent(40) == 0 && Math.Abs(axis.Percent(44) - 10) < 1e-8, "right percent axis uses visible left opening reference");
        var plot = new Rectangle(5, 8, 100, 200);
        Check(Math.Abs(axis.PriceAt(plot, axis.Y(plot, 42)) - 42) < 1e-5, "price and pixel coordinates invert consistently");
        var centered = MarketAxis.Centered([41d, 43, 48], 40);
        Check(Math.Abs(centered.Y(plot, 40) - (plot.Top + plot.Height / 2f)) < 1e-5 && Math.Abs(centered.Percent(centered.Maximum) + centered.Percent(centered.Minimum)) < 1e-8, "minute zero remains centered even with exclusively positive price samples");
        Check(new SongHeat(10000, 1000).Shares > new SongHeat(1000, 100).Shares, "more likes and comments increase virtual share capital");
        Check(SongHeat.Estimate("test") == SongHeat.Estimate("test") && SongHeat.Estimate("test").Simulated, "fallback song heat is stable and labeled simulated");
        string directory = Path.GetFullPath("Tools/MarketHistoryVerification/Fixtures/" + Guid.NewGuid().ToString("N"));
        SongRecording Recording(string title, int version) => new(Guid.NewGuid().ToString("N"), title, 60, DateTime.Now.AddSeconds(version), true, true,
            Enumerable.Range(0, 600).Select(i => new SongSample(i / 10d, 0, .02f, -.01f, (float)(Math.Sin(i * .02 + version) * .035), .5f, "", 0, i / 20, 100, 4000)).ToArray(), [new(0, "历史版本验证"), new(20, "重复听歌 · 分时明细")], OpeningPrice: 40 + version, Bars: Enumerable.Range(0, 30).Select(i => new SongBar(0, .02f, -.02f, (float)(Math.Sin(i * .2) * .015), i)).ToArray(), Completed: true);
        var records = Enumerable.Range(0, 7).Select(i => Recording("常听歌曲", i)).ToArray();
        foreach (var item in records) SongRecordingStore.Save(directory, item);
        Check(SongRecordingStore.Load(directory).Length == 5 && Directory.GetFiles(Path.Combine(directory, "Retired"), "*.json").Length == 2, "sixth and seventh versions retire oldest while preserving five active versions");
        var last = records[^1]; SongRecordingStore.Save(directory, last);
        Check(SongRecordingStore.ReadIndex(directory)[SongRecordingStore.Key(last.Title)].Plays == 7, "periodic saves never inflate repeat count");
        foreach (int i in Enumerable.Range(0, 25)) SongRecordingStore.Save(directory, Recording("单次歌曲" + i, i));
        var saved = SongRecordingStore.Load(directory);
        Check(saved.Select(r => r.Title).Distinct().Count() == 20 && saved.Count(r => r.Title == "常听歌曲") == 5, "default twenty-song library prioritizes repeated songs");
        var once = Recording("容易淘汰", 0); SongRecordingStore.Save(directory, once); SongRecordingStore.Configure(directory, 1);
        SongRecordingStore.Save(directory, once); SongRecordingStore.Save(directory, once);
        Check(SongRecordingStore.ReadIndex(directory)[SongRecordingStore.Key(once.Title)].Plays == 1, "evicted current session cannot gain artificial repeat count through autosaving");
        Check(SongRecordingStore.Load(directory).Select(r => r.Title).Distinct().Count() == 1 && SongRecordingStore.Settings(directory).MaxSongs == 1, "custom library capacity persists and enforces limit");
        Check(saved.All(r => r.OpeningPrice > 0 && r.Bars?.Length == 30 && r.Samples.All(s => s.BarIndex >= 0 && s.Volume > 0 && s.Amount > 0)), "stored versions preserve opening price, bars and tick volume and amount");
        string legacyDirectory = Path.Combine(directory, "legacy"); Directory.CreateDirectory(legacyDirectory);
        File.WriteAllText(Path.Combine(legacyDirectory, "legacy.json"), "{\"Id\":\"legacy\",\"Title\":\"旧版记录\",\"Duration\":60,\"RecordedAt\":\"2026-10-08T00:00:00\",\"Simulated\":true,\"AuthoritativeTime\":true,\"Samples\":[{\"Seconds\":1,\"Open\":0,\"High\":0,\"Low\":0,\"Close\":0,\"Energy\":0.5,\"Event\":\"\",\"Segment\":0}],\"Lyrics\":[]}");
        Check(SongRecordingStore.Load(legacyDirectory).Single().OpeningPrice == 100, "legacy recordings load with original hundred-yuan price basis");
        using (var history = new SongHistoryForm(saved.Where(r => r.Title == "常听歌曲").ToArray()))
        {
            Preview(history, "MarketHistory_FiveVersions");
            var versions = (CheckedListBox)typeof(SongHistoryForm).GetField("versions", Flags)!.GetValue(history)!;
            Check(versions.CheckedItems.Count == 5, "history opens five versions simultaneously for overlay");
            var canvas = (Control)typeof(SongHistoryForm).GetField("canvas", Flags)!.GetValue(history)!;
            var historyPlot = (Rectangle)typeof(SongHistoryForm).GetField("plot", Flags)!.GetValue(history)!;
            typeof(Control).GetMethod("OnMouseDown", Flags)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 2, historyPlot.Left + 6, historyPlot.Top + 20, 0)]);
            Application.DoEvents(); Check(history.OwnedForms.OfType<SongHistoryForm>().Any(), "double-click saved K candle opens actual saved intrabar ticks");
            foreach (var child in history.OwnedForms) child.Close(); history.Close();
        }
        using (var detail = new SongHistoryForm([records[0] with { Samples = records[0].Samples.Where(s => s.BarIndex == 4).ToArray(), Bars = [] }], true)) { Preview(detail, "MarketHistory_Intrabar"); detail.Close(); }
        using (var form = new Form1())
        {
            FieldInfo F(string name) => typeof(Form1).GetField(name, Flags)!;
            void Set(string name, object value) => F(name).SetValue(form, value);
            object? Call(string name, params object?[] values) => typeof(Form1).GetMethods(Flags).Single(m => m.Name == name && m.DeclaringType == typeof(Form1)).Invoke(form, values);
            Set("saveIndicatorSettings", false); Call("OnFormClosing", null, new FormClosingEventArgs(CloseReason.None, false));
            Call("ResetSongChart", 60d, "行情验证"); double firstPrice = (double)F("songOpeningPrice").GetValue(form)!;
            Call("ResetSongChart", 60d, "行情验证"); double secondPrice = (double)F("songOpeningPrice").GetValue(form)!;
            int oldBand = (int)F("minuteBandIndex").GetValue(form)!, oldCompare = (int)F("frequencyMidpointBand").GetValue(form)!;
            Call("ResetSongChart", 60d, "新的歌曲");
            int band = (int)F("minuteBandIndex").GetValue(form)!, compare = (int)F("frequencyMidpointBand").GetValue(form)!;
            Check(band != oldBand && compare != oldCompare && band != compare && band is >= 0 and < 100 && compare is >= 0 and < 100, "new song randomizes stock-linked sample and distinct comparison bands without repeating previous round");
            Check(((TrackBar)F("minuteFrequencyControl").GetValue(form)!).Value == band && ((TrackBar)F("frequencyMidpointControl").GetValue(form)!).Value == compare, "both frequency sliders follow random song selection");
            var pairSnapshot = (SongRecording)Call("CurrentRecording")!;
            Check(pairSnapshot.SampleBand == band && pairSnapshot.ComparisonBand == compare && pairSnapshot.SecurityCode.Length == 6, "history snapshot preserves stock code and both comparison frequencies");
            secondPrice = (double)F("songOpeningPrice").GetValue(form)!;
            Check(firstPrice is >= 5 and <= 100 && secondPrice is >= 5 and <= 100 && firstPrice != secondPrice, "each listening round starts from randomized valid opening price");
            Call("ResetSongChart", 60d, "行情验证"); secondPrice = (double)F("songOpeningPrice").GetValue(form)!;
            Set("currentTrackTitle", "行情验证"); Set("lastTrackPositionSeconds", 59d); Set("songPositionSeconds", 59d);
            var samples = (List<SongSample>)F("recordingSamples").GetValue(form)!; samples.AddRange(records[0].Samples);
            Set("lastRecordingPosition", 59d);
            var current = (SongRecording)Call("CurrentRecording")!;
            Check(current.Completed && current.OpeningPrice == secondPrice, "finished song snapshot carries completion and per-round opening price");
            Set("closing", false);
            string id = (string)F("recordingId").GetValue(form)!;
            Call("ApplyTrackInfo", new MediaTrackInfo("行情验证", 60, 0, true));
            Check((string)F("recordingId").GetValue(form)! != id && samples.Count == 0, "same-title playback loop creates fresh independent version");
            Set("timelineCandleCount", 1); Call("SampleSongRecording");
            Check(samples.Single().BarIndex == 0 && samples.Single().Volume > 0 && samples.Single().Amount > 0, "new ticks bind to candle and accumulate virtual traded volume and amount");
            Set("closing", true); form.Show(); Application.DoEvents();
            using (var bmp = new Bitmap(form.Width, form.Height)) form.DrawToBitmap(bmp, new(Point.Empty, bmp.Size));
            var mainPlot = (Rectangle)F("terminalPlot").GetValue(form)!;
            var stage = (Control)F("stage").GetValue(form)!;
            int x = mainPlot.Left + Math.Max(1, mainPlot.Width / 100), y = mainPlot.Top + mainPlot.Height / 2;
            SendMessage(stage.Handle, 0x203, (IntPtr)1, (IntPtr)((y << 16) | (x & 65535))); SendMessage(stage.Handle, 0x202, IntPtr.Zero, (IntPtr)((y << 16) | (x & 65535)));
            Application.DoEvents(); Check(form.OwnedForms.OfType<SongHistoryForm>().Any(), "native double-click on main chart opens recorded candle ticks");
            foreach (var child in form.OwnedForms) child.Close();
            form.Close();
        }
        Console.WriteLine($"Completed {checks} market/history checks.");
    }
}
