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
- 角色状态只在 ModeHProfileDto.status，其他 DTO 不得复制 contractState。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments, read_modeh_group  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
STATE_MODEL = os.path.join(MODEH_DIR, "ModeHStateModel.cs")

FORBIDDEN_ROLE_FIELDS = [
    "isContractMain", "isContractSub", "isStarter", "isMatchStarter",
    "isMatchRelay", "contractState", "isMain", "isSub",
]


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
