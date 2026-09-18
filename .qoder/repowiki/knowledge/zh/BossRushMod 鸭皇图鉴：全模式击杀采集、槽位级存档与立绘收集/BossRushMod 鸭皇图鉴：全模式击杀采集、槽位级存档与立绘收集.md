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
    - tests/CodexPresentationGuard.py
---

## 1. 系统概述

鸭皇图鉴是跨模式的 Boss 收藏系统：排除 Mode H 观战与基地，玩家亲手击杀符合采集条件的 Boss，图鉴里对应条目
就点亮一格，记录累计击杀、初见日期、首杀所在模式和最快击杀时间，并配一张 AI 生成的立绘。
解锁数达到阈值时经现有成就系统发放里程碑奖励。

定位是让已有战斗产出可积累的收集进度：挑选未击败的目标、按挑战来源出击、首杀收录，再挑战改善用时。
入口是可用物品「鸭皇图鉴」（TypeID 500061，使用不消耗），基地商店可购。

`codexEnabled` 字段与旧键 `BossRush_CodexEnabled` 为兼容保留；图鉴现属默认内容，
不再注册总开关并在配置加载后强制为 true。dormant 清理契约仍保留供卸载与故障回落使用。

## 2. 关键文件与职责

| 文件 | 职责 |
| --- | --- |
| `CodexTuning.cs` | 全部常量单点：存档 key、schema 版本、里程碑阈值与奖励、丧尸合成 key、立绘 bundle 名、计时表容量上限 |
| `CodexModels.cs` | `CodexData` / `CodexEntry` DTO（`k` / `n` / `kills` / `first` / `fm` / `fast`），深复制候选避免拒写污染已提交状态 |
| `CodexCodec.cs` | 写侧 `SimpleJsonHelper` 追加（字节格式冻结），读侧 2026-09-06 起走共享节点解析器 `Common/Data/BossRushJsonValue.cs`；`CreateDefault()` 是唯一默认值出处，no-throw |
| `CodexPersistence.cs` | 槽位级存档门面：只保留 key / schema / 编解码绑定与下游复位，状态机（幂等订阅、槽位烙印、写屏障、回读核对、`Store()` 只入队）在共享的 `Common/Lifecycle/BossRushSlotJsonStore.cs` |
| `CodexSaveCoordinator.cs` | 图鉴**唯一**物理落盘入口：门面持有一个 `Common/Lifecycle/BossRushSaveCoordinatorEngine.cs` 实例（基地场景闸 + deferred 重试预算 + 欠一次 SaveFile 记账），`SaveFile` 本身只在引擎里 |
| `CodexBossCatalog.cs` | 展示目录：过滤池 ∪ 3 自定义 Boss ∪ 5 丧尸合成条目 ∪ 存档历史条目 |
| `CodexKillCollector.cs` | `Health.OnDead/OnHurt` 的命名 handler，过滤序 + 实例去重 + 最快击杀计时 |
| `CodexMilestones.cs` | 解锁数变化后调成就 `TryUnlock`，幂等 |
| `CodexPortraitCache.cs` | 立绘 bundle 加载与 per-sprite 缓存，**fail-open** |
| `CodexView.cs` / `CodexView_Grid.cs` | 面板、每页最多12张卡、待收集筛选与挑战来源（复用官方 ScrollRect 和 `Common/UI/BossRushUI.cs`） |
| `CodexBookItem.cs` | 入口物品 500061（`CodexBookConfig`）+ 零消耗 `UsageBehavior` + 商店注入与宿主兼容转发 |
| `CodexRuntimeModule.cs` | 宿主回调及运行时销毁清理的唯一 owner：dormant 契约、幂等 bootstrap、兼容开关门控 |

## 3. 架构与设计约定

### 3.1 数据源是官方静态事件，不是任何模式的内部管线

常规战斗经官方 `Health.Hurt()` 的死亡分支派发静态 `Health.OnDead`。
图鉴从已有全局事件转发采集，不接各模式的波次/奖励管线；Mode H 在采集入口明确排除。

这条选择是刻意的，两个被否掉的替代方案：

- `AchievementTracker.OnBossKilled`：只被竞技场与 Mode G 链路喂数，不是全模式数据源；
- `MutatorContext.EnemyKilledCallbacks`：那是变异词条的**局内**回调容器，有局才订阅、退局退订，
  而图鉴需要常驻（含基地与原版 raid）。

订阅接线在全 Mod 的集中点 `Utilities/PlayerLifecycleRuntimeHooks.cs`，与日报采集器同款纪律：
命名 handler（禁 lambda）、成对退订、handler 内开关早返。

