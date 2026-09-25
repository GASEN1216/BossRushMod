using System;
using System.Collections.Generic;
using UnityEngine;

internal static class Probe
{
    internal static readonly List<string> Trace = new List<string>();
    internal static void Close(string service, Transform target)
    {
        if (target == null || target.gameObject == null) throw new InvalidOperationException("Service closed after NPC destruction");
        Trace.Add(service + ":" + target.gameObject.name);
    }
}

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed;
            return an || bn ? an == bn : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object
        {
            Probe.Trace.Add("instantiate:" + ((GameObject)(Object)original).name);
            return (T)(Object)new GameObject(((GameObject)(Object)original).name);
        }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            var go = value as GameObject;
            if (go != null)
            {
                Probe.Trace.Add("destroy:" + go.name);
                foreach (var component in go.Components) component.Destroyed = true;
            }
            value.Destroyed = true;
        }
    }
    public class Component : Object { public GameObject gameObject; public Transform transform { get { return gameObject.transform; } } }
    public sealed class Transform : Component { }
    public sealed class GameObject : Object
    {
        public string name;
        public Transform transform;
        internal readonly List<Component> Components = new List<Component>();
        public GameObject(string value) { name=value; transform=AddComponent<Transform>(); }
        public T GetComponent<T>() where T : Component { foreach (var c in Components) if (c is T && c != null) return (T)c; return null; }
        public T AddComponent<T>() where T : Component, new() { var c=new T {gameObject=this}; Components.Add(c); return c; }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component { return new[] {GetComponent<T>()}; }
        public void SetActive(bool value) { Probe.Trace.Add("active:"+name+":"+value); }
    }
    public struct Vector3 { }
    public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } }
    public static class LayerMask { public static int NameToLayer(string name) { return 0; } }
    public static class Mathf
    {
        public static int Max(int a,int b) { return Math.Max(a,b); }
        public static int FloorToInt(float value) { return (int)Math.Floor(value); }
    }
}
namespace UnityEngine.Events { public delegate void UnityAction<T>(T value); }
namespace ItemStatsSystem { public sealed class Item { } }
public sealed class DuckovItemAgent { }
public sealed class Health : UnityEngine.Object
{
    public readonly HealthEvent OnHealthChange = new HealthEvent();
}
public sealed class HealthEvent
{
    private readonly List<UnityEngine.Events.UnityAction<Health>> listeners = new List<UnityEngine.Events.UnityAction<Health>>();
    public int Count { get { return listeners.Count; } }
    public void AddListener(UnityEngine.Events.UnityAction<Health> action) { listeners.Add(action); }
    public void RemoveListener(UnityEngine.Events.UnityAction<Health> action) { listeners.Remove(action); Probe.Trace.Add("health-remove"); }
}
public sealed class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main;
    public Health Health = new Health();
    public event Action<DuckovItemAgent> OnHoldAgentChanged;
    public int HoldSubscribers { get { return OnHoldAgentChanged == null ? 0 : OnHoldAgentChanged.GetInvocationList().Length; } }
}
public sealed class CourierNPCController : Component
{
    public void SetStationary(bool value) { Probe.Trace.Add("controller-stationary"); }
    public void StartTalking(bool value) { Probe.Trace.Add("talking:"+value); }
}
public sealed class CourierMovement : Component { public void SetStationary(bool value) { Probe.Trace.Add("movement-stationary"); } }
public static class NPCCommonUtils
{
    public static void FixShaders(GameObject npc,string context) { Probe.Trace.Add("shader"); }
    public static void SetLayerRecursively(GameObject npc,int layer) { Probe.Trace.Add("layer"); }
}
public static class NPCShopSystem { public static void CloseShopIfOwnedBy(Transform t) { Probe.Close("shop",t); } }
public static class ReforgeUIManager { public static void CloseUIIfOwnedBy(Transform t) { Probe.Close("reforge",t); } }
public static class CourierService { public static void CloseServiceIfOwnedBy(Transform t) { Probe.Close("courier",t); } }
public static class StorageDepositService { public static void CloseServiceIfOwnedBy(Transform t) { Probe.Close("storage",t); } }
public static class CourierPaidLootSweepService { public static void CloseServiceIfOwnedBy(Transform t) { Probe.Close("sweep",t); } }

