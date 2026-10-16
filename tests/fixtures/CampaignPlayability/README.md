# 战役目标与终章生命周期回归

2026-10-15 S-03：完整真实死亡采集器解析玩家、霜之哀伤、召唤法杖、宠物与雇佣 Boss 的原主人，经完整 Campaign 事实路由、个人交付和编码器执行。不同拥有者只生成本人击杀事实；旁观者、已死亡拥有者及切换到未准入槽的身体不能给旧槽刷击杀。房主本人另走实际本地交付并检查目标计数，重复死亡只计一次。团队波次保留原准入成员，包括断线或死亡后收到既得团队事实；重连换槽仍绑定原资产。终章在真实正常入口冻结参与名单，迟加入本场不补资格。

参与资格读取方法从实际 D/E/F、竞技场及丧尸模块抽取执行，表中的记录和 epoch 是显式可变输入。召唤/宠物/雇佣 owner 查询在本夹具中是可变宿主边界；对应 owner 表真实性由个人域及 Mode E 回归另证。本组不能证明完整模式准入网络到达、原生战斗或三端游戏正常。

2026-10-15：联机事实直接执行整份生产 `CampaignRuntimeModule_Coop.cs`、真实 `CampaignObjectiveTracker` / `CampaignModeBridge`、真实 `CoopPersonalDelivery` 和 `BossRushCoopJson`。原资格来自本人实际模式入场 epoch：D/E/F 与标准场从对应生产只读 getter 抽取，丧尸用实际 RunId，终章用真实 FinalRun。宿主桥整份链接，正常与 Dev 终章启动方法及场景/销毁生命周期抽取执行。测试先提供真实入口字段，首次战役世界快照可以缺失；收据不能首次武装或借用新一局的合同。

此组覆盖首快照延迟及终局空快照、同场同模式快速重开、旧 Core 未绑定/新 token、房主个人死亡但队员世界仍活动、不同真实模式边沿、原资产 A→B→A、契约取消/重新接取、原完整目标数量、乱序重复和实际保存失败重试。切回 A 恢复原进度对象、精确共享时钟和通知状态；离开 A 期间不计时，返回后受伤必须使原无伤目标失败。F 个人 ledger 早于 Campaign bridge 捕获时先确认实际 epoch，个人与世界 lease 共享同一批新目标和时钟；同一次标记击杀只按 max 合并世界事实与个人连续收据。

网络子组从真实 host delivery 生成 JSON，按冻结的 guest 资产经真实 codec 注入接收路径；错槽不 ACK，回原槽跨图支付原 lease，重复、空房间握手与 host ACK 均执行真实 helper。仅网络传输、原生场景/角色/模式入口字段和保存接收端为受控输入；此夹具没有执行完整 D/E/F/丧尸玩家入口、原生网络调度、官方钱包或存档落盘。真实模式业务由各自夹具和游戏验收另证；本组 L2 不代表 L3。

2026-10-13：抽取真实 F3 `RunCampaignFinalBoss`，并沿真实终章生成/死亡/清理 owner 执行。受控官方 Hurt 边界在无敌和加载中返回 false；时间、工厂和物理仍为替身。覆盖正常入场期间零次伤害、解除无敌后唯一 Hurt 到达一次真实死亡回调、无敌不结束超时失败、切图/首领销毁/owner 结束使等待作废，以及 Hurt 返回 false 的明确失败。原实机用例在沙暴冠军正常 1.1 秒入场无敌期间立即 Hurt，未读返回值，造成计数 0；本次补的是实际前置条件，保留死亡表现和 BGM 释放的原判据。此处旧场景事实的结算规则已由 2026-10-15 的原入场 lease 回归替代：有效旧事实只更新原合同，不推进当前新局。

2026-10-08：终章生成改接独立 SandstormChampionBoss 工厂。夹具只替代 Unity 工厂为可延迟 Task，并记录生产传入的 ownership lease，实际执行生产 runId 闭包，确认清理前为真、清理后为假；不把替身字段误称为非波次证明。冠军战斗、死亡入箱与视觉仍需 L1 接线审核和 L3 验收。

2026-10-07 报名石再次复现：夹具抽取完整真实 `SetupBossRushInDemoChallenge` 与真实普通/雪图路牌创建方法，直接链接 Campaign 宿主桥，证明官方 AfterInit 已真但 BossRush 二次传送及路牌尚未生成时不创建石头。默认出生点与真实路牌配置点刻意相距数百米；玩家后来走开，报名石仍围绕路牌生成。另覆盖显式自定义牌位置、雪图隐形交互点、牌失活/销毁/跨场景拒绝与单实例。几何执行生产 `TryFindAroundPlayer`、`TrySampleNavMesh`、`TryRaycastSnapPreserveXZ`、`PassesMinPlayerDistance`，NavMesh 适配器返回失败，Physics 适配器对受控水平地面求交；断言可见牌的受控 +0.8 米 pivot 输入确实被投回地面。模式检测、路牌模型工厂、垃圾桶/NPC/清场表现为替身，不模拟 Unity 渲染或实地图可达性。夹具逐次生成生产来源 SHA-256；此前只等官方初始化的实现须在“official AfterInit cannot create the altar before BossRush's real second teleport and roadsign creation”断言转红。

