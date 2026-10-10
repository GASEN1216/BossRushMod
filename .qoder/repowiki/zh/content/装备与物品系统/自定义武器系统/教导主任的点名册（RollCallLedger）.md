# 教导主任的点名册

2026-10-09，COMPAT 装备扩展；获取依赖永久捏脸 NPC 的可选商店 SCHEMA+。TypeID 为 `BossRushItemIds.RollCallLedger`（500106），品质 5、重量 0.3、价值 20000，无耐久，装备在图腾槽。天空岛登云码头的「浮舟的航前杂货」好感 3 级出售，库存 1；不进随机奖池。

## 玩法与伤害

从第一次有效直伤开始，5 秒内命中三名不同的敌对角色，依次短暂显示数字；第三名触发后先清名单、进入 6 秒冷却，再在下一帧向仍存活且仍敌对的目标追加物理效果伤害。每人伤害为 `20 + 该目标首次记名一击的 finalDamage * 0.25`，继续接受官方防御结算。重复命中不会刷新窗口或改写首击记录；击杀也算记名，但死者不承受追加伤害。

效果伤害、持续伤害、友军、中立者和遗种崽不记名。各目标独立保存首击，避免把第三发大伤害复制到其余目标。追加伤害标记 `isFromBuffOrEffect = true`、`fromWeaponItemID = 0`，不能再次触发点名或冒充 Mode G 玩家直伤。

## 注册与生命周期

- [RollCallLedgerConfig.cs](file://Integration/NewWeapons/RollCallLedger/RollCallLedgerConfig.cs) 集中玩法参数与中英文说明；[RollCallLedgerWeaponConfig.cs](file://Integration/NewWeapons/RollCallLedger/RollCallLedgerWeaponConfig.cs) 复用图腾工厂并设置物品属性。
- [NewWeaponItemConfigurators.cs](file://Integration/NewWeapons/Common/NewWeaponItemConfigurators.cs) 登记物品配置器；[EquipmentConfiguratorBootstrap.cs](file://Integration/EquipmentConfiguratorBootstrap.cs) 登记装备配置器。
- [NewWeaponPlaceholderRegistry.cs](file://Integration/NewWeapons/Common/NewWeaponPlaceholderRegistry.cs) 用现有图腾克隆创建物品；[BossRushDynamicItemRegistry.cs](file://Integration/BossRushDynamicItemRegistry.cs) 提供重启读档时的按需注册。
- [NewWeaponRuntime.cs](file://Integration/NewWeapons/Common/NewWeaponRuntime.cs) 拥有初始化、场景重绑和销毁入口；[NewWeaponEquipState.cs](file://Integration/NewWeapons/Common/NewWeaponEquipState.cs) 缓存实际佩戴。
- [RollCallLedgerRuntime.cs](file://Integration/NewWeapons/RollCallLedger/RollCallLedgerRuntime.cs) 只在实际佩戴且非基地时订阅伤害事件，只有名单或待结算存在时创建逐帧组件。单纯冷却不需要 Update。
- [RollCallLedgerRules.cs](file://Integration/NewWeapons/RollCallLedger/RollCallLedgerRules.cs) 独立处理去重、时间窗、首击快照和冷却；[RollCallLedgerFx.cs](file://Integration/NewWeapons/RollCallLedger/RollCallLedgerFx.cs) 用官方短气泡和共享爆点表现。

卸下清除名单与待结算，但保留冷却；死亡、场景加载开始与主角更换清全部状态。场景补配还可能晚于官方初始化事件，必须走 `SetupForScene()` 清理并重新绑定已佩戴者。结算过程中用代数和 owner 复核防止其他伤害回调触发换图后继续打旧目标。

## 图标和获取

正式图标键为 `Assets/Items/roll_call_ledger_icon.png`，经 `production_icons` 包加载。[RollCallLedgerIcon.cs](file://Integration/NewWeapons/RollCallLedger/RollCallLedgerIcon.cs) 让动态 prefab 拥有 GPU 复制的纹理和 Sprite，避免卸包后失效；克隆的物品组件不能释放原 owner 的资源。

浮舟的货物来自 `Assets/Data/DuckNpcs.json` 中 `sky_fuzhou` 的可选 `permanent.shop`；岛上本人位于登云码头的 `POI_A`。商店名为「浮舟的航前杂货 / Fuzhou's Departure Supplies」，按原价 20000 出售，婚后仍沿用原身份和商品配置。解析、通用互动与婚后访问见 [捏脸 NPC 工具链](file://.qoder/repowiki/zh/content/NPC 关系系统/捏脸 NPC 工具链.md)。旧蓝图缺少 shop 时继续没有商店。

## 验证边界

[RollCallLedger 执行回归](file://tests/fixtures/RollCallLedger/README.md) 链接生产逻辑，覆盖直接伤害归因、窗口、去重、冷却、致死命中、暂停、卸下、主角重建、场景取消与伤害回调重入。装备注册与黑名单结构由既有守卫覆盖。Windows 编译和离线回归不证明实机观感；数字位置、图标显示、实际防御结算与商店交互仍需游戏内确认。

## 2026-10-10 渡口购买入口

浮舟本人和登云码头 `Search_A` 工台共用 `sky_fuzhou` 的 `permanent.shop`：好感 3 级、库存 1、售价 20000。`SkyIslandWorldStoryServices.AttachDockShop` 将通用 `NPCShopInteractable` 加进工台原交互组，未解锁时监听好感并隐藏，直接调用官方商店，不增加剧情面板选项。`SkyIslandWorldStory.Dispose` 先关闭工台持有的商店、从交互组移除子项并销毁；浮舟本人回收由 `DuckNpcSpawner.Despawn` 关闭其商店。婚后或居民生成失败时工台仍可购买。
