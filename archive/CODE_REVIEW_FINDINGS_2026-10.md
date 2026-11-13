# 2026-10 已闭环代码审查原文归档

本文件保留历史原文；Fixed 仅指所记证据级别，实机待验状态不因归档关闭。

## 2026-10-13 玩家反馈实现全面复审与修复（Fixed，COMPAT，L1/L2）

审查基线为 `f3bb8e3b` 之后当时未提交工作区（125 个已跟踪改动文件、4 个新增 C# 文件）。随后按 owner「修复所有你觉得需要修复的地方」完成 CR-001～011；修复沿用库存事务、保存协调器、入口与排序流程，未增加生产 C# 文件或存档字段。下列位置和复现结果保留审查时快照，修复结果见本节闭环表。未部署、启动游戏或访问玩家存档，Fixed 只表示 L1/L2 闭环。

### CR-2026-10-13-001 — P1 / Fixed / COMPAT：解锁材料扣除后异常会重新锁回属性

- 位置：`Integration/Reforge/ReforgeUIManager.cs:119–123`；依赖 `Integration/ItemFactory.cs:496–554`。
- 触发与影响：官方库存先修改数量/移除物品，再发通知；通知抛异常时 `ConsumeItem` 返回 false，新入口将已经解除的属性重新锁回。玩家损失回火液却没有获得解除效果。
- 证据 L1/L2：抽取原样生产消耗、锁定/解锁与点击方法，模拟官方提交后通知异常。正常路径 `2→1, locked=false`；部分堆叠异常 `2→1, locked=true`；最后一件移除异常 `1→0, locked=true`。探针：`Build/review-20261013/reforge_probe.py`。
- 最小修复与验证：复用现有库存预留/回滚事务形态，按实际物品与锁定结果提交，不能以捕获异常后的 false 判断“尚未扣除”。补部分堆叠、最后一件、通知异常及失败回滚的执行回归；REF-01 实机在蓝色锁定行按当前确认流程操作，材料与锁定状态不一致即不合格。

### CR-2026-10-13-003 — P1 / Fixed / COMPAT：旧欠票未清仍允许再次付票，连续失败丢失退款责任

- 位置：`ModeH/ModeHTicketRefund.cs:57,67–86`、`ModeH/ModeHEntry.cs:136–142`。
- 触发与影响：首次退票失败留下 `_owedSlot`；下次入口补发仍失败，却继续打开选图并允许支付新票。再次退款失败只覆盖同一槽号，两张欠票最终只退一张。
- 证据 L1/L2：正式入口调用链已核对；独立探针链接完整生产退款类，两次输出 Owed，恢复发票后仅创建 1 张且 `HasOwedRefund=false`。探针：`Build/entry-review-20261013/prepare.py`。
- 最小修复与验证：让结清方法返回结果，同槽欠票未结清时保留责任、阻止新付款入口并提示重试，无需增加多笔债务系统。补持续失败后恢复的正式入口回归；MH-01 实机连续入场失败后在背包/脚下核对退票，少票不合格。

### CR-2026-10-13-004 — P1 / Fixed / COMPAT：白手起家仍把创建武器视为交付成功

- 位置：`ModeD/ModeDEquipment_StarterKit.cs:61–65,123,151–161`。
- 触发与影响：`lastGivenWeapon` 在装备前赋值；装备失败后忽略入包返回值。两处均拒绝交付或交付前异常时，新的成功判据仍为 true，模式继续启动，玩家却没有开局武器。本项是 CR-2026-10-12-003 的修复遗漏。
- 证据 L1/L2：抽取真实 `GivePlayerStarterKit` 与 `GiveRandomWeapon`，模拟官方明确失败返回，结果 `success=true, player items=0`；现有 ModeDEntryOwnership 替换了真实物品池，不能覆盖该路径。探针同 CR-003。
- 最小修复与验证：让武器发放返回真实交付结果，确认装备或入包后设置后续弹药引用；异常先核对归属，只清理未交付实例。补两种拒绝与提交后通知异常；MD-01 实机进入白手起家检查武器槽/背包，无武器却进入战斗不合格。

### CR-2026-10-13-005 — P1 / Fixed / COMPAT：喂食通知异常导致食物损失且经验不增加

- 位置：`PetNest/PetNestCompanionCare.cs:148–152`，与 CR-001 共用 `ItemFactory.ConsumeItem` 的提交判定问题。
- 触发与影响：食物已经移除，通知抛错后消耗函数返回 false；新喂食入口中止经验/冷却事务，`consumed` 仍为 false，也不执行返还。
- 证据 L1/L2：原样生产喂食、选食物、消耗及提交方法的隔离执行确认食物从 1 变 0、经验仍为 0。探针：`Build/review-20261013-pets/probe.py`。
- 最小修复与验证：和 CR-001 共用可靠的消耗收据/回滚方式，保留具体对象与原数量，不靠重新创建同 TypeID 代替原资产恢复。补消费后的通知异常；PET-01 实机通过宠物“喂食”查看背包和经验，食物减少但无经验不合格。

### CR-2026-10-13-006 — P1 / Fixed / COMPAT：喂食成长与食物消耗未建立同批保存义务

- 位置：`PetNest/PetNestCompanionCare.cs:146–157`；保存链 `PetNestRuntimeModule.cs:221` → `PetNestSaveCoordinator.cs:185–187` → 共享保存引擎。
- 触发与影响：成功喂食后，在下一次官方 CollectSaveData 前卸载 Mod/销毁宿主，且没有其他操作登记主角资产快照。成长与冷却已 StageCommit，但没有调用现有资产屏障；CollectPendingAssets 第 105–106 行直接返回，官方 SaveFile 仅落已有缓存，可能保存新成长和旧食物快照。
- 证据 L1/L2：探针链接实际共享保存引擎与节流器、原样生产资产采集，宿主销毁落盘结果 `exp=20, food=1, inventory captures=0`；官方 `SavesSystem.SaveFile:503–519` 不触发采集。探针同 CR-005。
- 最小修复与验证：消费前登记局内可用的 `RequireBackpackAssetSnapshot`，沿现有协调器将消费、经验和冷却作为一批提交/落盘，并保留失败重试义务；不另建存档管线。补宿主销毁及落盘失败重试；PET-02 实机喂食后卸载/重载并检查食物与成长，二者恢复到不同时间点不合格。

