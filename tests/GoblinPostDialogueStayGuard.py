"""GoblinPostDialogueStayGuard: 叮当对话 / 重铸 / 商店结束后原地站够，召唤能打断（2026-11-24 玩家反馈）。

- 默认停留至少 2 分钟（玩家关界面后翻背包挑下一件再回来）。
- 聊天与重铸入口用默认停留常量；商店、重铸界面关闭（showFarewell）在 EndDialogueWithStay 里抬到默认停留。
- 停留期间 isInDialogue 为 true，移动组件丢弃寻路；两个召唤入口必须先打断停留，否则召唤道具失效。
"""

from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source  # noqa: E402


CONSTANTS = Path("Integration/Constants/GoblinNPCConstants.cs")
DIALOGUE = Path("Integration/NPCs/Goblin/GoblinNPCDialogue.cs")
CONTROLLER = Path("Integration/NPCs/Goblin/GoblinNPCController.cs")
INTERACTABLE = Path("Integration/Reforge/GoblinReforgeInteractable.cs")
REFORGE_UI = (Path("Integration/Reforge/ReforgeUIManager.cs"), Path("Integration/Reforge/ReforgeUIManager_RuntimeAndCleanup.cs"))
STAY = "BossRush.Constants.GoblinNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION"


def fail(message: str) -> int:
    print("GoblinPostDialogueStayGuard: FAIL - " + message)
    return 1


def read(path: Path) -> str:
    return clean_source(path.read_text(encoding="utf-8-sig"))


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0 or source.find(signature, start + 1) >= 0:
        return ""
    opening = source.index("{", start)
    depth, index = 1, opening + 1
    while depth:
        depth += (source[index] == "{") - (source[index] == "}")
        index += 1
    return source[opening:index]


def main() -> int:
    match = re.search(r"public const float DEFAULT_DIALOGUE_STAY_DURATION = ([0-9.]+)f;", read(CONSTANTS))
    if not match or float(match.group(1)) < 120.0:
        return fail("goblin must stay at least 2 minutes after dialogue / reforge / shop")

    stay = method_body(read(DIALOGUE), "public void EndDialogueWithStay(float stayDuration = 10f, bool showFarewell = false)")
    floor = "if (showFarewell && stayDuration < GoblinNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION)"
    adjust = "AdjustDialogueStayDurationForSpouseFollow("
    if floor not in stay or adjust not in stay or stay.index(floor) > stay.index(adjust):
        return fail("shop / reforge close must be floored to the default stay before spouse-follow adjustment")

    interactable = read(INTERACTABLE)
    if "EndDialogueWithStay(10f)" in interactable or interactable.count("EndDialogueWithStay(" + STAY + ")") != 3:
        return fail("chat / rebuke / error paths must use the default stay constant")
    for path in REFORGE_UI:
        if "currentController.EndDialogueWithStay(" + STAY + ", true);" not in read(path):
            return fail("reforge UI close must use the default stay constant -> " + str(path))

    controller = read(CONTROLLER)
    for signature in ("public void RunToPlayer()", "public void RunToPlayerWithDialogue(bool positive)"):
        body = method_body(controller, signature)
        if "CancelPostDialogueStayForSummon();" not in body or body.index("CancelPostDialogueStayForSummon();") > body.index("isRunningToPlayer = true;"):
            return fail("summon must cancel the post-dialogue stay before running -> " + signature)
    cancel = method_body(controller, "private void CancelPostDialogueStayForSummon()")
    for token in ("isInStoryDialogue", "ShouldRemainStationaryAtWeddingChapel()", "StopCoroutine(currentStayCoroutine);", "isInDialogue = false;"):
        if token not in cancel:
            return fail("stay cancellation lost a condition -> " + token)

    print("GoblinPostDialogueStayGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
