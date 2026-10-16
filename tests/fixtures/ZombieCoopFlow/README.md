# 丧尸模式联机个人资源回归

聚合入口：`python tools/run_runtime_regressions.py --filter ZombieCoopFlow`。

逐字提取生产 `ZombieModeRuntimeModule_Coop` 的有效参与者判定、开局／奖励准备屏障、安全区与撤离全员判定、个人收益收据、原资产钱包和入场退款队列，联结原奖励污染计算、Boss 波判定及撤离现金入口。提取来源与 SHA-256 写入 `Build/runtime-regressions/ZombieCoopFlow/source-hashes.txt`。

2026-10-16：撤离方法使用原生产文件的 using 声明独立编译，不能再由共用生成头掩盖缺失命名空间。真实 `BuildCoopHostWaiting` 与共享 `BossRushUIDepth.ApplyButtonDepth` 原方法一同编译、执行；验证创建等待按钮、隐藏选择面板、实际委托可再次打开确认且资格撤销后不再调用。原生 UI 对象、贴图和底层 ApplyDepth 绘制仍为明确隔离边界；不声称真实画面通过。完整真实 Delivery 子组仍独立执行 8 条断言并核本次 TargetPath。

覆盖 16 人动态参与者、死亡／断线／拒绝入场、选择准备、各人收益、重复结算、延迟收据、换槽隔离、切回原槽后领取与同一退款重复请求去重。钱包、NPC、Unity 对象、传输及个人存储都是内存替身，不读取玩家存档，不启动游戏。

结果属于 L2；不能证明原生库存寄存、真实场景加载、区域伤害、临时服务交互、物理掉落及 Steam 房间稳定性。真实操作步骤见 `docs/reports/testing/2026-10-13-联机全量适配验收.md`。

本轮 2026-10-13 边界补充：钱包键冻结为 room:asset:abs(run)，当前 room 收据查自己的资产和 run；相同数字 run 跨房互不覆盖。个人实际 cash 清零后观察原钱包 MaxWave，并成功 Report 原 Campaign lease 才置 Extracted；世界 SuccessExit 先到不能 ACK 欠款。Tracker 真合同实现由 CampaignPlayability 单独验证，本夹具 adapter 记录原 lease 和波数，验证新局 99 波不污染旧局 5 波。原 client projection 失去 room/role 后调用既有 cleanup，冻结钱包仍可补账；真实 run-only cleanup 顺序由 ZombieModeHostOwners 链接。

周期 starter/reward-done 改用显式 repeat 路径，真实 SendCoopZombieCommand 现与 OnUpdate 一起提取执行；两个同样的手动 beacon 仍记录两次 Request。传输边界只记录选择的接口，不实现恒真去重；实际完整 Channel 的去重、lease 与 ACK 由 CoopProtocol 执行。当前 61 条断言通过，个人收据/退款和钱包结算不加世界绑定门。

2026-10-16 M04/M06：新增真实撤离资格、Starter 确认／提交、Select、Finalize、host skip、个人退款、迟加入 admission 与实际确认选项方法。断线撤离仅按最后真实圈内／倒计时资格，发原资产的完整已获得点数。选流派先发真实 starter 意图，只有房主确认该原资产后才调用原完整 pack；房主先跳过则回滚原库存转存并退全部暂扣现金／邀请函。退款失败保留原资产债务，换槽不发，恢复原槽重试恰好一次。新局到来先结清旧未确认入口；本场被跳过者下一场可选择。Active 中未被显式跳过的合法迟加入仍沿原付款、选择、host 确认和完整开局包参与后续玩法。只有被显式 skip 的本场玩家及非原确认资产拒绝个人准入；已暂扣的 skip 收据完整退款。仅选择在途不能提前把个人运行态推进 Active。

客端执行真实 host StarterReady 数组同步和 Campaign getter，离线／当前 Active 无要求，错误 run 或新槽不继承旧资格。房主等待页继续持有实际 KeepCoopHostStartChoice 判据，取消共享确认后可再次人工点击；真实 Show 的 anchor、token 与生命周期撤销分别记录。具体 Canvas 创建、输入租约、动画与 Unity 销毁仍属 L1，未作 L3。Grant 工厂、Rollback 外层、EntryDebt 的钱包／存盘接收器是显式计数／故障边界，不在此夹具复造原物树；真实转存／回滚另由 AuditModeLifecycle/transfer 执行。实际入口的 Execute→Register 与 ApplyWorld→Admission 调用由结构守卫钉住，不把这个派发适配器说成完整网络协议。

主组通过后顺序执行真实个人交付子组：同份 Complete、OnUpdate、Apply、钱包结算，链接完整 `CoopPersonalDelivery.cs` 和 codec。在线原资产必须实际提交并 ACK；离线合格者欠账保留，同时允许在线成员推进原生返程边界。新 generation 与未绑定世界不阻止既有永久收据重试；新槽拒收无 ACK，原槽重连后完整 7+7+7 净化点现金结算，重复包不增量。仅传输与原生返程函数采用内存边界，实际磁盘现金、Unity 清理和原生场景到达仍需 L3。
