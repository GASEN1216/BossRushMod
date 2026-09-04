# 小满（示例捏脸 NPC）

> **状态**：示例/参考实现。名字与 `NPC_ID` 在首次随版本发布前可改，发布后不可改
> （好感度、婚姻、剧情标记都以 id 为存档键，改名等于把玩家与它的所有关系静默清零）。

## 定位

`duck_npc_xiaoman` 是**捏脸永久 NPC 路线**的第一只，用来验证整条链路，
也作为后续永久 NPC 的模板。它没有专属服务（不卖东西、不治疗），
只有社交功能：聊天、送礼、好感度、结婚。

与羽织、叮当最大的不同是**它没有任何美术资产**：长相是
`Assets/Data/DuckNpcs.json` 里的一段 `faceJson`，本体是官方 `CharacterMainControl`，
移动/动画/语音/脚步全部由官方栈提供。

## 数据

全部定义在 `Assets/Data/DuckNpcs.json` 的 `duck_npc_xiaoman` 一条里：

| 项 | 值 |
| --- | --- |
| 显示名 | 小满 / Xiao Man |
| 底模 | 官方默认鸭模（`baseModel` 留空） |
| 外观 | 青瓷色羽毛、深色短发、偏大的青灰色眼睛 |
| 装备 | 橘子耳机（TypeID 1252） |
| 移动 | 漫步，半径 6m |
| 阵营 | `player`（清场豁免与不被攻击的根基） |
| 喜欢 | `Consumable` / `Food` 标签的物品 |
| 对话 | 问候语按好感度分 0 / 5 / 10 三档；含婚后专属台词 |

`scenes` 目前留空 —— **尚未指定常驻场景**，需要 owner 决定它住在哪张图。
填上场景名后它会在该场景自动生成。

## 能力

复用现有系统，无一处新写：

- 好感度 1–10 级、每日首次聊天涨点、不互动会衰减（`AffinityManager`）
- 送礼与反应气泡（`NPCGiftSystem`）
- 分级对话气泡（`NPCDialogueSystem` + 蓝图里的 `dialogues`）
- 结婚、婚礼教堂驻留、配偶跟随、离婚（`NPCMarriageSystem` + 6 处泛化分支）
- Mode H 清场豁免（`DuckNpcRuntimeMarker` 实现 `INPCController`）

## 相关

- [捏脸 NPC 工具链](捏脸%20NPC%20工具链.md) —— 架构与原理
- [捏脸NPC使用手册](../../../../docs/制作教程/捏脸NPC使用手册.md) —— 怎么加下一只
- [好感度框架核心](好感度框架核心.md)

## 2026-09-04 深度复审修复

`COMPAT`。普通 DuckNpcModule 的场景判定和生成循环同时排除 isPermanent 蓝图，小满只交 DuckNpcPermanentModule 持有，避免普通分身绕过婚姻/交互生命周期。克隆类材料（遗种蛋、词缀熔石、种子）配置时调用 ModeFItemConfigHelper.ClearInheritedUsage，清来源行为并解绑空 UsageUtilities；空行为表在官方仍可用，不能只 Clear 列表。

章节来源：`Integration/NPCs/DuckNpc/DuckNpcModule.cs`、`Integration/Items/ModeFItemConfigHelper.cs`、`Integration/AffixForge/AffixForgeStoneConfig.cs`。
