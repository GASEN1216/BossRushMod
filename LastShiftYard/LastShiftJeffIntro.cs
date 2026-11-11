using System;
using Duckov.Quests;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT / WIRE+：杰夫的末班编组站引导（官方任务 590216，owner 2026-11-11 授权）：去仓库区西场找检票员余响说上话，
    /// 回基地交给杰夫。事实在编组站剧情存档里（<see cref="LastShiftStoryData.JeffAccepted"/> / <see cref="LastShiftStoryData.Met"/> /
    /// <see cref="LastShiftStoryData.JeffDone"/>），官方任务页只是投影（口径同 590215 `EclipseBloomIntroduction`）。
    /// 和余响说过话（对话或剪票上车）就算完成目标；没有提交物和奖品。只在还没见过她、没去过编组站时发放。
    /// 由 <see cref="LastShiftRuntimeModule"/> 持有。
    /// </summary>
    internal sealed class LastShiftJeffIntro : IOfficialQuestClient
    {
        internal const int QuestId = 590216;
        private const string NameKey = "BossRush_LastShiftYard_Jeff_Name";
        private const string DescriptionKey = "BossRush_LastShiftYard_Jeff_Description";
        private readonly OfficialQuestProjection projection;
        private QuestGiver jeff;
        private float nextJeffAttempt;
        private bool deliveryActive;

        internal LastShiftJeffIntro(OfficialQuestProjection projection)
        {
            this.projection = projection;
            InjectLocalizations();
            if (projection != null) projection.Register(BuildBinding());
            else Debug.LogWarning(LastShiftRaidScene.LogTag + " 官方任务核心尚未准备，杰夫的编组站引导未注册");
        }

        internal static void InjectLocalizations()
        {
            LocalizationHelper.InjectLocalization(NameKey, L10n.T("末班车还在剪票", "The Last Train's Still Taking Tickets"));
            LocalizationHelper.InjectLocalization(DescriptionKey, L10n.T(
                "仓库区西场，第二座龙门吊底下，有个检票员还在给末班车剪票。那条线早停了，可听人说夜里还有货车进站，车上的封签货没人签收。你去找她聊两句，回来跟我说说是真是假。",
                "Under the second gantry crane in the warehouse district's west yard, there's a ticket inspector still punching tickets for the last train. That line shut down years ago, but word is freight still pulls in at night and nobody's signing for the sealed cargo. Go have a word with her, then come tell me if it's true."));
        }

        private static LastShiftStoryData Data { get { return LastShiftStory.Current; } }

        private OfficialQuestBinding BuildBinding()
        {
            return new OfficialQuestBinding
            {
                QuestId = QuestId, GiverId = (int)QuestGiverID.Jeff,
                ObjectName = "BossRush_LastShiftYard_Jeff_OfficialQuest",
                NameKey = NameKey, DescriptionKey = DescriptionKey, Client = this,
                CanOffer = CanOffer, CanDeliver = CanDeliver,
                IsAccepted = () => Data != null && Data.JeffAccepted && !Data.JeffDone,
                IsDelivered = () => Data != null && Data.JeffDone,
                RewardPaid = () => Data != null && Data.JeffDone,
                StateStamp = () => Data == null ? 0 : (Data.JeffAccepted ? 1 : 0) | (Data.Met ? 2 : 0) | (Data.JeffDone ? 4 : 0),
                Accept = Accept, Deliver = Deliver, BeginDelivery = BeginDelivery, EndDelivery = EndDelivery,
                DeliverBlocked = () => L10n.T("先去仓库区跟余响说上话。", "Go talk to Yu Xiang in the warehouse district first."),
                Tasks = new[] { new OfficialQuestTaskBinding
                {
                    TaskId = 1, Done = () => Data != null && Data.Met,
                    Description = () => Data != null && Data.Met
                        ? L10n.T("回基地跟杰夫说说", "Go back to base and tell Jeff")
                        : L10n.T("在仓库区西场找到检票员余响，和她对话", "Find the ticket inspector Yu Xiang in the warehouse west yard and talk to her"),
                    ExtraHint = () => L10n.T("仓库区地图上标着末班车剪票处。", "The last train ticket desk is marked on the warehouse district map.")
                } }
            };
        }

        private static bool InBase()
        {
            LevelManager level = LevelManager.Instance;
            return level != null && level.IsBaseLevel && !SceneLoader.IsSceneLoading && LevelManager.AfterInit;
        }

        private static bool PlayerAlive()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            return player != null && player.Health != null && !player.Health.IsDead;
        }

        private static bool CanOffer()
        {
            LastShiftStoryData d = Data;
            return d != null && LastShiftStory.Writable && !d.JeffAccepted && !d.JeffDone && !d.Met && d.Runs == 0 && InBase() && PlayerAlive();
        }

        private bool CanDeliver()
        {
            LastShiftStoryData d = Data;
            return d != null && LastShiftStory.Writable && d.JeffAccepted && d.Met && !d.JeffDone && InBase() && PlayerAlive();
        }

        private bool Accept(out string message)
        {
            message = L10n.T("这事待会儿再说。", "Let's talk about this later.");
            if (!CanOffer() || !LastShiftStory.SetJeff(true, false)) return false;
            message = L10n.T("仓库区西场，第二座龙门吊。去吧。", "Warehouse district, west yard, second gantry crane. Off you go.");
            if (projection != null) projection.NotifyProgressChanged(this);
            return true;
        }

        private bool Deliver(out string message)
        {
            message = L10n.T("还没见着人吧？", "You haven't found her yet, have you?");
            if (!CanDeliver() || !LastShiftStory.SetJeff(true, true)) return false;
            message = L10n.T("末班车还在开？行，那边的货你自己看着办。", "So the last train still runs? Fine. The cargo out there is your call.");
            if (projection != null) projection.NotifyProgressChanged(this);
            return true;
        }

        private bool BeginDelivery(Func<bool> collectAssets, out string message)
        {
            message = L10n.T("这事待会儿再说。", "Let's talk about this later.");
            if (deliveryActive || !CanDeliver()) return false;
            // 无提交物、现金与奖品：只防重入，不额外保存玩家战利品（口径同 590215）。
            deliveryActive = true;
            message = null;
            return true;
        }

        private void EndDelivery(bool committed) { deliveryActive = false; }

        /// <summary>剧情存档里刚记下「见过余响」：刷新杰夫头顶的标记与任务页（由 <see cref="LastShiftStory.MarkMet"/> 调）。</summary>
        internal void NotifyChanged() { if (projection != null) projection.NotifyProgressChanged(this); }

        /// <summary>模块每帧调：在基地时找一次杰夫，交给投影核心挂任务。</summary>
        internal void Tick()
        {
            if (!InBase()) { jeff = null; return; }
            if (jeff != null || Time.unscaledTime < nextJeffAttempt) return;
            nextJeffAttempt = Time.unscaledTime + 1f;
            if (OfficialQuestGiverLocator.TryFind(QuestGiverID.Jeff, out jeff) && projection != null) projection.PrepareGiver(jeff);
        }

        string IOfficialQuestClient.LogTag { get { return "[LastShiftJeff]"; } }
        bool IOfficialQuestClient.Ready { get { return LastShiftStory.Current != null && LastShiftStory.Writable; } }
        int IOfficialQuestClient.Slot
        {
            get
            {
                try { return SavesSystem.CurrentSlot; }
                catch (Exception) { return -1; }
            }
        }
        void IOfficialQuestClient.BeginTick() { }
        void IOfficialQuestClient.EndTick(bool dirty) { if (dirty) OfficialQuestGiverLocator.RefreshMarker(jeff); }
        void IOfficialQuestClient.RefreshMarkers() { OfficialQuestGiverLocator.RefreshMarker(jeff); }

        internal void Dispose()
        {
            if (projection != null) projection.UnregisterClient(this);
            jeff = null;
        }
    }
}
