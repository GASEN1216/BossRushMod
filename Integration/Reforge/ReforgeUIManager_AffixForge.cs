// ============================================================================
// ReforgeUIManager_AffixForge.cs - 锻造 UI 的「词缀锻造」模式（ReforgeUIManager 的 partial 续）
// ============================================================================
// 模块职责：
//   重铸与词缀锻造共用官方 ItemDecomposeView 这一套被劫持的界面。本文件只放
//   「词缀模式与重铸模式的差异」，重铸模式的语义一行不改：
//     - 背包/仓库过滤谓词换成 AffixForgeSystem.CanAffixForge
//     - 隐藏金钱滑块 / 正负极性倾向滑块 / 冷淬液计数（词缀费用固定、不做加权）
//     - 主按钮文字改「随机词缀」
//     - 右侧插一块词缀面板：≤3 行，图标 56px + 名称 + 一句话描述 + 锁定按钮
//     - probabilityText 复用成「费用 / 熔石 / 提示」三行文本
//
// 为什么模式枚举与状态字段声明在这个新文件里：
//   ForgeUIMode 与 currentForgeMode 放在新 partial 文件，共享文件（ReforgeUIManager.cs /
//   _ComparisonAndState.cs / _RuntimeAndCleanup.cs）只需要各插一行分流调用，
//   零字段新增、零语义改写。分流点清单见交付说明。
//
// 硬约束（AGENTS 4.14 UI 共享库）：
//   1. 本面板寄生在官方 View 的 Canvas 下，**不新建 Canvas**，因此不涉及 sortingOrder；
//      将来若需要独立 Canvas 必须用 BossRushUILayers 常量，禁止魔法数字。
//   2. 颜色一律取 BossRushUIColors，底图一律 BossRushUI.ApplyPanelSkin。
//   3. 文本一律 ZombieModeUIHelper.CreateTMPText（内部已 GetGameFont），新文本用 TMP；
//      严禁 Resources.GetBuiltinResource<Font>("Arial.ttf")（渲染不了中文）。
//   4. 按钮回调必须是命名方法且不捕获 Item —— 三个槽各配一个薄包装方法，
//      内部现取 selectedItem，避免闭包持有已销毁的物品引用。
//   5. 两模式共用同一条关闭路径（ReforgeUIMonitor → Cleanup），
//      CleanupAffixForgeUI 必须把模式复位成 Reforge，这是互踩的最后一道防线。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>锻造 UI 的两种模式。现有调用一律 Reforge，语义零变化。</summary>
    internal enum ForgeUIMode
    {
        Reforge = 0,
        AffixForge = 1
    }

    public static partial class ReforgeUIManager
    {
        // ============================================================================
        // 常量（避免魔法数字）
        // ============================================================================
        private const string AFFIX_PANEL_NAME = "AffixForgePanel";
        private const int AFFIX_PANEL_WIDTH = 460;
        private const int AFFIX_PANEL_PADDING = 8;
        private const int AFFIX_PANEL_SPACING = 6;
        private const int AFFIX_PANEL_CORNER_RADIUS = 8;
        private const int AFFIX_ICON_SIZE = 56;
        private const int AFFIX_ROW_HEIGHT = 64;
        private const int AFFIX_STONE_ROW_HEIGHT = 44;
        private const int AFFIX_STONE_ICON_SIZE = 36;
        private const int AFFIX_NAME_FONT_SIZE = 20;
        private const int AFFIX_DESC_FONT_SIZE = 15;
        private const int AFFIX_STONE_FONT_SIZE = 22;
        private const int AFFIX_LOCK_BUTTON_WIDTH = 84;
        private const int AFFIX_LOCK_BUTTON_HEIGHT = 30;
        private const int AFFIX_LOCK_FONT_SIZE = 15;
        private const int AFFIX_ICON_FALLBACK_FONT_SIZE = 34;

        // 锁定语义配色，与 PropertyEntryInteractable 一致：白=普通，蓝=悬停可操作，金=已锁定
        private static readonly Color AffixLockNormalColor = Color.white;
        private static readonly Color AffixLockHoverColor = new Color(0.4f, 0.7f, 1f, 1f);
        private static readonly Color AffixLockLockedColor = new Color(1f, 0.84f, 0f, 1f);

        // ============================================================================
        // 状态
        // ============================================================================
        private static ForgeUIMode currentForgeMode = ForgeUIMode.Reforge;

        /// <summary>OpenAffixForgeUI 置位，被 OpenUI 首行的 EnsureDefaultForgeMode 消费。</summary>
        private static bool affixEntryPending;

        private static GameObject affixPanelRoot;
        private static GameObject affixStoneContainer;
        private static TextMeshProUGUI affixStoneCountText;
        private static readonly List<AffixRowWidgets> affixRows = new List<AffixRowWidgets>();
        private static bool affixForging;

        // 被词缀模式隐藏的重铸控件，关闭时按自己的记录还原（Cleanup 会先把共享引用置 null）
        private static GameObject affixHiddenMoneySliderRoot;

        private sealed class AffixRowWidgets
        {
            public GameObject Root;
            public Image Icon;
            public TextMeshProUGUI IconFallbackText;
            public TextMeshProUGUI NameText;
            public TextMeshProUGUI DescText;
            public Button LockButton;
            public TextMeshProUGUI LockButtonText;
            public int SlotIndex;
        }

        // ============================================================================
        // 入口与模式切换
        // ============================================================================

        /// <summary>当前锻造模式。</summary>
        internal static ForgeUIMode CurrentForgeMode
        {
            get { return currentForgeMode; }
        }

        internal static void SetForgeMode(ForgeUIMode mode)
        {
            currentForgeMode = mode;
        }

        /// <summary>
        /// 以词缀模式打开锻造 UI。复用重铸那条 OpenUI 路径（含 ItemDecomposeView 的 null 守卫、
        /// 延迟改 UI 协程与 ReforgeUIMonitor），只是先把模式置成 AffixForge。
        /// </summary>
        public static void OpenAffixForgeUI(GoblinNPCController controller)
        {
            try
            {
                if (ModBehaviour.Instance == null || !ModBehaviour.Instance.IsAffixForgeConfiguredEnabled())
                {
                    ModBehaviour.DevLog("[AffixForgeUI] 开关未开启，忽略打开请求");
                    return;
                }

                affixEntryPending = true;
                currentForgeMode = ForgeUIMode.AffixForge;
                affixForging = false;

                OpenUI(controller);

                ModBehaviour.DevLog("[AffixForgeUI] UI 已打开（词缀锻造模式）");
            }
            catch (Exception e)
            {
                affixEntryPending = false;
                currentForgeMode = ForgeUIMode.Reforge;
                ModBehaviour.DevLog("[AffixForgeUI] [ERROR] 打开词缀锻造 UI 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 由 OpenUI 首行调用。只有本次打开确实来自 OpenAffixForgeUI 时才保持词缀模式，
        /// 其余一切入口（重铸子交互、丧尸模式临时哥布林）一律复位成重铸。
        /// </summary>
        internal static void EnsureDefaultForgeMode()
        {
            if (affixEntryPending)
            {
                affixEntryPending = false;
                currentForgeMode = ForgeUIMode.AffixForge;
                return;
            }

            currentForgeMode = ForgeUIMode.Reforge;
        }

        /// <summary>
        /// 背包/仓库格子的可选谓词。两模式共用一个入口，共享文件只需把
        /// ReforgeSystem.CanReforge(e) 换成本方法。
        /// </summary>
        internal static bool IsForgeSelectable(Item e)
        {
            if (e == null)
            {
                return true;
            }

            try
            {
                if (currentForgeMode == ForgeUIMode.AffixForge)
                {
                    return AffixForgeSystem.CanAffixForge(e);
                }

                return ReforgeSystem.CanReforge(e);
            }
            catch
            {
                return false;
            }
        }

        // ============================================================================
        // 六个分流点（共享文件各插一行 `if (AffixForge_HandleXxx()) return;`）
        // ============================================================================

        /// <summary>接管 ReapplyModifications。</summary>
        internal static bool AffixForge_HandleReapply()
        {
            if (currentForgeMode != ForgeUIMode.AffixForge)
            {
                return false;
            }

            try
            {
                ModifyInventoryFilter();
                ModifySlider();          // 先拿到 moneySlider 引用，随后整块隐藏
                HideReforgeOnlyWidgets();
                ApplyAffixButtonText();
                RefreshAffixPanel();
                UpdateAffixStoneCount();
                UpdateAffixProbabilityText();
                UpdateAffixButtonInteractable();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeUI] [ERROR] 重新应用修改失败: " + e.Message);
            }

            return true;
        }

        /// <summary>接管 BuildDelayedUIElements（共享文件里配 `yield break;`）。</summary>
        internal static bool AffixForge_HandleDelayedBuild()
        {
            if (currentForgeMode != ForgeUIMode.AffixForge)
            {
                return false;
            }

            try
            {
                BuildAffixPanel();
                HideReforgeOnlyWidgets();
                ApplyAffixButtonText();
                RefreshAffixPanel();
                UpdateAffixStoneCount();
                UpdateAffixProbabilityText();
                UpdateAffixButtonInteractable();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeUI] [ERROR] 延迟构建词缀面板失败: " + e.Message);
            }

            return true;
        }

        /// <summary>
        /// 接管 OnItemSelectionChanged 的后半段。
        /// 词缀模式下【不创建】属性锁定交互（PropertyEntryInteractable 是重铸专用），
        /// 也不做预制体属性对比。
        /// </summary>
        internal static bool AffixForge_HandleSelectionChanged()
        {
            if (currentForgeMode != ForgeUIMode.AffixForge)
            {
                return false;
            }

            try
            {
                bool forgeable = selectedItem != null && AffixForgeSystem.CanAffixForge(selectedItem);

                if (targetNameDisplay != null)
                {
                    targetNameDisplay.text = selectedItem != null ? selectedItem.DisplayName : "-";
                }

                if (noItemSelectedIndicator != null)
                {
                    noItemSelectedIndicator.SetActive(selectedItem == null);
                }

                if (reforgeButton != null)
                {
                    reforgeButton.gameObject.SetActive(forgeable);
                }

                HideReforgeOnlyWidgets();
                ApplyAffixButtonText();
                RefreshAffixPanel();
                UpdateAffixStoneCount();
                UpdateAffixProbabilityText();
                UpdateAffixButtonInteractable();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeUI] [ERROR] 选择变化处理失败: " + e.Message);
            }

            return true;
        }

        /// <summary>接管 ResetButtonState 与 UpdateReforgeButtonInteractable。</summary>
        internal static bool AffixForge_HandleButtonState()
        {
            if (currentForgeMode != ForgeUIMode.AffixForge)
            {
                return false;
            }

            try
            {
                bool forgeable = selectedItem != null && AffixForgeSystem.CanAffixForge(selectedItem);

                if (reforgeButton != null)
                {
                    reforgeButton.gameObject.SetActive(forgeable);
                }

                ApplyAffixButtonText();
                FixCannotForgeIndicator(forgeable);
                FixNoItemSelectedIndicator();

                if (resultDisplayObj != null)
                {
                    resultDisplayObj.SetActive(false);
                }

                if (probabilityText != null)
                {
                    probabilityText.gameObject.SetActive(true);
                }

                HideReforgeOnlyWidgets();
                UpdateAffixProbabilityText();
                UpdateAffixButtonInteractable();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeUI] [ERROR] 按钮状态处理失败: " + e.Message);
            }

            return true;
        }

        /// <summary>接管 UpdateProbabilityDisplay。</summary>
        internal static bool AffixForge_HandleProbabilityDisplay()
        {
            if (currentForgeMode != ForgeUIMode.AffixForge)
            {
                return false;
            }

            try
            {
                UpdateAffixProbabilityText();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeUI] [ERROR] 更新费用显示失败: " + e.Message);
            }

            return true;
        }

        /// <summary>接管 OnReforgeButtonClick。</summary>
        internal static bool AffixForge_HandleButtonClick()
        {
            if (currentForgeMode != ForgeUIMode.AffixForge)
            {
                return false;
            }

            try
            {
                if (affixForging || selectedItem == null)
                {
                    return true;
                }

                affixForging = true;
                AffixForgeResult result = null;
                try
                {
                    result = AffixForgeSystem.RollUnlockedSlots(selectedItem);
                }
                finally
                {
                    affixForging = false;
                }

                if (result != null && result.Success && BossRushAudioManager.Instance != null)
                {
                    BossRushAudioManager.Instance.PlayReforgeSFX();
                }

                RefreshAffixPanel();
                UpdateAffixStoneCount();
                ShowAffixResultMessage(result);
                UpdateAffixButtonInteractable();
            }
            catch (Exception e)
            {
                affixForging = false;
                ModBehaviour.DevLog("[AffixForgeUI] [ERROR] 执行词缀锻造失败: " + e.Message);
            }

            return true;
        }

        // ============================================================================
        // 清理
        // ============================================================================

        /// <summary>
        /// 由 Cleanup() 调用。注意 Cleanup 在调用本方法之前已经把 moneySlider /
        /// probabilityText 等共享引用置 null，所以还原被隐藏的控件只能靠本文件自己的记录。
        /// </summary>
        internal static void CleanupAffixForgeUI()
        {
            try
            {
                if (affixPanelRoot != null)
                {
                    affixPanelRoot.SetActive(false);
                }

                if (affixStoneContainer != null)
                {
                    affixStoneContainer.SetActive(false);
                }

                // 金钱滑块是官方控件，重铸模式还要用，必须还原
                if (affixHiddenMoneySliderRoot != null)
                {
                    affixHiddenMoneySliderRoot.SetActive(true);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeUI] [WARNING] 清理词缀 UI 异常: " + e.Message);
            }

            affixPanelRoot = null;
            affixStoneContainer = null;
            affixStoneCountText = null;
            affixHiddenMoneySliderRoot = null;
            affixRows.Clear();
            affixForging = false;
            affixEntryPending = false;

            // 模式复位：两模式互踩的最后一道防线
            currentForgeMode = ForgeUIMode.Reforge;
        }

        /// <summary>由 ResetStaticCaches() 末尾调用。</summary>
        internal static void ResetAffixForgeStaticCaches()
        {
            CleanupAffixForgeUI();
        }

        // ============================================================================
        // 面板刷新
        // ============================================================================

        private static void RefreshAffixPanel()
        {
            if (affixPanelRoot == null)
            {
                return;
            }

            bool forgeable = selectedItem != null && AffixForgeSystem.CanAffixForge(selectedItem);
            int capacity = forgeable ? AffixForgeSystem.GetSlotCount(selectedItem) : 0;

            for (int i = 0; i < affixRows.Count; i++)
            {
                AffixRowWidgets row = affixRows[i];
                if (row == null || row.Root == null)
                {
                    continue;
                }

                bool visible = row.SlotIndex <= capacity;
                row.Root.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                AffixSlotView view = default(AffixSlotView);
                bool hasSlot = AffixItemData.TryReadSlot(selectedItem, row.SlotIndex, out view);
                RefreshAffixRow(row, hasSlot, view);
            }
        }

        private static void RefreshAffixRow(AffixRowWidgets row, bool hasSlot, AffixSlotView view)
        {
            if (!hasSlot || view.IsEmpty)
            {
                SetRowIcon(row, null);
                if (row.NameText != null)
                {
                    row.NameText.text = L10n.T("空词缀槽", "Empty Affix Slot");
                    row.NameText.color = BossRushUIColors.TextSecondary;
                }

                if (row.DescText != null)
                {
                    row.DescText.text = L10n.T("使用「随机词缀」为该槽注入一条词缀。", "Use Reroll Affixes to fill this slot.");
                    row.DescText.color = BossRushUIColors.TextSecondary;
                }

                SetLockButtonState(row, false, false);
                return;
            }

            AffixDefinition definition = AffixDefinitions.Find(view.AffixId);
            if (definition == null)
            {
                // 未知 id fail-open：KV 原样保留，只是 UI 显示为未知
                SetRowIcon(row, null);
                if (row.NameText != null)
                {
                    row.NameText.text = L10n.T("未知词缀", "Unknown Affix");
                    row.NameText.color = BossRushUIColors.TextSecondary;
                }

                if (row.DescText != null)
                {
                    row.DescText.text = L10n.T("该词缀来自其它版本，暂不生效。", "This affix comes from another version and is inactive.");
                    row.DescText.color = BossRushUIColors.TextSecondary;
                }

                SetLockButtonState(row, false, view.Locked);
                return;
            }

            SetRowIcon(row, TryLoadAffixIconSprite(definition));

            if (row.NameText != null)
            {
                row.NameText.text = AffixDefinitions.GetDisplayName(view.AffixId);
                row.NameText.color = GetRarityColor(definition.Rarity);
            }

            if (row.DescText != null)
            {
                row.DescText.text = AffixDefinitions.GetDisplayDescription(view.AffixId, view.Tier);
                row.DescText.color = BossRushUIColors.TextSecondary;
            }

            SetLockButtonState(row, true, view.Locked);
        }

        private static void SetRowIcon(AffixRowWidgets row, Sprite sprite)
        {
            if (row.Icon != null)
            {
                row.Icon.sprite = sprite;
                row.Icon.enabled = sprite != null;
            }

            if (row.IconFallbackText != null)
            {
                row.IconFallbackText.gameObject.SetActive(sprite == null);
            }
        }

        /// <summary>
        /// 锁定按钮三态：可锁定=白字，已锁定=金字，不可操作=按钮隐藏。
        /// 悬停变蓝由 ApplyButtonColors 的 highlightedColor 提供，与
        /// PropertyEntryInteractable 的白/蓝/金语义一致。
        /// </summary>
        private static void SetLockButtonState(AffixRowWidgets row, bool interactable, bool locked)
        {
            if (row.LockButton == null)
            {
                return;
            }

            row.LockButton.gameObject.SetActive(interactable);
            row.LockButton.interactable = interactable;

            if (row.LockButtonText == null)
            {
                return;
            }

            if (locked)
            {
                row.LockButtonText.text = L10n.T("已锁定", "Locked");
                row.LockButtonText.color = AffixLockLockedColor;
            }
            else
            {
                row.LockButtonText.text = string.Format(
                    L10n.T("锁定 x{0}", "Lock x{0}"),
                    AffixForgeSystem.GetLockStoneCost());
                row.LockButtonText.color = AffixLockNormalColor;
            }
        }

        private static Color GetRarityColor(AffixRarity rarity)
        {
            if (rarity == AffixRarity.Rare)
            {
                return BossRushUIColors.RarityRare;
            }

            if (rarity == AffixRarity.Curse)
            {
                return BossRushUIColors.RarityEpic;
            }

            return BossRushUIColors.RarityUncommon;
        }

        private static Sprite TryLoadAffixIconSprite(AffixDefinition definition)
        {
            try
            {
                string relativePath = AffixDefinitions.GetIconRelativePath(definition);
                if (string.IsNullOrEmpty(relativePath))
                {
                    return null;
                }

                return ItemFactory.GetSpriteFromFile(relativePath);
            }
            catch
            {
                return null;
            }
        }

        private static Sprite TryLoadAffixForgeStoneSprite()
        {
            try
            {
                return ItemFactory.GetSpriteFromFile("Assets/Items/" + AffixForgeStoneConfig.ICON_NAME + ".png");
            }
            catch
            {
                return null;
            }
        }

        // ============================================================================
        // 熔石计数 / 费用文本 / 按钮
        // ============================================================================

        private static void UpdateAffixStoneCount()
        {
            if (affixStoneCountText == null)
            {
                return;
            }

            try
            {
                int owned = AffixForgeSystem.GetOwnedStoneCount();
                affixStoneCountText.text = string.Format(L10n.T("词缀熔石 x{0}", "Affix Forge Stone x{0}"), owned);
                affixStoneCountText.color = owned > 0 ? BossRushUIColors.TextPrimary : BossRushUIColors.Danger;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeUI] [WARNING] 更新熔石计数失败: " + e.Message);
            }
        }

        /// <summary>复用重铸的 probabilityText，改成"费用 / 熔石 / 提示"三行。</summary>
        private static void UpdateAffixProbabilityText()
        {
            if (probabilityText == null)
            {
                return;
            }

            if (selectedItem == null)
            {
                probabilityText.text = L10n.T("请选择要锻造的装备", "Select equipment to forge");
                probabilityText.color = Color.gray;
                return;
            }

            if (!AffixForgeSystem.CanAffixForge(selectedItem))
            {
                probabilityText.text = L10n.T("该装备无法附加词缀", "This equipment cannot carry affixes");
                probabilityText.color = new Color(1f, 0.5f, 0.5f);
                return;
            }

            int moneyCost = AffixForgeSystem.GetMoneyCost(selectedItem);
            int stoneCost = AffixForgeSystem.GetStoneCost(selectedItem);
            int ownedStones = AffixForgeSystem.GetOwnedStoneCount();
            long playerMoney = GetPlayerMoney();

            string moneyColor = playerMoney >= moneyCost ? "#FFFF4D" : "#FF4D4D";
            string stoneColor = ownedStones >= stoneCost ? "#4DFF4D" : "#FF4D4D";

            probabilityText.text = string.Format(
                "<color={0}>{1}: {2}</color>\n" +
                "<color={3}>{4}: {5} / {6}</color>\n" +
                "{7}",
                moneyColor,
                L10n.T("费用", "Cost"),
                moneyCost,
                stoneColor,
                L10n.T("词缀熔石", "Affix Forge Stone"),
                stoneCost,
                ownedStones,
                L10n.T("锁定的词缀槽不会被重新随机。", "Locked affix slots are not rerolled."));
            probabilityText.color = Color.white;
        }

        private static void ApplyAffixButtonText()
        {
            if (reforgeButton == null)
            {
                return;
            }

            string label = L10n.T("随机词缀", "Reroll Affixes");
            TextMeshProUGUI[] texts = reforgeButton.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null)
                {
                    texts[i].text = label;
                }
            }
        }

        private static void UpdateAffixButtonInteractable()
        {
            if (reforgeButton == null)
            {
                return;
            }

            reforgeButton.interactable = CanAffordAffixRoll();
        }

        private static bool CanAffordAffixRoll()
        {
            try
            {
                if (selectedItem == null || !AffixForgeSystem.CanAffixForge(selectedItem))
                {
                    return false;
                }

                if (AffixForgeSystem.GetOwnedStoneCount() < AffixForgeSystem.GetStoneCost(selectedItem))
                {
                    return false;
                }

                if (GetPlayerMoney() < AffixForgeSystem.GetMoneyCost(selectedItem))
                {
                    return false;
                }

                return HasUnlockedAffixSlot(selectedItem);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasUnlockedAffixSlot(Item item)
        {
            int capacity = AffixForgeSystem.GetSlotCount(item);
            for (int slotIndex = 1; slotIndex <= capacity; slotIndex++)
            {
                AffixSlotView view = default(AffixSlotView);
                if (!AffixItemData.TryReadSlot(item, slotIndex, out view))
                {
                    return true;   // 尚未初始化的槽视为可锻造
                }

                if (!view.Locked)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ShowAffixResultMessage(AffixForgeResult result)
        {
            if (probabilityText == null)
            {
                return;
            }

            if (result == null)
            {
                UpdateAffixProbabilityText();
                return;
            }

            if (!result.Success)
            {
                string message = string.IsNullOrEmpty(result.ErrorMessage)
                    ? L10n.T("锻造失败", "Forge failed")
                    : result.ErrorMessage;
                probabilityText.text = "<color=#FF4D4D>" + message + "</color>";
                probabilityText.color = Color.white;
                return;
            }

            UpdateAffixProbabilityText();
        }

        // ============================================================================
        // 隐藏重铸专属控件
        // ============================================================================

        /// <summary>
        /// 词缀模式下不需要的重铸控件全部隐藏。
        /// 金钱滑块的根节点记进 affixHiddenMoneySliderRoot，关闭时按记录还原。
        /// </summary>
        private static void HideReforgeOnlyWidgets()
        {
            try
            {
                if (tendencySliderRoot != null && tendencySliderRoot.activeSelf)
                {
                    tendencySliderRoot.SetActive(false);
                }

                if (moneySlider != null && moneySlider.transform.parent != null)
                {
                    GameObject moneyRoot = moneySlider.transform.parent.gameObject;
                    affixHiddenMoneySliderRoot = moneyRoot;
                    if (moneyRoot.activeSelf)
                    {
                        moneyRoot.SetActive(false);
                    }
                }

                if (coldQuenchFluidContainer != null && coldQuenchFluidContainer.activeSelf)
                {
                    coldQuenchFluidContainer.SetActive(false);
                }

                if (resultDisplayObj != null && resultDisplayObj.activeSelf)
                {
                    resultDisplayObj.SetActive(false);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeUI] [WARNING] 隐藏重铸控件失败: " + e.Message);
            }
        }

        private static void FixCannotForgeIndicator(bool forgeable)
        {
            try
            {
                System.Reflection.FieldInfo cannotField = CannotDecomposeField;
                if (cannotField == null || decomposeView == null)
                {
                    return;
                }

                GameObject indicator = cannotField.GetValue(decomposeView) as GameObject;
                if (indicator == null)
                {
                    return;
                }

                indicator.SetActive(selectedItem != null && !forgeable);

                TextMeshProUGUI[] texts = indicator.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] != null)
                    {
                        texts[i].text = L10n.T("该装备无法附加词缀", "This equipment cannot carry affixes");
                    }
                }
            }
            catch { }
        }

        private static void FixNoItemSelectedIndicator()
        {
            if (noItemSelectedIndicator == null)
            {
                return;
            }

            try
            {
                TextMeshProUGUI[] texts = noItemSelectedIndicator.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] != null)
                    {
                        texts[i].text = L10n.T("请选择要锻造的装备", "Select equipment to forge");
                    }
                }
            }
            catch { }
        }

        // ============================================================================
        // 锁定按钮回调（三个薄包装 → 一个命名处理器，全程不捕获 Item）
        // ============================================================================

        private static UnityEngine.Events.UnityAction GetLockButtonHandler(int slotIndex)
        {
            if (slotIndex == 1)
            {
                return OnAffixLockButton1Clicked;
            }

            if (slotIndex == 2)
            {
                return OnAffixLockButton2Clicked;
            }

            return OnAffixLockButton3Clicked;
        }

        private static void OnAffixLockButton1Clicked()
        {
            OnAffixLockButtonClicked(1);
        }

        private static void OnAffixLockButton2Clicked()
        {
            OnAffixLockButtonClicked(2);
        }

        private static void OnAffixLockButton3Clicked()
        {
            OnAffixLockButtonClicked(3);
        }

        private static void OnAffixLockButtonClicked(int slotIndex)
        {
            try
            {
                if (currentForgeMode != ForgeUIMode.AffixForge || selectedItem == null)
                {
                    return;
                }

                AffixSlotView view = default(AffixSlotView);
                if (!AffixItemData.TryReadSlot(selectedItem, slotIndex, out view) || view.IsEmpty)
                {
                    return;
                }

                AffixForgeResult result = view.Locked
                    ? AffixForgeSystem.UnlockSlot(selectedItem, slotIndex)
                    : AffixForgeSystem.LockSlot(selectedItem, slotIndex);

                RefreshAffixPanel();
                UpdateAffixStoneCount();
                ShowAffixResultMessage(result);
                UpdateAffixButtonInteractable();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeUI] [ERROR] 词缀锁定操作失败: " + e.Message);
            }
        }
    }
}