### 3.2 过滤序（顺序有意义，删任何一条都会记错）

1. 开关与空目标早返；随后取出并移除死亡目标的计时，归属改变也不遗留起点；
2. 排除 Mode H（必须先于主角归属判断，覆盖其控制互换）；
3. 排除玩家自己的死亡；只记 `info.fromCharacter` 为主角的击杀（环境伤害、随从击杀不算）；
4. 排除遗种巢随从（`PetNestCompanionAgent.IsCompanionHealth`）；
5. 解析受害角色，排除友军 `Teams.player` 与基地场景；
6. 身份归属：主路检查 Boss 身份并取 `characterPreset.nameKey`；丧尸支路**仅在丧尸模式激活时**才走
   `GetComponent<ZombieModeEnemyRuntimeMarker>()`，marker 存在时由它判定是否为 Boss；
7. 实例去重（`HashSet`，切场景清空），计算有效用时；
8. 在数据副本上记击杀，Store 接受后才发布目录进度、请求保存并评估成就。

### 3.3 已知语义（与既有系统口径一致，不是 bug）

- 龙王 1 血护驾召唤的龙裔遗族计入 `DragonDescendant` 条目 —— 与成就、遗种巢博物馆同口径。
- 龙裔「假死」走致死钳制、不进死亡分支，因此**不会双记**。
- Mode F 个别兜底死亡路径不触发 `Health.OnDead`，会少记一次；方向安全（少记不多记），暂不追。
- Mode G staging Boss 冻结后销毁（非死亡），不计入，正确。

### 3.4 最快击杀 = 「首次玩家伤害 → 死亡」区间

复用 `OnGlobalHurt` 记录 `Time.time` 起点、`OnGlobalDead` 结算取 min，不侵入各模式的生成路径。
时序安全性来自官方派发顺序：致命一击的 `OnHurt` 在 `OnDead` **之后**派发且 `isDead` 已置位，
所以 `!target.IsDead` 检查挡住死后脏写。计时字典满时只拒绝新起点，保留已观测战斗。
一击致死若没有起点仍收录击杀，但不猜测用时或发速杀成就；有起点的同帧击杀保留最小正计时。

语义上这是「交战时长」而非「出场到死亡」，跨波躲藏会拉长该 Boss 的表观时间——
作为收集玩法的趣味数字足够，且全模式统一口径。

### 3.5 存档跟槽位（不是账号全局）

key `BossRush_Codex_v1`，`Save<string>` 整存 JSON，顶层字段为 `schemaVersion`、`lastUpdatedTicks`、`entries`。三条理由：

1. 里程碑奖励落在当前槽位资产上，统计跟着槽位才不会出现「新槽位白拿/旧槽位拿不到」；
2. `SaveGlobal` 每次调用立即整写 Global.json + 备份，高频击杀场景需要自建第二套节流，无现成模板；
3. 遗种巢博物馆（同为图鉴语义）就是槽位级先例。

有效 Boss 击杀在有容量上限的数据副本上修改并编码，经 Store 接受后进入单一 pending。
官方 `OnCollectSaveData` 合并快照；主动物理落盘复用协调器的基地 `Tick()` 与宿主销毁兜底。
schema 不符或收藏结构损坏时启用写屏障，保留原始档；不把坏条目跳过后覆写成缩水收藏。

### 3.6 展示目录取并集

`EnsureBuilt` = `GetFilteredEnemyPresets()` ∪ 3 自定义 canonical key ∪ 5 丧尸合成条目
∪ **存档中已有击杀的历史条目**。公共池中的自定义 Boss 先排除，再按自定义类别统一登记。
最后一项解决的是「玩家在 Boss 筛选器里禁用了某 Boss 之后，
已收集的历史条目从图鉴里消失」。

`BossFilter.InvalidateFilteredPresetsCache()` 是唯一咽喉点，图鉴目录在那里并联失效重建。
待收集筛选只影响展示；全录逐个核对目录 key，官方池读取失败不发全录。名字按当前语言解析，缺译文才回落历史快照。

### 3.7 立绘缓存是 fail-open（与 Mode G 的 fail-closed 相反）

Mode G/H 的展示 bundle 是**入口门票**，缺包必须 fail-closed 挡住进入。
图鉴面板展示的是玩家已解锁的内容，缺图不该让整个面板消失，所以这里刻意偏离为 fail-open：
`GetSprite` 返回 null，由 View 落占位链（bundle → `preset.GetCharacterIcon()` → 首字圆底）。
偏离理由写在文件头注释里。

