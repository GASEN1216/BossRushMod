# 天空岛最终生产审计

2026-09-14。离线制作、三轮全场景迭代、Unity 作者工程验证和本地场景资源交付已完成。最终资源尚未做真人游戏验收：用户最新要求为“暂时不进行游戏内实测”，因此本报告不标记完整 Production Ready，也不宣称游戏已达到 60 FPS。

分类：**COMPAT**（美术与兼容修复）、**SCHEMA+**（仅作者导出元数据的可选记录，不涉及玩家存档）、**OPERATIONAL**（作者依赖修正、本地资源构建和复制）。本次没有提交或推送代码。

## Initial State

实际作者工程为 `D:/code/ykf/duckov_modding-main/UnityFiles/BossRush`，Unity 2022.3.62f3，Blender 5.2.1。作者 URP 依赖按当前 Unity 版本收敛到 14.0.12；最终验证覆盖 Forward 和游戏使用的 Deferred 路径。运行时入口加载 `Assets/arenas/sky_island_raid`。

原场景的岛群、桥网、赤陶屋顶、橄榄绿植被和居民陈设已有明确方向。问题集中在破片状岛底、部分 UV 拆点后减面造成的裂口、树干截面翻轴、道路中央装饰、陈设穿插以及不可重复的随机朝向。用户提供的游戏截图用于定位 A 岛道路灌木；截图中的约 30 FPS 没有作为基准测试。

已保存作者 ArtSource、Assets/SkyIsland、编辑器脚本、Packages、ProjectSettings 和原工具副本，并记录 SHA。原场景资源另存可回退副本，见 [备份目录](D:/code/ykf/BossRushMod/Build/sky-production-20260914/backup)。存档备份仅为只读复制，不作为测试通过证据。

| 同口径指标 | 原始基线 | 最终 |
| --- | ---: | ---: |
| Unity 导入可见 Renderer | 777 | 721 |
| Unity 导入可见三角面 | 1,636,738 | 1,559,387 |
| Unity 材质 | 110 | 99 |
| Blender 可见三角面，含原退化面 | 1,674,132 | 1,559,387 |
| Blender 可见零面积三角面 | 39,155 | 0 |
| 出击资源包字节 | 118,636,309 | 105,408,139 |

Unity 三角面减少 4.73%，包体减少 11.15%。Blender 基线包含后来导出或导入时舍弃的退化面，不能与 Unity 的基线混算。

## Art Direction Analysis

保留暖砂灰岩、赤陶屋瓦、橄榄与金黄叶色、少量粉花和青蓝风晶。村庄维持近景密度，道路与战斗广场保留留白；钟庭、金环、巨树承担主地标。岛群外形受既有地表与导航契约约束，未为了概念图改动岛形、桥位和玩法落点。

岛底从碎片拼接改为连续主壳和局部宽扶壁，层次由大形、错肩和岩层材质共同表达。成熟房屋、灯具、摊位和纪念物保留原设计细节。曾制作的简化道具候选因细节与角色弱于原件被拒绝，未接入生产。

## Native Astra Generated References

六张新图均使用本任务的原生 image generation 工具生成，提示词与结果保存在 [References](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/References)。未调用 `docs/` 下的生图接口或服务。

| 参考 | 用途与实际采用范围 |
| --- | --- |
| [A：英雄视角](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/References/A_hero_native.png) | 岛底体积、云海与主次关系。 |
| [B：鸟瞰](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/References/B_birdseye_native.png) | 布局阅读与色彩；生成图的海面背景不适用，未采用。 |
| [C：游玩视角](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/References/C_gameplay_native.png) | 主路净空、近景层次与地标辨识。 |
| [D：建筑](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/References/D_architecture_native.png) | 屋面、墙体与木构细节关系。 |
| [E：地质](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/References/E_geology_native.png) | 闭合岩壳、宽扶壁和断肩。 |
| [F：砂岩纹理](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/References/F_sandstone_native.png) | 实际用于 GeologySand / Light / Deep，经过材质乘色与 32 米投影适配。 |

