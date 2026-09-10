# 天空岛原创生活模型与布景

2026-09-09。分类：`COMPAT / OPERATIONAL`；离线布景 metadata 的兼容扩展归为 `SCHEMA+`。本页记录本轮生活模型、摆放与作者验收流程。独立 Unity 美术预览、补种复核与最终重建已完成；最终资源统计、物理报告和部署指纹以 [Validation](Validation/) 中对应构建的记录为准。

## 模型与分布

本轮新增 14 类原创模型，在 12 座岛上放置 28 个实例，每类均已进入整图摆放。模型定义在 [sky_island_life_models.py](../../tools/sky_island_life_models.py)，摆放事实源是 [settlement_layout.json](settlement_layout.json)。以下分布按当前规划记录列出；它描述离线布景，游戏内可见性与通行仍须用最终资源验收。

| 模型 | 稳定 key | 岛区 | 实例数 |
| --- | --- | --- | --- |
| 双色篷布菜摊 | `produce_stall` | B | 1 |
| 双轮补给手推车 | `supply_handcart` | A、C | 2 |
| 野营 A 字帐篷 | `camp_tent` | D、S3 | 2 |
| 工具钳台工作台 | `tool_workbench` | G、S4 | 2 |
| 圆肚雨水桶架 | `rain_barrel` | C | 1 |
| 小风车菜圃 | `windmill_planter` | B、C、S1 | 3 |
| 浮舟物资箱组 | `floatboat_cargo` | A、G | 2 |
| 木柴与劈柴架 | `firewood_rack` | D、G | 2 |
| 鸭嘴邮筒 | `duck_mailbox` | B、S2 | 2 |
| 晾晒架 | `laundry_rack` | B | 1 |
| 堆肥种植箱 | `compost_planter` | C | 1 |
| 野餐桌凳组合 | `picnic_table` | B、E、F、H | 4 |
| 生活区路牌 | `wayfinding_sign` | E、H | 2 |
| 茶水灶 | `tea_stove` | B、D、F | 3 |

A 为登云码头，B 为风铃集，C 为青穗梯田，D 为悬根林，E 为鸣风栈道，F 为镜水寺，G 为残星工坊，H 为归航钟庭；S1 至 S4 分别为蛙鸣池、倒挂邮亭、听雨洞与残星瞭台。码头强调运输与补给，村落强调售卖、用餐和晾晒，农田强调蓄水与种植，工坊强调修理和堆料，支路以少量道具说明驻留用途。

模型采用程序化原创网格，复用整图生成器的盒体、圆柱、梁、曲面与共享色板。主要轮廓先由圆润、厚实的体块确定，再补篷布折面、绳索、车轮、把手和工具等辨识细节；布面带厚度和封边，叶片保留体积。`build_model` 构造单体，`model_geometry` 提取按材质分组的网格，`stamp` 将同一套几何合入岛区。独立模型库与整图消费同源造型，避免两份模型分别演变。

作者坐标为 Unity XYZ 米制，Y 向上，模型正面朝 -Z；独立 FBX 导出时归一底部原点，整图摆放依据真实网格最低点贴地。模型按岛区与共享材质合并，运行时加载成品资源。

## 构图与研究依据

以下三篇公开制作拆解已取得正文，并核实标题与 canonical 链接。文中的制作经验与本岛应用分别说明；参考用于构图和造型方法，本轮 14 类生活模型由仓库生成器原创制作。

