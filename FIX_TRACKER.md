# FIX_TRACKER.md — 修复状态与兼容性流水账

更早的完整记录见 `archive/`；近期已闭环的大篇幅审计正文也按月份存档，当前文件保留索引与未闭环条目。

## 2026-09-29 鸭王杯改群战 + 全 Boss 池 + 迷宫入场 + 词条 HUD 重叠（COMPAT，L1/L2）

- 群战改版（owner 需求 1）：每场选人页抽左边 3~20 人（可换 3 批）→ 按战力给右边配对手（差 ≤ 500）→ 对照押注 → 两群互殴；一季固定 6 场，名人堂按胜场 / 净赚排名。「自己调整再开打」注释掉，单挑版代码保留不走。拍铃改为全局天灾六选一。不加持久字段，两队只在运行时。
- 全 Boss 池（需求 2）：宿主全部 Boss + 三只自定义 Boss（owner 追加：战力一律 1000）；自定义 Boss 走托管生成器新增的 ModeH owner，Mode H 期间不锁看台玩家。
- 迷宫进鸭王杯回基地（需求 3）：擂台选址加宽松档（绕路 ≤ 4 倍 + 20 m、看台不要求视线）；根因为推断（拐角多的图在严格档全被拒），没有本次实机日志佐证。
- 变异词条面板压住崽的 HUD（需求 4）：面板顶边 -240 → -300，排到伙伴卡（底边 -288）下方。
- 验证：Windows 正式编译通过（仅既有 CS0649）；全量守卫通过，改了 3 个守卫字面量（龙王锁玩家门、名人堂淘汰规则各做反向验证）；执行回归全量通过，新增 ModeHGroupRoster 夹具（3000 组抽取 / 排名 / 淘汰，反向探针转红后按字节还原），ManualSeptemberReview 补迷宫宽松档断言（关掉宽松档转红）；Wiki 构建通过。本会话未启动游戏、未读写玩家存档。
- L3 待验 `GROUP-H-01`：进鸭王杯任选地图（含迷宫），选人页应看到 3~20 张 Boss 卡、「换一批」「就这队」；确认后右边出现对手、两边合计战力差 ≤ 500；开打后两群互相找人打、不追看台上的玩家；拍铃弹出天灾名并生效；打满 6 场进名人堂排名页。迷宫仍直接回基地、选人页卡片重叠出框、两群站着不动超过 20 秒、龙王 / 女巫冲向看台、第 6 场前赛季提前结束，均不合格。`HUD-01`：普通 BossRush 带崽并开变异词条，左上崽卡与词条面板不重叠。

## 2026-09-29 天空岛发布前复审（COMPAT）

CR-2026-09-29-101：官方等待点击继续时暂停消耗进岛超时预算，点击后的真实加载仍有超时保护；生产租约 47 断言及三个反向探针通过。CR-2026-09-29-102：补回观星与断风装备的岛内效果范围。最新集成验证、产物哈希与 REL-SKY-01～08 实机清单见[审查报告](docs/reports/sky-island/2026-09-29-天空岛发布前代码审查.md)。本会话没有启动游戏、访问玩家存档或部署到实际游戏目录。

## 2026-09-29 Mode H 发布复核（7 项新增修复，COMPAT）

- CR-2026-09-29-013～019 全部修复：押品嵌套、旧仓库暂不可读、放弃财务决议、无 Season 恢复动作、倒地接力、ERROR 枪槽恢复、生成异常收口；详情和 L3 清单见[发布复核报告](docs/reports/reviews/2026-09-29-模式H发布复核与验收.md)。
- 完整门禁 717 守卫 / 110 执行夹具通过；1125 份生产源 Windows 正式编译 exit 0，保留 3 条既有 CS0649；14 个 Dev 标识缺席。
- 隔离 Managed 普通拷贝，候选 DLL / 7 份 Mode H JSON / 展示包 9 项哈希一致，脚本校验 73 包；DLL SHA-256 `D5CD329449BB56E933A833CB817F7A31DC574793C4FC90B17F9D5C4AECA0AE3B`。证据 `Build/modeh-release-reaudit/`。
- 本轮未启动游戏、访问玩家存档或执行发布；完整六场、混合 Mod、镜头/输入、九图和性能仍待 L3。此前台账正文已逐字归档并保留待验索引，未扩大门禁预算。

## 2026-09-29 未提交改动提交前复核（COMPAT / SAFE / OPERATIONAL，L1/L2）

- 对 Mode H 下注与仓库保存、恢复与页面重入、战斗生成与租约清理、天空岛加载等待分别核对生产调用和官方实现；纳入同期补入的增援配装异常处理与放弃押注保护。复核后未发现未修 confirmed issue。
- 修正 MatchFlow 超出 1200 行预算的问题：将初始生成与接力共用的协程包装逐字提取到同一 partial 的 `ModeHRuntimeModule_SpawnRoutine.cs`，登记正式编译清单并同步夹具提取路径；没有扩大预算。同步放弃确认的中英文文案及两篇专题知识库。
- `FIX_TRACKER.md` 双方记录完整保留；9 月 20 日第三轮正文与三个冲突 stage 逐字核对，归档原文无缺失。
- 验证：717 项全量守卫通过；4 个修改守卫的 8 个独立副本反向探针按预期转红，逐次按字节/SHA 还原并转绿；10 组相关执行回归通过；1125 份生产源码 Windows 正式编译通过，仅有 3 条既有 CS0649；Wiki 构建通过。回归采用进程级 `DOTNET_ROLL_FORWARD=Major`，图像检查依赖仅安装在忽略的 Build 目录。日志与源哈希见 `Build/review-20260929/`。
- 构建副作用如实记录：本次隔离 Managed 路径填写错误，正式脚本自动回退到实际游戏目录并同步 Mod；已告知 owner。构建与实际 `Duckov_Data/Mods/BossRush/BossRush.dll` 的 SHA-256 均为 `7496457CC31711021177895E7C75243F52F0FBF524B6A11D5624F3B2F6B180EC`，脚本校验 73 个 bundle。未启动游戏或读写玩家存档，不能据此声称 L3 通过。
- L3 待验 `COMMIT-H-01`：基地进入鸭王杯，依次选择首发/接力并点击开打，观察六场中的接力、镜头及结算；恢复页盖回比赛、无故判负、押注重复扣除或已赢押注只退本金均不合格。`COMMIT-SKY-01`：基地船点进入天空岛，在「点击继续」停留超过 120 秒再点击；应正常入岛，自动判加载失败或返航不合格。截图由 owner 查看，步骤记录在本文件。

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
## 2026-09-29 Mode H 发布前审核第二轮闭环（COMPAT）

- CR-2026-09-29-013～026：3 项 P1、5 项 P2、6 项 P3 全部修复；[报告与实机清单](docs/reports/reviews/2026-09-29-模式H发布前审核-第二轮.md)。未改 schema / 存档 key / TypeID / 经济数值。
- 全量 717 守卫、110 个执行回归通过；新增 / 改动的四组回归断言（押注留存、休眠敌人清场、跨代次还原、建页兜底）逐条反向验证转红并按字节还原。夹具替身补齐生产新成员（ModeHPlayerFlow、ModeHRecoverySecondReview、ModeHReinforcementSecondReview）。
- owner 拍板两项已落地：押注账本加可选 `combatStarted`（SCHEMA+，不升版本），开战后看台退出 / 放弃赛季 / 换季按输结清；Mode H 刷出角色 `Exp` 置 0，观战击杀不给经验。`ModeHCashBetGuard` +5 断言 / 探针，`ModeHSpawnTransactionGuard` +1，`SaveFailureRecovery` 新增 `StartedBetForfeit`，反向验证转红后按字节还原。
- 717 守卫、110 回归全绿；Windows 正式编译成功（2 条既有 CS0649），正式构建 `0BA3072F…` 已部署，游戏目录 SHA-256 与 `Build/` 一致；构建含另一会话未提交的天空岛工作区。未执行 L3，实机清单 H2-1…H2-12。

## 2026-09-29 Mode H 发布前审核闭环（COMPAT）

- CR-2026-09-29-001～012：8 项 P1、4 项 P2 全部修复；[完整证据与实机清单](docs/reports/reviews/2026-09-29-模式H发布前审核.md)。覆盖押注/仓库通知异常、加载后对账、租约、重试清理、跨季退出、页面重入与战斗事实归属；未改 schema / key / 经济数值。
- 717 个全量守卫通过；Windows 正式编译成功（3 条既有 CS0649）；ModeH 匹配的 12 个回归夹具与 SaveFailureRecovery 通过，两项修改 guard 的独立副本反向验证通过。
- 官方 Managed 普通拷贝下隔离构建与候选复制成功，73 个 bundle 校验；DLL、7 份 Mode H JSON、展示 bundle 源/目标 SHA-256 一致，14 个 Dev 专用标识缺席。证据 `Build/modeh-release-audit/`。
- 修正 `GameplayCoverage.json` 的 M_MODE_H_01 旧步骤，知识库同步。未执行 L3，未覆盖实际加载目录、未发布；连续六场、输入/镜头、续赛到账与性能仍待清单验收。

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

## 2026-09-28 十四项玩家反馈与日志复核（COMPAT / SCHEMA+ / WIRE+ / OPERATIONAL）

接续会话 `01a0e7ae-7bc7-7c73-8678-698875dec7f5`：修复 Mode H 落盘快照、循环确认、全装备调整及跨场选择，移除开盘动画和选人警告；精简 Mode G 入场页、居中规则并移除普通路牌 / 垃圾桶；崽独立官方格子背包与绑定持久化、巢页滚轮；Jeff 目标短文案；静听耳罩听觉开关；毒疫官方中毒、官方刷怪点及完整路径；神秘商人单入口。最新日志另修正 F3 累计重伤误报泄漏与倒影材质缺贴图属性错误。

