using System;

namespace BossRush
{
    /// <summary>接取 / 交付提交：把 Mod 事实落下，失败时给玩家一句原因。</summary>
    internal delegate bool OfficialQuestCommit(out string message);
    internal delegate bool OfficialQuestDeliveryBegin(Func<bool> collectAssets, out string message);

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

    /// <summary>奖励物品：交付成功那一拍由核心生成并发放（背包优先，放不下走官方仓库 / 自提）。</summary>
    internal struct OfficialQuestItemStack
    {
        internal int TypeId;
        internal int Count;
        internal OfficialQuestItemStack(int typeId, int count) { TypeId = typeId; Count = count; }
    }

    /// <summary>
    /// 交付时要带在身上交给给予者的物品：TypeIds 里任意几种凑够 Count 个即可。
    /// 核心为每条生成一个官方 Task（持有够了才算完成），交付事务先整份预留、客户端提交成功才真正收走，失败原样归还。
    /// 只认主角背包：与「带在身上交给他」的文案一致，也与交付时实际能扣的范围一致。
    /// </summary>
    internal sealed class OfficialQuestSubmission
    {
        internal int[] TypeIds;
        internal int Count;
        /// <summary>任务日志里的目标行（不含「身上 x/N」，核心补）。</summary>
        internal Func<string> Description;
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
        /// <summary>可选：官方完成面板按 Reward_Money 显示的金额（0 = 无）。纯展示；真正发放由各客户端的交付事务负责。</summary>
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
        /// <summary>冻结客户端采集/落盘，直到提交物、现金、实物与完成事实全部就绪；采集义务保留到物理保存成功。</summary>
        internal OfficialQuestDeliveryBegin BeginDelivery;
        internal Action<bool> EndDelivery;
        /// <summary>出击中的一次性奖品寄往官方待领取区，不为任务发奖提前保存整趟背包战利品。</summary>
        internal Func<bool> RewardsToInbox;
        /// <summary>可空：交付「从未交付变成已交付」那一拍调用一次。现有客户端（天空岛、鸭王征程）都传 null，发钱与交付事实在各自的交付事务里一起提交；留给以后需要交付后单独发奖的客户端。</summary>
        internal Action PayReward;
        /// <summary>事实指纹：变化时刷新给予者标记。</summary>
        internal Func<int> StateStamp;
        internal OfficialQuestTaskBinding[] Tasks;
        /// <summary>可空：交付成功后由核心发放的物品。与 RewardMoney 一样只在「未交付 → 已交付」那一拍发一次。</summary>
        internal OfficialQuestItemStack[] RewardItems;
        /// <summary>可空：交付时收走的物品。</summary>
        internal OfficialQuestSubmission[] Submissions;
        internal IOfficialQuestClient Client;
    }
}
