# 随机事件房间投影回归

执行当前生产 `RandomEventDirector` 联机捕获、应用、客户端 Tick 门控、上下文创建和清理；按 16 个独立成员检查 11 类事件的重复快照、迟加入、换图清理、同种事件下一次触发与房主拒绝投影。每次先构建，再执行本次 TargetPath，并记录源码与程序集 SHA-256。

同时提取生产 FX 接收与场景清理，验证真实房主来源、房间/场景身份、重复 Guid、非法坐标/持续时间、空投落点标记的限时 owner 和换图销毁。Unity 替身模拟销毁对象等于 null 及 GameObject 附属 Transform 一并销毁；定时销毁只记录期限，实际 Unity 定时器仍属 L3。

Unity、事件效果和网络角色是隔离替身。本夹具证明调度/投影所有权；实际三种 Tempo 属性由 `RandomEventTempo` 夹具链接生产事件及属性追踪器执行，原生世界物件同步和 16 个游戏客户端仍须 L3 验收。

2026-10-13：消息 Read<T> 改经真实 BossRushCoopJson 往返，工程直接链接实际 codec、共享解析器和 helper；FX 来源/身份/重复消息用例同时执行当前字段编码，不再用 (T)Payload 跳过网络字段边界。

真实 OnSceneChanged 与冻结原局 ValidateCoopRun 已纳入提取，执行同房暂空、Stop 正好清一次活动效果、旧模式/旧地图 reconnect-as-host 不升级，以及真实旧模式结束和新单机允许。不是恒真世界权限：模式签名、联网状态、room 与本地 handle 可分别变化。
