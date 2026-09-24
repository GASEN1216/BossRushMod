# Patches/AGENTS.md — Harmony 补丁专项规则

> 先读根目录 `AGENTS.md`。官方 API 的静默失败类陷阱见 `docs/contracts.md` §7.1；官方游戏更新后的复查清单见 `docs/architecture/Harmony补丁契约稳定性.md`（local-only）。

## 职责边界

`Patches/` 放跨模块的基础设施补丁，按功能分组（如 `Patches/Combat/`）。Boss、武器或单个子系统专属的补丁留在对应模块目录，除非它服务多个模块。补丁按类逐个安装（`Utilities/AlwaysOnRuntimeHooks.cs`），分组经 `Common/Infrastructure/HarmonyPatchGroupRegistrar` 注册；实际生效情况以逐类安装日志为准。

## 规则

- 加补丁前先查是否已有补丁命中同一目标方法。能在既有补丁链上加消费者就不新开补丁（死亡掉落、致死钳制都是这样接的）。
- 官方方法有重载时显式指定参数类型。
- 不依赖「更早的 Awake / Start 一定抢到时机」；选幂等、与时机无关的拦截点。
- 补丁或反射失败可能被防御式 catch 吞掉：关键绑定要有低噪声诊断或守卫（范例 `HarmonyBindingMethodIdentityGuard`）。
- 想让原版地图击杀也触发的掉落，挂 `CharacterMainControl.OnDead` 前缀（`Patches/Combat/CharacterOnDeadPatch.cs`），补齐 defer 协议，并把新 integration 登记进 `ExtraBossDropDeferGuard` 的 `INTEGRATIONS`。
- `Health.Hurt` 上的观察类补丁只插入观察、不改写伤害；IL 匹配失败要诊断并跳过（`docs/contracts.md` §7）。
- 新补丁 `.cs` 进 `compile_official.bat`（根 §4.1）。

## 验证

- 编译只证明语法和类型，不证明补丁命中。补丁命中、签名漂移和运行时副作用只能靠游戏内 smoke 或逐类安装日志确认；没实机要写明。
- 官方游戏更新后，逐项核对目标类、方法名、签名与字段形态。
