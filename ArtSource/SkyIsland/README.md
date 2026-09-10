# 晴岚群岛作者资源

2026-09-09 生活布景已交付：新增 14 类原创模型，28 个实例分布于 12 岛；独立模型库现为 24 个 FBX/Prefab。房屋轮廓、门窗、农田、补给箱与路侧植被同步细化，并补齐 77 组道具周边花草。最新制作链、参考来源与预览见 [生活布景交付](SETTLEMENT.md)。下文 2026-09-08 的包大小和物理统计保留为历史记录。

最新正式包为 59,469,605 字节，作者、仓库与游戏 Mod 三份指纹一致。Windows Release 编译、11 项天空岛守卫和作者 PlayMode 的 49 个玩法点 / 4215 个导航三角形地面检查通过。已更新本机 Mod 资源；按此前要求未启动游戏，游戏内表现与帧率未实测。

分类：COMPAT（新增独立场景资源）；OPERATIONAL（新增本地资源构建入口）。

可编辑 Blender、FBX、Unity 场景位于作者工程 `D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/ArtSource/SkyIsland` 与 `Assets/SkyIsland`。本目录保留可重复生成的数据、报告与制作记录，生成器为 `tools/generate_sky_island.py`，布景与免费素材适配分别为 `tools/sky_island_dressing.py` / `tools/sky_island_nature_assets.py`。完整交付说明见 [实际交付与验收](../../docs/制作教程/天空岛/天空岛_实际交付与验收.md)。

贴图由内置 imagegen 工具生成并复制到作者工程 `Assets/SkyIsland/Textures/`，不引用用户目录里的生成缓存。六张图为归航壁画、风纹织物、石面、草地、木板与鱼鳞瓦，均用于实际模型 UV 材质。完整提示词见 [image_prompts.txt](image_prompts.txt)。

最新的[增景与桥路优化记录](ENHANCEMENT.md)包含真实预览、免费素材许可和验证证据。场景新增粉白树冠、花草、悬藤、晶簇、月光蘑菇与祈愿灯，桥路和桥头铺装同步平滑，云海按层次重新制作。

2026-09-09 的[画风对齐原版记录](VANILLA_GRADE.md)把材质语言归位到从游戏本体实测的原版色带（暖砂岩、赤陶瓦、黄绿草），奇幻感改由光照与自发光承担，并记录了光色/天空色重复硬编码在四处的同步陷阱。该轮不改可行走几何与导航。

同日的 [Tripo3D 生成参数](TRIPO_SETTINGS.md) 记录逐件 `face_limit`、贴图分辨率分档与 Tripo 侧设置；完整链路（原版调色板实测方法、AI 建模输入图、Blender 规范化、材质与摆放接入、构建验证闸门）见[Tripo3D 建模接入与画风对齐教程](../../docs/制作教程/天空岛_Tripo3D建模接入与画风对齐教程.md)。

最终场景是 12 座可达岛、15 条桥路；另有 10 个独立 FBX/Prefab 模型与 `SkyIsland_ModelKit.blend`。正式游戏包为 `Assets/arenas/sky_island_raid`，真实 Scene 是 `Assets/SkyIsland/SkyIslandRaid.unity`，已复制至本机游戏 Mod 目录。它经官方 `SceneLoader` 进入独立出击，创建自己的官方服务和玩家，基地场景会卸载。**这是唯一会被运行时加载的包**。

旧的 `sky_island_world`（原型期 additive 资源 Scene `SkyIslandWorld.unity`）已于 2026-09-08 废弃：运行时不再有任何加载路径，`SkyIslandSceneLease` 与构建脚本的部署块一并删除。它与 `sky_island_raid` 共用同一套美术几何，保留一份本机副本仅作历史对照，不再部署给玩家（省约 54.3 MB）。

bundle 不入 git（`.gitignore` 的 `/Assets/*`）。重建入口见 `tools/generate_sky_island.py` 与作者 Unity 工程的 `Assets/Editor/SkyIslandRaidBuilder.cs`；作者导出 / 仓库源 / 游戏副本三份 SHA-256 记录在 `Validation/raid_deployment_hashes.json`。

作者构建入口是 `D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/Assets/Editor/SkyIslandRaidBuilder.cs` 的 `BossRush.SkyIslandRaidBuilder.BuildAndExit`；输出目录为作者工程 `SkyIslandRaidExport`。作者工程的旧游戏 DLL 缺失字段由运行时激活前补全，必需官方组件仍严格验证；装配细节见 [独立官方场景合同](OFFICIAL_SCENE_CONTRACT.md)。

**打包前提：作者工程必须处于 URP 之下**（CR-2026-09-10-004）。`ProjectSettings/GraphicsSettings.asset` 的
`m_CustomRenderPipeline` 必须指向一个真实存在的 `UniversalRenderPipelineAsset`
（当前指向工程内的 `Assets/SkyIsland/SkyIslandPreviewPipeline.asset`），并且
`Assets/UniversalRenderPipelineGlobalSettings.asset` 的 `m_StripUnusedVariants` 为 0。
否则 URP 的变体剥离会把 `BossRush/SkyIsland/{Environment,Water,Cloud}` 的变体**全部删光**：
包照常构建成功、能加载、能读回场景路径，但玩家一进岛就是
`Shader.isSupported == false`，`SkyIslandRendering.Apply` 当场抛错。
**每次重打包后、部署前必须跑**：

```
python tools/verify_sky_island_bundle_shaders.py
```

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
