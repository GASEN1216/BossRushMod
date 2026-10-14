using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using BossRush.Utils;

namespace BossRush
{
    internal sealed partial class CoopNpcWorld
    {
        [Serializable] internal sealed class SpouseIntent
        {
            public string NpcId, Asset, Run;
            public int Scene;
            public long Sequence;
            public bool Present, Following;
            public Vector3 Home;
        }
        [Serializable] private sealed class SpouseReply { public string Asset; public long Sequence; }
        private sealed class SpouseState
        {
            internal string PlayerId, WorldId;
            internal SpouseIntent Intent;
            internal CharacterMainControl Bound;
            internal bool Spawning, Moving;
        }
        private readonly Dictionary<string, SpouseState> spouses = new Dictionary<string, SpouseState>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> spouseSequences = new Dictionary<string, long>(StringComparer.Ordinal);
        private IDisposable followRequest, followReply;
        private SpouseIntent localSpouseIntent;
        private long localSpouseSequence;
        private float spouseRetryAt;
        private bool spouseAcknowledged;
        private bool spouseDisconnectSubscribed;
        private string spouseReleaseAsset;
        private float spouseReleaseAfter;

        internal static string SpouseWorldId(string player, string asset, string npc)
        { return "spouse:" + player.Length + ":" + player + ":" + asset.Length + ":" + asset + ":" + npc; }

        internal static GameObject FindLocalSpouse(string npc)
        {
            if (string.IsNullOrEmpty(npc) || string.IsNullOrEmpty(BossRushCoop.LocalPlayerId) || string.IsNullOrEmpty(BossRushCoop.LocalAssetId)) return null;
            return FindObject(SpouseWorldId(BossRushCoop.LocalPlayerId, BossRushCoop.LocalAssetId, npc));
        }

        private void SubscribeFollow()
        {
            followRequest = BossRushCoop.Register("npc.spouse.request", OnSpouseRequest);
            followReply = BossRushCoop.Register("npc.spouse.reply", OnSpouseReply);
            if (!spouseDisconnectSubscribed)
            { BossRushCoop.PlayerDisconnected += OnSpouseDisconnected; spouseDisconnectSubscribed = true; }
        }

        // 跟随状态属于个人婚姻；同一 NPC 在每份个人档案中有独立配偶实例。
        internal static bool ReserveLocalFollow(string id, bool following) { return true; }
        internal static bool RequestFollow(string npcId, bool following)
        {
            if (Current == null || !BossRushCoop.IsConnected) return false;
            Vector3 home;
            bool hasHome = Current.TryGetLocalSpouseHome(out home);
            return Current.SetLocalSpouseIntent(npcId, following || hasHome, following, home);
        }
        internal static void RequestLocalSpouseHome(string npcId, Vector3 home)
        { if (Current != null && BossRushCoop.IsConnected) Current.SetLocalSpouseIntent(npcId, true, false, home); }
        internal static void ReleaseLocalSpouse(string npcId)
        { if (Current != null && BossRushCoop.IsConnected) Current.SetLocalSpouseIntent(npcId, false, false, Vector3.zero); }
        internal static void DeferLocalSpouseRelease(float seconds)
        {
            if (Current == null || !BossRushCoop.IsConnected) return;
            Current.spouseReleaseAsset = BossRushCoop.LocalAssetId;
            Current.spouseReleaseAfter = Time.time + seconds;
        }

        private bool TryGetLocalSpouseHome(out Vector3 home)
        {
            home = Vector3.zero;
            return owner != null && owner.WeddingRuntime != null && owner.WeddingRuntime.TryGetCoopSpouseHome(out home);
        }

        private bool SetLocalSpouseIntent(string npc, bool present, bool following, Vector3 home)
        {
            if (string.IsNullOrEmpty(npc) || string.IsNullOrEmpty(BossRushCoop.LocalAssetId)) return false;
            RefreshCoopNpcScope();
            SpouseIntent previous = localSpouseIntent;
            if (previous == null || previous.NpcId != npc || previous.Asset != BossRushCoop.LocalAssetId
                || previous.Present != present || previous.Following != following || (previous.Home - home).sqrMagnitude > 0.01f
                || previous.Run != BossRushCoop.RunId || previous.Scene != BossRushCoop.SceneGeneration)
            {
                localSpouseIntent = new SpouseIntent { NpcId = npc, Asset = BossRushCoop.LocalAssetId, Run = BossRushCoop.RunId,
                    Scene = BossRushCoop.SceneGeneration, Sequence = ++localSpouseSequence,
                    Present = present, Following = following, Home = home };
                spouseAcknowledged = false; spouseRetryAt = 0f;
            }
            if (BossRushCoop.IsHost) { ApplySpouseIntent(BossRushCoop.LocalPlayerId, localSpouseIntent); return true; }
            if (spouseAcknowledged || Time.unscaledTime < spouseRetryAt) return true;
            spouseRetryAt = Time.unscaledTime + 1f;
            return BossRushCoop.SendToHost("npc.spouse.request", localSpouseIntent);
        }

