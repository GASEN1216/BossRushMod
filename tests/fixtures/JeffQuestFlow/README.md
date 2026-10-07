# Jeff 官方任务投影执行回归

2026-10-07：现装官方 `SaveFile` 的 IL 确认 `saving` 是 private static 字段，置 true 后同步调用 `ES3.StoreCachedFile`，成功才置 false，方法没有异常处理区。该契约用于共享受控保存包装：只在本次调用前未保存、同步调用抛出时释放自己留下的闩；行为执行由 `ContentTransactions` 覆盖。本夹具只读程序集，不执行官方存盘。

2026-10-06：覆盖 Campaign 读取失败进入默认缓存或写入故障时，客户端暂不投影，既有已接任务仍保留、第一章不可重接；恢复后继续使用同一个官方任务实例。`OfficialAssemblyContract` 增加现装 `SavesSystem` 和 `EasySave3` IL 校验：切槽先缓存再发事件、读写显式使用当前文件路径、SaveFile 只写缓存而不自动采集、ES3 缓存重载读取物理字节、Store 调用 Sync。真正跨进程的恢复状态验证在 `ContentTransactions`，不把这里的 Campaign 数据替身冒充真实存储。

入口：`python tools/run_runtime_regressions.py --filter JeffQuestFlow`。需要 .NET 8 SDK 和本机游戏 DLL；优先使用 `GAME_PATH`，否则从正式构建的 `Build/BossRush.rsp` 读取 Managed 目录。不启动游戏、不访问玩家存档。

完整链接真实 `OfficialQuestProjection`、任务/奖励组件、Binding、`CampaignOfficialQuestClient`、六章规则表、14 条引导表、引导事实采集器、基地目标提供者和真实章节 JSON/解析器。`ModeHRuntimeModule.HasCompletedMatch` 从当前生产文件提取编译，不重写判据。生成工程、源码 SHA-256 写入 `Build/jeff-quest-flow/`。

覆盖 14 条引导从官方接受、事实采集到基地交付；六章目标、未完成基地设施、拒写/恢复和交付；重建历史、重复注册、换槽、四种官方快照字段过滤、外部 ID 所有权。2026-09-26：234 条断言通过。

替身边界：Unity 对象模拟销毁即 null、销毁 GameObject 连带组件；官方 Quest/QuestManager 按反编译源的初始化、接受事件、目标与交付顺序适配。Campaign 进度/持久化、模式和装备数据源是宿主替身：真实章节追踪与模式桥由 `CampaignPlayability` 执行，真实存档/发奖事务由 `ContentTransactions` 执行。本夹具用通用客户端和 4 个实际天空岛 ID 检查 24 个 ID 共存，**不冒充天空岛客户端端到端测试**；天空岛规则、交互、交付由对应 `SkyIsland*` 夹具覆盖。

`OfficialAssemblyContract.cs` 仅用 PEReader 读取本机 `TeamSoda.Duckov.Core.dll` / `ItemStatsSystem.dll` 元数据和 IL，不加载执行 Unity 程序集。断言 Harvest 唯一 Return、Forget 后清格，Return 按最大堆叠生成物品，背包成功跳过仓库/失败落入仓库，满仓余量经 ItemTreeData 进入 IncomingItemBuffer；同时核对官方食用二次门/扣量顺序、反射字段及 Quest 完成顺序。这是实际 DLL 的静态契约检查，不是执行官方发货或安装 Harmony detour。

反向验证记录见 `Build/jeff-review-negative/results.json`：未结算战报误判、基地目标提前标完成均须触发失败，破坏后按字节恢复并核对 SHA-256。L2 不替代官方任务界面、资源、Mono Harmony 绑定与实机收成数量的 L3。
