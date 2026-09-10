# 天空岛字幕排队策略回归

分类 COMPAT。工程只链接完整生产 `DebugAndTools/SkyIsland/SkyIslandCaptionQueue.cs`，**没有任何替身**：
这个类型刻意不依赖 Unity，排队规则整段原样执行。

覆盖：普通字幕先来先播；与队里或正在播的同一句去重（后者要求刷新停留）；警示排在全部普通字幕之前、
其它警示之后；队满先丢最旧的普通字幕、全是警示时普通字幕直接丢；只有「正在播普通字幕 + 新来警示」才打断；
同句普通字幕以警示身份再来时升级插队；以及固定种子的 200 轮随机序列上的四条不变式
（容量不超、警示永远在普通字幕前、无重复、有普通字幕可让位时警示绝不被丢）。

使用 `python tests/fixtures/SkyIslandHudPolicy/run.py`，或聚合入口
`python tools/run_runtime_regressions.py --filter SkyIslandHudPolicy`。只需要本机 .NET SDK，不需要游戏程序集。

这组测试验证「谁先播、谁被丢、要不要打断」的算术，**不**验证字幕的淡入淡出时长、屏幕位置、
与官方 HUD 的遮挡和实际可读性；那些由 `tests/SkyIslandHudGuard.py` 钉结构、最终只能实机看
（`docs/制作教程/天空岛/天空岛_待人工验证清单.md`）。
