#!/usr/bin/env python3
"""
ModeHStructureGuard — Mode H 结构守卫（设计提案 §20.1、§23.2、§25、§26.1）。

不变式：
- 关键文件与核心类型存在；
- 全部持久枚举都有 Unknown=0 且逐项显式整数（禁止依赖 C# 默认序号）；
- ModeHLifecycle v1 取值逐项冻结，ModeHMatchPhase 只从 lifecycle 派生；
- 完整 Season 根字段齐全（禁止把 report/profile/roster/虚拟奖励拆成多次写入）；
- starter kit 字段与 schema 常量存在；
- 存档 key、数据文件名与本地化前缀冻结；
- Assets/Data/ModeH/ 未被 .gitignore 忽略（现有 !/Assets/Data/ 规则已放行子树）。

说明：新增 .cs 是否登记 compile_official.bat 由 ModeHCompileManifestGuard 与
仓库既有 OfficialCompileListFileExistenceGuard 双向守卫，本文件不重复断言。
"""
import os
import re
import subprocess
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_modeh_group, read_text, strip_cs_comments  # noqa: E402
MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
STATE_MODEL = os.path.join(MODEH_DIR, "ModeHStateModel.cs")
CONFIG = os.path.join(MODEH_DIR, "ModeHConfig.cs")
SEED = os.path.join(MODEH_DIR, "ModeHSeedStream.cs")
DIGEST = os.path.join(MODEH_DIR, "ModeHCanonicalDigest.cs")

REQUIRED_FILES = [
    STATE_MODEL,
    CONFIG,
    SEED,
    DIGEST,
]

# 全部持久枚举：名称 -> 必须逐项出现的 "成员 = 整数" 列表
PERSISTED_ENUMS = {
    "ModeHLifecycle": [
        ("Unknown", 0), ("None", 1), ("EntryIntent", 2), ("SceneLoading", 3),
        ("ProductionCertifying", 4), ("Drafting", 5), ("RosterLocked", 6),
        ("MatchBrief", 7), ("LoadoutEditing", 8), ("OddsPreview", 9),
        ("LoadoutLocked", 10), ("MatchSpawning", 11), ("MatchFighting", 12),
        ("ErrorRecoveryPending", 13), ("RelayPending", 14), ("MatchSettling", 15),
        ("Intermission", 16), ("TransferWindow", 17), ("HallOfFame", 18),
        ("SeasonEnded", 19), ("Recovering", 20), ("Suspended", 21),
        ("StakePrepared", 22),
    ],
    "ModeHMatchPhase": [
        ("Unknown", 0), ("None", 1), ("Brief", 2), ("Editing", 3), ("Locked", 4),
        ("Spawning", 5), ("Fighting", 6), ("RelayPending", 7), ("Settling", 8),
        ("Recovery", 9), ("Intermission", 10),
    ],
    "ModeHParticipantStatus": [
        ("Unknown", 0), ("Available", 1), ("Injured", 2), ("Retired", 3),
        ("Released", 4), ("Removed", 5),
    ],
    "ModeHMatchReportStatus": [
        ("Unknown", 0), ("SettledPendingArchive", 1), ("Archived", 2),
    ],
    "ModeHSeasonRewardOperationStatus": [
        ("Unknown", 0), ("Offered", 1), ("Applied", 2), ("Archived", 3),
    ],
    "ModeHRewardKind": [
        ("Unknown", 0), ("None", 1), ("UnlockKit", 2), ("FameDisplay", 3),
    ],
    "ModeHOfferStatus": [
        ("Unknown", 0), ("Pending", 1), ("Accepted", 2), ("Rejected", 3), ("Expired", 4),
    ],
    "ModeHCertificationStatus": [
        ("Unknown", 0), ("Passed", 1), ("Rejected", 2),
    ],
    "ModeHCommandCompatibilityStatus": [
        ("Unknown", 0), ("VerifiedBehavior", 1), ("ReportOnly", 2),
        ("Unavailable", 3), ("PartiallyVerified", 4),
    ],
    "ModeHHallOfFameCommandStatus": [
        ("Unknown", 0), ("Pending", 1), ("Completed", 2),
    ],
    "ModeHExitReason": [
        ("Unknown", 0), ("UserMapReturn", 1), ("SceneGenerationMismatch", 2),
        ("ModDestroyed", 3), ("TechnicalAbort", 4), ("SeasonComplete", 5),
        ("Unavailable", 6),
    ],
    "ModeHAvailabilityStatus": [
        ("Unknown", 0), ("Available", 1), ("Unavailable", 2),
    ],
    "ModeHMatchOutcome": [
        ("Unknown", 0), ("PlayerVictory", 1), ("PlayerDefeat", 2),
    ],
    "ModeHStakeReceiptStatus": [
        ("Unknown", 0), ("Planned", 1), ("Applied", 2), ("Verified", 3),
        ("Rejected", 4), ("Pending", 5), ("ManualIntervention", 6),
    ],
    "ModeHSettlementKind": [
        ("Unknown", 0), ("None", 1), ("MatchResult", 2), ("AbortReturn", 3),
    ],
    "ModeHRewardOperationStatus": [
        ("Unknown", 0), ("Planned", 1), ("Committed", 2), ("SettlementPending", 3),
        ("Settled", 4), ("ManualIntervention", 5),
    ],
    "ModeHAbortReturnOperationStatus": [
        ("Unknown", 0), ("Committed", 1), ("SettlementPending", 2), ("Settled", 3),
        ("ManualIntervention", 4),
    ],
    "ModeHStakePhase": [
        ("Unknown", 0), ("None", 1), ("Prepared", 2), ("EscrowSnapshotDurable", 3),
        ("EscrowRemovedDurable", 4), ("MatchLocked", 5), ("ResultCommitted", 6),
        ("SettlementPending", 7), ("Terminal", 8), ("CancelledTerminal", 9),
        ("AbortReturnCommitted", 10), ("RefundedTerminal", 11), ("ManualIntervention", 12),
    ],
}

