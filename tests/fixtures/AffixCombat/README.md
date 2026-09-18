# 词缀、共享变异与丧尸爆炸生命周期执行回归

运行 `python tools/run_runtime_regressions.py --filter AffixCombat`，需要 .NET 8，不启动游戏。

直接链接完整 `AffixRuntimeService.cs`、`AffixRuntimeService_Effects.cs`、`AffixDefinitions.cs`、
`ZombieModeRuntimeModule.cs` 与 `RunScopedRegistry.cs`。由装备变化与 Health 事件驱动真实词缀分发器。
`run.py` 逐字提取共享变异的死亡分发/延迟方法和天降殉爆 OnApply、丧尸奖励爆炸/末日脉冲/区域爆炸入口，
以及 `ZombieModeRunOnlyRecord`；不复写这些算法。

核对原爆炸不漏目标、追加效果离开原事件栈、来源过滤、天降殉爆保留伤己风险，
以及死亡、换装、切图、context/局失效、宿主关停后旧伤害作废。暂停等待与恢复、三次末日脉冲的
原始几何和伤害、调度失败回滚、反向清理不跳过其它记录、持续触发后不积累已完成协程均有断言。
区域爆炸还检查敌方 fallback 保留、玩家光环/弹道尾迹按效果归因且 fallback 不误伤自己。

Unity 对象、协程、生命、物品 KV、属性和 Buff 为替身；词缀 Buff 工厂/属性追踪、变异 context 的
建立与移除、丧尸局有效性/暂停状态、player-only fallback 接收端也为替身，不涵盖模式完整状态机。
协程在 StartCoroutine 时立即推进到首个 yield；
爆炸替身保留官方同一实例复用碰撞数组及 damagedHealth 的行为，用于暴露重入覆写。
实际物理命中、护甲、官方 Buff 叠层与渲染不在本夹具的证明范围内。产物仅写入 Build/affix-combat。
