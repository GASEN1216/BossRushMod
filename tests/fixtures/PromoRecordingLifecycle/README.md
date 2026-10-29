# PromoRecordingLifecycle

直接编译完整生产文件 `DebugAndTools/PromoRecording/PromoRecordingDirector.cs`，开启 `BOSSRUSH_DEV`。从 `SpawnTargets`、`SpawnBloom`、`Begin`、`StartStep`、`End` 和实际 `OnDestroy` 驱动生成与取消，不复制生产判据。

替身边界：Unity 组件销毁连带语义、可变场景加载状态、`FieldAdventureSupport.TryGetRaid` 的外部观测面，以及可控延迟的官方角色工厂 / 花冠工厂。工厂刻意忽略取消并交回迟到对象，验证生产侧最终提交仍拒绝它。UI、物品发放、实际导航和花冠战斗表现只是可编译替身，不能据此证明实机行为。

覆盖结束录制、加载开始、原场景卸载、主角替换 / 死亡、宿主销毁、进入独占模式、同载旧场景、新录制 / 重拍、花冠在途预留、旧回调不得污染后继请求、失败重试；另验证停止录制保留已提交的拍摄敌人及花冠战斗判据。

运行：`python tools/run_runtime_regressions.py --filter PromoRecordingLifecycle`。证据等级 L2；不启动游戏、不接触存档。
