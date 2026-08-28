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


def check_planner(errors):
    """\u00a717.5 计划候选重试与 roster-level veto。"""
    source = read_text(PLANNER)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHEncounterPlanner.cs")
        return
    code = strip_cs_comments(source)

    checks = [
        (r"public static bool TryBuildPlan\(", "计划生成入口"),
        (r"candidateIndex < ModeHConfig\.MaxPlanCandidateAttempts", "候选重试上限来自冻结常量"),
        (r"public static bool HasLegalArrangement\(", "roster-level veto 入口"),
        (r'failureReasonId = "plan_roster_no_legal_arrangement";', "无合法排列才换候选"),
        (r'failureReasonId = "plan_locks_all_archetypes";', "封死五原型才换候选"),
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

    print("ModeHRosterInvariantGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