参考图用于判断形体和材质，不作为模型、Unity 效果或实机性能证据。

## Assets Inspected

全量覆盖 71 个规范化源模型、24 件独立模型套件和整岛可见合批。保留原始 **848 行**追踪表，最终当前表为 **792 行**：71 个源模型加 721 个可见合批。两表区分本轮动作、当前验收状态和是否仍有待办，并记录源指纹、实际实例和未放置状态。

- [原始 848 行处置表](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/Final/Blender/asset_disposition_baseline848.csv)
- [当前 792 行处置表](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/Final/Blender/asset_disposition_current.csv)
- [逐对象网格审计](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/Final/Blender/scene_audit.json)
- [独立最终美术审查](D:/code/ykf/BossRushMod/Build/sky-production-20260914/round03/blender/final_review.md)

源模型动作统计为 51 KEEP、6 REFINE、11 REBUILD、3 REPLACE。处置统计包含程序覆盖和材质调整，不等于相同数量的源 JSON 被覆盖。

## Assets Kept

保留已有建筑群、桥、巨树、钟庭、主要装置、路灯、摊位、箱桶和多数小件的成熟造型。`great_tree` 的原 GLB 与当前 8,831 面源模型一致，没有经过造成裂口的减面；叶冠开放片按设计用途保留，修缝候选因没有实际改善而拒绝。

`crystal_fountain` 当前没有实例。它的青蓝实体晶体不能按颜色当作流水，最终保留原静态材质，未把未放置模型记成场景中已修好的喷泉。

## Assets Refined

`cave_rock / rock_b / glow_crystal / tea_house / village_house_a` 五件从真实原 GLB 修复同位置分裂拓扑，再按原预算减面；图集保持，实际包络全部位于原声明内。茶屋和民居的瓦脊、屋面裂口在正背近景对照中明显收拢，门脸、灯笼、烟囱及原屋顶轮廓保留。

第六件为 `waterfall`：原 JSON 保留，实际三处瀑布分成水、石两组，保留每个实例整件的面角法线。见 [五件修复源](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/RefinedSources)、[岩石修复证据](D:/code/ykf/BossRushMod/Build/sky-production-20260914/surface_repair/contract/final_review.json)、[屋顶对照与审图](D:/code/ykf/BossRushMod/Build/sky-production-20260914/surface_repair/roof/review.md)。

## Assets Rebuilt

重建 12 个闭合主岩壳和 36 块宽 / 长 / 窄扶壁，共 13,200 面。顶圈精确保留，新增岩体全部位于可玩地表下方；扶壁与主壳只有登记的局部咬合，没有复制完整内壳。

独立程序库覆盖 11 类：四种常规树、cherry_tree、bush_a/b、cliff_shrub_cap、fern_clump、coral_clump、stone_bench。其中 10 类实际进入 World；cliff_shrub_cap 当前未放置。原 JSON 库均保留。真实最终网格匹配确认 4 张石凳和 4 棵 cherry_tree 已接入，未只停留在候选库。

## Assets Replaced / Removed

旧 `cliff_chunk_a/b/c` 退出近景岛体，由闭合岩壳取代，旧源保留。根据实际包络和同源道路规则抑制 126 条侵入道路的装饰植被；未触及登记碰撞树，也未通过 fallback 又画回旧球或旧模型。相对原基线，138 个旧可见合批退役、82 个新合批补入，所有变化都在全清单中追踪。

## Modeling Improvements

几何处理新增 float32 精度的退化面检查，清除球极点、零半径截面及共线细叶产生的空面，不焊 UV、不批量删除正反双面花叶。三轮可见零面积面依次为 **443 → 0 → 0**。

树干使用连续传递的截面，消除近竖直分支突然翻轴形成的针腰。实体叶冠区分主簇、副簇与下部阴影体积；石凳有闭合座板、支撑和两脚。实际全场景复查没有发现需要阻断美术交付的 Critical / High 问题。

