# 附加资源 Scene 与飞行图腾生命周期回归

本夹具直接链接生产 `EquipmentEffectManager`、`EquipmentAbilityManager`、`EquipmentAbilityConfig`、`FlightTotemEffectManager`、`FlightAbilityManager`、`FlightConfig` 和 `SceneRuntimeGate`。

测试经真实静态装备事件穿/脱图腾，执行真实能力注册/注销与 `FlightAbilityManager` 的反射 Dash 缓存/恢复。覆盖天空岛、石堡、大小写规范化、资源往返后卸装、真实切图中的临时空槽保护、期间额外资源加载不提前解保护、真正关卡加载后恢复及同名外部 Scene 不误排除。

Unity GameObject/Component/Scene 事件、Item 槽位和角色字段为显式替身；飞行动作、物理与输入采样也为替身。反射字段调用本身使用 .NET 反射，未复制被测生产算法。它不验证真实 Scene 载入、飞行动作物理、游戏按键或其他 Mod 事件顺序。

运行：`python tools/run_runtime_regressions.py --filter EquipmentResourceScene`。

修复前直接执行旧生产装备回调：27 条断言、10 条失败；修复后预期全部通过。原失败证据为 `Build/equipment_resource_scene_before.log`。
