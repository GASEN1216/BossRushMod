# 天空岛渲染准入回归

原样提取生产 `SkyIslandRendering.Apply` 与其 shader 校验方法，执行真正的材质遍历和准入判断。验证环境、水、云三类 shader 保留、按 shader 去重、合法 Forward 但缺 GBuffer 的旧包被拒、不可用 shader 被拒、装饰材质原回退仍可用。

Unity Shader / Material / Renderer 是可控边界替身，只模拟 pass 标签、材质共享与图层，不渲染像素，也不证明 GPU 兼容或黑幕恢复；后者由 SkyIslandSceneReferenceBridge / SkyIslandRaidLease 夹具验证。运行 `python tools/run_runtime_regressions.py --filter SkyIslandRenderEntry`。