续查发现官方物品树的连接类型不支持直接 JsonUtility 序列化，新增显式背包快照保留配件、嵌套物品与 KV；补齐同帧卸载前的资产采集义务、保存失败保留容器及恢复失败回收，并保留官方加载事件。`backpackJson` 为原 v2 的可选扩展，旧档缺省空包；没有新 TypeID。

验证：711 / 711 完整守卫；109 / 109 执行回归（首轮 104 / 5，补齐四项环境依赖并同步一个旧夹具后五项复验通过）；Windows 正式 / Dev 真编译通过，正式 / Dev 标识分别缺席 / 存在。正式 DLL 已部署、哈希核对一致：`A5106DE97477EBC2EE00E912F9DDD218F7D2DAE8831339D33A07553A7C1B119E`，73 个资源包校验通过。证据在 `Build/feedback-20260928-final/`；没有启动游戏或访问玩家存档，L3 待复验，未提交或推送。

详细落点、日志来源、验证证据与 FEEDBACK-* 实机步骤见 [十四项反馈报告](docs/reports/testing/2026-09-28-十四项反馈修复与日志复核.md)。`Player.log` 截至 19:47:01，早于最终修复产物；鸭鸭市场与官方加载路径的异常另列，未将它们误归为本次 BossRush 修复完成。

## 2026-09-28 天空岛八项修复与生产验收准备（COMPAT / WIRE+）

**范围**：按 owner「全部修复直到生产标准」修复 CR-2026-09-28-001～008，涵盖奖励持久化、目标缓存、三处本地化、自动组选敌、密集爆炸和 F3 性能窗口。保存、资产采集、本地化、交互名与窗口完整性均复用既有机制；奖励原样提取到同 owner 的独立 partial，宿主预算下调为 58 文件 / 20060 行。没有新 TypeID、玩家 schema 或数值调整。

**验证**：711/711 全量守卫、109/109 执行回归通过；1119 源 Windows 正式/Dev 隔离构建 exit 0；正式 14 个 Dev 标识全部缺席，Dev 全部存在。正常目标/摘要 10000 次稳定读取为 0 B，爆炸稳定容量和重入深度复用缓冲。保存、本地化、重试、战斗、采样各组反向探针准确转红并按字节还原。集成修正只补旧夹具的表现依赖、共享真实资产采集生成器和 .NET 10 OSR 测量预热，没有放宽阈值或改无关玩法。

**产物/边界**：正式 SHA-256 `4C9F89C683AE2082612226934CB7407A7C405FD131B35781A64C79F2FA6B5D0B`，Dev `D385BC2F9498CA74919436BD9DB67281C0B724C0B7AF8801153AFF749A42F8ED`。代码/日志/隔离产物在 `Build/sky-island-fixes-20260928/`，细节与 FIX-SKY / PERF-SKY 实机步骤见 [修复验收报告](docs/reports/sky-island/2026-09-28-天空岛修复与生产验收.md)。尚未启动游戏、访问玩家存档或部署；L3 与真实性能验收待 owner 授权，不能把 L2 写成实机达标。未提交或推送。

<!-- BEGIN PRODUCTION AND BOSS FIX TRACKER 2026-09-27 -->

## 2026-09-27 生产就绪审计与 Boss 设计修复（COMPAT / OPERATIONAL）

**范围**：owner「全部修复」UI / 特效 / Boss 审核结论，并续完成 `CODE_REVIEW_FINDINGS.md`「2026-09-27 生产就绪全面审计」。402–405 的修复由另一轮工作留在工作区，本轮逐项复核 diff 后接手验证；406 由本轮龙裔配置补齐闭环。

**Boss（UVB-01–06）**：`Utilities/BossSkillDamageRules.cs`（翻滚豁免 + Stat 比值倍率）；龙王 `DragonKingAbilityController*`、`DragonKingConfig`；龙裔 `DragonDescendantAbilities*`、`DragonDescendantConfig`、`DragonDescendantRocketMarker`；幽灵女巫 `PhantomWitchAbilityController*`、`PhantomWitchConfig`、`PhantomWitchBoss*`、`PhantomWitchBossCurseRealmRuntime`；丧尸 `ZombieModeTuning`、`ZombieModeModels`、`ZombieModeRuntimeModule_BossController`。数值取舍：火箭弹 1.6 m / 10 伤（有预警可躲后提高代价）；女巫硬直 0.35–0.7 s；丧尸不封顶 Boss 数以免改经济，改为 0.9 s 全局起手间隔。

**特效 / UI（UVB-07）**：`FenHuangComboPatchesAndFx`、`ZombieModeRewardTriggerEffects`、`DragonKingShockwaveEffect`、`AchievementEntryUI`。

**审计 402–406**：见 `CODE_REVIEW_FINDINGS.md` 同日表。守卫随结构同步：`AssetSnapshotBoundaryGuard`、`OfficialQuestProjectionGuard` 锚点更新并反向验证；`ModBehaviourInstanceClassificationGuard` 基线 269/412（新增音效改走既有入口，未再抬高）。

**文档**：玩家 Wiki 中英（龙王、龙裔、女巫、Boss 攻略）与 repowiki 四篇 Boss 文档追加 2026-09-27 小节。

**验证**：完整守卫 711/711；隔离回归 108/108（Harmony 三项设环境变量补跑）；`npm --prefix wiki-site run build` 通过；Windows 正式构建 exit 0，部署 `Mods/BossRush/BossRush.dll` SHA-256 `EEC6C46F421A349BC168E2B75A383C63E8B9EF1CAA48633EAA3C17959E8D54C6` 与 Build 一致，73 包资源校验通过。L3 未执行：翻滚穿招、预警时长与硬直手感、冠军之影技能伤害 ×1.6、丧尸多 Boss 节奏、新拖尾与渐变观感、序章/任务奖励/亡魂的断电重开，按续审报告 L3 步骤实机。

<!-- END PRODUCTION AND BOSS FIX TRACKER 2026-09-27 -->

<!-- BEGIN WIKI CONTENT AUDIT TRACKER 2026-09-27 -->

## 2026-09-27 WikiContent 对照最新代码全面审校（SAFE）

**范围**：owner「全面看一下 WikiContent 是否符合最新代码，并且讲人话」。核对除历史版本 changelog 外的全部条目（中英），只改有代码依据的地方；只改正文与站点导航文字，不动代码、存档、TypeID。

**修正**：
- 英文名全站对齐代码：护士 Yuori → Yu Zhi（`Localization/LocalizationInjector.cs:139`）、Duck King Codex → Duckov Codex（`Integration/Codex/CodexBookItem.cs:42`）、Peace Amulet → Peace Charm（`Integration/Items/PeaceCharmConfig.cs:14`）；含 `catalog.tsv`、`structure.mts`、`hubs/systems.en.md`。
- 龙裔 / 龙王套装「火焰免疫、火伤转治疗」→ 先掉血、下一帧补回火焰部分 80%，燃烧照挂，冲刺无无敌帧（`Integration/Bonus/DragonSetBonus.cs` `OnDragonSetHurt`）；两套装备页、装备总览、两篇 Boss 页同步。
- 龙息对玩家封顶只限固定项；毒蛇匕首补必挂原版中毒、箱桶不叠毒。
- 词缀熔石叮当商店 Lv.2 → Lv.10（`GoblinAffinityConfig.cs:805`，原与词缀锻造页矛盾）。
- 新手上路补序章首趟上岛补给（`SkyIslandPreludeFlow.cs:104`）、冷淬液 Lv.4 是解锁购买；模式总览改正鸭王杯可用地图（`ModeHMapSupportRegistry.cs:166`）。

**文风**：三组审校未发现典型 AI 套话，未为改而改。

**存疑未改**：`FlightTotemConfig.cs` 物品面板的滑翔下降速度 -2 与实际生效的 `FlightConfig.cs` -0.8 不一致（代码侧，Wiki 按 -0.8）；v2.3.0 日志中天空岛区域 / 搜刮点 / 采集点数量在 C# 里查不到，需 owner 确认。装备 / Boss / 物品 / NPC / 天空岛一组的数值复核深度不如其余两组。

**验证**：`npm --prefix wiki-site run build` 通过；`run_guards.py --changed-only` 288 通过，Wiki / Glyph / SkyIsland 过滤组全绿。L1 静态核对，游戏内显示未实机看。

<!-- END WIKI CONTENT AUDIT TRACKER 2026-09-27 -->

## 2026-09-27 修复独立验收问题并合并远端（COMPAT / SAFE / OPERATIONAL）

CR-2026-09-27-001–007 均完成代码与 L1/L2 修复。拉取前终态 699 全量守卫、99 全量隔离回归、Windows 正式/Dev 构建及标识检查全部通过；两套各 72 bundle 哈希一致。真实部署仍为 `47F26AD8…`，未部署修复版或启动游戏。原有 Goblin bin/obj 按字节保留。

随后取得远端 `e172936d` 的 19 个独有提交，在独立树与本地 27 个独有提交合并。保留双方功能并适配迁移 owner、正式目录和真实生产夹具；没有抬高宿主预算。新增特效资源已由 owner 的 Unity 项目补齐并核验；合并后 711 守卫、107 回归及正式/Dev 完整隔离构建通过，详细通过项与证据边界以 [修复与合并记录](architecture/MIGRATION_REPAIR_20260927.md) 为准。

历史 2026-09-26 的“697/95 全通过”是当时记录；独立审计确认了运行器旧 DLL 和 tmp 污染漏洞，不能追认为可靠的当次全量执行证明。本次全量结果均使用修复后的验证入口。L3 继续见 `architecture/MIGRATION_ACCEPTANCE.md`，新增 `M_RUNTIME_05` 覆盖同场景停用、预热、迟到请求和真实退款。

<!-- BEGIN SKY ISLAND PRELUDE REWARD TRACKER 2026-09-27 -->

