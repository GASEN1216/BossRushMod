"""Guard: 许愿抽奖揭晓与收场的结构不变式（2026-09-23 审美审查 UD-11 / UD-12 / UD-15 / UD-21）。

锁住三件事，任何一件退回去都会让玩家「抽到了什么都不知道」或重复拿奖：
1. 奖励只在 Complete() 里发一次：先置 finished、再回调、再走共享淡出；OnDestroy 的保底回调只在 !finished 时触发。
   收场不能退回一帧 Destroy(gameObject)。
2. Esc 在滚动阶段只「跳到揭晓」（置 skipRequested 后 return），揭晓阶段才 Complete()；
   轮带循环要认 skipRequested，结束后进 PlayRevealSequence。
3. 揭晓横幅把奖品名写到屏幕上；许愿面板成功后走 BeginSuccessBeat，抽奖只在 OnClose 的
   ReleaseSuccessBeatOnClose 出口开一次。
"""

from pathlib import Path
import sys


RAV = Path("Integration/WishFountain/WishFountainRewardAnimationView.cs")
REVEAL = Path("Integration/WishFountain/WishFountainRewardAnimationView_Reveal.cs")
UI = Path("Integration/WishFountain/WishFountainUI.cs")
UI_FEEL = Path("Integration/WishFountain/WishFountainUI_Feel.cs")


def fail(message: str) -> int:
    print("WishRewardRevealGuard: FAIL - " + message)
    return 1


def strip_line_comments(text: str) -> str:
    return "\n".join(line.split("//", 1)[0] for line in text.splitlines())


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
    for path in (RAV, REVEAL, UI, UI_FEEL):
        if not path.exists():
            return fail("missing " + path.as_posix())

    rav = strip_line_comments(RAV.read_text(encoding="utf-8"))
    reveal = strip_line_comments(REVEAL.read_text(encoding="utf-8"))
    ui = strip_line_comments(UI.read_text(encoding="utf-8"))
    ui_feel = strip_line_comments(UI_FEEL.read_text(encoding="utf-8"))

    # 1) 一次发奖 + 淡出收场
    complete = extract_block(rav, "private void Complete()")
    if not complete:
        return fail("missing Complete()")
    i_guard = complete.find("if (finished)")
    i_flag = complete.find("finished = true;")
    i_callback = complete.find("finishedCallback(rewardTypeId, rewardDisplayName)")
    if min(i_guard, i_flag, i_callback) < 0 or not (i_guard < i_flag < i_callback):
        return fail("Complete() must early-return on finished, set finished before invoking the reward callback")
    if "BossRushUIKit.PlayCloseAndDestroy(gameObject" not in complete:
        return fail("Complete() must close through the shared fade (BossRushUIKit.PlayCloseAndDestroy)")
    if "Destroy(gameObject)" in complete.replace("PlayCloseAndDestroy(gameObject", ""):
        return fail("Complete() must not destroy the overlay in one frame")
    on_destroy = extract_block(rav, "private void OnDestroy()")
    if "if (!finished && finishedCallback != null)" not in on_destroy:
        return fail("OnDestroy fallback callback must stay gated by !finished")

    # 2) Esc：滚动中跳到揭晓，揭晓中才收下
    update = extract_block(rav, "private void Update()")
    if "GetKeyDown(KeyCode.Escape)" in update:
        return fail("Update() must not handle Esc directly; route through HandleRevealInput")
    if "HandleRevealInput();" not in update:
        return fail("Update() must call HandleRevealInput()")
    handle = extract_block(reveal, "private void HandleRevealInput()")
    rolling = extract_block(handle, "if (!revealStarted)")
    if "skipRequested = true;" not in rolling or "return;" not in rolling or "Complete()" in rolling:
        return fail("Esc while rolling must only request skip-to-reveal, never Complete()")
    if "Complete();" not in handle.replace(rolling, ""):
        return fail("Esc / click during the reveal must collect via Complete()")
    roll = extract_block(rav, "private IEnumerator PlayAnimationCoroutine()")
    if "!skipRequested" not in roll or "PlayRevealSequence()" not in roll:
        return fail("roll loop must honour skipRequested and then play the reveal")

    # 3) 屏幕上出现奖品名；许愿面板成功后停一拍、只在关闭出口开抽
    banner = extract_block(reveal, "private void ShowResultBanner(")
    if "rewardDisplayName" not in banner:
        return fail("reveal banner must put the reward name on screen")
    if "BeginSuccessBeat();" not in ui:
        return fail("successful wish must go through BeginSuccessBeat()")
    on_close = extract_block(ui, "protected override void OnClose()")
    if "ReleaseSuccessBeatOnClose();" not in on_close:
        return fail("OnClose must release the pending draw via ReleaseSuccessBeatOnClose()")
    release = extract_block(ui_feel, "private void ReleaseSuccessBeatOnClose()")
    i_check = release.find("if (!rewardPendingOnClose)")
    i_clear = release.find("rewardPendingOnClose = false;")
    i_notify = release.find("NotifyClosedAfterSuccessfulWish();")
    if min(i_check, i_clear, i_notify) < 0 or not (i_check < i_clear < i_notify):
        return fail("ReleaseSuccessBeatOnClose must be idempotent (check, clear, then start the draw)")
    if ui.count("NotifyClosedAfterSuccessfulWish();") != 0:
        return fail("WishFountainUI.cs must not start the draw directly; only ReleaseSuccessBeatOnClose may")

    print("WishRewardRevealGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
