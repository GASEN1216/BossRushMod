#!/usr/bin/env python3
"""
ModeHRosterInvariantGuard — Mode H roster 不变式守卫（设计提案 §17.3、§26.1）。

不变式：
- matchStarter 必须是存活合同选手；
- matchRelay 只能是另一名存活合同选手或 Empty；
- 存活合同选手数为 1 时强制 matchRelay=Empty；
- 存活数为 0 时不得进入 LoadoutEditing；
- 合同角色只由 contractMainProfileId / contractSubProfileId 两个 ID 决定，
  不在 profile 或 roster 中复制 role 布尔；
- 角色状态只在 ModeHProfileDto.status，其他 DTO 不得复制 contractState；
- 五席试棚（§17.2）：覆盖五原型、至多一名异常、至少两名稳定底色、
  stableKey 与 profileId 均不重复、展示顺序由 runSeed 固定（关页不重抽）；
- 敌军计划（§17.5）：连续 MaxPlanCandidateAttempts 个候选失败才 TechnicalAbort，
  且 roster-level veto 保证始终至少保留一种合法排列。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments, read_modeh_group  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
STATE_MODEL = os.path.join(MODEH_DIR, "ModeHStateModel.cs")
DRAFT = os.path.join(MODEH_DIR, "ModeHDraftController.cs")
PLANNER = os.path.join(MODEH_DIR, "ModeHEncounterPlanner.cs")

FORBIDDEN_ROLE_FIELDS = [
    "isContractMain", "isContractSub", "isStarter", "isMatchStarter",
    "isMatchRelay", "contractState", "isMain", "isSub",
]


def check_draft(errors):
    """\u00a717.2 五席试棚的不变式都必须在 draft controller 内落地。"""
    source = read_text(DRAFT)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHDraftController.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"public static bool TryBuildDraft\(", "五席生成入口"),
        (r"ModeHConfig\.RequiredArchetypeCoverage", "五原型覆盖引用冻结常量"),
        (r"ModeHConfig\.MaxAnomalyCandidatesInDraft", "异常上限引用冻结常量"),
        (r"ModeHConfig\.MinStableTemperamentCandidatesInDraft", "稳定底色下限引用冻结常量"),
        (r"ModeHConfig\.DraftCandidateCount", "候选数引用冻结常量"),
        (r'failureReasonId = "draft_duplicate_stable_key";', "stableKey 去重"),
        (r'failureReasonId = "draft_duplicate_profile_id";', "profileId 去重"),
        (r'failureReasonId = "draft_archetype_uncovered:"', "原型未覆盖 fail-closed"),
        (r"ModeHSeedStream\.Create\(runSeed, ModeHSeedStream\.Domains\.Draft",
         "展示顺序由 runSeed 派生（关页不重抽）"),
        (r"public static bool TrySignContracts\(", "五选二签约入口"),
        (r'failureReasonId = "sign_duplicate_profile";', "主将与替补不得同人"),
        (r"public static bool TryAssignEchoDestinations\(", "落选三路分流入口"),
        (r"ModeHSeedStream\.Domains\.Echo", "分流使用固定 echo 种子"),
        (r"stream\.Shuffle\(remaining\);", "分流是一次 Fisher-Yates"),
        (r"ModeHStableIds\.EchoDestinationReturnEnemy", "回场签"),
        (r"ModeHStableIds\.EchoDestinationTransferCandidate", "候签"),
        (r"ModeHStableIds\.EchoDestinationRemoved", "撕票"),
        (r"public static bool IsRemovedProfile\(", "撕票不得暗中返场"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Draft] 不满足: " + desc)

    # 候选是公开档案，不得持有 Unity 对象引用
    for forbidden in ["CharacterRandomPreset", "CharacterMainControl", "GameObject", "UnityEngine"]:
        if re.search(r"\b{}\b".format(re.escape(forbidden)), code):
            errors.append("[Draft] 候选档案不得引用 Unity 对象: " + forbidden)

    # 抽取只能来自生产目录
    if "template.ProductionCandidate" not in code:
        errors.append("[Draft] 候选必须来自 productionCandidate=true 的签名目录")


def method_body(code, signature):
    """对去注释后的当前方法按花括号截取，避免被其他方法的同名约束替代。"""
    start = code.find(signature)
    if start < 0:
        return ""
    opening = code.find("{", start)
    depth = 1
    end = opening + 1
    while end < len(code) and depth:
        depth += (code[end] == "{") - (code[end] == "}")
        end += 1
    return code[start:end] if depth == 0 else ""


def check_planner_code(code, errors):
    """§17.5：主候选和确定性组合修复都必须通过相同的约束。"""

    checks = [
        (r"public static bool TryBuildPlan\(", "计划生成入口"),
        (r"candidateIndex < ModeHConfig\.MaxPlanCandidateAttempts", "候选重试上限来自冻结常量"),
        (r"public static bool HasLegalArrangement\(", "roster-level veto 入口"),
        (r'failureReasonId = "plan_roster_no_legal_arrangement";', "无合法排列才换候选"),
        (r"public static List<string> CollectLockedArchetypes\(", "硬封锁集合派生"),
        (r"ModeHSeedStream\.Domains\.EncounterPlan", "计划种子域固定"),
        (r"technicalRetrySequence", "重试序号进入种子派生"),
        (r'TryComputeObjectDigest\(plan, "planDigest"', "计划摘要排除自身字段"),
        (r"public static bool TryApplyRecon\(", "侦察入口"),
        (r'failureReasonId = "recon_already_consumed";', "侦察每场至多一次"),
        (r"corridor\.MinFillPercent", "威胁走廊下界参与审计"),
        (r"ModeHConfig\.ThreatBudgetTolerance", "预算 5%% 容差引用冻结常量"),
        (r'failureReasonId = "plan_batch_exceeds_cap";', "单批人数不得超同屏上限"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Planner] 不满足: " + desc)

    candidate = method_body(code, "private static ModeHMatchPlanDto BuildCandidate(")
    repair = method_body(code, "private static bool TryFindLegalRosterSelection(")
    select = method_body(code, "private static bool TrySelectUnits(")
    veto_body = method_body(code, "public static bool HasLegalArrangement(")
    matrix = method_body(code, "public static List<string> CollectLockedArchetypes(")

    def need(body, pattern, description):
        if not re.search(pattern, body, re.S):
            errors.append("[Planner] 不满足: " + description)

    # 原来的两个独立拒绝原因合并为一次修复入口；修复失败仍拒绝候选。
    need(candidate,
         r"if \(lockedArchetypes.Count >= ModeHStableIds.AllArchetypes.Length\s*"
         r"\|\| !HasLegalArrangement\(liveArchetypeIds, lockedArchetypes\)\)\s*\{",
         "封死五原型或当前合同无合法排列时必须进入修复/拒绝分支")
    need(candidate,
         r"if \(!TryFindLegalRosterSelection\(unitCount, enemyStableKeyPool, requiredCoreKey,\s*"
         r"corridor, condition, liveArchetypeIds, out repaired\)\)\s*\{\s*"
         r'failureReasonId = "plan_roster_no_legal_arrangement";\s*return null;\s*\}',
         "矩阵拒绝后的修复必须使用同一人数/池/核心/走廊/条件/合同，失败拒绝")
    need(candidate,
         r"units = repaired;\s*batchIndices = null;\s*"
         r"if \(!TryAssignBatches\(units, entryScript, corridor.SimultaneousCap, PickCoreIndex\(units\),\s*"
         r"out batchIndices, out failureReasonId\)\) return null;",
         "修复替换后必须重新校验同屏上限与分批")
    need(repair,
         r"available.Count > ModeHConfig.MaxProductionCandidateCount \|\| available.Count < count",
         "组合枚举必须限制认证池规模并拒绝人数不足")
    need(repair, r"if \(bits != count\) continue;", "组合修复保持骨架要求人数")
    need(repair,
         r"if \(!string.IsNullOrEmpty\(requiredCoreKey\) && !candidate.Contains\(requiredCoreKey\)\) continue;",
         "组合修复不得丢弃回响核心")
    for body, name in ((repair, "组合修复"), (select, "抽样选择")):
        need(body,
             r"int budgetPremiumMilli = 1000\s*\+ \(int\)\(ModeHConfig.ThreatActionCountCoefficient \* 1000f\) \* \(count - 1\);",
             name + "与有效威胁使用同一人数溢价")
        need(body,
             r"long scaledBudget = \(long\)corridor.ThreatBudget \* budgetPremiumMilli / 1000L;\s*"
             r"int lowerBound = \(int\)\(scaledBudget \* corridor.MinFillPercent / 100L\);\s*"
             r"int upperBound = \(int\)\(scaledBudget\s*"
             r"\* \(100 \+ \(int\)\(ModeHConfig.ThreatBudgetTolerance \* 100f\)\) / 100L\);",
             name + "必须保留同一威胁走廊上下界")
    need(repair,
         r"int effective = ComputeEffectiveThreat\(candidate\);\s*"
         r"if \(effective < lowerBound \|\| effective > upperBound\) continue;",
         "组合修复实际威胁必须在上下界内")
    need(repair,
         r"List<string> tags = CollectCapabilityTags\(candidate, condition\);\s*"
         r"if \(HasLegalArrangement\(liveArchetypeIds, CollectLockedArchetypes\(tags\)\)\)\s*\{\s*"
         r"result = candidate;\s*return true;\s*\}",
         "修复成功必须再次按候选和擂台条件通过合同原型矩阵")
    # 合同原型只能来自五原型集合；五种均封锁时此谓词必然为 false。
    need(veto_body,
         r"Array.IndexOf\(ModeHStableIds.AllArchetypes, archetypeId\) >= 0\s*"
         r"&& \(lockedArchetypes == null \|\| !lockedArchetypes.Contains\(archetypeId\)\)\) return true;",
         "合法排列必须是未封锁的已知原型，五原型全封锁不得通过修复")
    need(matrix,
         r"if \(!enemyCapabilityTags.Contains\(entry.HardLockedBy\[j\]\)\) continue;\s*"
         r"if \(!locked.Contains\(entry.ArchetypeId\)\) locked.Add\(entry.ArchetypeId\);",
         "硬封锁矩阵必须按任一命中标签累积被封原型")

    # veto 只读公开原型，不得读虚拟 kit / 口令 / 本场顺序
    veto = re.search(r"public static bool HasLegalArrangement\([\s\S]*?\n        \}", code)
    if veto:
        body = veto.group(0)
        for forbidden in ["Kit", "commandId", "CommandId", "matchStarter", "matchRelay"]:
            if forbidden in body:
                errors.append("[Planner] roster veto 不得读取: " + forbidden)

    # 计划不得反向读赔率
    if re.search(r"\bModeHOddsController\b", code):
        errors.append("[Planner] 计划不得反向读取赔率服务")


def check_planner(errors):
    source = read_text(PLANNER)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHEncounterPlanner.cs")
        return
    code = strip_cs_comments(source)
    check_planner_code(code, errors)
    mutations = [
        ("lockedArchetypes.Count >= ModeHStableIds.AllArchetypes.Length", "false"),
        ("|| !HasLegalArrangement(liveArchetypeIds, lockedArchetypes)", "&& !HasLegalArrangement(liveArchetypeIds, lockedArchetypes)"),
        ('failureReasonId = "plan_roster_no_legal_arrangement";', 'failureReasonId = "ignored";'),
        ("available.Count > ModeHConfig.MaxProductionCandidateCount || available.Count < count", "available.Count < count"),
        ("if (bits != count) continue;", ""),
        ("if (!string.IsNullOrEmpty(requiredCoreKey) && !candidate.Contains(requiredCoreKey)) continue;", ""),
        ("if (effective < lowerBound || effective > upperBound) continue;", "if (effective > upperBound) continue;"),
        ("if (effective < lowerBound || effective > upperBound) continue;", "if (effective < lowerBound) continue;"),
        ("CollectCapabilityTags(candidate, condition)", "CollectCapabilityTags(candidate, null)"),
        ("HasLegalArrangement(liveArchetypeIds, CollectLockedArchetypes(tags))", "true"),
        ("Array.IndexOf(ModeHStableIds.AllArchetypes, archetypeId) >= 0", "true"),
        ("!lockedArchetypes.Contains(archetypeId)", "true"),
        ("locked.Add(entry.ArchetypeId);", "locked.Clear();"),
        ("scaledBudget * corridor.MinFillPercent / 100L", "0"),
        ("ModeHConfig.ThreatBudgetTolerance * 100f", "999f"),
    ]
    for before, after in mutations:
        if before not in code:
            errors.append("[Planner probe] 缺少变异目标: " + before)
            continue
        rejected = []
        check_planner_code(code.replace(before, after, 1), rejected)
        if not rejected:
            errors.append("[Planner probe] 放宽约束的变异漏检: " + before)


def main():
    errors = []

    model = read_modeh_group("ModeHStateModel.cs", "ModeHStateDtos.cs")
    if model is None:
        print("ModeHRosterInvariantGuard: FAIL (1 errors)")
        print("  - [File] 缺少 ModeH/ModeHStateModel.cs")
        return 1
    code = strip_cs_comments(model)

    validator = re.search(
        r"public static bool ValidateRosterInvariant\([\s\S]*?\n        \}", code)
    if not validator:
        errors.append("[Invariant] 缺少 ValidateRosterInvariant 集中实现")
    else:
        body = validator.group(0)
        checks = [
            (r'failureReasonId = "no_live_contract";', "存活数为 0 拒绝"),
            (r'failureReasonId = "starter_not_live_contract";', "先发必须是存活合同选手"),
            (r'failureReasonId = "relay_not_live_contract";', "接力者必须是存活合同选手"),
            (r'failureReasonId = "relay_duplicates_starter";', "接力者不得与先发重复"),
            (r'failureReasonId = "relay_must_be_empty_for_single_roster";', "单人 roster 强制无接力"),
            (r"liveContractProfileIds\.Count == 1 && hasRelay", "存活数为 1 时禁止接力者"),
        ]
        for pattern, desc in checks:
            if not re.search(pattern, body):
                errors.append("[Invariant] 不满足: " + desc)

    contract = re.search(r"public sealed class ModeHContractDto[\s\S]*?\n    \}", code)
    if not contract:
        errors.append("[Contract] 未找到 ModeHContractDto")
    else:
        body = contract.group(0)
        if "contractMainProfileId" not in body or "contractSubProfileId" not in body:
            errors.append("[Contract] 合同必须由两个 profileId 决定")
        fields = re.findall(r"public\s+\w+\s+(\w+)\s*;", body)
        if len(fields) != 2:
            errors.append("[Contract] ModeHContractDto 只能有两个字段，实际: " + str(fields))

    roster = re.search(r"public sealed class ModeHMatchRosterDto[\s\S]*?\n    \}", code)
    if not roster:
        errors.append("[Roster] 未找到 ModeHMatchRosterDto")
    else:
        body = roster.group(0)
        for field in ["matchStarterProfileId", "matchRelayProfileId", "activeProfileId",
                      "enteredProfileIds", "relayConsumed"]:
            if not re.search(r"\b{}\s*;".format(field), body):
                errors.append("[Roster] 缺少字段: " + field)
        for forbidden in FORBIDDEN_ROLE_FIELDS:
            if re.search(r"\b{}\s*;".format(forbidden), body):
                errors.append("[Roster] 不得复制角色布尔: " + forbidden)

    profile = re.search(r"public sealed class ModeHProfileDto[\s\S]*?\n    \}", code)
    if not profile:
        errors.append("[Profile] 未找到 ModeHProfileDto")
    else:
        body = profile.group(0)
        if not re.search(r"\bstatus\s*;", body):
            errors.append("[Profile] 角色状态必须只在 profile 的 status 字段")
        for forbidden in FORBIDDEN_ROLE_FIELDS:
            if re.search(r"\b{}\s*;".format(forbidden), body):
                errors.append("[Profile] 不得复制合同角色字段: " + forbidden)

    check_draft(errors)
    check_planner(errors)

    # 其他 DTO 不得复制 contractState
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        text = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        if re.search(r"public\s+\w+\s+contractState\s*;", text):
            errors.append("[Profile] {} 复制了 contractState".format(name))

    if errors:
        print("ModeHRosterInvariantGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHRosterInvariantGuard: PASS (15 rejected planner regression mutations)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
