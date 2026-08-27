#!/usr/bin/env python3
"""
ModeGPerformanceGuard — Mode G 性能守卫（规格 §20 第 28 条）。

不变式：
- OnHurt 热路径：exact Health 字典 O(1) 早返、零分配、不 GetComponent；
  HandleOnHurt 先 _state/IsCombatActive/IsRegisteredBossHealth 三级早返；
- OnDead staging 分支：未激活只读静态 bool 快速早返（不建集合、不分配），
  fail-open=false；
- Starting/波前预分配：pendingActivationHandles 容量构造、_healthScratch
  Clear 复用；遥测缓存容量常量 32/32/64/3/3（+128）预分配构造；
- HUD <=4Hz：RefreshIntervalSeconds=0.25f 节流 + 文本仅 Ordinal 变化赋值；
  StringBuilder(128) 预分配；
- 奖励尖峰：strict materializer 每帧至多 1 件 InstantiateSync（类体内
  恰好一次调用，nextIndex 单调推进）；
- 展示 bundle 每 runtime 至多一次 LoadFromFile（_loadAttempted 闸门 +
  文件内 LoadFromFile 恰好一处）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TELEMETRY = os.path.join(REPO_ROOT, "ModeG", "ModeGCombatTelemetry.cs")
ONDEAD = os.path.join(REPO_ROOT, "Patches", "Combat", "CharacterOnDeadPatch.cs")
HUD = os.path.join(REPO_ROOT, "ModeG", "ModeGHUD.cs")
CACHE = os.path.join(REPO_ROOT, "ModeG", "ModeGPresentationAssetCache.cs")
RUNTIME = os.path.join(REPO_ROOT, "ModeG", "ModeGRuntimeModule.cs")
MATERIALIZER = os.path.join(
    REPO_ROOT, "LootAndRewards", "VictoryRewardShadowCrateController.cs")


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def strip_comments(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def main():
    errors = []
    telemetry = read(TELEMETRY, errors)
    onde = read(ONDEAD, errors)
    hud = read(HUD, errors)
    cache = read(CACHE, errors)
    runtime = read(RUNTIME, errors)
    mat = read(MATERIALIZER, errors)

    if telemetry:
        checks = [
            ("OnHurtContractNote",
             r"OnHurt handler 先 exact Health 字典 O\(1\) 早返、零分配、不 GetComponent",
             "OnHurt 契约注释（O(1)/零分配/不 GetComponent）"),
            ("OnHurtEarlyReturns",
             r"private void HandleOnHurt\(Health health, DamageInfo info\)"
             r"[\s\S]{0,200}?if \(_state == null \|\| !_state\.IsCombatActive\) return;"
             r"[\s\S]{0,80}?if \(health == null\) return;"
             r"[\s\S]{0,80}?if \(!_state\.IsRegisteredBossHealth\(health\)\) return;",
             "HandleOnHurt 三级早返（未激活/空/未登记）"),
            ("CapacityConstants",
             r"public const int AmmoCacheCapacity = 32;"
             r"[\s\S]*?public const int ProjectileThreatCountCap = 32;"
             r"[\s\S]*?public const int WeaponFamilyCacheCapacity = 64;"
             r"[\s\S]*?public const int BossCacheCapacity = 3;"
             r"[\s\S]*?public const int NamedAmmoCapacity = 3;",
             "预分配容量常量 32/32/64/3/3 冻结"),
            ("PreallocatedCaches",
             r"new Dictionary<int, double>\(AmmoCacheCapacity\)"
             r"[\s\S]*?new Dictionary<int, ModeGDirectDamageClass>\(WeaponFamilyCacheCapacity\)"
             r"[\s\S]*?new Dictionary<int, BulletThreatProfile>\(AmmoCacheCapacity\)"
             r"[\s\S]*?new Dictionary<Health, float>\(BossCacheCapacity\)",
             "遥测字典按容量预分配"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, telemetry):
                errors.append("[{}] 不满足: {}".format(name, desc))

        # HandleOnHurt 方法体内禁重查符号（热路径零分配）
        m = re.search(r"private void HandleOnHurt\(Health health, DamageInfo info\)"
                      r"([\s\S]*?)\n        #", telemetry)
        if m:
            body = strip_comments(m.group(1))
            for token in ["GetComponent", "FindObjectsOfType", "FindObjectOfType"]:
                if token in body:
                    errors.append("[OnHurtAlloc] HandleOnHurt 含重查符号 {}".format(token))
        else:
            errors.append("[OnHurtBody] HandleOnHurt 方法体未找到")

    if onde:
        checks = [
            ("StaticBoolGate",
             r"if \(IsModeGSuppressionArmed\(\)(?: \|\| ModeHDeathSuppressionRegistry\.IsSuppressionArmed)?\)",
             "Prefix 顶部静态 bool 快速早返闸门（2026-08-28：Mode H 追加同形快门，Mode G 语义不变）"),
            ("StaticBoolNoThrow",
             r"private static bool IsModeGSuppressionArmed\(\)"
             r"[\s\S]{0,200}?return ModeGRuntimeGates\.IsModeGSuppressionActive;"
             r"[\s\S]{0,80}?catch[\s\S]{0,40}?return false;",
             "静态开关查询 no-throw（异常=未激活）"),
            ("NoAllocContractNote",
             r"先读静态 bool 快速早返，未激活时不建集合、不分配",
             "未激活零分配契约注释"),
            ("FailOpenFalse",
             r"任何异常 fail-open=false",
             "fail-open=false 契约注释"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, onde):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if hud:
        checks = [
            ("RefreshInterval",
             r"private const float RefreshIntervalSeconds = 0\.25f;",
             "HUD 刷新间隔 0.25s（<=4Hz）冻结"),
            ("ThrottleGate",
             r"if \(_refreshTimer < RefreshIntervalSeconds\) return;",
             "节流早返（未到间隔不刷新）"),
            ("ChangeOnlyAssign",
             r"if \(!string\.Equals\(text, _lastText, StringComparison\.Ordinal\)\)",
             "文本仅 Ordinal 变化时赋值"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, hud):
                errors.append("[{}] 不满足: {}".format(name, desc))

        m = re.search(r"new StringBuilder\((\d+)\)", hud)
        if not m:
            errors.append("[BuilderPreallocated] HUD StringBuilder 未预分配容量")
        elif int(m.group(1)) < 128:
            errors.append("[BuilderPreallocated] HUD StringBuilder 预分配容量 {} < 128".format(m.group(1)))

    if cache:
        checks = [
            ("BundleLoadGate",
             r"if \(_bundle != null\) return true;"
             r"[\s\S]{0,60}?if \(_loadAttempted\) return false;"
             r"[\s\S]{0,60}?_loadAttempted = true;",
             "bundle 每 runtime 至多一次 LoadFromFile（_loadAttempted 闸门）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, cache):
                errors.append("[{}] 不满足: {}".format(name, desc))
        count = len(re.findall(r"AssetBundle\.LoadFromFile\(", cache))
        if count != 1:
            errors.append("[BundleLoadCount] LoadFromFile 出现 {} 次（应为 1）".format(count))

    if runtime:
        checks = [
            ("HandleListCapacity",
             r"new List<ManagedBossRuntimeHandle>\(wave\.bossCount\)",
             "pendingActivationHandles 容量预分配"),
            ("HealthScratchReuse",
             r"_healthScratch\.Clear\(\);"
             r"[\s\S]{0,80}?_spawnTransaction\.CollectActiveBossHealth\(_healthScratch\);",
             "_healthScratch Clear 复用（不重复分配）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, runtime):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if mat:
        m = re.search(r"public sealed class ModeGRewardStrictMaterializer : MonoBehaviour"
                      r"([\s\S]*?)\n    \}", mat)
        if m:
            body = strip_comments(m.group(1))
            n = len(re.findall(r"ItemAssetsCollection\.InstantiateSync\(", body))
            if n != 1:
                errors.append("[RewardSpike] materializer InstantiateSync 出现 {} 次（应为 1）".format(n))
            if not re.search(r"nextIndex\+\+;", body):
                errors.append("[RewardSpike] materializer 缺 nextIndex 单调推进")
        else:
            errors.append("[MaterializerClass] ModeGRewardStrictMaterializer 类体未找到")

    if errors:
        print("ModeGPerformanceGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGPerformanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