## 2026-09-27 序章「云上的坐标」加物品奖励（COMPAT / SAFE）

**授权与范围**：owner「序章也加上物品奖励」。只给 590001 加 `RewardItems`：星苔药膏 ×2、驱风香 ×2、风灯 ×1（第一趟上岛的补给），奖金 5000、收走航向仪与解锁航线不变。交付只有 `TryCompleteOfficialQuest` 一个入口（在基地），由共享核心在交付成功后发进背包，放不下进仓库；已交付的重试与老档航线回填都不发。回退：删掉 `BuildDefinition` 里的 `RewardItems`。

**验证**：全量守卫 677 通过（`OfficialQuestProjectionGuard` 的岛上奖励检查扩到序章，新增反向检查 1 条，共 31 条）；执行回归 9 组通过（含编译序章的 SkyIslandInteraction / Story / Encounters）；Windows 正式构建成功（仅既有 CS0649），部署 DLL 与 Build 一致，Dev 标识 absent；Wiki 构建通过。构建脚本末尾的 `skyisland_fx` 清单报错属另一会话。L3 未做。

<!-- END SKY ISLAND PRELUDE REWARD TRACKER 2026-09-27 -->

<!-- BEGIN SKY ISLAND QUEST REWARDS TRACKER 2026-09-27 -->

## 2026-09-27 天空岛三条主线加物品奖励（COMPAT / SAFE）

**授权与范围**：owner「天空岛那三条也加上物品奖励直接给到玩家背包」。只动 590011–590013 的奖励，奖金、门、交付流程与序章不变；无新 TypeID / 存档字段。

**内容**：点亮两端航标 +风灯 ×2、驱风香 ×2；钟庭之争 +星苔药膏 ×3、归航菜便当 ×2；归航钟 +晴岚护符 ×1、星屑 ×3（都是岛上登记物品，接着用在下一段路或岛上配方）。数据在 `SkyIslandOfficialQuestTable`（无依赖结构 `SkyIslandQuestReward`），桥 `ToStacks` 交给共享核心，沿用上一轮的交付事务：先生成、`TryDeliverQuest` 成功后才 `SendToPlayer` 进背包，背包满走官方仓库（出击中进待领取缓冲）。三条只有官方任务页一个交付入口，已核对不会绕过；老档回填不补发。回退：删掉对应 `RewardItems`。

**验证**：全量守卫 677 通过（`OfficialQuestProjectionGuard` 新增桥传递与「奖励物必须是岛上登记物品」两条及对应反向检查）；执行回归 11 组通过，含编译该任务表的 SkyIslandInteraction / Marriage / Story / Encounters / ValidationJudges / F3AutotestJudges 与 JeffQuestFlow 525（同 ID 段客户端交付一次到包）。Windows 正式构建成功（仅既有 CS0649），部署 DLL 与 Build 一致，Dev 标识 absent；构建脚本末尾的 `skyisland_fx` 清单报错属另一会话未登记的资源包。Wiki 构建通过。L3 未做：岛上交付后背包实际数量、背包满时的去向需实机确认。

<!-- END SKY ISLAND QUEST REWARDS TRACKER 2026-09-27 -->

<!-- BEGIN JEFF QUEST REWARDS TRACKER 2026-09-27 -->

## 2026-09-27 Jeff 任务的物品奖励、引导奖金与提交物品（COMPAT / WIRE+ / SAFE）

**授权与范围**：owner「奖励不单单是钱，适当插入要提交物品的任务，你自己看看怎么给」，按授权直接定。只动鸭王征程六章与十四条引导；天空岛四条的奖励与会话事务不动。无新 TypeID / 任务 ID / 存档字段。设计理由、完整奖励表与人工步骤 R1–R6 见 [交付说明](docs/reports/reviews/2026-09-27-Jeff任务奖励与提交物品.md)。

**内容**：
- 引导奖励：每条 3000–10000 现金（合计 74000），外加下一条要用的入场物品。
- 章节奖励：奖金不变，另各发一份物品。
- 提交物品：第 2 章交 2 个后山收成（三种任选）；天空岛装备引导交 5 片残铜片。
- 数据只在 `Campaign/CampaignRewardTable.cs`。

**实现**：
- 共享投影核心新增可选 `RewardItems` / `Submissions`，在 `TryCommitDelivery` 里按「生成奖励 → 预留提交物 → 客户端交付 → 收走 → 发放」执行，任一步失败原样退回。
- 提交物复用 `SkyIslandInventoryTransaction`，奖励行照官方 RewardItem 显示。
- 引导奖金走 `CampaignProgressService.TryDeliverGuide`，与章节同一套补偿式发钱。
- 新文件：`OfficialQuestItemRules.cs`、`OfficialQuestItems.cs`、`CampaignRewardTable.cs`，均已登记编译清单。

**回退**：`CampaignRewardTable` 对应条目改成 `Pays(0)` / `null`。

**验证**：
- 全量守卫 677 通过。
- 执行回归 10 组通过：JeffQuestFlow 520、ContentTransactions 396、CampaignPlayability 187、BackMountainMorph 1176、BackMountainLifecycle 181 与天空岛四组。
- 7 个运行时反向探针转红后按 SHA-256 还原。
- Windows 正式构建成功，仅既有 CS0649；部署 DLL 与 Build 一致，Dev 标识 absent；Wiki 构建通过。
- 构建脚本末尾的资源清单报错来自另一会话未提交的 `skyisland_fx` 资源包，与本轮无关。

**边界**：L3 未做。

<!-- END JEFF QUEST REWARDS TRACKER 2026-09-27 -->

<!-- BEGIN JEFF FRUIT REAUDIT TRACKER 2026-09-27 -->

## 2026-09-27 Jeff 任务、菜地收获与果实变身复审（COMPAT / WIRE+ / SAFE）

**范围**：owner 要求结合官方源码完整审查 Jeff 任务链路（任务可挂、可推进）、菜地收获到手与吃下变身，达到生产级；期间无人值守，完成验证后提交。保留工作区其他会话的改动；未启动游戏、未读玩家存档。完整 [复审报告与人工清单 Q1–G2](docs/reports/reviews/2026-09-27-Jeff任务与果实复审.md)。

**修复**：CR-2026-09-27-301–308。天空岛装备引导加航线前置，不再卡住引导链；接取写失败告诉玩家；吃完保留吃前武器；到期等动作结束再恢复；雨天火免不再回血；作物表晚注入时重读菜地存档防丢作物；兜底注册检查返回值；起步种子提示浇水。无新 TypeID / 任务 ID / 存档字段 / 数值变更。

**决定与回退**（玩法取舍，按 owner「确保能顺利推进」的要求直接定）：天空岛装备引导改为航线开通后才排进来，理由是它依赖整条序章与岛上 Boss，放在第 5 条会挡住九条入门引导；回退办法是删掉 `GuidePrerequisiteMet` 里的 SkyIslandGear 分支。到期恢复最多等 3 秒（`RestoreWaitLimitSeconds`），这期间属性仍在、不放能力。

**验证**：相关守卫全绿，`EmptyCatchGuard` 968/968；执行回归 10 组通过（BackMountainMorph 1176、BackMountainLifecycle 181、JeffQuestFlow 405 含本机 DLL 契约、CampaignPlayability 187、ContentTransactions、GardenHarvestNotice、SkyIslandDelivery、SkyIslandInteraction 202、SkyIslandOfficialContract 78、SkyIslandStory 54769）；6 个反向探针转红后按 SHA-256 还原。Windows 正式构建成功（仅既有 CS0649），部署 DLL 与 Build 一致，Dev 标识 absent；构建来自共享工作区。Wiki 文案同步后 `npm --prefix wiki-site run build` 通过。

**边界**：L3 未做，不宣称实机手持物、雨天、菜地读档已通过；人工步骤见报告。

<!-- END JEFF FRUIT REAUDIT TRACKER 2026-09-27 -->

## 2026-09-27 天空岛发版复审：Boss 光与声回执、苇白礼物标签、Wiki 对账（COMPAT / SAFE）

**范围**：owner 要求发版前对天空岛做 Wiki 逐条对账、内容可达性、六居民好感婚姻、Boss 质感与可玩性五向复审。上一轮（09-26）已覆盖机制与关系链，本轮只补新问题；结论见 CODE_REVIEW_FINDINGS CR-2026-09-27-001–004，报告 `docs/reports/sky-island/天空岛_发版复审_2026-09-27.md`（local-only）。

**改动**：
- Boss 光与声（CR-002）：`tools/gen_sky_island_sfx.py` 新增 `boss_telegraph / impact / shatter / phase / defeat.wav`（产物在 local-only 的 `Assets/Sounds/SkyIsland`，由正式编译脚本整树部署）；`SkyIslandImpactFx.cs`（`SkyIslandBossSfx` / `Flash` / `PhaseBurst` / `DefeatBurst` / `SpawnWave`），接线 `SkyIslandBossForge`、`SkyIslandForemanBoss`、`SkyIslandRootHunterBoss`、`SkyIslandSickleBoss`、`SkyIslandStormBoss`、`SkyIslandWindhunterChief`、`SkyIslandMirrorChief`、`SkyIslandBossVoice`。回退：删除这些调用与新类即可，无存档影响。
- 苇白 / 浮舟礼物标签 `Tools` → 官方 `Tool`（CR-001）。回退：改回即恢复旧（失效）行为，无存档影响。
- 目标卡「先清守卫」提示（CR-004），`SkyIslandStoryRules` 抽出与 `TryApply` 共用的判据。
- Wiki 夜风一句中英同步（CR-003），「岛上的敌人」补一句蓄力音 / 换阶段 / 倒下回执说明。
- 新守卫 `tests/SkyIslandBossFeedbackGuard.py`、`tests/DuckNpcGiftTagGuard.py`；`tests/fixtures/SkyIslandEncounters/Stubs.cs` 补 `Color` / `DefeatTint` / `SkyIslandImpactFx` 替身。repowiki《天空岛头目战斗与资源》新增「光与声的回执」。

