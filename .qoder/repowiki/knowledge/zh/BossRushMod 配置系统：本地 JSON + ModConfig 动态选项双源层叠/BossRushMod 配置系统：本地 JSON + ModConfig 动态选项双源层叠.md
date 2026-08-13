---
kind: configuration_system
name: BossRushMod 配置系统：本地 JSON + ModConfig 动态选项双源层叠
category: configuration_system
scope:
    - '**'
source_files:
    - Config/Config.cs
    - ModConfigApi.cs
    - Config/LootBlacklistRegistry.cs
    - Config/NPCSpawnConfig.cs
    - Assets/Data/LootBlacklist.json
    - Assets/config/wish_config.dat.example
    - Integration/BossRushIntegration_StartAndScene.cs
---

## 1. 系统/方法概述

BossRushMod 采用**双源配置层叠**策略：
- **运行时用户配置**（可编辑）：通过 Unity `Application.streamingAssetsPath` 下的 `BossRushModConfig.txt`（JSON 格式，由 `JsonUtility` 序列化/反序列化），以及可选的第三方模组 `ModConfig` 提供的在线滑条/开关 UI。
- **数据型配置**（只读或半只读）：`Assets/Data/LootBlacklist.json` 提供掉落黑名单 itemIds；`Assets/config/wish_config.dat` 为愿望池外部配置（示例文件 `wish_config.dat.example` 仅含版本号与占位符）；NPC 刷新点、各子系统开关等大量“内容配置”直接以 C# 常量/字典硬编码在源码中（如 `Config/NPCSpawnConfig.cs`、`Integration/*/Config/*.cs`）。

配置加载顺序：**默认值 → 本地 JSON 文件 → ModConfig 在线选项**，后者优先覆盖前者。所有数值加载时均做范围钳制（clamp），保证非法输入不会破坏运行。

## 2. 关键文件与包

| 路径 | 作用 |
|---|---|
| `Config/Config.cs` | 核心配置类 `BossRushConfig`、本地 JSON 读写、ModConfig 反射集成、运行时热更新事件处理 |
| `ModConfigApi.cs` | 对 `ModConfig` 模组的**安全反射封装**（`ModConfigAPI`），提供 `SafeLoad/SafeSave/AddInputWithSlider/AddBoolDropdownList` 等无异常 API |
| `Config/LootBlacklistRegistry.cs` | 从 `Assets/Data/LootBlacklist.json` 解析掉落黑名单，失败时回退到硬编码兜底列表 |
| `Config/NPCSpawnConfig.cs` | NPC（快递员/哥布林/护士）在各场景的刷新点坐标表，按场景名映射到 `Vector3[]` |
| `Assets/Data/LootBlacklist.json` | 玩家可替换的掉落黑名单数据文件 |
| `Assets/config/wish_config.dat(.example)` | 愿望池外部配置（二进制/文本，当前仓库内为占位示例） |
| `Common/Data/JsonDataRegistry.cs` | 被 LootBlacklistRegistry 调用的统一数据文件读取入口 |
| `Integration/BossRushIntegration_StartAndScene.cs` | 启动流程中调用 `LoadConfigFromFile()` / `SaveConfigToFile()`，触发配置初始化 |

## 3. 架构与设计约定

### 3.1 配置数据结构
`BossRushConfig` 是一个 `[Serializable]` 内部类，集中声明所有可配置项（波次间隔、随机掉落开关、Boss 倍率、Mode D 敌人数、变异词条开关/数量、成就快捷键等），并通过 `JsonUtility.ToJson/FromJson` 持久化。新增配置项需同时修改三处：
1. `BossRushConfig` 字段定义
2. `LoadConfigFromModConfig()` 中的键名与加载逻辑
3. `SetupModConfig()` 中的 UI 注册（`AddInputWithSlider` / `AddBoolDropdownList`）
4. `TryLoadSingleModConfigValue()` 与 `IsHandledModConfigOptionKey()` 的热更新分支

