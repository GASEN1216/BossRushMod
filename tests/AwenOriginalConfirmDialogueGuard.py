"""
Guard: sweep confirmation must use the BossFilter-style reusable modal UI.
"""

from pathlib import Path
import sys


SERVICE = Path("Integration/NPCs/Courier/CourierPaidLootSweepService.cs")
ADAPTER = Path("Integration/NPCs/Courier/OriginalConfirmDialogueAdapter.cs")
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    service_text = SERVICE.read_text(encoding="utf-8")
    compile_text = COMPILE.read_text(encoding="utf-8")

    custom_confirm_type = "Confirm" + "Dialog" + "UI"
    if custom_confirm_type in service_text:
        return fail("AwenOriginalConfirmDialogueGuard: courier sweep still references legacy prompt type")

    if "OriginalConfirmDialogueAdapter.Execute(" not in service_text:
        return fail("AwenOriginalConfirmDialogueGuard: courier sweep does not execute confirm adapter")

    if "sweepPromptInProgress" not in service_text:
        return fail("AwenOriginalConfirmDialogueGuard: courier sweep lacks prompt re-entry guard")

    if "return IsCurrentServiceNpc(npcTransform);" not in service_text:
        return fail("AwenOriginalConfirmDialogueGuard: prompt re-entry does not reject unrelated service NPCs")

    if "GetComponentInParent<CourierMovement>()" not in service_text or "GetComponentInParent<CourierNPCController>()" not in service_text:
        return fail("AwenOriginalConfirmDialogueGuard: service NPC binding does not handle child transforms")

    bind_index = service_text.find("BindServiceNpc(npcTransform);")
    fresh_prompt_index = service_text.find("return ShowFreshSweepPrompt(npcTransform, plans, cost);")
    if bind_index < 0 or fresh_prompt_index < 0 or bind_index > fresh_prompt_index:
        return fail("AwenOriginalConfirmDialogueGuard: fresh sweep prompt does not bind service NPC before async confirm")

    if not ADAPTER.exists():
        return fail("AwenOriginalConfirmDialogueGuard: missing OriginalConfirmDialogueAdapter")

    adapter_text = ADAPTER.read_text(encoding="utf-8")

    for required in (
        "GameplayDataSettings.UIPrefabs.Button",
        "CanvasScaler",
        "GraphicRaycaster",
        "RenderMode.ScreenSpaceOverlay",
        "TextMeshProUGUI",
        "Button",
        "Image",
        "InputManager.DisableInput",
        "InputManager.ActiveInput",
        "UnityEngine.Object.DontDestroyOnLoad",
        "Time.timeScale",
        # 2026-09-23 审美审查 UA-30：遮罩 / 面板走共享 token，不再是 0.45 纯黑 + 中性灰面板。
        "backgroundImage.color = BossRushUIColors.Backdrop;",
        "panelImage.color = BossRushUIColors.Surface;",
        "BossRushUI.PlayOpenAnimation(panelRoot);",
        "new GameObject(\"Panel\")",
        # UA-29：官方 View 里的不可逆操作走不关 View 的入口；显示期间吃掉官方 UI 取消键，隐藏 / 销毁时退订。
        "public static UniTask<OriginalConfirmDialogueResult> ExecuteOverActiveView(",
        "if (!keepActiveView)",
        "UIInputManager.OnCancelEarly += OnUICancelEarly;",
        "UIInputManager.OnCancelEarly -= OnUICancelEarly;",
        "Button target = defaultToCancel ? cancelButton : confirmButton;",
    ):
        if required not in adapter_text:
            return fail("AwenOriginalConfirmDialogueGuard: adapter missing " + required)

    hide_index = adapter_text.find("private static void HideDialog()")
    hide_body = adapter_text[hide_index:hide_index + 400] if hide_index >= 0 else ""
    if "UnsubscribeCancelEarly();" not in hide_body:
        return fail("AwenOriginalConfirmDialogueGuard: HideDialog must unhook UI cancel (AGENTS 4.6)")

    for legacy_look in (
        "new Color(0.24f, 0.19f, 0.13f",   # 没有 sprite 的棕色直角标题条
        "new Color(0.12f, 0.12f, 0.12f",   # 中性灰面板
        "closeButtonLabel.text = \"X\";",  # 字母 X 当关闭图标
    ):
        if legacy_look in adapter_text:
            return fail("AwenOriginalConfirmDialogueGuard: adapter regressed to legacy look -> " + legacy_look)

    confirm_index = adapter_text.find("confirmButton = UnityEngine.Object.Instantiate(buttonPrefab, footer.transform);")
    cancel_index = adapter_text.find("cancelButton = UnityEngine.Object.Instantiate(buttonPrefab, footer.transform);")
    if confirm_index < 0 or cancel_index < 0:
        return fail("AwenOriginalConfirmDialogueGuard: missing footer button instantiation")
    if confirm_index > cancel_index:
        return fail("AwenOriginalConfirmDialogueGuard: footer button order is not confirm-left cancel-right")

    for forbidden in (
        "global::ConfirmDialogue",
        "SkipHide",
        "Resources.FindObjectsOfTypeAll<",
        "ResetOriginalDialogueState",
        "BossRush_OriginalConfirmDialogue_Clone",
    ):
        if forbidden in adapter_text:
            return fail("AwenOriginalConfirmDialogueGuard: adapter still depends on legacy original prompt path -> " + forbidden)

    if "Integration\\NPCs\\Courier\\OriginalConfirmDialogueAdapter.cs" not in compile_text:
        return fail("AwenOriginalConfirmDialogueGuard: compile_official.bat does not include adapter")

    print("AwenOriginalConfirmDialogueGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
