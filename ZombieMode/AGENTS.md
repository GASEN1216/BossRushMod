# ZombieMode/AGENTS.md — 末日丧尸模式专项规则

> 先读根目录 `AGENTS.md`。本文件只记录丧尸模式独有的边界；守卫索引见 `tests/README.md`。

## 核心边界

丧尸模式是独立模式：有自己的生命周期与战斗状态机、奖励选择、净化点经济、临时 NPC、区域伤害、撤离和 run-only cleanup，不接入标准 BossRush 的共享变异词条 roll（根 §4.11）。

## 规则

- 不接共享变异词条 roll。
- **不按性能档改变玩法结果**：视觉与性能保护可以降级表现，但不同机器必须得到相同的奖励、伤害、刷怪和状态效果（`ZombieModeNoPerformanceGameplayScalingGuard` 等）。
- 运行时对象、协程、事件登记到 run-only cleanup 通道，`RunOnlyObjects` 是唯一可信源。
- 刷怪复用 `SpawnEnemyCore(...)`；优先复用共享缓存的官方 `Points` 世界坐标，地图配置画像与玩家周围候选环作回退。所有来源（含分裂 / 召唤 / Boss）须在进入生成核心前经 `SpawnPositionHelper.TryResolveReachableFrom` 证明与玩家连通：官方 AI 走 A* Pathfinding，官方关卡不烘焙 Unity NavMesh，所以 A* 图优先、无 A* 才退 NavMesh；不能用 Raycast 命中地形代替可达性。收集刷怪点要等官方 `LevelManager.AfterInit`（玩家已被官方定位），失败回基地也要等它。
- 定时逻辑统一走本模式的 unscaled 时间轴；确需直接用 `Time.deltaTime` / `Time.time` 的地方写注释说明理由（`ZombieModeTimeAxisGuard`）。
- 状态机、入场事务、奖励目录变更时同步相关 `ZombieMode*Guard.py`。奖励目录守卫刻意不核对具体数值，调平衡不需要改守卫。
- 区域伤害、撤离、临时 NPC、奖励弹道是运行时高风险区，不能只凭编译通过宣布完成。

## 验证

- `python tools/run_guards.py --filter ZombieMode`；入场退款等逻辑另有执行回归 `python tools/run_runtime_regressions.py --filter ZombieMode`。
- 入口、开局、奖励选择、战斗、撤离 / 失败、再次进出场景需要游戏内 smoke；Windows 端串联入口是 `test_zombiemode_goal_windows.bat`。