namespace BossRush
{
    public sealed class ModBehaviour { public static void DevLog(string value) { } }
    internal sealed class BossRushStatModifierRecord { }
    internal static class RuntimeStatModifierTracker
    {
        internal static void RemoveAll(List<BossRushStatModifierRecord> list,string context) { Probe.Trace.Add(context); list.Clear(); }
    }
    internal static class NotificationText { internal static void Push(string text) { Probe.Trace.Add("notify:"+text); } }
    internal static class L10n { internal static string T(string text) { return text; } }
    internal enum ZombieModeRunOnlyObjectKind { EventListener }
    internal sealed class ZombieModeTemporaryNpc
    {
        internal GameObject GameObject;
        internal string ServiceType;
        internal ZombieModeNpcServiceState ServiceState;
    }
    internal sealed class ZombieModeTemporaryRealNpcRecord { internal GameObject GameObject; internal bool SafeZoneBound; }
    internal sealed class ZombieModeNpcServiceState
    {
        internal bool SafeZoneBound, BossNodeStock;
        internal readonly List<int> MerchantStockRemaining = new List<int>();
        internal readonly List<int> NurseUsesRemaining = new List<int>();
    }
    internal sealed class ZombieModeOptionRuntimeState
    {
        internal bool MutatorBulletTimeEnabled, MutatorGuardianShieldEnabled, PlayerHealthListenerRegistered;
        internal readonly List<BossRushStatModifierRecord> GuardianShieldRecords = new List<BossRushStatModifierRecord>();
        internal readonly List<BossRushStatModifierRecord> ModifierRecords = new List<BossRushStatModifierRecord>();
        internal void Reset() { Probe.Trace.Add("options-reset"); PlayerHealthListenerRegistered=false; MutatorBulletTimeEnabled=false; MutatorGuardianShieldEnabled=false; }
    }
    internal sealed class ZombieModeRunState
    {
        internal int RunId, CurrentWave, PurificationPoints;
        internal readonly List<ZombieModeTemporaryNpc> TemporaryNpcs = new List<ZombieModeTemporaryNpc>();
        internal readonly List<ZombieModeTemporaryRealNpcRecord> TemporaryRealNpcs = new List<ZombieModeTemporaryRealNpcRecord>();
        internal readonly ZombieModeOptionRuntimeState OptionRuntime = new ZombieModeOptionRuntimeState();
    }
    internal static class ZombieModeNpcCatalog
    {
        internal sealed class MerchantStockEntry { internal int BasePrice, TypeId; internal string GrantTag, DisplayKey; }
        internal sealed class NurseServiceEntry { internal int BasePrice; internal string ServiceKey; }
        internal static MerchantStockEntry[] NormalWaveStock, BossNodeStock;
        internal static NurseServiceEntry[] NurseServices;
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        private readonly ZombieModeRunState runState;
        internal readonly List<Action> Cleanups = new List<Action>();
        internal readonly Dictionary<GameObject,Action> ObjectCleanups = new Dictionary<GameObject,Action>();
        internal Func<int,bool> ItemGrant;
        internal Func<int,bool> NurseEffect;
        internal ZombieModeRuntimeModule(ZombieModeRunState value) { runState=value; }
        private bool IsZombieModeRunValid(int id) { return id>0 && id==runState.RunId; }
        private bool IsZombieModeBossWave(int wave) { return wave%5==0; }
        private float GetZombieModeNpcServicePriceMultiplier() { return 1f; }
        private bool TryGiveZombieModeItemToPlayerOrDrop(int id) { return ItemGrant(id); }
        private bool ApplyZombieModeNurseServiceEffect(int id) { return NurseEffect(id); }
        private bool IsZombieModeMerchantBulletStock(ZombieModeNpcCatalog.MerchantStockEntry entry) { return false; }
        private bool TryGiveZombieModeMerchantAmmoForEquippedWeapon(ZombieModeNpcCatalog.MerchantStockEntry entry) { throw new InvalidOperationException("Unexpected ammo branch"); }
        private bool TryPurchaseZombieModeGuaranteedMerchantStockFromPool(ZombieModeNpcCatalog.MerchantStockEntry entry) { throw new InvalidOperationException("Unexpected guarantee branch"); }
        private bool TryGiveRandomZombieModeMerchantItemFromModeEPool(ZombieModeNpcCatalog.MerchantStockEntry entry) { throw new InvalidOperationException("Unexpected pool branch"); }
        private void RegisterZombieModeRunOnlyObject(int id,ZombieModeRunOnlyObjectKind kind,GameObject obj,UnityEngine.Object target,Action cleanup) { Cleanups.Add(cleanup); }
        private void RemoveZombieModeRunOnlyObjectRecord(GameObject target)
        {
            Probe.Trace.Add("remove-record:"+target.name);
            Action cleanup;
            if(ObjectCleanups.TryGetValue(target,out cleanup)) { cleanup(); ObjectCleanups.Remove(target); }
        }
        private void HandleZombieModePlayerHealthChangedForOptions(Health health) { }
        private void OnZombieModeSpreadHoldAgentChanged(DuckovItemAgent agent) { }
        private void RemoveZombieModePhase2ContractRuntimeEffects() { Probe.Trace.Add("phase2-remove"); }
        internal void RegisterEffects() { EnsureZombieModeOptionPlayerHealthListener(); EnsureZombieModeProjectileSpreadListener(); }
        internal GameObject CreateCourier() { return CreateZombieModeTemporaryCourierNpc(new Vector3()); }
        internal void AddSpreadSnapshot(int id)
        {
            var snapshot=new ZombieModeProjectileSpreadSnapshot();
            snapshot.ModifierRecords.Add(new BossRushStatModifierRecord());
            zombieModeProjectileSpreadSnapshots.Add(id,snapshot);
        }
        internal int SnapshotCount { get { return zombieModeProjectileSpreadSnapshots.Count; } }
    }
}
