"""
Guard: Mode E healthbar name override must cache desired text per healthbar
target instead of rebuilding suffix strings every throttled LateUpdate pass.
"""

from pathlib import Path
import sys


SOURCES = [
    Path("ModeE/ModeE.cs"),
    Path("ModeE/ModeEUiAndHealthBars.cs"),
    Path("ModeE/ModeEStartup.cs"),
    Path("ModeE/ModeELifecycle.cs"),
    Path("ModeE/ModeEIntegrityAndHelpers.cs"),
]


def fail(message: str) -> int:
    print(message)
    return 1


def read_mode_e_sources() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in SOURCES)


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
    text = read_mode_e_sources()

    for required in (
        "modeEHealthBarBaseTextByBarId",
        "modeEHealthBarDesiredTextByBarId",
        "modeEHealthBarTargetIdsByBarId",
        "modeEHealthBarAppliedVersionByBarId",
        "modeEHealthBarNameVersion",
        "BuildModeEDesiredHealthBarText",
        "ClearModeEHealthBarOverrideCache",
    ):
        if required not in text:
            return fail(f"ModeEHealthBarNameCacheGuard: missing healthbar cache invariant -> {required}")

    reset_body = extract_method_body(text, "private void ResetModeEUiCaches()")
    if reset_body is None:
        return fail("ModeEHealthBarNameCacheGuard: missing ResetModeEUiCaches body")

    for required in (
        "modeEHealthBarBaseTextByBarId.Clear()",
        "modeEHealthBarDesiredTextByBarId.Clear()",
        "modeEHealthBarTargetIdsByBarId.Clear()",
        "modeEHealthBarAppliedVersionByBarId.Clear()",
    ):
        if required not in reset_body:
            return fail(f"ModeEHealthBarNameCacheGuard: ResetModeEUiCaches lacks -> {required}")

    apply_body = extract_method_body(text, "internal void ApplyModeEHealthBarNameOverride")
    if apply_body is None:
        return fail("ModeEHealthBarNameCacheGuard: missing ApplyModeEHealthBarNameOverride body")

    for required in (
        "modeEHealthBarDesiredTextByBarId.TryGetValue",
        "modeEHealthBarAppliedVersionByBarId.TryGetValue",
        "modeEHealthBarTargetIdsByBarId.TryGetValue",
        "BuildModeEDesiredHealthBarText(",
    ):
        if required not in apply_body:
            return fail(f"ModeEHealthBarNameCacheGuard: ApplyModeEHealthBarNameOverride lacks -> {required}")

    if "StripModeEFactionSuffix(" in apply_body:
        return fail("ModeEHealthBarNameCacheGuard: ApplyModeEHealthBarNameOverride still strips suffixes directly")

    print("ModeEHealthBarNameCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
