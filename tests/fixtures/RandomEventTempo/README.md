# 随机战场事件执行回归

直接编译生产 `RandomEventTempo`、事件模型、数值与 `RuntimeStatModifierTracker`。验证三个事件真正挂上两项属性、敌我效果一致、新敌人按节流补挂、重复 Tick 不叠层、各结束原因和重复清理恢复全部属性、缺失必要属性触发失败后 Scope 仍回滚。

`CharacterMainControl`、角色属性容器、场内敌人查询和 `RuntimeScope` 是明确的宿主替身；不验证 Unity 音画、实际 AI、自然等待的随机序列或游戏帧率。通过聚合入口 `python tools/run_runtime_regressions.py --filter RandomEventTempo` 运行，输出只写 `Build/random-event-tempo/`。

2026-10-13：另按 16 个参与者执行三种联网 Tempo 的生产事件代码，每次验证本机玩家得到原值属性、客端敌人不被重复加成、切图完整撤销。联机传输仍由 RandomEventsCoop 与 CoopProtocol 独立覆盖。

同日补充角色重生、同角色更换 CharacterItem、两项属性仅加载一项后再次补齐、Main 暂时消失后恢复的执行回归。验证旧记录撤销、半成品回滚、恢复后不叠层及结束时两套记录全部释放；房主和客端权限分支都执行生产逻辑。权限门面和角色切换仍是隔离替身，不证明实际联机重生生命周期或网络传输。
