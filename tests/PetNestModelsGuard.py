#!/usr/bin/env python3
"""
PetNestModelsGuard — 遗种巢数据模型守卫（实施计划 步骤 1）。

不变式：
- PetNestModels.cs 是纯数据层：禁 `using UnityEngine`（及任何 UnityEngine.* 类型），
  DTO 必须能脱离宿主被 guard / JSON 层独立处理；
- 禁静态可变状态：DTO 只描述形状，所有权归 Service；
- 禁字段初始化器：容器由 Normalize() 统一兜底，避免两套真相；
- 每个带容器字段的 DTO 都要有 Normalize()；
- 存档 key / schemaVersion / 数值常量只能出现在 PetNestTuning.cs，
  模型层不得内联魔法数与 key 字面量；
- PetNestTuning.cs 只有常量，无逻辑、无可变静态字段；
- 血脉目录 fail-closed：查不到 preset 不进目录，且不经 ModBehaviour.Instance。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import read_petnest, report, strip_cs_comments  # noqa: E402

GUARD = "PetNestModelsGuard"

REQUIRED_DTOS = [
    "PetNestScarRecord",
    "PetNestTalentEntry",
    "PetNestAdultSnapshot",
    "PetNestPetRecord",
    "PetNestSoulLedgerEntry",
    "PetNestNestData",
    "PetNestExpeditionRecord",
    "PetNestExpeditionData",
    "PetNestMemorialEntry",
    "PetNestLineageStats",
    "PetNestMuseumData",
]

DTOS_WITH_CONTAINERS = [
    "PetNestPetRecord",
    "PetNestNestData",
    "PetNestExpeditionRecord",
    "PetNestExpeditionData",
    "PetNestMuseumData",
]


def check_models(errors):
    text = read_petnest("PetNestModels.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestModels.cs")
        return
    code = strip_cs_comments(text)

    # 1. 纯数据：禁 UnityEngine
    if re.search(r"^\s*using\s+UnityEngine", code, re.MULTILINE):
        errors.append("[纯数据] PetNestModels.cs 不得 using UnityEngine")
    if re.search(r"\bUnityEngine\.", code):
        errors.append("[纯数据] PetNestModels.cs 不得引用 UnityEngine.* 类型")
    for unity_type in ["Vector3", "Vector2", "GameObject", "MonoBehaviour", "Transform", "Color"]:
        if re.search(r"\b" + unity_type + r"\b", code):
            errors.append("[纯数据] PetNestModels.cs 不得出现 Unity 类型: " + unity_type)

    # 2. 禁静态可变状态
    for m in re.finditer(r"^\s*(?:public|internal|private|protected)\s+static\s+(?!readonly\b)(?!class\b)[\w<>\[\],\. ]+\s+\w+\s*[;=]",
                         code, re.MULTILINE):
        errors.append("[无状态] DTO 层不得有静态可变字段: " + m.group(0).strip())

    # 3. 禁字段初始化器（DTO 字段一律不带 = 初值）
    for m in re.finditer(r"^\s*public\s+(?!static\b)[\w<>\[\],\. ]+\s+\w+\s*=\s*", code, re.MULTILINE):
        errors.append("[初始化器] DTO 字段不得带初始化器: " + m.group(0).strip())

    # 4. DTO 齐全
    for dto in REQUIRED_DTOS:
        if not re.search(r"internal sealed class " + dto + r"\b", code):
            errors.append("[DTO] 缺少数据类: " + dto)

    # 5. 带容器的 DTO 必须有 Normalize()
    for dto in DTOS_WITH_CONTAINERS:
        block = re.search(r"internal sealed class " + dto + r"\b[\s\S]*?\n    \}", code)
        if block is None:
            continue
        if "public void Normalize()" not in block.group(0):
            errors.append("[兜底] " + dto + " 缺少 Normalize() 容器兜底")

    # 6. 存档 key / 魔法数不得内联在模型层
    if re.search(r'"BossRush_PetNest_', code):
        errors.append("[归位] 存档 key 字面量必须在 PetNestTuning.cs，模型层不得内联")

    # 7. 枚举取值稳定（序列化用 int，禁止调整既有值）
    for pattern, desc in [
        (r"InNest = 0", "PetNestPetState.InNest = 0"),
        (r"Deployed = 1", "PetNestPetState.Deployed = 1"),
        (r"OnExpedition = 2", "PetNestPetState.OnExpedition = 2"),
        (r"Downed = 3", "PetNestPetState.Downed = 3"),
        (r"Safe = 0", "PetNestRiskTier.Safe = 0"),
        (r"Rough = 1", "PetNestRiskTier.Rough = 1"),
        (r"Desperate = 2", "PetNestRiskTier.Desperate = 2"),
    ]:
        if not re.search(pattern, code):
            errors.append("[枚举稳定性] 缺少或改动了序列化取值: " + desc)


def check_tuning(errors):
    text = read_petnest("PetNestTuning.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestTuning.cs")
        return
    code = strip_cs_comments(text)

    if not re.search(r"internal static class PetNestTuning", code):
        errors.append("[Tuning] 必须是 internal static class PetNestTuning")

    # 只允许 const / static readonly；不得有可变静态字段与方法
    for m in re.finditer(r"^\s*internal\s+static\s+(?!readonly\b)(?!class\b)[\w<>\[\],\. ]+\s+\w+\s*[;=]",
                         code, re.MULTILINE):
        errors.append("[Tuning] 只允许 const / static readonly: " + m.group(0).strip())
    if re.search(r"^\s*(?:internal|public|private)\s+static\s+[\w<>\[\],\. ]+\s+\w+\s*\(", code, re.MULTILINE):
        errors.append("[Tuning] 数值常量单点不得含方法逻辑")

    # 三个存档 key + schemaVersion 必须在这里
    for pattern, desc in [
        (r'NestStorageKey = "BossRush_PetNest_Nest_v1"', "巢存档 key"),
        (r'ExpeditionStorageKey = "BossRush_PetNest_Expedition_v1"', "远征存档 key"),
        (r'MuseumStorageKey = "BossRush_PetNest_Museum_v1"', "博物馆存档 key"),
        (r"CurrentSchemaVersion = 1", "schema 版本"),
        (r'LocalizationPrefix = "BossRush_PetNest_"', "本地化前缀"),
        (r'EggLineageVariableKey = "PetNest_Lineage"', "蛋血脉 KV 键"),
    ]:
        if not re.search(pattern, code):
            errors.append("[Tuning] 缺少契约常量: " + desc)

    # 死亡率三档必须成文（远征出发前明示的数据来源）
    for pattern in ["DeathRateSafe", "DeathRateRough", "DeathRateDesperate"]:
        if pattern not in code:
            errors.append("[Tuning] 缺少远征死亡率常量: " + pattern)


def check_catalog(errors):
    text = read_petnest("PetNestLineageCatalog.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestLineageCatalog.cs")
        return
    code = strip_cs_comments(text)

    # 资格口径来自 Boss 过滤池 + 三个自定义 Boss
    if "GetFilteredEnemyPresets()" not in code:
        errors.append("[资格] 官方血脉必须来自 GetFilteredEnemyPresets() 过滤池")
    for const in ["DragonDescendantConfig.BOSS_NAME_KEY",
                  "DragonKingConfig.BossNameKey",
                  "PhantomWitchConfig.BossNameKey"]:
        if const not in code:
            errors.append("[资格] 缺少自定义 Boss 血脉常量: " + const)

    # 不得经 ModBehaviour.Instance（冻结基线）
    if "ModBehaviour.Instance" in code:
        errors.append("[门面] 血脉目录不得经 ModBehaviour.Instance，owner 必须显式传入")

    # fail-closed：查不到 preset 就不进目录
    if not re.search(r"if \(preset == null\) continue;", code):
        errors.append("[fail-closed] 官方血脉查不到 preset 必须跳过，不得回落")

    # 惰性构建 + 可作废
    if not re.search(r"internal static void EnsureBuilt\(ModBehaviour owner\)", code):
        errors.append("[构建] 缺少幂等入口 EnsureBuilt(ModBehaviour owner)")
    if not re.search(r"internal static void Invalidate\(\)", code):
        errors.append("[构建] 缺少目录作废入口 Invalidate()")

    # 元素推导与目的地元素
    if "DeriveElement" not in code:
        errors.append("[元素] 缺少 elementFactor_* 元素推导")
    if "GetDestinationElement" not in code:
        errors.append("[元素] 缺少远征目的地元素映射")


def main():
    errors = []
    check_models(errors)
    check_tuning(errors)
    check_catalog(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
