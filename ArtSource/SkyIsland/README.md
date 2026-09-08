# 晴岚群岛作者资源

分类：COMPAT（新增独立场景资源）；OPERATIONAL（新增本地资源构建入口）。

可编辑 Blender、FBX、Unity 场景位于作者工程 `D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/ArtSource/SkyIsland` 与 `Assets/SkyIsland`。本目录保留可重复生成的数据、报告与制作记录，生成器为 `tools/generate_sky_island.py`，布景与免费素材适配分别为 `tools/sky_island_dressing.py` / `tools/sky_island_nature_assets.py`。完整交付说明见 [实际交付与验收](../../docs/制作教程/天空岛/天空岛_实际交付与验收.md)。

贴图由内置 imagegen 工具生成并复制到作者工程 `Assets/SkyIsland/Textures/`，不引用用户目录里的生成缓存。六张图为归航壁画、风纹织物、石面、草地、木板与鱼鳞瓦，均用于实际模型 UV 材质。完整提示词见 [image_prompts.txt](image_prompts.txt)。

最新的[增景与桥路优化记录](ENHANCEMENT.md)包含真实预览、免费素材许可和验证证据。场景新增粉白树冠、花草、悬藤、晶簇、月光蘑菇与祈愿灯，桥路和桥头铺装同步平滑，云海按层次重新制作。

最终场景是 12 座可达岛、15 条桥路；另有 10 个独立 FBX/Prefab 模型与 `SkyIsland_ModelKit.blend`。正式游戏包为 `Assets/arenas/sky_island_raid`，真实 Scene 是 `Assets/SkyIsland/SkyIslandRaid.unity`，已复制至本机游戏 Mod 目录。它经官方 `SceneLoader` 进入独立出击，创建自己的官方服务和玩家，基地场景会卸载。**这是唯一会被运行时加载的包**。

旧的 `sky_island_world`（原型期 additive 资源 Scene `SkyIslandWorld.unity`）已于 2026-09-08 废弃：运行时不再有任何加载路径，`SkyIslandSceneLease` 与构建脚本的部署块一并删除。它与 `sky_island_raid` 共用同一套美术几何，保留一份本机副本仅作历史对照，不再部署给玩家（省约 54.3 MB）。

bundle 不入 git（`.gitignore` 的 `/Assets/*`）。重建入口见 `tools/generate_sky_island.py` 与作者 Unity 工程的 `Assets/Editor/SkyIslandRaidBuilder.cs`；作者导出 / 仓库源 / 游戏副本三份 SHA-256 记录在 `Validation/raid_deployment_hashes.json`。

作者构建入口是 `D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/Assets/Editor/SkyIslandRaidBuilder.cs` 的 `BossRush.SkyIslandRaidBuilder.BuildAndExit`；输出目录为作者工程 `SkyIslandRaidExport`。作者工程的旧游戏 DLL 缺失字段由运行时激活前补全，必需官方组件仍严格验证；装配细节见 [独立官方场景合同](OFFICIAL_SCENE_CONTRACT.md)。

- [Unity 晴昼预览](Previews/SkyIsland_Unity_Village.webp)
- [Unity 晨光预览](Previews/SkyIsland_Unity_Village_Morning.webp)
- [Unity 暮色预览](Previews/SkyIsland_Unity_Village_Dusk.webp)
- [Unity 星夜预览](Previews/SkyIsland_Unity_Village_Night.webp)
- [独立模型库预览](Previews/SkyIsland_ModelKit_Preview.webp)
- [资源包物理验证](Validation/sky_island_physics_validation.json)
- [部署文件指纹](Validation/deployment_hashes.json)
- [独立出击包构建验证](Validation/sky_island_raid_validation.json)
- [独立出击包部署指纹](Validation/raid_deployment_hashes.json)

原美术资源包的 Unity PlayMode 从真实 bundle 加载场景，49 个玩法点地面/胶囊检查、3655 个导航三角形的地面射线通过。Unity 场景预览和 Blender 渲染仅证明作者资源的视觉结果；游戏中的移动、敌人寻路、晨光/晴昼/暮色/星夜四档光色和退出恢复需要独立实机验证。最低硬件与游戏帧率未验收。

2026-09-08 独立出击包在 Unity 编辑器严格构建与资源包路径回载校验通过，54,310,435 字节；作者、仓库与游戏 Mod 三份 SHA256 一致。该次构建报告只证明组件装配和资源路径，`gameSmoke` 记录 `not_run_per_user_request`（按用户要求未进入游戏测试）。真实玩家用例登记在 `Assets/Data/GameplayCoverage.json` 的 `SKY_ISLAND` 项，覆盖往返重进、故事与居民、死亡墓碑和加载取消，未执行用例不能标为 PASS。
