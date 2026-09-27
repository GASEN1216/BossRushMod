通过 `python tools/run_runtime_regressions.py --filter ModeEFEnemySpawnRuntime` 运行。

夹具编译完整的 `Utilities/ModeEFEnemySpawnRuntime.cs`，只将 UniTaskVoid 返回载体替换为可等待的 Task。覆盖阵营缓存的引用/数量失效规则、Boss 与小怪池隔离、狼与 bear 分配、随机次数、生成前预计数、800/500 ms 等待、提交与失败计数、龙裔占位回退、旧局失败回调隔离和安全点选择。

Unity 对象与销毁、随机源、时间等待、点位准备及 EnemySpawnRuntime 工厂边界为可控替身；替身不复制生成算法。真实工厂和后处理由 EnemySpawnRuntime 既有夹具覆盖。玩家对象必经销毁并模拟 Unity 假 null。夹具不证明真实导航、模型、战斗或实际帧耗时。生产文件 SHA-256 随结果保存。
