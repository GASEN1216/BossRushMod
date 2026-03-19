# BossRushMod for Escape from Duckov

**中文** | **[English](README_EN.md)**

<p align="center">
  <img src="preview.png" alt="BossRush Mod Preview" width="400">
</p>

[![Steam Workshop](https://img.shields.io/badge/Steam%20Workshop-3612465423-blue?logo=steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3612465423)
[![Game](https://img.shields.io/badge/Game-Escape%20from%20Duckov-orange)](https://store.steampowered.com/app/3167020)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

## 项目概览

BossRushMod 是《Escape from Duckov》的综合型大型 Mod。它以 BossRush 竞技场玩法为核心，但当前源码已经扩展成一个包含多模式玩法、自定义 Boss、自定义装备与物品、NPC 关系线、成就、重铸、游戏内 Wiki、本地化、音频和运行时稳定性修复的完整内容包。

这份 README 反映当前仓库源码基线。更完整的开发向说明见 [docs/项目全景文档.md](docs/项目全景文档.md)。

## 内容总览

- 5 个主要玩法模式：标准 BossRush 3 档、Mode D、Mode E
- 9 张已接入 BossRush 的地图
- 2 个核心自定义 Boss：龙裔遗族、焚天龙皇
- 3 位常驻 NPC：阿稳、叮当、羽织
- 多条系统线：装备能力、物品、重铸、好感度、婚姻、成就、游戏内百科

## 玩法模式

| 模式 | 进入方式 | 核心规则 |
|------|----------|----------|
| **弹指可灭** | 携带 BossRush 船票进入 | 每波 1 个 Boss，适合初次体验 |
| **有点意思** | 携带 BossRush 船票进入 | 每波 3 个 Boss，标准多目标战斗 |
| **无间炼狱** | 携带 BossRush 船票进入 | 无限波次，每波 Boss 数由配置决定，带现金池与自动吸附 |
| **Mode D：白手起家** | 裸装携带 BossRush 船票进入 | 随机起装，独立敌池、掉落和成长节奏 |
| **Mode E：划地为营** | 裸装携带营旗进入 | 多阵营沙盒混战，支持随机旗、指定旗和“爷的营旗”独狼模式 |

## 支持地图

当前代码中已注册并可用于 BossRush 的地图一共 9 张：

- DEMO终极挑战 / DEMO Ultimate Challenge
- 零度挑战 / Zero Challenge
- 零号区 / Ground Zero
- 仓库区 / Hidden Warehouse
- 农场镇 / Farm Town
- J-Lab实验室 / J-Lab Laboratory
- 口口场地 / Underground Arena
- 37号实验区 / Zone 37 Experimental Area
- 迷宫 / Maze

地图选择由原版 UI 集成层接管，BossRush、Mode D 和 Mode E 共用这套地图接入基础。

## 自定义内容

### Boss

- **龙裔遗族**：BossRush 体系中的核心自定义 Boss 之一。
- **焚天龙皇**：当前高阶核心 Boss，英文名为 **Skyburner Dragon Lord**。

### NPC

- **阿稳（Awen）**：快递员与基础引导 NPC，关联商店、存储、Wiki 书等流程。
- **叮当（Dingdang）**：哥布林 NPC，关联重铸、礼物、折扣和故事线。
- **羽织（Yu Zhi）**：护士 NPC，负责治疗、礼物互动和婚姻线。

### 装备与能力

- 龙套装
- 龙王套装
- 飞行图腾
- 逆鳞
- 龙王专属武器系统

### 关键物品

- BossRush 船票
- 生日蛋糕
- 冒险家日志 / Wiki Book
- 钻石、钻戒、砖石、安神滴剂、平安护身符、叮当涂鸦、荒野号角
- Mode E 营旗
- Mode E 战场道具：挑衅烟雾弹、混沌引爆器、猎王响哨、血狩烽火
- 成就勋章

### 关键系统

- NPC 好感度、对话、礼物、礼物容器、商店、婚姻
- 装备重铸系统
- 成就系统与 Steam 风格弹窗
- 游戏内 Wiki
- BossFilter：Boss 池筛选与无间炼狱权重编辑
- 波次奖励、掉落箱、地图交互物
- 现金自动吸附、敌人卡住/坠落恢复等稳定性逻辑

## 配置

BossRush 目前同时支持两种配置入口：

1. `ModConfig`
2. 本地文件 `StreamingAssets/BossRushModConfig.txt`

当前关键配置项如下：

| 键名 | 默认值 | 说明 |
|------|--------|------|
| `waveIntervalSeconds` | `15` | 波次间休息时间 |
| `enableRandomBossLoot` | `true` | 启用 Boss 随机掉落加成 |
| `useInteractBetweenWaves` | `false` | 波次间改为手动交互开下一波 |
| `lootBoxBlocksBullets` | `false` | 掉落箱是否可作为掩体挡子弹 |
| `infiniteHellBossesPerWave` | `3` | 无间炼狱每波 Boss 数 |
| `bossStatMultiplier` | `1.0` | Boss 全局数值倍率 |
| `modeDEnemiesPerWave` | `3` | 白手起家每波敌人数 |
| `disabledBosses` | `[]` | 被禁用的 Boss 列表 |
| `bossInfiniteHellFactors` | `{}` | 无间炼狱 Boss 刷新权重因子 |
| `enableDragonDash` | `true` | 是否启用龙冲刺相关能力 |
| `achievementHotkey` | `L` | 成就面板热键，内部存储为 `KeyCode` 整数值 |
| `useWolfModelForWildHorn` | `true` | 荒野号角是否使用狼模型 |

## 技术栈与运行方式

| 项目 | 说明 |
|------|------|
| 语言 | C# 7.3 |
| 运行时 | Unity（游戏内嵌 Mono） |
| 构建方式 | `compile_official.bat` 直接调用已安装 .NET SDK 的 Roslyn `csc.dll`，无 `.csproj` |
| 输出 | `Build/BossRush.dll` |
| Harmony | 通过 Workshop 路径下的 `0Harmony.dll` 引用，主要用于 Mode E 运行时补丁 |

## 从源码构建

这个仓库不是标准 `.csproj` 工程，而是脚本驱动的 C# 源码仓库。

### 构建脚本

- `compile_official.bat`：编译全部源码并尝试部署 `Build/BossRush.dll`
- `test_bossrush_official.bat`：编译后复制到本地游戏目录，便于进游戏测试
- `cleanup_old_files.bat`：清理旧产物

### 环境前提

- Windows
- 已安装 `dotnet` SDK
- 本地存在 `Escape from Duckov` 游戏目录
- 本地存在 Workshop 目录和 `HarmonyLoadMod`
- 游戏依赖程序集位于 `Duckov_Data\\Managed\\`

### 维护注意事项

- 新增 `.cs` 文件后，必须同步修改 `compile_official.bat`，否则不会参与编译。
- 构建脚本包含硬编码路径，换机器或换盘符时需要先调整脚本。
- 项目主入口是 `ModBehaviour`，但大量逻辑分散在多个 `partial class` 文件中。

## 目录结构

```text
BossRushMod/
├── ModBehaviour.cs                  # 主入口与全局状态
├── ModConfigApi.cs                  # ModConfig 封装
├── Achievement/                     # 成就、勋章、Steam 风格弹窗
├── Audio/                           # 音频管理
├── BossFilter/                      # Boss 池筛选与无间炼狱因子
├── Config/                          # 运行时配置与数据
├── DebugAndTools/                   # ItemSpawner、InventoryInspector、NPC 传送 UI
├── Integration/                     # 动态物品、装备、NPC、商店、Wiki、关系系统总线
├── Interactables/                   # 路牌、补给、维修、清箱、传送
├── Localization/                    # 本地化注入与文本管理
├── LootAndRewards/                  # 掉落、奖励、奖励箱
├── MapSelection/                    # BossRush 地图选择接入
├── ModeD/                           # 白手起家
├── ModeE/                           # 划地为营、营旗、商人、战场道具
├── UIAndSigns/                      # 场内提示、横幅、路牌 UI
├── Utilities/                       # 刷怪、缓存、敌人恢复监控等工具
├── WavesArena/                      # 标准 BossRush / 无间炼狱核心逻辑
├── WikiContent/                     # 游戏内百科内容
└── docs/                            # 设计文档与项目说明
```

## 调试与开发辅助

项目内置了完整的调试热键体系，常用项包括：

| 热键 | 功能 |
|------|------|
| `F2` | 打开 / 关闭 `ItemSpawner` |
| `F3` | 打开 / 关闭婚姻系统测试面板 |
| `F4` | 清空成就数据 |
| `F5` | 输出附近建筑 / 对象信息 |
| `F6` | 切换放置模式 |
| `F7` | 输出最近交互点信息 |
| `F8` | 输出附近角色信息 |
| `F9` | 发放 BossRush 船票并打开地图选择 |
| `F10` | 强制清场并触发通关流程 |
| `F11` | 打开 `InventoryInspector` |
| `F12` | 打开 / 关闭 NPC 传送 UI |
| `Ctrl+F10` | 打开 / 关闭 BossFilter |
| `L` | 默认成就面板热键 |

## 文档

- 开发总览：[docs/项目全景文档.md](docs/项目全景文档.md)
- 设计文档目录：[docs/](docs/)
- 游戏内百科内容：[WikiContent/](WikiContent/)

## 许可

本项目采用 [MIT License](LICENSE)。
