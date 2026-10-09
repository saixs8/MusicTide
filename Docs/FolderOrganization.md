# 目录整理记录

2026-10-08 整理，程序保持 v0.6.0。本次修改文档与目录组织。

| 目录 | 内容 |
|---|---|
| Source | 当前源码、项目及标准 bin／obj 构建目录 |
| Output-Major | 原发布程序、依赖、用户设置与数据 |
| Tools | 回归、预览及现场诊断项目 |
| Docs | 功能文档、截图、日志及诊断资料 |
| Archive | 历史临时工程和构建缓存 |

## 本次归整

- `work/` → `Tools/`，同步更新命令及代码相对路径；项目引用仍为 `../../Source/SpectrumKlinePlayer.csproj`。
- `Docs/*.log` → `Docs/Logs/`。
- 工具内 `HiddenLyric.png`、`Minimized.png`、`PlayerRendered.png` → `Docs/Diagnostics/`；后续歌词诊断截图也输出到该目录。
- `.dotnet_home`、`.obj_publish`、`.obj_publish_fd`、`Source/.obj_publish_fix` → `Archive/20261008-Cleanup/BuildScratch/`。
- `.lyric_check` → `Archive/20261008-Cleanup/LegacyLyricCheck/`；现行歌词回归为 `Tools/LyricVerification`。
- 两份生成测试 LRC → `Archive/20261008-Cleanup/GeneratedFixtures/`；运行回归时会重新生成。
- 整理前 README 备份为 `Archive/20261008-Cleanup/README_Before.md`。

移动清单见 [MoveManifest.json](../Archive/20261008-Cleanup/MoveManifest.json)，记录原路径、目标路径、文件数和字节数。历史内容采用归档方式保留。

`Output-Major/` 的程序、依赖、`Settings/` 和 `Data/` 保持原位置。`Source/bin`、`Source/obj`、`Tools/*/bin` 和 `Tools/*/obj` 是正常构建产物，可由编译重新生成；工具演示数据属于验证环境。

## 后续存放原则

- 源码放 `Source/`，验证工具放 `Tools/`。
- 使用指南更新根 README，功能细节及验证结果放 `Docs/`。
- 日志放 `Docs/Logs/`，播放器诊断图放 `Docs/Diagnostics/`，程序截图放 `Docs/UiPreview/`。
- 临时工程放带日期的 `Archive/` 子目录。
- 发布时保留 `Output-Major/Settings` 与 `Output-Major/Data`。

## 整理后核对

- 43 个本地 Markdown 链接、10 个工具项目引用及 14 个移动目标均已核对。
- 从新路径运行 `Tools/LyricVerification`，28 项回归通过。
- `Tools/LyricDiagnostics` Release 编译通过，零警告、零错误。
- 发布程序仍为 v0.6.0；EXE 的 SHA256 整理前后相同，程序入口和用户数据目录保持原位置。

## 2026-10-09 当前整理（v0.9.10）
Distribution 的 AppPayload、RuntimePayload、DownloadCache、app.zip、runtime.zip 移到 Build/Installer；安装验证沙盒和生成 EXE 移到 Build/Verification/Installer。Tools/Sysinternals 移到 Runtime/Diagnostics。打包脚本同步采用新路径。源码与用户运行目录不移动，原设置与历史保留。
Distribution 仅保留最新安装包、干净源码包、各自校验记录和 README。新增 Licenses 原文、非商业 LICENSE、第三方声明、贡献说明、开发与隐私指南；详细旧使用说明整理到 Docs/UserGuide.md，根 README 改为统一入口。移动记录见 Logs/Organization-20261009.json。
本地 Build/Runtime/Archive/Output-Major 与私人日志截图全部排除源码包。只在构建成功后删除旧交付包。

整理后验证：源码白名单包独立解压编译成功，0 警告/0 错误；主要文档本地链接检查通过。源码包与安装负载均包含 LICENSE、THIRD_PARTY_NOTICES 和原第三方许可，无 Settings/Data 等用户目录。更新安装包实测许可落盘通过，108 个既有用户文件哈希不变。
