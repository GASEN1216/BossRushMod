# FIX_TRACKER.md — 修复状态与兼容性流水账

更早的完整记录见 `archive/`；近期已闭环的大篇幅审计正文也按月份存档，当前文件保留索引与未闭环条目。

**2026-11-20 · COMPAT · L1/L2**　玩家反馈退出重进后武器消失、仓库物品变问号。修复冷启动注册、分包手持模型绑定、部分失败重试和克隆/异步资源归属。角色和仓库合成档往返、重复 TypeID 与真实包来源已纳入自动回归。故障证据与玩家侧验证边界见 [CR-2026-11-20-001 至 003](CODE_REVIEW_FINDINGS.md)。随 v2.6.1 发版：`info.ini` 升 2.6.1，WikiContent 新增中英 `changelog__v2_6_1` 并补总览，工坊简介同步；L3 待玩家侧确认。

## 2026-11-27 v2.7.0 发版：文案润色收口、日志补齐（COMPAT；不改 Key / TypeID / 存档；L2 + 正式真编译；L3 待 owner）

- 依据：owner「请你看看准备好没有，我们准备发版」，复核后「全部补齐再提交」。
- 文案润色：别的会话留在工作区、没登记的一轮改动，本次逐项对过代码后并入。
  - 叮当统一写男性「他 / his」：试用品委托提示、注释、Wiki 各页；本次补 v2.3.8 旧日志与英文 highlights。
  - 结婚过场：叮当、闸伯（`tidegate_zhabo`）、无声钟守（`sky_bellkeeper`）各写一套四句，见 `NPCMarriageSystem.BuildMarriageCutsceneDialogues`；其余 NPC 仍用通用四句。闸伯在 `DuckNpcs.json` 补 7 类婚后台词。
  - 叮当：送礼被嫌弃时换成专属台词，好感台词去掉傲娇口癖；五种危险试用品各有一句完成台词（`DangerousPrototypeRuntimeModule.PrototypeCompleteLine`）。
  - 出征令：战帖印文、压轴横幅、天象 / 战场印改成双语（`ArenaExpeditionTuning`、`ArenaInkTitleCard`、`ArenaExpeditionHud`）；断补提示改写「消耗品」；悬赏令英文按单复数取词。
  - 交互名：「开始第一波」「查看宿命回响」改走本地化键 `BossRush_StartFirstWave` / `BossRush_ModeG_Interact`，两个键已在 `LocalizationInjector_NpcUiAndItems` 注入；`BossRush_ModeG_Preview` 留给 HUD。
  - 术语：英文 Pet Nest → Relic Nest、cub → hatchling、Test Area 37 → Experimental Area 37，成就英文名与「Residual Sun Shield Shard」等对齐物品名。船票改名「BossRush 船票 / BossRush Ticket」，本次补 Mode F / G 退款提示、许愿台奖励英文名、Wiki 关键物品页与 a.md 开头。
  - 物品说明写实：平安符写明「生命低于 50% 受击、10% 概率回满、每图 1 次」，对照 `PeaceCharmConfig` / `PeaceCharmRuntime`；砖石、钻石写明「使用即召唤叮当」。
  - 潮闸镇：闸伯的信和剧情提示把竹筏位置改成「下水道东头那条水道的最北头」，与 `TidegateEntryPoint` 注释一致（离线读官方场景）。本次同步 Wiki 潮闸镇页和 `GameplayCoverage.json` 人工步骤。
  - 其余：英文旧版本日志 26 篇（v1.6.2 起）里残留的中文译成英文、术语对齐；README 中英更新到 v2.7.0；`BossRushMoneyTransfer` 与 `MoneyDeliveryNetDeltaGuard` 的注释改记 11-27 玩家确认的根因（前置 HarmonyLib 排在后面），判定逻辑不变。
- 发版：
  - `info.ini` 升 2.7.0，已在游戏目录。
  - `changelog__v2_7_0` 中英补上加码、天象双刃与回响 / 沉疴 / 破绽。「云上的坐标」改写为前置排序这一根因，并注明发钱判定为何保留；highlights 中英同步。
  - 上级目录 a.md / b.md 的 v2.7.0 条目加上加码，`workshop_description_budget.py`：a.md 7696、b.md 7695，均 OK。
  - 生成页用 `npm --prefix wiki-site run build` 整站同步，比同步前后的 git status，只多出预期的 5 个生成页。
- 夹具：`ModuleOwnerCleanup` 自 97584f07 起编译失败。原因是生产文件 `WavesArenaRuntimeModule_BossAccess.cs` 改调 `LiveExpeditionRaise()` 和两参数的 `HidesMapBeacons`，那一轮只跑了两项隔离回归，没发现。本次在 `tests/fixtures/ModuleOwnerCleanup/Program.cs` 补上这两个只供编译的替身，断言不变。
- 证据：
  - 全量守卫 800 PASS、0 新红。
  - 正式真编译 0 错 0 警告，`Build/BossRush.dll` 与游戏目录 SHA-256 同为 `5971E5F5…713C`，Dev 标识缺席，81 个包核对通过。
  - 执行回归：全量跑完，补夹具后 `ModuleOwnerCleanup` PASS。`FieldAdventureVisuals` 换到新证据目录后 PASS（含反向探针）。`Build/FieldAdventureVisuals/UnityProject/Library/SourceAssetDB` 已损坏（`MDB_INVALID`），这次没有删它。
  - `npm --prefix wiki-site run build` 通过。
  - 未进游戏，未读写玩家存档。
- [L3-REL-01] 新档读闸伯的信，照信里写的「东头那条水道」走到竹筏。不合格：信里的方向和竹筏实际位置对不上（这条位置至今只有离线读场景的依据）。
- [L3-REL-02] 中文、英文各进一次竞技场：路牌「开始第一波」和宿命回响的交互名都显示文字，不出现 `*BossRush_...*`；英文下战帖印显示 CHILL / STD / HARD / DOOM，压轴横幅显示 FINAL / HEADLINER，都不截字。
- [L3-REL-03] 分别与叮当、闸伯、无声钟守结婚：各自播放专属的四句过场，不再是通用台词。

## 2026-11-27 出征令改进三批：补强、天象双刃与新诅咒、加码阶梯（COMPAT；本地偏好 SCHEMA+；L2 + Dev / 正式真编译；L3 待 owner）

- 依据：复审后 owner 要改进方案，随后「都做，第三批搞成杀戮尖塔那种，战规简单到难排序，玩家自己选择加码。确保UI，交互逻辑，局内特效都是高端高级大气符合逻辑，具有史诗感」。
- 第一批（补强）：狂暴另加伤害 +25%；全场亲卫 + 影从共用上限 12；血债同一只敌人 0.6 秒内连中只记一次。`ArenaTrialsAffixes.cs`、`ArenaTrialsCurses.cs`、`ArenaTrialsTuning.cs`。
- 第二批（天象双刃与新诅咒）：雷暴下落雷劈敌 ×2 并演银色电弧（`ArenaTrialsHazards.HitLightning`、`ArenaTrialsFx.Smite`）；血月下倒下的波次首领多掉 1 件，掉落入口问到时演血月献祭（`ArenaTrialsSky.ClaimBloodMoonLoot`、`ArenaTrialsFx.BloodOffering`）；永夜压慢敌方反应 ×1.5 / 开火间隔 ×1.35，换天与收尾只在值仍是本战规写的那份时写回（`ArenaTrialNightDull`）。厄运池 5 → 8：回响（紫印收缩后再震，翻滚可躲）、沉疴（墨絮随身流血，翻滚即清）、破绽（玩家元素系数 ×1.15 / ×1.25，朱砂火星），新文件 `WavesArena/Trials/ArenaTrialsCurseActs.cs`。
- 第三批（加码）：新文件 `WavesArena/ArenaExpeditionRaise.cs`（梯子、`ApplyTo`、奖励、本地偏好两组键）与 `WavesArena/ArenaExpeditionRaiseRow.cs`（朱印台阶：逐枚落印、朱笔、褪印、加满燃余烬加金环与钟磬）；`ArenaExpeditionOrder` 带 `Raise`；`ArenaTrialsTuning.ProfileFor(tier, raise)` 复制后改；`ArenaExpeditionTuning` 加双参数休整 / 额外休整 / 烽烟圈 / 无间奖金 / 通关箱重载与 `RaiseRule`、`IsSuperseded`；模块经 `LiveExpeditionRaise`，通关记最高级、横幅写级数；面板左栏加高到 840、悬赏令加「加码」一节并略去被换掉的档位战规、立誓落级数小印；局内角标右上一枚级数小印。两个新 `.cs` 登记 `compile_official.bat`（CRLF 保持）。
- 守卫：`ArenaExpeditionOrderGuard` 新增 `check_raise`（梯子顺序、`RaiseRule` 按名对表、`ApplyTo` 十二级、复制语义、`LiveExpeditionRaise`、奖励与通关记录、面板接线），休整 / 烽烟圈 / 赏格断言随双参数更新；`ArenaTrialsGuard` 补厄运 8 种、新诅咒只经 `HurtNeutral` 且翻滚可躲 / 可清、不放碰撞体、血月 / 永夜 / 雷暴三处。反向验证 10 种改法（梯子换序、`RaiseRule` 错下标、删 `Clone`、焚城级不生效、悬赏令不略去、`LiveExpeditionRaise` 不认 Mode D、`CurseKinds` 改 5、沉疴不认翻滚、永夜无条件写回、雷暴不加倍）全部转红，按字节还原、SHA-256 一致后转绿。
- F3：`ARENA_EXPEDITION_TUNING` 加 `WishRaiseMismatch`（梯子 0/5/8/0、标准 +5 与困难 / 困难 +8 与噩梦机制逐项相同、共享配置未被改、急行军第 3 级与失明第 2 级、困难 +8 悬赏令 10 条、奖励、厄运 8 种都有名字）。
- Wiki / repowiki：`WikiContent/{zh,en}/mode/mode__mode_a.md` 与受跟踪的 `wiki-site/docs/{,en/}game-modes/standard.md` 逐段手改（加码一节、天象双刃、三种新诅咒、狂暴 / 召唤 / 血债），没跑整站同步；《标准 BossRush 模式》《难度级别系统》补 2026-11-27 段。
- 证据：全量守卫 800 PASS、0 新红；Dev 与正式真编译均 0 错 0 警告；正式 DLL 与游戏目录 SHA-256 同为 `C747A9EF…B1C9`，30 个 Dev 标识缺席；`ArenaHostRemainder`、`LegacyArenaEntry` 隔离回归 PASS；`vitepress build docs` 通过（未跑 sync）。未进游戏，未读写玩家存档。
- 工作区：`ArenaExpeditionTuning.cs`、`ArenaTrialsCurses.cs`、`ArenaExpeditionHud.cs` 等与别的会话未提交的改动并存，本次只动上面列出的段落；编译含那些改动。
- [L3-RAISE-01] 路牌打开出征令选标准：战帖卡下一排「不加 · Ⅰ–Ⅴ」，每枚下写战规名；点 Ⅲ，Ⅰ→Ⅲ 从左到右依次落印、只响一声、朱笔写到 Ⅲ；悬赏令出现「加码」一节列出三条、战帖大字旁落 Ⅲ 小印、赏格多一行「加码 Ⅲ：通关箱件数 +15%」；再点「不加」，印慢慢褪去。不合格：落印顺序乱、台阶与战帖卡或征程卡相叠、名字被截、悬赏令压到「等等 / 立誓」。
- [L3-RAISE-02] 选困难点 Ⅷ：最高一级燃起余烬、外圈金环、一声古钟；悬赏令列磨刀、急行军与八条加码，不再出现天象无常 / 厄运 / 天灾 / 凶相，整张不截字。不合格：被换掉的那几条仍在，或正文缩到读不清。
- [L3-RAISE-03] 选轻松、噩梦：加码一栏整体压暗，只写一句为什么不加码，点不动。
- [L3-RAISE-04] 困难加 Ⅱ 立誓：档印之后再落一枚 Ⅱ 小印；开局右侧角标右上有 Ⅱ；M 图不标首领烽烟圈；每 2–3 波变天。不合格：小印与档印同时落下，或 M 图仍有烽烟圈。
- [L3-RAISE-05] 标准加 Ⅰ 打短征通关：通关横幅带「加码 Ⅰ」；再开出征令，标准的 Ⅰ 上方有「最高」，并停在 Ⅰ。
- [L3-OMEN-01] 雷暴下把首领引进落雷：首领身上炸开冷银电弧、掉血明显多于平时。血月下打死波次首领：尸身一圈血色余烬升起，掉落多 1 件。永夜下敌人开火变慢、换天后恢复。
- [L3-CURSE-01] 回响：挨打处亮紫印、收缩约 1 秒后震一下，翻滚出圈不掉血。沉疴：挨打后身上墨絮跟随、按跳掉血，翻滚一次墨尘散开即止。破绽：挨打溅朱砂火星，同一下伤害明显变大。
- [L3-RAISE-06] Dev 构建跑 F3 `ARENA_EXPEDITION_TUNING`：PASS，`wrong` 里没有 `raise_` 开头的码。

## 2026-11-27 出征令战规复审三项（CR-2026-11-27-001～003；COMPAT；L2 + Dev / 正式真编译；L3 待 owner）

- 依据：owner「请你审核一下我们出证令做的那些改动，看看是否词条什么的都足够有趣多变并且都实装了效果也好」，随后「登记并全修」。根因见 `CODE_REVIEW_FINDINGS.md` 同日节。
- F3（001）：`DebugAndTools/F3GameplayValidationWishes.cs` 改战规条数、强制词条口径，加逐档战规开关、词缀抽取、血债补丁装上核对与清理后战规自毁；四个判定函数原样挪进新文件 `DebugAndTools/F3GameplayValidationExpeditionTrials.cs`，登记 `compile_official.bat`（CRLF 保持）。
- 血债（002）：`WavesArena/Trials/ArenaTrialsPatches.cs` 新增 `Drug.CanBeUsed`、`CharacterMainControl.UseItem` 两个前置。
- 词缀（003）：`WavesArena/Trials/ArenaTrialsAffixes.cs` 抽取提成 `RollInto`；`WavesArena/ArenaExpeditionTuning.cs` 凶煞一行改文案。
- 守卫：`tests/ArenaTrialsGuard.py` 补两个补丁目标、药品前置门控、提示前置只截「因血债不可用且含回血药」、`Roll` 只委托 `RollInto`、补齐循环在取类之后、文案不再写「四类各一」。反向验证 10 种改法（药品前置不认主玩家、药品前置放行、删掉药品补丁、提示不看可用性、提示判反、提示对任何药都截、不补齐、无视预设、补齐挪到取类前、文案改回四类各一）全部红在预期断言，按字节还原、SHA-256 一致后转绿。
- Wiki / repowiki：`WikiContent/{zh,en}/mode/mode__mode_a.md` 与受跟踪的 `wiki-site/docs/{,en/}game-modes/standard.md` 逐行手改（凶煞条数、血债不扣药、内容首领补法），没跑整站同步；《标准 BossRush 模式》补 2026-11-27 段。
- 证据：台账写完后全量守卫 800 PASS、0 新红；Dev 与正式真编译均 0 错 0 警告；正式 DLL 与游戏目录 SHA-256 同为 `CAAB5CF7…582C`，Dev 标识缺席；`ArenaHostRemainder`、`LegacyArenaEntry` 隔离回归 PASS。未进游戏。
- 工作区：`ArenaExpeditionHud.cs`、`ArenaExpeditionTuning.cs`、`ArenaInkTitleCard.cs`、`ArenaTrialsCurses.cs`、`ArenaTrialsPatches.cs` 与英文 Wiki 两份同时有别的会话未提交的改动，本次只动上面列出的行；本次两次编译都含那些改动。
- [L3-TRIALS-01] 噩梦出征令，被 Boss 打掉血、血债生效时用医疗包 / 绷带：出「血债未清，伤药不灵」，读条不开始、药不少。不合格：读条走完药被扣，或官方「无法使用」与它叠出。再用只止血不回血的药：照常能用。
- [L3-TRIALS-02] 噩梦出征令，Boss 池只开内容首领（如冠军之影）：普通波首领名牌前 3 个词缀前缀，压轴 4 个；压轴打死多掉 2 件。不合格：只有 2 个前缀，或压轴只多掉 1 件。
- [L3-TRIALS-03] Dev 构建跑 F3 `ARENA_EXPEDITION_TUNING`、`ARENA_EXPEDITION_ORDER`、`ARENA_EXPEDITION_CLEANUP`：三条 PASS，TUNING 指标含 `blood_debt_patches=installed/installed`。不合格：任一 FAIL（附失败码）。
- [L3-TRIALS-04] 路牌打开出征令选噩梦：悬赏令「凶煞」一行是「每只首领带 3 条词缀，压轴带 4 条」。

## 2026-11-26 路牌出征令不见了，只剩「哎哟~你干嘛~」（COMPAT；L2 + 正式真编译；L3 待 owner）

- 依据：owner 反馈「出征令选项现在没有在路牌上出现了，现在只有哎哟你干嘛了」。
- 根因：`9d2f73ab` 把生小鸡 `BossRushEntryInteractable` 挂成路牌子选项，它的 Awake 沿用旧写法把自身碰撞体置为 `enabled = true`（其余子选项都关掉）。该碰撞体与路牌碰撞体同点、同在 Interactable 层；官方 `CA_Interact.SearchInteractableAround` 按碰撞体距离取最近、平局取先到，常选中这个非组交互，菜单只剩它一项，出征令主交互与交互组整体不可见，长按也走生小鸡。
- 修复：`Interactables/BossRushSignOptionInteractables.cs` 生小鸡子选项改为关闭自身碰撞体，只经路牌交互组菜单出现。
- 守卫：`ArenaExpeditionOrderGuard` 新增 `[Entry]` 断言；反向验证（改回 `true` 转红，按字节还原后转绿）。
- 证据：`--changed-only` 守卫 PASS；正式真编译成功，`Build\BossRush.dll` 与游戏目录 SHA-256 同为 `D44FEE84…DEF2`。未进游戏。
- [L3-SIGN-01] 进竞技场走到路牌：主交互名是「出征令」，切换可见「哎哟~你干嘛~」；长按主交互打开出征令面板。不合格：只有「哎哟~你干嘛~」一项，或长按直接下蛋。

## 2026-11-26 外部复审第二轮四项（CR-2026-11-26-005～008；COMPAT；L2 + 正式真编译；L3 待 owner）

- 依据：对 f32a1bd4 的外部复审四条，逐条核实成立；owner 原话「全部修复」。根因与证据见 `CODE_REVIEW_FINDINGS.md` 同日第二轮节。
- 启动：宿主 Awake 的 `RegisterRuntimeModules`、Start 的 `StartIntegrationRuntime` 改为单行隔离；`BossRushStartupGuard.CompleteStart` 在 Start 末尾打印启动自检，`CompleteAwake` 只接装备抢救。
- 迟到激活：`BeginAwake` 以 `LevelManager.Instance != null` 判迟到（关卡在加载也算）。
- 装备抢救：重建完成后 `SameContext` 核对存档槽、关卡、主角、角色物品与原父物品，变了就作废；孤儿扫描改 `FindObjectsByType`（含未激活、不排序）。
- Harmony：`FindHarmonyCopies` 验文件头、前置第一、版本从高到低；`EnsureHarmonyLoaded` 逐个尝试。
- 守卫：`HostStartupIsolationGuard`（22 探针）、`EquipmentRescueEvidenceGuard`（18 探针）同步；`CustomBossArenaPresetAwakeRegistrationGuard` 的 Start 精确比较随写法更新，顺序不变式不变。
- 夹具：新增 `EquipmentRescueTiming`（登记进 `tools/run_runtime_regressions.py`，五个用例，四种生产回退都红在预期断言）；`HostDestroyOwnership` 抽取真实 Start，加两种抛错用例，三种生产回退都转红。生产文件均按字节还原、SHA-256 一致。
- 证据：正式真编译 0 错 0 警告，`Build\BossRush.dll` 与游戏目录 SHA-256 同为 `9C2236AE…4470`，Dev 标识缺席；全量守卫 800 PASS；22 个相关隔离回归 PASS；Mono 六种目录布局实测见台账。未进游戏，未读写玩家存档。
- repowiki：《运行时模块系统》《物品注册表与动态加载》2026-11-26 节就地改为现行行为（不再写「RegisterRuntimeModules 保持直调」）。

## 2026-11-26 玩家反馈两项：重进加载不出来 / Mod 装备消失；「云上的坐标」交不了（CR-2026-11-26-001～004；COMPAT / OPERATIONAL；L2 + 正式真编译；L3 待 owner）

- 依据：两条玩家反馈；owner 原话「问题1已经反复出现多次请你看看如何一次性解决？结合"鸭科夫源码"文件夹」。外部审查意见作参考：读档前就绪门控、保留原始物品树、奖金幂等可重试、修正 Harmony 前置 ID。根因与证据见 `CODE_REVIEW_FINDINGS.md` 同日节。
- 钱：新增 `BossRushMoneyTransfer`。天空岛 `TryDeliverQuest`、成就 `TryPay`、征程章节 / 引导的发钱与退款、鸭王杯 `Commit` 统一按「官方返回 true 或余额确实变了即到账」判定。
- 装备：新增 `BossRushEquipmentRescue`；`NeedsFallback` 交出空壳前记一笔。
- 启动：新增 `BossRushStartupGuard`。宿主 Awake 五段改成单行 try/catch，结尾 `CompleteAwake`，OnDestroy 调 `Shutdown`；`RegisterRuntimeModules`、`runtimeModuleHost.OnAwake`、Start 保持直调，以满足既有结构守卫。
- 构建：`compile_official.bat` 的 HARMONY_MOD_ID 改为 3589088839；三个新 `.cs` 登记进编译清单。
- 守卫：新增 `HostStartupIsolationGuard`、`EquipmentRescueEvidenceGuard`、`MoneyDeliveryNetDeltaGuard`，以及共用的 `tests/guard_mutation_util.py`（内存变异探针，要求红在预期的那条断言上，不碰工作区）。`ModeHCashBetGuard`、`CampaignGuideLifecycleGuard`、`CampaignSkeletonGuard`、`ContentCashSnapshotGuard` 的锚点随调用同步，不变式不变；在工作区变异后确认转红，再按字节还原，SHA-256 一致。
- 夹具：`SkyIslandStory` 加 `AfterAdd` 两条用例；`HostDestroyOwnership` 覆盖启动顺序和三种阶段抛错；`DynamicItemInitialization` 断言「记一笔」；`ContentTransactions`、`F3AutotestJudges`、`SkyIslandValidationJudges` 补链接；README 写明替身边界。反向验证：天空岛、成就、鸭王杯 Pay-false 三条，把生产改回旧判定后都红在预期断言上。`TryDebit` 的「Pay 返回 false 但已扣」没有夹具经过，只有守卫覆盖。
- 证据：正式真编译 0 错 0 警告，`Build\BossRush.dll` 与游戏目录 SHA-256 同为 `F4AB0362…653E`，Dev 标识缺席；全量守卫 800 PASS；20 个相关隔离回归 PASS。未进游戏，未读写玩家存档。L3 清单见 `CODE_REVIEW_FINDINGS.md` 同日节。
- repowiki：《物品注册表与动态加载》《运行时模块系统》《天空岛剧情与持久化》《开发环境搭建》各补 2026-11-26 节。

## 2026-11-25 成就页截图与水墨 UI 复核全修（CR-2026-11-25-022～038，014 复开改正；COMPAT；L2 + 正式真编译；L3 待 owner）

- 依据：owner 成就页截图「你看还有这些问题，请你再次确保UI显示都正确的哈」。先按截图几何定根因，再分三路只读复核全部水墨界面，确认项全修，C4、C8 两条核实不成立（理由见 `CODE_REVIEW_FINDINGS.md` 同日节）。
- 共享库：
  - 内容节点宽度：成就、Boss 池、图鉴三处手搭 ScrollRect 的内容 `sizeDelta` 归零（022）；新守卫 `RectTransformDefaultSizeGuard` 拦住以后再漏设（反向验证三处转红，按字节还原）。
  - 底槽与细条：共享滑条、共享滚动条槽改淡金 `Divider`（023）。
  - `BossRushInkSkin.CreateSeal`：边距归零、自动缩字下限不超过上限，英文印最低 7（035）。
  - `StampSeal` 入场动画没走完就不震（`BossRushUIEntranceAnimation.IsPlaying`，029）。
- 调用方：成就 / 图鉴 / 收藏册 / 遗种巢 / Mode H 诊断条底槽改 `Divider`（023）；Mode H 钟窗口与选手详情、伴宠经验、随机事件、Mode G HUD 细条填充改纯色，节拍器指针改 `SolidBar`（024）；孵化卡 224 见方、中心 152（025）；远征总结轴身换古铜、正面名字让出结果印（026）；天空岛 HUD 竖条挪到 x 9–14（027）；丧尸投资标题金笔左对齐 240 宽（028）；Steam 成就弹窗落印不震弹窗（029）；日志标题金笔位置取常量、英文标题笔身同步收窄、卷首朱笔按字宽让位（030）；灯箱遮罩可点暗处关闭、去掉整屏金边（031）；许愿台先排版再放墨（032）；许愿墨环套在中奖格上（033）；邸报三张深色图标垫浅纸（034）、「阅」印缩到 24 斜钤右上角并随跨期收走（036）；图鉴刻度与变异词条强调笔倍率按宽度换算（037）；Boss 池行底补 α 0.3 细边（038）。
- 台账：两本台账将超行数上限，先把已闭环旧节逐字归档——修复流水 11-05～11-06 潮闸镇八节 → `archive/FIX_TRACKER_2026-11.md`（新建），9 月四节 → `archive/FIX_TRACKER_2026-09.md`；审查台账 10 月九节 → `archive/CODE_REVIEW_FINDINGS_2026-10.md`。带 Open / UNVERIFIED 的节留在正文；「旧文件每一非空行要么在正文要么在归档」核对 0 丢失。
- 验证：
  - 守卫：全量 `python tools/run_guards.py` `PASS=797 NEW-FAIL=0 KNOWN-RED=0`（含新守卫）；回填台账后 `--changed-only` 与 `LedgerSize` 复跑。
  - 正式编译：去掉 `BOSSRUSH_DEV_BUILD` 后 `& "$PWD\compile_official.bat"` 0 错误 0 警告，81 个资源包 SHA-256 校验通过；`Build\BossRush.dll` 与游戏目录同为 `DF819996F49A18A63D176A37DE791DA799D8FB7A401BC5510ADA8F31F7AA0242`，两份 `check_dll_identifiers --expect absent` PASS。
  - 隔离执行回归：ManualSeptemberReview、AchievementRuntime、AchievementIcons、BossFilterRuntime、DailyReportHostUI、ModeGCombat、ModeHPlayerFlow、ModeHReviewFixes、PetNestCompanionActivation、RandomEventTempo、ReloadMetronome、SharedModalInput、SkyIslandHudPolicy 等 17 个 17 PASS。
  - 未进游戏，以下为 L3 待 owner。
- L3 实机清单（正式构建；不合格即记回本节）：
  - [L3-INK-16] 成就页（L 键）：卡片左侧图标完整，右侧「已授」印与「√ 已领取」完整；每张卡四边墨笔边都在，卡与卡之间不再是两道孤立金线；统计栏进度条看得见淡金底槽，填充在槽里。不合格：图标或印章被切、统计栏只有一截金色胶囊。
  - [L3-INK-17] Ctrl+F10 Boss 池：列表行左右不被切，行与行之间有一道淡淡的细边分开；勾选 / 悬停只改行底颜色。不合格：行越出面板、整片连成一块。
  - [L3-INK-18] 图鉴与收藏册：进度条有淡金底槽；图鉴里程碑刻度是细竖笔，不是压扁的墨团；图鉴「已击败」英文印读得出 SLAIN，成就 DONE、成就弹窗 RARE / FEAT 不带省略号。
  - [L3-INK-19] 遗种巢：孵化揭晓「遗种破壳」下的金笔完整露在卡片上方；远征总结卷轴上下轴身是古铜色；翻牌正面长名字不压右上角结果印；伴宠 HUD 经验细条连贯、不断成虚线。
  - [L3-INK-20] HUD 细条：鸭王杯钟窗口与选手详情血条、随机事件进度、Mode G HUD 进度条、装填节拍器指针都是连贯实线，切分辨率不闪；天空岛 HUD 卡片左侧竖条不压卡片墨边。
  - [L3-INK-21] 冒险家日志：连续切换 5 篇以上，标题下金笔始终在标题正下方、长度完整；切英文后长分类名（EQUIPMENT & POWERS）两侧朱笔不压字；点开插图后点暗处能关，屏幕四边没有一圈金线。
  - [L3-INK-22] 许愿台：进游戏后第一次打开，标题金笔紧贴标题、有墨晕；许愿揭晓时墨环套在中奖格上。
  - [L3-INK-23] Mode G 宿命回响入场页：开场动画没放完就点契约卡，卡片最终回到原位，不停在半路；Steam 式成就弹窗落印时弹窗不跳。
  - [L3-INK-24] 邸报：期号、截止、提示三处图标看得清（身后有浅纸底）；签到后「阅」印斜钤在格子右上角，不压天号。
  - [L3-INK-25] 丧尸投资页：标题下金笔从标题左缘起笔，不伸到余额下面；任一共享滚动条与滑条能看见淡金底槽。

## 2026-11-25 水墨 UI 审查全修（CR-2026-11-25-002～021 与线索、顺带项；COMPAT / SAFE；L2 + 正式真编译；L3 待 owner）

- 依据：owner「等它停下就全修」。另一会话停下后改；只动本批的行，不碰别的会话同文件里的其它改动。
- 共享库（一处改、全项目受益）：
  - `BossRushInkSkin`：新增 `CreateVerticalBrushStroke`（普通 Image + 竖笔贴图，代替转 90° 的遮罩笔，010）；`AddInkFrame` 复用主题按钮边时换回原图（004）；新增 `CenterInParent`，`StampSeal` 墨环按印章实际中心落（012），可选不出声（016）；印章、墨环、余烬粒子一律 `ignoreLayout`（006）。
  - `BossRushInkWidgets`：`AddLeftOrAlignedStroke` 先摘下再销毁旧笔（007）；`PlayInkSurfaceOpen` 不再多放一滴墨（011）；`CreateHeaderTitle` 框高不低于字号×1.45+4。
  - `BossRushInkTheme.SpawnOpenBloom`：固定中心锚点、按面板实际矩形定位（拉伸 / 非中心轴心面板不再成全屏暗斑）。
  - `IntegrationUIFeedback`：关闭淡出与复位不写根画布缩放（成就、收藏册、图鉴的根画布由 CanvasScaler 驱动）。
