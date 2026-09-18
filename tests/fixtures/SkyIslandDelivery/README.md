# SkyIslandDelivery

运行：`python tools/run_runtime_regressions.py --filter SkyIslandDelivery`。

逐字抽取纪念品 `SkyIslandItems.TryGive`、蛙卵 `SkyIslandGnats.TakeSpawn` / `SkyIslandFieldcraft.ConsumeOne`、头目补发 `SkyIslandBossLoot.TryAddFresh`；完整链接生产 `SkyIslandInventoryTransaction`、`InteractableLootboxInventoryHelper` 和 TypeID 表，避免替身重新实现事务判据。

覆盖纪念品台账拒绝、精确缓冲回执、交付失败回滚，以及蛙卵整堆/部分扣料后的通知异常、失败重试、重复取用、回调重入、返航/销毁时归还原件；头目补发在满箱、挂载后通知抛错、入箱拒绝与扩容失败时的所有权。共用事务另有未提交归还、幂等 Dispose 和成品合堆检查。修复前两项新回归分别在蛙卵丢材料与满箱丢装备断言上转红。

库存替身按官方 `Inventory.AddAt` / `RemoveAt` / `Item.StackCount` 的「先变更、后通知」时序及容量建模；Unity 对象按已销毁等于 null、销毁对象连带组件模拟。剧情可写门、昼夜、场景存活、物品实例化和投递后端是替身，不证明实际背包 UI、物理掉落、存档 IO、进程中断或帧时间。这是 L2，仍需 owner 实机验证正常取卵/放生与专属掉落。