        private void TickLocalSpouse()
        {
            string npc = AffinityManager.GetCurrentSpouseNpcId();
            if (string.IsNullOrEmpty(npc))
            {
                if (spouseReleaseAsset == BossRushCoop.LocalAssetId && Time.time < spouseReleaseAfter) return;
                if (localSpouseIntent != null && localSpouseIntent.Asset == BossRushCoop.LocalAssetId)
                    SetLocalSpouseIntent(localSpouseIntent.NpcId, false, false, Vector3.zero);
                return;
            }
            bool following = AffinityManager.IsSpouseFollowingPlayer(npc);
            if (owner != null && owner.WeddingRuntime != null) owner.WeddingRuntime.RetireOfflineSpouseForCoop(npc);
            Vector3 home;
            bool hasHome = TryGetLocalSpouseHome(out home);
            SetLocalSpouseIntent(npc, following || hasHome, following, home);
        }

        private void OnSpouseReply(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost || localSpouseIntent == null) return;
            SpouseReply reply = message.Read<SpouseReply>();
            if (reply != null && reply.Asset == BossRushCoop.LocalAssetId && reply.Asset == localSpouseIntent.Asset
                && reply.Sequence == localSpouseIntent.Sequence) spouseAcknowledged = true;
        }

        private static bool IsSpouseNpc(string npc)
        { return npc == GoblinAffinityConfig.NPC_ID || npc == NurseAffinityConfig.NPC_ID || PermanentDuckNpcRegistry.IsPermanentDuckNpc(npc); }

