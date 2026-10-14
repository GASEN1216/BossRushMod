# Mode E 原局永久奖励与交付

运行 `python tools/run_runtime_regressions.py --filter ModeECoopEpoch`。入口每次明确构建并执行本次 TargetPath，真实 codec 与源码哈希随日志保存。

逐字提取生产个人收据、原资产 epoch 账本、房主累计击杀发布、每 10 次挑衅烟雾弹与仓库树身份去重，同时链接真实 `TryGiveItemToPlayerOrDrop` 和 `SkyIslandInventoryTransaction.TryDeliver`。覆盖 16 个原参与者、换槽、迟到旧收据、乱序、部分交付重试、工厂/保存失败、20 次对应 2 件、通知异常、真实合堆差额与原生入包/Drop 提交后异常。

宿主替身只承接 Unity、原生物品/仓库与支付前后状态；AddItem、合堆、Drop 刻意模拟官方提交早于通知的时序。原生物理背包、真实 UI、Mono、磁盘写入与跨进程连接不由本夹具证明。停止网络/换房执行真实 projection owner 退出方法，EndModeE 的引擎收尾由适配器记录调用；完整生命周期在 AuditModeLifecycle 等夹具另外链接。
