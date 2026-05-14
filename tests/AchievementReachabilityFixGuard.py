"""Guard: achievement progress persistence and kill counting stay reachable and idempotent."""

from pathlib import Path
import sys


TRACKER = Path("Achievement/AchievementTracker.cs")
MANAGER = Path("Achievement/BossRushAchievementManager.cs")
TRIGGERS = Path("Achievement/AchievementTriggers.cs")
RUNTIME = Path("Achievement/AchievementRuntimeHooks.cs")
DRAGON_KING = Path("Integration/DragonKing/DragonKingBoss.cs")
WAVES = Path("WavesArena/WavesArena.cs")


def fail(message: str) -> int:
    print("AchievementReachabilityFixGuard: FAIL - " + message)
    return 1


def read(path: Path) -> str:
    if not path.exists():
        raise FileNotFoundError(str(path))
    return path.read_text(encoding="utf-8", errors="ignore")


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace_start = text.find("{", start)
    if brace_start < 0:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail(label + " missing token: " + snippet)
    return 0


def main() -> int:
    try:
        tracker = read(TRACKER)
        manager = read(MANAGER)
        triggers = read(TRIGGERS)
        runtime = read(RUNTIME)
        dragon_king = read(DRAGON_KING)
        waves = read(WAVES)
    except FileNotFoundError as exc:
        return fail("missing source: " + str(exc))

    for snippet in [
        "private static bool statsDirty = false;",
        "private static void MarkStatsDirty()",
        "private static bool HasUnsavedStatsChanges()",
        "private static void MarkStatsClean()",
        "if (HasUnsavedStatsChanges() && (currentTime - lastSaveTime >= AUTO_SAVE_INTERVAL))",
        "if (HasUnsavedStatsChanges())",
    ]:
        result = require(tracker, snippet, "AchievementTracker")
        if result:
            return result

    descendant_collect = extract_block(tracker, "public static void OnCollectDragonDescendantLoot")
    king_collect = extract_block(tracker, "public static void OnCollectDragonKingLoot")
    for block, label in [
        (descendant_collect, "DragonDescendant collection save"),
        (king_collect, "DragonKing collection save"),
    ]:
        if not block:
            return fail("missing " + label + " block")
        for snippet in ["MarkStatsDirty();", "SaveStats();"]:
            if snippet not in block:
                return fail(label + " must mark dirty and save immediately")
        if "TryAutoSave();" in block:
            return fail(label + " must not rely on throttled autosave")

    manager_unlock = extract_block(manager, "public static bool TryUnlock")
    manager_reset = extract_block(manager, "public static void ResetStaticCaches")
    if not manager_unlock or not manager_reset:
        return fail("missing achievement manager blocks")
    for snippet in [
        'if (achievementId != "completionist")',
        "TryUnlockCompletionistIfReady();",
        "public static void CheckCompletionistAchievement()",
        "private static void TryUnlockCompletionistIfReady()",
    ]:
        result = require(manager, snippet, "BossRushAchievementManager")
        if result:
            return result
    if "AchievementTracker.ForceSave();" not in manager_reset:
        return fail("ResetStaticCaches must force-save AchievementTracker stats before clearing")

    for snippet in [
        "private readonly HashSet<CharacterMainControl> achievementCountedBossKills",
        "private bool CheckBossKillAchievementsOnce(CharacterMainControl bossMain, string bossTypeOverride = null)",
        "private void ResetAchievementBossKillTracking()",
        "BossRushAchievementManager.CheckCompletionistAchievement();",
    ]:
        result = require(triggers, snippet, "AchievementTriggers")
        if result:
            return result
    if "ResetAchievementBossKillTracking();" not in runtime:
        return fail("achievement runtime cleanup must clear boss kill tracking")

    dragon_death = extract_block(dragon_king, "private void OnDragonKingDeath(")
    waves_death = extract_block(waves, "private void HandleBossDeath(")
    if not dragon_death or not waves_death:
        return fail("missing death handling block")
    if 'CheckBossKillAchievementsOnce(deadKing, "DragonKing")' not in dragon_death:
        return fail("DragonKing death must use achievement kill de-dup helper")
    if 'CheckBossKillAchievements("DragonKing")' in dragon_death:
        return fail("DragonKing death still directly increments achievement kills")
    if "CheckBossKillAchievementsOnce(bossMain);" not in waves_death:
        return fail("generic BossRush death must use achievement kill de-dup helper")
    if "CheckBossKillAchievements(bossType);" in waves_death:
        return fail("generic BossRush death still directly increments achievement kills")

    print("AchievementReachabilityFixGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
