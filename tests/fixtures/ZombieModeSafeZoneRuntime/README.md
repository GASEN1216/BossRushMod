# 丧尸安全区与敌人空间查询回归

运行 `python tools/run_runtime_regressions.py --filter ZombieModeSafeZoneRuntime`。

直接链接完整生产 `ZombieModeSafeZoneController.cs` 与 `ZombieModeTuning.cs`，逐字抽取模块的敌人收集、AI 缓存、最近目标、引力井、局代次判据、事件记录与裁剪逻辑，以及真实 RunOnlyRecord 和枚举。验证双槽边界、物理弹出与仇恨恢复、普通敌人移除/Boss 保留、恢复登记移除时序、RunOnlyObjects 过滤、缓存失效、两个 owner 隔离、射击事件幂等与清理后重订阅。

Unity 对象、NavMesh、射击事件和安全区视觉更新是宿主替身；假 null 与 GameObject 连带销毁组件按 Unity 语义模拟。恢复登记 Action 没有默认实现，测试在创建模块后显式绑定并观察调用时刻。真实游戏的导航网格、AI 与地图卸载仍需 Windows 构建及 owner 实机验证。

2026-09-28 增加可达点缺失用例：安全区驱逐候选与可靠回退都失败时必须留在原位，不得使用原始几何坐标。导航判据由 SpawnRuntime 单独执行生产方法，本夹具控制其结果验证驱逐分支。

2026-10-20 增加引力井异常数值回归：逐字执行生产有限性判据与聚怪方法，覆盖 NaN / Infinity 的原点、半径、拉力、敌人位置，以及有限数相乘溢出的半径 / 距离、非法旋转与控制器缺失；无效输入不得写移动状态。正常拉取保留高度、半径、停距与 Boss 豁免，必须经过官方 ECM2.SetPositionAndRotation(..., false)，保留速度和贴地状态。替身单独保存物理位置和速度以暴露只写 Transform 的错误；官方入口抛错时继续处理后续敌人。真实 ECM2.dll 的该接口经只读 IL 核对，不切 active、不暂停贴地；真实碰撞和贴地表现仍属 L3 验收。
