# 危险试用品完成事务执行回归

入口：`python tools/run_runtime_regressions.py --filter FieldAdventurePrototypeCompletion`，或单独执行本目录 `run.py`。默认连同四项隔离反向探针一起运行。

夹具逐字抽取生产 `OnUpdate`、`BeginLease`、`OnHoldChanged`、`RefreshVisuals`、`OnShot`、`IsHeld`、`ResolveShotDirection`、`TraceBlast`、`TickRecoil`、`FireSpecial`、`LaunchFinale`、`TickFinales`、`Explode`、`TryHurt`、`CompleteLeaseIfReady`、`EndLease`、`CleanupRaid`、`ClearFinales`，直接链接真实 `DangerousPrototypeRules.cs`，不复制达标或烟花算法。实际生产 BeginLease 订阅替身静态事件，测试通过该事件发射，按实际委托列表检查生产退订。

覆盖喷气炮同步达标、烟花主弹第 10 次命中达标、主弹仅中 9 次后由子弹达标；完成后清 borrower/gun/item、隐藏装置、一次通知与仍可单次领奖的规则状态；再次开枪和继续更新不重发通知。完成保留短视觉尾巴与全部已发射子弹，真实 TickFinales 继续爆炸并扣血，尾巴最终释放。还覆盖暂停、离手后完成、主动归还后的既有烟花不得推进已取消试验、场景退出清空烟花、超时只发回收提示以及重复 Cleanup 的幂等性。

替身边界：Unity 向量/对象/物理、角色/伤害宿主、试验台入口创建、对话、场景门与视觉。伤害宿主只按传入数值扣血，物理用解析平面与可控目标集，视觉只记录 Rig 隐藏/释放与烟花爆发；不能证明真实 PhysX、渲染、UI 可见性、复杂游戏伤害回调或性能。`Claim` 只验证生产规则的单次领奖状态，本夹具未验证真实试用品奖励交付。轨道炮/雷霆分发替身若被意外调用会抛错，不计作这两种试用品的伤害覆盖。

新增用例先执行旧生产代码，必须在“asynchronous main explosion: completion releases both event subscriptions”失败。四项反向探针分别移除异步完成入口、移除一次性 owner 门、完成时提前清空烟花、遗漏射击事件退订，要求对应行为断言失败。仅在 `Build/` 隔离副本修改，逐轮恢复原字节并记录 SHA-256；生产输入与实际新构建 TargetPath 哈希随日志留存。

证据为 L2。L3 需在 Dev F3 录制面板生成危险试验台，借用烟花试用品命中足够敌人后保持持枪，观察达标提示只出现一次、枪上装置及时撤去、剩余烟花继续播放；再按 F 与叮当或试验台交互领奖，验证回收、切图和死亡后的清理。任一无提示、装置残留、重复提示、尾效中断或重复发奖均不合格。
