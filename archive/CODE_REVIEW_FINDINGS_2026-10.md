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

## 2026-10-11 最近五次提交审查修复（COMPAT / SAFE，L1/L2）

| ID | 等级 / 状态 / 分类 | 修复与验证边界 |
| --- | --- | --- |
| CR-2026-10-11-001 | P2 / Fixed / COMPAT | 冠军有效战斗遇到零游戏帧时长时不推进 Update 内招式；暂停与拍照保留冲锋，失效 owner 仍清理。真实 Update / Dashes 暂停与恢复回归通过。 |
| CR-2026-10-11-002 | P2 / Fixed / COMPAT | 穿墙冲锋以不超过 50 ms 的子步消费整帧时间，逐段验证地面；超时采用位移帧的已消费时间，途中取消停止剩余子步。120/60/30/20/10 FPS、交替长帧、断崖/高度、穿墙、翻滚与部分安全段伤害回归通过。 |
| CR-2026-10-11-003 | P2 / Fixed / COMPAT | 棍卫三档地面光带显式启用游戏时间；玩家默认仍用未缩放时间，光带几何、颜色与寿命参数不变。完整生产光带与棍卫前摇回归覆盖拍照、慢动作、结束与销毁。 |
| CR-2026-10-11-004 | P2 / Fixed / SAFE | 两个 2026-09-23 已闭环章节逐字追加到既有月归档，正文留索引；原文 SHA-256 与归档字节一致，1500 行上限未放宽。LedgerSizeGuard 通过。 |

新增执行入口 `SandstormCombatTiming`，旧代码先有 8 个场景失败，修复后通过；原 SandstormCombatRuntime / SandstormChampionPattern 继续覆盖完整调度表。只增加普通分支与小步地面检查，不改伤害、招式数值、音效、资源、存档或 Harmony 目标。最终守卫、正式编译及回归证据见 FIX_TRACKER 同日条目；没有实机 L3，不以离线通过承诺观感或性能。

## 2026-10-10 提交前复核（COMPAT / SAFE / OPERATIONAL，L1/L2）

- CR-2026-10-10-004，P1 / Fixed：钟守的商品原仅挂本人交互，`SkyIslandResidents.SpawnOneAsync` 会跳过婚后居民，缺席时岛内无法兑换。`SkyIslandWorldStoryServices` 现让 `Search_H` 复用同一 `ReloadMetronomeShopInteractable`，装配/清理与渡口商店共用方法，不增加 Tick。真实方法回归 955 项通过；断开钟庭入口的隔离探针准确转红，字节与 SHA-256 还原后通过。本人及婚后基地入口保持不变，实机可见性待验。
- CR-2026-10-10-005，P2 / Fixed：AchievementIcons 与 ResourceProduction 的旧 Unity 替身缺少共享 `ProductionIconCache.CloneTexture` 使用的 GraphicsFormat / CopyTexture API，导致两组回归编译失败。仅补共享测试替身，新增不支持、尺寸/格式/mip/采样参数与异常释放断言；两组回归均通过，不改变生产复制逻辑，不代表真实 GPU 像素或耗时通过。

- CR-2026-10-10-006，P2 / Fixed：已提交的点名册资源清单要求原图和正式包 alias，但本机两者均缺，导致 DailyReportArt 正式资源校验失败。复用 owner 指定作者工程原 PNG，以现有 IconsBuild 重建并只替换本地 production_icons；349 个 alias/Sprite 与压缩、预算、日报像素检查通过，旧包已备份。未部署实机，私有资源不进公开 commit。完整证据见[提交前审核报告](docs/reports/reviews/2026-10-10-提交前全面审核.md)。

## 2026-10-10 节拍器生产图标持有（COMPAT，L1/L2）

- CR-2026-10-10-003，P2 / Fixed：节拍器接入独立资源包时直接把包内 Sprite 交给持久物品 prefab，`ReloadMetronomeAssets.ResetStaticCaches()` 的 `Unload(true)` 会销毁其依赖贴图。已改为 `ReloadMetronomeIcon` 经 `ProductionIconCache.CloneTexture` 持有 GPU 副本，并以组件实例 ID 限定释放 owner；普通物品克隆销毁不释放副本。守卫删除实际 GPU 复制调用后按预期转红，按字节恢复、SHA-256 核对后通过。此项为交付前静态发现，热重载实机仍待验。

## 2026-10-10 点名册获取与商店收口（COMPAT，L1/L2）

- CR-2026-10-10-001，P1 / Fixed：先前把唯一获取途径接到普通玩家无法正常遇到的小满示例蓝图。商品迁至天空岛浮舟 sky_fuzhou，好感 3 级、售价 20000；Search_A 渡口工台追加同配置商店，兜底婚后离岛和居民生成失败。数据回归 666 条、婚姻 949 条、交互 949 条通过；删除装配/清理接线与错误 NPC ID 等 6 个隔离探针准确转红。
- CR-2026-10-10-002，P2 / Fixed：NPCShopSystem.Cleanup 销毁店铺对象却未关闭官方 StockShopView，owner 回收或 runtime 清理后仍可能残留窗口与输入屏蔽。现先停服务、退订，再仅关闭显示当前店且仍打开的 View；NPCShopPayment 18 条通过，旧 Cleanup 与删 Target 归属检查均在隔离副本转红。

743 项守卫、Windows 正式编译和 73 包部署哈希通过；13 个关键部署文件另行匹配。L3 待 RC-FZ-01～05，见[浮舟商店修正报告](docs/reports/testing/2026-10-10-点名册浮舟商店修正.md)。

## 2026-10-09 小满商店升级提示（COMPAT，L1/L2）

- CR-2026-10-09-001，P2 / Fixed：PermanentDuckNpcAffinityConfig.UnlocksByLevel 与 AffinityUIManager 都添加“解锁”/“Unlocked”前缀，导致好感升级横幅重复显示。配置改为仅返回 ShopName；中英文完整名称回归先确认旧实现失败，修正后 PermanentDuckNpcDialogue 666 checks 通过。正式编译部署完成；真实横幅与婚后购买待 RC-05 实机检查。见[点名册交付报告](docs/reports/testing/2026-10-09-点名册与小满商店交付.md)。

## 2026-10-08 噬魂挽歌内部名称显示（COMPAT，L1/L2）

- CR-2026-10-08-021，P1 / Fixed：500044 专属包走 EquipmentFactory，旧装配只登记 ItemFactory 与实例恢复；文本位于配置末段，未接统一启动/语言刷新。首次查看可能漏出 PhantomScythe_Melee_Item，切语言保留旧文本。补装备配置器 TypeID 过滤、统一文本注入及实际 raw key/历史别名，先命名再配置组件；保留编号和资源身份。实际包字段已读回；28 项生产入口/文本断言、4 个行为与 1 个登记守卫反向探针通过。见[文本修复报告](docs/reports/testing/2026-10-08-噬魂挽歌内部名称显示修复.md)。

