# 自定义地图接入官方 M 键地图（含分区「未探索」灰显）

> 2026-09-09 在天空岛（晴岚群岛）上实际走通并留档。本文写的是**已验证的做法**，
> 不是设想；每一条坑都对应一次真实的失败或一次代码级核对。
>
> 适用范围：任何由 Mod 提供的 AssetBundle 独立场景（天空岛、石堡前哨、试验场……）。

## 0. 一句话结论

**官方地图系统是纯场景数据驱动的，接一张新图不需要写任何运行时代码**——
往场景里放一个 `MiniMapSettings` 组件、配一张烘焙好的俯视图就行。

Mod 侧曾经因为不知道这件事，自绘了一整套 F6 地图界面（315 行），
而官方地图键在那张图上其实是**死键**。这是最贵的一个教训：动手写 UI 之前，
先确认官方那套到底缺什么。

## 1. 官方地图是怎么工作的

反编译核对过的调用链（`TeamSoda.Duckov.Core.dll`）：

| 环节 | 代码 |
| --- | --- |
| 玩家按地图键 | `CharacterInputControl.OnUIMapInput` → `MiniMapView.Show()`，再按一次 `Close()` |
| 能不能开 | `MiniMapView.Show()`：`if (Instance != null && MiniMapSettings.Instance != null) Instance.Open();` |
| 开图时装配 | `MiniMapView.OnOpen()` → `display.AutoSetup()` → `FindAnyObjectByType<MiniMapSettings>()` → `Setup(provider)` |
| 每条图层 | `MiniMapDisplayEntry.Setup`：`sizeDelta = Vector2.one * sprite.texture.width * PixelSize`；`anchoredPosition = cur.Offset`；`if (cur.Hide) showGraphics = false;` |
| 图名/描述 | `MultiSceneCore.Instance.SceneInfo` 的 `DisplayName` / `Description` |
| 世界坐标换算 | `MiniMapDisplay.TryConvertWorldToMinimap(worldPos, sceneID, …)`，按 **sceneID 字符串**找条目 |

三个直接可用的结论：

1. **场景里没有 `MiniMapSettings`，地图键就是死键**（`Show()` 第一句就早返），不是"打开一张空白图"。
2. **`OnOpen()` 每次都重跑 `AutoSetup()`**，所以运行时改 `maps` 里的字段（比如 `hide`）**下次开图即刻生效**，不需要通知谁。
3. **`PixelSize = imageWorldSize / sprite.texture.width`**，而尺寸用 `Vector2.one * width`——
   所以**每张贴图必须是正方形**，且每条 `MapEntry` 的 `imageWorldSize` 要按自己那张的边长算。

### `MiniMapSettings.MapEntry` 的字段

全部是 public 字段，Editor 脚本直接赋值即可，不用反射：

```csharp
public float imageWorldSize;      // 这张图覆盖的世界边长（米）
public string sceneID;            // 必须等于地图身份 id
public Sprite sprite;
public SpriteRenderer offsetReference;  // 读它的 localPosition 当 Offset（世界 XZ 偏移）
public Vector3 mapWorldCenter;    // 世界→地图换算的中心
public bool hide;                 // true = 这条不画
public bool noSignal;
```

## 2. 完整流程（天空岛实例）

### 2.1 离线烘焙贴图

`tools/build_sky_island_minimap.py`，从 `ArtSource/SkyIsland/layout.json` 的**地面三角**光栅化。

**为什么不在 Unity 里架相机渲染**：离线烘焙确定性、可被守卫逐字节 sha256 钉住、
不依赖 URP 与游戏 DLL，而且用的就是导航面同一份权威几何——
地图轮廓和玩家实际能走的地面**必然**一致，不会出现"图上有路、走不过去"。

产物：

- `sky_island_minimap.png`：底图，全岛**暗灰剪影**，始终显示（就是"未探索"的样子）
- `sky_island_minimap_<区域>.png` × 12：分区**彩色**图层，裁到各自的**方形**包围盒
- `Validation/sky_island_minimap.json`：世界范围、中心、每层的 `imageWorldSize` / `offset` / sha256

桥面同时算进**两端**岛屿的图层，任一端去过就跟着上色，避免"两头亮着、中间断开"。

### 2.2 场景构建器装配

`Assets/Editor/SkyIslandRaidBuilder.cs` 的 `BuildMiniMap()`：读元数据 → 导入贴图为 Sprite →
建 13 条 `MapEntry`（1 底图 + 12 分区）→ 挂到关卡根节点下。

分区图层的偏移经 `offsetReference`：建一个空物体、挂 `SpriteRenderer`、
`localPosition = (offsetX, offsetZ, 0)`。`MiniMapSettings.Awake` 会把这些物体 `SetActive(false)`，
它们纯粹当数据用。

