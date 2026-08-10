"""Guard Mode E shell boss rewards: final preset kind, sessionGeneration only, claim-first, no-throw settle."""

from pathlib import Path
import sys


BATTLE = Path("ModeE/ModeEBattle.cs")
SCALING = Path("ModeE/ModeEBattle_ScalingAndRuntime.cs")
MODE_E = Path("ModeE/ModeE.cs")
SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")


def fail(message: str) -> int:
    print("ModeEShellRewardGuard: FAIL - " + message)
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
    battle = BATTLE.read_text(encoding="utf-8")
    scaling = SCALING.read_text(encoding="utf-8")
    mode_e = MODE_E.read_text(encoding="utf-8")
    support = SUPPORT.read_text(encoding="utf-8")
    combined = battle + "\n" + scaling + "\n" + mode_e + "\n" + support

    required = [
        "private ModeEShellRewardKind ClassifyFinalModeEShellRewardKind(",
        "private ModeEShellRewardSnapshot CaptureAndClaimModeEShellRewardSnapshot(",
        "private void SettleModeEShellRewardNoThrow(ModeEShellRewardSnapshot snapshot)",
        "private static int CalculateModeEShellStandardBossBase(float birthMaxHealth)",
        "state.rewardSettled = true;",
        "snapshot.PlayerAliveAtSnapshot",
        "rewardSessionGeneration",
        "ModeEShellRewardKind.None",
        "ModeEShellRewardKind.StandardBoss",
        "ModeEShellRewardKind.PromotedBoss",
        "modeEShellFirstPositiveRewardGranted",
        "reward += 10;",
        "CreditModeEShell(reward, \"Boss reward\")",
        ".sqrMagnitude <= 64f",
    ]
    for token in required:
        if token not in combined:
            return fail("missing reward invariant -> " + token)

    classify = extract_method(battle, "private ModeEShellRewardKind ClassifyFinalModeEShellRewardKind(")
    if not classify:
        # may live in scaling file
        classify = extract_method(scaling, "private ModeEShellRewardKind ClassifyFinalModeEShellRewardKind(")
    if not classify:
        return fail("missing ClassifyFinalModeEShellRewardKind body")
    if "ctx.preset" not in classify and "preset" not in classify:
        return fail("reward kind must derive from final preset")

    capture = extract_method(scaling, "private ModeEShellRewardSnapshot CaptureAndClaimModeEShellRewardSnapshot(")
    if not capture:
        return fail("missing capture/claim snapshot")
    if "state.rewardSettled = true;" not in capture:
        return fail("complete state first death must claim rewardSettled")
    if "PlayerAliveAtSnapshot" not in capture:
        return fail("snapshot must record playerAliveAtSnapshot")
    # Must not check merchantGeneration for reward eligibility.
    if "merchantGeneration" in capture.lower() and "rewardMerchantGeneration" in capture:
        return fail("boss reward must not depend on merchantGeneration")

    settle = extract_method(scaling, "private void SettleModeEShellRewardNoThrow(ModeEShellRewardSnapshot snapshot)")
    if "modeEShellEconomyAvailable" not in settle:
        return fail("settle must require economy capability")
    if "modeFActive" not in settle:
        return fail("Mode F must zero rewards")
    if "PlayerAliveAtSnapshot" not in settle:
        return fail("settle must honor playerAliveAtSnapshot veto")
    if "IsCurrentModeEShellSession" not in settle:
        return fail("settle must validate sessionGeneration")
    if "catch" not in settle:
        return fail("settle must be no-throw")

    reward_formula = extract_method(
        scaling, "private static int CalculateModeEShellStandardBossBase(")
    for token in [
        "MODE_E_SHELL_REFERENCE_HEALTH = 500.0",
        "MODE_E_SHELL_REFERENCE_HEALTH",
        "MODE_E_SHELL_REFERENCE_REWARD",
        "MODE_E_SHELL_REWARD_PER_HEALTH_DOUBLING",
        "safeHealth / MODE_E_SHELL_REFERENCE_HEALTH",
        "Math.Log(normalizedHealth, 2.0)",
        "Math.Ceiling(continuousReward)",
    ]:
        if token not in scaling and token not in reward_formula:
            return fail("continuous health-based reward formula missing -> " + token)
    for forbidden in ["Math.Floor", "Mathf.Clamp", "tier ="]:
        if forbidden in reward_formula:
            return fail("Boss reward must not use hard-coded health tiers -> " + forbidden)

    death = extract_method(scaling, "private void OnModeEEnemyDeath(")
    if not death:
        return fail("missing OnModeEEnemyDeath")
    if "CaptureAndClaimModeEShellRewardSnapshot" not in death:
        return fail("death must capture/claim before cleanup")
    if "SettleModeEShellRewardNoThrow" not in death:
        return fail("death must settle after existing cleanup")
    if death.index("CaptureAndClaimModeEShellRewardSnapshot") > death.index("SettleModeEShellRewardNoThrow"):
        return fail("claim must precede settle")

    # Ensure settle is after claim and typically after cleanup markers if present.
    if "rewardSettled" in death and death.index("CaptureAndClaim") > death.rfind("SettleModeEShellRewardNoThrow"):
        return fail("settle must run after claim")

    print("ModeEShellRewardGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
