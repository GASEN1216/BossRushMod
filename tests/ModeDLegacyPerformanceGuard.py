"""
Guard: Mode D legacy 品质分布路径应复用初始化期的按品质分桶缓存，
避免运行时反复扫描全池或重复 Search。
"""

from pathlib import Path
import sys


MODED = Path("ModeD/ModeD.cs")
EQUIPMENT = Path("ModeD/ModeDEquipment.cs")
ITEM_POOL = Path("ModeD/ModeDItemPool.cs")
ITEM_POOL_QUALITY = Path("ModeD/ModeDItemPool_Quality.cs")
RUNTIME = Path("ModeD/ModeDRuntimeModule.cs")


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
    item_pool_text = ITEM_POOL.read_text(encoding="utf-8")
    quality_text = ITEM_POOL_QUALITY.read_text(encoding="utf-8")
    runtime_text = RUNTIME.read_text(encoding="utf-8")

    if "internal readonly ModeDItemPool ItemPool = new ModeDItemPool();" not in runtime_text:
        return fail("ModeDLegacyPerformanceGuard: Mode D runtime does not own the shared item pool service")
    if "return modeDRuntime.ItemPool;" not in mode_d_text:
        return fail("ModeDLegacyPerformanceGuard: host bridge does not use the registered Mode D pool")

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
        if snippet not in item_pool_text:
            return fail("ModeDLegacyPerformanceGuard: missing ModeD bucket cache snippet -> " + snippet)

    for bucket in ("Armort", "Helmet", "Ammo", "Medical", "Totem", "Mask", "Accessory"):
        if "readonly Dictionary<int, List<int>> modeD" + bucket + "PoolByQuality" not in item_pool_text:
            return fail("ModeDLegacyPerformanceGuard: item pool does not own " + bucket + " bucket")

    for snippet in (
        "modeDAccessoryPoolByQuality",
        "RebuildModeDQualityBuckets(modeDAccessoryPool, modeDAccessoryPoolByQuality);",
    ):
        if snippet not in item_pool_text:
            return fail("ModeDLegacyPerformanceGuard: missing shared item pool cache -> " + snippet)

    for snippet in ("TryGetRandomItemByExactQualityBucket(", "modeDAmmoPoolByQuality"):
        if snippet not in equipment_text:
            return fail("ModeDLegacyPerformanceGuard: missing ModeDEquipment bucket usage snippet -> " + snippet)

    exact_quality_body = extract_method_body(
        quality_text,
        "internal int TryGetRandomItemByExactQualityBucket(",
    )
    if exact_quality_body is None:
        return fail("ModeDLegacyPerformanceGuard: missing exact-quality bucket helper body")

    if "GetMetaData" in exact_quality_body:
        return fail("ModeDLegacyPerformanceGuard: exact-quality helper still scans metadata at runtime")
    if "internal sealed partial class ModeDItemPool" not in equipment_text or "TryGetRandomItemByExactQualityBucket(modeDAmmoPoolByQuality," not in equipment_text:
        return fail("ModeDLegacyPerformanceGuard: equipment path bypasses the shared quality helper")

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
