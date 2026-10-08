# Legacy 竞技场入场归属回归

直接链接完整 `IntegrationRuntimeModule_ArenaEntry`、`ModeDRuntimeModule` 与生命周期源码；逐字提取正式 DEMO setup、模式判定、就绪等待桥、宿主转发和 Integration 的 Awake / Destroy / 官方就绪查询。实跑 Normal / D / E / F / G / H 分支，D 的 active 状态与开局装备由真实生产逻辑执行。官方库存和其他模式启动、Unity 场景 / 角色 / 协程调度、NPC / UI / 物品池为边界替身。

覆盖原图卸载、官方开始切图、同名不同 handle、等待和延迟启动期间换角色、死亡、后继请求、30 秒超时、模块销毁、晚初始化角色；原图卸载时必经销毁旧角色 GameObject 与组件，并模拟 Unity 销毁对象等同 null。Mode E 的嵌套验证及异步路牌补建均验证取消和反序释放，另验证普通子 IEnumerator 递归执行，以及 CustomYieldInstruction / Coroutine 保留原生等待语义。

运行 `python tools/run_runtime_regressions.py --filter LegacyArenaEntry`。每次生成独立构建目录，只执行本次构建返回的 TargetPath；日志保存执行对象 SHA-256，`production-sha256.json` 保存生产源码哈希。此夹具提供 L1 / L2，不能证明 Unity 实际调度或天空岛实机游玩。
