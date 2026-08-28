#!/usr/bin/env python3
"""
ModeHSeasonCompositionGuard — Mode H Season payload 组成守卫（设计提案 §20.1、§26.1）。

不变式：
- `ModeHSeasonDto` 含带三签名的生产认证快照、候选、回响、计划、锁盘、赛前快照、
  offer、虚拟筹码、完整 profile/contract/roster/report/operation 集合；
- runtime owner token **未进入任何持久 DTO**（只在 ModeHRunState 内存里）；
- `MatchSettling` 单次提交全部战斗事实；`Intermission` 不首次写入伤病或筹码；
- 所有持久枚举显式赋整数且 `Unknown=0`；
- Season 根字段带 schemaVersion / 三签名 / payloadDigest / slotGeneration。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_modeh_group, read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")

REQUIRED_SEASON_FIELDS = [
    "schemaVersion", "signatureAlgorithmVersion", "modBuildSignature",
    "gameBuildSignature", "contentCatalogSignature", "payloadDigest", "slotGeneration",
    "productionCertificationSnapshot", "runState", "draftCandidateProfileIds",
    "echoAssignments", "profiles", "contract", "matchRoster", "currentMatchPlan",
    "currentLoadoutLock", "preMatchSnapshot", "currentOffer", "currentBattleSnapshot",
    "virtualStakeCredits", "reservedVirtualStake", "unlockedKitIds",
    "seasonRewardOperations", "matchReports", "appliedEventTokenIds", "hallOfFameCommand",
]

# runtime owner token 绝不能出现在任何持久 DTO 字段名里
FORBIDDEN_DTO_FIELDS = ["ownerToken", "runOwnerToken", "ownerTokenValue"]


def check_season_dto(errors):
    model = read_modeh_group("ModeHStateModel.cs", "ModeHStateDtos.cs")
    if model is None:
        errors.append("[File] 缺少 Mode H 状态模型")
        return None
    code = strip_cs_comments(model)

    season = re.search(r"public sealed class ModeHSeasonDto[\s\S]*?\n    \}", code)
    if not season:
        errors.append("[DTO] 未找到 ModeHSeasonDto")
        return code
    body = season.group(0)
    for field in REQUIRED_SEASON_FIELDS:
        if not re.search(r"\b{}\s*;".format(field), body):
            errors.append("[DTO] Season 缺少根字段: " + field)

    certification = re.search(
        r"public sealed class ModeHProductionCertificationDto[\s\S]*?\n    \}", code)
    if not certification:
        errors.append("[DTO] 未找到 ModeHProductionCertificationDto")
    else:
        cert_body = certification.group(0)
        for field in ["gameBuildSignature", "modBuildSignature", "contentCatalogSignature"]:
            if not re.search(r"\b{}\s*;".format(field), cert_body):
                errors.append("[DTO] 生产认证快照缺少签名字段: " + field)
    return code


def check_owner_token_not_persisted(errors, code):
    """owner token 只在内存：任何持久 DTO 都不得携带它。"""
    if code is None:
        return
    for match in re.finditer(r"public sealed class (ModeH\w*Dto)\b([\s\S]*?)\n    \}", code):
        name = match.group(1)
        body = match.group(2)
        for forbidden in FORBIDDEN_DTO_FIELDS:
            if re.search(r"\b{}\s*;".format(forbidden), body):
                errors.append("[Token] {} 不得持久化 runtime owner token".format(name))

    run_state = re.search(r"public sealed class ModeHRunStateDto[\s\S]*?\n    \}", code)
    if run_state and re.search(r"\bownerToken\s*;", run_state.group(0)):
        errors.append("[Token] ModeHRunStateDto 不得包含 ownerToken")

    # ModeHRunState 的 OwnerToken 不得写进 ToDto
    run = read_text(os.path.join(MODEH_DIR, "ModeHRunState.cs"))
    if run is not None:
        run_code = strip_cs_comments(run)
        to_dto = re.search(r"public ModeHRunStateDto ToDto\(\)[\s\S]*?\n        \}", run_code)
        if to_dto and "OwnerToken" in to_dto.group(0):
            errors.append("[Token] ToDto 不得写入 OwnerToken")


def check_enum_explicit_integers(errors, code):
    """所有持久枚举都必须显式赋整数且 Unknown=0。"""
    if code is None:
        return
    for match in re.finditer(r"public enum (ModeH\w+)\b([\s\S]*?)\n    \}", code):
        name = match.group(1)
        body = match.group(2)
        members = re.findall(r"^\s*(\w+)\s*=\s*(-?\d+)", body, re.MULTILINE)
        raw_members = re.findall(r"^\s*(\w+)\s*(?:=|,|$)", body, re.MULTILINE)
        if len(members) != len([m for m in raw_members if m not in ("get", "set")]):
            errors.append("[Enum] {} 存在未显式赋值的成员".format(name))
        if not members or members[0][0] != "Unknown" or members[0][1] != "0":
            errors.append("[Enum] {} 的首个成员必须是 Unknown = 0".format(name))


def check_settlement_boundary(errors):
    """
    MatchSettling 单次提交全部战斗事实；Intermission 不得首次写入伤病或筹码。
    以状态机的转换表与结算入口的静态形状为准。
    """
    machine = read_text(os.path.join(MODEH_DIR, "ModeHStateMachine.cs"))
    if machine is None:
        errors.append("[File] 缺少 ModeH/ModeHStateMachine.cs")
        return
    code = strip_cs_comments(machine)
    if "MatchSettling" not in code or "Intermission" not in code:
        errors.append("[Settle] 状态机缺少 MatchSettling / Intermission")
    if not re.search(r"ModeHLifecycle\.MatchSettling[\s\S]{0,200}?ModeHLifecycle\.Intermission", code):
        errors.append("[Settle] MatchSettling 必须能够推进到 Intermission")

    # 结算写入必须集中在 flush coordinator 的单次提交
    coordinator = read_text(os.path.join(MODEH_DIR, "ModeHSaveFlushCoordinator.cs"))
    if coordinator is None:
        errors.append("[File] 缺少 ModeH/ModeHSaveFlushCoordinator.cs")
        return
    ccode = strip_cs_comments(coordinator)
    if ccode.count("SavesSystem.SaveFile(") > 1:
        errors.append("[Settle] SaveFile 只能有一个调用点")

    # 虚拟筹码结算只在 Settle 入口写回；Intermission 不得首写
    stake = read_text(os.path.join(MODEH_DIR, "ModeHVirtualStakeController.cs"))
    if stake is not None:
        scode = strip_cs_comments(stake)
        if "Intermission" in scode:
            errors.append("[Settle] 虚拟筹码不得感知 Intermission 阶段")

    injury = read_text(os.path.join(MODEH_DIR, "ModeHInjuryAndScarSystem.cs"))
    if injury is not None:
        icode = strip_cs_comments(injury)
        if "Intermission" in icode:
            errors.append("[Settle] 伤病结算不得感知 Intermission 阶段")


def main():
    errors = []
    code = check_season_dto(errors)
    check_owner_token_not_persisted(errors, code)
    check_enum_explicit_integers(errors, code)
    check_settlement_boundary(errors)

    if errors:
        print("ModeHSeasonCompositionGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHSeasonCompositionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
