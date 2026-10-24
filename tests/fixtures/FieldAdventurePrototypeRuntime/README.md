# 试用品射向与后坐执行回归

入口：`python tools/run_runtime_regressions.py --filter FieldAdventurePrototypeRuntime`。
反向验证：`python tests/fixtures/FieldAdventurePrototypeRuntime/negative_probes.py`。

夹具按原方法体抽取真实 `DangerousPrototypeRuntimeModule.IsHeld / OnShot / ResolveShotDirection / TraceBlast / TickRecoil`，直接链接 `DangerousPrototypeRules.cs`。官方 `ItemAgent_Gun.TransToFire` 从 `muzzle.forward` 起到 `overrideShootPoint` 前的方向分支也按原样抽取执行，作为近距、水平距离与 `CharacterMoveability` 合同的比对对象。构建由中央 runner 的 `run_project_fixture` 执行，每次使用新目录，显式构建后只执行本次 `TargetPath`；报告记录实际二进制与生产、官方源码 SHA-256。

覆盖脚边、角色与枪口之间、枪口处、枪口前 0.1 米阈值两侧、远距、角色身后、上下高差、移动能力 0 / 0.5 / 0.5001 / 1、角色平移和枪口旋转。实际 `OnShot` 进入 `TraceBlast`，断言辅助攻击与官方基础方向一致、巨炮水平后坐与射向相反、非巨炮无后坐、冷却期间无重复射线。暂停、离手、Holder 失效、场景门关闭和销毁角色均走实际门控；后坐阻挡、缺地面和游泳会停止强制速度。

贴墙验证用可复算的无限平面交点替身，覆盖直射、反向、切向、高差、斜墙和距胸口极近的阻挡。生产起点必须在检查段内且处于角色一侧；真实 `TraceBlast` 不能伤到墙后目标，反射段回到近侧。这个夹具没有使用 PhysX，不能证明实体厚度、初始重叠、曲面、复杂网格或角色贴墙时的实际接触结果。Unity 向量和物理边界、角色/伤害宿主、场景门及通知是替身；视觉和收尾 `EndLease` 没有链接。Unity 已销毁对象判空及 `Destroy(GameObject)` 连带销毁组件有模拟并执行。

六项反向验证分别恢复旧瞄点减枪口、移除移动门、把水平距离改回三维距离、旁路方向助手、恢复旧墙面退让和反转后坐符号。每项只修改独立副本，必须在指定行为断言转红，随后按原字节还原、重新运行绿色基线，并核对共享生产源码哈希未变。

本夹具提供 L2 执行证据，正式编译另行执行。L3 仍需 owner 在游戏内借用两种试用品，依次瞄脚边、枪口前后、远处与高低差目标，并贴薄墙/斜墙射击；附加攻击应朝官方基础射向，巨炮只向相反水平后退，墙后目标不应被辅助攻击穿墙命中。枪械私有 `OverrideShootPoint`、散射、每颗子弹角度和第三方射击事件修改不在本次官方基础方向合同内。