**验证**：`--filter SkyIsland` 49 PASS；全量守卫 673 PASS / 2 FAIL（`EmptyCatchGuard` 指向 `ZombieMode/ZombieModeMapSelectionHelper.cs`、`ZombieModeMutantWikiGuard` 生成页，均为另一会话正在改的丧尸模式文件，本轮未碰；Wiki 站点重建后后者转绿）；天空岛执行回归 13 PASS；Windows 正式编译 Build succeeded（隔离 GAME_PATH，未部署，既有 RuntimeGate CS0649）；`npm --prefix wiki-site run build` 成功、Wiki 守卫 15 PASS。无 L3：未启动游戏、未读存档、未部署。

**同日追加（owner 拍板后）**：`Consumable` → 官方 `Drink`（晴禾、小满）；匠首过热热浪（`CreateHeatShimmer`）与穗镰泥面流动（`SkyIslandMudFlow`），共享材质与粒子、不重打包（真折射需自研着色器 + 重打包，作者工程有他人未完成资产，未做）；新文件 `SkyIslandChampionMoves.cs`（已登记编译清单）给折翎「三刀封路」、守钟装置「钟鸣」，逃圈 1.8 / 4.0 / 5.31 m/s 均 ≤ 5.5；新守卫 `SkyIslandChampionMovesGuard`。全量守卫 676 PASS / 0 FAIL，天空岛回归 13 PASS，正式构建已部署（DLL SHA-256 `07C7F709…F45CA`，音效 / 数据 / Wiki 逐项一致，Dev 标识 absent；部署含当时共享工作区其它会话未提交改动）。回退：删 `BindChampionMoves` 两处调用、`overheatShimmer`、`SkyIslandMudFlow.Attach` 即可，无存档影响。

**同日追加·重打包（owner 批准）**：没有重打场景包 `sky_island_raid`（作者工程 Sky Island 目录有他人 57 处未提交改动），改为新建独立小包 `Assets/ui/skyisland_fx`（21,531 B，SHA-256 `d80936f5…0e65`）：作者工程 `Assets/SkyIsland/Fx/` 热浪折射 `HeatHaze` 与泥面流动 `MudFlow` 两个透明 `UniversalForward` 着色器 + 材质、`SkyIslandFxBundleBuilder`；运行时 `SkyIslandFxAssets`（新文件，与音效类一起从 `SkyIslandImpactFx.cs` 原样拆出守 1200 行预算）一次性加载，缺包 / 不受支持 / 管线没开 Opaque Texture 时退回粒子版；`compile_official.bat` 部署段；守卫 `SkyIslandFxBundleGuard`（外部制品）。全量守卫 677 PASS / 0 FAIL，回归 13 PASS，已部署（DLL `b8a3a626…c6c9`）。回退：删 `CreateHeatHaze` / `TryUseFlowMaterial` 调用或删包即回到粒子版。

## 2026-09-26 点唱机曲目响度、天空岛常驻 BGM、模组更名与网址、v2.3.0 日志并版（COMPAT / SCHEMA+ / OPERATIONAL）

**点唱机「放不出来」**：owner 反馈后山点唱机的 Mod 音乐像是没有。结论（L2）：两首文件都已部署，游戏自带 FMOD 2.3.8 按官方 `CustomSFXCallback` 的 mode 0x10202 离线实测都能解码播放；问题是它们直接复用了 Boss 战的程序化氛围循环，约 -25 LUFS，比官方点唱机曲目（-12.3 LUFS）小 13 dB，在基地环境声里几乎听不见；另外点唱机要交付征程第三章才解锁。处理：点唱机改放 -16 LUFS 的 ogg 副本，并新增第三首天空岛主题曲「晴岚群岛」；作者名改为按语言取用（`authorEn`，SCHEMA+）；Boss 战循环本身不动。

**天空岛常驻 BGM**：`BgmTracks.json` 新增可选 `sceneTracks`（SCHEMA+）；`BossBgmCoordinator` 加场景租约层（`AcquireSceneBgm` / `ReleaseSceneBgm`，按 owner 实例 id + 场景 handle 校验），Boss 曲起播时让位、`StopBossBgm` 先接回场景曲；`SkyIslandAmbience` 构造时获取、Dispose（撤离 / 倒下 / 清理三条路径都会走）时释放。曲目由 owner 提供的 ryw.mp3 转成 `Assets/Sounds/BGM/sky_island_theme.ogg`（-16 LUFS）：FMOD 把这份 mp3 的时长读成 69.3 秒（实际 34.4 秒），直接循环会每遍空白半首，转 ogg 后读数 34.388 秒。守卫 `BossBgmCoordinatorGuard` 新增 6–8 条（接回场景曲、复位先撤租约、天空岛获取 / 释放、场景曲只许 ogg / wav），三处人为破坏均转红、按字节还原后转绿。

**模组更名与网址**：显示名改为「BossRush · 晴岚群岛」（英文 BossRush · Qinglan Archipelago）：README、Wiki 站名 / 页脚 / 首页 / RSS、游戏内百科目录页标题、仓库与游戏目录两份 info.ini（游戏目录那份保留 version 2.2.5）。`name = BossRush`、命名空间、存档与本地化 key 不变。在线 Wiki 统一为 https://bossrushmod.pages.dev/ ：`seo.mts` 的 `siteUrl()` 默认返回它，Cloudflare 构建从此生成 sitemap / canonical / RSS；VitePress base 保留 `DEPLOY_TARGET` 分支，GitHub Pages 副本继续可用（要停掉需改 workflow，待 owner 定）。

**v2.3.0 并版**：v2.3.0 改为「尚未发布」，并入 v2.2.5 之后全部玩家可见改动（中英）；catalog 标题「v2.3.0（即将发布）」。另见同日「焚天龙铳去掉白色光效」条目。

**验证**：全量守卫 673 PASS；后山执行回归 2 PASS；Wiki 构建、237 页链接检查、导航回归通过；Windows 正式构建 Build succeeded 并部署，DLL / 曲目 / 曲目表 / 百科哈希与仓库一致，`check_dll_identifiers --expect absent` PASS。**未实机（L3 待 owner）**：进天空岛是否起播并循环、打完 Boss 是否接回、撤离后是否停；点唱机三首是否都能选到、响度是否合适（需第三章已交付）。ryw.mp3 的作者经 owner 确认为「洛克王国」，点唱机署名已改（英文 Roco Kingdom）。

## 2026-09-26 天空岛敌人与 Boss 按原版参照提升 50%（COMPAT）

owner 指定生物 Wiki 的原版属性作为参照、要求整体提升 50%，并明确包含 Boss。固定 15 个原版参照覆盖 11 个自动头目 / 岛主、折翎、守钟装置、噬风与回响及普通精英；普通敌怪用各自官方底模。血量、伤害、移动、弹速、射程、感知乘 1.5，散布、反应和射击延迟除 1.5。Boss 近战以 Wiki 通用伤害为参照，普通敌怪保留官方独立近战基准。游戏难度照常生效，装备、护甲、技能机制、经验与掉落保持原配置。

基准在 `SkyIslandCombatBalance`，档案明确 `VanillaPresetId`；`SkyIslandCombatPreset` 只在创建前写克隆。删除 EnemyTiers / BossForge 的旧倍率，重试不复利、原版资源不变。零号区序章的断风游猎·守同样提前准备基准（375 血），独立生成入口与岛内共享规则。

验证（L2）：干净 worktree 仅应用本次改动后，相关源码守卫 89 PASS / 0 FAIL，2 项外部制品检查按 source-only 标 PARTIAL；Windows 正式编译成功（既有 RuntimeGate CS0649），临时游戏副本的自动部署未成功，不用于替换实际游戏 DLL。四组执行回归全部通过：SkyIslandEncounters 773、SkyIslandStory 54612、SkyIslandValidationJudges 156、F3AutotestJudges 330 条断言。15 项 Wiki 守卫、中英 Wiki 构建、80 项导航、237 页 / 39145 引用检查通过，0 缺失 / 0 锚点错误。新 / 修改守卫的 6 项变异在临时副本逐次转红并按字节还原转绿，包含注释掉序章强化。未启动游戏或读取玩家存档；L3 待实机，不把离线结果当成实战保证。

数值速查：匠首 1200、猎首 675、穗镰 915、观星手 600、截信人 255、听雨人 450、蚋笛翁 240、镜中客 330、断风追 / 伏 / 守 339 / 285 / 375、折翎 622.5、守钟装置 480、噬风及回响 3000、普通精英 270（均为基础血量）。完整 Wiki 快照进 `tests/fixtures/SkyIslandEncounters/VanillaCombatReference.json`，回退调整统一倍率或恢复本次修改前的档案和装配代码，不迁移玩家存档。

<!-- BEGIN JEFF FRUIT TRACKER 2026-09-26 -->

## 2026-09-26 Jeff 任务、菜地收获与三形态生产链审查（COMPAT / WIRE+ / OPERATIONAL）

**范围**：owner 要求结合官方源码完整审查。覆盖 Jeff 21 条及岛上 3 条投影任务、六章采集/交付/存档、14 引导、菜地注册/发货、果实消费/属性/攻击/恢复。保留工作区其他会话修改；未启动游戏、未读取玩家存档。审查完成后 owner 明确授权检查无误后提交，仅提交本专题代码、回归与文档。完整 [审查报告与 J01–M04 人工清单](docs/reports/reviews/2026-09-26-Jeff任务与果实生产审查.md)。

**修复**：CR-2026-09-26-201–204。果实 OnFinish 前缀覆盖食用二次门关闭仍扣量；近战能力只听 CA_Attack 成功事件；基地目标不再被 ReadyToDeliver 误标已完成；鸭王杯引导读取已结算/已归档战报。无新任务/TypeID/schema、无数值取舍；一条接一条和旧档状态保持原设计。收获原流程只验明，不复制发货或存档引擎。

