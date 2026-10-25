# FIX_TRACKER.md — 修复状态与兼容性流水账

更早的完整记录见 `archive/`；近期已闭环的大篇幅审计正文也按月份存档，当前文件保留索引与未闭环条目。

## 2026-10-25 F3 面板滚动、录制小键盘热键与野外趣事落点放宽（COMPAT；L1/L2，L3 待验）

- **F3 左侧导航溢出**：导航 VerticalLayoutGroup 不控子项高度，按钮的 LayoutElement 不生效、取新建 RectTransform 默认 100 高，8 个页签约 880 高撑出 682 高的导航区。导航与内容都改走 `F3DebugUiKit.CreateScrollList`（共享 `ConfigureScrollRect` 滚动条），导航控高；按钮行改为 `F3FlowRowLayout` 自动折行，旧行宽按内容撑开、以中心为轴向左右溢出被裁。
- **录制热键**：PgDn/PgUp/Home/Insert/End 之外同时认小键盘 3/9/7/0/1（键帽上印着同名导航键），提示条与页面说明同步。
- **怪件快递签收台常生成失败**：接单后签收台只认 28–45 米、12 个候选一轮，拥挤地图常提示「附近没有合适的签收落点」。新增 `FieldAdventureSupport.TryFindGroundBands` 由远到近放宽（28–45→20–36→14–28 米，再以玩家为圆心重找），判据不变；发件台失败不再走 ClearWorld（原先清掉场景号与录制标记，重试时改挂远处阿稳）；录制模式快递一项当场重开并在失败时显示「未触发」。
- **同类问题**：宝箱怪（20–36→12–24 米）、试验台（14–30→8–18 米）原先固定尝试次数全落空即整趟不出现，同样放宽；录制刷靶子一个都没放下时报错，花冠落点放宽。
- 验证：全量守卫 752 PASS；Dev 与正式构建均编译通过，正式 DLL `D8157889…0022A` 已部署且与 `Build/` 一致；FieldAdventure 六组执行回归通过，快递 19 项反向探针照常转红。夹具替身补 `TryFindGroundBands`。未进游戏，面板观感与实际落点成功率待 L3。

## 2026-10-25 全模块边界审查与修复（COMPAT / SAFE；L1/L2，L3 待验）

修复 `CR-2026-10-25-001`～`010`：共享 store 的跨槽待写与首次写入屏障；危险试用品委托迟到提交；Mode D 原玩家归属、两模式加载开始时旧生成失效及第 0 波误结算；霜之哀伤慢创建；成就面板在宿主加载早返后的独立关闭；Boss 非法倍率、NaN 倒计时与 JSON Int64 上边界。每项触发条件、位置、证据、覆盖范围及 6 个手工实机步骤见 `CODE_REVIEW_FINDINGS.md` 同日记录。另补 `architecture/modules.json` 中既有 PromoRecording 两份源码的归属登记（SAFE）。

验证结果：

- 首轮守卫 751 PASS / 1 FAIL；补模块登记后最终全量 **752 PASS / 0 FAIL / 0 KNOWN-RED**，日志 `Build/audit-20261025-guards-final.log`。
- 原有全量执行回归首轮 142 PASS / 2 环境失败；为 AuditCombatSeptember 和 SkyIslandCombatRuntime 显式设置隔离 Managed 与安装的 Harmony 路径后两组通过，原有 **144 组**均已有本轮通过证据。原生 Unity FieldAdventureVisuals 及其反向探针通过；这是隔离编辑器证据，不是实际游戏 L3。
- 正式构建完成后，对修改及共享存档/解析消费者统一重跑 **24 PASS / 0 FAIL**，包含新增 PrototypeCommissionLifecycle（67 条）、扩展 ManualEquipmentRecovery（72 条）和 SaveFailureRecovery（401 条）。结果清单 `Build/audit-20261025/affected-results.json`，聚合日志 `Build/audit-20261025-runtime-final-affected.log`。
- 新边界先红后绿：JSON 越界、零倍率、NaN 倒计时、切槽通知中断、首次写入未绑槽、Mode D 死亡/加载/第 0 波分别执行旧生产失败；委托三项与霜之哀伤一项隔离反向探针验证通过，原生产文件哈希保持。
- Windows C# 7.3 正式编译通过（已移除 BOSSRUSH_DEV_BUILD），引用本机游戏 Managed 的真实拷贝，日志 `Build/audit-20261025-compile-official.log`。两条 CS0649 是既有 Mode H 兼容页面字段；现有 PrematchDeclutter 守卫要求不启用，未当作 bug 改动。隔离目录未设置 Mods 目标，脚本报告自动复制失败；编译产物已独立保存在 `Build/audit-20261025/BossRush.official.dll`，SHA-256 为 `66567F1C3CAF9C4502F55A5863BA2E4E1676B9037529E6599C64580BD2C26BDE`。没有向实际游戏目录部署。
- `git diff --check` 与模块预算守卫通过；未新增生产源码、TypeID、持久化 key 或 schema。对应 repowiki 与回归 README 已同步，无公开/私有仓库推送或提交。

性能范围：新增检查围绕实际写入、活动召唤、活动对话或已开面板执行；没有新增全局扫描、资源预热或修改玩法数量。未采样游戏帧耗时，不能作为性能改善的实测证据。本轮未启动游戏或访问玩家存档；真实加载、控制恢复、召唤画面和存档切换仍待按验收清单完成 L3。

## 2026-10-24 第三轮：新敌人更耐打、随机遇敌、叮当委托与五种试用品（COMPAT / SCHEMA+；L1/L2，L3 待验）

owner 实测「太容易被打倒、进图必刷」并要求叮当试用品先接委托再随机出现、种类更多更夸张。蚀日花冠：生命 7500、花瓣 7.5%，花瓣护核每片 18%（四瓣 28%），引导进行中必刷、完成后每趟 37 号 30%。贪吃宝箱怪：生命 900+300/口，闭嘴铁皮 ×0.45、张嘴全额，每趟 35% 随机出现、时间与距离随机。减伤统一走新增 `Utilities/ReceiverDamageScaling.cs`，挂在既有 `DamageReceiver.Hurt` 前缀上（不新开补丁，参数改 ref），卸载路径兜底清空。叮当委托 `DangerousPrototypeCommission`：好感 10 级在基地叮当组内挂选项，接下即剧情、对话完直接完成，新存槽键 `BossRush_DangerousPrototype_Commission_v1`（SCHEMA+，不接官方 Quest）。试验台改为委托完成后每趟 45% 随机出现、随机时刻、14–30 米并标地图，每台只放一件；试用品扩为五种（倒鸭一号·超载、折射太阳·暴走数值加强，新增嘎嘎轨道炮、雷霆鸭嘴、压轴大烟花），各有招牌部件与专属特效。夹具与守卫同步：花冠规则 / 运行时（护核 28%→64%、每趟只掷一次、任务目标必刷）、宝箱规则 / 运行时（新生命、铁皮倍率、撤销登记）、试用品规则（五种数值、随机分布、非法 id）、原型运行时分发替身。全量守卫 752 PASS；全量执行回归 144 组通过（AudioRuntime 首轮偶发失败、单独重跑通过；FieldAdventureVisuals 因测试工程 Library 缓存指向旧反向验证路径而中断，清缓存后重跑通过，宝箱受击 575 万项全命中）；试用品、花冠规则与残日盾片反向探针通过。正式构建 DLL `7E55D7DBE22A1A1173FAD66052674BE10F4C1F078A49E431710898236E5B4F7E` 已部署。未提交。

## 2026-10-24 提交前全量核对与残日盾片图标重做（COMPAT / SAFE；L1/L2）

残日盾片 UI 图标原为灰色哑光渲染、与游戏内深色模型不符，按星阙图标标准以游戏内模型预览为参考重出（gpt-image-2.5-flare 图像编辑，透明底 512），作者源旧图存 `ArtSource/EclipseBloom/Previews/`，经作者工程 `EclipseBloomBundleBuilder` 重打 `residual_sun_shield_model`，`EclipseBloomAuthoredBundleGuard` 通过、作者台账 50 项更新并核验。全量核对修掉 5 处此前遗留：`ZombieModeRuntimeModule.cs` 超 1200 行，入场等待两方法原样拆到 `ZombieModeRuntimeModule_TargetSceneEntry.cs`（4 个守卫与 ZombieModeHostOwners 夹具改读两文件，已反向验证）；冠军之影入池新增一处宿主身份校验，`ModBehaviourInstanceClassificationGuard` 基线与分类文档补登 271 / 414；ArenaHostRemainder、AuditModeLifecycle 夹具补入池适配器替身；怪件快递表现拆出 `PlayBurstFx` / `PlayShoutRing` 以兼容 FieldAdventureDeliveryRuntime 抽取。结果：全量守卫 752 PASS；全量执行回归 144 PASS（设 GAME_PATH / BOSSRUSH_GAME_MANAGED / BOSSRUSH_HARMONY_DLL）；正式构建 DLL `DC3CE0BE673E26535FDC8001F5BCC8A28C16DD76C144B65C0468E60CC38F0F5A` 与盾片包均已部署并核对 SHA。L3 仍待实机。

## 2026-10-24 蚀日花冠 37 号目标圈不刷新（COMPAT；L1/L2，L3 待验）

owner 实测进 37 号目标圈看不到花冠；Player.log 中该趟出击没有任何花冠生成记录（同日 BossRush 池抽到花冠时有）。根因判断：`EclipseBloomEncounterPlacement` 只有圈心 3 m 内 5 个候选，且要求 3.8 / 8 / 14 m 三圈 48 个采样全部可走、无墙、高差 ≤0.7 m，军事基地 14 m 内必有障碍，五点恒被否、每 2 秒重试一轮永不生成（未在游戏内复现，属推断）。改为目标圈内黄金角螺旋 24 个候选（≤18 m）、分圈门槛（12/12、12/16、8/16），一轮失败后放宽为 10/9/5 并记 Dev 日志。EclipseBloomRuntime 新增候选分布用例，`EclipseBloomIntegrationGuard` 通过。

## 2026-10-24 新敌人难度重做与表现质感提升（COMPAT；L1/L2，L3 待验）

owner 实测意见「新增敌怪没有难度、人机感重、特效塑料感」。蚀日花冠：预警前 0.45 秒扇区转向玩家后锁定（`EclipseBloomRules.Track` / `AttackSerial`），休整追击 6 m 起、12 m 外全速，离花冠 9.5 m 外每次新预警按阶段落 1/2/3 道预判日珥（新文件 `EclipseBloomSolarFlares.cs`），第三阶段贴身也隔轮落一道；核心 / 花瓣受击反馈、出伤日焰喷发、预警末段明灭与定位音效。残日盾片表现重做为开合缓动的四瓣日轮 + 120° 光弧 + 绕身日珠，撤驱动先收拢淡出。贪吃宝箱怪：远距扑跳（`GreedyMimicTerrain.cs` 校验落点）、9 m 内锁线舌击、隔墙两轮跳过掩体、真实飞行弹幕与二波重瞄、炸弹预判、冷却随血量 2.2→1.15 s、残血落地接舌击；预警圈倒计时烧满、柔光弹道线、朝镜头血条带残影、FxKit 粒子替换实心几何火花、战败鼓胀后崩开。叮当试用品：双层柔边光束、开火 / 撞墙 / 命中敌人粒子、蓄满闪光与提示音、折射太阳反弹瞄准线；怪件快递签收彩纸、汽水泡沫、巨蛋羽毛与柔光台环。

同步：花冠规则 / 运行时、宝箱运行时、盾片夹具替身与断言按新行为改写（不放宽旧断言，旧「45 米咬击」改为舌击 / 扑跳 / 飞行弹幕三组）；残日盾片反向探针两处锚点改指 `ExplorationRuntimeRegistration.cs`（此前已失配）。`FieldAdventureIntegrationGuard` 的奖励位置断言随「宝箱会移动」改为战败时实际位置（扑跳中先落回落点），已做反向验证。EclipseBloomParticles 真实 Unity 回归通过。验证：Windows 正式构建通过并部署，DLL `FE95C66F1828E874BDB33F772B337EBE7A15091D7DF34A8BA6AF3F83DEC69240`；EclipseBloomRules / EclipseBloomRuntime / ResidualSunShield / FieldAdventureRules / FieldAdventurePrototypeRuntime / FieldAdventureMimicRuntime 回归与三组反向探针通过；冠军之影 / 花冠入池的两条生成委托从宿主 `EnemySpawnHostBridge` 移到新文件 `Utilities/BossPoolSpawnBindings.cs`，宿主只剩一行转发，`ModBehaviourPartialBudgetGuard` 回到 19947 行转绿（`EclipseBloomIntegrationGuard`、`SandstormChampionPoolGuard` 同步改指并反向验证）；changed-only 532 项守卫全绿，EnemySpawnRuntime / SandstormChampion / EclipseBloomRuntime 回归通过；Wiki 站点构建通过。未提交、未启动游戏。

## 2026-10-24 冠军之影入池与龙息丧尸兼容（COMPAT / WIRE+；L1/L2，L3 待验）

冠军之影新增 `SandstormChampionPoolAdapter`，接入标准 BossRush、无间炼狱、Mode D 与既有随机乱入，沿用前期强 Boss 保护、原工厂、每实例技能与星阙掉落。E/F/G/H、遗种巢和丧尸流程显式拒绝；未添加丧尸专属 Boss，既有运行时 preset clone 过滤保留。波次租约仅清理自身，非波次不登记，池内击杀不推进征程章节。交叉复审补齐 defer 的真实时序：额外等待及有效性检查在控制器初始化/激活前完成。

CR-2026-10-24-001 已修：龙息缺失对丧尸系数导致直接命中伤害为 0，新旧实例补值 1 并使官方哈希缓存失效；已有有效系数、重铸和磨损保留。丧尸开局近战/枪械仅影响起装、前期保底强化与重抽倾向，未发现整局武器锁定；两类武器奖励都保留。玩家无法说明具体“无法使用”表现，剩余开火/装弹现场仍须实机确认。

三位子代理均 gpt-6.1-sol / xhigh，独立复审已消除缓存和 defer 两项实现缺口。冠军池工厂 57 条、龙息 42 条执行断言通过；冠军相关 10 组中央回归通过，双方各 5 项隔离负向探针准确转红并保持共享源码 SHA。两个仅含本轮增量的功能候选均通过 Windows 正式隔离构建，75 个资源包 SHA 校验通过，无 Dev 定义，仅两条既有 ModeH CS0649；最终 DLL SHA-256 `5A86F3951412B53AA6988373B9CD9CE60492B3D388370FFDA9A330C6442B4828`。证据、守卫与文档检查结果及复审报告在 `Build/champion-dragonbreath-20261024/`。

按用户要求拆功能本地提交；保留其他任务改动，未推送、真实部署、启动游戏或访问存档。L3 待验证：普通/无限/Mode D 的冠军抽取与多实例掉落、死亡/切图清理；BR 弹药开火/装弹及新旧龙息对丧尸伤害。相关中英 Wiki、生成页面及专题 repowiki 已同步。

## 2026-10-23 CR-008 补充：吞咽动画受击外沿修复（COMPAT；Fixed at L1/L2，L3 待验）

CR-2026-10-23-008 的动画外沿遗漏已补齐。`GreedyMimicVisual.cs:66、165、207` 缓存主箱身和两块箱盖网格包络，每帧变换 24 个角点；`GreedyMimicEncounter.cs:69、156、194` 在成长回调及动画后更新唯一 BoxCollider，通过 kinematic Rigidbody 发布姿态，保留胸口基点与一次成长缩放。吞咽正负相、呼吸、跳动与开盖均覆盖；不逐帧重建碰撞体或全局 SyncTransforms。官方爆炸和近战不为 simpleHealth 多 Collider 去重，因此保持单盒。

真实 Unity 原生基线 5,754,526 项检查通过，486,373 次实际主网格射线、1,459,119 次球扫全部命中，吞咽外沿漏判为 0，四次官方爆炸均只扣一次血；成长当刻、回血打断、收缩拒收、暂停和销毁均覆盖。六项原生变异、15 项结构变异、独立旧盒变异及原有两项伤害变异共 24 项反向验证均实跑预期转红并恢复字节/SHA。独立复审另有 129,702 项检查通过，旧盒 875 条射线漏判准确转红；三位子代理均 gpt-6.1-sol / xhigh，未发现最终候选新增 confirmed issue。

533 项相关守卫、六组 FieldAdventure 中央回归与共享迷雾回归通过；Windows 正式隔离构建通过，DLL SHA-256 `B1DD5331ABDEBD4E2C1F172EE2A658631544CC93826F0288625F8648082B65A2`，14 个 Dev 标识缺席、77 个资源包 SHA 校验通过，仅两条既有 ModeH CS0649。真实游戏 DLL SHA 未变，其他任务改动保留；未提交、真实部署、启动游戏或访问存档。单盒为当前主箱体的保守包络，包含开口空隙，曲面边缘不逐三角形贴合；L3 与实际帧耗仍待验。完整证据与人工清单见 [吞咽受击修复报告](Build/mimic-swallow-collision-fix-20261023/修复与验证.md)。

## 2026-10-23 CR-008 / 009 修复（COMPAT；Fixed at L1/L2，L3 待验）

008：宝箱受击体与模型共用单位缩放 Facing，稳定跟随同一 yaw，成长不叠乘；原生 456 条断言、64 个正常边缘组合全部命中，恢复旧 Root 后 16 个横向组合准确转红。009：阿稳快递子项按同一可用判据摘挂，刷新官方组内容并保留其它选中对象/事件；同代对话收尾恢复，清理先退组再销毁，池复用、隐藏/重开菜单与旧对话跨代返回均覆盖。3300 条永久快递断言通过，19 项隔离负探针实跑转红并按字节恢复。两项回填 Fixed at L1/L2；009 的局部官方 HUD 兼容调用为 WIRE+，无需整体替换反射策略。

最终 533 项相关守卫、6 组 FieldAdventure 中央回归、共享迷雾回归、Windows 正式隔离构建通过；结构守卫 7 项、原生受击体 1 项、独立交叉探针 2 项及原有宝箱伤害 2 项反向验证也通过。三个 gpt-6.1-sol / xhigh 子代理完成修复与交叉复审，独立官方交互探针 77 条通过，未发现新增 confirmed issue。DLL SHA-256 `458722F995D2B9B7BBE3239591754D0150ADC900411453062DEC8C1330C46EA5`，14 个 Dev 标识缺席，77 个资源包 SHA 校验通过，两条既有 ModeH CS0649。

未提交、真实部署、启动游戏或访问存档；构建仅写本轮 Managed 实体拷贝对应的隔离目录，真实游戏 DLL SHA 未变。其它任务改动保留，专题 repowiki 与夹具边界已同步。该轮 L3 与性能未实测；吞咽动画外沿现已补充修复，见上节。完整证据、精确位置与验收步骤见 [修复与验证](Build/field-adventures-fixes-008-009-20261023/修复与验证.md)。

## 2026-10-23 野外趣事初始独立审核（SAFE；CR-008、009 后续修复见上节）

独立审查当前生产、官方源码与资源，确认两个 P2 / COMPAT / L1+L2：CR-2026-10-23-008 为宝箱 Body 转向而 Root 下矩形受击盒不转，真实 Unity 16 个主网格命中组合中横向 8 个受击盒射线与球扫漏判；CR-2026-10-23-009 为快递已有订单/两单耗尽后阿稳组内保留无响应选项，完整官方 HUD 与动作链实际复现。初审时两项均 Open，后续已 Fixed at L1/L2（见上节）；本段为初审记录，未修生产；002～007 经重新核实维持 Fixed at L1/L2，均尚无 L3。

533 项 changed-only 守卫、6 组 FieldAdventure 回归、共享迷雾回归、Windows 正式隔离构建通过；新增宝箱 42 条装配/生命周期与快递 3048 条断言包含缺陷特征复现，不能据此写成缺陷已修。试用品真实 PhysX 40 个安全前置组合通过；8 个胸口在墙内/表面的构造保留 UNVERIFIED，待实机可达性确认。未重跑旧全部 20 项负探针或全量守卫；没有性能实测。

DLL SHA-256 `E71A3440FC59E83D320EE4EDF088B69E42C0FDA6ADA042D299A16887E7007223`；构建只部署到 Managed 实体拷贝对应的隔离目录。没有启动游戏、访问存档、真实部署、提交或推送。全部证据、精确行号、修复建议与 FA-A01～A08 验收在 [独立审核报告](Build/field-adventures-independent-audit-20261023/审核报告.md)，范围与哈希见同目录 `validation.json`；已有台账正文和其他任务改动保留。

## 2026-10-23 野外趣事六项修复与交叉复审（COMPAT；CR-002～007 Fixed at L1/L2，L3 待验）

三个 GPT-6.1 Sol / xhigh 子代理完成分模块修复与交叉复审，主审复核共享链，最终未发现新增 confirmed issue。宝箱按实际暴击正伤害中断回血，长舌十二段覆盖完整锁定落点；快递统一官方朝向、携带球心兼容移动/转身帧序，刷新官方已绑定文字行且保留选择；叫声同步主/音效音量与 Mute，暂停菜单同样生效；试用品沿官方近距/Moveability 门计算射向与后坐，贴墙起点沿已检查线段退回。细节、当前锚点与证据边界见 `CODE_REVIEW_FINDINGS.md` 同日节。

中央 FieldAdventure 六组回归全过，包含规则/库存 1376、场景门 43、宝箱 199、快递 2993、试用品 2316 条断言和真实 Unity 六组 Deferred 视觉；20 项新隔离负探针均按预期转红并恢复 SHA。Windows 正式隔离编译通过（无 Dev，两条既有 ModeH 警告），DLL SHA `A38565E7B6B3E1848EAE4812CAFAB478D1C89714E568D010C32B3A3D7E02ABF8`。全量 751 守卫初跑两红，台账已原字节归档并复测通过，唯一保留的是既有丧尸文件 1239 > 1200；最终 533 项 changed-only 守卫全部通过，未抬预算或加白名单。

专题 repowiki、官方 API 陷阱与夹具边界已同步。范围/哈希/日志/人工验收在 `Build/field-adventures-fixes-20261023/`；没有部署、启动游戏、访问玩家存档、提交或推送。L3 待验收步骤 FA-01～FA-06 见其中 `验收与复审.md`，尚未采样实际性能。

## 2026-10-23 蚀日花冠粒子报错与独立音乐（COMPAT）

CR-2026-10-23-001 已修复：余烬线性速度三轴统一 TwoConstants，零横向速度及原 y 漂移/轨道效果不变。玩家原日志 239 次刷屏；真实 Unity 引擎基线 94 次模拟零错误，隔离副本恢复旧写法重现 112 次同类错误并准确转红，生产 SHA 保持。新增 `EclipseBloomParticles` 中央回归，覆盖三阶段、逐瓣破碎、死亡爆发和销毁。正式构建通过，14 个 Dev 标识缺席。

原创专曲《黑日花冠》约 131 秒，沿现有 owner 租约接入，替换幽灵女巫曲目引用；20,005 条运行时断言、7 项音乐反向验证、12 项音频检查和真实 FMOD 无声解码通过。正式 DLL、曲目表和新 WAV 已更新本机 Mod 并逐份核对 SHA，原文件已备份。作者源、部署和复测证据见 [本轮报告](docs/reports/testing/蚀日花冠粒子报错与独立配乐-2026-10-23.md)。未启动游戏，修复后的 L3 游戏复测待确认。

