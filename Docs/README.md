# 文档索引

使用说明以项目根目录 [README](../README.md) 为准；本目录保存各版本实现与验证记录。

## 功能与验证
 
当前入口： [完整使用指南](UserGuide.md) · [开发与发布](Development.md) · [数据与隐私](Privacy.md) · [非商业许可](../LICENSE) · [第三方声明](../THIRD_PARTY_NOTICES.md)。

- [多播放器验证](MultiPlayerLyricsVerification.md)：QQ／网易云／汽水适配及验证范围。
- [无声与安装验证](SilenceAndInstallerVerification.md)：v0.9.8–v0.9.10 静音、安装与离线补装。

| 文档 | 内容 |
|---|---|
| [MarketHistoryVerification.md](MarketHistoryVerification.md) | v0.9.0 虚拟行情、随机价格、五版叠加、容量与 K 柱分时钻取 |
| [NavigationDefaultsVerification.md](NavigationDefaultsVerification.md) | v0.8.5 原生鼠标事件、少量 K 线缩放、9 档字体与 5 个默认关键词 |
| [EmotionDurationVerification.md](EmotionDurationVerification.md) | v0.8.1 AI 情绪程度、自动秒数与本机模型验证 |
| [DayNavigationVerification.md](DayNavigationVerification.md) | v0.8.0 日 K 缩放、历史拖动与指标联动 |
| [LyricLearningVerification.md](LyricLearningVerification.md) | v0.7.0 关键词扩充、本地学习与 Ollama 接口 |
| [PlaygroundVerification.md](PlaygroundVerification.md) | v0.6.0 七种玩法、大字菜单与 39 项检查 |
| [MusicFeaturesVerification.md](MusicFeaturesVerification.md) | v0.5.0 连板、对比与 25 项检查 |
| [IndicatorsVerification.md](IndicatorsVerification.md) | v0.4.0 指标、四副图与 44 项检查 |
| [TrackSwitchVerification.md](TrackSwitchVerification.md) | v0.3.1 切歌／托盘同步与 28 项歌词回归 |
| [PlaybackControlsVerification.md](PlaybackControlsVerification.md) | v0.3.0 菜单、歌曲及音量控制 |
| [UiPreview/README.md](UiPreview/README.md) | 截图索引及生成命令 |
| [FolderOrganization.md](FolderOrganization.md) | 目录整理与归档位置 |

## 历史研究与计划

- [PlaybackClockResearch.md](PlaybackClockResearch.md)：播放器真实时间获取研究。
- [LyricSyncVerification.md](LyricSyncVerification.md)：早期真实时钟修复验证。
- [USBSideScreenPlan.md](USBSideScreenPlan.md)：硬件和 USB 通信后续计划。

## 日志与诊断图

- [Logs/PlaybackClockLiveCheck.log](Logs/PlaybackClockLiveCheck.log)：历史真实时间日志。
- [Logs/TrackSwitchBefore.log](Logs/TrackSwitchBefore.log)：修复前切歌现场。
- [Logs/TrackSwitchLiveCheck.log](Logs/TrackSwitchLiveCheck.log)：修复后切歌现场。
- `Diagnostics/`：历史播放器窗口诊断截图。

历史文档保留当时版本与观察；可重复运行的工具路径统一更新为 `Tools/`。

现场日志、诊断截图和历史 UI 图片为本地工作资料，不进入源码包；源码包中的相关引用用于说明历史验证范围。
