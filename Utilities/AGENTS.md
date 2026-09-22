# Utilities/AGENTS.md — 跨模块基础设施专项规则

> 先读根目录 `AGENTS.md`。涉及 hooks、刷怪、恢复、场景门控时读 `docs/架构说明/` 下对应文档（local-only）。

## 职责边界

`Utilities/` 只放跨模块共享的运行时基础设施：运行时 hooks、刷怪核心、恢复监控、场景门控、缓存、run-scoped cleanup、平台辅助。单模块 helper 留在模块目录（`docs/架构说明/Utilities去重约定.md`）；新 hook 归位前读 `docs/架构说明/Hooks分层约定.md`。

## 规则

- `EnemySpawnCore`、`SpawnPositionHelper`、`EnemyRecoveryMonitor` 是运行时高风险区，调整前读 `docs/架构说明/刷怪与恢复系统设计.md`。恢复系统的经验常数是调参结果，不凭直觉改。
- Mod 生成的敌人要解除官方距离休眠（`SpawnedEnemyActivationHelper`，原因见 `docs/contracts.md` §7.1），并保留敌对性安全网（根 §4.5）。Mode E/F 有独立阵营体系；Mode G 冻结分支与原版 spawner 角色不走这条解除。
- `SceneRuntimeGate` 与 gameplay runtime gate 是过图性能关键路径，不在 transition 帧引入重活。
- `RunScopedRegistry` 的 cleanup 语义与守卫绑定，改结构时同步 `tests/`。
- `OfficialQuests/` 是官方 `Duckov.Quests` 投影核心（2026-09-22 由天空岛桥抽出）：全仓库只有一个活动实例（`OfficialQuestRuntimeModule` 持有并 Tick，注册顺序先于天空岛与征程），四个 Harmony 目标只在 `OfficialQuestComponents.cs` 装一次，给予者全局扫描只在 `OfficialQuestGiverLocator`。客户端（天空岛、鸭王征程）只提供 `OfficialQuestBinding` 委托，不得在自己目录里再碰 `QuestCollection` / `QuestManager` / 快照过滤（`tests/OfficialQuestProjectionGuard.py`）。
- 这里的改动影响所有模式：写明影响到哪些模式，并跑受影响模式的守卫与执行回归。

## 验证

- 热路径改动写明性能影响；没有实际采样只能写「静态预期」。
- 刷怪、恢复、场景门控的改动需要游戏内 smoke，编译和守卫不足以证明行为正确。
