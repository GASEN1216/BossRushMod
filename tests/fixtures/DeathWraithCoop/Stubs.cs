using System;
using System.Collections.Generic;
using System.Text;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a,Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b); }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public override bool Equals(object b) { return ReferenceEquals(this,b); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c) { x=a;y=b;z=c; } public static Vector3 operator -(Vector3 a,Vector3 b) {return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);} public float sqrMagnitude {get{return x*x+y*y+z*z;}} }
    public sealed class Transform { public Vector3 position; }
    public sealed class GameObject : Object { public string name; public readonly Transform transform=new Transform(); }
    public static class Time { public static float unscaledTime,realtimeSinceStartup; }
}
public enum WraithTier { Weak,Balanced,Strong }
[Serializable] public sealed class CustomFaceSettingData { public int Eye; }
public static class AudioManager { public enum VoiceType { A } public enum FootStepMaterialType { A } }
public sealed class Health : UnityEngine.Object
{
    internal CharacterMainControl Character; public bool showHealthBar;
    public CharacterMainControl TryGetCharacter() { return Character; }
}
public sealed class CharacterMainControl : UnityEngine.Object
{
    internal int Ai; public bool dropBoxOnDead=true;
    public readonly UnityEngine.GameObject gameObject=new UnityEngine.GameObject();
    public UnityEngine.Transform transform { get { return gameObject.transform; } }
    public readonly Health Health=new Health();
    public CharacterMainControl() { Health.Character=this; }
}
namespace ItemStatsSystem.Data { [Serializable] public sealed class ItemTreeData { public int rootInstanceID;public List<Entry> entries=new List<Entry>();[Serializable] public sealed class Entry { public int typeID;public string Variable; } } }
namespace ItemStatsSystem {public sealed class Item {}}
namespace Duckov {public sealed class DeadBodyManager {public sealed class DeathInfo {public bool valid;public uint raidID;public string subSceneID;public UnityEngine.Vector3 worldPosition;}}}
public sealed class InteractableLootbox:UnityEngine.Object {public static InteractableLootbox Prefab;public readonly UnityEngine.Transform transform=new UnityEngine.Transform();}
namespace Duckov.Utilities {public static class GameplayDataSettings {public sealed class Settings {public InteractableLootbox LootBoxPrefab_Tomb;}public static Settings Prefabs;}}
namespace BossRush.Utils { internal static class NPCNameTagHelper { internal static readonly List<UnityEngine.Transform> Named=new List<UnityEngine.Transform>(); internal static void RegisterOriginalHealthBarName(UnityEngine.Transform t,string name,float y,string tag) { Named.Add(t); } } }
namespace Saves
{
    public static class SavesSystem
    {
        public static int CurrentSlot; public static bool IsSaving,FailRead,FailWrite; public static int Reads,Writes;
        private static readonly Dictionary<string,object> data=new Dictionary<string,object>();
        public static bool KeyExisits(string key) { if(FailRead)throw new Exception("read");Reads++;return data.ContainsKey(CurrentSlot+":"+key); }
        public static T Load<T>(string key) { return BossRush.BossRushCoopJson.FromJson<T>(BossRush.BossRushCoopJson.ToJson(data[CurrentSlot+":"+key])); }
        public static void Save<T>(string key,T value) { if(FailWrite)throw new Exception("write");Writes++;data[CurrentSlot+":"+key]=BossRush.BossRushCoopJson.FromJson<T>(BossRush.BossRushCoopJson.ToJson(value)); }
        internal static void Seed(int slot,List<BossRush.WraithInfo> value) { data[slot+":BossRush_DeathWraith_List"]=value; }
        internal static List<BossRush.WraithInfo> At(int slot) { return (List<BossRush.WraithInfo>)data[slot+":BossRush_DeathWraith_List"]; }
    }
}
namespace BossRush
{
    internal static class CoopAiPredictionHealthProtection { internal static readonly List<CharacterMainControl> Marked=new List<CharacterMainControl>(); internal static void MarkReplica(CharacterMainControl character) { Marked.Add(character); } }
    internal abstract class BossRushRuntimeModuleBase { public virtual void OnUpdate(float a,float b) { } }
    internal sealed class BossRushCoopPlayer { internal string Id,AssetId;internal bool IsConnected=true; }
    internal sealed class BossRushCoopMessage { internal string SenderId;internal bool IsFromHost;internal object Value;internal T Read<T>() { return BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(Value)); } }
    internal static class BossRushCoop
    {
        internal static bool IsConnected=true,IsHost=true,SessionReady=true;
        internal static bool HasBoundScene; internal static string SceneToken;
        internal static bool IsClient { get { return !IsHost; } }
        internal static string RunId="room",LocalAssetId="asset-host",LocalPlayerId="host";
        internal static int SceneGeneration=1;
        internal static readonly Dictionary<string,BossRushCoopPlayer> Players=new Dictionary<string,BossRushCoopPlayer>();
        internal static readonly Dictionary<int,CharacterMainControl> Ai=new Dictionary<int,CharacterMainControl>();
        internal static readonly List<object> Sent=new List<object>();
        private sealed class Subscription : IDisposable { public void Dispose() { } }
        internal static IDisposable Register(string topic,Action<BossRushCoopMessage> callback) { return new Subscription(); }
        internal static BossRushCoopPlayer FindPlayer(string id) { BossRushCoopPlayer p;return Players.TryGetValue(id,out p)?p:null; }
        internal static bool SendToHost<T>(string topic,T value) { Sent.Add(value);return true; }
        internal static bool Broadcast<T>(string topic,T value) { Sent.Add(value);return true; }
        internal static int GetAiId(CharacterMainControl c) { return c!=null?c.Ai:0; }
        internal static CharacterMainControl FindAi(int id) { CharacterMainControl c;return Ai.TryGetValue(id,out c)?c:null; }
        internal static bool RequestAiRegistration(CharacterMainControl c) { Ai[c.Ai]=c;return true; }
    }
    internal sealed class CoopPersonalDelivery<T> : IDisposable where T:class
    {
        internal sealed class Packet { internal string Player,Asset,Id;internal T Value; }
        internal readonly List<Packet> Sent=new List<Packet>();
        private readonly HashSet<string> ids=new HashSet<string>();
        internal CoopPersonalDelivery(string topic,Func<T,bool> apply) { }
        internal bool Deliver(string player,string id,T value,string asset=null)
        { if(ids.Add(player+":"+asset+":"+id))Sent.Add(new Packet { Player=player,Asset=asset,Id=id,Value=value });return true; }
        internal void Tick(float time) { }
        public void Dispose() { }
    }
    internal sealed partial class DeathWraithRuntimeModule : BossRushRuntimeModuleBase
    {
        private const string DEATH_WRAITH_LIST_SAVE_KEY="BossRush_DeathWraith_List";
        private List<WraithInfo> _deathWraithListCache;
        private readonly HashSet<uint> _deathWraithPendingRemovals=new HashSet<uint>();
        private readonly List<WraithInfo> _deathWraithPendingAppends=new List<WraithInfo>();
        private bool _deathWraithListDirty;private float _deathWraithListDirtySince,_deathWraithReadRetryAt,_deathWraithSaveRetryAt;
        private int deathWraithSpawnGeneration;
        private readonly List<DeadBodySpawnContext_DeathWraith> pendingDeadBodySpawnContexts=new List<DeadBodySpawnContext_DeathWraith>();
        private readonly HashSet<uint> spawningWraithRaidIds=new HashSet<uint>();
        private readonly Dictionary<uint,CharacterMainControl> activeWraithsByRaidId=new Dictionary<uint,CharacterMainControl>();
        private readonly Dictionary<CharacterMainControl,uint> activeWraithRaidIdByCharacter=new Dictionary<CharacterMainControl,uint>();
        private void ClearPendingDeathWraithInfo_DeathWraith() { }
        internal int SpawnCount,Destroyed;internal string Scene="scene-a";
        private bool IsDeathWraithSystemEnabled() { return true; }
        private string GetActiveSubSceneId_DeathWraith() { return Scene; }
        private static void DevLog(string text) { }
        private int GetStoredDeathWraithLimit_DeathWraith() { return 4; }
        private void DestroyActiveWraithByRaidId_DeathWraith(uint id,string reason) { }
        private void DestroyWraithInstance_DeathWraith(CharacterMainControl c,string reason) { Destroyed++;c.Destroyed=true;c.Health.Destroyed=true;c.gameObject.Destroyed=true; }
        private void DestroyOwnedWraithPresetClone_DeathWraith(CharacterMainControl c) { }
        private WraithTier ClassifyWraithTier_DeathWraith(int value,long wealth) { return WraithTier.Weak; }
        private string GetWraithDisplayName_DeathWraith(string name,WraithTier tier) { return name; }
        private void TrySpawnStoredDeathWraithForRaid_DeathWraith(uint id,UnityEngine.Vector3 pos,CoopWraithSpawn entry=null)
        { if(entry==null){RouteOriginalTomb(id,pos);return;}if(!IsCoopWraithSpawnCurrent(entry))throw new Exception("unowned spawn");SpawnCount++;var actor=new CharacterMainControl { Ai=-SpawnCount };actor.transform.position=pos;RegisterCoopWraith(entry,actor); }
        internal void Start() { InitializeCoopDeathWraith();RefreshCoopWraithWorld(); }
        internal void Receive(string player,string asset,WraithInfo info,UnityEngine.Vector3 tomb,int generation=-1,int[] order=null)
        {
            string json=BossRushCoopJson.ToJson(info);string[] pieces=SplitCoopWraithSnapshot(json);int count=pieces.Length;
            if(order==null) { order=new int[count];for(int i=0;i<count;i++)order[i]=i; }
            foreach(int i in order)
            {
                var part=new CoopWraithPart { Run=BossRushCoop.RunId,Token=BossRushCoop.SceneToken,Generation=generation<0?BossRushCoop.SceneGeneration:generation,
                    Asset=asset,Scene=info.subSceneID,Key=asset+":"+info.raidID+":"+CoopWraithFingerprint(json),Position=tomb,Part=i,Count=count,
                    Data=Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(pieces[i])) };
                ReceiveCoopWraithPart(new BossRushCoopMessage { SenderId=player,Value=part });
            }
        }
        internal int Actors { get { return coopWraithSpawns.Count; } }
        internal CharacterMainControl Actor(string owner,string asset,uint raid)
        { foreach(CoopWraithSpawn entry in coopWraithSpawns.Values)if(entry.Owner==owner&&entry.Asset==asset&&entry.Info.raidID==raid)return entry.Character;return null; }
        internal bool LegacyKillReceipt(string asset,uint raid) { return ApplyCoopWraithClear(new CoopWraithClear {Asset=asset,Raid=raid}); }
        internal void Touch(uint raid) { NotifyOriginalDeadBodyTouched_DeathWraith(new Duckov.DeadBodyManager.DeathInfo {raidID=raid}); }
        internal bool Killed(CharacterMainControl actor) { return HandleCoopWraithDeath(actor.Health); }
        internal string PacketOwner(int index) { return coopWraithClears.Sent[index].Player; }
        internal int Packets { get { return coopWraithClears.Sent.Count; } }
        internal bool ApplyPacket(DeathWraithRuntimeModule host,int index) { return ApplyCoopWraithClear(host.coopWraithClears.Sent[index].Value); }
        internal void SelectSlot(int slot,string asset)
        { Saves.SavesSystem.CurrentSlot=slot;BossRushCoop.LocalAssetId=asset;_deathWraithListCache=null;_deathWraithListDirty=false;_deathWraithReadRetryAt=_deathWraithSaveRetryAt=0; }
        internal void Queue(uint raid,UnityEngine.Vector3 tomb) { QueueCoopDeathWraith(raid,tomb); }
        internal void SendTick() { TickCoopWraithRequests(); }
        internal void Cleanup() { ClearCoopWraithWorld(); }
        internal void ReceiveWorld(DeathWraithRuntimeModule host) { host.PublishCoopWraithWorld();ReceiveCoopWraithWorld(new BossRushCoopMessage { IsFromHost=true,Value=BossRushCoop.Sent[BossRushCoop.Sent.Count-1] });BindCoopWraithReplicas(); }
        internal void BindTick() { BindCoopWraithReplicas(); }
        internal bool CurrentSpawn(uint raid) {foreach(var e in coopWraithSpawns.Values)if(e.Info.raidID==raid)return IsCoopWraithSpawnCurrent(e);return false;}
        internal int Queued { get { return coopWraithPending.Count; } }
        internal void ReceiveSentPart(string player,object value)
        {
            CoopWraithPart part=(CoopWraithPart)value;
            part.Data=Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(part.Data));
            ReceiveCoopWraithPart(new BossRushCoopMessage { SenderId=player,Value=part });
        }
        internal string SnapshotVariable(uint raid)
        { foreach(CoopWraithSpawn entry in coopWraithSpawns.Values)if(entry.Info.raidID==raid)return entry.Info.itemTreeData.entries[0].Variable;return null; }
    }
}

namespace Duckov.Scenes {internal static class MultiSceneCore {internal static object Instance;}}
