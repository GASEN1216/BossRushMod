本夹具通过聚合入口 `python tools/run_runtime_regressions.py --filter IntegrationLeafOwners` 运行。

直接链接生产护士模块与宿主桥、Mutator 流程与宿主桥、Mode G managed-character 服务，逐字提取 Integration 模块的船票注册和动态初始化方法，以及旧宿主四个登记入口。验证护士 owner 隔离、生成与失败清理顺序、静态 prefab、Unity 假 null 与连带销毁，船票标签/注册/回退/卸载和跨 owner 静态闩，Mutator 门控/上下限/清理顺序，Mode G 暂存登记、异步失效、冻结/配置/激活/失败清理。

Unity、NPC 资源加载、物品表、配置器、场景与 Mode G 登记表为可观测替身；`UniTask` 用 .NET 的 async method builder 转接 Task。实际游戏 API、资源和显示效果不由此夹具证明。生产文件哈希落在 `Build/runtime-regressions/IntegrationLeafOwners/production-sha256.json`。
