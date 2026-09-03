#!/usr/bin/env python3
"""
ModeHCommandCompatibilityGuard — 口令兼容矩阵守卫（设计提案 §17.6.4、§26.1）。

不变式：
- 兼容矩阵键含 effectId（不是 commandId 整体），effectId 命名固定为
  &lt;commandId&gt;.&lt;controlPointId&gt;；
- 口令级状态由 effect 派生，四态齐全且存在 PartiallyVerified；
- 每个生产 stable key 至少 3 条可用通用口令，否则内容不可用；
- ReportOnly / Unavailable 不进入选择 UI，也不进入赔率；
- 未通过 effect 不进入候选卡文案（只由已通过 effect 生成）；
- 每个 adapter 都有 Apply/Reassert/Restore/Validate 四段、原值快照、
  CommandReassertIntervalSeconds = 0.1 常量，且所有终止路径都会还原；
- 自结算 effect 对任何 key 恒 VerifiedBehavior，但不得据此把整条口令标成 VerifiedBehavior。
"""
import io
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

REGISTRY = os.path.join(REPO_ROOT, "ModeH", "ModeHCommandCompatibilityRegistry.cs")
ADAPTERS = os.path.join(REPO_ROOT, "ModeH", "ModeHCommandAdapters.cs")
CONTROLLER = os.path.join(REPO_ROOT, "ModeH", "ModeHCommandController.cs")
CONFIG = os.path.join(REPO_ROOT, "ModeH", "ModeHConfig.cs")
STATE_MODEL = os.path.join(REPO_ROOT, "ModeH", "ModeHStateModel.cs")
COMPAT_JSON = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH", "CommandCompatibility.json")
COMMANDS_JSON = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH", "Commands.json")
CERTIFICATION = os.path.join(REPO_ROOT, "ModeH", "ModeHProductionCertification.cs")
PROBE = os.path.join(REPO_ROOT, "ModeH", "ModeHCommandCertificationProbe.cs")


def load_json(path):
    if not os.path.exists(path):
        return None
    with io.open(path, "r", encoding="utf-8") as fh:
        try:
            return json.load(fh)
        except ValueError:
            return None


