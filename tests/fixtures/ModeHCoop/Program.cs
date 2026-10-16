using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public static class Debug { public static void LogWarning(string x) {} }
    public static class Mathf { public static float Max(float a, float b) { return Math.Max(a,b); } }
}
namespace BossRush
{
    enum ModeHLifecycle { None, Drafting, RosterLocked, MatchBrief, MatchFighting, Intermission }
    class Run { public string RunId="season"; public long RunSeed=17,OwnerToken=1,StateSequence; public int MatchIndex=1,TechnicalRetrySequence; public ModeHLifecycle Lifecycle=ModeHLifecycle.Drafting; public void RestoreMatchIndex(int match,int retry,long sequence) {MatchIndex=match;TechnicalRetrySequence=retry;StateSequence=sequence;} }
    class Roster { public int RerollsUsed,MatchIndex; public string RunId; public List<int> Allies=new List<int>(),Enemies=new List<int>(); }

    class Health {public bool IsDead;}
    class CharacterMainControl {public Health Health;public object characterPreset; public Item CharacterItem;public static CharacterMainControl Main {get{return BossRushCoop.FindPlayer(BossRushCoop.LocalPlayerId).Character;}}}
    class ModeHSpectatorLease {public static int Acquires;public void Release(int generation) {}public bool TryAcquire(int position,int generation,long owner,out string error) {Acquires++;error=null;return true;}}
    class Map {public int SpectatorPos;public string SceneName="TestArena",SceneId="TestArenaID";}
    class BossRushCoopPlayer { public string Id; public string AssetId="slot-A"; public bool IsConnected=true; public CharacterMainControl Character; }
    static class BossRushCoop
    {
        public static bool IsConnected=true, IsHost=true, IsClient=false, SessionReady=true, SceneReady=true;
        public static bool HasBoundScene;
        public static string SceneToken;
        public static string LocalPlayerId="p0",RunId="h-room";public static void EndControlLease(string lease){}
        public static string LocalAssetId { get { return FindPlayer(LocalPlayerId).AssetId; } }
        public static List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>();
        public static void MarkSceneReady(string key) {}
        public static bool AllPlayersSceneReady(string key) { return SceneReady; }
        public static bool ArePlayersSceneReady(string key,IEnumerable<string> participants) { return SceneReady; }
        public static BossRushCoopPlayer FindPlayer(string id) { return Players.Find(x=>x.Id==id); }
    }
    class CoopGameplayCommand { public string PlayerId, Text, Action, AssetId; }
    class CoopGameplayChannel<T>
    {
        public int Requests;
        public void Tick(float dt) {} public void Publish() {}
        public bool Request(string action, string text=null) { Requests++; return true; }
        public bool RequestRepeat(string action, string text=null) { return Request(action, text); }
    }
    class CoopPersonalDelivery<T>
    {
        public bool HasPendingConnectedDeliveries;
        public int Starts;
        public string LastTargetAsset; public readonly Dictionary<string,string> Targets = new Dictionary<string,string>();
        public void Tick(float dt) {}
        public bool HasPendingForPlayers(IEnumerable<string> ids) { return HasPendingConnectedDeliveries; }
        public bool Deliver(string player,string id,T payload,string targetAssetId=null) { Starts++; LastTargetAsset=targetAssetId; Targets[player]=targetAssetId;return true; }
    }
    class Ui { public void ApplyHudVisibility() {} }
    class CombatControl { public string Operator; public void SetCoopOperator(string player) { Operator=player; } }
    partial class ModeHRuntimeModule
    {
        private string _coopWorldAsset,_coopWorldRun,_coopWorldRoom;
        private bool _coopWorldWasConnected;
        private string _coopProjectionToken;
        private object _season=new object();private Map _map;
        private bool _seasonDirty;
        private int _sceneGeneration;
        private ModeHSpectatorLease _spectatorLease=new ModeHSpectatorLease();
        public int Released, Restored, Reconciled;
        private void EnsureContentScanned() {}
        private void CancelSceneReadyWait() {}
        private void ReleaseRuntimeObjects() {Released++;}
        private void BeginNewRunSession() {}
        private void RestoreFromSaveIfPresent() {Restored++;}
        private void ReconcileCashBetOnRestore() {Reconciled++;}
        private void ProjectRunStateIntoSeason() {}
        private void LogFailure(string name,Exception e) {throw e;}
        public void FreezeAsset() {BindCoopWorldAsset();}
        public void SetMap() {_map=new Map();}
        public void SwitchSlot() {RestoreForSlotChange();}
        public bool Save() {return TryPersistSeason("fixture",true);}
        public bool PreparePersonal(int match) {return _coopLedger.Prepare("season",match,17,false,2);}
        public bool Result(ModeHCoopResult result) {return ApplyCoopResult(result);}
        CoopGameplayChannel<ModeHCoopWorld> _coopChannel=new CoopGameplayChannel<ModeHCoopWorld>();
        CoopPersonalDelivery<ModeHCoopResult> _coopResults=new CoopPersonalDelivery<ModeHCoopResult>();
        ModeHCoopLedger _coopLedger=new ModeHCoopLedger();
        Ui _ui; Run _runState=new Run(); Roster _groupRoster=new Roster();
        ModeHCoopWorld _coopView;
        HashSet<string> _coopRequested=new HashSet<string>(), _coopFunded=new HashSet<string>(), _coopForfeited=new HashSet<string>(), _coopExited=new HashSet<string>();
        Dictionary<string,string> _coopRequestedAssets=new Dictionary<string,string>(),_coopFundedAssets=new Dictionary<string,string>();
        Dictionary<string,string> _coopSkipped=new Dictionary<string,string>(); HashSet<string> _coopSkipQueued=new HashSet<string>();
        public string Target(string id) {string asset;return _coopResults.Targets.TryGetValue(id,out asset)?asset:null;} public void Skip() {StartCoopWithConfirmedPlayers();} public bool Skipped(string id,string asset) {return IsCoopSkipped(id,asset);}
        string _coopRound, _coopStartedRound,_coopParticipantRun,_coopBellRequester;
        bool _coopProjection, _coopStarting, _commandsClosed,_shutdownCompleted,_restoredSeasonPending,_resumeScenePending;string _coopProjectionRoom,_coopControlLease,_coopUiStamp,_coopPrepared;
        void DestroyUi() {Released++;}
        public void BeginProjection(){_coopProjection=true;_coopProjectionRoom=BossRushCoop.RunId;_coopProjectionToken=BossRushCoop.SceneToken;}public bool Projecting{get{return _coopProjection;}}
        CombatControl _combatControl=new CombatControl();
        bool GroupModeEnabled { get { return true; } }
        int GroupMatchIndex { get { return 1; } }
        public int Starts, Bells;
        void RestoreCoopSpectators() {} void ProtectCoopSpectators(float dt) {}
        void RefreshCoopSlot() {}
        public int SpectatorTicks;
        void TickCoopSpectator(float dt) {SpectatorTicks++;} void RouteUiForLifecycle(ModeHLifecycle phase) {}
        bool IsGroupRosterPhase() { return _runState.Lifecycle==ModeHLifecycle.Drafting || _runState.Lifecycle==ModeHLifecycle.MatchBrief; }
        void StartGroupMatch() { if(RequestCoopStart()) return; Starts++; _runState.Lifecycle=ModeHLifecycle.MatchFighting; }
        void RerollGroupRoster() { if(RequestCoopReroll()) return; _groupRoster.RerollsUsed++; }
        void OnBellPressed() { Bells++; }
        public void Command(string player,string action,string round=null) { ExecuteCoopCommand(new CoopGameplayCommand { PlayerId=player,Action=action,Text=round??CurrentCoopRound(),AssetId=BossRushCoop.FindPlayer(player)!=null?BossRushCoop.FindPlayer(player).AssetId:"unknown" }); }
        public void Tick() { TickCoop(1f); }
        public int Rerolls { get { return _groupRoster.RerollsUsed; } }
        public int Funds { get { return _coopFunded.Count; } }
        public void Pending(bool value) { _coopResults.HasPendingConnectedDeliveries=value; }
        public int StartReceipts { get { return _coopResults.Starts; } }
        public bool IsFunded(string id) { return IsCoopFunded(id); }
        public string BetAsset(string id) { return FundedAsset(id); }
        public string LastAsset { get { return _coopResults.LastTargetAsset; } }
    }
    static class ModeHGroupTeamTags {public static void SetActive(bool x){}}
    static class ModeHRuntimeGates { public static int SlotGeneration; public static void SetCombatFrameActive(bool x){}public static bool OwnerActive;public static void SetRunOwnerActive(bool x) {OwnerActive=x;} }
    static class BossRushMapSelectionHelper { public static bool Pending; public static int Generation; public static bool HasPendingModeHEntryIntent() {return Pending;} public static int GetPendingModeHSceneGeneration() { return Generation; } public static int FreezeModeHEntryIntent(string name,string id) { Pending=true;return Generation=77; } }
    static class ModeHEntry {public static void CancelPendingEntry() {BossRushMapSelectionHelper.Pending=false;}}
    static class ModeHTicketRefund {public static void ResetStaticCaches() {}}
    static class ModeHSessionSummary {public static void Discard() {}}
    class BossRushSlotJsonStoreSpec<T> where T:class
    {
        public string StorageKey,LogPrefix,DisplayName; public int SchemaVersion;
        public Func<T,string> Encode; public Func<string,T> Decode; public Func<string,int> ReadSchemaVersion;
        public Func<T> CreateDefault; public Action NotifySlotChanged,AfterPendingWriteFlushed;
    }
    class BossRushSlotJsonStore<T> where T:class
    {
        readonly BossRushSlotJsonStoreSpec<T> spec;
        T value; public bool HasPendingWrite,IsStoreFaulted; public string LastError;
        public BossRushSlotJsonStore(BossRushSlotJsonStoreSpec<T> spec) { this.spec=spec; value=spec.CreateDefault(); }
        public T Current { get { return value; } }
        public void EnsureSubscribed() {} public void ResetAll() {}
        public bool Store(T next) { if (Faults.StoreFail) return false; value=spec.Decode(spec.Encode(next)); HasPendingWrite=true; return true; }
        public bool FlushPending() { HasPendingWrite=false;return true; }
    }
    interface IBossRushSaveBatchSource
    {
        string LogPrefix {get;} bool HasPendingWrite {get;} bool IsStoreFaulted {get;} bool HasSnapshotObligation {get;}
        string LastError {get;} bool CollectSnapshot(out string error); bool FlushPending(); void OnPhysicalSaveSucceeded();
    }
    class BossRushSaveCoordinatorEngine
    {
        IBossRushSaveBatchSource source;
        public BossRushSaveCoordinatorEngine(IBossRushSaveBatchSource source,bool flag) {this.source=source;}
        public void Reset() {} public void NotifyPendingWriteFlushed() {} public void Tick() {}
        public bool RequestFlush(out string error,bool bypass=false) { error=null;return !Faults.FlushFail && source.FlushPending(); }
    }
    static class Faults { public static bool StoreFail,FlushFail,PaymentFail; }
    static class ModeHConfig { public const int SeasonMatchCount=6,CurrentSchemaVersion=1,MinOdds=1,MaxOdds=5; }
    partial class ModeHGroupBattle { public const int WinnerBlue=1,WinnerRed=2; }
    static class L10n { public static string T(string cn,string en) {return en;} }
    class ModeHCashBetRecord { public string runId; public int matchIndex,status,kind,betSide,odds,combatStarted; public long payout,amount; }
    static class ModeHCashBetService
    {
        public const int StatusReserved=1,StatusSettled=2,KindItems=1;
        public const int BetSideBlue=1,BetSideRed=2;
        public static ModeHCashBetRecord Current;
        public static long StandingAmount;
        public static int Reserves;
        public static bool TryReserve(string run,int match,int odds,long amount,out string failure,int side)
        {failure=null;if(Current!=null&&Current.status==StatusReserved)return false;Reserves++;Current=new ModeHCashBetRecord {runId=run,matchIndex=match,odds=odds,amount=amount,status=StatusReserved,betSide=side};return true;}
        public static int Payments,ItemPayments,Refunds; public static bool TryRefund(string context,out long amount) {amount=0;if(Faults.PaymentFail || Current==null || Current.status!=StatusReserved)return false;amount=Current.amount;Current.payout=amount;Current.status=3;Refunds++;return true;}
        public static ModeHCashBetRecord ReservedFor(string run,int match) {return Current!=null&&Current.status==StatusReserved&&Current.runId==run&&Current.matchIndex==match?Current:null;}
        public static bool TryMarkCombatStarted(string run,int match) {return true;}
        public static bool TrySettleItems(string run,int match,bool won,long seed) {ItemPayments++;long paid;return TrySettle(run,match,won,0,0,"",out paid);}
        public static bool TrySettle(string run,int match,bool won,long a,long b,string c,out long payout)
        {
            payout=0;if(Faults.PaymentFail)return false;
            if(Current==null||Current.runId!=run||Current.matchIndex!=match||Current.status!=StatusReserved)return false;
            Current.status=StatusSettled;Current.payout=payout=won?Current.amount*2:0; Payments++;return true;
        }
        public static long SaturatingAdd(long a,long b) {return a+b;}
    }
    class ModeHHallOfFameRecordDto
    {
        public string hallOfFameId,archetypeId,quirkId,createdUtc,aliasKey,temperamentId,anomalyId,signatureCommandId,gameBuildSignature,modBuildSignature;
        public int schemaVersion,seasonVersion,maxOddsWin,finalVirtualStakeCredits;
        public List<string> scarIds,matchReportIds,substituteHistory;
    }
    static class ModeHGroupHallOfFame { public const string ArchetypeTag="group"; public static string EncodeScore(int w,int m,long n) {return w+":"+m+":"+n;} }
    static class ModeHSaveFlushCoordinator
    {
        public static int SeasonWrites;
        public static bool RequestSeasonWrite(object season,out string error,bool durable) {error=null;SeasonWrites++;return true;}
        public static Dictionary<string,ModeHHallOfFameRecordDto> Hall=new Dictionary<string,ModeHHallOfFameRecordDto>();
        public static bool RequestHallOfFameInsert(ModeHHallOfFameRecordDto record,out string error,bool durable)
        { error=null;if(Faults.FlushFail)return false;Hall[record.hallOfFameId]=record;return true; }
    }
    class Program
    {
        static int checks;
        static void Check(bool condition,string label) {if(!condition)throw new Exception(label);checks++;}
        static void Main()
        {
            BossRushCoop.HasBoundScene=true;BossRushCoop.SceneToken="h-initial-core";
            for(int i=0;i<16;i++)BossRushCoop.Players.Add(new BossRushCoopPlayer {Id="p"+i,Character=new CharacterMainControl {Health=new Health()}});
            var room=new ModeHRuntimeModule();
            room.Command("p0","funded");Check(room.Funds==0,"unrequested funds cannot start a match");
            room.Command("p0","ready","stale-round");room.Command("p0","funded");Check(room.Funds==0,"stale roster cannot take funds");
            for(int i=0;i<15;i++) {room.Command("p"+i,"ready");room.Command("p"+i,"funded");}
            room.Command("p1","reroll");Check(room.Rerolls==0,"first ready freezes shared roster");
            room.Tick();Check(room.Starts==0,"16th participant must confirm");
            room.Command("p15","ready");room.Command("p15","funded");
            BossRushCoop.SceneReady=false;room.Tick();Check(room.Starts==0,"real scene barrier required");
            BossRushCoop.SceneReady=true;room.Pending(true);room.Tick();Check(room.Starts==0,"previous personal payments must finish");
            room.Pending(false);room.Tick();Check(room.Starts==1,"all 16 start exactly one shared match");
            room.Tick();room.Tick();Check(room.Starts==1&&room.StartReceipts==16,"combat-start facts delivered once to each participant");
            room.Command("visitor","bell");Check(room.Bells==0,"late spectator cannot consume bell");
            room.Command("p15","bell");Check(room.Bells==1,"16th participant can ring the shared bell");
            var disconnected=new ModeHRuntimeModule();
            for(int i=0;i<15;i++){disconnected.Command("p"+i,"ready");disconnected.Command("p"+i,"funded");}
            BossRushCoop.Players[15].IsConnected=false;disconnected.Tick();Check(disconnected.Starts==1,"disconnected member does not deadlock readiness");
            BossRushCoop.Players[15].IsConnected=true;
            var deadHost=new ModeHRuntimeModule();
            for(int i=1;i<16;i++){deadHost.Command("p"+i,"ready");deadHost.Command("p"+i,"funded");}
            deadHost.Tick();Check(deadHost.Starts==0,"live unready host still confirms its personal bet");
            BossRushCoop.Players[0].Character.Health.IsDead=true;deadHost.Tick();
            Check(deadHost.Starts==1,"host death before confirming does not block 15 prepared guests");
            BossRushCoop.Players[0].Character.Health.IsDead=false;
            var exited=new ModeHRuntimeModule();
            for(int i=0;i<15;i++){exited.Command("p"+i,"ready");exited.Command("p"+i,"funded");}
            exited.Command("p15","leave");exited.Tick();Check(exited.Starts==1,"connected player who leaves spectating cannot deadlock the next match");
            exited.Command("p15","bell");Check(exited.Bells==0,"exited participant cannot ring the bell");
            var anonymous=new ModeHRuntimeModule();anonymous.Command("unknown","ready");anonymous.Command("unknown","funded");Check(anonymous.Funds==0,"unknown participant cannot join funded cohort");
            var switched=new ModeHRuntimeModule();
            for(int i=0;i<16;i++){switched.Command("p"+i,"ready");switched.Command("p"+i,"funded");}
            BossRushCoop.Players[15].AssetId="slot-B";switched.Tick();
            Check(switched.Starts==1&&!switched.IsFunded("p15"),"changed slot cannot inherit funded state or deadlock the other 15");
            switched.Tick();Check(switched.BetAsset("p15")=="slot-A"&&switched.LastAsset=="slot-A","combat fact retains original payer asset target");
            switched.Command("p15","bell");Check(switched.Bells==0,"new slot cannot control a bet paid by old slot");
            BossRushCoop.Players[15].AssetId="slot-A";

            var hostSwitched=new ModeHRuntimeModule();hostSwitched.FreezeAsset();hostSwitched.SetMap();
            for(int i=0;i<16;i++){hostSwitched.Command("p"+i,"ready");hostSwitched.Command("p"+i,"funded");}
            BossRushCoop.Players[0].AssetId="slot-B";hostSwitched.SwitchSlot();hostSwitched.Tick();
            Check(ModeHSpectatorLease.Acquires==1,"new host character re-acquires original shared spectator lease once");hostSwitched.Tick();
            Check(ModeHSpectatorLease.Acquires==1,"retained host spectator lease is idempotent across frames");
            Check(hostSwitched.Released==0&&hostSwitched.Restored==0&&hostSwitched.Starts==1,"real slot-restore entry keeps host world owner and starts 15 remaining players");
            Check(!hostSwitched.IsFunded("p0")&&ModeHRuntimeGates.OwnerActive,"switched host is a world owner without old asset personal eligibility");
            int writes=ModeHSaveFlushCoordinator.SeasonWrites;Check(hostSwitched.Save()&&ModeHSaveFlushCoordinator.SeasonWrites==writes,"real season persist entry cannot write old season into new slot");
            BossRushCoop.Players[0].AssetId="slot-A";Check(hostSwitched.Save()&&ModeHSaveFlushCoordinator.SeasonWrites==writes+1,"return original asset can persist its own continuing season");
            Check(hostSwitched.PreparePersonal(6)&&hostSwitched.Result(new ModeHCoopResult {Run="season",Match=6,Seed=17,Winner=1,Token="host-final",Final=true}),"host withdrawn from native season can settle retained personal ledger after original asset return");
            Check(ModeHSaveFlushCoordinator.Hall.ContainsKey("hof|season"),"host final personal receipt still records its hall of fame through retained ledger");
            ModeHSaveFlushCoordinator.Hall.Clear();

            var ledger=new ModeHCoopLedger();
            Check(ledger.Prepare("r",1,7,false,2),"personal choice persisted before bet");
            Check(!ledger.ResolveSide("r",1,true),"reconnect retains original side");
            Check(ledger.Prepare("r",1,7,true,9),"same-round retry reuses choice");
            Check(!ledger.Prepare("r",1,8,false,2),"same-round seed mismatch cannot claim readiness");
            ModeHCashBetService.Current=new ModeHCashBetRecord {runId="r",matchIndex=1,status=1,amount=100};
            var result=new ModeHCoopResult {Run="r",Match=1,Seed=7,Winner=1,Token="one"};
            Check(!ledger.Apply(new ModeHCoopResult {Run="r",Match=1,Seed=8,Winner=1,Token="wrong"},false),"wrong seed cannot settle a reserved bet");
            Faults.PaymentFail=true;Check(!ledger.Apply(result,false),"failed payment does not acknowledge result");
            Faults.PaymentFail=false;Faults.StoreFail=true;Check(!ledger.Apply(result,false),"result journal failure remains retryable");
            Faults.StoreFail=false;Check(ledger.Apply(result,false),"retry commits settled result");
            Check(ledger.Apply(result,false)&&ModeHCashBetService.Payments==1,"replayed result never pays twice");
            Check(ledger.IsResolved("r",1),"resolved fact survives receipt retry");
            Check(!ledger.Apply(new ModeHCoopResult {Run="r",Match=1,Seed=7,Winner=2,Token="conflict"},false),"conflicting replay cannot overwrite resolved result");
            Check(!ledger.Apply(new ModeHCoopResult {Run="other",Match=1},false),"foreign run cannot settle personal record");
            Check(ledger.Prepare("r",6,7,true,3),"last round prepared");ledger.Forfeit("r",6);
            ModeHCashBetService.Current=new ModeHCashBetRecord {runId="r",matchIndex=6,status=1,kind=1,amount=50};
            var final=new ModeHCoopResult {Run="r",Match=6,Seed=7,Winner=2,Token="last",Final=true};
            Check(ledger.Apply(final,false),"item forfeit closes through existing item journal");
            Check(ModeHCashBetService.Current.payout==0,"forfeited winner does not collect winnings");
            Check(ModeHSaveFlushCoordinator.Hall["hof|r"].quirkId=="1:2:50","hall of fame uses own picks and own net");
            Check(ledger.Apply(final,false)&&ModeHSaveFlushCoordinator.Hall.Count==1,"hall replay is idempotent");
            Faults.FlushFail=true;Check(!ledger.Prepare("later",1,1,false,1),"durable flush gates ready");
            Faults.FlushFail=false;Check(ledger.Prepare("later",1,1,false,1),"durable prepare can resume");
            var carried=new ModeHCashBetRecord {runId="later",matchIndex=1,status=1,betSide=2,odds=3};
            Check(ledger.AlignReservedBet("later",1,carried)&&ledger.ResolveSide("later",1,false),"personal side follows the actual reserved asset journal");
            Faults.StoreFail=true;Check(!ledger.Forfeit("later",1),"failed forfeit journal cannot report successful exit");Faults.StoreFail=false;
            BossRushCoop.IsHost=false;BossRushCoop.IsClient=true;var projection=new ModeHRuntimeModule();projection.BeginProjection();projection.Tick();Check(projection.Projecting&&projection.Released==0,"H actual client tick retains original native room projection");BossRushCoop.RunId="other-h-room";projection.Tick();Check(!projection.Projecting&&projection.Released==1&&projection.Restored==1,"H actual tick releases old room spectator/runtime before processing new room");projection.Tick();Check(projection.Released==1,"H old room cleanup is idempotent");var stopped=new ModeHRuntimeModule();stopped.BeginProjection();BossRushCoop.IsConnected=false;BossRushCoop.IsClient=false;stopped.Tick();Check(!stopped.Projecting&&stopped.Released==1,"H native StopNetwork releases former client world before offline owner can tick");BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;
            WorldOwnerRegression.Run(Check);
            CachedSceneRegression.Run(Check);
            BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;BossRushCoop.IsClient=false;BossRushCoop.HasBoundScene=true;BossRushCoop.SceneReady=true;
            foreach(var p in BossRushCoop.Players){p.IsConnected=true;p.AssetId="slot-A";if(p.Character!=null&&p.Character.Health!=null)p.Character.Health.IsDead=false;}
            BossRushCoop.Players[1].AssetId="skip-original";var skip=new ModeHRuntimeModule();skip.Command("p0","ready");skip.Command("p0","funded");skip.Command("p1","ready");skip.Skip();
            Check(skip.Target("p1")=="skip-original","H skip refund targets original unconfirmed asset instead of host slot");
            Check(skip.Skipped("p1","skip-original")&&!skip.Skipped("p0","slot-A"),"H host explicit start freezes only unconfirmed original assets");
            skip.Command("p1","funded");Check(!skip.IsFunded("p1"),"H late funding after skip cannot enter current match");skip.Tick();Check(skip.Starts==1,"H explicit start advances confirmed host without 15 unconfirmed players");
            var refund=new ModeHCoopLedger();Check(refund.Prepare("skip-cash",1,777,false,2),"H unconfirmed ledger records original run");ModeHCashBetService.Current=new ModeHCashBetRecord {runId="skip-cash",matchIndex=1,status=1,amount=71};int refunds=ModeHCashBetService.Refunds;
            Faults.PaymentFail=true;Check(!refund.CancelUnstarted("skip-cash",1,777)&&refund.Owns("skip-cash",1)&&ModeHCashBetService.Current.status==1,"H cash refund failure retains original prepared ledger and held bet");Faults.PaymentFail=false;
            Check(refund.CancelUnstarted("skip-cash",1,777)&&!refund.Owns("skip-cash",1)&&ModeHCashBetService.Current.payout==71&&ModeHCashBetService.Refunds==refunds+1,"H skipped cash returns full original amount and removes only unconfirmed participation");Check(refund.CancelUnstarted("skip-cash",1,777)&&ModeHCashBetService.Refunds==refunds+1,"H refund receipt retries cannot pay twice");
            Check(refund.Prepare("skip-item",1,778,false,2),"H next round accepts a new confirmation after skip");ModeHCashBetService.Current=new ModeHCashBetRecord {runId="skip-item",matchIndex=1,status=1,kind=1,amount=90};Check(!refund.CancelUnstarted("skip-item",1,999),"H wrong seed cannot cancel another prepared bet");int releaseBefore=ModeHItemBetStake.Releases;Check(refund.CancelUnstarted("skip-item",1,778)&&ModeHItemBetStake.Releases==releaseBefore+1,"H skipped original item stake unlocks exact existing items");
            Check(refund.Prepare("started",1,779,false,2),"H real confirmed bet owns ledger");ModeHCashBetService.Current=new ModeHCashBetRecord {runId="started",matchIndex=1,status=1,combatStarted=1,amount=80};Check(!refund.CancelUnstarted("started",1,779)&&ModeHCashBetService.Current.status==1,"H skip cannot refund actual started combat");
            Console.WriteLine("ModeHCoop PASS: "+checks+" assertions; production room coordination and ledger; in-memory host/save adapters.");
        }
    }
}
