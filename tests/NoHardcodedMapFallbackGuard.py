"""
Guard: map data must keep moving from ModBehaviour hardcoding to JSON.

This guard verifies JSON deployment wiring and enforces that runtime source no
longer depends on the legacy hardcoded fallback.
"""

from pathlib import Path
import re
import sys


PROJECT_ROOT = Path(__file__).resolve().parent.parent
TESTS_DIR = Path(__file__).resolve().parent
MOD_BEHAVIOUR = PROJECT_ROOT / "ModBehaviour.cs"
LEGACY_FALLBACK = PROJECT_ROOT / "tests" / "fixtures" / "LegacyMapSpawnPointFallbackSnapshot.cs.txt"
SPAWN_POINTS_DIR = PROJECT_ROOT / "Assets" / "SpawnPoints"
COMPILE_SCRIPT = PROJECT_ROOT / "compile_official.bat"
DEPLOY_SCRIPT = PROJECT_ROOT / "test_bossrush_official.bat"
ENFORCE_MARKER = TESTS_DIR / "no_hardcoded_map_fallback_enforced.txt"
EXCLUDE_DIRS = {"Build", ".codex_tmp", ".git", ".kiro", "docs", "tests", "鸭科夫源码"}

HARDCODED_ARRAY_RE = re.compile(r"private\s+static\s+readonly\s+Vector3\[\]\s+\w+SpawnPoints\b")
HARDCODED_CONFIG_RE = re.compile(r"BossRushMapConfigs")


def script_contains_spawnpoint_deploy(script: Path) -> bool:
    if not script.exists():
        return False
    text = script.read_text(encoding="utf-8", errors="ignore").lower()
    return "assets\\spawnpoints\\*.json" in text and "xcopy" in text


def should_exclude(path: Path) -> bool:
    rel = path.relative_to(PROJECT_ROOT)
    return any(part in EXCLUDE_DIRS for part in rel.parts)


def find_runtime_legacy_references() -> list:
    refs = []
    for cs_file in sorted(PROJECT_ROOT.rglob("*.cs")):
        if should_exclude(cs_file) or cs_file == LEGACY_FALLBACK:
            continue
        text = cs_file.read_text(encoding="utf-8", errors="ignore")
        if "LegacyMapSpawnPointFallback" in text:
            refs.append(cs_file.relative_to(PROJECT_ROOT).as_posix())
    return refs


def main() -> int:
    print("NoHardcodedMapFallbackGuard: checking map JSON and fallback state...")

    failures = []
    warnings = []

    json_files = sorted(SPAWN_POINTS_DIR.glob("*.json")) if SPAWN_POINTS_DIR.exists() else []
    if len(json_files) < 9:
        failures.append(f"expected at least 9 spawn-point JSON files, found {len(json_files)}")

    if not script_contains_spawnpoint_deploy(COMPILE_SCRIPT):
        failures.append("compile_official.bat does not deploy Assets\\SpawnPoints\\*.json")
    if not script_contains_spawnpoint_deploy(DEPLOY_SCRIPT):
        failures.append("test_bossrush_official.bat does not deploy Assets\\SpawnPoints\\*.json")

    mod_text = MOD_BEHAVIOUR.read_text(encoding="utf-8", errors="ignore") if MOD_BEHAVIOUR.exists() else ""
    hardcoded_arrays = len(HARDCODED_ARRAY_RE.findall(mod_text))
    has_config_table = bool(HARDCODED_CONFIG_RE.search(mod_text))
    uses_legacy_runtime_fallback = "LegacyMapSpawnPointFallback" in mod_text
    has_legacy_file = LEGACY_FALLBACK.exists()
    runtime_legacy_refs = find_runtime_legacy_references()
    compile_text = COMPILE_SCRIPT.read_text(encoding="utf-8", errors="ignore").lower() if COMPILE_SCRIPT.exists() else ""
    compile_references_legacy = "common\\mapconfig\\legacymapspawnpointfallback.cs" in compile_text
    enforce = ENFORCE_MARKER.exists()

    if hardcoded_arrays:
        message = f"ModBehaviour.cs still declares {hardcoded_arrays} hardcoded spawn-point arrays"
        if enforce:
            failures.append(message)
        else:
            warnings.append(message)

    if has_config_table:
        message = "ModBehaviour.cs still references BossRushMapConfigs hardcoded fallback"
        if enforce:
            failures.append(message)
        else:
            warnings.append(message)

    if uses_legacy_runtime_fallback:
        message = "ModBehaviour.cs still calls LegacyMapSpawnPointFallback at runtime"
        if enforce:
            failures.append(message)
        else:
            warnings.append(message)

    if runtime_legacy_refs:
        message = "runtime source still references LegacyMapSpawnPointFallback: " + ", ".join(runtime_legacy_refs)
        if enforce:
            failures.append(message)
        else:
            warnings.append(message)

    if compile_references_legacy:
        message = "compile_official.bat still compiles LegacyMapSpawnPointFallback.cs"
        if enforce:
            failures.append(message)
        else:
            warnings.append(message)

    if has_legacy_file and not enforce:
        warnings.append("LegacyMapSpawnPointFallbackSnapshot exists as a tests-only consistency snapshot")

    if warnings:
        print("NoHardcodedMapFallbackGuard: WARN Phase 1 fallback still present:")
        for warning in warnings:
            print("  WARN " + warning)

    if failures:
        print("NoHardcodedMapFallbackGuard: FAIL")
        for failure in failures:
            print("  FAIL " + failure)
        return 1

    state = "enforced" if enforce else "soft"
    print(f"NoHardcodedMapFallbackGuard: PASS ({state}, {len(json_files)} JSON files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
