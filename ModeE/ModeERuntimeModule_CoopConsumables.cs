using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopModeEUtilityRequest
    {
        public string Id, AssetId, Run, Epoch;
        public int Scene;
        public int Item;
    }
    [Serializable]
    internal sealed class CoopModeEUtilitySmoke
    {
        public string Id, Run;
        public int Scene;
        public Vector3 Position;
    }

    internal sealed partial class ModeERuntimeModule
    {
        private readonly Dictionary<string, CoopModeEUtilityRequest> coopUtilityRequests = new Dictionary<string, CoopModeEUtilityRequest>();
        private readonly Dictionary<string, CoopModeEPersonal> coopUtilityResults = new Dictionary<string, CoopModeEPersonal>();
        private float coopUtilityRetry;
        private IDisposable coopUtilitySmokeChannel;
        private readonly HashSet<string> coopUtilitySmokeSeen = new HashSet<string>();

        internal static bool RequestCoopRespawnItem(int item)
        {
            if (!BossRushCoop.IsClient) return false;
            ModeERuntimeModule runtime = coopCoalOwner;
            if (runtime == null || !IsCoopRespawnItem(item)) return true;
            runtime.RefreshCoopCoalSession();
            var request = new CoopModeEUtilityRequest { Id = Guid.NewGuid().ToString("N"), Item = item,
                AssetId = BossRushCoop.LocalAssetId, Run = BossRushCoop.RunId, Scene = BossRushCoop.SceneGeneration, Epoch = runtime.coopEpoch };
            runtime.coopUtilityRequests[request.Id] = request;
            runtime.coopChannel.Request("utility", text: BossRushCoopJson.ToJson(request));
            return true;
        }

        private static bool IsCoopRespawnItem(int item)
        {
            return item == RespawnItemConfig.TAUNT_SMOKE_TYPE_ID || item == RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID
                || item == RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID || item == RespawnItemConfig.ALL_KINGS_BANNER_TYPE_ID;
        }

        private void TickCoopUtilities(float deltaTime)
        {
            coopUtilityRetry -= Mathf.Max(0f, deltaTime);
            if (coopUtilityRetry > 0f) return;
            coopUtilityRetry = 1f;
            RefundCancelledCoopUtilities();
            if (!BossRushCoop.IsClient) return;
            foreach (CoopModeEUtilityRequest request in coopUtilityRequests.Values)
                if (request.AssetId == BossRushCoop.LocalAssetId && request.Run == BossRushCoop.RunId)
                    coopChannel.Request("utility", text: BossRushCoopJson.ToJson(request));
        }

        private void ExecuteCoopUtility(CoopGameplayCommand command)
        {
            CoopModeEUtilityRequest request = string.IsNullOrEmpty(command.Text) ? null : BossRushCoopJson.FromJson<CoopModeEUtilityRequest>(command.Text);
            Guid id;
            if (request == null || request.AssetId != command.AssetId || !Guid.TryParseExact(request.Id, "N", out id) || !IsCoopRespawnItem(request.Item)) return;
            string key = command.PlayerId + ":" + request.Id;
            CoopModeEPersonal result;
            if (coopUtilityResults.TryGetValue(key, out result))
            { coopPersonal.Deliver(command.PlayerId, "utility:" + request.Id, result, request.AssetId); return; }
            result = new CoopModeEPersonal { UtilityId = request.Id };
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            if (request.Run == BossRushCoop.RunId && request.Scene == BossRushCoop.SceneGeneration && request.Epoch == coopEpoch
                && modeEActive && player != null && player.IsAlive && IsCoopModeEParticipant(player))
            {
                if (request.Item == RespawnItemConfig.TAUNT_SMOKE_TYPE_ID || request.Item == RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID)
                {
                    Vector3[] flattened = GetModeEFlattenedSpawnPoints();
                    var points = new List<Vector3>();
                    bool all = request.Item == RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID;
                    for (int i = 0; i < flattened.Length; i++)
                        if (all) points.Add(flattened[i]);
                        else InsertNearestModeERespawnPoint(points, flattened[i], player.Character.transform.position, 10);
                    int count;
                    result.UtilitySucceeded = TryStartModeERespawn(points, all ? "Chaos Detonator" : "Taunt Smoke", out count);
                }
                else
                {
                    bool all = request.Item == RespawnItemConfig.ALL_KINGS_BANNER_TYPE_ID;
                    var enemies = new List<CharacterMainControl>();
                    foreach (CharacterMainControl enemy in modeEAliveEnemies)
                        if (IsValidModeEEnemyTarget(enemy) && enemy.Team != player.Character.Team
                            && (all || (enemy.transform.position - player.Character.transform.position).sqrMagnitude <= MODE_E_BOSSCALL_RANGE * MODE_E_BOSSCALL_RANGE))
                            enemies.Add(enemy);
                    result.UtilitySucceeded = enemies.Count > 0 && player.Character.mainDamageReceiver != null;
                    if (result.UtilitySucceeded)
                        ApplyCoopUtilityAggro(enemies, player.Character, all ? MODE_E_ALL_KINGS_TRACE_DISTANCE : MODE_E_BOSSCALL_TRACE_DISTANCE).Forget();
                }
            }
            coopUtilityResults[key] = result;
            if (result.UtilitySucceeded)
            {
                PlayCoopUtilitySmoke(player.Character.transform.position);
                PublishCoopUtilitySmoke(player.Character.transform.position);
            }
            coopPersonal.Deliver(command.PlayerId, "utility:" + request.Id, result, request.AssetId);
        }

        private void PublishCoopUtilitySmoke(Vector3 position)
        {
            if (!BossRushCoop.IsHost) return;
            BossRushCoop.Broadcast("ModeE.utility-smoke", new CoopModeEUtilitySmoke {
                Id = Guid.NewGuid().ToString("N"), Run = BossRushCoop.RunId, Scene = BossRushCoop.SceneGeneration, Position = position });
        }
        private void OnCoopUtilitySmoke(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            CoopModeEUtilitySmoke effect = message.Read<CoopModeEUtilitySmoke>();
            Guid id;
            if (effect == null || effect.Run != BossRushCoop.RunId || effect.Scene != BossRushCoop.SceneGeneration
                || !Guid.TryParseExact(effect.Id, "N", out id) || !coopUtilitySmokeSeen.Add(effect.Id)) return;
            PlayCoopUtilitySmoke(effect.Position);
        }
        private void PlayCoopUtilitySmoke(Vector3 position)
        {
            try
            {
                UnityEngine.Object prefab = GetModeESmokeVfxPrefab();
                if (prefab != null) UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            }
            catch (Exception e) { ModBehaviour.DevLog("[ModeE/CoopUtility] Smoke: " + e.Message); }
        }

        private async UniTaskVoid ApplyCoopUtilityAggro(List<CharacterMainControl> enemies, CharacterMainControl target, float traceDistance)
        {
            string run = BossRushCoop.RunId;
            int scene = BossRushCoop.SceneGeneration, token = CurrentModeESessionToken;
            try
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (!BossRushCoop.IsHost || !modeEActive || CurrentModeESessionToken != token
                        || BossRushCoop.RunId != run || BossRushCoop.SceneGeneration != scene || target == null
                        || target.Health == null || target.Health.IsDead) return;
                    bool woke;
                    ForceBossAggroToPlayer(enemies[i], target, traceDistance, out woke);
                    if ((i + 1) % MODE_E_AGGRO_BATCH_SIZE == 0 && i + 1 < enemies.Count)
                        await UniTask.Delay(MODE_E_AGGRO_BATCH_INTERVAL_MS);
                }
            }
            catch (Exception e) { ModBehaviour.DevLog("[ModeE/CoopUtility] " + e.Message); }
        }

        private bool ApplyCoopUtilityReceipt(CoopModeEPersonal receipt)
        {
            CoopModeEUtilityRequest request;
            if (!coopUtilityRequests.TryGetValue(receipt.UtilityId, out request)) return true;
            if (request.AssetId != BossRushCoop.LocalAssetId) return false;
            if (!receipt.UtilitySucceeded && !modeEHost.TryGiveItemToPlayerOrDrop(request.Item, GetRespawnItemName(request.Item), false)) return false;
            coopUtilityRequests.Remove(receipt.UtilityId);
            modeEHost.ShowMessage(receipt.UtilitySucceeded
                ? L10n.T("战术效果已激活。", "Tactical effect activated.")
                : L10n.T("当前无法触发战术效果，物品已返还。", "Tactical effect unavailable; item returned."));
            return true;
        }

        private static string GetRespawnItemName(int item)
        {
            if (item == RespawnItemConfig.TAUNT_SMOKE_TYPE_ID) return L10n.T("挑衅烟雾弹", "Taunt Smoke");
            if (item == RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID) return L10n.T("混沌引爆器", "Chaos Detonator");
            if (item == RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID) return L10n.T("猎王响哨", "Bosscall Whistle");
            return L10n.T("血狩烽火", "Bloodhunt Beacon");
        }

        private void CleanupCoopUtilities()
        {
            RefundCancelledCoopUtilities();
            if (coopCoalRun != BossRushCoop.RunId) coopUtilityResults.Clear();
            coopUtilityRetry = 0f;
            coopUtilitySmokeSeen.Clear();
        }

        private void RefundCancelledCoopUtilities()
        {
            var completed = new List<string>();
            foreach (CoopModeEUtilityRequest request in coopUtilityRequests.Values)
            {
                if ((BossRushCoop.IsConnected && request.Run == BossRushCoop.RunId) || request.AssetId != BossRushCoop.LocalAssetId) continue;
                if (modeEHost.TryGiveItemToPlayerOrDrop(request.Item, GetRespawnItemName(request.Item), false)) completed.Add(request.Id);
            }
            foreach (string id in completed) coopUtilityRequests.Remove(id);
        }
    }
}
