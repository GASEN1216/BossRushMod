# 石堡前哨地图隔离回归

链接生产 `StoneOutpostMapDataLease.cs` 与 `StoneOutpostMapRaster.cs`，执行引用归还、重复释放、后来 owner 接管、其他模块单字段更新、空条目拒绝、地图坐标轴/边界/网格检查。

Unity 图形和地图数据类型由简单替身提供；不模拟官方显示池、Harmony、鼠标输入或 Unity 已销毁对象语义，不替代实机地图操作。可给 `run.py` 传 Blender 几何 JSON 路径和 PPM 输出路径，生成实际生产栅格算法的北朝上预览。

入口：`python tools/run_runtime_regressions.py --filter StoneOutpostMap`。
