# CODE_REVIEW_FINDINGS.md — 已确认问题库

2026-09-30 天空岛模型碰撞修复（COMPAT / SCHEMA+ / L1+L2）：建筑占位被误裁成实体地面洞、硬物缺碰撞与名义包围盒误挡、救援以标记高度代替命中地面均已修复，验证和 COLL-01～06 清单见 [交付报告](docs/reports/sky-island/2026-09-30-天空岛模型碰撞修复与交付.md)。复审 CR-2026-09-30-001（P2 / OPERATIONAL）：导航指纹未包含桥梁硬面，也不检测新增硬物；Fixed：生产 apply_navigation 重新枚举当前硬物集合，指纹包含桥梁；11 项执行反例、两个恢复旧逻辑的内存变异和真实 Blender 来源校验通过。切片输入与导航几何未改，完整来源绑定后 raid 包再次 PhysX PASS。

更早的完整记录见 `archive/`；近期已闭环的大篇幅审计正文也按月份存档，当前文件保留索引与未闭环条目。

2026-09-29 天空岛发布前复审（2 项 Fixed / COMPAT），[完整证据与验收清单](docs/reports/sky-island/2026-09-29-天空岛发布前代码审查.md)：CR-2026-09-29-101（P1）修复官方“点击继续”等待被 120 秒场景超时误判；CR-2026-09-29-102（P2）补回装备特殊效果的岛内范围说明。证据止于 L1/L2，L3 待验；没有将历史实机结果沿用到当前候选。

## 2026-09-29 Mode H 发布前审核第二轮（14 项 Fixed / COMPAT / L1+L2）
CR-2026-09-29-013～015（P1）：看台退出回基地押注被误退、进程内换档后内容闸不重开、休眠原生敌人未清场；016～020（P2）：附加场景离场残留无敌中立、名人堂暂存失败重试误报成功、建页异常留下无按钮模态页、五处单行 TMP 框高不足、战斗每帧拼字符串；021～026（P3）六项。证伪 3、未证实 4；owner 拍板 2 项已落地（开战后退出 / 放弃押注按输结清、观战击杀不给经验），触发 / 修法 / 实机清单见[第二轮报告](docs/reports/reviews/2026-09-29-模式H发布前审核-第二轮.md)。
## 2026-09-29 Mode H 发布前审核（12 项 Fixed / COMPAT / L1+L2）

## 2026-09-29 Mode H 发布前审核与复核（19 项 Fixed / COMPAT / L1+L2）

001～012 见[前轮审核报告](docs/reports/reviews/2026-09-29-模式H发布前审核.md)，新增 013～019 的触发、位置、修复及实机清单见[发布复核报告](docs/reports/reviews/2026-09-29-模式H发布复核与验收.md)。717 守卫、110 执行夹具及 Windows 正式构建通过，无未修 confirmed finding，L3 尚待验收。
- CR-2026-09-29-001～003（P1）：实物押注通知异常双扣；旧仓库通知异常丢托管/已交付物品；钱包未就绪的恢复迟付及终季误退本金。SaveFailureRecovery 故障回归闭环。
- CR-2026-09-29-004～007（P1）：观战状态写后异常未补偿；早期恢复不能挂起；生成重试遗留主协程；跨季相同序号吞挂起退出。ModeHCombatRelease / ModeHReviewFixes 闭环。
- CR-2026-09-29-008（P1）：建页故障重入后旧页覆盖恢复态；ModeHPlayerFlow 九类页面与 owner 边界闭环。
- CR-2026-09-29-009～012（P2）：原图刷怪器部分回滚/原值错误；接力继承远程受伤；核心击杀归属与前批终结标签错误；Unity 假 null 残留死亡抑制登记。ModeHCombatRelease / ModeHMarketAudit 闭环。
- CR-2026-09-29-013～015（P1）：嵌套押品重复估值/额外扣现金；仓库未就绪误入永久人工介入；放弃覆盖已决押注。SaveFailureRecovery / GameplayLogFixes / ModeHRecoverySecondReview 闭环。
- CR-2026-09-29-016～017（P1）：无 Season / owner 的旧押品恢复动作缺失；倒地帧被胆怯截断接力。ModeHPlayerFlow / ModeHMarketAudit 闭环；恢复页不可关闭线索已证伪。
- CR-2026-09-29-019（P1）：初始/接力协程与增援配装异常不进技术重试。共用逐层协程驱动并补增援异常边界，ModeHReinforcementSecondReview 闭环。
- CR-2026-09-29-018（P2）：ERROR 切回时官方控制动作未停止，丢失原枪槽。先停本次动作再恢复控制，ModeHMarketAudit 闭环。

## 2026-09-28 天空岛全面审核与修复（8 项 Fixed / L1+L2，L3 待实机）

原审计 2 项 P1、3 项 P2 已修复；两条战斗线索经实际 DLL/行为树与隔离反例确证，另修 F3 窗口完整性，共 8 项。原始触发见[审核快照](docs/reports/reviews/2026-09-28-天空岛全面代码审核.md)，最终实现、复用/性能边界和实机步骤见[修复验收报告](docs/reports/sky-island/2026-09-28-天空岛修复与生产验收.md)。

| ID | 严重度 / 分类 / 状态 | 修复与证据 |
| --- | --- | --- |
| CR-2026-09-28-004 | P1 / COMPAT / Fixed | `SkyIslandWorldStoryRewards` 与 `SkyIslandStoryService.Begin/EndKeepsakeDelivery` 按实际缓冲回执提交，覆盖满包罗盘；冻结交付中采集，寄存实物/收据同批保存，独立快照义务在 typed pending 消费后继续驱动 Tick。Story/Delivery 回归覆盖重开、物理失败、换槽、只采集未写盘和限频重试；L3 见 FIX-SKY-01。 |
| CR-2026-09-28-002 | P1 / COMPAT / Fixed | `SkyIslandItems.InjectLocalization` 同时注入 18 件物品名称与 `_Desc`，删除官方空 setter/只读属性的无效反射。真实注入入口中英回归及反向验证通过；L3 见 FIX-SKY-02。 |
| CR-2026-09-28-001 | P2 / COMPAT / Fixed | `SkyIslandStoryService` 目标/摘要加入接纳快照身份缓存键，清场数组变化及时失效。两端守卫和中英回归通过，正常稳定状态 10000 次读取托管分配为 0 B；L3 见 FIX-SKY-03。 |
| CR-2026-09-28-005 | P2 / COMPAT / Fixed | 信鸽/采集 Relabel 复用基类 ApplyInteractName；搜索点复用章节表和全局语言刷新，延迟 Start 不覆盖新语言。现存对象热切执行回归及反向通过；L3 见 FIX-SKY-02。 |
| CR-2026-09-28-003 | P2 / COMPAT / Fixed | `SkyIslandSceneReferenceBridge.InjectLocalization` 接全局刷新，语言变化不重装桥。场景名称热切回归通过；L3 见 FIX-SKY-02。 |
| CR-2026-09-28-006 | P2 / COMPAT / Fixed | 原 U-CB-01：实际黑板/行为树确认强制追踪覆盖敌对 NPC 目标。自动遭遇改用官方感知，手动挑战保留追踪，不新增全局扫描。真实生成入口与选敌逻辑回归/反向通过；专属招式仍按原玩家目标设计，L3 见 FIX-SKY-04。 |
| CR-2026-09-28-007 | P2 / COMPAT + WIRE+ / Fixed | 原 U-CB-02：原官方方法在受控 9 目标场景只处理 8 个。新增 `SkyIslandExplosionBufferPatch` 临时借换 manager 缓冲，饱和增长、按重入深度复用，Finalizer 恢复原数组/Health列表/掩码，继续执行官方伤害循环。64 条真实 Harmony 隔离断言及反向通过；真实 PhysX/L3 见 FIX-SKY-04。 |
| CR-2026-09-28-008 | P2 / COMPAT / Fixed | `SamplePerformanceWindow` 复用完整窗口判据，锁定场景、拒绝不足额样本，并在取消/Dispose 后关闭开窗时录制。63 条 F3 执行断言及反向通过；跨图或中断样本不更新有效基线，L3 见 PERF-SKY-01～03。 |

