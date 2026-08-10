"""Guard Mode E all-faction Boss hire ownership, attribution, and cleanup."""

from pathlib import Path
import sys


FEATURE = Path("ModeE/ModeELotteryAndHiring.cs")
BATTLE = Path("ModeE/ModeEBattle.cs")
SCALING = Path("ModeE/ModeEBattle_ScalingAndRuntime.cs")
STARTUP = Path("ModeE/ModeEStartup.cs")
LIFECYCLE = Path("ModeE/ModeELifecycle.cs")
FOLLOWER = Path("Integration/Frostmourne/FrostmourneAction.cs")
INTERACTION_HELPER = Path("Integration/Utils/NPCInteractionGroupHelper.cs")
HARMONY = Path("ModeE/ModeEHarmonyPatch.cs")


def fail(message: str) -> int:
    print("ModeEBossHiringGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for idx in range(brace, len(text)):
        if text[idx] == "{":
            depth += 1
        elif text[idx] == "}":
            depth -= 1
            if depth == 0:
                return text[start : idx + 1]
    return ""


def main() -> int:
    feature = FEATURE.read_text(encoding="utf-8")
    battle = BATTLE.read_text(encoding="utf-8")
    scaling = SCALING.read_text(encoding="utf-8")
    startup = STARTUP.read_text(encoding="utf-8")
    lifecycle = LIFECYCLE.read_text(encoding="utf-8")
    follower = FOLLOWER.read_text(encoding="utf-8")
    interaction_helper = INTERACTION_HELPER.read_text(encoding="utf-8")
    harmony = HARMONY.read_text(encoding="utf-8")

    if "SpawnEnemyCore(" in feature:
        return fail("hiring must upgrade the existing Boss instance, not spawn another")
    if "offerObject.AddComponent<ModeEBossHireInteractable>()" in feature:
        return fail("Boss hire interaction must not run InteractableBase.Awake on a bare active object")
    if "GetOrCreateStandaloneInteractable<ModeEBossHireInteractable>" not in feature:
        return fail("Boss hire interaction must reuse the safe standalone interaction helper")
    for token in [
        "childObj.SetActive(false);",
        "GetOrCreateGroupList(",
        "childObj.SetActive(true);",
    ]:
        if token not in interaction_helper:
            return fail("standalone interaction must initialize serialized lists before Awake -> " + token)
    for token in [
        "RegisterModeEBossHireOffer(character, trackedFaction, ctx.isBoss);",
        "UnregisterModeEBossHireRuntime(enemy, true);",
        "InitializeModeELotteryAndHiringRuntime();",
        "CleanupModeELotteryAndHiringRuntime();",
    ]:
        if token not in "\n".join([battle, scaling, startup, lifecycle]):
            return fail("Mode E register/cleanup symmetry missing -> " + token)

    register = extract_method(feature, "private void RegisterModeEBossHireOffer(")
    if "faction != modeEPlayerFaction" in register:
        return fail("every Mode E Boss must be hireable regardless of spawn faction")
    current = extract_method(feature, "private bool IsModeEBossHireStateCurrent(")
    if "state.Character.Team == state.Faction" not in current:
        return fail("unhired offers must remain bound to their current tracked faction")
    if "RefreshModeEBossHireOffers();" in register:
        return fail("Boss registration must update only the new offer, not scan all offers")
    if "RefreshModeEBossHireOffer(state);" not in register:
        return fail("Boss registration must initialize its own offer visibility")

    base_price = extract_method(feature, "private static int CalculateModeEBossHireBasePrice(")
    for token in [
        "MODE_E_BOSS_HIRE_REFERENCE_HEALTH = 1000f",
        "MODE_E_BOSS_HIRE_REFERENCE_PRICE = 200",
        "MODE_E_BOSS_HIRE_MIN_BASE_PRICE = 50",
        "MODE_E_BOSS_HIRE_MAX_BASE_PRICE = 2000",
        "MODE_E_BOSS_HIRE_PRICE_ROUNDING = 10",
        "maxHealth * MODE_E_BOSS_HIRE_REFERENCE_PRICE",
        "Mathf.CeilToInt(",
        "Mathf.Clamp(",
    ]:
        if token not in feature and token not in base_price:
            return fail("health-based Boss base pricing missing -> " + token)
    resolve_price = extract_method(feature, "private int ResolveModeEBossHireBasePrice(")
    for token in [
        "GetModeEMaxHealthValue(character)",
        "character.Health.MaxHealth",
        "CalculateModeEBossHireBasePrice(maxHealth)",
    ]:
        if token not in resolve_price:
            return fail("Boss max-health price snapshot missing -> " + token)

    price = extract_method(feature, "private int GetCurrentModeEBossHirePrice(")
    for token in [
        "modeEHiredBosses.Count",
        "price *= 2L;",
        "return int.MaxValue;",
    ]:
        if token not in feature and token not in price:
            return fail("overflow-safe exponential pricing missing -> " + token)
    if "MODE_E_MAX_HIRED" in feature or "MAX_HIRED_BOSS" in feature:
        return fail("hiring must not impose a gameplay count cap")

    show = extract_method(feature, "private bool ShouldShowModeEBossHireOffer(")
    if "modeEShellBalance >= price" not in show:
        return fail("offer visibility must use balance >= price")
    if "gameObject.SetActive(visible)" not in feature:
        return fail("offer list visibility must be controlled through active GameObject state")

    hire = extract_method(feature, "internal bool TryHireModeEBoss(")
    for token in [
        "TryAcquireModeEShellRuntimeTransaction(character, out owner)",
        "TryDebitModeEShell(price, owner.TransactionID)",
        "TryConvertModeEBossToPlayerFaction(",
        "character.gameObject.AddComponent<FrostmourneZombieFollower>()",
        "follower.InitializeForModeE(",
        "modeEHiredBosses[character] = state",
        "state.Owner = player",
        "MarkModeEShellTransactionCommitted(owner.TransactionID)",
        "RefundIfDebited(owner.TransactionID)",
        'ClearBusyAndReleaseModeEShellTransactionIfOwned(owner, "Boss hire finally")',
    ]:
        if token not in hire:
            return fail("hire transaction/reuse missing -> " + token)

    conversion = extract_method(feature, "private bool TryConvertModeEBossToPlayerFaction(")
    for token in [
        "character.SetTeam(modeEPlayerFaction);",
        "ai.searchedEnemy = null;",
        "ai.noticed = false;",
        "ai.forceTracePlayerDistance = 0f;",
        "UntrackModeEAliveEnemy(character, trackedFaction);",
        "TrackModeEAliveEnemy(character, modeEPlayerFaction);",
        "scalingState.registeredFaction = modeEPlayerFaction;",
        "scalingState.rewardKind = ModeEShellRewardKind.None;",
        "scalingState.rewardStateComplete = false;",
        "RegisterModeEEnemyLootHandler(character, modeEPlayerFaction);",
    ]:
        if token not in conversion:
            return fail("cross-faction Boss conversion missing -> " + token)

    rollback = extract_method(feature, "private void RollbackModeEBossFactionConversion(")
    for token in [
        "character.SetTeam(snapshot.CharacterTeam);",
        "TrackModeEAliveEnemy(character, snapshot.TrackedFaction);",
        "snapshot.ScalingState.rewardKind = snapshot.RewardKind;",
        "snapshot.AI.searchedEnemy = snapshot.SearchedEnemy;",
    ]:
        if token not in rollback:
            return fail("failed hire must restore converted Boss state -> " + token)

    attribution = extract_method(
        feature, "internal void AttributeModeEHiredBossKillToOwner(")
    for token in [
        "modeEHiredBosses.TryGetValue(damageInfo.fromCharacter, out state)",
        "state.Owner != CharacterMainControl.Main",
        "damageInfo.fromCharacter = state.Owner;",
    ]:
        if token not in attribution:
            return fail("hired Boss kill owner attribution missing -> " + token)
    for forbidden in ["foreach (", "for (", "new List<", "GetComponents"]:
        if forbidden in attribution:
            return fail("death attribution must stay O(1) -> " + forbidden)
    for token in [
        '[HarmonyPatch(typeof(Health), "Hurt", new Type[] { typeof(DamageInfo) })]',
        "[HarmonyTranspiler]",
        'AccessTools.Field(typeof(Health), "isDead")',
        "instruction.opcode != OpCodes.Stfld",
        "new CodeInstruction(OpCodes.Ldarga_S, (byte)1)",
        "new CodeInstruction(OpCodes.Call, attributeMethod)",
        "public static void AttributeKillToOwner(ref DamageInfo damageInfo)",
        "inst.AttributeModeEHiredBossKillToOwner(ref damageInfo);",
    ]:
        if token not in harmony:
            return fail("Health.Hurt death-only attribution patch missing -> " + token)
    if "public static void Prefix(ref DamageInfo damageInfo)" in harmony:
        return fail("hired Boss source must not be rewritten before damage calculation")

    team_guard = extract_method(
        feature, "internal bool ShouldBlockModeEHiredBossTeamChange(")
    for token in [
        "requestedTeam == modeEPlayerFaction",
        "modeEHiredBosses.TryGetValue(character, out state)",
        "state.Owner == CharacterMainControl.Main",
    ]:
        if token not in team_guard:
            return fail("hired managed Boss team ownership guard missing -> " + token)
    if "inst.ShouldBlockModeEHiredBossTeamChange(__instance, _team)" not in harmony:
        return fail("SetTeam patch must preserve hired Boss ownership")

    unregister = extract_method(feature, "private void UnregisterModeEBossHireRuntime(")
    if "if (refreshOffers && wasHired)" not in unregister:
        return fail("ordinary enemy death must not refresh every Boss hire offer")

    if "RefundIfDebited" in unregister or "CreditModeEShell" in unregister:
        return fail("Boss death/cleanup must never refund hire cost")
    for token in [
        "ModeEShellBalanceChanged += OnModeEBossHireShellBalanceChanged;",
        "ModeEShellBalanceChanged -= OnModeEBossHireShellBalanceChanged;",
    ]:
        if token not in feature:
            return fail("single balance subscription lifecycle missing -> " + token)

    for token in [
        "public void InitializeForModeE(",
        "followSlotCount",
        "followRepathInterval",
        "ModeESharedPathRequestInterval",
        "protected override bool TryRequestFollowPath(Vector3 destination)",
        "if (aiController != null && !aiController.enabled)",
    ]:
        if token not in follower:
            return fail("shared combat follower managed-Boss protection missing -> " + token)

    print("ModeEBossHiringGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