### 3.2 双源层叠机制
- **本地文件优先**：`LoadConfigFromFile()` 先尝试读取 `StreamingAssets/BossRushModConfig.txt`，若不存在则调用 `SaveConfigToFile()` 写出默认值。
- **ModConfig 覆盖**：通过反射查找 `ModConfig.OptionsManager_Mod.Load<T>(key, defaultValue)`，用 `BossRush_<option>` 形式的 key 拉取在线选项，并对每个值执行 clamp（例如 `waveIntervalSeconds` 限制 2~60，`bossStatMultiplier` 限制 0.1~10，`mutatorCount` 限制 1~10）。
- **热更新**：通过 `ModConfig.ModBehaviour.AddOnOptionsChangedDelegate` 订阅变更事件，`OnModConfigOptionsChanged` 根据 changedKey 调用 `TryLoadSingleModConfigValue` 并立即 `SaveConfigToFile()`，部分选项（如波次间隔）还会触发 `StartNextWaveCountdown` 即时生效。

### 3.3 数据型配置
- **LootBlacklist**：`LootBlacklistRegistry.EnsureInitialized()` 单例式懒加载，从 `Assets/Data/LootBlacklist.json` 解析 `itemIds` 数组；解析失败或为空时回退到 `CreateFallbackBlacklistIds()` 的硬编码列表，确保游戏始终可用。
- **NPC 刷新点**：`NPCSpawnConfig` 将每个场景的 `Vector3[]` 与是否随机选择封装进 `NPCSceneSpawnConfig`，并提供 `TryGetCourierNormalModePosition` / `TryGetGoblinSpawnPosition` / `TryGetNurseSpawnPosition` 等查询方法，支持避开其他 NPC 的最小距离过滤。
- **Wish 配置**：`wish_config.dat` 是二进制/自定义格式，仓库仅提供 `.example` 模板，实际内容由外部工具生成。

### 3.4 配置键命名约定
所有 ModConfig 键遵循 `<ModName>_<OptionName>` 形式，其中 `ModName = "BossRush"`，例如 `BossRush_waveIntervalSeconds`、`BossRush_EnableRandomBossLoot`、`BossRush_MutatorCount`。该约定贯穿 `LoadConfigFromModConfig`、`TryLoadSingleModConfigValue`、`IsHandledModConfigOptionKey` 三处。

## 4. 约定与约束

| 规则 | 来源/证据 |
|---|---|
| 配置文件路径固定为 `Application.streamingAssetsPath + "/BossRushModConfig.txt"` | `ConfigFilePath` 属性 |
| 本地配置使用 Unity `JsonUtility` 序列化，字段必须 `[Serializable]` | `BossRushConfig` 类及 `ToJson/FromJson` 调用 |
| 缺失本地配置文件时自动写出默认值 | `LoadConfigFromFile` 中 `else { SaveConfigToFile(); }` |
| 所有数值加载后必须 clamp 到定义域（如 2~60s、0.1~10x、1~10 个） | `LoadConfigFromModConfig` 与 `TryLoadSingleModConfigValue` 中的显式 clamp |
| 新增配置项必须同步更新四处：结构体、加载、UI 注册、热更新分支 | 代码中每个选项都重复出现于这四段 |
| ModConfig 依赖是可选的：未安装时静默跳过，不影响运行 | `FindModConfigType` 返回 null 时记录日志并 return |
| 掉落黑名单 JSON 解析失败时回退到硬编码兜底列表 | `LootBlacklistRegistry.RegisterBlacklist` 中 fallback 逻辑 |
| NPC 刷新点按场景名作为 key 的字典组织，BossRush 模式使用固定坐标，普通模式使用随机点池 | `NPCSpawnConfig.CourierBossRushPositions` 与 `CourierNormalModeConfigs` |
| 运行时修改 ModConfig 选项后会写回本地 JSON 文件 | `OnModConfigOptionsChanged` 末尾 `SaveConfigToFile()` |

## 5. 与其他模块的关系

- 各子系统的“内容配置”（装备套装、新武器、重生道具、阵营旗帜等）集中在 `Integration/Config/*.cs` 中以静态常量/字典暴露，供对应模块直接使用，不经过 `BossRushConfig`。
- `Common/Data/JsonDataRegistry` 提供统一的资源数据读取，被 LootBlacklistRegistry 复用。
- 调试/测试侧通过 `tests/ModConfigOptionChangeGuard.py` 等守护脚本验证配置变更行为不被破坏。

总体而言，BossRushMod 的配置系统以 `Config/Config.cs` 为核心，围绕“本地 JSON + ModConfig 在线选项”的双源层叠设计，辅以数据型 JSON 与硬编码常量两类只读配置，形成用户可调参数与内容数据分离、运行时可热更新的清晰分层。