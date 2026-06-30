"""Guard: per-frame ZombieMode controllers should reuse cached ModBehaviour owner."""

from pathlib import Path
import sys


SOURCES = {
    "ZombiePurificationPointController": Path("ZombieMode/ZombiePurificationPointController.cs"),
    "ZombieModeHudController": Path("ZombieMode/ZombieModeHudController.cs"),
}


def fail(message: str) -> int:
    print("ZombieModeControllerOwnerCacheGuard: FAIL - " + message)
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


def strip_cache_helper(body: str) -> str:
    helper = "private ModBehaviour GetRuntimeOwner()"
    helper_index = body.find(helper)
    if helper_index < 0:
        return body

    brace_start = body.find("{", helper_index)
    if brace_start < 0:
        return body

    depth = 0
    for idx in range(brace_start, len(body)):
        ch = body[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return body[:helper_index] + body[idx + 1:]

    return body


def main() -> int:
    for class_name, path in SOURCES.items():
        text = path.read_text(encoding="utf-8-sig")
        body = extract_class_body(text, class_name)
        if body is None:
            return fail(f"missing {class_name} body")

        required = [
            "private ModBehaviour owner;",
            "owner = ModBehaviour.Instance;",
            "private ModBehaviour GetRuntimeOwner()",
            "ModBehaviour inst = owner;",
            "if (inst == null)",
            "inst = ModBehaviour.Instance;",
            "owner = inst;",
        ]
        for snippet in required:
            if snippet not in body:
                return fail(f"{class_name} missing owner-cache snippet -> {snippet}")

        active_body = strip_cache_helper(body)
        active_body = active_body.replace("owner = ModBehaviour.Instance;", "")
        if "ModBehaviour.Instance" in active_body:
            return fail(f"{class_name} still queries ModBehaviour.Instance outside owner cache")

    print("ZombieModeControllerOwnerCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
