# 无声修复与一键安装 v0.9.8

## 无声仍运动的根因

FFT 无声时目标已清零，但界面定时器继续执行行情更新。频段平滑下降影响价格，分时／日 K 的偏移和随机跳空、玩法以及基础成交量继续推进；采集启动异常还会自动开启模拟。另有 WASAPI SILENT 标志下 8 位 PCM 的零字节被解释成 -1 的问题。

本次修改：有新鲜且有效音频才能推进行情，音频丢失超过 350 毫秒或 RMS 低于 0.0001 时暂停，保留当前 OHLC／K 周期；独立能量继续衰减并最终精确归零。歌词及播放时间仍独立更新。模拟只由手动选择启用。SILENT 标志直接写入零样本，不再依赖 PCM 字节编码。

`Tools/SilenceVerification` 10 项通过：模拟可用、无声价格不漂移、分时不漂移、没有新增 K 柱／虚拟成交量、量能归零、旧包拒绝、8 位静音校验、恢复音频和无补柱。原歌词 28 项回归通过。

## 安装包

安装器基于 Windows 自带 .NET Framework 的 WinForms 启动，不需要先安装 .NET 9。携带独立 .NET 9.0.20、程序二进制及 Ollama CPU 依赖；模型通过固定 manifest 的 SHA256 下载。解压检查目标绝对路径，禁止跨安装目录写入。个人设置／历史不进入负载。

官方 .NET 两个 ZIP 已按官方发布元数据 SHA512 校验，记录在 `Docs/Logs/InstallerRuntimeSources.json`。PowerShell 下载等待过久，改用 Microsoft CDN 与 curl 成功取得并校验；未关闭 TLS 校验。

安装、独立运行时启动、CPU 模型实际推理、重复安装保留用户文件、真实仓库小文件断点续传均通过。推理结果保存于 `Docs/Logs/InstallerModelInference.json`，界面预览 `Docs/UiPreview/MusicTideSetup.png`。

测试过程中应用按需启动的 AI 服务持有 Ollama.exe，重复安装最初遇到占用；现先比较资源与已安装文件 SHA256，完全相同的文件不覆盖，重复安装通过。未来二进制不同且被占用时安装报错重试，不强制结束其他用户程序。

## 官方依据

- [.NET 发布模式](https://learn.microsoft.com/en-us/dotnet/core/deploying/)：框架依赖部署及 AppRelative 私有运行时查找。
- [.NET 9 发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/9.0/releases.json)：运行时下载和哈希。
- [Ollama v0.40.1](https://github.com/ollama/ollama/releases/tag/v0.40.1)：本地引擎；CPU 依赖来自项目中已验证、有效签名的官方二进制。

## v0.9.9 补充修复
无声首帧直接清零 energyLevels、energyTargets、bandTargets 和 musicEnergy，覆盖量能柱、表格、排序与底部能量。无声期间不执行坐标范围平滑及歌词粒子位移；行情、指标、成交量保持静止。播放器时钟、歌词定位和人工交互继续可用。15 项静音检查通过，新增首帧归零、坐标范围与粒子不变验证。

## v0.9.10 离线安装验证
实际 --test-offline 安装退出码 0，生成 ai-install-pending.json，未写入未就绪模型 manifest。随后 --test-install 使用经过哈希校验的本机模型补装退出码 0，manifest 就绪且待补装标记删除。18 项应用检查通过（原 15 项静音回归，加完成安装无提醒、离线重复启动提醒、抑制提醒持久化）。安装器实际截图已检查。发布前后 108 个用户文件 SHA256 一致。Distribution 仅保留 v0.9.10 安装包。
