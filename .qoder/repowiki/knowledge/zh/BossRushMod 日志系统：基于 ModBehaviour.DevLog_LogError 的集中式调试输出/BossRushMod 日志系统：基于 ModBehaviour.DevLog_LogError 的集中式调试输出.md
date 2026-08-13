---
kind: logging_system
name: BossRushMod 日志系统：基于 ModBehaviour.DevLog/LogError 的集中式调试输出
category: logging_system
scope:
    - '**'
source_files:
    - ModBehaviour.cs
    - ModConfigApi.cs
    - ZombieMode/ZombieModeEntry.cs
    - Achievement/AchievementEntryUI.cs
    - Achievement/AchievementIconLoader.cs
    - Achievement/AchievementMedalConfig.cs
    - Achievement/AchievementMedalItem.cs
    - Utilities/Utilities.cs
---

## 1. 使用的系统与框架

本仓库没有引入第三方日志库（如 Serilog、NLog、Unity Logger），而是采用**最简方案**：所有模块通过 `BossRush.ModBehaviour` 暴露的静态方法 `DevLog(string)` 与 `LogError(string)` 统一输出日志，底层由游戏引擎（Duckov / Unity）的日志通道承载。`ModConfigAPI` 中还有一个 `[System.Diagnostics.Conditional("BOSSRUSH_DEV")]` 修饰的本地 `DevLog`，仅编译时存在，运行时委托给 `ModBehaviour.DevLog`。

- 日志级别：**无结构化 level 字段**。代码中区分两类调用——`DevLog` 用于开发/调试信息，`LogError` 用于错误路径；二者在调用点语义不同，但均通过同一入口进入，未见运行时按级别过滤逻辑。
- 结构化字段：**无 JSON/键值对结构**。日志为拼接字符串，约定以 `[模块名]` 前缀标识来源（例如 `[BossRush]`、`[AchievementEntryUI]`、`[AchievementIconLoader]`、`[AchievementMedalConfig]`、`[AchievementMedal]`、`[AchievementMedalItem]`、`[ModConfig_v1]`），便于 grep 定位。
- Sink：直接依赖宿主游戏的日志系统（`Player.log` 等），仓库根目录附带 `Player.log` 文件作为运行产物示例。

## 2. 关键文件

| 文件 | 作用 |
|---|---|
| `ModBehaviour.cs` | 模组单例入口，集中声明并调用 `DevLog` / `LogError`，是绝大多数模块的日志出口 |
| `ModConfigApi.cs` | 提供 `ModConfigAPI` 安全封装，其内部 `DevLog` 带 `Conditional("BOSSRUSH_DEV")`，转发到 `ModBehaviour.DevLog` |
| `ZombieMode/ZombieModeEntry.cs` | 定义局部 `DevLogOnceZombieModeOpaqueFilterFailure`，体现“一次性日志”模式 |
| `Achievement/*` | 成就子系统大量使用 `ModBehaviour.DevLog` / `LogError` 记录 AssetBundle/Sprite 加载流程 |
| `Utilities/Utilities.cs` | 工具类中对 Boss 数值倍率调整过程输出详细 DevLog |

## 3. 架构与约定

- **单入口**：全仓几乎统一通过 `ModBehaviour.DevLog(message)` 和 `ModBehaviour.LogError(message)` 输出，避免各模块各自实现日志器。
- **标签化来源**：消息首段固定 `[ClassName]` 或 `[BossRush]` 前缀，形成轻量级 source tag，替代结构化 field。
- **条件编译开关**：`ModBehaviour` 中通过 `#if BOSSRUSH_DEV` 控制 `HardcodedDevModeEnabled`，配合 `ModConfigAPI.DevLog` 上的 `Conditional("BOSSRUSH_DEV")`，使部分调试日志仅在开发构建中编译进程序集。
- **一次性日志**：`ZombieModeEntry` 中的 `DevLogOnceZombieModeOpaqueFilterFailure(int, string)` 展示了“按 key 去重”的一次性日志模式，防止重复刷屏。
- **异常包装**：错误路径统一走 `LogError`，并在 catch 块内记录异常消息，保持正常流程与异常流程的日志分离。

## 4. 约定与约束

- **必须通过 `ModBehaviour.DevLog` / `LogError` 输出**：仓库中所有业务模块（Achievement、Integration、ModeE/F/G、ZombieMode、Utilities 等）均以该方式记录日志，未见绕过此入口的直接 `Debug.Log` 调用。
- **日志消息必须以 `[模块名]` 开头**：这是跨模块可检索的约定，不是编译器强制规则，但被广泛遵守。
- **开发日志与错误日志分开**：正常调试用 `DevLog`，失败/异常分支用 `LogError`；两者在语义上区分，但未见统一的 log level 枚举或过滤器。
- **条件编译裁剪**：`ModConfigAPI` 的 `DevLog` 使用 `[System.Diagnostics.Conditional("BOSSRUSH_DEV")]`，确保非开发构建下不产生调用开销。
- **无运行时级别过滤**：未发现根据配置动态开启/关闭某级别日志的代码，日志开关仅依赖编译期宏。
- **无结构化序列化**：日志始终为字符串拼接，未使用 JSON、字典或自定义事件对象；如需结构化分析需依赖外部 grep/正则扫描。

总体而言，这是一个**极简、集中、标签化的调试日志体系**：以 `ModBehaviour` 为唯一日志门面，通过 `[Tag]` 前缀区分来源，借助条件编译控制开发日志体积，将实际输出委托给宿主引擎，未引入额外框架或 sink 层。
