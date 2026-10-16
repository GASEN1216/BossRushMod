"""Mode G 准入、原资产与原客端身份；执行回归使用真实入口和收据。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent


def body(path, signature):
    source = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for i in range(opening, len(source)):
        depth += (source[i] == "{") - (source[i] == "}")
        if depth == 0:
            return source[opening:i + 1]
    raise AssertionError(signature)


def main():
    coop = "ModeG/ModeGCoopRuntime.cs"
    module = "ModeG/ModeGRuntimeModule.cs"
    checks = [
        ("cached entry exact actual scene", coop, "private bool IsCoopEntrySceneCurrent",
         r"return BossRushCoop.HasBoundScene && !string.IsNullOrEmpty\(coopLatestToken\) && coopLatestToken == BossRushCoop.SceneToken;"),
        ("snapshot freezes actual token", coop, "private void ApplyCoopWorld(",
         r"if \(!BossRushCoop.HasBoundScene\) return;[\s\S]*?coopLatest = state;\s*coopLatestToken = BossRushCoop.SceneToken;"),
        ("entry debt before cached world gate", coop, "private void TickCoopEntry(",
         r"coop.Tick\(deltaTime\); coopCombat.Tick\(deltaTime\);\s*if \(BossRushCoop.IsClient && \(!BossRushCoop.HasBoundScene \|\| \(coopLatest != null && !IsCoopEntrySceneCurrent\)\)\) return;\s*RetryCoopAdmission"),
        ("stale entry cannot pay from cached active world", coop, "private bool QueueCoopEntry()",
         r"if \(!BossRushCoop.IsClient \|\| \(IsCoopEntrySceneCurrent && coopLatest != null && coopLatest.Active\)\) return false;"),
        ("replica cache freezes actual token", coop, "internal void ApplyCoopWorld(ModeGCoopWorld world)",
         r"if \(!BossRushCoop.HasBoundScene\) return;\s*coopWorld = world; coopWorldToken = BossRushCoop.SceneToken;"),
        ("replica cache refuses old token before native lookup", coop, "private void DriveCoopGuest()",
         r"if \(!BossRushCoop.HasBoundScene \|\| string.IsNullOrEmpty\(coopWorldToken\) \|\| coopWorldToken != BossRushCoop.SceneToken\) return;[\s\S]*?BossRushCoop.FindAi\(actor.Id\)"),
        ("start causal baseline", coop, "private void AdmitCoopParticipant(",
         r"if \(requested > sent\) return;[\s\S]*?Order = requested,[\s\S]*?if \(baseline.Order != requested\) return;"),
        ("snapshot original assets", coop, "private ModeGCoopWorld CaptureCoopWorld()",
         r"Asset = modeGRuntime.GetCoopParticipantAsset\(player.Id\), Order = order"),
        ("client frozen baseline", coop, "internal void FreezeCoopAdmission(",
         r"coopRequestedAdmissionBaseline = Math.Max\(0, unqualifiedOrder\);[\s\S]*?participant.Asset == coopRunAsset[\s\S]*?Math.Max\(coopRequestedAdmissionBaseline, participant.Order\)"),
        ("explicit bounded failure", coop, "private void RetryCoopAdmission(",
         r"Time.realtimeSinceStartup >= coopAdmissionDeadline\)\s*\{ modeGRuntime.End\(ModeGExitReason.TechnicalIntegrityLoss\); return; \}"),
        ("ordered pre-entry range", coop, "internal bool ApplyCoopCombat(",
         r"if \(fact.Order != coopAppliedOrder \+ 1\) return false;\s*if \(fact.Wave < coopEntryWave[\s\S]*?!coopEntryActors.Contains\(fact.Boss\)\)[\s\S]*?coopAppliedOrder = fact.Order"),
        ("denied refund exit", coop, "internal bool ApplyCoopCombat(",
         r"if \(fact.AdmissionDenied\)\s*\{\s*if \(coopAdmissionReady\) return false;\s*End\(ModeGExitReason.TechnicalIntegrityLoss\);"),
        ("pre-admission payment lease", coop, "private void DriveCoopGuest()",
         r"if \(coopAdmissionReady && !_firstWaveCombatStarted[\s\S]*?DisarmStartupRefund\(\); \}\s*if \(!coopAdmissionReady\) return;"),
        ("original slot before payment", "ModeG/ModeGEntry.cs", "public bool TryStartModeG()",
         r"if \(IsCoopParticipantAssetRejected\(\)\)\s*\{[\s\S]*?return false;\s*\}[\s\S]*?if \(QueueCoopEntry\(\)\) return true;"),
        ("finished owner before payment", "ModeG/ModeGEntry.cs", "public bool TryStartModeG()",
         r"if \(IsCoopRunClosedForLocalAsset\(\)\)\s*\{[\s\S]*?return false;\s*\}[\s\S]*?if \(QueueCoopEntry\(\)\) return true;"),
        ("original client identity", coop, "private void FreezeCoopAsset()",
         r"coopRunWasClient = BossRushCoop.IsClient;\s*coopRunWasConnected = BossRushCoop.IsConnected;\s*coopAdmissionReady = !coopRunWasClient;"),
        ("original host connection authority", coop, "internal bool CoopRunHasWorldAuthority",
         r"return !coopRunWasClient && \(coopRunWasConnected \? BossRushCoop.IsHost : !BossRushCoop.IsConnected\);"),
        ("stopped host closes original owner", coop, "internal bool CheckCoopAsset()",
         r"if \(coopAssetBound && !coopRunWasClient && !CoopRunHasWorldAuthority\)\s*\{\s*if \(!IsCoopAssetCurrent\) DisarmStartupRefund\(\);\s*End\(coopRunWasConnected && !BossRushCoop.IsConnected\s*\? ModeGExitReason.TechnicalIntegrityLoss : ModeGExitReason.SceneChanged\);\s*return false;\s*\}"),
        ("stopped host personal combat closed", coop, "internal static bool CanApplyPersonalCombat",
         r"coopOwner.modeGRuntime.CoopRunWasClient \? BossRushCoop.IsConnected : coopOwner.modeGRuntime.CoopRunHasWorldAuthority"),
        ("spawn frozen authority", module, "private async UniTaskVoid SpawnWaveAsync(",
         r"if \(!CoopRunHasWorldAuthority\) return;"),
        ("drive frozen client", module, "private void DriveCore(",
         r"if \(CoopRunWasClient\) \{ DriveCoopGuest\(\); return; \}"),
        ("async original client", module, "private bool CanContinueRun()",
         r"&& \(!CoopRunWasClient \|\| BossRushCoop.IsConnected\)"),
        ("async original authority", module, "private bool CanContinueRun()",
         r"&& \(CoopRunWasClient \|\| CoopRunHasWorldAuthority\)"),
        ("async original room", module, "private bool CanContinueRun()",
         r"&& \(string.IsNullOrEmpty\(coopRunRoom\) \|\| string.IsNullOrEmpty\(BossRushCoop.RunId\) \|\| coopRunRoom == BossRushCoop.RunId\)"),
        ("new room closes old core", coop, "internal bool CheckCoopAsset()",
         r"!string.IsNullOrEmpty\(coopRunRoom\) && !string.IsNullOrEmpty\(BossRushCoop.RunId\)[\s\S]*?coopRunRoom != BossRushCoop.RunId\)\s*\{ End\(ModeGExitReason.SceneChanged\); return false; \}"),
        ("fresh run actual cleanup", coop, "private void ApplyCoopWorld(",
         r"modeGRuntime.State.runId != state.Id[\s\S]*?modeGRuntime.End\(ModeGExitReason.SceneChanged\); ShutdownModeG\(\);"),
        ("terminal original owner", coop, "internal static void FinishCoopRun(",
         r"run.State.IsTerminal[\s\S]*?coopClosedRuns.Add\(run.CoopRunRoom[\s\S]*?run.CoopRunAsset\);"),
        ("committed replica freezes original Health", coop, "private void DriveCoopGuest()",
         r"_spawnTransaction.TryCommit\(actor.Slot, actor.Key, character\)\)\s*\{ coopBosses\[actor.Id\] = character; coopBossHealth\[actor.Id\] = character.Health; \}"),
        ("receipt original scene and registered Health", coop, "internal bool ApplyCoopCombat(",
         r"coopBossGeneration != BossRushCoop.SceneGeneration[\s\S]*?!coopBossHealth.TryGetValue\(fact.Boss, out health\)[\s\S]*?ReferenceEquals\(health, null\)[\s\S]*?!_state.IsRegisteredBossHealth\(health\)\) return false;"),
        ("original Health reaches actual death route", coop, "internal bool ApplyCoopCombat(",
         r"_telemetry.AcceptCoopDamage\(health, info\);\s*if \(fact.Dead\) HandleBossDead\(health, info\);"),
        ("terminal releases replica references", "ModeG/ModeGRuntimeModule_PublicApiAndShutdown.cs", "public void End(",
         r"ClearCoopBossReferences\(\);"),
        ("Unity-null original identity remains registered", "ModeG/ModeGRunState.cs", "public bool IsRegisteredBossHealth(",
         r"if \(ReferenceEquals\(health, null\)\) return false;[\s\S]*?_trackedBossByHealth.ContainsKey\(health\)"),
        ("Unity-null exact identity can settle", "ModeG/ModeGSpawnTransaction.cs", "public bool MarkKilled(",
         r"if \(ReferenceEquals\(health, null\) \|\| !_activeBosses.Remove\(health\)\) return false;[\s\S]*?_state.UnregisterTrackedBoss\(health\)"),
        ("committed replica keeps managed achievement type", module, "private bool ResolveBossAchievementType(",
         r"_spawnTransaction.TryGetCommittedKeyByHealth\(health, out key\)[\s\S]*?key == ModeGEncounterVariation.ManagedDragonDescendantKey\) bossType = \"DragonDescendant\";[\s\S]*?key == ModeGEncounterVariation.ManagedDragonKingKey\) bossType = \"DragonKing\";[\s\S]*?key == ModeGEncounterVariation.ManagedPhantomWitchKey\) bossType = \"PhantomWitch\";"),
    ]
    failures = [name for name, path, signature, pattern in checks if not re.search(pattern, body(path, signature))]
    runner = (ROOT / "tests/fixtures/ModeGEntryOwners/run.py").read_text(encoding="utf-8-sig")
    stubs = clean_source((ROOT / "tests/fixtures/ModeGEntryOwners/Stubs.cs").read_text(encoding="utf-8-sig"))
    if "private bool ApplyCoopCombat(" in stubs or "internal bool ApplyCoopCombat(" in stubs:
        failures.append("no fake combat apply")
    for signature in ("private bool ApplyCoopCombat(", "private void DeliverOrdered(", "internal bool ApplyCoopCombat(",
                      "private void DriveCoopGuest()", "private void RefundStartupPaymentOnTechnicalFailure("):
        if signature not in runner:
            failures.append("fixture actual method: " + signature)
    combat_runner = (ROOT / "tests/fixtures/ModeGCombat/run.py").read_text(encoding="utf-8-sig")
    combat_stubs = clean_source((ROOT / "tests/fixtures/ModeGCombat/Stubs.cs").read_text(encoding="utf-8-sig"))
    if "private void HandleBossDead(" in combat_stubs or "private void HandleBossDead(" not in combat_runner:
        failures.append("fixture must execute actual death route after replica destruction")
    for failure in failures:
        print("FAIL " + failure)
    print("ModeGCoopAdmissionGuard: " + ("FAIL" if failures else "PASS"))
    return bool(failures)


if __name__ == "__main__":
    raise SystemExit(main())