# 完整 Season payload 根字段（§20.1）
SEASON_ROOT_FIELDS = [
    "schemaVersion", "signatureAlgorithmVersion", "modBuildSignature", "gameBuildSignature",
    "contentCatalogSignature", "payloadDigest", "slotGeneration",
    "productionCertificationSnapshot", "runState", "draftCandidateProfileIds",
    "echoAssignments", "profiles", "contract", "matchRoster", "currentMatchPlan",
    "currentLoadoutLock", "preMatchSnapshot", "currentOffer", "currentBattleSnapshot",
    "virtualStakeCredits", "reservedVirtualStake", "unlockedKitIds",
    "seasonRewardOperations", "matchReports", "appliedEventTokenIds", "hallOfFameCommand",
]

# 核心 DTO 类型（§20.1）
REQUIRED_TYPES = [
    "ModeHSeasonDto", "ModeHRunStateDto", "ModeHProductionCertificationDto",
    "ModeHPresetCertificationRecordDto", "ModeHProfileDto", "ModeHContractDto",
    "ModeHEchoAssignmentDto", "ModeHMatchRosterDto", "ModeHLoadoutKitDto",
    "ModeHMatchPlanDto", "ModeHLoadoutLockDto", "ModeHMatchReportDto", "ModeHOfferDto",
    "ModeHSeasonRewardOperationDto", "ModeHItemTreeSnapshotDto", "ModeHStakeReceiptDto",
    "ModeHRewardOperationDto", "ModeHPreMatchSnapshotDto", "ModeHBattleSnapshotDto",
    "ModeHStakeJournalDto", "ModeHAbortReturnOperationDto", "ModeHHallOfFameRecordDto",
    "ModeHHallOfFameCommandDto", "ModeHCertificationCacheDto",
]

# starter kit 字段（§17.7）
KIT_FIELDS = [
    "kitId", "isStarterKit", "starterOrder", "compatibleProfileIds",
    "compatibleArchetypeIds", "replaceSlot", "typeId", "gameQuality",
    "ammoTypeId", "ammoCount", "contentSignature",
]

