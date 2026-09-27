# 宿主销毁归属回归

`run.py` 从当前生产源抽取完整 `ModBehaviour.Awake()`、`OnDestroy()` 和成就清理兼容转发，直接链接生产 `BossRushRuntimeModuleHost`、基类与接口。执行未 Awake、重复实例、Unity fake-null 的活动 owner、重复销毁及旧实例迟到回调；正常 owner 必须保留全部清理槽位、模块逆序销毁和最后的存档节流复位。

Unity 对象替身实现销毁对象同时销毁组件和 fake-null 比较。模块装配及清理叶子用记录调用的替身，成就字段在真实 Awake 调用装配前保持 null；本夹具只证明宿主入口的归属门与调度，不证明叶子内部资源收尾。各叶子另由自己的生产执行回归覆盖，不启动游戏或访问玩家存档。

通过 `python tools/run_runtime_regressions.py --filter HostDestroyOwnership` 执行。产物和生产 SHA-256 写入 `Build/runtime-regressions/HostDestroyOwnership/`。
