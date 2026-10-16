using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace UnityEngine
{
    public class Object
    {
        private bool destroyed;
        public static bool operator ==(Object a,Object b) { bool an=ReferenceEquals(a,null)||a.destroyed,bn=ReferenceEquals(b,null)||b.destroyed;return an||bn?an==bn:ReferenceEquals(a,b); }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public override bool Equals(object value) { return ReferenceEquals(this,value); }
        public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }
        public static void Destroy(Object value)
        {
            if(ReferenceEquals(value,null))return;value.destroyed=true;
            var go=value as GameObject;if(!ReferenceEquals(go,null))go.DestroyComponents();
            var component=value as Component;if(!ReferenceEquals(component,null)&&component.gameObject!=null)component.gameObject.Remove(component);
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T:class { return gameObject!=null?gameObject.GetComponent<T>():null; }
        public T GetComponentInParent<T>() where T:class { return GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool includeInactive) where T:class { return GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { }
    public sealed class GameObject : Object
    {
        public bool activeSelf=true;
        public readonly Transform transform=new Transform();
        public void SetActive(bool value) { activeSelf=value; }
        private readonly Dictionary<Type,Component> components=new Dictionary<Type,Component>();
        public T GetComponent<T>() where T:class { Component c;return components.TryGetValue(typeof(T),out c)?c as T:null; }
        public T AddComponent<T>() where T:Component,new() { var c=new T { gameObject=this };components[typeof(T)]=c;return c; }
        internal void Attach(Component c) { components[c.GetType()]=c; }
        internal void DestroyComponents() { foreach(var c in new List<Component>(components.Values))Object.Destroy(c);components.Clear(); }
        internal void Remove(Component c) { components.Remove(c.GetType()); }
    }
    public static class Time { public static float unscaledTime,realtimeSinceStartup; }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } public static Vector3 forward { get { return new Vector3(0,0,1); } } }
    public struct Quaternion { public float x,y,z,w; }
    public sealed class Transform { public Vector3 position,localScale;public Quaternion rotation;public void SetPositionAndRotation(Vector3 p,Quaternion q) { position=p;rotation=q; } }
    public static class Debug { public static void LogWarning(object message) { } }
}
public struct DamageInfo { public CharacterMainControl fromCharacter;public float damageValue; }
public sealed class CharacterMainControl : UnityEngine.Component
{
    public readonly Health Health;internal string PlayerId;internal int Ai;
    internal CharacterMainControl(float health=10f,float armor=0f)
    { gameObject=new UnityEngine.GameObject();gameObject.Attach(this);Health=new Health { gameObject=gameObject,Character=this,Armor=armor };gameObject.Attach(Health);Health.CurrentHealth=health; }
}
public sealed class Health : UnityEngine.Component
{
    internal CharacterMainControl Character;internal float Armor;internal bool Throw;internal Action Nested;
    private float current;
    public float CurrentHealth
    { [MethodImpl(MethodImplOptions.NoInlining)] get { return current; }
      [MethodImpl(MethodImplOptions.NoInlining)] set { current=value; } }
    public bool IsDead { get { return CurrentHealth<=0f; } }
    public CharacterMainControl TryGetCharacter() { return Character; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool Hurt(DamageInfo damageInfo)
    {
        if(IsDead)return false;
        float amount=Math.Max(1f,damageInfo.damageValue*2f/(Armor+2f));
        CurrentHealth-=amount;
        if(Nested!=null)Nested();
        if(Throw)throw new InvalidOperationException("hurt");
        return true;
    }
}
namespace BossRush
{
    internal sealed class ModBehaviour { internal static void DevLog(string text) { } }
    internal abstract class BossRushRuntimeModuleBase
    { public abstract string ModuleName { get; } public virtual void OnAwake(ModBehaviour owner) { } public virtual void OnUpdate(float a,float b) { } public virtual void OnDestroy() { } }
    internal sealed class BossRushCoopMessage { internal bool IsFromHost;internal object Value;internal T Read<T>() { return BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(Value)); } }
    internal static class BossRushCoop
    {
        internal static bool IsHost,IsConnected=true,SessionReady=true,HasBoundScene;
        internal static string SceneToken;
        internal static bool IsClient { get { return IsConnected&&!IsHost; } }
        internal static bool HasWorldAuthority { get { return !IsConnected||IsHost; } }
        internal static string LocalPlayerId="local",RunId="room";internal static int SceneGeneration=1;
        internal static readonly Dictionary<int,CharacterMainControl> Ai=new Dictionary<int,CharacterMainControl>();
        internal static object Last;internal static Action<BossRushCoopMessage> Handler;
        internal static event Action<int,CharacterMainControl> AiSpawned;
        internal static void RaiseAi(int id,CharacterMainControl character) { if(AiSpawned!=null)AiSpawned(id,character); }
        internal static bool HasAiSubscriber { get { return AiSpawned!=null; } }
        internal static string GetPlayerId(CharacterMainControl c) { return c!=null?c.PlayerId:null; }
        internal static int GetAiId(CharacterMainControl c) { return c!=null?c.Ai:0; }
        internal static CharacterMainControl FindAi(int id) { CharacterMainControl c;return Ai.TryGetValue(id,out c)?c:null; }
        internal static void Broadcast<T>(string topic,T value) { Last=value; }
        internal static IDisposable Register(string topic,Action<BossRushCoopMessage> callback) { Handler=callback;return new Token(callback); }
        private sealed class Token : IDisposable { private Action<BossRushCoopMessage> callback;internal Token(Action<BossRushCoopMessage> c) { callback=c; } public void Dispose() { if(Handler==callback)Handler=null; } }
    }
    internal static class CoopAiPredictionFxCounters { internal static int Spawned; }
    internal sealed class SandstormOrb:UnityEngine.Component { internal static SandstormOrb Spawn(object owner,UnityEngine.Vector3 p,UnityEngine.Vector3 v,float damage,bool variant,bool replica) { CoopAiPredictionFxCounters.Spawned++;return new SandstormOrb {gameObject=new UnityEngine.GameObject()}; } }
    internal sealed class SandstormCycloneSeed:UnityEngine.Component { internal static SandstormCycloneSeed Spawn(object owner,UnityEngine.Vector3 p,UnityEngine.Vector3 v,bool variant,bool replica) { CoopAiPredictionFxCounters.Spawned++;return new SandstormCycloneSeed {gameObject=new UnityEngine.GameObject()}; } }
    internal sealed class SandstormTornado:UnityEngine.Component { internal static SandstormTornado Spawn(object owner,UnityEngine.Vector3 p,bool variant,bool replica) { CoopAiPredictionFxCounters.Spawned++;return new SandstormTornado {gameObject=new UnityEngine.GameObject()}; } }
    internal sealed class SandstormChampionBody:UnityEngine.Component { internal static SandstormChampionBody Attach(CharacterMainControl character,float scale,bool minion) {return new SandstormChampionBody {gameObject=new UnityEngine.GameObject()};}internal void ReadCoopState(BossRushCoopBossState state) {} }
    internal static class ModeGRuntimeGates { internal static bool IsModeGStagingBarrierActive;internal static bool IsModeGStagingHealthBlocked(Health health) { return false; } }
    internal static class ReverseScaleAbilityManager { internal static bool IsPostTriggerInvincible(Health h) { return false; }internal static bool TryPrepareLethalProtectionDuringHurt(Health h) { return false; } }
    internal sealed class ReverseScaleConfig { internal static readonly ReverseScaleConfig Instance=new ReverseScaleConfig();internal float TriggerHealthThreshold=1f; }
    internal sealed class DragonKingAbilityController { internal bool ShouldClampLethalHealthDuringHurt() { return false; } }
    internal static class DragonKingConfig { internal const float ChildProtectionHealthThreshold=1f; }
    internal sealed class DragonDescendantAbilityController { internal bool ShouldClampLethalHealthDuringHurt() { return false; } }
    internal static class PetNestCompanionAgent { internal static bool IsCompanionArmed;internal static bool IsCompanionHealth(Health h) { return false; }internal static void NotifyLethalClamped(Health h) { } }
    internal static class PetNestDownedHandler { internal static void NotifyLethalClamped(Health h) { } }
}

// 原生联机 HealthPatch 的两个确定行为：打前传原伤害；预测死亡额外传99999。
// 不链接/调用游戏进程，替身只负责输送该原生边界，保护与 setter 链均链接完整生产文件。
[HarmonyPatch(typeof(Health),nameof(Health.Hurt))]
internal static class NativePredictionFixturePatch
{
    internal static readonly List<float> Reports=new List<float>();internal static bool NativeDead;
    [HarmonyPrefix] private static void Prefix(Health __instance,DamageInfo damageInfo)
    { if(BossRush.BossRushCoop.IsClient&&damageInfo.fromCharacter!=null&&damageInfo.fromCharacter.PlayerId=="local")Reports.Add(damageInfo.damageValue); }
    [HarmonyPostfix] private static void Postfix(Health __instance)
    { if(BossRush.BossRushCoop.IsClient&&__instance.IsDead&&!NativeDead)Reports.Add(99999f); }
}
