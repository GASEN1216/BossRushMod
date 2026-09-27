# ModeDestroyLifecycle

直接执行当前生产 `ModeD/ModeDRuntimeModule.cs` 和从 E/F owner 提取的 `OnAwake/OnDestroy`、模式结束与会话门。D 清场、E 共享状态复位和角色清理、F 退出/延迟销毁/工事清理、E/F 玩家与敌人 Modifier 移除、死亡/掉落事件解绑均提取生产方法；完整链接 `ModeEFEnemyRegistry`、`RunScopedRegistry` 和 `ModeFModels`。提取不重写控制流，输出生产 SHA-256。

覆盖直接销毁活跃模式与启动中断残留、重复销毁、失效 session、死亡和掉落订阅、协程停止、角色与组件 Unity 假 null。卸载不发待领奖励；已消费但未提交的工事和维修物品只退款一次，已提交的不退款。另覆盖正常 F 退出退款顺序和 0.25 秒等待。宿主转发动作、UI、壳事务、引擎物理和商人实体是边界替身；商人异步生产逻辑与真实 session gate 由 `AuditModeLifecycle/merchant_owner` 补充。替身不会代做被测试的模式结束、角色删除、事件退订或 Modifier 移除。

由 `python tools/run_runtime_regressions.py --filter ModeDestroyLifecycle` 运行。`BOSSRUSH_FIXTURE_OUTPUT` 可将验证产物定向独立目录。L2 不能证明 Unity 帧末销毁、地图/资源或游戏内卸载 UI 正确。

运行态的 cleanupPending 由生产 BeginSession 置位，在完整 End/Exit 收尾后释放；用例覆盖从未启动、已正常退出及启动中断（active 已关闭但 cleanupPending 尚在）的不同归属，不用未启动模式清其他模式的玩家阵营或共享缓存。

预热另外抽取真实 Schedule、Prepare 与统一 Stop：在 BeginSession 前启动，手动推进生产 IEnumerator 到商人子协程等待后直接销毁，验证外层和独立 child 都停止。宿主替身在销毁后对 StartCoroutine/StopCoroutine 抛错，自动停止已有协程；用例覆盖 fake-null 宿主仍执行 C# 模式清理。F 正常退出中的对象与 Coroutine 由同一账本保留，既测正常等待完成释放，也测等待未结束时直接模块销毁和原生宿主销毁。
