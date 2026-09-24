# FIX_TRACKER.md — 修复状态与兼容性流水账

更早的完整记录见 `archive/`；近期已闭环的大篇幅审计正文也按月份存档，当前文件保留索引与未闭环条目。

## 归档索引

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


## 2026-09-24 docs 整理收尾：丧尸治疗文案、过时注释与死常量、资源部署描述（SAFE / COMPAT）

**来源 / 分类**：`docs/` 整理时子代理核对出的待拍板项，owner 定「2、4、5 都修，改描述」。治疗文案 `COMPAT`（只改显示文字，本地化 key 不变）；其余 `SAFE`。

**完成**：
- 丧尸医疗终端「治疗」：玩家看到「恢复缺失生命 50%」，实现是 `min(缺失生命, 最大生命 × 50%)`（`ZombieMode/ZombieModeRewardNpcServices.cs` 的 `ApplyZombieModeNurseServiceEffect`）。按实现改文案为「治疗：恢复 50% 最大生命」/「Heal: Restore 50% Max HP」（`Localization/LocalizationInjector.cs`，key `BossRush_ZombieMode_Npc_NurseService_HealHalf` 不变），价目表规格同步。
- 过时注释：`DragonBreathBuffHandler.cs` 文件头「每层 2 点」改为按 `BASE_DAMAGE_PER_LAYER`（1 点、对玩家封顶 2 点）；`WishFountainUI.cs` 文件头删去已不存在的 NotificationText 大横幅；`F3GameplayValidationSkyIslandRuntimeCases.cs` 的 21 组 / 61 敌改为引用 `ExpectedEncounterGroups` / `ExpectedEncounterEnemies`；`OfficialQuestBinding.cs` 的 `PayReward` 注释改为现状（天空岛、征程都传 null，发钱归交付事务）。
- 死常量：删掉 `FenHuangHalberdIds.AssetBundlePath`、`FrostmourneIds.AssetBundlePath`（无调用点，值也不是实际包名 `*_model`）。
- 资源部署描述：待拍板里说「`compile_official.bat` 没部署 `Assets/npcs`、`Assets/entity`、`Assets/ui/bossrush_wiki`」不成立——编译结尾的 `tools/Deploy-ResourceBundles.ps1` 按 `tools/resource_release_manifest.json`（72 个包，含这 7 个）部署并校验 SHA-256，游戏目录出现清单外 bundle 会让构建失败。构建脚本不改；改正 `Integration/AGENTS.md` 接线第 8 条与 6 篇教程（NPC、交互物、Wiki 书 UI、接线清单、近战武器、头盔），写明新 bundle 要登记进清单。

**验证**：全量守卫 665/665 PASS；Windows 正式编译 `Build succeeded!`（`GAME_PATH` 指向临时 Managed 拷贝，未部署到游戏目录；唯一警告 CS0649 在未改动的 `SkyIslandOfficialQuestTable.cs`）。L1/L2；未实机。

**L3 待 owner 实机**：丧尸模式安全区打开护士医疗终端，服务列表第一项应显示「治疗：恢复 50% 最大生命」（英文 "Heal: Restore 50% Max HP"），文字不溢出按钮；在缺血少于一半时购买应回满、缺血多于一半时回复最大生命的一半。

## 2026-09-24 日报正式包没跟版面重打，底图与新版面错位（OPERATIONAL / SAFE）

**现象**：`python tests/DailyReportArtPropertyTest.py` 在 fc7da8b8 上红，报 `button`、`legend_0…4`、`icon_gossip` 烤色。

**根因（是包，不是底图，也不是判据）**：b2f98312 把签到卡、按钮、图例整体下移 60 px，状态行图标跟着下移；原图随后按新版面用生成器重出，但本机 `Assets/ui/production_icons` 还是 09-23 10:56 按旧版面打的包。实测：
- 正式包里的底图按**旧**版面量，留白判据全过；按新版面量就是上面 7 个区域。
- 当前原图按新版面以 0 容差全过；HEAD 的 `tools/gen_daily_report_ui.py` 重跑，底图与吉祥物逐字节一致，版面表内容一致（只差换行，已按原字节还原）。
所以判据阈值不用动，底图也不用重画。

**处置**：
- 作者工程（`git status` 干净、Unity 与游戏都没运行）跑 `python tools/optimize_unity_resources.py --stage IconsBuild --output Build/resource-production-20260924-daily-report-relayout`：退出码 0，`BUILD PASS sprites=346`。新旧包 346 张贴图逐张比对，只有 `daily_report_bg` 变了，别名清单相同。
- `production_icons`：`a75b0a30…41dc` 换成 **`031ee280…5055`**；游戏目录经 `tools/Deploy-ResourceBundles.ps1` 部署，部署前只有这一个包不一致、版面表已是新版，部署后 72 包 SHA-256 一致。旧包备份在本会话 scratchpad。
- 作者工程的远端同时有 831d0ec「20260924」：它的日报底图与这次构建写进作者工程的底图是同一个 blob，另外只多了 18 段 `overridden: 0` 的 Android 平台块，不影响 Windows 构建。已把本次构建留下的 4 处改动存进作者工程的 stash，再快进到 831d0ec，工作区干净，包不用重打。
- 补判据：留白判据只量控件位置，版面挪动后旧底图的控件位碰巧落在纯色上就会漏。`tools/daily_report_art_contract.py` 新增 `measure_source_drift` / `validate_source_drift`，按打包口径缩放后比对正式 Sprite 与当前原图；`tools/verify_unity_resource_release.py` 取数与判定都接上，属性测试补合成正反例与发布校验反例。实测阈值依据：同源包平均差 0.25、超 16 级占 0.015%；旧包平均差 10.2、占 20%；阈值取平均 2.0、占比 1%。这是收紧，不是放宽。

**验证（L2）**：`python tools/run_guards.py --filter DailyReport` 3 PASS；`--changed-only` 3 PASS；`BaseBuildingResourcePropertyTest`、`UnityResourceBudgetPropertyTest` PASS；`python tools/verify_unity_resource_release.py` 72 包 0 错误。反向验证：旧包按字节换回仓库后属性测试转红，同源判据对旧包单独报 stale（平均 10.20、占比 0.2034）；按字节还原成 `031ee280…` 后转绿。

**未验证（L3）**：没进游戏看日报。owner 看图清单：基地对「日报报箱」按交互键打开日报，看签到区。不合格的样子是：签到按钮、签到格或底部五个图例色块与底图上的卡片边框错开，或者卡片底边外露出按钮、图例的残影。

## 2026-09-24 合并 origin/main（b2f98312 续接人工复查与鸭王杯 F3 认证收尾）（OPERATIONAL）

本地 fe122a36 与远端 b2f98312 各自从 25fe502a 出发，29 个文件重叠，4 个有冲突：
- `Integration/DailyReport/DailyReportUI_Dashboard.cs`：远端去掉关闭按钮、悬赏全文显示不滚动（`CreateIconText(..., true, false)`）；本地 A-16 把字号收成三档常量。合并取远端的布局与「换行、不滚动」，字号用 `BodyFontSize`，关闭按钮按远端删除。
- `tests/DailyReportPresentationGuard.py`：远端 `check_bounty_and_close` 的正则钉死字面量 `20f`，与本地 `check_ui_consensus`（禁止字面量字号）互斥。按其断言本意（「今日悬赏必须启用换行并关闭卡片内滚动」）把字号参数改为 `BodyFontSize`，换行 / 不滚动照旧钉住；反向验证：把 `true, false` 改成 `true, true` 转红，按字节还原。
- `tests/ModBehaviourInstanceClassificationGuard.py` 与分类文档：两边都给 Integration 加了宿主引用（本地百科淡出 +3、远端菜地横幅 +2），合并为 268 / 409，按实际计数核对通过。
- `FIX_TRACKER.md`：两边的条目全部保留，本地在上。

验证：全量守卫 664 PASS / 1 红；Windows 正式构建通过，`Build/BossRush.dll` 与游戏目录同为 `D8030B0C…`；执行回归 59/59 PASS（含远端新增的 `ModeHPlayerFlow`、`GardenHarvestNotice`；net10 两个照旧临时改 net8、按字节还原）。

**本机仍红的一条（环境，不是代码）**：`DailyReportArtPropertyTest` 读本机正式包 `Assets/ui/production_icons` 的日报底图像素。远端改了版面表 `Assets/Data/DailyReportLayout.json` 并在它那台机器上重打了包；包是 local-only，本机这份还是 09-23 的旧包，按新版面检查就在按钮、图例、图标位上「烤色」。本地底图原图已按新版面用 `tools/gen_daily_report_ui.py` 重新生成（生成的版面表与合并结果内容一致；原图备份在本会话 scratchpad），`BOSSRUSH_GUARD_SOURCE_ONLY=1` 下源码部分通过。要转绿需在本机 Unity 作者工程重打 `production_icons`（`tools/optimize_unity_resources.py --stage IconsBuild`），或从远端那台机器拷回它的包；在那之前本机进游戏时日报底图会与新版面错位。

## 2026-09-24 第三轮：押背包物品不设限，赢了按押品的品质与价值发奖品（COMPAT / SCHEMA+）

**起因**：owner：「押上的物品不要有限制，只是其品质和价钱会影响到再次给予其奖品的品质和价钱。」

**改了什么**（设计与回退：`docs/design/鸭王杯押钱_2026-09-24.md` 第 5、7 节）：
- **押什么都不限**：去掉第二轮的「最多 6 件、估值合计 1,000–50,000、单件超过 50,000 不列、能卖的才列、装着东西的容器不列、只列 24 件」。只挡任务物品（`Sticky`，官方连丢都不让丢，收走会断任务）和估值为 0 的东西（换不出奖品）。容器能押，卡片写「连里面的 N 件」，估值与收走都连内容一起。选择页列出全部候选、可滚动。
- **赢了发奖品**（原来是发「赔付减估值」的钱）：品质 = 押上物品按估值加权的平均品质（四舍五入，夹在 1–8，`ModeHItemBetEntry.PrizeQuality`）；总价值 = 「赔付 − 估值」（与押钱同一公式，长期回报不变）；件数 = 押上的件数，最多 6 件（`ModeHConfig.ItemBetMaxPrizeItems`，管的是发出来的奖品，免得背包塞爆）。`ModeHItemBetStake.PreparePrizes` 从共享 `BossRushQualityItemPool` 按品质挑价值落在每件目标值 50%–100% 的东西（种子确定），放不下就降一档品质；经 `ModeHRewardItemPool.TryInstantiate`（空壳门禁）实例化；凑不满的零头折成钱（账本 `prizeCash`，不超过「赔付 − 估值」）。
- **至多发一次**：先备好奖品、账本记成才 `ItemUtilities.SendToPlayer(prize, true, false)` 发（进背包，满了落在脚下，不送仓库），账本没记成就销毁、下次重备。
- 账本：押品条目多记品质（`typeId|数量|估值|品质|名字`），记录加 `prizes` / `prizeCash` 两个可选字段。开盘格子押物品时写「奖品约值 X」，结算页列出奖品与零头。
- 百科 `mode__mode_h` 中英「押物品」一节改写。

**替 owner 定的取舍（可回退）**：
1. 任务物品仍然不能押：官方给它们打了 `Sticky`，连丢都不让丢，收走会让任务卡住。估值为 0 的东西不列：押了也换不出奖品。
2. 奖品件数最多 6 件（押多少件都行，奖品封顶）；奖品品质按估值加权平均，不因赔率冷门额外升档（价值已经随赔率走）。
3. 一件都挑不到（候选池空或都太贵）时整笔折成钱，结算页写明。

**验证**：
- `python tools/run_guards.py` 全量 664/664 PASS。`ModeHIsolationGuard.check_item_bet_stake` 改为放行「只在 `DeliverPrizes` 调一次 `SendToPlayer(prize, true, false)`、奖品只经共享候选池与实例化门禁」，禁直接 `InstantiateSync` / `SendToPlayerStorage`；`ModeHCashBetGuard` 改新的结算口径并钉「先备奖品 → 记账 → 发奖 / 销毁」顺序与奖品品质、价值来源（18 个内存变异探针）。落盘反向验证：发奖改送仓库、绕过实例化门禁，隔离守卫都转红。
- 执行回归 `ModeHItemBetLedger` 补奖品品质（加权、夹范围、贵的说了算）、件数上限、折钱上限、2,000 万大额期望为负；落盘变异「品质改截断」「件数不封顶」都转红，按字节还原。全量 57/57 PASS（net10 两个照旧临时改 net8、按字节还原）。
- Windows 正式构建 Build succeeded，`Build/BossRush.dll` 与游戏目录同为 `66D88972…`；提交前去掉 `AchievementView.cs` 一处行尾空格后重编，最终 `EFCDA758…`，已部署并核对；`check_dll_identifiers --expect absent` PASS。在线 Wiki 重建通过，Wiki 守卫与 `PlayerFacingGlyphGuard` 通过。
- 证据级别 L1 + L2；**未实机**。挑奖品、实例化、发奖要真实物品表，只能实机看（设计文档第 8 节第 8、10、11 步）。

## 2026-09-24 第二轮：押背包物品、押注跟着这一场走、鸭王杯 ESC、遗种巢确认框迁共享件、看图器重复加载（COMPAT / SCHEMA+）

**起因**：上一节「UI 共识全量修复 + 鸭王杯押钱」交付时列了四件没做的（押背包物品、鸭王杯各页 ESC、看图器回退重复加载 bundle、遗种巢两个自写确认框没迁共享件）。owner：「没做的都做一下吧」。

**押背包物品**（设计与回退：`docs/design/鸭王杯押钱_2026-09-24.md` 第 5 节）：
- 押注行加一颗「押物品」，打开 `ModeHPage.ItemBet` 卡片栅格（官方物品图标、名字 ×数量、估值；选中金边 + 「√ 已押上」；「完成」或 ESC 回原页）。一场最多 6 件、估值合计 1,000–50,000，只管下一场；选押钱档会清掉押物品。
- 估值按官方商人收购口径 `GetTotalRawValue × 0.5`（`ModeHConfig.ItemBetValuePermille`）：按原价估的话押卖不上价的东西比卖掉划算。
- 物品押上**不离开背包**（托管在内存里的物品会随崩溃消失）；赢了东西留着、另发「赔付减估值」，输了由 `ForfeitLocked` 收走仍在玩家身上的那几件，找不到的（官方背包键在看台上也能开）按估值从余额扣到 0 为止，堆叠被合并变多只扣回押上的数量；读档后按账本 typeId / 数量重新认领。
- 新文件 `ModeH/ModeHItemBetStake.cs` 是 Mode H 玩家资产访问白名单的一条（`ModeHIsolationGuard.check_item_bet_stake`：只读背包、不生成不塞物品、收走只有一处且幂等）。账本加 `kind` / `items` / `charged` 三个可选字段（旧记录读出为押钱）。

**押注跟着这一场走**（自查发现，设计文档第 6 节）：上一节的押钱在技术重试、恢复回落和 `TryReturnRealStakeOnAbort`（挂起 / 关停 / 切图中止，从暂停菜单回主菜单也会走到）都整额退押金，打输了退出重进等于免费重掷，长期回报高于 0.92。现在这些路径都不退，重锁时 `ReserveStandingCashBet` 先经 `ModeHCashBetService.ReservedFor` 沿用挂着的那一笔、按重打结果结算（§17.4「不判负」照旧）；只有恢复页放弃赛季、开新赛季对到上一季、F3 清理才退。

**鸭王杯 ESC**：`ModeHActionData.IsCancel` 标出本页的「返回」（整备页与押物品页的「完成」、恢复壳的「稍后处理」），`ModeHUI` / `ModeHRecoveryPanel` 用 `PetNestCancelKey` 把 ESC 接到它；共享确认框或恢复壳盖在上面时让出。没有返回语义的页（选人、看盘、赔率、结算、转会、名人堂）不接：吞掉 ESC 会让玩家在这些页上打不开官方暂停菜单，而暂停菜单由官方 `TimeScaleManager` 压时间，盖在这些页上无害。

**文案**：风险提示 `BossRush_ModeH_RealStakeRiskNotice` 改为「押的是你的钱或背包里的东西……输了押上的归庄家」（key 不变，`ModeHLocalizationGuard` 关键词同步）；Wiki `mode__mode_h`（押物品一节、中断与退回改写、FAQ 两条）与新手路线的押注提醒中英同步，在线站重建。

**遗种巢两个确认框迁到共享件**：放生（含批量）与亡命出发改走 `BossRushConfirmDialog`（`PetNestUI.ConfirmRelease` / `ConfirmDepart` / `ShowConfirm`，Anchor 指向遗种巢主面板，面板关掉时确认框下一帧按取消收场），文案一字未改；删除 `PetNest/PetNestReleaseConfirmModal.cs` 与本轮新建未入库的 `PetNest/PetNestDepartConfirmModal.cs`（备份在本会话 scratchpad）并同步编译清单。给共享框加了一个可选字段 `DecorateTarget`（单只异色崽名字的流光），默认不变。与原来的差别：确认框层级从 `PetNestModal`（3150）抬到共享框默认的 `ModalConfirm`（3200），会压在揭晓演出之上，ESC 交给最上层。守卫 `PetNestUILayerGuard` 新增 `check_confirms`（Danger、服务调用只在 OnConfirm 里、失败回抛、Anchor、ESC 让位、目录下不得再有自绘 Confirm 类），8 条落盘反向验证。

**看图器回退重复加载 bundle**：`ImageViewerUI` 回退路径每次反射 `LoadFromFile` 从不 Unload，同一个 bundle 第二次被 Unity 拒绝。改为按完整路径缓存（先查自己的缓存、再借 `ItemFactory` 已打开的同名 bundle、都没有才经共享 `ResourceBundleLoader` 打开），同一张图只造一次 Sprite；`ResetStaticCaches` 只卸自己打开的（`Unload(false)`），接到 `IntegrationRuntimeHooks` 的销毁路径。`ItemFactory.FindAlreadyLoadedAssetBundle` 改 internal 复用。守卫 `ResourceProductionGuard` 补四步顺序与卸载口径，7 条反向验证。这条回退只有 `ItemFactory` 取不到图时才走到，正常游玩触发不了，只有 L1/L2。

**替 owner 定的取舍（可回退，回退办法见设计文档第 7 节）**：
1. 押物品上限 6 件、合计 1,000–50,000，与押钱同一量级；只管一场（赢了不自动接着押同几件）。
2. 输了时押上的东西找不到按估值扣钱，扣到 0 为止（不欠债）；余额不够抵的差额收不回，这是已知的残余口子，要专门在看台上开背包丢东西才能钻。
3. 中断不退押注：真崩溃的玩家也要重打这一场来结清，但结果只由重打决定，不判负。

**验证**：
- `python tools/run_guards.py` 全量 664/664 PASS。`ModeHCashBetGuard` 重写（押物品、押注沿用，16 个内存变异探针）；`ModeHIsolationGuard` 登记白名单第四条并新增 `check_item_bet_stake`；`ModeHStructureGuard`、`ModeHLocalizationGuard` 同步。
- 新执行回归夹具 `tests/fixtures/ModeHItemBetLedger`：从生产逐字抽取账本的押物品记账 / 结算 / 退回与赔付公式，钱包替身，核对押物品赢输退各动多少钱、扣钱不低于 0、校准只降不升、期望为负；三处落盘变异（退回动钱、赢了发全额、校准取 min）都转红，按字节还原。
- `python tools/run_runtime_regressions.py` 57/57 PASS（`AuditModeLifecycle`、`NpcAuditFixes` 是 net10 夹具，本机临时改 net8 跑、跑完按字节还原）。`ModeHMarketAudit` 替身补 `IsCancel`。
- Windows 正式构建 Build succeeded，`Build/BossRush.dll` 与游戏目录同为 `6568F87E…`；`check_dll_identifiers --expect absent` PASS。
- `npm --prefix wiki-site run build` 通过；Wiki 守卫 15/15；`PlayerFacingGlyphGuard` 抓到维基里一个 U+2212 减号，已改成「减」。
- 证据级别 L1 + L2；**未实机**。实机清单见设计文档第 8 节第 6–10 步与本轮交付回复（第 10 步的限制已被第三轮取消）。

