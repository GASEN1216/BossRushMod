using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object {internal bool Destroyed;public static void Destroy(Object value){if(ReferenceEquals(value,null))return;value.Destroyed=true;GameObject body=value as GameObject;if(body!=null)foreach(Object component in body.Components)component.Destroyed=true;}public static bool operator ==(Object a,Object b){return (ReferenceEquals(a,null)||a.Destroyed)?ReferenceEquals(b,null)||b.Destroyed:ReferenceEquals(a,b);}public static bool operator !=(Object a,Object b){return !(a==b);}public override bool Equals(object obj){return this==obj as Object;}public override int GetHashCode(){return base.GetHashCode();}}
    internal sealed class GameObject : Object {internal string name;internal static GameObject CreatePrimitive(PrimitiveType kind){return new GameObject();}internal T GetComponent<T>() where T:class{return null;}internal readonly List<Object> Components=new List<Object>();internal readonly Transform transform=new Transform();internal bool active=true;internal void SetActive(bool value){active=value;}internal T AddComponent<T>() where T:new(){T value=new T();Object component=value as Object;if(component!=null)Components.Add(component);return value;} }
    public struct Vector3
    {
        public float x, y, z;public static Vector3 forward {get{return new Vector3(0,0,1);}}public static Vector3 down {get{return new Vector3(0,-1,0);}}public Vector3 normalized{get{float m=magnitude;return m>0?this*(1f/m):zero;}} public static Vector3 zero {get{return new Vector3();}}
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float magnitude {get{return (float)Math.Sqrt(sqrMagnitude);}} public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 up {get{return new Vector3(0,1,0);}}public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 operator *(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}
    }
    public sealed class Transform {public Vector3 forward=Vector3.forward;public Quaternion rotation; public Vector3 position; }
    internal enum PrimitiveType {Capsule}
    internal sealed class Renderer {}
    internal struct RaycastHit {internal Vector3 point;}
    internal static class Physics {internal static bool Raycast(Vector3 origin,Vector3 direction,out RaycastHit hit,float distance){hit=new RaycastHit();return false;}}
    public struct Quaternion {private float angle;public static Quaternion Euler(float x,float y,float z){return new Quaternion {angle=y*(float)Math.PI/180f};}public static Quaternion LookRotation(Vector3 direction){return new Quaternion();}public static Vector3 operator *(Quaternion q,Vector3 v){return new Vector3(v.x*(float)Math.Cos(q.angle)+v.z*(float)Math.Sin(q.angle),v.y,v.z*(float)Math.Cos(q.angle)-v.x*(float)Math.Sin(q.angle));}}
    public static class Mathf { public static int RoundToInt(float value){return (int)Math.Round(value);} public static int Max(int a, int b) { return Math.Max(a,b); } }
    internal sealed class WaitForSeconds {internal WaitForSeconds(float seconds){}}
    public static class Time { public static float unscaledTime; public static float unscaledDeltaTime=1; }
}
public sealed class Health : UnityEngine.Object { public bool IsDead; }
public sealed class CharacterMainControl : UnityEngine.Object { public static CharacterMainControl Main; public readonly Health Health=new Health(); public readonly UnityEngine.Transform transform = new UnityEngine.Transform(); internal readonly UnityEngine.GameObject gameObject=new UnityEngine.GameObject();internal CharacterMainControl(){gameObject.Components.Add(this);gameObject.Components.Add(Health);}internal T GetComponent<T>() where T:class{return null;} }
public sealed class CountDownArea { public readonly UnityEngine.Transform transform = new UnityEngine.Transform(); }
namespace Duckov.Economy { public static class EconomyManager { public static bool Succeed = true; public static long Cash; public static bool Add(long amount) { if(!Succeed)return false; Cash+=amount;return true; } } }
namespace Duckov.UI { public static class NotificationText { public static string Last; public static bool Throw; public static void Push(string text) { Last=text; if (Throw) throw new InvalidOperationException("notification fault"); } } }
namespace BossRush
{
    internal static class CampaignContentCatalog { internal const string ModeModeF="modeF",ModeZombie="zombie"; }
    internal static class CampaignObjectiveTracker {
        internal sealed class ExtractionLease {internal string Chapter,Asset;internal bool Reported;}
        internal static readonly List<string> Waves=new List<string>();internal static void ObserveExtractionLeaseWave(ExtractionLease lease,int wave) {if(lease!=null)Waves.Add(lease.Chapter+":"+wave);}
        internal static string Chapter="old";internal static bool SaveAvailable=true;internal static readonly List<string> Reports=new List<string>();
        internal static ExtractionLease CaptureExtractionLease(string mode) { return new ExtractionLease {Chapter=Chapter,Asset=BossRushCoop.LocalAssetId}; }
        internal static bool ReportExtractionLease(ExtractionLease lease) {if(lease==null||lease.Reported)return true;if(lease.Asset!=BossRushCoop.LocalAssetId||!SaveAvailable)return false;Reports.Add(lease.Chapter);lease.Reported=true;return true;}
    }

