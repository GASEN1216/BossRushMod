# 沙暴冲锋与棍卫预警计时回归

入口：`python tools/run_runtime_regressions.py --filter SandstormCombatTiming`。

原样提取生产控制器 Update、TickFight、RunFight、Dashes、PhaseDashStep、AimDash、PhaseTo、WaitForFightSeconds 与扫掠距离方法，链接完整的 SandstormChampionConfig、SandstormChampionMinionArts 和 AstralStaffFxShapes。生产招式调度表另由 SandstormCombatRuntime / SandstormChampionPattern 覆盖，本夹具将 Fight 入口限定为待检查的冲锋。

覆盖普通暂停与拍照暂停/恢复、120/60/30/20/10 FPS、交替长帧、子步地面检查、断崖与高度突变、穿墙、翻滚免疫、暂停期间死亡清理及一二阶段原移动通道。棍卫的 Sweep/Spin/Starfall 从真实前摇入口生成真实光带，验证慢动作和拍照时钟、网格布局、结束和取消释放，同时验证玩家默认光带仍按未缩放时间播放。

宿主替身：可控时钟、简化水平转向、平地/断崖探针、官方位移、伤害次数、Unity 对象/组件连带销毁与渲染数据容器。粒子、相机、音效、符轮和命中特效由空适配器承担；不证明真实 ECM2、GPU、画面或性能。新增用例先对修复前生产代码运行确认失败，再验证修复。L3 仍需游戏内暂停、拍照、低帧率与实地地形验收。

多人棍卫扇形伤害同时核对真实 `Strike` 向网络击退边界传出的目标身体、原强度、0.14 秒时长、翻滚门和独立事务 ID；墙壁与翻滚阻挡时不发击退。此处击退服务为记录参数的替身，实际服务的身份校验、收据及交付由 `DragonKingCoopWorld` 执行，不能把本夹具的转发断言当作联网交付通过。
