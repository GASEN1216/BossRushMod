# 白手起家交付与鸭王杯船票责任回归

通过 `python tools/run_runtime_regressions.py --filter ModeEntryPaymentDelivery` 执行。每次链接完整生产 `ModeHTicketRefund`、`ModeHAvailability`、`SkyIslandInventoryTransaction`，逐字提取 D 开局武器/整套发放、H `TryEnter`、地图选择前缀与费用收据、清理方法；所有生产源 SHA-256 写入输出目录。构建后仅执行本次 MSBuild 返回的 TargetPath。

官方 `Item.Combine` / `ItemUtilities.AddAndMerge` 方法体也参与执行，只适配反编译生成的非法局部标识符及 IL 已初始化的局部变量。替身仅模拟物品槽位、实际归属、同步通知、Cost 值类型与 items 引用、当前槽位和 UI/加载边界，不替换发放、付款匹配、欠票门或合堆修复逻辑。

覆盖武器双拒绝、装备/入包后通知异常、欠票未清拒绝新付款、通知重入、默认不可堆叠船票、满包落地、两处交付都失败后补发、其他模组启用堆叠后 Combine 目标通知异常、同值不同付款、加载拒绝前实际收据、未付款到图、取消/fallback 状态、槽绑定和就绪请求防重入；普通模式原预扣行为另断言。`BOSSRUSH_ENTRY_CASE=starter/debt/refund/stack/receipt` 可单独运行一组。

`BOSSRUSH_ENTRY_SOURCE_ROOT` 可指向稀疏的修复前字节副本，缺文件从当前仓库补齐；旧版本不存在 CaptureModeHPayment 时只有测试适配空方法，旧付款方法仍原样运行。该选项用于旧码转红，不参与生产实现。`BOSSRUSH_FIXTURE_OUT` 可写到独立 Build 目录。

这是 L2 证据；不启动游戏、不读写存档。官方付款事件的前置订阅者抛异常会阻断后续回执，这是当前接线的实机边界；这里不声称证明全部第三方付款回调顺序。