**没做的**：上一节列的四件都已做完。押钱赢率仍要实机跑够场次再调表。

## 2026-09-24 UI 共识全量修复 + 鸭王杯押钱（COMPAT / SCHEMA+）

**起因**：同日《UI 共识对照审查》（`docs/reports/reviews/2026-09-24-UI共识对照审查.md`）列出 A-01…A-43、B-01…B-34。owner：「那就全部修复吧」，并当场拍板四项待定：许愿台默认实名保持；重铸不加确认；鸭王杯押注「不是有意这样设计的」，改成玩家自己选押多少钱、「看比赛输赢，按赔率抽水」、总体下来玩家的钱慢慢往下掉；天空岛光色交互对玩家开放。

**分工**：系统 / 集成类（A-01…A-39、A-43）与模式类（丧尸、宿命回响、血猎、天空岛、遗种巢 A-40/A-41）各一个修复代理；鸭王杯全部 B 条、共享确认框、押钱由主会话做；Wiki 正文一个代理。主会话复核三份报告后统一编译、跑全量守卫与执行回归。

**修了什么**（逐条见审查报告第九节）：
- 共享确认框 `Common/UI/BossRushConfirmDialog.cs`（新）：问句标题 + 对象 + 后果、左确认右取消、危险确认 Danger 实心、ESC = 取消、可挂 Anchor 随宿主关闭。用在词缀解锁、Boss 池重置、鸭王杯放弃赛季 / 战痕替换 / 押仓库物品锁盘；阿稳的官方确认框在不可逆模式下换共享 Danger 键。
- 四条已核 P1（CR-2026-09-24-001…004）全部 Fixed：词缀解锁先确认、远征翻牌跳过收成汇总、丧尸撤离宿主受理后才还租约、鸭王杯放弃赛季先确认。
- 系统类：重铸 / 词缀按钮写价钱、费用区两行、白话倾向；许愿台 / 日报 / 图鉴 / 成就字号收级、不挂灰按钮、状态标签代替灰按钮；Boss 池 ESC 与 × 同一出口（先存再关）、两页签；百科外链进页眉、淡入淡出；删好感面板死代码；成就 / 图鉴 / Boss 池 / 百科改走模态租约。
- 模式类：丧尸现金投入去「跳过」、整卡可点、ESC = 继续战斗；宿命回响默认选中契约、「N 阶」、HUD 反制进度条；血猎雷达 token；天空岛退单 / 互斥挑战进确认子页、手记当前栏常亮。
- 鸭王杯：侦察 / 押注进正文选项行（`ModeH/ModeHUIPageRows.cs`，新），就地失败行，Danger 只在主操作上实心；整备四页签不分页；战痕与整备奖励改卡片；恢复壳占模态租约 + 可滚动正文 + 「稍后处理」；零战报结算给「查看恢复选项」；名人堂「结束赛季」+ 立绘；看盘 / 赔率 / 选人页组装挪到 `ModeHRuntimeModule_MatchPages.cs`（新，MatchFlow 行数预算）。
- **押钱**（设计与回退：`docs/design/鸭王杯押钱_2026-09-24.md`）：仓库只在基地场景存在，原真实押品链在比赛里恒不可用，押品选择器不再画。改为押 0 / 1,000 / 5,000 / 20,000，锁盘落盘后从账户余额扣；赢了拿回 `押金 × 920 ÷ 假定胜率‰`（x1…x5 假定 85/70/55/42/30%），满 20 场后取 max(表, 实际胜率) 只降不升；技术中止、放弃赛季、切图中止原样退回。账本 `BossRush_ModeHCashBet_v1`（`ModeHCashBetService.cs`，新）照 `AchievementRewardJournal` 与现金快照同批落盘，Reserved → Settled / Refunded 至多一次；入场「开盘」揭晓 `ModeHBetRevealView.cs`（新，不挡操作，由宿主 Tick 驱动）。风险提示改押钱口径。赛季 DTO 不动。
- Wiki：`mode__mode_h`（押注整节重写、结算 / 整备 / 恢复 FAQ）、`mode__mode_g`（默认选中、「N 阶」）、新手路线的押品警告；在线站速查框（`infobox.mts`）Mode H 的「你能做的 / 地图」两行按现行代码改。

**兼容性**：COMPAT；押钱账本是新增本槽 typed 键（SCHEMA+，旧档读出无押注）；新增本地化 key `Notify_ExtractionAreaFailed`；风险提示 key 不变只改值。无 TypeID、无破坏性存档改动。新文件 6 个 .cs 均已登记编译清单（`ModeHUIPageRows`、`ModeHRuntimeModule_MatchPages`、`ModeHRuntimeModule_BetFlow`、`ModeHCashBetService`、`ModeHBetRevealView`、`BossRushConfirmDialog`）。

**替 owner 定的取舍（好玩优先 / 主流口径，均可回退）**：
1. 押钱档位、抽水 8%、假定胜率表、20 场校准门槛——都在 `ModeHConfig`，改表即回退；只关玩法把 `CashBetAmounts` 改成 `{ 0L }`。「玩家赢钱概率 40%」取决于实战胜率，需实机跑够场次看分档统计再调。
2. 成就 / 图鉴 / 百科改走模态租约后，局内打开会停住游戏时间（与官方 ESC 暂停、遗种巢等面板一致）。回退：三处 `ClaimModalInput` 换回 `InputManager.DisableInput`。
3. Boss 池一个都没勾时关不掉（ESC 也不行），统计行写明原因；× / ESC / Ctrl+F10 统一「先存再关」；切页签保留滚动位置。
4. 日报品质 1–6 用官方物品框颜色名，7 及以上统一「顶级」。
5. 丧尸撤离页两张卡对调（主操作在右）；开局流派页 ESC 只吃掉按键（必选、无取消）。
6. 宿命回响选中态改金色；钟守「挑战守钟装置」同样加确认。
7. 遗种巢一次翻两张以上自然翻完也收汇总；改名提交的名字等于默认名时存成「没起名」（换语言跟着变）。
8. 放弃赛季的确认正文写明「押的钱原样退回，旧档托管的仓库物品也还回」。

**验证**：
- `python tools/run_guards.py` 全量 664/664 PASS。新增 `tests/UIConsensusSystemPanelsGuard.py`、`tests/ModeHCashBetGuard.py`（9 个内存变异探针）；`ModeHStructureGuard` 新增 `check_irreversible_confirms`（三处落盘反向验证转红、按字节还原）；`UILayoutReadabilityGuard` 的 Mode H 卡片断言改读「基准 - shift」并新增立绘分支几何（新探针转红）；`ModBehaviourInstanceClassificationGuard` 基线 Integration 263 → 266（百科淡出协程挂宿主 +3，分类文档同步）；其余十余个守卫由两个修复代理同步改写并做了落盘反向验证。
- `python tools/run_runtime_regressions.py` 56/56 PASS（`AuditModeLifecycle`、`NpcAuditFixes` 是 net10 夹具，本机临时改 net8 跑通后按字节还原）。本轮同步：`ManualSeptemberReview` 抽取 `CountRecords` 并新增多张汇总断言；`AffixSelectionUI` 抽取 `FormatReforgeAmount`、按钮文案断言带价钱；Mode H 五个夹具与 `GameplayLogFixes` 补押钱钩子替身。
- Windows 正式构建 Build succeeded，`Build/BossRush.dll` 与 `D:\software\steam\...\Mods\BossRush\BossRush.dll` 同为 `9FB5C190…`；`check_dll_identifiers --expect absent` PASS。
- `npm --prefix wiki-site run build` 通过，Wiki 守卫 15/15。
- 证据级别 L1 + L2；**未实机**。

**没做的**（同日第二轮已全部做完，见上一节）：押背包物品（要另做托管，owner 说「或者直接砸钱也行」，本轮只做押钱）；鸭王杯各模态页没接 ESC（多数页没有「取消」语义，吞掉 ESC 会打不开暂停菜单，留实机看）；看图器回退路径重复加载 bundle（`ImageViewerUI.cs:433`，仍待实机）；遗种巢放生 / 亡命出发两个自写确认框没迁到共享件（共识文档已注明是先例，行为已守卫）。

**文档**：审查报告第九节（修复状态与第八节去向）；`CODE_REVIEW_FINDINGS.md` CR-2026-09-24-001…004 → Fixed；押钱设计 `docs/design/鸭王杯押钱_2026-09-24.md`；repowiki Mode H 专题（现行「押钱」一节）与架构设计补记；`docs/architecture/UI制作共识.md` 确认弹窗改「已共享」；根 `AGENTS.md` §4.14 交互骨架一句、`docs/ai-docs-migration.md` 同步。

## 2026-09-24 遗种巢 UI 交互重排：列表 + 详情、按钮跟着对象走（COMPAT）

**起因**：owner「我们现在一股脑把所有功能都做成按钮丢出来，不符合最佳 UI 交互原则」，要求参照主流软件与 Apple 的交互口径给方案并实施。方案对照 Apple HIG（Split View / Sidebar、破坏性按钮远离安全按钮）、iOS 照片「选择」模式、宝可梦 HOME 的盒子 + 概要，以及仓库 §4.14。

**诊断（8 条）与处理**：
1. 每张崽卡挂「设为出战 / 改名」两颗按钮 + 五行正文 → 巢页改「列表 + 详情」，行上不放按钮，信息与操作进右栏详情（`PetNest/PetNestUINestPage.cs`、`PetNest/PetNestUILayout.cs`）。
2. 「选中」是看不见的跨页隐式状态（远征页要「先回巢选」）→ 详情底栏「派去远征」带着崽跳到远征页；远征页自带「派谁去」头像小卡。
3. 底栏混放单只操作与全局操作、红色实心放生常驻 → 详情底栏只放作用在当前崽上的操作：放生红描边红字靠左，「设为出战」是唯一 AccentFill；「不带崽出门」改成出战崽详情里的「取消出战」。
4. 灰掉的占位按钮（不带崽出门、已出战、远征中、放生）→ 不挂，远征中的崽底栏写剩余时间。
5. 捡漏背包 / 保底 / 扩建说明排在 24 张卡之后 → 出战席位格副行 + 出战崽详情 + 页眉「说明」页。
6. 孵化页同血脉蛋各占一张卡、凝蛋按钮在底栏 → 按血脉合并「×N」；遗魂账本一行一条带进度条，够数的行内挂凝蛋。
7. **亡命档（真死）点「出发」立即执行、没有确认** → 新增 `PetNest/PetNestDepartConfirmModal.cs`；远征页改一屏选完（目的地分段按钮默认落在契合的 + 风险档三列对比卡 + 一个「出发」），删掉三张目的地卡里一模一样的正文。
8. NPC 交互菜单 4 项与面板页签重复 → 「孵化」「远征」按有事可做显隐（护士治疗项的同一模式，巢是交互主体时不改列表），博物馆项去掉。
另：页签带待办数字（「孵化 ·2」）；碑文从一长串「·」改成分行。

**兼容性**：COMPAT。服务层入口、存档、TypeID、本地化 key 都没动（`Release_Action`、`CondenseProgress`、`BossRush_PetNest_Interact_Museum` 三个 key 不再使用但保留）；`PetNestMuseumInteractable` 是运行时 AddComponent 的类型，不进存档或 bundle，删除安全。

**决定（好玩优先 / 主流口径，可回退）**：远征目的地默认选中有元素契合的那个、风险档默认稳妥；选中态沿用 WarningText 描边；站在巢边孵完蛋后「孵化」菜单项要走开再回来才消失（为避开官方菜单中途增删错位，点它仍能打开孵化页）。回退：`git revert` 本次提交即可，数据层无迁移。

**验证**：
- `python tools/run_guards.py` 全量 662/662 PASS。改写 / 新增断言：`PetNestUILayerGuard`（画法与组装文件不建 canvas、亡命出发必须先弹确认、出战失败回写 NoteFailure）、`PetNestBuilderInjectionGuard`（子选项只在巢不是交互主体时刷新）、`UILayoutReadabilityGuard`（两栏间隔与安全边距、行文字避让按钮列与勾选框、单行框高，+3 个内存反向检查）、`PersistentHudVisibilityGuard` 登记新弹窗。反向验证：5 处生产代码人为破坏全部转红、按 sha256 还原。
- `python tools/run_runtime_regressions.py --filter ContentTransactions` PASS（替身补 `AppendPetPicker`、血脉目录 `All`；抽取新增数据类）；`ManualSeptemberReview` PASS。
- Windows 正式构建 Build succeeded，部署到 `D:\software\steam\...\Mods\BossRush\BossRush.dll`，sha256 与 `Build/` 一致（`54EBDF56…`，含下面「复核补修」），编译输出里 PetNest 文件零警告。
- **复核补修（同日）**：交互菜单不在 Start 时算显隐（背包 / 仓库未就绪会把「孵化」误藏），巢不可用时不动子选项；已核对官方 `CA_Interact.SearchInteractableAround` 在选定主体前先调 `CheckInteractable`，玩家每次走近刷新一次。批量模式下远征中的崽不画勾选框；「说明」页放生一节标题改成「放生」；详情底栏走表只挂在还在路上的远征上（到点未结算时写「打开天灾远征页结算」，不再每秒整页重建）；底栏按钮量完宽度后重新打开自动缩字（避免长英文标签被 Ellipsis 清空）；危险次级按钮改红字。
- 文档：设计与验收记录 `docs/design/遗种巢UI交互重排_2026-09-24.md`；通用做法沉淀为 `docs/architecture/UI制作共识.md`（`.gitignore` 放行），根 `AGENTS.md` §4.14 新增「交互骨架」一条指向它。
- 其他界面对照共识的首轮审查（只审不改）：`docs/reports/reviews/2026-09-24-UI共识对照审查.md`，4 条已核 P1 登记为 CR-2026-09-24-001…004（同日已全部修复，见上一节「UI 共识全量修复」）。
- 证据级别 L1 + L2；**未实机**。实机清单见 `docs/design/遗种巢UI交互重排_2026-09-24.md` 第 7 节。

## 2026-09-24 续接人工复查与鸭王杯 F3 认证收尾（COMPAT / SCHEMA+ / WIRE+ / OPERATIONAL）

承接会话 `01a0ce08-0491-77b1-98a3-b0bbce929093`。普通新赛季、续赛及 Dev 构建均直接检查发布目录后选人；动态认证仅由 F3「玩法验收 → 鸭王杯逐项认证」显式启动，要求专用测试档且停在选人页。关闭 F3 后再释放选人页暂停；完成、失败、取消均回收诊断选手并恢复原页面，不重抽候选、不写赛季或认证缓存。复用 `ValidationCoroutineStack` 处理嵌套异常与取消，旧 owner 回调不能干扰新认证。自动验收改为普通入场和再次入场用例，同步覆盖清单。补修发布支持状态导致整备页漏掉口令说明，说明与候选列表共用兼容判据；取消认证不再误报通过。

一并收齐上轮菜地横幅、遗种巢灯光与宠物容量、日报、天空岛招牌移除及鸭王杯交战/拍铃修复的代码、夹具和专题资料。菜地补丁新增的两处宿主取值按 Unity owner 归类，计数守卫与分类文档同步。

验证（L1/L2）：全量守卫 663 PASS；最终补修后 167 项相关守卫 PASS。58 组隔离回归全部通过，其中 ModeHPlayerFlow 153 条、菜地横幅 76 条；新改守卫的稀疏副本反向验证转红、按字节恢复后转绿。Windows 正式版和 Dev 版编译成功，仅既有 RuntimeGate CS0649；14 个 Dev 专用标识在正式版缺席、Dev 版齐全。正式 DLL 已部署且 SHA-256 一致：`6377310021E1F3DC66D16620F5D1E1FB7B9980A94692C85436CE5F79D2B8FBAE`；72 个包部署哈希核对通过。Wiki 构建、80 项导航与 237 页链接检查通过（0 缺失、0 断锚）；额外图片总表检查发现本机已有 51 份图鉴源图尚无对应站点产物，当前页面引用检查无缺失，本轮未扩展站点图库。

环境说明：使用自带 Pillow 的 bundled Python；本机仅 .NET 10，夹具以 `DOTNET_ROLL_FORWARD=Major` 运行，分配测量关闭分层编译；三组官方绑定夹具显式指定实际 Harmony 路径后通过，未放宽断言。日志及聚合结果在 `Build/thread-resume-20260924/`。无 L3、未启动游戏或读写玩家存档；复测操作与看图清单见 `docs/reports/testing/20260924会话续接与提交记录.md`，其余六项沿用 `20260923人工复查续修记录.md`。

## 2026-09-23 菜地收获完成横幅（COMPAT / WIRE+）

owner 要求收获时给玩家横幅。新增 `GardenHarvestNoticePatch`，只在官方 `Crop.Harvest` 的唯一 `Cost.Return → Forget` 之间等待已有交付任务；原版与 Mod 作物均显示名称、数量，以及基地仓库/满仓马蜂自提点说明。任务失败不误报到账，换槽/换主角/切图/停用/卸载后不迟发，正常 Crop 回收不吞提示。保留原发货、清格和异常消费者，无新存档字段或每帧开销。中英 Wiki 与后山专题已同步。

L1/L2：157 相关守卫、76 条隔离回归通过，6 个反向探针按字节还原；官方实际 IL 匹配确认，Windows 正式编译通过（仅既有 CS0649），Dev 标识缺席，Wiki 构建/导航 80 项通过。正式构建脚本曾被自动审批以 blocked by policy 拒绝，已用仅写工作区的编译方式产出正式 DLL `E9F22EE5…2A0D5EAF`；owner 退出游戏后已定点部署 DLL 与中英百科 3 文件，并逐一核对 SHA-256。详见 `docs/reports/testing/20260923菜地收获横幅修复记录.md`。

## 2026-09-23 人工复查六项续修（COMPAT / SCHEMA+ / OPERATIONAL）

承接 `docs/reports/testing/20260922人工实测发现的问题-复核记录.md` 的六条新复查。冰霜作者资源已改但四端仍旧包，已单独重打、回读并正式部署；遗种巢罩灯补暖光；日报去关闭按钮、悬赏全文显示、修复旧图标叠画与反向淡出曲线；删除天空岛独立招牌；Mode H 正式入口免动态认证，追加保留旧值的 ReleaseSupported，补对手目标与每场拍铃重开；宠物扩容等待官方完整就绪，按实际 Item/Inventory owner 同步与回收，切图等安全箱快照加载，官方 Push 提交后才清理实物。

证据 L1/L2：663 守卫、57 组隔离回归全绿，Windows 正式编译成功（既有 RuntimeGate CS0649），14 个 Dev 专用标识缺席；72 包源/目标哈希一致且实际读包 0 错误；25 个 DLL/数据/散图/Wiki 文件核对。Wiki 构建与 80 项导航通过。Pillow 缺失与 .NET 10 分层编译分配计量的环境失败已在正确依赖、关闭分层编译后通过原断言，无放宽。最初游戏占用 DLL 部署失败，owner 回复已退出后完成部署；DLL `D052898A…69DBE8706`，frost_set `A3236664…4FA4E553`，production_icons `031EE280…6E485055`。

详细原因、完整哈希、反向验证、回退资源及六条实机操作清单见 `docs/reports/testing/20260923人工复查续修记录.md`。尚无 L3，未采样性能；宠物此前未扩容的具体实机场景待日志核对。回退用本次 `Build/manual-recheck-20260923/loose-before`、DLL before 文件和资源部署器生成的备份；灯光可撤掉 Attach 调用，Mode H 可恢复原入口分支与配套守卫。

