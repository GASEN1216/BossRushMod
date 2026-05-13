"""Guard: ZombieMode purification costs must go through the shared spend helper."""

from pathlib import Path
import sys


ROOTS = [Path("ZombieMode"), Path("Integration")]
SPEND_HELPER = "public bool SpendZombieModePurificationPoints(int cost, string reason)"
REWARD_PARTS = [
    Path("ZombieMode/ZombieModeRewards.cs"),
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]


def fail(message: str) -> int:
    print("ZombieModePurificationSpendCentralizationGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
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
                return text[start:index + 1]

    return ""


def extract_method_line_range(text: str, signature: str) -> tuple[int, int] | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace = text.find("{", start)
    if brace < 0:
        return None

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                start_line = text[:start].count("\n") + 1
                end_line = text[:index].count("\n") + 1
                return start_line, end_line

    return None


def main() -> int:
    spend_path = None
    spend_text = ""
    spend_body = ""
    for path in REWARD_PARTS:
        text = path.read_text(encoding="utf-8", errors="ignore")
        body = extract_method(text, SPEND_HELPER)
        if body:
            spend_path = path
            spend_text = text
            spend_body = body
            break

    if not spend_body:
        return fail("missing shared spend helper")
    if "zombieModeRunState.PurificationPoints -= cost;" not in spend_body:
        return fail("shared spend helper must be the only direct subtraction site")
    spend_range = extract_method_line_range(spend_text, SPEND_HELPER)
    if spend_range is None:
        return fail("could not resolve shared spend helper line range")

    offenders = []
    for root in ROOTS:
        if not root.exists():
            continue
        for path in root.rglob("*.cs"):
            text = path.read_text(encoding="utf-8", errors="ignore")
            for line_number, line in enumerate(text.splitlines(), start=1):
                if "PurificationPoints -=" not in line:
                    continue
                if path == spend_path and spend_range[0] <= line_number <= spend_range[1]:
                    continue
                offenders.append(f"{path}:{line_number}: {line.strip()}")

    if offenders:
        return fail("direct purification subtraction outside SpendZombieModePurificationPoints -> " + "; ".join(offenders[:12]))

    for token in [
        "SpendZombieModePurificationPoints(pointsCost, \"ContractPollutionDeal\")",
        "SpendZombieModePurificationPoints(pointsCost, \"ContractDevilBargain\")",
        "SpendZombieModePurificationPoints(cost, \"ZombieModeMerchantService\")",
        "SpendZombieModePurificationPoints(cost, \"ZombieModeNurseService\")",
        "RefundZombieModePurificationPoints(cost, \"ZombieModeMerchantServiceFailed\")",
        "RefundZombieModePurificationPoints(cost, \"ZombieModeNurseServiceFailed\")",
    ]:
        combined = "\n".join(
            path.read_text(encoding="utf-8", errors="ignore")
            for root in ROOTS
            if root.exists()
            for path in root.rglob("*.cs")
        )
        if token not in combined:
            return fail("missing centralized spend/refund token -> " + token)

    print("ZombieModePurificationSpendCentralizationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
