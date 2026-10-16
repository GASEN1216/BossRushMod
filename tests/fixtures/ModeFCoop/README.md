# Mode F 联机个人结算回归

通过聚合入口 `python tools/run_runtime_regressions.py --filter ModeFCoop` 执行。逐字提取生产 `ModeFRuntimeModule_Coop.cs` 的开局账本、Tick、世界快照、个人收据、击杀交付、印记查找和撤离函数，记录源码及本次 TargetPath 的 SHA-256。

覆盖 16 位玩家资产冻结、换槽无成绩继承或旧局 starter、切回恢复、击杀乱序与重放、inactive 世界快照先到、撤离先到与连续击杀收据随后到、补给完整保留和撤离恰好一次。房间名册、传输、装备工厂、成长效果和实际寄存落地为受控替身；测试真实回调顺序及付款归属，不能证明 Unity 场景、原生库存存盘或多进程传输。

2026-10-13 追加 E/F 实际准入事务：消费单机原数量语义的整件营旗／收发器及船票，完整发装失败回收新物，原对象退款失败保留，预付票物化失败重试、原资产归属、再次入場等均执行生产 CommitCoopModeEntry。

跨局追加完整生产 epoch 账本、原补给类型阶梯、官方缓冲债务方法和 OnModeFExtractionSuccess；链接真实 BossRushCoopJson + BossRushJsonValue + SimpleJsonHelper。覆盖旧收据在新世界后抵达、部分补给成功、工厂失败、存盘失败、缓冲重新加载、跨槽原身份、旧成长不影响新局、原 1/3/10/20 数量、悬赏类型只抽一次、当前结算不重复发奖与本局返程只等待本局 ACK。该缺陷修复前真实回归为红；最新 93 条断言为绿。原生 Buffer/工厂／背包和保存适配器为受控替身，不能证明真实磁盘、进程退出或资源拾取。

本轮 2026-10-13 边界补充：冻结账本还链接真实 Campaign lease 调用点：连续个人 kill 必须先 ObserveExtractionLeaseKill，VictimMarks 判定真实标记；旧 extract 先交原物、再 Report 原合同、最后置墓碑。Tracker 合同/存储实现由 CampaignPlayability 独立生产链接，当前夹具记录实际 lease/序号/marked 参数与失败重试边界。新 native room/client role 丢失走原 ExitModeF，旧补给按原账本补给，不复活世界；实际物品交付 helper 的合堆/通知异常由 ModeECoopEpoch 链接。

周期 admission 采用 RequestRepeat；此组网络端点只承接真实周期方法的接口，完整去重与 ACK 由 CoopProtocol 执行，F 工事周期由 ModeECoopEpoch/RepeatCallers 提取真实方法执行。个人付款、物品交付及原合同回执仍沿原方法，当前 108 条通过。

2026-10-16 M04：真实 Tick、RefreshCoopModeFExtractionEligibility、IsCoopModeFExtractionRecipient 与 CompleteCoopModeFExtraction 一起执行。断线成员仅保留实际最后圈内或倒计时已经开始时的存活资格；提前在圈外断线、已观察死亡、当前换槽均不得领奖。按原 participant.AssetId 发完整原印记／击杀结算；测试使用与房主不同的 original asset。个人迟到账本及原 16 人用例保留。房间与 delivery 仅记录请求，不能证明网络或原生圈碰撞。

主组通过后，`delivery_chain.py` 顺序构建独立子组，保留同份生产 Complete、Tick、个人 Apply，并链接完整 `CoopPersonalDelivery.cs` 与真实 codec。实际交付队列、ACK、去重及跨 generation 重试不再用主组的记录替身。覆盖在线收据未 ACK 时不返程、仅剩离线原资产欠账时返程、原生加载边界调用一次、换槽拒收不 ACK、原槽重连后完整 3+3+7 件寄存奖励及重复不增量。房间端点／原生加载计数和库存工厂仍是内存边界；这不证明游戏已经加载 Base 或物品已经落在真实存档。
