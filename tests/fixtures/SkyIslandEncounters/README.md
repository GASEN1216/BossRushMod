# 天空岛遭遇 owner 执行回归

运行 `python tools/run_runtime_regressions.py --filter SkyIslandEncounters`。

直接链接生产 `SkyIslandEncounters.cs` 全文件与真实内容 registry / JSON。Unity 对象销毁、Health 事件、
异步角色工厂、物理地面和 UI 日志使用替身；不访问游戏存档或真正生成 Unity 角色。

2026-10-06 独立深审：序章额外原样执行 `ShouldRunObjective` 和 `IsObjectiveGeneration`，取消此前始终返回 true 的验收替身。未接任务、已持航向仪、已开航线均由生产门拒绝；角色工厂挂起后撤销接取、开航线、拿到仪器、换图、变更代数或关闭 owner，生产 SpawnBoss 必须回收迟到角色。这些是玩家入口的生产判据和异步边界证据，序章 Tick 到该判据的接线另由源码审查与守卫核对。

覆盖敌人先死后销毁仍能清场、提交失败重试、不重复生成已死亡槽、缺 Health 的异常角色回收、
离场期间在途 preset 保留、迟到角色回收、角色清理后延迟释放 preset、实际 JSON 接线和共享剧情脸。
官方掉落仅验证生产配置启用其默认路径，不声称替身验证了官方 LootBox / 经验 / 魂的游戏行为。

气泡调度器和话语表也直接链接生产文件；官方气泡 UI、暂停及剧情对话状态是显式替身。
检验冷却、距离、暂停、owner 失效和显示失败不消费预算，不声称已验证官方气泡渲染。
头目配装/招式由 Forge 替身承接；`SkyIslandBossVoice.cs` 直接链接生产全文件，Forge 将真实组件挂到替身角色。

`ChatterBehaviorRegression.cs` 从真实遭遇 Tick / Health 事件 / BossVoice Update 驱动，覆盖当前目标与近期动静、
交战禁止闲话、脱战恢复、忙碌/距离/展示失败后的事件重试、目标变更丢弃、多人争用与死亡优先级、
事件过期及进度合并、事件与闲话各自冷却、当刻语言、补刷复位、血线重试及重复绑定/销毁退订。
真实导航、官方 AI 感知和 UI 像素未被替身验证；待播只承诺进入展示流程，不保证之后异步展示完成。

2026-09-18 补齐官方气泡合同：manager 缺席/停用/销毁、缺 prefab 的同步静默完成、
已失败/取消的异步结果、正常跨帧完成及清理后的迟到异常。替身的 Forget 不阻塞，
不再用同步抛错代替官方 UniTask 失败。仅证明请求记账与异常观察，不证明实际像素可见。

2026-09-26：直接链接 `SkyIslandCombatBalance` 与 `SkyIslandCombatPreset`，按 Wiki 固定快照逐字段核对，
覆盖全部头目 / 岛主 / 剧情对手 / 噬风 / 回响与同组随从，真实 owner 在官方工厂调用前写好属性；
验证普通敌人底模差异、独立近战伤害、负暴击修正、基准资源不被污染和重复准备不复利。
官方 CreateCharacterAsync 内部 Stat / 难度 / AI 装配仍是宿主边界，替身只记录调用时数据，待 Windows 编译与 L3 核实。

2026-09-26 序章补验：`run.py` 将正式 `SkyIslandPreludeFlow.SpawnBoss` 方法逐字写入 Build 下的编译输入；`PreludeSpawnRegression` 只存根场景 / 资源边界，工厂替身按调用当刻 `preset.health` 创建角色。验证序章守基础 375 HP、出场满血、伤害 / 移动 / 反应、source 不变、再次生成不复利，以及敌对、掉落开关与原 K3 配装入口。删除基准准备调用、或挪至 `await CreateCharacterAsync` 之后，执行结果都必须转红。该夹具没有运行 Unity 的真实物理、装备和尸体箱，不能替代实机。

序章 `SkyIslandPreludeFlow.SpawnBoss` 从生产文件逐字抽取执行；覆盖零号区断风游猎·守的生成前属性、满血、重复生成、敌对性与 preset owner。

2026-09-28：`TargetingPolicy` 经真实生成 owner 核对自动组传入自然选敌、手动剧情组保留强制追踪；
自动组仍禁用距离休眠、可以在死亡后完成清场。这里只记录 ApplyAi 的策略参数，实际方法及官方
感知结果、每帧目标和攻击目标的传递由 `SkyIslandCombatRuntime` 执行，不把替身记账当作真实 AI 实测。

2026-09-30 武器品质：`ArmoryRegression.cs` 链接生产 `SkyIslandEnemyArmoryRules`，核对普通档下限 3、头目档下限 5、巡守分档与口径白名单；从真实遭遇 owner 刷出全部遭遇组（含手动、噬风、回响、夜限定带队），逐位核对每名敌人按自己的档次配枪一次，正式序章 `SpawnBoss` 按头目档配枪。运行时 `SkyIslandEnemyArmory` 是替身，官方物品表、槽位、弹匣与 AI 拿枪待实机（`[SkyIslandArmory] ARMED` 日志）。删掉遭遇组的 `Arm` 调用后执行结果转红。
