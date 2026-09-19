using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        private bool destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.destroyed;
            bool bn = ReferenceEquals(b, null) || b.destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object b) { return ReferenceEquals(this, b); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.destroyed = true;
            GameObject go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (Object component in go.components) Destroy(component);
        }
    }
    public class GameObject : Object { public readonly List<Object> components = new List<Object>(); }
    public class MonoBehaviour : Object { public GameObject gameObject = new GameObject(); }
    public class Transform { public Vector3 position; }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x;this.y=y; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 up { get { return new Vector3(0,1,0); } }
        public static Vector3 forward { get { return new Vector3(0,0,1); } }
        public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator *(Vector3 a,float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
    }
    public static class Mathf
    {
        public const float Deg2Rad=(float)(Math.PI/180.0);
        public static float Cos(float a) { return (float)Math.Cos(a); }
        public static float Sin(float a) { return (float)Math.Sin(a); }
        public static int Abs(int a) { return Math.Abs(a); }
        public static float Clamp01(float x) { return Math.Max(0f, Math.Min(1f, x)); }
        public static int Clamp(int x, int min, int max) { return Math.Max(min, Math.Min(max, x)); }
        public static int FloorToInt(float x) { return (int)Math.Floor(x); }
        public static int RoundToInt(float x) { return (int)Math.Round(x); }
    }
}
namespace Duckov.Utilities { public class Tag { public string name; } }
namespace ItemStatsSystem.Stats
{
    public enum ModifierType { PercentageAdd, PercentageMultiply }
    public class Modifier
    {
        public readonly ModifierType type; public readonly float value; public readonly object Source;
        public Modifier(ModifierType type, float value, object source) { this.type=type; this.value=value; Source=source; }
        public Modifier(ModifierType type, float value, bool a, int order, object source) : this(type,value,source) { }
    }
    public class Stat
    {
        public float BaseValue = 1f;
        public readonly List<Modifier> modifiers = new List<Modifier>();
        public float Value
        {
            get
            {
                float add = 0, multiply = 1;
                foreach (var m in modifiers) if (m.type == ModifierType.PercentageAdd) add += m.value; else multiply *= 1+m.value;
                return BaseValue * (1+add) * multiply;
            }
        }
        public void AddModifier(Modifier m) { modifiers.Add(m); }
        public void RemoveModifier(Modifier m) { modifiers.Remove(m); }
    }
}
namespace ItemStatsSystem
{
    public class Constants
    {
        public readonly Dictionary<string,float> data = new Dictionary<string,float>();
        public float GetFloat(string key, float fallback) { float v; return data.TryGetValue(key,out v) ? v : fallback; }
    }
    public class Item : UnityEngine.Object
    {
        public int TypeID; public Constants Constants;
        public int StackCount=1; public Inventory InInventory; public DuckovItemAgent ActiveAgent;
        public void DestroyTree() { UnityEngine.Object.Destroy(this); }
        public DuckovItemAgent Drop(Vector3 position,bool a,Vector3 direction,float b)
        { return ActiveAgent=new DuckovItemAgent(); }
        public readonly Dictionary<string,Stat> stats = new Dictionary<string,Stat>();
        public Stat GetStat(string key) { Stat s; return stats.TryGetValue(key,out s) ? s : null; }
    }
    public struct ItemMetaData { public int id; public Duckov.Utilities.Tag[] tags; }
    public class Inventory : UnityEngine.Object
    {
        public Action<Item> OnAdding;
        public bool AddAndMerge(Item item,int start)
        {
            item.InInventory=this;
            if (OnAdding!=null) OnAdding(item);
            return true;
        }
    }
    public static class ItemAssetsCollection
    {
        public static readonly Dictionary<int,Item> items = new Dictionary<int,Item>();
        public static readonly Dictionary<int,ItemMetaData> metadata = new Dictionary<int,ItemMetaData>();
        public static int InstantiateCount;
        public static Item InstantiateSync(int id) { InstantiateCount++; return new Item { TypeID=id }; }
        public static Item GetPrefab(int id) { Item item; return items.TryGetValue(id,out item) ? item : null; }
        public static ItemMetaData GetMetaData(int id) { ItemMetaData data; metadata.TryGetValue(id,out data); return data; }
    }
}
namespace ItemStatsSystem.Data { public class ItemTreeData { } }
public class DuckovItemAgent : UnityEngine.Object { }
public static class PlayerStorage
{
    public static List<ItemStatsSystem.Data.ItemTreeData> IncomingItemBuffer=new List<ItemStatsSystem.Data.ItemTreeData>();
}
public static class ItemUtilities
{
    public static void SendToPlayerStorage(Item item,bool a)
    {
        PlayerStorage.IncomingItemBuffer.Add(new ItemStatsSystem.Data.ItemTreeData());
        item.DestroyTree();
    }
}
public enum DamageTypes { normal, realDamage }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter; public bool isFromBuffOrEffect;
    public float damageValue, finalDamage; public DamageTypes damageType;
    public int fromWeaponItemID; public Vector3 damagePoint;
}
public class CharacterRandomPreset : UnityEngine.Object { }
public class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main;
    public Health Health; public Item CharacterItem; public CharacterRandomPreset characterPreset;
    public Transform transform = new Transform(); public GameObject gameObject = new GameObject();
}
public class Health : UnityEngine.Object
{
    public static event Action<Health,DamageInfo> OnHurt, OnDead;
    public CharacterMainControl owner;
    public float CurrentHealth; public bool IsDead;
    public float MaxHealth { get { return owner.CharacterItem.GetStat("MaxHealth").Value; } }
    public CharacterMainControl TryGetCharacter() { return owner; }
    public void SetHealth(float value) { CurrentHealth = value; }
    public void AddHealth(float value) { CurrentHealth = Math.Min(MaxHealth, CurrentHealth+value); }
    // 官方顺序：先扣血，死亡事件（允许回调注销目标），再发受伤事件。
    public void Hurt(DamageInfo info)
    {
        if (IsDead) return;
        CurrentHealth = Math.Max(0, CurrentHealth-info.finalDamage);
        if (CurrentHealth == 0) { IsDead=true; if (OnDead != null) OnDead(this,info); }
        if (OnHurt != null) OnHurt(this,info);
    }
    public static void EmitDead(Health h, DamageInfo info) { if (OnDead != null) OnDead(h,info); }
}
public class ItemSetting_Gun { public int TargetBulletID; }
public class ItemAgent_Gun
{
    public static event Action<ItemAgent_Gun> OnMainCharacterShootEvent;
    public ItemSetting_Gun GunItemSetting; public int ShotCount;
    public float CharacterDamageMultiplier, Damage, ExplosionDamageMultiplier;
    public void Shoot() { if (OnMainCharacterShootEvent != null) OnMainCharacterShootEvent(this); }
}
public static class LevelManager
{
    public static event Action<CharacterMainControl> OnControllingCharacterChanged;
    public static void Switch(CharacterMainControl player) { if (OnControllingCharacterChanged != null) OnControllingCharacterChanged(player); }
}
namespace Saves
{
    public static class SavesSystem
    {
        public static event Action OnCollectSaveData, OnSetFile, OnSaveDeleted;
        public static bool IsSaving;
        public static readonly Dictionary<string,object> Data = new Dictionary<string,object>();
        public static bool KeyExisits(string key) { return Data.ContainsKey(key); }
        public static T Load<T>(string key) { return (T)Data[key]; }
        public static void Save<T>(string key,T data) { Data[key]=data; }
        public static void SaveFile(bool a) { }
        public static void ChangeSlot() { Data.Clear(); if (OnSetFile != null) OnSetFile(); }
        public static void Collect() { if (OnCollectSaveData != null) OnCollectSaveData(); }
        public static void Delete() { if (OnSaveDeleted != null) OnSaveDeleted(); }
    }
}
namespace BossRush
{
    public static class FateEchoRelicConfig { public const int TYPE_ID=500057; }
    public enum WeaponFamily { None, Gun, Melee }
    public static class L10n
    {
        public static bool Chinese = true;
        public static string T(string cn, string en) { return Chinese ? cn : en; }
        public static string T(string key) { return key; }
    }
    public partial class ModBehaviour
    {
        public static ModBehaviour Instance = new ModBehaviour();
        public static void DevLog(string message) { }
        public void ShowMessage(string message) { }
        public void ReportModeGBossKillAchievement(int t, string b, bool f) { }
        public bool TryStartModeGRewardMaterialization_LootAndRewards(int[] ids,Inventory inventory,
            Action<int,Item,bool> perItem,Action<int,int,int> completed,out string reason)
        { reason="fixture does not start Unity components"; return false; }
        public static bool SelectFormation(Vector3[] source,int count,ModeGPlanVariant variant,out Vector3[] selected)
        { return TrySelectModeGFormation(source,new Vector3(),0,count,variant,ModeGWavePlan.GetFormationSpec(variant),false,out selected); }
    }
    public sealed class ZombieModeAttributeModifierRecord { public Item CharacterItem; public Stat Stat; public Modifier Modifier; public string StatName; }
    public static class RuntimeStatModifierTracker
    {
        public static void RemoveAll(List<ZombieModeAttributeModifierRecord> records, string label)
        { foreach (var r in records) r.Stat.RemoveModifier(r.Modifier); records.Clear(); }
    }
    public static class ModeGOfficialBossEligibilityRegistry
    {
        public const int MinimumProductionOfficialBossCount=1, OfficialPoolReplicationTarget=6;
    }
    public static partial class ModeGEncounterVariation
    {
        public static bool IsManagedSignatureKey(string key) { return key.StartsWith("managed_"); }
    }
    public static class SpawnPositionHelper
    {
        // 夹具仅验证有限候选的几何选取；实际物理落地需实机。
        public static bool TrySnapToGround(Vector3 source,out Vector3 grounded) { grounded=source;return true; }
    }
    public static class ModeGPersistenceFlushCoordinator
    {
        public static void RequestFlush() { }
        public static void NotifySlotChanged() { }
    }
    internal static class ModeGRecapPanel
    {
        internal static string GetAmmoDisplayName(int id) { return id.ToString(); }
        internal static string ComposeBanAttributionLine(string name,int share) { return name; }
    }
    internal static class ModeGDeathRouting { internal static void HandleVictory(ModeGRuntimeModule module) { } }
    internal static partial class HudHarness
    {
        internal static string Compose(ModeGHudModel model) { return ComposeObjectiveLine(model); }
    }
    internal sealed partial class ModeGRuntimeModule
    {
        private readonly ModeGRunState _state;
        private readonly ModeGCombatTelemetry _telemetry;
        private readonly ModeGAdaptiveCombat _adaptive;
        private readonly ModBehaviour _host = ModBehaviour.Instance;
        public ModeGRuntimeModule(ModeGRunState state,ModeGCombatTelemetry telemetry,ModeGAdaptiveCombat adaptive)
        { _state=state; _telemetry=telemetry; _adaptive=adaptive; }
        public void Settle(ModeGDistanceVerdict distance,ModeGDirectDamageClass terminal)
        { _activeDistanceVerdict=distance; _lastTerminalFamily=terminal; SettleCurrentWave(); }
        public static float SpawnWait(float elapsed,float previous,float now,bool wasPaused,bool paused)
        { return AdvanceSpawnWait(elapsed,previous,now,wasPaused,paused); }
        internal ModeGHudModel Objective(ModeGCounterAxis axis)
        { var model = new ModeGHudModel { axis=axis }; FillObjective(ref model); return model; }
    }
}