### CR-2026-10-13-002 — P2 / Fixed / COMPAT：新增一次性好感赠礼可能反复发放

- 位置：`Integration/Reforge/GoblinReforgeInteractable.cs:459–465`；依赖 `Integration/NPCs/Goblin/GoblinNPCReward.cs:58–78`。
- 触发与影响：回火液已进入背包后库存通知抛错，旧赠礼辅助方法返回 false，新一次性奖励入口便不写 claimed 标记。再次聊天再次发放。本项限定新增回火液赠礼，不扩大为旧奖励系统整体审计。
- 证据 L1/L2：抽取真实赠礼方法，并执行新领取条件，连续两次得到 `claimed=false, owned=4`。探针同 CR-001。
- 最小修复与验证：核对实际交付凭据后标记领取，提示失败不能改写交付结果；补已入包后通知异常和重复聊天。GIFT-01 实机达到好感门槛后连续聊天，奖励多于一次不合格。

### CR-2026-10-13-007 — P2 / Fixed / COMPAT：基地宠物照料入口全部被禁用

- 位置：`PetNest/PetNestCompanionCare.cs:330–331`；基地装配在 `PetNestBaseIdleSpawner.cs:175`、`PetNestCompanionSpawner.cs:530`。
- 触发与影响：基地由 BaseIdleSpawner 持有宠物，场景流程清空局内 CompanionRuntime；新选项却要求后者的 CompanionCharacter 非空。基地已有喂食、摸摸、行为选项，但无法操作。
- 证据 L1/L2：正式基地生命周期与装配链可达；真实 IsInteractable 在基地 owner 为空时返回 false。探针同 CR-005。
- 最小修复与验证：选项绑定实际宠物实例与 PetId，行为设定沿现有记录保存、出击时应用。补基地 owner 用例；PET-03 实机在基地选中宠物依次操作三项，任一无故禁用不合格。

### CR-2026-10-13-008 — P2 / Fixed / COMPAT：切换语言后照料菜单继续显示旧语言

- 位置：`PetNest/PetNestCompanionCare.cs:245–248`。
- 触发与影响：生成时将当前语言的文字写进不分语言的全局 override，仅生成/切行为时重新注入；之后切语言仍显示旧的喂食、摸摸和行为文本。
- 证据 L1/L2：官方 override 机制与所有注入调用点已核对；原样注入探针中文生成后切英文，文字仍为中文。探针同 CR-005。
- 最小修复与验证：复用现有语言切换事件由 runtime owner 刷新现存选项，或显示时解析当前语言；保持幂等订阅/清理。PET-04 实机保持宠物存在并切换中英文，菜单残留旧语言不合格。

### CR-2026-10-13-009 — P2 / Fixed / COMPAT：旧远征死亡结果会同时显示死亡率 0%

- 位置：`PetNest/PetNestExpeditionService.cs:127–130`、`PetNest/PetNestExpeditionRevealView.cs:552`。
- 触发与影响：更新前已经结算、尚未翻牌的风浪档死亡记录，仍保留死亡结果；新增统一概率封顶同时用于历史结果显示，打开 PlayPending 会同时显示“死亡率 0%”与“没有回来”。
- 证据 L1：结算状态、基地 PlayPending 到结果文案的正式链已核对；本项未做独立执行或实机验证。
- 最小修复与验证：当前概率封顶只影响未结算远征；历史结果显示当时记录的概率，不更改旧死亡结果。补升级前已结算/未结算两种记录；PET-05 实机翻旧结果，概率与历史结果相互矛盾不合格。

### CR-2026-10-13-010 — P2 / Fixed / COMPAT：Boss 过滤后第一波仍可能出现龙皇

- 位置：`WavesArena/WavesArenaRuntimeModule_Start.cs:62`，影响 `WavesArenaRuntimeModule.cs:102–113` 的旧排序逻辑。
- 触发与影响：新改动使洗牌后的过滤缓存生效，但前 20 波排除强 Boss 仍按未过滤池下标处理。关闭部分 Boss 后，原池第 21 位龙皇可以成为实际第一波，即使还有 22 个启用普通 Boss。此为 CR-2026-10-12-010 的直接影响面遗漏。
- 证据 L1/L2：抽取真实启动、过滤、预处理方法，合法洗牌数据包含 20 个关闭项、22 个普通启用项、1 个龙皇，输出 `raw index=20, enabled index=0, first=boss_dragonking`。探针：`Build/dragon-review-probe/probe.py`。
- 最小修复与验证：复用现有强 Boss 集合，按启用 Boss 的实际出场顺序处理前 20 个位置，再重建缓存；不新增配置。补混合开关池；BR-01 实机关闭部分 Boss 多次开局，普通候选足够时前 20 波出现龙皇不合格。

### 既有兼容债务与未证实线索

- CR-2026-10-13-011，P2 / Fixed / COMPAT / L1+L2：`ModeH/ModeHTicketRefund.cs` 迁入的旧交付逻辑，在其他 Mod 使船票可堆叠且合并通知异常时，可能目标堆加一后又将 incoming 落地。默认包 `maxStackCount=1`，不满足触发条件；不得写成默认必现。旧探针输出 `target=2, dropped=1`。现复用共享事务按实际合并差额修正剩余原件，只有未合并余量进入不合堆入包/落地；新增堆叠兼容执行回归与旧码反例均通过。
- 兼容加固（原 UNVERIFIED，不升级为默认可达故障）：付款回执绑定选中费用数组身份、generation 与槽位；对照当前游戏 DLL 的 Cost/LoadTask/Pay 链确认真实付款保留同一数组，回归覆盖另一费用数组的同价付款拒收。
- 兼容加固（原 UNVERIFIED）：场景回调不再推定付款；取消清理 H planned 与地图意图，换槽复用现有 RestoreForSlotChange 清入口；取得 run/租约前核对真实收据。正式清槽链已复核，相关真实方法断言通过。
- UNVERIFIED：若第三方更早的 OnCostPaid 订阅者抛异常，仍可能阻断本订阅收据；缺玩家现场证据，本轮未整体替换事件/Harmony 策略，需在具体 Mod 组合中验证。
- UNVERIFIED：宿命回响近身龙裔出生点未新增可达性校验；缺真实地图阻挡证据，保留墙边/平台/不同楼层 L3 检查。
- 天空岛修改确实将 `SpawnTomb=false` 改为放行并保留 SaveCharacter 门槛；这不能单凭旧复合条件日志证明玩家当次一定由 NoDeathDrops 导致。本轮结束时用户所给 `C:/Users/GASEN/Downloads/Player(1).log` 路径不存在，仓库 `Player.log` 是 8 月旧文件，未拿它替代当次日志。
- 范围差异：当前是两次点击确认解锁、亡命档保留死亡、宠物装备延期，已有 10-12 台账称当轮确认；本次不把这些设计差异当代码 bug。