## 2026-10-22 蚀日花冠官方地图、Boss 池与作者资源（COMPAT / SCHEMA+ / WIRE+）

落实 Jeff 590215 官方任务投影、普通 37 号实验区固定区域自然刷新、死亡自动放弃/可重新接取、每出击一次与跨槽/异步隔离。标准/无限、Mode D 和兼容事件接入 Boss 池；E/F/G 与遗种巢显式过滤。移除独立竞技场花苞入口。

闭环 CR-2026-10-22-003～009：完整盾片模型/图标/拾取代理，核心拓扑、贴地预警、可见性判伤、实际死亡掉落、死亡待写入保存门及 SpawnCore 倍率保留。正式建模走用户指定 Blender MCP，细节纹理走 docs API，既有 Unity 作者工程定向打包；两包注册发布清单，模型与图标由明确 owner 借用/清理。额外防止并发花冠临时 preset 成为另一实例的官方身份。

Windows 正式隔离构建通过（1245 源，Dev 标识缺席），DLL SHA 4D08FE728F43B8ADE23310DBCAC3E6E8A2DEAB9041648948676BEFD561DD5BEF。全量 750 守卫 749 PASS，唯一红项为既有 ZombieModeRuntimeModule 1239 > 1200；未调整预算。战斗19组、引导11例、资产76断言、SpawnCore94断言及规则/盾片回归通过；108 个隔离负向准确转红，原生产 SHA 未变。Wiki 构建、80 导航、265 页 / 46767 引用全绿。

[详细报告](docs/reports/testing/蚀日花冠官方地图与资源审核-2026-10-22.md) 保存资源/DLL SHA、证据路径和边界；[人工验收](docs/reports/testing/蚀日花冠与残日盾片实机验收-2026-10-22.md) 仍全部 pending。未部署、启动游戏、读写存档或公开提交/推送。

## 2026-10-22 Demo 丧尸入场与旧传送防护（COMPAT，CR-001/002 Fixed at L1/L2；未部署）

玩家反馈 Demo 第 17 波同图回到开局点，暂无现场日志。官方真实 StartPoints 为 `(460.66,-5,106.31)`；本机资源与当前 DLL 未找到按周期、波次或越界自动回起点规则。官方 InitLevel 正常尾部在 LevelInited 后约 0.25 秒写起点，输入 / 移动也受 LevelInited 门控，尚不能解释本次现场。正常 Demo 不生成旧 custom 请求，旧 custom 坐标也不自然等于官方初始点；反馈因果仍 UNVERIFIED。

CR-2026-10-22-001：custom 使用既有 IntegrationRuntimeModule 的请求 / 递归协程驱动，绑定 sceneLoaded 的目标 Scene，在官方 loader、MultiSceneCore、LevelInited 与 AfterInit 就绪后只绑定一次主角。允许 Main 到目标图的正常激活过渡；新普通 / custom 入场相互取消，切图、卸载、死亡、角色替换、owner 销毁及 H / 丧尸接管均取消。位置写入前复核，30 秒预算包含 loading，超时不放行；GroundZero 后继、持续清场与 Mode E 验证也登记同一请求。没有调整玩法、落点、存档或编译预算。

CR-2026-10-22-002：入场序号与原 owner、RunState、事务及 RunId 绑定等待，循环内及最终复核前后检查仍属于本次 LoadingMap 入场。最后一次 yield 后重新读取全部官方条件，并匹配目标场景 handle；未 ready 沿原失败与邀请函 / 现金退款门处理，LoadingMap 失败保持原不主动返基地协议。旧等待直接结束，不能清掉后继标记、创建新 RunId 或退款新现金 / 邀请函；未扩大 CR-2026-10-21-009 / 010 的退款故障边界。

验证：461 项 changed-only 守卫、153 项 ZombieMode 守卫全绿；8 组 ZombieMode 回归加 ModeHSceneEntry、LegacyArenaEntry、EntryAndReforgeCompatibility 共 11 组全绿。最新 ModeHSceneEntry 152 条、LegacyArenaEntry 53 条；HostOwners 原所有权 1,166 条，新目标就绪 12 组场景 4,872 条（包含逐帧 wait 检查）。副本中 4 个 CR-001 守卫变异、3 个执行变异及 5 个同时命中 CR-002 守卫 / 执行的变异均按预期转红，逐字还原与 SHA-256 核对后复跑转绿。报告分别为 `Build/custom-teleport-fix-negatives-20261022/report.json`、`Build/reviews/2026-10-22-zombie-demo-teleport/cr002-fix-evidence.json`。

Windows 正式构建采用真实 Managed 的物理拷贝作为 GAME_PATH，1220 个正式源码输入在编译前后哈希一致，未定义 BOSSRUSH_DEV。产物 `Build/teleport-safety-20261022/BossRush.dll`，SHA-256 `660449400e11f70912ecc379ddf60317bdbdd2006994f378e1efc00c9613df7b`；编译 0 错误、保留 2 条既有 Mode H CS0649 警告。隔离目标无 Mods 目录，构建尾部自动复制未成功且跳过资源部署；真实游戏 DLL 前后均为 `DDCB2E41BE74F5B14610611F7F8038E04A2140A9EB5AE83F3C5280B510B007ED`。未部署、启动游戏、访问存档或提交；编译记录与无部署证据见 `Build/teleport-safety-20261022/compile-result.json`。

L3 待 owner 后续授权部署后验收，记录文件 `Build/teleport-safety-20261022/runtime-smoke.md`，本轮未执行：

- T1：按当前绑定的交互键打开丧尸入口，选择 Demo 挑战，完成开局选项后用移动键离开出生区域，连续推进至第 17 波前后；观察玩家位置和波次 HUD。未操作传送仍突然回开局点、波次重置或初始化重复均不合格。
- T2：通过地图选择进入配置 custom 落点的普通地图，待加载结束后按移动键离开落点；再按正常入口流程验证 Mode E / H。观察出生落点、路牌 / 模式 UI 和角色控制；错误落点、重复搬动、正常入场超时或入口缺失均不合格。
- T3：在普通 custom 入场与 Mode E 验证等待阶段按已绑定的暂停 / 退出键结束该次流程，再从入口进入丧尸 Demo；观察新局位置、邀请函 / 现金和模式 UI。旧坐标写入、新局退款 / 标记被清除、普通路牌或 NPC 初始化延迟串入新局均不合格。若正常操作无法命中等待窗口，保留未覆盖，不能把 L2 取消用例当作实机通过。

## 2026-10-21 v2.3.7 发版准备与游戏目录部署（OPERATIONAL / SAFE，已部署、未发布）

owner 明确授权直接替换修复 DLL，并整理教训、双语 Wiki 与工坊描述。`info.ini` 从 2.3.6 升至 2.3.7；正式产物 `Build/entity-fog-20261021/BossRush.dll` 已复制至 `D:/software/steam/steamapps/common/Escape from Duckov/Duckov_Data/Mods/BossRush/BossRush.dll`，目标 SHA-256 与源均为 `DDCB2E41BE74F5B14610611F7F8038E04A2140A9EB5AE83F3C5280B510B007ED`。旧 DLL 与旧 info.ini 已备份到 `Build/release-v2.3.7-20261021/pre-deploy/`；版本元数据也已部署并核对哈希，74 个资源包通过 `Deploy-ResourceBundles.ps1 -VerifyOnly` 校验。游戏目录 WikiContent 共 255 项源文件逐一比对通过，其中更新 11 项；被覆盖的旧文件同样备份。部署与资源摘要在同一证据目录。

此部署包含本页下方 NPC 姓名、Boss / NPC 迷雾及丧尸专项修复，不把历史条目的“未部署”视为当前状态。知识库、开发规则与官方 API 契约补录接线教训；中英版本日志、README 与工坊源描述按待发布口径整理。没有启动游戏、访问玩家存档、向公开仓库提交 / 推送或上传创意工坊；L3 仍按下方 N1–N3、F1–F3 与 Z1–Z4 验收，尤其不将 NaN 传播防护写成已找到第 13 / 23 波首次异常来源。

发版材料验证：changed-only 守卫 458/458，Wiki 专项 15/15，站点构建、80 项导航与 261 页 / 45,546 引用链接检查通过。工坊中文 `../a.md` 与英文 `../b.md` 有效预算分别为 7005 / 7700、7588 / 7700 字节；中英日志、BBCode 与版本链接已校对。本地私有文档及 info.ini 经既有授权的 private-sync 同步完成（私有提交 `ee7a77b`）；公开代码 / Wiki 未提交。材料摘要、校验与同步日志保存在 `Build/release-v2.3.7-20261021/`。

同日文案修订：owner 即将发布，要求沿用前几个版本的语气。中英版本日志、总览、README、local-only 发布说明与工坊文本去掉准备状态及验收过程，改写为玩家遇到的问题；技术教训和真实验证边界保留在开发规则与台账。工坊最终预算为中文 6874 / 7700、英文 7542 / 7700 字节；本条记录文案调整，不表示已经执行公开上传。

## 2026-10-21 自定义 Boss / NPC 迷雾可见性（COMPAT / WIRE+，L1/L2，未部署、未实机）

CR-2026-10-21-011：实际模型包核对证实，龙王 / 龙裔装备及镰刀 / 戟缺 CharacterSubVisuals；原版持有 / 装备接口不补组件，只有枪械 BuildAgent 自带补齐。EquipmentFactory 在完整模型阶段为缺失者补官方组件，让 SetParent 自动登记；两把近战完成渲染准备后同步当前角色隐藏层。女巫 RestoreVisibleState 不再强制 Show，避免观察者为 0 时清除官方 Hidden 后无后继隐藏边沿。

三位独立 NPC 没有 FOW 组件且固定 Default 层；捏脸 NPC 保留友好阵营时又受官方友军显隐豁免。共享 ModEntityFogVisibility 直接接官方 FOW hider 事件，已有 source 复用，缺 source 才配置 inactive 三点 probe；只隐藏缓存 Renderer 与光源，不停用 AI / Collider / 服务，保留原 forceRenderingOff、light mask 与技能 enabled。沙暴 Body 在构建完成后复用角色 hider。订阅幂等，停用 / 销毁退订，主玩家死亡与非 Raid 保持可见。仅隐藏时遍历缓存，可见时 O(1)，没有逐帧树扫描；性能仅为静态预期。

验证：changed-only 守卫 260/260 通过；新增实链夹具 38 条断言通过，覆盖观察者、永久揭示、首帧、死亡、生命周期、技能显隐状态、光源恢复、官方子视觉与近战末尾同步。EquipmentBootstrapOwners、EquipmentConfiguratorRegistry、ResourceProduction、GoblinRuntimeModule、IntegrationLeafOwners 相关回归通过。19 个隔离守卫变异及其中 15 个执行变异均命中指定失败，逐字还原并核对 SHA-256 后转绿，报告 `Build/fog-visibility-negative-f4fugmw8/negative-results.json`。

Windows 正式隔离编译退出码 0，2 条既有 ModeH CS0649，14 个 Dev 标识均不在产物中；未写入真实游戏目录。DLL、构建日志、源摘要与验证副本在 `Build/entity-fog-20261021/`；`BossRush.dll` SHA-256 为 `DDCB2E41BE74F5B14610611F7F8038E04A2140A9EB5AE83F3C5280B510B007ED`，交付前 12 项生产源码 / 构建脚本哈希与编译时一致。包含此前姓名修复，不代表真实迷雾几何、URP 与帧耗已验收。

实机 F1：普通出击图背对 / 走出视野并隔墙观察阿稳、叮当、羽织与捏脸 NPC，应隐藏模型，返回视野恢复服务。F2：龙王 / 龙裔 / 女巫在视野外不露装备、近战和本体；女巫结束隐身、切武器后仍守视野，进入视野后其技能自身隐身继续有效。F3：沙暴冠军与召唤物离开视野后沙云 / 眼光不透雾，回到视野恢复；基地、主玩家死亡、切图往返不留隐藏对象或事件。未启动游戏或读写玩家存档。

## 2026-10-21 丧尸模式专项审查与修复（COMPAT，L1/L2，未部署）

本轮修复五项新发现与一项既有问题：CR-2026-10-21-004 撤离工厂接受结算回执后才返程；005 现金到账先提交点数、异常时核对官方余额，通知故障不重复发钱；006 开波回收已销毁星点登记，即使实体掉落列表为空也执行；007 三种预警自然过期补结算，避免低帧率漏触发；008 骚扰弹按帧轨迹检测并停在原目标点；CR-2026-10-20-001 三叉/散射移除二次削伤，保留选卡代价。Mode F 未提供新回执时沿用既有工厂行为，新增字段为可选、无存档格式变化。

验证：全量守卫最终 747/747 通过；相关生产执行回归 15/15 通过，覆盖 ZombieMode 八项、EnemyRecoveryRuntime、IntegrationThirdReviewFixes、SpawnPositionPolicy、EnemySpawnRuntime、ModeEFEnemySpawnRuntime、AuditModeLifecycle、RuntimeOwnership。新增或修改守卫与关键行为的隔离反向验证命中预期失败，按字节恢复后通过；报告分别在 `Build/zombie-extraction-negative-probes.json`、`Build/runtime-regressions/ZombieModeRewardRuntime/spread-negative-fz8x8w66/`、`Build/zombie-audit-20261021/star-cleanup-probes/`，预警/弹体修复前失败日志在 `Build/zombie-threat-review-20261021/`。

Windows 正式构建退出码 0，无 Dev，2 条既有 ModeH CS0649 警告；使用游戏 Managed 实体拷贝，编译期间 1,220 项源码/脚本哈希未变，真实游戏 DLL 哈希未变。独立保存产物 `Build/zombie-audit-20261021/BossRush.dll`，SHA-256 `210DC19AEBAE4CCD10431F7365C56E9F209106E086461A883F4D65ED0B2C70BA`；构建日志、源摘要、结果与隔离回归汇总均在同目录。未部署、未启动游戏或访问玩家存档。原第 13 / 23 波 Collider NaN 的首写来源仍未证实，本轮修复不得代称该根因。

另纠正中英 Wiki 与开局注释的旧“2,000 发”承诺（SAFE）：当前游戏说明、实际发放和 2026-10-17 回归均为普通弹药一组、重弹药至少 4 发，未修改数值。相关 Wiki 守卫、数量执行回归与站点构建通过。台账容量守卫发现超限后，将 9 月 27 日两项已闭环记录按原字节归档，证据在 `Build/zombie-audit-20261021/ledger-archive-proof.json`。

审查剩余项（Open，P2 / COMPAT，L1）：CR-2026-10-21-009 即时退款与欠账持久化同时失败后，HostLifecycle Reset 丢失暂扣事务；CR-2026-10-21-010 欠账读取异常被当作 0，随后成功写入可覆盖旧债。均有明确故障分支证据，无正常玩家 L3 复现。前者需单独设计存档身份、换槽/删档/销毁与重试 owner，后者需 TryRead 区分缺键与读错并补故障执行回归；本轮未扩大欠账协议和生命周期。具体路径与触发条件见 CODE_REVIEW_FINDINGS 同号记录。

L3 验收：Z1，单弹枪选三叉/散射后对同种敌人比较全中伤害，仍应体现散射的 5% 枪伤代价，但不能额外只剩原来五分之一；切枪/退局后属性恢复。Z2，Boss/特殊怪预警结束须结算一次，暂停不偷跑、来源死亡/换局不补击；骚扰弹穿过玩家要命中，玩家侧移及目标点后方不误伤。Z3，正常完成多波并撤离，净化点兑现金只发生一次、背包掉落保留、可再次入场；付款不可用须留场保留点数并能重试。Z4，分别覆盖 12→13→14 和 22→23→24 波，身体/头部命中应持续扣血、圈外满进度能结算。未采样帧耗，记录数量回归不等于实机无性能问题。

## 2026-10-21 自定义友好 NPC 移除常驻头顶姓名（COMPAT，L1/L2，未部署、未实机）

CR-2026-10-21-003：自定义 NPC 在俯视、第一和第三人称下都额外常驻头顶姓名。旧 `NPCNameTagHelper` 复制原版外观后禁用 `HealthBar` / `FadeGroup`，每帧自行投影并强制打开名字。本机原版资源证实友好 Jeff 等 preset 关闭 showName / showHealthBar，基地 Orange / Xavier 的静态模型没有头顶名牌，姓名由官方对话、商店和交互界面显示。

撤掉自定义友好 NPC 的独立头顶 UI。保留旧登记入口，初始姓名由原对话 owner 设置；只有显式改名时才同步已有官方 DuckovDialogueActor 的 BossRush_ 姓名键，避免延迟 actor 被出生时的旧语言覆盖。有待更新但无 actor 时每秒至多查找一次；不创建 actor、Health 或名牌，不加 Harmony。注销与静态清理只释放 helper 状态，现有对话 / 商店展示姓名。阿稳、叮当、羽织、永久捏脸 NPC 和天空岛居民共用此入口；专题文档同步更新。

Windows 正式隔离编译退出码 0，无 Dev 定义，2 条既有 ModeH CS0649 警告；Managed 为实体拷贝，未写入游戏 Mod 目录。正式 DLL 位于 `Build/npc-name-native-20261021/BossRush.dll`，SHA-256 `2D44E9A1990E1426DF3F6685BE04537E868E17DEAF62E4AD0D7567A4A3CFA57A`；构建日志与源文件摘要在同目录。名牌、Harmony、静态缓存、事件生命周期、编译清单、NPC 生命周期与 Transform 缓存、知识库引用守卫通过，14 个 Dev 标识均不在正式 DLL 中。SkyIslandInteraction 聚合回归 971 条断言通过；10 个隔离守卫变异均命中预期失败，其中 9 个同时触发指定执行回归失败，按字节恢复及 SHA-256 核对后全绿，证据 `Build/name-visibility-negative-b0nhb8_n/negative-results.json`。全局 changed-only 曾因共享 CODE_REVIEW_FINDINGS 台账超过 1200 行失败，本轮相关守卫均通过；不将其写成全局守卫全绿。

未启动游戏或读写玩家存档。实机步骤 N1：俯视、第一和第三人称各从基地出门，观察阿稳 / 叮当 / 羽织方向，平时不应常驻三个姓名。N2：逐个打开对话与商店，姓名应继续显示；往返切图后无头顶名牌残留或失去交互。N3：天空岛中英文切换、隐藏 / 重现居民、延迟开启永久 NPC 剧情，官方姓名不应被出生时缓存的旧语言覆盖。三位旧 NPC 已有的切语言后对话姓名刷新遗漏不属于本次引入；本次不重写其 owner 初始注入。编译与替身回归不代表实机通过。

## 2026-10-21 丧尸满进度滞留与信标后续波次（COMPAT，L1/L2，未部署、未实机）

玩家明确补充：圈外继续打怪时仍显示战斗击杀进度且已满，随后进圈用尸潮信标开波，之后怪无法命中。按该约束追查，不能按准备期环境怪解释；圈可能是自动准备区或战斗便携区，现有证据不能确定。首次 NaN 的托管写入者仍未证实，沿用 10-20 的传播防护、异常碰撞恢复与正式限频诊断。

2026-10-21 追加只读复核（SAFE）：玩家确认第 13 波也发生在原版本，未安装修复 DLL，奖励组合不详。第 13 / 23 波均为普通波，移速早已封顶；奖励枪伤代价在正常上限下最多 52%，未发现正常组合必然产生首个 NaN 或全场永久无敌的闭环。保留未证实状态，不认定为修复后复发。现有正式产物与本机游戏 DLL 的 SHA-256 仍分别为 `16ED59D3573F2622A1213FEE9DD5FAF53C5E6CA25891D7F95023EECF19ACE727`、`507E7C448EC2CA8B3D0D346C6586D46E1067F73155FB218D82D74ACB9E666E83`；本轮未改运行时代码、未部署、未新增实机证据。后续验收同时覆盖 12→13→14 与 22→23→24 波。

- CR-2026-10-21-001：Combat Tick 在安全区工作前补达标结算，普通波与已清空 Boss 波不再依赖下一次死亡事件。死亡先去重、注销并计数、生成本体星点，再执行选项连锁；死亡副作用分别捕获，事件总边界阻止异常逃回官方 Health.Hurt。低频波末清理、NPC 回收、补给、横幅分别隔离，结算协程幂等登记；启动 null / throw 或迭代器异常后最多每秒重试，补给与自然污染只在原 CompleteWave 执行一次。
- CR-2026-10-21-002：StartWave 加准备阶段门，先初始化新波目标再执行精英与横幅；信标以 run / wave / operationId 归属，尾帧检查阶段、撤离、flag 与所属操作，取消 / Dispose 仅释放本次读条，启动失败回滚。没有证据证明该防御缺口就是本次实际竞态，安全区和信标链也未发现给丧尸写无敌 / 关闭受击碰撞体。
- 验证：全量守卫 747/747，最终 changed-only 144/144；相关执行回归 13/13，其中新 `ZombieModeWaveRuntime` 80 场景 / 274 断言、`EnemyRecoveryRuntime` 117 断言。新夹具链接真实死亡、Tick / Start / Complete / Settlement、信标方法和阶段守门；物理、地图与奖励 UI 为替身边界。隔离反向验证 4 项分别移除满目标补偿、死亡异常隔离、开波阶段门、信标尾帧复核，均命中预期失败，按字节恢复后通过；原工作区 SHA 未变。
- 正式构建：Windows `compile_official.bat` 退出码 0，无 Dev 定义，2 条既有 ModeH CS0649 警告；使用 Managed 实体拷贝，游戏目录 DLL SHA 未变。`Build/BossRush.dll` SHA-256 `16ED59D3573F2622A1213FEE9DD5FAF53C5E6CA25891D7F95023EECF19ACE727`。日志及源文件摘要在 `Build/zombie-wave-progress-20261021/`，反向报告在 `Build/runtime-regression-probes/ZombieModeWaveRuntime-9_8a2p97/report.json`。

L3 待验收：正常入口打过 22→23→24 波；在圈外击杀达标，左上应退出满击杀进度并进入结算 / 奖励；选完奖励进入准备倒计时，倒计时或尸潮信标都只推进一波。后续身体、头部命中持续扣血，异常恢复保留剩余 HP。满进度滞留、跳波、重复奖励、怪只可攻击不可受伤、恢复后继续 Collider NaN 均不合格。编译与替身回归不代表游戏实机通过。

## 2026-10-20 丧尸第 23 波无敌：NaN 传播防护与碰撞恢复（COMPAT / WIRE+，L1/L2，未部署、未自行实机）

玩家补充的 Player.log 有 198 次 Collider 变换矩阵 NaN / Infinity，涉及同名迅捷精英的根、身体和头部，能解释受击检测丢失；日志无法还原首发写入者，也未证明第 23 波专属。CR-2026-10-20-002 已修复可证实的恢复遗漏与传播缺口，首发来源继续标 UNVERIFIED。

