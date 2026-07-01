# ZombieMode/AGENTS.md — 末日丧尸模式专项规则

> 先读根目录 `AGENTS.md`。本文件只记录 ZombieMode 独有边界。

## 核心边界

ZombieMode 是独立模式，不接入标准 BossRush 的共享 mutator roll，有自己的 lifecycle/combat 状态机、奖励选择、净化点经济、临时 NPC、区域伤害、撤离和 run-only cleanup。

## 必守规则

- 不把 ZombieMode 接入共享变异词条 roll。
- 不按性能档改变玩法结果。视觉/性能保护可以降级表现，不能让不同机器得到不同奖励、伤害、刷怪或状态效果。
- 运行时对象、协程、事件优先登记到 run-only cleanup 通道。
- 状态机变更必须同步相关 `ZombieMode*Guard.py`。
- 定时逻辑遵循现有时间轴约定，改动前查现有 guard 和 `tests/README.md`。
- 区域伤害、撤离、临时 NPC、奖励弹道等是运行时高风险区；不得只凭编译通过就宣布完成。

## 验证

- 至少跑相关 ZombieMode guard。
- 入口、开局、奖励选择、战斗、撤离/失败、再次进出场景需要人工 smoke。