### 2.3 重打包

本机免费许可证**不能加 `-batchmode` / `-nographics`**，用普通 Editor：

```powershell
$unityProject = 'D:\code\ykf\duckov_modding-main\UnityFiles\BossRush'
$unityExe = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe'
$logPath = Join-Path $unityProject 'SkyIslandRaidExport\skyisland_raid_build.log'
$a = @("-projectPath `"$unityProject`"", "-executeMethod BossRush.SkyIslandRaidBuilder.BuildAndExit", "-logFile `"$logPath`"") -join ' '
Start-Process -FilePath $unityExe -ArgumentList $a -PassThru -Wait
```

然后把 `SkyIslandRaidExport/sky_island_raid` 拷进仓库 `Assets/arenas/`，
`compile_official.bat` 的部署段会送进游戏目录。

### 2.4 运行时只做一件事

`DebugAndTools/SkyIsland/SkyIslandMapFog.cs`：按存档里的 `visitedRegions` 逐个翻 `hide`。
装配时刷一次（老档进来时已到访的区域直接是彩色的），之后每新到访一个区域再刷一次。

图层靠 **sprite 名字**认领（`sky_island_minimap_<区域>`），**不靠列表下标**——
下标会随烘焙脚本的输出顺序悄悄漂移，名字不会。

## 3. 踩过的坑（每条都真的踩过）

### 3.1 `combinedSprite` 必须留空

设了它，`MiniMapDisplay.Setup` 里 `flag = true`，per-map 条目会被传 `showGraphics: false`
而**不显示**，只显示 combined 条目；而 `SetupCombined` 会把 `sceneID` 置成空串，
于是 `TryConvertWorldToMinimap(worldPos, sceneID, …)` 按 sceneID 再也找不到条目，
玩家点位换算失效。**单图场景一律留空。**

### 3.2 不要放 `MiniMapCenter`

`MiniMapCenter.CacheThisCenter()` → `MiniMapSettings.Cache(this)` 会用自己的 transform
**覆盖** `mapWorldCenter`，和离线烘焙算出来的中心打架。直接在 `MapEntry` 里写死中心即可。

### 3.3 `GetSceneID(int)` 对 bundle 场景返回 null

官方的「居中到玩家」和无参坐标换算走：

```csharp
SceneInfoCollection.GetSceneID(SceneManager.GetActiveScene().buildIndex)
```

而 `InstanceGetSceneID(int)` 是在**全局 `entries`** 里找 `SceneReference.BuildIndex == buildIndex`
且 `UnsafeReason == None` 的条目。bundle 场景的 buildIndex 恒为 **-1**，
自定义场景又不该注入全局 entries（会和所有其它 -1 场景撞车），所以原实现只会返回 null。

解法：在场景引用桥里加一个 Harmony prefix，**按「当前活动场景是不是这张图」判定**：

```csharp
private static bool SceneIdByBuildIndexPrefix(int buildIndex, ref string __result)
{
    if (!registered || buildIndex >= 0 || !IsActiveScene) return true;
    __result = SceneId;
    return false;
}
```

**绝不能写成「buildIndex == -1 就返回自己」**——那是全局 -1 别名，会把所有自定义 bundle
场景一起匹配掉。守卫和隔离回归两侧都钉了这条：站在图上要解析得出，切到别的场景必须变回 null。

顺带一提，其余那条链早就是通的：`MapEntry.SceneReference` 依赖
`SceneInfoCollection.GetSceneInfo(sceneID)`（桥已经 patch），`UnsafeReason` 也已被桥
按 GUID 收窄成 `None`，所以 `MiniMapCenter.GetCenter(sceneID)` 能正常落到我们的条目上。

### 3.4 贴图必须是正方形

见第 1 节：`sizeDelta = Vector2.one * texture.width * PixelSize` 只取 width。
非正方形会让 X/Z 比例失真，玩家点位横竖偏移量不一致。

### 3.5 守卫的子串误伤

新加的 `SkyIslandMapFog` 含有子串 `SkyIslandMap`，把「不得重建自绘地图」那条守卫误判成失败。
断言要写 `new SkyIslandMap(` 这种真正的构造调用，不要裸类型名。
（这是本仓库反复出现的老问题，见 `AGENTS.md` 4.10。）

### 3.6 分区图层要「失败开放」，不能失败关闭

第一版把分区图层的初始 `hide` 设成 `true`，指望运行时按存档放开。这是**失败关闭**：
只要运行时认领图层失败（找不到 `MiniMapSettings`、sprite 命名改了、烘焙漏了一层……），
整张地图就永远只剩暗灰底图，等于地图废掉，而且不会报错。

改成初始 `hide = false`，由运行时把**没去过的**关掉。最坏情况退化成「全岛可见、没有迷雾」，
地图本身仍然可用。相应地，运行时那次初始 `Apply` 要放在装配**最前面**
（场景与相机一就绪就调），免得玩家刚落地时看到满图彩色。

顺带一提，`MiniMapSettings.Instance` 是个**从不清空**的静态
（只有 `Awake` 里 `if (Instance == null) Instance = this;`）。认领图层别只认它，
按「谁身上带着本图的分区贴图」找一遍更稳。

### 3.7 守卫依赖的素材必须纳管

`ArtSource/SkyIsland/*` 默认整个 local-only。烘焙出来的 PNG 放在那儿、
守卫又要读它们比 sha256 —— 结果就是「本地一直绿、fresh clone 必红」。
`.gitignore` 里那段注释本来就写着「新增此类 guard 依赖的素材数据时必须同步加到这里」，
照做即可（这批贴图合计约 52 KB）。

### 3.8 测试文件名要能被 runner 发现

`tools/run_guards.py` 只收 `*Guard.py` / `*PropertyTest.py` / `*Tests.py`。
对齐测试一开始叫 `...AlignmentTest.py`（单数 Test），文件在、手跑能过，
但**从来不进套件**——等于白写。命名前先看一眼 runner 的过滤条件。

### 3.9 死断言：写完必须反向验证

守卫里一度写成 `if layer.get('textureSize') != layer.get('textureSize')` ——
自己跟自己比，恒 False，**永远不会触发**。它和周围真断言混在一起，看起来一切正常，
套件也一直绿。

本仓库要求每条新断言都做一次反向验证（故意改坏 → 必须转红 → 按字节还原），
就是为了逮住这一类：不只是「断言能被绕过」（见 3.5），还有「断言根本不成立」。

## 4. 怎么在**不进游戏**的前提下验证对位

贴图对不对位，本来只能进游戏开一次地图才知道。现在有
`tests/SkyIslandMiniMapLayerAlignmentPropertyTest.py`：按官方那三行公式
（`sizeDelta` / `anchoredPosition` / `PixelSize`）把 12 张分区贴图贴回底图坐标系，
再和「一次性画出的全彩参考图」逐像素比对。

实测 **0.000% 错位**——`imageWorldSize` 与 `offset` 这两组数就此从"要靠实机确认"
变成了离线可证。烘焙脚本的投影或裁剪一旦改坏，这个测试立刻转红。

配套守卫 `tests/SkyIslandMiniMapGuard.py` 另外钉住：贴图与元数据 sha256 一致、
贴图实际宽高与元数据自洽且必须正方形（直接读 PNG 头，不依赖 Pillow）、
覆盖范围罩得住地面网格、构建器确实装配了 `MiniMapSettings`、
分区图层**默认可见**（见 3.6）、自绘地图不得复活、Mod 侧不得再处理地图输入、
以及烘焙贴图必须在 `.gitignore` 里放行（见 3.7）。

## 5. 换一张新图要做什么

1. 写一个烘焙脚本，从你那张图的权威几何产出正方形俯视图 + 元数据（照抄
   `tools/build_sky_island_minimap.py`，主要改投影范围和配色）。
2. 在场景构建器里加 `BuildMiniMap()`，注意 3.1 / 3.2。
3. 如果是 bundle 场景，确认场景引用桥有 3.3 那个 prefix。
4. 重打包、拷进 `Assets/arenas/`。
5. 抄一份对齐测试和守卫（注意 3.6 / 3.7 / 3.8 三条）。
6. **进游戏开一次地图**：确认白点落在你站的那个岛上，南北没反。
   偏了就是烘焙脚本投影那几行的事，改完重跑脚本 + 重打包，不用动 C#。

## 6. 相关文件

| 用途 | 路径 |
| --- | --- |
| 烘焙脚本 | `tools/build_sky_island_minimap.py` |
| 场景构建器 | `<Unity 工程>/Assets/Editor/SkyIslandRaidBuilder.cs` |
| 运行时分区迷雾 | `DebugAndTools/SkyIsland/SkyIslandMapFog.cs` |
| 场景引用桥 | `DebugAndTools/SkyIsland/SkyIslandSceneReferenceBridge.cs` |
| 离线对齐测试 | `tests/SkyIslandMiniMapLayerAlignmentPropertyTest.py` |
| 守卫 | `tests/SkyIslandMiniMapGuard.py` |
| 场景合同 | `ArtSource/SkyIsland/OFFICIAL_SCENE_CONTRACT.md` |
| 打包环境说明 | `docs/制作教程/AI图片生成与Unity自动打包流程.md` |
