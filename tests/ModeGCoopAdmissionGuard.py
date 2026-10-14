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
         r"coopRunWasClient = BossRushCoop.IsClient;\s*coopAdmissionReady = !coopRunWasClient;"),
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
    for failure in failures:
        print("FAIL " + failure)
    print("ModeGCoopAdmissionGuard: " + ("FAIL" if failures else "PASS"))
    return bool(failures)


if __name__ == "__main__":
    raise SystemExit(main())
