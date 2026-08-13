---
kind: error_handling
name: BossRushMod 错误处理：SafeRuntime 兜底、DevLog 日志与 Python Guard 预算约束
category: error_handling
scope:
    - '**'
source_files:
    - Utilities/SafeRuntime.cs
    - ModBehaviour.cs
    - tests/EmptyCatchGuard.py
    - tests/empty_catch_budget.txt
    - tests/WavesArenaCriticalExceptionGuard.py
    - tests/SmokeLogScan.py
    - tests/SmokeLogScanGuard.py
---

## 1. 使用的系统/方法

BossRushMod 没有引入统一的异常类型体系或中间件框架，而是采用“轻量级运行时兜底 + 集中日志 + Python 静态守护”的组合方式：
- 运行时兜底：`Utilities/SafeRuntime.cs` 提供 `Run(label, action)` 和 `Try(label, func, fallback)` 两个包装器，把任意 Unity 回调/Harmony 补丁中的异常捕获为一次性的 `[SafeRuntime] [WARNING]` 日志，并返回默认值，避免崩溃传播到游戏主循环。
- 日志通道：所有模块通过 `ModBehaviour.DevLog(...)`（继承自底层 Duckov Modding 基类）输出带 `[BossRush]` 前缀的调试日志；关键路径失败时也会用 `catch (Exception e) { DevLog("..." + e.Message); }` 记录上下文。
- 无自定义异常类型：代码中极少 `throw new`，更多是“try/catch + 降级/跳过 + 日志”的模式。对需要严格保证的关键路径，则通过 Python 测试脚本在编译期/CI 阶段断言。

## 2. 关键文件与包

- `Utilities/SafeRuntime.cs`：唯一集中封装 try/catch 的工具类，维护 `loggedWarningLabels` HashSet 防止同一 label 重复刷屏，并提供 `ResetStaticCaches()` 供场景切换时清理。
- `ModBehaviour.cs`：模组单例入口，大量使用 `DevLog` 记录启动、配置、生成 Boss、查找模板等流程；其基类提供 `DevLog` 实现。
- `tests/EmptyCatchGuard.py`：正则扫描全仓 `.cs` 中的空 `catch {}`，并与 `tests/empty_catch_budget.txt` 中登记的当前冻结预算比较，超过即 CI 失败。
- `tests/WavesArenaCriticalExceptionGuard.py`：针对 `WavesArena/WavesArena.cs` 和 `WavesArena/WavesArenaSpawnerControl.cs` 中若干关键方法，强制要求包含特定 `[BossRush] [ERROR] ...` 日志片段且不允许空 catch。
- `tests/SmokeLogScan.py` + `tests/SmokeLogScanGuard.py`：运行后扫描游戏最新 log 中是否出现含 `BossRush` 的 Exception/ERROR/Error 块，作为冒烟测试的一部分。

## 3. 架构与约定

- **分层兜底**：业务逻辑层（Achievement、Integration、ModeE/F/G、ZombieMode 等）遇到不可恢复的外部调用（AssetBundle、反射、Unity API）时自行 try/catch，并通过 `SafeRuntime.Run/Try` 包裹高频回调；最外层由 Harmony 补丁或 Unity 生命周期兜住。
- **一次性告警**：`SafeRuntime.LogWarningOnce` 用 label 去重，同一个操作只记一次警告，避免日志风暴。
- **日志格式约定**：错误日志统一以 `[BossRush] [ERROR]` 或 `[SafeRuntime] [WARNING]` 开头，便于 `SmokeLogScan` 和 `WavesArenaCriticalExceptionGuard` 正则匹配。
- **关键路径显式断言**：波次竞技场等核心状态机不依赖运行时 catch 吞错，而是由 `WavesArenaCriticalExceptionGuard.py` 在源码中检查每个关键方法是否记录了预期的错误日志。
- **空 catch 预算制**：仓库允许已登记的防御式空 catch，但预算只在审核新增宿主防崩边界后按精确现状更新；后续任何未登记增长都会被 `EmptyCatchGuard.py` 拒绝。
- 当前冻结值为 919，包含 Mode G 运行时、事务清理和跨宿主事件边界中的 no-throw 防崩分支；该数字是上限，不是新增空 catch 的额度。

## 4. 约定与约束

- **运行时**：
  - 对可能抛异常的 Unity/第三方调用，优先使用 `SafeRuntime.Try(label, () => ..., fallback)` 包裹，fallback 通常返回 false/null 并记录一次警告。
  - 禁止随意 `throw new` 向上抛出未定义异常类型；如需表示业务失败，应返回 bool/Result 并通过日志/事件通知。
  - 所有 catch 分支至少记录一条 `ModBehaviour.DevLog` 或写入 SafeRuntime 日志，不得静默吞掉异常——除非该 catch 已被计入 `empty_catch_budget.txt`。
- **构建/CI**：
  - `tests/EmptyCatchGuard.py` 必须在 CI 中执行；若新增空 catch 导致总数超过 `tests/empty_catch_budget.txt` 的冻结值，构建失败。
  - `tests/WavesArenaCriticalExceptionGuard.py` 强制 WavesArena 关键方法包含 `[BossRush] [ERROR] HandleBossDeath 错误:`、`ProceedAfterWaveFinished 错误:`、`OnBossSpawnFailed 错误:`、`TryFixStuckWaveIfNoBossAlive 错误:` 等日志片段，否则失败。
  - `tests/SmokeLogScanGuard.py` 会运行 `SmokeLogScanTests`，确保冒烟扫描工具本身可用。
- **可观测性**：错误信息需包含足够上下文（如文件名、资源名、参数），以便 `SmokeLogScan` 能定位问题；推荐格式为 `[模块名] 描述: 异常.Message`。

总体而言，该仓库的错误处理不是基于异常类型或中间件的“硬契约”，而是通过一个小型 SafeRuntime 工具、统一的 DevLog 日志约定，以及一组 Python 守卫脚本共同构成的“软约束 + 静态预算”体系。