提交前再次独立复核 CR-2026-10-08-018～021，未发现新增 confirmed issue。提交候选全量 743 项守卫、122 组回归与 Windows 正式编译通过；竞技场另做重入取消及 MoveNext / Current 异常释放探针，57 项通过。L3、完整 Unity 生命周期及自定义位置地图旧流程的边界见[提交前复核报告](docs/reports/reviews/2026-10-08-天空岛与镰刀提交前复核.md)。

## 2026-10-08 天空岛入场与兼容恢复（COMPAT / WIRE+，L1/L2）

- CR-2026-10-08-018，P1 / Fixed：旧竞技场等待跨图迟到派发，可能在岛上误启动裸装 Mode D。由 Integration owner 绑定原场景、请求代数和角色，在切图、后继请求与销毁时取消；超时不派发，嵌套协程透传并释放。LegacyArenaEntry 53 项及模式 / 征程接线回归通过，旧码与归属 / 取消变异准确转红。
- CR-2026-10-08-019，P1 / Fixed：不支持或缺少 GBuffer 的世界 shader 原来直接阻断整趟入场。现按支持与 GBuffer 检查选择本机官方材质，岛面 / 水面优先双面 Mask，保留世界材质名、贴图与参数；全部候选不可用及碰撞缺失仍拒绝。完整生产渲染类 180 项与旧码 / 7 类变异通过。本机官方 Cull、GBuffer 及当前包 alpha / 单位 ST 已核实，GPU 等价与新增图形后端支持未承诺。
- CR-2026-10-08-020，P1 / Fixed：慢读包挤占相机激活预算、导航 owner / data 延迟以及可选表现失败均可能误返航。激活独立计时，导航真实就绪后再扫描；HUD / 灰显 / 路灯 / 音景 / 居民 / 预热隔离，失败清理，居民装置任务兜底。预热 Build 上层与 owner 取消均释放，首个失败原因在基地黑幕后提示。租约 100、婚姻 949、战斗 64 条执行验证及 9 个隔离反向探针通过。

Fixed 表示代码与 L2 闭环；全量 743 项守卫、122 组回归、Windows 正式构建与 73 包部署哈希验证通过，最终引用清理另复跑相关验证。尚未取得反馈玩家当次日志或 L3，不能宣称已经确认玩家当次根因、实际画面或性能通过。证据与 SI-01～06 人工步骤见 [入场修复报告](docs/reports/sky-island/2026-10-08-天空岛入场与兼容修复.md)。

## 2026-10-08 未提交改动全面审核（COMPAT / OPERATIONAL，L1/L2）

| ID | 级别 / 状态 | 证据、根因与修复 |
| --- | --- | --- |
| CR-2026-10-08-014 | P1 / Fixed | F3 清空失败仍留下默认 pending，后续 Tick 可能真正清空。生产事务回归确认；改候选 store 保存，失败恢复原快照与重试义务，确认槽号再次核对，提交成功后的界面异常不误报失败，并清旧对白队列。 |
| CR-2026-10-08-015 | P2 / Fixed | 原磁盘诊断走官方内存缓存。改 ES3 File 显式读回；遗漏物理更新的故障注入与改回 Cache 的反向探针准确失败。Dev 真编译补齐 EasySave3 引用和构造器重载。 |
| CR-2026-10-08-016 | P1 / Fixed | 三 Boss 补注册时配置尚未载入，单纯作废筛选或立即读默认配置均不闭环。按真实宿主 Awake → LoadConfig → OnStart 顺序回归，由 BossFilter.OnStart 统一恢复实际禁用状态/因子；20 项反向探针通过。 |
| CR-2026-10-08-017 | P2 / Fixed | 共享包卸载摧毁地雷盒持久 prefab 借用的图标。生产 Config/Icon 回归重现，由 prefab 持有独立压缩 Texture/Sprite 副本；4 项资源所有权反向探针通过。 |

全量 743 项守卫、120 组回归、最终征程专项回归、Windows Dev/正式编译及部署哈希验证通过；保留 L3 与实际性能采样缺口。逐项需求、制品哈希与人工验收见 [审核报告](docs/reports/reviews/2026-10-08-未提交改动全面审核.md)。

## 2026-10-08 实档章节缺失、沙圈出生点与末阶段调度（COMPAT，L1/L2）

- CR-2026-10-08-008，P1 / Fixed：授权只读的实际征程 JSON 缺少整个 chapters 字段，前五章交付 token 仍在。以共享显式 JSON 编解码替换 Unity DTO 序列化，按持久化交付凭据兼容恢复，不重新发奖。
- CR-2026-10-08-009，P2 / Fixed：双生沙圈把 target 作为出生位置，直接落在玩家两侧。现统一使用 Boss 发射点贴地，并保持接触或到时凝柱。

末阶段停在首次换侧为日志确认的症状，宿主触发点尚未复现；局部改为 Update 驱动嵌套招式及定时等待，隔离回归验证宿主停止协程不会中断主攻击。泡泡官方 Hurt / HealthSimpleBase / OnShotDown 接线执行回归通过，外观更新为空心薄弧及细砂裂光。证据边界及人工验收见 [本轮报告](docs/reports/testing/2026-10-08-实档章节恢复与沙暴战斗修复.md)。没有离线修改玩家存档，未启动游戏。

## 2026-10-08 首次读档时机、沙暴持续与星阙命中（COMPAT，L1/L2）

- CR-2026-10-08-005，P1 / Fixed：征程主菜单阶段缓存未就绪的槽位并提前锁定初始化；对齐天空岛序章的关卡就绪门，已加载同槽保留退出保存，未知事实不反锁线索。真实生产读写/延迟加载/退出/换槽及 9 对独立写读进程回归通过。
- CR-2026-10-08-006，P1 / Fixed：召卫清除沙柱且存活门禁止喷弹；改并行召卫与沙柱独立寿命，转阶段也保留场上柱。
- CR-2026-10-08-007，P2 / Fixed：星阙重击没有核验 Hurt 结果及实际掉血，空砸仍强反馈；现真实伤害才触发停顿、震屏、击退，兼容沙泡简单生命并移除重复提示。

Fixed 为代码和离线闭环，真实重启与战斗尚待 L3。新制品、机制来源和实机步骤见 [升级报告](docs/reports/testing/2026-10-08-征程重启进度与猪鲨机制升级.md)。

## 2026-10-08 沙暴冠军接管与移动（COMPAT，L1/L2）

- CR-2026-10-08-012，P1 / Fixed：NodeCanvas BehaviourTree 继承 ScriptableObject，旧暂停辅助类转 Component 得空，原图可继续跑。新冠军/棍卫局部显式 Stop 四图与 owner，停 AI 子树、剥原武器和技能；未整体替换共享辅助类。
- CR-2026-10-08-013，P1 / Fixed：新冠军连续移动使用 SetPosition，官方每次暂停贴地并清速度，且每米近点 0.8 门槛可拒绝正常地板；改用官方 SetForceMoveVelocity / ECM2 与低频 A* 绕障，根碰撞体不放大，受阻后结束整组连冲转绕障。

