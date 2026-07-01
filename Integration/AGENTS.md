# Integration/AGENTS.md — 集成层专项规则

> 先读根目录 `AGENTS.md`。本文件只记录 `Integration/` 独有约束。

## 职责边界

`Integration/` 是 BossRushMod 的内容集成总线：动态物品、装备、NPC、商店、Wiki、好感度、婚姻、重铸、死亡亡魂、新武器、Boss 专属资源等都在这里接入。

## 必守规则

- 新增物品/装备先判断走 `ItemFactory` 还是 `EquipmentFactory`，不要把装备塞进通用物品管线，也不要绕过已有工厂手写注册。
- 新增 TypeID 遵循根 `AGENTS.md` 的递增规则，并登记 `docs/Bossrush使用物品ID表.md`。
- `DisplayNameRaw = "BossRush_*"` 必须有本地化注入，并接入 `InjectLocalization_Extra_Integration()`。
- 新增 NPC 优先实现 `INPCModule`，让 `NPCModuleRegistry` 自动发现；不要往 `ModBehaviour.InitializeAffinitySystem()` 里堆新 NPC 特例。
- 礼物、好感、对话、商店、婚姻优先复用 `Integration/Affinity/`、`Integration/NPCs/Common/`、`Integration/Wedding/` 的共享系统。
- 事件订阅必须有 `EnsureRuntime()` / `ShutdownRuntime()` 或同等 owner；静态缓存类应提供 `ResetStaticCaches()`。
- Boss 子目录新增文件遵循 `docs/架构说明/BOSS模板约定.md`；旧 Boss 不强制重构。
- 触及重铸、婚姻、寄存、愿望、好感度等持久化路径时，检查 `BossRush_` save key 和兼容读取。

## 验证

- 新增 `.cs` 后查 `compile_official.bat`。
- 物品/装备/NPC 变更至少需要 Windows 编译；运行时仍需游戏内 smoke。
- 本地化变更要在游戏内确认没有 `*BossRush_*` 占位符。
