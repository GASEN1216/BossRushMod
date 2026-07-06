"""Guard: Dragon King boss gun must keep all 15 ammo profiles playable."""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunProfiles.cs")
EXPECTED_PROFILE_IDS = {
    "Rocket",
    "Smg",
    "Assault",
    "Heavy",
    "Sniper",
    "Shotgun",
    "Magnum",
    "Arrow",
    "Energy",
    "Poop",
    "Candy",
    "IceBlade",
    "Snow",
    "Nano",
    "Firework",
}
EXPECTED_TYPE_IDS = [326, 594, 603, 612, 621, 630, 640, 648, 650, 944, 1262, 1303, 1351, 1434, 1523]


def fail(message: str) -> int:
    print("DragonKingBossGunProfileCoverageGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str | None:
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
    text = SOURCE.read_text(encoding="utf-8-sig")
    enum_body = extract_block(text, "internal enum DragonKingBossGunProfileId")
    if enum_body is None:
        return fail("missing DragonKingBossGunProfileId enum")

    enum_names = set(re.findall(r"^\s*(\w+)\s*=\s*\d+", enum_body, re.MULTILINE))
    if enum_names != EXPECTED_PROFILE_IDS:
        return fail("profile enum mismatch: " + ", ".join(sorted(enum_names ^ EXPECTED_PROFILE_IDS)))

    profile_count = text.count("Id = DragonKingBossGunProfileId.")
    if profile_count != 15:
        return fail(f"expected 15 ordered profiles, found {profile_count}")

    for type_id in EXPECTED_TYPE_IDS:
        pattern = r"TypeIds\s*=\s*new\[\]\s*\{\s*" + str(type_id) + r"\s*\}"
        if not re.search(pattern, text):
            return fail(f"missing TypeIds entry for {type_id}")

    if "public static bool TryResolveTypeId(int typeId" not in text:
        return fail("missing TryResolveTypeId helper for UI-selected ammo")

    print("DragonKingBossGunProfileCoverageGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