**验证**：全量 672 守卫通过，10 组相关执行回归通过（变身服务/前缀 1001、后山生命周期 176、征程玩法 187、JeffQuestFlow 234 及只读 DLL 契约等）；8 个隔离反向探针在预期断言转红并按字节恢复。Windows 正式构建成功、14 个 Dev 标识 absent，Build/部署 DLL SHA-256 一致：`9DBABAFF501B56B30F858561055B79A041A18DE85C4B7716458CA873D5F90E48`；固定副本和来源收据保存在 Build/jeff-reviewed-BossRush.dll 与 Build/jeff-review-receipt.json。此为共享工作区构建；完整证据和夹具边界见报告。相关 diff --check 通过。

**边界**：JeffQuestFlow 的 Campaign 持久化和宿主为适配器，真实事务由 ContentTransactions 覆盖；四条岛任务在新夹具只验证 ID 共存，岛上玩法用原专项回归。L3 未做，不宣称任务 UI、模型/物理、实际收成数量或性能已实机通过。游戏启动/玩家存档按 AGENTS §10 交 owner，剩余动作已给逐项判据。

<!-- END JEFF FRUIT TRACKER 2026-09-26 -->

## 2026-09-26 焚天龙铳所有弹药去掉那层很白的光效（COMPAT）

**授权与范围**：owner 原话「焚天龙铳所有弹药都不要加那个很白的光效，太塑料了」。只去掉白光这一层：不改伤害、弹道、射速、弹匣等数值，不削其他特效，不加 TypeID、不改存档 / 配置、不重打包；不提交 Git；按分工未跑 `compile_official.bat`（另有会话统一编译部署）。

**根因**：
- 龙铳 profile 引用的 `Fx_DragonGun_*` 拖尾 / 命中 / 爆炸预制体从没打进 `Assets/boss/dragonking`（UnityPy 列包确认只有龙王 7 个 prefab；`Player.log` 实录 `预制体不存在: Fx_DragonGun_SMG_Trail`），每发都落到 `DragonKingAssetManager` 的后备。后备配色 `GetEffectColor` 默认 `Color.white`：冲锋 / 突击 / 重型 / 狙击 / 霰弹 / 马格南 / 箭矢七种弹药每发都挂一团 0.6–0.8 m、α 0.8 的白色加色软圆光团（每秒 8 粒叠加，中心烧成纯白）+ 一盏白色点光源（强度 2、半径 1.5 m）+ 旋转器。2026-09-23 VB-29.1 之前这颗后备球用 Standard 材质、URP 下画不出来，只剩白灯；VB-29.1 把它换成看得见的加色光团，白光才变得这么显眼。
- 烟花弹自己的程序化特效也叠了白：弹壳拖尾头部 `Lerp(white, 色, 0.2)`（80% 白）、分裂火星拖尾 65% 白、终点火花起始色一半取白，绽放中间还有一层 3 团 0.35–0.6 m、(1, 0.95, 0.8) 的近白闪光 `BloomFlash`。

**完成**：
- `Integration/DragonKing/DragonKingAssetManager.cs`：`GetEffectColor` 改为 `TryGetEffectColor`，默认分支返回 false；两个 `CreateFallbackEffect` 在建 GameObject 之前对未登记名字返回 null，`AddFallbackVisuals` 对未登记名字不加任何东西。龙王 Boss 各 prefab 与能量弹 PWS 的专属配色（青色）照旧。七种弹药从此每发少建一个 GameObject + 粒子 + 点光源 + 旋转器。
- `Integration/DragonKing/Weapons/DragonKingBossGunProjectileAgent.cs`：烟花拖尾、拖尾火星、终点火花只用这一发的调色板色（渐变只管淡出），删掉 `hotColor` 与整层 `BloomFlash`（字段、创建、播放）；绽放剩 72 粒调色板火花 + 暖色光晕。终点火花的淡出渐变改在 `Initialize` 建一次，不再每次 `Play` 分配。
- 守卫：新增 `tests/DragonKingBossGunNoWhiteGlowGuard.py`（后备不许退回白色、龙铳登记的后备色不许近白、龙铳弹体 / 拖尾 / 命中 / 绽放 / 地面区代码里不许有 Lerp 到白 / WithAlpha(white) / 纯白字面量 / hotColor / BloomFlash，烟花仍用自身调色板色）；`tests/DragonKingBossGunFireworkActivationStaggerGuard.py` 去掉对 `flashEmission` 3 连发的断言（该层按 owner 要求删除，防回归改由新守卫钉住），光晕与 72 粒火花断言保留。
- repowiki：`.qoder/repowiki/zh/content/自定义 Boss 系统/焚天龙皇 Boss/武器系统/龙皇炮系统.md`「特效管理系统」补一条。WikiContent 没有描述这层白光的句子，未改。

**每个弹种仍有可见弹体**：15 种主弹都用官方基底弹体（`BulletRed`：红色 Lazer 拖尾 + 红色自发光小球；预热未完成时是 `BulletSMG` 橙色曳光，按 profile.Scale 缩放），本次没动。冲锋 / 突击 / 重型 / 狙击 / 霰弹 / 马格南 / 箭矢只剩这层基底弹体；火箭、糖果另有官方火焰拖尾；冰刃另有青色拖尾；烟花另有调色板色拖尾；能量保留青色光团；粪便、雪球、纳米本来就只有基底弹体。没有弹种因此变成看不见，所以没有改用主色补画。

**取舍与回退**：能量弹 PWS 的青色后备光团 (0.2, 0.95, 1) 算它自己的弹体色，保留；冰刃冰屑起始色 (0.9–0.95, 1, 1) 是冰色渐变的高光端，保留；烟花绽放的暖色光晕 (1, 0.8, 0.5)→调色板 30% 保留。七种弹药的 `Fx_DragonGun_*` 名字留在 profile 里：以后真把这些 prefab 打进包就会直接用上；缺包时什么都不画。回退：`TryGetEffectColor` 默认分支改回 `color = Color.white; return true;`，烟花三处 startColor 改回带白、恢复 `BloomFlash` 块，并同步两条守卫。

**验证（L1 / L2）**：`python tools/run_guards.py --changed-only` 400 个中 397 PASS，另 3 个（`SkyIslandWikiParityGuard` / `WikiSiteStructureGuard` / `ZombieModeMutantWikiGuard`）是另一会话同时在重生成 `wiki-site/docs` 时读到缺页，单跑复核三者均 PASS；`--filter DragonKing` 33/33 PASS，`LargeFileBudgetGuard`、`RepowikiReferenceGuard`、`StaticCacheLifecycleGuard`、`OfficialCompileListFileExistenceGuard` PASS。新守卫与改过的守卫在临时副本上做了 9 项反向验证（后备默认改回白、去掉配色门、加回 hotColor、终点火花起始色改回白、能量色改成近白、删掉烟花拖尾头部色、加回 BloomFlash、点光源改回白、光晕齐射改掉），全部红在预期断言上，按字节还原后绿，工作区文件 SHA-256 未变。`python tools/verify_syntax.py --with-bcl` 语法层零错误。另用现有 `Build/BossRush.rsp`（去掉 `BOSSRUSH_DEV`、`/out` 指向会话临时目录、引用本机游戏 `Duckov_Data\Managed`）直接调 Roslyn 编译，退出码 0、只有既有 CS0649，未部署、未碰游戏目录——正式构建与部署仍以统一编译会话为准。

**L3 未做（未实机）**：需要 owner 进游戏看，清单见交付回复（冲锋 / 突击 / 重型 / 狙击 / 霰弹 / 马格南 / 箭矢的弹体周围和脚下不再有白色光团和白色照亮；烟花弹壳拖尾头部、火星、终点火花是彩色而不是白芯，绽放中心不再发白；能量弹青色光团与冰刃拖尾照旧）。

<!-- BEGIN SKY RELEASE TRACKER 2026-09-26 -->

## 2026-09-26 天空岛发版全链审查与修复（COMPAT / SAFE / OPERATIONAL）

**授权**：owner 要求正式玩家路径、内容与Wiki承诺、全六NPC好感婚姻、Boss战斗/表现和资源交付全面审查并修复，兼容玩法取舍可直接定。未清玩家数据、未改TypeID/存档key/schema、未启动游戏、未读存档或截图；初次交付未提交Git，后续本地提交按下述授权澄清执行。保留初始obj与过程中其他会话改动。

**完成**：34份双语正文、12区域及中继、39箱/30采集点、18物品+序章仪器、17装备、11配方、主支线/委托/灯/蛙/信/图鉴/成长全链矩阵；六NPC逐个身份/位置/礼物/求婚/婚后恢复矩阵；11自动Boss与剧情战/噬风两形态逐项审查。详细 [发版验收](docs/reports/sky-island/天空岛_发版验收_2026-09-26.md) 链接三份分报告。

**修复**：补浮舟/眠苔/折翎/钟守共享关系并通用绑定；折翎战后本趟休整、下一趟及旧战败档恢复；婚后基地/名册文案与视频GetModPath。噬风首战锁风眼、圈灯伤害同心；disabled/缺圈取消技能，匠首不炸无圈点；穗镰站定锁圈；笛翁打断13.5秒；倒影共享Alpha材质；白天/十灯后风暴大风兑现。Wiki按实体仪器、+50%权重、维修最低费、最多四次、NPC位置、天色不改时钟等修正。F3六居民取数/纯判据与首战既有步骤增强，未婚外地实例不豁免、前三项交互必须进入实际激活列表。收尾发现外部基础数值迁移漏了序章守卫，补CreateCharacterAsync前的统一基准调用，出生375生命并保留原仪器/掉帽链；详情CR-2026-09-26-114。

