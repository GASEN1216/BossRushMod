# CODE_REVIEW_FINDINGS.md — 已确认问题库

> 只记录 confirmed findings。未验证线索放本文件的 UNVERIFIED 区，或在 `FIX_TRACKER.md` 中标为 `accepted/deferred/refuted/documented`。

> 2026-09-04 深度复审增补：固定基线 `f9b83c0..adf1f3e`，新增 **CR-2026-09-04-021..035：2 P0 / 10 P1 / 3 P2，均 Open**。
> 完整触发链、修复建议与验证边界见 [本轮审查报告](docs/代码审查/2026-09-04-f9b83c0-adf1f3e-深度复审.md)。下面的原状态汇总是此前历史统计，未包含本批。

## 状态汇总

| 严重级 | Open | Fixed | Deferred | WontFix | 合计 |
| --- | ---: | ---: | ---: | ---: | ---: |
| P0 | 0 | 17 | 0 | 0 | 17 |
| P1 | 6 | 44 | 0 | 0 | 50 |
| P2 | 4 | 42 | 0 | 0 | 46 |
| P3 | 1 | 25 | 0 | 0 | 26 |

最后更新：2026-09-03 在线 Wiki 渲染层核查（owner 追问"页面风格是否一致"）。
新增 CR-2026-09-03-021（P2，已修）：`[tip]/[warn]` 的 sync 正则 `(.+)$` 只吃第一行，
源文里折成两行的 callout 其续行会掉到闭合 `:::` 之外，线上渲染成「提示框 + 游离正文」，
多数还是从逗号处断开。全站 42 处（其中 8 处是前一批 CR-2026-09-03-016 改写整节时新引入的），
连同全站唯一的双 `<h1>` 页面（reforge）一并修平。修在源文不动正则——那个 transform 被
`ZombieModeMutantWikiGuard` 逐字节镜像，JS/Python 的 `$`+MULTILINE 语义不同，改正则会静默漂移。
新增 `WikiCalloutSingleLineGuard`（4 例反向验证），生成物全量审计 224 篇零缺陷，
并起 VitePress dev server 做了 DOM 核对。

上一次更新：2026-09-03「全部玩法完整性」全量扫描批（数据表 → 代码反查，覆盖全部 Assets/**/*.json）。
新增 CR-2026-09-03-019（P1，已修）与 CR-2026-09-03-020（P2，已修）：
Mode H 八条战痕**只有两条真能生效**——三条触发型的 triggerId 与数据表逐字对不上或干脆没有调用点，
加上 `bell_dependence` 的自结算收益分量识别不到、只兑现代价的"纯负面利弊绑定"；
以及 `appliesWhen` 条件层被解析后零读者，9 个分量一律无条件施加。
两条均已修：019 当轮修复；020 由 owner 拍板「随战斗持续求值」后实施。
编译零警告；`ModeHScarTriggerWiringGuard`（按 JSON 反查代码）共 9 条断言逐条反向验证。

上一次更新：2026-09-03「新模式生产可玩性」审核批（f9b83c0..工作区，零调用点扫描）。
新增 CR-2026-09-03-017（P1）与 CR-2026-09-03-018（P2），两条都属"实现完整但入口没接线"：
口令点火目标无生产者导致 `finish` 整条是空操作（且认证/遥测都报 held，三层检查全绿），
战场快照的重建侧全链零调用且与 §20.3 恢复语义互斥、在冻结转换表下结构性不可达。
两条均已修：Windows 编译零警告、新增 `ModeHCommandFireTargetGuard`，
新旧断言共 8 项逐条反向验证转红（其中 2 条断言方向由"必须存在"反转为"必须缺席"）。

上一次更新：2026-09-03 游戏内 Wiki 内容核对批（f9b83c0 以来全量改动 vs `WikiContent/`）。
新增 CR-2026-09-03-016：1 条合并立条的内容缺陷（1 个 P2 + 2 个 P3），全部玩家可见且已修——
图鉴页写了一个不存在的"Wiki 书入口"（`ToggleCodexPanel` 全仓库只有物品这一个调用点）、
随机事件页承诺乱入 Boss 掉战利品箱（无间炼狱里既不掉箱也不进现金池，杀它零产出）、
Boss 筛选器页的生效范围停在 Mode G / Mode H / 随机事件立项之前。
另有 14 处开发预览装备提示不再宣传"调试授予"路径。纯内容改动，`SAFE`，无代码变更；
517 guard 中 Wiki/repowiki/图鉴/随机事件相关全绿（工作区另有 4 个与本批无关的红项，见下）。

上一次更新：2026-09-03 可达性接线批（审核发现的全部问题，含次要项）。新增 CR-2026-09-03-012..014：
1 个 P0（Mode H 伤病/战痕/公开异常三层内容整体不生效——认证只覆盖 13 条口令，
异常与分量 ID 永远查不到实测记录，战痕一条开不出、伤病永远无名、四个异常一次不触发，
而选秀卡照样把异常当卖点展示）、1 个 P1（ERROR 完整互换 §17.6.5 零调用点，
连同两个 Harmony postfix 与看台表演整条是死代码；同时修掉观战租约不让渡输入这个硬阻断）、
1 个 P1（ApplyRetirement 零调用点，名人堂把已退役的主选手记成冠军、真正夺冠的替补
反被写进 substituteHistory，冠军与替补整个对调）。
三条均已修：Windows 编译通过、516 guard 全绿（新增 ModeHDataStampGuard）、
15 项新断言逐条反向验证转红。次要项 7 条一并消化（接线 4、删除 2、documented 1）。
实机 smoke 七项待人工，见 `FIX_TRACKER.md` 同日条目。

上一次更新：2026-09-03 f9b83c0 以来 365 个改动 .cs 的玩法向审核。新增 CR-2026-09-03-009..011：
1 个 P0（非本波 Boss 经掉落漏斗推波——乱入 Boss 跳波 + Mode D 把标准 Boss 刷进自己局内）、
1 个 P1（空投箱到时即销毁，不看玩家是否正在开箱）、1 个 P2（战役追踪按进场景而非开局武装）。
三条均已修：Windows 编译通过、515 guard 全绿、两个新 guard 各做过反向验证（共 12 项逐条转红）。
实机 smoke 五项待人工，见 `FIX_TRACKER.md` 同日条目。

上一次更新：2026-09-03 七日全面审核（96 个提交 / 约 10 万行新增）。新增 CR-2026-09-03-001..008：
1 个 P0（押品脱离仓库后无回滚，真实物品可永久丢失）、1 个 P1（濒退制「休息解除带伤」完全未接线）、
2 个 P2（锁盘全静默；ERROR 互换归属渗入图鉴与日报）、4 个 P3。八条全部已修：
Windows 编译通过、513 guard 全绿、新增/增补 guard 均经**反向验证**（逐条破坏不变式确认转红）。
实机 smoke 四项待人工，见 `FIX_TRACKER.md` 同日条目。

上一次更新：2026-09-02 第六轮（138 PASS / 0 FAIL / 0 SKIP / 0 WARN；014–017 转 Fixed。
H 初始整备 8/8、入场意图清除、丧尸结算返程、终章与最终订阅差值均实机通过。
人工清单仍有 113 条待验；既有 AI / 外部 Mod 异常不等同已排除）。

上一次更新：2026-09-02 第五轮（134 PASS / 3 FAIL / 1 WARN；H 首次认证及缓存通过，005/013 转 Fixed。
新增 014–017：丧尸撤离测试缺少正数样本、H 成功入场意图未消费、H 弹药误走装备槽、终章掉落订阅残留。
代码与离线验证完成；新增项在下一轮实机验证前保持 Open）。

上一次更新：2026-09-02 第四轮（完整 F3 报告 69 PASS / 2 FAIL；D 多波通过，010/012 转 Fixed。
H 逐候选不再拒绝或出现伤害异常，011 转 Fixed，但总体认证和缓存仍卡口令门，005 保持 Open。
新增 013：口令矩阵无生产写入者、签名绑定顺序与缓存恢复缺失；已修正并编译，游戏内复测前保持 Open）。

上一次更新：2026-09-01（新增 CR-2026-09-01-010，记录第二次 F3 报告确认的
2 个 P1 + 1 个 P2 + 1 个 P3；修复与静态验证已完成，下一份完整实机报告通过前保持 Open）。

上一次更新：2026-08-31（新增 CR-2026-08-31-009，记录首次 F3 完整验收日志确认的
4 个 P1 + 3 个 P2；代码、守卫和 Windows 编译修复已完成，但按发布门槛在下一份完整实机
报告通过前保持 Open，不提前标 Fixed）。

更早更新：2026-08-31（用户明确要求全部修复并保持内容开启。CR-2026-08-31-001..006
与原 deferred 的 CR-2026-08-31-007 七项均已 Fixed；新增 CR-2026-08-31-008 记录本轮
静态确认并修复的 5 个 P1 + 2 个 P2。模式H 赔率、日报未读提示两个旧 deferred 也已闭环。
修复内容与验证见 `FIX_TRACKER.md` 同日条目；涉及真实游戏对象、UI 和存档切槽的实机 smoke 仍待人工）。

更早更新：2026-08-29（四系统复审全面修复完成：CR-2026-08-29-008..021 全部 Fixed，
修复内容与验证见 `FIX_TRACKER.md` 的「四系统复审全面修复」条目。
Open 计数按问题条目计，分组条目内多项分别计数。上午修复轮的
CR-2026-08-29-001..007 未单独立条，见 `FIX_TRACKER.md` 四个修复包）。

## Confirmed Findings

### CR-2026-09-04-001：遗种蛋与词缀熔石 100% 作废（两个新系统的入门产出口全断）

**严重级**：P0（遗种巢与词缀锻造的**唯一**入门获取路径，拿不到就整条养成链走不通）
**兼容分类**：`COMPAT`（只补 defer 接线，不改掉落概率、TypeID、数据 schema）
**状态**：Fixed
**来源**：2026-09-04 f9b83c0..HEAD 新模式可玩性审核（官方源码逐条对照）

#### 位置

- `PetNest/PetNestDropService.cs`（TrySpawnEggIntoBossInventory 写 CharacterItem）
- `Integration/AffixForge/AffixForgeStoneDropService.cs`（同上）
- `LootAndRewards/LootAndRewards.cs:339/342`（两者的注册点）
- `LootAndRewards/LootAndRewardsRandomBossLoot.cs:520`（`dropBoxOnDead = false`）

#### 问题

官方 `CharacterMainControl.cs:1297-1304` 先派发 `BeforeCharacterSpawnLootOnDead`，
再 `if (dropBoxOnDead) CreateFromItem(characterItem)`。
`RandomizeBossLoot_LootAndRewards` 把 `dropBoxOnDead` 置 false 并另建一个带
**全新本地 Inventory**（`EnsureLocalInventory(lootbox, 512)`）的箱子，
全仓库没有任何代码把 `CharacterItem.Inventory` 转移进新箱子。
而遗种蛋与熔石的 handler 注册点就在主 handler 注册的**同一个函数体**内
（`RegisterBossRandomLootTracking`），必然配对生效——写进去的东西必然作废。

仓库本有正解 `ShouldDeferBlueBossExtraDropToBossRushLootbox`，
grep 确认只有寒霜长矛与女巫镰刀接了，这两个新系统都没接。

#### 影响

遗种蛋 500059 除天灾远征外无第二产出口，而远征需先有崽——引导链彻底断开，
玩家刷再多 Boss 也开不出第一枚蛋。词缀熔石同理（游戏内 Wiki 已向玩家承诺「Boss 掉落」）。
静默失效，无任何报错或日志。

#### 修复

两个 service 各补齐 defer 协议四处接线（判定 / pending 登记 / 进箱消费 / 撤销），
形态照寒霜长矛。PetNest 的 pending 用 `Dictionary<CharacterMainControl,string>`
而非 HashSet——血脉必须带到 consume 时才能 `TryStampLineage`。
新增 `tests/ExtraBossDropDeferGuard.py` 锁住四个 integration × 四处接线。

#### 遗留

实机 smoke 待人工：标准竞技场刷 Boss，确认奖励箱里能开出遗种蛋与词缀熔石。

---
### CR-2026-09-04-002：无间炼狱下全部额外掉落丢失（含既有的寒霜长矛与女巫镰刀）

**严重级**：P0
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：同上批（修 001 时发现该分支根本不建箱子）

#### 位置

- `LootAndRewards/LootAndRewardsRandomBossLoot.cs:307-320`（infiniteHellMode 分支）
- `LootAndRewards/LootAndRewards.cs:419-426`（Mark 条件含 `!infiniteHellMode`）

#### 问题

`MarkBossRushLootboxPathTracking` 的条件含 `!infiniteHellMode`，
所以无间炼狱下 defer 判定恒假，额外掉落走 `CharacterItem` 路径；
而无间炼狱分支 `dropBoxOnDead = false` 后**直接 return**，连箱子都不建。
两头落空。这条洞在本次修复前就存在，寒霜长矛与女巫镰刀在无间炼狱同样全丢。

#### 修复

新增 `ShouldDeferExtraBossDropToModPath`（显式并上 `infiniteHellMode`）统一判定；
无间炼狱分支在 `FinalizeBossRushLootboxPathTracking` **之前**调用
`DropPendingExtraLootIntoWorld`，按该模式既有的世界掉落通道（`Item.Drop`，
与里程碑现金同路）投放。一个插入点同时修好四个 integration。
守卫断言含顺序（Finalize 会撤销 pending，顺序反了等于没接）。

#### 遗留

实机 smoke 待人工：无间炼狱刷 Boss，确认地上能捡到额外掉落。

---
### CR-2026-09-04-003：龙皇绕过掉落登记，defer 判定对它恒假

**严重级**：P2
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：同上批

#### 位置

- `Integration/DragonKing/DragonKingBoss.cs`（手动注册路径）

#### 问题

龙皇自己手动订阅 `BeforeCharacterSpawnLootOnDead`，从不走
`RegisterBossRandomLootTracking`，因此 `MarkBossRushLootboxPathTracking` 也没跑过，
`bossRushLootboxPathBosses` 里没有它，defer 判定恒假，CR-001 的修复在龙皇身上不生效。

#### 修复

龙皇注册路径补一次 `MarkBossRushLootboxPathTracking(character)`。
**未**调整 `PetNestDropService.TryTrack` 的注册顺序：进箱消费在
`AddBossSpecialLootToLootboxCoroutine` 里，隔着至少一次 `yield`，
必然晚于同帧的多播 handler，现有顺序是安全的；
且 `PetNestDropLifecycleGuard:131-152` 冻结了「TryTrack 紧随订阅」，调序会误伤。

---
### CR-2026-09-04-004：Mode H 赔率页「锁盘」被推出屏幕，玩家只能弃局

**严重级**：P0（`OddsPreview` 唯一的玩家侧出边就是锁盘）
**兼容分类**：`COMPAT`（纯布局，不改状态机与数据）
**状态**：Fixed
**来源**：同上批（像素级推导 + 官方 UIInputManager 逃生路径核实）

#### 位置

- `ModeH/ModeHUIPages.cs`（CreateActions 单行居中平铺）
- `ModeH/ModeHRuntimeModule_MatchFlow.cs`（押品格塞进 page.Actions）

#### 问题

`CreateActions` 单行居中平铺、步距 264（`ActionSize(240,56)` + `CardGap 24`），
canvas 参考分辨率固定 1920x1080。按钮数 = 下注档(<=3) + **押品格(<=40)** + 锁盘(1)，
最坏 44 个、行宽约 11600px。第 8 个按钮起右边缘越过 960，即
**仓库前 40 格有 >=4 件物品就点不到「锁盘」**。
`CreateModalSurface` 上没有任何 Mask，越界按钮不会被裁掉而是照常画到屏幕外。

严重性已核实边界：该页 `ClaimModalInput` 置 timeScale=0 且 `InputManager.DisableInput`，
但官方 ESC 菜单走 `UIInputManager`（`鸭科夫源码/.../UIInputManager.cs:405-407`）
而非 `InputManager`，`View.ActiveView == null` 成立，**ESC 仍能开菜单退出关卡**。
所以是「这一局打不下去、只能弃局」，不是杀进程级死锁；
`EnforceModalInputPause` 每 LateUpdate 重置 timeScale=0，关掉 ESC 菜单也回不去。

#### 修复

押品格本质是**选择器**不是**动作**：移出动作行，进 `ModeHPageContent.RealStakeSlots`，
由新增的 `CreateRealStakeSlots` 渲染到已预留的 `ModeH_RealStakeSelector` 区，
超出视口即套 `ScrollRect + RectMask2D`。动作行只剩下注档 + 锁盘，固定 <=4 个。
形态照 `PetNestUI.cs:150-156`（同架构、同坑、已修，且由 PetNestUILayerGuard 冻结）。
`CreateActions` 另加换行兜底（`MaxSingleRowActions = 7`），越界向上堆而不是往两侧铺。

#### 遗留

实机 smoke 待人工：仓库放 >=10 件物品进看盘页，确认「锁盘」可见可点、押品格可滚动。

---
### CR-2026-09-04-005：Mode H 入口页第 4、5 张选秀卡被画在动作按钮底下

**严重级**：P1（赛季开局第一个页面）
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：同上批

#### 位置

- `ModeH/ModeHUIPages.cs`（CreateCardGrid 固定 3 列、无高度校验）

#### 问题

入口页 `ShowRealStakeNotice = true` 把 `cursorY` 压到 226；
`DraftCandidateCount = 5`、`columns` 固定为 3，第 2 行卡片 y 落在 [-398,-98]，
与动作行 y [-382,-326] 重叠。第 4、5 张选秀卡被按钮盖住。

#### 修复

按可用高度（`topY` 到 `ActionBandReserve`）推 `maxRows`，放不下就**加列**
（卡片变窄，下限 `CardMinWidth = 220`），而不是继续往下堆。
`ActionBandReserve` 提为常量，行列表与卡片网格共用，避免两处漂移。

---
### CR-2026-09-04-006：Mode H 恢复壳动作行 5 个按钮就出面板

**严重级**：P1（恢复壳是应急界面，它失效等于补救入口消失）
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：同上批

#### 位置

- `ModeH/ModeHRecoveryPanel.cs`（RebuildActions 同款无界单行公式）

#### 问题

与 CR-004 同一公式，步距 284、面板宽 1280，n>=5 即出面板。
恢复壳正是「取回押品」「结束赛季」这些补救按钮所在处。

#### 修复

同样按 `MaxSingleRowActions`（此处 = 4，按 1280 面板推导）换行向上堆。
新增 `tests/ModeHActionLayoutGuard.py` 同时锁住两处动作区、押品格去向与卡片网格避让。

---
### CR-2026-09-04-007：Mode H 赔率分量 18 条标签双前缀，全显示星号 raw key

**严重级**：P1
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：同上批

#### 位置

- `ModeH/ModeHOddsController.cs:600`（生产侧已拼完整 key）
- `ModeH/ModeHRuntimeModule_MatchFlow.cs:933`（消费侧又拼一次）

#### 问题

`entry.LabelKey` 已是完整 key，消费侧再拼 `LocalizationKeyPrefix`，得到
`BossRush_ModeH_BossRush_ModeH_Odds_*`，`Localization/ModeHLocalization.cs:333-350`
注册的 18 个 `Odds_*` 全部落空。
同文件 `:523-524` 早有对这个坑的白纸黑字告警，正确写法在 `:562`。
`ModeHLocalizationGuard` 只收字面量，运行时拼接进不了 `used` 集合，所以一直是绿的。

#### 修复

消费侧改为 `L10n.T(entry.LabelKey)`。
`ModeHLocalizationGuard` 新增双前缀反查：`LocalizationKeyPrefix +` 后不得跟**成员读取**
（裸后缀一定是字面量/局部变量/参数，成员读取拿到的是 DTO 里已拼好的完整 key），
并加一条生产侧对偶断言，防止两边只改一半。

---
### CR-2026-09-04-008：焚心椒「换弹更利索」用了不存在的 stat key（丧尸模式同款奖励一并失效）

**严重级**：P1
**兼容分类**：`COMPAT`（`AttributeBonuses` 是纯运行时字典、不落盘，改 key 不影响存档）
**状态**：Fixed
**来源**：同上批（12 个 stat key 逐个对官方源码做存在性校验）

#### 位置

- `Integration/BackMountain/RaidMealService.cs:179`
- `ZombieMode/ZombieModeTuning.cs:22`、`ZombieMode/ZombieModeRewards.cs:22`

#### 问题

`"ReloadSpeedMultiplier"` 在官方源码里**零命中**（`ZombieModeStatNames` 全部 12 个
key 逐个验过，只有这一个是 0）。官方真名是 `ReloadSpeedGain`
（`CharacterMainControl.cs:3588` 的 `reloadSpeedGainHash`），且早已定义在同一个文件里。
`RuntimeStatModifierTracker.TryAdd` 走 `GetStat` 返回 null，静默丢弃（AGENTS §14）。
不止后山：丧尸模式的「换弹速度」属性奖励用的是同一个幽灵 key，
`ApplyZombieModePlayerAttributeModifiers` 直接把它传给 `GetStat`，无任何映射，同样全废。
（丧尸模式的利弊/突变路径用的是正确的 `ReloadSpeedGain`，只有属性奖励这条错。）

#### 修复

两处改用 `ReloadSpeedGain`；删除幽灵常量本身；
顺带删掉 `RaidMealService` 里同为死 key 的 `MoveSpeed` 那行
（效果由并列的 `RunSpeed`/`WalkSpeed` 兜住，删除无行为变化）。
**保留** `ZombieModeStatNames.MoveSpeed` 常量：`ZombieModeProductionReadinessGuard`
要求它存在，且丧尸模式另有两处仍在用（那两处有 Walk/Run 扇出兜底）。

新增 `tests/StatKeyExistenceGuard.py`：把 `ZombieModeStatNames` 每个常量值对
`鸭科夫源码/` 做存在性校验，零命中即红；并单独判「MoveSpeed 挂给 Stat Modifier
却没有 Walk/Run 兜底」（Animator 用法放行）。这条能防住整类 §14 缺陷。

#### 遗留

实机 smoke 待人工：吃焚心椒进局确认换弹变快；丧尸模式取换弹速度奖励确认生效。

