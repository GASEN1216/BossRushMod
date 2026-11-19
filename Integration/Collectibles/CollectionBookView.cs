// ============================================================================
// CollectionBookView.cs - 收藏册界面（列表 + 详情页型，docs/architecture/UI制作共识.md）
// ============================================================================
// 为什么自绘：官方没有「按系列排的收藏图册」界面，笔记图鉴只放文字条目。骨架照鸭皇图鉴（CodexView）：
// 共享画布与遮罩、带框面板、幽灵关闭钮、模态输入租约、ESC 走 PetNestCancelKey。
//   - 左列：12 个系列（页签式分段按钮），写已登记 / 总数；
//   - 右侧：系列标题 + 进度条 + 集齐奖励，下面是这个系列的收集品卡片（没登记的只给剪影）；
//     打开就默认选中第一张，选中的卡片 WarningText 描边，紧贴卡片下方的详情卡写大图、档次、价值与见闻；
//   - 底栏：左边是这一页的来处，右边唯一的主操作「登记收集品」（只在基地、存档可写时挂），
//     点了打开单格登记容器（CollectionRegisterContainer，送礼同款）。
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
        private const float CloseSize = 36f;
        private const float CardWidth = 108f;
        private const float CardHeight = 136f;
        private const float CardSpacing = 8f;
        private const int Columns = 6;
        /// <summary>内容区自上而下：系列标题 0–30、进度条 34–40、奖励行 42–68、卡片网格从 76 起两行。</summary>
        private const float GridTop = 76f;
        private const float DetailGap = 12f;
        private const float DetailPadding = 14f;
        /// <summary>面板高度只取放得下两行卡片与详情卡的范围：太高时详情卡下面只剩一大片空。</summary>
        private const float MinPanelHeight = 640f;
        private const float MaxPanelHeight = 700f;
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
        private TextMeshProUGUI titleText, progressText, seriesTitleText, seriesRewardText, footerText;
        private TextMeshProUGUI detailName, detailMeta, detailText;
        private Image seriesFill, detailIcon;
        private RectTransform seriesList, grid;
        private Button registerButton;
        private readonly List<Button> seriesButtons = new List<Button>();
        private readonly List<CardView> cards = new List<CardView>();
        private CardView selectedCard;
        private ZombieModeUIHelper.ModalInputLease lease;
        private PetNestCancelKey cancelKey;
        private Coroutine closeFade;
        private float panelHeight;
        private bool built, open, destroying, chinese;
        private CollectionBookData rendered;
        private CollectibleSeries selected = CollectibleSeries.GroundZero;
        private int selectedTypeId;

        private sealed class CardView
        {
            internal GameObject Root;
            internal CollectibleDefinition Def;
            internal bool Registered;
            internal Image Stroke;
            internal Color StrokeColor;
        }

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

        /// <summary>没开着才打开（登记容器关上后回到收藏册用）；开着时什么都不做。</summary>
        internal static void Show(CollectiblesRuntimeModule owner)
        {
            if (instance != null && instance.open) return;
            Toggle(owner);
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
                panelHeight = Mathf.Clamp(screenHeight * 0.82f, MinPanelHeight, MaxPanelHeight);
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
            progressText.rectTransform.offsetMax = new Vector2(-(Padding + CloseSize + 10f), 0f);
            progressText.raycastTarget = false;

            Color ghost = new Color(BossRushUIColors.Danger.r, BossRushUIColors.Danger.g, BossRushUIColors.Danger.b, 0f);
            // CreateButton 的轴心在中心：x 是按钮中心离右沿的距离。旧值 -14 让 36 宽的按钮右半边伸出面板描边 4px，
            // 「×」压在边框上；改成右沿对齐页边距（与下面卡片网格的右边线同一条）。
            Button closeButton = ZombieModeUIHelper.CreateButton("CloseButton", header.transform, "×", new Vector2(1f, 0.5f),
                new Vector2(-(Padding + CloseSize * 0.5f), 0f), new Vector2(CloseSize, CloseSize), ghost, 24f,
                new Vector2(CloseSize, CloseSize), Close, true);
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
                new Vector2(0f, -15f), new Vector2(0f, 30f), TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            seriesTitleText.fontStyle = FontStyles.Bold;
            seriesTitleText.raycastTarget = false;

            // 系列进度条（UI 共识 §8「进度用进度条 + 数字」，画法同图鉴进度行）：凑齐换 Success。
            GameObject track = ZombieModeUIHelper.CreateRect("SeriesTrack", content.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -34f), new Vector2(0f, 6f), new Vector2(0.5f, 1f));
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = BossRushUIColors.Disabled;
            BossRushUI.ApplyPanelSkin(trackImage, 4, BossRushUISkinPart.ScrollHandle);
            trackImage.raycastTarget = false;
            GameObject fill = ZombieModeUIHelper.CreateRect("SeriesFill", track.transform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f));
            seriesFill = fill.AddComponent<Image>();
            seriesFill.color = BossRushUIColors.Accent;
            BossRushUI.ApplyPanelSkin(seriesFill, 4, BossRushUISkinPart.ScrollHandle);
            seriesFill.raycastTarget = false;

            seriesRewardText = ZombieModeUIHelper.CreateText("SeriesReward", content.transform, string.Empty, NoteFontSize, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -55f), new Vector2(0f, 26f), TextAlignmentOptions.Left, BossRushUIColors.TextSecondary);
            seriesRewardText.raycastTarget = false;

            GameObject gridObject = ZombieModeUIHelper.CreateRect("Grid", content.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -GridTop), new Vector2(0f, CardHeight * 2f + CardSpacing), new Vector2(0.5f, 1f));
            grid = gridObject.GetComponent<RectTransform>();
            GridLayoutGroup layout = gridObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(CardWidth, CardHeight);
            layout.spacing = new Vector2(CardSpacing, CardSpacing);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = Columns;

            BuildDetailCard(content.transform);
        }

        /// <summary>
        /// 详情卡紧贴卡片网格下方、铺满内容区剩下的高度（旧版是一段贴在内容区底边的字，和卡片之间空出一大块）：
        /// 左边大图（没登记是剪影），右边名字、档次与价值、见闻。
        /// </summary>
        private void BuildDetailCard(Transform parent)
        {
            float top = GridTop + CardHeight * 2f + CardSpacing + DetailGap;
            float height = panelHeight - HeaderHeight - FooterHeight - 16f - top;
            float iconSize = Mathf.Clamp(height - DetailPadding * 2f, 56f, 120f);

            GameObject card = ZombieModeUIHelper.CreateRect("Detail", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform rect = card.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(0f, -top);
            Image background = card.AddComponent<Image>();
            background.color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyFramedPanelSkin(background, 10, BossRushUISkinPart.Card);
            background.raycastTarget = false;

            GameObject icon = ZombieModeUIHelper.CreateRect("Icon", card.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(DetailPadding, -DetailPadding), new Vector2(iconSize, iconSize), new Vector2(0f, 1f));
            detailIcon = icon.AddComponent<Image>();
            detailIcon.preserveAspect = true;
            detailIcon.raycastTarget = false;
            detailIcon.enabled = false;

            float textLeft = DetailPadding * 2f + iconSize + 2f;
            detailName = ZombieModeUIHelper.CreateText("Name", card.transform, string.Empty, NameFontSize, new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            detailName.fontStyle = FontStyles.Bold;
            detailName.overflowMode = TextOverflowModes.Ellipsis;
            detailName.raycastTarget = false;
            Place(detailName.rectTransform, textLeft, DetailPadding, 30f);

            detailMeta = ZombieModeUIHelper.CreateText("Meta", card.transform, string.Empty, NoteFontSize, new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, TextAlignmentOptions.Left, BossRushUIColors.TextSecondary);
            detailMeta.raycastTarget = false;
            Place(detailMeta.rectTransform, textLeft, DetailPadding + 32f, 26f);

            detailText = ZombieModeUIHelper.CreateText("Story", card.transform, string.Empty, BodyFontSize, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, TextAlignmentOptions.TopLeft, BossRushUIColors.TextPrimary);
            detailText.rectTransform.offsetMin = new Vector2(textLeft, DetailPadding);
            detailText.rectTransform.offsetMax = new Vector2(-DetailPadding, -(DetailPadding + 66f));
            detailText.enableWordWrapping = true;
            // 矮屏（面板 640）时详情卡只剩两行的高度：自动缩字兜住英文长句，再不够才省略。
            detailText.enableAutoSizing = true;
            detailText.fontSizeMin = 12f;
            detailText.fontSizeMax = BodyFontSize;
            detailText.overflowMode = TextOverflowModes.Ellipsis;
            detailText.raycastTarget = false;
        }

        /// <summary>顶边对齐的单行字：左右撑满，距顶 <paramref name="fromTop"/>。</summary>
        private static void Place(RectTransform rect, float left, float fromTop, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -(fromTop + height));
            rect.offsetMax = new Vector2(-DetailPadding, -fromTop);
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
                int sizeCurrent = CollectionRegistration.SeriesSize(selected);
                seriesTitleText.text = (current != null ? current.Name : string.Empty) + "  " + haveCurrent + " / " + sizeCurrent;
                float ratio = sizeCurrent > 0 ? Mathf.Clamp01((float)haveCurrent / sizeCurrent) : 0f;
                seriesFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
                seriesFill.enabled = ratio > 0f;
                seriesFill.color = ratio >= 1f ? BossRushUIColors.Success : BossRushUIColors.Accent;
                seriesRewardText.text = RewardLine(selected, data);
                footerText.text = WhereLine(current);
                bool canRegister = module != null && module.CanRegisterNow;
                registerButton.gameObject.SetActive(canRegister);
                SetLabel(registerButton, L10n.T("登记收集品", "Register Items"));
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

        /// <summary>重建这一页的卡片，并保住选中（同一件还在这页就继续选它，否则默认选第一张，UI 共识 §6）。</summary>
        private void RebuildCards(CollectionBookData data)
        {
            for (int i = 0; i < cards.Count; i++) if (cards[i].Root != null) Destroy(cards[i].Root);
            cards.Clear();
            selectedCard = null;
            CardView first = null, keep = null;
            for (int i = 0; i < CollectibleCatalog.Count; i++)
            {
                CollectibleDefinition def = CollectibleCatalog.At(i);
                if (def == null || def.Series != selected) continue;
                var view = new CardView { Def = def, Registered = data != null && data.Registered.Contains(def.TypeId) };
                view.Root = CreateCard(def, view.Registered, view);
                cards.Add(view);
                if (first == null) first = view;
                if (def.TypeId == selectedTypeId) keep = view;
            }
            SelectCard(keep ?? first);
        }

        private GameObject CreateCard(CollectibleDefinition def, bool registered, CardView view)
        {
            GameObject card = ZombieModeUIHelper.CreateRect("Card_" + def.Key, grid, new Vector2(0.5f, 0.5f), new Vector2(CardWidth, CardHeight));
            Image background = card.AddComponent<Image>();
            background.color = registered ? BossRushUIColors.SurfaceRaised : BossRushUIColors.Surface;
            Image stroke = BossRushUI.ApplyFramedPanelSkin(background, 10, BossRushUISkinPart.Card);
            Button button = card.AddComponent<Button>();
            button.targetGraphic = background;
            ZombieModeUIHelper.ApplyButtonColors(button, background.color,
                Color.Lerp(background.color, BossRushUIColors.Accent, 0.22f), BossRushUIColors.Disabled);
            button.onClick.AddListener(delegate { SelectCard(view); });

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
                image.color = registered ? Color.white : Silhouette;
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
            // 记下描边的本色（金框卡片的描边已被改过），取消选中时还原。
            view.Stroke = stroke;
            view.StrokeColor = stroke != null ? stroke.color : BossRushUIColors.Stroke;
            return card;
        }

        /// <summary>选中一张卡：上一张描边还原，这一张换 WarningText 描边（UI 共识 §6），详情卡跟着换。</summary>
        private void SelectCard(CardView view)
        {
            if (selectedCard != null && selectedCard.Stroke != null) selectedCard.Stroke.color = selectedCard.StrokeColor;
            selectedCard = view;
            if (view == null || view.Def == null)
            {
                detailIcon.enabled = false;
                detailName.text = string.Empty;
                detailMeta.text = string.Empty;
                detailText.text = string.Empty;
                return;
            }
            selectedTypeId = view.Def.TypeId;
            if (view.Stroke != null) view.Stroke.color = BossRushUIColors.WarningText;
            ShowDetail(view.Def, view.Registered);
        }

        private void ShowDetail(CollectibleDefinition def, bool registered)
        {
            Sprite icon = ResolveIcon(def.TypeId);
            detailIcon.sprite = icon;
            detailIcon.enabled = icon != null;
            detailIcon.color = registered ? Color.white : Silhouette;
            detailName.text = CollectiblesLocalization.Name(def);
            detailName.color = registered ? BossRushUIColors.TextPrimary : BossRushUIColors.TextSecondary;
            string tier = "<color=#" + ColorUtility.ToHtmlStringRGB(RarityColor(def.Quality)) + ">"
                + def.Quality + L10n.T(" 档", "-tier") + "</color>";
            string status = registered
                ? "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.SuccessText) + ">" + L10n.T("已登记", "Logged") + "</color>"
                : L10n.T("未登记", "Not logged");
            detailMeta.text = tier + "  ·  " + L10n.T("价值 ", "Value ") + def.Value.ToString("N0") + "  ·  " + status;
            detailText.text = CollectiblesLocalization.Description(def);
        }

        private static readonly Color Silhouette = new Color(0f, 0f, 0f, 0.55f);

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
