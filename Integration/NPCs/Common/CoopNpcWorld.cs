using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>房主拥有 NPC 移动，每端挂自己的服务、关系与剧情交互。</summary>
    internal sealed partial class CoopNpcWorld : IDisposable
    {
        [Serializable] internal sealed class State { public string RunId; public int Generation; public long Sequence; public Entry[] Npcs; }
        [Serializable] internal sealed class Entry
        {
            public string Id, Kind, Owner, Asset, NpcId;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Active;
            public bool Following;
        }
        private sealed class Actor { internal GameObject Object; internal string Kind, Owner, Asset, NpcId; internal bool MovementSuppressed, Following, PresentationInitialized; }
        private readonly Dictionary<string, Actor> actors = new Dictionary<string, Actor>(StringComparer.Ordinal);
        private readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
        private readonly ModBehaviour owner;
        private readonly IDisposable subscription;
        private State latest;
        private float nextTick;
        private long stateSequence;
        private int generation;
        private int sceneGeneration = -1;
        private string runId;
        private bool disposed;
        internal static CoopNpcWorld Current { get; private set; }
        internal static bool ApplyingReplica { get; private set; }
        internal static bool MaySpawn { get { return BossRushCoop.HasWorldAuthority || ApplyingReplica; } }

        internal CoopNpcWorld(ModBehaviour mod)
        {
            owner = mod; Current = this;
            subscription = BossRushCoop.Register("npc.world", OnState);
            SubscribeFollow();
        }
        internal static void Track(string id, string kind, GameObject value)
        {
            if (Current == null || value == null || string.IsNullOrEmpty(id)) return;
            Current.actors[id] = new Actor { Object = value, Kind = kind };
        }
        internal static CharacterMainControl Find(string id)
        {
            GameObject value = FindObject(id);
            return value != null ? value.GetComponent<CharacterMainControl>() : null;
        }
        internal static GameObject FindObject(string id)
        {
            Actor actor;
            return Current != null && Current.actors.TryGetValue(id, out actor) && actor.Object != null
                ? actor.Object : null;
        }
        private static void StopReplicaMovement(GameObject value)
        {
            foreach (MonoBehaviour component in value.GetComponentsInChildren<MonoBehaviour>(true))
                if (component is CourierMovement || component is GoblinMovement || component is NurseMovement
                    || component is DuckNpcMovement) component.enabled = false;
            foreach (UnityEngine.AI.NavMeshAgent agent in value.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
                agent.enabled = false;
        }
        private void OnState(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            RefreshCoopNpcScope();
            State state = message.Read<State>();
            if (state == null || state.RunId != runId || state.Generation != sceneGeneration
                || state.Npcs == null || state.Npcs.Length > 4096 || state.Sequence <= 0
                || (latest != null && state.Sequence <= latest.Sequence)) return;
            latest = state;
        }

        internal void Tick()
        {
            if (disposed || !BossRushCoop.IsConnected || !BossRushCoop.SessionReady || SceneLoader.IsSceneLoading
                || !LevelManager.AfterInit || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.25f;
            RefreshCoopNpcScope();
            if (BossRushCoop.IsHost)
            {
                TickFollow();
                var entries = new List<Entry>(actors.Count);
                foreach (var pair in actors)
                    if (pair.Value.Object != null)
                        entries.Add(new Entry { Id = pair.Key, Kind = pair.Value.Kind,
                            Position = pair.Value.Object.transform.position, Rotation = pair.Value.Object.transform.rotation,
                            Active = pair.Value.Object.activeSelf, Owner = pair.Value.Owner, Asset = pair.Value.Asset,
                            NpcId = pair.Value.NpcId, Following = pair.Value.Following });
                BossRushCoop.Broadcast("npc.world", new State { RunId = runId, Generation = sceneGeneration, Sequence = ++stateSequence, Npcs = entries.ToArray() });
                return;
            }
            TickLocalSpouse();
            if (latest == null || latest.Npcs == null || latest.Npcs.Length > 4096
                || latest.RunId != runId || latest.Generation != sceneGeneration) return;
            foreach (Entry entry in latest.Npcs)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                Actor actor;
                if (!actors.TryGetValue(entry.Id, out actor) || actor.Object == null)
                {
                    if (pending.Add(entry.Id)) CreateReplica(entry, generation).Forget();
                    continue;
                }
                if (!actor.MovementSuppressed) { StopReplicaMovement(actor.Object); actor.MovementSuppressed = true; }
                if (!string.IsNullOrEmpty(entry.Owner)) UpdateSpousePresentation(actor, entry);
                actor.Object.transform.SetPositionAndRotation(entry.Position, entry.Rotation);
                if (actor.Object.activeSelf != entry.Active) actor.Object.SetActive(entry.Active);
            }
            var gone = new List<string>();
            foreach (var pair in actors)
            {
                bool exists = false;
                foreach (Entry entry in latest.Npcs) if (entry != null && entry.Id == pair.Key) { exists = true; break; }
                if (!exists) gone.Add(pair.Key);
            }
            foreach (string id in gone)
            {
                Actor actor = actors[id];
                if (actor.Object != null)
                { if (string.IsNullOrEmpty(actor.Owner)) UnityEngine.Object.Destroy(actor.Object); else DestroySpouseObject(actor.Object); }
                if (string.IsNullOrEmpty(actor.Owner) && PermanentDuckNpcRegistry.GetInstance(id) != null) PermanentDuckNpcRegistry.UnregisterInstance(id);
                actors.Remove(id);
            }
        }
        private async UniTaskVoid CreateReplica(Entry entry, int requestGeneration)
        {
            CharacterMainControl created = null;
            try
            {
                if (!string.IsNullOrEmpty(entry.Owner))
                { await CreateSpouseReplica(entry, requestGeneration); return; }
                if (entry.Kind == "duck")
                {
                    DuckNpcBlueprint blueprint;
                    if (!DuckNpcRegistry.TryGet(entry.Id, out blueprint)) return;
                    created = await DuckNpcSpawner.SpawnAsync(blueprint, entry.Position, entry.Rotation * Vector3.forward, true);
                    if (created == null) return;
                    if (disposed || requestGeneration != generation) { DuckNpcSpawner.Despawn(created); return; }
                    if (blueprint.isPermanent)
                    {
                        PermanentDuckNpcModule.AttachPermanentParts(created, blueprint, entry.Position);
                        PermanentDuckNpcRegistry.RegisterInstance(entry.Id, created);
                    }
                    Track(entry.Id, entry.Kind, created.gameObject);
                    if (!blueprint.isPermanent) NPCNameTagHelper.RegisterOriginalHealthBarName(created.transform, SkyIslandWorldStory.ResidentName(entry.Id), 2.2f, "[CoopNPC]");
                }
                else
                {
                    ApplyingReplica = true;
                    try
                    {
                        if (entry.Kind == "goblin") owner.SpawnGoblinNPC(entry.Position, true, true);
                        else if (entry.Kind == "nurse") owner.SpawnNurseNPC(entry.Position, true, true);
                        else if (entry.Kind == "courier") { ReplicaPosition = entry.Position; owner.SpawnCourierNPC(); }
                    }
                    finally { ApplyingReplica = false; ReplicaPosition = null; }
                }
            }
            catch (Exception e) { ModBehaviour.DevLog("[CoopNPC] 远端 NPC 交互装配失败: " + e.Message); }
            finally { if (requestGeneration == generation) pending.Remove(entry.Id); }
        }
        internal void ClearScene()
        {
            ClearSpouses();
            generation++; latest = null; pending.Clear();
            var gone = new List<string>();
            foreach (var pair in actors) if (pair.Value.Object == null) gone.Add(pair.Key);
            foreach (string id in gone) actors.Remove(id);
        }
        private void RefreshCoopNpcScope()
        {
            if (sceneGeneration == BossRushCoop.SceneGeneration && runId == BossRushCoop.RunId) return;
            ClearScene(); sceneGeneration = BossRushCoop.SceneGeneration; runId = BossRushCoop.RunId;
        }
        internal static Vector3? ReplicaPosition { get; private set; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; subscription.Dispose(); DisposeFollow(); ClearScene(); actors.Clear();
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }

}
