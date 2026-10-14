"""Guard: a new Mode F world must retain each frozen personal reward epoch."""
from pathlib import Path
from cs_source_util import clean_source
from ModeHOneClickFlowGuard import method_body, squeeze


def main():
    coop = clean_source(Path("ModeF/ModeFRuntimeModule_Coop.cs").read_text(encoding="utf-8-sig"))
    extraction = clean_source(Path("ModeF/ModeFExtraction.cs").read_text(encoding="utf-8-sig"))
    rewards = clean_source(Path("ModeF/ModeFFortifications_RepairRewardsCleanup.cs").read_text(encoding="utf-8-sig"))
    errors = []
    def require(value, label):
        if not value:
            errors.append(label)
    world = squeeze(method_body(coop, "private void ApplyCoopModeF("))
    personal = squeeze(method_body(coop, "private bool ApplyCoopModeFPersonal("))
    begin = squeeze(method_body(coop, "private void BeginCoopModeF("))
    store = squeeze(method_body(coop, "private bool TryStoreCoopModeFRewards("))
    preserve = squeeze(method_body(coop, "private void PreserveCoopModeFLocalLedger("))
    buffered = squeeze(method_body(coop, "private static bool IsCoopModeFRewardBuffered("))
    payout = squeeze(method_body(extraction, "internal void OnModeFExtractionSuccess("))
    types = squeeze(method_body(rewards, "private static List<int> GetModeFKillRewardTypeIds("))
    require(world.index("PreserveCoopModeFLocalLedger();") < world.index("coopEpoch = state.Epoch;"), "new world must preserve old personal ledger before replacing epoch")
    require("coopLocalLedgers.Clear()" not in coop and "AssetId = coopLocalAsset" in preserve, "personal ledgers must retain frozen asset across worlds")
    require("ledger.AssetId != BossRushCoop.LocalAssetId" in personal and "receipt.Epoch != coopEpoch" not in personal, "old epoch receipt must route to its frozen asset ledger")
    require("receipt.Kills != ledger.Kills + 1" in personal and "ledger.PendingKill == 0" in personal, "kill order and partial grant preparation must survive replay")
    require("else if (!TryStoreCoopModeFRewards(ledger.KillRewards)) return false;" in personal, "old kill must pay supplies through its own ledger")
    require("coopModeFPrepaidBountyRewards = ledger.ExtractionMarks;" in personal and "finally { coopModeFPrepaidBountyRewards = -1; }" in personal, "current extraction must mark bounty already delivered and release that mark")
    require("coopModeFPrepaidBountyRewards >= 0 ? 0 : modeFState.PlayerBountyMarks" in payout, "native extraction must not duplicate a committed co-op bounty")
    require("coopExtractionAwaitingReceipts = false;" in begin, "new run must retire old native return intent")
    tick = squeeze(method_body(coop, "private void TickCoopModeF("))
    require('!coopPersonal.HasPendingForIdPrefix("extract:" + coopEpoch + ":")' in tick, "native return must wait only for current extraction epoch")
    require("if (!IsCoopModeFRewardBuffered(debt.Data)) PlayerStorageBuffer.Buffer.Add(debt.Data);" in store and store.index("PlayerStorageBuffer.SaveBuffer();") < store.index("debt.Delivered = true;"), "buffer and save failure must retain unpaid tree debt without duplicate insertion")
    require("IsCoopModeFLocalAdmitted()" in preserve and "CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeModeF)" in preserve, "campaign lease must freeze only admitted original asset")
    require(personal.index("ObserveExtractionLeaseKill(ledger.Campaign, receipt.Kills, receipt.VictimMarks > 0)") < personal.index("ledger.PendingKill = receipt.Kills;"), "late authentic kill must observe original campaign before item/growth ledger commit")
    require("CampaignObjectiveTracker.ReportExtractionLease(ledger.Campaign)" in personal and personal.index("TryStoreCoopModeFRewards(ledger.ExtractionRewards)") < personal.index("ReportExtractionLease(ledger.Campaign)") < personal.index("ledger.Extracted = true;"), "frozen original campaign must be retried after reward commit and before extracted tombstone")
    require("if (coopModeFPrepaidBountyRewards < 0) owner.NotifyCampaignModeFExtracted();" in payout, "co-op current extraction must not report to newly armed native contract")
    require("buffered.rootInstanceID == data.rootInstanceID && buffered.RootTypeID == data.RootTypeID" in buffered, "slot buffer reload must recognize the original native tree identity")
    for token in ("FoldableCoverPackConfig.TYPE_ID", "killCount % 3 == 0", "EmergencyRepairSprayConfig.TYPE_ID", "killCount % 10 == 0", "ReinforcedRoadblockPackConfig.TYPE_ID", "killCount % 20 == 0", "BarbedWirePackConfig.TYPE_ID"):
        require(token in types, "original kill reward ladder must retain " + token)
    if errors:
        print("ModeFPersonalEpochGuard: FAIL\n" + "\n".join(errors))
        return 1
    print("ModeFPersonalEpochGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
