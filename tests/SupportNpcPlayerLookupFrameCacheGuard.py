"""Guard: support NPC runtime player lookups should share a per-frame cache."""

from pathlib import Path
import sys


SUPPORT_NPC_SOURCES = [
    Path("Integration/Utils/NPCFollowMovementBase.cs"),
    Path("Integration/NPCs/Courier/CourierMovement.cs"),
    Path("Integration/NPCs/Courier/CourierNPCController.cs"),
    Path("Integration/NPCs/Goblin/GoblinMovement.cs"),
    Path("Integration/NPCs/Goblin/GoblinNPCController.cs"),
    Path("Integration/NPCs/Nurse/NurseNPCController.cs"),
]


def fail(message: str) -> int:
    print("SupportNpcPlayerLookupFrameCacheGuard: FAIL - " + message)
    return 1


def main() -> int:
    helper_text = Path("Integration/Utils/NPCFollowMovementBase.cs").read_text(encoding="utf-8-sig")
    if "internal static class NPCPlayerLookupCache" not in helper_text:
        return fail("NPCPlayerLookupCache helper is missing")
    if "cachedPlayerLookupFrame == Time.frameCount" not in helper_text:
        return fail("helper does not cache player lookup work within a frame")
    if helper_text.count('GameObject.FindGameObjectWithTag("Player")') != 1:
        return fail("helper should be the only support-NPC Player tag lookup site")

    for source in SUPPORT_NPC_SOURCES:
        text = source.read_text(encoding="utf-8-sig")
        if source.name != "NPCFollowMovementBase.cs" and 'GameObject.FindGameObjectWithTag("Player")' in text:
            return fail(f"{source} still performs a direct Player tag lookup")

    print("SupportNpcPlayerLookupFrameCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
