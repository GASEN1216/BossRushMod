#!/usr/bin/env python3
"""
ModeGMapSupportGuard — Mode G 支持地图注册表守卫（规格 §20 第 3 条）。

不变式：
- SupportedMap 不可变条目含 exact sceneName+sceneID+非空 verificationRevision；
- IsSupported 必须 exact 匹配 scene pair（单点通过不得冒充三元组）；
- 支持集合直接复用地图选择 UI 的 GetAllMapConfigs()，不维护第二份清单；
- preview 按当前 active scene 冻结玩家实际选择的 Verified pair；
- 注册表 revision 取自 ModeGAvailability.CurrentVerificationRevision；
- 规格要求：每条记录须有 death behavior / 双语风险 key / 冻结安全三元组
  （缺失即 FAIL——实现补齐前不放宽）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")
RUNTIME_SCENE = os.path.join(MODEG_DIR, "ModeGRuntimeModule_PublicApiAndShutdown.cs")
INTEGRATION_CLEANUP = os.path.join(REPO_ROOT, "Integration", "BossRushIntegration_StartAndScene.cs")
MODE_RUNTIME_HOOKS = os.path.join(REPO_ROOT, "Utilities", "ModeRuntimeHooks.cs")


def read_file(name):
    path = os.path.join(MODEG_DIR, name)
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []

    registry = read_file("ModeGMapSupportRegistry.cs")
    entry = read_file("ModeGEntry.cs")
    runtime_scene = ""
    if os.path.exists(RUNTIME_SCENE):
        with open(RUNTIME_SCENE, "r", encoding="utf-8", errors="replace") as fh:
            runtime_scene = fh.read()
    if registry is None:
        errors.append("ModeGMapSupportRegistry.cs 不存在")
    if entry is None:
        errors.append("ModeGEntry.cs 不存在")

    if registry is not None:
        checks = [
            ("ImmutableEntry",
             r"public struct SupportedMap",
             "SupportedMap 条目类型存在"),
            ("BaseSceneName",
             r"public readonly string baseSceneName",
             "exact 运行时场景名字段"),
            ("CombatSceneId",
             r"public readonly string combatSceneName",
             "exact 加载用场景 ID 字段"),
            ("RevisionField",
             r"public readonly string verificationRevision",
             "验证 revision 字段"),
            ("ExplicitStatus",
             r"public enum ModeGMapSupportStatus[\s\S]*?NotVerified[\s\S]*?Verified"
             r"[\s\S]*?public readonly ModeGMapSupportStatus status",
             "地图资格必须有显式 NotVerified/Verified 状态"),
            ("MapSelectionSource",
             r"BossRushMapConfig\[\] configs[\s\S]{0,180}?ModBehaviour\.GetAllMapConfigs\(\)"
             r"[\s\S]{0,1600}?ModeGMapSupportStatus\.Verified,",
             "全部 Verified 地图必须直接来自地图选择 UI 配置"),
            ("VerifiedRecordGate",
             r"private static bool IsRecordVerified\(SupportedMap map\)"
             r"[\s\S]*?map\.status == ModeGMapSupportStatus\.Verified"
             r"[\s\S]*?CurrentVerificationRevision"
             r"[\s\S]*?safetyTriad",
             "Verified 查询须同时验证状态/revision/风险与安全三元组"),
            ("RevisionFromAvailability",
             r"ModeGAvailability\.CurrentVerificationRevision",
             "revision 取自 Availability 冻结值"),
            ("ExactPairMatch",
             r"string\.Equals\(maps\[i\]\.baseSceneName, baseScene, StringComparison\.Ordinal\)"
             r"[\s\S]*?string\.Equals\(maps\[i\]\.combatSceneName, combatScene, StringComparison\.Ordinal\)",
             "IsSupported 必须 sceneName+sceneID 双字段 exact Ordinal 匹配（禁单点冒充三元组）"),
            ("ReadOnlyRegistry",
             r"private static SupportedMap\[\] GetConfiguredMaps\(\)",
             "支持集合只从配置源生成，不开放运行时 Register"),
            ("CachedRegistry",
             r"private static SupportedMap\[\] _configuredMaps;"
             r"[\s\S]{0,600}?if \(_configuredMaps != null\) return _configuredMaps;"
             r"[\s\S]{0,2200}?_configuredMaps = maps\.ToArray\(\);",
             "交互热路径复用首次有效地图快照，不重复分配"),
            ("SelectedPairQuery",
             r"public static bool TryGetVerifiedPairForScene\(string sceneName, out string sceneId\)",
             "preview 冻结玩家实际选择地图的 Verified pair 查询存在"),
            # ---- 规格 §20 第 3 条要求的记录字段（实现缺失即 FAIL）----
            ("DeathBehaviorField",
             r"deathBehavior|DeathBehavior",
             "每条记录须有 death behavior 字段（规格 §20 第 3 条）"),
            ("RiskKeyField",
             r"[Rr]iskKey|riskLocKey|风险",
             "每条记录须有双语死亡风险 key（规格 §20 第 3 条）"),
            ("SafetyTriadField",
             r"[Ss]afety[Tt]riad|安全三元组",
             "每条记录须冻结安全三元组（规格 §20 第 3 条）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, registry):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if entry is not None:
        if "ModeGMapSupportRegistry.TryGetVerifiedPairForScene" not in entry:
            errors.append("[EntryFreezesVerifiedPair] preview 未从当前 active scene 冻结 Verified scene pair")
        if not re.search(
                r"GetActiveScene\(\)\.name[\s\S]{0,180}?"
                r"if \(!ModeGMapSupportRegistry\.TryGetVerifiedPairForScene"
                r"[\s\S]{0,300}?return null;",
                entry):
            errors.append("[EntryRejectsNoVerifiedMap] 当前场景不在地图 UI 配置时 preview 未直接 fail-closed")
        if "verificationRevision" not in entry:
            errors.append("[EntryFreezesRevision] preview 未冻结 verificationRevision")

    # 带局切图必须以 SceneChanged 显式终局后再关停。
    # 历史实现放在 ModeGRuntimeModule.OnSceneLoaded override 里，但 host 注册的是空壳实例
    # （真实 run 实例由 ModeGEntry 自建且从不注册），该 override 的 _state==null 早返使其永不可达，
    # 终局原因被 Dispose 兜底成 ModDestroyed。现由 ModeRuntimeHooks 的场景清理路径承担。
    if "OnSceneLoaded(SceneRuntimeContext context)" in runtime_scene:
        errors.append("[RuntimeSceneOverrideUnreachable] "
                      "ModeGRuntimeModule 不得实现 OnSceneLoaded：host 注册的是空壳实例，"
                      "该回调不可达；带局切图终局应走 ModeRuntimeHooks")

    mode_runtime_hooks = ""
    if os.path.exists(MODE_RUNTIME_HOOKS):
        with open(MODE_RUNTIME_HOOKS, "r", encoding="utf-8", errors="replace") as fh:
            mode_runtime_hooks = fh.read()
    if not re.search(
            r"if \(modeGActive\)[\s\S]{0,400}?"
            r"modeGRuntime\.End\(ModeGExitReason\.SceneChanged\)"
            r"[\s\S]{0,200}?ShutdownModeG\(\);",
            mode_runtime_hooks):
        errors.append("[RuntimeFrozenPair] 带局切图未先显式 End(SceneChanged) 再 ShutdownModeG")

    integration_cleanup = ""
    if os.path.exists(INTEGRATION_CLEANUP):
        with open(INTEGRATION_CLEANUP, "r", encoding="utf-8", errors="replace") as fh:
            integration_cleanup = fh.read()
    if "ModeGMapSupportRegistry.ResetStaticCaches();" not in integration_cleanup:
        errors.append("[MapCacheCleanup] Mod runtime 销毁时未释放 Mode G 地图配置快照")

    if errors:
        print("ModeGMapSupportGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGMapSupportGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
