from pathlib import Path
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
MAP_ISOLATION = Path("ZombieMode/ZombieModeMapIsolation.cs")
REGISTRY = Path("Integration/NPCs/Common/NPCModuleRegistry.cs")
COURIER = Path("Integration/NPCs/Courier/CourierNPC.cs")
GOBLIN = Path("Integration/NPCs/Goblin/GoblinNPC.cs")
NURSE = Path("Integration/NPCs/Nurse/NurseNPC.cs")
RUNNER = Path("Integration/NPCs/Courier/CourierLootSweepRunner.cs")
INTEGRATION = Path("Integration/BossRushIntegration.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    entry_text = ENTRY.read_text(encoding="utf-8")
    map_isolation_text = MAP_ISOLATION.read_text(encoding="utf-8")
    registry_text = REGISTRY.read_text(encoding="utf-8")
    courier_text = COURIER.read_text(encoding="utf-8")
    goblin_text = GOBLIN.read_text(encoding="utf-8")
    nurse_text = NURSE.read_text(encoding="utf-8")
    runner_text = RUNNER.read_text(encoding="utf-8")
    integration_text = INTEGRATION.read_text(encoding="utf-8")

    for snippet in [
        "public bool IsAnyBossRushLikeModeActive()",
        "public bool UsesArenaSupportNpcPlacement()",
        "public bool ShouldSuppressBaseNpcSpawnForCurrentMode()",
        "IsZombieModeActive",
    ]:
        if snippet not in entry_text:
            return fail("ZombieModeNpcHelperGuard: helper missing snippet -> " + snippet)

    if "mod.IsAnyBossRushLikeModeActive()" not in registry_text:
        return fail("ZombieModeNpcHelperGuard: registry courier module does not use unified active helper")

    for name, text in [("registry", registry_text), ("courier", courier_text), ("goblin", goblin_text), ("nurse", nurse_text), ("runner", runner_text)]:
        if "UsesArenaSupportNpcPlacement()" not in text and "mod.UsesArenaSupportNpcPlacement()" not in text:
            return fail("ZombieModeNpcHelperGuard: " + name + " does not use arena support NPC helper")

    if "ShouldSuppressBaseNpcSpawnForCurrentMode()" not in integration_text:
        return fail("ZombieModeNpcHelperGuard: normal-mode delayed NPC spawn does not use suppression helper")

    for snippet in [
        "ShouldPreserveZombieModeOriginalCharacter",
        "IsZombieModeRetainedNeutralWhitelisted",
        "RetainedNeutralWhitelistTypes",
        "Team.IsEnemy(Teams.player, character.Team)",
        "character.GetComponentInChildren<DuckovDialogueActor>(true)",
        "character.GetComponentInChildren<Duckov.Economy.StockShop>(true)",
        "behaviour is IMerchant",
        "typeName.IndexOf(\"Quest\"",
        "typeName.IndexOf(\"Dialogue\"",
        "typeName.IndexOf(\"Merchant\"",
        "typeName.IndexOf(\"Npc\"",
    ]:
        if snippet not in map_isolation_text:
            return fail("ZombieModeNpcHelperGuard: map isolation NPC preservation missing snippet -> " + snippet)

    print("ZombieModeNpcHelperGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