追加菜地核查（SAFE，L1）：官方收获经 Cost.Return(false,false,1,null) 直接送基地仓库，同类可静默叠加；满仓进入马蜂自提点「待取件」，腾空后点「发送至仓库」。Mod 三种作物产物与数量有效，未发现拦截官方收获链的补丁，未确认本次实际丢物。补中英百科去向说明；详细核查与复测见上述续修记录。

## 2026-09-23 玩家文案去「人机感」打磨（SAFE / COMPAT，纯文本）

**起因**：owner「优化 mod 剧情、UI、物品 / 装备描述的文字，不要有人机感」，并追加「Wiki 正文一起改」。

**范围与规模**（只改字符串值；key、占位符、数字、专有名词、标题、链接、catalog.tsv 未动）：
- 游戏内：剧情与对白（护士 / 叮当好感、捏脸 NPC、婚礼、天空岛居民 / 地点 / 信件 / 手记 / 头目、征程提示、宿命回响）约中 134 / 英 191 条；物品 / 装备 / UI（装备与物品 Desc、遗种巢、许愿台、日报、Mode E 物品）中 33 / 英 37 条。
- Wiki：`WikiContent/zh|en` 的 boss / npc / mode / tips / start / map / easter / config 约 239 处，item / equipment / system 约 310 处，`wiki-site/hubs/` 各一份。跳过 `changelog__*`（历史记录）与 `mode__mode_h.md`（另一会话在改）；成就文案（Steam 登记）不改。
- 逐条清单（文件:行号 | 原文 | 新文 | 原因）在本会话 scratchpad 的 `changes_A*.md` / `changes_B*.md`，未入库。

**顺带按代码改正的 Wiki 数值 / 机制**：遗种巢远征 2/4/8 小时 → 10/30/60 分钟（`PetNestTuning.cs:180-187`）；词缀熔石叮当解锁 Lv.2 → Lv.10（`GoblinAffinityConfig.cs:805`）；噬魂挽歌 / 幽灵诅咒改为每层 -30%、叠满 -90%、不定身（`PhantomWitchAssetManager.cs:336`、`:362`）；雷神套装对比段改成普攻附带 + 内置冷却；阿稳扫箱令标准 BossRush 也可用（`ModeEFLootboxTracker.cs:84`）；许愿成功横幅已不存在；平安护身符「多一条命」改为实际机制；征程第 2 章是陈列加成；Mode E BEAR ×2.5 是阵营单位血量伤害；英文入门页的旧征程口径与建筑名。游戏内：逆鳞描述补上 50% 与 8 颗（`HealPercent` / `PrismaticBoltCount`），冷淬液锁的是属性不是部件，安神滴剂英文 all → most。

**没改**：`LocalizationInjector.cs:840-848` 丧尸奖励同义反复（`ZombieModeRewardPlainTextGuard` 逐字钉住）；霜之哀伤「低语」（魔兽梗）。Wiki 两处自相矛盾未定：龙裔一阶段「每 10 发」爆炸范围（攻略页 1 m vs Boss 页 5 m 内 5 点火焰）、无间炼狱专属装备是否掉落（中英说法相反）。

**Needs owner confirmation**：平安护身符新描述「伤重时偶尔能让你缓过来」点破了隐藏机制；许愿台「写个愿望投进去，有人看」；共用花心台词对叮当串味；护士 9 级 / 「手都是抖的」等由深情改嘴硬的几句；系统拒绝提示改口语。

**验证**（L2，无 L3）：全量守卫 662 PASS / 0 NEW-FAIL；语法探针 PASS（语法通过，未正式编译：另一会话当时有未登记新文件且编译会自动部署）；执行回归 SkyIslandDialogue / Story / Interaction / Encounters / Loot / Delivery / Marriage、CampaignPlayability、RandomEventTempo / Failure、ModeGCombat、PermanentDuckNpcDialogue 全部 PASS；天空岛改过的居民台词用 `tools/sky_island_line_screens.py` 核过屏数不变；`npm --prefix wiki-site run build` 成功。实机只能看观感：进基地找护士 / 叮当送礼、看装备 tooltip、天空岛与居民对话，看有没有截断或串味。

<!-- BEGIN AESTHETIC AUDIT 2026-09-23 -->

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

## 2026-09-23 离线语法探针漏检编译清单文件（OPERATIONAL）

**问题**：`tools/verify_syntax.py` 自带一套 `echo(...\.cs` 正则读 `compile_official.bat`，
吃不下清单里残留的 `^` 续行写法（第 265-268 行：`echo(A.cs ^` 后面跟三行缩进路径）。
cmd 会把这几行拼成一条 echo，写进响应文件后 csc 按空白切参数，**正式构建照常编译这四个文件**
（`Build/bossrush.rsp` 第 191 行实测就是一行四个路径）；但探针把 `echo(...SkyIslandJournal.cs ^`
整行丢掉，`DebugAndTools/SkyIsland/SkyIslandJournal.cs` 从来没被离线语法检查过，
而且探针既不报错，也不显示 979 与 980 的差。`tools/gameplay_coverage.py` 的第三套正则更盲，
只看见 976 个（那四个文件都漏），只是它用的域集合恰好被同目录其它文件覆盖，暂未产生后果。

**修复**：

- 新增 `tools/compile_list.py`，作为编译清单解析的唯一实现，规则与
  `OfficialCompileListFileExistenceGuard` 原有正则逐字相同（实测两边同为 980 个源、零差集）。
- `tools/verify_syntax.py`、`tests/OfficialCompileListFileExistenceGuard.py`、
  `tools/gameplay_coverage.py` 全部改为 import 它，删掉各自的正则。
- 探针在启动 csc 之前核对「写进响应文件的集合 == 清单集合」，不一致就点名漏检/多检文件并 FAIL。
- 新增 `tests/SyntaxProbeCompileListParityGuard.py`：外部钉同一条等式，并用 AST 挡住
  「探针重新长出自己的 `.cs` 正则」与「响应文件核对被摘掉」。
- `compile_official.bat` 未改（保持 CRLF，那几行本来就能正确编译）。

**验证**（证据级别 L2，未做 Windows 正式编译）：

- 探针：修复前 979 源，修复后 980 源，`--with-bcl` 仍 PASS（语法层 CS1xxx 零错误）；
  `SkyIslandJournal.cs` 首次被语法检查，无错误。
- 全量守卫 656 PASS / 0 NEW-FAIL / 0 KNOWN-RED。
- `gameplay_coverage` 换解析器后域集合不变（70 -> 70，零差集），四条相关守卫仍 PASS。
- 反向验证（稀疏副本上做，每次按字节还原并核对 sha256，5/5 按预期转红）：
  探针漏检一个文件 -> 新守卫点名 `SkyIslandJournal.cs`；探针重新自带 `.cs` 正则 -> 新守卫红；
  摘掉 main() 里的核对 -> 新守卫红；共用正则退回看不见 `^` 续行 ->
  `OfficialCompileListFileExistenceGuard` 兜底报 3 个 omitted；
  在写 rsp 处插过滤 -> 探针自身 0.2 秒内 FAIL（csc 未被启动）。

<!-- END SYNTAX PROBE COMPILE LIST PARITY 2026-09-23 -->

<!-- BEGIN MANUAL 16 FIX 2026-09-23 -->

## 2026-09-23 修复 20260922 人工实测 16 项（COMPAT / SCHEMA+ / WIRE+ / OPERATIONAL）

对应 owner 清单 `docs/reports/testing/20260922人工实测发现的问题_修复记录.md`。无人值守交付，按「好玩优先」授权直接拍板的取舍与回退写在 [修复记录](docs/reports/testing/20260922人工实测发现的问题_修复记录.md)。

### 各项修复

- **1 冰霜套装**：霜冠校准表改为 `[24,30,24]` / y 0.29，包内实测 0.458 m 宽，小于鸭头 0.49 m。寒冰铠甲 prefab 子节点改为 `(58,64,38)` / y −0.035，盖住腹部。Unity 重打后只有 `frost_set` 变化。
- **2 报箱 / 遗种巢**（CR-2026-09-23-005/006）：
  - 发灰的原因是天空岛环境着色器在基地不受光；遗种巢缺碰撞的原因是建预制体时从来没补实体碰撞体。
  - 新增 `BuildingModelHelper.PrepareBaseBuildingModel`，照许愿台的做法：材质换官方 SodaCharacter，并补实体 BoxCollider。
  - 报箱的碰撞与许愿台同码，离线未找到缺碰撞的原因，记为 UNVERIFIED。
- **3 词缀名消失**（CR-2026-09-23-001）：TMP Ellipsis 在第一行都放不下时整串清空。名字行按实测行高给足，新增守卫 `AffixForgeNameRowGuard`。
- **4 日报**（CR-2026-09-23-002/003/004）：
  - 图例只剩色块（同一 TMP 机制）→ 行高改 36，改为五项。
  - 标题药丸灰方块（alpha 块在画布上挖了洞）→ 改画不透明图标。
  - 战绩表露半行 → 两列排。
  - 另外：圆形彩色徽章、去掉内框、报名放大、今日格高亮。
  - 底图经 IconsBuild 重打进 `production_icons`，会话开始时红的 `DailyReportArtPropertyTest` 转绿。守卫新增兜底版面同源检查。
- **5 船点多两个交互圈**（CR-2026-09-23-007）：鸭王杯 / 天空岛选项自己关世界标记，天空岛选项另外关交互碰撞体。
- **6 Mode H**：
  - 认证缓存去掉每次启动都会变的选档计数（CR-2026-09-23-010），加载页改成白话；
  - 只剩一个选人页（立绘 + 白话），选完自动开打；场间只剩一个按钮；
  - 开打时镜头跟随己方选手，Raid 图临时放宽战争迷雾（WIRE+）；
  - 拍铃卡挪到状态卡下方并重做；
  - 「人数区间」改为「场上敌人」（CR-2026-09-23-012）；
  - 新增守卫 `ModeHOneClickFlowGuard`。
- **7 / 10 / 11 / 12 遗种巢**：
  - 卡片显示炫彩色块与异色标记，巢页写保底进度与捡漏背包格数；
  - 点卡片即选中，支持批量放生（`TryReleasePets`，单个候选包，全放或全不放）；
  - 共享按钮新建时不再从白色淡入（CR-2026-09-23-008，F-21 共享根因），同页重绘保住滚动位置；
  - 「黑」的文字色压到 #858B95；
  - 异色名字加流光。
- **8 保底**（SCHEMA+）：10 枚必出炫彩、100 枚必出异色，计数存在巢数据的两个可选字段里，只在孵化事务里推进。执行回归 `PityGuarantees` 覆盖，并有反向探针。
- **9 / 12 崽身特效**（CR-2026-09-23-017）：
  - `PetNestAuraEffect` 重写，新增 `PetNestAuraRecipes` / `PetNestAuraTextures`：十色各有元素样式，赤色克隆官方火 AK 的龙息火焰；异色是双层金符文环、金星、光冠、呼吸点光；
  - 按崽尺寸缩放，固定发射率，粒子上限 60 / 120；
  - 新增守卫 `PetNestAuraEffectGuard`。
- **13 阿稳扫箱**：旧箱静默寄快递，只弹一条横幅。只寄扫出来的物品，玩家自己塞进去的东西退回背包。失败时退回逐件交付。
- **14 提价**：只动掉落专属、明显偏低的物品。遗种蛋 3,200 → 20,000，龙系五件与龙皇铳下限上调；商店商品不动。
- **15 菜园**：
  - 种子掉落不再受「Boss掉落随机化」开关影响（CR-2026-09-23-014）；
  - 基地售货机上架三种种子；
  - 起步种子各 2 颗，存档键 `BossRush_BackMountain_StarterSeeds_v1`（SCHEMA+）；
  - 第四章文案改为「在基地吃一份」。
- **16**：截图实为 Mode G 入场页。
  - 徽记不再压字，契约卡加边框与选中态，未选契约时不能开战，取消按钮改为中性的「暂不挑战」；
  - 中文「Resolve」统一改叫「决意」。

### 验证

- 全量守卫：**655 PASS / 0 FAIL**。
- 全量执行回归：54 PASS。`AuditModeLifecycle` / `NpcAuditFixes` 写死 net10.0，本机只有 .NET 8 SDK（NETSDK1045）；临时换成 net8.0 后两组 PASS，runner 已按字节还原。
- Windows 正式编译通过，DLL `117DC6097AD30DCA9890D995ED1471A3651EC27985D1D5FFEA41D8F2E59EB6F5` 已部署，游戏目录一致；`check_dll_identifiers --expect absent` PASS。
- 资源：72 包发布校验 0 错误，部署 VerifyOnly 72 包一致。
- Wiki 构建与 80 项导航通过。
- 新增或修改的断言均做了反向验证。

### 没做的

- 没有启动游戏、没有读写存档，**无 L3**。实机清单共 15 步，见修复记录文末。
- 作者工程与本仓库都未提交。工作区里另一会话的 `docs/contracts.md` 与两份设计提案未动。

<!-- END MANUAL 16 FIX 2026-09-23 -->

<!-- BEGIN FULL AUDIT FIXES 2026-09-22 -->

## 2026-09-22 全仓审计报告修复闭环（COMPAT / SCHEMA+ / OPERATIONAL）

原报告 82 项均已逐条复核并关闭；另从未验证线索确认并修复 2 项，共 84 项。其中 75 项在会话开始时已有对应修复，经本轮复核保留；其余 9 项为本轮补修或新增确认。分类为 COMPAT，成就领奖凭据与 Dev 恢复快照为 SCHEMA+，正式构建部署为 OPERATIONAL。

全量守卫 652 PASS / 0 FAIL；全量隔离回归 56 PASS / 0 FAIL；Windows 978 源正式与 Dev 编译通过。正式 DLL 已部署，Build 与游戏目标 SHA-256 一致，14 个 Dev 专用标识缺席；72 个资源包部署哈希检查通过。Wiki 构建和 80 项导航检查通过，237 页 / 39133 个引用无缺失链接或失效锚点。

正式 DLL SHA-256 `f58dea9cca4d4da50e3ab77d08a21790eb2d4e6b89552f5661f2293ed1981a0f`，Dev 构建仅留在验证目录。初始已有修复与本轮补漏分开记录；本地提交按审计修复范围收录，未推送。

里程碑高阶奖励采用原工作区已有的完整标价折现策略：每阶最多 100 皇冠 + 100 合法现金堆，超额入账户，每帧最多 8 实体，long 饱和防溢出；理由与回退约束见交付记录。

没有启动游戏、没有读写玩家存档、没有 L3；具体操作和 F3 看图清单见 [修复闭环](docs/reports/reviews/2026-09-22_full_audit_fixes.md)。原 82 项与新增两项全部回填状态；仍缺实机触发证据的线索保留未验证。

提交前复核：978 个生产源码与通过正式/Dev 编译的 SHA-256 清单一致，7 个新增生产源码均已登记。AuditModeLifecycle 的支援弹夹具改用保留官方零默认值的最小 ProjectileContext 契约替身，生产 builder 仍逐字抽取，移除对未纳管反编译源码的依赖后专项回归通过。提交仅收录本次审计修复、验证和台账，独立架构计划改动保留在工作区。

<!-- END FULL AUDIT FIXES 2026-09-22 -->

## 2026-09-22 实机启动报错：资源加载器路径键不一致导致全部 Mod 物品注册失败（COMPAT）

- 现象（owner 实机，`Player.log` 12:38）：启动进基地刷 47 条 `The AssetBundle '…' can't be loaded because another AssetBundle with the same files is already loaded`（bossrush_ticket、birthday_cake、ui/bossrush_wiki 与 Items/、Equipment/ 下全部 bundle），随后 `PlayerStorage.Load` / `LevelManager.CreateMainCharacterAsync` NRE（仓库与角色身上的 Mod 物品拿不到 prefab）。
- 根因（CR-2026-09-22-002）：`60bb84b6`（09-20）引入的异步加载器 `ResourceBundleLoader.Prepare` 用 `Path.Combine(GetModPath(), "Assets/Items/x")` 做 `pending` 字典键，而 `ItemFactory` / `EquipmentFactory` / 按需注册用 `Path.Combine(modDir, "Assets", "x")`，同一文件两种写法，字典查不到 → 同步兜底对已异步加载的 bundle 再调一次 `AssetBundle.LoadFromFile` → Unity 拒绝并返回 null。该提交之后没有实机跑过，本轮征程改动首次启动才暴露；不是征程改动引入。
- 修复：`ResourceBundleLoader` 的 `pending` 键统一经 `NormalizeKey`（`Path.GetFullPath`，异常时只换分隔符），`Pending.Key` 记录归一化键；`ShowcaseTagInjector` 取 prefab 改走 `BossRushDynamicItemRegistry.GetRegisteredPrefabWithoutEnsuring`（原来经补丁过的 `ItemAssetsCollection.GetPrefab` 会对每个 TypeID 触发同步按需注册，CR-2026-09-22-003）。
- L2：`ResourceProduction` 夹具新增「分隔符 / `.` 别名命中同一租约且不发第二次原生加载」用例，先在旧加载器上转红、修后转绿；`BackMountainPlayabilityGuard` 新增「取 prefab 只走 WithoutEnsuring」断言并反向验证；`BackMountainLifecycle` 回归绿；改动相关守卫绿（唯一红仍是本轮之前就红的 `SkyIslandMosquitoGuard`）。正式构建通过，游戏目录 DLL SHA-256 `8286B630…` 与 `Build/` 一致，`check_dll_identifiers --expect absent` PASS。
- L3：待 owner 重开游戏，`Player.log` 应无 `can't be loaded because another AssetBundle` 与 `PlayerStorage.Load` NRE。
- 复核（owner 12:57 Dev 构建跑 F3，`Player.log` 13:29）：物品 / 装备 bundle 的 47 条已清零，仓库与角色 NRE 消失，征程 / 后山用例 `DATA_CAMPAIGN_JSON` / `GARDEN_SITE_GATE` / `SHOWCASE_OFFICIAL_PROBE` / `BACKMOUNTAIN_SHOWCASE_DISPLAY` / `CAMPAIGN_FINAL_BOSS` 全 PASS。剩两条同类噪音与一条过期判据，本轮一起修：
  - 基地建筑 bundle（weddingchapel / starwish_fountain / petnest_relic_nest / bossrush_daily_mailbox）每次进基地各报一条同样的错（4 × 9 次）：装配管线每次进基地重跑 `RunSpecial`，而建筑自己还持有 bundle，`LoadFromFileAsync` 被 Unity 拒绝后消费方走「已注入，跳过」，功能无损但刷错（CR-2026-09-22-004）。`RunSpecial` 加可选 `alreadyLoaded` 判据，四处传各自持有字段（`DailyReportMailboxBuilder` / `PetNestBuilder` 加 `IsBundleLoaded`）；`ResourceProduction` 夹具加「持有中不发原生加载、释放后照常加载」用例；`PetNestBuilderInjectionGuard` 接线 token 同步。
  - `DATA_CODEX_FILTER_REFRESH` 红（official=45->45->45）：e80b1c3f（09-20 owner 拍板）起筛选器关掉的官方 Boss 由名单补成锁定卡，目录不再缩，判据过期。`CodexBossInfo` 加 `IsInCurrentPool`（池子给出=true、名单补的=false），用例改判「池子给出的少一格再恢复、目录不缩（名单读不出时允许少一格）」。
  - 主套件另一条红 `SKY_NIGHT_BOUNDARY_OFFICIAL`（官方 22–6 vs 岛上 19–5）与岛内 6 条红（截信人字幕、镰爪落点 EnemySpawn_C、断风风线、结局手记正文、英文居民 / 浮舟对白）都在天空岛头目 R2–R4 线（09-16 那轮已红 3 条），不属本轮，未动。`GamingConsole.Load` 的 NRE 是已知 P3（教堂整区重绘打断官方游戏机加载）；16 条 `[鸭鸭市场]` NRE 与 `casino_building` 缺 prefab 是别的 Mod。
  - 游戏目录现为 **Dev** 构建（owner 在跑 F3），交付前要换回正式构建。