---
### CR-2026-09-04-009：Mode F 血猎 Boss 加速完全不生效

**严重级**：P1
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：同上批，由新加的 `StatKeyExistenceGuard` 自动抓出，不是人工翻到的

#### 位置

- `ModeF/ModeFPhases.cs`（ApplyModeFBossMoveSpeedModifier / ClearModeFBossMoveSpeedModifiers）

#### 问题

`boss.CharacterItem.GetStat("MoveSpeed")` 恒返回 null（官方没有这个 stat），
紧接着 `if (speedStat == null) return;`，整个 Boss 加速函数每次都在这里静默退出。
血猎追击的 Boss 提速档位从来没生效过。

#### 修复

改挂官方真实的 `WalkSpeed` + `RunSpeed` 两条（追击走 Run、巡逻走 Walk，
只挂一条会让加速在另一半时间里看不出来），并改用共享的
`RuntimeStatModifierTracker`（记 Stat 引用而非 stat 名，Boss 销毁时不会误摘别人的）。
每只 Boss 的记录容器从单个 `Modifier` 改为 record 列表。

#### 遗留

实机 smoke 待人工：血猎模式推进阶段，确认 Boss 移速确实变快。

---

### CR-2026-09-04-010：Mode H 真实押品在仓库满时只留在内存，重启/切槽即永久丢失

**严重级**：P0
**兼容分类**：`COMPAT`（不改存档 schema；只新增 receipt 语义与一条官方缓冲区出口）
**状态**：Fixed
**来源**：2026-09-03 全面审核（静态确认 + 官方源码交叉核对，未运行验证）

#### 位置

- `ModeH/ModeHWarehouseStakeJournal.cs:789`（`ReturnEscrowItems` 无空位即 `return false`）
- 同文件 `:846`（`GrantPlannedRewards` 同构）、`:506-519`（`RollbackDetached` 第三处滞留点）
- 同文件 `:30`（`_escrowItems` 是纯内存 `static List<Item>`）、`:1090` / `:1104`（两处无条件 `Clear()`）

#### 证据

托管物只存在于 `_escrowItems`。仓库满时三条路径都「保持 pending」，而 `LoadPersisted`
与 `ResetStaticCaches` 会把这张表清空——玩家退出游戏 / 切槽 / 删档，真实装备即蒸发；
journal 里只剩语义摘要，全仓没有任何函数能从摘要反造物品
（`ModeHRewardItemPool.TryInstantiate` 只接受 typeId）。

官方本来就有正确出口：`PlayerStorage.Push(item, toBufferDirectly: true)`
（官方源码 `PlayerStorage.cs:162`）把物品序列化进**持久**的 `IncomingItemBuffer`（`:43`/`:207`），
玩家之后在 `StorageDock` 取回；官方任务奖励走的就是这条。Mode H 全程只用 `Inventory.AddAt`。

#### 影响

真实仓库装备无声蒸发，且此后该槽的真实押品被永久禁用。

#### 修复

三处滞留点全部改走官方溢出缓冲区，清空内存表之前先排空。落点必须是
`ModeHWarehouseStakeJournal`——`ModeHStakeJournalGuard.check_bridge` 明令禁止 bridge 出现
`PlayerStorage.Push`，`check_single_writer` 禁止其余 ModeH 文件引用 `PlayerStorage`。

---

### CR-2026-09-04-011：Mode H 押品阶段机三个死态，中止返还必然失败并连带锁死七个旧模式入口

**严重级**：P0
**兼容分类**：`COMPAT`（**未改动 §22.2 冻结转换表**，见下）
**状态**：Fixed

#### 位置

- `ModeH/ModeHRealStakeService.cs:297-327`（`TryAbortReturn` 对一切非 Prepared/EscrowSnapshotDurable 阶段都走 `CommitAbortReturn`）
- `ModeH/ModeHWarehouseStakeJournal.cs:563` / `:595`（`CommitResult` / `CommitAbortReturn` 的字段写入不随 `TryAdvancePhase` 回滚）
- `ModeH/ModeHRuntimeModule_CombatFlow.cs:809-817`（结算失败只写 `CriticalLog`）

#### 证据

`ResultCommitted` / `AbortReturnCommitted` / `SettlementPending` 三态在冻结表里没有通向
`AbortReturnCommitted` 的出边，`TryAbortReturn` 却无条件提交一次 abort return，
必撞 `journal_illegal_transition`。三条入口（离场、Suspended、恢复壳「取回押品」按钮）全部失效。
非终态 journal 经 `RecomputeSlotConsistency` 置 `SetExternalAssetRiskBlocked(true)`，
而 `IsLegacyModeEntryAllowed()` 被 **7 个旧模式入口**消费
（ModeD/E/F/G、WavesArena、ZombieMode×2）→ 该存档槽再也进不去任何旧模式。

附带：`CommitResult` 先写 `resultToken` 再 `TryAdvancePhase`，后者失败时不回滚该字段，
于是开头的 `commit_result_already_committed` 早退让**任何重试永久失败**。

#### 修复

**冻结表无需改动**——`ResultCommitted → SettlementPending → Terminal` 与
`AbortReturnCommitted → SettlementPending → RefundedTerminal` 本来就是合法路径，
缺的只是按阶段分派。新增 `TryCompleteFrozenSettlement` 沿用已冻结的 `settlementKind` 续做
（绝不切换 kind，那会撞 `journal_settlement_kind_drift`）；`TrySettleMatch` 加重入保护；
`Commit*` 的字段写入随阶段推进一起回滚；结算失败新增玩家可见文案 `Settle_Failed`。
`ManualIntervention` 单独留一条**只返还不推进阶段**的物理出路——物理交付不能依赖账目走到终态。

---

### CR-2026-09-04-012：随机事件乱入 Boss 顶掉本波 Boss 身份，标准竞技场卡波或误推波

**严重级**：P0
**兼容分类**：`COMPAT`
**状态**：Fixed

#### 位置

- `RandomEvents/RandomEventCatalog.cs:639`（从 `GetFilteredEnemyPresets()` 取池，无排除）
- `Utilities/EnemySpawnCore.cs:783/801/818`（路由到三个专用生成器）
- `Integration/DragonKing/DragonKingBoss.cs:265`、`Integration/PhantomWitch/PhantomWitchBoss.cs:207`（`currentBoss = character;` 无条件）

#### 证据

`RandomEvents/` 全目录 grep 无 `IsManagedBossPreset` 排除，而池里含三个自定义 Boss
（由各自 `Register*Preset()` 主动 Add 进去）。生成器无条件写波次身份容器，
而 `WavesArena.IsCurrentWaveBossMember` 正信这两个容器。
单 Boss 档：真 Boss 击杀不再推波；乱入者被销毁后 `TryFixStuckWaveIfNoBossAlive`
读到「无存活 Boss」反而**主动推波**。这是 CR-2026-09-03-009 同类问题经**生成路径**的第二次发生。

#### 修复

三个生成器补 `isNonWaveSpawn` 门控（照龙裔既有的 `isChildProtectionSummon` 先例），
经 `EnemySpawnCoreOptions.SuppressWaveBossRegistration` 从随机事件桥透传。

---

### CR-2026-09-04-013：`ShowMessage` 在正式构建里对玩家完全不可见（约 137 处调用）

**严重级**：P1
**兼容分类**：`SAFE`
**状态**：Fixed

#### 位置

`Common/Infrastructure/BossRushEagerReflectionCache.cs:91`、`UIAndSigns/UIAndSigns.cs:292-300`

#### 证据

绑定写的是 `GetMethod("ShowNext", Public|Static)`，而官方 `NotificationText.ShowNext()` 是
**私有实例零参**方法（官方源码 `NotificationText.cs:50`），公有静态的是 `Push(string)`（`:15`）。
绑定恒为 null → 整段永不执行；`statusMessage` 是 write-only 字段、零渲染方；
`DevLog` 被 `[Conditional("BOSSRUSH_DEV")]` 在正式构建里剥离。
多个守卫（如 `ModeHStructureGuard` 的锁盘反馈检查）正是拿「有没有调 ShowMessage」
当「有没有玩家可见反馈」的判据，因此一批「不再静默失败」的修复也跟着一起哑掉。

#### 修复

删掉反射，直接调官方公有静态 `NotificationText.Push`（样板：同文件 `ShowBigBanner`）。

---

### CR-2026-09-04-014：随机事件商人交互名 key 全仓零注入

**严重级**：P1
**兼容分类**：`COMPAT`
**状态**：Fixed

`RandomEvents/RandomEventEffectsBridge_Spawn.cs:576` 把
`RandomEventsTuning.LocalizationPrefix + "MerchantShop"` 写进 `_overrideInteractNameKey`，
而全仓 grep 该前缀只有 2 处命中（都是常量定义本身），`Localization/` 下无对应注入文件。
该模块其余文案走内联 `L10n.T(中,英)`，所以只有这个走查表的 key 漏出来，玩家看到带星号的原始 key。
修复：新增 `Localization/RandomEventsLocalization.cs` 并挂进 `InjectLocalization_Extra_Integration()`。

---

### CR-2026-09-04-015：大兴兴血脉的遗种巢随从入场即被自家清理扫描销毁并循环重生

**严重级**：P1
**兼容分类**：`COMPAT`
**状态**：Fixed

`ModBehaviour.cs:1375` 的 `TryCleanNonBossRushDaXingXing` 是全仓四条会 `Destroy` 角色的扫描里
**唯一**没做随从豁免的一条（另三条都调 `PetNestCompanionAgent.IsCompanionCharacter`）。
随从 clone 的中性化只改 team/exp/掉落、**不改 nameKey**，`DisplayName` 仍是「大兴兴」，
正好命中该函数的名字匹配；又因为不是「受伤致死」，`pet.state` 不转 `Downed`，
重试窗口内每秒重生一次再被杀一次，每轮还泄漏一个 clone preset。修复为一行级豁免。

---

### CR-2026-09-04-016：空投「翻箱保护」判据选错，宽限窗口几乎永不生效

**严重级**：P1
**兼容分类**：`SAFE`
**状态**：Fixed

CR-2026-09-03-010 用 `InteractableBase.Interacting` 判断「玩家正在翻箱」，但官方
`InteractableLootbox` 在战利品界面打开后一帧就 `StopInteract()`，此后 `Interacting` 恒 false
而界面仍开着——保护窗口等于没接上，箱子照旧到时带着物品销毁。
修复：改用官方公有静态事件 `InteractableLootbox.OnStartLoot` / `OnStopLoot` 做闩
（幂等订阅 + 成对退订），`Interacting` 保留为覆盖「正在打开」那一小段的次要信号。

---

### CR-2026-09-04-017：两个落盘协调器的重试链被自身消费，SaveFile 失败后数据永不落盘

**严重级**：P2
**兼容分类**：`COMPAT`
**状态**：Fixed

`Campaign/CampaignSaveCoordinator.cs` 与 `Integration/Codex/CodexSaveCoordinator.cs` 同形：
`FlushPending()` 一旦成功，pending 即被消费，`HasPendingWrite` 随之变 false。
而 `FlushBatch` 开头只用 `HasPendingWrite` 判断「有没有事要做」，于是 `SaveFile` 失败后
置起的重试标记在下一帧命中该早返直接 `return true`——**Tick 重试与宿主销毁兜底一起失效**，
数据停在 SavesSystem 内存里从不落盘。这与两个文件里关于 deferred 重试的注释承诺相反。
修复：新增独立的 `_saveFilePending`（欠一次 SaveFile），早返同时看它，只有 SaveFile 真正成功才清。

---

### CR-2026-09-04-018：Mode F 补位重生克隆 preset 未挂租约，每次补位泄漏一个 ScriptableObject

**严重级**：P2
**兼容分类**：`SAFE`
**状态**：Fixed

`ModeF/ModeFRespawn.cs` 的补位重生克隆了 `characterPreset` 却没挂
`ModeECharacterPresetLease`（对照 `ModeE/ModeEBattle.cs:671-692` 的准备期路径）。
commit `1583da1` 为修 CR-2026-09-01-010 #2 删掉了本文件的 `Destroy(characterPreset)`，
这条路径于是从「提前销毁」变成「永不销毁」。Mode F 的两条 Boss 生成路径是分叉的
（`RegisterModeFBoss` 全仓只有两个调用点），准备期那条有租约、战中补位这条没有。

---

### CR-2026-09-04-019：九个新 TypeID（500059-500067）全部未登记掉落黑名单

**严重级**：P2
**兼容分类**：`COMPAT`
**状态**：Fixed

`Assets/Data/LootBlacklist.json` 与 `Config/LootBlacklistRegistry.cs` 的硬编码 fallback 都没有它们。
日报签到池 `requireTags = null`、**只**过 `LootBlacklistRegistry`（`DailyReportRewards.cs:222`），
因此这九件会被当随机奖励发出去；许愿台的 `gift`/`healing` 两类把 `Special` 列进 requireTags
（`WishFountainRewardPoolBuild.cs:616-617`），带 Special 的自定义物品同样可能进池。
最坏是刷出一颗**没有血脉**的遗种蛋——孵化按物品 KV 上的血脉 key 工作，凭空造的那颗拿不到 key，
等于一个永远孵不出东西的死物。**附带更正 AGENTS.md §14**：先前「Special 一律不进许愿台」过于绝对。

---

### CR-2026-09-04-020：本轮次要项汇总（7 条 P2/P3）

**严重级**：P2 ×5 / P3 ×2
**兼容分类**：`SAFE` / `COMPAT`
**状态**：Fixed

1. `ModeHProfilePersistence` 的 `_storeFaulted` 切槽不复位 → 一次读回失败让**本进程所有槽**
   都写不进赛季，且无玩家可见提示（P2）。
2. `ModeHRuntimeModule.RestoreForSlotChange` 在已有活动 run 时早退 → 旧槽 `_runState` 留在内存，
   之后任何 `TryPersistSeason` 把旧槽赛季写进新槽（P2）。修复时**刻意不走 `ShutdownRuntime`**：
   它的中止路径会把押品退还到「当前」仓库，而此刻 `PlayerStorage` 已指向新槽。
3. `CodexView.ResetStaticCaches` 绕过 `Close()` 直接 Destroy → 面板开着时宿主销毁会把
   `InputManager.DisableInput` 永久留下，玩家输入锁死只能重启（P2）。
4. `MagicBlendInitializationOrderPatch` 的两张短路表只在宿主销毁时清，注释却写「切场景 / 宿主销毁」
   → 整会话按 Animator instanceID 无界增长（P2）。已并联进 `OnSceneUnloadAlwaysOnRuntime`。
5. `EnemySpawnCore` 延后后处理失败销毁角色时不解绑掉落追踪 → 已死引用留到下次场景清理（P2）。
6. 展示柜 MaxHealth 加成挂在官方满血治疗**之后** → 玩家每次进局都不满血、加成开局等于零（P2）。
   修复只在「原本满血」时补到新上限，避免变成收藏一变动就免费回血。
7. `PetNestUIPages.LastFailureText` 进程级静态残留；`run_guards.bat` / `verify_syntax.bat`
   的 fallback 分支 `exit /b %ERRORLEVEL%` 在**解析期**展开导致恒返回失败；
   `.gitignore` 漏放行 `docs/contracts.md`（`TypeIdLedgerGuard` 硬依赖它）（P3）。

### CR-2026-09-03-022：在线 Wiki 的 favicon 一直 404

**严重级**：P3
**兼容分类**：SAFE
**状态**：Fixed
**来源**：2026-09-03 配图需求落地时顺带发现

#### 位置

- `wiki-site/docs/.vitepress/config.mts:329`
- `wiki-site/docs/public/`（此前**不存在**）

#### 问题

`head` 里写了 `['link', { rel: 'icon', href: `${base}images/favicon.ico` }]`，
但 `wiki-site/docs/public/` 这个目录从来没建过，VitePress 也就没有任何静态资源可发。
线上标签页图标一直取不到，浏览器回落到默认地球图标。无报错、构建照样绿。

#### 修复

`tools/build_wiki_images.py` 用图鉴书图标生成 16/32/48 三尺寸 `favicon.ico`
写进新建的 `wiki-site/docs/public/images/`。配图需求本来就要建这个目录，顺手补上。

浏览器实跑核对：`fetch('/BossRushMod/images/favicon.ico')` 由 404 转 **200**。
`WikiImageAssetGuard` 只守配图清单，不守 favicon——它是单文件、无清单，
由 `build_wiki_images.py --check` 覆盖。

---

### CR-2026-09-03-021：多行 callout 在线上 Wiki 掉出提示框（渲染缺陷）

**严重级**：P2
**兼容分类**：SAFE（源文改排版 + 新增 guard，无代码/无 schema 改动）
**状态**：Fixed
**来源**：2026-09-03 owner 追问"在线 Wiki 页面风格是否一致"后的渲染层核查

#### 位置

- `wiki-site/scripts/sync-content.mjs:150-151`（callout 正则，**未改**）
- `WikiContent/{zh,en}/**.md` 共 25 个文件、42 处 callout
- `WikiContent/{zh,en}/system__reforge_and_achievements.md:1-7`（双页面标题）

#### 问题

sync 的 callout 转换是 `/^\[tip\]\s*(.+)$/gm → '::: tip\n$1\n:::'`。
`.` 不匹配换行，`(.+)$` **只吃第一行**。源文件里把一条 callout 折成两行时，
第二行被留在闭合 `:::` **之后**，线上渲染成「提示框 + 一段游离正文」，
而且多数是从逗号处断开。实测 `systems/random-events.md`：

```
::: warning
无间炼狱里请直接把它当障碍物绕开。它不推波、不掉箱、不进现金池，
:::
打赢它唯一的收获是弹药消耗和一段本可以用来推波的时间。   ← 掉出框外
```

全站 42 处，其中 **8 处是 CR-2026-09-03-016 那一批新引入的**（改写整节时按中文习惯折了行），
其余 34 处为历史遗留。这是"线上风格是否一致"这一问的实质答案：**此前不一致**。

顺带查出唯一的结构性不一致：`system__reforge_and_achievements` 有**两个** `##` 页面标题
（`重铸与成就` + `重铸系统`），转换后是一页两个 `<h1>`，全站仅此一例；
且第一个标题与 `catalog.tsv` 登记的条目名（`重铸系统`）对不上。
成就清单早已拆去 `system__achievements_list`，这是拆分时留下的壳。

#### 修复

**修在源文，不动正则**：`transformContent` 被 `tests/ZombieModeMutantWikiGuard.py`
逐字节镜像，而 JS 与 Python 在 `$` + MULTILINE 下语义不同，改正则容易让两边静默漂移；
单行 callout 本来也是本仓库的多数写法。42 处续行全部并回首行
（中文不加空格、西文加一个空格，按首尾字符是否 CJK 判定）。
reforge 双标题合并为一个，取 `catalog.tsv` 的登记名。

新增 `tests/WikiCalloutSingleLineGuard.py`：断言 callout 的下一行必须为空行、
文件结尾或另一个块级起始。注意 `**bold**` 不是列表项——`*` 必须后跟空白才算 bullet，
这一点第一版写错过，会漏掉 3 处。

#### 验证

- 新 guard 反向验证 4 例：折行 → RED、`**bold**` 续行 → RED、
  callout 后接真列表 → GREEN、基线 → GREEN；目标文件逐字节还原。
- 全量生成物审计（224 篇 / 179 个 callout）：掉框 0、容器不配平 0、未转换 `[tip]` 残留 0、
  标题跳级 0、多 h1 0。仅剩 2 篇 `index.md` 无 h1——那是 VitePress `layout: home` 的
  hero 页，本就没有 h1，属正常。
- VitePress dev server 实跑，DOM 核对：warning 框内含完整整句、`nextElementSibling`
  是 `H2` 而非游离段落；reforge 页 `h1count=1`、`h1 → h2 → h3` 无跳级。
- `--filter Wiki` 6 PASS（新 guard 已被 runner 自动发现）、`ZombieModeMutantWiki` PASS、
  `Repowiki` PASS。

#### 遗留

无。

---
### CR-2026-09-03-019：Mode H 八条战痕只有两条能生效（触发接线 + 自结算分量双重断链）

**严重级**：P1（战痕是 Mode H 唯一的永久成长产出，"拿到了但永远不生效"）
**兼容分类**：`COMPAT`（只补代码侧接线与分量识别，Scars.json 一字未改）
**状态**：Fixed
**来源**：2026-09-03「全部玩法完整性」全量扫描批（数据表 → 代码反查）

#### 位置

- `ModeH/ModeHCombatControl.cs`（`EvaluateTriggeredInjuries` 的触发调用）
- `ModeH/ModeHInjuryAndScarSystem.cs`（`TryOpenScarWindow`、`ApplySelfSettledComponents`）
- `Assets/Data/ModeH/Scars.json`（8 条战痕、5 条伤病）

#### 问题

`TryOpenScarWindow(scarId, triggerId)` 用 `string.Equals(spec.Trigger, triggerId, Ordinal)`
逐字比对，不匹配时 `return false` 且**不设** failureReasonId——调用方拿到 (false, null)，
只会当作"这次不该触发"。三种坏法同时存在：

| scarId | Scars.json 的 trigger | 代码实际传入 | 结果 |
| --- | --- | --- | --- |
| `broken_shield_charge` | `armor_first_break` | `armor_broken` | 字面量不匹配，永不触发 |
| `blood_rush` | `enemy_first_low_health` | 无调用点 | 永不触发 |
| `longshot_memory` | `first_ranged_damage_taken` | 无调用点 | 永不触发 |
| `crowd_favorite` | `enemy_count` | `crowd_present` | 多余调用（它是常驻战痕）|

能生效的只剩 `bell_dependence` 与 `relay_expert` 两条触发型，加上三条常驻
（`center_keeper` / `skill_saver_scar` / `crowd_favorite`，由 `ApplyStandingScars` 正常施加）。

**第二处断链**：`ApplySelfSettledComponents` 只认 `op = self_settled_command_scale`，
但数据表里 `bell_dependence` 与 `spirit` 用的是 `op = self_settled` +
`controlPointId = command_scale`。于是 `bell_dependence` 的 **+20% 收益从未生效**，
而它的 −10% `skillSuccessChance` 代价照常生效——一条**纯负面**的"利弊绑定"，
恰好违反本系统"不允许收益生效、代价失效"的冻结契约。
（`spirit` 不受影响：它另有 `OnEnemyCountChanged` 专用路径按常量 0.85 施加，与数据同值。）

