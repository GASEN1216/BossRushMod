using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

internal static class Probe
{
    internal static readonly List<string> Trace=new List<string>();
    internal static readonly Queue<bool> Grants=new Queue<bool>();
    internal static bool Enough=true,ThrowEconomy,Ammo=true;
    internal static int ResourceScans;
    internal static bool PayoutAvailable=true,ThrowAfterPayout,ThrowPayoutNotification;
    internal static long Money;
}
namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a,Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed; return an||bn?an==bn:ReferenceEquals(a,b); }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public override bool Equals(object other) { return ReferenceEquals(this,other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value) { if(value==null)return; var go=value as GameObject; if(go!=null) foreach(var c in go.Components)c.Destroyed=true; value.Destroyed=true; }
    }
    public class Component:Object { public GameObject gameObject; public Transform transform { get { return gameObject.transform; } } }
    public struct Scene { public bool IsValid() { return false; } }
    public class GameObject:Object
    {
        public string name; public Transform transform; public Scene scene;
        internal readonly List<Component> Components=new List<Component>();
        public GameObject(string name) { this.name=name;transform=new Transform { gameObject=this };Components.Add(transform); }
        public T AddComponent<T>() where T:Component,new() { var value=new T { gameObject=this };Components.Add(value);return value; }
        public T GetComponent<T>() where T:Component { foreach(var c in Components) if(c is T)return (T)c;return null; }
    }
    public class Transform:Component { public Vector3 localScale,position; }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } public static Vector3 zero { get { return new Vector3(); } } public static Vector3 up { get { return new Vector3(0,1,0); } } public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); } public static Vector3 operator *(Vector3 a,float n) { return new Vector3(a.x*n,a.y*n,a.z*n); } }
    public static class Mathf { public static int Max(int a,int b) { return Math.Max(a,b); } }
    public static class Debug { public static void LogWarning(string text) { Probe.Trace.Add("warning"); } }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() { Probe.ResourceScans++; return new T[0]; } }
}
namespace ItemStatsSystem { public sealed class Item { public string Caliber="9mm"; } }
namespace Duckov.Utilities
{
    public static class ItemAssetsCollection { public static Item InstantiateSync(int id) { Probe.Trace.Add("instantiate-gun"); return new Item(); } }
    public static class ItemUtilities { public static void SendToPlayer(Item item,bool first,bool second) { Probe.Trace.Add("send-gun"); } }
}
namespace Duckov.Economy
{
    public struct Cost { public long money; }
    public static class EconomyManager
    {
        public static long Money { get { return Probe.Money; } }
        public static bool Add(long amount) { Probe.Trace.Add("payout:"+amount); if(Probe.ThrowEconomy)throw new InvalidOperationException("before payout"); if(!Probe.PayoutAvailable)return false;Probe.Money+=amount;if(Probe.ThrowAfterPayout)throw new InvalidOperationException("OnMoneyChanged");return true; }
        public static bool IsEnough(Cost cost,bool storage,bool cash) { Probe.Trace.Add("cash-preview:"+cost.money); if(Probe.ThrowEconomy)throw new InvalidOperationException();return Probe.Enough; }
    }
}
namespace BossRush
{
    public enum ZombieModeStarterLoadout { None,Melee,Gunner }
    public enum ZombieModeLifecyclePhase { None,SelectingMap,WaitingStarterChoice,Active }
    public enum ZombieModeCombatPhase { None,InitialPreparation,ExtractionOpportunity,SuccessExit }
    public enum ZombieModeFailureReason { StarterLoadoutFailed,SuccessfulExtraction }
    public enum ZombieModeRunOnlyObjectKind { ExtractionPoint }
    public sealed class ZombieModeRunState
    {
        public int RunId=1,PurificationPoints; public long PendingCashInvestment; public string StarterAmmoCaliber;
        public ZombieModeStarterLoadout StarterLoadout; public ZombieModeLifecyclePhase LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice; public ZombieModeCombatPhase CombatPhase;
        public readonly List<ZombiePurificationStar> PendingPurificationStars=new List<ZombiePurificationStar>();
        public bool ExtractionSuccessHandled,ExtractionChanneling,BeaconChanneling,ActiveSafeZoneActive;
        public Vector3 ActiveSafeZoneCenter; public CountDownArea ActiveExtractionArea;
    }
    public sealed class ZombiePurificationStar { public bool Settled; public int PointsValue; public GameObject Visual; }
    public sealed class SoulCube:Component { }
    public sealed class SoulCollector:Component { public SoulCube cubePfb; }
    public sealed class ZombieModeVisualScaleRecord { public Transform Target; public Vector3 OriginalScale; }
    public sealed class ZombieModeEnemyRuntimeMarker:Component
    {
        public readonly List<ZombieModeVisualScaleRecord> VisualScaleRecords=new List<ZombieModeVisualScaleRecord>();
        public GameObject VisualFootMarker; public bool VisualFootMarkerFallbackApplied;
    }
    public static class ZombieModeFootMarkerPool { public static void Release(GameObject marker) { Probe.Trace.Add("release:"+marker.name); } }
    public static class ZombieModePickupAbsorbFx { public static void Play(GameObject value) { Probe.Trace.Add("absorb:"+value.name); } }
    public static class ZombieModeTuning { public const int StarterMaxQuality=5,StarterGunnerExtraAmmoCount=2000,CashToPurificationRatio=100; public const float ExtractionCountdownSeconds=15,ExtractionAreaTriggerRadius=3; }
    public static class L10n { public static string T(string key) { return key; } public static string T(string chinese,string english) { return english; } }
    public sealed class ModBehaviour
    {
        public static void DevLog(string text) { }
        public void ShowBigBanner(string text) { Probe.Trace.Add("banner"); }
        public void PreCacheMapSpawnerPositions() { Probe.Trace.Add("precache"); }
        public void ResetZombieModeOriginalSpawnerStateForRuntimeModule() { Probe.Trace.Add("reset-spawners"); }
        public void DisableZombieModeOriginalSpawnersForRuntimeModule() { Probe.Trace.Add("disable-spawners"); }
        public ZombieModeRunState State;
        public void NotifyCampaignZombieExtracted() { Probe.Trace.Add("campaign"); }
        public void CleanupZombieModeForRuntimeModule(ZombieModeFailureReason reason)
        {
            Probe.Trace.Add("cleanup");
            UnityEngine.Object.Destroy(State.ActiveExtractionArea.gameObject);
            State.ActiveExtractionArea=null;State.RunId++;State.PurificationPoints=0;State.ExtractionSuccessHandled=false;
        }
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        private readonly ModBehaviour owner; private readonly ZombieModeRunState runState;
        internal ZombieModeRuntimeModule(ModBehaviour owner,ZombieModeRunState state) { this.owner=owner;runState=state;owner.State=state; }
        private bool IsZombieModeRunValid(int runId) { return runState.RunId==runId; }
        private bool TryGiveRandomItemByTags(string[] tags,int quality,int maximum) { Probe.Trace.Add("grant:"+tags[0]); return Probe.Grants.Count==0 || Probe.Grants.Dequeue(); }
        private int TryGiveRandomItemByTagsTimes(string[] tags,int quality,int maximum,int count) { Probe.Trace.Add("grant-many:"+tags[0]+":"+count); return count; }
        private int FindRandomItemTypeByTags(string[] tags,int quality,int maximum) { Probe.Trace.Add("select-gun"); return 4; }
        private string TryReadZombieModeItemCaliber(Item item) { Probe.Trace.Add("caliber"); return item.Caliber; }
        private bool TryGiveZombieModeStarterAmmo(string caliber,int count) { Probe.Trace.Add("ammo:"+caliber+":"+count); return Probe.Ammo; }
        private void FailZombieModeBeforeActive(ZombieModeFailureReason reason) { Probe.Trace.Add("fail"); }
        private void FinalizeZombieModeEntryResources() { Probe.Trace.Add("finalize"); }
        private void UnlockZombieModeContainersForActiveRun(int runId) { Probe.Trace.Add("unlock:"+runState.LifecyclePhase); }
        private void BeginZombieModePreparation(int runId,bool initial,bool second) { Probe.Trace.Add("prepare:"+runState.CombatPhase); }
        private void DisableZombieModeOriginalExtractionPoints(int runId) { Probe.Trace.Add("disable-extraction"); }
        private void DisableZombieModeOriginalCharacters() { Probe.Trace.Add("disable-characters"); }
        private void RestoreZombieModeOriginalCharacters() { Probe.Trace.Add("restore-characters"); }
        private void RestoreZombieModeOriginalExtractionPoints() { Probe.Trace.Add("restore-extraction"); }
        internal bool CompleteExtraction(int runId) { return CompleteZombieModeExtractionSuccess(runId); }
        private void TryReleaseZombieModeExtractionCountdownUi() { Probe.Trace.Add("release-ui"); }
        private void ShowZombieModeExtractionOpportunityUi(int runId) { Probe.Trace.Add("retry-ui"); }
        private void TryNotifyZombieModeExtraction() { Probe.Trace.Add("notify-extraction"); }
        private void TryNotifyZombieModeExtractionFromFactory(Vector3 position) { Probe.Trace.Add("notify-extraction"); }
        private void TryLoadBaseSceneAfterZombieModeExtraction() { Probe.Trace.Add("load-base"); }
        private void RegisterZombieModeRunOnlyObject(int runId,ZombieModeRunOnlyObjectKind kind,GameObject go,UnityEngine.Object target,Action cleanup) { Probe.Trace.Add("register-extraction"); }
    }
}
