# 野外趣事入口与落点隔离回归

直接链接生产 `FieldAdventureSupport.cs`，覆盖普通出击入口、基地与模式隔离、加载与死亡、主/子场景归属、导航连通、地面/坡度/净空、玩家真实路线（`FieldAdventureRoute` 替身：走不到即作废、只在廉价判据之后查、逐档放宽每档都要求）、候选预算及无效坐标。

宿主替身只提供场景、角色和物理/导航查询的可控响应。它不证明官方地图实际可达性、物理碰撞或游戏内渲染；`FieldAdventureRoute` 的 A* 路径与沿路扫墙只有 L1 接线与实机能证。

运行：`python tools/run_runtime_regressions.py --filter FieldAdventureGate`。
