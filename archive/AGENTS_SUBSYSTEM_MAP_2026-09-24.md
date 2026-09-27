原根 AGENTS.md §3 子系统地图，2026-09-24 迁出，供历史对照。

## 3. 子系统地图

| 路径 | 职责 | 专项规则 |
| --- | --- | --- |
| `ModBehaviour.cs`、`ModConfigApi.cs` | 主入口、全局状态、ModConfig API | 本文 §4.15 |
| `WavesArena/` | 标准 BossRush 与无间炼狱波次 | |
| `ModeD/`、`ModeE/`、`ModeF/` | 白手起家、划地为营、血猎追击 | |
| `ModeG/` | 宿命回响：九波三幕、宿敌、契约 | |
| `ModeH/` | 百战留痕（黑市鸭王杯）：经理人模式 | |
| `ZombieMode/` | 末日丧尸模式，独立生命周期与奖励 | `ZombieMode/AGENTS.md` |
| `Campaign/` | 鸭王征程：六章剧情契约，经全局采集器与少量 notify 漏斗挂到各模式，不重构模式代码 | |
| `PetNest/`、`RandomEvents/` | 遗种巢（养崽）、局内随机事件 | |
| `Integration/` | 物品、装备、NPC、商店、好感、婚姻、重铸、词缀锻造、图鉴、日报、竞技场后山、新武器与套装、天空岛物品 | `Integration/AGENTS.md` |
| `DebugAndTools/` | 调试工具、F3 调试菜单与玩法验收；`SkyIsland/` 是天空岛运行时（正式内容）；`ArenaPrototype/` 是石堡等场景原型 | `DebugAndTools/SkyIsland/AGENTS.md` |
| `Common/` | 共享特效、装备能力、数据解析、存档引擎、共享 UI 库 `Common/UI/BossRushUI.cs` | 本文 §4.14 |
| `Utilities/` | 跨模块运行时 hooks、刷怪核心、场景门控、恢复监控 | `Utilities/AGENTS.md` |
| `Patches/` | 跨模块 Harmony 补丁 | `Patches/AGENTS.md` |
| `Config/`、`Localization/`、`LootAndRewards/` | 配置与数据注册、本地化注入、掉落与奖励 | |
| `Achievement/`、`Audio/`、`BossFilter/`、`Interactables/`、`MapSelection/`、`UIAndSigns/` | 各自独立的小子系统 | |
| `Injection/` | 旧注入逻辑的残留占位，实际逻辑已并入 `ModBehaviour` / `Integration` | |
| `Assets/Data/`、`Assets/SpawnPoints/` | 进 git 的 JSON 数据表；其余 `Assets/`（bundle、图片、音效）local-only | |
| `ArtSource/SkyIsland/`、`tools/` | 天空岛可重复生成的数据；生成器、构建与校验脚本 | |
| `tests/` | 结构守卫、属性测试、执行回归夹具 | `tests/AGENTS.md` |
| `WikiContent/`、`wiki-site/` | 游戏内百科正文（中英）、VitePress 在线 Wiki | `wiki-site/AGENTS.md` |
| `docs/` | 架构说明、教程、参考表、设计稿、报告、契约，默认 local-only；目录见 `docs/README.md` | `docs/AGENTS.md` |
| `.qoder/repowiki/` | 详细知识库（底子是 2026-08 的生成快照） | 本文 §4.13 |
| `鸭科夫源码/` | 官方反编译源码，只读参考；grep 时排除或写明用途 | |