- 引力井：拒绝非法原点、半径、拉力、坐标、旋转与溢出计算；通过真实 ECM2.SetPositionAndRotation 同步物理，保留速度与贴地。已核对真实 DLL，避开频繁 ForceSetPosition 的禁启和暂停贴地副作用。
- 共享恢复：对标准 BossRush、D/E/F/G 和丧尸模式已有跟踪敌人增加合法根姿态 / 受击祖先缓存，坏状态不覆盖快照；沿原有效落点恢复，保留剩余 HP、敌人身份和击杀 / 奖励流程，清 Movement 输入、ECM2 积累力 / 速度和动态刚体惯性。无合法快照或可达点等待；同步失败继续重试。受击祖先缓存按当前层级恢复，处理晚挂头碰撞体和移出根的旧节点。
- 诊断与成本：正式限频输出恢复前位置 / 旋转 / 缩放、节点路径、实例 ID、速度及强制移动状态。沿用每秒主巡检，缓存受击祖先每 2 秒检查、树发现每 10 秒刷新；常态不用逐帧全树扫描。此为静态成本分析，未做游戏性能采样。
- 验证与交付：正式编译、全量守卫、EnemyRecoveryRuntime 与 ZombieModeSafeZoneRuntime 等生产回归以及隔离反向验证结果见 `Build/zombie-invincible-20261020/` 与 `Build/runtime-regressions/`。DLL 使用无 Dev 定义的正式脚本，在游戏 Managed 拷贝上编译；未启动游戏或读写玩家存档。

验证：全量守卫 747/747 通过；最终失败重试 / 层级排序补丁后 changed-only 114/114 通过。隔离反向验证 8 个变异全部按预期转红并还原；相关生产执行回归 12/12 通过（ZombieMode 七项、EnemyRecoveryRuntime、IntegrationThirdReviewFixes、SpawnPositionPolicy、EnemySpawnRuntime、ModeEFEnemySpawnRuntime），恢复夹具 117 条断言。Windows 正式编译退出码 0，无 Dev 定义，2 条既有 ModeHPageContent 未赋值警告；使用 Managed 实体拷贝，游戏目录 DLL SHA-256 未变。正式产物 `Build/BossRush.dll` SHA-256 `C0159A3BAED3DF957265B79D500D824DB1FD1677B7AB2876259CF6BB816AC7DC`，完整证据在 `Build/zombie-invincible-20261020/` 和 `Build/runtime-regressions/`。

L3 待验收：普通入口打过 22→23→24 波，连续命中迅捷精英的身体与头部应掉血并可继续下一波；若触发恢复，已受伤怪保留剩余血量；持续无敌、满血重置或恢复后继续出现 Collider NaN 为不合格。首次 NaN 的托管写入来源仍未证实，新诊断用于缩小再次出现时的污染位置。

## 2026-10-18 v2.3.6 发版准备（COMPAT / OPERATIONAL，L1/L2；本轮修复的 F3 核对 L3 见下一节，未推送）

- `info.ini` 2.3.5 → 2.3.6（不进 git，已手动拷进游戏目录）；WikiContent 中英 `changelog__v2_3_6`、`catalog.tsv` 插行并顺延 order、两份 `changelog__highlights`；上一级目录 `a.md` / `b.md` 换成 v2.3.6 + v2.3.5 两段、链接指向 v2.3.6，`workshop_description_budget.py` 6950 / 7577（上限 7700）。
- 正式构建部署：游戏目录 `BossRush.dll` 与 `Build/` 同为 `E7E5DDD65D3412D335A6CAB35E80EC44F7316329CC3BFA5F817BB50896A7BFDB`，`check_dll_identifiers.py --expect absent` 14 个 Dev 标识全无，本轮新加的 F3 字符串也不在；游戏目录 WikiContent 已含 v2.3.6。
- 验证：全量守卫 747/747、全量执行回归 130/130、Wiki 站点构建通过（顺带的 8 份仅换行差异已还原）。push 与创意工坊发布待 owner。

## 2026-10-18 v2.3.5 之后五个提交审查：CR-2026-10-18-001～022 全部修复（COMPAT / OPERATIONAL，L1/L2，未提交、未部署、未实机）

审查结论与锚点见 `CODE_REVIEW_FINDINGS.md` 同日节。owner 要求全部修复，钻戒限量与官方存档键两条授权「好玩优先自己定」。

- 丧尸模式：属性满级判定提成 `IsZombieModeAttributeBonusAtCap`（绝对值比，减伤满 -40% 不再出卡）；`TryGiveZombieModeAmmo` 加 `allowQualityFallback`，商人保底品质的购买传 false，没有保底品质就留着保底、按普通区间买；区间回退改取离区间最近的那档；补给 / 弹雨改走 `TryGiveZombieModeSupplyAmmo`（手上口径发不出就退回开局口径，再落随机弹）；配口径先看 `GetGun()` 手持枪，再主槽、副槽。弹药夹具替身按官方 `DownGradeSearch` 收成单档降级，补区间高于可用品质、保底 (6,6)、属性满级四类用例。
- 好感与商店：钻石戒指（拍板）改为 `RestockOnlyWhenSingle` + 3 个游戏日——已婚不补，单身时满 3 天补 1 枚，离婚、戒指丢失后都能再求婚；规则在纯函数 `NPCShopStockLedger.EffectiveRestockDays`，不改存档编码。官方存档键（拍板）：开店后 `DetachOfficialShopSaveHooks` 摘掉官方 `Save` / `Load` 回调，不再写 `StockShop_BossRushNpc_*`，反射失败只记日志退回旧行为。好感页羽织的折扣标「治疗折扣」，有商店的 NPC 改读 `NPCShopSystem.GetDiscount`；衰减提示累加；永久 NPC 折扣上限 0.5 → 0.30（守卫同步并钉代码常量）；对话序列吞异常时 `DialogueManager.SequenceFailureSerial` 自增，剧情据此不记看过、不发奖。
- 遗种巢：`TryHatchAll` 走 `TryHatchEggCore(…, false, …)`，整批最后落一次盘，写盘失败时已提交的崽仍进揭晓并带出原因；部分孵化后停下写「孵出 k 枚，其余没孵：…」；巢页按崽身上的 `lockedByExpeditionId` 找本趟记录；队满不挂「派去远征」、底栏写原因；「治一道疤（40 遗魂）」有疤就挂；摸摸脉冲在体型被改写时 `Rebase`，曲线换成 EaseOut / SmoothStep；到期提醒去重换槽清空、键带出发时间；成就行 `ShinyFrameOnly` 只描边；页签指示条改 `Accent`；`RollNewPet` summary 归位。
- 守卫：`PetNestHatchCommitGuard` 跟到核心方法并断言整批单次落盘；`ZombieModeCompanionCounterplayGuard` 断言整条 if 条件；`PetNestUILayerGuard` 补三份新 UI 文件；`DuckNpcInvariantGuard` 折扣上限 0.30 并钉 `MaxDiscountRate`。
- 文档：`docs/contracts.md` §3 / §7.1；repowiki 好感度框架核心、僵尸模式、遗种巢系统；游戏内与在线 Wiki 的钻石戒指获取说明（中英）。
- CR-023（修复阶段全量回归发现，HEAD 上就红）：`ManualSeptemberReview` 补 1170e834 新成员的惰性替身；`IntegrationThirdReviewFixes` 的刚硬用例改为「崽的命中不吃减伤」。
- 预算：`NPCShopSystem` 改 partial，`DetachOfficialShopSaveHooks` / `ConfigureShopIdentity` / `SuppressOfficialRestock` 原样移到新文件 `Integration/Affinity/Systems/NPCShopSystemOfficialShopHooks.cs`（已登记编译清单），主文件 1144 行；`CODE_REVIEW_FINDINGS.md` 满 1200 行，已闭环的 2026-09-22 人工实测复核修复交付节逐字移入 `archive/CODE_REVIEW_FINDINGS_2026-09.md` 并留索引。

验证：Windows 正式编译 0 错误（`GAME_PATH` 指游戏程序集拷贝，未部署；`Build/BossRush.dll` SHA-256 `CB722D094B3F4ECF2A366111373173E0D18F5F22DABFC0B154B94F486A717BBA`，游戏目录 DLL 未变）；全量守卫 747 中 746 过，余下 `LargeFileBudgetGuard`（GoblinAffinityConfig 超 2 行）改掉后单跑与 changed-only 189 项全过；全量执行回归 130/130；Wiki 站点构建通过（构建顺带的 8 份仅换行差异已还原）。反向验证 10 项（满级绝对值、保底不回退、最近品质、衰减累加、已婚不补、整批落盘守卫与夹具、反制整条件、UI 分层、折扣上限）全部转红并按字节还原。CR-003 / 009 / 015–018 / 020 / 022 只有 L1（读码与编译），没有执行回归。

L3（2026-10-18 owner 跑 F3 自动验收 `20261018_071925_541`，Dev 构建 122ED28A，新增 11 条用例在 `DebugAndTools/F3GameplayValidationReviewFixes.cs`）：10 PASS / 1 SKIP。`MODE_ZOMBIE_REWARD_FIXES` 实机证明 6 项属性满级不进选卡池、归零后都回来；真实物品表 23 个口径 69 项弹药判定全过（Rocket 只有 5–9 品，按回退取到）；手持枪优先。`AFFINITY_RING_RESTOCK_MARRIAGE` 已婚 0 / 单身 1 / 当天 0；`AFFINITY_TAB_NURSE_DISCOUNT` 写「治疗折扣」；`AFFINITY_DECAY_NOTICE_ACCUMULATE` 8 天 / 80；`NPC_SHOP_OFFICIAL_SAVE_HOOKS` 官方回调 1→0；遗种巢详情、队满、治疤价钱、脉冲回基准、成就只描边全过；`PETNEST_HATCH_ALL_PLAN` 因测试档没有蛋 SKIP（整批单次落盘仍只有 L2）。整轮另有 11 红：9 条与 10-16 两轮相同（纪念品残留 ×2、Mode G 九波、Mode H 入场 ×4、终章 Boss、云蚋演练）；新出现的 `SKY_AUTO_REAL_COMBAT_GATE`（剧情面板拦截半径只有 5 米，瞬移后等 4 秒判，敌人未进 5 米）和 `FINAL_5S`（整轮从开局基线起就慢一倍，岛内基线也慢 40%）都不在本轮改动面，记 UNVERIFIED，下轮复跑再看。

L3 仍缺（F3 覆盖不到的手感与画面）：丧尸模式减伤满 40% 后三选一里不再出现减伤；副槽拿 RPG 时弹药补给给火箭弹；商人保底 6 品在口径没有 6 品时不被吃掉。叮当 Lv.7 买戒指后关店重开售罄；结婚后不补；离婚后（距上次补货满 3 个游戏日）再开店有 1 枚。羽织好感页写「治疗折扣」。遗种巢一次孵 3 枚以上点「全部孵化」无明显卡顿、全部入巢；刚归来未揭晓的崽再派出，巢页显示剩余时间；队满时巢页没有「派去远征」；治疤按钮带价钱、遗魂不够点了提示；摸摸后紧接升级体型正确。

## 2026-10-17 玩家反馈：丧尸模式 J-Lab 掉帧、RPG 不配火箭弹、属性满了还出、白手起家前两波出 Boss（COMPAT，L1/L2，未提交、未实机）

- 白手起家前两波：第 1–2 波只刷小怪池里血量最低、不显示名字的 preset。官方 Boss 里也有不显示名字的（`Cname_Boss_Red/Blue`），天空岛小兵、遗种崽、冠军杖卫等运行时克隆改过名（不带 "(Clone)"）且底模多是官方 Boss，都可能混进小怪池。`ModeDRuntimeModule_EnemyPools.cs` 小怪池排除 `isBoss` 与实例 ID 为负的运行时 preset；按名缓存同名优先官方资源（遗种崽克隆沿用官方血脉 nameKey，会顶替官方狼）。根因是读代码推断：没有玩家日志，没确认玩家那局混进来的是哪个 preset。另一可能（未改）：进图时狗子背包还在读，裸装判定回「未就绪」，整局回落成普通挑战；入场 setup 在 `LevelInited` 之后再等 0.5 秒才判定，这条概率低。
- RPG 不配火箭弹：`TryGiveZombieModeAmmo` 只在品质 1–2（合约档 / 商人按各自区间）按口径找弹药，官方 `Search` 落空时只往低品质降级。区间里没有该口径就改发该口径最低品质那档；不堆叠 / 小堆叠的重弹药补到 `HeavyAmmoMinimumRounds`（4）发；不堆叠物品不再写数量（官方会报错误日志）。弹药补给与弹雨改按手上的枪配口径。新执行回归 `ZombieModeAmmoSelection`。
- 远程伤害满 100% 还出：属性到上限后从候选里拿掉（此前以 20% 权重留着，选了不加）。
- J-Lab 掉帧（只读代理审出热点，读代码确认的是同步 A* 查询无退避；与 J-Lab 的关联是推测，未采样帧耗）：卡死 Boss 找不到落点时 1 秒后再试，不再每帧重扫；地图点由近到远扫、找到第一个够远的可达点即停（结果与原全量扫描相同）；玩家吸不上导航面时整轮早退（新共享 `SpawnPositionHelper.IsReachableAnchor`）；同一帧整轮失败不再重找（安全区一次弹多只、多 Boss 同帧解卡）。没改：骚扰弹每发一盏点光源 + 两份材质（`ZombieModeHarasserProjectileGuard` 钉着材质归属，且与 J-Lab 的关联只是推测）。
- 守卫：`ZombieModeSpawnSelectionSqrGuard` 片段随新结构更新（仍只用平方距离、两档距离），反向验证转红后按字节还原；执行回归 `ZombieModeSpawnRuntime` 新增由近到远、保底、锚点早退与同帧记忆用例，反向验证（去掉锚点早退）转红。

验证：隔离签出（HEAD + 本次文件，`GAME_PATH` 指游戏程序集拷贝，未部署）Windows 正式编译 0 错误；全量守卫 737 过、9 红均为干净签出缺 local-only 资源 / shapely；执行回归 ZombieMode 七项、ModeD 两项、SpawnPositionPolicy、LegacyArenaEntry、CampaignPlayability 全过。

L3 缺：J-Lab 丧尸模式打到有 Boss 的波次，对比改前帧率（尤其站在台面 / 楼梯上、Boss 卡墙时）；随机枪械抽到 RPG 后背包里应有火箭弹；远程伤害到 100% 后不再出现在三选一里；白手起家第 1–2 波不应出现 Boss（先去过天空岛或带崽出战后再进更容易复现旧问题）。

## 2026-10-17 NPC 好感 / 商店改进阶段一（COMPAT / SCHEMA+，L1/L2，未实机）

方案稿 `docs/design/2026-10-17_NPC好感商店个性系统改进方案.md` 第 3 节，owner 同意第 7 节按建议默认值定（戒指终身 1 枚、按游戏日补货、衰减 10 且配偶锁 Lv.10）。

- P1-1 商店库存：新文件 `Integration/Affinity/NPCShopStockLedger.cs`（已登记编译清单），库存写进 `AffinityData.shopStockTokens`；`NPCShopSystem` 开店按记录恢复、购买与关店回写、压住官方 `RefreshIfNeeded`；钻石戒指 `RestockDays = 0`。顺带修掉潜在问题：动态 `StockShop` 的 merchantID 默认是官方 `Albert`，开店时存档会覆盖官方 Albert 的库存存档（静态推断，未实机复现）。
- P1-2 衰减：每天 15 → 10，里程碑与配偶下限，下次聊天提示一次。
- P1-3 永久 NPC 折扣：蓝图 `discountsByLevel`，浮舟与钟守配了 10/15/20%。
- 文档：`docs/contracts.md` §3（`NPCAffinity` 的 `SCHEMA+`）、§7.1（NPC 商店）；repowiki `好感度框架核心.md`；注释里「每次开店重置」的四处说法已改。

验证：全量守卫 746/746；全量执行回归 129/129（`AuditCombatSeptember`、`SkyIslandCombatRuntime` 需设 `GAME_PATH` / `BOSSRUSH_GAME_MANAGED` / `BOSSRUSH_HARMONY_DLL` 后复跑；`PermanentDuckNpcDialogue` 原断言「永久 NPC 不打折」按 P1-3 改为校验蓝图折扣）；`DuckNpcInvariantGuard` 新增蓝图折扣与补货天数校验（反向探针转红后按字节还原）；执行回归 `NpcAuditFixes`、`NPCShopPayment` 新增用例并做反向探针；Windows 正式构建 0 错误，已部署，DLL SHA-256 `29F37E60EBAAAC946D0A379C244CB8A4B7A86D116C09D94E41622BDB14978E31`。

L3 缺：叮当 Lv.7 买戒指后关店重开应显示售罄；买空一件商品，跨一个游戏日后补货；看过 Lv.5 剧情的 NPC 连续多日不聊，不应跌破 Lv.5，下次聊天时应弹出「好久不见」提示；配偶连续多日不聊不应被送回家；浮舟 Lv.3 商店价格应比原价低 10%。

## 2026-10-17 遗种巢 UI 与交互全部修复 + 丧尸模式崽打不动（COMPAT / SCHEMA+，L1/L2，未部署、未实机）

起因：owner 要求对照主流游戏审查遗种巢 UI 与交互（报告 `docs/reports/reviews/2026-10-17-遗种巢UI与交互对照审查.md`），补充「高级感、流畅感、不要塑料感、该加动画就加」，并授权全部自行拍板；同时转来玩家反馈「带崽打丧尸一滴血一滴血地掉」。

- 动效与局部刷新：巢页点选 / 勾选原地改行 + 右栏淡入；新选中项描边淡入（选中色由 WarningText 改 Accent）；页签指示条；进度条平滑与数字滚动；成功轻提示 + 官方确认音；官方翻页键切页签；演出关闭后自动刷新。新文件 `PetNest/PetNestUIMotion.cs`、`PetNestUIMotionParts.cs`。
- 巢页：排序、收藏（SCHEMA+ `favorite`，不能放生 / 不走亡命档 / 不进一键勾选）、行动方式三选一、独立背包统一叫法、下一级预告、勾不上的原因、立绘框身份色。
- 孵化：巢满不挂按钮、全部孵化（`TryHatchAll`）、保底进度条；揭晓按稀有度预兆 + 爆发 + 分档音效 + 批量汇总。
- 远征：就地「查看结果」、删页顶警示、目的地第二行写元素、档位卡首行成功 / 死亡率、收藏的崽不显示亡命档；翻牌按结果配音效、阵亡牌压暗多停；基地内到期弹官方通知。
- 博物馆：全谱系剪影、点格子浮层详情（新文件 `PetNestUIMuseumDetail.cs`）、驯养成就、碑文日期；组装挪到新文件 `PetNestUIMuseumPage.cs`，`PetNestUILayerGuard` 同步纳入（反向验证：去掉碑文档位转红）。
- 局内：HUD 经验条、升级脉冲 + 音效 + 头顶飘字；喂食选项名写食物 / 冷却；行动方式选项名写下一种；摸摸轻跳。
- 丧尸模式：精英「刚硬」（远程 ×0.1）「反制」（同类连击减伤 60%）此前把崽算作玩家方，崽不会换武器，对这类精英几乎打不动，还会替主角攒反制计数让精英减主角的伤害。改为只认主角本人（`IsPlayerOwnHit`），Boss 护盾与护盾光环对崽照常结算；与中英 Wiki「主玩家」一致。这改动 10-07 af9bcb43 把判据扩到宠物的那一部分；10-06 裁决原文是「宠物攻击正常消费 Boss 防御」，护盾部分保留。新守卫 `ZombieModeCompanionCounterplayGuard`（反向验证：「反制」改回旧判据转红）。
- 没做：分区旁「?」、局内快捷键、实际生命 / 伤害数值（原因见报告第 8 节）。

验证：改动相关守卫与新守卫通过；全量 747 条中 10 条红，全部来自另一会话放在仓库内的 `.claude/worktrees/loving-shirley-d7cbf3/` 与它未登记的 `DebugAndTools/TerrariaBridge/`，与本次无关；执行回归 ContentTransactions 627 断言（新增收藏、全部孵化 8 条，反向验证收藏放生拦截转红）、PetNestCare（喂食冷却选项名，反向验证转红）、PetNestGrowth、PetNestCompanionActivation、ZombieMode 六项全过；`GAME_PATH` 指向游戏程序集拷贝的隔离 Windows 正式编译 0 错误（工作区里有另一会话的半成品，所以没有部署到游戏目录）。

L3 缺：报告第 9 节的看图清单——巢页点选是否顺滑、页签指示条、轻提示位置、全部孵化汇总、远征就地翻牌后面板刷新、博物馆浮层、HUD 经验条与升级飘字、喂食选项名、丧尸模式里崽打「刚硬」「反制」精英的血条变化。

## 2026-10-17 遗种巢远征与战痕调整（COMPAT，经济调整经 owner 授权，L1/L2，未实机）

起因是同日的设计评估（对照碧蓝航线委托、刺客信条兄弟会、XCOM、暗黑地牢等）；owner 要求四条都改、数值按好玩拍板。

- 远征同时在途上限：基础 2 队，图鉴解锁 10 / 20 血脉各 +1，最多 4。新文件 `PetNest/PetNestExpeditionSlots.cs`（已登记编译清单），只数未结算记录；`TryDepart` 事务内外两次判，失败码 `expedition_slots_full`。不写档，旧档超额在途照常结算。
- 平安档成功率 0.55 → 0.85，现金 400–1200 → 300–900、遗魂 15 → 10，单趟期望与旧值同量级；风险档卡首行写实际成功率。
- 遗魂治疤：`PetNestService.TryHealScar` 花 40 同血脉遗魂治最早一道，并进旧伤计数，不加存档字段；巢页「治一道疤」按钮与战痕栏花费 / 余额。
- 元素亲和：风暴海域 电 + 空间、酸雨废墟 毒 + 物理、极寒荒原 冰 + 火 + 幽灵，判定走 `DestinationAcceptsElement`；远征页写这只崽的元素与契合表。
- 顺手：炫彩注释「45 种」改为 90 种有序搭配（与 Wiki、代码一致）。
- 文档：Wiki 中英 `system__pet_nest`、repowiki `遗种巢系统.md`。

验证：全量守卫 746/746；执行回归 ContentTransactions（619 断言，新增 `PetNestScarTreatmentAndExpeditionSlots`）与 PetNestGrowth / PetNestCompanionActivation / PetNestCare 全过；新用例两处生产变异（队数判据放宽、治疤改治最新一道）与 `PetNestModelsGuard` 元素覆盖（删幽灵）均反向转红后按字节还原；Wiki 构建通过；Windows 正式构建 0 错误并已部署，DLL SHA-256 `B95447390462DD46C3DDAFE0B805CFABE75637D0A7EDCBBF85D5C94A893965DE`（游戏目录一致），74 包哈希核对通过，WikiContent 已同步。

L3 缺：远征页「在途 x / y 队」与队满提示、风险档卡成功率行、巢页治疤按钮与战痕栏在 1920×1080 下是否换行合适；治疤后下一局崽的移速恢复；数值手感（平安档是否还有人用、风浪档是否仍是主力）。

## 2026-10-17 v2.3.5 发版（COMPAT / OPERATIONAL，L1/L2，未实机）

汇总 958de5d9（天空岛指引对齐）与 d6d313cb（交互点可见物）：Wiki 新增 v2.3.5 中英更新日志并登记目录与总览；`info.ini` 升到 2.3.5 并拷进游戏目录；工坊简介 ../a.md / ../b.md 换上 v2.3.5、删去 v2.3.3 段，`tools/workshop_description_budget.py` 量得 a.md 6951、b.md 7685（预算 7700）。为不超 1500 行，2026-09-26～27 四节逐字移入 `archive/FIX_TRACKER_2026-09.md`。

