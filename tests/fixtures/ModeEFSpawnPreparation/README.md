# E/F 共用刷怪准备执行回归

运行 `python tools/run_runtime_regressions.py --filter ModeEFSpawnPreparation`。

直接链接完整生产 `Utilities/ModeEFSpawnPreparation.cs`。覆盖原图 Points 世界坐标优先、已销毁 spawner 跳过、同场景复用和换场景重扫、配置点优先、10 米严格边界、玩家阵营优先及独狼轮询、清理参数和两个服务实例隔离、缺图点时的后备请求，以及安全落点选择和传送后通知次序。

替身提供 Vector 运算、场景、Points、Unity 对象销毁判空、主角 Transform、地图配置、ObjectCache 和受控 NavMesh 结果；配置/主角与注入委托均由用例显式装配。后备点生成委托只记录调用，不重写 D 的随机后备算法。该 L2 回归不模拟真实场景扫描、NavMesh、物理碰撞、显示或帧耗；E/F 共用同一生产实例由 `ModeEFSpawnParityGuard` 核对装配路径。
