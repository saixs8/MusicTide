using System.IO;
using System.Text.Json;

namespace SpectrumKlinePlayer;

public sealed partial class Form1
{
    private readonly ClimaxMarket climaxMarket = new();
    private float musicEnergy;
    private double lastEnergyAt;
    private bool trackIsPlaying = true;
    private readonly List<SongSample> recordingSamples = new();
    private string recordingId = Guid.NewGuid().ToString("N");
    private DateTime recordingAt = DateTime.Now;
    private double lastRecordingPosition = -1, nextRecordingSave;
    private double recordingStartClock;
    private int recordingSegment;
    private bool recordingSimulated, recordingAuthoritative = true, recordingFinished;
    private string recordingError = string.Empty;
    private string RecordingsDirectory => Path.Combine(AppContext.BaseDirectory, "Data", "SongRecordings");
    private string ClimaxSettingsPath => Path.Combine(AppContext.BaseDirectory, "Settings", "climax.json");

    private void LoadClimaxSettings()
    {
        try
        {
            if (!File.Exists(ClimaxSettingsPath)) return;
            var s = JsonSerializer.Deserialize<ClimaxSettings>(File.ReadAllText(ClimaxSettingsPath));
            if (s is not null && s.Threshold is >= .3 and <= .95 && s.HoldSeconds is >= .2 and <= 15
                && s.TriggerProbability is >= 0 and <= 1 && s.MinimumBoards is >= 2 and <= 12
                && s.MaximumBoards >= s.MinimumBoards && s.MaximumBoards <= 12
                && s.OneWordProbability is >= 0 and <= 1 && s.BreakProbabilityPerSecond is >= 0 and <= .1
                && s.ExtremeSeconds is >= 3 and <= 120) climaxMarket.Settings = s;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    private ToolStripMenuItem BuildMusicFeaturesMenu()
    {
        var menu = new ToolStripMenuItem("音乐玩法");
        var enabled = new ToolStripMenuItem("高潮连板") { CheckOnClick = true, Checked = climaxMarket.Settings.Enabled };
        enabled.Click += (_, _) => { climaxMarket.Settings = climaxMarket.Settings with { Enabled = enabled.Checked }; climaxMarket.Cancel(); SaveClimaxSettings(); };
        menu.DropDownItems.Add(enabled);
        menu.DropDownItems.Add("连板与炸板设置…", null, (_, _) => EditClimaxSettings());
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add("双歌对比…", null, (_, _) => OpenSongComparison());
        menu.DropDownItems.Add("保存当前歌曲记录", null, (_, _) =>
        {
            SaveCurrentRecording();
            MessageBox.Show(this, recordingSamples.Count == 0 ? "识别歌曲并播放后，才会生成可对比的记录。" :
                recordingError.Length > 0 ? recordingError : "已保存，可以在“双歌对比”中选择。", "歌曲记录");
        });
        menu.DropDownItems.Add("歌曲历史 · 最多五版叠加…", null, (_, _) => { SaveCurrentRecording(); var saved = SongRecordingStore.Load(RecordingsDirectory); if (saved.Length == 0) { MessageBox.Show(this, "尚无歌曲线图记录。", "歌曲历史"); return; } new SongHistoryForm(saved).Show(this); });
        menu.DropDownItems.Add("历史库容量…", null, (_, _) => EditLibraryCapacity());
        menu.DropDownItems.Add("评论与喜欢数量…", null, (_, _) => EditSongHeat());
        AddPlaygroundMenu(menu);
        menu.DropDownOpening += (_, _) => enabled.Checked = climaxMarket.Settings.Enabled;
        return menu;
    }

    private void SaveClimaxSettings()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(ClimaxSettingsPath)!); File.WriteAllText(ClimaxSettingsPath, JsonSerializer.Serialize(climaxMarket.Settings)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show(this, e.Message, "设置保存失败"); }
    }

