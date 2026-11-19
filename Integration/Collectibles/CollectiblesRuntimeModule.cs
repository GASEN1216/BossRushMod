// ============================================================================
// CollectiblesRuntimeModule.cs - 收集品子系统的唯一 owner（AGENTS.md §4.15）
// ============================================================================
// 持有：收藏册存档、开箱掉落、系列加成、杰夫引导 590217、收藏册界面与登记容器、基地售货机上架、6–8 档格子边框。
// 订阅只有一处（CollectibleLootRuntime 的 OnStartLoot），OnDestroy 统一退订与清理（§4.6）。
// 每帧成本：存档协调器 Tick + 登记容器 Tick + 主角引用比较 + 基地里找一次杰夫，系统空闲时都是 O(1) 早返（§4.12）。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Economy;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    internal sealed class CollectiblesRuntimeModule : BossRushRuntimeModuleBase
    {
        internal static CollectiblesRuntimeModule Current { get; private set; }

        public override string ModuleName { get { return "Collectibles"; } }

        private ModBehaviour owner;
        private CollectionBookPersistence persistence;
        private CollectionBookData data;
        private int openedSlot = -1;
        private CollectibleLootRuntime loot;
        private readonly CollectionSeriesBonus bonus = new CollectionSeriesBonus();
        private CollectionJeffIntro jeffIntro;
        private CollectionRegisterContainer container;
        private bool registering;
        private bool reopenBookPending;
        private int reopenBookAfterFrame;
        private bool shopInjectedThisScene;
        private float nextShopAttempt;

        public override void OnAwake(ModBehaviour host)
        {
            owner = host;
            Current = this;
            persistence = new CollectionBookPersistence(OnSlotChanged, () => !registering);
            loot = new CollectibleLootRuntime(host);
            container = new CollectionRegisterContainer(RegisterItems, IsRegistered, OnRegisterClosed);
            OfficialQuestProjection projection = host != null && host.OfficialQuestRuntime != null ? host.OfficialQuestRuntime.Projection : null;
            jeffIntro = new CollectionJeffIntro(this, projection);
        }

        // ---------------- 事实 ----------------

        /// <summary>当前槽的收藏册事实；还没就绪（切图中、没有槽位）时为 null。</summary>
        internal CollectionBookData Data { get { return EnsureData() ? data : null; } }

        internal bool Writable { get { return persistence != null && persistence.Writable; } }

        private bool EnsureData()
        {
            if (persistence == null || !LevelManager.AfterInit) return false;
            int slot;
            try { slot = SavesSystem.CurrentSlot; }
            catch (Exception) { return false; }
            if (slot < 0) return false;
            if (data == null || openedSlot != slot)
            {
                data = persistence.Load();
                openedSlot = slot;
            }
            return data != null && !persistence.HasWriteBarrier;
        }

        private void OnSlotChanged()
        {
            data = null;
            openedSlot = -1;
            shopInjectedThisScene = false;
            if (jeffIntro != null) jeffIntro.OnSlotChanged();
            if (container != null && container.IsOpen) container.CloseAndReturn(false);
            reopenBookPending = false;
            CollectionBookView.CloseIfOpen();
        }

        private bool Commit(CollectionBookData value)
        {
            if (persistence == null || value == null || !persistence.Store(value)) return false;
            data = value;
            return true;
        }

        internal bool CommitJeff(bool accepted, bool done)
        {
            if (!EnsureData()) return false;
            CollectionBookData next = data.Clone();
            next.JeffAccepted = accepted;
            next.JeffDone = done;
            return Commit(next);
        }

        internal bool IsRegistered(int typeId)
        {
            CollectionBookData d = Data;
            return d != null && d.Registered.Contains(typeId);
        }

        // ---------------- 登记 ----------------

        /// <summary>登记只在基地、存档可写时开放。</summary>
        internal bool CanRegisterNow
        {
            get
            {
                LevelManager level = LevelManager.Instance;
                return level != null && level.IsBaseLevel && !SceneLoader.IsSceneLoading && Data != null && Writable;
            }
        }

        /// <summary>收起收藏册、打开单格登记容器（送礼同款）：放进一件，点「登记」才登记。</summary>
        internal bool OpenRegisterContainer()
        {
            if (!CanRegisterNow || container == null || container.IsOpen) return false;
            reopenBookPending = false;
            CollectionBookView.CloseIfOpen();
            return container.Open();
        }

        /// <summary>玩家关上登记容器：这一趟登记过东西就回到收藏册看刚点亮的卡片（下一帧再开，不和关界面的取消键抢同一次输入）。</summary>
        private void OnRegisterClosed(bool registeredAny)
        {
            reopenBookPending = registeredAny;
            reopenBookAfterFrame = Time.frameCount;
        }

        private void TickReopenBook()
        {
            if (!reopenBookPending || Time.frameCount <= reopenBookAfterFrame) return;
            reopenBookPending = false;
            if (CanRegisterNow && (container == null || !container.IsOpen)) CollectionBookView.Show(this);
        }

        /// <summary>
        /// 登记事务：先算出这次能收的收集品和新集齐的系列，写进存档；写成功才发奖金、收物品。
        /// 返回应当收走的物品；不能登记或存档写不进去时返回 null（物品留在格子里，关上时退回）。
        /// </summary>
        private List<Item> RegisterItems(List<Item> items)
        {
            if (items == null || items.Count == 0 || !EnsureData() || !Writable) return null;
            registering = true;
            try
            {
                var typeIds = new List<int>(items.Count);
                foreach (Item item in items) typeIds.Add(item != null ? item.TypeID : 0);
                CollectionRegistrationPlan plan = CollectionRegistration.Plan(data, typeIds);
                if (!plan.HasWork)
                {
                    ShowMessage(L10n.T("这件已经登记过了，或者不是收集品。", "That one is already logged or isn't a collectible."));
                    return null;
                }
                var taken = new List<Item>();
                var newNames = new List<string>();
                foreach (int index in plan.AcceptedIndices)
                {
                    taken.Add(items[index]);
                    newNames.Add(CollectiblesLocalization.Name(CollectibleCatalog.Get(typeIds[index])));
                }

                // 先写存档，写成功才发奖金、提示、刷新任务；写成功之后无论哪一步出错，物品都照常收走。
                bool committed = CollectionRegistration.Apply(plan, Commit,
                    warning => Debug.LogWarning(CollectibleTuning.LogPrefix + "登记后续步骤异常（物品照常收走）: " + warning),
                    () => PayCash(plan.Cash),
                    () => AnnounceRegistered(newNames, plan.Completed, plan.FullNow),
                    () => { if (jeffIntro != null) jeffIntro.NotifyChanged(); });
                if (!committed)
                {
                    ShowMessage(L10n.T("收藏册这会儿写不进去，这件先留在格子里。", "The album couldn't be updated right now. The item stays in the slot."));
                    return null;
                }
                return taken;
            }
            finally
            {
                registering = false;
                if (persistence != null) persistence.RequestFlush();
            }
        }

        private static void PayCash(long cash)
        {
            if (cash > 0 && !EconomyManager.Add(cash)) Debug.LogWarning(CollectibleTuning.LogPrefix + "集齐奖金发放失败: " + cash);
        }

        private void AnnounceRegistered(List<string> names, List<CollectibleSeries> completed, bool fullNow)
        {
            string text = L10n.T("登进收藏册：", "Logged in the album: ") + string.Join(L10n.T("、", ", "), names.ToArray());
            ShowMessage(text);
            foreach (CollectibleSeries series in completed)
            {
                CollectibleSeriesInfo info = CollectibleCatalog.GetSeries(series);
                CollectibleTuning.SeriesBonus b = CollectibleTuning.BonusOf(series);
                ShowMessage(L10n.T("「", "\"") + (info != null ? info.Name : string.Empty) + L10n.T("」凑齐了：奖金 ", "\" complete: ")
                    + CollectibleTuning.SeriesCash(series).ToString("N0") + L10n.T("，", " cash, ") + (b != null ? b.Text : string.Empty));
            }
            if (fullNow)
                ShowMessage(L10n.T("整本收藏册都凑齐了。那位老收藏家寄来奖金 ", "The whole album is complete. The old collector sent ")
                    + CollectibleTuning.AllSeriesCash.ToString("N0") + L10n.T("。", " cash."));
        }

        private void ShowMessage(string text)
        {
            try { if (owner != null) owner.ShowMessage(text); }
            catch (Exception) { /* 提示失败不影响登记 */ }
        }

        // ---------------- 界面与售货机 ----------------

        internal void ToggleBook()
        {
            CollectionBookView.Toggle(this);
        }

        /// <summary>接过引导：本次基地场景立刻给在场售货机补挂收藏册。</summary>
        internal void OnBookUnlocked()
        {
            shopInjectedThisScene = false;
            nextShopAttempt = 0f;
        }

        private void TickShop()
        {
            if (shopInjectedThisScene || owner == null || Time.unscaledTime < nextShopAttempt) return;
            LevelManager level = LevelManager.Instance;
            if (level == null || !level.IsBaseLevel || SceneLoader.IsSceneLoading) return;
            CollectionBookData d = Data;
            if (d == null || !d.BookUnlocked) return;
            nextShopAttempt = Time.unscaledTime + 2f;
            StockShop[] shops = ObjectCache.GetStockShops();
            if (shops == null || shops.Length == 0) return;
            bool stocked = false;
            for (int i = 0; i < shops.Length; i++)
            {
                if (shops[i] != null && CollectionBookItem.TryInjectIntoShop(shops[i], owner)) stocked = true;
            }
            // 真挂上（或本来就在）才收工；售货机还没认出来、克隆注册一时失败时，按 2 秒节流再试（CR-2026-11-19-004）。
            if (stocked) shopInjectedThisScene = true;
        }

        // ---------------- 模式门控 ----------------

        /// <summary>BossRush 各模式、丧尸、鸭王杯、征程竞技场进行中：收集品不参与（口径同 EclipseBloomRuntimeModule）。</summary>
        internal static bool OtherModeActive(ModBehaviour host)
        {
            if (host == null) return true;
            try
            {
                if (host.IsAnyBossRushLikeModeActive() || host.IsActive || host.IsCampaignFinalBossActive
                    || host.CampaignArenaActiveForRuntime || WavesArenaRuntimeModule.ArenaPlanned
                    || WavesArenaRuntimeModule.ArenaActive
                    || BossRushMapSelectionHelper.GetPendingEntryKind() != BossRushPendingEntryKind.None
                    || host.CampaignModeDActiveForRuntime || host.CampaignModeEActiveForRuntime
                    || host.CampaignModeFActiveForRuntime || host.CampaignModeGActiveForRuntime) return true;
                if (host.CampaignZombieRunForRuntime != null
                    && host.CampaignZombieRunForRuntime.LifecyclePhase != ZombieModeLifecyclePhase.None) return true;
                return host.ModeHRuntime != null && host.ModeHRuntime.RunState != null
                    && host.ModeHRuntime.RunState.Lifecycle != ModeHLifecycle.None;
            }
            catch (Exception)
            {
                return true;
            }
        }

#if BOSSRUSH_DEV
        // ---------------- F3 Dev（只在 Dev 构建里存在） ----------------

        /// <summary>把一件收集品放进背包（放不下进仓库）。</summary>
        internal bool DevGive(int typeId)
        {
            Item item = CollectibleItems.InstantiateChecked(typeId);
            if (item == null) return false;
            if (!ItemUtilities.SendToPlayerCharacterInventory(item, false)) ItemUtilities.SendToPlayerStorage(item, false);
            return true;
        }

        /// <summary>收藏册事实整份改写：全登记（不发奖金、不收物品）或清空（连同杰夫引导）。只给专用测试档用。</summary>
        internal bool DevSetAll(bool registerAll, out string detail)
        {
            detail = null;
            if (!EnsureData() || !Writable) { detail = L10n.T("存档不可写", "Save not writable"); return false; }
            CollectionBookData next = new CollectionBookData();
            if (registerAll)
            {
                next.JeffAccepted = data.JeffAccepted;
                next.JeffDone = data.JeffDone;
                for (int i = 0; i < CollectibleCatalog.Count; i++) next.Registered.Add(CollectibleCatalog.At(i).TypeId);
                for (int s = 0; s < CollectibleCatalog.SeriesCount; s++)
                    if (CollectibleCatalog.GetSeries((CollectibleSeries)s).CountsForSet) next.PaidSeries.Add(s);
                next.FullPaid = true;
            }
            if (!Commit(next)) { detail = L10n.T("写入失败", "Write failed"); return false; }
            if (persistence != null) persistence.RequestFlush();
            if (jeffIntro != null) jeffIntro.NotifyChanged();
            return true;
        }
#endif

        // ---------------- 生命周期 ----------------

        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            shopInjectedThisScene = false;
            nextShopAttempt = 0f;
            if (container != null && container.IsOpen) container.CloseAndReturn(false);
            reopenBookPending = false;
            // 过图收起收藏册（口径同图鉴）：登记按钮只在基地挂，开着跨图不会重算，模态租约也不该跨场景持有。
            CollectionBookView.CloseIfOpen();
            CollectibleSlotFrame.ClearFreshDrops();
        }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (persistence != null && !registering) persistence.Tick();
            if (container != null) container.Tick();
            TickReopenBook();
            if (!EnsureData()) return;
            bonus.Tick(data);
            if (jeffIntro != null) jeffIntro.Tick();
            TickShop();
        }

        public override void OnDestroy()
        {
            if (container != null && container.IsOpen) container.CloseAndReturn(false);
            container = null;
            reopenBookPending = false;
            CollectionBookView.ResetStaticCaches();
            CollectibleSlotFrame.ResetStaticCaches();
            CollectibleShowcaseModels.ResetStaticCaches();
            bonus.Remove();
            if (jeffIntro != null) jeffIntro.Dispose();
            jeffIntro = null;
            if (loot != null) loot.Dispose();
            loot = null;
            if (persistence != null) persistence.Dispose();
            persistence = null;
            data = null;
            openedSlot = -1;
            CollectibleLootRoller.ResetStaticCaches();
            CollectibleLootRuntime.ResetStaticCaches();
            if (Current == this) Current = null;
            owner = null;
        }
    }
}
