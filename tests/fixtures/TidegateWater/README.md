# TidegateWater 隔离执行回归

完整链接生产 `TidegateWater.cs`、`TidegateWorld.cs`、`TidegateWaterRegions.cs`。
从当前发布源 `ArtSource/Tidegate/export/terrain_lagoon.u8` 按作者构建器同样的行翻转与 RGHalf 精度生成真实遮罩输入；
保留抗锯齿中间值，再转回 `GetPixels()` 提供的 float，避免把半精度量化差异带入轮廓判定。
托管双线性采样使用着色器的 GPU 归一化 uv / 纹素中心口径 `uv * size - 0.5`，与可见水面共享同一条 G=0.5 分界。

执行首次建水体、高潮 / 低潮变化、泻湖放水与再涨潮，核对每只真实生产代码创建的盒子的世界顶面。
以官方相同的 0.2 m 球对 AABB 做空间查询，核对四个曾被错误划为泻湖的低岸坐标、真内湖与外海同时查询；
检查合并区域无面积重叠、没有面积缺失，并输出实际盒数、托管采样次数与离线生成耗时。
另核对 `(56.75,-16)` 的 G≈0.492541504、`(80.75,25.5)` 的 G≈0.848585644，
以及附近有接缝余量的点与四个旧岸点的精确 G；前两点可阻止回退到缺少半纹素偏移的旧 CPU 口径。

Unity 的对象树、Transform 缩放、BoxCollider bounds、球盒相交、Texture 与属性入口由替身提供；
不运行 Unity、官方游泳控制器、PhysX 或渲染，也不证明真实游戏的帧时间或轮廓接缝观感。
0.25 m 栅格轮廓近似带约 0.18 m，另有官方球查询半径 0.2 m 的跨界带，仍须实机验证。

入口：`python tools/run_runtime_regressions.py --filter TidegateWater`。
