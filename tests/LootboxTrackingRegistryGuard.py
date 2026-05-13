"""
Guard: marked BossRush lootbox collection should move from full-scene scans to
an incremental marker registry.
"""

from pathlib import Path
import re
import sys


SOURCES = [
    Path("Interactables/BossRushInteractables.cs"),
    Path("Interactables/BossRushLootboxInteractables.cs"),
]
ENTRYPOINT_COLLECTION_METHODS = [
    "CollectMarkedLootboxes",
    "CollectMarkedLootboxesForSession",
]
UTILITY_REQUIRED_METHODS = [
    "RegisterMarkedLootboxMarker",
    "UnregisterMarkedLootboxMarker",
    "CollectMarkedLootboxesFromRegistry",
]
FULL_SCAN_PATTERN = re.compile(
    r"(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)*"
    r"FindObjects(?:OfType|ByType)\s*<\s*InteractableLootbox\s*>\s*\(",
    re.MULTILINE,
)
CLASS_PATTERN = re.compile(
    r"^\s*(?:public|internal|private|protected(?:\s+internal)?|private\s+protected)?"
    r"\s*(?:static\s+)?class\s+([A-Za-z_][A-Za-z0-9_]*)\b",
    re.MULTILINE,
)
STATIC_METHOD_PATTERN = re.compile(
    r"^\s*(?:public|internal|private|protected(?:\s+internal)?|private\s+protected)?"
    r"\s*static\s+[A-Za-z0-9_<>,\[\]\s\?]+\b([A-Za-z_][A-Za-z0-9_]*)\s*\(",
    re.MULTILINE,
)
MARKER_LOOTBOX_FIELD_PATTERN = re.compile(
    r"\b(?:private|internal|public|protected(?:\s+internal)?|private\s+protected)?"
    r"\s*InteractableLootbox\s+[A-Za-z0-9_]*cached[A-Za-z0-9_]*\s*(?:=|;)",
    re.IGNORECASE | re.MULTILINE,
)


def fail(message: str) -> int:
    print(message)
    return 1


def read_interactable_sources() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in SOURCES)


def neutralize_csharp_comments_and_strings(text: str) -> str:
    result: list[str] = []
    index = 0
    length = len(text)

    while index < length:
        char = text[index]
        next_char = text[index + 1] if index + 1 < length else ""

        if char == "/" and next_char == "/":
            result.append(" ")
            result.append(" ")
            index += 2
            while index < length and text[index] != "\n":
                result.append(" ")
                index += 1
            continue

        if char == "/" and next_char == "*":
            result.append(" ")
            result.append(" ")
            index += 2
            while index < length:
                current = text[index]
                following = text[index + 1] if index + 1 < length else ""
                if current == "*" and following == "/":
                    result.append(" ")
                    result.append(" ")
                    index += 2
                    break

                result.append("\n" if current == "\n" else " ")
                index += 1
            continue

        if char == "@":
            third_char = text[index + 2] if index + 2 < length else ""
            if next_char == '"' or (next_char == "$" and third_char == '"'):
                if next_char == '"':
                    result.append(" ")
                    result.append(" ")
                    index += 2
                else:
                    result.append(" ")
                    result.append(" ")
                    result.append(" ")
                    index += 3

                while index < length:
                    current = text[index]
                    following = text[index + 1] if index + 1 < length else ""
                    if current == '"' and following == '"':
                        result.append(" ")
                        result.append(" ")
                        index += 2
                        continue
                    if current == '"':
                        result.append(" ")
                        index += 1
                        break

                    result.append("\n" if current == "\n" else " ")
                    index += 1
                continue

        if char == "$" and next_char == "@":
            third_char = text[index + 2] if index + 2 < length else ""
            if third_char == '"':
                result.append(" ")
                result.append(" ")
                result.append(" ")
                index += 3
                while index < length:
                    current = text[index]
                    following = text[index + 1] if index + 1 < length else ""
                    if current == '"' and following == '"':
                        result.append(" ")
                        result.append(" ")
                        index += 2
                        continue
                    if current == '"':
                        result.append(" ")
                        index += 1
                        break

                    result.append("\n" if current == "\n" else " ")
                    index += 1
                continue

        if char == '"':
            result.append(" ")
            index += 1
            escaped = False
            while index < length:
                current = text[index]
                result.append("\n" if current == "\n" else " ")
                index += 1

                if escaped:
                    escaped = False
                    continue

                if current == "\\":
                    escaped = True
                    continue

                if current == '"':
                    break
            continue

        if char == "'":
            result.append(" ")
            index += 1
            escaped = False
            while index < length:
                current = text[index]
                result.append("\n" if current == "\n" else " ")
                index += 1

                if escaped:
                    escaped = False
                    continue

                if current == "\\":
                    escaped = True
                    continue

                if current == "'":
                    break
            continue

        result.append(char)
        index += 1

    return "".join(result)


