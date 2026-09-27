# 装备配置器注册回归

运行器从生产 `EquipmentFactory.cs` 抽取注册表字段和三个登记/执行方法，在独立宿主中检验重复登记保持位置、枪械前置配置阶段与一般装备阶段分离、非法 key 拒绝。Item 为替身；bundle 载入、Prefab 与官方工厂行为由正式编译和其他回归覆盖。

同时从 `BossRushIntegrationRuntimeModule_ContentRegistration.cs` 抽取完整 `LoadEquipmentContent`，验证占位符、枪运行时与武器补配顺序，以及重复引导、缺模型、占位符失败、单件模型失败和枪初始化异常的原有恢复边界。工厂查找、配置器和日志是记录调用的替身；不模拟 AssetBundle 或 Unity 对象生命周期。
