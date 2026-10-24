using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed;
        public static readonly List<Object> All = new List<Object>();
        public Object() { All.Add(this); }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static T Instantiate<T>(T source) where T : Object
        {
            return (T)(Object)((BossRush.CharacterRandomPreset)(Object)source).Copy();
        }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null) || value.Destroyed) return;
            GameObject go = value as GameObject;
            if (go != null)
            {
                foreach (Component component in go.Components.ToArray()) Destroy(component);
            }
            MethodInfo hook = value.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (hook != null) hook.Invoke(value, null);
            value.Destroyed = true;
        }
    }
    public class Component : Object { public GameObject gameObject; }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        public readonly Transform transform = new Transform();
        public bool Active;
        public T AddComponent<T>() where T : Component, new()
        {
            T result = new T { gameObject = this }; Components.Add(result); return result;
        }
        public T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(); }
        public void SetActive(bool value) { Active = value; BossRush.Probe.Events.Add("active:" + value); }
    }
    public class Transform { public Vector3 localScale, position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 one { get { return new Vector3(1f, 1f, 1f); } }
        public static Vector3 forward { get { return new Vector3(0f, 0f, 1f); } }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public struct Color { public Color(float r, float g, float b, float a) { } }
    public static class Mathf { public static int Max(int a, int b) { return Math.Max(a, b); } }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() { return Object.All.OfType<T>().Where(p => !((Object)(object)p).Destroyed).ToArray(); } }
    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void Log(string value) { }
        public static void LogWarning(string value) { Warnings.Add(value); }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle, buildIndex; }
    public static class SceneManager
    {
        public static Scene Current = new Scene { handle = 17, buildIndex = 7 };
        public static Scene GetActiveScene() { return Current; }
    }
}
namespace NodeCanvas.BehaviourTrees
{
    public class BehaviourTreeOwner { public bool enabled; public void StopBehaviour(bool value) { } }
}
namespace ItemStatsSystem.Items { public class Placeholder { } }
namespace ItemStatsSystem
{
    public class Stat { public float BaseValue = 1f; }
    public class Item : UnityEngine.Object
    {
        public int TypeID;
        public float Durability, MaxDurability = 100f;
        public readonly UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public readonly Dictionary<string, Stat> Stats = new Dictionary<string, Stat> { { "MaxHealth", new Stat() } };
        public Inventory Inventory = new Inventory();
        public bool Dropped;
        public Stat GetStat(string key) { Stat value; return Stats.TryGetValue(key, out value) ? value : null; }
        public void DestroyTree() { UnityEngine.Object.Destroy(gameObject); }
        public void Drop(UnityEngine.Vector3 at, bool a, UnityEngine.Vector3 direction, float rotation) { Dropped = true; }
    }
    public class Slot { public Item Content; public Item Unplug() { Item value = Content; Content = null; return value; } }
    public class Inventory
    {
        public int Capacity;
        public bool Accept = true;
        public readonly List<Item> Content = new List<Item>();
        public void SetCapacity(int value) { Capacity = value; }
        public bool AddAndMerge(Item item, int value) { if (!Accept) return false; Content.Add(item); return true; }
    }
    public static class ItemAssetsCollection
    {
        public static bool Available = true;
        public static Item Last;
        public static Item InstantiateSync(int typeID) { return Last = Available ? new Item { TypeID = typeID } : null; }
    }
}
namespace BossRush
{
    using UnityEngine;
    using ItemStatsSystem;
    internal static class Probe { internal static readonly List<string> Events = new List<string>(); }
    public enum Teams { player, middle, wolf, scav }
    public enum CharacterIconTypes { boss }
    public static class Team { public static bool IsEnemy(Teams first, Teams second) { return first != second && second != Teams.middle; } }
    public static class SceneLoader { public static bool IsSceneLoading; }
    public class EnemyPresetInfo
    {
        public string name, displayName;
        public int team, expReward;
        public float baseHealth, baseDamage, healthMultiplier, damageMultiplier;
    }
    public class ModBehaviour : Object
    {
        public static ModBehaviour Instance;
        public static bool ModeG, ModeH;
        public bool IsModeEActive, IsModeFActive, IsZombieModeActive;
        public bool HasArenaEnemyPresetCatalog = true;
        public int StatApplications;
        public readonly List<EnemyPresetInfo> Catalog = new List<EnemyPresetInfo>();
        public readonly List<CharacterMainControl> WaveBosses = new List<CharacterMainControl>();
        public CharacterMainControl CurrentBoss;
        public EnemyPresetInfo FindArenaEnemyPreset(string key) { return Catalog.FirstOrDefault(p => p.name == key); }
        public void AddArenaEnemyPreset(EnemyPresetInfo value) { Catalog.Add(value); }
        public void RegisterArenaWaveBossFromContent(CharacterMainControl value) { CurrentBoss = value; WaveBosses.Add(value); }
        public void ClearArenaCurrentBossFromContent(CharacterMainControl value) { if (ReferenceEquals(CurrentBoss, value)) CurrentBoss = null; }
        public void RemoveArenaWaveBossFromContent(CharacterMainControl value) { WaveBosses.Remove(value); }
        public void ApplyCampaignBossStatMultiplierForRuntime(CharacterMainControl value)
        {
            StatApplications++; value.MeleeScale *= 2f;
        }
        public static bool IsModeGRunInProgressSafe() { return ModeG; }
        public static bool IsModeHRunInProgressSafe() { return ModeH; }
        public static void DevLog(string message) { }
    }
    public class CharacterRandomPreset : Object
    {
        public string nameKey;
        public bool isBoss, showName, showHealthBar, hasSkill = true, defaultWeaponOut = true;
        public object skillPfb = new object();
        public static readonly Queue<TaskCompletionSource<CharacterMainControl>> Pending = new Queue<TaskCompletionSource<CharacterMainControl>>();
        public static CharacterRandomPreset LastCreatedFrom;
        public static int RelatedScene;
        public CharacterRandomPreset Copy() { return new CharacterRandomPreset { nameKey = nameKey, name = name }; }
        public UniTask<CharacterMainControl> CreateCharacterAsync(Vector3 position, Vector3 direction, int scene, object group, bool leader)
        {
            LastCreatedFrom = this; RelatedScene = scene;
            var source = new TaskCompletionSource<CharacterMainControl>(); Pending.Enqueue(source);
            return new UniTask<CharacterMainControl>(source.Task);
        }
        public static CharacterMainControl Complete()
        {
            var character = new CharacterMainControl(); Pending.Dequeue().SetResult(character); return character;
        }
    }
    public class CharacterMainControl : Component
    {
        public CharacterMainControl()
        {
            gameObject = new GameObject { Active = true }; gameObject.Components.Add(this);
        }
        public Transform transform { get { return gameObject.transform; } }
        public CharacterRandomPreset characterPreset;
        public Item CharacterItem = new Item();
        public readonly HealthState Health = new HealthState();
        public bool isBossCharacter, dropBoxOnDead, Released;
        public Teams Team = Teams.middle;
        public float MeleeScale = .65f;
        public ActionState CurrentAction;
        public object CurrentHoldItemAgent;
        public void SetTeam(Teams team) { Team = team; }
        public void CancleSkill() { }
        public void Trigger(bool a, bool b, bool c) { }
        public void ChangeHoldItem(object item) { CurrentHoldItemAgent = item; }
        public Slot PrimWeaponSlot() { return null; }
        public Slot SecWeaponSlot() { return null; }
        public Slot MeleeWeaponSlot() { return null; }
        public T GetComponentInChildren<T>(bool inactive) where T : class { return null; }
    }
    public class HealthState
    {
        public float MaxHealth = 5000f;
        public bool showHealthBar;
        public void SetHealth(float value) { }
        public void RequestHealthBar() { Probe.Events.Add("healthbar"); }
    }
    public class ActionState { public void StopAction() { } }
    public class Tree { public void Stop(bool value) { } }
    public class AICharacterController
    {
        public GameObject gameObject;
        public bool defaultWeaponOut, enabled, hasSkill;
        public Tree patrolTree, alertTree, combatTree, combat_Attack_Tree;
        public void StopMove() { }
        public T[] GetComponentsInChildren<T>(bool inactive) { return new T[0]; }
    }
    public class BossAIController { public BossAIController(CharacterMainControl character, string name) { } public void Pause() { } }
    public static class BossRushEagerReflectionCache { public static FieldInfo CharacterRandomPreset_CharacterIconType; }
    public static class BossSkillDamageRules { public static float ReadMeleeDamageStatBase(CharacterMainControl boss) { return boss.MeleeScale; } }
    public static class SpawnedEnemyActivationHelper { public static void ReleaseFromPlayerDistanceSleep(CharacterMainControl boss) { boss.Released = true; Probe.Events.Add("wake"); } }
    public static class BossRushItemIds { public const int AstralStaff = 500082; }
    public static class BossRushDynamicItemRegistry { public static void EnsureRegistered(int value) { } }
    public static class AstralStaffWeaponConfig
    {
        public static bool Valid = true;
        public static bool TryConfigure(Item item) { return Valid; }
    }
    public static class L10n { public static string T(string cn, string en) { return cn; } }
    public class DamageInfo { }
    internal sealed partial class SandstormChampionController : MonoBehaviour
    {
        private CharacterMainControl _boss;
        private Item _reward;
        private CharacterRandomPreset _preset;
        internal Func<bool> Current;
        internal float Baseline;
        internal Item Reward { get { return _reward; } }
        internal void Initialize(CharacterMainControl boss, CharacterRandomPreset preset, Item reward, float baseline,
            BossAIController ai, Func<bool> current, int scene, ModBehaviour owner)
        {
            Probe.Events.Add("initialize");
            _boss = boss; _preset = preset; _reward = reward; Current = current; Baseline = baseline;
        }
        internal void TransferReward() { OnBeforeSpawnLoot(new DamageInfo()); }
        private void OnDestroy()
        {
            if (_reward != null) Object.Destroy(_reward.gameObject);
            _reward = null; if (_preset != null) Object.Destroy(_preset); _preset = null;
        }
    }
}