用户反馈站桩，日志确认生成成功及 BehaviourTrees:0；没有记录具体地形拒绝分支。Fixed 表示代码与 L2 验证闭环，未宣称新 DLL 实机移动通过。小弟无掉落创建窗口、额外奖励过滤和清理接线已检查；证据见 [修复报告](docs/reports/testing/2026-10-08-沙暴冠军站桩与召唤修复.md)。

## 2026-10-08 进入竞技场的额外预热（COMPAT，L1/L2）

- CR-2026-10-08-010，P2 / Fixed：普通 DEMO 场景无条件启动整套 Mode E 预热；已删除场景公共路径的启动，E/F 实际入口仍保留按需准备。
- CR-2026-10-08-011，P2 / Fixed：焚皇戟管理器切图时同步加载龙王包，背包闲置也承担成本；现由主玩家手持事件触发异步加载并分帧扩池，离手/死亡/切图/停用取消，预览不抢同步请求。

本轮日志可证旧戟初始化时载包，Mode E 的 1607.5 ms 仅来自此前对话，本轮未重获竞技场样本。报名石无主因证据，性能提升比例和视觉手感等待 L3。守卫、回归、正式构建及部署核对记录见 [交付报告](docs/reports/testing/2026-10-08-沙暴冠军与星阙交付.md)。

## 2026-10-08 空仓地雷盒全面复审（COMPAT / SAFE，L1/L2）

| ID | 级别 / 状态 / 分类 | 根因与最小修复 |
| --- | --- | --- |
| CR-2026-10-08-002 | P2 / Fixed / COMPAT | ManualEquipmentRecovery 夹具未链接新增引用的 BossRushItemIds，导致 CS0103；补链接真实 Config/ConfigItemIds.cs，复跑 57 checks 通过。 |
| CR-2026-10-08-003 | P2 / Fixed / COMPAT | 地雷只等待目标场景 LevelBeginInitializing 取消，官方旧场景黑幕渐变期间仍可引爆。订阅 SceneLoader.onStartedLoadingScene 即时取消，使用判据检查 IsSceneLoading，阻断回调中开始切图后的剩余目标。旧生产代码失败后补修，地雷回归扩为 17 场景 / 223 断言。 |
| CR-2026-10-08-004 | P2 / Fixed / SAFE | 中英 Mod 简介仍写 37 / 20 件装备，和本轮 38 / 21 不一致。同步 WikiContent 源文并补全掉落专题与切图取消说明。 |

全量守卫 738 / 738、执行回归 118 / 118、Windows 正式编译、Wiki 构建与 80 项导航检查通过；未发现剩余 confirmed issue，L3 尚未运行。证据与剩余实机步骤见 [全面复审记录](docs/reports/reviews/2026-10-08-空仓地雷盒全面复审.md)。

## 2026-10-08 空仓地雷盒实现期复核（COMPAT，L1）

CR-2026-10-08-001：P2 / Fixed。新图腾动态 prefab 使用 DontDestroyOnLoad 并留在 ItemAssetsCollection；初版却由 NewWeaponRuntime cleanup 销毁程序化 Sprite/Texture，同进程重新启用时复用旧 prefab 会继续引用失效图标。改由动态 prefab 上的组件拥有资源，用创建者 instanceId 保护共享图标，宿主 cleanup 不再释放它。当前代码审查与正式编译通过；实际热重载未做 L3，证据和验收步骤见 [交付记录](docs/reports/testing/2026-10-08-空仓地雷盒交付与验收.md)。

<!-- 2026-11-19 archive byte SHA-256: f5df1df2845bea8b8a3983e42c3d73eb0cf99451fb47ea8eb608684a6257eaee -->

## 2026-10-18 v2.3.5 之后五个提交审查（001～023 全部 Fixed，COMPAT / OPERATIONAL，L1/L2，L3 待 owner）

范围 `3abfddcc..e25005ee`：`5a835089`、`9f9f9449`、`1170e834`、`c09c53cb`、`e25005ee`。审查时 HEAD 隔离签出 Windows 正式编译 0 错误；全量守卫 747/747；相关执行回归 7 项全过。没有 P0 / P1。下列位置按审查时 HEAD 行号；owner 要求全部修复，钻戒限量与官方存档键两条授权按好玩优先自定。