- owner 纠正（同日）：官方「陈列柜」是废弃建筑，建造菜单里没有；能建的官方展示建筑只有枪械展示架（槽位要 `Gun`）、假人（枪 / 近战 / 头盔 / 护甲 / 面罩 / 耳机）与基地皮肤柜（CR-2026-09-22-006）。`ShowcaseTagInjector` 改为 `ShowcaseTrophyCatalog`：只判哪些 Mod 物品算战利品，**不再补任何官方展示标签**（Mod 枪甲自带槽位标签，探针已证能上架）；征程第三章目标 / 交付对话 / 线索 / 飘字 / 任务说明、后山互动提示、Wiki 中英与站点、覆盖表、交付文档、repowiki 全部改口为「枪械展示架 / 假人」。`Chapters.json` 与硬编码同步（签名互锁仍绿）。L2：全量守卫 645 绿，3 红都在 HEAD 上就红（`BaseBuildingResourcePropertyTest` 缺 lz4 模块、`EmptyCatchGuard` 的 `ZombieModeRewardProjectileSpread.cs`、`SkyIslandMosquitoGuard` wav 部署）；BackMountainLifecycle / CampaignPlayability / ContentTransactions 回归绿；Dev 构建 `1CA540FD` 已部署；Wiki 构建通过。
- 官方菜地链路复核（同日，通读 `鸭科夫源码` 的 ConstructionSite / CostTaker / Garden / Crop / CropDatabase / GardenView 并重解 level5）：链路成立——工地 `dontSave=false`（官方键 `ConstructionSite_GardenConstruction`），Mod 只激活 `Interactparent` → `CostTaker.OnEnable` 登记官方造价牌 → 付款 → `OnBuilt` 亮 `Built` 子树 → `Garden.Awake/Start` 读档；种子靠 `CropDatabase.IsSeed` 进官方选种界面，种下消耗 1 颗（仓库 / 背包 / 宠物包都算），产物走 `Cost.Return`。三处修正（CR-2026-09-22-007）：①造价是官方定的 **铲子 ×1 + 粑粑 ×9**（粑粑只掉自蝇蝇队员 / 队长，或分解粑粑枪射程模组），ch1 交付对话 / ch1 解锁飘字 / ch2 接取提示原来写「去建设面板建菜地」是错的，改为工地付款并写明造价与来源，Wiki 中英同步；②产物 / 种子 prefab 是克隆链（遗种蛋 ← 便携安全区装置）带来的 3D 模型，官方 `Crop.RefreshDisplayInstance` 与 `InteractablePickup` 都用它，菜地里会长出「装置」——`ConfigureItem` 反射清空 `itemGraphic`，官方退回图标立牌；③浇水的 UniTask 状态机不在反编译源里，浇水是否免费未核实（GardenView 只有种植走 `Cost`，按免费假设）。待 owner 决定：是否接受官方造价（粑粑 ×9 要专门去打蝇蝇）作为第二章前置；替代方案是 `CostTaker.SetCost` 改价，属改官方数值，未做。L2 绿，Dev 构建已部署。

## 2026-09-22 鸭王征程重设计：杰夫发放 + 新故事 + 后山改接官方建筑（COMPAT / SCHEMA+ / WIRE+ / OPERATIONAL）

- 官方任务投影核心抽到 `Utilities/OfficialQuests/`（唯一实例、四补丁只装一次、给予者扫描唯一），天空岛桥改为客户端，天空岛守卫与 13 组执行回归原样绿；新守卫 `OfficialQuestProjectionGuard`（26 探针 + 3 结构探针）。
- 征程六章投影成官方 Quest 590101–590106（给予者 Jeff=1，owner 本轮授权，已登记 AGENTS §4.14/§10 与 `docs/contracts.md` §7.1/§3.2）；`CampaignProgressService` 仍是唯一权威，发钱 / token / 线索归交付事务；公告板退役（老档保留、互动提示找杰夫，`CampaignBoardView` 删除）。
- 新故事《册子上的名字》：杰夫口吻文案；新目标类型 `garden_built` / `trophy_displayed`（基地侧，事实由后山经 `CampaignBaseObjectives` 提供）；ch2 加建菜地、ch3 加摆战利品、ch5 波次门 4→5；`chapterId` / `clueId` / token / 奖金不变。
- 菜地：官方基地菜地工地的付费交互父物体默认 inactive（UnityPy 读 level5 核实），第一章交付后只 `SetActive` 那个父物体，Mod 只读官方键 `ConstructionSite_GardenConstruction`（守卫禁止写）。展示柜：自建登记簿退役（`ShowcaseUI` 删除），Mod 战利品补官方 `ShowCase` 标签，陈列加成按官方陈列柜实摆计算（公式不变），存档 `BossRush_BackMountain_Showcase_v1` 新增可选 `sourceVersion`（`schemaVersion` 保持 1）。点唱机不改代码，加 CR-2026-09-18-025 防回归断言。
- L2：全量守卫绿（唯一红 `SkyIslandMosquitoGuard` 在改动前 HEAD 已红，与本轮无关）；CampaignPlayability / ContentTransactions / ContentBuildingOwnership / BackMountainLifecycle / SkyIsland 回归全绿；正式编译通过并部署；Wiki 构建通过。L3 未做，清单与待 owner 决定项（官方陈列柜槽位标签需 F3 探针 `SHOWCASE_OFFICIAL_PROBE` 实机读出）见 `docs/reports/reviews/2026-09-22-鸭王征程重设计交付.md`。
- 提交 `5a317b22`、`0e67241d`、`229fd37a`、`4b5f4ea2`，未推送。
<!-- BEGIN FULL AUDIT 2026-09-21 -->

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

## 2026-09-20 人工实测第二轮补漏（COMPAT / SAFE）

- 逐条复核 `20260920人工实测发现的问题.md` 的 17 项；保留上一轮和并行资源会话改动。当前结论、证据与 M20-01–08 人工操作见 `docs/reports/testing/20260922人工实测复核修复记录.md`，第一轮报告已标注为历史实施记录。
- 修复 `CR-2026-09-20-006–012`：基地/局内随从取消与迟到生成竞态；孵化跳过丢结果、暂停与详情空间；Mode H 均值落点及地下退出兜底；日报长正文截断和金额刷新；图鉴实际子场景与失败缓存；Mode E 套装敌友判断；异色中英前缀与 HUD 语言缓存。
- L2：全量 645 项守卫通过；51 组执行回归全部通过（其中 3 组修正本机 Harmony/Managed 环境变量后重跑）。新 `ManualSeptemberReview` 98 项断言、`ContentTransactions` 262 项断言通过。7 组行为反向探针与 3 组守卫反向探针均按预期转红、按字节还原；日志与散列在 `Build/manual-second-20260920/`。
- Windows Roslyn 正式参数快照编译通过（960 源码、退出码 0，只有已有 RuntimeGate CS0649）；资源会话收尾后再编当前完整源码，编译前后源码散列一致，Release 的 14 个 Dev 标识缺席。最终 SHA-256 `8EAC1113F0786A56DA601A2992B65F7F105154B5C74B85278DC90FCE8879C21C`，产物 `Build/manual-second-20260920/compile-final/BossRush.Release.dll`，未部署本轮 DLL。
- 本轮未新增存档字段/TypeID、未重打资源包、未启动游戏、未读写存档、未查看截图。现有 9 张图的配置成立已由执行回归验证，导航、看台、音效、字体、模型/粒子与性能仍需 owner 的 L3 证据；不承诺图像与参考 100% 一致。

## 2026-09-20 资源生产化续作（COMPAT / OPERATIONAL，无 LOD）

- 游戏目标清单外 bundle 改为复制前/后 fail-closed；历史 sky_island_world 在 SHA-256 备份及生产零引用核验后仅从游戏目标移除，作者/仓库历史文件保留。
- 两张旧图标实际 1024 DXT5 → 256 BC7；只重打 birthday_cake、bossrush_ticket 与新增 production_icons。328 个 Sprite 按原 PNG 地址加载，正式压缩包可用时禁止 raw PNG 常驻，缺包/Dev 保留回退；解码失败和自造对象幂等清理补齐。
- 装备/物品 bootstrap 与天空岛预载改用可取消异步/分帧流程，迟到请求释放、场景切换重试、宿主销毁、超时、同步兼容调用均有观测；官方同步 GetPrefab 合同保留。F3 既有只读入口增加基地/天空岛各 10 秒窗口，不新增按钮、不写剧情或存档。
- L1/L2：645 全量守卫、51 套执行回归、10 个反向探针、69 包 UnityPy 及天空岛 Deferred GBuffer 核验通过；两张 Item 序列化数据除引用映射外相同。最终命令、SHA-256、资源尺寸/格式与物件所有权边界见 `docs/reports/testing/20260920_资源生产化续作交付.md` 和 `Build/resource-production-20260920/`。

## 2026-09-20 基地四建筑模型接入（COMPAT / OPERATIONAL）

- 接入 `output/building_concepts/` 的四个锁定 GLB：`439d…` 报箱、`61ee…` 公告栏、`b592…` 展示柜、`d6da…` 遗种巢；保留原建筑 ID、交互、存档身份和缺包占位回退。
- 新增 `tools/building_model_manifest.json`、`tools/import_building_models.py`、`tools/BaseBuildingBundleBuilder.cs.txt`。模型归一化为地面原点、单网格、单材质，1024×1024 BC7、Mesh/Texture Read/Write 关闭、无 Crunch；没有生成或修改 `LODGroup`。
- 公告栏和展示柜接入现有 `FactoryResourceLoading.RunSpecial` 预载和 `BuildingModelHelper` 租约；实例化失败、缺包、场景切换和 owner 清理均保留原 fallback 并释放未转交 bundle。
- L1/L2：Unity 构建与回载、UnityPy 72 包验证、四建筑属性测试、资源守卫、全量守卫 646 PASS、全量隔离回归 51 PASS、Windows 正式编译和正式 DLL 标识检查通过。未启动游戏，未读写存档；模型比例、碰撞和视觉仍待 owner L3 目检。
- 四建筑包的源、仓库、作者 `ResourceRelease` 和游戏目标 SHA-256 与包体记录见 `Build/buildings-20260920/final-hashes.json`；可回退副本见同目录 `deployment-backup/`。
- L3 未执行：没有启动游戏或访问玩家存档，没有真实 F3 数据，不宣称无卡顿。报告产出位置及逐步人工验收/看图清单已写进交付文档。

## 2026-09-20 Unity 资源生产优化（COMPAT / OPERATIONAL）

- 按 `docs/reports/testing/20260920_资源生产化续作交付.md` 实施：能量盾 410,434 → 40,000 三角形；天空岛环境贴图用 1024 BC7 交付副本；纯渲染网格按引用关闭 CPU 副本；PNG 展示路径释放解码 CPU 像素并补齐所有权清理；68 个发布包改用 LZ4、0 个 Crunch。
- 发布包排除运行时不加载的历史 `sky_island_world`，未删除本地文件。此前未覆盖的龙王、goblinnpc、nursenpc、bossrush_wiki、love_heart 已纳入清单和 SHA-256 门禁，复制前自动备份。
- 全量收尾发现并修复 Mode G 通用徽记策略与专用构建器冲突：恢复 256×256、横幅保持 1024×576，Unity 回读通过，包体 199,571 B；补齐词缀选物协程新增 4 条宿主引用的分类记录。
- L2：最终全量守卫 643 PASS / 0 FAIL，资源预算/贴图策略属性测试、部署脚本隔离测试、AchievementIcons / AffixSelectionUI / ManualEquipmentRecovery / ContentThirdReviewFixes 执行回归、UnityPy 逐包核验和天空岛 shader/GBuffer 校验通过；Windows 正式构建 `Build succeeded!`，Release DLL 无 Dev 标识并与游戏目标一致。219 份作者/工作区副本及 68 个游戏包 SHA 一致；4 个收尾反向探针按预期转红并还原复绿。包体/纹理/网格数字见专题交付文档。
- L3 尚待 owner：词缀按钮、能量盾近远景、天空岛贴图/导航、模式 UI 和固定环境下帧耗/GC/Native Memory 采样。没有启动游戏或读取存档。

## 2026-09-20 全部头盔校正与佩戴管线统一（COMPAT / SAFE / 局部 OPERATIONAL）

- owner 已确认第一版雷霆总体方向正确（L3 owner 目检），反馈头顶穿模并授权批量处理。第二轮将雷霆子节点 Y 从 0.28 提到 0.36，宽深约增加 5%；修正霜冠轴向与冠环位置，以及星铜、观星镜、青穗斗笠、断风兜帽的主体定位。龙王/龙裔源 prefab 按字节保持不变。
- 数值唯一源 `tools/helmet_fit_profiles.json`；`tools/helmet_fit.py` 同步/检查。Blender 导入只标准化，Unity `HelmetFitUtility` 在网格子节点绝对赋值；天空岛生成、全量打包与雷霆旧入口均接统一管线，防止重生成覆盖或重复烤偏移。
- L2：Unity 2022.3.62f3 编译及三包构建退出码 0，包内姿态/源盒/资源清单/无外部依赖通过；42 个资源入口、54 个网格/贴图载荷和非头盔装备姿态不变。HelmetFit 9 项属性测试和接线守卫通过，5 个反向探针实跑转红并按字节还原复绿；changed-only 168 PASS / 0 FAIL，贴图预算 4 项通过。
- `thunder_set`、`frost_set`、`skyisland_boss_gear` 已部署到指定的 `Duckov_Data/Mods/BossRush/Assets/Equipment/`，作者产物/仓库/游戏三处 SHA-256 一致。旧包和 prefab 备份、日志、哈希回执在 `Build/helmet-fit/all_helmets/`。
- 教程、规范、contracts 与模型绑定知识库已同步；统一入口 `docs/guides/头盔佩戴与装备尺寸校准.md`。逐顶数值、部署哈希、回退方法及试戴清单见 `docs/guides/头盔佩戴与装备尺寸校准.md`。
- 第二轮实机尚待 owner 试戴，不能把离线模板视作实际捏脸。没有启动游戏或读取存档，没有改运行时 DLL。

## 2026-09-20 词缀选物 UI 覆盖与资源生产审计（COMPAT / OPERATIONAL）

- owner 再次实测“其他武器选中后仍为无法分解”。确认上一轮恢复登记修复没有覆盖 UI：词缀分支早退跳过下一帧复位、漏修正原版提示；共享按钮刷新还漏了词缀模式分流。现按当前选物合并一次下一帧刷新，关闭/销毁/切模式有门禁且 Cleanup 取消任务；共享按钮入口复用词缀费用与锁槽判据。
- L2：`AffixSelectionUI` 69 项断言通过（含两种事件先后、不可分解武器、中英、资源不足/全锁、快速切换与生命周期）；`AffixCombat` / `ManualEquipmentRecovery` 通过；相关守卫 166 PASS / 0 FAIL；6 个反向探针预期转红、按字节还原后通过。无新增 Harmony 补丁、轮询或玩法数值变更。
- Windows 正式构建与游戏 DLL 部署通过，Release 不含 11 个 Dev 标识。SHA-256 `D6CF7A16FEFDD4EBEA288C40F4F1DA8B41D1D51EC508D700167ED0595A2E2BC4`；原目标 DLL 已备份，核验在 `Build/unity-production-review-20260920/deployment-verification.json`。重启后的真实 UI 仍待 L3，操作清单见 `docs/reports/testing/20260922人工实测复核修复记录.md`。
- 资源审计结论：目前不能认定已达生产级性能。69 包 / 190.06 MiB，排除不再部署的 world 原型为 138.26 MiB；能量盾 767,375 顶点 / 410,434 三角形，65 个 LZMA 包配同步加载，天空岛未压缩纹理与可读网格还有成本，Mode G/H 交付包仍 Crunch。另查出游戏目录 5 个包与工作区不同。详细数据、优先级与 Unity 2022.3 官方依据见 `docs/reports/testing/20260920_资源生产化续作交付.md`。本轮资源只读审计，未实施减面、环境压缩、全包重打或加载架构改造；未启动游戏或读写玩家存档。

## 2026-09-20 人工实测补漏（第三轮，COMPAT / SAFE / 局部 OPERATIONAL）

- 复核 `20260919人工实测发现的问题.md` 的 25 条有效问题，第 26 条为空；保留已有改动。完整逐项结论、证据与 T01–T04 复测清单见 `docs/reports/testing/20260919人工实测修复记录_第三轮.md`。
- **装备恢复（已修复，L1/L2）**：补齐匕首 500048、法杖 500049、冰霜长矛 500051 的实例配置登记；锻造资格、重铸估价/选物及手持恢复改走完整 `RestoreRuntimeState`，避免基础配置覆盖 RF 增益；实例补配保留已有耐久和维修损耗，含 0 耐久破损态。
- **法杖请求（已修复，L1/L2）**：正常 1.2 秒收势保留尚在加载的请求；离手、停用及清理推进代数，换走再拿回不接纳旧结果；迟到实体继续经原路径销毁。没有真实帧耗采样，首发流畅度仍待 L3。
- **纹理生成链（已修复，L1/L2）**：便携安全区 Editor 构建器不再重新开启 Crunch，天空岛头目装备策略也显式关 Crunch；纹理工具保留 LF/CRLF 与未启用平台的原始内容。守卫覆盖当前 importer 和四个会写 importer 的构建器。
- **资源交付（已完成，仅工作区）**：授权 Unity 专用入口重打便携安全区包，退出码 0；图标 512×512，DXT5Crunched → DXT5；资源身份、TypeID 500058、其余六对象载荷及作者 prefab 一致。已回拷 `Assets/Items/portable_safe_zone_device`，SHA-256 `6288667d3fc2f81fd1c01ea34ef939ea6b08d03f7508b5face9a60d2b5919b5c`，备份与核验记录在 `Build/manual-review-20260920/`。
- **验证**：最终 changed-only 守卫 165 PASS / 0 FAIL；本轮相关执行回归合计 15 组通过，其中新增 `ManualEquipmentRecovery` 57 项断言；5 个装备/召唤与 3 个纹理反向探针均实跑转红并按字节还原后通过；复制资源后 3 项专项检查再次通过。
- **构建**：Windows Release / Dev 真编译均通过（947 源码、42 引用，只有既有 RuntimeGate CS0649），两档 Dev 标识检查通过。产物在 `Build/manual-review-20260920/compile-working/`，Release SHA-256 `523A7ED1623D08EE42381A6C880763A3A3AC7A18671B87CCBBB21338830F9836`。
- **剩余验证**：未部署游戏、未启动游戏、未读写玩家存档、未看实测截图。恢复后的面板/磨损、首发召唤与取消、图标观感需 owner 按第三轮报告 T01–T04 实测；前两轮 M/N 清单仍保留。没有把编译、隔离回归或包体检查称为 L3。

## 2026-09-20 雷霆头盔佩戴试修（COMPAT / OPERATIONAL）

> 后续状态：本版方向已由 owner 实机确认；头顶穿模已进入第二轮调整，批量校正与最新证据见上方“全部头盔校正与佩戴管线统一”。下文保留第一阶段历史记录。

