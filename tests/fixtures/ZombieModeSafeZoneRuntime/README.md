# 丧尸安全区与敌人空间查询回归

运行 `python tools/run_runtime_regressions.py --filter ZombieModeSafeZoneRuntime`。

直接链接完整生产 `ZombieModeSafeZoneController.cs` 与 `ZombieModeTuning.cs`，逐字抽取模块的敌人收集、AI 缓存、最近目标、引力井、局代次判据、事件记录与裁剪逻辑，以及真实 RunOnlyRecord 和枚举。验证双槽边界、物理弹出与仇恨恢复、普通敌人移除/Boss 保留、恢复登记移除时序、RunOnlyObjects 过滤、缓存失效、两个 owner 隔离、射击事件幂等与清理后重订阅。

Unity 对象、NavMesh、射击事件和安全区视觉更新是宿主替身；假 null 与 GameObject 连带销毁组件按 Unity 语义模拟。恢复登记 Action 没有默认实现，测试在创建模块后显式绑定并观察调用时刻。真实游戏的导航网格、AI 与地图卸载仍需 Windows 构建及 owner 实机验证。