# schema / key 常量（§20.3）
CONFIG_CONSTANTS = [
    (r'public const string SeasonStorageKey = "BossRush_ModeH_Season_v1";', "Season 存档 key"),
    (r'public const string HallOfFameStorageKey = "BossRush_ModeH_HallOfFame_v1";', "名人堂存档 key"),
    (r'public const string StakeJournalStorageKey = "BossRush_ModeH_StakeJournal_v1";', "stake journal key"),
    (r"public const int CurrentSchemaVersion = 1;", "schemaVersion 冻结"),
    (r"public const int CurrentSignatureAlgorithmVersion = 1;", "签名算法版本冻结"),
    (r"public const int CurrentCertificationSchemaVersion = 1;", "认证 schema 版本冻结"),
    (r'public const string LocalizationKeyPrefix = "BossRush_ModeH_";', "本地化前缀冻结"),
    (r'public const string DataSubDirectoryName = "ModeH";', "数据子目录冻结"),
    (r"public const int SeasonMatchCount = 6;", "三幕六战"),
    (r"public const float MatchDurationSeconds = 180f;", "单场 180 秒"),
    (r"public const int MinProductionCandidateCount = 8;", "生产目录下限 8"),
    (r"public const int MaxProductionCandidateCount = 12;", "生产目录上限 12"),
    (r"public const float CommandWindowSeconds = 6f;", "口令窗口 6 秒"),
    (r"public const float CommandReassertIntervalSeconds = 0\.1f;", "口令重申 0.1 秒"),
    (r"public const float BattleSnapshotIntervalSeconds = 10f;", "快照周期 10 秒"),
    (r"public const float StandInRadiusMeters = 3\.0f;", "看台表演半径"),
    (r"public const float StandInRepathIntervalSeconds = 0\.5f;", "看台重设间隔"),
    (r"public const int InitialVirtualStakeCredits = 6;", "初始虚拟筹码 6"),
    (r"public const int MaxVirtualStakeCredits = 30;", "虚拟筹码上限 30"),
    (r"public const int MaxVirtualStakePerMatch = 2;", "每场下注上限 2"),
    (r"public const int MaxHallOfFameRecords = 32;", "名人堂上限 32"),
    (r"public const int MaxScarsPerProfile = 3;", "战痕上限 3"),
]

DATA_FILE_NAMES = [
    "BossProfiles.json", "Commands.json", "CommandCompatibility.json",
    "LoadoutKits.json", "ThreatPlans.json", "Scars.json", "OddsWeights.json",
]


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def check_enum(model, name, members, errors):
    match = re.search(r"public enum {}\s*\{{([\s\S]*?)\n    \}}".format(re.escape(name)), model)
    if not match:
        errors.append("[Enum] 未找到枚举: " + name)
        return
    body = match.group(1)
    for member, value in members:
        pattern = r"\b{}\s*=\s*{}\b".format(re.escape(member), value)
        if not re.search(pattern, body):
            errors.append("[Enum] {} 缺少显式整数成员: {} = {}".format(name, member, value))
    # 禁止隐式成员（每个非空、非注释成员行都必须带 "= 整数"）
    for line in body.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("//") or stripped.startswith("/*") or stripped.startswith("*"):
            continue
        if stripped.startswith("///"):
            continue
        if "=" not in stripped:
            errors.append("[Enum] {} 存在未显式赋值的成员: {}".format(name, stripped))


