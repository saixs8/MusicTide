# 发布文件

当前版本 **v0.9.10**。本目录只保留最新交付物，中间素材位于 `../Build/Installer`。

| 文件 | 内容 |
|---|---|
| `MusicTideSetup-v0.9.10.exe` | Windows x64 一键安装器，支持完整／离线安装 |
| `MusicTide-Source-v0.9.10.zip` | 干净源码、文档及非商业许可 |
| `Setup-SHA256.json` / `Source-SHA256.json` | 对应文件的 SHA256 |

## 安装

Windows 10 2004／19041 或更新、Windows 11，Intel／AMD x64。默认安装到 `%LOCALAPPDATA%\Programs\MusicTide`，建立桌面及开始菜单快捷方式，无需管理员权限。

- 完整安装：主程序、私有 .NET 9.0.20、Ollama 0.40.1 CPU 引擎，以及下载并校验约 986 MB 的 qwen2.5:1.5b 模型。
- 离线安装：内置组件正常安装，跳过模型下载。行情、频谱及播放器／本地歌词可用；在线歌词查询需要网络。
- 网络或下载失败可先离线完成，保留断点。未补装时每次启动询问，支持“暂不安装”和“不再询问”。不自动后台下载。
- 联网后可在启动提醒里补装；已关闭提醒时运行安装目录的 `MusicTideSetup.exe`，选择完整安装。
- 已有 Settings 和 Data 保留；不强杀其他目录的播放器、Ollama 或应用进程。

安装器未配置商业代码签名。复制源码包不会自动提供 .NET 或 AI 模型；编译方法见 [开发指南](../Docs/Development.md)。

## 许可与数据

安装包和主程序携带项目 [非商业许可](../LICENSE)、[第三方声明](../THIRD_PARTY_NOTICES.md)以及原许可文件。未经授权禁止商用，非商业限制适用于项目自有内容；第三方组件按原许可管理。源码包不含开发者歌词缓存、听歌历史、私人设置、诊断截图或运行时模型。

## 重建与清理

```powershell
powershell -File Tools/Installer/BuildSetup.ps1
powershell -File Tools/BuildSourcePackage.ps1
```

从项目根目录执行，先准备安装素材。读取 csproj 版本号，新包生成成功后才替换旧版本并更新 SHA256；正式目录各保留一个最新安装包和源码包。