终态：全量守卫 711/711、执行回归 109/109；1119 份 C# 的 Windows 正式/Dev 隔离构建 exit 0，正式 14 个 Dev 标识缺席、Dev 全部可检出。回归目标 net8.0 在本机 .NET 10.0.5 以 Major roll-forward 执行。没有部署、启动游戏或读写玩家存档，尚不能宣称生产实机验收完成或整岛无性能问题。

## 2026-09-27 生产就绪全面审计（代码修复与 L1/L2 已闭环，L3 待实机）

CR-2026-09-27-401～406 的原始发现、逐项回填及当时验证已[按原文归档](archive/CODE_REVIEW_FINDINGS_2026-09-27_production_readiness.md)。运行时验收仍见[续审报告](docs/reports/reviews/2026-09-27-生产就绪全面审计-天空岛重点-续审.md)，不得把当时离线通过解释为 L3 已完成。

<!-- BEGIN UI VFX BOSS DESIGN REVIEW 2026-09-27 -->

## 2026-09-27 UI / 特效 / Boss 设计审核（Fixed / L1+L2，L3 待实机）

只读审核三路（UI 与波次遮挡、特效塑料感、Boss 技能机制），随后按 owner「全部修复」实施。UI 与波次提示未见遮挡或塑料感高危项（横幅走官方 `NotificationText` 队列，实际屏幕位置待实机）；特效已普遍三段式。确认并修复：

| ID | 严重程度 / 兼容性 | 问题 | 状态 |
| --- | --- | --- | --- |
| UVB-2026-09-27-01 | 高 / COMPAT | 龙王技能弹、冲锋、撞击，龙裔冲撞直接 `Hurt` 不看 `Dashing`，与官方子弹/爆炸/近战的翻滚豁免不一致 | Fixed：`Utilities/BossSkillDamageRules.cs` 统一瞬发豁免；持续地面区域按官方 `ZoneDamage` 不豁免 |
| UVB-2026-09-27-02 | 高 / COMPAT | 幽灵女巫（冠军之影同源）主力招式零硬直、固定轮播、P3 残喘突袭与侧翼压制同码、两组技能只有配置 | Fixed：三招收招硬直、战术包袋随机不连出、残喘重斩追击、删死配置、起手音 |
| UVB-2026-09-27-03 | 中 / COMPAT | 冠军之影 1.6 倍与全局 Boss 倍率不作用于自制技能伤害；龙裔二阶段覆盖掉全局倍率 | Fixed：按「当前 Stat / 刷怪基线」放大；龙裔二阶段乘法保留 |
| UVB-2026-09-27-04 | 中 / COMPAT | 龙王大招预警期间常规扫射不停、虚影枪末线 0.1 s、二段冲锋无预警 | Fixed：预警类大招停火、末线 ≥0.4 s、二段 0.3 s 倒计时光圈 |
| UVB-2026-09-27-05 | 中 / COMPAT | 龙裔「火箭弹」无弹体无预警、在玩家脚下瞬间结算；二阶段冲刺无起手 | Fixed：锁点 + 贴地预警 0.8 s + 落点爆炸（1.6 m/10 伤）；冲刺 0.3 s 扬尘起手 |
| UVB-2026-09-27-06 | 中 / COMPAT | 丧尸 Hunter 冲刺起手 0.3 s；同场多 Boss 技能无全局节流 | Fixed：0.45 s；`BossSkillGlobalSpacingSeconds` 错开起手（Boss 数量与奖励不变，未封顶以免改经济） |
| UVB-2026-09-27-07 | 低 / COMPAT | 焚皇戟火焰单色覆盖、丧尸支援弹无辨识、龙王冲击波单色、成就灰阶字面量色 | Fixed：三段渐变/金红色相、主题色拖尾、色相推进、`BossRushUIColors.Disabled` |

未改：ModeH 危险按钮（B-04）核实已在 `fe122a36` 修复；Storm 脉冲圈登记 `TelegraphRingNames` 需搬常量并改三处结构守卫，而 Storm 逃圈速度已有 `SkyIslandStormEchoEscapePropertyTest` 保护，不做。

<!-- END UI VFX BOSS DESIGN REVIEW 2026-09-27 -->

## 2026-09-27 模块解耦独立审计修复（L1/L2 已闭环）

基点 `f90d6a5d` 的七项确认问题已修复；拉取前 699 守卫、99 隔离回归、Windows 正式/Dev 构建全部通过。随后合并远端的终态验证另见 [完整修复与合并记录](architecture/MIGRATION_REPAIR_20260927.md)，不能以此前结果替代合并后验证。

| ID | 严重度 / 分类 | 修复与证据 |
| --- | --- | --- |
| CR-2026-09-27-001 | P1 / COMPAT | D/E/F owner 先失效会话后清本局；E 预热 child、F 0.25 秒待销毁对象均回收；真实销毁/退出 103 断言及强反向。 |
| CR-2026-09-27-002 | P1 / COMPAT | 真实 E/F gate 拒绝销毁 owner，商人异常判据失败关闭；商人与共享敌人迟到结果回归及反向通过。 |
| CR-2026-09-27-003 | P1 / OPERATIONAL | 聚合器只执行本次 build 的 TargetPath；保留旧 DLL 的真实保存屏障破坏命中预期失败，恢复后绿。 |
| CR-2026-09-27-004 | P2 / COMPAT | ReferenceEquals 活动宿主门阻止未装配重复实例清全局；真实 Awake/OnDestroy/dispatcher 及 3 个反向。 |
| CR-2026-09-27-005 | P2 / SAFE | 静态缓存守卫只读正式清单并追真实 OnDestroy 调用图；28 用例与 4 个含旧 tmp 的强反向。 |
| CR-2026-09-27-006 | P2 / COMPAT | Arena 私有时钟和窄动作，套装/装备/NPC 独立 owner 清理；39 项生产路径检查、11 个强反向。 |
| CR-2026-09-27-007 | P2 / SAFE | 两篇当前天空岛专题改为真实正式路径，核对目标存在。 |

