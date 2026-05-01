"""ZombieModeRunScopedRegistryGuard: RunOnlyObjects 是协程/事件唯一可信源。"""
from pathlib import Path
import sys


def fail(msg: str) -> int:
    print(msg)
    return 1


def main() -> int:
    for path in Path("ZombieMode").glob("*.cs"):
        text = path.read_text(encoding="utf-8")
        if "RegisteredCoroutines" in text:
            return fail("ZombieModeRunScopedRegistryGuard: " + str(path) + " 仍在引用 RegisteredCoroutines")
        if "EventListenerHandles" in text:
            return fail("ZombieModeRunScopedRegistryGuard: " + str(path) + " 仍在引用 EventListenerHandles")
    return 0


if __name__ == "__main__":
    sys.exit(main())
