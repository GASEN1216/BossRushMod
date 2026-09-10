# 天空岛场景增景与桥路优化

2026-09-08。分类：COMPAT / SCHEMA+（离线布局新增可选几何元数据）/ OPERATIONAL（本机资源重建与部署）。

桥路由折线路点改为相切圆弧，所有 15 条桥、30 个桥口共用实体桥面、护栏与导航的横截面。最大分段转角 10°；4 米桥口缓坡的入口最高约 2.10°；岛内铺路吸附真实桥中心并沿桥方向延伸 8 / 16 米后转弯。桥板和桥墩按全桥累计里程布置，纵梁共用横截面端点；焊点检查覆盖舍入格边缘的近重合顶点。

免费素材来自 Kenney Nature Kit（CC0-1.0）：原包、License.txt、36 组精选 OBJ/MTL、清单与导入模块保存在 `ArtSource/SkyIsland/ThirdParty/KenneyNatureKit/` 和 `tools/sky_island_nature_assets.py`。`tools/sky_island_dressing.py` 将其中 13 种草甸模型组成 194 处花草簇、2303 株装饰，另用睡莲叶制作莲花；配合原创粉白树冠、40 处浮空晶簇花园、9 株月光蘑菇、225 条悬崖藤蔓、9 条根拱花藤、8 朵莲花、28 盏祈愿灯与少量发光花粉。各区按花色和植物种类区分，草甸按真实模型半径避开铺路、桥口、建筑、标记与中央空场；高体量晶簇与月蘑菇放在可行走岛面之外。

云团在 Blender 中融合为静态平滑网格，56 组云分近、中、远层，各组球冠大小、高度、缩放与朝向不同；运行时只消费成品网格。`Cloud` 专用 shader 使用不透明深度写入、冷暖阴影、银边和轻微缓漂；远平面改为与四时段天空匹配的浅色雾底，移除周期条纹和可见背景边缘。`Environment` / `Water` 增加晶体柔光、细波与瀑布流纹，`PearlGlow` / 晶体可轻微呼吸。保留原三个 shader 名与四个会话光照全局参数，无新增运行时组件、逐物体 Update、实时灯或透明叠层。

新增植物、晶簇与灯饰按岛区/共享材质合并；护栏去掉密集曲线节点上的重复柱头、降低小件面数并按所在岛/桥分组剔除。云有独立扩大后的包围盒，关闭会重算该包围盒的静态合并。最终 Unity 资源为 632 Renderer、924964 可见三角、39 材质，云占 109030 三角。新增内容后总规模高于旧版；这些是资产统计，不能据此声称游戏帧率提升。

当前导航 3533 顶点 / 3655 三角，低于 A* 4095 顶点限制。独立几何回归覆盖 12 种人为破坏；实际布景重放与 2303 株净空检查通过，最近铺路净空约 0.797 米，9 株月光蘑菇距岛面至少 4.807 米、距桥面约 4.02 米，160 晶体跨材质重复面为 0。最终资源包进入 Unity PlayMode 后，49 个玩法点的地面/胶囊与 3655 个导航面中心射线通过。九张 Unity 作者预览已目检；Windows Dev 编译与天空岛相关守卫通过，资源已部署至本机 Mod 目录。资源包 54310321 字节，SHA-256 `ebf6f76758515d03551a058845b13e192c17a29c3aff1cbb9e359a24ab137afc`。

验证证据在 `ArtSource/SkyIsland/Validation/`，日志为 `Build/sky_island_enhancement_blender.log`、`sky_island_enhancement_unity.log`、`sky_island_enhancement_physics.log`、`sky_island_enhancement_compile.log`。本轮没有进入 Duckov 验证玩家移动、战斗、镜头和帧率；Unity 作者验证不等于游戏实机 smoke。此前模型交付中的旧统计由本次记录取代，任务/存档/正式地图入口仍沿用原场景探索范围。

## 实际预览

这些图片来自本次真实 Unity 模型与 shader，不是概念图。旧版对照保存在 `Previews/BeforeEnhancement/`。

- [村庄晴昼](Previews/SkyIsland_Unity_Village.webp)
- [村庄细节](Previews/SkyIsland_Unity_Village_Detail.webp)
- [暮色](Previews/SkyIsland_Unity_Village_Dusk.webp)、[星夜](Previews/SkyIsland_Unity_Village_Night.webp)、[晨光](Previews/SkyIsland_Unity_Village_Morning.webp)
- [桥路与云海](Previews/SkyIsland_Unity_Bridges_Clouds.webp)
- [悬根林](Previews/SkyIsland_Unity_RootForest.webp)
- [镜水寺](Previews/SkyIsland_Unity_Temple.webp)
- [群岛全景](Previews/SkyIsland_Unity_Overview.webp)

## 制作入口

作者工程位于 `D:/code/ykf/duckov_modding-main/UnityFiles/BossRush`。`Assets/SkyIsland/SkyIslandAuthoring.unity` 是可见编辑场景，`ArtSource/SkyIsland/SkyIslandWorld.blend` 是可编辑 Blender 源文件，`Assets/SkyIsland/SkyIslandWorld.fbx` 是导出模型。

先运行仓库 `tools/sky_island_navigation.py` 更新共同几何，再用 Blender 后台运行 `tools/generate_sky_island.py -- --project <作者工程> --skip-render`，最后在 Unity 调用 `BossRush.SkyIslandBundleBuilder.BuildResourcesAndExit`（`BuildAndExit` 附带作者预览，在作者工程内会失败并跳过 world 包）。真实包验证入口为 `BossRush.SkyIslandBundleBuilder.ValidatePhysicsAndExit`。菜单 **Build → Sky Island → Open Authoring Preview** 可直接打开当前作者场景。

免费素材的官方页面：[Kenney Nature Kit](https://kenney.nl/assets/nature-kit)；本地[许可证与来源](ThirdParty/KenneyNatureKit/README.md)。本轮没有使用收费素材。

游戏重启后，可从基地使用 **F3 → 场景调试 → 天空岛 · 晴岚群岛 → 从基地前往天空岛** 查看新资源；巡览、光色切换和返航入口继续可用；地图已改用官方地图键（原自绘简图已删除）。
