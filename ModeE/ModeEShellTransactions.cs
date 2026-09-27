// Mode E merchant runtime: ModeEShellTransactions.cs
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
        private bool IsModeEShellTransactionOwner(ModeEShellTransactionOwner owner)
        {
            ModeEShellTransactionOwner current = modeEShellTransactionOwner;
            return owner != null && current != null &&
                   current.SessionToken == owner.SessionToken &&
                   current.MerchantGeneration == owner.MerchantGeneration &&
                   current.TransactionID == owner.TransactionID &&
                   object.ReferenceEquals(current.Shop, owner.Shop);
        }

        private bool TryAcquireModeEShellTransaction(
            StockShop shop,
            bool isSellAll,
            out ModeEShellTransactionOwner owner)
        {
            owner = null;
            if (!IsCurrentModeEShellCapability(shop) || modeEShellTransactionOwner != null)
            {
                return false;
            }

            owner = new ModeEShellTransactionOwner
            {
                SessionToken = modeEShellSessionToken,
                MerchantGeneration = modeEShellMerchantGeneration,
                TransactionID = NextModeEShellCounter(ref modeEShellNextTransactionID),
                Shop = shop,
                OwnsBuying = false,
                OwnsSelling = false,
                IsSellAll = isSellAll
            };
            modeEShellTransactionOwner = owner;
            modeEShellTransactionUiBindings[owner.TransactionID] = modeEShellActiveUiBindingID;
            PublishModeEShellTransactionGateChanged(owner, true);
            return true;
        }

        private void SetModeEShellBusyField(FieldInfo field, StockShop shop, bool value, string fieldName)
        {
            if (field == null || shop == null) return;
            try { field.SetValue(shop, value); }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] restore " + fieldName + " failed: " + e.Message);
            }
        }

        private void ClearBusyAndReleaseModeEShellTransactionIfOwned(
            ModeEShellTransactionOwner owner,
            string reason,
            bool processFatalCleanup = true)
        {
            if (!IsModeEShellTransactionOwner(owner)) return;

            if (owner.OwnsBuying)
            {
                SetModeEShellBusyField(modeEShellBuyingField, owner.Shop, false, "buying");
            }
            if (owner.OwnsSelling)
            {
                SetModeEShellBusyField(modeEShellSellingField, owner.Shop, false, "selling");
            }

            modeEShellTransactionOwner = null;
            modeEShellTransactionUiBindings.Remove(owner.TransactionID);
            PublishModeEShellTransactionGateChanged(owner, false);

            if (processFatalCleanup && modeEShellFatalCleanupPending)
            {
                modeEShellFatalCleanupPending = false;
                InvalidateModeEShellMerchantGeneration("fatal transaction cleanup: " + reason);
            }
        }

        private bool IsModeEShellTransactionContextValid(
            ModeEShellTransactionOwner owner,
            StockShop.Entry entry,
            long uiBindingID,
            bool requireBalance)
        {
            if (!IsModeEShellTransactionOwner(owner) ||
                !IsCurrentModeEShellCapability(owner.Shop) ||
                owner.SessionToken != modeEShellSessionToken ||
                owner.MerchantGeneration != modeEShellMerchantGeneration ||
                 !IsCurrentModeEShellUiBinding(owner.Shop, uiBindingID) ||
                 entry == null ||
                 !IsCurrentModeEShellEntryReference(owner.Shop, entry) ||
                 entry.CurrentStock < 1)
            {
                return false;
            }

            if (requireBalance)
            {
                int price;
                if (!TryGetModeEShellPrice(owner.Shop, entry.ItemTypeID, out price) ||
                    modeEShellBalance < price)
                {
                    return false;
                }
            }

            long capturedUiBindingID;
            if (!modeEShellTransactionUiBindings.TryGetValue(owner.TransactionID, out capturedUiBindingID) ||
                capturedUiBindingID != uiBindingID)
            {
                return false;
            }

            return true;
        }

        internal bool ShouldBypassModeEShellSellPatch(StockShop shop)
        {
            ModeEShellTransactionOwner owner = modeEShellTransactionOwner;
            if (!modeEShellOriginalSellBypassArmed || owner == null ||
                modeEShellOriginalSellBypassTransactionID != owner.TransactionID ||
                !object.ReferenceEquals(modeEShellOriginalSellBypassShop, shop) ||
                !object.ReferenceEquals(owner.Shop, shop) ||
                !IsModeEShellTransactionOwner(owner))
            {
                return false;
            }

            // 只允许紧随反射 Invoke 的这一条原版调用通过；同栈重入必须重新经过共享交易门。
            modeEShellOriginalSellBypassArmed = false;
            modeEShellOriginalSellBypassTransactionID = 0L;
            modeEShellOriginalSellBypassShop = null;
            return true;
        }

        private async UniTask InvokeOriginalModeEShellSellAsync(
            ModeEShellTransactionOwner owner,
            Item item)
        {
            if (modeEShellSellTarget == null || owner == null || owner.Shop == null ||
                !IsModeEShellTransactionOwner(owner))
            {
                throw new MissingMethodException(typeof(StockShop).FullName, "Sell");
            }

            object taskObject;
            modeEShellOriginalSellBypassArmed = true;
            modeEShellOriginalSellBypassTransactionID = owner.TransactionID;
            modeEShellOriginalSellBypassShop = owner.Shop;
            try
            {
                taskObject = modeEShellSellTarget.Invoke(owner.Shop, new object[] { item });
            }
            finally
            {
                if (modeEShellOriginalSellBypassTransactionID == owner.TransactionID)
                {
                    modeEShellOriginalSellBypassArmed = false;
                    modeEShellOriginalSellBypassTransactionID = 0L;
                    modeEShellOriginalSellBypassShop = null;
                }
            }

            if (!(taskObject is UniTask))
            {
                throw new InvalidOperationException("StockShop.Sell did not return UniTask");
            }

            await (UniTask)taskObject;
        }

        internal async UniTask WrapModeEShellSellAsync(StockShop shop, Item item)
        {
            long uiBindingID = modeEShellActiveUiBindingID;
            if (item == null || !IsCurrentModeEShellUiBinding(shop, uiBindingID)) return;

            ModeEShellTransactionOwner owner;
            if (!TryAcquireModeEShellTransaction(shop, false, out owner)) return;
            try
            {
                if (shop.Busy || !IsModeEShellTransactionContextValidForSale(owner, uiBindingID)) return;
                owner.OwnsSelling = true;
                await InvokeOriginalModeEShellSellAsync(owner, item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] ordinary cash sale failed: " + e.Message);
            }
            finally
            {
                ClearBusyAndReleaseModeEShellTransactionIfOwned(owner, "Sell finally");
            }
        }

        private bool IsModeEShellTransactionContextValidForSale(
            ModeEShellTransactionOwner owner,
            long uiBindingID)
        {
            return IsModeEShellTransactionOwner(owner) &&
                   IsCurrentModeEShellCapability(owner.Shop) &&
                   IsCurrentModeEShellUiBinding(owner.Shop, uiBindingID) &&
                   modeEShellTransactionUiBindings.ContainsKey(owner.TransactionID) &&
                   modeEShellTransactionUiBindings[owner.TransactionID] == uiBindingID;
        }

        internal bool TryBeginModeEShellSellAll(StockShop shop, out long transactionID)
        {
            transactionID = 0L;
            long uiBindingID = modeEShellActiveUiBindingID;
            if (!IsCurrentModeEShellUiBinding(shop, uiBindingID)) return false;

            ModeEShellTransactionOwner owner;
            if (!TryAcquireModeEShellTransaction(shop, true, out owner)) return false;
            if (shop.Busy)
            {
                ClearBusyAndReleaseModeEShellTransactionIfOwned(owner, "SellAll saw Busy");
                return false;
            }

            owner.OwnsSelling = true;
            transactionID = owner.TransactionID;
            return true;
        }

        internal async UniTask SellModeEItemWithinSellAllAsync(
            StockShop shop,
            Item item,
            long transactionID)
        {
            ModeEShellTransactionOwner owner = modeEShellTransactionOwner;
            if (owner == null || !owner.IsSellAll || owner.TransactionID != transactionID ||
                !IsModeEShellTransactionOwner(owner) || item == null)
            {
                throw new InvalidOperationException("Mode E SellAll transaction owner is stale");
            }

            long capturedUiBindingID;
            if (!modeEShellTransactionUiBindings.TryGetValue(transactionID, out capturedUiBindingID) ||
                !IsCurrentModeEShellUiBinding(shop, capturedUiBindingID))
            {
                throw new InvalidOperationException("Mode E SellAll UI binding is stale");
            }

            await InvokeOriginalModeEShellSellAsync(owner, item);
        }

        internal void EndModeEShellSellAll(StockShop shop, long transactionID)
        {
            ModeEShellTransactionOwner owner = modeEShellTransactionOwner;
            if (owner == null || !object.ReferenceEquals(owner.Shop, shop) ||
                owner.TransactionID != transactionID)
            {
                return;
            }

            ClearBusyAndReleaseModeEShellTransactionIfOwned(owner, "SellAll finally");
        }

        private static StockShop.Entry FindModeEShellEntry(StockShop shop, int itemTypeID)
        {
            if (shop == null || shop.entries == null) return null;
            for (int i = 0; i < shop.entries.Count; i++)
            {
                StockShop.Entry entry = shop.entries[i];
                if (entry != null && entry.ItemTypeID == itemTypeID) return entry;
            }
            return null;
        }

        private static bool IsCurrentModeEShellEntryReference(
            StockShop shop,
            StockShop.Entry expectedEntry)
        {
            if (shop == null || expectedEntry == null || shop.entries == null) return false;
            for (int i = 0; i < shop.entries.Count; i++)
            {
                if (object.ReferenceEquals(shop.entries[i], expectedEntry)) return true;
            }
            return false;
        }

        private static bool IsModeEShellItemAssigned(Item item)
        {
            if (item == null || item.IsBeingDestroyed || item.StackCount <= 0) return true;
            try { if (item.InInventory != null) return true; } catch { }
            try { if (item.PluggedIntoSlot != null) return true; } catch { }
            try { if (item.ParentItem != null) return true; } catch { }
            try { if (item.IsInPlayerCharacter()) return true; } catch { }
            try { if (item.IsInPlayerStorage()) return true; } catch { }
            return false;
        }

        private static bool TryGetModeEShellIncomingBufferCount(out int count)
        {
            count = -1;
            try
            {
                List<ItemTreeData> buffer = PlayerStorage.IncomingItemBuffer;
                if (buffer == null) return false;
                count = buffer.Count;
                return true;
            }
            catch { return false; }
        }

        private static bool TryFindModeEShellIncomingBufferCommit(
            int startIndex,
            int sourceInstanceID,
            int expectedTypeID,
            int expectedStackCount,
            out ItemTreeData matched,
            out bool ambiguous)
        {
            matched = null;
            ambiguous = false;
            if (startIndex < 0) return false;
            try
            {
                List<ItemTreeData> buffer = PlayerStorage.IncomingItemBuffer;
                if (buffer == null) return false;
                int start = Mathf.Clamp(startIndex, 0, buffer.Count);
                int matchCount = 0;
                for (int i = start; i < buffer.Count; i++)
                {
                    ItemTreeData candidate = buffer[i];
                    if (candidate != null && candidate.rootInstanceID == sourceInstanceID)
                    {
                        matched = candidate;
                        matchCount++;
                    }
                }

                if (matchCount > 1)
                {
                    ambiguous = true;
                    matched = null;
                    ModBehaviour.DevLog("[ModeE/Shell] Incoming Buffer commit identity is ambiguous: instance=" +
                        sourceInstanceID + ", matches=" + matchCount);
                    return false;
                }

                if (matchCount == 1)
                {
                    int actualTypeID = 0;
                    int actualStackCount = -1;
                    try
                    {
                        actualTypeID = matched.RootTypeID;
                        ItemTreeData.DataEntry root = matched.RootData;
                        if (root != null) actualStackCount = root.StackCount;
                    }
                    catch { }
                    ModBehaviour.DevLog("[ModeE/Shell] Incoming Buffer commit diagnostic: instance=" +
                        sourceInstanceID + ", expectedType=" + expectedTypeID +
                        ", actualType=" + actualTypeID + ", expectedCount=" +
                        expectedStackCount + ", actualCount=" + actualStackCount);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void FailAmbiguousCommittedModeEShellDelivery(
            Item deliveryItem,
            string reason,
            ref bool allowPurchasedObserver)
        {
            allowPurchasedObserver = false;
            RetireCommittedModeEShellSourceItemNoThrow(deliveryItem);
            FailModeEShellEconomyDuringCommittedTransaction(reason);
        }

        private static bool RetireCommittedModeEShellSourceItemNoThrow(Item item)
        {
            if (item == null) return true;
            try { item.Detach(); } catch { }
            try { item.DestroyTree(); } catch { }
            try { if (item != null && !item.IsBeingDestroyed) item.MarkDestroyed(); } catch { }
            try
            {
                if (item != null && item.gameObject != null)
                {
                    UnityEngine.Object.Destroy(item.gameObject);
                }
            }
            catch { }

            try { return item == null || item.IsBeingDestroyed; }
            catch { return item == null; }
        }

        private System.Collections.IEnumerator VerifyModeEShellRetiredSourceNextFrame(Item item, int sourceInstanceID)
        {
            yield return null;
            if (item == null) yield break;

            ModBehaviour.DevLog("[ModeE/Shell] committed buffer source remained Unity-valid next frame: instance=" +
                sourceInstanceID);
            try { item.MarkDestroyed(); } catch { }
            try
            {
                if (item.gameObject != null) UnityEngine.Object.Destroy(item.gameObject);
            }
            catch { }
            SetModeEShellEconomyUnavailable("buffer source retirement failed", true);
        }

        private bool HandleCommittedModeEShellDeliveryRemainder(
            Item deliveryItem,
            int sourceInstanceID,
            int expectedTypeID,
            int expectedStackCount,
            bool saveCharacter,
            int incomingBufferStart,
            out bool allowPurchasedObserver)
        {
            allowPurchasedObserver = true;
            if (IsModeEShellItemAssigned(deliveryItem)) return true;

            if (saveCharacter && incomingBufferStart < 0)
            {
                FailAmbiguousCommittedModeEShellDelivery(
                    deliveryItem,
                    "Incoming Buffer snapshot unavailable after committed delivery",
                    ref allowPurchasedObserver);
                return true;
            }

            ItemTreeData matched;
            bool ambiguous = false;
            // 只有可保存角色的场景允许 Incoming Buffer 成为交付结果。
            if (saveCharacter && TryFindModeEShellIncomingBufferCommit(
                        incomingBufferStart,
                        sourceInstanceID,
                        expectedTypeID,
                        expectedStackCount,
                        out matched,
                        out ambiguous))
            {
                bool retired = RetireCommittedModeEShellSourceItemNoThrow(deliveryItem);
                if (!retired)
                {
                    allowPurchasedObserver = false;
                    FailModeEShellEconomyDuringCommittedTransaction(
                        "Incoming Buffer committed but source could not be retired");
                }
                else
                {
                    try { modeEHost.StartCoroutine(VerifyModeEShellRetiredSourceNextFrame(deliveryItem, sourceInstanceID)); }
                    catch { }
                }
                return true;
            }
            if (saveCharacter && ambiguous)
            {
                FailAmbiguousCommittedModeEShellDelivery(
                    deliveryItem,
                    "Incoming Buffer contains multiple matching commits",
                    ref allowPurchasedObserver);
                return true;
            }

            if (saveCharacter)
            {
                int bufferStartBeforeStorage;
                if (TryGetModeEShellIncomingBufferCount(out bufferStartBeforeStorage))
                {
                    try { ItemUtilities.SendToPlayerStorage(deliveryItem, false); }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[ModeE/Shell] storage fallback threw: " + e.Message);
                    }

                    if (TryFindModeEShellIncomingBufferCommit(
                            bufferStartBeforeStorage,
                            sourceInstanceID,
                            expectedTypeID,
                            expectedStackCount,
                            out matched,
                            out ambiguous))
                    {
                        bool retired = RetireCommittedModeEShellSourceItemNoThrow(deliveryItem);
                        if (!retired)
                        {
                            allowPurchasedObserver = false;
                            FailModeEShellEconomyDuringCommittedTransaction(
                                "Incoming Buffer committed but source could not be retired");
                        }
                        else
                        {
                            try { modeEHost.StartCoroutine(VerifyModeEShellRetiredSourceNextFrame(deliveryItem, sourceInstanceID)); }
                            catch { }
                        }
                        return true;
                    }
                    if (ambiguous)
                    {
                        FailAmbiguousCommittedModeEShellDelivery(
                            deliveryItem,
                            "Storage fallback produced multiple matching buffer commits",
                            ref allowPurchasedObserver);
                        return true;
                    }

                    if (IsModeEShellItemAssigned(deliveryItem)) return true;
                }
            }

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null) deliveryItem.Drop(player, true);
                else deliveryItem.Drop(deliveryItem.transform.position, true, Vector3.up, 0f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] committed remainder drop failed: " + e.Message);
            }
            return true;
        }

        private bool TryWriteModeEShellCurrentStock(StockShop.Entry entry, int nextStock)
        {
            try
            {
                modeEShellCurrentStockField.SetValue(entry, nextStock);
                object readBack = modeEShellCurrentStockField.GetValue(entry);
                return readBack is int && (int)readBack == nextStock;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] currentStock backing write/read failed: " + e.Message);
                return false;
            }
        }

        private void InvokeModeEShellStockChanged(StockShop.Entry entry)
        {
            try
            {
                Action<StockShop.Entry> handlers = modeEShellOnStockChangedField.GetValue(entry)
                    as Action<StockShop.Entry>;
                if (handlers == null) return;
                Delegate[] subscribers = handlers.GetInvocationList();
                for (int i = 0; i < subscribers.Length; i++)
                {
                    try { ((Action<StockShop.Entry>)subscribers[i])(entry); }
                    catch (Exception e) { ModBehaviour.DevLog("[ModeE/Shell] onStockChanged observer failed: " + e.Message); }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] onStockChanged dispatch failed: " + e.Message);
            }
        }

        private void InvokeModeEShellAfterItemSold(StockShop shop)
        {
            try
            {
                Action<StockShop> handlers = modeEShellOnAfterItemSoldField.GetValue(null) as Action<StockShop>;
                if (handlers == null) return;
                Delegate[] subscribers = handlers.GetInvocationList();
                for (int i = 0; i < subscribers.Length; i++)
                {
                    try { ((Action<StockShop>)subscribers[i])(shop); }
                    catch (Exception e) { ModBehaviour.DevLog("[ModeE/Shell] OnAfterItemSold observer failed: " + e.Message); }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] OnAfterItemSold dispatch failed: " + e.Message);
            }
        }

        private void InvokeModeEShellItemPurchased(StockShop shop, Item item)
        {
            try
            {
                Action<StockShop, Item> handlers = modeEShellOnItemPurchasedField.GetValue(null)
                    as Action<StockShop, Item>;
                if (handlers == null) return;
                Delegate[] subscribers = handlers.GetInvocationList();
                for (int i = 0; i < subscribers.Length; i++)
                {
                    try { ((Action<StockShop, Item>)subscribers[i])(shop, item); }
                    catch (Exception e) { ModBehaviour.DevLog("[ModeE/Shell] OnItemPurchased observer failed: " + e.Message); }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] OnItemPurchased dispatch failed: " + e.Message);
            }
        }

        private void PushModeEShellPurchaseNotification(StockShop shop, string displayName)
        {
            try
            {
                string format = shop.PurchaseNotificationTextFormat;
                string message = string.IsNullOrEmpty(format)
                    ? displayName
                    : StringExtensions.Format(format, new
                    {
                        itemDisplayName = displayName ?? string.Empty
                    });
                NotificationText.Push(message);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] purchase notification failed: " + e.Message);
            }
        }

        internal async UniTask<bool> BuyModeEShellItemAsync(
            StockShop shop,
            int itemTypeID,
            int amount)
        {
            // 首版严格限制 amount == 1；任何其他值都不得取得锁或修改状态。
            if (amount != 1) return false;

            long uiBindingID = modeEShellActiveUiBindingID;
            if (!IsCurrentModeEShellUiBinding(shop, uiBindingID)) return false;

            ModeEShellTransactionOwner owner;
            if (!TryAcquireModeEShellTransaction(shop, false, out owner)) return false;

            Item deliveryItem = null;
            bool debited = false;
            bool committed = false;
            try
            {
                if (shop.Busy) return false;
                SetModeEShellBusyField(modeEShellBuyingField, shop, true, "buying");
                owner.OwnsBuying = true;

                StockShop.Entry entry = FindModeEShellEntry(shop, itemTypeID);
                int shellPrice;
                if (entry == null || entry.CurrentStock < 1 ||
                    !TryGetModeEShellPrice(shop, itemTypeID, out shellPrice))
                {
                    EnsureModeEShellPriceScheduled(shop, itemTypeID);
                    return false;
                }
                if (modeEShellBalance < shellPrice) return false;

                deliveryItem = await ItemAssetsCollection.InstantiateAsync(itemTypeID);
                if (deliveryItem == null ||
                    !IsModeEShellTransactionContextValid(owner, entry, uiBindingID, true))
                {
                    DestroyModeEShellTemporarySample(deliveryItem);
                    deliveryItem = null;
                    return false;
                }

                NormalizeModeEShellStackForShop(shop, deliveryItem);
                deliveryItem.FromInfoKey = "UI_Trade";
                string capturedDisplayName = deliveryItem.DisplayName;

                if (!TryDebitModeEShell(shellPrice, owner.TransactionID))
                {
                    DestroyModeEShellTemporarySample(deliveryItem);
                    deliveryItem = null;
                    return false;
                }
                debited = true;

                int sourceInstanceID = deliveryItem.GetInstanceID();
                int expectedStackCount = deliveryItem.StackCount;
                // 发货链可能在库存满时直接写入 Incoming Buffer；
                // 必须在任何 SendTo* 之前快照 Count，才能用新增区间匹配 rootInstanceID。
                bool saveCharacter = false;
                try { saveCharacter = LevelConfig.SaveCharacter; } catch { }
                int incomingBufferStart = -1;
                if (saveCharacter &&
                    !TryGetModeEShellIncomingBufferCount(out incomingBufferStart))
                {
                    incomingBufferStart = -1;
                }
                committed = true;
                MarkModeEShellTransactionCommitted(owner.TransactionID);

                try { ItemUtilities.SendToPlayerCharacterInventory(deliveryItem, false); }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ModeE/Shell] character inventory delivery threw: " + e.Message);
                }

                bool allowPurchasedObserver;
                HandleCommittedModeEShellDeliveryRemainder(
                    deliveryItem,
                    sourceInstanceID,
                    itemTypeID,
                    expectedStackCount,
                    saveCharacter,
                    incomingBufferStart,
                    out allowPurchasedObserver);

                int nextStock = entry.CurrentStock - 1;
                bool stockWritten = nextStock >= 0 && TryWriteModeEShellCurrentStock(entry, nextStock);
                if (stockWritten)
                {
                    InvokeModeEShellStockChanged(entry);
                }
                else
                {
                    FailModeEShellEconomyDuringCommittedTransaction("currentStock write/read failed");
                }

                // 保持当前 DLL 顺序：库存订阅者 -> OnAfterItemSold -> OnItemPurchased -> 通知。
                InvokeModeEShellAfterItemSold(shop);
                if (allowPurchasedObserver)
                {
                    InvokeModeEShellItemPurchased(shop, deliveryItem);
                }
                PushModeEShellPurchaseNotification(shop, capturedDisplayName);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] purchase transaction failed: " + e.Message);
                if (committed)
                {
                    FailModeEShellEconomyDuringCommittedTransaction(
                        "unexpected exception after delivery commit");
                }
                return committed;
            }
            finally
            {
                if (!committed && debited)
                {
                    RefundIfDebited(owner.TransactionID);
                }
                if (!committed && deliveryItem != null)
                {
                    DestroyModeEShellTemporarySample(deliveryItem);
                }
                ClearBusyAndReleaseModeEShellTransactionIfOwned(owner, "Buy finally");
            }
        }

        internal void ApplyModeEShellItemEntryUi(
            StockShopItemEntry itemEntry,
            StockShopView master,
            StockShop.Entry entry)
        {
            if (itemEntry == null || master == null || entry == null) return;
            StockShop shop = master.Target;
            if (!IsCurrentModeEShellCapability(shop)) return;

            TextMeshProUGUI priceText = modeEShellItemEntryPriceTextField.GetValue(itemEntry)
                as TextMeshProUGUI;
            if (priceText == null) return;

            Item sample = null;
            try { sample = shop.GetItemInstanceDirect(entry.ItemTypeID); } catch { }
            if (sample != null) NormalizeModeEShellStackForShop(shop, sample);

            int shellPrice = 0;
            if (TryGetModeEShellPrice(shop, entry.ItemTypeID, out shellPrice))
            {
                priceText.text = L10n.T("贝壳 ", "Shells ") + shellPrice.ToString("N0");
            }
            else
            {
                priceText.text = L10n.T("贝壳载入中", "Shell price loading");
                EnsureModeEShellPriceScheduled(shop, entry.ItemTypeID);
            }
        }

        internal void ApplyModeEShellInteractionButtonUi(StockShopView view)
        {
            if (view == null) return;
            StockShop shop = view.Target;
            if (!IsCurrentModeEShellCapability(shop)) return;

            Button button = modeEShellViewInteractionButtonField.GetValue(view) as Button;
            Image image = modeEShellViewInteractionButtonImageField.GetValue(view) as Image;
            TextMeshProUGUI interactionText = modeEShellViewInteractionTextField.GetValue(view) as TextMeshProUGUI;
            TextMeshProUGUI priceText = modeEShellViewPriceTextField.GetValue(view) as TextMeshProUGUI;
            if (button == null || interactionText == null || priceText == null) return;

            StockShopItemEntry selection = view.GetSelection();
            if (selection == null)
            {
                // 玩家背包/仓库物品仍按官方账户现金出售。
                interactionText.text = L10n.T("出售到账户", "Sell to account");
                if (modeEShellTransactionOwner != null) button.interactable = false;
                return;
            }

            StockShop.Entry entry = selection.Target;
            int shellPrice = 0;
            bool priceReady = entry != null && TryGetModeEShellPrice(shop, entry.ItemTypeID, out shellPrice);
            bool gateFree = modeEShellTransactionOwner == null;
            bool unlocked = selection.IsUnlocked();
            bool inStock = entry != null && entry.CurrentStock > 0;
            bool enough = priceReady && modeEShellBalance >= shellPrice;
            bool canBuy = gateFree && unlocked && inStock && enough;

            button.interactable = canBuy;
            if (!gateFree)
            {
                interactionText.text = L10n.T("交易处理中...", "Transaction in progress...");
            }
            else if (!inStock)
            {
                interactionText.text = L10n.T("已售罄", "Sold out");
            }
            else if (!priceReady)
            {
                interactionText.text = L10n.T("价格载入中...", "Price loading...");
                if (entry != null) EnsureModeEShellPriceScheduled(shop, entry.ItemTypeID);
            }
            else
            {
                interactionText.text = L10n.T("购买", "Buy");
            }

            priceText.text = priceReady
                ? L10n.T("贝壳 ", "Shells ") + shellPrice.ToString("N0") +
                  L10n.T(" / 持有 ", " / Held ") + modeEShellBalance.ToString("N0")
                : L10n.T("贝壳价格不可用", "Shell price unavailable");
            if (image != null)
            {
                // 官方自己的「可买 / 不可买」两色，旧版刷成荧光绿 / Color.gray（UB-21，见 GetModeEShellOfficialButtonColor）
                image.color = GetModeEShellOfficialButtonColor(view, canBuy);
            }
        }

        internal void RefreshOpenModeEShellUi(
            StockShop shop,
            long uiBindingID,
            int? itemTypeID)
        {
            if (!IsCurrentModeEShellUiBinding(shop, uiBindingID)) return;
            StockShopView view = StockShopView.Instance;
            if (view == null || !object.ReferenceEquals(view.Target, shop)) return;

            // Setup/Postfix already applies the shell price to every newly bound row.
            // Only a completed price event needs to revisit a row; balance/gate changes
            // affect the interaction button, not every pooled entry.
            if (itemTypeID.HasValue)
            {
                try
                {
                    StockShopItemEntry[] entries =
                        view.GetComponentsInChildren<StockShopItemEntry>(false);
                    for (int i = 0; i < entries.Length; i++)
                    {
                        StockShopItemEntry row = entries[i];
                        if (row == null || row.Target == null ||
                            row.Target.ItemTypeID != itemTypeID.Value)
                        {
                            continue;
                        }
                        ApplyModeEShellItemEntryUi(row, view, row.Target);
                    }
                }
                catch { }
            }

            try { modeEShellRefreshInteractionButtonTarget.Invoke(view, null); }
            catch (Exception e) { ModBehaviour.DevLog("[ModeE/Shell] UI event refresh failed: " + e.Message); }
        }

        internal bool IsCurrentModeEShellBalanceEvent(
            ModeEShellBalanceChangedEvent evt,
            StockShop shop,
            long uiBindingID)
        {
            return evt != null &&
                   evt.SessionToken == modeEShellSessionToken &&
                   evt.SessionGeneration == modeEShellSessionGeneration &&
                   IsCurrentModeEShellUiBinding(shop, uiBindingID);
        }

        internal bool IsCurrentModeEShellPriceEvent(
            ModeEShellPriceCacheChangedEvent evt,
            StockShop shop,
            long uiBindingID)
        {
            return evt != null &&
                   evt.SessionToken == modeEShellSessionToken &&
                   evt.SceneBuildIndex == modeEShellSessionScene &&
                   evt.SessionGeneration == modeEShellSessionGeneration &&
                   evt.MerchantGeneration == modeEShellMerchantGeneration &&
                   object.ReferenceEquals(evt.Shop, shop) &&
                   IsCurrentModeEShellUiBinding(shop, uiBindingID);
        }

        internal bool IsCurrentModeEShellGateEvent(
            ModeEShellTransactionGateChangedEvent evt,
            StockShop shop,
            long uiBindingID)
        {
            return evt != null &&
                   evt.SessionToken == modeEShellSessionToken &&
                   evt.MerchantGeneration == modeEShellMerchantGeneration &&
                   IsCurrentModeEShellUiBinding(shop, uiBindingID);
        }
    }
}
