// Mode E merchant runtime: ModeEMerchantSellAllUI_ShopViewSetup.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using TMPro;
using HarmonyLib;
using SodaCraft.StringUtilities;

namespace BossRush
{
    internal static partial class ModeEMerchantSellAllUI
    {
        internal static bool CanReuseShopViewSetup(StockShopView shopView, StockShop shop)
        {
            if (shopView == null || shop == null ||
                !object.ReferenceEquals(shopView.Target, shop))
            {
                return false;
            }

            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null)
            {
                return false;
            }

            ModeEShellShopPatchDisposition disposition =
                inst.GetModeEShellShopPatchDisposition(shop);
            if (disposition == ModeEShellShopPatchDisposition.HandleModeE)
            {
                return !object.ReferenceEquals(modeEMerchantProgressiveShop, shop) ||
                       modeEMerchantProgressivePopulationComplete;
            }

            // Mode F 复用同一分类 StockShop，但不进入贝壳交易边界。
            return disposition == ModeEShellShopPatchDisposition.PassOriginal &&
                   inst.IsModeFActive &&
                   !string.IsNullOrEmpty(shop.MerchantID) &&
                   shop.MerchantID.StartsWith("ModeE_", StringComparison.Ordinal);
        }

        internal static void BeginShopViewSetup(StockShop shop)
        {
            modeEMerchantShopViewSetupInProgress = IsBossRushModeCategoryShop(shop);
        }

        internal static void EndShopViewSetup()
        {
            modeEMerchantShopViewSetupInProgress = false;
        }

        internal static ProgressiveShopViewSetupState PrepareProgressiveShopViewSetup(
            StockShopView shopView,
            StockShop shop)
        {
            if (shopView == null || shop == null || shop.entries == null)
            {
                return null;
            }

            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null ||
                inst.GetModeEShellShopPatchDisposition(shop) !=
                    ModeEShellShopPatchDisposition.HandleModeE)
            {
                return null;
            }

            InitializeReflection();
            ProgressiveShopViewSetupState state = new ProgressiveShopViewSetupState
            {
                Shop = shop,
                TotalEntryCount = shop.entries.Count,
                SetupStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                Restored = false
            };
            TryRecycleActiveModeEShopEntries(shopView, state);

            if (shop.entries.Count <= MODE_E_SHOP_INITIAL_ENTRY_COUNT)
            {
                return state;
            }

            if (stockShopEntryPoolField == null ||
                stockShopEntryPoolField.FieldType != typeof(PrefabPool<StockShopItemEntry>) ||
                stockShopItemEntrySetup == null)
            {
                // Contract drift falls back to the complete original Setup.
                return state;
            }

            List<StockShop.Entry> originalEntries = shop.entries;
            List<StockShop.Entry> initialEntries = new List<StockShop.Entry>(
                MODE_E_SHOP_INITIAL_ENTRY_COUNT);
            for (int i = 0; i < MODE_E_SHOP_INITIAL_ENTRY_COUNT; i++)
            {
                initialEntries.Add(originalEntries[i]);
            }

