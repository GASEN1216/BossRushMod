# AchievementRuntime

通过 `python tools/run_runtime_regressions.py --filter AchievementRuntime` 运行。

直接编译完整的 `AchievementTriggers.cs`、`AchievementRuntimeModule.cs`、宿主兼容 Hooks 与 `AchievementMedalItem.cs`。另外抽取当前根装配语句、原销毁槽位的成就清理及 Manager/Icon 重置语句、勋章 TypeID/存档键/默认库存常量。生产查询字段不设替身默认值，通过真实绑定语句装配。

覆盖初始化及两次 Popup 请求顺序、异常初始化后原外层路径、同实例注册、实际退订先于缓存重置、Medal 原独立订阅时机、重复清理、Legacy 引用去重、Mode G 三元组去重及会话隔离、玩家事件门、Health 重绑和销毁、快捷键实时读取与官方界面门、勋章注入幂等、库存保存、跨槽缓存失效与失败 fallback。

Unity/输入、事件源、成就统计及落盘服务、商店和资源为记录替身。成就 Manager/Tracker 的持久化内部实现、真实 Steam 弹窗、完整库存和场景行为不在本夹具范围。Unity 替身模拟 destroyed-null 和销毁 GameObject 连带销毁组件，场景销毁为必经用例。生产源 SHA-256 写入对应构建目录；结论属于 L2。