| ID | 级 / 分类 | 问题 | 位置 |
| --- | --- | --- | --- |
| CR-2026-10-18-001 | P2 / COMPAT | 丧尸模式减伤按负数累加，判上限用正数 0.40，满级后仍出卡、选了不加 | `ZombieMode/ZombieModeRuntimeModule_RewardCatalogAndSelection.cs:329`、`ZombieModeRewardItemGrants.cs:534` |
| CR-2026-10-18-002 | P2 / COMPAT | 商人「下次购买保底 6 品」在该口径没有 6 品弹药时被回退成最低品质弹药并清掉保底 | `ZombieModeRewardItemGrants.cs:653-681`、`ZombieModeRewardNpcServices.cs:183-195` |
| CR-2026-10-18-003 | P2 / COMPAT（PLAUSIBLE） | 弹药补给 / 弹雨：手上口径发不出弹药时不再退回开局口径，直接发随机弹（是否存在无弹药口径的枪未核实） | `ZombieModeRewardItemGrants.cs:89-99,560-569`、`ZombieModeRewardTriggerEffects.cs:307-318` |
| CR-2026-10-18-004 | P2 / COMPAT | 钻石戒指终身限 1 枚，离婚不重置，求婚 / 丢失后只能赌许愿池，离婚后基本无法再婚 | `Integration/Affinity/NPCs/GoblinAffinityConfig.cs:805`、`NPCShopStockLedger.cs:96` |
| CR-2026-10-18-005 | P2 / COMPAT | 好感页对所有 NPC 读 `DiscountsByLevel` 并写「商店折扣」，羽织的治疗折扣被写成商店折扣 | `Integration/Affinity/UI/AffinityTabData.cs:101,195-198` |
| CR-2026-10-18-006 | P2 / COMPAT | 「全部孵化」每枚蛋各走一次 `RequestAssetFlush(bypassGates)`，一次点击同步整档写盘 N 次 | `PetNest/PetNestHatchService.cs:123-138,156-180` |
| CR-2026-10-18-007 | P2 / COMPAT | 刚归来未揭晓的崽再派出后，巢页按 petId 取到旧的已结算记录，显示「远征已归来 / 查看结果」 | `PetNest/PetNestUINestPage.cs:265-284,372-380` |
| CR-2026-10-18-008 | P3 / COMPAT | 弹药区间回退一律取最低品质；请求区间高于可用品质时应取最接近的那档 | `ZombieModeRewardItemGrants.cs:669-681` |
| CR-2026-10-18-009 | P3 / SAFE | 「按手上的枪配口径」实际是主武器槽优先，没读当前手持 | `ZombieModeRewardNpcServices.cs:210-228` |
| CR-2026-10-18-010 | P3 / SAFE | 弹药夹具替身降级语义与官方 `Search` 不一致，缺保底 (6,6) 与区间高于可用品质用例 | `tests/fixtures/ZombieModeAmmoSelection/Stubs.cs:84-93` |
| CR-2026-10-18-011 | P3 / SAFE | 衰减提示直接赋值，未聊天期间第二次衰减覆盖第一次，提示天数 / 扣分偏少 | `Integration/Affinity/AffinityManagerPersistenceAndDecay.cs:118` |
| CR-2026-10-18-012 | P3 / OPERATIONAL | 动态 `StockShop` 的 `Awake` 已订阅 `SavesSystem.OnCollectSaveData`，商店开着时会写官方存档键 `StockShop_BossRushNpc_<npcId>`（Mod 不读） | `Integration/Affinity/Systems/NPCShopSystem.cs:85-87,776-777` |
| CR-2026-10-18-013 | P3 / COMPAT | 永久 NPC 折扣上限 0.5 超过低买高卖临界 1/3（现有数据最高 0.20） | `Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcData.cs:109,172` |
| CR-2026-10-18-014 | P3 / SAFE | 对话系统吞掉非取消异常后，剧情仍被记成看过并发奖 | `PermanentDuckNpcStoryPlayer.cs:148-150`、`Integration/Dialogue/DialogueManager.cs:424-427` |
| CR-2026-10-18-015 | P3 / SAFE | 远征队满时巢页仍挂「派去远征」，点进去才被拒 | `PetNest/PetNestUINestPage.cs:326-334` |
| CR-2026-10-18-016 | P3 / SAFE | 「治一道疤」遗魂不够时不挂、按钮不写价钱，不符合付费服务口径 | `PetNest/PetNestUINestPage.cs:308-324` |
| CR-2026-10-18-017 | P3 / COMPAT | 摸摸脉冲结束时把体型还原成开始时的值，期间升级的新体型被盖回；曲线用 `Mathf.Sin` | `PetNest/PetNestUIMotionParts.cs:138-172`、`PetNestCompanionCare.cs:265` |
| CR-2026-10-18-018 | P3 / COMPAT | 远征到期提醒去重集合跨存档槽不清，换槽后同 id 的远征不提醒 | `PetNest/PetNestRuntimeModule.cs:324,340,354-364` |
| CR-2026-10-18-019 | P3 / COMPAT | 「全部孵化」孵出过一枚后吞掉后续失败原因；写盘失败但已提交的那枚不进揭晓 | `PetNest/PetNestUIPages.cs:546`、`PetNestHatchService.cs:138,175-177` |
| CR-2026-10-18-020 | P3 / SAFE | 博物馆驯养成就行复用 `Shiny`，给白字标题挂每帧流光 | `PetNest/PetNestUIMuseumPage.cs:222` |
| CR-2026-10-18-021 | P3 / SAFE | 守卫缺口：三份新 PetNestUI 文件不在分层守卫清单；丧尸反制守卫只断言子串 `IsPlayerOwnHit(`，加 `|| IsPlayerSideAttack` 仍能过 | `tests/PetNestUILayerGuard.py:130-134`、`tests/ZombieModeCompanionCounterplayGuard.py:39-44` |
| CR-2026-10-18-022 | P3 / SAFE | 页签指示条用 `AccentFill`（应为 `Accent`）；`RollNewPet` 的 summary 错挂到 `TryHatchAll` 头上 | `PetNest/PetNestUIMotion.cs:84`、`PetNestHatchService.cs:148-156` |
| CR-2026-10-18-023 | P2 / SAFE | 修复阶段全量回归发现：`1170e834` 后 HEAD 上两份执行回归就是红的（当轮只跑了按名过滤的回归）。`ManualSeptemberReview` 抽取的揭晓 / 归巢方法引用新成员却没有替身，编译失败；`IntegrationThirdReviewFixes` 仍断言「崽的远程命中吃刚硬减伤」，与有意改成「反制只认玩家本人」的设计相反 | `tests/fixtures/ManualSeptemberReview/Stubs.cs`、`HomecomingStubs.cs`；`tests/fixtures/IntegrationThirdReviewFixes/ZombieBossCases.cs:235` |

修复闭环（详见 `FIX_TRACKER.md` 同日节）：001 `IsZombieModeAttributeBonusAtCap` 比绝对值；002 / 008 `allowQualityFallback` + 取离区间最近的品质；003 `TryGiveZombieModeSupplyAmmo` 退回开局口径；009 先读 `GetGun()`；010 替身按官方 `DownGradeSearch`、补用例；004（拍板）戒指 `RestockOnlyWhenSingle` + 3 天，已婚不补、单身补；005 羽织标「治疗折扣」；011 衰减提示累加；012（拍板）开店摘掉官方 `Save` / `Load` 回调；013 折扣上限 0.30；014 `DialogueManager.SequenceFailureSerial`；006 / 019 整批单次落盘、部分孵化写原因；007 按 `lockedByExpeditionId` 找本趟；015 队满不挂；016 治疤有疤就挂并写价钱；017 `PetNestPulse.Rebase` + 共享缓动；018 换槽清空、键带出发时间；020 `ShinyFrameOnly`；021 两个守卫补强；022 `Accent` 与 summary 归位；023 补替身、断言随设计更新。为满足大文件预算，`NPCShopSystem` 改 partial，官方商店钩子三方法原样移到 `NPCShopSystemOfficialShopHooks.cs`（已登记编译清单）。

- Refuted：「好感写屏障期间剧情奖励能反复领、钻戒限量守不住」。屏障只在读档失败时保持，反序列化先进候选表、失败不写入，内存好感表为空且加分同样被拦，等级停在 0，到不了 3 级以上剧情与 7 级戒指。
- UNVERIFIED / Needs runtime smoke：没有 `GameClock` 的场景里 `GameClock.Day` 为 0，可能被当成「日子倒退」补满每日商品；好感页签克隆自笔记按钮，笔记未解锁时是否随模板隐藏、是否带「笔记」悬浮字；好感页根节点盖住 `ViewArea` 是否挡页签栏点击；批量孵化揭晓汇总 9–10 行时是否压住关闭按钮；博物馆首次打开全谱系头像同步加载耗时；白手起家小怪池按实例 ID 正负排除运行时 preset 的假设，以及 Mode E/F 阵营小怪表是否因排除 `isBoss` 变空；平安档现金期望实际上调约 16%（文字口径写的是不变）。

## 2026-10-25 全模块重点链路审查（COMPAT；L1/L2 修复，L3 待验）

