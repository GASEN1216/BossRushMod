# 审计模式修复执行回归

真实生产逻辑：逐字抽取丧尸 `ZombieModeRuntimeModule` 的 RunId 门、入场转存、存储/收件箱回退、回滚与顶层物品收集方法，并抽取原宿主兼容桥；直接链接生产 `RunScopedRegistry`。另直接链接 Mode H 兼容性 Registry / 真实 JSON 签名表、WavesArena 生命周期 owner；逐字抽取支援弹 builder、认证报告写出与异步诊断 owner / Cancel / Release。运行时抽取记录 SHA-256。

宿主替身：物品 / 收件箱内存模拟（不读玩家存档）、Unity 对象销毁语义、受控异步角色工厂。诊断 owner 的 UniTask 返回类型替换为 Task，方法体保持逐字；不验证 Unity AI / 物理 / 场景引擎。支援弹使用 `Projectile.cs` 中的最小 `ProjectileContext` 契约替身，只声明生产 builder 访问的字段。已核对官方定义是无构造器的结构体，`default(ProjectileContext)` 的数值字段均为 0，特别是 `damageFactorToZombie = 0`；替身不提供非零默认值，遗漏生产赋值会使三类支援弹倍率断言失败。builder 仍逐字抽取当前生产方法；官方 Projectile / Health 如何消费伤害及实际物理命中不在此夹具证明范围内。夹具不再依赖 local-only 官方反编译文件，干净检出只需仓库源码和 .NET SDK。

入口：`python tools/run_runtime_regressions.py --filter AuditModeLifecycle`。所有结果仅 L2。

2026-09-22 补充：逐字抽取 Mode E 商人、Mode F 补位及金鸭雨现金生产方法，受控工厂覆盖旧请求的 null / fault / success 和后继请求交错；仅将 UniTask 返回类型与 Yield 换为 Task，其余方法体不改。商人构建/失败、补位结案和现金完成回调以可观测替身记录调用，验证旧任务不消费新局计数、不关闭新商人经济、不投放迟到现金。丧尸转存回归直接执行 RuntimeModule 生产方法，覆盖库存顺序、装备槽引用去重、仓库满与缺失时的收件箱回退、Reforge 同步/序列化/保存/销毁顺序、反向回滚、重复调用，以及第二件保存失败时先撤候选副本再返还原物；认证 owner 增加迟到失败与当前失败对照。

补查的 Mode D 直接链接真实 `ModeDRuntimeModule`，验证退出、重开同号波次、同场景重载和销毁均失效旧闭包；实际队列消费点由 `ModeDAsyncOwnerGuard` 检查。丧尸拍照直接抽取统一暂停门及暂停时钟方法，以基本 Time/CameraMode 替身验证拍照暂停与恢复，不模拟实际阶段 UI 或敌人 AI。

交叉复核另抽取 F3 的 TryReclaimAutotestItems / ClearAutotestSnapshotKey；物品收回器和计数器是可观测替身，仅验证门控顺序、短缺契约与失败留键，不声称测试了真实 Inventory/ES3。里程碑直接链接完整服务，核对 1–16 阶累计完整标价总额、故障重试、每帧/每阶实体预算，另验 15/16/32/33 阶数学边界及宿主 long 饱和。

AutotestBuffer 子夹具另外逐字抽取实际 CountOwnedItems / CountBufferedItems / CountInInventory、ReclaimAutotestItems、RemoveOwnedItems / RemoveFromBuffer / RemoveFromInventory 与清键流程。官方 ItemTreeData 的 rootInstanceID / entries / Count 变量由小型数据替身表达；验证 Buffer 尾部新增奖励回收、原树与无关物品保留、部分堆叠、缺主角/背包、旧快照基线不足、SaveBuffer 和物理写失败留键。只验证生产算法与调用顺序，不声称执行了真实 Unity 或 ES3。

RunOnly 清理子夹具逐字抽取 `ZombieModeRuntimeModule` 的登记、敌人 / 未知记录剪枝、单对象移除、RunId 失效及整局清理方法，并抽取宿主薄桥和 `ZombieModeRunOnlyRecord`；直接链接生产 `RunScopedRegistry.ForEachReverse`。Unity 对象替身模拟销毁后判空和 `Destroy`，宿主替身仅记录保险、效果、支援队列、事件索引、奖励 UI 与地图隔离清理动作。断言覆盖旧 RunId 拒绝登记、奖励 UI 死记录回收、对象单项移除、敌人与未知记录剪枝、失败及成功撤离清理顺序、反向回收、RunId 在记录回调前失效以及终局后账本清空；不模拟真实 Unity 场景切换与玩家存档。

EnemyRuntime 子项逐字抽取 `ZombieModeRuntimeModule_EnemyRuntime` 的实例 ID / marker 索引与 marker 注册方法，并抽取原宿主入口、视觉复原 / 脚印释放 helper；RunId 和场景有效性门直接抽取模块生产方法。可观测 Unity 替身覆盖 stale RunId 在组件访问前早返、现有 marker 重用、marker 状态重置、比例恢复、脚印池释放、索引缓存命中 / fallback / unregister / clear，以及索引写入先于 RunOnly 清理登记；RunOnly owner 只用夹具记录登记时序并触发抽取自宿主的真实清理回调，真正 RunOnly Registry 行为仍由前述清理子夹具验证。该子项不模拟真实 AI、Unity 生命周期和游戏场景。

Zombie HUD 子项逐字抽取 `ZombieModeRuntimeModule_Hud` 的创建、显隐状态更新、销毁、文本缓存与净化点滚动方法。Unity HUD component、宿主和 RunOnly 登记由可观测替身提供；断言覆盖过期 RunId 早返、创建与登记顺序、独立实例缓存、重复显隐/销毁幂等、文本变化才刷新，以及净化点正向累计、插值、稳定停止和减少路径。夹具不创建 TMP / Canvas，不证明真实画面排版与官方 HUD 隐藏的实机效果。
撤离结算子项逐字抽取 `ZombieModeRuntimeModule_Extraction.cs` 的成功结算、净化点现金结算与官方 `CountDownArea` 成功事件分发方法。可观测替身验证现金失败时保留净化点并恢复撤离选择、现金成功后先通知战役再按停止 / 成功顺序派发并清理、成功回调重复到达不重复结算，以及缺少成功监听时按通知撤离、回基地、清理的顺序兜底；不模拟真实 `EconomyManager`、官方场景加载或 Unity 事件系统。