文件位置、触发、影响、源哈希、各夹具替身边界及剩余 L3 在完整记录。实机仍为 MANUAL_PENDING，未将构建或隔离回归写成游戏内已生效。

<!-- BEGIN JEFF FRUIT REAUDIT 2026-09-27 -->

## 2026-09-27 Jeff 任务与菜地果实链路复审（8 项 Fixed / L1+L2，L3 待验）

结合 `鸭科夫源码/` 与本机官方 DLL 复审任务挂载与推进、收获到手、吃下变身三条链，重点补上一轮替身没有模拟的官方行为（`CanEditInventory`、雨天元素系数、`Garden.Load` 缺表分支）。完整依据、反驳 / 接受项与人工步骤见 [复审报告](docs/reports/reviews/2026-09-27-Jeff任务与果实复审.md)。编号 301 起。

| ID | 等级 / 分类 | 确认问题与修复 | 验证 / 状态 |
| --- | --- | --- | --- |
| CR-2026-09-27-301 | P1 / COMPAT | 引导一条接一条，第 5 条「天空岛装备」无前置，未做完 Jeff 序章的档卡死后面九条。`GuidePrerequisiteMet` 加 `SkyIslandPreludeFlow.CanUseRoute`，前置未到跳过。 | Fixed / L2。JeffQuestFlow 新档走完其余 11 条；反向探针转红。 |
| CR-2026-09-27-302 | P2 / COMPAT / WIRE+ | 吃果实时 `CA_UseItem` 在跑，官方 `SetCharacterModel` 把 `holdWeaponBeforeUse` 清成 -1，`OnStop` 切近战（无近战则空手）。换模前后写回官方字段。 | Fixed / L2。三果实 × 三种吃前武器；DLL 契约核对字段与调用顺序。 |
| CR-2026-09-27-303 | P2 / COMPAT | 到期时正在近战 / 冲刺 / 交互 / 用道具，换回模型会连手持武器一起销毁。改为等动作结束再恢复，最多 3 秒。 | Fixed / L2。等待、放行、卡死强制恢复；探针转红。 |
| CR-2026-09-27-304 | P2 / COMPAT / WIRE+ | 官方雨天对火焰系数再减 0.15 不截断，乘 0 的火免变成火伤回血且越过上限。雨天补排在乘算之后的 `Add +0.15`。 | Fixed / L2。夹具 Stat 按官方 Order 重写；探针得 -0.15 转红。 |
| CR-2026-09-27-305 | P2 / COMPAT / WIRE+ | 作物表注入晚于 `Garden.Load` 时，未初始化 Crop 带默认 Data 进字典，下次存档抹掉 Mod 作物。注入成功后对含空壳的 Garden 重读一次存档。 | Fixed / L2。早读→晚注入→恢复，健康菜地与模板不重读；探针转红。 |
| CR-2026-09-27-306 | P3 / COMPAT | `AddDynamicEntry` 在官方物品表未就绪时返回 false，兜底注册却记成功。改为检查返回值、交给重试。 | Fixed / L2。探针转红。 |
| CR-2026-09-27-307 | P3 / COMPAT | 官方接取后 Mod 事实写失败只打 DevLog（正式构建不存在），任务无声消失。改为给玩家原因，投影退回可接取页。 | Fixed / L2。失败提示、退回、重试。 |
| CR-2026-09-27-308 | P3 / COMPAT | 官方作物不浇水不长，起步种子提示没说。补中英一句。 | Fixed / L1。 |

反驳：切语言后种子名不跟随（已在 `InjectLocalization_Extra_Integration`）。接受：基地可食用（Wiki 设计）、额外攻击不出命中标记（`isFromBuffOrEffect` 设计）、护甲削减额外伤害（数值取舍）、换武器慢一帧与女巫持枪手势（待实机）。

<!-- END JEFF FRUIT REAUDIT 2026-09-27 -->

<!-- BEGIN JEFF FRUIT AUDIT 2026-09-26 -->

## 2026-09-26 Jeff 任务与菜地果实链路（4 项 Fixed / L1+L2，L3 待验）

结合官方反编译源与本机 DLL 审查 24 个任务 ID（Jeff 21、岛上 3）、收获路线与三种变身。完整接线、验证边界和人工步骤见 [审查报告](docs/reports/reviews/2026-09-26-Jeff任务与果实生产审查.md)。编号 201 起区分同日并发专题。

| ID | 等级 / 分类 | 确认问题与修复 | 验证 / 状态 |
| --- | --- | --- | --- |
| CR-2026-09-26-201 | P1 / COMPAT / WIRE+ | 官方食用二次 CanBeUsed 失败不进 OnUse，但 OnFinish 仍扣数量，旧补偿漏掉读条期间失效。果实专用前缀在扣量入口复查主玩家/场景/资格，原 OnUse 启动失败补偿保留。 | Fixed / L1+L2。真实前缀与服务覆盖三果实 1/20 堆叠、死亡/切图/停用/场景消失/已有变身；DLL 核对方法字段。 |
| CR-2026-09-26-202 | P2 / COMPAT | 官方 OnAttackEvent 即使近战 StartAction 被拒也发，导致果实能力被无效输入触发。改订阅原 CA_Attack.OnAttack，保存同一 action 引用并退订。 | Fixed / L1+L2。成功近战有能力，无效输入无能力；恢复/生命周期回归通过。 |
| CR-2026-09-26-203 | P2 / COMPAT | ReadyToDeliver 只表示局内目标结算，正文却给未建菜地/未摆战利品标“已达成”。基地正文仅认 baseFact，历史 Completed 由客户端传完成事实。 | Fixed / L1+L2。中英目标、缺设施拒交、补齐可交及历史不反转；交付门原本正确。 |
| CR-2026-09-26-204 | P2 / COMPAT | 鸭王杯“看完一场”使用 IsMatchInProgress，刚开战就达成。只读 HasCompletedMatch 改查当前已有已结算/已归档战报。 | Fixed / L1+L2。真实属性提取执行，未结算拒绝，正常结算达成；不新增存档或改经济数值。 |

没有把官方异步发货的理论异常当作已发生丢物。背包失败转仓、满仓转自提已核对实际 IL，物品数量/画面仍待 L3；不以隔离宿主替身冒充 Unity 或 Harmony 已生效。

<!-- END JEFF FRUIT AUDIT 2026-09-26 -->

<!-- BEGIN SKY RELEASE FINDINGS 2026-09-26 -->

## 2026-09-26 天空岛发版审查（COMPAT / SAFE；修复完成，L3 待 owner）

本轮按用户授权的正式玩家可达、六居民关系、内容承诺与战斗品质标准审查。详细证据与候选门禁见 [发版验收](docs/reports/sky-island/天空岛_发版验收_2026-09-26.md)，完整内容、NPC、Boss 矩阵链接在报告内。编号101起用于区分同日并发专题；本节只记有源码根因的缺口，不将未实机的观感推测记为确认缺陷。