2026-10-07：新增 active scene 句柄晚于场景回调变化、地图注册表同场景稍晚就绪两条回归。夹具现在还抽取并执行真实 `TickCampaignFinalBossAltar` / `CreateCampaignFinalBossAltar`，不再用空方法代替石头维护；覆盖加载及 `AfterInit=false` 尾段不创建，最终搬人后在新位置旁创建、地面不可用时等待、碰撞体与交互组件、重复 tick 单实例且不重复查几何，以及待交付后移除。对象销毁连带销毁组件。场景句柄、地面查询、GameObject 组件容器和 FX 是替身，故不能证明地图中的可见位置或特效渲染。去掉生产场景句柄失效条件或 AfterInit 门，在隔离副本分别于 late active-scene 和 initialization-tail 断言转红。

2026-10-07：终章独白增加由官方 DialogueUI / actor 失效独立发出的取消，取消前不调用 Campaign cleanup。真实终章编排必须复位当前决战并允许重新交互；旧对话迟到取消不得清掉后继决战。已有工厂在途 → cleanup → 迟到角色回收，以及旧生成异常不能取消新请求的用例继续保留。渲染与官方对话 UI 为可控替身，生产 runId、取消、清理、实例认领与死亡逻辑逐字执行。

通过 `python tools/run_runtime_regressions.py --filter CampaignPlayability` 运行。

直接链接生产章节目录、实际 `Chapters.json`、共享 JSON 解析器、目标追踪器、死亡/受伤采集器、模式桥与 NoteIndex 桥。覆盖六章完成路径、无伤边界、近战契约的开局工具判据、友军/中立与非玩家击杀过滤、暂停、计数封顶、待交付不重新武装、死亡/换局复位和图鉴双向修复。零伤害用例依据官方 `Health.Hurt`：即使 `finalDamage` 为零，仍发出 `Health.OnHurt`。

Mode F 闩回归直接链接生产 `ModeFRuntimeModule_BountyLatch.cs` 与战役模式桥，验证印记先写后删、纯查询不消费、victim 身份匹配和一次性消费；场景、装备和 HUD 等模块依赖不进入该夹具。

每次运行从当前 `CampaignFinalBoss.cs` 抽取真实状态、只读属性、启动、门禁、场景缓存、独白到生成、生成、死亡和清理方法，放入实际 `CampaignRuntimeModule` owner；从 `CampaignRuntimeModule.cs` 抽取真实 `OnSceneLoaded` 与 `OnDestroy`，并直接链接整份 `CampaignRuntimeModuleHostBridge.cs`。从 `CampaignDialoguePlayer.cs` 抽取取消方法，仅把异步载体 UniTask 换成 Task。覆盖旧宿主入口落到同一模块、独立宿主状态隔离、场景代数失效、取消独白不生成、后继对话使用新 token、工厂迟到成功回收、旧请求异常不取消新请求、让路退订、场景销毁后可重打、已排队的重复死亡回调只完成一次。卸载用例在真实 `OnDestroy` 清空模块 owner 后继续完成旧工厂，验证异步入口捕获的原宿主仍先回收掉落再销毁迟到 Boss。对象替身模拟 Unity 已销毁等于 null 和 GameObject 销毁连带组件；测试必须经过这条销毁路径。来源 SHA-256 写入 `Build/campaign-playability/production-source-sha256.json`。

替身提供官方事件输入、武器标签、模式字段、图鉴列表/字典、持久化接收端与可控 Boss 工厂。独白等待为可取消 Task；实际对白、官方 UI 与 UniTask player loop 由 `SkyIslandDialogue` 的共享管理器回归另行覆盖。此夹具不模拟战斗、物理落点、渲染、原生输入、玩家存档和奖金落盘。近战实际配装调用点由 `CampaignFlowGuard` 验证，物品工厂成功率须 L3。L2 证明这些输入能驱动生产目标/编排，不能代替实机入口、战斗手感和帧耗采样。奖金与持久化由 `ContentTransactions` 覆盖。

2026-10-13：执行真实 ExtractionLease 与原契约接受修订，验证旧局 F 标记击杀、Zombie 波次和撤离不污染新 Armed，资产返回、放弃重接、写失败、重复与连续收据；实际世界事实与个人击杀证据取 max，不能叠加。终章还抽取原 connected/role/room 的 Capture/IsValid 方法，执行 Stop、新房间、重连、场景重新建立与无 COOP。网络与保存接收端可变，不能据此证明真实多端到达、官方任务 UI 或落盘成功。
