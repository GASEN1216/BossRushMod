# Utilities/AGENTS.md — 跨模块工具专项规则

> 先读根目录 `AGENTS.md`。涉及 hooks、刷怪、恢复、场景门控时读 `docs/架构说明/` 下对应文档。

## 职责边界

`Utilities/` 只放跨模块共享基础设施：运行时 hooks、Harmony 初始化、刷怪核心、恢复监控、场景门控、缓存、run-scoped cleanup、平台辅助等。

## 必守规则

- 单模块 helper 不要提前放进 `Utilities/`；遵循 `docs/架构说明/Utilities去重约定.md`。
- 新 hook 判断归位前读 `docs/架构说明/Hooks分层约定.md`。
- `EnemySpawnCore`、`SpawnPositionHelper`、`EnemyRecoveryMonitor` 是运行时高风险区，调整前读 `docs/架构说明/刷怪与恢复系统设计.md`。
- 恢复系统经验常数是调参结果，不要凭直觉改。
- `SceneRuntimeGate` / gameplay runtime gate 是过图性能关键路径；不要在 transition 帧引入重活。
- `RunScopedRegistry` 的 cleanup 语义与 guard 绑定，改结构时同步 `tests/`。

## 验证

- 热路径改动需要特别说明性能影响。
- 刷怪/恢复/场景门控改动必须人工游戏内 smoke；编译和 guard 不足以证明行为正确。
