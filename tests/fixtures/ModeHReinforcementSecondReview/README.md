# Mode H 增援第二轮复审执行回归

覆盖 CR-2026-09-05-013 / 014 / 016。兼容分类 COMPAT；不读写玩家存档，不启动或部署游戏。

运行：`python tests/fixtures/ModeHReinforcementSecondReview/run.py`。需要 .NET 8 SDK；C# 语言版本限制为 7.3。输出与来源 SHA-256 在 `Build/fix-20260905-r2/ModeHReinforcementSecondReview/`。

每次运行重新从当前生产源码读取并编译：

- 完整 `ModeHSpawnTransaction.cs`，不替换其事务或异步所有权逻辑。
- `CombatProfiles` 整个“敌军分批入场”区域，包括字段、拆批、容量门、协程、所有权校验和释放。
- 完整 `TickActiveCombat`、`ReleaseCombatRuntimeObjects`、`IsCallbackStillValid`。
- 完整 `ModeHCombatControl.Tick`、`OnEnemyEntered`、`OnEnemyBatchEntered`、`SetEnemySpawningPending`。

Unity、preset/生成端、遥测伤害输入和其他战斗效果是可控替身。UniTask 替身只提供语言级 awaitable/状态接口，使用测试主线程同步完成的 TaskCompletionSource；生产代码仍沿用 UniTask，不引入线程池 continuation。该夹具不能证明 Unity 生命周期或真实场景物理效果，正式 Windows 编译与游戏 smoke 仍应单独执行。

执行用例涵盖同步创建仍分帧、真正全批胜利对照、异步挂起与去重、整批容量与预留、超过全局上限、多个批次持有到收尾、preset 缺失、同步创建异常、异步故障、提交失败、提交后登记异常、超时收尾、停止迭代器后晚成功/晚故障、完成到协程恢复之间取消、重复回滚、同 owner/scene/match 下新旧本场版本、不同 matchIndex、场景代次变化、RelayPending、初始/接力选手事务、取消后重用事务与旧迭代器恢复、逐帧预算等待时取消、死亡选手不判胜。

`ModeHReinforcementSecondReviewGuard.py` 另外保持生产接线/所有权结构不变量，并在内存副本上运行 11 个负向变异。guard 不替代本执行夹具。
