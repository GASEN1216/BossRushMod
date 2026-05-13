"""Guard: Mode E/F HealthBar lookups must prefer registered caches over scans."""

from pathlib import Path
import sys


MODEE_UI = Path("ModeE/ModeEUiAndHealthBars.cs")
MODEE_CORE = Path("ModeE/ModeE.cs")
MODEE_HELPERS = Path("ModeE/ModeEIntegrityAndHelpers.cs")
MODEF_UI = Path("ModeF/ModeFUI.cs")
MODEF_HEALTHBARS = Path("ModeF/ModeFUI_BountyRadarAndHealthBars.cs")


def fail(message: str) -> int:
    print("ModeEFHealthBarRegistryGuard: FAIL - " + message)
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


def require(text: str, needle: str, message: str) -> int | None:
    if needle not in text:
        return fail(message)
    return None


def forbid(text: str, needle: str, message: str) -> int | None:
    if needle in text:
        return fail(message)
    return None


def require_order(text: str, first: str, second: str, message: str) -> int | None:
    first_idx = text.find(first)
    second_idx = text.find(second)
    if first_idx < 0 or second_idx < 0 or first_idx >= second_idx:
        return fail(message)
    return None


def main() -> int:
    modee_core = MODEE_CORE.read_text(encoding="utf-8")
    modee_ui = MODEE_UI.read_text(encoding="utf-8")
    modee_helpers = MODEE_HELPERS.read_text(encoding="utf-8")
    modef_ui = MODEF_UI.read_text(encoding="utf-8")
    modef_healthbars = MODEF_HEALTHBARS.read_text(encoding="utf-8")

    for text, needle, message in (
        (modee_core, "modeEHealthBarCacheByTargetId", "Mode E must own a target-id HealthBar registry"),
        (modee_ui, "internal void RegisterModeEHealthBar(HealthBar healthBar)", "Mode E must expose HealthBar registration"),
        (modee_ui, "private bool TryGetCachedModeEHealthBar(Health health, out HealthBar healthBar)", "Mode E must read registered HealthBars by Health target"),
        (modee_ui, "private void ScanAndCacheModeEHealthBars()", "Mode E scan fallback must be isolated to one low-frequency helper"),
        (modee_helpers, "RegisterModeEHealthBar(healthBar);", "Mode E HealthBar patch path must register bars before applying text"),
        (modef_ui, "internal void RegisterModeFHealthBar(HealthBar healthBar)", "Mode F must keep HealthBar registration"),
        (modef_ui, "private bool TryGetCachedModeFHealthBar(Health health, out HealthBar healthBar)", "Mode F must keep registered-cache lookup"),
        (modef_healthbars, "RegisterModeFHealthBar(healthBar);", "Mode F HealthBar patch path must register bars before applying text"),
    ):
        result = require(text, needle, message)
        if result is not None:
            return result

    find_modee = extract_method_body(modee_ui, "private HealthBar FindModeEHealthBar")
    if find_modee is None:
        return fail("missing FindModeEHealthBar")
    for first, second, message in (
        ("TryGetCachedModeEHealthBar(health, out healthBar)", "ScanAndCacheModeEHealthBars();", "Mode E lookup must try the registry before scanning"),
        ("Time.unscaledTime < modeENextHealthBarLookupTime", "ScanAndCacheModeEHealthBars();", "Mode E scan fallback must stay cooldown-gated"),
    ):
        result = require_order(find_modee, first, second, message)
        if result is not None:
            return result
    result = forbid(find_modee, "FindObjectsOfType<HealthBar>", "Mode E direct lookup must not scan inline")
    if result is not None:
        return result

    scan_modee = extract_method_body(modee_ui, "private void ScanAndCacheModeEHealthBars")
    if scan_modee is None:
        return fail("missing ScanAndCacheModeEHealthBars")
    result = require(scan_modee, "UnityEngine.Object.FindObjectsOfType<HealthBar>()", "Mode E scan fallback must remain explicit and reviewable")
    if result is not None:
        return result

    scan_modef = extract_method_body(modef_ui, "private void ScanAndCacheModeFHealthBars")
    if scan_modef is None:
        return fail("missing ScanAndCacheModeFHealthBars")
    result = forbid(scan_modef, "modeFHealthBarCacheByTargetId.Clear();", "Mode F scan fallback must not clear the registration cache")
    if result is not None:
        return result

    print("ModeEFHealthBarRegistryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
