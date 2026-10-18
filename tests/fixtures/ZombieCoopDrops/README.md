# 丧尸普通掉落联机归属回归

运行 `python tools/run_runtime_regressions.py --filter ZombieCoopDrops`。

直接提取生产 `TryDropZombieModeItemNearPosition`、`DropZombieModeItemAtPlayerFeet` 和 `RegisterZombieModeDropCandidate`，执行单机入包/超重落脚、多人尸体处共享掉落、房主本体不可用、客端禁止重复创建及过期局门控。注册对象、品质标记、原波次及清理时间保持真实方法行为。每次构建执行 fresh TargetPath 并记录生产 SHA-256。

物品工厂、背包、原生 Item.Drop 与 run-only 的最终接收器为可记录替身；本回归证明实际交付路径与次数，不证明原生跨进程同步、拾取竞争、碰撞或游戏内清理。原生同步契约另查安装 COOP DLL，最终由三端 L3 验收。
