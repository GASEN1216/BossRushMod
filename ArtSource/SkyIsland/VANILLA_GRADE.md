# 天空岛画风对齐原版鸭科夫

2026-09-09。分类：COMPAT（材质与光照数值调整）/ OPERATIONAL（本机重建与部署）。

本轮只改颜色与光照，**没有改动任何可行走几何、碰撞或导航**：导航顶点在改动前后同为 4037 顶点 / 4215 三角，与本轮无关。目标是让天空岛的材质语言回到原版鸭科夫，奇幻感改由光照和自发光承担。

## 依据：从游戏本体实测的原版基准

原版材质的 `_BaseColor` 几乎全为白色，颜色都在贴图里，因此查材质无效。用 UnityPy 扫 `Duckov_Data/resources.assets` 与 `sharedassets{0,1,4,5,10,31,34,42,48}.assets`，取名字以 `_C` 结尾的反照率贴图（`_M` 是全黑遮罩图，会把统计带偏），共 154 张：

| 类别 | 原版样本 | H / S / L |
| --- | --- | --- |
| 砂岩石材 | `T_Tile_Stones_C` `#A98A6F` | H28 S25 L55 |
| 草地 | `T_Tile_Grass_01_C` `#7F9154` | H78 S27 L45 |
| 木材 | `T_Tile_WallWoodBlank01_2_C` `#BF9364` | H31 S42 L57 |
| 赤陶屋顶 | `T_Tile_Roof02_2_C` `#976D49` | H28 S35 L44 |
| 砖墙 | `T_Tile_BrickWall01_2_C` `#DEC9A0` | H40 S48 L75 |

结论：原版色相锁在暖琥珀 H18–40，草地 H78–89；冷色只出现在水泥、沥青和实验室材质上。天空岛原来的岩体 `#607e83`(H189 青蓝)、草地 H100 纯绿、屋顶 `#357e7e`(H180 青瓦) 是三处最大偏差。

## 改了什么

**贴图定级**（`tools/sky_island_texture_grade.py`，可复现，原图全部保留）：色相按「围绕均值压缩扰动再平移并钳进暖色带」处理，保留每片瓦、每块石的手绘扰动与明度笔触。产出 `roof_handpainted_terracotta.png`（H156 青瓦 → H26 赤陶）、`stone_handpainted_vanilla.png`（H42→H33）、`grass_handpainted_vanilla.png`（S42→S32）、`wood_handpainted_vanilla.png`（S61→S56）。输出文件名保留 `handpainted` 子串，否则 `SkyIslandBundleBuilder` 会把导入模式设成 Clamp 而出现平铺接缝。

**调色板**（`tools/generate_sky_island.py`）：32 项 `PALETTE` 与 13 项 `TILED_TEXTURES` 按上表归位，tint 全部落在 1.0 附近。键名是贯穿 FBX 材质槽、`Sky_*.mat` 与 `geometry.json` 的稳定标识，因此只改颜色不改名——`Teal*` 现在装的是赤陶屋顶色。`PaintTeal*` 三项**刻意保留青绿漆**，作为暖色场景里唯一的高饱和点缀。

**光照**：环境光压暗约 35% 并推冷、主光提亮约 20% 并推暖、太阳高度角放低（晴昼 55°→48°），整体曝光基本持平而对比度显著提高，形成原版那种「暖光 + 深冷阴影」。夜档环境光压得最狠，让灯笼、晶簇与月光蘑菇的自发光成为夜里的主角。

**云海分层**：原本 7 朵里只有 1 朵换色，云海读成均匀白色圆点。改为按远近三层分配材质（近景暖白 → 中景中性 → 远景压向天色），新增 `CloudFar`，形成空气透视纵深。

**天空**：从 H201 S46 的高饱和青蓝改为 H205 S29，不再与暖色地面对冲。

## 四处同步的陷阱

光色与天空色**重复硬编码在四个位置**，只改一处会静默不生效：