#### 修复

- 三条触发型战痕按数据表逐字对齐并补上条件：
  - `armor_first_break` → 护甲物品耐久首次归零（官方按 `damageInfo.armorBreak` 扣 `Item.Durability`，
    耐久是唯一可靠事实源；护甲 stat 不随耐久线性下降。没穿甲则不触发，语义如此）；
  - `enemy_first_low_health` → 复用点火目标扫描已算好的最残敌军比例，不另开每帧遍历；
  - `first_ranged_damage_taken` → 遥测新增 `ActiveFighterTookRangedDamage`，
    由 `IModeHTelemetrySink.OnParticipantHurt` 新增的 `fromWeaponItemID` 参数驱动，
    远程判定沿用 ModeG / Campaign 既有的 Gun/MeleeWeapon tag 口径（本地复制，AGENTS 4.9）。
- 删除 `crowd_favorite` 的多余触发调用（常驻战痕不走触发路径）。
- `ApplySelfSettledComponents` 同时识别两种 command_scale 写法；
  带条件门的条目（`requiresEnemyCountAtLeast`，当前只有 `spirit`）跳过无条件施加，
  否则 ×0.85 会与专用路径叠成 ×0.7225。
- 新增 `tests/ModeHScarTriggerWiringGuard.py`：按 JSON 反查代码，
  锁住「触发型必须有逐字匹配的调用点」「常驻不得走触发路径」「自结算分量必须能被命中」。

#### 遗留

实机 smoke 待人工：让选手吃远程伤害 / 打破护甲 / 把敌人打残，确认三条战痕各自开窗。

---

### CR-2026-09-03-020：战痕的 `appliesWhen` 条件层完全未求值

**严重级**：P2（不阻断玩法；影响的是 9 个分量的生效条件，属数值与手感）
**兼容分类**：`COMPAT`（只加条件判定与求值输入，Scars.json 一字未改）
**状态**：Fixed（owner 2026-09-03 拍板「随战斗持续求值」，已实施）
**来源**：同上批

#### 位置

`Assets/Data/ModeH/Scars.json`（9 个分量）；
`ModeH/ModeHContentModels.cs:37`（`AppliesWhen` 字段）；
`ModeH/ModeHContentCatalogParsers.cs:264`（唯一写入点）

#### 问题

`appliesWhen` 被解析进 `ModeHEffectSpec.AppliesWhen`，然后**没有任何读者**——
全仓库只有字段声明与那一行赋值。8 种条件（`before_bell`、`starter_opening`、
`enemy_count_at_least_3`、`single_core_fight`、`condition_danger_edge`、
`condition_open_field`、`reinforcement_pending`、`first_wave_alive`）一律不生效，
9 个分量全部**无条件施加**。最明显的后果是 `crowd_favorite`：
它的两个收益写着"场上敌军≥3 才给"、代价写着"单核战才吃"，两个互斥条件同时恒真，
于是这条战痕在任何局面下都同时拿到全部收益和全部代价，设计意图被抹平。

#### 修复（owner 拍板：随战斗持续求值）

否决的是「开窗时一次性求值」：它对常驻战痕是错的——`crowd_favorite` 在选手登场时开窗，
那时敌军还没生成，`enemy_count_at_least_3` 恒假，收益反而永远拿不到。

实施口径：条件随重申循环（`CommandReassertIntervalSeconds`，0.1 秒）持续求值，
分量按条件真伪**上下线**。

- 新增 `ModeHEffectConditions` 求值器，覆盖全部 8 种条件。`condition_<id>` 直接与本场
  `plan.conditionId` 逐字比对——`danger_edge` 与 `open_field` 本就是 ThreatPlans 里
  真实存在的 `arenaConditionId`，不需要另建映射表。
- `ModeHCommandAdapter` 新增 `SyncConditionalEffects`：条件真伪翻转时才动手，
  假→真按**当前值**重新捕获并施加，真→假还原那一条并摘掉。
  重新捕获当前值而不是开窗时的值，与本适配器一贯的嵌套语义一致
  （口令/伤病/战痕三套窗口可能同时改同一个控制点，每层只还原到自己接手时看到的值）。
- **点火类分量同样受条件约束**：否则"下线"只对调制类生效，点火分量会绕过条件
  继续每 0.1 秒把 AI 的目标掰回去。
- `Restore == false` 的分量（当前只有 `nextReleaseSkillTimeMarker`）一旦施加就不下线，
  维持它"写入后交还原版、绝不还原"的契约。
- `Restore()` 与条件下线共用同一个 `WriteOriginal`，避免两处 switch 漂移出不同的控制点集合。
- 条件输入由 `ModeHCombatControl.RefreshEffectConditionInputs` **每帧**刷新
  （不跟点火目标一起节流：重申落在哪一帧不由本类决定）。
  新增 `ModeHParticipantRef.BatchIndex` 与 `ModeHCombatTelemetry.HasLiveEnemyInBatch`
  以支撑 `first_wave_alive`；擂台条件与末批次序号在 `BeginMatch` 一次性交付，
  战斗控制不反向持有 Season 引用。
- **自结算分量例外**：`_selfSettledCommandScale` 是累乘标量，无法只撤销其中一项，
  因此只在开窗时求值一次。这只对整场恒定的条件成立，故守卫断言
  自结算分量只能带 `condition_*` 族。
- 未知条件取值一律**按无条件生效**处理（fail-open）：认不出就按假会静默禁掉分量，
  那正是本次要消灭的失败形态；拼写错误交由构建期守卫拦。

`ModeHScarTriggerWiringGuard` 扩充 4 条断言并逐条反向验证：
Reassert 不调 Sync、条件失去判定分支、`condition_<id>` 指向不存在的擂台条件、
自结算分量带动态条件——四条都能转红。

#### 遗留

实机 smoke 待人工：`crowd_favorite` 在敌军数跨过 3 的前后、`bell_dependence` 在拍铃前后，
观察分量是否真的上下线。

---
### CR-2026-09-03-017：Mode H 口令点火目标无生产者，`finish` 整条是空操作

**严重级**：P1（玩家每场唯一一次的干预手段，八条口令里有一条点了等于没点）
**兼容分类**：`COMPAT`（只补生产侧计算，不改数据表、不改存档、不改口令语义）
**状态**：Fixed
**来源**：2026-09-03「新模式生产可玩性」审核批（f9b83c0..工作区，零调用点扫描）

#### 位置

- `ModeH/ModeHCombatControl.cs`（`RefreshFireContext` / 已删除的 `SetFireTargets`）
- `ModeH/ModeHCommandAdapters.cs:356,364`（两个消费分支）
- `Assets/Data/ModeH/Commands.json:95,160`、`Assets/Data/ModeH/Scars.json:161`

#### 问题

`ModeHCommandFireContext` 的 `NearestEnemy` / `LowestHealthEnemy` 有消费者
（`ModeHCommandAdapter.Fire` 的 `fire_notice_nearest` 与 `fire_lowest_health_target`），
但**没有生产者**：唯一的设值口 `ModeHCombatControl.SetFireTargets(...)` 全仓库零调用点，
`RefreshFireContext` 只填 `ArenaCenter` 与 `EnemyCount`，注释写着"最近/最残敌人由生成事务
在每次登记时刷新"——而生成事务里没有这段。两个字段恒为 null，两个分支永远进不去。

玩家侧后果：

- **`finish`**（intent=execute）两个 effect 全依赖它。`fire_lowest_health_target` 空转，
  `fire_notice_current_target` 只能把 AI 本来就有的目标重新 notice 一次——**整条口令是空操作**。
  拍铃每场限一次，选它等于把唯一的干预机会扔掉。
- **`press`** 4 个 effect 里 3 个正常，转火最近一项失效。
- `Scars.json` 里带 `fire_lowest_health_target` 的战痕同样空转。

**为什么编译、guard 与生产认证三道全绿还是漏了**：`ModeHCommandAdapter.Validate()`
对这两个控制点的判据是 `_ai.searchedEnemy != null` 与 `_ai.noticed`——AI 自己有目标就算
"保持住了"。于是认证把 `finish` 标成 `VerifiedBehavior` 正常发给玩家选，逐 effect 遥测也报 held。
这是本仓库反复出现的"静默失败"最纯粹的一例：三层检查都绿，功能不存在。

#### 修复

目标改由 `ModeHCombatControl` 内部按遥测的存活敌军名单计算（`RefreshFireTargets`）：

- 名单来源是 `ModeHCombatTelemetry._liveEnemies`（登记与死亡两处维护），
  新增零分配访问口 `GetLiveEnemyAt(int)`，不暴露内部 List 也不复制；
- 参照点是**当前登场选手**而不是擂台中心——口令是发给他的；
- 目标取官方 `CharacterMainControl.mainDamageReceiver`（`searchedEnemy` 的类型）；
- 生命归零的不计入"最残"（那是等待死亡结算的尸体）；
- 热路径纪律（AGENTS 4.12）：按 `CommandReassertIntervalSeconds` 节流，
  节奏与重申循环一致；拍铃走 `RefreshFireContext(0f, true)` 强制重扫，不吃缓存；
  扫描是对个位数名单的一次 O(n) 遍历，无分配、无 `GetComponent`、无场景查找；
- `BeginMatch` 清空两个目标，避免把上一场已销毁的引用带进新一场；
- 战痕开窗的首次点火改用战斗控制器转发进来的活上下文（`_sharedFireContext`），
  否则带 `fire_lowest_health_target` 的战痕在开窗那一下仍会空转；
- 删除 `SetFireTargets` 外部设值口——它就是本 bug 的成因。

新增 `tests/ModeHCommandFireTargetGuard.py`，5 项断言逐条反向验证转红
（其中"只清空未赋值"一条是反向验证时才发现第一版写松了，已收紧成"必须赋非 null"）。

#### 遗留

实机 smoke 待人工：开一场 Mode H，分别选 `finish` 与 `press` 拍铃，
确认选手确实转火到最残 / 最近的敌人。

---

### CR-2026-09-03-018：Mode H 战场快照的重建侧整条不可达，与 §20.3 恢复语义互斥

**严重级**：P2（不是可玩性阻断——回滚语义本身自洽安全；但半接的路径会误导后续改动）
**兼容分类**：`SAFE`（删除的全部是零调用点分支，删除前后运行时行为逐字相同）
**状态**：Fixed（按 §20.3 收敛）
**来源**：同上批

#### 位置

- `ModeH/ModeHBattleSnapshot.cs`（`Validate` / `IsPositionUsable` / `TryRestoreHealth`、`ModeHSnapshotRebuildPlan`）
- `ModeH/ModeHCombatControl.cs`、`ModeHCommandController.cs`、`ModeHCombatTelemetry.cs`（三个 `RestoreFromSnapshot`）

#### 问题

采集侧很活跃：四类触发点都在写，且随 Season 落盘进 `currentBattleSnapshot`。
读回侧**一个调用点都没有**——`ModeHCombatControl.RestoreFromSnapshot`、
`ModeHBattleSnapshot.Validate`、`TryRestoreHealth` 全链零调用。

这不是"少接一根线"，而是**两套互斥的恢复语义**，且实际生效的是另一套：
§20.3 规定战前/战中的任何故障一律回落到**同一场看盘**
（`ResolveRecoveryResumeLifecycle` 把 `MatchBrief..MatchSettling` 整个战斗族映射到 `MatchBrief`），
由 `RestoreMatchReservationAndSnapshot` 整场回滚：退还预留、还原选手档案、
删除未归档结算、清空 `currentBattleSnapshot`。

冻结转换表站在 §20.3 这边：`ModeHStateMachine` 里 `Recovering` 的出边只有
`EntryIntent / SceneLoading / Drafting / RosterLocked / MatchBrief / ErrorRecoveryPending /
Intermission / TransferWindow / HallOfFame / Suspended`，**没有任何一条通向战斗态**。
局中重建因此在状态机层面结构性不可达。

连带影响：本轮工作区里新加的 ERROR 互换快照重建支路（`_errorSwapRebuildProfileId`，
§17.6.5 第 8 条）写在这条死路径内部，**落地即不可达**。

#### 修复

按 §20.3 收敛，删除永远跑不到的重建侧，并在原处留下完整理由与"将来若要启用"的清单：

- 删 `ModeHSnapshotRebuildPlan`、`Validate`、`IsPositionUsable`、`TryRestoreHealth`；
- 删三个 `RestoreFromSnapshot` 与 `_errorSwapRebuildProfileId` 身份门；
- **采集侧保留不动**：`currentBattleSnapshot` 是 Season 落盘字段并参与 §20.2 canonical digest，
  摘掉它是 `SCHEMA-`（老档 `VerifyDigest` 会失败），属 AGENTS.md §10 需 owner 签字；
- `ModeHBattleSnapshotGuard` 与 `ModeHStandInGuard` 的断言**方向反转**：
  从"必须存在"改为"必须保持缺席"，防止后来者只接回一半又造出"写好了但跑不到"。
  两条都做过反向验证。

#### 遗留

**需 owner 拍板**：是否要真正启用局中重建（玩家中断后接着打，而不是重打这一场）。
那需要一起做三件事——冻结表给 `Recovering` 加战斗态出边、恢复驱动接重建、
重新引入这三个方法——属状态机改造，本轮不擅自决定。当前语义（重打同一场，
资产与结算全额回滚）本身自洽且安全，玩家不会卡死也不会被吞奖。

---
### CR-2026-09-03-016：游戏内 Wiki 三处内容与代码不符（玩家可见）

**严重级**：P2（一条 P2 + 两条 P3 合并立条，均为玩家可见的错误信息）
**兼容分类**：SAFE（纯内容修订，无代码改动）
**状态**：Fixed
**来源**：2026-09-03 f9b83c0 以来全量改动的 WikiContent 逐条回查批

#### 位置

- `WikiContent/{zh,en}/system__codex.md`（"怎么打开" 小节）
- `WikiContent/{zh,en}/system__random_events.md`（"不速之客" 小节）
- `WikiContent/{zh,en}/system__boss_filter_and_wiki.md`（"概述" 与 "禁用 Boss"）
- `.qoder/repowiki/zh/content/高级功能/鸭皇图鉴系统.md:47`（同一条图鉴入口错述）

#### 问题

**1（P2）**：图鉴页写"Wiki 书里也有一个入口，点一下直接跳到图鉴面板"。
`CodexRuntimeModule.ToggleCodexPanel()` 全仓库**唯一**调用点是
`Integration/Codex/CodexBookItem.cs:309` 的 `UsageBehavior`；`WikiUIManager` /
`WikiContentManager` 里没有任何图鉴按钮，也没有注册快捷键（`_wiki_link` 分类是
外链在线 Wiki，不是图鉴）。玩家会照着这句话在 Wiki 书里反复找一个不存在的入口。
repowiki 同一条也写了"Wiki 书里也有一个交叉入口，点击即关书并打开图鉴面板"。

**2（P3）**：随机事件页写乱入 Boss"照常掉一个战利品箱"，并把整段结论落在
"要不要为一箱战利品多打一场计划外的 Boss"。这在**无间炼狱**里不成立：
`OnBossBeforeSpawnLoot_LootAndRewards` 在 `infiniteHellMode` 分支无条件
`dropBoxOnDead = false`（改发现金池），而 CR-2026-09-03-009 修复后
`HandleBossDeath` 的本波成员校验在现金池累加**之前**早返——乱入 Boss 两头都不占，
杀它零产出。随机事件恰好在无间炼狱触发，玩家会为一个不存在的箱子多打一场。

**3（P3）**：Boss 筛选器页的生效范围停在四个模式（标准/白手起家/划地为营/血猎追击），
且"禁用后不再出现在**任何模式**的 Boss 池中"。实际 `GetFilteredEnemyPresets()`
的消费者还包括 `ModeG/ModeGSpawnTransaction.cs`、`RandomEvents/RandomEventCatalog.cs`
（乱入 Boss 池）、`Integration/Codex/CodexBossCatalog.cs` 与
`PetNest/PetNestLineageCatalog.cs`；而 Mode H（自持 `BossProfiles.json`）与
丧尸模式（自持丧尸）**不吃**这份筛选。页面写于 Mode G / Mode H / 随机事件立项之前。

#### 修复

三处按代码实读改写，中英双语同改，并同步 repowiki 那一条：

- 图鉴页改为"这本书就是唯一的入口"，明写没有快捷键、没有第二入口。
- 随机事件页按模式分列产出，并加 `[warn]`：无间炼狱里它不推波、不掉箱、不进现金池。
- 筛选器页补齐六模式 + 乱入池，列出 Mode H / 丧尸两个例外，并说明图鉴与遗种巢
  血脉名单跟随同一池子（但**已收集条目不消失**，与 `CodexPersistence` 的实际行为一致）。
- "禁用 Boss"一句由"任何模式"改为"上面列出的那些模式"。

顺带把 7 件开发预览装备（zh+en 共 14 处）的"仅开发/调试授予可获得"改为叮当的原话
「上头还没批出库」——`LocalizationInjector.cs:438` 已有这句游戏内台词，
面向玩家的页面不应该宣传调试授予路径。

`wiki-site/docs/` 已由 `wiki-site/scripts/sync-content.mjs` 重新生成（222 篇）。

#### 遗留

无。三条均为静态可证（调用点计数 / 分支早返顺序 / 消费者清单），不需要实机复验。

---
### CR-2026-09-03-012：模式H 伤病 / 战痕 / 公开异常三层内容整体不生效

**严重级**：P0
**兼容分类**：COMPAT（认证缓存失效重跑一次；无存档 schema 变更）
**状态**：Fixed
**来源**：2026-09-03 可达性接线批（静态确认：调用链 + 数据表交叉核对，未运行验证）

#### 位置

- `ModeH/ModeHCommandCertificationProbe.cs:53`（Run 只遍历 `ModeHContentCatalog.Commands`）
- `ModeH/ModeHCommandCompatibilityRegistry.cs:334`（HasVerifiedBehavior 只查 effect 级）
- `ModeH/ModeHProductionCertification.cs:676`（BuildCommandStatuses 只落盘口令）
- `Assets/Data/ModeH/CommandCompatibility.json`（selfSettledEffects 只有一条）

#### 问题

`HasVerifiedBehavior` 要求 `_effectStatuses[(stableKey, id)] == VerifiedBehavior`，
或 id 在 `_selfSettledEffectIds` 里。而唯一写入者只遍历 `Commands`，只写
`<commandId>.<controlPointId>` 形状的 id。于是 `Scars.json` 的分量 ID
（`leg.sightDistance` 等）与四个裸异常 ID（`blood`/`crowd`/`strong`/`error`）
**永远查不到记录**，`IsEntryUsableForKey` 与 `HasVerifiedAnomalyBehavior` 恒 false。

玩家侧后果：战痕一条都开不出（`PickScarOffer` 恒 `scar_offer_no_candidate`）；
伤病永远无名（可用条目只有全自结算的 `armor`/`spirit` 两条，少于门槛 3 条，
`PickInjury` 返回空串）；三条胆怯与 ERROR 一次都不触发。
而选秀卡 `BuildProfileCardBody` **无条件**展示异常名与描述，玩家据此签约。

另有同源的一半：`BuildBehaviorSnapshot` 零调用，`profile.behaviorStatuses` 恒空，
`ModeHOddsController.IsVerified` 恒 false，玩家侧伤病 / 异常 / 战痕赔率项也一直计 0。

#### 修复

探针提取 `ProbeGroup` 后依次驱动 Commands → Injuries → Scars（适配器 `ApplyEffects`
本就是通用入口，`ownerEntryId` 只是标签）；注册表新增条目级 `GetBehaviorEntryStatus`
（伤病战痕不设 PartiallyVerified，§17.4）；认证报告与缓存往返一并按条目级查询
（不改这一处，结论会在名人堂缓存往返上整批丢失，且口令层看起来毫无异常）；
四个公开异常按 §17.6.4 line 1308 转入 `selfSettledEffects` 并重新盖章；
`BuildBehaviorSnapshot` 重写为只产出赔率真正查询的三类，在抽签与转会签入两处填充。

#### 遗留

战痕 `blood_rush` 仍不可开出：唯一非自结算分量 `blood_rush.searchedEnemy` 的
`ReadField` 读不到（守卫明令禁止加 case——点火类效果没有目标遥测，
`_ai.searchedEnemy != null` 证明不了仍是我们设的那个目标）。战痕池实际 7 / 8。

---
### CR-2026-09-03-013：ERROR 完整互换（§17.6.5）从未被调用，且租约不让渡输入

**严重级**：P1
**兼容分类**：WIRE+
**状态**：Fixed
**来源**：2026-09-03 可达性接线批（零调用 grep + 调用链复核；未运行验证）

#### 位置

- `ModeH/ModeHCombatControl.cs:490`（TryBeginErrorSwap，零调用点）
- `ModeH/ModeHSpectatorLease.cs:116`（DisableInput 只在 Release 恢复）

#### 问题

`TryBeginErrorSwap` 是唯一能把 `_swapPhase` 推离 `None` 的入口，全仓零调用点。
`TickErrorSwap` 首行早返，于是 `CompleteSwapHandover`、`SetStandInActive(true)`、
`ModeHStandInPerformer` 与两个 Harmony postfix 在生产里全是死代码。
`_errorTriggered` 只被写进赛后报告——「本局触发过 ERROR」被记下来，游戏里什么都没发生。
这是 `FIX_TRACKER.md` 2026-08-29 条目那份「战斗驱动尚未接线」清单里唯一没跟上的一项。

叠加的硬阻断：观战租约在 `TryAcquire` 步骤 1 就 `InputManager.DisableInput`，
只在 `Release` 恢复。即使接通，玩家拿到的也是一个**动不了的选手**。

#### 修复

