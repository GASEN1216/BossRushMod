# 贪吃宝箱怪生产执行回归

运行 `python tools/run_runtime_regressions.py --filter FieldAdventureMimicRuntime`。脚本每次在独立目录构建，并执行本次 MSBuild 返回的 TargetPath，记录程序集与输入 SHA-256。默认同时做隔离反向验证。

真实生产逻辑：`GreedyMimicEncounter.cs`、`GreedyMimicRules.cs`；只读链接官方反编译的 `HealthSimpleBase.cs`、`DamageReceiver.cs`。实例走完整 Encounter 构造、接收体激活、官方扣血事件、生产 Tick 及 Dispose，不复制暴击、打断或范围算法。

宿主替身：Unity 对象、物理阻挡、角色、背包、奖励、UI、视觉。Unity 对象模拟销毁假空值、GameObject 连带组件销毁以及激活时 Awake；Awake 前注入的健康/事件字段没有替身默认值。视觉仅记录舌头调用与完整落点，Bounds / Rigidbody 属性仅满足当前生产装配接口，不能证明动画包络、物理同步、实际舌尖位置、渲染或迷雾。真实 Unity 动画碰撞、官方爆炸单次扣血、舌头与分段绑定由 `FieldAdventureVisuals` 另验。

覆盖普通/强制暴击在 45 点阈值的上下界与精确边界、未发生暴击时忽略倍率、连续混合伤害、非主玩家/无归属的零倍率、零/负倍率、负伤害回血、官方拒收伤害、蓄力以外的伤害、受伤唤醒、9 米内锁定直线舌击（3 / 8.99 米命中、侧移 1.5 米躲开）、远距扑跳（12 米落点命中、30 米截到 16 米落空、无合法落点不出招）、真实飞行弹幕（两口弹药补第二波、每波一次、出膛后侧移可躲）及离开 45 米取消、清理和退订。

反向验证只改 `Build/`，每个 mutant 使用自己的隔离副本：恢复按原始 damageValue 累加时必须在“critical exact”的真实扣血归因断言转红；移除主玩家过滤时必须在来源过滤断言转红。每轮恢复副本原字节并校验、记录 SHA-256，生产文件始终不写入。可用 `BOSSRUSH_MIMIC_RUNTIME_EVIDENCE_DIR` 指定证据目录。该回归是 L2，不能证明游戏内碰撞、迷雾、操作或实际性能。
