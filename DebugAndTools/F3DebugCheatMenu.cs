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
        private enum F3DebugCheatPage
        {
            Teleport,
            PlayerStats,
            Resources,
            Battle,
            NpcStory,
            SceneDebug
        }

        private sealed class F3DebugCheatPlayerState
        {
            public float maxHealthMultiplier = 1f;
            public float gunDamageMultiplier = 1f;
            public float meleeDamageMultiplier = 1f;
            public float? headArmorOverride;
            public float? bodyArmorOverride;

            public void Reset()
            {
                maxHealthMultiplier = 1f;
                gunDamageMultiplier = 1f;
                meleeDamageMultiplier = 1f;
                headArmorOverride = null;
                bodyArmorOverride = null;
            }
        }

        private sealed class F3DebugCheatRuntimeBindings
        {
            public Stat maxHealthStat;
            public Modifier maxHealthModifier;

            public Stat gunDamageStat;
            public Modifier gunDamageModifier;

            public Stat meleeDamageStat;
            public Modifier meleeDamageModifier;

            public Stat headArmorStat;
            public Modifier headArmorModifier;

            public Stat bodyArmorStat;
            public Modifier bodyArmorModifier;

            public void Clear()
            {
                maxHealthStat = null;
                maxHealthModifier = null;
                gunDamageStat = null;
                gunDamageModifier = null;
                meleeDamageStat = null;
                meleeDamageModifier = null;
                headArmorStat = null;
                headArmorModifier = null;
                bodyArmorStat = null;
                bodyArmorModifier = null;
            }
        }

        private GameObject f3DebugCheatMenuRoot;
        private bool f3DebugCheatMenuVisible = false;
        private F3DebugCheatPage f3DebugCheatCurrentPage = F3DebugCheatPage.Teleport;
        private Transform f3DebugCheatContentRoot;
        private Text f3DebugCheatSummaryText;
        private Text f3DebugCheatStatusText;
        private readonly Dictionary<F3DebugCheatPage, Image> f3DebugCheatNavButtonImages = new Dictionary<F3DebugCheatPage, Image>();

        private InputField f3ItemIdInputField;
        private InputField f3ItemCountInputField;
        private InputField f3MoneyInputField;
        private InputField f3MaxHealthMultiplierInputField;
        private InputField f3GunDamageMultiplierInputField;
        private InputField f3MeleeDamageMultiplierInputField;
        private InputField f3HeadArmorInputField;
        private InputField f3BodyArmorInputField;

        private readonly F3DebugCheatPlayerState f3DebugCheatPlayerState = new F3DebugCheatPlayerState();
        private readonly F3DebugCheatRuntimeBindings f3DebugCheatRuntimeBindings = new F3DebugCheatRuntimeBindings();
        private float f3DebugCheatSummaryNextRefreshTime = -1f;
        private float f3DebugCheatPlayerNextApplyTime = -1f;
        private bool f3DebugCheatPlayerApplyPending = false;
        private string f3DebugCheatPlayerApplyReason = string.Empty;
        private float f3DebugCheatPreviousTimeScale = 1f;
        private bool f3DebugCheatPreviousCursorVisible = false;
        private CursorLockMode f3DebugCheatPreviousCursorLockState = CursorLockMode.Locked;
        private bool f3DebugCheatPresentationStateCaptured = false;
        private bool f3DebugCheatInputDisabled = false;

        private void CheckF3DebugCheatMenuHotkey()
        {
            if (!DevModeEnabled)
            {
                if (f3DebugCheatMenuVisible)
                {
                    HideF3DebugCheatMenu();
                }
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.F3))
            {
                ToggleF3DebugCheatMenu();
            }
        }

        private void TickF3DebugCheatMenu()
        {
            if (f3DebugCheatMenuVisible && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                HideF3DebugCheatMenu();
                return;
            }

            if (f3DebugCheatMenuVisible && Time.unscaledTime >= f3DebugCheatSummaryNextRefreshTime)
            {
                RefreshF3DebugCheatSummary();
                f3DebugCheatSummaryNextRefreshTime = Time.unscaledTime + 0.25f;
            }

            if (HasActiveF3PlayerCheatConfig() && f3DebugCheatPlayerApplyPending && Time.unscaledTime >= f3DebugCheatPlayerNextApplyTime)
            {
                ApplyPlayerCheatParameters(true);
            }
        }

        private void F3DebugCheatMenuLateUpdate()
        {
            if (!f3DebugCheatMenuVisible)
            {
                return;
            }

            ApplyF3DebugCheatPresentationState();
        }

        private void OnSceneLoaded_F3DebugCheatMenu(Scene scene, LoadSceneMode mode)
        {
            RemovePlayerCheatRuntimeModifiers();
            QueuePlayerCheatApply("scene_loaded");

            if (f3DebugCheatMenuRoot != null)
            {
                RefreshF3DebugCheatSummary();
            }
        }

        private void OnDestroy_F3DebugCheatMenu()
        {
            RemovePlayerCheatRuntimeModifiers();
            f3DebugCheatRuntimeBindings.Clear();
            DestroyF3DebugCheatMenuUI();
        }

        private void ToggleF3DebugCheatMenu()
        {
            if (f3DebugCheatMenuVisible)
            {
                HideF3DebugCheatMenu();
            }
            else
            {
                ShowF3DebugCheatMenu();
            }
        }

        private void ShowF3DebugCheatMenu()
        {
            try
            {
                if (f3DebugCheatMenuRoot == null || f3DebugCheatContentRoot == null || f3DebugCheatSummaryText == null || f3DebugCheatStatusText == null)
                {
                    DestroyF3DebugCheatMenuUI();
                    CreateF3DebugCheatMenuUI();
                }

                if (f3DebugCheatMenuRoot == null)
                {
                    SetF3DebugCheatStatus(L10n.T("调试菜单创建失败", "Failed to create debug menu"), true);
                    return;
                }

                CaptureF3DebugCheatPresentationState();
                f3DebugCheatMenuRoot.SetActive(true);
                f3DebugCheatMenuVisible = true;
                ApplyF3DebugCheatPresentationState();
                DisableF3DebugCheatInput();

                RefreshF3DebugCheatSummary();
                RefreshF3DebugCheatPage();
                f3DebugCheatSummaryNextRefreshTime = Time.unscaledTime + 0.25f;
            }
            catch (Exception e)
            {
                try
                {
                    EnableF3DebugCheatInput();
                    if (f3DebugCheatMenuRoot != null)
                    {
                        f3DebugCheatMenuRoot.SetActive(false);
                    }
                }
                catch { }

                f3DebugCheatMenuVisible = false;
                try
                {
                    RestoreF3DebugCheatPresentationState();
                }
                catch { }

                DevLog("[BossRush] F3 调试菜单显示失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("打开调试菜单失败", "Failed to open debug menu"), true);
            }
        }

        private void HideF3DebugCheatMenu()
        {
            try
            {
                EnableF3DebugCheatInput();

                if (f3DebugCheatMenuRoot != null)
                {
                    f3DebugCheatMenuRoot.SetActive(false);
                }

                f3DebugCheatMenuVisible = false;
                RestoreF3DebugCheatPresentationState();
            }
            catch (Exception e)
            {
                f3DebugCheatMenuVisible = false;
                try
                {
                    RestoreF3DebugCheatPresentationState();
                }
                catch { }
                DevLog("[BossRush] F3 调试菜单隐藏失败: " + e.Message);
            }
        }

        private void CaptureF3DebugCheatPresentationState()
        {
            if (f3DebugCheatPresentationStateCaptured)
            {
                return;
            }

            f3DebugCheatPreviousTimeScale = Time.timeScale;
            f3DebugCheatPreviousCursorVisible = Cursor.visible;
            f3DebugCheatPreviousCursorLockState = Cursor.lockState;
            f3DebugCheatPresentationStateCaptured = true;
        }

        private void ApplyF3DebugCheatPresentationState()
        {
            Time.timeScale = 0f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void RestoreF3DebugCheatPresentationState()
        {
            if (!f3DebugCheatPresentationStateCaptured)
            {
                return;
            }

            Time.timeScale = f3DebugCheatPreviousTimeScale;
            Cursor.visible = f3DebugCheatPreviousCursorVisible;
            Cursor.lockState = f3DebugCheatPreviousCursorLockState;
            f3DebugCheatPresentationStateCaptured = false;
        }

        private void DisableF3DebugCheatInput()
        {
            if (f3DebugCheatInputDisabled || f3DebugCheatMenuRoot == null)
            {
                return;
            }

            InputManager.DisableInput(f3DebugCheatMenuRoot);
            f3DebugCheatInputDisabled = true;
        }

        private void EnableF3DebugCheatInput()
        {
            if (!f3DebugCheatInputDisabled || f3DebugCheatMenuRoot == null)
            {
                return;
            }

            InputManager.ActiveInput(f3DebugCheatMenuRoot);
            f3DebugCheatInputDisabled = false;
        }

        private void CreateF3DebugCheatMenuUI()
        {
            try
            {
                Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");

                f3DebugCheatMenuRoot = new GameObject("F3DebugCheatMenu");
                Canvas canvas = f3DebugCheatMenuRoot.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1001;

                CanvasScaler scaler = f3DebugCheatMenuRoot.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                f3DebugCheatMenuRoot.AddComponent<GraphicRaycaster>();

                GameObject background = CreateUIObject("Background", f3DebugCheatMenuRoot.transform);
                RectTransform backgroundRect = background.GetComponent<RectTransform>();
                backgroundRect.anchorMin = Vector2.zero;
                backgroundRect.anchorMax = Vector2.one;
                backgroundRect.offsetMin = Vector2.zero;
                backgroundRect.offsetMax = Vector2.zero;
                Image backgroundImage = background.AddComponent<Image>();
                backgroundImage.color = new Color(0f, 0f, 0f, 0.72f);
                Button backgroundButton = background.AddComponent<Button>();
                backgroundButton.onClick.AddListener(HideF3DebugCheatMenu);

                GameObject panel = CreateUIObject("Panel", f3DebugCheatMenuRoot.transform);
                RectTransform panelRect = panel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(1550f, 920f);
                Image panelImage = panel.AddComponent<Image>();
                panelImage.color = new Color(0.08f, 0.09f, 0.12f, 0.98f);

                GameObject title = CreateUIObject("Title", panel.transform);
                RectTransform titleRect = title.GetComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0f, 1f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.pivot = new Vector2(0.5f, 1f);
                titleRect.anchoredPosition = new Vector2(0f, -18f);
                titleRect.sizeDelta = new Vector2(-150f, 42f);
                Text titleText = title.AddComponent<Text>();
                titleText.font = font;
                titleText.fontSize = 28;
                titleText.alignment = TextAnchor.MiddleLeft;
                titleText.color = Color.white;
                titleText.text = L10n.T("F3 调试作弊总控面板", "F3 Debug Cheat Control");

                GameObject closeButtonObject = CreateUIObject("CloseButton", panel.transform);
                RectTransform closeButtonRect = closeButtonObject.GetComponent<RectTransform>();
                closeButtonRect.anchorMin = new Vector2(1f, 1f);
                closeButtonRect.anchorMax = new Vector2(1f, 1f);
                closeButtonRect.pivot = new Vector2(1f, 1f);
                closeButtonRect.anchoredPosition = new Vector2(-20f, -18f);
                closeButtonRect.sizeDelta = new Vector2(120f, 42f);
                Image closeButtonImage = closeButtonObject.AddComponent<Image>();
                closeButtonImage.color = new Color(0.35f, 0.18f, 0.18f, 1f);
                Button closeButton = closeButtonObject.AddComponent<Button>();
                closeButton.onClick.AddListener(HideF3DebugCheatMenu);
                CreateCenteredText(closeButtonObject.transform, font, L10n.T("关闭", "Close"), 20, Color.white);

                GameObject summary = CreateUIObject("Summary", panel.transform);
                RectTransform summaryRect = summary.GetComponent<RectTransform>();
                summaryRect.anchorMin = new Vector2(0f, 1f);
                summaryRect.anchorMax = new Vector2(1f, 1f);
                summaryRect.pivot = new Vector2(0.5f, 1f);
                summaryRect.anchoredPosition = new Vector2(0f, -74f);
                summaryRect.sizeDelta = new Vector2(-40f, 104f);
                Image summaryImage = summary.AddComponent<Image>();
                summaryImage.color = new Color(0.12f, 0.14f, 0.18f, 1f);
                f3DebugCheatSummaryText = CreateFillText(summary.transform, font, 18, new Color(0.92f, 0.95f, 0.98f, 1f), TextAnchor.UpperLeft);
                f3DebugCheatSummaryText.horizontalOverflow = HorizontalWrapMode.Wrap;
                f3DebugCheatSummaryText.verticalOverflow = VerticalWrapMode.Overflow;
                f3DebugCheatSummaryText.text = string.Empty;

                GameObject navPanel = CreateUIObject("NavPanel", panel.transform);
                RectTransform navRect = navPanel.GetComponent<RectTransform>();
                navRect.anchorMin = new Vector2(0f, 0f);
                navRect.anchorMax = new Vector2(0f, 1f);
                navRect.pivot = new Vector2(0f, 1f);
                navRect.anchoredPosition = new Vector2(20f, -194f);
                navRect.sizeDelta = new Vector2(220f, -270f);
                Image navImage = navPanel.AddComponent<Image>();
                navImage.color = new Color(0.11f, 0.12f, 0.16f, 1f);
                VerticalLayoutGroup navLayout = navPanel.AddComponent<VerticalLayoutGroup>();
                navLayout.padding = new RectOffset(10, 10, 10, 10);
                navLayout.spacing = 10f;
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

                GameObject contentPanel = CreateUIObject("ContentPanel", panel.transform);
                RectTransform contentPanelRect = contentPanel.GetComponent<RectTransform>();
                contentPanelRect.anchorMin = new Vector2(0f, 0f);
                contentPanelRect.anchorMax = new Vector2(1f, 1f);
                contentPanelRect.offsetMin = new Vector2(260f, 68f);
                contentPanelRect.offsetMax = new Vector2(-20f, -194f);
                Image contentPanelImage = contentPanel.AddComponent<Image>();
                contentPanelImage.color = new Color(0.10f, 0.11f, 0.15f, 1f);

                GameObject scrollView = CreateUIObject("ScrollView", contentPanel.transform);
                RectTransform scrollRect = scrollView.GetComponent<RectTransform>();
                scrollRect.anchorMin = Vector2.zero;
                scrollRect.anchorMax = Vector2.one;
                scrollRect.offsetMin = new Vector2(14f, 14f);
                scrollRect.offsetMax = new Vector2(-14f, -14f);
                ScrollRect scroll = scrollView.AddComponent<ScrollRect>();
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;

                GameObject viewport = CreateUIObject("Viewport", scrollView.transform);
                RectTransform viewportRect = viewport.GetComponent<RectTransform>();
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = Vector2.zero;
                viewportRect.offsetMax = Vector2.zero;
                // 注意：Mask 依赖 Image 渲染写入 Stencil Buffer 才能让子节点显示。
                // 默认 UI Shader 开启 UNITY_UI_ALPHACLIP，当 color.a < 0.001 时会 discard 像素，
                // 导致 Stencil 不写入，所有子元素会被完全裁切（表现为内容区空白）。
                // 所以 alpha 必须 > 0，再通过 Mask.showMaskGraphic=false 隐藏颜色本身。
                Image viewportImage = viewport.AddComponent<Image>();
                viewportImage.color = new Color(1f, 1f, 1f, 1f);
                Mask viewportMask = viewport.AddComponent<Mask>();
                viewportMask.showMaskGraphic = false;

                GameObject content = CreateUIObject("Content", viewport.transform);
                RectTransform contentRect = content.GetComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0.5f, 1f);
                contentRect.anchoredPosition = Vector2.zero;
                contentRect.sizeDelta = new Vector2(0f, 0f);
                VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
                contentLayout.padding = new RectOffset(8, 8, 8, 8);
                contentLayout.spacing = 14f;
                contentLayout.childControlHeight = false;
                contentLayout.childControlWidth = true;
                contentLayout.childForceExpandHeight = false;
                contentLayout.childForceExpandWidth = true;
                ContentSizeFitter contentFitter = content.AddComponent<ContentSizeFitter>();
                contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scroll.viewport = viewportRect;
                scroll.content = contentRect;
                f3DebugCheatContentRoot = content.transform;

                GameObject statusBar = CreateUIObject("StatusBar", panel.transform);
                RectTransform statusRect = statusBar.GetComponent<RectTransform>();
                statusRect.anchorMin = new Vector2(0f, 0f);
                statusRect.anchorMax = new Vector2(1f, 0f);
                statusRect.pivot = new Vector2(0.5f, 0f);
                statusRect.anchoredPosition = new Vector2(0f, 16f);
                statusRect.sizeDelta = new Vector2(-40f, 40f);
                Image statusImage = statusBar.AddComponent<Image>();
                statusImage.color = new Color(0.13f, 0.15f, 0.18f, 1f);
                f3DebugCheatStatusText = CreateFillText(statusBar.transform, font, 18, new Color(0.90f, 0.95f, 0.98f, 1f), TextAnchor.MiddleLeft);
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
        }

        private void CreateF3DebugCheatNavButton(Transform parent, Font font, F3DebugCheatPage page, string text)
        {
            GameObject buttonObject = CreateUIObject("Nav_" + page, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.18f, 0.20f, 0.26f, 1f);
            Button button = buttonObject.AddComponent<Button>();
            button.onClick.AddListener(delegate
            {
                f3DebugCheatCurrentPage = page;
                RefreshF3DebugCheatPage();
            });
            LayoutElement layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 48f;
            CreateCenteredText(buttonObject.transform, font, text, 18, Color.white);
            f3DebugCheatNavButtonImages[page] = image;
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
            }

            RefreshF3DebugCheatSummary();
        }

        private void BuildF3TeleportPage()
        {
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
        }

        private void BuildF3SceneDebugPage()
        {
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
        }

        private GameObject CreateF3Section(string title, string description, Font font)
        {
            GameObject section = CreateUIObject("Section_" + title, f3DebugCheatContentRoot);
            Image sectionImage = section.AddComponent<Image>();
            sectionImage.color = new Color(0.14f, 0.15f, 0.20f, 1f);
            VerticalLayoutGroup layout = section.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 10f;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = section.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LayoutElement element = section.AddComponent<LayoutElement>();
            element.minHeight = 120f;

            CreateLabel(section.transform, font, title, 24, new Color(0.97f, 0.98f, 1f, 1f), FontStyle.Bold);
            if (!string.IsNullOrEmpty(description))
            {
                CreateLabel(section.transform, font, description, 17, new Color(0.84f, 0.89f, 0.95f, 1f), FontStyle.Normal);
            }
            return section;
        }

        private GameObject CreateF3Row(Transform parent)
        {
            GameObject row = CreateUIObject("Row", parent);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            ContentSizeFitter fitter = row.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return row;
        }

        private GameObject CreateF3LabeledInputRow(Transform parent, Font font, string label, out InputField inputField, string text)
        {
            GameObject row = CreateF3Row(parent);
            CreateLabelWithWidth(row.transform, font, label, 18, 210f);
            inputField = CreateInputField(row.transform, font, text, 140f);
            return row;
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private Text CreateLabel(Transform parent, Font font, string text, int fontSize, Color color, FontStyle style)
        {
            GameObject go = CreateUIObject("Label", parent);
            Text label = go.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.fontStyle = style;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return label;
        }

        private Text CreateLabelWithWidth(Transform parent, Font font, string text, int fontSize, float width)
        {
            GameObject go = CreateUIObject("Label", parent);
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            element.preferredHeight = 34f;
            Text label = go.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            return label;
        }

        private void CreateCenteredText(Transform parent, Font font, string text, int fontSize, Color color)
        {
            GameObject textObject = CreateUIObject("Text", parent);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Text label = textObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
        }

        private Text CreateFillText(Transform parent, Font font, int fontSize, Color color, TextAnchor alignment)
        {
            GameObject textObject = CreateUIObject("Text", parent);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 6f);
            rect.offsetMax = new Vector2(-10f, -6f);

            Text label = textObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            return label;
        }

        private Button CreateActionButton(Transform parent, Font font, string text, Color color, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = CreateUIObject("Button_" + text, parent);
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.preferredWidth = 170f;
            element.preferredHeight = 40f;
            element.minHeight = 40f;
            Image image = go.AddComponent<Image>();
            image.color = color;
            Button button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);
            CreateCenteredText(go.transform, font, text, 17, Color.white);
            return button;
        }

        private Button CreatePresetButton(Transform parent, Font font, string text, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = CreateUIObject("Preset_" + text, parent);
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.preferredWidth = 76f;
            element.preferredHeight = 36f;
            Image image = go.AddComponent<Image>();
            image.color = new Color(0.25f, 0.28f, 0.35f, 1f);
            Button button = go.AddComponent<Button>();
            button.onClick.AddListener(onClick);
            CreateCenteredText(go.transform, font, text, 16, Color.white);
            return button;
        }

        private InputField CreateInputField(Transform parent, Font font, string text, float width)
        {
            GameObject root = CreateUIObject("InputField", parent);
            LayoutElement element = root.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            element.preferredHeight = 40f;
            Image image = root.AddComponent<Image>();
            image.color = new Color(0.95f, 0.97f, 1f, 1f);
            InputField inputField = root.AddComponent<InputField>();

            GameObject placeholderObject = CreateUIObject("Placeholder", root.transform);
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(10f, 6f);
            placeholderRect.offsetMax = new Vector2(-10f, -6f);
            Text placeholderText = placeholderObject.AddComponent<Text>();
            placeholderText.font = font;
            placeholderText.fontSize = 17;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.color = new Color(0.55f, 0.58f, 0.62f, 1f);
            placeholderText.text = text;

            GameObject textObject = CreateUIObject("Text", root.transform);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 6f);
            textRect.offsetMax = new Vector2(-10f, -6f);
            Text fieldText = textObject.AddComponent<Text>();
            fieldText.font = font;
            fieldText.fontSize = 17;
            fieldText.alignment = TextAnchor.MiddleLeft;
            fieldText.color = new Color(0.12f, 0.12f, 0.15f, 1f);
            fieldText.text = text;

            inputField.textComponent = fieldText;
            inputField.placeholder = placeholderText;
            inputField.text = text;
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.contentType = InputField.ContentType.Standard;
            return inputField;
        }

        private void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private void UpdateF3DebugCheatNavButtonColors()
        {
            foreach (KeyValuePair<F3DebugCheatPage, Image> entry in f3DebugCheatNavButtonImages)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                entry.Value.color = entry.Key == f3DebugCheatCurrentPage
                    ? new Color(0.30f, 0.45f, 0.68f, 1f)
                    : new Color(0.18f, 0.20f, 0.26f, 1f);
            }
        }

        private void RefreshF3DebugCheatSummary()
        {
            if (f3DebugCheatSummaryText == null)
            {
                return;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            string subSceneId = string.Empty;
            try
            {
                subSceneId = MultiSceneCore.ActiveSubSceneID ?? string.Empty;
            }
            catch { }

            string playerPosText = "-";
            CharacterMainControl player;
            if (TryGetMainCharacter(out player))
            {
                Vector3 p = player.transform.position;
                playerPosText = string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}, {2:0.0})", p.x, p.y, p.z);
            }

            string summary = L10n.T("DevMode: ", "DevMode: ") + DevModeEnabled
                + "    " + L10n.T("场景: ", "Scene: ") + sceneName
                + "    " + L10n.T("SubScene: ", "SubScene: ") + (string.IsNullOrEmpty(subSceneId) ? "-" : subSceneId)
                + "    " + L10n.T("坐标: ", "Pos: ") + playerPosText
                + "\n" + L10n.T("BossRush激活: ", "BossRush Active: ") + IsActive
                + "    ModeE: " + modeEActive
                + "    ModeF: " + modeFActive
                + "\n" + L10n.T("作弊状态: ", "Cheat State: ") + BuildPlayerCheatStateSummary()
                + "\n" + BuildCurrentPlayerStatsReadout();

            f3DebugCheatSummaryText.text = summary;
        }

        private string BuildPlayerCheatStateSummary()
        {
            List<string> parts = new List<string>();
            parts.Add("HP x" + f3DebugCheatPlayerState.maxHealthMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            parts.Add("Gun x" + f3DebugCheatPlayerState.gunDamageMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            parts.Add("Melee x" + f3DebugCheatPlayerState.meleeDamageMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
            parts.Add("Head=" + (f3DebugCheatPlayerState.headArmorOverride.HasValue ? f3DebugCheatPlayerState.headArmorOverride.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-"));
            parts.Add("Body=" + (f3DebugCheatPlayerState.bodyArmorOverride.HasValue ? f3DebugCheatPlayerState.bodyArmorOverride.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-"));
            return string.Join(", ", parts.ToArray());
        }

        private string BuildCurrentPlayerStatsReadout()
        {
            CharacterMainControl player;
            if (!TryGetMainCharacter(out player))
            {
                return L10n.T("当前玩家属性: 玩家未就绪", "Current player stats: player not ready");
            }

            float hp = ReadCharacterStatValue(player, "MaxHealth");
            float gun = ReadCharacterStatValue(player, "GunDamageMultiplier");
            float melee = ReadCharacterStatValue(player, "MeleeDamageMultiplier");
            float head = ReadArmorStatValue(player, "HeadArmor", true);
            float body = ReadArmorStatValue(player, "BodyArmor", false);

            return string.Format(CultureInfo.InvariantCulture,
                "{0}{1:0.##}    Gun={2:0.##}    Melee={3:0.##}    HeadArmor={4:0.##}    BodyArmor={5:0.##}",
                L10n.T("当前玩家属性: HP=", "Current Player Stats: HP="),
                hp,
                gun,
                melee,
                head,
                body);
        }

        private void SetF3DebugCheatStatus(string message, bool isError)
        {
            if (f3DebugCheatStatusText != null)
            {
                f3DebugCheatStatusText.text = message;
                f3DebugCheatStatusText.color = isError
                    ? new Color(1f, 0.65f, 0.65f, 1f)
                    : new Color(0.90f, 0.95f, 0.98f, 1f);
            }

            if (!string.IsNullOrEmpty(message))
            {
                try
                {
                    ShowMessage(message);
                }
                catch { }
            }
        }

        private bool TryGetMainCharacter(out CharacterMainControl main)
        {
            main = null;
            CharacterMainControl candidate = null;

            try
            {
                candidate = CharacterMainControl.Main;
            }
            catch { }

            if (IsMainCharacterForF3Debug(candidate))
            {
                main = candidate;
                return true;
            }

            try
            {
                candidate = playerCharacter as CharacterMainControl;
            }
            catch { }

            if (IsMainCharacterForF3Debug(candidate))
            {
                main = candidate;
                return true;
            }

            return false;
        }

        private static bool IsMainCharacterForF3Debug(CharacterMainControl candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            try
            {
                if (candidate == CharacterMainControl.Main)
                {
                    return true;
                }
            }
            catch { }

            try
            {
                return CharacterMainControlExtensions.IsMainCharacter(candidate);
            }
            catch { }

            return false;
        }

        private float ReadCharacterStatValue(CharacterMainControl player, string statName)
        {
            if (player == null || player.CharacterItem == null)
            {
                return -1f;
            }

            try
            {
                Stat stat = player.CharacterItem.GetStat(statName);
                if (stat != null)
                {
                    return stat.Value;
                }
            }
            catch { }

            return -1f;
        }

        private float ReadArmorStatValue(CharacterMainControl player, string statName, bool isHelmet)
        {
            if (player == null)
            {
                return -1f;
            }

            try
            {
                if (player.CharacterItem != null)
                {
                    Stat rootStat = player.CharacterItem.GetStat(statName);
                    if (rootStat != null)
                    {
                        return rootStat.Value;
                    }
                }
            }
            catch { }

            try
            {
                Item equipped = isHelmet ? player.GetHelmatItem() : player.GetArmorItem();
                if (equipped != null)
                {
                    Stat stat = equipped.GetStat(statName);
                    if (stat != null)
                    {
                        return stat.Value;
                    }
                }
            }
            catch { }

            return -1f;
        }

        private void ApplyAllPlayerCheatInputs()
        {
            float maxHealthMultiplier;
            if (!TryReadFloatInput(f3MaxHealthMultiplierInputField, 1f, out maxHealthMultiplier))
            {
                SetF3DebugCheatStatus(L10n.T("血量倍率输入无效", "Invalid health multiplier"), true);
                return;
            }

            float gunDamageMultiplier;
            if (!TryReadFloatInput(f3GunDamageMultiplierInputField, 1f, out gunDamageMultiplier))
            {
                SetF3DebugCheatStatus(L10n.T("枪械伤害倍率输入无效", "Invalid gun multiplier"), true);
                return;
            }

            float meleeDamageMultiplier;
            if (!TryReadFloatInput(f3MeleeDamageMultiplierInputField, 1f, out meleeDamageMultiplier))
            {
                SetF3DebugCheatStatus(L10n.T("近战伤害倍率输入无效", "Invalid melee multiplier"), true);
                return;
            }

            float? headArmorOverride;
            if (!TryReadOptionalFloatInput(f3HeadArmorInputField, out headArmorOverride))
            {
                SetF3DebugCheatStatus(L10n.T("头部护甲输入无效", "Invalid head armor override"), true);
                return;
            }

            float? bodyArmorOverride;
            if (!TryReadOptionalFloatInput(f3BodyArmorInputField, out bodyArmorOverride))
            {
                SetF3DebugCheatStatus(L10n.T("身体护甲输入无效", "Invalid body armor override"), true);
                return;
            }

            f3DebugCheatPlayerState.maxHealthMultiplier = F3DebugCheatMath.SanitizeMultiplier(maxHealthMultiplier);
            f3DebugCheatPlayerState.gunDamageMultiplier = F3DebugCheatMath.SanitizeMultiplier(gunDamageMultiplier);
            f3DebugCheatPlayerState.meleeDamageMultiplier = F3DebugCheatMath.SanitizeMultiplier(meleeDamageMultiplier);
            f3DebugCheatPlayerState.headArmorOverride = headArmorOverride;
            f3DebugCheatPlayerState.bodyArmorOverride = bodyArmorOverride;

            ApplyPlayerCheatParameters(false);
            RefreshF3DebugCheatSummary();
        }

        private void ApplyPlayerCheatParameters(bool silent)
        {
            RemovePlayerCheatRuntimeModifiers();

            CharacterMainControl player;
            if (!TryGetMainCharacter(out player))
            {
                QueuePlayerCheatApply("player_missing");
                if (!silent)
                {
                    SetF3DebugCheatStatus(L10n.T("玩家未就绪，参数已保存，稍后自动应用", "Player not ready. Values saved and will auto-apply later"), true);
                }
                return;
            }

            Item characterItem = player.CharacterItem;
            if (characterItem == null)
            {
                QueuePlayerCheatApply("character_item_missing");
                if (!silent)
                {
                    SetF3DebugCheatStatus(L10n.T("玩家 CharacterItem 未就绪，稍后自动应用", "Player CharacterItem not ready. Will retry later"), true);
                }
                return;
            }

            try
            {
                ApplyCharacterMultiplierModifier(characterItem, "MaxHealth", f3DebugCheatPlayerState.maxHealthMultiplier, ref f3DebugCheatRuntimeBindings.maxHealthStat, ref f3DebugCheatRuntimeBindings.maxHealthModifier);
                ApplyCharacterMultiplierModifier(characterItem, "GunDamageMultiplier", f3DebugCheatPlayerState.gunDamageMultiplier, ref f3DebugCheatRuntimeBindings.gunDamageStat, ref f3DebugCheatRuntimeBindings.gunDamageModifier);
                ApplyCharacterMultiplierModifier(characterItem, "MeleeDamageMultiplier", f3DebugCheatPlayerState.meleeDamageMultiplier, ref f3DebugCheatRuntimeBindings.meleeDamageStat, ref f3DebugCheatRuntimeBindings.meleeDamageModifier);

                ApplyArmorOverrideModifier(player, "HeadArmor", true, f3DebugCheatPlayerState.headArmorOverride, ref f3DebugCheatRuntimeBindings.headArmorStat, ref f3DebugCheatRuntimeBindings.headArmorModifier);
                ApplyArmorOverrideModifier(player, "BodyArmor", false, f3DebugCheatPlayerState.bodyArmorOverride, ref f3DebugCheatRuntimeBindings.bodyArmorStat, ref f3DebugCheatRuntimeBindings.bodyArmorModifier);

                if (player.Health != null)
                {
                    player.Health.SetHealth(player.Health.MaxHealth);
                }

                f3DebugCheatPlayerApplyPending = false;
                f3DebugCheatPlayerApplyReason = string.Empty;
                f3DebugCheatPlayerNextApplyTime = Time.unscaledTime + 1f;

                if (!silent)
                {
                    SetF3DebugCheatStatus(L10n.T("玩家参数已应用", "Player cheat parameters applied"), false);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 应用玩家作弊参数失败: " + e.Message);
                QueuePlayerCheatApply("apply_failed");
                if (!silent)
                {
                    SetF3DebugCheatStatus(L10n.T("应用玩家参数失败", "Failed to apply player parameters"), true);
                }
            }
        }

        private void ApplyCharacterMultiplierModifier(Item characterItem, string statName, float multiplier, ref Stat trackedStat, ref Modifier trackedModifier)
        {
            if (characterItem == null)
            {
                return;
            }

            Stat stat = null;
            try
            {
                stat = characterItem.GetStat(statName);
            }
            catch { }

            if (stat == null)
            {
                trackedStat = null;
                trackedModifier = null;
                return;
            }

            trackedStat = stat;
            if (Mathf.Abs(multiplier - 1f) <= 0.0001f)
            {
                trackedModifier = null;
                return;
            }

            float delta = F3DebugCheatMath.ComputeMultiplierAdditiveDelta(stat.BaseValue, multiplier);
            Modifier modifier = new Modifier(ModifierType.Add, delta, this);
            stat.AddModifier(modifier);
            trackedModifier = modifier;
        }

        private void ApplyArmorOverrideModifier(CharacterMainControl player, string statName, bool isHelmet, float? overrideValue, ref Stat trackedStat, ref Modifier trackedModifier)
        {
            trackedStat = null;
            trackedModifier = null;

            if (!overrideValue.HasValue)
            {
                return;
            }

            Stat stat;
            if (!TryResolveArmorTargetStat(player, statName, isHelmet, out stat))
            {
                QueuePlayerCheatApply(isHelmet ? "head_armor_missing" : "body_armor_missing");
                return;
            }

            trackedStat = stat;
            float delta = F3DebugCheatMath.ComputeAbsoluteAdditiveDelta(stat.Value, overrideValue.Value);
            Modifier modifier = new Modifier(ModifierType.Add, delta, this);
            stat.AddModifier(modifier);
            trackedModifier = modifier;
        }

        private bool TryResolveArmorTargetStat(CharacterMainControl player, string statName, bool isHelmet, out Stat stat)
        {
            stat = null;
            if (player == null)
            {
                return false;
            }

            try
            {
                if (player.CharacterItem != null)
                {
                    stat = player.CharacterItem.GetStat(statName);
                    if (stat != null)
                    {
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                Item equipped = isHelmet ? player.GetHelmatItem() : player.GetArmorItem();
                if (equipped != null)
                {
                    stat = equipped.GetStat(statName);
                    if (stat != null)
                    {
                        return true;
                    }

                    stat = EnsureRuntimeStatExists(equipped, statName, 0f);
                    if (stat != null)
                    {
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                if (player.CharacterItem != null)
                {
                    stat = EnsureRuntimeStatExists(player.CharacterItem, statName, 0f);
                    if (stat != null)
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private Stat EnsureRuntimeStatExists(Item item, string statName, float defaultValue)
        {
            if (item == null)
            {
                return null;
            }

            StatCollection stats = item.Stats;
            if (stats == null)
            {
                try
                {
                    item.CreateStatsComponent();
                    stats = item.Stats;
                }
                catch { }
            }

            if (stats == null)
            {
                return null;
            }

            Stat stat = null;
            try
            {
                stat = stats.GetStat(statName);
            }
            catch { }

            if (stat != null)
            {
                return stat;
            }

            try
            {
                stat = new Stat(statName, defaultValue, false);
                stats.Add(stat);
                return stat;
            }
            catch
            {
                return null;
            }
        }

        private void RemovePlayerCheatRuntimeModifiers()
        {
            TryRemoveTrackedModifier(f3DebugCheatRuntimeBindings.maxHealthStat, f3DebugCheatRuntimeBindings.maxHealthModifier);
            TryRemoveTrackedModifier(f3DebugCheatRuntimeBindings.gunDamageStat, f3DebugCheatRuntimeBindings.gunDamageModifier);
            TryRemoveTrackedModifier(f3DebugCheatRuntimeBindings.meleeDamageStat, f3DebugCheatRuntimeBindings.meleeDamageModifier);
            TryRemoveTrackedModifier(f3DebugCheatRuntimeBindings.headArmorStat, f3DebugCheatRuntimeBindings.headArmorModifier);
            TryRemoveTrackedModifier(f3DebugCheatRuntimeBindings.bodyArmorStat, f3DebugCheatRuntimeBindings.bodyArmorModifier);
            f3DebugCheatRuntimeBindings.Clear();
        }

        private void TryRemoveTrackedModifier(Stat stat, Modifier modifier)
        {
            if (stat == null || modifier == null)
            {
                return;
            }

            try
            {
                stat.RemoveModifier(modifier);
            }
            catch { }
        }

        private bool HasActiveF3PlayerCheatConfig()
        {
            return Mathf.Abs(f3DebugCheatPlayerState.maxHealthMultiplier - 1f) > 0.0001f
                || Mathf.Abs(f3DebugCheatPlayerState.gunDamageMultiplier - 1f) > 0.0001f
                || Mathf.Abs(f3DebugCheatPlayerState.meleeDamageMultiplier - 1f) > 0.0001f
                || f3DebugCheatPlayerState.headArmorOverride.HasValue
                || f3DebugCheatPlayerState.bodyArmorOverride.HasValue;
        }

        private void QueuePlayerCheatApply(string reason)
        {
            if (!HasActiveF3PlayerCheatConfig())
            {
                f3DebugCheatPlayerApplyPending = false;
                f3DebugCheatPlayerApplyReason = string.Empty;
                f3DebugCheatPlayerNextApplyTime = Time.unscaledTime + 1f;
                return;
            }

            f3DebugCheatPlayerApplyPending = true;
            f3DebugCheatPlayerApplyReason = reason ?? string.Empty;
            f3DebugCheatPlayerNextApplyTime = Time.unscaledTime + 1f;
        }

        private void ResetPlayerCheatConfigToDefaults()
        {
            RemovePlayerCheatRuntimeModifiers();
            f3DebugCheatPlayerState.Reset();
            f3DebugCheatPlayerApplyPending = false;
            f3DebugCheatPlayerApplyReason = string.Empty;
            f3DebugCheatPlayerNextApplyTime = Time.unscaledTime + 1f;

            if (f3MaxHealthMultiplierInputField != null) f3MaxHealthMultiplierInputField.text = "1";
            if (f3GunDamageMultiplierInputField != null) f3GunDamageMultiplierInputField.text = "1";
            if (f3MeleeDamageMultiplierInputField != null) f3MeleeDamageMultiplierInputField.text = "1";
            if (f3HeadArmorInputField != null) f3HeadArmorInputField.text = string.Empty;
            if (f3BodyArmorInputField != null) f3BodyArmorInputField.text = string.Empty;

            RefreshF3DebugCheatSummary();
            SetF3DebugCheatStatus(L10n.T("玩家作弊参数已恢复默认", "Player cheat parameters reset to default"), false);
        }

        private bool TryReadFloatInput(InputField field, float defaultValue, out float result)
        {
            result = defaultValue;
            if (field == null)
            {
                return true;
            }

            string text = field.text != null ? field.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                result = defaultValue;
                return true;
            }

            return TryParseFloat(text, out result);
        }

        private bool TryReadOptionalFloatInput(InputField field, out float? result)
        {
            result = null;
            if (field == null)
            {
                return true;
            }

            string text = field.text != null ? field.text.Trim() : string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            float value;
            if (!TryParseFloat(text, out value))
            {
                return false;
            }

            result = value;
            return true;
        }

        private bool TryParseFloat(string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return true;
            }

            return false;
        }

        private void SetInputFieldText(InputField field, string text)
        {
            if (field != null)
            {
                field.text = text;
            }
        }

        private void HealPlayerToFull()
        {
            CharacterMainControl player;
            if (!TryGetMainCharacter(out player) || player.Health == null)
            {
                SetF3DebugCheatStatus(L10n.T("玩家未就绪，无法满血", "Player not ready. Cannot heal"), true);
                return;
            }

            player.Health.SetHealth(player.Health.MaxHealth);
            SetF3DebugCheatStatus(L10n.T("已恢复至满血", "Healed to full"), false);
        }

        private void TeleportToCurrentSceneDefaultPoint()
        {
            CharacterMainControl player;
            if (!TryGetMainCharacter(out player))
            {
                SetF3DebugCheatStatus(L10n.T("未找到玩家，无法传送", "Player not found. Cannot teleport"), true);
                return;
            }

            Vector3 targetPosition = GetCurrentSceneDefaultPosition();
            try
            {
                player.SetPosition(targetPosition);
            }
            catch
            {
                player.transform.position = targetPosition;
            }

            SetF3DebugCheatStatus(L10n.T("已传送到当前场景默认点", "Teleported to the current scene default point"), false);
        }

        private async void TeleportToBossRushStartPointFromF3()
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            bool alreadyInBossRushScene = currentSceneName == BossRushArenaSceneName || currentSceneName == BossRushArenaSceneID;
            if (!alreadyInBossRushScene)
            {
                if (SceneLoader.Instance == null)
                {
                    SetF3DebugCheatStatus(L10n.T("SceneLoader 未就绪，无法前往 BossRush 起始点", "SceneLoader not ready. Cannot go to the BossRush start point"), true);
                    return;
                }

                try
                {
                    HideF3DebugCheatMenu();
                    ShowMessage(L10n.T("正在前往 BossRush 起始点...", "Traveling to the BossRush start point..."));
                    await SceneLoader.Instance.LoadScene(
                        BossRushArenaSceneID,
                        null,
                        false,
                        false,
                        true,
                        false,
                        default(MultiSceneLocation),
                        true,
                        false
                    );
                    ShowMessage(L10n.T("已进入 BossRush 场地", "Entered the BossRush arena"));
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] 前往 BossRush 起始点失败: " + e.Message + "\n" + e.StackTrace);
                    SetF3DebugCheatStatus(L10n.T("前往 BossRush 起始点失败", "Failed to go to the BossRush start point"), true);
                }
                return;
            }

            CharacterMainControl player;
            if (!TryGetMainCharacter(out player))
            {
                SetF3DebugCheatStatus(L10n.T("未找到玩家，无法传送到 BossRush 起始点", "Player not found. Cannot teleport to the BossRush start point"), true);
                return;
            }

            Vector3 targetPosition = GetDefaultPositionForScene(BossRushArenaSceneName);
            if (targetPosition == Vector3.zero)
            {
                targetPosition = GetCurrentSceneDefaultPosition();
            }
            try
            {
                player.SetPosition(targetPosition);
            }
            catch
            {
                player.transform.position = targetPosition;
            }

            SetF3DebugCheatStatus(L10n.T("已传送到 BossRush 起始点", "Teleported to the BossRush start point"), false);
        }

        private async void TeleportPlayerHomeToBaseScene()
        {
            if (SceneLoader.Instance == null)
            {
                SetF3DebugCheatStatus(L10n.T("SceneLoader 未就绪，无法回基地", "SceneLoader not ready. Cannot return home"), true);
                return;
            }

            try
            {
                HideF3DebugCheatMenu();
                ShowMessage(L10n.T("正在返回基地...", "Returning to base..."));
                await SceneLoader.Instance.LoadScene(
                    BaseSceneName,
                    null,
                    false,
                    false,
                    true,
                    false,
                    default(MultiSceneLocation),
                    true,
                    false
                );
                ShowMessage(L10n.T("已返回基地", "Returned to base"));
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 回基地失败: " + e.Message + "\n" + e.StackTrace);
                SetF3DebugCheatStatus(L10n.T("返回基地失败", "Failed to return to base"), true);
            }
        }

        private void SpawnItemFromF3Inputs()
        {
            int itemId;
            if (!TryReadPositiveInt(f3ItemIdInputField, out itemId))
            {
                SetF3DebugCheatStatus(L10n.T("请输入有效的物品 ID", "Please enter a valid item ID"), true);
                return;
            }

            int count;
            if (!TryReadPositiveInt(f3ItemCountInputField, out count))
            {
                count = 1;
            }

            int successCount = 0;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Item item = ItemAssetsCollection.InstantiateSync(itemId);
                    if (item == null)
                    {
                        break;
                    }

                    ItemUtilities.SendToPlayer(item);
                    successCount++;
                }

                if (successCount <= 0)
                {
                    SetF3DebugCheatStatus(L10n.T("物品创建失败或 ID 不存在", "Item spawn failed or ID does not exist"), true);
                    return;
                }

                SetF3DebugCheatStatus(L10n.T("已发放物品 x", "Spawned item x") + successCount, false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 发放物品失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("发放物品失败", "Failed to spawn item"), true);
            }
        }

        private void SpawnQuickTestItem(int itemId, int count, string successMessage)
        {
            if (itemId <= 0 || count <= 0)
            {
                SetF3DebugCheatStatus(L10n.T("快捷发物品失败：参数无效", "Quick spawn failed: invalid parameters"), true);
                return;
            }

            int successCount = 0;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Item item = ItemAssetsCollection.InstantiateSync(itemId);
                    if (item == null)
                    {
                        break;
                    }

                    ItemUtilities.SendToPlayer(item);
                    successCount++;
                }

                if (successCount <= 0)
                {
                    SetF3DebugCheatStatus(L10n.T("快捷发物品失败", "Quick spawn failed"), true);
                    return;
                }

                SetF3DebugCheatStatus(successMessage + " x" + successCount, false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 快捷发物品失败: typeId=" + itemId + ", error=" + e.Message);
                SetF3DebugCheatStatus(L10n.T("快捷发物品失败", "Quick spawn failed"), true);
            }
        }

        private bool TryReadPositiveInt(InputField field, out int value)
        {
            value = 0;
            if (field == null || string.IsNullOrWhiteSpace(field.text))
            {
                return false;
            }

            return int.TryParse(field.text.Trim(), out value) && value > 0;
        }

        private void AddMoneyFromInputField()
        {
            if (f3MoneyInputField == null)
            {
                SetF3DebugCheatStatus(L10n.T("金额输入框未就绪", "Money input field not ready"), true);
                return;
            }

            long amount;
            if (!long.TryParse(f3MoneyInputField.text.Trim(), out amount) || amount <= 0)
            {
                SetF3DebugCheatStatus(L10n.T("请输入有效金额", "Please enter a valid amount"), true);
                return;
            }

            AddMoneyAndReport(amount);
        }

        private void AddMoneyAndReport(long amount)
        {
            try
            {
                if (!EconomyManager.Add(amount))
                {
                    SetF3DebugCheatStatus(L10n.T("加钱失败", "Failed to add money"), true);
                    return;
                }

                SetF3DebugCheatStatus(L10n.T("已增加金钱: ", "Added money: ") + amount.ToString("N0", CultureInfo.InvariantCulture), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 加钱失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("加钱失败", "Failed to add money"), true);
            }
        }

        private void ClearWishRewardCooldownOnly()
        {
            try
            {
                WishFountainService.ClearWishRewardCooldownForDevMode();
                SetF3DebugCheatStatus(L10n.T("已清除星愿奖励冷却", "Wish reward cooldown cleared"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 清除星愿奖励冷却失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("清除奖励冷却失败", "Failed to clear reward cooldown"), true);
            }
        }

        private void ClearWishSendCooldownOnly()
        {
            try
            {
                WishFountainService.ClearSendCooldownForDevMode();
                SetF3DebugCheatStatus(L10n.T("已清除星愿发送冷却", "Wish send cooldown cleared"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 清除星愿发送冷却失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("清除发送冷却失败", "Failed to clear send cooldown"), true);
            }
        }

        private void ClearAllWishDevCooldowns()
        {
            try
            {
                WishFountainService.ClearWishRewardCooldownForDevMode();
                WishFountainService.ClearSendCooldownForDevMode();
                SetF3DebugCheatStatus(L10n.T("已清除星愿奖励与发送冷却", "Wish reward and send cooldowns cleared"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 清除星愿冷却失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("清除星愿冷却失败", "Failed to clear Wish Fountain cooldowns"), true);
            }
        }

        private void OpenInventoryInspectorFromF3()
        {
            try
            {
                InventoryInspector inspector = GetComponent<InventoryInspector>();
                if (inspector == null)
                {
                    inspector = gameObject.AddComponent<InventoryInspector>();
                }

                HideF3DebugCheatMenu();
                inspector.ShowAndRefresh();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 打开 InventoryInspector 失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("打开背包检查器失败", "Failed to open inventory inspector"), true);
            }
        }

        private void ForceKillAllEnemiesFromF3()
        {
            try
            {
                ForceKillAllEnemies();
                SetF3DebugCheatStatus(L10n.T("已执行强制清场", "Force kill executed"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 强制清场失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("强制清场失败", "Failed to force kill enemies"), true);
            }
        }

        private void TriggerBossRushVictoryFromF3()
        {
            if (!IsActive)
            {
                SetF3DebugCheatStatus(L10n.T("当前不在 BossRush 流程中", "BossRush is not active right now"), true);
                return;
            }

            try
            {
                ForceKillAllEnemies();
            }
            catch { }

            try
            {
                OnAllEnemiesDefeated();
                SetF3DebugCheatStatus(L10n.T("已触发通关流程", "Victory flow triggered"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 触发通关失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("触发通关失败", "Failed to trigger victory"), true);
            }
        }

        private void GrantTicketAndOpenMapSelectionFromF3()
        {
            try
            {
                int ticketTypeId = bossRushTicketTypeId > 0 ? bossRushTicketTypeId : BossRushItemIds.BossRushTicket;
                Item ticket = ItemAssetsCollection.InstantiateSync(ticketTypeId);
                if (ticket != null)
                {
                    ItemUtilities.SendToPlayerCharacterInventory(ticket, false);
                }

                HideF3DebugCheatMenu();
                BossRushMapSelectionHelper.ShowBossRushMapSelection();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 发船票并打开地图失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("打开地图失败", "Failed to open map selection"), true);
            }
        }

        private void GrantZombieInvitationAndOpenMapSelectionFromF3()
        {
            try
            {
                string failureReason;
                if (!CanStartZombieModeMapSelectionPhase1(out failureReason))
                {
                    SetF3DebugCheatStatus(string.IsNullOrEmpty(failureReason) ? L10n.T("当前无法开始尸潮模式", "Cannot start Zombie Mode now") : failureReason, true);
                    return;
                }

                ZombieTideInvitationConfig.EnsureRuntimeFallbackRegistrationShell();
                Item invitation = ItemAssetsCollection.InstantiateSync(BossRushItemIds.ZombieTideInvitation);
                if (invitation == null)
                {
                    SetF3DebugCheatStatus(L10n.T("尸潮邀请函创建失败", "Failed to create Zombie Tide Invitation"), true);
                    return;
                }

                ItemUtilities.SendToPlayerCharacterInventory(invitation, false);
                if (!ZombieModeMapSelectionHelper.ShowZombieModeMapSelection(out failureReason))
                {
                    SetF3DebugCheatStatus(string.IsNullOrEmpty(failureReason) ? L10n.T("打开尸潮地图失败", "Failed to open Zombie Mode map") : failureReason, true);
                    return;
                }

                HideF3DebugCheatMenu();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 打开尸潮地图失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("打开尸潮地图失败", "Failed to open Zombie Mode map"), true);
            }
        }

        private void TriggerZombieModeExtractionFromF3()
        {
            try
            {
                if (!IsZombieModeActive)
                {
                    SetF3DebugCheatStatus(L10n.T("尸潮模式未激活", "Zombie Mode is not active"), true);
                    return;
                }

                if (TryUseZombieModeBeacon())
                {
                    SetF3DebugCheatStatus(L10n.T("已触发尸潮撤离", "Zombie extraction triggered"), false);
                }
                else
                {
                    SetF3DebugCheatStatus(L10n.T("当前无法触发尸潮撤离", "Cannot trigger Zombie extraction now"), true);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 触发尸潮撤离失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("触发尸潮撤离失败", "Failed to trigger Zombie extraction"), true);
            }
        }

        private void ResetZombieModeFromF3()
        {
            try
            {
                DebugResetZombieModeShell();
                SetF3DebugCheatStatus(L10n.T("已重置尸潮模式", "Zombie Mode reset"), false);
                RefreshF3DebugCheatSummary();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 重置尸潮模式失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("重置尸潮模式失败", "Failed to reset Zombie Mode"), true);
            }
        }

        private void TogglePlacementModeFromF3()
        {
            try
            {
                TogglePlacementMode();
                SetF3DebugCheatStatus(L10n.T("已切换放置模式", "Placement mode toggled"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 切换放置模式失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("切换放置模式失败", "Failed to toggle placement mode"), true);
            }
        }

        private void ClearAchievementsFromF3()
        {
            try
            {
                BossRushAchievementManager.DebugResetAll();
                AchievementEntryUI.ClearIconCache();
                SteamAchievementPopup.ClearIconCache();

                if (AchievementView.Instance != null && AchievementView.Instance.IsOpen)
                {
                    AchievementView.Instance.RefreshAll();
                }

                SetF3DebugCheatStatus(L10n.T("已清空所有成就数据", "All achievement data cleared"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 清空成就失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("清空成就失败", "Failed to clear achievements"), true);
            }
        }

        private void DumpNearbyObjectsFromF3()
        {
            try
            {
                CharacterMainControl player;
                if (!TryGetMainCharacter(out player))
                {
                    SetF3DebugCheatStatus(L10n.T("未找到玩家，无法输出对象信息", "Player not found. Cannot dump objects"), true);
                    return;
                }

                Vector3 playerPos = player.transform.position;
                string sceneName = SceneManager.GetActiveScene().name;
                if (sceneName.Contains("Base_Scene"))
                {
                    LogNearbyBuildingInfo(playerPos, 15f);
                }
                else
                {
                    LogNearbyGameObjects(playerPos, 10f, 30);
                }

                SetF3DebugCheatStatus(L10n.T("已输出附近对象信息", "Nearby object info dumped"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 输出附近对象失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("输出附近对象失败", "Failed to dump nearby objects"), true);
            }
        }

        private void DumpNearestInteractableFromF3()
        {
            try
            {
                CharacterMainControl main;
                if (!TryGetMainCharacter(out main))
                {
                    SetF3DebugCheatStatus(L10n.T("未找到玩家，无法输出交互点", "Player not found. Cannot dump interactables"), true);
                    return;
                }

                Vector3 playerPos = main.transform.position;
                InteractableBase[] allInteractables = UnityEngine.Object.FindObjectsOfType<InteractableBase>(true);
                InteractableBase nearest = null;
                float bestDistSq = float.MaxValue;

                if (allInteractables != null)
                {
                    for (int i = 0; i < allInteractables.Length; i++)
                    {
                        InteractableBase it = allInteractables[i];
                        if (it == null || it.gameObject == null)
                        {
                            continue;
                        }

                        float distSq = (it.transform.position - playerPos).sqrMagnitude;
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            nearest = it;
                        }
                    }
                }

                if (nearest != null)
                {
                    float dist = Mathf.Sqrt(bestDistSq);
                    string sceneName = SceneManager.GetActiveScene().name;
                    string name = nearest.gameObject.name;
                    string interactName = string.Empty;
                    try { interactName = nearest.InteractName; } catch { }
                    int groupCount = 0;
                    try
                    {
                        var list = nearest.GetInteractableList();
                        groupCount = list != null ? list.Count : 0;
                    }
                    catch { }

                    DevLog("[BossRush] F3 场景调试：当前场景=" + sceneName +
                           ", 玩家位置=" + playerPos +
                           ", 最近交互点 name=" + name +
                           ", InteractName=" + interactName +
                           ", 位置=" + nearest.transform.position +
                           ", 距离=" + dist +
                           ", 组内成员数量=" + groupCount);
                    SetF3DebugCheatStatus(L10n.T("已输出最近交互点信息", "Nearest interactable info dumped"), false);
                }
                else
                {
                    SetF3DebugCheatStatus(L10n.T("当前场景未找到交互点", "No interactables found in the current scene"), true);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 输出最近交互点失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("输出最近交互点失败", "Failed to dump nearest interactable"), true);
            }
        }

        private void DumpSceneCharactersFromF3()
        {
            try
            {
                CharacterMainControl main;
                if (!TryGetMainCharacter(out main))
                {
                    SetF3DebugCheatStatus(L10n.T("未找到玩家，无法输出角色信息", "Player not found. Cannot dump characters"), true);
                    return;
                }

                Vector3 playerPos = main.transform.position;
                CharacterMainControl[] characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                if (characters == null || characters.Length == 0)
                {
                    SetF3DebugCheatStatus(L10n.T("当前场景未找到任何角色", "No characters found in the current scene"), true);
                    return;
                }

                DevLog("[BossRush] F3 场景调试：玩家位置=" + playerPos + "，开始列出除玩家外的所有角色");
                for (int i = 0; i < characters.Length; i++)
                {
                    CharacterMainControl c = characters[i];
                    if (c == null)
                    {
                        continue;
                    }

                    bool isMain = false;
                    try
                    {
                        if (c == main)
                        {
                            isMain = true;
                        }
                        else
                        {
                            isMain = CharacterMainControlExtensions.IsMainCharacter(c);
                        }
                    }
                    catch { }

                    if (isMain)
                    {
                        continue;
                    }

                    Vector3 pos = c.transform.position;
                    float dist = (pos - playerPos).magnitude;
                    string presetKey = string.Empty;
                    Teams team = Teams.scav;
                    try
                    {
                        if (c.characterPreset != null)
                        {
                            presetKey = c.characterPreset.nameKey;
                            team = c.characterPreset.team;
                        }
                    }
                    catch { }

                    float maxHealth = -1f;
                    try
                    {
                        if (c.Health != null)
                        {
                            maxHealth = c.Health.MaxHealth;
                        }
                    }
                    catch { }

                    DevLog("[BossRush] F3 角色：goName=" + c.gameObject.name +
                           ", presetKey=" + presetKey +
                           ", team=" + team +
                           ", MaxHP=" + maxHealth +
                           ", pos=" + pos +
                           ", dist=" + dist.ToString("F1", CultureInfo.InvariantCulture));
                }

                SetF3DebugCheatStatus(L10n.T("已输出场景角色信息", "Scene character info dumped"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 输出角色信息失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("输出角色信息失败", "Failed to dump scene characters"), true);
            }
        }
    }
}
