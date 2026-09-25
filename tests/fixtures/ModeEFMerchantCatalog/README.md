# 共享商人目录执行回归

运行 `python tools/run_runtime_regressions.py --filter ModeEFMerchantCatalog`。

直接链接完整生产 `Utilities/ModeEFMerchantCatalog.cs`，覆盖商人预设的字典、场景扫描与图标回退，已销毁预设重选，分类/标签回退顺序，多标签去重，搜索品质边界，缓存返回副本，消费者之间保留原静态缓存语义，医疗品排除、静态清理与分帧预热等待次数。

替身提供 Unity 已销毁对象判空、标签、预设、官方物品搜索和场景预设数组；查询委托由用例显式装配。未模拟游戏资源加载、实际商品交付、UI 或真实帧耗。商人生成、交易与迟到请求仍由 `AuditModeLifecycle` 等现有夹具覆盖。