验证：全量守卫 746/746、全量执行回归 129/129、Wiki 构建通过；Windows 正式构建 0 错误并已部署，DLL SHA-256 `F74C48C73A702350CC43322ABE712DD3F01B581D9EABD803C7A7C7661D723A75`，74 包哈希一致，无 Dev 标识。实机清单不变：PS-01–PS-06、PV-01–PV-10、2.15.41–2.15.46、G-01–G-04。

## 2026-10-17 天空岛交互点可见性：每处交互都有实体物件（COMPAT，L1/L2，未提交、未实机）

owner 要求地图上有交互的地方都要有看得见的实体物件。盘点报告 `docs/reports/reviews/2026-10-17-天空岛交互点可见性盘点.md`：原先看不见的是 20 处见闻点 `Search_*`（触发盒）、2 个航路图（触发球）、12 处信鸽（光 + 走近浮现的字）、30 处采集点（光斑 + 点光）；搜刮 / 谢礼 / 奖励箱、纪念物与归航船名册、居民、零号区航向仪、基地船本来就看得见；兜底装置、商店、合成台、菜畦、药臼、风晶灯、谜题、签筒都是挂在见闻点或航路图上的子选项，跟随宿主。

- 见闻点：`Search_B / E / H` 的小木牌出自会话「天空岛三处看不见的交互点补立牌」的补丁（`SkyIslandGates.AddPointSign`）；本条在其上补齐其余 17 处（15 块双桩 + 码头整备、风标两块单桩窄牌 `AddPointPost`，牌面与字抽成 `AddPointBoard` 共用），字与交互名同词、随语言切换。落点离线求解：相机反方向 2.2 m 起摆，木桩不压路缘、不穿实体，不进任何触发盒，离自家见闻点最近；风标那处站在铺装广场正中，窄牌在 3.7 m 外广场边沿。
- 新文件 `SkyIsland/SkyIslandPointProps.cs`：白鸽（随信鸽飞走）、航路图架、五种采集物（采走当帧拿掉），程序化基本体 + 场景已加载的作者材质（`SkyIslandPropPalette` 每个 owner 每趟扫一次），无碰撞体，摆在交互体正中。已登记 `compile_official.bat`。
- 原落点在路面 / 广场铺装正中、实物压路的 12 处挪到路边：风铃集航路图（`GuideOffset` 改走 `PlacementFor`，码头那处逐位不变）、信鸽 Letter_03/04/06/07/09/10（`PlacementFor` 改写表；09 另躲开巡守槽位 4 m）、采集点 B1/B2/C1/F2/S4a（`BuildNodes` 方位）。
- 运行时摆放表 +100 行（`point_sign` 56、`interact_prop` 44）；场景净空证据表用 2026-10-01 台账 `ledger_d`（实例 13,593、三角形 2,890,336 与原证据相同，作者件无一移动，不用重打场景包）重跑，设计接触 23 条与原先逐条相同、开放 0。
- 守卫：新 `SkyIslandPointPropsPropertyTest`（建交互体的 9 处写法各登记实物证据、摆放表逐处核对、7 条破坏探针）；`SkyIslandInteractionCompetitionPropertyTest` 木牌集合改为恰好等于全部 `Search_*`、航路图按 `placement_for` 复算；`SkyIslandPlayabilityGuard` 认 `PlacementFor`；`SkyIslandContentPackGuard` ② 注释与报错按「Search_E 旁有只写地名的小木牌」改写。反向验证：删采集物调用、删一块木牌各自转红，按字节还原 sha 一致。

验证见报告第五节。L3 缺：实机清单 `docs/guides/sky-island/天空岛_待人工验证清单.md` 末节 PV-01–PV-10（接在小木牌 PS-01–PS-06 之后）。

合并（2026-10-17，owner 同意）：合并补丁 `git apply --check` 通过后打进主工作区（含小木牌补丁全部内容，未单独打那份），报告拷进 `docs/reports/reviews/`，PS / PV 两段清单追加到人工清单末尾。主工作区复验：全量守卫 736 通过，另 10 条红只因扫进了 `.claude/worktrees/` 下两个会话的工作树副本（逐条核对，报错路径全在工作树里），主工作区源码自身无红；SkyIsland 执行回归 17/17；Windows 正式构建 0 错误并已部署，DLL SHA-256 `EC0A3D73F37708585C16DE19D984C67A30BE31424ADCD447F562B18F8B63959A`（游戏目录与 Build 一致，无 Dev 标识）。

## 2026-10-17 天空岛剧情指引全面核查（COMPAT，L1/L2，未提交、未实机）

owner 要求从头到尾再查一遍整条剧情指引（含基地序章），报告 `docs/reports/reviews/2026-10-17-天空岛剧情指引全面核查.md`。新执行回归 `SkyIslandGuidanceMatrixRegression` 用生产 `TryApply` / `CanOffer` / `CanDeliver` 枚举 3952 个可达剧情状态 × 中英 × 苇白 / 浮舟 / 钟守逐个缺席，核九条不变式（有圈、圈里有事、照圈能通关、圈与罗盘不泛称、引号里是真按钮名、英文无中文、缺席给予者带兜底装置、做完换导语、交付被拦有原因）；结构不变式本来就全过，没有死胡同。确认并修 22 类，只改文案、标签和两个纯函数，不动判据、旗标、存档、任务 ID 与坐标：

- 岛上（G1–G14）：给予者不在岛上仍叫人找他（14 处，改成写地方或补兜底装置）；苇白说猎首在「风标底下」（实在东边 51 米，步骤表断言同步）；目标卡英文 pick reconcile 不是按钮名；钟守讲和只列缺的物证并带地方（`MissingProof`）；钟庭圈写「钟守留言」；双航标门导语不再说「门后就是钟庭」（那扇门在北头 83 米外）；风标 / 星灯 / 折翎 / 观星镜做完后面板换导语（`SkyIslandPointText.BriefFor`）；结局后紫圈与罗盘写清是哪件支线（`SkyIslandMapMarkers.SideTask`）；引风牌子英文原词；风标交互名「系牢回程绳桥」；眠苔「附近两伙人」；航标任务说明「两伙人」；浮舟在家台词点名「渡口整备」；官方「完成任务」被拦时给天空岛自己的原因（`SkyIslandOfficialQuestTable.DeliverBlocked` 接到桥）。
- 序章（P1–P8，只读子代理审、本会话按代码与官方本地化表复核）：丢了航向仪要先再打倒守卫；背包满拆出的航向仪进自提柜时提示回基地到马蜂自提点取；「残骸」改「摔坏的航向仪」；中文「Jeff」改官方名牌「杰夫」；圈只在零号区地图上；「断风游猎 · 守」；交付后指去船边；Wiki 用官方按钮原词、守卫约 70 米醒。

验证：全量守卫 745/745（`SkyIslandPreludeGuard` 加 4 条结构检查与内置反向探针，`SkyIslandPlayabilityGuard` 认 `MissingProof`）；执行回归 SkyIsland 17/17，F3AutotestJudges、AuditModeLifecycle、CampaignPlayability、F3ValidationExecution 通过；新矩阵九处改坏全部转红、按字节还原 sha 一致；改写台词 15 段屏数不变；步骤表守卫 90 步 / 298 行（新增 `SKY_AUTO_END_MAP` 与 4 步断言，人工清单 2.15.41–2.15.46、末节 G-01–G-04）；Wiki 构建通过（8 个只改换行的镜像已还原）；Windows 正式构建 0 错误并已部署，DLL SHA-256 `F7492AE2853B0E5F62B18BAF5F2DFB2C4255B7A980BAC62D9577B821820B44D8`，无 Dev 标识。未修交出去：看不见的交互点补立牌、序章任务列表「任意地点」（要新增反射官方私有字段）、守卫刷不出来的兜底。

## 2026-10-17 玩家反馈「双航标门在哪里，风晶哪里烧」（COMPAT，L1/L2，未提交、未实机）

结局后、打过噬风的档：「引风」（烧一块晴岚风晶唤回噬风·回响）在栈道南头的双航标门装置（`Search_E`）上点，可 K3 一开地图就不圈它、罗盘也不指，文案只说「栈道可引风」「去双航标门烧」，没说门在栈道哪头；名字又像北头那扇两灯亮才开的钟庭门（离装置 83 米）。改法：`SideTargets` 在结局后打过噬风时照样圈 `Search_E`，紫圈写「双航标门 · 在这儿烧风晶引风」；罗盘经 `CompassSideTargets` 只在这一趟真引得了风时才指它，报「鸣风栈道南头的双航标门，在那儿烧风晶引风」，否则照旧指缺灯处和风晶簇。目标卡结局行、钟守结局台词（点名「风眼已散」那块牌子）、手记里风晶与噬风之核两行、钟守拒绝话、苇白台词都写成「栈道南头的双航标门」；Wiki 中英与镜像补位置。屏数不变；步骤表两条手记断言跟着改，结局阶段加一条目标卡断言。

验证：执行回归 `PointerTargets` ⑤ 与 SkyIsland 17/17；守卫 changed-only 60/60（`SkyIslandContentPackGuard`、`SkyIslandContentWeaveGuard` 同步新的罗盘写法）；三处改坏（不圈、目标卡改回、罗盘不过滤）全部转红、按字节还原；步骤表守卫、Wiki 构建、Windows 正式构建通过并已部署（DLL `FE62B5A2…`，无 Dev 标识）。为不超 1500 行，09-22 三节逐字移入 `archive/FIX_TRACKER_2026-09.md`。实机看：结局档打过噬风、K3 已开，按 M 栈道南头有紫圈；带核和风晶时罗盘指过去。

## 2026-10-16 F3 第十轮（`20261016_135926_649`）6 条新红复核（SAFE，L1/L2/L3，未提交；复跑 5 条转绿、云蚋按 owner 目检改判）

只读文字报告、Player.log 与截图的数值复算（PIL 逐像素算亮度，没看图）。6 条都不是生产缺陷：5 条已在复跑中转绿；云蚋初判有误，owner 目检看得见，改成只记读数：

1. `SKY_AUTO_REAL_PIGEON_LETTER` 读信面板没开——**步骤时序**。上一步停在 `POI_F`，交互那一刻 Player.log 打出「附近还有威胁」（5 米交火门，同帧有 Patrol6 配枪），门照设计挡住了。步骤改成先回 `PlayerSpawn`（巡守槽离码头 ≥45 米）、`wait_quiet:6:true` 再读信。
2. `SKY_AUTO_REAL_NIGHT_GNATS` 云蚋韦伯 0.364→0.17——**判据不可分辨，改成只记读数**（owner 2026-10-17 目检复跑截图：云蚋正常看得见）。初判是「强制夜里只拨 Mod 钟点、官方后处理仍按官方钟点（各轮都在 8.4–8.9 点开跑）挑白天，10-01 起白天换成户外浅蓝雾」，据此让 `night:on` 在岛上克隆配置里把非夜里时段的 Volume 临时换成同条目夜里那份（`SyncAutotestNightVolume`，off / restore / 环境还原写回，不拨官方时钟、不写存档）。复跑 `20261016_152733_616` 换成了（`swapped=6` / `restored=5`），画面却更亮：云蚋那张整帧中位亮度 09-22 0.009 → 首轮 0.037 → 复跑 0.082，码头篝火邻域 0.005 → 0.024 → 0.068，云蚋韦伯 0.098 / 0.10。所以后处理不是主因，初判作废（L3 读数）；换 Volume 这一处保留，因为它让强制夜里与真夜里同一套后处理，复跑这一步的画面就是玩家真夜里在码头看到的。剩下的嫌疑是 10-01 路灯把码头地面照亮（ce1f8360），而云蚋是 `Sprites/Default` 不受光的暗色小贴图，在亮地面上对比塌了——未分离验证（UNVERIFIED）。同尺寸的空地面在三轮截图里韦伯中位 0.045–0.065、p90 0.4–0.6，云蚋 0.098 跟空地分不开，亮度探针在灯照地面上没有判别力；两条 `visible_min:gnat` 门槛改 0（只记读数），看得见与否以 owner 目检为准，步骤标题写明。
3. `SKY_AUTO_NEW_S2_FOCUS` 正文对比度 2.93——**判据取样假红**。两轮截图正文周围的亮度分布一致（全分辨率外圈中位 0.021 / 0.021，正文约 9:1），按存档截图复刻探针：外圈上限 1200 时步长 10 像素，字框差 1 像素就把下沿亮线整行取进来，同帧在 2.98–7.00 之间翻（09-22 的 PASS 也是运气）。字框与外圈取样上限改 30000（`AutotestTextSampleCap`），同帧 ±1 像素只在 8.1–9.3；三轮存档截图的其它 7 条对比度断言复算无一翻红。附带发现：S2 底图左三分之一压暗后仍有 0.13，那一段按像素量约 3.5:1（声明色 5.07:1，即 `BackgroundTintAlpha` 注释里的设计值），owner 2026-10-17 定不再压暗。
4. `SKY_AUTO_NEW_S4_GUARD` 面板直接开了——**判据过期**。d483925e 把交火门收到贴身 5 米，守卫刷在 17–34 米外，面板不再先被挡；生产路径照设计在正文写「先清除瞭台上的守卫」、不挂收录 / 谜题。步骤改成 `wait_panel` + `body_contains` + `choice_absent` + `close_panel`。
5. / 6. `SKY_AUTO_STORM_KEEPSAKES`、`SKY_AUTO_BASE_AFTER_RESIDUE` 纪念品 7–8 件——**测试槽残留 + 判据不看基线**。开跑快照里缓冲区已有航徽 7、噬风之核 7（`500068:7->8`、`verified=7/7`），是 09-15…09-22 七轮旧验收漏收的（4dd333ab 之前收回不看官方待收取缓冲区）；本轮只多发 1 核，还原后回到 7/7。全自动验收改按快照基线判「多出来的至多一件」（`ValidateAutotestKeepsakes*`，判据仍是 `JudgeKeepsakes`）；只读套件与主套件照旧按绝对件数，所以测试槽上的 `SKY_KEEPSAKE_ITEMS` / `_BASE` 只读用例在残留清掉之前仍会红——清不清（删专用测试档 1 缓冲区里 7+7 件旧奖品）留 owner 定。owner 2026-10-17 同意删残留：收回时纪念品目标件数压到一件（`AutotestReclaimTarget`，只在过了 `AutotestWriteAllowed` 的专用测试档），下一次 F3 全自动验收收尾时删掉缓冲区里多出的 6 + 6 件，之后只读用例在这个档上也能绿；AuditModeLifecycle 加两条断言（压到一件且留第一条、其它物品照快照件数；本轮新发的照样收回），反向探针转红后按字节还原。另记：收回先删缓冲区尾部，本轮把一件 `give_if_missing` 放进背包的核留在了背包、删掉的是一条原有缓冲记录（总数对、位置变），影响只在测试槽，未修。

复跑 `20261016_152733_616`（Dev `720D3F37…`，MVID `63cfed0c` 与部署一致）：信鸽、倒挂邮亭、残星瞭台、两条纪念品 5 条 L3 转绿（另一会话的 `SKY_AUTO_NEW_GUARD_EAST` 也转绿），86 PASS / 2 FAIL / 1 SKIP；剩云蚋（见上）与 `SKY_AUTO_REAL_BOSS_WAYLAYER`（截信人伸手时人被甩出够得着的圈、抓空，09-22 同样红过、10-16 首轮绿，步骤只贴一次身，属步骤时序抖动，不在本次 6 条里，未改）。复跑后已换回正式构建并部署，游戏目录 DLL `86A41D71…`（含共用工作区全部未提交改动），不含 Dev 标识。

验证：全量守卫 745/745；执行回归 SkyIslandValidationJudges（新增 3 条基线断言，判据改回绝对件数时转红、按字节还原 sha 一致）、F3AutotestJudges、AuditModeLifecycle、F3ValidationExecution、F3MapTourJudges 全过；步骤表守卫通过。Dev 与正式构建都在临时 `GAME_PATH`（Managed 拷贝）下 0 错误，游戏目录 DLL 仍是 `4349F09F…` 未动；正式构建不含 Dev 标识。复跑清单见 `C:\Users\GASEN\AppData\Roaming\Claude\side-session-notes\local_32cbdadf-d7c8-411e-885b-443581f7995c\2026-10-16-F3第十轮六条新红复核.md`。

## 2026-10-16 天空岛任务指引指向复核（COMPAT，L1/L2，未提交、未实机）

接着 v2.3.4 的双航标门一条，把天空岛每条「去哪儿、点哪一项」的指引按交互点逐条对过，报告 `docs/reports/reviews/2026-10-16-天空岛任务指引指向复核.md`（坐标取 `layout.json`；`Search_*` 交互点在场景里是看不见的触发盒）。确认 7 类，全部只改文案与标签，不动判据、旗标、存档、任务 ID 与交互点坐标：

1. 居民不在岛上时，地图圈与罗盘仍写「找苇白 / 找浮舟」，钟守那条写「归航钟庭」，圈里其实只有兜底装置：新增 `SkyIslandMapMarkers.ContactTask`，人在写找他、人不在写装置交互名（委托板 / 渡口整备 / 钟守留言）；浮舟、钟守两条任务的 `Contact` 补「不在就找「…」」。
2. 敲钟的四句指引（目标卡「去钟庭的钟架」、接任务回话、任务目标行「走到归航钟跟前」、`Search_H` 导语「这就是归航钟」）指向北头挂大钟的钟架，交互点 `Search_H` 在西南角、相隔 55 米：改指「钟守留言」或钟守本人。钟守在家台词不再说「回钟庭找我」。
3. 罗盘与 K3 门牌说「双航标门木牌」，`Search_E` 没有木牌，看得见的门牌在 72–74 米外北头钟庭门：改「栈道南头的双航标门」。
4. 守卫「风标跟前一伙」实际在风标以东 51 米：改「风标东边」，Wiki 中英与站点镜像同步。
5. K1 按钮叫「系牢林边旧运菜道 K1」，回话、拒绝话、门牌都叫「回程绳桥」：按钮改「系牢回程绳桥 K1」。
6. 眠苔只拿到旧信就叫人去见折翎（要旧信与航路图两样），折翎了结后仍说「折翎在等」：按旗标分三句。
7. 穿镜纹甲、没带齐两样时，折翎和解按钮仍叫你「出示旧信与航路图」：换成「让他看你身上的镜纹甲」；折翎了结后眠苔不再劝人穿甲去谈。

验证：`run_guards --changed-only` 52/52、`--filter SkyIsland` 61/61；`run_runtime_regressions --filter SkyIsland` 17/17（设 GAME_PATH / BOSSRUSH_GAME_MANAGED / BOSSRUSH_HARMONY_DLL）；Wiki 构建通过；Windows 正式构建 0 错误并已部署本机游戏目录，DLL SHA-256 `D3222BF213C3CCEB3BCF0CA75D8D5EAFD0CEE7A50BE1B1E9C5473605BA541857`（最后一处台词改动之后编的）。新增执行回归 `SkyIslandNavigationRegression.PointerTargets`（中英各一遍；`ContactTask` 由 `run.py` 逐字抽取），`SkyIslandContentPackGuard` 钉 `SkyIslandWorldStory` / `SkyIslandGates` 接线；婚姻夹具替身补 `SkyIslandStoryFlag` 与 `Has`（`ZhelingChoices` 读旗标）。十处反向验证（六处执行回归、四处守卫）全部改坏转红、按字节还原 sha 一致。改写台词用 `tools/sky_island_line_screens.py` 核过屏数与原句一致。

未修、交出去：委托板 / 双航标门 / 钟守留言三处交互点在场景里看不见，补立牌或把 `Search_H` 挪到钟架下归「天空岛交互流程优化」；「星灯」是 `Search_G` 旁的铜灯柱还是 58 米外的发光铜环机，待实机。实机清单见报告第五节（R1–R10）。

F3 全自动验收补三步（owner 要求「能自动跑的写进步骤表」）：`SKY_AUTO_NEW_GUARD_EAST`（新档先清药臼那伙，等字幕「还差一伙在风标东边」）、`SKY_AUTO_BEACONS_K1_NAME`（K1 按钮名）、`SKY_AUTO_BELL_POINTER`（目标卡与钟庭导语）；英文复看在 `SKY_AUTO_ALT_BRIEFS_WEST` / `_EAST` 各加一条断言；人工清单 2.15.38–2.15.40 与覆盖表同步，`SKY_AUTO_BEACONS_MAP` 标题改成圈双航标门。`DevAutotestResetEncounter` 重置遭遇时一并清掉本趟委托 / 清场字幕记账（只在 Dev），否则前面步骤清过的组重刷后不再出字幕。居民不在岛上、只带旧信、穿镜纹甲三类要婚姻或物证状态，步骤表造不出，仍人工。步骤表守卫 89 步 / 293 行通过，三处改错字反向检查转红；Dev 构建已部署（DLL `5444FFED…`，步骤表 `F4391ED1…`），跑完 F3 要换回正式构建。owner 当晚跑了一轮（`20261016_135926_649`，MVID 与 Dev 构建一致，81 PASS / 7 FAIL / 1 SKIP）：K1 按钮中英、钟庭目标卡 / 导语 / 按钮中文与导语英文全部 L3 通过；`SKY_AUTO_NEW_GUARD_EAST` 记 FAIL，但 Player.log 第 27991 行有「风标的守卫清了一伙，还差一伙在风标东边」（L3 日志），是步骤清完又等了约 3 秒才开始看字幕、字幕已播完，已改成清完立刻等，下一轮验证。另外 6 条新红（信鸽读信面板没开、夜里云蚋可见度、倒挂邮亭正文对比度、残星瞭台守卫重置后还没刷出、纪念品件数被测试槽历史残留撑到 7–8）与本轮改动无关，对照基线是 09-22，未处理。已换回正式构建并部署（DLL `4349F09F…`，Dev 标识全无）。归档：`FIX_TRACKER.md` 超 1500 行，2026-09-23 三节逐字移入 `archive/FIX_TRACKER_2026-09.md`。

## 2026-10-16 玩家反馈四项 + v2.3.4 发版（COMPAT / OPERATIONAL，L1/L2，未实机）

