# 鸭王杯联机执行回归

执行完整生产 `ModeHCoopLedger.cs`，以及从当前源码提取的房间命令、准备屏障、开战收据函数。覆盖 16 人准备、场景屏障、断线、旧对阵拒绝、阵容冻结、个人选择恢复、付款失败、收据重放、押品弃赛与个人名人堂。

替身只承担房间传输、Unity、存档接口和原押注服务；不证明真实联机传输、钱包存盘或游戏画面。原现金与押品服务由已有 Mode H 回归覆盖。

用 `python tools/run_runtime_regressions.py --filter ModeHCoop` 运行，显式构建并执行本次 TargetPath，记录产物与生产源码 SHA-256。

2026-10-13 追加原房主资产换槽后的共享 owner、个人弃赛和 Season 写入门、重入角色观战租约、已死未选择房主与加载中 null 的区别。当前 48 条断言覆盖房主加 15 位客人准备仍可前进；个人凭证使用真正联机 codec，嵌套赔率和结果不再以 System.Text.Json 替身充当生产编码。

本轮 2026-10-13 边界补充：新增真实 TickCoop 与 ReleaseCoopProjection 的跨原生房间/StopNetwork 回归，释放旧 spectator/运行态后才能处理新 room 或离线帧。角色、镜头和 UI 对象仍由明确的引擎适配器承接，不能据此声称真实游戏镜头正确。

2026-10-15 原生世界 owner 回归：执行当前生产的冻结房间/原角色判据、TickCoop、TryTransition、异步 callback、RequestExit、ShutdownRuntimeInternal、原押品返还及显式续赛调度。实际 StopNetwork、非空新房间、host 变 guest、原单机连入新房间拒绝旧世界；同房间成员离线、SessionReady 暂失和原 host 的空握手保留世界。显式续赛先冻结当前房间再发起加载，取消中的请求不能停留为无法操作的壳。真实 PrepareGroupCustomUnitAsync 以可延迟工厂完成，房间/角色/连接失效后原 handle OwnerInvalid 回收，不提交旧角色。投影在 Stop 后、清理 Tick 前调用实际 StartGroupMatch 时不能转成单机世界。

世界 owner 新用例的服务/引擎边界明确为原生角色状态、工厂 Task 及 handle 清理记录、外层 Shutdown 宿主、存档与押注最终接收器；实际 ShutdownRuntimeInternal / TryReturnRealStakeOnAbort 执行。真实 UI StartGroupMatch 与真实 roster 准备条件、TryTransition 执行，自动推进器只调用 firstStep，未运行 Unity 战斗。原现金押注、ledger 和个人 delivery 实例保持，不用网络中断冒充弃赛。当前 88 断言提供 L2 证据，真实票款/押品持久化和多人恢复仍需新 DLL 实测。

2026-10-15 旅行缓存补审：当前 95 断言；新增提取完整生产 `TryPrepareCoopBet`，不再用空方法跳过预付。实际 TickCoop 与延迟调用在未 bound 或缓存 token 已过期时不 Prepare 原 ledger、不调用现金 reserve、不请求 ready、不驱动旧 AI 摄像。个人 ledger 与 result Tick 继续，原 Reserved 押款不因加载间隙被清理或再扣；明确输入当前新快照状态后，真实 ledger Prepare 与完整金额的 reserve 重新工作。现金 reserve 是记录真实请求参数的外部边界，不证明官方钱包存盘；缓存状态输入显式模拟生产 Apply 之后的本地世界状态，Apply 的 token 写入另由结构守卫钉住。实体/镜头/传输仍是替身，本轮不新增 L3 结论。

周期 funded 采用 RequestRepeat，手动 ready/bell/reroll 保留 Request。此组传输边界仍只记录参数/次数，未在替身实现或声称去重；真正 lease、ACK、不同事务数量与 10000 次重报行为由 CoopProtocol 的完整生产 Channel 执行。95 条原 H 断言仍通过。

2026-10-16 M06：执行真实 StartCoopWithConfirmedPlayers、QueueCoopSkippedRefunds 和完整 ModeHCoopLedger.CancelUnstarted。房主本人已确认资金后，可显式开始并跳过未确认者；迟到 funded 不得加入本场，退款收据冻结原资产，本场 skip 不延续到下一场。现金按原 Reserved 金额完整退还，押品仅解锁原物，失败保留原凭证，重报不重复付；已标记 combatStarted 的实际参赛押注不得借 skip 退掉。此处取消的是本场尚未确认押注，不回退已完成的赛季入场船票。现金、存盘、押品最终 receiver 是受控边界；不能据此声称真钱包或 Item 实例已归还。
