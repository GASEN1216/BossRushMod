# 远端 AI 预测生命回归

完整链接 `CoopAiPredictionHealthProtection.cs` 与原 `BossLethalHealthProtectionPatch.cs`，使用本机已安装的 Harmony，在隔离 Windows .NET Framework 进程实际安装 Prefix、Finalizer 和 CurrentHealth setter 补丁。记录本次全新执行文件与 SHA-256。

Unity 组件、角色和网络由替身隔离；原生联机替身复现源码确认的“打前传 damageValue，客户端预测死后再传 99999”边界。验证标记 AI 仍传原伤害但不发送强制致死，房主高护甲重新裁决，真实死亡快照放行，本机身份/精确 Health 限制，嵌套与异常清理，异步负 AI ID 投影，非房主/旧场景拒绝，销毁退订与 marker 撤销预测资格。

实体角色单独链接完整生产 `TrackAuthority`、`MarkReplica`、`HasEntityAuthority`、`ClearWorld` 和 `OnDestroy`。验证停止网络后旧客端复制体不能变成单机世界实体；原房主实体不能被新房间重绑；旧客端成为房主后也不能重新登记旧复制体；刚创建的单机与新房主实体仍能正常执行。Unity 替身模拟已销毁对象等于 null 和销毁 GameObject 连带销毁组件，销毁是固定执行步骤。网络角色、房间和时间由测试控制，不能据此宣称原生断线与弱网实测。

这证明真实 Harmony 对生产保护和 setter 链的绑定与 L2 逻辑；未启动 Unity、读取玩家存档或实测工坊传输，不证明真实帧时序、网络命中特效及捏脸外观。人工 L3 按联机验收报告检查。

场景边界还逐字提取完整 `BossRushCoopFxRuntimeModule.OnHazards` 和三种生产 DTO，链接真实 `BossRushCoopJson` 编解码；完整预测 owner 源文件继续参与编译。标准聚合入口为 `python tools/run_runtime_regressions.py --filter CoopAiPrediction`，当前执行 54 项断言。网络 facade 的房间、场景绑定状态和旅行 token 由用例明确赋值，默认不构造非空身份。Unity 对象、危险体工厂和原生 AI 表仍是边界替身，三种危险体工厂只记录真实处理器的调用数量，不声称已验证 Unity 视觉。

实际 Begin 后数字代次尚未前移时，旧包、缺 token、错误 token、非房主包都不得创建危险体、改变同 ID 新角色的显隐缩放或取得伤害保护。当前绑定和精确 token 的包仍生成完整三种危险体。真实 Harmony Hurt 在 Begin 到 owner Tick 之间立即撤销旧保护；清理不擦除实体永久镜像角色。新 token 绑定后必须重新收到当前授权；房主加载前登记但尚未发布的合法实体在绑定后仍发布。

本轮独立负例在 `Build/coop-independent-audit/modes-fx-prediction-boundary/negative/results.json`：22 项守卫破坏实际转红，其中 7 项同时执行生产方法转红，全部按原字节恢复并重跑转绿。其余单层门有调用者的重复保护，未将它们声称为独立行为红例。旧生产源分别运行 Fx 和 Prediction 两个入口均真实失败，保存在同专题 `old/`；这仍是 L2，正式 Windows 整体编译和新场景实机留给根验收。
