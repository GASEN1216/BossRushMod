# 天空岛战斗目标与爆炸缓冲执行回归

通过聚合入口运行：`python tools/run_runtime_regressions.py --filter SkyIslandCombatRuntime`。
需要 Windows .NET Framework、.NET SDK，以及 `BOSSRUSH_HARMONY_DLL` 和 `BOSSRUSH_GAME_MANAGED`。
后者应指向游戏 Managed 副本；只读取程序集元数据，不加载游戏场景或玩家存档。

运行器每次创建独立目录，用 Windows Framework 引用编译新 EXE，核对并记录 SHA-256 后执行该文件。
没有使用历史 bin 或 `dotnet run` 的隐式目标。

生产侧直接链接 `SkyIslandExplosionBufferPatch.cs`、`SkyIslandExplosionObstaclePatch.cs` 与档次枚举；
逐字抽取 `SkyIslandEnemyTiers.TraceDistance / ApplyAi`。官方侧逐字抽取现有反编译源中的
`AICharacterController.Update`、`AIMainBrain.DoSearch`、`SearchEnemyAround.OnSearchFinished`、
`SetAim.OnExecute`、`ExplosionManager.CreateExplosion / CheckObsticle`，每个方法的 SHA 留在本次输出。
另外对实际官方 DLL 检查字段类型、方法签名和相关 IL，避免只依赖旧文本猜测 API。

真实 Harmony 安装生产 Prefix / Finalizer 并执行官方爆炸方法，不由测试手工调用补丁。
覆盖首用与已有状态、场景句柄门、密集目标扩容、原缓冲引用与内容恢复、同一 manager 转到其他场景、
阵营过滤、Health 去重、冲刺豁免、遮挡、simple health、原特效单次发生、异常、嵌套调用和缓冲复用。
修复前的官方八槽反例也会执行，明确显示 9 个有效重叠目标只收到 8 次伤害。

选敌用官方 DoSearch → 黑板结果回写 → AI Update → 官方 SetAim 入口，验证自动组保留敌对 NPC、
普通狼阵营可保留反击对象、同一自然感知仍能找到玩家并进入攻击目标，手动挑战保留强制追踪。
`SkyIslandEncounters` 另一夹具负责真实生成入口的策略参数、阵营、死亡清场与距离休眠配置。

边界：Unity 对象、物理查询集合/遮挡和角色字段是显式替身。物理替身只实现 NonAlloc 的容量截断合同，
不模拟 PhysX 的真实顺序或场景网格；也没有实际扣血、枪械开火、伤害表现或帧耗采样。
这些是 L2 数据流与生命周期证据，正常玩家路线的两派交战、密集场命中和性能仍需 L3。

2026-09-28 的实际资源取证在本轮 Build 报告中：`AIControllerTemplate` 黑板将 SearchedEnemy
绑定到 AICharacterController.searchedEnemy；Patrol_Normal 搜索回写同一变量；Combat_Normal
只在目标被遮挡时重新搜索；Combat_Attack_Nomral 的 SetAim 读取同一变量。