## UV Improvements

地质表面采用 32 米周期，侧面 V 沿世界高度对齐，接近水平的断面才切到 XZ 投影，避免旧主轴投影在斜面跨 45° 时产生纹理方向跳变。最终实际 FBX 投影复算最大误差约 0.00000144 UV。

五件图集源的修复先保持面角位置与图集坐标关系，再重新减面；不是重新绘制原图集，也不宣称减面前后所有三角相同。最终贴图网格缺 UV、非有限 UV 和贴图 UV 零面积面均为 0。

## Material Improvements

新增三档砂岩材质并按原场景暖灰色带调色。F 原生 PNG 与作者纹理 SHA 一致，Unity 三个材质的 BaseMap 均回读指向该纹理。Blender 乘色节点没有被直接作为图片节点嵌入 FBX；Unity 构建器通过材质名和 metadata 正确绑定，不能把“运行时绑定成功”写成“FBX 内嵌贴图成功”。

瀑布石壁不再使用流水材质。三个实际实例的六个最终网格保留 custom split normals：每处石组 3,562 面、水组 160 面，面和 UV 对应不变；相对原整件最大法线偏差约 **0.106°**，旧拆组产生的 >5° 残余归零。见 [最终 FBX 验证](D:/code/ykf/BossRushMod/Build/sky-production-20260914/round03/geology-final-v2/final_fbx_validation.json)。

## Lighting Improvements

沿用原运行时光照与日夜控制。作者预览采用同源太阳 / 环境色设置，Blender 审图关闭云的投影以对应 Unity 的 Cloud ShadowCastingMode.Off。

近云以宽云筏为主；远云缩小、降低并靠近天空色，最终固定侧视中原来压住地标的大白墙消失。最高云顶为 -82 米，最低主岩体约 -70.985 米；云海与主岛体垂直分离约 11 米。没有新增实时体积步进、透明卡片堆叠或 CPU 云生成。

## Vegetation Improvements

道路检查使用实际植被半径和“道路半宽 + 0.9 米”边距。树只检查离地 2.2 米以下部分，避免把高处树冠误作地面阻挡。A 出生主路及北桥头已通过最终焦点图核对，周边花草、摊位和灯保留，未把村庄清空。

B 南石凳原座板与摊位侧箱有 50 对三角交叉。仅移动该张凳子约 2.5 米到邻近净空位置，保留朝向和随机序列；114 条摆放记录只有这一条位置变化。最终凳体对摊位、箱桶、花箱、路灯的真实 BVH 相交均为 0，并通过严格 PlantingSpace 检查。见 [最终焦点与净空验证](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/Final/Blender/final_focus_validation.json)。

## LOD Improvements

本轮没有新增 LODGroup 或 Billboard，也没有把减少总面数包装成 LOD 实现。当前继续使用按岛 / 材质合批与视锥剔除；材质支持 instancing，静态环境保留 BatchingStatic，云保留动态边界余量。

多数小件已经处于低面数预算，区域合批直接套 LOD 会使整片植被同时跳变。基于当前四机位成本，本轮选择修复重复、退化和不可见的错误几何，保留近景轮廓。是否增加逐资产 LOD，应由恢复实机测试后的 GPU / 距离成本决定；当前没有把 LOD 切换质量记为通过。

## Collision Improvements

确认并修复了 Python 字符串 hash 随进程变化造成的朝向和碰撞尺寸漂移。确定种子与原始 World 反解的朝向登记进入生产输入，绘制和 collision_fit 共用同一朝向函数。两次独立 PYTHONHASHSEED 对照从 21 个碰撞差异、29 个外观差异降为 0。

最终 146 个碰撞盒、82 个标记与备份逐值相同，layout 文件字节相同；最终 FBX 的 284 个 NAV / COL / Ground / Marker 保护对象逐项相同。导航维持 3,870 顶点、4,048 三角。Unity PlayMode 对 27 个地面、147 个墙 / 护栏碰撞体执行实测，49 个玩法标记胶囊净空及 4,048 个导航三角中心地面命中全部通过。

