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
    /// 使用 TMP 的 <link> 标签实现可点击文本区域。
    /// 悬停时提亮当前链接并播官方 UI/hover、点中时播 UI/click（审美审查 UA-29：旧写法鼠标移上去毫无变化）。
    /// </summary>
    public class DepositLinkClickHandler : MonoBehaviour, IPointerClickHandler, IPointerMoveHandler, IPointerExitHandler
    {
        private TextMeshProUGUI textComponent;
        private Camera uiCamera;

        void Awake()
        {
            textComponent = GetComponent<TextMeshProUGUI>();
        }

        private string FindLinkId(Vector2 screenPosition)
        {
            if (textComponent == null) return null;

            // 获取 UI 相机（用于坐标转换）
            if (uiCamera == null)
            {
                Canvas canvas = textComponent.canvas;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    uiCamera = canvas.worldCamera;
                }
            }

            int linkIndex = TMP_TextUtilities.FindIntersectingLink(textComponent, screenPosition, uiCamera);
            if (linkIndex < 0 || textComponent.textInfo == null || linkIndex >= textComponent.textInfo.linkCount) return null;
            return textComponent.textInfo.linkInfo[linkIndex].GetLinkID();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (!StorageDepositService.IsServiceActive || eventData == null) return;
            StorageDepositService.SetHoveredDepositLink(FindLinkId(eventData.position));
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            StorageDepositService.SetHoveredDepositLink(null);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!StorageDepositService.IsServiceActive) return;
            if (textComponent == null) return;

            // 检测点击的链接
            string linkID = FindLinkId(eventData.position);
            if (string.IsNullOrEmpty(linkID)) return;

            BossRushUISound.PlayClick();
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
        private static int depositSessionGeneration;
        private static DepositTransaction transactionOwner;

        private sealed class DepositTransaction
        {
            internal int Session;
            internal int Slot;
            internal CharacterMainControl Player;
            internal Transform Npc;
            internal bool Purification;
        }

        private static bool IsTransactionBusy { get { return transactionOwner != null; } }

        private static DepositTransaction TryBeginTransaction()
        {
            if (!isServiceActive || IsTransactionBusy || !DepositDataManager.CanWrite) return null;
            var transaction = new DepositTransaction
            {
                Session = depositSessionGeneration,
                Slot = Saves.SavesSystem.CurrentSlot,
                Player = CharacterMainControl.Main,
                Npc = courierNPCTransform,
                Purification = IsZombieModeTemporaryCourierPurificationService()
            };
            if (transaction.Player == null) return null;
            transactionOwner = transaction;
            return transaction;
        }

        private static bool IsCurrentTransaction(DepositTransaction transaction)
        {
            return transaction != null && ReferenceEquals(transactionOwner, transaction)
                && transaction.Session == depositSessionGeneration && isServiceActive
                && transaction.Slot == Saves.SavesSystem.CurrentSlot
                && transaction.Player != null && transaction.Player == CharacterMainControl.Main
                && DepositDataManager.CanWrite;
        }

        private static void EndTransaction(DepositTransaction transaction)
        {
            if (ReferenceEquals(transactionOwner, transaction)) transactionOwner = null;
        }

        internal static bool OwnsShop(StockShop shop)
        {
            return shop != null && ReferenceEquals(shop, depositShop);
        }

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

        // 「全部取出 | 全部丢弃」两条链接的颜色：一律由共享 token 预先转成十六进制（审美审查 UA-29 / UA-31），
        // 不再写 #33CC33 / #CC3333 这类纯色。悬停色是同色相向白提亮 35%。
        private static readonly string DepositSuccessHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.SuccessText);
        private static readonly string DepositSuccessHoverHex = "#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(BossRushUIColors.SuccessText, Color.white, 0.35f));
        private static readonly string DepositDangerHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText);
        private static readonly string DepositDangerHoverHex = "#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(BossRushUIColors.DangerText, Color.white, 0.35f));
        private static readonly string DepositMutedHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary);
        private static readonly string DepositTextHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextPrimary);

        // 鼠标当前悬停的链接 id（retrieve / discard / null），只在变化时重排一次文字。
        private static string hoveredDepositLink = null;
        // 「全部丢弃」确认框在等玩家回答：防止连点弹出第二个。
        private static bool discardConfirmPending = false;

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
            discardConfirmPending = false;
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
