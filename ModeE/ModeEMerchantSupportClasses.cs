// ============================================================================
// ModeEMerchantSupportClasses.cs - Mode E merchant interactable, UI, and pet helpers
// ============================================================================

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
    public partial class ModBehaviour
    {
        private const string MODE_E_SHELL_HARMONY_OWNER = "com.bossrush.mod";
        // TryGetIDByName 按 ItemMetaData.Name 查询；Item_SeaShell 是本地化键，不是物品内部名。
        private const string MODE_E_SHELL_ITEM_NAME = "SeaShell";
        private const long MODE_E_SHELL_CASH_UNIT = 2000L;

        private static long NextModeEShellCounter(ref long counter)
        {
            if (counter == long.MaxValue)
            {
                // 运行期不应触达；保留正数身份且不回到 0。
                counter = 1L;
            }
            else
            {
                counter++;
                if (counter <= 0L) counter = 1L;
            }

            return counter;
        }

        private static bool IsModeEShellMethodContract(
            MethodInfo method,
            Type returnType,
            bool isStatic,
            params Type[] parameterTypes)
        {
            if (method == null || method.ReturnType != returnType || method.IsStatic != isStatic)
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != parameterTypes.Length) return false;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != parameterTypes[i]) return false;
            }

            return true;
        }

        private static bool IsModeEShellFieldContract(
            FieldInfo field,
            Type fieldType,
            bool isStatic)
        {
            return field != null && field.FieldType == fieldType && field.IsStatic == isStatic;
        }

        private static bool IsModeEShellPropertyContract(
            PropertyInfo property,
            Type propertyType,
            bool isStatic)
        {
            MethodInfo getter = property != null ? property.GetGetMethod(true) : null;
            return property != null && property.PropertyType == propertyType &&
                   getter != null && getter.IsStatic == isStatic;
        }

        private static void RecordModeEShellContractFailure(
            List<string> failures,
            bool valid,
            string contractName)
        {
            if (!valid)
            {
                failures.Add(contractName);
            }
        }

        private bool ResolveModeEShellRuntimeContracts()
        {
            try
            {
                Type stockShopType = typeof(StockShop);
                Type entryType = typeof(StockShop.Entry);
                Type itemEntryType = typeof(StockShopItemEntry);
                Type viewType = typeof(StockShopView);
                Type[] buyArgs = new Type[] { typeof(int), typeof(int) };
                Type[] sellArgs = new Type[] { typeof(Item) };
                Type[] setupArgs = new Type[] { typeof(StockShopView), typeof(StockShop.Entry) };

                modeEShellBuyTarget = AccessTools.Method(stockShopType, "Buy", buyArgs);
                modeEShellSellTarget = AccessTools.Method(stockShopType, "Sell", sellArgs);
                modeEShellGetItemInstanceDirectTarget = AccessTools.Method(
                    stockShopType,
                    "GetItemInstanceDirect",
                    new Type[] { typeof(int) });
                modeEShellItemEntrySetupTarget = AccessTools.Method(itemEntryType, "Setup", setupArgs);
                modeEShellViewSetupTarget = AccessTools.Method(
                    viewType,
                    "Setup",
                    new Type[] { typeof(StockShop) });
                modeEShellRefreshInteractionButtonTarget = AccessTools.Method(viewType, "RefreshInteractionButton");

                modeEShellBuyingField = AccessTools.Field(stockShopType, "buying");
                modeEShellSellingField = AccessTools.Field(stockShopType, "selling");
                modeEShellCurrentStockField = AccessTools.Field(entryType, "currentStock");
                modeEShellOnStockChangedField = AccessTools.Field(entryType, "onStockChanged");
                modeEShellOnAfterItemSoldField = AccessTools.Field(stockShopType, "OnAfterItemSold");
                modeEShellOnItemPurchasedField = AccessTools.Field(stockShopType, "OnItemPurchased");
                modeEShellPurchaseNotificationFormatProperty = AccessTools.Property(
                    stockShopType,
                    "PurchaseNotificationTextFormat");

                modeEShellItemEntryPriceTextField = AccessTools.Field(itemEntryType, "priceText");
                modeEShellViewPriceTextField = AccessTools.Field(viewType, "priceText");
                modeEShellViewInteractionButtonField = AccessTools.Field(viewType, "interactionButton");
                modeEShellViewInteractionButtonImageField = AccessTools.Field(viewType, "interactionButtonImage");
                modeEShellViewInteractionTextField = AccessTools.Field(viewType, "interactionText");
                modeEShellViewMerchantNameTextField = AccessTools.Field(viewType, "merchantNameText");

                List<string> failures = new List<string>();
                RecordModeEShellContractFailure(failures, IsModeEShellMethodContract(
                    modeEShellBuyTarget, typeof(UniTask<bool>), false, typeof(int), typeof(int)),
                    "StockShop.Buy(int,int):UniTask<bool>");
                RecordModeEShellContractFailure(failures, IsModeEShellMethodContract(
                    modeEShellSellTarget, typeof(UniTask), false, typeof(Item)),
                    "StockShop.Sell(Item):UniTask");
                RecordModeEShellContractFailure(failures, IsModeEShellMethodContract(
                    modeEShellGetItemInstanceDirectTarget, typeof(Item), false, typeof(int)),
                    "StockShop.GetItemInstanceDirect(int):Item");
                RecordModeEShellContractFailure(failures, IsModeEShellMethodContract(
                    modeEShellItemEntrySetupTarget, typeof(void), false,
                    typeof(StockShopView), typeof(StockShop.Entry)),
                    "StockShopItemEntry.Setup(StockShopView,Entry):void");
                RecordModeEShellContractFailure(failures, IsModeEShellMethodContract(
                    modeEShellViewSetupTarget, typeof(void), false, typeof(StockShop)),
                    "StockShopView.Setup(StockShop):void");
                RecordModeEShellContractFailure(failures, IsModeEShellMethodContract(
                    modeEShellRefreshInteractionButtonTarget, typeof(void), false),
                    "StockShopView.RefreshInteractionButton():void");
                RecordModeEShellContractFailure(failures,
                    IsModeEShellFieldContract(modeEShellBuyingField, typeof(bool), false),
                    "StockShop.buying:bool");
                RecordModeEShellContractFailure(failures,
                    IsModeEShellFieldContract(modeEShellSellingField, typeof(bool), false),
                    "StockShop.selling:bool");
                RecordModeEShellContractFailure(failures,
                    IsModeEShellFieldContract(modeEShellCurrentStockField, typeof(int), false),
                    "StockShop.Entry.currentStock:int");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    modeEShellOnStockChangedField, typeof(Action<StockShop.Entry>), false),
                    "StockShop.Entry.onStockChanged:Action<Entry>");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    modeEShellOnAfterItemSoldField, typeof(Action<StockShop>), true),
                    "StockShop.OnAfterItemSold:Action<StockShop>");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    modeEShellOnItemPurchasedField, typeof(Action<StockShop, Item>), true),
                    "StockShop.OnItemPurchased:Action<StockShop,Item>");
                RecordModeEShellContractFailure(failures, IsModeEShellPropertyContract(
                    modeEShellPurchaseNotificationFormatProperty, typeof(string), false),
                    "StockShop.PurchaseNotificationTextFormat:string");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    modeEShellItemEntryPriceTextField, typeof(TextMeshProUGUI), false),
                    "StockShopItemEntry.priceText:TextMeshProUGUI");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    modeEShellViewPriceTextField, typeof(TextMeshProUGUI), false),
                    "StockShopView.priceText:TextMeshProUGUI");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    modeEShellViewInteractionButtonField, typeof(Button), false),
                    "StockShopView.interactionButton:Button");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    modeEShellViewInteractionButtonImageField, typeof(Image), false),
                    "StockShopView.interactionButtonImage:Image");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    modeEShellViewInteractionTextField, typeof(TextMeshProUGUI), false),
                    "StockShopView.interactionText:TextMeshProUGUI");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    modeEShellViewMerchantNameTextField, typeof(TextMeshProUGUI), false),
                    "StockShopView.merchantNameText:TextMeshProUGUI");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    ReflectionCache.StockShop_MerchantID, typeof(string), false),
                    "StockShop.merchantID:string");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    ReflectionCache.StockShop_AccountAvaliable, typeof(bool), false),
                    "StockShop.accountAvaliable:bool");
                RecordModeEShellContractFailure(failures,
                    ModeEMerchantSellAllUI.VerifyModeEShellRuntimeContracts(),
                    "ModeEMerchantSellAllUI runtime contracts");

                if (failures.Count > 0)
                {
                    DevLog("[ModeE/Shell] M0/M1 反射契约不匹配: " +
                        string.Join(", ", failures.ToArray()));
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Shell] M0/M1 反射契约解析失败: " + e.Message);
                return false;
            }
        }

        private static bool HasExpectedModeEShellPatch(
            MethodBase target,
            MethodInfo expectedPatch,
            bool prefix)
        {
            if (target == null || expectedPatch == null)
            {
                return false;
            }

            HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(target);
            if (patchInfo == null)
            {
                return false;
            }

            IList<HarmonyLib.Patch> patches = prefix ? patchInfo.Prefixes : patchInfo.Postfixes;
            if (patches == null)
            {
                return false;
            }

            for (int i = 0; i < patches.Count; i++)
            {
                HarmonyLib.Patch patch = patches[i];
                if (patch != null &&
                    string.Equals(patch.owner, MODE_E_SHELL_HARMONY_OWNER, StringComparison.Ordinal) &&
                    patch.PatchMethod == expectedPatch)
                {
                    return true;
                }
            }

            return false;
        }

        internal bool VerifyModeEShellPatchInstallation()
        {
            if (!ResolveModeEShellRuntimeContracts())
            {
                return false;
            }

            MethodInfo buyPrefix = AccessTools.Method(typeof(ModeEShellBuyPatch), "Prefix");
            MethodInfo sellPrefix = AccessTools.Method(typeof(ModeEShellSellPatch), "Prefix");
            MethodInfo itemEntryPostfix = AccessTools.Method(typeof(ModeEShellShopItemEntryPatch), "Postfix");
            MethodInfo viewSetupPrefix = AccessTools.Method(typeof(ModeEMerchantShopViewSetupReusePatch), "Prefix");
            MethodInfo buttonPostfix = AccessTools.Method(typeof(ModeEShellInteractionButtonPatch), "Postfix");

            List<string> missingPatches = new List<string>();
            if (!HasExpectedModeEShellPatch(modeEShellBuyTarget, buyPrefix, true))
                missingPatches.Add("StockShop.Buy prefix");
            if (!HasExpectedModeEShellPatch(modeEShellSellTarget, sellPrefix, true))
                missingPatches.Add("StockShop.Sell prefix");
            if (!HasExpectedModeEShellPatch(modeEShellItemEntrySetupTarget, itemEntryPostfix, false))
                missingPatches.Add("StockShopItemEntry.Setup postfix");
            if (!HasExpectedModeEShellPatch(modeEShellViewSetupTarget, viewSetupPrefix, true))
                missingPatches.Add("StockShopView.Setup reuse prefix");
            if (!HasExpectedModeEShellPatch(modeEShellRefreshInteractionButtonTarget, buttonPostfix, false))
                missingPatches.Add("StockShopView.RefreshInteractionButton postfix");

            bool installed = missingPatches.Count == 0;

            if (!installed)
            {
                DevLog("[ModeE/Shell] 交易/UI Harmony 补丁安装证明失败，分类商店保持关闭: " +
                    string.Join(", ", missingPatches.ToArray()));
            }

            return installed;
        }

        private void InitializeModeEShellSession(int sessionToken, int sceneBuildIndex)
        {
            InvalidateAndResetModeEShellSession("next session initialization");

            modeEShellSessionToken = sessionToken;
            modeEShellSessionScene = sceneBuildIndex;
            modeEShellSessionGeneration++;
            if (modeEShellSessionGeneration <= 0L) modeEShellSessionGeneration = 1L;
            modeEShellBalance = 0;
            modeEShellFirstPositiveRewardGranted = false;
            modeEShellFatalCleanupPending = false;

            try
            {
                modeEShellItemTypeID = ItemAssetsCollection.TryGetIDByName(MODE_E_SHELL_ITEM_NAME, false);
                if (modeEShellItemTypeID < 0)
                {
                    DevLog("[ModeE/Shell] 贝壳物品解析失败: ItemMetaData.Name=" +
                        MODE_E_SHELL_ITEM_NAME + ", localizationKey=Item_SeaShell");
                }
            }
            catch (Exception e)
            {
                modeEShellItemTypeID = -1;
                DevLog("[ModeE/Shell] 贝壳物品解析异常: ItemMetaData.Name=" +
                    MODE_E_SHELL_ITEM_NAME + ", " + e.Message);
            }

            bool contractsReady = modeEShellItemTypeID >= 0 && VerifyModeEShellPatchInstallation();
            modeEShellEconomyAvailable = contractsReady;
            if (!contractsReady)
            {
                SetModeEShellEconomyUnavailable("session preflight failed", false);
                return;
            }

            DevLog("[ModeE/Shell] M1 capability ready: session=" + sessionToken +
                ", sessionGeneration=" + modeEShellSessionGeneration +
                ", SeaShell=" + modeEShellItemTypeID);
        }

        private bool IsCurrentModeEShellSession(int sessionToken, int sceneBuildIndex, long sessionGeneration)
        {
            return modeEActive &&
                   !modeFActive &&
                   sessionToken > 0 &&
                   modeEShellSessionToken == sessionToken &&
                   modeEShellSessionScene == sceneBuildIndex &&
                   modeEShellSessionGeneration == sessionGeneration &&
                   modeESessionToken == sessionToken &&
                   UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == sceneBuildIndex;
        }

        private bool IsCurrentModeEShellMerchantScope(StockShop shop, long merchantGeneration)
        {
            if (!IsCurrentModeEShellSession(
                    modeEShellSessionToken,
                    modeEShellSessionScene,
                    modeEShellSessionGeneration) ||
                shop == null ||
                merchantGeneration != modeEShellMerchantGeneration ||
                !modeEMerchantShops.Contains(shop))
            {
                return false;
            }

            long registeredGeneration;
            return modeEMerchantShopGenerations.TryGetValue(shop, out registeredGeneration) &&
                   registeredGeneration == merchantGeneration;
        }

        internal bool IsCurrentModeEShellCapability(StockShop shop)
        {
            return modeEShellEconomyAvailable &&
                   IsCurrentModeEShellMerchantScope(shop, modeEShellMerchantGeneration);
        }

        internal ModeEShellShopPatchDisposition GetModeEShellShopPatchDisposition(StockShop shop)
        {
            // UnityEngine.Object 重载的 == 会在同帧回调排空前把待销毁组件报告为 null。
            // 退役 Mode E 商店在该窗口仍必须保持 Block，不能回落到现金 Buy。
            if (object.ReferenceEquals(shop, null) || !modeEOwnedShopTombstones.Contains(shop))
            {
                return ModeEShellShopPatchDisposition.PassOriginal;
            }

            if (!IsCurrentModeEShellMerchantScope(shop, modeEShellMerchantGeneration) ||
                !modeEShellEconomyAvailable)
            {
                return ModeEShellShopPatchDisposition.Block;
            }

            return ModeEShellShopPatchDisposition.HandleModeE;
        }

        private void PublishModeEShellBalanceChanged()
        {
            Action<ModeEShellBalanceChangedEvent> handlers = ModeEShellBalanceChanged;
            if (handlers == null) return;

            ModeEShellBalanceChangedEvent evt = new ModeEShellBalanceChangedEvent
            {
                SessionToken = modeEShellSessionToken,
                SessionGeneration = modeEShellSessionGeneration,
                NewBalance = modeEShellBalance
            };

            Delegate[] subscribers = handlers.GetInvocationList();
            for (int i = 0; i < subscribers.Length; i++)
            {
                try { ((Action<ModeEShellBalanceChangedEvent>)subscribers[i])(evt); }
                catch (Exception e) { DevLog("[ModeE/Shell] 余额订阅者异常: " + e.Message); }
            }
        }

        private void PublishModeEShellPriceChanged(StockShop shop, long merchantGeneration, int? itemTypeID)
        {
            Action<ModeEShellPriceCacheChangedEvent> handlers = ModeEShellPriceCacheChanged;
            if (handlers == null) return;

            ModeEShellPriceCacheChangedEvent evt = new ModeEShellPriceCacheChangedEvent
            {
                SessionToken = modeEShellSessionToken,
                SceneBuildIndex = modeEShellSessionScene,
                SessionGeneration = modeEShellSessionGeneration,
                MerchantGeneration = merchantGeneration,
                Shop = shop,
                ItemTypeID = itemTypeID
            };

            Delegate[] subscribers = handlers.GetInvocationList();
            for (int i = 0; i < subscribers.Length; i++)
            {
                try { ((Action<ModeEShellPriceCacheChangedEvent>)subscribers[i])(evt); }
                catch (Exception e) { DevLog("[ModeE/Shell] 价格订阅者异常: " + e.Message); }
            }
        }

        private void PublishModeEShellTransactionGateChanged(ModeEShellTransactionOwner owner, bool isBusy)
        {
            Action<ModeEShellTransactionGateChangedEvent> handlers = ModeEShellTransactionGateChanged;
            if (handlers == null || owner == null) return;

            ModeEShellTransactionGateChangedEvent evt = new ModeEShellTransactionGateChangedEvent
            {
                SessionToken = owner.SessionToken,
                MerchantGeneration = owner.MerchantGeneration,
                TransactionID = owner.TransactionID,
                IsBusy = isBusy,
                Shop = owner.Shop
            };

            Delegate[] subscribers = handlers.GetInvocationList();
            for (int i = 0; i < subscribers.Length; i++)
            {
                try { ((Action<ModeEShellTransactionGateChangedEvent>)subscribers[i])(evt); }
                catch (Exception e) { DevLog("[ModeE/Shell] 交易门订阅者异常: " + e.Message); }
            }
        }

        private bool TryDebitModeEShell(int amount, long transactionID)
        {
            if (amount <= 0 || transactionID <= 0L ||
                modeEShellDebits.ContainsKey(transactionID) ||
                modeEShellRefundedTransactions.Contains(transactionID) ||
                !modeEShellEconomyAvailable ||
                modeEShellBalance < amount)
            {
                return false;
            }

            long next = (long)modeEShellBalance - amount;
            if (next < 0L || next > int.MaxValue) return false;

            modeEShellBalance = (int)next;
            modeEShellDebits[transactionID] = amount;
            PublishModeEShellBalanceChanged();
            return true;
        }

        private int CreditModeEShell(int amount, string reason)
        {
            if (amount <= 0 || !modeEShellEconomyAvailable)
            {
                return modeEShellBalance;
            }

            long next = (long)modeEShellBalance + amount;
            modeEShellBalance = (int)Math.Max(0L, Math.Min((long)int.MaxValue, next));
            DevLog("[ModeE/Shell] Credit " + amount + " (" + reason + "), balance=" + modeEShellBalance);
            PublishModeEShellBalanceChanged();
            return modeEShellBalance;
        }

        private bool RefundIfDebited(long transactionID)
        {
            int amount;
            if (transactionID <= 0L ||
                modeEShellCommittedTransactions.Contains(transactionID) ||
                modeEShellRefundedTransactions.Contains(transactionID) ||
                !modeEShellDebits.TryGetValue(transactionID, out amount))
            {
                return false;
            }

            modeEShellRefundedTransactions.Add(transactionID);
            long next = (long)modeEShellBalance + amount;
            modeEShellBalance = (int)Math.Max(0L, Math.Min((long)int.MaxValue, next));
            PublishModeEShellBalanceChanged();
            return true;
        }

        private void MarkModeEShellTransactionCommitted(long transactionID)
        {
            if (transactionID > 0L && modeEShellDebits.ContainsKey(transactionID))
            {
                modeEShellCommittedTransactions.Add(transactionID);
            }
        }

        internal int CurrentModeEShellBalance
        {
            get { return modeEShellBalance; }
        }

        internal bool IsModeEShellTransactionGateBusy
        {
            get { return modeEShellTransactionOwner != null; }
        }

        internal void SubscribeModeEShellUiEvents(
            Action<ModeEShellBalanceChangedEvent> balance,
            Action<ModeEShellPriceCacheChangedEvent> price,
            Action<ModeEShellTransactionGateChangedEvent> gate)
        {
            ModeEShellBalanceChanged += balance;
            ModeEShellPriceCacheChanged += price;
            ModeEShellTransactionGateChanged += gate;
        }

        internal void UnsubscribeModeEShellUiEvents(
            Action<ModeEShellBalanceChangedEvent> balance,
            Action<ModeEShellPriceCacheChangedEvent> price,
            Action<ModeEShellTransactionGateChangedEvent> gate)
        {
            ModeEShellBalanceChanged -= balance;
            ModeEShellPriceCacheChanged -= price;
            ModeEShellTransactionGateChanged -= gate;
        }
    }

    public partial class ModBehaviour
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
                DevLog("[ModeE/Shell] capability disabled: " + reason);
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
            DevLog("[ModeE/Shell] committed transaction contract failure: " + reason);
        }

        private void InvalidateModeEShellUiBinding(string reason)
        {
            modeEShellActiveUiBindingID = 0L;
            modeEShellActiveUiShop = null;
            if (VerboseStartupDebugLogsEnabled)
            {
                DevLog("[ModeE/Shell] UI binding invalidated: " + reason);
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

            ModeEMerchantSellAllUI.DetachForModeEShellInvalidation(this, reason);
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

            if (retired.Length > 0 && this != null)
            {
                try { StartCoroutine(ConfirmRetiredModeEShellShopsDestroyedNextFrame(retired)); }
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
                DevLog("[ModeE/Shell] runtime destroy retained live owned-shop tombstones");
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

        internal void NormalizeModeEShellStackForShop(StockShop shop, Item item)
        {
            if (shop == null || item == null || !item.Stackable) return;
            if (!string.Equals(shop.MerchantID, "ModeE_Bullet", StringComparison.Ordinal)) return;
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
                long quantified = (raw + MODE_E_SHELL_CASH_UNIT - 1L) / MODE_E_SHELL_CASH_UNIT;
                if (quantified < 1L || quantified > int.MaxValue) return false;
                shellPrice = (int)quantified;
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Shell] price unavailable: " + e.Message);
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
                DevLog("[ModeE/Shell] lazy price task failed: type=" + itemTypeID + ", " + e.Message);
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
                    PublishModeEShellPriceChanged(shop, merchantGeneration, null);
                }
            }

            DevLog("[ModeE/Shell] 分类商店贝壳价格预缓存完成");
        }
    }

    public partial class ModBehaviour
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
                DevLog("[ModeE/Shell] restore " + fieldName + " failed: " + e.Message);
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
                DevLog("[ModeE/Shell] ordinary cash sale failed: " + e.Message);
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
                    DevLog("[ModeE/Shell] Incoming Buffer commit identity is ambiguous: instance=" +
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
                    DevLog("[ModeE/Shell] Incoming Buffer commit diagnostic: instance=" +
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

            DevLog("[ModeE/Shell] committed buffer source remained Unity-valid next frame: instance=" +
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
                    try { StartCoroutine(VerifyModeEShellRetiredSourceNextFrame(deliveryItem, sourceInstanceID)); }
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
                        DevLog("[ModeE/Shell] storage fallback threw: " + e.Message);
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
                            try { StartCoroutine(VerifyModeEShellRetiredSourceNextFrame(deliveryItem, sourceInstanceID)); }
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
                DevLog("[ModeE/Shell] committed remainder drop failed: " + e.Message);
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
                DevLog("[ModeE/Shell] currentStock backing write/read failed: " + e.Message);
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
                    catch (Exception e) { DevLog("[ModeE/Shell] onStockChanged observer failed: " + e.Message); }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Shell] onStockChanged dispatch failed: " + e.Message);
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
                    catch (Exception e) { DevLog("[ModeE/Shell] OnAfterItemSold observer failed: " + e.Message); }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Shell] OnAfterItemSold dispatch failed: " + e.Message);
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
                    catch (Exception e) { DevLog("[ModeE/Shell] OnItemPurchased observer failed: " + e.Message); }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Shell] OnItemPurchased dispatch failed: " + e.Message);
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
                DevLog("[ModeE/Shell] purchase notification failed: " + e.Message);
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
                    DevLog("[ModeE/Shell] character inventory delivery threw: " + e.Message);
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
                DevLog("[ModeE/Shell] purchase transaction failed: " + e.Message);
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
                image.color = canBuy ? new Color(0.2f, 0.8f, 0.35f, 1f) : Color.gray;
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
            catch (Exception e) { DevLog("[ModeE/Shell] UI event refresh failed: " + e.Message); }
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

    public class ModeEShopInteractable : InteractableBase
    {
        /// <summary>关联的 StockShop 实例</summary>
        private StockShop _shop;

        /// <summary>显示名称（如"枪械"、"护甲"等）</summary>
        private string _displayName;

        /// <summary>
        /// 初始化交互选项
        /// </summary>
        public void Setup(StockShop shop, string displayName)
        {
            _shop = shop;
            _displayName = displayName;
            this.overrideInteractName = true;
            this._overrideInteractNameKey = displayName;
        }

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                if (!string.IsNullOrEmpty(_displayName))
                    this._overrideInteractNameKey = _displayName;
            }
            catch { }
            try { base.Awake(); } catch { }
            try
            {
                // 禁用碰撞体（作为子交互选项不需要独立碰撞检测）
                this.interactCollider = GetComponent<Collider>();
                if (this.interactCollider != null)
                    this.interactCollider.enabled = false;
            }
            catch { }
            try { this.MarkerActive = false; } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
            try
            {
                // Start 后重新设置名称（防止被 base.Start 覆盖）
                this.overrideInteractName = true;
                if (!string.IsNullOrEmpty(_displayName))
                    this._overrideInteractNameKey = _displayName;
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            if (_shop == null) return false;
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null) return false;
            return inst.GetModeEShellShopPatchDisposition(_shop) != ModeEShellShopPatchDisposition.Block;
        }

        /// <summary>
        /// 玩家选择此交互选项时，打开对应分类的商店 UI
        /// </summary>
        protected override void OnTimeOut()
        {
            System.Diagnostics.Stopwatch openStopwatch =
                System.Diagnostics.Stopwatch.StartNew();
            long readinessMilliseconds = 0L;
            long showUiMilliseconds = 0L;
            long attachMilliseconds = 0L;
            try
            {
                if (_shop == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] ModeEShopInteractable: _shop 为 null");
                    return;
                }

                ModBehaviour inst = ModBehaviour.Instance;
                ModeEShellShopPatchDisposition disposition = inst != null
                    ? inst.GetModeEShellShopPatchDisposition(_shop)
                    : ModeEShellShopPatchDisposition.Block;
                if (disposition == ModeEShellShopPatchDisposition.Block)
                {
                    if (inst != null) inst.ShowModeEShopLoadingFeedback();
                    return;
                }
                if (disposition == ModeEShellShopPatchDisposition.HandleModeE)
                {
                    long readinessStarted = openStopwatch.ElapsedMilliseconds;
                    bool samplesReady = inst.AreAllModeEShopOfficialSamplesReady(_shop);
                    readinessMilliseconds =
                        openStopwatch.ElapsedMilliseconds - readinessStarted;
                    if (!samplesReady)
                    {
                        inst.ShowModeEShopLoadingFeedback();
                        return;
                    }
                }

                long showUiStarted = openStopwatch.ElapsedMilliseconds;
                _shop.ShowUI();
                showUiMilliseconds = openStopwatch.ElapsedMilliseconds - showUiStarted;
                long attachStarted = openStopwatch.ElapsedMilliseconds;
                ModeEMerchantSellAllUI.Attach(_shop);
                attachMilliseconds = openStopwatch.ElapsedMilliseconds - attachStarted;

                if (openStopwatch.ElapsedMilliseconds >= 16L)
                {
                    int itemCount = _shop.entries != null ? _shop.entries.Count : 0;
                    ModBehaviour.DevLog(
                        "[ModeE] [Profile] shop open sync: merchant=" + _shop.MerchantID +
                        ", items=" + itemCount +
                        ", readinessMs=" + readinessMilliseconds +
                        ", showUiMs=" + showUiMilliseconds +
                        ", attachMs=" + attachMilliseconds +
                        ", totalMs=" + openStopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] ModeEShopInteractable.OnTimeOut 失败: " + e.Message);
            }
        }
    }

    internal static class ModeEMerchantSellAllUI
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
            UpdateButtonState();
            UpdateShellBalanceText();
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

        private static void CreateSellAllButton()
        {
            StockShopView shopView = StockShopView.Instance;
            if (shopView == null || shopView.Target != currentShop)
            {
                return;
            }

            try
            {
                Duckov.UI.InventoryDisplay playerInventoryDisplay = null;
                if (playerInventoryDisplayField != null)
                {
                    playerInventoryDisplay = playerInventoryDisplayField.GetValue(shopView) as Duckov.UI.InventoryDisplay;
                }

                if (playerInventoryDisplay == null && characterInventoryDisplayField != null)
                {
                    playerInventoryDisplay = characterInventoryDisplayField.GetValue(shopView) as Duckov.UI.InventoryDisplay;
                }

                if (playerInventoryDisplay == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 无法获取玩家背包 InventoryDisplay，跳过创建一键卖出按钮");
                    return;
                }

                Button sortButton = null;
                if (sortButtonField != null)
                {
                    sortButton = sortButtonField.GetValue(playerInventoryDisplay) as Button;
                }

                if (sortButton == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 无法获取整理按钮，跳过创建一键卖出按钮");
                    return;
                }

                if (sellAllButtonObject == null)
                {
                    sellAllButtonObject = UnityEngine.Object.Instantiate(
                        sortButton.gameObject,
                        sortButton.transform.parent);
                    sellAllButtonObject.name = "ModeEMerchantSellAllButton";
                }
                else if (sellAllButtonObject.transform.parent != sortButton.transform.parent)
                {
                    sellAllButtonObject.transform.SetParent(sortButton.transform.parent, false);
                }
                sellAllButtonObject.SetActive(true);

                RectTransform rt = sellAllButtonObject.GetComponent<RectTransform>();
                RectTransform sortRt = sortButton.GetComponent<RectTransform>();
                if (rt != null && sortRt != null)
                {
                    rt.anchorMin = sortRt.anchorMin;
                    rt.anchorMax = sortRt.anchorMax;
                    rt.pivot = sortRt.pivot;
                    rt.anchoredPosition = sortRt.anchoredPosition + new Vector2(0f, sortRt.sizeDelta.y + 8f);
                    rt.sizeDelta = new Vector2(sortRt.sizeDelta.x + 80f, sortRt.sizeDelta.y);
                }

                LayoutElement layoutElement = sellAllButtonObject.GetComponent<LayoutElement>();
                LayoutElement sourceLayoutElement = sortButton.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    if (sourceLayoutElement != null && sourceLayoutElement.preferredWidth > 0f)
                    {
                        layoutElement.preferredWidth = sourceLayoutElement.preferredWidth + 80f;
                    }

                    if (sourceLayoutElement != null && sourceLayoutElement.minWidth > 0f)
                    {
                        layoutElement.minWidth = sourceLayoutElement.minWidth + 80f;
                    }
                }

                ContentSizeFitter contentSizeFitter = sellAllButtonObject.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                {
                    contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                }

                sellAllButton = sellAllButtonObject.GetComponent<Button>();
                if (sellAllButton != null)
                {
                    sellAllButton.onClick.RemoveAllListeners();
                    sellAllButton.onClick.AddListener(OnSellAllButtonClicked);
                }

                sellAllButtonText = sellAllButtonObject.GetComponentInChildren<TextMeshProUGUI>();
                UpdateButtonState();

                ModBehaviour.DevLog("[ModeE] 一键卖出按钮创建成功");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] 创建一键卖出按钮失败: " + e.Message);
            }
        }

        private static void CreateShellBalanceText()
        {
            if (modeEShellOwner == null) return;
            StockShopView shopView = StockShopView.Instance;
            if (shopView == null || shopView.Target != currentShop || merchantNameTextField == null) return;

            try
            {
                TextMeshProUGUI merchantName = merchantNameTextField.GetValue(shopView) as TextMeshProUGUI;
                if (merchantName == null) return;

                if (shellBalanceTextObject == null)
                {
                    shellBalanceTextObject = UnityEngine.Object.Instantiate(
                        merchantName.gameObject,
                        merchantName.transform.parent);
                    shellBalanceTextObject.name = "ModeEShellBalanceText";
                }
                else if (shellBalanceTextObject.transform.parent != merchantName.transform.parent)
                {
                    shellBalanceTextObject.transform.SetParent(merchantName.transform.parent, false);
                }
                shellBalanceTextObject.SetActive(true);
                shellBalanceTextObject.name = "ModeEShellBalanceText";
                shellBalanceText = shellBalanceTextObject.GetComponent<TextMeshProUGUI>();
                if (shellBalanceText == null)
                {
                    UnityEngine.Object.Destroy(shellBalanceTextObject);
                    shellBalanceTextObject = null;
                    return;
                }

                shellBalanceText.richText = false;
                shellBalanceText.fontSize = Mathf.Max(14f, merchantName.fontSize * 0.72f);
                RectTransform sourceRect = merchantName.rectTransform;
                RectTransform targetRect = shellBalanceText.rectTransform;
                if (sourceRect != null && targetRect != null)
                {
                    targetRect.anchorMin = sourceRect.anchorMin;
                    targetRect.anchorMax = sourceRect.anchorMax;
                    targetRect.pivot = sourceRect.pivot;
                    targetRect.sizeDelta = sourceRect.sizeDelta;
                    targetRect.anchoredPosition = sourceRect.anchoredPosition +
                        new Vector2(0f, -Mathf.Max(24f, sourceRect.rect.height * 0.65f));
                }

                shellBalanceTextObject.SetActive(true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] 创建余额文本失败: " + e.Message);
            }
        }

        private static void UpdateShellBalanceText()
        {
            if (shellBalanceText == null || modeEShellOwner == null) return;
            shellBalanceText.text = L10n.T("贝壳：", "Shells: ") +
                modeEShellOwner.CurrentModeEShellBalance.ToString("N0");
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
            Color buttonColor;

            if (isSelling || (modeEShellOwner != null && modeEShellOwner.IsModeEShellTransactionGateBusy))
            {
                displayText = L10n.T("交易处理中...", "Transaction in progress...");
                interactable = false;
                buttonColor = Color.gray;
            }
            else
            {
                int itemCount = CountSellableInventoryItems();
                bool canSell = currentShop != null && itemCount > 0;
                displayText = canSell ? sellAllLabel + " (" + itemCount + ")" : sellAllLabel;
                interactable = canSell;
                buttonColor = canSell ? new Color(0.2f, 0.8f, 0.2f) : Color.gray;
            }

            ApplyButtonState(sellAllButton, sellAllButtonObject, sellAllButtonText, displayText, interactable, buttonColor);
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

        private static void ApplyButtonState(
            Button targetButton,
            GameObject targetObject,
            TextMeshProUGUI targetText,
            string displayText,
            bool interactable,
            Color buttonColor)
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
            ColorBlock colors = targetButton.colors;
            colors.normalColor = buttonColor;
            colors.highlightedColor = buttonColor * 1.1f;
            colors.pressedColor = buttonColor * 0.9f;
            colors.disabledColor = Color.gray;
            targetButton.colors = colors;
        }
    }

    // ========================================================================
    // ModeEPetSpawner — 召唤煤球辅助类
    // ========================================================================

    /// <summary>
    /// Mode E 召唤煤球辅助类。
    /// 提供静态方法用于生成煤球宠物NPC。
    /// </summary>
    public static class ModeEPetSpawner
    {
        /// <summary>缓存的煤球预设（避免重复查找）</summary>
        private static CharacterRandomPreset cachedCoalballPreset = null;

        /// <summary>
        /// 异步生成煤球宠物（供 Harmony patch 调用）
        /// </summary>
        public static void SpawnPet()
        {
            var inst = ModBehaviour.Instance;
            int modeFSessionToken = inst != null ? inst.CurrentModeFSessionToken : 0;
            int modeESessionToken = inst != null ? inst.CurrentModeESessionToken : 0;
            int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            SpawnPetAsync(modeFSessionToken, relatedScene, modeESessionToken, relatedScene).Forget();
        }

        /// <summary>
        /// 清理缓存（Mode E 结束时调用）
        /// </summary>
        public static void ClearCache()
        {
            cachedCoalballPreset = null;
        }

        /// <summary>
        /// 重置宠物NPC的雇佣交互点状态，防止位置哈希导致的状态复用
        /// 原版游戏使用位置哈希作为 requireItemUsed 的存储键，
        /// 相同位置生成的NPC会共享状态，导致第一次雇佣后后续不再需要消耗物品
        /// </summary>
        private static void ResetPetHireInteractable(GameObject petGo)
        {
            try
            {
                if (petGo == null) return;

                // 查找宠物NPC上的所有 InteractableBase 组件
                var interactables = petGo.GetComponentsInChildren<InteractableBase>(true);
                if (interactables == null || interactables.Length == 0)
                {
                    ModBehaviour.DevLog("[ModeE] 煤球NPC上未找到 InteractableBase");
                    return;
                }

                foreach (var interact in interactables)
                {
                    if (interact == null) continue;

                    // 通过反射重置 requireItem 和 requireItemUsed 状态
                    try
                    {
                        // 获取 requireItem 字段
                        var requireItemField = typeof(InteractableBase).GetField("requireItem",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        // 获取 requireItemUsed 字段
                        var requireItemUsedField = typeof(InteractableBase).GetField("requireItemUsed",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        // 获取 requireItemId 字段（用于判断是否是雇佣交互）
                        var requireItemIdField = typeof(InteractableBase).GetField("requireItemId",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                        if (requireItemField != null && requireItemIdField != null)
                        {
                            int itemId = (int)requireItemIdField.GetValue(interact);
                            // 只重置需要 ID=388 物品的交互点（雇佣交互）
                            if (itemId == 388)
                            {
                                requireItemField.SetValue(interact, true);
                                if (requireItemUsedField != null)
                                {
                                    requireItemUsedField.SetValue(interact, false);
                                }
                                ModBehaviour.DevLog("[ModeE] 已重置煤球雇佣交互点状态 (requireItemId=388)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ModBehaviour.DevLog("[ModeE] [WARNING] 重置交互点状态失败: " + ex.Message);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] ResetPetHireInteractable 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 异步生成煤球NPC
        /// </summary>
        private static async UniTaskVoid SpawnPetAsync(
            int modeFSessionToken,
            int modeFRelatedScene,
            int modeESessionToken,
            int modeESessionRelatedScene)
        {
            try
            {
                // 获取玩家位置
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 召唤煤球：玩家为空");
                    return;
                }

                // 查找煤球预设（优先使用缓存）
                CharacterRandomPreset coalballPreset = cachedCoalballPreset;

                if (coalballPreset == null)
                {
                    // 优先从 ModBehaviour 的缓存字典查找
                    var inst = ModBehaviour.Instance;
                    if (inst != null)
                    {
                        // 通过反射获取 cachedCharacterPresets（如果可访问）
                        // 回退到 FindObjectsOfTypeAll
                        try
                        {
                            var allPresets = ObjectCache.GetCharacterPresets();
                            foreach (var preset in allPresets)
                            {
                                if (preset == null) continue;
                                try
                                {
                                    string nameKey = preset.nameKey;
                                    if (!string.IsNullOrEmpty(nameKey) && nameKey.Contains("SnowPMC"))
                                    {
                                        coalballPreset = preset;
                                        cachedCoalballPreset = preset; // 缓存以供后续使用
                                        ModBehaviour.DevLog("[ModeE] 找到煤球预设: " + nameKey);
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }

                if (coalballPreset == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 未找到煤球预设 (Character_SnowPMC)，无法召唤");
                    NotificationText.Push(L10n.T("未找到煤球预设", "Coalball preset not found"));
                    return;
                }

                // 在玩家前方生成煤球
                Vector3 spawnPos = player.transform.position + player.transform.forward * 1.5f;
                Vector3 dir = -player.transform.forward;
                var coalballCharacter = await coalballPreset.CreateCharacterAsync(spawnPos, dir, modeFSessionToken > 0 ? modeFRelatedScene : modeESessionRelatedScene, null, false);
                if (coalballCharacter == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 煤球生成失败");
                    return;
                }

                // 设置煤球为玩家阵营
                var inst2 = ModBehaviour.Instance;
                if (inst2 == null ||
                    !inst2.IsModeEOrModeFSpawnSessionStillValid(
                        modeFSessionToken,
                        modeFRelatedScene,
                        modeESessionToken,
                        modeESessionRelatedScene))
                {
                    try
                    {
                        if (coalballCharacter.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(coalballCharacter.gameObject);
                        }
                    }
                    catch { }

                    ModBehaviour.DevLog("[ModeE] 煤球生成完成时模式已结束或场景已切换，已放弃该实例");
                    return;
                }

                if (inst2 != null)
                {
                    coalballCharacter.SetTeam(inst2.ModeEPlayerFaction);
                }
                else
                {
                    coalballCharacter.SetTeam(Teams.player);
                }

                // [修复] 重置煤球NPC的雇佣交互点状态，防止位置哈希导致的状态复用
                // 原版游戏使用位置哈希作为 requireItemUsed 的存储键，
                // 相同位置生成的NPC会共享状态，导致第一次雇佣后后续不再需要消耗物品
                ResetPetHireInteractable(coalballCharacter.gameObject);

                ModBehaviour.DevLog("[ModeE] 煤球召唤成功");
                NotificationText.Push(L10n.T("煤球已召唤！", "Coalball summoned!"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] SpawnPetAsync 失败: " + e.Message);
            }
        }
    }
}
