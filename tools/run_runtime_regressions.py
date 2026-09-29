"""Aggregate production-linked regression fixtures without launching the game."""
from pathlib import Path
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import json
import os
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parent.parent
SCRIPT_FIXTURES = (
    "DeathWraithPersistence",
    "ModeDEntryOwnership",
    "PetNestGrowth", "ModeHPreparedEquipment", "EntryAndReforgeCompatibility", "BackMountainMorph", "JeffQuestFlow", "SaveFailureRecovery", "F3MapTourJudges",
    "RuntimeRegressionRunner", "HostDestroyOwnership", "ModeDestroyLifecycle", "ModuleOwnerCleanup",
    "HostMapArenaOwners", "HostUtilityOwners",
    "AuditCoreParsing", "AuditModeLifecycle", "AuditCombatSeptember", "NpcAuditFixes", "GoblinRuntimeModule",
    "ModeHSceneEntry", "ModeHPlayerFlow", "AchievementRuntime", "ArenaHostRemainder", "ModeGEntryOwners", "AudioRuntime",
    "ResourceProduction", "ZombieModeHostOwners", "ModeEFEnemyRegistry", "SteamPlatformInfo",
    "ManualSeptemberReview", "GardenHarvestNotice",
    "AffixSelectionUI", "ManualEquipmentRecovery", "AchievementIcons", "DynamicItemInitialization", "SkyIslandSceneReferenceBridge", "RandomEventsFailure", "RandomEventTempo",
    "RuntimeOwnership", "BossFilterRuntime", "ModeRuntimeDispatch", "EquipmentConfiguratorRegistry", "SharedModalInput", "NPCShopPayment", "ContentTransactions", "BackMountainLifecycle", "ModeGCombat", "CampaignPlayability", "BossRewardDelivery", "AffixCombat", "ContentSecondReview", "AirdropSecondReview",
    "HarmonyBindingSecondReview", "ModeHReinforcementSecondReview", "modeh_effects",
    "ModeHThirdReviewFixes", "ModeHMarketAudit", "ModeHItemBetLedger", "ContentThirdReviewFixes", "IntegrationThirdReviewFixes", "IntegrationLeafOwners",
    "ContentBuildingOwnership", "DailyReportHostUI", "BuildingRestoreCore", "F3ValidationExecution", "SetBonusCoroutines", "GameplayLogFixes",
    "StoneOutpostSceneLease", "StoneOutpostMap", "EquipmentResourceScene", "SkyIslandStory", "SkyIslandDelivery", "SkyIslandOfficialContract", "SkyIslandEncounters", "SkyIslandLighting", "SkyIslandRaidLease", "SkyIslandLoot",
    "SkyIslandMarriage", "SkyIslandHudPolicy", "SkyIslandDialogue", "SkyIslandInteraction", "SkyIslandCombatRuntime", "ZombieModeEntryDebt", "ZombieModeSafeZoneRuntime", "ZombieModeRewardRuntime", "ZombieModeSpawnRuntime", "ZombieModeStarterRuntime", "PermanentDuckNpcDialogue", "RewardPoolReliability",
    "SkyIslandValidationJudges", "F3AutotestJudges", "SpawnPositionPolicy", "EnemySpawnRuntime", "EnemyRecoveryRuntime", "RandomEventEffectsOwners", "ModeEFSpawnPreparation", "ModeEFEnemySpawnRuntime", "ModeEFSpawnPostprocessScheduler", "ModeEFVirtualSpawnerRegistry", "ModeEFMerchantCatalog", "FlightTotemRuntimeModule", "EquipmentBootstrapOwners", "AwenLootSweepRuntime", "WavesArenaPresetWeight", "BirthdayCakeGift",
)
PROJECT_FIXTURES = {
    "ModeHCombatRelease": "ModeHCombatRelease.csproj",
    "ReviewSeptember": "ReviewSeptember.csproj",
    "ModeHReviewFixes": "Review.csproj",
    "ModeHRecoverySecondReview": "Review.csproj",
}


def run_command(command, cwd):
    result = subprocess.run(command, cwd=cwd, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, timeout=600,
                            env=dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONUTF8="1"))
    return result.returncode, result.stdout.decode("utf-8", errors="replace")


def run_project_fixture(project, output_root, cwd):
    """Build once in a fresh directory, then execute that build's actual TargetPath.

    dotnet run can select the default bin executable despite output property
    overrides. Keeping build and execution explicit also makes old fixture bin/obj
    harmless. Querying TargetPath supports an AssemblyName different from the project.
    """
    output_root.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="run-", dir=output_root)).resolve()
    binary_dir = invocation / "bin"
    code, output = run_command([
        "dotnet", "build", str(project), "--configuration", "Release", "--nologo",
        "--output", str(binary_dir),
        "-p:BaseIntermediateOutputPath=" + str(invocation / "obj") + os.sep,
        # -getProperty alone only evaluates a project: an explicit target is required.
        "-target:Build", "-getProperty:TargetPath,TargetFramework",
    ], cwd)
    if code:
        return code, output
    try:
        # MSBuild writes the property JSON last, after warnings/custom target output.
        properties = json.loads(output[output.rfind("\n{") + 1:].strip())["Properties"]
        target = Path(properties["TargetPath"]).resolve()
        if not target.is_relative_to(binary_dir) or not target.is_file():
            raise ValueError("TargetPath is missing or outside this build's output: " + str(target))
    except (KeyError, ValueError, TypeError) as error:
        return 1, output + "\nRuntime fixture build did not provide a fresh TargetPath: " + str(error)
    output += "\nExecute fresh TargetPath: " + str(target)
    output += "\nSHA-256: " + hashlib.sha256(target.read_bytes()).hexdigest() + "\n"
    code, execution_output = run_command(["dotnet", str(target)], cwd)
    return code, output + execution_output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--filter", default="", help="Run names containing this text")
    parser.add_argument("--jobs", type=int, default=3, help="Concurrent fixtures (default 3)")
    parser.add_argument("--list", action="store_true", help="List selected fixtures without running")
    args = parser.parse_args()
    names = sorted(name for name in (*SCRIPT_FIXTURES, *PROJECT_FIXTURES)
                   if args.filter.casefold() in name.casefold())
    if not names:
        parser.error("No matching fixtures")
    if args.list:
        print("\n".join(names))
        return 0
    out = ROOT / "Build/runtime-regressions"
    out.mkdir(parents=True, exist_ok=True)

    def run(name):
        here = ROOT / "tests/fixtures" / name
        try:
            if name in PROJECT_FIXTURES:
                code, output = run_project_fixture(here / PROJECT_FIXTURES[name], out / name, ROOT)
            else:
                code, output = run_command([sys.executable, str(here / "run.py")], ROOT)
        except (OSError, subprocess.TimeoutExpired) as error:
            code, output = 1, str(error)
        (out / (name + ".log")).write_text(output, encoding="utf-8")
        return name, code, output

    results = {}
    with ThreadPoolExecutor(max_workers=max(1, min(args.jobs, len(names)))) as pool:
        for future in as_completed([pool.submit(run, name) for name in names]):
            name, code, output = future.result()
            results[name] = code
            print(("PASS " if code == 0 else "FAIL ") + name, flush=True)
            if code:
                print(output[-5000:], flush=True)
    (out / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    failures = sorted(name for name, code in results.items() if code)
    print(f"Runtime regressions: {len(results) - len(failures)} PASS / {len(failures)} FAIL")
    if failures:
        print("Failed: " + ", ".join(failures))
    print("Logs: " + str(out))
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