### 修复闭环（COMPAT，L1/L2）

| ID | 已实施的最小修复 | 执行证据 |
| --- | --- | --- |
| 001 | 锁定/解锁共用现有材料预留事务，按真实属性标记提交；失败归还原件，阻止点击重入与行换绑。 | ReforgeAssetTransactions 38 项；旧解锁代码 5 项转红。 |
| 002 | 赠礼共用合堆差额收据与真实入包/落地归属，显示失败不否定交付；防重入，领取前要求好感存储可写。 | 同上；旧赠礼代码 6 项转红，含提交后通知异常。 |
| 003 / 004 / 011 | 欠票未还阻止新付款；退款按真实差额交付；白手起家仅在武器确实属于玩家时承认成功。 | ModeEntryPaymentDelivery 50 项；旧码三类反例转红。 |
| 005 / 006 | 食物使用原件预留/回滚；消费前登记资产快照义务，成长接纳后沿原协调器落盘/重试；表现失败不撤销资产结果。 | PetNestCare 37 项；ContentTransactions 609 项含真实保存链 8 项；旧喂食代码反例转红。 |
| 007 / 008 / 009 | 基地选项绑定实际宠物与槽位；复用现有语言重注入；已结算远征保留历史概率，在途记录按新规则结算。 | PetNestCare、CompanionActivation 27 项、Growth 51 项；旧行为 9 类失败场景。 |
| 010 | 按过滤后实际启用顺序保护前 20 波，在启动时完成一次排序并复用缓存失效。 | AuditModeLifecycle 新增 11 项；旧排序方法反例转红。 |

修改过的重铸、Mode H、宠物守卫均做隔离副本「通过→破坏后失败→按字节还原后通过」，证据分别在 `Build/fix-20261013/reforge-negative/`、`Build/entry-fix-baseline/guard-reverse/`、`Build/review-20261013-pets/guard-negative/`。修复未提高宿主 partial 预算，58 个文件仍为 19948 行；新增工作仅在点击、入场和既有语言事件触发，不增加每帧扫描。

修复后 Windows 真编译 1196 个源文件通过（0 错误、2 个既有 CS0649），隔离 DLL SHA-256 `e584696a31e03de5c6454ea7819442774a51c56215ed916317f37c5129343ab7`，源码清单在 `Build/fix-20261013/compile/manifest.json`。全量回归初跑 127/128，通过补齐旧 ModeHReviewFixes 夹具对真实退款清理方法的依赖，重试 70 项通过，最终 128/128；未用恒成功替身代替生产逻辑。初次汇总与重试分别保留在 `Build/fix-20261013/runtime-results.json`、`Build/entry-fix-baseline/ModeHReviewFixes-results.json`。

资源阻断同步闭环（SAFE / OPERATIONAL）：从作者原件在隔离工程恢复 `production_icons` 与缺失的 `reload_metronome`，349 个公共图标及既有 348 个 Sprite 的尺寸/PPU 一致；未改发布清单，未部署游戏。完整资源校验发现天空岛检查器仍使用旧的 7 张纹理名，已改为沿 Renderer → Material → _BaseMap 的真实引用验证，复用现行作者元数据与尺寸表，检查 BC7、不可读、完整 mip 和材质绑定，保留原导航/网格判据。真实 99 材质 / 919 材质槽及作者 GUID 引用一致，天空岛包未改；7 类移除判据的隔离反向验证与 6 类真实包记录反例均准确失败。最终发布清单 74/74 包、0 错误，证据 `Build/resource-recovery-20261013/full-resource-verification-final.json`；初次 73/74 结果保留。

745 项守卫均有修复后通过证据：全量初跑 743 通过，宿主预算与退款调用链守卫修正后重跑通过，之后变化的资源属性测试与文档引用检查通过。没有实机或帧耗数据；上方 REF / GIFT / MD / MH / PET / BR 的操作、观察位置与不合格条件仍需 L3，PERF-01 同地图比较无宠物、三种行动方式和密集战斗，观察稳定帧时间、周期尖峰及切图残留。

### 复审阶段验证证据（修复前历史快照）

- 全量守卫 744 项：743 通过，DailyReportArtPropertyTest 唯一失败，原因是本机既有 production_icons 包与清单的 alias/Sprite 不一致；不能写成全部通过。日志 `Build/review-20261012-guards.log`。另核实 release 清单要求的 `Assets/ui/reload_metronome` 缺失，完整发布资源校验未通过。
- Windows 真编译：按当前官方显式清单编译 1196 个源文件、真实游戏/Harmony 引用、C# 7.3、非 Dev，输出到隔离 Build 目录；0 错误、2 个既有 CS0649。不是完整部署流水线通过。哈希和源码清单：`Build/review-20261013/compile/manifest.json`。
- 执行回归 125/125 通过：初次两项因缺环境变量失败，补游戏 DLL/Harmony 拷贝后仅复跑失败项通过；汇总 `Build/review-20261013/runtime-results.json`，保留初次与重试结果。当前夹具未覆盖上述全部新增入口，因此另做独立探针。
- Wiki 构建及 80 项导航检查通过；现金交付独立探针另有 9 项通过，包括金额守恒、堆叠上限、最多 12 堆、到账后广播异常和账户缺失留余额。
- 所有结论上限 L1/L2。没有 L3 或帧耗采样，不能承诺“无性能问题”；PERF-01 仍需在同地图比较无宠物、跟随/主动/回避及多敌人战斗，检查稳定帧时间、周期尖峰和切图后残留任务。本次提出的修复优先沿用库存事务、保存协调器、入口门和现有过滤流程，避免重写模式状态机。

<!-- 归档原文结束；以上原文按字节保留。 -->

## 2026-10-07 Player.log 复现：二次传送与终章误清场（COMPAT，L1/L2）

