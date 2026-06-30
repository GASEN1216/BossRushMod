// ============================================================================
// StorageDepositService.cs - 阿稳寄存服务核心逻辑
// ============================================================================
// 模块说明：
//   管理"阿稳寄存"服务功能，包括：
//   - 创建和配置寄存商店（使用 StockShop 组件）
//   - 处理物品存入（双击背包物品）
//   - 处理物品取回（购买商品）
//   - 动态计算寄存费用
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
    /// <summary>
    /// TMP 链接点击处理器（处理"全部取出"和"全部丢弃"的点击）
    /// 使用 TMP 的 <link> 标签实现可点击文本区域
    /// </summary>
    public class DepositLinkClickHandler : MonoBehaviour, IPointerClickHandler
    {
        private TextMeshProUGUI textComponent;
        private Camera uiCamera;

        void Awake()
        {
            textComponent = GetComponent<TextMeshProUGUI>();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!StorageDepositService.IsServiceActive) return;
            if (textComponent == null) return;

            // 获取 UI 相机（用于坐标转换）
            if (uiCamera == null)
            {
                Canvas canvas = textComponent.canvas;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    uiCamera = canvas.worldCamera;
                }
            }

            // 检测点击的链接
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(textComponent, eventData.position, uiCamera);
            if (linkIndex < 0) return;

            // 获取链接信息
            TMP_LinkInfo linkInfo = textComponent.textInfo.linkInfo[linkIndex];
            string linkID = linkInfo.GetLinkID();

            ModBehaviour.DevLog("[DepositLinkClickHandler] 点击链接: " + linkID);

            // 根据链接 ID 执行对应操作
            if (linkID == "retrieve")
            {
                StorageDepositService.OnRetrieveAllClickedPublic();
            }
            else if (linkID == "discard")
            {
                StorageDepositService.OnDiscardAllClickedPublic();
            }
        }
    }

    /// <summary>
    /// 阿稳寄存服务核心逻辑（静态类）
    /// </summary>
    public static partial class StorageDepositService
    {
        // ============================================================================
        // 私有字段
        // ============================================================================

        // 商店相关
        private static GameObject shopObject;
        private static StockShop depositShop;

        // NPC 引用
        private static Transform courierNPCTransform;
        private static CourierNPCController courierController;
        private static CourierMovement courierMovement;

        // 服务状态
        private static bool isServiceActive = false;
        private static bool isQuickDepositInProgress = false;
        private static bool isRetrieveAllInProgress = false;

        // 商品索引映射（Entry -> DepositedItemData 索引）
        private static Dictionary<StockShop.Entry, int> entryIndexMapping = new Dictionary<StockShop.Entry, int>();

        // 常量
        private const float GOODBYE_BUBBLE_DURATION = 4f;
        private const float BUBBLE_Y_OFFSET = 1.5f;

        // 反射缓存
        private static FieldInfo textSellField = null;
        private static FieldInfo priceTextField = null;
        private static FieldInfo itemInstancesField = null;
        private static FieldInfo accountAvaliableField = null;
        private static MethodInfo cacheItemInstancesMethod = null;
        private static MethodInfo setupAndShowMethod = null;
        private static FieldInfo entryTemplateField = null;
        private static FieldInfo stockShopItemDisplayField = null;
        private static FieldInfo stockShopItemPriceTextField = null;
        private static FieldInfo refreshCountDownField = null;
        private static FieldInfo stockEntryInnerField = null;      // StockShop.Entry 内部的 entry 字段
        private static FieldInfo innerEntryPriceFactorField = null; // StockShopDatabase.ItemEntry 的 priceFactor 字段
        private static FieldInfo playerInventoryDisplayField = null;
        private static FieldInfo characterInventoryDisplayField = null;
        private static FieldInfo sortButtonField = null;
        private static FieldInfo interactionButtonField = null;
        private static FieldInfo interactionTextField = null;
        private static bool reflectionInitialized = false;

        // "全部取出"按钮相关
        private static GameObject retrieveAllButtonObj = null;
        #pragma warning disable CS0414
        private static UnityEngine.UI.Button retrieveAllButton = null;
        #pragma warning restore CS0414
        private static TMPro.TextMeshProUGUI retrieveAllText = null;
        private static TMPro.TextMeshProUGUI originalRefreshCountDown = null;
        private static GameObject hiddenRefreshLabel = null;  // 被隐藏的"下次刷新"标签
        private static Color colorEnough = new Color(0.2f, 0.8f, 0.2f);  // 绿色
        private static Color colorNotEnough = new Color(0.9f, 0.2f, 0.2f);  // 红色
        private static Color colorDiscard = new Color(0.7f, 0.3f, 0.3f);  // 丢弃按钮颜色（暗红色）

        // "全部丢弃"按钮相关
        private static GameObject discardAllButtonObj = null;
        #pragma warning disable CS0414
        private static UnityEngine.UI.Button discardAllButton = null;
        private static TMPro.TextMeshProUGUI discardAllText = null;
        #pragma warning restore CS0414

        // 玩家背包侧的一键寄存按钮
        private static GameObject quickDepositButtonObj = null;
        private static UnityEngine.UI.Button quickDepositButton = null;
        private static TMPro.TextMeshProUGUI quickDepositButtonText = null;
        private static Inventory playerInventory = null;

        // 物品实例缓存（使用唯一索引作为 key，避免同类型物品冲突）
        private static Dictionary<int, Item> depositItemInstances = new Dictionary<int, Item>();

        // 原始售出文字（用于恢复）
        private static string originalTextSell = null;

        // 待寄存物品（用于拦截售出逻辑）
        #pragma warning disable CS0414
        private static Item pendingDepositItem = null;
        #pragma warning restore CS0414

        public static void ResetStaticCaches()
        {
            try
            {
                UnregisterEvents();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] ResetStaticCaches 解绑事件失败: " + e.Message);
            }

            try
            {
                RestoreShopUIText();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] ResetStaticCaches 恢复 UI 文本失败: " + e.Message);
            }

            try
            {
                if (courierController != null)
                {
                    courierController.StopTalking();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] ResetStaticCaches 停止对话失败: " + e.Message);
            }

            try
            {
                if (courierMovement != null)
                {
                    courierMovement.SetInService(false);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[StorageDepositService] [WARNING] ResetStaticCaches 恢复移动失败: " + e.Message);
            }

            isServiceActive = false;
            isQuickDepositInProgress = false;
            isRetrieveAllInProgress = false;
            pendingDepositItem = null;

            Cleanup();

            textSellField = null;
            priceTextField = null;
            itemInstancesField = null;
            accountAvaliableField = null;
            cacheItemInstancesMethod = null;
            setupAndShowMethod = null;
            entryTemplateField = null;
            stockShopItemDisplayField = null;
            stockShopItemPriceTextField = null;
            refreshCountDownField = null;
            stockEntryInnerField = null;
            innerEntryPriceFactorField = null;
            playerInventoryDisplayField = null;
            characterInventoryDisplayField = null;
            sortButtonField = null;
            interactionButtonField = null;
            interactionTextField = null;
            reflectionInitialized = false;
            originalTextSell = null;
        }

        private static bool IsCurrentDepositEntry(StockShop.Entry stockEntry)
        {
            return stockEntry != null && entryIndexMapping.ContainsKey(stockEntry);
        }

        private sealed class RetrieveAllDepositItem
        {
            public int DepositIndex;
            public DepositedItemData DepositData;
            public Item RestoredItem;
            public int Fee;
        }

        // ============================================================================
        // 公共属性
        // ============================================================================

        /// <summary>
        /// 检查服务是否激活
        /// </summary>
        public static bool IsServiceActive { get { return isServiceActive; } }

        // ============================================================================
        // 公共方法
        // ============================================================================

        /// <summary>
        /// 打开寄存服务（由 CourierStorageInteractable 调用）
        /// </summary>
    }
}