`ModeHRuntimeModule_CombatFlow.TryBeginErrorSwapIfDue` 作为唯一生产调用点，
每帧轮询 `ErrorTriggered && !ErrorSwapAttempted`；新增 `_errorSwapAttempted` 闩
（没有它，2 秒 deadline 回滚后条件立刻重新成立，玩家会被反复夺走控制权）；
`RestoreFromSnapshot` 补 §17.6.5 第 8 条的重建（复用同一路径，带 profileId 身份门）；
租约新增 `YieldInputForErrorSwap` / `ReclaimInputAfterErrorSwap`（只动自己的 token，
`_inputDisabled` 保持 true 使 Release 分支不变，最终态恒为「输入已恢复」）。
实测入口：F3 用例 `MODE_H_ERROR_SWAP`（owner 裁决：实测放 F3，不进生产认证）。

#### 遗留

互换期间光标被官方锁定、铃是 uGUI 按钮，因此点不到铃（结束后仍可用、未消耗）；
被接管选手的背包在互换期间可达（§17.6.5 只保证看台身体不碰真实仓库）。两条列入 §26.5。

---
### CR-2026-09-03-015：焚天龙皇不掉词缀熔石（挂接点漏并联）

**严重级**：P2
**兼容分类**：COMPAT
**状态**：Fixed
**来源**：2026-09-03 WikiContent 内容核对批（写 Boss 页掉落清单时逐条回查代码发现）

#### 位置

- `Integration/AffixForge/AffixForgeStoneDropService.cs:56`（TryTrack，此前全仓库唯一调用点在共享路径）
- `Integration/DragonKing/DragonKingBoss.cs:356`（龙王手动掉落订阅，注释已写明不经共享路径）

#### 问题

`AffixForgeStoneDropService.TryTrack` 只在 `LootAndRewards.RegisterBossRandomLootTracking`
里被调用。焚天龙皇不走那条路径（它自己手动订阅 `BeforeCharacterSpawnLootOnDead`），
因此熔石 handler 从未挂到龙王身上——三个自定义 Boss 里只有它不掉词缀熔石。

无报错、无日志、编译与 guard 全绿，属于典型的"接线漏一处"静默失效。

同一位置的遗种巢是**已修复的先例**：`PetNestDropService.TryTrack` 早已在这里并联，
且带注释说明原因。熔石在同一批接线（`5d2a0e3`）里加入共享路径时漏做了这一步。

对照确认不受影响：后山种子挂在 `AddBossSpecialLootToLootboxCoroutine` 内，龙王经
`OnBossBeforeSpawnLoot_LootAndRewards` 仍会走到，种子掉落正常。

#### 修复

`DragonKingBoss.cs` 三处并联，逐字照搬遗种巢既有写法：生成侧紧随手动掉落订阅调
`TryTrack`，离场与死亡两个清理点各调一次 `ClearTracking`。服务内部幂等、开关关闭时
早返，因此 dormant 契约与掉落概率均不变。

`tests/AffixForgeInvariantGuard.py` 新增 `check_forge_stone_drop_wiring`，三条断言
（共享路径挂接、龙王挂接、龙王两处退订 + TryTrack 必须紧随订阅）均经反向验证转红。

#### 遗留

实机 smoke 待人工：8% 概率下建议至少刷 20 次龙王再判定。

---
### CR-2026-09-03-014：ApplyRetirement 零调用点，名人堂把冠军与替补记反

**严重级**：P1
**兼容分类**：WIRE+
**状态**：Fixed
**来源**：2026-09-03 可达性接线批（零调用 grep + 读侧复核；未运行验证）

#### 位置

- `ModeH/ModeHTransferMarket.cs:261`（ApplyRetirement，零调用点）
- `ModeH/ModeHRuntimeModule_CombatFlow.cs:906`（BuildHallOfFameRecord 读 contractMain）

#### 问题

退役只写在 profile.status 上，合同槽从不结算。排兵布阵不受影响
（`GetLiveContractProfileIds` 本来就过滤 Retired，下一场照样派活人上），
但名人堂 `BuildHallOfFameRecord` 直接读 `contract.contractMainProfileId` 认冠军、
读 `contractSubProfileId` 填 `substituteHistory`。
结果是：主选手中途退役、替补顶上并夺冠时，**名人堂把已退役的主选手记成冠军，
真正打完 3-6 场的替补反被记成替补**——两个字段整个对调。

#### 修复

`BeginMatchSettlement` 在两次 `ResolveRestRecovery` 之后、虚拟筹码结算之前调用一次
（`ResolveDownInjury` 是赛季里唯一写 Retired 的路径，`ResolveRestRecovery` 只能解除
从未登场者的带伤、不可能反退役，所以合同槽结算必须排在人事步骤最后）。
`false` 返回不需要新路由：那一支意味着两名合同选手都已退役，
`RouteAfterIntermission` 已经会走 `FinishSeason("no_live_contracts")`，
且 `live.Count == 0` 短路在 `EnterHallOfFame` 之前。

#### 遗留

晋升后替补槽被清空，「替补顶上夺冠」这一支的 `substituteHistory` 会是空的
（冠军字段本身已修好）。要记录被晋升者需加持久字段，而该 DTO 进 canonical digest，
加字段会让所有已存名人堂信封 VerifyDigest 失败。留待单独评估。

---
### CR-2026-09-03-001：模式H 押品脱离仓库后阶段推进失败无回滚，真实物品永久丢失

**严重级**：P0
**兼容分类**：BREAKING（玩家真实资产）
**状态**：Fixed
**来源**：2026-09-03 七日全面审核（静态确认，含官方 API 与设计稿逐条对照）

#### 位置

- `ModeH/ModeHWarehouseStakeJournal.cs:481`（TryRemoveEscrow 末步）
- `ModeH/ModeHWarehouseStakeJournal.cs:690`（TryCancelWithoutRemoval 只看内存态）
- `ModeH/ModeHSaveFlushCoordinator.cs:173`（IsSaving 时写盘必然失败）

#### 问题

`TryRemoveEscrow` 先 `inventory.RemoveAt` 把押品摘出仓库，再 `TryAdvancePhase(EscrowRemovedDurable)`。
该推进内含落盘，`SavesSystem.IsSaving` 时返回 `flush_deferred_is_saving`，phase 回滚到
`EscrowSnapshotDurable`，但**物品不回滚**——只活在内存 `_escrowItems` 里。
同一函数的上一条失败路径（TryComputeInventoryDigest）是有 `RollbackDetached` 的，此处漏了。

#### 影响

三条出路全部封死：① 本会话取回走 `TryCancelWithoutRemoval`，被 `cancel_escrow_still_held`
挡死（a570162 新加的关停/挂起/生成失败三条返还路径全部经此函数，全部失败）；
② 重启/切槽后 `LoadPersisted` 清空 `_escrowItems`，该检查失效，journal 被静默归档成
`CancelledTerminal`（语义是"已证明从未移除"）；③ 玩家侧 `PrepareLockedMatch` 失败只写
`DevLog`，正式构建被 `[Conditional]` 剥离，按钮毫无反应。触发条件是「押品锁盘时恰逢官方自动存档」。

违反设计稿 `docs/设计提案/2026-08-17_斗蛐蛐新模式创意脑暴.md:1744`「匹配不到 pre-image → 人工介入」。

#### 修复

① `TryAdvancePhase` 失败分支补 `RollbackDetached`，与既有分支对称；
② 新增 `VerifyEscrowStillInInventory`：逐项 `CountOccurrences` 与 `preCount` 比对，
不足即 `EnterManualIntervention`。用逐项比对而非整仓 digest 全等，避免"玩家挪了别的东西"误报。

#### 验证需求

Windows 编译 + 513 guard 通过；`ModeHStakeJournalGuard` 新增两条断言并经反向验证。
**实机待做**：Dev 下令 `RequestStakeJournalWrite` 返回 false，确认物品回到仓库。

---
### CR-2026-09-03-002：模式H 濒退制「休息一场解除带伤」完全未实现

**严重级**：P1
**兼容分类**：COMPAT
**状态**：Fixed
**来源**：2026-09-03 七日全面审核（零调用点 grep 双重复核）

#### 位置

- `ModeH/ModeHCombatTelemetry.cs:169`（HasRested 零调用）
- `ModeH/ModeHInjuryAndScarSystem.cs:355`（injuryId 只写不清）
- `Localization/ModeHLocalization.cs:219-220`（Injury_Rested / Injury_Retired 零消费）

#### 问题

owner 2026-08-17（濒退制）与 2026-08-18（按实际登场判定休息）两次裁决冻结的规则，
代码里只有「进带伤 / 进退役」两条边，没有任何从 `Injured` 回到 `Available` 的路径。
判定用的积木 `HasRested` 写好了但全仓库零调用；两个文案 key 注入了但无人消费。

#### 影响

把带伤选手按在替补席完全没有收益（一个核心战术抉择是空的）；伤病 debuff
（腿伤/手伤/护具受损/旧伤/心气受挫）在其后**每一场**都生效；赔率惩罚
`starterInjured -5` / `relayInjured -3` 永久挂着。六场赛季比设计难度显著更高，
选手实际是「两条命、无恢复」。

#### 修复

新增 `ModeHInjuryAndScarSystem.ResolveRestRecovery`（清 `injuryId` + 复位 `Available`）；
`BeginMatchSettlement` 在两次 `ResolveDownInjury` 之后对 starter/relay 两席各调一次；
结算页消费两个文案 key。休息名单只存运行时 `_restedProfileIds`，**不进持久化 DTO**——
`ModeHCanonicalDigest` 按反射遍历全部公有字段，加字段会让已存赛季 VerifyDigest 失败进写屏障。

#### 验证需求

`ModeHStructureGuard` 新增断言（含"两席都要结算"）并经反向验证。
**实机待做**：主将倒地带伤 → 下一场留替补席不登场 → 确认解除且结算页显示「完整休息」。

---
### CR-2026-09-03-003：模式H 锁盘按钮所有失败原因均静默

**严重级**：P2
**兼容分类**：COMPAT
**状态**：Fixed
**来源**：2026-09-03 七日全面审核

#### 位置

- `ModeH/ModeHRuntimeModule_MatchFlow.cs:863-868`

#### 问题

`PrepareLockedMatch` 失败只写 `DevLog`，而它带 `[Conditional("BOSSRUSH_DEV")]`，
正式构建里整个被剥离。按钮 `Interactable` 也不检查这些前提。
可返回的失败原因含 `lock_command_missing`、`match_no_selectable_command`、
`lock_selection_missing`、`match_roster_no_live_contract` 与全部押品失败。

#### 影响

玩家点「锁盘」毫无反应，与按钮损坏无异，被堵在赔率页。`lock_command_missing` 现实可达：
`GetSelectableCommands` 按 stableKey 取，某选手若无通过认证的口令即为空。
这是继拍铃、押品选择之后的第三处同类静默。

#### 修复

新增 `ResolveLockRejectReason` 按原因分档（押品类委托既有押品文案），
新增 `LockReject_CommandUnavailable` / `LockReject_RosterMissing` / `LockReject_Generic`
三条中英文案；绝不把内部 reasonId 原文展示给玩家。

#### 验证需求

`ModeHStructureGuard` 断言失败分支必须 `ShowMessage` 且不得直接展示 reasonId，经反向验证。
**实机待做**：制造一次锁盘失败确认有提示。

---
### CR-2026-09-03-004：ERROR 互换期间的击杀归属渗入图鉴与日报

**严重级**：P2
**兼容分类**：COMPAT
**状态**：Fixed（owner 2026-09-03 裁决：Mode H 击杀不计入；日报整个采集器跳过）
**来源**：2026-09-03 七日全面审核

#### 位置

- `Integration/Codex/CodexKillCollector.cs`（OnGlobalDead / OnGlobalHurt）
- `Integration/DailyReport/DailyReportStatsCollector.cs`（IsActive）

#### 问题

两个全局采集器只按 `info.fromCharacter.IsMainCharacter` 过滤，而 ERROR 完整互换期间
官方会把归属改写成主角（`ModeHEventRouter.SetErrorSwapControlledParticipant` 的存在即为佐证）。
于是同一场 Mode H 比赛里 99% 的击杀（选手打的）不计、ERROR 那次却计。

对照：战役侧本就安全（`ResolveCampaignCurrentMode` 对 G/H 返回 null）；
成就侧不可达（`Assets/Data/ModeH/BossProfiles.json` 排除表已含 `DragonDescendant` / `boss_dragonking`）；
PetNest 侧不可达（随从被 `PetNestModeGate` 挡在 Mode H 外）。

#### 影响

擂台 Boss 可进鸭皇图鉴并污染「最快击杀」记录；日报战绩与悬赏可在观战模式里推进。

#### 修复

两处加 `ModBehaviour.IsModeHRunInProgressSafe()` 门控（该门面已被 RandomEventModeGate、
PetNestModeGate 用于同一目的）。日报按 owner 选择放在 `IsActive` 总闸上，一次覆盖
击杀/双向伤害/玩家阵亡三类，避免「击杀不算但伤害算」的自相矛盾。
保留 `CodexTuning.ModeIdModeH` 常量与 `FormatModeName` 分支（存档兼容面）。

#### 验证需求

`CodexKillTrackingGuard` / `DailyReportPersistenceGuard` 新增断言并经反向验证。
**实机待做**：Mode H 打完一场后确认图鉴与日报无新增。

---
### CR-2026-09-03-005：战役交付退款失败时可重复领取奖金

**严重级**：P3
**兼容分类**：COMPAT
**状态**：Fixed
**来源**：2026-09-03 七日全面审核

#### 位置

- `Campaign/CampaignProgressService.cs:307-372`

#### 问题

交付先发钱再写状态，写状态失败则退款。但退款也失败时章节仍是 `ReadyToDeliver`，
玩家可再次交付再拿一次奖金，此前只有一句文案「请勿重复交付」拦着。

#### 影响

经济漏洞面。需要「写盘失败 **且** 退款失败」两个低概率事件同时发生，实际概率极低。

#### 修复

新增会话级闩 `_cashPaidPendingChapterId`：发钱成功即置位，写盘成功或退款成功则清空；
重试交付时若闩命中则跳过发钱、只补写状态。换槽与静态复位一并清空。
**已接受的残留**：闩是会话级，跨重启失效——不为 P3 增加存档字段。

#### 验证需求

静态；实机不必专门构造（触发条件本身极罕见）。

---
### CR-2026-09-03-006：ResolveFallbackActions 已成死逻辑

**严重级**：P3
**兼容分类**：SAFE（仅注释）
**状态**：Fixed（按注释澄清，**刻意不删代码**）
**来源**：2026-09-03 七日全面审核

#### 位置

- `Utilities/ModeExtractionPointFactory.cs:69-73`

#### 问题

CR-2026-09-02-006 把它挪到 `ClearPersistentEvents` 之后（修复本身正确），
而后者是整体 `new UnityEvent()` 替换，此后 `GetPersistentEventCount()` 恒为 0，
函数内循环永不执行、恒返回 (true, true)。

#### 影响

无行为影响，但会误导后来者以为仍有「官方回调探测」能力。

#### 修复

**不删除函数**：`tests/ZombieModeExtractionFactoryGuard.py:50-73` 把
`ClearPersistentEvents < ResolveFallbackActions < ConfigureEvents` 的顺序冻结为不变式，
删掉等于抹掉该 finding 的回归防线（AGENTS.md 4.10 不得为改动放宽 guard）。
改为补注释说明现状与「何时会重新生效」。零行为改动。

---
### CR-2026-09-03-007：GameQuality 的 CS0649 注释与实际取色不符

**严重级**：P3
**兼容分类**：SAFE（仅注释）
**状态**：Fixed
**来源**：2026-09-03 七日全面审核

#### 位置

- `ModeH/ModeHRuntimeModule_MatchFlow.cs:362`

#### 问题

注释称「留 0 时渲染器走 accent 色」，实际 `ModeHUI.ResolveRarityColor(0)` 返回
`BossRushUIColors.RarityCommon`；accent 只在 `IsAnomaly` 时才换成 Warning。

#### 影响

表现无害（所有选秀卡统一中性描边），但「刻意不赋值」的理由写错了。

#### 修复

改注释为真实取色，保留结论（该 CS0649 是有意的）。

---
### CR-2026-09-03-008：摘要集合语义清单漏登记第二份入场名单

**严重级**：P3
**兼容分类**：COMPAT（未上线，owner 确认可直接优化）
**状态**：Fixed
**来源**：2026-09-03 制定修复计划时发现

#### 位置

- `ModeH/ModeHCanonicalDigest.cs:50`

#### 问题

入场名单在两个 DTO 上各有一份：`ModeHMatchRosterDto.enteredProfileIds`（:188）与
`ModeHMatchReportDto.entrantIds`（:358）。`SetSemanticFields` 长期只登记前者，
后者从未参与排序去重——而它来自 `HashSet` 枚举转 List。

> 首次记录时曾误判为「清单指向不存在的字段」并改成替换，
> 经 guard 反向验证发现 `enteredProfileIds` 也是真实字段，已更正为两份都登记。

#### 影响

潜在：`entrantIds` 顺序若变化会让同一逻辑状态算出不同摘要，误判
`season_digest_mismatch` 并进写屏障（押品禁用、恢复壳接管）。当前顺序实际稳定，未触发。

#### 修复

两份都登记；`ModeHStructureGuard` 新增断言——清单字段必须在 DTO 中真实存在，
且两份入场名单都必须登记，防止再次漂移。

---

### CR-2026-09-03-009：非本波 Boss 经掉落漏斗推进波次（跳波 + Mode D 跨模式串台）

**严重级**：P0
**兼容分类**：COMPAT
**状态**：Fixed
**来源**：2026-09-03 f9b83c0 以来 365 个改动 .cs 的审核（静态确认 + 逐调用点实读）

#### 位置

- `WavesArena/WavesArena.cs:348`（HandleBossDeath 无本波成员校验）
- `LootAndRewards/LootAndRewardsRandomBossLoot.cs:296`（唯一没有成员证明的调用点）
- `Utilities/EnemySpawnCore.cs:447` / `:1023`（isBoss 一律登记 bossSpawnTimes）
- `RandomEvents/RandomEventEffectsBridge_Spawn.cs:92`（乱入 Boss 走 Legacy 掉落追踪）
- `ModeD/ModeDWaves.cs:79` / `:255` / `:555`（Mode D 置 IsActive 且 Boss 同样进掉落追踪）

#### 问题

`HandleBossDeath` 的三个调用点里，`OnEnemyDiedWithDamageInfo` 的两个已先证明成员身份，
但掉落漏斗那条走的是逐角色 `BeforeCharacterSpawnLootOnDead` 钩子，只验「在不在
`bossSpawnTimes` 里」——任何走共享刷怪核心且 `isBoss=true` 的 Boss 都满足。
唯一的排除项是 `bossName.Contains("DragonDescendant")` 名字启发式。

#### 影响

1. **标准 / 无间炼狱跳波**：杀死随机事件乱入 Boss（`RndEvt_Intruder_*`）即推进波次。
   `ProceedAfterWaveFinished` 还会 `currentBoss = null` 把本波真 Boss 丢出状态机，
   玩家再打死它时掉落漏斗**再推一次波**。最后一波则提前触发 `OnAllEnemiesDefeated`
   （通关横幅 + 奖励箱），场上还留着没打完的 Boss。
2. **Mode D 跨模式串台**（同一根因，独立于随机事件）：Mode D 会
   `SetBossRushRuntimeActive(true)`，其 Boss 死亡同样落到这里，于是
   `ProceedAfterWaveFinished → StartNextWaveCountdown → SpawnNextEnemy`
   **把标准竞技场的 Boss 刷进 Mode D**；`currentEnemyIndex` 攒够还会在 Mode D 里
   放标准通关演出。`OnEnemyDiedWithDamageInfo:219` 早有 `modeDActive` 早返，这条一直漏着。

`tests/RandomEventsWaveIsolationGuard.py` 的 docstring 明确点名了「跳波」这个失效模式，
但它只静态检查 `RandomEvents/` 目录内是否出现波次符号，看不到经共享刷怪核心的间接路径。

#### 修复

`HandleBossDeath` 在**成就与去重之后**加一道 `IsCurrentWaveBossMember` 分界线：
分界线之上是与单次击杀绑定的记账（`countedDeadBosses` / `UnregisterEnemyRecovery` /
`CheckBossKillAchievementsOnce`），之下才是波次账（无间炼狱现金池 / `currentWaveBosses`
摘除 / `defeatedEnemies` / 推波）。

`modeDActive` 判在成员 helper 内而**不是**方法开头：Mode D 的 Boss 击杀成就只经
`HandleBossDeath` 的 `CheckBossKillAchievementsOnce` 计数（全仓库仅两个调用点之一），
顶部早返会把它整条掐掉——这一点由本轮设计复核发现并纠正。

成员判定复用 `OnEnemyDiedWithDamageInfo` 的三层比对（引用 → Health → gameObject），
多 Boss 档查 `currentWaveBosses` 后回落 `currentBoss`（后者是无条件赋值的），
异常一律 fail-closed。新增 `tests/WavesArenaBossMembershipGuard.py`（反向验证 5 项转红）。

---

### CR-2026-09-03-010：空投补给箱到时即销毁，不看玩家是否正在开箱

**严重级**：P1
**兼容分类**：COMPAT
**状态**：Fixed
**来源**：同上

#### 位置

- `RandomEvents/RandomEventCatalog.cs:266`（OnCleanup 无条件 ClearScope）
- `RandomEvents/RandomEventDirector.cs:562`（EndActiveEvent 在 OnCleanup 后**无条件**再 Clear 一次）
- `RandomEvents/RandomEventsTuning.cs:78`（AirdropDurationSeconds = 45f）

#### 问题

空投箱由 `ctx.Scope` 托管，事件到时（45s，含 2.6s 下落）即被 `Destroy`，
不检查玩家是否已到达或正开着战利品界面。落点距玩家至少 18m，而一场 Boss 战常超过 45 秒。
它是权重最高的事件（30）。同组金鸭雨反而写明「掉在地上的现金不回收——它已经是玩家收益」，
两者口径不一致。

#### 影响

玩家眼前的箱子连同没拿走的东西一起消失；正开着界面时还会留下一个指着已销毁目标的面板。

#### 修复

