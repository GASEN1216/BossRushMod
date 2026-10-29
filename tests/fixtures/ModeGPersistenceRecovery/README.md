# ModeGPersistenceRecovery

直接编译生产 `ModeGPersistenceFlushCoordinator`、`ModeGProfilePersistence`、`ModeGNemesisPersistence` 与共享 `BossRushSaveFileThrottle`。逐个以独立测试进程运行，保留 Mode G 单向 fault 的真实契约，不用反射重置生产状态。

SavesSystem 替身只持有内存数据，模拟官方 `SaveFile` 置私有 `saving = true` 后在备份、索引备份或物理写阶段抛异常且没有 finally 的合同；帧调度可手动推进。三种故障标签代表同一官方异常窗口，不声称运行了真实 ES3 / 磁盘。普通保存繁忙时保持官方闩不动，成功后再续批。

覆盖 coordinator 正常/销毁路径与两 DTO 直接 `FlushPending(true)`，验证失败释放本次宿主闩、其他子系统可再保存，同时 Mode G 的 faulted / pending 语义原样保留；验证战斗顺延、非战斗补写、批内双 DTO 仅一次物理写。证据为 L2；不读写玩家存档。

运行：`python tools/run_runtime_regressions.py --filter ModeGPersistenceRecovery`。
