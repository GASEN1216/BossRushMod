"""
Guard: 快递员（阿稳）Update 不得每帧查存档。

CourierNPCController.Update 每帧调用 CheckFirstMeetTrigger。以前它第一句就读 HasTriggeredFirstMeet，
而那个属性每次都 SavesSystem.KeyExisits + SavesSystem.Load<bool>（两次 ES3 查询），见过面之后也照查不误
（2026-10-01 发布前审查 P3：快递员每帧查一次存档）。

不变式：
1. HasTriggeredFirstMeet 首句是实例缓存短路 `if (firstMeetTriggeredCached) return true;`，
   读到存档值时写回缓存（只缓存 true 的语义由「只有 SetFirstMeetTriggered 写、且只写 true」保证）。
2. CheckFirstMeetTrigger 里先做对话中 / 玩家为空 / 距离判断，HasTriggeredFirstMeet 只出现在距离分支里面。
3. Update 方法体内不直接出现 SavesSystem。
"""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source

SOURCE = Path("Integration/NPCs/Courier/CourierNPCController.cs")


def fail(message: str) -> int:
    print("CourierFirstMeetSaveQueryGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None
    brace = text.find("{", start)
    if brace < 0:
        return None
    depth = 0
    for idx in range(brace, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace : idx + 1]
    return None


def norm(text: str) -> str:
    return " ".join(text.split())


def main() -> int:
    if not SOURCE.exists():
        return fail(f"missing {SOURCE}")
    text = clean_source(SOURCE.read_text(encoding="utf-8-sig"))

    if "private bool firstMeetTriggeredCached = false;" not in norm(text):
        return fail("missing instance cache field firstMeetTriggeredCached")

    prop = extract_block(text, "private bool HasTriggeredFirstMeet")
    if prop is None:
        return fail("missing HasTriggeredFirstMeet property")
    prop_n = norm(prop)
    if not prop_n.startswith("{ get { if (firstMeetTriggeredCached) return true;"):
        return fail("HasTriggeredFirstMeet must short-circuit on firstMeetTriggeredCached before touching SavesSystem")
    for required in (
        "firstMeetTriggeredCached = Saves.SavesSystem.Load<bool>(FIRST_MEET_SAVE_KEY);",
        "return firstMeetTriggeredCached;",
    ):
        if required not in prop_n:
            return fail(f"HasTriggeredFirstMeet lacks -> {required}")

    check = extract_block(text, "private void CheckFirstMeetTrigger()")
    if check is None:
        return fail("missing CheckFirstMeetTrigger body")
    check_n = norm(check)
    dialogue_gate = "if (isInFirstMeetDialogue) return;"
    player_gate = "if (playerTransform == null) return;"
    near_branch = "if ((CachedTransform.position - playerTransform.position).sqrMagnitude <= nearDistanceSqr) {"
    save_gate = "if (HasTriggeredFirstMeet) return;"
    positions = [check_n.find(token) for token in (dialogue_gate, player_gate, near_branch, save_gate)]
    if any(pos < 0 for pos in positions):
        return fail("CheckFirstMeetTrigger lacks dialogue / player / distance / save gates")
    if not positions == sorted(positions):
        return fail("CheckFirstMeetTrigger must query HasTriggeredFirstMeet only inside the near-distance branch")
    if check_n.count("HasTriggeredFirstMeet") != 1:
        return fail("CheckFirstMeetTrigger must read HasTriggeredFirstMeet exactly once (inside the near branch)")

    update = extract_block(text, "void Update()")
    if update is None:
        return fail("missing Update body")
    if "SavesSystem" in update:
        return fail("Update must not query SavesSystem directly")

    print("CourierFirstMeetSaveQueryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
