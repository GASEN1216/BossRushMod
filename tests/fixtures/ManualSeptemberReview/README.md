# 2026-09-20 人工实测补漏回归

通过 `python tools/run_runtime_regressions.py --filter ManualSeptemberReview` 执行。

2026-10-06：生产基地 / 出击生成入口继续直接链接，激活替身在调用当刻记录 `allowCombat`，断言基地显式禁战、出击默认允许战斗；行为树与跟随表现由实际游戏验证。

同轮携崽范围修订：覆盖超过 90 秒的加载仍保留入场机会、玩家死亡取消在途角色、替换主角拒绝旧请求、已入场后 H 接管与主角替换立即回收。真实激活和 AI 子树另由 PetNestCompanionActivation 验证；本夹具不会把激活替身当作战斗证据。

同日独立复审：增加真实 `PetNestRuntimeModule.OnUpdate` / `OnSceneLoaded` / `TickBaseMaintenance` 与 `PetNestProgressionService.SettleRunHomecoming` / `AddExp`。顺序为成功出战 → 加载已开始但仍在旧关卡 Update → 角色被立即清理 → 基地未初始化 → 基地就绪后结算；核对实际经验与生涯次数，并再次维护确认不重复。旧实现先清 ActiveCompanionPetId，实跑在归巢经验断言转红；修复只在既有 Update 清理前保存归巢身份。`HomecomingStubs.cs` 仅替代无关基地业务，存储事务边界不在这个用例内，另由 ContentTransactions 覆盖。相关前后日志为 `Build/runtime-regressions/PetNestHomecoming-before.log` / `PetNestHomecoming-after.log`。

2026-09-28 同步：生产随从已使用独立背包，生成仍等待 LevelInited / AfterInit / 切图结束及主角物品就绪，但不再等待官方 PetInventory。回归钉住官方宠物背包缺席时仍可生成，并以调用计数验证 CleanupOnce 会取消旧角色的待处理重伤事件。下方官方容量桥测试继续覆盖保留的 Dev 探针，不再表示生产背包实现；独立物品树与保存事务由 ContentTransactions 覆盖。

2026-09-23 复查增补：直接链接 `PetNestPetProxyBridge.cs`，不再用空的容量桥替身。覆盖官方关卡和安全箱就绪前不得生成随从、官方背包即时从 2 格扩为 6 格、重复调用不叠加、按真实属性回收溢出、清理不误改尚存的新场景背包，以及旧 Unity 对象销毁后等待安全箱快照加载并恢复越界物品。Stat、Inventory、反射字段由宿主替身模拟；字段名、官方属性和 `SetCapacity` 行为已对照 `鸭科夫源码/`，实际游戏界面的格子与存档恢复仍待 L3。

2026-09-22 增补：远征 `PlayRoutine` 与暂停等待逐字抽取，测试驱动递归执行每个 `Current` 子 IEnumerator。分别在进入阶段和结果停留阶段暂停，验证不消费 `MarkRevealed`，恢复后同一记录仅确认一次。面板控件和存档确认只以计数替身观测，不证明实机模态菜单的操作表现。

直接编译生产的基地/局内随从生命周期、Mode H 地图派生和图鉴场景解析；孵化结果、跳过、暂停与套装命中判据逐字抽取。九张地图来自真实 JSON。替身只替代 Unity 角色、UI、时间、官方场景表与 UniTask 宿主，用可控的 TaskCompletionSource 在两段 await 之间切换席位和取消请求。

覆盖迟到生成、旧 finally 与新请求竞争、清理/重伤、跳过保留完整结果、大奖音效一次、暂停停表、子场景记录与未就绪重试、Mode E 玩家阵营下的敌友过滤，以及九图派生的实际落点/互斥席位/离场兜底。不能证明实际寻路连通性、模型显示、粒子观感或字体测量（L3 待实机）。

2026-09-25 增补：逐字抽取生产 `ComputePreparedPowerEdge`，验证双方交换对称、我方属性递增时赔率分差单调。真实链接 `ModeHMapSupportRegistry` 与 `ModeHSeedStream`，可控官方A* / Physics替身覆盖同seed场地恢复、看台/隔离点随场地移动、A*缺席/扫描中/部分路径/长绕路/遮挡拒绝，以及路径池Claim归零。替身不证明游戏地图实际A*导航或碰撞数据正确。
# 2026-10-07 归巢事务故障补充

原样提取生产模块、归巢结算、击杀预算复位及 PetNestPersistence 的 BeginTransaction / CommitTransaction / AbortTransaction。存储接受边界使用可拒绝的深拷贝候选替身，覆盖 Begin 拒绝、Store 拒绝不泄漏经验、5 秒维护重试、成功后复位预算、活动且重伤同一宠去重、同 pet id 换槽隔离。这里的可写性恢复仅验证模块重试编排；真实写故障闩锁的恢复由 ContentTransactions 用实际 Bundle 与协调器验证，不能将本夹具替身旗标变化称为真实存档恢复。
