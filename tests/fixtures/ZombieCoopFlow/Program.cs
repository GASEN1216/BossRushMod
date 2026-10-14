using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    }
    public sealed class Transform { public Vector3 position; }
    public static class Mathf { public static int Max(int a, int b) { return Math.Max(a,b); } }
    public static class Time { public static float unscaledTime; }
}
public sealed class Health { public bool IsDead; }
public sealed class CharacterMainControl { public static CharacterMainControl Main; public readonly Health Health=new Health(); public readonly UnityEngine.Transform transform = new UnityEngine.Transform(); }
public sealed class CountDownArea { public readonly UnityEngine.Transform transform = new UnityEngine.Transform(); }
namespace Duckov.Economy { public static class EconomyManager { public static bool Succeed = true; public static long Cash; public static bool Add(long amount) { if(!Succeed)return false; Cash+=amount;return true; } } }
namespace Duckov.UI { public static class NotificationText { public static bool Throw; public static void Push(string text) { if (Throw) throw new InvalidOperationException("notification fault"); } } }
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

    internal static class L10n { internal static string T(string key) { return "{0}"; }internal static string T(string a,string b) { return b; } }
    internal sealed class BossRushCoopPlayer
    {
        internal string Id, AssetId = "slot-A";
        internal bool IsConnected = true, IsAlive = true;
        internal readonly CharacterMainControl Character = new CharacterMainControl();
    }
    internal static class BossRushCoop
    {
        internal static bool IsConnected = true;
        internal static bool IsHost=true,HasWorldAuthority=true;internal static bool IsClient {get {return IsConnected&&!IsHost;}}internal static string RunId="z-room";
        internal static string LocalAssetId = "slot-A";
        internal static readonly List<BossRushCoopPlayer> Players = new List<BossRushCoopPlayer>();
        internal static bool AnyPlayerAlive { get { return Players.Exists(p=>p.IsConnected && p.IsAlive); } }
    }
    internal static class ZombieModeTuning { internal const float ExtractionAreaTriggerRadius = 3f; }
    internal enum ZombieModeCombatPhase { Combat, SuccessExit,RewardSelection,ExtractionOpportunity }
    internal enum ZombieModeLifecyclePhase { None,WaitingStarterChoice,Active,Exiting }
    internal enum ZombieModeFailureReason { SuccessfulExtraction,Unknown }
    internal sealed class ModBehaviour { internal bool CampaignEnabled;internal bool IsCampaignConfiguredEnabled() { return CampaignEnabled; }internal int Extracts;internal void ShowBigBanner(string text) { } internal void NotifyCampaignZombieExtracted() { Extracts++; } internal static void DevLog(string value) { }internal void CleanupZombieModeForRuntimeModule(ZombieModeFailureReason reason) { } }
    internal sealed class FlowState { internal readonly CountDownArea ActiveExtractionArea=new CountDownArea();internal bool ExtractionChanneling,BeaconChanneling;internal int RunId = 11, CurrentWave = 5, PurificationPoints; internal bool ExtractionSuccessHandled; internal ZombieModeCombatPhase CombatPhase;internal ZombieModeLifecyclePhase LifecyclePhase=ZombieModeLifecyclePhase.Active; }
    internal sealed class ZombieModeEntryTransaction { internal bool CashTemporarilyHeld=true, InvitationTemporarilyHeld=true; internal long CashWithheldAmount=250; }
    internal static class ZombieModeEntryDebt { internal static long Cash;internal static int Invitations;internal static bool RefundCash(long x) { Cash+=x;return true; }internal static bool RefundInvitation() { Invitations++;return true; } }
    internal class RuntimeBase { public virtual void OnUpdate(float deltaTime,float unscaledDeltaTime) { } }
    internal sealed class FakeChannel { internal sealed class Entry {internal string Player,Id,Asset;internal CoopZombiePersonal Receipt;}internal readonly List<Entry> Pending=new List<Entry>();internal bool HasPendingConnectedDeliveries;internal bool HasPendingForIdPrefix(string prefix) { return HasPendingConnectedDeliveries; }internal void Tick(float dt) { }internal void Deliver(string player,string id,CoopZombiePersonal receipt,string asset) { Pending.Add(new Entry {Player=player,Id=id,Asset=asset,Receipt=BossRushCoopJson.FromJson<CoopZombiePersonal>(BossRushCoopJson.ToJson(receipt))}); } }
    internal sealed partial class ZombieModeRuntimeModule : RuntimeBase
    {
        private readonly FakeChannel coopZombieWorld=new FakeChannel(),coopZombiePersonal=new FakeChannel();
        private bool coopZombieExtractionPending,coopZombiePersonalDeath,coopZombieStarterChosen;
        private float coopZombieReadyRetry;private int coopZombieCompletedRewardWave=-1;private int Preparations;
        private void TryNotifyZombieModeExtraction() { }private void TryLoadBaseSceneAfterZombieModeExtraction() { }
        private void SettleZombieModeFailureInsuranceShell(int run) { }private void RemoveZombieModeAttributeModifiers() { }private void RemoveZombieModeOptionRuntimeEffects() { }
        private void SendCoopZombieCommand(string action,int value=0) { }private void UnlockZombieModeContainersForActiveRun(int run) { }
        private void BeginZombieModePreparation(int run,bool first,bool keep) { Preparations++;runState.CombatPhase=ZombieModeCombatPhase.Combat; }
        private void BeginZombieModeExtractionOpportunity(int run) { }private void FailZombieModeActive(int run) { }
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
            var contracted=new ZombieModeRuntimeModule();contracted.runState.RunId=20;contracted.coopZombieStarterChosen=true;contracted.owner.CampaignEnabled=true;CampaignObjectiveTracker.Chapter="old-z-contract";contracted.RememberCoopZombieWallet();
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
            BossRushCoop.IsHost=false;var stopped=new ZombieModeRuntimeModule();stopped.coopZombieProjectionRoom=BossRushCoop.RunId;stopped.coopZombieLocalRun=11;BossRushCoop.IsConnected=false;stopped.OnUpdate(1,1);Check(stopped.coopZombieProjectionRoom==null&&stopped.coopZombieLocalRun==0,"stopped native network retires old zombie projection before offline fallback");BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;
            Console.WriteLine("ZombieCoopFlow: PASS "+assertions+" source-linked assertions (16 participants)");
        }
    }
    internal static class Program { private static void Main() { ZombieModeRuntimeModule.Run(); } }
}
