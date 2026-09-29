# ModeGEntryOwners

执行入口：`python tools/run_runtime_regressions.py --filter ModeGEntryOwners`。不启动游戏，不读取玩家存档；产物与生产源码 SHA-256 写入 `Build/runtime-regressions/ModeGEntryOwners/`。

完整编译 `ModeGEntry.cs`、`ModeGRuntimeBridge.cs`、`ModeGSpawnTransaction.cs`、`ModeGEntryRuntimeServices.cs` 和 `ModeGEntryHostBridge.cs`，仅把 `UniTask<T>` 换成 `Task<T>`。同时链接完整生产 `ModeGRunState`、`ModeGStateModel`（含 RunContext、门控与资格判据）、`ManagedBossSpawnContracts`，并逐字抽取 `ModeGBossSnapshot` 和核心的 `Update` / `OnUpdate`。入口业务、宿主委托绑定、退款责任转移、原 tick 顺序、生成 staging 登记与 await 前后的归属判断均执行生产实现。

`ModeGRuntimeModule.Initialize`、`StartRun`、`ArmStartupRefund`、`End`、`Dispose` 和 `DriveCore` 是可控外部依赖替身，用来观察入口何时绑定真实 RunState/RunContext、是否创建独立核心、何时把退款责任交给核心，以及是否按序清理。它们不证明核心九波战斗或核心实际退款算法正确；生产战斗、奖励与持久化另由 `ModeGCombat` 覆盖。HUD、玩家背包、持久化、地图资源和共享生成器也用记录调用的替身；Unity Object 模拟已销毁对象的假 null 与销毁 GameObject 连带销毁 Character/Health。

用例通过旧宿主入口执行双局与旧局解绑、新核心/HUD/RunContext 引用一致性、注册 shell 的真实 OnUpdate 早返、绑定后宿主状态变化、所有模式冲突门、预检不消费、船票部分消费失败、Initialize/StartRun/HUD 异常后的退款责任、预扣票取消幂等、竞技场准备与提交顺序、热键暂停门、三个托管 adapter 的迟到返回，以及官方工厂在宿主销毁和下一局启动后的原 state/原宿主回收。官方生成桥按原协议返回 prepared handle 后，由用例模拟外层租约 owner 调用 CleanupOnce；本夹具不宣称完整 late-cleanup sink 调度已覆盖。

该夹具提供 L2 证据，不能代替 Unity 主线程调度、真实资源/背包交付、AI、HUD 视觉或游戏帧耗的 L3 验收。

2026-09-28：竞技场准备新增禁止普通路牌 / 补弹创建的执行断言；垃圾桶仅随普通路牌创建，因此 Mode G 不再出现这些对象。
