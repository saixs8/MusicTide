using System.Drawing.Drawing2D;

namespace SpectrumKlinePlayer;

public sealed class SongComparisonForm : Form
{
    private readonly SongRecording[] recordings;
    private readonly ComboBox leftChoice = new(), rightChoice = new();
    private readonly CheckBox progressAlignment = new() { Text = "按歌曲进度对齐", Checked = true, AutoSize = true };
    private readonly TrackBar cursor = new() { Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Dock = DockStyle.Bottom, Height = 40 };
    private readonly ComparisonCanvas canvas = new() { Dock = DockStyle.Fill };
    public SongComparisonForm(SongRecording[] recordings)
    {
        this.recordings = recordings;
        Text = "双歌对比 · 音乐行情"; Size = new(1200, 760); MinimumSize = new(900, 600);
        StartPosition = FormStartPosition.CenterParent; BackColor = TerminalTheme.Background; ForeColor = TerminalTheme.Text;
        Font = new("Microsoft YaHei UI", 9);
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 92, ColumnCount = 2, RowCount = 2, Padding = new(10), BackColor = TerminalTheme.Panel };
        top.ColumnStyles.Add(new(SizeType.Percent, 50)); top.ColumnStyles.Add(new(SizeType.Percent, 50));
        top.RowStyles.Add(new(SizeType.Absolute, 34)); top.RowStyles.Add(new(SizeType.Absolute, 38));
        foreach (var box in new[] { leftChoice, rightChoice })
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList; box.Dock = DockStyle.Fill;
            box.BackColor = TerminalTheme.Input; box.ForeColor = TerminalTheme.Text;
            box.Items.AddRange(recordings.Cast<object>().ToArray());
            box.SelectedIndexChanged += (_, _) => canvas.Invalidate();
        }
        top.Controls.Add(leftChoice, 0, 0); top.Controls.Add(rightChoice, 1, 0);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        options.Controls.Add(progressAlignment);
        var swap = new Button { Text = "交换两首歌", AutoSize = true, Height = 27, BackColor = TerminalTheme.Selected, ForeColor = TerminalTheme.Text };
        swap.Click += (_, _) => { int old = leftChoice.SelectedIndex; leftChoice.SelectedIndex = rightChoice.SelectedIndex; rightChoice.SelectedIndex = old; };
        options.Controls.Add(swap); top.Controls.Add(options, 0, 1);
        top.Controls.Add(new Label { Text = "移动鼠标或拖动底部滑条，联动查看歌词与能量", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = TerminalTheme.Muted }, 1, 1);
        Controls.Add(canvas); Controls.Add(cursor); Controls.Add(top);
        leftChoice.SelectedIndex = 0;
        int different = Array.FindIndex(recordings, 1, r => r.Title != recordings[0].Title);
        rightChoice.SelectedIndex = different >= 1 ? different : 1;
        progressAlignment.CheckedChanged += (_, _) => canvas.Invalidate();
        cursor.ValueChanged += (_, _) => canvas.Invalidate();
        canvas.Render = DrawComparison;
        canvas.MouseMove += (_, e) =>
        {
            int half = canvas.Width / 2, local = e.X >= half ? e.X - half : e.X;
            cursor.Value = Math.Clamp((int)((local - 57) * 1000d / Math.Max(1, half - 84)), 0, 1000);
        };
    }

    private void DrawComparison(Graphics g, Rectangle area)
    {
        if (leftChoice.SelectedIndex < 0 || rightChoice.SelectedIndex < 0) return;
        var left = recordings[leftChoice.SelectedIndex]; var right = recordings[rightChoice.SelectedIndex];
        g.Clear(TerminalTheme.Background); g.SmoothingMode = SmoothingMode.AntiAlias;
        float Range(SongRecording r) => r.Samples.Max(s => Math.Abs(s.Close - r.Samples[0].Close));
        float range = Math.Max(.05f, Math.Max(Range(left), Range(right)) * 1.12f);
        double seconds = Math.Max(left.Duration, right.Duration);
        DrawSong(g, new(8, 6, area.Width / 2 - 12, area.Height - 12), left, range, seconds);
        DrawSong(g, new(area.Width / 2 + 4, 6, area.Width / 2 - 12, area.Height - 12), right, range, seconds);
    }

    private void DrawSong(Graphics g, Rectangle bounds, SongRecording song, float range, double commonSeconds)
    {
        using var grid = new Pen(TerminalTheme.Grid); using var frame = new Pen(TerminalTheme.Border);
        using var line = new Pen(TerminalTheme.Cyan, 1.5f); using var energyPen = new Pen(TerminalTheme.Yellow, 1.2f);
        using var eventBrush = new SolidBrush(TerminalTheme.Rise);
        double axisSeconds = progressAlignment.Checked ? song.Duration : commonSeconds;
        double selectedSeconds = axisSeconds * cursor.Value / 1000;
        float baseline = song.Samples[0].Close;
        g.DrawRectangle(frame, bounds);
        TextAt(g, song.Title, new(bounds.Left + 10, bounds.Top + 6, bounds.Width - 20, 30), TerminalTheme.Text, 13, true);
        double mean = song.Samples.Average(s => s.Energy);
        var peaks = song.Samples.Count(s => s.Energy >= .64);
        TextAt(g, $"时长 {Time(song.Duration)}   平均能量 {mean:P0}   高能量采样 {peaks * 100d / song.Samples.Length:F0}%", new(bounds.Left + 10, bounds.Top + 40, bounds.Width - 20, 25), TerminalTheme.Muted);
        TextAt(g, $"已采集 {Time(song.Samples.Min(s => s.Seconds))}–{Time(song.Samples.Max(s => s.Seconds))} · {song.RecordedAt:MM-dd HH:mm} · {(song.Simulated ? "含模拟数据" : "系统音频")}{(!song.AuthoritativeTime ? " · 估算时间" : "")}",
            new(bounds.Left + 10, bounds.Top + 66, bounds.Width - 20, 25), TerminalTheme.Muted, 8);
        int bottomHeight = 126;
        var plot = new Rectangle(bounds.Left + 49, bounds.Top + 118, bounds.Width - 76, Math.Max(60, (bounds.Height - 118 - bottomHeight - 39) * 2 / 3));
        var energy = new Rectangle(plot.Left, plot.Bottom + 39, plot.Width, Math.Max(40, bounds.Bottom - bottomHeight - plot.Bottom - 39));
        TextAt(g, "行情走势 · 以首个采集点归零 · 同一纵轴比例", new(plot.Left, plot.Top - 24, plot.Width, 22), TerminalTheme.Text, 8);
        for (int i = 0; i <= 4; i++)
        {
            float y = plot.Top + plot.Height * i / 4f;
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            TextAt(g, $"{range * (1 - i / 2f) * 100:F0}%", new(bounds.Left + 3, y - 10, 42, 20), TerminalTheme.Muted, 8);
        }
        for (int i = 0; i <= 4; i++)
        {
            float x = plot.Left + plot.Width * i / 4f;
            g.DrawLine(grid, x, plot.Top, x, plot.Bottom); g.DrawLine(grid, x, energy.Top, x, energy.Bottom);
            TextAt(g, progressAlignment.Checked ? $"{i * 25}%" : Time(axisSeconds * i / 4), new(x - 23, energy.Bottom + 3, 50, 21), TerminalTheme.Muted, 8);
        }
        TextAt(g, "能量 · 红色标记为连板事件", new(energy.Left, energy.Top - 23, energy.Width, 21), TerminalTheme.Yellow, 8);
        g.DrawRectangle(grid, plot); g.DrawRectangle(grid, energy);
        float X(double t) => plot.Left + (float)(t / axisSeconds) * plot.Width;
        float Y(float v) => plot.Top + plot.Height * (.5f - (v - baseline) / (2 * range));
        var state = g.Save(); g.SetClip(new Rectangle(plot.Left, plot.Top, plot.Width, energy.Bottom - plot.Top));
        SongSample? previous = null;
        foreach (var s in song.Samples)
        {
            if (previous is { } p && s.Segment == p.Segment && s.Seconds >= p.Seconds && s.Seconds - p.Seconds < 2)
            {
                g.DrawLine(line, X(p.Seconds), Y(p.Close), X(s.Seconds), Y(s.Close));
                g.DrawLine(energyPen, X(p.Seconds), energy.Bottom - p.Energy * energy.Height, X(s.Seconds), energy.Bottom - s.Energy * energy.Height);
            }
            else { g.DrawEllipse(line, X(s.Seconds) - 1, Y(s.Close) - 1, 2, 2); }
            if (s.Event.Length > 0 && (previous is null || previous.Event != s.Event))
                g.FillPolygon(eventBrush, [new PointF(X(s.Seconds), plot.Top + 2), new PointF(X(s.Seconds) - 3, plot.Top + 9), new PointF(X(s.Seconds) + 3, plot.Top + 9)]);
            previous = s;
        }
        g.Restore(state);
        using var cross = new Pen(TerminalTheme.Text) { DashStyle = DashStyle.Dash };
        float cursorX = X(selectedSeconds); g.DrawLine(cross, cursorX, plot.Top, cursorX, energy.Bottom);
        var current = SongRecordingStore.At(song, selectedSeconds);
        string value = current is null ? "此位置未采集" : $"走势 {(current.Close - baseline) * 100:+0.00;-0.00;0.00}%  能量 {current.Energy:P0}  {current.Event}";
        TextAt(g, $"{Time(selectedSeconds)}  {value}", new(bounds.Left + 10, bounds.Bottom - 94, bounds.Width - 20, 26), TerminalTheme.Cyan, 9);
        int lyric = LyricPlaybackClock.FindActive(song.Lyrics, selectedSeconds);
        TextAt(g, lyric >= 0 ? song.Lyrics[lyric].Text : "此位置暂无歌词", new(bounds.Left + 10, bounds.Bottom - 64, bounds.Width - 20, 38), TerminalTheme.Text, 13, true);
        TextAt(g, "空白为未采集区段；记录保留各次拖动后的采样", new(bounds.Left + 10, bounds.Bottom - 27, bounds.Width - 20, 22), TerminalTheme.Muted, 8);
    }

    private static string Time(double seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
    private static void TextAt(Graphics g, string text, RectangleF bounds, Color color, float size = 9, bool bold = false)
    {
        using var font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, font, brush, bounds, format);
    }
    private sealed class ComparisonCanvas : Panel
    {
        public Action<Graphics, Rectangle>? Render;
        public ComparisonCanvas() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); Render?.Invoke(e.Graphics, ClientRectangle); }
    }
}