- 调用方：日志正文挂到 pagesGroup、右栏不再抄左栏偏移、书缝与竖分隔改竖笔、图库页脚框高 20（002 / 010）；宿命回响三处标题金笔锚顶边（008）、「契合」印 40 见方上移（021）；节拍器判定窗口纯色硬边（009）；身份色条读强调竖条实际位置排在右侧、竖向拉伸，立绘与文字随之右移（013）；孵化标题上移 16（014）；远征翻下一张前熄余烬（015）；一键领取只第一枚出落印声、收藏册只在点卡时出墨环与笔扫、邸报不再双响（016）；看图器印章挪到图上层、开着换图重画标题金笔（017）；邸报纸色在皮肤之后设、`ButtonIdle` 取实际铺的 `CinnabarFill`（018）；危险键字色按落下的底色算（019）；天象行先藏再淡入（020）；遗种巢 / 鸭王杯 / 许愿输入框选中改 `WarningText`（005，照 UI 共识）；Codex 详情、宿命回响三面板、鸭王杯恢复页删掉多放的一滴墨（011）；报头让出印宽（线索）；友好名牌不关名字底下的回退字形（线索）。
- 顺带：天空岛警示线高度常量改 5；出征令 HUD 静态缓存注释改成与 `Hide()` 一致；`Common/UI/AGENTS.md` 补「滚动列表用共享库」例外并记 `docs/ai-docs-migration.md`；repowiki《UI 系统集成》附录五补「审查全修后的用法约定」。
- 003 撤回：19 张插图都在已部署的 `production_icons` 包里（UnityPy 列包 0 缺，包与游戏目录 SHA-256 一致），正式构建按路径取图走包、不读散图，审查时只查了散图目录。`WikiImageAssetGuard` 加判据 5「游戏内插图都登记进 production_icons 清单」，防真正会漏的那种情况。
- 守卫：`WikiImageAssetGuard` 加判据 5；`DailyReportPresentationGuard` 加「纸色在皮肤之后设 / ButtonIdle 是 CinnabarFill」；`FriendlyNpcNameplateGuard` 改为断言跳过名字整棵子树。三处都做了反向验证（破坏转红，按字节还原后 SHA-256 一致）。
- 验证：
  - 守卫：全量 `python tools/run_guards.py` `PASS=796 NEW-FAIL=0 KNOWN-RED=0`；回填台账、补夹具替身后 `--changed-only` 与 `LedgerSize` 复跑通过。
  - 正式编译：去掉 `BOSSRUSH_DEV_BUILD` 后 `& "$PWD\compile_official.bat"` 0 错误 0 警告，81 个资源包 SHA-256 校验通过；`Build\BossRush.dll` 与游戏目录同为 `69E867781C2C2595170BF3DC0D16FE18A1759A23A6072B000F8CB14141AE4B9D`，`check_dll_identifiers --expect absent` 两份都 PASS（无 Dev 标识）。
  - 隔离执行回归：相关 17 个夹具 17 PASS。`ManualSeptemberReview` 首跑红：另一会话给孵化揭晓新增 `PlayInkResultAccents()`，夹具只抽 `ShowResult` 等五个方法，不认识它；在夹具 `Stubs.cs` 补惰性替身（与 `PlayRevealBurst` 同类，纯表现），复跑通过。
  - 未进游戏，以下为 L3 待 owner。
- L3 实机清单（正式构建；不合格即记回本节）：
  - [L3-INK-01] 冒险家日志任翻一篇：两栏正文都在纸面内，纸中间有书缝、左栏典籍 / 篇目之间有竖分隔。不合格：正文越出纸边或压到页脚线，纸中间没有书缝。
  - [L3-INK-02] 装填节拍器：金色判定窗两端是硬边，宽度与实际判定一致。不合格：两端发虚、看着比判定窄。
  - [L3-INK-03] 遗种巢异色 / 炫彩崽的卡片与伴宠 HUD：两条身份色条都看得见、贯通卡高，立绘和文字不压色条。不合格：只剩一色或色条只到半高。
  - [L3-INK-04] 孵化揭晓：「遗种破壳」标题与金笔完整，不被卡片挡；远征翻牌出现阵亡后，后面归巢的崽与卡背不飘红灰余烬。
  - [L3-INK-05] 许愿台连开三次：标题下始终只有一道金笔；许愿成功后「愿成」印不在面板末尾多出整宽金条，粒子不排成一列，再开面板不冒火星；抽奖指针主线可见。
  - [L3-INK-06] 成就页一键领取多个：只响一声落印，墨环落在各自印章上（不在卡片中部）；收藏册打开、切系列时无声无墨环，点卡才有。
  - [L3-INK-07] 邸报：签到只响一声；切英文后报头 "THE DUCKOV DAILY" 不压右侧「NEWS」印；纸面是暖宣纸色，签到按钮是朱砂底。
  - [L3-INK-08] 宿命回响入场页、弃局页、战报：标题金笔紧贴标题下方，不横穿说明或契约卡；选中卡的「契合」印不压说明首行右端。
  - [L3-INK-09] 任一带危险键（朱红底）的确认框：危险键文字是浅朱色。不合格：宣纸白字。
  - [L3-INK-10] 出征令变天：天象行直接淡入。不合格：先整行显示、突然消失、再淡入。
  - [L3-INK-11] 2560×1440 或 4K：反复开关成就、收藏册、图鉴，关闭时不闪、不缩错。
  - [L3-INK-12] 友好 NPC 名牌：带回退字形的名字（生僻字、日文假名等）完整显示，不缺字。
  - [L3-INK-13] 看图器：「墨藏」印在图片上层；开着时切换图片，标题金笔宽度跟着新标题变。
  - [L3-INK-14] 日志目录卡、出征令、确认框、成就页的按钮：墨笔边是粗笔触，不是细丝；遗种巢当前崽、鸭王杯选中卡、许愿输入框获得焦点时的描边明显比未选中亮。
  - [L3-INK-15] 打开宿命回响、鸭王杯恢复页、图鉴详情、看图器：只有一层墨晕，没有全屏暗斑；天空岛警示字幕下的警示线约 5 像素高。

## 2026-11-25 文案去 AI 味与机制对齐（COMPAT；不改 Key / TypeID / 存档；L2 + 正式隔离真编译；L3 待 owner）

- 依据：owner 2026-11-25 审定的文案优化方案（同条数改写，梗、彩虹字、颜文字与配色保留；机制照代码写，不许诺没有的东西；中英逐句对应）。
- 第一次提交 `9d2f73ab`：
  - 路牌主交互与长按改为「出征令」，彩蛋「哎哟~你干嘛~」降为子选项、原文不动；两类子选项原样提到 `Interactables/BossRushSignOptionInteractables.cs`。
  - Mode E / F 带阵营旗或血猎收发器却没脱光时，提示写明是哪件挡住，仍放行进标准竞技场（`NotifyBloodHuntBlocked`，D 的提示压 8 秒）。
  - 小满经 `PermanentDuckNpcRegistry.AllowsAutoSpawn` 只在 Dev 构建自动出现（含配偶恢复），F3 照常刷；JSON 与存档不改。
  - 设置标题写清作用；成就与宿命回响热键加「禁用 / None」（存 0，不再退回 L）。
  - 丧尸投资卡与保险卡按代码写实（保住的是首次领保险时背包第一件）；报错给下一步，英文货币 `$`，贝壳官方英文名 Seashells。
  - 叮当 123 行、羽织 120 行、天空岛居民 66 句改写，条数不变，「人类」清零；README 中英更新到 v2.6.2，掉落保底改为 Q5+、最大生命超过 250。
  - 新守卫 `TestNpcDevOnlySpawnGuard`、`AchievementWikiStatsGuard`、`NpcDialogueHumanWordGuard`，均做反向验证。顺带收入另一会话已改好的成就 Wiki 统计行（46 个、奖金合计 20,640,000），新守卫依赖它。
- 本次补完（当时文件在另一会话的未提交改动里，只改本批的行、只暂存本批的行）：
  - `WavesArenaRuntimeModule_BossSpawning.cs` 第三处 Boss 池为空的提示改指「Boss池设置」；`ArenaExpeditionTuning.cs` 悬赏令「不计通关成就」改为「不计通关/无伤/速通成就」（与 `ExpeditionCountsForClearAchievements` 一致）。
  - 新守卫 `BossPoolHotkeyHintGuard`：带 Ctrl+F10 的提示必须写明「Boss池设置」/ Boss Pool Settings，并核对窗口标题；三处破坏转红、只改注释不误报，文件按字节还原。
  - Wiki 中英：装备 / 武器 / 模式 / 系统页的模板小标题「这是啥？/ 这是什么 / What Is It?」与「怎么进？」改为武器定位 / 装备定位 / 玩法机制 / 入场条件 / 概述（Role / At a Glance / How It Plays / Overview）；六处口水句改写，婚后送戒指改按代码写递增扣分；新手路线、首局指引、模式总览、无间炼狱入场改为「出征令选阵容」；地图数补潮闸镇与末班编组站；成就大全 19 条描述按出征令后的计数口径；配置总览补禁用热键、Q5+ 保底条件、倍率影响反应与伤害上限、烟花巡游不占次数；后山页引用的设置旧名、逆鳞开场句同步。生成页按同一行手改，没跑整站同步。
- 验证：快照（HEAD + 本批）单独正式编译 0 错误；全量守卫与 HEAD 基线比无新增红项；相关执行回归通过；`npx vitepress build docs` 通过。游戏目录 DLL 未动。
- L3 待 owner：①竞技场路牌提示为「出征令」、长按开面板，子选项仍能生小鸡；②阵营旗 + 背心进竞技场有具体提示且能进；③ModConfig 成就热键选「禁用」后按 L 不开面板；④正式构建基地里没有小满；⑤叮当、羽织、岛民气泡与丧尸卡面无方块字、不溢出；⑥出征令选短征时悬赏令那行写「不计通关/无伤/速通成就」且不溢出框。

## 2026-11-25 出征令困难 / 噩梦战规机制化：天象、诅咒、天灾、首领词缀、焚城（COMPAT，不改存档；L2 + Dev 隔离真编译；L3 待 owner）

- 来由：交接单「出征令困难 / 噩梦战规机制化」分四批（① 天象 + 诅咒，② 天灾 + 临时地形，③ 首领词缀 + 额外掉落，④ 焚城）；owner 追加「所有特效交互逻辑都要高级高端大气史诗感，不要塑料廉价感」。
- 各档战规（悬赏令与 Wiki 标准模式页同口径）：
  - 轻松 / 标准：只换天色（每 3–4 波），不改数值。
  - 困难：磨刀、凶相（1 条词缀，压轴 2 条）、厄运（轻）、急行军、天象无常（每 4–5 波变天）、天灾（30–45 秒一阵）。
  - 噩梦：杀心、凶煞（3 条，压轴 4 条）、失明、断补、血债、厄运（重）、急行军、焚城、天翻地覆（每 2–3 波变天）、天灾连环（15–25 秒一阵，25% 叠两种）。
- 结构：新目录 `WavesArena/Trials/`（13 个 .cs，全部登记 `compile_official.bat`）。局内服务对象 `ArenaTrials` 的唯一 owner 是 `WavesArenaRuntimeModule_Expedition`：开局 Begin、收局 End，开波 / 收波 / 首领登记经 `ArenaTrials.Notify*` 转入；不是出征令的局不建对象，每帧零成本。各部分按 Sky → Curses → Hazards → Affixes → FireRing 的固定顺序驱动；伤害一律经 `ArenaTrials.HurtNeutral`（不吃难度倍率、不看护甲），属性修饰记在 `ArenaTrials` 的记录表里，Shutdown 逐条摘。没有新增 ModBehaviour partial，也没有新增 `ModBehaviour.Instance` 引用。
- 取舍与理由：
  - 天象全部自绘，不调官方 `WeatherManager`：官方雨天会改元素系数，等于暗改数值。
  - 天灾做了九种（交接单写的是八种），九种都能被抽到；暴雨减半毒雾 / 熔缝。首领只吃天灾三成伤害，免得天灾替玩家打 Boss。
  - 词缀身体是首领预设的小号复制（亲卫 / 影从体型 0.6、生命为首领上限的 12%、伤害 ×0.5、主将倒下随之散去）；内容 Boss 没有可复制的预设，只挂身法 / 死相两类。
  - 双生一对最多拉起 2 次，否则两只轮流倒下永远打不完；分身 / 双生压收波上限 150 秒，超时散掉剩下的身体照常收波（兜底防卡波）。
  - 亡魂、殉爆不随收波消失，各自走完时长才收（下一波开打也不打断），收局 Shutdown 时一并收掉；伤害都能翻滚躲开。
  - 额外掉落：挂 1–2 条词缀的首领本体多掉 1 件，挂 3–4 条的多掉 2 件，从它自己的掉落池出；身体一律 0、死了不掉箱。
  - 焚城只烧玩家（不让首领被风筝到圈外烧死）；圈心是刷新点重心贴 NavMesh，落不下改用最近刷新点；半径取最远刷新点（开波时玩家更远就按玩家）+10 米，下限 24 米；开打 30 秒起用 14 秒收到 60%（压轴 40%），收紧前 6 秒地上与 M 图先画落点圈；圈外每 0.5 秒结算一跳，合每秒 4% 生命上限的火伤，翻滚躲不掉；收波 4 秒退去。
- 表现（按「史诗、不塑料」做）：天象横扫是墨书天象名加一方朱印、一声低钟；焚城是三笔地面火线（焦痕、余烬床、炽缝），火舌、浓烟、火星只在玩家看得见的那段弧上发，配三盏随噪声明灭的暖光；燃起从离玩家最近的点向两头烧合拢；收紧时战鼓、合拢时盖印；屏幕热浪画在 `ArenaExpeditionHud` 色调层，跟官方 HUD 显隐与暂停双门。M 图用两枚官方区域 POI（火圈、落点）。
- 第四批完成后请独立审查（只读代码），报 6 个问题和 1 个推测，全部修了：
  1. 词缀身体的血量基准与狂暴体型取得太早：首领入场通知在变异词条挂上之前（`LegacySpawn` 先乘战帖并通知、后 `MutatorManager.ApplyToEnemy`）。现在身体血量在落地登记时按首领当时的生命上限折算（`MasterMaxHealth` 现读，首领倒下时再读一次，读不到用上次的值）；狂暴以进入狂暴那一刻的体型为底长大，收尾也还原到它。巨灵兵首领狂暴不会再先缩回 1.0，群鼠首领长到 0.75×1.3。
  2. 燃起最后一帧没补画整圈：帧率低于约 40 时火墙停在开口的弧上，一直到 30 秒收紧才补上。现在燃起结束的那一帧也重画（`wasIgniting`）。
  3. 双生拉起途中幸存者倒下，先演了「封住」，被拉起的那只却照样落地、压着收波。现在这一对封住后，在路上的那只落地即散（`pairSealed`）。
  4. `OnBossSpawnFailed` 两处推波和批量刷怪最终校验绕过了压收波，同波另一只首领刷不出来时会把先倒下那只的分身一起收掉。三处都补了 `ExpeditionHoldsWaveEnd(null)`。
  5. 燃起火头按帧发粒子：144 帧时占满火舌粒子上限，30 帧时只有一半。改按秒计量（每个火头每秒 54 条火舌、22 颗火星）。
  6. 退场把关着的灯以旧亮度重新点亮，在原位置闪一下。现在关灯前先把亮度清零。
  7. （推测）身体的生命修饰记录只在收局时摘，无间长局会越攒越多。现在身体移出列表时一并丢掉它那条记录（`ForgetBodyAt`）。
  - 另附 HUD 一处：HUD 不在时焚城的第一次收紧连鼓声都没有。`PlayOmenSweep` 改为返回是否真的演出来，演不出来就只擂鼓、下回再写横扫。
- 守卫：新增 `tests/ArenaTrialsGuard.py`，覆盖 owner 与生命周期、补丁早返、各档开关、天象、诅咒、天灾、词缀、焚城与编译登记 11 组不变式；夹具 `ArenaHostRemainder/Stubs.cs` 跟着 `ApplyExpeditionTierToSpawnedBoss` 签名补。反向验证 112 条，全部实跑转红、按字节还原并核对 SHA-256：① 20、② 21、③ 28、④ 26、审查修复 17。
- 验证（最后一轮，审查修复之后）：
  - `python tests/ArenaTrialsGuard.py` PASS；`python tools/run_guards.py --changed-only` PASS=475 NEW-FAIL=0 KNOWN-RED=0。
  - Dev 真编译在隔离签出（HEAD 9d2f73ab + 本条文件，`GAME_PATH` 指 Managed 拷贝）上 0 错误。
  - 提交 dab03116 之后在主工作区正式构建并部署：0 错误，81 个资源包 SHA-256 校验通过；游戏目录 `BossRush.dll` SHA-256 `4139F1D5796FCBEEE2FA12AFDE37E89A129865B989E80352F87C212DC29596B7` 与 `Build/` 一致，`check_dll_identifiers --expect absent` PASS。主工作区里另一个会话未提交的 UI 水墨升级与 Wiki 插图改动也编进了这个包。
  - 本条涉及的文件：`WavesArena/Trials/`（13 个新文件）、`ArenaExpeditionHud.cs`、`ArenaExpeditionTuning.cs`、`ArenaInkTitleCard.cs`（只加 `Playing`，变天横扫等标题幕收完再出）、`WavesArenaRuntimeModule_` 的 BossLootEvent / BossSpawning / Expedition / LegacySpawn / Tick / WaveDeaths、`compile_official.bat` 的 13 行 Trials、`tests/ArenaTrialsGuard.py`、夹具 `ArenaHostRemainder/Stubs.cs`、Wiki 四页、repowiki 一篇。
  - 执行回归 15/15 PASS：ArenaHostRemainder、AuditModeLifecycle、LegacyArenaEntry、ModuleOwnerCleanup、HostMapArenaOwners、WavesArenaPresetWeight、BossRewardDelivery、ModeDEntryOwnership、ModeRuntimeDispatch、ModeDestroyLifecycle、EnemyRecoveryRuntime、AchievementRuntime、AudioRuntime、EclipseBloomRuntime、EntryAndReforgeCompatibility。
  - 文档：Wiki 中英「标准 BossRush 模式」新增「战规细则」节、各档条目改写，「变异词条」页改成只有轻松借妙手回春；`npm --prefix wiki-site run build` 通过（会重写受跟踪的生成页）。repowiki《标准 BossRush 模式》加 2026-11-25 一段和 6 个文件引用。
- 遗留：
  - repowiki《难度级别系统》第 31 行的出征令描述已过时，但这份文件已被另一个会话暂存，本条没有动。
  - 局内表现、手感与数值都没有实机采样，性能也没测。
- L3 待 owner（游戏目录已是上面那个正式包；正式包里没有 DevLog，全程看屏幕即可，不需要截图）：
  - T1 天象：标准档打 8 波，每 3–4 波天色换一次，数值不变。不合格：天色不换，或伤害 / 移速有变化。
  - T2 变天横扫：困难档变天时，屏幕上方出现墨色横扫（天象名 + 朱印 + 低钟），屏幕右侧中线偏上、战帖印下方的「天象 · 名」一行随之更新。不合格：横扫和标题卡叠在一起，或那一行没换。
  - T3 雾天：困难档遇到雾，雾里的敌人在玩家视野里被遮住，散雾后恢复。不合格：雾里的敌人照常清楚可见，或雾散后 AI 仍然半瞎。
  - T4 推力类天灾：站着会被推动，翻滚中不被推，收波当帧停止推。不合格：翻滚中被推，或收波后还在推。
  - T5 词缀名缀：困难档首领血条名字前面有彩色词缀名（如「分裂 」），不截断、不显示 `*BossRush_*`。不合格：缺字、出现省略号或键名。
  - T6 分裂 / 双生压波：打倒带分裂的首领后出现两只分身，分身没打完不进下一波。不合格：分身还在就收波，或分身全倒后 10 秒内还不收波。
  - T7 双生拉起：只打倒一只，10 秒内地上倒计时符阵转满，那只半血站起来；同一对最多站起 2 次；拉起途中打倒另一只，演「封住」且不再有身体站起来。不合格：站起第 3 次，或封住后仍有双生落地。
  - T8 护驾：三名亲卫在时首领明显难打（减伤 80%），打掉最后一名时首领身上的护罩碎裂。不合格：亲卫全倒后仍然减伤。
  - T9 闪现：首领蓄势约 0.5 秒后落到玩家身后约 2.6 米，落点在地面上，不卡墙、不悬空。不合格：落进墙里、掉下地面或闪到别的楼层。
  - T10 狂暴：首领半血时 0.6 秒内平滑长大到 1.3 倍、移速加快。另开一局带巨灵兵（以及一局带群鼠），以开局体型为底长大。不合格：先缩小再长大，或长完小于开局体型。
  - T11 亡魂：首领倒下后亡魂追玩家约 15 秒，翻滚能躲开碰撞伤害。不合格：亡魂穿墙瞬移，或翻滚中仍然掉血。
  - T12 殉爆：倒地 3 秒后爆炸，地上的预警圈与实际伤害范围（6 米）一致。不合格：站在圈外挨炸，或站在圈内没事。
  - T13 额外掉落：挂 1–2 条词缀的首领多掉 1 件、3–4 条的多掉 2 件；分身、双生、亲卫、影从死了都不掉箱。不合格：身体掉箱，或件数不对。
  - T14 焚城燃起：噩梦开波时火线从离玩家最近的点烧向两头，约 3 秒合拢成整圈，不留缺口。不合格：留缺口，或从远端起火。
  - T15 预告与收紧：开打 24 秒时地上与 M 图出现金色落点圈；30 秒时战鼓响，首次收紧（与压轴）有「焚城」横扫，14 秒收到落点圈，合拢时盖印。不合格：M 图没圈，横扫与标题卡重叠，或第一次收紧时没有鼓声。
  - T16 圈外灼烧：站到圈外，每 0.5 秒掉一跳火伤，屏幕四周出现橙红热浪晕，回到圈内后逐渐消退；敌人站在圈外不掉血。不合格：圈内也掉血，翻滚能躲开灼烧，或敌人被烧。
  - T17 退去与清理：收波后火墙 4 秒内退去、热浪归零，M 图上两枚圈都消失；切图或中途退出不残留火线和灯光。不合格：残留任何火线、光斑或 M 图圈。
  - T18 性能：噩梦第 10 波以后（天灾 + 焚城 + 4 条词缀同屏）帧率与不开出征令时对比。本条未采样，没有数据。

## 2026-11-25 全项目 UI 纯水墨升级：彻底消弭官方预制件与现代细条混搭（COMPAT；L2 + 正式真编译；L3 待 owner）

- owner 原话：「全部都做了！不要混搭，我也看到了混搭的效果并不是很好看。并且都以最高水平做了」。
- 共享底座扩展（`Common/UI/BossRushInkWidgets.cs` 新文件已登记 `compile_official.bat`；同步扩充 `Common/UI/BossRushInkSkin.cs` 与 `Common/UI/BossRushUI.cs`、`Integration/UI/IntegrationUIFeedback.cs`）：
  - 新增 `GetVerticalBrushSprite()` 纵向尖锋毛笔 Sprite 并在 `ResetStaticCaches()` 销毁释放。
  - 新增共享组件 `CreateSealBadge`、`CreateHeaderTitle`、`SetSegmentUnderline`、`PlayActionRipple`、`PlayInkSurfaceOpen`、`StyleInkBarPair`、`CreateScrollShell`、`PlayScrollUnroll`、`BossRushInkEmberField`，统一印章落印、金笔底划、展卷、水墨渐强栏与火星粒子。
- 彻底清除独立界面中的 `GameplayDataSettings.UIPrefabs.Button`/`ScrollRect`、`TitleRail`/`TopTrace`/`AccentRail`/`AccentLine` 与几何 `Hairline` 混搭（原生贴合系统 `ReforgeUIManager*`、`AffinityTabView`、`FriendlyNpcNameplates`、`LootView`、`ModeEMerchantSellAllUI` 保持原版融入；独立弹窗 `OriginalConfirmDialogueAdapter` 改为全水墨自绘）：
  - 系统界面：`AchievementView`、`BossFilterUi`、`CodexView`/`CodexView_Grid`（已诛/未逢印章 + 详情金笔）、`CollectionBookView`、`AchievementEntryUI`（已授印章）、`SteamAchievementPopup`（达成/绝响印章 + 墨环/火星）、`ImageViewerUI`（金轴边框 + 墨藏印章 + 墨晕开屏）、`OriginalConfirmDialogueAdapter`、`MutatorUI`。
  - 许愿台与遗种巢：`WishFountainUI`/`WishFountainUI_Feel`（去顶条、去状态竖条、愿成金印 + 火星粒子）、`WishFountainRewardAnimationView`/`_Reveal`（品质笔触条、得赐金印 + 墨环 + 爆发微粒）、`PetNestHatchRevealView`（异色/炫彩/初鸣印章 + 墨环 + 粒子、斜削毛笔侧条）、`PetNestExpeditionRevealView`（展卷摘要 `InkScrollShell` + 铭记/凯旋/陨落/负伤/归巢落印 + 灰烬微粒）、`PetNestUILayout` / `PetNestCompanionHudView`。
  - 擂台模式：`ModeGInteractable` / `ModeGRecapPanel` / `ModeGHUD`（契合/凯旋/饮恨印章、噩梦卡火星、进度条毛笔化）；`ModeHUI` / `ModeHUIPages`（移除独立 `UIPrefabs.ScrollRect` 实例化并加 `#pragma warning disable 0649` 守住已停用风险横幅兼容字段）/ `ModeHUIPageParts` / `ModeHUIFighterDetails` / `ModeHRecoveryPanel`（钟罩敲响墨环、胜出/败绩/待决印章、水墨条）。
  - 《鸭科夫日报》改版古宣水墨邸报：`DailyReportUI.cs` / `DailyReportUI_Dashboard.cs` 弃用浅色烤纸底图 `daily_report_bg.png`，改用近黑崩边墨底 + `GetFiberSprite()` 宣纸纤维 + 木金轴杆 + 鎏金/暗朱毛笔分栏条 + 「邸报」/「阅」朱印，并保留 `DailyReportLayoutTable` 与 `dr_icon_*`/`dr_mascot.png`；`DailyReportPresentationGuard.py` 同步改用线性色彩对比度与古宣构件检查。
  - 天空岛与战术/独立 HUD：`SkyIslandHud`（水墨主题下切换纵向毛笔指示条 + 横幅金环）、`ModeFStatusHud`、`ZombieModeHudController`/`ZombieModeUiWidgets`/各确认与选卡视图、`CampaignHud`、`RandomEventHud`、`ReloadMetronomeHud`。
- 守卫与文档同步：
  - `AwenOriginalConfirmDialogueGuard.py`、`UIConsensusSystemPanelsGuard.py`、`DailyReportPresentationGuard.py` 同步并在盘上用破坏探针反向验证（全部实跑 `rc=1` 转红、恢复后 SHA-256 逐字节一致 `cdd23ad3f781` / `5d489fb3d7b2`）。
  - 更新 `docs/architecture/UI制作共识.md` §12、`.qoder/repowiki/zh/content/用户界面与本地化/UI 系统集成.md` 附录五、`.qoder/repowiki/knowledge/zh/BossRushMod 鸭科夫日报：自算游戏日、签到梯度与报箱建筑/...md`（`RepowikiReferenceGuard` 309 篇 / 994 目标 PASS）。
- 验证：
  - 正式编译：`& "$PWD\compile_official.bat"` 0 错误 0 警告，81 个资源 bundle 校验通过；`Build\BossRush.dll` 与游戏目录 `D:\software\steam\steamapps\common\Escape from Duckov\Duckov_Data\Mods\BossRush\BossRush.dll` SHA-256 一致为 `428DF3655674826E2425FC92E68D06A785371AC60233CF1D31F01FE7BF5B5B5C`。
  - 守卫：`python tools/run_guards.py --changed-only` 跑通全部 468 个脚本（`PASS=468 NEW-FAIL=0 KNOWN-RED=0`）；另有 25 项核心 UI/编译/契约守卫 + `StaticCacheLifecycleGuard.py` 逐项 PASS。
  - 隔离执行回归：`python tools/run_runtime_regressions.py` 所选 17 项 UI/宿主/模式夹具全部通过（`17 PASS / 0 FAIL`）。
- L3 实机核对清单：见本次交付说明中的 `[L3-UI-*]` 分项核对点、位置与不合格判据。

## 2026-11-25 owner 实机反馈：名牌照原版、出征令不开变异词条、噩梦加码、全项目统一水墨 UI（COMPAT；L2 + Dev 真编译；L3 待 owner）

- owner 原话：「名牌的样式和原版 NPC 的不一致」「打出征令时不开变异词条吧，以免乱七八糟」「噩梦加的倍率不是很多啊，你可以大胆加加」「我们项目已有的 UI，你觉得可以改的都改成出征令这种 UI……统一一下我们项目的风格……确保是最优方案并且符合人体直觉，高级高端大气感，极简化。但是已有的功能都要有」。
- 名牌：`FriendlyNpcNameplates.CreatePlate` 克隆官方 `HealthBarManager.healthBarPrefab`，只留它的 `nameText`（同字体、字号、描边、颜色），其余图形都拆掉；画布缩放经新增的共享 `BossRushUIKit.MatchCanvasScaler` 照抄官方 HUD 画布。F3 用例 `NPC_NAMEPLATE_SETTING` 加 `official_style` 判据。
- 出征令不开变异词条：`TryRollMutatorsForArena(…, ExpeditionForcedMutators(expeditionOrder), expeditionOrder == null)` 一路透传 `allowRandom` 到 `MutatorModeFlow`，出征令开的局不抽随机词条。战帖借用的战规（共享词条）照常强制加上，但不进变异词条栏；只有战规时词条栏不出现（`MutatorUI.ShowBanner`）。
- 噩梦加码（`ArenaExpeditionTuning`）：首领生命 ×1.6 → ×2.5、伤害 ×1.3 → ×1.75、反应 ÷1.3 → ÷1.5、无间奖金 ×1.5 → ×2；轻松、标准、困难不变。理由：困难与噩梦之间的倍率台阶原来只有 0.3 / 0.15，叠上 8 条战规仍不够「噩梦」；奖金同步抬，不冲经济（只作用于无间奖金池，通关箱规则不变）。Wiki 中英标准模式页、变异词条页同步。
- 全项目统一水墨 UI（新文件 `Common/UI/BossRushInkTheme.cs`，已登记编译清单）。只接管共享库入口，调用方不改，已有功能全在：
  - 底图：`GetInjected` 的面板 / 卡片 / 按钮三档换成 `BossRushInkSkin` 的崩边墨块（按档换 pixelsPerUnit 的 Sprite 壳，不复制纹理），分隔线档换成淡金横笔；滚动滑块仍取图集。
  - 描边：`ApplyPanelStroke` 先走 `TryApplyFrame` 画墨笔边，面板档另铺宣纸纤维。
  - 按钮：不挂投影与斜面（`ApplyDepth` 早返）；`TryApplyButtonFrame` 按角色挂墨笔边——主操作金边；危险的 `Danger` 实色底在 `ApplyButtonColors` 里换成墨底 `DangerFill`，朱边、浅朱字（`GetButtonTextColor` 认出），与主操作的朱砂底分得开。
  - `CreateModalSurface` 不画强调竖条与顶边细条；`CreateCard` 的强调竖条往里收。
  - 打开动效：0.42 秒从 0.97 缓出、0.3 秒淡入，240×160 以上的面板背后墨晕洇开再散并自毁；`WriteRevealStrokes` 把面板里的墨笔标题金笔写出（`PlayBrushWrite` 记下整笔宽度，重播不越写越短）。
  - `BossRushUIColors` 换成与 `BossRushInkColors` 同色的水墨 token；宣纸字提亮到 #F1E9D6，白字底判定上限 0.143。日报面板浅纸按钮压暗一档以保 4.5:1。
- 旗舰界面：
  - 遗种巢：标题换成 `CreateBrushTitle`（「遗种巢」+ PET NEST）；四个页签居中改成一行字（常态透明、悬停字后洇淡金，三态色经 `ApplyButtonColors`），选中为宣纸粗字；指示条换成一笔鎏金，切页时边滑边从左重写（`TickTabStroke`），配翻纸声；页签行下托一道淡金横笔。所有页、按钮、键位（Q / E、ESC、说明页）不变。
  - 共享确认框、遗种巢改名框：`BossRushInkTheme.AddTitleStroke` 按 TMP 字形边界在标题下配一笔金，随打开动效写出；丧尸模式各面板标题下本来就有分隔线（现为淡金横笔），不加。
- 守卫（§4.10 同步，反向验证均做）：
  - 新增 `BossRushInkThemeGuard`：锁 10 类接管点、动效时长与起始缩放、墨晕自毁、缓存清理链、色板同色；16 个内存反向检查，另在磁盘上把 `Active` 改成 false 实跑转红、按字节还原。
  - `SkyIslandUiContrastGuard`：墨笔边覆盖率按生产贴图公式的 Python 移植复算（拉伸边中位覆盖率取四边最小，0.83），新增危险角色（DangerText on DangerFill 常态 / 悬停 ≥ 4.5）一节，30 个反向检查。
  - `UILayoutReadabilityGuard`：遗种巢标题按 `CreateBrushTitle` 固定版式复算不越过面板上边、英文小字不压页签行；页签底色必须经共享 ColorBlock 入口；17 个反向检查。
  - `BossRushUISkinLoaderGuard`、`FriendlyNpcNameplateGuard`、`ArenaExpeditionOrderGuard`、`MutatorSemanticsGuard`、`WavesArenaStateOwnershipGuard` 跟着结构改；磁盘反向探针 7 处（allowRandom、噩梦倍率、词条栏过滤、宿主透传两处、名牌缩放、遗种巢标题高度）全部转红，sha 校验还原。
  - 夹具：`AuditModeLifecycle/WaveOrdering.cs`、`IntegrationLeafOwners/Program.cs` 跟着 allowRandom 签名补。
