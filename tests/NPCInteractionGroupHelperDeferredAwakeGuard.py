"""
Guard: grouped sub-interactables must be fully wired before Awake can run.

Reason:
- WeddingChapelReplayInteractable hit `Replay base.Awake failed` in Player.log.
- The helper currently adds the component while the child object is active, before
  setup data and group membership are finished.

Requirements:
- AddSubInteractable temporarily deactivates an active child before AddComponent
- it assigns the Interactable layer during setup
- it registers the component into groupList before restoring active state
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/Utils/NPCInteractionGroupHelper.cs")


def fail(message: str) -> int:
    print(message)
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
    block = extract_block(text, "public static T AddSubInteractable<T>(")
    if not block:
        return fail("NPCInteractionGroupHelperDeferredAwakeGuard: missing AddSubInteractable block")

    if "bool restoreActive = childObj.activeSelf;" not in block:
        return fail("NPCInteractionGroupHelperDeferredAwakeGuard: AddSubInteractable does not capture child active state")

    if re.search(r"if\s*\(\s*restoreActive\s*\)\s*\{\s*childObj\.SetActive\s*\(\s*false\s*\)\s*;\s*\}", block) is None:
        return fail("NPCInteractionGroupHelperDeferredAwakeGuard: AddSubInteractable does not deactivate active children before AddComponent")

    if "childObj.layer = interactableLayer;" not in block:
        return fail("NPCInteractionGroupHelperDeferredAwakeGuard: AddSubInteractable does not assign Interactable layer")

    if re.search(r"groupList\.Add\s*\(\s*component\s*\)\s*;\s*[\s\S]*if\s*\(\s*restoreActive\s*\)\s*\{\s*childObj\.SetActive\s*\(\s*true\s*\)\s*;\s*\}", block) is None:
        return fail("NPCInteractionGroupHelperDeferredAwakeGuard: child is reactivated before group registration completes")

    print("NPCInteractionGroupHelperDeferredAwakeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