def main():
    errors = []

    registry = read_text(REGISTRY)
    if registry is None:
        print("ModeHCommandCompatibilityGuard: FAIL (1 errors)")
        print("  - [File] 缺少 ModeH/ModeHCommandCompatibilityRegistry.cs")
        return 1
    code = strip_cs_comments(registry)

    checks = [
        (r"public static ModeHCommandCompatibilityStatus GetEffectStatus\(string stableKey, string effectId\)",
         "逐 effect 状态查询"),
        (r"public static ModeHCommandCompatibilityStatus GetCommandStatus\(string stableKey, string commandId\)",
         "口令级状态由 effect 派生"),
        (r"if \(passed == effectIds\.Count\) return ModeHCommandCompatibilityStatus\.VerifiedBehavior;",
         "全部通过才是 VerifiedBehavior"),
        (r"if \(passed > 0\) return ModeHCommandCompatibilityStatus\.PartiallyVerified;",
         "部分通过为 PartiallyVerified"),
        (r"if \(unavailable == effectIds\.Count\) return ModeHCommandCompatibilityStatus\.Unavailable;",
         "控制点缺失为 Unavailable"),
        (r"return ModeHCommandCompatibilityStatus\.ReportOnly;", "其余为 ReportOnly"),
        (r"public static bool IsCommandSelectable\(string stableKey, string commandId\)",
         "选择 UI 门"),
        (r"public static List<string> GetVerifiedEffectIds\(", "候选卡文案只用已通过 effect"),
        (r"public static bool MeetsCommandGate\(string stableKey\)", "每 key 至少 3 条可用通用口令"),
        (r"ModeHConfig\.MinUsableCommonCommandsPerKey", "引用 3 条门槛常量"),
        (r"public static void BindBuildSignature\(", "按三签名绑定并在变化时清空实测结果"),
        (r"_selfSettledEffectIds", "自结算 effect 集合"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Registry] 不满足: " + desc)

    # 必须有实际生产写入者；只有矩阵接口和 gate 的形状，无法发现全池只有一条口令可用。
    certification = strip_cs_comments(read_text(CERTIFICATION) or "")
    probe = strip_cs_comments(read_text(PROBE) or "")
    for token in ("new ModeHCommandCertificationProbe(scavHandle, wolfHandle)",
                  "_commandProbe.Run(stableKey, map, keyDeadline)",
                  "yield return probe.Current", "ReleaseDiagnosticPair();",
                  "RestoreCertificationEffects(report.records)",
                  "EvaluateThreshold(cached.passedStableKeys)",
                  '_lastError ?? "certification_threshold_not_met"'):
        if token not in certification:
            errors.append("[Certification] 生产口令采样/缓存恢复/诊断漏接线: " + token)
    run = certification.split("internal IEnumerator Run(", 1)[-1].split("internal void Cancel()", 1)[0]
    if not (0 <= run.find("BindBuildSignature(") < run.find("yield return CertifyKey(")):
        errors.append("[Certification] 必须在逐 key 测量前绑定签名，不能测完后清空矩阵")
    for token in ("_scavAdapter.ApplyEffects(", "_wolfAdapter.ApplyEffects(",
                  "yield return null;", "held.IntersectWith(_scavAdapter.Validate())",
                  "held.IntersectWith(_wolfAdapter.Validate())", "_scavAdapter.Tick(delta, null)",
                  "_wolfAdapter.Tick(delta, null)", "samples < 3", "effect.Restore",
                  "scavAi.isActiveAndEnabled", "wolfAi.isActiveAndEnabled",
                  "certification_command_restore:", "ModeHCommandCompatibilityRegistry.RecordEffectStatus(",
                  "finally", "_scavAdapter.Restore();", "_wolfAdapter.Restore();"):
        if token not in probe:
            errors.append("[Probe] 缺少真实双角色跨帧采样及清理: " + token)
    sampling = probe.split("while (elapsed < sampleWindow || samples < 3)", 1)[-1]
    if not (0 <= sampling.find("yield return null;") < sampling.find("held.IntersectWith(")
            < sampling.find("_scavAdapter.Tick(")):
        errors.append("[Probe] 必须跨帧后先读再重申，不得用刚写入的值自证成功")
    read_field = probe.split("private static object ReadField(", 1)[-1].split("public void Dispose()", 1)[0]
    if any('case "' + point + '"' in read_field for point in
           ("moveToPos", "nextReleaseSkillTimeMarker", "searchedEnemy", "setNoticedToTarget")):
        errors.append("[Probe] 未接目标/路径/技能遥测的效果不能伪装为字段保持证据")
    for token in ("RestoreCertificationEffects(", "ClearStableKey(record.stableKey)",
                  "record.status != (int)ModeHCertificationStatus.Passed",
                  "knownEffects.Contains(effect.entryId)", 'effect.entryKind != "effect"',
                  "effect.status > (int)ModeHCommandCompatibilityStatus.Unavailable"):
        if token not in code:
            errors.append("[Registry] 缓存必须恢复合法逐 effect 证据: " + token)

    # 选择门只接受 VerifiedBehavior / PartiallyVerified
    selectable = re.search(
        r"public static bool IsCommandSelectable\([\s\S]{0,500}?\n        \}", code)
    if selectable:
        body = selectable.group(0)
        if "ReportOnly" in body or "Unavailable" in body:
            errors.append("[Registry] 选择门不得接受 ReportOnly / Unavailable")

    # 自结算不得整条标为 VerifiedBehavior
    effect_status = re.search(
        r"public static ModeHCommandCompatibilityStatus GetEffectStatus\([\s\S]{0,900}?\n        \}", code)
    if effect_status and "_selfSettledEffectIds" not in effect_status.group(0):
        errors.append("[Registry] 自结算 effect 必须在 effect 级返回 VerifiedBehavior")
    command_status = re.search(
        r"public static ModeHCommandCompatibilityStatus GetCommandStatus\([\s\S]{0,900}?\n        \}", code)
    if command_status and "_selfSettledEffectIds" in command_status.group(0):
        errors.append("[Registry] 口令级状态不得直接读自结算集合（必须逐 effect 派生）")

    adapters = read_text(ADAPTERS)
    if adapters is None:
        errors.append("[File] 缺少 ModeH/ModeHCommandAdapters.cs")
    else:
        acode = strip_cs_comments(adapters)
        for pattern, desc in [
            (r"public bool Apply\(", "Apply"),
            (r"public void Reassert\(", "Reassert"),
            (r"public void Restore\(\)", "Restore"),
            (r"public List<string> Validate\(\)", "Validate"),
        ]:
            if not re.search(pattern, acode):
                errors.append("[Adapters] 缺少四段之一: " + desc)
        if not re.search(r"_reassertAccumulator < ModeHConfig\.CommandReassertIntervalSeconds", acode):
            errors.append("[Adapters] 重申间隔必须使用冻结常量")
        if not re.search(r"if \(_windowRemaining <= 0f\)[\s\S]{0,200}?Restore\(\);", acode):
            errors.append("[Adapters] 窗口结束必须自动还原")

    controller = read_text(CONTROLLER)
    if controller is None:
        errors.append("[File] 缺少 ModeH/ModeHCommandController.cs")
    else:
        ccode = strip_cs_comments(controller)
        cchecks = [
            (r"public bool TryRingBell\(", "拍铃入口"),
            (r"_bellUsesRemaining--;\s*\n\s*_bellConsumed = true;", "拍铃 CAS 先消耗后应用"),
            (r"command_bell_consumed", "重复拍铃拒绝"),
            (r"command_signature_owner_absent", "招牌口令持有者不在场拒绝"),
            (r"spec\.RequiresRelayEntered && !relayEntered", "handoff 需要接力已登场"),
            (r"public void RestoreAll\(\)", "统一幂等还原入口"),
            (r"ModeHCommandCompatibilityRegistry\.IsCommandSelectable", "可选列表只取可用口令"),
        ]
        for pattern, desc in cchecks:
            if not re.search(pattern, ccode):
                errors.append("[Controller] 不满足: " + desc)

    config = read_text(CONFIG)
    if config and not re.search(r"public const float CommandReassertIntervalSeconds = 0\.1f;", config):
        errors.append("[Config] 重申间隔未冻结为 0.1 秒")

    model = read_text(STATE_MODEL)
    if model and not re.search(r"PartiallyVerified = 4", strip_cs_comments(model)):
        errors.append("[Model] 兼容状态缺少 PartiallyVerified")

    compat = load_json(COMPAT_JSON)
    commands = load_json(COMMANDS_JSON)
    if compat is None:
        errors.append("[Data] 缺少或无法解析 CommandCompatibility.json")
    elif commands is not None:
        catalog_ids = set()
        for entry in compat.get("effectCatalog") or []:
            effect_id = entry.get("effectId")
            command_id = entry.get("commandId")
            control_point = entry.get("controlPointId")
            if not effect_id or not command_id or not control_point:
                errors.append("[Data] effectCatalog 条目字段缺失")
                continue
            expected = "{}.{}".format(command_id, control_point)
            if effect_id != expected and not effect_id.startswith(command_id + "."):
                errors.append("[Data] effectId 命名必须是 <commandId>.<controlPointId>: " + effect_id)
            catalog_ids.add(effect_id)

        used = set()
        for section in ("commonCommands", "signatureCommands"):
            for entry in commands.get(section) or []:
                for effect in entry.get("effects") or []:
                    if effect.get("effectId"):
                        used.add(effect["effectId"])
        missing = used - catalog_ids
        if missing:
            errors.append("[Data] 兼容矩阵未覆盖 effect: " + str(sorted(missing)))

        self_settled = set(compat.get("selfSettledEffects") or [])
        if "steady.coward_mitigation" not in self_settled:
            errors.append("[Data] steady.coward_mitigation 必须登记为 Mode H 自结算")

    if errors:
        print("ModeHCommandCompatibilityGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHCommandCompatibilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