| ID | 级别 / 状态 / 分类 | 根因与最小修复 |
| --- | --- | --- |
| CR-2026-10-07-017 | P1 / Fixed / COMPAT | `AfterInit` 只代表官方初始化，BossRush 的延迟入口协程还会再次搬人；报名石已在旧落点生成。复用真实路牌 owner：当前场景路牌实际生成后才允许报名石出现，以路牌实际位置为锚点投影地面，与垃圾桶共用同一场地来源。 |
| CR-2026-10-07-018 | P1 / Fixed / COMPAT | 第六章独白、异步生成和决战期间普通波次 `IsActive` 仍为 false，Legacy 持续预清场因此删除女巫；征程检测实例消失收尾并重建石头。直接清场首部检查终章占场；原持续协程在占场时暂停扫描、刷怪器处理和次数预算，失败/退出后恢复。 |

用户提供的 `Player.log` 证明旧交付仍有运行时缺口：先生成石头，后执行 BossRush 传送；女巫生成成功后紧接着清理一个敌人，随后征程自动收尾。CR-016 的 active scene 缓存修复保留，但其“等待 AfterInit 即可避免旧位置”的结论不成立，由 CR-017 补齐。新修复的验证与人工步骤见 [日志复现报告](docs/reports/reviews/2026-10-07-报名石位置与冠军女巫误清场.md)。

当前修复：相关守卫 30 / 30、执行回归 117 / 117、Windows 正式编译及部署哈希一致；征程真实入口/路牌/几何 225 checks、真实预清场 661 assertions。全量守卫 737 / 738，唯一红项为未改动的 local-only 物品 ID 表已写 500104/下一可用 500105，而公开台账仍止于 500103；未修改或放宽该守卫。新制品 L3 未运行，Fixed 仅指本次代码与离线验证闭环。

## 2026-10-07 征程直接关闭与终章报名石（COMPAT，L1/L2）

| ID | 级别 / 状态 / 分类 | 根因与最小修复 |
| --- | --- | --- |
| CR-2026-10-07-015 | P1 / Fixed / COMPAT | 官方采集先消费征程 typed pending，但引擎未登记此路径的物理保存义务；随后未写盘或失败时，Tick 与关闭兜底误判无事可做。成功写入和回读后通过可选回调通知征程原引擎保留 owed/deferred，真正保存成功或切槽才清除。真实采集、关闭补盘、切槽与独立新进程文件恢复回归覆盖。 |
| CR-2026-10-07-016 | P1 / Fixed（场景缓存）；二次传送遗漏见 CR-017 / COMPAT | sceneLoaded 后 active scene 可能晚切换，终章只按 generation 缓存 false 导致报名石整局缺失。缓存加入 active scene.handle，否定结果每秒重试，并等待官方 AfterInit。原回归仅覆盖官方搬人，未覆盖 BossRush 后续二次传送；玩家日志已证实该测试缺口。 |

完整 16 项反馈复核、验证与 RQ-01～03 实机步骤见 [本轮报告](docs/reports/reviews/2026-10-07-征程重启与终章石复核.md)。用户明确关闭前第六章“已接取”；没有读取玩家档、启动游戏或推定已丢进度恢复，Fixed 表示代码和离线证据闭环。

## 2026-10-07 远端合并后的全部改动复审（COMPAT / WIRE+，L1/L2）

用户授权快进合并 `f0711934`，本地全部修复经保护快照恢复，未创建或推送公开提交。当前完整结论见 [合并复审报告](docs/reports/reviews/2026-10-07-全部改动独立深度复审.md)。远端已占用 001～005，以下本地项采用 006～014；两边重复发现的极值支付合并为 001，最终保留“先 Pay、明确拒付且余额未变、确有溢出才 Add”的窄分支。

| ID | 级别 / 状态 / 分类 | 根因、最小修复与 L2 证据 |
| --- | --- | --- |
| CR-2026-10-07-006 | P1 / Fixed / COMPAT / WIRE+ | 官方激活栅栏之后的事件／黑幕异常不会再经过 NextFrame，漏登记或放行原生 operation 使后续切图阻塞。首次 setter 登记，异常与表现结束放行，完成前保留资源；真实 Harmony、现装 DLL 与反向探针通过。 |
| CR-2026-10-07-007 | P1 / Fixed / COMPAT | 未产生岛图时无 sceneUnloaded 可供 Session 清理，成功返家后重复重载基地。无岛实例的返航派发后交回租约；真实 DispatchReturnIfReady 修前红、修后绿。 |
| CR-2026-10-07-008 | P1 / Fixed / COMPAT / WIRE+ | 返航幕布卸掉岛图后再失败，旧释放门放弃重试，目标半恢复角色可能覆写旧快照。保留 returnFailed 并保护失败返航目标的再次采集；原岛与死亡保存继续，租约／Harmony 回归通过。 |
| CR-2026-10-07-009 | P1 / Fixed / COMPAT | 按技能目标判断友军被碰撞入口复用，追敌中的友军龙裔会伤害同营主角。入口按实际碰撞双方 Team.IsEnemy 判断；真实碰撞回归与旧判据反向验证通过。 |
| CR-2026-10-07-010 | P1 / Fixed / COMPAT | 押注键写／回读一次故障留下单向 store fault，资金已扣但结算／物理保存永久受阻。同槽恢复已接受账本，空 replacement 先订阅，保留原协调器现金／物品义务；真实故障、重启、冻结赔率与换槽回归通过。 |
| CR-2026-10-07-011 | P2 / Fixed / COMPAT | 加载立即清理先于基地回调，归巢随从 ID 被清空，漏经验和场次。既有 Update 在清理前留 ID；实际归巢 AddExp 流程修前转红。 |
| CR-2026-10-07-012 | P2 / Fixed / COMPAT | 龙皇护卫龙裔降基础生命后写基值为当前生命，200% 设置时半血登场。改回读 Health.MaxHealth；10/50/100/200% 与旧代码反向回归通过。 |
| CR-2026-10-07-013 | P2 / Fixed / COMPAT | 归巢事务拒绝仍清待办与预算，真实写故障也不恢复。成功接受才消费待办，5 秒维护重试并绑定槽；已接受 pending 真正写入／回读一致后清写故障，未知 schema 屏障保留。生产模块和真实 store／协调器回归及四项反向验证通过。 |
| CR-2026-10-07-014 | P2 / Fixed / COMPAT | 普通非 Boss 预设 fallback 的龙皇／龙裔实例死亡先清 preset，全局入口在组件辨认前早返漏图鉴。两位既有死亡 owner 复用稳定身份入口；两个生产方法、普通 fallback、第三方改 key、归属与去重回归通过，逐个删调用准确转红。 |

