// ============================================================================
// CollectionJeffIntro.cs - 杰夫的收藏册引导（官方任务 590217，owner 2026-11-19 授权）
// ============================================================================
// 接取：杰夫介绍收集品和收藏册，送一本（背包放不下进仓库），基地售货机从此上架收藏册。
// 目标：用收藏册登记任意一件收集品；完成后回基地跟杰夫说说。没有提交物、金钱或额外奖品。
// 事实在 BossRush_CollectionBook_v1（jeffAccepted / jeffDone），官方任务页只是投影（口径同 590216）。
// 杰夫的话讲人话：说见闻和来由，不报概率、格子这类机制。
// ============================================================================

using System;
using Duckov.Quests;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    internal sealed class CollectionJeffIntro : IOfficialQuestClient
    {
        internal const int QuestId = 590217;
        private const string NameKey = "BossRush_CollectionBook_Jeff_Name";
        private const string DescriptionKey = "BossRush_CollectionBook_Jeff_Description";

        private readonly CollectiblesRuntimeModule module;
        private readonly OfficialQuestProjection projection;
        private QuestGiver jeff;
        private float nextJeffAttempt;
        private bool deliveryActive;

        internal CollectionJeffIntro(CollectiblesRuntimeModule owner, OfficialQuestProjection questProjection)
        {
            module = owner;
            projection = questProjection;
            InjectLocalizations();
            if (projection != null) projection.Register(BuildBinding());
            else Debug.LogWarning(CollectibleTuning.LogPrefix + "官方任务核心尚未准备，杰夫的收藏册引导未注册");
        }

        internal static void InjectLocalizations()
        {
            LocalizationHelper.InjectLocalization(NameKey, L10n.T("压箱底的老物件", "Odds and Ends"));
            LocalizationHelper.InjectLocalization(DescriptionKey, L10n.T(
                "最近搜箱子的都说，翻着翻着会多出样怪东西：老票根、旧徽章、谁家传下来的小摆件。卖了能换几个钱，可我有个爱收藏的老伙计托我留意，说凑成一整套的，他出大价钱收。这本册子你拿着，觉得值得留的就登进去。",
                "Folks who've been digging through crates keep turning up odd little things: old ticket stubs, worn badges, somebody's heirloom knick-knacks. You can sell them for a bit, but an old collector friend asked me to keep an eye out. He pays well for a full set. Take this album and log anything you think is worth keeping."));
        }

        private CollectionBookData Data { get { return module == null ? null : module.Data; } }

        private OfficialQuestBinding BuildBinding()
        {
            return new OfficialQuestBinding
            {
                QuestId = QuestId, GiverId = (int)QuestGiverID.Jeff,
                ObjectName = "BossRush_CollectionBook_Jeff_OfficialQuest",
                NameKey = NameKey, DescriptionKey = DescriptionKey, Client = this,
                CanOffer = CanOffer, CanDeliver = CanDeliver,
                IsAccepted = () => Data != null && Data.JeffAccepted && !Data.JeffDone,
                IsDelivered = () => Data != null && Data.JeffDone,
                RewardPaid = () => Data != null && Data.JeffDone,
                StateStamp = () => Data == null ? 0
                    : (Data.JeffAccepted ? 1 : 0) | (Data.Registered.Count > 0 ? 2 : 0) | (Data.JeffDone ? 4 : 0),
                Accept = Accept, Deliver = Deliver, BeginDelivery = BeginDelivery, EndDelivery = EndDelivery,
                DeliverBlocked = () => L10n.T("先用收藏册登记一件收集品。", "Log one collectible in the album first."),
                Tasks = new[] { new OfficialQuestTaskBinding
                {
                    TaskId = 1, Done = () => Data != null && Data.Registered.Count > 0,
                    Description = () => Data != null && Data.Registered.Count > 0
                        ? L10n.T("回基地跟杰夫说说", "Go back to base and tell Jeff")
                        : L10n.T("用收藏册登记任意一件收集品", "Log any collectible in the album"),
                    ExtraHint = () => L10n.T("在基地翻开收藏册，点「登记」把收集品放进去。", "Open the album at base and press Register to put collectibles in.")
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

        private bool CanOffer()
        {
            CollectionBookData d = Data;
            return d != null && module.Writable && !d.JeffAccepted && !d.JeffDone && InBase() && PlayerAlive();
        }

        private bool CanDeliver()
        {
            CollectionBookData d = Data;
            return d != null && module.Writable && d.JeffAccepted && !d.JeffDone && d.Registered.Count > 0 && InBase() && PlayerAlive();
        }

        private bool Accept(out string message)
        {
            message = L10n.T("这事待会儿再说。", "Let's talk about this later.");
            if (!CanOffer() || !module.CommitJeff(true, false)) return false;
            BookDelivery given = GiveBook();
            module.OnBookUnlocked();
            // 杰夫照实说册子去了哪：背包、仓库，还是一时没拿出来（CR-2026-11-19-005）。
            if (given == BookDelivery.Backpack)
                message = L10n.T("册子你拿着。登进去的东西就归册子了，舍不得的先别登。",
                    "Here's the album. Whatever you log stays in it, so hold off on anything you can't part with.");
            else if (given == BookDelivery.Storage)
                message = L10n.T("你身上塞不下了，册子我放你仓库里了。登进去的东西就归册子了，舍不得的先别登。",
                    "You've got no room on you, so I put the album in your storage. Whatever you log stays in it, so hold off on anything you can't part with.");
            else
                message = L10n.T("册子一时找不着了，你去售货机拿一本。登进去的东西就归册子了。",
                    "I can't lay my hands on the album right now. Grab one from the vending machine. Whatever you log stays in it.");
            NotifyChanged();
            return true;
        }

        private bool Deliver(out string message)
        {
            message = L10n.T("还没登过东西吧？", "You haven't logged anything yet, have you?");
            if (!CanDeliver() || !module.CommitJeff(true, true)) return false;
            message = L10n.T("登上了？行，往后凑齐一页，那老伙计会把钱送来。", "Logged one? Good. Fill a page and the old collector will send the money over.");
            NotifyChanged();
            return true;
        }

        private bool BeginDelivery(Func<bool> collectAssets, out string message)
        {
            message = L10n.T("这事待会儿再说。", "Let's talk about this later.");
            if (deliveryActive || !CanDeliver()) return false;
            // 无提交物、现金与奖品：只防重入（口径同 590216）。
            deliveryActive = true;
            message = null;
            return true;
        }

        private void EndDelivery(bool committed) { deliveryActive = false; }

        private enum BookDelivery { Failed, Backpack, Storage }

        /// <summary>送一本收藏册：先进背包，放不下进仓库；实例化不出真物品就不送（售货机已上架）。</summary>
        private static BookDelivery GiveBook()
        {
            try
            {
                if (!CollectionBookItem.EnsureRuntimeRegistration()) return BookDelivery.Failed;
                Item book = ItemAssetsCollection.InstantiateSync(CollectionBookItem.TypeId);
                if (book == null) return BookDelivery.Failed;
                if (book.TypeID != CollectionBookItem.TypeId) { book.DestroyTree(); return BookDelivery.Failed; }
                if (ItemUtilities.SendToPlayerCharacterInventory(book, false)) return BookDelivery.Backpack;
                ItemUtilities.SendToPlayerStorage(book, false);
                return BookDelivery.Storage;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "送收藏册失败: " + e.Message);
                return BookDelivery.Failed;
            }
        }

        /// <summary>登记、接取、交付之后刷新杰夫头顶的标记与任务页。</summary>
        internal void NotifyChanged()
        {
            if (projection != null) projection.NotifyProgressChanged(this);
        }

        /// <summary>模块每帧调：在基地时找一次杰夫，交给投影核心挂任务。</summary>
        internal void Tick()
        {
            if (!InBase()) { jeff = null; return; }
            if (jeff != null || Time.unscaledTime < nextJeffAttempt) return;
            nextJeffAttempt = Time.unscaledTime + 1f;
            if (OfficialQuestGiverLocator.TryFind(QuestGiverID.Jeff, out jeff) && projection != null) projection.PrepareGiver(jeff);
        }

        internal void OnSlotChanged()
        {
            jeff = null;
            nextJeffAttempt = 0f;
            deliveryActive = false;
        }

        string IOfficialQuestClient.LogTag { get { return "[CollectionJeff]"; } }
        bool IOfficialQuestClient.Ready { get { return Data != null && module.Writable; } }
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
