# 词缀、共享变异与丧尸爆炸生命周期执行回归

运行 `python tools/run_runtime_regressions.py --filter AffixCombat`，需要 .NET 8，不启动游戏。

2026-10-06：逐字抽取 `RefreshDisplayNames` / `StampNameKV` 与键构造方法，经真实运行时订阅的选物事件驱动。覆盖旧名称按槽内实际档位补成分档本地化键、锁定位和未知词缀不变、重复查询无写入、浏览背包装备不激活战斗效果、中英切换、订阅幂等和卸载退订。词缀槽读取与 CustomDataCollection 是受控替身，不读任何玩家存档；官方详情 / 悬浮提示的像素与 Harmony 安装需实机确认。

2026-10-07：三档荆棘通过装备事件激活完整生产分发器；受控宿主把原始 100 点伤害结算为 40 点，核对实际反伤输入为 4 / 7.2 / 11.2，攻击者再次减伤、物理属性、特效递归标记、0.35 秒冷却及名称 Ⅰ / Ⅱ / Ⅲ。宿主防御系数仅用于制造 raw / final 不相等的边界，不是对官方护甲公式的替代实现；真实公式、元素最小 1 点和事件顺序另对照官方 `Health.Hurt`。反向探针把生产荆棘基数改回 damageValue，必须在三档数值断言转红。

此外逐字抽取生产词缀资格与计价方法，验证空槽按数量收费、锁定少一颗熔石、稀有度附加费、极端价格饱和，以及读档自定义武器先补配再判断资格。物品组件/标签/KV 和重铸基础费用是受控宿主替身，价格计算与装备归类本身来自生产源码。

直接链接完整 `AffixRuntimeService.cs`、`AffixRuntimeService_Effects.cs`、`AffixDefinitions.cs`、
`Common/Stats/RuntimeStatModifierTracker.cs`，并以底层 Stat 替身验证三种 ModifierType、失败分支、移除和 owner 隔离；
`ZombieModeRuntimeModule.cs` 中逐字抽取 `DeferExplosion`、`ExplosionNextFrame`、`TriggerDoomPulse` 三个生产方法，
并直接链接 `RunScopedRegistry.cs`。夹具不编译模块中不相关的入场/场景成员，以免为宿主流程引入大批无关游戏类型替身。
由装备变化与 Health 事件驱动真实词缀分发器。
`run.py` 逐字提取共享变异的死亡分发/延迟方法和天降殉爆 OnApply、丧尸奖励爆炸/末日脉冲/区域爆炸入口，
以及 `ZombieModeRunOnlyRecord`；不复写这些算法。

奖励执行迁移后，奖励爆炸与末日脉冲入口也直接编入 `ZombieModeRuntimeModule`，原宿主实例继续作为爆炸协程 owner 传递。
宿主归并后，区域爆炸兼容入口 `DealZombieModeExplosionAreaDamage` 从 `ZombieModeCombatHostBridge.cs` 逐字抽取；实际爆炸业务仍来自 `ZombieModeRuntimeModule_PollutionSkills.cs`。

核对原爆炸不漏目标、追加效果离开原事件栈、来源过滤、天降殉爆保留伤己风险，
以及死亡、换装、切图、context/局失效、宿主关停后旧伤害作废。暂停等待与恢复、三次末日脉冲的
原始几何和伤害、调度失败回滚、反向清理不跳过其它记录、持续触发后不积累已完成协程均有断言。
区域爆炸还检查敌方 fallback 保留、玩家光环/弹道尾迹按效果归因且 fallback 不误伤自己。

Unity 对象、协程、生命、物品 KV、底层 Stat 和 Buff 为替身；词缀 Buff 工厂、变异 context 的
建立与移除、丧尸局有效性/暂停状态、player-only fallback 接收端也为替身，不涵盖模式完整状态机。
协程在 StartCoroutine 时立即推进到首个 yield；
爆炸替身保留官方同一实例复用碰撞数组及 damagedHealth 的行为，用于暴露重入覆写。
实际物理命中、护甲、官方 Buff 叠层与渲染不在本夹具的证明范围内。产物仅写入 Build/affix-combat。
