# Mode D 准入 owner 回归

直接编译完整生产 `ModeDRuntimeModule.cs` 与 `ModeDRuntimeModule_Lifecycle.cs`，逐字提取当前宿主的 `TryStartModeD`、`StartModeD`、裸体检查及船票查询薄桥。验证风险门 → Mode E 互斥 → 裸体检查 → 真实 StartModeD 的顺序，所有拒绝分支、两段异常边界、初始状态清理、配置读取、物品 / 敌人池 / 开局装备 / 变异 / 路牌顺序及调用实例身份。包含 Awake 前调用，避免把原来捕获的空状态异常移到宿主转发之外。

替身仅覆盖外部风险服务、库存读取、Unity 类型、装备池、变异、路牌和 UI 边界，记录调用并允许注入异常；没有替换准入或启动实现。实际 Unity 场景调度、物品资源与玩家存档不在此夹具范围。生成目录保存生产源 SHA-256。

经 `python tools/run_runtime_regressions.py --filter ModeDEntryOwnership` 运行。`BOSSRUSH_FIXTURE_OUT` 可将生成工程和构建产物写入独立验证目录。