    private void EditClimaxSettings()
    {
        var s = climaxMarket.Settings;
        using var dialog = new Form { Text = "高潮连板与炸板设置", Size = new(520, 545), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, BackColor = TerminalTheme.Panel, ForeColor = TerminalTheme.Text, Font = Font };
        var enabled = new CheckBox { Text = "启用高潮连板（每首歌最多一次，方向随机）", Checked = s.Enabled, Bounds = new(20, 15, 465, 30) };
        dialog.Controls.Add(enabled);
        string[] labels = ["高潮能量阈值（%）", "高能量持续时间（秒）", "每根K线触发概率（%）", "最少连续板数", "最多连续板数", "一字板概率（%）", "普通炸板概率（% / 秒）", "极端能量多久后更易炸板（秒）"];
        decimal[] values = [(decimal)s.Threshold * 100, (decimal)s.HoldSeconds, (decimal)s.TriggerProbability * 100,
            s.MinimumBoards, s.MaximumBoards, (decimal)s.OneWordProbability * 100, (decimal)s.BreakProbabilityPerSecond * 100, (decimal)s.ExtremeSeconds];
        decimal[] minima = [30, .2m, 0, 2, 2, 0, 0, 3], maxima = [95, 15, 100, 12, 12, 100, 10, 120];
        var inputs = new NumericUpDown[8];
        for (int i = 0; i < labels.Length; i++)
        {
            dialog.Controls.Add(new Label { Text = labels[i], Bounds = new(20, 57 + i * 40, 330, 27), TextAlign = ContentAlignment.MiddleLeft });
            inputs[i] = new NumericUpDown { Bounds = new(356, 59 + i * 40, 120, 27), Minimum = minima[i], Maximum = maxima[i],
                DecimalPlaces = i is 1 or 6 ? 1 : 0, Increment = i is 1 or 6 ? .1m : 1, Value = values[i], BackColor = TerminalTheme.Input, ForeColor = TerminalTheme.Text };
            dialog.Controls.Add(inputs[i]);
        }
        dialog.Controls.Add(new Label { Text = "默认：连续 2–5 根，一字板 65%，普通炸板 0.3%/秒。\n高能量或低能量持续超时后，炸板概率逐渐上升。", Bounds = new(20, 385, 465, 52) });
        var ok = new Button { Text = "保存", Bounds = new(370, 449, 106, 32), BackColor = TerminalTheme.Selected, ForeColor = TerminalTheme.Text };
        ok.Click += (_, _) =>
        {
            if (inputs[4].Value < inputs[3].Value) { MessageBox.Show(dialog, "最多板数不能小于最少板数。"); return; }
            climaxMarket.Settings = new(enabled.Checked, (double)inputs[0].Value / 100, (double)inputs[1].Value,
                (double)inputs[2].Value / 100, (int)inputs[3].Value, (int)inputs[4].Value,
                (double)inputs[5].Value / 100, (double)inputs[6].Value / 100, (double)inputs[7].Value);
            climaxMarket.Cancel(); SaveClimaxSettings(); dialog.DialogResult = DialogResult.OK;
        };
        dialog.Controls.Add(ok); dialog.AcceptButton = ok; dialog.ShowDialog(this);
    }

