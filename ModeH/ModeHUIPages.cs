using System;
using System.Collections.Generic;
using Duckov.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// 一个模态页面的内容与回调（只读快照 + owner-checked 命令）。
    /// 页面本身不读全局状态，全部由调用方组装，保证按钮绑定当前状态与 owner token。
    /// </summary>
    internal sealed class ModeHPageContent
    {
        /// <summary>页面标题（已本地化）。</summary>
        public string Title;
        /// <summary>正文（已本地化）。</summary>
        public string Body;
        /// <summary>卡片列表（五席候选、市场 offer、名人堂条目等）。</summary>
        public List<ModeHCardData> Cards = new List<ModeHCardData>();
        /// <summary>逐行拆解（赔率分量、战报时间线等）。</summary>
        public List<string> Lines = new List<string>();
        /// <summary>底部动作按钮。</summary>
        public List<ModeHActionData> Actions = new List<ModeHActionData>();
        /// <summary>是否在顶部显示真实资产风险行（入口页必须为 true）。</summary>
        public bool ShowRealStakeNotice;
        /// <summary>押品选择器是否可用；不可用时原位显示 DisabledReason。</summary>
        public bool RealStakeSelectorEnabled;
        /// <summary>押品选择器禁用原因（已本地化）。</summary>
        public string RealStakeDisabledReason;
    }

    /// <summary>一张卡片的只读数据。</summary>
    internal sealed class ModeHCardData
    {
        /// <summary>标题（选手名 / 套装名）。</summary>
        public string Title;
        /// <summary>副标题（原型 · 底色）。</summary>
        public string Subtitle;
        /// <summary>正文（怪癖/异常、招牌口令、传闻）。</summary>
        public string Body;
        /// <summary>品质（决定描边颜色）；0 表示用 accent。</summary>
        public int GameQuality;
        /// <summary>是否是异常（用 Warning/Danger token 区分于普通怪癖）。</summary>
        public bool IsAnomaly;
        /// <summary>点击回调；为 null 表示只读卡。</summary>
        public Action OnClick;
        /// <summary>点击按钮文案。</summary>
        public string ActionLabel;
    }

    /// <summary>一个底部动作按钮。</summary>
    internal sealed class ModeHActionData
    {
        /// <summary>按钮文案（已本地化）。</summary>
        public string Label;
        /// <summary>点击回调。</summary>
        public Action OnClick;
        /// <summary>是否可交互（按当前状态与 owner token 决定）。</summary>
        public bool Interactable = true;
        /// <summary>是否是危险动作（用 Danger token）。</summary>
        public bool IsDanger;
    }

    /// <summary>
    /// Mode H 六个非战斗模态页的构建器（设计提案 §23.1）。
    ///
    /// 与 `ModeHUI` 拆成两个文件只是为了遵守单文件 1200 行预算；
    /// 两者共用同一套 canvas、层级常量与模态租约，不是第二套 UI 系统。
    ///
    /// 表现基线（§23.1 冻结）：
    /// - 五席试棚与败者市场用**卡片栅格**（`BossRushUI.CreateCard` + accent rail + 品质描边）；
    /// - 赔率公开分差**逐行可展开拆解**，下注额是 0/1/2 三档步进控件；
    /// - 结算战报按事件序号排时间线，战痕 offer 用二选一卡片呈现利弊绑定；
    /// - 内容超出时用复用的 `ScrollRect`，不按内容实时扩容或每帧重建布局。
    /// </summary>
    internal static class ModeHUIPages
    {
        #region 分派

        /// <summary>构建指定页面的内容。</summary>
        public static void Build(
            ModeHPage page, Transform surface, Vector2 panelSize, ModeHPageContent content)
        {
            if (surface == null || content == null) return;

            ModeHUI.CreateTitle(surface, content.Title, panelSize);

            float cursorY = panelSize.y * 0.5f - ModeHUI.SafeMargin - 76f;
            if (content.ShowRealStakeNotice)
            {
                cursorY = CreateRealStakeNotice(surface, panelSize, cursorY);
            }

            switch (page)
            {
                case ModeHPage.Entry:
                case ModeHPage.Transfer:
                case ModeHPage.HallOfFame:
                    CreateCardGrid(surface, panelSize, content, cursorY);
                    break;
                case ModeHPage.Odds:
                    CreateOddsPage(surface, panelSize, content, cursorY);
                    break;
                case ModeHPage.Brief:
                case ModeHPage.Settlement:
                    CreateLineList(surface, panelSize, content, cursorY);
                    if (content.Cards.Count > 0)
                    {
                        // 战痕 offer 的二选一卡片挂在正文下方
                        CreateCardGrid(surface, panelSize, content, cursorY - 320f);
                    }
                    break;
                default:
                    ModeHUI.CreateBody(surface, content.Body, panelSize, 0f);
                    break;
            }

            CreateActions(surface, panelSize, content);
        }

        #endregion

        #region 风险行

        /// <summary>
        /// §22.1 冻结：入口/试棚页顶部固定显示真实资产风险行，**不可折叠、不可关闭**。
        /// </summary>
        private static float CreateRealStakeNotice(
            Transform surface, Vector2 panelSize, float cursorY)
        {
            GameObject notice = ZombieModeUIHelper.CreateRect(
                "ModeH_RealStakeNotice", surface,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, cursorY - NoticeHeight * 0.5f),
                new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f, NoticeHeight),
                new Vector2(0.5f, 0.5f));
            Image background = notice.AddComponent<Image>();
            background.color = BossRushUIColors.Danger;
            background.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(background, 8);

            GameObject textObj = ZombieModeUIHelper.CreateRect(
                "Text", notice.transform, new Vector2(0.5f, 0.5f),
                new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f - 24f, NoticeHeight - 12f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                textObj,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStakeRiskNotice"),
                22f, TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(text);
            return cursorY - NoticeHeight - 16f;
        }

        #endregion

        #region 卡片栅格

        /// <summary>
        /// 卡片栅格：一张卡固定读出原型图标位、底色、怪癖/异常、招牌口令、传闻。
        /// 异常用 Warning/Danger token 区分于普通怪癖，不与普通词条同级展示。
        /// </summary>
        private static void CreateCardGrid(
            Transform surface, Vector2 panelSize, ModeHPageContent content, float topY)
        {
            int count = content.Cards.Count;
            if (count == 0) return;

            float usableWidth = panelSize.x - ModeHUI.SafeMargin * 2f;
            int columns = count <= 3 ? Math.Max(1, count) : 3;
            float cardWidth = Mathf.Min(CardMaxWidth, (usableWidth - (columns - 1) * CardGap) / columns);
            float startX = -((columns - 1) * (cardWidth + CardGap)) * 0.5f;

            for (int i = 0; i < count; i++)
            {
                ModeHCardData data = content.Cards[i];
                if (data == null) continue;
                int column = i % columns;
                int row = i / columns;
                Vector2 position = new Vector2(
                    startX + column * (cardWidth + CardGap),
                    topY - CardHeight * 0.5f - row * (CardHeight + CardGap));

                Color accent = data.IsAnomaly
                    ? BossRushUIColors.Warning
                    : ModeHUI.ResolveRarityColor(data.GameQuality);
                GameObject card = BossRushUI.CreateCard(
                    "ModeH_Card_" + i, surface, position,
                    new Vector2(cardWidth, CardHeight),
                    BossRushUIColors.SurfaceRaised, accent, true);

                CreateCardText(card.transform, "Title", data.Title, 26f,
                    BossRushUIColors.TextPrimary, cardWidth, CardHeight * 0.5f - 28f);
                CreateCardText(card.transform, "Subtitle", data.Subtitle, 20f,
                    BossRushUIColors.Accent, cardWidth, CardHeight * 0.5f - 62f);
                CreateCardText(card.transform, "Body", data.Body, 18f,
                    BossRushUIColors.TextSecondary, cardWidth, -10f);

                if (data.OnClick == null) continue;
                ZombieModeUIHelper.CreateButton(
                    "ModeH_CardAction_" + i, card.transform,
                    data.ActionLabel != null
                        ? data.ActionLabel
                        : L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                    new Vector2(0.5f, 0f), new Vector2(0f, 34f),
                    new Vector2(cardWidth - 40f, 48f), BossRushUIColors.Accent, 20f,
                    new Vector2(cardWidth - 56f, 36f),
                    new UnityEngine.Events.UnityAction(data.OnClick), true);
            }
        }

        private static void CreateCardText(
            Transform parent, string name, string value, float fontSize, Color color,
            float cardWidth, float offsetY)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(
                name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, offsetY), new Vector2(cardWidth - 32f, 96f),
                new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, value != null ? value : string.Empty, fontSize,
                TextAlignmentOptions.TopLeft, color);
            BossRushUI.ApplyGameFont(text);
        }

        #endregion

        #region 赔率页

        /// <summary>
        /// 锁定赔率大字居中，公开分差逐行拆解，下注额 0/1/2 三档步进；
        /// 押品选择器与虚拟下注并排，`IsSlotConsistent` 为假时禁用并原位显示原因。
        /// </summary>
        private static void CreateOddsPage(
            Transform surface, Vector2 panelSize, ModeHPageContent content, float topY)
        {
            GameObject headline = ZombieModeUIHelper.CreateRect(
                "ModeH_OddsHeadline", surface, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, topY - 48f), new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f, 96f),
                new Vector2(0.5f, 0.5f));
            TextMeshProUGUI headlineText = ZombieModeUIHelper.CreateTMPText(
                headline, content.Body, 48f, TextAlignmentOptions.Center, BossRushUIColors.Accent);
            BossRushUI.ApplyGameFont(headlineText);

            CreateLineList(surface, panelSize, content, topY - 120f);

            // 押品选择器：与虚拟下注并排，禁用时在原位显示具体原因
            GameObject selector = ZombieModeUIHelper.CreateRect(
                "ModeH_RealStakeSelector", surface,
                new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-(ModeHUI.SafeMargin + StakeSelectorSize.x * 0.5f),
                    ModeHUI.SafeMargin + StakeSelectorSize.y * 0.5f + 72f),
                StakeSelectorSize, new Vector2(0.5f, 0.5f));
            Image selectorBackground = selector.AddComponent<Image>();
            selectorBackground.color = content.RealStakeSelectorEnabled
                ? BossRushUIColors.SurfaceRaised
                : BossRushUIColors.Disabled;
            BossRushUI.ApplyPanelSkin(selectorBackground, 8);

            string selectorText = content.RealStakeSelectorEnabled
                ? L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_NotSelected")
                : (content.RealStakeDisabledReason != null
                    ? content.RealStakeDisabledReason
                    : L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_Disabled"));

            GameObject selectorTextObj = ZombieModeUIHelper.CreateRect(
                "Text", selector.transform, new Vector2(0.5f, 0.5f),
                new Vector2(StakeSelectorSize.x - 24f, StakeSelectorSize.y - 24f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                selectorTextObj,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_Selector") + "\n" + selectorText,
                20f, TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(text);
        }

        #endregion

        #region 行列表

        /// <summary>
        /// 逐行列表。行数超出可视范围时放进复用的 `ScrollRect`，
        /// 不按内容实时扩容，也不每帧重建布局。
        /// </summary>
        private static void CreateLineList(
            Transform surface, Vector2 panelSize, ModeHPageContent content, float topY)
        {
            if (content.Lines.Count == 0)
            {
                ModeHUI.CreateBody(surface, content.Body, panelSize, 0f);
                return;
            }

            float viewportHeight = Mathf.Max(200f, topY - (-panelSize.y * 0.5f + ModeHUI.SafeMargin + 96f));
            float contentHeight = content.Lines.Count * LineHeight;
            Transform host = surface;

            if (contentHeight > viewportHeight)
            {
                host = CreateScrollHost(
                    surface, panelSize, topY, viewportHeight, contentHeight).transform;
                topY = contentHeight * 0.5f;
            }

            for (int i = 0; i < content.Lines.Count; i++)
            {
                GameObject row = ZombieModeUIHelper.CreateRect(
                    "ModeH_Line_" + i, host, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, topY - LineHeight * (i + 0.5f)),
                    new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f - 24f, LineHeight - 4f),
                    new Vector2(0.5f, 0.5f));
                TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                    row, content.Lines[i], 22f, TextAlignmentOptions.Left,
                    BossRushUIColors.TextSecondary);
                BossRushUI.ApplyGameFont(text);
            }
        }

        /// <summary>官方 ScrollRect prefab 优先；不可用时回退共享库手搓一个可滚动容器。</summary>
        private static GameObject CreateScrollHost(
            Transform surface, Vector2 panelSize, float topY, float viewportHeight, float contentHeight)
        {
            ScrollRect official = TryInstantiateOfficialScrollRect(surface);
            if (official != null)
            {
                RectTransform rect = official.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(
                    panelSize.x - ModeHUI.SafeMargin * 2f, viewportHeight);
                rect.anchoredPosition = new Vector2(0f, topY - viewportHeight * 0.5f);
                if (official.content != null)
                {
                    official.content.sizeDelta = new Vector2(rect.sizeDelta.x, contentHeight);
                    return official.content.gameObject;
                }
                return official.gameObject;
            }

            GameObject viewport = ZombieModeUIHelper.CreateRect(
                "ModeH_Scroll", surface, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, topY - viewportHeight * 0.5f),
                new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f, viewportHeight),
                new Vector2(0.5f, 0.5f));
            viewport.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;

            GameObject contentRoot = ZombieModeUIHelper.CreateRect(
                "ModeH_ScrollContent", viewport.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f, contentHeight),
                new Vector2(0.5f, 1f));
            scroll.content = contentRoot.GetComponent<RectTransform>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            return contentRoot;
        }

        private static ScrollRect TryInstantiateOfficialScrollRect(Transform parent)
        {
            try
            {
                if (GameplayDataSettings.UIPrefabs == null) return null;
                ScrollRect prefab = GameplayDataSettings.UIPrefabs.ScrollRect;
                if (prefab == null) return null;
                return UnityEngine.Object.Instantiate(prefab, parent, false);
            }
            catch (Exception)
            {
                // 官方 prefab 不可用：回退共享库手搓
                return null;
            }
        }

        #endregion

        #region 动作按钮

        /// <summary>底部动作按钮。按钮的可交互性由调用方按当前状态与 owner token 决定。</summary>
        private static void CreateActions(
            Transform surface, Vector2 panelSize, ModeHPageContent content)
        {
            int count = content.Actions.Count;
            if (count == 0) return;

            float startX = -((count - 1) * (ActionSize.x + CardGap)) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                ModeHActionData action = content.Actions[i];
                if (action == null) continue;
                ZombieModeUIHelper.CreateButton(
                    "ModeH_Action_" + i, surface, action.Label,
                    new Vector2(0.5f, 0f),
                    new Vector2(startX + i * (ActionSize.x + CardGap),
                        ModeHUI.SafeMargin + ActionSize.y * 0.5f),
                    ActionSize,
                    action.Interactable
                        ? (action.IsDanger ? BossRushUIColors.Danger : BossRushUIColors.Accent)
                        : BossRushUIColors.Disabled,
                    22f, new Vector2(ActionSize.x - 16f, ActionSize.y - 16f),
                    action.OnClick != null
                        ? new UnityEngine.Events.UnityAction(action.OnClick)
                        : null,
                    action.Interactable);
            }
        }

        #endregion

        #region 布局常量

        private const float NoticeHeight = 64f;
        private const float CardMaxWidth = 420f;
        private const float CardHeight = 300f;
        private const float CardGap = 24f;
        private const float LineHeight = 34f;
        private static readonly Vector2 ActionSize = new Vector2(240f, 56f);
        private static readonly Vector2 StakeSelectorSize = new Vector2(420f, 220f);

        #endregion
    }
}
