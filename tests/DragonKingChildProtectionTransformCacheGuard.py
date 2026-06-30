"""Guard: Dragon King child-protection paths should reuse cached boss Transform."""

from pathlib import Path
import sys


CONTROLLER = Path("Integration/DragonKing/DragonKingAbilityController.cs")
CHILD = Path("Integration/DragonKing/DragonKingAbilityController_ChildProtection.cs")


def fail(message: str) -> int:
    print("DragonKingChildProtectionTransformCacheGuard: FAIL - " + message)
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


def main() -> int:
    controller = CONTROLLER.read_text(encoding="utf-8-sig")
    child = CHILD.read_text(encoding="utf-8-sig")

    for token in [
        "private Transform bossTransform;",
        "private Transform BossTransform",
        "bossTransform = bossCharacter != null ? bossCharacter.transform : null;",
        "return bossTransform;",
        "bossTransform = null;",
    ]:
        if token not in controller:
            return fail("missing controller cache token -> " + token)

    if "bossCharacter.transform" in child:
        return fail("child-protection file still reads bossCharacter.transform directly")

    for signature in [
        "private void ShowDragonKingDialogue()",
        "private IEnumerator FlyToHeight(float targetHeight)",
        "private void UpdateFlightPlatformPosition()",
        "private void LateUpdate()",
        "private void CreateFlightCloudEffect()",
        "private void FireChildProtectionBolt()",
        "private Vector3 GetRandomSpawnPoint()",
        "private void TriggerLinkedDeath()",
    ]:
        block = extract_block(child, signature)
        if block is None:
            return fail("missing block -> " + signature)
        if "BossTransform" not in block:
            return fail(signature + " should use BossTransform")

    print("DragonKingChildProtectionTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