- 按 owner 指定先修雷神之角 `500055`，其它头盔待这一顶实机确认后处理。
- 对照龙王 / 龙裔原始 FBX 与已打包模型，确认雷霆网格是 Z 向上、Y 向前；预制体覆盖了 FBX 的 X 轴校正，仅保留 Y=180，导致横戴。只改作者工程 `ThunderHorn_Helmet_Model.prefab` 的网格子节点：Position `(0,0.28,0)`、Rotation `(-90,180,0)`，Scale 保持 `(55,70,55)`；偏移按去除长角后的中央盔壳范围计算。
- 单包构建器 `ThunderHelmetPilotBuilder` 只构建 `thunder_set`；Unity 2022.3.62f3 编译/构建退出码 0，四资源入口、姿态与回读包围盒校验通过。新包已复制到本仓库 `Assets/Equipment/thunder_set`，SHA-256 `f4b322e43cdc1e6092c1c0ee5f1eda4d5b8e13d4e4cabc5405d31e109891e039`；旧包备份在 `Build/helmet-fit/thunder_set.before`。9 个网格/贴图资源载荷与旧包一致。2026-09-20 10:30，owner 指定部署目录后，已更新游戏 Mod 的 `Duckov_Data/Mods/BossRush/Assets/Equipment/thunder_set`，部署后 SHA-256 与试修包一致；目标旧包备份为 `Build/helmet-fit/deployed-thunder_set.before`。L3 待试戴。
- L1：官方挂载链与预制体覆盖核对；L2：网格轴向与盔壳中心复算通过。L3 待 owner 目检，尚未宣称实机贴合。完整证据、操作清单及后续教程/脚本同步入口见 `docs/guides/头盔佩戴与装备尺寸校准.md`。

## 2026-09-20 岛上三条主线：交付奖金与文案统一（COMPAT）

- 来源：owner 要求「所有天空岛相关的任务的文字、奖励和交付物品都和序章一样优化」。
- **奖金（已做）**：`SkyIslandOfficialQuestTable` 给三条主线加 `RewardMoney`——点亮两端航标 3000、钟庭之争 5000、归航钟 8000（序章 5000）。发放复用序章那一条：桥比较交付前后的旗标，只在「未交付 → 已交付」那一拍调 `EconomyManager.Add`，官方任务页与完成面板照 `Reward_Money` 显示「金钱 +N」，已领取状态读本槽交付事实，读档重建投影不会再发。老档 `TryBackfillIslandQuests` 的回填不走这条路，所以**补齐状态不补发奖金**。
- **数值理由（owner 授权「好玩优先」拍板）**：按链条递进，参照岛上既有价码（渡口整备下限 60、满血苔药 480）与商店大件（新武器 20000、套装 30000）——一条主线任务给得比一趟整备多得多，又不至于一条任务顶掉半件大装备。回退办法：改 `SkyIslandOfficialQuestTable` 里三个常量即可，不涉及存档。
- **文案（已做）**：三条任务的标题、说明、目标行、阻塞提示，以及苇白 / 晴禾的接取 - 交付引导句，全部按官方 `Quests` 表语气重写（第一人称、短句、不用分号）。复核后天空岛运行时里**玩家可见的 `L10n.T` 文案已无「；」**。名词（守钟装置、归航钟、航向仪）保持原叫法；`SkyIslandMarriageTextRegression` 的两个中文锚点随文案同步（`回岛接交` → `岛上接`），断言语义不变。
- **交付物品：本轮没有为岛上三条新增（设计决定，可一句话推翻）**。序章那件之所以成立，是因为它从头目尸体箱里出、还要活着带回基地，有真实的风险；岛上三条的目标是**世界状态**（两盏灯亮了 / 钟守松口了 / 钟响了），没有可搬运的东西，任何信物都只能在状态翻转那一刻自动塞进背包、下一步原样交回去，属于纯记账（根 `AGENTS.md` §4.16「不为新增而新增」）。另外收尾那条已经发实物：敲钟给晴岚航徽，击败噬风给噬风之核。owner 要的话，按序章同一套接线补 TypeID 即可。
- L2：13 项天空岛隔离回归全绿；changed-only 守卫 150 PASS / 0 FAIL；Windows 正式构建 `Build succeeded!`；Wiki 站构建与中英一致性守卫通过。游戏内百科与在线 Wiki 的天空岛页已写明三条奖励与「回填不补发」。
- L3 未验证：三条任务各交一次看是否到账且只到账一次、老档回填不应到账、任务页奖励行显示是否正常，要 owner 实机确认。

## 2026-09-20 地图标记场景参数：原判被推翻 + 三处收成一份解析（SAFE / 更正）

- **更正**：同日上一条把天空岛序章「地图上没标出来」判成「`SimplePointOfInterest` 的场景参数传了 Unity 场景名，被官方按子场景过滤掉」。这条判断**错了**，现更正如下。
- **证据（L2，实读官方数据）**：从 `Duckov_Data/resources.assets` 里定位官方 `SceneInfoCollection` 资产（条目是 id / SceneReference GUID / displayName key 三元组），再用同文件里的 GUID→场景路径表交叉核对。零号区三条的 id 与场景资源名**相同**：`Level_GroundZero_Main`、`Level_GroundZero_1`、`Level_GroundZero_Cave`。也就是说序章原来硬编码的 `"Level_GroundZero_1"` 本来就是合法的官方场景 ID，`GetBuildIndex` 查得到，标记不会因此被过滤。另从玩家日志实证激活场景确实会切到子场景（`Active scene changing: Level_GroundZero_Main -> Level_GroundZero_1`），`scene.name == GroundZeroScene` 这道门也是对的。（只读了 `Player.log`，没有读写任何存档文件。）
- **但这个坑本身是真的**：同一张表里至少有三条 id 与场景资源名不同，基地的子场景就在其中——`Base_SceneV2_2` → `Base_SceneV2_Sub_01.unity`、`Level_Factory_Main` → `Factory_Main.unity`、`Prepare` → `PREPARE.unity`。传场景名的写法因此是「这张图碰巧对、换张图就整条标记消失且不报错」。
- **`ModeF/ModeFExtraction.cs` 的嫌疑同样不成立**：它把 `SceneManager.GetActiveScene().name` 当场景 ID 传，但 Mode F 跑在零号区，那里 id == 名字，所以当前不会丢标记。按「不凭猜断言」的要求记为 **REFUTED**，不作为缺陷修复。
- **实际改动（加固 + 去重）**：新增 `Utilities/MapPointSceneResolver.cs`（根 §4.9：跨模块基础设施），顺序为「按激活场景 buildIndex 反查官方 ID → `MultiSceneCore.ActiveSubSceneID` → 场景名」。Mode F 撤离点、丧尸模式安全区、天空岛序章目标三个 `SimplePointOfInterest.Create(` 调用点全部改走它，原本的三份私有解析收成一份。丧尸模式保留自己的「本局场景名」末位兜底；它原来的顺序把场景名排在 `ActiveSubSceneID` 前面，现在排在后面（更正确，因为 `ActiveSubSceneID` 本身就是官方 ID）。
- **标记形状按目标尺度决定**：天空岛序章目标画 22 米区域圈（坠落点 + 守卫占一小片地方）；Mode F 撤离点触发半径只有 3 米，画成圈比图标还小更难看见，**评估后保持只画点**，不改。
- **L2**：新增 `tests/MapPointSceneIdGuard.py`（3 个调用点 + 4 个反向探针，逐个实跑转红）；`ZombieModeSafeZoneMapPoiBeaconReuseGuard` 的锚点同步换成共享解析，断言语义不变。全量守卫与执行回归见下方复跑结果。Windows 正式构建 `Build succeeded!`。
- **序章「地图上没标出来」的真实成因仍未定位**：现有证据只能排除场景参数这一条。剩下的可能是标记太小被漏看（本轮已改成区域圈）、`NoConflictingMode()` 当时不成立、或者玩家当时并不在 `Level_GroundZero_1` 子场景。owner 实机复验时如果圈还是不出现，请用 Dev 构建看 `Player.log` 里有没有 `[SkyIslandPrelude] 航向仪地图标记已创建 sceneId=...`：有这行就说明标记建出来了，问题在显示层；没有这行就说明 `EnsureObjective` 根本没跑到。

## 2026-09-20 序章任务实物交付、奖金与地图标记（COMPAT / SCHEMA+ / SAFE）

- 来源：owner 报「接了『云上的坐标』去零号区不知道去哪找线索，地图上没标出来」，并要求（1）把官方任务系统可配置的部分补进教程文档，（2）序章改成打死 Boss 掉落交付物 + 5000 金钱奖励，（3）任务文案按官方语气重写。
- **地图标记**：本轮先判成「场景参数传错导致整条标记被官方过滤」，**后经实读官方数据推翻**（见下一条 2026-09-20 的更正）。实际改动是两项：标记从一个点改成半径 22 米的区域圈（坠落点 + 守卫占一小片地方，只画点容易漏看），以及场景参数改走共享解析 `Utilities/MapPointSceneResolver.cs`（加固，不是成因）。`SkyIslandLiveLocalizationGuard` 与新守卫 `MapPointSceneIdGuard` 同步断言解析入口。
- **交付物**：新增 TypeID `500103`「失落的航向仪」（`Integration/SkyIsland/SkyIslandNavInstrumentConfig.cs`，零新增 bundle 的克隆兜底）。断风游猎 · 守倒下时在官方 `BeforeCharacterSpawnLootOnDead` 那一拍塞进头目库存，随官方尸体箱一起掉出，不另建箱子。售价 0、登记掉落黑名单、配置器与动态注册均已接线。
- **不会卡死**：世界目标的在场判据从剧情位改成「手上有没有」，阵亡掉包后守卫下一趟照常回来；残骸交互体改成「手上一具都没有时再拆一具」的兜底产出。官方目标的完成判据同样看持有（未采样时退回剧情位，避免每次切图先取消再完成、白响一次通知）。
- **奖金**：`SkyIslandOfficialQuestDefinition.RewardMoney = 5000`，桥比较交付前后的旗标，只在「未交付 → 已交付」那一拍调 `EconomyManager.Add`。官方任务页的奖励行用自写的 `SkyIslandOfficialQuestReward`（`Claimed` 读交付事实、`OnClaim` 空实现、文案复用官方 `Reward_Money` 格式串）。**没有用官方 `QuestReward_Money`**：它的已领取写在实例上，而投影每次加载都重建，玩家能在已完成页反复领同一笔钱（守卫已禁用该符号）。`Quest.requiredItemID` 经反射写入，只影响详情页的「所需物品」栏。
- **文案**：按 `StreamingAssets/Localization/ChineseSimplified.csv` 的 `Quests` 表语气重写序章与岛上三条的标题、说明、目标行与阻塞提示（第一人称、短句、不用分号）。名词（守钟装置、归航钟、航向仪）保持原叫法——执行回归逐字比对目标行与面板「还差什么」。
- **文档**：`docs/guides/官方任务系统接入教程.md` 补 §2a–§2f（Quest 全字段、官方 Task 清单、Reward 与领取时序、Condition、任务挂件与地图标记坑、本地化键与格式串）、§10（交付物与奖励结算顺序）、§15.10–15.12（三个新坑）、§16（L1/L3 新增检查项）与 §18（任务文案怎么写，含官方原文对照表）。游戏内百科与在线 Wiki 的天空岛页同步改成实物交付流程。
- L2：changed-only 守卫 132 PASS / 0 FAIL，全量 637 PASS；`SkyIslandPreludeGuard` 新增 12 条断言与 14 个反向探针（共 61 个，逐个实跑转红）；13 项天空岛隔离回归全绿，`SkyIslandStory` 新增「零号区目标真值表」逐字抽取 `ShouldRunObjective` 穷举接取 / 拿到 / 丢失 / 已交付四态。Wiki 构建通过。
- 构建与部署：Windows 正式构建 `Build succeeded!`，仅剩既有 `RuntimeGate` CS0649；部署到游戏目录的 `Duckov_Data\Mods\BossRush\BossRush.dll` 与 `Build/BossRush.dll` 的 SHA-256 一致（`AD893BD0080601FEBDFD8EB11029DA0E2660344C711D36331422B8873E6C5612`），`check_dll_identifiers.py --expect absent` 通过（11 个 Dev 标识全部缺席）。
- L3 未验证：没有启动游戏、没有读写玩家存档。地图圈是否真的出现在 M 键地图上、尸体箱里有没有航向仪、名字与图标是否正常（本轮没有专属图标 PNG，缺图时退回风标罗盘的图）、5000 到账一次且不可重复领取、丢掉仪器后守卫是否回来，都要 owner 按教程 §16 的 L3 第 12–15 条实机确认。

## 2026-09-19 人工实测补漏（第二轮，COMPAT / SAFE / OPERATIONAL）

- owner 给出作者工程绝对路径后，把第一轮挂起的第 1/3/4 条资源工作做完，并修掉三处第一轮「改了表征没改根因」和一处第一轮完全没发现的问题。逐项说明见 `docs/reports/testing/20260919人工实测修复记录_第三轮.md`。
- **第 1 条（贴图）**：真正的两个原因是「importer Max Size 被留在 64/128」和「crunch quality 50 的块状噪点」，不是生成脚本的输出尺寸。按 owner 口径（物品/装备/图标 128–512，立绘/横幅/海报最多 1024，关 crunch）改了作者工程 213 个 `.meta`，并把口径写进三个 Editor 构建器防止下次导入写回去。新增 `tools/apply_unity_texture_policy.py` 与守卫 `tests/UnityTextureImportPolicyGuard.py`（反向破坏已验证）。天空岛环境/地形贴图按 owner 决定保持原样。
- **第 3 条（成就图标）**：生成 10 张新图标（256px，带 alpha）；另外发现旧的 36 张在包里只有 64px，用 `tools/sync_achievement_icons.py` 把源图落成 256 PNG 走 PNG 优先路径，不重打包即可生效。十条新成就的触发链回归第一轮已补齐，本轮复核确认都有真实达成断言。
- **第 4 条（建筑图标）**：重出遗种巢与征程公告板；另外补上第一轮没注意到的后山战利品展示柜（原本是不透明彩色渲染图）。三张现在可见像素 100% 纯白、透明底，与合格的报箱同一路。
- **第 11 条（挥砍拖尾）**：根因是 `rateOverDistance` 把一帧的量全撒在当帧那一个位置上，配合缓出曲线就成了「起手一坨」。改成 `EmitAlongArc` 沿弧插值逐点 `Emit`，自动发射全关，另加 `sizeOverLifetime` 收尾。`EquipmentManualFindingsGuard` 同步断言新口径。
- **第 16 条（显示不出来的符号）**：改用「GBK 能否编码」作客观判据重扫，玩家可见文本里又找出 20 多处（征程 HUD 的 ✓✗、百科三级项目符号 •◦▪、百战留痕与许愿池的 ✓、宿命回响的 ▶、诅咒词缀与天空岛说明里的 U+2212 减号、游戏内百科 19 个文件里的 ✅❌⚔☠）。全部换成 GBK 内的同义写法，新增守卫 `tests/PlayerFacingGlyphGuard.py`（覆盖生产 `.cs` 字面量、`WikiContent/` 与 `Assets/Data/`；日志与报告文本除外，♥ 为实测可显示的单点例外）。在线 Wiki 描述游戏内标记的地方同步改成 √/×。
- **第 2 条（鸭皇图鉴崩溃）**：第一轮漏了静态 `ItemAssetsCollection.InstantiateAsync(int)`——它的方法体是编译器生成的状态机，看不出转不转发给 `_Local`，不能靠猜。本轮补上 Postfix 并让守卫要求三条实例化入口都有。
- **第 7 条**：补上「为什么大部分弄不了」的解释——不合格装备在格子里点不动，玩家永远看不到那句提示，现在未选中时就写明可锻造范围。图腾不进词缀体系仍是既有设计。
- **第 20 条**：F3 补上逐委托、逐目标入口（下一个委托 / 查看进度 / 只接取 / 完成下一条目标 / 交付 / 静态自检）。自检刻意只做静态核对，不驱动追踪器（达标会经 `NotifyObjectivesSatisfied` 改章节状态）。`ManualProgressionGuard` 同步断言。
- **第一轮完全没发现的**：仓库里六处写死 `D:/code/ykf/duckov_modding-main/...`，工程早搬到 Steam 库下，而这些引用大多「找不到就跳过」——`SkyIslandMiniMapGuard` 的第 3 组检查因此从来没真正跑过。统一改走 `tools/unity_project_path.py`（含 Unity Editor 路径，本机实际在 `E:\Unity\2022.3.62f3`）。修好后那组检查第一次真正执行，结果 PASS。
- **OPERATIONAL（部署缺口）**：`compile_official.bat` 的部署段只按名单复制部分 bundle，`frost_set` / `thunder_set` / `frostmourne` / `fenhuang` 和约二十个物品包从来不在名单里——重打了也到不了游戏目录。补一段 `Assets\Equipment` 与 `Assets\Items` 的整目录 `xcopy /D` 扫尾（增量，无改动时不重复复制）。
- **重打包（owner 明确授权「现在就重打全部 bundle」）**：用工程自带版本 `E:\Unity\2022.3.62f3`（revision 与 `ProjectVersion.txt` 一致）跑 `DuckovBundleBuilder.BuildAllBundles`。52 个包重打完成后卡在 URP/Lit 片元着色器的 55,296 个变体（实测约 1.4 变体/秒，折合 11 小时），主动中止；已重打的 52 个里属于 Mod 的 46 个按 SHA 差异回拷，未重打的 `phantom_scythe` / `starwish_fountain` / `weddingchapel` 保持原样（它们的贴图本轮也没改）。天空岛头目装备另用专用构建器单独重打（Standard 着色器，秒级）。回拷前对 49 个基线包做了完整备份。
- 尺寸变化与预期一致：`achievement_icons` 69,507→902,713（64→256，像素 16 倍）、`frost_set` +48% / `thunder_set` +47% / `viperdagger_melee_model` +29%（关 crunch）、`fenhuang_halberd_item` +658% / `frostmourne_item` +564%（图标 128→512）、`skyisland_boss_gear` 9,554,567→4,207,756（1024→512）。`goblinnpc` / `nursenpc` / `respawn_items` 等有 −0% 级别的重打抖动，非本轮贴图改动所致。
- L2：全量守卫 637 PASS / 0 FAIL（新增两个守卫均做过反向破坏→转红→按字节还原）；隔离执行回归 47 PASS / 0 FAIL（3 项需 `BOSSRUSH_HARMONY_DLL` 与 `BOSSRUSH_GAME_MANAGED`，本机分别指向创意工坊 `3588386576\0Harmony.dll` 与 `Duckov_Data\Managed`）。Wiki 构建通过，237 页 / 39,130 处引用，0 缺失 / 0 坏锚点。
- 构建与部署：Dev 与 Release 均编译通过，仅剩既有 `RuntimeGate` CS0649 警告；留在游戏目录的是 Release，`check_dll_identifiers.py --expect absent` 实查 11 个 Dev 标识全部缺席；`Assets\Equipment`(13) / `Assets\Items`(80) / `Assets\achievement`(47) / `Assets\buildings`(9) 与 `BossRush.dll` 逐文件 SHA-256 与仓库一致，0 不匹配。
- L3 未验证：没有启动游戏、没有读写玩家存档、没有看实测截图。贴图清晰度与噪点、三张建筑图标风格、挥砍拖尾观感、符号是否还有豆腐块、F3 逐委托流程、图鉴使用/丢弃，都要 owner 按 `docs/reports/testing/20260919人工实测修复记录_第三轮.md` 的 N01–N08 实机确认。

## 2026-09-19 人工实测 25 项修复（COMPAT / SCHEMA+ / OPERATIONAL）

