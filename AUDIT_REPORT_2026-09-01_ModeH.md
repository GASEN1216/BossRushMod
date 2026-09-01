# 审核报告：f9b83c0 以来新增玩法

审核日期：2026-09-01
审核范围：f9b83c0fa21a3ef03e8abc0941fb71beb3201ba2 之后新增的九个子系统
审核方式：只读审核，未修改任何代码

## 结论摘要

| 维度 | 结论 |
|---|---|
| 性能 | 通过。未发现每帧热路径问题 |
| 实际可用 | **1 个阻断级缺陷**（Mode H 拍铃 + 伤病窗口全链路失效） |
| 功能完整 | 入口接线齐全，9 个 TypeID 均已注册 |
| 可玩性 | 1 处回归（PetNest 归乡冷却误伤），1 处待观察 |

守卫套件：510/510 PASS，NEW-FAIL=0，KNOWN-RED=0（用时 148.6s）。
守卫全绿但仍漏掉了下面的 P0 —— 说明该路径没有守卫覆盖。

---

## P0：Mode H 拍铃与伤病系统全场失效

`ModeH/ModeHCombatControl.cs:804`

```csharp
try { return character.GetComponent<AICharacterController>(); }
```

`AICharacterController` **不在角色根节点上**。官方 `AICharacterController.Init`
（`鸭科夫源码/TeamSoda.Duckov.Core/AICharacterController.cs:74`）执行

```csharp
base.transform.SetParent(this.characterMainControl.transform, false);
```

即 AI 组件被挂为**子对象**。因此根节点上的 `GetComponent` 恒返回 `null`。

### 影响链

`_activeAi = ResolveAi(...)`（`ModeHCombatControl.cs:168`）恒为 null，向下传两处：

1. **拍铃**：`ModeHCommandController.cs:147` 判 `activeAi == null` →
   返回 `command_no_active_fighter`。拍铃是 Mode H 整场比赛中玩家**唯一**的干预手段
   （每场限一次），现在 100% 失败。
2. **伤病/战痕窗口**：`ModeHInjuryAndScarSystem.cs:174` 判 `_ai == null` →
   返回 `window_no_active_fighter`。所有非自结算分量的引擎侧效果全部不生效。

### 为何没被发现

失败只走 DevLog，没有任何玩家可见反馈（`ModeHRuntimeModule_MatchFlow.cs:261`）：

```csharp
ModBehaviour.DevLog("[ModeH] 拍铃被拒绝: " + ...);
return;
```

玩家按下按钮 → 无提示、无音效、无状态变化，且拍铃次数不被消耗。
表现为"按钮没反应"，不会崩溃、不报错、不写日志到玩家可见处。
`command_no_active_fighter` / `window_no_active_fighter` 两个 ID 也没有对应的
本地化条目，即便将来接了 UI 也会显示原始 ID。

### 交叉验证

全仓库 ~20 处 AI 解析，**只有这一处**用根节点 `GetComponent`：

- 官方写法（`CharacterSpawnerRoot.cs:164`）用缓存字段 `c.aiCharacterController`
- 本 mod 其余全部用 `GetComponentInChildren` 或缓存字段，例如
  `ModBehaviour.cs:1239`、`DragonKingBoss.cs:535`、`FrostmourneAction.cs:464`、
  `MutatorDefinitions.cs:282`、`DeathWraithCombatLoadout.cs:50`

修复方向：改用 `character.aiCharacterController`（官方缓存字段，最稳），
或退化为 `GetComponentInChildren<AICharacterController>(true)`。

---

## P1：PetNest 归乡冷却是误修

`FIXES_2026-09-01.md` 记录的"归乡经验泄漏"修复瞄错了目标。

归乡经验是**每次返回固定 +10**，并由 `IsBaseLevel` 把关，**不随时间累积**，
因此原本不存在所谓的刷取泄漏。新加的冷却没有堵住漏洞，却顺带把
同一入口上的**击杀预算耦合**一起挡掉了 —— 正常游玩节奏下反复进出基地时，
本该结算的部分会被冷却静默跳过。

建议：回滚该冷却，或把冷却范围收窄到确实需要限流的分支，不要覆盖击杀预算路径。

---

## 已复核并确认无问题的部分

- **伤害管线**：两处按比例伤害都正确处理了护甲折减顺序与致死重判，
  代码内已写明管线顺序注释。Overcharge 词条设 `ignoreArmor` 未设 `critRate`，
  而 `DamageInfo(main)` 的 `critRate` 默认 `0f`，故结果确定、无隐藏暴击方差。
- **经济与奖励事务**：写法防御性充分，失败模式有注释交代，未见重复发放或丢失。
- **每帧性能**：三个主循环内无 `new List` / 数组分配；`RefreshFireContext`
  无场景查询。全仓新系统仅一处 `FindObjectsOfType`
  （`ModeHArenaIsolationLease.cs:187`），且是每租约一次，非每帧。
  该处已修好一个更严重的旧问题：`player == null` 时改为 fail-closed，
  不再可能把玩家角色本身 Destroy。
- **TypeID**：9 个新 ID 全部通过命名常量注册，无字面量硬编码。
- **入口接线**：`GoblinAffixForgeInteractable` 经 `AddSubInteractable` 接入，
  与 AGENTS.md 记载一致。
- **距离休眠陷阱**：三处直接 `CreateCharacterAsync` 均为装饰性/友方
  （鸭子、商人、宠物随从）。需击杀清波的入侵 Boss 走共享
  `EnemySpawnCore`，该路径已调用 `ReleaseFromPlayerDistanceSleep`。
  PetNest 另用 `setActiveByPlayerDistance = false` 兜底。

## 需留意但未构成缺陷

Mode H 选手在预备区生成时距观战位 **238m**，超出官方 100m 休眠半径
（`distance = 100f`，`FixedUpdate` 中无条件 `SetActive`）。ModeH 未清
`setActiveByPlayerDistance`。由于比赛结束靠 `Health.OnDead` 事件而非轮询
`IsDead`，不会静默卡死；`TryCommit` 会把选手移入场地从而自愈。
观战位距离检查有 30m 下限但无上限——建议补一个上限，使其不超过休眠半径。

---

## 关于既有 AUDIT_2026-09-01.md

该文件声称"91 guards, 0 FAIL"（实际 510），并引用了五个**代码中不存在**的符号：
`PetNestDoorInteractable`、`AffixForgeBridge`、`CodexBookInteractable`、
`PetNestExpedition`、`_combatProfileCache`。图鉴是道具而非建筑，没有 interactable 类。
本次审核未采信该文件，全部结论直接取自源码。建议废弃或重写它。
