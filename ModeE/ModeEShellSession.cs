// Mode E merchant runtime: ModeEShellSession.cs
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
    internal sealed partial class ModeERuntimeModule
    {
        private bool HasModeEShellSessionState()
        {
            return modeEShellSessionToken > 0 ||
                   modeEShellEconomyAvailable ||
                   modeEMerchantShopGenerations.Count > 0;
        }

        private void DisableModeEShellShopInteraction(StockShop shop)
        {
            if (shop == null) return;

            try
            {
                ModeEShopInteractable[] interactables = shop.GetComponents<ModeEShopInteractable>();
                for (int i = 0; i < interactables.Length; i++)
                {
                    if (interactables[i] != null) interactables[i].enabled = false;
                }
            }
            catch { }

            try { shop.enabled = false; } catch { }
            try
            {
                if (shop.gameObject != null) shop.gameObject.SetActive(false);
            }
            catch { }
        }

        private System.Collections.IEnumerator ConfirmRetiredModeEShellShopsDestroyedNextFrame(
            StockShop[] retired)
        {
            yield return null;

            if (retired == null) yield break;
            for (int i = 0; i < retired.Length; i++)
            {
                StockShop shop = retired[i];
                if (shop == null)
                {
                    modeEOwnedShopTombstones.Remove(shop);
                    continue;
                }

                try
                {
                    if (shop.gameObject != null) UnityEngine.Object.Destroy(shop.gameObject);
                }
                catch { }
            }

            yield return null;
            for (int i = 0; i < retired.Length; i++)
            {
                StockShop shop = retired[i];
                if (shop == null)
                {
                    modeEOwnedShopTombstones.Remove(shop);
                }
            }
        }

        private void SetModeEShellEconomyUnavailable(string reason, bool cleanupExistingShops)
        {
            bool wasAvailable = modeEShellEconomyAvailable;
            modeEShellEconomyAvailable = false;
            if (wasAvailable || !string.IsNullOrEmpty(reason))
            {
                ModBehaviour.DevLog("[ModeE/Shell] capability disabled: " + reason);
            }

            if (!cleanupExistingShops || modeEMerchantShops.Count == 0)
            {
                return;
            }

            for (int i = 0; i < modeEMerchantShops.Count; i++)
            {
                DisableModeEShellShopInteraction(modeEMerchantShops[i]);
            }

            if (modeEShellTransactionOwner != null)
            {
                modeEShellFatalCleanupPending = true;
            }
            else
            {
                InvalidateModeEShellMerchantGeneration("capability disabled: " + reason);
            }
        }

        private void FailModeEShellEconomyDuringCommittedTransaction(string reason)
        {
            modeEShellEconomyAvailable = false;
            modeEShellFatalCleanupPending = true;
            for (int i = 0; i < modeEMerchantShops.Count; i++)
            {
                DisableModeEShellShopInteraction(modeEMerchantShops[i]);
            }
            ModBehaviour.DevLog("[ModeE/Shell] committed transaction contract failure: " + reason);
        }

        private void InvalidateModeEShellUiBinding(string reason)
        {
            modeEShellActiveUiBindingID = 0L;
            modeEShellActiveUiShop = null;
            if (ModBehaviour.VerboseStartupDebugLogsEnabled)
            {
                ModBehaviour.DevLog("[ModeE/Shell] UI binding invalidated: " + reason);
            }
        }

        internal bool TryAttachModeEShellUi(StockShop shop, out long uiBindingID)
        {
            uiBindingID = 0L;
            InvalidateModeEShellUiBinding("Attach replaces previous owner");
            if (!IsCurrentModeEShellCapability(shop))
            {
                return false;
            }

            uiBindingID = NextModeEShellCounter(ref modeEShellNextUiBindingID);
            modeEShellActiveUiBindingID = uiBindingID;
            modeEShellActiveUiShop = shop;
            return true;
        }

        internal void DetachModeEShellUi(StockShop shop, long uiBindingID, string reason)
        {
            if (modeEShellActiveUiBindingID == uiBindingID &&
                object.ReferenceEquals(modeEShellActiveUiShop, shop))
            {
                InvalidateModeEShellUiBinding(reason);
            }
        }

        internal bool IsCurrentModeEShellUiBinding(StockShop shop, long uiBindingID)
        {
            return uiBindingID > 0L &&
                   modeEShellActiveUiBindingID == uiBindingID &&
                   object.ReferenceEquals(modeEShellActiveUiShop, shop) &&
                   IsCurrentModeEShellCapability(shop);
        }

        private bool BeginModeEShellMerchantGeneration(string reason)
        {
            InvalidateModeEShellMerchantGeneration(reason);
            if (!modeEShellEconomyAvailable || !VerifyModeEShellPatchInstallation())
            {
                SetModeEShellEconomyUnavailable("merchant preflight failed", true);
                return false;
            }

            return true;
        }

        private bool RegisterModeEShellMerchantShop(StockShop shop)
        {
            if (shop == null || !modeEShellEconomyAvailable ||
                !IsCurrentModeEShellSession(
                    modeEShellSessionToken,
                    modeEShellSessionScene,
                    modeEShellSessionGeneration))
            {
                return false;
            }

            // owned 身份先登记，再进入活动集合和交互组。
            modeEOwnedShopTombstones.Add(shop);
            modeEMerchantShopGenerations[shop] = modeEShellMerchantGeneration;
            modeEMerchantShops.Add(shop);
            return true;
        }

        private void InvalidateModeEShellMerchantGeneration(string reason)
        {
            long oldGeneration = modeEShellMerchantGeneration;
            modeEShellMerchantGeneration++;
            if (modeEShellMerchantGeneration <= 0L) modeEShellMerchantGeneration = 1L;

            ClearModeELotteryMerchantRuntime();
            ModeEMerchantSellAllUI.DetachForModeEShellInvalidation(modeEHost, reason);
            InvalidateModeEShellUiBinding(reason);

            StockShop[] retired = modeEMerchantShops.ToArray();
            for (int i = 0; i < retired.Length; i++)
            {
                StockShop shop = retired[i];
                if (shop == null) continue;
                modeEOwnedShopTombstones.Add(shop);
                DisableModeEShellShopInteraction(shop);
                try
                {
                    if (shop.gameObject != null) UnityEngine.Object.Destroy(shop.gameObject);
                }
                catch { }
            }

            modeEMerchantShops.Clear();
            modeEMerchantShopGenerations.Clear();
            modeEShellPriceCache.Clear();
            modeEShellPendingPriceKeys.Clear();

            ModeEShellTransactionOwner owner = modeEShellTransactionOwner;
            if (owner != null && owner.MerchantGeneration == oldGeneration)
            {
                ClearBusyAndReleaseModeEShellTransactionIfOwned(
                    owner,
                    "merchant generation invalidated",
                    false);
            }

            if (retired.Length > 0 && modeEHost != null)
            {
                try { modeEHost.StartCoroutine(ConfirmRetiredModeEShellShopsDestroyedNextFrame(retired)); }
                catch { }
            }
        }

        internal void InvalidateAndResetModeEShellSession(string reason)
        {
            InvalidateModeEShellMerchantGeneration(reason);

            modeEShellEconomyAvailable = false;
            modeEShellSessionToken = 0;
            modeEShellSessionScene = -1;
            modeEShellSessionGeneration++;
            if (modeEShellSessionGeneration <= 0L) modeEShellSessionGeneration = 1L;
            modeEShellBalance = 0;
            modeEShellFirstPositiveRewardGranted = false;
            modeEShellItemTypeID = -1;
            modeEShellFatalCleanupPending = false;
            modeEShellDebits.Clear();
            modeEShellRefundedTransactions.Clear();
            modeEShellCommittedTransactions.Clear();
            modeEShellTransactionUiBindings.Clear();
            modeEShellOriginalSellBypassArmed = false;
            modeEShellOriginalSellBypassTransactionID = 0L;
            modeEShellOriginalSellBypassShop = null;
        }

        internal void DestroyModeEShellRuntimeState()
        {
            InvalidateAndResetModeEShellSession("runtime destroy");
            StockShop[] tombstones = new StockShop[modeEOwnedShopTombstones.Count];
            modeEOwnedShopTombstones.CopyTo(tombstones);
            for (int i = 0; i < tombstones.Length; i++)
            {
                try
                {
                    StockShop shop = tombstones[i];
                    if (shop != null && shop.gameObject != null)
                    {
                        DisableModeEShellShopInteraction(shop);
                        // owner 正在 OnDestroy，无法依赖下一帧协程；同步销毁后再确认
                        // Unity-invalid，才能安全移除 retired blocker。
                        UnityEngine.Object.DestroyImmediate(shop.gameObject);
                    }
                }
                catch { }
            }

            bool allInvalid = true;
            for (int i = 0; i < tombstones.Length; i++)
            {
                if (tombstones[i] != null)
                {
                    allInvalid = false;
                    break;
                }
            }
            if (allInvalid)
            {
                modeEOwnedShopTombstones.Clear();
            }
            else
            {
                ModBehaviour.DevLog("[ModeE/Shell] runtime destroy retained live owned-shop tombstones");
            }
            ModeEShellBalanceChanged = null;
            ModeEShellPriceCacheChanged = null;
            ModeEShellTransactionGateChanged = null;
        }

        internal bool AreAllModeEShopOfficialSamplesReady(StockShop shop)
        {
            if (!IsCurrentModeEShellCapability(shop) || shop.entries == null)
            {
                return false;
            }

            for (int i = 0; i < shop.entries.Count; i++)
            {
                StockShop.Entry entry = shop.entries[i];
                if (entry == null) continue;
                Item sample = null;
                try { sample = shop.GetItemInstanceDirect(entry.ItemTypeID); }
                catch { return false; }
                if (sample == null) return false;
            }

            return true;
        }

        internal void ShowModeEShopLoadingFeedback()
        {
            try
            {
                NotificationText.Push(L10n.T(
                    "商店货物仍在载入，请稍后再试",
                    "Shop stock is still loading. Please try again shortly."));
            }
            catch { }
        }

        internal static bool IsModeEBulletStackShop(StockShop shop)
        {
            return shop != null &&
                   string.Equals(shop.MerchantID, "ModeE_Bullet", StringComparison.Ordinal);
        }

        internal void NormalizeModeEShellStackForShop(StockShop shop, Item item)
        {
            if (shop == null || item == null || !item.Stackable) return;
            if (!IsModeEBulletStackShop(shop)) return;
            if (item.StackCount < item.MaxStackCount)
            {
                item.StackCount = item.MaxStackCount;
            }
        }

        private bool TryCalculateModeEShellPrice(StockShop shop, Item sample, out int shellPrice)
        {
            shellPrice = 0;
            if (shop == null || sample == null) return false;

            try
            {
                NormalizeModeEShellStackForShop(shop, sample);
                int cashPrice = shop.ConvertPrice(sample, false);
                if (cashPrice <= 0) return false;
                long raw = cashPrice;
                if (IsModeEBulletStackShop(shop) && sample.Stackable && sample.StackCount > 1)
                {
                    raw *= MODE_E_SHELL_BULLET_STACK_PRICE_FACTOR;
                }
                long quantified = (raw + MODE_E_SHELL_CASH_UNIT - 1L) / MODE_E_SHELL_CASH_UNIT;
                if (quantified < 1L || quantified > int.MaxValue) return false;
                shellPrice = (int)quantified;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] price unavailable: " + e.Message);
                return false;
            }
        }

        private bool TryWriteModeEShellPrice(
            StockShop shop,
            int itemTypeID,
            long merchantGeneration,
            Item sample)
        {
            if (!IsCurrentModeEShellMerchantScope(shop, merchantGeneration) ||
                !modeEShellEconomyAvailable)
            {
                return false;
            }

            int price;
            if (!TryCalculateModeEShellPrice(shop, sample, out price))
            {
                return false;
            }

            ModeEShellPriceKey key = new ModeEShellPriceKey
            {
                Shop = shop,
                ItemTypeID = itemTypeID,
                MerchantGeneration = merchantGeneration
            };
            modeEShellPriceCache[key] = price;
            PublishModeEShellPriceChanged(shop, merchantGeneration, itemTypeID);
            return true;
        }

        internal bool TryGetModeEShellPrice(StockShop shop, int itemTypeID, out int price)
        {
            price = 0;
            if (!IsCurrentModeEShellCapability(shop)) return false;
            ModeEShellPriceKey key = new ModeEShellPriceKey
            {
                Shop = shop,
                ItemTypeID = itemTypeID,
                MerchantGeneration = modeEShellMerchantGeneration
            };
            return modeEShellPriceCache.TryGetValue(key, out price) && price > 0;
        }

        private static void DestroyModeEShellTemporarySample(Item sample)
        {
            if (sample == null) return;
            try { sample.Detach(); } catch { }
            try { sample.DestroyTree(); }
            catch
            {
                try { sample.MarkDestroyed(); } catch { }
                try
                {
                    if (sample != null && sample.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(sample.gameObject);
                    }
                }
                catch { }
            }
        }

        private async UniTask CacheSingleModeEShellPriceAsync(
            int sessionToken,
            int sceneBuildIndex,
            long sessionGeneration,
            long merchantGeneration,
            StockShop shop,
            int itemTypeID,
            ModeEShellPriceKey key)
        {
            Item temporarySample = null;
            try
            {
                if (!IsCurrentModeEShellSession(sessionToken, sceneBuildIndex, sessionGeneration) ||
                    !IsCurrentModeEShellMerchantScope(shop, merchantGeneration) ||
                    !modeEShellEconomyAvailable)
                {
                    return;
                }

                Item sample = null;
                try { sample = shop.GetItemInstanceDirect(itemTypeID); } catch { }
                if (sample == null)
                {
                    temporarySample = await ItemAssetsCollection.InstantiateAsync(itemTypeID);
                    sample = temporarySample;
                }

                if (!IsCurrentModeEShellSession(sessionToken, sceneBuildIndex, sessionGeneration) ||
                    !IsCurrentModeEShellMerchantScope(shop, merchantGeneration) ||
                    !modeEShellEconomyAvailable)
                {
                    return;
                }

                TryWriteModeEShellPrice(shop, itemTypeID, merchantGeneration, sample);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] lazy price task failed: type=" + itemTypeID + ", " + e.Message);
            }
            finally
            {
                if (temporarySample != null) DestroyModeEShellTemporarySample(temporarySample);
                modeEShellPendingPriceKeys.Remove(key);
            }
        }

        internal void EnsureModeEShellPriceScheduled(StockShop shop, int itemTypeID)
        {
            if (!IsCurrentModeEShellCapability(shop)) return;

            ModeEShellPriceKey key = new ModeEShellPriceKey
            {
                Shop = shop,
                ItemTypeID = itemTypeID,
                MerchantGeneration = modeEShellMerchantGeneration
            };
            if (modeEShellPriceCache.ContainsKey(key) || !modeEShellPendingPriceKeys.Add(key)) return;

            CacheSingleModeEShellPriceAsync(
                modeEShellSessionToken,
                modeEShellSessionScene,
                modeEShellSessionGeneration,
                modeEShellMerchantGeneration,
                shop,
                itemTypeID,
                key).Forget();
        }

        private async UniTask CacheAllModeEShopItemInstancesAsync(
            int sessionToken,
            int sceneBuildIndex,
            long sessionGeneration,
            long merchantGeneration,
            StockShop[] shops)
        {
            const int batchSize = 8;
            int processed = 0;
            if (shops == null) return;

            for (int s = 0; s < shops.Length; s++)
            {
                StockShop shop = shops[s];
                if (shop == null || shop.entries == null) continue;

                for (int i = 0; i < shop.entries.Count; i++)
                {
                    if (!IsCurrentModeEShellSession(sessionToken, sceneBuildIndex, sessionGeneration) ||
                        !IsCurrentModeEShellMerchantScope(shop, merchantGeneration) ||
                        !modeEShellEconomyAvailable)
                    {
                        return;
                    }

                    StockShop.Entry entry = shop.entries[i];
                    if (entry != null)
                    {
                        ModeEShellPriceKey key = new ModeEShellPriceKey
                        {
                            Shop = shop,
                            ItemTypeID = entry.ItemTypeID,
                            MerchantGeneration = merchantGeneration
                        };
                        if (!modeEShellPriceCache.ContainsKey(key) && modeEShellPendingPriceKeys.Add(key))
                        {
                            await CacheSingleModeEShellPriceAsync(
                                sessionToken,
                                sceneBuildIndex,
                                sessionGeneration,
                                merchantGeneration,
                                shop,
                                entry.ItemTypeID,
                                key);
                        }
                    }

                    processed++;
                    if (processed % batchSize == 0)
                    {
                        await UniTask.Yield();
                    }
                }

                if (IsCurrentModeEShellMerchantScope(shop, merchantGeneration) &&
                    modeEShellEconomyAvailable)
                {
                    BuildModeELotteryPoolState(shop, merchantGeneration);
                    PublishModeEShellPriceChanged(shop, merchantGeneration, null);
                }
            }

            ModBehaviour.DevLog("[ModeE/Shell] 分类商店贝壳价格预缓存完成");
        }
    }
}