合并后的共享内容与 H 独立保存协调器均补 sticky saving 自动恢复回归，H 的测试不再使用非 sticky 替身；荆棘补三档 finalDamage、冷却、来源及递归验证，未改玩法数值。最终全量守卫 736 / 736、一次完整回归 117 / 117、Windows 正式编译及 73 包部署验证通过；源／部署 DLL SHA-256 为 `3A378E05F60EDB9107FDD225450EBD9D2A1788907AFD4C677CFAA7716BC0F034`，14 个 Dev 标识不存在。三件资源网格已确认齐全。详细证据及人工步骤在合并报告；L3 未执行。

## 2026-10-07 最新提交独立深度复审（COMPAT / WIRE+，L1/L2）

重新从玩家入口、生产调用、现装官方 DLL / IL 与失败恢复审查 `af9bcb43`，没有把旧 Fixed 或绿测试当结论。完整逐项结论、反向证据和 R01～R18 实机步骤见 [本轮报告](docs/reports/reviews/2026-10-07-最新提交独立深度复审.md)。下述 Fixed 仅为代码与离线验证闭环；未启动游戏、访问玩家存档或提交 / 推送公开代码。

| ID | 级别 / 状态 / 分类 | 根因与最小修复 |
| --- | --- | --- |
| CR-2026-10-07-001 | P2 / Fixed / COMPAT | 官方 Pay(Cost) 忽略 cashAvailable 的预检仍相加账户与背包现金，unchecked long 溢出误拒合法大额押注。仅此上溢边界改走已有账户 Add(delta)，普通支付和账本流程保留。真实官方预检替身与现装 IL 证明；移除生产分支后金额回归和守卫均转红。 |
| CR-2026-10-07-002 | P1 / Fixed / COMPAT | inactive 巡守根下的官方子 AI 被默认 GetComponentInChildren 漏掉，首次生成或挂起恢复失败。唯一查询补 true；直接执行生产 ConfigureNavigation，旧 HEAD 在 inactive_child_navigation 实际转红，另 8 项隔离变异验证。 |
| CR-2026-10-07-003 | P1 / Fixed / COMPAT | 官方先派发实例死亡再派发全局死亡，女巫实例 owner 先清 preset，使普通击杀也漏图鉴。清理前提交稳定身份并复用原过滤 / 实例去重；三 Boss clone 名统一既有清理约定，第三方改名后仍释放自有副本。真实死亡方法、清理 helper、官方顺序共 471 条断言；移除身份调用及逐个恢复旧 clone 名均使生产回归与守卫转红。 |
| CR-2026-10-07-004 | P1 / Fixed / WIRE+ | 官方 SaveFile 置 saving 后写盘异常没有 finally，后续重试永久被 IsSaving 拦住。共享引擎和 H 的已有唯一写点复用受控同步包装，只释放自己失败调用留下的私有 saving 闩，原异常与 pending 保留。旧 sticky 测试手清闩已纠正；去掉包装后生产回归与新守卫均转红。现装 IL 钉住字段、时序和无 finally。 |
| CR-2026-10-07-005 | P1 / Fixed / COMPAT | 官方对话 UI / actor 失效独立取消终章独白，旧 catch 留下活动闩，记名石持续不可用。仅当前 runId 取消时归还召唤权，旧请求不清后继 owner；恢复旧 catch 后独立取消回归实际转红。 |

保存保护只覆盖共享内容引擎与 H 发起的同步写盘，不接管官方独立保存、Mode G / 好感旧直调；持续 IO 故障、进程终止与已丢历史数据仍有边界。未新增常驻扫描、生产源文件或存档 schema，未放宽守卫。全量守卫 736 / 736；回归覆盖 117 / 117（全量 116 加修正测试替身后复跑，保留逐次结果）；Windows 正式编译 / 73 包部署通过，源 / 部署 DLL SHA-256 `CB1DCB32458ACAA87C9366A04910F1261B3FC4DB435F20D11237491C4752B205` 一致，14 项 Dev 标识检查通过，详见本轮报告。

资源复核：起始全量守卫 735 / 735，源与部署 skyisland_fx 均直接读到三件 LootCrate 网格（199,122 B，SHA-256 `1699BADD7F674CF81B51D24AE28483F4C13E513C30B9FC2E11EFBA9C12FD67E1`），本轮未重打包。CR-2026-10-06-012 的历史缺失在当前制品上不成立，不能继续作为既有红项。旧 10-06 报告本机缺失，以下历史验证数字保留，当前结论以本轮报告为准。

## 2026-10-06 第二轮深审：征程重复报名、宠物全模式、押注全余额与晴岚黑屏（L1/L2）

用户补充与最终实施见 [完整 17 项报告](docs/reports/reviews/2026-10-06-用户反馈审核与修复.md)。本节取代首轮对征程“未复现”、宠物 G／丧尸禁入、押注固定 20,000 和敲钟地图未知的结论。以下已修复项均未作 L3 声明。

| ID | 级别 / 状态 / 分类 | 根因与修复证据 |
| --- | --- | --- |
| CR-2026-10-06-013 | P1 / Fixed / COMPAT | `Campaign/CampaignPersistence.cs:158` 恢复时先 Store 再首次订阅，订阅主动清缓存使已接受进度丢失。已改为先订阅再恢复；真实订阅旧代码转红，三类异常分别经独立写进程／读进程文件回归验证。 |
| CR-2026-10-06-014 | P1 / Fixed / COMPAT | `Campaign/CampaignOfficialQuestClient.cs:103` 故障时仍 Ready，会拿默认空章节刷新 Jeff。读屏障或写故障时暂停投影，防止“报个名”重新可接；现装 Core／EasySave3 IL 契约与官方投影夹具通过。 |
| CR-2026-10-06-015 | P1 / Fixed / COMPAT | `PetNest/PetNestCompanionSpawner.cs` staging 根 inactive 时未包含 inactive 子树，旧基地禁战可能拿不到 AI。包含 inactive 查询后在激活前停战斗树；真实 TryActivate／Agent 回归证明基地禁战、出击新实体正常 AI／伤害，并覆盖换队、换主角、死亡和长加载。 |
| CR-2026-10-06-016 | P2 / Fixed / COMPAT | `MapSelection/BossRushMapSelectionHelper.cs:220` 地图费用仍残留 868 回退，首轮只修 Mode D 漏了此入口。两条入口回退共享发布常量 500001；真实地图费用与注册失败回归通过。 |
| CR-2026-10-06-017 | P1 / Fixed / WIRE+ | `SkyIsland/SkyIslandSceneReferenceBridge.cs` 与 `SkyIslandRaidLease.cs` 在官方加载异常后漏 Hide，且早期分帧等待不观察取消。只补本租约黑幕引用计数，观察四处官方等待并释放目标 operation 的激活阻塞，资源和补丁延迟到各自真正完成再释放。真实 Harmony／现装 DLL／租约回归与两项反向验证通过。 |
| CR-2026-10-06-018 | P1 / Fixed / SCHEMA+ | `ModeH/ModeHCashBetService.cs` 大额乘法、胜率、估值及累计有溢出风险，恢复会按变化后的分数赔付。decimal／long 安全计算，锁盘复验上限和扣款，新增可选冻结分子／分母／胜负结果；旧 v1/v2/v3 可读，小额零返还赢注和 long.MinValue 展示均正确。 |

