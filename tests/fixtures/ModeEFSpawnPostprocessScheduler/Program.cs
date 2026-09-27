using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed;
            bool bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(GameObject go)
        {
            if (go == null) return;
            BossRush.Program.Events.Add("destroy:" + go.Name);
            go.Destroyed = true;
            foreach (var component in go.Components) component.Destroyed = true;
        }
    }
    public class GameObject : Object
    {
        public string Name;
        public bool Active;
        public readonly List<Object> Components = new List<Object>();
        public void SetActive(bool active) { Active = active; BossRush.Program.Events.Add("active:" + Name + ":" + active); }
    }
    public struct Vector3 { }
    public static class Time
    {
        public static int frameCount, Reads;
        public static float Now, Advance;
        public static float realtimeSinceStartup { get { Reads++; float value = Now; Now += Advance; return value; } }
    }
}
namespace Cysharp.Threading.Tasks
{
    public struct UniTask<T> { public Task<T> Value; }
    public class UniTaskCompletionSource<T>
    {
        private readonly TaskCompletionSource<T> source = new TaskCompletionSource<T>();
        public UniTask<T> Task { get { return new UniTask<T> { Value = source.Task }; } }
        public bool TrySetResult(T value) { return source.TrySetResult(value); }
    }
}
namespace BossRush
{
    using UnityEngine;
    internal sealed class ManagedBossRuntimeHandle { }
    public sealed class EnemyPresetInfo { public string displayName; }
    public sealed class CharacterMainControl : UnityEngine.Object
    {
        public sealed class HealthState
        {
            public bool Invincible;
            public void SetInvincible(bool value) { Invincible = value; Program.Events.Add("invincible"); }
        }
        public sealed class CharacterItemState { public object Inventory; }
        public GameObject gameObject;
        public HealthState Health;
        public CharacterItemState CharacterItem;
        public CharacterMainControl(string name)
        {
            gameObject = new GameObject { Name = name };
            gameObject.Components.Add(this);
            Health = new HealthState(); CharacterItem = new CharacterItemState { Inventory = new object() };
        }
    }
    internal sealed class ModeDItemPool
    { internal sealed class SharedModeEnemyEquipmentMaterializationPlan { public int Remaining; } }
    internal static class ModBehaviour
    {
        internal static bool DevModeEnabled { get { return false; } }
        internal static bool ModeEFSpawnProfilingEnabled { get { return false; } }
        internal static void DevLog(string text) { }
    }
    internal static class SpawnedEnemyActivationHelper
    { internal static void ReleaseFromPlayerDistanceSleep(CharacterMainControl c) { Program.Events.Add("wake:" + c.gameObject.Name); } }
    internal static class MutatorManager
    { internal static void ApplyToEnemy(CharacterMainControl c) { Program.Events.Add("mutator:" + c.gameObject.Name); } }
    internal static class Program
    {
        internal static readonly List<string> Events = new List<string>();
        private static void Check(bool condition, string label)
        { if (!condition) throw new Exception(label + " / " + string.Join(",", Events)); Console.WriteLine("PASS " + label); }
        private static ModeEFSpawnPostprocessScheduler Scheduler()
        {
            var scheduler = new ModeEFSpawnPostprocessScheduler();
            scheduler.BindServices(
                (c, plan) => { Events.Add("equipment:" + c.gameObject.Name); return --plan.Remaining <= 0; },
                c => Events.Add("multiplier:" + c.gameObject.Name),
                (c, count) => Events.Add("loot:" + c.gameObject.Name + ":" + count),
                plan => Events.Add("cleanup-plan"),
                c => Events.Add("untrack:" + c.gameObject.Name));
            return scheduler;
        }
        private static Task<EnemySpawnCoreResult> Queue(ModeEFSpawnPostprocessScheduler scheduler,
            CharacterMainControl character, int equipmentSteps = 0, bool multiplier = false, Func<bool> active = null,
            Func<EnemySpawnContext, bool> commit = null, EnemySpawnCoreOptions options = null, bool skipLoot = false)
        {
            var plan = equipmentSteps == 0 ? null : new ModeDItemPool.SharedModeEnemyEquipmentMaterializationPlan { Remaining = equipmentSteps };
            return scheduler.ScheduleModeEFSpawnPostprocessAsync(character,
                new EnemyPresetInfo { displayName = character.gameObject.Name }, true, new Vector3(),
                active, plan, multiplier, skipLoot, commit, options).Value;
        }
        private static void ResetClock(int frame = 0)
        { Time.frameCount = frame; Time.Now = 0; Time.Advance = 0; Time.Reads = 0; Events.Clear(); }
        private static void Main()
        {
            ResetClock(); var scheduler = Scheduler(); scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(Time.Reads == 0 && Events.Count == 0, "empty scheduler returns before clock or service calls");
            var a = Queue(scheduler, new CharacterMainControl("a"), 3, true);
            var b = Queue(scheduler, new CharacterMainControl("b"), 3, true);
            scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(Events.SequenceEqual(new[] { "equipment:a", "equipment:b", "equipment:a", "equipment:b", "equipment:a", "equipment:b", "multiplier:a", "multiplier:b" })
                && !a.IsCompleted && !b.IsCompleted, "baseline tick is round-robin and bounded to eight steps");
            scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(a.Result.success && b.Result.success, "commit happens after equipment and multiplier stages");

            ResetClock(); scheduler = Scheduler();
            Queue(scheduler, new CharacterMainControl("a"), 100);
            Queue(scheduler, new CharacterMainControl("b"), 100);
            Time.frameCount = 54; scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(Events.Count == 8, "before final five frames keeps normal budget");
            Events.Clear(); Time.frameCount = 55; scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(Events.Count == 16 && Events.Take(6).SequenceEqual(new[] { "equipment:a", "equipment:a", "equipment:a", "equipment:b", "equipment:b", "equipment:b" }),
                "deadline sprint raises bounded work and per-job steps at frame 55");
            scheduler.ClearModeEFSpawnPostprocessScheduler();

            ResetClock(); scheduler = Scheduler(); Queue(scheduler, new CharacterMainControl("timed"), 100);
            Time.Advance = .01f; scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(Events.SequenceEqual(new[] { "equipment:timed" }), "elapsed frame budget stops work before step ceiling");
            Time.Advance = 0; scheduler.ClearModeEFSpawnPostprocessScheduler();

            ResetClock(); scheduler = Scheduler(); var other = Scheduler();
            a = Queue(scheduler, new CharacterMainControl("a"), 1);
            b = Queue(other, new CharacterMainControl("b"));
            scheduler.ClearModeEFSpawnPostprocessScheduler();
            Check(a.Result.failureReason == "scheduler_cleared" && !b.IsCompleted
                && Events.SequenceEqual(new[] { "cleanup-plan", "untrack:a", "destroy:a" }),
                "clear cancels only its own queue and cleans plan before untrack and destruction");
            other.TickModeEFSpawnPostprocessScheduler(); Check(b.Result.success, "independent scheduler remains usable");

            ResetClock(); scheduler = Scheduler(); var dead = new CharacterMainControl("dead");
            a = Queue(scheduler, dead, 1); UnityEngine.Object.Destroy(dead.gameObject); Events.Clear();
            scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(a.Result.failureReason == "character_missing" && Events.SequenceEqual(new[] { "cleanup-plan" }),
                "destroyed character closes job without duplicate destruction or untrack");
            a = Queue(scheduler, new CharacterMainControl("ended"), 1, active: () => false); Events.Clear();
            scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(a.Result.failureReason == "mode_ended" && Events.SequenceEqual(new[] { "cleanup-plan", "untrack:ended", "destroy:ended" }),
                "ended scope cancels before materialization");

            ResetClock(); scheduler = Scheduler(); var live = new CharacterMainControl("live");
            a = Queue(scheduler, live, commit: ctx => { Events.Add("commit"); return true; });
            scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(a.Result.success && Events.SequenceEqual(new[] { "active:live:True", "wake:live", "mutator:live", "loot:live:3", "commit" }),
                "normal commit preserves activation, wake, mutator and loot order");
            Events.Clear(); live = new CharacterMainControl("hold");
            a = Queue(scheduler, live, commit: ctx => { throw new Exception("must not commit externally held job"); },
                options: new EnemySpawnCoreOptions { HoldForExternalCommit = true, ApplySharedMutators = false }, skipLoot: true);
            scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(a.Result.success && live.Health.Invincible && !live.gameObject.Active
                && Events.SequenceEqual(new[] { "invincible", "active:hold:False" }), "external hold skips wake, mutators, loot and legacy commit");
            Events.Clear(); a = Queue(scheduler, new CharacterMainControl("reject"), commit: ctx => { throw new Exception("reject"); });
            scheduler.TickModeEFSpawnPostprocessScheduler();
            Check(a.Result.failureReason == "commit_failed" && Events.Skip(4).SequenceEqual(new[] { "untrack:reject", "destroy:reject" }),
                "throwing commit is observable and untracks before destruction");
        }
    }
}
