# CODE_REVIEW_FINDINGS.md — 已确认问题库

## 2026-09-25 F3 实机报告（runId 20260925_044506_391）复核：4 项生产缺陷 + 4 项验收数据 / 判据问题（均 Fixed / L1+L2，L3 待下一轮 F3）

证据来源：owner 在 `51d2f0e6` Dev 构建上跑的 F3（主套件 327 过 / 9 红，全在天空岛；岛内全自动 79 / 6 / 1）。基地与各模式 1–7 阶段 182 项全过；`Player.log` 的异常全部来自 DuckMarket、MoveBlackMarket 与官方 `GamingConsole.Load`。

| ID | 级别 / 兼容分类 | 已确认问题与修复 | 验证 / 状态 |
| --- | --- | --- | --- |
| CR-2026-09-25-006 | **P2** / COMPAT | 岛上判夜 19–5 与官方运行时不同相：`SKY_NIGHT_BOUNDARY_OFFICIAL` 实机读出 `TimeOfDayController.nightStart=22 / morningStart=6`，09-16（CR-2026-09-16-004）照反编译源的字段初值 19 / 5 对齐，被 `LevelManagerPrefab` 序列化值覆盖。19–22 点岛上已起夜风、刷云蚋、放夜限定头目而官方仍是黄昏。`SkyIslandNight` 改 22–6、`ForcedHour` 2；光照晨光段 6–7、暮色→星夜 18–22；取数补记 `official_dawn` 只记不判。夜长 10 → 8 现实分钟（owner 授权「全部修复」按对齐官方拍板）。 | **Fixed / L1+L2**。SkyIslandLighting / SkyIslandStory 夹具按 22–6 改写边界与天亮折算；`SkyIslandMosquitoGuard` 钉 22 / 6；Wiki 中英 6 页、契约、repowiki 同步。 |
| CR-2026-09-25-007 | **P2** / COMPAT | 天空岛五扇门的导航封锁会整体丢失（`Player.log` `gate navigation block was lost and re-applied … walkable_before=3705`），1 秒自检只重封且之后不再记日志，`SKY_GATE_REACHABILITY` 两次落在丢失窗口里：门关着 `Search_H_02` 走得到。玩家本人被碰撞体挡住，受影响的是敌人 / 居民寻路。根因（L1 推断）：Mod 自建 `NavMeshGraph` 没关 `enableNavmeshCutting`，克隆官方 prefab 上的 `NavmeshCut` 让 tile 整块重建、`Walkable=false` 随旧节点丢掉。`ArenaPrototypeNavigation.BeginScan` 关掉切割；重封改为每次计数、日志 10 秒限频。 | **Fixed / L1+L2**。游戏 A* DLL 含该字段（编译通过即证）；`SkyIslandGateNavigationPropertyTest` 钉住这一行。是否根治以下一轮 F3 `gate_locked_blocked=1/1` 与日志无 `lost and re-applied` 为准。 |
| CR-2026-09-25-008 | P3 / COMPAT | 苇白在「两盏灯都亮、航标单没接」时先说「这单还没交呢」再说「这单你还没接」，前后矛盾（英文复拍读出）。改为这一格只说「还没接，先接再交」。 | **Fixed / L2**。SkyIslandMarriageTextRegression 加「未接单不催交」断言；离线逐屏复算英文 10 屏 → 9 屏且无矛盾。 |
| CR-2026-09-25-009 | P3 / COMPAT | 浮舟「十盏灯都亮」英文在 `54c8d98c`（09-17）被拆成三屏、中文两屏，违反 `DescribeNpc` 注释「改写保持屏数」，`SKY_AUTO_ALT_FUZHOU` 的星工装备断言落到别的句子上。英文合回两屏。 | **Fixed / L2**。`tools/sky_island_line_screens.py` 复算 2 屏；离线逐屏确认第 6 屏为星工装备。 |
| CR-2026-09-25-010 | P3 / TEST | `SKY_AUTO_REAL_BOSS_SICKLE` 的瞬移偏移 `EnemySpawn_C:-9:5` 落进梯田小屋碰撞盒，整圈 1.2 m 都被占，自 09-16 起每轮 `target_ground_missing`。改 `9:5`；`SkyIslandAutotestTableGuard` 新增按几何表碰撞盒离线复算落点（含反向检查）。 | **Fixed / L2**。守卫 32 条反向检查全红，改回旧偏移即转红。 |
| CR-2026-09-25-011 | P3 / TEST | `SKY_AUTO_REAL_NIGHT_GNATS` 可见度探针只挑最近的一只、刷新方位随机，扇形外的精灵被官方夜里战争迷雾整块盖住，读数 0.129 / 0.033 随方位跳。owner 目检「看得到，不用调」。表现不动、门槛不放宽，只把刷新改成主角瞄准方向 ±15°（`spawn_gnats:6:0:ahead`，`DevSpawnAhead`）。 | **Fixed / L1**。演练符号表两处守卫同步；实机读数待下一轮 F3。 |
| CR-2026-09-25-012 | P3 / TEST | `SKY_AUTO_END_JOURNAL` 断言没跟 `54c8d98c` 的手记改写；顺手把「消耗一块「引风」」改成「烧一块来「引风」」（引风是装置动作，不是物品），两处断言同步。 | **Fixed / L2**。守卫文本核对通过。 |
| CR-2026-09-25-013 | P3 / TEST | 全自动测试档从不接、交航路任务，结局后苇白先念 6 屏催单，`SKY_AUTO_ALT_RESIDENT` 的情报句被挤到第 7 屏。剧情阶段补齐 590001 序章与三条岛上任务的接 / 交（阶段目标文案逐一复算不变），情报句落在第 4 屏，断言挪到标题写的第 3–4 句的第 4 句。`F3AutotestJudges` 红样本改为按位核 Ending 缺失。 | **Fixed / L2**。离线逐屏复算；F3AutotestJudges 330 条、SkyIslandStory 全绿。 |

<!-- BEGIN FULL AUDIT FINDINGS 2026-09-25 -->

## 2026-09-25 全仓审查与修复：5 项新登记、1 项旧问题局部复开（均 Fixed / L1+L2，L3 待 owner）

审查基线 `ab5bb2920542d89bdf5e10d3217c75204804f3bf`，确认 **2 项 P1、4 项 P2**。用户随后授权“全部修复确保没问题就提交 commit”，六项均已修复。原始复现值与审查时的红项保留在 [审查快照](docs/reports/reviews/2026-09-25_full_audit_report.md)；修复设计、验证和逐步实机清单见 [修复交付](docs/reports/testing/2026-09-25_full_audit_fixes.md)。没有本轮 L3，不将离线通过写成已验证可玩。

| ID | 级别 / 兼容分类 | 已确认问题与修复 | 验证 / 状态 |
| --- | --- | --- | --- |
| CR-2026-09-25-002 | P1 / COMPAT / SCHEMA+ | 押物品原先先 Settled、后发奖，发送失败与重启可能丢奖，实物没有对应保存快照。`ModeHCashBetService.TrySettleItems` 现先提交固定计划，再将实物和剩余义务同批保存，最后结清现金；满包保留欠账，已准备计划不能退款或被下一笔覆盖。 | **Fixed / L1+L2**。SaveFailureRecovery 覆盖交付前后异常、满包、部分交付、计划/实物/钱包三个保存失败与重启边界、输局快照、切槽及通知重入。 |
| CR-2026-09-25-003 | P1 / COMPAT / SCHEMA+ | 恢复原先按 TypeID/数量误认另一件未押装备。锁盘给物品写持久身份并随主角树保存，`ModeHItemBetStake.RebindFromLedger` 只按 TypeID + 唯一身份匹配；旧账本无身份或重复身份时不猜测，沿用缺失估值补偿。 | **Fixed / L1+L2**。同型号第二件押注、场景销毁重建、重复身份、旧五列凭据与数量增减执行回归通过。 |
| CR-2026-09-25-001 | P2 / COMPAT | 日报 Store 已推进一天、物理保存失败却保留旧计时，重试会再推进一天并误清连签。`DailyReportService.SettleRollover` 改为 Store 接受即消费计时，IO 由协调器重试。 | **Fixed / L1+L2**。SaveFailureRecovery 验证 Store 拒绝、SaveFile 异常后 IsSaving 保持 true、恢复后日号/余数一致；领取入口硬写失败仍反馈 PersistBlocked。 |
| CR-2026-09-11-019（邀请函局部分支） | P2 / COMPAT | 已实例化却未送达仍销账。`ZombieModeEntryDebt.TryDeliverInvitation` 统一接收方门控与背包/仓库/有效拾取物/Buffer 回执；未送达不销账，已有回执的通知异常不重发，关卡就绪补偿早于角色加载的经济事件。 | **Fixed / L1+L2**。原夹具错误判据已纠正，75 条断言通过；现金补偿算法不变。 |
| CR-2026-09-25-004 | P2 / SAFE | ModeHRecoverySecondReview 固定点击过期 Actions，生产 Offered 入口已是 Cards。夹具仅调整 Offered 卡片入口，Applied 确认仍保留 Actions。 | **Fixed / L2**。原 33 条 owner、幂等、恢复与持久屏障断言全部通过。 |
| CR-2026-09-25-005 | P2 / OPERATIONAL | 本机作者霜冠校准副本、prefab 和 ResourceRelease 分叉，仓库现有包本来正确。作者源已按已发布姿态同步，同包铠甲一起防止倒退；只重打 frost_set，逐对象和整体哈希与仓库原包一致。 | **Fixed / 本机 L1+L2**。Unity 校验、回读通过；作者导出/发布源/仓库包均为 `a3236664…e553`。未部署或试戴，观感待 owner。 |

修复后验证：Windows 隔离正式编译通过，正式 DLL 无 14 个 Dev 专用标识；全量执行回归 **60 PASS / 0 FAIL**；修改守卫的 **13 个落盘反向探针**全部在预期断言处转红、SHA-256 核验还原；Wiki 构建、80 项导航、237 页 / 39,133 引用链接检查通过。全量守卫 **665 PASS / 0 FAIL / 0 KNOWN-RED**（含本机资源，未降级为 source-only）。修复证据在 `Build/fixes-20260925/`，原审查证据仍在 `Build/audit-20260925/`。

UNVERIFIED 不计入上述六项：嵌套 Sticky 押品需要 `AcceptSticky=true` 的玩家可达容器，尚未证实；特殊 Boss 提交失败分支的完整回收与反向遍历回调多删元素，也仍缺可达性 / 后续回收证据。云蚋 render 报错已证伪为缺 Pillow 后的半初始化连带异常，依赖齐全时原守卫通过。

<!-- END FULL AUDIT FINDINGS 2026-09-25 -->


<!-- BEGIN UI CONSENSUS AUDIT FINDINGS 2026-09-24 -->

## 2026-09-24 UI 共识对照审查中确认的缺陷（均 Fixed / L1+L2，L3 待 owner）

全文与其余 P2 / P3 在本地 `docs/reports/reviews/2026-09-24-UI共识对照审查.md`（口径 `docs/architecture/UI制作共识.md`）。这里只登记主会话亲自核对过代码、会让玩家受损或卡住的四条。同日 owner「全部修复」，四条连同其余 P2 / P3 全部修完，修法见审查报告第九节与 `FIX_TRACKER.md` 同日「UI 共识全量修复」一节。

| ID | 级别 | 问题与根因 | 位置 |
| --- | --- | --- | --- |
| CR-2026-09-24-001 | P1 | 词缀锻造已锁槽的按钮文案是状态「已锁定」，点击分支直接 `UnlockSlot`（免费、无确认），锁定时扣的熔石不退，误点即损失 | `Integration/Reforge/ReforgeUIManager_AffixForge.cs`（锁定按钮回调）、`Integration/AffixForge/AffixForgeSystem.cs`（`LockSlot` 扣熔石 / `UnlockSlot` 不退）；修：已锁槽改成不可点的「已锁定」标签 + 红描边「解锁」，点了先弹共享 `BossRushConfirmDialog` 写明熔石不退，确认回调再核对是同一件物品；守卫 `ReforgeUIFeelGuard` §7、`UIConsensusSystemPanelsGuard` |
| CR-2026-09-24-002 | P1 | 远征翻牌播放中「跳过」/ ESC 走 `SkipAll`：剩余记录全部 `MarkRevealed` 后直接关窗，阵亡与负伤结果一张不显示、也不会再弹 | `PetNest/PetNestExpeditionRevealView.cs`（`OnSkipOrClose` / `SkipAll`）；修：跳过 / ESC 改走 `SkipToSummary`，剩余记录标记已翻后收成一屏汇总（阵亡红字）再由「关闭」收起；一次翻两张以上自然翻完也收汇总；守卫 `PetNestRevealIdempotencyGuard`，执行回归 `ManualSeptemberReview` 补多张汇总断言 |
| CR-2026-09-24-003 | P1 | 丧尸撤离抉择按钮先 `RestoreInputState()` 还模态租约再调宿主；宿主拒绝（信标引导中、撤离区建不出）时页面不关，面板盖着而时间恢复、角色可动 | `ZombieMode/ZombieModeExtractionController.cs`（`ZombieModeExtractionOpportunityView` 按钮回调）；修：两颗按钮、两张卡、ESC 同走 `Choose`，宿主受理并收页时才还租约，被拒原因（新 key `Notify_ExtractionAreaFailed`）浮在「立即撤离」上方；守卫 `ZombieModeChoiceUiPauseAndLayoutGuard` |
| CR-2026-09-24-004 | P1 | 鸭王杯恢复壳「放弃本赛季并结清押品」标 `IsDanger` 直接绑 `AbandonSeasonFromRecovery`，无确认，实心红与实心主色「同场重开」并排 | `ModeH/ModeHRuntimeModule_UiFlow.cs`（`BuildRecoveryActions`）、`ModeH/ModeHRecoveryPanel.cs`；修：放弃先弹 `BossRushConfirmDialog`（Danger，写明后果），恢复壳按钮改成红描边靠左、主操作靠右，恢复壳占模态租约并新增「稍后处理」；放弃时先退挂着的押金；守卫 `ModeHStructureGuard`，执行回归 `ModeHRecoverySecondReview` |

<!-- END UI CONSENSUS AUDIT FINDINGS 2026-09-24 -->


<!-- BEGIN AESTHETIC AUDIT FINDINGS 2026-09-23 -->

## 2026-09-23 UI / 交互 / 特效审美审查中确认的缺陷（均 Fixed / L1+L2，L3 待 owner）

全部约 280 条审美 finding（观感、配色、版式、动效取舍）在本地 `docs/reports/reviews/2026-09-23-审美审查/`，流水见 `FIX_TRACKER.md` 同日「全 Mod UI / 交互 / 特效」一节。这里只登记**有确定根因、会让设计效果根本不出现或行为出错**的缺陷。

| ID | 级别 | 问题与根因 | 位置 |
| --- | --- | --- | --- |
| CR-2026-09-23-018 | P1 | 幽灵女巫整套程序化材质首选 `Legacy Shaders/Particles/Additive` 等游戏里不存在的着色器（UnityPy 直读 resources.assets），线与面片落到 `Sprites/Default` 发不了光；粒子若先撞上龙王包里的 `Particles/Standard Unlit`，没开 `_ALPHABLEND_ON` 时 alpha 恒为 1，成了加色方片 | `Integration/PhantomWitch/*`；修：`Common/Effects/BossRushFxMaterials.cs` |
| CR-2026-09-23-019 | P1 | `startSizeMultiplier` 在「两常数随机」模式下只改上限，0.1–0.2 m 的烟与星尘被拉到最大 2 m，噬魂挽歌每一刀冒紫色大雾团 | `PhantomWitchScytheSwingFx.cs` 等；守卫 `PhantomWitchScytheSwingParticleProfileGuard` 补运行时断言 |
| CR-2026-09-23-020 | P1 | `Circle` 发射器没转 90°，诅咒领域立着 4.5 m 的星火拱门，魂雾 / 地雾 / 灵纱 / 龙皇铳地面区域是竖着的圆盘 | 女巫、龙王武器 |
| CR-2026-09-23-021 | P2 | 共享粒子材质把 Legacy Alpha Blended 的 `_TintColor` 设成白（默认 0.5），片元 2×，所有使用方颜色与 alpha 翻倍（霜雾、飞行云、天空岛灶火烟成发光白团） | `Common/Effects/RingParticleEffect.cs` |
| CR-2026-09-23-022 | P1 | Mode H 结算页 `Body`（「本场胜利 / 失利」）在有逐行内容时不渲染，而逐行内容恒有「耗时」一行：每场打完都看不到胜负 | `ModeH/ModeHUIPages.cs` |
| CR-2026-09-23-023 | P2 | 通关奖励箱虚影改到透明队列，但箱子着色器只有 GBuffer pass，透明队列里根本不画：玩家只看到两盏大灯 | `LootAndRewards/VictoryRewardShadowCrateController.cs` |
| CR-2026-09-23-024 | P2 | Mode F 放置预览用不支持透明的着色器，写的 0.4 alpha 不生效，出来是纯绿 / 纯红实心模型 | `ModeF/ModeFFortifications.cs` |
| CR-2026-09-23-025 | P2 | `UnityEngine.UI.Outline` / `Shadow` 挂在 TextMeshProUGUI 上（TMP 自己 SetMesh，不走 IMeshModifier），雷达字、丧尸 HUD、弹幕以为有描边 / 投影，实际没有 | Mode F 雷达、丧尸 HUD、许愿弹幕；修：`BossRushUIKit.ApplyWorldTextOutline` |
| CR-2026-09-23-026 | P2 | 面板描边是创建时的第一个子物体，之后加的全宽标题栏 / 页脚盖住上下框线（图鉴、成就页），成就页四角露出直角 | `Common/UI/BossRushUI.cs`；修：`BossRushStrokeOnTop` |
| CR-2026-09-23-027 | P1 | 寄存「全部丢弃」是一行下划线文字，点一下直接删光全部寄存物品，没有确认 | `Integration/NPCs/Courier/StorageDepositService.cs`；守卫 `StorageDepositDiscardConfirmGuard` |
| CR-2026-09-23-028 | P2 | 幽灵女巫瞬移标记给借来的霜之哀伤冰焰改色时直接写 `sharedMaterials`：玩家手里的冰焰（以及共用那份材质的特效）被染成紫色 | `PhantomWitchVfxRedesign.cs`（`RetintTeleportMarkerAura`） |
| CR-2026-09-23-029 | P3 | 每次回基地弹一条只有中文的「BossRush 挑战已就绪！」，竞技场分支还把 GameObject 名念给玩家 | `UIAndSigns/UIAndSigns.cs` |
| CR-2026-09-23-030 | P3 | 共享按钮原地改色（页签、拍铃）时即时写常态色，鼠标还停在按钮上也丢了悬停色 | `ZombieMode/ZombieModeUIHelper.cs` |
| CR-2026-09-23-031 | P3 | 菜地在本趟基地里刚开放时，售货机的 Awake 注入早已跑过：当趟没有种子，提示却叫玩家去买 | `Integration/BackMountain/BackMountainItems.cs` |

报箱缺碰撞（CR-2026-09-23-006 的后半）仍 UNVERIFIED：离线核对代码、层、尺寸与许愿台等价；本轮加了 `[BaseBuilding]` 碰撞体参数日志，等 owner 按清单 R1 实测区分「Default 层不挡人」与「报箱特有问题」。

<!-- END AESTHETIC AUDIT FINDINGS 2026-09-23 -->


<!-- BEGIN MANUAL 16 FINDINGS 2026-09-23 -->

## 2026-09-23 人工实测 16 项中确认的缺陷（均 Fixed / L1+L2，L3 待 owner）

详情与证据见 [修复记录](docs/reports/testing/20260922人工实测发现的问题_修复记录.md)。只登记有确定根因的缺陷；功能需求类（保底、选人页、特效重做等）不在此列。

| ID | 级别 | 问题与根因 | 位置 |
| --- | --- | --- | --- |
| CR-2026-09-23-001 | P2 | 词缀名整行消失。名字 26 号、关闭自动缩字，框高 32 比一行中文矮；TMP Ellipsis 在首行都放不下时整串清空（09-19 放大字号引入，09-20 修复未覆盖高度） | `Integration/Reforge/ReforgeUIManager_AffixForge*.cs` |
| CR-2026-09-23-002 | P2 | 日报图例只有色块没有字。原因同上：行高 30，18 号字无法缩小 | `Assets/Data/DailyReportLayout.json`、`DailyReportUI_Dashboard.cs` |
| CR-2026-09-23-003 | P3 | 日报标题药丸显示灰方块。生成器画的 alpha 48 白块在 RGBA 画布上是覆盖，等于挖洞，透出背后的遮罩 | `tools/gen_daily_report_ui.py` |
| CR-2026-09-23-004 | P3 | 日报战绩表 4–5 行塞进 3 行高的框，末行露半截 | `DailyReportUI.cs`（JoinColumns） |
| CR-2026-09-23-005 | P2 | 报箱、遗种巢发灰。基地建筑包材质是天空岛环境着色器（Unlit、自算光、岛外冷色环境光），许愿台的着色器替换只认 Standard | `Common/Buildings/BuildingModelHelper.cs` |
| CR-2026-09-23-006 | P2 | 遗种巢没有实体碰撞：建预制体时只实例化模型，从来没补碰撞体。报箱缺碰撞离线未证实（UNVERIFIED） | `PetNest/PetNestBuilder.cs` |
| CR-2026-09-23-007 | P3 | 船点多出鸭王杯、天空岛两个交互圈。事后追加进组的选项没关自己的世界标记，天空岛选项的基类还重开了交互碰撞体 | `ModeH/ModeHInteractable.cs`、`DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs` |
| CR-2026-09-23-008 | P3 | 共享按钮新建时从白色淡入 0.08 秒。赋 ColorBlock 触发非即时过渡；是 F-21 的共享根因，遗种巢整页重建时尤其明显 | `ZombieMode/ZombieModeUIHelper.cs` |
| CR-2026-09-23-010 | P2 | Mode H 认证缓存键含每次启动归零的选档计数，同一版本同一存档也会反复重跑热身 | `ModeH/ModeHProductionCertification.cs` 等 |
| CR-2026-09-23-012 | P3 | Mode H HUD 用「人数区间」的文案显示场上敌人数 | `ModeH/ModeHUI.cs` |
| CR-2026-09-23-014 | P2 | 菜地种子唯一来源被玩家可关的「Boss掉落随机化」开关挡住，也没有商店来源，关掉开关就永远拿不到种子 | `LootAndRewards/LootAndRewardsSpecialLoot.cs`、`Integration/BackMountain/*` |
| CR-2026-09-23-017 | P2 | 遗种巢异色 / 炫彩特效是雾片配方染色：挂在角色根上不随崽缩小，每帧手动撒粒子（浓度随帧率变化），粒子长到 0.7–1.1 m，成一团黄雾 | `PetNest/PetNestAuraEffect.cs` |

<!-- END MANUAL 16 FINDINGS 2026-09-23 -->

<!-- BEGIN FULL AUDIT REPAIR INDEX 2026-09-22 -->

## 2026-09-22 全仓审计条目修复闭环

原报告 82 项均已逐条复核并关闭；另从未验证线索确认并修复 2 项，共 84 项。其中 75 项在会话开始时已有对应修复，经本轮复核保留；其余 9 项为本轮补修或新增确认。分类为 COMPAT，成就领奖凭据与 Dev 恢复快照为 SCHEMA+，正式构建部署为 OPERATIONAL。

全量守卫 652 PASS / 0 FAIL；全量隔离回归 56 PASS / 0 FAIL；Windows 978 源正式与 Dev 编译通过。正式 DLL 已部署，Build 与游戏目标 SHA-256 一致，14 个 Dev 专用标识缺席；72 个资源包部署哈希检查通过。Wiki 构建和 80 项导航检查通过，237 页 / 39133 个引用无缺失链接或失效锚点。

对应原审计块的 82 项状态已回填 Fixed，新增 -049/-050 已登记。没有 L3；详细证据与人工清单见 [修复记录](docs/reports/reviews/2026-09-22_full_audit_fixes.md)。

<!-- END FULL AUDIT REPAIR INDEX 2026-09-22 -->



<!-- BEGIN FULL AUDIT EXTRA INDEX 2026-09-22 -->

### CR-2026-09-22-049 · P2 / COMPAT · Mode D 旧分帧队列与异步结果缺局身份，同号新局可接收旧派发和结案

- 状态：Fixed / L1+L2；L3 待 owner。
- 证据：已确认 L1：旧分帧队列继续消费共享刷怪表，完成回调只比较 waveIndex；核心虽检查 modeDActive，同号新局重开后旧任务仍可通过。现由 ModeDRuntimeModule 持有 generation，Start/End/scene/destroy 使旧身份失效；队列、成功/失败回调和自动下一波均复核。L2 覆盖实际 runtime owner 与接线守卫，不宣称 inactive 时必然刷出实体。
- 位置：`ModeD/ModeDRuntimeModule.cs`, `ModeD/ModeD.cs`, `ModeD/ModeDWaves.cs`。

### CR-2026-09-22-050 · P2 / COMPAT · 丧尸拍照期间 unscaled 阶段与刷新时钟继续推进

- 状态：Fixed / L1+L2；L3 待 owner。
- 证据：已确认 L1：官方 TimeScaleManager 在 CameraMode.Active 时 timeScale=0，丧尸统一暂停门此前缺 CameraMode 且 Tick 接 unscaledDeltaTime。补入统一门后实际暂停时钟抽取 L2 证明 20 秒拍照不推进，恢复不补扣。
- 位置：`ZombieMode/ZombieModeEntry.cs`, `ZombieMode/ZombieModeRuntimeHooks.cs`, `Utilities/ModeRuntimeHooks.cs`。

<!-- END FULL AUDIT EXTRA INDEX 2026-09-22 -->



<!-- MANUAL 17 FIXED 2026-09-22 -->

## 2026-09-22 人工实测复核修复交付（COMPAT / OPERATIONAL / SAFE）

原 17 项全面复核中的 12 项 confirmed findings 现均完成离线修复。完整代码锚点、资源证据和人工清单见 [20260922 人工实测复核修复记录](docs/reports/testing/20260922人工实测复核修复记录.md)。原始 17 项仍有 L3 与产品口径待验；此状态只覆盖下表，其他全仓审计事项按各自记录。

| ID | 本轮状态 / 证据 |
| --- | --- |
| CR-2026-09-22-011 | Fixed / L1+L2；冰原掠夺者图鉴收录；L3 待 owner |
| CR-2026-09-22-012 | Fixed / L1+L2；Mode H 入场与续赛时序；L3 待 owner |
| CR-2026-09-22-013 | Fixed / L1+L2；派遣/放生后实体与光环回收；L3 待 owner |
| CR-2026-09-22-014 | Fixed / L1+L2；当前 D 盘正式部署缺口；部署 72 包/531 文件已核验 |
| CR-2026-09-22-015 | Fixed / L1+L2；套装旧协程清 pending 导致双发；L3 待 owner |
| CR-2026-09-20-016 | Fixed / L1+L2；日报生产包仍烤旧动态控件；L3 待 owner |
| CR-2026-09-20-017 | Fixed / L1+L2；日报正文两倍宽裁字；L3 待 owner |
| CR-2026-09-20-019 | Fixed / L1+L2；旧在途彩宠死亡翻牌丢色；L3 待 owner |
| CR-2026-09-21-034 | Fixed / L1+L2；FallbackItem 错误消耗奖励游标；L3 待 owner |
| CR-2026-09-22-016 | Fixed / L1+L2；锁定宠仍进入必拒绝的派遣菜单；L3 待 owner |
| CR-2026-09-22-017 | Fixed / L1+L2；远征翻牌暂停期间自动推进；L3 待 owner |
| CR-2026-09-21-038 | Fixed / L1+L2；云蚋守卫断言旧复制语句；守卫闭环 |

最终全量守卫 649 PASS / 0 FAIL，隔离回归 52 PASS / 0 FAIL，Windows 960 源正式编译成功；D 盘正式目标 72 包三端一致且实际判包通过，531 个部署文件一致，14 个 Dev 标识缺席。未启动游戏或访问玩家存档。证据目录 `Build/manual-fix-20260922/`。下面保留的审计复现与 Open 状态是修复前快照；本表是这些 ID 的最新状态。

<!-- BEGIN FULL AUDIT 2026-09-21 -->

## 2026-09-21—22 全仓范围审计阶段记录（SAFE / OPERATIONAL；未修代码）

本轮累计确认 82 项（含并发修复后的历史项）：P1 25、P2 57；L1 48、L2 34。没有 L3。

状态按各条记录；外部并发修复单独注明。本审计未修生产代码；全仓逐方法深读尚未完成，覆盖差异与继续清单见 [本轮报告](docs/reports/reviews/2026-09-21_full_audit_report.md)。基线 `60bb84b6278b7c08d0ab49a8396168fd59e3b81b`。没有L3、没有真实部署。

### CR-2026-09-21-001 · P1 / COMPAT · 好感读取失败后下一次存档收集覆盖原有全部关系记录

- 状态：Fixed（2026-09-22 复核）；证据：L2；会话初始已有修复：Load 先建立写屏障；KeyExisits 确认缺键或 JSON 全量成功后解除，失败时 SaveImmediate、Collect、Shutdown 与业务写入均早返。坏 JSON、类型错误和读取异常均在生产类隔离回归中保留原字节。 L3 待 owner。
- 位置：`Integration/Affinity/AffinityManagerPersistenceAndDecay.cs:261–296`（60bb84b）；`Integration/Affinity/AffinityManager.cs:171–183`（60bb84b）
- 触发与根因：已有NPCAffinity读取异常或坏JSON，随后正常Collect/退出保存。 Load清空表但失败无写屏障，OnCollectSaveData无条件SaveImmediate并SaveFile。
- 影响：已有好感/婚姻/跟随/故事/奖励标记被空或默认数据覆盖。
- 已核对保护：换槽清空合理，保存异常保dirty；没有区分缺key与读错的写入资格。
- 建议：存在但读失败时保持只读屏障与原字节，恢复前阻止业务与Shutdown覆盖。保持旧key。
- 验证：8份未改生产源码内存宿主probe：2300点已婚旧记录在一次Load异常后Collect变空表，Save/SaveFile各1；坏JSON同结果。Build/integration-audit-20260921/affinity-probe/result.json。

### CR-2026-09-21-002 · P1 / COMPAT · 丧尸入场满仓转存后的回滚删除唯一物品副本

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；初始工作区已保留成功转存的完整 inbox 树；SaveBuffer 失败时撤销未提交候选并保留原物。补第二件保存失败与重复回滚的资产守恒回归。L1/L2。 L3 待 owner。
- 位置：`ZombieMode/ZombieModeInventoryTransfer.cs:85–94`（60bb84b）；`ZombieMode/ZombieModeInventoryTransfer.cs:112–127`（60bb84b）；`ZombieMode/ZombieModeEntry.cs:600–633`（60bb84b）；`ZombieMode/ZombieModeCleanup.cs:389–400`（60bb84b）
- 触发与根因：丧尸入场时仓库无空格或不可用，原装备/背包物进入官方 inbox 后，后续初始化任一步失败或在最终提交前切图。 转存已 DestroyTree 原物品；回滚仅从 Buffer Remove 对应 ItemTreeData 并 SaveBuffer，未重建或保留树。
- 影响：原物品唯一副本永久消失。属于玩家资产主流程回归；未证实整个存档不可读，因此按 P1。
- 已核对保护：live warehouse item 回滚可返还，inbox 分支没有恢复；EntryDebt 仅现金/邀请函；RunScopedRegistry 不重建树；ZombieModeReviewFixGuard 只验证 Remove/Clear
- 建议：可靠重建并交付完整树后再删 buffer；失败保留备份和重试 owner，或保留已成功转存条目。补满仓/半途失败/重复回滚资产守恒回归。
- 验证：Build/audit-2026-09-21/zombie-transfer-probe 链接完整生产源码，net10 返回 1：5 PASS/2 FAIL；满仓回滚 ownedCopies=0 savedBuffer=0。无玩家存档、无 L3。

### CR-2026-09-21-003 · P1 / COMPAT · 寄存加载异常后仍允许用空缓存覆盖原全局列表

- 状态：Fixed（2026-09-22 复核）；证据：L2；初始修复已为主快照/备份完整性和读取异常建立写屏障，拒绝缺字段的部分恢复；完整备份可只读入内存。AddItem、ClearAll、Save 的故障注入均无写入；守卫副本去掉屏障已预期转红。 L3 待 owner。
- 位置：`Integration/NPCs/Courier/DepositDataManager.cs:178–182`（60bb84b）；`Integration/NPCs/Courier/DepositDataManager.cs:232–267`（60bb84b）
- 触发与根因：打开寄存时LoadGlobal任一读取异常，随后新增寄存并保存。 外层catch清缓存且isLoaded=true，绕过备份恢复并无写屏障；AddItem/Save可继续写原全局key。
- 影响：旧物品从可读主快照消失，新快照自洽不自动回旧备份，后续保存覆盖备份。
- 已核对保护：三列表generation+backup覆盖部分写入不一致，但不覆盖读取异常后继续写；全局共享寄存本身是既有设计。
- 建议：无法完整恢复时只读，拒绝寄存/取出/丢弃写事务，保留原字节并允许重新加载后恢复。
- 验证：注入主/备份读取异常与字段缺失，任何后续业务动作不改旧列表；正常旧格式仍恢复。L3未执行。

### CR-2026-09-21-004 · P1 / COMPAT · 普通寄存单件取回恢复失败时仍删除原完整物品记录

- 状态：Fixed（2026-09-22 复核）；证据：L2；初始修复将普通/净化点单件取回接管到 Buy 前缀：恢复完整树成功后才收费，交付成功后按记录身份移除。恢复 null/throw 与交付失败均保留记录，交付前异常退款并清理临时物，交付后通知异常不重复交付。 L3 待 owner。
- 位置：`Integration/NPCs/Courier/StorageDepositSingleRetrieve.cs:138–144`（60bb84b）；`Integration/NPCs/Courier/StorageDepositSingleRetrieve.cs:228–235`（60bb84b）；`Integration/NPCs/Courier/DepositDataManager.cs:273–292`（60bb84b）
- 触发与根因：普通金币寄存单件取回时 ItemTreeData.InstantiateAsync 返回 null/抛错，或实例补配/交付异常。 占位物购买后异步恢复失败仍 RemoveItem(depositIndex) 并 Save；普通分支没有保留记录和费用退款。
- 影响：原配件、容器内容与重铸变量从寄存主数据删除；玩家至多留下基础占位物，交付前异常时可能实物也不剩。
- 已核对保护：临时丧尸净化点分支及全部取回保留失败记录，但没有覆盖普通单件；备份不是事务回滚且会被后续保存替换。
- 建议：完整交付确认后才按稳定身份移除记录；失败保留原树，回滚占位物/费用并清理临时实例。
- 验证：可控恢复 null/throw/交付失败隔离测试；专用档检查含配件和容器内容的正常取回，L3 未执行。

### CR-2026-09-21-005 · P1 / COMPAT · 寄存单件、批量与关闭重开缺少共同异步事务所有权

- 状态：Fixed（2026-09-22 复核）；证据：L2；初始修复统一 DepositTransaction，绑定服务代次、槽、玩家和原收费 NPC；单件/批量互斥，Cleanup 换代，await 后拒绝旧上下文，按记录对象提交。可控任务验证旧 A 在关闭/重开 B 后完成时不交付、不移除记录且不释放 B owner。 L3 待 owner。
- 位置：`Integration/NPCs/Courier/StorageDepositSingleRetrieve.cs:113–126`（60bb84b）；`Integration/NPCs/Courier/StorageDepositBulkActions.cs:413–442`（60bb84b）；`Integration/NPCs/Courier/StorageDepositBulkActions.cs:664–675`（60bb84b）；`Integration/NPCs/Courier/StorageDepositBulkActions.cs:985–993`（60bb84b）
- 触发与根因：全部取回恢复尚未完成时单件取回相同记录；或关闭服务再重开并再次全部取回，旧 await 随后完成。 单件和批量都保存旧列表索引，缺少 session/generation/稳定记录身份；批量 busy 只保护自身，Cleanup 未取消任务却复位 busy。
- 影响：同一快照重复交付、旧索引误删/漏删当前记录；旧任务还可能使用后继服务的付款上下文。实际重叠窗口需 L3 测定。
- 已核对保护：官方 StockShop.Busy 无法覆盖直接从自定义链接进入的批量业务；单批倒序删除只保证本批索引顺序。
- 建议：所有寄存动作共享事务 owner，清理撤销代际，每次 await 后验同一上下文，按稳定记录身份提交；旧 finally 不得释放后继 busy。
- 验证：可控异步 A 挂起→关闭重开/操作 B→释放 A，断言每件至多一次且旧请求不能删新记录/扣新上下文；专用档验证异步窗口。

### CR-2026-09-21-006 · P1 / COMPAT · NPC 赠礼退回背包失败时只激活数据物品，未创建拾取代理

- 状态：Fixed（2026-09-22 复核）；证据：L2；初始修复在满包返还时调用官方 Item.Drop 创建拾取代理；主玩家缺席时改交官方仓库缓冲 owner。隔离回归直接抽取生产 DropItemOnGround 验证调用，而非只检查 activeSelf；物理拾取未做 L3。 L3 待 owner。
- 位置：`Integration/Affinity/Services/NPCGiftContainerService.cs:799–823`（60bb84b）；`鸭科夫源码/TeamSoda.Duckov.Core/ItemExtensions.cs:91–123`（60bb84b）
- 触发与根因：赠礼容器放入不能合并的礼物，再拆分玩家背包其他堆叠填满空格，关闭赠礼窗口；或满背包时 NPC 拒绝礼物。 ReturnContainerItemsToPlayer 先 Detach；AddAndMerge 失败后的 DropItemOnGround 只设位置和 SetActive，未调用官方 Drop/CreatePickupAgent。
- 影响：物品不在背包/礼物容器，又没有正常可交互拾取入口，切图后丢失。
- 已核对保护：正常 AddAndMerge 成功路径安全；官方 LootView 支持 SplitDialogue；清理不销毁原 Item 不能补足拾取代理。
- 建议：复用 item.Drop 或已验证交付工具；玩家缺席时保留明确待交付 owner。
- 验证：专用档按填满背包步骤取消赠礼，脚下必须可交互拾取原物品；离线替身检查创建拾取代理而非只检查 activeSelf。

### CR-2026-09-21-007 · P1 / COMPAT · 成就领取标记独立落盘，奖金未与当前槽经济共同保存

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；全局领奖意向先落盘，本槽现金和收据同批保存，最后提交全局已领；原槽重试不重复发钱。 L3 待 owner。
- 位置：`Achievement/BossRushAchievementManager.cs:361–388`（60bb84b）；`Achievement/BossRushAchievementManager.cs:420–426`（60bb84b）；`鸭科夫源码/TeamSoda.Duckov.Core/Saves/SavesSystem.cs:700–709`（60bb84b）
- 触发与根因：通过勋章页单件或全部领取奖励后，在下一次官方采集当前槽经济之前异常中断/重载。 EconomyManager.Add 只改活对象；SaveData 却用 SaveGlobal 立即提交全局 claimedRewards。领取入口和 OnMoneyChanged 消费者没有经济快照及共同提交。Add 返回 false 也仍提交领取状态。
- 影响：领取记录永久为已领，奖金可回到领取前余额且不允许重领；经济 manager 缺席时也会记已领。物品奖励循环只有日志，但当前目录没有配置 itemIds，不能另算不可达的物品漏发缺陷。
- 已核对保护：AchievementEntryUI.TryClaim 与 AchievementView 批量领取无另行保存；官方 Add/SaveGlobal/OnCollectSaveData；日报 MoneyChanged 仅记录 delta；全局成就跨槽是既有设计；副代理交叉复核无其他补丁补存
- 建议：先证明奖金到账，复用资产快照/持久奖励债务保证已领取事实和当前槽资产共同提交；维护全局领取身份，失败保留重试资格。
- 验证：Build/audit-2026-09-21/achievement-probe：原样抽取 ClaimReward/SaveData；money 100→600，但 durable_money=100、durable_claimed=true，模拟中断后 repeat_claim=false。替身只模拟官方已核实的 API 保存语义，非 Unity 实机。

### CR-2026-09-21-008 · P1 / COMPAT · 晴岚主线交付与现金奖励未共同持久化

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；序章和三条岛上任务统一走 TryDeliverQuest；实际余额确认、交付旗标与现金快照由同一保存批次提交。 L3 待 owner。
- 位置：`DebugAndTools/SkyIsland/SkyIslandOfficialQuestBridge.cs:138–142`（60bb84b）；`DebugAndTools/SkyIsland/SkyIslandOfficialQuestBridge.cs:523–529`（60bb84b）；`DebugAndTools/SkyIsland/SkyIslandStoryService.cs:734–751`（60bb84b）
- 触发与根因：交付晴岚主线后，Delivered事实已成功落盘，而尚未进行下一次官方经济采集时进程中断/重载。Tick(true)仍可能被保存门推迟，不保证每次交付立即落盘。 DefaultCommit（及序章自定义 Deliver）先 TryApply + Tick(true)，共享引擎以 SaveFile(false) 保存 Delivered；随后 EconomyManager.Add 只改内存 Money。天空岛 SaveSource 没有 EconomyData 快照义务。
- 影响：Delivered 已保存而 5000/3000/5000/8000 对应奖金未保存；Claimed 只读 Delivered，重建投影无法补发。
- 已核对保护：官方 EconomyManager.Add/Save/OnCollectSaveData；官方 Quest.TryComplete 及自定义 Reward.OnClaim；SkyIslandStoryService.SaveSource 与共享保存引擎；全仓 EconomyData 写入与 OnMoneyChanged 订阅；Campaign 已有现金屏障，天空岛未接入
- 建议：复用现金快照义务，将确认到账现金与交付/领取事实同批保存；失败时保留可重试奖励债务。
- 验证：L1 源码与官方反编译。需隔离中断恢复测试；L3 由 owner 在专用档逐项交付、重载核对。

### CR-2026-09-21-009 · P1 / COMPAT · 丧尸跳弹、分叉、返程支援弹遗漏僵尸倍率，实际伤害归零

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：统一支援弹 builder 继承 gun.DamageFactorToZombie，继续保持 fromWeaponItemID=0。L2：模式代理确认生产方法提取回归，三种伤害 factor 下僵尸倍率为 1.5 且基础伤害为正。 L3 待 owner。
- 位置：`ZombieMode/ZombieModeRewardTriggerEffects.cs:409–425`（60bb84b）；`ZombieMode/ZombieModeRewardTriggerEffects.cs:83–114`（60bb84b）；`ZombieMode/ZombieModeRewardCatalogAndSelection.cs:291–293`（60bb84b）；`鸭科夫源码/TeamSoda.Duckov.Core/Projectile.cs:296–300`（60bb84b）；`鸭科夫源码/TeamSoda.Duckov.Core/Health.cs:353–358`（60bb84b）
- 触发与根因：在丧尸模式取得跳弹/分叉/返程奖励，玩家原始枪弹命中后派生的支援弹再命中丧尸。 default ProjectileContext 的 damageFactorToZombie 为0，builder没有赋值；官方Projectile把0覆盖到DamageInfo，Health对isZombie乘0。
- 影响：三个正常可选奖励的支援弹伤害收益失效，对应原有代价仍生效；不泛化到别的自制弹体或所有附带效果。
- 已核对保护：实际resources.assets五种Cname_Zombie的isZombie均为1，CreateCharacterAsync确实写入Health；Sanitizer不修改isZombie；Init postfix因fromWeaponItemID=0退出；DamageInfo构造器默认1被Projectile覆盖；Health最低1点只处理正数，0无法回升；空元素因子有物理fallback，非根因
- 建议：构造支援弹时明确继承或设定正的DamageFactorToZombie，复用官方枪属性并保留支援弹归因隔离；补丧尸与普通目标/正伤害/倍率执行回归。
- 验证：Build/audit-2026-09-21/zombie-support-projectile：完整生产builder逐字抽取并链接官方ProjectileContext，3 PASS/3 FAIL，40/35/45基础伤害的context僵尸倍率均0；真实Unity资源类型树读取isZombie=1。L2止于builder与资源，伤害链L1；未运行物理命中或游戏。

### CR-2026-09-21-010 · P1 / COMPAT · 龙套OnHurt清零共享火元素列表，改变同爆炸后续目标伤害

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：OnDragonSetHurt 不再写调用方共享 elementFactors。L2：原生产回调验证元素列表不变且按现有 80% 规则回补。 L3 待 owner。
- 位置：`Integration/Bonus/DragonSetBonus.cs:465–473`（60bb84b）；`鸭科夫源码/TeamSoda.Duckov.Core/ExplosionManager.cs:37–66`（60bb84b）
- 触发与根因：火元素爆炸先命中龙套主角再中其他目标，或复用同DamageInfo列表的持续伤害再次执行。 struct中的elementFactors是List引用，OnHurt写factor=0污染调用方共享列表；伤害此时已扣。
- 影响：后续目标火伤归零、后续订阅者元素失真，当前只额外安排80%治疗。
- 已核对保护：穿龙套正火贡献分支不隔离引用；冰雷贡献观察只读。
- 建议：移除OnHurt后对共享列表的写入，保持80%回补约定；若复用现有贡献观察器，须补充fire分支，现有API只采冰/电。
- 验证：逐字生产探针factor1变0、安排80回血、后续火贡献0；见Build/integration-audit-20260921/dragon-fire-probe/result.json/source-hashes.json，实际爆炸次序待L3。

### CR-2026-09-21-011 · P1 / COMPAT · 玩家熔浆硬编码player阵营，Mode E中会烧主角和同旗友军

- 状态：Fixed（2026-09-22 复核）；证据：L1；L1：PlayerLavaZone 捕获 owner / scene，按 owner.Team 判敌，显式排除本人，失效 owner 销毁区域，并标记效果伤害。 L3 待 owner。
- 位置：`Integration/Bonus/DragonSetBonus.cs:814–826`（60bb84b）；`Integration/Bonus/DragonSetBonus_Dash.cs:420–423`（60bb84b）
- 触发与根因：以非player营旗进入Mode E，龙王套二段冲刺铺熔浆，或焚皇戟火柱复用PlayerLavaZone；主角/同旗盟友进入区域。 主角已设实际营旗阵营，PlayerLavaZone固定Team.IsEnemy(Teams.player,target)且不排Main，直接Hurt无友伤门。
- 影响：己方熔浆烧自己和盟友，80%延迟回血不能完全免傷或救已死玩家。
- 已核对保护：普通player阵营被正确排除；冰雷已改实际阵营，熔浆漏改。合同明确不伤友军。
- 建议：以实际owner.Team及显式排owner判敌，保持owner/场景有效性，标效果来源。
- 验证：普通和Mode E五阵营下主角/友军/敌军矩阵，只有敌军受伤；L3未执行。

### CR-2026-09-21-012 · P1 / COMPAT · 逆鳞棱彩弹无敌友过滤，追踪并直接伤害友军

- 状态：Fixed（2026-09-22 复核）；证据：L1；L1：搜索和碰撞共用 IsPrismaticBoltEnemy，排除 owner 和非敌对角色，伤害标记效果来源。 L3 待 owner。
- 位置：`Integration/ReverseScale/ReverseScaleAbilityManager.cs:686–696`（60bb84b）；`Integration/ReverseScale/ReverseScaleAbilityManager.cs:726–756`（60bb84b）
- 触发与根因：有可受伤召唤物/Mode E友军在场时触发逆鳞，友军在搜敌或碰撞范围。 搜敌/碰撞只排主玩家，不做Team.IsEnemy；直接Health.Hurt绕过Projectile阵营过滤，官方Hurt本身不拒绝友伤。
- 影响：反击弹可锁定/消耗在友军并击傷或击杀；缺少效果来源标记还可被直接击杀系统采集，但不声称无限递归。
- 已核对保护：主角被排除，无敌NPC拒伤但可能吸弹；new DamageInfo有正常Zombie倍率。
- 建议：搜敌和命中共享真实主角阵营的敌对判据，排除友方继续寻敌，并标自建效果归因。
- 验证：Teams.player和Mode E营旗下友军不受伤/不吸弹，敌军仍命中；L3未执行。

### CR-2026-09-21-013 · P1 / COMPAT · 旧NPC故事切图中断后堵住共享对话队列

- 状态：Fixed（2026-09-22 复核）；证据：L2；保留初始 actor/UI/session 隔离修复，并补出两个真实遗漏：旧 key 序列吞取消使调用者继续标记故事/发奖，以及 Cleanup 后旧 drain 仍可能确认同一 UI 的后继请求。现在取消传播，阿稳/羽织/叮当/婚礼文字重播独立处理取消；阿稳首次见面完成标记后移；drain 受请求代次门控。新增生产执行回归和两个副本变异均证实边界。 L3 待 owner。
- 位置：`Integration/Dialogue/DialogueManager.cs:323–352`（60bb84b）；`Integration/Dialogue/DialogueManager.cs:516–574`（60bb84b）；`Integration/Dialogue/DialogueManager.cs:709–727`（60bb84b）
- 触发与根因：阿稳首次见面/羽织或叮当故事等确认期间切图或回菜单，旧UI销毁；新场景再触发对话。 旧故事CancellationToken.None等待仅看callback；官方DoSubtitle无finally保证回调，NPC销毁不取消，生产未调Cleanup；sessionOwner不清使AcquireSession永远等。
- 影响：后续共享Mod对话不显示，旧等待和NPC业务悬挂；不宣称新场景一定永久锁移动。
- 已核对保护：NPC catch里的ForceEnd只在抛错时执行；无取消挂起不进入catch。天空岛自带取消owner。InputManager是实例集合可随场景重建。
- 建议：向旧故事传宿主/NPC/场景取消，清理时按owner取消等待并排空官方请求，迟到回调不得写新会话。
- 验证：官方DLL展开Build/audit-2026-09-21/official_DialogueUI.cs:185；完整生产DialogueManager+既有stubs探针120次Pump后新旧都挂起、sessionOwner1/pendingOfficial1/请求仍1。见dialogue-probe/result.json。

### CR-2026-09-21-014 · P1 / COMPAT · 普通重铸全部属性已固定时仍扣费并忽略 Success=false

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；全锁物品在按钮和执行入口统一拒绝；失败且未改属性退还原币种；扣款/退款通知异常也清 busy。新增两项故障用例先红后绿。 L3 待 owner。
- 位置：`Integration/Reforge/ReforgeUIManager_ComparisonAndState.cs:471–484`（60bb84b）；`Integration/Reforge/ReforgeSystem.cs:619–629`（60bb84b）；`Integration/Reforge/ReforgeSystem.cs:925–929`（60bb84b）
- 触发与根因：所有可调整属性都被冷淬液固定，投入/极性费用合计大于0且余额足够，再点击普通重铸。 CanReforge/按钮判据不排除已固定属性，扣款后 Reforge 实际跳过全部属性并返回 false，调用者无条件 reforgeCompleted=true。
- 影响：扣金币或临时丧尸服务净化点但不改变任何属性、不显示失败也不退款。
- 已核对保护：isReforging 只防同步重入；余额门不验证未锁属性；词缀锻造使用另一套补偿流程，不受此项影响。
- 建议：统一未固定属性资格供 UI/执行复用，检查 ReforgeResult.Success 并根据是否已实际变更完成退款/回滚。
- 验证：一属性/多属性全部锁定和部分锁定，普通金币/净化点两种服务验证；执行真实收费入口与 Reforge 返回值。

### CR-2026-09-21-015 · P1 / COMPAT · 龙皇召唤超时或取消后，迟到的子龙裔没有回收 owner

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：父代次、requestActive、死亡和 Mode G owner 共同门控，超时 / Dispose 清请求，迟到结果被 ReleaseChild 回收。L2：生产协程和异步方法验证正常接管、10 秒超时、Dispose 后迟到。 L3 待 owner。
- 位置：`Integration/DragonKing/DragonKingAbilityController_ChildProtection.cs:350–372`（60bb84b）；`Integration/DragonKing/DragonKingAbilityController_ChildProtection.cs:545–556`（60bb84b）；`Integration/DragonKing/DragonKingAbilityController_ChildProtection.cs:646–668`（60bb84b）
- 触发与根因：孩儿护我子龙裔生成超过 10 秒才返回，或等待期间结束模式 / 清理父方控制器。 回调只写等待协程的局部 spawnResult/spawnCompleted；超时 / StopCoroutine 没有作废异步请求，也未回收迟到结果。
- 影响：留下已激活、未完成减半属性或父子死亡订阅的 Legacy 子龙裔，退出后仍可继续制造敌人。
- 已核对保护：SpawnCore 最终 isActiveCheck 不覆盖直接调用 Legacy 子生成器；OnBossDeath/OnDestroy 仅停止协程，async 继续；CleanupChildProtection 清字段而非取消 future；DragonKingChildSpawnCancellationGuard 实跑 PASS，只断言 null→联动死亡，不验证迟到结果
- 建议：召唤请求持有 generation / parent owner；超时和清理使请求失效，回调回收迟到实体；已接管子实体由明确 owner 清理。
- 验证：Build/audit-2026-09-21/boss-child-probe/run.py 原样抽两生产方法；正常完成接管对照，超时迟到和 IEnumerator Dispose 后迟到均复现孤立实例。source-hashes.json/result.log。未 L3。

### CR-2026-09-21-016 · P1 / COMPAT · Mode G 孩儿护我子龙裔绕开 PhaseProxy 托管和掉落抑制

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：Mode G context 传播到父控制器，子龙裔以 PhaseProxy 走 prepare → owner commit → activate，关闭 Legacy 掉落，主实例字段只由 Primary 写入。L2：提交先于激活和取消 prepare 后回收均 PASS。 L3 待 owner。
- 位置：`Integration/DragonKing/DragonKingBoss_ModeGAdapter.cs:33–35`（60bb84b）；`Integration/DragonKing/DragonKingAbilityController_ChildProtection.cs:553–555`（60bb84b）；`Integration/DragonDescendant/DragonDescendantBoss.cs:230–235`（60bb84b）
- 触发与根因：Mode G 龙皇正常召唤子龙裔后，结束该局或杀死子龙裔。 适配器只传播 linked credit，不给控制器托管 context / PhaseProxy 回调；子实体通过 Legacy SpawnDragonDescendant 激活并注册标准掉落，未注册到 Mode G 表。
- 影响：Mode G End 无法可靠回收子实体，普通尸体箱和额外掉落穿透 Mode G 奖励隔离，写共享龙裔实例字段。
- 已核对保护：audit_modes 独立复核 ModeG End tracked/managedHandles/committedAuxiliaries；child:true 仅跳波次与普通死亡监听，不关闭 dropBox；LootAndRewardsRandomBossLoot 非活跃非龙皇分支保留官方箱并返还额外掉落；ManagedBossRole.PhaseProxy 仅声明；提交入口只接受 Auxiliary
- 建议：Mode G 子召唤接 prepare/commit-before-activation/cleanup 的 PhaseProxy owner 合同，并禁用 Legacy 全局登记和掉落。
- 验证：L1 双代理调用链复核。需要真实 Mode G 召唤、死亡、放弃和切图人工验收，未 L3。

### CR-2026-09-21-017 · P1 / COMPAT · 标准与无间炼狱的旧异步生成结果可以提交到退出后或新局

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；初始工作区已由 WavesArena owner 捕获波代次、scene handle、玩家身份与存活状态，标准/多 Boss 每次 await 与重试复核；普通工厂提交和专属 Boss 透传相同有效性门。L1；owner 隔离为 L2。 L3 待 owner。
- 位置：`WavesArena/WavesArenaBossSpawning.cs:581–624`（60bb84b）；`WavesArena/WavesArenaBossSpawning.cs:628–761`（60bb84b）；`ModBehaviour.cs:1171–1251`（60bb84b）；`WavesArena/WavesArena.cs:634–669`（60bb84b）
- 触发与根因：标准/无间单波或多 Boss 正在创建/重试时死亡、切图或退出；旧任务随后成功/失败，期间可能已重入新局。 factory await、逐次重试及最终成功/失败没有 run/scene/wave 身份验证，使用共享 currentBoss/currentWaveBosses/余数/波次。
- 影响：旧角色仍激活并写共享 owner，旧失败推进或修正新局波次；具体落地场景依工厂与切图时序，不声称必在基地。
- 已核对保护：死亡仅清 active/currentBoss 与退订；离场清容器但未失效异步；Tick inactive 门不覆盖异步续体；标准创建不走 SpawnEnemyCore 的 isActiveCheck
- 建议：捕获 run/scene/wave generation 并在所有 await 后与失败收尾复验；旧成功回收、旧失败只完成自身。
- 验证：静态追到真实入口与提交、死亡/场景清理。未做受控官方工厂 L2，未 L3。

### CR-2026-09-21-018 · P1 / COMPAT · Mode E 旧商人创建失败可以销毁后继局商人并关闭经济

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；初始工作区已在商人工厂 null 与异常分支前核对原 session，迟到成功只销毁原请求角色。补真实生产方法抽取的 A/B 局交错回归。L1/L2。 L3 待 owner。
- 位置：`ModeE/ModeEMerchant.cs:122–144`（60bb84b）；`ModeE/ModeEMerchant.cs:180–194`（60bb84b）；`ModeE/ModeEMerchant.cs:239–257`（60bb84b）；`ModeE/ModeEMerchantSupportClasses.cs:633–659`（60bb84b）
- 触发与根因：A 局商人创建等待中退出，B 局已建立新商人后 A 的工厂返回 null 或异常。 失败分支在 session 校验之前调用共享 FailModeEShellMerchantBuild；null 参数回退到当前 modeEMerchantNPC 并关经济/销毁。
- 影响：新局商人消失、贝壳经济入口关闭。
- 已核对保护：成功迟到有 session/scene 门；Fail helper 没有 token/owner；EndModeE 失效 session，但失败调用不读取它
- 建议：失败续体先验证捕获 session；只清理本次请求拥有的 spawnedCharacter，禁止 null 回退当前新 owner。
- 验证：L1 完整失败调用链；仍需可控 factory null/fault/success 的隔离回归与 owner L3。

### CR-2026-09-21-019 · P1 / COMPAT · 亡魂异步生成在清理或配置关闭后仍可激活登记

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：已有修复提供宿主 / 场景 / 槽 / generation 门。本轮补 OnSetFile 同槽重载失效、未移交物品树和装备恢复异常的 finally 回收。L2：五个 await 逐一重载并开启相同 raidID 后继请求，旧结果销毁且旧 finally 不释放新预约；factory / 配装异常正确清理。 L3 待 owner。
- 位置：`Integration/DeathWraith/DeathWraithSpawnFlow.cs:161–190`（60bb84b）；`Integration/DeathWraith/DeathWraithSpawnFlow.cs:217–226`（60bb84b）；`Integration/DeathWraith/DeathWraithLifecycleAndPersistence.cs:68–86`（60bb84b）；`Integration/DeathWraith/DeathWraithSystem.cs:186–200`（60bb84b）
- 触发与根因：亡魂在CreateCharacter/装备恢复/Yield期间，于同场景关闭亡魂配置；或切图/卸载使请求失效，旧任务随后完成。 多个 await 后无 host/scene handle/slot/开关/generation 复核；清理只销毁已登记实体并清 busy，尚在创建的角色不在集合内。
- 影响：迟到敌人重新激活/登记，可能残留旧场景和旧槽索引；配置关闭已退订死亡事件后迟到对象也失去该 tracking 清理。
- 已核对保护：入口同 raidID 防重复、异常 finally 回收已赋 spawnedWraith；它们没有跨清理代际作用。官方场景销毁可能使部分任务先变 null，所以不是每次切图必现。
- 建议：复用 PermanentDuckNpcModule 的 owner/generation/scene 请求纪律；每次 await 后验证，失效结果仅销毁自身，旧 finally 不写后继索引。
- 验证：可控三处挂起点分别触发清理/关闭/换槽，释放后实例和 preset 为零；L3 专用档快速退出重进确认无迟到/重复亡魂。

### CR-2026-09-22-018 · P1 / COMPAT · 婚礼站位扫描可把地下活动模板当成已放置教堂并迁移配偶

- 状态：Fixed（2026-09-22 复核）；证据：L2；初始修复在扫描和缓存回读两处调用 IsPlacedSceneObject，排除资源模板、无效/卸载场景以及非当前 active/main 场景实例。夹具验证模板与其他场景均拒绝、当前场景建筑接受；未执行 Unity 实际枚举或迁移。 L3 待 owner。
- 位置：`Integration/Wedding/WeddingBuildingInjector.cs:476–479`（60bb84b）；`Integration/Wedding/WeddingBuildingInjector.cs:630–641`（60bb84b）；`Integration/Wedding/WeddingBuildingInjector_DataEventsAndRuntime.cs:631–646`（60bb84b）
- 触发与根因：已有教堂、当前有未跟随配偶，生成或恢复配偶站位时 Building 扫描先返回 active 且 DontDestroyOnLoad 的运行时模板。 模板与已放置建筑同为 wedding_chapel ID；模板位于 (0,-9999,0) 并在创建末尾激活。FindWeddingBuildingNPCPosition 只按 ID 取首个，不排 weddingBuildingPrefabGO 或非当前场景对象，再缓存其 Transform/SpawnPoint。
- 影响：实际配偶可生成或移动到模板的地下站位 (0,-9999,2)，后续查找继续复用错误缓存，玩家在放置教堂旁找不到配偶。并非声称所有扫描顺序都必现。
- 已核对保护：HasWeddingBuildingPlaced 只证明当前槽存在教堂，不能证明命中的对象是已放置实例。ObjectCache.GetSceneObjectsByType 实际用 Object.FindObjectsOfType(type)，不排 DontDestroyOnLoad 活动模板；官方 Building.Awake 也不隐藏该模板。日报同类扫描显式排模板，可复用其判据。
- 建议：站位查询显式排除 weddingBuildingPrefabGO 并校验当前场景/已放置实例身份；仅缓存真实建筑，保持官方恢复和现有建筑 ID。
- 验证：Build/b13etc-audit-20260922/result.json 的 wedding_selector：逐字生产选择/缓存方法在模板先返回时选 (0,-9999,2)，随后重排仍复用地下缓存；实物先返回对照为 (100,0,102)。扫描顺序及函数点由替身提供，未执行 Unity 枚举或配偶迁移。

### CR-2026-09-22-001 · P1 / COMPAT · 空 Boss 池仍提交开战并隐藏难度选项，重新启用 Boss 后首波无法恢复

- 状态：Fixed（2026-09-22 复核）；证据：L1；初始工作区已在开战副作用前拒绝空池，难度交互在 IsActive 仍 false 时不隐藏选项。L1。 L3 待 owner。
- 位置：`WavesArena/WavesArenaBossSpawning.cs:73–106`（60bb84b）；`WavesArena/WavesArenaBossSpawning.cs:461–469`（60bb84b）；`Interactables/BossRushInteractables.cs:187–235`（60bb84b）；`WavesArena/WavesArenaSpawnerControl.cs:156–205`（60bb84b）
- 触发与根因：在 Ctrl+F10 Boss 池设置全不选，然后从竞技场路牌选择任一标准/无间炼狱难度；看到空池提示后按提示重新启用至少一个 Boss。 StartFirstWave 在确认池非空之前就清场并设 IsActive=true，计数归零，再调用 SpawnNextEnemy。空池分支仅显示提示并返回，没有撤销开战或安排重试。调用方不检查启动结果，随后隐藏难度选项并把路牌改成 Cheer。
- 影响：首波没有生成 Boss，无法击杀推进，也没有下一波倒计时；启用 Boss 后只有过滤缓存改变。首波卡住，玩家需离场重进，空池提示中的设置操作不能让当前挑战继续。
- 已核对保护：BossFilter.SetBossEnabled/EnableAllBosses/SyncBossPoolToConfig 无 SpawnNextEnemy/StartFirstWave 重试；保存只是 JsonUtility.ToJson/WriteAllText。；TickWavesArenaRuntime 仅在已有 waitingForNextWave 倒计时完成时刷怪；新挑战初始 waitingForNextWave=false。；TryFixStuckWaveIfNoBossAlive 在多 Boss remaining<=0、单 Boss currentBoss==null 时早返，所以不能修复从未生成首 Boss 的情况。；BossRushInteractable.IsInteractable 在 IsActive 时拒绝；难度对象又被主动失活，路牌 Cheer 的 OnTimeOut 不做事。；Config.OnModConfigOptionsChanged 只有波次间隔变更且 waitingForNextWave 已为真才重算倒计时。；主代理独立交叉核对自愈的提前返回，本代理回读后确认。
- 建议：在清场、设 active 和移除难度入口之前检查过滤池；启动失败保留可重新选择的路牌状态，或让 StartFirstWave 明确返回是否成功提交。修复不需要修改经济/配置身份。
- 验证：L1 完整入口→开战→空池返回→路牌移除→配置重启用→Tick/自愈链，未运行 Unity。owner：全不选后选难度应停留可重试入口；再启用一个 Boss 并选难度，首波应生成且不需离场。

### CR-2026-09-22-036 · P1 / COMPAT · 失效波次的迟到龙裔或龙皇只被通用 Destroy，专属生命周期账本未释放

- 状态：Fixed（2026-09-22 复核）；证据：L1；L1：实际有效性判据透传到两个专用生成器，各 await 后取消；finally 与外层保险按实例清 ledger / preset。补修龙皇 Initialize 前后引用差值，异常只释放本次取得的引用，不影响同场另一只 Boss。 L3 待 owner。
- 位置：`Integration/DragonDescendant/DragonDescendantBoss.cs:67–244`（60bb84b）；`Integration/DragonDescendant/DragonDescendantBoss_RuntimeAndCleanup.cs:231–260`（60bb84b）；`Integration/DragonKing/DragonKingBoss.cs:226–416`（60bb84b）；`Integration/DragonKing/DragonKingBoss.cs:768–827`（60bb84b）；`Utilities/EnemySpawnCore.cs:900–918`（60bb84b）
- 触发与根因：Mode D 的龙裔或龙皇、或 Mode E 的龙裔，在 CreateCharacterAsync 或 deferActivationUntilNextFrame 的 await 期间令其 wave/session token 失效；返回后 EnemySpawnCore 发现 isActiveCheck 为 false。Mode E 明确跳过龙皇，ZombieMode 使用固定僵尸 preset，标准/无间炼狱不走 SpawnEnemyCore，均不在本 finding 触发范围。 两个专用生成器在 await 后已经写入 currentBoss、龙皇实例/掉落/死亡委托字典、火焰套装 Health.OnHurt 集合，并启动 BGM；EnemySpawnCore 的失效分支只 Destroy(character.gameObject)，没有调用 CleanupDragonDescendant、CleanupTrackedDragonKingsOnArenaExit、OnDragonKingDeath 或等价的实例释放方法。
- 影响：已销毁角色可继续作为 currentBoss 或龙皇字典键；龙裔/龙皇的静态 Health.OnHurt 订阅及集合不会走对应退订路径。当前基线中 CleanupDragonDescendant 与 CleanupTrackedDragonKingsOnArenaExit 没有生产调用者，故不能依赖下一次模式结束自动收束这一迟到实例。
- 已核对保护：EnemySpawnCore:900-918 确实在失效时销毁刚返回的特殊 Boss，阻止其提交到调用方，但没有专属清理回调。；实际调用者只有 ModeD/ModeDWaves:555（waveToken）和 ModeE/ModeEBattle:579（Mode E/F session token）；ZombieMode 两个调用都传固定僵尸 preset，Mode E 明确 skipDragonKing=true。；DragonDescendantAbilityController.OnDestroy 只退订实例 OnHurt 和停止自身协程，不能移除 ModBehaviour.dragonDescendantBossHealths 或 Health.OnHurt。；DragonKingAbilityController.OnDestroy 同样不移除 ModBehaviour.activeDragonKingHealths、dragonKingInstances、死亡/掉落委托字典。；正常死亡分别会走 OnDragonDescendantDeath / OnDragonKingDeath，竞技场退出则走各自 Cleanup 路径；迟到 Destroy 未经过这些入口。；BOS-01 的孩儿护我 async void 迟到回收是另一条已登记链路，本条覆盖普通 SpawnCore 专用 Boss。
- 建议：让专用生成器接收同一份有效性/代次上下文，并在每个 await 后、任何登记前 fail-closed；保留外层保险时，应按 Boss 类型调用实例级 release，清 currentBoss、套装集合、委托字典、BGM owner、掉落跟踪和 runtime preset，再销毁角色。
- 验证：L1：逐行追踪 ModeD/ModeDWaves:548-559、ModeE/ModeEBattle:566-610、SpawnDragonDescendant/SpawnDragonKing、EnemySpawnCore 和两套正常清理。L2 建议用可控延迟的 CharacterRandomPreset：在 await 期间使 Mode D waveToken 或 Mode E session token 失效，随后断言 currentBoss 为空、两张龙皇字典为空、两个 Health 集合为空且 Health.OnHurt 已退订。未启动游戏。

### CR-2026-09-22-037 · P1 / COMPAT · 龙裔移除原枪后未核验龙息注册，InstantiateSync 异常被吞而 Boss 空手继续生成

- 状态：Fixed（2026-09-22 复核）；证据：L1；L1：龙息 GetPrefab 在原槽替换前预检；校验实例 TypeID / ItemSetting_Gun，Plug 回读确认，失败 throw 到生成 finally。真实官方 InstantiateSync 缺 entry 会抛异常的行为已由审计官方源码证据核对，现不再吞成成功。 L3 待 owner。
- 位置：`Integration/DragonDescendant/DragonDescendantBoss.cs:701–763`（60bb84b）；`Integration/DragonDescendant/DragonBreathWeaponConfig.cs:165–184`（60bb84b）；`鸭科夫源码/ItemStatsSystem/ItemAssetsCollection.cs:147–208`（60bb84b）
- 触发与根因：dragon 装备 bundle 未加载、资产丢失或动态注册失败后生成龙裔。 EquipDragonBreathWeapon 先 Unplug 并 Destroy 原枪，再直接调用 ItemAssetsCollection.InstantiateSync(500005)。真实 DLL 的 InstantiateSync 对未登记的 TypeID 读取 Instance.GetEntry(typeID).prefab；GetEntry 返回 null 时在 fallback 逻辑前抛 NullReferenceException。EquipDragonBreathWeapon 的外层 catch 仅写日志，随后 EquipDragonDescendant 和 SpawnDragonDescendant 继续刷新、激活、注册能力并返回成功。
- 影响：dragon bundle 未加载或动态注册失败时，龙裔的原枪已被销毁，龙息也未装入，Boss 仍作为成功结果进入波次，变为无主武器的核心 Boss。这个路径不依赖旧反编译中“FallbackItem”的错误推断。
- 已核对保护：FindItemByTypeId 为头盔和护甲使用 GetPrefab，但龙息实例化前没有等价预检。；DragonBreathWeaponConfig 的空检查不能处理 InstantiateSync 在到达配置器前抛出的异常。；BossRushDynamicItemRegistry 会尝试 LoadEquipmentBundles，但 EquipmentFactory 在 bundle 不存在或加载失败时返回 0；没有证据表明该路径能为 500005 合成同语义占位枪。；真实 DLL ILSpy 输出 InstantiateSync:216-235 显示：Instance 缺席或已存在 Entry 的 prefab 为空才会进入 fallback；未登记 TypeID 的 entry2 为 null，访问 prefab 直接异常。
- 建议：在移除原枪前先用 ItemAssetsCollection.GetPrefab(500005) 验证真实 prefab；缺失时 fail-closed 并由现有 failure 通知处理。若实例化/配置/Plug 任何一步失败，恢复原枪或销毁本次角色，不得继续作为成功龙裔。
- 验证：L1：已核对装备顺序、工厂失败语义与真实 DLL 实现。L2 建议替身让 GetEntry(500005) 返回 null，确认原枪未移除或生成整体失败且角色被清理。未修改 bundle、未实机。

### CR-2026-09-22-038 · P1 / COMPAT · 荒野号角的在途坐骑创建跨场景后仍绑定旧主角并发布结果

- 状态：Fixed（2026-09-22 复核）；证据：L1；L1：坐骑请求捕获主角 / host / scene / generation，创建返回后再校验，迟到 horse 销毁；ClearMountCache 作废 pending，旧 finally 不复位新请求。 L3 待 owner。
- 位置：`Integration/Items/WildHornUsage.cs:72–100`（60bb84b）；`Integration/Items/WildHornUsage.cs:116–191`（60bb84b）；`Integration/BossRushIntegration_StartAndScene.cs:329–342`（60bb84b）
- 触发与根因：玩家使用荒野号角，testVehicle.CreateCharacterAsync 尚未返回时切图、结束出击或重建主角。 SpawnMountAsync 只在 await 前记录 sceneIndex，并在 await 后无场景、主角身份、请求代次或 pending owner 校验地把 horseAI.master、player.horseAI 和静态 cachedHorseAI 写到捕获的 player。OnSceneLoaded 仅 ClearMountCache，不能取消 UniTask 或销毁迟到的 horse。
- 影响：迟到坐骑可以遗留在旧或新场景，并把已销毁的旧 CharacterMainControl 写为主人；新场景第一次使用号角还可能因迟到回调重建缓存而误判已有坐骑。
- 已核对保护：OnUse 用 3 秒冷却避免短时重复按键，但没有 pending spawn 标志。；ClearMountCache 只置 cachedHorseAI = null；没有 generation、CancellationToken 或迟到 horse 的 Destroy。；创建成功后没有比较 SceneManager.GetActiveScene().buildIndex、CharacterMainControl.Main 或 player 的有效性。；现有场景初始化确实调用 WildHornUsage.ClearMountCache，因此该清理窗口可由正常切图触发。
- 建议：给每次创建分配 scene/request generation，在 await 后同时核对当前 scene、主角引用和请求号；失败时清理迟到 horse。将 pending 请求纳入 ClearMountCache/运行时清理，并在请求仍在途时拒绝第二次创建。
- 验证：L1：已读取 OnUse、SpawnMountAsync、ClearMountCache 和场景加载调用点。L2 建议用延迟 vehicle preset：触发后切图，再完成任务，断言 horse 被销毁、旧 player.horseAI 未写入且新场景缓存保持空。未启动游戏。

### CR-2026-09-22-039 · P1 / COMPAT · “开启下次扫箱”会直接销毁尚未领取的代收箱物品

- 状态：Fixed（2026-09-22 复核）；证据：L2；初始修复将下次扫箱按钮改为先 ReleasePendingSweepResultToPlayer，失败保留 pending owner 并立即返回，成功清空箱后才新开付费流程。直接抽取生产路径验证 2 件未领物先返还再销毁，返还异常保留两件且无新扫箱；守卫变异确认阻止恢复旧丢弃调用。 L3 待 owner。
- 位置：`Integration/NPCs/Courier/CourierPaidLootSweepService.cs:757–781`（60bb84b）；`Integration/NPCs/Courier/CourierPaidLootSweepService.cs:784–818`（60bb84b）；`Integration/NPCs/Courier/CourierPaidLootSweepService.cs:1022–1121`（60bb84b）
- 触发与根因：付费扫箱结果窗口仍含任意物品时，玩家不逐件取走，直接点击结果窗 0.15 秒后出现的“开启下次扫箱 / Start Next Sweep”。 按钮回调进入 DiscardPendingSweepResultInternal，而不是 ReleasePendingSweepResultToPlayer。前者关闭 LootView、Destroy pendingResultObject 并清空所有 owner 引用；只有退出服务和 runtime reset 的路径会先 TryReturnResultItemsToPlayer。
- 影响：本次已经从 Boss 箱转移进代收箱、但尚未取走的物品失去领取路径，随后下一次扫箱会再次收费。按钮文字没有说明会丢弃上一箱，且同类关闭/卸载路径已经采用返还语义。
- 已核对保护：结果箱只允许一份 pending owner，OnStopLoot 和 CloseServiceIfOwnedBy 能退出服务，避免重开时重复展示。；ResetStaticCaches、NPC owner 销毁和异常收尾走 ReleasePendingSweepResultToPlayer，确实会逐件返还。；TryProcessSingleLootbox 的成功分支在销毁原箱前已经把未消耗物移至结果 Inventory；所以该按钮不是仅清理一个空 presenter。；StartNextSweepDelayed 仅在销毁旧 owner 后重新打开付费确认，没有保存旧 Inventory 快照。
- 建议：按钮点击先调用 ReleasePendingSweepResultToPlayer(true, false)，确认旧箱交付后再启动下一次；若产品希望允许放弃，改成显式“放弃剩余物品”且增加二次确认，不复用开始下一次的主按钮。
- 验证：L1：从玩家分组交互 -> TryRunPaidSweep -> SetPendingSweepResult/OpenPendingSweepResult -> CreateStartNextSweepButton -> OnStartNextSweepButtonClicked 逐方法审读。补隔离替身：结果 Inventory 有两件物品时点击下一次，断言交付函数各调用一次、Destroy 发生在交付后；空箱仍可直接启动。未运行 Unity。

### CR-2026-09-21-020 · P2 / COMPAT · 龙焰印记到期后再次命中会恢复过期层数

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：AddMark 和 ConsumeMarks 统一以 Time.time < expireTime 判断有效层数。L2：实际 Tracker 覆盖临界时间、重新命中和消费。 L3 待 owner。
- 位置：`Integration/DragonKing/Weapons/DragonFlameMarkTracker.cs:40–47`（60bb84b）
- 触发与根因：印记 6 秒到期后、2 秒节流清理下一次执行前，对同一目标再次叠印记。 AddMark 命中字典直接累加旧 stacks 并刷新 expireTime，未检查旧记录是否过期。
- 影响：过期层数被续上，裂地爆燃按这些层数造成额外伤害。
- 已核对保护：GetMarkCount 与 CleanupExpired 虽检查到期，但两处实际叠加入口先 AddMark；FenHuangHalberdAction 通过 GetMarkCount 后 ConsumeMark，可消费已复活层数；清理每 2 秒执行，存在可达间隔
- 建议：AddMark 先判 expireTime，过期按新记录创建；统一查询/消费/清理的边界。
- 验证：Build/audit-2026-09-21/boss-mark-probe 直接链接整个生产文件；t=0.1 加3，t=6.05 清理，t=6.2 加1，查询和消费均4而非1。未 L3。

### CR-2026-09-21-021 · P2 / COMPAT · 火焰爆炸池预热中清缓存后 running 标志阻止所有后续预热

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：ClearFireExplosionEffectPool 推进 generation 并同时清 running / requested size，旧 finally 不改后继任务。L2：预热挂起时清池，随后新预热能够完成。 L3 待 owner。
- 位置：`Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs:321–326`（60bb84b）；`Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs:344–350`（60bb84b）；`Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs:375–385`（60bb84b）
- 触发与根因：预热停在 DelayFrame 时，切图或 runtime cleanup 清池。 Clear 递增 generation，未清 running；旧任务 finally 因 generation 不同跳过清标志，后续 Request 永远因 running=true 早退。
- 影响：本进程后续预热不再启动，池为空时回到同步创建效果；实际帧耗未采样。
- 已核对保护：ClearSceneCaches/CleanupRuntime/ResetStaticCaches 均未额外复位此标志；旧代任务 finally 的 generation 门；RentFireExplosionEffect 为空时同步创建
- 建议：清理时同时复位 running 和请求目标，保留 generation 防旧任务覆盖新任务。
- 验证：Build/audit-2026-09-21/boss-pool-probe/run.py 抽取方法体不变，UniTaskVoid 仅返回类型适配 Task；正常预热对照和中断后再次请求均执行。result.log/source-hashes.json。未 L3。

### CR-2026-09-21-022 · P2 / COMPAT · 焚天龙铳专属预热从通用装备加载入口无条件启动

- 状态：Fixed（2026-09-22 复核）；证据：L1；L1：通用登记只初始化监听；主玩家持握门控预热，生成物品等待前后均复核 generation / owner，离手和切图取消。 L3 待 owner。
- 位置：`Integration/EquipmentContentRegistry.cs:30–31`（60bb84b）；`Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs:154–185`（60bb84b）；`Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs:229–284`（60bb84b）
- 触发与根因：通用装备加载完成，即使主玩家没有拥有或手持龙铳。 全局 LoadEquipmentContent 无条件启动扫描/GenerateItems 预热，缺少实际手持 owner、scene generation 与 shutdown 取消。
- 影响：未使用装备仍执行最多三批临时物品生成，旧异步任务在缓存清理后可能回写；违反实际使用门，未采样具体帧耗。
- 已核对保护：WarmupStarted 只防重复；DestroyGeneratedItems finally 存在，因此未声称永久物品泄漏；ClearSceneCaches 重置布尔但不作废 in-flight
- 建议：复用手持变化事件门控和 generation 取消，离手/切图/清理使未完成请求作废。
- 验证：L1 调用链。需要 F3 资源性能观测验证未手持时零专属准备及退出时取消；不宣称已有帧耗测量。

### CR-2026-09-21-023 · P2 / COMPAT / OPERATIONAL · F3 收回测试物品后清恢复键，却未保存收回后的容器

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；收回测试物品后核对背包、仓库和官方 Buffer 三处总数并采集容器；成功才清恢复键，失败保留重试。补修 Buffer 纪念品漏回收、清键落盘失败仍汇总 PASS，以及缺主角时误记零基线。新快照可选 bufferCountsIncluded=true；旧快照缺 Buffer 基线时保留恢复键，避免误删原有物。 L3 待 owner。
- 位置：`DebugAndTools/F3GameplayValidationAutotest.cs:275–293`（60bb84b）；`DebugAndTools/F3GameplayValidationAutotestStory.cs:352–360`（60bb84b）；`DebugAndTools/F3GameplayValidationAutotestStory.cs:619–629`（60bb84b）
- 触发与根因：自动验收或崩溃恢复在基地收回多余测试物品后，下一次官方采集前中断并重读专用测试档。 ReclaimAutotestItems 只修改活库存；随后清恢复键并 SaveFile(false)，没有采集 MainCharacterItemData/PlayerStorage/Buffer。恢复路径同构；收回结果无条件记 PASS。
- 影响：官方返基地保存的旧测试物品重读后可再次出现，恢复键已空；验收清理结论可能为假绿。影响限 Dev 专用测试档。
- 已核对保护：AutotestLegRestore 与同步 fallback；TryRecoverAutotestSnapshot；RestoreAutotestStoryAtBase 仅还原剧情；ClearAutotestSnapshotKey 与 CompleteSession/ClearRunMarker 均 SaveFile(false)；官方 SaveFile(false) 不触发 OnCollectSaveData
- 建议：收回后的容器快照与清恢复键同批保存；逐 TypeID 核验数目，失败或不足额保留恢复键并记 FAIL/PENDING_RECOVERY。
- 验证：L1。需正常结束、取消、崩溃恢复三条重读档测试；由 owner 运行专用档。

### CR-2026-09-21-024 · P2 / COMPAT · 装备能力最终清理未销毁常驻管理器，持握事件继续留存

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：最终清理注销并 Destroy，OnDestroy 负责退订；本轮回归发现覆盖 Awake 的旧子类没有写基类 _instance，补为经只读 Instance 查找真实 manager。L2：真实 FlightAbilityManager 最终销毁断言 PASS，替身模拟 Unity 假 null 和组件连带销毁。 L3 待 owner。
- 位置：`Common/Equipment/EquipmentAbilityManager.cs:568–573`（60bb84b）；`Integration/NewWeapons/SummonStaff/SummonStaffManager.cs:85–105`（60bb84b）
- 触发与根因：Mod 初始化四个武器 manager 后卸载，随后同进程再次加载。 CleanupStatic 只 UnregisterAbility；DontDestroyOnLoad manager 的 OnDestroy 不执行，所以 _instance、输入缓存和召唤法杖持握事件不释放。
- 影响：卸载保留管理器与全局回调；不同程序集重载可积累旧组件。固定残留数量不等于已实测卡顿。
- 已核对保护：abilityEnabled=false 与 CanRunGameplayRuntimeCached 限制技能执行；相邻 combo/飞行管理器有明确 Destroy，四类武器无额外兜底。
- 建议：场景注销与宿主最终销毁分离；最终清理 Destroy 实际拥有的 manager，交给唯一 OnDestroy 退订。
- 验证：两轮加载/卸载后对象和订阅数量回基线，重新加载持握事件只触发一次；Unity Destroy 时序需 L3。

### CR-2026-09-21-025 · P2 / COMPAT · 英文环境普通重铸仍输出中文费用、概率与按钮文字

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；普通重铸费用、极性概率、按钮和错误消息在刷新时选择中英文本。 L3 待 owner。
- 位置：`Integration/Reforge/ReforgeUIManager_ComparisonAndState.cs:382–410`（60bb84b）；`Integration/Reforge/ReforgeUIManager_ComparisonAndState.cs:234–238`（60bb84b）
- 触发与根因：切英文后打开叮当普通重铸，选物并调整投入。 主要说明直接中文拼接赋给 TMP，不走本地化，连英文 Decompose 也直接换成中文重铸。
- 影响：英文玩家无法按当前语言阅读交易所需的费用、概率和操作信息。
- 已核对保护：AffixForge_HandleProbabilityDisplay 是另一条双语页面路径，不会替普通重铸翻译；没有后置正文翻译。
- 建议：在取用/刷新时通过现有 L10n 双语生成，保持既有 key 和数值。
- 验证：中英文各验证普通重铸/词缀页，选择、金额滑块和语言切换后主要文字一致更新。

### CR-2026-09-21-026 · P2 / COMPAT · 飞行体力耗尽后基类早返阻断滑翔和松手更新

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：仅飞行覆写 ContinueUpdatingWhenStaminaDepleted，其他能力保持耗尽早返。L2：零体力下降为负速度，松手后停止。 L3 待 owner。
- 位置：`Common/Equipment/EquipmentAbilityAction.cs:239–247`（60bb84b）；`Integration/FlightTotem/CA_Flight.cs:244–249`（60bb84b）
- 触发与根因：持续飞行使 CurrentStamina ≤ 0.1，然后继续按住或松开空格。 基类 OnStaminaDepleted 后 return；飞行覆写只置下降标记，不更新速度或停止；实际输入和下降在被跳过的 OnAbilityUpdate。
- 影响：官方体力恢复前不执行滑翔/松手停止，动作和平台可暂时残留；具体物理表现待 L3。
- 已核对保护：死亡门仍先执行；FlightAbilityManager 每帧 UpdateAction 也仍受同一基类门，没有第二更新路径。
- 建议：允许飞行子类接管耗尽更新，保留其他能力体力保护，确保下降和停止仍处理。
- 验证：体力 0 下保持/松开输入，检查下降速度、动作和平台清理；恢复体力后不续接旧状态。 已执行逐字生产方法 probe，见 Build/integration-audit-20260921/flight-probe/result.json 和 source-hashes.json。

### CR-2026-09-21-027 · P2 / COMPAT · 飞行目标速度在首个物理步清零，低帧率多物理步覆盖成零

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：pendingVerticalDelta 持续表示目标速度，不在首个物理步清零；既有 Stop / Reset 清理仍保留。L2：两个连续物理步速度一致，位置后备累加两步位移。 L3 待 owner。
- 位置：`Integration/FlightTotem/CA_Flight.cs:438–455`（60bb84b）；`Integration/FlightTotem/FlightAbilityManager.cs:104–118`（60bb84b）
- 触发与根因：一个渲染帧间隔执行多个 FixedUpdate。 目标速度每帧写入 pendingVerticalDelta，首个 FixedUpdate 应用后清零，下一步无新 Update 则写零；位置 fallback 也只累计一物理步位移。
- 影响：上升/下降速度依赖帧调度并可能抖动；没有实测定量比例。
- 已核对保护：停止/重置清零是合理保护，但当前运行中每个物理步也清零；没有其他 FixedUpdate 补速。
- 建议：目标速度保持到下一动作更新，Stop/Reset 清零；或使用明确累计位移预算跨物理步消费。
- 验证：相同时长在 60/30/15 FPS 和多物理步调度下比较高度和下降距离，停止后无残余位移。 已执行逐字生产方法 probe，见 Build/integration-audit-20260921/flight-probe/result.json 和 source-hashes.json。

### CR-2026-09-21-028 · P2 / COMPAT · 图片查看器的一次性 timeScale 暂停被官方下一帧覆盖

- 状态：Fixed（2026-09-22 复核）；证据：L1；图片查看器持有并释放现有 ModalInputLease，宿主卸载也释放，不再直接硬写全局时间倍率。 L3 待 owner。
- 位置：`Integration/UI/ImageViewerUI.cs:255–300`（60bb84b）；`鸭科夫源码/TeamSoda.Duckov.Core/TimeScaleManager.cs:13–32`（60bb84b）
- 触发与根因：在游戏中使用叮当涂鸦打开图片或缺图占位。 MonoBehaviour 查看器只在打开时写 Time.timeScale=0，官方 TimeScaleManager 每帧按自己的状态重写；关闭还硬写 1。
- 影响：全屏图遮住场景时后台仍运行，不能达到代码声明的暂停；输入是否被其他 View 限制未作推断。
- 已核对保护：查看器未占用 GameManager.Paused/CameraMode/官方时间管理状态；原生 TimeScaleManager 写入已从反编译源码核实。
- 建议：复用现有官方 View 或暂停/时间租约，按 owner 获取释放，不直接写全局倍率。
- 验证：安全专用场景观察后台时钟/对象暂停；叠加暂停/子弹时间关闭后原倍率保持，L3 未执行。

### CR-2026-09-21-029 · P2 / COMPAT · 旧好感记录缺衰减日期时被当成第0天并额外补扣

- 状态：Fixed（2026-09-22 复核）；证据：L2；初始解码修复让缺少 lastDecayCheckDay、lastGiftDay、lastChatDay 的旧记录保留 -1。生产回归 day 100、2300 点旧记录首次检查衰减为 0，点数与配偶保持；未修改每日衰减数值。 L3 待 owner。
- 位置：`Integration/Affinity/AffinityJsonSerializer.cs:80–85`（60bb84b）；`Integration/Affinity/AffinityData.cs:24`（60bb84b）；`Integration/Affinity/AffinityManagerPersistenceAndDecay.cs:49–77`（60bb84b）
- 触发与根因：旧记录没有lastDecayCheckDay，当前天号较大且NPC生成触发衰减。 字段默认-1被ExtractInt缺字段0覆盖，绕过首次初始化分支，并用不完整互动历史回溯30天。
- 影响：旧档升级首次加载可额外丢失好感/满级跟随资格。
- 已核对保护：真实日常衰减/30天上限保留；问题只针对缺字段首次恢复。
- 建议：缺字段保留-1并走首次初始化；其他缺省-1的字段同时核对，不改变日常数值。
- 验证：实际生产解码day100、lastGift/Chat99、points2300旧记录：字段0，扣435，剩1865；见affinity-probe/result.json。

### CR-2026-09-21-030 · P2 / COMPAT · 婚礼教堂建造资格泄漏到未解锁存档槽

- 状态：Fixed（2026-09-22 复核）；证据：L2；初始修复通过 WeddingBuildingRequirementsPatch 每次查询当前槽资格与 CanWrite，资源注册仍保留供旧教堂恢复。夹具直接抽取生产后缀验证 A 满资格 -> B 无资格 -> A 满资格；官方 Harmony 实际命中和 UI 仍待 L3。 L3 待 owner。
- 位置：`Integration/Wedding/WeddingBuildingInjector.cs:208–215`（60bb84b）；`Integration/Wedding/WeddingBuildingInjector_DataEventsAndRuntime.cs:86–91`（60bb84b）
- 触发与根因：槽A满好感并注入教堂→同进程切未解锁且未建教堂槽B→打开建造菜单。 weddingBuildingInjected无换槽复位；长寿BuildingDataCollection保留空requireBuildings/requireQuests条目，无每槽资格gate。
- 影响：B槽可提前购买教堂，绕过该槽满好感解锁。
- 已核对保护：已有教堂旧档恢复是合理例外，不能约束未建的B槽；没有婚礼专用RequirementsSatisfied补丁/条目移除。
- 建议：按槽维护建造资格和注册条目所有权，同时恢复任何已放置教堂的prefab。
- 验证：A→未解锁B→A，B不能新建，已有教堂旧档模型和配偶仍恢复。L3未执行。

### CR-2026-09-21-031 · P2 / COMPAT · NPC商店关闭未销毁每次新建的展示Item实例

- 状态：Fixed（2026-09-22 复核）；证据：L1；初始修复在生成每个独立展示 Item 时登记 ownedDisplayItems，Cleanup 对自有未进入背包/槽的实例执行 DestroyTree 并清集合，关闭与 ShowUI 失败调用 Cleanup。官方买入生成另一实体，未把购买物登记为展示 owner；未做 Unity 对象数量实测。 L3 待 owner。
- 位置：`Integration/Affinity/Systems/NPCShopSystem.cs:430–448`（60bb84b）；`Integration/Affinity/Systems/NPCShopSystem.cs:692–708`（60bb84b）
- 触发与根因：同一场景反复打开关闭叮当小店。 每货品InstantiateSync的独立Item只存字典未挂商店父级；Cleanup只Destroy商店；官方StockShop.OnDestroy只退存档事件。
- 影响：每轮残留一批展示物品及Unity资源直到场景清理；没有实测性能数字。
- 已核对保护：同次TypeID缓存去重；每次开店补库存是明文设计，不视为缺陷；寄存的主动清缓存不覆盖NPCShop。
- 建议：追踪展示Item所有权，在关闭/初始化失败时DestroyTree，避免误毁购买实物或全场扫描。
- 验证：同场景20次开关后展示Item数回基线且购买实物保持；Unity对象数量需L3。

### CR-2026-09-21-032 · P2 / COMPAT · 毒蛇匕首把Poison tick和灌能附伤误当刀击叠毒

- 状态：Fixed（2026-09-22 复核）；证据：L1；L1：OnHurt 在武器 ID 门同时排除 isFromBuffOrEffect，Poison tick 和灌能不能冒充刀击。 L3 待 owner。
- 位置：`Integration/NewWeapons/ViperDagger/ViperDaggerRuntime.cs:83–114`（60bb84b）；`Integration/AffixForge/AffixRuntimeService_Effects.cs:376–383`（60bb84b）
- 触发与根因：毒刀命中后继续持握等待官方Poison tick；或毒刀附灌能后直接攻击。 OnHurt只检查武器ID/持握/主角和目标，没有排isFromBuffOrEffect；官方Poison与灌能都保留原武器ID。
- 影响：5刀叠层与累计实伤被DoT和附伤放大，脱离真实近身命中要求。
- 已核对保护：自身毒爆weaponID0只阻断自身递归；ThunderRing已有IsPlayerDirectHit可复用。
- 建议：采用现有直接命中完整判据，只累计直接武器击打，保持Poison正常伤害。
- 验证：一刀等DoT不增加额外层；灌能一刀仍一层；五次直接命中一次爆发，L3未执行。

### CR-2026-09-21-033 · P2 / COMPAT · E/F/丧尸旧生成失败与 finally 可释放后继局的预约和计数

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；初始工作区已给 E 失败结案/重刷 finally、F 失败/异常与丧尸预约释放/波 Boss 失败补 session/run 门，普通僵尸 slotHeld 单次释放。补 F 旧请求四种结果交错回归。L1；F 生产方法 L2。 L3 待 owner。
- 位置：`ModeE/ModeEBattle.cs:589–600`（60bb84b）；`ModeE/ModeERespawnItems.cs:432–435`（60bb84b）；`ModeF/ModeFRespawn.cs:714–729`（60bb84b）；`ModeF/ModeFRespawn.cs:792–833`（60bb84b）；`ZombieMode/ZombieModeSpawner.cs:396–400`（60bb84b）；`ZombieMode/ZombieModeSpawner.cs:440–442`（60bb84b）；`ZombieMode/ZombieModeWaveController.cs:435–442`（60bb84b）
- 触发与根因：旧异步创建在 A 局退出并重开 B 局后才失败，新局已有自己的生成预约/计数。 成功提交验证 session/runId；失败与 finally 使用共享计数/标记，只看当前 active 或完全无身份门。
- 影响：E 已结案数失真、重刷 running 提前解除；F 新局 inflight 被扣并重新补位、龙裔限制清除；丧尸新局 pending/当前 Boss 余数减少，可能超额派发或提早满足实际余数。
- 已核对保护：SpawnEnemyCore 失效返回 Failed 并触发 onFailed；各 End 清共享状态但旧请求仍存在；Zombie CompleteWave 有 runId 门，不能撤销先前扣错的计数
- 建议：预约/结案操作带捕获 token 与一次消费 gate，失败清理遵循与成功相同 owner 门。
- 验证：L1 已追主路径与清理；未做跨局交错 L2/L3。

### CR-2026-09-21-034 · P2 / COMPAT · 远征补发缺少 prefab 预检，官方空壳可消费奖励游标

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；远征 GrantOneItem 在实例化前验证真实 prefab；缺资源不消费奖励游标。 L3 待 owner。
- 位置：`PetNest/PetNestExpeditionService.cs:930–942`（60bb84b）；`PetNest/PetNestExpeditionService.cs:961–963`（60bb84b）；`PetNest/PetNestExpeditionService.cs:903–913`（60bb84b）
- 触发与根因：到期远征保存的奖励 TypeID 仍存在于官方资产表，但 prefab 为空或失效时在基地补发。 GrantOneItem 仅判 InstantiateSync 返回 null；官方为存在但缺 prefab 的条目返回非 null 同 TypeID FallbackItem。共享品质池仅筛 metadata/黑名单。
- 影响：空壳被作为真实物品发放，grantedLootUnits/rewardsGranted 已提交，资源恢复后不再补发。
- 已核对保护：官方 ItemAssetsCollection.InstantiateSync/FallbackItem；BossRushQualityItemPool.BuildCandidates 无 prefab 过滤；BossRushDynamicItemRegistry 仅负责支持的 Mod 注册；GrantRewards 游标与 TryGrantPendingRewards 资产屏障；未知 TypeID GetEntry null 可能抛错，未扩大为所有未知 id
- 建议：实例化前确认 GetPrefab；缺资源保留游标。按真实物品归属核实交付。
- 验证：L1。需持久奖励条目 prefab 缺失的隔离故障注入；已有奖励池和 ContentTransactions 测试是否覆盖此入口由主代理确认。
- 当前差异复核：Fixed (concurrent edit; L1)；当前GrantOneItem在InstantiateSync前新增GetPrefab(typeId)==null返回false，缺prefab不再消费游标。；主代理已读当前diff；当前隔离回归/构建待并发差异审查者验证，原发现仍保留基线L1证据。

### CR-2026-09-21-035 · P2 / COMPAT · 金鸭雨的在途现金生成没有事件取消门

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；初始工作区已捕获该次 RandomEventContext，OnCleanup 清 owner；现金每个 await 前后及完成回调复核。補生产方法受控 late success/fault 取消测试。L1/L2。 L3 待 owner。
- 位置：`RandomEvents/RandomEventEffectsBridge_Loot.cs:498–524`（60bb84b）；`RandomEvents/RandomEventCatalog_Fun.cs:318–323`（60bb84b）；`RandomEvents/RandomEventCatalog_Fun.cs:352–355`（60bb84b）
- 触发与根因：金鸭雨逐堆 InstantiateAsync/Yield 期间，在同一地图关闭随机事件、结束当前模式或销毁宿主。 循环只比 scene.buildIndex，没有 context/generation/Scope/owner 生存校验；调用者没有把任务注册到 Scope。
- 影响：停止后尚未完成的现金请求仍继续落地，旧完成回调继续写已结束事件计数。已落地现金保留本身是设计。
- 已核对保护：RandomEventDirector.EndActiveEvent/ShutdownRuntime 的 Scope 清理；Catalog_Fun GoldenDuckRain 调用与 OnCleanup；整个 SpawnRandomEventCashPilesAsync await 前后判断；同目录 BossIntrusion/Merchant 已使用 context 身份门
- 建议：捕获该次 context，await 前后核验 owner/generation/活动事件，销毁迟到未投放物品；保留已经落地收益。
- 验证：L1。需延迟实例化期间分别取消/停用/销毁的隔离回归和 owner 实机。

### CR-2026-09-21-036 · P2 / COMPAT · 随机空投继承或进入 inactive 状态后不会激活

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；初始工作区已在 inactive staging 下克隆、创建本地库存并配置 Loader，移至当前场景后激活；首轮 Awake 随机关闭时再次激活并核对 activeInHierarchy。L1；品质池配置 L2，Unity Awake 时序仍 L3。 L3 待 owner。
- 位置：`RandomEvents/RandomEventEffectsBridge_Loot.cs:58–63`（60bb84b）；`RandomEvents/RandomEventEffectsBridge_Loot.cs:90–122`（60bb84b）；`ModBehaviour.cs:908–938`（60bb84b）
- 触发与根因：缓存模板gameObject.activeSelf为false，或新箱Loader.Awake.RandomActive对生成位置返回false。仅父物体inactive而activeSelf为true不等同于此条件。 模板 getter 取首个带 Loader 对象、不排除 inactive 场景实例；Instantiate 后配置链没有显式激活根物体。
- 影响：OnTrigger 仍报成功和消费配额，下落/landed 对 inactive Transform 也可完成，玩家无法看见或交互。
- 已核对保护：EnsureLocalInventory/CreateLocalInventory 仅建库存；DecorateLootbox 仅交互装饰；官方 MoveToActiveWithScene 只 SetParent；本机 DLL 实际 LootBoxLoader.Setup 无 SetActive(true)，CheckHideIfEmpty 只关视觉；F3 只验证非 null/landed
- 建议：采用已验证 inactive staging 和 Loader 副作用隔离，配置完独立库存后激活；验收检查 activeInHierarchy。
- 验证：L1 + 本机官方 DLL ilspycmd 展开 Build/audit-2026-09-21/LootBoxLoader.actual.cs。实际模板选中频率与游戏内结果未 L3。

### CR-2026-09-21-037 · P2 / COMPAT · 标准胜利演出延迟结束后可在后继场景创建返回交互体

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；初始工作区已在 2 秒和气泡等待后验证原 WavesArena owner，结束旧虚影与创建返回体均受同一门控。L1；有效性 L2。 L3 待 owner。
- 位置：`LootAndRewards/LootAndRewardsVictoryRewards.cs:110–146`（60bb84b）；`WavesArena/WavesArenaEntryAndTeleport.cs:310–359`（60bb84b）
- 触发与根因：标准挑战胜利后的两秒延迟/气泡等待期间发生切图或死亡返基地，等待在新场景主角存在后恢复。 场景校验只在 async 方法开始；延迟后重新读取 CharacterMainControl.Main，再无条件调用没有场景门的 TryCreateReturnInteractable。
- 影响：旧胜利对话和返回方块可出现在后继场景；场景清理会把 demoChallengeStartPosition 归零，因此不能据此宣称必然传到旧竞技场坐标。
- 已核对保护：VictoryRewardShadowCrate 属于旧场景会销毁，故不宣称旧箱必然跨图；返回交互体仅按名字去重、未验证局/场景；OnSceneLoaded 的起点归零不取消该异步续体
- 建议：胜利收尾捕获原局/scene handle；每次 await 后核对，过期只清理自身演出，不访问当前共享 controller 或创建新 UI/交互体。
- 验证：L1；需 owner 在专用档胜利演出尚未结束时离场，确认基地没有旧胜利气泡/绿色返回方块。未运行 Unity 时序。

### CR-2026-09-21-038 · P2 / SAFE / OPERATIONAL · 云蚋守卫仍要求旧音效部署语句，完整音效目录部署后全量守卫误报

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；云蚋守卫验证整个 Sounds 递归部署及产物，不再断言旧单目录复制语句；补齐本机 Pillow 后通过。 L3 待 owner。
- 位置：`tests/SkyIslandMosquitoGuard.py:158–160`（60bb84b）；`compile_official.bat:1216–1233`（60bb84b）
- 触发与根因：在具备 Pillow 等依赖的当前基线执行 python tools/run_guards.py，或 CI source-only 守卫。 守卫按 Assets\Sounds\SkyIsland\*.wav 字面量断言，构建脚本已经改用 xcopy /E /Y /I Assets\Sounds 递归部署整个目录。
- 影响：正确的当前部署结构使守卫退出 1，阻断全量门禁；这条红项不能作为游戏云蚋音效缺失的证据。
- 已核对保护：实际 compile_official 的目录递归复制与部署后子目录在场核验；未放宽/修改 guard，基线 known_red 为空；Pillow 导入失败引出的早期 AttributeError 已排除
- 建议：后续修复将守卫改为验证当前完整声音树部署语义及产物，而非旧语句，并按规定做反向验证。
- 验证：guards-with-deps.log：647 个中 645 PASS、2 FAIL；BaseBuildingResource 的依赖问题另行补验通过，云蚋部署断言仍为真实未修门禁红项。
- 当前差异复核：Fixed (concurrent edit)；其他会话将守卫改为验证Sounds整树部署。；主代理在当前工作区单独运行SkyIslandMosquitoGuard.py，退出0；Build/audit-2026-09-21/mosquito-concurrent-fix.log。；L2仅该守卫；其余当前全量由并发差异复核记录。

### CR-2026-09-21-039 · P2 / COMPAT · 无间炼狱高波数里程碑奖励发生整数溢出

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；初始工作区已用独立里程碑服务按 long 饱和计算，合法堆叠分帧发放；每阶超额皇冠/现金按完整标价进入账户。L2 验证 15/16/32/33 阶数学边界、1–16 阶完整标价守恒、异常重试与单帧最多 8 件。该既有策略需在交付中说明，不声称原物品形态完全不变。 L3 待 owner。
- 位置：`LootAndRewards/LootAndRewardsInfiniteHell.cs:215–229`（60bb84b）；`LootAndRewards/LootAndRewardsInfiniteHell.cs:259–263`（60bb84b）
- 触发与根因：无间炼狱完成第 1600 波及更高里程碑（代码无终点/上限）。 倍率是 32 位 1 << (tier-1)，long cashPerStack 又直接强转 int。第1600波 tier=16，cashPerStack=3276800000，unchecked 转 int 为 -1018167296；第3200波倍率变负，第3300波移位位数回绕。
- 影响：第1600波负数被写入StackCount后官方setter会DestroyTree，导致这一批现金被销毁；后续倍率还会变负或回绕。同方法生成皇冠数量指数增长但尚无实测帧耗。
- 已核对保护：官方Item.StackCount末尾对小于1的堆叠DestroyTree；正数还有MaxStackCount上限，未推断更早截断阈值；模式无波数上限、每100波触发；GetWaveIntervalSeconds 恒钳在2–60，零间隔不是玩家可达的触发条件
- 建议：在不任意削减奖励的前提下以 long/checked 计算并按合法堆叠分批发放；为指数倍率定义明确上界/替代结算，经济策略变化需 owner 决定。
- 验证：L1 数值推导与官方 setter；可隔离测试1500/1600/3200/3300边界，实际超高波数性能尚未采样。

### CR-2026-09-21-040 · P2 / COMPAT · 百科界面常驻对象和关闭事件缺少宿主卸载清理

- 状态：Fixed（2026-09-22 复核）；证据：L1；集成层销毁调用 WikiUIManager.Shutdown，关闭界面、退订取消事件、释放输入并销毁 root。 L3 待 owner。
- 位置：`Integration/WikiUIManager.cs:154–178`（60bb84b）；`Integration/WikiUIManager.cs:1065–1074`（60bb84b）；`Integration/WikiBookItem.cs:300–309`（60bb84b）
- 触发与根因：使用冒险家日志创建百科UI，关闭后卸载Mod；或保持百科打开时卸载。 普通静态单例持有 DontDestroyOnLoad 的 uiRoot；类没有销毁/Reset入口，宿主也没有调用其 CloseUI 或清理。只有用户正常关页才解除 OnCancelEarly。
- 影响：卸载后根Canvas/子物件仍存活；打开状态卸载会留下关闭事件回调。不同程序集重载可累积旧UI。未证明具体帧耗或永久输入锁。
- 已核对保护：全生产仓 WikiUIManager 引用及宿主/集成清理；资源工厂清理不拥有运行时clone；integration代理交叉核对无通用Canvas销毁
- 建议：补明确的宿主最终清理，先关闭/退订，再Destroy运行时root并清静态引用；场景切换是否保留阅读状态可单独保留。
- 验证：owner专用档开关书后卸载，检查WikiUI root/事件回到基线；再次加载只生成一份。L1，未L3。

### CR-2026-09-21-041 · P2 / COMPAT · 奖励箱通知异常清理会销毁已入箱物品

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；奖励箱 catch 按实际 InInventory 归属确认已交付数量，只销毁未交付的游离实例。 L3 待 owner。
- 位置：`DebugAndTools/SkyIsland/SkyIslandRewardCrate.cs:175–182`（60bb84b）；`DebugAndTools/SkyIsland/SkyIslandRewardCrate.cs:265–272`（60bb84b）
- 触发与根因：官方 AddAt 已写库存归属后，后续库存树/重量/contentChanged 观察者抛错。 Fill/AddGoods catch 不回读真实归属，直接 DestroyTree。
- 影响：已经成功入箱的奖励被删除，added 仍为0，可能进一步撤掉空箱。
- 已核对保护：官方 Inventory.AddAt 的先归属后通知时序；Build 本地库存只防串库存；已修复 helper 只被 BossLoot/Fieldcraft 使用，不保护这两条填箱路径
- 建议：复用安全入箱 helper/回读目标归属；已送达计成功，仅清理未拥有实例。
- 验证：Build/audit-2026-09-21/sky-crate-probe/run.py 原样抽两生产方法，正常对照及两条通知异常均实跑，DestroyedWhileOwned=true。未 L3。

### CR-2026-09-22-019 · P2 / COMPAT · 飞行图腾直接读取Dash并启动动作，绕过暂停和界面输入门

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：输入门统一检查 InputActived、暂停、timeScale、光标和 ActiveView；Update、TryExecuteAbility 与持续按键读取共用门。L2：暂停 / 官方界面拒绝启动，关闭界面后可以重新启动。 L3 待 owner。
- 位置：`Integration/FlightTotem/FlightAbilityManager.cs:104–118`（60bb84b）；`Integration/FlightTotem/FlightAbilityManager.cs:128–147`（60bb84b）；`Common/Equipment/EquipmentAbilityManager.cs:197–213`（60bb84b）；`Common/Equipment/EquipmentAbilityAction.cs:163–188`（60bb84b）
- 触发与根因：佩戴飞行图腾且体力≥5，打开会禁止游戏输入的暂停/界面后按Dash（默认空格），或从界面触发同一输入。 FlightAbilityManager只判断场景runtime，基类直接读取InputAction.WasPressedThisFrame/原始空格；无InputManager.InputActived/View/暂停判断。TryExecuteAbility直接StartActionByCharacter，官方该方法也不做输入门控。
- 影响：游戏输入本应被禁用时仍可启动飞行动作、创建平台/云雾并扣5点启动体力。暂停中物理是否移动取决于Unity调度，不宣称暂停时必然升高；已有体力耗尽/FixedUpdate问题另有独立条目。
- 已核对保护：CanRunGameplayRuntimeCached只挡场景加载/主菜单，不挡GameManager.Paused或View；CA_Flight未覆写IsReadyInternal为界面判断，OnAbilityStart只配置飞行表现；官方CharacterInputControl保留Dash动作，正常路径经InputManager.Dash的InputActived门；本调用绕过该路径；霜剑/焚皇戟/镰刀/法杖管理器已有IsGameplayInputAllowed，飞行没有同类判断
- 建议：飞行启动复用本仓库实际游戏输入判据：InputActived、暂停/时间状态和活动界面；继续保留并行飞行设计，不再让界面输入启动动作。
- 验证：L1调用链与官方InputManager:124–128/951–963、CharacterActionBase:StartActionByCharacter；未启动Unity。owner可在保留暂停/输入框的页面按空格，对比体力、动作/平台和成就；界面关闭前不得启动飞行。

### CR-2026-09-22-010 · P2 / COMPAT · 黑名单 itemIds 类型错误时误读后续数组并绕过安全回退

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；黑名单解析改用共享 JSON parser，itemIds 类型错误时拒收整份并走既有安全回退。 L3 待 owner。
- 位置：`Config/LootBlacklistRegistry.cs:75–95`（60bb84b）；`Config/LootBlacklistRegistry.cs:32–43`（60bb84b）；`Common/Loot/BossRushQualityItemPool.cs:87–96`（60bb84b）
- 触发与根因：Assets/Data/LootBlacklist.json 被错误编辑为 {"itemIds":null,"other":[500001]} 或 itemIds 是字符串而后面有另一个非空数组；默认随包数据正常。 ParseItemIds 找到键后直接寻找任意后续 [ 和 ]，未确认该数组属于 itemIds。非空解析结果使 RegisterBlacklist 完全采用错误列表，跳过硬编码 fallback。
- 影响：黑名单内容变成无关数组，多个随机奖励/战利品候选池不再排除应保留的任务或专属物品。没有据此断言默认配置会错发物品。
- 已核对保护：非法整数和空数组会回退，但字段类型错误且另有数组时不会；现有 LootBlacklistDataRegistryGuard 用Python json.loads核当前数据与fallback一致，不执行生产解析的坏值分支；共享品质池在构建时直接消费Contains，未叠加硬编码排除表
- 建议：用现有 BossRushJsonValue 读取指定字段并验证整数数组；错误类型、无数组或非法元素整体返回失败，保留原硬编码回退语义。
- 验证：Build/audit-2026-09-22-config/Probe.csproj 原样抽 ParseItemIds，正常42返回42，null/string itemIds均错误返回other的500001；仅日志替身，源码SHA已存，未L3。

### CR-2026-09-22-020 · P2 / COMPAT · NPC 反馈与教堂文案缓存未按语言切换失效

- 状态：Fixed（2026-09-22 复核）；证据：L2 + L1；初始修复让叮当/羽织语言缓存按 L10n.IsChinese 取用时失效，教堂天数缓存包含语言并重新注入回忆标签。夹具直接验证叮当正向反馈和羽织治疗反馈 CN -> EN -> CN；其余反馈/解锁及教堂缓存接线为 L1，未验证实际界面。 L3 待 owner。
- 位置：`Integration/Affinity/NPCs/GoblinAffinityConfig.cs:206–214`（60bb84b）；`Integration/Affinity/NPCs/NurseAffinityConfig.cs:775–799`（60bb84b）；`Integration/Wedding/WeddingChapelInteractable.cs:100–130`（60bb84b）；`Integration/Wedding/WeddingChapelInteractable.cs:191–198`（60bb84b）
- 触发与根因：先在中文读取普通赠礼反应/羽织治疗反馈/解锁说明或教堂天数，再在同会话切为英文后再次查看。反向切换同理。 NPC 的若干 static string[]/Dictionary 首次求值时已选择语言，后续仅判 null；教堂只比较配偶与天数，回忆入口仅 Awake 注入。全局 OnSetLanguage 重注入未清这些字段，也未重建教堂两项 key。
- 影响：当前语言下仍输出旧语言的礼物/治疗/解锁反馈及教堂标签，出现中英混用；取用时实时 L10n 的名字和普通对话不受这条缓存缺口影响。
- 已核对保护：ModBehaviour.OnGameLanguageChanged 确实会 InjectLocalization，但该注入不访问 NPC 配置的私有静态文本缓存；Nurse actor 名称有专用重注入，未把它误计入本项。教堂 force=true 可更新，但正常刷新不带 force。
- 建议：按当前语言键控文本缓存或保存双语原值在取用时选择；教堂缓存包含语言并刷新回忆入口，沿用现有本地化 key。
- 验证：Build/b13etc-audit-20260922/result.json：完整生产两个 NPC 配置在 CN→EN 后赠礼/治疗/商店解锁仍返回原中文，同时实时姓名已为 Yu Zhi；逐字教堂刷新仍中文，force 控制组变 Together: 2 days。无真实设置菜单/字体渲染。

### CR-2026-09-22-021 · P2 / COMPAT · 对话 actor 缓存随 NPC 重建保留已销毁对象引用

- 状态：Fixed（2026-09-22 复核）；证据：L2；初始修复在 Get/Create 时清理销毁 key/actor，Remove 通过 ReferenceEquals 识别已销毁托管 key。生产工厂夹具验证 100 次创建销毁后显式 Remove 无残留，以及未 Remove 时下一次 Create 仅保留活 actor；无每帧扫描。 L3 待 owner。
- 位置：`Integration/Dialogue/DialogueActorFactory.cs:43`（60bb84b）；`Integration/Dialogue/DialogueActorFactory.cs:94–107`（60bb84b）；`Integration/Dialogue/DialogueActorFactory.cs:294–318`（60bb84b）
- 触发与根因：反复切图并重建阿稳/叮当/羽织等通过工厂创建 actor 的 NPC。 static Dictionary 以 GameObject 为强引用 key，并保存 actor；NPC 销毁没有调用 Remove，工厂没有清扫坏条目或切图清理。Remove 本身用 Unity != null，因此对象已经销毁后调用也不删除旧 key。
- 影响：每轮已销毁的 GameObject/actor 托管包装及其引用继续留在字典，数量随重建增长，直到最终缓存 reset；没有原生内存或帧耗时实测。
- 已核对保护：Get/Create 会拒用销毁的 actor，但不会移除旧条目；官方 DuckovDialogueActor.OnDisable 只清自己的 ActiveActors 列表。AlwaysOn 最终卸载会 ResetStaticCaches，因此不是永远无法清除。
- 建议：由 NPC owner 在销毁前注销，或让工厂按安全时点清扫已销毁 key；Remove 需能按托管引用识别已销毁对象，避免每帧全表扫描。
- 验证：Build/b13etc-audit-20260922/result.json 的 actor_cache：完整生产工厂配 destroyed-as-null 的 GameObject/component 替身，100 次创建/销毁及销毁后 Remove 后仍有100项坏key；最终 Reset 后为0。只证缓存语义，不代表Unity原生内存采样。

### CR-2026-09-22-022 · P2 / COMPAT · 赠礼入口提前检查每日限制，使已婚戒指例外无法到达

- 状态：Fixed（2026-09-22 复核）；证据：L1；初始修复把唯一入口提前判断改为 CanOpenGiftSelection，已婚可到选物步骤；GiveGift 仍只允许钻石戒指绕过每日限制。顺读完整入口 -> OpenService -> ExecuteGift -> GiveGift 及三类返回消费路径，未执行真实容器选择。 L3 待 owner。
- 位置：`Integration/Affinity/Interactables/NPCGiftInteractable.cs:65–80`（60bb84b）；`Integration/Affinity/Systems/NPCGiftSystem.cs:58–68`（60bb84b）
- 触发与根因：玩家已经结婚，目标 NPC 当天已收过礼物，再点击赠礼准备送钻石戒指。 唯一生产赠礼 UI 入口先按 CanGiftToday 早返，尚未允许选择物品；GiveGift 中明文实现的已婚戒指绕过每日限额分支因此到不了。
- 影响：同配偶重复戒指的专属拒绝/返还对话、向另一 NPC 每次送戒指的花心惩罚分支，在当天目标已收礼时无法触发，玩家只看到普通今日已赠送反馈。
- 已核对保护：未婚第一次求婚仍受原每日限额；本项不建议放开所有普通礼物。OpenService 只有这个生产调用者，ExecuteGift 才调用 GiveGift，没有另一条玩家可选戒指的通路。
- 建议：让 UI 能在该例外上下文进入选物步骤，最终仍由同一物品级判据拒绝普通重复礼物并处理戒指；避免前后两套不一致的限额判据。
- 验证：L1 从 NPCGiftInteractable→NPCGiftContainerService→NPCGiftSystem 全链确认。后续专用档验证已婚同日目标收礼后再送普通物/同配偶戒指/其他NPC戒指三组，普通物仍拒绝，戒指分别按既定分支处理。未实机。

### CR-2026-09-22-023 · P2 / COMPAT · 图鉴把 Mode E 非 player 营旗的友方 Boss 计为有效击杀

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；图鉴命中计时与死亡采集都按伤害来源实际阵营调用 Team.IsEnemy，排除 Mode E 同旗 Boss。 L3 待 owner。
- 位置：`Integration/Codex/CodexKillCollector.cs:114–127`（60bb84b）；`Integration/Codex/CodexKillCollector.cs:184–198`（60bb84b）
- 触发与根因：Mode E 主角和友方 Boss 同为 wolf/scav/usec/bear/lab 等非 player 阵营，友军被玩家来源的可友伤效果击杀；这条 Health.OnDead 进入全局采集器。 OnGlobalHurt/OnGlobalDead 只比较 victim.Team == Teams.player，没有按实际来源主角的 Team 判断敌对性；雇佣 Boss 不属于 PetNestCompanionAgent 豁免。
- 影响：友方 Boss 会增加击杀数、解锁图鉴条目，并可触发首条或速杀等图鉴成就。该项是采集资格错误，不声称图鉴本身造成友伤。
- 已核对保护：Mode H、基地、主角、遗种巢随从和 player 阵营目标均有过滤；同为 player 的对照正确排除。日报采集器已使用 Team.IsEnemy(info.fromCharacter.Team, victim.Team)，不会出现这条错误。
- 建议：两条采集入口共享实际玩家阵营的敌对判据，并保留当前特殊模式、基地和随从排除；不要修改 TypeID、Boss key 或已有收藏。
- 验证：Build/b17-b20-audit-20260922/codex/result.json：同为 wolf 的事件输入 tracked=1、kills=1、Store=1、首条成就=true；同为 player 则全部为0/false，敌方对照仍正常。完整生产 collector/catalog/codec/models/milestones，现有内存宿主 stubs；没有 Unity 物理或实机伤害。

### CR-2026-09-22-024 · P2 / COMPAT · 日报签到键写入失败后仍向界面返回 Success

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；日报 Store 后检查 RequestFlush 的硬失败，只有正常延期视作已接收；硬写失败不向签到界面返回成功。 L3 待 owner。
- 位置：`Integration/DailyReport/DailyReportService.cs:975–983`（60bb84b）；`Integration/DailyReport/DailyReportService.cs:583–595`（60bb84b）；`Integration/DailyReport/DailyReportUI.cs:395–420`（60bb84b）
- 触发与根因：在基地点击签到，候选副本入队成功，但随后的 SavesSystem.Save<string> 抛错或读回不一致。 Persist 只检查 Store 的入队结果，调用丢弃布尔结果的 RequestFlush() 后直接返回 true，未检查同步 flush 已把持久层置为 StoreFaulted。
- 影响：实际旧快照仍未签到，当前内存和界面却显示已签到/签到成功；已知故障下奖品被正确阻止，但玩家得不到应有的保存失败反馈。
- 已核对保护：既有回归检查了 flush 故障后不发奖和保留欠奖，未检查签到 Outcome。正常每帧节流、非基地 deferred 与硬错误必须区分，不能把延迟保存一律视为拒绝。
- 建议：将同步硬写失败反馈到签到结果和 UI，保留可恢复 pending/债务；明确区分正常 deferred 与不可写状态，不强制绕过全局落盘节流。
- 验证：Build/b17-b20-audit-20260922/daily/result.json：完整生产 service/codec/store/coordinator/engine，受控键写异常后 Outcome=Success、StoreFaulted=true、内存 LastSigned=1、模拟磁盘 LastSigned=0、grants=0。另测物理 SaveFile 失败也返回 Success。全部使用内存保存宿主，无玩家数据访问。

### CR-2026-09-22-025 · P2 / COMPAT · 日报滚动文本改成横向拉伸后保留固定宽度，正文溢出裁剪

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；日报横向拉伸文本统一清除 sizeDelta.x，避免叠加旧固定宽度。 L3 待 owner。
- 位置：`Integration/DailyReport/DailyReportUI_Dashboard.cs:241–244`（60bb84b）；`Integration/DailyReport/DailyReportUI_Dashboard.cs:268–283`（60bb84b）
- 触发与根因：正常打开日报，任何 wrap=true 的正文进入 CreateText 的 ScrollRect 分支，包括进账、悬赏、运势和昨日战绩。 文本创建时 sizeDelta.x = slice.width；移入同宽 viewport 后 anchorMin.x=0、anchorMax.x=1，却没有把 sizeDelta.x/横向 offsets 归零，宽度成为 viewport.width + slice.width。
- 影响：正文按两倍可见宽度布局，并被 RectMask2D 裁切；换行和滚动高度与可见区域不一致，左右两端可能不可见。
- 已核对保护：ZombieModeUIHelper.CreateText/CreateRect 确实保留传入 sizeDelta；ContentSizeFitter 仅驱动 Vertical PreferredSize，Horizontal=Unconstrained；SetText 只设 Vertical；FitPaper 仅等比缩放整页，不改变局部宽度关系。
- 建议：切换到横向 stretch 时清除横向尺寸增量，使文本宽度与 viewport 一致，同时保留现有垂直首选尺寸和滚动能力。
- 验证：L1 完整布局调用链与 RectTransform 尺寸关系；未运行 Unity。owner 检查中英文首字/末字、长悬赏的换行、滚动到底完整性；不把纯尺寸推导当作截图或 L3。
- 当前差异复核：Fixed (concurrent edit; L1)；当前CreateText的横向stretch分支新增sizeDelta=Vector2.zero，消除额外横向宽度。；主代理当前代码差异复核；L3真实排版仍未执行。

### CR-2026-09-22-026 · P2 / COMPAT · 日报已声明悬赏状态的类型错误被降成 false，欠款绕过写屏障消失

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；已声明 bountyCompleted/bountyRewardClaimed 必须为布尔值；错误类型拒收，不把欠款降成未完成。 L3 待 owner。
- 位置：`Integration/DailyReport/DailyReportCodec.cs:198–212`（60bb84b）；`Integration/DailyReport/DailyReportService.cs:506–520`（60bb84b）
- 触发与根因：schemaVersion=1 的已有已结算悬赏记录中，bountyCompleted 或 bountyRewardClaimed 仍存在但 JSON 类型不合法，例如字符串；其它冻结奖金与目标字段仍可读。 旧悬赏布尔字段使用 GetBool(..., false)，将缺字段和已声明但类型错误合并；Decode 返回非空，共享 Store 不建立写屏障，GetPendingBountyCash 又依赖被降成 false 的完成标记。
- 影响：已有欠款可被视为未完成并在下一次 rollover 自洽地重写；相反方向的 claimed 字段损坏也可能导致重新支付。不能把它描述成有效正常存档必现。
- 已核对保护：新 bountyCashReward/pendingBountyCash 字段和新欠奖字段已有严格类型验证；这不覆盖旧 BountyCompleted/BountyRewardClaimed。旧档缺字段默认值是必要兼容，修复应只区分已声明的坏类型。
- 建议：对已声明的业务字段使用 TryGetBool/相应类型读取，失败拒收整份 payload 并沿用现有写屏障；缺失旧可选字段继续使用既有默认值。
- 验证：Build/b17-b20-audit-20260922/daily/result.json：800 金已完成未领取记录的 bountyCompleted 改为字符串后 decode_accepted=true、write_barrier=false、pending=0；生产 rollover 后仍为0且已重写原字符串。完整生产 codec/store/service，内存字节与事件宿主；未碰真实存档。

### CR-2026-09-22-027 · P2 / COMPAT · 幽灵诅咒使用默认 Add 修饰器，百分比减速实际变成固定值减速

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：WalkSpeed 和 RunSpeed 均在模板激活前写 PercentageAdd。L2：真实 ItemStatsSystem.Stat / Modifier 上 2、3、8 三组基础速度与 1、2、3 层均符合每层 -30%。 L3 待 owner。
- 位置：`Integration/PhantomWitch/PhantomWitchAssetManager.cs:333–365`（60bb84b）；`Integration/PhantomWitch/PhantomWitchConfig.cs:275–278`（60bb84b）；`鸭科夫源码/TeamSoda.Duckov.Core/ModifierAction.cs:11–34`（60bb84b）；`鸭科夫源码/ItemStatsSystem/Stats/ModifierType.cs:6–13`（60bb84b）
- 触发与根因：Boss 技能或玩家噬魂挽歌给角色挂幽灵诅咒，并叠至 1–3 层。 GetCurseBuff 给两个新建 ModifierAction 写 WalkSpeed/RunSpeed 和 -0.3，但从未写 ModifierType。官方默认枚举 0 是 Add，Awake 按该字段创建 Modifier，层数更新只乘 modifierValue，不改变类型。缓存的 modifierTypeField 在当前生产文件中没有消费者。
- 影响：“每层降低 30% 移速”实际每层只减 0.3 个速度单位，三层减 0.9；基础速度 3 的干净属性实例变为 2.1，而按文案三层应为 0.3。Boss 诅咒和玩家武器共用该 Buff，均受影响。
- 已核对保护：两条 ModifierAction 的字段装配、Buff effects/triggers 接线和 CurrentLayers 放大均已读。；官方 ModifierAction/ModifierType/Stat.Recalculate 已对照；没有其它代码对已构建 curse ModifierType 修正。；定位器、诅咒特效和武器 500044 元数据不改变 Modifier 类型。
- 建议：在激活 Buff 模板前明确写入现有官方百分比修饰器类型，并将 1/2/3 层与不同基础速度的数值纳入有意义的回归；保留原 Buff ID 与 30% 配置语义。
- 验证：Build/audit-2026-09-22/B35/B35Probe.csproj 引用本机真实 ItemStatsSystem.dll；输出 defaultModifier=Add, baseSpeed=3, oneLayer=2.7, threeLayers=2.1, percentageThreeLayers=0.2999999。真实官方 Stat/Modifier 执行；Buff 模板装配未在 Unity 中运行，装配→默认类型由 L1 证明。

### CR-2026-09-22-028 · P2 / COMPAT · 镰刀普攻附加诅咒在官方未命中后重新投概率，首次触发率变为 75%

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：视觉回调只观察 HasBuff，不再独立 roll 或补 AddBuff。L2：四组等权官方 / 旧 fallback 随机输入只有 2 组附加诅咒，首次概率为官方 50%。 L3 待 owner。
- 位置：`Integration/PhantomWitch/PhantomWitchCurseSweatVfx.cs:148–206`（60bb84b）；`Integration/PhantomWitch/PhantomWitchScytheWeaponConfig.cs:534–550`（60bb84b）；`Integration/PhantomWitch/PhantomWitchScytheConfig.cs:51–52`（60bb84b）；`鸭科夫源码/TeamSoda.Duckov.Core/Health.cs:312–314`（60bb84b）；`鸭科夫源码/TeamSoda.Duckov.Core/Health.cs:451–460`（60bb84b）
- 触发与根因：玩家持噬魂挽歌普攻一个存活、尚未中幽灵诅咒且没有 Buff 抗性的普通角色。 官方 Health.Hurt 已先按 damageInfo.buffChance 投一次概率并 AddBuff，之后才广播 Health.OnHurt。OnGlobalHurt 对没有诅咒的目标又用同一个 0.5 概率重新抽取，无法区分“官方正常没投中”和“装配失败”。
- 影响：未中诅咒目标首次施加的概率是 0.5 + 0.5×0.5 = 0.75，而配置为 0.5。已有诅咒时该 fallback 早返，因此不是每一击都 75%，问题集中在首次挂上或到期后重挂。
- 已核对保护：官方 ItemAgent_MeleeWeapon 将 ItemSetting 的 buff 与 buffChance 原样复制到 DamageInfo（271–272）。；CouldApply/ShouldApply 的主玩家、武器 ID、非 Buff/Effect、相同 Buff 元数据过滤全部通过正常普攻，无法消除二次 roll。；官方 Health.Hurt 的 Buff 阶段先于 OnHurt 广播；既有 Prefilter/Fallback 守卫只核资格，不核概率所有权。；目标已有诅咒时 hasCurse 分支只挂视觉并返回，不重复新增层；本报告不夸大为双层必触发。
- 建议：普攻附加只保留一个概率 owner。视觉回调只观察官方 Buff 结果；若确需补失败，复用同一次已确定的 roll 事实并按失败原因补，不重新随机。
- 验证：Build/audit-2026-09-22/B35/Program.cs 原样抽取生产 OnGlobalHurt 与 9 个辅助方法，只替换 UnityEngine.Random.value 为可控 seam；夹具按官方已核顺序先执行相同 buff 判断再调用生产 hook。四个等权概率区间组合得到 appliedCombinations=3/4。角色/Buff manager 为替身，未运行 Unity。

### CR-2026-09-22-029 · P2 / COMPAT · 特效数量降档会改写女巫招式，并隐藏仍在伤人的诅咒领域

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：残影出招轮廓和 Boss 领域持续边界均走 Critical。L2：实际 PerformancePolicy 在 Reduced / Minimal 下不跳过 Critical，相关生产调用由守卫检查。 L3 待 owner。
- 位置：`Integration/PhantomWitch/PhantomWitchAssetManager.cs:809–817`（60bb84b）；`Integration/PhantomWitch/PhantomWitchAssetManager.cs:866–879`（60bb84b）；`Integration/PhantomWitch/PhantomWitchAbilityController_PackageScheduler.cs:222–269`（60bb84b）；`Integration/PhantomWitch/PhantomWitchPerformancePolicy.cs:49–69`（60bb84b）；`Integration/PhantomWitch/PhantomWitchBossCurseRealmRuntime.cs:28–44`（60bb84b）
- 触发与根因：共享活动特效根节点达到 10 时，女巫准备残影双段攻击或提交诅咒领域。无间炼狱最多 10 个 Boss，玩家镰刀领域也进入同一个 root 计数，条件并非资源缺失才有。 WraithWindupOutline 和 BossCurseRealmVisual 被标为 Standard；Minimal 档会主动返回 null。招式把 null 当作缺预警资源而 fallback 为普通扫击；Boss 领域则无论 visual 是否存在都会继续创建/运行伤害 runtime。
- 影响：原本两次 18 点的残影攻击变为一次 18 点普通扫击；领域的短暂预警结束后可能完全没有持续视觉，但仍按 15 点/0.5 秒伤害。性能档位改变玩法与危险提示，无法仅以“可选装饰降档”解释。
- 已核对保护：生产 PerformancePolicy 的 Critical 分支永不跳过，缺口来自两个调用方的 Standard 分类。；CreateCurseRealmWarningCircle 为 Critical，但只是 1.05 秒预警，不能替代 3–4 秒持续领域。；ExecuteTelegraphedCurseRealm 与 BossCurseRealmRuntime 都不以 visualMarker 创建成功为伤害提交条件。；玩家领域与 Boss VFX 经 RegisterEffectRoot 共用计数；Config 无间炼狱数量限制为 1–10。
- 建议：把决定招式及持续危险边界的提示纳入不可省略的最小视觉，使用已有 Critical 通道；性能降档只削减装饰，不能切换伤害包或清掉领域边界。保留真实资源失败时的明确降级策略。
- 验证：诊断项目直接编入整个生产 PhantomWitchPerformancePolicy.cs：roots=9 时 Reduced/standardSkipped=false；roots=10 时 Minimal/standardSkipped=true；Critical 两次都 false。上述技能替换和领域继续伤害由生产调用链 L1 证明；未采样 Unity 实际帧耗、可见性或多人同波画面。

### CR-2026-09-22-030 · P2 / COMPAT · 女巫特效归池后仍留在旧控制器清理表，会误删新借用者特效并积累重复引用

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：Recycler 归池解除旧 owner，控制器按 owner 裁剪并避免重复登记。L2：A 归还、B 借到同根后清 A 不销毁 B；12 轮借还旧跟踪表归零。 L3 待 owner。
- 位置：`Integration/PhantomWitch/PhantomWitchVfxRedesign.cs:22–38`（60bb84b）；`Integration/PhantomWitch/PhantomWitchVfxRedesign.cs:146–168`（60bb84b）；`Integration/PhantomWitch/PhantomWitchAbilityController_CleanupAndTelemetry.cs:78–91`（60bb84b）；`Integration/PhantomWitch/PhantomWitchAbilityController_CleanupAndTelemetry.cs:139–159`（60bb84b）
- 触发与根因：同场两只女巫：A 的闪现特效播放结束进入共享池，B 随后从同 key 借到这个根对象，A 此时死亡或退出清理。单只女巫长时间重复闪现也能累积同对象引用。 VfxRecycler 归池只清子层级并失活，没有解除旧 owner 的 activeEffects 登记或更新租约。TrackEffect 每次无条件 Add；PruneDestroyedEffects 只删 Unity null，已归池但活着的根对象一直留下。
- 影响：A 的 CleanupAllEffects 会 Destroy 正在由 B 使用的同一根对象，使 B 本次特效提前消失。单 owner 重复借还同一根也令追踪表随施法次数增长；这是托管引用列表增长，未把它夸大为持续创建同数量 native 特效或已测掉帧。
- 已核对保护：TryAcquireCleanPooledRoot 会重建干净层级与复位变换，但不转移 owner 身份。；回收表每个 key 上限 20，只约束池容量，不限制每个控制器 activeEffects 的重复登记。；闪现 CreateTeleportEffect 使用池且其根没有同寿命 FadeDestroy，所以不是只能在被 Destroy 的假池对象上触发。；Boss OnBossDeath/OnPlayerDeath/OnDestroy 都进入 CleanupAllEffects；清理不是仅作废旧列表。
- 建议：让活动特效持有可核验的 owner/借用代次，并在归池时解除本次登记；owner 清理只销毁仍属于自己的当前借用。避免依靠 GameObject 引用充当跨复用周期的身份。
- 验证：Build/audit-2026-09-22/B36_B28/Program.cs 抽取生产 GetOrBuild/TryAcquire/IsReusable/CleanupRoot/Recycler/CreateRoot 与控制器 Track/Prune/Cleanup。Unity 对象、时间、销毁为替身；输出 sameRootReused=true、newOwnerEffectDestroyed=true。12 次借还后 trackedReferences=12、uniqueRoots=1。未运行 Unity 生命周期。

### CR-2026-09-22-031 · P2 / COMPAT · 玩家诅咒领域未标记效果伤害，持续 tick 可触发直接命中与击杀词缀

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：领域 DamageInfo 标记 isFromBuffOrEffect，继续保留原武器来源和数值。L2：生产伤害包进入真实 Affix IsPlayerHitOnEnemy 谓词后被拒绝作为直接命中。 L3 待 owner。
- 位置：`Integration/PhantomWitch/PhantomWitchScytheAction.cs:302–316`（60bb84b）；`Integration/AffixForge/AffixRuntimeService.cs:802–832`（60bb84b）；`Integration/AffixForge/AffixRuntimeService.cs:733–785`（60bb84b）；`Integration/AffixForge/AffixRuntimeService_Effects.cs:108–128`（60bb84b）
- 触发与根因：主玩家仍手持带汲血或灌能词缀的噬魂挽歌，右键领域对存活敌人每 0.5 秒造成伤害；领域击杀时还可能触发已装备的击杀类词缀。 领域 new DamageInfo(caster) 保留官方构造器默认 isFromBuffOrEffect=false，并写 fromWeaponItemID=500044。Affix 的直接命中/击杀过滤只靠该标记和角色/武器身份，当前领域恰好满足。ModeGTelemetrySuppressionScope 只屏蔽 Mode G 遥测，不改 DamageInfo，也不被 Affix handler 消费。
- 影响：站在领域中的敌人可持续为汲血/灌能提供额外触发，领域击杀也可被当成直接击杀。不是 Buff 自身二次投概率的问题，与 B35-02 的普攻挂诅咒概率重复独立。
- 已核对保护：真实 DamageInfo 构造器明确 isFromBuffOrEffect=false；领域没有之后补写。；汲血/灌能 AppliesTo=AnyWeapon，0.4/0.25 秒 CD 小于领域 0.5 秒 tick；远离最近射击窗口时 shot gate 不阻断。；Affix 的 _dispatching 防同步自递归，不能阻止独立每帧/每半秒入口；目标仍存活时非致命 OnHurt 路径可达。；ModeGTelemetrySuppressionScope 文档与实现均只按 exact Health 记录 Mode G suppression，不改写伤害对象。
- 建议：领域创建的 DamageInfo 显式标为效果伤害，按现有玩家效果归因契约设置武器来源字段；保持玩家作为施法者与原领域伤害数值。补生产伤害包进入实际被动归因谓词的回归。
- 验证：诊断原样提取领域 DamageInfo 初始化块及 Affix IsPlayerHitOnEnemy，宿主/伤害对象是字段替身。输出 sourceType=500044、originalIsFromBuffOrEffect=false、acceptedAsDirectHit=true；只把效果标记置 true 的控制组为 false。后续词缀分发由源码接线证明，未运行 Unity/真实回血。

### CR-2026-09-22-032 · P2 / COMPAT · 女巫 Billboard 使用地面 XZ 网格，面向相机后高度退化

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；L1：Billboard 使用独立 XY 网格，地面保持原 XZ 网格，静态清理释放新增 mesh。L2：三组宽高下相机平面投影面积等于 width × height，证据见 combat-billboard-geometry.json；未做像素目检。 L3 待 owner。
- 位置：`Integration/PhantomWitch/PhantomWitchVfxRedesign.cs:955–978`（60bb84b）；`Integration/PhantomWitch/PhantomWitchVfxRedesign.cs:1007–1024`（60bb84b）；`Integration/PhantomWitch/PhantomWitchVfxRedesign_RuntimeComponents.cs:27–36`（60bb84b）
- 触发与根因：创建任意带 PhantomWitchBillboard 的自建发光 Quad，例如瞬移亮点、蓄力心光或命中血色亮片，并由 LateUpdate 对齐相机。 GetQuadMesh 的四个顶点都在 XZ 平面、y=0；CreateBillboardQuad 用(width,height,1)缩放该网格，随后 Billboard 把世界旋转直接设为 camera.rotation。高度缩放乘的是全零 y，法线则对齐相机 up 而非 forward。
- 影响：传入 height 对实际网格没有作用；在相机平面投影中四点共线，正交或视轴中心视角退化为线，透视偏轴也不会成为预期的正面矩形。这只影响此工厂创建的 Quad，粒子、LineRenderer 与 PrimitiveType.Quad 光环不在本结论内。
- 已核对保护：没有在 MeshFilter 或 Billboard 后续补 90 度基底旋转；LateUpdate 唯一写相机旋转。；同一 GetQuadMesh 也供地面 stain 使用，不能全局改成 XY 而不区分消费者。；材质透明/双面并不能使高度全零的投影恢复面积；未以截图观感猜测。
- 建议：为面向相机的 Quad 使用 XY 基底，或在 Billboard 旋转中明确补偿地面网格基底；保持地面 Quad 的 XZ 语义，验证宽高参数与朝向。
- 验证：Build/audit-2026-09-22/B36_B28/billboard-geometry.json 从当前 GetQuadMesh 提取四顶点，按实际缩放与相机对齐合同计算。height=0.1/1/2 均 scaledYSpan=0、viewPlaneArea=0。是几何 L2，不是实机像素；不宣称所有透视偏轴位置完全不可见。

### CR-2026-09-22-033 · P2 / COMPAT · Mode H 恢复认证报告丢弃 ActionApplied，使 finish 口令从可选列表消失

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；初始工作区已恢复合法 ActionApplied=5，拒绝派生状态 PartiallyVerified=4 与越界值。真实 JSON 表/生产 writer/Registry 往返 L2。 L3 待 owner。
- 位置：`ModeH/ModeHCommandCompatibilityRegistry.cs:327–335`（60bb84b）；`ModeH/ModeHProductionCertification.cs:279–282`（60bb84b）；`ModeH/ModeHProductionCertification.cs:848–854`（60bb84b）；`ModeH/ModeHStateModel.cs:184–199`（60bb84b）；`tests/ModeHCommandCompatibilityGuard.py:119–123`（60bb84b）
- 触发与根因：正常 Mode H 生产动作认证为 finish 的两个分量写入 ActionApplied=5；首次认证结束立即 BuildReport→ApplyReportToRegistries，或后续命中认证缓存/恢复 Season。 RestoreCertificationEffects 先 ClearStableKey，再以 effect.status > Unavailable(3) 拒绝条目。新增合法 ActionApplied=5 永远被过滤，原有动作证据丢失。旧守卫还要求这一错误上限。
- 影响：真实 Commands.json 中 finish 全由动作分量组成，报告恢复前可选，之后为 ReportOnly/不可选；实际整备菜单按 IsCommandSelectable 筛掉它。混合字段/动作口令仍可为 PartiallyVerified，不能据此宣称所有动作效果都停止执行。
- 已核对保护：生产 ProbeActionGroup 写 ActionApplied，AppendEntryStatus 原样保存数值 5；首次认证 Run:281–282、缓存命中和 Season 恢复均经过 ApplyReportToRegistries；Passed/known effect/entryKind/签名/认证阈值检查未放行合法值 5；ModeHCommandController.GetSelectableCommands→ModeHRuntimeModule_LoadoutEditing.GetMatchCommands 的实际菜单调用；ModeHCommandCompatibilityGuard 固定断言旧上限；未修改守卫；ToCompatibilityStatus 也不认 5 但没有生产调用，因此未另报缺陷
- 建议：按合法枚举语义恢复 ActionApplied，继续拒绝未知状态与未知 effect；保持既有枚举整数/存档字段。同步旧守卫并反向验证，新增原生产报告写出与恢复的动作/字段/混合/非法状态往返。
- 验证：Build/audit-2026-09-22-B48：直接编译原 registry、签名/JSON/目录/DTO/枚举并加载真实七份签名表；AppendEntryStatus 从生产逐字抽取。受控成功动作输入：before.finish=ActionApplied/selectable=True；saved status=5；after.finish=ReportOnly/selectable=False；字段 guard 对照仍 VerifiedBehavior/selectable=True。一次执行退出 0。L2 只证明状态往返，未运行 Unity AI 或实机菜单。

### CR-2026-09-22-034 · P2 / COMPAT · Mode H 生产认证取消后，在途诊断角色没有迟到回收者

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；初始工作区已用独立 async owner 接收诊断句柄，取消/超时/旧代次迟到回收，成功接收后外层协程未消费时 Cancel 也可回收。补 late fault/current fault 对照。L1/L2。 L3 待 owner。
- 位置：`ModeH/ModeHProductionCertification.cs:337–365`（60bb84b）；`ModeH/ModeHProductionCertification.cs:299–305`（60bb84b）；`ModeH/ModeHProductionCertification.cs:639–648`（60bb84b）；`ModeH/ModeHRuntimeModule_SceneFlow.cs:564–569`（支持文件SHA 127a7011b255）；`ModeH/ModeHRuntimeModule_SceneFlow.cs:865–881`（支持文件SHA 127a7011b255）；`ModeH/ModeHSpawnBridge.cs:118–176`（支持文件SHA af5d23e45749）
- 触发与根因：正常 Mode H 生产认证创建第一只或第二只诊断角色的 await 尚未返回时，玩家点击诊断页取消、切图或宿主清理；raw bridge 随后成功返回。15 秒 key 超时也存在同类所有权缺口。 创建 task 只存在于 CertifyKey 局部变量；_active*Handle 在 GetResult 后才赋值。StopCoroutine/Cancel 只回收已取得 handle，raw CreateIsolatedAsync 没有取消代次或独立完成接收者，晚成功结果无人调用 Recycle。
- 影响：成功桥仍登记并返回 inactive/无敌角色与 clone preset，诊断临时对象及登记失去回收 owner。不能据此宣称必然在基地刷活怪、永久影响波次或已测具体内存；实际场景销毁时序未知。
- 已核对保护：真实诊断取消按钮→Cancel/AbortSetup→ReleaseRuntimeObjects 的 StopCoroutine 与再次 Cancel；ReleaseDiagnosticPair 仅持有已返回的 _activeScavHandle/_activeWolfHandle；raw bridge 有异常清理但成功后只返回 handle，没有 cancellation/generation；正式比赛 CreateOwnedIsolatedAsync 独立 async 续体会回收晚结果，认证未使用该包装；宿主 Reset 清登记不等于销毁没有 owner 的 Unity 对象；ModeHCertificationCoroutineDriveGuard 与 ModeHSpawnTransactionGuard 未覆盖认证等待期取消
- 建议：为认证创建保留独立 async owner/代次：完成后先核对归属，过期立即 Recycle；超时保留晚结果接收者，不提前消费 pending UniTask。复用正式生成的已验证所有权模式，补第一只/第二只创建期取消及晚成功/失败回归。
- 验证：Build/audit-2026-09-22-B49：逐字抽取 CertifyKey/Cancel/ReleaseDiagnosticPair/登记方法，受控工厂返回替身。cancel_pending late_recycled=False/recycle_calls=0；cancel_returned 对照 True/1；timeout_pending routine_more=False/False/0。取消主证明不依赖替身对 pending GetResult 的异常行为。L2仅认证消费者，Unity对象/场景/性能未运行。

### CR-2026-09-22-035 · P2 / COMPAT · Mode G 确认页将尚未选择的第一份契约画为已选中

- 状态：Fixed（2026-09-22 复核）；证据：L1；初始工作区已把第一候选初始标签改为未选，与 _selectedCandidateIndex=-1 一致；确认仍要求玩家显式选契约。L1。 L3 待 owner。
- 位置：`ModeG/ModeGInteractable.cs:197–203`（60bb84b）；`ModeG/ModeGInteractable.cs:278–285`（60bb84b）；`ModeG/ModeGInteractable.cs:383–386`（60bb84b）；`ModeG/ModeGInteractable.cs:424–428`（60bb84b）
- 触发与根因：正常携带船票和宿命回响信物进入地图，确认页初次打开后不点击契约卡，直接点击立即迎战。 OpenConfirmPage 将 _selectedCandidateIndex 置为 -1，但 Contract_0 调用 BuildChoiceLabel(first,true) 预置选中箭头；没有在建页末尾同步 SelectCandidate(0)。确认逻辑仍要求有效选择索引。
- 影响：第一项显示 → 选中标记，确认时却提示请先选择契约，需要额外点击。属于局部 UI 状态矛盾；未导致吞票、错误契约提交或整局不可玩。
- 已核对保护：真实 WavesArena/BossRushEntryFlow:191 与当前 Integration/BossRushIntegration_TravelAndSetup:400 调用 TryOpenConfirmation；可用性/preview freshness/exact scene pair 只决定开页，不设置选择索引；SelectCandidate 与 ConfirmAndStart 索引检查本身正确；完整 OpenConfirmPage 无默认 SelectCandidate(0) 调用；现有 canonical/B48/B49 未登记同一问题；相关守卫未核对该初始标记
- 建议：保持显式选择要求时，两卡初始均显示未选；若需要默认首项，则通过同一选择函数同步标签与索引。不改候选随机、存档或入场资源逻辑。
- 验证：L1 直接读取完整建页、选择、确认方法及真实调用者。无 L2/L3；低风险 UI 初始化分支未写照搬实现测试。owner 后续确认页打开后先看箭头、不点契约直接开始，显示状态和放行结果应一致。

### CR-2026-09-22-002 · P2 / COMPAT · 不完整地图向量被作为零坐标登记，替代玩家安全落点回退

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；地图向量通过共享 JSON 值解析，必须恰好三个有限数字；坏向量不再成为合法零坐标。 L3 待 owner。
- 位置：`Common/MapConfig/MapSpawnPointRegistry.cs:643–650`（60bb84b）；`Common/MapConfig/MapSpawnPointRegistry.cs:703–715`（60bb84b）；`Common/MapConfig/MapSpawnPointRegistry.cs:750–761`（60bb84b）；`ModBehaviour.cs:191–205`（60bb84b）；`WavesArena/BossRushEntryFlow.cs:292–296`（60bb84b）
- 触发与根因：外部 Assets/SpawnPoints 地图 JSON 被错误编辑或损坏：customSpawnPos=[] / ["bad",5,6]，或 spawnPoints=[[]]。默认随包数据未发现这些坏值。 ParseNextFloat 失败返回 0；向量读取不验证三个数字、闭合结构与整体有效性，仍返回 HasValue 的 Vector3 或非空刷新点数组。必填校验只检查数组非空。
- 影响：错误可选落点成为 (0,0,0)，优先于 defaultSignPos，被标准入场 SetPosition 消费；错误必填刷新点也被登记为零点。未在游戏里验证世界原点地形或坠落结果。
- 已核对保护：必填身份/显示名/刷新点非空校验与 TryLoadJson catch；GetCurrentSceneDefaultPosition 按 HasValue 优先取 customSpawnPos；标准入场无额外数据校验；SpawnPositionHelper 只做几何采样，无法恢复解析失败语义；EnemyRecoveryMonitor 只保护敌人；MapSpawnRegistryPropertyTest 使用 Python json.loads 模型，未执行生产分量解析；一致性守卫只核对当前正确数据
- 建议：复用共享 token 解析并要求三个有限数字；坏可选点回到未设置并诊断，必填点无有效候选时拒绝地图。保留合法原点输入，不把零坐标整体禁止。
- 验证：Build/audit-2026-09-22-common-followup/Probe.cs 通过 Add-Type 直接编译四份原生产解析/配置源码和 Vector3/L10n/日志替身；result.txt 记录 empty_optional/string_component accepted=true 且 custom=(0,0,0)，empty_spawn accepted=true/count=1/(0,0,0)。L2 解析 + L1 消费链，未 L3。

### CR-2026-09-22-003 · P2 / COMPAT · 共享浮点读取把有限 double 溢出为 Infinity 后仍返回成功

- 状态：Fixed（2026-09-22 复核）；证据：L1+L2；double 转 float 后再检查 NaN/Infinity，TryGetFloat 失败归零、AsFloat 返回调用者 fallback。 L3 待 owner。
- 位置：`Common/Data/BossRushJsonValue.cs:202–211`（60bb84b）；`Common/Data/BossRushJsonValue.cs:359–363`（60bb84b）；`Integration/NPCs/DuckNpc/DuckNpcBlueprint.cs:330–333`（60bb84b）；`Integration/NPCs/DuckNpc/DuckNpcMovement.cs:80–84`（60bb84b）；`Integration/NPCs/DuckNpc/DuckNpcMovement.cs:267–283`（60bb84b）
- 触发与根因：Assets/Data/DuckNpcs.json 中 canWander=true 的生产 NPC 的 wanderRadius 被错误编辑为 1e39，并在有 A* 图的场景完成 Bind 后开始闲逛。当前随包半径均正常。 TryGetFloat 仅判断 double.IsNaN/IsInfinity，转换为 float 后不验证，返回 true；AsFloat 直接强转。NPC 两层正数判断把 Infinity 当作有效半径，不能触发 8 m 回退。
- 影响：闲逛目标的分量成为 Infinity/NaN 并交给 Seeker.StartPath，错误半径未安全回退。移动系统异常时会停用移动，NPC 仍可交互；未验证 A* 的最终表现，不宣称宿主崩溃或卡局。
- 已核对保护：BossRushJsonParser 拒绝 double 非有限值，但 1e39 是有限 double；DuckNpcRegistry 使用共享 JSON 路径，蓝图/Bind 均仅以 >0 判半径；真实 DuckNpcModule/PermanentDuckNpcModule 在 canWander 时传蓝图半径；DuckNpcMovement 检查 AstarPath.active 并在异常时停用移动，但未检查 target 有限性；modelScale 已被 Mathf.Clamp 限制，排除模型无限放大论断；只报告有明确消费路径的半径问题
- 建议：转换后检查 float 有限性：TryGetFloat 返回 false，AsFloat 返回 fallback；保持 double token 本身合法。补 float.MaxValue/略超上限/±1e39/正常整数小数的实际 C# 回归。
- 验证：同一生产源探针：TryParse({"wanderRadius":1e39})=true，TryGetFloat=true/value=Infinity，GetFloat=Infinity，正数回退未触发，0.5f*radius=Infinity。NPC 消费链为 L1；Unity/A* 未运行。

### CR-2026-09-22-040 · P2 / COMPAT · 无间炼狱现金吸附提示绕过语言解析，英文界面持续显示中文

- 状态：Fixed（2026-09-22 复核）；证据：L1；初始工作区已在现金磁铁气泡取用时通过 L10n.T 解析中英，金额累计窗口保持。L1。 L3 待 owner。
- 位置：`WavesArena/InfiniteHellCashMagnet.cs:249–256`（60bb84b）
- 触发与根因：将游戏语言设为英文，进入无间炼狱并走近地上的现金触发磁铁自动拾取。 UpdateFlyingCashPickups 直接用中文字符串拼接累计现金并传给 DialogueBubblesManager.Show；没有经过 L10n.T、LocalizationHelper 或本地化 key。
- 影响：英文玩家每次吸附现金都会看到中文提示，不符合玩家可见文字的中英契约。
- 已核对保护：官方 DialogueBubblesManager 消费的是已经构造好的 text；调用处没有语言分支。；现金磁铁只在 infiniteHellMode 激活，故范围限定为无间炼狱，不泛指所有模式。；清理集合和累计窗口已存在，与本问题无关。
- 建议：在显示时用现有 L10n.T 或 LocalizationHelper 解析中英模板，保留数字格式、颜色和累计窗口。
- 验证：L1：逐方法审查与实际调用链；未运行 Unity。后续 owner 在英文无间炼狱拾取现金，应看到英文金额提示，切回中文后下一次提示应为中文。

### CR-2026-09-22-041 · P2 / COMPAT · Mode E 击杀成长气泡在英文语言下仍固定显示中文

- 状态：Fixed（2026-09-22 复核）；证据：L1；初始工作区已在玩家击杀成长气泡取用时解析中英模板，百分比计算保持。L1。 L3 待 owner。
- 位置：`ModeE/ModeEBattle_ScalingAndRuntime.cs:327–331`（60bb84b）；`ModeE/ModeEBattle_ScalingAndRuntime.cs:587–594`（60bb84b）
- 触发与根因：英文语言下参加 Mode E，由玩家补刀一个敌对阵营 Boss。 OnModeEEnemyDeath 调用 ShowModeEPlayerGrowthBubble，后者直接拼接中文“生命/伤害+0.1%，总加成”并显示，没有解析当前语言。
- 影响：击杀成长的即时玩法反馈对英文玩家未本地化；同一路径的贝壳奖励有 L10n.T，不会修正这个独立气泡。
- 已核对保护：调用点核对 killedByPlayer 与敌方阵营，仅对满足条件的击杀触发。；同文件贝壳提示双语不覆盖成长字符串。；数值增长和存档不受此文本问题影响。
- 建议：沿用同文件贝壳提示的 L10n.T 模式，在显示时构造中英成长模板。
- 验证：L1 静态完整调用链，未运行 Unity。owner 在英文 Mode E 补刀敌方 Boss，确认成长信息及累计百分比为英文；切中文再击杀应恢复中文。

### CR-2026-09-22-042 · P2 / COMPAT · Mode F 工事部署和维修在暂停或官方界面打开时仍消费鼠标点击

- 状态：Fixed（2026-09-22 复核）；证据：L1；初始工作区已有 overlay/暂停门；本轮补官方 InputManager.InputActived，禁止交互占用期间读鼠标。两入口保持原选择/退款语义。L1。 L3 待 owner。
- 位置：`ModeF/ModeFFortifications.cs:334–340`（60bb84b）；`ModeF/ModeFFortifications.cs:393–402`（60bb84b）；`ModeF/ModeFFortifications.cs:438–445`（60bb84b）；`ModeF/ModeFPhases.cs:142–146`（60bb84b）
- 触发与根因：Mode F 使用工事包或维修喷剂进入鼠标选择状态，然后按 Esc 打开暂停菜单或打开背包/商店，在界面内左键或右键操作。 TickModeF 每帧直接调用 UpdateFortPlacementMode/UpdateModeFRepairSelection；两者只检查选择状态并使用 UnityEngine.Input，没有暂停、官方 UI 或交互占用门。宿主 SceneRuntimeGate 只判断地图与加载状态，timeScale=0 不会停止这些鼠标分支。
- 影响：点击菜单可以确认部署/维修或取消并退款，造成玩家未在场景中确认的动作；鼠标滚轮也可以在操作界面时旋转场景预览。
- 已核对保护：Mode F 雷达的 IsModeFBountyRadarSuppressedByOverlay 有 HUD/暂停门，但仅包住雷达，不包住后面的工事输入。；部署确认的玩家距离/碰撞检查不检查界面状态，维修确认只检查目标与距离。；丧尸入口另有 IsZombieModeRuntimePaused 早返；本条明确限定 Mode F，未把丧尸暂停路径一并认定。
- 建议：复用仓库统一的暂停/官方界面/输入占用判据，在两个交互 update 的输入读取前早返；恢复后保留原选择，取消退款沿现有入口。
- 验证：L1：ModBehaviour.Update→SceneRuntimeGate→ModeRuntimeHooks→TickModeF→两个输入方法全链；未运行 Unity。owner：进入选择后打开 Esc/背包，点击、滚轮、右键不应改变场景选择或物品数；关闭界面后确认/取消各一次。

### CR-2026-09-22-043 · P2 / COMPAT · 工事资源缺失时空的模型后备被当成成功，几何后备永远不进入

- 状态：Fixed（2026-09-22 复核）；证据：L1；初始工作区已在预览与直接落地识别无 Renderer 模型空壳并销毁，进入已有可见 primitive 后备。L1。 L3 待 owner。
- 位置：`ModeF/ModeFFortifications.cs:283–288`（60bb84b）；`Utilities/EntityModelFactory.cs:425–430`（60bb84b）；`ModeF/ModeFFortifications_RuntimePlacement.cs:35–53`（60bb84b）
- 触发与根因：Mode F 或丧尸模式部署工事时，entity bundle 未安装、加载失败，或预制体名在 bundle 中缺失。 EntityModelFactory.Create 对未初始化/找不到 prefab 返回只有 Transform 的非空 GameObject。两个工事入口仅在返回 null 时调用 CreateFallbackModeFFortification，因此绕过已有可见几何后备，继续为无 Renderer 的对象配置 Health 和碰撞体并返回成功。
- 影响：预览和已部署工事没有可见网格，却会消耗部署物品、占用工事名额并产生不可见障碍。不是对正常随包资产损坏的推断，范围只包括明确的资源缺失分支。
- 已核对保护：EnsureModeFFortificationRenderersVisible 只遍历已有 Renderer 并启用，不会补出网格。；GetModeFFortificationLocalBounds 能使用 FortDef 固定尺寸，因此空对象并不会阻止碰撞体创建。；CreateFallbackModeFFortification 确实提供了各类型 primitive 几何，但两个调用点的 null 判据与工厂返回约定不一致。
- 建议：在工事入口核对预制体可用性或有效 Renderer，再选择已有几何后备；无有效模型/后备时走现有失败退款路径，避免改变其他使用工厂的调用者契约。
- 验证：L1：工厂返回契约、两处调用者、几何/物理配置全链。未修改本地 bundle、未实机。后续用隔离 bundle 缺失环境部署三类工事，预览/落地必须可见，或明确失败且物品归还。

### CR-2026-09-22-044 · P2 / COMPAT · Mode F 补位 Boss 缓存已翻译名称，切语言后血条仍保留旧名称

- 状态：Fixed（2026-09-22 复核）；证据：L1；初始工作区已将 preset name key 写入 marker，显示时 L10n.T 解析当前语言；旧 DisplayName 字段仍可兼容没有 key 的调用者。L1。 L3 待 owner。
- 位置：`ModeF/ModeFRespawn.cs:878–881`（60bb84b）；`ModeF/ModeFUI.cs:445–476`（60bb84b）；`ModeF/ModeFUI.cs:495–508`（60bb84b）
- 触发与根因：Mode F 生成补位 Boss 后在游戏设置切换中英语言，观察该存活 Boss 的血条、榜首或吞噬公告名称。 补位时将 spawnedPreset.displayName 的已解析字符串写入 ModeFBossDisplayNameMarker；GetModeFActorDisplayName 优先直接返回 marker。语言切换只增加 HP 文本版本并重建后缀，没有重算 marker，因此重建仍取旧名称。
- 影响：同一条血条可以出现旧语言 Boss 名与新语言悬赏后缀，后续含该角色名的广播也沿用旧语言。
- 已核对保护：SyncModeFHealthBarNameLanguageState 确实让 UI 缓存失效，但不修改名字 marker。；actor.characterPreset.DisplayName 是后备分支，非空 marker 会阻止其取当前语言。；只在补位调用 SetModeFBossDisplayName，故没有声称所有初始 Boss 都被此分支影响。
- 建议：marker 保存稳定 preset key 或双语身份，在显示时解析；或者语言变化时按身份重新计算 marker，避免仅重新缓存已翻译文本。
- 验证：L1 全赋值/优先查询/语言失效路径，无 L3。owner：中文开局待补位 Boss 出现，切英文并等待血条刷新；名称与 Bounty 后缀应同时变为英文，切回同理。

### CR-2026-09-22-045 · P2 / COMPAT · 成就治疗监听只绑定初始化时的主角，后续出击可漏记铁人挑战治疗

- 状态：Fixed（2026-09-22 复核）；证据：L1；每次官方关卡初始化重绑主角 Health，先退订旧对象，再绑定新对象并重置血量基准。 L3 待 owner。
- 位置：`Achievement/AchievementTriggers.cs:679–693`（60bb84b）；`Achievement/AchievementTriggers.cs:709–713`（60bb84b）；`Achievement/AchievementTriggers.cs:214–221`（60bb84b）
- 触发与根因：Mod 初始化时 CharacterMainControl.Main 尚不存在，或者订阅后读档/出击重建主角；随后在无间炼狱使用治疗并达到第 10 波。 SubscribeAchievementEvents 只由一次 InitializeAchievementSystem 调用，且只绑定当时 player.Health；BeginAchievementSession 只快照 lastPlayerHealth，不重绑定。之后主角没有 OnHealthChange listener，HasUsedHealItem 维持 false。退订也重新读取当前主角，不能移除原 Health 上的 listener。
- 影响：用过治疗仍可能解锁“铁人挑战”，原主角 Health 仍存活时还保留旧 owner 回调；不能仅凭读取全局伤害事件证明治疗跟踪有效。
- 已核对保护：全仓搜索 SubscribeAchievementEvents 唯一生产调用为 InitializeAchievementSystem，achievementSystemInitialized 阻止重复初始化。；OnPlayerHurtForAchievement 是静态 Health.OnHurt，仅记录受伤，不覆盖治疗路径。；BeginAchievementSession 重置/记录 lastPlayerHealth，但没有监听新 Health。；隔离正常绑定对照可以检测相同生命增加，确认问题来自绑定窗口。
- 建议：保存实际订阅的 Health owner，随主角创建/替换重绑并对称解绑；沿既有生命周期事件接入，避免每帧扫描。后续单独核对“使用治疗物品”与普通回血的判据。
- 验证：L2：Build/audit-2026-09-22-terra-continuation/achievement-heal-probe/run.py 逐字抽取生产订阅/退订/回血回调/第10波判定，在 .NET 10 上验证 5 个观察：初始化无主角漏记、误解锁、已绑定正对照、新主角漏记、错对象退订。事件调度与玩家生命周期是替身，无 Unity/L3。owner：正常启动读档后进入无间炼狱，受伤后用药，完成10波不应得到铁人挑战；另一局不使用治疗再确认。

### CR-2026-09-22-046 · P2 / COMPAT · 成就界面和弹窗常驻对象没有宿主卸载清理，旧 UI 与输入占用可保留

- 状态：Fixed（2026-09-22 复核）；证据：L1；成就模块退出关闭并销毁 AchievementView 与 SteamAchievementPopup，条目 Cleanup 和实例引用同步释放。 L3 待 owner。
- 位置：`Achievement/AchievementRuntimeHooks.cs:41–47`（60bb84b）；`Achievement/AchievementView.cs:104–143`（60bb84b）；`Achievement/SteamAchievementPopup.cs:146–153`（60bb84b）；`ModBehaviour.cs:777–780`（60bb84b）
- 触发与根因：加载 Mod 后关闭/卸载该 Mod；若成就面板正打开，其 DisableInput owner 与画布仍存在。重新启用时旧类型的 singleton 也可能被复用。 两个 EnsureInstance 都创建独立 DontDestroyOnLoad GameObject。CleanupAchievementRuntime 只退订事件与清击杀记录，module OnDestroy 只丢 owner；宿主清 manager/icon cache 时没有 Close/Destroy 这两个 UI。AchievementView.OnDestroy 本身也只清 singleton，不归还打开面板的输入占用。
- 影响：卸载后的成就面板/弹窗继续存在并保留旧回调、条目和输入占用；图标和共享皮肤缓存已被清理，重载 UI 可能引用已销毁资源。未声称每次重载都会堆积新对象。
- 已核对保护：所有 AchievementView/SteamAchievementPopup 引用与宿主/module cleanup 已交叉搜索，没有 Destroy 或静态 Cleanup 入口。；用户手动 Close 可以归还 InputManager 占用，但卸载路径未调用。；两个 UI 自身 OnDestroy 有部分资源清理，但其常驻 GameObject 没有被宿主销毁。；图标清理本身具有资源所有权保护，问题是面板仍存活。
- 建议：成就 runtime 的唯一 cleanup owner 显式 Close 后销毁两个常驻 UI，并在 View.OnDestroy 兜底归还输入；复用旧实例前应由清理保证无残留。
- 验证：L1：完整 Awake→EnsureInstance→打开输入占用→宿主卸载路径；未进行 Mod 热卸载实测。owner：开成就页后关闭 Mod，确认画布和提示对象消失、人物输入恢复；重新启用后图标正常，重复三次对象数不增长。

### CR-2026-09-22-048 · P2 / COMPAT · 天空岛头目配装演练超时或取消后丢弃生成任务，迟到角色没有回收入口

- 状态：Fixed（2026-09-22 复核）；证据：L1；演练生成保留 Task；超时/取消后由异步回收者等待迟到角色并销毁角色和专属 preset。 L3 待 owner。
- 位置：`DebugAndTools/F3GameplayValidationSkyIslandDrill.cs:192–208`（60bb84b）；`DebugAndTools/F3GameplayValidationSkyIslandDrill.cs:270–274`（60bb84b）
- 触发与根因：仅 Dev 构建：专用测试槽运行天空岛头目配装演练，CreateCharacterAsync 超过 8 秒，或在完成之前取消/结束会话，随后官方创建任务完成。 演练仅轮询局部 awaiter 到截止时间；未完成时记录 spawn_timeout 并离开，不再 GetResult，也未安装迟到结果清理。finally 只销毁已赋值的 created 和克隆 preset，created 在超时/取消时仍为 null，不能回收随后完成的角色。
- 影响：演练结束后可能留下无演练 owner 的测试角色或在途创建异常，干扰后续测试与当前岛上会话。范围限定 Dev 演练，不影响正式构建可达代码。
- 已核对保护：成功创建路径最终有 SetActive(false)+Destroy(created.gameObject)，该保护不能覆盖未取回的任务结果。；clone 的销毁只处理 CharacterRandomPreset 对象，不等同于取消官方创建任务或销毁其输出。；Execution.CompleteSession 对岛内演练跳过 ValidationSafeCleanup，也没有按请求持有此 awaiter。；固定场景/测试档开跑门已经存在，问题是已授权演练内部的异步收尾。
- 建议：复用现有带 owner 的迟到结果回收模式：让任务完成端持有请求状态，取消/超时只撤销提交权，随后成功产生的角色必须立即回收，并完整观察异常。
- 验证：L1：演练入口→生成轮询→finally→CompleteSession；未运行实机。后续隔离注入可控生成延迟，覆盖成功、8秒超时后成功、取消后成功，断言迟到角色不存活。owner 使用 Dev 专用槽运行 SKY_DRILL_BOSS_LOADOUT 后查看报告 spawn_timeout 与场上残留；实机不易稳定制造延迟，不能据普通一次通过证伪。

### CR-2026-09-22-004 · P2 / COMPAT · Boss 池因子编辑页关闭重开后，工具栏模式与列表内容不一致

- 状态：Fixed（2026-09-22 复核）；证据：L1；Boss 池重开按当前编辑模式重建列表，同时清旧因子选择器映射。 L3 待 owner。
- 位置：`BossFilter/BossFilter.cs:385–410`（60bb84b）；`BossFilter/BossFilter.cs:544–579`（60bb84b）；`BossFilter/BossFilter.cs:1118–1129`（60bb84b）
- 触发与根因：Ctrl+F10 打开 Boss 池，进入无间炼狱因子页后直接点 X、保存并关闭或 Ctrl+F10，再次打开。 因子页已销毁 Toggle 并清空 bossToggles；OpenBossPoolWindow 复用画布时把 isInfiniteHellFactorMode 设回 false，只更新工具栏并调用 RefreshBossPoolUI，没有重建普通列表。
- 影响：重开后仍显示因子选择行，工具栏却显示全选/全不选；这些按钮修改的是看不见的启用状态。玩家需要再进因子页并点返回，才能恢复可见 Toggle。
- 已核对保护：CloseBossPoolWindow 只失活画布，不销毁内容；三种关闭路径均可达。；ExitInfiniteHellFactorMode 才调用 PopulateBossList；重开分支未调用。；ResetBossPoolFilterStateForEnemyPresetRefresh 会销毁画布，但正常关闭重开不会刷新预设。
- 建议：复用画布打开时通过既有退出因子模式流程重建 Toggle 列表，或保留并正确恢复原编辑模式；统一模式标志、工具栏和内容。
- 验证：L1 源码状态链已确认，未运行 Unity。owner 验收：因子页分别通过 X、保存关闭、快捷键退出，再打开应展示 Toggle；全选/全不选的可见状态与统计一致。

### CR-2026-09-22-005 · P2 / COMPAT · 在线 Wiki 切换语言后，顶栏联想继续使用旧语言索引

- 状态：Fixed（2026-09-22 复核）；证据：L1；Wiki 联想按语言变更清索引并递增代次，迟到的旧语言加载不能覆盖当前索引。 L3 待 owner。
- 位置：`wiki-site/docs/.vitepress/theme/components/WikiHeadSearch.vue:53–71`（60bb84b）；`wiki-site/docs/.vitepress/theme/components/WikiNetbar.vue:40–43`（60bb84b）；`wiki-site/docs/.vitepress/theme/components/WikiHead.vue:153–155`（60bb84b）
- 触发与根因：先在中文顶栏搜索框触发索引加载，再点 English 进行站内语言切换，并再次输入查询；反向切换同理。 ensureIndex 只要 index 或 loaderPromise 存在就返回，没有按 localeIndex 失效。语言按钮走 VitePress 客户端路由，Theme.Layout 与 WikiHeadSearch 均未以语言为 key，实例持续存在。
- 影响：英文页的联想结果仍来自中文索引并含中文页面 URL，可能把用户带回中文页；中文专有词或英文名的命中也与当前语言不一致。
- 已核对保护：searchIndex.ts 的共享缓存按 locale/loaders 区分，但组件的提前返回使它无法被再次调用。；WikiSearchBox 完整弹层用 computedAsync 跟踪 localeIndex，故该问题仅限顶栏联想。；安装的 VitePress 1.6.4 app/index.js:50 返回 h(Theme.Layout)，router.js 同源链接拦截走 go(href)，不会整页重载。
- 建议：将联想索引绑定当前 locale；语言变化时取消旧请求的提交资格并重置索引/查询结果，再通过现有 loadSearchIndex 装载对应语言。
- 验证：内存中提取生产 ensureIndex，仅将动态 import 边界替换为确定性 fixture，使用已安装 esbuild 去除 TS 后执行：activeLocale=en、indexLocale=root、loadCalls=[root]。L2 证明缓存不失效；没有浏览器/L3 操作。

### CR-2026-09-22-006 · P2 / COMPAT · 在线 Wiki 联想索引首次加载失败后，同页会话内不能重试

- 状态：Fixed（2026-09-22 复核）；证据：L1；联想加载成功或失败都在 finally 释放 loaderPromise，失败后下一次输入可以重新请求。 L3 待 owner。
- 位置：`wiki-site/docs/.vitepress/theme/components/WikiHeadSearch.vue:57–71`（60bb84b）
- 触发与根因：首次联想索引 import 或 loadSearchIndex 失败，网络/资源随后恢复，用户重新聚焦输入或输入新词。 catch 将 index 清空，但 finally 只清 loading；已完成的 loaderPromise 一直保留，后续 ensureIndex 因 loaderPromise 为真直接返回，不再发起加载。
- 影响：顶栏联想持续无结果，加载提示已消失，刷新页面才能恢复；该失败不会破坏完整搜索弹层的独立加载路径。
- 已核对保护：searchIndex.ts 在失败时清共享 cache，无法清除 WikiHeadSearch 自己持有的 Promise。；query 的 120 ms debounce 与 focus 均调用同一个 ensureIndex，没有其他重置入口。
- 建议：请求结束后只清除属于该请求的 loaderPromise，失败保持可重试；同时与语言变更的请求 generation 对齐，避免旧 finally 清掉新请求。
- 验证：同一个原函数隔离探针先拒绝一次 import，第二次恢复 fixture 后调用 ensureIndex；实际 attempts=1、index=null、loading=false，第二次未尝试。未发出任何网络请求。

### CR-2026-09-22-007 · P2 / COMPAT · 返回方块直接读取 E 键，会绕过官方界面和暂停输入门

- 状态：Fixed（2026-09-22 复核）；证据：L1；初始工作区已把返回组件抽至独立文件并在 E 键读取前检查暂停、官方 HUD 和 InputActived。L1。 L3 待 owner。
- 位置：`Interactables/BossRushLootboxInteractables.cs:1037–1047`（60bb84b）；`ModBehaviour.cs:1718–1765`（60bb84b）
- 触发与根因：标准挑战胜利后先走入返回方块触发范围，随后打开背包或暂停菜单并按 E。 BossRushReturnInteractable.Update 只检查 playerNear 和 UnityEngine.Input.GetKeyDown(E)，未查询官方输入可用性、View 或暂停。ReturnToBossRushStart 也直接 SetPosition；Time.timeScale=0 不阻止 Update 与原始按键读取。
- 影响：在官方界面或暂停中仍可被传回挑战起点，并使返回方块失活，消耗此次返回交互。
- 已核对保护：官方 InputManager.DisableInput 只维护输入源集合；它不屏蔽 UnityEngine.Input。；官方 TimeScaleManager 仅将暂停的 timeScale 设为 0。；宿主 ReturnToBossRushStart 的零起点 fallback 只避免无效坐标，不能阻止暂停中操作。；与既有 ROOT-02 的胜利续体跨场景创建问题独立；本条仅论正常场景中的输入穿透。
- 建议：返回入口复用 InteractableBase/官方交互输入，或在读取 E 之前同时遵守现有官方界面、输入与暂停门；只有实际完成返回才失活。
- 验证：L1 已核入口、宿主与官方输入/时间代码；未实机。owner 验收：进入返回触发范围后打开背包/暂停菜单，按 E 不移动、不消耗方块；关闭界面后正常 E 返回。

### CR-2026-09-22-008 · P2 / COMPAT · 在线 Wiki 更新日志门户的折叠按钮未控制实际显隐

- 状态：Fixed（2026-09-22 复核）；证据：L1；更新日志门户将折叠状态绑定到实际控制内容显隐的类名。 L3 待 owner。
- 位置：`wiki-site/docs/.vitepress/theme/components/WikiPanel.vue:56–70`（60bb84b）；`wiki-site/docs/.vitepress/theme/components/WikiPanel.vue:175–185`（60bb84b）；`wiki-site/docs/.vitepress/theme/css/layout.css:298–306`（60bb84b）
- 触发与根因：宽于 1366 px 时在非日志页点左栏更新日志标题，或在日志页尝试折叠该门户。 点击调用 toggle(changelog) 写 overrides，但更新日志模板的 collapsed 只绑定 !changelogHere，没有读取 collapsed(changelog) 或 overrides。
- 影响：非日志页始终折叠，点标题不能展开最近版本入口；日志页始终展开，无法收起。901–1366 px 的后置 collapsed 规则也覆盖 hover/focus；小于等于 900 px 的统一展开样式避开本问题。
- 已核对保护：普通类目门户使用 collapsed(id)，证明通用逻辑本身接线正确。；读取 responsive.css 的 hover/focus 与 collapsed 覆盖顺序，未把手机菜单算作受影响。；其他更新日志链接仍能导航，故不是全站日志不可达。
- 建议：更新日志门户复用与普通类目相同的 collapsed 判定，并保留当前日志类目默认展开；同步其持久偏好读取。
- 验证：L1 的模板依赖与 CSS 显隐链已确认；未做像素/浏览器验收。验收应覆盖宽屏非日志页展开、日志页折叠及 901–1366 px 横条模式。

### CR-2026-09-22-009 · P2 / COMPAT · 在线 Wiki 站内跳页后，页脚保留首次页面的最后编辑时间

- 状态：Fixed（2026-09-22 复核）；证据：L1；页脚挂载后监听页面更新时间与语言，路由切换重算；无时间戳页面清空旧值。 L3 待 owner。
- 位置：`wiki-site/docs/.vitepress/theme/components/WikiFooter.vue:30–40`（60bb84b）；`wiki-site/docs/.vitepress/theme/Layout.vue:125–130`（60bb84b）
- 触发与根因：从首页或任意已显示最后编辑时间的页面，通过站内导航进入更新时间不同的页面，或切换语言。 formatted 是普通 ref，日期只在 onMounted 回调中根据 page.lastUpdated/lang 计算一次；持久 Layout 中的 WikiFooter 不会因 route/page 改变重新挂载，也没有 watch。
- 影响：新页页脚误标为首个页面的更新时间；首次页没有时间时，后续有时间页面也不显示。切语言后日期格式仍可能保留旧语言。
- 已核对保护：VitePress app/index.js:50 的 Layout 实例复用；theme Layout 直接渲染 WikiFooter，无 route key。；git log 实际不同：wiki-site/docs/index.md 为 2026-09-06T00:27:36+08:00，wiki-site/docs/bosses/dragon-king.md 为 2026-09-17T09:31:38+08:00。；动态 ui.lastEdited 文案和主题 footerText 的 computed 不会重算 formatted。
- 建议：保留客户端挂载后格式化以避免时区 hydration 差异，并在挂载后侦听当前页时间戳及语言；无时间的新页必须清空 formatted。
- 验证：L1 源码与真实两页 git 时间已核，未浏览器复现。验收：首页进入龙王页，页脚日期应随页改变；中英切换日期格式同步；无 lastUpdated 页应隐藏旧值。

### CR-2026-09-22-047 · P2 / COMPAT · 龙铳冰刃爆发的 Cold 附加效果无阵营过滤

- 状态：Fixed（2026-09-22 复核）；证据：L1；L1：ApplyDeathBuff 复用 IsTraceReceiverUsable，检查真实 caster 与 Team.IsEnemy，同次半径内友军和本人被排除。 L3 待 owner。
- 位置：`Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs:184–207`（60bb84b）；`Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs:2115–2141`（60bb84b）；`Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs:2237–2263`（60bb84b）
- 触发与根因：玩家使用装填冰刃弹（TypeID 1303）的焚天龙铳，弹体在施放者或友军 1.25 米内命中目标或障碍而结束。 HandleDeath 为冰刃的主投射物在 ExplosionRange > 0 时调用 ApplyDeathBuff(Cold)。该方法遍历半径内所有 DamageReceiver 并直接 AddBuff，没有像 HandleDamageReceiverHit、ApplyRadiusDamage 或 GroundZone 那样检查 sourceContext.team、realFromCharacter 或 Team.IsEnemy。
- 影响：冰刃的爆发附加减速可施加给玩家本人和同队单位；伤害部分仍走独立过滤，因而这是状态效果的友伤漏洞，不与 INT-19/INT-20 的熔浆/共享元素列表重复。
- 已核对保护：HandleDamageReceiverHit:1917-1931 排除同队和真实施放者。；GroundZone.TickZone:325-334 也排除同队和真实施放者。；ApplyDeathBuff:2244-2262 只做 receiver 去重，未做任一阵营或施放者检查。；IceBlade profile:184-207 配置 Element=ice、ExplosionRange=1.25、ExplosionDamageFactor=0.55，满足主投射物死亡爆发路径。
- 建议：复用 DragonKingBossGunRuntime 的统一敌对判据，在 ApplyDeathBuff 中跳过 source team、realFromCharacter 与非敌对 receiver；把 buff 的筛选与同一次半径伤害使用同一份规则。
- 验证：L1：逐行追踪冰刃 profile → HandleDeath → ApplyDeathBuff。L2 建议让 player 与同队单位站在冰刃爆点内，断言他们未获得 Cold，而敌方仍获得；未启动游戏。
<!-- END FULL AUDIT 2026-09-21 -->

## 2026-09-20 人工实测第二轮补漏（COMPAT）

均为 Fixed（L1/L2），L3 待 owner；逐项证据与 M20-01–08 操作见 `docs/reports/testing/20260922人工实测复核修复记录.md`。

| ID | 级别 | 触发条件与影响 | 修复与证据 |
| --- | --- | --- | --- |
| CR-2026-09-20-006 | P1 | 基地/局内随从创建中更换或取消席位，最后 await 后的旧请求仍可激活，旧 finally 还可能清除替换请求的标记。 | BaseIdleSpawner、CompanionRuntime 与 Service 统一取消代数、席位复验和标记所有权；真实生命周期代码的受控异步回归及反向探针通过。 |
| CR-2026-09-20-007 | P2 | 孵化“跳过”直接关闭，略过完整结果和异色音乐；暂停时实时动画继续，详情框过小。 | HatchRevealView 跳过先完整揭晓、再次点击关闭，音效幂等、暂停停表、扩大详情区；生产方法执行回归通过，排版待 L3。 |
| CR-2026-09-20-008 | P1 | Mode H 派生用刷怪点均值作为斗士落点，均值不保证在地面；无离场配置时曾回退地下隔离点。 | 五个已有实点分给斗士与四个对手，看台另选，退出安全回落看台；九图真实 JSON 回归及恢复均值的反向探针通过，物理连通性待 L3。 |
| CR-2026-09-20-009 | P2 | 日报固定卡片裁掉长正文，面板刷新判据漏收入/支出变化。 | 卡片内 ScrollRect 保存全文，TMP 量高；金额变化纳入原有限频刷新。编译与现有日报回归通过，真实字体/滚轮待 L3。 |
| CR-2026-09-20-010 | P2 | 图鉴先取主场景丢失实际子场景；未就绪解析结果缓存 null，使就绪后仍无法显示名字。 | 复用 MapPointSceneResolver，仅缓存成功解析；实际子场景、重试和切语言回归通过，旧档不推测回填。 |
| CR-2026-09-20-011 | P2 | 套装以固定 Teams.player 判断敌友，Mode E 玩家换阵营后可能对友军附伤或漏选目标。 | 命中与扫描都用玩家当前阵营的 Team.IsEnemy；同队/中立/敌队/随从执行判据和两项接线反向探针通过。 |
| CR-2026-09-20-012 | P2 | 异色名只有星号而无明确前缀；静止随从 HUD 的名字缓存不随语言切换。 | Chroma 增加中英异色前缀，HUD 模型和名字缓存纳入语言；前缀/克隆执行断言及相关守卫通过，效果观感待 L3。 |

## 2026-09-20 资源生产化续作（COMPAT / OPERATIONAL）

| ID | 级别 | 已确认问题 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-20-003 | P1 / OPERATIONAL | 发布脚本只验证清单内文件，游戏目录历史 sky_island_world 等未知 bundle 可静默残留。 | Fixed（L1/L2）：复制前/后检查整个目标，伪装后缀与 Assets 外资源也拒绝。历史包先 SHA-256 备份再移除；隔离部署反例证明合法目标文件不会被提前覆盖。 |
| CR-2026-09-20-004 | P1 / COMPAT | non-readable PNG 只释放 CPU 副本，GPU RGBA32 常驻仍在；蛋糕/船票实际进包 1024。 | Fixed（L1/L2）：生产压缩图标包按旧路径缓存 Sprite，328 个实际 BC7；旧两图 256 BC7，可读性关闭，缺包/Dev fallback 和失败清理保留。UnityPy 与 Unity Item/Sprite 实读通过，视觉清晰度待 owner L3。 |
| CR-2026-09-20-005 | P1 / COMPAT | 装备/物品目录与天空岛预载同步读取，原异步外壳仍阻塞且缺少统一取消/失败观测。 | Fixed（L1/L2）：异步加载、逐包让帧、场景重试、迟到释放；兼容同步查询可抢先接管请求，仍存在需要 L3 采样的同步成本。F3 只读 10 秒取数与纯判据分离。无真实游戏帧耗结论。 |

### CR-2026-09-20-013 · P2 / COMPAT / OPERATIONAL · 基地四建筑只有程序化占位模型

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignBoardBuilder.cs`、`Integration/BackMountain/ShowcaseBuildingBuilder.cs`、`Common/Buildings/BuildingModelHelper.cs`。
- 原因与修复：公告栏、展示柜没有正式模型资源，报箱和遗种巢只保留旧占位/旧资源路径。锁定四个 GLB 与 SHA-256 映射，统一导入为地面原点单网格，公告栏/展示柜通过既有异步预载加载，缺包仍走原程序化 fallback；bundle 租约只在实例化成功后转交，失败和销毁路径释放。
- 资源判据：四包合计 5,868,039 B；每包一张 1024×1024 BC7、关闭 Read/Write/Crunch，三角面 7,337–9,731；共享 `BossRush/SkyIsland/Environment` 含 `UniversalGBuffer`。未生成 LODGroup。
- 验证：L1/L2：四建筑属性测试含反例、72 包 UnityPy、全量守卫 646 PASS、隔离回归 51 PASS、Windows 正式编译与部署哈希一致。真实基地落点、遮挡、交互距离和观感待 owner L3。

证据、实际变更清单、资源包体与理论内存口径、全部命令及回退路径：`docs/reports/testing/20260920_资源生产化续作交付.md`。本轮不做 LOD 或远景替换，原有其它会话改动保留。

## 2026-09-20 Unity 资源交付与运行时所有权（COMPAT / OPERATIONAL）

| ID | 级别 | 触发条件与影响 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-20-002 | P1 / COMPAT | 便携/物品/地图展示图的 PNG 解码默认保留 CPU 像素副本；失败分支和地图缓存未完整释放自造对象；实际发布包使用 LZMA/Crunch，高面数能量盾和未压缩天空岛环境纹理带来不必要的加载、GPU 内存和帧耗成本；构建部署遗漏五个资源包。 | Fixed：图像加载使用 non-readable + owner 清理；能量盾 40,000 三角形；天空岛 1024 BC7；68 包 LZ4、无 Crunch；Mode G 徽记恢复专用构建器 256×256 合同、包体 199,571 B；发布清单和 SHA-256 门禁补齐遗漏包。L2 见 `docs/reports/testing/20260920_资源生产化续作交付.md` 与 `Build/resource-optimization-20260920/`；L3 帧耗、观感和玩法仍待 owner 实机。 |

## 2026-09-20 词缀选物 UI 覆盖（COMPAT）

| ID | 级别 | 触发条件与影响 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-20-001 | P1 / COMPAT | `ReforgeUIManager_AffixForge.AffixForge_HandleSelectionChanged` 提前返回，跳过普通重铸的延迟复位且漏清原版提示；官方 `ItemDecomposeView.Setup` 后执行时，会按分解配方隐藏合法词缀装备的按钮。共享 `UpdateReforgeButtonInteractable` 又用普通重铸成本覆盖词缀可用性。 | Fixed（L1/L2）：合并下一帧刷新、修复提示、共享模式分流，关闭/切模式/销毁与清理门禁保留。69 项 UI 执行断言、166 项相关守卫、6 个反向探针通过，正式编译部署哈希一致；L3 待重启复测。见 `docs/reports/testing/20260922人工实测复核修复记录.md`。 |

## 2026-09-19 奖励池可靠性复核（COMPAT）

| ID | 级别 | 触发条件与影响 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-19-016 | P1 / COMPAT | `ModeHRewardItemPool` 直接消费官方 `GetAllTypeIds` 的未排序结果，合法候选枚举顺序变化会让同一 `(runSeed, txId, slot)` 重放出不同奖励；`TryInstantiate` 未先检查 prefab，官方缺资源时返回的同 TypeID 空壳可能进入 escrow journal。前者破坏崩溃重放确定性，后者会造成奖励收据看似成功但无法真实交付。 | Fixed：Mode H 与日报、天灾远征统一复用 `BossRushQualityItemPool` 的排序/黑名单/非空缓存；实例化前增加 `Instance + GetPrefab` 门禁及失败原因。`RewardPoolReliability` 从 247 项 / 65 失败恢复为 247 PASS，changed-only 39 PASS、全量 628 PASS；未改经济、存档字段或奖励品质。L3 仍需真实 Mode H 结算与资源缺席场景确认。 |

## 2026-09-19 Wiki 内容与公开部署复核（SAFE / OPERATIONAL）

来源：owner 要求全面核对 Wiki 与最新代码，随后授权修复并提交本地 commit。文案问题已按现有代码修正，公开部署仍待推送授权；验证证据见 `FIX_TRACKER.md` 同日 Wiki 小节。

| ID | 级别 / 分类 | 已确认问题与影响 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-19-013 | P2 / SAFE | 中文消耗品、NPC 物品和护士页把安神滴剂写成负面 Buff 全清，并推荐解除幽灵女巫诅咒；实际 `CalmingDropsUsage` 要求 `NurseHealingService.HasDebuffs`，可治疗名单不含诅咒 ID 500043，仅有该状态时无法使用。 | Fixed：三组中英正文同步限定清除范围与使用条件，使用实际本地化名 Ghost Curse，生成页与构建后 HTML 已核对（L1/L2）。未扩展药品或治疗行为。 |
| CR-2026-09-19-014 | P2 / SAFE | `infobox.mts` 的护士服务中英文都列出复活、野战诊所，普通护士交互与治疗代码没有这些服务；正文背景中的诊所不是独立服务入口。 | Fixed：速查框改为恢复满血、清除可治疗的负面状态；正文同时修正 Lv.6 的 75 折笔误为 7.5 折。双语渲染与 HTTP 页面检查通过（L1/L2）。 |
| CR-2026-09-19-015 | P2 / OPERATIONAL | 公开 main 与成功 Pages 部署仍为 2026-09-08 的 4b1b5b68，天空岛双语/新装备页面 404，日报仍显示旧悬赏规则；本地 commit 不会更新公开网站。 | Open（待发布）：本地最新完整站点构建、237 页链接检查和 12 个关键页面 HTTP 检查通过；owner 本轮授权本地 commit，未授权 push，未发布。需推送经审核的提交后核对 Actions head_sha 和公开页面。 |

## 2026-09-19 Mode G 异常路径复核（COMPAT）

接续全面审核任务；四组问题修复完成，L1/L2 通过，L3 待 owner。详细证据、可玩闭环与实机操作见 `docs/reports/reviews/2026-09-19-ModeG异常路径复核.md`。

| ID | 级别 | 触发条件与影响 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-19-010 | P1 | `ModeGRewardStrictMaterializer.Update/CancelAndDestroy`：背包销毁后无限早返；交付回调重入取消提前清空快照或错算在途物品，奖励租约/结算保护不能可靠释放。 | Fixed：失效走取消；当前件先结算再取消剩余槽，保持每帧一件。实际发放器源码执行回归覆盖销毁、两种回调取消和一次完成；移除保护转红。 |
| CR-2026-09-19-011 | P1 | `ModeGRuntimeBridge.TrySelectModeGFormation`：贪心首选阻塞后续槽，即使存在合法双/三 Boss 组合也中止。 | Fixed：仅失败时在同一落地点集回溯，不放宽间距、不额外查物理。具体反例及 1200 组样本对照独立穷举通过，恢复贪心单次选点转红。 |
| CR-2026-09-19-012 | P1 | `ModeGRuntimeModule.AwaitSpawnAttemptWithTimeout`：工厂随暂停停止，15 秒技术预算仍按墙钟消耗，可误判整局生成失败。 | Fixed：暂停及恢复边界不计时，取消/迟到清理保留。时间预算执行回归、暂停接线守卫及两个反向探针通过；真实异步工厂待 L3。 |
| CR-2026-09-19-009 | P2 | `ModeGProfilePersistence.RecordRun`：Defeat 只累加败北记录，遗漏清零契约连胜。 | Fixed：同一终局事务清零，battleResultToken 去重、历史保留；败北后从 1 重新累计回归通过，移除赋值转红。 |


## 2026-09-19 NPC 对白与天空岛气泡复核（COMPAT / SAFE）

四项已确认缺陷已修复，L1/L2 通过，L3 待 owner。最初本地审查编号 001–004 与并行日报审查冲突，入库统一为 005–008。

| ID | 级别 / 分类 | 触发条件与影响 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-19-005 | P2 / COMPAT | `SkyIslandEncounters.TickChatter` 在交战且事件已消费时仍选 Idle，敌人开火中念闲话；只看旧听声来源还会漏掉视觉/强制追踪。 | Fixed：共用当前目标/近期动静判定，交战禁止闲话；目标改变丢弃旧发现事件。真实 owner Tick 的交战、其他阵营、脱战与切目标回归覆盖。 |
| CR-2026-09-19-006 | P2 / COMPAT | 小兵发现和组内死亡在候选遍历时先消费，忙碌、冷却、距离、优先级或显示失败可永久吞掉事件。 | Fixed：有限待播进度，成功才消费，12 秒过期，事件短冷却不缩短后续闲话间隔。多候选争用、死亡优先、失败恢复和过期执行回归覆盖。 |
| CR-2026-09-19-007 | P2 / COMPAT | `SkyIslandBossVoice.OnHurt` 先消费血线旗标，开场冷却内连跨 60%/30% 后不再触发，整场丢掉受伤台词。 | Fixed：合并最新血线，Update 重试；死亡取消、重复绑定先退订。真实 BossVoice 联合调度器覆盖快/慢血线、冷却、失败、过期和销毁。 |
| CR-2026-09-19-008 | P2 / SAFE（验收） | F3 `JudgeChatter` 对缺 owner 的 -1/-1 计数仍 PASS；两个 Busy 布尔之和不会大于 2，不能验证同屏上限。 | Fixed：缺必要依赖 FAIL，无发送观测 SKIP，请求指标明确不证明像素。逐字抽取生产判据覆盖负值、缺入口、空名单与无/单侧观测。 |

文案另修羽织与叮当共 24 个双语句对，去除赠礼额度/按钮/系统和生硬抽象比喻，保留人物口气及原事件池。完整验证与实机操作见 `FIX_TRACKER.md` 同日 NPC 条目。

## 2026-09-19 鸭科夫日报入口、阅读与边界复核

本轮修复均为 COMPAT，L1/L2 已验证，L3 待 owner 实机。提交复核另补齐共享奖池 `Common/Loot` 在日报/远征既有验收项中的源码映射，覆盖守卫与移除映射的反向探针通过。完整范围、玩法矩阵、外部依赖边界和操作/看图清单见 `docs/reports/reviews/2026-09-19-鸭科夫日报生产复核.md`。

| ID | 级别 / 分类 | 已确认问题与触发条件 | 修复与证据 |
| --- | --- | --- | --- |
| CR-2026-09-19-001 | P1 / COMPAT | `DailyReportMailboxBuilder.InitDailyReportMailbox` 忽略数据注入失败仍置初始化完成；`InjectDailyReportBuildingData` 遇已存在的元数据提前退出，缺失的 prefab 不会补齐。依赖缺席/注册中断后可长期失去有效入口。 | Fixed：两表分别核对，身份/造价完整绑定，成功才置完成；恢复、重复调用与 Unity 销毁替换的生产方法执行回归通过。忽略失败门控/不补缺失 prefab/绕过 prefab 身份校验的探针分别触发守卫或执行回归失败。 |
| CR-2026-09-19-002 | P2 / COMPAT | `DailyReportUI.BuildLayout/LockFontSize` 用固定行高和 Truncate，不根据文本所需高度扩展；长正文会被裁剪，整张纸加滚动条也救不回文本框内部的截断。 | Fixed：共享 MeasureTextHeight，双栏取较高者、按实际高度推进下栏和滚动范围，容器适配且保留阅读位置；签到区固定上沿，长说明不再向上挤压按钮。生产排版方法长短文本/签到避让回归通过；真实 TMP 字形、最终像素待 L3。 |
| CR-2026-09-19-003 | P2 / COMPAT | 零死亡任务的已失败与未开始同为 0/1，UI 不区分永久失败；创刊号和次日都显示第 1 期。 | Fixed：展示复用失败/达成判据，死亡变化刷新，显示保持条件与下一期时间估算；首期专门引导、刊号按日报日递增。双语状态、结算与期号执行回归通过。 |
| CR-2026-09-19-004 | P2 / COMPAT | `DailyReportService.Report*` 的极端整数/货币累计回绕、伤害相加变 Infinity，可能让完成进度倒退或 JSON 不可回读。 | Fixed：累积统计饱和、伤害保持有限；负 delta 最小值有保护。边界/编解码回归通过；恢复 Kills++ 的反向探针命中预期失败。 |


## 2026-09-18 晴岚群岛生产复核：库存与奖励交付

来源：owner 要求全面审核并优化至生产水准。本轮确认的两项均已修复，证据为 L1/L2，待实机；完整范围矩阵、失败复现、构建边界和操作/看图清单见 `docs/reports/sky-island/2026-09-18-晴岚群岛生产复核与交付可靠性.md`。

| ID | 级别 / 分类 | 已确认问题 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-18-001 | P2 / COMPAT | 蛙卵取用的独立 `ConsumeFromPack` 在官方库存先改变、后通知且通知抛错时丢失云苔，未进入携带状态；未设置库存忙标志，通知重入还能重复扣料。 | **Fixed（L1/L2，待 L3）**。`ConsumeOne` 复用合成/点灯的预留事务，预留后复核会话，finally 归还未提交材料并清忙标志，删除重复扣料算法。真实 TakeSpawn 入口覆盖整堆/部分失败、重试、重复操作、返航/销毁与重入；旧实现和三项门控破坏均转红。 |
| CR-2026-09-18-002 | P1 / COMPAT | 头目抽中的专属装备未穿上时，`SkyIslandBossLoot.TryAddFresh` 直接 AddItem，满背包拒收后清理掉奖励；挂载后的通知抛错也会在 finally 销毁已送达装备。 | **Fixed（L1/L2，待 L3）**。复用 `InteractableLootboxInventoryHelper.TryAddExtraItem`，满箱扩一格、按实际归属认交付；外层仅清理未归属实例。覆盖满箱/挂载后异常/拒收/扩容失败/缺 prefab，绕过 helper 的变异转红。概率与官方尸体箱时序不变。 |



## 2026-09-17 天空岛导航优化后全面复审（023–025 已修复，待 L3）

来源：owner「再次全面审核一遍」。完整调用链、官方 DLL IL、隔离复现与 R-01～05 实机清单见 `docs/reports/sky-island/2026-09-18-天空岛复审三项修复与验收.md`。原轮只审查与登记，021 / 022 未发现回归。后续按 owner“全部修复”完成以下三项，L1 / L2 通过，待 L3；实现与验收见 `docs/reports/sky-island/2026-09-18-天空岛复审三项修复与验收.md`。

| ID | 级别 / 分类 | 原问题（修复前位置） | 状态、证据与建议 |
| --- | --- | --- | --- |
| CR-2026-09-17-025 | P2 / COMPAT | `SkyIslandChatter.cs:162` 的 Show 把官方 `DialogueBubblesManager.Show` 正常返回当成已显示；官方 manager 缺席或 prefab 缺失时是静默完成，仍被记入 SpokenCount 并消耗冷却。现有夹具只模拟同步抛错，不能覆盖该合同。 | **已修复（2026-09-18，L1 / L2，待 L3）**。发送前检查现存管理器；已完成 UniTask 只消费结果、不记账，正常跨帧流程才消费冷却，静态回调观察后续异常。替身覆盖静默返回、失败、取消和清理后异常；反向撤销判定及异常观察均转红。计数仅代表进入展示流程，不证明像素可见。 |
| CR-2026-09-17-023 | P2 / COMPAT | `SkyIslandRuntimeModule.cs:208,289` 船点招牌和入口 key、`SkyIslandPreludeFlow.cs:355,544` 仪器地图名和交互 key 只在创建时取语言；全局切语言注入链遗漏它们。 | **已修复（2026-09-18，L1 / L2，待 L3）**。三个既有 key 统一进序章语言注入链，招牌 LateUpdate 按语言变化更新原 TMP，清理复位引用与缓存。同一批目标双向语言回归通过；移除注入/推进或稳定语言早返均被拦截。R-01 / R-02 待实机。 |
| CR-2026-09-17-024 | P2 / COMPAT | `SkyIslandResidents.cs:185` 将普通居民名字按生成时语言交给 NPCNameTagHelper，helper 缓存成 DisplayName，之后只刷新旧字符串；岛上切语言不更新浮舟、眠苔、折翎、钟守的头顶名。 | **已修复（2026-09-18，L1 / L2，待 L3）**。岛上 owner 按语言变化更新既有姓名登记，含隐藏及永久居民，异步出生后现取姓名。共享 helper 保留 UI、样式与高度，不新建已注销登记。六位居民双向语言及销毁/注销回归通过；R-03 待实机。 |


## 2026-09-17 天空岛导航与场景刷新复核

本轮面向完整玩家旅程核对现有内容，详细完成度与 N-01～07 实机清单见 `docs/reports/sky-island/2026-09-17-天空岛完成度与体验优化.md`。保留并行婚姻/居民气泡改动，下面两项是本轮新增修复。

| ID | 级别 / 分类 | 已确认问题 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-17-021 | P2 / COMPAT | `SkyIslandMapMarkers.ObjectiveTargets` 只按航标/结局指路；HUD 已按官方任务指引接取和复命。双航标点亮后地图/罗盘主线仍指钟庭，敲钟后直接取消主目标，漏掉尚未交付的任务。 | **Fixed（L1/L2，待 L3）**。选择逻辑从原任务文案方法抽到 `NextContactQuest`，HUD 与地图复用同一任务及既有给予者装置映射。8 条中英/东西顺序/和解战斗完整路线每步经 Codec 重开并核对目标，三种错误导航变异全部命中预期断言。蛙卵/信鸽优先级与可选支线保留。 |
| CR-2026-09-17-022 | P2 / COMPAT（性能） | `SkyIslandWorldStory.Tick` 在任意剧情位变化后调用整组 `RebuildFeedback`；接交任务位不改变场景却销毁重建所有纪念物、灯光与交互体，语言和旗标同时变化还可能同帧重建两次。 | **Fixed（L1/L2，待 L3）**。重建入口按七种可见事实与当前语言早返，128 种组合覆盖真实变化、纯任务变化、语言变化、删除事实和旧对象销毁；取消早返/漏掩码/忽略语言三种变异均转红。最大组合的纯任务变化不再重复创建 8 个测试物体；没有实机帧时间采样，不能据此宣称零性能问题。 |

## 2026-09-17 天空岛婚姻修复复审（两项均已修复，待实机）

按 owner“再次审核”发现延迟外层与 actor 复用问题，再按“全部修复”完成以下 COMPAT 修复。013/014 的原修复保持有效；复现证据见 `docs/reports/sky-island/2026-09-17-天空岛婚姻复审问题修复与验收.md`，最终实现、验证及实机清单见 `docs/reports/sky-island/2026-09-17-天空岛婚姻复审问题修复与验收.md`。

| ID | 级别 / 分类 | 已确认问题 | 状态与证据 / 建议 |
| --- | --- | --- | --- |
| CR-2026-09-17-019 | P1 / COMPAT | `NPCMarriageSystem.cs:222,647` 的结婚/离婚静态异步收尾等待后才取当前宿主，按名字回收 registry，没有原场景、槽、角色或操作代际校验；旧对象在换图时销毁、新实例先登记后，迟到回调会误删新 NPC。离婚还会作废当前恢复请求。 | **Fixed（L1 / L2，待 L3）**。送戒指/离婚入口捕获宿主、玩家、槽、场景和原实例；成功的新关系操作更新代际，异步继续/反馈/回收前复核身份、关系及跟随状态。旧回调不动当前 registry 或恢复请求，文字回退随原角色销毁取消。SkyIslandMarriage 从真实入口覆盖两个居民及叮当/羽织、逐项上下文失效和同一实例重复关系操作；撤销关键门与销毁令牌会触发行为失败。任务板接管仍有效。 |
| CR-2026-09-17-020 | P2 / COMPAT | `SkyIslandResidentDialogue.cs:95` 复用婚礼先创建的 actor 时按居民 ID 注入名字，实际 actor 的名字 key 却带 `marriage_`，并且缺失的剧情 portrait 没有补装。教堂「回忆当天」走文字回退后切语言，再聊航路会残留旧语言名字及缺立绘。 | **Fixed（L1 / L2，待 L3）**。共享 RefreshPresentation 刷新复用 actor 实际 NameKey，剧情补可用立绘；缺资源保留已有图，婚礼复用同样刷新名字。真实工厂和生产解析方法覆盖婚礼先创建/剧情先创建、晴禾/苇白、中英双向切换及再次回忆；错误 key、不装 portrait、不刷新回忆名字三种反向探针均转红。 |


## 2026-09-17 全项目续审：战斗追加效果与生命周期

详细调用链、隔离边界、反向验证及实机操作见 `docs/reports/reviews/2026-09-17-战斗回调与生命周期复核.md`。没有启动游戏，下面修复证据均止于 L1 / L2。

| ID | 级别 / 分类 | 已确认问题 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-17-015 | P1 / COMPAT | 词缀殉爆、共享变异天降殉爆、丧尸奖励与死亡爆炸可在 Health 的 Hurt / Dead 事件中同步再调用官方 CreateExplosion，覆盖外层共用 colliders / damagedHealth，合法群体命中输入会漏掉原爆炸的后续目标。 | **Fixed（L2）**。先让出一帧再结算，原 context / 局 / 玩家 / 关卡失效时作废，暂停时等待；丧尸复用 RunOnlyObjects 并立即释放完成记录。共享变异补效果归因和来源过滤，保留天降殉爆伤己风险。AffixCombat 真实入口 + 官方缓冲语义替身复现旧词缀漏伤；分别撤销三处延迟均触发行为断言。 |
| CR-2026-09-17-016 | P2 / COMPAT | 官方致死回调先发 OnDead 后发 OnHurt。词缀原分发器不校验主角存活，死亡后仍可能反弹 / 加磐石，死契 ticker 与旧词缀 context 留存。 | **Fixed（L2）**。主角死亡立即 ClearActiveContext，受伤 / 重建入口同时检查存活；复用该清理方法，无新增生命周期状态。保留结构事件供新角色 / 换装重建。回归覆盖致死尾随、死亡后复活的旧爆炸取消、重建后效果恢复；去掉死亡清理确实转红。 |
| CR-2026-09-17-017 | P2 / COMPAT | 丧尸玩家光环 / 弹道尾迹的区域爆炸没有效果标记，能绕过原本排除效果伤害的命中 / 击杀分发。官方爆炸调用失败时又无视 canHurtSelf=false，回退为对施放者造成伤害。 | **Fixed（L2）**。玩家来源区域爆炸标为效果；player-only fallback 继承防自伤限制，敌方来源及后备保持。逐字区域入口回归覆盖两个分支，撤掉标记 / 防自伤门各自触发预期断言。 |
| CR-2026-09-17-018 | P2 / SAFE（测试） | 并行新增天空岛气泡依赖后 SkyIslandEncounters 夹具没有链接真实 Chatter / 话语表，导致全量执行回归无法编译该组。 | **Fixed（L2）**。补链接与明确的官方 UI / Forge 替身，追加失败不消耗预算、距离 / 冷却 / 暂停 / 对话 / 销毁 / owner 失效检查。生产气泡代码由原会话负责，本轮没有改动；移除显示失败门和强制台词暂停门分别命中对应断言。后续同类集成缺口（两个判据夹具漏链居民剧情 partial）一并补齐；末次全量执行回归 41 / 41 PASS。 |

## 2026-09-17 天空岛 NPC 中途结婚与剧情衔接（已修复，待实机）

原始证据见 `docs/reports/sky-island/2026-09-17-天空岛婚姻复审问题修复与验收.md`；后续按 owner 要求完成 COMPAT 修复，行为与实机步骤见 `docs/reports/sky-island/2026-09-17-天空岛婚姻复审问题修复与验收.md`。

| ID | 级别 / 分类 | 已确认问题 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-17-013 | P1 / COMPAT | 原给予者成功缓存不随 NPC 销毁失效，苇白本趟结婚后官方 590011 无法接交，继而影响 590012。常规航务委托不是官方主线替代入口。 | **Fixed（L1 / L2，待 L3）**。预算/成功早退前核对实际给予者，销毁时撤销该 ID 并重开有限重试，同趟补委托板；随行配偶使用现有组，晚于板生成也保留同 ID 任务入口。SkyIslandMarriage 覆盖婚礼/送回家/重入/UI 迟到/耗尽预算，撤掉失效处理命中行为失败。婚后双语剧情交互同时补齐。 |
| CR-2026-09-17-014 | P2 / COMPAT | 原离婚分发缺永久 NPC 实体分支，晴禾 / 苇白只取消请求和占位物，真实教堂角色及 registry、驻留状态残留。 | **Fixed（L1 / L2，待 L3）**。保留异步作废，再分发 ReleaseDivorcedNpc 回收实体并注销；原居住场景下次接管，不在基地重刷天空岛居民。SkyIslandMarriage 覆盖两个 ID 与幂等清理；RuntimeOwnership 保持通过。 |


> 只记录 confirmed findings。未验证线索放本文件的 UNVERIFIED 区，或在 `FIX_TRACKER.md` 中标为 `accepted/deferred/refuted/documented`。

## 2026-09-17 龙裔燃烧弹误选烟花

| ID | 级别 / 分类 | 已确认问题 | 修复与证据 |
| --- | --- | --- | --- |
| CR-2026-09-17-012 | P2 / COMPAT | `PreCachePrefabs` 把名称含 `fire` 的任何 Grenade 当燃烧弹，`Firework` 命中；搜索还预先缓存首个任意手雷，最终选择依赖已加载资源及遍历顺序。用户报告更新后投烟花；本轮确认的是错误选择规则，未复现用户游戏中的具体遍历顺序。 | **Fixed（L1 + 结构守卫）**。物品站确认官方燃烧弹为 #941 / `Item_FireGrenade`；按物品身份读取 `ItemSetting_Skill → Skill_Grenade → grenadePfb`，并照官方 `OnRelease` 同步引信与范围等参数，缺失时使用既有火焰爆炸后备。定时与复活八方向共用入口。六种反向破坏均被守卫拦截并按字节还原；Windows 编译及 L3 状态见 FIX_TRACKER 同名记录。 |

## 2026-09-17 全项目玩法闭环复核：无伤判定、纯演出与额外战利品

用户优先目标清楚、奖励有用、战斗与配装深度，并明确不删减内容。以下为本轮从当前生产代码与官方实现核实的问题；与同日天空岛并行会话的修复分别记账。详细范围与 L3 待验项见 `docs/reports/reviews/2026-09-17-战斗回调与生命周期复核.md`。

| ID | 级别 / 分类 | 已确认问题 | 修复与证据 |
| --- | --- | --- | --- |
| CR-2026-09-17-008 | P2 / COMPAT | `CampaignObjectiveCollector.OnGlobalHurt` 对每个玩家受击事件都报告失去无伤。官方 `Health.Hurt` 即使 `finalDamage == 0` 也发 OnHurt，导致第一章前两波无伤目标误败。 | **Fixed（L2）**。仅正最终伤害记受伤；`CampaignPlayability` 直接链接采集器、追踪器和六章实际数据，验证零/负/NaN 与正伤害、波次边界、暂停、撤离、失败重试与换局。恢复旧判定确实转红；实机事件链待验。 |
| CR-2026-09-17-009 | P2 / COMPAT | `CreateRandomEventHarmlessExplosion` 用零伤害调用官方 ExplosionManager。官方仍逐接收体调用 Hurt，0.05 米半径不能保证不命中；烟花和空投尘土会额外进入战斗事件管线，违背纯演出用途。 | **Fixed（L1 + 结构守卫）**。直接实例化官方 normal/flash FX，保留原范围与幅度的镜头轻震；空投自己的引怪声保留。`RandomEventFlavorBudgetGuard` 下钻桥方法，反向加入战斗调用/移除特效均报错。视觉与实机受击副作用待 L3。 |
| CR-2026-09-17-010 | P1 / COMPAT | 种子、龙裔与龙王专属奖励直接调用 `Inventory.AddItem` 且忽略 false。`LogBossLootInventory_LootAndRewards` 在另一个协程把箱容量收紧到现有物品数；专属奖励含子 IEnumerator，入箱路径没有容量保证。合法满箱输入下官方 AddItem 返回 false；旧代码漏物品却记录成功，龙系还登记收藏（L2 已复现，实机发生频率未测）。 | **Fixed（L2）**。三条路径复用 `InteractableLootboxInventoryHelper.TryAddExtraItem`，有空格复用、满箱扩一格，失败回收未交付实例；只成功交付才登记收藏。`BossRewardDelivery` 链接种子与 helper、逐字抽取龙系方法，34 条断言；逐条恢复旧入箱调用均报满箱丢奖励。实例化/扩容故障、回调在挂载后抛错也覆盖；Unity 调度和搜刮界面待 L3。 |

### 同轮工作树集成时修正的文案回归

| ID | 级别 / 分类 | 已确认问题 | 修复与证据 |
| --- | --- | --- | --- |
| CR-2026-09-17-011 | P2 / SAFE | 并行精简后的灭蚊灯说明写成“吸引并电落 12 米内的云蚋”/“lures and zaps … within 12m”，把吸引与击杀范围合成同一数值，容易让玩家把灯摆在无法攻击的位置。 | **Fixed（L1）**。只修这两行说明，明确“12 米吸引、3.2 米内逐只电落”；依据 `SkyIslandMosquitoRules.ZapperLureRadius = 12f`、`ZapperRadius = 3.2f`、`ZapperHits` 与既有双语 Wiki。运行规则不变，完整文案精简仍归原会话；物品提示框待实机目检。 |

## 2026-09-17 天空岛主线与任务生命周期复核

本轮新任务流与存档代码未获 L3；详细操作与证据边界见 `docs/reports/sky-island/2026-09-17-天空岛流程与完成度复核.md`。钟庭就地敲钟是经本次优化请求采用的玩法决定，单列在 FIX_TRACKER，不把原设计偏好当作 bug。

| ID | 级别 / 分类 | 已确认问题 | 修复与证据 |
| --- | --- | --- | --- |
| CR-2026-09-17-001 | P1 / COMPAT、SCHEMA+ | `TryBackfillIslandQuests` 没有一次性门，故事重开时以目标事实补 Delivered；已接任务完成目标后返回基地便可能被代交 | **Fixed（L2）**。可选迁移完成字段；真实规则与 Service 保存重开测试保持待交付；既有任务位不回填 |
| CR-2026-09-17-002 | P2 / COMPAT | `StoryRules.Objective` 不看官方任务接取 / 交付位：双航标完成直接催去钟庭，没有提示向苇白复命、再找浮舟 | **Fixed（L2）**。HUD 从任务表取地点与阶段，中英完整旅程回归 |
| CR-2026-09-17-003 | P1 / COMPAT | `EnsureStory` 兼容检查失败后保留 story，下次仅因 IsCurrentSlot 即返回成功，跳过迁移和 routeUnlocked 更新 | **Fixed（L2）**。独立就绪状态、兼容成功后才发布、同一 owner 重试与关闭复位；逐字抽取生产方法注入实际键写故障验证 |
| CR-2026-09-17-004 | P2 / COMPAT | 普通居民交互分支漏挂官方给予者；关系居民 Attach 因 UI 尚未就绪失败后，fallback 又把 HasResident 当成已经完成接线 | **Fixed（L1，守卫通过）**。两个分支都接，重试查真实挂接并补原交互组；Unity 时序待 L3 |
| CR-2026-09-17-005 | P2 / COMPAT | “航路任务” raw key 只在 Attach 注入，缺少全局切语言重注入，已有居民交互保留旧语种 | **Fixed（L1，守卫通过）**。并入已接好的任务本地化注入链；界面刷新待 L3 |
| CR-2026-09-17-006 | P2 / COMPAT | 任务表 CanDeliver 未约束地图且生产 TryCommitDelivery 不调用它，声明的岛上交付限制缺少执行门 | **Fixed（L1/L2）**。表约束所在地图，桥提交前调用；地点与写屏障矩阵执行回归，桥接线由守卫核对 |
| CR-2026-09-17-007 | P2 / COMPAT（PERF） | 序章 Tick 与任务上下文常驻调用 IsBundleDeployed，每次执行 File.Exists；同步任务与标记时还有额外调用 | **Fixed（L1/L2）**。现有 Schedule 采样、Tick 和 Context 读 owner 内存；调度刷新与查询不触盘有隔离测试，真实帧时间未采样 |

未证实为缺陷：场景内 `QuestGiverView` 是否始终存在、长英文 HUD 实际布局、夜战与头目帧时间。没有用未经测量的性能猜测重写战斗或削减特效。

## 2026-09-16 天空岛昼夜时钟与官方任务接口审核：1 P1 + 1 P2 + 2 P3（均已修，L1 / L2）

来源：owner 要求审核昼夜切换是否跟官方时钟、剧情任务有没有走官方接口。未启动游戏、未读写玩家存档；正式构建已部署，实机按人工清单 2.24。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-16-003 | **P1** / WIRE | 序章桥 `officialQuest.Tick()` 排在 `SkyIslandPreludeFlow.Tick()` 的 `GetComponent<SkyIslandSession>() != null` 早退之后，岛上根本不跑；任何岛上官方任务都无法投影。岛上主线（航标 → 钟守 → 归航钟）全部只在 Mod 旗标 + 自绘面板里，玩家看不到官方任务日志。 | **Fixed（L1 / L2）**。桥上移到 `SkyIslandRuntimeModule` 常驻并先于早退运行；单任务桥改成注册表；岛上三条挂苇白 / 浮舟 / 钟守（`SkyIslandOfficialQuestTable`），`SkyIslandPreludeGuard` 钉住时序。 |
| CR-2026-09-16-004 | **P2** / PLAYABILITY | 岛上判夜 21–5 与官方 `TimeOfDayController` 19–5 差两小时且两套同时生效：19–21 点官方 Volume 走夜档、敌人视野已按夜里衰减，岛上却不起夜风、不刷云蚋、夜限定头目不补刷。时间值本身 100% 读官方 `GameClock`（基地 `TimeOfDayConfig.forceSetTime/Weather` UnityPy 实读 false，不拨表）。 | **Fixed（L2）**。`SkyIslandNight` 19–5、`ForcedHour` 22、光照分段 16–18 / 18–19；守卫 / 夹具 / 文案 12 处同步；Dev 只读用例 `SKY_NIGHT_BOUNDARY_OFFICIAL` 实机比对官方常量。 |
| CR-2026-09-16-005 | P3 / DOC | `DebugAndTools/SkyIsland/AGENTS.md` §4 仍写「官方任务系统刻意不接」，与代码、根 AGENTS、contracts 冲突；repowiki 同。 | **Fixed**。 |
| CR-2026-09-16-006 | P3 / DOC | 教程示例标识符（`IsOwnQuest` / `ObjectiveDone` / `TryCommitFinalFact`）与实现对不上；未提 `completedQuests` 残留钉死 `IsQuestAvaliable`、`ActivateQuest` / `IsQuestAvaliable` 对未知 id NRE。 | **Fixed**：教程按注册表实现对齐并补 §7a 自定义给予者。 |

### 已接受（documented）

- 已交付任务 history 缺失时 `ForceComplete` 重建再发一次 `Quest.onQuestCompleted`：`AchievementManager` 查不到 `Quest_59xxxx` 直接返回，`BDSManager` 一条匿名遥测；每次加载每条至多一次。摘掉单个监听要反射事件后备字段，属 §10 级别改动，不值。
- `ModBehaviourPartialBudgetGuard` 红由另一会话未提交的 +5 行造成（见 FIX_TRACKER 本轮），本轮净增 0。

### UNVERIFIED（需要 L3）

- 官方 `QuestGiverView` 在自建 bundle 场景里是否存在（代码 fail-closed）；岛上给予者三页、目标通知一次、原地交付、换槽 / 重进 / 旧槽回填、缺席兜底、隔离副本卸载；19 点起夜与官方同相。

## 2026-09-16 天空岛入口与 Jeff 序章审核：2 P1（均已修，L1 / L2）

来源：owner 要求全面审核天空岛的玩家流程、代码设计与性能，并对照 `鸭科夫源码/`，把首次进入改成由官方 NPC Jeff 自然引出。本轮未启动游戏、未部署、未读写玩家存档；实机表现与帧时间仍按人工清单验收。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-16-001 | **P1** / PLAYABILITY + WIRE | 天空岛入口没有任何故事门：模块一发现基地船点就直接加招牌和「前往天空岛」，`SkyIslandSession.CanEnter` 也没有解锁判据。新玩家安装 Mod 后会被直接告知可上岛，官方世界里没有人、物或事件介绍群岛，流程突兀；老玩家与新玩家也没有兼容分流。 | **Fixed（L1 / L2）**。向官方 `QuestCollection` 注册稳定 ID `590001` 的 Quest / Task prefab，给予者为 `QuestGiverID.Jeff`：在 Jeff 官方任务页接取「云上的坐标」→ 正常出击零号区 → 地图 POI「失落的航向仪」→ 近点才生成「断风游猎·守」→ 击杀后读取坐标，官方任务目标完成并提示 → 回 Jeff 的官方进行中页点「完成任务」解锁船点。船点扫描、交互回调、`SkyIslandSession.CanEnter` 三层共用同一解锁事实；旧槽只有存在既有群岛事实才自动补齐三段旗标，空白槽不迁移。官方任务只做 UI / 事件投影，保存快照过滤本 Mod ID，权威进度仍在 Mod 分槽存档。 |
| CR-2026-09-16-002 | **P1** / SAVE（故障恢复） | `SkyIslandStoryService.SettleRaidHeld(false)` 在确认 store 可写前就清掉 `raidHeldNotes`。若此时已有写屏障 / `StoreFaulted`，恢复 owner 会把内存旧快照重新编码，原本应随出击回滚的灯、蛙等记录会永久留档，造成物品回滚而剧情事实没回滚。 | **Fixed（L2）**。`keep=false` 在写屏障和恢复期间继续保留排除集；真正存入撤回候选后才清。`keep=true` 则在编码前解除排除。`SkyIslandAuditRegression` 加入故障注入，核对恢复 JSON 不含被撤回记录。 |

### UNVERIFIED（需要 L3）

- Jeff 官方任务的可接取 / 进行中 / 已完成页、任务标记、零号区 POI 可见性、目标位置的实地碰撞、头目生成与掉落箱、官方完成按钮和船点即时开放，均需按人工清单第 0 步实测。
- 静态热路径检查未发现新逐帧全局扫描：主循环 0.25 秒节流，Jeff 查询最多 12 次，角色资源扫描只在玩家进入目标 70 米内且本次需要生成头目时执行。没有本轮实机采样，不能据此宣称「无性能问题」；零号区接敌帧时间与应用退出时尚有待写故事批次的故障窗口仍需 L3 / 进程级故障注入。

## 2026-09-15 F3 全自动验收首轮复核补修：1 P2 + 2 P3（均已修，未实机）

来源：owner 首轮全自动验收（runId `20260914_143303_766`，Dev MVID `2fb9d50b`）复核后要求全部修复。
对话多选看不见、岛上点灯被拒两处生产缺陷由另一会话在 `84994b1` 修复（见 `FIX_TRACKER.md` 2026-09-14 首轮实测一节）；本节是其余三条。
修复均为 L1 / L2，待下一轮复测。流水见 `FIX_TRACKER.md` 同日一节。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-15-001 | P3 / COMPAT（本地化） | 天空岛世界空间文字只在建出来或门状态变化时写一次：岛上中途切语言后，桥口木牌、采集点与搜刮点浮空字、纪念物与信鸽标签仍是旧语言（首轮复拍英文扫描 75 处，L3） | **Fixed（L2）**。四个 owner 在已有推进里比较语言，变了才重写：门不重扫导航；纪念物抽成 `RebuildFeedback`，不重播回话、不补发。新增 `SkyIslandWorldTextLanguageGuard`，反向验证 7 处 |
| CR-2026-09-15-002 | **P2** / COMPAT（存档恢复） | CR-2026-09-07-008 的修复没有生效。`RefreshWeddingBuildingPresence` 在注入前调官方 `BuildingManager.Any(id, false)`，而 `Any` 跳过 info 未注册的记录，注入前恒 false；缺好感历史标记、存档里放过教堂的档两条入口都不注入，教堂永久不显示，官方每次进基地报 `No prefab for building wedding_chapel`（09-07 起每轮 3 条） | **Fixed（L2）**。改按 `BuildingManager.GetBuildingAmount` 的原始 ID 计数（`BuildingInjectionHelper` 新绑定）。`GameplayLogFixes` 夹具改为原样抽取生产方法、替身按官方语义，换回 `Any` 转红；`ContentBuildingOwnershipGuard` 同步 |
| CR-2026-09-15-003 | P3 / TEST（判据错位） | `MODE_H_FULL_SEASON` 要求赛季结束后回到 `Base_SceneV2`，而生产在名人堂确认后原地收场、不切场景；09-08 起每轮白等 90 秒后判红，掩盖其余子项 | **Fixed**。在竞技场原地判干净并拆出子项；`GameplayValidationRunnerGuard` 禁止赛季文件再等基地场景 |

## 2026-09-14（四）UI 优化对照审核：6 P2 + 18 P3（均已修）+ 5 条 PLAUSIBLE

来源：对照图集规格、实机前减负报告、人工清单 §2.14–2.16、AGENTS §4.14 / §4.17，审核 `c00617f` / `00c8624` / `599bc6b`（报告 `docs/reports/reviews/2026-09-14-UI优化对照审核.md`，local-only，编号 F-01…F-29）。owner 随后要求全部修复、需要拍板的自行按主流游戏口径定。流水与拍板见 `FIX_TRACKER.md` 同日（四）一节。**修复全部是 L1 / L2，没有实机**（清单第 2.18 步）。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-14-015 | **P2** / TEST（合成模型错） | F-01：`SkyIslandUiContrastGuard` 在 sRGB 数值上做 alpha 混合，又把相对亮度统计值当 sRGB 灰度代入；游戏是 Linear 色彩空间。按线性复算：大标题眉题 2.13、字幕 3.95 / 警示 2.86、右上卡片边 2.16、焦点行对常态行 2.13，守卫却全绿 | **Fixed（L2，观感待 L3）**。守卫改线性光合成，模型钉在复算值上；常量按线性模型重定，焦点改 Accent 行边。`SkyIslandUiContrastGuard` 25 个反向检查 |
| CR-2026-09-14-016 | **P2** / UX | F-02：描边环厚度 1.25 时直边上没有满覆盖纹素（最亮 0.75），「≥3.1:1」按满覆盖算，实际 2.3–2.4:1 | **Fixed**。`StrokeThickness = 1.5`；守卫照生产距离场算覆盖率 |
| CR-2026-09-14-017 | **P2** / COMPAT（判据错位） | F-03：秘境物证 S1–S4 走剧情动作只写旗标、不进 `discoveredNotes`，手记最多 16/20、官方图鉴镜像与 F3 `SKY_OFFICIAL_NOTES` 必然 `unlock_vs_save`；夹具直接写 `discoveredNotes` 掩盖了它 | **Fixed**。`SkyIslandJournal.Recorded` 认 `TrySearchEvidenceFlag`（取自 `Describe`）；夹具改走生产路径 +2 条断言；磁盘探针 R11 |
| CR-2026-09-14-018 | **P2** / UX | F-04：字幕压暗底宽度写死 1180，平台只剩中间 472 px，800 px 的一行字幕行尾压暗只有 0.40 | **Fixed**。`FitCaptionScrim` 按实测文字宽反算；守卫按行首行尾复算 |
| CR-2026-09-14-019 | **P2** / UX | F-05：采集光斑 `localScale = 1.8` 作用在 0.64 m 的贴图上，实际直径 1.15 m | **Fixed（L1）**。缩放按贴图 bounds 反算 |
| CR-2026-09-14-020 | **P2** / UX（规则只落一半） | F-06：「先判断再挂 / 超过 4 项分二级」只覆盖剧情动作：收过的「收录」、没做完的「交付委托」、「今日已派完」一类占位、锁住的配方、冷却与满血时的服务都照挂；留言板最多 6 项平铺 | **Fixed**。见 FIX_TRACKER（O-3 口径）；`SkyIslandChoiceGateGuard` +11 个反向检查 |
| CR-2026-09-14-021 | P3 / UX | F-07：`ZombieModeUIHelper` 的 Success / Warning 仍是压暗前的旧值，Mode G / 丧尸同色按钮白字 4.15:1 | **Fixed**。改引 token |
| CR-2026-09-14-022 | P3 / TEST（守卫盲区） | F-08：`PersistentHudVisibilityGuard` 只做子串断言，7 个语义变异全部 PASS；字面量层级与 `OnGUI` 不在扫描范围 | **Fixed**。语义判据 + 全层级归类 + 字面量 / OnGUI 扫描；41 个反向检查（含全部 7 个变异） |
| CR-2026-09-14-023 | P3 / UX | F-09：「目标更新」竖条 0.5 s 硬切，`ObjectiveFlashRise` 无引用，注释写 ease-out | **Fixed**。0.15 s ease-out 提亮，按档量化写色 |
| CR-2026-09-14-024 | P3 / UX | F-10：居民台词按 `\n` 切、不按句，最长一屏 66 字 | **Fixed**。按句切、过短合并；`SkyIslandDialogue` |
| CR-2026-09-14-025 | P3 / TEST（复写） | F-12：面板布局属性测试只读两个赋值式、其余复写；标题按 24pt 估，最坏组合缺「立绘 + 横幅」 | **Fixed**。按生产 `Show` 求值标题宽 / 字号 / 地板；列表页不带立绘由结构断言钉住；磁盘探针 R10 |
| CR-2026-09-14-026 | P3 / COMPAT（竞态） | F-13：取消只停我们的 `WaitUntil`，官方 `DoSubtitle` / `DoMultipleChoice` 协程还挂着，紧接着开下一段可能吞掉第一句 | **Fixed**。取消后经官方 `Confirm()` / `confirmedChoice` 推完，推完才放掉请求计数；`SkyIslandDialogue` +2 条断言、磁盘探针 R13 |
| CR-2026-09-14-027 | P3 / UX | F-14：描边圆角按 border 16 取，`panel_surface` 实际弧半径约 14 | **Fixed**。按弧半径（面板 14 / 按钮 9，与 border 取小）；`BossRushUISkinLoaderGuard` |
| CR-2026-09-14-028 | P3 / UX | F-15：共享 `CreateCard` 走两参 Auto 落进 Button 档、没有描边（5 个调用方） | **Fixed**。Card 档 + 描边 |
| CR-2026-09-14-029 | P3 / UX | F-16：描边子物体没设 `ignoreLayout`，带 LayoutGroup 的宿主（F3 天空岛面板）把它排成第一行 | **Fixed**。`LayoutElement.ignoreLayout = true` |
| CR-2026-09-14-030 | P3 / UX（缺包时） | F-17：Rule 档未注入时退到调用方半径的实心圆角条，8 px 高的分隔线变粗条 | **Fixed**。退到 divider 同形程序化条 |
| CR-2026-09-14-031 | P3 / UX | F-18：ESC 键帽压住晴禾立绘顶部约 6.5 px | **Fixed**。有立绘时主视觉地板加键帽那一截 |
| CR-2026-09-14-032 | P3 / COMPAT（重入） | F-22：`DialogueManager` 没有会话归属，后一段顶掉前一段的选项、一次点击答复两段；7 处序列调用不判 `IsDialogueActive` | **Fixed**。会话归属 + 排队；`SkyIslandDialogue` |
| CR-2026-09-14-033 | P3 / COMPAT | F-23：图鉴镜像只增不减，存档里没有、官方已点亮的条目永远不收回 | **Fixed**。双向镜像（`UnlockedNotes` 收回 + `onNoteStatusChanged`）；`SkyIslandStory` +1 条断言、磁盘探针 R12 |
| CR-2026-09-14-034 | P3 / UX（文案分层残留） | F-24：剧情回执追加「进度已记录，待安全时机保存」；谜题页 80–170 字；合成台开场白 92–108 字；钟守「下一步」78 字；英文导语没有长度把关 | **Fixed**。五处都改；`SkyIslandPlaytimeFlowGuard` 加英文导语上限、磁盘探针 R4 |
| CR-2026-09-14-035 | P3 / UX（fail-open 缺口） | F-25：压暗底生成失败时大标题静默直接压在云上 | **Fixed**。退纯色、峰值不打折、打 LogWarning |
| CR-2026-09-14-036 | P3 / UX | F-27：卡片淡入途中被收回（或反过来）时显示量在 EaseOut 值与线性值之间跳一帧 | **Fixed**。`RetargetCard` 换算进度 |
| CR-2026-09-14-037 | P3 / UX | F-28：① 互斥分支了结后正文永久挂伪「下一步」；② 挑战的距离 / 交战中 / 存活上限不在 `Describe`，挂着的挑战项点了才拒；③ 新档手记子页十几行「尚未收到」 | **Fixed**。`Foreclosed`；`CanBeginChallenge` / `CanBeginStoryChallenge` 挂与点共用；来信 / 名册合成一句。ChoiceGate / ContentExpansion / StormEcho 守卫同步，磁盘探针 R5 / R9 |
| CR-2026-09-14-038 | P3 / L10N | F-29：actor 名字创建时按当时语言注入并缓存，本趟切语言后对话框里名字停在旧语言 | **Fixed**。已有 actor 重进时按当前语言刷新 |

### UNVERIFIED（PLAUSIBLE，取决于 Unity 运行时）

- **F-11** 最长英文点名在 34pt 下折两行、实底带占主视觉 52–58%：已按防御性修复处理（标题先缩字号保一行），布局属性测试逐个点名复算 ≤50%；实际排版待 L3。
- **F-19** ESC 键帽 13px `TextSecondary` 在最亮横幅上约 3.99:1：已改 TextPrimary；读数待截图取色。
- **F-20** 判定不读官方隐藏令牌，玩官方游戏机时常驻 HUD 不收：已读令牌；实机待清单 2.18.24。
- **F-21** 回执重开面板时整排选项发白约 5 帧：已立即落到常态色；实机待看。
- **F-26** 剧情面板 `Mask` 模板二值裁切，圆角锯齿可能只是挪了位置：**Deferred**，只能实机看（清单 2.18.26）。

## 2026-09-14（三）天空岛 B 轮「噬风·回响」：1 P3（已修）+ 两条帧时间线索的处置

本轮主体是新增内容（噬风·回响，不是缺陷，见 `FIX_TRACKER.md` 同日（三）一节）。下面只记从上一节 UNVERIFIED 线索里核实出来的一条，以及另一条线索换成了什么工具。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-14-014 | P3 / PERF（首用卡顿） | 物资池懒建：`SkyIslandLootPools.GetBand` 首次用到才建池。搜刮箱要等玩家走进 72 m 才由 `SkyIslandScavenging.Build → SkyIslandRewardCrate.Fill → SkyIslandLootPools.Get` 去建、去填，所以第一次走近远航档或星工档箱子的那一帧要扫全部官方标签。静态读链确认；首轮实机 `SKY_LOOT_BANDS` 单步 639 ms 是同一段代码的耗时（游玩中那一帧没有单独测）。 | **Fixed（L2）**。会话 `Build()` 在读条画面下调 `SkyIslandLootPools.Prewarm()`，按品质带逐带建、每建一个让出一帧，缓存口径不变；F3 `SKY_LOOT_BANDS` 先读缓存状态再 `Get`，交给纯判据 `JudgeLootPrewarm`。守卫 `SkyIslandLootPrewarmGuard`（7 个反向检查）、执行回归 `SkyIslandStory`（带表）与 `SkyIslandValidationJudges`（判据）。**预热耗时未实机** |

- **天空岛帧时间（上一节 UNVERIFIED 第一条）**：仍是 UNVERIFIED，没有猜修。本轮加了 Dev 构建的分项计时（`SkyIslandFrameProfile`，17 段，`[Conditional("BOSSRUSH_DEV")]`），`SKY_PERF_BASELINE_5S` / `SKY_PERF_FINAL_5S` 的 metrics 追加各段 p95 / 最大值、最慢三段、活动灯数、开阴影的灯数与可见 renderer 数。拿到一份实机报告才能定位，读法见清单第 2.17 步 A 段。

## 2026-09-14（二）天空岛首轮岛内 F3 实机日志复核：1 P2 + 3 P3（均已修）+ 线索

来源是 owner 13:34–13:35 在岛上跑的岛内只读验收与 Dev 演练（`599bc6b` 的 Dev 构建，MVID `801bcff5`）。每条都对照 `Player.log` 原文与源码核实；修复本身是 L1 / L2，未实机复测。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-14-010 | **P2** / TEST（假红 + 假绿） | ① `SKY_GATE_REACHABILITY` 把全部自动遭遇组当必到点，09-13 补密加在归航钟庭的 `H_02` 要双航标开门才走得到，新档必红。② 探路只看 `path.error`，不看终点离目标多远：五门全关时钟庭地标 `POI_H` 被记成可达（离线属性测试证明它此时走不到），只有更远的 `Search_H_02` 才报错——这条用例的 PASS 抓不到真正的软锁。 | **Fixed**。锁门岛表 `GateLockedIslands` + `ReachabilityGateFor` + `ProbeReachedTarget`（终点 ≤2 m）；门关着的锁门岛点反过来核对走不到，走得到记红。属性测试 32 种门组合复算表、判据回归 +16 条、`SkyIslandFullAuditGuard` 钉接线；磁盘探针 R1–R5 |
| CR-2026-09-14-011 | P3 / TEST（崩溃） | Dev 演练 `SKY_DRILL_OFFICIAL_DIALOGUE`：`cancelled.GetResult()` 之后又读 `cancelled.IsCompleted`，UniTask 任务已回池，抛 `Token version is not matched`，整条演练 `_UNHANDLED`、断言全丢。`599bc6b` 引入。 | **Fixed**。完成状态在取结果前读进局部变量；新守卫 `UniTaskAwaiterReuseGuard`（全仓、6 个反向检查）；磁盘探针 R6 |
| CR-2026-09-14-012 | P3 / TEST（误导） | 云蚋演练 `force_night_restored=` 打印的是开跑前的值 `previousForceNight`，读起来像「没还原」；`finally` 实际已还原，但报告里没有真正的还原结果。 | **Fixed**。`finally` 之后读回、写 before / after，不一致记红；`SkyIslandDrillNoPersistenceGuard` +2 个反向检查；磁盘探针 R7 |
| CR-2026-09-14-013 | P3 / COMPAT（退出报错） | 在岛上直接退游戏：`SkyIslandRuntimeModule.OnDestroy` 无条件 `Close(true, "runtime_shutdown")` → 派发返航 → 销毁途中 `SceneLoader.LoadScene` → Unity 报 `GameObjects can not be made active when they are being destroyed`。两局 Player.log 都复现。岛上进度由 `Cleanup` 的 `CloseOrRetain` 落盘，未见数据后果。 | **Fixed**。订阅 `Application.quitting`（同一 owner 布尔、`OnDestroy` 退订），退游戏走 `Close(false, "application_quit")`；`SkyIslandLifecycleGuard` 钉住；磁盘探针 R8 / R9 |

### UNVERIFIED（线索，不是 confirmed bug）

- **天空岛帧时间**：码头零敌人时 p95 50.6 / 54.0 ms，本机主套件在其它地图 15–22 ms。只有一次采样，另一会话当时是否占用机器不明；日志拿不到原因（renderers=1272、materials=142、lights=13）。
- **物资池懒建**：`SKY_LOOT_BANDS` 单步 639 ms；`SkyIslandLootPools.GetBand` 首次用到才建池，正常游玩第一次生成远航 / 星工档箱子可能卡顿。未在游玩中测。
- **验收单帧 1944.77 ms**：日志定位不到用例。
- **`SKY_ENCOUNTER_CAP` 的密集段**：这次站在码头、活敌 0，帧时间判据没有被触发，PASS 只证明了内容表与活体上限。

## 2026-09-14 天空岛与共享 UI「实机前减负」：4 P2 + 5 P3（均已修）+ 登记与未验证线索

任务书要求把最后那次实机的人工检查尽量改成读报告，并修剧情面板三处与常驻 HUD 显隐。每条都先读码核实再动手；
证据来自离线读码、按生产常量复算与反编译源对照，**本轮没有进游戏**，全部是 L1 / L2。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-14-001 | **P2** / TEST（假红） | `SKY_BOUNTY_GATING` 的 `active_contract_unfinishable` 对四类委托一律硬判。驱蚋是软门（`SkyIslandSessionGnatBounty.AvailableGnatCull`：刷新是概率事件、白天可完成量恒 0、焚香能把供给压没，退单出口兜底），所以**白天带着没做完的驱蚋单跑 F3 必定红**，而那是合法状态。 | **Fixed**。判据收进纯函数 `JudgeBountyGating`：前三类硬门不变，驱蚋只记 `soft=`。`SkyIslandFullAuditGuard` 改钉判据本体并加钉软门 / 硬门；执行回归 `SkyIslandValidationJudges`；探针 P09（驱蚋退回硬判 → 守卫与回归双红） |
| CR-2026-09-14-002 | P3 / TEST（覆盖漏登记） | `tools/gameplay_coverage.py` 抽用例 id 的正则只认主套件外壳（`RunSyncCase` / `RunIsolatedCase` / …），**不认 `RunSkyIslandSync` / `RunSkyIslandCase`**。岛内 28 条恰好都手工登记了，`automatic - required` 那一侧又是子串判定，于是「用例存在但没登记」不会报。 | **Fixed**。外壳名单收成 `CASE_RUNNERS` + `case_ids_in`；新守卫 `GameplayCoverageCaseRunnerGuard`（外壳全登记或写明排除、端到端两条探针、名单退回旧样子时岛内新用例「看不见」）；探针 P10 / P11 |
| CR-2026-09-14-003 | P3 / MAINTAINABILITY | `SkyIslandEncounters.LivingEnemyCount` 注释写明「给 F3 验收记录 12 活体上限的实际水位」，**全仓零调用**。 | **Fixed**。观测面 `ValidationLivingEnemies` → `SKY_ENCOUNTER_CAP`（开跑与 3 秒采样窗口各读）；`SkyIslandValidationSuiteGuard` 钉接线；探针 P15 |
| CR-2026-09-14-004 | P3 / TEST | `tests/fixtures/F3ValidationExecution` 只逐字抽了主套件的 `RunIsolatedCase` / `TryStep`；**天空岛外壳与它的全部 SKIP 分支从未离线执行过**。 | **Fixed**。同一夹具追加抽取 `RunSkyIslandSync` / `RunSkyIslandCase` / `SkyIslandSessionStillValid` / `RunSyncCase` / `SkyIslandSkipCase`，13 条新断言（38 → 51）；探针 P16（会话没了记成 PASS → 回归红） |
| CR-2026-09-14-005 | **P2** / UX | 居民面板：`BuildHero` 有立绘时 `titleBlock = Mathf.Max(PortraitSize, titleHeight)`，实底带 = inset×2 + block/2 + title/2 = **169 px**，盖掉 247 px 插图的 **68%**；而 `Show` 与旁边注释都说「立绘不算进来」（86 px）。布局属性测试与离线预览自己写了一份 Show 口径，**看不出这条分支**。 | **Fixed**。Show 把标题块传给 BuildHero，不再另算；测试与预览改为从生产源码读两处算式按真实分支求值，新增同一口径 / 实底带 ≤ 一半两条判据与旧写法探针；磁盘探针 P01 |
| CR-2026-09-14-006 | **P2** / UX（a11y） | 选项行：`image.color = SurfaceRaised×0.78`，`highlightedColor = GetHoverColor(SurfaceRaised)` 是绝对色，ColorTint 相乘后**悬停变暗**（最亮底图上合成亮度 0.0140 → 0.0076）；键盘 `Select()` 却把 `image.color` 换成更亮的色。两套相反。另外按原强度（向白 0.22）即便方向对，焦点行对常态行也只有 1.66:1。 | **Fixed**。Graphic 置白、底色进 ColorBlock（`ZombieModeUIHelper.ApplyButtonColors`），悬停与键盘当前项共用 `FocusColor`（0.39 / 0.90），`navigation=None`。WCAG 实算：焦点对常态最差 3.13:1、标签 4.76:1。`SkyIslandUiContrastGuard` 新增复算 + 结构断言 + 6 个探针；磁盘探针 P02 / P03 |
| CR-2026-09-14-007 | P3 / UX | 零选项面板没有可导航项，右上 ESC 键帽 `raycastTarget=false`——**纯鼠标玩家看得见关闭提示却点不动**。 | **Fixed**。键帽接成可点按钮（不进 `buttons`，键盘 / 手柄行为不变，数字键帽仍不吃点击）；`SkyIslandHudGuard` 第 21 节；磁盘探针 P04 |
| CR-2026-09-14-008 | **P2** / UX（叠层） | 常驻 HUD 不跟随官方界面：随机事件徽章（HudOverlay 1200）、血月红罩（1200）、伴宠（990）、Mode H 观战 HUD（960）、Mode G 状态文本（900）**完全没有显隐门**；Mode F 雷达只认 View 与暂停菜单（`NPCCommonUtils.IsAnyUIOpen`），看不见官方对话与拍照模式；词条浮层只看 `InputManager.InputActived` 与 timeScale；丧尸 HUD（28000，压在暂停菜单之上）只认 `PauseMenu.Shown`；天空岛卡片与征程追踪条暂停菜单开着时不隐藏。官方 Views 与对话画布在 sortingOrder 100（2026-09-10 UnityPy 读 `resources.assets` 实测），这些全都压在背包、地图、对话上。 | **Fixed**。十块 HUD 的每帧入口同时经过 `IsOfficialHudHidden()` 与 `IsGamePaused()`；Mode H 挂在模块每帧入口（`TickHud` 只在交战期调，刷怪期会漏）。新守卫 `PersistentHudVisibilityGuard`（落点 + 驱动 + 全仓 HUD 层级文件必须归类，31 个反向检查）；磁盘探针 P05–P08 |
| CR-2026-09-14-009 | P3 / DOCS | 人工清单 2.14.9「面板开着时按 ESC 打开暂停菜单，看入场动画停住」照做不了：面板里 ESC 是关闭键（`Tick` 与 `OnCancel` 都 `Dispose`）。 | **Fixed**。按实际口径改写；入场动画的暂停门由代码守着，面板开着时没有实机入口 |

### 登记在案、本轮不动手（任务书第四节「发现相关问题只登记」）

- **Dev 专用自建试验场的状态行**（`DebugAndTools/ArenaPrototype/ArenaPrototypeControls.cs`，`BossRushUILayers.Hud`）不跟随官方界面。只在 Dev 构建出现，已进 `PersistentHudVisibilityGuard` 的排除清单并写明理由。
- **Mode H 诊断页**（`ModeHUI` 的 `ModeHDiagnostics` 970，带「取消并退款」按钮，只在认证阶段出现）按模态面板排除——判断依据是它是一块需要玩家操作的准备期界面，不是常驻信息。若 owner 认为它也该让位，另开一轮。
- **`Integration/Dialogue/DialogueManager.cs` 同一文件 CRLF / LF 混排**（613 / 51 行）。按字节替换时要兼容两种换行；本轮没有改它。

### UNVERIFIED（未验证线索，不是 confirmed bug）

- `DialogueManager.ShowMultipleChoiceInternal` 自身不判 `isDialogueActive`，`BeginDialogueSession` 已激活时直接返回但仍会 `RequestMultipleChoices`——**两个调用方同时弹多选时后一个可能顶掉前一个的界面**。现有调用方（`SkyIslandResidentDialogue.Run`）都先判 `IsDialogueActive`，Dev 演练 `SKY_DRILL_OFFICIAL_DIALOGUE` 验的也是这条调用方门。未实机、未构造并发场景。
- 丧尸模式 HUD 画布挂着一个**启用的** `GraphicRaycaster`，而它是非交互 HUD（线索来自读码，`ZombieModeHudController.cs` 约 325–330 行）。是否挡住了局内点击未核实。
- 词条浮层原来的抑制条件（`!InputManager.InputActived || Time.timeScale <= 0f`）是否覆盖官方对话与全部 View 未在反编译源里核实；本轮已并入与 `SkyIslandHud` 同一份判定，不再依赖它。

## 2026-09-13（第三轮）天空岛 owner 试用反馈：4 P2 + 2 P3（均已修）+ 1 条 documented 决策

owner 提了六件事。逐条查证下来**三件是真问题、一件比反馈说的更糟、一件有更好的解法、一件不成立**。
证据全部来自离线读码 / 读 `layout.json` / 按生产常量复算，**本轮没有进游戏**——
所有与手感、帧时间、对话弹出相关的结论一律标「未运行验证」，见
`docs/guides/sky-island/天空岛_待人工验证清单.md`。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-13-008 | **P2** / PLAYABILITY | **选项一律无条件挂，前置判断全丢进回调——玩家先看见再被拒绝。** 六个选项构造器（`Add` / `Challenge` / `ServiceChoice` / `CraftChoice` / `JournalChoice` / `StormChoice`）**没有一个带条件**，`choices.Add` 一律执行，能不能做全在 `Select` 回调里判。实际后果：新档走进归航钟庭就看得见「敲响归航钟」，点下去回一句「先恢复两端航标」；群岛手记首页 6 项全部无条件，点进「见闻与物证」是 20 行 `□ …（尚未收录）` 空占位。`SkyIslandWorldStory` 里那条「交还种植记录后这一项不该再挂」的注释**写了三个月、没有对应代码**。 | **Fixed**。新增 `AddIf(choices, label, action)`：走 `SkyIslandStoryRules.CanApply` 统一判，**不满足就整条不挂**（不挂灰掉的占位项——灰项和挂满一样吵），未满足的前置改由 `Hint()` / `NextStep()` 写进正文的一句「下一步」。gating 的唯一事实源收敛到新抽出的 `SkyIslandStoryRules.Describe`，`TryApply` 与 `CanApply` 都从它走，**判断与执行不可能分叉**。守卫 `SkyIslandPlaytimeFlowGuard` retarget 到 `Describe` 并**新增**「两条路径必须都经过 `Describe`」断言（配破坏探针：改坏转红、逐字节还原后 sha256 一致、复绿） |
| CR-2026-09-13-009 | **P2** / MAINTAINABILITY（同域两套轮子） | **官方对话与官方图鉴我们自己早就封装好了，天空岛一行没调。** `Integration/Dialogue/DialogueManager.cs`（`ShowDialogueSequenceBilingual` / `ShowMultipleChoiceBilingual`，actor 走 `DialogueActorFactory.CreateBilingual`，自带立绘位）征程与快递员都在用；官方图鉴 `Duckov.NoteIndexs.NoteIndex` 也有现成范例 `Campaign/CampaignNoteBridge.cs`。天空岛**两处都是零调用**，居民台词自己糊在面板正文位上（最长一段 156 个中文字符一次性铺满），见闻自建了第二套手记（只在岛上翻得到，一趟结束就没了）。而且代码与文档里**从来没写过「为什么不用」**——owner 因此直接问了出来。 | **Fixed，混合架构**。叙事走官方：新增 `SkyIslandResidentDialogue`，逐句推进 + 官方立绘 + 多选「我想办点事 / 先这样」，失败**一律 fail-open 直接开面板**（跟 NPC 说不上话不能变成接不了委托）。见闻走官方：新增 `SkyIslandNoteBridge`，20 条注册进官方图鉴，**我们的存档仍是唯一权威、官方那边只做镜像**（口径同征程）；踩到官方两个坑并记档——`SetNoteDynamic` 只写字典不写 `notes` 列表（只调它界面一条也看不见，必须两边都写）、`titleKey`/`contentKey` 是只读派生属性（文案必须走本地化注入）。**自绘面板保留的理由第一次写进代码**（见 CR-2026-09-13-013）。守卫 `SkyIslandOfficialApiReuseGuard`（8 个破坏探针） |
| CR-2026-09-13-010 | **P2** / PLAYABILITY（内容密度） | **岛上敌人比官方图低一个数量级，而且通关后近四成面积是死区——不是设计如此。** 按最可比口径（玩家 55 m 半径内敌人数）复算：天空岛 **1.23**，官方 9 张图中位 **11.60**，**低 9.4 倍**；**51.8% 的可走面积任何时候都刷不出敌人**。对照设计书自己的规格「12–16 遭遇点、**每点 2–4 敌**」——实现把点数拉满 16、每点却只给下限 2。另查出一个洞：**E 鸣风栈道与 H 归航钟庭只有手动组（噬风 / 钟守），而手动组打过一次永不再现**，这两岛（20.9% 面积）通关后永久零敌人；加上刻意安全的 A 码头 + B 风铃集（18.3%），**通关后 39.2% 是死区**。 | **Fixed，补洞 + 加到规格中值（owner 拍板）**。**不重打包**——全部用 `layout.json` 里已存在但空着的 marker：5 个中继平台只用了 2 个，补 `Relay_K1/K2/K3`（设计书原话「战斗只放在中继岛平台」，这三个正是捷径回程的平台，此前回程白走），E / H 各补一个自动组。自动组人数 2 → 3（规格 2–4 的中值）。结果：自动组 13 → 18、敌人 34 → **61**、密度 3.15 → **5.65 敌/万 m²**（官方 11.84，**仍只有一半**，刻意留余量），通关后死区 39.2% → 18.3%（只剩刻意安全的 A+B）。A 码头 / B 风铃集 / 12 座普通桥面**一律不动**。四处同步：`World.json` + `CreateFallback()` + `SkyIslandContentExpansionGuard` + `SkyIslandContentPlacementPropertyTest`（后者用真实几何复算每个新槽位站不站得住）。**经济口径变化已登记**：每敌一个尸体箱，+27 敌 = +27 箱产出与时长 |
| CR-2026-09-13-011 | **P2** / UX（文案） | **「小灰字太长太小读不懂」的真正来源是状态转储，不是 579 条文案都要重写。** 天空岛共 **579** 条 `L10n.T`，平均中文 19 字，只有 **25 条 ≥60 字**——全量重写是错的解法。真正读不懂的是把内部状态直接印给玩家：手记总览新档输出「见闻 0/20 · 信鸽来信 0/12 · 船员名册 0/4 · 纪念品 0/3 · 到访区域 0/12 · 岛上的灯 3/10 · 蛙鸣池的蛙 0/3」（**141 字**七组分数），以及 `SaveStatus` 把「群岛记录已同步」这种**存档系统内部诊断**直接摆在正文位。 | **Fixed，分层不删字**。正文位只留**一句导语 ≤40 中文字**（新增 `SkyIslandPointText.Brief`，20 处见闻点逐条覆盖、无一落进 default）；25 条长文**一条没删**，搬去官方对话（居民台词）与官方图鉴条目正文（见闻）。`SaveStatus` 的 OK 态不再进正文位，只有出问题时才经 HUD 的持续性问题通道报。可读性同步：正文色 `TextSecondary` → `TextPrimary`（导语是主信息不是注脚）、`BodyFont` 22 → 24。守卫：`SkyIslandPlaytimeFlowGuard` **新增**导语覆盖断言（少一条 `Brief` 就红）、`SkyIslandUiContrastGuard` 复核新配色 |
| CR-2026-09-13-012 | **P3** / UX | **「继续旅程」页脚是纯冗余**：`onClick` 就是 `Dispose()`，与 ESC 完全等价，而且**没有任何守卫断言它存在**。它还占掉一整块版式高度，并在第 6 个选项出现时与之重叠（今天最多 5 项撞不上，是数出来的巧合不是结构保证）。 | **Fixed**。删 `BuildFooter` / `FooterHeight` / `chrome` 里的那一项（`Gap * 3f` → `* 2f`）；主视觉右上角补一个 `ESC` 小键帽（`raycastTarget=false`，不占版式高度）。⚠️ **零选项面板**（收下信、纪念物）去掉页脚后 `buttons.Count == 0`、W/S/Enter 全部早退、**只剩 ESC 能关**——可接受（有键帽提示），已在代码注释里写明。`SkyIslandStoryPanelLayoutPropertyTest` 的页脚碰撞断言整条换成「最后一项不掉出面板下 Pad」 |
| CR-2026-09-13-013 | **P3** / UX（美术） | **人物立绘被一个黑色圆角方板框住。** 立绘是「`SurfaceRaised` 圆角底板 + 描边 + 内缩 5px 的立绘」三层，而六张 PNG **全是 512×512 RGBA 真抠图**（四角 alpha=0，透明像素占 35–50%）——底板纯属多余，把已经抠好的图又装回了框里。 | **Fixed**。删掉底板与描边两层，`Face` 直接站在主视觉插图上，按主视觉高度排（208 px，脱开原来的 160 方板）。补**脚下落影**（复用 `SkyIslandUiArt.GetRadialGlow()` 那张程序化径向柔光）——抠图直接压在插图上边缘会糊，落影把人「钉」在地上。`BossRushUISkinLoaderGuard` 里「立绘底板必须有描边」那条断言**改成钉新形态**（断言「不再有底板」+「有落影」），不是删掉 |

### 本轮登记为 documented、**不是缺陷**的一条决策

**官方任务系统 `Duckov.Quests` 的最终边界（2026-09-16 owner 两次拍板）：跨局主线接（Jeff 序章 + 岛上三条挂岛上 NPC），按出击刷新的岛上委托不接。**

岛上给予者用官方给予者枚举之外的整数（5901–5903，`SkyIslandOfficialQuestGivers`）。四条安全证据：官方 UI 不显示给予者名（`QuestsData.GetDisplayName` / `GetInfo` 无调用方）；`Quest.Compare` 只做整数减法；按给予者列任务的官方查询只做整数相等比较；`Quest.SaveData.questGiverID` 随整条记录被我们从快照剥掉。

`Quest` 与 `Task` 是 MonoBehaviour prefab，`QuestManager.ActivateQuest` 会从 `GameplayDataSettings.QuestCollection` 克隆；
`QuestGiverID` 是写死的 enum，因此本 Mod 复用已有的官方 Jeff，不创建新发布人。序章是跨局、一次性的持久流程，语义与官方 Quest 一致，现已用稳定 Quest ID `590001` 与 Task ID `1` 接入 Jeff 的可接取 / 进行中 / 已完成页面和玩家任务日志。

卸载兼容仍是硬约束：官方 `QuestManager` 默认把任务写进 `"Quest"/"Data"`，缺 prefab 时加载会打 `LogError`。owner 已明确授权接入（对应 `AGENTS.md` 的拍板要求），实现没有把风险转嫁给存档：Mod 故事 key 仍是唯一权威；Harmony 只在 `GenerateSaveData` / `SetupSaveData` 的快照中剥离 ID `590001` 的 active、history、completed、ever-inspected 四类记录，每次加载再从 Mod 事实重建官方投影。卸载后官方存档没有孤儿 Quest ID。

跨 Mod 冲突按所有权处理：任务可用性、完成按钮、全局任务事件、存档过滤与销毁清理不只比较整数 ID，还要求当前注册模板带专用对象名和 `SkyIslandOfficialPreludeTask` 组件。若别的内容先占用 `590001`，本 Mod 停止注册并完全放行对方任务，不删除对方存档；结构性注册失败也停止周期重试。

岛上居民委托继续按单次出击刷新，不进入跨局 Quest；它们还依赖模态功能面板冻结战斗，不能为了界面统一改变玩法语义。

**与之相反的另一条则该接、已经接了**：官方图鉴 `NoteIndex`（见 CR-2026-09-13-009）——
它没有第 1、2、4 条问题，而且同一仓库里征程早有范例。第 3 条部分成立（2026-09-14 UI 优化对照审核 O-1 更正）：
官方 `NoteIndex.Save` 会把解锁状态写进官方存档键 `NoteIndexData`，卸载 Mod 后只剩带本 Mod 前缀的孤儿 key（读档不报错，
图鉴已解锁数可能虚高）。同日拍板接受为例外，镜像改为双向同步，见 `docs/contracts.md` §7.1。

这条边界写在这里是为了**避免以后每轮重查一遍**。同样的理由另有两处副本：
`tests/SkyIslandOfficialApiReuseGuard.py` 的文件头（守卫会断言这些关键词在本文件里还在），
以及 `DebugAndTools/SkyIsland/SkyIslandStoryPresentation.cs` 的文件头。

## 2026-09-13 天空岛 UI 美术化审核：1 P1 + 4 P2 + 2 P3（均已修）

本轮的问题不是「界面缺素材」。盘点第一件事就推翻了这个前提：
**`Assets/ui/bossrush_ui_skin` 早就存在且完全合规**——8874 B / UnityFS 2022.3.62f3，
六张 sprite（`panel_surface` 48²b16、`panel_raised` 48²b16、`button_normal` 32²b10、
`button_hover` 32²b10、`scroll_handle` 24²b8、`divider` 8²b2）尺寸与 Border 逐项对得上规格表，
`m_PixelsToUnits=100`、pivot 0.5/0.5、RGBA32 未压缩（resS 恰好 29184 B = 六张之和）、
**7296 个像素里 0 个 `R != G != B`**（纯灰度），带 1px 亮内描边；加载器也早就接在
`Utilities/AlwaysOnRuntimeHooks.cs:189`。问题全在「做出来了但没用对」。
证据全部来自离线解包 bundle 与按生产常量复算，**本轮没有进游戏**。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-13-001 | **P1** / COMPAT（观感根因） | **图集里烤进去的内描边被深色 token 乘没了，"打了图集还像原型"的根因就是这个。** 规格 §2 同时要求「描边画进图里」与「颜色一律由 `Image.color` 施加」，这两条在本 Mod 的深色调上**互相抵消**。实算：`panel_surface` 的描边像素灰度 ≈250、填充 ≈193（图里差 **49/255**），乘上 `BossRushUIColors.Surface(0.045,0.055,0.065)` 之后屏幕上最大通道差只剩 **2.9/255**；卡片底 `SurfaceRaised` 上是 4.9/255。8bit 量化下人眼能稳定分辨的下限约 2–3/255，**等于没有边**。只有亮底 `Accent`（页脚按钮）上还剩 35.3/255——所以"图集有描边"这件事只在亮底上成立。连带后果：`SurfaceRaised` 对 `Surface` 只有 **1.03:1**（亮云海）/ 1.07:1（暗地形），远低于 WCAG 1.4.11 对非文本的 3:1，剧情面板的五行选项在离线预览里根本不像五个独立可点区域。 | **Fixed（L1/L2）**。描边改成**独立 Image + 独立亮色 token**：新增 `BossRushUI.ApplyPanelStroke` 与程序化「只有环、中心透明」的圆角九宫格 `GetStrokeSprite`（与 `BuildRoundedSprite` 同一个距离场，`f = radius - distance` 即向内深度，外沿 0.5px 抗锯齿、内沿在 `StrokeThickness=1.25` 处淡出；九宫格中心区深度恒等于 radius，所以拉伸出来的整块中心透明，环不随面积变粗）。圆角按**图集实际的 border** 取（`GetSkinCornerRadius`：注入后面板是 16，不是调用方传的 18；按传入值画会露出一道错位的弧）。新增 token `BossRushUIColors.Stroke`（Divider 同色相、alpha 0.78）——直接用 `Divider`(a=0.32) 只有 1.54:1。实算：面板 / 右上卡片 / 选项行的边在场景亮度 p90 / p99 / 暗地形三档下分别是 3.39 / 3.37 / 3.44、3.22 / 3.14 / 3.41、3.31 / 3.31 / 3.32，**全部 ≥3.1:1**。规格文档 §2 的那条已作废并改写。守卫 `SkyIslandUiContrastGuard` + `BossRushUISkinLoaderGuard` |
| CR-2026-09-13-002 | P2 / COMPAT（图集没用对） | **六张图只用了两张，而且分档太粗，细元素比程序化皮肤还差。** 加载器只取 `panel_surface` / `button_normal`（`BossRushUISkinLoader.cs:36,39`），`panel_raised` / `divider` / `scroll_handle` 三张躺在 bundle 里从来取不出来（`button_hover` 按规格本来就是可选、走 ColorTint，属正常）。而 `ApplyPanelSkin` 只有两档（`radius >= 12` 用面板图、否则一律按钮图），于是：3px 的 accent 竖条与 12px 宽的滚动条轨道都穿 32×32 / border 10 的按钮图——Unity 的 `Image.GetAdjustedBorders` 在 rect 小于 border 之和时会把 border 等比压下去并把中心区压到 0，画出来是按钮圆角的一道糊痕；右上 HUD 卡片（r=10）穿的也是按钮皮，而它是一块信息板。 | **Fixed（L1/L2）**。新增六档 `BossRushUISkinPart`（Hairline / Rule / Button / Card / Panel / ScrollHandle）与三参 `ApplyPanelSkin` 重载；加载器五张全喂、`Cleanup` 逐档复位再 `Unload(true)`（漏一档只有那一档白板、其余正常，现场极难定位）。`radius <= 3` 一律程序化。**`divider` 的亮带在可拉伸中心区（8 行里的第 3–4 行），所以铺它的 rect 高度必须 ≥5**——两处 1px 裸 `Image` 的分隔线同步改成高度 8。`ApplyPanelSkin(Image,int)` 两参签名原样保留（守卫按子串钉），实现放在三参重载里并**排在转发器之前**——`BossRushUISkinLoaderGuard` 按第一个同名方法取方法体，转发器排前面会让守卫失去对真实实现的约束力。守卫重写为 check + 9 个破坏探针，按**具体调用点**钉住每个细元素的显式档 |
| CR-2026-09-13-003 | P2 / COMPAT（可读性） | **区域大标题的压暗底把厚度给错了行。** 竖向是 `0.5-0.5·cos(2πv)` 的余弦钟形，峰值恰好落在 44px 的大地名上——它按 WCAG 只需要 3:1；而真正需要 4.5:1 的两行小字被甩到钟形的腰上：在 230 高的压暗底里，眉题（y=+44）v=0.691 → α=0.405，落地提示（y=−42）v=0.317 → α=0.423。实算（场景亮度取 12 张场景横幅的相对亮度统计 p90=0.679）：眉题 **2.42:1**、提示行 **2.54:1**；云海高光（p99=0.839）下 1.66 / 1.75。离线预览图里一眼可见两行糊在云里。另有一条 fail-open 陷阱：`AddScrim` 在压暗底生成失败时直接 `return`，大标题就完全裸在云上（1.81:1 / 1.14:1）且不报错。 | **Fixed（L1/L2）**。竖向换成「两端 smoothstep 淡出 + 中间平台」（`SkyIslandUiArt.Plateau`，`ScrimEdge=0.28`），三行文字**连同文字框的上下两端**全部坐在平台上；`ScrimPeak` 0.60 → 0.72。复算：p90 下眉题/提示行 5.70:1、大地名 10.55:1；p99 下 4.67 / 4.67 / 8.64，**全部达标**。压暗底同时加高 230 → 340、横向淡出 0.26 → 0.30：平台曲线会让它读成**一块看得见的深色板**（上下两条边清清楚楚，正是这个文件一开始就要消灭的东西），拉长淡出区之后回到一团柔光晕。宽度按**实测文字宽**反算（`FitBannerScrim`：`宽度 ≥ 文字宽 ÷ (1-2×edge)`）——英文落地提示整句能到约 700px，不扩宽的话行尾会滑进淡出区。字幕压暗底同理改成按比例给（旧的「文字高 + 40」定值下，两行字幕会顶进上下淡出区：88 高上文字占 v∈[0.227,0.773] 而平台只有 [0.28,0.72]）。守卫按文字框上下两端复算，4 个探针 |
| CR-2026-09-13-004 | P2 / SAFE（可读性，共享 token） | **`GetButtonTextColor` 在两个常用底色上选不出达标的字色。** `Success`(L=0.180) 与 `Warning`(L=0.170) 都低于亮底阈值 0.30 → 选白字，而白字压在它们上面只有 **4.15:1** 与 **4.34:1**，都过不了正文 4.5:1（18px 按钮标签按正文算）。反过来选深字更差（4.27:1）。这不是天空岛独有——共享库的按钮策略在全 Mod 生效。另外 `SkyIslandControls.cs:102` **写死 `TextPrimary`**、完全绕开了 `GetButtonTextColor`：白字压在 `Accent` 上只有 **2.23:1**。 | **Fixed（L1/L2）**。① `Success` / `Warning` 压暗约 7%（`(0.18,0.52,0.36)` → `(0.166,0.484,0.335)`，`(0.58,0.42,0.17)` → `(0.539,0.390,0.158)`）：白字变成 **4.67:1 / 4.89:1**，色相与饱和度未动，亮底阈值余量反而从 40%/43% 升到 49%/52%（`UILayoutReadabilityGuard` 的 TOKEN_MARGIN 仍绿）。**这是全 Mod 级 token 改动，ModeH / PetNest / ZombieMode 的同色按钮一并变暗 7%。** ② F3 面板改调 `BossRushUI.GetButtonTextColor(color)`。守卫按 `LightBackgroundLuminance` 复算五种按钮底色，两条探针（改回旧值）转红 |
| CR-2026-09-13-005 | P2 / COMPAT（可发现性，超出原定改造面） | **采集点「远处有光」在代码里根本不存在。** 设计口径写的是「远处有光、走近浮名字」，实机一趟真人出击 `gathered=1/30`。读代码：采集点在玩家进入 `ActivationRange=60m` 时才建，建出来的可见物只有两样——一盏 `range=6m / intensity 0.8（白天）` 的**点光源**，和一行 `SkyIslandProximityLabel.Attach(sign, 5f, 10f)`（10 m 才开始浮现、5 m 全显）的世界文字。白天一盏 6m 范围、0.8 强度的点光打在被日光照亮的砂岩地面上，60 m 外**没有任何可见信号**。`1/30` 是这个结构的必然结果，不是玩家不想采。 | **Fixed（L1，采集率需 L3）**。补一张程序化径向柔光贴地（64×64 白 + alpha，直径 1.8 m，按五种资源各自 tint，白天 α≈0.36 / 夜里 α≈0.585，风晶簇更亮）。**躺平在地面上**——固定俯视视角下不需要 billboard，也就**没有任何每帧工作**；昼夜只在 `Tick` 里既有的夜晚翻转那一次改颜色。点光源保留，继续负责夜里的局部照明。贴图带 `HideFlags.DontSave`，由 `SkyIslandFieldcraft.ResetStaticCaches` 经新增的 `SkyIslandGathering.ResetStaticCaches` 显式销毁（口径同 `SkyIslandUiArt` / `SkyIslandRendering`）。**本条超出任务书声明的改造面，owner 在批准方案时一并放行** |
| CR-2026-09-13-006 | P3 / SAFE（内容缺口 + 动效） | **群岛手记是全链条里唯一既没有立绘也没有插图的一页**（`SkyIslandWorldStory.cs:624` 传 `null, null`），而它文本最长（总览 + 岛上的灯 + 群岛之物）。另外几处硬开关：右上卡片用 `SetActive` 「啪」地弹出/消失，与同文件里刻意做成连续淡变的 `SkyIslandProximityLabel` 自相矛盾；「目标更新」眉题出现时没有任何动作，玩家很可能整条错过；撤离环靠 `SetActive(true)` 凭空出现，风标点亮这件事没有视觉回执；面板开启动画 0.12 s（60fps 下 7 帧，基本等于直接出现）且没有 `IsGamePaused()` 门。 | **Fixed（L1/L2，手感需 L3）**。① 新增 `skyisland_scene_journal.png`（1024×288，走既有 `tools/gen_sky_island_ui_art.py` 同一条管线与画风口径，参考 12 张现有横幅）+ `SkyIslandUiArt.GetJournalBanner()`。② 动效全部用 `BossRushUI.EaseOut` / `SmoothStep`，**不引入 DOTween 一类依赖**：卡片入场 0.22 s ease-out + 右侧滑入 8px、退场 0.18 s 线性；「目标更新」时强调竖条闪 0.5 s；细横线随淡入 0.45 s ease-out 展开；字幕入场上浮 6px；面板开启 0.12 → 0.18 s 并补暂停门；选项行错峰入场（第 i 行延迟 25 ms、各 0.16 s ease-out 上浮 6px，**只改 CanvasGroup.alpha 与 anchoredPosition，不碰 `interactable`**——第一帧就能按数字键）；地面圆环补程序化横向柔边贴图 + 撤离环 0.35 s 开环与 1.6 s 呼吸（**只动带宽与不透明度，半径一帧不动**——半径等于撤离判定半径是 COMPAT 契约；噬风预警圈不挂，那条是计时器必须线性）。稳态每帧只有一次浮点比较，值没变不写 RectTransform |
| CR-2026-09-13-007 | P2 / COMPAT（观感，owner 指出） | **剧情面板还是一块纯色板。** 上一轮把描边、分档、对比度都修了，但版式没动：插图只占顶上一条 `ContentWidth`（828）宽、最高 232 的横幅，**剩下 700+ px 全是 `BossRushUIColors.Surface` 的纯色底**。owner 的原话是「背景是黑色的程序化的东西，如果你可以把背景弄成图片然后在上面写字那种才好看」。同时点名：字偏小（正文 20 / 选项 21 / 标题 31，在 1920×1080 参考画布上）、选项多时整屏都是框。另外查出一条**随之而来的新坑**：把标题挪到插图上之后，原来的 `GetBannerFade()`（`alpha = t²`）不能用——它把不透明度全堆在底边，标题上沿离 hero 底边 123 px、渐隐总高 153 px 时那里 alpha 只有 **0.19**，最上面那行等于直接压在没处理过的插图上。这和区域大标题压暗底那条 bug 是同一类：**把不透明度放到了文字不在的地方**。 | **Fixed（L1/L2）**。① **整块面板底换成图**：`tools/gen_sky_island_panel_backgrounds.py` 从既有 13 张区域横幅派生 `skyisland_bg_*.png`（220×236，中心裁 62% → 缩放 → 高斯模糊 9 → 降饱和 0.82，13 张共 499 KB，**纯 Pillow、不调生图 API**）。为什么必须是模糊小图：面板是竖的（880×约 940），横幅是 3.56:1，cover 上去要放大 3.26 倍、只看得见原图中间 26% 的宽度，云海渐变会出现带状阶梯；模糊之后分辨率就不重要了。压暗（`BackgroundTintAlpha=0.72`）归代码管、不烤进图里——按 13 张底图实测最亮那张的 p99（0.747）复算，正文仍有约 5.0:1。② **主视觉全出血 + 标题压在图上**：插图铺满 `PanelWidth`、上边顶到面板顶边，圆角由背景层的 `Mask`（模板是面板同一张九宫格，`showMaskGraphic=false`，容器内缩 1px 让面板自己那圈抗锯齿边露出来）统一切。标题与立绘建在渐隐之后、压在图上。③ **实底带取代单条渐变**：`HeroTitleBandAlpha=0.82` 的实底带罩住标题，带子上方再用 `GetBannerFade()` 淡出到全透（渐变整体乘同一个 alpha，接缝处才不露亮缝）。0.82 是按**纯白底**定的，标题有 10.7:1。④ **选项行底半透明**（`ChoiceRowAlpha=0.78`）让底图透出来——下界不是随手取的：再透一点，描边对行底就跌破非文本 3:1（0.78 → 3.09:1、0.65 → 2.96:1）。⑤ **字号整体 +2**（正文 22 / 选项 23 / 标题 24–34）。⑥ **居民面板也有主视觉了**：`SkyIslandResidents.MarkerOf(id)` 把「谁站在哪个地标」暴露出来，居民面板拿他家那一区的插图当主视觉（此前传 `null`，主视觉退化成一条空带）。⑦ 版式算术整体重排（hero 无上 Pad、四个 Gap），`SkyIslandStoryPanelLayoutPropertyTest` 同步改写并新增「标题上沿必须落在实底带里」的断言，中英双语 15 种组合仍全绿；`SkyIslandUiContrastGuard` 新增正文/选项标签/选项边/主视觉标题四条复算与 4 条破坏探针 |

## 2026-09-12（第四轮）玩家链路审核：3 P2 + 4 P3（均已修，含待拍板项 R-6 落地）

本轮的问题不是「承诺兑现了没有」，而是**一个真人从头玩到尾，这张图走不走得通、卡不卡**。
输入包含两份实机证据：`BossRushValidation_20260912_073636_324`（天空岛套件 28 PASS / 0 FAIL / 1 SKIP）
与同场 `Player.log` 里**一趟真人打完的 338 秒出击**（`SKY_TIMING` 全链：落地 8.1 s → 码头见闻 46.6 s →
渡口整备 50.2 s → 风铃集 160.2 s → 镜水寺 249.1 s → 采集 273.3 s → 清场 283.2 s → 听雨洞 325.0 s → 离岛 338.5 s）。
主线闭环、门控、失败恢复、内容咬合逐条走过，**没有发现新的生产阻断**；下表六条全部已修。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-12-011 | P2 / SAFE（内容缺失，非翻译质量） | **英文玩家拿不到主线的操作说明。** `WikiContent/en/map__sky_island.md` 的「The first stretch」**整段缺了中文版的四条路线要点**：「悬根林：清除风标周围的威胁，操作风标台」「残星工坊：清除星灯周围的威胁，操作星灯台」「回程捷径：满足修复条件后操作绳桥或栅门」「鸣风栈道：两端航标亮起后继续前往归航钟庭」。英文版只剩一句「head west… or east…」。这四条正是**唯一**告诉玩家「航标台要先清守卫」的地方——目标卡不说，要到装置面板才知道（可玩性评估 §5.1 已点名）；中文玩家靠 Wiki 补得上，英文玩家补不上。`git show d9ae590` 确认**自 2026-09-09 首版起就缺**，非本轮回归。成因是仓库里**没有任何守卫比对中英 Wiki 的正文**：`SkyIslandPlayerEntryGuard` 只看两边各自含哪些关键词，`WikiSiteStructureGuard` 只看导航结构。 | **Fixed**。英文版补回四条，并把原先散文写的「中继平台」一段一并改成第五条，使两版逐章节 1:1。`wiki-site` 的两份镜像经 `node scripts/sync-content.mjs` 重生成。新守卫 `tests/SkyIslandWikiParityGuard.py`：① 中英两版章节数与逐章节的条目 / 表行 / 提示数全等（今天 17/17 全等，是真不变式）；② 五条路线要点按**语义锚点**各自存在（条数相等挡不住换掉正文）；③ 两份在线站镜像与权威源逐章节一致（手改镜像而不改权威源会转红，镜像的 `::: tip` 容器语法已认）。两条破坏探针（删掉一条 / 条数不变但换掉正文）分别转红 |
| CR-2026-09-12-012 | P2 / SAFE（诚实性，会诱导出已知回归） | **`AvailableBountyProgress` 的主注释里有一条与代码相反的硬断言。** 原文：「`Threats` 的清场是**持久存档事实**，已清过的组这局根本不会再触发回调（`Tick` 直接短路成 Cleared）」。这对 `Threats` 计的那批遭遇是**反的**：`SkyIslandEncounters.Tick` 的短路条件是 `!encounter.Started && encounter.Manual && completed(id)`，**只关手动组**（折翎 / 钟守 / 噬风）；自动组按出击刷新、照样重新触发回调。而 `RemainingClearable` 自己的注释正好写着反面警告——「这里**不能**再排除『存档里已清过』的组……若沿用旧的 `!completed(id)` 过滤，第二次进岛起可完成量恒为 0，『清理航路威胁』永远派不出来」。也就是说**主注释正在把下一个读代码的人推向那条已知回归**。`CR-2026-09-12-004` 修的是同一个注释块里紧挨着的「单调递减」那句，这一条漏了。实机侧面印证：F3 `Threats=avail 13/target 3`，13 = 16 组 − 3 手动组。 | **Fixed**。注释改为如实口径（数的是本趟未清的自动组；存档已清事实只关手动组；照它过滤会让第二趟起恒为 0）。为守住 `SkyIslandSession.cs` 的 1200 行预算，细节留在 `RemainingClearable`，主文件只留一句指路——与 `CR-2026-09-12-004` 同一写法。`SkyIslandGnatBountyGuard` 新增第 6 节：`Tick` 的短路必须带 `encounter.Manual`；`RemainingClearable` 不得出现 `completed(`；注释不得再出现旧谎话且必须写明 `encounter.Manual`。三条破坏探针全红 |
| CR-2026-09-12-013 | P3 / SAFE（开销，R-9 分项） | **剧情体显隐是每帧两次的路径，却无条件做工。** 会话 `Update` 每帧对折翎与钟守各调一次 `SkyIslandResidents.SetVisible`，而这两个目标状态整趟出击只翻转个位数次。旧写法每帧都要做 HashSet 增删 + Dictionary 查（各一次字符串散列）再**无条件** `gameObject.SetActive(visible)`，即使状态一个字没变。 | **Fixed**。`hidden` 是唯一事实源（异步生成落地时由 `SpawnOneAsync` 照它补一次 `SetActive`），因此按它整条早退安全，也不会漏掉「先 SetVisible、后生成」那条时序。口径同 `SkyIslandLighting` 的写入阈值与 `SkyIslandMapMarkers.Apply` 的早退。`SkyIslandFullAuditGuard` 新增断言（早退必须排在增删与 `SetActive` 之前），一条破坏探针转红。**帧时间收益未实测**。R-9 剩余的「物品池首查落玩法帧」本轮**仍未动**：那要改装配期分帧预热，而实机 p95 只有 11.4 ms，没有数据支持现在动它 |
| CR-2026-09-12-014 | P3 / COMPAT（降级面不对称） | **四项居民服务里只有苔药没有装置兜底。** 浮舟的整备挂码头装置、晴禾的归航菜挂菜畦、苇白的委托挂留言板、眠苔的**药臼**挂悬根林见闻点 `Search_D_02`——唯独眠苔的**苔药**只挂在她本人身上。她的异步生成失败的那一趟，玩家没有唯一的付费回血手段，而星苔药膏恰恰要在她的药臼上做，等于把「回血」整条线掐断。这与同一处改动里刚刚为「生成可能失败」给药臼补兜底的理由自相矛盾 | **Fixed**。`Search_D_02` 同时挂苔药与药臼；`healReadyAt` 是 `SkyIslandServices` 的单例字段，两个入口共用同一次冷却，不会双领。中英 Wiki 同步补一句。`SkyIslandFullAuditGuard` 新增「每一项居民服务都必须有装置兜底」的结构断言（按 `Talk` ↔ `ReadPoint` 两侧的 `ServiceChoice` 集合比较），并一并钉住三处合成台；一条破坏探针转红 |
| CR-2026-09-12-015 | P3 / COMPAT（提示噪声） | **「天空岛航路已开放」每次回基地都重播一次。** `announced` 跟着 `OnStartedLoading`（每次切图开始）与「离开基地」两处复位，于是每撤离一次、走回码头子场景就再念一遍。收齐十二封信要 ≥12 趟，这句话就念十二遍。与同文件里刻意把招牌收成「走近才浮现」（`SkyIslandProximityLabel`，注释写明「常驻的浮空字正是网游式头顶标语的来源」）的取向相反 | **Fixed**。去掉两处复位，改成**每进程一次**（模块实例随 Mod 宿主存活）。**入口本身仍每次回基地重新挂**（船点子场景会卸载），只有这句话不再重播。`SkyIslandPlayerEntryGuard` 新增断言（`announced` 不得被复位、置位点唯一），一条破坏探针转红 |
| CR-2026-09-12-016 | P2 / `SCHEMA+`（本地化，可玩性评估 R-6 落地） | **晴禾与苇白的 46 条好感与婚姻台词只有中文。** 实测确认：`Assets/Data/DuckNpcs.json` 里两位各 23 条（问候四档、闲聊、送礼四种回应、升级、道别、婚后六组、三种气泡），而**整份 schema 里唯一的英文字段是 `displayNameEn`**。英文玩家与她们聊天、送礼、婚后对白读到的全是看不懂的中文。这是 R-1…R-14 里唯一一条纯粹损害玩家体验、且没有技术阻碍的开项 | **Fixed（待实机看译文观感）**。`SCHEMA+`：`dialogues` / `marriedDialogues` / 三组气泡里的每一句既可以是裸字符串（老写法），也可以是 `{"cn": …, "en": …}`，两种形态可混排；缺 `en` 时 `L10n.T` 自动回落中文，**老蓝图（`duck_npc_xiaoman`）一个字未改、行为不变**。两条 load-bearing 细节：① 语言在**取用时**解析（蓝图只解析一次，而玩家能在游戏里切语言；气泡的 `string[]` 视图按语言缓存并在切换时重建）；② 档位判据 `IsTierObject` 必须看 `lines` 这个键而不是只看 `Kind`——台词的对照形态本身也是对象，只看 `Kind` 会把 `[{cn,en}]` 这样的单档误判成档位数组而**整组台词静默丢失**。46 条已补译，术语与英文 Wiki 对齐（Frogsong Pool / planting record / Hanging Root Wood / wind beacon / Fallen Star Workshop / star lamp）。守卫 `DuckNpcInvariantGuard` 新增 schema 与数据两侧断言；**新增执行回归 `tests/fixtures/PermanentDuckNpcDialogue`（162 条断言）**——结构守卫证明不了「解析真的把两种形态都读出来了」，而那正是最容易静默坏掉的一层。四条破坏探针全红 |
| CR-2026-09-12-017 | P3 / SAFE（文档事故，唯一事实来源写错） | **`AGENTS.md` 写死的导航顶点余量与实机差 4 倍。** `AGENTS.md:390` 写「导航网格顶点 **4037 / 4095**（余量 1.4%）」，而运行时闸门比的是 `ArenaPrototypeNavigation.cs:24` 的 `mesh.vertexCount`，**实机读数 3870 / 4095（余量 5.5%）**（F3 `SKY_NAV_GRAPH`，由 `F3GameplayValidationSkyIslandCases` 直接取该字段）。4037 是离线 UnityPy 读到的顶点缓冲计数，口径不同。方向是安全的（低估），但 AGENTS 是唯一事实来源，可玩性评估第七节把 E9「云底渡口」标成「导航顶点余量已不多」正是照这个数写的——它会挡掉本可以做的内容 | **Fixed**。改成「以运行时为准：3870 / 4095（余量 5.5%），来源 runId `20260912_073636_324`；离线 UnityPy 的 4037 是另一种口径，不要拿它算余量」，并点明闸门位置。数值本身离线钉不死（要读 bundle），`SkyIslandFullAuditGuard` 只钉「写了哪一侧口径」 |

## 2026-09-12（第五轮）owner 授权后的待拍板项落地：1 P1 + 2 P2

owner 指示「全部需要我决策的都你自己定」并要求上线，于是把前四轮**挂着没动**的待拍板项逐条定案并实现。
每一条都按「可回退 + 离线可证 + 不与已公布设计冲突」三条标准取舍，理由写在下表和代码注释里。

| ID | 级别 / 分类 | 事项与定案 | 实现与验证 |
| --- | --- | --- | --- |
| CR-2026-09-12-018 | **P1** / COMPAT（可达性） | **Mode H 认证池 8–9 人的死局**（第三轮登记的 UNVERIFIED）。用冻结数据穷举：8 人时 **52.9%** 的合法抽签会在某一场建不出六场，9 人 2.5%，≥10 为 0。三条互斥出路取**抬高 `MinProductionCandidateCount`（8 → 9）**。**不取「放宽走廊」**：那是改平衡数值、离线证不了手感。**不取「让落选三席回到对手池」**：与已公布设计直接冲突——落选三人各翻一张**去向牌**（回场签 / 候签 / **撕票「本季永久移除，谁也签不到」**，见玩家 Wiki），无差别丢回对手池等于让「撕票」那位照样上场。**停在 9 而不是 10**：认证失败是每台机器的事，10 会把只认证过 9 个预设的玩家整个挡在模式外；而 9 人的 2.5% **有出路**（判死提示本来就是「退出本赛季重新进入，候选名单会重抽」，重进即重抽），8 人的 52.9% 没有出路——过半的重进照样撞墙。旧值 8 的理由「5 席 + 3 备选」本身算术不成立。 | `ModeHConfig.MinProductionCandidateCount = 9`。撞门槛走的是**既有**的 `AbortSetup` → `Abort_Certification` 文案 + `AbortAndRefund`：在玩家选秀、下注、押注**之前**说清楚并退票离场。`ModeHSeasonViabilityGuard` 枚举起点跟着抬到 9、`KNOWN_DEAD_SCENARIOS` 由 `{8: 1332, 9: 62}` 收成 `{9: 62}`；`ModeHPresetEligibilityGuard` / `ModeHStructureGuard` 的冻结常量同步。执行回归 `ModeHMarketAudit` 把「8 人在选秀门口就被 `draft_pool_too_small` 挡下」正向钉住，并把原来的 8 人可建性审计整体改到 9 人那一档。一条破坏探针（改回 8）转红 |
| CR-2026-09-12-019 | P2 / COMPAT（经济数值） | **岛上物资按品质带内「种类均匀」抽**（R-4）。本轮首次从实机 `SKY_LOOT_BANDS` 的池子大小反解出直方图（算术自洽、可交叉验证）：`q1=77 · q2–3=277 · q4–5=174 · q6–8=204`。**星工遗存带里高半段（204 种）的物品种类比低半段（174 种）还多**，于是「均匀抽」不是「像原版那样随机」，而是**明显比原版更肥**：一趟满搜期望约 91 件、其中约 16 件落在 q6–8。这与设计自己写下的顾虑正好相反（`GuaranteeMinQuality` 的注释：「地上捡到的搜刮箱保持全随机——否则十个星工遗存箱每个保底一件高品质，一趟就发烂了」）——设计**想要**的是别太肥，只是没人算过带内构成。**定案：加一层按品质的抽样权重**。 | 新增纯算术 `SkyIslandLootTables.QualityWeight(quality, minQuality)`：每升一档 ×`QualityFalloffPerStep = 0.6`，**相对本带下界**算（保底带在自己的带里重新递减），权重恒为正（八档跨度最小仍有 280），**没有任何一档会被抽空**。`SkyIslandLootPools` 在既有的按带缓存旁并排缓存一张累积权重表（同一次查询里算好——那一趟本来就要对每个 id 问一次 prefab 做价值上限，品质顺手读），抽样是一次 `Next` + 一次二分。**池子构成、品质带、件数、保底口径、价值上限一律不动。** 按同一份反解估算：星工带 q6–8 占比 54% → 25.6%，一趟满搜的 q6–8 期望 ≈16 件 → ≈8 件。**回退**：把 `QualityWeight` 改成恒返回 `QualityWeightScale` 即退回均匀抽。执行回归 `SkyIslandLoot` 新增权重性质断言（严格递减、恒为正、保底带自带下界、0.6 的逐档数值冻结）；`SkyIslandContentExpansionGuard` 钉住接线与常量；中英 Wiki 改口径 |
| CR-2026-09-12-020 | P2 / COMPAT（节奏） | **12 封信「一趟一封」是全图唯一严格线性、不可压缩的时长乘数**：收齐至少 12 趟，把约 3 小时的内容拉成约 8 小时的完成路径，多出来的部分没有新内容、只是重复出击。**定案：前 8 封维持一趟一封（教学与伏笔，按趟送有节奏上的道理），后 4 封「应时的信」在条件满足时同趟连送。** | 新增纯逻辑 `SkyIslandLetters.NextSameRaidFor`：**只有「有前置且前置已满足」的信**才连送，无前置的前 8 封一律返回 null。`SkyIslandWorldStory.RearmPigeonIfStoryLetterWaiting` 在收下一封之后把一次性闩 `pigeonPlaced` 重新打开，落点与字幕走与首封**完全相同**的那条路径（字幕同样延后 `PigeonCaptionDelay`，免得回执和「又一只信鸽落在…」挤在同一秒）。收齐从 ≥12 趟压到 ≥8 趟，砍掉的正是最没内容的那几趟。执行回归 `SkyIslandStory` 新增：前 8 封逐封确认不连送、双航标后 `Letter_09` 同趟到、敲钟后四封连成一串、收齐后停止；`SkyIslandContentPackGuard` 钉住规则唯一性与重新武装的三处接线 |

### 本轮同时定案、不算缺陷的两条（原待拍板 #10 / R-2 / R-11）

| 事项 | 定案 | 落地 |
| --- | --- | --- |
| **K1 捷径几乎不省路**（R-2，原待拍板 #3） | **接受现状，改说法**。v2 的「风标一亮，悬根林岛心广场就开出返航风道（离装置 39 m）」本来就取代了 K1 的回程价值；为它重打包场景不值得。但 Wiki 原文「缩短重访路线」对三条捷径一视同仁，是**不实承诺** | 中英 Wiki 改成逐条如实：K3 最值得开（后续从码头去风眼少走近一半）、K2 次之、K1 几乎不省路「留给想走风景的人」。代码与木牌文案本来就是中性的（「系牢回程绳桥」），不动 |
| **守钟装置用钟守本人的脸**（R-11，原待拍板 #10） | **保留，并把它写成设定**。换脸要动美术；而「钟庭的敲钟机械一律照着当值守钟人的样子铸，好让归来的人远远认得出谁在等」既解释了这张脸，也正好是这张图的主题 | 归航钟留言（`Lore("Search_H")`，**挑战选项就在同一页上**）与中英 Wiki 的敌人一节各补一段。玩家从此读得到「你打的是装置，不是他本人」 |
| K1–K3 的 K 码给不给玩家看、Bell Court / 残星 的译法 | **维持现状，关闭该议题**。本轮四处（代码 / 中英 Wiki / 在线站 / 木牌）逐项核对**未发现漂移**；K 码印在世界里的木牌上，是三座长得很像的桥的唯一简称，改它要动木牌、按钮、回话与两版 Wiki，玩家零收益 | 无改动，仅在台账登记为已决 |

### 本轮登记为 UNVERIFIED 的线索（不计入 confirmed，不在本轮范围）

| 线索 | 证据 | 现状 |
| --- | --- | --- |
| **全仓 112 对中英 Wiki 里有 33 对形状不一致** | 按「逐章节条目 / 表行 / 提示数」扫全仓：`map__sky_island.md` 修好后仍有 33 对不等，涉及 changelog、equipment、item、mode 等。 | **不改，也不该一刀切**。抽样核对 `item__consumables.md`：英文版**刻意**把「堆叠」「使用时间」合成一行（`Stack 10 / Use time 3s`），10 个章节条条少一条，但**内容是全的**。按条数一刀切会把排版约定当成缺陷，属于把守卫写成假不变式。因此 `SkyIslandWikiParityGuard` 只钉天空岛地图页（两版是 1:1 写的）。其余 33 对要逐页人工判「是排版约定还是真漏内容」，是另一件事的范围 |
| **`duck_npc_xiaoman` 的 34 条台词仍只有中文** | 同一份 `DuckNpcs.json`；本轮只译了天空岛的两位。 | **本轮不做**：小满是示例捏脸 NPC，不在天空岛审核范围（AGENTS §7「不做与任务无关的重构」）。schema 已经支持，补译就是往 JSON 里填 `en`，不需要再动代码 |

## 2026-09-12（第三轮）F3 实机报告驱动：1 P1 + 2 P2 + 4 P3（均已修）

本轮的输入第一次是**实机证据**：`BossRushValidation_20260912_072302_209`（全量套件，`pass=156 fail=5`）与
`BossRushValidation_20260912_073636_324`（天空岛套件，`pass=28 fail=0`），外加同一次会话的 `Player.log`
与一份外部审查报告。`Player.log` 里的 7 条 `RUNTIME_ERROR` 全部 `source=host_or_other_mod`
（MoveBlackMarket、MakeTimeQuacker、TriangleDuckAttachmentExpansion、鸭鸭市场、官方 `Duckov.MiniGames.GamingConsole`），
与本 Mod 无关，不计入本表。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-12-003 | **P1** / COMPAT（生产阻断） | **Mode H 选秀页会变成永久死局：玩家点多少次「签约」都签不下去，也退不出来。** 实机证据：`MODE_H_FULL_SEASON` 连点 388 次「签约」、100 秒内 lifecycle 一步没动（`phase_stalled:Drafting`，`fights=0`），`Player.log` 里是同一行 `签约组合六场可行性检查失败: season_viability_match_6:plan_threat_out_of_corridor` 刷了 194 遍；`MODE_H_CACHE_HIT_CLEANUP` 连带红（`season_not_archived`，赛季根本没法归档）。根因有两层：**① 走廊里有结构上永远抽不出来的 (骨架, 人数)。** 走廊下界是 `threatBudget × minFillPercent`，按「基础威胁和」编制，而单体威胁分只有 38..62（`BossProfiles.json` 12 条）。逐档复算：第 6 场 `champion_beast` 只许 1–2 人，n=1 上界 62 < 下界 136、n=2 上界 120×1.2=144 < 下界 164，**两档都恒不可行**；第 3 场 `relay_squad` 的 n=4（0/495 组合可行）、第 1 场 `single_beast` 的 n=1（整份目录只有 62 分那位可行，而他在本机 `certification_preset_unavailable`）同理。这些空抽白白吃掉 8 个候选 × 3 次技术重试的预算。**② 对手池与签约组合无关。** `BuildPlanEnemyPool` 把**全部五席**都排除，因此第 6 场一旦判死，20 种签约顺序全部一样死；而选秀页既没有取消已选主将的入口（再点一次是 `return` 空操作），也没有退出口，玩家只能强退。规模：复现玩家那台机器的 10 人认证池后，**25 个种子 × 20 种签约组合里只有 120/500 可建，10/25 个种子（40%）连一种可签组合都没有**。 | **Fixed（L2 已证，待实机复跑 F3）**。`ModeHEncounterPlanner.BuildCandidate` 改为：擂台条件先抽 → 用与威胁修复同一条枚举（`TryFindLegalRosterSelection`：同走廊、同必选回响核心、同擂台条件、同合同原型矩阵）筛出**本池真组得出来**的 (骨架, 人数) → 只从这些里抽；一个都筛不出来时回落原路径并照旧报原来的拒绝原因（`available.Count > MaxProductionCandidateCount` 时枚举会整体早退，兜底必须留着）。**数据表、走廊数值、`ModeHConfig` 常量一字未动。** 同时 `OnDraftPick` 支持「再点一次已选主将 = 取消选择」，玩家不再被自己点错的主将锁住；「换替补」那句提示改为如实说明对手池与签约组合无关。修后同一复现：**418/500 可建，25 个种子的每一个主将都至少有一个可签的替补**（0 死局）。新守卫 `tests/ModeHSeasonViabilityGuard.py`（7 条破坏探针全红，并在数据层重算走廊算术：每场都有可行档；认证池 ≥ 10 人时任何合法五席都建得出六场）；执行回归 `ModeHMarketAudit` 新增 `TenCertifiedViabilityAudit`（500 组签约组合，逐组走真实 `CanConstructFullSeason`） |
| CR-2026-09-12-004 | P2 / SAFE（诚实性） | **`SkyIslandSession.AvailableBountyProgress` 的主注释与 `SkyIslandWorldStory.cs` 的行注释都声明了一条驱蚋委托并不满足的硬不变式。** 原文：「这些量只会随本趟进度单调递减（每推进 1 点，剩余量减 1），所以接单时 available ≥ target 就保证这一单做得完」。`Gnats` 分支两处都不满足：`CullableBeforeDawn` 依赖概率刷新，且 `swarm.Alive` 会随新群**回升**；更实际的是玩家自己就能把供给压没——接单后整夜焚驱风香或站在灶火烟里，`SpawnWeight` 归零、已有蚊群全部 `Scatter`（`killed=false` 不计数）。纯概率风险很小、退单出口也在，但**注释声称的硬保证已经不成立**，下一个读代码的人会被误导。 | **Fixed**。三处注释改为如实口径：前三类是硬门、驱蚋是软门，出口是 `TryAbandon`；不单调的两个原因与「玩家自己能压没供给」写在 `SkyIslandSessionGnatBounty.AvailableGnatCull` 的注释里（主文件只留一句指路，`SkyIslandSession.cs` 仍在 1200 行预算内） |
| CR-2026-09-12-005 | P2 / COMPAT（可发现性） | **白天去找苇白，四类委托都挂不出来时，回话完全不提夜里那张驱蚋单。** `SkyIslandWorldStory.BountyChoices` 的 `offered == 0` 分支只说「航路这阵子清得差不多了，物资点也翻遍了。下次出岛再来看看吧。」——驱蚋单是四类里唯一「白天永远挂不出来」的方向，玩家在游戏里第一次该遇到它的地方无从知道它存在。CR-2026-09-12-002 做了内容、守卫钉了、Wiki 写了，唯独游戏里没说。 | **Fixed**。该分支按「这一趟真会起蚋（`SkyIslandGnats.Usable`）且此刻不是夜里」补一句「天黑以后再来一趟——起蚋的夜里我这儿还有一张驱蚋的单子」；精灵表缺失、整趟不刷蚋的那一趟仍说原来那句，不承诺不存在的活。新增只读属性 `HasGnatBountyThisRaid` / `IsNightNow`（读钟仍只经 `SkyIslandLighting` 一处） |
| CR-2026-09-12-006 | P3 / COMPAT（一致性） | **风标罗盘同一件物品的两处说明互相矛盾。** `SkyIslandCompassUsage.DisplaySettings`（玩家按住物品看到的「使用」说明）还写着批次二的旧口径「指向信鸽或下一个目标」，而物品描述与实际读数（`SkyIslandWorldStory.CompassReading`）早已是批次四的五级优先：蛙卵 → 信鸽 → 当前目标 / 支线 → 缺灯处 → 没采的风晶簇。 | **Fixed**。面板说明改为与读数、物品描述同一份口径 |
| CR-2026-09-12-007 | P3 / COMPAT（降级方向） | **灯的锚点缺失时「数得上、看不见、烤不着」，还替玩家把夜风关掉。** `SkyIslandFieldcraft.AddFire` 找不到锚点只打 WARNING 就返回：不建光、不进取暖列表；但 `lightsLit` 取自存档旗标（`SkyIslandLights.LitCount`），那一盏照样计入「十盏之后夜里不再起风」。降级方向与玩家直觉相反，也与云蚋那边「资源不对就硬失败」的工程口径相反。 | **Fixed**。新增 `missingFireAnchors` 记账，夜风改用 `NightWind(night, lightsLit - missingFireAnchors)`（真的建起来的盏数）；手记与面板仍显示存档记录的盏数。`SkyIslandContentWeaveGuard` 同步钉住新口径与记账 |
| CR-2026-09-12-008 | P3 / SAFE（文档事故） | **`SkyIslandGnats.Remove` 的 XML doc 被整块并进了 `PlayerCollider()` 的注释块**，一个注释块里连续两个 `<summary>`，`Remove`（击杀唯一上报点）本体没有文档。 | **Fixed**。把 `Remove` 的 doc 移回它自己头上 |
| CR-2026-09-12-009 | P3 / SAFE（开销，R-9 分项） | **天空岛两处每帧 Tick 把节流判断排在委托调用之后**：`SkyIslandEncounters.Tick` 与 `SkyIslandScavenging.Tick` 都写成 `closed \|\| !valid() \|\| Time.time < nextTick`，于是 0.25 s / `TickInterval` 的门明明能挡掉绝大多数帧，`IsSessionValid` 委托仍然每帧走一次。 | **Fixed**。两处改为 `closed \|\| Time.time < nextTick \|\| ...valid()`。`IsSessionValid` 是纯查询（只读状态与 `SceneLoader.IsSceneLoading`），顺序可换。帧时间收益**未实测** |
| CR-2026-09-12-010 | P3 / SAFE（开销，R-9 分项） | **`MultiSceneCore.ActiveSubSceneID` 的前缀补丁每帧把 `Scene` 装箱一次。** `SkyIslandSceneReferenceBridge.CoreActiveIdPrefix` 用 `activeSubSceneField.GetValue(core)` 读那个字段，`Scene` 是 struct，`FieldInfo.GetValue` 每次都产生一个装箱对象；而这个 getter 正是官方地图 / 迷雾 / HUD 每帧都问的那一个（同文件 `IsScene` 的注释已经点明这条是热路径）。 | **Fixed**。改用 `AccessTools.FieldRefAccess<MultiSceneCore, Scene>(activeSubSceneField)`（仓库既有写法，见 `DragonKingBossGunProjectileAgent`），**Harmony 绑定目标不变**：字段存在性仍由原来那行 `RequireField` 检查，官方改名时仍在同一处报同样的错。写入路径与加载 / 卸载路径未动。执行回归 `SkyIslandSceneReferenceBridge`（真 Harmony，47 条断言）通过；帧时间收益**未实测** |

### 本轮复核后**不成立**的外部报告条目

| 外部报告条目 | 复核结果 |
| --- | --- |
| P3-4「『数灯的孩子』伏笔没有专属落点，十盏灯的收尾词不点孩子的名」 | **不成立**。`SkyIslandLights.Capstone` 在 `HEAD` 上就是「十盏灯都亮了。**蛙鸣池边那个等灯的孩子**，终于数到了十——从今夜起，岛上的夜里不再起风，只有桥上还留着一点。」与 `Letter_04`（「数到十盏的时候，我就该回家了」）成对。无需改动 |
| P3-3「`SkyIslandCompassUsage` 用 `FindObjectOfType` 取会话」 | **不改**。报告自己也写了「仅按下时一次、无性能问题，属风格残留」。天空岛会话没有静态 `Current`，为此新开一个静态入口是为风格引入新的生命周期面；按「不做与任务无关的重构」保留 |

### UNVERIFIED / 待定位（不计入 confirmed）

| 线索 | 证据 | 现状 |
| --- | --- | --- |
| **Mode F 撤离后返回基地卡在 `LoadingScreen_Black` 约 290 秒** | 实机 F3：`MODE_F_EXTRACTION` FAIL（`triggered=True,resolved=True,still_active=False,base_ready=False`，等满 100 秒），随后 `MODE_ZOMBIE_EXTRACTION_RESTORE_ARENA`、`STANDARD_VICTORY_REWARD_RESTORE_ARENA` 各等 90 秒后报 `previous_scene_load_timeout`（`SceneLoader.IsSceneLoading` 一直为真），两个下游用例被迫 SKIP。`Player.log` 在切到 `LoadingScreen_Black` 后只有「Unloading 1660 unused Assets」，**没有任何异常**；场景最终在第四次等待窗口里自己走到了 `Base`。对照组：同一次运行的 `SCENE_RETURN_BASE`（同样是 `LoadBaseScene(null, true)`）10.5 秒通过。 | **未定位，不猜修**。已知差异只有「撤离路径在 `LoadBaseScene` 之前先调了 `LevelManager.NotifyEvacuated`（整档同步落盘 + 广播 `OnEvacuated`）」，而本机同时装着 5 个第三方 Mod 且其中数个订阅场景事件。官方 `SceneLoader` 的 async 状态机在反编译源码里没有方法体，静态读不出阻塞点。**需要一次带 `SceneLoader.IsSceneLoading` / `OnEvacuated` 订阅者计时的复跑**（最好在只装本 Mod 的干净档上）才能判定是本 Mod、宿主还是其它 Mod。`ModeFExtraction.TryLoadBaseSceneAfterModeFExtraction` 没有 `IsSceneLoading` 前置检查（F3 harness 自己的 `LoadScene` 有，并写明「Mode F 撤离可能已经启动返程，禁止叠加第二次加载」），这是复跑时第一个该验的假设——但在没有证据前不加这道门：误判时会把玩家留在竞技场里 |
| **Mode H 认证池只有 8–9 人时仍可能建不出某一场** | 用冻结数据穷举（`ModeHSeasonViabilityGuard` 会重算）：认证池 8 人（`MinProductionCandidateCount` 允许的下限，对手池只剩 3 人）时 **52.9%** 的合法抽签有一场建不出（第 6 场 900 / 第 4 场 432）；9 人时 **2.5%**（全在第 4 场）；**≥ 10 人时为 0**。CR-2026-09-12-003 的修复消灭了走廊空抽，但对手池本身太小时无解。 | **Needs owner confirmation**。三条互斥的路：① 把 `MinProductionCandidateCount` 从 8 抬到 10（代价：更多玩家的机器进不了 Mode H，走既有的退款离场）；② 放宽第 4 / 第 6 场走廊（改数值）；③ 让落选三席回到对手池（改「对手池 = 认证池 − 五席」的设计语义，第 5 场回响核心已经是这条的例外）。当前数量已由守卫冻成上界，数据改坏会转红 |
| **`plan_roster_no_legal_arrangement`（第 4 / 第 6 场）** | 同一份 500 组复现里剩下 36 组（7.2%）。与走廊那类不同，它**依赖签约组合**（roster veto 读的是签下这两位的公开原型），换一个替补就能换掉；执行回归已钉住「每个主将都至少有一个可签的替补」。 | **接受当前口径**：这是设计里「敌军组合不得同时封死你全部原型」的正常拒绝，玩家有可走的路 |

## 2026-09-12（第二轮）关闭历史 Open 项：1 P1 + 1 孪生缺陷

owner 指示「以最佳代码规范修复所有已知问题，达到生产水准」后，把 `FIX_TRACKER.md` 与本文件里
仍标 Open / Deferred 的条目逐条过了一遍。结果：

| 条目 | 处理 |
| --- | --- |
| `CR-2026-09-11-019`（P1，Open） | **已修**，见上表。复查时确认**邀请函返还是同一缺陷的孪生体**（`InstantiateSync` 返回 null 时照样清事务状态），一并修掉 |
| `CR-2026-09-11-017`（P1，Partially fixed / Deferred） | **已修**。跨重启窗口经复查是结构性关闭的（资产屏障 → typed pending → `SaveFile` 的顺序），本轮把这条屏障补进守卫。详见上表 |
| `CR-2026-08-31-009`、`CR-2026-09-01-010`（各 P1/P2，Open） | **代码侧无待办**，两条都标着「修复与静态验证已完成，等下一份完整 F3 报告确认」。本轮无法启动游戏，因此保持 Open；**不能**在没有新报告的情况下改判 Fixed |
| Mode H `woundedUnits` / 底色 / 怪癖倍率「owner 决策项」 | **不是缺陷**。逐条反查：`woundedUnits` → `publicSummary.visibleWoundedEnemyCount` → `ModeHOddsController.ComputeEnemyStatusScore`，权重在 `Assets/Data/ModeH/OddsWeights.json` 的 `enemyWeights` 里有实值（`woundedEnemy: -5` 等），并由「行为已实测」门控（`HasVerifiedInjuryBehavior`）。原记录说的是「没有擅自再补新数值」，不是「没接线」 |
| 鸭皇图鉴「全收集」跟随 Boss 筛选器 | **维持现状**（见 2026-09-12 第一轮的取舍表）。改成按未过滤池计会让永久禁用某只 Boss 的玩家**永远拿不到**这条里程碑——与「确保所有玩法都可实现」直接冲突 |

顺带修掉的文档缺陷（`SAFE`）：`docs/reference/Bossrush使用物品ID表.md` 里有两处**空表**——丧尸模式一节后面
挂着一个没有标题也没有数据的表头，以及一节写着「占位 ID（1 个）」却零行。台账是 TypeID 的事实源，
空表会让人以为漏登记。改成如实登记的「保留空洞（2 个，不回填、不复用）」，写明 `500009` 与
`500047`（后者的来历：被删功能竞速试炼门票的残留常量，已从 `Config/ConfigItemIds.cs` 移除）。

## 2026-09-12 近两月新增内容的「可玩性」审核：2 P2（均已修）+ 2 条登记在案的取舍

本轮只问三个问题：**新内容玩家有没有理由去碰**、**碰了之后逻辑走不走得通**、**承诺的玩法是不是真能做到**。
范围是 `c7cc4c1`（2026-07-11）以来的全部新系统。前几轮审核已经把「承诺没有生产写入 / 先记账后交付 / 失败不可恢复」
这一类问题清干净了，本轮确认的两条都属于**另一类**：代码没错，但**这块内容对玩家没有意义**。
证据 L1（读代码与逐条复算）+ L2（守卫、执行回归与破坏探针），**无实机**。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-12-001 | P2 / COMPAT（内容设计） | **两个纯演出的随机事件会吃掉玩家的单局事件配额。** 频率档（低 2 / 中 3 / 高 5，`RandomEventsTuning.MaxEventsPerRunByFrequency`）是玩家为「会发生点什么」留的额度，而 `TryStartRandomEvent` 对所有事件一律 `_eventsFiredThisRun++`。鸭王的烟花（权重 10）与鸭群巡游（权重 8）按设计就是零伤害、零影响、零奖励（玩家 Wiki 原文「就是好看」「这就是全部内容」；巡游的鸭子在 `OnCleanup` 里一律销毁，打死也没有掉落），合计占池 18/133 ≈ **13.5%**。低频档一局只有两次事件：**至少一次落空的概率约 25%，两次都落空约 1.8%**；落空的那次还照样占住并发槽（14 / 22 秒）与随后的 45–75 秒冷却。 | **Fixed（待实机）**。`RandomEventBase.ConsumesRunBudget` 默认 `true`（新事件默认占配额），两个纯演出事件覆写为 `false`；`TryStartRandomEvent` 改为 `_activeEventCounted = evt.ConsumesRunBudget;` 再按它推进计数，`TickEventActive` 的异步全失败退款分支仍按 `_activeEventCounted` 走（没计数就不会倒扣成负数）。并发恒 1、冷却与开局静默不变；**配额用尽后连纯演出也不再触发**，低频档仍是真安静。新守卫 `tests/RandomEventFlavorBudgetGuard.py`（5 条破坏探针全红）；中英文 Wiki 与在线站的「频率」一节改口径为「最多几次**有玩法的**事件」 |
| CR-2026-09-12-002 | P2 / COMPAT（内容设计） | **天空岛内容批次四里「把云蚋打下来」这条路零回报，于是为它做的两件东西无人会用。** 云蚋被打死不掉任何东西（`SkyIslandGnats.Remove` 只放一摊印子），全 Mod 也没有任何目标在统计击杀数。对照两件对策的成本：驱风香（云苔纤维 ×2 + 青穗草 ×2，材料价值 300）燃着时 `SkyIslandMosquitoRules.SpawnWeight` 直接返回 0——**整群都不来**，外加挡住一切风（`SkyIslandWarmth.Shelter`）与耐力恢复 +15%，持续 300 秒；风晶灭蚊灯（晴岚风晶 ×1 = 风晶碎片 ×5，加残铜片 ×3，材料价值 2790，两盏合 1395/盏）要先点亮两盏风晶灯才解锁配方，持续同样 300 秒，却把 12 米内的刷新权重 **×1.5**（`ZapperFactor`）主动招蚋，再以 3.2 米内每 0.7 秒一只慢慢电。即：**贵约 4.6 倍、解锁更晚、效果更差、还引来更多蚊子**。风灯的「招蚋 + 照着的不躲」同理没有兑现对象。这正是「为了堆内容而加」的形态：东西做完了，但玩家没有理由碰。 | **Fixed（待实机）**。把云蚋接进已有的航务委托，新增第四类 `SkyIslandBountyKind.Gnats`「夜里驱蚋」（`BaseGnatTarget = 12`，与另外三类同一条 +1 递增与同一档谢礼）。击杀只由 `SkyIslandGnats.Remove(killed: true)` 一处上报（枪 / 灭蚊灯 / 蒲扇三条路都汇到那里，`Dispose` 直接清数组因此离岛不算击杀）；可完成量走新拆出的 partial `SkyIslandSessionGnatBounty.cs`，不是夜里 / 没有蚊群一律 0（白天根本不挂出这一单），夜里按 `CullableBeforeDawn(RealSecondsUntilDawn(...))` 取保守下界，沿用「只派做得完的单」与退单出口。读钟仍只有 `SkyIslandLighting` 一处（新增 `ClockScale()` 读 `GameClock.clockTimeScale`，默认 60）。新守卫 `tests/SkyIslandGnatBountyGuard.py`（8 条破坏探针全红）；执行回归 `SkyIslandStory` 2444 → 2486 条；中英文 Wiki 与在线站同步 |

### 登记在案、本轮不改行为的两条取舍

| 事项 | 事实 | 结论 |
| --- | --- | --- |
| 鸭皇图鉴「全收集」里程碑（$1,000,000）跟着 Boss 筛选器走 | `CodexBossCatalog.AddOfficialEntries` 取的是 `GetFilteredEnemyPresets()`（**过滤后**的池），`IsFullyUnlocked` 遍历的就是这份加自定义 3 只、丧尸 5 只与存档历史条目。玩家把筛选器收窄到自己已经打过的 Boss，就能提前拿到全收集奖金（自定义与丧尸 8 只仍是硬门槛）。 | **保留当前口径**。反过来按未过滤池计，会让永久禁用某只 Boss 的玩家**永远拿不到**这条里程碑——可达性比防刷更重要，而收窄筛选器是玩家对自己存档的主动选择。登记为已知取舍，不作为 bug。 |
| 结局之后没打噬风时，鸣风栈道与桥恒为大风 | `stormPending = BothBeaconsLit && !StormResolved` 在敲钟结局后仍然成立，`WindLevel` 因此在那两处封顶到 2；大风下 `SpawnWeight` 返回 0，那两处夜里永远不刷云蚋，十盏灯「夜里不再起风」的收尾在那里也看不到。 | **保留**（原待拍板 #49）。复核 `SkyIslandSession.BeginStoryChallenge`：噬风的前置只有「双航标已亮且未击败」，**结局并不关掉这扇门**，所以这是玩家随时能自己解除的持续压力，不是死锁。观感待实机。 |

## 2026-09-11 本轮全面生产审核新增确认项

以下条目由本轮代码链路审核确认，证据为 L1/L2；“待实机”不等于已达到运行时生产标准。

| ID | 级别 / 分类 | 已确认缺陷 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-11-004 | P1 / COMPAT | 天空岛纪念品先记 `Keepsake_*` 再实例化物品；资源暂缺会永久记账但玩家未收到，后续也不会补发。 | Fixed（夹具/守卫通过，待编译与实机） |
| CR-2026-09-11-005 | P2 / COMPAT | 只读或写屏障失败时仍扣蛙卵材料，终点交付必失败且资源无效消耗。 | Fixed（夹具/守卫通过，待编译与实机） |
| CR-2026-09-11-006 | P1 / COMPAT | Mode H 第 4 场转会读取真实击败战报，但生产链路从未写入，市场报价永久为空。 | Fixed（17 条新夹具通过，待编译与实机） |
| CR-2026-09-11-007 | P1 / COMPAT | Mode H 将 profileId 当 archetypeId 传入阵容禁用判定，且敌军池未扣除五席，导致克制门与“撕票绝不返场/第 5 场回场签”契约失效。 | Fixed（夹具与相关守卫通过，待编译与实机） |
| CR-2026-09-11-008 | P1 / COMPAT | 遗种巢远征欠奖先删除/放生崽后再补发时无法取得血脉，发放函数仍推进游标并返回成功，遗种蛋永久丢失。 | Fixed（82 条事务断言与守卫通过，待编译与实机） |
| CR-2026-09-11-009 | P2 / OPERATIONAL | `run_guards.py --changed-only` 在 Windows 默认 GBK 下读取 Git 中文路径可能漏选改动。 | Fixed（87 条受影响守卫与专门回归通过） |
| CR-2026-09-11-010 | P2 / COMPAT | 展示柜只有登记入口，满 8 格后无法撤销/替换低品质收藏，早期选择会永久锁死后续升级。 | Fixed（82 条事务断言与可见替换/撤销入口通过，待实机） |
| CR-2026-09-11-011 | P2 / COMPAT | 鸭皇图鉴对所有受伤敌人开速杀计时，非 Boss 和长局容量处理不完整，可能清空仍在计时的 Boss。 | Fixed（仅 Boss 开表、死亡/补刀清理与容量回归通过；待编译与实机） |
| CR-2026-09-11-012 | P1 / COMPAT | Mode H 认证池在排除本季席位、保留回场签后，部分阵容无法构造完整六场；原流程仍可锁盘，后续才技术中止。 | Fixed（锁定前及转会接受前预构造剩余赛程；20 条市场断言、600/600 计划组合通过，待编译与实机） |
| CR-2026-09-11-013 | P1 / COMPAT | 随机事件的 Boss / 商人异步生成全部失败时仍占用一次自然事件名额并继续空转；旧回调还可能污染下一轮同类事件。 | Fixed（失败退款、冷却、上下文隔离夹具 4 场通过，待编译与实机） |
| CR-2026-09-11-014 | P2 / COMPAT | 图鉴的受伤与死亡入口对直接致死、死亡同帧回调和非主角补刀缺少一致的结束语义，可能漏记速杀或残留计时。 | Fixed（生产入口 guards 通过，带真实官方 Health 顺序的完整回归与实机待完成） |
| CR-2026-09-11-015 | P2 / SAFE | PetNest 中英文玩家 Wiki 把亡命档写成更快练级；按当前生产时长与经验折算，三档存活经验每小时相同，文案会诱导玩家承担无额外练级收益的死亡风险。 | Fixed（WikiContent 与在线站同步，静态链接待构建） |
| CR-2026-09-11-016 | P1 / COMPAT | 天空岛点亮风晶灯先记剧情和永久计数，再逐项扣材料；扣料或写屏障失败时会出现“灯已点亮但材料未完整消耗”的半提交。 | Fixed（全量材料预留→记录→提交事务，SkyIslandLoot 1817、SkyIslandStory 2444 与相关 guards 通过，待编译与实机） |
| CR-2026-09-11-017 | P1 / COMPAT | 天空岛纪念品的剧情手记与官方背包/仓库/待领取缓冲不是同一事务；交付在获得归属前抛错时可能烧掉补发资格，进程中断也可能留下「已记账、物品未持久化」。 | **Fixed（2026-09-12，待实机故障注入）**。①抛错分支此前已修：未归属实例精确回滚 `Keepsake_*`，已归属或有仓库缓冲回执则保留记录防重复。②**跨重启窗口经复查是结构性关闭的**：`GrantKeepsakes` 在 `TryGive` 之前调 `story.RequireAssetSnapshot`，失败即 `continue` 不发放；共享落盘引擎 `BossRushSaveCoordinatorEngine` 的顺序是「`CollectSnapshot`（把主角物品、生命、`PlayerStorage`、`PlayerStorageBuffer` 写进 ES3 缓存）→ `FlushPending`（手记进缓存）→ `SavesSystem.SaveFile(false)`」，任一步失败即 `Defer` 且**不落盘**，`assetSnapshotRequired` 只在 `OnPhysicalSaveSucceeded` 清除。手记在 `FlushPending` 之前只存在于 store 的内存队列，`TryGive` 内部又是同步的（`recordGrant()` 与 `SendToPlayer` 之间不让出主线程），因此不存在「手记落盘、物品没落盘」的交错。原判「无跨系统原子提交」低估了这条资产屏障。③本轮把天空岛补进 `tests/AssetSnapshotBoundaryGuard.py`（此前只盖 Mode H 与 PetNest，天空岛这条屏障没有守卫、可静默回退）：钉住「负判 + 失败跳过 + 之后才发放」、四样资产快照、`OnPhysicalSaveSucceeded` 清义务、`TryGive` 的记账/回滚顺序与引擎的三段顺序，5 条破坏探针全红。**剩余 L3**：实机故障注入（进程级中断）仍未做 |
| CR-2026-09-11-018 | P1 / COMPAT | ZombieMode 撤离现金结算忽略 `EconomyManager.Add` 失败返回值，仍清零本局净化点并继续清理场景；经济实例暂不可用时玩家已赚奖励永久丢失。 | Fixed（`SettleZombieModeExtractionCashShell` 仅成功清零；失败保留净化点并回到撤离机会；`ZombieModeCashAndOriginalExtractionGuard` / `ZombieModeExtractionCleanupGuard` 通过，待编译与实机） |
| CR-2026-09-11-019 | P1 / COMPAT | ZombieMode 入场现金退款同样忽略 `EconomyManager.Add` 失败，并在 `finally` 清除 `CashTemporarilyHeld` / `CashWithheldAmount`；切图或初始化失败时经济实例暂不可用会永久丢失已扣入场费。**复查时确认邀请函返还是同一个缺陷的孪生体**：`ItemAssetsCollection.InstantiateSync` 资源未就绪返回 null，`finally` 照样清 `InvitationTemporarilyHeld`，玩家花掉的尸潮邀请函同样永久蒸发。 | **Fixed（2026-09-12，待实机故障注入）**。owner 批准后按「可持久化退款欠账」实施：新增 `ZombieMode/ZombieModeEntryDebt.cs`（`SCHEMA+`，两个原始类型 key，老档缺键即 0，写入回读核对）。`RefundCash` / `RefundInvitation` 返回「了结了没有」——退成功或已记账都算了结，**连账都记不下才返回 false**，宿主据此保留事务状态待重试；结账先到账再销账（销账失败只重发、绝不吞），邀请函逐张推进。结账点为官方 `EconomyManager.OnEconomyManagerLoaded`（命名方法、幂等订阅、随模块销毁退订）与每次入场扣款之前。宿主 partial 只剩两行转发（AGENTS 4.15，`ZombieModeEntry.cs` 1279 → 1202 行，两个预算守卫回绿）。新守卫 `tests/ZombieModeEntryDebtGuard.py`（10 条破坏探针全红）+ 执行回归 `tests/fixtures/ZombieModeEntryDebt`（58 条断言，逐字链接生产文件）；契约登记见 `docs/contracts.md` §3 |

## 2026-09-11 天空岛内容批次四「云蚋」：接判夜与灶火时确认并修掉的既有缺陷 1 P2 + 1 P3（Fixed）

随内容批次四（夜里的蚊群「云蚋」，见 `FIX_TRACKER.md` 同日条目）把判夜收成一处、给灶火做看得见的火与烟时确认的**既有**问题（批次三 2026-09-11 引入）。
证据 L1（读代码与反编译源）+ L2（守卫与执行回归），**无实机**。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-11-002 | P2 / COMPAT（玩法） | **判夜两份口径，且没有官方时钟实例时整趟判成夜里**。光照 `SkyIslandLighting.Tick` 把 `GameClock.TimeOfDay.TotalHours` 直接交给 `ResolveTimeBlend`（`hours < 5 \|\| hours >= 21` 走星夜整档），夜风 `SkyIslandFieldcraft.IsNight` 另读一次交给 `SkyIslandFieldcraftRules.IsNight`（又写一份 21 / 5）。反编译源 `GameClock.cs` 的 `TimeOfDay` 由 `SecondsOfDay` 算出，**没有实例时恒为 00:00 而不抛异常**：两处都判成夜里——光照整趟星夜、夜风整趟起风，`IsNight` 外面那层 `try/catch`（注释写着「取不到官方时钟就按白天算」）兜不住。两份 21–5 各自维护，云蚋再抄第三份就会出现「天黑了、风没起、蚊子却来了」。 | **Fixed（待实机）**。新纯逻辑 `DebugAndTools/SkyIsland/SkyIslandNight.cs`：`IsNight(hours)`（非有限值不算夜里）与 `EffectiveHours(clockAvailable, clockHours)`（没有实例 → NaN）；运行时读钟只剩 `SkyIslandLighting.ClockHours()` 一处，光照、夜风、云蚋与蛙卵都经它判夜；Dev 构建另有 F3「强制夜里」（`#if BOSSRUSH_DEV`，只改本 Mod 读数，模块销毁复位）。不用官方 `TimeOfDayController.AtNight`（19–5 点，与岛上的光和风差两小时）。守卫 `SkyIslandMosquitoGuard` §1 钉住唯一读钟点、NaN 判定、全岛源码不出现 `AtNight`、开关只在 Dev 区块里写；执行回归 `SkyIslandLighting`（逐小时光照档与判夜一致、没有时钟回退晴昼、强制夜里与复位）与 `SkyIslandStory`（非有限值不算夜里）；反向探针见 `FIX_TRACKER.md` 同日条目。**实机没确认天空岛场景里到底有没有 `GameClock` 实例**：有实例时，除两份口径合一外行为与原来相同 |
| CR-2026-09-11-003 | P3 / COMPAT（表现） | **灶火锚点缺失时静默少一处火，而且灶火本身看不出是火**。`SkyIslandFieldcraft.AddFire` 找不到标记（布局改名或场景包不对）直接 `return`，那处的光与取暖一起消失、日志里没有一行；找得到时也只有一盏点光，走近看不出「这里生着火」——内容批次四要拿灶火的烟当云蚋的安全区，玩家得看得出烟在哪。 | **Fixed（待实机）**。锚点缺失打 `LogWarning`；三处灶火经新 `SkyIslandHearthFx` 在装置旁 2.4 m 生火苗与烟（落点复用经过几何回归的 `SkyIslandRewardCrate.TryFindCratePosition`，共享程序化粒子材质，不带碰撞体、不新增交互体），点光、取暖与驱蚋判定一并挪到火上；灶火与风晶灯分表（`hearthFires` 判烟 / `lampFires` 判招蚋）。守卫 `SkyIslandMosquitoGuard` §6 钉住警告、建火顺序与分表。火堆落点与烟的观感列在待人工验证清单 2.13.9 |

## 2026-09-11 天空岛内容批次三：顺带确认的既有经济风险 1 P1（Fixed，同日 owner 授权拍板后修）

随内容批次三（采集点 / 群岛材料 / 合成台 / 局内耗材 / 夜风，见 `FIX_TRACKER.md` 同日条目）做「每趟期望产出与经济对比」时，用离线解码的官方物品表复算搜刮池确认的**既有**问题（批次一 2026-09-09 物资搜集点引入）。
批次三没有修改箱子与物品池；**是否改、怎么改是经济决策（AGENTS §10），本条只登记，不改行为**。证据 L2（离线数据复算），无实机统计。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-11-001 | P1 / COMPAT（经济） | 星工遗存搜刮池与保底带里有官方「皇冠」（id 1254，Value 21,593,218，品质 7，标签 `Luxury` / `Helmat` / `DecorateEquipment` / `ShowCase`）：它不带 `LootExcludeTagPolicy` 的任何排除标签，也不在 `Assets/Data/LootBlacklist.json`，于是进了 `SkyIslandLootPools.GetBand`（`DebugAndTools/SkyIsland/SkyIslandLootPools.cs:44-91`）。岛上是**品质带内按物品种类均匀抽**（`SkyIslandRewardCrate.Fill`，`:160-161`），原版则按 `RandomContainer` 的品质权重抽。星工遗存带（4–8）共 189 种，每次抽取 1/189；一趟约 30 次 → 至少一顶皇冠约 **14.7%**（期望 0.16 顶 ≈ 342 万，按均值把一趟搜刮价值从约 32.5 万抬到约 375 万）；委托第三单与噬风战利品的保底带（6–8，40 种）每次 2.5%。 | **Fixed（待实机）**。owner 授权自行拍板（「好玩就行」）后选了「`GetBand` 加单件价值上限」：`SkyIslandLootTables.MaxPoolItemValue = 100000`，`SkyIslandLootPools.GetBand` 建池后 `RemoveAll` 掉官方价值超过上限的物品——皇冠 21,593,218、神秘钥匙 O 253,228 / X 151,675 出池，铜钱剑蓝图 66,666、纯金徽章 55,898 这类稀有大货保留。只影响天空岛的搜刮箱、委托谢礼与噬风战利品，不动全局掉落黑名单（许愿台、日报不受影响）。离线复算：星工遗存带均值不含皇冠约 7,196 → 约 5,100。守卫 `SkyIslandContentExpansionGuard` 钉住上限常量、读官方 prefab 价值与建池顺序，执行回归钉住 `AllowedInPool` 边界，反向探针见 `FIX_TRACKER.md` 同日「拍板」条目。原候选修法：`GetBand` 加单件价值上限；或把 1254 登记进掉落黑名单（会同时影响许愿台、日报等随机池，需一并评估）；或按原版 `RandomContainer` 权重抽（评估报告待拍板 #5）。见 `docs/reports/sky-island/天空岛_内容批次三_2026-09-11.md` 4.3 与待拍板 #24。数据：只读代理离线解码 `Duckov_Data/resources.assets` 的官方物品表（1,569 件，与各自预制体逐件一致）；官方 `ItemFilter` 的过滤函数不在反编译源里，「品质闭区间、requireTags 全含、excludeTags 全不含」是假设 |

## 2026-09-10 天空岛内容批次二：评估报告遗留项 1 P2 / 3 P3

随内容批次二（信鸽来信 / 秘境谜题 / 群岛手记 / 归航船名册 / 5 件天空岛物品，见 `FIX_TRACKER.md` 同日条目）一起修掉的评估报告第六节遗留项。
布局 v2 已由并行会话提交（`f64f4c1`），这些不再是「并行会话在改的文件」。行号是修复前 `HEAD`（`f64f4c1`）的。**全部为 L1 / L2 证据，无实机验证。**

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-10-036 | P2 / COMPAT | 折翎居民站在 `POI_F`（`SkyIslandResidents.cs:27`），他那一战的遭遇锚点却是 `EnemySpawn_F`（`Assets/Data/SkyIsland/World.json:17`），布局 v2 里两点相距 63.2 m（v1 为 7 m）；`SkyIslandEncounters.ChallengeRange = 70`（`:200`）正是为兜住这 63 m 而设。在折翎面前选「挑战」：本人隐藏，战斗体刷在约两屏之外再跑过来。旧腰牌纪念物挂在 `POI_F`，也不在他倒下的那一带。 | **Fixed（待实机）**；站位改为 `EnemySpawn_F`，旧腰牌纪念物跟着挪（不动 `World.json` 与布局）。`SkyIslandContentPackGuard` §1 按 `World.json` 读遭遇锚点、按 `layout.json` 算站位距离 ≤ 10 m，并核对腰牌同点；`SkyIslandPlaytimeFlowGuard` §2 钉新锚点；交互竞争属性测试复算新站位不抢交互。探针 P01 / P19 |
| CR-2026-09-10-037 | P3 / COMPAT | 航标撤离开放提示的英文漏了「站进绿环即可撤离」半句（`SkyIslandMapMarkers.cs:60`、`:63`）。 | **Fixed**；两条都补上。守卫 §7 数到两处，探针 P08 |
| CR-2026-09-10-038 | P3 / OPERATIONAL | v1 撤离口径残留：F3 面板说明「敲响归航钟后钟庭的绿环同样可用」（`SkyIslandControls.cs:31-32`，v2 双航标即开且有两处航标广场）、结局目标句「码头或钟庭返航」（`SkyIslandStoryRules.cs:261-262`）、`OFFICIAL_SCENE_CONTRACT.md:32`「钟庭绿环随敲钟结局出现」、爆炸遮挡补丁注释里的岛面高度仍是 v1 的 0 / 8 / … / 62（`SkyIslandExplosionObstaclePatch.cs:21`）。 | **Fixed**；四处改成 v2 口径，注释高度按 `layout.json` 实际值。守卫 §7 从 `layout.json` 计算期望高度行、禁止旧面板说明回潮、要求目标句含航标广场；探针 P07 |
| CR-2026-09-10-039 | P3 / COMPAT | 官方地图只圈主线目标，结局后什么都不圈（`SkyIslandMapMarkers.ObjectiveTargets`，`:70-80`）；噬风与四件支线物证从来不上地图，结局后「补齐支线」没有任何指引。 | **Fixed（待实机）**；新增 `SideTargets`：双航标且噬风未打时圈 `POI_E`（「可选挑战 · 噬风」），结局后圈未拿的支线物证（种植记录已拿未交时圈留言板），浅色区分；风标罗盘在主线目标为空时也指向这些点。守卫 §7，探针 P20 |

反向验证：本批 23 个探针全部转红（本表四条对应 P01 / P07 / P08 / P19 / P20，其余是批次二内容接线与执行回归的探针），执行回归的 7 个都红在预期断言上、无编译错误；
破坏做在仓库稀疏副本（`HEAD` + 本批文件）上，逐字节还原并 sha256 核对，真实工作区前后 sha256 不变。

## 2026-09-10 天空岛可玩性与时长评估：4 P2 / 3 P3

owner 预期「整座岛约 10 小时体验完」，要求判断这个预期、优化局内流程并修 bug，无人值守。结论是**不成立**（中值：主线约 21 分钟、全部内容各一次约 1.6 小时、含合理重复约 4.2 小时）；
时长模型、进度依赖图、流程问题、扩充方案与 13 项待拍板在 `docs/reports/sky-island/天空岛_可玩性与时长评估_2026-09-10.md`（local-only）。行号是修复前 `HEAD`（`977eff9`）的。
并行会话尚未提交的布局 v2（岛群压缩、航标撤离、官方地图标记）相关问题只记在评估报告第六节 R-1…R-14，不在本表。**全部为 L1 / L2 证据，无实机验证。**

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-10-029 | P2 / COMPAT | 战斗里了结的三件事的剧情回话被整句丢弃：会话在清场或噬风倒下时直接 `story.TryApply` 并扔掉 `message`（`SkyIslandSession.cs:1104-1106`、`:1125`），玩家只看到「航路已清理 · 折翎」。「航路交给你」（`SkyIslandStoryRules.cs:148`）、钟守松口（`:179`）、「噬风散了，钟守少了一个理由」（`:186`，噬风说服路线在场上唯一的提示）从来没有显示过。 | **Fixed（待实机）**；文案收成 `SkyIslandStoryRules.CombatOutcome`，`TryApply` 三个分支取它；`SkyIslandWorldStory.Tick` 按新增旗标经 `session.Announce` 读成字幕，进岛首帧不重播。`SkyIslandPlaytimeFlowGuard` §1、执行回归 `SkyIslandStory`（字幕与规则回话同源） |
| CR-2026-09-10-030 | P2 / COMPAT | 折翎旧腰牌纪念物读不到刻字：打赢折翎后他的剧情体不再露面，`DescribeNpc("sky_zheling")` 里那句刻字再无入口，纪念物面板只显示旅程摘要（`SkyIslandWorldStory.cs:384`、`:415`）——Wiki 承诺的「旧腰牌写着『航路交给你』，钟守认这份物证」读不到。 | **Fixed（待实机）**；`ZhelingBadgeText`（刻字 + 物证含义 + 摘要），`Beacon` 接受正文参数。守卫 §2 |
| CR-2026-09-10-031 | P3 / COMPAT | 「挑战旧航路守卫」「挑战守钟装置」「直面云海里的那阵风」三个选项先 `presentation.Dispose()` 再返回回执（`SkyIslandWorldStory.cs:309`、`:334`），按钮回调把回执交给 `SetBodyText`（`SkyIslandStoryPresentation.cs:395`），而它见正文已销毁直接返回（`:442`）——面板一关就没了下文。 | **Fixed（待实机）**；收起后经 `session.Announce` 读成字幕。守卫 §3 并禁止「收起后直接 return 回执」的写法 |
| CR-2026-09-10-032 | P2 / COMPAT | 8 个 `Search_*_02` 见闻点全部落进 `PointName` / `Lore` 的 default（`SkyIslandWorldStory.cs:42`、`:469-471`）：标题都是「阅读群岛见闻」、正文是同一段木牌文案。20 处见闻只有 13 种，而且没有一处给支线或机制线索（S4 观星镜此前只在钟守的拒绝文案里出现）。 | **Fixed（待实机）**；8 个专属标题与正文，各指向一处支线或机制（种植记录、委托规矩、眠苔、寄给折翎的旧信、噬风风眼躲法、航路图、瞭台守卫与观星镜、钟守要的证据）。守卫 §4（20 个键逐个有专属分支、正文两两不同）；`SkyIslandStoryPanelLayoutPropertyTest` 与 `SkyIslandLocalizationGuard` 复跑绿 |
| CR-2026-09-10-033 | P3 / COMPAT | 眠苔苔药的 5 分钟冷却走 `Time.unscaledTime`（`SkyIslandServices.cs:242`、`:244`、`:261`）：暂停菜单、拍照模式与剧情面板把 `timeScale` 压到 0 时冷却照走，开着暂停菜单挂 5 分钟就能再敷一副。 | **Fixed（待实机）**；改 `Time.time`。守卫 §5 |
| CR-2026-09-10-034 | P2 / COMPAT | 岛上每接受一条事实，下一个 45 m 内无敌人的帧就整档同步写盘：`SkyIslandStoryService.Tick`（`:255`）→ `BossRushSaveCoordinatorEngine.cs:269` → 官方 `SavesSystem.SaveFile`（反编译源 `Saves/SavesSystem.cs:503-520`：备份拷贝 + `ES3.StoreCachedFile` 整档写）。新存档从头玩下来，首次到访 12 区、首次清场 13 组、收录 20 处见闻就是四十多次，落在刚清完一组、刚踏上新岛的帧上。 | **Fixed（待实机）**；到访 / 清场 / 见闻去抖 30 秒（`FlushDebounceSeconds`），剧情动作与欠账重试立刻写，离岛、死亡与宿主销毁走 `TryClose` 绕闸全写。守卫 §6；执行回归（去抖窗口、战斗门、剧情动作带走攒着的事实、离岛全写、重入不丢）。帧时间收益未实测 |
| CR-2026-09-10-035 | P3 / COMPAT | 折翎「挑战旧航路守卫」紧挨「留下来谈」，打赢即永久关闭和解线（`ReconcileZheling` 对已了结的折翎直接拒绝），按钮上不说（`SkyIslandWorldStory.cs:317`）。 | **Fixed（待实机）**；标签写明「（战胜后不能再和解）」。守卫 §8 |

反向验证：30 个探针（守卫 24、执行回归 6）全部转红，执行回归 6 个都红在预期断言上、无编译错误；破坏做在仓库干净签出（`HEAD` + 本轮文件）上，逐字节还原并 sha256 核对，真实工作区前后 sha256 不变。

## 2026-09-10 天空岛全方位审核：3 P1 / 13 P2 / 5 P3

owner 要求对天空岛（晴岚群岛）做一次全方位审核并按严重度全部修复、无人值守。范围：生命周期与静态缓存、存档安全（含 F3 套件只读）、
原版契约（`HUDManager` 显隐、`EvacuationCountdownUI` / `CountDownArea` 时基、`UIInputManager`、`TimeScaleManager`）、每帧开销、
UI/UX 与指引（与官方 HUD 的遮挡按 UnityPy 读官方预制体实测）、本地化、守卫质量与文档一致性。行号是修复前 `HEAD`（`23cd133`）的。
完整记录（触发条件与后果、修法、证据级别、待拍板）在 `docs/reports/sky-island/天空岛_全方位审核_2026-09-10.md`（local-only）。
**全部为 L1 / L2 证据，无实机验证。**

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-10-008 | **P1** / COMPAT | 撤离读秒与腾空救援走 `unscaledTime`（`SkyIslandSession.cs` HEAD:562、603–604、611、619）。官方 `TimeScaleManager` 在暂停菜单与拍照模式时把 `timeScale` 压到 0、`CountDownArea` 按 `Time.time` 计时（`CountDownArea.cs:27`）；本岛只加了 `View.ActiveView` 门，而 `PauseMenu` 是 `UIPanel` 不是 `View`——**开着暂停菜单或拍照模式站在撤离圈里 3 秒就被送回基地**，腾空时开暂停菜单会被「救」回落脚点。 | **Fixed（待实机）**；停留秒数 `extractionHeld` 按 `Time.deltaTime` 累加（带 View 门），腾空计时改 `Time.time`，剧情面板那一支不再顺延。`SkyIslandPlayerEntryGuard` / `SkyIslandLifecycleGuard` 按行钉时基，9 个变异探针转红。 |
| CR-2026-09-10-009 | **P1** / COMPAT | 噬风战利品箱与它自己的尸体箱同点：官方 `CharacterMainControl.OnDead` 在倒下位置生成尸体箱（`CharacterMainControl.cs:1304`），会话在同一坐标 `DropTrophy`（HEAD:1017）。`CA_Interact` 按到交互体轴心的距离**严格小于**取唯一目标（`CA_Interact.cs:78`），两箱几乎重合时其中一个整局选不中——保底星工遗存箱可能拿不到。 | **Fixed（待实机）**；落点走 `SkyIslandRewardCrate.TryFindCratePosition` 退开一个交互间距，退不开才落原点。内容守卫钉住。 |
| CR-2026-09-10-010 | **P1** / SAFE | F3 岛内套件**并非只读**：会话外壳沿用主套件——开场 `WriteRunMarker`（内部 `SavesSystem.SaveFile`，出击途中绕过战斗落盘门写盘，`F3GameplayValidationExecution.cs` HEAD:32）、每帧 `ProtectCurrentPlayer` 给玩家无敌并回满血（HEAD:41、187，紧接着要人工验的噬风伤害与苔药计价全被污染）、抑制 Mod 提示条（HEAD:56）、收尾跑全宿主 `ValidationSafeCleanup`（HEAD:163）。 | **Fixed（待实机）**；岛内模式四处全部门控，`SESSION` 行改报 `read_only=true`，收尾只清自己写过的标记。`SkyIslandValidationSuiteGuard` 重写，对应探针全部转红。 |
| CR-2026-09-10-011 | P2 / COMPAT | 区域判定取「最近地标 60 m」（HEAD:632–653）：按作者布局导航网格复算，码头以外七个主岛只有 30–53% 的可走面判到本岛，其余地方卡片停在上一个岛；CS1 桥 71%、FS3 桥 61% 的桥面隔岸点亮 S1 / S3 迷雾并推进「巡视群岛区域」。注释里的「(50, 73.2) 可用区间」只量了桥头到对岸地标，`SkyIslandContentExpansionGuard`（HEAD:553–556）又把这份错误几何钉成了断言。 | **Fixed（待实机）**；改为脚下地面碰撞体 `COL_Ground_{区域}` 判定（每 0.2 s 本就打的地面射线顺手查表），区域名与到访记账同源，桥上保持上一个。新增 `tests/SkyIslandRegionResolutionPropertyTest.py`：12 区域 100% 解析到本岛、桥面零泄漏，内置 3 条反向探针。 |
| CR-2026-09-10-012 | P2 / COMPAT | 巡岛可完成量按 `POI_` 节点计（HEAD:912），装饰节点 `POI_B_Mural` 恒为「未访问」：还剩 3 个真区域时可完成量算成 4，**恰好派得出一张做不完的「巡视群岛区域 ×4」**。CR-2026-09-10-001「不会派出做不完的单」的论证只算了走遍全岛。 | **Fixed（待实机）**；改数地面切分出的区域表；F3 `SKY_BOUNTY_GATING` 加「可完成量 ≤ 区域数」。 |
| CR-2026-09-10-013 | P2 / COMPAT | 字幕不分级（`SkyIslandHud.cs` HEAD:359）：噬风相位台词（说完 1.4 s 后圈内吃满伤害）按普通字幕排队，要等上一条停满再淡出；队满丢最旧，三条普通字幕就能把它挤掉。 | **Fixed（待实机）**；排队规则抽成纯逻辑 `SkyIslandCaptionQueue`（警示插队、打断普通字幕、队满先丢普通字幕），相位台词走警示通道；新增执行回归 `SkyIslandHudPolicy`。 |
| CR-2026-09-10-014 | P2 / COMPAT | `CampaignHud` 不跟随官方 HUD 显隐（HEAD:79）：它在 `HudOverlay`（1200），官方背包、地图、对话画在 sortingOrder 100（UnityPy 读 `resources.assets`），契约追踪条压在它们上面，天空岛出击里同样出现。 | **Fixed（待实机）**；判定收成 `BossRushUI.IsOfficialHudHidden()`，天空岛 HUD 与契约追踪条共用一份。 |
| CR-2026-09-10-015 | P2 / COMPAT | 剧情面板键盘导航一按跳两格（`SkyIslandStoryPresentation.cs` HEAD:527）：官方 `UIInputManager` 把 UI_Navigate 的 started / performed / canceled 全订上且不看阶段（`UIInputManager.cs:461–463`），W/S 一按两条同向事件。注释与文档还宣称「手柄支持」，而官方输入资产 `Duckov Controls` 没有任何手柄绑定。 | **Fixed（待实机）**；按边沿走一步、回中位重新武装；选项回执重开保留当前项；注释、Wiki、清单改为键盘口径。 |
| CR-2026-09-10-016 | P2 / COMPAT | HUD 与官方 HUD 遮挡（UnityPy 读官方预制体，本库 1 单位 = 官方 4/3 单位）：字幕中心 y=-330、中心轴心、最高 96（HEAD:92、233），两行时整段压住搜箱开门时的交互读条 `ActionProgress_Slider`（顶边 243）；右上卡片顶边写死 110（HEAD:54，`CampaignHud` 同），玩家展开官方「操作说明」提示栈（11 行）后被整块盖住。 | **Fixed（待实机）**；字幕底边钉在距底 254、封顶两行，大标题上移到 -150；卡片每 0.25 s 按 `IndicatorHUD` 实际下沿重排（`BossRushUI.GetTopRightHudTop`）。`SkyIslandHudGuard` 按常量复算纵向避让。 |
| CR-2026-09-10-017 | P2 / COMPAT | 渡口整备入列门只看 `UseDurability`（`SkyIslandServices.cs` HEAD:200）：药品、食物这类用耐久记剩余次数的物品也被按维修价补满次数，官方维修台对它们显示「无法维修」（`ItemRepairView.cs:65–74`）。 | **Fixed（待实机）**；门改为 `item.Repairable && item.MaxDurabilityWithLoss >= 1f`，与官方一致。 |
| CR-2026-09-10-018 | P2 / OPERATIONAL | F3 岛内用例判据缺陷：`SKY_RESIDENTS` 按地形根扫交互体（HEAD:567），居民不挂在地形根下，**只要有居民在岛就必然假红**；`SKY_INTERACTION_SEPARATION` 漏了居民、又把子物体碰撞体并进包围盒（HEAD:271）；`SKY_STORY_OBJECTIVE` 拿缓存与规则比（恒等，HEAD:434）；`SKY_STORY_CODEC` 往返只比数组长度（HEAD:460）；`SKY_SCAVENGE_PLACEMENT` 一个箱都没建、`SKY_PANEL_ART` 一张图都没部署时记 PASS；`SKY_EXTRACTION_OFFICIAL_UI` 不查实例所属场景（官方静态实例从不清空）。 | **Fixed**（判据；待实机跑）；逐条改正，判据不成立时记 SKIP；`SkyIslandFullAuditGuard` 钉住。 |
| CR-2026-09-10-019 | P2 / SAFE | F3 岛内用例的副作用与错误归因：`SKY_PANEL_ART` 在玩家帧上同步解码约 20 MB 插图、并把查不到的 null 永久缓存（此后整局面板无图）；会话途中结束（撤离、倒下、换槽）后剩余用例在没有岛的环境里红一片，`SKY_FINAL_SESSION_INTACT` 还记成「套件结束了这趟出击」；开跑时玩家自己开着的面板被算成套件漏租约。 | **Fixed**；`SkyIslandUiArt.HasArt` 只读探测；`RunSkyIslandSync` / `RunSkyIslandCase` 会话门记 SKIP；启动门拒绝开着模态面板或官方界面；租约按开跑基线比。 |
| CR-2026-09-10-020 | P2 / COMPAT | 桥口木牌英文关闭态 6–7 行、约 3.3–3.9 单位高，木板只有 2.8（`SkyIslandGates.cs` HEAD:99 固定 4.4 号、关自动缩放），英文整段溢出木板。 | **Fixed（待实机）**；自动缩放 2.4–4.4 + 省略号兜底，中文观感不变；`SkyIslandFullAuditGuard` 钉住。 |
| CR-2026-09-10-021 | P2 / COMPAT | 失败提示把异常原文拼给玩家（`SkyIslandSession.cs` HEAD:173、360，`SkyIslandEncounters.cs` HEAD:239、308）：异常原文按维护语言是中文，英文玩家看到半句读不懂的中文——与 CR-2026-09-09-011 同类，F3 英文用例扫不到这条路径。 | **Fixed**；`SkyIslandStoryRules.WithDetail`：英文界面只给前缀并指向 Player.log，原文进日志；执行回归钉住出口，`SkyIslandFullAuditGuard` 钉住调用点（审核后又补修同类 4 处：生成敌人、前往下一个地标、切换光色、打开地图失败）。 |
| CR-2026-09-10-022 | P2 / OPERATIONAL | 文档与实现不一致：撤离环实为青色（`BossRushUIColors.Accent`）而 F3 面板、中英 Wiki、覆盖清单写「蓝环」；`OFFICIAL_SCENE_CONTRACT.md:31`、本文件、repowiki 两处仍写「未复用官方倒计时控件」（`a613774` 已复用）；Wiki 写目标在屏幕上方状态行（实为右侧卡片）、`M_SKY_ISLAND_01` 仍写 F6 旅程图；人工清单写 F3 自动断言 26 / 27 条（实为 28）。 | **Fixed**；逐处改正，`SkyIslandFullAuditGuard` 钉住旧说法不回潮。 |
| CR-2026-09-10-023 | P2 / OPERATIONAL | `SkyIslandValidationSuiteGuard` 挡不住它声称要挡的破坏：在审核开始时的代码副本上跑 22 个变异探针，除 3 个对照探针正常转红外，**其余 19 个破坏全部让它保持全绿**——调用前加空格绕过禁用清单、`&& false` 废掉专用测试档门、`if (false)` 包住用例仍算已执行、恒真 lambda 换掉判据、同名局部变量冒充静态类、`0x4E001` 以 `0x4E00` 开头骗过子串比对、观测面方法里顺手写字段。 | **Fixed**；重写为结构判断（规范空白、切方法体、完整语句匹配、字段写入与调用白名单），重做的 40 个探针全部转红。 |
| CR-2026-09-10-024 | P3 / COMPAT | 表现层生命周期：剧情面板用 Unity 的 `!= null` 判隐藏令牌（HEAD:262、572），画布先被销毁时漏注销；选项回执重开面板丢键盘当前项；玩家倒下或开始切图时不收官方读条（ready 落下后停在屏幕上读到 00:00）；离圈后文字读秒最多残留 0.5 s；暂停菜单开着时 HUD 的字幕与大标题照常在背后播完。 | **Fixed（待实机）**。 |
| CR-2026-09-10-025 | P3 / COMPAT | 开销：浮空提示字每帧量距离并按 1% 阈值重写 TMP 顶点色（走过一次淡变带重建上百次）；`LandmarkLabel` 每 0.5 s new 两个八元素数组、`FieldStatus` 与 `CurrentObjective` 每 0.5 s 重拼字符串；`SkyIslandSceneReferenceBridge.IsScene` 挂在全局补丁热路径上，每次读 `scene.path` 新建托管字符串（任何地图都在跑）。 | **Fixed**；距离检查按接近速度推迟、alpha 按 5% 量化；输入不变复用字符串；`IsScene` 先比 `buildIndex` 再比句柄缓存；`SkyIslandHudGuard` / `SkyIslandFullAuditGuard` 钉住。 |
| CR-2026-09-10-026 | P3 / COMPAT | 奖励箱：`InstantiateSync` 缺资源时的 `FallbackItem` 带着**同一个 TypeID**（`ItemStatsSystem/ItemAssetsCollection.cs`），`TypeID` 回读挡不住（`AGENTS.md` 旧记录有误）；物资池查询失败的空结果被永久缓存（本进程之后每趟出击的箱子都是空的）；一件都没装进去的箱子仍报「建成」，委托谢礼被消耗。 | **Fixed**；实例化前 `GetPrefab`；只缓存完整查询；空箱收回并返回 false。 |
| CR-2026-09-10-027 | P3 / OPERATIONAL | F3 杂项：必到遭遇点标记缺失时静默跳过（少证一件事仍 PASS）；探路等待期间场景卸载后再读 `target.name` 二次抛；英文用例手写 13 个见闻点，漏掉全部 `_02` 点位；注释与实现不符（「30 秒」实为 60、「12 个地标」实为 13、「码位写反斜杠转义」实为整数常量）。 | **Fixed**；`SkyIslandFullAuditGuard` 钉住。 |
| CR-2026-09-10-028 | P3 / COMPAT | 英文术语与语法：Sky Island / Sky Islands 混用；钟庭名 Bell Court 与 Homecoming Bell Court 混用；「1 pieces」「1 seconds」；「the Silent Bell Keeper」带小写冠词直接当面板标题与血条名；留言板交单时仍写「left at her feet」；`Island story ·` 交互名；腰牌文字与 Wiki「the route is yours now」不一致。 | **Fixed**；`SkyIslandFullAuditGuard` 钉住。 |

**核实后不是缺陷**（依据见审核文档）：岛上官方 `EvacuationCountdownUI` 实例存在（LevelManager 预制体随 `LevelConfig.Awake` 实例化）；
撤离环画得出来（`Sprites/Default` 恒在包内）；落地点在 `POI_A` 60 m 内（44.6 m）；默认键位下交互键与确认键不会同帧撞车；
隐藏令牌泄漏不会让官方 HUD 永久隐藏（`ShouldDisplay` 只数未销毁的令牌）；CR-2026-09-10-003 的 `TimeOfDayConfig` 常驻副本没有全局副作用
（UnityPy 读 `Base.unity` 与 16 张 `*_Main` 场景：子树只有 `TimeOfDayConfig` + 5 个 `TimeOfDayEntry`，无 Volume / Light / Update 逻辑，
`lookDevVolume` 指向子树外且只在编辑器读；租约注释已据此更正）。

**不在本轮范围**：试验场 / 石堡前哨 `ArenaPrototypeSession` 的撤离读秒同样走 `unscaledTime` 且不看官方界面（`ArenaPrototypeSession.cs:389`；已单独修复，见 `FIX_TRACKER.md`「2026-09-10 石堡前哨撤离读秒对齐官方 CountDownArea」）。

验证：正式构建（`Build/bossrush.rsp` 无 `/define`）绿并部署，SHA-256 源与游戏目录一致；全量守卫 585 个中本轮改动相关的全绿
（另 2 个红项来自并行会话未提交的布局改动，换回 `HEAD` 版本复跑均绿；并行改动落地前全量 584 / 0）；
`run_runtime_regressions --filter SkyIsland` 8/8；142 个变异探针（含新增守卫 `tests/SkyIslandFullAuditGuard.py` 的 34 个）在仓库稀疏副本上逐条转红、逐字节还原并 sha256 核对，真实工作区前后不变。
未进游戏、未读写存档。

## 2026-09-10 碰撞排障：1 P1（替换件是空气墙、附加件能穿过去）

owner 首次进岛看得见地形之后反馈「有些模型玩家可以穿过去，有些则有空气墙」。
离线把作者 FBX 里的 `COL_*` 碰撞盒与 Tripo 模型的真实包围盒逐个对照（必须用节点的完整 TRS：
FBX 导出带 scale 100 与绕 X −90°，只看局部尺寸会得到退化盒），两种现象各有一个成因。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-10-007 | P1 / COMPAT | ①**空气墙**：Tripo 替换件沿用 layout 登记的**名义尺寸**出碰撞盒，而模型被 `FOOTPRINT` 归一化后往往远小于名义尺寸——单边空隙中位 1.80 m，最严重的 `H_HomecomingBell` 盒 34×26 m、模型只有 4.4×6.0 m，玩家离钟十几米就被挡住。②**穿模**：`ANCHORS` 附加件是新增几何，layout 里没有对应障碍，**一个碰撞盒都没有**，建筑、树干、石柱都能直接穿过去。编译、guard 与判包都只看得到「盒存在」，看不到盒与模型对不上。 | **Fixed（待实机）**；`tools/sky_island_tripo_props.py` 新增 `collision_fit()`：替换件的盒收敛到模型绕 Y 旋转后的真实 XZ 投影，**只缩不放**（52 个替换件里 31 个被收敛）；新增 `COLLISION_POLICY` 给附加件按件补盒——实体件（建筑、亭子、平台、大石）按真实投影再内收 `COLLISION_INSET` 0.15 m，树与柱只挡干心（`('trunk', r)`，树冠不挡），可以走上去的石阶与贴路/墙角的语义分支小件（路灯、长椅、旗杆、花箱、桶箱、板车）一律不补。碰撞盒 88 → 161，**美术零改动**（MeshRenderer 769 / Material 96 / Texture2D 73 / MeshCollider 28 与上一版一致），导航 4037 顶点未变（导航按 `layout['obstacles']` 的名义尺寸挖洞，本修复不动那份数据）。**已知取舍**：新增的 73 个盒全部落在导航网格上，A* 不认这些盒，敌人可能贴着新碰撞打滑——实机第一优先观察项；正解是把附加件登记进 layout 让导航重新挖洞，但导航 4037/4095 只剩 58 顶点余量，做不了。回退：清空 `COLLISION_POLICY` 后重打包。 |

## 2026-09-10 地形全黑排障：1 P0（鸭科夫跑在 URP Deferred，自研着色器没有 GBuffer pass）

owner 首次成功进岛（`Player.log` 10:38 那次全程无报错：`RENDER_READY materials=0 textured=420
ground=27 walls=89`、`ENTER_PASS nodes=4215`、`SEARCH_COMPLETE`、`POINT_OPENED` 都正常），
但截图里**整张地形一片漆黑**——人能走、能搜点、能开箱、地图未探索灰度也正常，就是看不见地。

**离线逐项排除**（UnityPy 直读包与游戏自带场景）：变体没被剥（三个着色器都有 d3d11 编译产物）、
材质 `_BaseColor` 非黑且贴图挂着、769 个 `MeshRenderer` 全部 `m_Enabled=1`、
合批网格 71 张与子网格数自洽、根节点 `SkyIslandWorld` 在 (0,0,0) 单位变换、
相机 `cullingMask` 含 Ground(7)/Wall(6)、`EPOURP.dll` 只是描边 RendererFeature 不是管线替换。

**决定性 A/B**：同一套 `SkyIslandRendering.Apply` 代码路径下，石堡前哨原型
（`Build/arena_prototype_ingame.png`，材质全部转成官方 `SodaCraft/SodaCharacter`）**在游戏里渲染完好**；
天空岛包 90/96 个材质用自研着色器，`materials=0` 表示一个都没转——**没转的那批全部不可见**。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-10-006 | **P0** / COMPAT ✅**已实机验证** | 三个自研着色器（`BossRush/SkyIsland/{Environment,Water,Cloud}`）只声明了 `UniversalForwardOnly`，**没有 `UniversalGBuffer` pass**。而鸭科夫跑在 **URP Deferred** 下——两条独立证据：①官方 `SodaCraft/SodaLit`、`SodaLit_EdgeLight`、`SodaLit_Mask2`、`SodaLit_Blend2`、`SodaLit_EdgeLight_Mask` 这一整套**世界**着色器的 pass 只有 `ShadowCaster` / `DepthOnly` / `DepthNormals` / `UniversalGBuffer`，**一条前向 pass 都没有**（前向模式下它们会全部画不出来，所以官方不可能是 Forward）；②`globalgamemanagers` 的 always-included 里躺着 `Hidden/Universal Render Pipeline/StencilDeferred`，那是 Deferred 专属。角色用的 `SodaCraft/SodaCharacter` 同时有 `UniversalForward` **和** `UniversalGBuffer`，所以官方预制体（敌人、战利品箱）照常可见——这正是「只有地形黑」的原因。作者工程本身不在 Deferred 下预览，整条链上编译、guard、判包、离线回归**都证明不了这件事**。 | **Fixed（已实机验证 2026-09-10 11:48）**；给 Environment / Cloud 各加一条 `UniversalGBuffer` pass，Water 的 `UsePass` 同步加一条。口径抄官方 URP `Terrain/Details`（同样是「自己算完光照再进 GBuffer」）：SubShader 标签加 `UniversalMaterialType="Unlit"`，GBuffer 片元把自算颜色当 `globalIllumination` 写进 GBuffer3、albedo 置 0、`kLightingInvalid`，延迟光照阶段按模板跳过这些像素，画风与前向路径完全一致。原 `UniversalForwardOnly` 改成 `UniversalForward`——前者在延迟下**也会被画**，与 GBuffer 撞成双绘（URP `UniversalRenderer.cs` 注释把这个明确列为 ERROR）。重打包后 Environment 的 d3d11 程序数 20 → 38，pass 3/2/2 → 4/3/3；**美术零改动**（GameObject 982 / MeshRenderer 769 / Material 96 / Texture2D 73 / 碰撞体 28+88 / 顶点 2,483,428 与上一版逐项一致）。`tools/verify_sky_island_bundle_shaders.py` 新增 `UniversalGBuffer` 判据，用旧包实测形状反向验证：旧形状 3/3 判红、新形状 3/3 判绿。另加一次性运行时诊断 `SkyIslandRendering.LogDiagnostics`（`RENDER_DIAG`，激活后与导航扫描后各一次），万一还黑可一次定位是被剔除还是画成黑。**实机结果（`Player.log` 11:48）**：owner 确认地形正常显示；日志全程零报错，`晴岚群岛已就绪` → `ENTER_PASS nodes=4215 …` → `SEARCH_COMPLETE` → `CLEANUP reason=raid_unloaded` 一条链完整，`RENDER_DIAG` 打出 `passes=UniversalForward|UniversalGBuffer|SHADOWCASTER|DepthOnly`、`pipeline=UniversalRenderPipelineAsset`、`lightingEnabled=1 sun=(1.00,0.82,0.62) ambient=(0.26,0.33,0.45)`。**注意 `visible=` 这一项在这两个采样点不可信**——`activated` 与 `post_scan` 都还在读条幕布后面，相机尚未渲染过 raid 场景，成功这局它照样是 `False`，别拿它当判据。 |

## 2026-09-10 船点入口误报排障：1 P2（`基地船点入口未找到` 是假警报）

owner 追问「进不去天空岛」时顺带查到：同一份 `Player.log` 里除了着色器那条 P0，还有一条
`[BossRush][CRITICAL] [SkyIsland] 基地船点入口未找到`。`FIX_TRACKER.md` 上一轮把它挂起为
「等进岛跑通后确认是不是失败返航的连带影响」——**不是**，两者无关。

用 UnityPy 直读游戏自带场景定位到官方船点的真实位置（build index 取自 `globalgamemanagers`
的场景表，逐个 `levelN` 回读根节点确认）：

- build index 5 = `Base_SceneV2`（主城区）：**一个含 `Boat` 的节点都没有**，
  `Ship/ShipCompute|ShipDir|ShipPower|…/Interact` 全是造船台，不是出击点。
- build index 6 = `Base_SceneV2_Sub_01`：`Envir/Prfb_BoatBetweenBaseAndFarm/Interact`，
  同层挂着官方自己的 `Interact_Challenge` / `Interact_SnowChallenge`——这才是出击船点。

而三份 Player.log（2026-08-29、2026-09-10 两次）里 `Base_SceneV2_Sub_01` **从未出现过**：
玩家没走到码头，那张按需加载的子场景就不在场，扫描当然扫不到。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-10-005 | P2 / OPERATIONAL ✅**已实机验证** | `SkyIslandRuntimeModule.OnUpdate` 的船点重试窗口（12 次 × 1 s）跑空后**无条件**发 `CriticalLog("sky-island-entry-missing")`，提示语还断言「请检查基地船点初始化」。但官方出击船点在按需加载的 `Base_SceneV2_Sub_01` 里，玩家站在主城区时它本来就不在场，**跑空是正常状态**，于是每次进/回基地都误报一次 CRITICAL。危害有二：①真故障（船点在场却挂不上）与正常状态在日志里长得一模一样；②本次排障就被它带偏过，一度据此判定「正式构建里玩家看不到航路入口」。**功能本身没坏**：`ModBehaviour.OnSceneLoaded` 订阅的是 `SceneManager.sceneLoaded`（`Integration/BossRushIntegration_StartAndScene.cs:95`），对**任何**场景加载都会转发给 RuntimeModule，玩家走到码头触发子场景加载时 `ScheduleEntry` 会重新武装 12 次重试并挂上入口。 | **Fixed**；扫描循环命中 `IsBaseHubBoatInteractable` 时记 `boatSeen`，跑空后分两路：没见过船点只发 `DevLog`（`[Conditional("BOSSRUSH_DEV")]`，正式构建整句编译掉），见过船点却注入失败才发 CRITICAL，口径改为「已找到基地船点但航路子交互注入失败」。`tests/SkyIslandPlayerEntryGuard.py` 加三条**结构**断言（命中赋值 / 跑空分支形状 / CRITICAL 必须排在 `if (!boatSeen)` 之后），3 条人为破坏逐条转红并按字节还原（sha256 `d8dd6780…` 前后一致）。**实机结果（`Player.log` 11:48）**：CRITICAL 消失，改为出现 `[SkyIsland] 基地船点所在子场景尚未加载，暂不挂航路入口；走到码头会自动重试。` |

**顺带更正一条既有记载**：上一轮台账担心「包里只有 d3d11 变体，玩家 `-force-vulkan` 会重现同一个
`isSupported == false`」。用 UnityPy 读游戏自带的 `globalgamemanagers.assets`，**官方 43 个着色器
`platforms` 全是 `(4,)`（D3D11）**——游戏本体就是 D3D11-only，强制别的 API 会先把原版打死。
天空岛包的变体集与游戏完全一致，这条风险不成立。

## 2026-09-10 实机日志排障：3 P0（天空岛 100% 进不去，三个独立成因）

owner 报「进不去天空岛」，依据是 `%LocalLow%/TeamSoda/Duckov/Player.log`（2026-09-10 08:38 那次）与
前一份 `Player-prev.log`（2026-09-09）。两份日志同一条报错、同一处栈帧，是**必现**而不是偶发：

```
[SkyIsland] RAID_ASSEMBLY_FAILED System.InvalidOperationException: 独立关卡配置、天气或 MultiSceneCore 缺失/禁用
  at BossRush.SkyIslandRaidLease.OnSceneLoaded (...)
[SkyIsland] 天空岛创建失败：独立关卡配置、天气或 MultiSceneCore 缺失/禁用
```

场景包本身没问题：用 UnityPy 1.25.3 直读**已部署**的 `Assets/arenas/sky_island_raid`，
`SkyIslandLevel`（`m_IsActive=False`）上 `LevelConfig` 与 `MultiSceneCore` 都在且 `m_Enabled=1`，
`subScenes` 唯一且 `sceneID=BossRush_SkyIsland`、`cachedLocations` 含 `StartPoints/PlayerSpawn`、
`cachedTeleporters=[]`，`SkyIslandLocations/StartPoints/PlayerSpawn` 与地形根 `PlayerSpawn` 坐标一致
（均 z=-326），`Exit` / `Navigation` 齐备。合同前后各项判据实测都能过。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-10-002 | **P0** / COMPAT | `SkyIslandRaidLease.OnSceneLoaded` 把官方天气与起始 Buff 的注入排在了 `SkyIslandOfficialContract.VerifyBeforeActivation` **之后**，而该合同的判据里就有 `config.timeOfDayConfig == null \|\| config.startBuffPrefabs == null`。这两项**永远不可能来自场景包**：官方 `TimeOfDayConfig` 是运行时对象（实为场景 `MonoBehaviour`，见 CR-2026-09-10-003），作者工程引用不到（UnityPy 读回包内该字段为 `{FileID 0, PathID 0}`），`startBuffPrefabs` 更是连字段都没序列化进去。于是合同必然在第二条判据上判死，**天空岛 100% 进不去**，且四种成因合并成同一句提示，日志上看起来像「场景包坏了」。判据是 2026-09-09 `d9ae590` 随激活前合同一起引入的，此前的包同样进不去（`Player-prev.log` 两次尝试同一条错）。 | **Fixed**（待实机）；注入提到合同之前，合同仍严格早于 `services.SetActive(true)`，语义不变（现在这两条 null 判据真正校验的是「注入落地了没有」）。同时把「配置/Core 缺失」与「天气/起始 Buff 未注入」拆成两条提示，两种完全不同的故障不再同形。`SkyIslandOfficialContractGuard` 新增顺序断言；`tests/fixtures/SkyIslandRaidLease` 的合同替身现在如实记录**被调用当刻**的装配状态并断言之——把顺序改回去，守卫与夹具双双转红（已逐条反向验证并按字节还原）。 |

**为什么离线设施此前没抓到**：`tests/fixtures/SkyIslandOfficialContract` 的 `LevelConfig` 替身把
`timeOfDayConfig` / `startBuffPrefabs` 默认初始化成非空，等于替身场景「天生就装配好了」——
合同的这两条判据在夹具里永远走不到红。这次的修法把「注入发生在合同之前」变成夹具的显式断言，
而不是继续依赖替身的默认值。

### 第二发：官方天气本身是**场景组件**，出图即销毁

修好顺序后再进一次，报错换成了新拆出来的那条口径 `独立关卡天气或起始 Buff 未注入`
（`Player.log` 2026-09-10 09:00 那次）——`startBuffPrefabs` 是当场 new 的，不可能为空，
所以判死的一定是 `timeOfDayConfig`，即注入进去的值本身就是空。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-10-003 | **P0** / COMPAT | 官方 `TimeOfDayConfig` 与它引用的 5 个 `TimeOfDayEntry` **都是 `MonoBehaviour`，不是 ScriptableObject 资产**（`鸭科夫源码/TeamSoda.Duckov.Core/TimeOfDayConfig.cs:7`、`TimeOfDayEntry.cs:6`）。UnityPy 读官方 `level3`(Base) 与 `level13`(GroundZero) 确认层级完全一致：`LevelConfig / TimeOfDayConfig / TimeOfDay_{Default,Cloudy,Rainy,Storm_I,Storm_II}`——**天气是每张地图自己场景里的对象**。`SkyIslandSession.Build` 在基地取 `LevelConfig.Instance.timeOfDayConfig` 交给租约长期持有，而官方 `SceneLoader` 会先卸载基地再加载天空岛：等注入时那个组件早已随基地场景销毁，Unity 重载的 `==` 判它为 null，合同照样判死。**天空岛仍然 100% 进不去**，且与 CR-2026-09-10-002 是两个独立成因（顺序修好只是让它露出来）。 | **Fixed**（待实机）；`Prepare` 改为在**还在基地时** `Object.Instantiate(template)` 克隆整棵子树并 `DontDestroyOnLoad`：子树内的 config→entry 引用由 Instantiate 重映射，跨出子树的只剩 `VolumeProfile`（`TimeOfDayPhase` 是 `[Serializable] struct`，只含 tag + VolumeProfile 资产），本来就是共享资产。副本在唯一收口 `TryRelease` 销毁，`Object.Destroy` 对已销毁对象幂等。`SkyIslandSession` 顺带不再把基地组件存成字段。 |

**夹具为什么放过它**：`tests/fixtures/SkyIslandRaidLease` 的 `UnityEngine.Object` 替身没有模拟
Unity 那条「已销毁对象 `== null`」的重载，也没有任何一步模拟「基地场景卸载」——
在夹具眼里被销毁的组件依然是个好端端的托管对象。现在替身补了 `==`/`!=` 重载与
「销毁 GameObject 连同组件一起销毁」，`New()` 在 `Prepare` 之后**必定**销毁模板，
把实机时序如实搬进夹具：改回「原样持有基地实例」，
`lease assembles official services after session owner disappears` 当场转红。

### 第三发：场景包里的自研着色器**没有任何编译产物**

天气修好后再进一次（`Player.log` 09:24），合同全过、场景激活、官方关卡开始初始化，
死在更后面的材质装配：`天空岛创建失败：天空岛专用着色器不受当前显卡支持：BossRush/SkyIsland/Environment`。
显卡是 Intel Iris Xe / D3D11 11.1，跑得动原版全图，不可能不支持一个 `#pragma target 3.5` 的 URP 着色器。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-10-004 | **P0** / OPERATIONAL | 作者工程 `GraphicsSettings.m_CustomRenderPipeline = 0`，六个画质档里当前档（Ultra）的 `customRenderPipeline` 也是 0，其余五档指向的 GUID 在工程里**根本不存在**（是从游戏工程抄来的 QualitySettings 残留）。于是 URP 的 `ShaderScriptableStripper` 在打包时拿不到任何 URP 资产，`CanRemoveVariant` 的 `supportedFeaturesList` 为空列表、循环一次不进、`removeInput` 保持 true —— **三个自研着色器的变体被全部剥离**。构建日志的证据是 `After scriptable stripping: 0` 与 `d3d11 (total internal programs: 0, unique: 0)`；UnityPy 读已部署的包，`Environment`/`Cloud` 的 SubShader **pass 数为 0**（`Water` 是 `UsePass` 型，指向 Environment，一并失效）。而 90/96 个材质用的就是 `Environment`。包能构建、能加载、能读回场景路径，作者侧全 PASS，玩家一进岛必炸。日志里那句 `Build Finished, Result: Failure.` 在 2026-09-09 的构建里就有，当时被判为噪声（见本文件上一节的部署台账注记）——它不是噪声。 | **Fixed**（待实机）；作者工程 `Assets/UniversalRenderPipelineGlobalSettings.asset` 的 `m_StripUnusedVariants` 改 0，并把 `GraphicsSettings.m_CustomRenderPipeline` 指向工程里已有的 `Assets/SkyIsland/SkyIslandPreviewPipeline.asset`（**只改后者不够也只改前者不够**：单改 strip 开关重打包，日志仍是 `After scriptable stripping: 0`）。重打包后 `Environment` 20 个 d3d11 程序 / 3 pass、`Cloud` 8 个 / 2 pass、`Water` 2 pass（UsePass 无自身程序，属正常）。UnityPy 逐项对比新旧包：GameObject 982、MeshRenderer 769、Material 96、Texture2D 73、碰撞体 28/88、全部标记数、网格总顶点 2,483,428 **完全一致**，唯一差异就是着色器 pass 从 0 变成有。新增 `tools/verify_sky_island_bundle_shaders.py` 作为重打包后的强制闸门。 |

**为什么这一条编译和守卫永远抓不到**：它只存在于 99 MB 的二进制包里，而包是 local-only 不进 git。
仓库侧唯一能做的就是**在部署前用 UnityPy 判包**——`tools/verify_sky_island_bundle_shaders.py`
就是把本轮的手工排查固化下来（判据：三个着色器各自 pass 数 > 0）。它的红态在本轮是**实测过**的：
同一脚本读旧包时 `Environment`/`Cloud` 都是 `passes=0`。

## 2026-09-10 天空岛验收设施轮：0 新 confirmed，3 条既有 finding 补 L2 证据

本轮是**验收设施轮**，不是审查轮：目标是把「离线能证的部分证完，剩下必须人工的变成一张可执行清单」。
**没有开游戏、没有做任何游戏内测试、没有读写玩家存档**，因此没有一条结论标 L3，
下面三条既有 finding 的 `Fixed（待实机）` 状态**一概不动**，只是各自多了一层此前没有的离线/岛内证据。
完整交付说明见 `docs/reports/sky-island/天空岛优化_交付报告.md`，逐条人工步骤见 `docs/guides/sky-island/天空岛_待人工验证清单.md`。

| 既有 ID | 本轮补的证据 | 状态 |
| --- | --- | --- |
| CR-2026-09-09-004 / -005（同点交互竞争 / U1） | 新增 `tests/SkyIslandInteractionCompetitionPropertyTest.py`：用真实作者几何算出**全部静态交互体**（见闻点 20 / 纪念物 7 / 航路图 2 / 搜刮箱 39 / 谢礼箱 3 / 居民 6 = 77 个）的世界坐标，两两比触发体积共 **2926 对，零重叠**，最紧一对余量 0.60 m（`search:Search_D` ↔ 它自己的纪念物，3.20 m vs 需 2.60 m）。此前只有文本守卫钉住「代码里写了退避」，没人算过跨系统的组合——而 `SkyIslandRewardCrate.TryFindCratePosition` **本来就不做「与其它交互体净空」检查**。另在岛内 F3 套件补 `SKY_INTERACTION_SEPARATION`，按 `Collider.bounds` 实测真实尺寸（离线拿不到官方 `InteractableLootbox` 预制体的 collider）。6 条人为破坏 5 条转红（第 2 条是探针挑错，见交付报告如实记录）。 | **Fixed（待实机）不变** |
| CR-2026-09-09-006（天空岛爆炸穿墙） | 此前 `armed` / `armedSceneHandle` 是私有字段，**人在岛内也无法确认补丁真的挂上了**。新增只读 `SkyIslandExplosionObstaclePatch.IsArmedFor(scene)`（判据与 `Prefix` 前两行逐字一致）与岛内用例 `SKY_EXPLOSION_PATCH`，同时钉住 `FlattenHeight(0.2, 0.6) == 0.5`（与原版平地逐位一致）。 | **Fixed（待实机）不变** |
| CR-2026-09-09-007（噬风相位提速复利） | 此前只有 `SkyIslandContentExpansionGuard` 的**文本**断言（禁止 `/=` 自乘形式）。新增岛内用例 `SKY_STORM_TUNING` 做**数值**断言：四档阈值严格递减、`PhaseSpeedup` 单调不减且封顶 `MaxPhaseSpeedup`、末档等于封顶值、`PhaseForFraction` 逐档对齐，并把「逃出第一圈所需速度」= `PulseRadius / PulseTelegraph` 写进 metrics（当前 5.00 m/s）。**跑不跑得掉仍必须实机**。 | **Fixed（待实机）不变** |

新增守卫 `tests/SkyIslandValidationSuiteGuard.py` 钉住验收套件本身的三条纪律
（能在岛内跑 / 全程只读 / 判据不成立时记 SKIP 不记 PASS），10 条人为破坏逐条转红并按字节还原。

### 本轮新增 confirmed：1 P3

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-10-001 | P3 / COMPAT | 场景包里有 **13 个 `POI_` 前缀节点**而不是 12：多出来的 `POI_B_Mural`（风铃集壁画）是地形根的直接子物体、单位缩放零旋转，因此 `SkyIslandSession.PrepareMarkers` 会把它收进 `landmarks`。后果有二：①`AvailableBountyProgress(SkyIslandBountyKind.Survey)` 遍历 landmarks 取 `name.Substring(4)` 查 `SkyIslandStoryService.RegionBit`，`"B_Mural"` 未登记、返回 0，于是 `HasVisitedRegion` 恒为 false，**「巡视群岛区域」的可完成量永久多算 1**；②`SkyIslandSession.LandmarkLabel("POI_B_Mural")` 按 `name[4]=='B'` 落到「风铃集」，与 `POI_B` 显示同名。用 UnityPy 读**已构建的 bundle** 确认，**新旧两个包都有**该节点，非本轮美术改动引入。 | **Fixed**（2026-09-10 全方位审核，见 CR-2026-09-10-012：下面「不会派出做不完的单」的论证只算了走遍全岛——还剩 3 个真区域时可完成量算成 4，恰好派得出一张 ×4；巡岛可完成量已改为数地面切分出的区域）。原状态：Open（P3，有意不修）；影响有限：`TargetFor(Survey)` 基线为 4，1 < 4，走遍全岛后这类委托只是不再派出，**不会派出做不完的单**；`RecordRegionVisited("B_Mural")` 同样因 bit==0 返回 false，不污染存档。修它要动 `PrepareMarkers` 的收录口径或给 `RegionBit` 加白名单，代价大于收益。**已连带修正**新 F3 用例 `SKY_MARKERS`：原断言 `landmarks == 12` 会在完全健康的包上假红，改为断言 `POI_A..H` / `POI_S1..S4` 十二个区域标记**各自存在**，多余 POI_ 节点只写进 metrics。 |

## 2026-09-09 天空岛全面审核（第三轮）：1 P1 / 2 P2 / 6 P3

范围是天空岛的**本地化覆盖、撤离点可发现性与每帧运行开销**，与同日「内容扩充」「进出岛流程」
「可玩性复审」三轮不重叠。依据：`DebugAndTools/SkyIsland/` 全部 33 个源文件（6750 行）、
`Assets/Data/SkyIsland/World.json`、`ArtSource/SkyIsland/layout.json` 的实际坐标、
`tools/generate_sky_island.py` 的标记生成方式，以及官方 `ItemRepairView` / `ExplosionManager`
反编译源码。基线三绿（编译 / 15 守卫 / 7 回归）后开审，全部为静态确认，**无实机验证**。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-09-011 | P1 / COMPAT | 天空岛叙事层**整层没有英文**：剧情面板、选项标签、HUD 目标行与物资/委托行、桥口木牌、居民服务回话、委托名与进度——约 250 条玩家可见文案硬编码中文，`SkyIslandWorldStory`（84）/ `StoryRules`（45）/ `StoryService`（27）/ `Services`（25）/ `Gates`（18）/ `Bounty`（15）六个文件零 `L10n.T`。而 `SkyIslandBounty.NameEn` 早已写好却**零调用**，`SkyIslandWorldStory` 也直接用 `TierNameCn` 绕过了 `L10n.T`——英文本就在范围内（全 Mod 约 2700 处 `L10n.T`，Campaign 叙事同样走它；`WikiContent/en/map__sky_island.md` 按英文正文维护），只是接线漏了。 | Fixed（待实机）；**274 条成对文案**全部走 `L10n.T`，地名人名沿用在线 Wiki 已发布的英文；`SkyIslandBounty.Name()` 成为委托名唯一取用点，居民名复用 `SkyIslandWorldStory.ResidentName()`。诊断文本（`Debug.Log*` / `DevLog` / `CriticalLog` / `throw` / `lease.Abort`）按「维护语言中文」保留。新守卫 `tests/SkyIslandLocalizationGuard.py`，4 条人为破坏逐条转红并按字节还原。 |
| CR-2026-09-09-012 | P2 / COMPAT | 撤离点**没有任何视觉标识**，但三处文案承诺「蓝环 / 绿环」：`Exit` / `BellExtraction` 在作者场景里只是 Blender Empty（`generate_sky_island.py` 的 `marker()`），自绘地图删除后官方小地图也不标撤离点（Wiki 自己写「撤离点不在地图上标注」）。文案是从石堡前哨/竞技场原型抄来的，那两张图的环画在场景包里。码头那个尚可（离 `PlayerSpawn` 13 m），**钟庭那个是终章后才开放、离最近地标 14.4 m、无标识无地图点**。 | Fixed（待实机）；新增 `SkyIslandExtractionRings`（`SkyIslandGroundRing.cs`）真的画出两个圈：半径即 `ExtractionRadius`（圈内即判定内）、按地面射线吸附落点、钟庭环与 `BellExitIfUnlocked()` 同一事实源、无碰撞体、`Apply` 只在解锁翻转时动一次。F3 面板与中英 Wiki 四处文案同步改为「站进撤离环」。 |
| CR-2026-09-09-014 | P2 / COMPAT | `SkyIslandRuntimeModule.OnUpdate` 每帧调 `SceneManager.GetActiveScene().name`。`Scene.name` 每次调用都新建托管字符串，而该模块在**所有场景**每帧都跑（基地 + 每张官方出击图 + 每场 BossRush），节流判断还排在它后面。项目自己在 `Campaign/CampaignFinalBoss.cs:103` 把同一模式标注成「每帧产生垃圾（AGENTS.md 4.12）」并用场景代数缓存解决过。 | Fixed；改为按 `Scene.handle`（结构体整数，不分配）缓存，并在 `OnStartedLoading` 与 `ScheduleEntry` 两处作废缓存兜住句柄复用。同时把 `SkyIslandEncounters` 每帧路径上的 `Find(e => ...)` / `Exists(e => ...)` 闭包换成显式循环。 |
| CR-2026-09-09-013 | P3 / COMPAT | 折翎战败后剧情体会**短暂重新现身**：`SetVisible` 由 `!IsBusy && !ZhelingDefeated` 驱动，两个条件延迟不同——最后一名倒下的那一帧 `IsBusy` 即转 false，而持久 flag 要等 `encounters.Tick`（0.25 s 节流）提交并被存档接受。存档有写屏障时 flag 永远落不下来，即**持久可见**——正是 CR-2026-09-09 那轮想消灭的画面在故障路径上的残留。 | Fixed（待实机）；改为单一事实源 `SkyIslandEncounters.HasStarted(id)`（`Started \|\| Cleared`）。钟守不动：他的战斗对象是「失控的守钟装置」，人本就该在战斗后回来。`SkyIslandContentExpansionGuard` 同步改钉新表达式并加反例。 |
| CR-2026-09-09-015 | P3 / COMPAT | `SkyIslandLighting.Tick()` 每帧重写 6 个 `VolumeParameter.Override`、3 个 `RenderSettings.ambient*Color`（Trilight 下每次赋值让 Unity 重算环境球）与 4 个 `Shader.SetGlobal*`；**锁定预设时这些值恒定却照写**，自动档每帧的增量也远在感知阈之下。 | Fixed；加感知阈以下的变化门（颜色 0.002 ≈ 8 位色 0.5 级、强度 0.002、太阳角 0.05°，实测最快过渡约 0.0044°/帧），切档用 `dirty` 强制落地。**未实测帧率**，只做纸面推算。 |
| CR-2026-09-09-016 | P3 / COMPAT | `SkyIslandStormBoss` 的预警圈用 `renderer.material`——官方文档明说这种副本要调用方自己销毁，于是**每次脉冲泄漏一份材质**；`ResetStaticCaches` 又只把 `ringMaterial` 置 null 而不 `Destroy`，与同目录 `SkyIslandRendering.Dispose` 的口径不一致。 | Fixed；贴地圆环的建造收敛到共享 `SkyIslandGroundRing`（撤离环与预警圈共用），改用 `sharedMaterial`（颜色走 LineRenderer 顶点色，共用不影响各自上色），`ResetStaticCaches` 真的 `Destroy`。 |
| CR-2026-09-09-017 | P3 / COMPAT | HUD「物资 已搜/可搜」的分母包含建箱失败的点：`PlacedPoints` 不排除 `Failed`，而同类的 `AvailablePoints` 排除了。玩家搜完全岛仍会看到 37/39，像是漏了两处。 | Fixed；`PlacedPoints` 改为 `Placed && !Failed`，与 `AvailablePoints` 同口径。 |
| CR-2026-09-09-018 | P3 / OPERATIONAL | 三个成员零调用（生产、守卫、夹具、工具全查过）：`SkyIslandBounty.NameEn`、`SkyIslandContent.TierName`、`SkyIslandMapFog.LayerCount`（注释写「给守卫与验收日志用」，两边都没人读）。 | Fixed；`NameEn` 经 `Name()` 接线（见 011）、`TierName` 删除、`LayerCount` 接进 `ENTER_PASS` 日志。 |
| CR-2026-09-09-019 | P3 / SAFE | 两处陈旧注释：`SkyIslandServices` 写「眠苔那副 **120** 的苔药」（实际 `HealPriceFull = 480` 且按缺失比例计价）；`SkyIslandSession.Cleanup` 的「本局属性加成必须在离岛时摘掉」挂在 `Safe("map_fog", ...)` 上，实际 owner 是下一行的 `Safe("services", ...)`。 | Fixed；两处改正。 |

**审核中确认无问题的部分**（都实查过，不列为 finding）：TypeID 未新增；事件订阅全部幂等且有退订；
`Modifier` 非 `[Serializable]`，归航菜加成不会随官方角色保存带回基地；`StableHash` 恒非负，
`PresetIndex` 不会越界；`KnownFlags = 65535` 已含 `StormSlain`；`RepairPriceFor` 与官方
`ItemRepairView.CalculateRepairPrice` 逐行一致；爆炸补丁签名与层掩码与官方 `CheckObsticle` 一致；
三处挑战的 90 m 距离门实算最远 51.2 m；纪念物/航路图 3.2 m 退避实算最近邻 ≥4.70 m；
委托三类可完成量的单调递减推理成立（`Survey` 第三单会被正确挡住）；
地图边界 475/425 对实际岛体 ~397/375 留有余量。

验证：Windows Release `compile_official.bat` 绿并部署；全量 **579 个守卫**绿（新增
`SkyIslandLocalizationGuard.py`，`SkyIslandPlayabilityGuard` 扩到 12 组）；
`run_runtime_regressions --filter SkyIsland` 7/7 绿；两组守卫共 **11 条人为破坏反向验证**
逐条转红并按字节还原。**未进游戏 smoke**，逐条实机步骤见 `Assets/Data/GameplayCoverage.json`
的 `M_SKY_ISLAND_08` / `M_SKY_ISLAND_09`。未提交、未推送、未发布 Workshop。

## 2026-09-09 天空岛可玩性复审（第二轮）：1 P1 / 5 P2 / 2 P3

范围是天空岛的**交互可达性与官方语义**，与同日「内容扩充」「进出岛流程」两轮不重叠。
依据：`DebugAndTools/SkyIsland/` 全部源码、`Assets/Data/SkyIsland/World.json`、
`ArtSource/SkyIsland/layout.json` 的实际海拔与坐标，以及官方
`CA_Interact.SearchInteractableAround` / `ExplosionManager.CreateExplosion` 的反编译源码。
全部为静态确认，**无实机验证**。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-09-004 | P1 / COMPAT | 完成纪念物与装置**同点**：`SkyIslandWorldStory.Beacon` 把 `*_Completed` 交互体建在 `point.position`，与 `PrepareMarkers` 挂在同一标记上的见闻交互体世界坐标完全相同。官方 `CA_Interact.SearchInteractableAround` 按「玩家到 collider 距离**严格小于**」挑唯一目标，距离相等时由 `OverlapSphereNonAlloc` 返回顺序决定，且两者不在同一交互组、滚轮切不过去。Search_D / Search_G 上挂着 K1 / K2，Search_E 挂着 K3 与噬风，Search_H 挂着敲响归航钟 —— 被只读纪念牌盖住即捷径与终章永久不可达。 | Fixed（待实机）；纪念物退开 `SkyIslandRewardCrate.InteractableSeparation = 3.2f`，落点复用经真实几何回归的 `TryFindCratePosition`，方位角取标记名 `StableHash`；无净空时只留光、不挂交互体。`SkyIslandPlayabilityGuard` 钉住并反向验证转红。 |
| CR-2026-09-09-005 | P2 / COMPAT | 航路图交互体与见闻点同点：`SkyIslandGuideInteractable.Attach` 用 `localPosition = Vector3.zero` 挂在 `Search_A_02` / `Search_B_02`，而这两个标记本身已有见闻交互体。同 004 机制，手柄玩家的地图与光色入口和见闻点必有一个按不到。 | Fixed（待实机）；改走 `GuideOffset()`，与 004 共用间距常量与放置算法；地面校验失败时退回纯方位角偏移而不是放弃入口。 |
| CR-2026-09-09-006 | P2 / WIRE+ | 官方爆炸遮挡在天空岛恒失效：`ExplosionManager.CheckObsticle` 把射线起终点 y **硬编码为 0.5**（按原版地面 y≈0 写的齐腰视线）。天空岛 12 个岛在 y = 0/8/16/18/24/26/28/42/44/52/62，除码头外无任何地面在 0.5 附近，射线恒打空、遮挡恒为「无」。叠加官方 `CreateExplosion` 无距离衰减 ⇒ 噬风三段脉冲与双方全部手雷、套装反击爆炸**穿墙打满**。 | Fixed（待实机）；新增 `SkyIslandExplosionObstaclePatch` 前缀，把两点压到 `min(startY,endY)+0.3`（y=0 平地上还原官方 0.5，逐位一致）。**只在天空岛生效**：`SkyIslandRaidLease` 按 raid 场景 `Arm`/`Disarm`，前缀先看 `armed` 再比对活动场景句柄，未武装直接交还原方法。夹具补两条生命周期断言，去掉 `Arm` 实测转红。 |
| CR-2026-09-09-007 | P2 / COMPAT | 噬风相位提速**复利**：`EnterPhase` 每档 `/= 1.25f`。相位由 2 档增到 4 档后累计 1.25⁴ ≈ 2.44，再叠档次自带 1.7 倍，末段反应时间只有官方拾荒者的 1/4.15 —— 没有反应窗口。 | Fixed（待实机）；`Bind` 记下基线（此刻 `ApplyAi` 已跑过，基线含档次倍率），按 `PhaseSpeedup(phase)` 算绝对值赋回，四档线性摊到 `MaxPhaseSpeedup = 1.6` 封顶，重复进档不再叠加。守卫禁止 `/=` 自乘形式。 |
| CR-2026-09-09-008 | P2 / COMPAT | 钟守物证路线只认 `ZhelingReconciled`：但同一份内容表里 `ZhelingPass` 门是 `ZhelingReconciled \| ZhelingDefeated` **任一即开**。选择挑战折翎的玩家被永久关在物证路线之外，而失败提示仍要求他去「与折翎和解」—— 一个已不可能达成的条件（`ReconcileZheling` 对已了结的折翎直接拒绝）。 | Fixed（待实机）；改判 `source.ZhelingResolved`，提示同步为「和解或战胜都算」。中英 Wiki 与 repowiki 同步。 |
| CR-2026-09-09-009 | P2 / COMPAT | 存档落盘门是全图口径：`story.Tick(!encounters.HasLivingEnemies)`。同日自动组改为按出击刷新后，全岛几乎总有活敌，等于把落盘门永久关上——已接受的剧情事实只能等离岛或死亡才写盘，中途崩溃全丢。 | Fixed（待实机）；改为 `HasLivingEnemiesWithin(玩家位置, SaveQuietRadius = 45)`，保留「不在交火帧写盘」本意。守卫禁止全图口径复现。 |
| CR-2026-09-09-010 | P3 / COMPAT | 遭遇 preset 与身份无关：`sources[i % sources.Count]` 让全岛 13 组的带队者永远是按名字排序的第一个 preset，第二名永远是第二个；`sources` 通常有十几种官方拾荒者。 | Fixed（待实机）；改走 `PresetIndex(id, index)` = `StableHash(id + "#" + index) % sources.Count`。不用 `string.GetHashCode`：Mono 与 .NET Core 口径不同会让不同机器同一处刷出不同敌人。 |
| CR-2026-09-09-011 | P3 / COMPAT | 剧情面板选项不随状态刷新：`SkyIslandStoryPresentation.Show` 只在打开那一刻烘成按钮。接完委托，三个「接委托」按钮仍在，再点只得到「手头这一单还没交」；交完单，派单选项要退出面板再进才看得到。 | Fixed（待实机）；`SkyIslandWorldStory` 增加 `reopen` + `Refreshed()`，任何成功改变状态的选项都重开当前页，本次返回文案写进新面板正文；`Dispose` 放开 `reopen` 捕获的说话人引用。 |

同轮修正的**文档失配**（不计 finding，随代码一并改）：玩家 Wiki 中英两份写「状态行在屏幕下方」（HUD 实为顶部锚定）、
「地图标出撤离点位置与直线距离」（改用官方 M 键地图后无任何代码绘制撤离点）、
「出击途中只能用随身现金结算」（`LevelConfig.accountAvailable` 序列化默认 true，银行存款可用）。
`wiki-site` 经 `scripts/sync-content.mjs` 重新生成。

验证：Windows Release 编译绿并部署；全量 **578** guard 绿；`run_runtime_regressions --filter SkyIsland` 7/7 绿。
新增 `tests/SkyIslandPlayabilityGuard.py`，**14 条人为破坏在沙箱副本上逐条转红并按字节还原**
（沙箱是为了不动到并行会话的工作区），其中一条专门验证注释剥离真的生效。
**无实机验证**：交互体退开后的实际选中优先级、爆炸遮挡在真实岛体碰撞（尤其斜坡与桥面）上的表现、
末相位手感、敌人外观区分度，全部仍需游戏内 smoke。

## 2026-09-09 天空岛进出岛流程对照复审：2 P2 / 1 P3

逐环节对照官方 `MapSelectionView.LoadTask` → `SceneLoader.LoadScene` → `LevelManager.InitLevel`、出口预制体 `CountDownArea` + `SceneLoaderProxy.Task`、`CharacterDieTask`（官方源码目录缺 async 正文，用 `.codex_tmp/core_decomp/` 的还原版）。切图骨架一致；入口不走官方地图板/费用/确认是 owner 明确保留的产品决定，不计 finding。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-09-001 | P2 / COMPAT | 撤离圈计时不看官方界面：官方 `CountDownArea.Update` 在 `View.ActiveView != null` 时不推进，天空岛 `Update` 只在自家 F6 地图/剧情面板可见时归零，玩家开着背包站在圈里 3 秒就被送回基地。锚点 `SkyIslandSession.cs` 撤离判定行。 | Fixed（待实机）；判定行加 `View.ActiveView == null` 门。守卫 `SkyIslandPlayerEntryGuard` 钉住该行；去掉门后实测转红。 |
| CR-2026-09-09-002 | P2 / COMPAT | 返航派发后未封锁输入：官方 `SceneLoaderProxy.LoadScene` 先 `InputManager.DisableInput` 再派发，天空岛 `Close` 直接 `DispatchReturnIfReady`，黑幕淡入那一秒玩家仍可移动/开火/触发交互（`NotifyEvacuated` 此前已存过一次档）。 | Fixed（待实机）；新增 `BlockInputForReturn()`，封锁源为岛场景内临时对象（挂在地形根 `SkyIslandWorld` 下），随场景卸载解封，`Cleanup` 也销毁。**不能挂宿主**：`InputManager.blockInputSources` 只在源销毁/失活时移除，DontDestroyOnLoad 宿主会让回基地后输入永久锁死。守卫钉住调用顺序、场景归属与 Cleanup 销毁；三条破坏逐条转红。 |
| CR-2026-09-09-003 | P3 / COMPAT | 官方加载器同步拒绝时空等 120 秒：`SceneLoader.LoadScene` 遇 `IsSceneLoading`/`GetSceneInfo` 为空只记 LogError 返回，`BeginLoad` 的 `LoadFinished` 立刻为 true，但 `Build` 等 root 的循环不看它，HUD 挂「正在加载…」两分钟，超时后又按已起航发起一次多余的回基地加载。`CanEnter` 已挡住绝大多数情况，属单帧竞态。 | Fixed（待实机）；循环内 `lease.LoadFinished` 即抛，并置 `loadStarted = false` 走「未起航」清理。守卫钉住；改成 `if (false)` 后转红。 |

Documented（不计缺陷）：服务根/地形根在 `sceneLoaded` 回调里才激活，先于租约订阅的处理器看到的是没有 `LevelManager` 的战斗场景；仓库内处理器无同步依赖。官方倒计时控件 `EvacuationCountdownUI` 未复用（**2026-09-10 更正**：`a613774` 起读条显示已复用官方控件，计时也在全方位审核中改走游戏时间，见 `OFFICIAL_SCENE_CONTRACT.md` 与 CR-2026-09-10-008）。两条已写入 `OFFICIAL_SCENE_CONTRACT.md`。

## 2026-09-08 天空岛全面审计追加：1 P1 / 1 P2

对未提交工作树做的独立全面审核。编译、572 guard、27 条执行回归当时全绿，这两条都是绿灯查不出的类型。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-08-006 | P1 / OPERATIONAL | `SkyIslandSceneLease`（111 行）零实例化：全仓唯一引用是 `ModBehaviour.OnSceneLoaded` 的静态路径判断。正式地图早已换成 `SkyIslandRaidLease` + `SkyIslandRaid.unity`，但构建脚本仍把 **54,310,321 字节的 `Assets/arenas/sky_island_world` 部署给每个玩家**（占部署总量 172 MB 的约三分之一），且 `SkyIslandLifecycleGuard` 与一套 17 断言的执行回归还在为这段死代码站岗。 | Fixed；删除类、编译清单条目、bat 部署块、`SceneRuntimeGate.SkyIslandResourceScenePath`、宿主过滤行、fixture 与守卫断言，本机游戏目录旧副本一并清除。部署量 172 MB → 120 MB。`EquipmentResourceSceneGuard` 反向加断言：天空岛不得再被登记成附加资源 Scene（它是完整独立关卡，本就该走正常切图保护）。 |
| CR-2026-09-08-007 | P2 / COMPAT | 缺场景包时入口 **fail-late**：`SkyIslandRuntimeModule` 无条件在基地船点挂「前往天空岛」子交互、立世界文字招牌、发一条公告，`CanEnter` 也不查资源；`File.Exists` 只在 `SkyIslandRaidLease.Prepare()` 里，玩家点下去、HUD 建好、剧情 store 订阅完之后才报「缺少天空岛独立出击场景包」。bundle 不入 git，从仓库构建的人必然撞上。 | Fixed；新增 `SkyIslandRaidLease.IsBundleDeployed()`，`CanEnter` 与船点入口都先问一次；缺包时不挂选项、不立招牌、不发公告，只写一条 `CriticalLog`。`SkyIslandPlayerEntryGuard` 断言检查必须早于船点交互查找。 |

同轮修掉的 P3（不单独立 finding）：`compile_official.bat` 的 UTF-8 BOM 与 5 个部署块缺 echo、
`ArenaPrototypeSession.cs` 的 CRLF/LF 混排、`GameplayCoverage.json` 把「晴禾 / 苇白」写成「青禾 / 未白」、
每次进岛重复解析 World.json、`SkyIslandRendering.Apply` 的冗余形参、只被守卫读的 `ResidentSceneName` 常量、
`SkyIslandMapGraphic` 缺 65535 UI 顶点上限的显式失败、门状态按全部 flags 而非门位重扫导航、
`Summary` 与地图坐标每帧重建字符串。

**审计期同时修正了三个被上一轮改动改崩的 fixture**：`SkyIslandOfficialContract`（27 个编译错误）、
`SkyIslandStory`（缺 `UnityEngine` 命名空间）、`SkyIslandSceneReferenceBridge`（缺 `LevelManager`，
且 transpiler 用 `AccessTools.PropertyGetter` 定位 `LevelInited`，替身写成字段会让 Harmony 抛
`ArgumentNullException`）。同时按报告 `U4` 修正存档替身偏差：官方是
`SaveFile(bool writeSaveTime)`，**不触发** `OnCollectSaveData`，且方法体无 try/finally——
物理写异常后 `IsSaving` 会一直停在 true。

## 2026-09-08 天空岛端到端验收：3 P1 / 1 P2

工作树基线 `4b1b5b6` 加当前未提交天空岛实现；完整证据、玩家链路、复现与验证边界见 [天空岛端到端验收与代码审查](docs/reports/sky-island/2026-09-08-天空岛端到端验收与代码审查.md)。审查当轮仅记录、未改代码；四条已于同日修复，状态见下表。**验收结论不改判**：修复只消除了已确认缺陷，实机验收（报告第 6 节 8 项）一条都还没做。

| ID | 级别 / 分类 | 已确认缺陷 | 状态与验证 |
| --- | --- | --- | --- |
| CR-2026-09-08-004 | P1 / COMPAT | 普通离岛的 `SkyIslandStorySaveRecovery` 挂在 Mod host 上；host 后续销毁时 `OnDestroy` 调用 `StoryService.Close`，即使保存失败仍退订关闭，没有转交独立 owner，已接受但尚未写入官方缓存的剧情丢失。锚点：`SkyIslandStorySaveRecovery.cs:48`、`SkyIslandStoryService.cs:156`、`SkyIslandSession.cs:585`。 | Fixed（待实机）；`CloseOrRetain` 从第一次移交起就建独立 `DontDestroyOnLoad` owner，不再挂宿主，并对同一 service 去重；`host` 形参随之删除。`SkyIslandStory` fixture 补报告原始复现：IsSaving 阻塞下接受航路图 → 销毁宿主 → 解除忙 → 重读同槽，断言进度未丢。**反向验证已实跑**：把 owner 改回挂宿主后该断言转红。 |
| CR-2026-09-08-003 | P1 / COMPAT | 一次键写入异常使共享 store 进入单向 StoreFaulted；天空岛补写成功后仍无法完成 `TryClose`，恢复 owner 始终占据同槽，`CanEnter` 永远显示保存中。锚点：`SkyIslandStoryService.cs:169`、`SkyIslandStorySaveRecovery.cs:40`、`SkyIslandSession.cs:58`。 | Fixed（待实机）；`TryRecoverFaultedStore()` 在 `Tick` 与 `TryClose` 两处尝试恢复——不清共享单向故障标记，而是另建 store、重读原 key、把最后已接受快照经 Encode/Decode 往返校验后移交，1 秒节流。fixture 补：首次键写入抛异常 → 恢复存储 → 有界次数内 `TryClose` 成功且事实保留。**反向验证已实跑**：抽掉 `TryClose` 的恢复调用后该断言转红。 |
| CR-2026-09-08-002 | P1 / COMPAT | 正式 F6 地图的“立即返回基地”直接调用 `Close(true, "map_return")`，经 `ReturnToBase(moved)` 触发官方撤离与角色保存；没有位置、战斗或三秒停留检查，绕过交付规定的撤离圈。锚点：`SkyIslandMap.cs:90`、`SkyIslandSession.cs:391`、`SkyIslandRaidLease.cs:97`。 | Fixed（待实机）；F6 地图的「立即返回基地」已删除，地图改为**撤离点导航**：码头蓝点恒显、归航钟庭绿点在结局后补画、实时显示最近撤离点与直线距离。撤离判定收敛为 `SkyIslandSession.IsInsideExtraction()` 单一事实源（`ExtractionRadius` 2.5m / `ExtractionHold` 3s），Dev 与初始化失败的内部返回入口保留。中英 Wiki 四处同步。`SkyIslandPlayerEntryGuard` 新增反例断言：地图内不得出现 `Close(` 或 `returnToBase`。**2026-09-09 更新**：地图已换成官方 M 键地图，`SkyIslandMap.cs` 删除；同一条不变式改为断言 `SkyIslandSession.OpenMap()` 内不得出现 `Close(` / `returnToBase` / `ReturnToBase`，撤离判定仍唯一留在 `IsInsideExtraction()`。 |
| CR-2026-09-08-005 | P2 / COMPAT、WIRE+ | 缺少真实 SceneLocationsProvider 等导致官方 InitLevel 在设置 LevelInited 前抛异常，外层加载仍等待；Session 超时返航又要求同一个 LoadFinished，租约 recovery 被 loading 挡住。锚点：`SkyIslandSceneReferenceBridge.cs:218`、`SkyIslandSession.cs:553`、`SkyIslandRaidLease.cs:123`。 | Fixed（待实机）；桥接侧的 `VerifyBeforeActivation` / `Begin|Bind|Abort|EndInitialization` / `SaveBeforeLoadPrefix` / `LoaderInitializationTranspiler` 已全部接线：租约持有初始化令牌，激活官方服务**之前**跑完最小装配合同，失败或超时给官方等待一个 `OperationCanceledException`，释放时归还令牌。`SkyIslandRaidLease` fixture 30 断言、`SkyIslandOfficialContract` 78 断言、`SkyIslandSceneReferenceBridge` 44 断言（真实 Harmony）覆盖缺 provider → 不激活 → 有界返航 → 释放 → 可重入。 |

证据目录：`Build/sky-island-review/`、`Build/sky_story_review_probe/`。本轮 Windows 845 个生产输入真实 DLL Release 编译通过、14 项相关守卫通过、7 套天空岛回归 315 条断言通过；另有故障复现探针。无游戏内验收，不宣称全部内容实际可达、死亡墓碑或性能合格。交互同点竞争、官方墓碑迟到回调及性能缺口在完整报告中单列未验证项。

## 2026-09-08 附加资源 Scene 与装备卸装：1 项

| ID | 级别 / 分类 | 已确认缺陷 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-08-001 | P1 / COMPAT | `Common/Equipment/EquipmentEffectManager.cs` 的 `OnSceneUnloaded` 对任何 Scene 启用切图保护。天空岛/石堡返基地只卸载附加资源 Scene，不再加载官方关卡，保护因此保持 true；随后卸下飞行图腾时 `CheckAllSlots` 早返，真实 `FlightAbilityManager.UnregisterAbility/RestoreDash` 未执行，飞行能力与 Dash 替换残留。资源 sceneLoaded 反过来也会在真实切图中提前清除保护。 | Fixed：两个装备回调在状态改变前经共享 `SceneRuntimeGate.IsModResourceScene` 排除两个精确路径；旧生产代码夹具 27 断言 / 10 失败，修复后 27 全通过，含真实飞行管理器的 Dash 还原。Windows Dev 编译与相关 guard 通过；游戏 smoke 待人工。 |

路径常量集中在 `SceneRuntimeGate`，天空岛/石堡资源租约复用，不让 Common 装备层反向依赖调试会话。真实关卡切图、临时空槽保护、真实关卡加载后的解除，以及同名/前缀相同的外部 Scene 行为均保留。证据：`Build/equipment_resource_scene_before.log`、`Build/runtime-regressions/EquipmentResourceScene.log`、`Build/equipment_resource_scene_compile.log`。本轮没有游戏内复测或提交。

## 2026-09-07 最新实机日志修复：7 项

输入 `BossRushValidation_20260907_150312_248.log`（141 PASS / 4 FAIL / 1 WARN）及同次 `Player.log`。
运行 DLL MVID `b1b4746d-aabc-401d-a680-904a1468c306`；日志已执行新版 G 九波 / H 六场入口，属于有效失败证据。
以下为代码修复状态；隔离回归和 Windows 编译已验证，修复后 Unity 实机仍待复测。

| ID | 级别 / 分类 | 已确认缺陷 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-07-005 | P1 / COMPAT | Mod 晚于选档事件启动时，`ModeHWarehouseStakeJournal` 未 LoadPersisted，一致性与 deferred 均保持默认 false；零押品锁盘也永久报 `stake_slot_inconsistent`，本次六场测试实际 0 场。 | Fixed：OnAwake 风险扫描后、恢复赛季前加载当前押品日志；干净/未就绪/活动/损坏/已返还日志回归通过，保留旧押品安全屏障 |
| CR-2026-09-07-006 | P2 / COMPAT | 认证候选刚保存后，`DebugFinishValidationSeason` 的普通写入被同帧节流，归档失败，触发首次认证及缓存认证清理 FAIL。 | Fixed：退出归档要求 durable 保存；实际协调器回归验证同帧成功及真实 I/O 失败仍不退出 |
| CR-2026-09-07-007 | P2 / COMPAT | 官方 Hurt 先派发死亡、Mode G 停用龙王，再派发 OnHurt；迟到回调重触发孩儿护我，向 inactive GameObject 启动协程。 | Fixed：OnBossHurt 首先检查组件/角色/生命/死亡阶段；停用、死亡与正常技能回归通过 |
| CR-2026-09-07-008 | P2 / COMPAT | `wedding_chapel` 已有放置记录，但缺少好感历史标记时两条初始化入口均拒绝注册 prefab；官方重绘已有建筑报缺模型。 | **Reopened 2026-09-15 → 见 CR-2026-09-15-002**：原修复在注入前用官方 `Any` 判存在，`Any` 跳过未注册 info，注入前恒 false，修复没有生效；当时的回归把存在判定整段替成常量，没有抓到 |
| CR-2026-09-07-009 | P2 / COMPAT、WIRE+ | 官方搜索/障碍任务的延迟回调直接访问 `agent.gameObject`，角色销毁后 agent 无效时抛空引用；同次日志反复出现。 | Fixed：两个精确目标 Prefix 在原解引用前检查 Unity 生命周期；原回调错误复现，正常结果与任务完成回归通过 |
| CR-2026-09-07-010 | P2 / COMPAT、WIRE+ | 官方 `InteractableBase.Awake` 无条件遍历未初始化的交互组，运行时 AddComponent 缺少序列化列表时失败；本次 `MakeTimeQuacker.Bed2Interactable.Awake` 走到该路径。 | Fixed：Awake 前仅补 null 列表，保留已有组与完整原初始化；原方法回归复现失败并验证修复 |
| CR-2026-09-07-011 | P2 / COMPAT、WIRE+ | 官方 FowSmoke 长计时无销毁取消，切图后继续申请 `WaitForEndOfFrame(this)`，已销毁 runner 触发 StartCoroutine 空引用。 | Fixed：仅该重载中已销毁 FowSmoke 返回取消任务；实际游戏 IL/签名核对及正常/失效/其他 owner 边界回归通过 |

`casino_building` 缺少 prefab 仍为未解决的外部资源线索：当前生产源码与已安装的 44 个 Mod DLL 均无该 ID 定义，不能据此推断其原提供方，也不能删除存档建筑记录来消除报错。验证明细见 `FIX_TRACKER.md` 同日条目。

## 2026-09-07 近两周复核与 F3：4 项已修复

范围 2026-08-24 起 153 个提交，冻结 HEAD `965f839`，并包含审查期间 Wiki 工作区增量。
分类均为 COMPAT；源码与隔离回归已验证，新 DLL Unity F3 / 人工验收仍待执行。
完整范围、触发链与边界见 [近两周审核与 F3 验收](docs/reports/reviews/2026-09-07-近两周审核与F3验收.md)。

| ID | 级别 / 分类 | 已确认缺陷 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-07-001 | P1 / COMPAT | F3 嵌套协程异常后未 Dispose 暂停父协程，finally 清理不执行；套件外层异常/报告初始化失败还可能留下运行句柄与缺失汇总。 | Fixed：统一协程栈和会话收尾；旧生产方法探针复现，新真实隔离壳/栈 38 条断言通过 |
| CR-2026-09-07-002 | P2 / COMPAT | G 仅启动、H 仅认证即可报通过，九波/六场未执行；SUMMARY 未把自动覆盖缺项计入结果。 | Fixed：新增完整流程断言与 INCOMPLETE/MVID/报告 I/O 判定；新流程实机待验 |
| CR-2026-09-07-003 | P2 / COMPAT | 雷霆旧激活协程因过期退出后，finally 清掉重穿后新链的 in-flight 与命中集合。 | Fixed：finally 校验激活代数；完整生产协程回归通过 |
| CR-2026-09-07-004 | P2 / COMPAT | 冰/雷主角死亡只清冷却，未取消排队冰葬/引雷/反震，死后或复活后仍结算旧伤害。 | Fixed：死亡推进既有代数；两套延时伤害回归通过 |

## 2026-09-06 全量深度审查：5 P1 / 5 P2 已修复（2026-09-07 回填）

范围 `f9b83c0fa21a3ef03e8abc0941fb71beb3201ba2..18c43dacb32749b65057c97fdd454888af5abe57`（131 个提交），加最终 2026-09-06 01:11:56 +08:00 的工作区快照。
本批 **10 项已在后续提交修复，2026-09-07 复核回填 Fixed**；其中 016 来自审查期间并发新增的套装代码。已有 D-1 / D-5 继续沿用旧设计复审编号，不重复立条；下方历史批次的 Fixed 状态不变。
完整触发链、建议与证据见 [全量深度审查报告](docs/reports/reviews/2026-09-06-全量深度审查修复验收.md)。原轮仅审查及登记；本次根据当前生产代码与回归补充修复状态，原缺陷锚点保留为历史证据。

| ID | 级别 / 分类 | 已确认缺陷与当前代码锚点 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-06-007 | P1 / COMPAT | `ModeH/ModeHRuntimeModule_MatchFlow.cs:201` 技术重试只恢复虚拟筹码和锁盘快照，旧真实押品 journal 仍 MatchLocked；恢复页面显示零件，空选择绕过一致性检查后继续承担旧押品。 | Fixed：`522274e`，2026-09-07 代码/回归复核；Unity 待验 |
| CR-2026-09-06-008 | P1 / COMPAT | `ModeH/ModeHRuntimeModule_SettlementFlow.cs:204` 持久战痕候选的动作依赖内存 `_pendingScarProfileId`，冷恢复丢接受／拒绝，随后装备选择归档推进，无法补做。 | Fixed：`522274e`，2026-09-07 代码/回归复核；Unity 待验 |
| CR-2026-09-06-009 | P1 / COMPAT | `Integration/DailyReport/DailyReportService.cs:624-638` 已知存档门面单向故障时仍反复补发里程碑，领取标记永远失败；每次开报纸都增加同一件实物。 | Fixed：`522274e`，2026-09-07 代码/回归复核；Unity 待验 |
| CR-2026-09-06-010 | P1 / SCHEMA+ | `Integration/DailyReport/DailyReportService.cs:315-316、538-539` 断签／翻期清空唯一未领奖励载体，第7格／第30格已经赚取但发放失败的奖励永久消失；建议独立持久欠账，保留现有签到规则。 | Fixed：`522274e`，2026-09-07 代码/回归复核；Unity 待验 |
| CR-2026-09-06-011 | P1 / COMPAT | `wiki-site/docs/.vitepress/theme/composables/useWiki.ts:89` 在 withBase 前剥离 `/`，深层中英文页面的共享导航成为相对链接、重复拼接目录；冻结构建有 6,212 处无效引用。 | Fixed：`90378de`，2026-09-07 代码/回归复核；Unity 待验 |
| CR-2026-09-06-012 | P2 / COMPAT | `Integration/NPCs/DuckNpc/DuckNpcMovement.cs:325` Hold／PauseFor 只 StopMove，未取消在途寻路；晚回调使官方 PathControl 再次移动，聊天暂停失效。 | Fixed：`522274e`，2026-09-07 代码/回归复核；Unity 待验 |
| CR-2026-09-06-013 | P2 / COMPAT | `Integration/NPCs/DuckNpc/DuckNpcMovement.cs:197-205` Moving 早返挡住后面的0.6秒跟随重规划与40米追赶，必须等旧路径结束或12秒超时。 | Fixed：`522274e`，2026-09-07 代码/回归复核；Unity 待验 |
| CR-2026-09-06-014 | P2 / COMPAT | `Integration/NPCs/DuckNpc/DuckNpcRuntimeMarker.cs:136-140` 结束聊天无条件 Release，释放婚姻系统原有的教堂 Hold，6秒后配偶重新随机漫步。 | Fixed：`522274e`，2026-09-07 代码/回归复核；Unity 待验 |
| CR-2026-09-06-015 | P2 / COMPAT | `Integration/Codex/CodexKillCollector.cs:339-344` 新增冠军之影等历史 key 后不使目录失效；卡片缺失，解锁数使用新集合、全录分母使用旧集合，可提前授予全谱成就。 | Fixed：`522274e`，2026-09-07 代码/回归复核；Unity 待验 |
| CR-2026-09-06-016 | P2 / COMPAT | `Integration/Bonus/SetBonusVisuals.cs:610` 冰霜／雷霆吸收按减免前元素比划分减免后总伤害，把物理伤害错误算为元素治疗；来自并发工作区增量。 | Fixed：`c6a83bc`，2026-09-07 代码/回归复核；Unity 待验 |

原审查基线验证：冻结的 **793 个生产输入 Windows Release/Dev 真编译通过**；**544 guard 全绿**；10组既有执行回归共 **402 条正确行为断言通过**。
原审查另新增 **25 条缺陷复现检查**和 Wiki 构建／链接审计证明原冻结版本存在本批错误，不计为修复通过。没有 Unity 实机验证、部署或提交；完整输入哈希、执行证据与并发范围见报告。

2026-09-07 回填证据：ModeHThirdReviewFixes、ContentThirdReviewFixes、IntegrationThirdReviewFixes 执行通过；Wiki 构建及 80 个导航目标检查通过。原冻结版本的复现结论与当时验证数字仍保留。

## 2026-09-06 设计与代码规范复审：D-4 / D-3 / D-2 三项已修复

来源 [设计与代码规范复审报告](docs/reports/reviews/2026-09-05-f9b83c0-设计与代码规范复审.md)（范围 `f9b83c0..HEAD`，视角为设计 / 复用 / 可维护性，与同日三轮玩法缺陷审核互补，不重开它们的条目）。owner 2026-09-06 指示按 D-4 → D-3 → D-2 顺序全部修复；D-1 / D-5 及 8 条 P3 仍为 Open，见该报告。修复流水账见 `FIX_TRACKER.md` 同日条目。提交状态：D-4 已由并行会话拆提交为 `18c43da`，D-3 为 `71356fa`；D-2 与本轮文档 / 守卫收尾仍在工作树，未提交。

| ID | 级别 / 分类 | 缺陷与代码锚点（修复前） | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-06-001 | P2 / SAFE | `ModBehaviour.cs:785-816` 遗种巢 / 日报的宿主销毁清理与模块 `OnDestroy` 各写一份（PetNest 7 条重叠、10 条只在宿主、8 条只在模块）且宿主先清，模块自己的落盘随即空转；图鉴 / 随机事件经 `CleanupCodexRuntimeOnDestroy` / `CleanupRandomEventsRuntimeOnDestroy` 同形。 | Fixed：清理 owner 唯一化到各 `RuntimeModule.OnDestroy()`，宿主只经 `runtimeModuleHost.OnDestroy()` |
| CR-2026-09-06-002 | P2 / COMPAT | `ModeH/ModeHJsonValue.cs` 已被 9 个 ModeH 之外的文件依赖却挂 ModeH 前缀；`PetNest/PetNestJson.cs` 是第二套嵌套解析器；`CodexCodec.cs:5-12` / `DailyReportCodec.cs:6-7` 为避开两者选了最弱的前缀提取器，把 schema 绑在「只能一个数组 / entries 必须最后 / key 不得互为前缀」上；`F3GameplayValidationCoverage.cs:20` 绕过 `JsonDataRegistry` 这个「唯一读取入口」。 | Fixed：`Common/Data/BossRushJsonValue.cs` 单一解析器 + 写出器，Codex / 日报读侧改走节点解析器（写侧字节不变） |
| CR-2026-09-06-003 | P2 / COMPAT | 落盘协调器 ×4（归一化后 Codex↔DailyReport 仅差 84 行）、单 key 存档门面 ×3（Codex↔DailyReport 仅差 49 行）、建筑交互体 ×5（DailyReport↔Campaign 仅差 27 行）逐字复制；`_saveFilePending` 那类修复需逐份重做，`SaveCoordinatorRetryGuard` 只能逐份锁同一条不变式。 | Fixed：`BossRushSaveCoordinatorEngine` / `BossRushSlotJsonStore<T>` / `BossRushBuildingInteractableBase`，子系统只保留一行式门面与绑定，调用面不变 |

## 2026-09-06 冰霜 / 雷霆套装开放获取后的全面审核：4 项（均已修）

owner 要求「全面审核一遍」后，对本轮改动及其直接影响面（套装效果、表现层、掉落链路、配置与文案）逐条复核所得。
修复见 `FIX_TRACKER.md` 同日条目；实机 smoke 待做。

| ID | 级别 / 分类 | 已确认缺陷与代码锚点 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-06-007 | P1 / COMPAT | `Integration/Bonus/ThunderSetBonus_Storm.cs`（修复前）引雷术靠 `Hurt` 同步派发的 `OnDead` 再进 `TryScheduleThunderChain` 来接下一跳，而一跳最多打死 3 个目标、每个都满足 `thunderChainDepth < MAX_DEPTH`，于是**每个死亡目标各起一条链**：3 目标 × 3 跳 = 最坏 3+9+27 = 39 次伤害结算、39 道电弧与 39 次音效挤在 0.24 秒内，且无跨跳去重，同一敌人可被反复电。与说明/Wiki/F3 用例承诺的「最多 3 跳」不符，密集波次下是可感知的帧率与数值双重问题。现改为线性链：下一跳由协程自己接（每跳只取第一具尸体），`thunderChainInFlight` + `isFromBuffOrEffect` 双保险挡住再入，`thunderChainHits` 做跨跳去重，全程至多 9 次伤害。 | Fixed |
| CR-2026-09-06-008 | P2 / COMPAT | `Integration/Bonus/{ThunderSetBonus_Storm,FrostSetBonus_Nova}.cs`（修复前）延时结算协程只检查 `xxxSetActive`。场景重载走的是 `SetBonusManager` 的「先停用再重查」，同一帧内该布尔会先 false 再 true，于是上一张图排队的连锁/霜爆会带着**旧场景坐标**在新场景结算。现引入 `setBonusGeneration`（停用时递增），三个延时协程都带代数校验。静态推断，需实机复测。 | Fixed |
| CR-2026-09-06-009 | P2 / COMPAT | `Integration/Config/FrostThunderSetConfig.cs`（修复前）四件装备耐久 999 但从未打 `Repairable` 标签。官方 `Item.Repairable = UseDurability && Tags.Contains("Repairable")`，而 `UseDurability` 就是 `MaxDurability > 0`——必然为 true，因此维修台会显式显示「无法维修」（`ItemRepairView.cs:263` 的 `cannotRepairIndicator`），磨损永久带着并按耐久比折损售价（`Item.GetTotalRawValue`）。装备不可获取时无影响，本轮开放获取后成为玩家可见问题。龙王套装一直有这个标签。现补 `EquipmentHelper.AddRepairableTag(item)`。 | Fixed |
| CR-2026-09-06-010 | P2 / SAFE | `WikiContent/{zh,en}/equipment/equipment__{frost,thunder}_set.md`（修复前）四份都写「**死亡掉落**：不会因死亡掉落 / Won't drop on death」，但这四件既没有 `DontDropOnDeadInSlot` 标签也不是 `Sticky`。官方 `CharacterMainControl.cs:1963/1971/2002` 只对带该标签或 Sticky 的主角物品免除死亡掉落，所以实际会掉。装备不可获取时无影响，本轮开放获取后是**直接误导玩家的错误说明**。现按实际行为改写（与龙王套装一致：会掉），并补一行维修说明。若 owner 希望改成绑定不掉，是 `AddTagToItem(item, "DontDropOnDeadInSlot")` 一行，但那属经济/难度取舍，未擅自决定。 | Fixed |

## 2026-09-06 冰霜 / 雷霆套装重做时确认的 3 项（均已修）

在把 500053-500056 从开发预览转为正式内容的过程中确认。修复见 `FIX_TRACKER.md` 同日条目；实机 smoke 待做。

| ID | 级别 / 分类 | 已确认缺陷与代码锚点 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-06-004 | P1 / COMPAT | `Integration/Bonus/ThunderSetBonus.cs:228`（修复前）反击 `CreateExplosion(pos, 4f, dmg, normal, 0.3f)` 漏传第 6 参 `canHurtSelf`；官方 `ExplosionManager.cs:17/32-36` 默认 `true` 时 `selfTeam = Teams.all`，`Team.IsEnemy(Teams.all, x)` 恒真，爆炸中心的玩家自己必吃 25 电伤，与四份 Wiki「对自身无伤害」相反。现显式传 `false` 并置 `isFromBuffOrEffect`/`fromWeaponItemID = 0`。 | Fixed |
| CR-2026-09-06-005 | P1 / COMPAT | `Integration/Bonus/SetBonusManager.cs:135-150`（修复前）场景加载只重查不重挂：官方每图重建主角与 `CharacterItem`（`LevelManager.LoadOrCreateCharacterItemInstance`），旧 Item 上的电抗/冰抗 Modifier 作废，`xxxSetActive` 仍为 true 于是不翻转，被动静默丢失直到重新穿脱。现先停用两套再 `CheckSetBonusStatus(main, announce:false)`。静态推断，需实机复测。龙套装眼光同病未在本轮修。 | Fixed |
| CR-2026-09-06-006 | P2 / COMPAT | `Integration/Bonus/ThunderSetBonus.cs:228`（修复前）在 `Health.OnHurt` 回调内直接 `CreateExplosion`：若触发伤害本身来自敌方爆炸，此时正处在 `ExplosionManager` 的 `for` 循环里，嵌套调用会覆写其实例共享的 `colliders[8]` 并 `damagedHealth.Clear()`，外层循环继续跑脏数据。现把反震结算延后一帧（`ThunderCounterStep` 协程）。静态推断，需实机复测。 | Fixed |

## 2026-09-05 二次深度复审：10 项已完成代码修复（2026-09-06 验收）

固定基线 `f9b83c0fa21a3ef03e8abc0941fb71beb3201ba2..29cb0c12dfe33164e35ce775e470621ea4afcb4b`，并纳入当前未提交修复与 Wiki 工作。
本批新增 **5 P1 / 5 P2，共 10 项，现均 Fixed**（代码修复与隔离执行回归通过，Unity 实机待验）；不重复下方已 Fixed 的 15 项，也不是历史累计统计。
原触发链与修复前证据保留在 [二次深度复审报告](docs/reports/reviews/2026-09-06-二次复审修复验收.md)，表中代码锚点保留原缺陷位置。用户要求“全面修复”后，全部修复为 `COMPAT`，文档为 `SAFE`；修复后行为及验证见 [二次复审修复验收](docs/reports/reviews/2026-09-06-二次复审修复验收.md)。未提交 commit。

| ID | 级别 / 分类 | 当前工作区已确认缺陷与代码锚点 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-05-011 | P1 / COMPAT | `ModeH/ModeHRuntimeModule_SettlementFlow.cs:92` 恢复幕间能按持久数据显示奖励按钮，动作却依赖空的 `_lastRewardOperation`，两个选项均早退，无法推进。 | Fixed |
| CR-2026-09-05-012 | P1 / COMPAT | `ModeH/ModeHRuntimeModule_UiFlow.cs:406` 场内放弃中断赛季后直接清 owner，未释放观战输入、竞技场租约及旧页面。 | Fixed |
| CR-2026-09-05-013 | P1 / COMPAT | `ModeH/ModeHCombatControl.cs:287` 零活敌即判胜，遗漏未入场/生成中的后批；当前多批计划可在增援强制分帧期间提前结算。 | Fixed |
| CR-2026-09-05-014 | P1 / COMPAT | `ModeH/ModeHRuntimeModule_CombatProfiles.cs:292` 增援事务只存局部变量，场末未停止增援协程或回收成功批次；迟到提交还可访问已清控制器。 | Fixed |
| CR-2026-09-05-015 | P1 / COMPAT | `ModeH/ModeHRuntimeModule_SceneFlow.cs:266` 赛季 ID 仅由地图与进程内 generation 组成，重启重复后，名人堂去重吞掉另一个新冠军。 | Fixed |
| CR-2026-09-05-016 | P2 / COMPAT | `ModeH/ModeHRuntimeModule_CombatProfiles.cs:239` 后批门控只看是否还有空位，不检查整批容量；真实 `[2,2]` 计划可突破 cap=3。 | Fixed |
| CR-2026-09-05-017 | P2 / COMPAT | `RandomEvents/RandomEventEffectsBridge_Loot.cs:329` 实际 randomPool 使用 Q1–Q8 通用候选；官方池分支跳过 qualities，三模式空投上下限失效。 | Fixed |
| CR-2026-09-05-018 | P2 / COMPAT | `Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs:190` 旧异步生成忙时丢弃新场景唯一请求，旧请求失效后只清 busy，新场景不再生成永久 NPC。 | Fixed |
| CR-2026-09-05-019 | P2 / COMPAT | `Integration/BackMountain/ShowcaseService.cs:237` 先撤生命上限加成再判原本满血，登记收藏将 101/102 的受伤状态补到 102.5/102.5。 | Fixed |
| CR-2026-09-05-020 | P2 / COMPAT | `Common/Infrastructure/HarmonyBindingSelfCheck.cs:99` 按补丁类计数，却只查目标上的 owner；同目标两类仅装一类仍报 2/2 通过。 | Fixed |

修复验收：完整 786 项生产输入的 Windows Release/Dev 真编译均通过；**544 guard 全绿**；新增 **219** 加既有 **183**，共 **402 条执行断言通过**。覆盖恢复奖励动作、弃赛清理、跨进程唯一 ID、增援异步所有权/容量/判胜、NPC 后继、生命采样、空投实际池与真实 Harmony 部分挂载；相关 repowiki 10 篇已同步。未进行 Unity 实机 smoke、部署或提交。
原审查的 538 guard、19 条缺陷复现断言及 Wiki 冻结快照结果仍保留在原报告，不与修复后的正确行为断言混淆。其他任务的后续 Wiki 修改不在本次修复验收内。

## 2026-09-05 全面复审：15 项已完成代码修复

固定基线 `f9b83c0fa21a3ef03e8abc0941fb71beb3201ba2..29cb0c12dfe33164e35ce775e470621ea4afcb4b`。
本批确认 **9 P1 / 6 P2，共 15 项，现均 Fixed**（10 项新增、5 项复核既有问题）；不是整个历史问题库的累计统计。
每项完整触发链、修复建议与验证边界见 [全面复审报告](docs/reports/reviews/2026-09-05-全面复审修复验收.md)。
用户要求全部修复后，15 项已完成代码修复和隔离回归，Unity 实机待验；见 [修复验收记录](docs/reports/reviews/2026-09-05-全面复审修复验收.md)。本批修复均为 `COMPAT`，记录文档为 `SAFE`，表中分类及代码行号保留修复前缺陷证据（旧 022 的缺陷影响仍标 `BREAKING`）。未提交 commit。

| ID | 级别 / 分类 | 已确认缺陷与代码锚点 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-05-001 | P1 / COMPAT | `ModeH/ModeHRuntimeModule.cs:336` 恢复只填 run owner，不填 `_season`；有效 Suspended 赛季同场重开仍得到 Unknown，再次挂起。 | Fixed |
| CR-2026-09-05-002 | P1 / COMPAT | `ModeH/ModeHInjuryAndScarSystem.cs:332` 触发战痕不查当前选手持有集合；认证通过后，零战痕选手也能触发全局战痕。 | Fixed |
| CR-2026-09-05-003 | P1 / COMPAT | `Campaign/CampaignProgressService.cs:356` 完成状态成功落盘，但奖金未采集到 EconomyData；下次官方采集前重载会永久漏领。 | Fixed |
| CR-2026-09-05-004 | P1 / COMPAT | `Integration/DailyReport/DailyReportService.cs:477` 悬赏 claimed 与现金快照不同步；正常补发后重载可能只留下已领标志。 | Fixed |
| CR-2026-09-05-005 | P1 / COMPAT | `PetNest/PetNestHatchService.cs:275` 凝蛋扣魂与入巢各自提交，同帧第二次物理写延期；中断可留下“已扣魂、没有崽”的永久半交易。 | Fixed |
| CR-2026-09-05-006 | P1 / COMPAT | `ZombieMode/ZombieModeDropsAndPerformance.cs:713` 所有权检查每秒一次，销毁仍逐帧；扫描间隔内已拾取的过期物品仍可被 Destroy。 | Fixed |
| CR-2026-09-05-007 | P1 / COMPAT | `Integration/Wedding/WeddingModBehaviourBridge.cs:78` 永久配偶真正异步生成后没有婚姻收尾；同文件 `538` 的跨图跟随恢复也缺永久 NPC 生成分支。 | Fixed |
| CR-2026-09-05-008 | P2 / COMPAT | `ModeH/ModeHInjuryAndScarSystem.cs:172` 窗口在 adapter 尚未 Restore 时被移出 owner 列表，限时修改无法正常还原。 | Fixed |
| CR-2026-09-05-009 | P2 / COMPAT | `ModeH/ModeHInjuryAndScarSystem.cs:274` 自结算倍率丢失 TargetCommandId 和有效期，指定口令 5 秒效果变成整场全口令效果。 | Fixed |
| CR-2026-09-05-010 | P2 / COMPAT | `ModeH/ModeHCombatControl.cs:218` 首发先开常驻战痕、后填场地上下文；center_keeper 条件收益首次误判后不重算。 | Fixed |

复核既有 ID，避免重复立条：

| ID | 级别 / 分类 | 当前证据及本次增补 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-04-024 | P1 / COMPAT | `ModeH/ModeHRuntimeModule_MatchFlow.cs:1060` 押品强写后普通赛季写被同帧闸拒绝；本次另确认无押品冠军终局在 `ModeHRuntimeModule_CombatFlow.cs:987` 同样挂起，恢复重试仍失败。 | Fixed |
| CR-2026-09-04-022 | P1 / BREAKING | `ModeH/ModeHInventoryPersistenceBridge.cs:143` 旧 Prepared 摘要仍固定用新格式计数，相同实物误报不存在并进入 ManualIntervention。 | Fixed |
| CR-2026-09-04-025 | P2 / COMPAT | `ModeH/ModeHRuntimeModule_LoadoutEditing.cs:67` 清空接力 ID 后，真正休息的带伤选手被排除于赛后恢复遍历。 | Fixed |
| CR-2026-09-04-034 | P2 / COMPAT | `Integration/BackMountain/RaidMealUsageBehavior.cs:45` 官方完成帧二次 CanBeUsed 可绕过 OnUse 补偿，SaveFile 故障后仍可扣餐无效果。 | Fixed |
| CR-2026-09-04-036 | P2 / COMPAT | `PetNest/PetNestHatchService.cs:138` 资产采集失败后早退漏 RecordHatch，后续 Tick 补盘不补统计。 | Fixed |

修复验证：Windows Release/Dev 真编译通过（786 个生产输入，无 C# diagnostics）；新增 155 条和既有 28 条执行断言通过。当前全量守卫为 537 PASS / 1 FAIL，唯一失败为其他未提交 Wiki 工作中新出现的 `npc-goblin.webp` 未登记图片清单；本轮相关守卫全部通过，未删除其他工作产物或放宽断言。
新增修复夹具验证修复后的正确行为，与原审查用于复现缺陷的定向夹具区分；没有 Unity 实机 smoke。原审查时的 534 guard 及干净 HEAD 结果保留在原报告中。
以下 2026-09-04 注记与状态汇总保留为历史快照，不应覆盖本节的最新复核结论。

> 2026-09-04 深度复审增补：固定基线 `f9b83c0..adf1f3e`，新增 **CR-2026-09-04-021..035：2 P0 / 10 P1 / 3 P2，均 Open**。
> 完整触发链、修复建议与验证边界见 [本轮审查报告](docs/reports/reviews/2026-09-05-全面复审修复验收.md)。下面的原状态汇总是此前历史统计，未包含本批。
>
> 2026-09-04 修复复审：固定基线 `adf1f3e..29cb0c1`。021、023、026–033、035 原缺陷已修复；
> 022、025、034 重新打开，024 仍未闭环；026 原缺陷已修复，但新增 036。当前残留 2 P1 / 3 P2。
> 完整证据见 [修复复审报告](docs/reports/reviews/2026-09-05-全面复审修复验收.md)。

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

违反设计稿 `docs/design/2026-08-17_斗蛐蛐新模式创意脑暴.md:1744`「匹配不到 pre-image → 人工介入」。

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

**严重级**：P1；**兼容分类**：`BREAKING`；**状态**：Reopened（修复复审确认旧 journal 回归）；**来源**：完整静态调用链与隔离复现。

`ModeH/ModeHWarehouseStakeJournal.cs:1162–1167` 装载 journal 后清空 `_escrowItems`；`normalizedTreePayload` 全仓只有写入与声明，没有读取重建者。已有押品移除被官方保存后若进程中断，恢复返还在 `849–859` 恒因物品短缺进入人工介入。需核验持久证据后实现一次性重建与 receipt 防重。CR-2026-09-04-010 的满仓缓冲修复没有覆盖此支路。未实机复现。

**修复**：新增完整物品树恢复消费者，按槽、阶段、post-image 与逐项凭据恢复到官方缓冲区；旧载荷缺失变量类型时保留人工介入。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

**修复复审**：新格式重建链成立；但旧 `Prepared` / `EscrowSnapshotDurable` 取消时，
`CountOccurrences` 始终生成新格式摘要，无法命中旧摘要，物品仍在仓库也会进入人工介入。

### CR-2026-09-04-023：Mode H 延迟物理落盘后下一帧丢弃欠账

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：生产源码隔离复现。

`ModeH/ModeHSaveFlushCoordinator.cs:178–185` 只检查 typed pending。前一轮 `FlushPending` 已清 pending，但因战斗帧/节流尚未 SaveFile，下帧就清 deferred 返回成功。宿主强制保存也会早退。实际源码 harness 证明物理写次数始终停在 1，下一帧 deferred 已变 false。需独立保留 `_saveFileRequired` 至真实写盘成功。复现日志：`Build/review-adf1f3e/modeh-flush-repro.log`。

**修复**：独立物理写欠账，不依赖 typed pending；战斗等待不耗重试预算，失败重试重新采集资产/回滚 journal。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-024：真实押品同步四阶段必然撞每帧保存节流

**严重级**：P1；**兼容分类**：`COMPAT`；**状态**：Reopened（修复复审确认仍阻断）；**来源**：静态完整锁盘链与节流源码复现。

`ModeH/ModeHRealStakeService.cs:224–226` 在同一按钮回调中连续请求四个 durable 阶段。Prepare 首次落盘后下一阶段必被每帧闸拒绝，回滚到 Prepared 又留下 slot inconsistent，之后无法正常锁盘。需可续做的分帧事务或明确的资产屏障；不能把延期当永久失败，也不能提前声明 durable。仅补 023 的欠账位无效。完整 UI 锁盘待实机验证。

**修复**：真实押品四阶段使用强制同步屏障，失败回滚实物并重暂存一致账本；虚拟筹码预留同步撤回。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

**修复复审**：四阶段本身可以同帧完成，但紧随其后的 `loadout_locked` 赛季保存走普通节流，
必被本帧最后一次强制写盘挡下；调用方回滚并挂起，仍无法开赛。

### CR-2026-09-04-025：Mode H 赛前阵容、kit 与口令缺少玩家编辑入口

**严重级**：P2；**兼容分类**：`COMPAT`；**状态**：Reopened（主体已修，休息结算残留）；**来源**：生产写入点与当前设计/Wiki 对照。

`ModeH/ModeHRuntimeModule_CombatFlow.cs:34–48` 固定前两名为首发/接力、默认选 kit；`90–92` 固定口令。赔率页 `ModeHRuntimeModule_MatchFlow.cs:980–1002` 仅提供下注、押品、锁盘。玩家不能按现有承诺选择阵容、装备与通用令，亦不能主动让受伤主将休息。需把三个选择器接入真实编辑状态、赔率与摘要，并让锁盘消费玩家选择。未实机验证。

**修复**：赔率页接入阵容/休息/套装/口令滚动编辑；赔率、摘要与锁盘共同消费选择，并校验回调 owner。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

**修复复审**：阵容、kit、口令编辑主体已接通；“接力休息”清空 relay ID 后，结算仍只遍历
锁定的 starter/relay，明确休息的带伤选手不在恢复范围内。

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

**严重级**：P2；**兼容分类**：`COMPAT`；**状态**：Reopened（故障路径仍可绕过补偿）；**来源**：官方使用完成链静态确认。

`Integration/BackMountain/RaidMealUsageBehavior.cs:63–68` 在 Save/Load 失败或读回不匹配时仅 return，官方 `CA_UseItem.OnFinish` 仍在 Use 后 StackCount--，餐品丢失且未登记。需登记成功才消耗的单一责任或可靠补偿。此为故障注入条件，不能推断为普通保存并发竞争或正常必现。需真实使用阶段注入失败验证，未实机复现。

**修复**：登记失败预补数量抵消官方随后扣量，满堆不钳制；写/回读失败恢复旧餐登记。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

**修复复审**：动作结束时官方会再次检查 `CanBeUsed`；若 SaveFile I/O 异常把 `IsSaving`
留在 true，餐食行为的 `OnUse`/finally 均被跳过，官方完成动作仍扣数量。
隔离对照已确认原栈 1→0，登记/补偿/提示均未执行；日志 `Build/review-29cb0c1/content/repro.log`，
宿主依赖为 stub，未实机验证。

### CR-2026-09-04-035：血月收尾清表前未结清最后轮询窗口的击杀

**严重级**：P2；**兼容分类**：`COMPAT`；**状态**：Fixed（实机复测待完成）；**来源**：静态调度与生命周期证据。

`RandomEvents/RandomEventCatalog.cs:573–580` 直接清 _tracked；收益只在每 2 秒 Tick 轮询结算。最后扫描之后、75 秒到期之前杀死带增益敌人，或杀敌后立即局末，1200 收益会漏发。需结束清理前 drain 已发生死亡，或活动期间先记录死亡事实再兑现。需最后 2 秒/立即局末测试且验证不重复，未实机复现。

**修复**：具名幂等死亡订阅记录事实，Tick 与结束前结算共用去重计数，再退订清表。 **验证**：见 FIX_TRACKER 同日深度复审修复条目；未进行游戏实机测试。

### CR-2026-09-04-036：孵化资产延期成功后不再补记孵化统计

**严重级**：P2；**兼容分类**：`COMPAT`；**状态**：Fixed（隔离回归通过；实机故障注入待完成）；**来源**：修复差异、完整调用链与隔离故障注入。

`PetNest/PetNestHatchService.cs` 现把孵化统计（含异色计数与血脉首次解锁）写入与新崽相同的 Bundle 候选，
再由 `PetNestSaveCoordinator` 以实物快照义务重试物理落盘；统计不会依赖揭晓演出或后续单独回调。
`tests/fixtures/ContentTransactions/Program.cs` 的实体蛋路径注入资产序列化失败后，验证候选 Bundle 已包含统计、
下一 Tick 重试后孵化数与异色数只增加一次，并在物理写失败及官方采集回调路径复跑通过。完整夹具运行结果为
`ContentTransactions: 82 assertions passed`（2026-09-11）。仍未进行 Unity 实机故障注入，因此保留该验证缺口。

## UNVERIFIED / Seeded Leads

> 这里的内容不是 bug。升格前必须读代码或运行验证。

- **遗种巢 跨档写污染组合链**（016 的延伸，逐环节已静态核过、多步组合需实机）：关开关后已挂 handler 触发 `AddSouls` 会从当前槽重建缓存 → 主菜单切档（无人清缓存）→ 重开开关（EnsureBootstrapped 不重置缓存）→ 下次 Commit 把 A 档巢数据写进 B 档。与 017 的日报跨档渗漏同构。
- **遗种巢 远征奖励非崩溃失败被吞（已复核关闭）**：该历史线索已由 `PetNestExpeditionService.TryGrantPendingRewards` 的按条目游标与 `rewardsGranted` 门控修复；`GrantRewards` 任一物品或现金失败都会保留欠账和游标，下一次运行时重试。`ContentTransactions` 82 条断言含盖章失败后恢复且只投递一次；仍需 Unity 实机资源故障注入。
- **遗种巢 OnExpedition 孤儿锁无自愈（已实现，待专门验证）**：`PetNestExpeditionService.ReconcileOrphanedExpeditionLocks` 在可写事务内检查远征记录，确认无匹配后才复位 `OnExpedition` / `lockedByExpeditionId`，并在写屏障或存档故障时保持不动，避免误解锁。当前尚无专门夹具覆盖该入口，保留验证缺口，不再视为未修复代码问题。
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


## 2026-09-18 Mode G 生产审核与可玩性修复

来源：owner 全面审核优化请求。兼容分类均为 COMPAT；文案与守卫修复为 SAFE。当前证据最高 L2，实机待 owner 验证；完整覆盖与验收见 `docs/reports/reviews/2026-09-19-ModeG异常路径复核.md`。

| ID | 级别 | 已确认问题 | 修复与证据 |
| --- | --- | --- | --- |
| CR-2026-09-18-003 | P1 | 致命伤在 OnDead 注销 Boss 后被 OnHurt 早返丢弃，单发击杀可整波零贡献。 | **Fixed，待L3**。ModeGCombatTelemetry.HandleOnDead 在注销回调前复用 RecordDirectDamage；OnHurt 排除已死亡对象。官方事件顺序已核对，生产链接回归先红后绿。 |
| CR-2026-09-18-004 | P1 | 以 damageValue 而非官方 finalDamage 计算血量贡献，低穿甲可虚报、暴击会少计，零实际伤害也能堆进度。 | **Fixed，待L3**。改读官方结算值并拒绝非有限/非正值，减伤、暴击、零伤害执行回归通过。 |
| CR-2026-09-18-005 | P1 | HUD 声明污染/缓存溢出使挑战无效，但结算和下波预测仍能消费残缺样本。 | **Fixed，待L3**。IsWaveScoreValid 成为 HUD、轴判据、末击归因和波末结算共同入口，污染/溢出执行回归通过。 |
| CR-2026-09-18-006 | P1 | 普通弹药省略爆炸字段时官方按0处理，Mode G 以 NaN 判无效，使合法弹药无法学习。 | **Fixed，待L3**。爆炸参数采用官方缺省0，未知damageMultiplier及非法值仍拒绝；五发普通弹药可学习和点名的回归通过。 |
| CR-2026-09-18-007 | P2 | 属性双门槛满足后 HUD 提前显示清波即破解，隐藏相反武器系末击条件。 | **Fixed，待L3**。只有距离轴提前显示清波提示；属性轴保留武器系、进度及末击要求，真实格式函数回归通过。 |
| CR-2026-09-18-008 | P1 | 完整奖励计划直到九波胜利才构建，候选不足/缺 prefab 的确定性失败会浪费整局。 | **Fixed，待L3**。Initialize 校验并冻结10槽计划，缺失时拒绝启动交回既有退款事务；胜利按原档位截取。入口接线L1，分带、缺池、确定性及前缀回归L2。 |
| CR-2026-09-18-009 | P2 | Mode G 放弃键只检查官方 View，漏暂停/对话/拍照；结算页暂停仍自动消失，失败页显示未获得的奖励件数；官方宿敌直接显示原始key。 | **Fixed，待L3**。复用共享界面/暂停门，失败页明确无通关奖励，宿敌名经现有本地化入口读取。L1接线，界面观感/输入待L3。 |
| CR-2026-09-18-010 | P2 | 中英文攻略把学习说成必须命中、把休整空放说成不违规，省略属性末击和弹种不复用，并误称主动退出增加败北记录。 | **Fixed，待L3**。按真实生产条件修正文案，列出八项契约的配装/操作；轻量契约保留为准备型荣誉目标，不改稳定ID或存档语义。L1。 |
| CR-2026-09-18-011 | P2 | ModeGManagedBossAuxiliaryGuard 遍历整个工作区并按字符串判断消费，基线超时300秒；注释也能冒充激活接线。 | **Fixed，待L3**。限定女巫适配器和随从生产文件，剥注释核对绑定、提交拒绝、激活顺序和幂等释放；专项守卫36项全部通过，破坏接线反向验证单列。 |


## 2026-09-18 鸭皇图鉴生产审核

范围及最终证据见 `docs/reports/reviews/2026-09-18-鸭皇图鉴生产审核.md`。以下只记已确认问题；玩法指引与分页筛选取舍另记交付报告。

| ID | 级别 / 分类 | 已确认问题 | 修复与证据 |
| --- | --- | --- | --- |
| CR-2026-09-18-012 | P1 / COMPAT | CodexCodec.Decode 将非数组 entries 当空档，跳过坏项、重复 key 并截断超限数据；下一次正常存盘可能覆盖收藏。 | **Fixed（L1/L2，待 L3）**。严格验证数组、条目身份、统计类型/范围与 schema 整数；非法数据整体拒读进入共享写屏障，可选字段缺失仍兼容。 |
| CR-2026-09-18-013 | P1 / COMPAT | CodexKillCollector.RecordKill 直接改 Current，Store 拒绝后仍改变内存收藏并调用里程碑。 | **Fixed（L1/L2，待 L3）**。在 Clone 候选上修改；Store 接受后才发布目录和成就，已知写屏障/故障提前退出。 |
| CR-2026-09-18-014 | P2 / COMPAT | 公共池已含三个自定义 Boss，目录先按官方录入后跳过自定义分类；名字与静态 UI 文案停留在首次构建语言。 | **Fixed（L1/L2，待 L3）**。公共池排除自定义键后统一分类；名称现取本地化，UI 按语言变化刷新。 |
| CR-2026-09-18-015 | P2 / COMPAT | Boss 计时容量满时清空全部起点；目标转友军后死亡会提前退出而不清表；丧尸受伤路径每次拼接身份字符串。 | **Fixed（L1/L2，待 L3）**。满表只拒绝新计时，死亡先清理起点；五种丧尸 key 改用既有冻结常量。 |
| CR-2026-09-18-016 | P2 / COMPAT | 图鉴已保存的有效速杀在成就尚未保存时，重开面板只补累计成就，无法补判十秒成就。 | **Fixed（L1/L2，待 L3）**。面板补判真实已保存的有效最快用时，复用既有成就幂等接口；切槽清判定缓存。 |
| CR-2026-09-18-017 | P2 / COMPAT | CodexView.EnsureGridLayout 延迟 Destroy 官方 VerticalLayoutGroup 后同帧添加 GridLayoutGroup，违反同物体单 LayoutGroup 约束；每次打开全目录创建卡片/加载立绘。 | **Fixed（L1/L2，待 L3）**。即时移除克隆容器的旧布局；每页最多12卡按页取图，旧卡立即失活，销毁路径释放输入；新增呈现守卫。 |


## 2026-09-18 竞技场后山生产复审

### CR-2026-09-18-018 · P1 · COMPAT · 出击餐在局内换区时丢失

- 状态：fixed，L3 待 owner 实机。
- 位置：`Integration/BackMountain/BackMountainRuntimeModule.cs`。
- 证据与修复：原 OnSceneLoaded 无条件 ClearForRun，消费过的餐不能重挂。现以官方 raid ID、槽位和角色身份保留同局餐，结束/死亡/换槽清理；执行回归覆盖 additive 与角色销毁重建。
- 证据等级：L1 当前生产代码与官方源码；可隔离部分 L2 见 `BackMountainLifecycle` / `BackMountainPlayabilityGuard`。
- 验证需求：`docs/reports/reviews/2026-09-18-竞技场后山生产复审.md` 的逐步清单；不把离线结果当作实机表现。

### CR-2026-09-18-019 · P1 · COMPAT · 焚心椒零基数换弹增益与餐食提前结算

- 状态：fixed，L3 待 owner 实机。
- 位置：`Integration/BackMountain/RaidMealService.cs`。
- 证据与修复：官方 ItemAgent_Gun 用时间/(1+ReloadSpeedGain)，原 PercentageAdd 在零基数上无效；原代码先清登记后忽略 TryAdd 返回值。现显式 Add，所有 stat 成功才结算，失败移除部分效果并恢复待餐。
- 证据等级：L1 当前生产代码与官方源码；可隔离部分 L2 见 `BackMountainLifecycle` / `BackMountainPlayabilityGuard`。
- 验证需求：`docs/reports/reviews/2026-09-18-竞技场后山生产复审.md` 的逐步清单；不把离线结果当作实机表现。

### CR-2026-09-18-020 · P1 · COMPAT · 设施恢复被解锁总门挡住且偏好变更不刷新

- 状态：fixed，L3 待 owner 实机。
- 位置：`Integration/BackMountain/BackMountainRuntimeModule.cs`。
- 证据与修复：原 RefreshFacilitiesForScene 先检查任意设施解锁，使菜地棘轮无法独立恢复；OnUpdate 不响应 UnlockAll。现按基地场景名早期恢复、就绪补试、偏好变化刷新，种植开放前写入恢复标记。
- 证据等级：L1 当前生产代码与官方源码；可隔离部分 L2 见 `BackMountainLifecycle` / `BackMountainPlayabilityGuard`。
- 验证需求：`docs/reports/reviews/2026-09-18-竞技场后山生产复审.md` 的逐步清单；不把离线结果当作实机表现。

### CR-2026-09-18-021 · P1 · COMPAT · 六件后山物品没有官方描述键

- 状态：fixed，L3 待 owner 实机。
- 位置：`Integration/BackMountain/BackMountainItems.cs`。
- 证据与修复：官方 Item.DescriptionRaw 恒为 DisplayNameRaw+_Desc，旧反射写描述不会改变该 getter；旧 InjectLocalization 只注入名称。现补齐中英文 _Desc，含实际效果与覆盖规则，使用行为复用共享绑定。
- 证据等级：L1 当前生产代码与官方源码；可隔离部分 L2 见 `BackMountainLifecycle` / `BackMountainPlayabilityGuard`。
- 验证需求：`docs/reports/reviews/2026-09-18-竞技场后山生产复审.md` 的逐步清单；不把离线结果当作实机表现。

### CR-2026-09-18-022 · P1 · COMPAT · 展示柜没有占用输入且可见动作与资格脱节

- 状态：fixed，L3 待 owner 实机。
- 位置：`Integration/BackMountain/ShowcaseUI.cs`。
- 证据与修复：CreateCanvasRoot 只建 Raycaster，不禁用玩家输入。现取得共享模态租约，Esc/销毁/换槽释放；绘制与执行复用资格判定，补 Backpack 可达性。生产生命周期提取回归验证旧画布不会关闭新画布。
- 证据等级：L1 当前生产代码与官方源码；可隔离部分 L2 见 `BackMountainLifecycle` / `BackMountainPlayabilityGuard`。
- 验证需求：`docs/reports/reviews/2026-09-18-竞技场后山生产复审.md` 的逐步清单；不把离线结果当作实机表现。

### CR-2026-09-18-023 · P2 · COMPAT · 展示柜模板跨槽重复追加且卸载不释放资源

- 状态：fixed，L3 待 owner 实机。
- 位置：`Integration/BackMountain/ShowcaseBuildingBuilder.cs`。
- 证据与修复：旧换槽只移除 infos，下一次注入再次追加同一 prefab；Cleanup 只清图标引用。现按引用去重，真实注入成功才置完成，清理 own prefab/材质/纹理/目录；同步采用 URP 材质。资源回收和渲染仍需 L3。
- 证据等级：L1 当前生产代码与官方源码；可隔离部分 L2 见 `BackMountainLifecycle` / `BackMountainPlayabilityGuard`。
- 验证需求：`docs/reports/reviews/2026-09-18-竞技场后山生产复审.md` 的逐步清单；不把离线结果当作实机表现。

### CR-2026-09-18-024 · P2 · COMPAT · 展示柜低层登记可绕过食材排除，F3 使用该漏洞写探针

- 状态：fixed，L3 待 owner 实机。
- 位置：`Integration/BackMountain/ShowcaseService.cs`。
- 证据与修复：旧 TryDisplay(int) 不验证目录品质与后山食材，F3 正用餐食 TypeID 登记，满柜时又无法选探针。现服务复核、拒绝非法输入，异常/超容量集合保留原文并写保护，共享 JSON 读写；F3 改只读，事务由隔离回归执行。
- 证据等级：L1 当前生产代码与官方源码；可隔离部分 L2 见 `BackMountainLifecycle` / `BackMountainPlayabilityGuard`。
- 验证需求：`docs/reports/reviews/2026-09-18-竞技场后山生产复审.md` 的逐步清单；不把离线结果当作实机表现。

### CR-2026-09-18-025 · P2 · COMPAT / SCHEMA+ · 英文点唱机仍显示硬编码中文曲名

- 状态：fixed，L3 待 owner 实机。（2026-09-22 复核：`BgmTracks.json` 的 `musicNameEn`、`BossBgmTrackTable` 可选解析、`JukeboxTrackInjector` 按路径原位替换、部署段与 L2 均在位；`BackMountainStructureGuard` 加三条防回归断言。仍待 L3。）
- 位置：`Integration/BackMountain/JukeboxTrackInjector.cs`。
- 证据与修复：原曲目表只有 musicName，注入器无语言路径且按标题判重。新增可选 musicNameEn 并缺省回退旧名；按音频路径更新原槽，语言切换不改索引、不重复追加。
- 证据等级：L1 当前生产代码与官方源码；可隔离部分 L2 见 `BackMountainLifecycle` / `BackMountainPlayabilityGuard`。
- 验证需求：`docs/reports/reviews/2026-09-18-竞技场后山生产复审.md` 的逐步清单；不把离线结果当作实机表现。


Mode G 本轮收尾补证（2026-09-18）：最终专项守卫37 PASS、生产源码夹具19 PASS、三个守卫10次反向验证全部按预期转红并逐字还原。CR-2026-09-18-009同时补齐HUD与结算页画布构建异常的owner回收。CR-2026-09-18-011的守卫本身已完成L2验证；其守护的真实女巫激活/回收仍待L3。隔离基线加本轮Mode G修改已正式编译成功；全工作区编译/守卫受并行新武器开发影响未全绿。详情与人工清单见上述报告。


## 2026-09-18 鸭王征程生产审核与体验优化

来源：owner 要求全面审核并优化。以下为当前代码确认并已修复的问题；未做 L3，完整范围与验收见 `docs/reports/reviews/2026-09-22-鸭王征程重设计交付.md`。第三章去等待属于体验取舍，不列为缺陷。


### CR-2026-09-22-001 · documented · COMPAT / SCHEMA+ / WIRE+ · 鸭王征程改由官方 Jeff 发放、后山三设施改接官方建筑（设计决策归档）

- 状态：documented（owner 2026-09-22 拍板；不是缺陷）。
- 位置：`Utilities/OfficialQuests/`、`Campaign/CampaignOfficialQuestClient.cs`、`Campaign/CampaignQuestTable.cs`、`Campaign/CampaignBaseObjectives.cs`、`Integration/BackMountain/GardenConstructionSite.cs`、`ShowcaseDisplayScanner.cs`、`ShowcaseTagInjector.cs`、`ShowcaseService.cs`。
- 决定与理由：①征程六章接官方 `Duckov.Quests`（590101–590106，给予者 Jeff=1），投影核心只有一份，Mod 存档仍是权威；旧自绘公告板 / 面板退役，老档已建的保留。②官方基地菜地工地的付费交互父物体默认 inactive，玩家原本没有途径建成，Mod 只激活父物体、不写官方键；官方将来自己放出即 no-op。③自建「战利品登记簿」退役，加成改按官方陈列柜实摆计算（官方柜是基地存储，不带出击，换来真实陈列）；回退 = judges 改回读缓存、`sourceVersion` 回 1。④「建好菜地」放第二章（token 只在交付时发；上一章解锁的东西是下一章目标）。
- 证据等级：L1 + L2；L3 待 owner（清单见 `docs/reports/reviews/2026-09-22-鸭王征程重设计交付.md`）。
- 待 owner 决定：官方陈列柜槽位标签（需 F3 `SHOWCASE_OFFICIAL_PROBE` 实机读出）；若官方陈列柜被 `requireQuests` 门住是否改官方数据；官方将来改成任务解锁菜地时 Mod 是否让位。

### CR-2026-09-22-002 · P0 · COMPAT · ResourceBundleLoader 的 pending 键按字符串比较，同一 bundle 两种路径写法触发同步二次加载，全部 Mod 物品注册失败

- 状态：Fixed（L2），待 L3。
- 位置：`Utilities/ResourceBundleLoader.cs`（`Prepare` / `LoadFromFile` / `Pending.ReleaseOwned`）。
- 证据：owner 实机 `Player.log`（2026-09-22 12:38）47 条 `can't be loaded because another AssetBundle with the same files is already loaded`，栈为 `Prepare` consumer → `EnsureRegistered` / `ItemFactory.LoadBundleInternal` → `ResourceBundleLoader.LoadFromFile` → `AssetBundle.LoadFromFile`；随后 `PlayerStorage.Load` 与 `CreateMainCharacterAsync` 因 prefab 为 null 抛 NRE。
- 根因：`FactoryResourceLoading.RunSpecial` 以 `Path.Combine(GetModPath(), "Assets/Items/x")` 为键，消费方以 `Path.Combine(modDir, "Assets", "Items", "x")` 查，`Dictionary<string,…>(OrdinalIgnoreCase)` 视为两个键；引入于 `60bb84b6`（09-20），此后未实机。
- 修复：键统一经 `NormalizeKey`（`Path.GetFullPath`），`Pending.Key` 持有归一化键；夹具 `ResourceProduction` 加别名用例（旧代码转红）。
- 兼容性：COMPAT；不改任何调用方路径写法。

### CR-2026-09-22-003 · P2 · COMPAT · ShowcaseTagInjector 经补丁过的 ItemAssetsCollection.GetPrefab 枚举全部物品，等于在 bootstrap 里对每个 TypeID 强制同步按需注册

- 状态：Fixed（L2）。
- 位置：`Integration/BackMountain/ShowcaseTagInjector.cs`（`IsShowcaseTrophy` / `EnsureTagged`）。
- 问题：文件头承诺「不 force-load bundle」，但 `ItemAssetsCollection.GetPrefab` 被 `ItemAssetsCollectionDynamicRegistrationPatch` 接管，对未注册的 TypeID 会同步跑 `EnsureRegistered`，异步预热失去意义，且在 CR-2026-09-22-002 存在时每件都撞二次加载。
- 修复：改走 `BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring`（`prefabCheckDepth` 旁路补丁），未注册的下次进基地再补；`BackMountainPlayabilityGuard` 加断言并反向验证。

### CR-2026-09-22-004 · P2 · COMPAT · 基地建筑 bundle 每次进基地被装配管线重复异步加载，Unity 每次报四条 "already loaded"

- 状态：Fixed（L2），待 L3。
- 位置：`Integration/FactoryResourceLoading.cs`（`RunSpecial`）、`Integration/IntegrationDeferredBootstrap.cs` 四处调用、`DailyReportMailboxBuilder` / `PetNestBuilder` 新增 `IsBundleLoaded`。
- 证据：owner 实机 `Player.log`（2026-09-22 13:29）weddingchapel / starwish_fountain / petnest_relic_nest / bossrush_daily_mailbox 各 9 条，栈在原生线程（`LoadFromFileAsync`），紧随其后是各建筑「已注入，跳过」。
- 根因：`60bb84b6` 把建筑加载改成 `RunSpecial` 无条件 `Prepare`，但建筑注入器持有静态 bundle 不释放；`LoadDirectory` 有 `loaded(name)` 门而 `RunSpecial` 没有。
- 修复：`RunSpecial(..., Func<bool> alreadyLoaded = null)`，持有中直接交给消费方；判据与消费方的持有字段同源。

### CR-2026-09-22-005 · P3 · SAFE · F3 用例 DATA_CODEX_FILTER_REFRESH 的判据停在锁定卡之前

- 状态：Fixed（L2），待 L3。
- 位置：`DebugAndTools/F3GameplayValidationCodex.cs`、`Integration/Codex/CodexBossCatalog.cs`（`CodexBossInfo.IsInCurrentPool`）。
- 证据：本轮 F3 `official=45->45->45` FAIL；09-16 之前 `47->46->47` PASS；中间 e80b1c3f 让名单把筛掉的官方 Boss 补成锁定卡（owner 2026-09-20 拍板）。
- 修复：用例改判池子给出的条目少一格再恢复，目录不缩；名单读不出（fail-open）时仍允许少一格。

### CR-2026-09-22-006 · P2 · COMPAT · 陈列加成设计押在官方「陈列柜」上，而该建筑是官方废弃的、建造菜单里没有

- 状态：Fixed（L2），待 L3。
- 位置：`Integration/BackMountain/ShowcaseTrophyCatalog.cs`（原 `ShowcaseTagInjector.cs`）、`ShowcaseDisplayScanner.cs`、征程文案（`CampaignContentCatalog` / `CampaignDialoguePlayer` / `CampaignLocalization` / `Chapters.json`）、Wiki。
- 证据：owner 2026-09-22 实机确认；F3 `SHOWCASE_OFFICIAL_PROBE`（13:29）场上只有 Gun 架（12 槽 `Gun`）、假人（7 槽按装备类型）、基地皮肤柜，Mod 枪（500035）与护甲（500004）已经摆在架子上。
- 根因：设计阶段从反编译源看到 `Tag_ShowCase` 与 `Showcase_01` 就当成可建，没有核对建造菜单数据。
- 修复：撤掉 `ShowCase` 标签注入（Mod 装备自带槽位标签），加成按枪械展示架 / 假人实摆计算（采集与公式不变）；玩家可见文案全部改口；`BackMountainPlayabilityGuard` 禁止再补展示标签。
- 教训：「官方有这个建筑类」不等于「玩家建得出来」，与菜地工地一样要先看场景 / 建造表。

### CR-2026-09-22-007 · P1 · COMPAT · 菜地指引写错入口（「建设面板」），造价与粑粑来源没告诉玩家；产物在菜地里长成克隆源的 3D 模型

- 状态：Fixed（L2），待 L3。
- 位置：`Campaign/CampaignDialoguePlayer.cs`（ch1 交付第 3 句）、`Campaign/CampaignContentCatalog.cs`（ch2 `GetEntryHint`、ch1 `GetDeliveredNotice`）、`Integration/BackMountain/BackMountainItems.cs`（`ConfigureItem`）、Wiki 中英。
- 证据：`鸭科夫源码` `ConstructionSite` / `CostTaker` / `Garden` / `Crop` / `GardenView`；level5 重解 `GardenConstruct`：`dontSave=0`、`money=0`、`items=[(98,1),(938,9)]`；`resources.assets` 扫 `Item` 组件 98=Shovel（铲子）、938=Shit（粑粑 / Poop）；官方 Wiki：粑粑只掉自蝇蝇队员 / 队长，分解粑粑枪射程模组得 5。
- 问题：①菜地是基地里的官方工地，付费交互由 Mod 打开，不在建造菜单，文案却让玩家去建设面板找；②第二章前置要 9 坨粑粑，玩家不知道去哪弄；③产物 prefab 沿克隆链带着便携安全区装置的模型，官方 `Crop.RefreshDisplayInstance` 用 `item.ItemGraphic` 摆作物，`InteractablePickup` 同理。
- 修复：三处文案改为「带铲子 ×1、粑粑 ×9 去工地交钱动工」并在对话里点出蝇蝇；Wiki 写清造价、配方与掉落地点；`ConfigureItem` 反射清空私有 `itemGraphic`，官方退回 `spriteGraphicPfb` 图标立牌（官方无模型物品同一条路）。
- 未核实：`GardenView.WateringTask` 状态机不在反编译源里，浇水按免费假设。
- 待 owner：官方造价是否接受为第二章前置；改价要走 `CostTaker.SetCost`（改官方数值，§10）。

### CR-2026-09-18-026 · P1 / COMPAT · 终章异步生成未与标准波次隔离，旧失败可清掉后继挑战

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignFinalBoss.cs`。
- 原因与修复：原工厂未传 isNonWaveSpawn，使用默认波次登记；旧请求 catch 无条件清理。现在走原工厂非波次路径，成功/异常都校验生成编号；死亡、让路、切槽、卸载回收，取消独白等待。
- 验证：L1/L2：真实编排抽取回归、非波次/旧异常/取消反向探针。

### CR-2026-09-18-027 · P1 / COMPAT · 目标终点入队失败后会随离场丢失重试机会

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignProgressService.cs`。
- 原因与修复：原目标完成只依赖局内追踪再次通知，终章死亡后立刻 ResetSession，一次性事件无法重发。服务保存会话内未入队事实，每秒重试；待交付和已达标未入队均不允许放弃，切槽清旧事实。
- 验证：L1/L2：ContentTransactions 执行一次终点、离场重试、切槽和重复操作；重试早返变异转红。

### CR-2026-09-18-028 · P2 / COMPAT · 待交付章节可被重新武装，完成计数继续变化

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignObjectiveTracker.cs`。
- 原因与修复：原 EnsureArmedFor 只认活动章节定义，ReadyToDeliver 仍被 GetActiveChapterDef 返回；计数与无伤判断在完成后继续消费事件。改为只武装 ContractActive，完成冻结、计数封顶，并重置同模式新局的波次观察。
- 验证：L1/L2：真实追踪器与模式桥覆盖；重新武装变异转红。

### CR-2026-09-18-029 · P2 / COMPAT · 敌方目标未按实际敌我关系过滤

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignObjectiveCollector.cs`。
- 原因与修复：原主玩家致命一击通过后直接统计，未排除友军与中立头目。复用官方 Team.IsEnemy，拒绝非敌对受害者，保留模式现有击杀归属。
- 验证：L1/L2：友军/中立/敌对回归，移除敌对门控变异转红。

### CR-2026-09-18-030 · P2 / COMPAT · 第二章近战目标缺少稳定开局工具

- 状态：Fixed，待 L3。
- 位置：`ModeD/ModeDEquipment_StarterKit.cs`。
- 原因与修复：原近战武器只有 40% 开局概率，而契约要求五次近战击杀且入场禁止自带装备。仅 ModeD 活动近战契约保证调用原近战配装；其它模式共用整备不获得这个保证，待交付也不触发。
- 验证：L1/L2：实际入口接线、契约判据回归、移除 ModeD 限定的反向探针；物品实例化仍待 L3。

### CR-2026-09-18-031 · P2 / COMPAT · 公告板未取得模态输入，HUD 语言与高度未随内容更新

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignBoardView.cs、Campaign/CampaignHud.cs`。
- 原因与修复：原建 Canvas 只提供 Raycaster；模态界面未占有输入，HUD 脏检查不含语言且固定高度。复用 ModalInputLease 并在 Esc/销毁/切槽释放；语言参与脏检查、正文实测量高；待交付和失败反馈明确。
- 验证：L1；L2 结构接线及模态/语言反向探针。实际输入栈、渲染和双语布局待 L3。

### CR-2026-09-18-032 · P2 / COMPAT · 线索镜像可残留旧槽状态，旧对话等待未取消

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignNoteBridge.cs、Campaign/CampaignDialoguePlayer.cs`。
- 原因与修复：原注册只补新条目的字典，解锁只向官方单向追加；常驻 actor 的异步对话可跨槽继续。改为权威存档双向校正列表/字典/解锁，读故障不反锁；共享取消 token 与代际门保护交付反馈。
- 验证：L1/L2：真实 Note 桥回归、取消方法/终章编排、共享 DialogueManager 回归及反向探针；官方图鉴界面待 L3。

### CR-2026-09-18-033 · P2 / COMPAT · 程序化资源缺少完整归属，重复创建/卸载可残留材质和图标

- 状态：Fixed，待 L3。
- 位置：`Campaign/CampaignAssetCache.cs、Campaign/CampaignBoardBuilder.cs`。
- 原因与修复：召唤石子件原分别 new Material，公告板运行时图标与材质未进入统一账本。共用现有 AssetCache 的材质和 ownedObjects，运行时纹理/sprite/材质随 owner 回收，染色工具从宿主原样移入资源类型；无新增缓存或调度器。
- 验证：L1：分配与销毁路径核对；L2 资源归属接线守卫。实际 Unity 对象回收与帧耗待 L3。


提交集成补记（OPERATIONAL，L1/L2）：上游 f3d28dc 新增 SkyIslandBossVoice.cs 未登记 compile_official.bat，当前 HEAD 的 BossForge 已引用它；由 OfficialCompileListFileExistenceGuard 可直接检出。本次仅补显式编译清单一行，使新提交可独立构建，不改该模块玩法。

## 2026-09-20 第三轮（外部审查 11 条复核）

### CR-2026-09-20-013 · P1 / COMPAT · 冰霜冻结「假成功」：AddBuff 后无条件报成功

- 状态：Fixed，待 L3。
- 位置：`Integration/Bonus/FrostSetBonus.cs`。
- 原因与修复：官方 `CharacterMainControl.AddBuff` 返回 void，且有三条静默 no-op 路径
  （`buffResist` 命中该 `ExclusiveTag`、同 tag 已有更高 `ExclusiveTagPriority`、
  同 tag 同优先级但现存剩余时间更长）；抗冻目标走第一条。旧代码调用后直接 `return true`，
  兜底减速在 `CharacterItem == null` 早返后外层也仍报成功。于是目标没被冻住，
  却照样播音效、出特效、扣掉 6 秒反击冷却。现在回读 `target.HasBuff(freezeBuff.ID)`
  才算成功，兜底减速按「两条速度 Stat 是否存在 + 协程是否真的起得来」回报真实结果
  （起不来当场回滚，不留永久减速）；反击冷却挪进冻结成功分支。
- 验证：L1 官方源码核对（`鸭科夫源码/TeamSoda.Duckov.Core/CharacterMainControl.cs:2199`
  与 `Duckov/Buffs/CharacterBuffManager.cs:55`）；L2 `SetBonusLifecycleGuard` 新断言 +
  `SetBonusCoroutines`「冻结失败不播音效」；反向探针转红。实际冻结表现待 L3。

### CR-2026-09-20-014 · P2 / COMPAT · 霜噬 / 雷噬在效果落地前就消耗冷却

- 状态：Fixed，待 L3。
- 位置：`Integration/Bonus/FrostSetBonus_Nova.cs`、`Integration/Bonus/ThunderSetBonus_Storm.cs`。
- 原因与修复：`lastFrostBiteTime` / `lastThunderBiteTime` 写在 `StartCoroutine` 之前。
  目标在 40~50 毫秒延迟窗口内被打死、期间脱下装备或切图、或雷噬扫不到其它敌人时，
  实际零伤害却已吃掉一整轮冷却（单挑 Boss 时「雷噬扫空」是最常见的一种）。
  改为在结算步真正走到「会造成伤害」那一步才写冷却，并用
  `frostBitePending` / `thunderBitePending` 挡住延迟窗口内的重复排队。
  反 DPS 三道闸不受影响：同一时刻仍最多一条在飞，伤害仍是常数，仍只认直接命中。
- 验证：L2 `SetBonusLifecycleGuard` 改为按**位置关系**断言（冷却写入必须在 `StartCoroutine`
  之后、`count <= 0` 早返之后、`TryApplyFrostFreeze` 成功分支之内），
  `SetBonusCoroutines` 新增扫空 / 延迟内死亡两组断言；三个反向探针转红。手感待 L3。

### CR-2026-09-20-015 · P2 / COMPAT · 官方 Boss 名单只参与分类，没补图鉴目录

- 状态：Fixed，待 L3。
- 位置：`Integration/Codex/CodexBossCatalog.cs`、`Integration/Codex/CodexOfficialBossRegistry.cs`。
- 原因与修复：目录来源只有 `GetFilteredEnemyPresets()`，被筛选器关掉或 preset 尚未被
  `InitializeEnemyPresets` 扫到的官方 Boss 连锁定卡都没有，「还差哪几只」查不到。
  新增第 1b 步 `AddOfficialRosterEntries()`，按有序的 `OfficialBossKeys()`（40 条）
  补成未解锁卡；名单读不出来时该步等于不存在（fail-open）。
  连带「全收集」分母改为官方全部 Boss + 3 自定义 + 5 丧尸，不再随筛选器缩水（有意为之）。
  顺带删除只剩夹具在用的死代码 `GetEncounterHint`。
- 验证：L2 `ContentThirdReviewFixes/Codex` 链接真实 registry 与仓库真实 JSON，
  断言「过滤池为空时 `Cname_StormBoss1` 仍有锁定卡」；反向探针转红。面板观感待 L3。

### CR-2026-09-20-016 · P2 / COMPAT · 日报底图与运行时重复绘制同一处控件

- 状态：Fixed，待 L3。
- 位置：`tools/gen_daily_report_ui.py`、`Integration/DailyReport/DailyReportUI_Dashboard.cs`。
- 原因与修复：签到格 / 签到按钮 / 图例色块被底图烤了一遍、运行时又画一遍，
  叠出双描边（运行时 9-slice 的四角透明，底图那层会透出来），且颜色两个来源已经漂了
  （C# `CellEmpty` 199,189,166 vs 脚本 226,219,205）。按钮的注释甚至写着「只放透明按钮」，
  代码却设了底色并套了皮。收敛成「底图只画不变的装饰，会变色的一律归运行时」，
  脚本不再画这三处也不再保留那几个颜色常量，底图已重出并部署。
- 验证：L2 `DailyReportPresentationGuard` 新增按版面表**去底图取色**的判据；
  反向探针（把按钮烤回 PNG）转红。观感待 L3。

### CR-2026-09-20-017 · P2 / COMPAT · 日报卡片内滚动实际滚不动

- 状态：Fixed，待 L3。
- 位置：`Integration/DailyReport/DailyReportUI_Dashboard.cs`。
- 原因与修复：内容 `sizeDelta` 写死成 viewport 高度，Clamped 模式下 `ScrollRect`
  认为内容刚好装得下，控件在、事件在，就是滚不到最后一行。改由
  `ContentSizeFitter.verticalFit = PreferredSize` 按 TMP 首选高度撑开。
- 验证：L2 守卫钉住「高度来自 ContentSizeFitter」且禁止再写死 `slice.height`；反向探针转红。
  实际滚轮交互待 L3。

### CR-2026-09-20-018 · P2 / OPERATIONAL · 六个音效文件夹从未被部署脚本拷出去

- 状态：Fixed，L2。
- 位置：`compile_official.bat`。
- 原因与修复：逐文件夹清单只列了 BGM / SkyIsland / SetBonus / NewWeapons；
  代码实际还读 Achievement、DragonKing、Goblin、Nurse、items、lottery 六个。
  它们在 owner 的游戏目录里存在是早年手工拷的，干净安装会静默无声
  （文件不在就跳过播放，编译 / 守卫 / 部署全绿）。许愿台大奖音乐与遗种巢异色揭晓
  复用的 `Assets/Sounds/lottery/special.mp3` 正在其中。
  改为整树 `xcopy /E` + 逐文件夹缺失告警（整树拷贝掩盖不了源目录本来就缺）。
- 验证：L2 新增 `tests/LooseSoundDeploymentGuard.py`（从 C# 抽出被读取的文件夹，
  断言脚本会拷且都在告警清单里），`SkyIslandMosquitoGuard` 同步改断言；两个反向探针转红。
  实跑构建输出 10 个文件夹全部部署、无缺失告警。

### CR-2026-09-20-019 · P2 / SCHEMA+ · 远征与纪念碑丢失炫彩 / 异色

- 状态：Fixed，待 L3。
- 位置：`PetNest/PetNestModels.cs`、`PetNestPersistenceCodec.cs`、`PetNestChroma.cs`、
  `PetNestExpeditionService.cs`、`PetNestUIPages.cs`、`PetNestExpeditionRevealView.cs`。
- 原因与修复：`PetNestChroma.Decorate` 只吃 `PetNestPetRecord`，而远征列表、翻牌卡与碑文
  显示的往往是真死结算后已被移出巢的崽——那正是最该显示异色金字的一档。
  新增 `SCHEMA+` 可选字段（`petShiny/petChromaA/petChromaB`、碑文 `chromaA/chromaB`，
  schemaVersion 不变，老档读出默认值即普通名字），新增脱离 PetRecord 的
  `Decorate` / `DescribePair` 重载，展示入口统一为 `DescribeDecoratedPetName`。
  远征卡刻意不设 `card.Shiny`：描边优先级会让异色顶掉亡命档的红边警示。
- 验证：L2 `ContentTransactions` 新增 7 条断言（已移除的崽仍显示异色与搭配名、Clone 保留、
  远征与纪念碑各自的存档往返、老档回落普通名字）；三个反向探针转红。观感待 L3。

### 复核后不成立（记录以免重复排查）

- **立绘 bundle 与作者工程不一致：refuted。** 比对对象错了：作者工程的 `AssetBundles/`
  是旧的临时输出目录，正式出口是 `ResourceRelease/`。逐文件核对 `ResourceRelease/Assets`
  与仓库、游戏目录**全部 SHA-256 一致**（含 `codex_portraits` 5,025,857 字节）。
  `AssetBundles/` 里的旧副本是陷阱（`petnest_relic_nest` 在那里只有 36 KB，正式版 1.5 MB），
  不要从那个目录重打。
- **报箱 / 公告栏 / 展示柜未接 3D 模型：已过期。** 四个 builder 都先走 bundle 加载、
  缺包才退回占位，四个 bundle 当日已产出并部署。bundle 内是否真含对应 prefab 只能 L3
  （本机无 UnityPy，LZ4 压缩包无法离线开箱）。
- **实机跑的不是第二轮产物：已过期。** 当日 21:19 已重建部署，本轮再次构建部署，
  `Build/BossRush.dll` 与游戏目录 SHA-256 一致 `6FF528E2…68F193EF`。

### 仍待实机

- **Mode H 九图**：`TryDeriveMap` 已是 fail-closed，斗士只落在真实刷新点、
  离场点不会回退到地下隔离点。导航连通性、视野、双方生成与安全退出只能 L3。


<!-- MANUAL 17 AUDIT 2026-09-22 -->

## 2026-09-22：20260920 人工实测 17 项当前实现复核

状态：以下均为 Open / 未修；7 项新登记，3 项旧结论因残余路径复开，2 项既有 Open finding 本轮重新取证仍成立。只登记本次已确认事实，不把待 L3 和需求口径差异列作缺陷。

完整逐子要求、触发链、官方资源/代码证据、最小修复与人工清单见 [完整报告](docs/reports/testing/20260922人工实测复核修复记录.md)。本轮未修生产代码、未重打包/真实部署、未启动游戏或访问玩家存档。

| ID | 严重度 / 分类 | 当前问题、准确锚点与玩家影响 | 证据 | 最小建议 |
| --- | --- | --- | --- | --- |
| CR-2026-09-22-011 | P1 / COMPAT | 冰原掠夺者 `Cname_RaiderIce` 被补入目录，但官方唯一预设 `EnemyPreset_Snow_Raider` 的 `isBoss=false`，`Integration/Codex/CodexKillCollector.cs:289` 拒收；全收集被该卡阻断。目录入口 `Integration/Codex/CodexBossCatalog.cs:255`。 | L1 + L2：实际官方资源字段、现机官方 DLL、生产 Catalog/Collector 探针：`listed=True recorded=False ... complete=False`。详见附录 A。 | 让采集资格与权威目录身份一致，保留玩家归属、H 排除和自定义/丧尸优先级；不通过改官方预设的全局 Boss 身份修补图鉴。 |
| CR-2026-09-22-012 | P1 / COMPAT | Mode H 已把玩家放到看台，旧普通传送等待结束后又搬到 custom 点：`Integration/BossRushIntegration_StartAndScene.cs:431` → `Integration/BossRushIntegration_TravelAndSetup.cs:121`，H 早退在同文件 `:319`，已经太晚。七张图两点相差 11.24–23.76 m。 | L1 + L2 点位复算；实际官方 async 加载顺序也存在后续覆盖源。未宣称九图全部必失败。详见附录 B。 | 普通传送在入口和等待后排除 typed H；H 租约等待当前角色、目标子场景和官方传送就绪，并带 generation 取消。 |
| CR-2026-09-22-013 | P1 / COMPAT | 远征出发/放生清 `deployedPetId` 后未通知实体层：`PetNest/PetNestExpeditionService.cs:305`、`PetNest/PetNestService.cs:289`；已激活的宠和光环继续存在，后续记录甚至可能被删除。 | L1 实体维护链 + L2 生产事务/通知探针。详见附录 C 的 PN-01。 | 成功提交所有席位变更后统一通知，复用现有代数和 CleanupOnce；失败事务不提前回收。 |
| CR-2026-09-22-014 | P1 / OPERATIONAL | 当前管线 D 盘 `Duckov_Data/Mods/BossRush` 缺 13 个发布包，包含图鉴、生产图标、四建筑；已部署 DLL 的类型集和霜冠包内姿态不含当前完整实现。`compile_official.bat:1097`、`:1391` 为目标路径依据。 | L2 文件清单、包内姿态、DLL 元数据、VerifyOnly 失败。另有 59 包字节不同，仅凭该数字不判断全部内容损坏。 | 后续修复完成后向核实的实际目标交付正式 DLL/全部清单包/散装数据，验包、验类型、验路径后由 owner 启动验收。本轮不执行。 |
| CR-2026-09-22-015 | P2 / COMPAT | 旧套装协程先清新请求的 pending，再发现 generation 失效；第三击可重排，结算又不复核冷却。`Integration/Bonus/FrostSetBonus_Nova.cs:98`、`Integration/Bonus/ThunderSetBonus_Storm.cs:105`。 | L2：两套装都复现 `extraQueued=1 hitsWithin40ms=2`。固定低伤并不能满足内置冷却要求。 | pending 绑定请求 owner，旧代数不得写新状态；结算重新核冷却。保留空结算不扣冷却。 |
| CR-2026-09-20-016（复开） | P2 / OPERATIONAL | raw 日报底图移除了动态块，但正式优先加载的 `Assets/ui/production_icons` 内仍烤旧签到格/按钮/图例。`Integration/DailyReport/DailyReportBackground.cs:36`、`Integration/ProductionIconCache.cs:29`。 | L1 + L2 三端内容取样：raw 动态位均纸色，作者源及包/仓库包保留旧色。不是实际截图。 | 更新作者生产图标输入并重导入/重打包，检查实际 alias 像素，不能仅重出 raw PNG。 |
| CR-2026-09-20-017（复开） | P2 / COMPAT | PreferredSize 修了纵向高度，但横向 stretch 保留旧 `sizeDelta.x`，正文实际宽是 viewport 两倍；遮罩裁字且不能横滚。`Integration/DailyReport/DailyReportUI_Dashboard.cs:243`、`:260`、`:264`、`:268`。 | L1 + L2 尺寸复算；现有 DailyReportPresentationGuard 仍 PASS，未覆盖此条件。 | stretch 后清 `sizeDelta.x`，保留纵向 PreferredSize；验首字、末行、宽度和中英文。 |
| CR-2026-09-20-019（复开） | P2 / COMPAT | 升级前在途记录没有颜色快照，原彩宠真死移除后翻牌退回裸名，尽管结算前原宠颜色仍在。`PetNest/PetNestExpeditionService.cs:416`、`:452`；`PetNest/PetNestModels.cs:649`。 | L2：结算前 Shiny，结算后 `LegacyCub`，纪念碑仍保有异色/black。详见 PN-02。 | 删除原宠前从同 petId 补齐缺失外观快照；不猜无来源的历史死亡记录。 |
| CR-2026-09-21-034（仍未修） | P2 / COMPAT | 远征奖励只判 InstantiateSync 非 null，官方已知 TypeID 缺 prefab 时返回非 null FallbackItem，仍消费奖励游标。`PetNest/PetNestExpeditionService.cs:936`、`:961`；官方 `鸭科夫源码/ItemStatsSystem/ItemAssetsCollection.cs:147`。 | 本轮重新取证 L1 + L2：fallback 被投递，`grantedLootUnits=1,rewardsGranted=true`，prefab 预检 0 次。 | 投递前核真实 prefab，失败保留欠账和游标，恢复后只交付一次。 |
| CR-2026-09-22-016 | P2 / COMPAT | 已远征的选中宠仍能走目的地→风险→出发菜单，但服务必拒绝。`PetNest/PetNestUIPages.cs:531`、`:561`；`PetNest/PetNestExpeditionService.cs:254`。 | L1，PN-03。没有发现重复成功派遣，不夸大为重复扣款。 | 显示和执行复用可派遣判据；锁定宠正文说明，改选其他宠后再挂出发卡。 |
| CR-2026-09-22-017 | P2 / COMPAT | 远征翻牌仍以 WaitForSecondsRealtime 自动推进且标已翻，缺孵化已有的暂停门。`PetNest/PetNestExpeditionRevealView.cs:196`、`:206`。 | L1 门控遗漏；从该模态界面调出官方暂停的具体操作尚未 L3 验证，不能称已复现错过结果。 | 复用暂停感知等待；暂停期间不推进或 MarkRevealed。 |
| CR-2026-09-21-038（仍未修） | P2 / SAFE / OPERATIONAL | 云蚋守卫仍要求旧 `Assets\\Sounds\\SkyIsland\\*.wav` 语句，构建已整树复制，导致全量门禁红。`tests/SkyIslandMosquitoGuard.py:158` 对比 `compile_official.bat:1224`。 | 本轮 L2 全量实跑重现；是守卫漂移，不是音效缺失证据。 | 守卫随当前部署语义更新，并做反向验证；不能加白名单或删除检查求绿。 |

旧 CR-2026-09-20-016 的 raw 修正、017 的纵向高度修正、019 的新快照修正仍成立；复开仅表示它们分别未覆盖正式包内容、横向 stretch 宽度和升级前在途记录。历史当日的部署记录不等同于当前 D 目标已部署。
