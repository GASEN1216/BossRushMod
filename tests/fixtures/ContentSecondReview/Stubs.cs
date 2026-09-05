using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// Host substitutes only; queue, scene validation, completion, cleanup and health ordering
// are extracted from production. TCS continuations run inline like the Unity main thread.
namespace UnityEngine
{
    public struct Vector3 { public static Vector3 back; }
    namespace SceneManagement
    {
        public struct Scene { public string name; public int handle; }
        public static class SceneManager
        {
            public static Scene Current;
            public static bool ThrowRead;
            public static Scene GetActiveScene()
            {
                if (ThrowRead) throw new InvalidOperationException("scene unavailable");
                return Current;
            }
        }
    }
}
public class ModBehaviour
{
    public static ModBehaviour Instance;
    public bool Destroyed, Arena;
    public bool ShouldUseRandomSupportNpcSelection(string name) { return Arena; }
    public bool UsesArenaSupportNpcPlacement() { return Arena; }
    public static void DevLog(string message) { }
    // Unity objects compare equal to null after destruction.
    public static bool operator ==(ModBehaviour a, ModBehaviour b)
    {
        bool an = ReferenceEquals(a, null) || (!ReferenceEquals(a, null) && a.Destroyed);
        bool bn = ReferenceEquals(b, null) || (!ReferenceEquals(b, null) && b.Destroyed);
        return an || bn ? an == bn : ReferenceEquals(a, b);
    }
    public static bool operator !=(ModBehaviour a, ModBehaviour b) { return !(a == b); }
    public override bool Equals(object other) { return ReferenceEquals(this, other); }
    public override int GetHashCode() { return base.GetHashCode(); }
}
public sealed class Health
{
    public float Bonus, CurrentHealth;
    public int Heals;
    public bool ThrowMax;
    public float MaxHealth
    {
        get { if (ThrowMax) throw new InvalidOperationException("health unavailable"); return 100 * (1 + Bonus); }
    }
    // Mirrors official Health: live MaxHealth stat getter, independently stored current HP.
    public void SetHealth(float value) { CurrentHealth = Math.Min(MaxHealth, value); Heals++; }
}
public sealed class CharacterMainControl
{
    public static CharacterMainControl Main;
    public Health Health = new Health();
    public bool Destroyed;
}
public sealed class DuckNpcBlueprint
{
    public string id = "xiaoman";
    public bool AllowsScene(string scene) { return scene == "Base" || scene == "B" || scene == "C"; }
}
public static class AffinityManager { public static bool Married; public static bool IsMarriedToPlayer(string id) { return Married; } }
public static class NPCAffinityInteractionHelper { public static void ApplyDailyDecayOnSpawn(string id, string prefix) { } }
public static class PermanentDuckNpcRegistry
{
    public static CharacterMainControl Instance;
    public static bool ThrowRegister;
    public static List<DuckNpcBlueprint> GetAllPermanent() { return new List<DuckNpcBlueprint> { new DuckNpcBlueprint() }; }
    public static CharacterMainControl GetInstance(string id) { return Instance; }
    public static void RegisterInstance(string id, CharacterMainControl npc)
    {
        if (ThrowRegister) throw new InvalidOperationException("register failed");
        Instance = npc;
    }
    public static void UnregisterInstance(string id) { Instance = null; }
}
public static class DuckNpcSpawner
{
    public sealed class PendingSpawn
    {
        public int Handle;
        public TaskCompletionSource<CharacterMainControl> Completion = new TaskCompletionSource<CharacterMainControl>();
    }
    public static List<PendingSpawn> Pending = new List<PendingSpawn>();
    public static int Despawns;
    public static Task<CharacterMainControl> SpawnAsync(DuckNpcBlueprint blueprint, Vector3 position, Vector3 facing)
    {
        var pending = new PendingSpawn { Handle = UnityEngine.SceneManagement.SceneManager.Current.handle };
        Pending.Add(pending);
        return pending.Completion.Task;
    }
    public static void Despawn(CharacterMainControl npc) { npc.Destroyed = true; Despawns++; }
}
public static class TaskCapture
{
    public static List<Task> Tasks = new List<Task>();
    public static void Forget(this Task task) { Tasks.Add(task); }
}
public partial class PermanentDuckNpcModule
{
    private const string LogPrefix = "fixture";
    private static Dictionary<string, object> _configs = new Dictionary<string, object>();
    private static void RegisterAllAffinityConfigs() { }
    private static bool TryResolveSpawnPosition(string scene, out Vector3 position) { position = new Vector3(); return true; }
    private static void AttachPermanentParts(CharacterMainControl npc, DuckNpcBlueprint blueprint, Vector3 position) { }
    public bool Busy { get { return _spawnInFlight; } }
    public bool Pending { get { return _pendingSpawnRequest != null; } }
}
public sealed class ZombieModeAttributeModifierRecord { public Health Owner; }
public static class RuntimeStatModifierTracker
{
    public static bool RejectAdd;
    public static void RemoveAll(List<ZombieModeAttributeModifierRecord> records, string context)
    {
        foreach (var record in records) record.Owner.Bonus = 0;
        records.Clear();
    }
    public static bool TryAdd(CharacterMainControl main, string stat, float value, object source,
        List<ZombieModeAttributeModifierRecord> records, string context)
    {
        if (RejectAdd || main.Health == null) return false;
        main.Health.Bonus = value;
        records.Add(new ZombieModeAttributeModifierRecord { Owner = main.Health });
        return true;
    }
}
public enum BackMountainFacility { Showcase }
public static class BackMountainUnlocks
{
    public static bool Unlocked = true;
    public static bool IsFacilityUnlocked(BackMountainFacility value) { return Unlocked; }
}
public static class ZombieModeStatNames { public const string MaxHealth = "MaxHealth"; }
public static class BackMountainConfig { public const string LogPrefix = "fixture"; }
public static partial class ShowcaseService
{
    private static List<ZombieModeAttributeModifierRecord> _records = new List<ZombieModeAttributeModifierRecord>();
    private static object _modifierSource = new object();
    private static float _bonus;
    private static float CalculateBonus() { return _bonus; }
    public static void Configure(Health health, float oldBonus, float newBonus)
    {
        _records.Clear();
        if (oldBonus > 0) _records.Add(new ZombieModeAttributeModifierRecord { Owner = health });
        health.Bonus = oldBonus;
        _bonus = newBonus;
        BackMountainUnlocks.Unlocked = true;
        RuntimeStatModifierTracker.RejectAdd = false;
    }
    public static int Records { get { return _records.Count; } }
}
