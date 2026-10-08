# 内容资产事务回归

2026-10-08：首次读档对齐天空岛序章的关卡就绪时机。新增完整生产 `CampaignRuntimeModule` 与 `CampaignFacilityUnlocks`，在主菜单 Awake / Start / 场景回调 / Tick 后才向同槽提供官方缓存字节，不借换槽回调刷新；验证首次就绪前不读取或覆盖默认档、就绪后章节、线索、设施 token 与 Jeff 三态恢复。已加载同槽快照在过图期间仍可查询，宿主销毁在关卡就绪标记消失后仍保存 pending，包括键写故障恢复；切槽后不把旧快照写入新槽。独立写 / 读进程增加 Jeff 接取、已体验、已交付三种场景，每个读进程从未就绪的 Awake 开始，在文件字节到达后才 Tick；奖励不重复发放。Unity 关卡状态、官方缓存到达和任务 UI 注册为替身；真实 runtime、进度服务、门面、存档 store、恢复及物理保存协调器完整执行。所有文件只在 Build 下生成，不读写玩家存档。

2026-10-07：新增官方 `Collect` 已消费征程 typed pending、但尚未物理写盘的退出路径（CR-2026-10-07-015）。真实共享 store 成功写键并回读后，经征程绑定通知共享 engine 保留物理保存义务。回归覆盖不调用官方 `SaveFile` 或它失败后直接关闭宿主，随后由独立读进程只用文件字节恢复待交付；同进程另验回基地 Tick 补盘，以及采集后切槽不得替旧槽写盘。另一个独立进程变体从前五章 Completed 测试前置快照出发，真实接取第六章后直接关闭，再验证全新进程保持 active=ch6、ContractActive 和前五章完成记录；章节内容和奖励数值由替身提供，但 ID / order 不再共用 ch1。移除征程的 `AfterPendingWriteFlushed` 绑定，在隔离副本会丢失待交付事实并转红。这些用例不模拟官方自己触发保存后留下的 sticky-saving 闩，也不承诺强杀进程前尚未写盘的数据可恢复。

2026-10-06 二次复核：`CampaignDiskRegression.cs` 让真实共享 store 先订阅，再注入首个章节 key 写失败 / 回读失败 / 已交付 key 写失败，由常规协调器 Tick 恢复。旧顺序会在恢复订阅时清掉 accepted 快照，已实际转红。修复后每种场景启动一个写进程和一个独立读进程，只交换本次新目录中的物理文件；验证恢复后的章节与奖金存续、提前只读初始化后 bootstrap 重读、换槽隔离。文件只位于 `Build/content-transactions/campaign-process-*`；ES3 文件容器和 Unity JSON 使用测试适配器，生产进度、JSON 绑定、共享 store、订阅和协调器完整链接。现装宿主 IL 契约由 `JeffQuestFlow/OfficialAssemblyContract.cs` 核验。

2026-10-06：征程重启回归清除全部 Mod / 官方内存缓存，仅从上一次物理快照恢复，验证接取、官方采集后的待交付、Jeff 交付后防重领、非基地宿主退出兜底四条生产保存链。官方磁盘与载入由内存字典替身模拟，不读取玩家存档；仅能证明生产状态/协调器在这些保存时点的行为。

2026-09-28：新增遗种背包事务：同血脉不同崽按 id 隔离，带引号的物品树 JSON 经 Bundle 克隆与编解码原样保留，空背包默认兼容旧档，非空背包禁止放生 / 远征，出击图不等待基地仓库，物理失败保留并续写双方容器与崽记录，官方 IsSaving 采集可推进最新状态，拒写与切档不污染其他崽。

直接链接 `PetNestBackpackSnapshot` 与 `PetNestBackpackRestoration`，执行完整树的 KV 原始字节、重复 / 空 key、配件连接、稀疏嵌套格子和高位排序锁往返；拒绝循环、断边、重复引用与孤儿数据。恢复 owner 用真实生产方法验证动态插槽和容器重建、节点未连接时取消、Plug 拒绝、变量写失败和缺 prefab 时逐节点回收；官方 `OnItemLoaded` 每节点通知保留写变量后 / 接树前时序，观察者抛错仍完整回收。实际 Unity 物品组件、逐帧调度、交互 UI 和故障容器脱离角色为宿主替身 / 静态接线，不能据此宣称实机通过。

2026-09-22：补入出战宠移除 / 放生 / 远征成功和拒绝时的实体通知计数；旧彩宠在死亡或放生前保存颜色，已有快照不覆盖、无原宠不借色；缺 prefab 的官方非 null FallbackItem 故障及资源恢复后的单次续发。派遣页面组装方法逐字抽取，卡片渲染替身只记录是否请求挂入口，从而验证与真实 `CanDepart` / `TryDepart` 对锁定和重伤的判据一致。实体销毁和渲染不在本夹具中模拟，由生命周期夹具与实机补充。

2026-09-19：新增五项遗种巢成就生产链测试。真实蛋孵化及放生推进到 30 种血脉，检查 9/10、29/30 阈值、异色 roll、亡命远征死亡、物理保存失败重试与重复授奖幂等。成就宿主替身遵循真实 `TryUnlock` 的未初始化拒绝和已解锁去重规则，不预设初始化成功。测试先复现首次成就初始化遗漏与纪念碑候选阶段提前授奖，再验证修复。

运行：`python tools/run_runtime_regressions.py --filter ContentTransactions`。需要 .NET 8 SDK，不需要游戏 DLL，不接触游戏存档。项目、抽取方法、编译产物及源码 SHA-256 均写到 `Build/content-transactions/`；失败返回非零退出码。