1. 汲血打死敌人不回血：官方致死顺序是 OnDead → OnHurt，命中类词缀用 `victim.IsDead` 排掉了致命一击。`AffixDefinition` 加 `ProcsOnLethalHit`，只给汲血开；灌能等其它命中词缀不变。丧尸模式「吸血」奖励同理被 `DeathSettled` 挡住，抽成 `TryApplyZombieModeTriggerLifesteal`，死亡处理里补判一次。描述与双语 Wiki 写明「打死敌人的那一下也算」。
2. 弹指可灭首波出幽灵女巫：前 20 波强力 Boss 排除表漏了 `boss_phantomwitch`；补上。另把保护窗口改成 `min(20, 启用普通 Boss 数)`，Boss 池开得少时强力 Boss 也压到最后几波（以前 ≤20 个就整段不保护）。
3. 天空岛找不到双航标门木牌：引噬风与开 K3 都在 `Search_E` 的面板里点，地图可选圈却画在噬风遭遇锚点 `POI_E`（风眼），离木牌 31 米，12 米半径罩不住。改圈 `Search_E`，噬风打过但 K3 没开时也圈；标签写「双航标门 · 在这儿引噬风 / 开中轴旧桥」，罗盘与 K3 门牌同样点名木牌。
4. 宿命回响 Boss 流血死亡卡进度：Mode G 只靠 `Health.OnDead` 记 Boss 击杀，没有轮询兜底。流血 / 毒区这类无攻击者死亡上，前面的订阅者抛异常（已知击杀提示类 Mod 不判空攻击者）就会截断多播。新增 `SweepSilentBossDeaths`：战斗 / 最后处决阶段每 0.25 秒扫已登记 Boss，已死或已销毁的交给同一条 `HandleBossDead`（`MarkKilled` 去重，改为引用判空以接住已销毁 Health）。玩家那局的具体抛异常者 UNVERIFIED，需要 Player.log。
5. 标准 BossRush / 无间炼狱同一缺口：原有 10 秒一次的 `TryFixStuckWaveIfNoBossAlive` 不会永久卡波，但它直接推波，跳过 `HandleBossDeath` 里的击杀成就、敌人回收退订、无间炼狱现金池与已击败计数，最多还要等 10 秒。新增 `SweepSilentWaveBossDeaths`（0.25 秒，排在 10 秒自检之前）：只扫本波容器（多 Boss 档 `CurrentWaveBosses`、单 Boss 档 `CurrentBoss`），已死且不在 `CountedDeadBosses` 里的交给同一条 `HandleBossDeath`；已销毁、拿不到角色的仍由 10 秒自检推波。
6. 发版：Wiki 新增 v2.3.4 中英更新日志并登记目录与总览；`info.ini` 升到 2.3.4。工坊简介 a.md / b.md 上一版贴不进去：Steamworks 说明上限 8000 是 UTF-8 字节，网页提交换行按 CRLF 计，a.md 实为 8097、b.md 8765。新增 `tools/workshop_description_budget.py` 按最坏口径（CRLF + HTML 转义）计并留 300 余量；新版删去 v2.3.2 段，a.md 7009、b.md 7623。

验证：全量守卫 745/745、全量执行回归 129/129（三个 D 盘环境变量已设；Mode G 兜底提取到 `ModeG/ModeGRuntimeModule_SilentDeathSweep.cs` 后重跑 ModeGCombat、ModeGEntryOwners 与全量守卫）、Wiki 构建通过。Windows 正式构建已部署本机游戏目录，0 错误、2 条既有 CS0649，DLL SHA-256 `70B4E71278FF243E29D16D7B4B85A03E5EF6101031DC6536B43A563433848114`，74 包哈希一致，不含 Dev 代码；`info.ini` 2.3.4 已拷进游戏目录。新增断言：汲血致命一击、小 Boss 池排序、地图圈双航标门；结构守卫：Mode G 与标准波次兜底、`MarkKilled` 引用判空。汲血回归、Mode G 两条、标准波次两条、丧尸吸血一条各做一次「改坏转红、按字节还原转绿」。同步守卫：SkyIslandContentPackGuard（圈 `Search_E`、禁止圈 `POI_E`）、ModeGDeathRoutingGuard、ModeGSpawnTransactionGuard、ZombieModeTriggerPlayerLookupSingleReadGuard、WavesArenaBossMembershipGuard。

实机清单：LS-A 手持带汲血的武器、半血一枪打死小怪，血量不涨即不合格；WA-A 弹指可灭开局连开 5 次，前 20 波出现龙皇 / 龙裔 / 幽灵女巫 / 风暴 Boss 即不合格；SK-A 两灯修好后按 M，紫圈不在栈道靠悬根林的木牌上、或圈里按不出「直面云海里的那阵风」即不合格；MG-A 宿命回响里让 Boss 流血致死，1 秒内不计击杀、不进结算即不合格；BR-A 无间炼狱里让 Boss 流血致死，1 秒内不推下一波、现金池没涨即不合格；WS-A 用新的 a.md / b.md 更新工坊描述，被拒或被截断即不合格。

## 2026-10-15 v2.3.3 发版准备（COMPAT / OPERATIONAL，L1/L2）

复查本轮五项时发现饰品槽检查照抄空 catch，全仓空 catch 969 超预算 968；三段槽位循环合成 `TryFindFilledSlot`，判定顺序与结果不变，降到 966。本机私有资源停在 10-13 早上（图标包缺 500109、物品 ID 表未登记），经 `private-sync.ps1 sync` 拉齐后两项守卫转绿。

最终验证：745/745 守卫、129/129 执行回归（AuditCombatSeptember 与 SkyIslandCombatRuntime 需设 GAME_PATH / BOSSRUSH_GAME_MANAGED / BOSSRUSH_HARMONY_DLL 后通过）、Wiki 构建通过。Windows 正式构建已部署本机游戏目录，DLL SHA-256 `252623f60293385d3283f3e42dc93ee8ad1f55694937a35b4910ba133f0c283d`，74 包哈希一致，不含 Dev 代码；`info.ini` 升到 2.3.3 并同步到游戏目录（构建脚本不复制它）。新增 Wiki v2.3.3 更新日志，工坊简介 a.md / b.md 换上 v2.3.3、移除 v2.3.1。未启动游戏，2026-10-14 条目的实机清单仍待 L3。

## 2026-10-14 玩家反馈五项（COMPAT，L1/L2，未提交、未实机）

1. 白手起家二次进不去：10-12/13 的修复（狗子背包船票放行、拦截原因提示、启动失败回滚、路牌晚注入重布置）在 2.3.2 发布之后，玩家手上的版本还没有。本轮补两处：面罩 / 耳机 / 图腾挪到武器槽之后判，只剩饰品挡路时也明说；开局武器发放失败换一把重试（最多 3 次），不再因一次抽签整局回落普通挑战。玩家口述「背包和宠物背包都空」时到底被什么拦住仍 UNVERIFIED，需要 Player.log。
2. 白手起家第一波出 Boss：随机事件「Boss 乱入」对白手起家没有波次门槛，开局 90 秒就能乱入。改为白手起家第 6 波（Mode D 自己首次出 Boss）之前不触发；标准挑战不变。另一条来源是启动失败回落普通挑战，由第 1 项的重试与既有回滚提示覆盖。
3. 自定义 NPC 好感看不见：9-23 删除每次聊天的好感横幅后，好感只在加分瞬间露面。现在捏脸 NPC、羽织、叮当走近时「聊天」选项显示「聊天（好感 Lv.3 · 120/300）」，每只 NPC 独立 key、0.25 秒节流、菜单显示中不改。
4. 进下水道陈列生命加成失效：下水道是基地子场景，官方建筑区不在其中，扫描得到空列表并覆盖陈列登记、撤掉生命加成。现在建筑区未加载时跳过扫描，保留缓存与加成。
5. 天空岛撤离后基地变黑：静态排查未找到岛上残留状态，根因 UNVERIFIED。返航后第一次基地关卡就绪时调 `DynamicGI.UpdateEnvironment()` 重算环境光，并在正式日志记录刷新前与 2 秒后的光照快照（`[SkyIsland] 返航进基地光照`）。

验证：Windows 正式编译通过（隔离 GAME_PATH，未部署），0 错误、2 条既有 CS0649，DLL SHA-256 `243fb0410baf2ee07b4c84bd01087a741e64f502e6bf47101b236ab27ba22c87`；改动相关守卫 38/39，唯一红项 SkyIslandFieldcraftGuard 是本地 `docs/reference/Bossrush使用物品ID表.md` 未登记上一提交的 500109，与本轮无关；回归 ModeDEntryOwnership、ModeEntryPaymentDelivery、BackMountainLifecycle、SkyIslandLighting、RandomEvent*、NpcAuditFixes 通过。饰品提示断言与陈列建筑区守卫各做一次「改坏转红、按字节还原转绿」。

实机清单：MD-A 背包清空、只戴一个图腾进白手起家，不出现「身上还戴着…」提示即不合格；MD-B 白手起家前 5 波出现「Boss 乱入」横幅即不合格；AF-A 走近捏脸 NPC / 羽织 / 叮当，「聊天」后缺好感、被截断、整行消失或显示 `*BossRush_Chat_xxx*` 即不合格，切语言后再走近要跟着变；SC-A 陈列 Q5+ 战利品后进下水道，生命上限下降即不合格；SI-A 天空岛撤离回基地，画面变黑时把 Player.log 里两条 `返航进基地光照` 发回来。

## 2026-10-13 甩锅许可证提交验收（COMPAT，L1/L2）

新增图腾 500109：主玩家实际佩戴后直接命中敌人挂 5 秒炸弹，当前携带者的直接命中可继续转移，转移不刷新引信；浮舟好感 3 级商店出售，售价 18000、库存 1，婚后与渡口装置沿用同一配置。保留远端已发布的 500108 回火液，同步 TypeID 台账、双语 Wiki、配置器、黑名单与验收登记。效果伤害携带物品归因，头顶对象复用并避免重复 UI 属性写入；未新增存档字段。

独立运行时与接线审查未发现生产阻塞，修正测试说明残留旧编号。745 项全量守卫、195 条许可证回归、672 条 NPC 商店回归、Windows 正式构建、Wiki 构建、80 个导航 URL 和 251 页链接检查通过。变更守卫具备 4 项接线反向验证与 6 项 HUD 反向验证，均按预期转红并恢复；正式部署 74 个资源包 SHA-256 一致，缺失的节拍器包从私有远端恢复。证据目录 `Build/pass-the-bomb-final-20261013/`；完整人工验收见 `docs/reports/testing/2026-10-13-甩锅许可证提交验收.md`。未进行 L3 实机或帧时间采样。

## 2026-10-13 推送后源码门禁兼容修复（COMPAT / SAFE，L2）

修复 CR-2026-10-13-012 / 013：三个天空岛源文件指纹检查共用仅规范换行的函数，生产读取兼容已发布原字节签名，新输出统一 LF；内容变化仍使证据过期。植物源码检查沿现有 `PARTIAL` 协议验证公开元数据与真实细化算法，完整检查继续要求私有 OBJ/MTL。未修改游戏生产代码、地图资源、发布清单或 CI 工作流。

验证覆盖 Windows CRLF、真实 Ubuntu LF、旧码失败反例、实际内容破坏/按字节恢复、本地真实植物模型与源码范围声明；公开 LF 干净副本全量 736 PASS / 9 PARTIAL / 0 FAIL，记录在 `Build/commit-20261013/source-only-final-outside-build.log`。1196 个 C# 源文件与本轮 Windows 正式编译快照全部一致，沿用下节编译、128 组回归与 74 包验证证据；这些离线证据不替代 L3。

## 2026-10-13 玩家反馈实现复审修复（COMPAT / SAFE / OPERATIONAL，L1/L2）

CR-2026-10-13-001～011 已修复：重铸、赠礼、退款和喂食复用已有库存事务，以真实属性/交付结果认账，失败归还原件；阻止重复回调发奖与未结旧欠票时再次付票；白手起家按武器实际归属确认启动。喂食消费前登记已有资产快照义务，成长接纳后落盘失败交原协调器重试；基地照料绑定实际宠物与槽位，切语言沿既有注入刷新，旧远征展示保留结算概率。强 Boss 早期排序改按过滤后的实际序列。

入口兼容加固：H 付款回执绑定选中费用数组、generation 和槽位（已对照当前 DLL 保留同一数组）；场景回调不能冒充付款，取消与换槽复用现有清理链。仅补原方法与 owner 状态，未新增生产 C# 或存档字段、未提高 19948 行宿主预算、未增加每帧扫描；005/006 的改动不扩大前一轮 SCHEMA+。

验证：全量守卫初跑 743/745，两项修复后通过（Mode H 59 项、宿主预算及重铸守卫复跑）；128/128 执行回归具备通过证据（初跑 127 通过，旧 ModeHReviewFixes 夹具补接真实退款清理方法后 70 项通过）。重铸/赠礼新增 38 项、入口支付交付 50 项、宠物照料 37 项、早期排序 11 项，均有旧码失败反例；变更守卫均做隔离破坏与字节恢复验证。Windows 按官方清单以真实程序集编译 1196 源文件通过，0 错误、2 条既有 CS0649；隔离 DLL SHA-256 `e584696a31e03de5c6454ea7819442774a51c56215ed916317f37c5129343ab7`，源码哈希全部匹配。证据在 `Build/fix-20261013/`、`Build/entry-fix-baseline/`、`Build/review-20261013-pets/`。Wiki 构建与 80 项导航检查沿用同次复审通过记录，本次未再改 Wiki 源文。

发布资源阻断已修复：从作者原件在隔离工程恢复公共图标包和节拍器包（349 图标，旧 Sprite 尺寸/PPU 不变），补齐原图/构建来源；天空岛包保持原字节，仅将旧纹理名校验换成当前 99 材质 / 919 材质槽的真实引用、BC7、尺寸、mip、可读性与白 tint 验证。发布清单未修改，74/74 包最终通过，资源属性测试与 7 类隔离反向验证通过，6 类真实包记录反例均被拒；`Build/resource-recovery-20261013/full-resource-verification-final.json` 保留完整记录。后续修改的资源守卫、文档引用检查也通过，最终 745 项守卫均有本轮通过证据。

未部署、启动游戏或访问玩家存档；没有帧耗采样，不能承诺游戏内零性能问题。REF-01 / GIFT-01 / MD-01 / MH-01 / PET-01～05 / BR-01 / PERF-01 的操作与不合格条件见 CODE_REVIEW_FINDINGS 同日条目；天空岛当次日志路径已不存在，第三方前置付款通知抛错、龙裔墙边出生点仍须现场证据，不把这些未证实线索写成已解决的玩家事故。

## 2026-10-12 玩家反馈与 Player(1).log 全面修复（COMPAT / SCHEMA+，L1/L2）

CR-2026-10-12-001～012 已修复（触发条件见 CODE_REVIEW_FINDINGS 同日条目）：天空岛兼容死亡不掉落 Mod；白手起家拦截原因可见、船票可放狗子背包、补查遗种崽背包、启动失败如实返回并回滚；鸭王杯认官方 `"Done!"` 完成标记、中止原因进正式日志、退票按真实结果提示并保留欠账；航向仪不再被误删；龙皇阶段互斥与悬空收尾、龙裔死后回调、擂台死目标；Boss 出场顺序真正打乱；无间炼狱龙系专属装备落地、现金不截断不丢余数；遗种巢风浪档不再真死。

owner 当轮拍板的新增：500108 回火液（叮当商店 4 级 + 好感 6 级赠 2 瓶，作为「新商品不进叮当商店」的唯一例外，已记入 Integration/AGENTS.md），重铸界面持有时已锁行变蓝、两步确认解除；毒蛇匕首毒爆追加目标最大生命 5%（单次 ≤400）；遗种巢行动方式（跟随默认 / 主动 / 回避）、喂食（+20 经验、10 分钟冷却）、摸摸、主人 30 米内击杀共享经验、HUD 显示经验与行动方式、伤害倍率改 0.25×√来源（0.12–0.8）、远征卡面补全收益。宠物记录新增 `behaviour` / `lastFeedUtcTicks` 两个可选字段（SCHEMA+，旧档默认值）。宠物装备推迟到单独一轮。退票实现迁出宿主 partial 后，`modbehaviour_partial_budget.json` 总行数下调到 19948。

验证：守卫全量 744 项中 743 通过，唯一红项 DailyReportArtPropertyTest 为本机 `Assets/ui/production_icons`（10-08 旧包）与清单别名不符，和本轮无关；执行回归 125/125 通过（含真实官方 DLL IL 断言 `"Done!"` 位于最终落点之后）；6 个反向探针（退票顺序、龙系预载、宠物目标写入、Done! 判据、Mode D 恒真、跟随不收手）全部转红并按字节还原。Windows 正式编译 0 错误，仅既有 Mode H 两条 CS0649；脚本尾部的 `Missing release bundle: Assets/ui/reload_metronome` 是本机缺私有资源包，DLL 已部署，`Build/` 与游戏目录 SHA-256 均为 `44E49286F2D51A9AE24097B891DDD04D1E45ACDBDD6C42A1B6AA2ED0563D11B9`。在线 Wiki 构建通过。

未启动游戏、未读写玩家存档，帧耗未采样。L3 清单 19 项（SI-ND / MD / MH / REF / VD / DK / BR / IH / PET / PERF）见本地 `docs/reports/testing/玩家反馈全面修复_2026-10-12.md`。

## 2026-10-11 最近五次提交审查的四项修复（COMPAT / SAFE，L1/L2）

CR-2026-10-11-001～004 已修复：冠军暂停保留动作，长帧分步消费完整移动时间并保留地面安全门；棍卫三档地面预警改用游戏时间，玩家默认光带保持原样；两段已闭环台账逐字存入 [2026-09 归档](archive/FIX_TRACKER_2026-09.md)，保留索引与原验证边界。新增 SandstormCombatTiming 链接真实冲锋、完整光带与棍卫招式，旧码先在 8 个场景转红，修复后通过。伤害、速度常量、招式表、颜色、网格、音效、资源及存档不变。

最终验证：744 项守卫均有本轮通过证据（Python 3.13 全量 742 通过，两项 Shapely 几何检查改用匹配现有依赖的 Python 3.12 通过）；125/125 执行回归、新增计时 22 场景/226 检查、8 项隔离反向探针与 Windows 正式编译通过。DLL SHA-256 `f5f322d10e885384552afd668358e06bf555f163196a654ed62bfb36c9724b92` 已与本机游戏目标核对，74 包哈希一致；同步了目标目录落后的 production_icons，其旧包与 DLL 已备份在 `Build/recent-fixes-20261011/deployment-backup/`。源码快照、精确归档、反向探针与部署证据在同一 Build 目录。未启动游戏或访问玩家存档，L3 仍需在冲锋中暂停/恢复、棍卫前摇中拍照/恢复，以及实地低帧率下复验。每项触发条件与修复方向见 CODE_REVIEW_FINDINGS 同日条目。

## 2026-10-11 钟守节拍器原版 Boss 掉落续作（COMPAT，L1/L2）

按 owner 已确认的范围，在 `NewWeaponBossDropHandler` 新增 `Cname_Speedy` → 500107 映射，共用 20% 额外掉率、原版库存追加与竞技场 / 无间炼狱 pending 消费；保留钟守好感 5 级、20 枚背包贝壳兑换。急冻团长和普通随从不命中。同步中英文物品说明、Wiki 源文与生成页及 repowiki；未增加每帧扫描、补丁或事件订阅。未发现新增 confirmed issue。

续作核对：中断前相关 13 项守卫、Wiki 构建已通过；掉落回归副本的全部生产输入及夹具与当前文件 SHA-256 一致，218 项断言通过，错物品 / 错掉率两个反向探针在预期断言转红并按字节恢复。补做 Windows 正式隔离编译（201 个游戏 DLL 拷贝逐个核对），仅有既存 Mode H 两条 CS0649；产物 SHA-256 为 `7D36A106B1D2DBD885C38A79E5A7EA94954712B9BF5C69D392132F4A437EE566`。249 页 Wiki 链接与 80 项导航检查通过。日志见本地 `Build/review-20261010/metronome-boss-*`、`Build/metronome-drop-probe-8e96ejg4/` 与 `Build/review-20261011/compile.log`。

未部署、未启动游戏或访问玩家存档，实际帧耗与掉落实机表现仍待 L3。人工步骤 RM-DROP-01：在原版地图击败急速团长，按游戏设置的交互键打开尸体箱，观察是否能获得单件节拍器且保留原有战利品；单次未掉落属于正常概率，重复发放或原战利品消失不合格。RM-DROP-02：将所得图腾装备，开始整匣换弹，在金色窗口再按换弹键（默认 R），观察提前结算且正常消耗弹药；切枪、卸装、死亡或切图后仍残留节拍条不合格。

## 2026-10-10 提交前最小修复（COMPAT / SAFE / OPERATIONAL，L1/L2）

CR-2026-10-10-004 / 005 已闭环：钟庭装置复用既有商店装配和本人兑换组件，补齐居民缺席入口；两个旧资源夹具共用 GPU 复制替身。新增闲置补丁 100000 轮零托管分配、不查询装备、不创建 HUD 的断言，节拍器回归 160 项、天空岛交互 955 项通过。游戏内性能未采样，不据离线结果声称无卡顿。CR-2026-10-10-006 同步修复：复用作者工程点名册原图，重打公共图标包，349 个图标校验通过。全量 744 守卫与 124 组回归均在修复和环境复跑后具备通过证据；Windows 正式隔离编译及 Wiki 构建通过，本轮未部署实机。完整验证与资源同步结果见本地 `docs/reports/reviews/2026-10-10-提交前全面审核.md`。

## 2026-10-10 节拍器赛璐璐资源与反馈（COMPAT / OPERATIONAL，L1/L2）

CR-2026-10-10-003 已修复：生产图标经 GPU 复制后归持久 prefab 持有，资源包卸载不再摧毁已有图标。按 owner 要求，经已授权的备用生图 API 重做赛璐璐弹匣摆锤图标；机械刻度、铜色高光、短拖尾与卡扣声已打入独立 Unity 包并回读验证。成功自然装填结束保留短尾光，硬取消仍立即清理；执行回归 148 项通过。构建、部署及人工验收边界见[节拍器验收记录](docs/reports/testing/2026-10-10-钟守节拍器验收.md)。

## 2026-10-10 点名册改由浮舟出售与工台兜底（COMPAT / OPERATIONAL，L1/L2）

CR-2026-10-10-001 / 002 已修复：点名册唯一卖家改为天空岛登云码头浮舟，好感 3 级、售价 20000。渡口 Search_A 原交互组追加同配置商店；婚后本人仍可买，离岛时工台仍能补给。共用商店清理同时关闭匹配的官方窗口，先退订防重入。先前小满交付记录保留为历史，当前入口以本节为准。

全量 743 项守卫、点名册 18 场景 / 256 断言、NPC 数据 666 条、婚姻 949 条、天空岛交互 949 条及商店付款/清理 18 条均通过。新增渡口回归 6 个、共用 View 清理 2 个反向探针按预期转红且按字节还原。Wiki 构建、80 项导航、247 页链接检查通过。正式 DLL 48ef1e93917465d0963b180b9d1657f0f4417d387a080310d16b5d9bd8d98328 已部署，73 个包及 13 个关键文件哈希匹配。尚未进行 L3；细节及 RC-FZ-01～05 见[修正报告](docs/reports/testing/2026-10-10-点名册浮舟商店修正.md)。

提交前再次独立核对装备与获取两条链路，未发现新增 confirmed issue。对照本机官方 Health 致死事件顺序、图腾槽位事件、同步/异步物品实例化与 StockShop.BuyTask 的扣款/背包/满包寄存顺序；确认当前唯一卖家为浮舟，工台及婚后恢复共用同一商品配置。代码与 L2 证据可提交，实际购入、装备和效果观感仍保留 RC-FZ 实机边界。

## 2026-10-09 教导主任的点名册与小满旧物摊（COMPAT / SCHEMA+ / OPERATIONAL，L1/L2）

新增图腾 500106：5 秒命中 3 名不同敌人，下一帧逐人追伤 20 + 首击实际伤害的 25%，冷却 6 秒；按需装备监听与场景清理。永久捏脸 NPC 支持可选通用商店，小满好感 3 级出售，婚后入口保留；生成图标已打入正式包，中英 Wiki 与台账接通。

