# 回归执行器产物一致性

通过聚合入口 `python tools/run_runtime_regressions.py --filter RuntimeRegressionRunner` 运行。

夹具调用真实 `tools/run_runtime_regressions.py` 的构建和执行函数，创建一个隔离的 .NET 工程：先保留默认 bin/obj 中的成功产物，再使当前源码失败，验证失败退出码来自本次新输出。还覆盖自定义 AssemblyName、已有隔离成功产物以及编译失败时禁止运行旧 DLL。

这里的 C# 程序只提供不同退出码，验证的是生产测试入口，不冒充任何玩法实现。全部输出位于 `Build/runtime-runner-self-test/`；不读取官方程序集或玩家存档。