按用户裁决，宠物仅 H（含待入场）禁止；G 清场／Boss 结案沿用现有豁免，丧尸清场显式排除宠物身份，宠物攻击正常消费 Boss 防御；不把宠物计为敌人。押注只扣本金，上限为 min(余额, floor(余额÷本场真实返还倍率))，取消固定 20,000 上限，UI 用归一化整数进度，大额不经过 float。

补充黑屏兼容：当前源与部署包 shader 判包通过；`SkyIslandRendering` 入场按 shader 去重校验 GBuffer，旧 Forward-only 包虽然 isSupported 也不会静默进入黑地形。天空岛“归航钟”没有女巫生成调用，敲钟写 Ending 后向钟守交付。

验证：117 / 117 全量执行回归，734 / 735 全量守卫；唯一失败仍是首轮 CR-012 既有特效包缺三件搜刮箱网格。Windows 正式编译／部署通过，DLL SHA-256 `0F109A2031B81F4EA376AF862F72AEE86624DD44A851E46B61C6348F4B7F883C`，14 项 Dev 标识检查通过，未启动游戏／读取玩家存档。

## 2026-10-06 玩家 16 项反馈审核（COMPAT / SCHEMA+，L1/L2）

逐项结论、生产路径、故障边界与 UF-01～15 实机步骤见 [审核报告](docs/reports/reviews/2026-10-06-用户反馈审核与修复.md)。本轮未启动游戏、未读取或修改玩家存档。以下 Fixed 只表示代码与离线验证闭环，不表示 L3 已通过。

| ID | 级别 / 状态 | 证据与修复 |
| --- | --- | --- |
| CR-2026-10-06-001 | P1 / Fixed / COMPAT | `ZombieMode/ZombieModeSpawner.cs` 的 Boss 全图点可落在追踪范围外；复用最近可达点、拥挤环和追踪距离，ZombieModeSpawnRuntime 覆盖。 |
| CR-2026-10-06-002 | P1 / Fixed / COMPAT | `PetNest/PetNestModeGate.cs` 允许名单排除普通出击；最终按用户裁决允许所有游戏地图与模式，仅 H（含待入场）禁止，基地由独立闲逛 owner 管理。PetNestGrowth 覆盖；旧 G／丧尸禁入描述已撤销。 |
| CR-2026-10-06-003 | P1 / Fixed / COMPAT | 基地宠物未禁战；`PetNestCompanionSpawner.cs` 显示前停官方 AI 子树并复用跟随，基地调用显式 allowCombat:false，ManualSeptemberReview 覆盖。 |
| CR-2026-10-06-004 | P1 / Fixed / COMPAT | `SkyIslandSession.cs` 与 `ModeHRuntimeModule_SceneFlow.cs` 在 A* 正扫描时立即扫描或选址，会触发返家；复用有界等待，导航未就绪不可提前接管。 |
| CR-2026-10-06-005 | P1 / Fixed / COMPAT | `CampaignFinalBoss.cs` 把竞技场环境已激活误判成普通波次在战斗，挡住记名石；改为同时检查 IsActive，CampaignPlayability 覆盖。 |
| CR-2026-10-06-006 | P1 / Fixed / COMPAT | `ModeD/ModeD.cs` 注册未就绪时把船票 ID 回退成 868；改用共享 500001 常量，ModeDEntryOwnership 覆盖。 |
| CR-2026-10-06-007 | P1 / Fixed / COMPAT | 龙皇、女巫注册被旧 static 成功标记阻断，目录清空后无法补登记；改查当前目录，WavesArenaPresetWeight 覆盖三 Boss 重建。 |
| CR-2026-10-06-008 | P2 / Fixed / COMPAT | 女巫基础预设查找缓存失败或销毁对象，迟到资源无法恢复；仅成功有效结果早返，执行回归覆盖迟到与 Unity 销毁语义。 |
| CR-2026-10-06-009 | P2 / Fixed / COMPAT | `CodexKillCollector.cs` 仅用可变 nameKey 归档，第三方改名可把龙皇记成观测者；复用已有能力组件识别，普通杂兵轻门与后续命中 O(1) 保留，图鉴回归覆盖。 |
| CR-2026-10-06-010 | P1 / Fixed / COMPAT | Mode E 猎犬友军恢复强追玩家，龙裔技能固定指主角；按友军身份禁强追、共用官方 AI 敌对目标，ModeECombatTargeting 覆盖。 |
| CR-2026-10-06-011 | P2 / Fixed / COMPAT | `ModeDRuntimeModule_Waves.cs` 对已乘生命比例的 Value 求 Add 增量会二次缩放并让当前血量与上限不一致；改为每波 3% 整体乘算并回读上限，BossHealthScaling 覆盖 50/100/200。 |
| CR-2026-10-06-012 | P2 / Rechecked 2026-10-07 / OPERATIONAL | 10-06 历史记录为特效包缺三件网格；10-07 源 / 部署包均直接读到三件，原守卫通过。当前资源缺口不成立，本轮未重打包，未把资源存在当画面验收。 |

首轮待确认项已重新调查：征程故障恢复重置见 CR-013／014；天空岛归航钟没有女巫生成步骤。本次按用户明确的鸭王杯 Mode H 复核，仍未从当前生产链证实“正常观众被全员攻击”。未接序章刷零号区守卫被当前前置／异步复核门反证。随机敌人 Mod 全面兼容性未实机测试。