关键是**延到期而不是延清理**——`EndActiveEvent` 的兜底 `ctx.Scope.Clear` 无条件执行，
在 `OnCleanup` 里放行无效。改为覆写 `OnTick`，在玩家开着箱子时把
`ctx.DurationSeconds` 推到 `ElapsedSeconds + 3s`，累计上限 120s（owner 拍板）。
判据用 `InteractableBase.Interacting`（无副作用纯属性），
`LootView.TargetInventory` 作兜底且库存引用在 `OnTrigger` 缓存一次
（`InteractableLootbox.Inventory` 不是纯 getter）。销毁前先关界面。

只有 `Expired` 一条路径可延后；`RunEnded` / `SceneChanged` / `SwitchDisabled` /
`HostDestroyed` / `DebugForced` / `TriggerFailed` 都直接调 `EndActiveEvent`，照旧强制销毁。
硬帽是必需的：并发恒 1，无上限则玩家挂着界面即可让本局后续事件全部不再触发。

代码拆进新文件 `RandomEvents/RandomEventAirdropHold.cs`（partial）：内联会把
`RandomEventCatalog.cs` 顶到 1237 行，超 `LargeFileBudgetGuard` 的 1200 硬预算（实测转红）。
文件名刻意不以 `RandomEventCatalog` 开头，否则 `RandomEventsWaveIsolationGuard` 的
子类/OnCleanup 配平计数会被多数一次。新增 `tests/RandomEventAirdropHoldGuard.py`（反向验证 7 项转红）。

---

### CR-2026-09-03-011：战役目标追踪按「进场景」而非「开局」武装，且换模式不解除

**严重级**：P2
**兼容分类**：COMPAT
**状态**：Fixed
**来源**：同上

#### 位置

- `Campaign/CampaignModeBridge.cs:117`（标准分支只看 bossRushArenaActive）
- `Campaign/CampaignModeBridge.cs:112` / `:153`（丧尸用 LifecyclePhase != None）
- `Campaign/CampaignObjectiveTracker.cs:73`（模式不符时 return 但不解除）

#### 问题

1. `bossRushArenaActive == true && IsActive == false` 是一等长存状态（整个大厅期都是它）。
   第 1 章的无伤目标因此在玩家走去路牌的路上挨一下伤就被判死；胜利后
   （`IsActive` 已复位、`bossRushArenaActive` 仍为真）追踪还赖着不走。
2. `EnsureArmedFor` 在「当前章节模式 ≠ 传入模式」时直接 return **不解除**：
   接了第 1 章再去打 Mode E，`_armedMode` 停在 `"standard"`，Mode E 里挨一下伤
   会经 `ReportPlayerDamaged` 把第 1 章无伤判死（Mode E 的 `GetCampaignCurrentWave` 返回 0）。
3. 丧尸的 `LifecyclePhase != None` 从 `SelectingMap` 就为真——玩家还在基地点地图选择界面
   就已武装。

#### 修复

标准分支追加 `IsActive`；`EnsureArmedFor` 在模式不符时先 `ResetSession()`；
丧尸两处改用模式自己的权威判据 `ZombieModePhaseGuards.IsRunActive`
（`IsZombieModeActive` 用的就是它）。

胜利结算不受影响：`SetBossRushRuntimeActive(false)` 与 `NotifyCampaignStandardCleared()`
之间没有 await，且四条 Notify 漏斗各自会先调 `EnsureArmedFor`，此时仍是本局武装状态。

> 首次落地时把 `IsRunActive` 写成 `ZombieModeTuning.IsRunActive` —— 它与
> `ZombieModePhaseGuards` 同在 `ZombieModeTuning.cs` 一个文件里，探查按文件定位后
> 类名归属记错。**Windows 真编译当场报 CS0117 拦下**，再次印证 AGENTS 4.2：
> guard 全绿不能替代编译。

**已知残留**（accepted）：Mode D 自身也有「已进图、未开波」窗口，但期间
`ModeDWaveIndex == 0` 使 `ReachWave` 不会误报，竞技场此时已清场故 `MeleeKills` 也无从累加。

---

### CR-2026-09-02-001：F3 直载基地子场景导致黑屏，模式失败返程污染后续用例

**严重级**：P1 Major

**兼容分类**：`COMPAT`

**状态**：Fixed（2026-09-02 完整 F3 报告验证）

**来源**：用户黑屏反馈、Player.log、`BossRushValidation_20260901_143149_397.log` 与实际调用链。

原 `DebugAndTools/F3GameplayValidationRunner.cs` 用 `LoadScene("Base_SceneV2")` 直载子场景，
日志只见该子场景、不见完整 `Base`；加载任务结束即记 PASS，终态还跳过 AfterInit。
同时 H 认证全拒返基地后，后续场内用例仍在基地启动，造成受污染的 PASS/FAIL。
新 `F3GameplayValidationScenes.cs` 使用官方 `LoadBaseScene` 并核对完整就绪状态；
`F3GameplayValidationStages.cs` 每个场内用例前恢复竞技场，已有加载结束前不发起第二次加载。
基地/竞技场未就绪则跳过依赖用例，如实记录状态；不放宽正式 H 认证退出约束。
`BossRushValidation_20260902_114245_015.log` 中返程、完整就绪、最终清场/回读均 PASS，
SUMMARY 完整输出，H 拒绝后的竞技场恢复也通过。本次报告未复现加载黑屏。

### CR-2026-09-02-002：F3 终章 DamageInfo 零值初始化造成伤害空引用

**严重级**：P1 Major

**兼容分类**：`COMPAT`

**状态**：Fixed（2026-09-02 实机 CAMPAIGN_FINAL_BOSS PASS）

**来源**：Player.log 7400 行与官方 `DamageInfo` / `Health.Hurt` 源码。

`RunCampaignFinalBoss` 使用无参 `new DamageInfo()`，对 struct 不会调用带可选参数的构造器，
`elementFactors` 为 null；官方 Hurt 读取其 Count，必然空引用。改为 `new DamageInfo(player)`
并填写受击目标与位置，继续走真实 Hurt/死亡/呈现链。新报告 death_presentations=1，清理 PASS。
后续第三轮报告的曲目加载、播放及租约用例也已通过，见 CR-2026-09-02-007。

### CR-2026-09-02-003：F3 验收源码引用三个不存在的 API，阻断正式编译

**严重级**：P0 Blocker

**兼容分类**：`COMPAT`

**状态**：Fixed（Windows 正式编译通过）

**来源**：Roslyn CS1061、官方 DamageInfo 与 Mode H 租约类定义。

`F3GameplayValidationRunner.cs` 的 `damage.damageCreator`、
`ModeHRuntimeModule_SceneFlow.cs` 的 `_arenaLease.Dispose()` / `_spectatorLease.Dispose()`
均不存在。伤害改用实际字段；`ForceResetStateForValidation` 改用既有完整
`ReleaseRuntimeObjects`（租约真实 API 是 Release(sceneGeneration)），保留 run 上下文先尝试
押品返还，释放对象后再清临时赛季/地图和 owner。Dev 构建通过，43 项相关守卫通过。
后续两轮 H 拒绝后的清理、场景恢复与最终泄漏差值通过；认证成功后的释放路径仍待覆盖。
完整记录见 FIX_TRACKER 的 2026-09-02 条目。

### CR-2026-09-02-004：距离休眠退订使用了角色的主场景索引

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed（第三轮 MODE_D_LIFECYCLE PASS）

F3 的 Mode D 首波和多波均记录三只狼 inactive 且存活。当前游戏 DLL 的 CreateCharacterAsync
调用 SetRelatedScene，后者将角色重挂到 MultiSceneCore 主场景父级，却按 relatedScene 子场景
登记距离休眠。helper 用 GO.scene 退订，移除失败也不报异常。现改为按已加载场景索引只移除
当前角色的登记，标准 Boss 路径也接入。F3 增补 activeSelf、父级与对象场景诊断。
第三轮报告 `BossRushValidation_20260902_121947_845.log` 中三只狼全部 active/self/parent 为 true，
实际对象场景为 Level_DemoChallenge_Main；首波生命周期通过。多波另受 CR-2026-09-02-012 阻断。

### CR-2026-09-02-005：Mode H 认证拒绝已归一化的克隆，且旧受控击杀未触发死亡

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed（第五轮首次认证与缓存实机 PASS）

12 个候选全部 audit_cannot_die。SpawnBridge 已在独立 clone 打开非 Raid 图死亡，但静态审计
先按原 preset 拒绝；同时 SetHealth(0) 只写生命值，不设置 IsDead 或触发事件。现保留其他静态资格，
改为检查实际 Health 的 CanDieIfNotRaidMap、对两个诊断 clone 执行完整 DamageInfo 的 Hurt，
实例事件监听在 finally 退订，只有 IsDead 与受伤/死亡事件齐全才通过。候选数量和缓存签名门不变。
第三轮候选均已进入真实击杀，但遇到 certification_kill_failed:NullReferenceException，
见 CR-2026-09-02-011；首次认证和缓存仍未获得实机 PASS，不能提前关闭此项。
第四轮没有逐 key 拒绝或伤害异常，流程进入整体门槛失败；口令矩阵漏测见 013。
生产认证整体 PASS 前继续保留此项的完整验证要求。

第五轮 `BossRushValidation_20260902_133917_599.log`：首次认证 45719ms、缓存 142ms，
均 drafting=True、archived=True；Player.log 记录 passed=12、common=7、overall=True。
本项认证链已闭环；全赛季、真实押品与本轮新增入场/整备问题分别验收，不扩大此 PASS 的范围。

### CR-2026-09-02-006：撤离工厂删除官方回调后仍跳过通知与返程兜底

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed（第三轮 Mode F 撤离及完整返基地 PASS）

Mode F 日志已经结算成功并退出模式，却未切回基地。ModeExtractionPointFactory 先从 prefab
持久事件判定无需兜底，再用空事件替换全部回调，两个执行方同时消失。改为替换后判断；
成功事件用一次性占有标记包住结算与兜底，避免 ZombieMode 在成功结算中重新派发同一事件导致双重返程。
第三轮 MODE_F_EXTRACTION 记录 resolved=True、stillActive=False、baseReady=True。
共享工厂的丧尸重入分支仅静态检查，丧尸实际撤离仍需单独实机覆盖。

### CR-2026-09-02-007：BGM 非空曲目表经 JsonUtility 读取后数组为空

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed（第三轮曲目加载、播放与租约 PASS）

已部署文件与源码哈希一致且含 2/2/2 条目，运行时却多次记录 boss=0/stinger=0/jukebox=0。
协调器未获得任何可播放曲目，租约用例随之失败。新增 BossBgmTrackTable 显式复用既有 token parser，
三组数组与可选默认值独立探针通过；不改音频资源或龙王旧 mp3 路径。
第三轮 Player.log 记录 boss=2/stinger=2/jukebox=2，女巫与龙裔播放/停止均有记录，
BGM_OWNER_LEASES 的共享、替换恢复与最终清空断言全部通过。

### CR-2026-09-02-008：F3 在 Mode F 退出重置后读取瞬时结算标志

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed（第三轮 MODE_F_EXTRACTION PASS）

成功事件同步调用 ExitModeF 并 Reset ExtractionResolved，测试随后读到 false。
现比较生产成功结算计数增量，并同时验证模式退出与基地完整就绪，避免把“结算过”误当作“撤离完成”。

### CR-2026-09-02-009：F3 在标准 Boss 完成登记之前击杀创建中对象

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed（第三轮 STANDARD_VICTORY_REWARD PASS）

Player.log 的 ForceKillAllEnemies 先于“记录 Boss 生成信息”和“生成成功”。
全场扫描能提前发现官方 async 创建中的角色，此时死亡未命中 currentBoss。
改为等生产登记的活跃 Boss，再定点 Hurt 并验证 IsDead；胜利仍由原波次逻辑与奖励箱断言确认。
第三轮记录 spawned=1、victory=True、rewardCrate=True。

### CR-2026-09-02-010：F3 多波测试把波次推进误当作下一波生成完成

**严重级**：P2 Minor

**兼容分类**：`COMPAT`

**状态**：Fixed（第四轮 MODE_D_MULTI_WAVE PASS）

ModeDStartNextWave 先增加编号，再异步生成新怪。旧测试自动开波后立即读取可玩数量，可能读到零；
击杀后再全场 Destroy 清理也可能碰到零间隔启动的下一波。改为快照并逐只 Hurt 当前波登记角色，
推进后有界等待新波活跃角色；两个波次仍都必须有真实活跃、存活、敌对的登记对象才 PASS。
第三轮首波活跃，但第二只狼的伤害链抛 FormatException，未走到新波检查，见 CR-2026-09-02-012。
第四轮 `BossRushValidation_20260902_124241_090.log` 记录 wave=1->2、playable=3/3、
advanced=True、manual_push=True；两波激活/存活/敌对均确认。

### CR-2026-09-02-011：Mode H 诊断伤害空来源与外部死亡订阅不兼容

**严重级**：P1 Major
**兼容分类**：COMPAT
**状态**：Fixed（第四轮逐 key 不再拒绝或抛伤害异常；总体认证另受 013 阻断）

第三轮首次认证和缓存用例中，12 个候选均在真实 Hurt 返回前出现 NullReferenceException。
认证构造 `new DamageInfo((CharacterMainControl)null)`；只读反编译本机已安装的
BattlefieldTypeKillNotice.dll，发现其 Health.OnDead 订阅直接读取 damageInfo.fromCharacter.IsMainCharacter，
没有空值保护。空来源与该订阅的组合必然异常，独立模拟回调也复现；旧日志只记录异常类型，
尚无原始完整栈能唯一归因这次异常，修复后游戏内结果仍需确认。

现在两只诊断 clone 互作受控伤害来源，拒绝 null、自身或主玩家来源，避免认证记入玩家击杀提示；
保持真实 Hurt、IsDead 与受伤/死亡事件断言。异常输出 stable key、队伍、已观察事件和完整栈，
finally 始终退订临时监听；不吞异常冒充通过，不修改外部 Mod。
第四轮日志没有认证伤害异常或逐 key 拒绝，仍由整体门槛拒绝。结合当前执行链可确认
逐候选已完成，但这不代表 H 完整开局和缓存通过；原异常无完整栈的归因限制仍保留。

### CR-2026-09-02-012：F3 同帧连续击杀触发外部经验提示的未初始化文本解析

**严重级**：P2 Minor
**兼容分类**：COMPAT
**状态**：Fixed（第四轮 MODE_D_MULTI_WAVE PASS，无该伤害格式异常）

第三轮 MODE_D_MULTI_WAVE 首波三只狼正常激活；前两只已进入死亡结算，第二次 Hurt 抛 FormatException。
已安装 BattlefieldTypeKillNotice 的 BuildUI 未将经验文本初始化为数字，首次 tween 在后续更新才写入；
同帧第二次击杀却会对旧模板文本调用 long.Parse。F3 原方法在一个循环内同步杀完快照，
符合该触发条件，模拟回调已复现。原日志无完整异常栈，保留新日志与实机确认要求。

当前波快照改由 IEnumerator 每次真实击杀后让出一帧，使 UI 更新有机会完成；
仍检查每只 IsDead，异常记完整栈并 FAIL，推进后仍等待新波真实生成。
改动只覆盖 F3 批量驱动节奏，不修改玩家正常战斗、官方死亡链或外部 Mod 的群体击杀实现。

### CR-2026-09-02-013：Mode H 口令矩阵无人写入，缓存也未恢复逐效果证据

**严重级**：P1 Major
**兼容分类**：COMPAT
**状态**：Fixed（第五轮首次认证与缓存实机 PASS）

第四轮逐 key 无拒绝，却仍 certification_threshold_not_met。全仓调用检查确认
RecordEffectStatus 只有声明、生产零调用；默认只有 steady 的自结算效果通过，
所有 key 永远只有 1 条可用通用口令，无法满足冻结的至少 3 条门槛。真实矩阵源码和内容数据
独立探针复现。认证后才首次 BindBuildSignature 还会清空矩阵，缓存 ApplyReportToRegistries
只物化 preset、不恢复 commandStatuses，修好测量后仍会丢证据。

新增已登记编译的 ModeHCommandCertificationProbe，复用生产 adapter 在两只实际激活的诊断
角色上采样可读、可还原字段：每条跨至少 3 帧、累计 0.3 秒，先读后重申，双方均保持且
还原成功才写逐效果 VerifiedBehavior；目标/路径/技能 marker 无对应遥测，保留 ReportOnly。
不改 8 候选、5 原型、3 口令门槛；取消时还原 adapter、退订并回收双角色。

测量前绑定三签名并清当前 key；报告涵盖通用和招牌口令。缓存恢复仅接受 Passed 记录中的
已知逐 effect 合法状态，聚合口令状态重新派生并重查门槛，不能由缓存整体 Passed 绕过。
日志增加逐 key 可用口令数、汇总计数和实际门槛失败原因。12 项生产源码模拟检查通过，
不等于 Unity AI 运行时认证；下一份完整 F3 需确认首次认证、缓存命中、退出清理和最终状态。

第五轮 `BossRushValidation_20260902_133917_599.log`：首次认证 45719ms、缓存 142ms，
均 drafting=True、archived=True；Player.log 记录 passed=12、common=7、overall=True。
本项认证链已闭环；全赛季、真实押品与本轮新增入场/整备问题分别验收，不扩大此 PASS 的范围。

### CR-2026-09-02-014：丧尸撤离验收要求正数净化点，却没有准备样本

**严重级**：P2 Minor
**兼容分类**：COMPAT
**状态**：Fixed（2026-09-02 第六轮实机通过）

第五轮 `MODE_ZOMBIE_EXTRACTION` 因 `no_points_to_verify_settlement` 失败。生产开局净化点为 0，
用例只等 6 秒，没有拾取或击杀，却要求净化点大于 0 才触发真实成功事件。
现在先用生产 `CollectZombieModePurificationPoint` 收取 3 点并回读增量，再走撤离 UI/事件、
两次派发、钱包差值、模式退出和完整返基地断言。保持生产初始值、奖励比例和人工自然倒计时场景。

第六轮 `BossRushValidation_20260902_140735_794.log`：MODE_ZOMBIE_EXTRACTION PASS，pickup=0->3，现金 26302460->26302463->26302463，active=False、base_ready=True。成功事件与重复派发已验证；自然倒计时/离圈中断仍待人工。

### CR-2026-09-02-015：H 成功创建赛季后保留入场意图，回到地图会重开 H

**严重级**：P1 Major
**兼容分类**：COMPAT
**状态**：Fixed（2026-09-02 第六轮实机通过）

第五轮 H 首次认证和缓存用例均通过，后续无 H 入场操作的场景恢复却再次出现“赛季已创建”。
Player.log 9637 行创建新赛季，9691 行终章主动让路，9718 行销毁迟到 Boss，最终终章生成超时。
`TryMatchModeHSceneIntent` 只匹配而不消费，成功 `CreateDraftingSeason` 与关停也未清理。
首份赛季写入/读回成功后用既有 `CancelPendingEntry` 消费意图及预扣票所有权；失败时仍走原退款。
Dev 强制清理增加遗留意图回收，F3 在正常归档后核对意图已消失。

第六轮 `BossRushValidation_20260902_140735_794.log`：两次 H 入场均 intent_cleared=True、archived=True。Player.log 只创建两个测试赛季，后续重访竞技场不再额外创建；终章顺利完成。正常入场消费已验证；入场失败退款仍按独立场景验收。

### CR-2026-09-02-016：H 将冻结弹药写入装备槽，真实比赛生成反复失败

**严重级**：P1 Major
**兼容分类**：COMPAT / WIRE+
**状态**：Fixed（2026-09-02 第六轮实机通过）

第五轮真实生成出现 `kit_apply_ammo_plug_failed:starter_sidearm` 与 `starter_marksman_rifle`。
官方 `ItemUtilities.TryPlug` 只枚举装备槽，不能存入弹药；直接给 StackCount 写总量还会被上限截断。
`ModeHLoadoutKitApplicator` 改为同步填新造枪弹匣和临时选手库存，按上限分堆，严格保留冻结总量；
逐实例登记所有权，不合并到旧堆，任何失败逆序回收。校验口径、实际存入结果与真实弹匣数量。
官方直接填库存不会刷新 `_bulletCountCache`：用缓存字段失效后通过公开 BulletCount getter 重算，
字段缺失明确拒绝；绑定名称在本机反编译源码确认，不改变其它模式或玩家武器。
新增 `MODE_H_STARTER_KITS` 在已认证 Drafting 租约内逐件生成/装配全部 starter，回读槽位、
弹匣可用数与实际库存总数；超时/取消迟到实例仍回收。此测试不代表整场 AI 战斗或六场赛季通过。

第六轮 `BossRushValidation_20260902_140735_794.log`：MODE_H_STARTER_KITS PASS，8/8。步枪 loaded=usable=30 / total=120，射手枪 10/40，手枪 13/60；全部槽位 TypeID 正确。Player.log 无 kit_apply_ammo_plug_failed 或 H 技术故障。装配已验证，完整 AI 比赛/六场赛季仍未由此证明。

### CR-2026-09-02-017：终章直接销毁 Boss 未清掉熔石掉落订阅

**严重级**：P2 Minor
**兼容分类**：COMPAT
**状态**：Fixed（2026-09-02 第六轮实机通过）

第五轮终章让路销毁迟到 Boss 后，`FINAL_LEAK_DELTA` 记录 affix_stone_hooks=0->1。
熔石服务只有死亡结算、宿主销毁和下一次登记时的死引用裁剪，没有场景收尾；
终章直接 Destroy 与迟到生成分支也未经过 `ClearBossRandomLootTracking`。
现在 Integration 场景回调先 ClearAllTracking；终章两条主动销毁路径先清自身掉落登记再 Destroy，
覆盖场景回调之后才到达的实例。自然死亡流程不提前清理，不改变掉落概率、不用测试清场抹平计数。

第六轮 `BossRushValidation_20260902_140735_794.log`：CAMPAIGN_FINAL_BOSS PASS，death_presentations=1、bgm_owners=0；FINAL_LEAK_DELTA PASS，affix_stone_hooks=0->0，全部被测登记回到基线。常规终章与返场已验证；主动中止/迟到生成分支仍属于故障注入边界。

### CR-2026-07-05-001：焚天龙铳切弹时容量 baseline 会被取整反推污染

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：代码审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 守卫文件：`tests/DragonKingBossGunReforgeBaselineGuard.py`

