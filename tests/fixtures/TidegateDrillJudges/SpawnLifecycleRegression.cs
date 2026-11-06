using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    // Slot、Spawn 和 DevRemoveExtras 从生产文件抽取；只替代 Unity 与官方异步创建边界。
    internal sealed partial class TidegateEnemies
    {
        private readonly List<Slot> slots = new List<Slot>();
        private readonly Dictionary<Slot, CharacterRandomPreset> sources = new Dictionary<Slot, CharacterRandomPreset>();
        private TestWorld world;
        private TestNavigation navigation;
        private TidegateTide tide = new TidegateTide();
        private CharacterMainControl player;
        private const float MinSpawnDistance = 25f, MaxSpawnDistance = 75f;
        private Func<bool> valid;
        private bool closed;
        private int inFlight;
        private float revealUntil = -1f;
        internal int SpawnedCount { get; private set; }
        private CharacterRandomPreset SourceFor(Slot slot) { return sources[slot]; }
        private static readonly Vector3 BossDigSpot = new Vector3(4.5f, 0f, -50.5f);
        private bool DiggersOut() { return false; } // 此夹具只测异步刷怪；专属战斗由 TidegateSandBoss 夹具驱动。
        private void DevNoteSpawn(Slot slot, Vector3 position) { }
        private bool OnLeadRoute(Slot slot, Vector3 position) { return true; } // 隔离图连通性，只在实机有 A* 节点。
        private bool connected = true;
        private bool SamePatrolArea(Vector3 a, Vector3 b) { return connected; }
        private static void DevObservePatrol(Slot slot, Vector3 target, bool newLeg) { }
        // 复现生产 MarkOnMap 的输入门：角色尚未登记时不会创建地图点，以验证 Spawn 的真实调用顺序。
        private void MarkOnMap(Slot slot) { if (slot.Character != null) slot.MapPoint = new GameObject("map_point"); }

        internal static List<string> CheckLateSpawnCancellation()
        {
            var errors = new List<string>();
            var owner = new TidegateEnemies
            {
                world = new TestWorld { Root = new GameObject("world") },
                navigation = new TestNavigation(), valid = () => true, revealUntil = 40f,
                player = CharacterMainControl.Create("player"),
            };
            owner.player.transform.position = new Vector3(40, 0, 0);
            var extraResult = new TaskCompletionSource<CharacterMainControl>();
            var regularResult = new TaskCompletionSource<CharacterMainControl>();
            var extra = new Slot { Id = "TidegateExtraSpawn_1", Kind = "digger", Marker = new GameObject("extra_anchor").transform };
            var regular = new Slot { Id = "SPAWN_town_scav_0", Kind = "scav", Marker = new GameObject("regular_anchor").transform };
            owner.slots.Add(extra);
            owner.slots.Add(regular);
            owner.sources[extra] = new CharacterRandomPreset { Completion = extraResult.Task, name = "extra" };
            owner.sources[regular] = new CharacterRandomPreset { Completion = regularResult.Task, name = "regular" };
            owner.Spawn(extra);
            CharacterRandomPreset extraClone = CharacterRandomPreset.LastClone;
            owner.Spawn(regular);
            if (owner.inFlight != 2 || !extra.DevSpawning) errors.Add("spawn_async_boundary_not_reached");

            int pending;
            if (owner.DevRemoveExtras(out pending) != 1 || pending != 1 || owner.slots.Count != 1 || !extra.DevCancelled)
                errors.Add("pending_extra_not_cancelled_and_removed");
            if (regular.DevCancelled || regular.Marker == null) errors.Add("regular_spawn_was_cancelled");
            int again;
            if (owner.DevRemoveExtras(out again) != 0 || again != 0) errors.Add("extra_cleanup_not_idempotent");

            var late = CharacterMainControl.Create("late_extra");
            extraResult.SetResult(late);
            if (!SpinWait.SpinUntil(() => owner.inFlight == 1, 2000)) errors.Add("cancelled_spawn_finally_not_reached");
            if (late.gameObject != null || late.gameObject.Active || extraClone != null || extra.Character != null || extra.DevSpawning)
                errors.Add("late_extra_or_preset_survived_cleanup");
            if (owner.SpawnedCount != 0) errors.Add("cancelled_extra_was_retained");

            var kept = CharacterMainControl.Create("regular");
            regularResult.SetResult(kept);
            if (!SpinWait.SpinUntil(() => owner.inFlight == 0, 2000)) errors.Add("regular_spawn_finally_not_reached");
            if (regular.Character != kept || kept.gameObject == null || owner.SpawnedCount != 1)
                errors.Add("normal_spawn_did_not_survive_extra_cleanup");
            if (regular.MapPoint == null) errors.Add("new_spawn_missing_active_lighthouse_reveal");
            // 点位本身可走，但投影踩在深水中时，改找岸上，而非直接把角色交给官方创建。
            owner.world.Ground = x => x < 1f ? -3f : 1f;
            Vector3 shore;
            if (!owner.TryNearbyFooting(new Vector3(0, 0, 0), out shore) || shore.x < 1f)
                errors.Add("deep_projection_did_not_move_to_dry_ground");
            owner.world.Ground = x => -3f;
            var drowned = new Slot { Id = "wet", Group = "cape", Kind = "scav", Marker = new GameObject("wet").transform };
            owner.Spawn(drowned);
            if (drowned.Started || drowned.Failed || drowned.RetryAfter <= Time.time || owner.inFlight != 0)
                errors.Add("entirely_submerged_anchor_not_deferred");
            owner.world.Ground = x => 1f;
            owner.player.transform.position = new Vector3(5, 0, 0);
            owner.Spawn(drowned);
            if (drowned.Started || drowned.Failed || owner.SpawnedCount != 1)
                errors.Add("dry_projection_inside_player_ring_was_spawned");
            owner.player.transform.position = new Vector3(40, 0, 0);
            var floodResult = new TaskCompletionSource<CharacterMainControl>();
            owner.sources[drowned] = new CharacterRandomPreset { Completion = floodResult.Task, name = "rain" };
            owner.Spawn(drowned);
            owner.tide.DeepOnCompletion = true;
            var arriving = CharacterMainControl.Create("late_flood");
            floodResult.SetResult(arriving);
            if (!SpinWait.SpinUntil(() => owner.inFlight == 0, 2000) || drowned.Started || arriving.gameObject != null
                || owner.SpawnedCount != 1 || drowned.Character != null) errors.Add("rising_tide_kept_late_underwater_spawn");
            var nearResult = new TaskCompletionSource<CharacterMainControl>();
            owner.sources[drowned] = new CharacterRandomPreset { Completion = nearResult.Task, name = "approach" };
            owner.tide.DeepOnCompletion = false;
            owner.Spawn(drowned); // 槽开刷时玩家在 40 m 外，等官方返回前跑到了 5 m。
            owner.player.transform.position = new Vector3(5, 0, 0);
            var tooClose = CharacterMainControl.Create("late_too_close");
            nearResult.SetResult(tooClose);
            if (!SpinWait.SpinUntil(() => owner.inFlight == 0, 2000) || drowned.Started || tooClose.gameObject != null
                || owner.SpawnedCount != 1 || drowned.Character != null) errors.Add("approaching_player_kept_late_spawn");
            owner.SpawnExtra(new Vector3(0, 0, 0), "digger");
            var queued = owner.slots[owner.slots.Count - 1];
            if (queued.Started || queued.Failed || owner.inFlight != 0 || owner.InSpawnRing(queued.Origin, true))
                errors.Add("gate_queue_started_inside_safety_radius");
            owner.player.transform.position = new Vector3(120, 0, 0);
            if (!owner.InSpawnRing(queued.Origin, true) || owner.InSpawnRing(queued.Origin)) errors.Add("gate_far_approach_blocked");
            var crossing = new TaskCompletionSource<CharacterMainControl>();
            owner.sources[queued] = new CharacterRandomPreset { Completion = crossing.Task, name = "extra_crossing" };
            owner.Spawn(queued);
            owner.player.transform.position = new Vector3(5, 0, 0);
            var closeExtra = CharacterMainControl.Create("late_close_seawall");
            crossing.SetResult(closeExtra);
            if (!SpinWait.SpinUntil(() => owner.inFlight == 0, 2000) || queued.Started || queued.Failed || closeExtra.gameObject != null)
                errors.Add("gate_async_spawn_was_retained_beside_player");
            return errors;
        }

        private static void AddPost(List<Slot> points, string id, string group, string kind, Vector3 position)
        {
            Transform marker = new GameObject(id).transform;
            marker.position = position;
            points.Add(new Slot { Id = id, Group = group, Kind = kind, Marker = marker, Home = position });
        }

        internal static List<string> CheckRaidVariety()
        {
            var errors = new List<string>();
            var points = new List<Slot>();
            var layout = new TestWorld { PlayerSpawn = new GameObject("player_spawn").transform };
            AuthoredLayout(points, layout);
            var used = new HashSet<string>();
            for (int seed = 0; seed < 512; seed++)
            {
                var owner = new TidegateEnemies { world = layout };
                owner.SelectRoster(new List<Slot>(points), seed);
                foreach (Slot slot in owner.slots) { slot.JoinedAt = -1f; used.Add(slot.Id); }
                owner.AddPartners("town", layout.Waypoints["town"], 4, seed);
                owner.AddPartners("docks", layout.Waypoints["docks"], 3, seed);
                owner.AssignRoles();
                int town = owner.slots.FindAll(s => s.Group == "town").Count, docks = owner.slots.FindAll(s => s.Group == "docks").Count;
                if (owner.slots.Count != 27 || town != 9 || docks != 6 || owner.slots.FindAll(s => s.Kind == "guard").Count != 4
                    || owner.slots.FindAll(s => s.Kind == "boss").Count != 1)
                { errors.Add("encounter_band_seed_" + seed + "_town_" + town + "_docks_" + docks); break; }
                if (owner.slots.FindAll(s => s.Role == TidegateEnemyRole.Marksman && s.Id == "SPAWN_ww_guard_8").Count != 1
                    || owner.slots.FindAll(s => s.Role == TidegateEnemyRole.Gunner && s.Group == "dig").Count != 1
                    || owner.slots.FindAll(s => s.Role == TidegateEnemyRole.Hound && s.Lead != null && ScavDistrict(s.Group)).Count != 2)
                    { errors.Add("roles_or_teams_not_bounded_" + seed); break; }
                foreach (Slot slot in owner.slots)
                {
                    if (slot.Lead == null) continue;
                    float range = (slot.Origin - slot.Lead.Origin).sqrMagnitude;
                    if (range < 25f || range >= 676f || (slot.Origin - layout.PlayerSpawn.position).sqrMagnitude < 729f)
                        errors.Add("companion_not_near_its_scout_or_ambushing_arrival");
                    slot.Lead.JoinedAt = -1f;
                    if (CanJoin(slot, 100f)) errors.Add("partner_without_scout");
                    slot.Lead.JoinedAt = 100f;
                    if (CanJoin(slot, 103.9f) || !CanJoin(slot, 107f)) errors.Add("partner_delay");
                    slot.Lead.JoinedAt = -1f;
                }
                foreach (Slot slot in points) { slot.Role = TidegateEnemyRole.Regular; slot.HasPatrol = false; }
            }
            if (used.Count != points.Count) errors.Add("some_authored_spawn_positions_never_participate");
            if (errors.Count == 0) Console.WriteLine("Tidegate actual layout: 512 seeds x 27 slots, town 9 / docks 6, 7 delayed partners, 4 guards + 1 boss.");
            foreach (SkyIslandLootTier tier in new[] { SkyIslandLootTier.Supply, SkyIslandLootTier.Voyage, SkyIslandLootTier.Starworks })
            {
                int[] counts = new int[8];
                double mean = 0;
                for (int roll = 0; roll < 100; roll++)
                {
                    int n = TidegateLoot.RollCount(tier, roll);
                    if (n < 1 || n >= counts.Length) { errors.Add("box_capacity"); break; }
                    counts[n]++; mean += n * 0.01;
                }
                if (counts[2] + counts[3] >= 60 || (tier == SkyIslandLootTier.Supply && (mean > 2.7 || mean < 2.1))
                    || (tier == SkyIslandLootTier.Starworks && (counts[1] + counts[2] > 0 || mean < 4.0)))
                    errors.Add("container_quantities_flat_or_unbalanced_" + tier);
                Console.WriteLine("Tidegate loot " + tier + " roll_count_mean=" + mean.ToString("F2") + " counts_1_to_6="
                    + string.Join("/", Array.ConvertAll(counts, n => n.ToString()), 1, 6));
            }
            return errors;
        }

        internal static List<string> CheckSquadPatrol()
        {
            var errors = new List<string>();
            var owner = new TidegateEnemies { world = new TestWorld { Ground = x => 1f }, navigation = new TestNavigation() };
            Slot lead = new Slot { Character = CharacterMainControl.Create("lead"), HasPatrol = true, PatrolPost = new Vector3(15, 1, 0) };
            Slot partner = new Slot { Character = CharacterMainControl.Create("partner"), Lead = lead, Home = new Vector3(0, 1, 7) };
            lead.Ai = lead.Character.GetComponentInChildren<AICharacterController>();
            partner.Ai = partner.Character.GetComponentInChildren<AICharacterController>();
            Time.time = 10f;
            owner.TickSquadPatrol(lead); owner.TickSquadPatrol(partner);
            if (lead.PatrolLeg != 1 || lead.Ai.Goal.x != 15f || partner.Ai.Goal.x != 15f || partner.Ai.Goal.z < 1f)
                errors.Add("squad_not_moving_to_same_flank_on_separate_shoulders");
            lead.Ai.searchedEnemy = new object(); Time.time = 20f;
            owner.TickSquadPatrol(lead); owner.TickSquadPatrol(partner);
            if (lead.Ai.Commands != 1 || partner.Ai.Commands != 1) errors.Add("squad_orders_during_combat");
            lead.Ai.searchedEnemy = null; partner.Ai.Noticing = true;
            owner.TickSquadPatrol(partner);
            if (partner.Ai.Commands != 1) errors.Add("squad_suppresses_investigation");
            lead.Character.transform.position = lead.PatrolGoal;
            owner.TickSquadPatrol(lead); Time.time = 24f; owner.TickSquadPatrol(lead);
            if (lead.PatrolLeg != 1) errors.Add("squad_no_observable_pause");
            Time.time = 27f; owner.TickSquadPatrol(lead);
            if (lead.PatrolLeg != 2 || lead.Ai.Goal.x != 0f) errors.Add("squad_never_returns_to_scout_post");
            lead.Character.transform.position = lead.PatrolGoal; owner.TickSquadPatrol(lead);
            owner.connected = false; Time.time = 34f; owner.TickSquadPatrol(lead);
            partner.Ai.Noticing = false; owner.TickSquadPatrol(partner);
            if (lead.PatrolLeg != 0 || lead.Ai.Stops == 0 || partner.Ai.Stops == 0) errors.Add("disconnected_patrol_did_not_stop");
            Time.time = 1f;
            return errors;
        }
    }

    internal sealed class TestWorld
    {
        internal GameObject Root;
        internal Transform PlayerSpawn;
        internal Dictionary<string, List<Vector3>> Waypoints = new Dictionary<string, List<Vector3>>();
        internal Func<float, float> Ground = x => 0f;
        internal bool TryGroundAt(Vector3 point, out float y) { y = Ground(point.x); return true; }
    }
    internal sealed class TidegateTide
    {
        internal bool DeepOnCompletion;
        internal bool IsDeepAt(Vector3 position) { return DeepOnCompletion; }
        internal float WaterLevelAt(Vector3 point) { return 0f; }
    }
    internal static class GameplayDataSettings { internal static class Layers { internal const int wallLayerMask = 1; } }
    internal sealed class TestNavigation
    {
        internal int Mask { get { return 1; } }
        internal bool TryNearestWalkable(Vector3 point, out Vector3 snapped) { snapped = point; return true; }
    }
    internal enum Teams { player, scav, wolf }
    internal static class Team { internal static bool IsEnemy(Teams self, Teams other) { return self != other; } }
    internal static partial class TidegateEnemyActors { internal static bool IsPatrolWolf(CharacterRandomPreset s) { return false; } }
    internal sealed class TidegateSandBoss : Component
    {
        internal static void Tune(CharacterRandomPreset p) { }
        internal void Bind(CharacterMainControl c, AICharacterController ai, TidegateTide tide, TestWorld world, Vector3 deck,
            Vector3 flats, Func<bool> posture, Func<bool> valid, string source) { }
    }
    internal enum SkyIslandEnemyTier { Elite }
    internal static class SkyIslandEnemyArmory { internal static void Arm(CharacterMainControl c, SkyIslandEnemyTier tier) { } }
    internal static class SpawnedEnemyActivationHelper { internal static void ReleaseFromPlayerDistanceSleep(CharacterMainControl c) { } }
    internal static class TidegateRaidScene { internal const string LogTag = "[Tidegate]"; }
    internal sealed class TidegatePresetOwner : Component { internal CharacterRandomPreset Preset; }
    internal sealed class Seeker : Component { internal int graphMask; internal void CancelCurrentPathRequest() { } }
    internal sealed class AICharacterController : Component
    {
        internal object searchedEnemy; internal bool Noticing;
        internal Vector3 patrolPosition, Goal; internal int Commands, Stops;
        internal bool isNoticing(float seconds) { return Noticing; }
        internal bool HasPath() { return false; }
        internal bool WaitingForPathResult() { return false; }
        internal void MoveToPos(Vector3 goal) { Goal = goal; Commands++; }
        internal void StopMove() { Stops++; }
    }
    internal sealed class TestHealth { internal bool IsDead; }
    internal sealed class CharacterMainControl : Component
    {
        internal TestHealth Health;
        internal Teams Team;
        internal void SetTeam(Teams team) { Team = team; }
        internal static CharacterMainControl Create(string name)
        {
            var root = new GameObject(name);
            var character = root.AddComponent<CharacterMainControl>();
            character.Health = new TestHealth();
            root.AddComponent<Seeker>();
            root.AddComponent<AICharacterController>();
            return character;
        }
    }
    internal sealed class CharacterRandomPreset : UnityEngine.Object
    {
        internal static CharacterRandomPreset LastClone;
        internal Task<CharacterMainControl> Completion;
        internal bool dropBoxOnDead, setActiveByPlayerDistance;
        internal float patrolRange;
        internal Teams team;
        internal CharacterRandomPreset Clone()
        {
            LastClone = new CharacterRandomPreset { Completion = Completion, name = name };
            return LastClone;
        }
        internal Task<CharacterMainControl> CreateCharacterAsync(Vector3 position, Vector3 forward, int team, object a, bool b) { return Completion; }
    }
}

