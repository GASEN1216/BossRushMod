# 沙暴战斗调度与可击破弹幕回归

入口：`python tools/run_runtime_regressions.py --filter SandstormCombatRuntime`。

动态抽取生产控制器的 TickFight、RunFight、Fight、WaitForFightSeconds 原样执行，链接完整招式模式。移动、换位和特效动作由计数及定时替身承担。逐帧取消宿主协程，验证末阶段至少连续执行 12 组一/二/三冲，嵌套等待不会被吞掉，死亡终止后续出手。

额外抽取完整 OrbitAndOrbs：在移动目标、受阻轨道替身下，以 120 / 30 FPS、0.7 秒和 5 秒长帧运行，验证普通发射和末尾补发共 31 泡，每颗都朝实际玩家方向。抽取 SampleHeightDensity，使用 Unity SmoothStep 的 from/to/t 数学语义检查主体浓度与上下边界；不调用渲染器，不把密度数值合格等同画面合格。

弹幕执行生产 Spawn、Init 中完整接收体装配段及 OnShotDown；链接只读官方 DamageReceiver / HealthSimpleBase 源码。反编译 HealthSimpleBase 副本仅增加 Random 类型别名，不修改方法体或官方源文件。Unity 替身模拟失活装配、首次激活 Awake、事件和死亡停用，检查非触发碰撞体、敌对阵营、生命值以及 Hurt → Dead → 击破回调。渲染与 Pop 粒子收尾由计数替身承担。

边界：不模拟真实 Unity Physics 武器查询、ECM2 地形、全部第三方 Harmony 或真实图像。此回归证明调度与受伤接线可执行，不能证明末阶段实机停顿已消失、枪弹必定命中或视觉达标。手工验收为 M_CAMPAIGN_SANDSTORM_01。