CR-2026-10-09-001 / Fixed：升级横幅重复“解锁”前缀，改配置只返回商店名；旧实现先转红，修正后 NPC 回归 666 checks。点名册回归 18 场景 / 256 断言、全量 743 守卫、图标包回读及 Wiki 构建 / 80 项导航通过。Windows 正式编译和 73 包部署校验通过，DLL 哈希 18c8c172…，无 Dev 标识。已处理私有同步遗留报告冲突；实机玩法、图标与婚后购买仍需 RC-01～05 验收。详见[交付报告](docs/reports/testing/2026-10-09-点名册与小满商店交付.md)。

## 2026-10-09 冠军之影 / 星阙打磨与 v2.3.2（COMPAT，L1/L2，图标经 owner 实机确认）

- 表现：冠军之影出场、眼光、受击、转阶段、沙柱成形与死亡全套特效；星阙三档重击分层（AstralStaffFxArts）、挥击拖尾、命中四层与击杀余韵；共享 BossRushFxKit 新增 Glint / Shockwave / 锥形喷射，默认值不改既有调用。
- 玩法（owner 逐轮要求）：冲锋三阶段预判走位并跟随预警带；末阶段冲锋穿墙（PhaseTo，SetPosition 守卫改为 2 处）并轮换双生沙卷 / 绕圈泡 / 大沙暴；沙暴棍卫随机使出星阙三档重击（SandstormChampionMinionArts，翻滚免疫、隔墙不打、暖橙预警）。星阙轻击体力 7→5、两段一豆、蓄势 0.45 秒一豆，去掉 EventSystem 悬停门，松手失败有提示；12 条程序化棍势 / 打击音效（tools/gen_astral_staff_sfx.py）。
- 修复：第三方 VTModifiers 的 CA_Attack.OnStop 后缀不判空改 meleeWeapon.slashFx 缩放（IL 实查），星阙每次收招 NRE → 挂自毁占位 AstralStaffSlashStub。生产包贴图不可读，Instantiate 只拷空壳导致星阙 / 空仓地雷盒图标空白 → 星阙借用包内 Sprite，地雷盒改 ProductionIconCache.CloneTexture（GPU 复制）。
- 资源：星阙生图图标进 production_icons（IconsBuild sprites=348）；版本号 2.3.2，Wiki 新增 v2.3.2 更新日志。

验证：全量守卫 743 / 743；SandstormCombatRuntime、SandstormChampionPattern、ManualEquipmentRecovery、CampaignPlayability、EmptyMagazineMine 回归通过；SandstormChampionCombatGuard 新增断言均做反向验证；Windows 正式构建 a1984ca4… 已部署 D 盘并逐文件核对 DLL、图标包与音效 SHA-256。owner 实机确认星阙图标显示；冲锋预判、穿墙、棍卫重击、音效听感与帧耗仍待实机。

## 2026-10-08 噬魂挽歌文本与本轮交付（COMPAT / WIRE+ / OPERATIONAL，L1/L2）

CR-2026-10-08-021 已修复：噬魂挽歌（500044）接入装备工厂，文本走统一启动/语言变更注入，先于组件配置处理实际 raw key 与旧别名。资源包读回确认它是正常专属近战；反馈拼写不作为非法物品证据。28 项文本回归及 4 个行为 / 1 个守卫反向探针通过。

全量 743 / 743 守卫、122 / 122 执行回归通过。正式编译发现文本迁移后的旧缓存清理引用，移除后复跑静态缓存 / 女巫守卫与 28 项文本回归通过。Windows 正式构建成功，73 个资源包部署 SHA-256 验证通过；本轮构建、Build 副本和游戏 Mods/BossRush/BossRush.dll 的 SHA-256 均为 `DB8CA50D34BE2F861399A95C1E901BAE1463647FD443B3D94C1BFD5202CD1D4F`。响应文件无 BOSSRUSH_DEV，本轮四个生产入口已在 DLL 内核对。

共享工作区另有星阙、沙暴冠军与特效修改，正式构建在 `Build/release-sky-scythe-20261008/source` 独立副本执行，并行文件采用本轮起始 HEAD 基线；原工作区不回滚。逐文件源码哈希、排除列表和验证记录见 `Build/release-sky-scythe-20261008/source-snapshot.json` 与 `final-validation.json`。未启动游戏、访问玩家存档或取得 L3，编译/部署通过不能等同实机可玩验证。

详见[噬魂挽歌报告](docs/reports/testing/2026-10-08-噬魂挽歌内部名称显示修复.md)与[天空岛报告](docs/reports/sky-island/2026-10-08-天空岛入场与兼容修复.md)。

提交前再次全面复核：天空岛、竞技场与镰刀三项独立审查均未发现新增 confirmed issue；精确提交候选的 743 / 743 守卫、122 / 122 回归通过。独立 Windows 正式编译成功，DLL SHA-256 为 `91947F27A1104659C426AF315A54B0383C85CBEA3E0C7C4454E115975DE1F9D4`，无 Dev 符号并含四个生产入口；本次编译使用游戏 Managed 拷贝，未再次部署，前述部署记录属于上一轮。仅纳入本轮 50 个文件及三个新增编译项，共享目录中的并行修改保留。初次审核副本的路径 / 本地依赖 / 游戏程序集环境问题均按原断言复跑通过，详见[提交前复核报告](docs/reports/reviews/2026-10-08-天空岛与镰刀提交前复核.md)。

## 2026-10-08 天空岛入场与兼容恢复（COMPAT / WIRE+，L1/L2）

- CR-2026-10-08-018，P1 / Fixed：旧竞技场等待跨图迟到派发，可能在岛上误启动裸装 Mode D。由 Integration owner 绑定原场景、请求代数和角色，在切图、后继请求与销毁时取消；超时不派发，嵌套协程透传并释放。LegacyArenaEntry 53 项及模式 / 征程接线回归通过，旧码与归属 / 取消变异准确转红。
- CR-2026-10-08-019，P1 / Fixed：不支持或缺少 GBuffer 的世界 shader 原来直接阻断整趟入场。现按支持与 GBuffer 检查选择本机官方材质，岛面 / 水面优先双面 Mask，保留世界材质名、贴图与参数；全部候选不可用及碰撞缺失仍拒绝。完整生产渲染类 180 项与旧码 / 7 类变异通过。本机官方 Cull、GBuffer 及当前包 alpha / 单位 ST 已核实，GPU 等价与新增图形后端支持未承诺。
- CR-2026-10-08-020，P1 / Fixed：慢读包挤占相机激活预算、导航 owner / data 延迟以及可选表现失败均可能误返航。激活独立计时，导航真实就绪后再扫描；HUD / 灰显 / 路灯 / 音景 / 居民 / 预热隔离，失败清理，居民装置任务兜底。预热 Build 上层与 owner 取消均释放，首个失败原因在基地黑幕后提示。租约 100、婚姻 949、战斗 64 条执行验证及 9 个隔离反向探针通过。

Fixed 表示代码与 L2 闭环；最终全量检查、正式构建与部署哈希见上节。尚未取得反馈玩家当次日志或 L3，不能宣称已经确认玩家当次根因、实际画面或性能通过。证据与 SI-01～06 人工步骤见 [入场修复报告](docs/reports/sky-island/2026-10-08-天空岛入场与兼容修复.md)。

## 2026-10-08 未提交改动全面审核（COMPAT / OPERATIONAL，L1/L2）

复核 F3 清空征程、三个独立 Boss 启动注册、图鉴目录与地雷盒图标的全部未提交改动，补修四处已证实问题：

- CR-2026-10-08-014，P1 / Fixed：清空失败仍遗留默认 pending，后续可能静默落盘。改为候选 store 事务，先完成旧保存义务，失败恢复最新原快照；确认槽号再次核对。成功后清理会话及旧对白队列，界面刷新异常不会误报保存失败。
- CR-2026-10-08-015，P2 / Fixed：所谓磁盘读回实际读官方 ES3 缓存。改为显式 File 位置读回，补 EasySave3 编译引用及官方构造器的明确重载。
- CR-2026-10-08-016，P1 / Fixed：Boss 池可能在 Awake 预热，而真实配置到 Start 才装载；仅补注册并作废筛选不能完成恢复。三个 Boss 在 Awake 幂等补注册，BossFilter.OnStart 在配置加载后统一重建禁用状态、因子和目录。
- CR-2026-10-08-017，P2 / Fixed：地雷盒直接借用生产包 Sprite，包卸载会使持久 prefab 的图标失效。现由既有图标组件持有压缩 Texture/Sprite 副本，重复配置复用，缺包或复制失败使用程序化兜底，最终随 prefab 释放。

图鉴保留官方 roster 锁定卡、池内条目、自定义、丧尸与历史的去重并集，不承诺固定 52 条；本次 CodexBossCatalog 只有注释修正。地雷盒炸弹狂人 20% 额外掉落及现有玩法接线通过复核。38 个 Wiki 状态文件仅换行差异，未改正文；本地 .workbuddy 过程日志不纳入提交。

验证：全量 743 / 743 守卫、120 / 120 执行回归通过，最后的征程修改另复跑 ContentTransactions（含 9 组独立写读进程）通过；图标 21 场景 / 299 断言、实际生产包和 Wiki 构建/链接检查通过。Boss 20 项、重置 2 项、图标 4 项与生图规格 1 项反向探针均在预期判据转红后恢复。Windows Dev 隔离编译与正式编译通过，正式 DLL 与部署 SHA-256 同为 `CDA238DC477BD913F0C846A726C52547B6E1A9DB23305B1F2C322FD33C1F8D9A`，73 包哈希验证通过；14 项既有 Dev 标识及 5 项新增重置标识均完成 Dev 存在/正式不存在检查。

Fixed 表示代码与离线证据闭环；未启动游戏、访问玩家存档或取得性能采样，L3 仍需实机确认。完整根因、证据和 R01～R05 人工验收见 [审核报告](docs/reports/reviews/2026-10-08-未提交改动全面审核.md)。

## 2026-10-08 沙暴冠军系列提交前复核（SAFE / COMPAT / OPERATIONAL，L1/L2）

用户授权将本会话累计的冠军、星阙、进度保存与进图预热修复提交并推送。推送前合入远端 57a65f15 空仓地雷盒，保留双方注册、编译清单、Wiki 与回归入口；编号 500104 / 500105 均有实现，下一号 500106。修复旧装备恢复夹具缺少星阙依赖，补实际属性应用与恢复回归；星阙轻重击停顿异常增加每次运行时只记一次的诊断。最终全量 742/742 守卫、11 组相关回归、236 篇 Wiki 构建、80 项导航及正式编译部署通过，最终 DLL 哈希一致。新版本视觉与真实游戏存取仍保留 L3 验收边界。详见 [提交前复核](docs/reports/testing/2026-10-08-沙暴冠军提交前复核.md)。

## 2026-10-08 沙尘浓度、内向泡泡与沙龙轮廓（COMPAT，L1/L2）

修正砂云顶部淡出误用 Unity SmoothStep 输出区间，导致整个体积的浓度系数不超过 0.17 的问题；主体恢复满浓度，上下边界渐隐。二阶段 31 泡按实际 Boss / 玩家位置朝内发射，包含长帧补发。最新日志确认旧版本至少有一只沙鲨被武器击破，不能称完全停喷；表现补回砂质躯干、双翼和摆尾，死亡停止刷新。241 项战斗回归、188 项节奏断言、专项守卫和 6 项反向探针通过，Windows 正式编译部署成功；新画面仍待 owner 实机。变更相关守卫 227/228，遗留 SkyIslandFieldcraftGuard 为私有物品表 ID 不一致。详见 [本轮报告](docs/reports/testing/2026-10-08-沙尘恢复与内向弹幕修复.md)。

## 2026-10-08 用户截图反馈后的砂云表现重做（COMPAT，L1/L2）

用户截图显示沙柱仍像发光线圈，Boss 头顶呈线框皇冠，前次美术改进未达到用户要求。本轮删除装饰螺旋、线框皇冠、泡泡轨道和沙鲨折线，使用固定容量砂云体积、上升砂晶、短砂雾尾迹和薄膜弹幕；地面危险圈使用纹理砂痕。参考 Riot 官方 VFX 资料及 Unity The Heretic 粒子角色方向。运行机制和判定范围沿用现有代码，表现组件随 owner 停止刷新并自然回收。新构建的画面品质仍待实机，不能把守卫或编译记为美术验收通过。详见 [砂云重做报告](docs/reports/testing/2026-10-08-沙暴冠军砂云表现重做.md)。

## 2026-10-08 实档章节缺失、沙圈出生点与末阶段调度（COMPAT，L1/L2）

- CR-2026-10-08-008，P1 / Fixed：授权只读的实际征程 JSON 缺少整个 chapters 字段，前五章交付 token 仍在。以共享显式 JSON 编解码替换 Unity DTO 序列化，按持久化交付凭据兼容恢复，不重新发奖。
- CR-2026-10-08-009，P2 / Fixed：双生沙圈把 target 作为出生位置，直接落在玩家两侧。现统一使用 Boss 发射点贴地，并保持接触或到时凝柱。

末阶段停在首次换侧为日志确认的症状，宿主触发点尚未复现；局部改为 Update 驱动嵌套招式及定时等待，隔离回归验证宿主停止协程不会中断主攻击。泡泡官方 Hurt / HealthSimpleBase / OnShotDown 接线执行回归通过，外观更新为空心薄弧及细砂裂光。证据边界及人工验收见 [本轮报告](docs/reports/testing/2026-10-08-实档章节恢复与沙暴战斗修复.md)。没有离线修改玩家存档，未启动游戏。

## 2026-10-08 追踪双圈、沙暴翻倍与沙卫提速（COMPAT / OPERATIONAL，L1/L2）

双生沙暴改为贴地追踪圈，接触或 3 秒到期才凝柱；墙体阻挡不提前引爆。小/大柱半径 3.4/5.6 米、高 12/24 米，地圈与伤害同步；沙卫移速 6.2、寻路间隔 0.16 秒、首次出手 0.2 秒。表现改分层翻卷沙云、渐细短流、薄冠云与细金砂。用户要求本轮不开子代理，已独立完成。

正式构建与部署 SHA-256：`65265C783B3E89A885A433076013A9C3F32E214F97A93390C2507A8E07F2A3A3`。全量守卫 740 项初绿、台账索引修复后剩余 1 项复验绿；沙暴 172 断言、6 项反向验证、招式回归 188 断言及 Wiki 构建/导航通过。真实地形与视觉待 owner 复测，见 [本轮报告](docs/reports/testing/2026-10-08-追踪沙圈与沙暴表现优化.md)。

## 2026-10-08 征程加载时机、猪鲨循环与星阙表现（COMPAT / OPERATIONAL，L1/L2）

征程首次读档改为天空岛序章同样的关卡就绪时机，保留已加载快照的退出补盘；修复召卫清沙暴、禁止喷弹，以及星阙无实际伤害仍触发强反馈。冠军补完整主要三阶段猪鲨循环与出界激怒，50% / 15% 转阶段，冲速提升、旋风加高且弹体可破；沙卫使用沙尘精外形与星阙、无掉落。星阙三档加入收束符轮、细光轨和分档音效。

Windows 正式构建与部署 SHA-256：`54DB13829D47F1D6D4BDC62CF51A8FDEDCDB427B3CD6B2EDD9928918C2B0217A`。全量守卫 741/741，章节/引导独立进程恢复、188 条生产招式调度断言、专项守卫及反向验证通过；当前实机未运行。详见 [交付证据与步骤](docs/reports/testing/2026-10-08-征程重启进度与猪鲨机制升级.md)。

## 2026-10-08 沙暴冠军站桩、纯弹幕与持棍沙卫（COMPAT / OPERATIONAL，L1/L2）

用户实机反馈站桩。局部修复原 AI Graph 未真正停止、逐帧 SetPosition 重置移动与过严导航拒绝链；连续移动改官方驱动，原武器与技能移除，补五连冲/三连冲/末阶段一二三连冲、旋风弹幕和无掉落星阙棍卫。具体导致本次站桩的物理条件日志未记录，修复后 L3 尚待 owner 复测。

739 / 739 守卫、112 条新接线断言、5 组反向验证、征程与动态物品两组执行回归通过；Windows 正式编译与部署 SHA-256 一致：`0DDC0CA900DA6D56503A16BB32C7C21C8B7A370D60F4E94F9562181D971181A2`。详见 [本轮报告与实机步骤](docs/reports/testing/2026-10-08-沙暴冠军站桩与召唤修复.md)。

## 2026-10-08 进图预热、沙暴冠军与星阙（COMPAT / OPERATIONAL，L1/L2）

普通竞技场删除无条件 Mode E 预热；焚皇戟改主玩家手持后异步载包与分帧扩池，补离手/死亡/切图/停用取消。重做第六章冠军为三阶段沙暴精怪，沿用任务和图鉴稳定身份；星阙 500104 走冠军必掉、纯代码光棍、三段蓄豆重击，补完注册/黑名单/双语 Wiki/覆盖清单。

全量守卫 738 / 738、8 组相关回归、隔离反向探针、Windows 正式构建与部署哈希、Wiki 构建及 80 项导航检查通过。最终 DLL SHA-256：`ED693DB8EC85EA0429582514F92355CBAB7ACAAF8FD993F98B2BB88DD35DD9AC`。本轮没有找到前次 1607.5 ms 原始竞技场日志，未做 L3，因此不宣称实测提速、视觉达标或无性能问题。详见 [交付与人工验收](docs/reports/testing/2026-10-08-沙暴冠军与星阙交付.md)。

## 2026-10-08 空仓地雷盒全面复审与提交（COMPAT / SAFE，L1/L2）

复审修正 CR-2026-10-08-002～004：旧回归夹具补链接物品 ID；地雷在 SceneLoader 开始加载时立即取消，并拒绝渐变期新布雷及后续目标结算；中英简介、掉落专题和切图说明同步。地雷生产链接回归扩至 17 场景 / 223 断言，旧代码先在切图窗口断言失败后修复。

最终全量守卫 738 / 738、全部执行回归 118 / 118、Windows 正式编译、Wiki 构建和 80 项导航检查通过；构建/部署 DLL SHA-256 为 `3DB958F02482A8CA3D983622FAA2E68139576765D6E41DCD2CF5F10A9532A897`。未发现剩余 confirmed issue；未运行 L3。完整证据见 [全面复审记录](docs/reports/reviews/2026-10-08-空仓地雷盒全面复审.md)。

## 2026-10-08 空仓地雷盒与炸弹狂人专属掉落（COMPAT，L1/L2）

新增图腾 500105，炸弹狂人 20% 专属额外掉落，沿用原版尸体箱、BossRush 奖励箱和无间炼狱落地通道。同一弹匣至少开火 6 次后打空布雷，1.5 秒引信、4 米/160 基础物理伤害、6 秒冷却；提前换弹/切枪清计数，卸下/死亡/切图取消。保留本地台账先登记的星阙 500104。

实现期审查 CR-2026-10-08-001 已修正：程序化图标资源归动态 prefab 组件持有，避免宿主热重载销毁存活物品的共享图标。380 项相关守卫、新增 13 场景/171 断言及 3 项现有执行回归、Wiki 构建、Windows 正式编译通过；本地部署 DLL 哈希一致。L3 待验，详见 [交付与验收记录](docs/reports/testing/2026-10-08-空仓地雷盒交付与验收.md)。

## 2026-10-07 日志复现的报名石位置与女巫误清场（COMPAT，L1/L2）

用户在上一轮交付后提供 `Player.log`：报名石早于 BossRush 二次传送生成；女巫已成功生成，随后被普通开波前清场删除，征程自动收尾后又生成石头。新增 CR-017/018；上轮官方 AfterInit 回归没有覆盖 Mod 延迟入口与持续清场的组合，不能作为此路径通过的证据。

按用户指出的路牌/垃圾桶流程复用真实路牌 owner：征程等待当前场景的可用路牌实际出现，再按路牌位置投影地面生成报名石。直接清场与持续清场均检查终章占场，覆盖独白及未认领的工厂在途角色。持续清场只暂停，失败后原协程可恢复，决战期间不扫描也不消耗循环预算。当前执行验证及实机步骤见 [日志复现报告](docs/reports/reviews/2026-10-07-报名石位置与冠军女巫误清场.md)。没有启动游戏或访问玩家存档。

验证：相关守卫 30 / 30，全部执行回归 117 / 117；CampaignPlayability 225 checks、ArenaHostRemainder 661 assertions，旧源码与隔离变异准确转红后按字节还原。Windows 正式构建和 73 包部署验证通过，14 个 Dev 标识不存在，构建/部署 DLL SHA-256 为 `AC2194F9D0313F342C4CDF65292AFADDDC3067AA0D68E8C89F218DEBC3F86CD7`。全量守卫 737 / 738，剩余 SkyIslandFieldcraftGuard 因本次未改动的私有物品表 500104 与公开上限 500103 不一致；没有改台账或放宽断言。真实地图位置、女巫接战与导航仍待 LOG-01～03，不能将部署当作实机通过。

## 2026-10-07 征程直接关闭与第六章报名石复核（COMPAT，L1/L2）

用户仍复现直接关闭游戏后征程清零，关闭前第六章已接取。新增修复 CR-2026-10-07-015/016：官方采集消费征程 pending 后由成功回读通知原协调器保留物理保存义务；终章场景缓存同时观察 active scene 句柄，否定结果限频重试，并等待官方 AfterInit 最终搬人结束才在出生点旁创建石头。保存 key、schema、章节与奖励保持兼容。第六章已接取独立新进程文件恢复和真实石头 Tick/Create 回归覆盖，完整 16 项当前代码结论及本次 RQ-01～03 实机步骤见 [本轮报告](docs/reports/reviews/2026-10-07-征程重启与终章石复核.md)。

本次没有启动游戏或访问玩家存档；已丢历史进度不推定恢复。验证与交付哈希以本轮报告的最终记录为准，不能用下面历史制品的哈希代替。

验证：全量守卫 736 / 736，最终 changed-only 36 / 36；执行回归覆盖 117 / 117（首轮一个夹具缺本机变量，补环境后通过）。最终征程用例 214 checks、事务用例 501 assertions 加 6 组独立进程，9 项隔离反向探针均准确转红并按字节还原。Windows 正式构建、73 包部署校验通过；构建／部署 DLL SHA-256 `843E26268E07195D423807F04D259E45EDC8DE548AFE0E87F27B466C02AD4509` 一致。L3 待 RQ-01～03。

## 2026-10-07 远端合并与全部改动复审收尾（COMPAT / WIRE+ / OPERATIONAL，L1/L2）

已按用户后续授权快进合并远端 `f0711934`；本地未提交修改先备份并 stash，7 处重叠逐项整合，保留不重叠本地内容。未创建或推送公开提交。完整逐项结论、代码／资源／未证实反馈分类、反向证据与人工步骤见 [合并复审报告](docs/reports/reviews/2026-10-07-全部改动独立深度复审.md)。

保留远端 CR-2026-10-07-001～005，本地追加 006～014；共 14 项（9 P1 / 5 P2）。补齐天空岛激活栅栏与失败返航收尾、Mode E 龙裔碰撞误伤、押注键级故障恢复、归巢身份与失败待办、护卫龙裔当前生命，以及普通 fallback Boss 死亡图鉴身份。重复极值支付问题归入 001，最终采用先 Pay、明确拒付且余额未变才允许的窄回退。远端的巡守 inactive 查询、女巫死亡顺序、官方物理保存 saving 闩和终章独白独立取消均重新对照现码／官方 API 并执行验证。

