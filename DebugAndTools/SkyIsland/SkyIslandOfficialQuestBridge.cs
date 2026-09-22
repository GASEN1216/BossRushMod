using System;
using System.Collections.Generic;
using Duckov.Economy;
using Duckov.Quests;

namespace BossRush
{
    /// <summary>
    /// 天空岛接官方任务系统的适配器 + 客户端：把 <see cref="SkyIslandOfficialQuestDefinition"/>（强绑定分槽故事事实）
    /// 用闭包包成与事实无关的 <see cref="OfficialQuestBinding"/>，交给共享投影核心 <see cref="OfficialQuestProjection"/>
    /// （`Utilities/OfficialQuests/`，2026-09-22 由本文件原样抽出）。剧情事实仍以 BossRush_SkyIsland_Story_v1 为权威。
    ///
    /// 任务表在 <see cref="SkyIslandOfficialQuestTable"/>（岛上三条）与 <see cref="SkyIslandPreludeFlow"/>（Jeff 序章）；
    /// 注册 / 投影同步 / Harmony 接线 / 存档过滤全在核心，本文件只负责：事实源解析、接取与交付的默认提交、
    /// 交付那一拍发钱、离岛后的会话状态与给予者标记。
    /// </summary>
    internal sealed class SkyIslandOfficialQuestBridge : IDisposable, IOfficialQuestClient
    {
        private const string LogPrefix = "[SkyIslandQuest]";

        private readonly ModBehaviour host;
        private readonly OfficialQuestProjection projection;
        private readonly Dictionary<int, SkyIslandOfficialQuestDefinition> definitions = new Dictionary<int, SkyIslandOfficialQuestDefinition>();
        private bool disposed, wasOnIsland;

        internal SkyIslandOfficialQuestBridge(ModBehaviour owner)
        {
            host = owner;
            projection = owner != null && owner.OfficialQuestRuntime != null ? owner.OfficialQuestRuntime.Projection : null;
            if (projection == null)
                ModBehaviour.CriticalLog("sky-island-quest-core-missing", LogPrefix + " [ERROR] 官方任务投影核心未就绪，晴岚任务不会注入官方任务页。");
            SkyIslandOfficialQuestTable.InjectLocalizations();
            foreach (SkyIslandOfficialQuestDefinition definition in SkyIslandOfficialQuestTable.Island) Register(definition);
        }

        /// <summary>登记一条定义并交给核心注册官方模板。同 ID 重复登记忽略。</summary>
        internal void Register(SkyIslandOfficialQuestDefinition definition)
        {
            if (disposed || definition == null || projection == null || definitions.ContainsKey(definition.QuestId)) return;
            definitions[definition.QuestId] = definition;
            projection.Register(BuildBinding(definition));
        }

        /// <summary>撤掉一条定义（序章销毁时用）。</summary>
        internal void Unregister(int questId)
        {
            if (!definitions.Remove(questId) || projection == null) return;
            projection.Unregister(questId);
        }

        /// <summary>找到一位官方给予者（基地的 Jeff）后：补注册、同步投影、刷新它的头顶标记。</summary>
        internal void PrepareGiver(QuestGiver giver)
        {
            if (disposed || projection == null) return;
            projection.PrepareGiver(giver);
        }

        /// <summary>事实刚改变：立刻刷 Task（官方会 Push「目标完成」通知）并刷头顶标记，不等下一拍。</summary>
        internal void NotifyProgressChanged()
        {
            if (disposed || projection == null) return;
            projection.NotifyProgressChanged(this);
        }

        // ---- IOfficialQuestClient ----

        string IOfficialQuestClient.LogTag { get { return LogPrefix; } }

        /// <summary>故事就绪 = 事实源解析得到且当前槽有数据（离岛的一两拍、换槽途中为假，核心对本客户端不清不建）。</summary>
        bool IOfficialQuestClient.Ready { get { return CurrentData() != null; } }

        int IOfficialQuestClient.Slot
        {
            get
            {
                try { return Saves.SavesSystem.CurrentSlot; }
                catch (Exception) { return -1; }
            }
        }

        void IOfficialQuestClient.BeginTick()
        {
            bool onIsland;
            SkyIslandOfficialQuestStory.Resolve(host, out onIsland);
            if (wasOnIsland && !onIsland) SkyIslandOfficialQuestGivers.ClearSessionState();
            wasOnIsland = onIsland;
        }

        void IOfficialQuestClient.EndTick(bool dirty)
        {
            bool onIsland;
            SkyIslandOfficialQuestStory.Resolve(host, out onIsland);
            if (onIsland)
            {
                SkyIslandSession session = host == null ? null : host.GetComponent<SkyIslandSession>();
                if (session != null) SkyIslandOfficialQuestGivers.EnsureDeviceFallback(session);
            }
            if (dirty) SkyIslandOfficialQuestGivers.RefreshMarkers();
        }

