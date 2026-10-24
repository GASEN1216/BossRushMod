通过 `python tools/run_runtime_regressions.py --filter EnemySpawnRuntime` 运行。

直接链接完整生产 `EnemySpawnCore`、后处理 scheduler 与 profiler，覆盖普通生成的等待帧与异步失效、配装/激活/掉落/提交顺序，真实后处理队列的完成与取消、特殊 Boss 参数与清理 owner、五次随机回退与单例限制、Mode G 托管失败关闭及句柄身份、外部冻结提交、旧回调失败通知与双 owner 隔离。

Unity 对象、官方预设创建、专用 Boss 工厂和配置/掉落动作采用可观测替身；对象销毁模拟 Unity 假 null 与组件连带销毁。UniTask 转接可控制完成时刻的 Task，不模拟 Unity PlayerLoop；无法替代游戏内主线程、地形、AI、资源与表现验证。生产源码 SHA-256 写入聚合回归输出目录。

花冠分派也直接执行生产 SpawnCore，断言专用委托、non-wave / defer 参数、取消清理及跳过通用配装与掉落。内容身份和工厂在本夹具为边界替身，完整花冠 adapter、目录和场景 owner 由 `EclipseBloomRuntime` 另行直接链接验证。

花冠工厂返回的近战倍率用 0.65、2.75 和已经乘 1.8 全局倍率的值覆盖：真实 SpawnCore 必须保留这些值，同时龙裔、龙王和女巫仍调用既有归一化。倍率数值和归一化动作为可观测边界替身，本夹具验证调度策略，不宣称执行了官方 Stat 实现。

2026-09-27 补齐真实 E/F owner 门：保留完整生产 EnemySpawnCore 与 scheduler，另逐字抽取 E/F 生命周期和会话有效性门，CreateCharacterAsync 用受控 Task 暂停。直接 OnDestroy 后放行工厂必须失败、销毁新角色且零 commit、配装、mutator、掉落和随机重试。此处 End/Exit 叶依赖故意不修改 active 状态，防止假清理掩盖错误门；完整清理由 ModeDestroyLifecycle 覆盖。

冠军分派覆盖独立委托、取消清理、non-wave / defer 参数，以及保留工厂已乘全局倍率的近战 Stat。池目录、模式门和多实例波次 owner 另由 `SandstormChampionPool` 直接链接验证。