        private void OnSpouseRequest(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.SessionReady) return;
            RefreshCoopNpcScope();
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            SpouseIntent intent = message.Read<SpouseIntent>();
            if (player == null || !player.IsConnected || intent == null || string.IsNullOrEmpty(intent.Asset)
                || intent.Asset != player.AssetId || intent.Run != BossRushCoop.RunId || intent.Scene != BossRushCoop.SceneGeneration
                || intent.Sequence <= 0 || !IsSpouseNpc(intent.NpcId) || !FiniteSpousePosition(intent.Home)) return;
            if (intent.Present && !intent.Following && !ModBehaviour.IsBaseHubSceneName(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)) return;
            ApplySpouseIntent(player.Id, intent);
            BossRushCoop.SendToPlayer(player.Id, "npc.spouse.reply", new SpouseReply { Asset = intent.Asset, Sequence = intent.Sequence });
        }
        private static bool FiniteSpousePosition(Vector3 p)
        {
            return !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.x)
                && !float.IsInfinity(p.y) && !float.IsInfinity(p.z) && p.sqrMagnitude < 100000000f;
        }

        private void ApplySpouseIntent(string player, SpouseIntent intent)
        {
            string scope = player.Length + ":" + player + ":" + intent.Asset;
            long sequence;
            if (spouseSequences.TryGetValue(scope, out sequence) && intent.Sequence < sequence) return;
            spouseSequences[scope] = intent.Sequence;
            string key = SpouseWorldId(player, intent.Asset, intent.NpcId);
            var removed = new List<string>();
            foreach (SpouseState entry in spouses.Values)
                if (entry.PlayerId == player && entry.Intent.Asset == intent.Asset && entry.WorldId != key) removed.Add(entry.WorldId);
            foreach (string id in removed) RemoveSpouse(id);
            if (!intent.Present) { RemoveSpouse(key); return; }
            SpouseState state;
            if (spouses.TryGetValue(key, out state))
            {
                if (state.Intent.Sequence == intent.Sequence) return;
                state.Intent = intent; state.Bound = null;
                return;
            }
            spouses[key] = new SpouseState { PlayerId = player, WorldId = key, Intent = intent };
        }

        private void TickFollow()
        {
            TickLocalSpouse();
            foreach (SpouseState state in new List<SpouseState>(spouses.Values))
            {
                BossRushCoopPlayer player = BossRushCoop.FindPlayer(state.PlayerId);
                if (player == null || !player.IsConnected || player.AssetId != state.Intent.Asset) { RemoveSpouse(state.WorldId); continue; }
                Actor actor;
                if (!actors.TryGetValue(state.WorldId, out actor) || actor.Object == null)
                { if (!state.Spawning && player.Character != null) CreateWorldSpouse(state, generation).Forget(); continue; }
                bool following = state.Intent.Following && player.IsAlive;
                if (state.Bound != player.Character || state.Moving != following || actor.Following != state.Intent.Following)
                {
                    SetSpouseMovement(actor.Object, following ? player.Character.transform : null);
                    if (!state.Intent.Following) actor.Object.transform.position = state.Intent.Home;
                    state.Bound = player.Character;
                    state.Moving = following;
                    UpdateSpousePresentation(actor, new Entry { NpcId = state.Intent.NpcId, Following = state.Intent.Following });
                }
            }
        }

        private async UniTaskVoid CreateWorldSpouse(SpouseState state, int requestGeneration)
        {
            state.Spawning = true;
            GameObject created = null;
            SpouseIntent intent = state.Intent;
            try
            {
                BossRushCoopPlayer player = BossRushCoop.FindPlayer(state.PlayerId);
                if (player == null || player.Character == null) return;
                Vector3 position = intent.Following ? player.Character.transform.position + Vector3.back * 2f : intent.Home;
                created = await CreateIndependentSpouse(intent.NpcId, position, false);
                SpouseState current;
                player = BossRushCoop.FindPlayer(state.PlayerId);
                if (created == null || disposed || requestGeneration != generation || !BossRushCoop.IsHost
                    || !spouses.TryGetValue(state.WorldId, out current) || !ReferenceEquals(state, current) || state.Intent != intent
                    || player == null || !player.IsConnected || player.AssetId != intent.Asset) return;
                var actor = new Actor { Object = created, Kind = "spouse", Owner = state.PlayerId, Asset = intent.Asset, NpcId = intent.NpcId };
                actors[state.WorldId] = actor;
                state.Bound = null;
                created.name = "BossRush_CoopSpouse_" + state.WorldId;
                UpdateSpousePresentation(actor, new Entry { NpcId = intent.NpcId, Following = intent.Following });
                created = null;
            }
            catch (Exception e) { ModBehaviour.DevLog("[CoopNPC] 独立配偶生成失败: " + e.Message); }
            finally { if (created != null) DestroySpouseObject(created); state.Spawning = false; }
        }

        private async UniTask CreateSpouseReplica(Entry entry, int requestGeneration)
        {
            if (string.IsNullOrEmpty(entry.Asset) || !IsSpouseNpc(entry.NpcId)
                || entry.Id != SpouseWorldId(entry.Owner, entry.Asset, entry.NpcId)) return;
            GameObject created = await CreateIndependentSpouse(entry.NpcId, entry.Position, true);
            if (created == null) return;
            bool present = false;
            if (latest != null && latest.Npcs != null)
                foreach (Entry current in latest.Npcs) if (current != null && current.Id == entry.Id) { present = true; break; }
            if (disposed || requestGeneration != generation || !present) { DestroySpouseObject(created); return; }
            var actor = new Actor { Object = created, Kind = "spouse", Owner = entry.Owner, Asset = entry.Asset, NpcId = entry.NpcId };
            actors[entry.Id] = actor;
            created.name = "BossRush_CoopSpouse_" + entry.Id;
            UpdateSpousePresentation(actor, entry);
            StopReplicaMovement(created); actor.MovementSuppressed = true;
        }

        private async UniTask<GameObject> CreateIndependentSpouse(string npc, Vector3 position, bool replica)
        {
            DuckNpcBlueprint blueprint;
            if (DuckNpcRegistry.TryGet(npc, out blueprint) && blueprint.isPermanent)
            {
                CharacterMainControl duck = await DuckNpcSpawner.SpawnAsync(blueprint, position, Vector3.forward, replica, false);
                if (duck == null) return null;
                PermanentDuckNpcModule.AttachPermanentParts(duck, blueprint, position);
                return duck.gameObject;
            }
            GameObject prefab = null;
            if (npc == GoblinAffinityConfig.NPC_ID && owner != null && owner.GoblinNpcRuntime != null
                && owner.GoblinNpcRuntime.LoadGoblinAssetBundle()) prefab = owner.GoblinNpcRuntime.GoblinPrefab;
            else if (npc == NurseAffinityConfig.NPC_ID) prefab = NurseNpcRuntimeModule.GetPrefabForRuntime();
            if (prefab == null) return null;
            GameObject created = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            try
            {
                created.SetActive(true);
                foreach (Transform child in created.GetComponentsInChildren<Transform>(true)) child.gameObject.SetActive(true);
                NPCCommonUtils.FixShaders(created, "[CoopSpouse]");
                NPCCommonUtils.SetLayerRecursively(created, LayerMask.NameToLayer("Default"));
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (npc == GoblinAffinityConfig.NPC_ID)
                {
                    if (created.GetComponent<GoblinNPCController>() == null) created.AddComponent<GoblinNPCController>();
                    GoblinMovement movement = created.GetComponent<GoblinMovement>();
                    if (movement == null) movement = created.AddComponent<GoblinMovement>();
                    movement.SetSceneName(scene);
                    if (created.GetComponent<GoblinInteractable>() == null) created.AddComponent<GoblinInteractable>();
                }
                else
                {
                    if (created.GetComponent<NurseNPCController>() == null) created.AddComponent<NurseNPCController>();
                    NurseMovement movement = created.GetComponent<NurseMovement>();
                    if (movement == null) movement = created.AddComponent<NurseMovement>();
                    movement.SetSceneName(scene);
                    if (created.GetComponent<NurseInteractable>() == null) created.AddComponent<NurseInteractable>();
                }
                return created;
            }
            catch { DestroySpouseObject(created); throw; }
        }

        private static void SetSpouseMovement(GameObject npc, Transform target)
        {
            NPCFollowMovementBase movement = npc.GetComponent<NPCFollowMovementBase>();
            if (movement != null)
            {
                movement.enabled = true;
                if (target == null) { movement.DisablePlayerFollow(); movement.enabled = false; }
                else movement.EnablePlayerFollow(target);
            }
            DuckNpcMovement duck = npc.GetComponent<DuckNpcMovement>();
            if (duck != null) { duck.enabled = true; if (target == null) { duck.DisablePlayerFollow(); duck.Hold(); } else { duck.Release(0f); duck.EnablePlayerFollow(target); } }
        }
        private static void UpdateSpousePresentation(Actor actor, Entry entry)
        {
            GameObject npc = actor.Object;
            if (npc == null) return;
            if (actor.PresentationInitialized && actor.Following == entry.Following) return;
            actor.PresentationInitialized = true;
            actor.Following = entry.Following;
            WeddingNpcResidentMarker marker = npc.GetComponent<WeddingNpcResidentMarker>();
            if (!entry.Following) { if (marker == null) marker = npc.AddComponent<WeddingNpcResidentMarker>(); marker.NpcId = entry.NpcId; }
            else if (marker != null) UnityEngine.Object.Destroy(marker);
            GoblinNPCController goblin = npc.GetComponent<GoblinNPCController>();
            if (goblin != null) { if (entry.Following) goblin.ExitStationaryIdleState(); else goblin.EnterStationaryIdleState(); }
            NurseNPCController nurse = npc.GetComponent<NurseNPCController>();
            if (nurse != null) nurse.StartIdleAnimation();
            GoblinInteractable goblinInteraction = npc.GetComponent<GoblinInteractable>();
            if (goblinInteraction != null) goblinInteraction.RefreshMarriageOptionVisibility();
            NurseInteractable nurseInteraction = npc.GetComponent<NurseInteractable>();
            if (nurseInteraction != null) nurseInteraction.RefreshMarriageOptionVisibility();
            PermanentDuckNpcInteractable duckInteraction = npc.GetComponentInChildren<PermanentDuckNpcInteractable>(true);
            if (duckInteraction != null) duckInteraction.RefreshMarriageOptionVisibility();
        }
        private static void DestroySpouseObject(GameObject npc)
        {
            CharacterMainControl duck = npc.GetComponent<CharacterMainControl>();
            if (duck != null) DuckNpcSpawner.Despawn(duck); else UnityEngine.Object.Destroy(npc);
        }
        private void RemoveSpouse(string id)
        {
            Actor actor;
            if (actors.TryGetValue(id, out actor)) { if (actor.Object != null) DestroySpouseObject(actor.Object); actors.Remove(id); }
            spouses.Remove(id);
        }
        private void ClearSpouses()
        {
            var keys = new List<string>();
            foreach (var pair in actors) if (!string.IsNullOrEmpty(pair.Value.Owner)) keys.Add(pair.Key);
            foreach (string id in keys) RemoveSpouse(id);
            spouses.Clear(); spouseSequences.Clear(); localSpouseIntent = null; spouseAcknowledged = false; spouseRetryAt = 0f;
            spouseReleaseAsset = null; spouseReleaseAfter = 0f;
        }
        private void OnSpouseDisconnected(string player)
        {
            var removed = new List<string>();
            foreach (SpouseState state in spouses.Values) if (state.PlayerId == player) removed.Add(state.WorldId);
            foreach (string id in removed) RemoveSpouse(id);
            string prefix = player.Length + ":" + player + ":";
            removed.Clear();
            foreach (string scope in spouseSequences.Keys) if (scope.StartsWith(prefix, StringComparison.Ordinal)) removed.Add(scope);
            foreach (string scope in removed) spouseSequences.Remove(scope);
        }
        private void DisposeFollow()
        {
            if (spouseDisconnectSubscribed)
            { BossRushCoop.PlayerDisconnected -= OnSpouseDisconnected; spouseDisconnectSubscribed = false; }
            if (followRequest != null) followRequest.Dispose(); if (followReply != null) followReply.Dispose();
            followRequest = followReply = null; ClearSpouses();
        }
    }
}
