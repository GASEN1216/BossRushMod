通过 `python tools/run_runtime_regressions.py --filter SandstormChampionPool` 运行。

直接链接完整生产 `SandstormChampionBoss`、`SandstormChampionPoolAdapter` 和配置，逐字抽取控制器的 `OnBeforeSpawnLoot`。覆盖目录幂等、独立实例与波次归属、non-wave 隔离、模式拒绝、生成等待与额外等待期间取消、宿主替换、场景切换、奖励准备失败，以及星阙入箱一次、入箱失败地面兜底和未发奖励回收。原工厂的属性倍率与解除距离休眠调用也在可观测边界上断言。

Unity 对象、官方角色工厂、AI、Stat、物品系统和控制器技能运行采用明确替身。Unity 销毁模拟假 null、组件级联销毁及 `OnDestroy`；工厂和等待帧可受控完成。控制器替身只持有原工厂移交的 preset / reward 和有效性回调，掉落方法为生产正文，不宣称运行了完整战斗或官方尸体箱。完整技能执行由既有 `SandstormCombatRuntime`、`SandstormCombatTiming`、`SandstormChampionPattern` 覆盖，实机碰撞、地形、视觉与可躲性仍需游戏验证。
