#!/usr/bin/env python3
"""
ModeHPresetEligibilityGuard — Mode H 选手资格与生产目录守卫（设计提案 §17.2、§26.1）。

静态目录不变式（本步已实现）：
- BossProfiles.json 自洽 contentSignature；
- 签名生产目录严格 8 至 12 个唯一 stable key，productionOrder 唯一；
- 五种公开原型全覆盖，且每种原型至少两名候选；
- 每条档案恰好一个底色 + （怪癖或异常之一，互斥）；
- 招牌口令、看台表演模式为冻结 ID；
- 硬排除名单包含 managed Boss、特殊无名预设与 Character_Ming；
- ModeHProfileRegistry 执行上述审计并 fail-closed。

运行时认证不变式：
- 逐 key 用同一审计 preset 的两个独立 clone（scav/wolf）做双向敌对与规范死亡诊断；
- 报告带 game/mod/content 三签名，按四签名缓存并命中才跳过逐 key 诊断；
- 缓存不得跳过 arena isolation lease、spectator lease 与地图点位审计；
- 存在“强制重新认证”入口；
- isBoss/team/vehicle/showName/canDie/附件/managed key 过滤存在，
  EnemyPresetInfo 未被当作完整资格；
- 生产 registry 只物化当前 runtime Passed key；
- 除编译期 dev harness 外不存在独立样机入口。
"""
import io
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_canonical_json import content_signature  # noqa: E402
from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

PROFILES_JSON = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH", "BossProfiles.json")
PROFILE_REGISTRY = os.path.join(REPO_ROOT, "ModeH", "ModeHProfileRegistry.cs")
CERTIFICATION = os.path.join(REPO_ROOT, "ModeH", "ModeHProductionCertification.cs")
PRESET_REGISTRY = os.path.join(REPO_ROOT, "ModeH", "ModeHPresetRegistry.cs")
AVAILABILITY = os.path.join(REPO_ROOT, "ModeH", "ModeHAvailability.cs")
MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
CONFIG = os.path.join(REPO_ROOT, "ModeH", "ModeHConfig.cs")

ARCHETYPES = ["assault", "finisher", "ranged", "sustain", "tank"]
TEMPERAMENTS = ["aggressive", "bulwark", "cautious", "hunter", "pack", "trickster"]
QUIRKS = ["center_keeper", "clutch", "protect_sub", "reload_first", "revenge",
          "skill_saver", "slow_start", "soft_target"]
ANOMALIES = ["blood", "crowd", "error", "strong"]
SIGNATURE_COMMANDS = ["anchor", "handoff", "last_mag", "together", "weakness"]
STANDIN_PATTERNS = ["anchor_stand", "erratic_dart", "gate_pace", "rail_charge",
                    "slow_circle", "wall_hug"]
REQUIRED_EXCLUSIONS = ["Character_Ming", "Cname_Boss_Red", "Cname_Boss_Blue"]

MIN_PRODUCTION = 8
MAX_PRODUCTION = 12


