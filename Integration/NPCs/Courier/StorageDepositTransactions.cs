// ============================================================================
// StorageDepositTransactions.cs - 寄存交易与商店显示
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Cysharp.Threading.Tasks;
using BossRush.Utils;

namespace BossRush
{
    public static partial class StorageDepositService
    {
        private static void OnItemSoldByPlayer(StockShop shop, Item soldItem, int price)
        {
            // 检查是否是我们的商店
            if (shop != depositShop) return;
            if (!isServiceActive) return;

            ModBehaviour.DevLog("[StorageDepositService] 拦截到售出事件: " +
                (soldItem != null ? soldItem.DisplayName : "null") + ", 价格: " + price);

            if (soldItem == null)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 无法获取售出物品信息");
                return;
            }

            // 异步处理寄存逻辑
            HandleDepositAsync(soldItem, price).Forget();
        }

        /// <summary>
        /// 异步处理物品寄存（优化：只更新新增的物品，不刷新全部）
        /// </summary>
        private static async UniTaskVoid HandleDepositAsync(Item soldItem, int price)
        {
            try
            {
                int newIndex = await StoreItemInDepositDataAsync(soldItem, price, "StorageDeposit.HandleDeposit");
                if (newIndex < 0)
                {
                    return;
                }

                // 刷新商品列表（添加新条目）
                RefreshShopEntries();

                // 刷新 UI 显示（只更新新增的条目）
                RefreshShopUIAndUpdateNewEntry(newIndex);

                // 更新"全部取出"按钮
                UpdateRetrieveAllButton();

                // 显示通知（使用本地化键）
                string msg = LocalizationHelper.GetLocalizedText("BossRush_StorageDeposit_Deposited");
                NotificationText.Push(msg);
                UpdateQuickDepositButtonState();

                ModBehaviour.DevLog("[StorageDepositService] 寄存完成: " + (soldItem != null ? soldItem.DisplayName : "null"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [ERROR] 处理寄存失败: " + e.Message);
            }
        }

        /// <summary>
        /// 单件/批量寄存共用的核心写入流程。
        /// 保持与单件寄存相同的扣费、写入和实例缓存口径。
        /// </summary>
        private static async UniTask<int> StoreItemInDepositDataAsync(Item item, int price, string runtimeContext)
        {
            if (item == null)
            {
                return -1;
            }

            string itemName = item.DisplayName;

            // 与单件寄存保持同一扣费口径。
            if (price > 0)
            {
                bool paid = false;
                if (courierNPCTransform != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(courierNPCTransform))
                {
                    paid = ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(
                        courierNPCTransform,
                        price,
                        "ZombieModeTempCourierDepositStore");
                }
                else
                {
                    var cost = new Cost(price);
                    if (cost.Enough)
                    {
                        cost.Pay();
                        paid = true;
                    }
                }

                if (paid)
                {
                    ModBehaviour.DevLog("[StorageDepositService] 已扣除售出所得: " + price);
                }
                else
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 余额不足以扣除售出所得: " + price + "，跳过扣费");
                }
            }

            int newIndex;
            if (!TryStoreItemInDepositData(item, out newIndex))
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 写入寄存数据失败: " + itemName);
                return -1;
            }

            ModBehaviour.DevLog("[StorageDepositService] 物品已存入寄存数据: " + itemName + ", index=" + newIndex);

            bool cached = await CacheDepositedItemInstanceAsync(newIndex, runtimeContext);
            if (cached)
            {
                Item restoredItem;
                if (depositItemInstances.TryGetValue(newIndex, out restoredItem) && restoredItem != null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] 恢复新寄存物品实例: index=" + newIndex + ", Name=" + restoredItem.DisplayName);
                }
            }