1. `DebugAndTools/SkyIsland/SkyIslandLighting.cs` 的 `Presets[]` —— 实机
2. 作者工程 `Assets/Editor/SkyIslandBundleBuilder.cs` 的 rig、`SetAuthoringLighting()` 与 5 处 `camera.backgroundColor` —— 仅作者预览
3. `Assets/SkyIsland/Shaders/SkyIslandCloud.shader` 约 123–125 行的 `haze` 四档常量 —— **玩家实际看到的「天空」是这里**，俯视构图里占满画面的是 `CloudBackdrop` 那块 y=-160、±6000 的巨型平面，与相机背景无关
4. 两个 shader 里 `_SkyIslandLightingEnabled` 未置位时的兜底 ambient 常量

同时 `SkyIslandCloud.shader` 的 `night = 1 - smoothstep(a,b,sun.r)` 阈值要覆盖新的夜间 `sun.r`，本轮由 0.24 调到 0.30。

## 同轮的其他画面改动

**云海造型重做。** 沿用原有的 metaball 离线融球方案，只改造型参数：加平底裁剪（模拟凝结高度）、
逐层递减的花椰菜冠、三种云型按远近加权混排、融合阈值 0.88→0.96、体素精度按距离分三档、
纵向比例按云型给。云三角面 **109,030 → 81,194（降 26%）**，效果反而更好——远景放粗省下的
比近景加细花的多。材质也从「7 朵换 1 朵」改为按远近三层分配，做出空气透视纵深。

**Tripo3D 英雄道具接入。** 新增 `MODEL_TEXTURES` 通道（有贴图但不平铺、保留模型自带图集 UV，
与 `Mural`/`Cloth` 同模式），首批接入悬钟石拱门、巨树带板根、悬浮水晶喷泉三件，各带 1024² 图集。
全部作为非行走装饰，导航顶点保持 4037 未变。参数与踩坑见
[TRIPO_SETTINGS.md](TRIPO_SETTINGS.md) 与
[Tripo3D 建模接入与画风对齐教程](../../docs/制作教程/天空岛_Tripo3D建模接入与画风对齐教程.md)。

## 重建顺序

```
python tools/sky_island_texture_grade.py --project <作者工程>
blender -b --python tools/generate_sky_island.py -- --project <作者工程> --skip-render
Unity -executeMethod BossRush.SkyIslandBundleBuilder.BuildAndExit      # 材质/预制体/预览包
Unity -executeMethod BossRush.SkyIslandRaidBuilder.BuildAndExit        # 玩家实际进的 sky_island_raid
tools/build_sky_island_art_preview.ps1                                  # 预览图（作者工程内渲染会因 Umbra 类型缺失失败）
```

`SkyIslandBundleBuilder.BuildAndExit` 在作者工程内跑到渲染步骤会抛 `TypeLoadException: Umbra.UmbraSoftShadows`，材质与预制体在此之前已写盘，属已知现象；预览必须走独立宿主工程。

## 验证

下表是**调色板与光照轮次**的验证结果（守卫 573 项为当时数量）。此后的云海造型重做与
Tripo3D 接入是另外的轮次，其验证见
[Tripo3D 建模接入与画风对齐教程](../../docs/制作教程/天空岛_Tripo3D建模接入与画风对齐教程.md)
的验收清单一节。

| 项目 | 结果 |
| --- | --- |
| Windows 编译 | 零错误零警告，`BossRush.dll` 4,654,592 字节 |
| 天空岛执行回归 | 7 PASS / 0 FAIL（含 `SkyIslandLighting`） |
| 结构守卫 | 573 PASS / 0 NEW-FAIL |
| `sky_island_raid` | 59,469,601 字节，作者/仓库/游戏三份 SHA256 一致（`2d582ade6001cd4c…`） |
| 渲染回采 | 草地 `#7C8C54` H77 S25 L44（原版 H78 S27 L45）；崖壁 `#9F876A` H32 S22 L52（原版 H28 S25 L55）；铺装 `#D9C9A2` H43 S42 L75（原版 H40 S48 L75） |

**未做实机验收。** 上述全部为后台构建、离线回归与渲染像素回采；玩家移动、战斗、镜头、四档光色切换与帧率必须进游戏实测，不能据本页宣称通过。