#### 问题

弹种属性切换时优先从当前已套 profile 的 Stat 反推 baseline，而不是优先使用已缓存 baseline。`Capacity` 写入前会取整，重铸过容量后连续切弹可能把真实弹匣基准反推歪。

#### 修复

`CaptureAmmoStatBaseline()` 改为优先返回 `statBaselineByItemInstance` 中的真实基准，只在缓存缺失时才从上一 profile 反推。守卫新增顺序断言，避免回退。

#### 验证需求

Windows 编译和相关 guard 通过；仍需进游戏确认重铸容量后的连续切弹面板与实际弹匣符合预期。

### CR-2026-07-05-002：焚天龙铳场景清理会丢失手持枪弹种 baseline

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：代码审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 守卫文件：`tests/DragonKingBossGunReforgeBaselineGuard.py`

#### 问题

场景加载时 `ClearDragonKingStaticCache()` 会调用龙铳 `ClearSceneCaches()`，旧实现同时清掉 per-item profile/baseline。若玩家手持枪实例仍保留已套 profile 的 Stat，后续场景重应用可能把已覆盖值当成新 baseline。

#### 修复

把场景级弹幕/命中缓存与枪实例弹种状态分离：`ClearSceneCaches()` 不再清 per-item profile/baseline；`CleanupRuntime()` / reset 路径统一清理弹种状态。守卫新增约束。

#### 验证需求

Windows 编译和相关 guard 通过；仍需进游戏切图后确认当前装填弹种属性不会二次倍率。

### CR-2026-07-05-003：焚天龙铳射击热路径每发重复写 Stat

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：代码审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs`
- 守卫文件：`tests/DragonKingBossGunReforgeBaselineGuard.py`

#### 问题

`ShootOneBullet` 兜底每发都会调用弹种属性应用，重复写入 `Damage`、`ShootSpeed`、`Capacity`、`ReloadTime`、`BulletDistance` 并在 dev 模式产生噪声日志。高射速弹种下会放大热路径开销。

#### 修复

`TryApplyAmmoProfile()` 在同一枪实例已经应用同一 profile 且 baseline 存在时直接跳过；仅 `Legacy`、`RuntimeRestore`、`SceneReapply` 强制重写。守卫禁止把 `ShootOneBullet` 加入强制重写路径。

#### 验证需求

Windows 编译和相关 guard 通过；仍需 dev 模式实机确认高射速射击不再刷弹种覆盖日志。

### CR-2026-07-01-001：售货机 UI 崩溃 — 延迟注入商品未缓存 itemInstance

**严重级**：P0 Blocker  
**兼容分类**：`WIRE-` / `OPERATIONAL`  
**状态**：Fixed  
**来源**：从旧 `docs/代码审查/CODE_REVIEW_FINDINGS.md` 与 `docs/协作/FIX_TRACKER.md` 迁移；本次文档收敛未重新进游戏验证。

#### 位置

- 修复文件：`Patches/Economy/StockShopGetItemInstanceDirectPatch.cs`
- 编译清单：`compile_official.bat`

#### 问题

商店条目延迟注入晚于原生 `StockShop.Start()` 的缓存时机，导致 BossRush 注入商品的 `itemInstance` 未缓存。`StockShopItemEntry.Setup()` 取到 null 后访问 `item.StackCount` 触发 `NullReferenceException`，售货机 UI 无法打开。

#### 修复

通过 Harmony Prefix 拦截 `StockShop.GetItemInstanceDirect(typeID)`：缓存命中时放行；缓存未命中且属于延迟注入条目时即时实例化并写回缓存，使调用点不再拿到 null。

#### 验证需求

Windows 实机：进入基地，打开售货机，确认商品显示、可购买，`Player.log` 无对应 NPE。

### CR-2026-07-02-001：许愿台弹幕当前打开轮次不会接入新拉取结果

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：本轮 git 审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/WishFountain/WishFountainUI.cs`
- 修复文件：`Integration/WishFountain/WishFountainDanmakuView.cs`

#### 问题

当面板先用本地缓存/内存缓存启动弹幕时，联网成功后的新数据只会写回缓存，不会接管当前这一次打开中的弹幕来源。结果是本轮看到的仍是旧弹幕，必须关闭再打开一次才会出现最新内容。

#### 修复

保留现有对象池、泳道和滚动逻辑，仅新增“更新内容源但不重置当前滚动”的能力：已在屏弹幕继续滑出，后续新入场弹幕改用最新拉取结果，避免整层闪断或重排。

#### 验证需求

Windows 编译通过；仍需进游戏确认打开许愿台时，已有缓存场景下联网返回后能无感接入新弹幕，且不会出现整层闪烁或输入卡顿。