def check_gitignore_allows_data_dir(errors):
    target = "Assets/Data/ModeH/BossProfiles.json"
    try:
        result = subprocess.run(
            ["git", "check-ignore", "-v", target],
            cwd=REPO_ROOT,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
    except Exception as exc:  # git 不可用时不阻断，只提示
        print("  (info) git check-ignore 不可用，跳过忽略检查: {}".format(exc))
        return
    if result.returncode == 0:
        errors.append(
            "[GitIgnore] Assets/Data/ModeH/ 被忽略: "
            + result.stdout.decode("utf-8", "replace").strip())


def check_ui_layers_and_deploy(errors):
    """
    §23.1：四个 Mode H 层级常量必须按数值升序插入 BossRushUILayers，
    Mode H 界面文件只引用常量、不得出现裸 sortingOrder 数字；
    §23.3：两个 bat 都要有 Mode H 数据与展示 bundle 的显式部署路径。
    """
    ui_library = read_text(os.path.join(REPO_ROOT, "Common", "UI", "BossRushUI.cs"))
    if ui_library is None:
        errors.append("[UI] 缺少 Common/UI/BossRushUI.cs")
    else:
        expected = [
            ("ModeHHud", 960),
            ("ModeHDiagnostics", 970),
            ("ModeHModal", 980),
            ("ModeHRecovery", 3100),
        ]
        for name, value in expected:
            if not re.search(
                    r"internal const int {} = {};".format(name, value), ui_library):
                errors.append("[UI] 缺少层级常量 {} = {}".format(name, value))

        # 声明顺序必须严格递增（追加到表尾会破坏叠放次序）
        declared = re.findall(r"internal const int (\w+) = (\d+);", ui_library)
        values = [int(v) for _, v in declared]
        if values != sorted(values):
            errors.append("[UI] BossRushUILayers 的声明顺序必须严格递增")

    # Mode H 界面文件不得出现裸 sortingOrder 数字
    for name in ["ModeHUI.cs", "ModeHUIPages.cs", "ModeHRecoveryPanel.cs"]:
        path = os.path.join(MODEH_DIR, name)
        code = read_text(path)
        if code is None:
            errors.append("[UI] 缺少 ModeH/" + name)
            continue
        stripped = strip_cs_comments(code)
        if re.search(r"sortingOrder\s*=\s*\d", stripped):
            errors.append("[UI] {} 出现裸 sortingOrder 数字".format(name))
        if re.search(r"CreateCanvasRoot\([^)]*,\s*\d+\s*,", stripped):
            errors.append("[UI] {} 的 CreateCanvasRoot 必须传层级常量".format(name))
        # 第二套遮罩色禁令
        if "new Color(0f, 0f, 0f, 0.7f)" in stripped:
            errors.append("[UI] {} 仍在用第二套遮罩色".format(name))

    # 两个 bat 的显式部署路径
    for bat_name in ["compile_official.bat", "test_bossrush_official.bat"]:
        text = read_text(os.path.join(REPO_ROOT, bat_name))
        if text is None:
            errors.append("[Deploy] 缺少 " + bat_name)
            continue
        for required, desc in [
            ("Assets\\Data\\ModeH", "Mode H 数据目录"),
            ("Assets\\ui\\modeh_presentation", "Mode H 展示 bundle"),
        ]:
            if required not in text:
                errors.append("[Deploy] {} 缺少 {} 的部署路径".format(bat_name, desc))


def main():
    errors = []

    for path in REQUIRED_FILES:
        if not os.path.exists(path):
            errors.append("[File] 缺少关键文件: " + os.path.relpath(path, REPO_ROOT))

    model = read_modeh_group("ModeHStateModel.cs", "ModeHStateDtos.cs")
    if model is None:
        errors.append("[File] 缺少 ModeH 数据契约文件组")
        model = ""
    config = read(CONFIG, errors)

    if model:
        for name, members in PERSISTED_ENUMS.items():
            check_enum(model, name, members, errors)

        for type_name in REQUIRED_TYPES:
            if not re.search(r"public sealed class {}\b".format(re.escape(type_name)), model):
                errors.append("[Type] 缺少 DTO 类型: " + type_name)

        season = re.search(r"public sealed class ModeHSeasonDto[\s\S]*?\n    \}", model)
        if not season:
            errors.append("[Season] 未找到 ModeHSeasonDto 定义")
        else:
            body = season.group(0)
            for field in SEASON_ROOT_FIELDS:
                if not re.search(r"\b{}\s*;".format(re.escape(field)), body):
                    errors.append("[Season] 根字段缺失: " + field)

        kit = re.search(r"public sealed class ModeHLoadoutKitDto[\s\S]*?\n    \}", model)
        if not kit:
            errors.append("[Kit] 未找到 ModeHLoadoutKitDto 定义")
        else:
            body = kit.group(0)
            for field in KIT_FIELDS:
                if not re.search(r"\b{}\s*;".format(re.escape(field)), body):
                    errors.append("[Kit] 字段缺失: " + field)

        # lifecycle -> matchPhase 是唯一派生映射
        if not re.search(r"public static ModeHMatchPhase GetMatchPhase\(ModeHLifecycle lifecycle\)", model):
            errors.append("[Derived] 缺少 lifecycle -> matchPhase 派生映射")
        # 未知整数映射 Unknown（写保护前提）
        if not re.search(r"public static ModeHLifecycle ToLifecycle\(int raw\)", model):
            errors.append("[Derived] 缺少未知 lifecycle 整数的 Unknown 映射")
        # roster 不变式集中实现
        if not re.search(r"public static bool ValidateRosterInvariant\(", model):
            errors.append("[Roster] 缺少 roster 不变式集中校验")
        # 持久 DTO 必须可序列化
        serializable_count = len(re.findall(r"\[Serializable\]", model))
        if serializable_count < len(REQUIRED_TYPES):
            errors.append("[Serializable] [Serializable] 标注数量不足: {} < {}".format(
                serializable_count, len(REQUIRED_TYPES)))

    if config:
        for pattern, desc in CONFIG_CONSTANTS:
            if not re.search(pattern, config):
                errors.append("[Config] 冻结常量缺失: " + desc)
        for name in DATA_FILE_NAMES:
            if '"{}"'.format(name) not in config:
                errors.append("[Config] 数据文件名未冻结: " + name)
        # 禁止在 ModeHConfig 中重新引入可写开关
        if re.search(r"public\s+static\s+bool\s+\w*Enabled", config):
            errors.append("[Config] ModeHConfig 不得声明可写 Enabled 字段")

    check_gitignore_allows_data_dir(errors)
    check_ui_layers_and_deploy(errors)

    if errors:
        print("ModeHStructureGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHStructureGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