- 验证：
  - 全量守卫：791/791 PASS（含新增 `BossRushInkThemeGuard`）。
  - Dev 真编译 0 错误并部署，游戏目录 `BossRush.dll` SHA-256 `49FD8CCA2FF0D21C744D027DFB652B2A716ABA914B7CE9B640EFC5D5C07A01F6` 与 `Build/` 一致；`check_dll_identifiers --expect present` 30/30。
  - 执行回归 13/13 PASS：SharedModalInput、IntegrationLeafOwners、AuditModeLifecycle、LegacyArenaEntry、WavesArenaPresetWeight、ArenaHostRemainder、F3ValidationExecution、SkyIslandInteraction、DailyReportHostUI、PetNestCare、PetNestGrowth、PetNestCompanionActivation、HostMapArenaOwners。
  - Wiki 站点构建：`npm --prefix wiki-site run build` 通过。
  - 文档：`Common/UI/AGENTS.md` 水墨主题一条改写、`docs/architecture/UI制作共识.md` §12 加「全局接管」、`docs/ai-docs-migration.md` 记一笔；repowiki《UI 系统集成》附录五、《遗种巢系统》同步。
- 同一时段另一个会话在改冒险家日志（`Integration/WikiUIManager*.cs`、`WikiContentManager.cs` 与几条守卫），不属于本条，本条未动这些文件。
- L3 待 owner（游戏目录现在是 Dev 包；交付前换正式构建并跑 `--expect absent`）：
  1. 名牌：走近小明与叮当 / 羽织，对比头顶名字的字体、字号、描边、颜色；不合格 = 任何一项肉眼可分。
  2. 出征令（ModConfig 变异词条保持打开）：四档各开一局，屏幕左上与开波横幅都不出现变异词条；困难 / 噩梦的战规照常生效。不合格 = 出现任何变异词条横幅或词条栏。
  3. 噩梦：同一只 Boss 在困难与噩梦下的血条长度与挨打掉血明显不同（约 ×1.9 / ×1.5）。
  4. 水墨 UI（遗种巢、确认框、改名框、图鉴、成就、日报、Mode H、丧尸模式面板、天空岛剧情面板）：面板是墨块 + 墨笔边，没有圆角高光与投影；主操作朱底金边、危险墨底朱边朱字；打开时背后一滴墨洇开再散、没有残留墨斑；所有按钮、页签、滚动、ESC 照常。不合格 = 文字看不清、墨斑留在屏幕上、按钮点不动或功能缺失。
  5. 遗种巢页签：Q / E 与鼠标切页时金笔边滑边重写、配翻纸声；「说明」页里金笔藏起、返回后重写。不合格 = 金笔停在旧页签下或宽度异常。
  6. 天空岛：剧情面板选项行在亮插图上仍看得出能点；右上 HUD 卡片可读。

## 2026-11-25 叮当重铸强度下调（COMPAT；旧装备读档压到新上限经 owner 同意；L2 + 正式真编译；L3 待验）

- 起因：玩家反馈重铸太离谱。旧实现每次在当前值上累加，结果只夹在预制体值 30%~200%；倾向拉到 90% 只多收价值的一半，离线模拟 5 万价值、5 条属性的装备中位 8 次（约 20 万）全属性 ≥1.9 倍。
- 改动（`ReforgeSystem.cs`）：
  - 每次以预制体原值为基准重抽，不再累加；单次幅度上限与最终上下限统一为预制体值 ±50%（`MAX_VALUE_OFFSET_PERCENT` 1.0 → 0.5）。下限因此从 30% 变为 50%。
  - 预制体为 0 的属性 ±0.5 → ±0.25；护甲 ±1 不变。去掉「|值|≤1 用 0~1 绝对幅度」分支，小数值属性同样按 ±50%。
  - 整数判定改为 |值| ≥ 2 且为整数；整数属性的上下限取到整数，Max/Min 标记与实际可抽值一致。1.0 倍率不再被取整成 0/2。
  - 新增 `ClampToReforgeBounds`。倾向滑块、费用、投入曲线、冷淬液 / 回火液均不变。
- 旧装备（owner 同意「读档恢复时压到新上限」）：`TryRestoreModifier/Stat/Variable` 把预制体值 + RF_ 差值夹进新上下限后生效；存档里的差值不改写。寄存 / 快递前的 `SyncCurrentReforgeState` 改走 `SyncTrackedReforgeValue`（在 `ReforgeDataPersistenceCleanup.cs`）：旧差值夹取后等于当前值时不写回，回退旧版本仍读回原值；玩家重新重铸的属性照常写新差值。
- 守卫：`EntryAndReforgeCompatibilityGuard` 新增重抽基准、三处读档夹取、存档同步不改写旧差值；3 处破坏均转红，在 scratchpad 副本里做，生产文件未动。
- 回归：`EntryAndReforgeCompatibility` 新增 8 组边界、3 条旧值夹取、12 组（三类属性 × 4 种预制体值）连续 30 次重铸不累加；scratchpad 副本里改回累加、改回 ±100%、改回 ±0.5、去掉夹取 4 处均转红。`DragonBreathZombieRuntime` 用例的 RF 差值从 +15 改为 +10（旧值 38 超出预制体 23 的新上限 34），桩补 `ClampToReforgeBounds`；其负向探针全部按预期转红。`NpcAuditFixes`、`ReforgeAssetTransactions`、`AffixSelectionUI`、`AffixCombat`、`DynamicItemInitialization`、`ManualEquipmentRecovery`、`ZombieModeRewardRuntime` 通过。
- 编译与守卫：正式构建通过并部署，游戏目录 `BossRush.dll` 的 SHA-256 与 `Build/` 一致（`2c7f4c91…`）。全量守卫 790 条里唯一新红是 `LargeFileBudgetGuard`（`ReforgeDataPersistence.cs` 1207 > 1200），已把新增的 `SyncTrackedReforgeValue` 放进同一 partial 的 `ReforgeDataPersistenceCleanup.cs`，回到 1185 行。
- 文档：Wiki 中英「重铸系统 · 属性变化范围」、repowiki「重铸系统」已更新。中英更新日志随下一次发版写（当前 `info.ini` 仍为 2.6.2）。
- 实机清单见 `CODE_REVIEW_FINDINGS.md` CR-2026-11-25-001。

## 2026-11-25 owner 实机反馈五项：许愿台两行字、名牌去配置、止语钟罩金钟罩、出征令悬赏令 + 战规多元化、专项 F3 日志（COMPAT；L2 + Dev 真编译；L3 待 owner）

- F3 专项报告 `BossRushValidation_20261125_004916_070.log`（这五项改动之前的 Dev 包）：pass=26、fail=0、skip=1。
  - 上一轮的两条红项都已转绿：BOSS_MAP_BEACON（beacons=1，偏移 14.8 米，没有贴在 Boss 身上）、ZOMBIE_CROWD_NAVIGATION（关掉初始流派面板后 20.6 秒内扫到 12 只丧尸，没有叠罗汉）。MODCONFIG_OPTION_LABELS 在中→英→中三轮里，12 个下拉标题都没有错。
  - SKIP：GOBLIN_POST_DIALOGUE_STAY，叮当当时在婚礼教堂，前置条件不满足，同上一轮。
  - 13 条 EXTERNAL_ERRORS 都不是本 Mod 的：鸭鸭市场 `DuckMarketView_NoteClone.OnLanguageChanged` 抛 NRE ×12，被配置标题用例的两次切语言触发，以前各轮都登记过；`No prefab for building casino_building` ×1，外部资源缺失，同 2026-09 的记录。
  - 回基地那一帧峰值 323 ms，算切图加载；最终 p95 为 33.93 ms，泄漏增量全部为 0。
  - SUMMARY 记 INCOMPLETE，原因是 17 条人工项没跑，属于预期。
- 许愿台：去掉标题下面那行说明（`WishFountainUI.hintText`），也去掉星宿带里星泉那行字（`springText` / `RefreshSpringText`）。流星落点改读 `springPoint`。
- 友好 NPC 名牌：去掉 ModConfig 三档，固定为走近显示（8 米亮、10 米灭）。名牌里只有名字，没有墨笔横条。
  - 配置键只在 2026-11-24 那批未提交的改动里出现过，从没发布，所以不算 SCHEMA-。
  - `FriendlyNpcNameplateGuard`、F3 用例 `NPC_NAMEPLATE_SETTING`、覆盖清单与 Wiki 已同步。
- 止语钟罩：戴着时主角身上常驻一口鎏金钟罩（`SilenceBellFx`），只在出击时随驱动生灭。
  - 钟身高度等于护罩余量：挨打变矮，回充时从钟口一圈圈铸回去，满罩时有流光上升。打碎后只剩钟口一圈余烬。
  - 只用共享 `BossRushFxKit` 材质，没有新着色器。钟跟着人走，不随 HUD 隐藏，暂停时定格；头顶条仍过 HUD 显隐和暂停两道门。
- 出征令：
  - 原面板下方的两行说明和「收起 / 立誓」按钮去掉。右边新增一张悬赏令，从上轴卷开：
    - 标题是档位名，下面依次是征程一行、「战规」、「赏格」。
    - 按钮「等等 / 立誓」都在令上，ESC 等于「等等」。
    - 换一个选择，整张令重新落墨。噩梦档纸色发焦、底边燃余烬。立誓时朱印盖在令上。
  - 战帖卡下面加一排小印，表示这档有几条战规。
  - 难度梯度改成一叠战规，参考杀戮尖塔的进阶，分四类：首领、己身、战场、天时。
    - 轻松：3 条宽限。
    - 标准：不加。
    - 困难：5 条。
    - 噩梦：8 条，含急行军（休整 ×0.5、取消每 5 波额外休整）、盲战（首领不进烽烟圈）。
  - Boss 倍率回落：生命改为 ×1.3 / ×1.6（原 1.4 / 2），伤害改为 ×1.15 / ×1.3（原 1.25 / 1.5）。难度主要靠战规叠出来。
  - 借用的战规就是共享变异词条：经 `ExpeditionForcedMutators` → `TryRollMutatorsForArena` → `MutatorModeFlow` → `MutatorManager.RollAndApply(forcedIds)` 强制加上。
    - 不占随机条数，关掉随机词条照样生效。
    - 同一互斥组的词条退出随机候选：四条治疗类词条新归 `healing` 组。
    - HUD 角标显示「战帖」。
  - 休整倍率和盲战都经 `LiveExpeditionTier` 判定，Mode D 不受影响。F3 竞技场用例结束后，用 `ClearMutatorsForMode` 清掉强制词条。
- 守卫与夹具：
  - `ArenaExpeditionOrderGuard` 新增第 8–10 项，反向验证 10/10 转红。
  - 四个守卫原先钉着旧代码，按 §4.10 跟着结构改了：`ArchitectureStructure`、`IntegrationLeafOwnership`、`MutatorSemantics`、`WavesArenaStateOwnership`。反向验证 7/7 转红，均按字节还原。
  - 夹具改动：
    - `IntegrationLeafOwners` 加强制词条断言。
    - `AuditModeLifecycle`、`ModuleOwnerCleanup` 补编译替身，两者原先都是 CS0103。
- 验证：
  - 全量守卫 790/790 PASS。
  - Dev 真编译 0 错误，部署 SHA-256 `34EC12DBD1C0C9F14E630611046520B26E7472B8EDBC3E230AF8C8A5A7E1593E`，与游戏目录一致；`check_dll_identifiers --expect present` 30/30。
  - Wiki 构建通过。
  - 执行回归 16 个 PASS：IntegrationLeafOwners、AffixCombat、AudioRuntime、AuditModeLifecycle、DailyReportHostUI、EquipmentBootstrapOwners、GameplayLogFixes、IntegrationThirdReviewFixes、ModuleOwnerCleanup、SkyIslandMarriage、ArenaHostRemainder、SilenceBellRules、WavesArenaPresetWeight、SkyIslandInteraction、F3ValidationExecution、LegacyArenaEntry。
  - 常驻 HUD 没有执行回归夹具，由 `PersistentHudVisibilityGuard` 覆盖。
- L3 待办：游戏目录里现在是 Dev 包。owner 按 M_STANDARD_03 / 04 / 07、M_NPC_NAMEPLATE_01、止语钟罩、许愿台各项实测。交付前换正式构建，再跑 `--expect absent`。

## 2026-11-25 F3「只跑本次改动」专项 + 配置下拉标题自动用例（COMPAT；L2 + Dev 真编译；L3 待 owner 按一次按钮）

- owner：「能不能从全量测试里分离出来我们这次的，单单测试我们这次的。」
- F3 面板「末班编组站演练」那一行新增按钮「只跑本次改动（许愿七项、尸潮、龙裔、叮当）」。实现在 `DebugAndTools/F3GameplayValidationWishFocus.cs`，整份只在 Dev 构建里编译。
  - 编排：基线 → 基地九条 → 进竞技场 → 场内六条与两次清场核对 → 回基地最终核对。
  - 开跑门、会话外壳与完整验收相同；不接天空岛全自动后半程；不改 `RunSession`（另一守卫的反向探针按字节钉着它）。
  - 覆盖账本用新增的 `GameplayCoverageReport.Focus`（Dev 独占）收窄到本次的用例与 17 条新增人工项，SUMMARY 能给出真 PASS，没跑到的仍算 INCOMPLETE。
  - 宿主 partial 只多 3 行（按钮直接调运行器静态入口，处理函数不进宿主），预算 19653 / 19659。
- 新增自动用例 `MODCONFIG_OPTION_LABELS`（基地阶段，完整验收与专项都跑）：每个登记过的下拉项按官方同一口径 `Options_<配置键>`.ToPlainText() 取标题，与当前语言那句逐字比；再切到另一种语言比一遍，`finally` 里切回原语言后再比一遍。没装 ModConfig 记 SKIP。
  - 用例体单列 `DebugAndTools/F3GameplayValidationModConfigLabels.cs`：许愿用例文件加上它到 1206 行，超出新文件 1200 行上限（`LargeFileBudgetGuard`）。
  - `ModConfigOptionLocalization.SnapshotForValidation`（Dev 独占）给出登记表快照；覆盖清单 CONFIG_UI 加自动项与人工项 `M_CONFIG_UI_04`。
- 守卫：新增 `tests/F3WishFocusGuard.py`，内置 18 条反向检查；另做 3 条落盘反向验证（用例文件加新用例、专项不回基地、删按钮），都转红并按 SHA 还原。`tools/check_dll_identifiers.py` 登记 `TryStartWishFocus`。
- 验证：`--changed-only` 535/535 PASS；执行回归 F3ValidationExecution、LegacyArenaEntry、AuditModeLifecycle PASS；Dev 真编译 0 错误，部署 SHA-256 `D1A53C1867D702F8CCF7D89D7129FD6F5B4DA509F05DE79E94D4B4BECC41B997`，`check_dll_identifiers --expect present` 30 个标识齐全。正式构建里标识不存在这一条未验（会覆盖游戏目录里 owner 正要用的 Dev 包），留到换回正式构建时跑 `--expect absent`。
- L3 待办：owner 在基地、专用测试档按一次按钮，把最新的 `BossRushValidation_*.log` 给 AI 读。复跑前先在 Boss 池里全开（烽烟圈那条要内容 Boss 也能开出来）。

## 2026-11-24 F3 首跑结论 + 配置下拉标题本地化 + 烽烟圈漏标内容 Boss（COMPAT；L2 + Dev 真编译；两处修复的 L3 待 owner 复跑）

- F3 报告 `BossRushValidation_20261124_150538_879.log`：pass=499、fail=11、skip=10。
- 新增用例的结果：
  - PASS（L3）：
    - INK_SKIN_ASSETS、ARENA_EXPEDITION_TUNING、MUTATOR_SWARM_WEIGHT（群鼠频率比 0.501）
    - WISH_FOUNTAIN_RULES、PETNEST_SOUL_FEED_RULES
    - AFFINITY_GIFT_JOURNAL_ROUNDTRIP、NPC_NAMEPLATE_SETTING
    - ARENA_EXPEDITION_ORDER（Boss 生命 ×1.40，有角标）
    - EQ_SILENCE_BELL_WARD（护罩 80 → 60，受击后转写补丁在位，卸下后驱动消失）
    - ARENA_EXPEDITION_CLEANUP、DRAGON_DESCENDANT_SPAWN_PLACEMENT（32 次取点，0 个不合格）
  - SKIP：GOBLIN_POST_DIALOGUE_STAY，原因是叮当此时在婚礼教堂（goblin_at_wedding_chapel），属于前置条件不满足。
  - FAIL，下面两处已修：
    - BOSS_MAP_BEACON（beacons=0）：产品缺陷。
      - 冠军之影、蚀日花冠经 `RegisterContentWaveBoss` 只进 `CurrentBoss` / `CurrentWaveBosses`，刻意不挂随机掉落漏斗，所以不在 `bossSpawnTimes` 里。烽烟圈原先只读 `CopyTrackedBossCharactersTo`（也就是 `bossSpawnTimes`），这两只在竞技场里永远不会有烽烟圈。
      - 修法：新增 `WavesArenaRuntimeModule.CopyMapBeaconBossesTo`，内容为掉落登记表加本波内容 Boss，去重后交给 `IsLiveHostile` 过滤；宿主注册这一行改接它。
      - `CopyTrackedBossCharactersTo` 不动：女巫离场清理会逐个销毁那张表里的角色。
      - `BossMapBeaconGuard` 跟着改接线断言，并新增来源断言。反向验证：接线退回旧函数、漏掉本波内容 Boss，两种破坏都转红，按 SHA 还原。
    - ZOMBIE_CROWD_NAVIGATION（57 秒内一只丧尸都没扫到）：测试脚手架的问题。
      - `ValidationStartZombie` 直接替玩家选了初始流派，但「初始流派选择」面板没关，一直占着模态输入；丧尸模式在模态期间整体暂停，不倒计时也不刷怪。
      - 用例改为照玩家点卡片时的收尾关掉面板，输入租约随面板的 OnDestroy 释放；等一帧后若仍在模态，记 FAIL `zombie_runtime_paused_by_modal`。
      - 共用的 `ValidationStartZombie` 不动：另外三条丧尸用例本来就是绿的，也受宿主 partial 预算约束。
  - 其余 FAIL：
    - 环境原因：MODE_F_LIFECYCLE、MODE_F_BOUNTY、MODE_G_LIFECYCLE、MODE_G_NINE_WAVES（preview_invalid）。
      - Player.log 记的是「Boss 池筛选初始化完成，已启用 2/49」，`BossRushModConfig.txt` 里 `disabledBosses` 有 47 个。只剩蚀日花冠和冠军之影两只，E/F 阵营池和 Mode G 的官方池都不收这两只，所以开不出敌人。
      - Boss 池只有在 Boss 池窗口关闭时才落盘；F3 收窄池子只改内存并在 finally 里还原，所以这 47 个禁用项是 owner 自己在窗口里设的。
      - 复跑这几条前，先在 Boss 池里全开。
    - 老红项，2026-10-18 那轮已经是红的：MODE_G_NINE_WAVES、MODE_H_PLAYER_ENTRY / _CLEANUP / REENTRY / _CLEANUP、CAMPAIGN_FINAL_BOSS。本轮没有动。
- 配置项「没有本地化键」（owner 反馈）：
  - 原因：
    - 用本地扫描器读 ModConfig.dll 的 IL，再对照官方源码。ModConfig 的下拉项（包括 `AddBoolDropdownList`）实例化的是官方 `OptionsUIEntry_Dropdown`，ModConfig 只在创建时 `SetText` 一次说明文字。
    - 官方组件在 Awake、OnValidate 和每次切语言时，都把标题重设为 `("Options_" + provider.Key).ToPlainText()`。这个键没人注入，于是显示成 `*Options_BossRush_xxx*`。
    - ModConfig 自己的滑块条目 `OptionsUIEntry_Slider_Mod.RefreshLable` 是空操作，标题一直是说明文字，不受影响。
  - 修法：
    - 新增 `Localization/ModConfigOptionLocalization.cs`。注册点改为 `Label(配置键, 中文, 英文)`：返回当前语言的文字给 ModConfig，同时登记下来并注入 `Options_<配置键>`。中英文仍只写在注册点一处。
    - `Inject()` 挂在 `InjectLocalization_Extra_Integration()`。宿主在切语言时会重调这条注入链，而且宿主的切语言回调比任何下拉项订阅得都早，所以先注入、后读取，读到的是新语言。
    - `ResetStaticCaches()` 挂在 `IntegrationRuntimeModule.OnDestroy`。
  - 覆盖范围：
    - 面板上实际显示的 12 个下拉项全部改完：8 个布尔开关、成就快捷键、宿命回响放弃快捷键、友好 NPC 名牌、鸭生无常开关。
    - 词缀锻造、图鉴、日报、遗种巢四个总开关的注册函数没有调用方（总开关恒开），也一并统一。
  - 已知限制：
    - 下拉选项文字（如名牌的「1 不显示」）仍在注册时按当时语言定稿，切语言后要重启才会跟着变。
    - 布尔项的「True / False」是 ModConfig 写死的，不归我们管。
- 新守卫 `ModConfigOptionLabelLocalizationGuard`：
  - 扫正式编译清单里所有 add*Bool* / add*Dropdown* / add*Toggle* 的 `.Invoke` 注册点（共 16 处），说明文字必须经 `Label`。
  - 同时核对键前缀 `Options_`、登记与重注入、挂载点。
  - 反向验证：布尔项退回 `L10n.T`、内联标题退回 `L10n.T`、注入链漏挂、前缀写错，四种破坏都转红，按 SHA 还原。
- 夹具：`LegacyArenaEntry` 编译 `IntegrationRuntimeModule.cs`，补了 `ModConfigOptionLocalization.ResetStaticCaches` 的空替身。
- 验证：
  - 全量守卫 789/789 PASS。
  - 隔离回归 PASS：DynamicItemInitialization、EquipmentConfiguratorRegistry、LegacyArenaEntry、ModuleOwnerCleanup、PhantomScytheLocalization、AchievementRuntime、AudioRuntime、BossHealthScaling、DailyReportHostUI。
  - Dev 构建 0 错误，已部署；游戏目录 DLL 与 `Build/BossRush.dll` 一致（`DBFEF456B76E25FD…`），`check_dll_identifiers --expect present` PASS。
- 待办：
  - owner 复跑 F3 看 BOSS_MAP_BEACON、ZOMBIE_CROWD_NAVIGATION，并在 ModConfig 面板切一次语言看标题。
  - 交付前换回正式构建，用 `check_dll_identifiers --expect absent` 实查。

## 2026-11-24 玩家反馈：尸潮抽搐扎堆与打不死、龙裔卡墙、叮当停留（COMPAT / WIRE+；L1/L2，L3 待 owner）

- **尸潮第 5 波起抽搐、不贴身、挤拐角和门外**（CR-2026-11-24-001）：官方 `TraceTarget` 每 0.15 秒调一次 `AI_PathControl.MoveToPos`，后者先取消在途寻路再排队。上百只丧尸同时追玩家时 A* 队列积压超过 0.15 秒，路径几乎都在算完前被取消，丧尸只能沿旧输入直走。新文件 `ZombieMode/ZombieModeCrowdNavigation.cs` 给 `MoveToPos(Vector3)` 加前缀，只拦本局在册、未结算的丧尸：
  - 在途请求不取消，超过 2.5 秒没回调才重发。
  - 近身保持官方 0.15 秒；8–40 米按距离插值到 1 秒，比较时留 0.03 秒一帧余量。
  - 没有路径、走到终点、被瞬移，或玩家挪位超过距离的两成（至少 2 米）时立即重算。
  - 模式未激活时第一句就放行；判定异常也放行官方寻路。
- **叠罗汉、打不到**（CR-2026-11-24-002）：最近刷怪点会让一波几十只在同一坐标出生，恢复监控也把掉队的拉回同一点。叠在别人头顶的丧尸高出子弹平面，看着打中实际打不到，还堵门卡住整波。
  - 普通 / 分裂丧尸的出生点和远距恢复点记 2 秒。目标点已被占时，按 1.6 / 3.0 / 4.4 米三环找可达、同层、不隔墙、不更贴近玩家的空位，每只最多 8 次查询；找不到就用原点。
  - 每秒一次的恢复巡检连续两次看到丧尸脚下是别的角色，就用 ECM2 `SetPositionAndRotation` 把它拨到下面那只旁边的可达地面，并立即重新寻路。不新增 Update。
  - 安全区兜底弹出原来也把多只丧尸送到同一个最近刷怪点，提交前复查补上，同样走散开入口。
  - 复查补源头预防：登记丧尸时给它的 ECM2 移动组件挂 `collisionBehaviourCallback`，别的角色（刚体上有 ECM2 `CharacterMovement`）不可站、不可跨上、不可停在边缘、不随其移动，叠上去会顺着胶囊滑下；聚怪奖励每 0.2 秒往一点拉也不再摞起来。回调全体共用一个静态委托，ECM2 只在脚下命中、台阶、停靠、平台判定时调用，地面和墙没有刚体时一次属性读取就返回。上面的巡检拨离只兜卡在两只中间缝里的少数情况。
- **鸭王杯龙皇召唤龙裔卡墙**（CR-2026-11-24-003）：后备落点固定取起飞点世界 +Z 5 米，不做落地校验。现在从起飞点向 8 个方向、按 5 / 3.5 / 2.2 米找点，要求在导航面上、与起飞点连通、同层、射线不隔墙；都不行就落在起飞点。
- **叮当重铸时乱跑**（CR-2026-11-24-004）：对话、重铸、商店结束后只站 10 秒。`DEFAULT_DIALOGUE_STAY_DURATION` 改为 150 秒，聊天与重铸入口改用这个常量；商店和重铸界面关闭（`showFarewell`）时至少站这么久。再次交互、剧情触发照旧会打断；婚礼与跟随的短停留不变。
  - 提交前复查发现：停留期间移动组件丢弃寻路结果，停留结束还会恢复闲逛，召唤道具在这 2.5 分钟里会像失效一样。两个召唤入口现在先打断停留；剧情对话和婚礼教堂站桩不打断。
- **守卫与回归**：
  - 新守卫 `ZombieModeCrowdNavigationGuard`、`DragonKingDescendantSpawnPlacementGuard`、`GoblinPostDialogueStayGuard`；`ZombieModeBossRushSpawnPointsOnlyGuard`、`EnemyRecoveryOwnershipGuard` 随结构同步。
  - 新执行回归 `ZombieModeCrowdNavigation`：覆盖节流判定，并用 0.4 秒回调延迟模拟队列积压，不节流时一条路径都送不到。
  - `ZombieModeSpawnRuntime` 新增散开用例：占位、三环、隔墙、查询上限、记忆过期、贴安全距离、恢复散开。`EnemyRecoveryRuntime` 断言巡检逐只调用拨离，`ZombieModeSafeZoneRuntime` 断言兜底弹出走散开入口。
  - 反向验证 15 处破坏全部转红，按 SHA 还原。
  - 源头预防复查：守卫补断言（共享静态委托、四个标志、回调里不记日志 / 不分配 / 不查场景、不覆盖别的系统已挂的回调、登记入口必挂）；`AuditModeLifecycle` 断言登记只挂一次；6 处破坏全部转红，按 SHA 还原。
- **性能复查**（owner 要求「是优化、不卡顿、最优实现」）：
  - 寻路前缀：模式未激活读一个属性就返回；激活时 HashSet、Dictionary 各查一次加几次字段读取，不分配。原来每只每秒 6.7 次请求、在途的全被取消；现在每只最多一个在途请求，远处约 1 秒一次，算出来的路径都被用上，A* 总请求量下降。
  - 可达性查询走 A* `GetNearest` 加连通区比较，不发起寻路。散开先用落点记忆排除已占格子，只在目标点被占时查，每只最多 8 次；拨离只在连续两次叠住时查。
  - 叠罗汉回调不新增帧循环；巡检复用原有每秒一次的恢复巡检。龙裔落点每次召唤算一次，先射线后可达；叮当停留只是协程计时。
  - 以上是静态分析与离线回归，实机帧耗未采样。
- **验证**：
  - 变更相关守卫 545 项 PASS；`GameplayValidationCoverageGuard` 在全量里瞬时转红一次（另一会话正在改覆盖数据），单独复跑 PASS。
  - 相关回归 16/16 PASS（含 Reforge、Goblin、Zombie、Mode H 各组）。
  - Windows 正式编译 0 错误，只有既有的 Mode H 两条 CS0649。部署 DLL 与 `Build/BossRush.dll` 的 SHA-256 一致（`E8A418A8C0CCBCAA…`）。
  - 归档：台账将超 1500 行，2026-10-09 / 10 五节逐字移入 `archive/FIX_TRACKER_2026-10.md`。
  - 源头预防复查后：干净签出（HEAD + 本次改动）正式编译通过（ECM2 回调按真实程序集编过）；全量守卫失败集合与 HEAD 相同（16 条缺本地资源）；相关回归 12/12 PASS。主工作区正式构建已部署，游戏目录 DLL 与 `Build/BossRush.dll` 一致（`289682D2…`，含其他会话未提交的改动）。
  - 没有启动游戏、没有读写存档。实机清单见 CR 条目，性能未采样。

## 2026-11-24 F3 验收补用例：许愿七项 + 尸潮 / 龙裔 / 叮当（SAFE 为主；龙裔落点函数改 internal static 属 COMPAT；L2，L3 待 owner 跑 F3）

- 覆盖：本会话批 0–E 的许愿七项，以及 `d21fe965`、`f7747bbb` 两个提交（尸潮寻路节流、落点散开、叠罗汉；龙裔落点避墙；叮当关店后停留 2.5 分钟、召唤可打断）。
- 新文件 `DebugAndTools/F3GameplayValidationWishes.cs`：整份 `#if BOSSRUSH_DEV`，不写存档。共 14 条用例，另有 2 条清场核对。
  - 基地阶段接在 `RunReviewFixBaseCases()` 后面，宿主 partial 只多一行。
    - `INK_SKIN_ASSETS`
    - `ARENA_EXPEDITION_TUNING`
    - `MUTATOR_SWARM_WEIGHT`：生产加权抽取 20 万次，群鼠频率与权重 1 的平均之比要落在 0.4–0.6。
    - `WISH_FOUNTAIN_RULES`
    - `PETNEST_SOUL_FEED_RULES`
    - `AFFINITY_GIFT_JOURNAL_ROUNDTRIP`
    - `NPC_NAMEPLATE_SETTING`
    - `GOBLIN_POST_DIALOGUE_STAY`：照关店调用传 10 秒加告别，等 11.5 秒仍在站，召唤后停留清掉、开始跑向玩家。
  - 竞技场阶段接在 `MODE_ZOMBIE_REWARD_FIXES` 后面。
    - `ZOMBIE_CROWD_NAVIGATION`：
      - 节流前缀已挂上，每只丧尸都挂着尸潮碰撞回调。
      - 12 秒采样内，没有丧尸连续 3 秒站在别的角色身上且高出半米。
      - 20 米外的丧尸平均每秒重算不超过 3.5 次。
    - `ARENA_EXPEDITION_ORDER`、`BOSS_MAP_BEACON`、`EQ_SILENCE_BELL_WARD`、`ARENA_EXPEDITION_CLEANUP`：开「单挑 · 困难 · 短征」这一局后依次核对。
      - 钟罩走受击扣血前的共享入口，并核对 `Health.Hurt` 的转写补丁。原因：套件每帧给玩家开无敌，`Hurt` 在无敌判定处就返回了。
      - 收尾时调 `ArenaExpeditionState.End()`，与切图时的收尾相同，免得后面的用例带着困难战帖。
    - `DRAGON_DESCENDANT_SPAWN_PLACEMENT`：从玩家脚下和贴墙处各取 16 次落点。
