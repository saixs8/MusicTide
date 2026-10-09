using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

partial class Setup
{
    TextBox pathBox;
    Button browse;
    Label status, detail;
    Panel progressPanel;
    int progressValue;
    bool completed, fixedTarget;
    readonly Color background = Color.FromArgb(20, 25, 34);
    readonly Color cardColor = Color.FromArgb(30, 38, 50);
    readonly Color accent = Color.FromArgb(35, 213, 192);
    readonly Color muted = Color.FromArgb(156, 174, 193);

    void BuildInterface(bool locked)
    {
        fixedTarget = locked;
        Text = "音潮行情 · 安装向导 v0.9.11";
        ClientSize = new Size(760, 620); FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        BackColor = background; ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        using (var iconStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            if (iconStream != null) Icon = new Icon(iconStream);
        Image logo = null;
        using (var logoStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.png"))
            if (logoStream != null) using (var image = Image.FromStream(logoStream)) logo = new Bitmap(image);
        Controls.Add(new PictureBox { Bounds = new Rectangle(28, 27, 50, 50), Image = logo, SizeMode = PictureBoxSizeMode.Zoom });
        Disposed += delegate { if (logo != null) logo.Dispose(); };
        Label title = TextLabel("音潮行情  MusicTide", 96, 24, 630, 36, Color.White, 22, true);
        Controls.Add(title);
        Controls.Add(TextLabel("让音乐成为行情", 98, 68, 600, 26, accent, 11, false));
        Controls.Add(TextLabel("01  选择组件     →     02  安装与下载     →     03  开始听歌", 30, 108, 690, 28, muted, 10, false));
        Panel options = new Panel { Bounds = new Rectangle(28, 149, 704, 124), BackColor = cardColor };
        Controls.Add(options);
        fullChoice.Dock = offlineChoice.Dock = DockStyle.None;
        fullChoice.Bounds = new Rectangle(16, 12, 667, 28);
        offlineChoice.Bounds = new Rectangle(16, 70, 667, 28);
        fullChoice.Text = "完整安装   ·   行情 + 歌词 + 本机 AI";
        offlineChoice.Text = "离线安装   ·   先使用行情和歌词";
        options.Controls.Add(fullChoice); options.Controls.Add(offlineChoice);
        fullChoice.TabIndex = 0; offlineChoice.TabIndex = 1; fullChoice.Checked = true;
        options.Controls.Add(TextLabel("额外下载约 986 MB 模型，支持断点续传；分析留在本机", 38, 40, 650, 22, muted, 9, false));
        options.Controls.Add(TextLabel("无需下载模型，联网后可补装；启动提醒可以关闭", 38, 98, 650, 22, muted, 9, false));
        Controls.Add(TextLabel(locked ? "安装位置  ·  补装沿用当前目录" : "安装位置", 28, 290, 704, 24, Color.White, 10, true));
        pathBox = new TextBox { Bounds = new Rectangle(28, 321, 590, 30), Text = root,
            BackColor = cardColor, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, ReadOnly = locked };
        browse = new Button { Text = "浏览…", Bounds = new Rectangle(628, 319, 104, 34), Enabled = !locked };
        StyleButton(browse, cardColor, Color.White);
        Controls.Add(pathBox); Controls.Add(browse);
        browse.Click += delegate {
            using (var picker = new FolderBrowserDialog { Description = "选择音潮行情的安装目录（其下将创建 App 和 Runtime）", SelectedPath = pathBox.Text })
                if (picker.ShowDialog(this) == DialogResult.OK) pathBox.Text = picker.SelectedPath;
        };
        Controls.Add(TextLabel("所选目录下创建 App 和 Runtime；原有设置与历史会保留。", 28, 357, 704, 23, muted, 9, false));
        status = TextLabel("准备就绪", 28, 393, 600, 26, Color.White, 11, true); Controls.Add(status);
        progressPanel = new Panel { Bounds = new Rectangle(28, 428, 704, 10), BackColor = cardColor };
        progressPanel.Paint += delegate(object sender, PaintEventArgs e) {
            using (var brush = new SolidBrush(accent)) e.Graphics.FillRectangle(brush, 0, 0, progressPanel.Width * progressValue / 100, progressPanel.Height);
        }; Controls.Add(progressPanel);
        detail = TextLabel("基础组件均已内置，选择安装方式后开始。", 28, 447, 704, 26, muted, 9, false); Controls.Add(detail);
        log.Dock = DockStyle.None; log.Bounds = new Rectangle(28, 479, 704, 62);
        log.BackColor = cardColor; log.ForeColor = muted; log.BorderStyle = BorderStyle.None; log.Visible = false; Controls.Add(log);
        var logs = new LinkLabel { Text = "查看安装日志", Bounds = new Rectangle(28, 565, 200, 28), LinkColor = muted, ActiveLinkColor = accent };
        logs.LinkClicked += delegate { log.Visible = !log.Visible; logs.Text = log.Visible ? "收起安装日志" : "查看安装日志"; }; Controls.Add(logs);
        install.Dock = DockStyle.None; install.Bounds = new Rectangle(498, 552, 234, 44); install.Text = "开始完整安装";
        StyleButton(install, accent, background); Controls.Add(install);
        ActiveControl = install;
        Controls.Add(TextLabel("源码公开 · 未经授权禁止商用", 28, 593, 450, 20, muted, 8, false));
    }
    static Label TextLabel(string text, int x, int y, int w, int h, Color color, float size, bool bold)
    { return new Label { Text = text, Bounds = new Rectangle(x,y,w,h), ForeColor = color, Font = new Font("Microsoft YaHei UI",size,bold ? FontStyle.Bold : FontStyle.Regular) }; }
    static void StyleButton(Button button, Color back, Color fore)
    { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.BackColor = back; button.ForeColor = fore; button.Cursor = Cursors.Hand; }
    static string ValidateInstallPath(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !Path.IsPathRooted(input.Trim()) || Path.GetPathRoot(input.Trim()).Length <= 2) throw new IOException("请选择完整的安装目录，例如 D:\\MusicTide。");
        string path = Path.GetFullPath(input.Trim()).TrimEnd(Path.DirectorySeparatorChar);
        if (path.Length <= 3 || string.Equals(path, Path.GetPathRoot(path).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new IOException("请使用独立文件夹，不要安装到磁盘根目录。");
        if (File.Exists(path)) throw new IOException("所选位置是文件，请选择文件夹。");
        Directory.CreateDirectory(path);
        string probe = Path.Combine(path, ".musictide-write-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { } File.Delete(probe);
        return path;
    }
    void ReportStatus(string text)
    {
        log.AppendText(text + "\r\n");
        if (completed) return;
        detail.Text = text.Replace("\r\n", "  ");
        if (text.StartsWith("1/4")) { status.Text = "正在安装基础组件"; progressValue = 5; }
        else if (text.StartsWith("2/4")) { status.Text = "准备本地 AI 模型"; progressValue = 20; }
        else if (text.StartsWith("3/4")) { status.Text = "配置本机 AI"; progressValue = 90; }
        else if (text.StartsWith("4/4")) { status.Text = "验证运行环境"; progressValue = 96; }
        else if (text.StartsWith("安装未完成")) { status.Text = "安装暂未完成，可重试"; log.Visible = true; }
        progressPanel.Invalidate();
    }
    void DownloadProgress(long received, long total, double speed)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(delegate {
            if (completed || IsDisposed) return;
            double fraction = total > 0 ? Math.Min(1, (double)received / total) : 0;
            progressValue = 20 + (int)(fraction * 65);
            status.Text = "正在下载本地 AI 模型  " + (fraction * 100).ToString("0.0") + "%";
            detail.Text = (received / 1048576.0).ToString("0.0") + " / " + (total > 0 ? (total / 1048576.0).ToString("0.0") : "?") + " MB    ·    " + speed.ToString("0.0") + " MB/s    ·    支持断点续传";
            progressPanel.Invalidate();
        }));
    }
}
