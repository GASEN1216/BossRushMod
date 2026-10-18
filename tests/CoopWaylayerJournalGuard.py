"""Waylayer single-original durable escrow; execution tests prove effects and fault recovery."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def body(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index("{", source.index(signature))
    depth = 0
    for end in range(start, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return re.sub(r"\s+", " ", source[start + 1:end]).strip()
    raise AssertionError("unclosed " + signature)


def need(source, signature, *tokens):
    current = body(source, signature)
    for token in tokens:
        assert token in current, signature + ": " + token
    return current


def main():
    journal = read("SkyIsland/CoopWaylayerJournal.cs")
    escrow = read("SkyIsland/SkyIslandWaylayerChief.cs")
    hooks = read("SkyIsland/CoopWaylayerJournalHooks.cs")
    assert 'StorageKey = "BossRush_CoopWaylayerJournal", SchemaVersion = 1' in journal
    persist = need(journal, "internal bool Persist()", "durableSlot == slot && durableJson == expected", "store.Store(Data)", "coordinator.RequestFlush(out error, true)", "location = ES3.Location.File", "Saves.SavesSystem.CurrentSlot != slot", "if (saved && Saves.SavesSystem.CurrentSlot == slot)")
    assert "SaveMainCharacter(" not in persist and "CollectSaveData(" not in persist, "single original journal must not force character snapshot"
    load = need(journal, "internal ItemTreeData PrepareLoad(", "ItemTreeData tree = Clone(saved)", '"tree:" + fingerprint + ":" + entry.instanceID', "Get(entry, SourceKey) == debt.Source", "if (!excluded && source != null) RemoveOne(tree, source)", "Contains(Get(tree.RootData, ExcludedKey), debt.Id)", "Set(tree.RootData, CharacterOwnerKey, value.Identity)")
    assert "value.Commits.Count == 0 && !Persist()" in load, "fresh native-first owner identity must be durable before character is tagged"
    assert load.index("value.Commits.Count == 0 && !Persist()") < load.index("Set(tree.RootData, CharacterOwnerKey, value.Identity)"), "fresh identity barrier must precede owner tag"
    assert "StealScore(debt." not in load, "debt provenance cannot fall back to type matching"
    need(journal, "internal bool OwnsCharacter(", "owner == Identity", "boundCharacter.Health.IsDead", "Data.Debts.Count == 0 || respawn")
    need(journal, "internal void BeforeNativeSave(", "!OwnsCharacter(character)", "character.SetString(ExcludedKey, Join(debited, returned))", "character.SetString(ReturnedKey, Join(returned, null))")
    need(journal, "internal bool FinishCommit(", "value.State == Success || (value.PublicationStarted && value.PublicationProtocol != 2 && value.PublicationProtocol != 3)", "value.State = Custody")
    need(journal, "internal bool StartPublication(", "value.State != Custody", "value.PublicationProtocol = 3", "value.PublicationStarted = true", "return Persist()")
    need(journal, "internal void HoldUncertainCustody(", "value.PublicationProtocol = 0", "value.PublicationStarted = true", "Persist()")
    need(journal, "internal bool CancelUnpublished(", "value.State == Success", "!retracted.IsBeingDestroyed", "value.PublicationStarted = false", "value.State = Failure", "return Persist()")
    need(journal, "internal bool MarkPublished(", "!value.PublicationStarted", "value.State = Success", "return Persist()")
    need(journal, "public void Dispose()\n        {\n            if (disposed)", "store.ShutdownSubscription()", "boundCharacter = null", "Current = null")
    reserve = need(escrow, "private void Reserve(", "!journal.OwnsCharacter(player.CharacterItem)", "journal.Prepare(request.Id, request.Host, item, tree)", "item.StackCount = before - 1", "if (journal.Reserve(entry.Debt)) response.Tree = entry.Debt.Tree")
    assert reserve.index("journal.Prepare(") < reserve.index("item.StackCount = before - 1"), "original tree must be durable before debit"
    response = need(escrow, "private async void ProcessResponse(", "journal.PrepareCommit(", "await ItemTreeData.InstantiateAsync(tree)", "!disposed && CurrentWorld(entry)", "entry.Chief.AcceptCoopSnatch(created, player.Character)", "entry.Chief.SuspendCoopSnatch(created)", "journal.FinishCommit(commit, true) && journal.StartPublication(commit)", "success = terminal = journal.MarkPublished(commit)", "terminal = journal.CancelUnpublished(commit, created)", "journal.HoldUncertainCustody(commit)", "bool held = commit != null && commit.State == CoopWaylayerJournal.Success && ReferenceEquals(entry.Custody, created)", "if (!success && !held) SkyIslandInventoryTransaction.DestroyUnowned(created)")
    assert response.index("entry.Chief.AcceptCoopSnatch(") < response.index("journal.FinishCommit(commit, true)"), "success needs real bag custody first"
    assert response.index("created.DestroyTree();") < response.index("journal.CancelUnpublished(commit, created)"), "only actually retracted original may authorize prepublication cancellation"
    assert response.index("entry.Chief.SuspendCoopSnatch(created)") < response.index("journal.StartPublication(commit)"), "v2 proof requires real item be private before the publication record"
    assert response.count("if (success) success = terminal = PublishCustody(entry);") == 3, "every first publication and retry needs durable terminal confirmation"
    need(escrow, "internal bool SuspendCoopSnatch(", "item.InInventory != BossInventory()", "item.Detach()", "return !SkyIslandInventoryTransaction.HasOwner(item)")
    need(escrow, "private bool PublishCustody(", "item == null || !CurrentWorld(entry)", "!SkyIslandInventoryTransaction.HasOwner(item)", "entry.Chief.AcceptCoopSnatch(item, player.Character)", "Present(entry)")
    query = need(escrow, "private void OnQuery(", "owner.Identity != query.Owner", "query.Host != journal.Identity", "commit.Owner != query.Owner || commit.Proof != query.Proof", "commit.State == CoopWaylayerJournal.Custody && commit.PublicationStarted && commit.PublicationProtocol != 2 && commit.PublicationProtocol != 3", "if (!commit.PublicationStarted || !journal.MarkPublished(commit)) return")
    assert "message.IsFromHost" not in query, "host query authenticates native member owner; terminal response is native host only"
    need(escrow, "private void OnResult(", "!message.IsFromHost", "result.Host == hostIdentity", "result.Run == BossRushCoop.RunId")
    tick = need(escrow, "internal void Tick(", "if (value.Settled || journal.IsReturned(debt.Id)) { pendingDebts.Remove(debt.Id); continue; }", "if (!journal.Decide(debt, false)) continue", "foreach (CoopWaylayerJournal.Debt debt in pendingDebts.Values)", "BossRushCoop.IsClient && !identityConfirmed")
    assert tick.index("if (value.Settled || journal.IsReturned(debt.Id))") < tick.index("if (!journal.Decide(debt, false)) continue"), "settled refund must not physically save on every tick"
    assert "journal.Data.Debts" not in tick[tick.index("RefreshRun();"):], "periodic work must not copy historical terminal debts"
    need(escrow, "private void QueueDecision(", "entry.DecisionQueued = decisions.Deliver(", "if (entry.DecisionQueued)", "SkyIslandInventoryTransaction.DestroyUnowned(entry.Custody)", "host.Remove(entry.Request.Id)")
    need(escrow, "private void OnIdentity(", "identityConfirmed = value.AckOwner == journal.Identity && value.AckAsset == BossRushCoop.LocalAssetId", "AckOwner = value.Identity, AckAsset = value.Asset")
    need(escrow, "private bool EnsureIdentity()", "identityReady != BossRushCoop.LocalReadyEpoch", "BossRushCoop.NativeService", "BossRushCoop.NativeHostPeer", "identityConfirmed = false")
    need(escrow, "private void NotifyPending(", "entry.PendingNotified || entry.Settled || !entry.Reserved", "entry.Debt.State != CoopWaylayerJournal.Reserved", "Duckov.UI.NotificationText.Push(L10n.T(", "entry.PendingNotified = true")
    need(escrow, "private static void NotifySettlement(", "if (!entry.PendingNotified) return", "entry.PendingNotified = false")
    shutdown = body(escrow, "public void Dispose()")
    assert "Refund(" not in shutdown and "Decide(" not in shutdown, "unknown unload cannot invent refundable failure"
    assert "foreach (HostEntry entry in host.Values) SkyIslandInventoryTransaction.DestroyUnowned(entry.Custody)" in shutdown, "owner shutdown retires only unowned private custody"
    need(hooks, "private static void Prefix(string key, out Scope", 'if (key != "MainCharacterItemData") return', "__state.Temporary = new CoopWaylayerJournal()")
    need(hooks, "private static void Finalizer(", "__state.Temporary.Dispose()", "Loading = __state.Previous")
    need(hooks, "private static void Prefix(ref ItemTreeData", "CoopWaylayerLoadScopeHook.Loading", "__0 = journal.PrepareLoad(__0)")
    need(hooks, "private static void Prefix(Item item, string key)", 'key == "MainCharacterItemData"', "ReferenceEquals(player.CharacterItem, item)", "BeforeNativeSave(item)")
    assert "__result" not in hooks and "MoveNext" not in hooks, "native value task and state machine are not replaced"
    print("CoopWaylayerJournalGuard: PASS")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("CoopWaylayerJournalGuard: FAIL - " + str(error))
        raise SystemExit(1)
