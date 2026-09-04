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
        /// <summary>
        /// 底部动作按钮。
        ///
        /// **数量必须有界**：`CreateActions` 是单行居中平铺，越界的按钮不会被裁剪
        /// （模态 surface 上没有任何 Mask），会直接画到屏幕外点不到。
        /// 数量随玩家状态变化的东西（押品格随仓库物品数变化）一律走
        /// `RealStakeSlots` 那样的独立滚动区，不要塞进这里。
        /// 由 ModeHActionLayoutGuard 守卫。
        /// </summary>
        public List<ModeHActionData> Actions = new List<ModeHActionData>();
        /// <summary>
        /// 押品格条目。数量 = 仓库前 40 格里的非空格数，**无上界**，
        /// 所以渲染进独立的滚动选择器区而不是底部动作行。
        /// </summary>
        public List<ModeHActionData> RealStakeSlots = new List<ModeHActionData>();
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
        /// <summary>
        /// 品质（决定描边颜色）。**故意保持不赋值**：目前唯一的卡片来源是选手与名人堂
        /// 记录，选手不是装备、没有品质等级，留 0 走 RarityCommon 的中性描边正是想要的表现
        /// （详见 ModeHRuntimeModule_MatchFlow.BuildProfileCard 的注释）。
        /// 定点关掉 CS0649：这条警告本身是对的，压掉它是为了不让每次构建都刷一行噪声、
        /// 从而掩盖真正新出现的警告；将来若有装备卡要用品质描边，直接赋值即可。
        /// </summary>
#pragma warning disable 0649
        public int GameQuality;
