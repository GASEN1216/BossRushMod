# 战斗审计执行回归

`python tools/run_runtime_regressions.py --filter AuditCombatSeptember`

生成器每次从当前生产文件逐字提取龙套伤害监听、飞行动作更新、孩儿护我异步/协程、火焰池预热、女巫诅咒观察和特效借还方法；印记与特效性能策略整文件链接。火焰池仅将 `async UniTaskVoid` 返回类型适配为 `Task`，方法体不改写。生成代码及源码 SHA-256 写入 `Build/runtime-regressions/AuditCombatSeptember/`。

断言涵盖共享元素列表不变、80% 火焰回补、零体力下降及松手停止、连续物理步维持目标速度、过期印记重新起算、迟到子实体回收、清池后重新预热、旧 owner 不清理新租用者、持续领域不触发直接命中，以及诅咒首次概率只有官方一次判定。诅咒减速用本机真实 `ItemStatsSystem.Stat/Modifier` 在多组基础速度与 1/2/3 层上执行。

亡魂夹具逐字抽取生产创建、登记、清理和槽位回调，在五个异步等待点分别执行同槽重载，再启动相同 raidID 的后继请求；验证旧结果销毁、旧 finally 不释放新请求，以及角色创建和装备恢复异常时保留的物品树得到回收。它只用内存里的槽号替身，不访问玩家存档。

Unity 物理、输入、对象与协程调度使用最小宿主替身；特效替身模拟销毁对象等同 null 和 GameObject 销毁组件，实际渲染、Buff 模板 Awake、声音与主线程帧耗不在此回归证明范围。迟到角色 factory 故意忽略取消门，验证消费者自身仍能兜底回收。测试不启动游戏、不读取存档。

2026-10-13：Realm 子工程逐字执行本人镰刀 DealRealmDamage、QueryRealmTargets 和 IsEnemyReceiver，覆盖 90 敌人 / 多碰撞器去重、实际友军 / 道具 / 死亡 / 2D 距离过滤、增容后的查询复用、1050 碰撞器时的完整 fallback、16 个不同施法者伤害归属与原伤害 / 元素 / 诅咒值。Physics 只提供受控命中数组，不证明 Unity 实际广相查询或网络伤害裁决。饱和早退、fallback 截断分别在副本转红并按字节还原。各子工程改为执行本次 fresh TargetPath，输出 SHA-256，避免历史默认 bin 假绿。

孩儿护我子工程同时原样抽取龙皇 `HasCombatAuthority` 和共享 `HasEntityAuthority`，使用真实 `DragonKingAbilityController` 类型。网络连接、房间、主客角色和组件查询是可变宿主边界；断线、换房和失去房主身份后的迟到子实体必须回收，镜像从出生起禁止召唤且断线后不能升级，新的单机实体仍正常召唤。实体标记的安装及 Unity 网络行为由其他生产链接回归和实机证据承担。
