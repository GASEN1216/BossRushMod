# 丧尸模式联机个人资源回归

聚合入口：`python tools/run_runtime_regressions.py --filter ZombieCoopFlow`。

逐字提取生产 `ZombieModeRuntimeModule_Coop` 的有效参与者判定、开局／奖励准备屏障、安全区与撤离全员判定、个人收益收据、原资产钱包和入场退款队列，联结原奖励污染计算、Boss 波判定及撤离现金入口。提取来源与 SHA-256 写入 `Build/runtime-regressions/ZombieCoopFlow/source-hashes.txt`。

覆盖 16 人动态参与者、死亡／断线／拒绝入场、选择准备、各人收益、重复结算、延迟收据、换槽隔离、切回原槽后领取与同一退款重复请求去重。钱包、NPC、Unity 对象、传输及个人存储都是内存替身，不读取玩家存档，不启动游戏。

结果属于 L2；不能证明原生库存寄存、真实场景加载、区域伤害、临时服务交互、物理掉落及 Steam 房间稳定性。真实操作步骤见 `docs/reports/testing/2026-10-13-联机全量适配验收.md`。

本轮 2026-10-13 边界补充：钱包键冻结为 room:asset:abs(run)，当前 room 收据查自己的资产和 run；相同数字 run 跨房互不覆盖。个人实际 cash 清零后观察原钱包 MaxWave，并成功 Report 原 Campaign lease 才置 Extracted；世界 SuccessExit 先到不能 ACK 欠款。Tracker 真合同实现由 CampaignPlayability 单独验证，本夹具 adapter 记录原 lease 和波数，验证新局 99 波不污染旧局 5 波。原 client projection 失去 room/role 后调用既有 cleanup，冻结钱包仍可补账；真实 run-only cleanup 顺序由 ZombieModeHostOwners 链接。