- 生产代码只动一处：`DragonKingAbilityController.ResolveDescendantSpawnNear` 由 `private` 改为 `internal static`。它本来就不读实例状态，F3 直接调它。`DragonKingDescendantSpawnPlacementGuard` 跟着改签名；把签名改回 `private` 后守卫转红，按 SHA 还原。
- 接线：
  - `compile_official.bat` 登记新文件。
  - `ArenaCaseIds` 补 6 个场内 ID，进不了竞技场时这些用例一起记 SKIP。
  - `GameplayCoverage.json` 把 16 个 ID 分进 11 个功能的 `automatic`。
- 验证：
  - 全量守卫 788/788 PASS。
  - `AuditCombatSeptember`、`BossHealthScaling` 两个编译龙皇文件的回归 PASS。
  - Dev 构建 0 错误，已部署，游戏目录 DLL 与 `Build/BossRush.dll` 一致（`418686A3D6254241…`）。`check_dll_identifiers --expect present` PASS。
- 结论（2026-11-24 首跑 `20261124_150538_879`）：见上面「F3 首跑结论」一节。新增用例 11 条 PASS、1 条 SKIP（叮当在婚礼教堂）、2 条 FAIL 已修（烽烟圈漏标内容 Boss 是产品缺陷，尸潮用例被初始流派面板卡住是脚手架问题）。交付前要换回正式构建，并用 `check_dll_identifiers --expect absent` 实查。

## 2026-11-24 玩家许愿七项 · 批 E 止语钟罩 500220（COMPAT；L1/L2，L3 待 owner，未提交）

- **止语钟罩**（第 10 项「自充能护盾」）：新增 `Integration/NewWeapons/SilenceBell/`，含 Config、Rules、Runtime、Fx、WeaponConfig、Shop 六个文件。
  - 图腾，品质 5。80 点护罩先于生命吃敌方直接与爆炸伤害；持续伤害、装备效果不吃。最后一次挨打 5 秒后每秒回 20，打碎后至少 9 秒。
  - 接在现有致死前入口 `ZombieModeDamageRuntime.ReduceFinalDamage`，顺序：残日盾片 → 钟罩 → 丧尸模式门，所以对全模式生效。
  - 未佩戴时首行 O(1) 返回；驱动只在出击场景实际佩戴时存在。卸下只撤驱动、余量保留（穿脱不能补满）；死亡、换场景、宿主销毁才满罩。
  - 表现：头顶铜条压在官方血条上沿，过官方 HUD 显隐与暂停双门并登记 `PersistentHudVisibilityGuard`。另有铜钟虚影、碎裂金屑墨烟、回满金环、三声钟声。只用共享 FX 材质与水墨色板，不新建材质或着色器。
  - 来路：无声钟守好感 5 级的第二只匣子「钟守的铜钟罩」，30 枚背包贝壳；本人、钟庭装置、婚后基地一致。节拍器的兑换事务提成 `ReloadMetronomeTrade.TryExchangeForShells(typeId, shellCost)`，门槛提成 `CanTradeAt`，两只匣子共用；节拍器行为不变。不进叮当商店，登记掉落黑名单。
  - 不新增配置键或存档字段。TypeID 台账三处改为下一可用 500221。
- **图标**：暂借 `collectible_sky_bronze_windbell`。正式图标 `silence_bell.png` 的说明已追加到 `docs/guides/出征令与许愿七项_生图说明.md` 末节：照现有物品图标的玩具感 3D 渲染画风，钟舌被布条缠住，轮廓必须和铜风铃分得开。交付后换声明、登记 `production_icon_manifest`、重打图标包、补 Wiki 页图标。Wiki 页暂用首字字牌。
- **守卫**：
  - 新守卫 `SilenceBellWiringGuard`。
  - 同步修改 `ReloadMetronomeWiringGuard`（钟守货架两件、事务转发）、`EquipmentFactoryConfiguratorRegistrationGuard`、`LootBlacklistDataRegistryGuard`、`PersistentHudVisibilityGuard`。
  - 反向验证：在生产代码里做 7 处破坏，全部转红，按 SHA 还原。
    - 护罩改 60
    - 卸下时重置护罩
    - 入口挪到盾片前
    - 货架门槛改 4 级
    - 铜条不跟官方显隐
    - 配置器名错位
    - 打碎只顺延 5 秒（执行回归转红）
- **执行回归**：
  - 新增 `SilenceBellRules`，直接编译生产状态机，23 项断言，含「打碎后不挨打第 13 秒回满」时间线。
  - `ResidualSunShield`、`IntegrationThirdReviewFixes` 两个夹具编译真实伤害入口，补钟罩替身。前者新增断言：钟罩收到的是盾片挡完之后的伤害。
  - `DynamicItemInitialization`、`PassTheBomb`、`ReloadMetronome` 同跑 PASS。
  - 终验全量时又发现两个夹具没跟上钟守的第二只匣子，已同步：
    - `PermanentDuckNpcDialogue`：钟守蓝图改断言两件（节拍器、钟罩），都是 5 级、各一件。
    - `SkyIslandInteraction`：生成器带上钟罩匣子字段，链接真实 `SilenceBellConfig`。新增断言：装置上有第二只匣子，5 级前隐藏，5 级打开贝壳兑换而不是现金商店，销毁时一起释放。反向验证：把装置子节点名改错后转红，按 SHA 还原。
- **终验（批 0–E 合计）**：
  - 全量守卫 788/788 PASS。首轮全量查出 `ModBehaviourInstanceClassificationGuard` 红：批 A 的出征令让 `Interactables/` 净减 1、`WavesArena/` 新增 2，总数 414 → 415。`--changed-only` 跑不到这条守卫。已同步守卫基线与分类文档，往生产文件加一行探针后转红，按 SHA 还原。
  - 全量执行回归（带 DLL 环境变量）170 PASS / 3 FAIL。其中两项是上面那两个夹具，修好后单独重跑都 PASS。第三项 `FieldAdventureVisuals` 和批 D 一样，Unity 没出报告；本批没有改它涉及的文件。
  - Windows 正式编译 0 错误，部署 DLL 与 `Build/BossRush.dll` 的 SHA-256 一致（`66FAFBEF8C32EAAF…`）。
  - Wiki 构建 PASS。
  - 台账超过 1500 行：把 10-11 至 10-17 已闭环的 16 节逐字归档到 `archive/FIX_TRACKER_2026-10.md`，核对零丢失。10-18、10-16 玩家反馈、10-14 三节带 UNVERIFIED，留在正文。
- **文档**：
  - Wiki 新增中英 `equipment__silence_bell`，并接入目录、导航、信息框；装备总览加一条，件数改为 42 / 25。
  - repowiki 新增专题「止语钟罩（SilenceBell）」，挂进「自定义武器系统」，节拍器专题补一行事务提取说明。
  - 实机用例 `M_EQ_SILENCE_BELL_01`–`05`。
  - 没有启动游戏，没有读写存档；铜条位置、虚影观感、钟声与性能都待 L3。

## 2026-11-24 玩家许愿七项 · 批 D 烽烟圈（COMPAT；L1/L2，L3 待 owner，未提交）

- **烽烟圈**（第 8 项「Boss 地图标记」）：新增 `Utilities/BossMapBeacon/`，含纯规则、表现、RuntimeModule 三个文件；宿主只注册一行。
  - 适用模式：标准 / 无间、Mode D、E、G。
  - 地图标记：每只 Boss 一个官方 `SimplePointOfInterest`。
    - 先画朱砂区域圈，35 / 20 / 10 米三档；圆心偏开 Boss，偏移不超过半径的 15/35，每 8 秒重落一次。
    - 安静满 60 秒收一档；主角打到这只 Boss，安静计时从头算。
    - 主角走到 40 米内，或者圈收到 10 米后又安静 60 秒，就显形：圈换成点标记，换水墨烽火图标并放大 1.3 倍。显形时 Boss 身上冲起一道暗红墨烟光柱，配一声战鼓，全场同一时刻最多一道。
    - 同屏最多 8 个，按距离由近到远补位；只标敌对的 Boss（Mode E 不标自己阵营）。
  - 数据只读现成登记表，不做场景查找：竞技场 / Mode D 读 `CopyTrackedBossCharactersTo`，Mode E 读 `ModeEAliveEnemies`，Mode G 读 `CollectTrackedBosses`。
  - 三种模式都不在时 O(1) 早返。`Health.OnHurt` 只在有标记时订阅；最后一个标记移除、ClearAll、换场景、销毁时都会退订。
  - 不新增 TypeID、配置键或存档字段。
- **批 A 漏跑的夹具**：本批跑全量回归时才发现，批 A 在竞技场模块加了出征令调用，两个整编 `WavesArenaRuntimeModule` 的夹具缺替身，一直编不过。
  - `ModuleOwnerCleanup`：替身补上 `ApplyExpeditionTierToContentBoss`，并断言每个内容 Boss 登记前都先乘战帖。
  - `AuditModeLifecycle`：
    - `wave_ordering`：补出征令替身，断言定队列发生在洗牌与前 20 波保护之后，全征仍按过滤池计数。
    - `wave_owner`：补 `EndExpeditionRun` 与两处 `ResetStaticCaches` 计数，断言换场景结束本局、销毁时同时清两份缓存。
- **守卫**：新守卫 `BossMapBeaconGuard`。反向验证：在生产代码里做 4 处破坏（订阅布尔、敌对过滤、重落常量、宿主来源），守卫全部转红，按 SHA 还原。执行回归 `BossMapBeaconRules` 约 2025 项断言；把显形边界的 `<=` 改成 `<` 后转红，按 SHA 还原。
- **验证**：
  - 变更相关守卫 534/534 PASS。
  - 全量执行回归 166 PASS / 5 FAIL，5 项都是环境原因：
    - `AuditCombatSeptember`、`SkyIslandCombatRuntime` 缺 `GAME_PATH` / `BOSSRUSH_HARMONY_DLL`；`EnemyRecoveryRuntime`、`ZombieModeSpawnRuntime` 受 NuGet 缓存内存不足（OutOfMemory）影响。这四项补齐 DLL 环境变量后单独重跑，全部 PASS。
    - `FieldAdventureVisuals` 两次都是 Unity 的 `Library/SourceAssetDB` 被另一进程锁住、没出报告；本批没有改它涉及的文件。
  - Windows 正式编译 0 错误，部署 DLL 与 `Build/BossRush.dll` 的 SHA-256 一致（`75135C493D29F5CA…`）。
  - Wiki 构建 PASS。
- **文档**：
  - Wiki 中英「游戏模式总览 · 通用规则」新增一条。
  - repowiki 新增专题「Boss 地图烽烟圈」，并在「高级功能」挂上链接。
  - 实机用例 `M_BOSS_BEACON_01`。
  - 没有启动游戏，没有读写存档；圈在 M 图上的观感、光柱与战鼓、8 个上限、性能都待 L3。

## 2026-11-24 玩家许愿七项 · 批 C 献魂 + 心意簿 + 友好 NPC 名牌（COMPAT / SCHEMA+；L1/L2，L3 待 owner，未提交）

- **献魂**（第 5 项）：巢页「成长」下一行「献魂（N 遗魂）」，水墨确认框。只扣这只崽自己血脉的遗魂，2 魂换 1 经验，一次补到下一级；每只崽每个游戏日一次（`lastSoulFeedDay` = `GameClock.Day + 1`，0 = 从没献过，SCHEMA+、bundle 版本不变）；满级、远征中、拿不到游戏时钟时拒绝。事务照治疤的候选包。演出 `PetNestSoulFeedFx`：魂火绕头像汇入、墨环、磬声、「Lv.N」朱印。
- **心意簿**（第 6 项）：`NPCGiftSystem.ResolveGiftPreference` 成为反应类型与心意簿共用的唯一判定（厌恶物品 → 喜欢物品 → 收集品 LikedBy → 喜欢的标签）；普通礼物路径第一次命中就记进 `AffinityData.knownLikes` / `knownDislikes`（SCHEMA+，旧档读成空），好感浮字贴纸笺「心意簿添了一笔」。好感页详情加「心意」一节，按当前配置对照，未摸清的只写剩几样。
- **友好 NPC 名牌**（第 6 项）：ModConfig 下拉「不显示（默认）/ 走近时显示 / 一直显示」，默认关延续 CR-2026-10-21-003。选项状态在静态类型 `FriendlyNpcNameplateSettings`：第一版写成 ModBehaviour partial，被 `ModBehaviourPartialBudgetGuard` 拦下，按 AGENTS 4.15 改成独立类型，`Config.cs` 只留三行转发。显示 `FriendlyNpcNameplates` 只读 `NPCNameTagHelper` 登记快照，每帧先过 HUD 显隐与暂停两道门；关时 O(1)，不建任何东西。
- **守卫**：
  - 新守卫 `PetNestSoulFeedGuard`、`AffinityGiftJournalGuard`、`FriendlyNpcNameplateGuard`。
  - `PetNestUILayerGuard` 纳入献魂文件；`PersistentHudVisibilityGuard` 登记名牌。
  - `CollectiblesCatalogGuard` 改为在解析器里查收集品偏好，并要求反应类型转发给解析器（同步改动，不是放宽）。
  - `ModConfigOptionChangeGuard` 能识别「静态设置类型」形状：白名单要写 `ModName + 类名.常量`，单键 loader 要写类名转发，未接线时扫描报盲。
  - 新增与改动的断言全部做了反向验证：共 32 处破坏都转红，按 SHA 还原。其中一处破坏写在注释里，守卫先剥注释，所以没转红，这是一次无效探针；改成真实代码后转红。
- **回归**：
  - `ContentTransactions` 的 `PetNestSoulFeed` 13 项。
  - `NpcAuditFixes` 的 Affinity 组，心意簿用例共 56 项。
  - `SkyIslandInteraction` 的名牌快照 3 项。
  - 三处生产变异（删日戳、删去重、删 2.2 米缺省高度）各自让对应夹具转红。
  - `PetNest` 四组仍 PASS。
- **验证**：
  - 变更相关守卫 487/487 PASS。
  - Windows 正式编译一次报 CS0165（名牌屏幕坐标 `local` 在 `&&` 短路后未确定赋值）；初始化后 0 错误。部署 DLL 与 `Build/BossRush.dll` 的 SHA-256 一致（`4A3DB955CFDA5839…`）。
  - Wiki 源中英同步了遗种巢（献魂）、好感与婚姻（心意簿）、配置选项（名牌）；repowiki 更新了「遗种巢系统」「好感度框架核心」「功能开关配置」。
  - 实机用例：`M_PETNEST_SOULFEED_01`、`M_AFFINITY_JOURNAL_01`、`M_NPC_NAMEPLATE_01`。
  - 没有启动游戏，没有读写存档；献魂数值手感、演出位置、纸笺与名牌观感都待 L3。

## 2026-11-24 玩家许愿七项 · 批 0 水墨套件 + 批 A 出征令与群鼠战术 + 批 B 许愿台星宿与星灯（COMPAT / SCHEMA+；L1/L2，L3 待 owner，未提交）

- **背景**：许愿台优先级表第 1、2、4 项，owner 逐项拍板（方案 `.claude/plans/toasty-frolicking-fern-*.md`）；美术方向水墨东方，UI、交互、特效要史诗、无塑料感。第 7 项天空岛刷新不做（已经削过一次）。
- **批 0 水墨套件**：`Common/UI/BossRushInkSkin.cs`（墨底、金框、朱印、笔势揭示、落印、`BossRushInkMotion` 不受 timeScale 影响、暂停时冻结）；`tools/gen_ink_ui_sfx.py` 程序化生成 11 个低频音效到 `Assets/Sounds/InkUI`（钟、磬、纸、笔、战鼓、余烬）；`UI制作共识.md` 新增第 12 节。
- **出征令**：路牌主选项改为「出征令」，打开水墨面板 `ArenaExpeditionOrderPanel`，三栏为阵容 × 战帖（轻松 / 标准 / 困难 / 噩梦）× 征程（短征 12 / 长征 24 / 全征）。
  - 数值只在 `ArenaExpeditionTuning.cs`，是本局临时倍率：不进 ModConfig，Mode D 借用竞技场模块时不生效。
  - 本局快照 `ArenaExpeditionRun` 由竞技场模块持有。短征、长征在洗牌之后定队列，末波是强 Boss 压轴。出场、倒计时预告与完成判定都经 `ResolveRunPresets` 读队列。
  - 战帖倍率：普通 Boss 在全局倍率之后乘，内容 Boss 在 `RegisterContentWaveBoss` 登记时乘。乘法用修饰器，来源是 `ArenaExpeditionBossScaling`，保持当前血量比例，并调低 AI 反应时间。
  - 不随战帖变的奖励：Boss 自带掉落和无间奖金按未乘战帖的生命计算。
  - 通关箱：件数按 `ceil(基数 × √(L/N))` 折算再按战帖增减；困难起品质下限 6。
  - 成就：旧通关、无伤、速通、累计只认全征 + 标准及以上；无间成就轻松档不计。新成就 `nightmare_clear`，图标待画，说明见 `docs/guides/出征令与许愿七项_生图说明.md`，没交之前用默认图标。
  - 面板记住上次选择：`BossRushLocalPrefs`，写入 `<Mod>/Cache/LocalPrefs.txt`（SCHEMA+）。
  - 演出：局内右侧有战帖小朱印 `ArenaExpeditionHud`（登记为 PERSISTENT 常驻 HUD）；压轴与噩梦登场有水墨标题幕 `ArenaInkTitleCard`（登记为 EXCLUDED）。
- **群鼠战术**：`MutatorDefinition` 加 `Weight` / `ExclusiveGroup`，`RollAndApply` 改为按权重不放回抽取，抽中后剔除同组。群鼠权重 0.5、体型 ×0.75、移速 +30%（原 ×0.6 / +45%），与巨灵兵同在 `enemy_size` 组。
- **许愿台（批 B，第 3 项）**：
  - 最少字数 20 → 4，按标准化后的有效字（字母、数字、汉字）数；提交校验与面板计数同一口径（`CountWishChars`），修掉旧版两边口径不一致。提示改成玩家视角，删掉「请不要输入无效/垃圾内容哦~」。
  - 星宿祈向：标题下一道十二宿穹顶（`WishFountainConstellations.cs` + `WishFountainUI_Constellations.cs`），一宿对应一个奖励类别。点亮后关面板开抽时经 `AddChosenWishCategory` 并进 `matchedCategoryIds`，等同写中关键词；品质概率与偏置上限不变。抽奖前先建奖池，关键词匹配不再依赖预热是否已完成。祈向只在本地，不进发送管线。送出时流星从点亮的一宿划进星泉。
  - 星灯：`WishFountainDanmakuView` 保留类名与公开入口，横向弹幕改成从面板两侧升起的纸灯，三层景深，同屏最多 12 盏，本层嵌套 Canvas 隔离批次重建。上墙前经 `FilterLanternWishes` 重跑联系方式 / 敏感词 / 字符多样性、压三遍以上的连续重复片段、按规范化文本去重。
  - 「星灯」开关在面板右上角，记在 `BossRushLocalPrefs` 的 `wish.starRiver`；熄灯后不拉取、不预热。
- **重铸注释**（SAFE）：`ReforgeSystem.cs` 三处与代码不符的注释改正：费用是价值的 1/100 不是 1/1000，没有保底，最终值夹在预制体值的 30%~200%。数值维持现状。
- **守卫与回归**：
  - 新守卫 `tests/ArenaExpeditionOrderGuard.py`：6 处破坏都在预期断言上转红，按字节还原。
  - `NonWaveBossSpawnGuard` 逐字钉住的 `RegisterContentWaveBoss` 方法体同步加入战帖调用，删掉这行即转红。
  - `PersistentHudVisibilityGuard` 登记了三个新文件。
  - 新守卫 `tests/WishConstellationLanternGuard.py`：最少字数、计数口径、十二宿与类别一致、抽奖顺序、清洗、开关门控共 7 处破坏都转红，按字节还原。
  - 执行回归 `ArenaHostRemainder`：替身记录调用顺序，断言「全局倍率 → 战帖 → 激活」和「销毁收掉快照与面板」。在 scratchpad 稀疏副本里两处破坏都转红，共享生产文件没有被改动。
- **文档**：Wiki 源 `WikiContent` 中英同步了标准 BossRush（出征令）、变异词条、成就大全（46 个）、许愿台（星宿祈向、星灯、4 字）；repowiki「许愿台系统」已更新；实机用例新增 `M_WISH_02`、`M_WISH_03`；repowiki「难度级别系统」「功能开关配置」和竞技场「架构设计」已更新；`GameplayCoverage.json` 新增实机用例 `M_STANDARD_03`–`06`、`M_MUTATORS_03`。
- **验证**：
  - 已通过：变更相关守卫全部 PASS；Windows 正式编译 0 错误（只有 2 条既有 CS0649），游戏目录 DLL 的 SHA-256 与 `Build/BossRush.dll` 一致；执行回归 10 项 PASS（含 `ArenaHostRemainder`、`AchievementRuntime`、`HostMapArenaOwners`、`BossHealthScaling`）；Wiki 站点构建通过。
  - 批 B：变更相关守卫 430 PASS；正式编译 0 错误，游戏目录 DLL 与 `Build/BossRush.dll` SHA-256 一致（`7261404191d77e6c…`）；Wiki 站点构建通过。许愿台没有可隔离的执行回归夹具，行为只到 L1/L2；星灯同屏开销未采样。
  - 未做：没有启动游戏，也没有读写存档；面板手感、角标位置、压轴标题幕、星宿穹顶与星灯观感都待 L3。
  - 待定：更新日志等发版时定版本号再写。

## 2026-11-21 玩家反馈四项 + v2.6.2 发版准备（COMPAT；L1/L2，L3 待 owner）

- **收藏册没鼠标**：`BossRushUIKit.EnforceModalInputPause` 每帧把未归还租约的 token 补进当前 InputManager（`ReassertModalInputBlocks`；`blockInputSources` 是 HashSet，幂等），记下实际挡住的那个 InputManager，归还时只在它身上放开。官方 `InputManager.UpdateCursor` 每帧按「输入是否被挡」重写光标，token 一丢就和租约来回拉扯。执行回归 `SharedModalInput` 新增晚到的 InputManager、token 丢失补占、已归还不补占三项；去掉补占调用实跑转红，还原后转绿。
- **出击图崽背包反复「背包与仓库还没就绪」**：官方 `OnCollectSaveData` 采集成功且巢数据进了缓存后，`PetNestSaveCoordinator.SettleOfficiallyCollectedAssets` 只撤采集那一刻 owner 的基地资产义务，降为引擎「还欠一次 SaveFile」；失败提示只在原因变了或玩家主动操作时弹。`AssetSnapshotBoundaryGuard` 同步；`ContentTransactions` 新增 `PetNestOfficialCollectSettlesBaseObligation`（基地采集后出击可封存 / 出击图采集不降级 / 巢数据没进缓存不降级），去掉降级调用实跑转红。
- **首次登记格子没弹出**：`CollectionRegisterContainer` 逐个调用 `OnStartLoot` 订阅者、各自 try；调完 LootView 没开就直接设 `targetLootBox` 再开；打不开、3 秒超时、按钮没建出来都走 `Fail()`，提示并回到收藏册。收藏册页脚写真实原因（`RegisterBlockedReason`），人在基地却登记不了时打 `Debug.LogWarning`；「能不能登记」变了就重刷。根因没在实机复现，这是对所有失败路径的加固。`CollectiblesCatalogGuard` 新增 5 条断言，4 处破坏实跑转红。
- **许愿抽奖削弱**（玩法数值，owner 要求「狠狠削弱，按游玩时长限制品质」）：下限 Q3，权重 Q3..Q8 = 45/33/15/5.5/1.2/0.3（旧表 Q4 起步 13:9:5:3:2，Q7 以上约 16%）；按本档游玩时长封顶，不满 5/15/30/60 小时最高 Q4/Q5/Q6/Q7，读不出按 Q4；品质改为对整个奖池掷，关键词筛出的池子只决定同品质里挑哪件。品质掷骰原样提到新 partial `WishFountainRewardOdds.cs`（Selection 超了 1200 行新文件预算），已登记编译清单。新守卫 `tests/WishRewardOddsGuard.py`，5 处破坏实跑转红。
- **发版准备**：`info.ini` 升 2.6.2 并拷进游戏目录（SHA-256 一致）；WikiContent 新增中英 `changelog__v2_6_2`，补总览与 catalog；收藏册、许愿台 Wiki 中英同步；工坊简介 `../a.md` / `../b.md` 换成 v2.6.2，超预算按规则删掉 v2.6.1 段（7587 / 7648 ≤ 7700）；repowiki 收集品、许愿台、遗种巢、UI 系统集成四篇同步。
- **验证**：全量守卫 778 PASS；Windows 正式编译 0 错误（2 条既有 CS0649），游戏目录 DLL SHA-256 与 `Build/BossRush.dll` 一致；全量执行回归 167/170，补设游戏程序集环境变量后 `AuditCombatSeptember`、`SkyIslandCombatRuntime` PASS，`FieldAdventureVisuals` 因本机 Unity 批处理没有许可证（No ULF license）产不出报告，它不编译本次改动的任何文件，记为未验证；Wiki 站点构建通过。没有启动游戏，也没有读写玩家存档，四项都待 L3。

## 2026-11-19 v2.6.0 发版收口（COMPAT；L1/L2/L3）

- **内容**：26da41b4 加入 6–8 档收集品陈列柜与地面特效（`CollectibleShowcaseFx.cs`），收集品页与 v2.6.0 更新日志按「点登记才登记、关上只退回」改写并补齐陈列柜、格框、特效、近战体力下调、图标重画与焚天龙皇射击声；5d6dd583 改写实机清单 `M_COLLECTIBLES_03`。
- **验证**：全量守卫 777 PASS；全量执行回归首跑 166/170，配游戏程序集环境变量后 `AuditCombatSeptember`、`SkyIslandCombatRuntime` PASS，`NpcAuditFixes` 另修（见下条），`FieldAdventureVisuals` 因作者工程被另一 Unity 实例占用未跑成；Wiki 构建通过；工坊简介 a.md 7675、b.md 7671 字节（b.md 原本超 121，压缩英文玩法细节）。owner 2026-11-19 回复已完成实机验收（`M_COLLECTIBLES_01`–`08`）并换回正式构建；游戏目录 DLL 与 `Build/BossRush.dll` SHA-256 一致（`4816af54dbda7ca1…`），`info.ini` 2.6.0 一致。

## 2026-11-19 执行回归 NpcAuditFixes 恢复（SAFE；L2）

- **起因**：v2.5.0（f97177e7a，2026-11-13）删掉了 `tests/fixtures/SkyIslandDialogue/Stubs.cs` 里的 UniTask 替身；`NpcAuditFixes/run.py` 一直截取那份文件 `namespace UnityEngine` 之前的一段当异步宿主，于是 Transactions 组从那天起编不过（CS0246 找不到 `Cysharp` / `UniTask<>`），全量回归恒红一项。生产代码无关。
- **改动**：替身原样搬进本夹具自带的 `tests/fixtures/NpcAuditFixes/AsyncHost.cs`（内容取自 f97177e7a 之前那份），`run.py` 改读它，不再跨夹具借用；README 的「net10 要临时改 net8」过期说明同步改掉。
- **验证**：`NpcAuditFixes` 5 组全绿（Affinity 44、Daily 12、Presentation 11、Transactions 22、Reforge 21）。反向探针：把替身 `UniTaskBuilder<T>.SetResult` 改成写默认值，Transactions 转红（「stable identity removes exactly the delivered record」），按字节还原（SHA-256 前缀 `1262bf00d4423fd6` 一致）后转绿。

## 2026-11-19 主角枪械暴击率加成改为爆头伤害（COMPAT；L1/L2/L3，已部署，owner 2026-11-19 实机确认）

- **起因**：owner 提醒「这游戏只有爆头才有暴击伤害」。官方 `ItemAgent_Gun.cs:1459-1463` 按 `CritRate × (1 + GunCritRateGain + 子弹加成)` 算完暴击率后，只要持枪的是主角，就改成 `AimingEnemyHead ? 1 : 0`。挂在主角身上的 `GunCritRateGain` 因此完全无效。近战暴击率 `MeleeCritRateGain` 官方没有改写，照常有效。
- **改动**（枪械那一半一律换成爆头伤害 `GunCritDamageGain`，基础值 0，用 Add；近战部分不动）：
  - 词缀鹰目：4 / 7 / 11% 的数值不变。文案改为「近战暴击率与爆头伤害各 +{0}」。词缀 id 与存档 KV 都没变。
  - 变异百步穿杨：改成「爆头伤害 +25%，枪械散射 -15%」。原来的「爆头伤害 +30%」只会是致命一击（暴伤 +50%）的弱化版，所以换成准头加爆头的组合，和致命一击、稳定瞄准（散射 -30%）各有分工。
  - 变异幸运星：改为「近战暴击率与爆头伤害各 +20%」。
  - 丧尸暴击专注：常量 `ZombieModeStatNames.GunCritRateGain` 改为 `GunCritDamageGain`。文案改为「爆头伤害 +15%，最多45%」，代价不变。
  - 收藏册竞技场系列已在上一条改过。
- **不动的**：
  - 鸭王杯机器人（`ModeHRuntimeModule_PreparedLoadouts.cs`）、宠物、天空岛和沙暴冠军的敌人。这些不是主角，官方的改写碰不到它们。
  - 武器自身的 `CritRate` 属性。和官方枪一样照常显示。
  - 焚天龙铳的自定义弹道（`DragonKingBossGunRuntime_ProjectilesAndPatches.cs:649-656`）：主角不瞄头时保留身体暴击，和官方不同。owner 拍板保留，作为这把枪的特色，代码处已加注释说明不要「修正」。
- **守卫**：
  - 新增 `tests/PlayerGunCritRateBonusGuard.py`：生产代码剥注释后出现 `"GunCritRateGain"` 字面量就判红，只放行作用于非主角的白名单；这几处已改的玩家加成必须继续用 `GunCritDamageGain`。
  - `ZombieModeRewardStatNameGuard`、`ZombieModeRewardPlainTextGuard` 同步到新属性名和新文案。
  - 反向验证：改回旧属性名、旧文案共 6 处，全部转红，按字节还原后哈希一致。
- **验证**：Windows 正式编译通过并部署。`Build/` 与游戏目录 `BossRush.dll` 的 SHA-256 都是 `4a5a7dbad24a137276289e6948a134d53b4b580a1293c3b0509a440c36bf85ad`，没有 Dev 标识。repowiki 词缀锻造页已同步。Wiki 三处文字（鹰目、百步穿杨、幸运星，中英各一）已交给 Wiki 会话，要等它改完再部署一次。
- **回归与台账**：
  - 回归：AffixCombat、ZombieModeRewardRuntime、IntegrationThirdReviewFixes、ZombieModeHostOwners、ZombieModeSafeZoneRuntime、ZombieModeWaveRuntime、CollectibleLoot 共 7 组，全部 PASS；
  - 守卫：changed-only 483 个，全部 PASS；
  - 台账：本条写完后超过 1500 行，把 2026-10-07 的 4 节原样移到 `archive/FIX_TRACKER_2026-10.md`，块 SHA-256 `c2127adc…04a2`，正文留了索引。
- **Wiki 与再部署**（同日稍后）：
  - Wiki 会话 `local_73a5bbd6` 改完了两批 Wiki，中英都有：收藏册的奖金、加成、剪影措辞和新件优先，以及鹰目、百步穿杨、幸运星和 v2.6.0 更新日志；站点构建通过。
  - 本会话逐条核对过文字。随后重新正式构建并部署：`Build/` 与游戏目录 DLL 均为 `9f99ee1e83a8fc8baf6fa0bb3dc5a1e182097d78100387a5bdcf10913eda0d80`，取代本条与下一条记的旧哈希；WikiContent 277 个文件和游戏目录逐字节一致；没有 Dev 标识。