| ID | 等级 / 分类 | 已确认问题与修复 | 验证 / 状态 |
| --- | --- | --- | --- |
| CR-2026-09-26-101 | P1 / COMPAT | 本轮“六居民全关系”标准下，浮舟、眠苔、折翎、钟守是非永久蓝图；`AttachPermanent` 又只认晴禾/苇白，改配置后婚后恢复仍无法接航路。补四份现有关系配置与偏好，绑定按六人地标识别，复用原好感/礼物/婚姻/恢复。 | Fixed / L1+L2。六人数据、真实绑定点击及关系生命周期回归；逐人实机婚姻仍待验。 |
| CR-2026-09-26-102 | P1 / COMPAT | 折翎显隐读取持久战败位，且 `HasStarted` 将旧 `Cleared` 当开战，旧战败档永久失去本人。新增局内 `WasStartedThisRaid` 查询，本趟战后休整、次趟恢复，保留旧选择与奖励事实；婚后/名册文本同步。 | Fixed / L1+L2。已保存Cleared与本趟Started回归；两守卫反向验证；旧档实际恢复待验。 |
| CR-2026-09-26-103 | P2 / COMPAT | 噬风首战圈随追逐移动，固定半径/预警时长无法保证玩家后撤可离圈。首战/回响均锁风眼，圈、灯、伤害共用原点；保留原伤害、阶段及高输出压阶段。 | Fixed / L1+L2。StormEcho/Playability/逃圈属性、首战既有F3步骤扩展；正常负重实打待验。 |
| CR-2026-09-26-104 | P2 / COMPAT | 圈工厂材质失败返回disabled但非null，控制器可无圈伤害；匠首部分圈生成失败仍炸全部预排落点。公共造圈拒disabled、噬风缺圈取消，星焰只结算成功有圈的对应点。 | Fixed / L1+L2。守卫与实际破坏红样本；资源实机不可见仍判不合格。 |
| CR-2026-09-26-105 | P2 / COMPAT | 穗镰镰扫范围随追逐移动，泥地减速时玩家可能不断被重新卷入。起手固定圆心，复用BossAIController站定蓄力及恢复，伤害位置与预兆相同。 | Fixed / L1+L2。守卫固定伤害圆心破坏转红；实际暂停/恢复和泥中应对待验。 |
| CR-2026-09-26-106 | P2 / COMPAT | 蚋笛翁被打断仅4.5秒冷却，比成功施法的9秒更快，正确打断反增加压力。打断改13.5秒，保留1秒/8伤打断门与成功9秒。 | Fixed / L1+L2。真实规则固定期望/上限判据；节奏取舍与回退见报告。 |
| CR-2026-09-26-107 | P2 / COMPAT | 镜中客倒影给不透明角色材质写alpha，无法保证半透明。保留BakeMesh及贴图，复用共享Alpha材质和正确Tint，不复制Item、不改本体。 | Fixed / L1+L2。材质接线守卫与包读取；实际透明层次和遮挡须owner目检。 |
| CR-2026-09-26-108 | P2 / COMPAT | Wiki承诺噬风/回响起大风，原WindLevel在白天栈道及十灯后只到微风。风暴且在栈道/桥时优先返回2，风核仍2→1，保留灯/香用途。 | Fixed / L1+L2。真实规则覆盖日夜/桥/栈道/十灯/风核及结束恢复；实机风级和寒意待验。 |
| CR-2026-09-26-109 | P2 / COMPAT | 婚礼视频用Assembly.Location查资源，字节加载时为空而直接回退。改用已验证GetModPath，保留角色→通用→文字过场次序。 | Fixed / L1+正式编译。现有视频文件定位静态核对；未声称六位有专属视频。 |
| CR-2026-09-26-110 | P3 / SAFE | Wiki新人“读坐标”与实体仪器交付不符、招引“半成”数值错、维修漏最低收费、噬风固定四次不符跨阈值行为、晴禾位置/天色切换描述不清。双语按生产事实修正，同时补全部六位关系说明。 | Fixed / L1+Wiki构建。原承诺/修正依据见内容矩阵，不靠删合理内容结案。 |
| CR-2026-09-26-114 | P2 / COMPAT | 并发基础数值迁移后，属性从Forge移到克隆preset阶段，序章SpawnBoss遗漏新入口，正式必经守卫仍用旧底模属性。现于官方CreateCharacterAsync前按K3_Relay/0/Chief应用同一规则，保留实体仪器/掉帽/敌对与owner。 | Fixed / L1+L2。逐字抽取SpawnBoss，钉工厂调用时375生命、源preset不变与重刷不复利；相关守卫反向验证。 |
| CR-2026-09-26-115 | P2 / SAFE | SKY_RESIDENTS以未婚外地registry实例豁免岛上缺席，且原只查组件不查能否进入菜单，可能假绿。现未婚必在岛，聊天/送礼/剧情必须在官方交互组且启用；剧情隐藏明确未观测，婚后显示沿原规则。 | Fixed / L2。186条纯判据，六项实际破坏转红/字节还原；日常真实交互仍待L3。 |

本轮没有新L3，不把部署、资源可读或F3规则回归写成实机好玩/视觉合格；全仓并发变更导致的候选红项在总报告单列，未覆盖其他会话代码。最终构建哈希、门禁、owner R01–R09与看图文件名均以总报告为准。

| CR-2026-09-26-116 | P2 / COMPAT | 已婚折翎同场随行时仍可从本人或Search_F开战，居民显隐不拥有配偶，导致同身份友敌同时在场。统一挑战判据拦截该组合，提示先送回家；陈旧确认回调使用同一拒绝原因。 | Fixed / L1+L2。真实入口回归覆盖64组合、中英、陈旧确认与送回家后恢复；婚姻组947 checks通过，F3 M22待实机。 |

<!-- END SKY RELEASE FINDINGS 2026-09-26 -->

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


## CR-2026-09-26-001 — 船票 owner 迁移后地图费用读回旧 ID（P1，已修复，COMPAT）

触发：地图选择创建传送费用时，`BossRushMapSelectionHelper.GetBossRushTicketTypeId` 仍通过 `typeof(ModBehaviour).GetField("bossRushTicketTypeId")` 查询；该成员在迁移后成为转发属性，反射恒取不到字段。注册已得到 500001 时仍退回 868，费用查询、票数检查和相关退款查询因此使用错误物品 ID。

修复：查询直接读取唯一注册 owner `IntegrationRuntimeModule.BossRushTicketTypeId`，保留正 ID 判断、未注册的 868 回退及防崩路径，不新增缓存或第二份状态。`IntegrationLeafOwners` 逐字执行生产注册、查询与 Cost 构造；修复前实跑命中 `map ticket lookup lost registered runtime owner ID`，修复后通过。新增断言覆盖 500001、未注册回退、备用正 ID、再次注册刷新、零现金且一张票。证据：`Build/migration/p6-ticket-before.log`、`p6-ticket-after.log`，级别 L1/L2；真实船点扣票/取消/退款仍由 `architecture/MIGRATION_ACCEPTANCE.md` 的 M_ENTRY_01–03 实机验收。

## 归档索引

