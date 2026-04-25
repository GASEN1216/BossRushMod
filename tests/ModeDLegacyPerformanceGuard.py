"""
Guard: Mode D legacy 品质分布路径应复用初始化期的按品质分桶缓存，
避免运行时反复扫描全池或重复 Search。
"""

from pathlib import Path
import sys


MODED = Path("ModeD/ModeD.cs")
EQUIPMENT = Path("ModeD/ModeDEquipment.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    mode_d_text = MODED.read_text(encoding="utf-8")
    equipment_text = EQUIPMENT.read_text(encoding="utf-8")

    required_mode_d_snippets = [
        "modeDArmortPoolByQuality",
        "modeDHelmetPoolByQuality",
        "modeDAmmoPoolByQuality",
        "modeDMedicalPoolByQuality",
        "modeDTotemPoolByQuality",
        "modeDMaskPoolByQuality",
        "RebuildModeDQualityBuckets(modeDArmortPool, modeDArmortPoolByQuality);",
        "RebuildModeDQualityBuckets(modeDHelmetPool, modeDHelmetPoolByQuality);",
        "RebuildModeDQualityBuckets(modeDAmmoPool, modeDAmmoPoolByQuality);",
        "RebuildModeDQualityBuckets(modeDMedicalPool, modeDMedicalPoolByQuality);",
        "RebuildModeDQualityBuckets(modeDTotemPool, modeDTotemPoolByQuality);",
        "RebuildModeDQualityBuckets(modeDMaskPool, modeDMaskPoolByQuality);",
    ]

    for snippet in required_mode_d_snippets:
        if snippet not in mode_d_text:
            return fail("ModeDLegacyPerformanceGuard: missing ModeD bucket cache snippet -> " + snippet)

    required_equipment_snippets = [
        "modeDAccessoryPoolByQuality",
        "RebuildModeDQualityBuckets(modeDAccessoryPool, modeDAccessoryPoolByQuality);",
        "TryGetRandomItemByExactQualityBucket(",
        "modeDAmmoPoolByQuality",
    ]

    for snippet in required_equipment_snippets:
        if snippet not in equipment_text:
            return fail("ModeDLegacyPerformanceGuard: missing ModeDEquipment bucket usage snippet -> " + snippet)

    exact_quality_body = extract_method_body(
        equipment_text,
        "private int TryGetRandomItemByExactQualityBucket(",
    )
    if exact_quality_body is None:
        return fail("ModeDLegacyPerformanceGuard: missing exact-quality bucket helper body")

    if "GetMetaData" in exact_quality_body:
        return fail("ModeDLegacyPerformanceGuard: exact-quality helper still scans metadata at runtime")

    ammo_body = extract_method_body(
        equipment_text,
        "private Item CreateRandomAmmoForEnemyLoot(int qualityLevel, int minQ, int maxQ)",
    )
    if ammo_body is None:
        return fail("ModeDLegacyPerformanceGuard: missing CreateRandomAmmoForEnemyLoot body")

    if "ItemAssetsCollection.Search(filter)" in ammo_body:
        return fail("ModeDLegacyPerformanceGuard: legacy ammo path still performs runtime Search")

    print("ModeDLegacyPerformanceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