验证：合并结果全量守卫 **736 / 736**（无红项白名单），一次完整执行回归 **117 / 117**；Windows 正式编译 1148 源文件成功，73 个资源包部署 SHA-256 验证通过，14 个 Dev 专用标识均不存在。源与实际部署 DLL SHA-256 为 `3A378E05F60EDB9107FDD225450EBD9D2A1788907AFD4C677CFAA7716BC0F034`。最终生产源码哈希与验证起点一致，证据在 `Build/deep-review-20261007/`。

资源现状：当前 skyisland_fx 的三件搜刮箱网格齐全，源／部署 SHA-256 均为 `1699BADD7F674CF81B51D24AE28483F4C13E513C30B9FC2E11EFBA9C12FD67E1`。资源由外部工作补齐，本轮实际验证并通过正式构建同步，没有放宽原资源守卫；旧 734 / 735 是历史候选记录。

补强回归：真实 inactive 子树、实际保存订阅副作用、黑幕引用计数、原生加载激活阻塞与释放、保存失败保留 saving 的宿主语义；共享引擎和 H 独立协调器都不再用手工清闩代替恢复。荆棘三档使用 finalDamage、0.35 秒冷却和反伤归属新增生产分发测试，数值未改。所有新确定缺陷均保留旧代码转红证据，修改守卫已反向验证并还原。

所有地图与模式允许携崽，仅 H（含待入场）禁止；Boss 生命比例仍为 10%～200%、默认 100%、只作用于新 Boss；押注上限、只扣本金和冻结倍率保持。未启动游戏、访问玩家存档或采样性能。实际模型、AI、黑幕、完整重启、第三方 Mod 与 GPU／帧耗仍需报告中的 L3 验收。

## 2026-10-07 最新提交独立深度复审（COMPAT / WIRE+ / OPERATIONAL，L1/L2）

对应 CR-2026-10-07-001～005，当前基准 `af9bcb43`。完整 16 项复审、根因、生产路径、验证边界与 R01～R18 实机步骤见 [本轮报告](docs/reports/reviews/2026-10-07-最新提交独立深度复审.md)。旧 10-06 报告本机缺失，以台账为线索重新查代码，没有沿用已修复结论。

本轮最小修复：inactive 巡守查询包含官方子 AI；女巫在实例死亡清理前向图鉴提交稳定身份，三独立 Boss 副本名统一既有清理约定；共享内容和 H 的同步写盘失败释放自己留下的官方 saving 闩并保留异常 / 待办；终章独白独立取消归还当前召唤权；大额合法押注绕过官方账户加现金溢出的错误预检。没有新增生产文件、常驻扫描、存档字段或提高宿主预算。

五项均补实际调用生产逻辑的失败回归，旧实现隔离实跑转红，修改守卫均反向验证并按字节还原；Mode D 另补真实库存 / 装备 / 狗子包遍历，旧 868 回退实际转红。全量守卫 736 / 736；回归最终覆盖 117 / 117（第二遍全量 116 绿，修正共享替身后失败组与受影响组分别复跑通过，逐次记录见报告）。首轮遗漏 GAME_PATH、新日志边界未补和第二轮共用替身声明冲突均已修正，不记为游戏缺陷。Windows 正式编译成功，源 / 部署 DLL SHA-256 `CB1DCB32458ACAA87C9366A04910F1261B3FC4DB435F20D11237491C4752B205` 一致，14 个 Dev 标识均不存在，73 个资源包部署哈希验证通过。

旧资源红项重新核实：源 / 部署 skyisland_fx 均含三件 LootCrate 网格且 SHA-256 一致，本轮未重打包、未放宽守卫，CR-2026-10-06-012 当前资源缺口不成立。历史 734 / 735 数字仅保留为当时记录。

所有游戏地图 / 模式允许携崽，仅 H 及待入场 H 禁止；基地禁战与出击新实体隔离；押注公式、只扣本金和冻结倍率保持；Boss 生命 10%～200%、默认 100%、只影响新 Boss 均复核。官方独立保存与持续 IO 故障仍不在新增局部保存保护范围，旧进度是否已丢未访问玩家档核实。未启动游戏、访问玩家存档、提交 / 推送公开代码；模型、加载、导航、技能、第三方 Mod 和帧耗仍待 L3，不宣称无性能问题。

## 2026-10-06 第二轮深审完成（COMPAT / SCHEMA+ / WIRE+ / OPERATIONAL，L1/L2）

对应 CR-2026-10-06-013～018，已更新 [完整 17 项报告](docs/reports/reviews/2026-10-06-用户反馈审核与修复.md)。重点修复真实订阅导致的征程故障恢复丢进度与 Jeff 默认投影、inactive staging 导致基地禁战没执行、地图费用旧船票回退、晴岚加载黑幕引用计数未归还与早期取消阻塞、大额押注溢出和重进赔付漂移。

用户明确的新范围已落实：全部游戏地图／模式允许携崽，仅鸭王杯 H 禁止；出击新实体正常 AI／伤害，与基地非战斗实体分离，主人换队、死亡和切图有对称处理。押注取消固定 20,000 上限，按已有余额÷真实返还倍率限定本金，锁盘只扣本金并复验；赔付分数与胜负结果为兼容可选字段。天空岛归航钟流程不生成女巫；本机 shader 包合格，旧包缺 GBuffer 会在入场明确拒绝并正常恢复黑幕。

验证：117 / 117 全量执行回归；734 / 735 全量守卫（剩余 CR-012 既有资源缺口，未放宽）；Windows 正式编译和部署哈希一致，DLL `0F109A2031B81F4EA376AF862F72AEE86624DD44A851E46B61C6348F4B7F883C`，Dev 标识全部不存在。新测试包含独立进程真实文件恢复、完整宠物激活、现装 DLL 加载／黑幕契约、实际 Harmony 替换、金额边界与反向验证。源码清单 1148，宿主 partial 仍 58 文件／20059 行，没有增加生产文件或提高预算。

未启动游戏、未读取或修改玩家存档；实机 UF-01～18 与帧耗采样仍待完成，已丢历史进度不能凭修复推断恢复。首轮的未复现结论与宠物／押注限制仅作为历史保留，以本节和当前报告为准。

## 2026-10-06 玩家 16 项反馈审核与修复（COMPAT / SCHEMA+ / OPERATIONAL，L1/L2）

对应 CR-2026-10-06-001～012，逐项真实性与 UF-01～15 验收见 [审核报告](docs/reports/reviews/2026-10-06-用户反馈审核与修复.md)。代码修复覆盖丧尸 Boss 接战、普通地图带崽与基地禁战、晴岚／鸭王杯导航等待、终章记名石、船票回退、独立 Boss 目录与女巫预设恢复、图鉴身份、Mode E 友军／技能目标。

词缀统一显示 Ⅰ / Ⅱ / Ⅲ，旧装备只按当前查看物品刷新名称；自选押注 0–20,000、100 步长并显示返还／净收益，沿用既有赔率、抽水与锁盘账本。新增 Boss 生命 10%–200% 滑条，默认100%，仅新 Boss 消费；与装备和波次加成正确叠加，普通敌人和宠物不受影响。新生产文件只有共享 BossRushUISlider，已登记正式编译；原丧尸休息滑条复用它。原类型查找下沉现有 ModConfigAPI，宿主预算未提高。

验证完成：相关守卫反向验证、115 / 115 执行回归、Windows 正式编译（1148 源文件）及部署哈希核对，Dev 专用标识不存在。完整 DLL SHA-256：`1875537FA67C6208C5C9A6F310D88E2EAED5EFE5EF11F617E7266E3B97E2F93B`。全量守卫为734 / 735，剩余既有 skyisland_fx 搜刮箱网格缺失，登记 CR-012 待资源处理，未改白名单。

未声称修复／实机证实：正常保存后征程重启清空（隔离回读正常）、未知地图的敲钟女巫、第三方随机敌人 Mod 的全面兼容性与零性能问题。荆棘仍按防御后的 finalDamage ×10/18/28% 反弹；未接天空岛序章不应生成任务守卫。未启动游戏或访问玩家存档，代码未提交或推送公开仓库。

## 2026-10-01 天空岛实机反馈七项（第二批）：完成面板、序章前入口、Boss 备弹、贝壳签筒、数值减半、岛上白天户外后处理、静默门 5 m（COMPAT，L1/L2）

- 序章交付完成面板一开就关（根因已查实，L1）：官方 `QuestCompletePanel` 逐帧等「奖励全已领取 / 点跳过」，投影奖励的 `Claimed` 读交付事实、交付那一拍已为真。`OfficialQuestCompletePatch` 加 Postfix，只把刚完成的那份实例的奖励行先显示成未领取，官方 `Claim` 收起；读档重建照旧已领取。征程任务同受益。
- 序章完成前基地船点仍有天空岛选项：`SkyIslandPreludeFlow.routeUnlocked` 只在开门面时读一次，F3 清空进度后缓存不刷新（L1 推断为实测成因）；改为每拍按剧情事实重读。「航路已开放」横幅改为只在 Jeff 交付那一拍置位、挂入口时念一次，开游戏进基地不再重念。
- Boss 有枪没子弹（L1）：`SkyIslandEnemyArmory` 只给换上的枪放备弹，达标留下的官方原装靠官方 `AddBullet`（按 Boss 预设弹药品质表抽，不少口径抽不到）。新增 `EnsureAmmo`：主 / 副武器每把枪补到三匣、至少 45 发（`ReserveRounds`），弹种先沿用已装 / 已带的。
- 贝壳签筒（新内容）：码头与风铃集航路图交互组各加小签（3 贝壳，航务补给档 q2–5）/ 大签（10 贝壳，星工遗存档 q4–8），走搜刮箱同一套池与合成台同一套扣料事务；不新增世界交互体。贝壳来源是保留官方 Boss 掉落表的头目 / 岛主 / 具名对手 / 噬风。
- 天空岛 Boss 数值减半：`SkyIslandCombatBalance.Multiplier` 3 → 1.5（感知 1.5 不变）；序章守卫 750、噬风 3000。
- 「视野外白天和黑夜一样、烟雾太浓」（根因已查实，离线读官方场景）：租约克隆的地堡 `TimeOfDayConfig` 晴天条目白天 / 黎明 / 夜里全用室内 `BaseVolume`，远景雾纯黑。新 `SkyIslandOutdoorDaylight` 在副本里把白天、黎明换成官方 `Volume_Level_GroundZero_Morning`（浅蓝远景雾），夜里与基地原件不动。一度试过调官方视野迷雾 `FogOfWarWorld` 的不透明度，查实不是成因后撤回。
- 「附近有威胁」静默门 35 m → 5 m（剧情面板、对话、交任务、航徽、云蚋散群同一道门）；巡守摆放净空与写盘半径不变。
- 证据（L2）：全量守卫 735/735；执行回归全量 113/113（三变量）；`JeffQuestFlow` 新增完成面板断言、`SkyIslandPlayerEntryGuard` 公告一次、`SkyIslandEnemyArmoryGuard` 补弹、`SkyIslandLifecycleGuard` 与 `SkyIslandRaidLease` 执行回归的户外后处理（只换副本）均反向验证转红后按字节还原；正式编译部署 `BossRush.dll` 1fbec4a1…（游戏目录同 sha）；Wiki 地图页 / 更新日志中英同步，站点重建通过。
- 未实机（L3）：Jeff 交付后完成面板停住、点「全部领取」或跳过才收；F3 清空进度后船点无「前往天空岛」、Jeff 重新挂「云上的坐标」；进基地横幅只在刚交付后出现一次；头目打空弹匣后能换弹继续开火（Player.log 无 `[SkyIslandArmory] 这把枪找不到可用弹药`）；航路图滚轮有两支签、贝壳不够有提示、背包满不扣贝壳；岛上白天远处不再压成一片黑、呈浅蓝远景雾（Player.log `DAYLIGHT_VOLUME replaced=` 大于 0），夜里观感不变；贴身 5 m 外的敌人不再挡对话。

## 2026-10-01 收尾：天空岛 CR-2026-09-30-005～012 与审查遗留 P3 / 复用全部修复（COMPAT / WIRE+ / OPERATIONAL，L2）

原 09-30 审查报告已不在本机与私有仓库，三路子代理按摘要在当前代码里重新定位，8 条 CR 与审查遗留 4 处全部成立并修复。

- CR-005～012：见 CODE_REVIEW_FINDINGS（撤离提前写盘同批存角色资产；婚后恢复补每日衰减；租约放下官方卡住的加载忙标志；帧时间跨终点长帧计入；HUD 恢复后过期警示即收；自动验收总状态并入套件红项；Shader 检查自动探测游戏路径；循环音不依赖发声体存活也能停）。
- 同日更正 CR-003 描述：官方死亡处理（丢物、存角色、写盘）先于会话回调执行，`OnPlayerDied` 是紧接其后立即结算写盘，不是「之前」。
- P3 性能：划地为营玩家血条与鸭王杯群战队伍后缀在名字 / 阵营 / 语言不变时复用缓存串；快递员首次见面只在走近时查存档且记住已见面；龙皇铳子弹补丁在没有龙皇铳子弹在飞时第一句早返（全场子弹每帧少一次 GetComponent）。
- 复用：`Integration/Items` 下 4 份私有 `SetHiddenMember` 改用 `ModeFItemConfigHelper.SetHiddenMember`（静态证明等价；`ModeE/RespawnItemConfig.cs` 那份带 try/日志，行为不同，未动）。
- 证据（L2）：全量守卫 735/735；执行回归全量 113/113；新增 / 扩展守卫 `ModeHGroupTeamTagsNoRebuildGuard`、`CourierFirstMeetSaveQueryGuard`、`DragonKingBossGunProjectilePatchEarlyOutGuard`、`ModeEHealthBarNameCacheGuard`、`PermanentSpouseRestoreLifecycleGuard`、`SkyIslandHudGuard`、`F3AutotestOrchestratorGuard`、`WindowsPathDetectionGuard`、`AssetSnapshotBoundaryGuard`、`SkyIslandContentPackGuard` 与对应执行回归均做反向验证；Dev 与正式构建都编过，正式 DLL `49336682` 已部署，`check_dll_identifiers --expect absent` PASS；`verify_sky_island_bundle_shaders.py` 不设变量实跑 PASS。
- 未实机（L3）：撤离结算页强退后灯与材料一致；婚后多日不互动好感下降；返航加载异常后约 2 秒重试（日志 `RAID_LOADER_FLAG_RELEASED`）；开地图 5 秒再关不再续播旧警示；回基地后听不到岛上风声；Mode E / 鸭王杯血条名字显示不变；快递员首次见面照常；龙皇铳各弹种照常。

## 2026-10-01 发布前外部审查：6 条 P1 + 11 条 P2/P3 全部核实成立并修复（COMPAT / SCHEMA+，L2）

外部审查（只读、未编译未实机）列的每条都先对照当前代码与官方反编译源核实，全部成立，按最小改法修。三路并行：本会话（天空岛 / 永久 NPC）+ 两个子代理（ModeH / 无间炼狱 / 划地为营 / 重铸 / 好感；丧尸 / 遗种巢 / 武器 / 龙王）。

- P1 永久 NPC 与天空岛居民头顶名字只登记一次（不跟随、隐藏不收、销毁不注销）：`DuckNpcRuntimeMarker` 加 LateUpdate 刷新、OnDisable 收起、OnDestroy 注销（口径同快递员 / 护士）。
- P1 群战重打沿用押注不记押哪队：押注记录加可选 `betSide`（旧档缺省 0，SCHEMA+）；沿用时恢复当初那一边、收起选边与「换一批」、倍率显示押注那档（`ModeHCashBetService` / `ModeHRuntimeModule_GroupFlow` / `_BetFlow`，GroupFlow 超行数把四个小方法挪进 BetFlow）。
- P1 无间炼狱本波现金跨局残留：复位块补 `InfiniteHellWaveCashThisWave = 0L`。
- P1 CR-2026-09-30-002 / 003 / 004：见 CODE_REVIEW_FINDINGS（回主菜单不再保留暂不入档记录；倒下紧接官方死亡存档立即结算写盘；折翎 / 钟守婚后跟随能走）。
- P2 划地为营 / 随机事件神秘商人 999999 生命没生效：`GetComponent<Item>()` 改 `character.CharacterItem`（新守卫 `ModeEMerchantHealthGuard`）。
- P2 天空岛巡守离居民太近：选点净空居民 35 m、码头 45 m（与剧情面板 / 写盘安静半径同口径），重生成 176 个槽位（钟庭合格位只剩 9 个，20 → 9），派生落点与场景审计同步（缺陷仍是 23 条设计接触）。
- P2 尸潮信标 / 便携安全区 / 尸潮邀请函悬停弹通知：三个 `CanBeUsed` 只返回结果，原因留在模式侧 Try 路径。
- P2 丧尸撤离销毁 1 秒内刚捡的物品：整表销毁前 `ReleaseZombieModeOwnedDropCandidates()` 先把已在背包 / 槽位里的候选移出。
- P2 遗种巢远征奖励发不出时每 5 秒整档写盘：只有真正到账才提交落盘，诊断计数封顶 6 次；存盘失败码补 6 条中英词条（守卫自动扫描失败码）。
- P2 好感度在官方采集事件里自己 `SaveFile`（`.bac` 存半成品）：去掉，数据随官方紧随其后的写盘落地。
- P2 焚皇断界戟跳斩落地 / 爆燃补 `isFromBuffOrEffect`；龙王、龙裔补敌对性安全网（豁免遗种巢随从与 Mode E/F）。
- P2 群战名人堂净赚只算最后一次会话：账本加按季累计字段（SCHEMA+，schemaVersion 不变，已登记 `docs/contracts.md`）。
- P2 重铸冷淬液：先 `LockProperty` 成功再扣，扣不掉撤回锁定。
- P3 霜之哀伤 / 噬魂挽歌 / 焚皇戟回退刀光每刀全内存扫描：照新武器加 5 秒重扫节流。
- 证据（L2）：全量守卫 732/732；执行回归全量 113/113（设 GAME_PATH 等三变量）；新守卫 `ReleaseReviewNpcAndDepartureGuard`（7 条内存反向探针）与各子路新增断言均做反向验证；正式编译部署见提交说明；Wiki 鸭王杯页与天空岛页、站点重建。
- 未实机（L3）：各条实机判据见对应子路报告摘要——NPC 名牌随人走、回主菜单不留灯、婚后折翎跟随、群战重打选边锁定、名人堂分次净赚、无间炼狱首波现金、商人打不死、重铸扣料、背包悬停无通知、撤离末秒拾取保留、焚皇技能不触发词缀、龙王仍敌对、刀光不缺。

## 2026-10-01 天空岛实机反馈七项：全岛官方 Boss、属性 ×3、加密巡守、搜刮箱、居民中立、码头 BGM（COMPAT / OPERATIONAL，L1/L2）

- 有枪不开（根因已查实）：刷怪底模按「阵营 scav、不是 Boss」收全部 preset，官方快照里 33 个只有一半是持枪拾荒者；序章「断风游猎·守」取名字排最前的，正好是近战的 `EnemyPreset_BossMelee_SchoolBully_Child`。开不开枪由 preset 私有 `aiController` 决定，与手里的枪无关。
- owner 拍板「天空岛全部用 Boss 预设」：新 `SkyIslandEnemySources`（头目 / 岛主 / 具名对手 / 噬风 / 序章克隆各自参照的官方 Boss；小兵、精英、巡守按点位散列到官方 Boss 池，排除 `_NPC_`、口口口口、`_Test`、载具、中立 / 玩家阵营、非 `EnemyPreset_` 前缀）；新 `SkyIslandMinionKit`（小兵 / 精英 / 巡守留 Boss 行头，杂项掉落、经验、现金换岛屿拾荒者口径，`isBoss` 关掉免得刷爆战役「击杀 Boss」、日报与 Rogue 结算）；`SkyIslandEnemyArmory` 空主武器槽不补枪（近战 Boss）。巡守岛区鸭模外观 `SkyIslandPatrolAppearance` 删除，头顶是 Boss 自己的名字。
- 属性：`SkyIslandCombatBalance.Multiplier` 1.5 → 3（生命、伤害、反应、开火前摇、散布、暴击）；新 `PerceptionMultiplier = 1.5`（移速、弹速、射程、视野、听觉、夜视，乘 3 会在一屏外开火）；小兵、精英、巡守按抽到的 Boss 原版算（owner 选「全按各自 Boss 原版 ×3」，巡守再乘岛区系数）；巡守的反应 / 开火前摇不再被岛区档案覆盖成 0.5–1.2 秒；序章守卫额外 ×2 生命（1500）。
- 密度与落点（子代理）：巡守 112 → 187、ActiveLimit 24 → 32、挂起半径 110 → 95 m；选点离岛缘 ≥12 / 10 m、离路网与地标 ≤20 m、离导航边界 ≥2 m（旧槽位 67 个离岛缘 <5 m）；派生 `runtime_placements.json` 与场景审计同步。
- 搜刮箱（子代理）：只对搜刮点改交互名「搜集 / Search」（反射写 `displayNameKey`，库存标题同名）并去掉「搬起」；每箱 3–7 格（生活 3–4、航务 4–6 / 富裕岛区 5–7、星工 6–7），第一格保底一堆岛上特产，其余按类别牌发物资 / 子弹 / 武器 / 装备 / 杂项，子弹按堆；12 个类别池进预热。一趟满搜约 200 格（旧约 92 件），经济约 ×2。
- 居民在岛上改 `Teams.middle`（敌人隔桥锁打不死的居民）；背景音乐只在登云码头放（按脚下岛区获取 / 释放场景曲租约）。
- 证据（L1/L2）：全量守卫 730/730；天空岛执行回归 16/16、F3 回归 3/3（设 GAME_PATH 等三个变量）；新回归 `EnemySourcesRegression`（Boss 池口径、参照克隆、小兵换掉落）与守卫改动均做反向验证；正式编译部署（最终 DLL 见下一条，构建与游戏目录 SHA 一致），`Patrols.json` 部署 SHA 一致；Wiki 站点重建。
- F3「NPC/剧情」页新增「清空天空岛剧情/任务」（只在 Dev 构建）：`SkyIslandProgressDebugControls`（宿主 partial 预算已满，挂在 `CampaignPetNestDebugControls.Build` 末尾）→ 确认框 → `SkyIslandStoryService.DevResetCurrentSlot`（只许岛外；基地优先用序章常驻门面，免得旧数据回写；共享 store + 立即落盘 + 读回核对）。官方任务日志由投影核心下一拍撤回；物品、好感、征程不动。Dev 与正式两种构建都编过，正式 DLL `4F9B3E32` 已部署，`check_dll_identifiers --expect absent` PASS。
- 未实机（L3）：Boss 池实际成员看 `Player.log` 的 `[SkyIslandEnemy] BOSS_POOL`；非鸭模 Boss（机械雪人等）上 Forge / 守钟装置换脸是否生效；Boss 原装枪不换时的备弹是否够；187 巡守 + Boss 模型的帧时间未采样；Boss 技能（手雷、召唤等）在桥上与居民附近的表现；首次到岛时居民 35 m 内有巡守，需要先清场。

