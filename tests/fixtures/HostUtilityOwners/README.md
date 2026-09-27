# HostUtilityOwners 执行回归

本夹具链接完整生产文件：`Utilities/BossStatScaling.cs`、`Utilities/BossRushWaitCache.cs`、`Utilities/ZombieSpawnSanitizer.cs`、`ZombieMode/ZombieModeSpawnSanitizationPolicy.cs` 和 `Integration/BossRushIntegrationRuntimeModule_AmmoShop.cs`。购买计数的原字段与属性从 `Integration/BossRushIntegrationRuntimeModule_RuntimeHooks.cs` 逐字抽取；没有给生产注入点补非空默认值。各输入 SHA-256 写入夹具输出目录。

行为覆盖：

- 倍率的显式 1 与 nullable 默认值、生命值同步、枪械/近战 3 倍上限、分段异常后继续、反应时间除法、缺少角色/物品/Health/AI。
- 三种等待对象在不同消费者间的稳定身份、相互独立身份及 0.1 / 0.5 / 1 秒构造参数。
- 官方非 Boss 自爆僵尸的保留策略、普通/其它特殊/Boss 的排除；两次策略查询发生在各自清理阶段，第二次能观察第一次清理的结果；先清技能后移附件、AI owner 身份与 fallback、精确且区分大小写的类型名、共享 prefab 不销毁、无 AI/无 preset、策略/技能回调/单个附件异常的不同边界。
- 商店按需创建与复用、原 53 个 ID 及顺序、库存 9999/价格 1.1/解锁策略、实例字典保留已有物品、单项工厂异常后继续、购买计数在每次 UI 回调前清零、同一 owner 的购买路由、关场和死亡清理、销毁对象的假 null、死亡清理的两层日志边界及缺少可选反射字段。

Unity 对象、Stats、AI、技能槽、物品工厂和 StockShop 是记录替身。Unity 替身模拟 destroyed-null，销毁 GameObject 会连带销毁组件与子对象；关场/死亡销毁是必经用例。StockShop 的字段名和原始条目结构对照官方反编译源，缓存反射使用真实 `FieldInfo`。没有执行游戏 AI、官方交易支付、Unity 帧调度或 UI 显示，相关正确性仍需 L3。

回调仅记录观测值，断言放在生产调用返回之后；异常吞并边界中的测试断言不能被当作成功。故意抛出的回调用于验证生产异常边界，其结果同样在外部断言。

运行：`python tools/run_runtime_regressions.py --filter HostUtilityOwners --jobs 1`。不要从夹具目录直接 `dotnet run`。迁移期间在独立 TEMP 中完成的日志、源码指纹、结构与执行变异证据见 `Build/migration/host-utility-owners/`。

`HostUtilityOwnershipGuard.py` 验证原宿主入口、配置参数、共享等待、策略注入、Integration 商店 owner 及关场/死亡调用点的接线。夹具执行真实 owner 方法，宿主场景生命周期的完整调用链仅为 L1，不能以本夹具通过宣称实机完成。
