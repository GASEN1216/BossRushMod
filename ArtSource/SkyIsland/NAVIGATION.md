# 晴岚群岛几何事实源

2026-09-08。分类：`COMPAT`（新增离线资源），不修改存档、地图配置入口或游戏模式。

`layout.json` 由 `tools/sky_island_navigation.py` 生成，是 Blender 场景、实体碰撞和 A* 导航共同消费的几何事实源。坐标固定为 **Unity X/Y/Z、米**，Y 向上；运行时整体偏移只应加一次。不要独立移动某一份桥或岛的碰撞。

## 重建与检查

在 BossRushMod 根目录执行：

```powershell
python -m pip install --target Build/sky-island-python-deps shapely==2.1.2
python -X utf8 tools/sky_island_navigation.py
python -X utf8 tests/SkyIslandNavigationPropertyTest.py
```

生成器需要 Shapely 2.1 的 GEOS 约束三角化；依赖可放在被 Git 忽略的任务目录，不必修改全局 Python。默认同时输出此目录的 `layout.json`、`navigation_validation.json` 和作者工程 `D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/Assets/SkyIsland/sky_island_layout.json`。`--output`、`--source-copy` 可覆盖输出路径，`--validate <json>` 只验证指定文件。

独立回归 `tests/SkyIslandNavigationPropertyTest.py` 只需 Python 标准库，直接检查保存的 JSON；不导入生成器，也不依赖 Blender、Unity、Shapely 或游戏进程。它还主动破坏桥、三角面、标记、障碍、高程、边界和顶点预算，另覆盖桥转角、横截面、桥口共边、高程缓入与未焊接桥口，确认十二种错误均被拒绝。

## 消费约定

| 字段 | 用途 |
| --- | --- |
| `islands` | 12 岛的 `id/name/theme/center/size/height/outline`；`outline` 为 X/Z 二维实际边界，桥口与两侧 10 m 保持直线，其余岸段圆润起伏 |
| `bridges` | 15 条全部开放的实体连接；`path` 为相切圆弧及缓坡采样后的三维路点，`controlPointsXZ` 保留原路线控制点，`entryEaseMeters` 记录桥口缓入长度；`width` 为实体宽度，`crossSections/surfaceTriangles` 保留实际缓坡高程 |
| `obstacles` | 唯一实障碍表：`id/island/kind/center/size/yaw/bounds`；`bounds` 是 XZ 矩形，`size` 顺序为 X/Y/Z |
| `ground` | 真实可行走地面，给可见铺地与 MeshCollider；不包含建筑或水池内部 |
| `navigation` | 仅给 A*。由实体域内缩 0.7 m、障碍外扩等价余量后，按每个实体三角片切分，保留同一高程 |
| `sourceGroundFaces` | 位于 `navigation` 内，与导航三角形一一对应的原实体面索引，用于逐面几何/高程复核 |
| `boundaryEdges` | 各 mesh 的边界索引对；地面外边界可用于护栏，障碍洞边界需要剔除后再做栏杆 |
| `markers` | 76 个唯一、落在导航安全域且连通的标记；`Exit` 是 `MainExtraction` 同坐标别名 |
| `verifiedPathsFromSpawn` | 由实际导航面边及面内线段组成的可行路线折线；路径长度是上界，不是游戏 A* funnel 最短距离 |
| `validation` | 当前生成结果的面积、包围框、坡度、数量、连通性与路线实测报告 |

全部 `vertices` 为三维数组，全部 `triangles` 为三个顶点索引，Unity 面朝上。桥坡为分片连续平面；桥口与目标岛必须共享完整边，不能只碰一个顶点、留 T 型接缝或依赖接近的坐标自动连通。

所有主区有 `Region_`、`EnemySpawn_`、`POI_`、`Search_`、`Search_*_02`、`Lamp_`、`Lamp_*_02`；S1–S4 有区域、敌人、POI、搜索点。`PlayerSpawn` 在 A，`MainExtraction` 和别名 `Exit` 在 A，`BellExtraction` 在 H。F 的区域点取 `(215,26,-65)`，避开镜池；任何新标记都必须重新执行几何检查。

装饰树根、柱脚、桌椅若实际挡角色，应加入同一障碍表或收进已有障碍脚印；不能仅增加可见模型，再给它随意补独立碰撞。纯薄铺地、空中风铃、树冠等装饰不应产生额外障碍。

## 本次离线验证结果

- 12 岛、15 条全开放道路、60 个障碍、76 个标记。风铃柱、鸭雕像与开放亭柱单独避障，钟庭底座按真实最外脚印加宽。
- 导航 3533 顶点 / 3655 三角面；实体地面 1274 顶点 / 1396 面，低于游戏 A* 单块 4095 顶点限制。
- X `-397.5…387.5`、Y `0…62`、Z `-350…375` 米；包围范围包含天空空隙。
- 实体可行走 XZ 面积 `223124.765 m²`；导航 XZ 面积 `215164.641 m²`，导航三维表面积 `215274.580 m²`。
- 全部导航共边连通，15 条桥分别验证两端与声明岛屿共边；最大坡度 `16.3139°`。
- 导航与实体边界、障碍的水平安全余量 `0.7 m`；逐导航面复核实体覆盖与高程相符。
- 出生至 H 区的可行路线长 `914.99 m`。按 A 出生 → B → C → D → E → G → F → B → E → H → 钟庭撤离的示例探索路线为 `2677.51 m`。均为可行折线的上界，不能据此声称精确最短路径或游玩时长。

这些是离线实际几何检查。Unity 导入、游戏内角色通行、AI 行为、镜头遮挡与性能必须另行验证；不能把此报告当作实机 smoke 通过。

桥路视觉由同一 `crossSections` 构造纵梁，桥板和桥墩按全桥弧长连续布置。岛内铺装的 30 个入口吸附真实桥心并沿入岛方向设置 8 / 16 米控制点，前 8 米与桥严格相切；花草布景读取实际铺路采样点避让。
