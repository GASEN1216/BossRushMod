using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object o) { return !object.ReferenceEquals(o, null); } }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float a, float b) { x = a; y = b; }
        public static Vector2 zero { get { return new Vector2(); } }
        public static Vector2 operator *(Vector2 a, float b) { return new Vector2(a.x * b, a.y * b); }
        public static implicit operator Vector3(Vector2 a) { return new Vector3(a.x, 0, a.y); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public Vector3 normalized { get { return this * (1f / (float)Math.Sqrt(Math.Max(sqrMagnitude, 0.00001f))); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static float Distance(Vector3 a, Vector3 b) { return (float)Math.Sqrt((a - b).sqrMagnitude); }
    }
    public struct Color { }
    public class Transform : Object { public Vector3 position; }
    public class GameObject : Object
    {
        public string name = "Probe";
        public bool activeSelf = true;
        public Transform transform = new Transform();
        private Dictionary<Type, MonoBehaviour> parts = new Dictionary<Type, MonoBehaviour>();
        public T GetComponent<T>() where T : MonoBehaviour { MonoBehaviour p; return parts.TryGetValue(typeof(T), out p) ? (T)p : null; }
        public T AddComponent<T>() where T : MonoBehaviour, new() { T p = new T(); p.gameObject = this; parts[typeof(T)] = p; return p; }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject;
        public bool enabled = true;
        public bool isActiveAndEnabled { get { return enabled && gameObject != null && gameObject.activeSelf; } }
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : MonoBehaviour { return gameObject.GetComponent<T>(); }
    }
    public static class Time { public static float time; }
    public static class Random
    {
        public static Vector2 insideUnitCircle { get { return new Vector2(1, 0); } }
        public static float Range(float a, float b) { return a + (b - a) * 0.5f; }
    }
    public static class Mathf
    {
        public static float Sqrt(float a) { return (float)Math.Sqrt(a); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Clamp(float v, float a, float b) { return Math.Max(a, Math.Min(v, b)); }
    }
}
namespace Pathfinding
{
    public class AstarPath { public static AstarPath active = new AstarPath(); }
    public class Path { public bool error; public List<Vector3> vectorPath; }
    public delegate void OnPathDelegate(Path p);
    public class Seeker : MonoBehaviour
    {
        public class Request { public Vector3 Target; public OnPathDelegate Callback; public bool Cancelled; }
        public readonly List<Request> Requests = new List<Request>();
        public Request Pending;
        public bool ThrowOnCancel;
        public int Cancels;
        public void CancelCurrentPathRequest(bool pool)
        {
            Cancels++;
            if (ThrowOnCancel) throw new Exception("injected cancellation failure");
            if (Pending != null) Pending.Cancelled = true;
            Pending = null;
        }
        public void StartPath(Vector3 from, Vector3 to, OnPathDelegate callback)
        {
            Pending = new Request { Target = to, Callback = callback };
            Requests.Add(Pending);
        }
        public void Deliver(int index, bool error = false)
        {
            Request request = Requests[index];
            if (Pending == request) Pending = null;
            // Deliberately permit successful completion even after cancel (already queued callback).
            request.Callback(new Path { error = error, vectorPath = new List<Vector3> { Vector3.zero, request.Target } });
        }
    }
}
public class Movement { public Vector3 CurrentMoveDirectionXZ; }
public enum Teams { player, wolf }
public class CharacterRandomPreset : UnityEngine.Object { public float aiCombatFactor = 1f; }
public class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main;
    public bool IsMainCharacter = true;
    public Transform transform = new Transform();
    public Vector3 LastMove;
    public Health Health;
    public Movement movementControl = new Movement();
    public CharacterRandomPreset characterPreset = new CharacterRandomPreset();
    public Item Helmet, Armor;
    public T GetComponent<T>() where T : class { return null; }
    public void SetMoveInput(Vector3 value) { LastMove = value; }
    public void SetAimPoint(Vector3 value) { }
    public void SetPosition(Vector3 value) { transform.position = value; }
    public Item GetHelmatItem() { return Helmet; }
    public Item GetArmorItem() { return Armor; }
}
public class Item : UnityEngine.Object
{
    private float durability = 10;
    public Action<float> OnDurability;
    public float Durability { get { return durability; } set { durability = value; if (OnDurability != null) OnDurability(value); } }
    public int GetInt(string key, int fallback) { return fallback; }
}
public class Buff : UnityEngine.Object { public Action<Health> Apply; }
public class DamageReceiver { }
public class MultiSceneCore { public static MultiSceneCore Instance; public bool IsLoading; }
public class RuleData { public bool AdvancedDebuffMode; public float DamageFactor_ToPlayer = 1f; }
public class LevelManager
{
    public static LevelManager Instance = new LevelManager();
    public static RuleData Rule = new RuleData();
    public bool IsBaseLevel;
    public bool IsRaidMap = true;
    public CharacterMainControl MainCharacter;
}
public static class EXPManager { public static void AddExp(int value) { } }
public static class UniTaskExtensions { public static void Forget(object value) { } }
public class UnityEvent<T> { public Action<T> Callback; public void Invoke(T value) { if (Callback != null) Callback(value); } }
public class PopText : UnityEngine.Object
{
    public static PopText instance;
    public static void Pop(string text, Vector3 position, Color color, float size, object sprite) { }
}
public static class GameplayDataSettings
{
    public class BuffsData { public Buff BoneCrackBuff, WoundBuff, UnlimitBleedBuff, BleedSBuff; }
    public static BuffsData Buffs = new BuffsData();
    public class UIStyleData
    {
        public class DisplayElementDamagePopTextLook { public float critSize, normalSize; public Color color; }
        public object CritPopSprite;
        public DisplayElementDamagePopTextLook GetElementDamagePopTextLook(ElementTypes type) { return new DisplayElementDamagePopTextLook(); }
    }
    public static UIStyleData UIStyle = new UIStyleData();
}
public partial class Health : MonoBehaviour
{
    public bool invincible, isDead, isZombie, CanDieIfNotRaidMap = true, Hidden = true;
    public bool IsMainCharacterHealth { get { return Owner != null && LevelManager.Instance.MainCharacter == Owner; } }
    public bool IsDead { get { return isDead; } }
    public float CurrentHealth = 1000, BodyArmor, HeadArmor;
    public Teams team = Teams.player;
    public Item item;
    public CharacterMainControl Owner;
    public readonly Dictionary<ElementTypes, float> Resistance = new Dictionary<ElementTypes, float>();
    public Func<ElementTypes, float> BeforeElement;
    public UnityEvent<DamageInfo> OnDeadEvent, OnHurtEvent;
    public static Action<Health, DamageInfo> OnDead, OnHurt;
    public float ElementFactor(ElementTypes type)
    {
        if (BeforeElement != null) return BeforeElement(type);
        float value; return Resistance.TryGetValue(type, out value) ? value : 1f;
    }
    public CharacterMainControl TryGetCharacter() { return Owner; }
    public void AddBuff(Buff buff, CharacterMainControl from, int weapon) { if (buff != null && buff.Apply != null) buff.Apply(this); }
    public void SetHealth(float value) { CurrentHealth = value; }
    public object DestroyOnDelay() { return null; }
}
namespace BossRush
{
    public partial class ModBehaviour : UnityEngine.Object
    {
        public static ModBehaviour Instance;
        public bool frostSetActive, thunderSetActive;
        public static readonly List<string> Logs = new List<string>();
        public static void DevLog(string value) { Logs.Add(value); }
        public static void CriticalLog(string key, string value) { Logs.Add(key + ": " + value); }
        public static float ReadPortion(Health h, DamageInfo info, ElementTypes element) { return GetSetBonusElementDamagePortion(h, info, element); }
    }
}
namespace BossRush.Utils
{
    public interface INPCController { }
    public static class NPCHeartBubbleHelper
    {
        public static void ShowLoveHeart(Transform t, float a, float b, float c, string s) { }
        public static void ShowBrokenHeart(Transform t, float a, float b, float c, string s) { }
    }
}
