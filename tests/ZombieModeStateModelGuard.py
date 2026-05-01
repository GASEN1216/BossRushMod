from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
MOD_BEHAVIOUR = Path("ModBehaviour.cs")

REQUIRED_MODEL_SNIPPETS = [
    "public enum ZombieModeLifecyclePhase",
    "WaitingStarterChoice",
    "Active",
    "Exiting",
    "public enum ZombieModeCombatPhase",
    "InitialPreparation",
    "ExtractionOpportunity",
    "public enum ZombieModeFailureReason",
    "SuccessfulExtraction",
    "public enum ZombieModePerformanceTier",
    "public enum ZombieModeBossKind",
    "public static class ZombieModeTuning",
    "PreparationCountdownSeconds = 30f",
    "BeaconChannelDurationSeconds = 3f",
    "ExtractionCountdownSeconds = 15f",
    "public static class ZombieModePhaseGuards",
    "IsBeforeActive",
    "AllowsBeacon",
    "AllowsExtraction",
    "Boss,",
    "Projectile,",
    "Fortification,",
    "Buff",
    "public sealed class ZombieModeRunState",
    "public int RunId;",
    "public ZombieModeMapProfile MapProfile;",
    "public long PendingCashInvestment;",
    "public long ConfirmedCashInvested;",
    "public readonly List<ZombiePurificationStar> PendingPurificationStars",
    "public readonly List<ZombieModeBossInstance> CurrentWaveBossInstances",
    "public readonly List<ZombieModeSpawnPoint> EffectiveSpawnPoints",
    "public int LivingZombieCount;",
    "public ZombieModePerformanceTier PerformanceTier",
    "public float BeaconChannelStartTime;",
    "public float BeaconChannelDuration",
    "public CountDownArea ActiveExtractionArea;",
    "public Vector3 ActiveSafeZoneCenter;",
    "public int PollutionFromNatural;",
    "public int TotalPollution",
    "public ZombieModeRewardNode CurrentRewardNode;",
    "public readonly ZombieModeInsuranceState InsuranceState",
    "public readonly List<ZombieModeDropCandidate> EntityDropCleanupCandidates",
    "public string StarterAmmoCaliber",
    "public readonly List<ZombieModeRunOnlyRecord> RunOnlyObjects",
    "public sealed class ZombieModeMapProfile",
    "public string MainSceneName",
    "public Vector3[][] SafeZoneExclusionPolygons",
    "public long CashWithheldAmount;",
    "public readonly List<string> BlockingMessages",
]

REQUIRED_ENTRY_SNIPPETS = [
    "private readonly ZombieModeRunState zombieModeRunState",
    "private readonly ZombieModeEntryTransaction zombieModeEntryTransaction",
    "private static int nextZombieModeRunId",
    "public bool IsZombieModeActive",
    "public int ZombieModeCurrentRunId",
    "private bool IsZombieModeRunValid(int runId)",
    "SceneManager.GetActiveScene()",
    "BuildZombieModeMapProfile",
    "ZombieModePhaseGuards.IsActive",
    "ZombieModeFailureReason.InitializationFailed",
    "FailZombieModeBeforeActive(ZombieModeFailureReason reason)",
]


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    model_text = MODELS.read_text(encoding="utf-8")
    entry_text = ENTRY.read_text(encoding="utf-8")
    mod_text = MOD_BEHAVIOUR.read_text(encoding="utf-8")

    for snippet in REQUIRED_MODEL_SNIPPETS:
        if snippet not in model_text:
            return fail("ZombieModeStateModelGuard: model missing snippet -> " + snippet)

    for snippet in REQUIRED_ENTRY_SNIPPETS:
        if snippet not in entry_text:
            return fail("ZombieModeStateModelGuard: entry missing snippet -> " + snippet)

    if "TickZombieMode(Time.deltaTime);" not in mod_text:
        return fail("ZombieModeStateModelGuard: ModBehaviour.Update does not tick ZombieMode")

    print("ZombieModeStateModelGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
