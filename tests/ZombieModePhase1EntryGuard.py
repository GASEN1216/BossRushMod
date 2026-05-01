from pathlib import Path
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
HELPER = Path("ZombieMode/ZombieModeMapSelectionHelper.cs")
MAP_SHELL = Path("ZombieMode/ZombieModeMapSelection.cs")
INVITATION_CONFIG = Path("Integration/Items/ZombieTideInvitationConfig.cs")
INVITATION_USAGE = Path("Integration/Items/ZombieTideInvitationUsage.cs")
BEACON_CONFIG = Path("Integration/Items/ZombieTideBeaconConfig.cs")
INTEGRATION = Path("Integration/BossRushIntegration.cs")
MOD_BEHAVIOUR = Path("ModBehaviour.cs")
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModePhase1EntryGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    entry_text = ENTRY.read_text(encoding="utf-8")
    helper_text = HELPER.read_text(encoding="utf-8")
    map_shell_text = MAP_SHELL.read_text(encoding="utf-8")
    invitation_config_text = INVITATION_CONFIG.read_text(encoding="utf-8")
    invitation_usage_text = INVITATION_USAGE.read_text(encoding="utf-8")
    beacon_config_text = BEACON_CONFIG.read_text(encoding="utf-8")
    integration_text = INTEGRATION.read_text(encoding="utf-8")
    mod_behaviour_text = MOD_BEHAVIOUR.read_text(encoding="utf-8")
    compile_text = COMPILE.read_text(encoding="utf-8")

    required_compile = [
        "ZombieMode\\ZombieModeMapSelectionHelper.cs",
        "Integration\\Items\\ZombieTideInvitationUsage.cs",
    ]
    for snippet in required_compile:
        result = require(compile_text, snippet, "compile entry")
        if result:
            return result

    required_invitation = [
        "public const int BASE_SHOP_STOCK = 5;",
        "public static bool EnsureRuntimeRegistration()",
        "ConfigureUsage(item)",
        "item.MaxDurability = 999f;",
        "public static void InjectIntoShops(string targetSceneName = null)",
        "currentScene != \"Base_SceneV2\"",
        "TryInjectIntoShop",
        "IsBaseHubNormalMerchantShop",
    ]
    for snippet in required_invitation:
        result = require(invitation_config_text, snippet, "invitation config")
        if result:
            return result

    required_beacon = [
        "public static bool EnsureRuntimeFallbackRegistrationShell()",
        "public static bool EnsureRuntimeRegistration()",
        "ConfigureUsage(item)",
    ]
    for snippet in required_beacon:
        result = require(beacon_config_text, snippet, "beacon runtime fallback")
        if result:
            return result

    required_usage = [
        "ZombieModeMapSelectionHelper.CanOpenZombieModeMapSelection",
        "ZombieModeMapSelectionHelper.ShowZombieModeMapSelection",
    ]
    for snippet in required_usage:
        result = require(invitation_usage_text, snippet, "invitation usage")
        if result:
            return result

    required_helper = [
        "public static class ZombieModeMapSelectionHelper",
        "private static bool pendingZombieMapConfirmed = false;",
        "public static Cost CreateZombieModeCost()",
        "public static Cost CreateZombieModeFreeMapEntryCost()",
        "cost.items[0].id = BossRushItemIds.ZombieTideInvitation;",
        "public static bool ShowZombieModeMapSelection(out string failureReason)",
        "ZombieModeMapEntryClickHandler",
        "ConfirmZombieModeMapEntry",
        "entry.enabled = false;",
        "ShowZombieModeCashInvestmentPrompt(delegate",
        "StartZombieModeConfirmedMapLoad",
        "SceneLoader.Instance.LoadScene",
        "inst.CancelZombieModeMapSelectionPhase1();",
    ]
    for snippet in required_helper:
        result = require(helper_text, snippet, "independent map helper")
        if result:
            return result

    if "BossRushMapSelectionHelper" in helper_text:
        return fail("ZombieModePhase1EntryGuard: Zombie map helper must not reuse BossRushMapSelectionHelper")
    if "pendingPrepaidTicketForCurrentEntry" in helper_text or "pendingPrepaidTicketForCurrentEntry" in entry_text:
        return fail("ZombieModePhase1EntryGuard: Zombie Phase 1 must not reuse BossRush prepaid ticket state")
    if "BossRushMapSelectionHelper.CreateBossRushCost" in helper_text:
        return fail("ZombieModePhase1EntryGuard: Zombie Phase 1 must not reuse BossRush ticket cost")
    if "ReadMapSelectionViewBool(mapView, \"confirmButtonClicked\")" in helper_text:
        return fail("ZombieModePhase1EntryGuard: Zombie Phase 1 must not poll original MapSelectionView confirmation")

    required_entry = [
        "public bool CanStartZombieModeMapSelectionPhase1(out string failureReason)",
        "public bool TryBeginZombieModeMapSelectionPhase1(out string failureReason)",
        "public void MarkZombieModeMapConfirmedPhase1()",
        "public void CancelZombieModeMapSelectionPhase1()",
        "private bool ShouldPreserveZombieModeStartupForSceneLoad(Scene scene)",
        "private bool TryHandleZombieModePendingMapSceneLoaded(Scene scene, BossRushMapConfig loadedMapConfig)",
        "ZombieModeMapSelectionHelper.HasPendingZombieEntry",
        "ZombieModeLifecyclePhase.WaitingStarterChoice",
    ]
    for snippet in required_entry:
        result = require(entry_text, snippet, "entry phase state")
        if result:
            return result

    required_map_shell = [
        "pendingZombieModeEntry = true;",
        "zombieModeRunState.LifecyclePhase = ZombieModeLifecyclePhase.SelectingMap;",
        "pendingZombieModeEntry = false;",
        "zombieModeEntryTransaction.Reset();",
    ]
    for snippet in required_map_shell:
        result = require(map_shell_text, snippet, "map selection shell")
        if result:
            return result

    required_integration = [
        "ZombieTideInvitationConfig.InjectIntoShops();",
        "ZombieTideInvitationConfig.InjectIntoShops(scene.name);",
        "if (TryHandleZombieModePendingMapSceneLoaded(scene, loadedMapConfig))",
    ]
    for snippet in required_integration:
        result = require(integration_text, snippet, "integration hook")
        if result:
            return result

    result = require(mod_behaviour_text, "if (!ShouldPreserveZombieModeStartupForSceneLoad(scene))", "scene cleanup guard")
    if result:
        return result

    print("ZombieModePhase1EntryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
