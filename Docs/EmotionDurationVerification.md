# v0.8.1 AI 情绪程度与自动持续时间

2026-10-08。用户要求由 AI 判断情绪程度，合理生成歌词关键词持续秒数。

## 时长规则

Ollama 接收实际听过的歌词片段，结合关键词所在句及相邻句判断效果类别和 0–1 的情绪表达程度。听歌次数、字数不能替代情绪程度；提示词强调否定、转折和景物描述的区别。

程序统一换算，而不是接受模型随意填写的 `seconds`：

```text
持续秒数 = round(2 + 10 × 情绪程度, 1)，范围 2–12 秒
行情影响强度 = round(0.5 + 情绪程度, 2)，范围 0.5–1.5
```

| 情绪程度 | 说明 | 示例秒数 |
|---|---|---|
| 0–24% | 轻微 | 20% → 4 秒 |
| 25–49% | 中等 | 40% → 6 秒 |
| 50–79% | 强烈 | 65% → 8.5 秒 |
| 80–100% | 极强 | 80% → 10 秒；100% → 12 秒 |

这是音乐行情玩法的时长策略，不是心理学量表。相同评分生成相同秒数，强程度不会生成更短时长。

## 校验与保留

- 原生 `/api/chat` 使用 JSON Schema，要求 `keyword/effect/intensity/reason`；类别限制为四种，评分为有限数值且在 0–1。
- 不接受缺失评分、`"80%"` 字符串、负数、超过 1 的值；忽略模型额外返回的时长和强度。
- 关键词必须出现在本次实际发送的片段中。模型看到的中文保持可读，避免把嵌套 JSON 的 Unicode 转义字面量当作歌词。
- 新的有效 AI 判断可以更新之前的 AI 规则；本地词库只更新其出现歌曲数，不覆盖 AI 的时长、强度、类别或评分。
- 没有 AI 判断的新词默认 6 秒并标注“待 AI 判断”；手动规则仍优先，用户修改自动规则后转为手动设定。
- 学习表显示程度、秒数和依据，依据自动换行；关键词编辑器显示 AI 情绪程度。事件提示同时显示评分与秒数，提示和行情效果使用同一个结束时刻。
- 学习缓存版本升为 2，使旧的任意秒数建议重新分析。本机可用项目独立 CLI 时，AI 调用自动启动本地服务，模型目录固定到项目，禁用云端能力。

接口依据：[Ollama 结构化输出](https://docs.ollama.com/capabilities/structured-outputs)、[本机模型](https://ollama.com/library/qwen2.5:1.5b)。

## 验证记录

`Tools/LyricLearningVerification`：50 项规则及接口检查通过，包含单调时长映射、评分校验、离线保留、手动优先、中文输入、Schema 请求和提示／效果同时到期。日志：[EmotionDuration_Checks.log](Logs/EmotionDuration_Checks.log)。

`Tools/PlaygroundVerification`：39 项原玩法回归通过。日志：[EmotionDuration_Playground.log](Logs/EmotionDuration_Playground.log)。

真实模型使用 `Tools/OllamaEmotionProbe`，每种明确情绪单独调用，并要求匹配类别及弱／强程度，再调用已听歌曲语料。0.5B 在平静语句上给出错误分类或空结果，未作为默认模型；保留失败记录：[0.5B 检查](Logs/Emotion_Model05BQuality.log)。

1.5B 初次把平静类别的确定程度误作高情绪强度。提示增加“不是分类置信度”的说明、温和与激烈表达参照，并提供已匹配候选词与真实上下文；有候选词时 Schema 至少要求一个判断，没有候选词时仍允许空数组。

最终真实检查（使用当前客户端）：

| 测试语境 | 模型判断 | 生成秒数 |
|---|---|---|
| 微风轻轻吹过、心情平静 | Calm，25% | 4.5 |
| 勇敢向前、永不放弃 | Attack，65% | 8.5 |
| 孤独压得喘不过气、眼泪止不住 | Retreat，90% | 11 |
| 狂风怒吼、烈火燃烧 | Volatile，90–95% | 11–11.5 |

6 首已听歌曲返回 4 条有效 AI 建议：“拥抱／等候／牵手”15% → 3.5 秒，“绽放”90% → 11 秒。已合并到发布版规则库，手动规则保留；当前项目启用本机 AI，默认使用已下载的 1.5B。这些是有限样例和接口检查，不保证所有歌词语义都准确；支持审阅和手动调整。

真实日志：[Emotion_RealModelProbe.log](Logs/Emotion_RealModelProbe.log)；有效建议：[JSON](Diagnostics/Emotion_RealModelSuggestions.json)；实际结果窗口：[截图](UiPreview/Emotion_RealModelLearning.png)。服务启用 `OLLAMA_NO_CLOUD=1`，模型保存在项目 `Runtime/Ollama/Models`，推理请求只发送到 127.0.0.1。
