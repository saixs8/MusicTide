using System.Drawing.Drawing2D;

namespace SpectrumKlinePlayer;

public sealed class SongHistoryForm : Form
{
    private readonly ComboBox songs = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400 };
    private readonly CheckedListBox versions = new() { Dock = DockStyle.Left, Width = 250, CheckOnClick = true };
    private readonly CheckBox candles = new() { Text = "日 K（双击柱查看分时）", Checked = true, AutoSize = true };
    private readonly CheckBox normalized = new() { Text = "版本按首价对齐", Checked = true, AutoSize = true };
    private readonly HistoryCanvas canvas = new() { Dock = DockStyle.Fill };
    private readonly DayKViewport view = new();
    private readonly SongRecording[] records;
    private readonly bool detail;
    private Rectangle plot;
    private int dragX, dragStart;
    private bool dragging;
    private Point? pointer;
    private SongRecording[] Selected => versions.CheckedItems.Cast<SongRecording>().Take(5).ToArray();
    public SongHistoryForm(SongRecording[] records, bool detail = false)
    {
        this.records = records; this.detail = detail;
        Text = detail ? "K 柱分时走势" : "歌曲历史 · 最多五版重叠";
        Size = new(1180, 740); MinimumSize = new(850, 520); StartPosition = FormStartPosition.CenterParent;
        BackColor = TerminalTheme.Background; ForeColor = TerminalTheme.Text; Font = new("Microsoft YaHei UI", 9);
        versions.BackColor = TerminalTheme.Panel; versions.ForeColor = TerminalTheme.Text;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new(9), BackColor = TerminalTheme.Panel };
        var latest = new Button { Text = "回到最新", AutoSize = true };
        latest.Click += (_, _) => { view.Latest(Count()); canvas.Invalidate(); };
        top.Controls.AddRange([songs, candles, normalized, latest,
            new Label { Text = "Ctrl+滚轮缩放 · 左右拖动 · 勾选版本叠加；空白为未采集区段", AutoSize = true, ForeColor = TerminalTheme.Muted }]);
        Controls.Add(canvas); Controls.Add(versions); Controls.Add(top);
        songs.Items.AddRange(records.GroupBy(r => SongRecordingStore.Key(r.Title)).Select(g => (object)g.First().Title).ToArray());
        songs.SelectedIndexChanged += (_, _) =>
        {
            versions.Items.Clear();
            foreach (var r in records.Where(r => SongRecordingStore.Key(r.Title) == SongRecordingStore.Key((string)songs.SelectedItem!)).OrderByDescending(r => r.RecordedAt).Take(5)) versions.Items.Add(r, true);
            view.Reset(); canvas.Invalidate();
        };
        versions.ItemCheck += (_, _) => { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => canvas.Invalidate())); };
        candles.CheckedChanged += (_, _) => { view.Reset(); canvas.Invalidate(); };
        normalized.CheckedChanged += (_, _) => canvas.Invalidate();
        if (detail) { candles.Checked = false; candles.Enabled = false; normalized.Checked = false; }
        canvas.Paint += (_, e) => Draw(e.Graphics);
        canvas.MouseDown += (_, e) =>
        {
            canvas.Focus(); if (e.Button != MouseButtons.Left || !plot.Contains(e.Location)) return;
            if (e.Clicks > 1 && candles.Checked)
            {
                dragging = false; canvas.Capture = false;
                int index = view.Start + (int)((e.X - plot.Left) * view.Capacity / (double)plot.Width);
                var first = Selected.FirstOrDefault();
                if (first is null) return;
                var bars = Bars(first); if (index < 0 || index >= bars.Length) return;
                var data = first.Samples.Where(s => s.BarIndex >= 0 ? s.BarIndex == bars[index].Index : (int)(s.Seconds / 2) == bars[index].Index).ToArray();
                if (data.Length == 0) { MessageBox.Show(this, "该柱没有保存分时采样。", "K 柱走势"); return; }
                new SongHistoryForm([first with { Title = $"{first.Title} · 第 {index + 1} 根走势", Samples = data, Bars = [] }], true).Show(this);
                return;
            }
            dragging = true; dragX = e.X; dragStart = view.Start; canvas.Capture = true;
        };
        canvas.MouseMove += (_, e) => { pointer = e.Location; if (dragging) view.MoveTo(dragStart + (int)Math.Round((dragX - e.X) * view.Capacity / (double)Math.Max(1, plot.Width)), Count()); canvas.Invalidate(); };
        canvas.MouseUp += (_, _) => { dragging = false; canvas.Capture = false; };
        canvas.MouseCaptureChanged += (_, _) => dragging = false;
        canvas.MouseWheel += (_, e) => { if (plot.Contains(e.Location) && (ModifierKeys & Keys.Control) != 0) { view.Zoom(Math.Sign(e.Delta), Count(), (e.X - plot.Left) / (double)plot.Width); canvas.Invalidate(); } };
        songs.SelectedIndex = 0;
        if (detail) while (view.Capacity > Math.Max(12, Count())) view.Zoom(1, Count());
    }
    private static SongBar[] Bars(SongRecording r) => r.Bars is { Length: > 0 } ? r.Bars : r.Samples.GroupBy(s => s.BarIndex >= 0 ? s.BarIndex : (int)(s.Seconds / 2))
        .Select(g => new SongBar(g.First().Open, g.Max(s => s.High), g.Min(s => s.Low), g.Last().Close, g.Key)).ToArray();
    private double AxisStart => detail ? Selected.SelectMany(r => r.Samples).Select(s => s.Seconds).DefaultIfEmpty(0).Min() : 0;
    private double AxisEnd => detail ? Selected.SelectMany(r => r.Samples).Select(s => s.Seconds).DefaultIfEmpty(1).Max() + .1 : Selected.Select(r => r.Duration).DefaultIfEmpty(1).Max();
    private int LineIndex(double seconds) => (int)Math.Round((seconds - AxisStart) * 10);
    private int Count() => candles.Checked ? Selected.Select(r => Bars(r).Length).DefaultIfEmpty(0).Max() : Math.Clamp((int)Math.Ceiling((AxisEnd - AxisStart) * 10), 1, 864000);
    private double SecondsAt(int index) => AxisStart + index / 10d;
    private static readonly Color[] Colors = [TerminalTheme.Cyan, TerminalTheme.Yellow, Color.Magenta, Color.LimeGreen, Color.Orange];
    private void Draw(Graphics g)
    {
        g.Clear(TerminalTheme.Background); g.SmoothingMode = SmoothingMode.AntiAlias;
        var selected = Selected; if (selected.Length == 0) return;
        var first = selected[0]; int total = Count(); var range = view.Range(total);
        plot = new(65, 82, Math.Max(10, canvas.Width - 132), Math.Max(40, canvas.Height - 205));
        var energy = new Rectangle(plot.Left, plot.Bottom + 34, plot.Width, 55);
        double Base(SongRecording r) => normalized.Checked ? first.OpeningPrice : r.OpeningPrice;
        double Price(SongRecording r, double v) => Base(r) * Math.Exp(v);
        var values = selected.SelectMany(r => candles.Checked
            ? Bars(r).Skip(range.Start).Take(range.End - range.Start).SelectMany(b => new[] { Price(r, b.Low), Price(r, b.High) })
            : r.Samples.Where(s => LineIndex(s.Seconds) >= range.Start && LineIndex(s.Seconds) < range.End).Select(s => Price(r, s.Close))).ToArray();
        var baseBars = Bars(first);
        double reference = candles.Checked && range.Start < baseBars.Length ? Price(first, baseBars[range.Start].Open)
            : detail ? Price(first, first.Samples[0].Open) : first.OpeningPrice;
        var axis = candles.Checked ? MarketAxis.Fit(values, reference) : MarketAxis.Centered(values, reference);
        using var grid = new Pen(TerminalTheme.Grid);
        for (int i = 0; i <= 6; i++)
        {
            float y = plot.Top + plot.Height * i / 6f; double price = axis.PriceAt(plot, y);
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            TextAt(g, price.ToString("0.00"), new(0, y - 9, 60, 20), TerminalTheme.Text);
            TextAt(g, $"{axis.Percent(price):+0.0;-0.0;0.0}%", new(plot.Right + 4, y - 9, 63, 20), price >= reference ? TerminalTheme.Rise : TerminalTheme.Fall);
        }
        g.DrawRectangle(grid, plot); g.DrawRectangle(grid, energy);
        TextAt(g, $"{(detail ? "柱内分时" : candles.Checked ? "日 K" : "完整分时")} · {range.Start + 1}–{range.End}/{total} · 左侧价格 / 右侧区间涨跌幅", new(plot.Left, 4, plot.Width, 25), TerminalTheme.Text);
        double opening = first.Samples[0].Open;
        string quote = $"开 {Price(first, opening):0.00}  高 {Price(first, first.Samples.Max(s => s.High)):0.00}  低 {Price(first, first.Samples.Min(s => s.Low)):0.00}  成交量 {first.Samples.Sum(s => s.Volume) / 100:0}手  成交额 {first.Samples.Sum(s => s.Amount) / 10000:0.00}万";
        if (candles.Checked) quote = $"开 {Price(first, opening):0.00}  高 {Price(first, first.Samples.Max(s => s.High)):0.00}  低 {Price(first, first.Samples.Min(s => s.Low)):0.00}  收 {Price(first, first.Samples[^1].Close):0.00}";
        TextAt(g, quote, new(plot.Left, 57, plot.Width, 22), TerminalTheme.Text);
        TextAt(g, "能量 · 随保存的音频采样", new(plot.Left, energy.Top - 22, plot.Width, 21), TerminalTheme.Muted);
        float X(int index) => plot.Left + (index - view.Start + .5f) * plot.Width / view.Capacity;
        for (int version = 0; version < selected.Length; version++)
        {
            var r = selected[version]; Color lineColor = !candles.Checked && version == 0 ? Color.White : Colors[version]; using var line = new Pen(lineColor, 1.3f);
            TextAt(g, $"{version + 1}: {r.RecordedAt:MM-dd HH:mm} {r.OpeningPrice:0.00}元", new(plot.Left + version * plot.Width / 5, 32, plot.Width / 5, 23), lineColor);
            var state = g.Save(); g.SetClip(plot);
            if (candles.Checked && version == 0)
            {
                var bars = Bars(r);
                for (int i = range.Start; i < Math.Min(range.End, bars.Length); i++)
                {
                    var b = bars[i]; using var pen = new Pen(b.Close >= b.Open ? TerminalTheme.Rise : TerminalTheme.Fall); using var brush = new SolidBrush(pen.Color);
                    float x = X(i), o = axis.Y(plot, Price(r, b.Open)), c = axis.Y(plot, Price(r, b.Close));
                    g.DrawLine(pen, x, axis.Y(plot, Price(r, b.High)), x, axis.Y(plot, Price(r, b.Low)));
                    float w = Math.Max(1, plot.Width / (float)view.Capacity * .6f), height = Math.Max(1, Math.Abs(o - c));
                    if (b.Close < b.Open) g.FillRectangle(brush, x - w / 2, Math.Min(o, c), w, height);
                    g.DrawRectangle(pen, x - w / 2, Math.Min(o, c), w, height);
                }
            }
            else if (candles.Checked)
            {
                var bars = Bars(r); for (int i = Math.Max(1, range.Start); i < Math.Min(range.End, bars.Length); i++) g.DrawLine(line, X(i - 1), axis.Y(plot, Price(r, bars[i - 1].Close)), X(i), axis.Y(plot, Price(r, bars[i].Close)));
            }
            else
            {
                for (int i = 1; i < r.Samples.Length; i++)
                {
                    var a = r.Samples[i - 1]; var b = r.Samples[i]; int ia = LineIndex(a.Seconds), ib = LineIndex(b.Seconds);
                    if (ib < range.Start || ia >= range.End) continue;
                    if (a.Segment == b.Segment && b.Seconds >= a.Seconds && b.Seconds - a.Seconds <= 1.2) g.DrawLine(line, X(ia), axis.Y(plot, Price(r, a.Close)), X(ib), axis.Y(plot, Price(r, b.Close)));
                }
            }
            g.Restore(state);
            state = g.Save(); g.SetClip(energy);
            if (candles.Checked)
            {
                var levels = r.Samples.GroupBy(s => s.BarIndex).ToDictionary(g => g.Key, g => g.Average(s => s.Energy));
                var rb = Bars(r);
                for (int i = range.Start; i < Math.Min(range.End, rb.Length); i++) g.DrawLine(line, X(i), energy.Bottom, X(i), energy.Bottom - Math.Clamp(levels.GetValueOrDefault(rb[i].Index), 0, 1) * energy.Height);
            }
            else foreach (var sample in r.Samples)
            {
                int i = LineIndex(sample.Seconds); if (i < range.Start || i >= range.End) continue;
                g.DrawLine(line, X(i), energy.Bottom, X(i), energy.Bottom - Math.Clamp(sample.Energy, 0, 1) * energy.Height);
            }
            g.Restore(state);
        }
        for (int tick = 0; tick <= 4; tick++)
        {
            int index = view.Start + (int)((view.Capacity - 1) * tick / 4d);
            string label = "--";
            if (candles.Checked && index >= 0 && index < baseBars.Length) label = $"第{index + 1}根";
            else if (!candles.Checked && index >= 0 && index < total) label = Time(SecondsAt(index));
            TextAt(g, label, new(plot.Left + plot.Width * tick / 4 - 26, energy.Bottom + 4, 55, 22), TerminalTheme.Muted);
        }
        if (pointer is { } mouse && plot.Contains(mouse))
        {
            int index = view.Start + (int)((mouse.X - plot.Left) * view.Capacity / (double)plot.Width);
            using var cross = new Pen(TerminalTheme.Muted) { DashStyle = DashStyle.Dash }; g.DrawLine(cross, mouse.X, plot.Top, mouse.X, energy.Bottom); g.DrawLine(cross, plot.Left, mouse.Y, plot.Right, mouse.Y);
            var sample = candles.Checked ? first.Samples.LastOrDefault(s => s.BarIndex == index) : index >= 0 && index < total ? SongRecordingStore.At(first, SecondsAt(index)) : null;
            if (sample is not null)
            {
                int lyric = LyricPlaybackClock.FindActive(first.Lyrics, sample.Seconds);
                TextAt(g, $"{Time(sample.Seconds)}  价格 {Price(first, sample.Close):0.00}  能量 {sample.Energy:P0}  {(lyric >= 0 ? first.Lyrics[lyric].Text : "")}", new(plot.Left, 57, plot.Width, 22), TerminalTheme.Cyan);
            }
        }
    }
    private static string Time(double seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
    private static void TextAt(Graphics g, string text, RectangleF bounds, Color color)
    {
        using var font = new Font("Microsoft YaHei UI", 8); using var brush = new SolidBrush(color); using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, font, brush, bounds, format);
    }
    private sealed class HistoryCanvas : Panel
    {
        public HistoryCanvas() { DoubleBuffered = true; ResizeRedraw = true; SetStyle(ControlStyles.Selectable, true); TabStop = true; }
    }
}
