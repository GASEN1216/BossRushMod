# PromoRecordingLifecycle

直接编译完整生产文件 `DebugAndTools/PromoRecording/PromoRecordingDirector.cs`，开启 `BOSSRUSH_DEV`。从 `SpawnTargets`、`SpawnBloom`、`Begin`、`StartStep`、`End`、实际 `Update` 和 `OnDestroy` 驱动输入、生成与取消，不复制生产判据。

替身边界：Unity 组件销毁连带语义、可变场景加载状态、`FieldAdventureSupport.TryGetRaid` 的外部观测面，以及可控延迟的官方角色工厂 / 花冠工厂。工厂刻意忽略取消并交回迟到对象，验证生产侧最终提交仍拒绝它。按键、暂停、官方输入许可、共享模态租约及 EventSystem 文本焦点均为可控的外部观测值；不重写其官方状态机。UI、物品发放、实际导航和花冠战斗表现只是可编译替身，不能据此证明实机行为。

覆盖结束录制、加载开始、原场景卸载、主角替换 / 死亡、宿主销毁、进入独占模式、同载旧场景、新录制 / 重拍、花冠在途预留、旧回调不得污染后继请求、失败重试；另验证停止录制保留已提交的拍摄敌人及花冠战斗判据。

输入回归从实际 `Update` 投入全部 10 个导航 / 小键盘热键，分别验证官方暂停、F3 时间归零、共享模态租约、官方输入禁用、legacy / TMP 文本焦点均阻止换镜头、重拍、暂停、刷靶和结束；正常游戏仍能使用全部热键，录制自身的暂停可以原键恢复，非文本选中与已失焦输入框不阻塞快捷键。

运行：`python tools/run_runtime_regressions.py --filter PromoRecordingLifecycle`。证据等级 L2；不启动游戏、不接触存档。