- 对照 `docs/reports/testing/20260919人工实测发现的问题.md` 的 25 条有效问题完成工作区内代码修复：动态物品同步/异步实例补初始化；词缀字号、全未锁槽计价与装备补配、熔石 Lv.10 上架；冰雷套装模型/耐久/三杀触发、五武器拖尾/握姿、法杖按手持分帧准备、盾实际回血数字、雷戒落雷；日报每日小礼到快递与布局；六章征程/遗种巢 F3 手动演练、随机事件开关和三个双刃事件、隐藏后山调试项、遗魂聚合气泡与蛋/石掉落链。成就回归同时修复首次孵化未初始化目录和远征纪念碑保存失败提前授奖。
- 数值决定按本次人工反馈：词缀每个未锁槽一石，金币为折后重铸基价 ×（10×未锁槽数 + 2×普通 + 5×稀有 + 8×诅咒），不另乘 T；冰/雷改三次直接击杀后小范围效果、四件耐久100；日常签到品质2小礼与里程碑分开；空投/金鸭雨首抽各约4.35%、排除上一事件后最高各5%，新增事件敌我同规则。蛋4%和熔石8%仍非保底。没有改 TypeID、既有 key、官方任务或破坏性 schema；日报追加可选 lastDailyRewardDayIndex / m{n}_isDaily。回退按精确差异进行，旧版不能识别新日常欠奖，先交付未清债务再降级，不迁移/删除玩家数据。
- 第1/3/4项资源工作尚未全部交付：十张成就和两张纯白建筑图的生成规格、PNG优先加载/旧包回退、缓存释放与复制链已完成；生成器修复重复乘alpha、失败保留旧资产及旧调用入口。只读解析203张本地bundle纹理元数据，确证天空岛17张装备Albedo仍为1024；生成表已改512，实际资源仍须作者工程重导入打包。未获外部作者工程/生图技能目录绝对路径授权，未生图、未重打包，不能把此三项标为全部完成。
- L2：主聚合14组执行回归全部PASS；动态物品9、成就加载71、图鉴225、遗种巢事务167、词缀66、日报177、套装及盾38、征程73、随机新事件483项检查通过。生成链20项、纹理预算4项通过；新增/修改守卫与关键行为在工作区副本做预期破坏转红、SHA还原后复验，包含最终气泡跳字/取消/重新激活的3项探针。全量源码检查主跑630 PASS / 3 PARTIAL；审阅工具属性测试首跑因TEMP目录前置失败，随后用工作区内微型仓库、同级临时目录及Pillow默认字体替身保留原断言复验PASS（4次调用/43字段，源码SHA未变），合计631 PASS / 3 PARTIAL。天空岛小地图守卫因外部Unity路径未授权未运行。没有放宽主仓库断言，系统中文字体渲染不计入此隔离证据。
- Windows最终Release/Dev隔离编译均PASS：按正式945源码/42引用清单，C#7.3，引用只用工作区内既有游戏/Harmony副本；输入哈希稳定，仅既有RuntimeGate CS0649警告。最终产物为 `Build/manual-fixes-20260919/compile-working/{Release,Dev}/BossRush.dll`，保留正式程序集名；11个自动验收Dev标识及新增手动控件均在Release缺席、Dev存在。成就部署块在工作区两种假目标布局验证复制后SHA相同，没有写实际游戏目录。
- Wiki标准构建、80项导航、237页/39130处引用检查通过，0缺失/0坏锚点。双语正文、速查框、配置契约和相关repowiki同步；没有发布网站。全部证据见 `Build/manual-fixes-20260919/`，逐项状态、玩法理由、资源剩余范围及M01–M12实机/看图清单见 `docs/reports/testing/20260919人工实测修复记录_第三轮.md`。
- L3未验证：未启动游戏、未操作玩家存档、未部署实际游戏、未查看实测截图、未提交或推送。模型、特效、气泡、UI、实际任务/掉落、Harmony命中和帧耗须owner按清单复测；不宣称已实机生效、无卡顿或25项全部完成。


## 2026-09-19 Mode H 奖励池可靠性复核（COMPAT）

- 完成 `CR-2026-09-19-016`：Mode H 同品质奖励改用共享 `BossRushQualityItemPool`，候选先过黑名单并排序后再由 `ModeHSeedStream` 抽取；`TryInstantiate` 在 `InstantiateSync` 前确认资源实例与 prefab 存在，拒绝官方缺资源时产生的同 TypeID 空壳。没有新增缓存、存档字段、TypeID、经济档位或第二套抽样算法。
- 玩法取舍：保持既有同品质奖励、种子域和 escrow journal 语义，只修复候选枚举顺序导致的重放漂移与缺资源假成功；回退为还原 `ModeHRewardItemPool.cs` 本轮差异即可，无玩家数据迁移。
- L2：`RewardPoolReliability` 247/247 PASS，覆盖三种合法候选顺序、共享池复用、空品质池、黑名单、缺 prefab 空壳、实例化返回 null/抛错、快递拒收与重试；全量源码守卫 628 PASS，changed-only 39 PASS；语法探针 CS1xxx 零错误。全量隔离回归其余 41 项通过，3 项因工作区外 Harmony DLL 未授权读取而未运行。
- L3 未验证：未启动游戏、未部署、未读写玩家存档。owner 需在 Dev 构建中走一次 Mode H 真实结算/重开，确认同一 journal 重放奖励不变、缺资源时保持 pending 而非生成空壳，并观察实际 UI/帧耗。

## 2026-09-19 Wiki 内容修正与发布前验证（SAFE / OPERATIONAL）

- SAFE：修正中英消耗品、NPC 物品、护士三组正文，明确安神滴剂与护士治疗仅清除可治疗状态，不能解除幽灵女巫的「幽灵诅咒 / Ghost Curse」；仅有该诅咒时不能使用安神滴剂。护士速查框去掉不存在的复活/野战诊所服务，Lv.6 折扣改正为 7.5 折；中文扫箱令计数与代码、英文统一为 Boss 死亡次数。对应六份在线正文由原同步脚本生成，未手改生成物；在线 Wiki 专题同步说明正文、速查框与公开部署应分别核对。
- L1/L2：现有 15 个 Wiki 守卫全部通过；原 `npm --prefix wiki-site run build` 完整通过，生成中英正文各 115 篇。80 项导航回归通过；237 个 HTML 页面、39,118 处引用无缺失链接或坏锚点；修改专题的 65 处引用目标存在。12 个关键中英页面的 HTML 内容检查通过，使用 VitePress preview 实际访问均 HTTP 200，覆盖药品、护士、天空岛、新装备与最新日报规则；414 个构建输入哈希与验证结束时工作区一致。
- 验证说明：英文诅咒名经实际本地化常量核对后统一为 Ghost Curse，并重新完整构建。一次临时页面探针误用 Windbreak，核对现有英文正文后改用 Galebreaker Hood，复验通过；没有改页面迎合探针。保留既有大 chunk 与 searchIndex 混合导入构建提示，未调整依赖、阈值或打包配置。证据保存在 `Build/wiki-content-fix-20260919/`。
- OPERATIONAL：公开 `main` 与最近成功部署仍是 2026-09-08 的 `4b1b5b68`；相对该版本，当前正文已有 108 篇修改、8 篇新增。当前只授权本地 commit，未推送或触发 Pages 部署，线上过期项保留待发布状态（CR-2026-09-19-015）。发布后仍需复查 Actions 的 head_sha 及公开双语天空岛/装备/护士/日报页面。
- 提交范围仅本轮文档、速查框、生成正文和两个台账的本小节；保留其他会话修改。未改 C#、数值、ID、存档或部署工作流，未正式编译游戏、启动游戏或做 L3；本轮不作运行时已生效或全项目无缺陷保证。

## 2026-09-19 Mode G 异常路径复核（COMPAT / SAFE）

- 完成 CR-2026-09-19-009～012：败北清零契约连胜；奖励背包销毁及交付/逐件回调取消均可靠结案；合法多 Boss 组合不再被贪心首选误拒绝；刷怪 15 秒预算排除暂停与恢复边界。沿用现有去重、租约、选点和迟到清理，没有更改经济、九波规则、存档 schema 或 ID。
- 玩法取舍：保留提前准备弹种、换武器系、调整距离和压血处决的不同决策，优先修正技术故障对挑战的中断；不凭缺少实机证据重做难度或奖励。回退仅撤销本轮精确差异，无玩家数据迁移。
- L2：287 项 changed-only 守卫通过；ModeGCombat 27 项通过，涵盖 1200 组几何可行性对照；六个反向探针命中预期失败，副本按字节恢复后复验通过，主工作区生产源码哈希不变。探针脚本早期两次因混合行尾锚点不匹配停止，已修正，不计为生产失败。
- Windows 编译：在工作区内按官方完整 942 文件/42 引用清单，以游戏程序集与 Harmony 既有副本进行 C# 7.3 正式配置隔离编译，返回 0；编译快照与工作区哈希一致。保留天空岛 RuntimeGate 的既有 CS0649 警告；11 个 Dev 标识缺席检查通过。未运行自动部署脚本。
- 中英 Wiki 补齐契约连胜中断条件，专题知识库同步；隔离网站构建、80 项导航与 237 页/39118 引用检查通过，0 缺失/0 坏锚点；最终 Mode G 专项 37 PASS。站点快照形成后另有三份物品说明被其他会话更新，故不宣称当前共享站点全部输入已验证；本轮 Mode G 两语输入与生成页均一致。构建输入、链接检查、源文件 SHA、反向验证和编译记录位于 `Build/modeg-review-20260919/`，汇总索引为 `validation-summary.json`。
- 提交前复核：owner 授权本地 commit 后，只选本轮 16 个文件，公共台账按小节暂存。待提交生产源码在 Windows 按官方完整清单编译通过，27 项回归及 11 个 Dev 标识缺席检查通过；此前六个反向探针的源码与暂存内容逐字一致（仅 Git 行尾规范化）。全量源码守卫 624 项经验证通过、3 项外部制品 PARTIAL，唯一剩余失败为基线已有的 `GameplayValidationCoverageGuard` 缺 `Common/Loot` 映射；未加入本轮差异时同样失败，未改断言或白名单。两项 Mode H 初始失败分别来自 Windows 扩展路径与 guard 排除祖先 Build，调整副本访问路径后原代码通过。待提交 Wiki 构建、80 项导航、237 页/39118 引用通过。证据在 `Build/modeg-commit-20260919/`；并行百科提交仅改 Markdown，集成时核对生产源码不变。
- L3 未验证：未启动游戏、未部署实际目录、未读写玩家存档、未查看截图、未提交。人工步骤 MG-R01～06 与 owner 看图清单在 `docs/reports/reviews/2026-09-19-ModeG异常路径复核.md`；真实暂停调度、AI/导航、奖励交付、画面与帧耗不能由离线通过代替，不宣称全部生产验收完成或无性能问题。


## 2026-09-19 NPC 对白与天空岛气泡修复（COMPAT / SAFE）

**完成**：修复 CR-2026-09-19-005～008。敌人交战不选闲话；发现玩家/同伴死亡发送成功才消费，受阻可重试；Boss 血线合并最新进度并由 Update 重试，绑定/退订幂等；F3 缺 owner/入口失败、未观测发送跳过。羽织 3 条、叮当 21 条双语文案改为人物会说的具体生活语言，保留事件键、池条数与每日赠礼规则。原有居民漫步、阵营共享池及专属 Boss 话语继续接在官方管线上。

**取舍与拓展**：复用同一 `SkyIslandChatterEvent` 记录单调进度，12 秒过期防止迟到喊旧事；事件 6 秒短冷却保证反应，下一句闲话仍等完整长冷却。快速跨两条血线合并一句，避免受伤短句排队刷屏。新事件沿用 owner 观测/成功消费和话语表，不缓存待播文本、不增加全局队列、存档字段或生产源文件。回退时整体还原本条对应代码/测试/文档即可，无存档迁移。

**验证**：工作区内游戏程序集副本 + Harmony 副本，按 `compile_official.bat` 的完整来源清单做 Windows Release/Dev 隔离编译；未执行自动部署。提交快照通过 17 项相关守卫、5 组执行回归：`SkyIslandEncounters` 129、`SkyIslandValidationJudges` 156、`SkyIslandStory` 54510、`SkyIslandDialogue` 32、`PermanentDuckNpcDialogue` 162 项断言；10 项反向探针覆盖交战闲话、过早消费、血线重试、缺 owner 假绿、冷却与旧目标，每项命中预期失败并按字节还原，恢复后重跑通过。编译包含正式清单 942 份源码和 42 个引用；仅有未修改的 `SkyIslandOfficialQuestDefinition.RuntimeGate` CS0649 警告。文案核对保留羽织 372、叮当 434 个双语句对及其分类结构。选跑范围针对本任务；未运行会访问外部作者工程的全量守卫或读取外部 Framework 的额外夹具。本地证据在 `Build/npc-fix-20260919/` 的 validation-summary、reverse-validation 和 compile-snapshot 中。证据为 L1/L2，不等于 L3 或未经采样的无性能问题。

**实机补验（本轮未启动游戏）**：由 owner 使用另行部署的 Dev 构建与专用测试档进行，生产交付前换回 Release。走近晴禾/苇白/浮舟/眠苔停留 60 秒，看是否在附近走动并偶尔冒泡；折翎/无声钟守按人设站定。靠近小兵先等闲话，再进入战斗和击倒其同伴，看是否停止闲话、事件不重复刷屏；脱战后等 60 秒确认恢复。Boss 出场后 6 秒内打到 30% 以下，停止攻击继续观察至少 6 秒，应只补一句最新受伤台词；死亡后不补受伤。中英文各走一遍，打开剧情/暂停时不应新增气泡，离岛重进不应重播上趟待播事件。在居民和敌人都曾播过气泡后按 F3 运行岛内只读套件，查看 `SKY_CHATTER` 的双侧计数与 `visual_check=manual`；新进岛尚未观测应 SKIP，不能据此宣称坏了或已验证画面。

**看图清单（owner 目检）**：建议自行截图命名 `NPC-R01-resident-zh/en`（居民头顶、姓名与气泡无遮挡且不断字）、`NPC-R02-mob-zh/en`（交战气泡符合当前情境）、`NPC-R03-boss-zh/en`（快速跨血线与倒下画面不挤成不可读叠字）、`NPC-R04-gifts-zh/en`（羽织/叮当普通及婚后赠礼、重复赠礼的官方对话中英文完整且无“额度/按钮”腔）。移动节奏、事件时序需连续观察，单张截图不能判定；本轮没有 owner 目检证据。实际帧时间/GC、官方气泡最终渲染和本机安装的程序集版本仍需 L3 确认。

## 2026-09-19 鸭科夫日报生产复核（COMPAT / SAFE）

**完成**：审核报箱 → 阅读 → 采集 → 跨日 → 悬赏/签到 → 发奖/补发 → 存档/清理链路。修复报箱注册假成功与缺 prefab 不恢复、长正文固定高度裁剪、零死亡已失败反馈/重复刊号、极端统计溢出；日报奖品直接复用 `BossRushQualityItemPool`，删除私有全表扫描/缓存；提交复核补齐 `GameplayCoverage.json` 中共享奖池到日报/远征既有验收项的映射，撤销映射的反向探针转红并恢复通过。复用共享文本测量、反射定位、View/ScrollRect、快递与存档引擎，未新增生产源码文件、依赖、任务体系、存档字段或经济档位。已同步双语 Wiki、契约与日报两篇 repowiki；finding 为 CR-2026-09-19-001～004。

**已验证**：五类悬赏全部 13 种目标通过生产采集器 → 结算 → Codec 重载不重复付款；日报状态/旧档/欠奖/故障、逐字抽取的布局/报箱注册回归通过，共 160 项日报检查；签到长说明固定上沿，覆盖按钮避让与短文案还原。11 项反向探针均命中预期失败，副本按字节恢复后重跑通过（`Build/daily-audit/mutations.json`）。现金事务与建筑桥接回归通过。最终变更相关守卫 **287 项全部通过**。较早一轮 279 项中曾有 `SkyIslandChatterGuard` 非日报失败，其他会话修改相关代码后最终转绿；本轮未修改它或其被守卫代码。

**基线与限制**：综合奖励回归修改前 243 检查/65 失败，修改后 247 检查/同样 65 失败，全部为 Mode H 候选顺序与空壳检查；日报及新增共享池四项检查未失败，保留原断言。Wiki 标准构建在清理生成的 `docs/en` 目录遇 Windows EPERM，已使用原生成器跳过清理恢复全部生成正文后再跑 VitePress；VitePress 构建成功（65.23 秒），导航 80 项通过；234 个正文/类目生成页与当前源内容的只读比对一致。日报守卫 2 项、Wiki 守卫 15 项、编译清单与引用守卫各 1 项通过。未执行需探测外部依赖并部署的正式脚本，未读取工作区外游戏/Harmony 引用，随后复用工作区内完整程序集副本，按正式清单完成 Windows Release/Dev 隔离编译（942 源码/42 引用，输入与当前源码哈希一致）；两档 Dev 标识扫描通过。未部署、未启动游戏或操作玩家存档。报箱专属模型 bundle 未在本地资源中发现，沿用既有回退模型。不能声称全部生产验收完成或无性能问题。

**提交前独立验证（最终候选）**：以 `f4dc12f5849c6196c7549e9f279db66acf15d9d5` 为基线，仅组装本轮 31 个文件；在 `Build/dc19f/` 隔离快照验证。全量源码守卫 **625 PASS / 0 FAIL / 3 PARTIAL**（138.5 秒）；PARTIAL 仅为未纳入快照的 Mode G、Mode H 展示制品与便携安全区 bundle。Windows Release/Dev 编译均通过（942 个生产源码、42 个本地引用，C# 7.3），仅原有 CS0649 警告；11 个 Dev 标识在 Release 全部 absent、Dev 全部 present。日报 160 项执行检查通过，前述 11 项反向探针与新增覆盖映射反向探针均通过。最终候选标准 Wiki build 通过，80 项导航通过；237 页、39,118 处引用，失效链接/片段均为 0。综合奖励回归保留同样 65 项 Mode H 基线失败，日报及新增共享池断言未失败。最终证据在 `Build/daily-commit-20260919/`；仅达 L1/L2，未部署、未进行 L3 或性能实测。

**后续验收**：本轮隔离构建在 `Build/daily-commit-20260919/compile-snapshot/`，由 owner 自行部署后检查建造/重进、双语长文与滚动/ESC、五类任务尤其零死亡失败、真假撤离边界、跨期/断签补发、真实快递/现金/存档以及帧时间/GC。操作与看图位置见 `docs/reports/reviews/2026-09-19-鸭科夫日报生产复核.md`。本轮保留工作区其他会话的遗种巢、Mode G、奖励箱等改动，本地提交限定上述日报范围，不推送。


## 2026-09-18 遗种巢（PetNest）生产水准审核与优化（COMPAT / SAFE）

**范围与完成**：全面审核并优化遗种巢系统，使其达到生产水准。
1. **玩家可见文本与裸 ID 拦截（AGENTS §4.4）**：
   - 补齐模式门控、随从入场、孵化与服务层全部可达失败码的 `Fail_*` 本地化注入（覆盖 `Fail_mode_g_banned`、`Fail_no_run_active` 等 30+ 项），`DescribeFailure` 支持截断异常名后缀匹配；
   - 面板天赋与战痕彻底告别英文 `statKey` 裸串，通过 `DescribeStatDelta` 统一步行/奔跑/生命/枪伤/近战/护甲/散布/背包格等属性本地化；
   - 孵化页遗魂账本按"离凝蛋还差多少"升序排序，可凝的血脉排在最前。
2. **激活养成循环与性格逻辑**：
   - 随从等级成长实装（`PetLevelMaxHealthBonusPerLevel` 与 `PetLevelDamageBonusPerLevel`），随从在局内随等级获得实在战斗力，局内 HUD 增加等级显示；
   - 修复随从入场生命上限 Modifier 时序缺陷：挂载 MaxHealth Modifier 后调用 `Health.SetHealth(Health.MaxHealth)` 补齐当前生命，解决入场残血/超血问题；
   - 建立单点性格效果表 `PetNestPersonality.cs`，莽撞/谨慎/懒散/忠诚四种性格在官方 AI（`sightDistance`、`traceTargetChance`）、跟随传送距离与专属属性/背包格上完全生效；
   - 天灾远征战利品接入通用共享池 `Common/Loot/BossRushQualityItemPool.cs`，风浪档掉落品质 3 物品，亡命档掉落品质 4 物品，翻牌演出动态解析并展示前三件物品名。
