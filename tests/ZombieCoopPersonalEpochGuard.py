"""Guard frozen zombie wallets, ordered network credits and native cash commit."""
from pathlib import Path
from cs_source_util import clean_source
from ModeHOneClickFlowGuard import method_body, squeeze


def main():
    coop = clean_source(Path("ZombieMode/ZombieModeRuntimeModule_Coop.cs").read_text(encoding="utf-8-sig"))
    extraction = clean_source(Path("ZombieMode/ZombieModeRuntimeModule_Extraction.cs").read_text(encoding="utf-8-sig"))
    lifecycle = clean_source(Path("ZombieMode/ZombieModeRuntimeModule_HostLifecycle.cs").read_text(encoding="utf-8-sig"))
    errors = []
    def require(ok, label):
        if not ok: errors.append(label)
    remember = squeeze(method_body(coop, "internal void RememberCoopZombieWallet("))
    personal = squeeze(method_body(coop, "private bool ApplyCoopZombiePersonal("))
    reset = squeeze(method_body(coop, "private void ResetCoopZombieRun("))
    issued = squeeze(method_body(coop, "private void DeliverCoopZombiePoints("))
    end = squeeze(method_body(extraction, "private void CompleteZombieModeExtractionSuccess("))
    cash = squeeze(method_body(extraction, "private bool SettleZombieModeExtractionCashShell("))
    cleanup = squeeze(method_body(lifecycle, "internal void CleanupZombieModeForSceneChange("))
    require("runState.LifecyclePhase == ZombieModeLifecyclePhase.None" in remember and "Math.Abs(runState.RunId)" in remember, "closed wallet must retain the original positive run identity and ignore cleared state")
    require(reset.index("RememberCoopZombieWallet();") < reset.index("coopZombieLocalRun = runId;"), "new run must preserve old frozen wallet")
    require("coopZombieWallets.Clear()" not in coop, "new worlds must retain unpaid old wallet")
    require(cleanup.index("CleanupZombieModeRunOnlyState(reason, true);") < cleanup.index("RememberCoopZombieWallet();") < cleanup.index("ClearRuntime();"), "wallet must snapshot after insurance and before native clear")
    require("runState.LifecyclePhase != ZombieModeLifecyclePhase.None" in personal and "if (!current)" in personal, "inactive receipts must use old wallet instead of reviving current run")
    require(personal.count("if (wallet.Received < receipt.Amount) return false;") == 2, "old and current extraction must wait for every awarded network point")
    require("wallet.Received" in issued or "coopZombiePointsIssued[key]" in issued, "host must retain per frozen participant award totals")
    require("coopZombiePointsIssued.TryGetValue" in end and "Amount = issued" in end, "extraction barrier must use each participant award total independent of spent points")
    require(cash.index("EconomyManager.Add(cashGain)") < cash.index("runState.PurificationPoints = 0;") < cash.index("NotificationText.Push"), "native cash commit must clear balance before fallible notification")
    require("coopZombieStarterChosen" in remember and "CampaignObjectiveTracker.CaptureExtractionLease(CampaignContentCatalog.ModeZombie)" in remember, "zombie campaign freezes only actual paid starter participant")
    require(personal.count("CampaignObjectiveTracker.ReportExtractionLease(wallet.Campaign)") == 2 and "owner.NotifyCampaignZombieExtracted()" not in personal, "old and current zombie extraction must resolve frozen original contract")
    require("wallet.Points = 0; CampaignObjectiveTracker.ObserveExtractionLeaseWave(wallet.Campaign, wallet.MaxWave); if (!CampaignObjectiveTracker.ReportExtractionLease(wallet.Campaign)) return false; wallet.Extracted = true;" in personal, "committed cash must not repeat while original contract save retries")
    require("wallet.MaxWave = Math.Max(wallet.MaxWave, runState.CurrentWave);" in remember and personal.count("ObserveExtractionLeaseWave(wallet.Campaign, wallet.MaxWave)") == 2, "old extraction must report frozen original reached wave instead of current world")
    require("wallet.Extracted = runState.CombatPhase" not in remember, "world success cannot acknowledge unpaid personal wallet")
    require('coopZombieWalletRoom + ":" + coopZombieLocalAsset' in remember and 'BossRushCoop.RunId + ":" + BossRushCoop.LocalAssetId' in personal, "equal numeric zombie runs in different rooms must retain separate frozen wallets")
    tick = squeeze(method_body(coop, "public override void OnUpdate("))
    require('HasPendingForIdPrefix(runState.RunId + ":extract:")' in tick and 'runId + ":extract:"' in end, "new native return must wait only for its bounded extraction run")
    if errors:
        print("ZombieCoopPersonalEpochGuard: FAIL\n" + "\n".join(errors)); return 1
    print("ZombieCoopPersonalEpochGuard: PASS"); return 0


if __name__ == "__main__":
    raise SystemExit(main())
