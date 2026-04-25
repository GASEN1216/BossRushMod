"""
Guard: Mode D / 白手起家共享背包预填物应复用 BossRush 候选池抽取，
并且预填路径必须改为 health-only 的 BossRush 概率模型。
"""

from pathlib import Path
import sys


MODED_EQUIPMENT = Path("ModeD/ModeDEquipment.cs")


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
    text = MODED_EQUIPMENT.read_text(encoding="utf-8")

    required_snippets = [
        "private bool TryCreateBossRushStyleInventoryLootItemForSharedModes(",
        "TryGetLegacyBossLootCandidates(",
        "BuildGeneralBossLootCandidateIdSet(",
        "PickBossRushStyleBucketItemId(",
        "LegacyBossLootProbabilityModel.BuildDistribution(",
    ]

    for snippet in required_snippets:
        if snippet not in text:
            return fail("ModeDSharedBossRushLootGuard: missing snippet -> " + snippet)

    equip_body = extract_method_body(text, "public void EquipEnemyForModeD(CharacterMainControl enemy, int waveIndex, float enemyHealth, bool isBoss = false)")
    if equip_body is None:
        return fail("ModeDSharedBossRushLootGuard: missing EquipEnemyForModeD body")

    if "GiveEnemyItemByCategoryNoWeapon(enemy, randomCategory, qualityLevel);" in equip_body:
        return fail("ModeDSharedBossRushLootGuard: EquipEnemyForModeD still uses old non-weapon category grant path")

    if "private bool TryCreateBossRushStyleInventoryLootItemForSharedModes(float enemyHealth, out Item item)" not in text:
        return fail("ModeDSharedBossRushLootGuard: shared picker still does not use enemyHealth input")

    fill_body = extract_method_body(text, "private void FillEnemyInventoryForModeD(CharacterMainControl enemy, int qualityLevel, float enemyHealth, int maxItemsToAdd)")
    if fill_body is None:
        return fail("ModeDSharedBossRushLootGuard: missing FillEnemyInventoryForModeD body")

    if "TryCreateBossRushStyleInventoryLootItemForSharedModes(enemyHealth, out randomItem)" not in fill_body:
        return fail("ModeDSharedBossRushLootGuard: FillEnemyInventoryForModeD does not use enemyHealth-based shared BossRush-style picker")

    if "CreateRandomGlobalItemForModeD(minQ, maxQ, enemyHealth)" not in fill_body:
        return fail("ModeDSharedBossRushLootGuard: FillEnemyInventoryForModeD fallback still does not use enemyHealth-based global picker")

    if "TryCreateBossRushStyleInventoryLootItemForSharedModes(enemyHealth, out randomExtraItem)" not in equip_body:
        return fail("ModeDSharedBossRushLootGuard: EquipEnemyForModeD extra prefill items do not use enemyHealth-based shared picker")

    forbidden_snippets = [
        "SharedModeLegacyQuality5Chance",
        "SharedModeLegacyQuality6Chance",
        "BuildSharedModeLegacyLootDistribution(",
        "ApplySharedModeLegacyLowQualityDeduction(",
    ]

    for snippet in forbidden_snippets:
        if snippet in text:
            return fail("ModeDSharedBossRushLootGuard: shared mode still overrides legacy distribution -> " + snippet)

    print("ModeDSharedBossRushLootGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
