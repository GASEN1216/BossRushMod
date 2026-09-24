# 便携安全区装置 Unity 资源制作约定

## 本机资源制作配置

- 作者工程路径一律走 `tools/unity_project_path.py`（`BOSSRUSH_UNITY_PROJECT` 优先，默认是仓库同级的 `duckov_modding-main/UnityFiles/BossRush`），不要写死。
- 生图网关、模型与密钥的配置方式见 [AI图片生成与Unity自动打包流程.md](AI图片生成与Unity自动打包流程.md)；密钥只放本机环境变量，不写进文档、脚本或日志。
- 高清原图归档在作者工程的 `ArtSource/` 下，不放进 Mod 的 `Assets/`。

## 代码契约

| 项目 | 固定值 |
| --- | --- |
| TypeID | `500058` |
| AssetBundle 文件名 | `portable_safe_zone_device` |
| 放置目录 | `Assets/Items/portable_safe_zone_device`（无扩展名） |
| Item Prefab 名 | `BossRush_PortableSafeZoneDevice` |
| 建议图标名 | `BossRush_PortableSafeZoneDevice_Icon` |
| 加载管线 | `ItemFactory`（注册计划在 `Integration/BossRushDynamicItemRegistry.cs`，带 `FallbackLoader`） |
| 作者工程构建器 | `Assets/Editor/PortableSafeZoneDeviceBundleBuilder.cs`，菜单 `Build/Duckov Build Portable Safe Zone Device`，输出到工程旁 `PortableSafeZoneDeviceExport/`，超过 512 KiB 直接报错 |

该物品不是装备，不应放进 `Assets/Equipment/`。代码已经提供运行时兜底注册；没有 AssetBundle 时功能仍可用，但会沿用兜底物品的外观。正式资源包加载后，`PortableSafeZoneDeviceConfig` 会覆盖 TypeID、名称、品质、耐久、标签和使用行为。

## Prefab 最小结构

1. 新建空物体并命名为 `BossRush_PortableSafeZoneDevice`。
2. 挂载原版 `ItemStatsSystem.Item` 组件，TypeID 设置为 `500058`。
3. 设置一个可用的物品图标；建议使用 256×256 或 512×512、透明背景 PNG，导入类型为 Sprite (2D and UI)。
4. 不需要手动挂 `PortableSafeZoneDeviceUsage` 或 `UsageUtilities`，运行时配置器会补齐并覆盖使用行为。
5. 上面几步由构建器自动完成：它以 `Assets/mymod/items/BossRush_ZombieTideBeacon/BossRush_ZombieTideBeacon.prefab` 为模板，在
   `Assets/Items/portable_safe_zone_device/` 下生成 prefab、配置图标导入，只把 prefab 与图标打进 `portable_safe_zone_device`。
6. 把无扩展名 bundle 复制到 Mod 仓库的 `Assets/Items/`；`compile_official.bat` 与 `test_bossrush_official.bat` 都有它的部署行。
   `tests/PortableSafeZoneDeviceBundleGuard.py` 核对 bundle 存在、UnityFS 头、1 KiB–512 KiB 之间、部署行在，以及本文里的四个标识。

## 图标生成提示词

完整的 API、透明背景、`ArtSource` 归档、确定性裁切和 Unity 打包流程见 [`AI图片生成与Unity自动打包流程.md`](AI图片生成与Unity自动打包流程.md)。高清原图归档在作者工程 `ArtSource/` 下，不能直接放入 Mod 的 `Assets/`。

可使用以下提示词生成透明背景物品图标：

> 单个便携式安全区部署装置，紧凑耐用的科幻野战发射器，折叠圆形投影环，绿色全息安全区微光，磨损的军用外壳，橄榄黑与哑光金属材质，少量青色指示灯，三分之四视角，居中构图，清晰轮廓，高质量 3D 游戏物品渲染，透明背景，无文字、无标志、无水印、无 UI 边框、无人物、无场景，适合 256×256 背包图标。

## 验收

- 启动日志没有 `ItemFactory` 的 bundle 加载失败或 TypeID 冲突。
- 奖励选中后能得到正确名称的物品，不显示 `*BossRush_*` 占位符。
- 物品只能在末日丧尸模式允许安全区的阶段使用。
- 成功部署后物品被消耗；安全区中心、边界环和地图标记移动到玩家附近。
- 缺少 AssetBundle 时仍能通过运行时兜底得到并使用物品。
