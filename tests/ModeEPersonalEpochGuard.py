"""Keep co-op E permanent every-ten rewards tied to original participant epoch."""
from pathlib import Path
from cs_source_util import clean_source
from ModeHOneClickFlowGuard import method_body, squeeze


def main():
    code=clean_source(Path("ModeE/ModeERuntimeModule_Coop.cs").read_text(encoding="utf-8-sig"));errors=[]
    def require(ok,label):
        if not ok:errors.append(label)
    reward=squeeze(method_body(code,"private bool ApplyCoopModeEBossReward("))
    personal=squeeze(method_body(code,"private bool ApplyCoopModeEPersonal("))
    send=squeeze(method_body(code,"private void DeliverCoopModeEDeath("))
    store=squeeze(method_body(code,"private static bool TryStoreCoopModeEReward("))
    world=squeeze(method_body(code,"private void ApplyCoopModeE("))
    require("coopRewardLedgers.Clear()" not in code and "ledger.AssetId != BossRushCoop.LocalAssetId" in reward,"old permanent reward must retain original asset ledger")
    require("if (!receipt.Starter) return ApplyCoopModeEBossReward(receipt);" in personal,"old boss rewards must route before current world gate")
    require("receipt.BossKills != ledger.BossKills + 1" in reward and "if (ledger.PendingKill == 0)" in reward,"out-of-order and partial gift retry must not repeat growth")
    require("receipt.BossKills % 10 == 0 && !ledger.SmokeDelivered" in reward and "RespawnItemConfig.TAUNT_SMOKE_TYPE_ID" in reward,"original every-ten permanent smoke quantity must remain")
    require("modeEHost.TryGiveItemToPlayerOrDrop" in reward and "else if (!TryStoreCoopModeEReward(ledger)) return false;" in reward,"current reward retains native inventory/drop and old reward uses original buffer")
    require("participant.BossKills++" in send and "BossKills = participant.BossKills }, participant.AssetId" in send,"publisher must freeze participant asset and cumulative awarded count")
    require("data.rootInstanceID == ledger.Smoke.rootInstanceID && data.RootTypeID == ledger.Smoke.RootTypeID" in store and "if (!buffered) PlayerStorageBuffer.Buffer.Add(ledger.Smoke);" in store and "PlayerStorageBuffer.SaveBuffer();" in store,"native buffer reload/save failure must retry original tree without duplication")
    require(world.index("PreserveCoopModeERewardLedger();") < world.index("coopEpoch = state.Epoch;"),"new world must preserve prior personal ledger before replacing epoch")
    require("if (current)" in reward and "if (current && reward > 0 && modeEShellEconomyAvailable)" in reward,"old permanent awards must not credit new run growth or shell currency")
    native=clean_source(Path("ModeF/ModeFFortifications_RepairRewardsCleanup.cs").read_text(encoding="utf-8-sig"))
    give=squeeze(method_body(native,"internal bool TryGiveItemToPlayerOrDrop("))
    require("SkyIslandInventoryTransaction.TryDeliver(item, pack," in give and "if (remaining.InInventory == pack) return true;" in give and "catch" in give, "post-commit inventory notification failure must retain real native ownership")
    require("remaining.ActiveAgent == pickup" in give and "return remaining.ActiveAgent != null;" in give, "post-commit drop exception must retain exact native pickup ownership")
    require("SkyIslandInventoryTransaction.DestroyUnowned(item);" in give and "CharacterMainControl.Main != player" in give and "player.CharacterItem != root" in give and "asset != BossRushCoop.LocalAssetId" in give, "delivery must keep frozen original root and destroy only unowned source")
    require("item.DestroyTree(" not in give, "presentation failure must never destroy a committed item")
    require(give.index("if (!sent) return false;") < give.index("if (showRewardBubble)"), "reward bubble is fallible presentation after the native delivery commit")
    if errors:print("ModeEPersonalEpochGuard: FAIL\n"+"\n".join(errors));return 1
    print("ModeEPersonalEpochGuard: PASS");return 0


if __name__ == "__main__":raise SystemExit(main())