- **待实机**：
  - 带鹰目或变异「百步穿杨 / 幸运星」爆头，伤害数字应明显高于不带时；打身体不暴击，和原来一样。
  - 丧尸模式拿「暴击专注」后，爆头伤害随层数上升。

## 2026-11-19 收集品与收藏册审核七条全修（COMPAT；L1/L2/L3，已部署，owner 2026-11-19 实机确认）

owner 看过审核清单后说「全部修复」，修的是 CR-2026-11-19-001～007，审核报告见 `docs/reports/reviews/2026-11-19-收集品与收藏册审核.md`。仍未提交。

- **001 光晕**：两处 `ConfigureItem` 按档次写 `DisplayQuality`（`CollectibleTuning.DisplayQualityOf`，1–8 档对应官方 White…Q8），不再沿用克隆源的红光晕。
- **002 登记事务**：新增纯函数 `CollectionRegistration.Apply`，先写存档，写成功才逐个 try 跑后续三步：发奖金、提示、刷新杰夫任务。一旦提交，物品必定收走。
- **003 部署**：关游戏后重跑正式构建并部署。`Build/` 与游戏目录 `BossRush.dll` 的 SHA-256 都是 `c2f65247bdd6b634d854194e8517b30c8cb82c7e61ee93abb05ae5beaefc1b6a`，按字节查不到 Dev 标识，81 个资源包 SHA-256 校验通过。
- **004 售货机**：`TryInjectIntoShop` 改为「现在卖不卖」，刚挂上或本来就在都算；`TickShop` 真挂上才收工，否则每 2 秒再试。
- **005 文案文档**：
  - 挖沙船船铃的英文改用 "The Dredge Boss"，道岔扳手改 "driver's cab"；
  - 杰夫按册子实际去处说话（背包 / 仓库 / 去售货机拿）；
  - ID 表表头范围改到 500219，补了 11-19 的变更记录；
  - 英文工坊简介 `b.md` 补上功能条目，现在 7675 字节。
  - Wiki 的措辞、奖金与加成数字已发给 Wiki 会话（`local_73a5bbd6`）去改，本会话没碰 `WikiContent/`、`wiki-site/`。
- **006 测试**：
  - TidegateCargo 新增 `CollectibleHook`：淹着不交、首开交一次、重开不交，水下上限 4、陆地 8；
  - LastShiftCargoRuntime 断言封签柜、铅封侧柜不交；
  - CollectibleLoot 新增登记事务故障注入、新件优先、显示品质、奖金不低于卖价四组断言。
- **007 平衡**（owner 授权，按好玩优先定）：
  - 集齐奖金不低于整套卖价：地图系列 2 万 → 3 万，全图系列 6 万 → 15 万，全集大奖 50 万不变；
  - 新件优先：抽中品质后，一半机会只在还没登记的件里挑。出现率、品质分布和 7、8 档每箱概率都不变；凑齐全图系列的中位数从约 400 件降到约 250 件，地图系列从 32 件降到 28 件；
  - 主角枪械只有爆头才算暴击（官方 `ItemAgent_Gun.cs:1460-1463` 对主角改成瞄头 = 1、否则 = 0），所以竞技场系列的加成从「枪械暴击率 +1%」改为「爆头伤害 +5%」（`GunCritDamageGain`）。
- **线索加固**：
  - 官方开箱判定另按箱子所在场景排除 Mod 图（`buildIndex < 0`），不再依赖活动场景；
  - 登记容器根对象 DontDestroyOnLoad，容器开着时一开始读条就先登记、退回；
  - 过图时收起收藏册（口径同图鉴）。
- **验证**：
  - 回归：CollectibleLoot（2,482,008 条断言）、TidegateCargo、LastShiftCargoRuntime 都通过；
  - 守卫：`CollectiblesCatalogGuard` 同步到新结构。反向验证改坏 8 处（显示品质、售货机置位、DDOL、Apply 的 try、全图奖金、新件优先、编组站排除、潮闸水下上限），全部转红，按字节还原后哈希一致；
  - Windows 正式编译通过。
- **待实机**：`M_COLLECTIBLES_01`～`05` 已补光晕颜色、奖金 30,000、背包满时送册的预期。
- **后续**：词缀锻造、变异词条、丧尸模式里同样无效的枪械暴击率，已在上一条「主角枪械暴击率加成改为爆头伤害」里改完。

## 2026-11-17 潮闸镇背向门前区：门背对镜头时站到门前也收起本栋上部（COMPAT；L1/L2，L3 待 owner）

- **起因**（owner「玩家是俯视角，潮闸镇有些门是上面被建筑挡住了，导致玩家需要蹭门才知道在哪里有门」）：官方默认机位俯仰 55°、偏航 −30°，从东南往西北看，北墙、西墙整面背对镜头，门外紧贴墙那几米地也被楼体挡住。布局 27 扇门里 11 扇背向：海员旅馆西门、仓房、镇卫生所、民居 h10、杂货铺、镇区泵站两扇、三号仓库北门、潮位站、船舱、沉没的储蓄所。
- **做法**：`Common/World/RaidIndoorRoofs.cs` 加门前区（`AddDoorway` / `TryGetRoom`），收起哪一栋只有「脚点在屋里」和「脚点在登记的门前区」两个来源，屋里优先；官方 `OcclusionFadeObject` 按进出计数，改成只经 `faded` 一个字段成对调用。室内补光仍只认真正进屋。新文件 `Tidegate/TidegateDoorways.cs`（已登记编译清单）把布局门表写死，只给背向门登记；门前区沿墙是门洞两侧各 1.5 m，深度取「楼高 × 背向分 / tan 55°」与「回到墙面前横移出墙端」两个约束的小者再加 1 m（至少 2.5 m），楼高读本栋 Roof 渲染器的实际顶高。`TidegateSession` 在世界与图层建好后登记一次。不恢复 11-07 撤掉的全图视线裁切，邻楼与室外其余地方不变。
- **验证**：`TidegateIndoorRoofGuard` 改到新结构（8 条新断言逐条人为破坏转红、按字节还原）；新增 `TidegateDoorwayPropertyTest`（门表与布局逐项一致、背向恰是北西两面、光线步进复算门洞两边与中点的被挡带，门前区要盖住又不能多出 0.3 m 以上；自检三种篡改转红），登记 `EXTERNAL_ARTIFACT_GUARDS`。首版公式按无限长墙算，被光线步进查出窄楼深 2–3 m，改成闭式两约束。夹具 `LastShiftLightingRuntime` 的 `OcclusionFadeObject` 替身改成官方的计数语义，新增门前区用例（只收本栋、补光不亮、进出不重复计数、别栋门前区盖进屋时屋里优先、清理还原）；去掉 `OnLeave`、把门前区排到屋里前面两种变异都转红（`if (true)` 是等价变异，不算）。`LastShiftWorldRuntime` 回归 PASS。正式 Windows 构建通过（仅既有 Mode H 两条 CS0649），已部署，游戏目录 DLL 与 `Build/` 同为 `7DEA0897…D3F0`，Dev 标识缺席；资源 81 包 SHA 一致。
- **待实机**：覆盖表人工项 `M_TIDEGATE_DOOR`（逐扇走到门口、进门、退回、走开；日志「背向镜头的门前区 11 处」）。手感（门前区够不够大、收起会不会突兀）只有 L3 能定。
- **规则**：`SkyIsland/AGENTS.md` §6.1 第 15 条；新地图流程 02 §4.1 / §5、05 §3 / §15、06 §2、README 教训索引。

## 2026-11-13 v2.5.0 发版：末班编组站快节奏改版、每班一场仗、审查六条与卡口站位（COMPAT；L2，部分 L3）

- **改版**（owner「做成快节奏的多趟火车的激战……每个有宝箱的地方都有激战……小队长……」）：一班 118 秒；第 8 班末班车尾梯是跟车走的撤离口，出站后 `YardClosed` 封站；车头撞押运；每班可签一柜（续签 4/5 件、Q5 随柜数升）；加价台逐班抽、进站揭晓；守货岗 12 处与三种小队长（`LastShiftLeaders.cs`）。追加「每班一场仗」：列车押运每班 3–5 人、另记一本账，来车给那一侧打光的守货岗补岗。
- **独立审查六条全修**：守货岗成员不走 25–75 m 刷怪环（只要求 ≥ 18 m）、卡口有开张上限、一个都没刷成不算打光；关着的会动出口补注销官方地图标记；押运班长压上来改整队叫醒；末班车走后尾梯锚点回站界外；一班车一人只撞一次；末班多放那人有座位、封站后不给拉铃；Dev 演练改推后末班、不回拨班号。
- **实机日志（owner 11-13 一局，旧一版）**：续签与收回、车尾送人、车挡 / 压上来 / 推车误伤都跑了；北公路卡口四个站位压在北跨线桥中间楼梯实心台阶里、整处没开——圈心挪到 (4.5, -46)，`LastShiftSquadPlacementPropertyTest` 加楼梯 / 坡道判据，原坐标反向转红。
- **验证**：全量守卫 768 PASS；全量执行回归 169 项：首跑 164 PASS，配上游戏程序集环境变量后 `AuditCombatSeptember`、`SkyIslandCombatRuntime` PASS，`TidegateWater` 夹具补链同日新增的 `TidegateBuildingCollision.cs` 与两处 Unity 替身后 PASS；仍红两项与本版无关——`FieldAdventureVisuals`（本机 Unity 无许可证）、`NpcAuditFixes`（夹具缺 UniTask 替身，18 个生产输入均未改动，HEAD 即红，待单独修）。正式 + Dev Windows 真编译通过（仅既有 Mode H 两条 CS0649），游戏目录 DLL 与 `info.ini` 哈希一致。每班一场仗与卡口新位置未实机（L2），实测清单 `M_LASTSHIFT_10`。
- **发版**：`info.ini` 2.5.0；WikiContent `changelog__v2_5_0` 中英、总览与目录；工坊 `a.md` / `b.md` 换 v2.5.0（7553 / 7699 字节）。宣发片录制计划 `docs/guides/trailer-kit/lastshift/实机视频计划.md`。

## 2026-11-13 仓库入口、官方对话与重进修复（COMPAT / SAFE；L3 待验）

- **CR-012**：入口原包零碰撞，补桌/站牌/接驳车与剪票钳分件；末班补五处门框、两宽柱脚、四药箱。潮闸按真实部件补 279 代理，11 个邻近交互可达、27 个既有门洞净空保留。作者脚本不再按高度或交互 marker 重叠删硬体，新旧包碰撞幂等复用，停用根装配不误建重复体。
- **CR-013**：余响聊天、交票、登车与收尾接真实官方 Actor / DialogueUI 和共享 DialogueManager。只当前 owner 消费 Esc，选择前取消无交物/登车，已确认登车的提交后结果页关闭沿用既定流程；切图单独取消。未读完回应不提前记已告知，语言刷新现有 M 图标记。
- **CR-014**：入口按真实 Scene handle 与有效状态重建；余响在角色刚创建、尚待配装时就归入口，旧任务只清自己的对象，销毁后的 actor 缓存也移除。每趟保留剪票处标记。旧日志证实第二次创建过，用户现场后来不可见的唯一根因未证实，仍须连续往返复验。
- **验证**：全量 768 PASS；四组生产执行回归通过，入口 51 断言、共享对话 42 断言；故障注入按预期转红/还原；Windows 原 bat 正式+Dev 真编译通过，1357 源码输入与工作区一致，29 个 Dev 标识分别全无/全有。未做 L3、不声明性能无回归。
- **交付**：正式 `33a3abfc8a6074a0cea65cfdd97b3c4a5e8c4ff4d4a22ae0116d9ddd0dfbb203`，Dev `0981f5a0abcfbec9d1d829d95146adb0f31f99f34c449a89bda18204166eef3b`，位于 `Build/warehouse-entry-20261113/`。真实游戏安装件仍为 `8fe6e102f9fd5fd501d52446b5122e1620e8706059877149aab1df6089672a15`，权限外未部署。场景包保持上一轮修复哈希，未完整重打 Blender/Unity。
- **规范与边界**：根/子系统规则、官方复用规范、新图教程、NPC 手册、contracts、两图知识库和人工覆盖已更新；禁止复制旧“矮硬物免碰撞”与自绘 NPC 全文确认框方案。未启动游戏、未读写存档、未提交；私有同步未运行。详见 [本批报告](docs/reports/testing/仓库入口_2026-11-13_实体对话与重进.md)。

## 2026-11-13 后续实机地面与曝光反馈（COMPAT / SAFE；新画面待 L3）

- **现场口径**：owner 回传 `BossRushValidation_20261113_045031_241`：末班演练 11 PASS / 0 FAIL，COVERAGE 的 387/211 为全项目未通过/未运行及人工待测，不是本图错误数量。原生补光 READY 证实路径，但人物过亮和地面污迹仍为视觉不合格。零性能基线不用于前后对比。
- **CR-010**：确认官方 SodaPointLight 为加法混合，四盏暖 HDR 灯重叠过强；两图共同改中性 191/255 色、Hardness .5 / FallOff 1.5，覆盖范围保留、后备普通灯强度减半。13 场景含房形和上身叠加峰值，放大灯体积负向命中并按字节还原。
- **CR-011**：污迹定位到作者四组 worn ground 贴花及 atlas 中深色椭圆/飞溅画笔；精确取消，生产源和实包配套，保留正常铺面结构、轨道/道砟与投影。专用资源检查和恢复路径见后续报告及 ArtSource README。
- **天气诊断**：原版仓库配置与官方相机链完整，未确认重复主光/错误 Volume。保留官方天气和 fog 阈值；两图 F3 装配在搬人前只读记录天气/时段、真实太阳与绑定、环境色、相机雾及房间灯值。缺现场参数不把灰雾归为阴天。生产日照夹具 10 场景、诊断偷换来源的负向通过。
- **验证与交付**：Windows 原 bat 正式/Dev 两版真编译通过，7 组相关执行回归通过，29 个 Dev 标识正式全无/Dev 全有。新正式 DLL `924d783cc9071b257a45392207d531dde3c3f0fd3873ecf1183ca370cf462992`，Dev `05a0bb31a5897d19607c606eec06052eeaa927765e2ce8282d76df27ae472535`；文件在 `Build/lastshift-visual-feedback-20261113/`，未覆盖真实游戏目录。最终资源与守卫结果见 [后续报告](docs/reports/testing/末班编组站_2026-11-13_地面与光照反馈.md)。
- **最终核验**：全量 766 PASS / 0 NEW-FAIL / 0 KNOWN-RED；末班资源包 `6d78eaf76b85d2f104c6f90fcfd9bbae85837e4965f8f197aae47ed4010aba27`，四个 Mesh + 一张 atlas 改动、其余 5466 对象字节一致，旧包/旧PNG/旧FBX独立反向、幂等及原备份重放均通过。编译输入与工作区零差异，20 文档 280 链接存在。实际安装 DLL 仍 D94B…，未把工程修复称为已部署。
- **规范**：官方复用/渲染规范、new-map 验收指南、contracts、SkyIsland 规则、两图知识库与人工覆盖项同步。当前图不算本批新外观通过；按报告到相同地面与司机室复测，并用新增日志确定实际天气/雾。未启动游戏、未读取或修改玩家存档、未提交；私有同步目录不可写，本批未同步。


## 2026-11-13 双地图官方复用及制作规范（COMPAT / WIRE+；L1/L2 完成，未部署 / L3 待验）

- **代码**：两图共享原生主光与 SodaPointLight，潮闸在基地户外卸载前捕获资源；官方驱动天气、自研材质只镜像。补近脚地面选择、末班两处原生浅水、带入钢印实例和奖励事务、潮闸手轮预留/关键库存核验/涨潮 LootView 与原生状态回写复位，以及官方出口标记与语言刷新。
- **资源**：潮闸实际外露共面修复覆盖 13 节点（8 Mesh / 5 marker Transform），只改已确认视觉层，非目标对象字节一致；最终静态水平扫描外露代表样本为零。作者源未重打，后续重建须按 ArtSource/Tidegate/README.md 重跑定点工具与实包核验。末班去草与透明/分层修复保持。
- **文档**：新官方复用规范，更新 new-map 各阶段、渲染规范、旧教程、物品接线、contracts、SkyIsland 规则与两图知识库；纠正新品默认进叮当/售货机的旧建议。官方原有能力、地图适配职责、资源所有权、交易时序、反向测试与 L3 条件分列。
- **验证**：全量 765 PASS；19 组相关执行回归 PASS；新增库存/原生状态/手轮提交三种生产变异及四项守卫变异命中预期并恢复。正式 / Dev Windows 真编译成功，1356 构建输入哈希一致，正式 51 个演练标识全无、Dev 全有。潮闸 shader、23 室内区几何及反向通过；19 份文档 275 条本地链接核验通过。详见 [本批报告](docs/reports/reviews/2026-11-13-双地图官方复用与制作规范复核.md)。
- **正式产物**：`Build/BossRush.dll` = `16249c1bbba2cf0317848d67d7170f364ef38bdfb00d79cdd8cbffcb44fee014`；潮闸包 = `a268a5f65fe40ed7896b5abae5756e0b00e1b81565da03a827a28b6260bf245c`；末班包 = `499a15401868d7f308de998391723e1e31fa21921e7f915a6e4a31df8904e699`。
- **边界**：安装目录、原 Unity 工程与私有同步 Git 目录不在可写范围；未部署、未执行私有同步、未启动游戏/访问存档、未提交。精确晴天 profile 缺失时有官方同时段后备与警告；室内实际亮度、天气观感、开窗瞬态与帧耗仍需报告中的 L3 操作验收。

## 2026-11-13 官方室内补光与去装饰草（COMPAT / WIRE+；L1/L2，未部署 / 新画面待验）

owner 反馈室内角色仍暗，并询问从地下冒出的草是否来自官方旧地图。已直接读官方源码、真实仓库资产 / URP 配置 / 草插件 IL，并核对现场安装 DLL 与上一轮地图包一致。

- **官方补光**：替换旧四盏 2.4 强度普通点光为官方 `SodaPointLightMesh` + `ZeroPoint` 占位材质，走活动相机的 `SodaLight` 覆盖渲染。官方着色器从深度重建位置、读法线与 GBuffer 反照率；因此角色获得补光，自研 albedo=0 的环境不再叠亮。使用官方低硬度与近线性衰减，按三间房尺寸设置椭球；不调相机或全局渲染配置，不去掉屋顶阴影。
- **生命周期**：仓库卸载前捕获并用停用 DDOL 持有节点保留共享资源，入图世界检查之后绑定；只建本图灯体。离屋当帧关、换屋复用、失败 / 离场均清理，不销毁官方共享资源。只读当前 renderer 的 protected `rendererFeatures` 校验官方通道，缺资源 / feature / mask / shader 时明确记录一次回退。成功日志 `INDOOR_FILL_READY mode=SodaPointLight layer=15`。
- **草**：四组 `Connected yard / grass ribbons 0..3` 加 `Verges and drainage reeds` 是原型细节脚本自动装饰，三版包 Mesh 字节一致；根高 .06 / -.05 / -.04，容易露出地坪。与官方 TastyGrass 全局画质无关，未改全局设置。五个 MeshRenderer 各改一个 enabled 字节，其余 5466 个对象不变；源 `props` / `scatter` FBX 同步去掉这五个 leaf 对象且保留上一轮分层，其它模型 / 材质一致。原型取消五组草的输出但保留其他使用的随机序列；Blender 组装按准确名单过滤旧 GLB。工具 `tools/remove_lastshift_grass.py`、名单 `tools/lastshift_vegetation.py`，报告 `ArtSource/LastShiftYard/lastshift_raid_grass_report.json`。
- **闪烁判断**：没有证据支持“官方旧地图叠在下面”。原型大块透明度与共面层问题已有独立证明；草面近乎竖直，不将其当成先前地面平行重叠的根因。移除草后，两组已修共面三角与四透明地贴均再次通过读回，135 材质 / 4 pass 保持。
- **验证**：75 项相关守卫 PASS；4 组 LastShift 回归 PASS，新 `LastShiftLightingRuntime` 内部 8 场景及四项隔离反向验证通过；真实 Windows 正式编译通过（两条既有 Mode H CS0649），正式 DLL 不含 29 个 Dev 标识；5 个渲染器修改、非草原型输出等价、FBX 保留、判包与旧闪面样本复核通过。灯资源跨图与逻辑正确性是 L2，屏幕亮度 / 帧耗仍待 L3。
- **工程产物**：`Build/BossRush.dll` SHA-256 `bfd7ced29cfcd563bc014574b2eb25e1015153d84f3e83229afceab66b5fcec0`；`Assets/arenas/lastshift_raid` SHA-256 `499a15401868d7f308de998391723e1e31fa21921e7f915a6e4a31df8904e699`。备份 / 编译日志 `Build/lastshift-official-light-20261113/`。本次没有新 Unity 构建，未启动游戏、未读写存档、未提交。
- **部署**：当前会话可写范围仍不含游戏 / 原 Unity 工程，安装件未被本次替换。owner 本机在仓库根去掉 `BOSSRUSH_DEV_BUILD` 后运行 `compile_official.bat` 部署，再按 M_LASTSHIFT_03 / M_LASTSHIFT_READ 复验；后续 Unity 重建前先同步本目录更新后的 FBX。

## 2026-11-13 末班编组站验收缺陷修复（COMPAT；L1/L2 完成，未部署 / L3 待验）

owner 在独立验收后要求「全部修复」。`CR-2026-11-13-001/002` 的代码缺陷已关闭，最终实机画面及到岗时机保留人工验收，不把离线通过写成已生效。

- **地图**：移除 0.02 m² 单面 / 0.01 m² 交集阈值，改为只排除 1e-8 数值退化面并补空间分桶边界。`tools/rebuild_lastshift_fbx_layers.py` 重算 6 份 FBX 与实例 layers，坐标 / 索引 / RGB / UV 等逐项不变；原 .blend 未重新生成。Blender 启动失败、Unity 许可证 IPC 超时退出 199，因此改用 `tools/rebuild_lastshift_bundle_layers.py` 在既有合法场景包上只重排顶点 alpha。最终包 5471 个对象中 243 Mesh 更新，其余对象、未改场景 member、容器参数、类型树与依赖保持；11 个原无颜色通道的网格所用材质均 `_UseVertexColor=0 / _Decal=0`，新增白 RGB 不影响着色。两个原漏检样本 alpha 255/255 → 223/255、191/223；四磨损贴花几何 / RGBA / UV / 混合均不变；独立低位水平面扫描未检出原有可见同层重叠。判包 135 材质 / 4 pass（含 GBuffer）通过。报告 `ArtSource/LastShiftYard/lastshift_fbx_layer_report.json` 与 `lastshift_raid_layer_report.json`，本次不是 Unity 重构建。
- **角色补光**：保留每屋四个真点光，`LastShiftSession.Update` 先发布当帧室内脚点再执行 `lights.Tick`，消除进出门慢一帧；反向顺序探针转红并按字节恢复。
- **Boss**：首个可签收班次锁线后，在对应柜后 3.2 m 生成并预留人数名额；已出场时扳岔即提前换岗。停靠导航按车身加 0.65 m 净空封三角形，进出站仍整线封锁，形状变化取消过期路径；普通 / 重载缓存分开。实际导航包 1217 三角形 × 四种停车状态与独立几何判据无差异、跨台均可达。后续临近锁线换台时正常步行需要时间，不承诺瞬间到柜，最终到岗时机待 L3。
- **签收**：必须实装普通 8 / 加价 9 件，前两件 Q4 以上、其余 Q2–Q4，才开放并消耗签收。隐藏装柜、实物核验、整柜回滚、重入门控；缺货 / 创建失败 / 库存异常 / 中途关闭均保留签收机会。新 `LastShiftCargoRuntime` 链接生产路径，11 场景（含三班单各两档）通过；移除实物校验的隔离反向转红后按字节恢复。
- **验证**：全量守卫 764 PASS；LastShift 回归 3 PASS；正式 / Dev 原脚本 Windows 编译通过（仅既有 Mode H 两条 CS0649），1352 个编译源与快照逐字节一致；正式 29 个 Dev 标识全无、Dev 全有；Wiki 构建通过。旧台账一节逐字移入 2026-10 月归档，LedgerSizeGuard 恢复通过，待 L3 状态保留。
- **产物**：工程 `Build/BossRush.dll` 是正式版，SHA-256 `6b18f9c640a1f9f8928bd67ed7df6785c0e4c7e3941746354d2efb9ff4af4b97`；`Assets/arenas/lastshift_raid` 为修正版，SHA-256 `f2f169469c86f2de393bbc47adf9ff8b83ab7e8b08a6d6f8b4a5bf54143a49fe`。旧包 / FBX 在 `Build/lastshift-fix-before-artifacts/`，编译证据在 `Build/lastshift-fix-compile-20261113/`，地图可重算工具已入源码目录。
- **交付边界**：游戏与原 Unity 作者工程均不在当前会话可写范围，未覆盖安装件；只读核对游戏 DLL 仍为 `a18b36c0…7af06`、地图仍 `b1b6d708…bc86c`。owner 可在本机仓库根运行 `Remove-Item Env:BOSSRUSH_DEV_BUILD -ErrorAction SilentlyContinue; $env:BOSSRUSH_NO_PAUSE='1'; & "$PWD\compile_official.bat"` 部署正式版，按目标哈希核对后重新进图复测。未启动游戏、未读写存档、未提交。

## 2026-11-13 末班编组站第三轮独立验收初始记录（当时未闭环；后续修复见上节）

- `CR-2026-11-13-001` Open / P1：当前发布导航包在西线或东线占用期间均无法跨台，押运长却到锁线后才换岗，对侧已能签收。生产规则与发布包复算 L1/L2，详见 `CODE_REVIEW_FINDINGS.md`；不能按驻点坐标已改判定完成。
- `CR-2026-11-13-002` Open / P2：计划为 8/9 件、两件 Q4 以上，但部分装箱失败仍消耗签收；L1 条件路径，未证明正常现货短缺。
- 地图大地贴修复已在实际部署包，角色真实点光接线通过离线核查；很小的共面几何及最终闪烁 / 人物亮度留 L3，未把 owner 的旧问题截图当新版通过证据。
- 本轮实跑：`run_guards.py --changed-only` 59 PASS；LastShift 回归 2 PASS；隔离源码 / 游戏 Managed 副本的原正式脚本 Windows 编译通过（两条无关 ModeH CS0649 警告），构建快照与当前 1352 个源文件逐字节相同；未部署该隔离 DLL。作者 / 仓库 / 游戏 `lastshift_raid` SHA-256 一致 `b1b6d708…bc86c`。只补验收台账，不改玩法，不启动游戏、不读写玩家存档，不提交。

## 2026-11-13 末班编组站第三轮：还在闪、人身上没亮、押运长离签收太远、签收太少（COMPAT；L1/L2，L3 待 owner）

owner 实机（六张截图）：①地图上还是闪（截图里锯齿边的深绿 / 土黄 / 青灰大块）；②进屋后周围亮了，玩家身上没亮；③押运长离签收的地方太远，放到签收处；④签收奖励太少，起码 7、8 件，品质不用都好，保底几件好的。

- ①根因：截图里的大块是 three.js 的磨损地皮 `Connected yard / worn ground 0–3`（`yard-details.mjs` `stain`），原型是 `transparent` + `depthWrite:false` 的顶点 alpha 贴花（斑心 0.24–0.64、外圈 0）。第二轮共面分层把它们的顶点 alpha 改写成层号、Unity 按不透明画，成了实心色块，同一张网格里几百块斑在同一高度互相抢深度（约 4950 m²；第二轮只查不同对象之间，查不到）。`tools/build_lastshift_blend.py` 新增 `mark_vertex_alpha_decals`（材质 Alpha 直接接顶点色 ⇒ 贴花：保留原 alpha、不进分层、清单记 `decal`）；作者工程 `LastShiftBuilder` 给贴花建透明队列 3000、SrcAlpha / OneMinusSrcAlpha、不写深度、不投影的材质（清单丢了标记就构建失败），`LastShiftEnvironment.shader` 前向 pass 的 `Blend` / `ZWrite` 走材质属性、`_Decal` 时顶点 alpha 当不透明度。另外共面分层的单位从对象细到「对象 × 连通块」：同一张网格内部还有约 1461 m² 不透明重叠（镜头可见约 186 m²，「废弃短线」等），本次 9718 个节点、0–5 层、同层重叠 0。
- ②`LastShiftLighting`：人在司机室 / 维修棚 / 押运库时，除了灯数组，在四盏顶灯位置各放一盏真点光（不投影、强度 2.4、出屋当帧关、`Dispose` 销毁）。灯数组只有自研着色器读，官方材质的角色只吃真光源；自研着色器 albedo 为 0，真光源不会把屋里照第二遍。
- ③`LastShiftEnemies`：押运长不再站押运库前院，守在停靠那一台的铅封柜正背面 3.2 m（`ChiefPost`）；开局在道岔当前指向的那一台，每班车进站锁线后换到停靠那一台、车走了原地等下一班（`GuardCargo`，交火中交给官方行为树）；押运库那一岗改由普通押运员顶（`PostHeld` 去掉押运长特例）。余响提示「押运长守着那儿」改为「库门口有押运员守着」。
- ④`LastShiftLoot.PlanCargo`：一柜 8 件（加价台 9 件）：头格照旧 Q4 75% / Q5 25%（加价台各半）、第二格保底 Q4，按班单类别抽精品；其余按班单类别补 Q2–Q4（每高一档权重 ×0.6，同风险柜普通格）。箱子 12 格。签收提示改成「满满一柜，最上面压着……」。经济：每趟签收从 1 件精品变成 2 件精品 + 6–7 件杂货，owner 本次明确要求。
- 文档：Wiki 中英与站点副本、`GameplayCoverage.json` 人工项 M_LASTSHIFT_05 / 06、repowiki 末班编组站页、设计稿、`ArtSource/LastShiftYard/README.md`「第三轮」、`SkyIsland/AGENTS.md` §6.1 第 7 / 10 条、`docs/ai-docs-migration.md`。守卫 `LastShiftReadabilityPropertyTest` 加第三轮段（源码 + 组装 / 构建报告 `decals`），反向 9 处破坏全部转红并按字节还原。
- 验证：全量守卫 763 PASS；回归 LastShift 2 PASS；Blender 组装 `LASTSHIFT_BLEND_OK`；Unity 批处理 `[LastShiftBuilder] PASS`（日志里那一行 `Build Finished, Result: Failure.` 与以往成功的编组站 / 潮闸镇场景包构建相同）、判包 PASS；UnityPy 读包：4 个贴花材质队列 3000 / 混合 5、10 / ZWrite 0，其余 131 个默认队列，磨损地皮顶点 alpha 字节 0…163，「废弃短线」按块 0–3 层；Wiki 站点构建通过；正式编译 `Build succeeded!` 并部署，D 盘游戏目录 DLL `91455a14…`、`lastshift_raid` `b1b6d708…86c` 与仓库产物逐字节一致。
- 未做 / 未验证：没开游戏（L3 待 owner）——贴花在官方延迟管线的透明前向 pass 里收不收阴影、真点光 2.4 的亮度在实机里是否合适、押运长换台时走的路线都要看实机；签收在官方 LootView 里 8–9 件的显示；BossRushMod、作者工程、私有资源均未提交。

## 2026-11-12 末班编组站实机八条：闪烁、屋顶、室内灯、围圈小队、押运长、卡脚、司机室楼面、交互点（COMPAT；L1/L2，L3 待 owner）

owner 实机（附 Player.log）：①很多地方一直在闪；②进屋 / 头顶有屋顶时没隐藏；③屋里不亮；④敌人生态要有趣，比如几个小队围成一圈不动、发现玩家才恢复 AI；⑤Boss 特效塑料、机制简单，血可减 1/3；⑥很多连接处卡脚、翻滚才能过；⑦悬空的司机室进去没实体、一进去就掉；⑧交互点不在物体中心，NPC 对话在头顶。

