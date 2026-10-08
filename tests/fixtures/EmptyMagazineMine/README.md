# 空仓地雷盒执行回归

唯一运行入口：`python tools/run_runtime_regressions.py --filter EmptyMagazineMine`。
使用 .NET 8 或兼容 SDK；工程允许向已安装的新主版本运行时滚动，兼容本机仅安装 .NET 10 的环境。
聚合入口在独立目录构建，按本次 MSBuild 返回的 TargetPath 执行并记录 SHA-256。
不要在夹具目录运行 `dotnet run`。

直接链接完整生产 `EmptyMagazineMineRuntime.cs`、`EmptyMagazineMineRules.cs`、
`EmptyMagazineMineConfig.cs` 和 `ConfigItemIds.cs`，从真实 `Subscribe()` 注册的事件驱动生产路径。
次数、末发判定、换弹与换手清理、冷却、取消、引信、伤害过滤和结算中失效没有替身实现。

明确场景序列覆盖：至少六次真实开火后末发才布雷；五发弹匣不合格；霰弹弹丸不算额外开火；
提前开始换弹后取消仍清次数；装填完成兜底；切到另一把再切回不能接续；冷却中的空仓不补发；
已有地雷不叠第二枚；卸下重戴不洗冷却；死亡、重新绑定、换图与 cleanup 取消；暂停冻结引信；
视效异常不吞结算；主角、当前阵营友军、遗种随从、中立、死亡角色、遮挡、同 Health 多碰撞体；
伤害回调里卸下装备会让剩余目标作废；订阅幂等且静态与实例事件均退订。

Unity 对象、官方事件发布者、当前装备缓存、物理碰撞结果与视效是宿主替身。
替身保留 Destroy 后 `== null`、销毁 GameObject 连带销毁组件与子节点，并运行真实 driver 的 OnDestroy；
每个场景清理必经对象图销毁，另有明确的切图后重建用例。
装备缓存没有启用默认值，测试必须先发装备事件；枪在扣弹后才发一次射击事件，装填完成事件与开始动作事件独立。
Team 替身保留官方当前阵营与 middle 语义，Health 只记录收到的伤害，不重造护甲公式。

这份回归不证明 Unity 真实物理命中、预警圈渲染、声音、图标、伤害减免后的数值或游戏手感。
它也不覆盖装备注册、掉落产出及共享装备缓存本身的实现；这些由对应接线检查与实机验收验证。

2026-10-08 全面复审补充：官方 SceneLoader 先设置 IsSceneLoading 并广播开始事件，之后才渐变黑幕、卸旧场景和初始化目标场景。
新增场景保留渐变期间的旧角色与敌人，验证立即取消、无事件通知时的状态门、伤害回调中开始切图阻断剩余目标，
以及加载尝试结束但未初始化新场景时能恢复。另模拟旧 driver 延迟 OnDestroy 通知，确保不误清新地雷。
旧生产源码先在 scene start immediately cancels mine before level init 断言失败，修复后再执行通过；不把原有直接 BeginLevel 的替身顺序当作官方切图顺序。
