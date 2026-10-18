# ZombieCoopKillRewards

真实链接 `ZombieModeCoopKillRewards.cs`、`CoopPersonalDelivery.cs` 及其 JSON 实现；从当前生产源码提取完整死亡结算、死亡奖励与 DoomPulse 布局方法。验证房主确认的 guest 击杀、个人回血重发及去重、资产与波次过期、共享汲取星、死房主下的 guest DoomPulse、效果伤害防递归和致死吸血。

Unity 身体、网络投递、物理爆炸与清理宿主为受控替身，不启动游戏，不证明实际房间表现或网络延迟下的手感。入口为 `tools/run_runtime_regressions.py --filter ZombieCoopKillRewards`。
