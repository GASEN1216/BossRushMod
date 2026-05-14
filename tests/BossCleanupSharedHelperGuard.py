"""Guard: DragonKing and PhantomWitch share the tracked-boss cleanup skeleton."""

from pathlib import Path
import sys


HELPER = Path("Utilities/BossCleanupHelpers.cs")
DRAGON = Path("Integration/DragonKing/DragonKingBoss.cs")
PHANTOM = Path("Integration/PhantomWitch/PhantomWitchBoss.cs")


def fail(message: str) -> int:
    print("BossCleanupSharedHelperGuard: FAIL - " + message)
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
    helper = HELPER.read_text(encoding="utf-8")
    dragon = DRAGON.read_text(encoding="utf-8")
    phantom = PHANTOM.read_text(encoding="utf-8")

    shared = extract_method(helper, "public static void CleanupTrackedBossCharacter(")
    if not shared:
        return fail("missing CleanupTrackedBossCharacter helper")

    for token in [
        "HashSet<CharacterMainControl> cleanedCharacters",
        "Action<CharacterMainControl> clearRandomLootTracking",
        "Action<CharacterMainControl> finalizeLootboxTracking",
        "Action releaseAssetReference",
        "DestroyRuntimePreset(character, nameKey, presetName, logTag);",
        "UnityEngine.Object.Destroy(character.gameObject);",
        "releaseAssetReference();",
    ]:
        if token not in shared:
            return fail("shared helper missing token: " + token)

    dragon_cleanup = extract_method(dragon, "private void CleanupTrackedDragonKingCharacter(")
    phantom_cleanup = extract_method(phantom, "private void CleanupTrackedPhantomWitchCharacter(")
    if not dragon_cleanup or not phantom_cleanup:
        return fail("missing boss-specific cleanup wrapper")

    for block, label, tokens in [
        (
            dragon_cleanup,
            "DragonKing",
            [
                "BossCleanupHelpers.CleanupTrackedBossCharacter(",
                "DragonKingConfig.BossNameKey",
                '"DragonKing_Preset"',
                "ReleaseDragonKingInstance",
            ],
        ),
        (
            phantom_cleanup,
            "PhantomWitch",
            [
                "BossCleanupHelpers.CleanupTrackedBossCharacter(",
                "PhantomWitchConfig.BossNameKey",
                '"PhantomWitch_Preset"',
                "ClearBossRandomLootTracking",
                "FinalizeBossRushLootboxPathTracking",
                "ReleasePhantomWitchInstance",
            ],
        ),
    ]:
        for token in tokens:
            if token not in block:
                return fail(label + " cleanup missing token: " + token)
        if "BossCleanupHelpers.DestroyRuntimePreset(" in block:
            return fail(label + " cleanup must not duplicate runtime preset destruction")
        if "UnityEngine.Object.Destroy(character.gameObject)" in block:
            return fail(label + " cleanup must not duplicate GameObject destruction")

    print("BossCleanupSharedHelperGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
