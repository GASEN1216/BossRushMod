#!/usr/bin/env python3
"""
ModeHBattleSnapshotGuard — Mode H 战场快照守卫（设计提案 §17.4、§26.1）。

不变式：
- ModeHBattleSnapshotDto 字段齐全，且**没有** Unity 引用 / InstanceID / 委托；
  位置只存 float x/y/z 与 rotationY；
- 四类采集时点齐全（每批敌军入场后 / 每 10 秒 / 拍铃后 / 倒地或接力后），
  且不在 Update 每帧写盘、不额外调用 SaveFile；
- snapshotDigest 走 §20.2 的同一 canonical digest 并排除自身字段；
- 重建走同一个 ModeHSpawnTransaction，按 healthFraction * MaxHealth 调 SetHealth，
  并读回核对；
- 六类 fail-closed 条件齐全，且全部回落到同场重开而非判负；
- 快照作为 Season 可空根字段 currentBattleSnapshot 挂载。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import contains_symbol, read_modeh_group, read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
SNAPSHOT = os.path.join(MODEH_DIR, "ModeHBattleSnapshot.cs")
CONFIG = os.path.join(MODEH_DIR, "ModeHConfig.cs")

REQUIRED_DTO_FIELDS = [
    "schemaVersion", "matchIndex", "technicalRetrySequence", "snapshotSequence",
    "snapshotDigest", "elapsedSeconds", "entryBatchIndex", "pendingBatchStableKeys",
    "activeEnemies", "entrant", "bellConsumed", "activeCommandId",
    "commandWindowRemainingSeconds", "scarWindowStates", "cowardCheckDone",
    "errorCheckDone", "errorSwapActive", "errorSwapProfileId", "standInPatternId",
    "appliedEventTokenIds",
]

FORBIDDEN_DTO_TYPES = [
    "Vector3", "Quaternion", "Transform", "GameObject", "CharacterMainControl",
    "Health", "Action", "Func", "InstanceID", "GetInstanceID",
]

# §17.4 原本的六类 fail-closed 条件全部属于「局中重建」那条链，
# 而重建在 2026-09-03 随 §20.3 收敛被移除（理由见 ModeHBattleSnapshot.cs 的
# 「重建校验（已随 §20.3 收敛而移除）」）。于是本 guard 的断言方向反过来：
# 不再要求这些 reason 存在，而是要求整条重建链**保持缺席**，
# 防止后来者只接回其中一半，重新造出"写好了但跑不到"。
REBUILD_ABSENT_SYMBOLS = [
    "ModeHSnapshotRebuildPlan",
    "TryRestoreHealth",
    "snapshot_restore_readback_mismatch",
    "snapshot_position_unusable",
]


def check_dto(errors):
    model = read_modeh_group("ModeHStateModel.cs", "ModeHStateDtos.cs")
    if model is None:
        errors.append("[File] 缺少 Mode H 状态模型")
        return
    code = strip_cs_comments(model)

    dto = re.search(r"public sealed class ModeHBattleSnapshotDto[\s\S]*?\n    \}", code)
    if not dto:
        errors.append("[DTO] 未找到 ModeHBattleSnapshotDto")
        return
    body = dto.group(0)
    for field in REQUIRED_DTO_FIELDS:
        if not re.search(r"\b{}\s*;".format(field), body):
            errors.append("[DTO] 缺少字段: " + field)

    # 位置类 DTO 只能是 float 分量
    for name in ["ModeHBattleEnemyStateDto", "ModeHBattleEntrantStateDto"]:
        sub = re.search(r"public sealed class {}[\s\S]*?\n    \}}".format(name), code)
        if not sub:
            errors.append("[DTO] 未找到 " + name)
            continue
        sub_body = sub.group(0)
        for axis in ["positionX", "positionY", "positionZ", "rotationY"]:
            if not re.search(r"public float {}\s*;".format(axis), sub_body):
                errors.append("[DTO] {} 的 {} 必须是 float 分量".format(name, axis))
        for forbidden in FORBIDDEN_DTO_TYPES:
            if re.search(r"public\s+{}\b".format(re.escape(forbidden)), sub_body):
                errors.append("[DTO] {} 不得包含 {}".format(name, forbidden))

    for forbidden in FORBIDDEN_DTO_TYPES:
        if re.search(r"public\s+{}\b".format(re.escape(forbidden)), body):
            errors.append("[DTO] ModeHBattleSnapshotDto 不得包含 " + forbidden)

    season = re.search(r"public sealed class ModeHSeasonDto[\s\S]*?\n    \}", code)
    if season and "currentBattleSnapshot" not in season.group(0):
        errors.append("[DTO] Season 缺少可空根字段 currentBattleSnapshot")


def check_capture(errors):
    source = read_text(SNAPSHOT)
    if source is None:
        errors.append("[File] 缺少 ModeH/ModeHBattleSnapshot.cs")
        return
    code = strip_cs_comments(source)

    # 四类采集时点
    for trigger in ["BatchEntered", "Interval", "BellCommitted", "DownOrRelay"]:
        if not re.search(r"{}\s*=\s*\d+".format(trigger), code):
            errors.append("[Capture] 缺少采集时点: " + trigger)

    checks = [
        (r"public bool Capture\(", "采集入口"),
        (r"public bool TickInterval\(float deltaTime\)", "间隔采集计时"),
        (r"_intervalAccumulator < ModeHConfig\.BattleSnapshotIntervalSeconds",
         "间隔引用冻结常量"),
        (r"dto\.snapshotSequence = _snapshotSequence \+ 1;", "每次采集递增序号"),
        (r'TryComputeObjectDigest\(dto, "snapshotDigest"', "摘要排除自身字段"),
        (r"public void AttachTo\(ModeHSeasonDto season\)", "随 Season 一并落盘"),

        (r"private static void WritePosition\(", "位置写入只取 float 分量"),
    ]
    for pattern, desc in checks:
        if not re.search(pattern, code):
            errors.append("[Capture] 不满足: " + desc)

    # 不得直接写盘
    for forbidden in ["SavesSystem", "SaveFile", "ModeHSaveFlushCoordinator"]:
        if contains_symbol(code, forbidden):
            errors.append("[Capture] 快照只在内存构造，不得调用存档 API: " + forbidden)

    # 不得在每帧路径写快照
    if re.search(r"void Update\(", code):
        errors.append("[Capture] 快照不得挂在 Update 每帧路径")

    # 局中重建链必须保持缺席（方向与旧版相反，理由见 REBUILD_ABSENT_SYMBOLS）
    for symbol in REBUILD_ABSENT_SYMBOLS:
        if symbol in code:
            errors.append(
                "[Rebuild] 局中重建链已随 §20.3 移除，不得复活: " + symbol
                + "（要启用必须先改 ModeHStateMachine 冻结表给 Recovering 加战斗态出边，"
                + "属 AGENTS.md §10 需 owner 签字）")

    # fail-closed 一律不得判负
    if re.search(r"PlayerDefeat", code):
        errors.append("[FailClosed] 快照路径不得产生判负结果，只能回落到同场重开")


def check_config(errors):
    config = read_text(CONFIG)
    if config is None:
        errors.append("[File] 缺少 ModeH/ModeHConfig.cs")
        return
    if not re.search(r"public const float BattleSnapshotIntervalSeconds = 10f;", config):
        errors.append("[Config] 快照间隔未冻结为 10 秒")
    if not re.search(r"public const float MatchDurationSeconds = 180f;", config):
        errors.append("[Config] 单场时长未冻结为 180 秒")


def main():
    errors = []
    check_dto(errors)
    check_capture(errors)
    check_config(errors)

    if errors:
        print("ModeHBattleSnapshotGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHBattleSnapshotGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