3. **性能优化与架构收敛**：
   - DTO 采用对象直拷（`Clone()`）替代整包 JSON 往返序列化，热路径（击杀经验、远征结算、孵化）吞吐显著提升；
   - `TickBaseMaintenance` 将时间闸前置于 `HasPendingRewardDebt` 全表扫描之前，避免基地每帧遍历远征表；
   - 死亡事件去重前置于事务深拷贝之前，避免为重复死亡事件支付整包克隆成本；
   - 收敛战痕聚合、模型缩放（统一到 `PetNestTuning.DefaultCubModelScale`）、删除未引用死常量 `PetExpExpeditionSurvive`。
4. **守卫与质量闸门**：
   - `compile_official.bat` 确认已登记 `PetNest/PetNestPersonality.cs` 与 `Common/Loot/BossRushQualityItemPool.cs`（AGENTS §4.1）；
   - `tests/PetNestModelsGuard.py` 扩充 `Clone()` 覆盖断言与字段防漂移检查，断言 `CloneBundle` 走直拷不调 `EncodeBundle`；
   - `tests/PetNestUILayerGuard.py` 增加可达 `failureReasonId` 必须有对应 `Fail_*` 键断言；
   - `tests/PetNestRuntimeModuleGuard.py` 原地裁剪 `os.walk` 的 `_dirs`，消除深层目录遍历性能瓶颈；
   - 20 个 PetNest 守卫全部 PASS；`tests/fixtures/ContentTransactions` 执行回归全部 PASS；反向变异破坏实跑转红验证完成并已按字节还原。

## 2026-09-18 天空岛导航、语言与气泡修复提交（COMPAT / SAFE）

**授权与范围**：owner 已明确同意把必要气泡依赖一起提交。本次只收 CR-2026-09-17-021～025 的修复、SkyIslandChatter / ChatterLines、居民 Tick 生命周期及 Session 调用、编译/测试接线和对应文档；候选共 32 个文件。共享文件按改动块拆分，保留其他会话的工作区和暂存成果。

**对提交候选的独立验证（L1 / L2）**：基于 7961898 的独立检出，Windows 原正式脚本 Build succeeded；12 组天空岛执行回归全部通过；源码守卫最终 618 PASS / 0 FAIL，3 项依赖外部资源的检查为 PARTIAL。ModeH 本地化守卫初轮误把绝对路径中的 Build 当排除目录，按 Windows build 路径拼写运行同一份未修改脚本后 PASS，没有修改判据。新增气泡文件本地化登记另做反向破坏，命中失败后逐字还原、基线复跑通过。正式 DLL 的 11 个 Dev 标识 absent，DLL 与隔离部署 SHA-256 均为 d3c371fe77e2632f6eb211f594c6e62958a0777a8608f0cdccfdc4523f067ebf。既有 RuntimeGate CS0649 保留。

**边界与验收**：没有部署真实游戏、启动游戏、访问玩家存档或读图；没有 L3 / 帧耗采样。操作清单沿用本轮修复报告 R-01～04 与导航报告 N-01～07，尤其确认基地/岛上切语言立即刷新、接交任务地图与 HUD 一致、异常气泡不占预算、重复进出无残留。证据在 Build/sky-island-commit-20260918/validation.json；不把更早集成快照或后来并行改动的结果冒充本提交结果。


**提交前基线前进后的补验**：另一会话完成 47c4b0f（取卵扣料与头目补发）后，候选已接到该提交上。对最终组合重新运行 Windows 正式编译、46 项天空岛守卫、编译清单守卫与 12 组天空岛回归，全部通过。最终 DLL / 隔离部署 SHA-256 为 0b105fcf58c33d64f2292a86523db5d3709dff47ee1aa1ca36abe90413566c78；上段全量源码检查对应原候选基线，新增父提交的影响由本段补验覆盖。

## 2026-09-18 晴岚群岛生产复核与交付可靠性（COMPAT / SAFE）

**范围与完成**：从玩家路线、资源用途、失败回滚与生命周期复核现有内容。本轮生产改动仅 `SkyIslandFieldcraft.cs` / `SkyIslandBossLoot.cs`；保留其他会话正在修改的剧情、语言、气泡、模式等内容。关闭 CR-2026-09-18-001 / 002：取蛙卵改走已有材料预留事务，防通知异常丢材料、重入多扣和会话结束后提交；头目补发改走已有额外战利品 helper，满箱扩一格且不销毁已入箱装备。没有另建管理器、每帧任务、TypeID、存档字段、数值或资源包。

**验证**：全量基线 621 PASS，最终 changed-only 178 PASS；最终天空岛执行回归 13/13 PASS。SkyIslandDelivery 31 条断言，完整链接生产库存事务/helper并抽取真实玩家入口；补齐原纪念品用例遗漏的通知故障开关。6 项反向变异在独立副本命中预期断言，每次按字节还原并核 SHA-256，最终基线重跑通过。Windows 原正式脚本在隔离快照编译 Build succeeded，936 个源码及脚本与工作区哈希一致；既有 RuntimeGate CS0649 保留。DLL SHA-256 `C155E39B94A39C25D0C45674D929A0CD9221EAFAA5C625A0FBCFD8A147FA76E3`，11 个 Dev 标识 absent。仓库/游戏现有场景包 shader 检查通过。

**提交前独立复核**：在 HEAD `7961898` 的隔离副本中只叠加本轮 8 个代码/测试文件，确认不依赖其他会话未提交的改动。Fieldcraft / BossEcology 两个专项守卫与 Delivery 31 条断言通过，Windows 原正式脚本再次 Build succeeded，11 个 Dev 标识 absent；DLL SHA-256 `8906C3F68886C36C679E085719E40E6B235E0BB5B26E56915D3AF2D69CED5DEF`。输入哈希无漂移，日志与清单在同一证据目录的 `commit-*.log` / `commit-inputs.json`，仍未部署真实游戏。

**设计与交付**：现有主线、支线、合成、夜间生态、装备和回响具有可追溯的获取/用途/反馈，保持现有玩法分工，优先修投入无回报的失败路径。回退仅撤本轮两处 diff 及对应测试/文档，无数据迁移。报告 `docs/reports/sky-island/2026-09-18-晴岚群岛生产复核与交付可靠性.md` 含覆盖矩阵和逐操作验收/看图清单，repowiki 专题同步。证据在 `Build/sky-audit-20260918/`，全量基线在 `Build/sky-audit-20260918-guards.log`。

**失败记录与边界**：首轮 WSL 缺依赖，改 Windows；并行修改中的两组夹具初轮失败、最终已过。初次隔离快照漏批处理四个续行源码，改用现有编译清单守卫解析器补齐后正式编译通过。隔离游戏目录没有 Mod 部署结构，脚本自动部署未完成；没有部署真实游戏。未启动游戏、读写玩家存档或读截图。没有本轮 L3/帧耗采样，不能宣称全内容实机生产验收完成或无性能问题；尚需 owner 按报告完成交互、战斗、重复进出和性能验收。



## 2026-09-18 天空岛复审 023–025 全部修复（COMPAT / SAFE，待实机）

**完成**：按 owner“全部修复”，关闭 CR-2026-09-17-023 / 024 / 025（L1 / L2，待 L3）。入口三条既有本地化 key 接现有注入链，招牌只在语言变化时更新原 TMP；岛上六位居民姓名更新已有登记，含隐藏/永久居民，出生跨帧后重新取语言；气泡先验管理器及返回任务，静默完成、失败/取消均不消耗预算，跨帧请求的后续异常由静态回调观察。

**设计与性能**：复用官方 UI、全局语言注入与现有 owner / 姓名登记；语言稳定时不遍历居民、不重复写 TMP；不增加反射、气泡池或全局状态机。计数只代表请求进入展示流程，不能证明像素可见或播放完整。没有实际帧时间采样；本轮不改 TypeID、存档、任务身份、数值或资源。

**验证**：全量守卫 622 PASS / 0 FAIL；天空岛 13 组执行回归全部通过，其中交互/语言 193 断言、遇敌/气泡 84 断言。8 个隔离反向探针均命中预期失败，全部按字节还原并核 SHA-256，最终基线复跑通过。官方 Show / ShowTask 只读 IL 确认静默完成与正常展示的跨帧合同。初轮夹具缺并行新增 NoticeFromCharacter 属性及字段抽取锚点过宽，修正后通过，原失败日志保留。

**正式构建**：Windows 原 compile_official.bat 完整路径在 937 项输入快照上 Build succeeded；既有 RuntimeGate CS0649 保留，11 个 Dev 标识 absent。DLL SHA-256 为 acfaf2a2477c976675aca73735b20172fd5f0a15a8d64e5e802abe51fc429efa。临时 Mods 目录尚未创建使自动复制失败；创建后仅手工复制到隔离目录并核相同哈希。未部署真实游戏。本轮五个生产文件与编译快照一致；收尾其他会话对 BossLoot / Fieldcraft 的改动单列为后续差异，不外推本轮验证结果。

**交付/边界/回退**：详见 `docs/reports/sky-island/2026-09-18-天空岛复审三项修复与验收.md` 的 R-01～04 操作与看图清单；证据目录 `Build/sky-island-fix-023-025-20260918/`。同步 confirmed 状态、官方合同、专题知识库与夹具边界。未启动游戏、未访问玩家存档、未读图、未提交；无 L3。回退仅撤本轮精确 diff，无数据迁移，不覆盖并行成果。


## 2026-09-18 Mode G 全链审核与生产修复（COMPAT / SAFE）

- 来源：owner 要求全面审核宿命回响的可玩性、实现、复用与性能。对应 confirmed findings：CR-2026-09-18-003～011，均已修复，实机项待 L3。
- 修复：致命伤在官方 OnDead 注销前计入且不重复；贡献使用 finalDamage；污染/缓存降级统一拒绝计分与预测；普通弹药爆炸字段缺省对齐官方；属性目标保留相反系末击提示；入场验证真实 prefab 并冻结十槽奖励计划；暂停/放弃键、失败奖励文案、宿敌翻译和画布构建失败回收。
- 内容决定：保留九波三幕、八个稳定契约及原经济数值；准备型目标讲清前一波直伤收尾、至少五发有效样本与未点名弹种，不另加系统或奖励。复用遥测/事务/存档/UI既有管线，只改11个已有Mode G生产文件。理由、完整覆盖矩阵与回退范围见报告。
- L2：ModeGCombat生产源码隔离回归19 PASS；Mode G专项守卫37 PASS；三个新增/修改守卫独立副本10次反向破坏命中预期红项、逐字还原后绿。预热后同一目标1000次直接命中在.NET 8替身中0字节托管分配，仅代表该路径。
- 编译：Windows原构建脚本对基线47c4b0f7c07602ed067a592ef014b2411b4a65c3加本轮11文件覆盖正式编译成功；源SHA一致，输出与假部署DLL一致，Dev标识缺席检查通过。产物位于Build/modeg-production-check/source-modeg-only/Build/BossRush.dll，未部署真实游戏。完整工作区首轮编译因并行新武器未完成接线失败，不声明整个工作区编译绿。
- 全库守卫：617 PASS / 9 NEW-FAIL；定向复查日报与两项Wiki转绿，五项仍指向并行新武器源码/清单/清理接线；RemovedLegacyPrompt全目录扫描300秒超时保留未通过。未放宽断言或修改无关模块。
- Wiki：Windows npm build成功，导航80项通过，237页/39098引用无缺失与坏锚点。Linux首次缺Rollup原生依赖的失败日志保留。中英正文、生成页与Mode G知识库同步。
- 限制：未启动游戏、未读写玩家存档、未读截图；AI/导航、实际武器归因与物品交付、UI输入/观感、跨局清理和真实性能待owner实机，不宣称全部达到生产标准或无性能问题。
- 报告与逐操作/看图清单：`docs/reports/reviews/2026-09-19-ModeG异常路径复核.md`。证据：`Build/modeg-production-check/evidence/`、`Build/runtime-regressions/ModeGCombat/`。本轮按 owner 授权本地提交，未推送；提交不代表 L3 验收完成。


## 2026-09-18 鸭皇图鉴生产审核与体验优化（COMPAT）

范围：图鉴入口、目录、击杀/计时、保存、里程碑、浏览和中英玩家指引。对应 `CR-2026-09-18-012`～`017`；完整报告及 C01～C07 实机步骤/看图清单：`docs/reports/reviews/2026-09-18-鸭皇图鉴生产审核.md`。

- 修复坏存档被当空档/截断覆盖、保存拒绝却修改内存和发奖、自定义 Boss 分类及语言冻结、满表清掉已有计时、已保存速杀不补判，以及官方 ScrollRect 布局替换时序。
- 复用现有存档/成就/刷怪目录/UI/立绘缓存。新增“只看待收集”、每页最多12卡及真实入口提示，维持现有经济和收集口径；不新增玩法填充、TypeID 或第二套管线。三类已禁用的自定义 Boss 需先重新启用；一击致死缺首击观测时仍不猜用时。
- 已完成：生产链接图鉴回归 **67 项**，含五类丧尸 marker、重复死亡、Mode H 及玩家/友军/基地/随从/杂兵排除；图鉴守卫 **3/3**、Wiki 守卫 **15/15**；4 个执行回归变异 + 4 个呈现守卫变异转红并按字节还原。Wiki 独立输出构建成功，237 页、39098 引用无缺失/坏锚点，导航80项通过。
- Windows 正式编译通过：干净基线 `f73d06bb` + 本轮10个图鉴生产文件，`compile-patch.log` 含 Build succeeded，临时游戏目录接收产物；图鉴源码 SHA-256 与工作区一致。当前共享工作区完整快照编译被并行新武器改动阻断：`NewWeaponAttribution` / `NewWeaponEquipState` 缺失引用、SummonStaffConfig 新常量未齐；先前 ModeHMatchRules 中间态已另行补齐。未修改这些专题来凑绿。
- `--changed-only` 一轮结果248 PASS / 8 FAIL。复查时 GameplayReliabilityPersistence 与两个 Wiki 中间态红项已转绿；余下5项指向并行新武器改动（两项 NewWeapon、ThunderRingHurtPrefilter、编译清单与静态缓存生命周期），没有归入图鉴已通过的证据。
- 证据 **L1/L2，未 L3**。没有启动游戏、碰玩家存档或向真实游戏 Mod 目录部署。丧尸 key 查询的 .NET 回归未测得分配，但未测整个受伤事件，也没有 Unity 帧时间采样，不宣称“无性能问题”。仅 owner 实机后可确认画面、输入恢复、真实发奖与性能。
- 提交前复核：owner 已授权本地 commit。隔离基线加图鉴生产/夹具改动再次通过67项断言和3项图鉴守卫；10个生产文件仍与正式编译快照一致。新手指引去掉“买书返奖回本”的误导，中英 Wiki 重新构建，15项守卫、80项导航与237页/39098引用检查通过。只提交图鉴范围，保留并行会话的工作区和暂存内容；未推送，L3仍待验。

取舍与回退：筛选只改变显示、分页只改变单次构建量，不改成就分母和奖励；来源提示复用目录分类，不复制 Boss 技能/数值表。回退呈现改动不需存档迁移。错误 payload 采取保护原字节的拒读，不能为恢复旧宽松行为而重新允许截断覆盖。


## 2026-09-18 鸭王征程生产审核与体验优化（COMPAT / SAFE）

**范围/结论**：按 owner 全面审核并优化，沿公告板→六章模式→交付/奖金/线索→设施→切槽与清理检查。保留六章、奖励、TypeID、schema/key，复用已有模式、配装、女巫工厂、模态输入、存档协调器和官方对话/图鉴。未改其它会话的并行业务代码。CR-2026-09-18-026、CR-2026-09-18-027、CR-2026-09-18-028、CR-2026-09-18-029、CR-2026-09-18-030、CR-2026-09-18-031、CR-2026-09-18-032、CR-2026-09-18-033 已修复（L1/L2，待 L3）。

**体验决定**：第二章活动近战契约在 ModeD 开局保证走原近战配装，局内正常捡装；E/F 共用整备不获得该保证。第三章仅需八名敌方头目，删十分钟等待：等待没有路线/资源决策，保留有效战斗目标与原奖金。公告板补入场、设施和最早撤离时机，无需额外场景任务物。

**可靠性与复用**：完成态不重新武装、计数封顶；一次目标入队失败由服务保留本会话事实并重试，换槽清旧事实。终章非波次生成，旧成功/旧异常隔离，死亡/让路/切槽取消独白并清理；图鉴按权威槽双向修复。公告板共享模态租约、Esc 释放，HUD 语言脏检查和量高；程序化资源进现有 AssetCache 账本。公告板宿主 partial 移除，预算下调，没有新增任务框架或平行缓存。

**验证**：CampaignPlayability 73 项、ContentTransactions 与 SkyIslandDialogue 通过；Campaign 两项守卫、预算及实例分类通过。14 个反向变异全部报预期错误、字节恢复后五组基线通过；源码与探针副本核对一致。全工作区 618 PASS / 9 FAIL：三项构建脚本 LF、五项并行新武器未完成接线、一项全树扫描 300 秒超时；未放宽守卫或掩盖失败。当前共享树首次正式编译被并行新武器缺引用挡住。随后 HEAD 加本轮征程/ModeD/章节 JSON 的独立副本 Windows 正式构建成功，934 个输入与 22 个覆盖文件有 SHA 清单，覆盖文件与工作树一致。DLL/隔离复制哈希一致，11 个 Dev 标识 absent；缺少完整资源，副本不是发布包，也未部署真实游戏。Wiki 独立构建通过，237 页/39098 引用无坏链接，80 项导航通过；共享 Wiki 的哈希争用、EPERM 以及 PowerShell 将进度 stderr 当错误的失败均如实记录。

**交付/边界/回退**：报告 `docs/reports/reviews/2026-09-22-鸭王征程重设计交付.md` 含各章可玩闭环、设计理由、失败记录、C-01～08 实操/看图清单；证据索引 `Build/campaign-audit-20260918/validation.json`。专题知识库、中英 Wiki、fixture 边界与 findings 同步。未启动游戏、未接触玩家存档、未读截图、未提交；没有 L3 和实际帧耗采样，不宣称全部已达生产水准或零性能问题。回退仅本轮精确 diff，无数据迁移，不覆盖并行成果。



**提交前集成修正（OPERATIONAL）**：并行提交 f3d28dc 新增 SkyIslandBossVoice.cs，但漏把已有的编译登记纳入提交。为保证当前分支可构建，本提交只补该文件的清单条目；其余未完成的构建脚本改动不纳入。共享事务夹具、实例分类和修复台账均按征程差异拆分暂存，其他会话修改保留。


**提交复核**：owner 追加授权本地提交后，从最新已提交基线 f3d28dc 单独构建本轮提交内容；623 项源码守卫 PASS、0 FAIL、3 项制品检查按 --source-only 标为 PARTIAL。CampaignPlayability、ContentTransactions、SkyIslandDialogue 三组执行回归通过；Windows 正式构建再次 Build succeeded，DLL 与隔离复制 SHA-256 一致，11 个 Dev 标识 absent。未部署真实游戏，L3 与帧耗采样仍待验。构建/检查/提交范围证据在 Build/campaign-commit-20260918；保留全部并行提交与未提交内容，未推送。

## 2026-09-18 竞技场后山生产复审（COMPAT / SCHEMA+）

