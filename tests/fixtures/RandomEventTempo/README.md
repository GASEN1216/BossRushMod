# 随机战场事件执行回归

直接编译生产 `RandomEventTempo`、事件模型、数值与 `RuntimeStatModifierTracker`。验证三个事件真正挂上两项属性、敌我效果一致、新敌人按节流补挂、重复 Tick 不叠层、各结束原因和重复清理恢复全部属性、缺失必要属性触发失败后 Scope 仍回滚。

`CharacterMainControl`、角色属性容器、场内敌人查询和 `RuntimeScope` 是明确的宿主替身；不验证 Unity 音画、实际 AI、自然等待的随机序列或游戏帧率。通过聚合入口 `python tools/run_runtime_regressions.py --filter RandomEventTempo` 运行，输出只写 `Build/random-event-tempo/`。