覆盖五项修复（`COMPAT`）：战役奖金与 Completed 同盘、日报悬赏奖金与 claimed 同盘、凝蛋单候选 Bundle、实体蛋消费与孵化统计提交、餐食官方二次门禁后的扣量补偿。

夹具直接链接当前完整生产文件：Campaign 进度/模型/持久化/协调器；PetNest 领域服务、孵化、统计、模型、JSON/Codec、完整 Bundle 存档和协调器；RaidMealUsageBehavior；共享 SaveFile 节流器及 SimpleJsonHelper。日报补发、Persist、实际现金发放和官方采集回调每次运行从当前源码提取，防止维护一份过期副本。

存档适配器明确区分内存缓存与物理快照，`SaveFile` 不自动采集，余额适配器沿用官方 `Add` 只改 live Money 的语义。支持键采集失败、物理写失败、同帧节流、资产序列化失败；Bundle 拒绝通过反射设置真实 Store 的故障状态。餐食适配器模拟官方 `UsageUtilities.Use` 二次检查行为和 `CA_UseItem.OnFinish` 无条件扣量，并保留 StackCount setter 的最大堆叠钳制。

这是一组生产 C# 执行回归，不等同于 Unity 实机验收：不验证 ES3 原子文件替换、官方事件运行时顺序、完整 Inventory 树序列化、UI 揭晓和音画效果。日报在 `Store` 拒绝后的既有“至少一次补偿”策略未改变；本夹具“一次领取”覆盖已接受候选后的采集、写盘和节流重试。真实运行仍需专用测试档 smoke。

2026-10-07：物理失败分两种注入：调用尚未置闩时异常；以及官方 `SaveFile` 缺少 `finally`、在置 `saving=true` 后异常。替身保留真实私有静态 `saving` 字段，真实共享引擎通过 `BossRushSaveFileThrottle.RunSaveFile` 只释放本次同步失败留下的闩，原异常继续进入既有 deferred 重试。征程与日报都无需夹具手工解除 `IsSaving` 即可继续落盘且不重复发奖；已在进行的保存被拒绝，包装不执行回调也不清闩。`JeffQuestFlow` 另核对现装官方 DLL 的 private static 字段、置闩 / ES3 写入 / 清闩顺序和缺少异常处理。官方自行触发或未经过此包装的保存异常仍不在保护范围；持续 IO 失败和进程终止不承诺未保存事实可恢复。

2026-09-11 增补：直接链接完整 `PetNestExpeditionService.cs`，验证远征遗种蛋的血脉身份不依赖
活着的崽：旧记录在放生前补齐、后续远征真死后仍补发原血脉、Bundle 编解码往返、缺失原崽时
不借用其他崽身份、盖章暂时失败保留游标且恢复后仅投递一次。随机数、物品实例化/盖章/配送、
经验与伤痕表现为宿主替身；真实出发、结算、移除、候选事务、编解码和奖励游标都执行生产代码。

同日直接链接完整 `ShowcaseService.cs`：满柜 Q5→Q8 原位升级（原加成公式不变）、重复/自产/低品质
拒绝、存档拒写与 Save 后回读失败时同时恢复内存及官方缓存、重新加载和明确撤销入口的后端事务。
属性挂载为替身，不证明 Unity 界面布局；可见按钮接线由 `BackMountainStructureGuard` 钉住。

2026-09-18 征程增补：目标全部完成的入队失败事实由真实 `CampaignProgressService` 保留；局内追踪 Reset 后仍能重试，同帧重复通知不重复完成，已达标不可被放弃，切槽丢弃旧槽的未入队事实。Boss 清理、面板关闭和对话取消在本夹具中是宿主替身；终章异步与取消生命周期由 `CampaignPlayability` 覆盖。只验证本进程保留并补交事实，不承诺进程在写入失败期间崩溃还能恢复这条未持久化事件。

2026-09-25：链接 CampaignGuideTable 并执行引导三态事务（旧档默认、接取前拒绝、体验不等于交付、写失败不污染当前缓存、克隆保留数组、重读不丢进度）。BackMountain 变身仍是宿主替身，不模拟其外观/物理。

2026-09-25 三形态扩展：后山 UsageBehavior 统一转到即时 MorphService；本夹具的变身宿主只控制成功/拒绝/异常，验证消费与补偿事务，不再将它伪装为 RaidMealService 登记。真实三形态外观、属性、碰撞和清理见 BackMountainMorph。BackMountainLifecycle 仍逐字链接旧 RaidMealService，验证旧档已预备餐食兑现、过区重挂与局末清理。
# 2026-10-07 归巢持久化恢复补充

链接完整 PetNest Bundle、Codec、Service、SaveCoordinator 与共享存档引擎，并提取真实归巢结算和经验方法。对 SavesSystem 适配器分别注入 Save 和写后回读异常，实际生产 Store 置故障；不手工清故障位，驱动实际协调器 Tick 完成同槽重写、一致回读与 SaveFile 后，验证归巢候选可继续接受、保留先前击杀经验且重伤宠仅获一次奖励。未知 schema 屏障保持只读，换槽不重放旧 pending。测试不访问玩家存档，也不承诺没有已接受 pending 的编码故障或进程退出后的未入队奖励可以恢复。

2026-10-08：CampaignExplicitCodecRegression 输入实际丢失 chapters 的脱敏 JSON，执行生产显式编解码、交付 token 恢复及第六章待交付状态保存/重读；同时拒绝缺 state、重复章节并覆盖 null 数组兼容。该用例不经过 JsonUtility 替身生成输入。
