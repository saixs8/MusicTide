using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class Setup : Form
{
    const string Model = "qwen2.5:1.5b";
    Label info = new Label { Dock = DockStyle.Top, Height = 70 };
    TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    Button install = new Button { Text = "一键安装全部组件", Dock = DockStyle.Bottom, Height = 46 };
    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MusicTide");
    bool busy;
    bool offlineMode;
    RadioButton fullChoice = new RadioButton { Text = "完整安装（下载本地 AI 模型，约 986 MB）", Dock = DockStyle.Top, Height = 30, Checked = true };
    RadioButton offlineChoice = new RadioButton { Text = "离线安装（先使用行情和歌词，AI 联网后补装）", Dock = DockStyle.Top, Height = 30 };
    readonly string sourceBlobs;
    Action<string> report;

    [STAThread] static int Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length >= 2 && args[0] == "--complete-ai") { Application.Run(new Setup(args[1], null)); return 0; }
        if (args.Length >= 2 && args[0] == "--test-offline")
        {
            try { var setup = new Setup(args[1], null); setup.offlineMode = true; setup.report = Console.WriteLine; setup.Install(false); return 0; }
            catch (Exception e) { File.WriteAllText(Path.Combine(args[1], "setup-test-error.txt"), e.ToString()); return 1; }
        }
        if (args.Length >= 3 && args[0] == "--test-install")
        {
            // Explicit test target and offline blob source, no user shortcuts or app launch.
            try { var setup = new Setup(args[1], args[2]); setup.report = Console.WriteLine; setup.Install(false); return 0; }
            catch (Exception e) { File.WriteAllText(Path.Combine(args[1], "setup-test-error.txt"), e.ToString()); return 1; }
        }
        Application.Run(new Setup(null, null)); return 0;
    }

    Setup(string target, string blobs)
    {
        if (target != null) root = Path.GetFullPath(target);
        sourceBlobs = blobs;
        Text = "音潮行情 · 一键安装 v0.9.10";
        Size = new Size(650, 420); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10); MinimumSize = Size;
        info.Text = "主程序、独立 .NET 和 Ollama 引擎均已内置，无需联网。\r\n完整安装另下载约 986 MB 模型；离线安装跳过下载。无需管理员权限。\r\n位置：" + root;
        Controls.Add(log); Controls.Add(offlineChoice); Controls.Add(fullChoice); Controls.Add(info); Controls.Add(install);
        offlineChoice.CheckedChanged += delegate { if (!busy) install.Text = offlineChoice.Checked ? "一键离线安装" : "一键安装全部组件"; };
        report = delegate(string text) { if (!IsDisposed) BeginInvoke(new Action(delegate { log.AppendText(text + "\r\n"); })); };
        install.Click += async delegate
        {
            offlineMode = offlineChoice.Checked;
            busy = true; install.Enabled = false; fullChoice.Enabled = offlineChoice.Enabled = false;
            try
            {
                await Task.Run(delegate { Install(true); });
                report(offlineMode ? "离线安装完成。AI 模型可在联网后补装。" : "安装完成。AI 在本机运行，首次加载模型可能需要一点时间。");
                install.Text = "安装完成";
                Process.Start(new ProcessStartInfo(Path.Combine(root, "App", "MusicTide.exe")) { WorkingDirectory = Path.Combine(root, "App"), UseShellExecute = true });
            }
            catch (Exception e) { report("安装未完成：" + e.Message + "\r\n保留下载进度，点击按钮可重试。"); install.Enabled = true; }
            finally { busy = false; fullChoice.Enabled = offlineChoice.Enabled = true; }
        };
        FormClosing += delegate(object sender, FormClosingEventArgs e)
        { if (busy) { e.Cancel = true; MessageBox.Show(this, "安装正在进行，请等待完成或失败后再关闭。"); } };
    }

    void Install(bool shortcuts)
    {
        if (!Environment.Is64BitOperatingSystem || Environment.OSVersion.Version.Build < 19041)
            throw new Exception("需要 Windows 10 2004／19041 或更新版本、64 位 Intel／AMD 电脑。");
        Directory.CreateDirectory(root);
        string app = Path.Combine(root, "App");
        foreach (var p in Process.GetProcessesByName("MusicTide"))
        {
            using (p)
            {
                try
                {
                    if (!string.Equals(p.MainModule.FileName, Path.Combine(app, "MusicTide.exe"), StringComparison.OrdinalIgnoreCase)) continue;
                    p.CloseMainWindow();
                    if (!p.WaitForExit(7000)) throw new IOException("请关闭安装目录中的音潮行情后重试。");
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        report("1/4 解压主程序和独立运行环境…");
        Extract("app.zip", app);
        Extract("runtime.zip", root);
        if (!offlineMode && sourceBlobs == null && !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
        { offlineMode = true; report("没有可用网络，先完成离线安装。"); }
        string runtime = Path.Combine(root, "Runtime", "Ollama");
        string models = Path.Combine(runtime, "Models");
        report("2/4 下载并校验本地 AI 模型…");
        var manifest = new JavaScriptSerializer().DeserializeObject(ReadResource("model.json")) as Dictionary<string, object>;
        var digests = new List<string>();
        digests.Add((string)((Dictionary<string, object>)manifest["config"])["digest"]);
        foreach (Dictionary<string, object> layer in (object[])manifest["layers"]) digests.Add((string)layer["digest"]);
        string blobs = Path.Combine(models, "blobs"); Directory.CreateDirectory(blobs);
        bool modelReady = true;
        try { foreach (string digest in digests)
        {
            string hex = digest.Substring(7);
            string dest = Path.Combine(blobs, "sha256-" + hex);
            if (File.Exists(dest) && Hash(dest) == hex) { report("模型文件已存在并通过校验：" + hex.Substring(0, 12)); continue; }
            if (offlineMode) { modelReady = false; continue; }
            if (sourceBlobs != null)
            {
                File.Copy(Path.Combine(sourceBlobs, "sha256-" + hex), dest, true);
                if (Hash(dest) != hex) throw new IOException("离线测试模型校验失败");
            }
            else Download("https://registry.ollama.ai/v2/library/qwen2.5/blobs/" + digest, dest, hex);
        } }
        catch (Exception e)
        {
            if (!(e is WebException) && !(e is IOException)) throw;
            modelReady = false; offlineMode = true;
            report("模型下载未完成：" + e.Message + "。先完成离线安装，下载进度保留，联网后可以补装。");
        }
        string manifestPath = Path.Combine(models, "manifests", "registry.ollama.ai", "library", "qwen2.5", "1.5b");
        if (modelReady) {
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
            File.WriteAllText(manifestPath, ReadResource("model.json"), new UTF8Encoding(false));
        }
        report("3/4 设置本机 AI（保留已有用户设置和歌曲历史）…");
        string settings = Path.Combine(app, "Settings"); Directory.CreateDirectory(settings);
        string pending = Path.Combine(settings, "ai-install-pending.json");
        if (!modelReady && !File.Exists(pending)) File.WriteAllText(pending, "{\"DontAskAgain\":false}", new UTF8Encoding(false));
        if (modelReady && File.Exists(pending)) File.Delete(pending);
        string localSetup = Path.Combine(root, "MusicTideSetup.exe");
        string currentSetup = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(Path.GetFullPath(currentSetup), Path.GetFullPath(localSetup), StringComparison.OrdinalIgnoreCase)) File.Copy(currentSetup, localSetup, true);
        string ai = Path.Combine(settings, "lyric-ai.json");
        if (!File.Exists(ai)) File.WriteAllText(ai, "{\"Automatic\":true,\"UseAi\":true,\"AutoApply\":true,\"Endpoint\":\"http://127.0.0.1:11534\",\"Model\":\"qwen2.5:1.5b\",\"Suppressed\":[]}", new UTF8Encoding(false));
        report("4/4 验证运行环境并建立快捷方式…");
        VerifyProcess(Path.Combine(root, "Runtime", "DotNet", "dotnet.exe"), "--list-runtimes");
        if (!File.Exists(Path.Combine(runtime, "ollama.exe"))) throw new IOException("Ollama 文件缺失。");
        if (shortcuts)
        {
            MakeShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "音潮行情.lnk"), app);
            string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "MusicTide");
            Directory.CreateDirectory(menu); MakeShortcut(Path.Combine(menu, "音潮行情.lnk"), app);
        }
        File.WriteAllText(Path.Combine(root, "installation.txt"), "MusicTide 0.9.10\r\nPrivate .NET 9.0.20\r\nOllama CPU\r\nModel " + Model + "\r\n" + DateTime.Now.ToString("s"));
        report("完成：" + Path.Combine(app, "MusicTide.exe"));
    }

    void Extract(string resource, string destination)
    {
        Directory.CreateDirectory(destination);
        string prefix = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        foreach (var entry in zip.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("安装资源包含无效路径。");
            if (entry.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
            if (File.Exists(target) && new FileInfo(target).Length == entry.Length)
            {
                using (var original = entry.Open()) using (var sha = SHA256.Create())
                {
                    string digest = BitConverter.ToString(sha.ComputeHash(original)).Replace("-", "").ToLowerInvariant();
                    if (Hash(target) == digest) continue;
                }
            }
            // Payloads contain only binaries and licenses, never personal settings/data.
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (var input = entry.Open()) using (var output = File.Create(target)) input.CopyTo(output);
        }
    }

    void Download(string url, string target, string hash)
    {
        string partial = target + ".download";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (File.Exists(partial) && Hash(partial) == hash)
                {
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(partial, target); return;
                }
                long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 30000; request.ReadWriteTimeout = 30000;
                request.UserAgent = "MusicTideSetup/0.9.10";
                if (offset > 0) request.AddRange(offset);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    bool resumed = response.StatusCode == HttpStatusCode.PartialContent;
                    if (resumed && !response.Headers["Content-Range"].StartsWith("bytes " + offset + "-")) throw new IOException("下载续传位置不匹配。");
                    if (!resumed) offset = 0;
                    long total = offset + response.ContentLength;
                    using (var input = response.GetResponseStream())
                    using (var output = new FileStream(partial, resumed ? FileMode.Append : FileMode.Create))
                    {
                        byte[] buffer = new byte[262144]; int count; long received = offset; long next = offset;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, count); received += count;
                            if (received >= next) { report("模型下载 " + (received / 1048576) + " / " + (total > 0 ? (total / 1048576).ToString() : "?") + " MB"); next = received + 8388608; }
                        }
                    }
                }
                if (Hash(partial) != hash) { File.Delete(partial); throw new IOException("模型 SHA256 校验失败。"); }
                if (File.Exists(target)) File.Delete(target);
                File.Move(partial, target); return;
            }
            catch (Exception e)
            {
                var web = e as WebException;
                var response = web == null ? null : web.Response as HttpWebResponse;
                if (response != null && (int)response.StatusCode == 416 && File.Exists(partial)) File.Delete(partial);
                report("下载重试 " + (attempt + 1) + "/3：" + e.Message); if (attempt == 2) throw; Thread.Sleep(1000);
            }
        }
    }

    static string Hash(string path)
    { using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
    static string ReadResource(string name)
    { using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) using (var reader = new StreamReader(stream)) return reader.ReadToEnd(); }
    static void VerifyProcess(string exe, string args)
    {
        using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true }))
        { if (!p.WaitForExit(20000) || p.ExitCode != 0) throw new IOException("运行环境检查失败。"); }
    }
    static void MakeShortcut(string path, string app)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic link = shell.CreateShortcut(path); link.TargetPath = Path.Combine(app, "MusicTide.exe");
        link.WorkingDirectory = app; link.IconLocation = link.TargetPath; link.Description = "音潮行情 · 音乐 K 线"; link.Save();
    }
}
