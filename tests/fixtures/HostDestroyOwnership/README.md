# 宿主销毁归属回归

`run.py` 从当前生产源抽取完整 `ModBehaviour.Awake()`、`OnDestroy()` 和成就清理兼容转发，直接链接生产 `BossRushRuntimeModuleHost`、基类与接口。执行未 Awake、重复实例、Unity fake-null 的活动 owner、重复销毁及旧实例迟到回调；正常 owner 必须保留全部清理槽位、模块逆序销毁和最后的存档节流复位。

Unity 对象替身实现销毁对象同时销毁组件和 fake-null 比较。模块装配及清理叶子用记录调用的替身，成就字段在真实 Awake 调用装配前保持 null；本夹具只证明宿主入口的归属门与调度，不证明叶子内部资源收尾。各叶子另由自己的生产执行回归覆盖，不启动游戏或访问玩家存档。

通过 `python tools/run_runtime_regressions.py --filter HostDestroyOwnership` 执行。产物和生产 SHA-256 写入 `Build/runtime-regressions/HostDestroyOwnership/`。

2026-11-26：`BossRushStartupGuard` 用记录调用的替身，五个初始化阶段替身会记轨迹、可按 `ThrowStage` 抛错。执行真实 Awake，断言先 `BeginAwake` 再装配模块、最后 `CompleteAwake`；任一阶段抛错立即上报，后面的阶段与自检照常执行；OnDestroy 在语言退订之后调 `Shutdown`。0Harmony 预加载、目录搜索和日志内容不在这里，由 `tests/HostStartupIsolationGuard.py` 守结构，行为要 L3。

2026-11-26 第二轮（外部复审）：`run.py` 另抽取真实 `Start()`。模块登记替身可按 `ThrowStage = "register"` 在登记完第一个模块后抛错，断言立即上报 `register-modules`、补丁层与后面各阶段照常执行；集成启动替身可按 `ThrowStage = "integration"` 抛错，断言上报 `integration-start` 后两个模块的 OnStart 都照常分发，启动自检（`CompleteStart`）排在 Start 最后。
