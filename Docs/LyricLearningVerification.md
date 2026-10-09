# v0.7.0 歌词关键词与本机 Ollama

2026-10-08。用户选择本机 Ollama，歌词只在本机分析。

## 实现

- `LyricRuleLearning`：67 条内置规则，附加词汇按听歌记录学习，总量最多 200。四类为进攻、回落、放大波动、稳定走势。“不放弃”等优先匹配，避免把否定词误学成回落。
- 从最近 20 首非模拟记录的采样位置查找实际歌词，每首最多最近 12 个不同句子，排除片头署名。重复听同一歌不虚增歌曲数；模型最多接收 8 首，每首 6 句，每句最多 60 字。
- 本地学习按关键词近期出现歌曲数调整强度和时长；Ollama 提出新的短语、类别、强度和时长，返回值必须出现在已听歌词中且满足类型与范围要求。
- 手动规则不被覆盖，修改自动规则会转为手动。删除词保存为抑制列表。自动规则优先于内置规则，手动规则最高；同句第一条匹配规则生效。
- 编辑器显示来源与歌曲数；“关键词学习与本机 AI”可读取已下载模型、设置自动学习与自动应用、立即学习和应用选中建议。关闭自动应用后保存建议等待审阅。
- 启动、自动保存记录、切歌保存时调度后台任务，至少间隔 60 秒，单个任务运行；手动立即学习等待已有任务完成后重新分析。指纹包含听歌内容、模型、接口、应用方式与抑制列表。
- 用户切换设置时，旧任务结果不会覆盖新设置；推理期间手动编辑的规则仍保留。关闭程序取消推理。

## 本机与数据约束

`OllamaLyricClient` 使用原生 `/api/tags`、`/api/show`、`/api/chat`。只接受本机 HTTP 地址，关闭代理和重定向。发送歌词前检查模型元数据，拒绝 `remote_host`、`remote_model` 和云端模型名；无需 API 密钥。

使用非流式 JSON 输出，90 秒请求超时、512 KB 响应上限，AI 强度 0.5–1.5、时长 1–12 秒。模型无效、离线或返回错误时仍可本地学习。

`Settings/lyric-ai.json` 保存选项与抑制词；`Data/Playground/lyric-learning.json` 保存建议、状态和指纹，不另存完整歌词副本。实际行情规则继续保存到 `Settings/playground.json`。老规则只迁移一次补充词库，保留手动优先级。

`Tools/StartLocalOllama.ps1` 支持已安装或项目内独立 CLI。新启动服务仅监听本机，设置 `OLLAMA_NO_CLOUD=1`，模型和日志放项目 `Runtime/Ollama`；已有服务沿用现有配置。

依据：[原生聊天 API](https://docs.ollama.com/api/chat)、[模型列表](https://docs.ollama.com/api/tags)、[Windows 独立 CLI](https://docs.ollama.com/windows)、[本地与云端配置](https://docs.ollama.com/faq)、[qwen2.5:0.5b 模型](https://ollama.com/library/qwen2.5:0.5b)。

## 验证结果

| 验证 | 结果 |
|---|---|
| Release 构建与发布 | 零警告、零错误，发布版本 0.7.0 |
| LyricLearningVerification | 37 项通过，含真实 Form1 后台学习与审阅模式 |
| PlaygroundVerification | 39 项通过；测试显式设置播放与歌词状态，避免现场播放器异步读取影响夹具 |
| LyricVerification | 28 项歌词回归通过 |
| 用户数据发布检查 | 9 个既有设置／数据文件 SHA256 保持一致 |
| 已有听歌记录 | 5 份记录，4 首有效歌曲，7 条本地建议；无外发 |
| 现场歌词 | 酷狗“许嵩 - 认错”，53 句；约 4 秒后取得真实进度 255.49 秒，约 6 秒推进至 257.37 秒；当前已在末句，索引 52 保持正确 |

接口验证使用仅监听本机的测试服务，确认模型检查不包含歌词，正常聊天能解析、云端模型在歌词发送前被拒绝。**这是协议测试，不是真实模型推理。**

本机尚未安装 Ollama，也没有可调用的本地模型。官方独立 ZIP 下载多次遇到 TLS 握手错误；运行程序与模型未安装，实际分类效果需在安装后验证。默认不勾选 AI，本地学习与自动应用可直接使用。

测试日志：[关键词 37 项](Logs/LyricLearning_Checks.log)、[玩法 39 项](Logs/LyricLearning_Playground.log)、[现场歌词](Logs/LyricLearning_LiveCheck.log)。界面：[关键词学习窗口](UiPreview/LyricLearning.png)。

## 后续使用

1. 从官方获得 Ollama，独立 ZIP 可解压至 `Runtime/Ollama`。
2. 项目根目录运行 `powershell -File Tools/StartLocalOllama.ps1 -Pull` 下载默认小模型并启动本机服务。
3. 打开“音乐玩法 → 关键词学习与本机 AI”，读取模型、勾选使用本机 Ollama，然后保存并立即学习。
4. 若先审阅，关闭自动应用；选中建议后应用。勾选“歌词事件牌”使规则影响音乐行情。
