"""Protocol3 originals remain journal-owned until a unique native claimant adopts one grant."""
from CoopWaylayerJournalGuard import read, need, body


def main():
    journal = read("SkyIsland/CoopWaylayerJournal.cs")
    claims = read("SkyIsland/CoopWaylayerClaims.cs")
    escrow = read("SkyIsland/SkyIslandWaylayerChief.cs")
    need(journal, "private static Document Decode(", "if (value.Grants == null) value.Grants = new List<Grant>()", "commit.PublicationProtocol != 3", "grant.Owner != value.Identity")
    freeze = need(journal, "internal bool FreezeClaim(", "value.PublicationProtocol != 3", "value.State != Success", "if (string.IsNullOrEmpty(value.ClaimId))", "value.ClaimOwner = owner", "value.ClaimOwner == owner && Persist()")
    assert freeze.index("if (string.IsNullOrEmpty(value.ClaimId))") < freeze.index("value.ClaimOwner = owner"), "an existing claimant cannot be reassigned"
    need(journal, "internal Grant PrepareGrant(", "value.ClaimOwner != Identity", "Data.Grants.Find(item => item.Id == value.ClaimId)", "grant.Proof != value.ClaimProof", "return Persist() ? grant : null")
    need(journal, "internal ItemTreeData PrepareLoad(", "foreach (Grant grant in value.Grants)", "Contains(Get(tree.RootData, ReturnedKey), grant.Id)", "Get(entry, GrantKey) == grant.Id", "if (proof || owned != null) returned.Add(grant.Id)", "Get(entry, ClaimLockKey) == grant.Id")
    publication = need(journal, "internal bool PublishOriginal(", 'Saves.ItemSavesUtilities.Save(body.CharacterItem, "MainCharacterItemData")', 'BossRushSaveFileThrottle.RunSaveFile(() => Saves.SavesSystem.SaveFile(false))', 'location = ES3.Location.File', 'ES3.Load<ItemTreeData>("Item/MainCharacterItemData", path, settings)', '== expected', '!BossRushSaveFileThrottle.TryBeginSaveFile(false)', 'return confirmed && CurrentOriginal(owner, asset, slot, path, body, character, original)')
    need(journal, "private bool CurrentOriginal(", "owner == Identity", "slot == Saves.SavesSystem.CurrentSlot", "path == Saves.SavesSystem.CurrentFilePath", "asset == BossRushCoop.LocalAssetId", "ReferenceEquals(body, CharacterMainControl.Main)", "!body.Health.IsDead", "ReferenceEquals(character, body.CharacterItem)", "original.InInventory == character.Inventory")
    assert "SaveMainCharacter" not in publication and "CollectSaveData" not in publication, "PC06 publication cannot write Health, warehouse or collect unrelated systems"
    need(journal, "private bool CurrentReturn(", "proof.Item.InInventory == character.Inventory", "proof.Item.StackCount == proof.Count", "proof.Asset == BossRushCoop.LocalAssetId", "ReferenceEquals(proof.Body, CharacterMainControl.Main)")
    need(journal, "internal bool OwnsCharacter(", "if (!ReferenceEquals(boundCharacter, current))", "Contains(proof, grant.Id)")
    need(claims, "internal static bool AllowInventory(", "ReferenceEquals(scope.Item, item)", "ReferenceEquals(scope.Inventory, inventory)", "!scope.Consumed", "scope.Consumed = true", "return false")
    need(claims, "internal static bool AllowPickup(", "control.characterMainControl == player", "TryClaim(item, player.CharacterItem)", "return false")
    need(claims, "internal static bool AllowSlot(", "if (!Locked(item)) return true", "return false")
    need(claims, "internal static bool AllowCombine(", "!Locked(target) && !Locked(incoming)", "return false")
    need(claims, "internal static bool AllowMerge(", "if (Locked(item))", "if (Locked(target)) return false")
    need(claims, "internal static bool AllowDrop(", "BossRushCoop.IsHost", "world.Commit.State == CoopWaylayerJournal.Success && owner.journal.Persist()")
    need(claims, "private static Ticket ParseTicket(", "value[0] != '{'", "catch (Exception) { return null; }")
    claim = need(claims, "private void Claim(", "ticket.Host != journal.Identity", "world.Ticket.Nonce != ticket.Nonce", "identityOf(playerId)", "player.Character == null", "if (!journal.FreezeClaim(world.Commit, owner)) return", "Retire(world)", "Deliver(world.Commit, player)")
    assert claim.index("journal.FreezeClaim(") < claim.index("Deliver(world.Commit, player)"), "personal original requires a physically confirmed unique claim"
    assert "item.transform.position" not in claim, "corpse distance uses authoritative container/character anchor"
    need(claims, "internal static void TrackCorpse(", "ReferenceEquals(world.Item, item)", "world.Ticket.Nonce == ticket.Nonce", "item.InInventory == inventory", "inventory.transform")
    apply = need(claims, "private async void ApplyGrant(", "await ItemTreeData.InstantiateAsync(pending.Record.Tree)", "asset != BossRushCoop.LocalAssetId", "ReferenceEquals(player, CharacterMainControl.Main)", "item.SetString(CoopWaylayerJournal.GrantKey, pending.Record.Id)", "using (Admit(item, pack)) pack.AddItem(item)", "item.InInventory == pack", "ReleaseActiveAgent()", "if (!journal.PublishOriginal(pending.Record.Id, item)) return", "journal.NoteReturned(pending.Record.Id)", "Lock(item, string.Empty)")
    assert apply.rindex("player.Health.IsDead") > apply.index("journal.PublishOriginal("), "native readback cannot unlock a dead or changed claimant"
    assert apply.index("journal.PublishOriginal(") < apply.index("journal.NoteReturned(") < apply.index("Lock(item, string.Empty)"), "grant cannot unlock or ACK before actual native file proof"
    need(escrow, "private async void Refund(", "CoopWaylayerClaims.LockOriginal", "journal.ObserveReturn(", "entry.RefundApplied", "!journal.PublishOriginal(entry.Response.Id, original)", "CoopWaylayerClaims.UnlockOriginal(original, entry.Response.Id)", "original.InInventory != pack", "ReferenceEquals(player, CharacterMainControl.Main)")
    recover = need(claims, "internal bool RecoverOriginal(", "owner != journal.Identity", "asset != BossRushCoop.LocalAssetId", "ReferenceEquals(previousBody, player)", "previousBody.Health.IsDead", "previousPack.RemoveItem(item)", "using (Admit(item, pack)) pack.AddItem(item)", "item.InInventory != previousPack", "item.InInventory != null")
    assert recover.index("previousPack.RemoveItem(item)") < recover.index("using (Admit(item, pack))"), "only the same detached private tree may migrate from a dead body"
    need(escrow, "private async void Refund(", "ReferenceEquals(original, entry.Whole)", "claims.RecoverOriginal(")
    track = need(claims, "internal void Track(", "ReferenceEquals(previous.Item, item)", "Retire(previous)", "if (!previous.Retired)", "worlds[commit.Id] = new World")
    assert track.index("Retire(previous)") < track.index("worlds[commit.Id] = new World"), "replacement track must retire old physical world and replicas"
    need(claims, "private static void Lock(", "Current.lockedInventories.Add(child.InInventory)")
    assert apply.index("item.SetString(CoopWaylayerJournal.GrantKey") < apply.index("pack.AddItem(item)") < apply.index("journal.NoteReturned"), "native notification save must already contain adoption proof"
    need(claims, "private async void RestoreWorld(", "world.Commit.State != CoopWaylayerJournal.Success", "!journal.Persist()", "ReferenceEquals(service, BossRushCoop.NativeService)")
    need(claims, "internal void ReconcilePlayer(", "identityOf(playerId)", "claimed.TryGetValue(owner, out entries)", "!journal.Persist()", "Deliver(commit, player)")
    need(claims, "private void RefreshIdentity(", "if (string.IsNullOrEmpty(commit.ClaimId)) worlds[commit.Id]", "else IndexClaim(commit)", "if (!journal.IsReturned(grant.Id))", "claimed.Clear()")
    need(escrow, "private void OnIdentity(", "player.AssetId != value.Asset", "claims.ReconcilePlayer(player.Id)")
    need(claims, "internal void Track(", "CoopWaylayerNativeBuildScope.RegisterTicketRoot(chief.CharacterItem)")
    after_await = apply[apply.index("await ItemTreeData.InstantiateAsync"):]
    assert after_await.count("player.Health.IsDead") == 3, "async grant must recheck native life before adoption, after notifications and physical readback"
    need(claims, "internal void Tick(", "world.Commit.PublicationProtocol != 3", "world.Commit.State != CoopWaylayerJournal.Success || !journal.Persist()")
    need(claims, "public void Dispose()\n        {\n            if (disposed)", "retiredSubscription.Dispose()", "ItemSnapshotRequested -= WriteTicket", "ItemSnapshotApplied -= ReadTicket", "lockedInventories.Clear()", "ReferenceEquals(Current, this)")
    assert "static readonly HashSet<Inventory>" not in claims, "inventory references belong to one disposable runtime owner"
    need(escrow, "private string ClaimIdentity(", "owner.Asset == player.AssetId", "other.IsConnected", "if (otherOwner == result) return null")
    need(escrow, "internal bool AcceptCoopSnatch(", "using (CoopWaylayerClaims.Admit(item, bag))")
    need(escrow, "private async void ProcessResponse(", "claims.Track(commit, created, BossRushCoop.FindEntity(packet.Boss))")
    print("CoopWaylayerClaimsGuard: PASS")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("CoopWaylayerClaimsGuard: FAIL - " + str(error))
        raise SystemExit(1)
