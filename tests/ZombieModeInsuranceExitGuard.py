from pathlib import Path
import sys


CLEANUP = Path("ZombieMode/ZombieModeCleanup.cs")
WAVES = Path("ZombieMode/ZombieModeWaveController.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REWARD_PARTS)

DEBUG = Path("ZombieMode/ZombieModeDebug.cs")
EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModeInsuranceExitGuard: missing " + label + " -> " + snippet)
    return 0


def extract_cleanup_method(text: str) -> str:
    marker = "private void CleanupZombieModeRunOnlyState"
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    cleanup = CLEANUP.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    rewards = read_rewards()
    debug = DEBUG.read_text(encoding="utf-8")
    extraction = EXTRACTION.read_text(encoding="utf-8")

    for snippet in [
        "ShouldSettleZombieModeFailureInsurance(reason)",
        "SettleZombieModeFailureInsuranceShell(zombieModeRunState.RunId)",
        "reason != ZombieModeFailureReason.SuccessfulExtraction",
        "ZombieModeFailureReason.PlayerDeath",
        "ZombieModeFailureReason.ManualExit",
        "ZombieModeFailureReason.SceneSwitched",
        "ZombieModeFailureReason.UnexpectedSceneUnload",
    ]:
        result = require(cleanup, snippet, "cleanup insurance gate")
        if result:
            return result

    cleanup_method = extract_cleanup_method(cleanup)
    if not cleanup_method:
        return fail("ZombieModeInsuranceExitGuard: cannot extract CleanupZombieModeRunOnlyState")

    if cleanup_method.find("SettleZombieModeFailureInsuranceShell(zombieModeRunState.RunId)") > cleanup_method.find("InvalidateZombieModeRun()"):
        return fail("ZombieModeInsuranceExitGuard: insurance settlement must run before run invalidation")

    if "SettleZombieModeFailureInsuranceShell(runId)" in waves:
        return fail("ZombieModeInsuranceExitGuard: death path must rely on unified cleanup insurance settlement")

    for snippet in [
        "DebugResetZombieModeShell",
        "CleanupZombieModeForSceneChange(ZombieModeFailureReason.ManualExit)",
    ]:
        result = require(debug, snippet, "manual exit cleanup")
        if result:
            return result

    for snippet in [
        "CleanupZombieModeForSceneChange(ZombieModeFailureReason.SuccessfulExtraction)",
    ]:
        result = require(extraction, snippet, "successful extraction cleanup")
        if result:
            return result

    for snippet in [
        "zombieModeRunState.PurificationPoints = 0;",
        "zombieModeRunState.InsuranceState.Reset();",
        "runId <= 0 || zombieModeRunState.RunId != runId",
        "CollectZombieModeTopLevelPlayerItems()",
        "PlayerStorage.Push(item, true)",
    ]:
        result = require(rewards, snippet, "insurance settlement semantics")
        if result:
            return result

    insurance_method_start = rewards.find("private void SettleZombieModeFailureInsuranceShell")
    insurance_method = rewards[insurance_method_start:rewards.find("private List<Item> CollectZombieModeInsuranceCandidates", insurance_method_start)]
    if "IsZombieModeRunValid(runId)" in insurance_method:
        return fail("ZombieModeInsuranceExitGuard: insurance settlement must not depend on active scene validity")

    print("ZombieModeInsuranceExitGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
