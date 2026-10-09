namespace SpectrumKlinePlayer;

internal static class PlaygroundUi
{
    public static void Theme(Form form, string title, Size size)
    {
        form.Text = title; form.Size = size; form.MinimumSize = new(760, 520);
        form.StartPosition = FormStartPosition.CenterParent; form.BackColor = TerminalTheme.Background;
        form.ForeColor = TerminalTheme.Text; form.Font = new("Microsoft YaHei UI", 11);
    }
    public static Button Button(string text, Action click)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 38, Padding = new(8, 3, 8, 3), BackColor = TerminalTheme.Selected, ForeColor = TerminalTheme.MenuText };
        button.Click += (_, _) => click(); return button;
    }
    public static DataGridView Grid(bool editable = false) => new()
    {
        Dock = DockStyle.Fill, BackgroundColor = TerminalTheme.Background, BorderStyle = BorderStyle.None,
        ReadOnly = !editable, AllowUserToAddRows = editable, AllowUserToDeleteRows = editable,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false,
        EnableHeadersVisualStyles = false, ColumnHeadersHeight = 38,
        ColumnHeadersDefaultCellStyle = new() { BackColor = TerminalTheme.Toolbar, ForeColor = TerminalTheme.MenuText, Font = new("Microsoft YaHei UI", 11, FontStyle.Bold) },
        DefaultCellStyle = new() { BackColor = TerminalTheme.Panel, ForeColor = TerminalTheme.Text, SelectionBackColor = TerminalTheme.Selected, SelectionForeColor = TerminalTheme.MenuText, Font = new("Microsoft YaHei UI", 11) },
        RowTemplate = { Height = 36 }, GridColor = TerminalTheme.Border,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    public static Label Label(string text, int height = 65) => new() { Text = text, Dock = DockStyle.Top, Height = height, Padding = new(12, 6, 12, 6), ForeColor = TerminalTheme.Text, BackColor = TerminalTheme.Panel };
    public static Form ShowRows(Form owner, string title, object rows, string note)
    {
        var form = new Form(); Theme(form, title, new(1150, 670));
        var grid = Grid(); grid.DataSource = rows; form.Controls.Add(grid); form.Controls.Add(Label(note, 85)); form.Show(owner); return form;
    }
}

public sealed class SongArenaForm : Form
{
    private readonly ComboBox left = new(), right = new();
    private readonly DataGridView score = PlaygroundUi.Grid();
    private readonly Label outcome = PlaygroundUi.Label("", 105);
    private readonly SongRecording[] records;
    public SongArenaForm(SongRecording[] recordings)
    {
        records = recordings; PlaygroundUi.Theme(this, "歌曲擂台 · 音乐对战", new(1080, 660));
        var picks = new TableLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new(10), ColumnCount = 2 };
        picks.ColumnStyles.Add(new(SizeType.Percent, 50)); picks.ColumnStyles.Add(new(SizeType.Percent, 50));
        foreach (var box in new[] { left, right })
        {
            box.Dock = DockStyle.Fill; box.DropDownStyle = ComboBoxStyle.DropDownList; box.Items.AddRange(records.Cast<object>().ToArray());
            box.SelectedIndexChanged += (_, _) => UpdateScores();
        }
        picks.Controls.Add(left); picks.Controls.Add(right);
        Controls.Add(score); Controls.Add(outcome); Controls.Add(picks);
        Controls.Add(PlaygroundUi.Label("评分规则：爆发力 40% + 能量变化 30% + 连板表现 30%。依据已采集片段评分，未采集部分不补造。\n连板表现包含随机玩法结果；分数会受到玩法开关、采集区间及 K 线周期影响。", 85));
        left.SelectedIndex = 0; right.SelectedIndex = Math.Max(1, Array.FindIndex(records, 1, r => r.Title != records[0].Title));
    }
    private void UpdateScores()
    {
        if (left.SelectedIndex < 0 || right.SelectedIndex < 0) return;
        var a = records[left.SelectedIndex]; var b = records[right.SelectedIndex];
        var sa = ArenaScore.Calculate(a); var sb = ArenaScore.Calculate(b);
        score.DataSource = new[]
        {
            new { 项目 = "高潮爆发力", 左侧歌曲 = sa.Burst.ToString("F1"), 右侧歌曲 = sb.Burst.ToString("F1"), 规则 = "峰值能量 ×100" },
            new { 项目 = "节奏变化", 左侧歌曲 = sa.Rhythm.ToString("F1"), 右侧歌曲 = sb.Rhythm.ToString("F1"), 规则 = "连续能量采样差的均值 ×500，上限100" },
            new { 项目 = "连板表现", 左侧歌曲 = sa.Boards.ToString("F1"), 右侧歌曲 = sb.Boards.ToString("F1"), 规则 = "最高连板序号 ×20，上限100" },
            new { 项目 = "综合分", 左侧歌曲 = sa.Total.ToString("F1"), 右侧歌曲 = sb.Total.ToString("F1"), 规则 = "40% / 30% / 30%" }
        };
        string winner = a.Id == b.Id ? "选择了同一份记录" : Math.Abs(sa.Total - sb.Total) < .05 ? "本轮平局" : "本轮胜出：" + (sa.Total > sb.Total ? a.Title : b.Title);
        outcome.Text = $"{winner}\n左：{a.Title} · {a.Samples.Length} 点{(a.Simulated ? " · 含模拟" : "")}\n右：{b.Title} · {b.Samples.Length} 点{(b.Simulated ? " · 含模拟" : "")}";
    }
}

