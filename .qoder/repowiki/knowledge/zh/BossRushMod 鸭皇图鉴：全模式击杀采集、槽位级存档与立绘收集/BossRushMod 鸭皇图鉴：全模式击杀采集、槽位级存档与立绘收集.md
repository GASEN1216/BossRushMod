---
kind: gameplay_system
name: BossRushMod 鸭皇图鉴：全模式击杀采集、槽位级存档与立绘收集
category: gameplay_system
scope:
    - Integration/Codex/**
source_files:
    - Integration/Codex/CodexTuning.cs
    - Integration/Codex/CodexModels.cs
    - Integration/Codex/CodexCodec.cs
    - Integration/Codex/CodexPersistence.cs
    - Integration/Codex/CodexSaveCoordinator.cs
    - Integration/Codex/CodexBossCatalog.cs
    - Integration/Codex/CodexKillCollector.cs
    - Integration/Codex/CodexMilestones.cs
    - Integration/Codex/CodexPortraitCache.cs
    - Integration/Codex/CodexView.cs
    - Integration/Codex/CodexView_Grid.cs
    - Integration/Codex/CodexBookItem.cs
    - Integration/Codex/CodexRuntimeModule.cs
    - Config/ConfigCodex.cs
    - Localization/CodexLocalization.cs
    - tests/CodexPersistenceGuard.py
    - tests/CodexKillTrackingGuard.py
---

## 1. 系统概述

鸭皇图鉴是**贯穿全部玩法入口**的收集型元系统：玩家每亲手击杀一个 Boss，图鉴里对应条目
就点亮一格，记录累计击杀、初见日期、首杀所在模式和最快击杀时间，并配一张 AI 生成的立绘。
解锁数达到阈值时经现有成就系统发放里程碑奖励。

定位上它是「乘法型」内容：不新开模式，而是让已有的 9 个入口都产出可积累的收集进度。
入口是可用物品「鸭皇图鉴」（TypeID 500061，使用不消耗），基地商店可购。

`codexEnabled` 字段与旧键 `BossRush_CodexEnabled` 为兼容保留；图鉴现属默认内容，
不再注册总开关并在配置加载后强制为 true。dormant 清理契约仍保留供卸载与故障回落使用。

## 2. 关键文件与职责

| 文件 | 职责 |
| --- | --- |
| `CodexTuning.cs` | 全部常量单点：存档 key、schema 版本、里程碑阈值与奖励、丧尸合成 key、立绘 bundle 名、计时表容量上限 |
| `CodexModels.cs` | `CodexData` / `CodexEntryData` DTO（`k` / `n` / `kills` / `first` / `fm` / `fast`） |
| `CodexCodec.cs` | `SimpleJsonHelper` 编解码（扁平对象 + 一层 `entries` 数组），`CreateDefault()` 是唯一默认值出处，no-throw |
| `CodexPersistence.cs` | 槽位级存档门面：订阅官方存档事件、写屏障、`Store()` 只入队 |
| `CodexSaveCoordinator.cs` | 图鉴**唯一** `SavesSystem.SaveFile` 调用点：基地场景闸 + deferred 重试预算 |
| `CodexBossCatalog.cs` | 展示目录：过滤池 ∪ 3 自定义 Boss ∪ 5 丧尸合成条目 ∪ 存档历史条目 |
| `CodexKillCollector.cs` | `Health.OnDead/OnHurt` 的命名 handler，过滤序 + 实例去重 + 最快击杀计时 |
| `CodexMilestones.cs` | 解锁数变化后调成就 `TryUnlock`，幂等 |
| `CodexPortraitCache.cs` | 立绘 bundle 加载与 per-sprite 缓存，**fail-open** |
| `CodexView.cs` / `CodexView_Grid.cs` | 面板与网格卡片（走 `Common/UI/BossRushUI.cs` 共享库） |
| `CodexBookItem.cs` | 入口物品 500061（`CodexBookConfig`）+ `UsageBehavior` + 商店注入 + partial ModBehaviour 的清理入口 |
| `CodexRuntimeModule.cs` | 宿主回调唯一落点：dormant 契约、幂等 bootstrap、开关热切 |

## 3. 架构与设计约定

### 3.1 数据源是官方静态事件，不是任何模式的内部管线

9 个入口的敌人死亡最终都会走官方 `Health.Hurt()` 的死亡分支派发静态 `Health.OnDead`，
**与模式无关**。因此图鉴直接订阅它，而不是接任何模式的波次/奖励管线。

这条选择是刻意的，两个被否掉的替代方案：

- `AchievementTracker.OnBossKilled`：只被竞技场与 Mode G 链路喂数，不是全模式数据源；
- `MutatorContext.EnemyKilledCallbacks`：那是变异词条的**局内**回调容器，有局才订阅、退局退订，
  而图鉴需要常驻（含基地与原版 raid）。

订阅接线在全 Mod 的集中点 `Utilities/PlayerLifecycleRuntimeHooks.cs`，与日报采集器同款纪律：
命名 handler（禁 lambda）、成对退订、handler 内开关早返。

### 3.2 过滤序（顺序有意义，删任何一条都会记错）

1. 开关门控早返；
2. 排除玩家自己的死亡；
3. 只记 `info.fromCharacter` 为主角的击杀（环境伤害、随从击杀不算）；
4. 排除友军（`Teams.player`，涵盖宠物/雇佣兵/临时同伴）；
5. 排除基地场景（保护基地闲逛的遗种幼体）；
6. 排除遗种巢随从（`PetNestCompanionAgent.IsCompanionHealth`）；
7. 实例去重（`HashSet`，切场景清空）；
8. 身份归属：主路查 `characterPreset.nameKey`；丧尸支路**仅在丧尸模式激活时**才走
   `GetComponent<ZombieModeEnemyRuntimeMarker>()`，避免每次普通击杀白付一次组件查找。

### 3.3 已知语义（与既有系统口径一致，不是 bug）

- 龙王 1 血护驾召唤的龙裔遗族计入 `DragonDescendant` 条目 —— 与成就、遗种巢博物馆同口径。
- 龙裔「假死」走致死钳制、不进死亡分支，因此**不会双记**。
- Mode F 个别兜底死亡路径不触发 `Health.OnDead`，会少记一次；方向安全（少记不多记），暂不追。
- Mode G staging Boss 冻结后销毁（非死亡），不计入，正确。

### 3.4 最快击杀 = 「首次玩家伤害 → 死亡」区间

不侵入 9 个模式的生成路径，改在 `OnGlobalHurt` 里记 `Time.time` 起点、`OnGlobalDead` 结算取 min。
时序安全性来自官方派发顺序：致命一击的 `OnHurt` 在 `OnDead` **之后**派发且 `isDead` 已置位，
所以 `!target.IsDead` 检查天然挡住死后脏写。计时字典有容量上限防泄漏。

语义上这是「交战时长」而非「出场到死亡」，跨波躲藏会拉长该 Boss 的表观时间——
作为收集玩法的趣味数字足够，且全模式统一口径。

### 3.5 存档跟槽位（不是账号全局）

key `BossRush_Codex_v1`，`Save<string>` 整存 JSON + `{schemaVersion, payload}` envelope。三条理由：

1. 里程碑奖励落在当前槽位资产上，统计跟着槽位才不会出现「新槽位白拿/旧槽位拿不到」；
2. `SaveGlobal` 每次调用立即整写 Global.json + 备份，高频击杀场景需要自建第二套节流，无现成模板；
3. 遗种巢博物馆（同为图鉴语义）就是槽位级先例。

高频击杀只改内存 + 单一 pending，物理写盘只有三个出口：官方 `OnCollectSaveData` 顺带 merge、
基地场景 `Tick()`、宿主销毁兜底。schema 不符时写屏障只读不覆盖。

### 3.6 展示目录取并集

`EnsureBuilt` = `GetFilteredEnemyPresets()` ∪ 3 自定义 canonical key ∪ 5 丧尸合成条目
∪ **存档中已有的历史条目**。最后一项解决的是「玩家在 Boss 筛选器里禁用了某 Boss 之后，
已收集的历史条目从图鉴里消失」。

`BossFilter.InvalidateFilteredPresetsCache()` 是唯一咽喉点，图鉴目录在那里并联失效重建。

### 3.7 立绘缓存是 fail-open（与 Mode G 的 fail-closed 相反）

Mode G/H 的展示 bundle 是**入口门票**，缺包必须 fail-closed 挡住进入。
图鉴面板展示的是玩家已解锁的内容，缺图不该让整个面板消失，所以这里刻意偏离为 fail-open：
`GetSprite` 返回 null，由 View 落占位链（bundle → `preset.GetCharacterIcon()` → 首字圆底）。
偏离理由写在文件头注释里。

资产命名已冻结：bundle 内 asset 名 = `codex_portrait_` + bossKey **全小写**。

## 4. 性能

`Health.OnDead/OnHurt` 是全 Mod 最热的静态事件（一局丧尸潮几千次），因此两个 handler：
零分配、零日志、零字符串拼接、开关早返 O(1)；丧尸 `GetComponent` 被模式门控。
面板打开是低频操作，每次全量重建卡片即可（与成就面板同型）。

## 5. 契约面（发布后冻结）

- 存档 key `BossRush_Codex_v1` 与 envelope 字段；条目字段 `k/n/kills/first/fm/fast`（只增不改）。
- TypeID `500061`（鸭皇图鉴）；本地化键 `BossRush_Codex_*`、`BossRush_CodexBook`。
- 立绘 bundle 名与 asset 命名规则 `codex_portrait_<bossKey 小写>`。
- 成就分类 `AchievementCategory.Codex`（**追加在枚举末尾**，老档 int 值不漂移）与 5 条成就 id。
- 丧尸合成 key `zombie_boss_<Kind>`。

## 6. 调试

F3 调试菜单可导出目录清单（nameKey + 显示名），用于核对立绘任务单与排查条目缺失。

## 7. 已知未完成项

- 立绘 AssetBundle 需在兄弟 Unity 工程构建；`AllowDevRawPngFallback` 在发布构建恒 false，
  因此仅有 raw PNG 时正式包会走占位链。
- 实机 smoke 未做（详见 `FIX_TRACKER.md` 2026-08-30 条目）。

## 8. 2026-08-31 商店入口修复

入口物品使用官方精确标签 `NotSellable` 禁止倒卖；商店 `priceFactor=1`，按物品原始 4000 金
定价，不再使用 `1/rawValue` 把价格压成 1 金。库存随官方 `OnCollectSaveData` 保存、
`OnSetFile` 复位；商店尚未注入时保留已加载缓存，售罄状态不会被默认库存 1 覆盖。
