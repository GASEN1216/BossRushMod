# ModeGEntryOwners

执行入口：`python tools/run_runtime_regressions.py --filter ModeGEntryOwners`。不启动游戏，不读取玩家存档；产物与生产源码 SHA-256 写入 `Build/runtime-regressions/ModeGEntryOwners/`。

完整编译 `ModeGEntry.cs`、`ModeGRuntimeBridge.cs`、`ModeGSpawnTransaction.cs`、`ModeGEntryRuntimeServices.cs` 和 `ModeGEntryHostBridge.cs`，仅把 `UniTask<T>` 换成 `Task<T>`。同时链接完整生产 `ModeGRunState`、`ModeGStateModel`（含 RunContext、门控与资格判据）、`ManagedBossSpawnContracts`，并逐字抽取 `ModeGBossSnapshot` 和核心的 `Update` / `OnUpdate`。入口业务、宿主委托绑定、退款责任转移、原 tick 顺序、生成 staging 登记与 await 前后的归属判断均执行生产实现。

`ModeGRuntimeModule.Initialize`、`StartRun`、`ArmStartupRefund`、`End`、`Dispose` 和 `DriveCore` 是可控外部依赖替身，用来观察入口何时绑定真实 RunState/RunContext、是否创建独立核心、何时把退款责任交给核心，以及是否按序清理。它们不证明核心九波战斗或核心实际退款算法正确；生产战斗、奖励与持久化另由 `ModeGCombat` 覆盖。HUD、玩家背包、持久化、地图资源和共享生成器也用记录调用的替身；Unity Object 模拟已销毁对象的假 null 与销毁 GameObject 连带销毁 Character/Health。

用例通过旧宿主入口执行双局与旧局解绑、新核心/HUD/RunContext 引用一致性、注册 shell 的真实 OnUpdate 早返、绑定后宿主状态变化、所有模式冲突门、预检不消费、船票部分消费失败、Initialize/StartRun/HUD 异常后的退款责任、预扣票取消幂等、竞技场准备与提交顺序、热键暂停门、三个托管 adapter 的迟到返回，以及官方工厂在宿主销毁和下一局启动后的原 state/原宿主回收。官方生成桥按原协议返回 prepared handle 后，由用例模拟外层租约 owner 调用 CleanupOnce；本夹具不宣称完整 late-cleanup sink 调度已覆盖。

该夹具提供 L2 证据，不能代替 Unity 主线程调度、真实资源/背包交付、AI、HUD 视觉或游戏帧耗的 L3 验收。

2026-10-15 round10：网络 AI 查询使用可变的原生角色索引，通过逐字抽取的 `DriveCoopGuest` 与真实生成事务提交原生副本，然后断言原 Health 和场景代次已冻结。没有在调用前替实现填好缓存；去掉生产提交后的冻结写入会使执行回归转红。死亡后的真实完整结案另由 ModeGCombat 验证。

2026-09-28：竞技场准备新增禁止普通路牌 / 补弹创建的执行断言；垃圾桶仅随普通路牌创建，因此 Mode G 不再出现这些对象。

2026-10-13 装配追加：AssemblyOrder 逐字提取并执行完整 production RegisterModules 和 RuntimeModuleHost，真实 Common Co-op OnAwake 建立门面后，再执行真实 G OnAwake 绑定稳定 owner；频道、消费者注册、消息 codec 为生产实现。其它模块生命周期是记录替身，门面和 G 的实际先后顺序不得被替身提前填值掩盖。当前 233 条断言通过；稀疏副本把 G 初始化放回注册期会在真实“Coop runtime must be registered before consumers”异常处失败。

2026-10-13 准入追加：移除外层恒真 ApplyCoopCombat 替身，逐字执行真实外层 Apply / DeliverOrdered / Pending / Finish，以及实际资产冻结、准入、内层 Apply 和完整 DriveCoopGuest。实际频道、PersonalDelivery 和 codec 编码后穿过受控队列；丢包注入解开真实嵌套收据，不只检查顶层 DTO。各模拟原生进程在消息回调时恢复对应静态 owner。

290 条断言包括未资格旧事实、补票、Start 后 admitted 命令在途的新伤害、基线丢失/重试、重复/错误基线、后续缺序号、晚加入资产首次冻结、旧局债务不挡新局、原 owner 终局墓碑、未知旧局不确认、迟到旧波次及本波已死 AI、切槽付款前拒绝、过期快照下已付款后的可靠 denied 和 120 秒准入超时。退款执行真实 RefundStartupPaymentOnTechnicalFailure、DisarmStartupRefund 和原入口退款转发；End 的总体清理/状态推进依然是受控依赖，未声称完整生产 End 已在此夹具执行。客端完整 Drive 的用例已显式准备真实 SpawnTransaction 的槽位状态；BeginCoopGuestWave、真实原生角色和资源工厂仍为外围依赖，未通过恒有 Boss 避开缺失 AI。

新房间和房主新 run 的快照通过真实 ApplyCoopWorld 结束旧核心，再由真实 Tick/付款入口进入新局；空 RunId 握手保留旧核心。ModeGCombat 另执行真实 CanContinueRun、资产/房间判据和冻结角色属性，验证原客端断线后门面即使返回单机世界权限，也不能获得本局生成权限；原无 COOP 单机仍正常。

2026-10-15 原 host Stop 补审：真实付款入口消费票与信物后，原 host 停止网络，在下一次 Tick 前即拒绝世界/个人战斗资格；入口 Tick 关闭原局。End 边界调用真实 RefundStartupPaymentOnTechnicalFailure 和 FinishCoopRun，首战前原资产的两件入场物各退一次，重复 Tick 不再退；同时换槽则真实 DisarmStartupRefund 阻止写入新资产。完整 End 的 Unity 清理仍未在此边界执行。当前 301 断言。

2026-10-15 实际场景缓存补审：当前 306 断言。完整提取运行时 `ApplyCoopWorld`，替换原同名边界方法；先通过它冻结真实快照 token，再在保持同房间与数字 generation 的情况下开始旅行或更换实际 token。完整 DriveCoopGuest 不得把新同 ID 原生角色提交进旧生成事务；当前新快照到来后仍提交完整角色。真实入口 Tick 在未 bound 或旧 token 期间不消费船票/信物，个人 delivery 继续 Tick。频道、codec 和生成事务是生产实现；实际 Core/token 由模拟原生端明确提供，未以此声称游戏时序已实测。
