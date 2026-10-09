# 第三方组件与内容声明

本项目自己的代码采用 [MusicTide 非商业源码许可](LICENSE)。下面的第三方组件各自遵循原许可，项目的非商业限制不会改变这些组件的独立权利。

| 项目 | 用途与版本 | 原许可／来源 | 本地许可 |
|---|---|---|---|
| .NET / Windows Desktop Runtime | Windows 桌面程序；安装包携带 9.0.20 | [dotnet/runtime](https://github.com/dotnet/runtime)、[MIT](https://github.com/dotnet/runtime/blob/v9.0.0/LICENSE.TXT) 及第三方条款 | [LICENSE](Licenses/DotNet-LICENSE.txt)、[第三方声明](Licenses/DotNet-ThirdPartyNotices.txt) |
| Ollama | 安装包携带 0.40.1 CPU 引擎 | [官方项目与 MIT 许可](https://github.com/ollama/ollama/blob/v0.40.1/LICENSE) | [LICENSE](Licenses/Ollama-LICENSE.txt)；底层库许可随 `Runtime/Ollama/lib/ollama` 分发 |
| qwen2.5:1.5b / Qwen2.5-1.5B-Instruct | 可选本地情绪分析模型，完整安装下载约 986 MB | [Ollama 模型条目](https://ollama.com/library/qwen2.5:1.5b)、[Qwen 官方 Apache-2.0 许可](https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct/blob/main/LICENSE) | [模型实际分发的许可层](Licenses/Qwen2.5-1.5B-LICENSE.txt)；固定 manifest 在 `Tools/Installer/model.json` |
| Windows API / UI Automation / Windows OCR / SMTC | 系统音频、窗口文本、OCR、媒体时间和控制 | 使用操作系统接口；Windows、字体及 SDK 由 Microsoft 自身条款管理 | 不作为本项目自有代码重新授权 |
| Sysinternals Handle | 仅本地开发诊断，非应用依赖、非安装负载 | [Microsoft 官方工具](https://learn.microsoft.com/sysinternals/downloads/handle)，专有条款 | `Runtime/Diagnostics/Sysinternals/Eula.txt`；不进入源码包 |

主项目当前没有显式 NuGet `PackageReference`；.NET SDK 仍会还原目标 Windows SDK 引用。系统 API 的直接调用不等于复制音乐客户端的源码。

## 歌词、音乐与品牌

- 歌词来自用户的本机缓存、播放器显示、用户提供的 LRC，或按歌曲元数据查询的网易云／LRCLIB 接口。接口访问不授予歌词或音乐的再发布权。
- 正式安装负载和源码包不包含开发者的歌曲记录、完整歌词缓存、私人设置或音乐文件。使用者导入和保存的内容仍归原权利人所有。
- QQ 音乐、网易云音乐、汽水音乐、酷狗、同花顺等名称仅用于兼容说明或界面研究来源，项目不属于这些公司，不复制或分发它们的客户端。
- 音潮行情的源码许可不能被用来重新授权第三方模型、第三方组件、歌词或音乐。

## 分发方式

源码包保留 `LICENSE`、本声明及 `Licenses/`；二进制主程序和安装包也携带这些文件。Ollama 底层依赖与 .NET 的原许可文件随各自运行时保存。
