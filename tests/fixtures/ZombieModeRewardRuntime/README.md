# ZombieModeRewardRuntime

通过 `python tools/run_runtime_regressions.py --filter ZombieModeRewardRuntime` 运行。

夹具抽取当前生产方法体与奖励事件 / 散射缓存字段，执行商店和医疗服务的扣点、发放失败退款、库存扣减，健康 / 手持事件的 owner 隔离、玩家替换与清理顺序，以及临时 Courier 的资源查询 / 交互绑定、真实 NPC 逆序回收和安全区绑定回收。生产 SHA-256 与生成工程在 `Build/runtime-regressions/ZombieModeRewardRuntime/`。

`SpreadRegression` 还执行生产三叉 / 散射的重建、切枪事件和清理方法，并逐字抽取官方 `ItemAgent_Gun.ShootOneBullet` 的伤害分摊 / 最小伤害语句执行，验证单弹枪、原生霰弹枪、组合升级和独立选卡代价下的整轮总伤。Stat 对象的顺序、值计算与修饰移除由夹具替身提供，不能证明 Unity 命中、护甲结算或游戏安装版本；官方源码也记录 SHA-256。独立装备修饰、重复重建、切枪与清理必须同时保持正确。

`python tests/fixtures/ZombieModeRewardRuntime/spread_negative_probes.py` 在 `Build/` 下的独立副本重新注入二次削伤，要求结构守卫与执行回归分别在预期断言转红，再按字节恢复、核对 SHA-256 并实跑转绿。

Unity 对象替身模拟已销毁对象等于 null，以及销毁 GameObject 连带销毁组件。物品发放、医疗效果、资源解析、服务关闭和 RunOnly 注册表边界由可记录调用的替身提供；注入的函数不提供非空默认值。测试验证调用时序和 owner，不替代官方物品树、导航、Unity 实例化、实际商店界面或游戏内切图。完整 RunOnly 清理由 AuditModeLifecycle 验证；实际掉落归属由 RuntimeOwnership 验证；爆炸调度和支援弹道由 AffixCombat / AuditModeLifecycle 验证。