验证：115 / 115 执行回归；734 / 735 全量守卫，唯一失败 CR-012；Windows 正式编译通过，1148 源文件，正式 DLL 源与目标 SHA-256 `1875537F...97E2F93B` 一致，Dev 标识检查通过。详细完整哈希和测试边界见报告。

## 2026-10-29 HEAD a8aed728 全面复审（4 项已于 2026-10-30 修复；COMPAT，L3 待验）

起点工作区干净，审查对象为 HEAD `a8aed728`。逐调用链复核 `41360aa4..a8aed728` 最新改动及其直接影响面，同时抽查未改动大模块的关键入口、退出、异步回调和存档边界。覆盖共享持久层、遗种巢、成就、好感、野外趣事、霜之哀伤、F3、标准竞技场、Mode D–H、丧尸、征程、天空岛和随机事件；全仓守卫与已有执行回归均运行。大模块采用关键路径抽查，不表示所有源码逐行或所有状态组合穷举。

2026-10-29 仅审查与登记；2026-10-30 按 owner「全部修复」完成以下四项生产修复及回归。下面保留原始触发与审查证据，不将历史报告作为确认依据，也不表示四项均由最后一个提交引入。没有提交、部署到真实游戏目录、启动游戏或访问玩家存档。

### CR-2026-10-29-008 · P1 / COMPAT · 零到账的远征补发失败仍可阻塞出击中的崽包保存

- 状态：Fixed（2026-10-30），L1 + L2，L3 待验；CR-2026-10-29-007 修复后的残余分支。
- 位置：`PetNest/PetNestExpeditionService.cs:585-590`、`:629-637`；后续阻塞在 `PetNest/PetNestSaveCoordinator.cs:148`。
- 触发：基地里有已结算欠奖，现金已经到账，但剩余物品的真实 prefab 暂时不可用。补发先调用 `RequireAssetSnapshot` 登记必须采集基地仓库的义务，实际没有调用现金或物品交付；无进展分支只提交诊断计数或中止事务，既不申请物理保存，也不撤销本次空义务。
- 证据：链接真实远征服务、Bundle、崽包 Store、共享保存引擎和协调器的隔离探针确认 `Adds=0`、无物品交付、`HasDeferredFlush=False`。额外模拟官方 `LevelManager.NotifySaveBeforeLoadScene` 的 Collect + SaveFile，物理写成功一次，义务仍保留；切到没有 `PlayerStorage` 的出击场景后，崽包前置检查和 Store 均成功，`RequestAssetFlush` 返回 `asset_save_not_ready:storage_instance`。官方 SaveFile 没有通知该协调器清义务，`PetNestPersistence.HandleCollectSaveData` 也只采集/flush，因而常规切图保存不能消除此状态。证据：`Build/review-head-20261029/persistence-transition/result.log`；原始探针 `Build/review-persistence-20261029/probe.log`。
- 影响：一次未到账的奖励重试把后续崽包转移绑定到出击图不存在的基地仓库，造成保存失败并反复提示，符合此前修复想消除的阻塞类型。
- 建议：补发持有 `AssetSnapshotToken`，在确认尚未调用任何资产变更 API 的失败路径撤销本次新建义务，或在基地妥善结清该批次。不能单凭奖励游标没有前进就撤销，外部通知可能在资产已变更后抛异常；已有真实欠账和重入的新义务必须保留。
- 修复：持有本次 token，以 `ref assetDeliveryStarted` 在现金/物品外部 API 调用前置位，`finally` 仅在投递尚未开始时撤销。缺 prefab、实例化/身份/盖章失败及计数封顶不再遗留空义务；旧 owner、重入 owner 和可能已变更资产的异常路径保留。ContentTransactions 新回归在旧代码先红、修后 698 条断言通过；两项同步守卫的 6 个隔离反向探针均命中并按字节还原，证据 `Build/fixes-20261030/content-before.log`、`content-after.log`、`asset-guard-negative-results.json`。
- 验证需求：补齐“现金已领 + 物品 prefab 缺失 + 官方切图前保存成功 + 出击崽包转移”回归，同时保留部分发奖、外部通知抛错、已有义务与重入 owner 的保护。L3 在专用测试档模拟缺资源后进出基地，观察崽包转移是否仍报仓库未就绪，并确认重载后的资产一致性。

### CR-2026-10-29-009 · P2 / COMPAT · 宝箱扑跳落地冲击忽略墙体遮挡

- 状态：Fixed（2026-10-30），L1 + L2，L3 待验；当前既有缺陷，与已修的炮弹撞墙分支不同。
- 位置：`Integration/FieldAdventures/Mimic/GreedyMimicEncounter.cs:395-396`。
- 触发与影响：扑跳预警锁定落点后，玩家移动到附近薄墙另一侧，只要仍在落地半径内且高度差小于 2.5 米，就会被直接扣血。落点自身的净空检查不覆盖整个冲击半径，合法落点也能出现这一条件。
- 证据：真实 Encounter、Rules、官方 HealthSimpleBase / DamageReceiver 隔离执行，落点 z=12、玩家 z=14，墙体位于两者之间；未投喂时身位半径 1.1 米、冲击半径 2.4 米，墙距落点 1.5 米不妨碍落点净空。遮挡查询分别返回无遮挡/有遮挡，当前实现两种情况均造成 26 伤害。只在隔离副本补 `Unblocked(leapTo, p)` 后，无遮挡仍伤 26、有墙伤害为 0。真实 PhysX 未执行。证据：`Build/review-integration-20261029/pounce-evidence.json`、`pounce-current.log`、`pounce-occlusion-counterfactual.log`。
- 建议：扑跳伤害结算复用舌击和炸弹已有的 `Unblocked` 门控，保留落地视觉、伤害值、半径和后续连招。
- 修复：落地伤害在原半径/高差判定后增加 `Unblocked(leapTo, p)`。执行回归覆盖薄墙分割冲击范围、无遮挡伤害、半径和高度边界；旧生产先红，4 个隔离反向探针验证承重判据。
- 验证需求：覆盖墙前/墙后、冲击半径边缘、落点有效但冲击区域被墙分割的情形；L3 在薄墙拐角观察扑跳预警后躲到墙后是否仍掉血。

### CR-2026-10-29-010 · P2 / COMPAT · 烟花试用品异步达标后遗漏完成通知和借用收尾