### CR-2026-07-02-002：许愿台弹幕失败结果被 45 秒 TTL 缓存，重开面板也不会立即重试

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：本轮 git 审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/WishFountain/WishFountainFetchPipeline.cs`

#### 问题

弹幕读取把“最近一次失败”与“最近一次成功”共用同一套 45 秒 TTL 缓存。只要飞书鉴权或列表请求瞬时失败一次，玩家在接下来 45 秒内反复关闭再打开许愿台，也只会立即复用失败结果，不会重新发起拉取。

#### 修复

保留成功结果的短 TTL，避免每次开面板都重新鉴权拉表；失败结果不再走 TTL 短路，玩家重新打开面板时会直接重试联网拉取。

#### 验证需求

Windows 编译通过；仍需进游戏在弱网或临时断网后反复开关许愿台，确认恢复联网后无需再等 45 秒就能重新拉到弹幕。

### CR-2026-07-02-003：许愿台关闭后未解绑静态弹幕回调，旧 View 会被挂到请求结束

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：本轮 git 审查 + 静态代码验证。

#### 位置

- 修复文件：`Integration/WishFountain/WishFountainFetchPipeline.cs`
- 修复文件：`Integration/WishFountain/WishFountainUI.cs`
- 守卫文件：`tests/WishDanmakuFetchLifecycleGuard.py`

#### 问题

每次打开许愿台都会往静态 waiter 列表追加新的 success/failure lambda，但关闭面板时只递增本地版本号，没有把这些 lambda 从 `WishFountainService` 移除。慢网、切场景或频繁开关面板时，旧 View 会一直被闭包引用到请求结束，额外保留无效回调。

#### 修复

为弹幕拉取增加显式的 waiter 解绑入口；`WishFountainView.CancelDanmakuFetch()` 在关闭、重开和销毁时统一撤销本轮注册的回调，避免旧面板实例被静态等待队列继续持有。

#### 验证需求

Windows 编译通过；仍需进游戏确认弱网下频繁开关许愿台不会报错、不会留下卡住的旧 UI，也不会影响下一次打开的弹幕刷新。

### CR-2026-08-17-001：Mode G 官方快照绕过逐 key eligibility，且九波计划错误要求整局全局去重

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed（2026-08-17 owner 发布裁决已替代逐 key allowlist）
**来源**：代码审查 + 静态代码验证。

#### 问题与影响

`CreateModeGBossSnapshot()` 曾把共享过滤池中的所有普通 preset 直接加入 Mode G；未知 Mod preset、未纳管 attachment/async/死亡/掉落 owner 也可能进入。`ModeGWavePlan` 又把所有 official primary 与 6 个 reserve 做整局全局去重，合法的 6-key 池也会无故拒绝 Starting。

#### 修复与验证

新增默认拒绝的官方资格记录（stable key、revision、副作用摘要、适应能力），快照和 lookup 全部消费 registry，同 key 多引用拒绝；计划改为逐槽 reserve、同波互斥、draw bag 跨波复用。2026-08-17 owner 进一步确认现有 `GetFilteredEnemyPresets()` 池内 Boss 均可用于 Mode G，因此生产策略改为信任该池，不再要求逐 key 硬编码 allowlist；托管 Boss 排除、唯一 stable key、重复引用拒绝仍保留。2026-08-18 owner 进一步裁决：启动最低为 1 个唯一 key，6 个只是完整 primary/reserve 编排目标；1-5 个时按 runSeed 从已有 stable key 确定性复制，事务按槽位/实例而非 key 去重。同期恢复玩家自带装备入场，并移除旧路牌的独立 Mode G 选项，自动分流后的 presenter 仍保持独立确认页。`ModeGManagedBossEligibilityGuard.py`、`ModeGPlayerLoadoutGuard.py` 与全部 Mode G guards 通过。

### CR-2026-08-17-002：未知存档版本和临时挂起宿敌可能被后续对局覆盖

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：代码审查 + 静态代码验证。

#### 问题与影响

未知/不可读 profile 或 nemesis payload 会回退空 DTO，但旧 `Store()` 没有 per-key 写屏障；未来版本数据可能被 v1 覆盖。有效持久宿敌因 preset、BossFilter、revision 或 adapter 暂时不可用时，本局会选临时宿敌，玩家死亡归因又可能把原 key/Rank 改成临时击杀者。

#### 修复与验证

两个 key 独立建立写屏障，Store/flush 均拒绝覆盖，另一 key 仍可保存；RunState 冻结 `ModeGNemesisSelectionSource`，`SuspendedPersistentV1` 时失败归因只展示受保护。宿主销毁前增加不重入官方保存的最终尽力 flush。三个持久化 guards 通过。

### CR-2026-08-17-003：Mode G 奖励 API 回调异常会误判交付失败，Rewarding 死亡可能被完成回调抢占

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：官方源码调用顺序核对 + 代码审查。

#### 问题与影响

官方背包/仓库 API 可能已提交物品后才由事件回调抛异常，旧逻辑会误判失败、跳过 fallback 或重复处理；取消 materializer 的同步完成回调还可能先锁定 `RewardAbandoned`，覆盖真正的 `RewardInterruptedByDeath`。

#### 修复与验证

交付后核对 Inventory、实例消费和 Incoming buffer，三路均失败才销毁；取消前先失效 reward nonce，完成回调首先检查 nonce，非 Victory 的 `End()` 统一取消 Rewarding。`ModeGRewardGuard.py`、`ModeGDeathRoutingGuard.py`、`ModeGCleanupGuard.py` 通过。

### CR-2026-08-17-004：Mode G 启动退款所有权不唯一，后续初始化异常可能双退

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：代码审查 + 失败路径推演。

#### 问题与影响

Runtime 接管启动退款后，若 `StartRun()` 已推进但 HUD 等后续步骤抛异常，Runtime `End()` 与外层失败分支可能各退款一次；首波同步启动后也可能被外层误判为启动前失败。

#### 修复与验证

`StartModeGRuntime` 显式返回退款所有权，`ArmStartupRefund()` 后只有 Runtime 可退款，外层仅在尚未转移所有权时处理；退款逐项核对交付结果。`ModeGPlayerLoadoutGuard.py` 与正式编译通过。

### CR-2026-08-17-005：Mode G 候选地图没有显式验证状态，未实测地图会被当作 Verified

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed（2026-08-17 owner 已批准地图选择 UI 全部有效地图）
**来源**：设计契约与实际注册表静态对照。

#### 问题与影响

`ModeGMapSupportRegistry` 只有候选 scene pair 和 revision，没有 `NotVerified/Verified` 状态；`TryGetPrimaryVerifiedPair()`、`IsSupported()` 和 `IsVerifiedSceneName()` 会直接接受首发候选。当前发布开关和官方 Boss 空表还能阻断入口，但后续填表开闸时会绕过地图死亡语义、安全三元组、导航与清理 smoke。

#### 修复与验证

新增显式 `ModeGMapSupportStatus`，所有支持查询统一要求状态、当前 revision、死亡风险和安全摘要完整。2026-08-17 owner 确认地图选择 UI 中全部有效配置均为安全地图，注册表因此改为直接复用 `GetAllMapConfigs()` 并生成 Verified 快照；preview 按当前 active scene 冻结玩家实际选择的 exact pair。空 scene、空刷新点、重复 pair 或不属于 UI 配置的场景仍 fail-closed。`ModeGMapSupportGuard.py`、`ModeGReleaseAvailabilityGuard.py` 和全部 28 个 Mode G guards 通过。

### CR-2026-08-29-008：模式H 锁盘按钮转换非法，玩家被困在时停赔率页

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（静态调用链验证，双重复核）

#### 位置

- `ModeH/ModeHRuntimeModule_MatchFlow.cs:441`（`TryTransition(当前状态→LoadoutLocked)`）
- `ModeH/ModeHStateMachine.cs:117-123`（冻结表：LoadoutEditing 只允许 `{OddsPreview, Recovering, ErrorRecoveryPending, Suspended}`）
- `tests/ModeHStateMachineGuard.py:38`（冻结同表）

#### 问题

看盘→赔率页走 MatchBrief→LoadoutEditing，全仓**没有任何调用**转入 OddsPreview（grep 全部 TryTransition 调用点核实）；锁盘按钮请求的 `LoadoutEditing→LoadoutLocked` 不在冻结表内，必被拒绝且仅 DevLog。`MatchFlow:427` 注释「主干走 LoadoutEditing/OddsPreview -> LoadoutLocked」与其引用的冻结表自相矛盾——批 2（a579c3e）新代码按错记的表写成。

#### 影响

赔率页经 `ClaimModalInput` 时停 + 禁输入，页面唯一按钮无效、无关闭按钮，玩家只能靠游戏自身 ESC 菜单逃生。批 2 宣称交付的「锁盘→分帧生成→校验→回滚」整段实机**一步都走不进去**（FIX_TRACKER 批 2 条目的交付边界描述不实）。

#### 建议修复

按设计意图三选一并同步 `ModeHStateMachineGuard`：打开赔率页时补 LoadoutEditing→OddsPreview 转换（贴合注释的主干）；或冻结表补 LoadoutEditing→LoadoutLocked 边；锁盘/回退按钮加失败可见反馈。建议同时给 ReachabilityGuard 增补「每个 TryTransition 调用点的 (from,to) 必须在冻结表内」静态断言（可一并抓住 009/010）。

#### 验证需求

编译 + ModeH guard 全量 + 实机：看盘→赔率→锁盘可推进到生成段。

### CR-2026-08-29-009：模式H 生成回滚「退回看盘」转换非法，成功路径卡死在 MatchSpawning

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（静态调用链验证，双重复核）

#### 位置

- `ModeH/ModeHRuntimeModule_MatchFlow.cs:550`（`TryTransition(→MatchBrief, "combat_wiring_pending")`）
- `ModeH/ModeHStateMachine.cs:148-154`（MatchSpawning 只允许 `{MatchFighting, Recovering, ErrorRecoveryPending, Suspended}`）

#### 问题

分帧生成校验通过并回滚后，代码试图 MatchSpawning→MatchBrief 退回看盘，该边不在冻结表内。转换被拒后玩家停留在 MatchSpawning：模态页已关、只剩空 HUD 与无效拍铃，toast 却提示已退回看盘。

#### 影响

当前被 008 遮蔽（生成段不可达）；修复 008 后立刻暴露为新的困死点。

#### 建议修复

与 008 同批：冻结表补边或改走合法中转，同步 guard；见 008 的 ReachabilityGuard 增补建议。

#### 验证需求

同 008，实机走完「生成→校验→回滚→看盘」全段。

### CR-2026-08-29-010：模式H Recovering 是死态：全部技术故障出口通向无按钮的壳

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（静态调用链验证，双重复核）

#### 位置

- 转入点：`ModeH/ModeHRuntimeModule_MatchFlow.cs:56/164/306/346/356/452/580`、`ModeHRuntimeModule.cs:242`、`_UiFlow.cs:279`
- 出口：`ModeHStateMachine.cs:209-221` 允许 Recovering→MatchBrief 等，但全仓**零调用**以 Recovering 为起点发起转换
- 恢复壳动作：`_UiFlow.cs:235-271`（只有 Suspended 才给「同场重开」，且该动作只做 Suspended→Recovering——转回死路）

#### 问题

选秀失败、计划失败、生成失败、晚到 spawner 等全部技术故障入口都进 Recovering，但没有任何代码把状态转出去；`MaxAutomaticTechnicalRetriesPerMatch=2` 的重试预算永远走不到第二次。

#### 影响

任何一次技术故障后，玩家面对无可点动作的恢复壳（spectator 租约还禁着输入），只能 ESC 离场——随后触发 011 的闩锁链。

#### 建议修复

给 Recovering 接出口：自动重试消耗预算→回 MatchBrief/重建计划，超限→Suspended；恢复壳补玩家动作。属下一批恢复流程的地基，与 012 一并设计。

#### 验证需求

实机模拟计划/生成失败（可用调试开关），确认重试与挂起路径。

### CR-2026-08-29-011：模式H shutdown 闩锁永不复位：一次会话只能玩一局，二次入场吞船票并搁浅

**严重级**：P0 Blocker
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（静态调用链验证，双重复核）

#### 位置

- `ModeH/ModeHRuntimeModule_SceneFlow.cs:431`（`_commandsClosed = true`，全仓无复位写入）
- `ModeH/ModeHRuntimeModule.cs:289-290`（`_shutdownCompleted = true`；唯一复位在 OnAwake:79，宿主每进程一次）
- 被拦截入口：`_SceneFlow.cs:83`、`_MatchFlow.cs:26`、`_UiFlow.cs:150` 等

#### 问题

ShutdownRuntime 单次执行正确，但两个闩锁落下后没有 per-run 复位。可用性门不感知闩锁：再点船坞入口→船票预扣→传送进图→模块完全不响应（OnSceneLoaded 被闩锁拦截），Legacy 接管又因 ModeH intent 让位。

#### 影响

玩家站在无模式接管的原版地图上，票已消耗、无退款、无提示。由于离开 008/010 困局的唯一方式就是触发 shutdown，这条链当前几乎必踩。

#### 建议修复

入场（BeginSeasonSetup / OnSceneLoadedInternal 起点）做 per-run 闩锁复位；或可用性门感知闩锁并拒绝入场（拒绝文案接 013）。

#### 验证需求

实机同会话连续两次入场。

### CR-2026-08-29-012：模式H 恢复壳在重启后不可达，「船坞恢复分支」未实现

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（零调用 grep 双重复核；启动时序两分支待实机定夺）

#### 位置

- `OpenRecoveryShell` 全仓唯一调用点 `_UiFlow.cs:187`（活动局页面路由）；`ModeHAvailability.EvaluateRecovery` 零调用；`ModBehaviour.ModeHRuntime` 门面零消费
- `UIAndSigns/UIAndSigns.cs:854` 注释宣称「恢复入口复用同一选项（内部按可用性分流）」，但 `ModeHInteractable.OpenEntryFlow`（:162-198）只有 TryEnter 一条路，`ModeHAvailability:92-96` 在 recovery-only 时直接拒绝

#### 问题

中断赛季重启游戏后没有任何路径打开恢复壳。叠加存档恢复只在 OnAwake 跑一次（`ModeHRuntimeModule.cs:83→179-212`）、`HandleSetFile` 不重跑，两种启动顺序各有一个坏结局：(a) SetFile 晚于 Awake（常规）→ 不触发 recovery-only，玩家可开新赛季，`CreateDraftingSeason` 静默覆盖旧赛季存档；(b) 恢复逻辑生效 → recovery-only 永久为真且壳不可达，模式被自己的存档记录锁死。哪种发生需实机确认，但两种都是缺陷。

#### 建议修复

船坞入口按 EvaluateRecovery 分流到恢复壳；存档恢复挂到 SetFile 回调重跑。属下一批恢复流程主体。

#### 验证需求

实机：中断赛季→重启→船坞入口应给恢复选项且旧赛季不被覆盖。

### CR-2026-08-29-013：模式H 开局中止链全程无玩家可见文案，14 个 Unavailable_* 键零消费

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（零消费 grep 双重复核）

#### 位置

- `ModeH/ModeHRuntimeModule_SceneFlow.cs:403-422`（AbortSetup 只 DevLog）→ `ModeHEntry.cs:161-169`（AbortAndRefund 只退款+传送）
- `ModeHAvailability.cs:139-146`（`GetReasonLocalizationKey` 与全部 `Unavailable_*` 键、`Unavailable_TicketRefunded` 零消费）；入口被拒时 `LastReasonId` 无人读

#### 问题

认证失败、取消认证、租约失败、Season 写盘失败、入口被拒时，玩家被无解释地退款传回基地。文案已做、没接——对照路牌 `OnTimeOut` 路径有文案（`ModeGInteractable` 同型参照）。

#### 建议修复

AbortSetup / 入口拒绝出口统一走 ShowMessage + GetReasonLocalizationKey。

#### 验证需求

实机制造认证超时/租约失败，确认横幅出现。

### CR-2026-08-29-014：模式G 无伤成就读跨局残留的 HasTakenDamage，同进程受过伤后 flawless 永久锁死

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（唯一置 false 点与调用方 grep 双重复核）

#### 位置

- `Achievement/AchievementTriggers.cs:421-433`（`BeginModeGAchievementSession` 只清去重集）
- `Achievement/AchievementTracker.cs:84`（唯一 `HasTakenDamage=false`，入口 `BeginAchievementSession` 仅 `ModeD/ModeDWaves.cs:76`、`WavesArena/WavesArenaBossSpawning.cs:76` 调用；模式G 启动路径不经过）
- 消费点：`ModeG/ModeGRuntimeModule.cs:805-810`（`wasFlawlessAtDeath` 快照）

#### 问题

同一进程先打过任何会受伤的模式（含前一局模式G），`HasTakenDamage` 残留 true → 之后模式G 真·无伤击杀龙王/龙裔时 `kill_dragon_king_flawless`/`kill_dragon_descendant_flawless` 不解锁。进程首局无伤的 smoke 恰好测不出。

#### 建议修复

`BeginModeGAchievementSession` 内重置 `HasTakenDamage`（或调 `ResetSessionStats`，注意与 Legacy 会话语义隔离）；同步 `ModeGAchievementIsolationGuard` 等相关 guard。

#### 验证需求

实机：先打一局受伤的任意模式，再打无伤模式G，确认 flawless 解锁。

### CR-2026-08-29-015：遗种巢 会话重启后血脉目录空窗：进一次竞技场之前官方血脉在基地全面不可用

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（调用点 grep 双重复核）

#### 位置

- `PetNest/PetNestLineageCatalog.cs:129-165`（AddOfficialLineages 读 `GetFilteredEnemyPresets`，`enemyPresets==null` 时返回空表：`BossFilter/BossFilter.cs:203-206`）
- `InitializeEnemyPresets` 全部调用点均在进竞技场路径与调试面板（`Integration/BossRushIntegration_StartAndScene.cs:350` 的 `bossRushArenaPlanned` 分支、`_TravelAndSetup.cs:332/466`、`ModeE/ModeEBattle.cs:160`、`ModeG/ModeGRuntimeBridge.cs:17`、`BossFilter.cs:373`），基地启动无一触发

#### 问题

重启会话后直接在基地孵官方血脉蛋 → `lineage_unknown`（文案误导玩家以为蛋坏了）；巢页卡片显示裸 `Cname_*` key、博物馆分母可能显示「5 / 3」、遗魂账本官方血脉整行缺失、凝蛋按钮消失。进一次竞技场（或开一次 Boss 池窗）后当场自愈。5e667b2 修的是「填充后重建」，未覆盖「填充之前」这段每会话必现的窗口。

#### 建议修复

`EnsureBootstrapped` 或基地早期装配主动触发一次 `InitializeEnemyPresets`（数据源 ObjectCache 在基地即可用）。

#### 验证需求

实机：重启→基地直接孵化官方蛋成功、图鉴/账本显示正常。

### CR-2026-08-29-016：遗种巢 关开关不清掉落追踪，dormant 契约被已挂接的 per-boss handler 穿透

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审（唯一调用链 grep 双重复核；跨档污染组合链见 UNVERIFIED）

#### 位置

- `PetNestRuntimeModule` 两个关闭分支（OnSceneLoaded:107-113、OnUpdate:163-170）与 `ShutdownIfEnabledTurnedOff`（:270-291）均不调 `PetNestDropService.ClearAllTracking()`
- `ClearAllTracking` 全仓唯一调用链：`PetNestDropService.cs:340`（ResetStaticCaches）← 宿主销毁
- handler 本体（`PetNestDropService.cs:137-170`）不查开关

#### 问题

竞技场中途关闭遗种巢开关后，场上已追踪 Boss 死亡仍会记遗魂/可能掉蛋/弹「可凝蛋」提示，违反「关闭即不产蛋不记魂」契约（每只已追踪 Boss 一发）。

#### 建议修复

三个关闭/停机路径并联 `PetNestDropService.ClearAllTracking()`（一行级）。

#### 验证需求

实机：战斗中关开关→击杀已追踪 Boss，确认无遗魂进账无掉蛋。

### CR-2026-08-29-017：日报 开关关闭期间换档：跨存档槽状态渗漏并覆写新档

**严重级**：P1 Major
**兼容分类**：`COMPAT`（修复本身；现象含存档覆写风险）
**状态**：Fixed
**来源**：2026-08-29 四系统复审（退订/缓存/重置路径三环 grep 双重复核）

#### 位置

- `Integration/DailyReport/DailyReportPersistence.cs:84-101`（`ShutdownSubscription` 退订全部三个 SavesSystem 事件，含 OnSetFile）
- `:121-133`（`HandleSetFile` 是唯一槽位重置路径）；`:152-158`（`LoadOrInit` 命中缓存直接返回，不校验槽位）
- `DailyReportService.cs:765-774`（`_initialized`/`_carrySeconds` 只靠 NotifySlotChanged 重置）

#### 问题

槽 A 游戏中关闭日报开关（退订 OnSetFile，缓存保留）→ 主菜单换槽 B（无人监听、缓存不重置，全仓无兜底）→ 重开开关 → 缓存仍是 A 数据 → 任一次 Persist/官方存盘把 A 的日报 JSON 写进 B 的存档：B 的天数/签到墙/连签/悬赏 claimed 被 A 整体顶掉，可造成进度丢失或重复领悬赏现金。删档变体同理。

#### 建议修复

`LoadOrInit` 记录 `SavesSystem.CurrentSlot`（或文件路径）不匹配即自失效；或关停时清 `_cache`/`_initialized`；或让 OnSetFile 订阅独立于开关存续。**PetNest 是同构形态，需一并排查**（其关停同样退订 OnSetFile：`PetNestRuntimeModule:275-283`）。

#### 验证需求

实机：槽 A 签到→关开关→载槽 B→开开关→开报纸看签到墙、存盘重进 B 确认不被覆写。

### CR-2026-08-29-018：模式H P2/P3 打磨项汇总（复审确认，6+3 项）

**严重级**：P2（6 项）/ P3（3 项）
**兼容分类**：`COMPAT`（⑥ 为 `SAFE` 文档同步）
**状态**：Fixed（第 2 项已于 2026-08-31 随完整战斗/押注闭环修复）
**来源**：2026-08-29 四系统复审（各项均静态确认，零调用类经双重 grep）

#### 问题清单

1. **看盘页显示原文 "{0}"**：`_MatchFlow.cs:265-269` 把值为「第 {0} 场」的 `Label_Match` 当纯前缀拼接 → 页面显示「第 {0} 场 1 / 6」；`RecoveryPanel:166` 用 Replace 是对的，两处不一致。
2. **赔率页没有赔率**：`ModeHOddsController`（BuildQuote/公开分）全仓零调用；`BuildOddsPageContent`（`_MatchFlow.cs:398-419`）不设 Body/Lines；§23.1 三档下注控件未做。批 2 的「接通赔率」实际只是「赔率页可达」。
3. **SavesSystem 订阅不退订（违反 4.6）**：`ModeHSaveFlushCoordinator.ShutdownSubscription`（:52-56）零调用；对照 PetNest/日报/模式G 均在销毁路径退订。
4. **技术重试不换计划**：`EnsureMatchPlan`（`_MatchFlow.cs:324`）只按 matchIndex 判缓存，technicalRetrySequence 织进种子（EncounterPlanner:102）却复用刚失败的同一计划，与 §17.4 相悖（当前被 010 遮蔽）。
5. **`_pendingContractMainId` 在 AbortSetup 后残留**（`_MatchFlow.cs:251`，仅成功签约清 :236）：同会话再开局，残留主将 ID 让新赛季首次点击直接触发签约判定。
6. **a579c3e 未同步 repowiki（违反 4.13）**：知识卡仍写「尚未接线：战斗主体（生成/…）」，而生成段已接线（`Mode H 百战留痕模式运行时/架构设计.md:27`）。
7. (P3) 赛季创建链两次相邻物理落盘（draft_candidates+首写；match_plan+first_match_brief）。
8. (P3) `modeHPlayerSpawnPos`/`modeHExitPos` 被 MapSupportRegistry:152-153 必填校验但运行时零使用。
9. (P3) 换档后模块内存 `_runState` 残留（BeginSeasonSetup 会覆盖，基本无害）。

#### 2026-08-31 追加闭环

看盘页现由正式对局控制器生成确定性公开分、胜率与赔率；锁盘后进入分帧生成、战斗、结算、
伤病/战痕、赛间恢复、转会与名人堂流程，不再用 `combat_wiring_pending` 回滚看盘。

### CR-2026-08-29-019：模式G P2 打磨项汇总（复审确认，4 项）

**严重级**：P2
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审

#### 问题清单

1. **自动入场流确认页打不开时对玩家完全静默**：`WavesArena/BossRushEntryFlow.cs:191-217`、`Integration/BossRushIntegration_TravelAndSetup.cs:396-424` 只退款+DevLog（else 分支日志还误写「确认页已取消」）；路牌 `OnTimeOut` 路径有文案，auto 路径缺同款。
2. **宿敌存档「战斗中不写盘」承诺未实现**：`ModeGNemesisPersistence.cs:13` 类头声称写屏障避战斗，实际波 3/6 宿敌死亡帧 `CheckNemesisDefeat`（`ModeGRuntimeModule.cs:926-951`）→ RequestFlush → 下一帧全量 `SavesSystem.SaveFile`，只避 IsSaving 不避战斗；与日报同轮「落盘避开战斗帧」标准相悖（卡顿幅度需实机测量，每局至多 2 次）。
3. **`ModeGInteractable.OnDestroy` 用 `new` 隐藏基类 virtual**（:495）：官方 `InteractableBase.OnDestroy` 是 `protected virtual`（Interacting 时 StopInteract）；当前唯一实例无碰撞体不触发，属潜伏缺陷。改 `protected override` + `base.OnDestroy()`。
4. **AFK 过期提示「重新打开确认页」场内无可达入口**（`ModeGEntry.cs:401-410`）：场内无模式G交互物，auto 确认页只在进图协程开一次；玩家唯一路径是出图重进，提示与可达操作不符。

### CR-2026-08-29-020：遗种巢 P2：PetNestDropService._hooks 慢泄漏

**严重级**：P2 Minor
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-29 四系统复审

#### 位置

- `PetNest/PetNestDropService.cs:34`（`_hooks` 表）；摘除仅在 boss 死亡路径（`LootAndRewards/LootAndRewardsRandomBossLoot.cs:443→ClearTracking`）与逐只清理
- `LootAndRewards/LootAndRewards.cs:473-481` 的 stale 清扫只清自家四表，不清 PetNest 并联表

#### 问题

未死亡也未被逐只清理的 Boss（弃局、直接撤离、切图销毁）在 `_hooks` 的条目永不移除，长会话跨多局无上限累积（死角色 key + 捕获 owner/character 的委托）。纯内存慢泄漏，无每帧成本。

#### 建议修复

stale 清扫处并联移除，或场景回调 `ClearAllTracking()`（与 016 同批顺手修）。

### CR-2026-08-29-021：日报 P2/P3 打磨项汇总（复审确认，1+5 项）

**严重级**：P2（1 项）/ P3（5 项）
**兼容分类**：`COMPAT`
**状态**：Fixed（第 6 项于 2026-08-31 以可选字段 `PendingIssueBanner` 向后兼容落盘）
**来源**：2026-08-29 四系统复审（①经官方源语义核对）

#### 问题清单

1. (P2) **悬赏现金忽略 `EconomyManager.Add` 失败返回值**：`DailyReportRewards.cs:126` 丢弃返回值；官方 `Add` 在 `Instance==null` 时返回 false 不抛异常 → `SettleBounty` 仍置 `BountyRewardClaimed=true` 落盘，补发被闸死，现金永久丢失且报纸公示「已寄出」。窗口窄（场景闸使 gameplay 帧 Instance 通常就绪）但属确定的错误处理缺失，一行修复。与本系统「先发后标记、宁可重发不吞奖」纪律相悖——里程碑物品路径检查了投递结果，现金路径没有。
2. (P3) **`BuildCandidates` 把瞬时失败缓存成会话级空池**（`DailyReportRewards.cs:164-217`）：`Instance==null`/异常返回的空数组被缓存到进程结束，该品质整会话 `no_candidate`；奖励不丢（下会话补发）但当次拿不到。空结果不应缓存。
3. (P3) **同场景热切开后建造菜单没有报箱**：注入闸只在进基地装配管线跑一次（`DailyReportMailboxBuilder.cs:103`、`IntegrationDeferredBootstrap.cs:300-306`），原地开开关要出图再回。PetNest 同构（`PetNestBuilder.cs:94`）。
4. (P3) **F3 dump「悬赏题目」打的是昨日已结算题**（`F3DebugCheatMenuActions.cs:845` 用 `data.BountyKindId` 而非 `GetActiveBounty()?.Id`），与旁边「悬赏进度」（今日题）不一致，干扰 D6 排查。
5. (P3) **跨天补发里程碑会换奖品**：`DailyReportService.cs:582` 补发传当前 DayIndex，与 `DailyReportRewards.cs` 头注释「同一 (seed, day, slot) 同一件」矛盾；品质一致、无重复发放，纯确定性承诺破口。
6. (P3) **未读提示不持久**：`DailyReportService.cs:83` `_pendingIssueBanner` 仅内存；战斗中跨天→未回基地退游戏，下会话不再提示（数据无损）。

### CR-2026-08-31-001：后山 P1：出击餐在正常流程中永远不会生效

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核（静态证明 + 官方反编译源时序核对）

#### 位置

- `Integration/BackMountain/RaidMealService.cs:125` `ApplyForRun()` 要求 `CharacterMainControl.Main != null`
- `Integration/BackMountain/BackMountainRuntimeModule.cs`（修复前）唯一调用点在 `RefreshFacilitiesForScene`，由 `SceneManager.sceneLoaded` 驱动

#### 问题

官方主角由 `LevelManager.CreateMainCharacterAsync` **异步**创建（反编译源 `LevelManager.cs:520`），`sceneLoaded` 回调那一刻 `CharacterMainControl.Main` 必然为 null。`ApplyForRun` 早返后**没有任何重试路径**（模块 `OnUpdate` 不重试，全仓仅此一个调用点）。`RaidMealService.cs:13` 的头注释自己写明生效点应为 `OnLevelInitialized`，但实现没接到那里。

#### 影响

三种出击餐（龙息果 / 焚心椒 / 幽影蘑菇）全部失效：玩家在基地吃下 → 提示「下一局出击时生效」→ 进局零加成零提示，且登记不被消费，之后每局同样失败。「种地 → 做饭 → 带增益出击」这条养成闭环的最后一环是断的。

#### 修复

模块拆成两个时机：设施注入留 `OnSceneLoaded`，角色加成改挂 `LevelManager.OnAfterLevelInitialized`（官方 `BuildingEffect` 与本 mod `SetBonusManager` / `DragonSetBonus` 用的同一时机）；订阅幂等 + 成对退订；模块若在关卡初始化之后才 bootstrap，用 `LevelManager.AfterInit` 补一次。

#### 验证需求

编译 + guard 已绿；**需实机 smoke**：基地吃餐 → 进局确认飘字与属性变化。

### CR-2026-08-31-002：后山 P1：展示柜加成在战局内实际不存在

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核

#### 位置

- `Integration/BackMountain/ShowcaseService.cs:181` `ReapplyBonuses()`；修复前场景侧调用点与 001 同一时机

#### 问题

与 001 同源：主角尚不存在时 `ReapplyBonuses` 只摘不挂，而注释所称「等下次场景就绪再来」并无对应机制——下一次仍是同一个过早时机。加成实际只在「UI 里登记新战利品」与「交付章节触发解锁事件」两个瞬间挂上，此后任何一次切场景即消失且不再重挂。

#### 影响

建筑描述承诺的「登记得越多，你越经打」在战局里不成立：进竞技场打 Boss 时 MaxHealth 加成为零。

#### 修复

与 001 共用 `OnAfterLevelInitialized` 挂载点；解锁事件路径额外调一次 `RefreshCharacterBoundEffects()`，让交付当场生效。

#### 验证需求

编译 + guard 已绿；**需实机 smoke**：登记后进局确认最大生命提升。

### CR-2026-08-31-003：征程 P1：终章决战打输一次即永久卡死

**严重级**：P1 Major
**兼容分类**：`COMPAT`
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核

#### 位置

- `Campaign/CampaignFinalBoss.cs`：修复前 `CleanupCampaignFinalBoss` 只有两个调用点（死亡回调、让路 tick）

#### 问题

玩家召唤「冠军之影」后死亡或中途离场时，Boss 随场景销毁、`OnDeadEvent` 永不触发，`campaignFinalBossActive` 卡在 true。后果三连：召唤石不再生成、`CanStartCampaignFinalBoss` 恒 false、契约 HUD 因模式桥短路且 `campaignLastObservedMode` 为 null 而永不 `ResetSession`，横幅在基地常驻。隐藏解法（随便开一局其它模式触发让路清理）玩家不可能自行发现。

#### 影响

战役高潮不可重试——而 1.6× 数值的强化 Boss 恰恰是最可能打输的一场。

#### 修复

三条收尾路径补齐：① `CampaignRuntimeModule.OnSceneLoaded` 幂等收尾（覆盖死亡回基地这条主路径）；② 让路 tick 增加「生成已出结果但实例已销毁」检测；③ 收尾自增 `campaignFinalBossRunId` 作废在飞的异步生成，协程回来发现编号不符即销毁产物，避免留下无人记账的强化女巫。收尾同时清掉终章局内追踪（仅在武装章节为终章时），修掉 HUD 在基地常驻。

#### 验证需求

编译 + guard 已绿；**需实机 smoke**：召唤决战 → 故意战死 → 回基地再进竞技场，确认召唤石重新出现且 HUD 不残留。

### CR-2026-08-31-004：后山 P1：展示柜收藏跨存档槽泄漏，可写脏另一个档

**严重级**：P1 Major
**兼容分类**：`COMPAT`（修复本身不改 schema；未修时会产生错误数据）
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核

#### 位置

- `Integration/BackMountain/ShowcaseService.cs`：`_displayed` / `_loaded` 无槽位烙印、不订阅 `OnSetFile`；`NotifySlotChanged()`（:311）**全仓零调用**

#### 问题

同一次会话内从 A 档切到 B 档：B 档能看到并享受 A 档的收藏加成；在 B 档做一次登记，`Store()` 会把「A 档收藏 + 新条目」整体写进 B 档的 `BossRush_BackMountain_Showcase_v1`——永久污染。对照组 `CampaignPersistence` 做了完整防御（OnSetFile 订阅 + 槽位比对 + 下游广播），注释还点名「PetNest 曾踩过同类坑」。

#### 修复

两道防线：① 运行时模块订阅 `SavesSystem.OnSetFile` / `OnSaveDeleted`，调 `ShowcaseService.NotifySlotChanged()` 与 `GardenSeedInjector.NotifySlotChanged()`；② `ShowcaseService` 缓存加槽位烙印，`EnsureLoaded` 每次比对 `SavesSystem.CurrentSlot`，对不上就摘掉旧加成并从新槽重读。

#### 验证需求

编译 + guard 已绿；**需实机 smoke**：A 档登记 → 不退游戏切 B 档 → 确认 B 档展示柜为空且加成为 0。

### CR-2026-08-31-005：征程 P2：召唤石维护 tick 每帧分配字符串

**严重级**：P2 Minor
**兼容分类**：`SAFE`
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核

#### 位置

- `Campaign/CampaignFinalBoss.cs` `ShouldCampaignFinalBossAltarExist`（修复前把 `IsCurrentSceneValidBossRushArena()` 排在章节检查之前）
- `ModBehaviour.cs:82`：`GetCurrentMapConfig` 经 `SceneManager.GetActiveScene().name` 每次分配托管字符串

#### 问题

战役恒开，该 tick 对每个玩家的每一帧生效，60fps 下约 2–4 KB/s 稳定 gen0 垃圾。量级不致掉帧，但违反仓库自身的热路径零分配纪律（AGENTS.md 4.12）。

#### 修复

判定改序：零分配的终章契约查询先短路；场景判定另按 `CampaignRuntimeModule.SceneGeneration` 缓存（`IsCampaignArenaSceneCached`），彻底消除每帧分配。

### CR-2026-08-31-006：征程 P2：契约 HUD 每帧构建字符串，与头注释承诺不符

**严重级**：P2 Minor
**兼容分类**：`SAFE`
**状态**：Fixed
**来源**：2026-08-31 征程/后山全面审核

#### 位置

- `Campaign/CampaignHud.cs`：头注释称「其余帧只有一次字符串构建前的短路比较」，实现却是先构建后比较（title 两次拼接 + `BuildBody()` 的 `ToString()`，合计 ≥3 次分配），只有 TMP 赋值被短路

#### 修复

改为构建**之前**做零分配脏检查：用复用的 `List<int>` / `List<bool>` 记录上次显示时各目标的 `Current` 与 `Failed`，逐项比对整数与 bool；内容真变了才拼字符串。隐藏时快照作废，保证再次显示必重建一次。

### CR-2026-08-31-007：征程/后山 P3 设计取舍汇总（7 项）

**严重级**：P3 Note
**兼容分类**：`COMPAT`
**状态**：Fixed（用户于 2026-08-31 明确要求全部修复）
**来源**：2026-08-31 征程/后山全面审核

#### 问题清单

1. **第一章对新玩家偏硬，且是整条内容线的总闸门**：ch1 要求「通关标准局 + 前 3 波无伤」同时达成，而 ch1 交付才解锁菜地。可考虑无伤挪去后面章节或降为 2 波。
2. **ch3 文案与实现口径不一致（宽松方向）**：「清掉 8 个敌对阵营的头目」实现上计数所有 `isBossCharacter` 击杀，无阵营过滤（`CampaignObjectiveCollector.cs:50`）。玩家不吃亏。
3. **展示柜「战利品」的真实定义是「手持 + Q≥5」**：登记走 `CurrentHoldItemAgent`（`ShowcaseUI.cs:249`），头盔/护甲等非手持高品质掉落无法登记；而出击餐（Q5、菜地可量产）反而可占 3 格并计入满柜加成。
4. **`CampaignNoteBridge.cs:18` 注释声称的兜底展示面不存在**：公告板面板实际没有线索页签，线索唯一展示面是官方笔记图鉴。需删注释或补页签。
5. **终章击杀后、交付前的毛边**：召唤石立即重新出现，可反复重打冠军之影（无重复奖励、无害，略破仪式感）。是否加 `ContractActive` 门禁由 owner 定。（同项的 HUD 常驻已随 003 修复。）
6. **`RegisterMeal` 失败时饭仍被框架吃掉**：`RaidMealUsageBehavior.cs:60` 登记失败直接 return，但 `CA_UseItem.OnFinish` 照常扣物品且无提示。窗口极小（需恰逢 `IsSaving`）。
7. **「目标已达成」不落盘，退游戏即整章重打**：ReadyToDeliver 是会话态（`CampaignProgressService.cs:36` 设计如此）。对 ch3（8 头目 + 撑满 10 分钟）这类长目标重打成本不低；是否为它落一位存档需 owner 取舍。

#### 修复决策

1. 第一章无伤门槛降为前 2 波，同时保留通关时的短局兜底判定。
2. 第三章中英文文案统一为「击败 8 名头目」，与实际 Boss 判定一致。
3. 展示柜增加穿戴物登记入口，并排除后山自产种子/餐品，避免量产物刷收藏。
4. 注释改为只承诺官方笔记图鉴；不再声称公告板存在未实现的线索页签。
5. 召唤石只在终章 `ContractActive` 时出现，击杀后等待交付期间不可重复召唤。
6. 出击餐在存盘中、陌生 ID 或登记失败时禁止使用，不让框架先扣物品。
7. `ReadyToDeliver` 作为可选字段向后兼容落盘，重启后恢复待交付态。

### CR-2026-08-31-008：全内容可用性复核补充（5 个 P1 + 2 个 P2）

**严重级**：P1（5 项）/ P2（2 项）
**兼容分类**：`COMPAT` + `SCHEMA+`
**状态**：Fixed
**来源**：2026-08-31 全内容静态审核、调用链与失败路径复核

#### 问题与修复

1. (P1) **模式H 地图列表与冻结目标漂移**：通用地图页可选不受支持地图，票已预扣但运行时拒绝接管。现只展示 `ModeHMapSupportRegistry.IsSupportedPair` 支持项，点击时按原配置索引重新冻结 sceneName/sceneID。
2. (P1) **模式H 清场误删友方/功能 NPC**：原生隔离会销毁除主玩家外的全部角色。现保留玩家队、主玩家、遗种巢随从和 `INPCController`，只清除明确敌对原生单位。
3. (P1) **征程交付忽略存档失败**：先改活缓存/发现金再写盘，失败时可重复交付。现克隆存档做事务写入，存档成功后才发布运行时 token，现金发放失败或落盘失败均回滚并保持待交付。
4. (P1) **词缀锻造静默写失败会吞现金/熔石或留下半成品**：现所有 KV 写入均读回核验；重铸、锁词缀按「核验旧值→扣款→扣材料→写入」执行，失败恢复槽位并核验退款/回滚结果，补偿失败会向玩家报错。
5. (P1) **后山登记/餐品存档写失败仍报告成功**：展示柜与出击餐写入均读回核验，登记/移除失败恢复内存与角色加成；餐品只有持久清除成功后才施加局内效果。
6. (P2) **鸭皇图鉴可被低价卖回且商店刷新会丢书**：图鉴设为不可出售、价格系数恢复正常；商店尚未可注入时保留缓存库存，存档事件订阅/退订成对。
7. (P2) **随机商人初始化时序会让库存为空或每次启动刷新**：反射绑定前置，配置完成前保持 inactive；首次激活只补一次库存，刷新时间戳稳定，弹药/医疗堆叠 99、高品质物品单件。

#### 验证需求

Windows 编译、相关结构守卫与全量 guard；实机需覆盖模式H 受支持地图、友方 NPC 共图、
模拟存档失败后的征程/锻造/后山重试，以及图鉴和随机商人的商店刷新。

### CR-2026-08-31-009：首次 F3 完整验收暴露的运行时回归（4 个 P1 + 3 个 P2）

**严重级**：P1（4 项）/ P2（3 项）
**兼容分类**：`COMPAT` + `OPERATIONAL`
**状态**：Open（修复与静态验证已完成；等待下一份完整 F3 报告确认后转 Fixed）
**来源**：Player.log + `BossRushValidation_20260831_125247_246.log`

#### 已确认问题

1. (P1) `CampaignContentCatalog` 使用 `JsonUtility` 解析两层对象数组时，实机只读出 version、
   `chapters` 静默为 null，导致已部署且哈希正确的正式表回退到硬编码。
2. (P1) Boss 乱入从图鉴目录抽到稳定 key 后直接进入 SpawnCore，但标准模式没有初始化
   `cachedCharacterPresets`；五次候选都报未找到预设。F3 只看 `TryForceTrigger=true`，仍把空转计 PASS。
3. (P1) F3 清场把除主玩家以外的所有存活角色都算成敌人；日志在开波前已有一个友方角色，
   清掉唯一 Boss 后仍报 `enemies=1` 并中止后续全套模式。
4. (P1) 动态商人 Animator 首个 `MagicBlendState.OnStateEnter` 早于 `MagicBlending.Start`，
   对空 Playable 调 `SetJobData` 抛异常；同时自定义 merchantID 被官方数据库报“未配置商人”。
5. (P2) Harmony 逐类隔离扫描对程序集内每个普通类型都创建 processor，普通业务方法名
   `Cleanup` 被 Harmony 当成 cleanup 回调，产生 3 条虚假补丁失败；真实补丁实际为 53/53。
6. (P2) 运行时 AddComponent 的日报报箱和征程公告板没有在 `base.Awake` 前初始化官方私有
   `otherInterablesInGroup`，每次进基地都产生可稳定复现的 NRE 警告；同类新增交互组件有相同风险。
7. (P2) F3 在 0.35 秒内轮流触发八种事件并立刻收尾，事件横幅在官方队列中延后播放，
   验收结束后仍持续弹出；这既污染清场结论，也遮蔽异步生成失败。

#### 已实现修复

- 征程表复用 Mode H 的严格 token parser，并保留整表签名/顺序/目标校验。
- 乱入桥先幂等准备官方 preset 缓存；八个事件新增实际副作用验收协议，F3 逐项等待至
  `Passed/Failed` 或 30 秒超时并写独立 case，不再把调度成功等同功能成功。
- 清场只统计 `Team.IsEnemy(Teams.player, team)` 的存活角色，明确排除遗种随从，并把残留实例、
  运行时 team 与 preset key 写进报告。
- 新增 MagicBlend 初始化顺序兼容补丁；商店以官方 ID 引导 Awake，同帧 Start 前切回稳定 Mod ID
  并覆盖事件库存。
- Harmony scanner 只处理类级或方法级含 `[HarmonyPatch]` 元数据的类型。
- 新增交互体均在 `base.Awake` 前建立私有分组空表；完整验收期间抑制普通消息/大横幅入队，finally 复位。

#### 验证需求

下一份 Dev F3 完整报告必须满足：Campaign `source=Json`；八个 `RANDOM_EVENT_*` 分项均 PASS；
Boss 乱入 `spawned>0`；商人 `merchant/shop=true, entries>0`；标准清场 `enemies=0`；完成后继续覆盖
Mode D/E/F/G/H、Zombie、终章/BGM、回基地回读与最终性能。Player.log 同时不得再出现本条所列
BossRush Harmony、Campaign、MagicBlend、merchantID 或自建 Interactable `base.Awake` 错误。

### CR-2026-09-01-010：第二次 F3 验收暴露的共享队列与模式清理回归

**严重级**：P1（2 项）/ P2（1 项）/ P3（1 项）
**兼容分类**：`COMPAT` + `OPERATIONAL`
**状态**：Open（修复与静态验证已完成；等待下一份完整 F3 报告确认后转 Fixed）
**来源**：Player.log + `BossRushValidation_20260831_152526_013.log`

#### 已确认问题

1. (P1) 标准模式的 Boss 乱入复用 Mode E/F 分帧后处理队列，但 scheduler 位于
   `TickWavesArenaRuntime` early-return 之后；标准模式中任务永远不推进，最终超时并在下一模式被清空。
2. (P1) Mode E/F 克隆预设在角色销毁前被提前 Destroy。Unity 伪 null 使后续
   `dropBoxOnDead`、Health 与 OnDestroy 链 NRE，Mode E 结束后留下 `no_preset` 角色并阻断整套验收。
3. (P2) Mode D 生成只强制 AI 仇恨，没有把中立官方预设的 runtime team 改成玩家敌对；
   角色虽进入本波登记表，却不满足战斗与 F3 敌对判定。旧 `EndModeD` 还只清列表，不销毁实体。
4. (P3) Mode E 动态分类商店在配置 merchantID 前以默认 `Albert` 执行 Awake，每次生成商人产生
   13 次无效官方数据库查询与警告；库存随后被覆盖，未造成当前用例失败，但污染日志并做无用工作。

#### 已实现修复

- 共享后处理 scheduler 在 WavesArena early-return 前无条件推进；空队列为 O(1) 快速返回。
- Mode D 在登记前设置并回读 wolf 敌对状态，失败即销毁；结束路径确定性注销恢复、禁掉落并销毁实体，
  非活动状态下重复调用也会清残留。
- Mode E/F 克隆预设改由对象级 lease 持有，角色 OnDestroy 后延迟释放；退出路径不再 Hurt，按顺序
  注销运行时并销毁角色。
- Mode E StockShop 使用 inactive → 官方 bootstrap ID → Awake → 稳定 Mode ID → 分类库存流程。
- F3 新增模式自有角色诊断与 2 秒逐帧清场等待；Mode D 报告登记对象的存活、阵营和敌对状态。

#### 验证需求

下一份完整 Dev F3 报告必须看到 `RANDOM_EVENT_BOSSINTRUSION`、`MODE_D_LIFECYCLE`、
`CLEANUP_AFTER_MODE_D`、`MODE_E_LIFECYCLE` 与 `CLEANUP_AFTER_MODE_E` 全部 PASS，并继续执行
Mode F/G/H、Zombie、终章/BGM、最终清场与存档回读。Player.log 不得再出现 Mode E 清理 NRE、
`scheduler_cleared` 乱入失败或 Mode E 分类商店 `Albert` 未配置噪声。

### CR-2026-09-04-021：真实押品 journal 与仓库快照没有共同提交

**严重级**：P0；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：完整静态调用链及官方存档源码。

`ModeH/ModeHWarehouseStakeJournal.cs:510–524` 移除真实物品后只保存 journal，`SaveFile(false)` 不会采集 `PlayerStorage`。返还/奖励路径同样可使 receipt 已落盘、仓库仍保存旧图像，异常退出后复制押品或漏奖励。修复需把当前槽物品快照纳入 durable 屏障。当前新增押品先受 024 阻断；历史未结事务以及修复节流后的路径仍受影响。需隔离存档中断恢复验证，未实机复现。

**修复**：押品写屏障采集并回读官方仓库快照，同时保存溢出缓冲区与 journal。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-022：持久化 escrow 快照没有跨会话实物重建入口

**严重级**：P0；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：完整静态调用链。

`ModeH/ModeHWarehouseStakeJournal.cs:1162–1167` 装载 journal 后清空 `_escrowItems`；`normalizedTreePayload` 全仓只有写入与声明，没有读取重建者。已有押品移除被官方保存后若进程中断，恢复返还在 `849–859` 恒因物品短缺进入人工介入。需核验持久证据后实现一次性重建与 receipt 防重。CR-2026-09-04-010 的满仓缓冲修复没有覆盖此支路。未实机复现。

**修复**：新增完整物品树恢复消费者，按槽、阶段、post-image 与逐项凭据恢复到官方缓冲区；旧载荷缺失变量类型时保留人工介入。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-023：Mode H 延迟物理落盘后下一帧丢弃欠账

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：生产源码隔离复现。

`ModeH/ModeHSaveFlushCoordinator.cs:178–185` 只检查 typed pending。前一轮 `FlushPending` 已清 pending，但因战斗帧/节流尚未 SaveFile，下帧就清 deferred 返回成功。宿主强制保存也会早退。实际源码 harness 证明物理写次数始终停在 1，下一帧 deferred 已变 false。需独立保留 `_saveFileRequired` 至真实写盘成功。复现日志：`Build/review-adf1f3e/modeh-flush-repro.log`。

**修复**：独立物理写欠账，不依赖 typed pending；战斗等待不耗重试预算，失败重试重新采集资产/回滚 journal。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-024：真实押品同步四阶段必然撞每帧保存节流

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：静态完整锁盘链与节流源码复现。

`ModeH/ModeHRealStakeService.cs:224–226` 在同一按钮回调中连续请求四个 durable 阶段。Prepare 首次落盘后下一阶段必被每帧闸拒绝，回滚到 Prepared 又留下 slot inconsistent，之后无法正常锁盘。需可续做的分帧事务或明确的资产屏障；不能把延期当永久失败，也不能提前声明 durable。仅补 023 的欠账位无效。完整 UI 锁盘待实机验证。

**修复**：真实押品四阶段使用强制同步屏障，失败回滚实物并重暂存一致账本；虚拟筹码预留同步撤回。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-025：Mode H 赛前阵容、kit 与口令缺少玩家编辑入口

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：生产写入点与当前设计/Wiki 对照。

`ModeH/ModeHRuntimeModule_CombatFlow.cs:34–48` 固定前两名为首发/接力、默认选 kit；`90–92` 固定口令。赔率页 `ModeHRuntimeModule_MatchFlow.cs:980–1002` 仅提供下注、押品、锁盘。玩家不能按现有承诺选择阵容、装备与通用令，亦不能主动让受伤主将休息。需把三个选择器接入真实编辑状态、赔率与摘要，并让锁盘消费玩家选择。未实机验证。

**修复**：赔率页接入阵容/休息/套装/口令滚动编辑；赔率、摘要与锁盘共同消费选择，并校验回调 owner。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-026：孵化新崽已保存但蛋消耗未采集

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：物品/持久化完整静态链。

`PetNest/PetNestHatchService.cs:122–128` 先保存新崽再消耗蛋，没有保存变更后的背包/仓库快照。下一次官方保存前异常退出，会恢复旧蛋而保留新崽。应共同提交实物容器与新崽，或建立可恢复孵化记录；仅交换语句顺序无效。需分别用背包蛋、仓库蛋执行隔离存档中断测试。未实机复现。

**修复**：先可逆摘蛋，再提交不提前 flush 的新崽候选，采集角色/仓库/缓冲区后同批落盘；拒绝候选则还原蛋。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-027：远征发奖游标先于实物快照落盘

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：完整静态链及官方物品保存代码。

`PetNest/PetNestExpeditionService.cs:497–508` 在 SendToPlayer 后持久化 rewardsGranted/游标，却未采集物品容器。产蛋奖励后、官方下次保存前中断，重进奖励缺失而账上已完成。应共同采集落盘实物与游标，未确认实物持久化时保留债务。需入背包/入仓库两条中断恢复测试。未实机复现。

**修复**：实际发奖先检查资产边界，游标提交及重试联合采集角色、仓库、缓冲区、现金。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-028：新材料和餐食继承便携安全区使用行为

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：两路静态复核。

`Integration/BackMountain/BackMountainItems.cs:225`、`AffixForge/AffixForgeStoneConfig.cs:105`、`Items/RelicEggConfig.cs:96` 未清克隆来源的 UsageUtilities.behaviors。克隆链为便携安全区→遗种蛋→种子/餐食/熔石；安全区使用行为无 TypeID 校验，丧尸局可错误部署并消耗这些物品。需清理来源行为，仅绑定目标物品应有行为。需实例行为表及游戏内右键验证，未实机复现。

**修复**：统一清除克隆使用行为、事件与耐久配置；材料解绑使用入口，餐品只绑定自身行为。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-029：日报跨日改写待发悬赏日期

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：完整静态补发链。

`Integration/DailyReport/DailyReportService.cs:433` 保留旧欠账种类/目标却写入新日期；补发 `467–478` 按新日期重抽，导致类型不匹配拒发或按新档位错发金额。需保留原债务日期，最小修复为移除该分支日期覆盖。验证首次付款失败、跨日后恢复仍按原金额且只发一次。未实机复现。

**修复**：欠款分支先于今日悬赏查询，完整冻结原债务日期与字段。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-030：普通 NPC 模块与永久模块重复生成小满

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：两路静态完整链复核。

`Integration/NPCs/DuckNpc/DuckNpcModule.cs:168–181` 未排除 isPermanent，永久模块又独立处理同一蓝图。当前小满已允许在基地生成，两套实例登记互不相通，下层无去重；结果为两只小满，普通分身无交互且已婚后仍生成。需普通模块场景判定和循环同时排除永久蓝图。未婚/已婚基地生成与交互待实机验证。

**修复**：普通模块的场景判定和生成循环均排除永久蓝图。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-031：无间炼狱龙皇额外掉落订阅晚于同步消费

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：两路静态事件顺序复核。

`Integration/DragonKing/DragonKingBoss.cs:381–396` 先订阅主掉落、后订阅遗种蛋/熔石；无间炼狱在 `LootAndRewardsRandomBossLoot.cs:329–330` 同步消费空 pending 后返回，两项服务才 roll 并入 pending，无后续消费，官方箱又已关闭。需将两项 TryTrack 提前至主 handler 注册前。旧 003 的“顺序安全”只考虑了标准箱的协程等待，漏了此分支。需控制 roll 命中后实机核对两种模式，未运行验证。

**修复**：两项额外掉落先订阅，主消费后订阅；修正旧 guard 对同步分支的错误要求。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-032：全量 CI 依赖未纳管制品与本机文档

**严重级**：P1；**兼容分类**：`OPERATIONAL`；**状态**：Fixed（实机复测待完成）；**来源**：固定提交独立干净检出实际执行。

`.github/workflows/guards.yml:24–35` 仅 checkout/Python 就跑全量 guard；独立检出实际 526 PASS、4 FAIL。缺少 Mode G/H 展示 bundle、Mode H 外部 Unity builder、便携安全区 bundle，以及 repowiki 所引用未纳管文档/info.ini。本机 530 PASS 被这些本地文件掩盖。需明确源码检查/制品验收输入边界，补齐输入或设置独立制品门禁，不能无说明跳过或放宽断言。流水线方案实施前需 owner 确认；本轮未改配置。日志：`Build/review-adf1f3e/guards-clean-checkout.log`。

**修复**：依用户本轮全部修复要求，限定修改源码 CI 检查范围；明确 PARTIAL 外部制品，默认完整验收不变。修正未纳管资料引用。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-033：展示柜未知版本或读档失败后仍可覆盖原 key

**严重级**：P2；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：静态保存调用链。

`Integration/BackMountain/ShowcaseService.cs:330–348` 先建空收藏再在失败时返回，没有只读写屏障。后续登记经 `365–376` 用空集合加新条目覆盖旧 key。需按槽保护读错/未知版本，成功重读或换槽才解除。需隔离存档注入未知 schema/读取故障后断言原 key 不变。未实机复现。

**修复**：按槽写屏障，key 不存在/合法读取才可写；未知版本与读取失败不覆盖。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-034：出击餐登记失败 return 仍触发官方扣量

**严重级**：P2；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：官方使用完成链静态确认。

`Integration/BackMountain/RaidMealUsageBehavior.cs:63–68` 在 Save/Load 失败或读回不匹配时仅 return，官方 `CA_UseItem.OnFinish` 仍在 Use 后 StackCount--，餐品丢失且未登记。需登记成功才消耗的单一责任或可靠补偿。此为故障注入条件，不能推断为普通保存并发竞争或正常必现。需真实使用阶段注入失败验证，未实机复现。

**修复**：登记失败预补数量抵消官方随后扣量，满堆不钳制；写/回读失败恢复旧餐登记。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-035：血月收尾清表前未结清最后轮询窗口的击杀

**严重级**：P2；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：静态调度与生命周期证据。

`RandomEvents/RandomEventCatalog.cs:573–580` 直接清 _tracked；收益只在每 2 秒 Tick 轮询结算。最后扫描之后、75 秒到期之前杀死带增益敌人，或杀敌后立即局末，1200 收益会漏发。需结束清理前 drain 已发生死亡，或活动期间先记录死亡事实再兑现。需最后 2 秒/立即局末测试且验证不重复，未实机复现。

**修复**：具名幂等死亡订阅记录事实，Tick 与结束前结算共用去重计数，再退订清表。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

## UNVERIFIED / Seeded Leads

> 这里的内容不是 bug。升格前必须读代码或运行验证。

- **遗种巢 跨档写污染组合链**（016 的延伸，逐环节已静态核过、多步组合需实机）：关开关后已挂 handler 触发 `AddSouls` 会从当前槽重建缓存 → 主菜单切档（无人清缓存）→ 重开开关（EnsureBootstrapped 不重置缓存）→ 下次 Commit 把 A 档巢数据写进 B 档。与 017 的日报跨档渗漏同构。
- **遗种巢 远征奖励非崩溃失败被吞**：`PetNestExpeditionService.cs:420-447` 注释宣称可补发，但 `GrantRewards`（:560-631）内部吞异常正常返回、:432 无条件置 `rewardsGranted=true`——蛋实例化失败/EconomyManager 异常 = 一次性丢失。修复需按条目粒度记账（现金已发、物品失败的部分重发问题）。
- **遗种巢 OnExpedition 孤儿锁无自愈**：Nest key flush 成功、Expedition key flush 异常进 StoreFaulted 后（`PetNestSaveCoordinator.cs:141-144` 逐 key 独立失败），官方存盘可落「崽=OnExpedition」而远征记录缺失；无 reconcile 路径，该崽永久锁死。建议 Normalize 处加「OnExpedition 且远征表无匹配 → 复位 InNest」自愈。
- **遗种巢 P3 一组**：基地闲逛崽实为跟随玩家（含 >40m 传送，观感是仪仗队）；场景切换只停两个演出层、主面板 modal lease 极端时序可带进战斗图；天赋/战痕/蛋 KV 展示裸英文 key。
- **模式H 关停竞态孤儿**：StopCoroutine 打断 `CreateCharacterAsync` await 期间，`CreateIsolatedAsync`（SpawnBridge:58-176）async 延续仍会完成并登记抑制表+生成隔离角色，RollbackAll 只回收已入列 handle（窗口数帧）。
- **模式G 弃局确认页开在 Spawning 相位且时停拖住工厂 >15s 是否误报 TechnicalIntegrityLoss**：取决于官方 `CreateCharacterAsync` 是否受 timeScale 影响，静态无法判定。
- **模式G SceneChanged 终局的 `ShowMessage` 在 OnSceneLoaded 时机是否可见**：需实机确认。
- **日报 中途弃 raid 可能计成「成功撤离」**：`DailyReportStatsCollector.cs:190-204` 按 `!info.dead` 计撤离；官方对「战局中退出→回基地」是否判死亡未定，若不判则撤离数可刷（并入 D6 冒烟项）。

## 新条目模板

```markdown
### CR-YYYY-MM-DD-NNN：问题标题

**严重级**：P0/P1/P2/P3  
**兼容分类**：SAFE / COMPAT / SCHEMA+ / SCHEMA- / WIRE+ / WIRE- / BREAKING / OPERATIONAL  
**状态**：Open / Fixed / Deferred / WontFix  
**来源**：代码审查 / 用户复现 / Player.log / guard / 人工 smoke

#### 位置

- `文件路径:行号`

#### 问题

是什么错，为什么是错。

#### 影响

玩家可见后果、静默失效、性能、存档或维护风险。

#### 建议修复

最小修复步骤。

#### 验证需求

编译、guard、人工 smoke 或无法验证原因。
```
