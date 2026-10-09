using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SpectrumKlinePlayer;

internal static class OptionalAiInstall
{
    internal static string PendingPath => Path.Combine(AppContext.BaseDirectory, "Settings", "ai-install-pending.json");
    internal static bool Pending => File.Exists(PendingPath);
    internal static bool ShouldAsk(string path)
    {
        if (!File.Exists(path)) return false;
        try { return !JsonSerializer.Deserialize<Preference>(File.ReadAllText(path))!.DontAskAgain; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NullReferenceException) { return true; }
    }
    internal sealed record Preference(bool DontAskAgain = false);

    // Runs before the main window; returning false exits after opening the installer.
    internal static bool Prompt()
    {
        if (!ShouldAsk(PendingPath)) return true;
        using var dialog = new Form { Text = "补装本地 AI", ClientSize = new(490, 220),
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = new("Microsoft YaHei UI", 10) };
        var description = new Label { Text = "当前已完成离线安装，本地 AI 模型尚未安装。\n联网后可补装约 986 MB 模型；现在也可以继续使用。\n未选择“不再询问”时，下次启动会再次提示。", Bounds = new(20, 18, 450, 88) };
        var suppress = new CheckBox { Text = "不再询问", Bounds = new(20, 112, 220, 28) };
        var complete = new Button { Text = "联网补装", Bounds = new(230, 164, 114, 34), DialogResult = DialogResult.OK };
        var later = new Button { Text = "暂不安装", Bounds = new(354, 164, 114, 34), DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange([description, suppress, complete, later]); dialog.CancelButton = later;
        var result = dialog.ShowDialog();
        if (suppress.Checked)
        {
            try { File.WriteAllText(PendingPath, JsonSerializer.Serialize(new Preference(true))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show("无法保存提醒偏好：" + e.Message); }
        }
        if (result != DialogResult.OK) return true;
        string root = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        string setup = Path.Combine(root, "MusicTideSetup.exe");
        if (!File.Exists(setup)) { MessageBox.Show("补装程序未找到，请重新运行音潮行情安装包并选择完整安装。"); return true; }
        try
        {
            var start = new ProcessStartInfo(setup) { UseShellExecute = true };
            start.ArgumentList.Add("--complete-ai"); start.ArgumentList.Add(root);
            Process.Start(start); return false;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show("无法启动补装程序：" + e.Message); return true; }
    }
}