def main():
    errors = []

    if not os.path.exists(PROFILES_JSON):
        print("ModeHPresetEligibilityGuard: FAIL (1 errors)")
        print("  - 缺少 Assets/Data/ModeH/BossProfiles.json")
        return 1

    with io.open(PROFILES_JSON, "r", encoding="utf-8") as fh:
        try:
            document = json.load(fh)
        except ValueError as exc:
            print("ModeHPresetEligibilityGuard: FAIL (1 errors)")
            print("  - BossProfiles.json 解析失败: {}".format(exc))
            return 1

    if document.get("schemaVersion") != 1:
        errors.append("[Schema] schemaVersion 必须为 1")
    declared = document.get("contentSignature", "")
    computed = content_signature(document)
    if declared != computed:
        errors.append("[Signature] contentSignature 不自洽: 声明={} 计算={}".format(
            declared or "(空)", computed))

    templates = document.get("profileTemplates") or []
    if not templates:
        errors.append("[Catalog] profileTemplates 不得为空")

    template_ids = set()
    stable_keys = set()
    production = []
    orders = set()

    for template in templates:
        template_id = template.get("profileTemplateId")
        if not template_id or template_id in template_ids:
            errors.append("[Template] profileTemplateId 缺失或重复: " + str(template_id))
            continue
        template_ids.add(template_id)

        stable_key = template.get("stableKey")
        if not stable_key or stable_key in stable_keys:
            errors.append("[Template] {} stableKey 缺失或重复".format(template_id))
        else:
            stable_keys.add(stable_key)

        if template.get("archetypeId") not in ARCHETYPES:
            errors.append("[Template] {} 原型非法".format(template_id))
        if template.get("temperamentId") not in TEMPERAMENTS:
            errors.append("[Template] {} 底色非法".format(template_id))

        quirk = template.get("quirkId") or ""
        anomaly = template.get("anomalyId") or ""
        if quirk and anomaly:
            errors.append("[Template] {} 怪癖与异常互斥，不能同时存在".format(template_id))
        if quirk and quirk not in QUIRKS:
            errors.append("[Template] {} 怪癖非法: {}".format(template_id, quirk))
        if anomaly and anomaly not in ANOMALIES:
            errors.append("[Template] {} 异常非法: {}".format(template_id, anomaly))
        if not quirk and not anomaly:
            errors.append("[Template] {} 必须有一个怪癖或异常".format(template_id))

        if template.get("signatureCommandId") not in SIGNATURE_COMMANDS:
            errors.append("[Template] {} 招牌口令非法".format(template_id))
        if template.get("standInPatternId") not in STANDIN_PATTERNS:
            errors.append("[Template] {} 看台表演模式非法".format(template_id))

        threat = template.get("threatScore")
        if not isinstance(threat, int) or threat <= 0:
            errors.append("[Template] {} threatScore 必须为正整数".format(template_id))

        if template.get("productionCandidate"):
            order = template.get("productionOrder")
            if not isinstance(order, int) or order <= 0 or order in orders:
                errors.append("[Template] {} productionOrder 非法或重复".format(template_id))
            else:
                orders.add(order)
            production.append(template)

    if len(production) < MIN_PRODUCTION:
        errors.append("[Catalog] 生产目录少于 {} 个: {}".format(MIN_PRODUCTION, len(production)))
    if len(production) > MAX_PRODUCTION:
        errors.append("[Catalog] 生产目录多于 {} 个: {}".format(MAX_PRODUCTION, len(production)))

    by_archetype = {}
    for template in production:
        by_archetype.setdefault(template.get("archetypeId"), []).append(template)
    for archetype in ARCHETYPES:
        count = len(by_archetype.get(archetype, []))
        if count == 0:
            errors.append("[Coverage] 生产目录未覆盖原型: " + archetype)
        elif count < 2:
            errors.append("[Coverage] 原型 {} 只有 {} 名候选，不足两名".format(archetype, count))

    excluded = document.get("excludedStableKeys") or []
    for key in REQUIRED_EXCLUSIONS:
        if key not in excluded:
            errors.append("[Exclusion] 硬排除名单缺少: " + key)
    for template in production:
        if template.get("stableKey") in excluded:
            errors.append("[Exclusion] 生产候选同时出现在排除名单: " + str(template.get("stableKey")))

    registry = read_text(PROFILE_REGISTRY)
    if registry is None:
        errors.append("[Code] 缺少 ModeH/ModeHProfileRegistry.cs")
    else:
        checks = [
            (r"profile_production_below_min", "生产目录下限 fail-closed"),
            (r"profile_production_above_max", "生产目录上限 fail-closed"),
            (r"profile_archetype_coverage", "原型覆盖 fail-closed"),
            (r"profile_archetype_thin", "每原型至少两名候选"),
            (r"profile_production_key_excluded", "生产候选不得命中排除名单"),
            (r"MinProductionCandidateCount", "引用 8 个下限常量"),
            (r"MaxProductionCandidateCount", "引用 12 个上限常量"),
            (r"public static bool IsExcludedStableKey\(string stableKey\)", "硬排除查询入口"),
            (r"public static bool IsStableTemperament\(", "稳定底色判定入口"),
        ]
        for pattern, desc in checks:
            if not re.search(pattern, registry):
                errors.append("[Code] 不满足: " + desc)

    config = read_text(CONFIG)
    if config:
        if not re.search(r"public const int MinProductionCandidateCount = 8;", config):
            errors.append("[Config] 生产目录下限未冻结为 8")
        if not re.search(r"public const int MaxProductionCandidateCount = 12;", config):
            errors.append("[Config] 生产目录上限未冻结为 12")
        if not re.search(r"public const float CertificationPerKeyTimeoutSeconds = 15f;", config):
            errors.append("[Config] 逐 key 认证上限未冻结为 15 秒")
        if not re.search(r"public const float CertificationPoolTimeoutSeconds = 180f;", config):
            errors.append("[Config] 全池认证上限未冻结为 180 秒")

    certification = read_text(CERTIFICATION)
    if certification is None:
        errors.append("[File] 缺少 ModeH/ModeHProductionCertification.cs")
    else:
        ccode = strip_cs_comments(certification)
        cert_checks = [
            (r"Teams\.scav, map\.StagingPos", "scav clone 在 staging 创建"),
            (r"Teams\.wolf, map\.StagingPos", "wolf clone 在 staging 创建"),
            (r"Team\.IsEnemy\(Teams\.scav, Teams\.wolf\)", "双向敌对核对"),
            (r"certification_team_drift", "下一帧阵营稳定核对"),
            (r"handle\.Health\.Hurt\(damage\)", "受控伤害必须走官方死亡链"),
            (r"TryControlledKill\(wolfHandle, scavHandle\.Character", "wolf 受击必须带对侧诊断来源"),
            (r"TryControlledKill\(scavHandle, wolfHandle\.Character", "scav 受击必须带对侧诊断来源"),
            (r"new DamageInfo\(attacker\)", "完整伤害必须带非空 NPC 来源"),
            (r"attacker == null \|\| attacker == handle\.Character \|\| attacker\.IsMainCharacter", "不得用空、自身或玩家来源伪造认证击杀"),
            (r"handle\.Health\.IsDead && observedHurt && observedDeath", "同时确认真实死亡与两个事件"),
            (r"!handle\.Health\.CanDieIfNotRaidMap", "校验生成角色在非 Raid 图可死亡"),
            (r"OnHurtEvent\.RemoveListener\(onHurt\)", "认证伤害监听必须退订"),
            (r"OnDeadEvent\.RemoveListener\(onDead\)", "认证死亡监听必须退订"),
            (r"CertificationPerKeyTimeoutSeconds", "逐 key 15 秒上限"),
            (r"CertificationPoolTimeoutSeconds", "全池 180 秒上限"),
            (r"certification_pool_timeout", "全池超时条目标 Rejected"),
            (r"TryUseCachedReport\(int slotGeneration\)", "四签名缓存命中入口"),
            (r"TryGetCertificationCache\(game, mod, content, slotGeneration\)", "四签名键控"),
            (r"internal static bool InvalidateCache\(out string error\)", "强制重新认证入口"),
            (r"PassesStaticAudit\(CharacterRandomPreset preset, out string failureReasonId\)",
             "回查原版 preset 做资格审计"),
            (r"audit_not_boss", "isBoss 过滤"),
            (r"audit_team_forbidden", "team 过滤"),
            (r"audit_is_vehicle", "载具过滤"),
            (r"audit_cannot_die", "非 Raid 图可死亡过滤"),
            (r"audit_special_attachments", "特殊附件过滤"),
            (r"audit_excluded_key", "managed / 排除 key 过滤"),
            (r"IsDiagnosticRegistryEmpty", "正式开战前 diagnostic registry 为空"),
        ]
        for pattern, desc in cert_checks:
            if not re.search(pattern, ccode):
                errors.append("[Certification] 不满足: " + desc)

        bridge = read_text(os.path.join(MODEH_DIR, "ModeHSpawnBridge.cs")) or ""
        if "clone.canDieIfNotRaidMap = true;" not in strip_cs_comments(bridge):
            errors.append("[Certification] 独立 clone 必须允许非 Raid 图死亡，不得改共享预设")
        if "!preset.canDieIfNotRaidMap" in ccode or "handle.Health.SetHealth(0f)" in ccode:
            errors.append("[Certification] 不得按原预设地图保护拒绝克隆，或用 SetHealth 冒充死亡")
        if "new DamageInfo((CharacterMainControl)null)" in ccode:
            errors.append("[Certification] 空伤害来源会触发第三方死亡订阅者空引用")

        # 缓存只跳过逐 key 诊断：不得出现跳过 lease/点位审计的分支
        for forbidden in ["SkipIsolationLease", "SkipSpectatorLease", "SkipMapAudit"]:
            if forbidden in ccode:
                errors.append("[Certification] 缓存不得跳过: " + forbidden)

        # EnemyPresetInfo 不得单独作为资格判断
        if "EnemyPresetInfo" in ccode:
            errors.append("[Certification] 不得用 EnemyPresetInfo 判定资格，必须回查 CharacterRandomPreset")

    preset_registry = read_text(PRESET_REGISTRY)
    if preset_registry is None:
        errors.append("[File] 缺少 ModeH/ModeHPresetRegistry.cs")
    else:
        pcode = strip_cs_comments(preset_registry)
        if not re.search(r"record\.status != \(int\)ModeHCertificationStatus\.Passed", pcode):
            errors.append("[PresetRegistry] 只能物化 Passed key")
        if not re.search(r"template == null \|\| !template\.ProductionCandidate", pcode):
            errors.append("[PresetRegistry] 必须同时满足静态签名目录")
        if not re.search(r"if \(!IsProductionKey\(stableKey\)\) return null;", pcode):
            errors.append("[PresetRegistry] 不在生产池的 key 不得取到 preset")

    availability = read_text(AVAILABILITY)
    if availability is not None:
        acode = strip_cs_comments(availability)
        if not re.search(r"public const bool AllowDevControlPointHarness = false;", acode):
            errors.append("[Harness] 编译期 harness 门必须恒 false")

    # 除编译期 harness 外不得存在独立样机入口
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        c = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        for forbidden in ["AllowDevTestEntry", "TryStartModeHPrototype", "ModeHSandbox"]:
            if forbidden in c:
                errors.append("[Prototype] {} 不得提供独立样机入口: {}".format(name, forbidden))

    if errors:
        print("ModeHPresetEligibilityGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHPresetEligibilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
