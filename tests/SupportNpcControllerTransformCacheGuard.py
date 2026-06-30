"""Guard: support NPC controllers should cache their Transform in hot paths."""

from pathlib import Path
import sys


SOURCES = [
    Path("Integration/NPCs/Courier/CourierNPCController.cs"),
    Path("Integration/NPCs/Goblin/GoblinNPCController.cs"),
    Path("Integration/NPCs/Nurse/NurseNPCController.cs"),
]


def fail(message: str) -> int:
    print("SupportNpcControllerTransformCacheGuard: FAIL - " + message)
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
    for source in SOURCES:
        text = source.read_text(encoding="utf-8-sig")
        name = str(source)

        if "private Transform cachedTransform;" not in text:
            return fail(name + " missing cached Transform field")

        accessor = extract_block(text, "private Transform CachedTransform")
        if accessor is None:
            return fail(name + " missing CachedTransform accessor")
        for token in [
            "cachedTransform = transform;",
            "return cachedTransform;",
        ]:
            if token not in accessor:
                return fail(name + " CachedTransform accessor missing token -> " + token)

        update = extract_block(text, "void Update()")
        if update is None:
            update = extract_block(text, "private void Update()")
        if update is None:
            return fail(name + " missing Update block")
        if "transform.position" in update:
            return fail(name + " Update should not use direct transform.position")
        if "CachedTransform.position" not in text:
            return fail(name + " hot paths should use cached Transform position")

        if "NPCNameTagHelper.RefreshOriginalHealthBarName(transform)" in text:
            return fail(name + " name-tag refresh should use cached Transform")
        if "NPCNameTagHelper.UnregisterOriginalHealthBarName(transform)" in text:
            return fail(name + " name-tag unregister should use cached Transform")

        if "public Transform NpcTransform => transform;" in text:
            return fail(name + " INPCController Transform should use cached Transform")

    goblin_dialogue = Path("Integration/NPCs/Goblin/GoblinNPCDialogue.cs").read_text(encoding="utf-8-sig")
    goblin_face_player = extract_block(goblin_dialogue, "private void FacePlayer()")
    if goblin_face_player is None:
        return fail("GoblinNPCDialogue.cs missing FacePlayer")
    for token in [
        "Transform selfTransform = CachedTransform;",
        "selfTransform.position",
        "selfTransform.rotation",
    ]:
        if token not in goblin_face_player:
            return fail("Goblin FacePlayer should use cached Transform token -> " + token)
    if "transform.position" in goblin_face_player or "transform.rotation" in goblin_face_player:
        return fail("Goblin FacePlayer should not use direct transform")

    print("SupportNpcControllerTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
