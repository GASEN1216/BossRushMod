# Mode H 入场时序隔离回归

通过 `python tools/run_runtime_regressions.py --filter ModeHSceneEntry` 执行，不启动游戏、不读取玩家存档。

每次构建逐字抽取当前生产 `OnSceneLoaded`、目标匹配、就绪调度/取消、两帧稳定判据、新赛季开局、续赛调度/完成和整个 Legacy custom 传送协程。只为独立宿主适配 `override` / `partial` 声明；续赛方法的返回声明由 UniTask 改为 Task，方法体不变。源码摘要写入 `Build/modeh-scene-entry/source-hashes.json`。

测试替身只控制 Unity 事件/帧、官方场景加载、角色/物理、原生租约与认证/持久化边界。清除 typed intent 模拟认证后正式消费；退款边界记录调用次数，不伪造玩家物品或写入存档。覆盖九图名、先 sceneLoaded 后角色创建/官方传送、Legacy 已在等待时切为 H、认证已消费 intent 后 owner 防护、替换等待/迟到旧枚举器、取消/场景/槽代数变化、销毁角色与宿主、暂停/超时、续赛等待和 Storm B0 / 冷库二次子场景传送。其余资产回归继续由 ModeHReviewFixes / ModeHRecoverySecondReview 覆盖。

这是 L2 时序回归，不证明真实 NavMesh、摄像机视野、Unity 协程/物理表现或九图完整可玩。

官方最终落点专门覆盖：当前 `InitLevel` 在 `LevelInited=true` 后仍等待 0.25 秒，执行
`mainCharacter.SetPosition(startPos)` 后才设 `AfterInit=true`。九张地图各模拟 15 个 60 Hz 帧，
期间即使主角/活动场景/子场景加载都就绪也不能取得租约；最终官方搬人及 AfterInit 后才允许。
全局 AfterInit 已真而后续子场景仍在加载的情况另测，避免两个门彼此掩盖。
