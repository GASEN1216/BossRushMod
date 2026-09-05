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

`scenes` 已配置基地场景；未婚时由永久 NPC 模块生成，已婚后改由婚姻桥恢复教堂驻留或配偶跟随。

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

## 婚后恢复（2026-09-05，COMPAT）

教堂冷加载时可以暂时显示占位 NPC；真实小满生成后会补驻留标记、清理占位物并刷新跟随/离婚选项。保存了跟随状态后切图或读档，会等待真实实例生成后重新启用跟随。重复恢复入口合并，切图、关系变化或清理后的迟到对象不会重新登记。无需迁移关系存档。

章节来源：`Integration/Wedding/WeddingModBehaviourBridge.cs`、`Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs`。隔离回归通过不等于已完成游戏内婚姻/导航 smoke。

## 普通场景的生成交接（2026-09-05，COMPAT）

未婚小满的旧场景生成仍在等待时，新场景入口会保留最新请求。连续 A→B→C 只等待旧 A 回收后生成最终 C，不再吞掉新场景唯一一次生成入口。请求同时绑定 Mod owner、模块世代、场景名和 handle；同名重载也会失效。旧请求的 finally 只释放自己持有的 busy，并交接仍有效的后继请求。

Destroy 清掉等待请求并推进世代，不提前放开仍在途的生成；无后继请求就不会销毁后复生。异步完成后再次核对婚姻与同名登记，竞争失败或异常仅回收自己创建的角色，保留较新实例。婚后教堂/跟随仍沿用上一节的恢复桥，不改变关系存档。

章节来源：`Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs`。验证：`tests/PermanentNpcSuccessorRequestGuard.py`、`tests/fixtures/ContentSecondReview/`；真实场景连续切换及 NPC 交互/导航仍需游戏内 smoke。
