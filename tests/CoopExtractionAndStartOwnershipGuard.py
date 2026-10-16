"""F/Z 断线撤离资格与 H/Z 房主跳过未确认者的个人退款边界。"""
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(path, signature):
    source = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for end in range(opening, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return " ".join(source[opening:end + 1].split())
    raise AssertionError(signature)


def main():
    f = "ModeF/ModeFRuntimeModule_Coop.cs"
    z = "ZombieMode/ZombieModeRuntimeModule_Coop.cs"
    h = "ModeH/ModeHRuntimeModule_Coop.cs"
    ledger = "ModeH/ModeHCoopLedger.cs"
    checks = [
        (f, "private bool IsCoopModeFExtractionRecipient(", "state.ExtractionLastAlive && (state.ExtractionLastInside || state.ExtractionCountdownQualified)"),
        (f, "private bool IsCoopModeFExtractionRecipient(", "!state.Admitted || state.Declined || state.Dead"),
        (f, "private void CompleteCoopModeFExtraction(", "IsCoopModeFExtractionRecipient(state)"),
        (f, "private void CompleteCoopModeFExtraction(", "Kills = state.Kills }, state.AssetId)"),
        (f, "private void TickCoopModeF(", "RefreshCoopModeFExtractionEligibility();"),
        (z, "private bool IsCoopZombieExtractionRecipient(", "state.LastAlive && (state.LastInside || state.CountdownQualified)"),
        ("ZombieMode/ZombieModeRuntimeModule_Extraction.cs", "private void CompleteZombieModeExtractionSuccess(", "IsCoopZombieExtractionRecipient(participant.Key, participant.Value)"),
        ("ZombieMode/ZombieModeRuntimeModule_Extraction.cs", "private void CompleteZombieModeExtractionSuccess(", "Amount = issued }, participant.Value)"),
        (z, "public override void OnUpdate(", "RefreshCoopZombieExtractionEligibility(runState.ActiveExtractionArea);"),
        (h, "private void ExecuteCoopCommand(", "_coopExited.Contains(command.PlayerId) || IsCoopSkipped(command.PlayerId, command.AssetId)"),
        (h, "private void StartCoopWithConfirmedPlayers(", "!IsCoopWorldAuthorityCurrent"),
        (h, "private void QueueCoopSkippedRefunds(", "Skipped = true }, skipped.Value)"),
        (h, "private void DecorateCoopBetPage(", "OnClick = StartCoopWithConfirmedPlayers"),
        (h, "private bool ApplyCoopResult(", "result.Skipped) return _coopLedger.CancelUnstarted(result.Run, result.Match, result.Seed)"),
        (ledger, "internal bool CancelUnstarted(", "if (bet.combatStarted != 0) return false;"),
        (ledger, "internal bool CancelUnstarted(", 'if (!ModeHCashBetService.TryRefund("coop_unconfirmed", out refunded)) return false;'),
        (ledger, "internal bool CancelUnstarted(", "ModeHItemBetStake.ReleaseLocked();"),
        (z, "private bool RegisterCoopZombieStarter(", "coopZombieSkipped.Contains(CoopZombieParticipantKey(player))"),
        (z, "private bool RegisterCoopZombieStarter(", "coopZombieDeclinedAssets.Contains(CoopZombieParticipantKey(player))"),
        (z, "private void ResetCoopZombieRun(", "coopZombieSkipped.Clear();"),
        (z, "private bool ApplyCoopZombieSkippedEntry(", "RollbackZombieModeInventoryTransfer();"),
        (z, "private bool ApplyCoopZombieSkippedEntry(", "Cash = entryTransaction.CashTemporarilyHeld ? entryTransaction.CashWithheldAmount : 0L"),
        (z, "private void QueueCoopZombieSkippedRefunds(", 'Kind = "skip-entry" }, skipped.Value)'),
        (z, "private void ApplyCoopZombieStarterConfirmation(", "coopZombieParticipants[player.Id] = player.AssetId; coopZombieStarterReady.Add(key);"),
        (z, "private void CommitCoopZombieStarterIfConfirmed(", "if (BossRushCoop.IsHost ? !coopZombieStarterReady.Contains(key) : coopZombieConfirmedStarter == null || Array.IndexOf(coopZombieConfirmedStarter, key) < 0) return;"),
        (z, "private void ShowCoopZombieStartConfirmation(", "StartCoopZombieWithConfirmedPlayers(run)"),
        (z, "private void ApplyCoopZombieWorld(", "ApplyCoopZombieAdmissionState(state.Run, state.StarterReady, state.Skipped)"),
        (z, "private void ApplyCoopZombieWorld(", "RetireCoopZombieUnconfirmedEntry(state.Run);"),
        (z, "private void RetireCoopZombieUnconfirmedEntry(", "ApplyCoopZombieSkippedEntry(coopZombieLocalRun);"),
        (z, "private bool ApplyCoopZombieAdmissionState(", "CoopZombieContainsPlayer(skipped, BossRushCoop.LocalPlayerId)"),
        (z, "private bool ApplyCoopZombieAdmissionState(", "CoopZombieContainsPlayer(confirmed, BossRushCoop.LocalPlayerId) && Array.IndexOf(confirmed, key) < 0"),
        (z, "private bool ApplyCoopZombieSkippedEntry(", "NotifyCoopZombieSkippedEntry(true, coopZombieRefunds.ContainsKey(refundKey));"),
        ("ZombieMode/ZombieModeEntry_StarterLoadout.cs", "internal void ShowZombieModeStarterChoice(", "CanOfferCoopZombieStart(runId)"),
        ("ZombieMode/ZombieModeEntry_StarterLoadout.cs", "private void BuildCoopHostWaiting(", "if (KeepCoopHostStartChoice() && startCoop != null) startCoop();"),
        ("ZombieMode/ZombieModeEntry_StarterLoadout.cs", "private void CreateLoadoutCard(", "if (KeepCoopHostStartChoice()) { BuildCoopHostWaiting(); ClaimInputAndPause(); }"),
        ("ZombieMode/ZombieModeEntry_StarterLoadout.cs", "private bool KeepCoopHostStartChoice(", "return chosen && canStartCoop != null && canStartCoop();"),
        (z, "private bool CanOfferCoopZombieStart(", "coopZombieStarterGranted && runState.LifecyclePhase == ZombieModeLifecyclePhase.WaitingStarterChoice"),
        (z, "private void ShowCoopZombieStartConfirmation(", "Anchor = anchor"),
        (z, "private void ShowCoopZombieStartConfirmation(", "token == BossRushCoop.SceneToken"),
        (z, "private static void NotifyCoopZombieSkippedEntry(", ": refundPending ? L10n.T("),
        (z, "private void ApplyCoopZombieWorld(", "if (!coopZombieStarterGranted) return;"),
        (z, "private bool RegisterCoopZombieStarter(", "coopZombieSkippedAssets.ContainsKey(player.Id)"),
    ]
    failures = [signature + " missing " + token for path, signature, token in checks if token not in body(path, signature)]
    assert "ZombieModeLifecyclePhase.WaitingStarterChoice" not in body(z, "private bool RegisterCoopZombieStarter("), "original Active late admission must remain legal"
    select = body("ZombieMode/ZombieModeEntry_StarterLoadout.cs", "public void SelectZombieModeStarterLoadout(")
    assert select.index('SendCoopZombieCommand("starter")') < select.index("CommitCoopZombieStarterIfConfirmed(runId)") < select.index("GrantZombieModeStarterLoadout(loadout)"), "network selection must await host; solo keeps original grant"
    for failure in failures:
        print("FAIL " + failure)
    print("CoopExtractionAndStartOwnershipGuard: " + ("FAIL" if failures else "PASS") + " (" + str(len(checks) + 2) + " boundaries)")
    return bool(failures)


if __name__ == "__main__":
    raise SystemExit(main())