- 2026-09-24 UI 共识对照审查中确认的缺陷（均 Fixed / L1+L2，L3 待 owner） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-23 UI / 交互 / 特效审美审查中确认的缺陷（均 Fixed / L1+L2，L3 待 owner） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-23 人工实测 16 项中确认的缺陷（均 Fixed / L1+L2，L3 待 owner） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-22 全仓审计条目修复闭环 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-20 人工实测第二轮补漏（COMPAT） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-20 资源生产化续作（COMPAT / OPERATIONAL） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-20 Unity 资源交付与运行时所有权（COMPAT / OPERATIONAL） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-20 词缀选物 UI 覆盖（COMPAT） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-19 奖励池可靠性复核（COMPAT） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-19 Mode G 异常路径复核（COMPAT） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-19 NPC 对白与天空岛气泡复核（COMPAT / SAFE） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-19 鸭科夫日报入口、阅读与边界复核 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-18 晴岚群岛生产复核：库存与奖励交付 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-18 Mode G 生产审核与可玩性修复 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-18 鸭皇图鉴生产审核 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-18 竞技场后山生产复审 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-20 第三轮（外部审查 11 条复核） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-21—22 全仓范围审计阶段记录（SAFE / OPERATIONAL；未修代码） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 天空岛导航优化后全面复审（023–025 已修复，待 L3） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 天空岛导航与场景刷新复核 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 天空岛婚姻修复复审（两项均已修复，待实机） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 全项目续审：战斗追加效果与生命周期 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 天空岛 NPC 中途结婚与剧情衔接（已修复，待实机） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 龙裔燃烧弹误选烟花 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 全项目玩法闭环复核：无伤判定、纯演出与额外战利品 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-17 天空岛主线与任务生命周期复核 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-16 天空岛昼夜时钟与官方任务接口审核：1 P1 + 1 P2 + 2 P3（均已修，L1 / L2） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-16 天空岛入口与 Jeff 序章审核：2 P1（均已修，L1 / L2） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-15 F3 全自动验收首轮复核补修：1 P2 + 2 P3（均已修，未实机） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-14（四）UI 优化对照审核：6 P2 + 18 P3（均已修）+ 5 条 PLAUSIBLE → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-14（三）天空岛 B 轮「噬风·回响」：1 P3（已修）+ 两条帧时间线索的处置 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-14（二）天空岛首轮岛内 F3 实机日志复核：1 P2 + 3 P3（均已修）+ 线索 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-14 天空岛与共享 UI「实机前减负」：4 P2 + 5 P3（均已修）+ 登记与未验证线索 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-13（第三轮）天空岛 owner 试用反馈：4 P2 + 2 P3（均已修）+ 1 条 documented 决策 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-13 天空岛 UI 美术化审核：1 P1 + 4 P2 + 2 P3（均已修） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-12（第四轮）玩家链路审核：3 P2 + 4 P3（均已修，含待拍板项 R-6 落地） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-12（第五轮）owner 授权后的待拍板项落地：1 P1 + 2 P2 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-12（第三轮）F3 实机报告驱动：1 P1 + 2 P2 + 4 P3（均已修） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-12（第二轮）关闭历史 Open 项：1 P1 + 1 孪生缺陷 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-12 近两月新增内容的「可玩性」审核：2 P2（均已修）+ 2 条登记在案的取舍 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-11 本轮全面生产审核新增确认项 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-11 天空岛内容批次四「云蚋」：接判夜与灶火时确认并修掉的既有缺陷 1 P2 + 1 P3（Fixed） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-11 天空岛内容批次三：顺带确认的既有经济风险 1 P1（Fixed，同日 owner 授权拍板后修） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 天空岛内容批次二：评估报告遗留项 1 P2 / 3 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 天空岛可玩性与时长评估：4 P2 / 3 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 天空岛全方位审核：3 P1 / 13 P2 / 5 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 碰撞排障：1 P1（替换件是空气墙、附加件能穿过去） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 地形全黑排障：1 P0（鸭科夫跑在 URP Deferred，自研着色器没有 GBuffer pass） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 船点入口误报排障：1 P2（`基地船点入口未找到` 是假警报） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 实机日志排障：3 P0（天空岛 100% 进不去，三个独立成因） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-10 天空岛验收设施轮：0 新 confirmed，3 条既有 finding 补 L2 证据 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-09 天空岛全面审核（第三轮）：1 P1 / 2 P2 / 6 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-09 天空岛可玩性复审（第二轮）：1 P1 / 5 P2 / 2 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-09 天空岛进出岛流程对照复审：2 P2 / 1 P3 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-08 天空岛全面审计追加：1 P1 / 1 P2 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-08 天空岛端到端验收：3 P1 / 1 P2 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-08 附加资源 Scene 与装备卸装：1 项 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-07 最新实机日志修复：7 项 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-07 近两周复核与 F3：4 项已修复 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-06 全量深度审查：5 P1 / 5 P2 已修复（2026-09-07 回填） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-06 设计与代码规范复审：D-4 / D-3 / D-2 三项已修复 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-06 冰霜 / 雷霆套装开放获取后的全面审核：4 项（均已修） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-06 冰霜 / 雷霆套装重做时确认的 3 项（均已修） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-05 二次深度复审：10 项已完成代码修复（2026-09-06 验收） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- 2026-09-05 全面复审：15 项已完成代码修复 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-001：遗种蛋与词缀熔石 100% 作废（两个新系统的入门产出口全断） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-002：无间炼狱下全部额外掉落丢失（含既有的寒霜长矛与女巫镰刀） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-003：龙皇绕过掉落登记，defer 判定对它恒假 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-004：Mode H 赔率页「锁盘」被推出屏幕，玩家只能弃局 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-005：Mode H 入口页第 4、5 张选秀卡被画在动作按钮底下 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-006：Mode H 恢复壳动作行 5 个按钮就出面板 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-007：Mode H 赔率分量 18 条标签双前缀，全显示星号 raw key → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-008：焚心椒「换弹更利索」用了不存在的 stat key（丧尸模式同款奖励一并失效） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-009：Mode F 血猎 Boss 加速完全不生效 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-010：Mode H 真实押品在仓库满时只留在内存，重启/切槽即永久丢失 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-011：Mode H 押品阶段机三个死态，中止返还必然失败并连带锁死七个旧模式入口 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-012：随机事件乱入 Boss 顶掉本波 Boss 身份，标准竞技场卡波或误推波 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-013：`ShowMessage` 在正式构建里对玩家完全不可见（约 137 处调用） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-014：随机事件商人交互名 key 全仓零注入 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-015：大兴兴血脉的遗种巢随从入场即被自家清理扫描销毁并循环重生 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-016：空投「翻箱保护」判据选错，宽限窗口几乎永不生效 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-017：两个落盘协调器的重试链被自身消费，SaveFile 失败后数据永不落盘 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-018：Mode F 补位重生克隆 preset 未挂租约，每次补位泄漏一个 ScriptableObject → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-019：九个新 TypeID（500059-500067）全部未登记掉落黑名单 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-020：本轮次要项汇总（7 条 P2/P3） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-022：在线 Wiki 的 favicon 一直 404 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-021：多行 callout 在线上 Wiki 掉出提示框（渲染缺陷） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-019：Mode H 八条战痕只有两条能生效（触发接线 + 自结算分量双重断链） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-020：战痕的 `appliesWhen` 条件层完全未求值 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-017：Mode H 口令点火目标无生产者，`finish` 整条是空操作 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-018：Mode H 战场快照的重建侧整条不可达，与 §20.3 恢复语义互斥 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-016：游戏内 Wiki 三处内容与代码不符（玩家可见） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-012：模式H 伤病 / 战痕 / 公开异常三层内容整体不生效 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-013：ERROR 完整互换（§17.6.5）从未被调用，且租约不让渡输入 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-015：焚天龙皇不掉词缀熔石（挂接点漏并联） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-014：ApplyRetirement 零调用点，名人堂把冠军与替补记反 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-001：模式H 押品脱离仓库后阶段推进失败无回滚，真实物品永久丢失 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-002：模式H 濒退制「休息一场解除带伤」完全未实现 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-003：模式H 锁盘按钮所有失败原因均静默 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-004：ERROR 互换期间的击杀归属渗入图鉴与日报 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-005：战役交付退款失败时可重复领取奖金 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-006：ResolveFallbackActions 已成死逻辑 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-007：GameQuality 的 CS0649 注释与实际取色不符 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-008：摘要集合语义清单漏登记第二份入场名单 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-009：非本波 Boss 经掉落漏斗推进波次（跳波 + Mode D 跨模式串台） → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-010：空投补给箱到时即销毁，不看玩家是否正在开箱 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-03-011：战役目标追踪按「进场景」而非「开局」武装，且换模式不解除 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-001：F3 直载基地子场景导致黑屏，模式失败返程污染后续用例 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-002：F3 终章 DamageInfo 零值初始化造成伤害空引用 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-003：F3 验收源码引用三个不存在的 API，阻断正式编译 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-004：距离休眠退订使用了角色的主场景索引 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-005：Mode H 认证拒绝已归一化的克隆，且旧受控击杀未触发死亡 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-006：撤离工厂删除官方回调后仍跳过通知与返程兜底 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-007：BGM 非空曲目表经 JsonUtility 读取后数组为空 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-008：F3 在 Mode F 退出重置后读取瞬时结算标志 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-009：F3 在标准 Boss 完成登记之前击杀创建中对象 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-010：F3 多波测试把波次推进误当作下一波生成完成 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-011：Mode H 诊断伤害空来源与外部死亡订阅不兼容 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-012：F3 同帧连续击杀触发外部经验提示的未初始化文本解析 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-013：Mode H 口令矩阵无人写入，缓存也未恢复逐效果证据 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-014：丧尸撤离验收要求正数净化点，却没有准备样本 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-015：H 成功创建赛季后保留入场意图，回到地图会重开 H → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-016：H 将冻结弹药写入装备槽，真实比赛生成反复失败 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-02-017：终章直接销毁 Boss 未清掉熔石掉落订阅 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-05-001：焚天龙铳切弹时容量 baseline 会被取整反推污染 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-05-002：焚天龙铳场景清理会丢失手持枪弹种 baseline → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-05-003：焚天龙铳射击热路径每发重复写 Stat → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-01-001：售货机 UI 崩溃 — 延迟注入商品未缓存 itemInstance → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-02-001：许愿台弹幕当前打开轮次不会接入新拉取结果 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-02-002：许愿台弹幕失败结果被 45 秒 TTL 缓存，重开面板也不会立即重试 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-07-02-003：许愿台关闭后未解绑静态弹幕回调，旧 View 会被挂到请求结束 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-08-17-001：Mode G 官方快照绕过逐 key eligibility，且九波计划错误要求整局全局去重 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-17-002：未知存档版本和临时挂起宿敌可能被后续对局覆盖 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-17-003：Mode G 奖励 API 回调异常会误判交付失败，Rewarding 死亡可能被完成回调抢占 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-17-004：Mode G 启动退款所有权不唯一，后续初始化异常可能双退 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-17-005：Mode G 候选地图没有显式验证状态，未实测地图会被当作 Verified → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-008：模式H 锁盘按钮转换非法，玩家被困在时停赔率页 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-009：模式H 生成回滚「退回看盘」转换非法，成功路径卡死在 MatchSpawning → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-010：模式H Recovering 是死态：全部技术故障出口通向无按钮的壳 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-011：模式H shutdown 闩锁永不复位：一次会话只能玩一局，二次入场吞船票并搁浅 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-012：模式H 恢复壳在重启后不可达，「船坞恢复分支」未实现 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-013：模式H 开局中止链全程无玩家可见文案，14 个 Unavailable_* 键零消费 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-014：模式G 无伤成就读跨局残留的 HasTakenDamage，同进程受过伤后 flawless 永久锁死 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-015：遗种巢 会话重启后血脉目录空窗：进一次竞技场之前官方血脉在基地全面不可用 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-016：遗种巢 关开关不清掉落追踪，dormant 契约被已挂接的 per-boss handler 穿透 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-017：日报 开关关闭期间换档：跨存档槽状态渗漏并覆写新档 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-018：模式H P2/P3 打磨项汇总（复审确认，6+3 项） → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-019：模式G P2 打磨项汇总（复审确认，4 项） → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-020：遗种巢 P2：PetNestDropService._hooks 慢泄漏 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-29-021：日报 P2/P3 打磨项汇总（复审确认，1+5 项） → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-001：后山 P1：出击餐在正常流程中永远不会生效 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-002：后山 P1：展示柜加成在战局内实际不存在 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-003：征程 P1：终章决战打输一次即永久卡死 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-004：后山 P1：展示柜收藏跨存档槽泄漏，可写脏另一个档 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-005：征程 P2：召唤石维护 tick 每帧分配字符串 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-006：征程 P2：契约 HUD 每帧构建字符串，与头注释承诺不符 → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-007：征程/后山 P3 设计取舍汇总（7 项） → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-08-31-008：全内容可用性复核补充（5 个 P1 + 2 个 P2） → `archive/CODE_REVIEW_FINDINGS_2026-08.md`
- CR-2026-09-04-021：真实押品 journal 与仓库快照没有共同提交 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-022：持久化 escrow 快照没有跨会话实物重建入口 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-023：Mode H 延迟物理落盘后下一帧丢弃欠账 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-024：真实押品同步四阶段必然撞每帧保存节流 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-025：Mode H 赛前阵容、kit 与口令缺少玩家编辑入口 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-026：孵化新崽已保存但蛋消耗未采集 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-027：远征发奖游标先于实物快照落盘 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-028：新材料和餐食继承便携安全区使用行为 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-029：日报跨日改写待发悬赏日期 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-030：普通 NPC 模块与永久模块重复生成小满 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-031：无间炼狱龙皇额外掉落订阅晚于同步消费 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-032：全量 CI 依赖未纳管制品与本机文档 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-033：展示柜未知版本或读档失败后仍可覆盖原 key → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-034：出击餐登记失败 return 仍触发官方扣量 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-035：血月收尾清表前未结清最后轮询窗口的击杀 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`
- CR-2026-09-04-036：孵化资产延期成功后不再补记孵化统计 → `archive/CODE_REVIEW_FINDINGS_2026-09.md`


<!-- BEGIN UI CONSENSUS AUDIT FINDINGS 2026-09-24 -->

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

## 2026-09-19 Wiki 内容与公开部署复核（SAFE / OPERATIONAL）

来源：owner 要求全面核对 Wiki 与最新代码，随后授权修复并提交本地 commit。文案问题已按现有代码修正，公开部署仍待推送授权；验证证据见 `FIX_TRACKER.md` 同日 Wiki 小节。

| ID | 级别 / 分类 | 已确认问题与影响 | 状态与证据 |
| --- | --- | --- | --- |
| CR-2026-09-19-013 | P2 / SAFE | 中文消耗品、NPC 物品和护士页把安神滴剂写成负面 Buff 全清，并推荐解除幽灵女巫诅咒；实际 `CalmingDropsUsage` 要求 `NurseHealingService.HasDebuffs`，可治疗名单不含诅咒 ID 500043，仅有该状态时无法使用。 | Fixed：三组中英正文同步限定清除范围与使用条件，使用实际本地化名 Ghost Curse，生成页与构建后 HTML 已核对（L1/L2）。未扩展药品或治疗行为。 |
| CR-2026-09-19-014 | P2 / SAFE | `infobox.mts` 的护士服务中英文都列出复活、野战诊所，普通护士交互与治疗代码没有这些服务；正文背景中的诊所不是独立服务入口。 | Fixed：速查框改为恢复满血、清除可治疗的负面状态；正文同时修正 Lv.6 的 75 折笔误为 7.5 折。双语渲染与 HTTP 页面检查通过（L1/L2）。 |
| CR-2026-09-19-015 | P2 / OPERATIONAL | 公开 main 与成功 Pages 部署仍为 2026-09-08 的 4b1b5b68，天空岛双语/新装备页面 404，日报仍显示旧悬赏规则；本地 commit 不会更新公开网站。 | Open（待发布）：本地最新完整站点构建、237 页链接检查和 12 个关键页面 HTTP 检查通过；owner 本轮授权本地 commit，未授权 push，未发布。需推送经审核的提交后核对 Actions head_sha 和公开页面。 |

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


## 2026-09-26 丧尸模式 Boss、图鉴与完整链路复核

分类：COMPAT / WIRE+；修复完成，L1+L2，L3 待 owner。详见 [审查与实机清单](docs/reports/reviews/2026-09-26_丧尸模式Boss与完整链路审查.md)。沿同日现有编号 110 继续登记，不覆盖其他会话的天空岛记录。

| ID | 等级 / 分类 | 已确认根因与影响 | 修复 / 验证 |
| --- | --- | --- | --- |
| CR-2026-09-26-111 | P1 / COMPAT / WIRE+ | `ZombieModeWaveController.HandleZombieModeHealthHurt` 在 OnHurt 补血，官方已先扣血/OnDead，Boss/群盾/减伤拦不住致死；`ZombieModePollution.ApplyZombieModeEnemyHurtAffixes` 的精英防御另按原始 damageValue 补血。 | Fixed：`ZombieModeDamageRuntime` 经既有 Hurt 补丁在 finalDamage 计算后、生命钳制前消费防御，删除补血与二次消费。L2 完整官方 Hurt + 真实 Harmony、本机官方 DLL 两种注入顺序、精英与 Boss 致死/溢出/来源边界。 |
| CR-2026-09-26-112 | P2 / COMPAT | `ZombieModeBossController.TickZombieModeHunterState` 在预警前改角色位置，然后在玩家位置落伤害，实际是提前瞬移。 | Fixed：先预警，再复用 `ZombieModeSprinterDashRuntime` 强制移动，在实际落点结算一次。L2 暂停、恢复、掉帧、死亡/换局取消通过；真实墙体碰撞待 L3。 |
| CR-2026-09-26-113 | P2 / COMPAT | 五类 Boss 没有独立轮廓；普通变异外形路径排除 IsBoss，原来只有体型与飘字，不能满足明确区分需求。 | Fixed：独立 `ZombieModeBossVisuals` 五类轮廓/色系/技能脉冲/官方姓名，跟随 modelRoot，暂停与销毁清理。L1+L2 接线与反向守卫；审美、遮挡、帧耗待 owner 目检。 |

图鉴复核：五个 `zombie_boss_*` key、主角亲手击杀、杂兵过滤、实例去重、回基地 flush 与持久化链未发现新增确认缺陷；`ContentThirdReviewFixes`、`SaveFailureRecovery` 通过。未修改图鉴生产代码，不能将隔离回归写成实机重启后已保存。独立 Windows 正式构建通过，未部署实际游戏目录；并行改动应整合后另作正式交付。

## 2026-09-27 天空岛发版复审（Wiki 对账、可达性、居民、Boss 质感）

分类：COMPAT / SAFE；修复完成，L1+L2，L3 待 owner。详见本地报告 `docs/reports/sky-island/天空岛_发版复审_2026-09-27.md`。

| ID | 等级 / 分类 | 已确认根因与影响 | 修复 / 验证 |
| --- | --- | --- | --- |
| CR-2026-09-27-001 | P2 / COMPAT | `Assets/Data/DuckNpcs.json` 苇白、浮舟 `positiveTags` 写 `"Tools"`；官方 Tag 名是 `Tool`（官方本地化键 `Tag_Tool`，没有 `Tag_Tools`），`NPCGiftSystem.HasPositiveTag` 按 Tag 资产 `.name` 精确比对。苇白没有按 TypeID 的喜好兜底，于是「苇白喜欢工具」从未兑现、任何礼物都拿不到 +80；浮舟靠 500075/500076 兜底，工具类同样失效 | Fixed：两处改 `Tool`；新守卫 `DuckNpcGiftTagGuard`（永久 NPC 喜好标签必须是官方 Tag、每位至少一条能命中），反向验证：还原 `Tools` 后两条断言转红、按 SHA 还原转绿。Tag 资产名等于本地化键后缀是按 Weapon / Food / Helmat 的既有用法推断，实机送一件官方工具验证（清单 M-02） |
| CR-2026-09-27-002 | P2 / COMPAT | 天空岛全部头目 / 岛主招式没有音效（`DebugAndTools/SkyIsland` 下只有环境音与云蚋调 `PostCustomSFX`）；走 `ExplosionFxTypes.custom` 的镰扫、落石、换位、冲步、伏击、绊索落地无声；结算无光；换阶段与倒下只有字幕 | Fixed：`gen_sky_island_sfx.py` 新增 5 种程序化音效；`SkyIslandImpactFx` 新增 `SkyIslandBossSfx`、`Flash`、`PhaseBurst`、`DefeatBurst`；Forge / 四位多阶段 Boss / 噬风 / 断风 / 镜中客 / 匠首 / 具名对手接线。判定一字未动。新守卫 `SkyIslandBossFeedbackGuard`（9 个内置反向探针）。观感与帧耗时待实机 |
| CR-2026-09-27-003 | P3 / SAFE | Wiki「白天在岛上是无风的」与 `SkyIslandFieldcraftRules.WindLevel` 不符：桥与中继平台白天也有微风，双航标后噬风未散时白天同样大风 | Fixed：中英同步改写；`SkyIslandWikiParityGuard` PASS |
| CR-2026-09-27-004 | P3 / COMPAT | 目标卡「恢复两端航标」不提示要先清守卫，新玩家站在风标台前按了没反应才知道（09-10 可玩性评估 5.1） | Fixed：航标未清守卫时加「（先清守卫）」，判据 `WindBeaconGuardsCleared / StarLampGuardsCleared` 与 `TryApply` 的拒绝共用；SkyIslandStory 执行回归 PASS |

复核后不成立 / 未改：
- Wiki「口口口口」不是占位符，是官方 preset `EnemyPreset_Boss_Island_Koukou` 的原名。
- Wiki 噬风「预警约一秒半」对应 `PulseTelegraph = 1.4f`，带「约」字，判一致。
- 晴禾与另一位 NPC 的 `positiveTags` 含 `Consumable`（官方无此 Tag），各有 `Food` 或 TypeID 兜底、不影响可送礼；登记为 `DuckNpcGiftTagGuard.KNOWN_NOOP` 既有债务，未改行为。
- 内容可达性（18 件岛物 + 航向仪、17 件装备、11 配方、4 委托、4 谜题、12 信、20 见闻、名册、主线四任务与三处缺席兜底）未发现新的 P0/P1（L1）。

## 2026-09-27 丧尸模式 Boss 表现、官方计数与补丁隔离复审

分类：COMPAT / WIRE+；L1+L2 + Windows 正式编译，L3 待 owner。详见 [复审报告](docs/reports/reviews/2026-09-27_丧尸模式Boss表现与链路复审.md)。同日 001–004 已被其他会话占用，本专题从 101 起。

| ID | 等级 / 分类 | 已确认根因与影响 | 修复 / 验证 |
| --- | --- | --- | --- |
| CR-2026-09-27-101 | P2 / COMPAT | `ZombieModeBossVisuals` 的线宽与余烬按身高倍数给，但 LineRenderer 线宽不随 Transform 缩放、粒子 Local 缩放不继承父级：能量环 1.2 cm、脉冲 2.6 cm、余烬 2.5 cm，低于 0.06 m 下限（VB-08），实机基本不可见；发光缝藏在甲内。 | Fixed：世界米数线宽 / 余烬、暗甲包发光刃尖、五种脚下纹章、共享 PlayBurst 起手与死亡爆发、追猎冲刺拖尾。守卫钉线宽下限、Local 缩放与五个纹章分支。观感待 L3。 |
| CR-2026-09-27-102 | P2 / COMPAT | 09-26 显示副本改 `nameKey` 且销毁才还原；官方 `CharacterMainControl.OnDead` 按它写 `SavesCounter` 击杀计数，官方存档多出 `Count/Kills/BossRush_ZombieMode_Boss_*`，击杀 `Cname_Zombie` 的官方任务漏算 Boss。 | Fixed：`ZombieModeDamageRuntime.ReduceFinalDamage` 判定致死后、扣血前换回原 preset。执行回归新增 3 条断言（吸收不换 / 致死恰好一次 / 普通丧尸不碰）。已写入的旧键不清理（§10）。 |
| CR-2026-09-27-103 | P3 / COMPAT | 丧尸 Boss 血条无官方 Boss 图标（preset iconType=none），与其他 Mod Boss 不一致。 | Fixed：显示副本设 `CharacterIconTypes.boss`。守卫覆盖。 |
| CR-2026-09-27-104 | P2 / WIRE+ | 丧尸减伤 Transpiler 挂在共享 `BossRushHealthHurtContextPatch`；IL 失配抛出时逐类安装器跳过整类，所有模式同时失去 Mode G 屏障、逆鳞无敌与 Boss 致死钳制。 | Fixed：独立 `ZombieModeHealthHurtDamagePatch`；`ModeGSpawnTransactionGuard` 登记精确身份，展示守卫禁止回挂。官方 DLL 两种注入顺序回归通过。 |
| CR-2026-09-27-105 | P3 / COMPAT | `Projectile` 类 run-only 记录局内不清理，毒径 / 远程弹道整局累积并被多处线性遍历（性能影响推断）。 | Fixed：登记新 Projectile 记录时摘除已销毁的同类记录。 |
| CR-2026-09-27-106 | P3 / COMPAT | 腐蚀 Boss 死后 0.5 s 被销毁，死亡毒云与残留腐蚀区 `source` 为空，回退成玩家来源的效果伤害，死因显示「自己」。 | Fixed（同日第二轮）：丧尸 Boss 的 `Health.DeadDestroyDelay` 延到最长残留区（8 s）+ 1 s，尸体保持失活但有效；静态 OnDead（晚于写击杀计数的实例 OnDeadEvent）再挂回显示副本，结算页死因显示 Boss 名。玩家兜底来源不变（`ZombieModeAreaDamagePlayerGuard` 禁止空来源）。 |

线索（UNVERIFIED）：~~追猎狂暴无单次标记~~ owner 同日定为「狂暴就是一直」，已改为触发一次持续到死亡（见 FIX_TRACKER 同日第二轮）；丧尸 Boss 未设 `isBossCharacter`，日报 / 征程 Boss 计数可能不含丧尸 Boss。图鉴链路复核无新增缺陷。

## 2026-09-29 天空岛发布前生产级审查

- CR-2026-09-29-101～117（P2×4 / P3×13，COMPAT / SAFE / OPERATIONAL）：Fixed，L1 + L2、无 L3；逐条根因 / 修复 / 验证见本地报告 `docs/reports/sky-island/天空岛_发布前生产审查_2026-09-29.md` §1，修复流水见 `FIX_TRACKER.md` 同日节。切片 C「Mod 物品进岛上物资池」refuted。
- CR-2026-09-29-112（P3 / COMPAT）：头目首杀手记与字幕不看击杀者，断风被拾荒者打死也记首杀（`SkyIslandBossForge.RaiseDefeated`，消费方 `SkyIslandWorldStoryBosses`）。Fixed（owner 2026-09-29 定「只认主角击杀」）：`RaiseDefeated` 带致死一击，倒下回执照放，首杀回调按官方击杀计数口径 `fromCharacter.IsMainCharacter` 过滤；尸体箱与掉落照常；三张装备页 Wiki 中英改为「亲手打倒的首杀」。

## 2026-09-30 天空岛道路与阴影（COMPAT，模型专题）

已修复旧路线穿入登记模型、房前家具朝向及长椅背板错位、重复输出已替换地标、跨级联阴影坐标插值、共面反绕序贴图 / 深度争抢和 normal bias 不一致。采用连续重规划道路、实际包络摆放、单一结构 owner、片元级阴影坐标与配对面标记；未关闭正常阴影。证据和实机判据见 [本轮报告](docs/reports/sky-island/2026-09-30-天空岛摆放与阴影修复.md)。此记录只覆盖模型专题，不关闭上方整体生产审查的独立玩法问题。
