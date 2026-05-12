"""
Guard: GetAllMapConfigs must preserve hardcoded map order.

Registry JSON may override individual map data, but UI/default ordering must follow
BossRushMapConfigs. New registry-only maps may be appended after hardcoded maps.
"""

from pathlib import Path
import sys


SOURCE = Path("ModBehaviour.cs")


def fail(message: str) -> int:
    print("MapSpawnRegistryOrderGuard: " + message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""
    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")
    block = extract_block(text, "public static BossRushMapConfig[] GetAllMapConfigs()")
    if not block:
        return fail("missing GetAllMapConfigs block")

    hardcoded_loop = "for (int i = 0; i < BossRushMapConfigs.Length; i++)"
    if hardcoded_loop not in block:
        return fail("missing hardcoded-order BossRushMapConfigs loop")

    if "_mapSpawnRegistry.TryGet(" not in block or ".sceneName" not in block:
        return fail("hardcoded loop must use registry TryGet override per scene")

    first_all = block.find("_mapSpawnRegistry.All()")
    first_hardcoded = block.find(hardcoded_loop)
    if first_all != -1 and first_all < first_hardcoded:
        return fail("registry enumeration happens before hardcoded ordering")

    print("MapSpawnRegistryOrderGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
