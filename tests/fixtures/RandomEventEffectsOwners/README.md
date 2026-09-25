通过 `python tools/run_runtime_regressions.py --filter RandomEventEffectsOwners` 运行。

直接链接生产查询、增益目标筛选、天气、播报和音效文件；逐字提取乱入 Boss、巡游鸭、商人异步生成与清理、空投协程，以及七个旧宿主入口。验证非本波选项、敌对安全网与随从豁免、异步失效与迟到回收、巡游完成回调、商店 UI/BGM/对象清理、缓存目标筛选、天气捕获与恢复、等待帧和落地回调。

异步适配沿用既有 `AuditModeLifecycle` 夹具：只把 `UniTaskVoid` / `UniTask.Yield` 映射为可等待的 `Task` / `Task.Yield`。Unity 对象模拟假 null 与 GameObject 连带组件销毁。官方生成核心、元数据、物理、UI 和商店装配为可观测替身；奖池配置仍由 `AirdropSecondReview` 执行真实生产方法，现金取消由 `AuditModeLifecycle` 覆盖。生产哈希随结果落盘，游戏内表现需实机验证。
