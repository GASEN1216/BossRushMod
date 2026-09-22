# 审计模式修复执行回归

真实生产逻辑：直接链接丧尸完整转存类、Mode H 兼容性 Registry / 真实 JSON 签名表、WavesArena 生命周期 owner；逐字抽取支援弹 builder、认证报告写出与异步诊断 owner / Cancel / Release。运行时抽取记录 SHA-256。

宿主替身：物品 / 收件箱内存模拟（不读玩家存档）、Unity 对象销毁语义、受控异步角色工厂。诊断 owner 的 UniTask 返回类型替换为 Task，方法体保持逐字；不验证 Unity AI / 物理 / 场景引擎。支援弹使用 `Projectile.cs` 中的最小 `ProjectileContext` 契约替身，只声明生产 builder 访问的字段。已核对官方定义是无构造器的结构体，`default(ProjectileContext)` 的数值字段均为 0，特别是 `damageFactorToZombie = 0`；替身不提供非零默认值，遗漏生产赋值会使三类支援弹倍率断言失败。builder 仍逐字抽取当前生产方法；官方 Projectile / Health 如何消费伤害及实际物理命中不在此夹具证明范围内。夹具不再依赖 local-only 官方反编译文件，干净检出只需仓库源码和 .NET SDK。

入口：`python tools/run_runtime_regressions.py --filter AuditModeLifecycle`。所有结果仅 L2。

2026-09-22 补充：逐字抽取 Mode E 商人、Mode F 补位及金鸭雨现金生产方法，受控工厂覆盖旧请求的 null / fault / success 和后继请求交错；仅将 UniTask 返回类型与 Yield 换为 Task，其余方法体不改。商人构建/失败、补位结案和现金完成回调以可观测替身记录调用，验证旧任务不消费新局计数、不关闭新商人经济、不投放迟到现金。丧尸转存增加第二件保存失败的部分提交守恒，认证 owner 增加迟到失败与当前失败对照。

补查的 Mode D 直接链接真实 `ModeDRuntimeModule`，验证退出、重开同号波次、同场景重载和销毁均失效旧闭包；实际队列消费点由 `ModeDAsyncOwnerGuard` 检查。丧尸拍照直接抽取统一暂停门及暂停时钟方法，以基本 Time/CameraMode 替身验证拍照暂停与恢复，不模拟实际阶段 UI 或敌人 AI。

交叉复核另抽取 F3 的 TryReclaimAutotestItems / ClearAutotestSnapshotKey；物品收回器和计数器是可观测替身，仅验证门控顺序、短缺契约与失败留键，不声称测试了真实 Inventory/ES3。里程碑直接链接完整服务，核对 1–16 阶累计完整标价总额、故障重试、每帧/每阶实体预算，另验 15/16/32/33 阶数学边界及宿主 long 饱和。

AutotestBuffer 子夹具另外逐字抽取实际 CountOwnedItems / CountBufferedItems / CountInInventory、ReclaimAutotestItems、RemoveOwnedItems / RemoveFromBuffer / RemoveFromInventory 与清键流程。官方 ItemTreeData 的 rootInstanceID / entries / Count 变量由小型数据替身表达；验证 Buffer 尾部新增奖励回收、原树与无关物品保留、部分堆叠、缺主角/背包、旧快照基线不足、SaveBuffer 和物理写失败留键。只验证生产算法与调用顺序，不声称执行了真实 Unity 或 ES3。
