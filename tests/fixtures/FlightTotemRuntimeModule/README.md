# 飞行图腾运行时 owner 回归

运行 `python tools/run_runtime_regressions.py --filter FlightTotemRuntimeModule`。

夹具直接链接生产 `FlightTotemBootstrap.cs`、`FlightTotemFactory.cs`、`FlightConfig.cs`、`EquipmentAbilityConfig.cs` 和 `AbilitySystemHelper.cs`，覆盖两个 owner 的初始化幂等、已自动加载时的物品复用、失败后的原有初始化闩、配置与本地化先后、重复初始化后的语言刷新、场景重绑的两次半秒等待以及清理顺序。

Unity 对象与协程调度、物品工厂与变量、管理器以及本地化服务由可记录调用的替身提供；已销毁 Unity 对象视为 null，销毁 GameObject 连带销毁组件。替身宿主没有物品初始化或本地化回调，因此模块绕回宿主会直接编译失败。资源内容、官方物品注册和真实装备能力仍须 Windows 正式构建与 owner 实机验证。
