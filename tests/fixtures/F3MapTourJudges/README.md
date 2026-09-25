# F3 地图选择器逐图进场：离线判据执行回归

分类：SAFE（测试）。运行 `python tools/run_runtime_regressions.py --filter F3MapTourJudges`（或 `python tests/fixtures/F3MapTourJudges/run.py`）。
不要在本目录直接 `dotnet run`：会留下 `bin/`、`obj/`。

- **真实生产逻辑**：`DebugAndTools/F3GameplayValidationMapTourJudges.cs` 原样链接编译——`CaseId`、`JudgeArrival`、`JudgeTour`。
- **数据**：`Assets/SpawnPoints/*.json`（地图选择器的清单）与 `tools/gameplay_coverage.py` 对 `MAP_TOUR_*` 的展开，核对游戏侧与离线报告的用例 id 同口径。
- **不覆盖**：取数（`F3GameplayValidationMapTour.cs` 的场景加载、射线落地、A* 最近点、回基地收尾）要真实游戏进程，只能靠实机 F3。
