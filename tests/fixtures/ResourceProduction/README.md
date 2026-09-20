# ResourceProduction

执行生产 ResourceBundleLoader、FactoryResourceLoading、ProductionIconCache 和逐字抽取的 Dev 纯指标判据。
Unity 替身控制原生请求完成时机、对象销毁语义与纹理借用；不模拟 GPU 性能或真实 Unity 调度。
覆盖成功转交、请求取消、场景失效取消、迭代器销毁、宿主销毁、超时、失败、加载子资源中销毁、图标别名/缓存/释放和分位数。
FactoryResourceLoading 的真实协程通过递归驱动器测试：切换 scene handle 后不消费旧请求，等待迟到结果卸载，再在新场景重试并只注册一次；销毁宿主后不再重试。物品配置器/工厂底层注册仍是替身，该测试不证明实际游戏物品可用或无卡顿。
入口：`python tools/run_runtime_regressions.py --filter ResourceProduction`。

四建筑补充：逐字抽取 BuildingModelHelper.TryInstantiateBundle，验证缺文件、缺指定 prefab、成功转交、实例激活异常后的实例与包清理；Unity 替身不验证实际渲染或原生内存释放。