- 对应 findings：CR-2026-09-18-018～025，代码修复完成，L3 待 owner。审查覆盖种子掉落→官方种植→出击餐、章节解锁、战利品登记/升级、点唱机、物品本地化、生命周期、资源清理与 F3 观测。
- 餐食按官方 raid ID/槽位/角色维持同局，换区重挂、结束清理；焚心椒零基数换弹属性改显式 Add；全部属性成功才结算，失败回滚。登记/消费复用同一事务，种子不能冒充餐食。
- 菜地在 Garden.Start 前按基地场景名恢复，历史恢复标记不再受实时解锁总门阻挡；先保存恢复标记再开放种植，关卡就绪补试。UnlockAll 与语言变化立即刷新；稳定一万次 tick 无场景扫描调用。
- 六件物品注入官方派生 _Desc 中英键并复用共享使用绑定；卸载复位物品配置器标志，支持 ItemFactory 清空后的重加载。
- 展示柜共用模态输入租约，Esc/场景销毁/换槽/卸载释放；只挂可执行动作，补 Backpack 登记。服务层复核资格，坏/超容量收藏保留原文且写保护，JSON 复用共享 parser/writer。F3 后山检查改只读，事务由离线执行回归验证。
- 建筑真实注入成功才置位、prefab 按引用去重，使用 URP 材质并释放自己持有的模板/材质/纹理。曲目表增加可选 musicNameEn（SCHEMA+），旧表回退原名；按路径更新原槽，切语言不增加曲目。
- 玩法取舍：保留伤害/机动/防御三餐、无物品消耗的收藏奖励和轻量音乐奖励；沿用既有概率、价格与成长数值，避免凭无实机证据重做经济。回退按本轮补丁；没有迁移玩家存档。
- L2：BackMountainLifecycle 104 条、ContentTransactions 88 条、BossRewardDelivery 34 条、F3ValidationExecution 51 条通过；ContentSecondReview 通过。后山/BGM 定向守卫通过。新守卫 9 个反向探针与运行时 3 个破坏探针均被捕获，稀疏副本按字节还原并复检 SHA-256。
- Windows 正式编译：隔离快照通过，16 份后山相关生产 C# 与工作区逐字节一致；仅在快照把并发开发的新武器目录还原 HEAD 以排除该处未完成改动。DLL 与临时 gamecopy 哈希一致，Dev 专用标识检查通过；这不是整个当前工作区已编译通过的声明，未部署实际游戏。
- Wiki 构建、80 项导航、237 页链接检查通过（0 缺失/0 断片）。本轮未查看游戏截图。初次 WSL Wiki 构建缺 Linux Rollup 依赖，改用现有 node_modules 对应的 Windows 环境通过。
- 全工作区门禁（首轮修复时）：首次全量 618 PASS / 9 FAIL；随后 changed-only 249 PASS / 8 FAIL，失败均属于其他会话的新武器接线/编译清单或 compile_official.bat 行尾修改。没有放宽断言或改动这些会话的文件。
- 提交前复核：以基线 f73d06bb 加本轮 28 个文件构造独立快照，相关源码守卫 68 PASS / 0 FAIL / 0 PARTIAL，五组后山相关执行回归通过；Windows 正式编译成功，临时复制 DLL 哈希一致、11 个 Dev 标识缺席。只包含本轮修改，不依赖其他会话的未提交代码；共享台账和回归入口按条目拆分。按 owner 本轮授权本地提交，不推送；L3 与真实性能仍待验收。证据为 Build/backmountain-review/commit-*.log 与 commit-manifest.json。
- 证据目录：Build/backmountain-review/。详细设计取舍、隔离编译边界、按操作步骤的 L3 清单与 owner 看图清单：docs/reports/reviews/2026-09-18-竞技场后山生产复审.md。真实输入、种植/掉落、模型与音频、手感及帧耗未采样，不宣称“全部生产验收完成”或“无性能问题”。


## 2026-09-19 Wiki 内容一致性核对与本地网站验证（SAFE）

- 按 owner「直接修正文案并验证本地网站」授权，修正 21 份中英 WikiContent 与 4 份网站分类页；重点为遗种巢等级/性格/远征奖励、天空岛婚姻对象与罗盘、英文叮当奖励、装备比例/寒冷/出伤时间、日报签到、模式入口与攻略。未修改运行时代码或依赖。
- 正文目录 115×2 完整配对，生成器重建 230 篇正文及分类页；额外修复雷霆指环/毒蛇匕首生成页与已有源文案的漂移。并行会话的日报和 Mode G 文案保留并纳入最终快照。
- L1/L2：15 项 Wiki 守卫、80 项导航检查通过；独立副本 VitePress 构建成功，237 页/39118 引用无缺失或坏锚点；37 页 HTTP 正文检查通过。17 件天空岛装备的 34 行中英数值核对一致。414 份输入与主工作区哈希一致，235 份生成 Markdown 二次同步无变化且与主目录一致。
- 共享目录首轮构建通过；随后因并行同步出现输入更新和生成页临时缺失，第二轮失败。保留失败记录并改用仓库内独立副本验证，没有放宽断言。报告：`docs/reports/reviews/2026-09-19-Wiki内容一致性检查.md`；证据：`Build/wiki-audit-20260919/`。
- 公开站仍为 2026-09-08 部署的 4b1b5b6，天空岛公开页 HTTP 404；本地相对公开部署有 117 个 WikiContent 路径变化。本轮未提交、推送、发布，也未启动游戏或读写玩家存档；不把本地通过写成公开站已更新或 L3 已通过。

- 提交前完整性复核：owner 追加授权本地 commit。以 TypeID 声明、物品配置器与 RuntimeModule 登记表核对，98 件物品两语言均有说明、16 个玩家系统均有专题；保留空洞与纯 Buff ID 不当作缺失物品。补齐遗种蛋的亡命远征获取途径，修正图鉴书「回本」误导与浮木错名。本次最终范围为 22 份 WikiContent + 4 份 hubs + 29 份生成页及本节台账；其他会话的日报、Mode G 正文和代码继续留在工作区。


## 2026-09-20 人工实测 17 项修复（COMPAT / SCHEMA+ / OPERATIONAL）

清单 `docs/reports/testing/20260920人工实测发现的问题.md` 17 项全部处理，详细记录与实机清单见
`docs/reports/testing/20260922人工实测复核修复记录.md`。

- **套装（1）**：冰霜「霜噬」/ 雷霆「雷噬」从击杀触发改为**普攻附带**。反 DPS 缩放三道闸：
  内置冷却（1.1 / 1.4 秒）与射速脱钩、单次伤害是常数（5 / 7，不乘触发那一击）、只认
  `!isFromBuffOrEffect` 的直接命中。击杀不再触发任何套装技能。雷霆反震的 AOE 是预期效果，
  但特效从 `ExplosionFxTypes.normal`（火焰系）改成 `flash`。「只有同时穿着才生效」为既有实现，本轮复核通过。
- **头盔（2）**：霜冠 `rotationEuler` 改 `[-90, 180, 0]`、`modelForward` 改 `+Y`——与雷神之角同批模型，
  旧表「不能照抄雷霆的 Y=180」这条结论被实机截图证伪。Unity 预制体、`HelmetFitProfiles.json`、
  属性测试同步；`frost_set` / `thunder_set` / `skyisland_boss_gear` 已重打并落位。
- **图鉴（3–6）**：新增 `CodexOfficialBossRegistry` + `Assets/Data/CodexOfficialBosses.json`
  （官方生物数据库 2026-09-20 快照，40 Boss / 42 非 Boss），分类改为 官方 Boss / 官方精英 / 模组 Boss；
  80 个官方条目立绘换成官方游戏内渲染图（`codex_portraits` 重打 36→88 张，贴图导入设置修正 51 张）；
  取消分页、滚轮 28→8；详情页删掉遭遇说明；「初见模式」→「初见场景」，存档 `SCHEMA+` 新增可选 `fs`
  （schemaVersion 保持 1），显示经 `CodexSceneNames` 走官方 `SceneInfoCollection`，**绝不显示裸场景 id**。
- **建筑概念图（7）**：`tools/gen_building_concepts.py` 产出报箱 / 公告栏 / 遗种巢 / 展示柜四张
  Tripo 输入图（浅灰平底、无投影、单体居中），落在 `output/building_concepts/`。
- **词缀（8）**：延时刷新只修按钮不刷行，导致「词缀名整列空着」。补上面板重建 + 行重刷 + 熔石/费用/物品名；
  词缀名锁成单行防止换行被裁。回归补两条断言并反向验证。
- **日报（9）**：改成卡片仪表盘。底图 `Assets/ui/DailyReport/daily_report_bg.png` 与坐标表
  `Assets/Data/DailyReportLayout.json` 由 `tools/gen_daily_report_ui.py` 一次产出，
  C# 侧 `DailyReportLayoutTable` 读同一份坐标摆字，底图与文字天生对齐；底图缺席 fail-open 退回纯纸色。
  滚动报纸那一套整体删除，`DailyReportUI.cs` 956→612 行。
- **全地图接入 Mode H（10）**：`TryDeriveMap` 从各图**已验证的 Boss 刷新点**派生擂台 / 看台 / 落点，
  隔离点取擂台中心正下方 240 米（staging 是 inactive 创建，不需要地面）。任何一步不合格即 fail-closed。
  离线复算 9 张图全部可派生；默认目标仍优先 owner 调过的 DemoChallenge。
- **遗种巢（11–17）**：孵化演出放慢约一倍且改为手动关闭；异色播放许愿台大奖音乐；
  新增**炫彩**（十色任取两色，45 种，10% 概率，`SCHEMA+` 字段 `chromaA/chromaB`），
  异色概率 1.5%→0.4%；名字「黑白 - xxx」两色渐变 / 异色金字带 ★（文字色按 Linear 空间复算保证 ≥4.5:1）；
  崽身上挂 `PetNestAuraEffect` 光环（炫彩两层一色一层、异色金色 + 点光，普通崽零对象）；
  基地只生成出战席位那一只、改席位/取消立刻收回；天灾远征改「目的地→风险档」两级卡片、
  面板动作条按条数定高；时长梯度 10 / 30 / 60 分钟。
- **验证**：全量守卫 **644 PASS / 0 FAIL**（含 3 个新增/改写守卫的反向验证）；
  执行回归 **49 PASS / 0 FAIL**（`SetBonusCoroutines` 按普攻命中重写；`ContentTransactions`、
  `ContentThirdReviewFixes`、`AffixSelectionUI` 随结构同步改写）；
  Windows 正式编译 `Build succeeded!`；`check_dll_identifiers --expect absent` 通过；
  DLL 与游戏目录 SHA-256 一致 `5B684BED…CFDED9E9`，68 个 bundle SHA-256 校验通过；
  `npm --prefix wiki-site run build` 通过。
- **未验证**：全部运行时观感（套装特效频率与手感、霜冠戴上的实际朝向、图鉴立绘在卡片上的观感、
  日报文字是否出框、炫彩渐变与光环的实际效果、Mode H 派生擂台是否真的能打）只能实机确认，
  清单在修复记录文末。本轮未启动游戏、未读写玩家存档。
  `tools/verify_sky_island_bundle_shaders.py` 本机缺 `UnityPy` 跑不起来，属既有环境限制，与本轮无关。

## 2026-09-20 第三轮：外部审查 11 条复核 + 补漏

对应 `docs/reports/testing/20260922人工实测复核修复记录.md`。兼容性：`COMPAT` + `SCHEMA+`
（远征记录 `petShiny/petChromaA/petChromaB`、纪念碑 `chromaA/chromaB`，schemaVersion 均未变）
+ `OPERATIONAL`（`compile_official.bat` 的音效部署改为整树 + 缺失告警）。无 `SCHEMA-` / `WIRE-` / `BREAKING`。

### 确认并修复（7 条）

- **CR-2026-09-20-013 套装冻结「假成功」**：`TryApplyFrostFreeze` 在 `AddBuff` 后无条件
  `return true`。官方 `AddBuff` 返回 void 且有三条静默 no-op 路径（`buffResist` 命中
  `ExclusiveTag`、同 tag 更高 `ExclusiveTagPriority`、同 tag 同优先级但现存剩余时间更长），
  抗冻目标正好走第一条。现在回读 `target.HasBuff(freezeBuff.ID)`；兜底减速也按
  CharacterItem / 速度 Stat / 协程是否真的拿到回报真实结果。反击冷却挪到冻结成功分支之内。
- **CR-2026-09-20-014 霜噬 / 雷噬提前消耗冷却**：冷却从排队时挪到结算时，
  `frostBitePending` / `thunderBitePending` 挡住延迟窗口内的重复排队。目标在 40~50 毫秒
  延迟里死掉、中途脱装备切图、或雷噬扫不到其它敌人时不再白吃一轮冷却。
  反 DPS 三道闸不变（同时最多一条在飞、伤害仍是常数、只认直接命中）。
- **CR-2026-09-20-015 官方 Boss 名单没补目录**：`AddOfficialEntries` 只遍历
  `GetFilteredEnemyPresets()`，被筛选器关掉或 preset 还没被扫到的官方 Boss 在图鉴里
  连锁定卡都没有。新增第 1b 步 `AddOfficialRosterEntries()`，按
  `CodexOfficialBossRegistry.OfficialBossKeys()`（有序，40 条）补成未解锁卡。
  连带：「全收集」的分母改为官方全部 Boss + 3 自定义 + 5 丧尸，不再随筛选器缩水。
  顺带删除只剩夹具在用的死代码 `GetEncounterHint`（owner 问题 5 已要求去掉那段提示）。
- **CR-2026-09-20-016 日报底图与运行时重复绘制**：签到格 / 签到按钮 / 图例色块
  两边都画，叠出双描边，且颜色两个来源已经漂了（C# `CellEmpty` 199,189,166 vs
  脚本 226,219,205）。收敛成「底图只画不变的装饰，会变色的归运行时」，
  `gen_daily_report_ui.py` 不再画这三处也不再保留那几个颜色常量，底图已重出并部署。
- **CR-2026-09-20-017 日报卡片内滚动实际滚不动**：内容 `sizeDelta` 写死成 viewport 高度，
  Clamped 模式下 `ScrollRect` 认为刚好装得下。改用
  `ContentSizeFitter.verticalFit = PreferredSize` 按 TMP 首选高度撑开。
- **CR-2026-09-20-018 散装音效没被部署**：`compile_official.bat` 的逐文件夹清单只列了
  BGM / SkyIsland / SetBonus / NewWeapons 四个，代码实际还读 Achievement、DragonKing、
  Goblin、Nurse、items、lottery 六个。这六个在 owner 游戏目录里是早年手工拷的，
  干净安装会静默无声——许愿台大奖音乐与遗种巢异色揭晓复用的
  `Assets/Sounds/lottery/special.mp3` 正在其中。改为整树 `xcopy /E` + 逐文件夹缺失告警。
- **CR-2026-09-20-019 远征 / 纪念碑丢炫彩与异色**：`PetNestChroma.Decorate` 只吃
  `PetNestPetRecord`，而远征卡、翻牌卡与碑文显示的往往是真死结算后已被移出巢的崽。
  新增 `SCHEMA+` 字段固化颜色、新增脱离 PetRecord 的 `Decorate` / `DescribePair` 重载，
  展示入口统一为 `PetNestExpeditionService.DescribeDecoratedPetName`。

### 复核后不成立 / 已过期（3 条）

- **审查第 4 条（立绘 bundle 与作者工程不一致）：refuted。** 比对对象错了——
  作者工程的 `AssetBundles/` 是旧的临时输出目录，正式出口是 `ResourceRelease/`。
  逐文件核对 `ResourceRelease/Assets` 与仓库、游戏目录：**全部 SHA-256 一致**，
  包括 `codex_portraits`（5,025,857 字节）。`AssetBundles/` 里的旧副本是陷阱，
  已记在交付文档里提醒不要从那里重打。
- **审查第 5 条（报箱 / 公告栏 / 展示柜没接 3D 模型）：已过期。**
  四个 builder 现在都先走 `BuildingModelHelper.TryInstantiateBundle` /
  专用 bundle 加载，缺包才退回占位；`bossrush_daily_mailbox`、`bossrush_campaign_board`、
  `bossrush_backmountain_showcase`、`petnest_relic_nest` 四个 bundle 均已存在并部署
  （另一个会话当日 21:11 产出，记录在 `Build/buildings-20260920/author-workspace-deployment.json`）。
  bundle 内部是否真的含对应 prefab 只能实机确认（本机无 UnityPy，压缩包无法离线开箱）。
- **审查第 11 条（实机跑的不是第二轮产物）：已过期。** 当日 21:19 另一个会话已重新构建部署；
  本轮再次构建部署，`Build/BossRush.dll` 与游戏目录 SHA-256 一致
  `6FF528E2777FAA2351C012E701719EE9D208FCB7EB76151688877E2B68F193EF`。

### 仍需实机（1 条）

- **审查第 10 条（Mode H 九图）**：`TryDeriveMap` 的选点已是 fail-closed，
  斗士只落在真实刷新点、离场点不会回退到地下隔离点（第二轮修复）。
  但导航连通性、视野、双方生成与安全退出**只能 L3 验证**，离线无法替代。

### 验证

- 全量守卫 **647 PASS / 0 FAIL / 0 KNOWN-RED**（新增 `LooseSoundDeploymentGuard`；
  改写 `SetBonusLifecycleGuard`、`DailyReportPresentationGuard`、`SkyIslandMosquitoGuard`）。
- 执行回归 **51 PASS / 0 FAIL**（3 个依赖本机游戏程序集的夹具需先设
  `BOSSRUSH_HARMONY_DLL` / `BOSSRUSH_GAME_MANAGED`，设后通过）。
- 反向验证：11 个探针（4 套装守卫 + 1 图鉴回归 + 2 日报守卫 + 2 音效守卫 + 3 遗种巢回归）
  全部在预期位置转红，并按字节还原。
- Windows 正式编译 `Build succeeded!`；`check_dll_identifiers --expect absent` 通过
  （14 个 Dev 标识全部缺席）；72 个 bundle SHA-256 校验通过；
  `npm --prefix wiki-site run build` 通过。
- 未验证：全部运行时观感与手感。本轮未启动游戏、未读写玩家存档（AGENTS §10 由 owner 自己做）。


<!-- MANUAL 17 AUDIT 2026-09-22 -->

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

## 2026-09-22 人工实测复核后的 12 项修复与正式交付

分类：COMPAT / OPERATIONAL；注释/守卫文档 SAFE。承接 owner“请你全部以最佳代码形式进行修复”，完整说明见 [20260922 人工实测复核修复记录](docs/reports/testing/20260922人工实测复核修复记录.md)。

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

套装按代数释放 pending 并在结算再验冷却；图鉴以官方名单补足非 isBoss 身份；H 等 AfterInit 和目标子场景稳定、普通传送两处排除 H，退出/续赛使旧等待失效；遗种巢成功提交后同步实体、移除前补旧颜色、发奖前检查 prefab，派遣页与服务同判据，翻牌遵守暂停。日报修正 stretch 宽度，原图同步作者后重打 production_icons，并把实际 Sprite 像素纳入发布验证；云蚋守卫同步整树音效复制语义。

最终验证：649 guards / 52 regressions 全绿，960 源 Windows 正式编译成功；DLL `40CCD683E27FD9F8524C3FF7E46A49B2F6893AE7F3CF3B6D553AD320363F3CD3`，14 个 Dev 标识缺席。正式目标 `D:\sofrware\steam\steamapps\common\Escape from Duckov\Duckov_Data\Mods\BossRush` 共 531 个文件与源一致，72 包三端 SHA 一致且部署包实际读取通过。35 个日报动态区域留白，其余图标纹理/别名/几何保持；霜冠根/子节点姿态已回读。各反向验证预期转红后按字节恢复。日志和回退备份见 `Build/manual-fix-20260922/`；天空岛部署记录已同步。

未运行游戏或访问玩家存档，L3 待 owner 按报告 R01–R17 清单验收。第三分类/立绘方式、全玩法跨模式边界、任意 RGB 和画面满意度仍保留产品口径差距，未把 12 项修复写成 17 项全部验收。未提交、推送或发布创意工坊。
