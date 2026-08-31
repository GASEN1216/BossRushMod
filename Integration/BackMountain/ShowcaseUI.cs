// ============================================================================
// ShowcaseUI.cs - 战利品展示柜面板
// ============================================================================
// 全部走 Common/UI/BossRushUI.cs 共享库（AGENTS.md 4.14）。
// 面板结构：标题（含当前加成）→ 八格登记列表 → 手持/穿戴登记按钮 → 关闭。
//
// 【登记的是「手上或穿戴的那件」，而且不会收走它】
//   不做背包选择器：那需要一整套物品栅格 UI，而展示柜的交互本来就该很轻。
//   玩家可登记当前手持物，也可从装备槽登记第一件合格战利品；东西照样归自己（见 ShowcaseService
//   头注释里「为什么是登记簿而不是储物柜」）。因此也不需要「取回」按钮。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>展示柜面板。全静态：同时只允许存在一个。</summary>
    internal static class ShowcaseUI
    {
        private static GameObject _root;

        internal static bool IsOpen { get { return _root != null; } }

        #region 开关

        /// <summary>打开面板（幂等：已开时先关再开，保证内容最新）。</summary>
        internal static void Open()
        {
            try
            {
                Close();
                Build();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 打开展示柜失败: " + e.Message);
                Close();
            }
        }

        internal static void Close()
        {
            try
            {
                if (_root != null)
                {
                    UnityEngine.Object.Destroy(_root);
                    _root = null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 关闭展示柜面板失败: " + e.Message);
            }
        }

        internal static void ResetStaticCaches()
        {
            Close();
        }

        #endregion

        #region 构建

        private static void Build()
        {
            Canvas canvas = BossRushUI.CreateCanvasRoot(
                "BossRushShowcaseCanvas", BossRushUILayers.Panel, true);
            _root = canvas.gameObject;

            BossRushUI.CreateBackdrop(_root.transform);

            GameObject panel = ZombieModeUIHelper.CreateRect(
                "Panel", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(760f, 580f));
            Image panelBg = panel.AddComponent<Image>();
            panelBg.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(panelBg, 14);

            BuildHeader(panel.transform);
            BuildSlots(panel.transform);
            BuildFooter(panel.transform);

            BossRushUI.PlayOpenAnimation(panel);
        }

        private static void BuildHeader(Transform parent)
        {
            GameObject header = ZombieModeUIHelper.CreateRect(
                "Header", parent, new Vector2(0.5f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -34f), new Vector2(0f, 68f), new Vector2(0.5f, 0.5f));
            Image headerBg = header.AddComponent<Image>();
            headerBg.color = BossRushUIColors.Header;
            BossRushUI.ApplyPanelSkin(headerBg, 12);

            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "Title", header.transform, L10n.T("战利品展示柜", "Trophy Showcase"),
                28f, new Vector2(0f, 10f), new Vector2(560f, 38f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(title);

            float bonus = ShowcaseService.CalculateBonus();
            string bonusText = L10n.T(
                "当前陈列 " + ShowcaseService.DisplayedCount + "/" + BackMountainConfig.ShowcaseSlotCount
                + "　最大生命 +" + (bonus * 100f).ToString("0.#") + "%",
                "Displayed " + ShowcaseService.DisplayedCount + "/" + BackMountainConfig.ShowcaseSlotCount
                + "　Max Health +" + (bonus * 100f).ToString("0.#") + "%");

            TextMeshProUGUI subtitle = ZombieModeUIHelper.CreateText(
                "Subtitle", header.transform, bonusText, 16f,
                new Vector2(0f, -16f), new Vector2(620f, 26f),
                TextAlignmentOptions.Center, BossRushUIColors.Accent);
            BossRushUI.ApplyGameFont(subtitle);
        }

        private static void BuildSlots(Transform parent)
        {
            IList<int> displayed = ShowcaseService.GetDisplayed();

            const float rowHeight = 44f;
            const float spacing = 6f;
            float startY = 170f;

            for (int i = 0; i < BackMountainConfig.ShowcaseSlotCount; i++)
            {
                float y = startY - i * (rowHeight + spacing);
                bool filled = i < displayed.Count;
                int typeId = filled ? displayed[i] : 0;

                GameObject row = BossRushUI.CreateCard(
                    "Slot_" + i, parent, new Vector2(0f, y), new Vector2(680f, rowHeight),
                    BossRushUIColors.SurfaceRaised,
                    filled ? BossRushUIColors.RarityLegendary : BossRushUIColors.Disabled,
                    true);

                string label = filled
                    ? ResolveItemName(typeId)
                    : L10n.T("（空位）", "(empty)");

                TextMeshProUGUI text = ZombieModeUIHelper.CreateText(
                    "Name", row.transform, label, 16f,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(20f, 0f), new Vector2(440f, 26f),
                    TextAlignmentOptions.Left,
                    filled ? BossRushUIColors.TextPrimary : BossRushUIColors.TextSecondary);
                text.rectTransform.pivot = new Vector2(0f, 0.5f);
                BossRushUI.ApplyGameFont(text);

                if (!filled) continue;

                int quality = 0;
                try
                {
                    ItemMetaData meta = ItemAssetsCollection.GetMetaData(typeId);
                    if (meta.id > 0) quality = meta.quality;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 构建展示柜格子失败: " + e.Message);
                }

                TextMeshProUGUI qualityText = ZombieModeUIHelper.CreateText(
                    "Quality", row.transform, quality > 0 ? "Q" + quality : string.Empty, 15f,
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-20f, 0f), new Vector2(80f, 26f),
                    TextAlignmentOptions.Right, BossRushUIColors.RarityLegendary);
                qualityText.rectTransform.pivot = new Vector2(1f, 0.5f);
                BossRushUI.ApplyGameFont(qualityText);
            }
        }

        private static string ResolveItemName(int typeId)
        {
            try
            {
                ItemMetaData meta = ItemAssetsCollection.GetMetaData(typeId);
                if (meta.id > 0 && !string.IsNullOrEmpty(meta.DisplayName)) return meta.DisplayName;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 解析收藏物名称失败: " + e.Message);
            }
            return "#" + typeId;
        }

        private static void BuildFooter(Transform parent)
        {
            ZombieModeUIHelper.CreateButton(
                "Display", parent, L10n.T("登记手持战利品", "Record held trophy"),
                new Vector2(0.5f, 0f), new Vector2(-190f, 40f), new Vector2(190f, 42f),
                BossRushUIColors.Accent, 15f, new Vector2(180f, 34f),
                delegate { OnDisplayHeld(); }, true);

            ZombieModeUIHelper.CreateButton(
                "DisplayEquipped", parent, L10n.T("登记穿戴战利品", "Record equipped trophy"),
                new Vector2(0.5f, 0f), new Vector2(15f, 40f), new Vector2(190f, 42f),
                BossRushUIColors.Accent, 15f, new Vector2(180f, 34f),
                delegate { OnDisplayEquipped(); }, true);

            ZombieModeUIHelper.CreateButton(
                "Close", parent, L10n.T("关闭", "Close"),
                new Vector2(0.5f, 0f), new Vector2(210f, 40f), new Vector2(130f, 42f),
                BossRushUIColors.SurfaceRaised, 16f, new Vector2(120f, 34f),
                delegate { Close(); }, true);
        }

        #endregion

        #region 交互回调

        private static void OnDisplayHeld()
        {
            try
            {
                TryDisplayItem(ResolveHeldItem());
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 登记操作失败: " + e.Message);
            }
        }

        private static void OnDisplayEquipped()
        {
            try
            {
                Item equipped = ResolveEquippedTrophy();
                if (equipped == null)
                {
                    Duckov.UI.NotificationText.Push(
                        L10n.T("没有找到可登记的穿戴战利品", "No eligible equipped trophy found"));
                    return;
                }
                TryDisplayItem(equipped);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 穿戴登记失败: " + e.Message);
            }
        }

        private static void TryDisplayItem(Item item)
        {
            string reason;
            if (!ShowcaseService.CanDisplay(item, out reason))
            {
                ModBehaviour.Instance?.ShowMessage(reason);
                return;
            }

            int typeId = item.TypeID;
            if (!ShowcaseService.TryDisplay(typeId))
            {
                ModBehaviour.Instance?.ShowMessage(L10n.T("登记失败", "Failed to record"));
                return;
            }

            // 物品不收走：登记簿只记 TypeID，玩家继续用自己的战利品。
            ModBehaviour.Instance?.ShowMessage(
                L10n.T("已登记：", "Recorded: ") + ResolveItemName(typeId));
            Open();
        }

        /// <summary>
        /// 玩家当前手持的物品；拿不到返回 null。
        /// 口径与 AffixForge 一致（CurrentHoldItemAgent.Item），官方 getter 在角色
        /// 未就绪时可能抛，统一包住。
        /// </summary>
        private static Item ResolveHeldItem()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return null;
                DuckovItemAgent agent = main.CurrentHoldItemAgent;
                return agent != null ? agent.Item : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>按装备槽顺序找第一件符合登记条件的穿戴战利品。</summary>
        private static Item ResolveEquippedTrophy()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                Item characterItem = main != null ? main.CharacterItem : null;
                if (characterItem == null || characterItem.Slots == null) return null;

                string[] preferredSlots =
                {
                    "Armor", "Helmat", "Helmet", "FaceMask",
                    "PrimaryWeapon", "SecondaryWeapon", "MeleeWeapon"
                };
                for (int i = 0; i < preferredSlots.Length; i++)
                {
                    try
                    {
                        var slot = characterItem.Slots.GetSlot(preferredSlots[i]);
                        Item candidate = slot != null ? slot.Content : null;
                        string ignored;
                        if (ShowcaseService.CanDisplay(candidate, out ignored)) return candidate;
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog(BackMountainConfig.LogPrefix
                            + "[WARNING] 扫描穿戴战利品失败: " + e.Message);
                    }
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion
    }

    public partial class ModBehaviour
    {
        /// <summary>展示柜交互入口。由 ShowcaseInteractable 调用。</summary>
        public void OpenShowcaseUI()
        {
            try
            {
                if (!IsBackMountainConfiguredEnabled()) return;
                if (!BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Showcase)) return;
                ShowcaseUI.Open();
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "[WARNING] 打开展示柜失败: " + e.Message);
            }
        }
    }
}