1. [Julia Cui：Magic Moorland](https://www.exp-points.com/julia-cui-magic-moorland)。作者用道路和资产朝向引导到主建筑，区分前、中、后景，并将与房屋争夺焦点的大树移到后方；植被颜色变化保持相近明度。天空岛沿用每区一个主要地标的组织方式，让桥口和铺路指向地标，高体量植物留出建筑轮廓，低矮绿植集中在路侧和道具外围。
2. [Kayla Kosik：Creating A Stylized Cozy Cottage In Unity](https://www.exp-points.com/kayla-kosik-creating-a-stylized-cozy-cottage-in-unity)。作者将建筑拆为简单体块，复用装饰条和材质，利用柔软植被及天空、环境色统一场景。天空岛以共享色板和可复用小件补足生活细节；植被按树冠、灌丛、低草花安排高度层次，各组保留大小变化和空隙。
3. [Michael Khinevich：The Moon Gate](https://www.exp-points.com/michael-khinevich-the-moon-gate)。作者通过地形和光线突出入口，用主道具损伤与周围残余结构表达场所历史，并将草片组合成草簇、复用资产形成变体。天空岛按运输、炊饮、种植与维修活动组织道具组合，利用摆放关系说明居民的日常用途；这些具体组合是本岛设计。

布景落点由 [sky_island_settlement.py](../../tools/sky_island_settlement.py) 规划：计算包含绳索、把手和外伸工具在内的实际变换网格包围盒，避让既有铺路、桥口、玩法标记与中央活动空间。实障碍通过 `life_prop` 进入共同几何。低矮草簇、碎石和灌丛沿路侧断续成组布置，并在道具周围补植，摆放需经过 `PlantingSpace` 净空检查。模型几何变化后必须重做规划，再生成导航和整图。

## 作者工程与生成链

作者工程仍为 `D:/code/ykf/duckov_modding-main/UnityFiles/BossRush`，Unity 版本为 `2022.3.62f3`。工程内路径如下：

- 整图源文件：`ArtSource/SkyIsland/SkyIslandWorld.blend`；模型库：`ArtSource/SkyIsland/SkyIsland_ModelKit.blend`。
- 整图 FBX 与 prefab：`Assets/SkyIsland/SkyIslandWorld.fbx`、`Assets/SkyIsland/SkyIslandWorld.prefab`。
- 作者预览场景：`Assets/SkyIsland/SkyIslandAuthoring.unity`；正式出击场景：`Assets/SkyIsland/SkyIslandRaid.unity`。
- 模型库：`Assets/SkyIsland/Models/`，清单为 `sky_island_model_kit.json`，Unity 单体 prefab 在其 `Prefabs/` 子目录。

生成顺序具有依赖关系：

```text
tools/sky_island_settlement.py 规划
  -> tools/sky_island_navigation.py
  -> tools/generate_sky_island.py
  -> tools/generate_sky_island_kit.py
  -> Unity world builder
  -> Unity raid builder
```

规划阶段使用 Blender，读取作者工程已有的 `Assets/SkyIsland/sky_island_layout.json`，输出仓库 `ArtSource/SkyIsland/settlement_layout.json`。规划记录模型源指纹、变换、实际障碍范围和受保护的玩法标记。随后 [sky_island_navigation.py](../../tools/sky_island_navigation.py) 消费规划中的障碍，更新仓库 [layout.json](layout.json) 与作者工程布局副本，地面、碰撞和导航继续来自共同几何。

[generate_sky_island.py](../../tools/generate_sky_island.py) 按计划摆入模型，复核模型范围与受保护标记，并将摆放及路侧补植记录写入作者工程 `ArtSource/SkyIsland/sky_island_dressing.json` 的 `settlement` 字段。[generate_sky_island_kit.py](../../tools/generate_sky_island_kit.py) 从相同模型定义导出独立 FBX、模型库 blend 与清单；它的单体导出本身不负责整图摆放。

Unity world builder 位于作者工程 `Assets/Editor/SkyIslandBundleBuilder.cs`，入口 `BossRush.SkyIslandBundleBuilder.BuildResourcesAndExit`，负责导入、材质/碰撞/导航装配、保存整图 prefab，并输出 `SkyIslandExport/sky_island_world` 与作者报告。Unity raid builder 位于 `Assets/Editor/SkyIslandRaidBuilder.cs`，入口 `BossRush.SkyIslandRaidBuilder.BuildAndExit`，消费更新后的 prefab，输出 `SkyIslandRaidExport/sky_island_raid` 和 `raid_scene_validation.json`。该入口只构建资源，独立作者预览入口见下一节，不能只重渲旧 prefab 就认定整图或正式包已更新。

正式运行时由 [SkyIslandRaidLease](../../DebugAndTools/SkyIsland/SkyIslandRaidLease.cs) 只加载仓库/部署目录中的 `Assets/arenas/sky_island_raid`。`sky_island_world` 在当前流程中用于作者资源构建与物理验证，不作为游戏天空岛入口；[compile_official.bat](../../compile_official.bat) 的天空岛部署对象为正式 raid 包。离线布景 metadata 的 `SCHEMA+` 分类对应作者数据扩展，不涉及玩家存档 schema。

## 独立预览与物理入口

[build_sky_island_art_preview.ps1](../../tools/build_sky_island_art_preview.ps1) 将作者美术、资源构建器和所需 metadata 复制到独立工程 `Build/sky-island-art-preview`。它配置独立的 URP 预览依赖，不导入游戏 DLL，规避作者宿主中 `SodaCraft.LightControl` 缺少 Umbra 依赖造成的渲染问题。游戏 DLL 无需修改。

在仓库根目录按所需阶段执行：

```powershell
./tools/build_sky_island_art_preview.ps1
./tools/build_sky_island_art_preview.ps1 -Physics
```

默认调用独立工程中的 `BossRush.SkyIslandBundleBuilder.RenderPreviewsAndExit`，从已构建的 prefab 生成真实 Unity 预览，日志为 `Build/sky_island_life_preview.log`。当前已成功得到这条路径的 Unity 预览。

`-Physics` 将原作者工程构建的 `SkyIslandExport/sky_island_world` 与 `sky_island_bundle_validation.json` 复制到独立工程，调用 `ValidatePhysicsAndExit`，在 PlayMode 加载该 world 包执行地面射线、玩法点胶囊与导航面地面检查。入口核对源 FBX 与包的构建指纹，日志为 `Build/sky_island_life_physics.log`，输出报告在 `Build/sky-island-art-preview/SkyIslandExport/`。模型或布局更新后，应先重新构建匹配的 world 包；该检查不覆盖正式 raid 场景的完整游戏流程。

两种调用会写入独立预览工程及日志，须在该工程空闲时串行运行。截图需要图形设备；PlayMode 检查依赖跨帧回调，脚本已由对应入口自行退出。

## 交付与验收记录

最终验收（2026-09-09）：14 类模型、28 个实例、12 座岛、76 个原布局标记保持一致；77 组道具周边补植覆盖全部 28 个实例。独立模型库包含原有 10 个和新增 14 个，共 24 个 FBX/Prefab。Unity 导入场景为 686 个 Renderer、43 个材质、1,176,955 个可见三角形，导航为 4037 顶点 / 4215 三角形。离线模型导出统计与 Unity 导入统计口径不同，分别保留原始报告。

`compile_official.bat` 的 Windows Release 编译通过并完成本机部署；`python tools/run_guards.py --filter SkyIsland` 为 11 PASS。`SkyIslandSettlementGeometryTests.py` 对照真实生成的网格贡献、模型库材质、导出标记和补植净空，拒绝 8 种人为破坏；它仍是离线元数据验证。作者 PlayMode 从最终 world 包加载，49 个玩法点地面与胶囊检查、4215 个导航面地面射线通过。正式 raid 包严格构建和路径回载通过，59,469,605 字节，作者 / 仓库 / 游戏 SHA256 一致，详见 `Validation/raid_deployment_hashes.json`。按用户此前要求未启动 Duckov，游戏内遮挡、寻路、战斗和帧率没有实测。

最终截图统一整理到 [Previews](Previews/) 下的 WebP 文件。包含村落 `SkyIsland_Unity_Village.webp` / `SkyIsland_Unity_Village_Detail.webp`、码头 `SkyIsland_Unity_Dock_Life.webp`、农田 `SkyIsland_Unity_Farm_Life.webp`，以及生活模型陈列图 `SkyIsland_LifeKit_Preview.webp`。本轮 14 张最终预览已复制为 WebP，来源指纹见 `Validation/settlement_preview_validation.json`。生活模型陈列图由 Blender 模型库渲染生成，区域图来自 Unity 作者预览，应保留来源区分。

补种复核与最终重建已完成。最终三角数、Renderer/材质规模、导航规模、包大小与 SHA256 统一查 [Validation](Validation/) 中对应版本的几何、模型库、布景、物理及 raid 部署记录；报告需能对应最终源文件与资源包，旧轮次 PASS 不自动覆盖新资源。

本轮验收应核对 14 类模型是否全部实际摆入、28 个实例是否覆盖 12 岛，以及道具正面、桥口、主路、玩法标记与农田的净空。继续检查村落、码头、农田等区域的地标可读性，四时段光色与遮挡，并核对 world 物理报告和正式 raid 包。Unity 预览成功只证明作者渲染链路可用；本文不预先宣称最终物理、全量守卫、部署或游戏内通行、战斗与性能全部通过。
