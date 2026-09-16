using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Quests;
using Duckov.Utilities;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 把天空岛的跨局主线投影成官方 Quest / Task：给予者的官方任务页负责接取、任务日志负责展示、
    /// 官方「完成任务」按钮负责交付。剧情事实仍以 BossRush_SkyIsland_Story_v1 为权威。
    ///
    /// 任务表在 <see cref="SkyIslandOfficialQuestTable"/>（岛上三条）与 <see cref="SkyIslandPreludeFlow"/>（Jeff 序章）；
    /// 本桥只做注册、投影同步、Harmony 接线与存档过滤，按条目各自 fail-closed（一条 ID 冲突不拖累其它条）。
    ///
    /// 官方 QuestManager 会把运行时 Quest prefab 的 ID 写进 "Quest"/"Data"；Mod 卸载后该 prefab
    /// 不存在，官方加载会报「未找到Quest」，completedQuests 里的残留还会把 IsQuestAvaliable 永久钉死。
    /// 本桥在 GenerateSaveData / SetupSaveData 的快照中只过滤自己拥有的 ID（active / history / completed / everInspected 四类），
    /// 下一次加载再根据 Mod 故事事实重建进行中 / 已完成投影，因此既复用官方 UI 与事件，也不留下孤儿 ID。
    ///
    /// 已接受的副作用：已交付任务的 history 投影缺失时用 ForceComplete 重建，会再发一次 Quest.onQuestCompleted
    /// （官方 AchievementManager 查不到 Quest_59xxxx 直接返回，BDSManager 只上报一条匿名遥测），每次加载每条至多一次。
    /// </summary>
    internal sealed class SkyIslandOfficialQuestBridge : IDisposable
    {
        private const float TickInterval = 0.25f;

        private static readonly FieldInfo QuestIdField = AccessTools.Field(typeof(Quest), "id");
        private static readonly FieldInfo QuestGiverField = AccessTools.Field(typeof(Quest), "questGiverID");
        private static readonly FieldInfo TaskIdField = AccessTools.Field(typeof(Duckov.Quests.Task), "id");
        private static readonly FieldInfo TaskMasterField = AccessTools.Field(typeof(Duckov.Quests.Task), "master");
        private static readonly FieldInfo CompletedQuestsField = AccessTools.Field(typeof(QuestManager), "completedQuests");

        private static SkyIslandOfficialQuestBridge active;

        private sealed class Entry
        {
            internal SkyIslandOfficialQuestDefinition Def;
            internal Quest Prefab;
            internal GameObject PrefabRoot;
            internal bool Blocked, CollisionReported;
            internal QuestManager CleanedFreshManager;
            internal int CleanedFreshSlot = int.MinValue;
            internal float NextRegistrationAttempt;
            internal int LastFlags = int.MinValue;
        }

        private readonly ModBehaviour host;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<int, Entry> byId = new Dictionary<int, Entry>();
        private bool disposed, subscribed, wasOnIsland;
        private float nextTick;
        private int lastSlot = int.MinValue;

        internal SkyIslandOfficialQuestBridge(ModBehaviour owner)
        {
            host = owner;
            active = this;
            Subscribe();
            SkyIslandOfficialQuestTable.InjectLocalizations();
            foreach (SkyIslandOfficialQuestDefinition definition in SkyIslandOfficialQuestTable.Island) Register(definition);
        }

        /// <summary>登记一条定义并立刻尝试注册官方模板。同 ID 重复登记忽略。</summary>
        internal void Register(SkyIslandOfficialQuestDefinition definition)
        {
            if (disposed || definition == null || byId.ContainsKey(definition.QuestId)) return;
            var entry = new Entry { Def = definition };
            entries.Add(entry);
            byId[definition.QuestId] = entry;
            EnsureRegistered(entry);
        }

        /// <summary>撤掉一条定义：只在确实拥有模板时清自己的投影、移出官方集合、销毁模板。</summary>
        internal void Unregister(int questId)
        {
            Entry entry;
            if (!byId.TryGetValue(questId, out entry)) return;
            Release(entry, Owns(entry));
            entries.Remove(entry);
            byId.Remove(questId);
        }

        private static bool Owns(Entry entry)
        {
            return entry != null && entry.Prefab != null && entry.Prefab.gameObject != null &&
                entry.Prefab.ID == entry.Def.QuestId && entry.Prefab.gameObject.name == entry.Def.ObjectName &&
                entry.Prefab.GetComponent<SkyIslandOfficialQuestTask>() != null;
        }

        /// <summary>所有权判据：先按 ID 查注册表，再验那条 ID 的模板确实是本 Mod 的（对象名 + 专用 Task 组件）。</summary>
        private static bool OwnsRegisteredQuest(int id)
        {
            Entry entry;
            return active != null && active.byId.TryGetValue(id, out entry) && Owns(entry);
        }

        internal static bool IsOwnQuest(int id) { return OwnsRegisteredQuest(id); }

        private static bool TryGetOwned(int id, out Entry entry)
        {
            entry = null;
            return active != null && active.byId.TryGetValue(id, out entry) && Owns(entry);
        }

        internal static bool CanOffer(int id)
        {
            Entry entry;
            if (!TryGetOwned(id, out entry)) return false;
            return SkyIslandOfficialQuestTable.CanOffer(entry.Def, SkyIslandOfficialQuestStory.Capture(active.host));
        }

        /// <summary>官方完成按钮按下：先把 Mod 的交付事实落下，成功才放行官方把任务挪进 history。</summary>
        internal static bool TryCommitDelivery(int id, out string reason)
        {
            Entry entry;
            if (!TryGetOwned(id, out entry))
            {
                reason = L10n.T("晴岚任务尚未就绪。", "The Qinglan quest is not ready yet.");
                return false;
            }
            if (entry.Def.Deliver != null) return entry.Def.Deliver(out reason);
            return active.DefaultCommit(entry, entry.Def.DeliveredFlag, entry.Def.DeliverAction, out reason);
        }

        internal static bool IsTaskDone(int questId, int taskId)
        {
            Entry entry;
            SkyIslandOfficialQuestTaskDefinition task;
            if (!TryGetTask(questId, taskId, out entry, out task)) return false;
            SkyIslandStoryData data = CurrentData();
            return data != null && task.Done(data);
        }

        internal static string DescribeTask(int questId, int taskId)
        {
            Entry entry;
            SkyIslandOfficialQuestTaskDefinition task;
            if (!TryGetTask(questId, taskId, out entry, out task)) return string.Empty;
            SkyIslandStoryData data = CurrentData();
            return data == null ? string.Empty : task.Description(data) ?? string.Empty;
        }

        internal static string TaskHint(int questId, int taskId)
        {
            Entry entry;
            SkyIslandOfficialQuestTaskDefinition task;
            if (!TryGetTask(questId, taskId, out entry, out task) || task.ExtraHint == null) return null;
            SkyIslandStoryData data = CurrentData();
            return data == null ? null : task.ExtraHint(data);
        }

        private static bool TryGetTask(int questId, int taskId, out Entry entry, out SkyIslandOfficialQuestTaskDefinition task)
        {
            task = null;
            entry = null;
            if (active == null || !active.byId.TryGetValue(questId, out entry) || entry.Def.Tasks == null) return false;
            for (int i = 0; i < entry.Def.Tasks.Length; i++)
                if (entry.Def.Tasks[i].TaskId == taskId) { task = entry.Def.Tasks[i]; return true; }
            return false;
        }

        private static SkyIslandStoryData CurrentData()
        {
            bool onIsland;
            SkyIslandStoryService story = active == null ? null : SkyIslandOfficialQuestStory.Resolve(active.host, out onIsland);
            return story == null ? null : story.Current;
        }

        internal void Tick()
        {
            if (disposed || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + TickInterval;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.Prefab == null && !entry.Blocked && Time.unscaledTime >= entry.NextRegistrationAttempt)
                {
                    entry.NextRegistrationAttempt = Time.unscaledTime + 1f;
                    EnsureRegistered(entry);
                }
            }
            QuestManager manager = QuestManager.Instance;
            if (manager == null) return;
            SkyIslandOfficialQuestContext context = SkyIslandOfficialQuestStory.Capture(host);
            if (wasOnIsland && !context.OnIsland) SkyIslandOfficialQuestGivers.ClearSessionState();
            wasOnIsland = context.OnIsland;
            // 故事暂缺（离岛的一两拍、换槽途中）不清也不建：这时清掉的投影会在故事回来后再建一遍，白发一轮事件。
            if (!context.StoryReady || context.Data == null) return;
            // 先按新槽整清、再按新槽重建：旧槽的 history 不能留到新槽里。
            bool slotChanged = context.Slot != lastSlot;
            lastSlot = context.Slot;
            bool markersDirty = slotChanged;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (!Owns(entry)) continue;
                if (slotChanged) { ClearProjection(entry, manager); entry.CleanedFreshManager = null; }
                Synchronize(entry, manager, ref context);
                RefreshActiveTask(entry, manager);
                if (entry.LastFlags != context.Data.flags) { entry.LastFlags = context.Data.flags; markersDirty = true; }
            }
            if (context.OnIsland)
            {
                SkyIslandSession session = host == null ? null : host.GetComponent<SkyIslandSession>();
                if (session != null) SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
            }
            if (markersDirty) SkyIslandOfficialQuestGivers.RefreshMarkers();
        }

        /// <summary>找到一位官方给予者（基地的 Jeff）后：补注册、同步投影、刷新它的头顶标记。</summary>
        internal void PrepareGiver(QuestGiver giver)
        {
            if (disposed || giver == null) return;
            for (int i = 0; i < entries.Count; i++) EnsureRegistered(entries[i]);
            QuestManager manager = QuestManager.Instance;
            SkyIslandOfficialQuestContext context = SkyIslandOfficialQuestStory.Capture(host);
            if (manager != null && context.StoryReady && context.Data != null)
                for (int i = 0; i < entries.Count; i++)
                    if (Owns(entries[i])) Synchronize(entries[i], manager, ref context);
            SkyIslandOfficialQuestGivers.RefreshMarker(giver);
        }

        /// <summary>事实刚改变：立刻刷 Task（官方会 Push「目标完成」通知）并刷头顶标记，不等下一拍。</summary>
        internal void NotifyProgressChanged()
        {
            if (disposed) return;
            QuestManager manager = QuestManager.Instance;
            if (manager == null) return;
            for (int i = 0; i < entries.Count; i++)
                if (Owns(entries[i])) RefreshActiveTask(entries[i], manager);
            SkyIslandOfficialQuestGivers.RefreshMarkers();
        }

        private void Subscribe()
        {
            if (subscribed) return;
            Quest.onQuestActivated += OnQuestActivated;
            Quest.onQuestCompleted += OnQuestCompleted;
            subscribed = true;
        }

        private void EnsureRegistered(Entry entry)
        {
            if (disposed || entry.Prefab != null || entry.Blocked) return;
            GameObject root = null;
            try
            {
                QuestCollection collection = GameplayDataSettings.QuestCollection;
                if (collection == null) return;
                Quest existing = collection.Get(entry.Def.QuestId);
                if (existing != null)
                {
                    if (existing.gameObject != null && existing.gameObject.name == entry.Def.ObjectName &&
                        existing.GetComponent<SkyIslandOfficialQuestTask>() != null)
                    {
                        entry.Prefab = existing;
                        entry.PrefabRoot = existing.gameObject;
                        entry.PrefabRoot.SetActive(true);
                        return;
                    }
                    if (!entry.CollisionReported)
                    {
                        entry.CollisionReported = true;
                        ModBehaviour.CriticalLog("sky-island-quest-id-collision-" + entry.Def.QuestId,
                            "[SkyIslandQuest] Quest ID " + entry.Def.QuestId + " 已被其它内容占用，这条晴岚任务不会注入官方任务页。");
                    }
                    entry.Blocked = true;
                    return;
                }
                if (QuestIdField == null || QuestGiverField == null || TaskIdField == null || TaskMasterField == null)
                    throw new MissingFieldException("官方 Quest/Task 私有字段签名已变化");
                if (entry.Def.Tasks == null || entry.Def.Tasks.Length == 0)
                    throw new InvalidOperationException("任务定义没有目标");

                root = new GameObject(entry.Def.ObjectName);
                root.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(root);
                Quest quest = root.AddComponent<Quest>();
                QuestIdField.SetValue(quest, entry.Def.QuestId);
                QuestGiverField.SetValue(quest, (QuestGiverID)entry.Def.GiverId);
                quest.DisplayNameRaw = entry.Def.NameKey;
                quest.DescriptionRaw = entry.Def.DescriptionKey;
                // 多个目标挂在同一个模板对象上：官方 Instantiate 整棵克隆，Task.Master 才会指向克隆后的 Quest。
                for (int i = 0; i < entry.Def.Tasks.Length; i++)
                {
                    var task = root.AddComponent<SkyIslandOfficialQuestTask>();
                    task.questId = entry.Def.QuestId;
                    task.taskId = entry.Def.Tasks[i].TaskId;
                    TaskIdField.SetValue(task, entry.Def.Tasks[i].TaskId);
                    TaskMasterField.SetValue(task, quest);
                    quest.Tasks.Add(task);
                }
                collection.Add(quest);
                entry.Prefab = quest;
                entry.PrefabRoot = root;
                ModBehaviour.DevLog("[SkyIslandQuest] 官方任务已注册: id=" + entry.Def.QuestId + " giver=" + entry.Def.GiverId);
            }
            catch (Exception e)
            {
                entry.Blocked = true;
                if (root != null) UnityEngine.Object.Destroy(root);
                entry.PrefabRoot = null;
                entry.Prefab = null;
                ModBehaviour.CriticalLog("sky-island-official-quest-register-" + entry.Def.QuestId,
                    "[SkyIslandQuest] [ERROR] 官方任务注册失败，本轮隐藏这条晴岚任务: " + e);
            }
        }

        private void Synchronize(Entry entry, QuestManager manager, ref SkyIslandOfficialQuestContext context)
        {
            int id = entry.Def.QuestId;
            SkyIslandStoryData data = context.Data;
            Quest activeQuest = Find(manager.ActiveQuests, id);
            Quest historyQuest = Find(manager.HistoryQuests, id);

            if (data.Has(entry.Def.DeliveredFlag))
            {
                entry.CleanedFreshManager = null;
                if (historyQuest != null)
                {
                    RemoveAllQuests(manager.ActiveQuests, id, null);
                    RemoveAllQuests(manager.HistoryQuests, id, historyQuest);
                    return;
                }
                if (activeQuest == null) activeQuest = ActivateProjectedQuest(entry, manager);
                else RemoveAllQuests(manager.ActiveQuests, id, activeQuest);
                if (activeQuest != null && !activeQuest.Complete) activeQuest.ForceComplete();
                return;
            }

            if (data.Has(entry.Def.AcceptedFlag))
            {
                entry.CleanedFreshManager = null;
                if (historyQuest != null)
                {
                    RemoveAllQuests(manager.HistoryQuests, id, null);
                    RemoveCompletedId(manager, id);
                }
                if (activeQuest == null) ActivateProjectedQuest(entry, manager);
                else RemoveAllQuests(manager.ActiveQuests, id, activeQuest);
                return;
            }

            // Mod 故事是权威。若旧实验版本或外部修改留下了同 ID 的运行时对象，空白故事槽要把它收掉。
            if (ReferenceEquals(entry.CleanedFreshManager, manager) && entry.CleanedFreshSlot == context.Slot &&
                activeQuest == null && historyQuest == null) return;
            ClearProjection(entry, manager);
            entry.CleanedFreshManager = manager;
            entry.CleanedFreshSlot = context.Slot;
        }

        private static void ClearProjection(Entry entry, QuestManager manager)
        {
            int id = entry.Def.QuestId;
            RemoveAllQuests(manager.ActiveQuests, id, null);
            RemoveAllQuests(manager.HistoryQuests, id, null);
            RemoveCompletedId(manager, id);
            manager.EverInspectedQuest.Remove(id);
        }

        private static void RefreshActiveTask(Entry entry, QuestManager manager)
        {
            Quest quest = Find(manager.ActiveQuests, entry.Def.QuestId);
            if (quest == null) return;
            foreach (Duckov.Quests.Task task in quest.Tasks)
            {
                var own = task as SkyIslandOfficialQuestTask;
                if (own != null) own.RefreshFromStory();
            }
        }

        private void OnQuestActivated(Quest quest)
        {
            Entry entry;
            if (disposed || quest == null || !TryGetOwned(quest.ID, out entry)) return;
            SkyIslandStoryData data = CurrentData();
            if (data == null || data.Has(entry.Def.AcceptedFlag) || data.Has(entry.Def.DeliveredFlag)) return;
            string message;
            bool accepted = entry.Def.Accept != null
                ? entry.Def.Accept(out message)
                : DefaultCommit(entry, entry.Def.AcceptedFlag, entry.Def.AcceptAction, out message);
            if (!accepted)
                ModBehaviour.DevLog("[SkyIslandQuest] [WARNING] 官方接取已发生，Mod 事实等待重试: " + message);
        }

        private void OnQuestCompleted(Quest quest)
        {
            Entry entry;
            if (disposed || quest == null || !TryGetOwned(quest.ID, out entry)) return;
            SkyIslandStoryData data = CurrentData();
            if (data != null && !data.Has(entry.Def.DeliveredFlag))
                ModBehaviour.DevLog("[SkyIslandQuest] [WARNING] 官方任务已完成，但交付事实尚未提交；下次同步会继续重试: " + quest.ID);
        }

        /// <summary>默认的接取 / 交付：只写对应旗标；已经写过就算成功（幂等）。</summary>
        private bool DefaultCommit(Entry entry, SkyIslandStoryFlag flag, SkyIslandStoryAction action, out string message)
        {
            bool onIsland;
            SkyIslandStoryService story = SkyIslandOfficialQuestStory.Resolve(host, out onIsland);
            if (story == null || story.Current == null)
            {
                message = L10n.T("晴岚任务尚未就绪。", "The Qinglan quest is not ready yet.");
                return false;
            }
            if (story.Current.Has(flag)) { message = null; return true; }
            if (!story.CanWrite) { message = story.SaveStatus; return false; }
            if (!story.TryApply(action, out message)) return false;
            story.Tick(true);
            if (host != null && !string.IsNullOrEmpty(message)) host.ShowMessage(message);
            message = null;
            return true;
        }

        private static Quest Find(IList<Quest> quests, int id)
        {
            if (quests == null) return null;
            for (int i = 0; i < quests.Count; i++)
                if (quests[i] != null && quests[i].ID == id) return quests[i];
            return null;
        }

        private static Quest ActivateProjectedQuest(Entry entry, QuestManager manager)
        {
            if (manager == null || !Owns(entry)) return null;
            manager.ActivateQuest(entry.Def.QuestId, (QuestGiverID)entry.Def.GiverId);
            return Find(manager.ActiveQuests, entry.Def.QuestId);
        }

        private static void RemoveAllQuests(IList<Quest> list, int id, Quest keep)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Quest quest = list[i];
                if (quest == null || quest.ID != id || ReferenceEquals(quest, keep)) continue;
                list.RemoveAt(i);
                if (quest.gameObject != null) UnityEngine.Object.Destroy(quest.gameObject);
            }
        }

        private static void RemoveCompletedId(QuestManager manager, int id)
        {
            if (manager == null || CompletedQuestsField == null) return;
            var values = CompletedQuestsField.GetValue(manager) as List<int>;
            if (values != null) while (values.Remove(id)) { }
        }

        internal static object FilterSaveSnapshot(object snapshot)
        {
            // 只剥离本桥确实拥有的 ID。若其它 Mod 抢先注册了同 ID，那一条注册会 fail closed，
            // 这里也必须完全放行它，不能仅凭整数 ID 删除对方的任务进度。
            if (active == null || !(snapshot is QuestManager.SaveData)) return snapshot;
            QuestManager.SaveData data = (QuestManager.SaveData)snapshot;
            for (int i = 0; i < active.entries.Count; i++)
            {
                Entry entry = active.entries[i];
                if (!OwnsRegisteredQuest(entry.Def.QuestId)) continue;
                int id = entry.Def.QuestId;
                RemoveSavedQuest(data.activeQuestsData, id);
                RemoveSavedQuest(data.historyQuestsData, id);
                if (data.completedQuests != null) while (data.completedQuests.Remove(id)) { }
                if (data.everInspectedQuest != null) while (data.everInspectedQuest.Remove(id)) { }
            }
            return data;
        }

        private static void RemoveSavedQuest(List<object> values, int id)
        {
            if (values == null) return;
            for (int i = values.Count - 1; i >= 0; i--)
            {
                object value = values[i];
                if (value is Quest.SaveData && ((Quest.SaveData)value).id == id) values.RemoveAt(i);
            }
        }

        private static void Release(Entry entry, bool ownsQuest)
        {
            QuestManager manager = QuestManager.Instance;
            if (ownsQuest && manager != null) ClearProjection(entry, manager);
            try
            {
                QuestCollection collection = GameplayDataSettings.QuestCollection;
                if (ownsQuest && collection != null && entry.Prefab != null) collection.Remove(entry.Prefab);
            }
            catch (Exception) { }
            if (ownsQuest && entry.PrefabRoot != null) UnityEngine.Object.Destroy(entry.PrefabRoot);
            entry.PrefabRoot = null;
            entry.Prefab = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            // 先冻结每条的所有权，再退订、再清：销毁途中模板引用变化不能让我们误清冲突 Mod 的同 ID 任务。
            var owned = new bool[entries.Count];
            for (int i = 0; i < entries.Count; i++) owned[i] = Owns(entries[i]);
            disposed = true;
            if (subscribed)
            {
                Quest.onQuestActivated -= OnQuestActivated;
                Quest.onQuestCompleted -= OnQuestCompleted;
                subscribed = false;
            }
            for (int i = 0; i < entries.Count; i++) Release(entries[i], owned[i]);
            entries.Clear();
            byId.Clear();
            if (ReferenceEquals(active, this)) active = null;
        }
    }

    /// <summary>
    /// 官方 Task 的通用实现：只带两个整数回查任务表（委托不随 Instantiate 克隆，静态引用会串），判据与文案都读 Mod 事实。
    /// </summary>
    public sealed class SkyIslandOfficialQuestTask : Duckov.Quests.Task
    {
        public int questId;
        public int taskId;
        private bool initialized, lastKnown;

        public override string Description
        {
            get { return SkyIslandOfficialQuestBridge.DescribeTask(questId, taskId); }
        }

        public override string[] ExtraDescriptsions
        {
            get
            {
                string hint = SkyIslandOfficialQuestBridge.TaskHint(questId, taskId);
                return string.IsNullOrEmpty(hint) ? new string[0] : new[] { hint };
            }
        }

        protected override bool CheckFinished() { return SkyIslandOfficialQuestBridge.IsTaskDone(questId, taskId); }
        protected override void OnInit()
        {
            lastKnown = CheckFinished();
            initialized = true;
        }
        public override object GenerateSaveData() { return CheckFinished(); }
        public override void SetupSaveData(object data)
        {
            lastKnown = CheckFinished();
            initialized = true;
        }

        internal void RefreshFromStory()
        {
            bool current = CheckFinished();
            // Task.Init 在“克隆时已经完成”时不会调用 OnInit。先无声采样，避免读档重建时重复播报完成。
            if (!initialized)
            {
                initialized = true;
                lastKnown = current;
                enabled = !current;
                return;
            }
            if (current == lastKnown) return;
            lastKnown = current;
            ReportStatusChanged();
            // 换槽或故事恢复可能让同一运行时投影从“已完成目标”回到“进行中”；两边都显式同步组件状态。
            enabled = !current;
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.MeetsPrerequisit))]
    internal static class SkyIslandOfficialQuestAvailabilityPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Quest __instance, ref bool __result)
        {
            if (__instance == null || !SkyIslandOfficialQuestBridge.IsOwnQuest(__instance.ID)) return true;
            __result = SkyIslandOfficialQuestBridge.CanOffer(__instance.ID);
            return false;
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.TryComplete))]
    internal static class SkyIslandOfficialQuestCompletePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Quest __instance, ref bool __result)
        {
            if (__instance == null || !SkyIslandOfficialQuestBridge.IsOwnQuest(__instance.ID)) return true;
            if (!__instance.AreTasksFinished()) return true;
            string reason;
            if (SkyIslandOfficialQuestBridge.TryCommitDelivery(__instance.ID, out reason)) return true;
            __result = false;
            if (!string.IsNullOrEmpty(reason) && ModBehaviour.Instance != null) ModBehaviour.Instance.ShowMessage(reason);
            return false;
        }
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.GenerateSaveData))]
    internal static class SkyIslandOfficialQuestSaveFilterPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref object __result)
        {
            __result = SkyIslandOfficialQuestBridge.FilterSaveSnapshot(__result);
        }
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.SetupSaveData), new Type[] { typeof(object) })]
    internal static class SkyIslandOfficialQuestLoadFilterPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ref object dataObj)
        {
            dataObj = SkyIslandOfficialQuestBridge.FilterSaveSnapshot(dataObj);
        }
    }
}
