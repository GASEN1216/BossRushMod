# 建筑模块 owner 与共享反射回归

运行 `python tools/run_runtime_regressions.py --filter ContentBuildingOwnership`，需要 .NET 8 SDK。

项目直接链接生产 `BuildingInjectionHelper.cs` 和生成的当前宿主桥，验证反射类型、精确重载、私有静态方法、ID 属性、四类容器赋值、缓存引用，以及四类建筑原入口的调用顺序、宿主隔离、切槽先于初始化、未初始化仍执行清理和每个宿主从其 WishFountain 模块借用模型。建筑模块用调用记录替身，宿主与 Unity 类型用最小替身。

真实建筑注入器由官方 Windows 构建校验绑定；`ContentBuildingOwnershipGuard.py` 锁定模块归属、显式 owner、共享工具、协程与事件退订接线。夹具不能替代 Unity 建造 UI、存档建筑恢复、重绘、模型 shader 或协程实机测试。

2026-09-26 宿主载体同步：宿主桥由 `integration_host_source.py` 从 `IntegrationHostCompatibility.cs` 的 ContentBuildingBridges 区域逐字生成，共享反射 helper 继续直接链接。
