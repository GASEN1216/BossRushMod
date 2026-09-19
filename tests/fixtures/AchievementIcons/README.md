# 成就图标加载执行回归

`python tools/run_runtime_regressions.py --filter AchievementIcons --jobs 1`

直接编译完整生产文件 `Achievement/AchievementIconLoader.cs`，验证 PNG 优先与缓存、纹理入口、Unity 已销毁缓存刷新、旧包 Sprite / Texture 回退、自有资源释放、失败路径清理、重复清理和路径输入拒绝。

宿主替身只模拟 Unity 对象的已销毁即 null 语义、资源分配 / 销毁计数、AssetBundle 查找与资产类型，以及纹理解码 / Sprite 创建成功和失败分支。测试文件使用单字节标记控制解码替身，不是真实美术资产；本夹具不验证 Unity PNG 解码器、实际图像透明度、Bundle 导入类型或游戏内视觉。没有使用 GameObject，因此不需要模拟其组件销毁。

运行产物只放在仓库 `Build/achievement-icons/`。本夹具属于 L2 隔离回归，不能替代 Windows 正式编译和 L3 游戏内验证。旧 Bundle 的 `Unload(true)` 行为保持现有契约；`ClearCache()` 只直接销毁本加载器创建的资源。
