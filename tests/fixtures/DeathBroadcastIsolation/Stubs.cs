// 宿主替身：只覆盖官方 Health.Hurt 与 CharacterMainControl.OnDead 方法体用到的成员。
// UnityEvent 替身按本机 UnityEngine.CoreModule 的真实成员形态写（PrepareInvoke 为 internal、
// BaseInvokableCall.Invoke(object[]) 为 public abstract，Invoke 不带 try），真实形态另由 OfficialCheck 在现装 DLL 上核对。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static implicit operator bool(Object o) { return o != null; }
        public static bool operator ==(Object a, Object b)
        { bool an = ReferenceEquals(a, null) || a.Destroyed; bool bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an && bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } }
    public struct Color { }
    public class SerializeField : Attribute { }
    public class Transform : Object { public Vector3 position; public Quaternion rotation; }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        public Transform transform = new Transform();
        public void SetActive(bool value) { activeSelf = value; }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject = new GameObject();
        public Transform transform { get { return gameObject.transform; } }
    }
    public static class Random { public static float Range(float a, float b) { return a + (b - a) * 0.5f; } }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Clamp(float v, float a, float b) { return Math.Max(a, Math.Min(v, b)); }
    }
    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void LogWarning(object message) { Warnings.Add(message == null ? "" : message.ToString()); }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction<T0>(T0 arg0);

    internal abstract class BaseInvokableCall
    {
        public abstract void Invoke(object[] args);
    }

    internal class InvokableCall<T1> : BaseInvokableCall
    {
        internal readonly UnityAction<T1> Action;
        public InvokableCall(UnityAction<T1> action) { Action = action; }
        public override void Invoke(object[] args) { Invoke((T1)args[0]); }
        public virtual void Invoke(T1 args0) { Action(args0); }
    }

    public abstract class UnityEventBase
    {
        private readonly List<BaseInvokableCall> runtimeCalls = new List<BaseInvokableCall>();
        private readonly List<BaseInvokableCall> executingCalls = new List<BaseInvokableCall>();
        private bool needsUpdate = true;
        internal int PrepareCount;

        internal void AddCall(BaseInvokableCall call) { runtimeCalls.Add(call); needsUpdate = true; }
        internal void RemoveCall(Predicate<BaseInvokableCall> match) { runtimeCalls.RemoveAll(match); needsUpdate = true; }

        internal List<BaseInvokableCall> PrepareInvoke()
        {
            PrepareCount++;
            if (needsUpdate)
            {
                executingCalls.Clear();
                executingCalls.AddRange(runtimeCalls);
                needsUpdate = false;
            }
            return executingCalls;
        }
    }

    public class UnityEvent<T0> : UnityEventBase
    {
        public void AddListener(UnityAction<T0> call) { AddCall(new InvokableCall<T0>(call)); }
        public void RemoveListener(UnityAction<T0> call)
        { RemoveCall(c => { var typed = c as InvokableCall<T0>; return typed != null && typed.Action == call; }); }

        // 与 Unity 一致：逐个调用、不 catch，前一个监听器抛异常会截断后面的。
        public void Invoke(T0 arg0)
        {
            List<BaseInvokableCall> calls = PrepareInvoke();
            for (int i = 0; i < calls.Count; i++)
            {
                var typed = calls[i] as InvokableCall<T0>;
                if (typed != null) typed.Invoke(arg0);
                else calls[i].Invoke(new object[] { arg0 });
            }
        }
    }
}

public enum Teams { player, scav, wolf }
public class CharacterRandomPreset : UnityEngine.Object { public float aiCombatFactor = 1f; public string nameKey = ""; }
public class Item : UnityEngine.Object
{
    public float Durability = 10f;
    public int GetInt(string key, int fallback) { return fallback; }
}
public class Buff : UnityEngine.Object { }
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
public static class EXPManager { public static bool AddExp(int value) { return true; } }
public static class UniTaskExtensions { public static void Forget(object value) { } }
public class PopText : UnityEngine.Object { public static PopText instance; public static void Pop(string t, Vector3 p, Color c, float s, object sprite) { } }
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
public static class SavesCounter { public static void AddKillCount(string key) { } }
public class Inventory { public bool NeedInspection; }
public class InteractableLootbox
{
    public static int Created;
    public bool needInspect;
    public Inventory Inventory = new Inventory();
    public static InteractableLootbox CreateFromItem(Item item, Vector3 position, Quaternion rotation, bool moveToGround, object prefab, bool isMain)
    { Created++; return new InteractableLootbox(); }
}
public class LevelConfig : UnityEngine.Object { public static LevelConfig Instance; public bool NeedInspectDeadLootBox; }
public static class SetActiveByPlayerDistance { public static int Unregistered; public static void Unregister(GameObject go, int scene) { Unregistered++; } }
public class CharacterModel : UnityEngine.Object { public Transform transform = new Transform(); }

public partial class CharacterMainControl : MonoBehaviour
{
    public bool IsMainCharacter;
    public CharacterRandomPreset characterPreset = new CharacterRandomPreset();
    public CharacterModel characterModel;
    public Item characterItem = new Item();
    public object deadLootBoxPrefab;
    public bool dropBoxOnDead = true;
    public int relatedScene = 1;
    private bool hasHandledDeath;
    public event Action<DamageInfo> BeforeCharacterSpawnLootOnDead;
    public Item GetHelmatItem() { return null; }
    public Item GetArmorItem() { return null; }
    public void Die(DamageInfo info) { OnDead(info); }
}

public partial class Health : MonoBehaviour
{
    public bool invincible, isDead, isZombie, CanDieIfNotRaidMap = true, Hidden = true;
    public bool IsMainCharacterHealth { get { return false; } }
    public bool IsDead { get { return isDead; } }
    public float CurrentHealth { get; set; }
    public float BodyArmor, HeadArmor;
    public Teams team = Teams.scav;
    public Item item;
    public CharacterMainControl Owner;
    public UnityEvent<DamageInfo> OnDeadEvent = new UnityEvent<DamageInfo>();
    public UnityEvent<DamageInfo> OnHurtEvent = new UnityEvent<DamageInfo>();
    public static event Action<Health, DamageInfo> OnDead;
    public static event Action<Health, DamageInfo> OnHurt;
    public float ElementFactor(ElementTypes type) { return 1f; }
    public CharacterMainControl TryGetCharacter() { return Owner; }
    public void AddBuff(Buff buff, CharacterMainControl from, int weapon) { }
    public void SetHealth(float value) { CurrentHealth = value; }
    public object DestroyOnDelay() { DestroyScheduled = true; return null; }
    public bool DestroyScheduled;
    internal static void ClearStaticSubscribers() { OnDead = null; OnHurt = null; }
}
