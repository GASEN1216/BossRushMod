"""Guard ZombieMode choice UI pause/cursor state and HUD requested offsets."""

from pathlib import Path
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
ENTRY_PARTS = [
    ENTRY,
    Path("ZombieMode/ZombieModeEntry_StarterLoadout.cs"),
]
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
    # 2026-09-23 审美审查：奖励选择面板与终端服务面板 / 交互体从 ZombieModeRewards.cs（宿主 partial）拆到独立文件，
    # 结构断言照旧覆盖它们。
    Path("ZombieMode/ZombieModeRewardSelectionView.cs"),
    Path("ZombieMode/ZombieModeTemporaryNpcServiceView.cs"),
]


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REWARD_PARTS)


def read_entry() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in ENTRY_PARTS)

cash = Path("ZombieMode/ZombieModeCashInvestmentView.cs")
extraction = Path("ZombieMode/ZombieModeExtractionController.cs")
HUD = Path("ZombieMode/ZombieModeHudController.cs")
UI_HELPER = Path("ZombieMode/ZombieModeUIHelper.cs")
MODE_RUNTIME_HOOKS = Path("Utilities/ModeRuntimeHooks.cs")
ZOMBIE_RUNTIME_HOOKS = Path("ZombieMode/ZombieModeRuntimeHooks.cs")


def fail(message: str) -> int:
    print("ZombieModeChoiceUiPauseAndLayoutGuard: FAIL - " + message)
    return 1


def extract_block(text: str, marker: str) -> str:
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
    entry = read_entry()
    rewards = read_rewards()
    cash_text = cash.read_text(encoding="utf-8")
    extraction_text = extraction.read_text(encoding="utf-8")
    hud = HUD.read_text(encoding="utf-8")
    helper = UI_HELPER.read_text(encoding="utf-8")
    mode_runtime_hooks = MODE_RUNTIME_HOOKS.read_text(encoding="utf-8")
    zombie_runtime_hooks = ZOMBIE_RUNTIME_HOOKS.read_text(encoding="utf-8")

    for snippet in [
        "internal sealed class ModalInputLease",
        "_modalInputLeaseCount",
        "_modalPreviousTimeScale",
        "_modalPreviousCursorVisible",
        "_modalPreviousCursorLockState",
        "Time.timeScale = 0f;",
        "Cursor.visible = true;",
        "Cursor.lockState = CursorLockMode.None;",
        "InputManager.DisableInput(inputToken);",
        "InputManager.ActiveInput(lease.InputToken);",
        "Time.timeScale = _modalPreviousTimeScale;",
        "Cursor.visible = _modalPreviousCursorVisible;",
        "Cursor.lockState = _modalPreviousCursorLockState;",
        "internal static bool IsModalInputPaused",
        "internal static void EnforceModalInputPause()",
    ]:
        if snippet not in helper:
            return fail("ZombieModeUIHelper modal input lease missing: " + snippet)

    tick_method = extract_block(entry, "private void TickZombieMode(float deltaTime)")
    if not tick_method:
        return fail("TickZombieMode not found")
    if "IsZombieModeRuntimePaused()" not in tick_method:
        return fail("TickZombieMode must not advance ZombieMode timers while modal UI pauses time")
    runtime_pause = extract_block(entry, "internal bool IsZombieModeRuntimePaused()")
    if "ZombieModeUIHelper.IsModalInputPaused" not in runtime_pause:
        return fail("ZombieMode runtime pause helper must include modal UI pause")

    late_update = extract_block(Path("ModBehaviour.cs").read_text(encoding="utf-8"), "void LateUpdate()")
    if not late_update:
        return fail("ModBehaviour LateUpdate not found")
    if "LateUpdateModeRuntimeGroup();" not in late_update:
        return fail("ModBehaviour LateUpdate must execute mode late-update hooks")
    late_update_mode_group = extract_block(mode_runtime_hooks, "internal void LateUpdateModeRuntimeGroup()")
    if "LateUpdateZombieModeRuntime();" not in late_update_mode_group:
        return fail("Mode late-update hooks must include ZombieMode modal pause enforcement")
    late_update_zombie_mode = extract_block(zombie_runtime_hooks, "internal void LateUpdateZombieModeRuntime()")
    if "ZombieModeUIHelper.EnforceModalInputPause();" not in late_update_zombie_mode:
        return fail("ZombieMode modal UI pause must be enforced in LateUpdate while the UI is open")

    for text, class_name in [
        (entry, "ZombieModeStarterChoiceView"),
        (rewards, "ZombieModeRewardSelectionView"),
        (cash_text, "ZombieModeCashInvestmentView"),
        (extraction_text, "ZombieModeExtractionOpportunityView"),
        (rewards, "ZombieModeTemporaryNpcServiceView"),
    ]:
        class_text = extract_block(text, "public sealed class " + class_name)
        if not class_text:
            return fail(class_name + " not found")
        for snippet in [
            "ZombieModeUIHelper.ModalInputLease inputLease",
            "ZombieModeUIHelper.ClaimModalInput(gameObject",
            "inputLease.Release();",
            "RestoreInputState();",
        ]:
            if snippet not in class_text:
                return fail(class_name + " missing shared pause/cursor/input handling: " + snippet)

    # 2026-09-24 UI 共识对照审查 B-01（CR-2026-09-24-003）：撤离抉择的模态租约只在宿主受理后还。
    # 旧版按钮回调先 RestoreInputState() 再调宿主；宿主拒绝（信标引导中、撤离点建不出来）时页面不关，
    # 面板盖着但时间恢复、角色能动。现在两条路只走 Choose：先调宿主，受理时宿主经 ReleaseInput 收页；
    # 拒绝时保留租约并就地提示原因。
    extraction_view = extract_block(extraction_text, "public sealed class ZombieModeExtractionOpportunityView")
    choose = extract_block(extraction_view, "private void Choose(bool extract)")
    if not choose:
        return fail("ZombieModeExtractionOpportunityView must route both choices through Choose(bool extract)")
    host_calls = [choose.find("owner.StartZombieModeExtractionFromUi(runId)"),
                  choose.find("owner.ContinueZombieModeAfterExtractionOpportunity(runId)")]
    if min(host_calls) < 0:
        return fail("Choose must call both host entry points")
    for release in ["RestoreInputState()", "ReleaseInput()", "inputLease.Release()"]:
        at = choose.find(release)
        if 0 <= at < max(host_calls):
            return fail("extraction choice must not release the modal lease before the host accepts: " + release)
    if "refusalKey = owner.StartZombieModeExtractionFromUi(runId)" not in choose or "ZombieModeUiNudge.Flash(" not in choose:
        return fail("a refused extraction must keep the page and show the host's reason in place")
    card = extract_block(extraction_view, "private void CreateChoiceCard(")
    if "RestoreInputState()" in card or "ReleaseInput()" in card:
        return fail("extraction choice buttons must not release the modal lease themselves; go through Choose")
    host_start = extract_block(extraction_text, "private string StartZombieModeExtraction(int runId)")
    if 'return "BossRush_ZombieMode_Notify_ExtractionBeaconLocked";' not in host_start or \
            'return "BossRush_ZombieMode_Notify_ExtractionAreaFailed";' not in host_start:
        return fail("StartZombieModeExtraction must return the refusal reason to the page instead of a toast hidden under the modal")

    for snippet in [
        "new Vector2(24f, -294f)",
        "new Vector2(-408f, -24f)",
        "new Vector2(0f, 156f)",
    ]:
        if snippet not in hud:
            return fail("HUD requested screen offset missing: " + snippet)

    print("ZombieModeChoiceUiPauseAndLayoutGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
