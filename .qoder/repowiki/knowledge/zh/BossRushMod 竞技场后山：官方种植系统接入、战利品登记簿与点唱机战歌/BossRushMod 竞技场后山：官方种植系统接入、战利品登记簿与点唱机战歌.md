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
`LevelManager.OnAfterLevelInitialized`（官方 `BuildingEffect` 给建筑加成用的同一时机，
本 mod 的 `SetBonusManager` / `DragonSetBonus` 处理「已穿戴装备进入游戏」用的也是它）
再挂 Modifier，清理走 `RuntimeStatModifierTracker.RemoveAll`。
同一时间只保留一条，后吃覆盖先吃。

**挂载时机是硬约束，不能退到 `sceneLoaded`**：官方主角由
`LevelManager.CreateMainCharacterAsync` **异步**创建，`SceneManager.sceneLoaded`
那一刻 `CharacterMainControl.Main` 必然还是 null。在场景回调里挂加成会静默失败
且没有重试，玩家侧表现为「饭吃了没效果」「登记了不加血」。
因此模块把两类刷新拆开：设施注入（作物表 / 点唱机 / 展示柜建筑）走 `OnSceneLoaded`，
角色加成（出击餐 Modifier、展示柜加成）走 `OnAfterLevelInitialized`；
模块若在关卡初始化之后才 bootstrap，用 `LevelManager.AfterInit` 补一次。
（CR-2026-08-31-001 / -002 修复。）

### 3.3 展示柜是「登记簿」而不是储物柜

最初设计是「存进去才有加成」，但那会逼玩家在「留着这把传说武器」和「换几个百分点属性」
之间二选一。绝大多数人会选前者，于是整个系统没人用。改成登记制之后，玩家带着战利品来
登记一次，东西照样归自己，加成来自「你确实打到过它」。收集与炫耀的驱动力保留了，
机会成本没了。附带好处：不需要动任何物品消耗/归还的 API，也就没有丢件的风险。

加成按品质给：每高于 Q4 一级 +0.5% 最大生命，八格全满额外 +5%，上限约 +21%。

**收藏缓存必须按槽隔离**：收藏是按槽存档的内存缓存（`ShowcaseService._displayed`）。
不隔离的话，同一次会话里从 A 档切到 B 档，A 档的收藏会在 B 档继续生效，
并在 B 档登记时把「A 档收藏 + 新条目」整体写进 B 档存档——永久污染。
两道防线：运行时模块订阅 `SavesSystem.OnSetFile` / `OnSaveDeleted` 调
`ShowcaseService.NotifySlotChanged()`；缓存自身也带槽位烙印，
`EnsureLoaded` 每次比对 `SavesSystem.CurrentSlot`，对不上就自失效重读
（与 `CampaignPersistence` 同款纪律）。（CR-2026-08-31-004 修复。）

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
| 500067 | PhantomMushroom | 幽影蘑菇 | 出击餐：下一局物理受伤倍率 -10% |

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

## 8. 2026-08-31 登记与餐食持久化收口

展示柜可从当前手持或 Armor/Helmat/FaceMask 等穿戴槽登记，物品不被收走；菜地种子与全部
后山餐食都排除在战利品外。登记写入返回 bool 并回读 JSON，官方正在保存或回读不一致时
撤销刚加入的 TypeID，不再显示成功后重进丢失。

出击餐在基地、非种子、已识别且 `SavesSystem.IsSaving == false` 时才允许使用；登记与消费都
回读核对。进入下一局时先把登记持久清零，成功后才挂 modifier，清零失败则本局不消耗也不生效，
避免同一份餐跨多局重复生效。旧档陌生 ID 会给玩家明确提示并尝试清除。

## 2026-09-04 焚心椒的换弹加成用了不存在的 stat key

兼容分类：COMPAT。Finding CR-2026-09-04-008。

`RaidMealService` 给焚心椒挂的是 `ZombieModeStatNames.ReloadSpeedMultiplier`，
但 `"ReloadSpeedMultiplier"` 在官方反编译源码里**零命中**——把
`ZombieModeStatNames` 全部 12 个常量逐个校验，只有这一个不存在。
官方真名是 `ReloadSpeedGain`（`CharacterMainControl` 的 `reloadSpeedGainHash`），
而且早就定义在同一个文件里、丧尸模式的利弊与突变路径用的就是正确的那个。
`RuntimeStatModifierTracker.TryAdd` 走 `GetStat` 拿到 null 后静默丢弃，
所以「焚心椒：换弹速度 +10%」这条从来没有生效过。

同一行上方的 `MoveSpeed` 也是死 key（Animator 参数名），
只是效果被并列的 `RunSpeed`/`WalkSpeed` 兜住了，删掉该行无行为变化。

修复：改用 `ReloadSpeedGain`，删掉幽灵常量本身与死 `MoveSpeed` 调用。
同一个幽灵 key 还让丧尸模式的「换弹速度」属性奖励一起失效，一并修正。
保留 `ZombieModeStatNames.MoveSpeed` 常量——`ZombieModeProductionReadinessGuard`
要求它存在，丧尸模式另有两处带 Walk/Run 扇出兜底的用法仍在用。

回归守卫：新增 `tests/StatKeyExistenceGuard.py`，把 `ZombieModeStatNames` 每个常量值
对 `鸭科夫源码/` 做存在性校验（零命中即红），并单独判「MoveSpeed 挂给 Stat Modifier
却没有 Walk/Run 兜底」（`Animator.StringToHash("MoveSpeed")` 这类正确用法放行）。
这条守卫落地当天就自动抓出了 Mode F Boss 加速的同类缺陷（CR-2026-09-04-009）。

章节来源：`Integration/BackMountain/RaidMealService.cs`、`ZombieMode/ZombieModeTuning.cs`、
`tests/StatKeyExistenceGuard.py`。

## 2026-09-04 审核修复

**展示柜加成不再在开局等于零。** 官方进局治疗发生在 `ShowcaseService.ReapplyBonuses` 之前，
治的是**加成前**的上限；随后抬高 MaxHealth，玩家于是每次进局都差着展示柜那一截血，
加成在开局形同虚设。现在抬上限前先判断玩家是否满血，是则同步补到新的满血；
**只在原本满血时补**，避免变成「收藏一变动就免费回血」。

**三种种子与三种出击餐（500062-500067）已登记掉落黑名单。** 此前九个新 TypeID
全部不在黑名单里，而日报签到池只按该黑名单过滤，许愿台的 `gift` / `healing` 两类
更是把 `Special` 列进 `requireTags`——带 Special 的自定义物品能从那两类进池。

## 2026-09-04 深度复审修复

`COMPAT`。展示柜按存档槽建立写屏障：不存在 key 才初始化可写空收藏；空载荷、未知版本、反序列化/读取错误保持只读，禁止登记覆盖原 key，换槽重新分类。

种子和出击餐配置首先清除克隆来源的 UsageUtilities、旧行为和使用事件；种子解绑使用入口，餐食只挂 RaidMealUsageBehavior。官方 CA_UseItem 在 Use 返回后无条件扣一份，登记失败时通过 Count KV 预补一份抵消扣减（包含满堆与最后一份）；登记写/读回异常尝试恢复上一餐登记，不把 UI 提示失败误判成登记失败。餐食仍只允许基地使用。

章节来源：`Integration/BackMountain/ShowcaseService.cs`、`Integration/BackMountain/RaidMealUsageBehavior.cs`、`Integration/BackMountain/RaidMealService.cs`、`Integration/BackMountain/BackMountainItems.cs`。