#pragma warning restore 0649
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
        /// <summary>
        /// 是否无视恢复壳的只读置灰（`allowActions=false`）。
        ///
        /// 只有**减少**玩家资产暴露的动作才可以置 true —— 目前唯一的用例是
        /// 「把内存托管中的押品还回仓库」。只读保护的本意是"证据不足时不许动
        /// 资产"，而 IsSlotConsistent=false 本身就是"押品还没归位"的同义词，
        /// 用它把唯一的补救按钮关掉会让玩家除删档外无路可走。
        /// 任何会**新增**资产风险的动作都不得使用这个旁路。
        /// </summary>
        public bool BypassReadOnly;
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

            // 行数必须放得下，否则末行会压到底部动作行上。
            // 入口页就是这么翻车的：ShowRealStakeNotice 把 topY 压到 226，
            // 5 张选秀卡按 3 列排成两行，第 2 行落在 y∈[-398,-98]，
            // 而动作行在 y∈[-382,-326] —— 第 4、5 张卡直接被画在按钮底下。
            // 放不下就加列（卡片变窄），而不是继续往下堆。
            float floorY = -panelSize.y * 0.5f + ActionBandReserve;
            float availableHeight = topY - floorY;
            int maxRows = Mathf.Max(1,
                Mathf.FloorToInt((availableHeight + CardGap) / (CardHeight + CardGap)));
            while (columns < count)
            {
                int rows = (count + columns - 1) / columns;
                if (rows <= maxRows) break;
                float probeWidth = (usableWidth - columns * CardGap) / (columns + 1);
                if (probeWidth < CardMinWidth) break;
                columns++;
            }

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

            // 标题行固定在选择器顶部，余下高度留给押品格滚动区
            GameObject selectorTextObj = ZombieModeUIHelper.CreateRect(
                "Text", selector.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(SelectorHeaderHeight * 0.5f + 8f)),
                new Vector2(StakeSelectorSize.x - 24f, SelectorHeaderHeight),
                new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                selectorTextObj,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_Selector") + "\n" + selectorText,
                20f, TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(text);

            CreateRealStakeSlots(selector.transform, content);
        }

        /// <summary>
        /// 押品格列表。**必须滚动**：格数 = 仓库前 40 格里的非空格数，无上界。
        ///
        /// 这里是本页唯一会随玩家仓库变长的区域。历史 bug 是把它塞进底部动作行，
        /// 那是单行居中平铺、又没有任何 Mask，格数一多就把排在最后的「锁盘」
        /// 推到屏幕外——赔率页是 timeScale=0 的模态页且没有关闭按钮，玩家只能弃局。
        /// 形态照 PetNestUI 的动作滚动区（那边同架构、同坑、已修）。
        /// </summary>
        private static void CreateRealStakeSlots(Transform selector, ModeHPageContent content)
        {
            int count = content.RealStakeSlots.Count;
            if (count == 0) return;

            float viewportHeight = StakeSelectorSize.y - SelectorHeaderHeight - 24f;
            if (viewportHeight < 40f) return;
            float contentHeight = count * (SlotButtonHeight + SlotGap);
            float slotWidth = StakeSelectorSize.x - 32f;

            Transform host = selector;
            float topY = -(SelectorHeaderHeight + 12f);
            if (contentHeight > viewportHeight)
            {
                GameObject scroll = ZombieModeUIHelper.CreateRect(
                    "ModeH_RealStakeSlotScroll", selector,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, topY - viewportHeight * 0.5f),
                    new Vector2(slotWidth + 8f, viewportHeight),
                    new Vector2(0.5f, 0.5f));
                scroll.AddComponent<RectMask2D>();

                GameObject scrollContent = ZombieModeUIHelper.CreateRect(
                    "Content", scroll.transform,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -contentHeight * 0.5f),
                    new Vector2(slotWidth + 8f, contentHeight),
                    new Vector2(0.5f, 0.5f));

                ScrollRect scrollRect = scroll.AddComponent<ScrollRect>();
                scrollRect.content = scrollContent.GetComponent<RectTransform>();
                scrollRect.viewport = scroll.GetComponent<RectTransform>();
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.scrollSensitivity = 24f;

                host = scrollContent.transform;
                topY = 0f;
            }

            for (int i = 0; i < count; i++)
            {
                ModeHActionData slot = content.RealStakeSlots[i];
                if (slot == null) continue;
                float y = topY - SlotButtonHeight * 0.5f - i * (SlotButtonHeight + SlotGap);
                ZombieModeUIHelper.CreateButton(
                    "ModeH_RealStakeSlot_" + i, host, slot.Label,
                    new Vector2(0.5f, 1f), new Vector2(0f, y),
                    new Vector2(slotWidth, SlotButtonHeight),
                    slot.Interactable ? BossRushUIColors.Danger : BossRushUIColors.Disabled,
                    18f, new Vector2(slotWidth - 12f, SlotButtonHeight - 8f),
                    slot.OnClick != null
                        ? new UnityEngine.Events.UnityAction(slot.OnClick)
                        : null,
                    slot.Interactable);
            }
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

            float viewportHeight = Mathf.Max(200f, topY - (-panelSize.y * 0.5f + ActionBandReserve));
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

        /// <summary>
        /// 底部动作按钮。按钮的可交互性由调用方按当前状态与 owner token 决定。
        ///
        /// 超过 `MaxSingleRowActions` 时自动换行向上堆叠，而不是继续往两侧铺开。
        /// 模态 surface 上没有 Mask，越界按钮不会被裁掉而是画到屏幕外点不到；
        /// 排在最后的往往正是「锁盘」「确认」这类唯一出口，一旦推出屏幕
        /// 玩家就困在 timeScale=0 的模态页里了。
        /// 常规页面本来就该 ≤7 个按钮（数量随玩家状态变化的走独立滚动区），
        /// 这里的换行是**兜底**，不是让调用方随便加按钮的许可。
        /// </summary>
        private static void CreateActions(
            Transform surface, Vector2 panelSize, ModeHPageContent content)
        {
            int count = content.Actions.Count;
            if (count == 0) return;

            // 每行容量按**本页面板宽度**算，而不是写死一个数：结算页用的是更窄的
            // ReportPanelSize(1180)，同样 5 个按钮在主面板(1480)里放得下、在结算页就出框。
            // 再与 MaxSingleRowActions 取小，兜住"面板比屏幕还宽"的假设失效。
            float step = ActionSize.x + CardGap;
            float usableWidth = panelSize.x - ModeHUI.SafeMargin * 2f;
            int fitPerRow = Mathf.Max(1, Mathf.FloorToInt((usableWidth + CardGap) / step));
            int perRow = Mathf.Min(count, Mathf.Min(fitPerRow, MaxSingleRowActions));
            float startX = -((perRow - 1) * step) * 0.5f;
            float rowStride = ActionSize.y + CardGap;

            for (int i = 0; i < count; i++)
            {
                ModeHActionData action = content.Actions[i];
                if (action == null) continue;
                int column = i % perRow;
                int row = i / perRow;
                // 末行贴底，早先的行往上堆：最后一颗按钮永远在最容易够到的位置
                int totalRows = (count + perRow - 1) / perRow;
                float y = ModeHUI.SafeMargin + ActionSize.y * 0.5f
                    + (totalRows - 1 - row) * rowStride;
                ZombieModeUIHelper.CreateButton(
                    "ModeH_Action_" + i, surface, action.Label,
                    new Vector2(0.5f, 0f),
                    new Vector2(startX + column * step, y),
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
        /// <summary>卡片最窄宽度：再窄标题就折行折到不可读，宁可继续往下排。</summary>
        private const float CardMinWidth = 220f;
        private const float CardHeight = 300f;

        /// <summary>
        /// 底部动作行的保留高度。行列表与卡片网格都按这个数留白，
        /// 三处必须用同一个常量，否则又会出现"某一处以为下面是空的"的重叠。
        /// </summary>
        private const float ActionBandReserve = ModeHUI.SafeMargin + 96f;
        private const float CardGap = 24f;
        private const float LineHeight = 34f;
        private static readonly Vector2 ActionSize = new Vector2(240f, 56f);
        private static readonly Vector2 StakeSelectorSize = new Vector2(420f, 220f);
        /// <summary>押品选择器顶部标题行高度，余下高度归押品格滚动区。</summary>
        private const float SelectorHeaderHeight = 56f;
        private const float SlotButtonHeight = 34f;
        private const float SlotGap = 4f;

        /// <summary>
        /// 单行动作区能容纳的按钮数上限。
        ///
        /// 推导：按钮居中平铺、步距 `ActionSize.x + CardGap` = 264，
        /// 最右一颗中心 x = (n-1)*132，右边缘再加半宽 120；
        /// canvas 参考分辨率 1920（`ConfigureCanvasScaler`），半宽 960。
        /// (n-1)*132 + 120 ≤ 960 → n ≤ 7。模态 surface 上没有任何 Mask，
        /// 越界按钮不会被裁掉，而是直接画到屏幕外点不到。
        /// </summary>
        internal const int MaxSingleRowActions = 7;

        #endregion
    }
}
