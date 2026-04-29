"""
Guard: 扫箱交互入口不应触发阿稳的随机闲聊气泡。
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/NPCs/Courier/CourierNPC.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, anchor: str, marker: str) -> str:
    marker_index = text.find(marker)
    if marker_index < 0:
        return ""

    start = text.rfind(anchor, 0, marker_index)
    if start < 0:
        return ""

    brace_start = text.find("{", start)
    if brace_start < 0:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start:index + 1]

    return ""


def require_silent_talking(block: str, block_name: str) -> int:
    if not block:
        return fail("AwenSweepInteractionNoSmallTalkGuard: missing block -> " + block_name)

    if "controller.StartTalking(false);" not in block:
        return fail("AwenSweepInteractionNoSmallTalkGuard: " + block_name + " does not use StartTalking(false)")

    if "controller.StartTalking();" in block:
        return fail("AwenSweepInteractionNoSmallTalkGuard: " + block_name + " still triggers random small talk")

    return 0


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    legacy_paid_sweep_block = extract_block(
        text,
        "protected override void OnInteractStart(CharacterMainControl interactCharacter)",
        "玩家选择付费扫箱")
    if not legacy_paid_sweep_block:
        return fail("AwenSweepInteractionNoSmallTalkGuard: failed to locate paid sweep OnInteractStart block")

    main_sweep_block = extract_block(
        text,
        "protected override void OnTimeOut()",
        "玩家选择付费扫箱（主交互）")
    if not main_sweep_block:
        return fail("AwenSweepInteractionNoSmallTalkGuard: failed to locate main sweep OnTimeOut block")

    result = require_silent_talking(legacy_paid_sweep_block, "paid sweep sub interaction")
    if result != 0:
        return result

    result = require_silent_talking(main_sweep_block, "paid sweep main interaction")
    if result != 0:
        return result

    print("AwenSweepInteractionNoSmallTalkGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
