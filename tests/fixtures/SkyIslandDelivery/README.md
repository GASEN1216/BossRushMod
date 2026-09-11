# SkyIslandDelivery

该夹具逐字抽取 `Integration/SkyIsland/SkyIslandItems.cs` 的 `TryGive`，在内存替身中执行纪念品交付边界：物品已归属但通知抛错、待领取缓冲已写入但后续抛错、交付失败回滚、回滚失败保留台账、台账拒绝和 prefab 缺失。它验证的是交付回执与回滚顺序；真实 Unity 的背包、仓库序列化和进程中断仍需实机 smoke。
