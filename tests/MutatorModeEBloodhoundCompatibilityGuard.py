from pathlib import Path
import sys


SOURCE = Path("ModeE/ModeEBattle.cs")


def fail(message: str) -> int:
    print("MutatorModeEBloodhoundCompatibilityGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8", errors="ignore")

    marker = "var ai = character.GetComponentInChildren<AICharacterController>();"
    start = text.find(marker)
    if start < 0:
        return fail("missing AI post-spawn block")

    block = text[start:start + 500]

    required = [
        'MutatorManager.HasActiveMutator("enemy_bloodhound")',
        "ai.forceTracePlayerDistance = bloodhoundActive ? 99999f : 0f;",
        "ai.noticed = true;",
    ]
    for needle in required:
        if needle not in block:
            return fail("missing Mode E/Mode F bloodhound compatibility token: " + needle)

    print("MutatorModeEBloodhoundCompatibilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
