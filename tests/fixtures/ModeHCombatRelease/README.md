# Mode H 发布前租约回归

运行 `python tools/run_runtime_regressions.py --filter ModeHCombatRelease`。

直接链接生产 `ModeHArenaIsolationLease`、`ModeHSpectatorLease`、`ModeHDeathSuppressionRegistry`，验证部分冻结失败回滚、晚到刷怪器的原始 created 状态、场景销毁后的释放、死亡抑制登记在 Unity 假 null 和 Health 缺失时的清理，以及官方 SetTeam / SetPosition 写入后事件抛异常时的玩家状态补偿。

Unity、角色、刷怪器、输入、镜头和地图由内存替身提供。替身模拟 Unity 已销毁对象的假 null 与 GameObject 连带销毁组件；SetTeam / SetPosition 的先写后回调顺序来自官方源码。用例不启动游戏，不读写玩家存档，不能证明游戏内 AI、镜头或地图隔离表现。
