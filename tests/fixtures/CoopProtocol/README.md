# 联机协议执行回归

真实生产逻辑：`BossRushCoopProtocol`、`CoopGameplayChannel`、`CoopPersonalDelivery`。覆盖 16 人身份独立、重放、场景屏障、客户端命令归属、房主快照顺序、个人收据重发与跨场景确认。

宿主替身：以内存队列替代 Steam / LiteNetLib；消息读写直接链接生产 BossRushCoopJson、共享解析器与转义工具，没有 JsonUtility 替身。没有替代生产命令/收据判据。此夹具不运行 Unity、场景、AI 或真实网络，不能证明实机可玩。

运行：`python tools/run_runtime_regressions.py --filter CoopProtocol`。

追加覆盖本机失败入待领队列、同步 ack/回调修改队列、同房间跨图不丢义务；本地及远端切槽、切回原槽重试、删除槽身份失效和旧槽在途命令拒绝；稳定参与者断线重连后命令序号重新开始。

独立复审追加：客户端暂时没有 RunId 的原生重连窗口保留已交付收据，丢失 ACK 后同房间重发不得重复发奖；进入新房间才清理旧运行期义务。已实跑旧码转红及修复通过。

收据分组查询执行真实递送队列：跨场景的逻辑 ID 去除代次前缀，调用者的结尾分隔符区分第 1 / 10 局；离线或已换槽的欠账只退出当前等待条件，切回原资产后仍足额交付，查询不会清除或刷新队列。实际编码、重试及 ACK 均沿生产实现，网络和存储回调仍是可失败的隔离边界。

2026-10-15：`ChannelScenes.cs` 直接执行整个生产 Channel，加入实际 Core 引用/handle、native service/host peer、资产、ReadyEpoch 与 SceneToken 可独立变化的边界。未绑定时保留原交互，Send=false 与丢 ACK 保留原序号；seq2 先到不能吞掉 seq1，同 payload 的两次真实交互保持两份。快照在实际 Core/token 绑定后应用，旧 token 的高 revision 不能覆盖正确状态。Core 替身含销毁等于 null、卸载及同图新实例，默认无 Core/无 token/未绑定，由用例显式装配；token 的产生与原生 Begin 关联由 CoopInstalledApi/SceneIdentity 和 CoopSession 独立验证。

命令 ACK 只确认终结分发，不代表购买、入场或奖励成功。生产处理器在业务副作用之后抛错时，Channel 仍透传异常，并在调用前消费序号、finally 发送 ACK，防止同序号重试再次产生副作用；后继独立命令仍可执行。用例包含 throw-after-effect 及乱序缓冲组合。此次隔离执行为 141 条断言，实际网络可靠性、Unity 加载与帧耗仍须 L3。

2026-10-15 周期重报补审：当前 156 条断言。真实 RequestRepeat 在同一 Core/handle、room、service/host peer、asset、ReadyEpoch 与 token 下复用尚未 ACK 的相同 action/value/text/flag；10000 次相同重报仍为 1 条，512 个不同事务及两次相同手动操作足额保留。Send=false 不因同事实轮询突发重发，正常 Tick 继续重试；ACK 后可重新报告业务事实。不同 lease 必须创建新请求。实际 SDK 网络与帧耗仍待新候选 L3。
