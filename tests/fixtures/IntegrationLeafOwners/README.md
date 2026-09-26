本夹具通过聚合入口 `python tools/run_runtime_regressions.py --filter IntegrationLeafOwners` 运行。

直接链接生产护士模块与宿主桥、Mutator 流程与宿主桥、Mode G managed-character 服务，逐字提取 Integration 模块的船票注册和动态初始化方法，以及旧宿主四个登记入口。验证护士 owner 隔离、生成与失败清理顺序、静态 prefab、Unity 假 null 与连带销毁，船票标签/注册/回退/卸载和跨 owner 静态闩，Mutator 门控/上下限/清理顺序，Mode G 暂存登记、异步失效、冻结/配置/激活/失败清理。

Unity、NPC 资源加载、物品表、配置器、场景与 Mode G 登记表为可观测替身；`UniTask` 用 .NET 的 async method builder 转接 Task。实际游戏 API、资源和显示效果不由此夹具证明。生产文件哈希落在 `Build/runtime-regressions/IntegrationLeafOwners/production-sha256.json`。

2026-09-26 宿主载体同步：护士和 Mutator 宿主桥读取 `IntegrationHostCompatibility.cs` 的完整命名区域；动态物品旧入口仍从 `BossRushIntegration.cs` 提取，模块与服务源码保持直接链接。

2026-09-26 P6 接点回归：逐字执行地图入口的 `GetBossRushTicketTypeId` 与 `CreateBossRushCost`，连接同一份生产注册状态，覆盖注册后的 500001、未注册的 868 回退、实际加载的备用正 ID、再次注册后的实时刷新，以及零现金/一张票费用。`Cost` 仅替代官方数据结构，不替代物品注册或地图查询逻辑；不证明官方 UI 的实际扣费与退款。
