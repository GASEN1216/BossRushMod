#!/usr/bin/env python3
"""
ModeHSaveCompatibilityGuard — Mode H 存档兼容守卫（设计提案 §20.2、§20.3、§20.4、§26.1）。

不变式：
- 两个已启用 v1 key（Season / HallOfFame）+ stake journal 保留名，全部带 BossRush_ModeH_ 前缀；
- envelope 必带 schemaVersion / signatureAlgorithmVersion / gameBuildSignature /
  modBuildSignature / contentCatalogSignature / payloadDigest；
- 读取路径对未知 schemaVersion、未知 signatureAlgorithmVersion、不可读 payload、
  摘要不符设置写入保护，且不覆盖旧值；
- 未知枚举整数一律映射 Unknown；
- 不得改写或复用旧模式（Mode D/E/F/G/Zombie）的存档 key；
- HallOfFame 跨 key 幂等：同 hallOfFameId 只插入一次，超过 32 条按 (createdUtc, hallOfFameId)
  删除最旧一条，重试不得重新生成时间；
- 旧构建名人堂记录只读可见，不因当前构建签名不同而删除或改写。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
CONFIG = os.path.join(MODEH_DIR, "ModeHConfig.cs")
STATE_MODEL = os.path.join(MODEH_DIR, "ModeHStateModel.cs")
SEASON = os.path.join(MODEH_DIR, "ModeHProfilePersistence.cs")
HALL = os.path.join(MODEH_DIR, "ModeHHallOfFamePersistence.cs")

ENVELOPE_FIELDS = [
    "schemaVersion", "signatureAlgorithmVersion", "modBuildSignature",
    "gameBuildSignature", "contentCatalogSignature", "payloadDigest",
]

LEGACY_KEYS = [
    "BossRush_ModeG_Profile_v1",
    "BossRush_ModeG_NemesisRecord_v1",
    "BossRush_MedalStock",
    "BossRush_TicketStock",
    "BossRush_JournalStock",
    "BossRush_DeathWraith_List",
]


def main():
    errors = []

    config = read_text(CONFIG)
    if config is None:
        errors.append("[File] 缺少 ModeH/ModeHConfig.cs")
    else:
        code = strip_cs_comments(config)
        keys = [
            ('public const string SeasonStorageKey = "BossRush_ModeH_Season_v1";', "Season key"),
            ('public const string HallOfFameStorageKey = "BossRush_ModeH_HallOfFame_v1";', "HallOfFame key"),
            ('public const string StakeJournalStorageKey = "BossRush_ModeH_StakeJournal_v1";', "journal 保留名"),
        ]
        for literal, desc in keys:
            if literal not in code:
                errors.append("[Key] 冻结 key 缺失: " + desc)

    model = read_text(STATE_MODEL)
    if model:
        code = strip_cs_comments(model)
        for dto, label in (("ModeHSeasonDto", "Season"), ("ModeHHallOfFameEnvelopeDto", "HallOfFame")):
            block = re.search(r"public sealed class {}[\s\S]*?\n    \}}".format(dto), code)
            if not block:
                errors.append("[Envelope] 未找到 " + dto)
                continue
            body = block.group(0)
            for field in ENVELOPE_FIELDS:
                if not re.search(r"\b{}\s*;".format(field), body):
                    errors.append("[Envelope] {} 缺少 envelope 字段: {}".format(label, field))
        for mapper in ["ToLifecycle", "ToParticipantStatus", "ToCompatibilityStatus", "ToStakePhase"]:
            if not re.search(r"public static \w+ {}\(int raw\)".format(mapper), code):
                errors.append("[Enum] 缺少未知整数 -> Unknown 映射: " + mapper)

    season = read_text(SEASON)
    if season is None:
        errors.append("[File] 缺少 ModeH/ModeHProfilePersistence.cs")
    else:
        code = strip_cs_comments(season)
        checks = [
            (r'SetWriteBarrier\("season_schema_incompatible"\)', "未知 schema 进入写入保护"),
            (r'SetWriteBarrier\("season_digest_mismatch"\)', "摘要不符进入写入保护"),
            (r'SetWriteBarrier\("season_payload_null"\)', "不可读 payload 进入写入保护"),
            (r"signatureAlgorithmVersion != ModeHConfig\.CurrentSignatureAlgorithmVersion", "签名算法版本核对"),
            (r"TryComputeObjectDigest\(dto, \"payloadDigest\"", "payloadDigest 排除自身字段计算"),
            (r"SavesSystem\.KeyExisits\(StorageKey\)", "KeyExisits 前置分类"),
            (r"readback\.payloadDigest, expectedDigest", "写后回读核对"),
        ]
        for pattern, desc in checks:
            if not re.search(pattern, code):
                errors.append("[Season] 不满足: " + desc)
        # 写入保护时不得覆盖旧值
        barrier = re.search(r"private static void SetWriteBarrier\(string reasonId\)[\s\S]{0,500}?\n        \}", code)
        if barrier and "Save<" in barrier.group(0):
            errors.append("[Season] 写入保护路径不得写盘")

    hall = read_text(HALL)
    if hall is None:
        errors.append("[File] 缺少 ModeH/ModeHHallOfFamePersistence.cs")
    else:
        code = strip_cs_comments(hall)
        checks = [
            (r"MaxHallOfFameRecords", "引用 32 条上限常量"),
            (r"return true; ?", "同 ID 幂等直接返回"),
            (r"envelope\.records\.Sort\(CompareRecords\);", "按 (createdUtc, hallOfFameId) 稳定排序"),
            (r"envelope\.records\.RemoveAt\(0\);", "超上限删除最旧一条"),
            (r"string\.Equals\(existing\.hallOfFameId, record\.hallOfFameId, StringComparison\.Ordinal\)",
             "按稳定 ID 去重"),
            (r"TryGetCertificationCache\(", "四签名认证缓存读取"),
            (r"cache\.slotGeneration != slotGeneration", "缓存按 slotGeneration 键控"),
            (r"if \(!cache\.snapshot\.overallPassed\) return null;", "未通过的缓存不得命中"),
        ]
        for pattern, desc in checks:
            if not re.search(pattern, code):
                errors.append("[HallOfFame] 不满足: " + desc)
        # 不得因签名不同删除旧记录
        if re.search(r"records\.Clear\(\)", code):
            errors.append("[HallOfFame] 不得清空历史记录")
        compare = re.search(r"private static int CompareRecords\([\s\S]{0,700}?\n        \}", code)
        if not compare:
            errors.append("[HallOfFame] 缺少 CompareRecords 稳定排序实现")
        else:
            body = compare.group(0)
            if "createdUtc" not in body or "hallOfFameId" not in body:
                errors.append("[HallOfFame] 排序必须同时使用 createdUtc 与 hallOfFameId")
        # 重试不得重新生成时间：时间只能来自记录快照
        if re.search(r"DateTime\.(UtcNow|Now)", code):
            errors.append("[HallOfFame] 插入/重试路径不得重新生成时间")

    # 不得改写旧模式 key
    for name in sorted(os.listdir(MODEH_DIR)):
        if not name.endswith(".cs"):
            continue
        code = strip_cs_comments(read_text(os.path.join(MODEH_DIR, name)) or "")
        for key in LEGACY_KEYS:
            if key in code:
                errors.append("[Legacy] {} 引用了旧模式存档 key: {}".format(name, key))

    if errors:
        print("ModeHSaveCompatibilityGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHSaveCompatibilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
