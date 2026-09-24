"""RunScopedRegistryGuard: 通用局生命周期注册表迭代 helper 的 invariant。"""

from pathlib import Path
import sys


HELPER = Path("Utilities/RunScopedRegistry.cs")
ZOMBIE_CLEANUP = Path("ZombieMode/ZombieModeCleanup.cs")
ZOMBIE_RUNTIME_MODULE = Path("ZombieMode/ZombieModeRuntimeModule.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    helper = HELPER.read_text(encoding="utf-8")
    cleanup = ZOMBIE_CLEANUP.read_text(encoding="utf-8")
    runtime_module = ZOMBIE_RUNTIME_MODULE.read_text(encoding="utf-8")

    for snippet in [
        "internal static class RunScopedRegistry",
        "internal static void ForEachReverse<TRecord>",
        "where TRecord : class",
    ]:
        if snippet not in helper:
            return fail("RunScopedRegistryGuard: helper missing -> " + snippet)

    if "RunScopedRegistry.ForEachReverse(" not in runtime_module:
        return fail("RunScopedRegistryGuard: ZombieMode RuntimeModule cleanup must reuse RunScopedRegistry.ForEachReverse")

    if "for (int i = runState.RunOnlyObjects.Count - 1; i >= 0; i--)" in runtime_module:
        return fail("RunScopedRegistryGuard: ZombieMode cleanup 仍保留旧 for 循环，未走 helper")

    print("RunScopedRegistryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
