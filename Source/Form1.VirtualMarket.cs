using System.IO;
using System.Text.Json;

namespace SpectrumKlinePlayer;

public sealed partial class Form1
{
    private double songOpeningPrice = 100;
    private double virtualVolume, virtualAmount;
    private MarketAxis? terminalAxis;
    private readonly Dictionary<string, SongHeat> songHeats = new();
    private string HeatPath => Path.Combine(AppContext.BaseDirectory, "Settings", "song-heat.json");
    private SongHeat CurrentHeat => songHeats.TryGetValue(currentTrackKey, out var heat) ? heat : SongHeat.Estimate(currentTrackKey);
    private double MusicPrice(double logarithm) => songOpeningPrice * Math.Exp(logarithm);
    private static string MarketNumber(double value) => value >= 1e8 ? $"{value / 1e8:0.00}亿" : value >= 1e4 ? $"{value / 1e4:0.00}万" : $"{value:0.00}";

    private void LoadSongHeats()
    {
        try
        {
            if (!File.Exists(HeatPath)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, SongHeat>>(File.ReadAllText(HeatPath));
            if (data is not null) foreach (var pair in data.Where(p => p.Value is { Likes: >= 0 and <= 1000000000, Comments: >= 0 and <= 1000000000 })) songHeats[pair.Key] = pair.Value;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }
    private void EditSongHeat()
    {
        var heat = CurrentHeat;
        using var form = new Form { Text = "歌曲评论与喜欢数", Size = new(430, 235), StartPosition = FormStartPosition.CenterParent, BackColor = TerminalTheme.Panel, ForeColor = TerminalTheme.Text };
        var likes = new NumericUpDown { Minimum = 0, Maximum = 1000000000, Value = heat.Likes, Bounds = new(190, 20, 185, 28), ThousandsSeparator = true };
        var comments = new NumericUpDown { Minimum = 0, Maximum = 1000000000, Value = heat.Comments, Bounds = new(190, 62, 185, 28), ThousandsSeparator = true };
        form.Controls.AddRange([new Label { Text = "喜欢数量", Bounds = new(20, 22, 160, 26) }, likes,
            new Label { Text = "评论数量", Bounds = new(20, 64, 160, 26) }, comments,
            new Label { Text = "可填写播放器上的实际热度；市值与财务指标仍为虚拟。", Bounds = new(20, 103, 370, 40) }]);
        var save = new Button { Text = "保存热度", Bounds = new(270, 151, 105, 30) };
        save.Click += (_, _) =>
        {
            try
            {
                songHeats[currentTrackKey] = new((long)likes.Value, (long)comments.Value, false);
                Directory.CreateDirectory(Path.GetDirectoryName(HeatPath)!);
                File.WriteAllText(HeatPath, JsonSerializer.Serialize(songHeats));
                stage.Invalidate(); form.DialogResult = DialogResult.OK;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show(form, e.Message, "热度保存失败"); }
        };
        form.Controls.Add(save); form.ShowDialog(this);
    }
    private void DrawMarketOverview(Graphics g, Rectangle bounds)
    {
        TerminalFill(g, bounds, TerminalTheme.Panel);
        using var border = new Pen(TerminalTheme.Border); g.DrawRectangle(border, bounds);
        var h = CurrentHeat;
        double price = chartMode == ChartMode.MinuteLine ? MusicPrice(minuteLineValue) : CurrentMusicPrice;
        double eps = songOpeningPrice / h.OpeningPE;
        string source = simulationMode ? "模拟频谱" : liveAudio ? "系统音频 · 实时" : "等待音频";
        TerminalText(g, $"虚拟行情 · {(h.Simulated ? "模拟热度" : "手动热度")} · {source}", new Rectangle(bounds.Left + 9, bounds.Top, bounds.Width - 151, 19), TerminalTheme.Cyan, 8);
        TerminalText(g, $"{FormatSongTime(GetPlaybackPositionSeconds())} / {FormatSongTime(songDurationSeconds)}", new Rectangle(bounds.Right - 142, bounds.Top, 133, 19), TerminalTheme.MenuText, 9, false, StringAlignment.Far);
        string[] cells = MarketOverviewCells();
        for (int i = 0; i < cells.Length; i++)
            TerminalText(g, cells[i], new RectangleF(bounds.Left + 9 + i % 3 * (bounds.Width - 18) / 3f, bounds.Top + 20 + i / 3 * 20, (bounds.Width - 18) / 3f - 5, 20),
                i == 0 ? price >= songOpeningPrice ? TerminalTheme.Rise : TerminalTheme.Fall : i is 1 or 2 ? TerminalTheme.Yellow : TerminalTheme.MenuText, 8.5f);

    }
    private string[] MarketOverviewCells()
    {
        var h = CurrentHeat;
        double price = chartMode == ChartMode.MinuteLine ? MusicPrice(minuteLineValue) : CurrentMusicPrice;
        double eps = songOpeningPrice / h.OpeningPE;
        string[] cells = [$"现价 {price:0.00}  {(price / songOpeningPrice - 1) * 100:+0.00;-0.00;0.00}%",
            $"总市值 {MarketNumber(price * h.Shares)}", $"流通市值 {MarketNumber(price * h.FloatShares)}", $"市盈率 {price / eps:0.00}",
            $"市净率 {price / (songOpeningPrice / 2.5):0.00}", $"换手率 {virtualVolume / h.FloatShares:P2}",
            $"成交量 {MarketNumber(virtualVolume / 100)}手", $"喜欢 {MarketNumber(h.Likes)}", $"评论 {MarketNumber(h.Comments)}"];
        if (chartMode == ChartMode.DayK)
            cells = [$"现价 {price:0.00}  {(price / songOpeningPrice - 1) * 100:+0.00;-0.00;0.00}%",
                $"总市值 {MarketNumber(price * h.Shares)}", $"流通市值 {MarketNumber(price * h.FloatShares)}",
                $"市盈率 {price / eps:0.00}", $"市净率 {price / (songOpeningPrice / 2.5):0.00}", $"总股本 {MarketNumber(h.Shares)}股",
                $"流通股本 {MarketNumber(h.FloatShares)}股", $"喜欢 {MarketNumber(h.Likes)}", $"评论 {MarketNumber(h.Comments)}"];
        return cells;
    }
    private string minuteVolumeCacheKey = "";
    private double[] minuteVolumeCache = [];
    private double[] MinuteVolumes()
    {
        string key = $"{recordingId}:{recordingSamples.Count}:{minutePointCapacity}:{songDurationSeconds}";
        if (key == minuteVolumeCacheKey) return minuteVolumeCache;
        minuteVolumeCacheKey = key; minuteVolumeCache = new double[minutePointCapacity];
        foreach (var sample in recordingSamples)
        {
            int index = Math.Clamp((int)(sample.Seconds / songDurationSeconds * (minutePointCapacity - 1)), 0, minutePointCapacity - 1);
            minuteVolumeCache[index] += sample.Volume;
        }
        return minuteVolumeCache;
    }
    private void DrawMinuteVolume(Graphics g, Rectangle plot)
    {
        TerminalText(g, $"分时成交量 {MarketNumber(virtualVolume / 100)}手 · 成交额 {MarketNumber(virtualAmount)}", new Rectangle(plot.Left, plot.Top - 19, plot.Width, 19), TerminalTheme.Yellow, 8);
        using var grid = new Pen(TerminalTheme.Grid); g.DrawRectangle(grid, plot);
        var volumes = MinuteVolumes(); double max = Math.Max(1, volumes.Max());
        var values = GetMinuteValuesInOrder().ToArray();
        var state = g.Save(); g.SetClip(plot);
        float width = Math.Max(1, plot.Width / (float)minutePointCapacity * .65f);
        using var up = new Pen(TerminalTheme.Rise, width); using var down = new Pen(TerminalTheme.Cyan, width);
        for (int i = 0; i < Math.Min(values.Length, volumes.Length); i++)
        {
            float x = plot.Left + i * plot.Width / (float)Math.Max(1, minutePointCapacity - 1);
            g.DrawLine(i == 0 || values[i] >= values[i-1] ? up : down, x, plot.Bottom, x, plot.Bottom - (float)(volumes[i] / max) * plot.Height);
        }
        g.Restore(state);
    }
    private void EditLibraryCapacity()
    {
        using var form = new Form { Text = "历史库容量", Size = new(440, 205), StartPosition = FormStartPosition.CenterParent };
        var count = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = SongRecordingStore.Settings(RecordingsDirectory).MaxSongs, Bounds = new(245, 25, 150, 28) };
        form.Controls.AddRange([new Label { Text = "最多保留歌曲数量", Bounds = new(20, 25, 215, 28) }, count, new Label { Text = "每歌最多 5 版，优先保留重复听过的歌。\n移出历史库的旧版保存在 Retired，便于恢复。", Bounds = new(20, 63, 380, 45) }]);
        var save = new Button { Text = "保存", Bounds = new(285, 115, 110, 30) };
        save.Click += (_, _) => { try { SongRecordingStore.Configure(RecordingsDirectory, (int)count.Value); form.DialogResult = DialogResult.OK; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show(form, e.Message); } };
        form.Controls.Add(save); form.ShowDialog(this);
    }
    private void OpenCandleDetail(int index)
    {
        var record = CurrentRecording();
        var samples = record.Samples.Where(s => s.BarIndex == index).ToArray();
        if (samples.Length == 0) { MessageBox.Show(this, "这根 K 柱还没有采集到分时数据。", "K 柱走势"); return; }
        new SongHistoryForm([record with { Samples = samples, Bars = [], Title = $"{record.Title} · 第 {index + 1} 根走势" }], true).Show(this);
    }
}
