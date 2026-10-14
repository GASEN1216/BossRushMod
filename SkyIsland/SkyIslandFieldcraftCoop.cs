using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class SkyIslandFieldcraft
    {
        internal bool QueueCoopGnatUse(SkyIslandFieldBuff buff, ItemStatsSystem.Item item)
        { return gnats != null && gnats.QueueCoopUse(buff, item); }
        internal void TickCoopWorld()
        {
            if (!disposed && !session.IsReady && session.IsWorldReady && gnats != null)
                gnats.Frame(Time.time, Time.deltaTime, BossRushCoop.GetCombatTarget(root.position));
        }
        [Serializable] private sealed class GatherRequest { public string Id, TravelId, AssetId; }
        [Serializable] private sealed class GatherState { public string TravelId; public string[] Claimed; }
        private readonly Dictionary<string, string> coopGatherClaims = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, GatherRequest> coopGatherReceipts = new Dictionary<string, GatherRequest>(StringComparer.Ordinal);
        private CoopPersonalDelivery<GatherRequest> coopHarvestDelivery;
        private IDisposable coopGatherRequests, coopGatherStates;
        private bool applyingCoopHarvest;
        private float nextCoopGatherState, nextCoopGatherRequest;

        private void SubscribeCoopHarvest()
        {
            coopHarvestDelivery = new CoopPersonalDelivery<GatherRequest>("sky.gather", ApplyCoopHarvest);
            coopGatherRequests = BossRushCoop.Register("sky.gather.request", OnCoopGatherRequest);
            coopGatherStates = BossRushCoop.Register("sky.gather.state", OnCoopGatherState);
        }
        private string TravelId { get { return SkyIslandCoopRuntime.Current != null ? SkyIslandCoopRuntime.Current.TravelId : null; } }
        private void RequestCoopHarvest(string id)
        {
            if (Time.unscaledTime < nextCoopGatherRequest) return;
            nextCoopGatherRequest = Time.unscaledTime + 1f;
            var request = new GatherRequest { Id = id, TravelId = TravelId, AssetId = BossRushCoop.LocalAssetId };
            if (BossRushCoop.IsClient) BossRushCoop.SendToHost("sky.gather.request", request);
            else AdmitHarvest(BossRushCoop.LocalPlayerId, request);
        }
        private void OnCoopGatherRequest(BossRushCoopMessage message)
        {
            if (BossRushCoop.IsHost && session.IsWorldReady) AdmitHarvest(message.SenderId, message.Read<GatherRequest>());
        }
        private void AdmitHarvest(string playerId, GatherRequest request)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(playerId);
            Vector3 position;
            if (request == null || request.TravelId != TravelId || player == null || !player.IsAlive
                || string.IsNullOrEmpty(request.AssetId) || request.AssetId != player.AssetId
                || !gathering.TryGetPosition(request.Id, out position)
                || (player.Character.transform.position - position).sqrMagnitude > 36f) return;
            string claimant;
            if (coopGatherClaims.TryGetValue(request.Id, out claimant)) return;
            // 一处只产生一张个人许可；持有者写屏障期间递送器重发，其他玩家不能重复领。
            coopGatherClaims.Add(request.Id, playerId);
            coopGatherReceipts.Add(request.Id, request);
            gathering.MarkHarvested(request.Id);
            coopHarvestDelivery.Deliver(playerId, TravelId + ":" + request.Id, request, request.AssetId);
            nextCoopGatherState = 0f;
        }
        private bool ApplyCoopHarvest(GatherRequest request)
        {
            if (request == null || request.AssetId != BossRushCoop.LocalAssetId || request.TravelId != TravelId || disposed || !session.IsReady) return false;
            SkyIslandGatherNode node = null;
            foreach (SkyIslandGatherNode candidate in SkyIslandFieldcraftRules.Nodes)
                if (candidate.Id == request.Id) { node = candidate; break; }
            if (node == null) return false;
            applyingCoopHarvest = true;
            try
            {
                if (!Harvest(node)) return false;
                gathering.MarkHarvested(node.Id);
                return true;
            }
            finally { applyingCoopHarvest = false; }
        }
        private void OnCoopGatherState(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            GatherState state = message.Read<GatherState>();
            if (state == null || state.TravelId != TravelId || state.Claimed == null || state.Claimed.Length > SkyIslandFieldcraftRules.Nodes.Length) return;
            foreach (string id in state.Claimed) gathering.MarkHarvested(id);
        }
        internal void TickCoopHarvest()
        {
            if (!BossRushCoop.IsConnected) return;
            coopHarvestDelivery.Tick(Time.unscaledDeltaTime);
            if (!BossRushCoop.IsHost || Time.unscaledTime < nextCoopGatherState) return;
            nextCoopGatherState = Time.unscaledTime + 0.5f;
            foreach (var claim in coopGatherClaims)
                coopHarvestDelivery.Deliver(claim.Value, TravelId + ":" + claim.Key, coopGatherReceipts[claim.Key], coopGatherReceipts[claim.Key].AssetId);
            var ids = new string[coopGatherClaims.Count]; coopGatherClaims.Keys.CopyTo(ids, 0);
            BossRushCoop.Broadcast("sky.gather.state", new GatherState { TravelId = TravelId, Claimed = ids });
        }
        private void DisposeCoopHarvest()
        {
            if (coopHarvestDelivery != null) coopHarvestDelivery.Dispose();
            if (coopGatherRequests != null) coopGatherRequests.Dispose(); if (coopGatherStates != null) coopGatherStates.Dispose();
            coopHarvestDelivery = null; coopGatherRequests = coopGatherStates = null;
            coopGatherClaims.Clear();
            coopGatherReceipts.Clear();
        }
    }
}
