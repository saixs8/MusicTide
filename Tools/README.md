# 开发与验证工具

原 `work/` 已整理为 `Tools/`，相对 `Source/` 的层级不变。所有命令从项目根目录执行；`bin/`、`obj/` 为工具自行生成的构建目录。

## 常规回归与预览

当前发布工具：

- `Installer/BuildSetup.ps1`：素材在 `Build/Installer`，输出最新安装包并清理旧版本。
- `BuildSourcePackage.ps1`：按白名单导出非商业源码包，不含本机数据或模型。
- `SilenceVerification`：18 项无声与离线提醒检查。
- `MultiPlayerVerification`：播放器识别、歌词缓存、时钟与切歌验证。
- `InstallerVerification`：预览与断点探针源码；已生成 EXE 和沙盒在 `Build/Verification/Installer`。
- Sysinternals 本地工具移至 `Runtime/Diagnostics/Sysinternals`，不随应用或源码包发布。

完整构建前提见 [开发指南](../Docs/Development.md)。

| 工具 | 功能 | 已记录结果 |
|---|---|---|
| DayNavigationVerification | 原生鼠标消息、少量 K 线缩放、双向拖动、历史与实时量能独立性 | v0.8.5：38 项 |
| LyricLearningVerification | 5 个默认关键词、已听片段、规则保护、AI 校验与云端阻断 | v0.8.5：52 项 |
| LyricVerification | 时钟、暂停、拖动、切歌、缓存、资讯浏览 | 28 项 |
| IndicatorVerification | 指标数学、参数、选择保存、固定能量位置 | 44 项 |
| MusicFeaturesVerification | 每首歌次数限制、实时量能、连板、炸板、双歌记录和窗口 | v0.8.8：39 项 |
| PlaygroundVerification | 七种玩法、负面盲盒、交易回顾、规则编辑、9 档菜单字体与成就 | v0.8.7：65 项 |
| MarketHistoryVerification | 随机开盘、双侧坐标、热度估值、五版／容量／次数、旧记录与原生双击 | v0.9.0：19 项 |
| UiPreview | 注入演示数据生成主窗口与四指标截图 | 联动检查及六种预览 |

```powershell
dotnet run --project Tools/LyricVerification -c Release
dotnet run --project Tools/IndicatorVerification -c Release
dotnet run --project Tools/MusicFeaturesVerification -c Release
dotnet run --project Tools/PlaygroundVerification -c Release
dotnet run --project Tools/LyricLearningVerification -c Release
dotnet run --project Tools/MarketHistoryVerification -c Release
dotnet run --project Tools/UiPreview -c Release
```

预览输出到 `Docs/UiPreview/`。部分工具显示测试窗口，并在工具自己的 `bin/` 下保存演示设置和记录，与发布版用户数据分开。

## 播放器现场检查

| 工具 | 用途与操作影响 |
|---|---|
| LyricLiveCheck | 显示完整程序并只读观察约 7 秒，保存真实截图和工具自身的歌曲记录 |
| PlaybackTimeProbe | 只读枚举酷狗窗口、无障碍控件及播放时间 |
| LyricDiagnostics | 只读扫描窗口与 OCR；截图保存到 `Docs/Diagnostics/` |
| PlaybackControlsCheck | 会发送播放及音量指令，按实现流程恢复播放和音量 |
| TrackSwitchCheck | 实际切歌检查；`--switch` 会发送下一首／上一首 |

部分探针用于历史现场排查，不作为独立播放器适配方案。`LyricDiagnostics/sqlite_probe.py` 为本地缓存研究探针。

```powershell
dotnet run --project Tools/LyricLiveCheck -c Release
dotnet run --project Tools/PlaybackTimeProbe -c Release
```

现场日志放在 `Docs/Logs/`。歌词回归产生的 `retrysong.lrc` 和 `unrelated.lrc` 为可再生成测试文件。

`StartLocalOllama.ps1 -Pull`：使用已安装或项目 `Runtime/Ollama/ollama.exe` 启动本机服务，默认下载 qwen2.5:1.5b。新服务禁用云端，模型和服务日志保存在项目运行目录。该脚本不下载运行程序本身。

`dotnet run --project Tools/OllamaEmotionProbe -c Release`：调用真实本机模型检查情绪程度与秒数映射，并分析发布目录中的已听片段。模型返回的有效规则写到 `Docs/Diagnostics/Emotion_RealModelSuggestions.json`，不会将歌词发送到远程接口。

加 `-- --apply` 会将有效建议合并到发布版规则设置，保留手动规则；默认只验证并生成日志与预览。
