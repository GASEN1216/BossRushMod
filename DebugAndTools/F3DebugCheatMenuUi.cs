// ============================================================================
// F3DebugCheatMenu partial - extracted from F3DebugCheatMenu.cs
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private void CreateF3DebugCheatMenuUI()
        {
            try
            {
                Font font = BossRushUI.GetLegacyChineseFont();

                f3DebugCheatMenuRoot = new GameObject("F3DebugCheatMenu");
                Canvas canvas = f3DebugCheatMenuRoot.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = BossRushUILayers.Modal;
                ZombieModeUIHelper.ConfigureCanvasScaler(f3DebugCheatMenuRoot.AddComponent<CanvasScaler>());
                f3DebugCheatMenuRoot.AddComponent<GraphicRaycaster>();

                // 版式（2026-10-25 优化）：深色遮罩 + 带描边的大面板；标题 / 摘要 / 左导航 / 内容 / 状态栏各自一块面，按钮走共享质感层。
                GameObject background = CreateUIObject("Background", f3DebugCheatMenuRoot.transform);
                F3DebugUiKit.Place(background, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                background.AddComponent<Image>().color = BossRushUIColors.BackdropStrong;
                background.AddComponent<Button>().onClick.AddListener(HideF3DebugCheatMenu);

                Image panelImage = F3DebugUiKit.CreateSurface(f3DebugCheatMenuRoot.transform, "Panel", BossRushUIColors.Surface, 16, BossRushUISkinPart.Panel);
                GameObject panel = panelImage.gameObject;
                RectTransform panelRect = panel.GetComponent<RectTransform>();
                panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(1600f, 940f);

                Text titleText = F3DebugUiKit.CreateFillText(panel.transform, font, 26, BossRushUIColors.TextPrimary, TextAnchor.MiddleLeft);
                F3DebugUiKit.Place(titleText.gameObject, new Vector2(0f, 1f), Vector2.one, new Vector2(28f, -66f), new Vector2(-170f, -14f));
                titleText.fontStyle = FontStyle.Bold;
                titleText.text = L10n.T("F3 调试总控", "F3 Debug Control") + "   <size=16><color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary) + ">"
                    + L10n.T("Esc 关闭 · 仅 Dev 构建", "Esc to close · Dev build only") + "</color></size>";
                Button closeButton = F3DebugUiKit.CreateButton(panel.transform, font, L10n.T("关闭", "Close"), BossRushUIColors.Danger, HideF3DebugCheatMenu, 120f, 40f, 18);
                F3DebugUiKit.Place(closeButton.gameObject, Vector2.one, Vector2.one, new Vector2(-148f, -60f), new Vector2(-24f, -20f));

                Image summaryImage = F3DebugUiKit.CreateSurface(panel.transform, "Summary", BossRushUIColors.Header, 10, BossRushUISkinPart.Card);
                F3DebugUiKit.Place(summaryImage.gameObject, new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -172f), new Vector2(-24f, -76f));
                f3DebugCheatSummaryText = F3DebugUiKit.CreateFillText(summaryImage.transform, font, 17, BossRushUIColors.TextSecondary, TextAnchor.UpperLeft);
                f3DebugCheatSummaryText.horizontalOverflow = HorizontalWrapMode.Wrap;
                f3DebugCheatSummaryText.verticalOverflow = VerticalWrapMode.Overflow;
                f3DebugCheatSummaryText.text = string.Empty;

                Image navImage = F3DebugUiKit.CreateSurface(panel.transform, "NavPanel", BossRushUIColors.Header, 10, BossRushUISkinPart.Card);
                GameObject navPanel = navImage.gameObject;
                F3DebugUiKit.Place(navPanel, Vector2.zero, new Vector2(0f, 1f), new Vector2(24f, 72f), new Vector2(240f, -186f));
                VerticalLayoutGroup navLayout = navPanel.AddComponent<VerticalLayoutGroup>();
                navLayout.padding = new RectOffset(10, 10, 12, 12);
                navLayout.spacing = 8f;
                navLayout.childControlHeight = false;
                navLayout.childControlWidth = true;
                navLayout.childForceExpandHeight = false;
                navLayout.childForceExpandWidth = true;

                CreateF3DebugCheatNavButton(navPanel.transform, font, F3DebugCheatPage.Teleport, L10n.T("传送", "Teleport"));
                CreateF3DebugCheatNavButton(navPanel.transform, font, F3DebugCheatPage.PlayerStats, L10n.T("玩家属性", "Player Stats"));
                CreateF3DebugCheatNavButton(navPanel.transform, font, F3DebugCheatPage.Resources, L10n.T("资源与物品", "Resources"));
                CreateF3DebugCheatNavButton(navPanel.transform, font, F3DebugCheatPage.Battle, L10n.T("战斗流程", "Battle"));
                CreateF3DebugCheatNavButton(navPanel.transform, font, F3DebugCheatPage.NpcStory, L10n.T("NPC/剧情", "NPC/Story"));
                CreateF3DebugCheatNavButton(navPanel.transform, font, F3DebugCheatPage.SceneDebug, L10n.T("场景调试", "Scene Debug"));
                CreateF3DebugCheatNavButton(navPanel.transform, font, F3DebugCheatPage.Validation, L10n.T("验收测试", "Validation"));
#if BOSSRUSH_DEV
                CreateF3DebugCheatNavButton(navPanel.transform, font, F3DebugCheatPage.Recording, L10n.T("录制视频", "Recording"));
#endif

                GameObject contentPanel = CreateUIObject("ContentPanel", panel.transform);
                F3DebugUiKit.Place(contentPanel, Vector2.zero, Vector2.one, new Vector2(256f, 72f), new Vector2(-24f, -186f));

                GameObject scrollView = CreateUIObject("ScrollView", contentPanel.transform);
                F3DebugUiKit.Place(scrollView, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                ScrollRect scroll = scrollView.AddComponent<ScrollRect>();
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 40f;

                GameObject viewport = CreateUIObject("Viewport", scrollView.transform);
                RectTransform viewportRect = F3DebugUiKit.Place(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                // Mask 依赖 Image 写 Stencil：默认 UI Shader 开 UNITY_UI_ALPHACLIP，a < 0.001 会 discard、子节点全被裁掉（内容区空白）。
                // 所以 alpha 必须 > 0，再用 showMaskGraphic=false 把颜色本身藏起来。
                viewport.AddComponent<Image>().color = Color.white;
                viewport.AddComponent<Mask>().showMaskGraphic = false;

                GameObject content = CreateUIObject("Content", viewport.transform);
                RectTransform contentRect = content.GetComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0.5f, 1f);
                contentRect.anchoredPosition = Vector2.zero;
                contentRect.sizeDelta = Vector2.zero;
                VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
                contentLayout.padding = new RectOffset(2, 8, 0, 8);
                contentLayout.spacing = 14f;
                contentLayout.childControlHeight = false;
                contentLayout.childControlWidth = true;
                contentLayout.childForceExpandHeight = false;
                contentLayout.childForceExpandWidth = true;
                content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.viewport = viewportRect;
                scroll.content = contentRect;
                f3DebugCheatContentRoot = content.transform;

                Image statusImage = F3DebugUiKit.CreateSurface(panel.transform, "StatusBar", BossRushUIColors.Header, 8, BossRushUISkinPart.Card);
                F3DebugUiKit.Place(statusImage.gameObject, Vector2.zero, new Vector2(1f, 0f), new Vector2(24f, 18f), new Vector2(-24f, 58f));
                f3DebugCheatStatusText = F3DebugUiKit.CreateFillText(statusImage.transform, font, 17, BossRushUIColors.TextPrimary, TextAnchor.MiddleLeft);
                f3DebugCheatStatusText.text = L10n.T("准备就绪", "Ready");

                DontDestroyOnLoad(f3DebugCheatMenuRoot);
                f3DebugCheatMenuRoot.SetActive(false);
            }
            catch
            {
                DestroyF3DebugCheatMenuUI();
                throw;
            }
        }

        private void DestroyF3DebugCheatMenuUI()
        {
            try
            {
                EnableF3DebugCheatInput();
            }
            catch { }

            f3DebugCheatMenuVisible = false;

            try
            {
                RestoreF3DebugCheatPresentationState();
            }
            catch { }

            if (f3DebugCheatMenuRoot != null)
            {
                Destroy(f3DebugCheatMenuRoot);
            }

            f3DebugCheatMenuRoot = null;
            f3DebugCheatInputDisabled = false;
            f3DebugCheatContentRoot = null;
            f3DebugCheatSummaryText = null;
            f3DebugCheatStatusText = null;
            f3DebugCheatNavButtonImages.Clear();
            f3ItemIdInputField = null;
            f3ItemCountInputField = null;
            f3MoneyInputField = null;
            f3MaxHealthMultiplierInputField = null;
            f3GunDamageMultiplierInputField = null;
            f3MeleeDamageMultiplierInputField = null;
            f3HeadArmorInputField = null;
            f3BodyArmorInputField = null;
            f3GameplayValidationStatusText = null;
        }

        private void CreateF3DebugCheatNavButton(Transform parent, Font font, F3DebugCheatPage page, string text)
        {
            Button button = F3DebugUiKit.CreateButton(parent, font, text, BossRushUIColors.Header, delegate
            {
                f3DebugCheatCurrentPage = page;
                RefreshF3DebugCheatPage();
            }, 0f, 46f, 18);
            f3DebugCheatNavButtonImages[page] = button.GetComponent<Image>();
        }

        private void RefreshF3DebugCheatPage()
        {
            if (f3DebugCheatContentRoot == null)
            {
                return;
            }

            UpdateF3DebugCheatNavButtonColors();
            ClearChildren(f3DebugCheatContentRoot);

            f3ItemIdInputField = null;
            f3ItemCountInputField = null;
            f3MoneyInputField = null;
            f3MaxHealthMultiplierInputField = null;
            f3GunDamageMultiplierInputField = null;
            f3MeleeDamageMultiplierInputField = null;
            f3HeadArmorInputField = null;
            f3BodyArmorInputField = null;

            switch (f3DebugCheatCurrentPage)
            {
                case F3DebugCheatPage.Teleport:
                    BuildF3TeleportPage();
                    break;
                case F3DebugCheatPage.PlayerStats:
                    BuildF3PlayerStatsPage();
                    break;
                case F3DebugCheatPage.Resources:
                    BuildF3ResourcesPage();
                    break;
                case F3DebugCheatPage.Battle:
                    BuildF3BattlePage();
                    break;
                case F3DebugCheatPage.NpcStory:
                    BuildF3NpcStoryPage();
                    break;
                case F3DebugCheatPage.SceneDebug:
                    BuildF3SceneDebugPage();
                    break;
                case F3DebugCheatPage.Validation:
                    BuildF3GameplayValidationPage();
                    break;
                case F3DebugCheatPage.Recording:
#if BOSSRUSH_DEV
                    PromoRecordingControls.Build(f3DebugCheatContentRoot, this, HideF3DebugCheatMenu, SetF3DebugCheatStatus);
#endif
                    break;
            }

            RefreshF3DebugCheatSummary();
        }

        private void BuildF3TeleportPage()
        {
            Font font = BossRushUI.GetLegacyChineseFont();
            GameObject section = CreateF3Section(L10n.T("传送与位置", "Teleport and Position"), L10n.T("回基地、回出生点、跳到 BossRush 起始点或当前场景默认点，并可切到现有 NPC 传送面板。", "Return home, return to spawn, jump to the BossRush start point or scene default point, or switch to the NPC teleport UI."), font);

            GameObject row1 = CreateF3Row(section.transform);
            CreateActionButton(row1.transform, font, L10n.T("回基地", "Go Home"), new Color(0.22f, 0.42f, 0.28f, 1f), delegate { TeleportPlayerHomeToBaseScene(); });
            CreateActionButton(row1.transform, font, L10n.T("返回当前局出生点", "Return to Spawn"), new Color(0.20f, 0.32f, 0.42f, 1f), delegate
            {
                ReturnToBossRushStart();
                SetF3DebugCheatStatus(L10n.T("已尝试返回当前局出生点", "Tried returning to the current spawn point"), false);
            });
            CreateActionButton(row1.transform, font, L10n.T("传送到 BossRush 起始点", "To BossRush Start"), new Color(0.25f, 0.34f, 0.40f, 1f), TeleportToBossRushStartPointFromF3);

            GameObject row2 = CreateF3Row(section.transform);
            CreateActionButton(row2.transform, font, L10n.T("传送到当前场景默认点", "To Scene Default"), new Color(0.25f, 0.34f, 0.40f, 1f), TeleportToCurrentSceneDefaultPoint);
            CreateActionButton(row2.transform, font, L10n.T("打开 NPC 传送面板", "Open NPC Teleport"), new Color(0.30f, 0.25f, 0.42f, 1f), delegate
            {
                HideF3DebugCheatMenu();
                ShowNPCTeleportUI();
            });
            CreateActionButton(row2.transform, font, L10n.T("刷新位置摘要", "Refresh Location"), new Color(0.34f, 0.28f, 0.20f, 1f), delegate
            {
                RefreshF3DebugCheatSummary();
                SetF3DebugCheatStatus(L10n.T("已刷新场景与位置摘要", "Location summary refreshed"), false);
            });

        }

        private void BuildF3PlayerStatsPage()
        {
            Font font = BossRushUI.GetLegacyChineseFont();
            GameObject section = CreateF3Section(L10n.T("玩家属性调参", "Player Stat Tuning"), BuildCurrentPlayerStatsReadout(), font);

            GameObject hpRow = CreateF3LabeledInputRow(section.transform, font, L10n.T("最大生命倍率", "Max Health Multiplier"), out f3MaxHealthMultiplierInputField, f3DebugCheatPlayerState.maxHealthMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            CreatePresetButton(hpRow.transform, font, "0.5x", delegate { SetInputFieldText(f3MaxHealthMultiplierInputField, "0.5"); });
            CreatePresetButton(hpRow.transform, font, "1x", delegate { SetInputFieldText(f3MaxHealthMultiplierInputField, "1"); });
            CreatePresetButton(hpRow.transform, font, "2x", delegate { SetInputFieldText(f3MaxHealthMultiplierInputField, "2"); });
            CreatePresetButton(hpRow.transform, font, "5x", delegate { SetInputFieldText(f3MaxHealthMultiplierInputField, "5"); });
            CreatePresetButton(hpRow.transform, font, "10x", delegate { SetInputFieldText(f3MaxHealthMultiplierInputField, "10"); });

            GameObject gunRow = CreateF3LabeledInputRow(section.transform, font, L10n.T("枪械伤害倍率", "Gun Damage Multiplier"), out f3GunDamageMultiplierInputField, f3DebugCheatPlayerState.gunDamageMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            CreatePresetButton(gunRow.transform, font, "0x", delegate { SetInputFieldText(f3GunDamageMultiplierInputField, "0"); });
            CreatePresetButton(gunRow.transform, font, "1x", delegate { SetInputFieldText(f3GunDamageMultiplierInputField, "1"); });
            CreatePresetButton(gunRow.transform, font, "2x", delegate { SetInputFieldText(f3GunDamageMultiplierInputField, "2"); });
            CreatePresetButton(gunRow.transform, font, "5x", delegate { SetInputFieldText(f3GunDamageMultiplierInputField, "5"); });
            CreatePresetButton(gunRow.transform, font, "10x", delegate { SetInputFieldText(f3GunDamageMultiplierInputField, "10"); });

            GameObject meleeRow = CreateF3LabeledInputRow(section.transform, font, L10n.T("近战伤害倍率", "Melee Damage Multiplier"), out f3MeleeDamageMultiplierInputField, f3DebugCheatPlayerState.meleeDamageMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            CreatePresetButton(meleeRow.transform, font, "0x", delegate { SetInputFieldText(f3MeleeDamageMultiplierInputField, "0"); });
            CreatePresetButton(meleeRow.transform, font, "1x", delegate { SetInputFieldText(f3MeleeDamageMultiplierInputField, "1"); });
            CreatePresetButton(meleeRow.transform, font, "2x", delegate { SetInputFieldText(f3MeleeDamageMultiplierInputField, "2"); });
            CreatePresetButton(meleeRow.transform, font, "5x", delegate { SetInputFieldText(f3MeleeDamageMultiplierInputField, "5"); });
            CreatePresetButton(meleeRow.transform, font, "10x", delegate { SetInputFieldText(f3MeleeDamageMultiplierInputField, "10"); });

            string headArmorText = f3DebugCheatPlayerState.headArmorOverride.HasValue
                ? f3DebugCheatPlayerState.headArmorOverride.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : string.Empty;
            GameObject headRow = CreateF3LabeledInputRow(section.transform, font, L10n.T("头部护甲覆盖", "Head Armor Override"), out f3HeadArmorInputField, headArmorText);
            CreatePresetButton(headRow.transform, font, "0", delegate { SetInputFieldText(f3HeadArmorInputField, "0"); });
            CreatePresetButton(headRow.transform, font, "3", delegate { SetInputFieldText(f3HeadArmorInputField, "3"); });
            CreatePresetButton(headRow.transform, font, "7", delegate { SetInputFieldText(f3HeadArmorInputField, "7"); });
            CreatePresetButton(headRow.transform, font, "10", delegate { SetInputFieldText(f3HeadArmorInputField, "10"); });
            CreatePresetButton(headRow.transform, font, L10n.T("清空", "Clear"), delegate { SetInputFieldText(f3HeadArmorInputField, string.Empty); });

            string bodyArmorText = f3DebugCheatPlayerState.bodyArmorOverride.HasValue
                ? f3DebugCheatPlayerState.bodyArmorOverride.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : string.Empty;
            GameObject bodyRow = CreateF3LabeledInputRow(section.transform, font, L10n.T("身体护甲覆盖", "Body Armor Override"), out f3BodyArmorInputField, bodyArmorText);
            CreatePresetButton(bodyRow.transform, font, "0", delegate { SetInputFieldText(f3BodyArmorInputField, "0"); });
            CreatePresetButton(bodyRow.transform, font, "3", delegate { SetInputFieldText(f3BodyArmorInputField, "3"); });
            CreatePresetButton(bodyRow.transform, font, "7", delegate { SetInputFieldText(f3BodyArmorInputField, "7"); });
            CreatePresetButton(bodyRow.transform, font, "10", delegate { SetInputFieldText(f3BodyArmorInputField, "10"); });
            CreatePresetButton(bodyRow.transform, font, L10n.T("清空", "Clear"), delegate { SetInputFieldText(f3BodyArmorInputField, string.Empty); });

            GameObject actionRow = CreateF3Row(section.transform);
            CreateActionButton(actionRow.transform, font, L10n.T("应用全部参数", "Apply All"), new Color(0.22f, 0.44f, 0.28f, 1f), ApplyAllPlayerCheatInputs);
            CreateActionButton(actionRow.transform, font, L10n.T("恢复默认", "Reset Defaults"), new Color(0.44f, 0.22f, 0.22f, 1f), ResetPlayerCheatConfigToDefaults);
            CreateActionButton(actionRow.transform, font, L10n.T("立即满血", "Heal Full"), new Color(0.24f, 0.34f, 0.44f, 1f), HealPlayerToFull);
            CreateActionButton(actionRow.transform, font, L10n.T("刷新当前值", "Refresh Stats"), new Color(0.34f, 0.30f, 0.20f, 1f), delegate { RefreshF3DebugCheatPage(); });
        }

        private void BuildF3ResourcesPage()
        {
            Font font = BossRushUI.GetLegacyChineseFont();
            GameObject section = CreateF3Section(L10n.T("资源与物品", "Resources and Items"), L10n.T("快速发物品、加钱、清冷却、打开背包检查器。", "Spawn items, add money, clear cooldowns, and open the inventory inspector."), font);

            GameObject itemRow = CreateF3Row(section.transform);
            CreateLabelWithWidth(itemRow.transform, font, L10n.T("物品 ID", "Item ID"), 18, 150f);
            f3ItemIdInputField = CreateInputField(itemRow.transform, font, string.Empty, 150f);
            CreateLabelWithWidth(itemRow.transform, font, L10n.T("数量", "Count"), 18, 70f);
            f3ItemCountInputField = CreateInputField(itemRow.transform, font, "1", 90f);
            CreateActionButton(itemRow.transform, font, L10n.T("发放物品", "Spawn Item"), new Color(0.22f, 0.42f, 0.28f, 1f), SpawnItemFromF3Inputs);

            GameObject quickItemRow = CreateF3Row(section.transform);
            CreateActionButton(quickItemRow.transform, font, L10n.T("船票 x1", "Ticket x1"), new Color(0.20f, 0.32f, 0.42f, 1f), delegate
            {
                SpawnQuickTestItem(bossRushTicketTypeId > 0 ? bossRushTicketTypeId : BossRushItemIds.BossRushTicket, 1, L10n.T("已发放 BossRush 船票", "Granted BossRush Ticket"));
            });
            CreateActionButton(quickItemRow.transform, font, L10n.T("戒指 x1", "Ring x1"), new Color(0.20f, 0.32f, 0.42f, 1f), delegate
            {
                SpawnQuickTestItem(DiamondRingConfig.TYPE_ID, 1, L10n.T("已发放钻石戒指", "Granted Diamond Ring"));
            });
            CreateActionButton(quickItemRow.transform, font, L10n.T("安神滴剂 x1", "Calming x1"), new Color(0.20f, 0.32f, 0.42f, 1f), delegate
            {
                SpawnQuickTestItem(CalmingDropsConfig.TYPE_ID, 1, L10n.T("已发放安神滴剂", "Granted Calming Drops"));
            });
            CreateActionButton(quickItemRow.transform, font, L10n.T("平安符 x1", "Charm x1"), new Color(0.20f, 0.32f, 0.42f, 1f), delegate
            {
                SpawnQuickTestItem(PeaceCharmConfig.TYPE_ID, 1, L10n.T("已发放平安护身符", "Granted Peace Charm"));
            });
            CreateActionButton(quickItemRow.transform, font, L10n.T("快递牌 x1", "Courier x1"), new Color(0.20f, 0.32f, 0.42f, 1f), delegate
            {
                SpawnQuickTestItem(AwenCourierTokenConfig.TYPE_ID, 1, L10n.T("已发放阿稳快递牌", "Granted Awen Courier Token"));
            });
            CreateActionButton(quickItemRow.transform, font, L10n.T("焚天龙铳套装", "Dragon Gun Kit"), new Color(0.24f, 0.34f, 0.44f, 1f), SpawnDragonKingBossGunDebugKitFromF3);

            GameObject moneyRow = CreateF3Row(section.transform);
            CreateLabelWithWidth(moneyRow.transform, font, L10n.T("加钱", "Add Money"), 18, 150f);
            f3MoneyInputField = CreateInputField(moneyRow.transform, font, "1000", 150f);
            CreateActionButton(moneyRow.transform, font, "+1000", new Color(0.24f, 0.34f, 0.44f, 1f), delegate { AddMoneyAndReport(1000L); });
            CreateActionButton(moneyRow.transform, font, "+10000", new Color(0.24f, 0.34f, 0.44f, 1f), delegate { AddMoneyAndReport(10000L); });
            CreateActionButton(moneyRow.transform, font, "+100000", new Color(0.24f, 0.34f, 0.44f, 1f), delegate { AddMoneyAndReport(100000L); });
            CreateActionButton(moneyRow.transform, font, L10n.T("按输入加钱", "Apply Money Input"), new Color(0.22f, 0.42f, 0.28f, 1f), AddMoneyFromInputField);

            GameObject cooldownRow = CreateF3Row(section.transform);
            CreateActionButton(cooldownRow.transform, font, L10n.T("清奖励冷却", "Clear Reward CD"), new Color(0.30f, 0.25f, 0.42f, 1f), ClearWishRewardCooldownOnly);
            CreateActionButton(cooldownRow.transform, font, L10n.T("清发送冷却", "Clear Send CD"), new Color(0.30f, 0.25f, 0.42f, 1f), ClearWishSendCooldownOnly);
            CreateActionButton(cooldownRow.transform, font, L10n.T("一键清空两者", "Clear Both"), new Color(0.22f, 0.42f, 0.28f, 1f), ClearAllWishDevCooldowns);

            GameObject inventoryRow = CreateF3Row(section.transform);
            CreateActionButton(inventoryRow.transform, font, L10n.T("打开背包检查器", "Open Inventory Inspector"), new Color(0.20f, 0.32f, 0.42f, 1f), OpenInventoryInspectorFromF3);
            CreateActionButton(inventoryRow.transform, font, L10n.T("输出背包详细日志", "Dump Inventory Log"), new Color(0.34f, 0.30f, 0.20f, 1f), delegate
            {
                LogInventoryDetailsForF3Debug();
                SetF3DebugCheatStatus(L10n.T("已输出背包详细日志", "Inventory details dumped to log"), false);
            });
        }

        private void BuildF3BattlePage()
        {
            Font font = BossRushUI.GetLegacyChineseFont();
            GameObject section = CreateF3Section(L10n.T("战斗流程", "Battle Flow"), L10n.T("快速过流程，清场、跳图、发船票、切放置模式。", "Fast-forward combat flow with kill, win, map, and placement tools."), font);

            GameObject row1 = CreateF3Row(section.transform);
            CreateActionButton(row1.transform, font, L10n.T("立即满血", "Heal Full"), new Color(0.24f, 0.34f, 0.44f, 1f), HealPlayerToFull);
            CreateActionButton(row1.transform, font, L10n.T("强制清场", "Kill All Enemies"), new Color(0.44f, 0.22f, 0.22f, 1f), ForceKillAllEnemiesFromF3);
            CreateActionButton(row1.transform, font, L10n.T("直接触发通关", "Trigger Victory"), new Color(0.22f, 0.42f, 0.28f, 1f), TriggerBossRushVictoryFromF3);

            GameObject row2 = CreateF3Row(section.transform);
            CreateActionButton(row2.transform, font, L10n.T("发船票并打开地图", "Grant Ticket + Map"), new Color(0.20f, 0.32f, 0.42f, 1f), GrantTicketAndOpenMapSelectionFromF3);
            CreateActionButton(row2.transform, font, L10n.T("切换放置模式", "Toggle Placement"), new Color(0.30f, 0.25f, 0.42f, 1f), TogglePlacementModeFromF3);

            GameObject row3 = CreateF3Row(section.transform);
            CreateActionButton(row3.transform, font, L10n.T("尸潮邀请函+地图", "Zombie Invite + Map"), new Color(0.20f, 0.32f, 0.42f, 1f), GrantZombieInvitationAndOpenMapSelectionFromF3);
            CreateActionButton(row3.transform, font, L10n.T("触发尸潮撤离", "Trigger Zombie Extract"), new Color(0.22f, 0.42f, 0.28f, 1f), TriggerZombieModeExtractionFromF3);
            CreateActionButton(row3.transform, font, L10n.T("重置尸潮模式", "Reset Zombie Mode"), new Color(0.44f, 0.22f, 0.22f, 1f), ResetZombieModeFromF3);
        }

        private void BuildF3NpcStoryPage()
        {
#if BOSSRUSH_DEV
            CampaignPetNestDebugControls.Build(f3DebugCheatContentRoot, this, HideF3DebugCheatMenu, SetF3DebugCheatStatus);
#endif
            Font font = BossRushUI.GetLegacyChineseFont();
            GameObject section = CreateF3Section(L10n.T("NPC 与剧情测试", "NPC and Story Tests"), L10n.T("整合已有的婚姻、成就、NPC 刷新和日限制测试能力。", "Pulls together the existing marriage, achievement, NPC refresh, and daily-limit test actions."), font);

            GameObject row1 = CreateF3Row(section.transform);
            CreateActionButton(row1.transform, font, L10n.T("清空成就", "Clear Achievements"), new Color(0.44f, 0.22f, 0.22f, 1f), ClearAchievementsFromF3);
            CreateActionButton(row1.transform, font, L10n.T("强制刷新哥布林", "Respawn Goblin"), new Color(0.22f, 0.42f, 0.28f, 1f), delegate
            {
                SpawnGoblinNPC(null, false, true);
                AppendMarriageTestLog("请求强制刷新哥布林");
                SetF3DebugCheatStatus(L10n.T("已请求强制刷新哥布林", "Requested Goblin respawn"), false);
            });
            CreateActionButton(row1.transform, font, L10n.T("强制刷新护士", "Respawn Nurse"), new Color(0.22f, 0.42f, 0.28f, 1f), delegate
            {
                SpawnNurseNPC(null, false, true);
                AppendMarriageTestLog("请求强制刷新护士");
                SetF3DebugCheatStatus(L10n.T("已请求强制刷新护士", "Requested Nurse respawn"), false);
            });

            GameObject row2 = CreateF3Row(section.transform);
            CreateActionButton(row2.transform, font, L10n.T("重置礼物限制", "Reset Gift Limit"), new Color(0.20f, 0.32f, 0.42f, 1f), delegate
            {
                AffinityManager.SetLastGiftDay(GoblinAffinityConfig.NPC_ID, -1);
                AffinityManager.SetLastGiftDay(NurseAffinityConfig.NPC_ID, -1);
                AppendMarriageTestLog("已重置哥布林/护士今日赠礼限制");
                SetF3DebugCheatStatus(L10n.T("已重置礼物限制", "Gift limit reset"), false);
            });
            CreateActionButton(row2.transform, font, L10n.T("重置聊天限制", "Reset Chat Limit"), new Color(0.20f, 0.32f, 0.42f, 1f), delegate
            {
                AffinityManager.SetLastChatDay(GoblinAffinityConfig.NPC_ID, -1);
                AffinityManager.SetLastChatDay(NurseAffinityConfig.NPC_ID, -1);
                AppendMarriageTestLog("已重置哥布林/护士今日聊天限制");
                SetF3DebugCheatStatus(L10n.T("已重置聊天限制", "Chat limit reset"), false);
            });
            CreateActionButton(row2.transform, font, L10n.T("重置剧情标记", "Reset Story Flags"), new Color(0.30f, 0.25f, 0.42f, 1f), delegate
            {
                AffinityManager.ResetStoryTriggers(GoblinAffinityConfig.NPC_ID);
                AffinityManager.ResetStoryTriggers(NurseAffinityConfig.NPC_ID);
                AffinityManager.FlushSave();
                AppendMarriageTestLog("已重置哥布林/护士的 5/10 级故事对话标记");
                SetF3DebugCheatStatus(L10n.T("已重置剧情标记", "Story flags reset"), false);
            });

            GameObject row3 = CreateF3Row(section.transform);
            CreateActionButton(row3.transform, font, L10n.T("哥布林=10级", "Goblin Lv10"), new Color(0.24f, 0.34f, 0.44f, 1f), delegate
            {
                AffinityManager.SetPoints(GoblinAffinityConfig.NPC_ID, AffinityManager.UNIFIED_MAX_POINTS);
                AppendMarriageTestLog("已设置哥布林为 10 级");
                SetF3DebugCheatStatus(L10n.T("已设置哥布林为 10 级", "Goblin set to Lv10"), false);
            });
            CreateActionButton(row3.transform, font, L10n.T("哥布林=1级", "Goblin Lv1"), new Color(0.24f, 0.34f, 0.44f, 1f), delegate
            {
                AffinityManager.SetPoints(GoblinAffinityConfig.NPC_ID, 0);
                AppendMarriageTestLog("已设置哥布林为 1 级");
                SetF3DebugCheatStatus(L10n.T("已设置哥布林为 1 级", "Goblin set to Lv1"), false);
            });
            CreateActionButton(row3.transform, font, L10n.T("护士=10级", "Nurse Lv10"), new Color(0.24f, 0.34f, 0.44f, 1f), delegate
            {
                AffinityManager.SetPoints(NurseAffinityConfig.NPC_ID, AffinityManager.UNIFIED_MAX_POINTS);
                AppendMarriageTestLog("已设置护士为 10 级");
                SetF3DebugCheatStatus(L10n.T("已设置护士为 10 级", "Nurse set to Lv10"), false);
            });
            CreateActionButton(row3.transform, font, L10n.T("护士=1级", "Nurse Lv1"), new Color(0.24f, 0.34f, 0.44f, 1f), delegate
            {
                AffinityManager.SetPoints(NurseAffinityConfig.NPC_ID, 0);
                AppendMarriageTestLog("已设置护士为 1 级");
                SetF3DebugCheatStatus(L10n.T("已设置护士为 1 级", "Nurse set to Lv1"), false);
            });

            GameObject row4 = CreateF3Row(section.transform);
            CreateActionButton(row4.transform, font, L10n.T("与哥布林结婚", "Marry Goblin"), new Color(0.22f, 0.42f, 0.28f, 1f), delegate
            {
                TriggerMarriageSequenceForNpc(GoblinAffinityConfig.NPC_ID);
                SetF3DebugCheatStatus(L10n.T("已触发哥布林结婚流程", "Triggered Goblin marriage flow"), false);
            });
            CreateActionButton(row4.transform, font, L10n.T("与护士结婚", "Marry Nurse"), new Color(0.22f, 0.42f, 0.28f, 1f), delegate
            {
                TriggerMarriageSequenceForNpc(NurseAffinityConfig.NPC_ID);
                SetF3DebugCheatStatus(L10n.T("已触发护士结婚流程", "Triggered Nurse marriage flow"), false);
            });
            CreateActionButton(row4.transform, font, L10n.T("与当前配偶离婚", "Divorce Current Spouse"), new Color(0.44f, 0.22f, 0.22f, 1f), delegate
            {
                TriggerDivorceForCurrentSpouse();
                SetF3DebugCheatStatus(L10n.T("已触发离婚流程", "Triggered divorce flow"), false);
            });
            CreateActionButton(row4.transform, font, L10n.T("当前配偶+1 花心事件", "Add Cheating Incident"), new Color(0.34f, 0.30f, 0.20f, 1f), delegate
            {
                string spouseId = AffinityManager.GetCurrentSpouseNpcId();
                if (string.IsNullOrEmpty(spouseId))
                {
                    SetF3DebugCheatStatus(L10n.T("当前没有配偶", "No current spouse"), true);
                    return;
                }

                AffinityManager.RecordCheatingIncidentForSpouse(spouseId);
                AppendMarriageTestLog("已记录花心事件，配偶=" + spouseId);
                SetF3DebugCheatStatus(L10n.T("已记录 1 次花心事件", "Recorded one cheating incident"), false);
            });

            BuildF3DuckNpcSection(font);
        }

        /// <summary>
        /// 捏脸 NPC 工具区：为「未来用原版捏脸造新 NPC」采集实机数据。
        /// 与现有哥布林/护士/快递员无关，不会改动它们。
        /// </summary>
        private void BuildF3DuckNpcSection(Font font)
        {
            GameObject section = CreateF3Section(
                L10n.T("捏脸 NPC 工具（新 NPC 路线验证）", "Duck NPC Tools (New NPC Route)"),
                L10n.T("清点官方捏脸家底、导出玩家捏脸 JSON、生成探针验证「无 preset 裸造角色」行为。报告落盘在 persistentDataPath/BossRushTestReports/。",
                    "Inventory the official custom-face assets, export the player's face as JSON, and spawn a probe to validate preset-less character creation. Reports land in persistentDataPath/BossRushTestReports/."),
                font);

            GameObject row1 = CreateF3Row(section.transform);
            CreateActionButton(row1.transform, font,
                L10n.T("输出捏脸家底报告", "Dump Face Inventory"),
                new Color(0.20f, 0.32f, 0.42f, 1f), DumpDuckNpcInventoryFromF3);
            CreateActionButton(row1.transform, font,
                L10n.T("导出玩家捏脸 JSON", "Export Player Face"),
                new Color(0.24f, 0.34f, 0.44f, 1f), ExportPlayerFaceFromF3);

            GameObject row2 = CreateF3Row(section.transform);
            CreateActionButton(row2.transform, font,
                L10n.T("探针：用玩家脸生成", "Probe: Player Face"),
                new Color(0.22f, 0.42f, 0.28f, 1f), SpawnDuckNpcProbeWithPlayerFaceFromF3);
            CreateActionButton(row2.transform, font,
                L10n.T("探针：用官方基线生成", "Probe: Baseline Face"),
                new Color(0.22f, 0.42f, 0.28f, 1f), SpawnDuckNpcProbeWithBaselineFaceFromF3);

            GameObject randomRow = CreateF3Row(section.transform);
            CreateActionButton(randomRow.transform, font,
                L10n.T("探针：随机夸张脸", "Probe: Random Wild Face"),
                new Color(0.42f, 0.28f, 0.46f, 1f), SpawnDuckNpcProbeWithRandomFaceFromF3);
            CreateActionButton(randomRow.transform, font,
                L10n.T("探针：随机脸+随机装备", "Probe: Wild Face + Gear"),
                new Color(0.46f, 0.30f, 0.20f, 1f), SpawnDuckNpcProbeFullRandomFromF3);
            CreateActionButton(randomRow.transform, font,
                L10n.T("探针：原地换一张脸", "Probe: Reroll Face"),
                new Color(0.30f, 0.38f, 0.46f, 1f), RerollDuckNpcProbeFaceFromF3);
            CreateActionButton(randomRow.transform, font,
                L10n.T("探针：重掷装备", "Probe: Reroll Gear"),
                new Color(0.30f, 0.38f, 0.46f, 1f), RerollDuckNpcProbeEquipmentFromF3);

            GameObject saveRow = CreateF3Row(section.transform);
            CreateActionButton(saveRow.transform, font,
                L10n.T("★ 保存为永久 NPC 数据", "★ Save As Permanent NPC"),
                new Color(0.52f, 0.36f, 0.16f, 1f), SaveDuckNpcProbeAsPermanentFromF3);

            BuildF3PermanentDuckNpcSection(font);
        }

        /// <summary>
        /// 永久捏脸 NPC（模式 B）测试区。与上面的探针区分开：
        /// 探针是"摇长相"的，这里是"验证永久 NPC 整条链路"的。
        /// </summary>
        private void BuildF3PermanentDuckNpcSection(Font font)
        {
            GameObject section = CreateF3Section(
                L10n.T("永久捏脸 NPC（模式 B）", "Permanent Duck NPC (Mode B)"),
                L10n.T("常驻 NPC：好感度 / 对话 / 送礼 / 婚姻，与羽织叮当同一套系统。"
                       + "蓝图 scenes 留空时不会自动生成，用下面的按钮当场召唤来测。",
                       "Persistent NPC with affinity/dialogue/gift/marriage. "
                       + "Spawn on demand here when the blueprint has no scene whitelist."),
                font);

            // 目标默认是 DuckNpcs.json 第一位（小满）；「切换目标」按表里顺序轮换，下面五个按钮都作用于当前目标。
            GameObject row1 = CreateF3Row(section.transform);
            CreateActionButton(row1.transform, font,
                L10n.T("永久 NPC：切换目标", "Permanent: Next Target"),
                new Color(0.20f, 0.32f, 0.42f, 1f), () => RunPermanentDuckNpcDebugFromF3(PermanentDuckNpcDebug.CycleTarget));
            CreateActionButton(row1.transform, font,
                L10n.T("永久 NPC：在此生成", "Permanent: Spawn Here"),
                new Color(0.22f, 0.42f, 0.28f, 1f), () => RunPermanentDuckNpcDebugFromF3(PermanentDuckNpcDebug.SpawnHere));
            CreateActionButton(row1.transform, font,
                L10n.T("永久 NPC：回收", "Permanent: Despawn"),
                new Color(0.44f, 0.22f, 0.22f, 1f), () => RunPermanentDuckNpcDebugFromF3(PermanentDuckNpcDebug.Despawn));
            CreateActionButton(row1.transform, font,
                L10n.T("永久 NPC：输出状态", "Permanent: Dump State"),
                new Color(0.30f, 0.25f, 0.42f, 1f), () => RunPermanentDuckNpcDebugFromF3(PermanentDuckNpcDebug.DumpState));

            GameObject row2 = CreateF3Row(section.transform);
            CreateActionButton(row2.transform, font,
                L10n.T("好感度拉满(含10级剧情标记)", "Max Affinity (+Story10)"),
                new Color(0.46f, 0.30f, 0.20f, 1f), () => RunPermanentDuckNpcDebugFromF3(PermanentDuckNpcDebug.MaxAffinity));
            CreateActionButton(row2.transform, font,
                L10n.T("好感度清零", "Reset Affinity"),
                new Color(0.34f, 0.30f, 0.20f, 1f), () => RunPermanentDuckNpcDebugFromF3(PermanentDuckNpcDebug.ResetAffinity));
            CreateActionButton(row2.transform, font,
                L10n.T("探针：回收", "Probe: Despawn"),
                new Color(0.44f, 0.22f, 0.22f, 1f), DespawnDuckNpcProbeFromF3);
            CreateActionButton(row2.transform, font,
                L10n.T("探针：输出状态", "Probe: Dump State"),
                new Color(0.30f, 0.25f, 0.42f, 1f), DumpDuckNpcProbeStateFromF3);
        }

        private void BuildF3SceneDebugPage()
        {
            ArenaPrototypeControls.Build(f3DebugCheatContentRoot, this, HideF3DebugCheatMenu, SetF3DebugCheatStatus);
            SkyIslandControls.Build(f3DebugCheatContentRoot, this, HideF3DebugCheatMenu, SetF3DebugCheatStatus);
            Font font = BossRushUI.GetLegacyChineseFont();
            GameObject section = CreateF3Section(L10n.T("场景调试", "Scene Diagnostics"), L10n.T("复用现有场景扫描和日志输出能力，用于定位地图、交互点和角色状态。", "Reuse the existing scene scanners and logs to inspect maps, interact points, and characters."), font);

            GameObject row1 = CreateF3Row(section.transform);
            CreateActionButton(row1.transform, font, L10n.T("输出附近建筑/对象", "Dump Nearby Objects"), new Color(0.20f, 0.32f, 0.42f, 1f), DumpNearbyObjectsFromF3);
            CreateActionButton(row1.transform, font, L10n.T("输出最近交互点", "Dump Nearest Interactable"), new Color(0.20f, 0.32f, 0.42f, 1f), DumpNearestInteractableFromF3);
            CreateActionButton(row1.transform, font, L10n.T("输出场景角色信息", "Dump Scene Characters"), new Color(0.20f, 0.32f, 0.42f, 1f), DumpSceneCharactersFromF3);
            CreateActionButton(row1.transform, font, L10n.T("刷新顶部摘要", "Refresh Summary"), new Color(0.34f, 0.30f, 0.20f, 1f), delegate
            {
                RefreshF3DebugCheatSummary();
                SetF3DebugCheatStatus(L10n.T("已刷新摘要", "Summary refreshed"), false);
            });

            // 遗种巢 PoC 闸门（实施计划 步骤 0）：召唤 / 回收 / 探针报告
            GameObject row2 = CreateF3Row(section.transform);
            CreateActionButton(row2.transform, font, L10n.T("遗种巢：召唤 PoC 幼体", "PetNest: Spawn PoC Cub"), new Color(0.22f, 0.42f, 0.28f, 1f), SpawnPetNestProbeCompanionFromF3);
            CreateActionButton(row2.transform, font, L10n.T("遗种巢：回收 PoC 幼体", "PetNest: Despawn PoC Cub"), new Color(0.44f, 0.22f, 0.22f, 1f), DespawnPetNestProbeCompanionFromF3);
            CreateActionButton(row2.transform, font, L10n.T("遗种巢：输出探针报告", "PetNest: Dump Probe Report"), new Color(0.20f, 0.32f, 0.42f, 1f), DumpPetNestProbeReportFromF3);

            // 日报：一个游戏日 = 24 现实分钟，冒烟不可能真等，必须能快进
            GameObject row3 = CreateF3Row(section.transform);
            CreateActionButton(row3.transform, font, L10n.T("日报：快进一天", "Daily: Skip One Day"), new Color(0.42f, 0.34f, 0.18f, 1f), AdvanceDailyReportOneDayFromF3);
            CreateActionButton(row3.transform, font, L10n.T("日报：打开报纸", "Daily: Open Paper"), new Color(0.20f, 0.32f, 0.42f, 1f), OpenDailyReportFromF3);
            CreateActionButton(row3.transform, font, L10n.T("日报：输出状态", "Daily: Dump State"), new Color(0.24f, 0.24f, 0.30f, 1f), DumpDailyReportStateFromF3);
        }

        // 以下构件已移到 F3DebugUiKit（2026-10-25），这里只留一行转发，调用点不变。
        private GameObject CreateF3Section(string title, string description, Font font) { return F3DebugUiKit.CreateSection(f3DebugCheatContentRoot, title, description, font); }
        private GameObject CreateF3Row(Transform parent) { return F3DebugUiKit.CreateRow(parent); }

        private GameObject CreateF3LabeledInputRow(Transform parent, Font font, string label, out InputField inputField, string text)
        {
            GameObject row = CreateF3Row(parent);
            CreateLabelWithWidth(row.transform, font, label, 18, 210f);
            inputField = CreateInputField(row.transform, font, text, 140f);
            return row;
        }

        private static GameObject CreateUIObject(string name, Transform parent) { return F3DebugUiKit.CreateUIObject(name, parent); }
        private Text CreateLabel(Transform parent, Font font, string text, int fontSize, Color color, FontStyle style) { return F3DebugUiKit.CreateLabel(parent, font, text, fontSize, color, style); }
        private Text CreateLabelWithWidth(Transform parent, Font font, string text, int fontSize, float width) { return F3DebugUiKit.CreateLabelWithWidth(parent, font, text, fontSize, width); }
        private void CreateCenteredText(Transform parent, Font font, string text, int fontSize, Color color) { F3DebugUiKit.CreateCenteredText(parent, font, text, fontSize, color); }
        private Text CreateFillText(Transform parent, Font font, int fontSize, Color color, TextAnchor alignment) { return F3DebugUiKit.CreateFillText(parent, font, fontSize, color, alignment); }
        private Button CreateActionButton(Transform parent, Font font, string text, Color color, UnityEngine.Events.UnityAction onClick) { return F3DebugUiKit.CreateActionButton(parent, font, text, color, onClick); }
        private Button CreatePresetButton(Transform parent, Font font, string text, UnityEngine.Events.UnityAction onClick) { return F3DebugUiKit.CreatePresetButton(parent, font, text, onClick); }
        private InputField CreateInputField(Transform parent, Font font, string text, float width) { return F3DebugUiKit.CreateInputField(parent, font, text, width); }
        private void ClearChildren(Transform parent) { F3DebugUiKit.ClearChildren(parent); }
    }
}