## Three Production Loops

三轮都执行完整 World 生成、11 个固定 Blender 视角、Unity 11 视角、物理检查和四机位性能采样；最终轮另有 8 张灰模 / 线框图和 4 张焦点图。第三轮中断的预审单独封存，未充当额外完成轮次。

| 轮次 | 本轮实现 | 复查后处理 |
| --- | --- | --- |
| Round 01 | 闭合岛体、实体植被、初版砂岩与网格清理 | 找到 443 个残余空面、朝向漂移、树干截面问题、道路植被和材质层次问题，继续修复。 |
| Round 02 | 稳定朝向、float32 清理、树干与道路净空、三件岩石 / 晶体修缝、云层与岩色调整 | 找到瀑布分组法线、B 南凳穿插和两屋顶裂口，继续修复。 |
| Round 03 | 保留瀑布整件法线、两屋顶修缝、石凳移位、远云收敛 | 最终源件 / FBX / Unity / 部署验证完成；实机按用户要求延期。 |

证据保存在 [完整生产工作目录](D:/code/ykf/BossRushMod/Build/sky-production-20260914)。每轮来源通过 SHA 与渲染前后校验固定。

## Unity Integration

最终 World、24 件模型套件和出击 Scene 已在真实 Unity 2022.3.62f3 中构建。套件共 56,618 面；居民陈设属性测试覆盖 14 类、28 个实例、81 个计划保护标记及 8 个破坏探针，全部通过。这里的 81 是陈设计划覆盖口径，场景总标记为 82，物理玩法标记子集为 49。

Windows `compile_official.bat` 编译通过。GAME_PATH 指向隔离编译目录，其 Managed 引用真实游戏 DLL；自动复制未作用于实际游戏。本次仅部署场景资源，没有向真实游戏复制新 BossRush.dll。天空岛相关守卫 **39 PASS / 0 FAIL**，额外陈设测试与 repowiki 引用检查通过。

最终包回读：Environment 4 passes / 93 材质，Water 3 passes / 2 材质，Cloud 3 passes / 4 材质；三类均具备 Deferred GBuffer 路径。仓库和本地 Mod 的副本再次检查通过。最终资源为 **105,408,139 字节**，作者、仓库、游戏目录 SHA 均为：

`67081298da3bd647f37263e725fa9d6a4a8354da3eba564b6204073210eb9636`

见 [交付清单](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/delivery_manifest.json)、[部署着色器复查](D:/code/ykf/BossRushMod/Build/sky-production-20260914/deployed-shaders.log)、[Windows 编译日志](D:/code/ykf/BossRushMod/Build/sky-production-20260914/windows-compile.log)。

## Performance Before

机器为 i5-13500H、Intel Iris Xe、约 32 GB 内存。采样使用同一份 Profiler 脚本、1920 × 1080 RenderTexture、D3D11、关闭垂直同步，固定出生点 / B / F / H 四机位。每机位预热 2 秒，测量至少 5 秒、至少 180 帧。该测试是 **Unity Editor PlayMode 的场景诊断**，不包含游戏 AI、交互、存档、真实游戏相机与宿主渲染功能。

最初基线的 Deferred 平均 / P95 毫秒分别为：出生点 5.65 / 6.76，B 8.26 / 9.60，F 7.60 / 8.96，H 4.03 / 4.97。最终首次采样出现大幅波动，不能直接拿不同机器负载窗口宣称模型性能变化。

因此又连续执行“旧版 Deferred → 最终 Deferred → 旧版 Deferred”，另做旧版 / 最终版 Forward 配对，并按秒记录进程 CPU。旧版两次也出现尾部波动；以下使用同一复测窗口的数字，原异常采样与最初基线都保留，没有删除不利结果。

## Performance After

