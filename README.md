# 音潮行情 · MusicTide

**v0.9.11 · Windows 音乐行情桌面程序 · 源码公开，限非商业使用**

把电脑正在播放的音乐转换为实时频谱、虚拟股票行情和 K 线，配合歌词资讯、本机 AI 情绪学习、高潮连板及歌曲对比。支持拖到 USB／HDMI 副屏并全屏显示。

> 本项目允许学习、修改和非商业分发，**未经书面授权禁止商用**。包括销售、收费部署、商业产品集成、企业经营用途及变现内容制作。详见 [LICENSE](LICENSE)。本项目采用自定义非商业源码许可，属于 source-available；[标准开源定义](https://opensource.org/osd)不允许限制商业用途，因此不将其宣传为 MIT／GPL 或 OSI 认可的开源项目。

所有证券代码、价格、成交量和财务指标均为音乐生成的虚拟数据，用于音乐可视化和娱乐。

## 下载与安装

| 交付物 | 用途 |
|---|---|
| [MusicTideSetup-v0.9.11.exe](https://github.com/saixs8/MusicTide/releases/download/v0.9.11/MusicTideSetup-v0.9.11.exe) | 普通用户一键安装；自带私有 .NET 和 Ollama CPU 引擎 |
| [MusicTide-Source-v0.9.11.zip](https://github.com/saixs8/MusicTide/releases/download/v0.9.11/MusicTide-Source-v0.9.11.zip) | 源码、文档、许可及工具；不含模型、用户数据和构建缓存 |
| `Output-Major/MusicTide.exe` | 本地手动发布版；需 .NET 9 Desktop Runtime，复制时保留同目录依赖 |
| [Setup-SHA256.json](https://github.com/saixs8/MusicTide/releases/download/v0.9.11/Setup-SHA256.json) | 安装包 SHA256 校验记录 |

运行环境：Windows 10 2004／19041 或更新、Windows 11，Intel／AMD x64。ARM64 原生运行未验证。开发需 .NET 9 SDK。

### 两种安装方式

- **完整安装**：内置主程序、.NET 9.0.20、Ollama 0.40.1 CPU 引擎；额外下载约 986 MB 的 `qwen2.5:1.5b`，校验 SHA256，支持断点续传。
- **离线安装**：跳过模型下载，可使用行情、频谱和播放器／本地歌词；在线查询歌词需要联网。本地 AI 待补装，基础词库学习仍可运行。
- 完整安装没有网络或下载失败时，先完成离线安装，保留下载进度。
- 待补装时每次启动提示，可“联网补装”“暂不安装”或勾选“不再询问”。之后可手动运行安装根目录里的 `MusicTideSetup.exe` 补装。补装成功后取消提醒。

默认安装至 `%LOCALAPPDATA%\Programs\MusicTide`，无需管理员权限，不修改全局 PATH。应用在 `App/`，私有运行时在 `Runtime/`。安装器保留已有设置和歌曲历史。详见 [安装说明](Distribution/README.md)。

## 快速使用

1. 启动程序，在音乐播放器中播放歌曲。
2. 左侧按板块筛选 100 个频段，点击行选中行情；点击表头循环升序、降序、默认顺序。
3. 右侧切换“日 K／分时”，底部歌词资讯突出当前句，后续歌词按时间顺序排列。
4. 顶部歌曲区提供上一首、下一首、暂停、停止、静音和音量调整。
5. “音乐玩法”管理对比、盲盒、高潮连板、关键词 AI 学习和歌曲历史；终端设置默认收起。

## 主要功能

| 功能 | 当前行为 |
|---|---|
| 实时频谱 | 100 个对数频段；固定能量柱持续跟随当前音乐，独立于图表浏览 |
| 证券映射 | 低频为沪深主板，中频前半创业板、后半科创板，高频北交所；编号为模拟代码 |
| 日 K | 左侧价格、右侧涨跌幅；按当前可见窗口适配，涨跌幅基准为最左柱开盘价 |
| 分时 | 白色现价、黄色均价；0% 始终居中，显示开高低、成交额、换手率和模拟成交量 |
| 技术指标 | 主图 MA/BOLL/EXPMA；副图 MACD/KDJ/RSI/WR/BIAS/CCI/ATR/ROC，最多 4 图，能量柱额外固定保留 |
| 图表浏览 | Ctrl+滚轮缩放、左键左右拖动；“回到最新”恢复跟随，双击 K 柱查看形成过程 |
| 虚拟估值 | 随机开盘 5–100 元；评论和喜欢数默认模拟，可手工填写，市值与财务指标仍为虚拟 |
| 歌曲历史 | 默认保留 20 首，可设 1–1000 首；每首最多 5 版叠加，优先保留重复次数多的歌 |
| 每歌采样 | 新歌及完整循环时随机选择行情与对比频段，两路不同，避免与上一轮重复 |
| 歌词学习 | 默认 5 个关键词；从已听片段学习，本地 Ollama 按情绪程度生成 2–12 秒影响时间 |
| 音乐玩法 | 双歌对比、歌曲擂台、虚拟打板、多空拉锯、正负盲盒、副歌返场、歌词事件牌和成就 |
| 高潮连板 | 高潮可随机连续涨停或跌停，一首歌最多触发一组；支持一字板和跳空及低概率炸板 |
| 无声处理 | 行情和动画停止，实时能量归零；恢复声音后继续，不补出静音期间 K 柱 |
| 字号与副屏 | 菜单默认 10 磅，10–24 磅共 9 档；F11 进入／退出全屏 |

详细参数、估值公式、玩法规则和操作方法见 [完整使用指南](Docs/UserGuide.md)。自定义 USB CDC 协议与屏幕固件尚未实现，见 [后续计划](Docs/USBSideScreenPlan.md)。

### 常用操作

| 操作 | 功能 |
|---|---|
| Ctrl+滚轮 / 左键拖动 | 日 K 缩放／平移 |
| End / Home | 回到最新／最早历史 |
| 左右方向键 / ＋－ | 图表取得焦点后平移／缩放 |
| 输入 BOLL、MACD 等 + Enter | 图表取得焦点后选择指标 |
| 点击副图、底部指标标签／右键 | 切换所选副图；双击指标名称修改参数 |
| Ctrl+M / Ctrl+L | 手动模拟频谱／显示隐藏歌词 |
| F11 / Esc | 全屏切换／清除指标输入或退出全屏 |

## 歌词支持与实际边界

- **酷狗**：读取真实播放时间和本机 KRC／LRC，已做现场验证；有真实时间与缓存时不要求桌面歌词，中途启动可对齐。
- **QQ 音乐、网易云音乐、汽水音乐**：已实现独立适配，优先使用 SMTC 时间和本地 LRC，再用在线匹配、窗口文本或 OCR 回退。适配分支及模拟窗口测试通过，尚未完成各官方客户端版本的现场验证，不能承诺所有版本均可识别。
- 自绘窗口、无障碍控件缺失、客户端不提供歌词或没有匹配时间轴时，可能仍无法同步。桌面歌词可见时更利于回退；单句重复副歌不用于猜测歌曲位置。
- 手工 LRC 放入程序旁 `Data/Lyrics/`，文件名为 `歌手 - 歌名.lrc` 或 `歌名 - 歌手.lrc`。已查询歌词缓存于 `Data/LyricCache/`。
- 当前句后续不跟随时点击“自动跟随”；无声仍波动先检查是否启用了模拟频谱，及其他程序是否正在发声。系统回放会包含其他应用声音。

见 [多播放器验证](Docs/MultiPlayerLyricsVerification.md)、[无声与安装验证](Docs/SilenceAndInstallerVerification.md)。

## 本机 AI、数据与网络

AI 仅支持本机 Ollama，模型默认 `qwen2.5:1.5b`。手动部署默认端口 `11434`，一键安装新建配置使用 `11534`，以软件设置为准。分析发送有限已听歌词片段到本机，拒绝远程地址及云端模型。AI 判断不保证准确，可审阅或手动修改。

联网功能包括模型下载和首次在线歌词匹配；在线匹配会发送歌名、歌手等查询元数据。**本机 AI 分析留在本机，不代表全部功能不联网。** 设置和历史位于程序旁 `Settings/`、`Data/`；升级前可备份这两个目录。详见 [数据与隐私](Docs/Privacy.md)。

## 源码、构建与贡献

核心使用 C# / WinForms、Windows WASAPI 回放采集、SMTC、UI Automation 和 Windows OCR。源码不依赖 QQ／网易云／汽水客户端源码。

```powershell
dotnet build Source/SpectrumKlinePlayer.csproj -c Release
dotnet publish Source/SpectrumKlinePlayer.csproj -c Release -o Output-Major
dotnet run --project Tools/SilenceVerification -c Release
dotnet run --project Tools/MultiPlayerVerification -c Release
```

[开发与打包指南](Docs/Development.md)说明源码构建、安装器素材准备及源码包生成。主程序可独立构建；完整安装器需要额外准备官方运行时素材。当前记录：18 项静音／离线提醒检查、28 项歌词回归以及多播放器适配验证；这些不等于所有第三方客户端现场验证。

贡献方式见 [CONTRIBUTING.md](CONTRIBUTING.md)。源码仓库：[saixs8/MusicTide](https://github.com/saixs8/MusicTide)，安装包见 [Releases](https://github.com/saixs8/MusicTide/releases)。

## 目录

```text
MusicTide/
├── README.md / LICENSE / THIRD_PARTY_NOTICES.md / CONTRIBUTING.md
├── Source/         主程序源码与图标
├── Tools/          构建、验证、诊断工具源码
├── Docs/           使用、开发、隐私及验证记录
├── Licenses/       第三方许可原文
├── Distribution/   最新安装包、干净源码包、校验文件
├── Output-Major/   本地运行版及用户数据（不进入源码包）
├── Build/          安装素材、下载缓存、测试沙盒（不进入源码包）
├── Runtime/        本机 Ollama、模型及可选诊断工具（不进入源码包）
├── Assets/         视频封面素材（不进入源码包）
└── Archive/        历史备份（不进入源码包）
```

[文档索引](Docs/README.md) · [工具索引](Tools/README.md) · [目录整理记录](Docs/FolderOrganization.md) · [第三方声明](THIRD_PARTY_NOTICES.md)

## 许可与对外描述

**音潮行情 MusicTide 是面向音乐爱好者的 Windows 桌面可视化工具。项目公开源码供学习、研究、修改和非商业分享，未经书面授权禁止商用。可把正在播放的音乐转换为实时频谱、虚拟 K 线、歌词资讯和音乐玩法，并通过本机 AI 辅助歌词情绪分析。所有行情均为虚拟音乐数据。**

本项目自己的代码使用 [非商业源码许可](LICENSE)。.NET、Ollama、Qwen 模型及底层库按各自原许可分发；歌词、音乐及第三方品牌不由本项目重新授权。详见 [第三方声明](THIRD_PARTY_NOTICES.md)。

### v0.9.11 安装向导
安装目录可以手工输入或通过浏览选择。界面采用深色风格、青色主按钮，显示组件选择、安装阶段、模型下载百分比/大小/速度，日志可展开。安装完成后点击启动。安装器窗口、EXE、顶部 Logo 与快捷方式使用音潮行情图标。AI 补装沿用当前安装目录。
