# 构建、验证与发布

所有命令在项目根目录执行。源码包不包含本机模型、用户数据或安装构建素材。

## 主程序

Windows 10 2004／19041 或更新、x64，安装 .NET 9 SDK：

```powershell
dotnet build Source/SpectrumKlinePlayer.csproj -c Release
dotnet publish Source/SpectrumKlinePlayer.csproj -c Release -o Output-Major
```

首次还原可能需要联网取得 Windows SDK 引用；源代码的主项目没有显式第三方 NuGet 包。`Output-Major` 为框架依赖版，需要 .NET 9 Desktop Runtime，不要只复制 EXE。

## 代码入口

| 文件／模块 | 责任 |
|---|---|
| `Source/Program.cs`、`OptionalAiInstall.cs` | 启动及可关闭的 AI 补装提醒 |
| `AudioLoopbackCapture.cs`、`Form1.cs` | WASAPI 回放采集、FFT、行情帧及无声门控 |
| `Form1.TerminalView.cs`、`Form1.DayNavigation.cs` | 行情布局与日 K 视口 |
| `TechnicalIndicators.cs`、`Form1.Indicators.cs` | 指标计算与选择 |
| `WindowsMediaSessionCapture.cs`、`PlayerWindowCapture.cs` | 媒体会话与窗口读取 |
| `CrossPlayerLyricCapture.cs`、`Form1.MultiPlayerLyrics.cs` | 多播放器歌词与时钟 |
| `OllamaLyricClient.cs`、`Form1.LyricLearning.cs` | 本机情绪判断和规则学习 |
| `Form1.MusicFeatures.cs`、`Form1.Playground.cs` | 连板、历史、对比与玩法 |
| `Tools/Installer/Setup.cs` | 基于 .NET Framework 的安装器 |

保留 `SpectrumKlinePlayer` 命名空间和项目文件名是为了兼容历史工具引用；产品和输出名已统一为 MusicTide。

## 回归

```powershell
dotnet run --project Tools/SilenceVerification -c Release
dotnet run --project Tools/LyricVerification -c Release
dotnet run --project Tools/MultiPlayerVerification -c Release
dotnet run --project Tools/IndicatorVerification -c Release
dotnet run --project Tools/DayNavigationVerification -c Release
```

按改动选择对应测试。`PlaybackControlsCheck` 和 `TrackSwitchCheck` 会操作实际播放器，使用前阅读源码和 [工具说明](../Tools/README.md)。大多数验证采用工具自己的输出目录，真实模型探针还会读取本地发布版已听片段。

## 可选本机 AI

安装官方 Ollama，或把官方独立 Windows ZIP 解压到 `Runtime/Ollama`。下载与本机推理：

```powershell
powershell -File Tools/StartLocalOllama.ps1 -Pull
```

默认使用 `qwen2.5:1.5b`、端口 `11434`。脚本不下载 Ollama 程序本身。已有服务沿用其模型目录；安装器打包需要模型在本项目 `Runtime/Ollama/Models/`，请核对目录，不能仅凭本机全局服务已安装模型就认定打包素材完整。

## 一键安装器

完整安装器的素材准备是独立步骤，不是单纯 `dotnet publish`：

1. 将官方 .NET 9.0.20 **Windows x64 core runtime ZIP** 与 **Windows Desktop Runtime ZIP** 解压、合并到 `Build/Installer/RuntimePayload/Runtime/DotNet/`，其中应包含 `dotnet.exe`、`shared/Microsoft.NETCore.App`、`shared/Microsoft.WindowsDesktop.App` 及原许可。
2. 将官方 Ollama 0.40.1 Windows 独立版放到 `Runtime/Ollama/`，保留 CPU 库、许可及 README；源码包的 `Runtime` 素材不内置。
3. 准备固定 `qwen2.5:1.5b` manifest 与全部模型 blob。模型下载可用上面的脚本；实际构建读取 `Runtime/Ollama/Models/manifests/registry.ollama.ai/library/qwen2.5/1.5b`。
4. 运行构建脚本。安装器通过 Windows 自带的 .NET Framework 4 C# 编译器构建，无需用户先装 .NET 9。

官方素材来源：[.NET 9 发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/9.0/releases.json)、[Ollama 0.40.1 发布页](https://github.com/ollama/ollama/releases/tag/v0.40.1)。核对官方哈希后再使用。

```powershell
powershell -File Tools/Installer/BuildSetup.ps1
```

脚本从 csproj 读取版本号；修改版本时同步 `Setup.cs` 界面版本及文档。`Build/Installer` 保存素材、缓存、ZIP；`Distribution` 只放最新交付物。新安装包成功构建后删除旧版本，生成 `Setup-SHA256.json`。应用负载只包含程序、许可证和声明，不携带开发者 Settings／Data。`-SkipPublish` 仅适用于已确认中间主程序版本一致的情况。

离线安装验证可运行 `MusicTideSetup-v版本.exe --test-offline <项目内绝对测试目录>`；完整安装离线测试使用 `--test-install <测试目录> <模型blob目录>`。这些测试不建立用户快捷方式，也不启动软件。

## 导出干净源码包

```powershell
powershell -File Tools/BuildSourcePackage.ps1
```

白名单包含源码、工具脚本、图标、固定模型 manifest、使用与开发文档、许可证。排除 bin／obj、运行时／模型、安装沙盒、诊断日志／截图、听歌历史／歌词缓存和私人设置。新源码包生成成功后替换旧版本，输出 `Source-SHA256.json`。

发布源码只授权非商业用途；发布前阅读 [LICENSE](../LICENSE) 和 [第三方声明](../THIRD_PARTY_NOTICES.md)。这里完成本地打包，不会自动创建仓库或上传文件。

第三方许可原文也保存在 Licenses，并随源码和安装负载一起分发。历史验证 Markdown 可随源码阅读，现场日志与截图不会进入源码包。
