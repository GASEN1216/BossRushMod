# 天空岛居民对话执行回归

分类：COMPAT。直接链接生产 `DialogueManager` 与 `SkyIslandResidentDialogue`，覆盖字幕和多选取消、迟到回调隔离、重复开口、失效会话、正常办事与缺失 actor 的降级。

Unity 对象、官方 UI 和 UniTask player loop 是可控替身：等待保留到显式 Pump，按生产传入的 CancellationToken 取消。测试不证明官方界面动画、真实输入栈或 Unity 的帧调度；这些须实机复测。共享旧 API 签名另外由 guard 保持。

运行：`python tools/run_runtime_regressions.py --filter SkyIslandDialogue`。
