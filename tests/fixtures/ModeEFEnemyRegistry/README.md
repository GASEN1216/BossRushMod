# E/F 共享敌人登记执行回归

运行 `python tools/run_runtime_regressions.py --filter ModeEFEnemyRegistry`。

直接编译完整生产 `Utilities/ModeEFEnemyRegistry.cs`，并从 Mode E 生产文件抽取同阵营掉落策略方法。替身只提供 Unity 对象销毁/假 null、事件、角色和配置状态；不复制登记或策略实现。

覆盖列表顺序与重复登记、阵营身份、缓存复用与失效、销毁对象回收、重复订阅、死亡前退订、实时掉落门、重置后下一局及多 owner 隔离。这里只提供 L2 证据。
