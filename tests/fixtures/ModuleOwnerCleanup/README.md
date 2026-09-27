# 模块清理 owner 与 Arena 窄动作执行回归

运行 `python tools/run_runtime_regressions.py --filter ModuleOwnerCleanup`。可设 `BOSSRUSH_FIXTURE_OUT` 将这个夹具的生成与编译输出放到独立目录。

直接链接生产 `SetBonusRuntimeHostBridge.cs`、飞行/逆鳞 bootstrap、`AbilitySystemHelper`、`WavesArenaRuntimeModule_BossAccess.cs` 和 `ModBossPresetLookup`。按唯一签名逐字抽取生产套装注册、退订、停用、反射缓存清理与冲刺取消方法，三 NPC 的 `OnAwake` / `OnDestroy` / `Destroy*NPC`，霜之哀伤完整 bootstrap 类，D 完整性 Tick，以及宿主对应窄桥。`production-sha256.json` 记录所有源文件指纹。

断言覆盖真实静态事件委托个数、重复注册、直接模块销毁与原总链先清理两种路径、Coroutine 停止后不得发生迟到效果、冲刺强制速度复位、NPC 对象与组件销毁及引用清空、重复销毁无重复支付结果释放。Arena 检查 D 与 Arena 消费同一个计时器、阈值与重置、单/多 Boss 列表身份及原顺序、精确预设查找、掉落时刻和数量，以及查询结果不能改 owner 集合。

替身边界：Unity 对象/主线程调度模拟销毁假 null、GameObject 连带组件销毁与协程取消；NPC 生成、资源、存档、管理器内部实现、视觉效果叶子和 D 实际刷怪动作不在本夹具内。套装的注册、退订、Deactivate 方法以及所有受验 OnDestroy 均为生产方法，事件退订没有空替身。事件处理器只记录回调，装备管理器记录调用，因此该夹具证明 owner 分发、取消和清理链，不证明实际装备渲染、战斗效果或存档行为。生产完整代码仍须 Windows 正式构建和 L3 实机验证。