            return newIndex;
        }

        /// <summary>
        /// 获取单件寄存对应的卖出价格，保持与原版商店卖出一致。
        /// </summary>
        private static int GetDepositSellPrice(Item item)
        {
            if (item == null || depositShop == null)
            {
                return 0;
            }

            try
            {
                return Mathf.Max(0, depositShop.ConvertPrice(item, true));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 计算寄存卖出价格失败: " + item.DisplayName + ", " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// 刷新商店 UI 显示
        /// </summary>
        private static void RefreshShopUI()
        {
            // 调用异步版本
            RefreshShopUIAsync().Forget();
        }

        /// <summary>
        /// 异步刷新商店 UI 显示（等待 UI 创建完成后再更新）
        /// </summary>
        private static async UniTaskVoid RefreshShopUIAsync()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView != null && depositShop != null)
                {
                    InitializeReflection();

                    if (setupAndShowMethod != null)
                    {
                        setupAndShowMethod.Invoke(shopView, new object[] { depositShop });
                        ModBehaviour.DevLog("[StorageDepositService] 商店 UI 已刷新");

                        // 等待几帧让 UI 完成创建
                        await UniTask.DelayFrame(2);

                        // 【关键】先隐藏无效条目（已购买的物品），再更新有效条目的显示
                        // 不再调用 ForceActivateAllEntries()，因为它会错误地激活已购买的旧条目
                        HideInvalidUIEntries();

                        // 刷新后更新所有商品条目的 ItemDisplay 和价格
                        UpdateAllItemDisplays();
                    }
                    else
                    {
                        ModBehaviour.DevLog("[StorageDepositService] [WARNING] 未找到 SetupAndShow 方法");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 刷新商店 UI 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 更新所有商品条目的 ItemDisplay（使用正确的物品实例）
        /// 解决原版 itemInstances 使用 TypeID 作为 key 导致同类型物品图标显示错误的问题
        /// 关键：通过 StockShopItemEntry.Target 获取 Entry，再通过 entryIndexMapping 找到正确的 depositIndex
        /// 注意：不再强制激活所有条目，由 HideInvalidUIEntries() 负责隐藏无效条目
        /// 支持同类型物品独立显示：使用 Entry 集合而不是 TypeID 集合判断有效性
        /// </summary>
        private static void UpdateAllItemDisplays()
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null || depositShop == null) return;

                // 调试：输出 entries 数量
                ModBehaviour.DevLog("[StorageDepositService] depositShop.entries.Count = " + depositShop.entries.Count);
                ModBehaviour.DevLog("[StorageDepositService] entryIndexMapping.Count = " + entryIndexMapping.Count);
                ModBehaviour.DevLog("[StorageDepositService] depositItemInstances.Count = " + depositItemInstances.Count);

                InitializeReflection();
                if (entryTemplateField == null) return;

                var entryTemplate = entryTemplateField.GetValue(shopView) as StockShopItemEntry;
                if (entryTemplate == null) return;

                Transform entriesParent = entryTemplate.transform.parent;
                if (entriesParent == null) return;

                // 获取 StockShopItemEntry 的 itemDisplay 字段
                if (stockShopItemDisplayField == null)
                {
                    ModBehaviour.DevLog("[StorageDepositService] [WARNING] 未找到 itemDisplay 字段");
                    return;
                }

                // 调试：输出父容器中的子对象数量
                int totalChildren = entriesParent.childCount;
                int activeChildren = 0;
                foreach (Transform child in entriesParent)
                {
                    if (child.gameObject.activeSelf) activeChildren++;
                }
                ModBehaviour.DevLog("[StorageDepositService] UI 父容器子对象: 总数=" + totalChildren + ", 激活=" + activeChildren);

                // 只激活在有效 Entry 集合中但被隐藏的条目
                int activatedCount = 0;
                foreach (Transform child in entriesParent)
                {
                    if (child == entryTemplate.transform) continue;

                    var uiEntry = child.GetComponent<StockShopItemEntry>();
                    if (uiEntry == null) continue;

                    var stockEntry = uiEntry.Target;
                    if (stockEntry == null) continue;

                    // 只激活在有效 Entry 集合中的条目（精确匹配 Entry 对象）
                    if (!child.gameObject.activeSelf && IsCurrentDepositEntry(stockEntry))
                    {
                        child.gameObject.SetActive(true);
                        activatedCount++;
                        ModBehaviour.DevLog("[StorageDepositService] 激活有效条目: TypeID=" + stockEntry.ItemTypeID);
                    }
                }

                if (activatedCount > 0)
                {
                    ModBehaviour.DevLog("[StorageDepositService] 激活了 " + activatedCount + " 个有效条目");
                }

                // 更新所有激活条目的 ItemDisplay 和价格显示
                int updatedCount = 0;
                int skippedInactive = 0;
                int skippedNoEntry = 0;
                int skippedNoMapping = 0;
                List<DepositedItemData> depositedItemsSnapshot = null;

                foreach (Transform child in entriesParent)
                {
                    // 跳过模板对象
                    if (child == entryTemplate.transform) continue;

                    // 检查是否激活
                    if (!child.gameObject.activeSelf)
                    {
                        skippedInactive++;
                        continue;
                    }

                    var uiEntry = child.GetComponent<StockShopItemEntry>();
                    if (uiEntry == null) continue;

                    // 获取 UI 条目对应的 StockShop.Entry
                    var stockEntry = uiEntry.Target;
                    if (stockEntry == null)
                    {
                        skippedNoEntry++;
                        continue;
                    }

                    // 通过 entryIndexMapping 找到正确的 depositIndex
                    int depositIndex;
                    if (!entryIndexMapping.TryGetValue(stockEntry, out depositIndex))
                    {
                        skippedNoMapping++;
                        ModBehaviour.DevLog("[StorageDepositService] [WARNING] 未找到 Entry 对应的 depositIndex, TypeID=" + stockEntry.ItemTypeID);
                        continue;
                    }

                    // 从我们的缓存获取正确的物品实例
                    Item correctItem;
                    if (depositItemInstances.TryGetValue(depositIndex, out correctItem) && correctItem != null)
                    {
                        // 获取 ItemDisplay 并更新
                        var itemDisplay = stockShopItemDisplayField.GetValue(uiEntry) as ItemDisplay;
                        if (itemDisplay != null)
                        {
                            itemDisplay.Setup(correctItem);
                            ModBehaviour.DevLog("[StorageDepositService] 更新商品条目 ItemDisplay: index=" + depositIndex + ", Name=" + correctItem.DisplayName);
                            updatedCount++;
                        }

                        // 更新价格显示（使用正确的寄存费用）
                        if (stockShopItemPriceTextField != null)
                        {
                            var priceTextComp = stockShopItemPriceTextField.GetValue(uiEntry) as TMPro.TextMeshProUGUI;
                            if (priceTextComp != null)
                            {
                                if (depositedItemsSnapshot == null)
                                {
                                    depositedItemsSnapshot = DepositDataManager.GetAllItems();
                                }

                                if (depositIndex < depositedItemsSnapshot.Count)
                                {
                                    int fee = depositedItemsSnapshot[depositIndex].GetCurrentFee();
                                    priceTextComp.text = FormatRetrieveFeeText(fee);
                                    ModBehaviour.DevLog("[StorageDepositService] 更新价格显示: index=" + depositIndex + ", fee=" + fee);
                                }
                            }
                        }

                    }
                }

                ModBehaviour.DevLog("[StorageDepositService] 已更新 " + updatedCount + " 个商品条目的 ItemDisplay");
                ModBehaviour.DevLog("[StorageDepositService] 跳过: 未激活=" + skippedInactive + ", 无Entry=" + skippedNoEntry + ", 无映射=" + skippedNoMapping);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 更新 ItemDisplay 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 刷新商店 UI 并更新所有条目（修复：同类型物品价格显示问题）
        /// 原版 Setup() 使用 GetItemInstanceDirect(TypeID) 获取物品实例，
        /// 同一 TypeID 的所有物品会获取到同一个实例，导致价格显示相同。
        /// 因此必须在 SetupAndShow 后调用 UpdateAllItemDisplays() 更新所有条目的价格。
        /// </summary>
        private static void RefreshShopUIAndUpdateNewEntry(int newIndex)
        {
            try
            {
                var shopView = StockShopView.Instance;
                if (shopView == null || depositShop == null) return;

                InitializeReflection();

                if (setupAndShowMethod != null)
                {
                    setupAndShowMethod.Invoke(shopView, new object[] { depositShop });
                    ModBehaviour.DevLog("[StorageDepositService] 商店 UI 已刷新（新增物品 index=" + newIndex + "）");

                    // 【修复】更新所有条目的 ItemDisplay 和价格，而不是只更新新增的
                    // 原因：SetupAndShow 会重新调用每个条目的 Setup()，
                    // 而 Setup() 使用 GetItemInstanceDirect(TypeID) 获取物品实例，
                    // 同类型物品会获取到同一个实例，导致价格显示为第一个物品的价格
                    UpdateAllItemDisplays();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] 刷新商店 UI 失败: " + e.Message);
            }
        }


        /// <summary>
        /// 商品购买事件处理（物品取回）
        /// 关键：商店给的是空白物品，需要替换为保存的完整物品（包含配件和容器内容）
        /// 修复：使用 entryIndexMapping 获取正确的索引，而不是通过 TypeID 查找（避免同类型物品冲突）
        /// </summary>
    }
}