本轮从干净工作区开始，四路并行检查宿主与共享持久层、各玩法模式、Integration 内容，以及世界/UI/音频链路，并交叉复核加载时宿主早返后的实际可达性。范围包括进入/退出、死亡、玩家销毁与替换、异步晚到、切图与切槽、重复回调、无效数值、交易和资源清理；这是按风险选择的生产链路审核，不代表全部源码逐行或全部状态组合已穷举。

以下问题均已修复，兼容分类为 COMPAT，证据为生产接线 L1 与隔离执行 L2。有效配置、已发布 key、TypeID 和存档 schema 保持兼容；没有游戏内 L3 证据。

- **CR-2026-10-25-001，P1：共享存档漏收切槽通知后可跨槽写入。** `Common/Lifecycle/BossRushSlotJsonStore.cs` 的 Store / FlushPending 原来只依赖读取入口检查槽位。官方 `SavesSystem.SetFile` 先切槽再调用普通多播事件，前置订阅者异常会截断后续通知；旧 pending 随协调器 flush 覆盖新槽。现两条写入口复核槽位，漂移时拒绝请求、清旧 pending 并通知 owner；首次直接 Store 也先加载 schema 并绑定槽位。`SaveFailureRecovery` 以真实 store 和内存宿主先复现红项，再验证通知中断、首次直接写入、高版本屏障和新槽恢复。
- **CR-2026-10-25-002，P1：危险试用品委托迟到完成可写入新槽。** `Integration/FieldAdventures/Prototypes/DangerousPrototypeCommission.cs` 的异步对话原未绑定原玩家、关卡与存档槽。现提交时复核请求所有权，并在加载开始、切图、切槽、玩家失效和销毁时取消；旧回调不能重复收尾新对话。`PrototypeCommissionLifecycle` 完整执行生产模块，67 条断言通过；忽略取消的迟到回调、加载时不调用模块 Tick、NPC 复位与退订均覆盖，三项隔离反向探针按预期转红。
- **CR-2026-10-25-003，P1：Mode D 的旧波次任务不绑定原玩家。** `ModeD/ModeDRuntimeModule.cs` 的 CaptureValidity 原只检查模式、波次及场景。死亡广播被截断、主角销毁或同图替换时仍可接受旧生成结果与自动开波。现捕获原玩家并检查身份和 Health；`AuditModeLifecycle` 在旧生产代码的死亡用例转红，修复后覆盖死亡、销毁、替换和重生后的旧请求失效。
- **CR-2026-10-25-004，P2：加载开始至 sceneLoaded 之间旧生成任务仍有效。** `ModeD/ModeDRuntimeModule.cs` 与 `WavesArena/WavesArenaRuntimeModule.cs` 的 CaptureValidity 补充 SceneLoader 加载门。根 Update 早返不会终止已运行的协程和异步生成，单靠场景代数更新过晚。`AuditModeLifecycle` 覆盖仅改变 loading 标志、场景和玩家仍相同的两模式窗口，旧 Mode D 实跑失败。
- **CR-2026-10-25-005，P2：Mode D 退出后可误结算第 0 波。** `ModeD/ModeDRuntimeModule_WaveResolution.cs` 的 TryResolveModeDWaveComplete 原可把退出后的空列表与零 pending 判为完成，迟到死亡回调可能改提示和路牌。现要求 modeDActive 且波次大于 0；真实入口的关停后再次结算用例先红后绿，现有连续波次回归仍通过。
- **CR-2026-10-25-006，P2：霜之哀伤慢创建被正常收招取消。** `Integration/Frostmourne/FrostmourneAction.cs` 原在 1.5 秒动作结束时无条件取消生成代数，冷加载超过动作时长便销毁合法召唤物。现区分正常完成和主动中断，管理器在离手、停用、死亡、加载与销毁时仍拒绝旧请求。`ManualEquipmentRecovery` 合计 72 条断言通过，恢复旧正常收势取消行为的隔离探针按预期失败。
- **CR-2026-10-25-007，P2：成就面板可跨加载或玩家失效残留。** `Achievement/AchievementRuntimeModule.cs` 与 `Achievement/AchievementView.cs` 统一检查关卡、主角与加载状态；View 自身 Update 负责关闭已开面板，独立于根宿主 gameplay gate。`AchievementRuntime` 执行真实宿主早退/模块分发及 View.Update，先证明加载时模块未被驱动，再证明面板自行关闭。实际 Unity 输入租约归还仍需 L3。
- **CR-2026-10-25-008，P2：无效 Boss 倍率污染生命与 AI 数值。** `Utilities/BossStatScaling.cs` 原直接使用零、负数或非有限倍率，可生成零生命并除零计算 AI 时序。现优先回退有效全局倍率，否则使用中性值 1；无效生命比例也使用 1。`BossHealthScaling` 在旧版零生命用例转红，修复后覆盖五类无效值，保留正常倍率叠加、护卫减半和生命比例语义。
- **CR-2026-10-25-009，P2：NaN 配置可拖住波次倒计时。** `Config/Config.cs` 的两个休息时间 getter 原仅用大小比较钳制，NaN 可穿透。现 NaN 沿用本地默认值，有限值与正负无穷沿用既有范围钳制。`BossHealthScaling` 链接真实 getter，旧版 NaN 断言转红，修复后默认值和上下限均通过。
- **CR-2026-10-25-010，P2：JSON 浮点转 Int64 在上边界溢出。** `Common/Data/BossRushJsonValue.cs` 原将 double 与 long.MaxValue 比较，但后者转为 double 已舍入到 2^63，导致该越界值被转成负数。现排除上界并拒绝非有限整数转换，保留真正的 long.MaxValue 整数 token、long.MinValue 浮点边界和既有四舍五入。`AuditCoreParsing` 在旧版解析 `9223372036854775808` 时先红，修复后边界回归通过。

首轮全量守卫为 751 PASS / 1 FAIL，唯一红项是 `architecture/modules.json` 漏登记既有 PromoRecording 两份源码，已补归属（SAFE）。首轮全量执行回归为 142 PASS / 2 环境失败；两组失败分别缺少显式 Managed / Harmony 环境变量，需补齐环境后重跑，不能计为通过。最终构建、门禁与重跑结果在同日 `FIX_TRACKER.md` 记录。

其他已审查链路未形成新增 confirmed finding：Mode E 阵营退出与共享租约、Mode F 补怪和撤离、Mode G/H 生成与场景准备、丧尸波次与安全区及结算、征程与随机事件取消、里程碑奖励、装备启停、NPC 商店/好感/婚姻交易、天空岛返航/剧情/巡守、遗种巢晚到生成、共享确认框、地图选择及 BGM owner。未测实际帧耗时，不据静态门控宣称无性能问题；历史待实机条目不因本轮离线通过而关闭。

人工验收记录建议文件名 `BossRush_Audit_20261025_smoke.txt`，以下是手工步骤 ID，并非新增 F3 自动用例：

