# NPCShopPayment

`run.py` 从 `Integration/Affinity/Systems/NPCShopSystem.cs` 提取当前支付策略、购买 / 回滚 / 出售回调，以及 `CloseShop`、owner 匹配、`ResetStaticCaches`、`Cleanup` 和事件注册 / 退订方法。每次在独立目录构建，并执行 MSBuild 返回的本次 `TargetPath`，日志记录路径与 SHA-256。

现金直通、净化点支付、扣点失败回滚、缺少价格、外店回调和禁止出售沿用原覆盖。生命周期用例验证匹配商店界面关闭一次、关闭前取消服务状态和事件监听、重入与重复清理、外店界面不受影响、静态重置和 UI 已卸载；还验证先关界面再销毁商店及展示物品。

替身提供官方商店事件、库存、现金和界面行为；`StockShopView.Close` 按官方顺序模拟 `open = false`、关闭事件和输入释放，Unity 对象模拟销毁判空及 GameObject 级联销毁组件。测试不加载 Unity，也不证明实机界面或购买可用。