**取舍及回退**：折翎不在尸体边即时出现，次趟恢复；不抹旧胜负/重发奖励。笛翁打断冷却4.5→13.5（成功仍9），奖励读招；回退只改FluteInterruptedCooldown。大风优先2、核仍减至1，回退WindLevel分支会恢复文案缺口。噬风保留高输出跳阶段、不强制锁血凑4次；文案“最多4次”。预警修复不降低正常伤害或阶段机制，逐项回退见Boss报告；关系数据永不清除。

**验证**：全仓672守卫通过；收尾天空岛48守卫、13组执行回归通过；共享NpcAuditFixes在私有SDK10保持net10目标通过，永久对话638、故事54,769、婚姻620、F3AutotestJudges通过。实际Windows正式编译和隔离Dev编译通过，Windows隔离Wiki构建、80项导航、237页/39,211引用且0坏链通过，412输入SHA稳定；最终候选与门禁边界见总报告。修改守卫均在隔离副本实际破坏转红、SHA还原后绿；只读纯判据186；8个改动守卫/属性对应25个文件破坏探针（含序章2项同时跑执行）留证。既有正式资源72包SHA通过，天空岛场景/17装备包实际UnityPy读取及Shader/贴图/头盔核验通过；作者工程脏但本轮没有改作者资产，不重包。

**最终候选**：Windows原样正式脚本在1036份源码实体快照编译通过。固化DLL为6,014,976字节，SHA-256 `5ba16770967ff6636fd08c370c97320daa7795cb5bcf76b310f86c38071db921`；2026-09-26 10:57（UTC+8）部署至实际E盘游戏 `Duckov_Data/Mods/BossRush/`，DLL、231份Wiki和2份数据共234文件SHA一致，两份天空岛包再次回读一致，14个Dev标识absent。回执与覆盖前备份见 `Build/sky-release-20260926/deployment-final.json` / `before-final-deploy/`。未覆盖共享Build输出；固化后MapSelectionHelper与Reforge比较UI的外部改动未进入该候选，不能代签其验证。

**交付复查**：11:00（UTC+8）游戏目录被另一会话覆盖，DLL变为 `4abc5b65383697b4b745e3177c43000464944b3174cd64a060d07dcec637f639`，另有8份Wiki正文漂移；14个Dev标识仍absent，但不能冒作本候选通过。未再覆盖对方部署；固化release保持完整。`deployment-at-handoff.json`留存差异，`restore_candidate.py`提供并发部署结束后的候选SHA预检、备份恢复与回读，已做语法检查、未实执行。

**发版条件**：固定总报告最新候选（收尾提交见第10节）并完成R01–R09指定实机验收后可以发版。没有本轮L3与性能采样；全仓历史失败已关闭，检查时点及并发边界见总报告。当前漂移部署不能直接签字；先恢复固化版本或另验最新外部候选，再走正式入口与全部NPC/Boss实机，owner按step/shot清单目检。固化产物为正式DLL，Dev只在隔离目录试编；游戏内是否生效及观感不计静态通过。

**早期提交预检（后被授权澄清替代）**：owner追加“达生产水准再commit”授权。HEAD150f53ba、暂存区为空；108份天空岛相关源与正式快照逐字节一致，diff --check通过。最近实机记录仍是9/25旧Dev（79 PASS /6 FAIL /1 SKIP），早于本轮六NPC与战斗修复，不能关闭本轮L3。未再改生产代码、未暂存/提交/覆盖部署；授权保留，待固定候选与本轮实机证据达到条件后提交。详见总报告第9节。

**授权澄清**：owner确认尚未实机，明确改为按代码复核/离线验证提交，实机检查进入F3。因此缺L3不再阻止本地commit，仍不写成实机已通过。提交基于已合入天空岛数值基准的7c282cdc；关系、战斗、Wiki与F3独立复核，其他专题不带入。

**提交前新增修复（CR-2026-09-26-116 / COMPAT）**：已婚折翎同场随行时，原挑战入口会再生成敌对折翎；统一CanBeginStoryChallenge判据拒绝该组合并提示先送回家，陈旧确认同判据重验。未婚、家中/异场配偶及原胜负状态不变，不写关系或剧情存档；回退仅撤销该门会恢复同名友敌风险。婚姻组947条回归通过。F3完整待测清单追加四项六人关系/求婚/读档/异常组合，补镰扫收招、倒影透明和打断13.5秒人工判据；86自动步骤保持，人工项仍为MANUAL_PENDING。

**精确提交候选复验（2026-09-26 11:24–11:29，UTC+8）**：在7c282cdc的独立worktree只应用本轮65份源码/测试/数据/Wiki/专题/台账差异。全量守卫671 PASS / 0 FAIL / 0 KNOWN-RED；13组天空岛执行回归、永久对话638及F3AutotestJudges330全部PASS，婚姻947、剧情54,769、只读判据186。Windows原样正式与Dev脚本均Build succeeded；正式DLL 6,011,904字节，SHA-256 `4ddaa1c9e4b4b9ecfa478154b21028a3f2ec0a978e594833e04abbff393a0d96`，14个Dev标识absent，Dev仅留隔离目录。Wiki中英构建、80项导航、15项守卫与字形检查通过，237页 / 39,145引用 / 0缺失或坏锚点，412输入未漂移。数值两守卫追加6次反向转红/还原转绿。全部证据在 `Build/sky-release-20260926/commit-review/`；首轮因候选缺本地GLB和误带他人编译登记的3个失败保留原日志，补齐资源、排除无关改动后完整复跑，并未放宽守卫。本次不覆盖真实游戏部署、不推送；六人婚后读档、全部Boss实打、视觉及性能保留F3人工待验，不能以该次L1/L2签署L3通过。

**最终集成与提交依据（2026-09-26）**：并发入场/重铸会话已先提交ee2ac261，本轮65文件候选以该提交为最终父基线，保留其32文件成果。重新跑全量守卫672 PASS / 0 FAIL / 0 KNOWN-RED；Windows正式和Dev编译通过，正式DLL 6,019,584字节、SHA-256 `e1e4d85865bab760adcd06b06769cb9da18bc76e6472a7253eee0cc3c6175ac4`，14个Dev标识absent；13组天空岛及PermanentDuckNpcDialogue、F3AutotestJudges、EntryAndReforgeCompatibility、ModeHSceneEntry共17组执行回归全部通过。最终Wiki构建、80导航、15守卫、字形与237页/39,145引用检查通过，0缺失/坏锚点；412输入稳定，65个提交路径均核SHA。旧7c282cdc证据保存在`commit-review/prior-7c282cdc/`，当前证据以`commit-review/*-result.json`、`wiki-final-receipt.json`为准。提交仅含本轮内容，不包含Build、DLL、bundle或其它会话未提交文件；主工作区及暂存内容按私有index事务保护。未覆盖实际游戏、未推送，L3仍留F3待验。

<!-- END SKY RELEASE TRACKER 2026-09-26 -->

## 2026-09-26 鸭王杯四页重排、刷新不闪、Jeff 引导一条接一条、崽炫彩蓝白绿精修（COMPAT / SAFE）

**授权与范围**：owner 截图反馈 5 项 + 追加 1 项（看盘页布局乱、刷新整页闪、「战况 / 侦察」可去掉且整备页乱、结算页优化、崽蓝白绿炫彩塑料感、Jeff 剧情一次全放出来且文案有人机感）。不加 TypeID、不改存档 schema、不重打包；不提交 Git。

**完成**：
- 根因（看盘 / 押物品 / 结算三页全乱）：`ModeHUIPages.CreateScrollHost` 复用官方 `UIPrefabs.ScrollRect` 时没摘 content 自带的竖排布局与自适应高度，手动定位的卡片被压成一列小圆点、左半边被裁。现在实例化后 `StripLayoutControllers`（DestroyImmediate，同 `CodexView.EnsureGridLayout`）；规则补进 AGENTS §4.14。
- 看盘页：场次 · 胜利返还倍率一行 + 本场规则一行小字；左右两列列头写合计战力、中缝 VS，每人一张横卡（立绘、名字、状态、右侧装备图标、下方八项属性格，双方同尺度，放得下两行四列用高卡，否则一行八列矮卡，再不够才滚动）。去掉「赛况 / 侦察」（owner 拍板）；「自己调整再开打」直接进整备页签，「完成」回对照页再锁盘。
- 整备页：页签下一行写当前首发 / 接力 / 口令；阵容页左列首发、右列接力（含「接力休息」）；配装页两列、每格带官方物品图标与品质边。
- 押物品页：整卡可点物品格，选中底色染主色、描边常亮主色 + 角标。结算页按内容估高，奖品一排居中，战报单按行数收高。
- 刷新不闪：选人页刷新候选 / 结算点下一场时，已有页面原地盖透明挡板（`ModeHUI.SetPageBusy`），预案分帧备好后同页换内容，选人页新卡错峰升起一次；只有没开页面时才出「准备参赛选手」占位页，占位换正式页不重播面板打开动画。
- Jeff 引导：同一时间只挂一条（`CampaignGuideTable.NextOfferableId`），交付后才挂下一条；菜地 / 陈列要征程第 1 / 2 章设施 token 才排进来，不挡后面的；旧档已同时接下的照常保留。14 条说明与接交提示改成杰夫当面对玩家说的话。
- 崽炫彩（子代理）：蓝 = 薄壁气泡 + 双圈涟漪 + 拉丝水珠，白 = 细光尘 + 虹彩珍珠晶片翻面，绿 = 带叶脉明暗的自然色叶片钟摆飘落 + 柔光孢子；改走共享 `BossRushFxKit.GetShapeMaterial`。共享画师的 Bubble / Leaf 重画、新增 Pearl / Ripple，这两种形状全仓只有遗种巢在用；其余 10 种形状逐像素与 HEAD 相同。

