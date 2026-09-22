using System;

namespace BossRush
{
    /// <summary>接取 / 交付提交：把 Mod 事实落下，失败时给玩家一句原因。</summary>
    internal delegate bool OfficialQuestCommit(out string message);

    /// <summary>
    /// 一条投影到官方 <c>Duckov.Quests</c> 的 Task。全部是无参委托：事实源由客户端自己在闭包里解析，
    /// 投影核心不知道「剧情事实」长什么样（天空岛读分槽故事，鸭王征程读章节状态机）。
    /// </summary>
    internal sealed class OfficialQuestTaskBinding
    {
        /// <summary>Quest 内唯一。</summary>
        internal int TaskId;
        internal Func<bool> Done;
        /// <summary>官方任务日志里的目标行。</summary>
        internal Func<string> Description;
        /// <summary>可空：ExtraDescriptsions 的一行提示。</summary>
        internal Func<string> ExtraHint;
    }

    /// <summary>
    /// 投影核心的客户端：一个子系统一份（天空岛 / 鸭王征程）。核心每拍按客户端分组同步，
    /// 事实未就绪（<see cref="Ready"/> 为假）时对该客户端不清不建。
    /// </summary>
    internal interface IOfficialQuestClient
    {
        /// <summary>日志前缀，如 "[SkyIslandQuest]"。</summary>
        string LogTag { get; }
        /// <summary>事实源已就绪：可以按 IsAccepted / IsDelivered 重建投影。</summary>
        bool Ready { get; }
        /// <summary>当前存档槽；取不到时返回 -1。换槽时核心先整清再重建。</summary>
        int Slot { get; }
        /// <summary>每拍同步之前调用（不论 Ready）。天空岛在这里处理离岛后的会话状态。</summary>
        void BeginTick();
        /// <summary>每拍同步之后调用（仅 Ready 时）。dirty = 换槽或任一条目的事实指纹变化。</summary>
        void EndTick(bool dirty);
        /// <summary>事实刚变化：让在场给予者重算头顶标记。</summary>
        void RefreshMarkers();
    }

    /// <summary>
    /// 一条投影到官方 Quest 的任务定义（与剧情事实无关的委托面）。Mod 存档是权威：
    /// 官方 active / history 只是从 <see cref="IsAccepted"/> / <see cref="IsDelivered"/> 重建出来的投影。
    /// 无 Unity / Duckov 依赖：<c>GiverId</c> 用 <c>QuestGiverID</c> 的整数值。
    /// </summary>
    internal sealed class OfficialQuestBinding
    {
        internal int QuestId;
        /// <summary>官方 <c>QuestGiverID</c> 的整数值：Jeff = 1；BossRush 自定义给予者用 5900–5949 保留区。</summary>
        internal int GiverId;
        /// <summary>模板 GameObject 名，所有权判据的一部分（ID + 对象名 + 专用 Task 组件）。</summary>
        internal string ObjectName;
        internal string NameKey;
        internal string DescriptionKey;
        /// <summary>可选：官方任务详情页「所需物品」栏显示的交付物（0 = 不显示）。纯展示。</summary>
        internal int RequiredItemId;
        internal int RequiredItemCount;
        /// <summary>可选：官方完成面板按 Reward_Money 显示的金额（0 = 无）。真正发放见 <see cref="PayReward"/>。</summary>
        internal int RewardMoney;
        /// <summary>官方可接取页的门（顶掉 Quest.MeetsPrerequisit）。</summary>
        internal Func<bool> CanOffer;
        /// <summary>官方完成按钮按下时的门；为假时交付被拦下。</summary>
        internal Func<bool> CanDeliver;
        /// <summary>可空：CanDeliver 为假时给玩家的原因；null 走核心默认文案。</summary>
        internal Func<string> DeliverBlocked;
        internal Func<bool> IsAccepted;
        internal Func<bool> IsDelivered;
        /// <summary>奖励行「已领取」：读 Mod 事实，读档重建多少次都只有一次。</summary>
        internal Func<bool> RewardPaid;
        /// <summary>官方接取已发生 → 写 Mod 事实。必填。</summary>
        internal OfficialQuestCommit Accept;
        /// <summary>官方完成按钮 → 写 Mod 交付事实。必填；返回 false 则官方任务不进 history。</summary>
        internal OfficialQuestCommit Deliver;
        /// <summary>可空：交付「从未交付变成已交付」那一拍调用一次（天空岛在这里发钱；征程为 null，发钱归交付事务）。</summary>
        internal Action PayReward;
        /// <summary>事实指纹：变化时刷新给予者标记。</summary>
        internal Func<int> StateStamp;
        internal OfficialQuestTaskBinding[] Tasks;
        internal IOfficialQuestClient Client;
    }
}
