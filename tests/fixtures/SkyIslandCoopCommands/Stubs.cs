using System;
using System.Collections.Generic;

namespace UnityEngine { public static class Time { public static float time, unscaledTime, realtimeSinceStartup,unscaledDeltaTime=.1f; } }
namespace ItemStatsSystem { public sealed class Item { } }
public sealed class CharacterMainControl { public static CharacterMainControl Main; public string Id; public Health Health=new Health(); public UnityEngine.Transform transform=new UnityEngine.Transform(); public DamageReceiver mainDamageReceiver=new DamageReceiver(); internal BossRush.SkyIslandEnemyLife Life; public T GetComponent<T>() where T:class { return Life as T; } public CharacterMainControl gameObject {get{return this;}} public T AddComponent<T>() where T:new() {var value=new T();Life=value as BossRush.SkyIslandEnemyLife;return value;} }
public struct DamageInfo { public CharacterMainControl fromCharacter; public float damageValue; public UnityEngine.Vector3 damagePoint; public bool isExplosion,isFromBuffOrEffect; public int fromWeaponItemID; public DamageInfo(CharacterMainControl owner) { this=default(DamageInfo);fromCharacter=owner; } }
namespace BossRush
{
    internal enum SkyIslandFieldBuff { None, Zapper, Fan }
    internal static class SkyIslandMosquitoRules { internal const float FanCooldownSeconds=2f; }
    internal static class SkyIslandFieldcraftRules { internal const float TickInterval=.1f;internal const string OffIsland="off"; internal static readonly string[] Nodes=new string[8]; }
    internal static class BossRushItemIds { internal const int SkyIslandGnatZapper=500084; }
    internal struct SkyIslandIngredient { internal SkyIslandIngredient(int id,int amount) { } }
    internal sealed class SkyIslandBounty { internal int Culls; internal void ReportGnatCulled() { Culls++; } }
    internal sealed partial class SkyIslandSession
    {
        internal bool IsReady,IsWorldReady;
        internal SkyIslandBounty Bounty=new SkyIslandBounty();
        internal void Announce(string text,bool warning) { }
    }
    internal enum SkyIslandStoryAction { ZhelingDefeated,BellKeeperDefeated,StormSlain }
    internal sealed class SkyIslandStoryData { internal bool ZhelingResolved,BellKeeperResolved,StormResolved; }
    internal sealed class SkyIslandStoryService { internal bool CanWrite=true; internal SkyIslandStoryData Current=new SkyIslandStoryData();internal bool TryApply(SkyIslandStoryAction action,out string message) {message=null;return false;}internal void LogTiming(string action,string value) { } }
    internal static class SkyIslandOfficialQuestStory { internal static SkyIslandStoryService Resolve(ModBehaviour owner,out bool island) {island=false;return null;} }
    internal sealed class SkyIslandInventoryTransaction : IDisposable
    {
        internal static int Inventory=1,Reserved,Commits,Refunds;
        private bool committed,disposed;
        internal static bool TryReserve(CharacterMainControl owner,SkyIslandIngredient[] inputs,out SkyIslandInventoryTransaction reservation)
        { reservation=null; if(Inventory<1)return false; Inventory--;Reserved++;reservation=new SkyIslandInventoryTransaction();return true; }
        internal void Commit() { if(committed)return;committed=true;Commits++; }
        public void Dispose() { if(disposed)return;disposed=true;Reserved--;if(!committed){Inventory++;Refunds++;} }
        internal static void Reset() { Inventory=1;Reserved=Commits=Refunds=0; }
    }
    internal sealed class BossRushCoopPlayer { internal string Id,AssetId="asset-1";internal CharacterMainControl Character;internal bool IsAlive=true;internal bool IsLocal {get{return Id==BossRushCoop.LocalPlayerId;}} }
    internal sealed class BossRushCoopMessage
    {
        internal string SenderId;internal object Payload; internal bool IsFromHost;
        internal T Read<T>() { return BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(Payload)); }
    }
    internal static partial class BossRushCoop
    {
        internal static bool IsHost,SessionReady=true;
        internal static bool HasBoundScene;
        internal static string SceneToken;
        internal static bool IsConnected=true;
        internal static bool IsClient { get { return IsConnected && !IsHost; } }
        internal static string LocalPlayerId="guest";
        internal static string LocalAssetId="asset-1";
        internal static string RunId="room";
        internal static readonly PlayerCollection Players=new PlayerCollection();
        internal static readonly List<BossRushCoopMessage> Requests=new List<BossRushCoopMessage>();
        internal static BossRushCoopPlayer FindPlayer(string id) { BossRushCoopPlayer p;return Players.TryGetValue(id,out p)?p:null; }
        internal static string GetPlayerId(CharacterMainControl actor) { return actor!=null?actor.Id:null; }
        internal static bool SendToHost<T>(string topic,T payload) { Requests.Add(new BossRushCoopMessage { SenderId=LocalPlayerId,Payload=payload });return true; }
    }
    internal sealed class CoopPersonalDelivery<T>:IDisposable
    {
        internal CoopPersonalDelivery() { }internal CoopPersonalDelivery(string name,Func<T,bool> callback) { }internal void Tick(float delta) { }public void Dispose() { }
        internal sealed class Packet { internal string Player,Id;internal T Payload; }
        internal readonly List<Packet> Sent=new List<Packet>();
        internal bool Deliver(string player,string id,T payload,string asset=null) { Sent.Add(new Packet { Player=player,Id=id,Payload=payload });return true; }
    }
    internal sealed partial class SkyIslandGnats
    {
        private readonly SkyIslandSession session=new SkyIslandSession();
        private readonly SkyIslandStoryService story=new SkyIslandStoryService();
        private bool disposed;private bool Usable { get { return !disposed; } }
        private string CoopTravel { get { return "travel-1"; } }
        internal bool CoopUsePending { get { return coopPendingUse!=null; } }
        private string coopPendingUse,coopPendingAsset;private int coopPendingBuff;private float coopUseAt,coopNextUseRequest,fanReadyAt,coopNextWorld;
        private SkyIslandInventoryTransaction coopUseCost;
        private readonly Dictionary<string,EnvironmentState> coopEnvironments=new Dictionary<string,EnvironmentState>();
        private readonly Dictionary<string,PersonalEvent> coopUses=new Dictionary<string,PersonalEvent>();
        private readonly HashSet<string> coopKilled=new HashSet<string>();
        private readonly Queue<string> coopRecentKilled=new Queue<string>();
        private readonly CoopPersonalDelivery<PersonalEvent> coopGnatPersonal=new CoopPersonalDelivery<PersonalEvent>();
        private long coopBiteSequence,coopReceivedRevision,coopWorldRevision;private int bitesSinceSample;
        internal int WorldUses;
        internal int Bites { get { return bitesSinceSample; } }
        internal int Culls { get { return session.Bounty.Culls; } }
        internal bool AcceptWorld=true;
        internal void Ready(bool value) { session.IsReady=session.IsWorldReady=value; }
        private bool SwingFan(CharacterMainControl player,out string said) { said="fan";WorldUses++;return AcceptWorld; }
        private bool DeployZapper(CharacterMainControl player,out string said) { said="zapper";WorldUses++;return AcceptWorld; }
        internal void TickUse() { TickCoopUse(); }
        internal void Receive(BossRushCoopMessage message) { ReceiveCoopUse(message); }
        internal bool ApplyPacket(SkyIslandGnats host,int index) { return ApplyCoopPersonal(host.coopGnatPersonal.Sent[index].Payload); }
        internal int Packets { get { return coopGnatPersonal.Sent.Count; } }
        internal string Destination(int index) { return coopGnatPersonal.Sent[index].Player; }
        internal void Bite(CharacterMainControl player) { ReportCoopBite(player); }
        internal void Kill(string key,CharacterMainControl player) { OnCoopGnatKilled(key,new DamageInfo { fromCharacter=player }); }
    }
}
