# 截信人个人扣物事务执行回归

聚合入口：`python tools/run_runtime_regressions.py --filter WaylayerEscrow`。

逐字抽取生产 `CoopWaylayerEscrow`，及首领的 `BestTarget`、`GiveBack`、`AcceptCoopSnatch`、`CompleteCoopSnatch`；记录源码哈希，执行本次新建 TargetPath 与 SHA-256。覆盖最贵耗材选择、整堆仅一件、房主库存不受客人劫包影响、请求/响应/决定重复、个人资产冻结、工厂 await 后切档/销毁/同图重载、实物归属确认、退款失败与重试、通知抛异常后的实际提交、新房间取消与退订。

Unity 对象销毁、库存操作、官方 ItemTreeData 序列化/异步工厂和网络发送是受控替身。替身明确提供失败或通知异常，工厂通过 TaskCompletionSource 暂停真实生产续作；不预填非空 prefab，测试显式登记资源。个人收据的传输与去重由 CoopProtocol 的独立生产回归验证，本夹具只捕获收据并实际执行生产应用回调，避免恒真发奖替身。它不验证网络延迟、真实背包事件、尸体箱拾取或游戏实机效果，属于 L1/L2。

发送替身经真实 `BossRushCoopJson` 往返嵌套物品树与变量列表；直接链接共用 token/parser/转义工具，没有自制 codec。新房间即使分配了不同 peer ID，也只向原个人资产返还未决预留。