- `AUDIT-01`：Mode D 待刷怪或自动下一波期间死亡；观察死亡画面和波次提示，死亡后继续生成或提示新波即不合格。
- `AUDIT-02`：标准竞技场批量生成、Mode D 自动下一波期间返基地；观察加载画面及入基地后的场景，不得有迟到敌人、下一波提示或“第 0 波完成”。
- `AUDIT-03`：冷启动后持霜之哀伤右键召唤；观察玩家周围，慢加载仍应完成召唤。另在生成中切武器和切图，原请求不得留下迟到亡灵。
- `AUDIT-04`：基地与加载、死亡期间分别按 L；观察屏幕中央成就面板、鼠标和移动控制，加载/死亡不得被面板遮挡，关闭后控制必须恢复。
- `AUDIT-05`：接叮当危险试用品委托后中断对话并返基地；观察叮当交互选项与朝向，取消后应能重新交互，正常完成只记录当前槽一次。
- `AUDIT-06`：使用独立测试槽正常切换两槽，核对图鉴、征程与日报各自进度；新槽出现另一槽进度或未知格式被覆盖即不合格。前置订阅者异常只在离线夹具注入，不要求修改真实玩家存档。

本轮未启动游戏、未访问玩家存档；以上实机结果尚未采集。

## 2026-10-24 龙息丧尸伤害修复（COMPAT / WIRE+；Fixed at L1/L2，L3 待验）

CR-2026-10-24-001，P2：龙息资源及运行时配置缺少 `DamageFactorToZombie`，官方枪械读取缺项为 0，弹道送入 `Health.Hurt` 后对 isZombie 目标把直接伤害清零。现装 resources.assets 五种 Cname_Zombie 的该标记均为 1，当前 DLL 的创建状态机也明确复制 preset.isZombie 至 Health，故不是只由测试替身构造的靶子。玩家未提供“无法使用”的具体表现，不能据此认定所有无法开火/装弹反馈都同因。

`Integration/DragonDescendant/DragonBreathWeaponConfig.cs` 为缺项补中性值 1，并刷新官方 StatCollection 的懒哈希缓存；`Integration/BossRushIntegrationRuntimeModule_Initialization.cs` 登记龙息专属恢复判据，使旧枪经初始化和手持入口补配，保留已有系数、重铸、磨损与运行时修饰。42 项回归直接执行生产恢复入口及完整官方枪械→弹道→DamageReceiver→Health 方法，旧行为直伤为 0、修复后恢复伤害，5 项负向探针均在预期断言转红。正式 Windows 隔离编译通过；无 L3。证据及宿主替身边界见 `Build/champion-dragonbreath-20261024/` 与夹具 README；待实机验收 BR 装弹、开火、旧枪重入场及普通/大体型丧尸伤害。

## 2026-10-23 野外趣事六项修复与交叉复审（COMPAT；002～007 Fixed at L1/L2，L3 待验）

本轮覆盖三个玩法的 17 份生产源码及直接调用链、宿主/资源登记与相关夹具；实际修改 6 份既有生产文件，无新增 TypeID、存档字段或生产源码文件。三个子代理均按用户要求使用 GPT-6.1 Sol / xhigh，分别修复后交叉审核其他模块，主审复核共享门、材质租约、官方 API、构建与测试边界。最终未发现新的 confirmed P0/P1/P2；不将未实机的观感或性能推测记为已确认问题。

- **CR-2026-10-23-002，P2 / COMPAT / Fixed at L1/L2。** 原 `OnHurt` 仅累加 raw damageValue，30 × 2 暴击实扣 60 却无法达到 45 点回血打断。`Integration/FieldAdventures/Mimic/GreedyMimicEncounter.cs:159` 现使用官方已判定的 crit 倍率累计正伤害，保留主玩家过滤，零/负倍率和回血不计入。FieldAdventureMimicRuntime 完整执行生产 Encounter、Rules、官方 HealthSimpleBase 与 DamageReceiver，覆盖普通/暴击边界及累计、拒收、非主玩家和负伤害；恢复旧累计方式会在真实扣血归因断言转红。L3 验证：实际普通/暴击达到 45 时药瓶蓄力应中断。

- **CR-2026-10-23-003，P2 / COMPAT / Fixed at L1/L2。** 原包裹按角色根 forward 摆放，官方按模型朝向检测，转身约 81° 后失去交互。`Integration/FieldAdventures/Delivery/OddParcelRuntimeModule.cs:350` 现沿 CurrentAimDirection 放在前方一米，抱起当刻同步；`:360` 的携带交互球退回玩家上方，放下/喷发成功落地时恢复中心，失败保持携带态。复审发现仅改朝向还不足以覆盖移动先于包裹更新，已补该帧序修复与回归；完整官方 CA_Interact 覆盖移动 0.5 米、任意转向与先搜索后更新。L3 验证：奔跑转身期间 F 交互持续可用，落地后须靠近包裹。

- **CR-2026-10-23-004，P2 / COMPAT / Fixed at L1/L2。** 原 32 米舌长与 45 米攻击门不一致，40 米仍受击但舌尖差约 8 米。`Integration/FieldAdventures/Mimic/GreedyMimicVisual.cs:216` 现伸至实际锁定落点，独立十二段各有迷雾采样与清理；弹幕保留八段/32 米。真实 Unity Transform 检查 31.99/32.01/40/45 米的连续端点、每段长度及采样登记，恢复旧截断会在真实端点断言转红。官方迷雾后端仍为替身，L3 验证最大射程实体、预警、命中及远端迷雾是否一致。

- **CR-2026-10-23-005，P2 / COMPAT / Fixed at L1/L2。** 原只更新本地化表，持续同一目标时压力/完整度文字不刷新。`Integration/FieldAdventures/Delivery/OddParcelInteractable.cs:62` 现仅在值/语言变化时刷新官方 HUD 已绑定行，不清目标、不重新选中；喷发当刻刷新。夹具执行完整官方 InteractHUD / InteractSelectionHUD，覆盖同目标变化、组选中与事件保留、语言切换；恢复只改本地化时按预期失败。L3 验证官方交互文字随搬运和喷发变化，组内选择不跳回。

- **CR-2026-10-23-006，P2 / COMPAT / Fixed at L1/L2。** 原无条件用瞄点减枪口，近瞄点会使附加攻击和巨炮后坐反向。`Integration/FieldAdventures/Prototypes/DangerousPrototypeRuntimeModule.cs:316` 与官方基础方向一致：水平距离超过角色到枪口距离加 0.1 米且 Moveability > 0.5 时才取瞄点，否则保留枪口前向；OnShot 和后坐共用结果。`:296` 遇墙沿胸口检查段退回，避免切向/反向射击时沿射向退到墙后。310 场景/2316 断言比对抽取的真实生产与官方方向分支，并执行 OnShot、TraceBlast、TickRecoil；六项隔离旧行为均转红。墙面为解析平面替身，L3 仍需脚边/高差/薄墙/斜墙与实际接触验证；私有 OverrideShootPoint 和第三方改弹道不在官方基础方向合同内。

