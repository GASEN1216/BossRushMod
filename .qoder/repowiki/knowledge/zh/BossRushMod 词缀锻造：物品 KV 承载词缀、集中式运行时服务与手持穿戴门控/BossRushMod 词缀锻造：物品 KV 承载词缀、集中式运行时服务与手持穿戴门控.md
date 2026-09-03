---
kind: gameplay_system
name: BossRushMod 词缀锻造：物品 KV 承载词缀、集中式运行时服务与手持穿戴门控
category: gameplay_system
scope:
    - Integration/AffixForge/**
source_files:
    - Integration/AffixForge/AffixDefinitions.cs
    - Integration/AffixForge/AffixItemData.cs
    - Integration/AffixForge/AffixForgeSystem.cs
    - Integration/AffixForge/AffixForgeStoneConfig.cs
    - Integration/AffixForge/AffixRuntimeService.cs
    - Integration/AffixForge/AffixRuntimeService_Effects.cs
    - Integration/AffixForge/AffixRuntimeTicker.cs
    - Integration/AffixForge/AffixBuffFactory.cs
    - Integration/AffixForge/GoblinAffixForgeInteractable.cs
    - Integration/AffixForge/AffixForgeHostCleanup.cs
    - Integration/Reforge/ReforgeUIManager_AffixForge.cs
    - Integration/Reforge/ReforgeUIManager_AffixForgePanel.cs
    - Config/ConfigAffixForge.cs
    - Localization/AffixForgeLocalization.cs
    - tests/AffixForgeInvariantGuard.py
---

## 1. 系统概述

词缀锻造给枪械 / 近战 / 护甲 / 头盔 / 面罩引入 12 条**行为型**词缀（不是纯数值属性）：
汲血、屠戮、磐石、迅手、荆棘（普通）；殉爆、狂潮、鹰目、灌能（稀有）；
狂血、玻璃炮、死契（诅咒，强力但带代价）。

入口是哥布林 NPC 的新子交互「词缀锻造」，复用现有重铸 UI 骨架，消耗新材料
「词缀熔石」（TypeID 500060）+ 金钱。可锁定单个词缀槽后重随机。

入口组件由 `GoblinReforgeInteractable.EnsureGroupedInteractionOptions` 与其余 6 个
子交互一并 `AddSubInteractable`（子节点名 `AffixForgeOption`）。**这一行是 load-bearing**：
2026-09-01 之前它缺失，导致组件从未被挂载、`ReforgeUIManager.OpenAffixForgeUI`
（唯一开 UI 的入口）没有任何调用点，整个子系统对玩家完全不可达。

熔石有两条产出线（游戏内 Wiki 承诺的口径）：
哥布林商店（`GoblinAffinityConfig.GetShopItems`，好感度 2 级解锁、库存 5）
与 Boss 掉落（`AffixForgeStoneDropService`，8%）。
**注意熔石带 `Special` tag，因此不会进星愿许愿台奖池**（那条池子按 tag 排除 Special），
旧 Wiki 曾写过「许愿台奖池」，已按实现更正。

`affixForgeEnabled` 字段与旧键 `BossRush_AffixForgeEnabled` 仅兼容保留；系统现属恒开默认内容。
关闭/卸载时隐藏入口且不激活行为的 dormant 契约仍保留，
已附着在装备上的词缀数据**保留不丢**。

## 2. 关键文件与职责

| 文件 | 职责 |
| --- | --- |
| `AffixDefinitions.cs` | 12 条词缀的代码数据表 + 顶部集中的数值常量区 |
| `AffixItemData.cs` | `AFX_` KV schema 读写、`IsAffixEligible`、装备类型归类 |
| `AffixForgeSystem.cs` | 槽位数、档位 roll、锁定规则、材料与金钱校验消耗 |
| `AffixRuntimeService.cs` | 集中式静态运行时服务：订阅、context 重建、分发、常驻 modifier |
| `AffixRuntimeService_Effects.cs` | 各词缀的具体效果实现，含死契流失 `TickDrainCore` |
| `AffixRuntimeTicker.cs` | 每帧 tick 宿主，驱动死契流失 |
| `AffixBuffFactory.cs` | 运行时构造 Buff（磐石 / 迅手 / 狂潮）并缓存 |
| `AffixForgeStoneConfig.cs` | 熔石物品 500060（零 bundle，运行时克隆兜底） |
| `AffixForgeStoneDropService.cs` | 熔石的 Boss 掉落轨（8%），并联挂在 `RegisterBossRandomLootTracking` 上，形态照 `PetNestDropService` |
| `ReforgeUIManager_AffixForge*.cs` | `ForgeUIMode` 枚举与词缀模式 UI 差异 |
| `AffixForgeHostCleanup.cs` | `partial ModBehaviour` 的具名销毁清理入口 |

## 3. 架构与设计约定

### 3.1 数据面：词缀只存物品 `Item.Variables` KV

前缀 `AFX_`：`AFX_V`（schema 版本）、`AFX_SLOT_1~3`（值形如 `"Id:tier"`）、
`AFX_LOCK_n`、`AFX_NAME_n`（值是本地化 key，`Display=true`）。

选这条路的原因：`ItemTreeData.FromItem` 会把 `Variables` 逐条深拷贝进官方存档，
`RF_`（重铸）与遗种蛋血脉已经实证这条契约可靠。于是得到两个好处：

- **零 mod 类型进档**——避开 ES3 把 assembly-qualified 类型名写进存档、改名即读不回的老坑；
- **跨存档零恢复代码**——KV 随官方序列化往返，行为在事件时刻按 KV 惰性重建。

`AFX_NAME_n` 设 `Display=true` 是为了借官方 `ItemDetailsDisplay` 免费显示词缀名
（String 型 KV 会自动 `ToPlainText()` 转译，左列名走 `Var_<key>` 本地化）。

行为参数**不进 KV**，由 `AffixDefinitions` 按 `(Id, tier)` 查表——改平衡数值不动存档。

### 3.2 行为面：集中式静态服务（否决了官方 Effect 挂件与 GunSettingExpend）

- **官方 Effect 挂件**：`OnShootAttackTrigger` 等注册到 `item.GetCharacterMainControl()`，
  **NPC 持有同样触发**，直接违反 AGENTS.md 4.12；且组件不进存档，每次实例化都要反射重挂。
- **`GunSettingExpend_*` 组件模式**：只有枪有 `onShootEvent` / `onHurtEnemyEvent`；
  近战命中在 `CheckCollidersInRange` 内直接 `Hurt`，无事件；护甲无对应面。覆盖不全。
- **集中式静态服务（采用）**：只订 3 个静态事件——`Health.OnHurt`、`Health.OnDead`、
  `ItemAgent_Gun.OnMainCharacterShootEvent`——按「当前激活词缀 context」分发。
  订阅数恒定，不随物品数量增长；三类装备统一覆盖；零新增 Harmony。

Buff 型 payload（磐石 / 迅手 / 狂潮）借 `PhantomWitchAssetManager` 的运行时 Buff 构造管线
（`AddComponent<Buff>` + 反射填 Effect / ModifierAction），这才是官方 Effect 管线的正确用法位。

### 3.3 4.12 门控：context 与订阅同生共死

context 只由**主角专属**事件重建：

- `OnMainCharacterChangeHoldItemAgentEvent`（手持变化）
- `OnMainCharacterSlotContentChangedEvent`（Armor / Helmat / FaceMask 三槽）
- `LevelManager.OnAfterLevelInitialized`（读档兜底，全量重扫）

**context 全空即立即退订**。这是硬要求：NPC 身上、仓库里、背包里的闲置装备必须
零订阅、零组件、零协程。分发时再做双保险——武器词缀要求
`fromCharacter == 主角 && fromWeaponItemID == 当前手持 TypeID && !isFromBuffOrEffect`。

常驻型词缀（鹰目 / 狂血 / 玻璃炮）激活时加 stat modifier 并记录，停用时 `RemoveAll`，
幅度**绝不进存档**，天然无残留。

清理路径：离手 / 卸下 / 主角死亡 / 切图 / Mod 卸载，全部立即清 context + 退订 + RemoveAll。

### 3.4 三处防事故设计

- **殉爆重入守卫**：击杀分发带 `_dispatching` 标志，防止爆炸连锁递归。
- **荆棘 / 灌能防循环**：追加伤害标记 `isFromBuffOrEffect`，分发端据此过滤，避免自触发。
- **死契保命**：持续流失**绝不走 `Hurt()`**——致死路径上 `Health.OnDead` 先于 `OnHurt` 派发，
  走 Hurt 会先触发一轮击杀词缀再把玩家打死。因此直接写 `CurrentHealth` 并 clamp 在
  `DrainFloorHealth = 1f`。这是逆鳞留下的教训。

### 3.5 与重铸的前缀双向互斥

`ReforgeSystem.IsRuntimeTrackingVariableKey` 是唯一咽喉点，加了 `AFX_` 前缀之后一处改动同时把
词缀 KV 挡在重铸 roll 池、属性锁定 UI 与 `RF_` 差值同步之外；反向上词缀锻造只识别 `AFX_`，
`RF_` / `ReforgeCount` 天然不可见。

刻意**不复用** `RF_` 前缀：`HasReforgeData` 按 `RF_` 判定，复用会把纯词缀装备拖进
「每次 ReapplyModifiers 全量遍历恢复」的慢路径且永不 MarkAsRestored，语义也混淆。

### 3.6 UI：单管理器 + 模式枚举，不建第二个劫持者

`ItemDecomposeView` 的劫持骨架（打开 / 背包过滤 / 物品选择 / UI 监控 / Cleanup）必须单 owner——
两个静态管理器同时劫持同一个 view 单例会在选择事件订阅与监控器清理上打架。
因此 `ReforgeUIManager` 增加 `ForgeUIMode { Reforge, AffixForge }`（枚举声明在新 partial 文件里，
不改动既有文件），词缀模式的差异全部落在 `_AffixForge*.cs` 两个 partial 里。

## 4. 与其他系统的边界

- **变异词条**：词条是「每局环境规则」，退局即清、不落物品；词缀是「物品资产」。
  两者各自订阅 `Health.OnDead`（多播互不干扰），效果允许叠加。
- **装备能力系统**：那是特定 TypeID 装备的**主动技能**（输入拦截 + StartAction）；
  词缀是任意装备的**被动触发行为**，不复用其反射注册管线。
- **套装 Bonus**：并行的槽位事件消费者，互不感知；穿套装同时带词缀完全允许。
- **元素反应**：明确不做。灌能只用官方 `DamageInfo.elementFactors` 已有的系数伤害，
  不做顺序判定或状态联动（该选题已被否）。

## 5. 契约面（发布后冻结）

- 物品 KV 前缀 `AFX_` 与各字段语义；12 条词缀 id 字符串。
- TypeID `500060`（词缀熔石）；本地化键 `BossRush_AffixForge_*`、`Var_AFX_NAME_n`。
- 词缀图标命名 `Assets/ui/AffixForge/affix_<id>.png`。
- `ReforgeSystem.IsRuntimeTrackingVariableKey` 的 `AFX_` 分支（删掉即数据损坏）。

## 6. 已知未完成项

实机 smoke 未做。必测项：门控四连测（手持 / 切换 / 空手 / 穿卸 → 订阅计数归零）；
旁观 NPC 同 TypeID 武器不触发；仓库闲置零订阅；存读档 KV 完整；
卖店买回 / 快递往返 / 掉落拾回后 KV 保留。

## 7. 2026-08-31 锻造事务收口

结算顺序冻结为“写 KV 并回读 → 扣钱 → 扣熔石”。槽内容、展示名与锁定位各自回读核对；
任一写失败先按快照恢复且不收费。熔石扣除失败会检查现金退款返回值并恢复全部槽，
退款或回滚不完整时向玩家显示“请勿继续操作”，不再把静默 setter 当成功。

## 2026-09-02 熔石掉落订阅的场景边界

兼容分类：COMPAT。`IntegrationRuntimeHooks.OnSceneLoadedIntegrationRuntime` 先调用 `AffixForgeStoneDropService.ClearAllTracking`，避免只有下一只 Boss 生成时才裁剪死引用。终章主动销毁及取消后迟到生成各自在 Destroy 前调用 `ClearBossRandomLootTracking`，覆盖晚于场景回调的登记。自然死亡仍由既有掉落回调结算，不提前解除、不改变掉落概率。第五轮原报告 affix_stone_hooks=0->1；第六轮 `BossRushValidation_20260902_140735_794.log` 已验证 0->0，终章和最终清理均 PASS。主动中止/迟到生成故障注入仍需单独验证。

章节来源：`Integration/IntegrationRuntimeHooks.cs`、`Integration/AffixForge/AffixForgeStoneDropService.cs`、`Campaign/CampaignFinalBoss.cs`。
