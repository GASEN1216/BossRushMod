"""Mode G 生产流程关键接线；行为边界由 ModeGCombat 夹具执行。"""
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
    checks = [
        ("LethalBeforeUnregister", "ModeG/ModeGCombatTelemetry.cs", "private void HandleOnDead(",
         r"RecordDirectDamage\(health, info\);\s*if \(_onBossDeadCallback != null\) _onBossDeadCallback\(health, info\);"),
        ("NoLethalDoubleCount", "ModeG/ModeGCombatTelemetry.cs", "private void HandleOnHurt(",
         r"if \(health.IsDead\) return;\s*RecordDirectDamage\(health, info\);"),
        ("SettledDamage", "ModeG/ModeGCombatTelemetry.cs", "private void RecordDirectDamage(",
         r"float amount = info\.finalDamage;\s*if \(amount <= 0f \|\| float.IsNaN\(amount\) \|\| float.IsInfinity\(amount\)\) return;"),
        ("SharedValidity", "ModeG/ModeGCombatTelemetry.cs", "public bool IsWaveScoreValid",
         r"return !IsTelemetryDegraded && !ContaminatedByCharacterSwitch;"),
        ("SettlementValidity", "ModeG/ModeGRuntimeModule.cs", "private void SettleCurrentWave()",
         r"if \(resolved && _telemetry.IsWaveScoreValid && _adaptive.RecordResolve\(axis\)\)"),
        ("RewardBeforeDispatcher", "ModeG/ModeGRuntimeModule.cs", "public bool Initialize(",
         r"_rewardPlan = ModeGRewardTransaction.BuildSlotPlan\(state.runSeed,\s*ModeGAdaptiveCombat.MaxResolveTotal, _host.GetModeGRewardCandidates\(\)\);"
         r"[\s\S]*?if \(_rewardPlan == null \|\| _rewardPlan.Count != ModeGRewardTransaction.SlotCount\)\s*\{[^{}]*?return false;\s*\}"
         r"[\s\S]*?ModBehaviour.ManagedBossSpawnDispatcher = _dispatcherRef;"),
        ("FrozenRewardConsumption", "ModeG/ModeGDeathRouting.cs", "private static void SubmitVictoryReward(",
         r"var plan = module.BuildVictoryRewardPlan\(resolve\);"),
        ("RewardPrefab", "ModeG/ModeGSpawnTransaction.cs", "internal List<ModeGRewardCandidate> GetModeGRewardCandidates()",
         r"ItemAssetsCollection.GetPrefab\(typeId\);\s*if \(prefab == null \|\| prefab.TypeID != typeId\) continue;"),
        ("OfficialModalInput", "ModeG/ModeGEntry.cs", "private void TryHandleModeGAbandonHotkey()",
         r"if \(BossRushUI.IsOfficialHudHidden\(\) \|\| BossRushUI.IsGamePaused\(\)\) return;"),
        ("AttributeTerminalHint", "ModeG/ModeGHUD.cs", "private static string ComposeObjectiveLine(",
         r"if \(m.objectiveState == ModeGObjectiveState.ThresholdsMet\s*&& m.axis == ModeGCounterAxis.Distance\)"),
    ]
    failures = []
    for name, path, signature, pattern in checks:
        if not re.search(pattern, body(path, signature)):
            failures.append(name + ": " + path)
    for failure in failures:
        print("FAIL " + failure)
    print("ModeGProductionFlowGuard: " + ("FAIL" if failures else "PASS"))
    return bool(failures)


if __name__ == "__main__":
    raise SystemExit(main())