namespace UnityEngine
{
    internal class Object
    {
        internal string name;
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        internal static T Instantiate<T>(T value) where T : Object { return (T)(Object)((BossRush.CharacterRandomPreset)(Object)value).Clone(); }
        internal static void Destroy(Object value, float delay = 0f)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            var root = value as GameObject;
            if (!ReferenceEquals(root, null)) root.DestroyComponents();
        }
    }
    internal class Component : Object
    {
        internal GameObject gameObject;
        internal Transform transform { get { return gameObject.transform; } }
        internal T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component { return gameObject.FindAll<T>(); }
        internal T GetComponentInChildren<T>() where T : Component { return gameObject.FindAll<T>()[0]; }
    }
    internal sealed class GameObject : Object
    {
        internal Transform transform;
        internal bool Active = true;
        private readonly List<Component> components = new List<Component>();
        internal GameObject(string title) { name = title; transform = new Transform { gameObject = this }; components.Add(transform); }
        internal void SetActive(bool value) { Active = value; }
        internal T AddComponent<T>() where T : Component, new() { var result = new T { gameObject = this }; components.Add(result); return result; }
        internal T[] FindAll<T>() where T : Component
        {
            var found = new List<T>();
            foreach (Component component in components) if (component is T) found.Add((T)component);
            return found.ToArray();
        }
        internal void DestroyComponents()
        {
            foreach (Component component in components)
            {
                component.Destroyed = true;
                var presetOwner = component as BossRush.TidegatePresetOwner;
                if (!ReferenceEquals(presetOwner, null)) Destroy(presetOwner.Preset);
            }
        }
    }
    internal sealed class Transform : Component
    {
        internal Vector3 position, forward;
        internal void SetParent(Transform parent, bool keepWorld) { }
    }
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        internal static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        internal float sqrMagnitude { get { return x*x + y*y + z*z; } }
        internal Vector3 normalized { get { float n = (float)Math.Sqrt(sqrMagnitude); return n > 0f ? this * (1f/n) : new Vector3(); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x, a.y+b.y, a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x, a.y-b.y, a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b, a.y*b, a.z*b); }
    }
    internal static class Time { internal static float time = 1f; }
    internal static class Mathf
    {
        internal const float PI = (float)Math.PI;
        internal static float Sin(float x) { return (float)Math.Sin(x); }
        internal static float Cos(float x) { return (float)Math.Cos(x); }
        internal static float Min(float x, float y) { return Math.Min(x, y); }
    }
    internal enum QueryTriggerInteraction { Ignore }
    internal static class Physics
    {
        internal static bool CheckCapsule(Vector3 a, Vector3 b, float r, int mask, QueryTriggerInteraction triggers) { return false; }
    }
    internal static class Debug { internal static void Log(string text) { } internal static void LogWarning(string text) { } }
}
