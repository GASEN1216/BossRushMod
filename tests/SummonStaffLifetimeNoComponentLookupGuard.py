"""Guard: SummonStaff ally lifetime expiry should not probe components before destroy."""

from pathlib import Path
import sys


SOURCE = Path("Integration/NewWeapons/SummonStaff/SummonStaffAction.cs")


def fail(message: str) -> int:
    print("SummonStaffLifetimeNoComponentLookupGuard: FAIL - " + message)
    return 1


def extract_class_body(text: str, class_name: str) -> str | None:
    start = text.find(f"class {class_name}")
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
                return text[brace_start:idx + 1]

    return None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    lifetime = extract_class_body(text, "SummonStaffAllyLifetime")
    if lifetime is None:
        return fail("missing SummonStaffAllyLifetime")

    update = extract_method_body(lifetime, "private void Update()")
    if update is None:
        return fail("missing SummonStaffAllyLifetime.Update")

    if "GetComponent<" in update:
        return fail("lifetime expiry still probes components before destroying the ally")

    required = [
        "elapsed += Time.deltaTime;",
        "if (elapsed >= lifetime)",
        "Destroy(gameObject);",
    ]
    for snippet in required:
        if snippet not in update:
            return fail("missing direct lifetime destroy snippet -> " + snippet)

    print("SummonStaffLifetimeNoComponentLookupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
