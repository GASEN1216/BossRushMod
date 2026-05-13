from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
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

EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")
EFFECT_PARTS = [
    EFFECTS,
    Path("ZombieMode/ZombieModeRewardOptionCore.cs"),
    Path("ZombieMode/ZombieModeRewardProjectileSpread.cs"),
    Path("ZombieMode/ZombieModeRewardRuntimeModifiers.cs"),
    Path("ZombieMode/ZombieModeRewardTriggerEffects.cs"),
]


def read_effects() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in EFFECT_PARTS)

LOCALIZATION = Path("Localization/LocalizationInjector.cs")

REWARD_TYPES = [
    "ContractDevilBargain",
    "ContractCursedReload",
    "ContractBloodPrice",
    "ContractCursePool",
]


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method_body(text: str, signature: str) -> str:
    start = text.find(signature)
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
                return text[brace + 1:index]
    return ""


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = read_rewards()
    effects = read_effects() if EFFECTS.exists() else ""
    localization = LOCALIZATION.read_text(encoding="utf-8")

    for reward_type in REWARD_TYPES:
        if reward_type not in models:
            return fail("ZombieModeRewardPhase2ContractGuard: missing enum -> " + reward_type)
        if "ZombieModeRewardType." + reward_type not in rewards:
            return fail("ZombieModeRewardPhase2ContractGuard: reward not wired in rewards -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in rewards:
            return fail("ZombieModeRewardPhase2ContractGuard: display missing -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in localization:
            return fail("ZombieModeRewardPhase2ContractGuard: localization missing -> " + reward_type)

    for token in [
        "ApplyZombieModePhase2ContractReward",
        "ContractRuntimeModifierRecords",
        "RemoveZombieModePhase2ContractRuntimeEffects",
        "PollutionFromContracts",
    ]:
        if token not in effects and token not in rewards and token not in models:
            return fail("ZombieModeRewardPhase2ContractGuard: missing token -> " + token)

    if "ContractAffixWeights" in effects or "ContractAffixWeights" in rewards or "ContractAffixWeights" in models:
        return fail("ZombieModeRewardPhase2ContractGuard: unused ContractAffixWeights state still present")

    if "private bool GrantZombieModeContractGearDealRewardOnly()" not in rewards:
        return fail("ZombieModeRewardPhase2ContractGuard: missing no-extra-cost gear deal grant helper")

    curse_pool_body = extract_method_body(effects, "private bool ApplyZombieModeContractCursePool(bool bossNode)")
    if not curse_pool_body:
        return fail("ZombieModeRewardPhase2ContractGuard: missing ContractCursePool body")
    if "ApplyZombieModeContractGearDeal(true);" in curse_pool_body:
        return fail("ZombieModeRewardPhase2ContractGuard: ContractCursePool must not double-charge by calling ApplyZombieModeContractGearDeal")
    if "GrantZombieModeContractGearDealRewardOnly();" not in curse_pool_body:
        return fail("ZombieModeRewardPhase2ContractGuard: ContractCursePool gear branch must grant gear without extra purification charge")

    print("ZombieModeRewardPhase2ContractGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
