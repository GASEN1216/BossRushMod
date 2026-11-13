# 末班编组站封签货事务回归

在仓库根目录运行 `python tools/run_runtime_regressions.py --filter LastShiftCargoRuntime`。

夹具链接生产 `LastShiftLoot`、`LastShiftTimetable`、`SkyIslandRewardCrate` 与 `SkyIslandLootTables`。从班次推进与签收机关进入生产路径，验证三个班单的普通台、加价台，实际库存件数、两格精品、杂货品质带、交互开放时序及每趟只签一次。

侧柜用例从同一套官方箱子机关进入，使用预留接口替身，验证开柜前隐藏、激活失败时钢印回到原位、重试提交和本趟新捡钢印无法开柜。丢下入场钢印再拾取一枚新钢印不会替换其开柜资格，找回原件后可以开柜。`TidegateStoryTransactions` 链接真实 `TidegateInventory` 验证物品预留。

替身提供 Unity 场景对象、停用父节点、层级销毁、官方箱子和本地库存 API、物品预制件与素材目录。替身能在物品实例化、入箱、入箱后的回调、激活、签收中的场景关闭处注入故障，也能令某个现货品质带为空。库存和场景替身用于验证事务与所有权，实际 Unity 的交互、预制件和官方现货内容需进游戏验收。
