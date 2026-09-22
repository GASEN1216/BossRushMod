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
    /// 官方任务投影核心：把 Mod 的跨局剧情投影成官方 Quest / Task——给予者的官方任务页负责接取、任务日志负责展示、
    /// 官方「完成任务」按钮负责交付。Mod 各子系统的存档仍是权威，本核心只做注册、投影同步、Harmony 接线与存档过滤，
    /// 按条目各自 fail-closed（一条 ID 冲突不拖累其它条）。
    ///
    /// 由天空岛桥（2026-09-16）原样抽出：全仓库只允许一个活动实例（静态入口全部经 <see cref="active"/>），
    /// 天空岛与鸭王征程各作为一个 <see cref="IOfficialQuestClient"/> 登记自己的定义。两份实例会互抢静态入口、
    /// 双重装四个 Harmony 补丁、快照过滤跑两遍，所以唯一 owner 是 <see cref="OfficialQuestRuntimeModule"/>。
    ///
    /// 官方 QuestManager 会把运行时 Quest prefab 的 ID 写进 "Quest"/"Data"；Mod 卸载后该 prefab
    /// 不存在，官方加载会报「未找到Quest」，completedQuests 里的残留还会把 IsQuestAvaliable 永久钉死。
    /// 本核心在 GenerateSaveData / SetupSaveData 的快照中只过滤自己拥有的 ID（active / history / completed / everInspected 四类），
    /// 下一次加载再根据各客户端的 Mod 事实重建进行中 / 已完成投影，因此既复用官方 UI 与事件，也不留下孤儿 ID。
    ///
    /// 已接受的副作用：已交付任务的 history 投影缺失时用 ForceComplete 重建，会再发一次 Quest.onQuestCompleted
    /// （官方 AchievementManager 查不到 Quest_59xxxx 直接返回，BDSManager 只上报一条匿名遥测），每次加载每条至多一次。
    /// </summary>
    internal sealed class OfficialQuestProjection : IDisposable
    {
        private const float TickInterval = 0.25f;

        private static readonly FieldInfo QuestIdField = AccessTools.Field(typeof(Quest), "id");
        private static readonly FieldInfo QuestGiverField = AccessTools.Field(typeof(Quest), "questGiverID");
        private static readonly FieldInfo TaskIdField = AccessTools.Field(typeof(Duckov.Quests.Task), "id");
        private static readonly FieldInfo TaskMasterField = AccessTools.Field(typeof(Duckov.Quests.Task), "master");
        private static readonly FieldInfo QuestRequiredItemField = AccessTools.Field(typeof(Quest), "requiredItemID");
        private static readonly FieldInfo QuestRequiredCountField = AccessTools.Field(typeof(Quest), "requiredItemCount");
        private static readonly FieldInfo QuestRewardsField = AccessTools.Field(typeof(Quest), "rewards");
        private static readonly FieldInfo RewardIdField = AccessTools.Field(typeof(Reward), "id");
        private static readonly FieldInfo RewardMasterField = AccessTools.Field(typeof(Reward), "master");
        private static readonly FieldInfo CompletedQuestsField = AccessTools.Field(typeof(QuestManager), "completedQuests");

        private static OfficialQuestProjection active;

        private sealed class Entry
        {
            internal OfficialQuestBinding Binding;
            internal Quest Prefab;
            internal GameObject PrefabRoot;
            internal bool Blocked, CollisionReported;
            internal QuestManager CleanedFreshManager;
            internal int CleanedFreshSlot = int.MinValue;
            internal float NextRegistrationAttempt;
            internal int LastStamp = int.MinValue;
        }

        private sealed class ClientState
        {
            internal IOfficialQuestClient Client;
            internal int LastSlot = int.MinValue;
        }

        private readonly ModBehaviour host;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<int, Entry> byId = new Dictionary<int, Entry>();
        private readonly List<ClientState> clients = new List<ClientState>();
        private bool disposed, subscribed;
        private float nextTick;

        internal OfficialQuestProjection(ModBehaviour owner)
        {
            host = owner;
            active = this;
            Subscribe();
        }

        /// <summary>登记一条定义并立刻尝试注册官方模板。同 ID 重复登记忽略；定义不完整直接拒绝（不进注册表）。</summary>
        internal void Register(OfficialQuestBinding binding)
        {
            if (disposed || binding == null || byId.ContainsKey(binding.QuestId)) return;
            if (binding.Client == null || binding.Accept == null || binding.Deliver == null ||
                binding.CanOffer == null || binding.CanDeliver == null || binding.IsAccepted == null || binding.IsDelivered == null)
            {
                ModBehaviour.CriticalLog("official-quest-binding-" + binding.QuestId,
                    "[OfficialQuest] [ERROR] 任务定义不完整（缺客户端或委托），拒绝登记: " + binding.QuestId);
                return;
            }
            var entry = new Entry { Binding = binding };
            entries.Add(entry);
            byId[binding.QuestId] = entry;
            if (FindClient(binding.Client) == null) clients.Add(new ClientState { Client = binding.Client });
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

        /// <summary>撤掉一个客户端的全部定义：先冻结每条所有权再清，与 Dispose 同一纪律。</summary>
        internal void UnregisterClient(IOfficialQuestClient client)
        {
            if (client == null) return;
            var owned = new List<Entry>();
            var flags = new List<bool>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (!ReferenceEquals(entries[i].Binding.Client, client)) continue;
                owned.Add(entries[i]);
                flags.Add(Owns(entries[i]));
            }
            for (int i = 0; i < owned.Count; i++)
            {
                Release(owned[i], flags[i]);
                entries.Remove(owned[i]);
                byId.Remove(owned[i].Binding.QuestId);
            }
            ClientState state = FindClient(client);
            if (state != null) clients.Remove(state);
        }

        private ClientState FindClient(IOfficialQuestClient client)
        {
            for (int i = 0; i < clients.Count; i++)
                if (ReferenceEquals(clients[i].Client, client)) return clients[i];
            return null;
        }

        private static bool Owns(Entry entry)
        {
            return entry != null && entry.Prefab != null && entry.Prefab.gameObject != null &&
                entry.Prefab.ID == entry.Binding.QuestId && entry.Prefab.gameObject.name == entry.Binding.ObjectName &&
                entry.Prefab.GetComponent<OfficialQuestProjectionTask>() != null;
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
            try { return entry.Binding.CanOffer(); }
            catch (Exception e)
            {
                ModBehaviour.DevLog(entry.Binding.Client.LogTag + " [WARNING] CanOffer 判据异常 quest=" + id + ": " + e.Message);
                return false;
            }
        }

        /// <summary>官方完成按钮按下：先把 Mod 的交付事实落下，成功才放行官方把任务挪进 history。</summary>
        internal static bool TryCommitDelivery(int id, out string reason)
        {
            Entry entry;
            if (!TryGetOwned(id, out entry))
            {
                reason = L10n.T("任务尚未就绪。", "The quest is not ready yet.");
                return false;
            }
            OfficialQuestBinding binding = entry.Binding;
            if (!binding.CanDeliver())
            {
                reason = binding.DeliverBlocked != null ? binding.DeliverBlocked() : null;
                if (string.IsNullOrEmpty(reason))
                    reason = L10n.T("目标完成后，请在任务给予者所在地图交付。", "Finish the objectives, then report to the giver on their map.");
                return false;
            }
            // 发钱只认「这一拍从未交付变成已交付」：读档重建投影走的是 ForceComplete，不经过这里，也就不会再发一次。
            bool wasDelivered = binding.IsDelivered();
            bool committed = binding.Deliver(out reason);
            if (committed && !wasDelivered && binding.PayReward != null) binding.PayReward();
            return committed;
        }

        /// <summary>官方任务详情页的「所需物品」栏：纯展示，收物品仍由那条任务自己的 Deliver 负责。</summary>
        private static void ApplyRequiredItem(Entry entry, Quest quest)
        {
            if (entry.Binding.RequiredItemId <= 0) return;
            if (QuestRequiredItemField == null || QuestRequiredCountField == null)
            {
                ModBehaviour.DevLog(entry.Binding.Client.LogTag + " [WARNING] 官方 Quest 的需求物品字段已改名，任务页不显示交付物。");
                return;
            }
            QuestRequiredItemField.SetValue(quest, entry.Binding.RequiredItemId);
            QuestRequiredCountField.SetValue(quest, entry.Binding.RequiredItemCount > 0 ? entry.Binding.RequiredItemCount : 1);
        }

        /// <summary>
        /// 奖励投影：官方 <c>Reward</c> 的 <c>Awake</c> 要求 master 已经就位，所以先建停用的子物体、写完字段再激活
        /// （模板根对象始终保持激活，官方克隆才不会继承停用状态）。
        /// 不用官方 <c>QuestReward_Money</c>：它的「已领取」写在实例上，而我们每次加载都重建一份投影，
        /// 玩家在已完成页就能一天领一次钱。这里的 Claimed 读的是客户端的交付事实，重建多少次都只有一次。
        /// </summary>
        private static void ApplyReward(Entry entry, Quest quest, GameObject root)
        {
            if (entry.Binding.RewardMoney <= 0) return;
            if (QuestRewardsField == null || RewardIdField == null || RewardMasterField == null)
            {
                ModBehaviour.DevLog(entry.Binding.Client.LogTag + " [WARNING] 官方 Reward 字段已改名，任务页不显示奖励行（交付照常发钱）。");
                return;
            }
            var rewards = QuestRewardsField.GetValue(quest) as List<Reward>;
            if (rewards == null) return;
            GameObject rewardHost = new GameObject("Reward");
            rewardHost.transform.SetParent(root.transform, false);
            rewardHost.SetActive(false);
            OfficialQuestProjectionReward reward = rewardHost.AddComponent<OfficialQuestProjectionReward>();
            reward.questId = entry.Binding.QuestId;
            reward.amount = entry.Binding.RewardMoney;
            RewardIdField.SetValue(reward, 1);
            RewardMasterField.SetValue(reward, quest);
            rewards.Add(reward);
            rewardHost.SetActive(true);
        }

        internal static bool IsRewardPaid(int questId)
        {
            Entry entry;
            if (!TryGetOwned(questId, out entry)) return false;
            return entry.Binding.RewardPaid != null ? entry.Binding.RewardPaid() : entry.Binding.IsDelivered();
        }

        internal static void ReportDeliveryFailure(string reason)
        {
            if (active != null && !active.disposed && active.host != null && !string.IsNullOrEmpty(reason))
                active.host.ShowMessage(reason);
        }

        internal static bool IsTaskDone(int questId, int taskId)
        {
            Entry entry;
            OfficialQuestTaskBinding task;
            if (!TryGetTask(questId, taskId, out entry, out task)) return false;
            return entry.Binding.Client.Ready && task.Done != null && task.Done();
        }

        internal static string DescribeTask(int questId, int taskId)
        {
            Entry entry;
            OfficialQuestTaskBinding task;
            if (!TryGetTask(questId, taskId, out entry, out task)) return string.Empty;
            if (!entry.Binding.Client.Ready || task.Description == null) return string.Empty;
            return task.Description() ?? string.Empty;
        }

        internal static string TaskHint(int questId, int taskId)
        {
            Entry entry;
            OfficialQuestTaskBinding task;
            if (!TryGetTask(questId, taskId, out entry, out task) || task.ExtraHint == null) return null;
            return entry.Binding.Client.Ready ? task.ExtraHint() : null;
        }

        private static bool TryGetTask(int questId, int taskId, out Entry entry, out OfficialQuestTaskBinding task)
        {
            task = null;
            entry = null;
            if (active == null || !active.byId.TryGetValue(questId, out entry) || entry.Binding.Tasks == null) return false;
            for (int i = 0; i < entry.Binding.Tasks.Length; i++)
                if (entry.Binding.Tasks[i].TaskId == taskId) { task = entry.Binding.Tasks[i]; return true; }
            return false;
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
            for (int c = 0; c < clients.Count; c++)
            {
                ClientState state = clients[c];
                IOfficialQuestClient client = state.Client;
                client.BeginTick();
                // 事实暂缺（离岛的一两拍、换槽途中、征程未 bootstrap）不清也不建：这时清掉的投影会在事实回来后再建一遍，白发一轮事件。
                if (!client.Ready) continue;
                // 先按新槽整清、再按新槽重建：旧槽的 history 不能留到新槽里。
                int slot = client.Slot;
                bool slotChanged = slot != state.LastSlot;
                state.LastSlot = slot;
                bool dirty = slotChanged;
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry entry = entries[i];
                    if (!ReferenceEquals(entry.Binding.Client, client) || !Owns(entry)) continue;
                    if (slotChanged) { ClearProjection(entry, manager); entry.CleanedFreshManager = null; }
                    Synchronize(entry, manager, slot);
                    RefreshActiveTask(entry, manager);
                    int stamp = entry.Binding.StateStamp != null ? entry.Binding.StateStamp() : 0;
                    if (entry.LastStamp != stamp) { entry.LastStamp = stamp; dirty = true; }
                }
                client.EndTick(dirty);
            }
        }

        /// <summary>找到一位官方给予者（基地的 Jeff）后：补注册、同步投影、刷新它的头顶标记。</summary>
        internal void PrepareGiver(QuestGiver giver)
        {
            if (disposed || giver == null) return;
            for (int i = 0; i < entries.Count; i++) EnsureRegistered(entries[i]);
            QuestManager manager = QuestManager.Instance;
            if (manager != null)
            {
                for (int c = 0; c < clients.Count; c++)
                {
                    IOfficialQuestClient client = clients[c].Client;
                    if (!client.Ready) continue;
                    int slot = client.Slot;
                    for (int i = 0; i < entries.Count; i++)
                        if (ReferenceEquals(entries[i].Binding.Client, client) && Owns(entries[i])) Synchronize(entries[i], manager, slot);
                }
            }
            OfficialQuestGiverLocator.RefreshMarker(giver);
        }

        /// <summary>事实刚改变：立刻刷该客户端的 Task（官方会 Push「目标完成」通知）并刷头顶标记，不等下一拍。</summary>
        internal void NotifyProgressChanged(IOfficialQuestClient client)
        {
            if (disposed || client == null) return;
            QuestManager manager = QuestManager.Instance;
            if (manager == null) return;
            for (int i = 0; i < entries.Count; i++)
                if (ReferenceEquals(entries[i].Binding.Client, client) && Owns(entries[i])) RefreshActiveTask(entries[i], manager);
            client.RefreshMarkers();
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
            OfficialQuestBinding binding = entry.Binding;
            GameObject root = null;
            try
            {
                QuestCollection collection = GameplayDataSettings.QuestCollection;
                if (collection == null) return;
                Quest existing = collection.Get(binding.QuestId);
                if (existing != null)
                {
                    if (existing.gameObject != null && existing.gameObject.name == binding.ObjectName &&
                        existing.GetComponent<OfficialQuestProjectionTask>() != null)
                    {
                        entry.Prefab = existing;
                        entry.PrefabRoot = existing.gameObject;
                        entry.PrefabRoot.SetActive(true);
                        return;
                    }
                    if (!entry.CollisionReported)
                    {
                        entry.CollisionReported = true;
                        ModBehaviour.CriticalLog("official-quest-id-collision-" + binding.QuestId,
                            binding.Client.LogTag + " Quest ID " + binding.QuestId + " 已被其它内容占用，这条任务不会注入官方任务页。");
                    }
                    entry.Blocked = true;
                    return;
                }
                if (QuestIdField == null || QuestGiverField == null || TaskIdField == null || TaskMasterField == null)
                    throw new MissingFieldException("官方 Quest/Task 私有字段签名已变化");
                if (binding.Tasks == null || binding.Tasks.Length == 0)
                    throw new InvalidOperationException("任务定义没有目标");

                root = new GameObject(binding.ObjectName);
                root.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(root);
                Quest quest = root.AddComponent<Quest>();
                QuestIdField.SetValue(quest, binding.QuestId);
                QuestGiverField.SetValue(quest, (QuestGiverID)binding.GiverId);
                quest.DisplayNameRaw = binding.NameKey;
                quest.DescriptionRaw = binding.DescriptionKey;
                // 多个目标挂在同一个模板对象上：官方 Instantiate 整棵克隆，Task.Master 才会指向克隆后的 Quest。
                for (int i = 0; i < binding.Tasks.Length; i++)
                {
                    var task = root.AddComponent<OfficialQuestProjectionTask>();
                    task.questId = binding.QuestId;
                    task.taskId = binding.Tasks[i].TaskId;
                    TaskIdField.SetValue(task, binding.Tasks[i].TaskId);
                    TaskMasterField.SetValue(task, quest);
                    quest.Tasks.Add(task);
                }
                ApplyRequiredItem(entry, quest);
                ApplyReward(entry, quest, root);
                collection.Add(quest);
                entry.Prefab = quest;
                entry.PrefabRoot = root;
                ModBehaviour.DevLog(binding.Client.LogTag + " 官方任务已注册: id=" + binding.QuestId + " giver=" + binding.GiverId);
            }
            catch (Exception e)
            {
                entry.Blocked = true;
                if (root != null) UnityEngine.Object.Destroy(root);
                entry.PrefabRoot = null;
                entry.Prefab = null;
                ModBehaviour.CriticalLog("official-quest-register-" + binding.QuestId,
                    binding.Client.LogTag + " [ERROR] 官方任务注册失败，本轮隐藏这条任务: " + e);
            }
        }

        private void Synchronize(Entry entry, QuestManager manager, int slot)
        {
            int id = entry.Binding.QuestId;
            Quest activeQuest = Find(manager.ActiveQuests, id);
            Quest historyQuest = Find(manager.HistoryQuests, id);

            if (entry.Binding.IsDelivered())
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

            if (entry.Binding.IsAccepted())
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

            // Mod 事实是权威。若旧实验版本或外部修改留下了同 ID 的运行时对象，空白槽要把它收掉。
            if (ReferenceEquals(entry.CleanedFreshManager, manager) && entry.CleanedFreshSlot == slot &&
                activeQuest == null && historyQuest == null) return;
            ClearProjection(entry, manager);
            entry.CleanedFreshManager = manager;
            entry.CleanedFreshSlot = slot;
        }

        private static void ClearProjection(Entry entry, QuestManager manager)
        {
            int id = entry.Binding.QuestId;
            RemoveAllQuests(manager.ActiveQuests, id, null);
            RemoveAllQuests(manager.HistoryQuests, id, null);
            RemoveCompletedId(manager, id);
            manager.EverInspectedQuest.Remove(id);
        }

        private static void RefreshActiveTask(Entry entry, QuestManager manager)
        {
            Quest quest = Find(manager.ActiveQuests, entry.Binding.QuestId);
            if (quest == null) return;
            foreach (Duckov.Quests.Task task in quest.Tasks)
            {
                var own = task as OfficialQuestProjectionTask;
                if (own != null) own.RefreshFromState();
            }
        }

        private void OnQuestActivated(Quest quest)
        {
            Entry entry;
            if (disposed || quest == null || !TryGetOwned(quest.ID, out entry)) return;
            OfficialQuestBinding binding = entry.Binding;
            if (!binding.Client.Ready || binding.IsAccepted() || binding.IsDelivered()) return;
            string message;
            bool accepted = binding.Accept(out message);
            if (!accepted)
                ModBehaviour.DevLog(binding.Client.LogTag + " [WARNING] 官方接取已发生，Mod 事实等待重试: " + message);
        }

        private void OnQuestCompleted(Quest quest)
        {
            Entry entry;
            if (disposed || quest == null || !TryGetOwned(quest.ID, out entry)) return;
            OfficialQuestBinding binding = entry.Binding;
            if (binding.Client.Ready && !binding.IsDelivered())
                ModBehaviour.DevLog(binding.Client.LogTag + " [WARNING] 官方任务已完成，但交付事实尚未提交；下次同步会继续重试: " + quest.ID);
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
            manager.ActivateQuest(entry.Binding.QuestId, (QuestGiverID)entry.Binding.GiverId);
            return Find(manager.ActiveQuests, entry.Binding.QuestId);
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
            // 只剥离本核心确实拥有的 ID。若其它 Mod 抢先注册了同 ID，那一条注册会 fail closed，
            // 这里也必须完全放行它，不能仅凭整数 ID 删除对方的任务进度。
            if (active == null || !(snapshot is QuestManager.SaveData)) return snapshot;
            QuestManager.SaveData data = (QuestManager.SaveData)snapshot;
            for (int i = 0; i < active.entries.Count; i++)
            {
                Entry entry = active.entries[i];
                if (!OwnsRegisteredQuest(entry.Binding.QuestId)) continue;
                int id = entry.Binding.QuestId;
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
            clients.Clear();
            if (ReferenceEquals(active, this)) active = null;
        }
    }
}
