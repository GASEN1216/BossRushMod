# ZombieModeHostOwners

通过 `python tools/run_runtime_regressions.py --filter ZombieModeHostOwners` 运行，不启动游戏，不读写玩家存档。

生产逻辑：完整链接 `ZombieModeRuntimeModule_HostLifecycle.cs` 与 `RunScopedRegistry.cs`；从现码逐字抽取宿主状态属性、Attach/Detach、清理与协程入口、等待场景的 iterator，以及 RuntimeModule 的 Awake/Destroy、AdoptHostState、RunId 门控、RunOnly 登记、失效与反向清理；真实使用阶段枚举、PhaseGuards、EntryTransaction 和 RunOnlyRecord。抽取来源 SHA-256 记录在 Build 输出。

用例检查未 Awake 构造的备用对象、按引用迁交和回收、错误 owner 与再注册、所有生命周期阶段的场景清理门、退款与 RunOnly 清理顺序、资源已结算分支、未注册宿主的旧 no-op 依赖行为、协程登记前后顺序、失效局与错误场景、module owner 清空和 Unity 宿主销毁后的原宿主回收，以及 iterator 的 Current 与子 IEnumerator 透传。

合并新增的 Boss 纹章缓存也抽取真实 `ZombieModeBossVisuals.ResetStaticCaches` 和五槽字段，经真实模块 `OnDestroy` 执行；覆盖全部纹理释放、Unity 假 null 引用清空、重复销毁与未挂宿主的模块。纹理替身只记录 Destroy 与假 null，实际 GPU 资源释放仍属 L3。

替身边界：Unity 对象/协程和当前场景、债务系统、退款与玩法清理的外部动作、RunState 的大量玩法字段及 ClearRuntime。替身记录外部调用，不重写本次移动的生命周期算法；Unity Object 模拟销毁后等同 null，GameObject 销毁会连带组件。此夹具是 L2 证据，不能替代游戏内场景、背包、退款、HUD、撤离与实际协程调度验收。
