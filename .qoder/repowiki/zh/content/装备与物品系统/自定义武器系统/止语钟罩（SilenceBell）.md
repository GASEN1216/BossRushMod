# 止语钟罩（SilenceBell）

2026-11-24，COMPAT。TypeID 500220，玩家许愿第 10 项「自充能护盾」。不新增存档字段或配置键。

## 玩家路径

无声钟守（`sky_bellkeeper`）好感 5 级 → 永久 NPC 交互组里与「钟守的旧物匣」并排的第二只匣子「钟守的铜钟罩」→ `SilenceBellShopInteractable` → 共享 `BossRushConfirmDialog` → 30 枚背包贝壳。兑换与节拍器共用同一条事务 `ReloadMetronomeTrade.TryExchangeForShells`（预留贝壳 → 背包交付 → 提交，失败释放预留），门槛共用 `ReloadMetronomeTrade.CanTradeAt`。钟庭 `Search_H` 装置挂同一组件（`BellKeeperWardOption`），钟守缺席或婚后离岛仍可兑换；婚后基地由永久 NPC 重建。`DuckNpcs.json` 的钟守蓝图登记 `500220` 5 级、库存 1。不进叮当商店、随机奖池，登记掉落黑名单。

## 生产调用链

- [数值](file://Integration/NewWeapons/SilenceBell/SilenceBellConfig.cs)：护罩 80；最后一次受击 5 秒后每秒回 20；打碎后至少 9 秒；单帧回充最多按 0.25 秒算。品质 5、重量 0.4、基础价值 18000。
- [护罩状态机](file://Integration/NewWeapons/SilenceBell/SilenceBellRules.cs)：不依赖 Unity。每次命中先把回充时刻顺延到「此刻 + 5 秒」（取晚），碎着挨打也顺延；打穿时标记碎裂并再顺延到「此刻 + 9 秒」。回充第一帧清除碎裂标记，回满那一帧只报告一次。
- [运行时](file://Integration/NewWeapons/SilenceBell/SilenceBellRuntime.cs)：唯一 owner。`ModifyFinalDamage` 首行在未佩戴时返回；跳过 `isFromBuffOrEffect` 与无来源伤害，只改主角 `Health`，敌我按 `Team.IsEnemy` 判定并豁免遗种巢随从；吃 `info.finalDamage` 后写回剩余伤害。
- 伤害入口：[ZombieModeDamageRuntime.ReduceFinalDamage](file://ZombieMode/ZombieModeDamageRuntime.cs) 由 `Health.Hurt` 转译器在扣血前调用，顺序为残日盾片 → 止语钟罩 → 丧尸模式门，所以对全模式生效、吃的是护甲与抗性结算后的伤害。
- [表现](file://Integration/NewWeapons/SilenceBell/SilenceBellFx.cs)：世界覆盖层画布上的头顶铜条（墨底、铜金填充、受击闪白、碎裂两片分开、回充流光、回满 1.2 秒后淡出），位置取 `HelmatSocket` 高度压在官方血条上沿；金钟罩（owner 2026-11-25 要求戴着就常驻）：驱动建好即 `Follow(主角)`，`LateUpdate` 先在 HUD 显隐门之前 `UpdateBell`（世界特效不随 HUD 隐藏，暂停时 dt=0 定格），再走头顶条的双门。钟用 LineRenderer 四环十肋（钟口最亮、肋往上渐淡、8°/秒自转、2.8 秒呼吸），外加地面宽柔光环、`RuneOuter` 水平钟纹（交叉淡入的突发发射器）和沿钟壁上飘的 `GlowDot` 金芒（手动 Emit，≤14 颗）。钟身高度 = 护罩余量（打碎归零，按 1/250 量化后才重算竖肋）；回充 / 戴上时顶边一道亮白金是正在铸的一圈，满罩时每 3.6 秒一道流光上行；受击整口钟一亮一胀 0.25 秒，打碎按打碎前高度整口崩开转朱 0.4 秒、之后钟口剩一圈暗红余烬；碎裂金屑与墨烟、回满脚下金环不变。只用 `BossRushFxKit` 共享材质与 `BossRushInkSkin` 钟声（嗡鸣节流 0.35 秒），不新建材质或着色器。`LateUpdate` 首行过官方 HUD 显隐与暂停双门，登记 `PersistentHudVisibilityGuard`。
- [物品配置](file://Integration/NewWeapons/SilenceBell/SilenceBellWeaponConfig.cs)：`NewWeaponConfiguratorCore.ConfigureTotem`，图腾三标签由核心统一加；中英名称与描述注入集成本地化。
- [兑换](file://Integration/NewWeapons/SilenceBell/SilenceBellShop.cs)：确认框只写钟守铜片上的一句话和「只收背包里的贝壳」，不讲机制。

## 生命周期与接线

`NewWeaponRuntime` 统一订阅、换场景、配置已加载物品、退订与销毁清理。`NewWeaponEquipState.TrackedTotemTypeIds` 登记 500220；驱动 `SilenceBellDriver` 只在出击场景实际佩戴时存在。卸下只撤驱动、护罩数值留着（不能靠穿脱补满）；死亡、换场景与宿主销毁才 `ResetFull`。

接线：`BossRushItemIds.SilenceBell`、ItemFactory 与装备配置器、`BossRushDynamicItemRegistry`（克隆兜底走 `NewWeaponPlaceholderRegistry`）、本地化、掉落黑名单数据与 fallback、编译清单、TypeID 台账三处。图标暂借 `collectible_sky_bronze_windbell`，正式图标 `silence_bell.png` 由生图师按 `docs/guides/出征令与许愿七项_生图说明.md` 末节绘制，交付后换声明、登记 `production_icon_manifest`、重打图标包并补 Wiki 页图标。

## 验证边界

守卫 `tests/SilenceBellWiringGuard.py`；执行回归 `python tools/run_runtime_regressions.py --filter SilenceBellRules`（生产状态机直接编译）。`ResidualSunShield` 与 `IntegrationThirdReviewFixes` 夹具编译真实伤害入口，带钟罩替身，前者核对钟罩收到的是盾片之后的伤害。这些属于 L1/L2；铜条位置、虚影观感、钟声与性能需要实机，人工清单见 `Assets/Data/GameplayCoverage.json` 的 `EQ_SILENCE_BELL`。