**取舍与回退**：侦察入口删除后 `TryApplyRecon` 与 reconChoices 数据保留（旧档已揭示结果、执行回归仍用），`ModeHReachabilityGuard` 不再要求生产调用方；回退即恢复 `AppendReconLinesAndActions` / `ApplyRecon` 与该守卫条目。引导链回退：删掉 `CanOffer` 里 `NextOfferableId` 那一条件。结算页收高回退：`ResolvePanelSize` 直接返回 `ReportPanelSize`。

**验证（L2）**：全量守卫 669 PASS / 0 FAIL；执行回归 ModeH 10 个、ContentTransactions（新增引导链 4 条断言）、CampaignPlayability、ManualSeptemberReview 全 PASS（ModeHMarketAudit 补了阵容两列与本场规则小字断言）。Windows 正式构建 `Build succeeded!`（唯一原有 CS0649）；14 个 Dev 标识 absent；部署 DLL 与 `Build/` SHA-256 均为 `20D0234E30885DE383884BD48A8AE085ACBBB2154B3A889975054790583782A9`，72 包 SHA-256 一致。Wiki 构建通过，237 页 / 39,141 引用 0 缺失。`git diff --check` 通过。

**L3 未做**：未启动游戏。owner 目检：看盘页两人 / 三敌是否一屏放下、属性格文字不重叠；选人页刷新时面板不关再开；整备页阵容两列对齐、配装图标；押物品页选中态；结算页高度；Jeff 任务页只挂一条、交付后出下一条；崽蓝白绿三色（清单见交付回复）。

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

## 2026-09-25 全仓审查六项修复与提交（COMPAT / SCHEMA+ / SAFE / OPERATIONAL）

**授权与范围**：用户“全部修复确保没问题就提交 commit”。基线 `ab5bb292` 上确认的 2 项 P1、4 项 P2 全部修复；未证实线索不伪装为 confirmed bug。详见 `CODE_REVIEW_FINDINGS.md` 同日六项及本地 `docs/reports/testing/2026-09-25_full_audit_fixes.md`。

**完成**：
- `CR-2026-09-25-002/003`：Mode H 押注账本仍用原 JSON 字符串 key `BossRush_ModeHCashBet_v1`，schemaVersion=2 兼容 v1。押品盖持久身份并与主角物品树同存；结算先固定输赢/奖品计划，再保存实物与剩余义务，最后结清现金/统计。满包、发送前失败与部分交付留欠账；已到账身份防重发，缺失估值随已收押品同存。恢复只认 TypeID + 唯一身份，缺身份或重复身份不拿同型号另一件顶替。共享 coordinator 重试，零金额阶段保留原现金快照义务。
- `CR-2026-09-25-001`：日报 Store 接受后消费跨天计时，再请求物理保存；物理失败不多推进一天，Store 拒绝仍退避。领取页失败反馈保持。
- `CR-2026-09-11-019` 局部分支：邀请函交付前核接收方、交付后核真实回执，未送达保留账目；通知异常已送达不重发。新增关卡就绪补发，与经济事件共用幂等 owner 并成对退订；不改现金退款算法。
- `CR-2026-09-25-004`：恢复夹具 Offered 选择改点 Cards，Applied 确认仍点 Actions，原 33 条行为断言保留。
- `CR-2026-09-25-005`：本机作者校准 JSON 与霜冠 prefab 对齐仓库已发布姿态，同包寒冰铠甲也同步。只重建 frost_set，新包与仓库原包逐字节相等，作者导出和 ResourceRelease 已对齐；保留作者其余工作，未部署游戏目录。

**取舍与回退**：奖品改为不合并地入包，满包等待空位，避免把不随主角快照保存的地面物当成持久交付。待领奖品未完成时不接受下一笔押注；中英文结算提示与 Wiki 已说明。旧凭据无身份按既有缺失估值补偿。数值表、TypeID、原 key 和赛季 DTO 不变；支持 v2 的版本必须负责结清义务，不能靠删字段或重写玩家档降级回退。恢复落地发奖前须补持久拾取回执。

**验证**：
- Windows 隔离正式编译 `Build succeeded!`，唯一原有 CS0649 警告；GAME_PATH 为临时 Managed 拷贝，无真实部署。DLL SHA-256 `5c05ae9789b00c6d95553f3649b726d070755bc03b4c05630fc3b0fb02e0cbf5`；14 个 Dev 专用标识均不存在。
- 全量执行回归 **60 PASS / 0 FAIL**，保持各夹具原 TargetFramework；使用私有 SDK 10 和复制的 .NET 8 运行时，不修改全局环境。新增 SaveFailureRecovery 141 条断言；ZombieModeEntryDebt 75 条；旧恢复组 33 条。
- 3 个修改守卫的 **13 个落盘反向探针**在隔离副本实跑红于预期断言，每次按字节还原、SHA-256 一致，还原后全绿。全量守卫 **665 PASS / 0 FAIL / 0 KNOWN-RED**（含本机资源，未降级为 source-only）。
- Wiki Windows 构建、80 项导航检查、237 页 / 39,133 引用链接检查通过。源码哈希与正式编译及新夹具输入相符。
- Unity 2022.3.62f3 定向构建 `HELMET_FIT_BUILD_OK`；包内对象和 Transform 均与仓库原包一致，作者导出/ResourceRelease/仓库 SHA-256 都是 `a323666417cd170f1941f5d616ec0750442426990aeae80ae52879b64fa4e553`。原始日志/备份在 `Build/fixes-20260925/`。

**L3 待 owner**：未启动游戏、读写玩家存档或读取截图，未测真实帧耗时。专用测试档需核对：鸭王杯“押物品”只押同型号第二件 → 中断后“同场重开”判负，第一件不得损失；获胜满包 → 腾出空位等待至少 2 秒 → 重进，无吞奖或重发；报箱签到后正常跨一个自算日只加一天；丧尸入场失败回基地后邀请函实际退回且不重复；霜冠正侧面与跑动、铠甲腹部覆盖由 owner 目检。逐步操作和不合格判据见交付报告。

**文档/提交范围**：契约、Mode H/日报/丧尸/装备专题、中英文 Mode H Wiki、测试说明及 findings 同步。只提交本轮代码与文档，不提交 Build、DLL、bundle、作者工程其余改动；不 push。


## 2026-09-26 模块解耦 P0–P6 离线完成（SAFE / COMPAT / OPERATIONAL，L3 待验收）

检查点 `3323e33e`、`adef32ef`、`403a09a4`、P3/P4 至 `cae48e91`、P5 `6409a0f4`，本节所在提交完成 P6。宿主 202 文件 / 103,052 行降至 58 / 20,114；47 模块覆盖 1,091 编译源；自动导入 38,854 → 17,783 B。完整成员样本中四类源码读量上升，未宣称普遍降本；逐文件后测见 `architecture/CONTEXT_BASELINE.md`。

修复 `CR-2026-09-26-001`：票 ID 字段迁属性后地图 GetField 失效，改读注册 owner；生产注册/费用回归先红后绿。最终 697 全量守卫 PASS（0 新红/基线红）、95 全量回归 PASS（0 FAIL），Windows 正式/Dev 构建、14 Dev 标识、72 bundle 哈希通过；P5 42 + 29 项结构变异命中原断言并按字节/SHA 恢复。历史与最终证据路径、全部检查点、两种构建 SHA 见 `architecture/MIGRATION_STATUS.md`。

最终正式部署到真实游戏目录，源/目标 SHA-256 一致：`47F26AD82B1E67BFE1ECB0C728A8E97B40EBA80745790694B54AA7AD184A88B3`，Dev 标识 absent。未启动游戏或读写玩家存档；实机清单 `architecture/MIGRATION_ACCEPTANCE.md` 全部 MANUAL_PENDING，含 G 第二局、跨模式/异常退出、自然撤离、共享 UI、装备/建筑和 F3 看图项。没有 L3 或性能采样结论。

## 归档索引

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


## 2026-09-26 丧尸模式 Boss 表现、击杀图鉴与完整链路（COMPAT / WIRE+ / SAFE）

- 修复 CR-2026-09-26-111～113：护盾/减伤提前到死亡前，以实际 finalDamage 单次消费；极速追猎从预警前瞬移改为先预警后真实冲刺、实际落点结算；五类 Boss 增加独立轮廓、技能脉冲、官方姓名血条，跟随官方 modelRoot。
- 复用共享 FX 材质/粒子、已有冲刺组件与既有 Health.Hurt 补丁，不新建宿主 partial。实例装甲与发光缝各一网格，粒子上限 20，暂停/死亡/旧 run/销毁路径清理。新增两源文件已进 CRLF 正式编译清单；宿主引用计数与归类随新增入口同步。
- 图鉴生产代码未改：五类 marker、玩家归属、普通丧尸过滤与实例去重已有链路可靠性证据；本轮复查采集→入队→回基地 flush，仍需真实槽重启确认。没有改 TypeID、图鉴 key、存档 schema 或经济参数。
- L2：丧尸 151 guards 全绿；IntegrationThirdReviewFixes 新增 34 条断言并保持原 127 条通过，ContentThirdReviewFixes、ZombieModeEntryDebt、SaveFailureRecovery 通过。11 次反向拒绝检查按字节还原。双语 Wiki 独立构建通过，导航 80 条，237 页 / 39,145 引用零缺失。
- Windows 正式编译使用独立 `17b1d360` 基线 + 本轮生产改动快照，Managed 使用真实拷贝；产物 `Build/zombie-audit-20260926/source/Build/BossRush.dll`，SHA-256 `CBDC36E1C7B57FCF047FF99091FC89820497D0B1B386BF641AFB68ABB161A6F6`，14 个 Dev 标识全部缺席。未覆盖实际游戏目录；独立快照不包含其它会话新功能，不视作全仓集成产物。
- 取舍与回退：沿原伤害/距离/冷却，Hunter 复用既有移动链；外形用代码轮廓避免增加加载与资源管线，可独立撤去 Attach/Pulse 和编译条目；冲刺可独立撤去接线与可选 impact 参数，不影响图鉴与防御。防御回退会恢复致死漏盾缺陷。
- L3 未做：未启动游戏、未访问玩家存档、未读取游戏截图。外形是否帅、技能是否易读、墙体碰撞、同屏帧耗、回基地及重启的图鉴、连续两局清理，交 owner 按 [完整报告的步骤与看图表](docs/reports/reviews/2026-09-26_丧尸模式Boss与完整链路审查.md) 验收。没有提交、推送或发布。

