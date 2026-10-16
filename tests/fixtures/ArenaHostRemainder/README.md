通过 `python tools/run_runtime_regressions.py --filter ArenaHostRemainder` 执行。

完整编译 Arena 的 LegacySpawn、LootTemplates、CharacterRegistry 和 EnemyMaintenance 四份生产文件，并抽取同一模块真实 OnAwake/OnDestroy。验证标准生成分支、迟到取消、队伍安全网、既有倍率和回调顺序、归属及范围清理、共享缓存和场景失效、奖励模板及碰撞配置。

Unity 对象、官方角色工厂、异步任务桥、Stats/AI/资源查找是可控制替身。销毁会销毁组件并模拟 Unity 假 null；Recovery 和掉落追踪为记录调用顺序的替身，不替代其它夹具或真实地图实测。生产 SHA-256 随执行结果保存。

同时链接完整 MutatorBossRegenRuntime，验证 Arena 与 D/E/F 分支顺序、原 deltaTime 取用和已销毁角色跳过。

2026-10-07：直接驱动真实 `ContinuousClearEnemiesUntilWaveStart` 的 IEnumerator，从普通待机清场启动，再于报名石启动对话／异步生成时将宿主的 `IsCampaignFinalBossActive` 置 true。创建没有 CurrentBoss、波次或 owned 登记的敌方 ghost，验证它不被删除；终章进行期间连续驱动 605 次，场景扫描和 spawner 停用次数均不增加，暂停不耗尽普通清场循环预算。直接 `ClearEnemiesForBossRush` 同样在刷新脏缓存前尊重终章 owner。结束或失败释放 owner 后，原协程恢复清理普通敌人，主角、遗种巢随从、官方宠物、中立角色和范围外角色仍按既有规则保留，普通波次开始仍会结束协程。

宿主的终章状态、官方 spawner 停用和等待对象是明确替身；清场、缓存刷新、角色筛选和协程执行都来自完整生产文件，销毁真实触发替身 GameObject 与所有所挂组件的 Unity 假 null 语义。没有用测试再实现一套清场判据，也不证明实际女巫工厂、粒子、对白或游戏调度。旧实现在未认领 ghost 存活断言实跑转红，前后日志分别保留于 `Build/runtime-regressions/ArenaHostRemainder-campaign-preclear-before.log` 和 `Build/runtime-regressions/ArenaHostRemainder-campaign-preclear-after.log`。

同轮反向验证在独立稀疏副本分别移除直接 Clear 门、循环暂停门和首次扫描门，均在对应行为断言转红；每次按字节还原并核对 SHA-256，恢复后全部 661 条断言通过。共享生产文件没有被变异。证据汇总为 `Build/runtime-regressions/ArenaHostRemainder-campaign-preclear-mutations.json`，各次日志为同名前缀的 `mutation-direct-gate.log`、`mutation-pause-gate.log`、`mutation-initial-gate.log` 和 `mutation-restored.log`。

2026-10-15：追加真实 Harmony 对 CLR 异步值类型 `MoveNext` 的执行。原无补丁时先按生产 Clear 方法清掉同一角色，再恢复等待，断言真实异步续作因物品树销毁发生 NullReference；应用逐字抽取的生产生成租约后，三条完整生产清场入口在官方宿主替身的真实等待期间保留原对象，真终态后重走原筛选，重复请求幂等。覆盖模块、角色、资产、活动场景实例、角色场景、StopNetwork、新房、房主角色变化和单机后入房的旧请求取消；保留异步原异常和原返回对象。角色销毁真实调用组件 OnDestroy，并销毁物品树和所有组件。此项不声称执行 Unity Mono 的原生角色工厂；官方 API 编译、实际同对象实机异常和修复后的游戏复验分开记录。

此夹具使用本机实际 Harmony DLL 和 Windows .NET Framework；每次创建独立输出目录，编译成功后显式执行本次绝对 TargetPath 并记录 SHA。可设置 `BOSSRUSH_FIXTURE_OUTPUT` 将独立证据写到 Build 指定目录，不覆盖全量 runner 的结果。


2026-10-16：同一真实 OnDestroy 继续抽取真实 CleanupCoopArenaRewards／CancelCoopArenaReward，验证未落地个人奖励视觉 owner 在清表前销毁。这里只提供 reward 状态外壳，不模拟交付成功；完整 receipt、原物保留和异步取消数量由 ArenaCoop 主组执行。
