# ResourceProduction

执行生产 ResourceBundleLoader、FactoryResourceLoading、ProductionIconCache 和逐字抽取的 Dev 纯指标判据。
Unity 替身控制原生请求完成时机、对象销毁语义与纹理借用；不模拟 GPU 性能或真实 Unity 调度。
覆盖成功转交、请求取消、场景失效取消、迭代器销毁、宿主销毁、超时、失败、加载子资源中销毁、图标别名/缓存/释放和分位数。
FactoryResourceLoading 的真实协程通过递归驱动器测试：切换 scene handle 后不消费旧请求，等待迟到结果卸载，再在新场景重试并只注册一次；销毁宿主后不再重试。物品配置器/工厂底层注册仍是替身，该测试不证明实际游戏物品可用或无卡顿。
入口：`python tools/run_runtime_regressions.py --filter ResourceProduction`。

四建筑补充：逐字抽取 BuildingModelHelper.TryInstantiateBundle，验证缺文件、缺指定 prefab、成功转交、实例激活异常后的实例与包清理；Unity 替身不验证实际渲染或原生内存释放。

2026-10-10：共享贴图复制 API 由 `tests/fixtures/TextureCopyStubs.cs` 补齐显式替身。ResourceProduction 执行生产 CloneTexture，覆盖不支持、复制参数、成功归属与异常释放；替身不验证真实 GPU 像素。

2026-11-19：ResourceBundleLoader.FindLoaded 是找已加载包的唯一入口，执行生产实现：包内名按 / 与 \ 取最后一段、OrdinalIgnoreCase 全等，子串、已销毁、无名不算，枚举抛错返回 null。RunSpecial 在换场景作废一次尝试后重新判 alreadyLoaded：等场景期间消费方已持有包时直接交给消费方，不发第二次原生加载，迟到的旧请求照常卸载一次。

2026-11-20 增加同步恢复接管超时包请求和预制体请求的覆盖，同时验证嵌套作用域退出、迟到回调与同一路径的新请求，以及普通查询跳过预载暂持的包。已经开始等待的 `RunSpecial` 会在恢复接管后重新检查持有状态，压缩图标会在普通查询遇到排空后重试。替身可延迟派发完成事件并记录强制读取次数，真实 Unity 的等待时长和素材显示仍需实机核对。
