"""ZombieModeGoalExperienceGuard: player-facing ZombieMode goal invariants.

This guard covers the issues from docs/2026-05-03_末日丧尸模式_goal执行文档.md:
- wave completion must clear only the player safe-zone radius, not wipe all ambient zombies;
- damage reduction must reduce ElementFactor_Physics instead of increasing it;
- beacon and cash text must match actual behavior;
- extraction continue must commit a real state change;
- high-value supply must not be a fake non-interactive cube;
- starter/reward equipment paths must not silently succeed with no item;
- primitive service terminals must be named as terminals in player-facing text.
"""

from pathlib import Path
import re
import sys


ROOT = Path(".")
WAVE = ROOT / "ZombieMode/ZombieModeWaveController.cs"
CLEANUP = ROOT / "ZombieMode/ZombieModeCleanup.cs"
REWARDS = ROOT / "ZombieMode/ZombieModeRewards.cs"
ENTRY = ROOT / "ZombieMode/ZombieModeEntry.cs"
CASH = ROOT / "ZombieMode/ZombieModeCashInvestmentView.cs"
EXTRACTION = ROOT / "ZombieMode/ZombieModeExtractionController.cs"
UI_HELPER = ROOT / "ZombieMode/ZombieModeUIHelper.cs"
Boss_CONTROLLER = ROOT / "ZombieMode/ZombieModeBossController.cs"
BEACON_CONFIG = ROOT / "Integration/Items/ZombieTideBeaconConfig.cs"
LOCALIZATION = ROOT / "Localization/LocalizationInjector.cs"


def read(path: Path) -> str:
    if not path.is_file():
        raise FileNotFoundError(str(path))
    return path.read_text(encoding="utf-8")


def fail(message: str) -> int:
    print("ZombieModeGoalExperienceGuard: FAIL - " + message)
    return 1


def extract_method(text: str, method_name: str) -> str:
    match = re.search(
        r"(?:private|public|internal)\s+(?:static\s+)?(?:[A-Za-z0-9_<>,\[\]\s]+\s+)?"
        + re.escape(method_name)
        + r"\s*\([^)]*\)\s*\{",
        text,
    )
    if match is None:
        return ""

    start = match.end()
    depth = 1
    index = start
    while index < len(text) and depth > 0:
        ch = text[index]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
        index += 1
    return text[start:index - 1]


