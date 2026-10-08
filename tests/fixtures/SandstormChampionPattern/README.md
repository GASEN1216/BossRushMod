# 沙暴冠军招式节奏执行回归

入口：`python tools/run_runtime_regressions.py --filter SandstormChampionPattern`。

链接完整生产 `SandstormChampionAttackPattern.cs`，不复制招式选择算法，也不使用游戏或 Unity 替身。检查完整循环、阶段边界、跳阶段、阶段不倒退、激怒往返及实例隔离。

生产控制器是否实际消费此引擎由 `SandstormChampionCombatGuard.py` 检查。这里不能证明实机速度、碰撞、泡弹可击破、特效可读性或难度手感；这些必须游戏内验收。