## 2026-10-01 天空岛路灯夜里亮并照亮周围（COMPAT，L1/L2）

- 根因：Tripo 雕花路灯与铜灯柱的灯罩是图集里的不透明画面玻璃，材质无自发光，`lantern()` 塞在里面的 Glow 灯芯被挡住；岛上没有灯光源，且环境着色器是 `UniversalMaterialType=Unlit`（GBuffer albedo 为 0），URP 点光源照不到岛面。
- 修：`tools/sky_island_surface_materials.py` 按灯头高度带与图集亮度把灯罩玻璃分进发光材质；生成器 `lamp_light` 登记 67 盏灯位（摆放裁决同步平移 / 撤掉），写成场景标记 `NightLamp_*`；作者着色器新增 16 盏灯的全局数组光照；新增 `SkyIsland/SkyIslandStreetLamps.cs`（会话持有，按日光色亮度判昼夜，白天 O(1) 早返，夜里写数组并在最近 6 盏放真实点光源）。
- 证据：新守卫 `SkyIslandStreetLampGuard`（反向验证）；全量守卫 730/730；天空岛执行回归 16/16；Unity 碰撞校验与 ECM2 移动实跑 0 失败；着色器判包 PASS；部署 `sky_island_raid` `498bb1f8…`、`BossRush.dll` `8f19794b…`。未实机：灯光强弱与半径按报告 §11 清单 L1–L8 实测后再调。

## 2026-09-30 天空岛场景净空、穿模与质感全岛清理（COMPAT / SCHEMA+ / OPERATIONAL，L1/L2）

- 根因（57a5408f 判据漏洞）：只对村口家具与 Tripo 锚点件按道路中心线查；岛缘散布树石花、按旧坐标画的地标零件（风铃集圆桌凳、镜水寺石灯、邮亭、码头货箱、洞口灯）、灯标记铜灯、中继平台灯都不过判据，后摆的件看不见先摆的件；替换成 Tripo 的地标还留着程序化重复发射（风铃横梁、S1 圆池、S3 球石洞、S4 望远镜）。运行时 C# 落点（搜刮箱、灶火、纪念木桩、信鸽）从没按路面核过。
- 修：`tools/sky_island_prop_placement.py` `PlacementSpace.fit`（实际网格截面，查路面 + 0.2 m 路缘、桥口 / 门口 / 标记 / 运行时物件净空与先摆硬物，就近平移或撤下），生成器按坐标画的件全部经 `fitted`、发射顺序改为「统一裁决 → 地标 → 村景 → 灯标记 → 散布 → Tripo → 花草」；E 钟梁 / F 亭顶落到实测柱顶，店招挂进真实墙面，壁画加柱、彩旗加杆，H 光环挪到拱后，中继灯改护栏灯，D 根树板根按标记缩小，撤下四座（F、H 各两座）底下空壳会卡人的残阶。C#：`SkyIslandLootTables` 9 个锚点改方位 / 距离，`PlacementFor` 改写灶火 / 纪念木桩 / 信鸽 4 处散列方位；巡守 112 槽位随新导航重取样（`tools/sky_island_patrol_slots.py`）。
- 质感：39 个环境材质「颜色进贴图、`_BaseColor` 全白」（`tools/sky_island_surface_textures.py`，生图底纹 5 张；原有平铺材质保持 1024²）；Tripo 件按平滑扇区求 corner normals（<40° 同扇区，平滑面无折痕、棱角硬边），岛缘卵石走别名材质 `Pebble`；26 张 Tripo 图集从原件重导 1024 / 2048（`tools/sky_island_tripo_textures.py`）；水晶光泽 0.64 → 0.28–0.42；发光材质底色压到 0.5；自发光按日照压暗（作者着色器）；花圃小植物轻量版。
- 第二、三次出包（同日，两次只读材质复测后返工）：第一轮把石木瓦平铺图烘成 512²（路面纹素 128 → 64 px/m）、逐面邻面平均的法线仍留折痕、卵石没吃到新法线、发光底色过曝、深木平色——全部返工。另查出生成器给巡守槽位让位会与导航成环（重跑时 G 岛小屋漂 0.866 m），改为不读导航派生的巡守槽位（`DERIVED_RUNTIME_KINDS`，新断言 `DERIVED_RUNTIME_RESERVED` 已反向验证）。第二次复测又查出瀑布水 / 石分面仍走旧法线（折痕 9–11%）、卵石被 bmesh 在非流形边处整块翻反（相邻绕序冲突 17.2%），第三次出包改为整件扇区法线、源绕序与 bmesh 定向取冲突少者：场景级 Tripo 折痕全 0%、硬边 ≥97.8%、绕序冲突 ≤0.19%。
- 证据：新实例台账 `tools/sky_island_instance_ledger.py` + 审计 `tools/sky_island_scene_audit.py`：修前 352 条、修后 0 条（23 条设计接触写理由）；新守卫 `SkyIslandSceneCleanlinessPropertyTest`（6 条真实反向探针 + 5 条内存探针）；Unity 碰撞校验 PASS（14,580 次胶囊扫掠 0 失败）、ECM2 移动实跑 PASS（0 失败）；着色器判包 PASS；全量守卫 729/729；天空岛执行回归 16/16；正式编译部署 `BossRush.dll` `cd89f0ce…`，`sky_island_raid` `af85bc0c…` 三份一致（前两次 `ad9dcae9…`、`42f91137…` 已替换；随后 10-01 路灯一条再次部署，见上）。
- 数字：可见三角形 3,426,285 → 2,904,928；渲染器 840 → 824；材质 99 → 98；导航 4011 → 4073 顶点（余量 22）；包体 160.9 → 232.3 MB（贴图 34.3 → 89.6 MB）。未实机；报告与看图清单 `docs/reports/sky-island/天空岛_场景净空穿模质感审计_20260930.md`。

## 2026-09-30 天空岛桥栏凸挡块：修角色被模型「弹」出地图（COMPAT，L2）

- 根因：官方角色移动 ECM2（半径 0.30、跨步 0.25、贴边悬停 0）的解穿插不扫掠。角色一旦压进栏杆边的木梁 / 柱子（瞬移、头目换位、刷怪落点），最小平移方向可能指向虚空；零厚度双面栏杆是空壳，挡不住这一步。作者工程新增 `SkyIslandMovementSimulation`，用游戏自带 ECM2 与官方预制体参数在最终 raid 包里真跑：外边冲撞 6157 次、起步穿插 5959 次、随机游走 1473 人，修前 18 处穿插起步被推出桥栏坠落（BF / FG / FS3 / GE / GS4 / K2 桥），边界冲撞与游走本身 0 越界。
- 修：`tools/generate_sky_island.py` `rail_buttresses` 沿 926 条外边在栏杆外侧生成 0.8 m 厚、-0.25～+1.7 m、随坡抬升、2 m 分段的定向凸挡块（3318 只），反折角处逐段内收不侵入可走域；元数据写进 `geometry.json` `railButtress`，作者 `SkyIslandBundleBuilder.AddRailButtresses` 建 `COL_Rail_Buttress_*` BoxCollider（走既有 `COL_Rail_` 前缀，运行时分墙体层），`SkyIslandCollisionValidation` 只放行这批声明过的盒子并逐只核对位置与尺寸。
- 验证（L2）：Unity 碰撞校验 PASS failures=0；ECM2 实跑 PASS 0 失败；反向验证（注释掉建挡块）实跑转红 18 处坠落，还原后字节一致；着色器判包、73 包资源校验、SkyIsland 守卫 57/58（唯一红为 `SkyIslandRoadPlacementPropertyTest` 在 runner 下找不到 shapely，带依赖路径单跑 PASS）。raid 包作者 / 仓库 / 游戏三份 `5f8b6c3a…` 一致（`Validation/buttress_deployment_hashes_20260930.json`）；本轮只换资源包，未重编 DLL，未启动游戏。
- 未覆盖：飞行图腾可越过 1.7 m 栏杆（玩法取舍，待 owner 定）；头目瞬移不查导航连通（审计线索，未改）。L3 人工：贴着 BF、FG、GE、K2 桥栏来回冲刺翻滚、被镜中客换位到桥边，不应掉出。

## 2026-09-30 天空岛主题曲更名「人鱼湾」（COMPAT，L1/L2）

owner 决定继续用洛克王国原曲，只改显示名：`BgmTracks.json` 点唱机条目 `晴岚群岛 / Qinglan Archipelago` → `人鱼湾 / Mermaid Bay`，作者仍为「洛克王国 / Roco Kingdom」，文件 `sky_island_theme.ogg` 与场景曲条目不变。点唱机按音频路径判重，旧条目原位换标题，存档 index 不移位。WikiContent 中英 6 处曲名同步，站点重建。owner 本地另存的 90 秒 `sky_island_theme.wav`（未入库、无引用）按 owner 要求删除；程序化原创替代曲草稿经 owner 否决，未入库。**验证**：`BossBgm` / `BackMountain` / `Wiki` 守卫 19 PASS，Wiki 站点构建通过；未 Windows 编译（无代码改动，JSON 随正式构建部署）。**未实机（L3 待 owner）**：基地点唱机曲目列表显示「人鱼湾」、切英文显示 Mermaid Bay。

## 2026-09-30 天空岛搜刮箱三种木箱外观（COMPAT / OPERATIONAL，L1/L2）

- owner 选方案 A：复用岛上已有的 Tripo 木箱，打进特效小包。生活物资＝桶箱堆（1.04×0.88×1.05 m）、航务补给＝一排长货箱（1.29×0.47×0.50 m）、星工遗存＝长着风晶的小货箱（1.08×0.91×0.43 m，加青色点光）。
- 链路：`tools/sky_island_loot_crates.py`（Blender 5.2 后台，退出码 0 + `SKY_ISLAND_LOOT_CRATES_OK`）→ 作者工程 `Assets/SkyIsland/Fx/Crates/`（JSON + 三个 Mesh 资产，未提交）→ `SkyIslandFxBundleBuilder.BuildOnlyAndExit`（Unity 2022.3.62f3 批处理，退出码 0，包内回读三件网格）→ UnityPy 判包：两个材质与两份着色器 d3d11 产物与旧包等长，新增三件网格，199,122 字节（上限 256 KiB）→ 复制 `Assets/ui/skyisland_fx`。
- 第一次导出绕序全反（叉积与法线一致 0/1400）：世界那条链是 FBX 导入器替它翻的，自己写 Unity 网格要反转每个三角形；脚本已加自检（< 95% 一致即失败）。
- 运行时：`SkyIslandLootCrateLook` 按名字取场景材质（`Sky_TripoCrateBarrel` / `Sky_TripoCoverCrates` / `Sky_TripoCrystalCluster`，已用 UnityPy 核对场景包里都有），只藏官方包渲染器（不碰交互气泡），三个候选朝向里挑不压墙的，不加实体碰撞，搜空不消失；缺网格或材质保留官方包。只作用于搜刮点，Boss 奖励箱与委托谢礼箱不变。
- 验证：新守卫 `SkyIslandLootCrateLookGuard`（6 条反向探针）、`SkyIslandFxBundleGuard` 扩到三件网格（+2 条反向探针）；changed-only 守卫除缺 shapely 外全绿；`verify_sky_island_bundle_shaders.py` PASS；正式编译部署，`BossRush.dll` `B2CDF181…`、`skyisland_fx` `1699BADD…` 作者产物 / 仓库 / 游戏目录三份一致。未实机：箱子在 URP Deferred 下的明暗、贴图是否对位、朝向是否压墙要看。

## 2026-09-30 天空岛第二轮：Mod 武器进池、去浮空字、NPC 单气泡、航路任务点了没反应（COMPAT，L1/L2）

- 武器池（owner 定）：官方与其它 Mod 的武器都进池、口径不再过滤；只排除本 Mod 自己的 500001–500999（主动能力绑主角、且是 Boss 奖励 / 传说级掉落）。枪仍要求有同口径弹药（没弹药 AI 打不响）。`TypeIdLedgerGuard` 豁免 500999 号段上界字面量。
- 搜刮箱与采集点头顶的浮空字整块删掉（连同距离门控与语言重写）；四个守卫同步改为「不得再挂世界字」，真实加回 TextMeshPro 字段四个都转红、按字节还原。
- NPC 一堆气泡：官方只在交互组宿主 Awake 里关组员气泡，Mod 运行时后加的组员（剧情「聊聊航路」、航路任务、各 NPC 送礼 / 服务选项）各挂一个气泡，还按各自偏移散在身上（任务给予者写死 0.1 米，「!」挂在腰上）。`NPCInteractionGroupHelper.AddSubInteractable` 现在 setup 前继承宿主偏移、setup 后 `MarkerActive = false`，全 Mod 的交互组都只剩宿主一个气泡；任务「!」在宿主气泡正上方 0.5 米。
- 航路任务点了没反应：运行时 `AddComponent<QuestGiver>` 拿到字段初值 `finishWhenTimeOut = true`、读条 0 秒，任务页开出来的下一帧交互「读条完成」自动结束，官方 `OnInteractStop` 把页关掉。setup 写 `finishWhenTimeOut = false`（官方预制体的序列化值）。新守卫 `NPCGroupMarkerAndQuestGiverGuard`（4 条内存反向探针）。
- 搜刮箱外观（owner 问要不要换）：现在用的是官方 `LootBoxPrefab`（敌人死亡掉的包），全局预制体里没有别的容器；方案待拍板，未动。
- 验证：守卫 changed-only 除缺 shapely 外全绿；天空岛 16 个执行回归全过；正式编译并部署，`BossRush.dll` SHA-256 `422984C2…` 与 `Build/` 一致（非 Dev；同时含另一会话正在改的 `SkyIslandWorldStory.cs`）。未实机。

## 2026-09-30 天空岛敌人与头目武器品质（COMPAT，L1/L2）

- owner 反馈精英和 Boss 拿品质 1 的斧头。根因：全岛敌人（含头目 / 岛主、序章断风游猎·守、普通巡守）都从官方普通拾荒者 preset 克隆，武器沿用官方随机装配；`SkyIslandBossForge` 只换护甲，`VanillaPresetId` 只供数值参照；巡守底模还是血量最低的拾荒者。
- 修：新增 `SkyIslandEnemyArmory`（运行时）与 `SkyIslandEnemyArmoryRules`（纯规则），官方创建完成后按档次补齐主武器与近战——拾荒者 3–4、精英 4–5、具名对手 / 头目 5–6、岛主 / 噬风 6–7（下限 3 / 5，不再往下退），巡守 Rank 1–4 走拾荒者档、5+ 走精英档。只发官方静态物品表武器，过掉落排除标签、黑名单、岛上 10 万价值上限，排除控心与粘手物品；枪限小口径 / 大口径 / 霰弹 / 狙击，装满弹匣 + 两匣备弹，弹药品质不低于档次下限（该口径没有就取最高品质）。达标原装不换，换下的销毁，换上的随官方尸体箱掉落。接线：遭遇组身份层后、序章 Forge 后、巡守固定外形后；武器池随物资池读条预热，模块 OnDestroy 复位。
- 经济：每名敌人尸体箱里的武器档次随之上升（头目 / 岛主每趟一把品质 5+ 枪），owner 本次要求内；数值只在 `SkyIslandEnemyArmoryRules` 一处，可回退。
- 验证：新守卫 `SkyIslandEnemyArmoryGuard`（9 条内存反向探针）；`SkyIslandEncounters` 执行回归新增 `ArmoryRegression`（全部遭遇组逐位档次 + 序章），真实删掉遭遇组配枪调用后转红、按字节还原；SkyIsland 守卫 56/58（红项为缺 shapely 与作者工程着色器函数缺失，与本次无关）；隔离正式编译通过；17:31 正式构建部署到游戏目录，`BossRush.dll` SHA-256 `71EFAFE8…DE04FC` 与 `Build/` 一致（非 Dev，含同工作区另一会话未提交的天空岛剧情改动）。未实机：清单「2026-09-30 敌人武器品质」W-01…W-05。

## 2026-09-30 天空岛普通巡守密度与固定外形（COMPAT / SCHEMA+ / OPERATIONAL，L1/L2）

新增独立巡守 owner 与 112 个固定点，A6/B8、C10、D/E/F各12、G/H各14、四小岛各6；十二套固定外形、八档由弱到强。原 22 个 Boss/剧情遭遇组不改，普通 active+pending上限24；近远恢复同 Actor/HP/装备，死亡本趟不重刷，晚到创建回收。724个不同守卫、Windows正式编译、16组相关执行回归及Wiki构建通过；调度器346断言与7变异、点位18反例、3修改守卫反向验证通过。实际DLL与配置已部署并核SHA，源码/完整观察步骤见 [密度交付报告](docs/reports/sky-island/2026-09-30-天空岛普通敌人密度与固定外形.md)。没有运行游戏或读写玩家存档，DENS-01～05待L3；此前生产审查问题仍未闭环。

## 2026-09-30 天空岛整体生产审查（未闭环，SAFE 审查记录）

候选 `a8d4c694` / 作者 `8bda2f38` 当前不能整体生产验收通过。CR-2026-09-30-002～012 为 3 项 P1、8 项 P2：主菜单/死亡/撤离的资产结算边界，两居民配偶跟随，婚后衰减，加载异常恢复，长帧漏采，警示恢复，总状态漏红、Shader 默认路径和音频异常停止。完整发现、资源核对、两个生产逻辑 L2 反例及 PROD-SKY-01～09 清单见 [审查报告](docs/reports/sky-island/2026-09-30-天空岛整体生产标准审查.md)。本轮只审查，未改生产代码或运行游戏；L3 待验，旧 F3 PASS 不作为当前候选通过证据。

## 2026-09-30 天空岛模型碰撞修复续接（COMPAT / SCHEMA+ / OPERATIONAL，L1/L2）

修复建筑底部实体地面洞、硬物碰撞漏配与名义包围盒误挡、岛边双面碰撞、斜桥柱顶及连捞落脚净空；重新烘焙 4,004 顶点 / 4,774 面的连通导航。最终 raid 包 Unity PlayMode 静态物理验证通过；CR-2026-09-30-001 的新增硬物与桥梁指纹漏检已修复，11 项生产调用反例及两个内存变异通过；全硬物来源重绑后再次 PhysX 复验通过。完整数字、部署哈希与 COLL-01～06 人工验收见 [交付报告](docs/reports/sky-island/2026-09-30-天空岛模型碰撞修复与交付.md)。Windows 正式构建已替换上一轮 Dev DLL，实际 raid 包及 DLL 哈希一致，73 个发布资源包校验通过；未启动游戏或访问玩家存档，L3 待验。

## 2026-09-30 鸭王杯模组 Boss 战力、丧尸模式 Boss 波补刷、WikiContent 全量对照代码（COMPAT，L1/L2）

- 鸭王杯模组 Boss 战力（owner 定）：龙裔遗族 1500、幻影女巫 500，焚天龙皇仍为 2000（`ModeHGroupConfig.DragonDescendantPower` / `PhantomWitchPower`）；夹具 ModeHGroupRoster 断言同步，改值反向验证转红后按字节还原。
- 丧尸模式（玩家反馈「第 30 波 6 只 Boss 只出来 5 只」）：没有波数上限，是无尽模式；Boss 数 = 1 + (波数-1)/5，第 30 波确是 6 只。单只 Boss 生成失败（落点解析不到可达点、生成核心重试耗尽）原先直接按「已击败」扣掉，HUD 写 6 只、场上少一只。现在每只最多尝试 4 次（首次 Boss 专属落点，之后随机可靠落点，间隔 15 帧），全部失败才扣并打 `[WARNING]`（`ZombieModeTuning.BossSpawnAttempts`）。具体是哪种失败没有日志，未证实。
- WikiContent：以 cf64459c（09-27 对照审校）之后的提交为线索，中英两份全量对照当前代码更新：鸭王杯（群战、战力、押注退出口径、本场总结、名人堂、自定义 Boss 表现）、三只模组 Boss 页、Mode G 确认页与英文名、丧尸模式开局装备与技能预警、词条面板位置、Boss 筛选因子页签、随机事件「Boss 乱入」、遗种巢放生、天空岛奖励去向与首杀、龙铳寒冷、腾云驾雾翻滚键、叮当 Lv.5/6 限量上架、征程奖励表改列表（WikiContent 不支持表格）、龙皇台词英文与代码一致；在线站速查框同步。
- 补（owner 定）：断界戟英文名统一为代码里的「Inferno Emperor's Realm-Breaking Halberd」（WikiContent、`catalog.tsv`、在线站结构与图标清单、README_EN）；v2.3.0 更新日志未发版，鸭王杯一节改写为群战、「不速之客」改为「Boss 乱入」。
- 验证：正式编译（部署 SHA 与 `Build/` 一致）、718 守卫、111 执行回归、`npm --prefix wiki-site run build` 通过；未实机。L3：丧尸模式打到第 30 波，Boss 应出齐 6 只；若 Player.log（Dev 构建）出现「Boss 生成失败，换落点重试」，把日志发回来查失败原因。

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

## 2026-09-26 模块解耦 P0–P6 离线完成（SAFE / COMPAT / OPERATIONAL，L3 待验收）

检查点 `3323e33e`、`adef32ef`、`403a09a4`、P3/P4 至 `cae48e91`、P5 `6409a0f4`，本节所在提交完成 P6。宿主 202 文件 / 103,052 行降至 58 / 20,114；47 模块覆盖 1,091 编译源；自动导入 38,854 → 17,783 B。完整成员样本中四类源码读量上升，未宣称普遍降本；逐文件后测见 `architecture/CONTEXT_BASELINE.md`。

修复 `CR-2026-09-26-001`：票 ID 字段迁属性后地图 GetField 失效，改读注册 owner；生产注册/费用回归先红后绿。最终 697 全量守卫 PASS（0 新红/基线红）、95 全量回归 PASS（0 FAIL），Windows 正式/Dev 构建、14 Dev 标识、72 bundle 哈希通过；P5 42 + 29 项结构变异命中原断言并按字节/SHA 恢复。历史与最终证据路径、全部检查点、两种构建 SHA 见 `architecture/MIGRATION_STATUS.md`。

最终正式部署到真实游戏目录，源/目标 SHA-256 一致：`47F26AD82B1E67BFE1ECB0C728A8E97B40EBA80745790694B54AA7AD184A88B3`，Dev 标识 absent。未启动游戏或读写玩家存档；实机清单 `architecture/MIGRATION_ACCEPTANCE.md` 全部 MANUAL_PENDING，含 G 第二局、跨模式/异常退出、自然撤离、共享 UI、装备/建筑和 F3 看图项。没有 L3 或性能采样结论。

## 归档索引

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


## 2026-09-30 天空岛摆放与阴影修复（COMPAT / SCHEMA+ / OPERATIONAL）

道路改按登记模型真实包络绕行，连接全部桥口与建筑前缘；房前家具按实际朝向和道路摆放，修正长椅背板偏移，去除已替换地标的旧重复结构。修正级联阴影插值、反绕序共面贴图争抢和投影 bias，保留正常阴影与孤立双面薄片。完整证据与 POS-SHADOW-01～05 人工验收见 [本轮报告](docs/reports/sky-island/2026-09-30-天空岛摆放与阴影修复.md)；最终构建与部署结果在报告收口，不沿用上轮 L3 结果。
