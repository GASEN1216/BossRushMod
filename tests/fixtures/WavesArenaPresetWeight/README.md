# WavesArenaPresetWeight

`run.py` extracts the production `PickRandomEnemyForInfiniteHell` method from
`WavesArenaRuntimeModule_EnemyPresets.cs`. Minimal random and catalog stubs test
the empty pool, factor-only fallback, zero-factor indexed fallback, late-wave
health weighting, user factor adjustment, and the number of random draws.

2026-10-06：追加逐字抽取三个独立 Boss 的生产注册方法，以及女巫 `FindPhantomWitchBasePreset`。覆盖已有条目不重复、目录清空重新登记、新宿主不受旧 static 标志影响、目录缺席后恢复；女巫首次资源缺失后重试、有效缓存不重复扫描、缓存对象销毁后改用新对象、保留官方 fallback、全部缓存销毁后正确返回空。

2026-10-08：追加抽取三个 Boss 的生产 `OnAwake` 与 BossFilter 的 `OnStart`、初始化、筛选、作废及重置方法。覆盖基地预热早于模块 owner 绑定时补齐三个 Boss、重复 Awake 幂等、冷池仍交给后续初始化，以及真实配置在 Awake 后载入时，Start 恢复禁用名单与无间炼狱因子。图鉴 / 血脉模块用读取当刻过滤池的观察器替身，断言它们最终收到的目录符合配置；这些测试不覆盖完整目录自身的分类与资源呈现。

目录、Config 常量和 Resources 为受控宿主替身；Unity Object 替身落实已销毁对象与 null 相等，销毁是必经步骤。注册和查找完整方法来自生产源码，`sources.json` 记录输入 SHA-256。没有读取游戏资源或玩家存档，不证明实际 AssetBundle 装载和地图中出场表现。
