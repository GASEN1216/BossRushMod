using System;
using System.Collections.Generic;
using HarmonyLib;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>Protocol 3: world objects are locked claim tickets; the journal owns the single original tree.</summary>
    internal sealed class CoopWaylayerClaims : IDisposable
    {
        [Serializable] internal sealed class Ticket
        {
            public string Id, Host, Proof, Room, Token, Nonce;
            public int Generation;
        }
        private sealed class World
        {
            internal CoopWaylayerJournal.Commit Commit;
            internal Ticket Ticket;
            internal Item Item;
            internal CharacterMainControl Chief;
            internal Transform CorpseAnchor;
            internal object Drop;
            internal bool Creating;
            internal bool Retired;
        }
        private sealed class PendingGrant
        {
            internal CoopWaylayerJournal.Grant Record;
            internal Item Item;
            internal bool Creating;
            internal CharacterMainControl Body; internal Inventory Pack;
        }
        internal sealed class Admission : IDisposable
        {
            internal readonly Item Item;
            internal readonly Inventory Inventory;
            internal readonly Admission Previous;
            internal bool Consumed;
            internal Admission(Item item, Inventory inventory)
            { Item = item; Inventory = inventory; Previous = admission; admission = this; }
            public void Dispose() { admission = Previous; }
        }
        [ThreadStatic] private static Admission admission;
        private readonly HashSet<Inventory> lockedInventories = new HashSet<Inventory>();
        internal static CoopWaylayerClaims Current { get; private set; }
        private const string Topic = "sky.waylayer.claim", SnapshotTag = "BossRush.WaylayerClaim";
        private readonly CoopWaylayerJournal journal;
        private readonly Func<string, string> identityOf;
        private readonly CoopPersonalDelivery<CoopWaylayerJournal.Commit> delivery;
        private readonly IDisposable subscription;
        private readonly IDisposable nativeBuildScope;
        private readonly IDisposable retiredSubscription;
        private readonly Dictionary<Item, Ticket> replicas = new Dictionary<Item, Ticket>();
        private readonly HashSet<string> retired = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, World> worlds = new Dictionary<string, World>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CoopWaylayerJournal.Commit>> claimed = new Dictionary<string, List<CoopWaylayerJournal.Commit>>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingGrant> grants = new Dictionary<string, PendingGrant>(StringComparer.Ordinal);
        private readonly Dictionary<string, Ticket> requests = new Dictionary<string, Ticket>(StringComparer.Ordinal);
        private string identity, deliveryRun, deliveryAsset;
        private int deliveryGeneration = -1;
        private float timer;
        private bool disposed;

        internal CoopWaylayerClaims(CoopWaylayerJournal journal, Func<string, string> identityOf)
        {
            this.journal = journal; this.identityOf = identityOf; Current = this;
            nativeBuildScope = CoopWaylayerNativeBuildScope.Acquire();
            delivery = new CoopPersonalDelivery<CoopWaylayerJournal.Commit>("waylayer-claim", ReceiveGrant);
            subscription = BossRushCoop.Register(Topic, ReceiveClaim);
            retiredSubscription = BossRushCoop.Register(Topic + ".retired", ReceiveRetired);
            BossRushCoop.ItemSnapshotRequested += WriteTicket;
            BossRushCoop.ItemSnapshotApplied += ReadTicket;
        }
        internal static IDisposable Admit(Item item, Inventory inventory) { return new Admission(item, inventory); }
        internal static void TrackCorpse(Item item, Inventory inventory)
        {
            CoopWaylayerClaims owner = Current;
            Ticket ticket = item != null ? ParseTicket(item.GetString(CoopWaylayerJournal.ClaimLockKey)) : null;
            World world;
            if (owner != null && inventory != null && owner.CurrentTicket(ticket) && owner.worlds.TryGetValue(ticket.Id, out world)
                && ReferenceEquals(world.Item, item) && world.Ticket.Nonce == ticket.Nonce && item.InInventory == inventory)
                world.CorpseAnchor = inventory.transform;
        }
        internal static bool Locked(Item item)
        { return item != null && !string.IsNullOrEmpty(item.GetString(CoopWaylayerJournal.ClaimLockKey)); }
        internal static bool IsWorldTicket(Item item)
        { return item != null && ParseTicket(item.GetString(CoopWaylayerJournal.ClaimLockKey)) != null; }
        private static void Lock(Item item, string value)
        {
            foreach (Item child in item.GetAllChildren(true, false)) if (child != null)
            {
                child.SetString(CoopWaylayerJournal.ClaimLockKey, value);
                if (!string.IsNullOrEmpty(value) && child.InInventory != null && Current != null) Current.lockedInventories.Add(child.InInventory);
            }
        }
        // A not-yet-published independent grant may outlive its dead body. Move only
        // that exact locked tree; never migrate a refunded mixed stack from a corpse.
        internal bool RecoverOriginal(Item item, string id, string owner, string asset, CharacterMainControl previousBody, Inventory previousPack, CharacterMainControl player, Inventory pack)
        {
            if (disposed || owner != journal.Identity || asset != BossRushCoop.LocalAssetId || player == null
                || !ReferenceEquals(player, CharacterMainControl.Main) || player.Health == null || player.Health.IsDead
                || player.CharacterItem == null || player.CharacterItem.Inventory != pack || item == null
                || item.GetString(CoopWaylayerJournal.ClaimLockKey) != id || previousPack == null || item.InInventory != previousPack
                || ReferenceEquals(previousBody, null) || ReferenceEquals(previousBody, player)
                || (previousBody != null && previousBody.Health != null && !previousBody.Health.IsDead)) return false;
            try { previousPack.RemoveItem(item); }
            catch (Exception e) { ModBehaviour.DevLog("[SkyIslandWaylayer] private original recovery notification: " + e.Message); }
            if (disposed || owner != journal.Identity || asset != BossRushCoop.LocalAssetId || player == null
                || !ReferenceEquals(player, CharacterMainControl.Main) || player.Health == null || player.Health.IsDead
                || player.CharacterItem == null || player.CharacterItem.Inventory != pack || item == null || item.InInventory != null
                || item.GetString(CoopWaylayerJournal.ClaimLockKey) != id) return false;
            try { using (Admit(item, pack)) pack.AddItem(item); }
            catch (Exception e) { ModBehaviour.DevLog("[SkyIslandWaylayer] private original adoption notification: " + e.Message); }
            return item != null && item.InInventory == pack;
        }
        internal static void LockOriginal(Item item, string id) { Lock(item, id); }
        internal static void UnlockOriginal(Item item, string id)
        { if (item != null && item.GetString(CoopWaylayerJournal.ClaimLockKey) == id) Lock(item, string.Empty); }
        internal void Track(CoopWaylayerJournal.Commit commit, Item item, CharacterMainControl chief)
        {
            if (commit == null || commit.PublicationProtocol != 3 || item == null) return;
            RefreshIdentity();
            World previous;
            if (worlds.TryGetValue(commit.Id, out previous) && previous.Item != null)
            {
                if (ReferenceEquals(previous.Item, item)) return;
                Retire(previous);
                if (!previous.Retired)
                { Lock(item, commit.Id); SkyIslandInventoryTransaction.DestroyUnowned(item); return; }
            }
            if (chief != null) CoopWaylayerNativeBuildScope.RegisterTicketRoot(chief.CharacterItem);
            item.SetString(CoopWaylayerJournal.CustodyKey, commit.Id);
            var ticket = new Ticket { Id = commit.Id, Host = commit.Host, Proof = commit.Proof, Room = BossRushCoop.RunId,
                Token = BossRushCoop.SceneToken, Generation = BossRushCoop.SceneGeneration, Nonce = Guid.NewGuid().ToString("N") };
            Lock(item, BossRushCoopJson.ToJson(ticket));
            worlds[commit.Id] = new World { Commit = commit, Item = item, Chief = chief, Ticket = ticket };
        }
        // Exact destination and one AddAt mutation only: nested notifications cannot borrow a broad bypass.
        internal static bool AllowInventory(Item item, Inventory inventory, bool mutation)
        {
            if (!Locked(item)) return true;
            Admission scope = admission;
            if (scope != null && ReferenceEquals(scope.Item, item) && ReferenceEquals(scope.Inventory, inventory) && !scope.Consumed)
            { if (mutation) { scope.Consumed = true; if (Current != null) Current.lockedInventories.Add(inventory); } return true; }
            if (CoopWaylayerNativeBuildScope.AllowInventory(item, inventory, mutation))
            { if (mutation && Current != null) Current.lockedInventories.Add(inventory); return true; }
            TryClaim(item, inventory != null ? inventory.AttachedToItem : null);
            return false;
        }
        internal static bool AllowSlot(Item item, Slot slot)
        {
            if (!Locked(item)) return true;
            if (CoopWaylayerNativeBuildScope.AllowSlot(item, slot)) return true;
            TryClaim(item, slot != null ? slot.Master : null);
            return false;
        }
        internal static bool AllowPickup(CharacterItemControl control, Item item)
        {
            if (!Locked(item)) return true;
            CharacterMainControl player = CharacterMainControl.Main;
            if (control != null && player != null && control.characterMainControl == player) TryClaim(item, player.CharacterItem);
            return false;
        }
        internal static bool AllowCombine(Item target, Item incoming)
        {
            if (!Locked(target) && !Locked(incoming)) return true;
            TryClaim(incoming, target);
            return false;
        }
        internal static bool AllowMerge(Inventory inventory, Item item)
        {
            if (Locked(item)) { AllowInventory(item, inventory, false); return false; }
            // AddAndMerge loops until Combine makes progress. Reject the outer call when
            // its destination contains a locked target, rather than trapping that loop.
            CoopWaylayerClaims owner = Current;
            if (inventory != null && owner != null && owner.lockedInventories.Contains(inventory))
            {
                foreach (Item target in inventory.Content) if (Locked(target)) return false;
                owner.lockedInventories.Remove(inventory);
            }
            return true;
        }
        private static bool LocalDestination(Item item)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null || item == null) return false;
            for (Item current = item; current != null; current = current.ParentItem)
                if (ReferenceEquals(current, player.CharacterItem)) return true;
            return false;
        }
        private static void TryClaim(Item item, Item destination)
        {
            CoopWaylayerClaims owner = Current;
            if (owner == null || owner.disposed || !LocalDestination(destination) || !BossRushCoop.IsConnected || !BossRushCoop.HasBoundScene) return;
            Ticket ticket = ParseTicket(item != null ? item.GetString(CoopWaylayerJournal.ClaimLockKey) : null);
            if (!owner.CurrentTicket(ticket)) return;
            owner.requests[ticket.Id] = ticket;
            owner.SendClaim(ticket);
        }
        private bool CurrentTicket(Ticket ticket)
        {
            return ticket != null && CoopWaylayerJournal.ValidId(ticket.Id) && CoopWaylayerJournal.ValidId(ticket.Host)
                && CoopWaylayerJournal.ValidId(ticket.Proof) && CoopWaylayerJournal.ValidId(ticket.Nonce)
                && ticket.Room == BossRushCoop.RunId && ticket.Token == BossRushCoop.SceneToken
                && ticket.Generation == BossRushCoop.SceneGeneration;
        }
        private static Ticket ParseTicket(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] != '{') return null;
            try { return BossRushCoopJson.FromJson<Ticket>(value); }
            catch (Exception) { return null; }
        }
        internal static bool AllowDetach(Item item)
        {
            return !Locked(item) || !CoopWaylayerJournal.ValidId(item.GetString(CoopWaylayerJournal.ClaimLockKey));
        }
        internal static bool AllowDrop(Item item)
        {
            if (!Locked(item)) return true;
            CoopWaylayerClaims owner = Current;
            Ticket ticket = ParseTicket(item.GetString(CoopWaylayerJournal.ClaimLockKey));
            World world;
            return owner != null && BossRushCoop.IsHost && owner.CurrentTicket(ticket)
                && owner.worlds.TryGetValue(ticket.Id, out world) && ReferenceEquals(world.Item, item)
                && world.Commit.State == CoopWaylayerJournal.Success && owner.journal.Persist();
        }
        private void SendClaim(Ticket ticket)
        {
            if (BossRushCoop.IsHost) Claim(BossRushCoop.LocalPlayerId, ticket);
            else if (BossRushCoop.IsClient) BossRushCoop.SendToHost(Topic, ticket);
        }
        private void ReceiveClaim(BossRushCoopMessage message)
        { if (!disposed && BossRushCoop.IsHost && message != null) Claim(message.SenderId, message.Read<Ticket>()); }
        private void Claim(string playerId, Ticket ticket)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || !CurrentTicket(ticket) || ticket.Host != journal.Identity) return;
            World world;
            if (!worlds.TryGetValue(ticket.Id, out world) || world.Ticket == null || world.Ticket.Nonce != ticket.Nonce
                || world.Commit.Proof != ticket.Proof) return;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(playerId);
            string owner = identityOf(playerId);
            if (player == null || !player.IsAlive || player.Character == null || string.IsNullOrEmpty(player.AssetId) || !CoopWaylayerJournal.ValidId(owner)) return;
            // The native loot interaction or ground pickup must refer to this live ticket, in reach.
            Item item = world.Item;
            if (string.IsNullOrEmpty(world.Commit.ClaimId))
            {
                if (item == null || (world.Chief != null && world.Chief.Health != null && !world.Chief.Health.IsDead && item.InInventory != null)) return;
                Vector3 position;
                if (item.ActiveAgent != null) position = item.ActiveAgent.transform.position;
                else if (world.CorpseAnchor != null) position = world.CorpseAnchor.position;
                else if (world.Chief != null) position = world.Chief.transform.position;
                else return;
                if ((player.Character.transform.position - position).sqrMagnitude > 36f) return;
            }
            if (!journal.FreezeClaim(world.Commit, owner)) return;
            IndexClaim(world.Commit);
            Retire(world);
            Deliver(world.Commit, player);
            if (world.Retired) worlds.Remove(world.Commit.Id);
        }
        private void Deliver(CoopWaylayerJournal.Commit commit, BossRushCoopPlayer player)
        {
            if (player != null && player.IsConnected && identityOf(player.Id) == commit.ClaimOwner)
                delivery.Deliver(player.Id, "waylayer-claim:" + commit.ClaimId, commit, player.AssetId);
        }
        private void IndexClaim(CoopWaylayerJournal.Commit commit)
        {
            List<CoopWaylayerJournal.Commit> entries;
            if (!claimed.TryGetValue(commit.ClaimOwner, out entries)) claimed.Add(commit.ClaimOwner, entries = new List<CoopWaylayerJournal.Commit>());
            if (!entries.Contains(commit)) entries.Add(commit);
        }
        // Durable history is indexed once. Authenticated identity/asset handshakes reopen
        // delivery, while the existing personal queue owns retries and acknowledgements.
        internal void ReconcilePlayer(string playerId)
        {
            if (disposed || !BossRushCoop.IsHost || !BossRushCoop.HasBoundScene) return;
            RefreshIdentity();
            string owner = identityOf(playerId);
            List<CoopWaylayerJournal.Commit> entries;
            if (owner == null || !claimed.TryGetValue(owner, out entries) || !journal.Persist()) return;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(playerId);
            foreach (CoopWaylayerJournal.Commit commit in entries) Deliver(commit, player);
        }
        private bool ReceiveGrant(CoopWaylayerJournal.Commit commit)
        {
            if (disposed || commit == null || commit.ClaimOwner != journal.Identity) return false;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || !journal.OwnsCharacter(player.CharacterItem)) return false;
            CoopWaylayerJournal.Grant record = journal.PrepareGrant(commit);
            if (record == null) return false;
            if (journal.IsReturned(record.Id)) return true;
            PendingGrant pending;
            if (!grants.TryGetValue(record.Id, out pending)) grants.Add(record.Id, pending = new PendingGrant { Record = record });
            if (journal.IsReturned(record.Id)) return true;
            if (!pending.Creating) ApplyGrant(pending);
            return journal.IsReturned(record.Id);
        }
        private async void ApplyGrant(PendingGrant pending)
        {
            if (pending.Creating || journal.IsReturned(pending.Record.Id)) return;
            pending.Creating = true;
            string owner = journal.Identity, asset = BossRushCoop.LocalAssetId;
            CharacterMainControl player = CharacterMainControl.Main;
            try
            {
                if (player == null || player.Health == null || player.Health.IsDead || player.CharacterItem == null || !journal.OwnsCharacter(player.CharacterItem)) return;
                if (pending.Item == null) pending.Item = await ItemTreeData.InstantiateAsync(pending.Record.Tree);
                if (disposed || owner != journal.Identity || asset != BossRushCoop.LocalAssetId || !ReferenceEquals(player, CharacterMainControl.Main)
                    || player == null || player.Health == null || player.Health.IsDead
                    || !ReferenceEquals(journal.Data.Grants.Find(value => value.Id == pending.Record.Id), pending.Record)) return;
                Item item = pending.Item;
                Inventory pack = player.CharacterItem.Inventory;
                if (item == null || pack == null || !journal.OwnsCharacter(player.CharacterItem)) return;
                Lock(item, pending.Record.Id);
                item.SetString(CoopWaylayerJournal.GrantKey, pending.Record.Id);
                if (item.InInventory != null && item.InInventory != pack
                    && !RecoverOriginal(item, pending.Record.Id, owner, asset, pending.Body, pending.Pack, player, pack)) return;
                // A grant is one exact tree, never merged or dropped as a fallback.
                try { if (item.InInventory != pack) using (Admit(item, pack)) pack.AddItem(item); }
                catch (Exception e) { ModBehaviour.DevLog("[SkyIslandWaylayer] claim adoption notification: " + e.Message); }
                if (item != null && item.InInventory == pack)
                {
                    pending.Body = player; pending.Pack = pack;
                    if (disposed || owner != journal.Identity || asset != BossRushCoop.LocalAssetId || !ReferenceEquals(player, CharacterMainControl.Main)
                        || player == null || player.Health == null || player.Health.IsDead
                    || !ReferenceEquals(journal.Data.Grants.Find(value => value.Id == pending.Record.Id), pending.Record)) return;
                    // Native inventory adoption normally releases pickup agents. An external
                    // notification must not leave an owned grant with a second world agent.
                    if (item.ActiveAgent != null) item.AgentUtilities.ReleaseActiveAgent();
                    if (item.ActiveAgent != null) return;
                    if (!journal.PublishOriginal(pending.Record.Id, item)) return;
                    if (disposed || owner != journal.Identity || asset != BossRushCoop.LocalAssetId || !ReferenceEquals(player, CharacterMainControl.Main)
                        || player == null || player.Health == null || player.Health.IsDead || player.CharacterItem == null
                        || player.CharacterItem.Inventory != pack || item == null || item.InInventory != pack
                        || !ReferenceEquals(journal.Data.Grants.Find(value => value.Id == pending.Record.Id), pending.Record)) return;
                    journal.NoteReturned(pending.Record.Id);
                    Lock(item, string.Empty);
                    requests.Remove(pending.Record.CommitId);
                    grants.Remove(pending.Record.Id);
                }
            }
            catch (Exception e) { ModBehaviour.DevLog("[SkyIslandWaylayer] claim delivery pending: " + e.Message); }
            finally
            {
                pending.Creating = false;
                if (disposed || owner != journal.Identity || asset != BossRushCoop.LocalAssetId || !ReferenceEquals(player, CharacterMainControl.Main))
                { SkyIslandInventoryTransaction.DestroyUnowned(pending.Item); pending.Item = null; }
            }
        }
        private void WriteTicket(Item item, Dictionary<string, string> data)
        {
            if (item == null || data == null) return;
            string value = item.GetString(CoopWaylayerJournal.ClaimLockKey);
            if (ParseTicket(value) != null)
            {
                data[SnapshotTag] = value;
                World world;
                Ticket ticket = ParseTicket(value);
                if (BossRushCoop.IsHost && CurrentTicket(ticket) && worlds.TryGetValue(ticket.Id, out world)
                    && ReferenceEquals(world.Item, item) && world.Drop == null) world.Drop = BossRushCoopTransport.CaptureNetworkDrop(item);
            }
        }
        private void ReadTicket(Item item, IReadOnlyDictionary<string, string> data)
        {
            string value;
            if (item == null || data == null || !data.TryGetValue(SnapshotTag, out value) || string.IsNullOrEmpty(value)) return;
            Lock(item, value);
            Ticket ticket = ParseTicket(value);
            if (ticket == null) return;
            replicas[item] = ticket;
            if (item.InInventory != null) lockedInventories.Add(item.InInventory);
            if (retired.Contains(ticket.Nonce)) DestroyTicket(item);
        }
        private void ReceiveRetired(BossRushCoopMessage message)
        {
            if (disposed || message == null || !message.IsFromHost) return;
            Ticket ticket = message.Read<Ticket>();
            if (!CurrentTicket(ticket)) return;
            retired.Add(ticket.Nonce);
            foreach (KeyValuePair<Item, Ticket> pair in new List<KeyValuePair<Item, Ticket>>(replicas))
                if (pair.Value.Nonce == ticket.Nonce && pair.Value.Host == ticket.Host && pair.Value.Id == ticket.Id) DestroyTicket(pair.Key);
        }
        private static void DestroyTicket(Item item)
        {
            if (item == null || !Locked(item)) return;
            try { item.Detach(); item.DestroyTree(); }
            catch (Exception e) { ModBehaviour.DevLog("[SkyIslandWaylayer] retired claim ticket: " + e.Message); }
        }
        private void Retire(World world)
        {
            Item item = world.Item;
            if (world.Retired) return;
            if (world.Drop == null && item != null && CurrentTicket(world.Ticket)) world.Drop = BossRushCoopTransport.CaptureNetworkDrop(item);
            if (world.Drop != null && !BossRushCoopTransport.CompleteNetworkDrop(world.Drop)) return;
            if (world.Ticket != null && !BossRushCoop.Broadcast(Topic + ".retired", world.Ticket)) return;
            DestroyTicket(item); world.Retired = item == null || item.IsBeingDestroyed;
        }
        private async void RestoreWorld(World world)
        {
            world.Creating = true;
            string owner = journal.Identity, room = BossRushCoop.RunId, token = BossRushCoop.SceneToken;
            object service = BossRushCoop.NativeService;
            int generation = BossRushCoop.SceneGeneration;
            Item created = null;
            try
            {
                created = await ItemTreeData.InstantiateAsync(world.Commit.Tree);
                if (disposed || owner != journal.Identity || !BossRushCoop.IsHost || room != BossRushCoop.RunId || token != BossRushCoop.SceneToken
                    || generation != BossRushCoop.SceneGeneration || !BossRushCoop.HasBoundScene || !string.IsNullOrEmpty(world.Commit.ClaimId)
                    || !ReferenceEquals(service, BossRushCoop.NativeService) || world.Commit.State != CoopWaylayerJournal.Success
                    || !journal.Persist() || created == null || CharacterMainControl.Main == null) return;
                Track(world.Commit, created, null);
                created.Drop(CharacterMainControl.Main, true);
                created = null;
            }
            catch (Exception e) { ModBehaviour.DevLog("[SkyIslandWaylayer] original claim recovery pending: " + e.Message); }
            finally { world.Creating = false; SkyIslandInventoryTransaction.DestroyUnowned(created); }
        }
        internal void Tick(float delta)
        {
            if (disposed) return;
            Current = this;
            CoopWaylayerNativeBuildScope.EnsureInstalled();
            RefreshIdentity();
            if (BossRushCoop.IsHost && (deliveryRun != BossRushCoop.RunId || deliveryGeneration != BossRushCoop.SceneGeneration
                || deliveryAsset != BossRushCoop.LocalAssetId))
            {
                deliveryRun = BossRushCoop.RunId; deliveryGeneration = BossRushCoop.SceneGeneration; deliveryAsset = BossRushCoop.LocalAssetId;
                ReconcilePlayer(BossRushCoop.LocalPlayerId);
            }
            delivery.Tick(delta);
            timer -= Mathf.Max(0f, delta); if (timer > 0f) return; timer = 1f;
            if (grants.Count > 0) foreach (PendingGrant pending in new List<PendingGrant>(grants.Values))
                if (journal.IsReturned(pending.Record.Id)) grants.Remove(pending.Record.Id);
                else if (!pending.Creating && journal.Persist()) ApplyGrant(pending);
            if (requests.Count > 0) foreach (Ticket ticket in new List<Ticket>(requests.Values)) if (CurrentTicket(ticket)) SendClaim(ticket);
            if (worlds.Count == 0 || !BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || CharacterMainControl.Main == null) return;
            foreach (World world in new List<World>(worlds.Values))
            {
                if (world.Commit.PublicationProtocol != 3 || world.Commit.State == CoopWaylayerJournal.Failure)
                { Retire(world); worlds.Remove(world.Commit.Id); continue; }
                if (world.Commit.State != CoopWaylayerJournal.Success || !journal.Persist()) continue;
                if (!string.IsNullOrEmpty(world.Commit.ClaimId))
                {
                    // Claim durability is rechecked before every first/recovered delivery.
                    Retire(world);
                    IndexClaim(world.Commit);
                    foreach (BossRushCoopPlayer player in BossRushCoop.Players) Deliver(world.Commit, player);
                    if (world.Retired) worlds.Remove(world.Commit.Id);
                }
                else if (!world.Creating && (world.Item == null || !CurrentTicket(world.Ticket)))
                {
                    Retire(world);
                    if (world.Retired) RestoreWorld(world);
                }
                else if (world.Item != null && world.Chief == null && world.CorpseAnchor == null && world.Item.ActiveAgent == null)
                {
                    try { world.Item.Drop(CharacterMainControl.Main, true); }
                    catch (Exception e) { ModBehaviour.DevLog("[SkyIslandWaylayer] recovered ticket drop pending: " + e.Message); }
                }
            }
        }
        private void RefreshIdentity()
        {
            if (identity != journal.Identity)
            {
                foreach (PendingGrant previous in grants.Values) SkyIslandInventoryTransaction.DestroyUnowned(previous.Item);
                identity = journal.Identity; deliveryRun = deliveryAsset = null; deliveryGeneration = -1; worlds.Clear(); claimed.Clear(); grants.Clear(); requests.Clear(); replicas.Clear(); retired.Clear(); lockedInventories.Clear();
                foreach (CoopWaylayerJournal.Grant grant in journal.Data.Grants)
                    if (!journal.IsReturned(grant.Id)) grants[grant.Id] = new PendingGrant { Record = grant };
                foreach (CoopWaylayerJournal.Commit commit in journal.Data.Commits)
                    if (commit.PublicationProtocol == 3 && commit.State == CoopWaylayerJournal.Success)
                    {
                        if (string.IsNullOrEmpty(commit.ClaimId)) worlds[commit.Id] = new World { Commit = commit };
                        else IndexClaim(commit);
                    }
            }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            subscription.Dispose(); retiredSubscription.Dispose(); delivery.Dispose();
            nativeBuildScope.Dispose();
            BossRushCoop.ItemSnapshotRequested -= WriteTicket;
            BossRushCoop.ItemSnapshotApplied -= ReadTicket;
            foreach (PendingGrant pending in grants.Values) SkyIslandInventoryTransaction.DestroyUnowned(pending.Item);
            foreach (World world in worlds.Values) { Retire(world); DestroyTicket(world.Item); }
            foreach (Item replica in replicas.Keys) DestroyTicket(replica);
            worlds.Clear(); claimed.Clear(); grants.Clear(); requests.Clear(); replicas.Clear(); retired.Clear(); lockedInventories.Clear();
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
    [HarmonyPatch(typeof(Inventory), "AddAt", new Type[] { typeof(Item), typeof(int) })]
    internal static class CoopWaylayerClaimAddAtHook
    {
        [HarmonyPrefix] private static bool Prefix(Inventory __instance, Item item, ref bool __result)
        { if (CoopWaylayerClaims.AllowInventory(item, __instance, true)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(CharacterItemControl), "PickupItem", new Type[] { typeof(Item) })]
    internal static class CoopWaylayerClaimPickupHook
    {
        [HarmonyPrefix] private static bool Prefix(CharacterItemControl __instance, Item item, ref bool __result)
        { if (CoopWaylayerClaims.AllowPickup(__instance, item)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(Inventory), "AddItem", new Type[] { typeof(Item) })]
    internal static class CoopWaylayerClaimAddItemHook
    {
        [HarmonyPrefix] private static bool Prefix(Inventory __instance, Item item, ref bool __result)
        { if (CoopWaylayerClaims.AllowInventory(item, __instance, false)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(Slot), "Plug")]
    internal static class CoopWaylayerClaimPlugHook
    {
        [HarmonyPrefix] private static bool Prefix(Slot __instance, Item otherItem, ref Item unpluggedItem, ref bool __result)
        { if (CoopWaylayerClaims.AllowSlot(otherItem, __instance)) return true; unpluggedItem = null; __result = false; return false; }
    }
    [HarmonyPatch(typeof(Item), "Combine", new Type[] { typeof(Item) })]
    internal static class CoopWaylayerClaimCombineHook
    {
        [HarmonyPrefix] private static bool Prefix(Item __instance, Item incomingItem)
        { return CoopWaylayerClaims.AllowCombine(__instance, incomingItem); }
    }
    [HarmonyPatch(typeof(ItemUtilities), "AddAndMerge", new Type[] { typeof(Inventory), typeof(Item), typeof(int) })]
    internal static class CoopWaylayerClaimMergeHook
    {
        [HarmonyPrefix] private static bool Prefix(Inventory inventory, Item item, ref bool __result)
        { if (CoopWaylayerClaims.AllowMerge(inventory, item)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(Item), "Detach")]
    internal static class CoopWaylayerClaimDetachHook
    {
        [HarmonyPrefix] private static bool Prefix(Item __instance) { return CoopWaylayerClaims.AllowDetach(__instance); }
    }
    [HarmonyPatch(typeof(ItemExtensions), "Drop", new Type[] { typeof(Item), typeof(Vector3), typeof(bool), typeof(Vector3), typeof(float) })]
    internal static class CoopWaylayerClaimDropHook
    {
        [HarmonyPrefix] private static bool Prefix(Item item) { return CoopWaylayerClaims.AllowDrop(item); }
    }
}
