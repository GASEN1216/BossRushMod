# ZombieModeSpawnRuntime

通过 `python tools/run_runtime_regressions.py --filter ZombieModeSpawnRuntime` 运行。

抽取当前普通/Boss 异步生成、暂停等待、名额预留释放、暂停候选销毁和敌对性准备的生产方法。控制共享 SpawnEnemyCore 完成回调与下一帧恢复，验证名额上限、阶段失效、暂停期间不生成、候选销毁后重试、重复失败只释放一次，以及随机抽取、属性准备、计数、安全区弹出、恢复锚点登记和 Boss 生命周期的顺序与 flags。

共享 SpawnEnemyCore、变体抽取、marker 注册、属性调参和场景有效性是可记录替身；UniTask 由 Task builder 提供确定性等待，Unity 替身模拟销毁后等于 null 及 GameObject 连带组件销毁。此夹具不替代真实 NavMesh、Unity 调度或游戏内敌人生成；共享 SpawnEnemyCore 内部的提交门控由其专门夹具验证。

2026-09-28 增加真实落点校验及区域伤害方法：通过受控 NavMesh 返回值验证采样失败、不连通、跨楼层、完整 XYZ 落点及最终普通 / Boss 生成拒绝；伤害接收器记录实际 DamageInfo，验证圈内 Poison Buff 与 100% 触发概率、圈外 / 暂停 / 旧 run 拒绝、普通区域不附毒。NavMesh 拓扑与官方 Buff 实际显示 / 抗性仍由宿主替身隔离，结论为 L2。

2026-10-06：抽取最近可达点选择与 Boss 落点方法，使用全图远点在前、近点在后的候选表验证 Boss 选择近点；拥挤点使用现有可达环，环不可用仍保留可达点。Boss 与普通丧尸使用同一追踪距离。候选环返回值是替身，具体地形及到达速度需实机。

携崽扩展：逐字抽取死亡处理和原角色隔离谓词，验证宠物身份即使临时队伍不同也保留、宠物自身死亡不扣敌人数和掉分，宠物击杀普通丧尸／Boss 各推进一次且重复死亡不会重复计数；奖励特效与最终结算由记录替身隔离。