public sealed class PaperTradingForm : Form
{
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 250 };
    private readonly PaperTrading account;
    private readonly Func<(string Song, double Price, double Seconds, bool Enabled)> quote;
    private readonly Label balance = PlaygroundUi.Label("", 106);
    private readonly DataGridView journal = PlaygroundUi.Grid();
    private readonly List<Button> orders = new();
    private long lastCount = -1;
    public PaperTradingForm(PaperTrading account, Func<(string, double, double, bool)> quote, Func<bool, double, bool> order, Action reset, Func<TradeRound[]> history)
    {
        this.account = account; this.quote = quote;
        PlaygroundUi.Theme(this, "虚拟打板挑战 · 操作回顾", new(1100, 720));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60, Padding = new(9), WrapContents = false };
        foreach (double fraction in new[] { .25, .5, 1 })
        {
            var button = PlaygroundUi.Button($"买入 {fraction:P0}", () => { if (!order(true, fraction)) MessageBox.Show(this, "当前不能成交：请等待歌曲播放，或检查可用虚拟资金。"); UpdateAccount(); });
            orders.Add(button); buttons.Controls.Add(button);
        }
        var sell = PlaygroundUi.Button("全部卖出", () => { if (!order(false, 1)) MessageBox.Show(this, "当前没有持仓或歌曲未在播放。"); UpdateAccount(); });
        orders.Add(sell); buttons.Controls.Add(sell);
        buttons.Controls.Add(PlaygroundUi.Button("新一局", () => { reset(); lastCount = -1; UpdateAccount(); }));
        buttons.Controls.Add(PlaygroundUi.Button("历史战绩", () => ShowTradeHistory(history())));
        Controls.Add(journal); Controls.Add(buttons); Controls.Add(balance);
        Controls.Add(PlaygroundUi.Label("每首歌曲初始虚拟资金 100,000；买入比例按可用现金计算。按当前音乐指数即时成交，无手续费与封板排队。\n切歌自动结束本局并开新局；下表保存每笔操作对应的播放时间、价格和歌词。", 85));
        refresh.Tick += (_, _) => UpdateAccount(); refresh.Start(); FormClosed += (_, _) => { refresh.Stop(); refresh.Dispose(); };
        UpdateAccount();
    }
    private void UpdateAccount()
    {
        var q = quote(); double equity = account.Equity(q.Price);
        balance.Text = $"{q.Song}   {(int)q.Seconds / 60:00}:{(int)q.Seconds % 60:00}   音乐指数 {q.Price:F2}\n现金 {account.Cash:N2}   持仓 {account.Shares:N0}   成本 {account.Cost:F2}\n总权益 {equity:N2}   本局收益 {(equity / PaperTrading.StartingCash - 1):P2}";
        foreach (var button in orders) button.Enabled = q.Enabled;
        if (lastCount == account.Revision) return;
        lastCount = account.Revision;
        journal.DataSource = account.Fills.AsEnumerable().Reverse().Select(f => new { 时间 = $"{(int)f.Seconds / 60:00}:{(int)f.Seconds % 60:00}", 操作 = f.Side, 数量 = f.Quantity, 成交指数 = f.Price.ToString("F2"), 当时权益 = f.Equity.ToString("F2"), 歌词 = f.Lyric }).ToArray();
    }
    private void ShowTradeHistory(TradeRound[] rounds)
    {
        var records = rounds.Reverse().ToArray();
        var window = PlaygroundUi.ShowRows(this, "虚拟打板 · 历史战绩",
            records.Select(r => new { 歌曲 = r.Song, 开始 = r.StartedAt.ToString("MM-dd HH:mm"), 结束权益 = r.FinalEquity.ToString("F2"), 收益率 = (r.FinalEquity / PaperTrading.StartingCash - 1).ToString("P2"), 成交次数 = r.Fills.Length }).ToArray(),
            "切歌、退出或新一局时保存战绩。未卖出的持仓按结束时的音乐指数估值。\n双击一行，查看该局每笔操作与当时歌词。");
        var grid = window.Controls.OfType<DataGridView>().Single();
        foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= records.Length) return;
            var r = records[e.RowIndex];
            PlaygroundUi.ShowRows(window, "操作回顾 · " + r.Song,
                r.Fills.Select(f => new { 秒数 = f.Seconds.ToString("F1"), 操作 = f.Side, 数量 = f.Quantity, 指数 = f.Price.ToString("F2"), 权益 = f.Equity.ToString("F2"), 歌词 = f.Lyric }).ToArray(),
                $"{r.Song} · 结束权益 {r.FinalEquity:N2} · 收益 {(r.FinalEquity / PaperTrading.StartingCash - 1):P2}");
        };
    }
}

