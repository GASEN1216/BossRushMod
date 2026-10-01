using System;
using Duckov.Quests;
using HarmonyLib;

namespace BossRush
{
    /// <summary>
    /// 官方 Task 的通用实现：只带两个整数回查投影核心（委托不随 Instantiate 克隆，静态引用会串），判据与文案都读客户端事实。
    /// </summary>
    public sealed class OfficialQuestProjectionTask : Duckov.Quests.Task
    {
        public int questId;
        public int taskId;
        private bool initialized, lastKnown;

        public override string Description
        {
            get { return OfficialQuestProjection.DescribeTask(questId, taskId); }
        }

        public override string[] ExtraDescriptsions
        {
            get
            {
                string hint = OfficialQuestProjection.TaskHint(questId, taskId);
                return string.IsNullOrEmpty(hint) ? new string[0] : new[] { hint };
            }
        }

        protected override bool CheckFinished() { return OfficialQuestProjection.IsTaskDone(questId, taskId); }
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

        internal void RefreshFromState()
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
            // 换槽或事实恢复可能让同一运行时投影从“已完成目标”回到“进行中”；两边都显式同步组件状态。
            enabled = !current;
        }
    }

    /// <summary>
    /// 官方奖励行的投影：只负责在任务详情页与完成面板上显示「金钱 +N」，不自己发钱。
    /// 「已领取」读的是客户端的交付事实，所以每次加载重建出来的投影都是已领取状态，官方按钮点不动，
    /// 不会出现「已完成页每天领一次」。与 Task 一样只带值类型字段回查（委托不随 Instantiate 克隆）。
    /// </summary>
    public sealed class OfficialQuestProjectionReward : Duckov.Quests.Reward
    {
        public int questId;
        public int amount;
        /// <summary>完成面板还开着：见 <see cref="OfficialQuestProjection.HoldRewardsForCompletePanel"/>。</summary>
        [NonSerialized] internal bool presenting;

        public override bool Claimed { get { return !presenting && OfficialQuestProjection.IsRewardPaid(questId); } }
        public override bool AutoClaim { get { return true; } }
        public override string Description { get { return OfficialQuestText.DescribeMoney(amount); } }

        /// <summary>发放随交付事实一次完成（见各客户端的 PayReward / 交付事务），这里只收起完成面板的展示态。</summary>
        public override void OnClaim()
        {
            presenting = false;
        }

        public override object GenerateSaveData() { return Claimed; }

        public override void SetupSaveData(object data)
        {
        }
    }

    /// <summary>
    /// 奖励物品行的投影：图标与「名字 xN」照官方 RewardItem 的样子显示，发放由投影核心在交付事务里一次完成。
    /// 「已领取」同样读客户端的交付事实。
    /// </summary>
    public sealed class OfficialQuestProjectionItemReward : Duckov.Quests.Reward
    {
        public int questId;
        public int typeId;
        public int amount;
        /// <summary>完成面板还开着：见 <see cref="OfficialQuestProjection.HoldRewardsForCompletePanel"/>。</summary>
        [NonSerialized] internal bool presenting;

        public override bool Claimed { get { return !presenting && OfficialQuestProjection.IsRewardPaid(questId); } }
        public override bool AutoClaim { get { return true; } }

        public override UnityEngine.Sprite Icon
        {
            get
            {
                try { return ItemStatsSystem.ItemAssetsCollection.GetMetaData(typeId).icon; }
                catch (Exception) { return null; }
            }
        }

        public override string Description { get { return OfficialQuestItems.DisplayName(typeId) + " x" + amount; } }

        /// <summary>发放在交付事务里完成，这里只收起完成面板的展示态。</summary>
        public override void OnClaim()
        {
            presenting = false;
        }

        public override object GenerateSaveData() { return Claimed; }

        public override void SetupSaveData(object data)
        {
        }
    }

    internal static class OfficialQuestText
    {
        /// <summary>奖励行文案复用官方 Reward_Money（各语言都有），取不到格式串时退回中英双语。</summary>
        internal static string DescribeMoney(int amount)
        {
            try
            {
                string format = L10n.T("Reward_Money");
                if (!string.IsNullOrEmpty(format) && format.IndexOf("{amount}", StringComparison.Ordinal) >= 0)
                    return format.Replace("{amount}", amount.ToString());
            }
            catch (Exception)
            {
                // 官方格式串取不到时走下面的退路
            }
            return L10n.T("金钱 +" + amount, "Currency +" + amount);
        }
    }

    /// <summary>官方 MeetsPrerequisit 要求 QuestRelationGraph 里有节点，运行时注册的任务必然为 false：自家 ID 用客户端的门顶掉。</summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.MeetsPrerequisit))]
    internal static class OfficialQuestAvailabilityPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Quest __instance, ref bool __result)
        {
            if (__instance == null || !OfficialQuestProjection.IsOwnQuest(__instance.ID)) return true;
            __result = OfficialQuestProjection.CanOffer(__instance.ID);
            return false;
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.TryComplete))]
    internal static class OfficialQuestCompletePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Quest __instance, ref bool __result)
        {
            if (__instance == null || !OfficialQuestProjection.IsOwnQuest(__instance.ID)) return true;
            if (!__instance.AreTasksFinished()) return true;
            string reason;
            if (OfficialQuestProjection.TryCommitDelivery(__instance.ID, out reason)) return true;
            __result = false;
            OfficialQuestProjection.ReportDeliveryFailure(reason);
            return false;
        }

        /// <summary>官方真的完成了这一拍：奖励行先显示成「未领取」，让官方完成面板停住等玩家点（见 HoldRewardsForCompletePanel）。</summary>
        [HarmonyPostfix]
        private static void Postfix(Quest __instance, bool __result)
        {
            if (__result && __instance != null && OfficialQuestProjection.IsOwnQuest(__instance.ID))
                OfficialQuestProjection.HoldRewardsForCompletePanel(__instance);
        }
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.GenerateSaveData))]
    internal static class OfficialQuestSaveFilterPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref object __result)
        {
            __result = OfficialQuestProjection.FilterSaveSnapshot(__result);
        }
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.SetupSaveData), new Type[] { typeof(object) })]
    internal static class OfficialQuestLoadFilterPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ref object dataObj)
        {
            dataObj = OfficialQuestProjection.FilterSaveSnapshot(dataObj);
        }
    }
}
