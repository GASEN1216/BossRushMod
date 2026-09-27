using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BossRush;

namespace UnityEngine
{
    public class Object
    {
        internal bool Dead;
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a, b) || (ReferenceEquals(a, null) || a.Dead) && (ReferenceEquals(b, null) || b.Dead); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            GameObject go = value as GameObject;
            if (go != null)
            {
                foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (Component component in go.Components) component.Dead = true;
                go.transform.SetParent(null, false);
            }
            value.Dead = true;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public class Transform : Component
    {
        public Transform parent;
        public List<Transform> Children = new List<Transform>();
        public void SetParent(Transform value, bool world)
        {
            if (parent != null) parent.Children.Remove(this);
            parent = value;
            if (parent != null) parent.Children.Add(this);
        }
    }
    public enum HideFlags { HideAndDontSave }
    public class GameObject : Object
    {
        public string name;
        public bool activeSelf = true;
        public HideFlags hideFlags;
        public readonly Transform transform;
        public readonly List<Component> Components = new List<Component>();
        public GameObject(string name) { this.name = name; transform = new Transform { gameObject = this }; Components.Add(transform); }
        public T AddComponent<T>() where T : Component, new() { T c = new T { gameObject = this }; Components.Add(c); return c; }
        public T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(c => c != null); }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public struct Color { public Color(float r, float g, float b, float a) { } }
    public static class Time { public static float unscaledTime; }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string method) { } public HarmonyPatch(Type type, string method, Type[] args) { } }
    public class HarmonyPrefix : Attribute { }
    public class HarmonyPostfix : Attribute { }
    public static class AccessTools
    {
        public static FieldInfo Field(Type t, string name)
        {
            while (t != null) { FieldInfo field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); if (field != null) return field; t = t.BaseType; }
            return null;
        }
    }
}
namespace Duckov.Quests
{
    // Adapters follow the official source's order: add active -> InitTasks -> Activated -> list event;
    // Complete getter consults completed/history, and Complete setter notifies the manager.
    public enum QuestGiverID { Jeff = 1 }
    public class Task : UnityEngine.MonoBehaviour
    {
        private int id;
        private Quest master;
        public int ID { get { return id; } }
        public Quest Master { get { return master; } }
        public virtual string Description { get { return ""; } }
        public virtual string[] ExtraDescriptsions { get { return new string[0]; } }
        protected virtual bool CheckFinished() { return false; }
        public bool IsFinished() { return CheckFinished(); }
        protected virtual void OnInit() { }
        public void Init() { if (IsFinished()) enabled = false; else OnInit(); }
        public virtual object GenerateSaveData() { return null; }
        public virtual void SetupSaveData(object data) { }
        public int Notifications;
        protected void ReportStatusChanged() { if (IsFinished()) Notifications++; }
    }
    public class Reward : UnityEngine.MonoBehaviour
    {
        private int id;
        private Quest master;
        public virtual bool Claimed { get { return false; } }
        public virtual bool AutoClaim { get { return false; } }
        public virtual string Description { get { return ""; } }
        public virtual UnityEngine.Sprite Icon { get { return null; } }
        public int RewardId { get { return id; } }
        public virtual void OnClaim() { }
        public virtual object GenerateSaveData() { return null; }
        public virtual void SetupSaveData(object data) { }
    }
    public class Quest : UnityEngine.MonoBehaviour
    {
        private int id, requiredItemID, requiredItemCount;
        private QuestGiverID questGiverID;
        private List<Reward> rewards = new List<Reward>();
        private bool complete;
        public int ID { get { return id; } }
        public QuestGiverID QuestGiverID { get { return questGiverID; } }
        public string DisplayNameRaw, DescriptionRaw;
        public List<Task> Tasks = new List<Task>();
        public IList<Reward> Rewards { get { return rewards; } }
        public bool Complete { get { return complete || (QuestManager.Instance != null && QuestManager.Instance.IsFinished(id)); } }
        public static event Action<Quest> onQuestActivated, onQuestCompleted;
        public bool AreTasksFinished() { return Tasks.All(t => t.IsFinished()); }
        public bool MeetsPrerequisit()
        {
            bool result = false;
            if (InvokePatch(typeof(OfficialQuestAvailabilityPatch), this, ref result)) return false; // no relation node in adapter
            return result;
        }
        public bool TryComplete()
        {
            bool result = false;
            if (!InvokePatch(typeof(OfficialQuestCompletePatch), this, ref result)) return result;
            if (Complete || !AreTasksFinished()) return false;
            ForceComplete(); return true;
        }
        private static bool InvokePatch(Type type, Quest quest, ref bool result)
        {
            object[] args = { quest, result };
            bool run = (bool)type.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
            result = (bool)args[1]; return run;
        }
        public void ForceComplete() { complete = true; if (onQuestCompleted != null) onQuestCompleted(this); }
        public Quest Clone(QuestGiverID giver)
        {
            var root = new UnityEngine.GameObject(gameObject.name + "(Clone)"); root.SetActive(gameObject.activeSelf);
            var copy = root.AddComponent<Quest>(); copy.id = id; copy.questGiverID = giver;
            copy.DisplayNameRaw = DisplayNameRaw; copy.DescriptionRaw = DescriptionRaw;
            foreach (OfficialQuestProjectionTask source in Tasks)
            {
                var task = root.AddComponent<OfficialQuestProjectionTask>(); task.questId = source.questId; task.taskId = source.taskId;
                HarmonyLib.AccessTools.Field(typeof(Task), "id").SetValue(task, source.ID);
                HarmonyLib.AccessTools.Field(typeof(Task), "master").SetValue(task, copy);
                copy.Tasks.Add(task);
            }
            // Mirrors Unity Instantiate: serialized fields of every reward row are copied onto the clone.
            foreach (Reward source in rewards)
            {
                Reward row;
                var money = source as OfficialQuestProjectionReward;
                var item = source as OfficialQuestProjectionItemReward;
                if (money != null) { var r = root.AddComponent<OfficialQuestProjectionReward>(); r.questId = money.questId; r.amount = money.amount; row = r; }
                else { var r = root.AddComponent<OfficialQuestProjectionItemReward>(); r.questId = item.questId; r.typeId = item.typeId; r.amount = item.amount; row = r; }
                HarmonyLib.AccessTools.Field(typeof(Reward), "id").SetValue(row, source.RewardId);
                HarmonyLib.AccessTools.Field(typeof(Reward), "master").SetValue(row, copy);
                copy.rewards.Add(row);
            }
            return copy;
        }
        public void Activate() { foreach (Task t in Tasks) t.Init(); if (onQuestActivated != null) onQuestActivated(this); }
        public struct SaveData { public int id; }
    }
    public class QuestCollection : List<Quest> { public Quest Get(int id) { return this.FirstOrDefault(q => q != null && q.ID == id); } }
    public class QuestGiver : UnityEngine.Component { }
    public class QuestManager
    {
        public static QuestManager Instance;
        private List<int> completedQuests = new List<int>();
        private List<Quest> active = new List<Quest>();
        public List<Quest> ActiveQuests { get { active.Sort((a,b) => (b.AreTasksFinished()?1:0)-(a.AreTasksFinished()?1:0)); return active; } }
        public List<Quest> HistoryQuests = new List<Quest>();
        public List<int> EverInspectedQuest = new List<int>();
        public QuestManager() { Instance = this; Quest.onQuestCompleted += Completed; }
        public void Dispose() { Quest.onQuestCompleted -= Completed; foreach (var q in active.Concat(HistoryQuests)) UnityEngine.Object.Destroy(q.gameObject); }
        public bool IsFinished(int id) { return completedQuests.Contains(id) || HistoryQuests.Any(q => q.ID == id); }
        public void ActivateQuest(int id, QuestGiverID giver)
        {
            Quest quest = Duckov.Utilities.GameplayDataSettings.QuestCollection.Get(id).Clone(giver);
            active.Add(quest); quest.Activate();
        }
        private void Completed(Quest quest) { if (active.Remove(quest)) { HistoryQuests.Add(quest); completedQuests.Add(quest.ID); } }
        public object GenerateSaveData()
        {
            return new SaveData { activeQuestsData = active.Select(q => (object)new Quest.SaveData {id=q.ID}).ToList(),
                historyQuestsData = HistoryQuests.Select(q => (object)new Quest.SaveData {id=q.ID}).ToList(),
                completedQuests = completedQuests.ToList(), everInspectedQuest = EverInspectedQuest.ToList() };
        }
        public void SetupSaveData(object data) { }
        public struct SaveData { public List<object> activeQuestsData, historyQuestsData; public List<int> completedQuests, everInspectedQuest; }
    }
}
namespace Duckov.Utilities { public static class GameplayDataSettings { public static Duckov.Quests.QuestCollection QuestCollection = new Duckov.Quests.QuestCollection(); } }
namespace Duckov.UI { public static class NotificationText { public static void Push(string message) { } } }
namespace Saves { public static class SavesSystem { public static int CurrentSlot; } }
public class LevelManager { public static LevelManager Instance = new LevelManager(); public bool IsBaseLevel = true; }
public static class SceneLoader { public static bool IsSceneLoading; }
public class CharacterMainControl { public static CharacterMainControl Main = new CharacterMainControl(); public ItemStatsSystem.Item CharacterItem = new ItemStatsSystem.Item(); }
namespace UnityEngine { public class Sprite { } }
namespace ItemStatsSystem
{
    public struct ItemMetaData { public UnityEngine.Sprite icon; public string DisplayName; }
    public static class ItemAssetsCollection { public static ItemMetaData GetMetaData(int id) { return new ItemMetaData { DisplayName = "item" + id }; } }
    public class Item
    {
        public int TypeID, Count = 1; public bool Reforged, Affix;
        public List<Item> Items = new List<Item>();
        public List<Item> GetAllChildren(bool self, bool recursive) { return Items; }
    }
}
namespace BossRush
{
    internal class CampaignSaveData { public string[] acceptedGuides = new string[0], experiencedGuides = new string[0], completedGuides = new string[0]; }
    // Persistence is intentionally an adapter here. ContentTransactions runs the real slot store and compensation paths.
    internal static class CampaignPersistence
    {
        internal static CampaignSaveData Current = new CampaignSaveData();
        internal static bool HasWriteBarrier, IsStoreFaulted;
        internal static bool IsGuideAccepted(string id) { return Current.acceptedGuides.Contains(id); }
        internal static bool IsGuideExperienced(string id) { return Current.experiencedGuides.Contains(id); }
        internal static bool IsGuideCompleted(string id) { return Current.completedGuides.Contains(id); }
        internal static bool TryAdvanceGuide(string id, int stage)
        {
            if (HasWriteBarrier || IsStoreFaulted) return false;
            if (stage == 1) Current.acceptedGuides = Current.acceptedGuides.Union(new[]{id}).ToArray();
            else if (stage == 2 && IsGuideAccepted(id)) Current.experiencedGuides = Current.experiencedGuides.Union(new[]{id}).ToArray();
            else if (stage == 3 && IsGuideExperienced(id)) Current.completedGuides = Current.completedGuides.Union(new[]{id}).ToArray();
            else return false;
            return true;
        }
    }
    // Official item side of the projection core as an adapter: a backpack of TypeID -> count.
    // The allocation rules (OfficialQuestItemRules) and the transaction order in OfficialQuestProjection are production code.
    internal sealed class OfficialQuestItemReservation : IDisposable
    {
        internal List<OfficialQuestItemStack> Plan; internal bool Committed, Disposed;
        internal void Commit() { if (Committed || Disposed) return; Committed = true; foreach (var p in Plan) OfficialQuestItems.Backpack[p.TypeId] -= p.Count; }
        public void Dispose() { Disposed = true; OfficialQuestItems.Disposed++; }
    }
    // Persistence is exercised with the real coordinator in ContentTransactions.
    internal static class CampaignSaveCoordinator
    {
        internal static bool BeginQuestDelivery(Func<bool> collect, out string reason) { reason = null; return true; }
        internal static void EndQuestDelivery(bool committed) { }
    }
    internal sealed class OfficialQuestRewardReservation : IDisposable
    {
        internal readonly List<OfficialQuestItemStack> Items = new List<OfficialQuestItemStack>();
        private bool committed;
        internal void Commit() { committed = true; }
        public void Dispose()
        {
            if (committed) return;
            foreach (var item in Items)
            {
                OfficialQuestItems.Backpack[item.TypeId] -= item.Count;
                OfficialQuestItems.Given.Remove(item);
            }
        }
    }
    internal static class OfficialQuestItems
    {
        internal static Dictionary<int,int> Backpack = new Dictionary<int,int>();
        internal static List<OfficialQuestItemStack> Given = new List<OfficialQuestItemStack>();
        internal static bool FailCreate; internal static int Created, Discarded, Disposed;
        internal static void Reset() { Backpack.Clear(); Given.Clear(); FailCreate = false; Created = Discarded = Disposed = 0; }
        internal static int HeldInBackpack(int typeId) { int n; return Backpack.TryGetValue(typeId, out n) ? n : 0; }
        internal static string DisplayName(int typeId) { return "item" + typeId; }
        internal static bool TryReserve(OfficialQuestSubmission[] subs, out OfficialQuestItemReservation reservation, out string reason)
        {
            reservation = null; reason = null;
            if (subs == null || subs.Length == 0) return true;
            var plan = new List<OfficialQuestItemStack>(); OfficialQuestSubmission missing;
            if (!OfficialQuestItemRules.TryPlan(subs, HeldInBackpack, plan, out missing)) { reason = "missing"; return false; }
            reservation = new OfficialQuestItemReservation { Plan = plan }; return true;
        }
        internal static bool TryCreate(OfficialQuestItemStack[] rewards, List<ItemStatsSystem.Item> created, out string reason)
        {
            reason = null;
            if (rewards == null) return true;
            if (FailCreate) { reason = "reward missing"; return false; }
            foreach (var r in rewards) { created.Add(new ItemStatsSystem.Item { TypeID = r.TypeId, Count = r.Count }); Created++; }
            return true;
        }
        internal static bool CanCollectAssets(bool inboxOnly) { return true; }
        internal static Func<bool> AssetCollector(bool inboxOnly) { return () => true; }
        internal static bool TryGive(List<ItemStatsSystem.Item> created, bool inboxOnly,
            out OfficialQuestRewardReservation delivered, out string reason)
        {
            delivered = new OfficialQuestRewardReservation(); reason = null;
            foreach (var item in created)
            {
                var stack = new OfficialQuestItemStack(item.TypeID, item.Count);
                Given.Add(stack); delivered.Items.Add(stack);
                int n; Backpack.TryGetValue(item.TypeID, out n); Backpack[item.TypeID] = n + item.Count;
            }
            return true;
        }
        internal static void Discard(List<ItemStatsSystem.Item> created) { Discarded += created.Count; created.Clear(); }
    }
    internal static partial class BossRushItemIds
    {
        public const int BossRushTicket = 500001, ZombieTideInvitation = 500045, PortableSafeZoneDevice = 500058, RelicEgg = 500059, AffixForgeStone = 500060;
        public const int DragonSeed = 500062, EmberSeed = 500063, PhantomSpore = 500064, DragonFruit = 500065, EmberChili = 500066, PhantomMushroom = 500067;
        public const int SkyIslandBrassScrap = 500076;
    }
    internal static class FactionFlagConfig { public const int RANDOM_FLAG_TYPE_ID = 500020; }
    internal static class BloodhuntTransponderConfig { public const int TYPE_ID = 500036; }
    internal static class ColdQuenchFluidConfig { public const int TYPE_ID = 500014; }
    internal static class CampaignProgressService
    {
        internal static int GuideCash;
        internal static bool TryDeliverGuide(string id, int cash) { if (!CampaignPersistence.TryAdvanceGuide(id, 3)) return false; GuideCash += cash; return true; }
        internal static Dictionary<string,CampaignChapterState> States = new Dictionary<string,CampaignChapterState>();
        internal static CampaignChapterState GetState(string id) { CampaignChapterState value; return States.TryGetValue(id,out value)?value:CampaignChapterState.Locked; }
        internal static string GetActiveChapterId() { return States.Where(p=>p.Value==CampaignChapterState.ContractActive || p.Value==CampaignChapterState.ReadyToDeliver).Select(p=>p.Key).FirstOrDefault(); }
        internal static bool TryAcceptContract(string id) { if(GetState(id)!=CampaignChapterState.Available || GetActiveChapterId()!=null)return false; States[id]=CampaignChapterState.ContractActive;return true; }
        internal static bool FailDeliver;
        internal static bool TryDeliver(string id) { if(FailDeliver || GetState(id)!=CampaignChapterState.ReadyToDeliver)return false; States[id]=CampaignChapterState.Completed; return true; }
    }
    internal class CampaignRuntimeModule { internal bool IsBootstrapped = true, IsEnabled = true; }
    internal static class CampaignDialoguePlayer { internal static void PlayChapterDelivered(CampaignChapterDef def) { } }
    internal static class CampaignObjectiveTracker { internal static string ArmedChapterId; internal static List<CampaignObjectiveProgress> Progress = new List<CampaignObjectiveProgress>(); }
    internal static class CampaignFacilityUnlocks
    {
        internal static HashSet<string> Tokens = new HashSet<string>();
        internal static string BuildTokenForChapter(int i) { return CampaignTuning.FacilityTokenPrefix+i; }
        internal static bool IsTokenGranted(string token) { return Tokens.Contains(token); }
    }
    internal static class CampaignAssetCache { internal static object GetChapterPoster(int order) { return null; } }
    internal static class OfficialQuestGiverLocator { internal static void RefreshMarkerFor(int id) { } internal static void RefreshMarker(Duckov.Quests.QuestGiver giver) { } }
    internal class ModBehaviour : UnityEngine.Object
    {
        internal static ModBehaviour Instance = new ModBehaviour();
        internal ModeHRuntimeModule ModeHRuntime = new ModeHRuntimeModule();
        internal RandomEventsRuntimeModule RandomEventsRuntime = new RandomEventsRuntimeModule();
        internal string Mode;
        internal string ResolveCampaignCurrentMode() { return Mode; }
        internal static void DevLog(string message) { }
        internal static void CriticalLog(string key, string message) { }
        internal string LastMessage;
        internal void ShowMessage(string message) { LastMessage = message; }
    }
    internal static class L10n { internal static bool Chinese; internal static string T(string cn, string en) { return Chinese?cn:en; } internal static string T(string key) { return key; } }
    internal static class BossRushUI { internal static bool IsOfficialHudHidden() { return false; } internal static bool IsGamePaused() { return false; } }
    internal static class JsonDataRegistry { internal static bool TryReadDataFile(string folder,string file,out string text) { text=File.ReadAllText(Path.Combine("Assets","Data",folder,file));return true; } }
    internal static class ModeGRuntimeGates { internal static bool IsModeGRunInProgress; }
    internal sealed partial class ModeHRuntimeModule { internal object _runState = new object(); internal ModeHSeasonDto _season = new ModeHSeasonDto(); }
    internal class ModeHSeasonDto { internal List<ModeHMatchReportDto> matchReports = new List<ModeHMatchReportDto>(); }
    internal class ModeHMatchReportDto { internal int reportStatus; }
    internal enum ModeHMatchReportStatus { Unknown=0, SettledPendingArchive=1, Archived=2 }
    internal static class PetNestService { internal static int PetCount; }
    internal static class PetNestCompanionRuntime { internal static bool HasCompanion; }
    internal class RandomEventsRuntimeModule { internal RandomEventDirector Director = new RandomEventDirector(); }
    internal class RandomEventDirector { internal int EventsFiredThisRun; }
    internal static class DailyReportService { internal static bool IsSignedToday; }
    internal static class SkyIslandBossRules { internal static object GearSpec(int id) { return id==500086?new object():null; } }
    internal static class SkyIslandPreludeFlow { internal static bool RouteOpen; internal static bool CanUseRoute(out string reason) { reason=RouteOpen?null:"route closed"; return RouteOpen; } }
    internal static class ReforgeDataPersistence { internal static bool HasReforgeData(ItemStatsSystem.Item item) { return item.Reforged; } }
    internal static class AffixDefinitions { internal const int MaxSlots=3; }
    internal struct AffixSlotView { internal bool IsEmpty; }
    internal static class AffixItemData { internal static bool TryReadSlot(ItemStatsSystem.Item item,int slot,out AffixSlotView value) { value=new AffixSlotView {IsEmpty=!item.Affix};return true; } }
}
