using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace UnityEngine
{
    public sealed class SerializeField : Attribute { }
    public class Object
    {
        private static int next; private readonly int id = ++next;
        public bool Destroyed; public string name;
        public int GetInstanceID() { return id; }
        public static implicit operator bool(Object o) { return o != null; }
        public static bool operator ==(Object a, Object b) { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an && bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object o) { return ReferenceEquals(this, o); }
        public override int GetHashCode() { return id; }
        public static void Destroy(Object o) { if (ReferenceEquals(o, null)) return; o.Destroyed = true; var go = o as GameObject; if (go != null) go.DestroyParts(); }
        public static T Instantiate<T>(T obj, Vector3 pos, Quaternion rotation) { return obj; }
    }
    public class GameObject : Object
    {
        public int layer; public bool activeSelf = true; public Transform transform = new Transform();
        private readonly Dictionary<Type, Object> parts = new Dictionary<Type, Object>();
        public void Put(Object part) { parts[part.GetType()] = part; var component = part as MonoBehaviour; if (component != null) component.gameObject = this; }
        public T GetComponent<T>() where T : class { foreach (var part in parts.Values) if (part is T) return part as T; return null; }
        public void SetActive(bool active) { activeSelf = active; }
        public void DestroyParts() { activeSelf = false; foreach (var part in parts.Values) part.Destroyed = true; }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject = new GameObject(); public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
    }
    public class Transform : Object { public Vector3 position; public Vector3 forward = new Vector3(0, 0, 1); public Quaternion rotation; }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 zero { get { return new Vector3(); } } public static Vector3 up { get { return new Vector3(0,1,0); } }
        public float magnitude { get { return (float)Math.Sqrt(x*x+y*y+z*z); } } public Vector3 normalized { get { return magnitude > 0 ? this * (1f/magnitude) : zero; } }
        public void Normalize() { this = normalized; }
        public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a,Vector3 b) { return a + -b; } public static Vector3 operator -(Vector3 a) { return a * -1; }
        public static Vector3 operator *(Vector3 a,float b) { return new Vector3(a.x*b,a.y*b,a.z*b); } public static Vector3 operator *(float a,Vector3 b) { return b*a; }
        public static float Distance(Vector3 a,Vector3 b) { return (a-b).magnitude; } public static float Angle(Vector3 a,Vector3 b) { return 0; }
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t) { return a + (b-a)*t; }
    }
    public struct Quaternion { public static Quaternion Euler(float x,float y,float z) { return new Quaternion(); } public static Quaternion LookRotation(Vector3 a,Vector3 b) { return new Quaternion(); } public static Vector3 operator *(Quaternion a,Vector3 b) { return b; } }
    public struct Color { }
    public static class Mathf
    {
        public static float Abs(float v) { return Math.Abs(v); } public static float Max(float a,float b) { return Math.Max(a,b); } public static float Min(float a,float b) { return Math.Min(a,b); }
        public static float Clamp(float v,float a,float b) { return Math.Max(a,Math.Min(b,v)); } public static float Lerp(float a,float b,float t) { return a+(b-a)*t; }
        public static float InverseLerp(float a,float b,float v) { return Clamp((v-a)/(b-a),0,1); } public static float MoveTowards(float a,float b,float step) { return a < b ? Math.Min(a+step,b) : Math.Max(a-step,b); }
    }
    public static class Random { public static float Range(float a,float b) { return a+(b-a)*0.5f; } }
    public static class Time { public static float deltaTime = 0.02f; }
    public class Collider : MonoBehaviour { }
    public struct RaycastHit { public float distance; public Vector3 point, normal; public Collider collider; }
    public static class Physics { public static RaycastHit[] Hits = new RaycastHit[0]; public static RaycastHit[] SphereCastAll(Vector3 p,float r,Vector3 d,float dist,int layers,int query) { return Hits; } }
}
namespace Duckov.Buffs { public class Buff : UnityEngine.Object { } }
namespace ItemStatsSystem { public sealed class ItemTypeIDAttribute : Attribute { } }
public sealed class HarmonyPostfixAttribute : Attribute { }
namespace ItemStatsSystem.Stats { public class NamespaceMarker { } }
namespace ItemStatsSystem
{
    public class Stat
    {
        private float baseValue; private bool display; public string Key; public float RuntimeBonus;
        public Stat(string key,float value,bool shown) { Key=key;baseValue=value;display=shown; }
        public float BaseValue { get { return baseValue; } set { baseValue=value; } } public float Value { get { return baseValue+RuntimeBonus; } }
        public void Initialize(StatCollection collection) { }
    }
}
namespace ItemStatsSystem
{
    public class ItemComponent { internal virtual void OnInitialize() { } }
    public class CustomData { public string Key; public float Value; public bool Display; public float GetFloat() { return Value; } }
    public class Variables : List<CustomData> { }
    public class Inventory { }
    public partial class Item : UnityEngine.Object
    {
        public int TypeID, Value = 45000; public string DisplayName = "fixture"; public float MaxDurability=100,Durability=100,DurabilityLoss;
        public StatCollection Stats; public Inventory Inventory; public Variables Variables = new Variables(); public ItemSetting_Gun Gun;
        public GameObject gameObject = new GameObject();
        public void CreateStatsComponent() { Stats = new StatCollection(); }
        public Stat GetStat(int hash) { return Stats == null ? null : Stats.GetStat(hash); }
        public int GetInt(string key,int fallback) { return fallback; }
        public T GetComponent<T>() where T : class { return Gun as T; }
    }
    public static class ItemAssetsCollection { public static readonly Dictionary<int,Item> Prefabs=new Dictionary<int,Item>(); public static Item GetPrefab(int id) { Item item; return Prefabs.TryGetValue(id,out item)?item:null; } }
}
public class ItemSetting_Gun : UnityEngine.Object
{
    public bool LoadingBullets; public Projectile bulletPfb; public bool CanControlMind; public ElementTypes element=ElementTypes.fire; public Duckov.Buffs.Buff buff;
    public void TriggerOnHurtEnemyEvent(DamageReceiver receiver,DamageInfo info) { }
}
public class ItemSetting_Bullet : UnityEngine.Object { }
public class DuckovItemAgent : UnityEngine.Object { public Item Item; public CharacterMainControl Holder; public GameObject gameObject=new GameObject(); }
public enum Teams { player,wolf,all }
public enum ControlMindTypes { none }
public class CharacterModel { public Transform HelmatSocket=new Transform(); }
public class CharacterRandomPreset : UnityEngine.Object { public float aiCombatFactor=1; }
public class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main; public bool IsMainCharacter,Dashing,isVehicle,protectVehicleDriver; public ControlMindTypes canBeControlVia;
    public float GunBulletSpeedMultiplier=1; public Teams Team=Teams.player; public CharacterRandomPreset characterPreset=new CharacterRandomPreset();
    public CharacterModel characterModel=new CharacterModel(); public Item CharacterItem; public DuckovItemAgent CurrentHoldItemAgent;
    public event Action<DuckovItemAgent> OnHoldAgentChanged;
    public void Hold(DuckovItemAgent agent) { CurrentHoldItemAgent=agent; if(OnHoldAgentChanged!=null) OnHoldAgentChanged(agent); }
    public ItemAgent_Gun GetGun() { return CurrentHoldItemAgent as ItemAgent_Gun; }
    public void SetTeam(Teams team) { Team=team; } public bool HasNearByHalfObsticle() { return false; } public List<GameObject> GetNearByHalfObsticles() { return new List<GameObject>(); }
    public void ControlOtherCharacter(CharacterMainControl character,float time) { } public Item GetHelmatItem() { return null; } public Item GetArmorItem() { return null; }
    public void AddBuff(Duckov.Buffs.Buff buff,CharacterMainControl attacker,int id) { }
}
public partial class ItemAgent_Gun
{
    private static int DamageFactorToZombieHash="DamageFactorToZombie".GetHashCode();
    private ItemSetting_Gun _gunItemSetting { get { return Item.Gun; } } public ItemSetting_Gun GunItemSetting { get { return Item.Gun; } }
    public Item BulletItem=new Item(); public float CurrentScatter,durabilityPercent=1; public Teams fallbackTeam=Teams.wolf;
    public Projectile projInst; public CharacterMainControl traceTarget;
    public float BulletSpeed=122,BulletDistance=28,TraceAbility,ControlMindTime,CharacterDamageMultiplier=1,BulletDamageMultiplier=1,DmgOverDistance=0.5f,CritDamageFactor=1.5f,CharacterGunCritDamageGain,CritRate,CharacterGunCritRateGain,bulletCritRateGain,ArmorPiercing,BulletArmorPiercingGain,ArmorBreak,BulletArmorBreakGain,BulletExplosionRange,BulletExplosionDamage,ExplosionDamageMultiplier=1,BulletBuffChanceMultiplier,BuffChance,BulletBleedChance;
    public int Penetrate; public ControlMindTypes ControlMindType;
    public float BulletCritDamageFactorGain;
    public float Damage { get { return Item.GetStatValue("Damage".GetHashCode()); } } public int ShotCount=1;
    public void Fire() { ShootOneBullet(Vector3.zero,new Vector3(0,0,1),Vector3.zero); }
}
public partial class Projectile
{
    public ProjectileContext context; public List<GameObject> damagedObjects=new List<GameObject>();
    private bool firstFrame=true,overMaxDistance,dead; private float traceLerpValue,traveledDistance,_distanceThisFrame,gravity,radius; private int hitLayers=1;
    private Vector3 startPoint,hitPoint,direction,velocity; private List<RaycastHit> hits; private DamageReceiver _dmgReceiverTemp; private GameObject hitFx;
    public void Init(ProjectileContext value) { context=value; direction=value.direction;velocity=value.speed*direction; }
    private void UpdateAimDirection() { }
    public void Hit() { UpdateMoveAndCheck(); }
}
public partial class DamageReceiver
{
    public Health health; public bool isHalfObsticle; public Teams Team { get { return health.team; } } public bool IsDead { get { return health.isDead; } } public bool IsMainCharacter { get { return health.IsMainCharacterHealth; } }
    public UnityEvent<DamageInfo> OnHurtEvent; public bool AddBuff(Duckov.Buffs.Buff buff,CharacterMainControl source) { return true; }
}
public class BulletBlocker : MonoBehaviour { public Teams team; public bool checkDirection,bulletBack; public CharacterMainControl from; }
public static class Team { public static bool IsEnemy(Teams a,Teams b) { return a!=b; } }
public static class HardwareSyncingManager { public static void SetEvent(string value) { } }
public class BulletPool { public Projectile Last; public Projectile GetABullet(Projectile prefab) { Last=new Projectile();return Last; } }
public class InputManager { public bool AimingEnemyHead; }
public class RuleData { public bool AdvancedDebuffMode; public float DamageFactor_ToPlayer=1; }
public class LevelManager { public static LevelManager Instance=new LevelManager(); public static RuleData Rule=new RuleData(); public CharacterMainControl MainCharacter,ControllingCharacter; public bool IsBaseLevel,IsRaidMap=true; public BulletPool BulletPool=new BulletPool(); public InputManager InputManager=new InputManager(); }
public class MultiSceneCore { public static MultiSceneCore Instance; public bool IsLoading; }
public static class EXPManager { public static void AddExp(int value) { } }
public static class UniTaskExtensions { public static void Forget(object value) { } }
public class UnityEvent<T> { public void Invoke(T value) { } }
public class PopText : UnityEngine.Object { public static PopText instance; public static void Pop(string t,Vector3 p,Color c,float s,object sprite) { } }
public static class GameplayDataSettings
{
    public class LayersData { public static bool IsLayerInLayerMask(int layer,int mask) { return (mask & 1<<layer)!=0; } }
    public class LayerSet { public int damageReceiverLayerMask=1,wallLayerMask,groundLayerMask,blockBulletLayers,halfObsticleLayer; }
    public static LayerSet Layers=new LayerSet();
    public class BuffSet { public Duckov.Buffs.Buff Pain,BoneCrackBuff,WoundBuff,UnlimitBleedBuff,BleedSBuff; } public static BuffSet Buffs=new BuffSet();
    public class PrefabSet { public Projectile DefaultBullet=new Projectile(); public GameObject BulletBackFx,BlockBulletFx,BulletHitObsticleFx; } public static PrefabSet Prefabs=new PrefabSet();
    public class UIStyleData { public class DisplayElementDamagePopTextLook { public float critSize,normalSize; public Color color; } public object CritPopSprite; public DisplayElementDamagePopTextLook GetElementDamagePopTextLook(ElementTypes type) { return new DisplayElementDamagePopTextLook(); } }
    public static UIStyleData UIStyle=new UIStyleData();
}
public static class GameManager { public class Scale { public void EnterBulletTime(float time) { } } public static Scale TimeScaleManager=new Scale(); }
public partial class Health
{
    public bool invincible,isDead,isZombie,CanDieIfNotRaidMap=true,Hidden=true;
    public CharacterMainControl Owner; public bool IsMainCharacterHealth { get { return Owner != null && Owner==LevelManager.Instance.MainCharacter; } }
    public float CurrentHealth=1000,BodyArmor,HeadArmor; public Teams team=Teams.wolf; public Item item;
    public UnityEvent<DamageInfo> OnDeadEvent,OnHurtEvent; public static Action<Health,DamageInfo> OnDead,OnHurt;
    public CharacterMainControl TryGetCharacter() { return Owner; } public float ElementFactor(ElementTypes type) { return 1; }
    public void AddBuff(Duckov.Buffs.Buff buff,CharacterMainControl source,int id) { } public void SetHealth(float value) { CurrentHealth=value; } public object DestroyOnDelay() { return null; }
}
namespace BossRush
{
    static partial class DragonBreathWeaponConfig
    {
        public const int WEAPON_TYPE_ID=DragonBreathConfig.WEAPON_TYPE_ID; private const string CALIBER="BR"; private const float MAX_DURABILITY=100,REPAIR_LOSS_RATIO=0.2f; private const int MIN_VALUE=45000;
        private static bool statFieldsCached; private static FieldInfo cachedStatBaseValueField,cachedStatDisplayField,cachedStatsDictionaryField;
        private static void ConfigureSharedGunFoundation(Item item,string caliber,float max,float ratio) { item.Inventory=item.Inventory??new Inventory();item.MaxDurability=max;item.Durability=max; }
        private static void AddWeaponTags(Item item) { } private static void SetBulletCountDisplay(Item item) { } private static void ConfigureGunSettings(Item item) { }
        private static void SyncItemValueFromRawValue(Item item) { } private static void VerifyCriticalStats(Item item) { } private static int RemoveLegacyTrackingModifiers(Item item) { return 0; }
        public static void TryAddFireEffectsToAgent(ItemAgent_Gun gun) { }
    }
    static partial class ReforgeDataPersistence
    {
        public const string MODIFIER_PREFIX="RF_MOD_",STAT_PREFIX="RF_STAT_",VARIABLE_PREFIX="RF_VAR_";
        public static void CleanupUnsupportedReforgeData(Item item) { }
        private static bool TryRestoreModifier(Item item,string key,float delta) { throw new Exception("Unexpected modifier reforge branch"); }
        private static bool TryRestoreVariable(Item item,string key,float delta) { throw new Exception("Unexpected variable reforge branch"); }
    }
    static class ReforgeSystem { public static void ApplyStatValueChangePublic(Stat stat,float value) { stat.BaseValue=value; } }
    static partial class CustomItemRuntimeStateHelper { public static void RegisterMeleeRuntimeConfiguredItem(int id,Action<Item> apply,string name,Action<Item> restored=null,Action<GameObject> visual=null) { } }
    partial class IntegrationRuntimeModule
    {
        private bool _dragonBreathEffectEventSubscribed; private CharacterMainControl _cachedMainCharForEffect;
        public void HoldChanged(DuckovItemAgent agent) { OnPlayerHoldAgentChanged(agent); }
    }
    partial class ModBehaviour
    {
        internal readonly IntegrationRuntimeModule bossRushIntegrationRuntime=new IntegrationRuntimeModule(); public static void DevLog(string value) { }
        public void Register() { RegisterCustomWeaponRuntimeConfigs(); }
    }
    static class NewWeaponRuntime { public static void RegisterRuntimeConfigs() { } }
    static class DragonKingBossGunConfig { public const int WeaponTypeId=500023; public static void ConfigureWeapon(Item item) { } }
    static class DragonKingBossGunRuntime { public static void RefreshAmmoProfileAfterRuntimeRestore(Item item) { } public static void TryAddFireEffectsToAgent(ItemAgent_Gun gun) { } }
    static class FenHuangHalberdIds { public const int WeaponTypeId=500025; }
    static class FenHuangHalberdWeaponConfig { public static void TryConfigure(Item item,string name) { } public static void PrepareRuntimeHoldAgentVisual(GameObject obj) { } }
    static class FrostmourneIds { public const int WeaponTypeId=500030; }
    static class FrostmourneWeaponConfig { public static void TryConfigure(Item item,string name) { } public static void PrepareRuntimeHoldAgentVisual(GameObject obj) { } public static void TryAddIceEffectsToGraphic(GameObject obj) { } }
    static class PhantomWitchConfig { public const int ReservedScytheTypeId=500028; }
    static class PhantomWitchScytheWeaponConfig { public static void TryConfigure(Item item) { } public static void PrepareRuntimeHoldAgentVisual(GameObject obj) { } }
    static class DragonBreathBuffHandler { public static void Subscribe() { } }
}