资产命名已冻结：bundle 内 asset 名 = `codex_portrait_` + bossKey **全小写**。

## 4. 性能

`Health.OnDead/OnHurt` 是高频静态事件，关闭时 O(1) 早返；丧尸组件查找受模式门控，
五类 key 返回既有常量，避免每次受伤枚举转字符串和拼接。有效 Boss 死亡会复制数据并编码，
不能称为零分配；普通受伤不执行复制或保存。字典初次扩容等分配也不能用 key 查询回归排除。
面板每页最多创建12张卡、按页取立绘；每帧只比较语言与已提交快照，变化或用户操作才重画。
旧卡先失活再销毁。离线分配检查不等于 Unity 帧耗采样，首次资源加载与长局性能仍待实机。

## 5. 契约面（发布后冻结）

- 存档 key `BossRush_Codex_v1` 与顶层字段 `schemaVersion/lastUpdatedTicks/entries`；条目字段 `k/n/kills/first/fm/fast`。
- TypeID `500061`（鸭皇图鉴）；本地化键 `BossRush_Codex_*`、`BossRush_CodexBook`。
- 立绘 bundle 名与 asset 命名规则 `codex_portrait_<bossKey 小写>`。
- 成就分类 `AchievementCategory.Codex`（**追加在枚举末尾**，老档 int 值不漂移）与 5 条成就 id。
- 丧尸合成 key `zombie_boss_<Kind>`。

## 6. 调试

F3 调试菜单可导出目录清单（nameKey + 显示名），用于核对立绘任务单与排查条目缺失。

## 7. 资源与实机验证状态

- 2026-09-18 已确认工作区 `Assets/ui/codex_portraits` 与 `Assets/Items/codex_book.png` 存在，构建脚本有部署接线。
  `AllowDevRawPngFallback` 在发布构建恒 false，缺包时仍按占位链显示；文件存在不等于游戏内资源加载成功。
- 本轮实机步骤 C01～C07 尚待 owner，见下方生产审核报告；不能以旧版 smoke 或离线检查替代。

## 2026-09-18 生产审核同步（COMPAT / SAFE）