- **CR-2026-10-23-007，P2 / COMPAT / Fixed at L1/L2。** 原 Unity 音源固定 0.7，不受官方 FMOD 设置影响。`Integration/FieldAdventures/Delivery/OddParcelVisual.cs:256` 现桥接 Master 与 Master/SFX 的 Volume、Mute；播放前及 runtime 暂停早返之前同步，AI 声源独立。运行时回归覆盖设置与暂停，真实 Unity AudioSource 检查实际音量属性；固定音量、漏 Mute、暂停后更新均被负探针抓到。L3 验证播放期间改主/音效音量或静音，叫声立即服从设置、敌人听觉不随玩家静音关闭。

验证：中央 `python tools/run_runtime_regressions.py --filter FieldAdventure` **6 PASS / 0 FAIL**；规则/库存 1376、场景门 43、宝箱 199、快递 2993、试用品 2316 条断言通过，真实 Unity 六组 Deferred 视觉无引擎错误和粉色像素。新运行时负探针 15 项、舌头结构守卫负探针 4 项、原生舌头负探针 1 项全部在预期断言转红，隔离副本按字节还原，生产输入 SHA 未变。新夹具已登记中央入口，各 README 写明真实生产与替身边界，不能把整体生命周期都声称为 L2。

全量 751 项守卫初跑 749 PASS / 2 FAIL：台账超限已把三节九月已闭环记录原字节追加至月档、正文留索引，LedgerSizeGuard 复测通过；唯一保留的是既有 ZombieModeRuntimeModule.cs 1239 > 1200，与本次无关。最终 FieldAdventureIntegrationGuard 与 533 项 changed-only 守卫全部通过。Windows 正式隔离构建通过、无 Dev 定义，只有两条既有 ModeH CS0649 警告；DLL SHA-256 `A38565E7B6B3E1848EAE4812CAFAB478D1C89714E568D010C32B3A3D7E02ABF8`。GAME_PATH 指向 Managed 实体拷贝，未部署、启动游戏、访问存档或提交。

证据汇总与人工验收见 `Build/field-adventures-fixes-20261023/`：`final-evidence.json`、`runtime-final.log`、`windows-build-final.log`、`guards-full.log`、`guard-probes/results.json` 与 `验收与复审.md`。原生最终报告 `Build/FieldAdventureVisuals/run-arsh4p5k/baseline.json`，旧舌长反向报告 `run-9l2xui66/negative-short-tongue.json`；快递七项负探针 `Build/runtime-regressions/FieldAdventureDeliveryRuntime/source-9sjlupx2/result.json`，试用品六项 `Build/runtime-regressions/FieldAdventurePrototypeRuntime-negative/copy-t_2yc3ek/report.json`，宝箱负探针随中央夹具默认执行。

剩余证据边界（非 confirmed finding）：反复切回借用枪会重建视觉对象，尚无帧耗采样，不能认定卡顿；宝箱直接 Health.Hurt 的载具保护路径未证实在普通出击可达；真实 PhysX、地形、迷雾遮挡、音量听感与操作手感仍待 L3。公开发布前按人工步骤检查，不能以编译/离线预览宣称游戏内已验证。

## 2026-10-23 蚀日花冠粒子持续报错（COMPAT）

CR-2026-10-23-001，P1 / COMPAT / Fixed at L1/L2，Needs runtime smoke：玩家日志中花冠生成后出现 239 次 `Particle Velocity curves must all be in the same mode`。`Integration/EclipseBloom/EclipseBloomBody.cs` 的 `BuildParticles` 只把线性 y 轴设为 TwoConstants，x/z 留为 Constant；现显式设置 x/z 零区间，保留 y 随机漂移与原轨道速度。真实 Unity 2022.3.62f3 播放模式的 94 次原生模拟零错误，三阶段/四瓣破碎/死亡清理均通过；隔离副本去掉 x/z 赋值后相同测试真实重现 112 次原报错，源码与资源 SHA 不变。此前静态预览和宿主替身未覆盖该约束，已新增 `EclipseBloomParticles` 中央执行入口。Windows 正式编译通过；修复后的游戏内复测待 owner。详见 [本轮报告](docs/reports/testing/蚀日花冠粒子报错与独立配乐-2026-10-23.md)。

## 2026-10-22 蚀日花冠地图接入与正式资源审核（COMPAT / SCHEMA+ / WIRE+）

CR-2026-10-22-003～009 已在 L1/L2 修复，L3 待验收。详见 [专题报告](docs/reports/testing/蚀日花冠官方地图与资源审核-2026-10-22.md)。

- 003，P1：盾片早期缺独立完整三维资源；已接 Blender MCP 作者模型、同源 512 图标、地面/装备代理及缺资源拒绝。
- 004，P2：日核旧网格接缝/极点退化；作者网格及最终包九个部件逐一验证闭合、绕序、各连通体积和包络。
- 005，P2：平面预警在起伏地面可能埋入地表仍判伤；改为分帧贴地与真实绘制面判伤。
- 006，P2：玩家同帧死亡使 owner 失效时可能吞已击杀 Boss 的专属掉落；以已提交实例实际死亡及 loaded 目标场景为准，发奖仍幂等。
- 007，P2：迷雾 forceRenderingOff / Renderer 关闭仍通过预警覆盖门；补真实可绘制状态，执行隐藏/恢复回归。
- 008，P2：死亡放弃拒写时销毁 flush 可能提交旧 accepted pending；共享保存采集受待放弃与交付事务门约束。
- 009，P2：SpawnCore 再归一化抹掉花冠已缩放的伤害；仅花冠保留工厂倍率，其他 Boss 不变。

全量 750 守卫为 749 PASS / 1 无关既有大文件预算 FAIL；Windows 正式隔离构建、相关中央回归与 108 项反向验证通过。已实读最终资源和离线 URP 预览；未部署、启动游戏或访问存档。

## 2026-10-21 自定义实体绕过迷雾隐藏（COMPAT / WIRE+）

CR-2026-10-21-011，P2 / COMPAT / WIRE+ / Fixed at L1/L2，Needs runtime smoke：自定义装备 / 近战模型缺官方 CharacterSubVisuals，独立 NPC / 沙暴外形缺 FOW 接线，女巫技能恢复还会强制 Show 清除官方 Hidden。现在分别补齐原版组件、共享官方 hider 渲染绑定并移除跨职责 Show；友好 NPC 不改阵营，AI / 交互继续运行。真实资源与 DLL 链已核对，正式隔离编译、260 项 changed-only 守卫、38 条实链执行断言和相关回归通过；19 个隔离守卫变异及其中 15 个执行变异按预期失败、还原后通过。L3 待实机，验证与步骤见 FIX_TRACKER 同名条目。

## 2026-10-21 自定义友好 NPC 常驻头顶姓名（COMPAT）