- 状态：Fixed（2026-10-30），L1 + L2，L3 待验；当前既有缺陷。
- 位置：`Integration/FieldAdventures/Prototypes/DangerousPrototypeRuntimeModule.cs:93`、`:117-125`；仅有的成功收尾在 `:369-373`。
- 触发：巨型烟花筒（GrandFinale）的主弹或子弹在后续 `OnUpdate → TickFinales → TryHurt → ObserveHit` 中达到目标，规则立即变成 `Completed=true / Active=false`。OnUpdate 随后从 `!rules.Active` 分支返回；成功的 `EndLease(false)` 和完成通知仅存在于同步的 `OnShot` 尾部。再次开枪又被 `IsHeld()` 内的 Active 条件提前拦截，不能补做收尾。
- 证据：从生产文件逐字抽取 OnUpdate、OnShot、LaunchFinale、TickFinales、Explode、TryHurt、EndLease 等方法，链接真实 Rules，用隔离宿主/物理/视觉替身执行。真实烟花爆炸达成 10 次命中后，`completed=True, subscribed=True, borrower=True, rig=True, notifications=0`；再开一枪并推进 200 秒仍保持同样残留。证据：`Build/review-head-20261029/finale-completion/result.log`。现有 `FieldAdventurePrototypeRuntime` 只验证射向/后坐，并将 FireSpecial 替身化，已有通过结果不覆盖此路径。
- 影响：试验已完成却无达标提示，枪上试用品模型和借用事件保持到换枪/切图等后续操作；试验台仍可按 Completed 状态领奖，未证实奖励丢失。
- 建议：成功收尾由一个幂等入口负责，同步射击和异步爆炸达标均调用，执行退订、清借用引用、收模型与一次性完成通知，同时保留已发射烟花的尾效。
- 修复：同步 OnShot 与 TickFinales 后共用 `CompleteLeaseIfReady()`，在 Active 早返前通过 lease owner 幂等收尾并通知；保留在途烟花的 Shooter 与尾效。新增 FieldAdventurePrototypeCompletion 执行真实方法，覆盖主弹、子弹、收枪后达标、重复 Tick/开枪、规则领奖、暂停、取消与超时；旧生产先红，81 条断言与 4 个隔离反向探针通过，真实奖励交付仍待实机。
- 验证需求：覆盖主弹达标、子弹达标、达标后不换枪等待、再次开枪、领奖和切图；L3 观察完成提示及手持模型是否及时撤去。

### CR-2026-10-29-011 · P2 / COMPAT · F3 数字输入会误触发录制快捷键

- 状态：Fixed（2026-10-30），L1 + L2，L3 待验；只影响 Dev 构建。
- 位置：`DebugAndTools/PromoRecording/PromoRecordingDirector.cs:102`、`:127-133`。
- 触发与影响：录制模式保持运行时打开 F3 的物品/金额输入框，用小键盘输入数字。HandleKeys 无条件先于暂停门执行，原始 `Input.GetKeyDown` 也不检查官方输入阻塞或输入框焦点：输入 3 会切换拍摄项，0 发起三个靶子生成，1 销毁录制组件。F3 的 `InputManager.DisableInput` 只登记官方输入阻塞源，不会截获 Unity 原始按键查询。
- 证据：完整生产 PromoRecordingDirector.cs 配现有生命周期夹具的宿主替身，将 `IsGamePaused` 设为 true 后驱动实际 Update：数字 3 令 index 从 -1 变 0，数字 0 产生 3 个敌人请求，数字 1 销毁导演。证据：`Build/review-head-20261029/recording-input/result.log`。输入焦点/实际 UI 尚未 L3。
- 建议：处理录制快捷键前检查 F3/模态输入与文本框焦点，暂停菜单中不消费拍摄操作；保留正常录制时的小键盘兼容和录制自身暂停键。
- 修复：快捷键统一检查官方暂停、timeScale、模态租约、官方输入和 legacy/TMP 焦点，录制自身 paused 不屏蔽 Home/7 恢复。真实 Update 回归在旧代码先红、修后 319 条断言通过，7 个隔离反向探针确认各门及恢复键；证据 `Build/fix-recording-input-20261030/`。
- 验证需求：录制中分别在 F3 金额、数量和属性输入框键入含 0/1/3/7/9 的数值，确认不会刷怪、切项或结束；关闭输入界面后快捷键照常生效。

2026-10-30 修复验证：全量守卫 752 PASS / 0 FAIL；Windows 正式/Dev 隔离真编译通过，独立复核无新增 confirmed issue。全量回归首轮 148 PASS / 1 组视觉夹具超时；单独延长该次等待重跑，真实 Unity 基线和 6 项原生反向验证通过，最终 149 组均有本轮通过证据，是多轮合并。原日志、各次结果、合并清单和源码/DLL 哈希证据见 `Build/fixes-20261030/`，详细修复与 FIX30-01～04 实机步骤见 `FIX_TRACKER.md`。未取得 L3，未提交或部署。

2026-10-29 审查验证：

- `python tools/run_guards.py --verbose`：752 PASS / 0 NEW-FAIL / 0 KNOWN-RED，未使用 source-only；日志 `Build/review-head-20261029/guards.log`。
- `python tools/run_runtime_regressions.py --jobs 4`：148 组 PASS / 0 FAIL；汇总 `Build/review-head-20261029/regression-results.json`，日志 `regressions.log`。新增审查探针单独执行，现有套件全绿不覆盖上述四项遗漏。
- Windows 官方脚本正式和 Dev 构建均退出 0，使用本机游戏 Managed 的独立拷贝。只有 ModeHUIPages 两个既有 CS0649 警告。隔离目录没有 Mod 子目录，脚本的自动复制未完成，本轮不将其记为部署或发布资源验证。日志 `Build/review-head-20261029/compile-release.log`、`compile-dev.log`；两种 DLL 分别保留，`Build/BossRush.dll` 已还原为本轮正式产物。
- `check_dll_identifiers.py`：正式 DLL 不含所检查的 14 个 Dev 专用标识，Dev DLL 能检出全部 14 个，正反对照均通过。
- 未取得 L3：真实输入栈、场景物理遮挡、过图存档时序和运行帧耗仍需游戏内验证。审查探针模拟宿主，不视作实机证据。

<!-- 2026-11-13 archive byte SHA-256: 04652d5ce38170410f167fc908553bd4016a9e12aedf9d1bdac83decee7d2057 -->
