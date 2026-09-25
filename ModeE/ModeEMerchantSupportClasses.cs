// Mode E merchant runtime: ModeEMerchantSupportClasses.cs
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
        private const string MODE_E_SHELL_HARMONY_OWNER = "com.bossrush.mod";
        // TryGetIDByName 按 ItemMetaData.Name 查询；Item_SeaShell 是本地化键，不是物品内部名。
        private const string MODE_E_SHELL_ITEM_NAME = "SeaShell";
        private const long MODE_E_SHELL_CASH_UNIT = 2500L;
        // 子弹整组现金基数低且商店 priceFactor 为 1（其余分类 ×10），
        // 贝壳侧额外乘整组溢价，避免一组子弹只值 1~2 贝壳。
        private const long MODE_E_SHELL_BULLET_STACK_PRICE_FACTOR = 5L;

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
                    BossRushEagerReflectionCache.StockShop_MerchantID, typeof(string), false),
                    "StockShop.merchantID:string");
                RecordModeEShellContractFailure(failures, IsModeEShellFieldContract(
                    BossRushEagerReflectionCache.StockShop_AccountAvaliable, typeof(bool), false),
                    "StockShop.accountAvaliable:bool");
                RecordModeEShellContractFailure(failures,
                    ModeEMerchantSellAllUI.VerifyModeEShellRuntimeContracts(),
                    "ModeEMerchantSellAllUI runtime contracts");

                if (failures.Count > 0)
                {
                    ModBehaviour.DevLog("[ModeE/Shell] M0/M1 反射契约不匹配: " +
                        string.Join(", ", failures.ToArray()));
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE/Shell] M0/M1 反射契约解析失败: " + e.Message);
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
                ModBehaviour.DevLog("[ModeE/Shell] 交易/UI Harmony 补丁安装证明失败，分类商店保持关闭: " +
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
                    ModBehaviour.DevLog("[ModeE/Shell] 贝壳物品解析失败: ItemMetaData.Name=" +
                        MODE_E_SHELL_ITEM_NAME + ", localizationKey=Item_SeaShell");
                }
            }
            catch (Exception e)
            {
                modeEShellItemTypeID = -1;
                ModBehaviour.DevLog("[ModeE/Shell] 贝壳物品解析异常: ItemMetaData.Name=" +
                    MODE_E_SHELL_ITEM_NAME + ", " + e.Message);
            }

            bool contractsReady = modeEShellItemTypeID >= 0 && VerifyModeEShellPatchInstallation();
            modeEShellEconomyAvailable = contractsReady;
            if (!contractsReady)
            {
                SetModeEShellEconomyUnavailable("session preflight failed", false);
                return;
            }

            ModBehaviour.DevLog("[ModeE/Shell] M1 capability ready: session=" + sessionToken +
                ", sessionGeneration=" + modeEShellSessionGeneration +
                ", SeaShell=" + modeEShellItemTypeID);
        }

        private bool IsCurrentModeEShellSession(int sessionToken, int sceneBuildIndex, long sessionGeneration)
        {
            return modeEActive &&
                   !modeEHost.IsModeFActive &&
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
                catch (Exception e) { ModBehaviour.DevLog("[ModeE/Shell] 余额订阅者异常: " + e.Message); }
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
                catch (Exception e) { ModBehaviour.DevLog("[ModeE/Shell] 价格订阅者异常: " + e.Message); }
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
                catch (Exception e) { ModBehaviour.DevLog("[ModeE/Shell] 交易门订阅者异常: " + e.Message); }
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
            ModBehaviour.DevLog("[ModeE/Shell] Credit " + amount + " (" + reason + "), balance=" + modeEShellBalance);
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

        internal Sprite CurrentModeEShellIcon
        {
            get
            {
                try
                {
                    Item shellPrefab = modeEShellItemTypeID >= 0
                        ? ItemAssetsCollection.GetPrefab(modeEShellItemTypeID)
                        : null;
                    return shellPrefab != null ? shellPrefab.Icon : null;
                }
                catch
                {
                    return null;
                }
            }
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
}
