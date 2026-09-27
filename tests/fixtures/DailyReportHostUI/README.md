# 日报 UI 宿主归位回归

运行 `python tools/run_runtime_regressions.py --filter DailyReportHostUI`。构建产物仅写 `Build/runtime-regressions/DailyReportHostUI/`，不启动游戏、不访问玩家存档。

完整编译生产 `DailyReportRuntimeModule.cs`、`DailyReportRuntimeModule_UI.cs` 与 `Utilities/SafeRuntime.cs`；宿主入口取当前 `IntegrationHostCompatibility.cs` 的完整 DailyReportUIBridge 区域，装配取当前根的原创建/注册语句，View 清理使用 `DailyReportUI.cs` 的真实 `CleanupRuntime` 方法。输入 SHA-256 记录在输出目录。

覆盖同实例装配、一次实时开关读取、缺官方 UI 管理器后重试、原两次管理器读取、复用面板、禁用门、过图销毁及重建、创建 null 与异常边界，以及完整模块清理中保存/退订/面板/报箱/缓存的顺序。Unity 替身模拟 destroyed-null 和销毁父 GameObject 连带销毁组件/子物体。存档和 UI 呈现是记录边界，生产注入字段没有非空默认值。此夹具给出 L2，真实报箱交互及画面仍需 L3。
