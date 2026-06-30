"""Guard: support NPC player lookup fallbacks should read CharacterMainControl.Main once."""

from pathlib import Path
import sys


SOURCES = [
    Path("Integration/NPCs/Courier/CourierMovement.cs"),
    Path("Integration/NPCs/Courier/CourierNPCController.cs"),
    Path("Integration/NPCs/Goblin/GoblinNPCController.cs"),
    Path("Integration/NPCs/Nurse/NurseNPCController.cs"),
]


def fail(message: str) -> int:
    print("SupportNpcPlayerLookupSingleReadGuard: FAIL - " + message)
    return 1


def main() -> int:
    for source in SOURCES:
        text = source.read_text(encoding="utf-8-sig")
        if "CharacterMainControl.Main.transform" in text:
            return fail(f"{source} still dereferences CharacterMainControl.Main after a separate null check")

    print("SupportNpcPlayerLookupSingleReadGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
