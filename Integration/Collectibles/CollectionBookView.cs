// ============================================================================
// CollectionBookView.cs - 收藏册界面（列表 + 详情页型，docs/architecture/UI制作共识.md）
// ============================================================================
// 为什么自绘：官方没有「按系列排的收藏图册」界面，笔记图鉴只放文字条目。骨架照鸭皇图鉴（CodexView）：
// 共享画布与遮罩、带框面板、幽灵关闭钮、模态输入租约、ESC 走 PetNestCancelKey。
//   - 左列：12 个系列（页签式分段按钮），写已登记 / 总数；
//   - 右侧：这个系列的收集品卡片（没登记的只给剪影），点卡片在下方看描述；
//   - 底栏：左边是这一页的来处与集齐奖励，右边唯一的主操作「登记」（只在基地、存档可写时挂）。
// 打开时只读事实快照，事实或语言变了才重建，平时不分配。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class CollectionBookView : MonoBehaviour
    {
        private const float PanelWidth = 1000f;
        private const float HeaderHeight = 58f;
        private const float FooterHeight = 74f;
        private const float SideWidth = 228f;
        private const float Padding = 20f;
        private const float CardWidth = 108f;
        private const float CardHeight = 136f;
        private const float CardSpacing = 8f;
        private const int Columns = 6;
        private const float TitleFontSize = 28f;
        private const float NameFontSize = 17f;
        private const float BodyFontSize = 15f;
        private const float NoteFontSize = 14f;

        private static CollectionBookView instance;

        private CollectiblesRuntimeModule module;
        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private Image backdrop;
        private GameObject panel;
        private TextMeshProUGUI titleText, progressText, seriesTitleText, seriesRewardText, detailText, footerText;
        private RectTransform seriesList, grid;
        private Button registerButton;
        private readonly List<Button> seriesButtons = new List<Button>();
        private readonly List<GameObject> cards = new List<GameObject>();
        private ZombieModeUIHelper.ModalInputLease lease;
        private PetNestCancelKey cancelKey;
        private Coroutine closeFade;
        private float panelHeight;
        private bool built, open, destroying, chinese;
        private CollectionBookData rendered;
        private CollectibleSeries selected = CollectibleSeries.GroundZero;

        // ---------------- 静态入口 ----------------

        internal static void Toggle(CollectiblesRuntimeModule owner)
        {
            try
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("BossRush_CollectionBookView");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<CollectionBookView>();
                }
                instance.module = owner;
                if (instance.open) instance.Close();
                else instance.Open();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 收藏册界面打开失败: " + e.Message);
            }
        }

        internal static void CloseIfOpen()
        {
            if (instance != null && instance.open) instance.Close();
        }

        /// <summary>先 Close 归还模态租约再销毁，否则宿主销毁时输入会被永久锁死（同 CodexView）。</summary>
        internal static void ResetStaticCaches()
        {
            if (instance == null) return;
            try
            {
                instance.destroying = true;
                instance.Close();
                Destroy(instance.gameObject);
            }
            catch (Exception) { /* 销毁失败不得拖崩宿主清理链 */ }
            instance = null;
        }

        // ---------------- 生命周期 ----------------

        private void Awake()
        {
            try
            {
                float screenHeight = ZombieModeUIHelper.GetReferenceViewportSize().y;
                panelHeight = Mathf.Clamp(screenHeight * 0.82f, 600f, 820f);
                Build();
                built = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 收藏册界面构建失败: " + e.Message);
            }
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            destroying = true;
            ReleaseInput();
            if (instance == this) instance = null;
        }

        private void Update()
        {
            if (!open) return;
            CollectionBookData data = module != null ? module.Data : null;
            if (chinese != L10n.IsChinese || !ReferenceEquals(rendered, data)) Refresh();
        }

        private void Open()
        {
            if (open || !built) return;
            open = true;
            if (closeFade != null) { StopCoroutine(closeFade); closeFade = null; }
            IntegrationUIFeedback.ResetFade(canvasGroup);
            canvas.gameObject.SetActive(true);
            BossRushUIEntranceAnimation.Play(backdrop.gameObject, 0f, 0.15f, 0f);
            if (lease == null) lease = ZombieModeUIHelper.ClaimModalInput(gameObject, "CollectionBook");
            cancelKey = PetNestCancelKey.Attach(canvas.gameObject, Close, () => BossRushConfirmDialog.IsOpen);
            Refresh();
            BossRushUI.PlayOpenAnimation(panel);
        }

        private void Close()
        {
            bool wasOpen = open;
            open = false;
            ReleaseInput();
            if (canvas == null) return;
            if (wasOpen && !destroying && isActiveAndEnabled && canvasGroup != null && canvas.gameObject.activeSelf)
            {
                if (closeFade != null) StopCoroutine(closeFade);
                closeFade = StartCoroutine(IntegrationUIFeedback.FadeOutAndDeactivate(canvasGroup, canvas.gameObject, BossRushUIKit.CloseSeconds));
            }
            else
            {
                canvas.gameObject.SetActive(false);
                IntegrationUIFeedback.ResetFade(canvasGroup);
            }
        }

        private void ReleaseInput()
        {
            if (cancelKey != null) { cancelKey.Detach(); cancelKey = null; }
            try { if (lease != null) lease.Release(); }
            catch (Exception) { /* 输入释放失败不阻断关闭 */ }
            lease = null;
        }

        // ---------------- 构建 ----------------

        private void Build()
        {
            canvas = BossRushUI.CreateCanvasRoot("CollectionBookCanvas", BossRushUILayers.Panel, true);
            canvas.transform.SetParent(transform, false);
            canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            backdrop = BossRushUI.CreateBackdrop(canvas.transform);
            Button backdropButton = backdrop.gameObject.AddComponent<Button>();
            backdropButton.transition = Selectable.Transition.None;
            backdropButton.onClick.AddListener(Close);

            panel = ZombieModeUIHelper.CreateRect("Panel", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(PanelWidth, panelHeight), new Vector2(0.5f, 0.5f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(panelImage, 14, BossRushUISkinPart.Panel);
            Button panelButton = panel.AddComponent<Button>();
            panelButton.transition = Selectable.Transition.None;

            BuildHeader();
            BuildSeriesList();
            BuildContent();
            BuildFooter();
        }

        private void BuildHeader()
        {
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(0f, HeaderHeight), new Vector2(0.5f, 1f));
            GameObject rail = ZombieModeUIHelper.CreateRect("TitleRail", header.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(Padding, 0f), new Vector2(3f, 24f), new Vector2(0f, 0.5f));
            Image railImage = rail.AddComponent<Image>();
            railImage.color = BossRushUIColors.Accent;
            BossRushUI.ApplyPanelSkin(railImage, 2, BossRushUISkinPart.Hairline);
            railImage.raycastTarget = false;

            titleText = ZombieModeUIHelper.CreateText("Title", header.transform, string.Empty, TitleFontSize, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            titleText.fontStyle = FontStyles.Bold;
            titleText.rectTransform.offsetMin = new Vector2(Padding + 10f, 0f);
            titleText.rectTransform.offsetMax = new Vector2(-260f, 0f);

            progressText = ZombieModeUIHelper.CreateText("Progress", header.transform, string.Empty, NameFontSize, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, TextAlignmentOptions.Right, BossRushUIColors.TextSecondary);
            progressText.rectTransform.offsetMin = new Vector2(Padding, 0f);
            progressText.rectTransform.offsetMax = new Vector2(-64f, 0f);
            progressText.raycastTarget = false;

            Color ghost = new Color(BossRushUIColors.Danger.r, BossRushUIColors.Danger.g, BossRushUIColors.Danger.b, 0f);
            Button closeButton = ZombieModeUIHelper.CreateButton("CloseButton", header.transform, "×", new Vector2(1f, 0.5f),
                new Vector2(-14f, 0f), new Vector2(36f, 36f), ghost, 24f, new Vector2(36f, 36f), Close, true);
            IntegrationUIFeedback.StyleGhostCloseButton(closeButton, closeButton.GetComponentInChildren<TextMeshProUGUI>(true));

            GameObject divider = ZombieModeUIHelper.CreateSeparator("HeaderDivider", panel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -HeaderHeight), 2f, BossRushUIColors.Divider);
            divider.GetComponent<RectTransform>().sizeDelta = new Vector2(-24f, divider.GetComponent<RectTransform>().sizeDelta.y);
        }

        private void BuildSeriesList()
        {
            GameObject list = ZombieModeUIHelper.CreateRect("SeriesList", panel.transform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(Padding, (FooterHeight - HeaderHeight) * 0.5f), new Vector2(SideWidth, -(HeaderHeight + FooterHeight + 16f)), new Vector2(0f, 0.5f));
            seriesList = list.GetComponent<RectTransform>();
            float available = panelHeight - HeaderHeight - FooterHeight - 16f;
            float step = available / CollectibleCatalog.SeriesCount;
            float height = Mathf.Min(38f, step - 4f);
            for (int s = 0; s < CollectibleCatalog.SeriesCount; s++)
            {
                CollectibleSeries series = (CollectibleSeries)s;
                Button button = ZombieModeUIHelper.CreateButton("Series_" + series, seriesList, string.Empty, new Vector2(0.5f, 1f),
                    new Vector2(0f, -(step * s + height * 0.5f + 2f)), new Vector2(SideWidth, height), BossRushUIColors.SurfaceRaised,
                    BodyFontSize, new Vector2(SideWidth - 16f, height), delegate { Select(series); }, true);
                BossRushUIKit.StyleSecondaryButton(button);
                seriesButtons.Add(button);
            }
        }

        private void BuildContent()
        {
            float left = Padding * 2f + SideWidth;
            GameObject content = ZombieModeUIHelper.CreateRect("Content", panel.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform rect = content.GetComponent<RectTransform>();
            rect.offsetMin = new Vector2(left, FooterHeight + 8f);
            rect.offsetMax = new Vector2(-Padding, -(HeaderHeight + 8f));

            seriesTitleText = ZombieModeUIHelper.CreateText("SeriesTitle", content.transform, string.Empty, NameFontSize, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -16f), new Vector2(0f, 30f), TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            seriesTitleText.fontStyle = FontStyles.Bold;
            seriesTitleText.raycastTarget = false;
            seriesRewardText = ZombieModeUIHelper.CreateText("SeriesReward", content.transform, string.Empty, NoteFontSize, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -42f), new Vector2(0f, 24f), TextAlignmentOptions.Left, BossRushUIColors.TextSecondary);
            seriesRewardText.raycastTarget = false;

            GameObject gridObject = ZombieModeUIHelper.CreateRect("Grid", content.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -60f), new Vector2(0f, CardHeight * 2f + CardSpacing), new Vector2(0.5f, 1f));
            grid = gridObject.GetComponent<RectTransform>();
            GridLayoutGroup layout = gridObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(CardWidth, CardHeight);
            layout.spacing = new Vector2(CardSpacing, CardSpacing);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = Columns;

            detailText = ZombieModeUIHelper.CreateText("Detail", content.transform, string.Empty, BodyFontSize, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 56f), new Vector2(0f, 104f), TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
            detailText.enableWordWrapping = true;
            detailText.raycastTarget = false;
        }

        private void BuildFooter()
        {
            GameObject divider = ZombieModeUIHelper.CreateSeparator("FooterDivider", panel.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, FooterHeight), 2f, BossRushUIColors.Divider);
            divider.GetComponent<RectTransform>().sizeDelta = new Vector2(-24f, divider.GetComponent<RectTransform>().sizeDelta.y);

            footerText = ZombieModeUIHelper.CreateText("FooterText", panel.transform, string.Empty, NoteFontSize, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, FooterHeight * 0.5f), new Vector2(0f, FooterHeight - 12f), TextAlignmentOptions.Left, BossRushUIColors.TextSecondary);
            footerText.rectTransform.offsetMin = new Vector2(Padding, footerText.rectTransform.offsetMin.y);
            footerText.rectTransform.offsetMax = new Vector2(-(Padding + 180f), footerText.rectTransform.offsetMax.y);
            footerText.raycastTarget = false;

            registerButton = ZombieModeUIHelper.CreateButton("Register", panel.transform, string.Empty, new Vector2(1f, 0f),
                new Vector2(-(Padding + 80f), FooterHeight * 0.5f), new Vector2(160f, 44f), BossRushUIColors.AccentFill, NameFontSize,
                new Vector2(144f, 40f), OnRegister, true);
            ZombieModeUIHelper.SetButtonBaseColor(registerButton, BossRushUIColors.AccentFill);
        }

        // ---------------- 刷新 ----------------

        private void Select(CollectibleSeries series)
        {
            selected = series;
            Refresh();
        }

        private void OnRegister()
        {
            if (module == null || !module.CanRegisterNow) { Refresh(); return; }
            module.OpenRegisterContainer();
        }

        private void Refresh()
        {
            if (!built) return;
            try
            {
                CollectionBookData data = module != null ? module.Data : null;
                rendered = data;
                chinese = L10n.IsChinese;
                titleText.text = CollectiblesLocalization.BookName;
                int registered = data != null ? data.Registered.Count : 0;
                progressText.text = L10n.T("已登记 ", "Logged ") + registered + " / " + CollectibleCatalog.Count;

                for (int s = 0; s < seriesButtons.Count; s++)
                {
                    CollectibleSeries series = (CollectibleSeries)s;
                    CollectibleSeriesInfo info = CollectibleCatalog.GetSeries(series);
                    int have = data != null ? data.RegisteredInSeries(series) : 0;
                    bool done = data != null && data.PaidSeries.Contains(s);
                    SetLabel(seriesButtons[s], (info != null ? info.Name : series.ToString()) + "  " + have + "/" + CollectionRegistration.SeriesSize(series) + (done ? " ★" : string.Empty));
                    IntegrationUIFeedback.StyleSegment(seriesButtons[s], series == selected);
                }

                CollectibleSeriesInfo current = CollectibleCatalog.GetSeries(selected);
                int haveCurrent = data != null ? data.RegisteredInSeries(selected) : 0;
                seriesTitleText.text = (current != null ? current.Name : string.Empty) + "  " + haveCurrent + " / " + CollectionRegistration.SeriesSize(selected);
                seriesRewardText.text = RewardLine(selected, data);
                footerText.text = WhereLine(current);
                bool canRegister = module != null && module.CanRegisterNow;
                registerButton.gameObject.SetActive(canRegister);
                SetLabel(registerButton, L10n.T("登记", "Register"));
                if (!canRegister) footerText.text += L10n.T("\n回到基地才能登记。", "\nRegister at base.");

                RebuildCards(data);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "[WARNING] 收藏册刷新失败: " + e.Message);
            }
        }

        private static string RewardLine(CollectibleSeries series, CollectionBookData data)
        {
            CollectibleSeriesInfo info = CollectibleCatalog.GetSeries(series);
            if (info == null || !info.CountsForSet) return L10n.T("珍品不算进集齐，见到了就是缘分。", "Treasures don't count toward a full set. Finding one is luck enough.");
            CollectibleTuning.SeriesBonus bonus = CollectibleTuning.BonusOf(series);
            string reward = L10n.T("集齐：奖金 ", "Complete: ") + CollectibleTuning.SeriesCash(series).ToString("N0")
                + L10n.T("，", " cash, ") + (bonus != null ? bonus.Text : string.Empty);
            bool paid = data != null && data.PaidSeries.Contains((int)series);
            return paid ? reward + L10n.T("（已领取）", " (claimed)") : reward;
        }

        private static string WhereLine(CollectibleSeriesInfo info)
        {
            if (info == null) return string.Empty;
            if (info.Local) return L10n.T("只在", "Found only in ") + info.Name + L10n.T("的箱子里出现。", " crates.");
            if (info.CountsForSet) return L10n.T("哪张图的箱子里都可能翻到。", "Can turn up in crates on any map.");
            return L10n.T("哪张图都可能有，但极少有人见过。", "Could be anywhere, but hardly anyone has seen one.");
        }

        private void RebuildCards(CollectionBookData data)
        {
            for (int i = 0; i < cards.Count; i++) if (cards[i] != null) Destroy(cards[i]);
            cards.Clear();
            detailText.text = L10n.T("点一件看看它的来历。", "Pick one to read about it.");
            for (int i = 0; i < CollectibleCatalog.Count; i++)
            {
                CollectibleDefinition def = CollectibleCatalog.At(i);
                if (def == null || def.Series != selected) continue;
                cards.Add(CreateCard(def, data != null && data.Registered.Contains(def.TypeId)));
            }
        }

        private GameObject CreateCard(CollectibleDefinition def, bool registered)
        {
            GameObject card = ZombieModeUIHelper.CreateRect("Card_" + def.Key, grid, new Vector2(0.5f, 0.5f), new Vector2(CardWidth, CardHeight));
            Image background = card.AddComponent<Image>();
            background.color = registered ? BossRushUIColors.SurfaceRaised : BossRushUIColors.Surface;
            Image stroke = BossRushUI.ApplyFramedPanelSkin(background, 10, BossRushUISkinPart.Card);
            Button button = card.AddComponent<Button>();
            button.targetGraphic = background;
            ZombieModeUIHelper.ApplyButtonColors(button, background.color,
                Color.Lerp(background.color, BossRushUIColors.Accent, 0.22f), BossRushUIColors.Disabled);
            CollectibleDefinition captured = def;
            bool capturedRegistered = registered;
            button.onClick.AddListener(delegate { ShowDetail(captured, capturedRegistered); });

            GameObject bar = ZombieModeUIHelper.CreateRect("Rarity", card.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -6f), new Vector2(-24f, 3f), new Vector2(0.5f, 1f));
            Image barImage = bar.AddComponent<Image>();
            barImage.color = RarityColor(def.Quality);
            BossRushUI.ApplyPanelSkin(barImage, 1, BossRushUISkinPart.Hairline);
            barImage.raycastTarget = false;

            GameObject iconHolder = ZombieModeUIHelper.CreateRect("Icon", card.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), new Vector2(76f, 76f), new Vector2(0.5f, 1f));
            Sprite icon = ResolveIcon(def.TypeId);
            if (icon != null)
            {
                Image image = iconHolder.AddComponent<Image>();
                image.sprite = icon;
                image.preserveAspect = true;
                image.raycastTarget = false;
                // 没登记的只给剪影：认得出形状，看不清细节。
                image.color = registered ? Color.white : new Color(0f, 0f, 0f, 0.55f);
            }

            TextMeshProUGUI name = ZombieModeUIHelper.CreateText("Name", card.transform, CollectiblesLocalization.Name(def), NoteFontSize,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 24f), new Vector2(-10f, 40f), TextAlignmentOptions.Center,
                registered ? BossRushUIColors.TextPrimary : BossRushUIColors.TextSecondary);
            name.enableAutoSizing = true;
            name.fontSizeMin = 11f;
            name.fontSizeMax = NoteFontSize;
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.raycastTarget = false;

            // 已登记的 6–8 档挂鎏金边框（与背包格子同一套）；金框和品质色内辉已经表明档次，顶上的品质细条收起。
            if (registered && CollectibleSlotFrame.HasFrame(def.Quality))
            {
                bar.SetActive(false);
                CollectibleSlotFrame.AttachToCard(card.GetComponent<RectTransform>(), def.Quality, iconHolder.transform, stroke);
            }
            return card;
        }

        private void ShowDetail(CollectibleDefinition def, bool registered)
        {
            if (def == null) return;
            string head = CollectiblesLocalization.Name(def) + L10n.T("  ·  ", "  ·  ") + def.Quality + L10n.T(" 档  ·  价值 ", "-tier  ·  Value ")
                + def.Value.ToString("N0") + (registered ? L10n.T("  ·  已登记", "  ·  Logged") : string.Empty);
            detailText.text = head + "\n" + CollectiblesLocalization.Description(def);
        }

        private static Sprite ResolveIcon(int typeId)
        {
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                return prefab != null ? prefab.Icon : null;
            }
            catch (Exception) { return null; }
        }

        private static Color RarityColor(int quality)
        {
            if (quality >= 6) return BossRushUIColors.RarityLegendary;
            if (quality == 5) return BossRushUIColors.RarityEpic;
            if (quality == 4) return BossRushUIColors.RarityRare;
            if (quality == 3) return BossRushUIColors.RarityUncommon;
            return BossRushUIColors.RarityCommon;
        }

        private static void SetLabel(Button button, string value)
        {
            if (button == null) return;
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = value;
        }
    }
}
