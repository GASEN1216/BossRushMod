"""Guard ZombieMode map selection against double-confirm during scene load."""

from pathlib import Path
import sys


MAP_SELECTION = Path("ZombieMode/ZombieModeMapSelectionHelper.cs")
UI_HELPER = Path("ZombieMode/ZombieModeUIHelper.cs")


def fail(message: str) -> int:
    print("ZombieModeMapSelectionDoubleClickGuard: FAIL - " + message)
    return 1


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    map_selection = MAP_SELECTION.read_text(encoding="utf-8")
    helper = UI_HELPER.read_text(encoding="utf-8")

    for snippet in [
        "private static bool pendingZombieMapLoadStarted = false;",
        "pendingZombieMapLoadStarted",
        "pendingZombieMapLoadStarted = true;",
        "pendingZombieMapLoadStarted = false;",
    ]:
        if snippet not in map_selection:
            return fail("map selection must track confirmed scene-load dispatch: " + snippet)

    confirm_method = extract_method(map_selection, "public static void ConfirmZombieModeMapEntry")
    if not confirm_method:
        return fail("ConfirmZombieModeMapEntry not found")
    if "pendingZombieMapLoadStarted" not in confirm_method:
        return fail("ConfirmZombieModeMapEntry must ignore clicks after map load starts")
    if "pendingZombieMapConfirmed" not in confirm_method:
        return fail("ConfirmZombieModeMapEntry must ignore clicks after confirmation")

    start_method = extract_method(map_selection, "private static void StartZombieModeConfirmedMapLoad")
    if not start_method:
        return fail("StartZombieModeConfirmedMapLoad not found")
    if "pendingZombieMapLoadStarted = true;" not in start_method:
        return fail("StartZombieModeConfirmedMapLoad must mark load-start synchronously before async delay")

    clear_method = extract_method(map_selection, "public static void ClearPendingZombieEntry")
    if not clear_method:
        return fail("ClearPendingZombieEntry not found")
    if "pendingZombieMapLoadStarted = false;" not in clear_method:
        return fail("ClearPendingZombieEntry must reset load-start guard")

    release_method = extract_method(helper, "private static void ReleaseModalInput")
    if not release_method:
        return fail("ReleaseModalInput not found")
    if "catch (Exception e)" not in release_method or "输入释放失败" not in release_method:
        return fail("Modal input release must catch InputManager.ActiveInput failures")

    print("ZombieModeMapSelectionDoubleClickGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
