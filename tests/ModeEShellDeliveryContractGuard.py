"""Guard Mode E shell delivery: amount==1, debit-only precommit, buffer retirement, no post-commit refund."""

from pathlib import Path
import sys


SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")


def fail(message: str) -> int:
    print("ModeEShellDeliveryContractGuard: FAIL - " + message)
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
    buy = extract_method(support, "internal async UniTask<bool> BuyModeEShellItemAsync(")
    if not buy:
        return fail("missing BuyModeEShellItemAsync")

    required = [
        "if (amount != 1) return false;",
        "IsCurrentModeEShellUiBinding(shop, uiBindingID)",
        "TryAcquireModeEShellTransaction(shop, false, out owner)",
        "TryDebitModeEShell(shellPrice, owner.TransactionID)",
        "MarkModeEShellTransactionCommitted(owner.TransactionID)",
        "ItemUtilities.SendToPlayerCharacterInventory(deliveryItem, false)",
        "HandleCommittedModeEShellDeliveryRemainder(",
        "TryGetModeEShellIncomingBufferCount(out incomingBufferStart)",
        "incomingBufferStart = -1;",
        "RefundIfDebited(owner.TransactionID)",
        "ClearBusyAndReleaseModeEShellTransactionIfOwned(owner, \"Buy finally\")",
        "NormalizeModeEShellStackForShop(shop, deliveryItem)",
        'deliveryItem.FromInfoKey = "UI_Trade"',
        "int expectedStackCount = deliveryItem.StackCount;",
        "FailModeEShellEconomyDuringCommittedTransaction(",
    ]
    for token in required:
        if token not in buy:
            return fail("buy delivery missing -> " + token)

    # amount != 1 must exit before lock / debit / stock.
    amount_idx = buy.index("if (amount != 1)")
    acquire_idx = buy.index("TryAcquireModeEShellTransaction")
    debit_idx = buy.index("TryDebitModeEShell")
    if not (amount_idx < acquire_idx < debit_idx):
        return fail("amount==1 and acquire must precede debit")

    acquire = extract_method(support, "private bool TryAcquireModeEShellTransaction(")
    for token in [
        "bool isSellAll",
        "IsSellAll = isSellAll",
        "OwnsBuying = false",
        "OwnsSelling = false",
    ]:
        if token not in acquire:
            return fail("transaction owner signature drift -> " + token)

    # No CurrentStock setter / stock mutation before commit.
    pre_commit = buy[: buy.index("MarkModeEShellTransactionCommitted")]
    if "CurrentStock =" in pre_commit or "TryWriteModeEShellCurrentStock" in pre_commit:
        return fail("must not mutate stock before commit")

    remainder = extract_method(support, "private bool HandleCommittedModeEShellDeliveryRemainder(")
    if not remainder:
        return fail("missing committed delivery remainder helper")
    for token in [
        "TryFindModeEShellIncomingBufferCommit",
        "RetireCommittedModeEShellSourceItemNoThrow",
        "SaveCharacter",
        "IsModeEShellItemAssigned",
    ]:
        if token not in remainder and token not in support:
            return fail("delivery remainder missing -> " + token)

    if "RetireCommittedModeEShellSourceItemNoThrow" not in support:
        return fail("committed source must be retired no-throw before observers")
    if "VerifyModeEShellRetiredSourceNextFrame" not in support:
        return fail("must verify source Unity-invalid next frame")
    for token in [
        "if (startIndex < 0) return false;",
        "if (matchCount > 1)",
        "out bool ambiguous",
        "Incoming Buffer snapshot unavailable after committed delivery",
        "int expectedStackCount",
        "matched.RootData",
        "root.StackCount",
        "Incoming Buffer commit diagnostic: instance=",
    ]:
        if token not in support:
            return fail("buffer identity must fail closed -> " + token)

    if 'format.Replace("{itemDisplayName}"' in support:
        return fail("purchase notification must use the official formatter semantics")
    notification = extract_method(support, "private void PushModeEShellPurchaseNotification(")
    if "StringExtensions.Format(format, new" not in notification:
        return fail("purchase notification must use the official formatter semantics")

    catch_idx = buy.find("catch (Exception e)", buy.index("MarkModeEShellTransactionCommitted"))
    finally_idx = buy.find("finally", catch_idx)
    committed_catch = buy[catch_idx:finally_idx]
    if "if (committed)" not in committed_catch or \
            "FailModeEShellEconomyDuringCommittedTransaction" not in committed_catch:
        return fail("unexpected post-commit exceptions must disable capability before gate release")

    # Post-commit path must not refund.
    post_commit = buy[buy.index("MarkModeEShellTransactionCommitted") :]
    if "RefundIfDebited" in post_commit.split("finally")[0]:
        return fail("committed path must not refund before finally")
    finally_block = buy[buy.index("finally") :]
    if "if (!committed && debited)" not in finally_block:
        return fail("finally may refund only when not committed")

    print("ModeEShellDeliveryContractGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
