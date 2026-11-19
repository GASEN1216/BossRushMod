# 成就图标加载执行回归

`python tools/run_runtime_regressions.py --filter AchievementIcons --jobs 1`

直接编译完整生产文件 `Achievement/AchievementIconLoader.cs`，验证 PNG 优先与缓存、纹理入口、Unity 已销毁缓存刷新、旧包 Sprite / Texture 回退、自有资源释放、失败路径清理、重复清理和路径输入拒绝。

宿主替身只模拟 Unity 对象的已销毁即 null 语义、资源分配 / 销毁计数、AssetBundle 查找与资产类型，以及纹理解码 / Sprite 创建成功和失败分支。测试文件使用单字节标记控制解码替身，不是真实美术资产；本夹具不验证 Unity PNG 解码器、实际图像透明度、Bundle 导入类型或游戏内视觉。没有使用 GameObject，因此不需要模拟其组件销毁。

运行产物只放在仓库 `Build/achievement-icons/`。本夹具属于 L2 隔离回归，不能替代 Windows 正式编译和 L3 游戏内验证。旧 Bundle 的 `Unload(true)` 行为保持现有契约；`ClearCache()` 只直接销毁本加载器创建的资源。

2026-09-20 补充：逐字抽取 `ItemFactory.GetSpriteFromFile` 与 `Shutdown`，验证重复配置只解码一次、路径分隔符共用缓存、已销毁缓存重载、解码拒绝/抛错与 Sprite 创建失败都立即清理。两条 PNG 生产路径均断言 `markNonReadable=true`。ItemFactory 的 Mod 路径与其他缓存字典为隔离替身，不覆盖游戏注册和 Bundle 加载；EquipmentHelperIcon 的转发由源码接线与正式编译核对。

2026-10-10：共享贴图复制 API 由 `tests/fixtures/TextureCopyStubs.cs` 补齐显式替身。ResourceProduction 执行生产 CloneTexture，覆盖不支持、复制参数、成功归属与异常释放；替身不验证真实 GPU 像素。

2026-11-19：按路径取图收成 `Integration/ModSprites.cs` 一个入口。夹具改为直接编译完整生产文件 `ModSprites.cs`（不再逐字抽取 ItemFactory 方法、不再链接已删除的旧散图解码器），验证：路径分隔符与大小写共用缓存、只解码一次、Unity-null 缓存重取（Mod 停用再启用）、非法路径（绝对路径、盘符、..）不碰磁盘、解码拒绝 / 抛错 / 空文件 / Sprite 创建失败都立即清理、正式构建且生产包在时只借包里的图不读散图、`Exists` 只查包目录不加载资源、`GetUnpacked` 照读散图、借来的压缩图不被 ModSprites 销毁、自己解码的只销毁一次；成就图标与地图缩略图改为借用 ModSprites，`ClearCache` / `ResetStaticCaches` 只放下引用。生产图标包是 `AssetBundle` 替身（`FileResult` / `Contains` / `AssetLoads` 计数），不是真实压缩包。