            long populationID = NextProgressivePopulationID();
            modeEMerchantProgressiveShop = shop;
            modeEMerchantProgressivePopulationComplete = false;
            shop.entries = initialEntries;
            state.OriginalEntries = originalEntries;
            state.InitialEntryCount = initialEntries.Count;
            state.PopulationID = populationID;
            return state;
        }

        internal static void CompleteProgressiveShopViewSetup(
            StockShopView shopView,
            StockShop shop,
            ProgressiveShopViewSetupState state,
            bool setupSucceeded)
        {
            if (state == null)
            {
                if (setupSucceeded && shopView != null && shop != null &&
                    object.ReferenceEquals(shopView.Target, shop))
                {
                    modeEMerchantProgressiveShop = shop;
                    modeEMerchantProgressivePopulationComplete = true;
                }
                return;
            }

            if (state.Restored) return;
            state.Restored = true;

            try
            {
                if (state.Shop != null && state.OriginalEntries != null)
                {
                    state.Shop.entries = state.OriginalEntries;
                }
                RestoreModeEShopEntryContentRoot(state);
                LogModeEShopSynchronousSetup(state);

                if (!setupSucceeded || shopView == null || state.Shop == null ||
                    !object.ReferenceEquals(shopView.Target, state.Shop))
                {
                    return;
                }

                if (state.OriginalEntries == null)
                {
                    modeEMerchantProgressiveShop = state.Shop;
                    modeEMerchantProgressivePopulationComplete = true;
                    return;
                }

                PopulateRemainingModeEShopEntriesAsync(shopView, state).Forget();
            }
            catch (Exception e)
            {
                modeEMerchantProgressivePopulationComplete = false;
                ModBehaviour.DevLog("[ModeE] [WARNING] 商店分帧加载启动失败: " + e.Message);
            }
        }

        private static void TryRecycleActiveModeEShopEntries(
            StockShopView shopView,
            ProgressiveShopViewSetupState state)
        {
            if (shopView == null || state == null ||
                stockShopEntryPoolField == null ||
                stockShopEntryPoolField.FieldType != typeof(PrefabPool<StockShopItemEntry>) ||
                stockShopPoolActiveEntriesField == null ||
                stockShopPoolActiveEntriesField.FieldType != typeof(List<StockShopItemEntry>))
            {
                return;
            }

            PrefabPool<StockShopItemEntry> entryPool =
                stockShopEntryPoolField.GetValue(shopView) as PrefabPool<StockShopItemEntry>;
            if (entryPool == null) return;

            List<StockShopItemEntry> activeEntries =
                stockShopPoolActiveEntriesField.GetValue(entryPool) as List<StockShopItemEntry>;
            if (activeEntries == null || activeEntries.Count == 0) return;

            Transform contentRoot = entryPool.poolParent;
            if (contentRoot != null && contentRoot.gameObject != null)
            {
                state.EntryContentRoot = contentRoot.gameObject;
                state.EntryContentRootWasActive = state.EntryContentRoot.activeSelf;
                if (state.EntryContentRootWasActive)
                {
                    state.EntryContentRoot.SetActive(false);
                }
            }

            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            activeEntries.RemoveAll(entry => entry == null);
            StockShopItemEntry[] entriesToRelease = activeEntries.ToArray();
            activeEntries.Clear();

            int released = 0;
            try
            {
                for (int i = 0; i < entriesToRelease.Length; i++)
                {
                    entryPool.Release(entriesToRelease[i]);
                    released++;
                }
            }
            catch (Exception e)
            {
                // Restore unreleased ownership so the original ReleaseAll can finish safely.
                for (int i = released; i < entriesToRelease.Length; i++)
                {
                    StockShopItemEntry entry = entriesToRelease[i];
                    if (entry != null && !activeEntries.Contains(entry))
                    {
                        activeEntries.Add(entry);
                    }
                }
                ModBehaviour.DevLog("[ModeE] [WARNING] 商店对象池线性回收失败，回退原版: " + e.Message);
            }

            state.RecycledEntryCount = released;
            state.RecycleElapsedMilliseconds = GetElapsedMilliseconds(started);
        }

        private static void RestoreModeEShopEntryContentRoot(
            ProgressiveShopViewSetupState state)
        {
            if (state == null || state.EntryContentRoot == null ||
                !state.EntryContentRootWasActive)
            {
                return;
            }

            try { state.EntryContentRoot.SetActive(true); }
            catch { }
        }

        private static long GetElapsedMilliseconds(long startedTimestamp)
        {
            long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - startedTimestamp;
            if (elapsedTicks <= 0L) return 0L;
            return (long)(elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }

        private static void LogModeEShopSynchronousSetup(
            ProgressiveShopViewSetupState state)
        {
            if (state == null || state.SetupStartedTimestamp <= 0L) return;

            long totalMilliseconds = GetElapsedMilliseconds(state.SetupStartedTimestamp);
            if (totalMilliseconds < 16L && state.RecycledEntryCount == 0) return;

            string merchantID = state.Shop != null ? state.Shop.MerchantID : "<null>";
            ModBehaviour.DevLog(
                "[ModeE] [Profile] shop setup sync: merchant=" + merchantID +
                ", items=" + state.TotalEntryCount +
                ", recycled=" + state.RecycledEntryCount +
                ", recycleMs=" + state.RecycleElapsedMilliseconds +
                ", totalMs=" + totalMilliseconds);
        }

        private static long NextProgressivePopulationID()
        {
            if (modeEMerchantProgressivePopulationID == long.MaxValue)
            {
                modeEMerchantProgressivePopulationID = 1L;
            }
            else
            {
                modeEMerchantProgressivePopulationID++;
                if (modeEMerchantProgressivePopulationID <= 0L)
                {
                    modeEMerchantProgressivePopulationID = 1L;
                }
            }
            return modeEMerchantProgressivePopulationID;
        }

        private static void CancelProgressiveShopViewPopulation(StockShop shop)
        {
            if (shop == null || !object.ReferenceEquals(modeEMerchantProgressiveShop, shop))
            {
                return;
            }
            NextProgressivePopulationID();
        }

        private static bool IsCurrentProgressivePopulation(
            StockShopView shopView,
            ProgressiveShopViewSetupState state)
        {
            return state != null &&
                   state.PopulationID == modeEMerchantProgressivePopulationID &&
                   shopView != null &&
                   state.Shop != null &&
                   state.OriginalEntries != null &&
                   object.ReferenceEquals(shopView.Target, state.Shop) &&
                   object.ReferenceEquals(state.Shop.entries, state.OriginalEntries);
        }

        private static async UniTask PopulateRemainingModeEShopEntriesAsync(
            StockShopView shopView,
            ProgressiveShopViewSetupState state)
        {
            int loaded = 0;
            int failed = 0;
            await UniTask.Yield();

            try
            {
                if (!IsCurrentProgressivePopulation(shopView, state)) return;

                PrefabPool<StockShopItemEntry> entryPool =
                    stockShopEntryPoolField.GetValue(shopView) as PrefabPool<StockShopItemEntry>;
                if (entryPool == null)
                {
                    modeEMerchantProgressivePopulationComplete = false;
                    ModBehaviour.DevLog("[ModeE] [WARNING] 商店分帧加载无法获取 EntryPool");
                    return;
                }
                ModBehaviour owner = ModBehaviour.Instance;

                for (int i = state.InitialEntryCount; i < state.OriginalEntries.Count; i++)
                {
                    if (!IsCurrentProgressivePopulation(shopView, state)) return;

                    StockShop.Entry entry = state.OriginalEntries[i];
                    if (entry == null || !entry.Show) continue;

                    StockShopItemEntry itemEntry = null;
                    try
                    {
                        itemEntry = entryPool.Get();
                        stockShopItemEntrySetup(itemEntry, shopView, entry);
                        if (owner != null)
                        {
                            owner.ApplyModeEShellItemEntryUi(itemEntry, shopView, entry);
                        }
                        itemEntry.transform.SetAsLastSibling();
                        loaded++;
                    }
                    catch
                    {
                        failed++;
                        if (itemEntry != null)
                        {
                            try { entryPool.Release(itemEntry); } catch { }
                        }
                    }

                    if ((loaded + failed) % MODE_E_SHOP_ENTRIES_PER_FRAME == 0)
                    {
                        await UniTask.Yield();
                    }
                }

                if (!IsCurrentProgressivePopulation(shopView, state)) return;
                modeEMerchantProgressivePopulationComplete = true;
                ModBehaviour.DevLog(
                    "[ModeE] 商店首开分帧加载完成: initial=" + state.InitialEntryCount +
                    ", deferred=" + loaded + ", failed=" + failed);
            }
            catch (Exception e)
            {
                modeEMerchantProgressivePopulationComplete = false;
                ModBehaviour.DevLog("[ModeE] [WARNING] 商店分帧加载失败: " + e.Message);
            }
        }

        internal static bool CanReuseInventoryDisplaySetup(
            Duckov.UI.InventoryDisplay display,
            Inventory target)
        {
            return modeEMerchantShopViewSetupInProgress &&
                   display != null &&
                   target != null &&
                   object.ReferenceEquals(display.Target, target);
        }

    }
}
