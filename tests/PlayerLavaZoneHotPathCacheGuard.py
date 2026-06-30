"""Guard: player lava zones should cache frame-time and Transform reads in hot paths."""

from pathlib import Path
import sys


SOURCE = Path("Integration/Bonus/DragonSetBonus.cs")


def fail(message: str) -> int:
    print("PlayerLavaZoneHotPathCacheGuard: FAIL - " + message)
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
                return text[start : idx + 1]

    return None


def extract_method_body(text: str, signature: str) -> str | None:
    block = extract_block(text, signature)
    if block is None:
        return None

    brace_start = block.find("{")
    return block[brace_start:] if brace_start >= 0 else None


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8-sig")
    lava_zone = extract_block(text, "public class PlayerLavaZone")
    if lava_zone is None:
        return fail("missing PlayerLavaZone")

    if "private Transform cachedTransform = null;" not in lava_zone:
        return fail("missing cached Transform field")

    initialize = extract_method_body(lava_zone, "public void Initialize(")
    if initialize is None:
        return fail("missing Initialize")
    if "cachedTransform = transform;" not in initialize:
        return fail("Initialize should seed cachedTransform")

    update = extract_method_body(lava_zone, "void Update()")
    if update is None:
        return fail("missing Update")

    required_update_tokens = [
        "float currentTime = Time.time;",
        "float elapsed = currentTime - createTime;",
        "if (currentTime - lastDamageTime >= damageInterval)",
        "lastDamageTime = currentTime;",
    ]
    for token in required_update_tokens:
        if token not in update:
            return fail("Update missing single-frame time token -> " + token)

    if update.count("Time.time") != 1:
        return fail("Update should read Time.time once")

    damage = extract_method_body(lava_zone, "private void DamageEnemiesInRange()")
    if damage is None:
        return fail("missing DamageEnemiesInRange")

    required_damage_tokens = [
        "Transform zoneTransform = cachedTransform;",
        "zoneTransform = transform;",
        "cachedTransform = zoneTransform;",
        "Physics.OverlapSphereNonAlloc(zoneTransform.position, radius, hitBuffer, characterLayerMask)",
    ]
    for token in required_damage_tokens:
        if token not in damage:
            return fail("DamageEnemiesInRange missing cached-transform token -> " + token)

    if "transform.position" in damage:
        return fail("DamageEnemiesInRange should not use direct transform.position")

    print("PlayerLavaZoneHotPathCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
