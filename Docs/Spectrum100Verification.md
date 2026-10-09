# v0.8.8：100 频段与发布文件名

- 频段由 80 增至 100，继续覆盖 38 Hz–16 kHz 对数范围；行情、FFT 目标、实时量能及列表同步扩容。
- 低中高频玩法使用按总频段数比例计算的边界，避免新增高频遗漏。
- MusicFeaturesVerification 39 项通过，包含全部数组长度、列表第 100 段、14 kHz PCM 响应新增频段、动态量能及切歌回归。
- UiPreview 四张预览生成，频段选择、滑条联动、图表切换和翻页检查通过。
- 发布 MusicTide.exe、MusicTide.dll、MusicTide.pdb、MusicTide.deps.json、MusicTide.runtimeconfig.json；EXE 产品及文件说明为音潮行情，版本 0.8.8。
- 旧版同名配套文件归 Archive/OutputName-v0.8.7。发布前后 37 个 Data/Settings 文件哈希一致。
