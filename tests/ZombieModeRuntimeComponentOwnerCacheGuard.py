"""Guard: ZombieMode runtime components should cache ModBehaviour owner lookups."""

from pathlib import Path
import sys


SOURCE = Path("ZombieMode/ZombieModePollution_RuntimeComponents.cs")


def fail(message: str) -> int:
    print("ZombieModeRuntimeComponentOwnerCacheGuard: FAIL - " + message)
    return 1


def extract_class_body(text: str, class_name: str) -> str | None:
    signature = f"public sealed class {class_name}"
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
                return text[brace_start:idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")

    required_classes = [
        "ZombieModeCommanderAuraRuntime",
        "ZombieModeCommanderAuraTargetRuntime",
        "ZombieModeRegenerationAffixRuntime",
        "ZombieModeShieldedAffixRuntime",
    ]

    for class_name in required_classes:
        body = extract_class_body(text, class_name)
        if body is None:
            return fail(f"missing {class_name} body")

        required = [
            "private ModBehaviour owner;",
            "owner = ModBehaviour.Instance;",
            "private ModBehaviour GetRuntimeOwner()",
            "ModBehaviour inst = owner;",
            "if (inst == null || inst.ZombieModeCurrentRunId != runId)",
            "inst = ModBehaviour.Instance;",
            "owner = inst;",
        ]
        for snippet in required:
            if snippet not in body:
                return fail(f"{class_name} missing owner-cache snippet -> {snippet}")

    direct_lookup_classes = required_classes
    for class_name in direct_lookup_classes:
        body = extract_class_body(text, class_name)
        if body is None:
            return fail(f"missing {class_name} body")

        body_without_helper = body.replace(
            "inst = ModBehaviour.Instance;\n                owner = inst;",
            "",
        )
        body_without_helper = body_without_helper.replace(
            "owner = ModBehaviour.Instance;",
            "",
        )
        if "ModBehaviour.Instance" in body_without_helper:
            return fail(f"{class_name} still queries ModBehaviour.Instance outside owner cache")

    print("ZombieModeRuntimeComponentOwnerCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