CR-2026-10-21-003，P2 / COMPAT / Fixed at L1/L2，Needs runtime smoke：旧 `NPCNameTagHelper` 禁用原版名牌组件后手工投影、每帧强制打开姓名，俯视、第一和第三人称均常驻。原版基地友好 NPC 没有这类头顶名牌，姓名在对话 / 商店 / 交互界面中展示。现撤掉独立 UI，保留登记兼容与显式语言更新；不创建 actor / Health，不覆盖原版姓名键，普通刷新不重写延迟 actor 的当前语言。正式编译通过，L3 待实机；验证见 FIX_TRACKER 同名条目。

## 2026-10-13 推送后跨平台门禁修复（Fixed，COMPAT / SAFE，L2）

- CR-2026-10-13-012，P2 / COMPAT：天空岛导航、道路与摆放审计将 Windows CRLF 原字节 SHA 带入公开仓库，Linux LF 签出误报布局/证据过期；生产 `apply_navigation` 同样误拒绝。复用 `sky_island_road_layout` 的纯函数，新签名只统一 CRLF→LF，读取兼容既有原字节、LF 和 CRLF。真实坐标、字段、空白、BOM 与字符串变化仍拒绝；不重写现有地图或审计 JSON。三项旧码在真实 Ubuntu LF 环境均复现，修后两种换行及生产拒绝路径完成反向验证。
- CR-2026-10-13-013，P2 / SAFE：植物几何测试在公开源码 CI 强制读取私有 OBJ/MTL，干净签出必失败。源码模式改用公开摆放包络驱动真实细化算法，明确报告 `PARTIAL`；完整模式保留真实模型、原半径与拓扑检查，缺原件仍失败。公开 12 类 / 36 包络、本地 12 个真实模型及 4 类破坏/恢复检查通过，证据 `Build/ci-flora-20261013/probe-687b5c9a/evidence.json`。

首推提交 `15439905` 的远端 Guards 失败由以上两类原因导致，共 4 项；Wiki 构建与部署通过。修后公开 LF 副本全量 736 PASS / 9 PARTIAL / 0 FAIL，日志 `Build/commit-20261013/source-only-final-outside-build.log`；指纹证据 `Build/ci-fingerprint-20261013/`。本次只改离线工具、测试与文档，1196 个已编译生产源哈希一致；不据门禁修复宣称实机或性能验证完成。

## 2026-10-12 玩家反馈与 Player(1).log 全面修复（COMPAT / SCHEMA+，L1/L2）

| ID | 等级 / 状态 / 分类 | 修复与验证边界 |
| --- | --- | --- |
| CR-2026-10-12-001 | P1 / Fixed / COMPAT | 天空岛合同把 `SpawnTomb` 当硬门槛，死亡不掉落类 Mod 让进岛必被拒（日志两次）。现只要求 `SaveCharacter`，`SpawnTomb=false` 记警告放行；夹具改为「墓碑关闭放行 / 保存关闭仍拒」，守卫禁止回退。 |
| CR-2026-10-12-002 | P1 / Fixed / COMPAT | 官方狗子背包跨局保留，里面任何东西（含船票）都让白手起家静默回落普通模式，正式版无日志；出战遗种崽的独立背包又完全不查。裸装遍历带出拦截项并双语提示，船票放行，加载中不算空，补查崽背包。 |
| CR-2026-10-12-003 | P2 / Fixed / COMPAT | `TryStartModeD` 恒返回 true，开局武器没发出时留下半激活状态；旧回归把这当预期。现以本次启动结论返回，失败回滚并提示；夹具三条断言反转并补重试。路牌晚于启动时重新布置 Mode D。 |
| CR-2026-10-12-004 | P1 / Fixed / COMPAT | 第三方 `OnAfterLevelInitialized` 订阅者抛异常时官方 `afterInit` 永远为假，鸭王杯卡满 30 秒后中止退票。改认官方最终落点后写入的 `"Done!"`，顺序由真实 DLL IL 断言钉住（反向改值转红）。 |
| CR-2026-10-12-005 | P2 / Fixed / COMPAT | 鸭王杯中止原因只写 DevLog；退票提示不看结果；退票失败即丢凭据；地图界面打不开时走直接传送丢 H 意图且不扣票；付票到目标场景之间无退款依据；意外场景静默清状态。退票迁 `ModeHTicketRefund`（NotPaid / Refunded / Owed，欠账入口补发），`OnCostPaid` 即登记，其余分支如实提示。 |
| CR-2026-10-12-006 | P2 / Fixed / COMPAT | 航向仪入箱 / 入包后广播抛错被代码销毁；死亡提示无条件说「在箱子里」；任一模式激活时守卫被静默清掉。改用共享 `TryAddExtraItem` 与归属核对，提示按真实结果，冲突时明说一次。 |
| CR-2026-10-12-007 | P1 / Fixed / COMPAT | 一击打到 1 血时孩儿护我与二阶段同时启动，二阶段收尾撤无敌、开组件。现互斥；二阶段被打断时补完收尾；召唤请求作废或龙裔消失时联动死亡收尾；宿命回响龙裔刷在龙皇旁。 |
| CR-2026-10-12-008 | P2 / Fixed / COMPAT | 龙裔死后迟到的受伤回调仍起冰减速协程（日志 10 次 inactive 报错）。补存活 / 激活守门。 |
| CR-2026-10-12-009 | P2 / Fixed / COMPAT | 擂台阵营模式下死目标仍算「有目标」，攻击循环空转、心跳照刷，看门狗不触发。清掉死目标逼官方 AI 重新索敌，`Resume` 不写回死目标。 |
| CR-2026-10-12-010 | P1 / Fixed / COMPAT | 开局洗牌只改 `EnemyPresets`，出场读的过滤缓存不失效，顺序从未打乱，龙裔 / 龙皇 / 幽灵女巫固定最后一波。洗牌后失效缓存。 |
| CR-2026-10-12-011 | P2 / Fixed / COMPAT | 无间炼狱关闭奖励箱后龙皇 / 龙裔专属装备整份丢失；单波现金三等分丢余数、超过单堆上限被截断、发放前先清账。专属抽签抽成纯函数落地掉落；现金按堆叠上限分堆、余额记账户、记不进滚入下一波。 |
| CR-2026-10-12-012 | P2 / Fixed / COMPAT | 遗种巢风浪档卡面「不会真死」，实际 6% 删档。owner 定风浪档改不死、亡命档保留真死；在途旧记录经 `EffectiveDeathRate` 封顶，不改存档。 |

均为 L1/L2：真实 DLL 合同、守卫与执行回归、Windows 正式编译；未启动游戏、未读写存档。验收清单与玩法新增（回火液 500108、毒蛇匕首厚血项、宠物行动方式 / 喂食 / 经验共享 / 伤害曲线）见 FIX_TRACKER 同日条目与本地 `docs/reports/testing/玩家反馈全面修复_2026-10-12.md`。
