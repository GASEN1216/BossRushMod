# 第三轮 Integration 缺陷执行回归

2026-10-06 携崽扩展：沿用原 Harmony 注入后的官方 Hurt 方法体，验证已登记宠物来源的攻击会被丧尸 Boss 护盾抵消且不会提前致死；未登记 NPC 来源保持原分支。宠物身份是显式设置的边界替身，实际 AI 激活由 PetNestCompanionActivation 单独覆盖。

对应 CR-2026-09-06-012 / 013 / 014 / 016，分类 `COMPAT`。

运行：`python tools/run_runtime_regressions.py --filter IntegrationThirdReviewFixes`。
所有编译、依赖副本、哈希与 IL 证据写入 `Build/integration-third-review-fixture/`，不启动游戏、不访问玩家存档、不部署。

需要 Windows、.NET SDK、本机游戏的 Harmony 和 Managed 程序集；可通过
`BOSSRUSH_HARMONY_DLL`、`BOSSRUSH_GAME_MANAGED` 覆盖定位。

执行范围：

- 完整生产 `DuckNpcMovement` / `DuckNpcRuntimeMarker` 加完整官方 `AI_PathControl`。
  替身 Seeker 故意在取消后仍投递成功回调，覆盖暂停、取消异常、恢复后旧请求、停用、
  移动中 0.6 秒重规划、慢请求避免饥饿、寻路超时、近距离停步、远距离追赶、驻留与对话独立所有权。
- 直接提取官方 `Health.Hurt` 完整方法体，保持算式、分支、事件顺序不变，
  用真实 Harmony 安装完整生产观察补丁，生产 helper 通过真实 `OnHurt` 回调读取贡献。
  与未打补丁的官方方法分别执行每种元素所得结果对照，验证混合抗性、免疫、最低 1 点、
  最终生命限额、暴击、穿甲、忽略护甲、真实伤害、难度、丧尸倍率、本击耐久破损、
  先加 Buff 再取护甲、同目标嵌套、嵌套异常、卸装与运行时 owner 变化。
- 另在本机真实 `TeamSoda.Duckov.Core.dll` 上读取原始 IL，运行完整生产定位与 transpiler，
  确认只插入一处观察调用；保存变换前后 IL 与游戏程序集 SHA-256。
- 负向检查：累加从加法改为减法、移除最低伤害赋值均须拒绝匹配；缺少贡献记录必须返回 0 并明确诊断。

边界：Unity、A* 调度、物品事件及生命对象外围依赖使用内存替身；不代表游戏内验证。
直接在 .NET Framework 隔离进程 detour 原游戏程序集会受其 `IValueTaskSource` 运行时依赖限制；
.NET 8 又与该安装的旧 Harmony 反射 API 不兼容。因此真实游戏程序集一层如实仅验证 IL，
执行行为由完整官方方法体与真实 Harmony 的兼容宿主完成。仍需实机验证聊天/驻留/跟随体感、
游戏加载时补丁绑定日志与元素回血显示。

2026-09-26 宿主载体同步：宿主 `HasSetBonusElementHealing` getter 从当前 `IntegrationHostCompatibility.cs` 提取，套装模块与真实 Harmony 观察逻辑保持原生产源。


2026-09-26 丧尸 Boss 链路：新增完整生产 `ZombieModeDamageRuntime`，逐字提取 Boss 护盾、减伤组合、精英防御、共享计时组件与冲刺组件；参数常量从生产 Tuning 抽取。真实 Harmony 驱动原版 Hurt 全文验证致死吸收、过量伤害、盾量单次消费、过期、光环组合、精英刚硬/反制、旧局与非玩家来源边界；本机真实 DLL 验证减伤与元素观察两种注入顺序。冲刺回归验证预警、暂停、恢复、实际落点、死亡与换局取消、掉帧后的单次伤害；运动/碰撞仍是观测替身，不能代替实机物理测试。Unity 替身补充销毁对象等于 null 及连带组件销毁语义。

2026-09-27 合并迁移：伤害调用完整穿过生产 `ZombieModeCombatHostBridge` 到 `ZombieModeRuntimeModule_BossController` 与 `ZombieModeRuntimeModule_PollutionTuning` 的真实成员；模块在每个目标场景建立时显式绑定，场景列表、时钟和武器元数据是边界替身。共享护盾和冲刺组件继续从生产独立类型提取，哈希记录覆盖薄桥、owner、组件及调参源码。删除真实宿主吸收转发后，致死前护盾消费断言必须失败。

2026-10-07 大额押注宿主契约：现装官方 DLL 的只读 IL 检查增加 `EconomyManager.Pay(Cost)` 和 `IsEnough`。前者即使收到 `cashAvailable:false`，首次预检仍硬编码现金可用；后者用未检查溢出的加法合计账户与现金。检查实际调用前的三条入栈指令，以及账户／现金 getter 后的普通 `add`、无 `add.ovf`。IL 留在 `installed-game-economy-il.txt`，未调用经济 API、未访问玩家数据。
