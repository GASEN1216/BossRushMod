using System;
using System.Collections.Generic;
namespace UnityEngine
{
    internal struct Vector3 { public float x,y,z; internal Vector3(float a,float b,float c) { x=a;y=b;z=c; } internal float sqrMagnitude { get { return x*x+y*y+z*z; } } public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); } }
    internal class Transform { internal Vector3 position; }
    internal static class Mathf { internal static float Max(float a,float b) { return Math.Max(a,b); } }
}
internal sealed class Health { internal bool IsDead; internal float MaxHealth=100; }
internal sealed class CharacterMainControl { internal static CharacterMainControl Main; internal readonly List<ItemStatsSystem.Item> Inventory=new List<ItemStatsSystem.Item>();internal readonly Health Health=new Health(); internal readonly UnityEngine.Transform transform=new UnityEngine.Transform(); internal int InstanceId; internal int GetInstanceID() { return InstanceId; } }
internal static class PlayerStorageBuffer
{
    internal static readonly List<ItemStatsSystem.Data.ItemTreeData> Buffer=new List<ItemStatsSystem.Data.ItemTreeData>();
    internal static bool SaveAvailable=true;internal static int Saves;
    internal static void SaveBuffer() { Saves++;if(!SaveAvailable)throw new Exception("native buffer save failure"); }
}
internal static class PlayerStorage { internal static int Pushes;internal static void Push(ItemStatsSystem.Item item,bool directly) { Pushes++;PlayerStorageBuffer.Buffer.Add(ItemStatsSystem.Data.ItemTreeData.FromItem(item));item.DestroyTree(); } }
namespace ItemStatsSystem.Data
{
    internal sealed class ItemTreeData { internal int TypeID;public int rootInstanceID;public int RootTypeID { get { return TypeID; } }internal static ItemTreeData FromItem(ItemStatsSystem.Item item) { return new ItemTreeData {TypeID=item.TypeID,rootInstanceID=item.Id}; } }
}
namespace ItemStatsSystem
{
    internal sealed class Item { private static int serial;internal readonly int Id=++serial;internal int TypeID;internal bool Destroyed;internal void Detach() { CharacterMainControl.Main.Inventory.Remove(this); }internal void DestroyTree() { Destroyed=true; } }
    internal static class ItemAssetsCollection { internal static bool Available=true;internal static int FailType;internal static Item InstantiateSync(int id) { return Available && id!=FailType?new Item {TypeID=id}:null; } }
}
namespace BossRush
{
    internal static class CampaignContentCatalog { internal const string ModeModeF="modeF",ModeZombie="zombie"; }
    internal static class CampaignObjectiveTracker {
        internal sealed class ExtractionLease {internal string Chapter,Asset;internal bool Reported;}
        internal static bool ObservationAvailable=true;internal static readonly List<string> Observations=new List<string>();
        internal static bool ObserveExtractionLeaseKill(ExtractionLease lease,int sequence,bool marked) {if(lease!=null)Observations.Add(lease.Chapter+":"+sequence+":"+marked);return ObservationAvailable;}
        internal static string Chapter="old";internal static bool SaveAvailable=true;internal static readonly List<string> Reports=new List<string>();
        internal static ExtractionLease CaptureExtractionLease(string mode) { return new ExtractionLease {Chapter=Chapter,Asset=BossRushCoop.LocalAssetId}; }
        internal static bool ReportExtractionLease(ExtractionLease lease) {if(lease==null||lease.Reported)return true;if(lease.Asset!=BossRushCoop.LocalAssetId||!SaveAvailable)return false;Reports.Add(lease.Chapter);lease.Reported=true;return true;}
    }

