# Mode H 市场与计划输入审计

该夹具每次运行都会复制并编译当前生产源码，验证：

- 第 4 场胜利的最后一名实际死亡敌军才可写入市场资格快照；
- 市场报价重新检查场次、胜负、认证来源、现有合同和过期身份；
- 计划生成把合同 profile 映射为 archetype，并排除本季五席及当前合同选手；
- 生产入口使用的两个派生集合（通过逐字提取的 helper）可交给 `ModeHEncounterPlanner.TryBuildPlan`。

执行部分直接运行 `ModeHCombatTelemetry`、`ModeHTransferMarket`、`ModeHDraftController` 和
`ModeHEncounterPlanner`。使用仓库 `Assets/Data/ModeH/BossProfiles.json` 与
`ThreatPlans.json`，Unity、Health、认证注册表和存档 I/O 由最小替身提供。
100 个 runSeed × 六场计划均在现有 2 次自动技术重试内成功构造；夹具另测第 4 场真实死亡到
战报、报价、接受，以及迟到死亡、重复接受、失败比赛、认证撤销、冲突快照和撕票等边界。

这不是 Unity、Health、AI、场景协程、官方存档或玩家仓库的实机验证；完整六场实际战斗、UI
交互与真实押品仍需人工 smoke。

签约可行性门：第二次选秀点击在写入 Season.contract 之前，调用 `CanConstructFullSeason`，用 `ModeHEncounterPlanner` 当前算法和现有每场技术重试上限预构造六场。失败只显示更换替补提示，不推进 `RosterLocked`，不触碰玩家资产；这是对认证池过小或威胁走廊无解的前置拒绝。该检查仅发生在选秀/整备 UI 回调，不进入战斗热路径。

转会可行性门：夹具逐字提取 `AcceptTransferOffer`，验证第 4 场转会在提交前预构造第 5、6 场；完整剩余赛程接受并关闭窗口，八候选认证池的无解转会保留 Pending 且不修改合同。拒绝时生产选秀页已有“重新选择主将”动作，清除 `_pendingContractMainId` 后可重新选两席。

## 2026-09-18 扩展：内容与擂台实战

额外完整编译 OddsController、VirtualStakeController、LoadoutEditing、ModeHLocalization、
ModeHMatchRules 和共享 RuntimeStatModifierTracker；准备/过滤/口令选择等入口从生产逐字提取。
覆盖不可用首发口令、护甲伤病过滤、分页/说明/过期回调、核心身份与入场计时、真实伤势侦察与
公开赔率，以及六类擂台规则的双边属性、区域进出、伤害宽限、增援独立窗口、治疗递归门和清理。
Unity/官方 Stat 的语义由 MatchRulesAudit 提供替身，包含销毁即 null 与父物体销毁子组件。
该夹具不证明渲染可见、官方物理/伤害事件及真实 AI；这些仍需 L3。权重与认证矩阵也使用受控替身。
