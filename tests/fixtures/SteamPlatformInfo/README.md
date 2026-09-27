# Steam 查询 owner 执行回归

运行 `python tools/run_runtime_regressions.py --filter SteamPlatformInfo`。

直接编译生产 `Utilities/SteamHelper.cs`。替身仅控制 Harmony 类型查找、官方反射方法和日志；不复制查询或缓存实现。覆盖首选/回退、空值、异常、缓存方法而非昵称、延迟类型加载、独立的一次性警告和非字符串返回。私有静态字段只在独立场景之间由夹具重置，不为生产增加测试入口。
