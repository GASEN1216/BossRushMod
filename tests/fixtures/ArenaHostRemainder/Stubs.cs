using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> All = new List<Object>();
        public static int SceneScans, ResourceScans;
        public bool Destroyed;
        public string name;
        public Object() { All.Add(this); }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(GameObject go)
        { if (go == null) return; go.Destroyed = true; foreach (var c in go.Components) c.Destroyed = true; BossRush.Probe.Events.Add("destroy"); }
        public static T[] FindObjectsOfType<T>() where T : Object
        { SceneScans++; return All.OfType<T>().Where(x => x != null).ToArray(); }
    }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform = new Transform();
        public bool Active;
        public UnityEngine.SceneManagement.Scene scene;
        public T Add<T>(T c) where T : Component { c.gameObject = this; Components.Add(c); return c; }
        public T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(x => x != null); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component { return Components.OfType<T>().Where(x => x != null).ToArray(); }
        public void SetActive(bool value) { Active = value; BossRush.Probe.Events.Add("active"); }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component { return gameObject.GetComponentsInChildren<T>(includeInactive); }
    }
    public class MonoBehaviour : Component
    { public Coroutine StartCoroutine(IEnumerator routine) { BossRush.Probe.Events.Add("coroutine"); return new Coroutine(); } }
    public sealed class Coroutine { }
    public class Collider : Component { public bool isTrigger; }
    public class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x;this.y=y;this.z=z; }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 forward { get { return new Vector3(0,0,1); } }
        public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator /(Vector3 a,float b) { return new Vector3(a.x/b,a.y/b,a.z/b); }
    }
    public static class Mathf { public static float Max(float a,float b) { return Math.Max(a,b); } }
    public static class Time { public static float time,deltaTime; }
    public static class Resources
    { public static T[] FindObjectsOfTypeAll<T>() where T : Object { Object.ResourceScans++; return Object.All.OfType<T>().Where(x => x != null).ToArray(); } }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int buildIndex; public string name; }
    public static class SceneManager
    { public static string Name="arena"; public static Scene GetActiveScene() { return new Scene { buildIndex=17,name=Name }; } }
}
namespace ItemStatsSystem { public class Stat { public float BaseValue=100; } }
namespace Duckov { }
namespace Duckov.Utilities { public class LootBoxLoader : UnityEngine.Component { } }
namespace BossRush
{
    using UnityEngine;
    using Cysharp.Threading.Tasks;
    internal static class Probe
    { internal static readonly List<string> Events=new List<string>(); internal static TaskCompletionSource<bool> YieldGate; }
    public enum Teams { player, middle, wolf, scav }
    public static class Team { public static bool IsEnemy(Teams a, Teams b) { return b==Teams.wolf || b==Teams.scav; } }
    public class EnemyPresetInfo { public string name, displayName; public int team; }
    public class CharacterRandomPreset : Object
    {
        public string nameKey, DisplayName;
        public bool showName;
        public Teams team;
        public int Calls;
        public Func<Task<CharacterMainControl>> Factory;
        public UniTask<CharacterMainControl> CreateCharacterAsync(Vector3 position, Vector3 direction,int scene,object data,bool active)
        {
            if (direction.z!=1 || scene!=17 || data!=null || active) throw new Exception("factory contract");
            Calls++; Probe.Events.Add("factory"); return new UniTask<CharacterMainControl>(Factory());
        }
    }
    public sealed class CharacterItem
    {
        public object Inventory=new object();
        public readonly Dictionary<string,ItemStatsSystem.Stat> Stats=new Dictionary<string,ItemStatsSystem.Stat>();
        public ItemStatsSystem.Stat GetStat(string key) { ItemStatsSystem.Stat value; return Stats.TryGetValue(key,out value)?value:null; }
    }
    public sealed class Health
    {
        public float CurrentHealth=30;
        public CharacterItem Item;
        public float MaxHealth { get { return Item.GetStat("MaxHealth").BaseValue; } }
        public void SetHealth(float value) { CurrentHealth=value; }
    }
    public sealed class DamageReceiver { public readonly Transform transform=new Transform(); }
    public class CharacterMainControl : MonoBehaviour
    {
        public static CharacterMainControl Main;
        public CharacterRandomPreset characterPreset;
        public bool Companion, ModeGOwned;
        public Teams Team=Teams.middle;
        public readonly CharacterItem CharacterItem=new CharacterItem();
        public readonly Health Health=new Health();
        public readonly DamageReceiver mainDamageReceiver=new DamageReceiver();
        public CharacterMainControl(string label)
        {
            name=label;new GameObject {name=label}.Add(this);
            foreach(var key in new[]{"MaxHealth","GunDamageMultiplier","MeleeDamageMultiplier"}) CharacterItem.Stats[key]=new ItemStatsSystem.Stat();
            Health.Item=CharacterItem;
        }
        public void SetTeam(Teams value) { Team=value;Probe.Events.Add("team"); }
    }
    public static class CharacterMainControlExtensions
    { public static bool IsMainCharacter(CharacterMainControl c) { return c==CharacterMainControl.Main; } }
    public class AICharacterController : Component
    {
        public float forceTracePlayerDistance;
        public DamageReceiver searchedEnemy;
        public bool noticed;
        public void SetTarget(Transform target) { Probe.Events.Add("target"); }
        public void SetNoticedToTarget(DamageReceiver target) { Probe.Events.Add("notice"); }
    }
    public static class PetNestCompanionAgent { public static bool IsCompanionCharacter(CharacterMainControl c) { return c.Companion; } }
    public static class ModeGRuntimeGates { public static bool IsDaXingXingOwnedByModeG(CharacterMainControl c) { return c.ModeGOwned; } }
    public static class ObjectCache
    { public static CharacterRandomPreset[] Presets; public static CharacterRandomPreset[] GetCharacterPresets() { return Presets; } }
    public static class SpawnedEnemyActivationHelper { public static void ReleaseFromPlayerDistanceSleep(CharacterMainControl c) { Probe.Events.Add("wake"); } }
    public static class MutatorManager
    {
        public static bool BossRegenEnabled;
        public static readonly List<MonoBehaviour[]> RegenCalls=new List<MonoBehaviour[]>();
        public static void ApplyToEnemy(CharacterMainControl c) { Probe.Events.Add("mutator"); }
        public static void TickBossRegen(float delta,List<MonoBehaviour> bosses)
        { if(delta!=Time.deltaTime)throw new Exception("regen time changed");RegenCalls.Add(bosses.ToArray()); }
    }
    public class InteractableLootbox : MonoBehaviour
    { public InteractableLootbox(string label) { name=label;new GameObject{name=label}.Add(this); } }
    public sealed class BossRushMapConfig { public Vector3? defaultSignPos,customSpawnPos;public Vector3[] spawnPoints; }
    public class ModBehaviour : MonoBehaviour
    {
        internal const float ARENA_RADIUS=500f,DaXingXingCleanInterval=.5f;
        internal static BossRushMapConfig Map;
        public ModBehaviour() { new GameObject{name="host"}.Add(this); }
        internal static BossRushMapConfig GetMapConfigBySceneName(string scene) { return Map; }
        internal static void DevLog(string text) { }
    }
    internal class EnemyRecoveryMonitor
    {
        internal IEnumerator DelayedBossPositionValidation(CharacterMainControl c,float delay)
        { if(delay!=.5f) throw new Exception("delay changed"); Probe.Events.Add("delay"); yield break; }
        internal void RegisterEnemyRecoveryAnchor(CharacterMainControl c,Vector3 position) { Probe.Events.Add("anchor"); }
    }
    internal abstract class BossRushRuntimeModuleBase
    { public virtual void OnAwake(ModBehaviour owner){} public virtual void OnDestroy(){} }
    internal sealed partial class WavesArenaRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private static WavesArenaRuntimeModule current;
        private int waveGeneration;
        private object milestoneDelivery;
        private EnemyRecoveryMonitor enemyRecoveryMonitor;
        internal MonoBehaviour CurrentBoss;
        internal bool InfiniteHellMode;
        internal int InfiniteHellWaveIndex,BossesPerWave;
        internal readonly List<MonoBehaviour> CurrentWaveBosses=new List<MonoBehaviour>();
        internal static InteractableLootbox CachedLootBoxTemplateWithLoader,CachedDifficultyRewardLootBoxTemplate;
        internal WavesArenaRuntimeModule(ModBehaviour host) { OnAwake(host);enemyRecoveryMonitor=new EnemyRecoveryMonitor(); }
        private void ReleaseBossRandomLootTrackingOnDestroy() { Probe.Events.Add("cleanup-loot"); }
        internal void RegisterBossRandomLootTracking(CharacterMainControl c,int count)
        { if(count!=3) throw new Exception("original loot count"); Probe.Events.Add("loot"); }
    }
}
