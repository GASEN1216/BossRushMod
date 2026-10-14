using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Duckov.UI;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopModeFFortificationState
    {
        public int Id, Type;
        public string Owner;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Health;
    }

    [Serializable]
    internal sealed class CoopModeFFortificationRequest
    {
        public string Id, Operation, AssetId, Run, Epoch;
        public int Scene;
        public int Type, Target, Item;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    internal sealed partial class ModeFRuntimeModule
    {
        private readonly Dictionary<string, CoopModeFFortificationRequest> coopFortRequests = new Dictionary<string, CoopModeFFortificationRequest>();
        private readonly Dictionary<string, CoopModeFPersonal> coopFortResults = new Dictionary<string, CoopModeFPersonal>();
        private string coopFortRun;
        private int coopFortScene = -1;
        private float coopFortRetry;
        private bool coopExtractionUiHeld;
        private readonly FieldInfo coopExtractionClock = BossRush.Common.Utils.ReflectionCache.GetField(typeof(CountDownArea),
            "timeWhenCountDownBegan", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool IsCoopFortificationOwner(ModeFFortificationMarker marker, CharacterMainControl player)
        {
            if (marker == null || player == null) return false;
            return BossRushCoop.IsConnected && !string.IsNullOrEmpty(marker.CoopOwnerId)
                ? marker.CoopOwnerId == BossRushCoop.GetPlayerId(player) : marker.OwnerCharacterId == player.GetInstanceID();
        }

        private bool RequestCoopFortification(string operation, FortificationType type, Vector3 position,
            Quaternion rotation, int target, int consumedItem)
        {
            if (!BossRushCoop.IsClient || !BossRushCoop.SessionReady || coopChannel == null || consumedItem <= 0) return false;
            RefreshCoopFortificationSession();
            var request = new CoopModeFFortificationRequest { Id = Guid.NewGuid().ToString("N"), Operation = operation,
                Type = (int)type, Position = position, Rotation = rotation, Target = target, Item = consumedItem,
                AssetId = BossRushCoop.LocalAssetId, Run = BossRushCoop.RunId, Scene = BossRushCoop.SceneGeneration, Epoch = coopEpoch };
            coopFortRequests[request.Id] = request;
            coopChannel.Request("fortification", text: BossRushCoopJson.ToJson(request));
            return true;
        }

        private void CancelFortPlacementPreviewAfterCoopRequest()
        {
            if (modeFPlacementPreview != null) UnityEngine.Object.Destroy(modeFPlacementPreview);
            modeFPlacementPreview = null; modeFPlacementActive = false; modeFPlacementItemTypeId = 0;
            modeFPlacementLastCanPlace = null; ClearFortPlacementPreviewMaterialCache();
        }

        private void TickCoopFortificationTransactions(float deltaTime)
        {
            RefreshCoopFortificationSession();
            coopFortRetry -= Mathf.Max(0f, deltaTime);
            if (coopFortRetry > 0f) return;
            coopFortRetry = 1f;
            RefundCancelledCoopFortifications();
            if (!BossRushCoop.IsClient) return;
            foreach (CoopModeFFortificationRequest request in coopFortRequests.Values)
                if (request.AssetId == BossRushCoop.LocalAssetId && request.Run == BossRushCoop.RunId)
                    coopChannel.Request("fortification", text: BossRushCoopJson.ToJson(request));
        }
        private void RefreshCoopFortificationSession()
        {
            if (coopFortRun != BossRushCoop.RunId || coopFortScene != BossRushCoop.SceneGeneration)
            {
                CleanupCoopFortificationTransactions();
                coopFortRun = BossRushCoop.RunId; coopFortScene = BossRushCoop.SceneGeneration;
            }
        }

        private void ExecuteCoopFortification(CoopGameplayCommand command)
        {
            CoopModeFFortificationRequest request = string.IsNullOrEmpty(command.Text) ? null : BossRushCoopJson.FromJson<CoopModeFFortificationRequest>(command.Text);
            Guid id;
            if (request == null || request.AssetId != command.AssetId || !Guid.TryParseExact(request.Id, "N", out id)) return;
            string key = command.PlayerId + ":" + request.Id;
            CoopModeFPersonal result;
            if (coopFortResults.TryGetValue(key, out result))
            { coopPersonal.Deliver(command.PlayerId, "fort:" + request.Id, result, request.AssetId); return; }
            result = new CoopModeFPersonal { Epoch = coopEpoch, TransactionId = request.Id };
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            CoopModeFPlayerState participant;
            if (request.Run == BossRushCoop.RunId && request.Scene == BossRushCoop.SceneGeneration && request.Epoch == coopEpoch
                && CanUseModeFortificationUtilities() && player != null && player.IsAlive && Finite(request.Position)
                && coopPlayers.TryGetValue(player.Id, out participant) && IsCoopModeFParticipant(player, participant)
                && (request.Position - player.Character.transform.position).sqrMagnitude <= 144f)
            {
                FortificationType type = (FortificationType)request.Type;
                FortDef definition = FortDef.Get(type);
                if (request.Operation == "place" && definition != null && request.Item == definition.ItemTypeId
                    && modeFState.ActiveFortifications.Count < MODEF_MAX_ACTIVE_FORTIFICATIONS && Finite(request.Rotation))
                {
                    GameObject probe = new GameObject("ModeF_CoopPlacementProbe");
                    try
                    {
                        probe.transform.SetPositionAndRotation(request.Position, request.Rotation);
                        string failure;
                        // 复用生产占地/玩家距离判定，并在房主再次确认地面，避免过期预览穿墙或悬空。
                        RaycastHit ground;
                        int mask = GetModeFFortificationPlacementRaycastFallbackMask();
                        bool grounded = Physics.Raycast(request.Position + Vector3.up * 2f, Vector3.down, out ground,
                            4f, mask, QueryTriggerInteraction.Ignore) && Mathf.Abs(ground.point.y - request.Position.y) <= 1f;
                        if (grounded && CanPlaceModeFFortification(player.Character, probe.transform, null, type, out failure))
                            result.TransactionSucceeded = PlaceModeFortification(type, request.Position, request.Rotation, explicitOwner: player.Character);
                    }
                    finally { UnityEngine.Object.Destroy(probe); }
                }
                else if (request.Operation == "repair" && request.Item == EmergencyRepairSprayConfig.TYPE_ID)
                {
                    ModeFFortificationMarker marker;
                    if (modeFState.ActiveFortifications.TryGetValue(request.Target, out marker)
                        && IsValidModeFRepairTarget(marker, player.Character, FORT_REPAIR_RANGE))
                    {
                        marker.BoundHealth.CurrentHealth = Mathf.Min(marker.MaxHealth,
                            marker.BoundHealth.CurrentHealth + marker.MaxHealth * FORT_REPAIR_PERCENT);
                        marker.LastKnownHealth = marker.BoundHealth.CurrentHealth;
                        result.TransactionSucceeded = true;
                    }
                }
            }
            coopFortResults[key] = result;
            coopPersonal.Deliver(command.PlayerId, "fort:" + request.Id, result, request.AssetId);
        }

        private bool ApplyCoopFortificationReceipt(CoopModeFPersonal receipt)
        {
            CoopModeFFortificationRequest request;
            if (!coopFortRequests.TryGetValue(receipt.TransactionId, out request)) return true;
            if (request.AssetId != BossRushCoop.LocalAssetId) return false;
            if (!receipt.TransactionSucceeded)
            {
                if (!TryGiveItemToPlayerOrDrop(request.Item, GetModeFUtilityItemDisplayName(request.Item), false)) return false;
                ShowModeFRewardBubble(L10n.T("部署或维修未成交，物品已退还", "Deployment or repair cancelled; item refunded"));
            }
            else ShowModeFRewardBubble(L10n.T(request.Operation == "repair" ? "工事已维修" : "工事已部署",
                request.Operation == "repair" ? "Fortification repaired" : "Fortification deployed"));
            coopFortRequests.Remove(receipt.TransactionId);
            return true;
        }

        private void CleanupCoopFortificationTransactions()
        {
            RefundCancelledCoopFortifications();
            if (coopFortRun != BossRushCoop.RunId) coopFortResults.Clear();
            coopFortRetry = 0f;
        }

        private void RefundCancelledCoopFortifications()
        {
            var completed = new List<string>();
            foreach (CoopModeFFortificationRequest request in coopFortRequests.Values)
            {
                if ((BossRushCoop.IsConnected && request.Run == BossRushCoop.RunId) || request.AssetId != BossRushCoop.LocalAssetId) continue;
                if (TryGiveItemToPlayerOrDrop(request.Item, GetModeFUtilityItemDisplayName(request.Item), false)) completed.Add(request.Id);
            }
            foreach (string id in completed) coopFortRequests.Remove(id);
        }

        private CoopModeFFortificationState[] CaptureCoopFortifications()
        {
            var states = new List<CoopModeFFortificationState>();
            foreach (ModeFFortificationMarker marker in modeFState.ActiveFortifications.Values)
            {
                if (marker == null || marker.IsDestroyed || marker.BoundHealth == null || marker.BoundHealth.IsDead) continue;
                states.Add(new CoopModeFFortificationState { Id = marker.FortificationId, Type = (int)marker.Type,
                    Owner = marker.CoopOwnerId, Position = marker.transform.position, Rotation = marker.transform.rotation,
                    Health = marker.BoundHealth.CurrentHealth });
            }
            return states.ToArray();
        }

        private void ApplyCoopFortifications(CoopModeFFortificationState[] states)
        {
            if (!BossRushCoop.IsClient || states == null || states.Length > MODEF_MAX_ACTIVE_FORTIFICATIONS) return;
            var present = new HashSet<int>();
            foreach (CoopModeFFortificationState state in states)
            {
                FortDef def = state != null ? FortDef.Get((FortificationType)state.Type) : null;
                if (state == null || state.Id == 0 || def == null || !Finite(state.Position) || !Finite(state.Rotation)
                    || float.IsNaN(state.Health) || state.Health <= 0f || state.Health > def.MaxHp || !present.Add(state.Id)) continue;
                ModeFFortificationMarker marker;
                if (!modeFState.ActiveFortifications.TryGetValue(state.Id, out marker) || marker == null)
                {
                    BossRushCoopPlayer master = BossRushCoop.FindPlayer(state.Owner);
                    if (!PlaceModeFortification((FortificationType)state.Type, state.Position, state.Rotation,
                        explicitOwner: master != null ? master.Character : null, networkReplica: true, networkId: state.Id)) continue;
                    marker = modeFState.ActiveFortifications[state.Id];
                }
                marker.CoopOwnerId = state.Owner;
                marker.BoundHealth.CurrentHealth = state.Health; marker.LastKnownHealth = state.Health;
            }
            var remove = new List<int>();
            foreach (var pair in modeFState.ActiveFortifications)
                if (!present.Contains(pair.Key)) { if (pair.Value != null) UnityEngine.Object.Destroy(pair.Value.gameObject); remove.Add(pair.Key); }
            foreach (int id in remove) modeFState.ActiveFortifications.Remove(id);
        }

        private void ReportCoopFortificationHit(ModeFFortificationMarker marker, DamageInfo damage)
        {
            if (!BossRushCoop.IsClient || marker == null || damage.fromCharacter != CharacterMainControl.Main
                || damage.damageValue <= 0f || float.IsNaN(damage.damageValue) || float.IsInfinity(damage.damageValue)) return;
            // 原联机 Health.Hurt 只处理 Character。无 Character 的工事在本地只播命中，房主扣一次血。
            coopChannel.Request("fortification-hit", marker.FortificationId,
                damage.damageValue.ToString("R", CultureInfo.InvariantCulture), damage.isExplosion);
        }

        private void ExecuteCoopFortificationHit(CoopGameplayCommand command)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            ModeFFortificationMarker marker; float damage;
            if (player == null || !player.IsAlive || !modeFState.ActiveFortifications.TryGetValue(command.Value, out marker)
                || marker == null || marker.BoundHealth == null || marker.IsDestroyed
                || !float.TryParse(command.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out damage)
                || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f || damage > 100000f
                || (marker.transform.position - player.Character.transform.position).sqrMagnitude > 90000f) return;
            marker.BoundHealth.Hurt(new DamageInfo(player.Character) { damageValue = damage, isExplosion = command.Flag,
                damagePoint = marker.transform.position, ignoreDifficulty = true });
        }

        private static bool Finite(Vector3 value)
        { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        private static bool Finite(Quaternion value)
        { return Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w) && value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > .01f; }
        private static bool Finite(float value)
        { return !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= 100000f; }

        private void UpdateCoopExtractionPresentation()
        {
            if (!BossRushCoop.IsConnected) return;
            if (BossRushCoop.IsClient && modeFActive && modeFState.ExtractionPointSpawned && modeFState.ActiveExtractionArea == null)
            {
                ModeExtractionPointResult result = ModeExtractionPointFactory.CreateExtractionPoint(new ModeExtractionPointRequest
                {
                    ObjectName = "ModeF_CoopExtractionPoint", Position = coopExtractionPosition,
                    CountdownSeconds = MODEF_EXTRACTION_COUNTDOWN_SECONDS, FallbackTriggerRadius = 3f,
                    IsCurrentArea = candidate => modeFActive && candidate == modeFState.ActiveExtractionArea
                });
                if (result != null) modeFState.ActiveExtractionArea = result.CountDownArea;
            }
            CountDownArea area = modeFState.ActiveExtractionArea;
            if (area == null) { coopExtractionUiHeld = false; return; }
            area.enabled = false; // 官方只识别本机 Main；共享计时由房主一处推进。
            if (coopExtractionClock != null) coopExtractionClock.SetValue(area, Time.time - Mathf.Clamp(coopExtractionElapsed, 0f, MODEF_EXTRACTION_COUNTDOWN_SECONDS));
            CharacterMainControl player = CharacterMainControl.Main;
            bool show = modeFActive && modeFState.ExtractionPointSpawned && !modeFState.ExtractionResolved
                && player != null && player.Health != null && !player.Health.IsDead
                && (player.transform.position - coopExtractionPosition).sqrMagnitude <= 9f;
            if (show && !coopExtractionUiHeld)
            { if (area.onCountDownStarted != null) area.onCountDownStarted.Invoke(area); coopExtractionUiHeld = true; }
            else if (!show && coopExtractionUiHeld)
            { if (area.onCountDownStopped != null) area.onCountDownStopped.Invoke(area); coopExtractionUiHeld = false; }
        }
    }

    [HarmonyPatch(typeof(Health), "Hurt")]
    internal static class CoopFortificationReplicaHurtPatch
    {
        private static bool Prefix(Health __instance, DamageInfo damageInfo)
        {
            if (!BossRushCoop.IsClient || __instance == null) return true;
            if (__instance.TryGetCharacter() != null) return true;
            ModeFFortificationMarker marker = __instance.GetComponent<ModeFFortificationMarker>();
            if (marker == null || !marker.NetworkReplica) return true;
            if (marker.NetworkHit != null) marker.NetworkHit(damageInfo);
            return false;
        }
    }
}