- ①根因是共面重叠：离线在 .blend 里查出 1982 对、2815 m² 同向投影重叠（间距 < 1 cm，435 对完全为 0）。`tools/build_lastshift_blend.py` 新增 `layer_coplanar`：不动几何，给上层排层号写进顶点色 alpha / 实例表 `layers`；作者工程着色器 `LastShiftLayerBias` 按层推深度（GBuffer / Forward / DepthOnly），实例合批把层号烘进 alpha。本次 0–6 层、同层重叠 0。
- ②除三栋楼外，作者工程 `BuildCanopyRoofs` 把西台雨棚、煤场 / 洗手 / 验货雨棚、南班房后檐、旧候车台、北线廊桥、悬着的 017 号机车切成屋顶组并放触发区（往北多放镜头被挡的一截）；运行时 `RaidIndoorRoofs` 不改逻辑，只加只读 `TryGetInside`。
- ③`LastShiftLighting`：人在司机室 / 维修棚 / 押运库里时排 2×2 盏不随昼夜的顶灯。
- ④⑤由子代理做：`LastShiftYard/LastShiftSquads.cs`（五处营地每趟开三处、同时一伙，AI 子物体停用 + 官方 Sleeping，看见 / 听见 / 受伤 / 同伴倒下 / 警铃整队醒，轨道车铃能调开）；`LastShiftEscortChief.cs` 重写 + `LastShiftChiefFx.cs`（1000 血；封线带、响墩两招轮换，半血两条封线 / 三颗响墩、预警缩短；纯色线换成流动斜纹带、弹着点、曳光、红旗、红灯；三个程序化哨音 `tools/gen_lastshift_sfx.py`）。新属性测试 `LastShiftSquadPlacementPropertyTest`、F3 演练加冻结站位与朝向判据、夹具 `LastShiftDrillJudges` 同步；Wiki 中英与站点副本、设计稿已改。
- ⑥日志里 4 条 `[RaidStuck]` 全是焚皇近战连段时的误报（`RaidStuckProbe` 现在动作进行中不记）。真正的卡点靠新加的作者工程离线走格 `LastShiftWalkSweep` 找到：地面碰撞是格心顶点连成的高度场，梯段 / 坡道侧面、坑沿成了伸进旁边一格的约 77° 隐形陡坡（37 处），司机室南沿两侧还留着 5.2 m 实心柱。构建器改成每格一块平面、落差立墙；走格从 877 次失败 / 37 处地面卡点降到 738 次（全是墙、栏杆、家具、站界）/ 0 处。
- ⑦构建器把桥面与司机室楼面并成一张碰撞、按第一个三角形整张翻绕序，司机室楼面因此朝下、从上面踩不住。改为逐三角形朝上，坐标探针断言四处楼面 / 桥面先打到架空层。
- ⑧`LastShiftWorld.InteractOffset` / `CrateMarkerOffset`（按 Blender 量的物体高度）替换各处一律 +1 m，官方箱子的 `interactMarkerOffset` 一并改；余响对话点从头顶（1 m，偏 0.3 m）挪到她胸口 0.62 m；剪票交互从台前半空挪到台上的打孔钳。
- 规则与文档：`SkyIsland/AGENTS.md` §6.1 加第 7–10 条；新地图流程 README / 03 §5.2 / 04 §5.2；`docs/ai-docs-migration.md`；`ArtSource/LastShiftYard/README.md`；repowiki 末班编组站页。守卫 `LastShiftReadabilityPropertyTest` 加第二轮段（源码 + 组装 / 构建 / 走格报告），反向 9 处破坏全部转红并按字节还原；F3 演练室内判据收紧为 11 个触发区全部绑定。
- 验证：全量守卫 763 PASS；回归 LastShift 2 / Tidegate 12 / SkyIsland 17 全 PASS；Unity 批处理构建 PASS、判包 PASS；正式编译 `Build succeeded!` 并部署，D 盘游戏目录 DLL `4B794FB1…E37F`、`lastshift_raid` `d1516912…6816`、三个新哨音按 SHA-256 一致，正式 DLL 无 Dev 标识。旧包备份在会话 scratchpad。
- 未做 / 未验证：没开游戏（L3 待 owner，清单见同日回复）；跨车厢的模型没做共面检查（模板在 Blender 里叠在原点，场上不会同时重叠）；导航仍单层，AI 不钻桥下 / 司机室下；BossRushMod、作者工程、私有资源均未提交。

## 2026-11-12 三张自研图读图优化：边界实物、逐件碰撞、半掩体、天空岛掩体与错峰（COMPAT；L1/L2，L3 待 owner）

玩家反馈「地图边界不明；地面无用装饰太多，分不清哪些有实体能挡子弹；天空岛太空旷、Boss 扎堆、没有掩体拉扯；潮闸镇特别容易被莫名其妙的东西卡脚」。owner 追加：边缘空气墙处放符合环境的实物；火车要过的地方别拦，要符合常理；教训写进 `docs/guides/new-map/`。

- 规则：`SkyIsland/AGENTS.md` §6.1 读图通则（三图共用）；新地图流程 README / 02 / 03 / 04 / 06 同步（`docs/ai-docs-migration.md` 有记）。
- 共用：`Utilities/RaidHalfCover.cs`（0.8–1.4 m 墙层盒挂贴身触发区，主角贴着时子弹越过，复用 ModeF 触发器与 `BossRushProjectileInitPatch`）；Dev 卡脚探针 `DebugAndTools/RaidStuckProbe.cs`（`[RaidStuck] STUCK` / `PARAMS`）；审计 `tools/map_readability_audit.py` 与三个 `*ReadabilityPropertyTest`（反向验证各自转红，登记 `EXTERNAL_ARTIFACT_GUARDS`）。
- 潮闸镇：碰撞改逐件有向盒（建筑墙 / 家具 / 外梯 / 管道 `tools/build_tidegate_building_colliders.mjs`；散布 / 大件 / 路灯 / 搜刮箱 / 阀门按可见网格），0.6 m 以下杂物无实体、战斗区杂件减半；栏杆网格碰撞阈值 0.3 → 0.6 m、外梯实心块；边界按地块放木栅栏 / 钢栅栏 / 隔离墩 / 警示桩，撤离口留空；三个刷怪 / 巡逻点挪出车与集装箱；导航 3456 → 3708 顶点。包 `25c0db1e…`。
- 末班编组站：站界废钢轨栅栏，铁轨出站处断开架限界门；栏杆跟坡分段；跨线桥 / 司机室下落回地坪、架空层另起 `COL_Ground_Overhead` 与桥墩立柱盒；押运长火线东侧两高两矮四块掩体；矮实心体去掉或并柱。包 `03f6fa39…`。
- 天空岛：E 与 S1–S4 头目场各一高一矮 `ArenaCover`（单独成组、登记半掩体）；遭遇组 6.5 m 散开、45 m 错峰、遭遇战 30 m 内不拉新巡守；灌木 / 绿篱压到 0.55 m；岛缘石块改贴地碎石（H / S1 保留，保巡守槽位）；护栏木梁加粗；B3 搜刮箱挪出路缘。发布导航 4073（0.9 档），场景审计 0 开放缺陷，作者碰撞校验 0 失败（顺带放行 `SkyPavilionRooms` 触发盒误报），ECM2 行走模拟 PASS。包 `00765c0c…`。
- 验证：全量守卫 762 PASS；隔离回归 SkyIsland / Tidegate / LastShift 全 PASS（两个夹具补 `RaidHalfCover` 替身，`TidegateDrillJudges` 点位样本随挪点更新）；Windows 正式编译部署，DLL 与三包按 D 盘游戏目录 SHA-256 一致，正式 DLL 无 Dev 标识。
- 未做 / 未验证：没开游戏（L3 待 owner：`M_TIDEGATE_READ`、`M_LASTSHIFT_READ`、`M_SKY_ISLAND_24`，回传 `[RaidStuck]` 行）；末班编组站导航单层，AI 不钻桥下；BossRushMod 与作者工程改动均未提交。

## 2026-11-12 末班编组站并入 main、杰夫引导门槛与流程断点（COMPAT / SCHEMA+；L1/L2，L3 待 owner）

owner 反馈「杰夫也没有开新任务」。查明两层原因：一是游戏目录 11-11 17:01 被移走 `lastshift_raid` / `lastshift_entry`，17:04 换成 main 正式构建（SHA-256 `12DE18F4…460A`，不含本图任何代码），590216 根本不存在；二是 `LastShiftJeffIntro.CanOffer` 要求「没见过余响且没从编组站撤离过」，先自己摸到仓库区的档（含 owner 实测档）永远接不到。对照天空岛序章 590001（可接只看接取 / 交付位 + 包在不在），改为没接没交且两包都在就发；接之前见过余响，目标直接算完成，杰夫换一句「你已经见过她了？」。`docs/contracts.md` 同步。

静态走查全链路（基地告示 → 仓库区剪票处与余响 → 转车 → 站内列车 / 道岔 / 搜刮 / 押运 → 撤离与存档 → 回访）未见卡死或回不了基地的断点，另修四处：缺包时基地告示不再贴（只贴一次，贴了就指向不存在的剪票处）；回访不再写首班教学落地提示；收尾页（单按钮）按 Esc 等同继续，票根交出后不再吞掉上车；`switch` 与 `note1`–`note3` 缺任一即装配失败（已用 UnityPy 核对现包四个标记都在）。`LegacyArenaEntry` 夹具补 `LastShiftItems` 替身（分支带入的红）。

并入：分支 `codex/new-raid-scene`（72a2d7d2…1ac8870c）以 910220d8 合入 main，冲突仅 `compile_official.bat` 与 `tools/check_dll_identifiers.py` 两处追加，两边都保留。合并后的树（`7310f8e2`）与部署所用临时合并树逐一相同。TypeID 500117/500118、存档键 `BossRush_LastShiftYard_Story_v1`、任务 590216 随分支进入 main（登记见 AGENTS §4.3 / §10 与 contracts）。

验证：临时合并树全量守卫 759 中 757 PASS，余 2 项（`BaseBuildingResourcePropertyTest`、`SkyIslandFloraGeometryPropertyTest`）为临时树缺 `output/` 与 `ArtSource/` 源，主工作树单跑均 PASS；全量执行回归 164 中首跑 139 PASS，补入官方源码与潮闸镇导出后 25 项重跑 24 PASS，`FieldAdventureVisuals` 因本机 Unity 无许可证超时（环境，未覆盖）。`LastShiftBundleShaders` 两包 PASS。Windows 正式编译通过（仅既有 Mode H 两条 CS0649），已部署：游戏目录 `BossRush.dll` 与构建产物均为 `83BBEF1148AFCE3466805819A9C2AF720E472223DB32BE5804A8B40083AC8E19`，无 Dev 专用标识；`lastshift_raid` `290CA682…`、`lastshift_entry` `92E56E98…`、含两枚新图标的 `production_icons` `B6AEFFE1…`、带余响的 `DuckNpcs.json` 逐一与构建一致，资源 81 包 SHA-256 校验通过，`info.ini` 仍 2.4.0 未改。

L3 待 owner：①基地找杰夫，可接页有「末班车还在剪票」，已见过余响的档接下即可交；②仓库区西场第二座龙门吊下有剪票台与余响，M 图有她的标记；③进站后 M 图上余响台子、三处线索图钉与场景位置一致（本图没有潮闸镇那样的地图校准与撤离点标记，整体平移即不合格）；④带票根剪票、收尾页按 Esc 仍上车；⑤第二趟起落地横幅不再出现「站到南护栏后面」。剪票处台灯是否起作用需在仓库区目视。未启动游戏、未读写存档。

## 2026-11-08 v2.4.0 发布准备：潮闸镇敌人整体加强、版本号与 Wiki（COMPAT；L1/L2，手感 L3 待验）

owner 要求发布前「提升敌人整体强度」并升版本、整理 WikiContent。新增 `Tidegate/TidegateCombatTuning.cs`（已登记编译清单）：刷怪在岗位 / 身份调好后、官方创建前改克隆——生命 ×1.5（守卫、突击队长 ×1.8），枪械与近战伤害 ×1.25，反应与开火前摇 ÷1.3，散布 ÷1.2；狙击手只加血，沙老大跳过，`aiCombatFactor` 不动。沙老大 1000→1500 血、伤害 1→1.25、前摇 0.4→0.3、两招 38/46→44/52（预警不变）；投掷手手雷上限 25→32。不加人、不换武器 / 掉落，经济不变。理由：好玩（镇上普通拾荒者太软）、可证（倍率全在一个文件、夹具执行）、改动小可回退（改常量即可）。

版本 2.3.9→2.4.0（`info.ini` 与描述加潮闸镇）；WikiContent 新增 `changelog__v2_4_0` 中英、catalog 插行、总览补 2.4.x；`map__tidegate` 中英按第四至六轮实况更新（31 人与分区上限、11 种岗位名、入室切顶、水里不能翻滚、水下约八成一件 Q5–6 大奖、敌人强度）。工坊 `a.md` / `b.md` 换 v2.4.0 + v2.3.9 并加潮闸镇段，预算 7500 / 7641（上限 7700）。repowiki 潮闸镇专题同步。