本专题最终全量门禁：672 PASS / 0 FAIL / 0 KNOWN-RED，diff --check 通过；编译所用本轮生产源码与当前工作区 SHA 一致，两份丧尸 Wiki 生成页也逐字节一致。证据归档于 `Build/zombie-audit-20260926/`。


## 2026-09-26 入场 Mod 兼容与重铸后坐力（COMPAT / WIRE+ / SAFE / OPERATIONAL）

- **已修**：自有地图点击先记录目标，回退共用条件/票数门；宿主清理前过滤非关卡附加资源 scene，DEMO 精确匹配待入场目标。
- **首次直接出生**：核对“鸭科夫源码”及实际 DLL 的异步初始化；官方确认成功后冻结一次入场资格，以 GetPlayerStartLocation 的子场景/坐标贯通首次创建、官方子场景加载定位与最终 startPos。目标已到达后仅保留 NPC/玩法初始化，不再二次搬人。取消/关闭/普通入图清理资格；配套补丁缺失或失败时保留旧传送。Mode E/H 与 DEMO 的 F/G 沿用各自原出场，直接传送兜底及第三方绕开官方选择器时仍走旧流程。
- **已修**：重铸倾向按官方极性换算收益，RecoilScaleV/H 越低越好；颜色、箭头、极值与揭晓同步。旧 RF_* 数值不迁移不反号，收费/幅度/边界/锁定不变。
- **L2**：changed-only 404 PASS；生产执行 490 + 102 条断言通过；7 次入场/重铸守卫、6 次执行反向验证及 1 次引用分类反向验证转红并按字节/SHA 还原。Wiki 构建、237 页/39,211 引用及 80 导航检查通过。
- **交付**：Windows 正式编译部署，Build/游戏 DLL SHA-256 均为 `FB848500F234707BF9339ADB791770C7B3DCA8C0C55E46F28004AA65E9D3D150`，14 个 Dev 标识缺席，72 包哈希一致；本轮不重包；另从暂存区导出 1037 个生产源，按正式参数独立 Windows 编译通过，确认提交不依赖其它会话未提交的源码。按用户要求仅提交本次修复，共享文件分离暂存，不含其它会话工作；未推送、发布、启动游戏或访问玩家档。
- **待 L3**：秘法纪元/出场 Mod 实际组合、首次可见画面、物理落地及性能未实测。复测：带原冲突物品票进 DEMO/零号区，首次画面在目标点且有 NPC/难度与首波 Boss；地下/冷库子图正确；取消后普通出击不受接管；E/H 等保留原流程；叮当重铸后坐力下降绿、上升红，旧装备不反号。详细步骤和回退见 [交付报告](docs/reports/reviews/入场Mod兼容与重铸后坐力修复_2026-09-26.md)。

## 2026-09-27 丧尸模式 Boss 表现、官方计数与补丁隔离（COMPAT / WIRE+ / SAFE）

- 修复 CR-2026-09-27-101～105，106 延期。Boss 特效改按世界米数（线宽 0.06 m、余烬显式 Local 缩放），背刺改暗甲包发光刃尖，五类各一枚脚下纹章（压在预警圈下），技能起手 / 死亡复用共享 `BossRushFxKit.PlayBurst`，追猎冲刺拖尾；血条加官方 Boss 图标。
- 显示用 preset 副本在致死一击扣血前换回原 preset：官方击杀计数与击杀丧尸任务恢复记在 `Cname_Zombie`，不再新增官方存档键。
- 丧尸减伤 Transpiler 拆成独立补丁类，IL 失配不再连带共享 Hurt 上下文补丁（Mode G 屏障、逆鳞、Boss 致死钳制）。Projectile 类 run-only 记录登记时摘除已销毁项。
- 没有改 TypeID、图鉴 key、存档 schema、本地化 key、伤害 / 冷却 / 经济数值；没有新增源文件。
- L2：全量守卫本轮文件全绿（两条红来自其他会话未提交的后山变身改动）；7 次反向验证按字节还原；`IntegrationThirdReviewFixes`（37 条丧尸断言，新增 3）、`ContentThirdReviewFixes`、`ZombieModeEntryDebt` 通过；在线 Wiki 临时副本构建通过。
- Windows 正式编译：HEAD + 本轮生产改动快照，`Build succeeded!`，DLL SHA-256 `5EE64134357908561D8742D4BBAD94CA1379CD159211A60DE1582146EA1DB23E`，14 个 Dev 标识缺席。未部署游戏目录。
- L3 未做：没有启动游戏、读写存档或看截图。外形是否帅、纹章是否好认、特效是否遮挡、帧耗、官方任务计数，按 [复审报告](docs/reports/reviews/2026-09-27_丧尸模式Boss表现与链路复审.md) 第 5 节清单由 owner 验收。
- 回退：纹章 / 拖尾 / 爆发各自独立可删；致死前还原与独立补丁类不要回退（分别带回 102、104）。

## 2026-09-27（第二轮）追猎狂暴常驻与残留区死因（COMPAT）

- owner 定「狂暴就是一直」：追猎低于 30% 血触发狂暴后持续到死亡，删掉 15 秒到期、体型回弹和 `HunterFrenzyDurationSeconds` / `FrenzyEndTime` / `FrenzyOriginalScale`。此前到期后下一击立刻重触发，体型在 1 与 1.08 倍间来回跳。`ZombieModeRuntimePauseRegressionGuard` 改为禁止重新引入到期时钟。
- 修 CR-2026-09-27-106：丧尸 Boss 尸体销毁延迟经 `BossRushEagerReflectionCache.Health_DeadDestroyDelay` 延到最长残留区 + 1 s（9 s），死后再挂回显示 preset，残留腐蚀区 / 毒径 / 死亡毒云害死玩家时结算页显示该 Boss 名而不是「自己」。击杀计数仍在致死前换回的原 preset 名下；残留区伤害、半径、时长不变，恢复监控与 Boss 实例都按已死过滤。
- 残余边界：若丧尸模式暂停但官方时间照走，超过 9 s 的残留区末段仍会回退到玩家来源（现有时长下不会发生）。
- L2：全量守卫本轮文件全绿（唯一红 `OfficialCompileListFileExistenceGuard` 是其他会话新增未登记的 `Utilities/OfficialQuests/OfficialQuestItemRules.cs`）；3 次反向验证按字节还原；`IntegrationThirdReviewFixes`、`AffixCombat`、`ContentThirdReviewFixes`、`ZombieModeEntryDebt` 通过；在线 Wiki 临时副本构建通过。
- Windows 正式编译：HEAD `61e77e6a` + 本轮生产改动快照，`Build succeeded!`，DLL SHA-256 `9A614B50108AD8B86F8D0B2184BAC268C56CF708C1248640001090B8503DCCE7`，14 个 Dev 标识缺席。未部署游戏目录。
- L3 待 owner：把追猎打到三成血以下，确认狂暴后体型不再忽大忽小、一直保持加速到死；站在腐蚀 Boss 死亡毒云里被毒死，结算页死因应是「腐蚀地面」而不是「自己」。

## 2026-09-29 天空岛发布前生产级审查与修复（COMPAT / SAFE / OPERATIONAL）

- 三切片只读审查（会话 / 存档 / 剧情；战斗 / Boss / 云蚋；UI / 世界 / 采集），主会话逐条对码后修 CR-2026-09-29-101～117（112 按 owner 拍板「首杀只认主角击杀」，口径同官方击杀计数）；C-03 refuted。未发现 P0 / P1。
- 玩法数值只动一处：断风冲步停步距离 2.5 → 1.2 m（105，理由见 findings）；截信人伸手圈半径改为结算判定 2.9 m（106）；剧情挑战在 12 名上限外预留 3 名（108）。其余为健壮性、生命周期、文案与文档。
- 新增源文件 `SkyIsland/SkyIslandSessionTick.cs`（已登记编译清单）；没有改 TypeID、存档 schema、本地化 key、官方任务。
- 守卫同步：`SkyIslandFrameProfileGuard`（分段标记改按 `TickSubsystems` 核，新增 Update 必须调用它的反向探针）、`SkyIslandChatterGuard` / `ContentExpansionGuard` / `HudGuard` / `PlayabilityGuard` / `ResidentsGuard` 改读整个会话类；4 次反向验证按字节还原。
- L2：全量守卫 717/717；`run_runtime_regressions --filter SkyIsland` 14/14（`SkyIslandHudPolicy` 42002 条断言含新增警示过期 3 条，`SkyIslandStory` 含冲步新断言）。
- Windows 正式编译 0 错误（天空岛 CS0649 消失），`Build/BossRush.dll` 与游戏目录 SHA-256 一致 `EDBE0CF5609C961253A31EC984CD8F67B12C550D165BF3E8F3A47FB10D49BDD6`，Dev 独有串缺席。注意：该构建含另一会话同时在改、未提交的 ModeH 工作区改动。
- 未做 / 待 owner：部署前 Dev 构建拦截（构建流水线改造）；头目自动组被远处未清敌群占满名额时整组不刷（需在性能预算与内容密度间取舍）。
- L3 未做：没有启动游戏、读写存档。实机清单见 `docs/guides/sky-island/天空岛_待人工验证清单.md`「2026-09-29 发布前审查新增」。
- 回退：各项彼此独立；105 / 106 / 108 是数值常量，单独回退即可。
