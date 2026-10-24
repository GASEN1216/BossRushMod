通过 `python tools/run_runtime_regressions.py --filter ZombieModeWaveRuntime` 运行。

夹具按原文链接生产死亡事件登记与 `HandleZombieModeHealthDead`、波次 Tick / Start / Complete / Settlement、信标 CanUse / TryUse / 引导协程与 operation owner 检查、`IsZombieModeRunValid`、真实枚举和 `ZombieModePhaseGuards`。结算超时常量也从生产 `ZombieModeTuning` 提取。输出目录记录这些来源的 SHA-256；状态推进、幂等、异常隔离与陈旧回调由生产方法执行。

用例覆盖：

- 圈内外普通波满目标与 Boss 已清空由 Tick 补结算；未满目标保持战斗；结算前安全区调用异常不挡推进。
- 三类准备阶段圈内外倒计时都能开波；战斗、结算、选奖励和离场不能直接再次开波；精英与横幅异常不丢 Boss 初始目标。
- 死亡副作用分别抛错后仍计数、结算并完成官方对象禁用；重复死亡与死效重入不重复计数、星点或奖励。
- 波末清理与补给异常；结算启动 throw / null、等待星点时防重复启动、协程中途异常释放 owner 后重试；重试不重复补给与 Boss 污染。
- 战斗中便携安全区不解锁信标；准备阶段信标圈内外可用；三秒读条、暂停顺延、尾帧切换 Combat / 撤离 / flag / wave / run / scene / 清理时拒绝开波；同 run、同 wave 的旧 generation 不取消新读条；启动 null 回滚与 Dispose 释放自有读条。
- Destroy 根对象后组件假 null，事件解绑与模式清理后不能继续结算。

宿主替身只提供角色／Health 事件、Unity 假 null 与连带销毁、可控时钟及逐帧协程调度，星点、掉落、死效、NPC 清理、准备对象清理、刷怪和奖励 UI 为可计数、可注入异常的边界。安全区是否包含玩家由替身的距离判断提供，实际驱逐与碰撞不在夹具内。Health 替身遵守官方先设死亡、发 OnDead、禁用对象、再发 OnHurt 的顺序，用于检查 Mod 异常是否逃出宿主。协程替身在启动时同步推进至首个 yield，后续帧异常会终止并 Dispose；它不模拟全部 Unity 调度语义。地图几何、实际伤害、UI 渲染、官方协程调度、PhysX 与性能需要游戏内验证。
