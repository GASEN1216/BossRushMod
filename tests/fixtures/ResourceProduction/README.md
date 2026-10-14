# ResourceProduction

执行生产 ResourceBundleLoader、FactoryResourceLoading、ProductionIconCache 和逐字抽取的 Dev 纯指标判据。
Unity 替身控制原生请求完成时机、对象销毁语义与纹理借用；不模拟 GPU 性能或真实 Unity 调度。
覆盖成功转交、请求取消、场景失效取消、迭代器销毁、宿主销毁、超时、失败、加载子资源中销毁、图标别名/缓存/释放和分位数。
FactoryResourceLoading 的真实协程通过递归驱动器测试：切换 scene handle 后不消费旧请求，等待迟到结果卸载，再在新场景重试并只注册一次；销毁宿主后不再重试。物品配置器/工厂底层注册仍是替身，该测试不证明实际游戏物品可用或无卡顿。
入口：`python tools/run_runtime_regressions.py --filter ResourceProduction`。

四建筑补充：逐字抽取 BuildingModelHelper.TryInstantiateBundle，验证缺文件、缺指定 prefab、成功转交、实例激活异常后的实例与包清理；Unity 替身不验证实际渲染或原生内存释放。

2026-10-10：共享贴图复制 API 由 `tests/fixtures/TextureCopyStubs.cs` 补齐显式替身。ResourceProduction 执行生产 CloneTexture，覆盖不支持、复制参数、成功归属与异常释放；替身不验证真实 GPU 像素。

2026-10-13：逐字提取实际 ResourcePerformanceWindow / Report、WriteResourcePerformanceReport 和 AddResourcePerformanceManifest，链接生产 BossRushCoopJson 与共享解析器，执行实际报告文件及两种 manifest 的写出。验证嵌套窗口、真实纯判据算出的 CPU 样本与分位数、明确 unavailable 的 GPU、完整 LoadRecord 数组、世界位置 / 相机四元数及 read_only。受控路径位于本次 TargetPath 附近；报告 runner 生命周期和 Unity 引擎采样是替身，不把构造样本称作实测性能。写出嵌套报告被改为空 JSON 时，副本回归在实际窗口断言转红。使用显式构建的 fresh TargetPath 和 SHA-256。
