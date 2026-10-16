using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal sealed class GameObject { internal readonly Transform transform=new Transform();internal bool active=true;internal void SetActive(bool value){active=value;} }
    public struct Vector3
    {
        public float x, y, z; public static Vector3 zero {get{return new Vector3();}}
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    }
    public sealed class Transform { public Vector3 position; }
    public static class Mathf { public static int RoundToInt(float value){return (int)Math.Round(value);} public static int Max(int a, int b) { return Math.Max(a,b); } }
    public static class Time { public static float unscaledTime; }
}
public sealed class Health { public bool IsDead; }
public sealed class CharacterMainControl { public static CharacterMainControl Main; public readonly Health Health=new Health(); public readonly UnityEngine.Transform transform = new UnityEngine.Transform(); }
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
        internal static bool HasBoundScene=true; internal static bool IsHost=true,HasWorldAuthority=true;internal static bool IsClient {get {return IsConnected&&!IsHost;}}internal static string RunId="z-room",SceneToken="z-token";
        internal static string LocalAssetId = "slot-A"; internal static string LocalPlayerId = "p0";
        internal static BossRushCoopPlayer FindPlayer(string id) {return Players.Find(x=>x.Id==id);}
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
    internal static class ZombieModeTuning { internal const float ExtractionAreaTriggerRadius = 3f; }
    internal enum ZombieModeCombatPhase { InitialPreparation,Combat, SuccessExit,RewardSelection,ExtractionOpportunity }
    internal enum ZombieModeLifecyclePhase { None,WaitingStarterChoice,Active,Exiting }
    internal enum ZombieModeFailureReason { SuccessfulExtraction,Unknown,StarterLoadoutFailed }
    internal enum ZombieModeStarterLoadout {Melee,Gunner}
    internal sealed class ModBehaviour { internal bool CampaignEnabled;internal bool IsCampaignConfiguredEnabled() { return CampaignEnabled; }internal int Extracts;internal void ShowBigBanner(string text) { } internal void NotifyCampaignZombieExtracted() { Extracts++; } internal static void DevLog(string value) { }internal void CleanupZombieModeForRuntimeModule(ZombieModeFailureReason reason) { } }
    internal sealed class FlowState { internal readonly CountDownArea ActiveExtractionArea=new CountDownArea();internal bool ZombieModeResourcesCommitted,EntryResourcesFinalized;internal ZombieModeStarterLoadout StarterLoadout;internal bool ExtractionChanneling,BeaconChanneling;internal int RunId = 11, CurrentWave = 5, PurificationPoints; internal bool ExtractionSuccessHandled; internal ZombieModeCombatPhase CombatPhase;internal ZombieModeLifecyclePhase LifecyclePhase=ZombieModeLifecyclePhase.Active; }
    internal sealed class ZombieModeEntryTransaction { internal bool EntryResourcesFinalized; internal bool CashTemporarilyHeld=true, InvitationTemporarilyHeld=true; internal long CashWithheldAmount=250; }
    internal static class ZombieModeEntryDebt { internal static long Cash;internal static int Invitations;internal static bool CashFail;internal static bool RefundCash(long x) { if(CashFail)return false;Cash+=x;return true; }internal static bool RefundInvitation() { Invitations++;return true; } }
    internal class RuntimeBase { public virtual void OnUpdate(float deltaTime,float unscaledDeltaTime) { } }
    internal sealed class FakeChannel { internal sealed class Entry {internal string Player,Id,Asset;internal CoopZombiePersonal Receipt;}internal readonly List<Entry> Pending=new List<Entry>();internal readonly List<string> Repeats=new List<string>(),Requests=new List<string>(); internal void Request(string action,int value,string text,bool flag) {Requests.Add(action+":"+value);} internal void RequestRepeat(string action,int value,string text,bool flag) {Repeats.Add(action+":"+value);} internal bool HasPendingConnectedDeliveries;internal bool HasPendingForIdPrefix(string prefix) { return HasPendingConnectedDeliveries; }internal void Publish() {} internal void Tick(float dt) { }internal bool Deliver(string player,string id,CoopZombiePersonal receipt,string asset) { Pending.Add(new Entry {Player=player,Id=id,Asset=asset,Receipt=BossRushCoopJson.FromJson<CoopZombiePersonal>(BossRushCoopJson.ToJson(receipt))}); return true; } }
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
        private void ExecuteCoopZombieCommand(CoopGameplayCommand command) {if(command.Action=="starter")RegisterCoopZombieStarter(BossRushCoop.FindPlayer(BossRushCoop.LocalPlayerId));}private void UnlockZombieModeContainersForActiveRun(int run) { }
        private void BeginZombieModePreparation(int run,bool first,bool keep) { Preparations++;runState.CombatPhase=ZombieModeCombatPhase.Combat; }
        private void BeginZombieModeExtractionOpportunity(int run) { }private void FailZombieModeActive(int run) { }
        private readonly Dictionary<string,CoopZombieExtractionEligibility> coopZombieExtractionEligibility=new Dictionary<string,CoopZombieExtractionEligibility>();
        private readonly HashSet<string> coopZombieStarterReady = new HashSet<string>();
        private readonly Dictionary<string,int> coopZombieRewardReady = new Dictionary<string,int>();
        private readonly Dictionary<string,string> coopZombieParticipants = new Dictionary<string,string>();
        private readonly HashSet<string> coopZombieDeclinedAssets = new HashSet<string>();
        private readonly Dictionary<string,CoopZombieRefund> coopZombieRefunds = new Dictionary<string,CoopZombieRefund>();
        private int coopZombieEntrySequence;
        private string coopZombieWalletRoom=BossRushCoop.RunId,coopZombieProjectionRoom;private bool coopZombieAdmissionPending;
        private readonly Dictionary<string,object> coopZombieTerminals=new Dictionary<string,object>(),coopZombieRealNpcs=new Dictionary<string,object>();
        private readonly Dictionary<string,CoopZombieWallet> coopZombieWallets = new Dictionary<string,CoopZombieWallet>();
        private readonly Dictionary<string,int> coopZombiePointsIssued=new Dictionary<string,int>();private long coopZombieReceiptSequence;
        private readonly HashSet<string> coopZombieWorldSelections=new HashSet<string>();private int coopZombieLocalRun,coopZombieDeclinedRun,coopZombieLocalRewardWave;
        private string coopZombieLocalAsset="slot-A";
        private readonly ZombieModeEntryTransaction entryTransaction = new ZombieModeEntryTransaction();
        private bool ShouldRollbackZombieModeEntryResources() { return true; }
        private readonly FlowState runState = new FlowState();
        private readonly ModBehaviour owner = new ModBehaviour();
        private bool IsPlayerInsideZombieModeExtractionArea(CountDownArea area) { return true; }
        private bool IsZombieModeRunValid(int run) { return runState.RunId==run; }private bool TryDispatchZombieModeExtractionSuccess(CountDownArea area) { return true; }private void TryReleaseZombieModeExtractionCountdownUi() { }private void ShowZombieModeExtractionOpportunityUi(int run) { }
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
            Console.WriteLine("ZombieCoopFlow: PASS "+assertions+" source-linked assertions (16 participants)");
        }
    }
    internal static class Program { private static void Main() { ZombieModeRuntimeModule.Run(); } }
}