        void IOfficialQuestClient.RefreshMarkers()
        {
            SkyIslandOfficialQuestGivers.RefreshMarkers();
        }

        // ---- 定义 → 绑定 ----

        private OfficialQuestBinding BuildBinding(SkyIslandOfficialQuestDefinition def)
        {
            var binding = new OfficialQuestBinding
            {
                QuestId = def.QuestId, GiverId = def.GiverId, ObjectName = def.ObjectName,
                NameKey = def.NameKey, DescriptionKey = def.DescriptionKey,
                RequiredItemId = def.RequiredItemId, RequiredItemCount = def.RequiredItemCount, RewardMoney = def.RewardMoney,
                CanOffer = () => SkyIslandOfficialQuestTable.CanOffer(def, SkyIslandOfficialQuestStory.Capture(host)),
                CanDeliver = () => SkyIslandOfficialQuestTable.CanDeliver(def, SkyIslandOfficialQuestStory.Capture(host)),
                IsAccepted = () => HasFlag(def.AcceptedFlag),
                IsDelivered = () => HasFlag(def.DeliveredFlag),
                RewardPaid = () => HasFlag(def.DeliveredFlag),
                Accept = def.Accept != null
                    ? new OfficialQuestCommit(def.Accept.Invoke)
                    : new OfficialQuestCommit((out string message) => DefaultCommit(def.AcceptedFlag, def.AcceptAction, out message)),
                Deliver = def.Deliver != null
                    ? new OfficialQuestCommit(def.Deliver.Invoke)
                    : new OfficialQuestCommit((out string message) => DefaultCommit(def.DeliveredFlag, def.DeliverAction, out message)),
                PayReward = () => PayRewardOnce(def),
                StateStamp = () => { SkyIslandStoryData data = CurrentData(); return data == null ? 0 : data.flags; },
                Client = this,
            };
            int count = def.Tasks == null ? 0 : def.Tasks.Length;
            binding.Tasks = new OfficialQuestTaskBinding[count];
            for (int i = 0; i < count; i++)
            {
                SkyIslandOfficialQuestTaskDefinition task = def.Tasks[i];
                binding.Tasks[i] = new OfficialQuestTaskBinding
                {
                    TaskId = task.TaskId,
                    Done = () => { SkyIslandStoryData data = CurrentData(); return data != null && task.Done(data); },
                    Description = () => { SkyIslandStoryData data = CurrentData(); return data == null ? string.Empty : task.Description(data) ?? string.Empty; },
                    ExtraHint = task.ExtraHint == null ? null
                        : new Func<string>(() => { SkyIslandStoryData data = CurrentData(); return data == null ? null : task.ExtraHint(data); }),
                };
            }
            return binding;
        }

        private bool HasFlag(SkyIslandStoryFlag flag)
        {
            SkyIslandStoryData data = CurrentData();
            return data != null && data.Has(flag);
        }

        /// <summary>交付成功且这一拍才落下交付事实时发一次钱。失败只记日志：剧情事实已经落下，不能因此回滚任务。</summary>
        private void PayRewardOnce(SkyIslandOfficialQuestDefinition def)
        {
            int money = def.RewardMoney;
            if (money <= 0) return;
            SkyIslandStoryData data = CurrentData();
            if (data == null || !data.Has(def.DeliveredFlag)) return;
            try
            {
                if (EconomyManager.Add(money))
                {
                    if (host != null) host.ShowMessage(OfficialQuestText.DescribeMoney(money));
                    return;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.CriticalLog("sky-island-quest-reward-" + def.QuestId,
                    LogPrefix + " [ERROR] 任务奖金发放异常 quest=" + def.QuestId + ": " + e);
                return;
            }
            ModBehaviour.CriticalLog("sky-island-quest-reward-" + def.QuestId,
                LogPrefix + " [ERROR] 任务奖金没有发出去 quest=" + def.QuestId + " money=" + money);
        }

        /// <summary>默认的接取 / 交付：只写对应旗标；已经写过就算成功（幂等）。</summary>
        private bool DefaultCommit(SkyIslandStoryFlag flag, SkyIslandStoryAction action, out string message)
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

        private SkyIslandStoryData CurrentData()
        {
            bool onIsland;
            SkyIslandStoryService story = SkyIslandOfficialQuestStory.Resolve(host, out onIsland);
            return story == null ? null : story.Current;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (projection != null) projection.UnregisterClient(this);
            definitions.Clear();
        }
    }
}
