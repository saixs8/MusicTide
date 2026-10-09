using System.IO;
using System.Text.Json;

namespace SpectrumKlinePlayer;

public sealed partial class Form1
{
    private readonly MusicPlayground playground = new();
    private readonly PaperTrading paperTrading = new();
    private readonly List<MusicAchievement> achievements = new();
    private readonly List<TradeRound> tradeRounds = new();
    private readonly HashSet<string> roundAchievements = new();
    private int sealedBoards, priorSealedDirection;
    private double lastPlayDriftAt;
    private string playStorageError = "";
    private DateTime tradeStartedAt = DateTime.Now;
    private string achievementNotice = "";
    private double achievementNoticeUntil;
    private string PlayDataDirectory => Path.Combine(AppContext.BaseDirectory, "Data", "Playground");
    private string PlaySettingsPath => Path.Combine(AppContext.BaseDirectory, "Settings", "playground.json");
    private double CurrentMusicPrice => MusicPrice(timelineClose);

    private void LoadPlaygroundData()
    {
        T? Read<T>(string file)
        {
            try { return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file)) : default; }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return default; }
        }
        var settings = Read<PlaygroundSettings>(PlaySettingsPath);
        if (settings is not null)
        {
            var rules = (settings.Rules ?? MusicPlayground.DefaultRules).Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Keyword) && r.Keyword.Length <= 30 && Enum.IsDefined(r.Effect) && r.Seconds is >= 1 and <= 30 && double.IsFinite(r.Strength)).Select(r => r with { Strength = Math.Clamp(r.Strength, .3, 2), Source = r.Source is "Builtin" or "Local" or "AI" ? r.Source : "Manual" }).Take(LyricRuleLearning.MaximumRules).ToArray();
            if (settings.KeywordLibraryVersion < 2)
                rules = LyricRuleLearning.CompactDefaults(rules);
            playground.Settings = settings with { MenuSize = MusicPlayground.MenuSizes.Any(size => size == settings.MenuSize) ? settings.MenuSize : 10, Rules = rules, KeywordLibraryVersion = 2 };
        }
        else playground.Settings = playground.Settings with { Rules = MusicPlayground.DefaultRules, KeywordLibraryVersion = 2 };
        LoadLyricLearning();
        SavePlaySettings();
        achievements.AddRange((Read<MusicAchievement[]>(Path.Combine(PlayDataDirectory, "achievements.json")) ?? []).Where(a => a is not null).TakeLast(1000));
        tradeRounds.AddRange((Read<TradeRound[]>(Path.Combine(PlayDataDirectory, "trades.json")) ?? []).Where(r => r is not null && r.Fills is not null).TakeLast(200));
        climaxMarket.AllowReseal = true;
    }

    private void SavePlayJson<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, path, true);
            playStorageError = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { playStorageError = "玩法记录保存失败：" + e.Message; }
    }
    private void SavePlaySettings() => SavePlayJson(PlaySettingsPath, playground.Settings);
    private void ResetPlaygroundRound()
    {
        playground.Reset(); paperTrading.Reset(); roundAchievements.Clear(); sealedBoards = priorSealedDirection = 0;
        tradeStartedAt = DateTime.Now;
        achievementNotice = ""; achievementNoticeUntil = 0;
        lastPlayDriftAt = clock.Elapsed.TotalSeconds;
    }
    private void SaveTradeRound()
    {
        if (paperTrading.Fills.Count == 0) return;
        tradeRounds.Add(new(currentTrackTitle, tradeStartedAt, paperTrading.Equity(CurrentMusicPrice), paperTrading.Fills.ToArray()));
        if (tradeRounds.Count > 200) tradeRounds.RemoveAt(0);
        SavePlayJson(Path.Combine(PlayDataDirectory, "trades.json"), tradeRounds);
    }

    private void ObserveCompletedBoard(Candle candle)
    {
        if (climaxMarket.Board is not { } board) { sealedBoards = priorSealedDirection = 0; return; }
        bool sealedNow = Math.Abs(candle.Close - board.Limit) < .00001;
        if (!sealedNow) { sealedBoards = priorSealedDirection = 0; return; }
        sealedBoards = priorSealedDirection == board.Direction ? sealedBoards + 1 : 1;
        if (sealedBoards >= 5) EarnAchievement("五连板");
        if (priorSealedDirection < 0 && board.Direction > 0) EarnAchievement("地天反转（两板）");
        if (priorSealedDirection > 0 && board.Direction < 0) EarnAchievement("天地反转（两板）");
        if (climaxMarket.Resealed) EarnAchievement("炸板回封");
        priorSealedDirection = board.Direction;
    }
    private void EarnAchievement(string name)
    {
        if (!roundAchievements.Add(name)) return;
        achievements.Add(new(name, currentTrackTitle, GetPlaybackPositionSeconds(), currentLyric, DateTime.Now));
        achievementNotice = "成就解锁 · " + name; achievementNoticeUntil = clock.Elapsed.TotalSeconds + 8;
        if (achievements.Count > 1000) achievements.RemoveAt(0);
        SavePlayJson(Path.Combine(PlayDataDirectory, "achievements.json"), achievements);
    }

    private void AddPlaygroundMenu(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add("歌曲擂台…", null, (_, _) => OpenArena());
        menu.DropDownItems.Add("虚拟打板挑战…", null, (_, _) => OpenPaperTrading());
        AddToggle("多空拉锯 · 低频多头 / 高频空头", s => s.BullBear, (s, value) => s with { BullBear = value });
        AddToggle("歌曲盲盒 · 每首随机行情性格", s => s.BlindBox, (s, value) => s with { BlindBox = value });
        menu.DropDownItems.Add("重新抽取当前歌曲盲盒", null, (_, _) =>
        { playground.Settings = playground.Settings with { BlindBox = true }; playground.Reroll(GetPlaybackPositionSeconds()); SavePlaySettings(); stage.Invalidate(); });
        var boxStatus = new ToolStripMenuItem(playground.BoxStatus) { Enabled = false };
        menu.DropDownItems.Add(boxStatus);
        menu.DropDownOpening += (_, _) => boxStatus.Text = playground.BoxStatus;
        AddToggle("副歌返场行情", s => s.Chorus, (s, value) => s with { Chorus = value });
        AddToggle("歌词事件牌", s => s.LyricCards, (s, value) => s with { LyricCards = value });
        menu.DropDownItems.Add("编辑歌词关键词与事件…", null, (_, _) => EditLyricRules());
        menu.DropDownItems.Add("关键词学习与本机 AI…", null, (_, _) => OpenLyricLearning());
        menu.DropDownItems.Add("连板成就与事件记录…", null, (_, _) => OpenAchievements());
        var font = new ToolStripMenuItem("菜单字号");
        foreach (int size in MusicPlayground.MenuSizes)
        {
            string label = size switch { 10 => "小号 · 默认", 11 => "标准", 12 => "中号", 14 => "大号", 16 => "加大", 18 => "特大", 20 => "超大", 22 => "超大 +", _ => "最大" };
            var item = new ToolStripMenuItem($"{size} 磅 · {label}") { Tag = size };
            item.Click += (_, _) => { playground.Settings = playground.Settings with { MenuSize = size }; SavePlaySettings(); if (menuBar is not null) TerminalTheme.SizeMenu(menuBar, size, true); };
            font.DropDownItems.Add(item);
        }
        font.DropDownOpening += (_, _) => { foreach (var item in font.DropDownItems.OfType<ToolStripMenuItem>()) item.Checked = (int)item.Tag! == playground.Settings.MenuSize; };
        menu.DropDownItems.Add(new ToolStripSeparator()); menu.DropDownItems.Add(font);
        void AddToggle(string label, Func<PlaygroundSettings, bool> get, Func<PlaygroundSettings, bool, PlaygroundSettings> set)
        {
            var item = new ToolStripMenuItem(label) { CheckOnClick = true, Checked = get(playground.Settings) };
            item.Click += (_, _) =>
            {
                bool wasBox = playground.Settings.BlindBox;
                playground.Settings = set(playground.Settings, item.Checked);
                if (!wasBox && playground.Settings.BlindBox) playground.Reroll(GetPlaybackPositionSeconds());
                SavePlaySettings(); stage.Invalidate();
            };
            menu.DropDownOpening += (_, _) => item.Checked = get(playground.Settings);
            menu.DropDownItems.Add(item);
        }
    }

    private SongRecording[] AvailablePlayRecordings()
    {
        SaveCurrentRecording(); var list = SongRecordingStore.Load(RecordingsDirectory).ToList();
        if (recordingSamples.Count > 0) { list.RemoveAll(r => r.Id == recordingId); list.Insert(0, CurrentRecording()); }
        return list.ToArray();
    }
    private void OpenArena()
    {
        var records = AvailablePlayRecordings();
        if (records.Length < 2) { MessageBox.Show(this, "先依次播放两首歌，自动保存记录后即可开擂台。", "歌曲擂台"); return; }
        new SongArenaForm(records).Show(this);
    }
    private void OpenPaperTrading()
    {
        new PaperTradingForm(paperTrading, () => (currentTrackTitle, CurrentMusicPrice, GetPlaybackPositionSeconds(), trackIsPlaying && timelineCandleCount > 0 && currentTrackTitle is not ("等待歌曲" or "未知歌曲")),
            (buy, fraction) =>
            {
                if (!trackIsPlaying || timelineCandleCount == 0 || currentTrackTitle is "等待歌曲" or "未知歌曲") return false;
                bool ok = buy ? paperTrading.Buy(CurrentMusicPrice, fraction, GetPlaybackPositionSeconds(), currentLyric)
                    : paperTrading.Sell(CurrentMusicPrice, GetPlaybackPositionSeconds(), currentLyric);
                if (ok) SavePlayJson(Path.Combine(PlayDataDirectory, "active-trade.json"), new TradeRound(currentTrackTitle, recordingAt, paperTrading.Equity(CurrentMusicPrice), paperTrading.Fills.ToArray()));
                return ok;
            }, () => { SaveTradeRound(); paperTrading.Reset(); tradeStartedAt = DateTime.Now; }, () => tradeRounds.ToArray()).Show(this);
    }
    private void EditLyricRules()
    {
        using var dialog = new LyricRulesForm(playground.Settings.Rules ?? MusicPlayground.DefaultRules);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var removed = (playground.Settings.Rules ?? MusicPlayground.DefaultRules).Select(r => r.Keyword).Except(dialog.Rules.Select(r => r.Keyword), StringComparer.OrdinalIgnoreCase);
            lyricAiSettings = lyricAiSettings with { Suppressed = (lyricAiSettings.Suppressed ?? []).Concat(removed).Except(dialog.Rules.Select(r => r.Keyword), StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
            playground.Settings = playground.Settings with { Rules = dialog.Rules }; SavePlaySettings(); SaveLyricLearning();
        }
    }
    private void OpenAchievements()
    {
        var entries = achievements.Select(a => new { 类型 = "成就", 内容 = a.Name, 歌曲 = a.Song, 秒数 = a.Seconds.ToString("F1"), 歌词 = a.Lyric, 日期 = a.EarnedAt.ToString("MM-dd HH:mm") })
            .Concat(playground.Events.Select(e => new { 类型 = "本曲事件", 内容 = e.Name, 歌曲 = currentTrackTitle, 秒数 = e.Seconds.ToString("F1"), 歌词 = e.Lyric, 日期 = recordingAt.ToString("MM-dd HH:mm") })).Reverse().ToArray();
        PlaygroundUi.ShowRows(this, "连板成就与事件记录", entries,
            $"成就：五连板、两板地天／天地反转、同一根板炸板回封。{playground.BoxStatus}\n{(playStorageError.Length > 0 ? playStorageError : "本曲解锁后自动保存；盲盒播放 12 秒后揭晓，可在菜单重新抽取。")}");
    }
}
