# Patches/AGENTS.md — Harmony 补丁专项规则

> 先读根目录 `AGENTS.md` 和 `docs/架构说明/Harmony补丁契约稳定性.md`。

## 职责边界

`Patches/` 放跨模块基础设施补丁，按功能分组。Boss 或武器专属补丁优先留在对应模块目录，除非它服务多个模块。

## 必守规则

- 新增或修改 `[HarmonyPatch]` 前，先查是否已有补丁命中同一目标方法。
- 官方方法有重载时必须显式指定参数类型，避免 `PatchAll()` 歧义。
- 不依赖“更早 Awake/Start 一定抢到时机”的脆弱假设；优先选择幂等、时机无关的拦截点。
- 补丁失败、反射失败可能被防御式 catch 吞掉。关键 patch 要有低噪声诊断或 guard。
- 新补丁 `.cs` 必须加入 `compile_official.bat`。
- 官方游戏更新后，按 Harmony 契约文档逐项核对目标类、方法名、签名、字段形态。

## 验证

- 编译只能证明 Mod 自身语法，不证明 patch 命中。
- patch 命中、签名漂移、运行时副作用必须靠游戏内 smoke 或 Harmony 调试日志确认。
