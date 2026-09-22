# 天空岛居民对话执行回归

分类：COMPAT。直接链接生产 `DialogueManager` 与 `SkyIslandResidentDialogue`，覆盖字幕和多选取消、迟到回调隔离、重复开口、失效会话、正常办事与缺失 actor 的降级。

2026-09-22 扩展：无 token 的旧本地化 key 故事必须把取消传回调用者，避免中断后标记完成或发奖励；旧官方 drain 挂起后发生 Cleanup，即使 UI 实例保持不变，也不得确认后继会话。后者用可挂起的 NextFrame 与独立的官方确认替身控制交错次序。两项已分别通过副本变异验证转红，工作区生产源码未用于临时破坏。

Unity 对象、官方 UI 和 UniTask player loop 是可控替身：等待保留到显式 Pump，按生产传入的 CancellationToken 取消。测试不证明官方界面动画、真实输入栈或 Unity 的帧调度；这些须实机复测。共享旧 API 签名另外由 guard 保持。

运行：`python tools/run_runtime_regressions.py --filter SkyIslandDialogue`。
