# 重铸与赠礼资产事务回归

通过聚合入口 `python tools/run_runtime_regressions.py --filter ReforgeAssetTransactions` 执行。

逐字抽取生产锁定/解锁点击方法、材料事务入口、叮当赠礼/落地/提示方法，链接完整 `PropertyLockSystem` 和 `SkyIslandInventoryTransaction`。库存模型复用 SkyIslandDelivery 的宿主替身，模拟官方先改变库存/变量/落地归属再发送通知的顺序；NPC 注册、渲染、玩家角色和 UI 确认状态为替身。两次赠礼尝试使用生产调用方同样的 claimed 条件，测试不读取真实好感存档。

覆盖整件与部分堆叠消耗通知异常、锁定/解锁、重试、变量提交前后异常、官方行重绑、入包/合堆/落地通知异常、满包、全部失败回滚、提示失败及未交付实例释放。L2 隔离证据，不代表 Unity 画面或存档实机通过。