    internal static class L10n
    {
        private static readonly Dictionary<string,string[]> Text=new Dictionary<string,string[]>();
        internal static bool Chinese;
        static L10n(){ZombieCoopLocalization.Register();}
        internal static void Register(string key,string cn,string en){Text.Add(key,new[]{cn,en});}
        internal static string T(string key){string[] value;return Text.TryGetValue(key,out value)?value[Chinese?0:1]:"{0}";}
        internal static string T(string a,string b) { return b; }
    }
    internal sealed class BossRushCoopPlayer
    {
        internal string Id, AssetId = "slot-A";
        internal bool IsConnected = true, IsAlive = true;
        internal readonly CharacterMainControl Character = new CharacterMainControl();
    }
    internal static class BossRushCoop
    {
        internal static bool IsConnected = true;
        internal static bool HasBoundScene=true; internal static bool IsHost=true;internal static bool HasWorldAuthority {get{return !IsConnected||IsHost;}set{IsHost=value;}}internal static bool IsClient {get {return IsConnected&&!IsHost;}}internal static string RunId="z-room",SceneToken="z-token";
        internal static string LocalAssetId = "slot-A"; internal static string LocalPlayerId = "p0";
        internal static BossRushCoopPlayer FindPlayer(string id) {return Players.Find(x=>x.Id==id);}
        internal static CharacterMainControl FindEntity(string id){return null;}internal static CharacterMainControl GetCombatTarget(UnityEngine.Vector3 origin){return CharacterMainControl.Main;}
        internal static readonly List<BossRushCoopPlayer> Players = new List<BossRushCoopPlayer>();
        internal static bool AnyPlayerAlive { get { return Players.Exists(p=>p.IsConnected && p.IsAlive); } }
    }
    internal static class BossRushUILayers { internal const int ZombieModal=300; }
    internal static class BossRushConfirmDialog {
        internal sealed class Options {internal string Title,Body,ConfirmLabel,CancelLabel;internal bool Danger;internal int SortingOrder;internal UnityEngine.GameObject Anchor;internal Action OnConfirm;}
        internal static Options Last;internal static int Shows;internal static void Show(Options options) {Last=options;Shows++;}
    }
    internal sealed partial class ZombieModeStarterChoiceView {
        private bool chosen;private Func<bool> canStartCoop;
        private Action startCoop;private UnityEngine.GameObject choicePanel,waitingPanel;
        private readonly UnityEngine.Transform transform=new UnityEngine.Transform();
        internal void BindForTest(Func<bool> canStart) {chosen=true;canStartCoop=canStart;}
        internal bool CanReopenForTest() {return KeepCoopHostStartChoice();}
        internal void BuildForTest(Action start) {startCoop=start;choicePanel=new UnityEngine.GameObject();BuildCoopHostWaiting();}
        internal bool ChoiceHidden {get{return !choicePanel.active&&waitingPanel!=null;}}
    }
    internal static class ZombieModeTuning { internal const float NavMeshLiftOffset=.1f,NavMeshSafeZoneRadius=3;internal const float ExtractionAreaTriggerRadius = 3f;internal const long CashToPurificationRatio=1; }
    internal enum ZombieModeCombatPhase { InitialPreparation,Combat, SuccessExit,RewardSelection,ExtractionOpportunity,None }
    internal enum ZombieModeLifecyclePhase { None,WaitingStarterChoice,Active,Exiting,SelectingMap,InitializingRun }
    internal enum ZombieModeFailureReason { SuccessfulExtraction,Unknown,StarterLoadoutFailed,InitializationFailed }
    internal enum ZombieModeStarterLoadout {Melee,Gunner}
    internal sealed class ModBehaviour { internal bool CampaignEnabled;internal bool IsCampaignConfiguredEnabled() { return CampaignEnabled; }internal int Extracts,Selections,ContinueClicks,ExtractClicks,ProjectileImpacts;internal UnityEngine.Vector3 ImpactPosition;internal void TryExecuteZombieModeHarasserProjectileImpact(int run,CharacterMainControl source,UnityEngine.Vector3 point,float damage,float slowRadius,float slowPercent,float slowDuration){ProjectileImpacts++;ImpactPosition=point;}internal int ExtractsUnused;internal void ContinueZombieModeAfterExtractionOpportunity(int run){ContinueClicks++;}internal string StartZombieModeExtractionFromUi(int run){ExtractClicks++;return null;}internal void SelectZombieModeReward(int run,ZombieModeRewardType reward){Selections++;}internal static BossRushMapConfig[] GetAllMapConfigs(){return new BossRushMapConfig[0];}internal bool IsAnyBossRushLikeModeActive(){return false;}internal int GetZombieModeBossRewardSelectionCountForRewardRuntimeModule(int wave){return 2;}internal void ShowBigBanner(string text) { } internal void NotifyCampaignZombieExtracted() { Extracts++; } internal static void DevLog(string value) { }internal int Cleanups,WorldTicks;internal void CleanupZombieModeForRuntimeModule(ZombieModeFailureReason reason) {Cleanups++;}internal void TickZombieModeWaveControllerForRuntimeModule(float dt){WorldTicks++;}internal void TickZombieModeBossControllerForRuntimeModule(float dt){}internal void UpdateModeFFortificationHighlightsForRuntimeModule(){}internal void UpdateFortPlacementMode(){}internal void UpdateModeFRepairSelection(){}internal bool ReadSceneLoaderDoneWithWarningForRuntimeModule(string reason){return true;}internal bool ReadLevelInitedWithWarningForRuntimeModule(string reason){return true;} }
    internal sealed class FlowState { internal CountDownArea ActiveExtractionArea=new CountDownArea();internal bool ZombieModeResourcesCommitted,EntryResourcesFinalized;internal ZombieModeStarterLoadout StarterLoadout;internal bool ExtractionChanneling,BeaconChanneling;internal int SceneBuildIndex=-1;internal bool IsCleaningUp;internal int RunId = 11, CurrentWave = 5, PurificationPoints; internal bool ExtractionSuccessHandled; internal ZombieModeCombatPhase CombatPhase;internal ZombieModeLifecyclePhase LifecyclePhase=ZombieModeLifecyclePhase.Active;
        internal readonly List<object> TemporaryNpcs=new List<object>(),TemporaryRealNpcs=new List<object>();internal long PendingCashInvestment,ConfirmedCashInvested;internal object MapProfile;internal ZombieModeRewardNode CurrentRewardNode;internal int CurrentWaveKills,CurrentWaveKillTarget,CurrentWaveBossesRemaining,LivingZombieCount,LivingNormalZombieCount,PollutionFromNatural,PollutionFromContracts,PendingEliteSquadCount;internal float PreparationTimer,ActiveSafeZoneRadius,PortableSafeZoneRadius;internal UnityEngine.Vector3 ActiveSafeZoneCenter,PortableSafeZoneCenter;internal bool ActiveSafeZoneActive,PortableSafeZoneActive;internal UnityEngine.GameObject ActiveSafeZoneVisual,PortableSafeZoneVisual;internal void ResetForNewRun(int run,int index,string name){RunId=run;CurrentWave=0;}}
    internal sealed class ZombieModeEntryTransaction { internal bool EntryResourcesFinalized; internal bool CashTemporarilyHeld=true, InvitationTemporarilyHeld=true; internal long CashWithheldAmount=250;internal void Reset(){EntryResourcesFinalized=CashTemporarilyHeld=InvitationTemporarilyHeld=false;CashWithheldAmount=0;} }
    internal static class ZombieModeEntryDebt { internal static long Cash;internal static int Invitations;internal static bool CashFail;internal static bool RefundCash(long x) { if(CashFail)return false;Cash+=x;return true; }internal static bool RefundInvitation() { Invitations++;return true; } }
    internal class RuntimeBase { public virtual void OnUpdate(float deltaTime,float unscaledDeltaTime) { } }
    internal sealed class FakeChannel { internal sealed class Entry {internal string Player,Id,Asset;internal CoopZombiePersonal Receipt;}internal readonly List<Entry> Pending=new List<Entry>();internal readonly List<CoopGameplayCommand> Commands=new List<CoopGameplayCommand>();internal readonly List<string> Repeats=new List<string>(),Requests=new List<string>(); internal void Request(string action,int value,string text,bool flag) {Requests.Add(action+":"+value);Commands.Add(new CoopGameplayCommand {Action=action,Value=value,Text=text,Flag=flag,PlayerId=BossRushCoop.LocalPlayerId,AssetId=BossRushCoop.LocalAssetId});} internal void RequestRepeat(string action,int value,string text,bool flag) {Repeats.Add(action+":"+value);} internal bool HasPendingConnectedDeliveries;internal bool HasPendingForIdPrefix(string prefix) { return HasPendingConnectedDeliveries; }internal void Publish() {} internal void Tick(float dt) { }internal bool Deliver(string player,string id,CoopZombiePersonal receipt,string asset) { Pending.Add(new Entry {Player=player,Id=id,Asset=asset,Receipt=BossRushCoopJson.FromJson<CoopZombiePersonal>(BossRushCoopJson.ToJson(receipt))}); return true; } }
    internal sealed partial class ZombieModeRuntimeModule : RuntimeBase
    {
        private readonly FakeChannel coopZombieWorld=new FakeChannel(),coopZombiePersonal=new FakeChannel();
        private bool coopZombieExtractionPending,coopZombiePersonalDeath,coopZombieStarterChosen;
        private float coopZombieReadyRetry;private int coopZombieCompletedRewardWave=-1;private int Preparations;
        private string[] coopZombieConfirmedStarter; private bool coopZombieStarterGranted;
        private readonly HashSet<string> coopZombieSkipped=new HashSet<string>(),coopZombieSkipQueued=new HashSet<string>();
        private readonly Dictionary<string,string> coopZombieSkippedAssets=new Dictionary<string,string>();
        private int Granted,RolledBack; private bool GrantSucceeds=true;
        private bool GrantZombieModeStarterLoadout(ZombieModeStarterLoadout loadout){if(!GrantSucceeds)return false;Granted++;return true;}
        private void FailZombieModeBeforeActive(ZombieModeFailureReason why) {}
        private void RollbackZombieModeInventoryTransfer(){RolledBack++;}
        private void RefundZombieModeInvitationIfNeeded(){} private void RefundZombieModeCashIfNeeded(){}
        private void TryNotifyZombieModeExtraction() { }private void TryLoadBaseSceneAfterZombieModeExtraction() { }
        private void SettleZombieModeFailureInsuranceShell(int run) { }private void RemoveZombieModeAttributeModifiers() { }private void RemoveZombieModeOptionRuntimeEffects() { }
        private void UnlockZombieModeContainersForActiveRun(int run) { }
        private void BeginZombieModePreparation(int run,bool first,bool keep) { Preparations++;runState.CombatPhase=ZombieModeCombatPhase.Combat; }
        private void BeginZombieModeExtractionOpportunity(int run) { }private void FailZombieModeActive(int run) { }
        private readonly Dictionary<string,CoopZombieExtractionEligibility> coopZombieExtractionEligibility=new Dictionary<string,CoopZombieExtractionEligibility>();
        private readonly HashSet<string> coopZombieStarterReady = new HashSet<string>();
        private readonly Dictionary<string,int> coopZombieRewardReady = new Dictionary<string,int>();
        private readonly Dictionary<string,string> coopZombieParticipants = new Dictionary<string,string>();
        private readonly HashSet<string> coopZombieDeclinedAssets = new HashSet<string>();
        private readonly Dictionary<string,CoopZombieRefund> coopZombieRefunds = new Dictionary<string,CoopZombieRefund>();
        private int coopZombieEntrySequence;
        private bool coopZombieLeaseCaptured,coopZombieLeaseConnected,coopZombieLeaseHost,coopZombieLeaseRevoked,coopZombieLeaseRetired,pendingEntry;private string coopZombieLeaseRoom;private string coopZombieWalletRoom=BossRushCoop.RunId,coopZombieProjectionRoom;private bool coopZombieAdmissionPending;
        private readonly Dictionary<string,object> coopZombieTerminals=new Dictionary<string,object>(),coopZombieRealNpcs=new Dictionary<string,object>();
        private readonly Dictionary<string,CoopZombieWallet> coopZombieWallets = new Dictionary<string,CoopZombieWallet>();
        private readonly Dictionary<string,int> coopZombiePointsIssued=new Dictionary<string,int>();private long coopZombieReceiptSequence;
        private readonly HashSet<string> coopZombieWorldSelections=new HashSet<string>();private int coopZombieLocalRun,coopZombieDeclinedRun,coopZombieLocalRewardWave;
        private string coopZombieLocalAsset="slot-A";
        private readonly ZombieModeEntryTransaction entryTransaction = new ZombieModeEntryTransaction();
        private bool ShouldRollbackZombieModeEntryResources() { return true; }
        private readonly FlowState runState = new FlowState();
        private static ZombieModeRuntimeModule coopZombieOwner;private object zombieModeRewardUiToken;private int coopZombieExtractionUiWave=-1;private CharacterMainControl coopZombieCommandPlayer;
        private int KillRewardRecords;private void ResetCoopZombieKillRewards(){}private void TickCoopZombieKillRewards(float dt){}private void ResetCoopZombieZoneEffects(){}private void TickCoopZombieZoneEffects(float dt){}private void ResetCoopZombieDropClaims(int run){}private void TickCoopZombieDropClaims(float dt){}private void RecordCoopZombieKillRewardSelection(BossRushCoopPlayer player,ZombieModeRewardType reward){KillRewardRecords++;}
        private readonly ModBehaviour owner = new ModBehaviour();
        private bool IsPlayerInsideZombieModeExtractionArea(CountDownArea area) { return true; }
        private bool TryDispatchZombieModeExtractionSuccess(CountDownArea area) { return true; }private void TryReleaseZombieModeExtractionCountdownUi() { }private int ExtractionShows,ExtractionClears;private void ShowZombieModeExtractionOpportunityUi(int run) { ExtractionShows++; }
        private bool OfficialReady=true;private bool IsOfficialLevelAfterInitForZombieMode(){return OfficialReady;}private const int InitialPurificationPoints=5;private int Initialized;private int BeginZombieModeRunShell(int index,string name){return runState.RunId;}private bool InitializeZombieModeRunAfterMapLoaded(int run){Initialized++;return true;}private void ResetZombieModeRuntimePauseClock(){}private void RefreshZombieModeRuntimePauseClock(){}private bool IsZombieModeRuntimePaused(){return false;}private void TickZombieModeDropsAndPerformance(float dt){}private void TickZombieModeTemporaryNpcProtection(){}private object BuildZombieModeMapProfile(int i,string name){return new object();}private Action Admit;private void ShowZombieModeCashInvestmentPrompt(Action accept,Action cancel){Admit=accept;}private bool TryRunZombieModePrechecks(out ZombieModeFailureReason reason){reason=ZombieModeFailureReason.Unknown;return true;}private int EntryCommits;private bool CommitZombieModeEntryResourcesShell(out ZombieModeFailureReason reason){EntryCommits++;reason=ZombieModeFailureReason.Unknown;return true;}private bool InitializeCoopZombiePersonalRun(int run){runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;return true;}private void ClearZombieModeEnemyInstanceIds(){}private void RegisterZombieModeEnemyInstanceId(CharacterMainControl character,ZombieModeEnemyRuntimeMarker marker){}private void MirrorCoopZombieTerminals(CoopZombieTerminal[] terminals){}private void UpdateZombieModeSafeZoneVisual(){}private void TryGiveZombieModeWaveClearHealingItem(){}private void ShowZombieModeRewardSelection(int run,bool boss){}private void EnsureZombieModeExtractionArea(int run){if(runState.ActiveExtractionArea==null)runState.ActiveExtractionArea=new CountDownArea();}private void ClearZombieModeExtractionOpportunityUi(){ExtractionClears++;}private void DestroyZombieModeActiveExtractionArea(){runState.ActiveExtractionArea=null;}private UnityEngine.GameObject CreateZombieModeFlatZoneVisual(string name,UnityEngine.Vector3 center,float radius,float height,UnityEngine.Color color){var body=new UnityEngine.GameObject();body.transform.position=center;return body;}private void AttachZombieModeSafeZoneBoundaryVisual(UnityEngine.GameObject visual,UnityEngine.Vector3 center,float radius){}private void RegisterZombieModeRunOnlyObject(int run,ZombieModeRunOnlyObjectKind kind,UnityEngine.GameObject gameObject,object target,Action cleanup){}
        private int Beacons,Portables,Continues,Extractions,Npcs,Gravity;private bool TryUseZombieModeBeacon(){Beacons++;return true;}private bool TryUseZombieModePortableSafeZoneDevice(){Portables++;return true;}private void ContinueZombieModeAfterExtractionOpportunity(int run){Continues++;}private string StartZombieModeExtractionFromUi(int run){Extractions++;return null;}private void RefreshZombieModeGravityWellTargets(int run,UnityEngine.Vector3 origin,float radius,float strength){Gravity++;}private void SpawnCoopZombieRewardNpc(ZombieModeRewardType reward){Npcs++;}
        private bool IsZombieModePositionInsideActiveSafeZone(UnityEngine.Vector3 position) { position.y=0f; return position.sqrMagnitude<=9f; }
        private static int assertions;
        private static void Check(bool value,string label) { assertions++; if(!value)throw new Exception(label); }
        internal static void Run()
        {
            var module = new ZombieModeRuntimeModule();
            var area = new CountDownArea();
            for(int i=0;i<16;i++) {BossRushCoop.Players.Add(new BossRushCoopPlayer { Id="p"+i });module.coopZombieParticipants["p"+i]="slot-A";}
            Check(!module.CoopZombieEveryoneReady(true),"starter waits for all 16");
            for(int i=0;i<15;i++)module.coopZombieStarterReady.Add("p"+i+":slot-A");
            Check(!module.CoopZombieEveryoneReady(true),"last participant cannot be skipped");
            BossRushCoop.Players[15].IsConnected=false;
            Check(module.CoopZombieEveryoneReady(true),"disconnect stops blocking starter");
            BossRushCoop.Players[15].IsConnected=true;
            Check(!module.CoopZombieEveryoneReady(true),"reconnected living participant must choose");
            BossRushCoop.Players[15].IsAlive=false;
            Check(module.CoopZombieEveryoneReady(true),"dead participant does not block starter");
            for(int i=0;i<15;i++)module.coopZombieRewardReady["p"+i+":slot-A"]=5;
            Check(module.CoopZombieEveryoneReady(false),"each living reward committed");
            module.coopZombieRewardReady["p3:slot-A"]=4;
            Check(!module.CoopZombieEveryoneReady(false),"previous-wave receipt cannot advance current node");
            module.coopZombieRewardReady["p3:slot-A"]=5;
            Check(module.CoopZombieAllInsideExtraction(area),"all living participants in horizontal circle");
            BossRushCoop.Players[3].Character.transform.position=new UnityEngine.Vector3(0,20,0);
            Check(module.CoopZombieAllInsideExtraction(area),"vertical character pivot offset does not change circle");
            BossRushCoop.Players[3].Character.transform.position=new UnityEngine.Vector3(3.1f,0,0);
            Check(!module.CoopZombieAllInsideExtraction(area),"one participant outside blocks extraction");
            Check(!module.CoopZombieAllPlayersInsideSafeZone(),"outside teammate prevents global threat suppression");
            BossRushCoop.Players[3].IsConnected=false;BossRushCoop.Players[3].IsAlive=false;
            Check(module.CoopZombieAllInsideExtraction(area),"disconnect does not block extraction");
            Check(module.CoopZombieAllPlayersInsideSafeZone(),"remaining party protected");
            BossRushCoop.Players[2].AssetId="slot-B";
            Check(!module.IsCoopZombieParticipant(BossRushCoop.Players[2]),"new slot cannot inherit original admission");
            Check(module.CoopZombieEveryoneReady(false),"slot-switched observer cannot block admitted party");
            BossRushCoop.Players[2].AssetId="slot-A";
            Check(module.IsCoopZombieParticipant(BossRushCoop.Players[2]),"original asset restores original admission");
            foreach(var player in BossRushCoop.Players)player.IsAlive=false;
            Check(!module.CoopZombieEveryoneReady(false),"empty alive set cannot advance wave");
            Check(!module.CoopZombieAllInsideExtraction(area),"empty alive set cannot succeed extraction");
            Check(module.GetCoopZombieRewardPollution(ZombieModeRewardType.ContractPollutionDeal)==2,"original boss pollution");
            module.runState.CurrentWave=6;
            Check(module.GetCoopZombieRewardPollution(ZombieModeRewardType.ContractPollutionDeal)==1,"original normal pollution");
            Check(module.GetCoopZombieRewardPollution(ZombieModeRewardType.InsuranceNearFull)==5,"original insurance pollution");
            module.runState.PurificationPoints=80;
            Check(!module.ApplyCoopZombiePersonal(new CoopZombiePersonal { Run=10,Kind="points",Amount=100 }),"old run cannot credit new wallet");
            Check(module.runState.PurificationPoints==80,"old receipt did not change wallet");
            Duckov.Economy.EconomyManager.Succeed=false;
            Check(!module.ApplyCoopZombiePersonal(new CoopZombiePersonal { Run=11,Kind="extract" }),"failed payout is not acknowledged");
            Check(module.runState.PurificationPoints==80 && !module.runState.ExtractionSuccessHandled,"failed payout preserves assets and success state");
            Duckov.Economy.EconomyManager.Succeed=true;
            Check(module.ApplyCoopZombiePersonal(new CoopZombiePersonal { Run=11,Kind="extract" }),"retry pays original wallet");
            Check(Duckov.Economy.EconomyManager.Cash==80 && module.runState.PurificationPoints==0 && module.owner.Extracts==0,"original local payout and campaign fact applied");
            module.runState.RunId=13;module.runState.CombatPhase=ZombieModeCombatPhase.Combat;module.runState.PurificationPoints=60;BossRushCoop.LocalAssetId="slot-B";module.RememberCoopZombieWallet();
            Check(!module.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=13,Kind="points",Amount=20}),"new slot cannot change old run points");
            Check(module.QueueCoopZombieWrongAssetRefund() && module.QueueCoopZombieWrongAssetRefund(),"wrong-slot refunds freeze original transaction once");
            module.TickCoopZombieRefunds();Check(ZombieModeEntryDebt.Cash==0 && ZombieModeEntryDebt.Invitations==0,"refund cannot enter new slot");
            module.runState.RunId=14;module.runState.PurificationPoints=500;module.coopZombieLocalAsset="slot-B";
            BossRushCoop.LocalAssetId="slot-A";
            Check(module.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=13,Kind="extract"}),"returning original asset settles preserved old wallet");
            Check(Duckov.Economy.EconomyManager.Cash==140 && module.runState.PurificationPoints==500,"old wallet payout does not consume current run wallet");
            module.TickCoopZombieRefunds();module.TickCoopZombieRefunds();
            Check(ZombieModeEntryDebt.Cash==250 && ZombieModeEntryDebt.Invitations==1,"old entry refunds commit exactly once on original asset return");
            var closing=new ZombieModeRuntimeModule();closing.runState.RunId=18;
            Check(closing.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=18,Kind="points",Amount=9}),"closed wallet credited before lifecycle cleanup");
            closing.runState.PurificationPoints-=3;closing.runState.RunId=-18;closing.runState.LifecyclePhase=ZombieModeLifecyclePhase.Exiting;closing.RememberCoopZombieWallet();
            closing.runState.PurificationPoints=0;closing.runState.LifecyclePhase=ZombieModeLifecyclePhase.None;closing.OnUpdate(1,1);
            long closingCash=Duckov.Economy.EconomyManager.Cash;
            Check(closing.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=18,Kind="extract",Amount=9}) && Duckov.Economy.EconomyManager.Cash==closingCash+6,"negative invalidated run snapshots before clear and inactive tick retains debt");
            Check(closing.runState.PurificationPoints==0 && !closing.runState.ExtractionSuccessHandled,"closed receipt never revives cleared native state");
            var notified=new ZombieModeRuntimeModule();notified.runState.RunId=19;notified.runState.PurificationPoints=7;Duckov.UI.NotificationText.Throw=true;
            long noticeCash=Duckov.Economy.EconomyManager.Cash;var noticeReceipt=new CoopZombiePersonal {Run=19,Kind="extract"};
            Check(notified.ApplyCoopZombiePersonal(noticeReceipt),"successful cash settlement survives notification failure");
            Duckov.UI.NotificationText.Throw=false;Check(notified.ApplyCoopZombiePersonal(noticeReceipt) && Duckov.Economy.EconomyManager.Cash==noticeCash+7,"notification fault retry cannot credit money twice");
            var contracted=new ZombieModeRuntimeModule();contracted.runState.RunId=20;contracted.coopZombieStarterChosen=contracted.coopZombieStarterGranted=true;contracted.owner.CampaignEnabled=true;CampaignObjectiveTracker.Chapter="old-z-contract";contracted.RememberCoopZombieWallet();
            contracted.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=20,Kind="points",Amount=12});contracted.ResetCoopZombieRun(23);contracted.runState.RunId=23;contracted.runState.CurrentWave=99;contracted.runState.PurificationPoints=99;CampaignObjectiveTracker.Chapter="new-z-contract";CampaignObjectiveTracker.SaveAvailable=false;
            var campaignReceipt=new CoopZombiePersonal {Run=20,Kind="extract",Amount=12};long campaignCash=Duckov.Economy.EconomyManager.Cash;
            Check(!contracted.ApplyCoopZombiePersonal(campaignReceipt) && Duckov.Economy.EconomyManager.Cash==campaignCash+12,"zombie cash commit remains paid while frozen old campaign save retries");
            CampaignObjectiveTracker.SaveAvailable=true;Check(contracted.ApplyCoopZombiePersonal(campaignReceipt) && Duckov.Economy.EconomyManager.Cash==campaignCash+12,"zombie frozen campaign retry cannot pay cash twice");
            Check(CampaignObjectiveTracker.Reports.Count==1 && CampaignObjectiveTracker.Reports[0]=="old-z-contract" && CampaignObjectiveTracker.Chapter=="new-z-contract" && contracted.runState.PurificationPoints==99,"old zombie campaign lease never arms or writes new contract");
            var start=new ZombieModeRuntimeModule();start.runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;start.coopZombieStarterChosen=false;
            foreach(var player in BossRushCoop.Players) {player.AssetId="slot-A";player.IsConnected=true;player.IsAlive=true;start.coopZombieParticipants[player.Id]="slot-A";start.coopZombieStarterReady.Add(player.Id+":slot-A");}
            BossRushCoop.Players[0].IsAlive=false;CharacterMainControl.Main=new CharacterMainControl();CharacterMainControl.Main.Health.IsDead=true;
            start.coopZombieStarterReady.Remove("p0:slot-A");start.OnUpdate(1,1);
            Check(start.runState.LifecyclePhase==ZombieModeLifecyclePhase.Active && start.Preparations==1,"dead host who never chose starter does not block 15 prepared guests");
            start.OnUpdate(1,1);Check(start.Preparations==1,"starter barrier advances only once");
            var across=new ZombieModeRuntimeModule();across.runState.RunId=21;
            Check(across.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=21,Kind="points",Amount=10}),"old wallet first points applied");
            across.runState.PurificationPoints-=4;
            across.ResetCoopZombieRun(22);across.runState.RunId=22;across.runState.PurificationPoints=500;
            long cashBefore=Duckov.Economy.EconomyManager.Cash;
            var oldSettlement=new CoopZombiePersonal {Run=21,Kind="extract",Amount=15};
            Check(!across.ApplyCoopZombiePersonal(oldSettlement) && Duckov.Economy.EconomyManager.Cash==cashBefore,"old extraction waits for missing network points across new run");
            Check(across.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=21,Kind="points",Amount=5}) && across.runState.PurificationPoints==500,"late old points do not change new wallet");
            Duckov.Economy.EconomyManager.Succeed=false;Check(!across.ApplyCoopZombiePersonal(oldSettlement),"old payout failure retains exact spend-adjusted wallet");
            BossRushCoop.LocalAssetId="slot-B";Duckov.Economy.EconomyManager.Succeed=true;Check(!across.ApplyCoopZombiePersonal(oldSettlement) && Duckov.Economy.EconomyManager.Cash==cashBefore,"old payout cannot reach another slot");BossRushCoop.LocalAssetId="slot-A";
            Check(across.ApplyCoopZombiePersonal(oldSettlement) && Duckov.Economy.EconomyManager.Cash==cashBefore+11 && across.runState.PurificationPoints==500,"original asset receives fifteen earned points minus original four-point spend");
            Check(across.ApplyCoopZombiePersonal(oldSettlement) && Duckov.Economy.EconomyManager.Cash==cashBefore+11,"old extraction retry cannot pay twice");
            var issued=new ZombieModeRuntimeModule();issued.ResetCoopZombieRun(31);issued.runState.RunId=31;
            foreach(var player in BossRushCoop.Players) {player.IsAlive=true;player.IsConnected=true;player.AssetId="slot-A";player.Character.transform.position=new UnityEngine.Vector3();issued.coopZombieParticipants[player.Id]="slot-A";issued.coopZombieStarterReady.Add(player.Id+":slot-A");}
            issued.DeliverCoopZombiePoints(3);BossRushCoop.Players[1].IsAlive=false;issued.DeliverCoopZombiePoints(2);BossRushCoop.Players[1].IsAlive=true;
            issued.runState.PurificationPoints=999;issued.CompleteZombieModeExtractionSuccess(31);
            var firstIssued=issued.coopZombiePersonal.Pending.Find(x=>x.Player=="p0" && x.Receipt.Kind=="extract");var secondIssued=issued.coopZombiePersonal.Pending.Find(x=>x.Player=="p1" && x.Receipt.Kind=="extract");
            Check(firstIssued.Receipt.Amount==5 && secondIssued.Receipt.Amount==3 && firstIssued.Id=="31:extract:","actual extraction payload carries each participant issued point total with bounded epoch ID");
            var current=new ZombieModeRuntimeModule();current.runState.RunId=31;
            Check(!current.ApplyCoopZombiePersonal(firstIssued.Receipt),"current extraction also waits for all prior point receipts");
            current.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=31,Kind="points",Amount=3});current.runState.PurificationPoints-=1;
            Check(!current.ApplyCoopZombiePersonal(firstIssued.Receipt),"current receipt barrier counts received points rather than net spend balance");
            current.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=31,Kind="points",Amount=2});long beforeCurrent=Duckov.Economy.EconomyManager.Cash;
            Check(current.ApplyCoopZombiePersonal(firstIssued.Receipt) && Duckov.Economy.EconomyManager.Cash==beforeCurrent+4,"current settlement retains real point-spend semantics");
            Check(CampaignObjectiveTracker.Waves.Contains("old-z-contract:5"),"old wallet observes only frozen original reached wave despite new run state");
            var successFirst=new ZombieModeRuntimeModule();successFirst.runState.RunId=32;successFirst.runState.PurificationPoints=8;successFirst.runState.CombatPhase=ZombieModeCombatPhase.SuccessExit;successFirst.RememberCoopZombieWallet();long successCash=Duckov.Economy.EconomyManager.Cash;Check(successFirst.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=32,Kind="extract"})&&Duckov.Economy.EconomyManager.Cash==successCash+8,"world success before personal receipt does not acknowledge unpaid wallet");
            var collided=new ZombieModeRuntimeModule();collided.runState.RunId=33;collided.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=33,Kind="points",Amount=7});BossRushCoop.RunId="other-z-room";collided.ResetCoopZombieRun(33);collided.runState.RunId=33;collided.runState.PurificationPoints=0;collided.runState.CombatPhase=ZombieModeCombatPhase.Combat;collided.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=33,Kind="points",Amount=3});long collidedCash=Duckov.Economy.EconomyManager.Cash;Check(collided.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=33,Kind="extract",Amount=3})&&Duckov.Economy.EconomyManager.Cash==collidedCash+3,"equal numeric run in another room uses independent original wallet");BossRushCoop.RunId="z-room";Check(collided.ApplyCoopZombiePersonal(new CoopZombiePersonal {Run=33,Kind="extract",Amount=7})&&Duckov.Economy.EconomyManager.Cash==collidedCash+10,"return original room retains exact old numeric run debt");
            BossRushCoop.IsHost=false;var reporting=new ZombieModeRuntimeModule();reporting.coopZombieStarterChosen=true;reporting.coopZombieCompletedRewardWave=5;reporting.runState.CombatPhase=ZombieModeCombatPhase.RewardSelection;reporting.OnUpdate(1,1);Check(reporting.coopZombieWorld.Repeats.Count==2&&reporting.coopZombieWorld.Requests.Count==0,"actual periodic starter and reward facts select Repeat transport");reporting.ApplyCoopZombieStarterConfirmation(999,new[]{"p0:slot-A"});UnityEngine.Time.unscaledTime=2;reporting.OnUpdate(1,1);Check(reporting.coopZombieWorld.Repeats.Count==4,"wrong run cannot suppress original starter report");reporting.ApplyCoopZombieStarterConfirmation(11,new[]{"p0:slot-A"});UnityEngine.Time.unscaledTime=4;reporting.OnUpdate(1,1);Check(reporting.coopZombieWorld.Repeats.Count==5,"host starter confirmation stops stable starter reports while unfinished reward remains independently reportable");reporting.SendCoopZombieCommand("beacon");reporting.SendCoopZombieCommand("beacon");Check(reporting.coopZombieWorld.Requests.Count==2,"each actual manual zombie interaction keeps separate transport requests");BossRushCoop.IsHost=false;var stopped=new ZombieModeRuntimeModule();stopped.coopZombieProjectionRoom=BossRushCoop.RunId;stopped.coopZombieLocalRun=11;BossRushCoop.IsConnected=false;stopped.OnUpdate(1,1);Check(stopped.coopZombieProjectionRoom==null&&stopped.coopZombieLocalRun==0,"stopped native network retires old zombie projection before offline fallback");BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;
            var offline=new ZombieModeRuntimeModule();offline.ResetCoopZombieRun(44);offline.runState.RunId=44;
            foreach(var member in BossRushCoop.Players){member.AssetId="slot-A";member.IsConnected=true;member.IsAlive=true;member.Character.transform.position=new UnityEngine.Vector3();if(member.Id=="p2")member.AssetId="offline-original";offline.coopZombieParticipants[member.Id]=member.AssetId;offline.coopZombieStarterReady.Add(member.Id+":"+member.AssetId);}
            BossRushCoop.Players[1].Character.transform.position=new UnityEngine.Vector3(10,0,0);offline.OnUpdate(1,1);BossRushCoop.Players[1].IsConnected=false;
            offline.runState.ExtractionChanneling=true;offline.OnUpdate(1,1);BossRushCoop.Players[2].Character.transform.position=new UnityEngine.Vector3(10,0,0);offline.OnUpdate(1,1);BossRushCoop.Players[2].IsConnected=false;
            BossRushCoop.Players[3].IsAlive=false;offline.OnUpdate(1,1);BossRushCoop.Players[3].IsConnected=false;offline.CompleteZombieModeExtractionSuccess(44);
            Check(!offline.coopZombiePersonal.Pending.Exists(x=>x.Player=="p1"&&x.Receipt.Kind=="extract"),"Z early disconnected outside does not get extraction receipt");
            Check(offline.coopZombiePersonal.Pending.Exists(x=>x.Player=="p2"&&x.Receipt.Kind=="extract"&&x.Asset=="offline-original"),"Z countdown disconnect keeps extraction receipt on original asset");
            Check(!offline.coopZombiePersonal.Pending.Exists(x=>x.Player=="p3"&&x.Receipt.Kind=="extract"),"Z observed death before disconnect cannot receive extraction");
            string campaignAsset;Check(offline.TryGetCoopCampaignParticipantAsset("44","p2",out campaignAsset)&&campaignAsset=="offline-original","Z campaign qualification uses frozen admitted original asset offline");Check(!offline.TryGetCoopCampaignParticipantAsset("43","p2",out campaignAsset),"Z campaign getter rejects prior run");
            var lastInside=new ZombieModeRuntimeModule();lastInside.ResetCoopZombieRun(45);lastInside.runState.RunId=45;
            foreach(var member in BossRushCoop.Players){member.IsConnected=true;member.IsAlive=true;member.Character.transform.position=new UnityEngine.Vector3();lastInside.coopZombieParticipants[member.Id]="slot-A";lastInside.coopZombieStarterReady.Add(member.Id+":slot-A");}
            lastInside.OnUpdate(1,1);BossRushCoop.Players[4].IsConnected=false;BossRushCoop.Players[5].AssetId="slot-B";lastInside.CompleteZombieModeExtractionSuccess(45);
            Check(lastInside.coopZombiePersonal.Pending.Exists(x=>x.Player=="p4"&&x.Receipt.Kind=="extract"),"Z last actual inside qualifies without countdown started");Check(!lastInside.coopZombiePersonal.Pending.Exists(x=>x.Player=="p5"&&x.Receipt.Kind=="extract"),"Z switched current slot cannot inherit receipt");
            var chosen=new ZombieModeRuntimeModule();BossRushCoop.IsHost=false;CharacterMainControl.Main=new CharacterMainControl();chosen.coopZombieLocalRun=11;chosen.runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;chosen.SelectZombieModeStarterLoadout(11,ZombieModeStarterLoadout.Gunner);
            Check(chosen.Granted==0&&!chosen.entryTransaction.EntryResourcesFinalized&&chosen.entryTransaction.CashTemporarilyHeld,"Z real local click queues choice but no pack or finalized fee before host confirmation");
            chosen.ApplyCoopZombieAdmissionState(11,new[]{"p0:slot-A"},new string[0]);Check(chosen.Granted==1&&chosen.entryTransaction.EntryResourcesFinalized,"Z actual confirmed choice grants original pack and finalizes held fee once");chosen.ApplyCoopZombieAdmissionState(11,new[]{"p0:slot-A"},new string[0]);Check(chosen.Granted==1,"Z repeated host confirmation cannot grant starter pack twice");
            var skipped=new ZombieModeRuntimeModule();skipped.coopZombieLocalRun=11;skipped.runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;skipped.SelectZombieModeStarterLoadout(11,ZombieModeStarterLoadout.Melee);long beforeRefund=ZombieModeEntryDebt.Cash;int beforeInv=ZombieModeEntryDebt.Invitations;
            skipped.ApplyCoopZombieAdmissionState(11,new string[0],new[]{"p0:slot-A"});Check(skipped.Granted==0&&skipped.RolledBack==1&&ZombieModeEntryDebt.Cash==beforeRefund+250&&ZombieModeEntryDebt.Invitations==beforeInv+1,"Z skip wins race before host confirmation: no free pack, original inventory rollback, full cash and invitation refund");skipped.ApplyCoopZombieAdmissionState(11,new[]{"p0:slot-A"},new[]{"p0:slot-A"});Check(skipped.Granted==0&&ZombieModeEntryDebt.Cash==beforeRefund+250,"Z stale confirmation after skip cannot grant or refund again");
            BossRushCoop.IsHost=true;foreach(var member in BossRushCoop.Players){member.IsConnected=true;member.IsAlive=true;member.AssetId="slot-A";}var begin=new ZombieModeRuntimeModule();begin.runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;begin.coopZombieStarterReady.Add("p0:slot-A");begin.coopZombieParticipants["p0"]="slot-A";begin.StartCoopZombieWithConfirmedPlayers(11);Check(begin.coopZombieSkipped.Contains("p15:slot-A")&&begin.coopZombiePersonal.Pending.Count==15,"Z explicit host start queues one original-asset skip refund for every unconfirmed member");Check(!begin.RegisterCoopZombieStarter(BossRushCoop.Players[15]),"Z late actual choice cannot enter after skip frozen");BossRushCoop.Players[15].AssetId="slot-B";Check(!begin.RegisterCoopZombieStarter(BossRushCoop.Players[15]),"Z skipped player cannot bypass host admission by switching asset");BossRushCoop.Players[15].AssetId="slot-A";begin.OnUpdate(1,1);Check(begin.Preparations==1,"Z skipped observers do not block confirmed host start");begin.ResetCoopZombieRun(12);begin.runState.RunId=12;begin.runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;Check(begin.RegisterCoopZombieStarter(BossRushCoop.Players[15]),"Z skipped member can confirm next run");
            BossRushCoop.IsHost=false;var clientCohort=new ZombieModeRuntimeModule();clientCohort.runState.RunId=61;clientCohort.ApplyCoopZombieStarterConfirmation(61,new[]{"p0:slot-A","p1:slot-A"});Check(clientCohort.TryGetCoopCampaignParticipantAsset("61","p0",out campaignAsset)&&campaignAsset=="slot-A","Z client actual host confirmation fills campaign cohort original owner table");BossRushCoop.Players[0].AssetId="slot-B";clientCohort.ApplyCoopZombieStarterConfirmation(61,new[]{"p0:slot-A"});Check(!clientCohort.TryGetCoopCampaignParticipantAsset("61","p0",out campaignAsset),"Z client confirmation never invents readiness for current new slot");BossRushCoop.Players[0].AssetId="slot-A";clientCohort.ApplyCoopZombieStarterConfirmation(61,new[]{"p0:slot-A"});Check(clientCohort.TryGetCoopCampaignParticipantAsset("61","p0",out campaignAsset)&&campaignAsset=="slot-A","Z original asset restores exact host cohort after returning");
            var debt=new ZombieModeRuntimeModule();debt.coopZombieLocalRun=11;debt.runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;debt.SelectZombieModeStarterLoadout(11,ZombieModeStarterLoadout.Melee);ZombieModeEntryDebt.CashFail=true;long debtCash=ZombieModeEntryDebt.Cash;debt.ApplyCoopZombieAdmissionState(11,new string[0],new[]{"p0:slot-A"});Check(debt.Granted==0&&debt.coopZombieRefunds.Count==1&&ZombieModeEntryDebt.Cash==debtCash,"Z skip retains original cash obligation when both actual payment and debt write fail");BossRushCoop.LocalAssetId="slot-B";debt.TickCoopZombieRefunds();Check(ZombieModeEntryDebt.Cash==debtCash,"Z skipped refund cannot reach another local slot");BossRushCoop.LocalAssetId="slot-A";ZombieModeEntryDebt.CashFail=false;debt.TickCoopZombieRefunds();debt.TickCoopZombieRefunds();Check(ZombieModeEntryDebt.Cash==debtCash+250&&debt.coopZombieRefunds.Count==0,"Z original slot gets exact held cash once on retry");BossRushCoop.IsHost=true;
            BossRushCoop.IsHost=false;var superseded=new ZombieModeRuntimeModule();superseded.coopZombieLocalRun=11;superseded.runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;superseded.SelectZombieModeStarterLoadout(11,ZombieModeStarterLoadout.Melee);long supersededCash=ZombieModeEntryDebt.Cash;superseded.RetireCoopZombieUnconfirmedEntry(12);Check(superseded.Granted==0&&superseded.coopZombieLocalRun==0&&ZombieModeEntryDebt.Cash==supersededCash+250,"Z new host run cannot overwrite old unconfirmed held entry before refund");superseded.SelectZombieModeStarterLoadout(11,ZombieModeStarterLoadout.Melee);Check(!superseded.coopZombieStarterChosen,"Z stale actual choice after skip cannot reopen current declined entry");
            BossRushCoop.IsHost=true;var waiting=new ZombieModeRuntimeModule();waiting.coopZombieLocalRun=11;waiting.runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;waiting.coopZombieStarterGranted=true;waiting.coopZombieParticipants["p0"]="slot-A";waiting.coopZombieStarterReady.Add("p0:slot-A");
            var waitingView=new ZombieModeStarterChoiceView();waitingView.BindForTest(()=>waiting.CanOfferCoopZombieStart(11));var waitingAnchor=new UnityEngine.GameObject();waiting.ShowCoopZombieStartConfirmation(11,waitingAnchor);Check(object.ReferenceEquals(BossRushConfirmDialog.Last.Anchor,waitingAnchor),"Z actual confirmation is anchored to retained choice for scene/lifecycle cleanup");int firstPrompt=BossRushConfirmDialog.Shows;
            Check(waitingView.CanReopenForTest(),"Z actual retained choice stays available after keep-waiting closes confirmation");waiting.ShowCoopZombieStartConfirmation(11);Check(BossRushConfirmDialog.Shows==firstPrompt+1,"Z host may explicitly reopen actual start confirmation without another loadout or per-frame popup");
            Action previousConfirm=BossRushConfirmDialog.Last.OnConfirm;BossRushCoop.SceneToken="new-token";previousConfirm();Check(waiting.coopZombieSkipped.Count==0,"Z old start dialog cannot skip members after actual scene token changes");BossRushCoop.SceneToken="z-token";waiting.runState.LifecyclePhase=ZombieModeLifecyclePhase.Active;Check(!waitingView.CanReopenForTest(),"Z retained host start view retires when confirmed cohort actually starts");
            BossRushCoop.IsHost=true;var activeHost=new ZombieModeRuntimeModule();activeHost.runState.LifecyclePhase=ZombieModeLifecyclePhase.Active;Check(activeHost.RegisterCoopZombieStarter(BossRushCoop.Players[15]),"Z original active-run late admission remains legal for a player never explicitly skipped");
            BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="p15";var late=new ZombieModeRuntimeModule();late.coopZombieLocalRun=11;late.runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;late.SelectZombieModeStarterLoadout(11,ZombieModeStarterLoadout.Melee);long beforeLateCash=ZombieModeEntryDebt.Cash;Check(late.ApplyCoopZombieAdmissionState(11,new[]{"p0:slot-A"},new string[0])&&late.Granted==0&&late.coopZombieStarterChosen&&ZombieModeEntryDebt.Cash==beforeLateCash,"Z legal late arrival waits for host confirmation without silently cancelling paid entry");Check(late.ApplyCoopZombieAdmissionState(11,new[]{"p0:slot-A","p15:slot-A"},new string[0])&&late.Granted==1,"Z active-run host-confirmed late arrival receives the full original starter pack");
            var latePending=new ZombieModeRuntimeModule();latePending.coopZombieLocalRun=11;latePending.runState.LifecyclePhase=ZombieModeLifecyclePhase.WaitingStarterChoice;latePending.SelectZombieModeStarterLoadout(11,ZombieModeStarterLoadout.Melee);long pendingCash=ZombieModeEntryDebt.Cash;Check(!latePending.ApplyCoopZombieAdmissionState(11,new[]{"p0:slot-A"},new[]{"p15:slot-A"})&&latePending.Granted==0&&latePending.coopZombieLocalRun==0&&ZombieModeEntryDebt.Cash==pendingCash+250,"Z explicit skip while pending choice is in flight refunds full held entry");
            var foreign=new ZombieModeRuntimeModule();foreign.runState.RunId=0;long foreignCash=ZombieModeEntryDebt.Cash;Check(!foreign.ApplyCoopZombieAdmissionState(99,new[]{"p15:other-original"},new string[0])&&foreign.coopZombieDeclinedRun==99&&foreign.Granted==0&&ZombieModeEntryDebt.Cash==foreignCash,"Z confirmed original asset cannot be inherited by another slot or start another payment");
            Check(!foreign.ApplyCoopZombieAdmissionState(100,new string[0],new[]{"p15:other-original"})&&foreign.coopZombieDeclinedRun==100,"Z explicitly skipped player cannot bypass current run by switching slots");BossRushCoop.LocalPlayerId="p0";
            ZombieModeRuntimeModule.NotifyCoopZombieSkippedEntry(true,true);Check(Duckov.UI.NotificationText.Last.Contains("pending"),"Z failed refund reports pending instead of claiming returned");ZombieModeRuntimeModule.NotifyCoopZombieSkippedEntry(true,false);Check(Duckov.UI.NotificationText.Last.Contains("refunded"),"Z completed refund reports actual returned result");
            var wire=BossRushCoopJson.FromJson<CoopZombieWorld>(BossRushCoopJson.ToJson(new CoopZombieWorld {Run=71,StarterReady=new[]{"p0:slot-A"},Skipped=new[]{"p1:slot-A"},Enemies=new[]{new CoopZombieEnemy {Entity="native-7"}}}));Check(wire.Skipped.Length==1&&wire.Skipped[0]=="p1:slot-A"&&wire.StarterReady[0]=="p0:slot-A"&&wire.Enemies[0].Entity=="native-7","Z actual world codec retains full ready/skipped/nested enemy DTOs");
            int startClicks=0;bool mayStart=true;var realWaiting=new ZombieModeStarterChoiceView();realWaiting.BindForTest(()=>mayStart);int depthBefore=BossRushUIDepth.Calls;
            realWaiting.BuildForTest(()=>startClicks++);
            Check(realWaiting.ChoiceHidden&&BossRushUIDepth.Calls==depthBefore+1,"Z actual waiting UI hides choices and resolves the real shared button-depth method");
            ZombieModeUIHelper.Last.Click();Check(startClicks==1,"Z actual retained waiting button can reopen host confirmation");
            mayStart=false;ZombieModeUIHelper.Last.Click();Check(startClicks==1,"Z actual waiting delegate rechecks eligibility before invoking start");
            L10n.Chinese=true;ZombieModeRuntimeModule.NotifyCoopZombieSkippedEntry(true,true);
            Check(Duckov.UI.NotificationText.Last.Contains("退款处理中"),"Z actual injected co-op text resolves current language at notification use");L10n.Chinese=false;
            RunCommandAndMirrorChecks();RunEnemyTargetChecks();RunLeaseChecks();RunNpcAnchorChecks();
            Console.WriteLine("ZombieCoopFlow: PASS "+assertions+" source-linked assertions (16 participants)");
        }
    }
    internal static class Program { private static void Main() { ZombieModeRuntimeModule.Run(); } }
}
namespace UnityEngine.SceneManagement
{
    internal struct Scene {internal string name;internal int buildIndex;internal bool isLoaded;}
    internal static class SceneManager {internal static Scene GetActiveScene(){return new Scene {name="ZombieTestScene",buildIndex=7,isLoaded=true};}}
}
namespace BossRush
{
    internal sealed class BossRushMapConfig {}
    internal enum ZombieModeRewardCategory {Economy}
    internal sealed class ZombieModeRewardNode {internal int Wave,RemainingSelections=1;internal bool BossNode;internal readonly List<ZombieModeRewardType> Options=new List<ZombieModeRewardType>();}
    internal sealed class ZombieModeRewardCatalogEntry {internal ZombieModeRewardCategory Category;internal ZombieModeRewardType RewardType;}
    internal static class ZombieModeMapSelectionHelper {internal static bool ShowZombieModeMapSelection(out string failure){failure=null;return false;}internal static void ConfirmZombieModeMapEntry(int index){}internal static int PendingClears;internal static void ClearPendingZombieEntry(){PendingClears++;}}
    internal static class ZombieModePhaseGuards {internal static bool IsActive(ZombieModeLifecyclePhase phase){return phase==ZombieModeLifecyclePhase.Active;}internal static bool IsRunActive(ZombieModeLifecyclePhase phase){return phase==ZombieModeLifecyclePhase.Active;}internal static bool ShouldPauseModePressure(ZombieModeCombatPhase phase){return false;}}
    internal enum ZombieModeEnemyKind {Normal,Special,Elite}
    internal enum ZombieModeBossKind {Titan}
    internal enum ZombieModeSpecialKind {None}
    internal enum ZombieModeRunOnlyObjectKind {Unknown}
    internal sealed class ZombieModeEnemyRuntimeMarker {internal T GetComponent<T>() where T:class{return Owner as T;}internal int RunId,PurificationPointValue;internal CharacterMainControl Owner;internal bool IsBoss,DeathSettled,RemovedFromRuntime;internal ZombieModeEnemyKind EnemyKind;internal ZombieModeBossKind BossKind;internal ZombieModeSpecialKind SpecialKind;}
    internal static class CoopAiPredictionHealthProtection {internal static void MarkReplica(CharacterMainControl character){}}
    internal static class ZombieModeZoneVisuals {internal static readonly UnityEngine.Color SafeZoneColor=new UnityEngine.Color(1);}
    internal sealed partial class ZombieModeRewardSelectionView
    {
        private int runId;private ModBehaviour owner;private bool selecting,closing;
        internal void Bind(int run,ModBehaviour host){runId=run;owner=host;}
        internal void Choose(ZombieModeRewardType type,object token){Select(type,token);}
    }
}
namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        private int GetZombieModeMinimumRewardCategoryCount(bool boss){return 1;}
        private List<ZombieModeRewardCatalogEntry> BuildZombieModeRewardCatalogEntries(bool boss){return new List<ZombieModeRewardCatalogEntry>{new ZombieModeRewardCatalogEntry {RewardType=ZombieModeRewardType.Heal}};}
        private bool IsZombieModeBossBonusRewardSelection(ZombieModeRewardNode node){return false;}
        private void KeepZombieModeBossBonusRewardEntries(List<ZombieModeRewardCatalogEntry> catalog){}
        private void TryAddZombieModeEarlyGuaranteedReward(ZombieModeRewardNode node,List<ZombieModeRewardCatalogEntry> catalog,List<ZombieModeRewardCategory> categories,bool gear){}
        private ZombieModeRewardCategory RollZombieModeRewardCategory(List<ZombieModeRewardCatalogEntry> catalog,List<ZombieModeRewardCategory> categories,int count,int minimum){return ZombieModeRewardCategory.Economy;}
        private ZombieModeRewardCatalogEntry SelectZombieModeRewardEntryForCategory(List<ZombieModeRewardCatalogEntry> catalog,ZombieModeRewardCategory category,List<ZombieModeRewardType> options){return catalog.Count==0?null:catalog[0];}
        private void EnsureZombieModeRewardCategoryDiversity(ZombieModeRewardNode node,List<ZombieModeRewardCatalogEntry> catalog,int minimum){}

        private CoopGameplayCommand WireCommand(CoopGameplayCommand original)
        {
            CoopGameplayCommand wire = BossRushCoopJson.FromJson<CoopGameplayCommand>(BossRushCoopJson.ToJson(original));
            wire.PlayerId = original.PlayerId;
            return wire;
        }
        private static void RunCommandAndMirrorChecks()
        {
            BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;BossRushCoop.HasBoundScene=true;
            BossRushCoop.LocalPlayerId="p1";BossRushCoop.LocalAssetId="slot-A";
            foreach(BossRushCoopPlayer player in BossRushCoop.Players){player.IsAlive=player.IsConnected=true;player.AssetId="slot-A";}
            var sender=new ZombieModeRuntimeModule();sender.runState.RunId=81;sender.runState.CurrentWave=5;
            sender.SendCoopZombieCommand("reward-world",1,ZombieModeRewardType.ContractPollutionDeal.ToString());
            var packet=sender.WireCommand(sender.coopZombieWorld.Commands[0]);
            var payload=BossRushCoopJson.FromJson<CoopZombieCommand>(packet.Text);
            Check(payload.Run==81&&payload.Wave==5&&payload.Text==ZombieModeRewardType.ContractPollutionDeal.ToString(),"Z actual Send and codec freeze business run and wave before transport retry");
            var host=new ZombieModeRuntimeModule();host.runState.RunId=81;host.runState.CurrentWave=5;host.runState.CombatPhase=ZombieModeCombatPhase.RewardSelection;
            host.coopZombieParticipants["p1"]="slot-A";host.coopZombieStarterReady.Add("p1:slot-A");
            BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="p0";
            host.ExecuteCoopZombieCommand(packet);host.ExecuteCoopZombieCommand(packet);
            Check(host.Npcs==1&&host.runState.PollutionFromContracts==2&&host.KillRewardRecords==1,"Z actual Execute applies current reward pollution NPC and kill-reward record once");
            host.runState.CurrentWave=10;host.ExecuteCoopZombieCommand(packet);
            Check(host.Npcs==1&&host.runState.PollutionFromContracts==2,"Z prior wave reward retry cannot apply to next reward wave in same scene");
            host.ResetCoopZombieRun(82);host.runState.RunId=82;host.runState.CurrentWave=5;host.coopZombieParticipants["p1"]="slot-A";host.coopZombieStarterReady.Add("p1:slot-A");host.ExecuteCoopZombieCommand(packet);
            Check(host.Npcs==1,"Z prior run reward cannot apply to same numbered wave of new run");
            BossRushCoop.IsHost=false;BossRushCoop.LocalPlayerId="p1";
            sender.SendCoopZombieCommand("starter");var staleStarter=sender.WireCommand(sender.coopZombieWorld.Commands[1]);
            sender.SendCoopZombieCommand("decline");var staleDecline=sender.WireCommand(sender.coopZombieWorld.Commands[2]);
            BossRushCoop.IsHost=true;host.coopZombieStarterReady.Clear();host.ExecuteCoopZombieCommand(staleStarter);host.ExecuteCoopZombieCommand(staleDecline);
            Check(host.coopZombieStarterReady.Count==0&&host.coopZombieDeclinedAssets.Count==0,"Z old starter and decline cannot alter next run cohort");
            BossRushCoop.IsHost=true;
            host.runState.RunId=81;host.runState.CurrentWave=10;host.ExecuteCoopZombieCommand(staleStarter);
            Check(host.coopZombieStarterReady.Contains("p1:slot-A"),"Z current run late admission remains valid across wave change");
            host.ExecuteCoopZombieCommand(staleDecline);Check(host.coopZombieDeclinedAssets.Contains("p1:slot-A")&&!host.coopZombieStarterReady.Contains("p1:slot-A"),"Z current run decline still withdraws matching original participant");
            host.coopZombieDeclinedAssets.Clear();host.coopZombieStarterReady.Add("p1:slot-A");
            sender.runState.CurrentWave=10;
            foreach(string action in new[]{"beacon","portable","continue","extract","gravity"})
            {
                BossRushCoop.IsHost=false;
                string text=action=="gravity"?BossRushCoopJson.ToJson(new CoopZombieArea {Radius=3,Strength=1}):null;
                sender.SendCoopZombieCommand(action,0,text);var world=sender.WireCommand(sender.coopZombieWorld.Commands[sender.coopZombieWorld.Commands.Count-1]);
                BossRushCoop.IsHost=true;
                host.runState.CurrentWave=11;host.ExecuteCoopZombieCommand(world);
                host.runState.CurrentWave=10;host.ExecuteCoopZombieCommand(world);
            }
            Check(host.Beacons==1&&host.Portables==1&&host.Continues==1&&host.Extractions==1&&host.Gravity==1,"Z general world operations reject stale wave and accept current original participant");
            host.coopZombieStarterReady.Clear();BossRushCoop.IsHost=false;sender.SendCoopZombieCommand("portable");BossRushCoop.IsHost=true;host.ExecuteCoopZombieCommand(sender.WireCommand(sender.coopZombieWorld.Commands[sender.coopZombieWorld.Commands.Count-1]));
            Check(host.Portables==1,"Z unadmitted observer cannot deploy a shared safe zone");
            BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="p0";host.coopZombieParticipants["p0"]="slot-A";host.coopZombieStarterReady.Add("p0:slot-A");host.SendCoopZombieCommand("beacon");
            Check(host.Beacons==2,"Z host direct action follows same encoded validated production command path");

            BossRushCoop.IsHost=false;var projected=new ZombieModeRuntimeModule();projected.runState.RunId=80;projected.coopZombieLocalRun=80;projected.coopZombieStarterGranted=true;
            projected.coopZombieParticipants["p1"]="slot-A";projected.coopZombieStarterReady.Add("p1:slot-A");BossRushCoop.Players[1].AssetId="slot-B";
            var state=new CoopZombieWorld {Run=82,SceneName="ZombieTestScene",Lifecycle=(int)ZombieModeLifecyclePhase.Active,Combat=(int)ZombieModeCombatPhase.ExtractionOpportunity,Wave=5,HasExtractionArea=true,ExtractionPosition=new UnityEngine.Vector3(50,0,0),StarterReady=new[]{"p0:slot-A","p1:slot-B"}};
            projected.ApplyCoopZombieWorld(state);
            Check(projected.coopZombieLocalRun==82&&projected.coopZombieParticipants.Count==0&&projected.coopZombieAdmissionPending,"Z actual ApplyWorld new run clears old asset cohort before admission");
            projected.Admit();projected.Admit();
            Check(projected.EntryCommits==1,"Z duplicate investment confirmation cannot charge entry twice");
            projected.coopZombieStarterChosen=true;projected.ApplyCoopZombieWorld(state);
            string asset;Check(projected.TryGetCoopCampaignParticipantAsset("82","p1",out asset)&&asset=="slot-B","Z actual next-run projection admits confirmed new asset rather than retaining previous run slot");
            projected.ApplyCoopZombieWorld(state);projected.ApplyCoopZombieWorld(state);
            Check(projected.ExtractionShows==1,"Z repeated world snapshots open extraction choice once");
            Check(projected.runState.ActiveExtractionArea.transform.position.x==50,"Z late client without safe zone uses actual host extraction center");
            state.Extracting=true;projected.ApplyCoopZombieWorld(state);int clears=projected.ExtractionClears;projected.ApplyCoopZombieWorld(state);
            Check(projected.ExtractionShows==1&&clears>0,"Z ongoing extraction never reopens choice over client movement");
            state.Extracting=false;projected.ApplyCoopZombieWorld(state);projected.ApplyCoopZombieWorld(state);
            Check(projected.ExtractionShows==2,"Z canceled countdown offers exactly one new retry choice");
            state.Combat=(int)ZombieModeCombatPhase.Combat;state.Wave=6;projected.ApplyCoopZombieWorld(state);
            Check(projected.runState.ActiveExtractionArea==null,"Z world phase exit retires old client extraction area");
            projected.runState.ActiveSafeZoneActive=true;projected.runState.ActiveSafeZoneRadius=3;projected.runState.ActiveSafeZoneCenter=new UnityEngine.Vector3(1,0,0);projected.MirrorCoopZombieSafeZone(false);var visual=projected.runState.ActiveSafeZoneVisual;
            projected.runState.ActiveSafeZoneCenter=new UnityEngine.Vector3(24,0,0);projected.MirrorCoopZombieSafeZone(false);
            Check(object.ReferenceEquals(visual,projected.runState.ActiveSafeZoneVisual)&&visual.transform.position.x==24,"Z existing safe zone visual follows replaced host center");
            projected.runState.ActiveSafeZoneActive=false;projected.MirrorCoopZombieSafeZone(false);
            Check(visual==null&&projected.runState.ActiveSafeZoneVisual==null,"Z inactive snapshot destroys old mirrored circle");

            coopZombieOwner=projected;projected.runState.CombatPhase=ZombieModeCombatPhase.RewardSelection;projected.runState.CurrentRewardNode=new ZombieModeRewardNode {Wave=6};projected.RollZombieModeRewardOptions();
            object firstToken=CaptureZombieModeRewardUiToken(82);var view=new ZombieModeRewardSelectionView();view.Bind(82,projected.owner);view.Choose(ZombieModeRewardType.Heal,firstToken);
            Check(projected.owner.Selections==1,"Z real reward view Select accepts current production roll token");
            projected.RollZombieModeRewardOptions();view.Choose(ZombieModeRewardType.Heal,firstToken);
            Check(projected.owner.Selections==1,"Z old card callback cannot select same enum after actual reward reroll");
            object secondToken=CaptureZombieModeRewardUiToken(82);view.Choose(ZombieModeRewardType.Heal,secondToken);
            Check(projected.owner.Selections==2,"Z rebuilt current card remains selectable");
            BossRushCoop.LocalAssetId="slot-B";view.Choose(ZombieModeRewardType.Heal,secondToken);BossRushCoop.LocalAssetId="slot-A";projected.runState.CombatPhase=ZombieModeCombatPhase.Combat;view.Choose(ZombieModeRewardType.Heal,secondToken);
            Check(projected.owner.Selections==2,"Z old view cannot select after asset or combat phase changed");
            var canceledAdmission=new ZombieModeRuntimeModule();canceledAdmission.runState.RunId=0;canceledAdmission.coopZombieLocalRun=0;
            canceledAdmission.ApplyCoopZombieWorld(new CoopZombieWorld {Run=91,SceneName="ZombieTestScene",Lifecycle=(int)ZombieModeLifecyclePhase.Active});Action oldAdmission=canceledAdmission.Admit;
            canceledAdmission.ApplyCoopZombieWorld(new CoopZombieWorld());oldAdmission();
            Check(!canceledAdmission.coopZombieAdmissionPending&&canceledAdmission.coopZombieLocalRun==0&&canceledAdmission.EntryCommits==0,"Z ended host run cancels pending client investment before stale confirm");
            canceledAdmission.ApplyCoopZombieWorld(new CoopZombieWorld {Run=92,SceneName="ZombieTestScene",Lifecycle=(int)ZombieModeLifecyclePhase.Active});oldAdmission();
            Check(canceledAdmission.coopZombieAdmissionPending&&canceledAdmission.EntryCommits==0,"Z old investment callback cannot cancel next run pending transaction");
            Action currentAdmission=canceledAdmission.Admit;BossRushCoop.SceneToken="next-z-scene";currentAdmission();BossRushCoop.SceneToken="z-token";
            Check(canceledAdmission.EntryCommits==0&&canceledAdmission.coopZombieAdmissionPending,"Z investment confirmation freezes original scene token");
            var closedExtraction=new ZombieModeExtractionOpportunityView();closedExtraction.Bind(82,projected.owner);closedExtraction.ReleaseInput();closedExtraction.Click(true);
            Check(projected.owner.ExtractClicks==0,"Z extraction choice released by host ignores stale callback");
            var openExtraction=new ZombieModeExtractionOpportunityView();openExtraction.Bind(82,projected.owner);openExtraction.Click(false);openExtraction.Click(true);
            Check(projected.owner.ContinueClicks==1&&projected.owner.ExtractClicks==0,"Z current extraction choice dispatches once and refuses closed follow-up");
            coopZombieOwner=null;BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="p0";
        }
    }
}
namespace BossRush
{
    internal static class BossRushUIKit {internal static void PlayCloseAndDestroy(UnityEngine.GameObject body){UnityEngine.Object.Destroy(body);}}
    internal static class ZombieModeUiNudge {internal static void Flash(UnityEngine.UI.Button button,string text){}}
    internal sealed class CancelKeyBoundary {internal void Detach(){}}
    internal sealed partial class ZombieModeExtractionOpportunityView
    {
        private int runId;private ModBehaviour owner;private bool decided,closed;private CancelKeyBoundary cancelKey;
        private readonly UnityEngine.GameObject gameObject=new UnityEngine.GameObject();private UnityEngine.UI.Button extractButton;
        private void RestoreInputState(){}
        internal void Bind(int run,ModBehaviour host){runId=run;owner=host;}
        internal void Click(bool extract){Choose(extract);}
    }
}
namespace BossRush
{
    internal abstract class HarasserRuntimeBoundary : UnityEngine.Object
    {
        protected readonly UnityEngine.GameObject gameObject=new UnityEngine.GameObject();
        protected UnityEngine.Transform transform {get{return gameObject.transform;}}
        protected int RuntimeRunId=11;
        protected abstract void TickRuntime(ModBehaviour inst);
    }
    internal sealed class TrajectoryBoundary {internal void SetPosition(int index,UnityEngine.Vector3 position){}}
    internal sealed partial class ZombieModeHarasserProjectileRuntime : HarasserRuntimeBoundary
    {
        private bool resolved;private CharacterMainControl source;private UnityEngine.Vector3 targetPosition,velocity;private float impactRadius=0.85f,speed=1,travelledDistance,maxTravelDistance=8,lastDistanceToTarget;private float damage=5,slowRadius=1,slowPercent=0.1f,slowDuration=1;private TrajectoryBoundary trajectory;
        internal void Prime(CharacterMainControl character,UnityEngine.Vector3 target){source=character;targetPosition=target;velocity=new UnityEngine.Vector3(1,0,0);}
        internal void Advance(ModBehaviour host){TickRuntime(host);}
        internal bool IsGone {get{return gameObject.Destroyed;}}
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        private CharacterMainControl SkillTarget;private int SpecialSkills,EliteSkills;
        private void TryExecuteZombieModeSpecialSkill(int run,CharacterMainControl character,ZombieModeEnemyRuntimeMarker marker,CharacterMainControl target){SkillTarget=target;SpecialSkills++;}
        private void TryExecuteZombieModeEliteSkill(int run,CharacterMainControl character,ZombieModeEnemyRuntimeMarker marker,CharacterMainControl target){SkillTarget=target;EliteSkills++;}
        private static void RunEnemyTargetChecks()
        {
            BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.IsHost=true;
            var mode=new ZombieModeRuntimeModule();coopZombieOwner=mode;mode.runState.CombatPhase=ZombieModeCombatPhase.Combat;
            var host=BossRushCoop.FindPlayer("p0");var guest=BossRushCoop.FindPlayer("p1");var observer=BossRushCoop.FindPlayer("p2");
            host.IsAlive=false;host.IsConnected=true;guest.IsConnected=guest.IsAlive=true;observer.IsConnected=observer.IsAlive=true;
            CharacterMainControl.Main=host.Character;CharacterMainControl.Main.Health.IsDead=true;
            guest.Character.transform.position=new UnityEngine.Vector3(4,0,0);observer.Character.transform.position=new UnityEngine.Vector3(0,0,0);
            mode.coopZombieParticipants[guest.Id]=guest.AssetId;mode.coopZombieStarterReady.Add(CoopZombieParticipantKey(guest));
            var enemy=new CharacterMainControl();var marker=new ZombieModeEnemyRuntimeMarker {RunId=11,Owner=enemy,EnemyKind=ZombieModeEnemyKind.Special};
            mode.TryExecuteZombieModeEnemyRuntimeSkill(marker);
            Check(mode.SpecialSkills==1&&object.ReferenceEquals(mode.SkillTarget,guest.Character),"Z special skill targets admitted living guest after host death rather than closer observer");
            marker.EnemyKind=ZombieModeEnemyKind.Elite;mode.TryExecuteZombieModeEnemyRuntimeSkill(marker);
            Check(mode.EliteSkills==1&&object.ReferenceEquals(mode.SkillTarget,guest.Character),"Z elite skill shares current admitted target selection");
            guest.IsAlive=false;mode.TryExecuteZombieModeEnemyRuntimeSkill(marker);
            Check(mode.EliteSkills==1,"Z world skill has no target when admitted party is dead");
            guest.IsAlive=true;BossRushCoop.IsHost=false;mode.TryExecuteZombieModeEnemyRuntimeSkill(marker);
            Check(mode.EliteSkills==1,"Z client replica cannot independently run world skills");BossRushCoop.IsHost=true;
            guest.Character.transform.position=new UnityEngine.Vector3(0.5f,0,0);var projectile=new ZombieModeHarasserProjectileRuntime();projectile.Prime(enemy,new UnityEngine.Vector3(1,0,0));projectile.Advance(mode.owner);
            Check(mode.owner.ProjectileImpacts==1&&mode.owner.ImpactPosition.x==0.5f&&projectile.IsGone,"Z actual harasser tick hits living guest and dispatches impact after host death");
            guest.Character.transform.position=new UnityEngine.Vector3(8,0,0);var missed=new ZombieModeHarasserProjectileRuntime();missed.Prime(enemy,new UnityEngine.Vector3(1,0,0));missed.Advance(mode.owner);
            Check(mode.owner.ProjectileImpacts==1&&missed.IsGone,"Z harasser reaching old target without current player remains a miss");
            BossRushCoop.IsConnected=false;CharacterMainControl.Main=host.Character;host.Character.Health.IsDead=false;marker.EnemyKind=ZombieModeEnemyKind.Special;mode.TryExecuteZombieModeEnemyRuntimeSkill(marker);
            Check(object.ReferenceEquals(mode.SkillTarget,host.Character)&&mode.SpecialSkills==2,"Z offline skills retain local player targeting");BossRushCoop.IsConnected=true;host.IsAlive=true;coopZombieOwner=null;
        }
    }
}

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        private static void RunLeaseChecks()
        {
            foreach(string loss in new[]{"disconnect","room","role","solo_join","client_room"})
            {
                BossRushCoop.RunId="lease-room";BossRushCoop.IsConnected=loss!="solo_join";BossRushCoop.IsHost=loss!="solo_join"&&loss!="client_room";BossRushCoop.LocalAssetId="slot-A";
                var runtime=new ZombieModeRuntimeModule();runtime.runState.RunId=201;runtime.ResetCoopZombieRun(201);runtime.runState.CurrentWave=3;runtime.runState.PurificationPoints=17;runtime.pendingEntry=true;
                runtime.coopZombiePersonal.Deliver("p1","201:owed",new CoopZombiePersonal {Run=201,Kind="extract",Amount=7},"slot-A");
                runtime.coopZombieRefunds["old-entry"]=new CoopZombieRefund {AssetId="slot-B",Cash=25,Invitation=true};
                Check(runtime.IsZombieModeRunValid(201),"Z frozen new run begins with current lease "+loss);
                runtime.TickZombieMode(1);Check(runtime.owner.WorldTicks==(BossRushCoop.IsHost||!BossRushCoop.IsConnected?1:0),"Z original lease permits appropriate world authority "+loss);
                if(loss=="disconnect")BossRushCoop.IsConnected=BossRushCoop.IsHost=false;
                if(loss=="room"||loss=="client_room")BossRushCoop.RunId="successor-room";
                if(loss=="role")BossRushCoop.IsHost=false;
                if(loss=="solo_join")BossRushCoop.IsConnected=BossRushCoop.IsHost=true;
                Check(!runtime.IsZombieModeRunValid(201),"Z production run validation cancels before next Update on "+loss);
                BossRushCoop.RunId="lease-room";BossRushCoop.IsConnected=loss!="solo_join";BossRushCoop.IsHost=loss!="solo_join"&&loss!="client_room";
                Check(!runtime.IsZombieModeRunValid(201),"Z returning old connection identity cannot resurrect revoked run "+loss);
                int ticks=runtime.owner.WorldTicks;runtime.TickZombieMode(1);runtime.OnUpdate(1,1);runtime.OnUpdate(1,1);
                Check(runtime.owner.WorldTicks==ticks&&runtime.owner.Cleanups==1&&runtime.coopZombieLocalRun==0&&!runtime.pendingEntry,"Z lease loss retires world once before offline/channel work "+loss);
                Check(runtime.coopZombiePersonal.Pending.Count==1&&runtime.coopZombieRefunds.ContainsKey("old-entry"),"Z permanent original-asset receipt and entry debt survive world retirement "+loss);
                if(loss!="solo_join")Check(runtime.coopZombieWallets["lease-room:slot-A:201"].Points==17,"Z retirement remembers original room wallet after connection loss "+loss);
                runtime.ResetCoopZombieRun(202);runtime.runState.RunId=202;
                Check(runtime.IsZombieModeRunValid(202),"Z explicit next run obtains fresh lease "+loss);
            }
            foreach(string loss in new[]{"room","new_entry"})
            {
                BossRushCoop.IsConnected=BossRushCoop.IsHost=true;BossRushCoop.RunId="entry-room";
                var runtime=new ZombieModeRuntimeModule();runtime.ResetCoopZombieEntryTransaction();runtime.OfficialReady=false;
                System.Collections.IEnumerator loading=runtime.WaitForZombieModeTargetSceneActiveThenInitialize(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),null);
                Check(loading.MoveNext(),"Z actual map iterator waits for official ready before "+loss);
                if(loss=="room")BossRushCoop.RunId="other-room";else runtime.ResetCoopZombieEntryTransaction();
                runtime.OfficialReady=true;
                Check(!loading.MoveNext()&&runtime.Initialized==0,"Z stale pending map iterator cannot initialize successor context "+loss);
            }
            BossRushCoop.IsConnected=BossRushCoop.IsHost=true;BossRushCoop.RunId="z-room";BossRushCoop.LocalAssetId="slot-A";
        }
    }
}