修复拒写污染、坏档截断、自定义分类与语言冻结、满表计时丢失、速杀补判及布局替换时序，
补齐待收集筛选、分页和挑战来源；保留原有收集范围、奖励、TypeID 和 v1 字段。
生产链接回归67项及相关守卫通过，干净基线叠加图鉴补丁的 Windows 正式编译通过；
共享工作区另有并行改动，未声明全库构建通过，尚无 L3 或游戏帧耗采样。
当前专题见 [鸭皇图鉴系统](file://.qoder/repowiki/zh/content/高级功能/鸭皇图鉴系统.md)，
完整证据、取舍与实机清单见本地交付报告 `docs/代码审查/2026-09-18-鸭皇图鉴生产审核.md`（local-only）。

## 8. 2026-08-31 商店入口修复

入口物品使用官方精确标签 `NotSellable` 禁止倒卖；商店 `priceFactor=1`，按物品原始 4000 金
定价，不再使用 `1/rawValue` 把价格压成 1 金。库存随官方 `OnCollectSaveData` 保存、
`OnSetFile` 复位；商店尚未注入时保留已加载缓存，售罄状态不会被默认库存 1 覆盖。

### 8.1 2026-09-01 补齐上架调用点（此前商店注入从未执行）

上一轮只修了**定价与库存语义**，却漏了让注入真正跑起来的两个调用点，
于是图鉴书在游戏里根本买不到，而 `ToggleCodexPanel` 的唯一调用点就是这本书的
`UsageBehavior` —— 整个图鉴面板因此不可达。现已补齐：

- `TryInjectCodexBookIntoShop` 加入 `TryInjectAllBossRushItemsIntoShop`
  （覆盖「商店 Awake 晚于 Mod」的 Harmony 路径）；
- `InjectCodexBookIntoShops` 加入 `IntegrationDeferredBootstrap`
  （覆盖「场景已加载完再进基地」的补注入路径）。

同时把 500061 登记进 `BossRushDynamicItemRegistry` 的 plans 表
（`FallbackLoader` = `CodexBookConfig.EnsureRuntimeFallbackRegistrationShell`）。
此前 shell 已写好但没登记，重启后玩家背包/仓库里的图鉴书会退化成官方
`FallbackItem`（AGENTS 契约第 6 节）。词缀熔石 500060 同批补登记。

## 9. 2026-08-31 官方预设池共享初始化

图鉴与遗种巢共用 `EnsureEnemyPresetsReadyForGameplayCatalogs()`：至少一个消费者启用才触发，
同一进程只做一次实际官方预设扫描。图鉴首次构建前必须先保证池就绪；Boss 过滤变化在同一
咽喉点同时通知两方，已打开的图鉴立即重建，未打开时只失效缓存。Dev F3 会记录官方条目数、
总分母、稳定键唯一性、过滤前后变化、目录 build 次数与预设 scan 次数。

## 2026-09-04 审核修复

**面板销毁必须走 `Close()`。** 面板打开时占了 `InputManager.DisableInput(gameObject)`，
只有 `Close()` 里的 `ActiveInput` 会还回去。`ResetStaticCaches` 此前直接 `Destroy` 对象绕过它，
宿主在面板开着时销毁会把玩家输入**永久**锁死，只能重启游戏。

**落盘重试链修复。** `CodexSaveCoordinator` 与 `CampaignSaveCoordinator` 同形：
`FlushPending()` 消费 pending 后 `HasPendingWrite` 变 false，旧早返会把「还欠一次 SaveFile」
误判成「无事可做」，`SaveFile` 失败即永不重试。已新增 `_saveFilePending` 独立记账。
2026-09-06 起这份记账连同整个协调状态机只存在于共享引擎 `BossRushSaveCoordinatorEngine`，
图鉴 / 征程 / 日报 / 遗种巢各持一个实例；同类修复不再需要在四份副本里各做一遍。

**图鉴书（500061）已登记掉落黑名单。** 与其余 8 个新 TypeID 一同补入——
日报签到池 `requireTags = null`、只过 `LootBlacklistRegistry`，不登记就会被当随机奖励发出去。

## 2026-09-06 历史条目即时入册与全录判定

`COMPAT`。首次击杀目录外 Boss（包括战役终章冠军之影）并被存档队列接受后，`CodexKillCollector` 在成就评估前调用 `CodexBossCatalog.SynchronizeHistoricalEntries`，增量并入历史 key。已有官方池无需每次击杀重建；目录版本变化也会使里程碑缓存重新评估。面板补判先同步历史条目，切槽仍使整个目录失效重建。

全录由 `IsFullyUnlocked` 逐一检查每个实际目录 key 的条目存在且 Kills > 0。额外历史条目或重复记录数量不能代替尚未击杀的 Boss；空目录绝不授予全录。保存 key、成就 ID、奖励数值与原击杀过滤不变。

回归：`tests/ContentThirdReviewFixesGuard.py` 与 `tests/fixtures/ContentThirdReviewFixes/run.py`，直接链接实际击杀采集器、目录与成就判定，覆盖冠军即时显示、补齐最后 key 才全录、相同数量但缺 key、重复击杀不重建、面板补判及切槽。Unity 面板和真实成就派奖仍需实机验证。

## 2026-09-07 界面可读性与视觉整理（COMPAT）

图鉴主面板高度改用共享逻辑视口，不再从 Screen.height 物理像素重复缩放。网格、目录缓存与立绘资产不变；实机待验。


## 2026-09-11 长局最快击杀计时隔离（COMPAT）

`CodexKillCollector.OnGlobalHurt` 原先为所有受伤角色开计时，而杂兵在死亡采集的 Boss 判定处提前返回，
起点只进不出。长局累计到容量上限时会清空整表，仍在交战的 Boss 随之失去起点与十秒速杀判据。
现在计时只接受敌方、非基地、非遗种随从且 `ResolveBossKey` 可识别的 Boss，沿用丧尸 marker 分支。
玩家先造成伤害、最后由随从或环境补刀时仅清理起点，不计为个人图鉴击杀。

`tests/fixtures/ContentThirdReviewFixes/run.py` 直接执行生产采集器：先给 Boss 开表，再伤害/击杀
超过容量的杂兵，Boss 仍正确记录 9 秒并触发既有成就；致命 OnHurt 不重开计时，非玩家补刀清理起点。
原计时表上限、存档字段和未知时间 0 哨兵未变。一击致死未经过非致死 OnHurt 时仍没有有效计时，
需要后续明确兼容表达；本轮不把未测得的时间写成任意正数。未进行 Unity 实机验证。

## 2026-09-11 同帧速杀计时（COMPAT）

若玩家首击与致死在同一帧，计时表仍有真实起点但 `Time.time` 差值为零时，记录 `0.001` 秒的最小正单位，使有效速杀不会被存档的 `0` 未知哨兵吞掉。计时表未命中（例如容量清理、切图或未收到首击）继续保留 `0` 未知语义，不把缺失记录猜成一击。
