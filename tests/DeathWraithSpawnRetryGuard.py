"""
Guard: Death Wraith spawning must bind to Duckov's original dead-body/tomb flow
instead of relying on scene-retry based delayed spawning.
"""

from pathlib import Path
import sys


DEATH_WRAITH_SOURCE = Path("Integration/DeathWraith/DeathWraithSystem.cs")
PATCH_SOURCE = Path("Integration/BossRushHarmonyPatch.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
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
    death_text = DEATH_WRAITH_SOURCE.read_text(encoding="utf-8")
    patch_text = PATCH_SOURCE.read_text(encoding="utf-8")

    required_death_snippets = [
        'private const string DEATH_WRAITH_LIST_SAVE_KEY = "BossRush_DeathWraith_List";',
        "public uint raidID;",
        "private readonly Dictionary<uint, CharacterMainControl> activeWraithsByRaidId",
        "private readonly Dictionary<CharacterMainControl, uint> activeWraithRaidIdByCharacter",
        "internal void NotifyOriginalDeadBodySpawnRequested_DeathWraith(DeadBodyManager.DeathInfo info)",
        "internal void NotifyOriginalDeadBodyLootboxCreated_DeathWraith(",
        "internal void NotifyOriginalDeadBodyTouched_DeathWraith(DeadBodyManager.DeathInfo info)",
        "GameplayDataSettings.Prefabs.LootBoxPrefab_Tomb",
        "LoadStoredDeathWraithInfos_DeathWraith()",
        "SaveStoredDeathWraithInfos_DeathWraith(",
    ]

    for snippet in required_death_snippets:
        if snippet not in death_text:
            return fail("DeathWraithSpawnRetryGuard: missing death-wraith snippet -> " + snippet)

    required_patch_snippets = [
        '[HarmonyPatch(typeof(DeadBodyManager), "SpawnDeadBody")]',
        '[HarmonyPatch(typeof(InteractableLootbox), "CreateFromItem")]',
        '[HarmonyPatch(typeof(DeadBodyManager), "NotifyDeadbodyTouched")]',
        "NotifyOriginalDeadBodySpawnRequested_DeathWraith",
        "NotifyOriginalDeadBodyLootboxCreated_DeathWraith",
        "NotifyOriginalDeadBodyTouched_DeathWraith",
    ]

    for snippet in required_patch_snippets:
        if snippet not in patch_text:
            return fail("DeathWraithSpawnRetryGuard: missing patch snippet -> " + snippet)

    forbidden_snippets = [
        "DEATH_WRAITH_SCENE_MATCH_RETRY_INTERVAL_SECONDS",
        "DEATH_WRAITH_SCENE_MATCH_RETRY_MAX_SECONDS",
        "TryRequestDeathWraithSpawnForActiveScene_DeathWraith",
        "DelayedSpawnDeathWraith_DeathWraith",
        "TryEvaluateDeathWraithSceneMatch_DeathWraith",
        "OnSubSceneLoaded_DeathWraith(MultiSceneCore core, Scene scene)",
        "等待子场景就绪后重试亡魂生成",
        "当前子场景尚未就绪",
    ]

    for snippet in forbidden_snippets:
        if snippet in death_text:
            return fail("DeathWraithSpawnRetryGuard: obsolete retry-based snippet still present -> " + snippet)

    touched_block = extract_block(
        death_text,
        "internal void NotifyOriginalDeadBodyTouched_DeathWraith(DeadBodyManager.DeathInfo info)",
    )
    if not touched_block:
        return fail("DeathWraithSpawnRetryGuard: missing NotifyOriginalDeadBodyTouched_DeathWraith block")

    if "RemoveStoredDeathWraithInfoByRaidId_DeathWraith" not in touched_block:
        return fail("DeathWraithSpawnRetryGuard: touched handler must clear the stored raid-bound wraith record")

    if "DestroyWraithInstance_DeathWraith" in touched_block:
        return fail("DeathWraithSpawnRetryGuard: touched handler must not destroy an already spawned guarding wraith")

    if "ForgetActiveWraith_DeathWraith" in touched_block:
        return fail("DeathWraithSpawnRetryGuard: touched handler must not unregister an already spawned guarding wraith")

    create_block = extract_block(
        death_text,
        "internal void NotifyOriginalDeadBodyLootboxCreated_DeathWraith(",
    )
    if not create_block:
        return fail("DeathWraithSpawnRetryGuard: missing NotifyOriginalDeadBodyLootboxCreated_DeathWraith block")

    if "LootBoxPrefab_Tomb" not in create_block:
        return fail("DeathWraithSpawnRetryGuard: lootbox-created handler must explicitly gate on the tomb prefab")

    if "TrySpawnStoredDeathWraithForRaid_DeathWraith" not in create_block:
        return fail("DeathWraithSpawnRetryGuard: lootbox-created handler must hand off to a raid-bound spawn helper")

    print("DeathWraithSpawnRetryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