    private void ObserveMusicEnergy()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = lastEnergyAt == 0 ? 0 : now - lastEnergyAt; lastEnergyAt = now;
        float target = AverageBandLevel(0, 18) * .55f + AverageBandLevel(0, BandCount) * .45f;
        if (!hasAudioSignal) { musicEnergy = 0; return; }
        musicEnergy += (target - musicEnergy) * (float)(1 - Math.Exp(-Math.Clamp(dt, 0, .25) / .7));
        if (trackIsPlaying && hasAudioSignal)
        {
            climaxMarket.Observe(musicEnergy, dt);
            int gameLyricIndex = lyricTimeline.Length > 0 ? activeLyricIndex : liveLyricHistory.Count - 1;
            playground.Update(GetPlaybackPositionSeconds(), dt, gameLyricIndex, currentLyric);
        }
    }

    private void BeginClimaxBar(ref Candle candle, float previousClose)
    {
        if (playground.Settings.BlindBox && playground.Personality == SongPersonality.LimitFlip && musicEnergy >= climaxMarket.Settings.Threshold)
            climaxMarket.QueueSingleBoard(playground.NextFlipDirection());
        if (climaxMarket.BeginBar(previousClose) is not { } board) return;
        candle.Open = candle.Close = candle.High = candle.Low = (float)board.Open;
        candle.LimitDirection = board.Direction;
    }

    private void ApplyClimaxBar(ref Candle candle, bool finish = false)
    {
        if (climaxMarket.Board is not { } board || !climaxMarket.Settings.Enabled) return;
        float close;
        if (board.OneWord && !climaxMarket.Broken) close = (float)board.Limit;
        else if (climaxMarket.Broken)
            close = (float)(board.Limit + (board.PreviousClose - board.Limit) * (.18 + .15 * musicEnergy));
        else
        {
            double phase = Math.Clamp((clock.Elapsed.TotalSeconds - dayKCandleStart) / candleDurationSeconds, 0, 1);
            double progress = finish ? 1 : Math.Clamp(phase * 1.3 + (musicEnergy - .5) * .12, 0, 1);
            close = (float)(board.Open + (board.Limit - board.Open) * progress);
        }
        candle.Close = close;
        candle.High = Math.Max(candle.High, close); candle.Low = Math.Min(candle.Low, close);
        candle.Velocity = close - candle.Open;
        timelineClose = close;
    }

    private SongRecording CurrentRecording() => new(recordingId, currentTrackTitle, songDurationSeconds,
        recordingAt, recordingSimulated, recordingAuthoritative, recordingSamples.ToArray(),
        lyricTimeline.Length > 0 ? lyricTimeline.ToArray() : liveLyricHistory.ToArray(),
        playground.Settings.BlindBox ? playground.PersonalityName : "", playground.Events.ToArray(), songOpeningPrice, GetDayCandles().Select((c, i) => new SongBar(c.Open, c.High, c.Low, c.Close, i)).ToArray(), lastRecordingPosition >= songDurationSeconds - 1.5, CurrentHeat, BandSecurity(minuteBandIndex).Code, minuteBandIndex, frequencyMidpointBand);

    private void SampleSongRecording()
    {
        if (!trackIsPlaying || currentTrackTitle is "等待歌曲" or "未知歌曲" || timelineCandleCount == 0) return;
        double position = lastTrackPositionSeconds >= 0 ? GetPlaybackPositionSeconds()
            : Math.Clamp(clock.Elapsed.TotalSeconds - recordingStartClock, 0, songDurationSeconds);
        if (lastRecordingPosition >= 0 && position < lastRecordingPosition - 1)
        { recordingSegment++; lastRecordingPosition = -1; climaxMarket.Cancel(); }
        if (lastRecordingPosition >= 0 && position - lastRecordingPosition < .1) return;
        if (lastRecordingPosition >= 0 && position - lastRecordingPosition > 3) { recordingSegment++; climaxMarket.Cancel(); }
        if (recordingSamples.Count >= 20000) return;
        double dt = lastRecordingPosition >= 0 ? Math.Clamp(position - lastRecordingPosition, 0, 1) : .1;
        lastRecordingPosition = position;
        double volume = CurrentHeat.FloatShares * (.15 + musicEnergy * musicEnergy) * .000008 * dt;
        virtualVolume += volume; double amount = volume * CurrentMusicPrice; virtualAmount += amount;
        var c = timelineCandles[timelineCandleCount - 1];
        recordingSamples.Add(new(position, c.Open, c.High, c.Low, c.Close, musicEnergy,
            (climaxMarket.Board is not null ? climaxMarket.Status : "") +
                (playground.CurrentNotification(position).Length > 0 ? " · " + playground.CurrentNotification(position) : ""), recordingSegment, dayHistory.Count, volume, amount));
        recordingSimulated |= simulationMode;
        recordingAuthoritative &= lastTrackPositionSeconds >= 0;
        if (!recordingFinished && position >= songDurationSeconds - 1.5) { recordingFinished = true; SaveCurrentRecording(); }
        if (clock.Elapsed.TotalSeconds >= nextRecordingSave)
        { nextRecordingSave = clock.Elapsed.TotalSeconds + 30; SaveCurrentRecording(); _ = LearnLyrics(false); }
    }

    private void SaveCurrentRecording()
    {
        try { SongRecordingStore.Save(RecordingsDirectory, CurrentRecording()); recordingError = string.Empty; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { recordingError = "歌曲记录保存失败：" + e.Message; }
    }

    private void ResetMusicFeatures()
    {
        SaveTradeRound();
        SaveCurrentRecording(); _ = LearnLyrics(false); recordingSamples.Clear(); recordingId = Guid.NewGuid().ToString("N");
        recordingAt = DateTime.Now; lastRecordingPosition = -1; recordingSegment = 0;
        recordingStartClock = clock.Elapsed.TotalSeconds;
        recordingSimulated = false; recordingAuthoritative = true; recordingFinished = false; trackIsPlaying = true;
        climaxMarket.Reset(); musicEnergy = 0; lastEnergyAt = clock.Elapsed.TotalSeconds;
        ResetPlaygroundRound();
    }

    private void OpenSongComparison()
    {
        SaveCurrentRecording();
        var recordings = SongRecordingStore.Load(RecordingsDirectory).ToList();
        if (recordingSamples.Count > 0)
        { recordings.RemoveAll(r => r.Id == recordingId); recordings.Insert(0, CurrentRecording()); }
        if (recordings.Count < 2)
        {
            MessageBox.Show(this, "需要至少两份歌曲记录。\n先播放一首歌，再切换另一首；记录会自动保存。\n再次进入“双歌对比”即可选择。", "双歌对比"); return;
        }
        new SongComparisonForm(recordings.ToArray()).Show(this);
    }
}
