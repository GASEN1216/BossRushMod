# 甩锅许可证

2026-10-13 核对，COMPAT 装备与现有商店内容扩展。`BossRushItemIds.PassTheBomb` 为 500109，品质 5、重量 0.3、基础价值 18000；占图腾槽，无耐久，不可堆叠，不进入随机奖池，不新增存档字段。

## 玩法

主玩家实际佩戴图腾，在出击场景直接伤害敌人后挂一颗 5 秒炸弹。之后由当前携带者的直接命中决定转移对象；敌人打中玩家会回传，玩家再命中其他角色可以继续转移。转移不刷新引信，同时最多存在一颗，挂弹开始 12 秒冷却。持续伤害、反伤、装备效果和炸弹自身伤害不挂弹、不传递。携带者死亡后原地留弹；卸下、主角死亡、切图取消当前炸弹，穿脱不能刷新已经开始的冷却。

爆炸基础物理效果伤害为玩家 40、其他角色 160，范围 3 米，周围目标受遮挡检查。友方只有正在携带炸弹的角色自身会受伤，附近其他友方不受波及。实际防御和归因按生产运行时及官方伤害结算执行。

## 公共接线

- [PassTheBombConfig.cs](file://Integration/NewWeapons/PassTheBomb/PassTheBombConfig.cs) 集中数值、图标路径和中英说明；[PassTheBombWeaponConfig.cs](file://Integration/NewWeapons/PassTheBomb/PassTheBombWeaponConfig.cs) 设置物品属性与图腾标签，并注入本地化。
- [NewWeaponItemConfigurators.cs](file://Integration/NewWeapons/Common/NewWeaponItemConfigurators.cs) 与 [EquipmentConfiguratorBootstrap.cs](file://Integration/EquipmentConfiguratorBootstrap.cs) 分别接物品、装备配置器；[BossRushDynamicItemRegistry.cs](file://Integration/BossRushDynamicItemRegistry.cs) 配置无专属 bundle 的动态注册计划。
- [NewWeaponPlaceholderRegistry.cs](file://Integration/NewWeapons/Common/NewWeaponPlaceholderRegistry.cs) 克隆已注册图腾并立即配置，支持重启后的按需注册，防止物品退化为 FallbackItem。
- [NewWeaponEquipState.cs](file://Integration/NewWeapons/Common/NewWeaponEquipState.cs) 追踪真实图腾槽；[NewWeaponRuntime.cs](file://Integration/NewWeapons/Common/NewWeaponRuntime.cs) 分发初始化、过图和销毁。
- [PassTheBombRuntime.cs](file://Integration/NewWeapons/PassTheBomb/PassTheBombRuntime.cs) 拥有炸弹状态、命中处理与取消；[PassTheBombRules.cs](file://Integration/NewWeapons/PassTheBomb/PassTheBombRules.cs) 处理倒计时、转移和冷却；[PassTheBombFx.cs](file://Integration/NewWeapons/PassTheBomb/PassTheBombFx.cs) 拥有头顶表现与清理。

## 图标与获取路径

图标为 `Assets/Icons/pass_the_bomb.png`（512），头顶炸弹标记为 `Assets/Icons/pass_the_bomb_marker.png`（256）；由 [production_icon_manifest.json](file://tools/production_icon_manifest.json) 登记并打入现有 `Assets/ui/production_icons` 包。运行时借用生产图标缓存或由图标组件维护独立副本；不需要新的装备 bundle。

`Assets/Data/DuckNpcs.json` 的 `sky_fuzhou.permanent.shop.items` 同时保留点名册 500106 与甩锅许可证 500109：好感 3 级、各库存 1、价格因子 1，不隐含折扣或概率缺货。许可证售价 18000。普通构建中的 [SkyIslandResidents.cs](file://SkyIsland/SkyIslandResidents.cs) 生成登云码头 `POI_A` 的浮舟，其 [PermanentDuckNpcInteractable.cs](file://Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcInteractable.cs) 按同一配置装配通用商店。婚后 [WeddingModBehaviourBridge.cs](file://Integration/Wedding/WeddingModBehaviourBridge.cs) 通过 `ForceSpawnAtAsync` 生成同身份、同组件的基地配偶。浮舟缺席时，[SkyIslandWorldStoryServices.cs](file://SkyIsland/SkyIslandWorldStoryServices.cs) 把同一个 `sky_fuzhou` 商店挂在登云码头 `Search_A` 渡口工台，沿用好感、货币与商品。

## 验证边界

生产 NPC 蓝图的解析、两件商品共存、独立 TypeID 与副本隔离由 [PermanentDuckNpcDialogue 执行回归](file://tests/fixtures/PermanentDuckNpcDialogue/README.md) 覆盖。工厂顺序、黑名单数据与编译清单由相应结构守卫检查；甩锅运行时回归见 [PassTheBomb 夹具](file://tests/fixtures/PassTheBomb/README.md)。以上均为 L1 / L2，不能证明头顶画面、实际护甲伤害和商店窗口的游戏内效果。

[GameplayCoverage.json](file://Assets/Data/GameplayCoverage.json) 的 `EQ_PASS_THE_BOMB` 将本模块和 Wiki 条目映射到现有 `ITEM_FACTORY_ALL` 自动用例；运行时注册表会生成 `ITEM_FACTORY_500109`，检查实际工厂实例的身份、名字和图标。登记本身不等于运行。`M_EQ_PASS_THE_BOMB_01` 至 `_05` 分别要求 owner 验收携带者接力与头顶画面、伤害与死者留弹、取消与菜单门控、三处商店和重启、首次挂弹及持续使用的性能，均保留待测状态。截图、录像和性能文字记录使用相同步骤 ID 命名。
