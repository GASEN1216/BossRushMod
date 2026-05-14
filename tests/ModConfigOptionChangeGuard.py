"""Guard: ModConfig option-change handling stays factored and complete."""

from pathlib import Path
import sys


SOURCE = Path("Config/Config.cs")

CONFIG_KEYS = [
    "_waveIntervalSeconds",
    "_EnableRandomBossLoot",
    "_UseLegacyBossLootProbabilities",
    "_UseInteractBetweenWaves",
    "_LootBoxBlocksBullets",
    "_InfiniteHellBossesPerWave",
    "_BossStatMultiplier",
    "_milestoneRestBonusSeconds",
    "_EnableDragonDash",
    "_UseWolfModelForWildHorn",
    "_EnableDeathWraithSystem",
    "_ModeDEnemiesPerWave",
    "_AchievementHotkey",
]


def fail(message: str) -> int:
    print("ModConfigOptionChangeGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
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


def main() -> int:
    if not SOURCE.exists():
        return fail("missing Config.cs")

    text = SOURCE.read_text(encoding="utf-8")
    on_changed = extract_method(text, "private void OnModConfigOptionsChanged(string changedKey)")
    handled = extract_method(text, "private bool IsHandledModConfigOptionKey(string changedKey)")
    log_change = extract_method(text, "private void LogModConfigOptionChanged(string changedKey)")
    post_change = extract_method(text, "private void ApplyPostModConfigOptionChange(string changedKey)")
    loader = extract_method(text, "private bool TryLoadSingleModConfigValue(string changedKey)")

    for method, label in [
        (on_changed, "OnModConfigOptionsChanged"),
        (handled, "IsHandledModConfigOptionKey"),
        (log_change, "LogModConfigOptionChanged"),
        (post_change, "ApplyPostModConfigOptionChange"),
        (loader, "TryLoadSingleModConfigValue"),
    ]:
        if not method:
            return fail("missing method: " + label)

    for snippet in [
        "if (!IsHandledModConfigOptionKey(changedKey))",
        "LogModConfigOptionChanged(changedKey);",
        "if (TryLoadSingleModConfigValue(changedKey))",
        "ApplyPostModConfigOptionChange(changedKey);",
        "StartNextWaveCountdown(false, true);",
    ]:
        if snippet not in on_changed:
            return fail("OnModConfigOptionsChanged missing token: " + snippet)

    if "changedKey == waveKey ||" in on_changed:
        return fail("OnModConfigOptionsChanged regressed to a giant key OR-chain")

    for key in CONFIG_KEYS:
        token = 'ModName + "' + key + '"'
        if token not in handled:
            return fail("handled-key helper missing: " + token)
        if token not in loader:
            return fail("loader missing: " + token)

    for snippet in [
        'ModName + "_EnableRandomBossLoot"',
        'ModName + "_UseLegacyBossLootProbabilities"',
        "RefreshBossRushLootboxPathTrackingForTrackedBosses();",
        'ModName + "_EnableDeathWraithSystem"',
        "HandleDeathWraithConfigChanged_DeathWraith();",
        'ModName + "_InfiniteHellBossesPerWave"',
        "bossesPerWave = config.infiniteHellBossesPerWave;",
    ]:
        if snippet not in post_change:
            return fail("post-change helper missing: " + snippet)

    print("ModConfigOptionChangeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