验证：`TidegateDrillJudges` 新增「倍率只写克隆」执行断言，`TidegateSandBoss` / `TidegateGunner` 夹具改断言新值；`TidegateRaidGuard` 守调用顺序与例外。反向：注释掉 Apply 后夹具与守卫均转红、删去 Boss 例外守卫转红，按字节还原。全量守卫 757 PASS；全量执行回归首跑 147 PASS / 15 FAIL——13 项为游戏目录中途消失导致缺官方 / Harmony DLL，设隔离路径后复跑 PASS；`F3ValidationExecution` 是 11-06 起的既有红（夹具桩缺 `CaptureEnemyStart`），补桩后 PASS；`EclipseBloomParticles`、`FieldAdventureVisuals` 因本机 Unity 无许可证无法执行（环境，未覆盖）。Wiki 构建通过，仅相关 6 页变化。Windows 正式编译通过（仅既有 Mode H 两条 CS0649），因 `D:\software\steam\...\Escape from Duckov` 在 21:26 后缺少 `Managed` 与工坊 Harmony（游戏疑被卸载或迁移），本次用 `Build/Release240/` 的隔离拷贝编译，**未部署到游戏目录**；`Build/BossRush.dll` SHA-256 `290B6EFD3C252DF3A1B2C22583F11BA4371ACA5B531A0D714E5E208B887E643B`，Dev 专用标识不在。同晚游戏装回后（`Managed` 与隔离拷贝的 `TeamSoda.Duckov.Core.dll`、工坊 Harmony 逐字节一致）按真实路径重跑正式编译并部署：游戏目录 `BossRush.dll` 与 `Build/` 均为 `61B6EC2D97B160E5ED7FD61CA33DC2D07B7B158C956DE423F39048967D84F6F9`，79 个资源包 SHA-256 校验通过，`info.ini`（2.4.0）已拷入 `Mods\BossRush\` 并核对哈希（旧件备份在 `Build/Release240/pre-deploy/`），WikiContent 抽查一致。

L3 待 owner：进潮闸镇看普通拾荒者与守卫的耐打和还手速度是否合适、沙老大击杀时间（M_TIDEGATE_12）、投掷手手雷伤害；不合格条件：普通敌人两三枪内无法击倒 / 刚露头就被秒、或沙老大打得过久。未启动游戏、未读写存档。

## 2026-11-08 潮闸镇敌人捏脸与演练误报（COMPAT；L1/L2，新外观 L3 待验）

对应 CR-2026-11-08-001–004：Scav 完成创建/配装后应用 11 套官方实例捏脸，区分嘴型、羽色、头型与体型；F3 读取当前实例。敌人验收改用真实 31 人纯判据并与 512 种子阵容回归共用；深水卷走仅选普通帮众。水下严格按 Quality ≥ 5、固定怀表豁免，修正 Q4/DQ6 鸭蹼误报；概率与颜色不改，夹具执行真实两层克隆注册链。

验证：全量守卫首跑 756 PASS + 1 台账超限，旧已完成节逐字归档后该项通过，最终 7 项相关守卫通过；12 组潮闸镇回归通过（首轮 DrillJudges 替身命名空间缺失，修正后单组补验通过）。外观 5、F3 3、水下 4 个执行/落盘反向探针均预期转红并按字节恢复。Windows Dev/正式编译成功，23 个 Dev 标识分离，正式部署 DLL SHA-256 `D2C04938CFEAD8B1B2AD92FE6ACB8B4279C71DFA46E234F0723588303FC1652E`，79 包核对一致。覆盖包 `Build/TidegateRound6/`，完整记录 `docs/reports/testing/潮闸镇_2026-11-08_敌人捏脸与演练误报修复.md`。

owner 新 L3 日志 `20261107_170000_978` 已确认上一版码头/镇区 P95 38.22/39.29 ms 通过，游泳禁翻滚通过；整套峰值 154.94 ms，不能扩展为全程无卡顿。此次未启动游戏或读写存档，新捏脸的装备遮挡、穿模和帧耗待实机。

## 2026-11-07 潮闸镇室内切顶与水下品质概率（COMPAT；L1/L2，L3 待复测）

对应复开的 `CR-2026-11-07-007` 与新增 `012`。owner 新图证实上一轮只隐藏瓦面仍留下高层外墙与太阳能设备。本轮按官方实际 Fade 分组包含上层外墙/设备的事实，重新划分全部 23 栋建筑：隐藏上部外壳及附着物，结构保留到本图设计的 0.9 m，地板与完整家具按语义保留；入室限定本栋，室外与邻屋完整。作者民居、五金店预览已目检；实际包逐面、面积/材质及反向验证通过，10 组栏杆碰撞逐值保持。

水下以 owner 明确的 Quality ≥ 5 为高档阈值；每趟 80% 分配一件 Q5–6 随机奖，其余水下随机物资最高 Q4，船长柜、金库与滩涂货单共用预算。固定怀表按 owner 确认继续保留并豁免统计。10,000 个生产抽签种子命中 80.05%；2,048 趟实际建箱命中 80.18%，每趟最多一件，基础标价 −53.75%、格数不变。陆地无货单 256 种子及含货单 64 种子逐件指纹与旧生产一致；货单在旧路线抽签结束后独立调整水下货，避免共享随机流带动陆地变化。

全量守卫 757 PASS，潮闸镇隔离回归 12 PASS，Windows Dev/正式真编译通过，正式版已部署并核对 DLL/地图哈希。覆盖包和预览在 `Build/TidegateRound5/`；完整证据见 `docs/reports/testing/潮闸镇_2026-11-07_室内切顶与水下红品概率.md`。本轮未启动游戏、未读写存档；建筑 renderer 增加 41 个，新版帧时间与最终视野仍待 L3，不关闭原性能待验证项。

## 2026-11-07 潮闸镇建筑、通行与第三份实机反馈（COMPAT / WIRE+；L1/L2，新版 L3 待复测）

对应 `CR-2026-11-07-007` 至 `011`。屋顶改为脚点进入本栋才调用官方 `ShadowOnly`，资源端分离真实屋顶；取消室外视线裁切与墙体虚化。桥头增加实际可见引坡和连续碰撞面，地形在甲板下让位，实际栏杆网格生成实体碰撞。游泳翻滚在官方动作入口拒绝，翻滚入水时终止并清冲量。

人形全部优先用官方 Scav；11 类岗位外观与双语名，猎犬保留普通狼。实际常驻 27→31（+14.8%），海堤增援另计。官方目录 256 种子×78箱：基础标价均值 +5.91%，普通街区 +5.74%；埋货与沉船单点抽样分别 +8.77% / +8.90%。容量与品质上下限不动。

最新报告 `20261107_024811_443` 为 16 PASS、2 FAIL、2 WARN，码头/镇区 P95 105.75/134.91 ms。消融中远处角色停用收益明显，新增 55/65 m 滞回、0.35 秒节拍的整角色挂起；返回保留同一血量与装备，死亡不重刷。战斗/调查、淘沙帮、海堤增援与 Boss 保持运行；灯塔先唤醒再发声。性能窗口补充挂起数；新版 P95 尚未取得，不宣称已达标。

首轮全量守卫 757 PASS、最终相关守卫 109 PASS、潮闸镇执行回归 12 PASS；隔离反向验证覆盖屋顶范围、专名/装备/密度、滞回/旧路径/死亡/游泳门。Windows Dev/正式编译、实际包屋顶逐面与着色器验证通过；四路线 574 个通行采样及 10 组栏杆双侧物理射线通过。正式版已部署并核对 DLL 和地图 SHA-256；`Build/TidegateRound4/` 留有 Dev/正式覆盖包。最终资源与部署记录见 `docs/reports/testing/潮闸镇_2026-11-07_建筑桥梁敌人与性能修复.md`。本次不改 TypeID 或存档 schema，未启动游戏或读写玩家存档。

## 2026-11-07 潮闸镇第二份实机报告修复（COMPAT；L1/L2，L3 待复测）

对应 `CODE_REVIEW_FINDINGS.md` 的 `CR-2026-11-07-001` 至 `006`。实机报告 `BossRushValidation_20261106_154008_887`（Dev MVID `c7f3c516`，即游戏目录 17:38 的 Dev 构建，潮闸镇代码与 `b64628aa` 相同）：12 PASS、3 FAIL、2 WARN。存档键、schema、TypeID 均未变。

- **001 演练误报**：领班柜拿钥匙前故意关着，四相判据只按淹没判断。改为纯判据 `TidegateDrillLootStateOk`（淹没与水深一致、没淹也没锁才可交互、埋货模型随淹没显隐），埋货按 0.12 m 泥面口径。新增 `TIDE_DRILL_SHIFT_KEY_LEAD`：到场上锁 → 走生产取钥匙一步 → 开锁、金圈、目标行 → 还原重锁；响铊测深只指未搜埋货。
- **002 卷走观测不到**：官方寻路会把人拉回可走面，演练瞬移一次留不住（`left_deep=True`、`deep_s=0`）。改为 Dev 钩子在生产 `TickTide` 查水深前把一名淘沙帮按在深水点上，由生产规则自己卷走；用例 finally 与还原都释放。
- **003 开闸增援被白白卷走**：开闸后泻湖跟着外海涨落，增援等泻湖涨过 −0.9 m 才撤，金库地面先淹（实机 `TidegateExtraSpawn_28`）。增援去金库改为同时要求淘沙帮的「下滩」窗口（涨潮前 20 秒撤）。卷走规则本身不变：增援也是淘沙帮。
- **004 箱子又公式化**：同一趟 7 只渔篓开头全是褐沙丁鱼，10 只工具柜 6 只是螺栓加螺母（写死的两套班次单）。改为工具柜 / 渔篓 / 药柜头两格按官方标签从同类别同品质带的小池加权不放回发牌，进图按标记名排序一次排好；渔篓只放 1–2 档小鱼。`TidegateTreasure` 256 种子全图：同类箱开头重复 2290/4841 → 0、对题率 84% → 100%、渔篓开头 42 种杂物 → 9 种小鱼都会出现；普通区箱均总标价 68,218 → 71,702（+5.1%），全图 367,565 → 378,893（+3.1%），单件价值上限未改。
- **005 精品撞车**：所长柜与船长柜同趟开出同一件 12009。同一趟的精品（含惊喜格与沙老大私货）去重，最多重抽 6 次；两柜共享 6 档以上物品的局 18/256 → 2/256（剩下的是金库随机格偶合）。
- **006 响铊重复与沙老大辨识**：带着响铊再打倒沙老大，尸体换成一件品质 5–6 私货（`TidegateLoot.RollBossSpare`，目录缺货仍给响铊）；模型放大 1.2 倍与同底模的风猎看守区分；血量 1000 取天空岛头目保底，理由写进代码（离线拿不到官方武器伤害，击杀用时待 `M_TIDEGATE_12` 实测）。轮班钥匙拿到后 HUD 目标行与 M 图金圈指向领班柜，开柜后撤掉。
- **性能（未修）**：码头 / 老镇 P95 58.90 / 61.50 ms，基线 29.56 ms（场上 12 名）→ 窗口时 26–27 名、批次 567 → 约 900；显卡约 7 ms、等待显示 0、潮闸镇 owner 合计 < 0.2 ms、官方 AI Update 加行为树 < 1 ms；渲染线程 / 物理 / 脚本更新 / GC 计数在正式游戏进程中不可用。新增 `TIDE_PERF_ABLATION`：老镇同一站位依次关远处敌人、敌人 AI、敌人模型、全部敌人、建筑属性块、阴影、装饰、建筑，各 5 秒报 P95 与降幅，首尾全开看漂移，测完即恢复。拿到这份数据前不改人数、不削特效。
- **验证**：反向探针 5 处（钥匙金圈、成套发牌、精品去重、私货替换、锁柜判据）均转红并按字节还原；用旧版 `TidegateLoot` 跑新断言转红。相关守卫与 Windows Dev / 正式真编译通过（各两条既有 Mode H CS0649），正式 DLL 已部署并核对 SHA-256，23 个 Dev 标识正式版全无、Dev 版齐全。Dev 复测 DLL 在 `Build/TidegateRound3/`。未启动游戏、未读写存档。
- **另**：生成全彩赛璐璐信纸样张 `ArtSource/Tidegate/journal/samples/`（与现行三色版画风并排），未替换游戏内插画，待 owner 选。

## 2026-11-06 死亡广播逐订阅者隔离 + 丧尸 Boss 死亡兜底（COMPAT；L1/L2，L3 待验）

玩家反馈「末日丧尸第 10 波 Boss 不见了，卡关」→ owner「确保以后所有模式都不会出现类似问题，并且不会有性能问题」。根因（读码确认，玩家那局的具体抛异常者 UNVERIFIED，需 `Player.log`）：官方 `Health.Hurt` 致死时依次发 `OnDeadEvent` → `Health.OnDead` → `SetActive(false)` → `OnHurtEvent` → `Health.OnHurt`，全程无 try；前面的订阅者抛异常（击杀提示类 Mod 不判空攻击者，流血 / 毒区必抛）就截断后面的所有订阅者，而 `DestroyOnDelay` 已启动，尸体照常消失。丧尸 Boss 波只靠死亡回调扣 `CurrentWaveBossesRemaining`，于是永远不结束；10-16 只给宿命回响和标准竞技场补过轮询兜底。

- **全模式根治**：新增 `Patches/Combat/DeathBroadcastIsolationPatch.cs`，两个独立 transpiler 把 `Health.Hurt` 的 `OnDeadEvent` / `Health.OnDead` / `Health.OnHurt` 与 `CharacterMainControl.OnDead` 的 `BeforeCharacterSpawnLootOnDead` 原地换成逐订阅者 try/catch 的等价调用，顺序与参数不变，异常 5 秒节流记 `Debug.LogWarning`（带抛异常的程序集与方法名）。各模式订阅写法不用改，官方、第三方与本 Mod 订阅者一并受益，以后新写的死亡订阅自动受保护。不改伤害、准入与返回值；`OnHurtEvent` 每击都发，不做反射隔离。
- **性能**：静态多播按委托引用缓存调用列表，稳态零分配；`OnDeadEvent` 只在死亡时走一次反射（`PrepareInvoke` + 逐监听器 `Invoke(object[])`），每次死亡几个小对象；丧尸兜底 0.25 秒扫本波几只 Boss，战斗外 O(1) 早返。未实机采样，上述为静态预期。
- **丧尸 Boss 兜底**：`ZombieModeRuntimeModule_BossSilentDeathSweep.cs`，已死未结案的 Boss 走同一条死亡路由（`DeathSettled` 去重），已销毁或登记丢失的按本波扣除、注销、不发奖励箱，扣完推进本波结算。补丁因官方 IL 变化没装上时也能兜住。
- **守卫与回归**：新守卫 `DeathBroadcastIsolationGuard`（三个探针反向验证转红、按字节还原）；`ModeGSpawnTransactionGuard` 按精确路径 / 类名登记新 Hurt 补丁；新执行回归 `DeathBroadcastIsolation`（官方方法体 + 真实 Harmony，复现截断后验证隔离；现装 DLL 上 IL 各命中一次、真实 `UnityEvent` 逐监听器送达）；`ZombieModeWaveRuntime` 加第 10 波双 Boss 截断 / 销毁场景（83 场景 293 断言）。
- **验证**：全量守卫 755/755；Windows 正式编译 0 错误、2 条既有 CS0649（隔离 worktree，`GAME_PATH` 指 Managed 拷贝，游戏目录 DLL 哈希不变）；全量执行回归 152/152（首轮 150/2，两条红是未设 `GAME_PATH` / `BOSSRUSH_HARMONY_DLL` 的环境缺失，设好后 `AuditCombatSeptember`、`SkyIslandCombatRuntime` 单跑通过）。
- **L3 待验**：ZB-A 末日丧尸打到第 10 波，让 Boss 流血 / 毒死（装着击杀提示类 Mod 更好），Boss 倒下后 1 秒内本波不结算即不合格；`Player.log` 搜「订阅者抛异常，已隔离」核对抛异常者。WG-A 游戏加载日志出现「Health.Hurt 广播隔离只命中部分调用点」或「掉落广播隔离未命中」即补丁失配。

## 2026-10-21 丧尸模式专项审查与修复（COMPAT，L1/L2，未部署）

本轮修复五项新发现与一项既有问题：CR-2026-10-21-004 撤离工厂接受结算回执后才返程；005 现金到账先提交点数、异常时核对官方余额，通知故障不重复发钱；006 开波回收已销毁星点登记，即使实体掉落列表为空也执行；007 三种预警自然过期补结算，避免低帧率漏触发；008 骚扰弹按帧轨迹检测并停在原目标点；CR-2026-10-20-001 三叉/散射移除二次削伤，保留选卡代价。Mode F 未提供新回执时沿用既有工厂行为，新增字段为可选、无存档格式变化。

验证：全量守卫最终 747/747 通过；相关生产执行回归 15/15 通过，覆盖 ZombieMode 八项、EnemyRecoveryRuntime、IntegrationThirdReviewFixes、SpawnPositionPolicy、EnemySpawnRuntime、ModeEFEnemySpawnRuntime、AuditModeLifecycle、RuntimeOwnership。新增或修改守卫与关键行为的隔离反向验证命中预期失败，按字节恢复后通过；报告分别在 `Build/zombie-extraction-negative-probes.json`、`Build/runtime-regressions/ZombieModeRewardRuntime/spread-negative-fz8x8w66/`、`Build/zombie-audit-20261021/star-cleanup-probes/`，预警/弹体修复前失败日志在 `Build/zombie-threat-review-20261021/`。

Windows 正式构建退出码 0，无 Dev，2 条既有 ModeH CS0649 警告；使用游戏 Managed 实体拷贝，编译期间 1,220 项源码/脚本哈希未变，真实游戏 DLL 哈希未变。独立保存产物 `Build/zombie-audit-20261021/BossRush.dll`，SHA-256 `210DC19AEBAE4CCD10431F7365C56E9F209106E086461A883F4D65ED0B2C70BA`；构建日志、源摘要、结果与隔离回归汇总均在同目录。未部署、未启动游戏或访问玩家存档。原第 13 / 23 波 Collider NaN 的首写来源仍未证实，本轮修复不得代称该根因。

另纠正中英 Wiki 与开局注释的旧“2,000 发”承诺（SAFE）：当前游戏说明、实际发放和 2026-10-17 回归均为普通弹药一组、重弹药至少 4 发，未修改数值。相关 Wiki 守卫、数量执行回归与站点构建通过。台账容量守卫发现超限后，将 9 月 27 日两项已闭环记录按原字节归档，证据在 `Build/zombie-audit-20261021/ledger-archive-proof.json`。

审查剩余项（Open，P2 / COMPAT，L1）：CR-2026-10-21-009 即时退款与欠账持久化同时失败后，HostLifecycle Reset 丢失暂扣事务；CR-2026-10-21-010 欠账读取异常被当作 0，随后成功写入可覆盖旧债。均有明确故障分支证据，无正常玩家 L3 复现。前者需单独设计存档身份、换槽/删档/销毁与重试 owner，后者需 TryRead 区分缺键与读错并补故障执行回归；本轮未扩大欠账协议和生命周期。具体路径与触发条件见 CODE_REVIEW_FINDINGS 同号记录。

L3 验收：Z1，单弹枪选三叉/散射后对同种敌人比较全中伤害，仍应体现散射的 5% 枪伤代价，但不能额外只剩原来五分之一；切枪/退局后属性恢复。Z2，Boss/特殊怪预警结束须结算一次，暂停不偷跑、来源死亡/换局不补击；骚扰弹穿过玩家要命中，玩家侧移及目标点后方不误伤。Z3，正常完成多波并撤离，净化点兑现金只发生一次、背包掉落保留、可再次入场；付款不可用须留场保留点数并能重试。Z4，分别覆盖 12→13→14 和 22→23→24 波，身体/头部命中应持续扣血、圈外满进度能结算。未采样帧耗，记录数量回归不等于实机无性能问题。

## 2026-09-30 天空岛第二轮：Mod 武器进池、去浮空字、NPC 单气泡、航路任务点了没反应（COMPAT，L1/L2）

- 武器池（owner 定）：官方与其它 Mod 的武器都进池、口径不再过滤；只排除本 Mod 自己的 500001–500999（主动能力绑主角、且是 Boss 奖励 / 传说级掉落）。枪仍要求有同口径弹药（没弹药 AI 打不响）。`TypeIdLedgerGuard` 豁免 500999 号段上界字面量。
- 搜刮箱与采集点头顶的浮空字整块删掉（连同距离门控与语言重写）；四个守卫同步改为「不得再挂世界字」，真实加回 TextMeshPro 字段四个都转红、按字节还原。
- NPC 一堆气泡：官方只在交互组宿主 Awake 里关组员气泡，Mod 运行时后加的组员（剧情「聊聊航路」、航路任务、各 NPC 送礼 / 服务选项）各挂一个气泡，还按各自偏移散在身上（任务给予者写死 0.1 米，「!」挂在腰上）。`NPCInteractionGroupHelper.AddSubInteractable` 现在 setup 前继承宿主偏移、setup 后 `MarkerActive = false`，全 Mod 的交互组都只剩宿主一个气泡；任务「!」在宿主气泡正上方 0.5 米。
- 航路任务点了没反应：运行时 `AddComponent<QuestGiver>` 拿到字段初值 `finishWhenTimeOut = true`、读条 0 秒，任务页开出来的下一帧交互「读条完成」自动结束，官方 `OnInteractStop` 把页关掉。setup 写 `finishWhenTimeOut = false`（官方预制体的序列化值）。新守卫 `NPCGroupMarkerAndQuestGiverGuard`（4 条内存反向探针）。
- 搜刮箱外观（owner 问要不要换）：现在用的是官方 `LootBoxPrefab`（敌人死亡掉的包），全局预制体里没有别的容器；方案待拍板，未动。
- 验证：守卫 changed-only 除缺 shapely 外全绿；天空岛 16 个执行回归全过；正式编译并部署，`BossRush.dll` SHA-256 `422984C2…` 与 `Build/` 一致（非 Dev；同时含另一会话正在改的 `SkyIslandWorldStory.cs`）。未实机。

## 2026-09-30 天空岛普通巡守密度与固定外形（COMPAT / SCHEMA+ / OPERATIONAL，L1/L2）

新增独立巡守 owner 与 112 个固定点，A6/B8、C10、D/E/F各12、G/H各14、四小岛各6；十二套固定外形、八档由弱到强。原 22 个 Boss/剧情遭遇组不改，普通 active+pending上限24；近远恢复同 Actor/HP/装备，死亡本趟不重刷，晚到创建回收。724个不同守卫、Windows正式编译、16组相关执行回归及Wiki构建通过；调度器346断言与7变异、点位18反例、3修改守卫反向验证通过。实际DLL与配置已部署并核SHA，源码/完整观察步骤见 [密度交付报告](docs/reports/sky-island/2026-09-30-天空岛普通敌人密度与固定外形.md)。没有运行游戏或读写玩家存档，DENS-01～05待L3；此前生产审查问题仍未闭环。

## 2026-09-30 天空岛整体生产审查（未闭环，SAFE 审查记录）

候选 `a8d4c694` / 作者 `8bda2f38` 当前不能整体生产验收通过。CR-2026-09-30-002～012 为 3 项 P1、8 项 P2：主菜单/死亡/撤离的资产结算边界，两居民配偶跟随，婚后衰减，加载异常恢复，长帧漏采，警示恢复，总状态漏红、Shader 默认路径和音频异常停止。完整发现、资源核对、两个生产逻辑 L2 反例及 PROD-SKY-01～09 清单见 [审查报告](docs/reports/sky-island/2026-09-30-天空岛整体生产标准审查.md)。本轮只审查，未改生产代码或运行游戏；L3 待验，旧 F3 PASS 不作为当前候选通过证据。

## 2026-09-29 玩家文案去人机味：全部修复（COMPAT）

**依据**：审查报告 `docs/reports/reviews/2026-09-29-玩家文案人机味审查.md`（local-only），分四片：M 杰夫 / 征程、A 天空岛与模式、B NPC 与基地系统、C 物品与本地化。owner 要求「全部修复，待拍板按主会话建议」。

**拍板**：
- 英文模式名以英文百科页标题为准：From Scratch / Faction War / Blood Hunt / Fate Echo / Black Market Duck Cup / Zombie Mode。物品显示名不改。
- 焚天龙皇叙述用 Skyburner Dragon Lord，套装与物品名不改。
- 羽织统一为「从 J-Lab 出来的护士」。
- 「爷的营旗」描述点出独狼。
- 婚恋 / 戒指台词按 NPC 分派。
- 阿稳用新行的冷面账房口吻，去掉「小子 / 稳叔 / 爷」。
- 无声钟守只写字、只敲钟。

**杰夫 / 征程**：
- 14 条引导按征程短句口吻重写，去掉「官方展示架 / 存档 / 属性 / 词缀槽 / BossRush 船票」等系统词；物品改用描述里的叫法，精确名留在目标行。
- 交付台词「下一章」改「下一行」。
- 失败提示按原因拆分支：不在基地 / 没试过 / 另有契约 / 账房在誊册子。
- 共享投影层提示去掉公文腔；奖品落点统一叫马蜂自提点 / Package Pickup。

**天空岛与模式**：
- 苇白、浮舟、已婚在家版去掉 UI 名与「接交 / 装置」。
- 钟守全部改为木牌上的字。
- 17 件 Boss 装备与 7 面营旗逐件重写。
- 罗盘、风晶、信件与手记的收束句改写。
- Mode H 恢复面板与 Mode G 契约去掉工程词。
- 划地为营的气泡改用阵营显示名。

**NPC 与基地系统**：
- 羽织礼物气泡与叮当分开。
- 新增 `NPCDialogueSystem.GetPersonaLine`，11 个婚恋 / 戒指 / 兜底 eventKey。叮当专属台词放在新 partial `GoblinAffinityConfig_Persona.cs`，已登记编译清单；主文件因此守住 1200 行预算。
- 叮当复读钩子换成日常小事。
- 羽织台词里的系统词改掉，3 级故事同步。
- 成就名与说明、许愿台、日报、PetNest 技术词都已改。

**物品与本地化**：
- 阿稳 146 条随机台词中换掉 52 条，提示类降到约 1/3。下标原位替换，本地化 key 不变。
- 寄存、扫箱、告别三类口吻统一。
- 叮当改造界面改回第三人称。
- 后山收成 / 种子、工事包、钻石、蛋糕、新武器、腾云驾雾补上物件描述。
- 描述事实错误已修：
  - 词缀熔石按未锁槽数计价。
  - 龙息补上灼烧机制。
  - 掩体生命值 250，同步改 ID 表。
- 尸潮邀请函 / 信标删掉重复注入，只留一个事实来源。
- 龙裔气泡改走 `L10n.T`，由单一出口提供。
- 变异词条 / 词缀英文重名已拆开。

**Wiki**：`WikiContent` 中英同步，`catalog.tsv` 只改腾云驾雾的英文标题，id 不变。已跑 `npm --prefix wiki-site run build`。

**测试同步**（断言的就是被改的文案）：
- `SkyIslandAutotest.json` 五处。
- `ZombieModeFortificationUsageGuard`、`ZombieModeRealTemporaryNpcUiCurrencyGuard` 各一处。
- `SkyIslandMarriage` 替身补了新接口。
- `NpcAuditFixes` 清单加了新 partial。

**验证**：
- 语法探针 PASS。
- 守卫全量 717 / 717。
- 执行回归全量 109 / 109。
- Windows 正式构建 0 错误，部署 SHA-256 `E6047E26DCFC75350E1CD2970AB430419A6052B3AFB32282B939974B68733A02` 与 Build 一致。
- 证据止于 L2，未实机，未提交。

**实机要看的**：
- 长描述在物品面板里是否被 TMP 裁掉。超过原文 120% 的有：天空岛装备英文多件（约 1.3–1.4 倍）、风标罗盘、后山三件收成、工事包、尸潮邀请函 / 信标、龙息、腾云驾雾、钻石、蛋糕、叮当重铸说明。
- 钟守「去敲归航钟吧」由 1 屏变 2 屏。
## 2026-09-29 Mode H 实机反馈九项 + 十四项复核（COMPAT / SCHEMA+）

**Mode H 运行时**：
- 第三方 `BattlefieldTypeKillNotice` 在 `Health.OnDead` 里读空 `fromCharacter`，吞掉后续死亡回调，造成「剩一个敌人」和 update 阶段 NRE、观战镜头丢失。改动：
  - 危险边伤害隔离（`ModeHMatchRules.HurtAtEdge`）；
  - update 失败改为静默重打本场（`HandleUpdateFailure`）；
  - 每帧剔除死 / 销毁 / 失活敌人（`ModeHCombatTelemetry`）；
  - 全灭先于超时判定。
- 兼容外部 Mod 枪（owner 要求）：预选配装按模板弹匣判据（有容器且容量 > 0）把关；实例弹匣仍不可用时，冻结弹量全放背包，不再判 `kit_apply_magazine_missing` 重开。
- 技术故障不弹「恢复」页，恢复页只在 Dev 保留且可关。
- 删掉拍铃「持有者不在场上」门，AI 超出视距 80% 时向目标靠近。
- 押钱结算挪到战报落盘之后，防止同一场重结。
- `startIndex` 刷屏来自官方 `CustomData.GetInt/GetBool` 的空数据，非本 Mod 字符串。

**Mode H 界面**：
- 删除选人 / 赔率 / 押注页的说明与警告文字。
- 配装页合为左右两列、列出全部装备；穿戴偏好存新键 `BossRush_ModeHKitPreference_v1`（SCHEMA+，旧档为空）。
- 地图选择器预览图与 BossRush 同源。
- 新增结束总结页 `ModeHSessionSummary`，回基地前展示本趟得失。
- `BuildHallOfFameRecord` 原样从 CombatFlow 移到 SettlementFlow，满足 1200 行预算。

**其他**：
- 丧尸刷怪点改用 A* 连通判定（`SpawnPositionHelper.TryResolveReachableFrom`），修复所有地图收集为 0。
- 失败回基地改为等官方 `AfterInit`，修复 InitLevel NRE。
- 静听耳罩：听力 +1 与听声辨位 +1 可见。
- 宿命回响入场页加「×」关闭。
- 杰夫目标改为直接动作，另加两条守卫。

**验证**：
- 守卫：全量 706 / 717 通过。剩余 11 条都由工作区 9-22 残留的未跟踪旧源码引起；在「HEAD + 本次改动」的干净 worktree 中，这 11 条连同行数预算全部通过。
- 执行回归：ModeH 11 / 11、Zombie 6 / 6、Campaign 1 / 1 通过。
- 构建：Windows 正式构建通过，部署 SHA-256 `C98BCF1F11D746C841DAF96BE567234684C672AFE57B2F7ABADA43F1C8A88F1F` 与 Build 一致。
- 证据止于 L2；L3 实机待验，未提交。

## 2026-09-25 鸭王杯选人与押注、遗种成长、后山变身及 Jeff 引导（COMPAT / SCHEMA+ / WIRE+ / OPERATIONAL）

**授权与范围**：owner 的 16 项需求；在本轮开始时已有的未提交实现上核对、修复和补齐，不提交 Git。官方 Wiki 生物页抓取为 `docs/reference/官方Wiki_Boss预制体_2026-09-25.json`（51 个 Boss 原始对象），当前鸭王杯候选查表见 `docs/reference/ModeH官方Boss预制体属性.md`。

**完成**：
- 鸭王杯：两步选首发/接力、三次刷新锁定首发；选人去下注、地图选择去横幅；候选逐帧预备全槽装备和兼容弹药，首发、接力、敌方与增援共用预览/实装计划。赛前左右双栏显示八项属性条、数字和装备，下方下注；背包、穿戴、任务、零估值与整件容器均可押。奖励图标加右下数量；揭晓动画完成才生成开战；观战投降/退出、终局返基地。
- 场地：每局按 seed 从已登记地图点随机选择，使用官方 AI 的 A* 图核对可走节点、完整路径、绕行与胶囊净空；看台/隔离点同步，续赛按原 seed 重建；没有安全组合中止，不用未经检查的固定点兜底。
- 遗种：蓝/白/绿炫彩配方与层次，三个自定义血脉的对应装备；新枪先核兼容弹药与弹匣再替换，另尝试补充 300 发备用弹药。提高初始体型，按来源 Boss 与等级重定伤害/生命/经验，升级保留生命比例。
- 后山：收成果实优先进包，满包沿官方仓库/快递；缺产物资源不清作物。龙息果/焚心椒/幽影蘑菇分别即时变成龙裔/龙皇/女巫 30 秒，保留玩家当前和最大生命与真实装备，结束/死亡/过图清理；旧档已预备餐食仍按原表兑现。
- Jeff：14 条一次性内容引导（590201–590214）覆盖 D–H、丧尸、遗种、随机事件、天空岛装备、菜地、陈列、词缀锻造、重铸、日报。沿唯一官方任务投影，基地接/交，Mod 三态权威，官方快照过滤，不额外发钱。中英文百科与相关 repowiki 同步。

**取舍与回退**：赔率保留五档与原抽水/样本校准，档位改由装备后战力分差决定，接力权重 0.8、旧公开条件最多修正 ±8；更强的我方不提高回报，不以押注金额反推赔率。公开 preset 随机 stat 区间取中点，预览与实装同值，保留 AI/技能/动态 buff；不是已测真实胜率。崽保持 Lv10/每级 100 经验；初始体型普通 0.62/龙王 0.64，每级初始体型 +5%；伤害来源倍率×0.20 后钳 0.12–0.28，每级 +8%、生命每级 +6%；归巢 20（重伤也计入）、击杀 4–16、每局击杀最多 80。三种变身分别主打前方龙息、贴身清群、机动斩击，参数及回退详见总报告。回退可调常量/接回原餐食入口，但保留已有 key、等级经验和旧餐食兑现；押钱账本 v3 的可选 prizeItems、引导三态可选字段必须兼容读取，不删除玩家数据。

**最终验证（L2）**：Windows 正式构建 `Build succeeded!`（唯一既有 RuntimeGate CS0649）；全量守卫 669 PASS / 0 FAIL / 0 KNOWN-RED，执行回归 64 PASS / 0 FAIL（私有 SDK 10 + 官方/Harmony DLL，未改目标框架）。新增 Morph 887、遗种成长 40、装备预案 17 条断言；赔率单调、A* 完整路径和女巫受击胶囊等反向验证按预期红、按字节还原绿。Wiki 构建、80 项导航、237 页 / 39,137 引用链接检查通过。正式部署 DLL 与 Build SHA-256 均为 `8d4ddfc72102e5f93275b4b5e51f71373ab89ade3e8152da3cba5001b6a0a189`；14 个 Dev 专用标识全部 absent（同轮 Dev 探针 present 也通过），72 包发布 SHA-256 一致。`git diff --check` 通过。全量日志与编译源哈希清单在 `Build/delivery-20260925/`，逐项交付见总报告。

**追补（同日，提交前复核）**：
- 结算页「选手 · 名声 N」只在名声大于 0 时显示（第 7 条「少放文字」的尾巴）。
- 第 2 条「进鸭王杯地图选择器时的横幅也去掉」：上面只去了地图卡右侧的预览大图，交互进入时 `ModeHInteractable.ShowRiskNotice` 推的风险提示横幅还在，一并删除；§22.1 的风险披露保留在选人页页脚（`CompactRiskNotice`），`ModeHLocalizationGuard` 的引用清单同步改为 UIPages + Localization 两处。回退：恢复 `OpenEntryFlow` 里的调用与守卫清单。
- 已修（第二轮追补，SCHEMA+）：选人页刷新次数原来只在内存里，退出重进走恢复后又给满 3 次（候选名单已落盘，等于无限重抽）。新增独立 key `BossRush_ModeHDraftRefresh_v1`（`ModeH/ModeHDraftRefreshLedger.cs`，共享 `BossRushSlotJsonStore`，按 runId 记已用次数，旧档读作 0；先记次数再落赛季；模块销毁退订）。回退：删掉三处调用，key 留着无害。
- 已修（同轮）：首发一旦选中就不能取消；若他和其余四人都凑不出六场且刷新用完，玩家会卡死在选人页。现在再点首发即取消重选；接力被拒时若五席里任意一对都排不满赛季且刷新已用完，挂出原有「退出本赛季」（`HasAnyViableDraftPair`，同一条签约 / 分流 / 六场可行性门）。`ModeHPrematchPresentationGuard` 补对应断言与 6 个变异探针（均按预期转红）。
- 验证（L2）：全量守卫 669 PASS / 0 FAIL；执行回归 64 PASS / 0 FAIL（需私有 .NET 10 SDK 与 `BOSSRUSH_HARMONY_DLL`，缺环境时会有 5 个夹具报 NETSDK1045 / 缺 Harmony，不是代码失败）；正式构建通过，14 个 Dev 标识 absent，`git diff --check` 通过；部署 DLL 与 Build SHA-256 均为 `cd7bc505a88a783e0b6ea917c017a4597436cd1c942b54bb4a3a2da9e86cae41`（第二轮追补后重建，取代上面的哈希）。

**L3 未做**：未启动游戏、读取玩家存档或实机截图；没有实测帧耗时。逐项操作及不合格判据见总报告与三个专项报告。owner 重点验证：锁首发后可刷新三次并自行选接力；赛前全槽属性与实装相符；动画结束才开战；穿戴押品按身份结算；随机场地可走、投降/退出和终局回基地；三色崽外观与装备/成长；三种收成果实 30 秒变身、生命不跳变、结束/过图恢复；Jeff 接取/体验/交付分别持久化。

## 2026-09-25 F3 实机报告红项修复 + F3 逐图进场（COMPAT / SAFE / OPERATIONAL）

**授权与范围**：owner 读完 F3 报告（runId `20260925_044506_391`，`51d2f0e6` Dev 构建）后说「全部修复确保没问题就提交」「云蚋能看到不用调」「拓展 F3 在所有地图选择器里的地图进行测试」。三个待拍板项按推荐方案定：夜长随官方缩到约 8 分钟；苇白前后矛盾的催单顺手修；手记改成「烧一块来「引风」」。回退：`SkyIslandNight` 三个常量、两段台词各一处，改回即可，不涉及存档。

**报告结论**：基地与模式 1–7 阶段 182 项全过（含当日鸭王杯押物品、日报、丧尸邀请函修复）；测试档剧情 / 环境 / 物品 / 金钱还原 PASS；`Player.log` 没有 BossRush 异常（DuckMarket 16、MoveBlackMarket 8、官方 `GamingConsole.Load` 1）。9 条红全在天空岛，逐条定性见 `CODE_REVIEW_FINDINGS.md` 同日 CR-2026-09-25-006 至 013。

**完成**：
- 生产：判夜 19–5 → 22–6（官方 prefab 运行时值）；天空岛导航图关 `enableNavmeshCutting`，门封锁丢失的重封计数 + 10 秒限频日志；苇白「灯亮未接单」只说先接；浮舟十盏灯英文合回两屏；手记措辞。
- 验收数据 / 判据：镰爪瞬移偏移改 `9:5`；云蚋改刷在瞄准方向 ±15°（表现与门槛不动）；剧情阶段补齐序章与三条岛上任务的接 / 交，苇白情报断言挪到第 4 句；两处手记断言跟文案；`SKY_NIGHT_BOUNDARY_OFFICIAL` 补记 `official_dawn`。
- 新守卫口径：`SkyIslandAutotestTableGuard` 按几何表碰撞盒离线复算瞬移落点（+1 条反向检查）；`SkyIslandGateNavigationPropertyTest` 钉住关切割那一行；SkyIslandMarriageTextRegression 加「未接单不催交」。
- **F3 逐图进场**（新）：主套件 6/7 之后按地图选择器清单（9 张，含两个子场景）逐张从基地进场 → 核对 → 回基地，用例 `MAP_TOUR_<场景>` + `MAP_TOUR_ALL`，登记进 `GameplayCoverage.json` 的 ENTRY；判据 `F3GameplayValidationMapTourJudges.cs` + 执行回归 `F3MapTourJudges`（40 条，反向验证：删掉导航判定即红，还原后 SHA-256 一致）。M_ENTRY_02 人工项只留移动、打怪手感与画面。
- 测试基建：`tests/fixtures/Directory.Build.props` 排除夹具本地 `obj/`、`bin/`（编辑器设计时构建生成的 `obj/Debug` 让整组回归 CS0579）；原有 20 个未跟踪的夹具 `obj/` 目录没删，挪到会话 scratchpad 备份。

**验证**：
- 全量守卫 663 PASS；2 红是 `BaseBuildingResourcePropertyTest` / `DailyReportArtPropertyTest` 在本机 Python 3.13 缺 `UnityPy`（导入即失败，与本轮无关，09-22 同类环境缺口）。
- 执行回归 61 / 61 PASS（其中 5 个用 09-25 留下的私有 .NET 10 SDK 与 `BOSSRUSH_HARMONY_DLL` / `BOSSRUSH_GAME_MANAGED` 跑）。
- Dev 构建与正式构建均 `Build succeeded!`（唯一警告是原有 CS0649）；`check_dll_identifiers --expect absent` PASS；游戏目录已换回**正式构建**，DLL SHA-256 `6E741FF4…22C7` 与 `Build/` 一致。Wiki 构建、237 页链接检查、Wiki 守卫通过。
- 离线逐屏复算（临时探针，已删）：苇白英文结局 + 七灯阶段 7 屏、情报在第 4 屏；浮舟第 6 屏是星工装备。

**L3 未做**：本轮没有启动游戏。owner 说不必再跑一轮；下次跑 F3 时看这几项：`SKY_GATE_REACHABILITY` 应 `gate_locked_blocked=1/1` 且 `Player.log` 无 `lost and re-applied`；`SKY_NIGHT_BOUNDARY_OFFICIAL` PASS 并读 `official_dawn`；`MAP_TOUR_*` 九张的 `points / grounded / nav` 与 `player_to_spawn_m`——导航阈值 3 m、传送阈值 4 m 是按生产逻辑定的，没实机标定，首轮若有个别刷新点红，先看 reason 里列出的点号再决定改数据还是改阈值。

## 归档索引

- 2026-11-06 潮闸镇五分钟体验复审（COMPAT / WIRE+）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-11.md`
- 2026-11-06 潮闸镇高价值搜刮与装船货单（COMPAT）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-11.md`
- 2026-11-06 潮闸镇路线、密度与工业危险（COMPAT / WIRE+）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-11.md`
- 2026-11-06 潮闸镇首轮实机反馈（COMPAT / WIRE+）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-11.md`
- 2026-11-06 潮闸镇新内容审核 001–010 全部修复（COMPAT / OPERATIONAL，L1/L2；L3 待验）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-11.md`
- 2026-11-05 潮闸镇生产水准复审与全部修复（COMPAT / OPERATIONAL；L1/L2，L3 待验）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-11.md`
- 2026-11-05 潮闸镇提交全面审查与全部修复（COMPAT；L1/L2，L3 待验）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-11.md`
- 2026-11-05 新地图「潮闸镇」接入游戏（COMPAT / SCHEMA+ / OPERATIONAL；L1/L2，L3 待验）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-11.md`
- 2026-09-26 模块解耦 P0–P6 离线完成（SAFE / COMPAT / OPERATIONAL，L3 待验收）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-22 人工实测复核后的 12 项修复与正式交付（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-29 天空岛发布前生产级审查与修复（COMPAT / SAFE / OPERATIONAL）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-30 天空岛摆放与阴影修复（COMPAT / SCHEMA+ / OPERATIONAL）（2026-11-25 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-10-30 v2.3.9 正式发布包准备（COMPAT / OPERATIONAL；L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-30 全面复审四项修复（COMPAT；L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-29 最新代码审核与遗种崽名字/背包排查（COMPAT；L1/L2 修复，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-25 F3 面板滚动、录制小键盘热键与野外趣事落点放宽（COMPAT；L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-25 全模块边界审查与修复（COMPAT / SAFE；L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-24 第三轮：新敌人更耐打、随机遇敌、叮当委托与五种试用品（COMPAT / SCHEMA+；L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-24 提交前全量核对与残日盾片图标重做（COMPAT / SAFE；L1/L2）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-24 蚀日花冠 37 号目标圈不刷新（COMPAT；L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-24 新敌人难度重做与表现质感提升（COMPAT；L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-24 冠军之影入池与龙息丧尸兼容（COMPAT / WIRE+；L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-23 CR-008 补充：吞咽动画受击外沿修复（COMPAT；Fixed at L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-23 CR-008 / 009 修复（COMPAT；Fixed at L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-23 野外趣事初始独立审核（SAFE；CR-008、009 后续修复见上节）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-23 野外趣事六项修复与交叉复审（COMPAT；CR-002～007 Fixed at L1/L2，L3 待验）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-23 蚀日花冠粒子报错与独立音乐（COMPAT）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-22 蚀日花冠官方地图、Boss 池与作者资源（COMPAT / SCHEMA+ / WIRE+）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-22 Demo 丧尸入场与旧传送防护（COMPAT，CR-001/002 Fixed at L1/L2；未部署）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-21 v2.3.7 发版准备与游戏目录部署（OPERATIONAL / SAFE，已部署、未发布）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-21 自定义 Boss / NPC 迷雾可见性（COMPAT / WIRE+，L1/L2，未部署、未实机）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-21 自定义友好 NPC 移除常驻头顶姓名（COMPAT，L1/L2，未部署、未实机）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-21 丧尸满进度滞留与信标后续波次（COMPAT，L1/L2，未部署、未实机）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-20 丧尸第 23 波无敌：NaN 传播防护与碰撞恢复（COMPAT / WIRE+，L1/L2，未部署、未自行实机）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-18 v2.3.6 发版准备（COMPAT / OPERATIONAL，L1/L2；本轮修复的 F3 核对 L3 见下一节，未推送）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-18 v2.3.5 之后五个提交审查：CR-2026-10-18-001～022 全部修复（COMPAT / OPERATIONAL，L1/L2，未提交、未部署、未实机）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-16 玩家反馈四项 + v2.3.4 发版（COMPAT / OPERATIONAL，L1/L2，未实机）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-14 玩家反馈五项（COMPAT，L1/L2，未提交、未实机）（2026-11-25 台账超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-17 玩家反馈：丧尸模式 J-Lab 掉帧、RPG 不配火箭弹、属性满了还出、白手起家前两波出 Boss（COMPAT，L1/L2，未提交、未实机）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-17 NPC 好感 / 商店改进阶段一（COMPAT / SCHEMA+，L1/L2，未实机）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-17 遗种巢 UI 与交互全部修复 + 丧尸模式崽打不动（COMPAT / SCHEMA+，L1/L2，未部署、未实机）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-17 遗种巢远征与战痕调整（COMPAT，经济调整经 owner 授权，L1/L2，未实机）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-17 v2.3.5 发版（COMPAT / OPERATIONAL，L1/L2，未实机）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-17 天空岛交互点可见性：每处交互都有实体物件（COMPAT，L1/L2，未提交、未实机）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-17 天空岛剧情指引全面核查（COMPAT，L1/L2，未提交、未实机）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-17 玩家反馈「双航标门在哪里，风晶哪里烧」（COMPAT，L1/L2，未提交、未实机）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-16 天空岛任务指引指向复核（COMPAT，L1/L2，未提交、未实机）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-15 v2.3.3 发版准备（COMPAT / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-13 甩锅许可证提交验收（COMPAT，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-13 推送后源码门禁兼容修复（COMPAT / SAFE，L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-13 玩家反馈实现复审修复（COMPAT / SAFE / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-12 玩家反馈与 Player(1).log 全面修复（COMPAT / SCHEMA+，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-11 最近五次提交审查的四项修复（COMPAT / SAFE，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-11 钟守节拍器原版 Boss 掉落续作（COMPAT，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-10 提交前最小修复（COMPAT / SAFE / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-10 节拍器赛璐璐资源与反馈（COMPAT / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-10 点名册改由浮舟出售与工台兜底（COMPAT / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-09 教导主任的点名册与小满旧物摊（COMPAT / SCHEMA+ / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-09 冠军之影 / 星阙打磨与 v2.3.2（COMPAT，L1/L2，图标经 owner 实机确认）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 噬魂挽歌文本与本轮交付（COMPAT / WIRE+ / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 天空岛入场与兼容恢复（COMPAT / WIRE+，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 未提交改动全面审核（COMPAT / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 沙暴冠军系列提交前复核（SAFE / COMPAT / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 沙尘浓度、内向泡泡与沙龙轮廓（COMPAT，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 用户截图反馈后的砂云表现重做（COMPAT，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 实档章节缺失、沙圈出生点与末阶段调度（COMPAT，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 追踪双圈、沙暴翻倍与沙卫提速（COMPAT / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 征程加载时机、猪鲨循环与星阙表现（COMPAT / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 沙暴冠军站桩、纯弹幕与持棍沙卫（COMPAT / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 进图预热、沙暴冠军与星阙（COMPAT / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 空仓地雷盒全面复审与提交（COMPAT / SAFE，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-08 空仓地雷盒与炸弹狂人专属掉落（COMPAT，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-09-30 天空岛搜刮箱三种木箱外观（COMPAT / OPERATIONAL，L1/L2）（2026-11-24 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-30 天空岛桥栏凸挡块：修角色被模型「弹」出地图（COMPAT，L2）（2026-11-21 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-30 天空岛主题曲更名「人鱼湾」（COMPAT，L1/L2）（2026-11-21 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-30 天空岛敌人与头目武器品质（COMPAT，L1/L2）（2026-11-21 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-30 天空岛模型碰撞修复续接（COMPAT / SCHEMA+ / OPERATIONAL，L1/L2）（2026-11-21 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-30 鸭王杯模组 Boss 战力、丧尸模式 Boss 波补刷、WikiContent 全量对照代码（COMPAT，L1/L2）（2026-11-21 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-10-07 日志复现的报名石位置与女巫误清场（COMPAT，L1/L2）（2026-11-19 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-07 征程直接关闭与第六章报名石复核（COMPAT，L1/L2）（2026-11-19 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-07 远端合并与全部改动复审收尾（COMPAT / WIRE+ / OPERATIONAL，L1/L2）（2026-11-19 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-07 最新提交独立深度复审（COMPAT / WIRE+ / OPERATIONAL，L1/L2）（2026-11-19 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-09-24 docs 整理收尾：丧尸治疗文案、过时注释与死常量、资源部署描述（SAFE / COMPAT）（2026-11-12 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-24 日报正式包没跟版面重打，底图与新版面错位（OPERATIONAL / SAFE）（2026-11-12 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-24 合并 origin/main（b2f98312 续接人工复查与鸭王杯 F3 认证收尾）（OPERATIONAL）（2026-11-12 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-24 第三轮：押背包物品不设限，赢了按押品的品质与价值发奖品（COMPAT / SCHEMA+）（2026-11-12 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-24 第二轮：押背包物品、押注跟着这一场走、鸭王杯 ESC、遗种巢确认框迁共享件、看图器重复加载（COMPAT / SCHEMA+）（2026-11-12 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-24 UI 共识全量修复 + 鸭王杯押钱（COMPAT / SCHEMA+）（2026-11-12 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-24 遗种巢 UI 交互重排：列表 + 详情、按钮跟着对象走（COMPAT）（2026-11-12 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-24 续接人工复查与鸭王杯 F3 认证收尾（COMPAT / SCHEMA+ / WIRE+ / OPERATIONAL）（2026-11-12 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-10-16 F3 第十轮（`20261016_135926_649`）6 条新红复核（SAFE，L1/L2/L3，未提交；复跑 5 条转绿、云蚋按 owner 目检改判）（2026-11-08 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-06 第二轮深审完成（COMPAT / SCHEMA+ / WIRE+ / OPERATIONAL，L1/L2）（2026-11-07 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-06 玩家 16 项反馈审核与修复（COMPAT / SCHEMA+ / OPERATIONAL，L1/L2）（2026-11-07 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-01 天空岛实机反馈七项（第二批）：完成面板、序章前入口、Boss 备弹、贝壳签筒、数值减半、岛上白天户外后处理、静默门 5 m（COMPAT，L1/L2）（2026-11-05 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-01 收尾：天空岛 CR-2026-09-30-005～012 与审查遗留 P3 / 复用全部修复（COMPAT / WIRE+ / OPERATIONAL，L2）（2026-11-05 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-01 发布前外部审查：6 条 P1 + 11 条 P2/P3 全部核实成立并修复（COMPAT / SCHEMA+，L2）（2026-11-05 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-01 天空岛实机反馈七项：全岛官方 Boss、属性 ×3、加密巡守、搜刮箱、居民中立、码头 BGM（COMPAT / OPERATIONAL，L1/L2）（2026-11-05 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-10-01 天空岛路灯夜里亮并照亮周围（COMPAT，L1/L2）（2026-11-05 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-10.md`
- 2026-09-30 天空岛场景净空、穿模与质感全岛清理（COMPAT / SCHEMA+ / OPERATIONAL，L1/L2）（2026-11-05 台账将超 1500 行时逐字归档） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-29 鸭王杯群战第四轮：龙裔二阶段弹幕、龙皇一阶段攻击（COMPAT，L1/L2） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-29 鸭王杯群战第三轮：焚天龙皇（COMPAT，L1/L2） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-29 鸭王杯群战第二轮：刷墙 / 观战切换 / 押哪边 / 队伍标识 / 同队误伤 / 可重复 / 场次爬升 / 龙皇（COMPAT，L1/L2） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-29 鸭王杯改群战 + 全 Boss 池 + 迷宫入场 + 词条 HUD 重叠（COMPAT，L1/L2） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-29 天空岛发布前复审（COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-29 Mode H 发布复核（7 项新增修复，COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-29 未提交改动提交前复核（COMPAT / SAFE / OPERATIONAL，L1/L2） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-29 Mode H 发布前审核第二轮闭环（COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-29 Mode H 发布前审核闭环（COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-28 十四项玩家反馈与日志复核（COMPAT / SCHEMA+ / WIRE+ / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-28 天空岛八项修复与生产验收准备（COMPAT / WIRE+） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-27 生产就绪审计与 Boss 设计修复（COMPAT / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-27 WikiContent 对照最新代码全面审校（SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-27 修复独立验收问题并合并远端（COMPAT / SAFE / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-27 序章「云上的坐标」加物品奖励（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-27 天空岛三条主线加物品奖励（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-27 Jeff 任务的物品奖励、引导奖金与提交物品（COMPAT / WIRE+ / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-27 Jeff 任务、菜地收获与果实变身复审（COMPAT / WIRE+ / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-27 天空岛发版复审：Boss 光与声回执、苇白礼物标签、Wiki 对账（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-26 点唱机曲目响度、天空岛常驻 BGM、模组更名与网址、v2.3.0 日志并版（COMPAT / SCHEMA+ / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-26 天空岛敌人与 Boss 按原版参照提升 50%（COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-26 Jeff 任务、菜地收获与三形态生产链审查（COMPAT / WIRE+ / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-26 焚天龙铳所有弹药去掉那层很白的光效（COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-26 天空岛发版全链审查与修复（COMPAT / SAFE / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-26 鸭王杯四页重排、刷新不闪、Jeff 引导一条接一条、崽炫彩蓝白绿精修（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-25 全仓审查六项修复与提交（COMPAT / SCHEMA+ / SAFE / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-26 丧尸模式 Boss 表现、击杀图鉴与完整链路（COMPAT / WIRE+ / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-26 入场 Mod 兼容与重铸后坐力（COMPAT / WIRE+ / SAFE / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-27 丧尸模式 Boss 表现、官方计数与补丁隔离（COMPAT / WIRE+ / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-27（第二轮）追猎狂暴常驻与残留区死因（COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-22 全仓审计报告修复闭环（COMPAT / SCHEMA+ / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-22 实机启动报错：资源加载器路径键不一致导致全部 Mod 物品注册失败（COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-22 鸭王征程重设计：杰夫发放 + 新故事 + 后山改接官方建筑（COMPAT / SCHEMA+ / WIRE+ / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-23 菜地收获完成横幅（COMPAT / WIRE+） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-23 人工复查六项续修（COMPAT / SCHEMA+ / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-23 玩家文案去「人机感」打磨（SAFE / COMPAT，纯文本） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 人工实测第二轮补漏（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 资源生产化续作（COMPAT / OPERATIONAL，无 LOD） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 基地四建筑模型接入（COMPAT / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 Unity 资源生产优化（COMPAT / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 全部头盔校正与佩戴管线统一（COMPAT / SAFE / 局部 OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 词缀选物 UI 覆盖与资源生产审计（COMPAT / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 人工实测补漏（第三轮，COMPAT / SAFE / 局部 OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 雷霆头盔佩戴试修（COMPAT / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 岛上三条主线：交付奖金与文案统一（COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 地图标记场景参数：原判被推翻 + 三处收成一份解析（SAFE / 更正） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 序章任务实物交付、奖金与地图标记（COMPAT / SCHEMA+ / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-19 人工实测补漏（第二轮，COMPAT / SAFE / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-19 人工实测 25 项修复（COMPAT / SCHEMA+ / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-19 Mode H 奖励池可靠性复核（COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-19 Wiki 内容修正与发布前验证（SAFE / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-19 Mode G 异常路径复核（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-19 NPC 对白与天空岛气泡修复（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-19 鸭科夫日报生产复核（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-18 遗种巢（PetNest）生产水准审核与优化（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-18 天空岛导航、语言与气泡修复提交（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-18 晴岚群岛生产复核与交付可靠性（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-18 天空岛复审 023–025 全部修复（COMPAT / SAFE，待实机） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-18 Mode G 全链审核与生产修复（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-18 鸭皇图鉴生产审核与体验优化（COMPAT） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-18 鸭王征程生产审核与体验优化（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-18 竞技场后山生产复审（COMPAT / SCHEMA+） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-19 Wiki 内容一致性核对与本地网站验证（SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 人工实测 17 项修复（COMPAT / SCHEMA+ / OPERATIONAL） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-20 第三轮：外部审查 11 条复核 + 补漏 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 天空岛导航优化后全面复审 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 天空岛完成度复核与导航/场景刷新优化（COMPAT / SAFE） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 天空岛婚姻复审问题全部修复（COMPAT / SAFE，待实机） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 天空岛「活人感」：居民漫步、全岛头顶气泡与居民对白改写（COMPAT，待实机） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 天空岛婚姻修复复审（只审核，未修复新问题） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 天空岛婚姻剧情修复（COMPAT，待实机） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 全项目续审：战斗回调、效果归因与清理 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 天空岛 NPC 婚姻与剧情衔接审核（未改生产代码） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 龙裔燃烧弹误选烟花 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 游戏内百科与在线 Wiki 内容一致性核对 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 玩家文案审核：阿稳新内容、天空岛对白与 UI 阅读负担 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 全项目玩法闭环复核：保留内容，修正目标与交付 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-17 天空岛完成度复核：主线就地衔接、交付恢复与任务入口优化 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-16 天空岛审核：昼夜对齐官方 19–5、岛上主线接官方任务（挂岛上 NPC） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-16 天空岛 Jeff 官方任务接入、旧档迁移与出击回滚修复 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-16 F3 第八轮复核：四条红项全修 + 日志里审出的两条（落脚点弹球、观星手无自动首杀断言） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-15 F3 第七轮复核（R2–R4 首次实机）：三位头目等不到是步骤顺序、苇白台词期望过时、钟庭门中途漏通；补头目专属捏脸 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-15 天空岛头目 / 岛主 R2–R4：九位 Boss 与 13 件专属装备（模型已进包、Dev 已部署；第七轮 F3 复核见上一节） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-15 头目 R1 补齐：残星匠首真实击杀后读尸体箱，核对「配装即掉落」 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-15 全自动实机验收第五轮：撤离环实机生效，截图审出字幕截断、地图标签星号、手记视口、钟守空面板 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-15 全自动实机验收第四轮：日志全绿，截图审出广场撤离环被台面盖住 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-15 全自动实机验收第三轮：点对话选项太早、风级读早、官方对话框溢出误报 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-15 全自动实机验收第二轮：Mode F 撤离后停在「点击继续」 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-15 全自动实机验收首轮复核补修：世界文字跟语言、婚礼教堂注入前判存在、Mode H 赛季收场判据 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-14 全自动实机验收：首轮实测复核与修复 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-14 全自动实机验收：待拍板落地 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-14 全自动实机验收（F3「自动验收」接上天空岛后半程，Dev 构建） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-14（五）天空岛头目 / 岛主 R1：残星匠首 + 瞭台观星手（新内容，TypeID 500086–500089） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-14（四）UI 优化对照审核：全部修复（6 P2 + 18 P3 已修；5 条 PLAUSIBLE 中 4 条防御性修复、1 条 Deferred） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-14（三）天空岛 B 轮「噬风·回响」+ 物资池预热 + 帧时间分项计时（新内容 + 1 P3 已修） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-14 天空岛实体植被、道路可视净空与 B 南石凳穿插修复 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-14 天空岛第三轮瀑布法线保持与屋面接缝修复 → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-14（二）天空岛首轮岛内 F3 实机日志复核：可达性假红与探路判据、对话演练崩溃、夜里指标、退游戏返航（1 P2 + 3 P3，全部已修） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-14 天空岛与共享 UI「实机前减负」：F3 自动检查 / 剧情面板三处 / 常驻 HUD 跟随官方界面（4 P2 + 5 P3，全部已实现） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-13 天空岛第三轮：官方 API 复用 / 选项分层 / 文案分层 / 敌怪补洞 / 立绘抠图（4 P2 + 2 P3 + 1 documented） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-13 天空岛 UI 美术化（1 P1 + 4 P2 + 2 P3，全部已实现） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-12（第五轮）owner 授权后把待拍板项全部定案并实现（1 P1 + 2 P2） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-12（第四轮）玩家链路审核与修复（3 P2 + 4 P3 已修，含 R-6 落地） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-12（第三轮）F3 实机报告驱动的审核与修复（1 P1 + 2 P2 + 4 P3 已修） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-12（第二轮）关闭历史 Open 项（1 P1 + 1 孪生缺陷已修） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-12 近两月新增内容的可玩性审核（2 P2 已修） → `archive/FIX_TRACKER_2026-09.md`
- 2026-09-11 全面生产审核（进行中） → `archive/FIX_TRACKER_2026-09.md`
- 最新修复 → `archive/FIX_TRACKER_2026-09.md`
- 状态定义 → `archive/FIX_TRACKER_2026-09.md`
- 条目模板 → `archive/FIX_TRACKER_2026-09.md`
- 修复记录 → `archive/FIX_TRACKER_2026-09.md`
- 变更日志 → `archive/FIX_TRACKER_2026-08.md`
- 2026-08-30 三系统落地：鸭皇图鉴 / 局内随机事件 / 词缀锻造 → `archive/FIX_TRACKER_2026-08.md`
- 2026-08-30 鸭王征程 / 竞技场后山 全面审核 → `archive/FIX_TRACKER_2026-08.md`
- 2026-09-14 天空岛生产整合：朝向漂移、退化面与最终资源 → `archive/FIX_TRACKER_2026-09.md`


