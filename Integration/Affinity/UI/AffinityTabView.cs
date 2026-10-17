// ============================================================================
// AffinityTabView.cs - 好感页签（官方 Tab 页签栏里的一页）
// ============================================================================
// 为什么是官方 View 而不是自建画布：
//   好感页挂在官方 GameplayUICanvas/Tabs/ViewArea 下，与背包、角色、任务、地图、笔记等页并列。
//   官方 View.Awake 用 parent.parent 找到页签栏 ViewTabs，打开时自动显示页签栏、关掉上一页，
//   ESC 与焦点互斥也走官方 View 栈；页签按钮的点亮由 ViewTabDisplayEntry 按类型名 "AffinityTabView" 判。
//   画布与缩放都是官方页的（参考分辨率 2560×1440，按宽度缩放），这里不建 Canvas、不碰层级。
//
// 画风（owner 2026-10-17「符合原版画风」）：
//   首选：克隆官方「笔记」页的骨架（毛玻璃框、左列表、右详情、官方列表行，见 AffinityTabOfficialSkin.cs），
//   只往里填字、心和进度条；配色取实机层级导出里的官方值（白字 / #FFFFFF80 次要字 / #F85566 红 / #99E0FF 进度）。
//   退路：官方笔记页不在或结构变了时，用同一套官方配色自绘一个「列表 + 详情」页（BuildFallbackLayout）。
//   心形用原版白色扁平「Heart」贴图染色，找不到时退回 Mod 自带的心。
//
// 页型（docs/architecture/UI制作共识.md 第 2 节）：B「列表 + 详情」，只看不操作，没有按钮。
//   列表行：名字 + 十颗心；今天还没聊过的打过交道的人亮官方红点。选中行亮官方选中框（退路里是描边换色）。
//   详情：名字、心、等级进度、今天聊过 / 送过礼没有、婚姻、各级好感奖励（含剧情节点）。
//   数据组装在 AffinityTabData.cs；每次打开重建一遍（语言在取用时解析）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossRush
{
    public class AffinityTabView : View, IPointerClickHandler
    {
        internal const string ObjectName = "BossRush_AffinityTabView";

        #region 官方配色（Dev 探针 OfficialViewTabsProbe 2026-10-17 实机导出）

        private static readonly Color OfficialTitle = new Color(0.973f, 0.949f, 0.918f, 1f);      // #F8F2EA 页名
        private static readonly Color OfficialText = Color.white;
        private static readonly Color OfficialTextSoft = new Color(1f, 1f, 1f, 0.5f);              // #FFFFFF80 次要字
        private static readonly Color OfficialSelect = new Color(0.380f, 0.792f, 0.976f, 1f);      // #61CAF9 选中
        private static readonly Color OfficialRed = new Color(0.973f, 0.333f, 0.400f, 1f);         // #F85566
        private static readonly Color OfficialProgress = new Color(0.600f, 0.878f, 1f, 1f);        // #99E0FF
        private static readonly Color OfficialProgressFull = new Color(0.800f, 0.929f, 0.596f, 1f); // #CCED98
        private static readonly Color OfficialTrack = new Color(1f, 1f, 1f, 0.10f);                // #FFFFFF1A
        private static readonly Color OfficialRow = new Color(0f, 0f, 0f, 0.196f);                 // #00000032
        private static readonly Color OfficialPanel = new Color(0f, 0f, 0f, 0.588f);               // #00000096
        private static readonly Color HeartEmpty = new Color(1f, 1f, 1f, 0.15f);

        #endregion

        #region 布局常量（参考分辨率 2560 宽）

        // 2026-10-17 owner「字体和 UI 整体再大一点」：整体约 1.3 倍，框与两栏的尺寸在 AffinityTabOfficialSkin
        private const float RowHeartSize = 26f;
        private const float RowHeartGap = 4f;
        private const float RowHeartInset = 24f;
        private const float RowNameInset = 24f;
        private const float DetailHeartSize = 44f;
        private const float DetailHeartGap = 8f;
        private const float LevelSize = 30f;
        private const float ProgressWidth = 700f;
        private const float ProgressHeight = 12f;

        // 退路自绘版：与官方骨架同一套尺寸（框 2000 宽、左栏 760，页名压在框顶上方）
        private const float FallbackFrameWidth = AffinityTabOfficialSkin.FrameWidth;
        private const float FallbackListWidth = AffinityTabOfficialSkin.ListWidth;
        private const float FallbackRowHeight = AffinityTabOfficialSkin.RowHeight;
        private const float FallbackRowGap = 8f;
        private const float FallbackPadding = 24f;
        private const float FallbackTitleSize = AffinityTabOfficialSkin.PageTitleSize;
        private const float FallbackNameSize = AffinityTabOfficialSkin.RowNameSize;
        private const float FallbackDetailNameSize = AffinityTabOfficialSkin.DetailTitleSize;
        private const float FallbackBodySize = AffinityTabOfficialSkin.DetailBodySize;

        #endregion

        #region 状态

        private FadeGroup fadeGroup;
        private AffinityTabSkeleton skeleton;

        private TextMeshProUGUI pageTitle;
        private TextMeshProUGUI countText;
        private ScrollRect listScroll;
        private RectTransform listContent;
        private GameObject listPlaceholder;
        private TextMeshProUGUI detailName;
        private RectTransform detailHeader;
        private RectTransform detailHearts;
        private TextMeshProUGUI detailLevel;
        private RectTransform progressTrack;
        private Image progressFill;
        private TextMeshProUGUI detailBody;
        private ScrollRect detailScroll;

        private readonly List<AffinityTabEntry> entries = new List<AffinityTabEntry>();
        /// <summary>每行的选中表现：官方行是 highlightIndicator，退路行是描边。</summary>
        private readonly List<GameObject> rowHighlights = new List<GameObject>();
        private readonly List<Image> rowStrokes = new List<Image>();
        private string selectedNpcId;

        #endregion

        #region 运行时装配

        /// <summary>挂到官方页面容器 ViewArea 下（与背包等页并列）；建好后是关着的。</summary>
        internal static AffinityTabView CreateUnder(Transform viewArea)
        {
            if (viewArea == null) return null;

            GameObject root = new GameObject(ObjectName,
                typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(FadeGroup), typeof(CanvasGroupFade));
            root.SetActive(false);
            root.transform.SetParent(viewArea, false);

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // 根节点透明但吃点击：挡住身后的世界，点空白处关页（官方任务 / 角色页的 CloseViewOnPointerClick 同一手感）
            Image hit = root.GetComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            FadeGroup fade = root.GetComponent<FadeGroup>();
            fade.manageGameObjectActive = true;
            ConfigureFadeGroup(root, fade);

            AffinityTabView view = root.AddComponent<AffinityTabView>();
            view.fadeGroup = fade;
            // 宿主此刻还没激活：克隆进来的官方零件不会跑 Awake / OnEnable，剥脚本是安全的
            view.skeleton = AffinityTabOfficialSkin.TryBuild(viewArea, rootRect);
            if (view.skeleton != null) view.BindOfficialLayout();
            else view.BuildFallbackLayout(rootRect);

            // 激活即 View.Awake：此时父子关系已就位，官方按 parent.parent 找到页签栏；autoClose 让它先关着
            root.SetActive(true);
            if (!view.open) fade.SkipHide();
            return view;
        }

        private void BindOfficialLayout()
        {
            pageTitle = skeleton.PageTitle;
            countText = skeleton.Count;
            listScroll = skeleton.ListScroll;
            listContent = skeleton.ListContent;
            listPlaceholder = skeleton.ListPlaceholder;
            detailName = skeleton.DetailTitle;
            detailBody = skeleton.DetailBody;
            detailScroll = skeleton.DetailScroll;
            detailHeader = skeleton.DetailHeader;
            BuildDetailHeader(detailHeader, detailBody != null ? detailBody.font : null);
        }

        /// <summary>右栏的「心 / 等级 / 进度条」块：两种版式共用。</summary>
        private void BuildDetailHeader(RectTransform header, TMP_FontAsset font)
        {
            GameObject hearts = ZombieModeUIHelper.CreateRect("Hearts", header,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -6f),
                new Vector2(10f * (DetailHeartSize + DetailHeartGap), DetailHeartSize), new Vector2(0f, 1f));
            detailHearts = hearts.GetComponent<RectTransform>();

            detailLevel = CreateLabel("Level", header, string.Empty, LevelSize, OfficialTextSoft, TextAlignmentOptions.Left, font);
            RectTransform levelRect = detailLevel.rectTransform;
            levelRect.anchorMin = new Vector2(0f, 1f);
            levelRect.anchorMax = new Vector2(1f, 1f);
            levelRect.pivot = new Vector2(0f, 1f);
            levelRect.anchoredPosition = new Vector2(0f, -6f - DetailHeartSize - 10f);
            levelRect.sizeDelta = new Vector2(0f, LineHeight(LevelSize));

            GameObject track = ZombieModeUIHelper.CreateRect("ProgressTrack", header,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, levelRect.anchoredPosition.y - LineHeight(LevelSize) - 6f),
                new Vector2(ProgressWidth, ProgressHeight), new Vector2(0f, 1f));
            progressTrack = track.GetComponent<RectTransform>();
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = OfficialTrack;
            trackImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(trackImage, 3, BossRushUISkinPart.Hairline);

            GameObject fill = ZombieModeUIHelper.CreateRect("Fill", track.transform,
                Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f));
            progressFill = fill.AddComponent<Image>();
            progressFill.color = OfficialProgress;
            progressFill.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(progressFill, 3, BossRushUISkinPart.Hairline);
        }

        /// <summary>
        /// 退路：官方笔记页不可用时自绘。尺寸照官方笔记页、配色照官方值，不套 Mod 自己的蓝灰描边皮。
        /// </summary>
        private void BuildFallbackLayout(RectTransform root)
        {
            GameObject frame = ZombieModeUIHelper.CreateRect("Frame", root,
                Vector2.zero, Vector2.one, new Vector2(0f, -20f), Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.5f, 0f);
            frameRect.anchorMax = new Vector2(0.5f, 1f);
            frameRect.offsetMin = new Vector2(-FallbackFrameWidth * 0.5f, AffinityTabOfficialSkin.FrameBottom);
            frameRect.offsetMax = new Vector2(FallbackFrameWidth * 0.5f, -AffinityTabOfficialSkin.FrameTop);
            Image frameImage = frame.AddComponent<Image>();
            frameImage.color = OfficialPanel;
            frameImage.raycastTarget = true;   // 点框里不算点空白
            BossRushUI.ApplyPanelSkin(frameImage, 16, BossRushUISkinPart.Panel);

            pageTitle = CreateLabel("Title", frameRect, string.Empty, FallbackTitleSize, OfficialText,
                TextAlignmentOptions.Bottom, null);
            RectTransform titleRect = pageTitle.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 0f);
            titleRect.anchoredPosition = new Vector2(0f, 8f);
            titleRect.sizeDelta = new Vector2(0f, LineHeight(FallbackTitleSize));

            countText = CreateLabel("Count", frameRect, string.Empty, AffinityTabOfficialSkin.CountSize, OfficialText,
                TextAlignmentOptions.Center, null);
            RectTransform countRect = countText.rectTransform;
            countRect.anchorMin = countRect.anchorMax = countRect.pivot = new Vector2(0f, 1f);
            countRect.anchoredPosition = new Vector2(FallbackPadding, -8f);
            countRect.sizeDelta = new Vector2(FallbackListWidth, LineHeight(AffinityTabOfficialSkin.CountSize));

            GameObject viewport = ZombieModeUIHelper.CreateRect("ListViewport", frameRect,
                new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f));
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.offsetMin = new Vector2(FallbackPadding, FallbackPadding);
            viewportRect.offsetMax = new Vector2(FallbackPadding + FallbackListWidth,
                -8f - LineHeight(AffinityTabOfficialSkin.CountSize) - 8f);
            viewport.AddComponent<RectMask2D>();
            listScroll = viewport.AddComponent<ScrollRect>();
            GameObject list = ZombieModeUIHelper.CreateRect("ListContent", viewport.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, FallbackRowHeight), new Vector2(0.5f, 1f));
            listContent = list.GetComponent<RectTransform>();
            listScroll.content = listContent;
            listScroll.viewport = viewportRect;
            BossRushUI.ConfigureScrollRect(listScroll);

            GameObject detail = ZombieModeUIHelper.CreateRect("Detail", frameRect,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform detailRect = detail.GetComponent<RectTransform>();
            detailRect.offsetMin = new Vector2(FallbackPadding * 2f + FallbackListWidth, FallbackPadding);
            detailRect.offsetMax = new Vector2(-FallbackPadding, -FallbackPadding);
            Image detailImage = detail.AddComponent<Image>();
            detailImage.color = OfficialRow;
            detailImage.raycastTarget = true;
            BossRushUI.ApplyPanelSkin(detailImage, 16, BossRushUISkinPart.Card);

            const float inner = 32f;
            detailName = CreateLabel("Name", detailRect, string.Empty, FallbackDetailNameSize, OfficialText,
                TextAlignmentOptions.Left, null);
            RectTransform nameRect = detailName.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = new Vector2(inner, -inner);
            nameRect.sizeDelta = new Vector2(-inner * 2f, LineHeight(FallbackDetailNameSize));

            GameObject header = ZombieModeUIHelper.CreateRect("AffinityHeader", detailRect,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(inner, -inner - LineHeight(FallbackDetailNameSize) - 4f),
                new Vector2(-inner * 2f, AffinityTabOfficialSkin.DetailHeaderHeight), new Vector2(0f, 1f));
            detailHeader = header.GetComponent<RectTransform>();
            BuildDetailHeader(detailHeader, null);

            detailBody = CreateLabel("Body", detailRect, string.Empty, FallbackBodySize, OfficialText,
                TextAlignmentOptions.TopLeft, null);
            RectTransform bodyRect = detailBody.rectTransform;
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.pivot = new Vector2(0f, 1f);
            bodyRect.offsetMin = new Vector2(inner, inner);
            bodyRect.offsetMax = new Vector2(-inner,
                -inner - LineHeight(FallbackDetailNameSize) - 4f - AffinityTabOfficialSkin.DetailHeaderHeight - 8f);
            detailBody.enableWordWrapping = true;
            detailBody.lineSpacing = 10f;
        }

        #endregion

        #region 打开 / 刷新

        /// <summary>页签按钮的点击入口。</summary>
        internal void OpenFromTab()
        {
            if (open) return;
            Open(null);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            Rebuild();
            if (fadeGroup != null) fadeGroup.Show();
        }

        protected override void OnClose()
        {
            base.OnClose();
            if (fadeGroup != null) fadeGroup.Hide();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // 只认直接点在根节点（空白处）上的点击；点到框、行、详情、滚动区都不关
            if (eventData == null || eventData.pointerPressRaycast.gameObject != gameObject) return;
            if (open) Close();
        }

        private void Rebuild()
        {
            try
            {
                entries.Clear();
                entries.AddRange(AffinityTabData.Build());

                if (pageTitle != null) pageTitle.text = L10n.T("好感", "Relations");
                if (countText != null)
                {
                    int met = 0;
                    for (int i = 0; i < entries.Count; i++) if (entries[i].Met) met++;
                    countText.text = L10n.T("已结识 ", "Met ") + met + " / " + entries.Count;
                }

                RebuildRows();
                if (listPlaceholder != null) listPlaceholder.SetActive(entries.Count == 0);

                int index = IndexOf(selectedNpcId);
                Select(index >= 0 ? index : 0);
                if (listScroll != null) listScroll.verticalNormalizedPosition = 1f;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffinityTab] [WARNING] 好感页刷新失败: " + e.Message);
            }
        }

        private void RebuildRows()
        {
            rowHighlights.Clear();
            rowStrokes.Clear();
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                // 立即销毁：官方列表内容带竖排布局，延迟销毁会让新旧两批行在同一帧里一起排版
                DestroyImmediate(listContent.GetChild(i).gameObject);
            }

            if (skeleton == null)
            {
                float height = Mathf.Max(FallbackRowHeight, entries.Count * (FallbackRowHeight + FallbackRowGap) - FallbackRowGap);
                listContent.sizeDelta = new Vector2(0f, height);
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (skeleton != null) CreateOfficialRow(entries[i], i);
                else CreateFallbackRow(entries[i], i);
            }

            if (skeleton != null) LayoutRebuilder.ForceRebuildLayoutImmediate(listContent);
        }

        private void CreateOfficialRow(AffinityTabEntry entry, int index)
        {
            GameObject row = Instantiate(skeleton.RowTemplate, listContent, false);
            row.name = "Row_" + entry.NpcId;
            row.SetActive(true);
            Transform rowTransform = row.transform;

            Transform titleT = AffinityTabOfficialSkin.FindByPath(rowTransform, skeleton.RowTitlePath);
            TextMeshProUGUI title = titleT != null ? titleT.GetComponent<TextMeshProUGUI>() : null;
            float heartsWidth = 10f * RowHeartSize + 9f * RowHeartGap;
            // 官方行高 64 由行上的 LayoutElement 定（列表内容是竖排布局），放大到 RowHeight
            LayoutElement rowLayout = row.GetComponent<LayoutElement>();
            if (rowLayout == null) rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = AffinityTabOfficialSkin.RowHeight;
            rowLayout.preferredHeight = AffinityTabOfficialSkin.RowHeight;
            if (title != null)
            {
                title.text = entry.Name;
                title.color = entry.Met ? OfficialText : OfficialTextSoft;
                title.fontSize = AffinityTabOfficialSkin.RowNameSize;
                // 序号列已隐藏：名字从行左边起、让出右侧的心
                RectTransform tr = title.rectTransform;
                tr.anchorMin = new Vector2(0f, 0f);
                tr.anchorMax = new Vector2(1f, 1f);
                tr.pivot = new Vector2(0.5f, 0.5f);
                tr.offsetMin = new Vector2(RowNameInset, 0f);
                tr.offsetMax = new Vector2(-(heartsWidth + RowHeartInset + 12f), 0f);
            }
            SetActiveByPath(rowTransform, skeleton.RowIndexPath, false);
            // 官方红点 = 「有新东西」：打过交道、今天还没聊过的人亮一颗
            SetActiveByPath(rowTransform, skeleton.RowDotPath, entry.Met && !entry.ChattedToday);

            Transform highlight = AffinityTabOfficialSkin.FindByPath(rowTransform, skeleton.RowHighlightPath);
            rowHighlights.Add(highlight != null ? highlight.gameObject : null);

            GameObject hearts = ZombieModeUIHelper.CreateRect("Hearts", rowTransform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-RowHeartInset, 0f),
                new Vector2(heartsWidth, RowHeartSize), new Vector2(1f, 0.5f));
            hearts.AddComponent<LayoutElement>().ignoreLayout = true;
            FillHearts(hearts.GetComponent<RectTransform>(), entry, RowHeartSize, RowHeartGap);

            AttachClick(row, index);
        }

        private void CreateFallbackRow(AffinityTabEntry entry, int index)
        {
            GameObject row = ZombieModeUIHelper.CreateRect("Row_" + entry.NpcId, listContent,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -index * (FallbackRowHeight + FallbackRowGap)),
                new Vector2(0f, FallbackRowHeight), new Vector2(0.5f, 1f));
            Image bg = row.AddComponent<Image>();
            bg.color = OfficialRow;
            bg.raycastTarget = true;
            BossRushUI.ApplyPanelSkin(bg, 8, BossRushUISkinPart.Card);
            Image stroke = BossRushUI.ApplyPanelStroke(bg, 8, BossRushUISkinPart.Card, Color.clear);
            rowStrokes.Add(stroke);

            float heartsWidth = 10f * RowHeartSize + 9f * RowHeartGap;
            TextMeshProUGUI name = CreateLabel("Name", row.transform, entry.Name, FallbackNameSize,
                entry.Met ? OfficialText : OfficialTextSoft, TextAlignmentOptions.Left, null);
            RectTransform nameRect = name.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(20f, 0f);
            nameRect.offsetMax = new Vector2(-heartsWidth - RowHeartInset - 8f, 0f);

            GameObject hearts = ZombieModeUIHelper.CreateRect("Hearts", row.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-RowHeartInset, 0f),
                new Vector2(heartsWidth, RowHeartSize), new Vector2(1f, 0.5f));
            FillHearts(hearts.GetComponent<RectTransform>(), entry, RowHeartSize, RowHeartGap);

            AttachClick(row, index);
        }

        private void AttachClick(GameObject row, int index)
        {
            AffinityTabRowClick click = row.AddComponent<AffinityTabRowClick>();
            int captured = index;
            click.Clicked = () => Select(captured);
        }

        private void Select(int index)
        {
            AffinityTabEntry entry = index >= 0 && index < entries.Count ? entries[index] : null;
            selectedNpcId = entry != null ? entry.NpcId : null;
            for (int i = 0; i < rowHighlights.Count; i++)
            {
                if (rowHighlights[i] != null) rowHighlights[i].SetActive(i == index);
            }
            for (int i = 0; i < rowStrokes.Count; i++)
            {
                if (rowStrokes[i] != null) rowStrokes[i].color = i == index ? OfficialSelect : Color.clear;
            }
            RenderDetail(entry);
            if (detailScroll != null) detailScroll.verticalNormalizedPosition = 1f;
        }

        private void RenderDetail(AffinityTabEntry entry)
        {
            ClearChildren(detailHearts);
            if (entry == null)
            {
                detailName.text = L10n.T("还没有认识的人", "No one here yet");
                if (detailHeader != null) detailHeader.gameObject.SetActive(false);
                detailBody.text = L10n.T("去基地和天空岛找居民聊聊天，好感会记在这里。",
                    "Chat with residents at the base or on Sky Island. Your relationships show up here.");
                return;
            }

            if (detailHeader != null) detailHeader.gameObject.SetActive(true);
            detailName.text = entry.Name;
            FillHearts(detailHearts, entry, DetailHeartSize, DetailHeartGap);
            detailLevel.text = AffinityTabData.BuildLevelLine(entry);
            float progress = AffinityTabData.LevelProgress01(entry);
            RectTransform fillRect = progressFill.rectTransform;
            fillRect.anchorMax = new Vector2(progress, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            progressFill.color = entry.IsMaxLevel ? OfficialProgressFull : OfficialProgress;
            detailBody.text = BuildDetailBody(entry);
        }

        private static string BuildDetailBody(AffinityTabEntry entry)
        {
            string soft = Hex(OfficialTextSoft, true);
            string head = Hex(OfficialTitle, false);

            string text = Section(L10n.T("今天", "Today"), head);
            text += (entry.ChattedToday
                ? L10n.T("聊天：聊过了", "Chat: done")
                : L10n.T("聊天：还没聊", "Chat: not yet")) + "\n";
            text += entry.GiftedToday
                ? L10n.T("礼物：送过了", "Gift: given")
                : L10n.T("礼物：还没送", "Gift: not yet");

            if (entry.Married)
            {
                text += "\n\n" + Section(L10n.T("婚姻", "Marriage"), head);
                text += string.IsNullOrEmpty(entry.MarriageDate)
                    ? L10n.T("你们已经结婚了", "You are married")
                    : L10n.T("结婚于 ", "Married on ") + entry.MarriageDate;
            }

            if (entry.Unlocks.Count > 0)
            {
                text += "\n\n" + Section(L10n.T("好感奖励", "Affinity rewards"), head);
                for (int i = 0; i < entry.Unlocks.Count; i++)
                {
                    AffinityTabUnlock unlock = entry.Unlocks[i];
                    if (i > 0) text += "\n";
                    text += unlock.Reached
                        ? "√ Lv." + unlock.Level + "  " + unlock.Text
                        : "<color=" + soft + ">Lv." + unlock.Level + "  " + unlock.Text + "</color>";
                }
            }
            return text;
        }

        private static string Section(string title, string colorHex)
        {
            return "<b><color=" + colorHex + ">" + title + "</color></b>\n";
        }

        #endregion

        #region 装配辅助

        private static void FillHearts(RectTransform parent, AffinityTabEntry entry, float size, float gap)
        {
            if (parent == null || entry == null) return;
            Sprite official = AffinityTabOfficialSkin.GetOfficialHeart();
            Sprite heart = official != null ? official : AffinityUIManager.GetHeartSprite();
            int filled = Mathf.Clamp(entry.Level, 0, 10);
            for (int i = 0; i < 10; i++)
            {
                GameObject obj = ZombieModeUIHelper.CreateRect("Heart" + i, parent,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * (size + gap), 0f),
                    new Vector2(size, size), new Vector2(0f, 0.5f));
                Image image = obj.AddComponent<Image>();
                image.raycastTarget = false;
                if (heart != null)
                {
                    image.sprite = heart;
                    image.preserveAspect = true;
                    // 原版心是白色贴图，按状态染色；Mod 自带的心本身是红的，空心只压暗
                    image.color = i < filled
                        ? (official != null ? OfficialRed : Color.white)
                        : (official != null ? HeartEmpty : new Color(1f, 1f, 1f, 0.2f));
                }
                else
                {
                    image.color = i < filled ? OfficialRed : HeartEmpty;
                    BossRushUI.ApplyPanelSkin(image, Mathf.RoundToInt(size * 0.5f), BossRushUISkinPart.Button);
                }
            }
        }

        private static void SetActiveByPath(Transform root, string path, bool active)
        {
            Transform t = AffinityTabOfficialSkin.FindByPath(root, path);
            if (t != null && t != root) t.gameObject.SetActive(active);
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private int IndexOf(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].NpcId == npcId) return i;
            }
            return -1;
        }

        private static TextMeshProUGUI CreateLabel(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment, TMP_FontAsset font)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), Vector2.zero);
            TextMeshProUGUI label = ZombieModeUIHelper.CreateTMPText(obj, text, size, alignment, color);
            if (font != null) label.font = font;
            // 共享默认是自动缩字 + 省略号（适合按钮标签）；这里字号定死、放不下截断。
            // 单行框高一律 ≥ 字号 × 1.45 + 4（LineHeight），否则 Truncate 会把整行清空（首版页名就这样消失过）。
            label.enableAutoSizing = false;
            label.fontSize = size;
            label.overflowMode = TextOverflowModes.Truncate;
            label.enableWordWrapping = false;
            return label;
        }

        /// <summary>TMP 单行框高 ≥ 字号 × 1.45 + 4（docs/architecture/UI制作共识.md 第 9 节）。</summary>
        private static float LineHeight(float fontSize)
        {
            return Mathf.Ceil(fontSize * 1.45f + 4f);
        }

        private static string Hex(Color color, bool withAlpha)
        {
            return "#" + (withAlpha ? ColorUtility.ToHtmlStringRGBA(color) : ColorUtility.ToHtmlStringRGB(color));
        }

        private static void ConfigureFadeGroup(GameObject root, FadeGroup fadeGroup)
        {
            CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
            CanvasGroupFade canvasFade = root.GetComponent<CanvasGroupFade>();
            if (canvasFade != null && canvasGroup != null)
            {
                // 与官方页一致：0.2 秒淡入淡出、由淡入淡出管理拦截点击（实机层级导出的 CanvasGroupFade 参数）
                SetPrivateField(canvasFade, "canvasGroup", canvasGroup);
                SetPrivateField(canvasFade, "showingCurve", AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
                SetPrivateField(canvasFade, "hidingCurve", AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
                SetPrivateField(canvasFade, "fadeDuration", 0.2f);
                SetPrivateField(canvasFade, "manageBlockRaycast", true);
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
            }

            FieldInfo field = typeof(FadeGroup).GetField("fadeElements", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && canvasFade != null)
            {
                field.SetValue(fadeGroup, new List<FadeElement> { canvasFade });
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            if (target == null) return;
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) field.SetValue(target, value);
        }

        #endregion
    }

    /// <summary>好感页列表行的点击（官方行原本的 NoteIndexView_Entry 已剥掉，点击由这里接手）。</summary>
    internal sealed class AffinityTabRowClick : MonoBehaviour, IPointerClickHandler
    {
        internal Action Clicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Clicked != null) Clicked();
        }
    }
}
