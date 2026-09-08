# F3 协程执行回归

直接链接生产 `DebugAndTools/ValidationCoroutineStack.cs`，并由 `run.py` 逐字提取生产
`RunIsolatedCase` / `TryStep` 后编译。验证嵌套异常、取消/超时式释放、
`Current`/`Dispose` 失败、逆序清理、幂等释放、循环拒绝与同步步骤分帧；
隔离壳还验证工厂异常、双异常，以及父协程清理先于竞技场回收。
不复制算法，不调用 Unity，不证明游戏内模式、加载与 UI 已通过。

入口：`python tools/run_runtime_regressions.py --filter F3ValidationExecution`。
也可执行 `python tests/fixtures/F3ValidationExecution/run.py`；不要跳过生成步骤直接 `dotnet run`。
产物位于 `Build/`。
