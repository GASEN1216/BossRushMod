通过 `python tools/run_runtime_regressions.py --filter EnemyRecoveryRuntime` 运行。

完整链接生产恢复监控、Boss 生成后/延迟位置校验，以及丧尸和竞技场的敌人枚举与候选点适配。验证一秒门控、模式优先级、连续下坠/静止/冷却边界、Unity 销毁、保留受伤血量、清零物理速度、阵营仇恨策略、远距普通丧尸与 Boss 区分、marker/AI 复用、E 候选数组身份缓存及重置、有效丧尸点优先和延迟协程等待。

Unity 对象、时钟、物理/导航采样、角色传送/Health/AI 和模式状态是可控制替身；未替代真实地形、Unity PlayerLoop、官方传送副作用或实际性能采样。所有环境委托在夹具 owner 装配时显式绑定。生产 SHA-256 位于聚合回归输出目录。
