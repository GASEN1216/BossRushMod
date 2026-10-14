using System;
using System.Collections.Generic;
using System.Text;
namespace UnityEngine
{
    internal sealed class GameObject : Object { internal bool Active=true; internal void SetActive(bool value) { Active=value; } }
    internal class Object
    {
        internal bool Destroyed;
        internal static void Destroy(GameObject value) { value.Destroyed=true; }
        public static bool operator ==(Object a,Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b); }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public override bool Equals(object b) { return ReferenceEquals(this,b); }
        public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    }
    internal static class Time { internal static float time; }
    internal struct Vector3 { internal float x; internal static Vector3 one { get { return new Vector3 { x=1 }; } } public static Vector3 operator *(Vector3 value,float scale) { return new Vector3 { x=value.x*scale }; } }
    internal sealed class Transform { internal Vector3 localScale; }
    internal struct Quaternion { }
    internal static class Mathf { internal static int Max(int a,int b) { return Math.Max(a,b); } internal static float Min(float a,float b) {return Math.Min(a,b);} internal static int Clamp(int value, int min, int max) { return Math.Min(max, Math.Max(min, value)); } }
}
namespace Duckov.Utilities
{
    internal enum CustomDataType { None, Float, Int, Bool, String }
    internal sealed class CustomData
    {
        internal string Key; internal CustomDataType DataType; internal bool Display; internal byte[] Raw;
        internal void SetString(string value) { if (DataType != CustomDataType.String) throw new Exception("type frozen"); Raw = Encoding.UTF8.GetBytes(value); }
        internal void SetInt(int value) { if (DataType != CustomDataType.Int) throw new Exception("type frozen"); Raw = BitConverter.GetBytes(value); }
        internal void SetBool(bool value) { if (DataType != CustomDataType.Bool) throw new Exception("type frozen"); Raw = BitConverter.GetBytes(value); }
    }
    internal sealed class Collection : List<CustomData>
    {
        internal void SetRaw(string key, CustomDataType type, byte[] raw, bool create, bool display)
        {
            CustomData entry = Find(variable => variable.Key == key);
            if (entry == null) { entry = new CustomData { Key = key, DataType = type }; Add(entry); }
            if (entry.DataType != type) throw new Exception("type frozen");
            entry.Raw = raw; entry.Display = display;
        }
    }
}
namespace ItemStatsSystem
{
    internal sealed class Item { internal int TypeID = 500001; internal Duckov.Utilities.Collection Variables = new Duckov.Utilities.Collection(); internal float Value = 1f; }
}
namespace BossRush
{
    internal static class BossRushCoop
    {
        internal static string LocalAssetId, LocalPlayerId, RunId;
        internal static bool IsConnected;
        internal static bool IsHost, IsClient, SessionReady;
        internal static int SceneGeneration;
        internal static readonly Dictionary<string, BossRushCoopPlayer> Players = new Dictionary<string, BossRushCoopPlayer>();
        internal static readonly List<BossRushCoopSummonPacket> Sent = new List<BossRushCoopSummonPacket>();
        internal static readonly List<PetNestCoopPacket> PetSent = new List<PetNestCoopPacket>();
        internal static readonly Dictionary<int,CharacterMainControl> Ai=new Dictionary<int,CharacterMainControl>();
        internal static CharacterMainControl FindAi(int id) { CharacterMainControl character;return Ai.TryGetValue(id,out character)?character:null; }
        internal static BossRushCoopPlayer FindPlayer(string id) { BossRushCoopPlayer player; return id != null && Players.TryGetValue(id, out player) ? player : null; }
        internal static string GetPlayerId(CharacterMainControl character) { foreach(var pair in Players) if(pair.Value.Character==character) return pair.Key; return null; }
        internal static void SendToHost(string topic, BossRushCoopSummonPacket packet) { Sent.Add(packet); }
        internal static void SendToHost(string topic, PetNestCoopPacket packet) { PetSent.Add(packet); }
        internal static void SendToPlayer(string player, string topic, BossRushCoopSummonPacket packet) { Sent.Add(packet); }
    }
    internal sealed class BossRushCoopPlayer { internal string Id, AssetId; internal bool IsAlive=true, IsConnected=true; internal CharacterMainControl Character; }
    internal sealed class BossRushCoopMessage { internal string SenderId; internal bool IsFromHost; internal object Packet; internal T Read<T>() where T:class { return BossRushCoopJson.FromJson<T>(BossRushCoopJson.ToJson(Packet)); } }
    internal sealed class Forgettable { internal void Forget() { } }
    internal sealed class SummonStaffConfig { internal const int SummonCount=3; internal float CooldownTime=12f; }
    internal sealed class FrostmourneConfig { internal const int SummonCount=5; internal float CooldownTime=10f; }
    internal static class NewWeaponIds { internal const int SummonStaffTypeId=500001; }
    internal static class FrostmourneIds { internal const int WeaponTypeId=500002; }
    internal partial class BossRushCoopContentRuntimeModule
    {
        private static BossRushCoopContentRuntimeModule instance;
        private string summonAsset;
        private readonly Dictionary<string,BossRushCoopSummonPacket> summonRequests=new Dictionary<string,BossRushCoopSummonPacket>();
        private readonly HashSet<string> acceptedSummons=new HashSet<string>();
        private readonly Dictionary<string,float> summonCooldowns=new Dictionary<string,float>();
        private readonly Dictionary<int,int> localSummons=new Dictionary<int,int>();
        private readonly Dictionary<CharacterMainControl,SummonOwner> summonEntities=new Dictionary<CharacterMainControl,SummonOwner>();
        internal int Executed;
        internal BossRushCoopContentRuntimeModule() { instance=this; }
        private Forgettable ExecuteSummon(CharacterMainControl character,int kind) { Executed++; return new Forgettable(); }
        internal int Pending { get { return summonRequests.Count; } }
        internal void Receive(BossRushCoopMessage message) { OnSummon(message); }
        internal void Ack(BossRushCoopMessage message) { OnSummonAck(message); }
        internal void Count(BossRushCoopMessage message) { OnSummonCount(message); }
    }
    internal sealed class PetNestCompanionHandle { internal bool CleanedUp; internal float ModelScale; internal CharacterMainControl Character; internal PetNestCompanionAgent Agent; internal Health Health; }
    internal sealed class PetNestCompanionAgent { internal CharacterMainControl Master; internal int BindCalls; internal void SetBehaviour(int state) { } internal void Bind(CharacterMainControl character,CharacterMainControl master,bool combat) { Master=master;BindCalls++; } }
    [Serializable] internal sealed class PetNestPetRecord { public string id,lineageKey,backpackJson;public long lastFeedUtcTicks; public int level=1,exp,state; public List<string> talents,scars;internal PetNestPetRecord Clone() {return (PetNestPetRecord)MemberwiseClone();} }
    internal enum PetNestPetState { Downed=1,OnExpedition=2 }
    internal static class PetNestTuning { internal const int PetMaxLevel=30,PetExpPerLevel=100,MaxScarsPerPet=16; internal const float FeedHealRatio=.1f; }
    internal static class PetNestCareService { internal static int Resolve(PetNestPetRecord pet) { return pet.state; } }
    internal static class PetNestService { internal static readonly Dictionary<string,PetNestPetRecord> Pets=new Dictionary<string,PetNestPetRecord>();internal static bool AcceptOutcome=true;internal static int Outcomes; internal static PetNestPetRecord TryGetPet(string id) { PetNestPetRecord pet;return Pets.TryGetValue(id,out pet)?pet:null; } internal static bool ApplyCoopOutcome(string id,int exp,bool downed,string place,string killer) {var pet=TryGetPet(id);if(!AcceptOutcome||pet==null)return false;pet.exp+=exp;Outcomes++;return true;} }
    internal static class PetNestCompanionRuntime { internal static PetNestCompanionHandle Bound; internal static void BindNetworkReplica(PetNestCompanionHandle handle,string id) { Bound=handle; }internal static void CleanupOnce() {Bound=null;} }
    internal static class PetNestModeGate {internal static bool IsCompanionAllowed(ModBehaviour owner,out string reason) {reason=null;return true;}}
    internal static class PetNestCompanionSpawner
    {
        internal static int Attached;
        internal static void RefreshProgression(PetNestCompanionHandle handle,PetNestPetRecord pet) { }
        internal static void CleanupOnce(PetNestCompanionHandle handle) {handle.CleanedUp=true;}
        internal static PetNestCompanionHandle AttachNetworkReplica(CharacterMainControl character,CharacterMainControl master,PetNestPetRecord pet,float scale,bool personal)
        { Attached++;return new PetNestCompanionHandle {Character=character,ModelScale=scale,Agent=new PetNestCompanionAgent {Master=master}}; }
    }
    internal sealed partial class PetNestCoopRuntimeModule
    {
        internal sealed class Entry { internal string Owner,Asset,Projection; internal int Ai,KillExp; internal bool Spawning,Base,Owned,Downed; internal PetNestPetRecord Pet; internal PetNestCompanionHandle Handle; }
        internal readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private readonly Dictionary<string,PetNestCoopPacket> pending=new Dictionary<string,PetNestCoopPacket>();
        private readonly Dictionary<string,long> stateSequences=new Dictionary<string,long>();
        private readonly Dictionary<string,long> requestSequences=new Dictionary<string,long>();
        private readonly HashSet<Health> victims=new HashSet<Health>();
        internal readonly Dictionary<string,PetNestCoopPacket> outstanding=new Dictionary<string,PetNestCoopPacket>();
        private readonly HashSet<string> received=new HashSet<string>();
        internal readonly Dictionary<string,int> killBudgets=new Dictionary<string,int>();
        private string rewardRun;private float nextTick;private ModBehaviour owner;private long nextRequestSequence;
        private static PetNestCoopRuntimeModule instance;private const string RequestTopic="pet.deploy",AckTopic="pet.ack";
        internal int Spawns,Publishes;
        internal PetNestCoopRuntimeModule() {instance=this;rewardRun=BossRushCoop.RunId;}
        private Forgettable Spawn(Entry entry,CharacterMainControl master,string run,int scene) {Spawns++;return new Forgettable();}
        private void Publish(Entry entry) {Publishes++;}
        internal void Request(PetNestCoopPacket packet,string sender) {OnRequest(new BossRushCoopMessage {Packet=packet,SenderId=sender});}
        internal void Release(PetNestCoopPacket packet,string sender) {OnRelease(new BossRushCoopMessage {Packet=packet,SenderId=sender});}
        internal void Award(PetNestCoopPacket packet) {OnAward(new BossRushCoopMessage {Packet=packet,IsFromHost=true});}
        internal void ClearNow() {Clear();}
        internal void State(PetNestCoopPacket packet) { OnState(new BossRushCoopMessage {IsFromHost=true,Packet=packet}); }
        private void Remove(string id,bool broadcast) { Entry entry;if(entries.TryGetValue(id,out entry) && entry.Handle!=null)entry.Handle.CleanedUp=true;entries.Remove(id); }
        internal void Finish(Entry entry) { FinishSpawn(entry); }
    }
    internal static class L10n { internal static string T(string zh,string en) { return en; } }
    internal sealed partial class ModeFRuntimeModule
    {
        internal readonly Dictionary<string,CoopModeFFortificationRequest> coopFortRequests = new Dictionary<string,CoopModeFFortificationRequest>();
        internal bool GiveSucceeds = true;
        internal int GiveCalls, Refunded, Bubbles;
        private bool TryGiveItemToPlayerOrDrop(int item,string name,bool show) { GiveCalls++; if (GiveSucceeds) Refunded++; return GiveSucceeds; }
        private string GetModeFUtilityItemDisplayName(int item) { return "item"; }
        private void ShowModeFRewardBubble(string text) { Bubbles++; }
        internal bool Apply(CoopModeFPersonal receipt) { return ApplyCoopFortificationReceipt(receipt); }
        internal void Cancel() { RefundCancelledCoopFortifications(); }
    }
    internal sealed partial class ModeERuntimeModule
    {
        internal sealed class PersonalItemHost {
            internal bool GiveSucceeds = true; internal int GiveCalls, Refunded, Messages;
            internal bool TryGiveItemToPlayerOrDrop(int item, string name, bool show) { GiveCalls++; if (GiveSucceeds) Refunded++; return GiveSucceeds; }
            internal void ShowMessage(string message) { Messages++; }
        }
        internal readonly PersonalItemHost modeEHost = new PersonalItemHost();
        internal readonly Dictionary<string, CoopModeEUtilityRequest> coopUtilityRequests = new Dictionary<string, CoopModeEUtilityRequest>();
        internal readonly Dictionary<string, CoopModeECoalRequest> coopCoalRequests = new Dictionary<string, CoopModeECoalRequest>();
        private static string GetRespawnItemName(int item) { return "item"; }
        internal bool UtilityReceipt(CoopModeEPersonal receipt) { return ApplyCoopUtilityReceipt(receipt); }
        internal bool CoalReceipt(CoopModeEPersonal receipt) { return ApplyCoopCoalReceipt(receipt); }
        internal void CancelUtilities() { RefundCancelledCoopUtilities(); }
        internal void CancelCoal() { RefundCancelledCoopCoal(); }
        private const int MODE_E_BOSS_HIRE_REFERENCE_PRICE=200;
        internal sealed class ModeEBossHireState { internal int BasePrice; internal CharacterMainControl Owner,Character; }
        internal readonly Dictionary<CharacterMainControl,ModeEBossHireState> modeEHiredBosses = new Dictionary<CharacterMainControl,ModeEBossHireState>();
        internal int Price(ModeEBossHireState offer,CharacterMainControl owner) { return GetCurrentModeEBossHirePrice(offer,owner); }
    }
    internal class ModBehaviour { internal static void DevLog(string message) { throw new Exception(message); } }
    internal static class NewWeaponEquipState { internal static int Dirty; internal static void MarkDirty() { Dirty++; } }
    internal static class AffixRuntimeService { internal static void NotifyItemAffixesChanged(ItemStatsSystem.Item item) { } }
    internal static class AffixDefinitions { internal const int MaxSlots = 3; internal const string KvPrefix = "AFX_"; }
    internal static partial class AffixItemData { internal static void RefreshDisplayNames(ItemStatsSystem.Item item) { } }
    internal static partial class ReforgeDataPersistence
    {
        internal enum PropertyType { Modifier, Stat, Variable }
        internal static void UnmarkRestored(ItemStatsSystem.Item item) { }
        private static void ResetPropertyToPrefabValue(ItemStatsSystem.Item item, string key, PropertyType type) { item.Value = 1f; }
    }
    internal static class CustomItemRuntimeStateHelper
    {
        internal static void RestoreRuntimeState(ItemStatsSystem.Item item, string context)
        {
            var variable = item.Variables.Find(entry => entry.Key == "RF_STAT_Damage");
            if (variable != null) item.Value = 1f + BitConverter.ToSingle(variable.Raw, 0);
        }
    }
}
internal sealed class LevelManager {internal static LevelManager Instance;internal bool IsBaseLevel;}
internal sealed class Health : UnityEngine.Object { internal bool IsDead;internal float MaxHealth=100,CurrentHealth=50;internal void SetHealth(float value) {CurrentHealth=value;} }
internal sealed class CharacterMainControl : UnityEngine.Object { internal static CharacterMainControl Main; internal readonly Health Health = new Health(); internal readonly UnityEngine.GameObject gameObject=new UnityEngine.GameObject(); internal readonly UnityEngine.Transform modelRoot=new UnityEngine.Transform(); internal HoldAgent CurrentHoldItemAgent; }
internal sealed class HoldAgent { internal ItemStatsSystem.Item Item; }
