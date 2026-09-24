# 哥布林 RuntimeModule 回归

运行 `python tools/run_runtime_regressions.py --filter GoblinRuntimeModule`，需要 .NET 8 SDK。

夹具直接编译生产侧 `GoblinNPC.cs` 与 `GoblinNPCRuntimeModuleHostBridge.cs`，覆盖唯一模块持有哥布林实例、控制器与共享 prefab 缓存，旧宿主生成/销毁/召唤入口转发、同名只读实例属性、场景筛选、快递员避让、落地修正、婚礼站桩以及 ZombieMode 共用资源入口。

Unity 对象、NPC 服务、AssetBundle 与场景查询由最小替身提供。夹具验证委托与状态时序，不验证真实 bundle 内容、Animator、Unity 物理与画面；不启动游戏、不访问真实游戏目录或玩家存档。实机生成、移动、婚礼站桩和交互仍需 L3 验收。
