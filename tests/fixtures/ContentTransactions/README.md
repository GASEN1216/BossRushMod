# 内容资产事务回归

运行：`python tests/fixtures/ContentTransactions/run.py`。需要 .NET 8 SDK，不需要游戏 DLL，不接触游戏存档。项目、抽取方法、编译产物及源码 SHA-256 均写到 `Build/content-transactions/`；失败返回非零退出码。

覆盖五项修复（`COMPAT`）：战役奖金与 Completed 同盘、日报悬赏奖金与 claimed 同盘、凝蛋单候选 Bundle、实体蛋消费与孵化统计提交、餐食官方二次门禁后的扣量补偿。

夹具直接链接当前完整生产文件：Campaign 进度/模型/持久化/协调器；PetNest 领域服务、孵化、统计、模型、JSON/Codec、完整 Bundle 存档和协调器；RaidMealUsageBehavior；共享 SaveFile 节流器及 SimpleJsonHelper。日报补发、Persist、实际现金发放和官方采集回调每次运行从当前源码提取，防止维护一份过期副本。

存档适配器明确区分内存缓存与物理快照，`SaveFile` 不自动采集，余额适配器沿用官方 `Add` 只改 live Money 的语义。支持键采集失败、物理写失败、同帧节流、资产序列化失败；Bundle 拒绝通过反射设置真实 Store 的故障状态。餐食适配器模拟官方 `UsageUtilities.Use` 二次检查行为和 `CA_UseItem.OnFinish` 无条件扣量，并保留 StackCount setter 的最大堆叠钳制。

这是一组生产 C# 执行回归，不等同于 Unity 实机验收：不验证 ES3 原子文件替换、官方事件运行时顺序、完整 Inventory 树序列化、UI 揭晓和音画效果。日报在 `Store` 拒绝后的既有“至少一次补偿”策略未改变；本夹具“一次领取”覆盖已接受候选后的采集、写盘和节流重试。真实运行仍需专用测试档 smoke。

物理失败分两种注入：可恢复异常；以及官方 `SaveFile` 缺少 `finally` 时 `IsSaving` 持续为 true 的异常。后者只验证义务保留、拒绝强写和不重复发放；恢复断言由夹具模拟宿主解除 `IsSaving` 后执行。生产代码不改官方 saving 状态，不承诺此类真实 IO 故障会自动恢复。
