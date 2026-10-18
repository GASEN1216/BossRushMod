using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopZombieDropClaim
    {
        public int Run, Generation, Outcome, Count, Remaining;
        public string Room, SceneToken, Key, Nonce, Asset;
    }

    internal sealed partial class ZombieModeRuntimeModule
    {
        private const string ZombieDropTag = "BossRush.Zombie.SharedDrop";
        private const string ZombieDropClaimTopic = "BossRush.Zombie.DropClaim";
        private const string ZombieDropResultTopic = "BossRush.Zombie.DropResult";
        private sealed class ZombieDropRecord
        {
            internal Item Item;
            internal CoopZombieDropClaim Marker, Grant;
            internal string Holder;
            internal object NativeDrop;
            internal bool Consumed;
            internal Vector3 Position;
            internal string RemainderKey;
            internal readonly Dictionary<string, ZombieDropFinalized> Finalized = new Dictionary<string, ZombieDropFinalized>(StringComparer.Ordinal);
        }
        private sealed class ZombieDropFinalized
        {
            internal string Holder;
            internal CoopZombieDropClaim Result;
        }
        private readonly Dictionary<Item, CoopZombieDropClaim> coopZombieDropMarkers = new Dictionary<Item, CoopZombieDropClaim>();
        private readonly Dictionary<string, ZombieDropRecord> coopZombieDrops = new Dictionary<string, ZombieDropRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, CoopZombieDropClaim> coopZombieDropRequests = new Dictionary<string, CoopZombieDropClaim>(StringComparer.Ordinal);
        private readonly Dictionary<string, CoopZombieDropClaim> coopZombieDropResults = new Dictionary<string, CoopZombieDropClaim>(StringComparer.Ordinal);
        private readonly Dictionary<string, CoopZombieDropClaim> coopZombieDropOutcomes = new Dictionary<string, CoopZombieDropClaim>(StringComparer.Ordinal);
        private readonly List<IDisposable> coopZombieDropSubscriptions = new List<IDisposable>();
        private CoopPersonalDelivery<CoopZombieDropClaim> coopZombieDropDelivery;
        private Harmony coopZombieDropHarmony;
        private Item coopZombieDropApplying;
        private float coopZombieDropRetry;
        private int coopZombieDropBoundRun;

        private void InitializeCoopZombieDropClaims()
        {
            if (coopZombieDropDelivery != null) return;
            coopZombieDropDelivery = new CoopPersonalDelivery<CoopZombieDropClaim>("ZombieDrops", ApplyCoopZombieDropGrant);
            coopZombieDropSubscriptions.Add(BossRushCoop.Register(ZombieDropClaimTopic, ReceiveCoopZombieDropClaim));
            coopZombieDropSubscriptions.Add(BossRushCoop.Register(ZombieDropResultTopic, ReceiveCoopZombieDropResult));
            coopZombieDropSubscriptions.Add(BossRushCoop.Register(ZombieDropResultTopic + ".ack", ReceiveCoopZombieDropResultAck));
            coopZombieDropSubscriptions.Add(BossRushCoop.Register(ZombieDropClaimTopic + ".denied", ReceiveCoopZombieDropDenied));
            BossRushCoop.ItemSnapshotRequested += WriteCoopZombieDropMarker;
            BossRushCoop.ItemSnapshotApplied += ReadCoopZombieDropMarker;
            coopZombieDropHarmony = new Harmony("BossRush.Zombie.SharedDropClaims");
            MethodInfo pickup = typeof(CharacterItemControl).GetMethod("PickupItem", new[] { typeof(Item) });
            coopZombieDropHarmony.Patch(pickup, new HarmonyMethod(typeof(ZombieModeRuntimeModule), "PrefixCoopZombieDropPickup") { priority = Priority.First });
        }

        private void TickCoopZombieDropClaims(float deltaTime)
        {
            if (coopZombieDropDelivery == null) return;
            if (!IsZombieModeRunValid(runState.RunId))
            {
                if (coopZombieDropBoundRun != 0 && (runState.RunId <= 0 || runState.IsCleaningUp)) ResetCoopZombieDropClaims(-1);
                return;
            }
            coopZombieDropDelivery.Tick(deltaTime);
            if (coopZombieDropRequests.Count == 0 && coopZombieDropResults.Count == 0) return;
            coopZombieDropRetry -= Mathf.Max(0f, deltaTime);
            if (coopZombieDropRetry > 0f) return;
            coopZombieDropRetry = 1f;
            foreach (CoopZombieDropClaim claim in new List<CoopZombieDropClaim>(coopZombieDropRequests.Values))
                if (IsCoopZombieDropCurrent(claim) && claim.Asset == BossRushCoop.LocalAssetId)
                    SendCoopZombieDropClaim(claim);
            foreach (CoopZombieDropClaim result in new List<CoopZombieDropClaim>(coopZombieDropResults.Values))
                SendCoopZombieDropResult(result);
        }

        private void ResetCoopZombieDropClaims(int keepRun = 0)
        {
            // 授权只有明确未提交才解锁；断线、换槽、超时均不能把它转给第二个人。
            if (BossRushCoop.IsHost)
                foreach (ZombieDropRecord record in coopZombieDrops.Values)
                    if (!record.Consumed)
                    {
                        object lease = record.NativeDrop;
                        if (lease == null && IsCoopZombieDropScopeCurrent(record.Marker)) lease = BossRushCoopTransport.CaptureNetworkDrop(record.Item);
                        BossRushCoopTransport.CompleteNetworkDrop(lease);
                    }
            foreach (var pair in new List<KeyValuePair<Item, CoopZombieDropClaim>>(coopZombieDropMarkers))
            {
                CoopZombieDropClaim marker = pair.Value;
                if (keepRun != 0 && pair.Key != null && marker.Room == BossRushCoop.RunId && marker.Generation == BossRushCoop.SceneGeneration
                    && marker.SceneToken == BossRushCoop.SceneToken && (keepRun > 0 ? marker.Run == keepRun : marker.Run > coopZombieDropBoundRun)) continue;
                // 换房后同一个 CLR Item 可能已被新 registry 接管；旧局不能重新取得它的退役权限。
                if (IsCoopZombieDropScopeCurrent(marker) || pair.Key == null || BossRushCoopTransport.CaptureNetworkDrop(pair.Key) == null)
                    RetireCoopZombieDropItem(pair.Key);
                coopZombieDropMarkers.Remove(pair.Key);
            }
            coopZombieDropBoundRun = Math.Max(0, keepRun);
            coopZombieDrops.Clear();
            coopZombieDropRequests.Clear();
            coopZombieDropResults.Clear();
            coopZombieDropOutcomes.Clear();
            coopZombieDropApplying = null;
            coopZombieDropRetry = 0f;
            if (coopZombieDropDelivery != null)
            {
                coopZombieDropDelivery.Dispose();
                coopZombieDropDelivery = new CoopPersonalDelivery<CoopZombieDropClaim>("ZombieDrops", ApplyCoopZombieDropGrant);
            }
        }

        private void CleanupCoopZombieDropClaims()
        {
            if (coopZombieDropDelivery != null) coopZombieDropDelivery.Dispose();
            coopZombieDropDelivery = null;
            foreach (IDisposable subscription in coopZombieDropSubscriptions) subscription.Dispose();
            coopZombieDropSubscriptions.Clear();
            BossRushCoop.ItemSnapshotRequested -= WriteCoopZombieDropMarker;
            BossRushCoop.ItemSnapshotApplied -= ReadCoopZombieDropMarker;
            if (coopZombieDropHarmony != null) coopZombieDropHarmony.UnpatchAll(coopZombieDropHarmony.Id);
            coopZombieDropHarmony = null;
            ResetCoopZombieDropClaims();
        }

        private void RegisterCoopZombieSharedDrop(int runId, Item item)
        {
            if (!BossRushCoop.IsHost || item == null || !IsZombieModeRunValid(runId)) return;
            var marker = new CoopZombieDropClaim { Run = runId, Generation = BossRushCoop.SceneGeneration,
                Room = BossRushCoop.RunId, SceneToken = BossRushCoop.SceneToken, Key = Guid.NewGuid().ToString("N") };
            coopZombieDropMarkers[item] = marker;
            coopZombieDrops.Add(marker.Key, new ZombieDropRecord { Item = item, Marker = marker });
        }

        private void WriteCoopZombieDropMarker(Item item, Dictionary<string, string> data)
        {
            CoopZombieDropClaim marker;
            if (ReferenceEquals(item, null) || data == null || !coopZombieDropMarkers.TryGetValue(item, out marker)) return;
            if (IsCoopZombieDropCurrent(marker) && item != null && item.InInventory == null && item.PluggedIntoSlot == null)
            {
                ZombieDropRecord record;
                if (BossRushCoop.IsHost && coopZombieDrops.TryGetValue(marker.Key, out record) && record.NativeDrop == null)
                    record.NativeDrop = BossRushCoopTransport.CaptureNetworkDrop(item);
                data[ZombieDropTag] = BossRushCoopJson.ToJson(marker);
            }
        }

        private void ReadCoopZombieDropMarker(Item item, IReadOnlyDictionary<string, string> data)
        {
            string json;
            if (item == null || data == null || !data.TryGetValue(ZombieDropTag, out json)) return;
            CoopZombieDropClaim marker = BossRushCoopJson.FromJson<CoopZombieDropClaim>(json);
            // 原生 ItemSpawn 与模式快照使用不同消息，先保留候选；未绑定局身份时前缀仍拒绝拾取。
            if (marker != null && marker.Run > 0 && marker.Generation >= 0 && !string.IsNullOrEmpty(marker.Room)
                && !string.IsNullOrEmpty(marker.SceneToken) && !string.IsNullOrEmpty(marker.Key) && marker.Key.Length <= 80)
                coopZombieDropMarkers[item] = marker;
        }

        private bool IsCoopZombieDropCurrent(CoopZombieDropClaim claim)
        {
            return claim != null && !string.IsNullOrEmpty(claim.Key) && claim.Key.Length <= 80
                && claim.Run == runState.RunId && claim.Generation == BossRushCoop.SceneGeneration
                && claim.Room == BossRushCoop.RunId
                && claim.SceneToken == BossRushCoop.SceneToken && BossRushCoop.IsConnected
                && IsZombieModeRunValid(claim.Run);
        }

        private static bool IsCoopZombieDropScopeCurrent(CoopZombieDropClaim marker)
        {
            return marker.Room == BossRushCoop.RunId && marker.Generation == BossRushCoop.SceneGeneration && marker.SceneToken == BossRushCoop.SceneToken;
        }

        private static bool PrefixCoopZombieDropPickup(CharacterItemControl __instance, Item item, ref bool __result)
        {
            ZombieModeRuntimeModule module = coopZombieOwner;
            CoopZombieDropClaim marker;
            // 没有本次普通共享掉落标记时只查一次字典，不扫描场景或参与者。
            if (module == null || ReferenceEquals(item, null) || !module.coopZombieDropMarkers.TryGetValue(item, out marker)
                || ReferenceEquals(module.coopZombieDropApplying, item)) return true;
            if (item == null || item.InInventory != null || item.PluggedIntoSlot != null)
            { module.coopZombieDropMarkers.Remove(item); return true; }
            __result = false;
            if (!module.IsCoopZombieDropCurrent(marker) || !BossRushCoop.HasBoundScene
                || __instance.characterMainControl != CharacterMainControl.Main || !module.IsCoopZombieLocalAssetCurrent()) return false;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead) return false;
            if (!module.coopZombieDropRequests.ContainsKey(marker.Key))
            {
                var claim = new CoopZombieDropClaim { Run = marker.Run, Generation = marker.Generation,
                    Room = marker.Room, SceneToken = marker.SceneToken, Key = marker.Key, Nonce = Guid.NewGuid().ToString("N"), Asset = BossRushCoop.LocalAssetId };
                module.coopZombieDropRequests.Add(claim.Key, claim);
                module.SendCoopZombieDropClaim(claim);
            }
            return false;
        }

        private void SendCoopZombieDropClaim(CoopZombieDropClaim claim)
        {
            if (BossRushCoop.IsHost) AcceptCoopZombieDropClaim(BossRushCoop.LocalPlayerId, claim);
            else BossRushCoop.SendToHost(ZombieDropClaimTopic, claim);
        }

        private void ReceiveCoopZombieDropClaim(BossRushCoopMessage message)
        {
            if (BossRushCoop.IsHost && message != null) AcceptCoopZombieDropClaim(message.SenderId, message.Read<CoopZombieDropClaim>());
        }

        private void AcceptCoopZombieDropClaim(string sender, CoopZombieDropClaim claim)
        {
            ZombieDropRecord record;
            if (!IsCoopZombieDropCurrent(claim) || !BossRushCoop.HasBoundScene || string.IsNullOrEmpty(claim.Nonce) || claim.Nonce.Length > 80
                || !coopZombieDrops.TryGetValue(claim.Key, out record)) return;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(sender);
            if (!IsCoopZombieParticipant(player) || !player.IsAlive || player.AssetId != claim.Asset) return;
            if (record.Consumed || record.Finalized.ContainsKey(claim.Nonce))
            { DenyCoopZombieDropClaim(sender, claim); return; }
            if (record.Grant != null)
            {
                if (record.Holder == sender && SameCoopZombieDropAttempt(record.Grant, claim)) DeliverCoopZombieDropGrant(record);
                else DenyCoopZombieDropClaim(sender, claim);
                return;
            }
            Item item = record.Item;
            if (item == null || item.InInventory != null || item.PluggedIntoSlot != null || item.ActiveAgent == null) return;
            record.NativeDrop = BossRushCoopTransport.CaptureNetworkDrop(item);
            // 没有原生 DropId 的对象不发授权，避免本地成功而世界复制体不能退役。
            if (record.NativeDrop == null) return;
            record.Position = item.ActiveAgent.transform.position;
            claim.Count = item.StackCount;
            record.Holder = sender;
            record.Grant = claim;
            DeliverCoopZombieDropGrant(record);
        }

        private void DeliverCoopZombieDropGrant(ZombieDropRecord record)
        {
            coopZombieDropDelivery.Deliver(record.Holder, record.Marker.Run + ":drop:" + record.Marker.Key + ":" + record.Grant.Nonce,
                record.Grant, record.Grant.Asset);
        }

        private bool ApplyCoopZombieDropGrant(CoopZombieDropClaim grant)
        {
            if (!IsCoopZombieDropCurrent(grant) || !IsCoopZombieLocalAssetCurrent() || grant.Asset != BossRushCoop.LocalAssetId) return true;
            CoopZombieDropClaim prior;
            if (coopZombieDropOutcomes.TryGetValue(grant.Nonce, out prior)) return true;
            CoopZombieDropClaim requested;
            if (!coopZombieDropRequests.TryGetValue(grant.Key, out requested) || !SameCoopZombieDropAttempt(requested, grant)) return true;
            if (!BossRushCoop.HasBoundScene) return false;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null) return false;
            Item item = null;
            foreach (var pair in coopZombieDropMarkers)
                if (pair.Value.Key == grant.Key && pair.Key != null) { item = pair.Key; break; }
            if (item == null) return false;
            if (player.Health.IsDead) return false;
            IDisposable networkDrop = null;
            if (BossRushCoop.IsClient)
            {
                networkDrop = BossRushCoopTransport.BeginNetworkItemDropScope();
                if (networkDrop == null) return false;
            }
            // 先记录正在提交。任何官方通知、原生 postfix 或发送异常都不能再次运行 PickupItem。
            var result = new CoopZombieDropClaim { Run = grant.Run, Generation = grant.Generation, Room = grant.Room, SceneToken = grant.SceneToken,
                Key = grant.Key, Nonce = grant.Nonce, Asset = grant.Asset, Outcome = 2 };
            coopZombieDropOutcomes.Add(grant.Nonce, result);
            coopZombieDropApplying = item;
            int initialCount = item.StackCount;
            try
            {
                bool success = player.PickupItem(item);
                result.Outcome = success || IsCoopZombieDropCommitted(item) ? 1 : 0;
                if (!success && !IsCoopZombieDropCommitted(item) && item.StackCount < initialCount)
                { result.Outcome = 3; result.Remaining = item.StackCount; }
            }
            catch (Exception)
            {
                if (IsCoopZombieDropCommitted(item)) result.Outcome = 1;
                else if (item.StackCount < initialCount) { result.Outcome = 3; result.Remaining = item.StackCount; }
            }
            finally
            {
                coopZombieDropApplying = null;
                if (networkDrop != null) networkDrop.Dispose();
            }
            coopZombieDropRequests.Remove(grant.Key);
            if (result.Outcome == 1)
            {
                if (BossRushCoop.IsClient) BossRushCoopTransport.ProtectNetworkDropPickup(item);
                coopZombieDropMarkers.Remove(item);
            }
            coopZombieDropResults[grant.Nonce] = result;
            SendCoopZombieDropResult(result);
            return true;
        }

        private static bool IsCoopZombieDropCommitted(Item item)
        {
            return item == null || item.IsBeingDestroyed || item.InInventory != null || item.PluggedIntoSlot != null;
        }

        private static void RetireCoopZombieDropItem(Item item)
        {
            if (item == null || item.InInventory != null || item.PluggedIntoSlot != null) return;
            item.AgentUtilities.ReleaseActiveAgent();
            UnityEngine.Object.Destroy(item.gameObject);
        }

        private static bool SameCoopZombieDropAttempt(CoopZombieDropClaim left, CoopZombieDropClaim right)
        {
            return left.Run == right.Run && left.Room == right.Room && left.Generation == right.Generation && left.SceneToken == right.SceneToken
                && left.Key == right.Key && left.Nonce == right.Nonce && left.Asset == right.Asset;
        }

        private void SendCoopZombieDropResult(CoopZombieDropClaim result)
        {
            if (BossRushCoop.IsHost) AcceptCoopZombieDropResult(BossRushCoop.LocalPlayerId, result);
            else BossRushCoop.SendToHost(ZombieDropResultTopic, result);
        }

        private void ReceiveCoopZombieDropResult(BossRushCoopMessage message)
        {
            if (BossRushCoop.IsHost && message != null) AcceptCoopZombieDropResult(message.SenderId, message.Read<CoopZombieDropClaim>());
        }

        private void AcceptCoopZombieDropResult(string sender, CoopZombieDropClaim result)
        {
            ZombieDropRecord record;
            if (!IsCoopZombieDropCurrent(result) || !coopZombieDrops.TryGetValue(result.Key, out record) || string.IsNullOrEmpty(result.Nonce)) return;
            ZombieDropFinalized finalized;
            if (record.Finalized.TryGetValue(result.Nonce, out finalized))
            {
                if (finalized.Holder == sender && SameCoopZombieDropAttempt(finalized.Result, result)) AcknowledgeCoopZombieDropResult(sender, result);
                return;
            }
            if (record.Holder != sender || record.Grant == null || !SameCoopZombieDropAttempt(record.Grant, result)) return;
            if (result.Outcome == 1 || result.Outcome == 3)
            {
                if (result.Outcome == 3 && (result.Remaining <= 0 || result.Remaining >= record.Grant.Count)) return;
                // 原生 postfix 对合堆后 Unity-null 的物品会早退；冻结的 DropId 仍沿原生删除路径补清。
                if (!BossRushCoopTransport.CompleteNetworkDrop(record.NativeDrop)) return;
                record.Consumed = true;
                if (result.Outcome == 3)
                {
                    if (!RestoreCoopZombieDropRemainder(record, result.Remaining)) return;
                }
                else if (!ReferenceEquals(record.Item, null)) coopZombieDropMarkers.Remove(record.Item);
            }
            else if (result.Outcome == 0)
            {
                // 仅明确 false 且物品仍在地面时才允许下一次申领。未知异常保留原持有人。
                record.Grant = null;
                record.Holder = null;
            }
            record.Finalized.Add(result.Nonce, new ZombieDropFinalized { Holder = sender, Result = result });
            AcknowledgeCoopZombieDropResult(sender, result);
        }

        private void AcknowledgeCoopZombieDropResult(string sender, CoopZombieDropClaim result)
        {
            if (sender == BossRushCoop.LocalPlayerId) coopZombieDropResults.Remove(result.Nonce);
            else BossRushCoop.SendToPlayer(sender, ZombieDropResultTopic + ".ack", result);
        }

        private bool RestoreCoopZombieDropRemainder(ZombieDropRecord record, int remaining)
        {
            Item item = record.Item;
            if (item == null || item.InInventory != null || item.PluggedIntoSlot != null || !IsZombieModeRunValid(record.Marker.Run)) return false;
            if (record.RemainderKey == null)
            {
                // 官方允许只合入一部分后返回 false；原物品只把余量重新生成一个共享 DropId。
                item.StackCount = remaining;
                RegisterCoopZombieSharedDrop(record.Marker.Run, item);
                record.RemainderKey = coopZombieDropMarkers[item].Key;
            }
            ZombieDropRecord remainder = coopZombieDrops[record.RemainderKey];
            if (remainder.NativeDrop == null)
            {
                remainder.NativeDrop = BossRushCoopTransport.CaptureNetworkDrop(item);
                if (remainder.NativeDrop == null)
                {
                    item.AgentUtilities.ReleaseActiveAgent();
                    item.Drop(record.Position, true, Vector3.forward, 18f);
                    remainder.NativeDrop = BossRushCoopTransport.CaptureNetworkDrop(item);
                }
            }
            return remainder.NativeDrop != null;
        }

        private void DenyCoopZombieDropClaim(string sender, CoopZombieDropClaim claim)
        {
            if (sender == BossRushCoop.LocalPlayerId) coopZombieDropRequests.Remove(claim.Key);
            else BossRushCoop.SendToPlayer(sender, ZombieDropClaimTopic + ".denied", claim);
        }

        private void ReceiveCoopZombieDropDenied(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || message == null || !message.IsFromHost) return;
            CoopZombieDropClaim denied = message.Read<CoopZombieDropClaim>();
            CoopZombieDropClaim pending;
            if (denied != null && coopZombieDropRequests.TryGetValue(denied.Key, out pending) && SameCoopZombieDropAttempt(pending, denied))
                coopZombieDropRequests.Remove(denied.Key);
        }

        private void ReceiveCoopZombieDropResultAck(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || message == null || !message.IsFromHost) return;
            CoopZombieDropClaim result = message.Read<CoopZombieDropClaim>();
            CoopZombieDropClaim pending;
            if (result != null && coopZombieDropResults.TryGetValue(result.Nonce, out pending) && SameCoopZombieDropAttempt(pending, result))
                coopZombieDropResults.Remove(result.Nonce);
        }
    }
}
