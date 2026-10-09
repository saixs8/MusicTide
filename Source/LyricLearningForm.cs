namespace SpectrumKlinePlayer;

public sealed class LyricLearningForm : Form
{
    private readonly CheckBox automatic = new() { Text = "根据听歌记录自动学习", AutoSize = true }, ai = new() { Text = "使用本机 Ollama", AutoSize = true }, apply = new() { Text = "自动应用（保留手动规则）", AutoSize = true };
    private readonly TextBox endpoint = new() { Width = 310 };
    private readonly ComboBox model = new() { Width = 250 };
    private readonly Label status = PlaygroundUi.Label("", 100);
    private readonly DataGridView grid = PlaygroundUi.Grid();
    private readonly LyricAiSettings original;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public LyricAiSettings Settings => original with { Automatic = automatic.Checked, UseAi = ai.Checked, AutoApply = apply.Checked, Endpoint = endpoint.Text.Trim(), Model = model.Text.Trim() };
    public LyricLearningForm(LyricAiSettings settings, Func<string> readStatus, Func<RuleSuggestion[]> proposals,
        Func<LyricAiSettings, Task> learn, Action<RuleSuggestion[]> accept)
    {
        original = settings; PlaygroundUi.Theme(this, "关键词学习 · 本机 Ollama", new(1050, 730));
        MinimumSize = new(1050, 620);
        automatic.Checked = settings.Automatic; ai.Checked = settings.UseAi; apply.Checked = settings.AutoApply;
        endpoint.Text = settings.Endpoint; model.Text = settings.Model;
        var options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new(10) };
        options.Controls.AddRange([automatic, ai, apply]);
        var connection = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, Padding = new(10) };
        connection.Controls.AddRange([new Label { Text = "本机地址", AutoSize = true }, endpoint, new Label { Text = "模型", AutoSize = true }, model]);
        var detect = PlaygroundUi.Button("读取本机模型", () => { });
        detect.Click += async (_, _) =>
        {
            detect.Enabled = false;
            try { var names = await OllamaLyricClient.Models(endpoint.Text); if (IsDisposed) return; model.Items.Clear(); model.Items.AddRange(names); if (names.Length > 0 && !names.Contains(model.Text)) model.SelectedIndex = 0; status.Text = $"找到 {names.Length} 个本地模型"; }
            catch (Exception e) { if (!IsDisposed) status.Text = "连接失败：" + e.Message; }
            finally { if (!IsDisposed) detect.Enabled = true; }
        };
        connection.Controls.Add(detect);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new(10) };
        var run = PlaygroundUi.Button("保存并立即学习", () => { });
        run.Click += async (_, _) =>
        {
            run.Enabled = false; status.Text = "正在学习；歌曲播放与歌词同步继续运行…";
            try { await learn(Settings); if (!IsDisposed) RefreshResults(); }
            catch (Exception e) { if (!IsDisposed) status.Text = "学习失败：" + e.Message; }
            finally { if (!IsDisposed) run.Enabled = true; }
        };
        buttons.Controls.Add(run);
        buttons.Controls.Add(PlaygroundUi.Button("应用选中建议", () => { accept(grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (RuleSuggestion)r.Tag!).ToArray()); status.Text = "已应用选中建议，手动规则保留。"; }));
        buttons.Controls.Add(PlaygroundUi.Button("保存设置", () =>
        {
            try { OllamaLyricClient.LocalEndpoint(endpoint.Text); DialogResult = DialogResult.OK; }
            catch (ArgumentException e) { status.Text = e.Message; }
        }));
        grid.Columns.Add("Keyword", "关键词"); grid.Columns.Add("Effect", "效果"); grid.Columns.Add("Strength", "强度");
        grid.Columns.Add("Emotion", "情绪程度"); grid.Columns.Add("Seconds", "持续秒数");
        grid.Columns.Add("Source", "来源"); grid.Columns.Add("Songs", "歌曲数"); grid.Columns.Add("Reason", "依据");
        grid.Columns["Reason"]!.FillWeight = 250;
        grid.Columns["Reason"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.Columns["Reason"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        Shown += (_, _) => grid.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells);
        grid.ColumnWidthChanged += (_, _) => { if (grid.IsHandleCreated) grid.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells); };
        Controls.Add(grid); Controls.Add(status); Controls.Add(connection); Controls.Add(options); Controls.Add(buttons);
        Controls.Add(PlaygroundUi.Label("本机 AI 根据歌词语境判断情绪程度，再自动生成 2–12 秒，情绪越强持续越久；表中显示程度、秒数及依据。\n先启动 Ollama 并勾选使用本机 Ollama。没有 AI 判断的新词默认 6 秒；已有 AI 时长和手动规则保留。", 85));
        RefreshResults();
        void RefreshResults()
        {
            status.Text = readStatus(); grid.Rows.Clear();
            foreach (var p in proposals()) { var r = p.Rule; int i = grid.Rows.Add(r.Keyword, MusicPlayground.EffectName(r.Effect), r.Strength, LyricRuleLearning.EmotionText(r), r.Seconds, LyricRuleLearning.SourceName(r.Source), r.SongCount, p.Reason); grid.Rows[i].Tag = p; }
            grid.ClearSelection();
        }
    }
}
