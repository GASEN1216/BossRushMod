# 房间生命周期执行回归

链接完整生产 `BossRushCoopRuntimeModule.cs` 和 `BossRushCoopProtocol.cs`，构建时从门面逐字抽取完整 `BossRushCoopPlayer` 声明并记录源码 SHA，经真实 `OnAwake`、`OnUpdate` 和入站消息分发验证：原版 Direct 未发断线事件时的成员退场、轮询前快速重连、稳定参与者恢复、客户端断线就绪撤销、切房清空旧 roster/场景代次、乱序 hello 不回滚资产、延迟 welcome 不回滚房间。

Unity、原生传输、角色和世界服务是受控替身；入站 payload 和 envelope 直接调用生产 BossRushCoopJson，链接共享解析器与转义工具，生产判据未被替换。此夹具不运行真实网络，也不证明实际 2–16 人可玩。真实 API 字段由 `CoopInstalledApi` 另行核对，真实投递仍须在游戏进程验收。

就绪流量用例通过 15 次实际入站 hello 建立 16 人生产参与者表，逐帧重复声明同一准备事实，验证立即发送量、全员屏障、客端周期重发和新代次同名屏障重报。周期重发只观察实际编码后的发送记录，不把它称为真实网络丢包恢复；实际同图重载和迟加入仍需独立场景及 L3 证据。旧逐帧实现会在发送量断言转红。

运行 `python tools/run_runtime_regressions.py --filter CoopSession`。聚合入口编译并执行新的 TargetPath，记录源码和产物 SHA-256。

存活用例通过实际 16 人名单编码、生产入站解析和真实 player.IsAlive 验证房主死亡事实覆盖健康代理、本机生命优先、未知成员、代理旧死亡状态、非房主/旧房间/旧代次/旧版本拒绝、资产更换、实际换体、同图新 Scene handle 在房主代次到达前隔离、断开与重连等待新事实。独立 wire 输入 DTO 只生成前向兼容字段，没有替换生产 roster 接收器。原无 alive 字段生产和旧 IsAlive 实跑失败；这证明 L2 行为，实际网络时延与游戏代理生命周期仍须 L3。
