using SpectrumKlinePlayer;
using System.Diagnostics;
using System.Reflection;
using System.IO;

class Program
{
    static int checks;
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); checks++; }

    [STAThread] static void Main(string[] args)
    {
        if (args.Contains("--fixture")) { Fixture(args.Last()); return; }
        foreach (var (alias, id) in new[] { ("QQMusic.exe", "qq"), ("cloudmusic", "netease"), ("SodaMusic", "soda"), ("Luna", "soda"), ("汽水音乐", "soda"), ("KuGou", "kugou") })
            Check(MusicPlayerProfiles.Find(alias)?.Id == id, "identify " + alias);
        Check(MusicPlayerProfiles.Find("notepad") is null, "unrelated apps are excluded");
        Check(MusicPlayerProfiles.CleanWindowTitle("汽水音乐") is null, "app name is not a song");
        Check(MusicPlayerProfiles.CleanWindowTitle("测试歌曲 - 测试歌手 - QQ音乐") == "测试歌曲 - 测试歌手", "strip player suffix");
        Check(MusicPlayerProfiles.SameTrack("测试歌手 - 测试歌曲", "测试歌曲 - 测试歌手"), "title orientation differs between SMTC and native player");
        Check(!MusicPlayerProfiles.SameTrack("歌手 - 歌曲", "其他歌手 - 歌曲"), "same name with another singer is a distinct track");
        Check(PlayerWindowCapture.ParseProgress(["01:30", "04:00"]) is { Position: 90, Duration: 240 }, "separate time controls");
        Check(PlayerWindowCapture.ParseProgress(["04:00", "01:30"]) is { Position: 90, Duration: 240 }, "reverse UI traversal preserves time");
        Check(PlayerWindowCapture.ParseProgress(["01:99", "04:00"]) is null, "invalid time rejected");
        Check(PlayerWindowCapture.ParseProgress(["01:30 / 04:00"]) is { Position: 90, Duration: 240 }, "combined time control");
        Check(PlayerWindowCapture.CleanLyric("测试歌曲", "测试歌手 - 测试歌曲") is null, "song label cannot become lyric");
        var lines = CrossPlayerLyricCapture.ParseLrc("[offset:500]\n[00:02.5][00:10.00]第一句\n[00:06.00]\n[00:05.10]第二句\n[00:99.00]错误时间");
        Check(lines.Length == 4 && lines[0].StartSeconds == 2 && lines[2].Text == "" && lines[3].StartSeconds == 9.5, "LRC fractions, offset, repeated timestamps, silence and sorting");
        var repeats = new TimedLyricLine[] { new(10, "同一句"), new(80, "同一句"), new(30, "唯一歌词") };
        Check(Form1.MatchObservedLine(repeats, "同一句", null) is null, "ambiguous chorus cannot guess mid-song start");
        Check(Form1.MatchObservedLine(repeats, "同一句", 78)?.StartSeconds == 80, "known clock disambiguates repeated chorus");
        Check(Form1.MatchObservedLine(repeats, "唯一歌词", null)?.StartSeconds == 30, "unique desktop line anchors mid-song");
        Check(CrossPlayerLyricCapture.MatchScore("晴天", "周杰伦", 269, "晴天", "周杰伦", 270) >= 0, "match exact title singer and duration");
        Check(CrossPlayerLyricCapture.MatchScore("晴天", "周杰伦", 269, "晴天", "翻唱歌手", 269) < 0, "reject cover by different singer");
        Check(CrossPlayerLyricCapture.MatchScore("晴天", "周杰伦", 269, "晴天", "周杰伦", 299) < 0, "reject different live version length");
        Check(CrossPlayerLyricCapture.MatchScore("晴天", "", -1, "晴天", "周杰伦", 269) < 0, "missing singer and duration cannot identify a version");
        var watch = Stopwatch.StartNew();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        _ = CrossPlayerLyricCapture.Poll("旧歌曲", "", "旧歌曲", 120, cancelled.Token);
        Check(CrossPlayerLyricCapture.Poll("新歌曲", "", "新歌曲", 120, cancelled.Token) is null && watch.ElapsedMilliseconds < 100, "song switch immediately invalidates pending lyrics without waiting for network");
        foreach (string alias in new[] { "QQMusic", "cloudmusic", "SodaMusic" }) CheckRealWindow(alias);
        if (args.Contains("--network"))
        {
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var document = Task.Run(() => CrossPlayerLyricCapture.LoadAsync("network-test-孤勇者", "陈奕迅 - 孤勇者", "陈奕迅", "孤勇者", -1, ct.Token)).GetAwaiter().GetResult();
            Check(document is { Lines.Length: > 20 }, "live provider returns a full synchronized lyric document");
            Console.WriteLine($"NETWORK source={document!.Source} lines={document.Lines.Length} duration={document.Duration}");
            var cached = Task.Run(() => CrossPlayerLyricCapture.LoadAsync("network-test-孤勇者", "陈奕迅 - 孤勇者", "陈奕迅", "孤勇者", -1, CancellationToken.None)).GetAwaiter().GetResult();
            Check(cached?.Lines.Length == document.Lines.Length, "downloaded lyrics reuse local cache");
        }
        Console.WriteLine($"All {checks} multi-player checks passed. Native window fixtures are test players, not installed vendor clients.");
    }

    static void CheckRealWindow(string alias)
    {
        string directory = AppContext.BaseDirectory;
        string path = Path.Combine(directory, alias + ".exe");
        File.Copy(Environment.ProcessPath!, path, true);
        using var process = Process.Start(new ProcessStartInfo(path, "--fixture " + alias) { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var player = MusicPlayerProfiles.Find(alias)!;
            string title = "测试歌手 - 测试歌曲";
            PlayerWindowSnapshot? read = null;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 7)
            {
                read = PlayerWindowCapture.Poll(player, title);
                if (read is { Position: >= 90, Duration: 240, Lyric: not null }) break;
                Thread.Sleep(100);
            }
            Check(read is { Position: >= 90, Duration: 240 }, alias + " reads actual separate WinForms clock controls");
            Check(read?.Lyric == "这是正在演唱的测试歌词", alias + " reads actual desktop lyric text");
            CheckPipeline(player, title);
            Check(PlayerWindowCapture.Poll(player, "其他歌手 - 新歌曲") is null, alias + " old snapshot cannot leak into next song");
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } }
    }

    static void CheckPipeline(MusicPlayerProfile player, string title)
    {
        const BindingFlags stat = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        using var form = new Form1();
        ((CancellationTokenSource)typeof(Form1).GetField("lyricCaptureCts", instance)!.GetValue(form)!).Cancel();
        var captureTask = (Task?)typeof(Form1).GetField("lyricCaptureTask", instance)!.GetValue(form);
        try { captureTask?.Wait(3000); } catch (AggregateException) { }
        typeof(WindowsMediaSessionCapture).GetField("<UsesEstimatedTimeline>k__BackingField", stat)!.SetValue(null, false);
        typeof(WindowsMediaSessionCapture).GetField("<ActiveSong>k__BackingField", stat)!.SetValue(null, "测试歌曲");
        typeof(WindowsMediaSessionCapture).GetField("<ActiveArtist>k__BackingField", stat)!.SetValue(null, "测试歌手");
        string key = MusicPlayerProfiles.Key(title) + "|240";
        var doc = new PlayerLyricDocument(key, "verification", 240, [new(90, "当前句"), new(100, "下一句")]);
        typeof(CrossPlayerLyricCapture).GetField("currentKey", stat)!.SetValue(null, key);
        typeof(CrossPlayerLyricCapture).GetField("current", stat)!.SetValue(null, doc);
        typeof(CrossPlayerLyricCapture).GetField("pending", stat)!.SetValue(null, null);
        var method = typeof(Form1).GetMethod("CaptureOtherPlayer", instance)!;
        (MediaTrackInfo? Track, string? Title, TimedLyricLine[] Lines, LyricFrame? Frame, string Status) Capture(string name, double position, bool playing, CancellationToken ct = default)
            => ((MediaTrackInfo?, string?, TimedLyricLine[], LyricFrame?, string))method.Invoke(form, [player, new MediaTrackInfo(name, 240, position, playing), ct])!;
        var first = Capture(title, 95, true);
        Check(first.Frame?.Text == "当前句" && first.Track?.PositionSeconds == 95, player.Id + " mid-song pipeline follows SMTC over window clock");
        var pause = Capture(title, 95, false);
        Check(pause.Track?.IsPlaying == false && pause.Frame?.Text == "当前句", player.Id + " pipeline preserves pause");
        Check(Capture(title, 105, true).Frame?.Text == "下一句", player.Id + " seeking advances lyric");
        Check(Capture(title, 92, true).Frame?.Text == "当前句", player.Id + " backward seek returns previous lyric");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var next = Capture("其他歌手 - 新歌曲", 12, true, cancellation.Token);
        Check(next.Lines.Length == 0 && next.Frame is null, player.Id + " song change clears lyric document immediately");
    }

    static void Fixture(string alias)
    {
        ApplicationConfiguration.Initialize();
        var profile = MusicPlayerProfiles.Find(alias)!;
        using var main = new Form { Text = "测试歌曲 - 测试歌手 - " + profile.Name,
            ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2500, 100), Size = new Size(300, 180) };
        main.Controls.Add(new Label { Text = "01:30", Location = new Point(10, 30) });
        main.Controls.Add(new Label { Text = "04:00", Location = new Point(120, 30) });
        using var lyric = new Form { Text = "桌面歌词", ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(-2500, 300), Size = new Size(400, 100) };
        lyric.Controls.Add(new Label { Text = "这是正在演唱的测试歌词", AutoSize = true });
        main.Shown += (_, _) => lyric.Show();
        Application.Run(main);
    }
}
