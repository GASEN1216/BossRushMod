"""Guard Mode E shell two-level generation lifecycle and UI binding invalidation."""

from pathlib import Path
import sys


SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")
MODE_E = Path("ModeE/ModeE.cs")
LIFECYCLE = Path("ModeE/ModeELifecycle.cs")
RUNTIME = Path("ModeE/ModeERuntimeModule.cs")
STARTUP = Path("ModeE/ModeEStartup.cs")


def fail(message: str) -> int:
    print("ModeEShellRuntimeLifecycleGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for idx in range(brace, len(text)):
        if text[idx] == "{":
            depth += 1
        elif text[idx] == "}":
            depth -= 1
            if depth == 0:
                return text[start : idx + 1]
    return ""


def main() -> int:
    support = SUPPORT.read_text(encoding="utf-8")
    mode_e = MODE_E.read_text(encoding="utf-8")
    lifecycle = LIFECYCLE.read_text(encoding="utf-8")
    runtime = RUNTIME.read_text(encoding="utf-8")
    startup = STARTUP.read_text(encoding="utf-8")
    combined = "\n".join([support, mode_e, lifecycle, runtime, startup])

    required = [
        "private long modeEShellSessionGeneration = 0L;",
        "private long modeEShellMerchantGeneration = 0L;",
        "private long modeEShellNextUiBindingID = 0L;",
        "private long modeEShellActiveUiBindingID = 0L;",
        "private void InvalidateModeEShellMerchantGeneration(string reason)",
        "internal void InvalidateAndResetModeEShellSession(string reason)",
        "internal void DestroyModeEShellRuntimeState()",
        "private void InvalidateModeEShellUiBinding(string reason)",
        "internal bool TryAttachModeEShellUi(StockShop shop, out long uiBindingID)",
        "internal void DetachModeEShellUi(StockShop shop, long uiBindingID, string reason)",
        "modeEOwnedShopTombstones",
    ]
    for token in required:
        if token not in combined:
            return fail("missing lifecycle invariant -> " + token)

    merchant_inv = extract_method(support, "private void InvalidateModeEShellMerchantGeneration(string reason)")
    if not merchant_inv:
        return fail("missing merchant generation invalidation")
    if "modeEShellMerchantGeneration++" not in merchant_inv:
        return fail("merchant invalidation must bump merchantGeneration")
    if "modeEShellSessionGeneration++" in merchant_inv:
        return fail("merchant invalidation must not bump sessionGeneration")
    if "modeEShellBalance = 0" in merchant_inv:
        return fail("merchant invalidation must not clear balance")
    if "modeEShellFirstPositiveRewardGranted" in merchant_inv and "= false" in merchant_inv[
        merchant_inv.find("modeEShellFirstPositiveRewardGranted") : merchant_inv.find("modeEShellFirstPositiveRewardGranted") + 80
    ]:
        return fail("merchant invalidation must not clear first-reward flag")
    if "InvalidateModeEShellUiBinding" not in merchant_inv:
        return fail("merchant invalidation must invalidate uiBindingId")
    if "modeEShellPriceCache.Clear()" not in merchant_inv:
        return fail("merchant invalidation must clear price cache for old generation")

    session_inv = extract_method(support, "internal void InvalidateAndResetModeEShellSession(string reason)")
    if "InvalidateModeEShellMerchantGeneration" not in session_inv:
        return fail("session invalidation must call merchant invalidation first")
    if "modeEShellEconomyAvailable = false" not in session_inv:
        return fail("session invalidation must clear capability")
    if "modeEShellBalance = 0" not in session_inv:
        return fail("session invalidation must zero balance")
    if "modeEShellFirstPositiveRewardGranted = false" not in session_inv:
        return fail("session invalidation must clear first-reward flag")
    if "modeEShellSessionGeneration++" not in session_inv:
        return fail("session invalidation must bump sessionGeneration")

    if "InvalidateAndResetModeEShellSession" not in combined:
        return fail("session reset entry must be reachable")
    # Lifecycle / destroy paths should call session reset.
    if "InvalidateAndResetModeEShellSession" not in lifecycle and "DestroyModeEShellRuntimeState" not in lifecycle:
        if "InvalidateAndResetModeEShellSession" not in runtime and "DestroyModeEShellRuntimeState" not in runtime:
            return fail("EndModeE/runtime destroy must call session reset")

    attach = extract_method(support, "internal bool TryAttachModeEShellUi(StockShop shop, out long uiBindingID)")
    if "NextModeEShellCounter(ref modeEShellNextUiBindingID)" not in attach and "modeEShellNextUiBindingID" not in attach:
        return fail("Attach must allocate new uiBindingId")
    if "InvalidateModeEShellUiBinding" not in attach and "DetachModeEShellUi" not in attach:
        # may detach old first
        if "modeEShellActiveUiBindingID" not in attach:
            return fail("Attach must manage active uiBindingId")

    runtime_destroy = extract_method(support, "internal void DestroyModeEShellRuntimeState()")
    if "UnityEngine.Object.DestroyImmediate(shop.gameObject)" not in runtime_destroy:
        return fail("runtime destroy must make owned shops Unity-invalid before clearing tombstones")
    if "bool allInvalid = true;" not in runtime_destroy:
        return fail("runtime destroy must verify all owned shops are Unity-invalid")
    clear_idx = runtime_destroy.find("modeEOwnedShopTombstones.Clear()")
    invalid_idx = runtime_destroy.find("if (allInvalid)")
    if clear_idx < 0 or invalid_idx < 0 or clear_idx < invalid_idx:
        return fail("runtime destroy may clear tombstones only after Unity-invalid verification")

    print("ModeEShellRuntimeLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
