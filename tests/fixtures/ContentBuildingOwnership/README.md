# 建筑模块 owner 与共享反射回归

运行 `python tests/fixtures/ContentBuildingOwnership/run.py`，需要 .NET 8 SDK。

项目直接链接生产 `BuildingInjectionHelper.cs` 和 `ContentBuildingBridges.cs`，验证反射类型、精确重载、私有静态方法、ID 属性、四类容器赋值、缓存引用，以及四类建筑原入口的调用顺序、宿主隔离、切槽先于初始化、未初始化仍执行清理和许愿台模型借用。建筑模块用调用记录替身，宿主与 Unity 类型用最小替身。

真实建筑注入器由官方 Windows 构建校验绑定；`ContentBuildingOwnershipGuard.py` 锁定模块归属、显式 owner、共享工具、协程与事件退订接线。夹具不能替代 Unity 建造 UI、存档建筑恢复、重绘、模型 shader 或协程实机测试。