| 机位 | 旧版 Deferred 平均范围 ms | 最终平均 ms | 最终 P95 ms | 绘制批次：旧 → 新 |
| --- | ---: | ---: | ---: | ---: |
| 出生点 | 6.87–6.99 | 7.93 | 14.30 | 183 → 210 |
| B 村庄 | 9.74–10.08 | 10.66 | 16.99 | 239 → 253 |
| F 区域 | 9.11–9.35 | 9.08 | 15.37 | 255 → 260 |
| H 钟庭 | 4.48–4.77 | 5.21 | 11.17 | 107 → 106 |

细化提高了出生点和村庄的部分绘制成本；F 的平均值接近旧版，H 批次减少。总面数和包体下降并不表示每个视角都更快。B 的 P95 为 16.99 ms，略高于 60 FPS 对应的 16.67 ms；当前不能判定严格帧率目标通过。

同窗口 Forward 旧 → 新平均毫秒为：出生点 11.47 → 13.14、B 14.84 → 15.70、F 13.95 → 14.08、H 9.88 → 10.48。GPU 独立计时不可用，不能把 unavailable 当成 0 ms；CPU 渲染线程 / Present 等未提供字段同样保留为不可用。外部负载观测只能帮助解释采样条件，不能证明所有尖峰的唯一原因。

原始数据与 CPU 采样见 [最终性能证据](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/Final/Performance)，完整前后屏幕和异常记录见 [连续复测](D:/code/ykf/BossRushMod/Build/sky-production-20260914/performance-recheck)。不据此换算或宣传实际游戏 FPS。

## Remaining Minor Issues

茶屋仍有极细的原生屋面叠片暗边，岩层横向节奏仍较规则，巨树保留风格化开放叶冠。这些已在实际近景、灰模和源件对照中审查，未发现新的贯穿裂洞或 Critical / High 问题。未放置的源库条目不能推断其未来摆放效果。

用户延期的项目包括：最终包在真实游戏中的走图、战斗、镜头遮挡、夜间 / 天气变化、流水与云动态观感、持续运行稳定性，以及受控游戏帧率。它们属于尚未执行的验收，不包装成“剩余小问题”或已通过项。

## Final Screenshots

以下为最终资源的真实 Unity 作者工程与 Blender 渲染，均不是生成参考图或真人游戏截图。

![Unity 最终建筑与街道](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/Final/Unity/09_Architecture_Close.png)

![Unity 最终侧视，地标后方远云收敛](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/Final/Unity/03_Side.png)

![Blender 最终 11 机位](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/Final/Blender/round03_11_views.jpg)

![最终出生路、桥头、石凳与屋顶焦点](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/Final/Blender/round03_focus_views.jpg)

## Final Status

**离线生产和作者工程验证完成；真人游戏验收按用户要求暂缓。** 最终未标记完整 Production Ready 或 60 FPS 通过。恢复实机验收时应针对已交付 SHA 的包执行，不沿用旧包的游戏测试记录。

- [可编辑整岛 Blender](D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/ArtSource/SkyIsland/SkyIslandWorld.blend)
- [24 件模型套件 Blender](D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/ArtSource/SkyIsland/SkyIsland_ModelKit.blend)
- [最终 World FBX](D:/code/ykf/duckov_modding-main/UnityFiles/BossRush/Assets/SkyIsland/SkyIslandWorld.fbx)
- [带审查机位的独立 Blender 快照](D:/code/ykf/BossRushMod/Build/sky-production-20260914/round03/blender/SkyIsland_ProductionAudit.blend)
- [本地出击资源包](D:/code/ykf/BossRushMod/Assets/arenas/sky_island_raid)
- [全部交付证据与资源指纹](D:/code/ykf/BossRushMod/ArtSource/SkyIsland/Production20260914/delivery_manifest.json)

最终 World FBX SHA：`de36df8f87ebde6597f73ae4bad6c210113463f34381f50c3efa58d626abd58f`。旧资源恢复入口记录在交付清单的 `previousRaidCopies[].rollbackFile`，没有删除原始源库或备份。