def slice_class(text: str, class_name: str) -> str:
    matches = list(CLASS_PATTERN.finditer(text))
    for index, match in enumerate(matches):
        if match.group(1) != class_name:
            continue

        start = match.start()
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        return text[start:end]

    return ""


def has_static_method(class_text: str, method_name: str) -> bool:
    return re.search(
        rf"^\s*(?:public|internal|private|protected(?:\s+internal)?|private\s+protected)?"
        rf"\s*static\s+[A-Za-z0-9_<>,\[\]\s\?]+\b{re.escape(method_name)}\s*\(",
        class_text,
        re.MULTILINE,
    ) is not None


def extract_method_block(class_text: str, method_name: str) -> str:
    signature = re.search(
        rf"\b{re.escape(method_name)}\s*\([^)]*\)\s*\{{",
        class_text,
        re.MULTILINE | re.DOTALL,
    )
    if signature is None:
        return ""

    brace_start = class_text.find("{", signature.start())
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(class_text)):
        char = class_text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return class_text[signature.start() : index + 1]

    return ""


def method_contains_call(class_text: str, method_name: str, call_name: str) -> bool:
    method_block = extract_method_block(class_text, method_name)
    if not method_block:
        return False

    return re.search(rf"\b{re.escape(call_name)}\s*\(", method_block) is not None


def lifecycle_contains_call(class_text: str, lifecycle_name: str, call_name: str) -> bool:
    method_block = extract_method_block(class_text, lifecycle_name)
    if not method_block:
        return False

    return re.search(
        rf"BossRushLootboxUtility\s*\.\s*{re.escape(call_name)}\s*\(\s*this\s*\)",
        method_block,
    ) is not None


def main() -> int:
    text = neutralize_csharp_comments_and_strings(read_interactable_sources())

    marker_section = slice_class(text, "BossRushLootboxMarker")
    if not marker_section:
        return fail("LootboxTrackingRegistryGuard: missing BossRushLootboxMarker class")

    utility_section = slice_class(text, "BossRushLootboxUtility")
    if not utility_section:
        return fail("LootboxTrackingRegistryGuard: missing BossRushLootboxUtility class")

    utility_methods = {match.group(1) for match in STATIC_METHOD_PATTERN.finditer(utility_section)}

    if FULL_SCAN_PATTERN.search(utility_section):
        return fail(
            "LootboxTrackingRegistryGuard: BossRushLootboxUtility still uses "
            "FindObjects(OfType|ByType)<InteractableLootbox>(...)"
        )

    for method_name in ENTRYPOINT_COLLECTION_METHODS:
        if method_name not in utility_methods:
            return fail(f"LootboxTrackingRegistryGuard: missing collection entrypoint {method_name}")

        if not method_contains_call(utility_section, method_name, "CollectMarkedLootboxesFromRegistry"):
            return fail(
                "LootboxTrackingRegistryGuard: collection path does not reuse "
                "CollectMarkedLootboxesFromRegistry -> " + method_name,
            )

    if not MARKER_LOOTBOX_FIELD_PATTERN.search(marker_section):
        return fail("LootboxTrackingRegistryGuard: BossRushLootboxMarker is missing a cached InteractableLootbox field")

    if not re.search(r"\bOnEnable\s*\(", marker_section):
        return fail("LootboxTrackingRegistryGuard: BossRushLootboxMarker is missing OnEnable")

    if not lifecycle_contains_call(marker_section, "OnEnable", "RegisterMarkedLootboxMarker"):
        return fail("LootboxTrackingRegistryGuard: BossRushLootboxMarker.OnEnable does not self-register")

    if not re.search(r"\bOnDisable\s*\(", marker_section):
        return fail("LootboxTrackingRegistryGuard: BossRushLootboxMarker is missing OnDisable")

    if not lifecycle_contains_call(marker_section, "OnDisable", "UnregisterMarkedLootboxMarker"):
        return fail("LootboxTrackingRegistryGuard: BossRushLootboxMarker.OnDisable does not self-unregister")

    for method_name in UTILITY_REQUIRED_METHODS:
        if not has_static_method(utility_section, method_name):
            return fail(f"LootboxTrackingRegistryGuard: BossRushLootboxUtility is missing {method_name}")

    print("LootboxTrackingRegistryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
