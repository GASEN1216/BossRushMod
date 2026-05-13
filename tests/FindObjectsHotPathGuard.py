"""
Guard: no scene-wide FindObjects calls in hot methods.

The first version blocks new calls in obvious per-frame or damage/death
callbacks. Existing non-hot startup, setup, UI-open, and cache-refresh scans are
tracked by later Batch E work.
"""

from pathlib import Path
import re
import sys


PROJECT_ROOT = Path(__file__).resolve().parent.parent
ALLOWLIST_FILE = Path(__file__).resolve().parent / "find_objects_hot_path_allowlist.txt"
EXCLUDE_DIRS = {"Build", ".codex_tmp", ".git", ".kiro", "docs", "tests", "鸭科夫源码"}

CALL_RE = re.compile(r"\b(?:Resources\.)?FindObjectsOfType(?:All)?\b")
METHOD_RE = re.compile(
    r"^\s*(?:public|private|protected|internal|static|virtual|override|sealed|async|\s)+"
    r"(?:[\w<>\[\],]+\s+)?(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^;]*\)"
)

EXACT_HOT_METHODS = {
    "Update",
    "LateUpdate",
    "FixedUpdate",
    "OnGUI",
    "OnRenderObject",
    "OnWillRenderObject",
}

HOT_NAME_FRAGMENTS = (
    "EveryFrame",
    "OnHurt",
    "OnDead",
    "OnDeath",
    "HealthHurt",
    "HealthDead",
    "EnemyDied",
)


def should_exclude(path: Path) -> bool:
    rel = path.relative_to(PROJECT_ROOT)
    return any(part in EXCLUDE_DIRS for part in rel.parts)


def is_hot_method(name: str) -> bool:
    if name in EXACT_HOT_METHODS:
        return True
    if name == "Tick" or name.startswith("Tick") or name.endswith("Tick"):
        return True
    return any(fragment in name for fragment in HOT_NAME_FRAGMENTS)


def load_allowlist() -> set:
    entries = set()
    if not ALLOWLIST_FILE.exists():
        return entries
    for line in ALLOWLIST_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [part.strip() for part in line.split("|")]
        if len(parts) >= 3:
            entries.add((parts[0], parts[1], parts[2]))
    return entries


def strip_line_comment(line: str) -> str:
    return line.split("//", 1)[0]


def scan_file(path: Path, allowlist: set):
    rel = path.relative_to(PROJECT_ROOT).as_posix()
    failures = []

    current_method = None
    current_hot = False
    brace_depth = 0
    method_start_depth = 0

    for line_no, raw_line in enumerate(path.read_text(encoding="utf-8", errors="ignore").splitlines(), 1):
        line = strip_line_comment(raw_line)

        if current_method is None:
            match = METHOD_RE.match(line)
            if match and "=>" not in line:
                name = match.group("name")
                current_method = name
                current_hot = is_hot_method(name)
                method_start_depth = brace_depth

        if current_hot and CALL_RE.search(line):
            snippet = line.strip()
            allowed = any(
                rel == allow_rel and current_method == allow_method and allow_snippet in snippet
                for allow_rel, allow_method, allow_snippet in allowlist
            )
            if not allowed:
                failures.append((rel, line_no, current_method, snippet))

        brace_depth += line.count("{") - line.count("}")
        if current_method is not None and brace_depth <= method_start_depth and "}" in line:
            current_method = None
            current_hot = False

    return failures


def main() -> int:
    print("FindObjectsHotPathGuard: checking hot-path scene scans...")

    allowlist = load_allowlist()
    failures = []
    for cs_file in sorted(PROJECT_ROOT.rglob("*.cs")):
        if should_exclude(cs_file):
            continue
        failures.extend(scan_file(cs_file, allowlist))

    if failures:
        print("FindObjectsHotPathGuard: FAIL FindObjects calls in hot methods:")
        for rel, line_no, method, snippet in failures:
            print(f"  FAIL {rel}:{line_no} in {method}: {snippet}")
        return 1

    print("FindObjectsHotPathGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
