using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public class Object { public static void Destroy(Object value) { } }
    public class Component:Object { public GameObject gameObject; public Transform transform { get { return gameObject.transform; } } public T GetComponentInParent<T>() where T:class { return gameObject.Character as T; } public T GetComponent<T>() where T:class { return typeof(T)==typeof(Rigidbody)?gameObject.Body as T:gameObject.Receiver as T; } }
    public enum ForceMode { Impulse }
    public sealed class Rigidbody { internal int Impulses;public void AddForce(Vector3 velocity,ForceMode mode) { Impulses++; } }
    public sealed class Coroutine { }
    public class MonoBehaviour:Component { public bool enabled=true; protected Coroutine StartCoroutine(System.Collections.IEnumerator routine) { return new Coroutine(); } protected void StopCoroutine(Coroutine routine) { } }
    public sealed class GameObject:Object
    {
        public bool activeInHierarchy=true; public readonly Transform transform; public CharacterMainControl Character;public DamageReceiver Receiver;public Rigidbody Body=new Rigidbody();
        public readonly List<Collider> Colliders=new List<Collider>(); public GameObject() { transform=new Transform(this); }
    }
    public sealed class Transform
    {
        internal readonly GameObject Owner;public Vector3 position;public Transform parent;public readonly List<Transform> Children=new List<Transform>();
        internal Transform(GameObject owner) { Owner=owner; }
        public Transform root { get { return parent!=null?parent.root:this; } }
        public bool IsChildOf(Transform target) { return this==target || parent!=null && parent.IsChildOf(target); }
        public T[] GetComponentsInChildren<T>(bool inactive) where T:class
        { var values=new List<T>();foreach(var c in Owner.Colliders)if(c is T)values.Add(c as T);foreach(var child in Children)values.AddRange(child.GetComponentsInChildren<T>(inactive));return values.ToArray(); }
    }
    public struct Vector3
    {
        public float x,y,z;public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 up { get { return new Vector3(0,1,0); } }
        public static Vector3 zero { get { return new Vector3(); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public Vector3 normalized { get { var length=(float)Math.Sqrt(sqrMagnitude);return length>0?this/length:new Vector3(); } }
        public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a,float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public static Vector3 operator /(Vector3 a,float b) { return a*(1/b); }
    }
    public struct Color { }
    public struct Bounds { public float SqrDistance(Vector3 value) { return 0; } }
    public struct RaycastHit { public Collider collider;public Vector3 point; }
    public static class Mathf { public static float Sqrt(float value) { return (float)Math.Sqrt(value); } public static float Max(float a,float b) { return Math.Max(a,b); } }
    public sealed class Collider:Component { public bool enabled=true;public Vector3 ClosestPoint(Vector3 center) { return transform.position; } }
    public static class Time { public static float time,unscaledTime; }
    public enum QueryTriggerInteraction { Collide }
    public static class Physics
    {
        internal static Collider[] Hits=new Collider[0];internal static int Queries;
        public static int OverlapSphereNonAlloc(Vector3 center,float radius,Collider[] buffer,int mask,QueryTriggerInteraction triggers=QueryTriggerInteraction.Collide) { Queries++;int count=Math.Min(buffer.Length,Hits.Length);Array.Copy(Hits,buffer,count);return count; }
        public static Collider[] OverlapSphere(Vector3 center,float radius,int mask,QueryTriggerInteraction triggers=QueryTriggerInteraction.Collide) { Queries++;return (Collider[])Hits.Clone(); }
        public static bool Raycast(Vector3 origin,Vector3 direction,out RaycastHit hit,float distance) { hit=default(RaycastHit);return false; }
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; }
    public static class SceneManager { public static int Handle=1; public static Scene GetActiveScene() { return new Scene { handle=Handle }; } }
}
public enum DamageTypes { normal }
public enum ElementTypes { fire,ghost }
public enum Teams { player,wolf,middle }
public static class Team { public static bool IsEnemy(Teams a,Teams b) { return a!=b && a!=Teams.middle && b!=Teams.middle; } }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;public float damageValue,crit;public int fromWeaponItemID;public DamageTypes damageType;public Vector3 damagePoint,damageNormal;
    public DamageInfo(CharacterMainControl actor) { this=default(DamageInfo);fromCharacter=actor; }
    public void AddElementFactor(ElementTypes type,float value) { }
}
public sealed class Health
{
    public bool IsDead;internal CharacterMainControl Character;internal int Hits;internal float Damage;
    public void Hurt(DamageInfo damage) { Hits++;Damage+=damage.damageValue; }
    public CharacterMainControl TryGetCharacter() { return Character; }
}
public sealed class DamageReceiver { public Health health;public Teams Team { get { return health.Character.Team; } } public Transform transform { get { return health.Character.transform; } }public int GetInstanceID() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }public void Hurt(DamageInfo damage) { health.Hurt(damage); } }
public sealed class CharacterMainControl:Component
{
    public DamageReceiver mainDamageReceiver { get { return gameObject.Receiver; } }
    public static CharacterMainControl Main; public readonly Health Health;public bool IsMainCharacter;public Teams Team=Teams.player;internal int Buffs;
    public CharacterMainControl() { gameObject=new GameObject { Character=this };Health=new Health { Character=this };gameObject.Receiver=new DamageReceiver { health=Health };gameObject.Colliders.Add(new Collider { gameObject=gameObject }); }
    public void AddBuff(Duckov.Buffs.Buff buff,CharacterMainControl source,int sourceId) { Buffs++; }
    internal int Pushes;internal Vector3 LastPush;public void SetForceMoveVelocity(Vector3 velocity) { Pushes++;LastPush=velocity; }
    internal void Parent(Transform parent) { transform.parent=parent;parent.Children.Add(transform); }
}
namespace Duckov.Buffs { public sealed class Buff { } }
namespace BossRush
{
    internal static class CoopAiPredictionHealthProtection
    {
        internal static readonly HashSet<CharacterMainControl> Denied = new HashSet<CharacterMainControl>();
        internal static bool HasEntityAuthority(CharacterMainControl actor) { return actor != null && !Denied.Contains(actor) && BossRushCoop.HasWorldAuthority; }
    }
    internal sealed class BossRushCoopPlayer { internal bool IsAlive=true,IsConnected=true;internal string Id,AssetId="asset";internal CharacterMainControl Character;internal bool IsLocal { get { return Id==BossRushCoop.LocalPlayerId; } } }
    internal sealed class BossRushCoopMessage { internal string SenderId,Json;internal bool IsFromHost;internal T Read<T>() { return BossRushCoopJson.FromJson<T>(Json); } }
    internal abstract class BossRushRuntimeModuleBase { public abstract string ModuleName { get; }public virtual void OnAwake(ModBehaviour owner) { }public virtual void OnUpdate(float delta,float unscaled) { }public virtual void OnDestroy() { } }
    internal sealed class CallbackLease:IDisposable { private readonly Action close;internal CallbackLease(Action close) { this.close=close; }public void Dispose() { close(); } }
    internal static class BossRushCoop
    {
        internal static bool IsConnected=true,IsHost=true;internal static bool HasWorldAuthority { get { return !IsConnected || IsHost; } }
        internal static bool IsClient { get { return IsConnected && !IsHost; } }
        internal static string RunId="room",LocalPlayerId="host",LocalAssetId="asset";internal static int SceneGeneration=1;
        internal static event Action SessionChanged;
        internal sealed class Sent { internal string Sender,Target,Topic,Json; }
        internal static readonly List<Sent> Outbox=new List<Sent>();
        internal static readonly Dictionary<string,Action<BossRushCoopMessage>> Handlers=new Dictionary<string,Action<BossRushCoopMessage>>();
        internal static IDisposable Register(string topic,Action<BossRushCoopMessage> callback) { string key=LocalPlayerId+":"+topic;Handlers[key]=callback;return new CallbackLease(()=>Handlers.Remove(key)); }
        internal static void SendToHost<T>(string topic,T value) { Outbox.Add(new Sent { Sender=LocalPlayerId,Target="host",Topic=topic,Json=BossRushCoopJson.ToJson(value) }); }
        internal static void SendToPlayer<T>(string target,string topic,T value) { Outbox.Add(new Sent { Sender=LocalPlayerId,Target=target,Topic=topic,Json=BossRushCoopJson.ToJson(value) }); }
        internal static void Emit(Sent value,bool host) { Handlers[value.Target+":"+value.Topic](new BossRushCoopMessage { SenderId=value.Sender,Json=value.Json,IsFromHost=host }); }
        internal static BossRushCoopPlayer FindPlayer(string id) { foreach(var player in Players)if(player.Id==id)return player;return null; }
        internal static string GetPlayerId(CharacterMainControl target) { foreach(var player in Players)if(player.Character==target)return player.Id;return null; }
        internal static bool AnyPlayerAlive { get { foreach(var p in Players)if(p.IsAlive)return true;return false; } }
        internal static readonly List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>();
        internal static bool IsPlayer(CharacterMainControl actor) { foreach(var p in Players)if(p.Character==actor)return true;return false; }
        internal static CharacterMainControl GetCombatTarget(Vector3 at) { foreach(var p in Players)if(p.IsAlive)return p.Character;return null; }
    }
    internal static class GameplayDataSettings { internal static class Layers { internal const int damageReceiverLayerMask=1; } }
    internal static class BossSkillDamageRules { internal static readonly HashSet<CharacterMainControl> Dodging=new HashSet<CharacterMainControl>();internal static bool IsDodging(CharacterMainControl actor) { return Dodging.Contains(actor); }internal static float ResolveGunDamageScale(CharacterMainControl actor,float baseline) { return 2f; } }
    internal static class DragonKingConfig { internal const float CollisionRadius=1.5f,CollisionCooldown=.5f,SunDanceDamagePerTick=4f,DamageMultiplier=1f,DamagePointHeightOffset=1f,PlayerTargetHeightOffset=1f,ProjectileHitRadius=1f,EtherealLanceDamage=4f;internal const string Sound_Hit="hit",Sound_BoltHit="hit"; }
    internal static class DragonKingFxShared { internal static void HitBurst(Vector3 at,Color color) { } }
    internal sealed class ModBehaviour { internal static ModBehaviour Instance;internal bool IsModeEActive;internal static void DevLog(string value) { }internal void PlaySoundEffect(string sound) { } }
    internal static class ModeHRuntimeGates { internal static bool IsModeHRunOwnerActive; }
    internal static class DragonDescendantConfig { internal const float CollisionDamage=4, Phase2DamageMultiplier=1; }
    public sealed partial class DragonDescendantAbilityController
    {
        private readonly CharacterMainControl bossCharacter=new CharacterMainControl { Team=Teams.wolf };
        internal CharacterMainControl BossForTest { get { return bossCharacter; } }
        internal void CollideForTest(CharacterMainControl target) { ApplyCollisionDamage(target); }
    }
    internal sealed partial class PhantomWitchAbilityController { }
    internal static class FenHuangHalberdRuntime { internal const int DamageReceiverLayerMask=1;internal static DamageReceiver TryGetDamageReceiver(Collider col) { return col.gameObject.Receiver; } }
    internal static class PhantomWitchAssetManager { internal static Duckov.Buffs.Buff GetCurseBuff() { return new Duckov.Buffs.Buff(); } }
    internal static class PhantomWitchCurseSweatVfx { internal static void TryAttach(GameObject target) { } }
    internal sealed partial class PhantomWitchBossCurseRealmRuntime
    {
        private Collider[] damageHitBuffer=new Collider[32];private readonly HashSet<int> processedReceiverIds=new HashSet<int>();
        private Vector3 origin;private float Radius=5,damagePerTick=4;private int weaponTypeId=123;private readonly CharacterMainControl caster=new CharacterMainControl { Team=Teams.wolf };
        internal void Pulse() { DealRealmDamage(); }
        internal CharacterMainControl CasterForTest { get { return caster; } }
    }
    internal enum DragonKingPhase { Active,Dead,Transitioning }
    public sealed partial class DragonKingAbilityController
    {
        internal static bool Faction; internal static bool IsFactionTargetMode() { return Faction; }
        internal CharacterMainControl CombatContactTarget { get { return CharacterMainControl.Main; } }
        private readonly CharacterMainControl bossCharacter=new CharacterMainControl { Team=Teams.wolf };private CharacterMainControl playerCharacter;
        internal CharacterMainControl BossForTest { get { return bossCharacter; } }
        private readonly Dictionary<CharacterMainControl,float> coopCollisionDamageTimes=new Dictionary<CharacterMainControl,float>();
        internal DragonKingPhase CurrentPhase=DragonKingPhase.Active;private DragonKingCollisionDetector collisionDetector;private float lastCollisionDamageTime;
        internal readonly List<CharacterMainControl> ContactHits=new List<CharacterMainControl>();private void ApplyCollisionDamage(CharacterMainControl target) { ContactHits.Add(target); }
        private readonly string coopContactId=Guid.NewGuid().ToString("N");private long coopContactSequence;
        private void OnPlayerDeath() { }private void PlayHurtSound() { }
        internal void Knock(CharacterMainControl target) { ApplyKnockback(target); }
        public void ApplySunBeamDamage() { ApplyDamageToPlayer(DragonKingConfig.SunDanceDamagePerTick,CombatContactTarget); }
        private static readonly Color BoltHitColor=new Color(),LanceHitColor=new Color();
        private bool IsPlayerDead() { return playerCharacter==null || playerCharacter.Health.IsDead; }
        private void ApplyDamageToPlayer(float damage) { ApplyDamageToPlayer(damage,playerCharacter); }
        private static bool TryGetSharedTargetHitboxBounds(CharacterMainControl target,out Bounds bounds) { bounds=new Bounds();return true; }
        internal bool Projectile(Vector3 at,bool all=false) { return CheckProjectileHit(at,4,false,new Vector3(),all); }
        internal bool Lance(GameObject lance) { return CheckLanceHit(lance,false,null,new Vector3()); }
    }
    internal sealed partial class DragonKingLavaZone:MonoBehaviour
    {
        private const float PLAYER_COLLIDER_REFRESH_INTERVAL=1f;private static readonly Collider[] emptyPlayerColliders=new Collider[0];
        private static readonly List<DragonKingLavaZone> activeZones=new List<DragonKingLavaZone>();
        private static readonly Dictionary<CharacterMainControl,PlayerZoneGeometry> coopGeometry=new Dictionary<CharacterMainControl,PlayerZoneGeometry>();
        private readonly Dictionary<CharacterMainControl,float> coopDamageTimes=new Dictionary<CharacterMainControl,float>();
        private static CharacterMainControl cachedMainPlayer;private static Transform cachedMainPlayerRoot;private static Collider[] cachedMainPlayerColliders=emptyPlayerColliders;private static float lastPlayerColliderRefreshTime=float.NegativeInfinity;
        private static Collider[] enemyZoneHits=new Collider[64];private static readonly List<CharacterMainControl> enemyZoneVictims=new List<CharacterMainControl>();
        private static Duckov.Buffs.Buff cachedBurnBuff=new Duckov.Buffs.Buff();private CharacterMainControl bossCharacter;
        private sealed class DragonKingLavaZoneUpdater:MonoBehaviour { }
        private static DragonKingLavaZoneUpdater updater;
        private bool isInitialized=true;private float damage=5,damageInterval=.2f,duration=10,radius=2,createTime,lastDamageTime;
        internal DragonKingLavaZone() { gameObject=new GameObject();bossCharacter=new CharacterMainControl { Team=Teams.wolf };activeZones.Add(this); }
        internal CharacterMainControl BossForTest { get { return bossCharacter; } }
        internal static int GeometryCount { get { return coopGeometry.Count; } }
        internal static void Pulse() { UpdateActiveZones(); }
        internal void Close() { UnregisterZone(this);ResetRuntimeState(); }
        internal void ResetCooldown() { coopDamageTimes.Clear();lastDamageTime=0; }
    }
    internal sealed partial class DragonKingShockwaveEffect : MonoBehaviour
    {
        private bool waveConnected,waveHost,waveRevoked;private string waveRoom,waveAsset;private CharacterMainControl waveBody;private int waveScene;
        private float knockbackForce=15,knockbackUpwardForce=8;private Coroutine knockbackClearCoroutine;private static readonly object knockbackClearTime=new object();
        private CharacterMainControl playerCharacter;private Transform cachedPlayerTransform;
        internal CharacterMainControl LocalTarget() { FindPlayer();return playerCharacter; }
        internal void BindScopeForTest() { CaptureWaveScope();FindPlayer(); }
        internal void KnockForTest() { KnockbackPlayer(new Vector3(1,0,0),new Vector3()); }
    }
}