def main() -> int:
    try:
        wave = read(WAVE)
        cleanup = read(CLEANUP)
        rewards = read(REWARDS)
        entry = read(ENTRY)
        cash = read(CASH)
        extraction = read(EXTRACTION)
        ui_helper = read(UI_HELPER)
        boss_controller = read(Boss_CONTROLLER)
        beacon = read(BEACON_CONFIG)
        localization = read(LOCALIZATION)
    except FileNotFoundError as exc:
        return fail("missing file: " + str(exc))

    if "CleanupZombieModeEnemiesNearPlayerSafeZone" not in cleanup:
        return fail("missing player safe-zone enemy cleanup helper")

    start_wave = extract_method(wave, "StartZombieModeWave")
    complete_wave = extract_method(wave, "CompleteZombieModeWave")
    if "CleanupZombieModeCombatEnemiesForWaveEnd(runId" in start_wave:
        return fail("StartZombieModeWave must not wipe ambient zombies")
    if "CleanupZombieModeCombatEnemiesForWaveEnd(runId" in complete_wave:
        return fail("CompleteZombieModeWave must not wipe ambient zombies")
    if "CleanupZombieModeEnemiesNearPlayerSafeZone(runId, \"CompleteWave\");" not in complete_wave:
        return fail("CompleteZombieModeWave must clear only the player safe-zone radius before reward/prep")

    damage_case = re.search(
        r"case\s+ZombieModeRewardType\.AttributeDamageReduction\s*:\s*"
        r"ApplyZombieModeAttributeReward\([^;]+;",
        rewards,
        re.S,
    )
    if damage_case is None:
        return fail("AttributeDamageReduction reward case missing")
    if "0.05f" in damage_case.group(0) and "-0.05f" not in damage_case.group(0):
        return fail("AttributeDamageReduction still applies positive ElementFactor_Physics")
    add_modifier = extract_method(rewards, "AddZombieModeAttributeModifier")
    if "percent <= 0f" in add_modifier:
        return fail("AddZombieModeAttributeModifier rejects legitimate negative reduction modifiers")

    if "使用：准备撤离" in beacon:
        return fail("Zombie Tide Beacon use text still says prepare extraction")
    if "下一波" not in beacon and "next wave" not in beacon.lower():
        return fail("Zombie Tide Beacon use text must mention starting the next wave")

    if "撤离时按 1:1 转回现金" in localization or "refunded 1:1 as Cash" in localization:
        return fail("cash prompt still promises 1:1 cash refund")

    continue_method = extract_method(extraction, "ContinueZombieModeAfterExtractionOpportunity")
    if "CloseZombieModeExtractionOpportunityAndContinue" not in continue_method:
        return fail("ContinueZombieModeAfterExtractionOpportunity must call a real continue helper")
    if re.sub(r"\s+", "", continue_method) == "ClearZombieModeExtractionOpportunityUi();":
        return fail("continue still only closes UI")

    airdrop_method = extract_method(rewards, "CreateZombieModeHighValueAirdrop")
    if "PrimitiveType.Cube" in airdrop_method or "ZombieMode_HighValueAirdrop" in airdrop_method:
        return fail("high-value map event still creates a fake cube airdrop")

    if "return grantedAny || loadout == ZombieModeStarterLoadout.Melee || loadout == ZombieModeStarterLoadout.Gunner;" in entry:
        return fail("starter loadout can still succeed without core gear")

    for required in [
        "GrantZombieModeFallbackPurificationReward",
        "BossRush_ZombieMode_Notify_RewardFallbackPurification",
    ]:
        if required not in rewards and required not in localization:
            return fail("missing reward fallback invariant: " + required)

    for forbidden in [
        '"召唤临时商人"',
        '"召唤临时护士"',
        '"临时商人"',
        '"临时护士"',
        '"Summon Temp Merchant"',
        '"Summon Temp Nurse"',
        '"Temp Merchant"',
        '"Temp Nurse"',
    ]:
        if forbidden in localization:
            return fail("player-facing temporary NPC text not renamed to terminal: " + forbidden)

    if "enableAutoSizing = true" not in ui_helper or "fontSizeMin" not in ui_helper:
        return fail("ZombieMode UI text must use shared auto-sizing to reduce small-screen overflow")
    for required in [
        "internal static GameObject CreateRect",
        "internal static TextMeshProUGUI CreateText",
        "internal static Button CreateButton",
    ]:
        if required not in ui_helper:
            return fail("ZombieModeUIHelper must centralize runtime UI helper: " + required)
    for path, text in [
        (CASH, cash),
        (ENTRY, entry),
        (EXTRACTION, extraction),
        (REWARDS, rewards),
    ]:
        for forbidden in [
            "private GameObject CreateRect",
            "private TextMeshProUGUI CreateText",
        ]:
            if forbidden in text:
                return fail(str(path) + " still carries duplicated UI helper: " + forbidden)

    for required in [
        "BossRush_ZombieMode_BossSkill_TitanShockwave",
        "BossRush_ZombieMode_BossSkill_HunterDash",
        "BossRush_ZombieMode_BossSkill_SplitterSummon",
        "BossRush_ZombieMode_BossSkill_ShielderSelfShield",
        "BossRush_ZombieMode_BossSkill_CorruptorZone",
    ]:
        if required not in boss_controller or required not in localization:
            return fail("missing boss skill readable text key: " + required)

    print("ZombieModeGoalExperienceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
