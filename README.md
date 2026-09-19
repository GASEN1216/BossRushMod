# BossRushMod for Escape from Duckov

**中文** | **[English](README_EN.md)**

<p align="center">
  <img src="preview.png" alt="BossRush Mod Preview" width="400">
</p>

[![Steam Workshop](https://img.shields.io/badge/Steam%20Workshop-3612465423-blue?logo=steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3612465423)
[![Game](https://img.shields.io/badge/Game-Escape%20from%20Duckov-orange)](https://store.steampowered.com/app/3167020)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

《逃离鸭科夫》（Escape from Duckov）的综合玩法 Mod。以 BossRush 竞技场为起点，现在有八种游戏模式、一张独立出击地图、原创 Boss 与装备、NPC 关系线、剧情战役、基地建筑，以及大量运行时稳定性修复。

- **玩家文档**：[在线 Wiki](https://gasen1216.github.io/BossRushMod/)（与游戏内百科同一份正文，中英双语）
- **订阅**：[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3612465423)
- **参与开发 / AI 协作**：先读 [AGENTS.md](AGENTS.md)

## 内容一览

### 游戏模式

| 模式 | 怎么进 | 玩法 |
| --- | --- | --- |
| 标准 BossRush（弹指可灭 / 有点意思） | 携带 BossRush 船票 | 每波 1 个 / 3 个 Boss |
| 无间炼狱 | 携带 BossRush 船票 | 无限波次，每波 Boss 数可配置，带现金池与自动吸附 |
| 白手起家（Mode D） | 裸装携带船票 | 随机起装，独立的敌池、掉落与成长节奏 |
| 划地为营（Mode E） | 裸装携带营旗 | 多阵营沙盒混战 |
| 血猎追击（Mode F） | 裸装携带船票与血猎收发器 | 四阶段大逃杀：持续失血、击杀回血、赏金追踪、工事与撤离 |
| 宿命回响（Mode G） | 携带船票与宿命回响信物 | 固定九波三幕，会反制你的宿敌与契约 |
| 百战留痕（黑市鸭王杯） | 基地码头的船，花一张船票 | 当经理人、自己不下场：签斗士、看盘口，一季六场 |
| 末日丧尸模式 | 基地商人处买尸潮邀请函 | 独立的生存模式：净化点经济、每波选强化、撤离结算 |

### 地图

- **9 张 BossRush 竞技场地图**，地图选择走原版界面。
- **天空岛 · 晴岚群岛**：从基地船点出发的独立出击地图，不消耗船票。十二座岛上有修复风标与星灯的主线、搜刮与采集、配方、居民委托，以及 Boss「噬风」。

### Boss、NPC 与装备

- **原创 Boss**：龙裔遗族、焚天龙皇、幽灵女巫。
- **NPC**：阿稳（快递员）、叮当（哥布林工匠，负责重铸）、羽织（护士），以及用捏脸工具做的永久 NPC；好感度、送礼与婚姻线。
- **装备**：龙裔 / 龙王套装、霜冠 / 雷神套装、腾云驾雾图腾、逆鳞、焚皇断界戟、龙息、焚天龙铳、噬魂挽歌、霜之哀伤，以及毒蛇匕首、召唤法杖、能量盾、冰霜长矛、雷电戒指。

### 系统

鸭王征程（六章剧情战役）、竞技场后山（菜地、战利品登记簿、点唱机）、遗种巢（养崽）、鸭科夫日报、鸭皇图鉴、局内随机事件、词缀锻造、重铸、星愿许愿台、死亡亡魂、变异词条、成就、Boss 筛选器、游戏内百科。

每一项的规则、数值与获取方式见在线 Wiki。

## 配置

两个入口：`ModConfig`，以及游戏目录下的 `StreamingAssets/BossRushModConfig.txt`（JSON）。玩法系统默认开启；鸭生无常可手动关闭，其余内容系统只暴露调参旋钮。常用项：

| 键名 | 默认值 | 说明 |
| --- | --- | --- |
| `waveIntervalSeconds` | `15` | 波次间休息时间（秒） |
| `milestoneRestBonusSeconds` | `30` | 每 5 波额外休息时间（秒），0 = 不额外休息 |
| `useInteractBetweenWaves` | `false` | 波次间改为手动交互开下一波 |
| `infiniteHellBossesPerWave` | `3` | 无间炼狱每波 Boss 数 |
| `bossStatMultiplier` | `1.0` | Boss 全局数值倍率 |
| `modeDEnemiesPerWave` | `3` | 白手起家每波敌人数 |
| `enableRandomBossLoot` | `true` | Boss 随机掉落加成 |
| `useLegacyBossLootProbabilities` | `true` | 标准 Boss 战利品箱用原版概率区间；没出 Q6+ 时额外追加 1 件保底 |
| `lootBoxBlocksBullets` | `false` | 掉落箱可作为掩体挡子弹 |
| `disabledBosses` | `[]` | 被禁用的 Boss 列表 |
| `bossInfiniteHellFactors` | `{}` | 无间炼狱 Boss 刷新权重因子 |
| `enableDragonDash` | `true` | 龙冲刺相关能力 |
| `enableDeathWraithSystem` | `true` | 死亡亡魂系统 |
| `useWolfModelForWildHorn` | `true` | 荒野号角使用狼模型 |
| `achievementHotkey` | `L` | 成就面板热键（内部存 `KeyCode` 整数） |

完整选项见 Wiki 的「配置选项」页。

## 从源码构建

仓库不是 `.csproj` 工程：`compile_official.bat` 显式列出全部源码，直接调用 .NET SDK 自带的 Roslyn `csc.dll`（C# 7.3），输出 `Build/BossRush.dll` 并部署到游戏的 Mod 目录。

需要：Windows、.NET SDK、本机安装的《逃离鸭科夫》、创意工坊的 HarmonyLoadMod。脚本会自动探测游戏与创意工坊的路径，探测不到时设置环境变量 `GAME_PATH` / `WORKSHOP_PATH`。

```text
compile_official.bat                     正式构建并部署
compile_dev.bat                          Dev 构建：调试日志、调试热键、F3 玩法验收
python tools/run_guards.py               结构守卫（CI 在推送与 PR 上自动跑）
python tools/run_runtime_regressions.py  隔离执行回归
npm --prefix wiki-site run dev           本地预览在线 Wiki
```

编译和守卫通过不代表运行时正确：Harmony 补丁与反射绑定只能在游戏里确认。新增 `.cs` 必须登记进 `compile_official.bat`；TypeID、本地化、存档兼容等规则见 [AGENTS.md](AGENTS.md)。

## 目录结构

```text
BossRushMod/
├── ModBehaviour.cs, ModConfigApi.cs   主入口、全局状态、配置 API
├── WavesArena/                        标准 BossRush、无间炼狱
├── ModeD/ ModeE/ ModeF/ ModeG/ ModeH/ 各模式（ModeH = 百战留痕）
├── ZombieMode/                        末日丧尸模式
├── Campaign/                          鸭王征程
├── PetNest/  RandomEvents/            遗种巢、局内随机事件
├── Integration/                       物品、装备、NPC、商店、好感、婚姻、重铸、图鉴、日报、后山……
├── DebugAndTools/                     调试工具与 F3 验收；SkyIsland/ 是天空岛运行时
├── Common/  Utilities/  Patches/      共享库、跨模块基础设施、Harmony 补丁
├── Config/  Localization/  LootAndRewards/  Achievement/  Audio/
├── BossFilter/  Interactables/  MapSelection/  UIAndSigns/
├── Assets/Data/  Assets/SpawnPoints/  进 git 的 JSON 数据（其余 Assets 为本地资源）
├── ArtSource/SkyIsland/  tools/       天空岛生成数据、各类生成与校验脚本
├── tests/                             结构守卫、属性测试、执行回归夹具
├── WikiContent/  wiki-site/           游戏内百科正文、在线 Wiki 站点
└── docs/                              本地设计与教程（默认不进 git）
```

## 调试热键

以下热键只在 Dev 构建（`compile_dev.bat`）里有：

| 热键 | 功能 |
| --- | --- |
| `F2` | 物品生成器 |
| `F3` | 调试作弊总控面板（传送、属性、物品、加钱、清冷却）与完整玩法验收 |
| `F4` | 清空成就数据 |
| `F5` / `F7` / `F8` | 输出附近建筑与对象 / 最近交互点 / 附近角色信息 |
| `F6` | 放置模式 |
| `F9` | 发放 BossRush 船票并打开地图选择 |
| `F10` | 强制清场并触发通关流程 |
| `F11` | 背包检查器 |
| `F12` | NPC 传送界面 |

正式构建里也有的：`Ctrl+F10` 打开 Boss 筛选器，`L`（可在配置里改）打开成就面板。

## 许可

本项目采用 [MIT License](LICENSE)。
