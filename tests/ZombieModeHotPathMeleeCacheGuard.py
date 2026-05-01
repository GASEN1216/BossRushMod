"""ZombieModeHotPathMeleeCacheGuard: 受伤热路径不得实例化物品判断近战类型。"""

from pathlib import Path
import re
import sys


POLLUTION = Path("ZombieMode/ZombieModePollution.cs")


def fail(message: str) -> int:
    print(message)
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


def main() -> int:
    text = POLLUTION.read_text(encoding="utf-8")
    body = extract_method_body(text, "private bool IsZombieModeDamageFromMeleeWeapon(")
    if body is None:
        return fail("ZombieModeHotPathMeleeCacheGuard: missing IsZombieModeDamageFromMeleeWeapon")

    if "InstantiateSync" in body:
        return fail("ZombieModeHotPathMeleeCacheGuard: damage hot path still instantiates weapon items")

    if "Destroy(item.gameObject)" in body:
        return fail("ZombieModeHotPathMeleeCacheGuard: damage hot path still destroys temporary weapon items")

    required = [
        "zombieModeMeleeWeaponTypeCache",
        "TryGetValue(damageInfo.fromWeaponItemID",
        "CacheZombieModeMeleeWeaponType(",
    ]
    for snippet in required:
        if snippet not in text:
            return fail("ZombieModeHotPathMeleeCacheGuard: missing melee cache snippet -> " + snippet)

    helper_body = extract_method_body(text, "private bool CacheZombieModeMeleeWeaponType(")
    if helper_body is None:
        return fail("ZombieModeHotPathMeleeCacheGuard: missing CacheZombieModeMeleeWeaponType helper")

    if re.search(r"InstantiateSync\s*\(", helper_body):
        return fail("ZombieModeHotPathMeleeCacheGuard: melee cache helper must inspect prefabs, not instantiate items")

    tag_body = extract_method_body(text, "private bool ItemHasZombieModeTag(")
    if tag_body is None:
        return fail("ZombieModeHotPathMeleeCacheGuard: missing ItemHasZombieModeTag helper")

    if "item.Tags.Contains(target)" not in tag_body:
        return fail("ZombieModeHotPathMeleeCacheGuard: ItemHasZombieModeTag lost direct tag lookup")

    if "string.Equals(tag.name, target.name, System.StringComparison.OrdinalIgnoreCase)" not in tag_body:
        return fail("ZombieModeHotPathMeleeCacheGuard: ItemHasZombieModeTag must keep name-based tag fallback")

    print("ZombieModeHotPathMeleeCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