namespace BossRush
{
    internal static class SpawnPositionHelper {internal static bool TrySampleNavMesh(UnityEngine.Vector3 origin,out UnityEngine.Vector3 resolved,float lift,float radius){resolved=origin;return false;}}
    internal static class ZombieModeNpcCatalog {internal static readonly float[] NpcAngleArrangement=new float[]{0,90};}
    internal static class ZombieModeServiceTerminalLook {internal static bool Dress(UnityEngine.GameObject root,UnityEngine.GameObject prefab,string name){return false;}}
    internal sealed class ZombieModeTemporaryNpcInteractable {internal void Initialize(int run,string service){}}
    internal sealed partial class ZombieModeRuntimeModule
    {
        private UnityEngine.GameObject resolveZombieModeNpcPrefab(string type){return null;}
        private void SetZombieModeRendererColor(UnityEngine.Renderer renderer,UnityEngine.Color color){}
        private static void RunNpcAnchorChecks()
        {
            BossRushCoop.IsConnected=BossRushCoop.IsHost=true;BossRushCoop.LocalPlayerId="p0";
            var runtime=new ZombieModeRuntimeModule();var host=BossRushCoop.FindPlayer("p0");var guest=BossRushCoop.FindPlayer("p1");var other=BossRushCoop.FindPlayer("p2");
            CharacterMainControl.Main=host.Character;host.IsAlive=false;host.Character.transform.position=new UnityEngine.Vector3(0,0,0);
            guest.IsAlive=guest.IsConnected=true;guest.Character.transform.position=new UnityEngine.Vector3(40,0,0);other.IsAlive=other.IsConnected=true;other.Character.transform.position=new UnityEngine.Vector3(100,0,0);
            runtime.coopZombieParticipants[guest.Id]=guest.AssetId;runtime.coopZombieStarterReady.Add(CoopZombieParticipantKey(guest));runtime.coopZombieParticipants[other.Id]=other.AssetId;runtime.coopZombieStarterReady.Add(CoopZombieParticipantKey(other));
            Check(runtime.GetZombieModeTemporaryRealNpcAnchorPosition().x==40&&runtime.CreateZombieModeTemporaryServiceTerminal(11,"Nurse").transform.position.x==40,"Z automatic NPC and service terminal anchor near admitted survivor after host death");
            runtime.coopZombieCommandPlayer=other.Character;
            Check(runtime.GetZombieModeTemporaryRealNpcAnchorPosition().x==100&&runtime.CreateZombieModeTemporaryServiceTerminal(11,"Supply").transform.position.x==100,"Z command-selected NPC and service terminal follow actual remote chooser");
            runtime.runState.ActiveSafeZoneActive=true;runtime.runState.ActiveSafeZoneCenter=new UnityEngine.Vector3(65,0,0);
            Check(runtime.GetZombieModeTemporaryRealNpcAnchorPosition().x==65&&runtime.CreateZombieModeTemporaryServiceTerminal(11,"Nurse").transform.position.x==65,"Z active safe zone remains shared NPC placement center");
            BossRushCoop.IsConnected=false;runtime.runState.ActiveSafeZoneActive=false;
            Check(runtime.GetZombieModeTemporaryRealNpcAnchorPosition().x==0&&runtime.CreateZombieModeTemporaryServiceTerminal(11,"Nurse").transform.position.x==0,"Z solo NPC placement retains local player anchor");
            BossRushCoop.IsConnected=BossRushCoop.IsHost=true;
        }
    }
}