    internal sealed class BossRushCoopPlayer { internal string Id,AssetId; internal bool IsConnected=true; internal bool IsLocal { get { return Id==BossRushCoop.LocalPlayerId; } } internal CharacterMainControl Character=new CharacterMainControl(); internal bool IsAlive { get { return IsConnected && Character!=null && !Character.Health.IsDead; } } }
    internal static class BossRushCoop { internal static string LocalPlayerId="host",LocalAssetId="A",RunId="room-one"; internal static bool HasBoundScene;internal static bool IsConnected=true,IsHost=true,IsClient=false,SessionReady=true; internal static readonly List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>(); internal static BossRushCoopPlayer FindPlayer(string id) { return Players.Find(p=>p.Id==id); } internal static string GetPlayerId(CharacterMainControl c) { var p=Players.Find(x=>x.Character==c);return p!=null?p.Id:null; } internal static CharacterMainControl FindEntity(string id) { return null; } }
    internal sealed class FakePersonal { internal sealed class Entry { internal string Player,Id,Asset; internal CoopModeFPersonal Payload; } internal readonly List<Entry> Pending=new List<Entry>(); internal bool Accept=true; internal int Attempts; internal bool HasPendingConnectedDeliveries; internal bool HasPendingForIdPrefix(string prefix) { return Pending.Exists(p=>p.Id.StartsWith(prefix,StringComparison.Ordinal) && BossRushCoop.FindPlayer(p.Player).IsConnected && p.Asset==BossRushCoop.FindPlayer(p.Player).AssetId); }internal void Tick(float dt) { } internal bool Deliver(string player,string id,CoopModeFPersonal payload,string asset=null) { Attempts++;if(!Accept)return false; Pending.Add(new Entry {Player=player,Id=id,Payload=payload,Asset=asset??BossRushCoop.FindPlayer(player).AssetId}); return true; } }
    internal sealed class FakeChannel { internal int Requests; internal void Tick(float dt) { } internal void Request(string action,string text=null,bool flag=false) { Requests++; } internal void RequestRepeat(string action,string text=null,bool flag=false) { Request(action,text,flag); } }
    internal sealed class FakeEquipment { internal int Starter;internal bool Available=true;internal bool Materialize; internal bool GivePlayerStarterKit() { Starter++;if(Materialize)CharacterMainControl.Main.Inventory.Add(new ItemStatsSystem.Item {TypeID=999});return Available; } internal void EnsureModeDGlobalItemPool() { } }
    internal sealed class FakeE { internal void ModeEGiveColdWeatherGear() { } internal bool CommitCoopModeEntry(string tag,bool bloodhunt,Action extras) { extras();return true; } }
    internal sealed class FakeMerchant { internal void BindCoopEntity(string entity) { } }
    internal sealed class CoopModeFFortificationState { }
    internal static class CoopAiPredictionHealthProtection { internal static void MarkReplica(CharacterMainControl c) { } }
    internal enum ModeFPhase { None,Preparation,Bounty,Extraction }
    internal sealed class FakeState { internal bool IsActive,ExtractionPointSpawned,ExtractionResolved,BloodfireOverloadActive;internal int RuntimeSessionToken,CurrentBountyLeaderMarks,PlayerKillCount,PlayerBountyMarks; internal float PhaseElapsed,PhaseDuration,TempMaxHealthGrowth,BloodfireCharge,BloodfireOverloadRemaining,InitialMaxHealthSnapshot;internal ModeFPhase CurrentPhase;internal CharacterMainControl CurrentBountyLeader;internal readonly List<CharacterMainControl> ActiveBosses=new List<CharacterMainControl>();internal readonly Dictionary<int,int> BountyMarksByCharacterId=new Dictionary<int,int>(); }
    internal sealed class FakeReward { internal float healAmount,maxHealthGain,bloodfireGain,overloadExtension;internal bool overloadStarted; }
    internal static class FoldableCoverPackConfig { internal const int TYPE_ID=500037; }
    internal static class EmergencyRepairSprayConfig { internal const int TYPE_ID=500040; }
    internal static class ReinforcedRoadblockPackConfig { internal const int TYPE_ID=500038; }
    internal static class BarbedWirePackConfig { internal const int TYPE_ID=500039; }
    internal static class SafeRuntime { internal static void Run(string label,Action run) { run(); } }
    internal sealed class FakeArena { internal int Rolls;internal int GetRandomInfiniteHellHighQualityRewardTypeID() { Rolls++;return 700000+Rolls; } }
    internal static class L10n { internal static string T(string a,string b) { return b; } }
    internal enum Teams { player,scav }
    internal static class BloodhuntTransponderConfig { internal const int TYPE_ID=500001; }
    internal static class ModeHRuntimeGates { internal static bool Allowed=true;internal static bool IsLegacyModeEntryAllowed() { return Allowed; } }
    internal static class ModBehaviour { internal const string RichSuccessTag="";internal static void DevLog(string message) { } }
    internal static class BossRushMapSelectionHelper { internal static bool Prepaid;internal static int GetBossRushTicketTypeId() { return 500002; }internal static bool HasPendingPrepaidTicket() { return Prepaid; }internal static void ClearPendingEntryFlowState() { Prepaid=false; } }
    internal static class ModeEntryInventory { internal static bool Naked=true;internal static ItemStatsSystem.Item FindFirstPlayerInventoryItemByTypeId(int id) { return CharacterMainControl.Main.Inventory.Find(x=>x.TypeID==id && !x.Destroyed); } internal static bool IsPlayerNakedWithAllowedItems(string tag,int ticket,int fee,bool allow) { return Naked; } }
    internal static class SkyIslandInventoryTransaction { internal static bool Deliver=true;internal static int Attempts;internal static bool TryDeliver(ItemStatsSystem.Item item,CharacterMainControl player) { Attempts++;if(!Deliver)return false;player.Inventory.Add(item);return true; } }
    internal sealed class FakeHost { internal bool CampaignEnabled;internal bool IsCampaignConfiguredEnabled() { return CampaignEnabled; }internal int CampaignNotified;internal Action CampaignAction;internal void NotifyCampaignModeFExtracted() { CampaignNotified++;if(CampaignAction!=null)CampaignAction(); }internal void ShowBigBanner(string text) { }internal bool Naked=true;internal bool IsPlayerNaked() { return Naked; }internal bool IsCurrentSceneValidBossRushArena() { return true; }internal void ShowMessage(string text) { } }
    internal sealed class CoopGameplayCommand { internal string PlayerId,AssetId,Action,Text;internal bool Flag; }
    internal sealed class CoopModeEParticipant { internal string Id,AssetId;internal bool Admitted,Declined; }
    internal partial class ModeERuntimeModule
    {
        private readonly Dictionary<string,bool> coopEntryDecisions=new Dictionary<string,bool>();
        private readonly Dictionary<string,List<ItemStatsSystem.Item>> coopEntryRefunds=new Dictionary<string,List<ItemStatsSystem.Item>>();
        private readonly Dictionary<string,List<int>> coopEntryRefundTypes=new Dictionary<string,List<int>>();
        internal readonly FakeEquipment equipment=new FakeEquipment {Materialize=true};internal readonly FakeHost modeEHost=new FakeHost();
        internal string coopEpoch="entry-run";internal bool modeEActive=true;internal readonly Dictionary<string,CoopModeEParticipant> coopModeEParticipants=new Dictionary<string,CoopModeEParticipant>();
        private void ExecuteCoopCoal(CoopGameplayCommand c) { }private void ExecuteCoopUtility(CoopGameplayCommand c) { }private void ExecuteCoopModeEHire(CoopGameplayCommand c) { }private bool TryStartModeE() { return false; }
        internal void Command(CoopGameplayCommand c) { ExecuteCoopModeE(c); }
        private (Teams?,ItemStatsSystem.Item) DetectFactionFlag() { var flag=ModeEntryInventory.FindFirstPlayerInventoryItemByTypeId(600001);return (flag!=null?(Teams?)Teams.scav:null,flag); }
        private bool CaptureModeEStartupInventorySnapshot(out HashSet<int> result) { result=new HashSet<int>();foreach(var item in CharacterMainControl.Main.Inventory)result.Add(item.Id);return true; }
        private bool RollbackModeEStartupInventory(HashSet<int> before) { CharacterMainControl.Main.Inventory.RemoveAll(x=>!before.Contains(x.Id));return true; }
        internal void Refund() { TickCoopEntryRefunds(); }
    }
    internal partial class ModeFRuntimeModule
    {
        private readonly Dictionary<string,CoopModeFLocalLedger> coopLocalLedgers=new Dictionary<string,CoopModeFLocalLedger>();
        private int coopModeFPrepaidBountyRewards=-1;private string coopLedgerRoom,coopProjectionRoom;private bool coopProjectionRetiring;
        internal int modeFSuccessfulExtractionCount;
        internal readonly FakeArena arena=new FakeArena();
        internal bool SupplyAvailable=true;internal int FailedSupplyType;internal readonly List<int> SupplyTypes=new List<int>();
        internal readonly Dictionary<string,CoopModeFPlayerState> coopPlayers=new Dictionary<string,CoopModeFPlayerState>();
        private readonly HashSet<string> coopStartedAssets=new HashSet<string>();private readonly Dictionary<string,bool> coopAdmissionDecisions=new Dictionary<string,bool>();private float coopAdmissionRetry;
        internal string coopEpoch,coopStartedEpoch,coopLocalAsset,coopExtractedEpoch;internal int coopAppliedKillCount;
        internal bool modeFActive=true,modeFPlayerDeathHandled,coopExtractionAwaitingReceipts,coopPersonalSettlementPending;
        private float coopExtractionElapsed;private UnityEngine.Vector3 coopExtractionPosition;
        private const float MODEF_EXTRACTION_COUNTDOWN_SECONDS=15;
        internal readonly FakeState modeFState=new FakeState { IsActive=true };
        internal readonly FakePersonal coopPersonal=new FakePersonal();private readonly FakeChannel coopChannel=new FakeChannel();
        internal readonly FakeEquipment equipment=new FakeEquipment();private readonly FakeE modeE=new FakeE();private readonly FakeMerchant merchantRuntime=new FakeMerchant();
        private readonly FakeHost owner=new FakeHost();private void ExecuteCoopFortification(CoopGameplayCommand c) { }private void ExecuteCoopFortificationHit(CoopGameplayCommand c) { }private bool TryStartModeF() { return false; }
        private readonly HashSet<CharacterMainControl> modeFActiveBossSet=new HashSet<CharacterMainControl>();private object modeFExtractionMapMarker;
        internal int Exits,Deaths,RewardCalls,NativeReturns;internal int Extractions { get { return modeFSuccessfulExtractionCount; } }internal readonly List<int> Grants=new List<int>();internal readonly List<int> SettledMarks=new List<int>();
        internal ModeFRuntimeModule() { owner.CampaignAction=()=>SettledMarks.Add(modeFState.PlayerBountyMarks); }
        private void TickCoopFortificationTransactions(float dt) { }private void UpdateCoopExtractionPresentation() { }
        private void TryNotifyModeFExtraction(UnityEngine.Vector3 p) { }private void TryLoadBaseSceneAfterModeFExtraction() { NativeReturns++; }
        private void MarkModeFBountyLeaderDirty(CharacterMainControl c=null) { }private void OnModeFPlayerDeath() { Deaths++;modeFActive=false; }
        private void ExitModeF(bool message=true) { if (coopModeFPrepaidBountyRewards >= 0) SettledMarks.Add(modeFState.PlayerBountyMarks);Exits++;modeFActive=false;modeFState.IsActive=false;modeFState.PlayerKillCount=modeFState.PlayerBountyMarks=0; }
        private void InitializeModeDItemPools() { }private void EnsureModeFPlayerNameTag() { }private void CreateModeFExtractionMapMarker(UnityEngine.Vector3 p) { modeFExtractionMapMarker=new object(); }
        private void ApplyCoopFortifications(CoopModeFFortificationState[] s) { }private void MarkModeFHealthBarNamesDirty() { }
        private bool ApplyCoopFortificationReceipt(CoopModeFPersonal s) { return true; }
        private void GiveModeFItem(int id,string name) { }private FakeReward ApplyModeFKillReward(bool marks,int amount) { RewardCalls++;return new FakeReward(); }
        private bool TryGiveItemToPlayerOrDrop(int id,string displayName,bool show=true,bool drop=true) { if(!SupplyAvailable || id==FailedSupplyType)return false;SupplyTypes.Add(id);Grants.Add(modeFState.PlayerKillCount);return true; }
        private string GetModeFUtilityItemDisplayName(int id) { return id.ToString(); }
        private string BuildModeFKillRewardBubbleText(bool m,float h,float max,float b,bool over,float ext) { return ""; }
        private void ShowModeFRewardBubble(string text,float seconds) { }private void MarkModeFPlayerNameTagDirty() { }
        private int DeliverModeFPendingUtilityRewardsToStorage() { return 0; }private int GetModeFStorageNotificationQueueCount() { return 0; }private void ClearModeFStorageNotificationQueue(int count) { }
        private void LatchModeFPlayerBountyKill(int id,bool b) { }private void ApplyModeFPhasePressure() { }private void RefreshModeFBountyLeaderIfDirty() { }
        internal int LedgerCount {get {return coopLocalLedgers.Count;}}
        internal void EnableCampaign() { owner.CampaignEnabled=true; }
        internal void Begin() { BeginCoopModeF(); }
        internal void Tick() { TickCoopModeF(1); } internal int AdmissionReports {get{return coopChannel.Requests;}} internal void AdmitForTest(bool value) {coopAdmissionDecisions[BossRushCoop.LocalAssetId+":"+coopEpoch]=value;}
        internal bool Apply(CoopModeFPersonal r) { return ApplyCoopModeFPersonal(r); }
        internal void World(CoopModeFState s) { ApplyCoopModeF(s); }
        internal void Kill(CharacterMainControl v,CharacterMainControl k) { DeliverCoopModeFKill(v,k); }
        internal int Marks(CharacterMainControl c) { return GetCoopModeFPlayerMarks(c); }
        internal void Extract() { CompleteCoopModeFExtraction(); }
        internal void Command(CoopGameplayCommand c) { ExecuteCoopModeF(c); }
    }
}
