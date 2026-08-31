---
kind: gameplay_system
name: BossRushMod 竞技场后山：官方种植系统接入、战利品登记簿与点唱机战歌
category: gameplay_system
scope:
    - Integration/BackMountain/**
source_files:
    - Integration/BackMountain/BackMountainConfig.cs
    - Integration/BackMountain/BackMountainUnlocks.cs
    - Integration/BackMountain/BackMountainItems.cs
    - Integration/BackMountain/GardenSeedInjector.cs
    - Integration/BackMountain/RaidMealUsageBehavior.cs
    - Integration/BackMountain/RaidMealService.cs
    - Integration/BackMountain/JukeboxTrackInjector.cs
    - Integration/BackMountain/BackMountainSeedDrops.cs
    - Integration/BackMountain/ShowcaseService.cs
    - Integration/BackMountain/ShowcaseUI.cs
    - Integration/BackMountain/ShowcaseInteractable.cs
    - Integration/BackMountain/ShowcaseBuildingBuilder.cs
    - Integration/BackMountain/BackMountainRuntimeModule.cs
    - Config/ConfigBackMountain.cs
    - Localization/BackMountainLocalization.cs
    - tests/BackMountainStructureGuard.py
---

## 1. 系统概述

竞技场后山是「两局之间做点别的」的基地侧循环，三个设施由鸭王征程的章节 token
逐个解锁（第 1 章→菜地、第 2 章→展示柜、第 3 章→点唱机）。

它是全 mod 第一个**大量复用官方现成闭环**的系统：菜地直接接官方种植，
点唱机直接往官方曲目列表追加，两处都是 public 字段/静态查找，零 Harmony。

「后山」是叙事包装，不做新场景——设施就摆在基地。

总开关 `backMountainEnabled` 属于**默认内容，恒为开启**（见
`Config/ConfigContentSystemSwitches.cs`）；而 `BossRush_BackMountainUnlockAll`
是**旋钮**不是内容开关，照常注册进 ModConfig UI，给的是「不跟剧情走、直接玩后山」
这个玩家偏好，默认 false。两者性质不同，别一起撤掉。

## 2. 关键文件与职责

| 文件 | 职责 |
| --- | --- |
| `BackMountainConfig.cs` | 常量单点：建筑 ID、两个存档 key、cropID 前缀（四个冻结契约）、设施↔章节映射、展示柜格数与造价 |
| `BackMountainUnlocks.cs` | 解锁读侧：现查征程契约（**不缓存**）、幂等订阅实时事件、UnlockAll 旁路 |
| `BackMountainItems.cs` | 六件物品（种子×3 + 出击餐×3）定义表、克隆兜底注册、本地化、出击餐使用行为挂载 |
| `GardenSeedInjector.cs` | 往官方 `CropDatabase.entries/seedInfos` 幂等注入作物与种子；棘轮策略 |
| `RaidMealService.cs` | 出击餐：食用登记（存档）→ 下局挂 Modifier → 局末 `RemoveAll` |
| `RaidMealUsageBehavior.cs` | `UsageBehavior` 子类，只在基地可用 |
| `JukeboxTrackInjector.cs` | 往官方 `BaseBGMSelector.entries` 幂等追加 mod 战歌 |
| `BackMountainSeedDrops.cs` | `partial ModBehaviour`：三个自定义 Boss 的种子掉落，接在掉落箱协程末尾 |
| `ShowcaseService.cs` | 战利品登记簿：TypeID 集合持久化 + 品质加成计算 + Modifier 挂/摘 |
| `ShowcaseUI.cs` / `ShowcaseInteractable.cs` / `ShowcaseBuildingBuilder.cs` | 面板 / 交互 / 建筑注入 |
| `BackMountainRuntimeModule.cs` | 宿主回调唯一落点：dormant 契约、幂等 bootstrap、场景级设施刷新 |

## 3. 架构与设计约定

### 3.1 菜地：官方种植系统的接入成本极低

官方 `CropDatabase` 的两张表 `entries`（`List<CropInfo>`）与 `seedInfos`
（`List<SeedInfo>`）都是 public，可以运行时直接追加。注入后官方种植 UI 会自动把
我们的种子列进可选列表（`GardenViewCropSelector` 用 `CropDatabase.IsSeed` 过滤背包）。

**为什么不需要植物模型**：`Crop.RefreshDisplayInstance` 用的是
`ItemAssetsCollection.GetPrefab(resultNormal)` 的 ItemGraphic——作物在地里长什么样，
就是它产出物品的样子。所以食材物品注册好了，作物外观自动就有了。
这是整个菜地方案成本极低的根本原因。

**顺序硬约束 + 棘轮策略**：`Crop.RefreshDisplayInstance` 对 `GetPrefab` 的结果
**没有空检查**，产物 TypeID 未注册时会在官方代码里 NRE。因此食材物品必须先于任何
菜园场景加载完成注册；且一旦玩家种下过 mod 作物，存档里就有了引用，此后注入不再
受开关回退影响（棘轮标记 `BossRush_BackMountain_GardenRatchet_v1`），把破档面缩到
「卸载整个 mod」——即使那样也不会崩，`Crop.Initialize` 找不到 CropInfo 会早退。

**为什么占六个 TypeID 而不是像遗种蛋那样一个号 + KV**：官方按 `SeedInfo.itemTypeID`
认种子、按 `CropInfo.resultNormal` 发产物，两边都是裸 int，认不了 KV。

### 3.2 出击餐：官方 Buff 不跨场景

`CharacterBuffManager` 没有存档，角色对象每场景重建，在基地吃下的 Buff 进图就没了。
所以「出击前吃、下一局生效」必须落存档：食用时登记一条待生效记录，下一局
`OnLevelInitialized`（官方 `BuildingEffect` 给建筑加成用的同一时机）再挂 Modifier，
清理走 `RuntimeStatModifierTracker.RemoveAll`。同一时间只保留一条，后吃覆盖先吃。

### 3.3 展示柜是「登记簿」而不是储物柜

最初设计是「存进去才有加成」，但那会逼玩家在「留着这把传说武器」和「换几个百分点属性」
之间二选一。绝大多数人会选前者，于是整个系统没人用。改成登记制之后，玩家带着战利品来
登记一次，东西照样归自己，加成来自「你确实打到过它」。收集与炫耀的驱动力保留了，
机会成本没了。附带好处：不需要动任何物品消耗/归还的 API，也就没有丢件的风险。

加成按品质给：每高于 Q4 一级 +0.5% 最大生命，八格全满额外 +5%，上限约 +21%。

### 3.4 解锁状态不得缓存

征程侧按契约在**读档装载 token 时不发事件**（那不是新授予）。若后山把查询结果缓存
下来，读档后就永远停在「启动瞬间的答案」上——玩家上次通关解锁的菜地，重进游戏后消失。
正确做法是每次现查（O(1) 哈希），并在每次场景加载时重新评估。
由 `tests/BackMountainStructureGuard.py` 断言。

### 3.5 注册顺序

后山 bootstrap 要订阅征程的解锁事件，而 host 按注册顺序回调，
因此 `BossRushRuntimeModuleRegistration` 里**征程必须排在后山之前**。

## 4. 物品与 TypeID

| TypeID | 内部名 | 中文 | 说明 |
| --- | --- | --- | --- |
| 500062 | DragonSeed | 龙裔之种 | 龙裔遗族掉落（25%），种出龙息果 |
| 500063 | EmberSeed | 龙皇焰种 | 焚天龙皇掉落（25%），种出焚心椒 |
| 500064 | PhantomSpore | 幽魂孢子 | 幽灵女巫掉落（25%），种出幽影蘑菇 |
| 500065 | DragonFruit | 龙息果 | 出击餐：下一局枪械与近战伤害 +10% |
| 500066 | EmberChili | 焚心椒 | 出击餐：下一局移速 +8%、换弹 +10% |
| 500067 | PhantomMushroom | 幽影蘑菇 | 出击餐：下一局最大生命 +10% |

全部走克隆兜底注册（零新增 bundle，照 `RelicEggConfig`），并登记进
`BossRushDynamicItemRegistry`——漏登记会让重启后它们退化成官方 FallbackItem。
自定义 cropID 为字符串 `BossRush_Crop_<seedTypeId>`，不占 TypeID 序列。

数值均为草案，待 owner 审定；改动只需改 `RaidMealService` / `ShowcaseService` 的常量。

## 5. 冻结契约

- 建筑 ID `bossrush_backmountain_showcase`
- 存档 key `BossRush_BackMountain_Showcase_v1`、`BossRush_BackMountain_RaidMeal_v1`
- cropID 前缀 `BossRush_Crop_`（进玩家存档的菜地格子）
- 棘轮键 `BossRush_BackMountain_GardenRatchet_v1`

前四条由 `tests/BackMountainStructureGuard.py` 钉住字面值，登记见 `docs/contracts.md` §3。

## 6. 既有文件触点

| 文件 | 改动 |
| --- | --- |
| `LootAndRewards/LootAndRewardsSpecialLoot.cs` | +1 行 `TryAddBackMountainSeedLoot(inv, bossMain)`，接在掉落箱协程末尾（额外掉落，不顶掉既有战利品） |
| `Integration/BossRushDynamicItemRegistry.cs` | 六件物品的 FallbackLoader 登记 |
| `Integration/BossRushIntegration_StartAndScene.cs` | 本地化注入 + 早期建筑注入 |
| `Common/Lifecycle/BossRushRuntimeModuleRegistration.cs` | 注册单实例，排在征程之后 |

## 7. 风险（必须实机验证）

1. 官方 Garden 三连：`GameplayDataSettings.CropDatabase` 非 null 时机、基地 Garden
   实例存在性、注入后种植/生长显示/收获全流程。失败即切退化方案（自建种植箱）。
2. mod 作物在卸载 mod 后的老档残留（棘轮注入缓解，`Crop.Initialize` 早退已确认不崩）。
3. `BaseBGMSelector` 追加时机与官方只存 index 导致的曲目移位（官方机制本身如此，
   mod 条目固定追加在官方之后）。
4. 展示柜建筑注入全链（照日报报箱已趟平，仍需实机过一遍）。
