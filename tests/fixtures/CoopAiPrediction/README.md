# 远端 AI 预测生命回归

完整链接 `CoopAiPredictionHealthProtection.cs` 与原 `BossLethalHealthProtectionPatch.cs`，使用本机已安装的 Harmony，在隔离 Windows .NET Framework 进程实际安装 Prefix、Finalizer 和 CurrentHealth setter 补丁。记录本次全新执行文件与 SHA-256。

Unity 组件、角色和网络由替身隔离；原生联机替身复现源码确认的“打前传 damageValue，客户端预测死后再传 99999”边界。验证标记 AI 仍传原伤害但不发送强制致死，房主高护甲重新裁决，真实死亡快照放行，本机身份/精确 Health 限制，嵌套与异常清理，异步负 AI ID 投影，非房主/旧场景拒绝，销毁退订与 marker 撤销预测资格。

实体角色单独链接完整生产 `TrackAuthority`、`MarkReplica`、`HasEntityAuthority`、`ClearWorld` 和 `OnDestroy`。验证停止网络后旧客端复制体不能变成单机世界实体；原房主实体不能被新房间重绑；旧客端成为房主后也不能重新登记旧复制体；刚创建的单机与新房主实体仍能正常执行。Unity 替身模拟已销毁对象等于 null 和销毁 GameObject 连带销毁组件，销毁是固定执行步骤。网络角色、房间和时间由测试控制，不能据此宣称原生断线与弱网实测。

这证明真实 Harmony 对生产保护和 setter 链的绑定与 L2 逻辑；未启动 Unity、读取玩家存档或实测工坊传输，不证明真实帧时序、网络命中特效及捏脸外观。人工 L3 按联机验收报告检查。