public sealed class LyricRulesForm : Form
{
    private readonly DataGridView grid = PlaygroundUi.Grid(true);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public LyricRule[] Rules { get; private set; }
    public LyricRulesForm(LyricRule[] rules)
    {
        Rules = rules; PlaygroundUi.Theme(this, "歌词事件牌 · 编辑关键词", new(900, 630));
        grid.Columns.Add("Keyword", "歌词关键词");
        grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Effect", HeaderText = "触发效果", DataSource = Enum.GetValues<LyricEffect>().Select(MusicPlayground.EffectName).ToArray(), FlatStyle = FlatStyle.Flat });
        grid.Columns.Add("Seconds", "持续秒数（1–30）");
        grid.Columns.Add("Strength", "强度（0.3–2）");
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "来源 / 歌曲数", ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Emotion", HeaderText = "AI 情绪程度", ReadOnly = true });
        foreach (var rule in rules) AddRule(rule);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, Padding = new(10) };
        buttons.Controls.Add(PlaygroundUi.Button("保存规则", Save));
        buttons.Controls.Add(PlaygroundUi.Button("恢复默认", () => { grid.Rows.Clear(); foreach (var r in MusicPlayground.DefaultRules) AddRule(r); }));
        grid.DataError += (_, e) => e.ThrowException = false;
        Controls.Add(grid); Controls.Add(buttons);
        Controls.Add(PlaygroundUi.Label("在歌词中出现关键词时触发事件，同一句只触发一次；多条匹配按从上到下的第一条执行。\n新增请填写底部空行；选中行后按 Delete 删除。规则保存后，在“音乐玩法”勾选“歌词事件牌”生效。", 85));
    }
    private void AddRule(LyricRule rule)
    {
        int index = grid.Rows.Add(rule.Keyword, MusicPlayground.EffectName(rule.Effect), rule.Seconds, rule.Strength, $"{LyricRuleLearning.SourceName(rule.Source)} / {rule.SongCount}", LyricRuleLearning.EmotionText(rule));
        grid.Rows[index].Tag = rule;
    }
    private void Save()
    {
        grid.EndEdit(); var rules = new List<LyricRule>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            string keyword = Convert.ToString(row.Cells[0].Value)?.Trim() ?? "";
            string effect = Convert.ToString(row.Cells[1].Value) ?? "";
            var choices = Enum.GetValues<LyricEffect>(); int i = Array.FindIndex(choices, e => MusicPlayground.EffectName(e) == effect);
            if (keyword.Length is 0 or > 30 || i < 0 || !double.TryParse(Convert.ToString(row.Cells[2].Value), out double seconds) || seconds is < 1 or > 30 || !double.IsFinite(seconds))
            { MessageBox.Show(this, $"第 {row.Index + 1} 行请填写 1–30 字关键词、触发效果和 1–30 秒时长。"); return; }
            double strength = row.Cells[3].Value is null ? 1 : double.TryParse(Convert.ToString(row.Cells[3].Value), out var parsed) ? parsed : double.NaN;
            if (!double.IsFinite(strength) || strength is < .3 or > 2) { MessageBox.Show(this, "强度需在 0.3–2 之间。"); return; }
            var original = row.Tag as LyricRule;
            rules.Add(original is not null && original.Keyword == keyword && original.Effect == choices[i] && original.Seconds == seconds && original.Strength == strength
                ? original : new(keyword, choices[i], seconds, strength));
        }
        if (rules.Count > LyricRuleLearning.MaximumRules) { MessageBox.Show(this, "最多保存 200 条规则。"); return; }
        Rules = rules.ToArray(); DialogResult = DialogResult.OK;
    }
}
