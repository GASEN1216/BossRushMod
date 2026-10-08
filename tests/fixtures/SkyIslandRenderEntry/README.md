# 天空岛渲染准入回归

直接编译完整生产 `SkyIslandRendering.cs`，执行真实材质遍历、官方候选选择、转换、碰撞门与 `Dispose`。验证健康作者 shader 原样保留、按 shader 去重；不支持或缺 GBuffer 时按环境/水面/云的顺序选择可用官方候选；所有候选不可用时仍拒绝；世界材质名称精确保留供搜刮箱查找；贴图、非平凡 UV、颜色、HDR 自发光与队列保留；同名不同引用隔离、共享引用复用、会话隔离与失败后清理。

Unity Shader / Material / Renderer 是可控边界替身，覆盖支持状态、pass 标签、实际属性读写、材质共享与图层。Object 模拟已销毁对象等于 null 与根节点销毁连带组件。替身不渲染像素，不证明 GPU 支持、贴图 ST 实际消费、双面状态或画面等价；官方候选的属性/GBuffer/Cull 依据本机 `resources.assets` 离线核对，视觉效果仍需实机验证。

运行 `python tools/run_runtime_regressions.py --filter SkyIslandRenderEntry`。每次独立构建并执行 MSBuild 返回的本次 TargetPath，留下生产源码 SHA-256 和执行产物 SHA-256。负向验证可将 `BOSSRUSH_SKY_RENDER_SOURCE` 指向旧生产源码或独立变异副本，并用 `BOSSRUSH_FIXTURE_OUT` 指定隔离输出，不修改共用工作区。