## 2026-09-23 全 Mod UI / 交互 / 特效「塑料感」审查与修复（COMPAT / OPERATIONAL，少量 WIRE+）

**起因**：owner「全面检查我们 mod 里的 UI 以及交互，确保符合审美，而不是塑料感，以及特效也是」，同时复核 20260922 人工实测 16 项。

**审查**：8 个区域只读审查，约 280 条 finding（判据、逐条锚点与修法在本地 `docs/reports/reviews/2026-09-23-审美审查/`：`ui_A…E_findings.md`、`vfx_A/B_findings.md`、`verify16_findings.md`；各区修复报告 `fix_*_report.md`）。16 项独立复核：9 项已修、4 项部分修复（本轮补完，见下）、3 项只能实机判断。

**共享层（主会话）**：
- 新增 `Common/UI/BossRushUIFeel.cs`：按钮经 `ApplyButtonColors` 自动挂官方 `UI/hover` / `UI/click` 音效与按下回弹（常态零 Update）；面与卡片经 `ApplyPanelStroke` 自动加外投影与顶边高光，描边随层级变化置顶；`BossRushUIKit.PlayCloseAndDestroy`（关闭淡出）、`StyleSecondaryButton`、`StyleBackdrop`（遮罩暗角 + 淡入，整页重建不重播）、TMP 世界字描边材质。
- 新 token `BossRushUIColors.AccentFill`（主按钮填充；Accent 不再整块平涂）；新层级 `BossRushUILayers.ScreenAmbience = -10`（血月暗角压在官方 HUD 之下）。
- `PlayOpenAnimation` 改为 0.16 s SmoothStep 淡入 + 0.22 s EaseOut 从 0.94 放大；按下色统一 `GetPressedColor`；原地改色时鼠标在上落到悬停色；`CreateHighlightBar` 圆角。
- 新增 `Common/Effects/BossRushFxMaterials.cs`：只用游戏里确认存在的 `Universal Render Pipeline/Particles/Unlit`（透明变体），兜底 Legacy Alpha Blended 以 `SetVector` 写中性 0.5 Tint。规则写进 AGENTS §4.14，守卫 `tests/BossRushUIFeelGuard.py`（10 个反向检查 + 磁盘反向验证）。

**各区修复**（逐条表见各区报告；P1、P2 除明确延期项外全修）：Mode H 24 条（含复核 V6 七条）、Mode G/E/F 与波次提示 20 条、丧尸模式 35 条、遗种巢 / 征程 / 随机事件 / 阿稳寄存 29+3 条、集成面板 37+2 条、天空岛 UI 23 条、日报 7 条（重打 `production_icons`，14 个图标与吉祥物为 AI 插画）、特效 A 29 条、特效 B 21 条、幽灵女巫 10 条。

**主会话补修**：
- 复核第 2 项：基地建筑建预制体时把实体碰撞体参数写进 Player.log（`[BaseBuilding]`，正式构建也打）；遗种巢交互 trigger 2→2.6 m。报箱碰撞仍 UNVERIFIED，等 owner 按清单 R1 实测。
- 复核第 15 项：菜地本趟开放时对在场售货机补挂种子（`BackMountainItems.TryInjectSeedsIntoLiveShops`）。
- 幽灵女巫瞬移标记直接改 `sharedMaterials`（借来的霜之哀伤冰焰），会把玩家的冰焰与共享材质染紫：改走 MaterialPropertyBlock。
- Wiki：保底措辞（连续 9 枚没出、第 10 枚必出）、炫彩 90 种（渐变有先后）、Mode G 中文「最后处决」、Mode H 打法标签。
- 超 1200 行的文件按 §4.15 原样拆 partial：`SkyIslandHud_Layout.cs`、`SkyIslandStoryPresentation_Parts.cs`、`CourierPaidLootSweepDelivery.cs`，读它们的 6 个守卫与 2 个夹具同步读新文件。F3 `MODE_H_FULL_SEASON` 接受无报价时自动关窗的转会窗口。
- `ModBehaviourInstanceClassificationGuard` 基线 410→404（Integration −6）；宿主 partial 预算 104499→103200（丧尸奖励面板迁出独立类）。

**延期 / 需 owner 定**：UB-29（地图选择改 Harmony postfix，新增绑定不做）；UD-07（`CreateTMPText` 默认自动缩字不改，改默认会让大量窄框文本整串清空）；UD-43 叮当 / 羽织 / 阿稳立绘与 UA-27 随机事件 9–11 图标、UE-08 信鸽、UE-12 罗盘（都要出图 / 重打包）；UD-49 号角音效（无音频资源）；VB-06 女巫横扫视觉半径是判定 2 倍（owner 旧要求，守卫钉着）；怨灵拖斩刀光锁方向而判定跟随玩家（要么改玩法要么改守卫意图）；VB-19 焚皇戟特效池（纯性能）；遗种巢头顶名条显示血脉名（要改 preset nameKey，离线证不了安全）；异色实际约 1.2%（100 枚保底后）；Mode H 迷雾半径 50 m 未动；许愿揭晓后头顶气泡保留（确认到账）。

**验证**（证据 L1 + L2，**没有 L3**）：
- 全量守卫 662 PASS / 0 NEW-FAIL / 0 KNOWN-RED。
- Windows 正式编译 `Build succeeded!`（1010 个源，无新警告）；`check_dll_identifiers --expect absent` PASS；DLL `842AEE45617B2C9AB5A85D199EF403DF86ECB1158B4ED3B9DF21DD5BB37216BC` 已部署并与 `Build/` 一致。
- 执行回归 56/56 PASS（`AuditModeLifecycle`、`NpcAuditFixes` 两组写死 net10，本机临时改 net8 跑通后按字节还原 run.py）；本轮同步了 `SkyIslandInteraction`、`BackMountainLifecycle`、`ContentTransactions`、`ManualSeptemberReview`、`NpcAuditFixes` 五个夹具的抽取范围与替身。
- `production_icons` 重打并部署（新 SHA-256 `230c07ce…62efe02`，旧包备份 `Build/resource-release-backups/20260923-094639-313/`），72 包发布校验 0 错误；作者工程 31 个未提交改动都是日报图。
- owner 看图清单：`docs/reports/testing/2026-09-23-UI与特效审美-看图清单.md`（R1–R8 + 共享层 S1–S8 + 各区条目）。

**owner 授权拍板（2026-09-23 同日，「照着你的感觉去决定」）**：
- VB-06 女巫横扫刀光：跟模型放大，但上限 1.35 倍判定半径（`PhantomWitchConfig.ScytheSweepVisualScaleCap`）；判定不变。理由：2 倍刀光让玩家读错危险区。守卫 `PhantomWitchScytheSweepScaleGuard` 补断言并反向验证。回退：常量改 `float.MaxValue`。
- 怨灵拖斩：两道刀光改用出手瞬间的朝向，与两段伤害、扇形预警一致；蓄力轮廓仍按起手方向。判定一字未改。`PhantomWitchSpecCompletionGuard` 规格同步并反向验证。回退：刀光改回 `lockedForward`、守卫正则还原。
- 异色实际约 1.2%（天然 0.4% + 100 枚保底）：保持，不改。
- 叮当 / 羽织 / 阿稳对话立绘：不做（自定义模型，AI 立绘对不上模型比没有更出戏；官方对话框立绘为空时整块隐藏，不留空框）。随机事件 9–11 图标：做了，`tools/gen_codex_art.py` 同风格出图（网关 3 次、0 失败），登记 `production_icon_manifest.json`，IconsBuild `BUILD PASS sprites=346`，`production_icons` → `a75b0a30b86a865541ebf2232b67cb08b3a7cf5051da6e98ba1c534c517341dc`，72 包发布校验 0 错误并已部署。
- 重编正式 DLL `648FAE0E48C662AD3AD51F935D91CF6B72AADD0DDC3B3A0AC491CD8B04F112C1` 已部署；662 守卫全绿。注意：这次编译用的是工作区当前内容，**包含**另一会话进行中的「去掉 ——」文案改动（只改字符串，编译通过）。

**工作区提醒**：同一时段另一会话在做「去掉 ——」的文案整理（`DebugAndTools/SkyIsland/*` 十余个文件、`WikiContent/*` 五十余篇），不属本轮；本轮部署的 DLL 编于 10:33，不含那些 10:38 之后的改动。提交时按文件分开暂存。

<!-- END AESTHETIC AUDIT 2026-09-23 -->


<!-- BEGIN SYNTAX PROBE COMPILE LIST PARITY 2026-09-23 -->

## 2026-09-23 离线语法探针漏检编译清单文件（已归档）

已闭环的完整原文见 [2026-09 月台账归档](archive/FIX_TRACKER_2026-09.md)，验证边界随原文保留。

<!-- END SYNTAX PROBE COMPILE LIST PARITY 2026-09-23 -->

<!-- BEGIN MANUAL 16 FIX 2026-09-23 -->

## 2026-09-23 人工实测 16 项修复（已归档）

已闭环的完整原文见 [2026-09 月台账归档](archive/FIX_TRACKER_2026-09.md)，验证边界随原文保留。

<!-- END MANUAL 16 FIX 2026-09-23 -->

<!-- BEGIN FULL AUDIT FIXES 2026-09-22 -->

## 2026-09-21—22 全仓审计登记（SAFE / OPERATIONAL，未修复）

本轮累计确认 82 项（含并发修复后的历史项）：P1 25、P2 57；L1 48、L2 34。没有 L3。

本轮仅建立审计报告、逐文件覆盖与发现记录；未修生产代码、未改守卫断言、未更改存档/TypeID/配置结构、未部署或发布。新问题通常为Open；已由其他会话修复者按逐项差异复核回填，不能把外部修复记成本审计的代码改动。

完整问题、触发/保护/建议与人工步骤见 [审计报告](docs/reports/reviews/2026-09-21_full_audit_report.md)。CSV明确区分关键链深读与结构扫描；剩余正文审查继续清单为 `docs/reports/reviews/2026-09-21_full_audit_report.md`。

| ID | 原分卷 | 证据 | 状态 |
| --- | --- | --- | --- |
| CR-2026-09-21-001 | INT-11 | L2 | Open / 本审计未修 |
| CR-2026-09-21-002 | MODES-01 | L2 | Open / 本审计未修 |
| CR-2026-09-21-003 | INT-16 | L1 | Open / 本审计未修 |
| CR-2026-09-21-004 | INT-01 | L1 | Open / 本审计未修 |
| CR-2026-09-21-005 | INT-02 | L1 | Open / 本审计未修 |
| CR-2026-09-21-006 | INT-03 | L1 | Open / 本审计未修 |
| CR-2026-09-21-007 | ROOT-01 | L2 | Open / 本审计未修 |
| CR-2026-09-21-008 | SKY-01 | L1 | Open / 本审计未修 |
| CR-2026-09-21-009 | MODES-05 | L2 | Open / 本审计未修 |
| CR-2026-09-21-010 | INT-20 | L2 | Open / 本审计未修 |
| CR-2026-09-21-011 | INT-19 | L1 | Open / 本审计未修 |
| CR-2026-09-21-012 | INT-17 | L1 | Open / 本审计未修 |
| CR-2026-09-21-013 | INT-13 | L2 | Open / 本审计未修 |
| CR-2026-09-21-014 | INT-04 | L1 | Open / 本审计未修 |
| CR-2026-09-21-015 | BOS-01 | L2 | Open / 本审计未修 |
| CR-2026-09-21-016 | BOS-02 | L1 | Open / 本审计未修 |
| CR-2026-09-21-017 | MODES-02 | L1 | Open / 本审计未修 |
| CR-2026-09-21-018 | MODES-03 | L1 | Open / 本审计未修 |
| CR-2026-09-21-019 | INT-05 | L1 | Open / 本审计未修 |
| CR-2026-09-22-018 | B13etc-01 | L2 | Open / 本审计未修 |
| CR-2026-09-22-001 | IF-07 | L1 | Open / 本审计未修 |
| CR-2026-09-22-036 | TERRA-BOSS-01 | L1 | Open / 本审计未修 |
| CR-2026-09-22-037 | TERRA-BOSS-02 | L1 | Open / 本审计未修 |
| CR-2026-09-22-038 | TERRA-BOSS-03 | L1 | Open / 本审计未修 |
| CR-2026-09-22-039 | TERRA-NPC-02 | L1 | Open / 本审计未修 |
| CR-2026-09-21-020 | BOS-03 | L2 | Open / 本审计未修 |
| CR-2026-09-21-021 | BOS-04 | L2 | Open / 本审计未修 |
| CR-2026-09-21-022 | BOS-05 | L1 | Open / 本审计未修 |
| CR-2026-09-21-023 | DEV-01 | L1 | Open / 本审计未修 |
| CR-2026-09-21-024 | INT-06 | L1 | Open / 本审计未修 |
| CR-2026-09-21-025 | INT-07 | L1 | Open / 本审计未修 |
| CR-2026-09-21-026 | INT-08 | L2 | Open / 本审计未修 |
| CR-2026-09-21-027 | INT-09 | L2 | Open / 本审计未修 |
| CR-2026-09-21-028 | INT-10 | L1 | Open / 本审计未修 |
| CR-2026-09-21-029 | INT-12 | L2 | Open / 本审计未修 |
| CR-2026-09-21-030 | INT-14 | L1 | Open / 本审计未修 |
| CR-2026-09-21-031 | INT-15 | L1 | Open / 本审计未修 |
| CR-2026-09-21-032 | INT-18 | L1 | Open / 本审计未修 |
| CR-2026-09-21-033 | MODES-04 | L1 | Open / 本审计未修 |
| CR-2026-09-21-034 | PET-01 | L1 | Fixed (concurrent edit; L1) / 本审计未修 |
| CR-2026-09-21-035 | RNG-01 | L1 | Open / 本审计未修 |
| CR-2026-09-21-036 | RNG-02 | L1 | Open / 本审计未修 |
| CR-2026-09-21-037 | ROOT-02 | L1 | Open / 本审计未修 |
| CR-2026-09-21-038 | ROOT-03 | L2 | Fixed (concurrent edit) / 本审计未修 |
| CR-2026-09-21-039 | ROOT-04 | L1 | Open / 本审计未修 |
| CR-2026-09-21-040 | ROOT-06 | L1 | Open / 本审计未修 |
| CR-2026-09-21-041 | SKY-02 | L2 | Open / 本审计未修 |
| CR-2026-09-22-019 | B03-01 | L1 | Open / 本审计未修 |
| CR-2026-09-22-010 | B04-01 | L2 | Open / 本审计未修 |
| CR-2026-09-22-020 | B13etc-02 | L2 | Open / 本审计未修 |
| CR-2026-09-22-021 | B13etc-03 | L2 | Open / 本审计未修 |
| CR-2026-09-22-022 | B13etc-04 | L1 | Open / 本审计未修 |
| CR-2026-09-22-023 | B17B20-01 | L2 | Open / 本审计未修 |
| CR-2026-09-22-024 | B17B20-02 | L2 | Open / 本审计未修 |
| CR-2026-09-22-025 | B17B20-03 | L1 | Fixed (concurrent edit; L1) / 本审计未修 |
| CR-2026-09-22-026 | B17B20-04 | L2 | Open / 本审计未修 |
| CR-2026-09-22-027 | B35-01 | L2 | Open / 本审计未修 |
| CR-2026-09-22-028 | B35-02 | L2 | Open / 本审计未修 |
| CR-2026-09-22-029 | B35-03 | L2 | Open / 本审计未修 |
| CR-2026-09-22-030 | B36B28-01 | L2 | Open / 本审计未修 |
| CR-2026-09-22-031 | B36B28-02 | L2 | Open / 本审计未修 |
| CR-2026-09-22-032 | B36B28-03 | L2 | Open / 本审计未修 |
| CR-2026-09-22-033 | B48-01 | L2 | Open / 本审计未修 |
| CR-2026-09-22-034 | B49-01 | L2 | Open / 本审计未修 |
| CR-2026-09-22-035 | B50B47-01 | L1 | Open / 本审计未修 |
| CR-2026-09-22-002 | COMMON-01 | L2 | Open / 本审计未修 |
| CR-2026-09-22-003 | COMMON-02 | L2 | Open / 本审计未修 |
| CR-2026-09-22-040 | CONT-ROOT-01 | L1 | Open / 本审计未修 |
| CR-2026-09-22-041 | CONT-ROOT-02 | L1 | Open / 本审计未修 |
| CR-2026-09-22-042 | CONT-ROOT-03 | L1 | Open / 本审计未修 |
| CR-2026-09-22-043 | CONT-ROOT-04 | L1 | Open / 本审计未修 |
| CR-2026-09-22-044 | CONT-ROOT-05 | L1 | Open / 本审计未修 |
| CR-2026-09-22-045 | CONT-ROOT-06 | L2 | Open / 本审计未修 |
| CR-2026-09-22-046 | CONT-ROOT-07 | L1 | Open / 本审计未修 |
| CR-2026-09-22-048 | CONT-ROOT-08 | L1 | Open / 本审计未修 |
| CR-2026-09-22-004 | IF-01 | L1 | Open / 本审计未修 |
| CR-2026-09-22-005 | IF-02 | L2 | Open / 本审计未修 |
| CR-2026-09-22-006 | IF-03 | L2 | Open / 本审计未修 |
| CR-2026-09-22-007 | IF-04 | L1 | Open / 本审计未修 |
| CR-2026-09-22-008 | IF-05 | L1 | Open / 本审计未修 |
| CR-2026-09-22-009 | IF-06 | L1 | Open / 本审计未修 |
| CR-2026-09-22-047 | TERRA-BOSS-04 | L1 | Open / 本审计未修 |

验证：隔离正式Windows构建通过；647守卫原聚合645 PASS/2 FAIL，资源依赖补验通过后剩1旧断言红；51组回归在.NET10 roll-forward下50通过，1项tiering对照通过；70类级Harmony目标元数据解析；Wiki构建/80导航/237页39130引用通过。各失败、对照和新缺陷探针日志完整保留，不能将原始失败命令写成全绿。

真实部署DLL前后SHA-256一致：`2C8FFD5D398204076132DCB2B708628ECDCCAF7025ADBD642FF5844A3E8E2140`。没有L3或性能采样。
<!-- END FULL AUDIT 2026-09-21 -->

## 2026-09-22：人工实测 17 项审核登记（SAFE；无生产修复）

完整报告：[20260920 人工实测 17 项全面复核](docs/reports/testing/20260922人工实测复核修复记录.md)。当前结论：不能认定全部修复。4 项 P1、8 项 P2，详见 findings 对应最新小节。

| ID | 对应原要求 | 当前状态 / 证据 |
| --- | --- | --- |
| CR-2026-09-22-011 | 3、6 图鉴收录 | Open / L1+L2 |
| CR-2026-09-22-012 | 10 Mode H 地图入场 | Open / L1+L2 算术，待 L3 |
| CR-2026-09-22-013 | 14、15 远征/放生收回 | Open / L1+L2 |
| CR-2026-09-22-014 | 部署链跨项 | Open / L2，目标限定当前 D 管线 |
| CR-2026-09-22-015 | 1 套装冷却 | Open / L2 |
| CR-2026-09-20-016 | 9 日报生产底图 | Reopened / L1+L2 |
| CR-2026-09-20-017 | 9 日报正文裁字 | Reopened / L1+L2 |
| CR-2026-09-20-019 | 14 旧在途彩宠翻牌 | Reopened / L2 |
| CR-2026-09-21-034 | 远征全流程奖励 | Open / 本轮补 L2 |
| CR-2026-09-22-016 | 16 远征菜单 | Open / L1 |
| CR-2026-09-22-017 | 远征展示暂停 | Open / L1，实操可达性待 L3 |
| CR-2026-09-21-038 | 验证门禁 | Open / 本轮重新 L2 |

本轮全量守卫 646 PASS / 1 FAIL / 0 KNOWN-RED；全部执行回归 51 PASS / 0 FAIL；Windows 原样正式脚本在 960 源码冻结副本编译成功，自动部署隔离到临时游戏目录。真实 D 目标校验失败（13 缺包，59 不同），未修复或部署它。L3 为 0。初始环境失败、本轮命令和探针证据均保留在 Build/audit-20260922-manual/ 及两个专项审查目录。新增坏行为探针的 PASS 仅表示缺陷复现。


<!-- MANUAL 17 FIXED 2026-09-22 -->
