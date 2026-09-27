// Mode E merchant runtime: ModeEMerchantSellAllUI.cs
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
        private const int MODE_E_SHOP_INITIAL_ENTRY_COUNT = 24;
        private const int MODE_E_SHOP_ENTRIES_PER_FRAME = 12;

        internal sealed class ProgressiveShopViewSetupState
        {
            internal StockShop Shop;
            internal List<StockShop.Entry> OriginalEntries;
            internal GameObject EntryContentRoot;
            internal bool EntryContentRootWasActive;
            internal int TotalEntryCount;
            internal int InitialEntryCount;
            internal int RecycledEntryCount;
            internal long RecycleElapsedMilliseconds;
            internal long SetupStartedTimestamp;
            internal long PopulationID;
            internal bool Restored;
        }

        private static FieldInfo playerInventoryDisplayField;
        private static FieldInfo characterInventoryDisplayField;
        private static FieldInfo sortButtonField;
        private static FieldInfo merchantNameTextField;
        private static FieldInfo refreshCountDownTextField;
        private static FieldInfo contextualCashDisplayField;
        private static FieldInfo cashDisplayTextField;
        private static FieldInfo stockShopEntryPoolField;
        private static FieldInfo stockShopPoolActiveEntriesField;
        private static MethodInfo sellMethod;
        private static Action<StockShopItemEntry, StockShopView, StockShop.Entry> stockShopItemEntrySetup;
        private static bool reflectionInitialized;

        private static StockShop currentShop;
        private static Inventory currentPlayerInventory;
        private static GameObject sellAllButtonObject;
        private static Button sellAllButton;
        private static TextMeshProUGUI sellAllButtonText;
        private static GameObject shellBalanceTextObject;
        private static TextMeshProUGUI shellBalanceText;
        private static bool shellBalanceHasShellIcon;
        private static GameObject lotteryButtonObject;
        private static Button lotteryButton;
        private static TextMeshProUGUI lotteryButtonText;
        private static RectTransform lotteryCountDownRow;
        private static Transform lotteryCountDownOriginalParent;
        private static int lotteryCountDownOriginalSiblingIndex;
        private static Vector2 lotteryCountDownOriginalAnchorMin;
        private static Vector2 lotteryCountDownOriginalAnchorMax;
        private static Vector2 lotteryCountDownOriginalPivot;
        private static Vector2 lotteryCountDownOriginalAnchoredPosition;
        private static Vector2 lotteryCountDownOriginalSizeDelta;
        private static Quaternion lotteryCountDownOriginalLocalRotation;
        private static Vector3 lotteryCountDownOriginalLocalScale;
        private static bool lotteryCountDownOriginalActiveSelf;
        private static bool lotteryCountDownLayoutCaptured;
        private static bool isSelling;
        private static long sellAllOperationCounter;
        private static long activeSellAllOperationID;
        private static ModBehaviour modeEShellOwner;
        private static long modeEShellUiBindingID;
        private static bool modeEMerchantShopViewSetupInProgress;
        private static long modeEMerchantProgressivePopulationID;
        private static StockShop modeEMerchantProgressiveShop;
        private static bool modeEMerchantProgressivePopulationComplete;

        private static long BeginSellAllOperation()
        {
            if (sellAllOperationCounter == long.MaxValue)
            {
                sellAllOperationCounter = 1L;
            }
            else
            {
                sellAllOperationCounter++;
                if (sellAllOperationCounter <= 0L) sellAllOperationCounter = 1L;
            }

            activeSellAllOperationID = sellAllOperationCounter;
            return activeSellAllOperationID;
        }

        private static bool IsCurrentSellAllOperation(long operationID, StockShop shop)
        {
            return operationID > 0L &&
                   activeSellAllOperationID == operationID &&
                   object.ReferenceEquals(currentShop, shop);
        }

        private static bool IsBossRushModeCategoryShop(StockShop shop)
        {
            if (shop == null)
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
            return disposition == ModeEShellShopPatchDisposition.HandleModeE ||
                   (disposition == ModeEShellShopPatchDisposition.PassOriginal &&
                    inst.IsModeFActive &&
                    !string.IsNullOrEmpty(shop.MerchantID) &&
                    shop.MerchantID.StartsWith("ModeE_", StringComparison.Ordinal));
        }

        internal static void Attach(StockShop shop)
        {
            Cleanup(false);

            if (shop == null)
            {
                return;
            }

            ModBehaviour inst = ModBehaviour.Instance;
            ModeEShellShopPatchDisposition disposition = inst != null
                ? inst.GetModeEShellShopPatchDisposition(shop)
                : ModeEShellShopPatchDisposition.PassOriginal;
            if (disposition == ModeEShellShopPatchDisposition.Block)
            {
                return;
            }
            if (disposition == ModeEShellShopPatchDisposition.HandleModeE)
            {
                long bindingID;
                if (!inst.TryAttachModeEShellUi(shop, out bindingID)) return;
                modeEShellOwner = inst;
                modeEShellUiBindingID = bindingID;
            }
            else if (string.IsNullOrEmpty(shop.MerchantID) ||
                     !shop.MerchantID.StartsWith("ModeE_", StringComparison.Ordinal))
            {
                // Mode F 沿用旧 MerchantID UI；Mode E owned 身份不依赖该字符串。
                return;
            }

            InitializeReflection();
            currentShop = shop;
            BindPlayerInventory();
            RegisterEvents();
            CreateSellAllButton();
            CreateShellBalanceText();
            CreateLotteryButton();
            UpdateButtonState();
            UpdateShellBalanceText();
            UpdateLotteryButtonState();
            if (modeEShellOwner != null)
            {
                modeEShellOwner.RefreshOpenModeEShellUi(
                    currentShop,
                    modeEShellUiBindingID,
                    null);
            }
        }

        private static void InitializeReflection()
        {
            if (reflectionInitialized)
            {
                return;
            }

            BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
            playerInventoryDisplayField = typeof(StockShopView).GetField("playerInventoryDisplay", privateInstance);
            characterInventoryDisplayField = typeof(StockShopView).GetField("characterInventoryDisplay", privateInstance);
            sortButtonField = typeof(Duckov.UI.InventoryDisplay).GetField("sortButton", privateInstance);
            merchantNameTextField = typeof(StockShopView).GetField("merchantNameText", privateInstance);
            refreshCountDownTextField = typeof(StockShopView).GetField(
                "refreshCountDown",
                privateInstance);
            contextualCashDisplayField = typeof(ContextualMoneyAndCash).GetField(
                "cashDisplay",
                privateInstance);
            cashDisplayTextField = typeof(CashDisplay).GetField("text", privateInstance);
            stockShopEntryPoolField = typeof(StockShopView).GetField("_entryPool", privateInstance);
            stockShopPoolActiveEntriesField = typeof(PrefabPool<StockShopItemEntry>).GetField(
                "activeObjects",
                privateInstance);
            sellMethod = typeof(StockShop).GetMethod("Sell", privateInstance, null, new Type[] { typeof(Item) }, null);
            MethodInfo itemEntrySetupMethod = typeof(StockShopItemEntry).GetMethod(
                "Setup",
                privateInstance,
                null,
                new Type[] { typeof(StockShopView), typeof(StockShop.Entry) },
                null);
            try
            {
                stockShopItemEntrySetup = itemEntrySetupMethod != null
                    ? (Action<StockShopItemEntry, StockShopView, StockShop.Entry>)Delegate.CreateDelegate(
                        typeof(Action<StockShopItemEntry, StockShopView, StockShop.Entry>),
                        itemEntrySetupMethod)
                    : null;
            }
            catch
            {
                stockShopItemEntrySetup = null;
            }
            reflectionInitialized = true;
        }

        internal static bool VerifyModeEShellRuntimeContracts()
        {
            InitializeReflection();
            bool inventoryDisplayReady =
                (playerInventoryDisplayField != null &&
                 playerInventoryDisplayField.FieldType == typeof(Duckov.UI.InventoryDisplay) &&
                 !playerInventoryDisplayField.IsStatic) ||
                (characterInventoryDisplayField != null &&
                 characterInventoryDisplayField.FieldType == typeof(Duckov.UI.InventoryDisplay) &&
                 !characterInventoryDisplayField.IsStatic);
            ParameterInfo[] sellParameters = sellMethod != null ? sellMethod.GetParameters() : null;
            return inventoryDisplayReady &&
                   sortButtonField != null &&
                   sortButtonField.FieldType == typeof(Button) &&
                   !sortButtonField.IsStatic &&
                   merchantNameTextField != null &&
                   merchantNameTextField.FieldType == typeof(TextMeshProUGUI) &&
                   !merchantNameTextField.IsStatic &&
                   refreshCountDownTextField != null &&
                   refreshCountDownTextField.FieldType == typeof(TextMeshProUGUI) &&
                   !refreshCountDownTextField.IsStatic &&
                   sellMethod != null &&
                   sellMethod.ReturnType == typeof(UniTask) &&
                   !sellMethod.IsStatic &&
                   sellParameters != null &&
                   sellParameters.Length == 1 &&
                   sellParameters[0].ParameterType == typeof(Item);
        }

        private static void RegisterEvents()
        {
            StockShop.OnAfterItemSold += OnAfterItemSold;
            ManagedUIElement.onClose += OnManagedUIElementClose;
            if (modeEShellOwner != null)
            {
                modeEShellOwner.SubscribeModeEShellUiEvents(
                    OnModeEShellBalanceChanged,
                    OnModeEShellPriceChanged,
                    OnModeEShellTransactionGateChanged);
            }
        }

        private static void UnregisterEvents()
        {
            StockShop.OnAfterItemSold -= OnAfterItemSold;
            ManagedUIElement.onClose -= OnManagedUIElementClose;
            if (modeEShellOwner != null)
            {
                modeEShellOwner.UnsubscribeModeEShellUiEvents(
                    OnModeEShellBalanceChanged,
                    OnModeEShellPriceChanged,
                    OnModeEShellTransactionGateChanged);
            }
        }

        private static void BindPlayerInventory()
        {
            if (currentPlayerInventory != null)
            {
                currentPlayerInventory.onContentChanged -= OnPlayerInventoryContentChanged;
                currentPlayerInventory = null;
            }

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.CharacterItem != null)
                {
                    currentPlayerInventory = player.CharacterItem.Inventory;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] 绑定玩家背包失败: " + e.Message);
            }

            if (currentPlayerInventory != null)
            {
                currentPlayerInventory.onContentChanged += OnPlayerInventoryContentChanged;
            }
        }

        private static void OnModeEShellBalanceChanged(ModeEShellBalanceChangedEvent evt)
        {
            if (modeEShellOwner == null ||
                !modeEShellOwner.IsCurrentModeEShellBalanceEvent(
                    evt,
                    currentShop,
                    modeEShellUiBindingID))
            {
                return;
            }

            UpdateShellBalanceText();
            UpdateLotteryButtonState();
            modeEShellOwner.RefreshOpenModeEShellUi(currentShop, modeEShellUiBindingID, null);
        }

        private static void OnModeEShellPriceChanged(ModeEShellPriceCacheChangedEvent evt)
        {
            if (modeEShellOwner == null ||
                !modeEShellOwner.IsCurrentModeEShellPriceEvent(
                    evt,
                    currentShop,
                    modeEShellUiBindingID))
            {
                return;
            }

            modeEShellOwner.RefreshOpenModeEShellUi(
                currentShop,
                modeEShellUiBindingID,
                evt.ItemTypeID);
            UpdateLotteryButtonState();
        }

        private static void OnModeEShellTransactionGateChanged(ModeEShellTransactionGateChangedEvent evt)
        {
            if (modeEShellOwner == null ||
                !modeEShellOwner.IsCurrentModeEShellGateEvent(
                    evt,
                    currentShop,
                    modeEShellUiBindingID))
            {
                return;
            }

            UpdateButtonState();
            UpdateLotteryButtonState();
            modeEShellOwner.RefreshOpenModeEShellUi(currentShop, modeEShellUiBindingID, null);
        }

        private static void UpdateButtonState()
        {
            if (sellAllButton == null)
            {
                return;
            }

            string sellAllLabel = L10n.T("一键卖出", "Sell All");
            string displayText;
            bool interactable;

            if (isSelling || (modeEShellOwner != null && modeEShellOwner.IsModeEShellTransactionGateBusy))
            {
                displayText = L10n.T("交易处理中...", "Transaction in progress...");
                interactable = false;
            }
            else
            {
                int itemCount = CountSellableInventoryItems();
                bool canSell = currentShop != null && itemCount > 0;
                displayText = canSell ? sellAllLabel + " (" + itemCount + ")" : sellAllLabel;
                interactable = canSell;
            }

            ApplyButtonState(sellAllButton, sellAllButtonObject, sellAllButtonText, displayText, interactable);
        }

        private static int CountSellableInventoryItems()
        {
            if (currentPlayerInventory == null)
            {
                BindPlayerInventory();
            }

            if (currentPlayerInventory == null || currentPlayerInventory.Content == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < currentPlayerInventory.Content.Count; i++)
            {
                if (IsInventoryIndexLocked(currentPlayerInventory, i))
                {
                    continue;
                }

                Item item = currentPlayerInventory.Content[i];
                if (item != null && item.CanBeSold && !IsItemWishlisted(item))
                {
                    count++;
                }
            }

            return count;
        }

        private static List<Item> CollectSellableInventoryItems()
        {
            List<Item> items = new List<Item>();

            if (currentPlayerInventory == null)
            {
                BindPlayerInventory();
            }

            if (currentPlayerInventory == null || currentPlayerInventory.Content == null)
            {
                return items;
            }

            for (int i = 0; i < currentPlayerInventory.Content.Count; i++)
            {
                if (IsInventoryIndexLocked(currentPlayerInventory, i))
                {
                    continue;
                }

                Item item = currentPlayerInventory.Content[i];
                if (item != null && item.CanBeSold && !IsItemWishlisted(item))
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private static void OnSellAllButtonClicked()
        {
            if (currentShop == null || isSelling ||
                (modeEShellOwner != null && modeEShellOwner.IsModeEShellTransactionGateBusy))
            {
                return;
            }

            SellAllInventoryItemsAsync().Forget();
        }

        private static async UniTaskVoid SellAllInventoryItemsAsync()
        {
            StockShop targetShop = currentShop;
            if (targetShop == null)
            {
                UpdateButtonState();
                return;
            }

            ModBehaviour capturedShellOwner = modeEShellOwner;
            long shellTransactionID = 0L;
            if (capturedShellOwner != null &&
                !capturedShellOwner.TryBeginModeEShellSellAll(targetShop, out shellTransactionID))
            {
                UpdateButtonState();
                return;
            }

            List<Item> itemsToSell = CollectSellableInventoryItems();
            if (itemsToSell.Count <= 0)
            {
                if (capturedShellOwner != null)
                {
                    capturedShellOwner.EndModeEShellSellAll(targetShop, shellTransactionID);
                }
                UpdateButtonState();
                return;
            }

            long sellAllOperationID = BeginSellAllOperation();
            isSelling = true;
            UpdateButtonState();

            int soldCount = 0;
            int failedCount = 0;

            try
            {
                for (int i = 0; i < itemsToSell.Count; i++)
                {
                    if (capturedShellOwner != null &&
                        !IsCurrentSellAllOperation(sellAllOperationID, targetShop))
                    {
                        break;
                    }

                    Item item = itemsToSell[i];
                    if (item == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (capturedShellOwner != null)
                        {
                            await capturedShellOwner.SellModeEItemWithinSellAllAsync(
                                targetShop,
                                item,
                                shellTransactionID);
                        }
                        else
                        {
                            await SellItemAsync(targetShop, item);
                        }
                        soldCount++;
                    }
                    catch (Exception e)
                    {
                        failedCount++;
                        ModBehaviour.DevLog("[ModeE] [WARNING] 一键卖出失败: " + item.DisplayName + ", " + e.Message);
                    }
                }

                if (soldCount > 0 && failedCount > 0)
                {
                    NotificationText.Push(L10n.T(
                        "已卖出 " + soldCount + " 件物品，" + failedCount + " 件未能卖出",
                        "Sold " + soldCount + " items, " + failedCount + " could not be sold"));
                }
                else if (soldCount > 0)
                {
                    NotificationText.Push(L10n.T(
                        "已卖出 " + soldCount + " 件物品",
                        "Sold " + soldCount + " items"));
                }
                else
                {
                    NotificationText.Push(L10n.T(
                        "没有物品被卖出",
                        "No items were sold"));
                }
            }
            finally
            {
                if (capturedShellOwner != null)
                {
                    capturedShellOwner.EndModeEShellSellAll(targetShop, shellTransactionID);
                }
                if (IsCurrentSellAllOperation(sellAllOperationID, targetShop))
                {
                    activeSellAllOperationID = 0L;
                    isSelling = false;
                    UpdateButtonState();
                }
            }
        }

        private static async UniTask SellItemAsync(StockShop shop, Item item)
        {
            if (shop == null)
            {
                throw new InvalidOperationException("shop is null");
            }

            if (item == null)
            {
                throw new InvalidOperationException("item is null");
            }

            if (sellMethod == null)
            {
                throw new MissingMethodException(typeof(StockShop).FullName, "Sell");
            }

            object taskObject = sellMethod.Invoke(shop, new object[] { item });
            if (!(taskObject is UniTask))
            {
                throw new InvalidOperationException("StockShop.Sell did not return UniTask");
            }

            await (UniTask)taskObject;
        }

        private static void OnPlayerInventoryContentChanged(Inventory inventory, int index)
        {
            UpdateButtonState();
        }

        private static void OnAfterItemSold(StockShop shop)
        {
            if (shop != currentShop)
            {
                return;
            }

            UpdateButtonState();
        }

        private static void OnManagedUIElementClose(ManagedUIElement element)
        {
            StockShopView shopView = element as StockShopView;
            if (shopView == null || currentShop == null)
            {
                return;
            }

            CancelProgressiveShopViewPopulation(currentShop);
            Cleanup(false);
        }

        private static void Cleanup(bool destroyUiObjects)
        {
            UnregisterEvents();

            ModBehaviour capturedShellOwner = modeEShellOwner;
            StockShop capturedShop = currentShop;
            long capturedBindingID = modeEShellUiBindingID;
            modeEShellOwner = null;
            modeEShellUiBindingID = 0L;
            if (capturedShellOwner != null)
            {
                capturedShellOwner.DetachModeEShellUi(
                    capturedShop,
                    capturedBindingID,
                    "StockShop UI cleanup");
            }

            if (currentPlayerInventory != null)
            {
                currentPlayerInventory.onContentChanged -= OnPlayerInventoryContentChanged;
                currentPlayerInventory = null;
            }

            if (sellAllButtonObject != null)
            {
                if (destroyUiObjects)
                {
                    UnityEngine.Object.Destroy(sellAllButtonObject);
                    sellAllButtonObject = null;
                }
                else
                {
                    sellAllButtonObject.SetActive(false);
                }
            }

            if (destroyUiObjects)
            {
                sellAllButton = null;
                sellAllButtonText = null;
            }
            if (shellBalanceTextObject != null)
            {
                if (destroyUiObjects)
                {
                    UnityEngine.Object.Destroy(shellBalanceTextObject);
                    shellBalanceTextObject = null;
                }
                else
                {
                    shellBalanceTextObject.SetActive(false);
                }
            }
            if (destroyUiObjects)
            {
                shellBalanceText = null;
                shellBalanceHasShellIcon = false;
            }
            RestoreModeELotteryCountdownLayout();
            if (lotteryButtonObject != null)
            {
                if (destroyUiObjects)
                {
                    UnityEngine.Object.Destroy(lotteryButtonObject);
                    lotteryButtonObject = null;
                }
                else
                {
                    lotteryButtonObject.SetActive(false);
                }
            }
            if (destroyUiObjects)
            {
                lotteryButton = null;
                lotteryButtonText = null;
            }
            currentShop = null;
            activeSellAllOperationID = 0L;
            isSelling = false;
        }

        internal static void DetachForModeEShellInvalidation(ModBehaviour owner, string reason)
        {
            if (owner == null || !object.ReferenceEquals(modeEShellOwner, owner)) return;
            CancelProgressiveShopViewPopulation(currentShop);
            Cleanup(false);
        }

        /// <summary>
        /// 静态缓存兜底清理 — 由 ResetModeEMerchantStaticCaches 统一调用。
        /// 作为 Cleanup 的上位兜底，确保反射缓存等静态字段被完整释放。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            modeEMerchantShopViewSetupInProgress = false;
            CancelProgressiveShopViewPopulation(modeEMerchantProgressiveShop);
            modeEMerchantProgressiveShop = null;
            modeEMerchantProgressivePopulationComplete = false;
            Cleanup(true);
            playerInventoryDisplayField = null;
            characterInventoryDisplayField = null;
            sortButtonField = null;
            merchantNameTextField = null;
            refreshCountDownTextField = null;
            contextualCashDisplayField = null;
            cashDisplayTextField = null;
            stockShopEntryPoolField = null;
            stockShopPoolActiveEntriesField = null;
            sellMethod = null;
            stockShopItemEntrySetup = null;
            reflectionInitialized = false;
        }

        private static bool IsInventoryIndexLocked(Inventory inventory, int index)
        {
            try
            {
                return inventory != null && inventory.IsIndexLocked(index);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsItemWishlisted(Item item)
        {
            try
            {
                return item != null
                    && ItemWishlist.Instance != null
                    && ItemWishlist.Instance.IsManuallyWishlisted(item.TypeID);
            }
            catch (Exception e)
            {
                // 异常时返回 false（允许卖出），但记录日志便于排障：
                // 若 ItemWishlist.Instance 状态异常或 IsManuallyWishlisted 抛异常，
                // 玩家的愿望清单物品可能被误卖，需要定位原因。
                ModBehaviour.DevLog("[ModeEMerchant] IsItemWishlisted 检查异常，默认允许卖出: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 写标签与可点状态。底色**不写**：一键卖出与抽奖都是克隆的官方「整理」按钮，保留官方 prefab 自带的
        /// ColorBlock 与三态（旧版刷成纯绿 / 天蓝、悬停只乘 1.1、禁用 Color.gray，和官方购买键三种颜色在同一个官方界面里打架，
        /// 2026-09-23 审美审查 UB-21 / UB-33）。
        /// </summary>
        private static void ApplyButtonState(
            Button targetButton,
            GameObject targetObject,
            TextMeshProUGUI targetText,
            string displayText,
            bool interactable)
        {
            if (targetButton == null)
            {
                return;
            }

            if (targetText != null)
            {
                targetText.text = displayText;
                targetText.richText = false;
            }
            else if (targetObject != null)
            {
                Text legacyText = targetObject.GetComponentInChildren<Text>();
                if (legacyText != null)
                {
                    legacyText.text = displayText;
                    legacyText.supportRichText = false;
                }
            }

            targetButton.interactable = interactable;
        }
    }
}
