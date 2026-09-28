# F3 协程执行回归

直接链接生产 `DebugAndTools/ValidationCoroutineStack.cs`，并由 `run.py` 逐字提取生产
`RunIsolatedCase` / `TryStep` 后编译。验证嵌套异常、取消/超时式释放、
`Current`/`Dispose` 失败、逆序清理、幂等释放、循环拒绝与同步步骤分帧；
隔离壳还验证工厂异常、双异常，以及父协程清理先于竞技场回收。
不复制算法，不调用 Unity，不证明游戏内模式、加载与 UI 已通过。

2026-09-14 追加**天空岛外壳**：同样逐字提取 `RunSkyIslandSync` / `RunSkyIslandCase` / `SkyIslandSessionStillValid`、
共享的 `RunSyncCase` 与 `SkyIslandSkipCase`。此前岛内外壳与它的 SKIP 分支**一次都没离线执行过**。现在覆盖：
会话没了 / 未就绪 / 场景实例换了三种 SKIP（且用例本体不被调用）；判据真 → PASS、假 → FAIL、
`SkyIslandSkipCase` → SKIP（带 metrics，绝不记 PASS）、意外异常 → FAIL；协程外壳的中止 SKIP、
会话没了在创建协程之前就 SKIP、工厂抛异常 / 返回 null → FAIL、嵌套异常 → `_UNHANDLED` FAIL 且挂起的 finally 照跑、
正常结束外壳不代记结论。

入口：`python tools/run_runtime_regressions.py --filter F3ValidationExecution`。
也可执行 `python tests/fixtures/F3ValidationExecution/run.py`；不要跳过生成步骤直接 `dotnet run`。
产物位于 `Build/`。

2026-09-28 追加性能窗口：逐字提取生产 `SamplePerformance` / `SamplePerformanceWindow` 及岛内分项录制开关，
直接链接 `ResourcePerformanceMetrics`。宿主时钟、场景句柄和录制结果用内存替身；验证完整 5 秒窗口、换图、
直接观察到取消、外层 Dispose（含当前模式已经复位）、采样帧不足与原有最终帧时间阈值。
中断样本不得更新基线，任何退出路径都要关闭开窗时的录制。10 秒资源窗口保持原默认时长。
构建经聚合 runner 的 fresh TargetPath 解析后执行，不再依赖 `dotnet run` 的默认输出选择。
