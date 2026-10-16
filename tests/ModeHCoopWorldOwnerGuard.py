"""H 世界冻结原生房间与角色，原押注/收据保留既有恢复管线。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(path, signature):
    text = clean_source((ROOT / path).read_text(encoding="utf-8-sig"))
    assert text.count(signature) == 1, signature
    start = text.index(signature)
    opening = text.index("{", start)
    depth = 0
    for end in range(opening, len(text)):
        depth += (text[end] == "{") - (text[end] == "}")
        if depth == 0:
            return text[opening:end + 1]
    raise AssertionError(signature)


def main():
    coop = "ModeH/ModeHRuntimeModule_Coop.cs"
    core = "ModeH/ModeHRuntimeModule.cs"
    scene = "ModeH/ModeHRuntimeModule_SceneFlow.cs"
    recovery = "ModeH/ModeHRuntimeModule_Recovery.cs"
    checks = [
        ("projection cache exact actual scene", coop, "private bool IsCoopProjectionSceneCurrent",
         r"return BossRushCoop.HasBoundScene && \(!_coopProjection \|\| !string.IsNullOrEmpty\(_coopProjectionToken\) && _coopProjectionToken == BossRushCoop.SceneToken\);"),
        ("personal obligations tick before cached world gate", coop, "private void TickCoop(",
         r"_coopLedger.Tick\(\);\s*_coopResults.Tick\(deltaTime\);[\s\S]*?if \(!IsCoopProjectionSceneCurrent\) \{ _coopChannel.Tick\(deltaTime\); return; \}[\s\S]*?EnsureCoopHostSpectator\(\);"),
        ("projection capture actual token", coop, "private void ApplyCoopWorld(",
         r"if \(!BossRushCoop.IsClient \|\| !BossRushCoop.HasBoundScene \|\| state == null\) return;[\s\S]*?_coopView = state;\s*_coopProjectionToken = BossRushCoop.SceneToken;"),
        ("delayed paid prepare refuses stale projection", coop, "private void TryPrepareCoopBet(",
         r"if \(BossRushCoop.IsConnected && !IsCoopProjectionSceneCurrent\) return;[\s\S]*?_coopLedger.Prepare\("),
        ("queued ready refuses stale projection", coop, "private bool RequestCoopStart()",
         r"if \(!IsCoopProjectionSceneCurrent\) return true;[\s\S]*?_coopChannel.Request\(\"ready\""),
        ("cleanup retires projection token", coop, "private void ReleaseCoopProjection()",
         r"_coopProjection = false; _coopProjectionRoom = null; _coopProjectionToken = null;"),
        ("freeze native owner", coop, "private void BindCoopWorldAsset()",
         r"_coopWorldRoom = BossRushCoop.RunId;\s*_coopWorldWasConnected = BossRushCoop.IsConnected;"),
        ("projection never owns world", coop, "private bool IsCoopWorldAuthorityCurrent",
         r"if \(_coopProjection\) return false;"),
        ("original offline world cannot enter new network", coop, "private bool IsCoopWorldAuthorityCurrent",
         r"if \(!_coopWorldWasConnected\) return !BossRushCoop.IsConnected;"),
        ("host and original room with empty welcome grace", coop, "private bool IsCoopWorldAuthorityCurrent",
         r"return BossRushCoop.IsHost && \(string.IsNullOrEmpty\(BossRushCoop.RunId\)\s*\|\| string.IsNullOrEmpty\(_coopWorldRoom\) \|\| _coopWorldRoom == BossRushCoop.RunId\);"),
        ("active resume not skipped", coop, "private bool CheckCoopWorldAuthority()",
         r"if \(_coopProjection \|\| \(_restoredSeasonPending && !_resumeScenePending\) \|\| !HasActiveRun\s*\|\| IsCoopWorldAuthorityCurrent\) return true;"),
        ("transport loss uses recoverable technical abort", coop, "private bool CheckCoopWorldAuthority()",
         r'RequestExit\(ModeHExitReason.TechnicalAbort, "coop_world_owner_changed"\);\s*return false;'),
        ("actual tick checks before offline return", coop, "private void TickCoop(",
         r"if \(!CheckCoopWorldAuthority\(\)\) return;\s*if \(!BossRushCoop.IsConnected\)"),
        ("async callback refuses invalid world", scene, "private bool IsCallbackStillValid(",
         r"if \(!IsCoopWorldAuthorityCurrent\) return false;"),
        ("settlement transition refuses invalid world", core, "internal bool TryTransition(",
         r"if \(_runState == null \|\| !IsCoopWorldAuthorityCurrent\) return false;"),
        ("commands cannot migrate world", coop, "private void ExecuteCoopCommand(",
         r"if \(!BossRushCoop.IsHost \|\| !HasActiveRun \|\| _commandsClosed \|\| command == null\s*\|\| !IsCoopWorldAuthorityCurrent\) return;"),
        ("world results cannot migrate", coop, "private void PublishCoopResult(",
         r"if \(!BossRushCoop.IsHost \|\| battle == null \|\| _runState == null \|\| !IsCoopWorldAuthorityCurrent\) return;"),
        ("explicit resume freezes before scene await", recovery, "private void BeginSeasonResumeScene()",
         r"BindCoopWorldAsset\(\);[\s\S]*LoadSeasonResumeScene\(_runState.OwnerToken"),
        ("guest cannot create restored world", recovery, "private void BeginSeasonResumeScene()",
         r'if \(BossRushCoop.IsClient\) \{ PresentRecoveryFailure\("season_resume_host_required"\); return; \}'),
        ("resume await bound to original world", recovery, "private bool IsSeasonResumeRequestCurrent(",
         r"&& IsCoopWorldAuthorityCurrent\s*&& _runState.OwnerToken == ownerToken"),
    ]
    failures = [name for name, path, signature, pattern in checks if not re.search(pattern, body(path, signature))]
    close = body(coop, "private bool CheckCoopWorldAuthority()")
    if any(token in close for token in (".Forfeit(", ".Apply(", ".Dispose(", "_coopLedger =", "_coopResults =")):
        failures.append("owner loss cannot consume or discard personal obligations")
    runner = (ROOT / "tests/fixtures/ModeHCoop/run.py").read_text(encoding="utf-8-sig")
    for signature in ("private async UniTask PrepareGroupCustomUnitAsync(", "private bool IsCallbackStillValid(",
                      "partial void ShutdownRuntimeInternal(", "private void TryReturnRealStakeOnAbort(",
                      "private void BeginSeasonResumeScene()", "private void CancelSeasonResume()", "private void TryPrepareCoopBet("):
        if signature not in runner:
            failures.append("fixture actual method: " + signature)
    for failure in failures:
        print("FAIL " + failure)
    print("ModeHCoopWorldOwnerGuard: " + ("FAIL" if failures else "PASS"))
    return bool(failures)


if __name__ == "__main__":
    raise SystemExit(main())
