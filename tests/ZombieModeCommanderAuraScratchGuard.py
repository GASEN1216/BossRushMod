"""Guard: zombie commander aura target cleanup should reuse scratch storage."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModePollution_RuntimeSkills.cs")


def fail(message: str) -> int:
    print("ZombieModeCommanderAuraScratchGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    body = extract_method_body(text, "internal void RefreshZombieModeCommanderAuraTargets(")
    if body is None:
        return fail("missing RefreshZombieModeCommanderAuraTargets body")

    forbidden = [
        "List<int> staleTargetIds = null;",
        "staleTargetIds = new List<int>();",
    ]
    for snippet in forbidden:
        if snippet in body:
            return fail("commander aura cleanup still allocates stale list -> " + snippet)

    required = [
        "commanderAuraStaleTargetsScratch",
        "commanderAuraStaleTargetsScratch.Clear();",
        "commanderAuraStaleTargetsScratch.Add(entry.Key);",
        "trackedTargets.Remove(commanderAuraStaleTargetsScratch[i]);",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("missing reusable stale-target scratch snippet -> " + snippet)

    print("ZombieModeCommanderAuraScratchGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
