using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        public bool Destroyed, Destroying;
        public static bool operator ==(Object a, Object b)
        { return ReferenceEquals(a, b) || (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public static implicit operator bool(Object obj) { return obj != null; }
        public override bool Equals(object obj) { return ReferenceEquals(this, obj); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object obj)
        {
            if (ReferenceEquals(obj, null) || obj.Destroyed || obj.Destroying) return;
            obj.Destroying = true;
            var go = obj as GameObject;
            if (!ReferenceEquals(go, null))
            {
                foreach (var child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (var component in go.Components.ToArray()) Destroy(component);
                go.transform.SetParent(null);
            }
            else
            {
                var callback = obj.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                if (callback != null) callback.Invoke(obj, null);
            }
            obj.Destroyed = true;
            obj.Destroying = false;
        }
        public static T Instantiate<T>(T source) where T : Object { return Instantiate(source, null); }
        public static T Instantiate<T>(T source, Transform parent) where T : Object
        {
            if (source == null) return null;
            var component = source as Component;
            GameObject original = component != null ? component.gameObject : source as GameObject;
            if (original == null) throw new NotSupportedException("Only visual GameObjects/components are cloned");
            var copies = new Dictionary<Object, Object>();
            GameObject clone = CloneTree(original, copies);
            foreach (var pair in copies.ToArray())
            {
                if (!(pair.Key is Component) || pair.Key is Transform) continue;
                foreach (var field in pair.Key.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.IsInitOnly || field.Name == "gameObject" || field.Name == "Destroyed" || field.Name == "Destroying") continue;
                    object value = field.GetValue(pair.Key);
                    var unityValue = value as Object;
                    Object replacement;
                    if (!ReferenceEquals(unityValue, null) && copies.TryGetValue(unityValue, out replacement)) value = replacement;
                    field.SetValue(pair.Value, value);
                }
            }
            clone.transform.SetParent(parent);
            return (T)copies[source];
        }
        private static GameObject CloneTree(GameObject source, Dictionary<Object, Object> copies)
        {
            var clone = new GameObject(source.name + "(Clone)");
            clone.SetActive(source.activeSelf);
            clone.transform.localPosition = source.transform.localPosition;
            clone.transform.localRotation = source.transform.localRotation;
            clone.transform.localScale = source.transform.localScale;
            copies[source] = clone;
            copies[source.transform] = clone.transform;
            foreach (var c in source.Components)
            {
                if (c is Transform) continue;
                var next = (Component)Activator.CreateInstance(c.GetType());
                next.gameObject = clone; clone.Components.Add(next); copies[c] = next;
            }
            foreach (var child in source.transform.Children)
                CloneTree(child.gameObject, copies).transform.SetParent(clone.transform);
            return clone;
        }
    }
    public class GameObject : Object
    {
        public HideFlags hideFlags;
        public static readonly List<GameObject> All = new List<GameObject>();
        public readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy { get { return activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy); } }
        public GameObject(string label = "object")
        { name = label; transform = new Transform { gameObject = this }; Components.Add(transform); All.Add(this); }
        public T AddComponent<T>() where T : Component, new()
        { var c = new T { gameObject = this }; Components.Add(c); return c; }
        public T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(c => c != null); }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        {
            var result = new List<T>();
            if (includeInactive || activeInHierarchy) result.AddRange(Components.OfType<T>().Where(c => c != null));
            foreach (var t in transform.Children) result.AddRange(t.gameObject.GetComponentsInChildren<T>(includeInactive));
            return result.ToArray();
        }
        public void SetActive(bool active) { activeSelf = active; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component { return gameObject.GetComponentsInChildren<T>(includeInactive); }
        public T GetComponentInParent<T>() where T : Component
        {
            for (Transform t = transform; t != null; t = t.parent)
            { T c = t.gameObject.GetComponent<T>(); if (c != null) return c; }
            return null;
        }
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public class Transform : Component
    {
        public readonly List<Transform> Children = new List<Transform>();
        public Transform parent;
        public Vector3 localPosition, localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 position { get { return parent == null ? localPosition : parent.position + localPosition; } set { localPosition = parent == null ? value : value - parent.position; } }
        public Vector3 forward { get { return Vector3.forward; } }
        public void SetParent(Transform value, bool worldPositionStays = true) { if (parent != null) parent.Children.Remove(this); parent = value; if (parent != null) parent.Children.Add(this); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { return magnitude < 0.00001f ? zero : this / magnitude; } }
        public void Normalize() { this = normalized; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator /(Vector3 a, float b) { return new Vector3(a.x / b, a.y / b, a.z / b); }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
    }
    public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } public static Quaternion LookRotation(Vector3 direction) { return identity; } }
    public struct Color { public float r, g, b, a; public Color(float red, float green, float blue, float alpha = 1) { r = red; g = green; b = blue; a = alpha; } }
    public static class Mathf { public static float Max(float a, float b) { return Math.Max(a, b); } public static float Min(float a, float b) { return Math.Min(a, b); } }
    public static class Time { public static float time, deltaTime; }
    public enum HideFlags { None }
    public class Collider : Component { public bool enabled = true; }
    public class CapsuleCollider : Collider { public float radius, height; public Vector3 center; }
    public class Renderer : Component { public bool forceRenderingOff; }
    public enum QueryTriggerInteraction { Ignore }
    public static class LayerMask { public static int GetMask(params string[] layers) { return 3; } }
    public static class Physics
    {
        public static readonly List<Collider> Hits = new List<Collider>();
        public static readonly List<Vector3> Blocked = new List<Vector3>();
        public static int Overlaps, Lines;
        public static int OverlapSphereNonAlloc(Vector3 origin, float radius, Collider[] result, int mask)
        {
            Overlaps++;
            int count = Math.Min(Hits.Count, result.Length);
            // Return controlled broad-phase hits, including out-of-range colliders, so the production filter is exercised.
            for (int i = 0; i < count; i++) result[i] = Hits[i];
            return count;
        }
        public static bool Linecast(Vector3 origin, Vector3 point, int mask, QueryTriggerInteraction trigger)
        { Lines++; return Blocked.Any(p => (p - point).sqrMagnitude < 0.001f); }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; }
    public static class SceneManager { public static int Handle = 1; public static Scene GetActiveScene() { return new Scene { handle = Handle }; } }
}
namespace ItemStatsSystem.Stats
{
    public enum ModifierType { Add, PercentageAdd, PercentageMultiply }
    public class Modifier
    {
        public readonly ModifierType Type;
        public readonly float Value;
        public readonly object Source;
        public Modifier(ModifierType type, float value, object source) { Type = type; Value = value; Source = source; }
    }
    public class Stat
    {
        public float BaseValue;
        public bool Reject;
        public readonly List<Modifier> Modifiers = new List<Modifier>();
        public float Value
        {
            get
            {
                float value = (BaseValue + Modifiers.Where(m => m.Type == ModifierType.Add).Sum(m => m.Value))
                    * (1 + Modifiers.Where(m => m.Type == ModifierType.PercentageAdd).Sum(m => m.Value));
                foreach (var m in Modifiers.Where(m => m.Type == ModifierType.PercentageMultiply)) value *= 1 + m.Value;
                return value;
            }
        }
        public void AddModifier(Modifier modifier) { if (Reject) throw new InvalidOperationException("missing stat binding"); Modifiers.Add(modifier); }
        public void RemoveModifier(Modifier modifier) { Modifiers.Remove(modifier); }
    }
}
namespace ItemStatsSystem
{
    public class Item : Component
    {
        public int TypeID, MaxStackCount = 20;
        private int count = 1;
        public int StackCount { get { return count; } set { count = Math.Max(0, Math.Min(MaxStackCount, value)); } }
        public bool Stackable { get { return MaxStackCount > 1; } }
        public readonly Dictionary<string, Stat> Stats = new Dictionary<string, Stat>();
        public readonly List<Item> Equipment = new List<Item>();
        public AgentUtilities AgentUtilities;
        public Stat GetStat(string name) { Stat value; return Stats.TryGetValue(name, out value) ? value : null; }
        public void SetInt(string key, int value, bool notify) { if (key != "Count") throw new InvalidOperationException(key); count = value; }
    }
    public class AgentUtilities
    {
        public readonly Dictionary<int, ItemAgent> Prefabs = new Dictionary<int, ItemAgent>();
        public ItemAgent GetPrefab(int key) { ItemAgent agent; return Prefabs.TryGetValue(key, out agent) ? agent : null; }
    }
    public static class ItemAssetsCollection
    {
        public static readonly Dictionary<int, Item> Prefabs = new Dictionary<int, Item>();
        public static Item GetPrefab(int id) { Item item; return Prefabs.TryGetValue(id, out item) ? item : null; }
    }
    public abstract class UsageBehavior : Component
    {
        public struct DisplaySettingsData { public bool display; public string description; }
        public abstract DisplaySettingsData DisplaySettings { get; }
        public abstract bool CanBeUsed(Item item, object user);
        protected abstract void OnUse(Item item, object user);
        public void FinishLikeOfficial(Item item)
        {
            // UsageUtilities.Use rechecks the gate; CA_UseItem.OnFinish then always decrements.
            // Failure compensation is tested only when this gate is still true and OnUse actually runs.
            if (CanBeUsed(item, null)) OnUse(item, null);
            item.StackCount--;
        }
    }
}
namespace ItemStatsSystem.Items { public class Slot { public Item Content; } }
public class ItemAgent : MonoBehaviour
{
    public enum AgentTypes { equipment, handheld }
    public AgentTypes AgentType;
    public int SourceTypeID;
    public Item BoundItem;
}
public enum HandheldAnimationType { normal, meleeWeapon }
public class DuckovItemAgent : ItemAgent { public HandheldAnimationType handAnimationType; }
public class CharacterEquipmentController { public static readonly int equipmentModelHash = "Equipment".GetHashCode(); }
public class DamageReceiver : Component { }
public class CharacterModel : Component
{
    public Transform HelmatSocket, ArmorSocket, RightHandSocket, LeftHandSocket;
    public string SourcePreset;
}
public class CharacterRandomPreset : UnityEngine.Object { public string nameKey; public CharacterModel CharacterModel; }
public enum Teams { player, enemy, friend }
public static class Team { public static bool IsEnemy(Teams a, Teams b) { return a != b && b == Teams.enemy; } }
public enum ElementTypes { fire }
public class DamageInfo
{
    public CharacterMainControl Attacker;
    public float damageValue;
    public Vector3 damagePoint;
    public bool isFromBuffOrEffect;
    public int fromWeaponItemID;
    public readonly Dictionary<ElementTypes, float> Elements = new Dictionary<ElementTypes, float>();
    public DamageInfo(CharacterMainControl attacker) { Attacker = attacker; }
    public void AddElementFactor(ElementTypes type, float value) { Elements[type] = value; }
}
public class Health : Component
{
    public bool IsDead;
    public float CurrentHealth = 73, MaxHealth = 123;
    public int SetHealthCalls;
    public readonly List<DamageInfo> Damage = new List<DamageInfo>();
    public void SetHealth(float value) { SetHealthCalls++; CurrentHealth = value; }
    public void Hurt(DamageInfo info) { Damage.Add(info); CurrentHealth -= info.damageValue; }
}
public class CharacterMainControl : Component
{
    public static CharacterMainControl Main;
    public Item CharacterItem;
    public Health Health;
    public CharacterModel characterModel;
    public DamageReceiver mainDamageReceiver;
    public DuckovItemAgent CurrentHoldItemAgent;
    public Teams Team;
    public Vector3 CurrentAimDirection;
    public readonly List<ItemAgent> PlayerVisuals = new List<ItemAgent>();
    public readonly List<CharacterModel> AppliedModels = new List<CharacterModel>();
    private Action<DuckovItemAgent> shoot, attack, holdChanged;
    private static Action<CharacterMainControl, ItemStatsSystem.Items.Slot> equipmentChanged;
    public event Action<DuckovItemAgent> OnShootEvent { add { shoot += value; } remove { shoot -= value; } }
    public event Action<DuckovItemAgent> OnAttackEvent { add { attack += value; } remove { attack -= value; } }
    public event Action<DuckovItemAgent> OnHoldAgentChanged { add { holdChanged += value; } remove { holdChanged -= value; } }
    public static event Action<CharacterMainControl, ItemStatsSystem.Items.Slot> OnMainCharacterSlotContentChangedEvent
    { add { equipmentChanged += value; } remove { equipmentChanged -= value; } }
    public int ShootSubscribers { get { return shoot == null ? 0 : shoot.GetInvocationList().Length; } }
    public int AttackSubscribers { get { return attack == null ? 0 : attack.GetInvocationList().Length; } }
    public int HoldSubscribers { get { return holdChanged == null ? 0 : holdChanged.GetInvocationList().Length; } }
    public static int EquipmentSubscribers { get { return equipmentChanged == null ? 0 : equipmentChanged.GetInvocationList().Length; } }
    public void Shoot() { if (shoot != null) shoot(null); }
    public void Attack() { if (attack != null) attack(null); }
    public void HoldChanged() { if (holdChanged != null) holdChanged(CurrentHoldItemAgent); }
    public void EquipmentChanged() { if (equipmentChanged != null) equipmentChanged(this, null); }
    public void SetCharacterModel(CharacterModel model)
    {
        if (characterModel != null) UnityEngine.Object.Destroy(characterModel.gameObject);
        characterModel = model;
        AppliedModels.Add(model);
        if (model == null) model = characterModel = FixtureWorld.MakeModel("player");
        model.transform.SetParent(transform);
        // Official SetCharacterModel overwrites collision geometry from the chosen model.
        var body = GetComponent<CapsuleCollider>();
        if (body != null) { body.radius = 2; body.height = 4; body.center = Vector3.one; body.enabled = false; }
        var damage = mainDamageReceiver != null ? mainDamageReceiver.GetComponent<CapsuleCollider>() : null;
        if (damage != null) { damage.radius = 3; damage.height = 5; damage.center = Vector3.one; damage.enabled = false; }
        PlayerVisuals.Clear();
        foreach (Item item in CharacterItem.Equipment)
        {
            var go = new GameObject("player-equipped-" + item.TypeID);
            go.transform.SetParent(model.transform);
            var visual = go.AddComponent<DuckovItemAgent>();
            visual.SourceTypeID = item.TypeID; visual.BoundItem = item;
            visual.AgentType = item.TypeID == FixtureWorld.PlayerWeapon ? ItemAgent.AgentTypes.handheld : ItemAgent.AgentTypes.equipment;
            go.AddComponent<Renderer>();
            if (visual.AgentType == ItemAgent.AgentTypes.handheld) CurrentHoldItemAgent = visual;
            PlayerVisuals.Add(visual);
        }
    }
}
public static class SceneLoader { public static bool IsSceneLoading; }
public class LevelManager { public static LevelManager Instance; public bool IsBaseLevel; }
namespace Duckov.Utilities { public static class GameplayDataSettings { public static class Layers { public static int wallLayerMask = 4; } } }
namespace Duckov.UI { public static class NotificationText { public static readonly List<string> Messages = new List<string>(); public static void Push(string text) { Messages.Add(text); } } }
namespace BossRush
{
    public class ModBehaviour : MonoBehaviour
    {
        public static ModBehaviour Instance;
        public bool ConfiguredEnabled;
        public readonly List<string> Messages = new List<string>();
        public bool IsBackMountainConfiguredEnabled() { return ConfiguredEnabled; }
        public void ShowMessage(string text) { Messages.Add(text); }
        public static void DevLog(string text) { }
    }
    public static class BossRushUI { public static bool Paused; public static bool IsGamePaused() { return Paused; } }
    public static class BackMountainConfig { public const string LogPrefix = "[BackMountain] "; }
    public static class BossRushItemIds
    { public const int DragonFruit = 500065, EmberChili = 500066, PhantomMushroom = 500067; }
    public static class DragonDescendantConfig
    { public const string BasePresetNameKey = "Cname_Boss_Red"; public const int DRAGON_HELM_TYPE_ID = 500003, DRAGON_ARMOR_TYPE_ID = 500004; }
    public static class DragonKingConfig
    { public const string BasePresetNameKey = "Cname_Boss_Red"; public const int DRAGON_KING_HELM_TYPE_ID = 500011, DRAGON_KING_ARMOR_TYPE_ID = 500012; }
    public static class PhantomWitchConfig
    { public const string BasePresetNameKey = "Cname_Ghost"; public const float BossModelScale = 2f; public const int ReservedScytheTypeId = 500044; }
    public static class PhantomWitchScytheConfig { public const float BaseAttackRange = 2.22f; }
    public static class PhantomWitchScytheWeaponConfig
    {
        public static readonly List<GameObject> Prepared = new List<GameObject>();
        public static bool AllPreparedSafely = true;
        public static void PrepareRuntimeHoldAgentVisual(GameObject go)
        {
            AllPreparedSafely &= !go.activeInHierarchy && go.GetComponentsInChildren<MonoBehaviour>(true).All(s => !s.enabled)
                && go.GetComponentsInChildren<Collider>(true).All(c => !c.enabled);
            Prepared.Add(go);
        }
    }
    public static class PhantomWitchScytheSwingFx
    { public static int Calls; public static void PlayAt(Vector3 pos, Quaternion rotation, float scale, bool special) { Calls++; } }
    public static class ObjectCache { public static CharacterRandomPreset[] Presets; public static CharacterRandomPreset[] GetCharacterPresets() { return Presets; } }
    public static class DragonKingFxShared { public static readonly List<Color> Colors = new List<Color>(); public static void HitBurst(Vector3 pos, Color color) { Colors.Add(color); } }
    public static class L10n { public static string T(string cn, string en) { return en; } }
    public class ZombieModeAttributeModifierRecord { public Item CharacterItem; public Stat Stat; public Modifier Modifier; public string StatName; }
    public static class BackMountainItems
    {
        public class Definition { public bool IsSeed; public string NameCN, NameEN; }
        public static Definition GetDefinition(int typeId)
        {
            if (typeId >= 500062 && typeId <= 500067) return new Definition { IsSeed = typeId <= 500064, NameCN = "果实", NameEN = "fruit" };
            return null;
        }
    }
    public static class RaidMealService { public static bool RegisterMeal(int typeId) { throw new InvalidOperationException("New consumption must not register old meals"); } }
}
