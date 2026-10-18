using System;
using System.Collections.Generic;
namespace UnityEngine
{
    internal static partial class Mathf { internal static int Clamp(int x, int a, int b) { return Math.Max(a, Math.Min(b, x)); } internal static float Round(float x) {return (float)Math.Round(x);} internal static int Max(int a,int b) {return Math.Max(a,b);} }
    internal partial class Object {internal bool Destroyed;public static bool operator ==(Object a,Object b){bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b);}public static bool operator !=(Object a,Object b){return !(a==b);}public override bool Equals(object b){return this==b as Object;}public override int GetHashCode(){return base.GetHashCode();}internal static void Destroy(GameObject go){if(ReferenceEquals(go,null))return;go.Destroyed=true;foreach(var child in go.Components)child.Destroyed=true;}}
    internal partial class MonoBehaviour : Object { public string name; public GameObject gameObject = new GameObject();internal MonoBehaviour(){gameObject.Components.Add(this);}public int GetInstanceID() { return GetHashCode(); } }
    internal partial class GameObject : Object {internal readonly List<Object> Components=new List<Object>();internal bool activeSelf=true;internal void SetActive(bool x){activeSelf=x;}public string name="fixture"; public UnityEngine.SceneManagement.Scene scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene(); public Transform transform = new Transform(); }
    internal class Transform { public Vector3 position; }
    internal struct Quaternion {public static Quaternion identity {get{return new Quaternion();}}} internal struct Vector3 {public static Vector3 zero {get{return new Vector3();}} public float x, y, z; public float sqrMagnitude {get{return x*x+y*y+z*z;}} public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);} public Vector3 normalized { get { return this; } } public Vector3(float x, float y, float z) { this.x=x;this.y=y;this.z=z; } public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); } }
    internal static class Time { public static float time, unscaledTime; }
    internal static class Random { public static Vector3 insideUnitSphere { get { return new Vector3(); } } public static float Range(float a, float b) { return a; } }
    internal static class Debug { public static void LogWarning(string x) {} }
}
namespace UnityEngine.SceneManagement
{
    internal struct Scene { public int buildIndex; public bool isLoaded; public string name;public int handle; public static bool operator ==(Scene a,Scene b){return a.handle==b.handle;}public static bool operator !=(Scene a,Scene b){return !(a==b);}public override bool Equals(object o){return o is Scene && this==(Scene)o;}public override int GetHashCode(){return handle;} }
    internal static class SceneManager { internal static void MoveGameObjectToScene(UnityEngine.GameObject go,Scene scene){go.scene=scene;}internal static int Handle=1; public static Scene GetActiveScene() { return new Scene { name="arena", isLoaded=true, handle=Handle }; } }
}
namespace ItemStatsSystem
{
    internal partial class Item
    {
        internal Inventory Inventory=new Inventory();internal List<Slot> Slots=new List<Slot>();internal Item Root;
        internal bool Destroyed;internal int DestroyFailures;
        internal int GetInstanceID() {return GetHashCode();}
        internal void Detach() {if(Root!=null)Root.Inventory.Content.Remove(this);}
        internal void DestroyTree() {if(DestroyFailures>0){DestroyFailures--;throw new Exception("destroy fault");}Destroyed=true;}
        internal int TypeID, StackCount, MaxStackCount = 1000000;
        internal int GetTotalRawValue() { return 100; }
        internal void Drop(UnityEngine.Vector3 p, bool b, UnityEngine.Vector3 d, float f) { if(TypeID==1254) Probe.Crowns+=StackCount; else Probe.Cash+=StackCount; }
    }
    internal partial class Inventory : UnityEngine.MonoBehaviour {internal List<Item> Content=new List<Item>();}
    internal class Slot {internal Item Content;}
    internal static class ItemAssetsCollection
    { internal static Item GetPrefab(int id) { return new Item { TypeID=id }; } internal static Item InstantiateSync(int id) { return new Item { TypeID=id }; } }
    internal static class Probe { internal static long Cash, Crowns; }
}
namespace Duckov.Economy
{
    internal static class EconomyManager
    { internal const int CashItemID=1; internal static object Instance=new object(); internal static long Money {get{return BossRush.BossRushCoop.Current.Money;}set{BossRush.BossRushCoop.Current.Money=value;}} internal static bool Add(long x) { Money+=x;if(BossRush.BossRushCoop.Current.ThrowAfterCredit){BossRush.BossRushCoop.Current.ThrowAfterCredit=false;throw new Exception("credit notification");}return true; } }
}
namespace BossRush
{
    internal static class CoopAiPredictionHealthProtection { internal static void MarkReplica(CharacterMainControl c) { } }
    internal class Health { public bool IsDead; public float MaxHealth=100; internal CharacterMainControl Character; internal static event Action<Health, DamageInfo> OnDead; internal T GetComponent<T>() where T:class { return Character as T; } }
    internal struct DamageInfo { }
    internal class CharacterMainControl : UnityEngine.MonoBehaviour
    { internal void SetPosition(UnityEngine.Vector3 value){transform.position=value;}public Health Health=new Health();public ItemStatsSystem.Item CharacterItem=new ItemStatsSystem.Item(); internal CharacterMainControl() {Health.Character=this;} internal static CharacterMainControl Main { get { return BossRushCoop.Current.Character; } } }
    internal static class CharacterMainControlExtensions { internal static bool IsMainCharacter(CharacterMainControl c) {return c==CharacterMainControl.Main;} }
    internal sealed partial class BossRushCoopPlayer { public CharacterMainControl Character; public bool IsAlive { get { return IsConnected && Character!=null && !Character.Health.IsDead; } } }
    internal sealed partial class TestPeer { internal CharacterMainControl Character = new CharacterMainControl(); }
    internal static partial class BossRushCoop
    {
        internal static IReadOnlyList<BossRushCoopPlayer> Players { get { var list=new List<BossRushCoopPlayer>();foreach(var peer in Peers) list.Add(FindPlayer(peer.Id));return list; } }
        internal static string GetEntityId(CharacterMainControl c) { return c==null?null:"a:"+c.GetInstanceID(); }
        internal static CharacterMainControl FindEntity(string id) { return null; }
        internal static bool IsPlayer(CharacterMainControl c) { return Peers.Exists(p=>p.Character==c); }
        internal static bool AnyPlayerAlive {get {return Peers.Exists(p=>p.Connected&&!p.Character.Health.IsDead);} }
    }
    internal static class BossRushInitialSpawn {internal static bool HasArrived(UnityEngine.Vector3 target){return false;}}
    internal static class BossRushMapSelectionHelper {internal static int Clears; internal static void ClearPendingEntryFlowState() {Clears++;} }
    internal static class BossRushUI { internal static bool IsGamePaused() { return false; } }
    internal static class L10n { internal static string T(string a,string b=null) { return b??a; } }
    internal static class ModeHRuntimeGates {internal static bool IsLegacyModeEntryAllowed(){return true;}internal static string ResolveLegacyBlockedMessageKey(){return "blocked";}}
    internal static class AchievementTracker { internal static bool HasTakenDamage,HasUsedHealItem,HasPickedUpItem;internal static float ArenaEnterTime; }
    internal class Sign { internal void SetVictoryMode() {} internal void SetCheerMode() {} }
    internal partial class ModBehaviour
    {
        internal WavesArenaRuntimeModule wavesArenaRuntime; private bool bossRushArenaActive; private object currentBoss;
        private bool modeDActive {get{return D!=null&&D.modeDActive;}}private bool modeEActive {get{return IsModeEActive;}}
        private readonly IntegrationStub bossRushIntegrationRuntime=new IntegrationStub();
        private void SetBossRushRuntimeActive(bool active) {IsActive=active;}
        private void OnEnemyDiedWithDamageInfo(Health health,DamageInfo info) { }
        private void LogLootWarningLimited(string message) { }
        private void EndModeD() {D.modeDActive=false;}
        private void EndModeE() {IsModeEActive=false;}
        internal void PlayerDied(Health health) {OnPlayerDeathInBossRush_LootAndRewards(health,new DamageInfo());}
        internal bool IsActive=true, IsModeDActive, IsModeEActive, Naked=true;internal ModeDRuntimeModule D;
        internal int ModeDConfiguredEnemiesPerWave;
        internal void StartModeD(){D.StartModeD();}internal void ShowBigBanner(string x){}
        internal void ResetArenaForModeD(){}internal void ClearModeDEnemyRecoveryState(){}
        internal void InitializeModeDEnemyPoolsForRuntime(){}internal void TryRollMutatorsForArena(string x){}
        internal Sign ArenaRewardSignInteract = new Sign(); internal UnityEngine.GameObject ArenaRewardSignGameObject=new UnityEngine.GameObject();
        internal int Begins, Clears, Courier, BossKills, Hell, DFlawless, DClears, Flawless;
        internal int EntryRequests; internal void StartCoopArenaEntryForScene(UnityEngine.SceneManagement.Scene scene) { EntryRequests++; }
        internal static UnityEngine.Vector3 GetDefaultPositionForScene(string scene){return GetCurrentSceneDefaultPosition();}
        internal static void DevLog(string x) {} internal static UnityEngine.Vector3 GetCurrentSceneDefaultPosition() { return new UnityEngine.Vector3(); }
        internal void ShowMessage(string x) {} internal void StartBossRush() {} internal bool IsCurrentSceneValidBossRushArena() { return true; }
        internal void UnregisterEnemyRecoveryForArena(CharacterMainControl c) { }
        internal bool IsPlayerNaked() { return Naked; }
        internal void BeginAchievementSessionForArena(string x) { Begins++; AchievementTracker.HasTakenDamage=false;AchievementTracker.ArenaEnterTime=UnityEngine.Time.time; }
        internal void CheckClearAchievementsForArena() { Clears++;if(!AchievementTracker.HasTakenDamage)Flawless++; }
        internal void NotifyCourierBossRushCompleted() { Courier++; }
        internal bool CheckBossKillAchievementsOnceForArena(CharacterMainControl c,string bossType=null) { BossKills++;return true; }
        internal void CheckInfiniteHellAchievementsForArena(int wave) { Hell=wave; }
        internal void CheckModeDFlawlessAchievementForRuntime() { DFlawless++; }
        internal void CheckModeDClearAchievementsForRuntime() { DClears++; }
    }
    internal sealed class IntegrationStub {internal void CleanupAmmoShopOnPlayerDeath(Action<string> warn) { } }
    internal sealed partial class WavesArenaRuntimeModule
    {
        private int waveGeneration;private object milestoneDelivery;private void SetBossRushRuntimeActive(bool active){IsActive=active;}
        private ModBehaviour owner; internal bool IsActive=true,ArenaActive,ArenaPlanned,InfiniteHellMode,WaitingForNextWave;
        internal int CurrentEnemyIndex,TotalEnemies,DefeatedEnemies,BossesPerWave=1,BossesInCurrentWaveRemaining,BossesInCurrentWaveTotal,InfiniteHellWaveIndex;
        internal float WaveCountdown;internal string NextWaveBossName;internal long InfiniteHellCashPool;
        private long InfiniteHellWaveCashThisWave;
        private readonly HashSet<CharacterMainControl> CountedDeadBosses=new HashSet<CharacterMainControl>();
        internal UnityEngine.MonoBehaviour CurrentBoss;internal List<UnityEngine.MonoBehaviour> CurrentWaveBosses=new List<UnityEngine.MonoBehaviour>();
        internal int Boxes, Quality;
        internal WavesArenaRuntimeModule(ModBehaviour x) { owner=x;x.wavesArenaRuntime=this;InitializeCoopArena(); }
        private void ConfigureBossRushMode(int x,bool b) {} private void StartFirstWave() {} private void SpawnNextEnemy() {}
        private void SpawnDifficultyRewardLootboxAtWorldPosition_LootAndRewards(int quality,UnityEngine.Vector3 position) { Boxes++;Quality=quality; }
        internal void Begin() { BeginCoopArenaRun(); } internal void Wave() { CaptureCoopArenaWaveParticipants(); }
        internal int Copies { get { return CoopArenaRewardCopies; } } internal string Epoch { get { return coopArenaEpoch; } } internal int RunSequence { get { return coopArenaRunSequence; } }
        internal void Fact(string kind,int wave=0) { DeliverCoopArenaFact(kind,wave); } internal void Boss(CharacterMainControl boss) { HandleBossDeath(boss,new DamageInfo()); }
        internal int WaveAdvances;
        private void ProceedAfterWaveFinished() { WaveAdvances++; }
        internal void Tick() { RetireLostCoopArenaProjection();coopPersonal.Tick(1); }internal void World(CoopArenaState state){ApplyCoopArena(state);} internal void Snapshot() { coopChannel.Tick(1); }
        internal bool Victory() { return TryBeginCoopArenaVictory(); } internal void QueueVictory(int quality) { QueueCoopArenaVictoryCopies(quality); }
        internal void TickBoxes() { TickCoopArenaVictoryCopies(); } internal bool Apply(CoopArenaPersonal fact) { return ApplyCoopArenaPersonal(fact); }
        internal void Dispose() { coopPersonal.Dispose();coopChannel.Dispose(); }
    }
    internal class Pool { internal int Kits;internal bool Fail,FailDestroy;internal Action Callback;internal ItemStatsSystem.Item Last;
        internal void InitializeModeDItemPools(Func<string,object> x) {}internal object FindTagByName(string s) { return null; }internal void EnsureModeDGlobalItemPool(){}
        internal bool GivePlayerStarterKit() {Kits++;var root=CharacterMainControl.Main.CharacterItem;Last=new ItemStatsSystem.Item{Root=root,DestroyFailures=FailDestroy?1:0};root.Inventory.Content.Add(Last);if(Callback!=null)Callback();return !Fail;} }
    internal sealed partial class ModeDRuntimeModule
    {
        private ModBehaviour owner;private static ModeDRuntimeModule current;internal bool modeDActive,modeDWaveCompletePending,modeDCleanupPending,lastStartSucceeded;
        internal int Generation,modeDEnemiesPerWave;private int generation;
        internal int modeDWaveIndex=10,modeDExpectedEnemiesInCurrentWave,modeDSpawnResolvedInCurrentWave;
        internal readonly List<CharacterMainControl> modeDCurrentWaveEnemies=new List<CharacterMainControl>();internal Pool ItemPool=new Pool();
        internal ModeDRuntimeModule(ModBehaviour x) {owner=x;x.D=this;InitializeCoopModeD();}
        private void SetupSignForModeD(Sign s) {} private void ShowModeDNextWaveOption() {} private bool ModeDStartNextWave() {return true;}
        internal static void Invalidate(ModBehaviour x){if(current!=null&&current.owner==x)current.Generation++;}
        private void CleanupModeDRuntimeOnDestroy(){modeDActive=false;}
        internal bool StartActual(){current=this;return TryStartModeD(owner);}
        internal bool PendingFor(string id){return coopPersonal.HasPendingForPlayers(new[]{id});}
        internal string Epoch {get{return coopEpoch;}}
        internal int RollbackDebt {get{return coopModeDStarterRollback.Count;}}
        internal int PendingAdmission {get{return ((System.Collections.ICollection)coopChannel.GetType().GetField("pendingRequests",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(coopChannel)).Count;}}
        internal void Freeze() { current=this;CaptureCoopModeDWaveParticipants();modeDWaveCompletePending=false; }internal void Complete() { modeDWaveCompletePending=true;DeliverCoopModeDWaveComplete(); }
        internal void Snapshot() {coopChannel.Tick(1);}internal void Tick() {TickCoopModeD(1);}internal void Dispose() {coopChannel.Dispose();coopPersonal.Dispose();}
    }
}
