"""
Guard: high-risk event subscriptions must have an unsubscribe path.

Batch A scope is intentionally narrow: the guard only checks the roadmap's
first high-risk events, accepts known legacy debt through a tests-side
allowlist, and fails on new unpaired subscriptions.
"""

from pathlib import Path
import re
import sys


PROJECT_ROOT = Path(__file__).resolve().parent.parent
ALLOWLIST_FILE = Path(__file__).resolve().parent / "event_subscription_lifecycle_allowlist.txt"
EXCLUDE_DIRS = {"Build", ".codex_tmp", ".git", ".kiro", "docs", "tests", "鸭科夫源码"}

TARGET_SUFFIXES = (
    "Health.OnDead",
    "Health.OnHurt",
    "InteractableLootbox.OnStopLoot",
    "SavesSystem.OnSetFile",
    "SceneManager.sceneLoaded",
)

SUBSCRIPTION_RE = re.compile(
    r"(?P<event>(?:[A-Za-z_][A-Za-z0-9_]*\.)*"
    r"(?:Health\.OnDead|Health\.OnHurt|InteractableLootbox\.OnStopLoot|"
    r"SavesSystem\.OnSetFile|SceneManager\.sceneLoaded))"
    r"\s*(?P<op>\+=|-=)\s*(?P<handler>[^;]+);"
)

PARTIAL_SOURCE_GROUPS = {
    "Integration/NPCs/Courier/CourierService": "Integration/NPCs/Courier/CourierService.cs",
    "Integration/PhantomWitch/PhantomWitchAbilityController": "Integration/PhantomWitch/PhantomWitchAbilityController.cs",
}


def should_exclude(path: Path) -> bool:
    rel = path.relative_to(PROJECT_ROOT)
    return any(part in EXCLUDE_DIRS for part in rel.parts)


def normalize_event(event_name: str) -> str:
    for suffix in TARGET_SUFFIXES:
        if event_name.endswith(suffix):
            return suffix
    return event_name


def normalize_handler(handler: str) -> str:
    handler = handler.split("//", 1)[0].strip()
    handler = re.sub(r"\s+", "", handler)
    return handler


def canonical_subscription_scope(rel: str) -> str:
    for prefix, canonical in PARTIAL_SOURCE_GROUPS.items():
        if rel == canonical:
            return canonical
        if rel.startswith(prefix + "_") and rel.endswith(".cs"):
            return canonical
    return rel


def load_allowlist() -> set:
    entries = set()
    if not ALLOWLIST_FILE.exists():
        return entries

    for line in ALLOWLIST_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [part.strip() for part in line.split("|")]
        if len(parts) < 3:
            continue
        entries.add((parts[0], normalize_event(parts[1]), normalize_handler(parts[2])))
    return entries


def scan_file(path: Path):
    rel = canonical_subscription_scope(path.relative_to(PROJECT_ROOT).as_posix())
    subscriptions = []
    unsubscriptions = set()

    for line_no, line in enumerate(path.read_text(encoding="utf-8", errors="ignore").splitlines(), 1):
        code = line.split("//", 1)[0]
        for match in SUBSCRIPTION_RE.finditer(code):
            event_name = normalize_event(match.group("event"))
            handler = normalize_handler(match.group("handler"))
            record = (rel, event_name, handler)
            if match.group("op") == "+=":
                subscriptions.append((record, line_no, line.strip()))
            else:
                unsubscriptions.add(record)

    return subscriptions, unsubscriptions


def main() -> int:
    print("EventSubscriptionLifecycleGuard: checking high-risk event subscriptions...")

    allowlist = load_allowlist()
    all_subscriptions = []
    all_unsubscriptions = set()

    for cs_file in sorted(PROJECT_ROOT.rglob("*.cs")):
        if should_exclude(cs_file):
            continue
        subscriptions, unsubscriptions = scan_file(cs_file)
        all_subscriptions.extend(subscriptions)
        all_unsubscriptions.update(unsubscriptions)

    failures = []
    warnings = []

    for record, line_no, line_text in all_subscriptions:
        rel, event_name, handler = record
        if "=>" in handler or handler.startswith("delegate"):
            if record not in allowlist:
                failures.append((rel, line_no, event_name, handler, "anonymous handler cannot be unsubscribed reliably"))
            continue

        if record in all_unsubscriptions:
            continue

        if record in allowlist:
            warnings.append((rel, line_no, event_name, handler))
            continue

        failures.append((rel, line_no, event_name, handler, "missing matching -= or allowlist entry"))

    if warnings:
        print("EventSubscriptionLifecycleGuard: WARN allowlisted legacy subscriptions:")
        for rel, line_no, event_name, handler in warnings:
            print(f"  WARN {rel}:{line_no} {event_name} += {handler}")

    if failures:
        print("EventSubscriptionLifecycleGuard: FAIL unpaired high-risk subscriptions:")
        for rel, line_no, event_name, handler, reason in failures:
            print(f"  FAIL {rel}:{line_no} {event_name} += {handler} ({reason})")
        return 1

    print(
        "EventSubscriptionLifecycleGuard: PASS "
        f"({len(all_subscriptions)} subscriptions checked, {len(warnings)} allowlisted)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
