# v0.4.0 常用指标与四副图

2026-10-08。用户确认：主图之外最多 4 个可选指标副图，能量柱额外固定保留。

## 参考

- 同花顺指标广场：https://pos.10jqka.com.cn/center/default/index/ 。选取基础、常用指标。
- 官方常用指标分类：https://www.10jqka.com.cn/ad_mar/man/f4.htm 。主图选择 MA、BOLL、EXPMA；副图选择 MACD、KDJ、RSI、WR、BIAS、CCI、ATR、ROC。
- 官方操作说明：https://www.10jqka.com.cn/modules.php?id_cat=5&myfaq=yes&name=FAQ 。参考图内右键“常用指标”、指标名称双击修改参数、主图叠加指标的操作。
- 同花顺手册：https://www.guosenqh.com.cn/upload/20240125/20240125163940134.pdf 。底部指标标签、图上指标参数和值及多图布局参考。
- 公式口径参考：https://www.10jqka.com.cn/ad_mar/man/f4-1.htm 。WR 使用国内终端常见 0–100 区间；ATR 使用真实波幅的 N 期简单平均。

## 布局与交互

上方 K 线／分时主图及 MA/BOLL/EXPMA 叠加线；其下最多 4 个独立技术指标图；频段能量柱固定在右侧图表底部；最底部为指标标签。

先点击副图确定要替换的窗口，再点击指标标签；也可用右键菜单“常用指标”、名称旁箭头、键盘名称加 Enter。主图类指标切换只更新叠加线，不占副图名额。右上 N副图菜单与顶部技术指标菜单提供 0–4 个副图选择。右键可删除当前副图。能量柱不出现在可替换或可删除的列表中。

双击指标名称或右键修改参数。MACD 校验短周期小于长周期；BOLL 可修改周期与标准差倍数；参数可恢复默认。选择、数量及参数保存到发布目录 Settings/indicators.json。

资讯区仍保留放大当前句和顺序歌词。重新分配固定高度，为主图及指标留出空间；同一窗口下副图数量变化不会改变资讯区或能量矩形的坐标。1180×720 下四副图为紧凑显示；更大窗口／全屏提供更多绘图空间。

## 数据及公式

指标输入来自现有音乐行情 OHLC。以 `100 × exp(行情值)` 映射为正价格指数，保证 BIAS、ROC 等比例公式在行情穿越零时稳定；主图线通过逆映射绘制到原来的涨跌幅坐标。MACD 为 EMA12−EMA26、DEA=EMA9(DIF)、柱=2(DIF−DEA)；BOLL 为 MA20 ± 2 倍总体标准差；KDJ 默认 9/3/3；RSI 默认 6/12/24。

最多保留 2048 根完成 K 线，EMA 不因可见窗口每次滚动而重新起算；当前未完成 K 线每帧参与计算。分时模式使用对应分时序列。切歌清空历史并保留指标设置。样本不足的均线显示 --；平价及空输入已覆盖，不产生无限值。

## 验证

- `Tools/IndicatorVerification`：44 项通过。含 MA、BOLL 的已知数值、MACD 递推、KDJ、RSI、WR、BIAS、CCI、ATR 跳空、ROC、平价／空输入、数量上限、0→4 能量矩形完全相同、四图顺序、目标窗口替换、键盘切换、时长／换歌状态、参数对话框及设置保存加载。
- `Tools/LyricVerification`：28 项通过，歌词同步逻辑保留。
- `Tools/UiPreview`：默认与最小窗口、分时、全屏及四副图截图；行情行点击、滑条联动、图表页签、滚轮通过。检查主图、四副图、固定能量及资讯无覆盖。
- `Tools/LyricLiveCheck`：真实酷狗读取 109 行歌词，真实进度持续推进，音量仍显示 100%；默认 MACD 在运行界面正常绘制。
- Release 编译、发布到原 Output-Major 目录完成。

预览：Docs/UiPreview/Terminal_Indicators4_1560x900.png、Terminal_Indicators4_1180x720.png、Terminal_Fullscreen.png。演示预览使用固定数据；Terminal_LiveLyrics.png 为实时运行截图。
