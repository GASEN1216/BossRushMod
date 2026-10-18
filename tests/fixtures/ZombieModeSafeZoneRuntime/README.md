# 丧尸安全区与敌人空间查询回归

运行 `python tools/run_runtime_regressions.py --filter ZombieModeSafeZoneRuntime`。

直接链接完整生产 `ZombieModeSafeZoneController.cs` 与 `ZombieModeTuning.cs`，逐字抽取模块的敌人收集、AI 缓存、最近目标、引力井、局代次判据、事件记录与裁剪逻辑，以及真实 RunOnlyRecord 和枚举。验证双槽边界、物理弹出与仇恨恢复、普通敌人移除/Boss 保留、恢复登记移除时序、RunOnlyObjects 过滤、缓存失效、两个 owner 隔离、射击事件幂等与清理后重订阅。

Unity 对象、NavMesh、射击事件和安全区视觉更新是宿主替身；假 null 与 GameObject 连带销毁组件按 Unity 语义模拟。恢复登记 Action 没有默认实现，测试在创建模块后显式绑定并观察调用时刻。真实游戏的导航网格、AI 与地图卸载仍需 Windows 构建及 owner 实机验证。

2026-09-28 增加可达点缺失用例：安全区驱逐候选与可靠回退都失败时必须留在原位，不得使用原始几何坐标。导航判据由 SpawnRuntime 单独执行生产方法，本夹具控制其结果验证驱逐分支。
# 2026-10-18 联机补充

完整安全区生产文件及实际掉落到期方法新增验证：房主角色不可用时，准备期仍清理已准入活客人附近的普通敌人；未准入观察者不解除安全区保护，也不保护其附近过期掉落。已准入客人五米内散落物保留，强制换波清理规则不变。参与资产/房间租约判定在本夹具为显式边界，完整生产谓词由 ZombieCoopFlow 验证；Unity 删除、所有权和实际距离分支在这里执行。

仇恨释放另验证房主身体为空时，实际安全区释放路径及 AI 目标写入方法仍恢复追击活客人；目标选择为显式绑定适配器，队员筛选由 ZombieCoopFlow 执行实际生产方法。将 Main 外层门恢复会令此用例转红。
