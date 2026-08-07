"""Guard Mode E shell ledger: session balance only, no entity Cost/ItemUtilities authority."""

from pathlib import Path
import sys


SUPPORT = Path("ModeE/ModeEMerchantSupportClasses.cs")
MODE_E = Path("ModeE/ModeE.cs")
HARMONY = Path("ModeE/ModeEHarmonyPatch.cs")


def fail(message: str) -> int:
    print("ModeEShellCurrencyBoundaryGuard: FAIL - " + message)
    return 1


def main() -> int:
    support = SUPPORT.read_text(encoding="utf-8")
    mode_e = MODE_E.read_text(encoding="utf-8")
    combined = support + "\n" + mode_e + "\n" + HARMONY.read_text(encoding="utf-8")

    required = [
        "private int modeEShellBalance = 0;",
        "private bool modeEShellEconomyAvailable = false;",
        "private bool TryDebitModeEShell(int amount, long transactionID)",
        "private int CreditModeEShell(int amount, string reason)",
        "private bool RefundIfDebited(long transactionID)",
        "PublishModeEShellBalanceChanged();",
        "modeEShellDebits[transactionID] = amount;",
        "modeEShellRefundedTransactions.Add(transactionID);",
        "modeEShellCommittedTransactions.Add(transactionID);",
        'private const string MODE_E_SHELL_ITEM_NAME = "SeaShell";',
        "ItemAssetsCollection.TryGetIDByName(MODE_E_SHELL_ITEM_NAME, false)",
        "ItemMetaData.Name=",
        "localizationKey=Item_SeaShell",
        "modeEShellEconomyAvailable = contractsReady;",
        "SetModeEShellEconomyUnavailable(",
    ]
    for token in required:
        if token not in combined:
            return fail("missing shell ledger invariant -> " + token)

    if 'TryGetIDByName("Item_SeaShell"' in combined:
        return fail("Item_SeaShell is a localization key; runtime lookup must use metadata name SeaShell")

    forbidden = [
        "new Cost((modeEShellItemTypeID",
        "new Cost((seaShell",
        "ItemUtilities.GetItemCount(modeEShellItemTypeID)",
        "ItemUtilities.GetItemCount(seaShell",
        "ItemUtilities.ConsumeItems(modeEShellItemTypeID",
        "ItemUtilities.ConsumeItems(seaShell",
    ]
    for token in forbidden:
        if token in combined:
            return fail("forbidden entity-shell balance authority -> " + token)

    debit = support[support.index("private bool TryDebitModeEShell") : support.index("private int CreditModeEShell")]
    if "PublishModeEShellBalanceChanged();" not in debit:
        return fail("debit must publish balance after ledger commit")
    if "modeEShellBalance = (int)next;" not in debit:
        return fail("debit must commit ledger before publish")
    if debit.index("modeEShellBalance = (int)next;") > debit.index("PublishModeEShellBalanceChanged();"):
        return fail("debit must commit ledger before publish")

    print("ModeEShellCurrencyBoundaryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
