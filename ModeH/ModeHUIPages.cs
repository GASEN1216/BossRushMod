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
        /// <summary>正文（已本地化）。结算页有胜负时它就是横幅上的大字（「本场胜利」）。</summary>
        public string Body;
        /// <summary>结算页的胜负色调；None 时不画胜负。</summary>
        public ModeHResultTone ResultTone;
        /// <summary>赔率页大数字上方的小字标签（「锁定赔率」）。</summary>
        public string Headline;
        /// <summary>赔率页的大数字（「x1.8」）；为空时赔率页按正文排（整备不可用一类兜底）。</summary>
        public string HeadlineValue;
        /// <summary>卡片列表（五席候选、市场 offer、名人堂条目等）。</summary>
        public List<ModeHCardData> Cards = new List<ModeHCardData>();
        /// <summary>逐行拆解（赔率分量、战报时间线等）。「键：值」的行按两栏排。</summary>
        public List<string> Lines = new List<string>();
        /// <summary>「已按默认值自动处理」的说明行（结算页）：排在战报行后面，行首一道警示色细条。</summary>
        public List<string> NoteLines = new List<string>();
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
        public List<ModeHActionData> PreparationOptions = new List<ModeHActionData>();
        /// <summary>是否在顶部显示真实资产风险行（入口页必须为 true）。</summary>
        public bool ShowRealStakeNotice;
        /// <summary>
        /// 风险行改成页脚一行小字而不是顶部红条。选人页用：正常流程按默认值开打、从不押真实物品，
        /// 顶部红条会把「挑个选手」吓成「要赌仓库」；披露本身仍在（§22.1 入口页固定显示）。
        /// </summary>
        public bool CompactRiskNotice;
        /// <summary>押品选择器是否可用；不可用时原位显示 DisabledReason。</summary>
        public bool RealStakeSelectorEnabled;
        /// <summary>押品选择器禁用原因（已本地化）。</summary>
        public string RealStakeDisabledReason;
        /// <summary>
        /// 同一页刷新（由 ModeHUI.OpenPage 写入，调用方不用管）：只换内容，不播卡片与行的入场动画。
        /// </summary>
        internal bool Refresh;
        /// <summary>
        /// 一排分段按钮（UI 制作共识：同页并列选项做成分段，不堆进底部动作条）：
        /// 页签式分页、下注档、免费侦察。AtTop 的排在页头下，其余排在动作带上方。见 ModeHUIPageRows.cs。
        /// </summary>
        public List<ModeHOptionRow> OptionRows = new List<ModeHOptionRow>();
        /// <summary>刚才那一下为什么没成（锁盘被拒、押品被拒……）：红字画在按钮带正上方，就在点的地方（审查 B-11）。</summary>
        public string FailureText;
        public List<ModeHCardData> PlayerFighters = new List<ModeHCardData>();
        public List<ModeHCardData> EnemyFighters = new List<ModeHCardData>();
        public List<ModeHItemIconData> RewardItems = new List<ModeHItemIconData>();
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
        /// <summary>
        /// 立绘键（选手的 stableKey = 官方 preset nameKey = 图鉴条目键）。非空时卡片按选人卡画，
        /// 立绘取自鸭皇图鉴：图鉴立绘 → 官方角色图标 → 模式徽记淡显 → 名字首字。
        /// </summary>
        public string PortraitKey;
        /// <summary>点击回调；为 null 表示只读卡。</summary>
        public Action OnClick;
        /// <summary>点击按钮文案。</summary>
        public string ActionLabel;
        /// <summary>直接给的图标（押物品选择页的官方物品图标）；非空时优先于立绘键。</summary>
        public Sprite Icon;
        /// <summary>选中态（押物品选择页勾上的那几件）：金色描边 + 右上角角标。</summary>
        public bool IsSelected;
        /// <summary>选中角标的字；空时写「√ 已选」。</summary>
        public string SelectedBadge;
        public List<ModeHStatData> Stats = new List<ModeHStatData>();
        public List<ModeHItemIconData> Equipment = new List<ModeHItemIconData>();
        public int Count = 1;
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
        /// 本页主操作（开打 / 锁盘 / 下一场）：AccentFill 实心底。一页最多一个；都没标时，
        /// 页上唯一一颗可点的非危险按钮自动当主操作，其余一律次级（深色底 + 描边）。
        /// </summary>
        public bool IsPrimary;
        /// <summary>
        /// 当前选中的那一档（下注档、押品格、整备选项）：仍可点，底色染一点主色 + 主色描边。
        /// 旧版把选中档画成禁用灰，看起来像「不能选」。
        /// </summary>
        public bool IsSelected;
        /// <summary>整备选项选中时右上角角标的字；空时写「√ 已选」。首发 / 接力 / 已带上分开写，两个角标不再像多选（审查 B-08 / B-22）。</summary>
        public string SelectedBadge;
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
        /// <summary>
        /// 这颗是本页的「返回」（整备页、押物品选择页的「完成」，恢复壳的「稍后处理」）：ESC / 手柄取消等于点它。
        /// 没有返回语义的页不标，ESC 照常交给官方暂停菜单（2026-09-24）。
        /// </summary>
        public bool IsCancel;
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
    ///
    /// 2026-09-23 审美打磨（审查 UB-01/03/04/09/10/11/12/13/34/35）：
    /// - 入口页与结算页的页头是模式横幅，标题 / 胜负压在横幅中央；其余页是「徽记 + 标题」；
    /// - 选人卡整张可点、悬停描边换主色；底部「选他出战」是描边按钮，不再是五条平涂薄荷绿；
    /// - 按钮分主次：一页最多一个 AccentFill 主按钮，危险操作 Danger，其余深色底 + 描边；选中档染主色而不是置灰；
    /// - 战报 / 看盘 / 赔率拆解放进一张卡片，「键：值」两栏、行间细分隔线，自动处理说明行首一道警示色细条；
    /// - 风险提示是深色卡 + 危险色竖条与字，不再是整条实心红。
    /// </summary>
    internal static partial class ModeHUIPages
    {
        #region 分派

        /// <summary>构建指定页面的内容。</summary>
        public static void Build(
            ModeHPage page, Transform surface, Vector2 panelSize, ModeHPageContent content)
        {
            if (surface == null || content == null) return;

            float cursorY = CreateHeader(page, surface, panelSize, content);
            if (content.ShowRealStakeNotice && !content.CompactRiskNotice)
            {
                cursorY = CreateRealStakeNotice(surface, panelSize, cursorY);
            }
            cursorY = CreateTopOptionRows(surface, panelSize, content, cursorY);

            switch (page)
            {
                case ModeHPage.Entry:
                case ModeHPage.Transfer:
                    CreateChampionCards(surface, panelSize, content, cursorY);
                    break;
                case ModeHPage.HallOfFame:
                    CreateCardGrid(surface, panelSize, content, cursorY);
                    break;
                case ModeHPage.ItemBet:
                    CreateItemBetGrid(surface, panelSize, content, cursorY);
                    break;
                case ModeHPage.Odds:
                    if (content.PreparationOptions.Count > 0)
                        CreatePreparationOptions(surface, panelSize, content, cursorY);
                    else if (content.PlayerFighters.Count > 0)
                        CreateMatchComparison(surface, panelSize, content, cursorY);
                    else CreateOddsPage(surface, panelSize, content, cursorY);
                    break;
                case ModeHPage.Brief:
                    if (content.PlayerFighters.Count > 0)
                    {
                        CreateMatchComparison(surface, panelSize, content, cursorY);
                        break;
                    }
                    goto case ModeHPage.Settlement;
                case ModeHPage.Settlement:
                    cursorY = CreateRewardIcons(surface, panelSize, content, cursorY);
                    CreateLineList(surface, panelSize, content, cursorY,
                        content.Cards.Count > 0 ? 224f : float.PositiveInfinity);
                    if (content.Cards.Count > 0)
                    {
                        // 战痕 offer 的二选一卡片挂在正文下方
                        CreateCardGrid(surface, panelSize, content, cursorY - 248f, false);
                    }
                    break;
                default:
                    ModeHUI.CreateBody(surface, content.Body, panelSize, 0f);
                    break;
            }

            if (HasCompactNotice(content))
            {
                CreateCompactRiskNotice(surface, panelSize, content);
            }
            CreateFooterOptionRows(surface, panelSize, content);
            CreateFailureLine(surface, panelSize, content);
            CreateActions(surface, panelSize, content);
        }

        #endregion

        #region 页头

        /// <summary>
        /// 页头。入口页与结算页画模式横幅，标题（入口页）或胜负（结算页）压在横幅中央两只鸭子之间的暗墙上；
        /// 其余页是「徽记 + 标题」一行。横幅取不到时退回标题行，结算页另补一张胜负卡——胜负绝不能丢
        /// （审查 UB-03：旧版有逐行战报时根本不渲染 Body，每场打完看不到输赢）。返回内容区顶边。
        /// </summary>
        private static float CreateHeader(ModeHPage page, Transform surface, Vector2 panelSize, ModeHPageContent content)
        {
            bool result = page == ModeHPage.Settlement && content.ResultTone != ModeHResultTone.None;
            if (page == ModeHPage.Entry || result)
            {
                float heroBottom;
                if (TryCreateHeroHeader(surface, panelSize, content, result, out heroBottom)) return heroBottom;
            }
            ModeHUI.CreateTitle(surface, content.Title, panelSize);
            float cursorY = panelSize.y * 0.5f - ModeHUI.SafeMargin - 76f;
            return result ? CreateResultCard(surface, panelSize, content, cursorY) : cursorY;
        }

        #endregion

        #region 风险行

        /// <summary>
        /// §22.1 冻结：入口/试棚页顶部固定显示真实资产风险行，**不可折叠、不可关闭**。
        /// 样式是深色卡 + 左侧危险色竖条 + 危险色字（审查 UB-12：旧版整条 1384×64 实心红，是整页最大的色块，比内容还抢眼）。
        /// </summary>
        private static float CreateRealStakeNotice(
            Transform surface, Vector2 panelSize, float cursorY)
        {
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;
            GameObject notice = BossRushUI.CreateCard(
                "ModeH_RealStakeNotice", surface,
                new Vector2(0f, cursorY - NoticeHeight * 0.5f), new Vector2(width, NoticeHeight),
                BossRushUIColors.SurfaceRaised, BossRushUIColors.DangerText, true);
            Image background = notice.GetComponent<Image>();
            if (background != null) background.raycastTarget = false;

            GameObject textObj = ZombieModeUIHelper.CreateRect(
                "Text", notice.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Vector2(0.5f, 0.5f));
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.offsetMin = new Vector2(22f, 6f);
            textRect.offsetMax = new Vector2(-16f, -6f);
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                textObj,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStakeRiskNotice"),
                17f, TextAlignmentOptions.MidlineLeft, BossRushUIColors.DangerText);
            BossRushUI.ApplyGameFont(text);
            return cursorY - NoticeHeight - 16f;
        }

        private static bool HasCompactNotice(ModeHPageContent content)
        {
            return content.ShowRealStakeNotice && content.CompactRiskNotice;
        }

        /// <summary>页脚风险行的下沿：排在按钮带、失败提示与页脚分段行上面；都没有时贴面板底边。</summary>
        private static float GetCompactNoticeBottom(Vector2 panelSize, ModeHPageContent content)
        {
            float stack = GetFooterStackTop(panelSize, content);
            if (stack > 0f) return stack + 6f;
            return content.Actions.Count > 0 ? GetActionBandTop(panelSize, content) + 6f : CompactNoticeBottom;
        }

        /// <summary>风险行的页脚形态：一行注脚小字（两行高，英文会折行）。</summary>
        private static void CreateCompactRiskNotice(Transform surface, Vector2 panelSize, ModeHPageContent content)
        {
            GameObject row = ZombieModeUIHelper.CreateRect(
                "ModeH_RealStakeNotice", surface,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, GetCompactNoticeBottom(panelSize, content) + CompactNoticeHeight * 0.5f),
                new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f, CompactNoticeHeight),
                new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                row,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStakeRiskNotice"),
                14f, TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(text);
        }

        /// <summary>
        /// 内容区下沿到面板底边的距离：让开底部动作带；有页脚风险行时让开它（风险行本身排在动作带上面）。
        /// </summary>
        private static float GetFloorReserve(Vector2 panelSize, ModeHPageContent content, float actionReserve)
        {
            // 动作带上方从下往上依次是：失败提示行 → 页脚分段行 → 页脚风险行（ModeHUIPageRows.GetFooterStackTop）
            if (!HasCompactNotice(content))
            {
                return Mathf.Max(actionReserve, GetFooterStackTop(panelSize, content) + CardGap * 0.5f);
            }
            return GetCompactNoticeBottom(panelSize, content) + CompactNoticeHeight + CardGap * 0.5f;
        }

        #endregion

        #region 选人卡

        /// <summary>
        /// 选人卡（入口页五张、转会页一张）：一排大卡，每张 = 图鉴立绘 + 名字 + 打法定位 + 两三句白话 + 「选他出战」。
        /// 立绘链与鸭皇图鉴同源（CodexView_Grid.ResolvePortrait）：图鉴立绘 → 官方角色图标 → 模式徽记淡显 → 名字首字；
        /// 图鉴缓存 fail-open，缺包只少一张图，不挡选人。
        /// 卡数有上界（候选固定五席），一排放得下，不需要滚动；底部按共用的动作带或页脚风险行让位。
        /// 可选的卡整张都能点（审查 UB-04），悬停时描边换主色。
        /// </summary>
        private static void CreateChampionCards(
            Transform surface, Vector2 panelSize, ModeHPageContent content, float topY)
        {
            float usableWidth = panelSize.x - ModeHUI.SafeMargin * 2f;
            if (!string.IsNullOrEmpty(content.Body))
            {
                GameObject intro = ZombieModeUIHelper.CreateRect(
                    "ModeH_CardGridBody", surface, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, topY - CardBodyHeight * 0.5f),
                    new Vector2(usableWidth - 24f, CardBodyHeight), new Vector2(0.5f, 0.5f));
                TextMeshProUGUI introText = ZombieModeUIHelper.CreateTMPText(
                    intro, content.Body, 18f, TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
                BossRushUI.ApplyGameFont(introText);
                topY -= CardBodyHeight + CardGap * 0.5f;
            }

            int count = content.Cards.Count;
            if (count == 0) return;
            float floorY = -panelSize.y * 0.5f + GetFloorReserve(panelSize, content, content.Actions.Count > 0
                ? GetActionBandReserve(panelSize, content)
                : ModeHUI.SafeMargin);
            float height = Mathf.Min(ChampionCardMaxHeight, topY - floorY);
            float width = Mathf.Min(ChampionCardMaxWidth, (usableWidth - (count - 1) * ChampionCardGap) / count);
            float startX = -((count - 1) * (width + ChampionCardGap)) * 0.5f;

            for (int i = 0; i < count; i++)
            {
                ModeHCardData data = content.Cards[i];
                if (data == null) continue;
                GameObject card = BossRushUI.CreateCard(
                    "ModeH_Card_" + i, surface,
                    new Vector2(startX + i * (width + ChampionCardGap), topY - height * 0.5f),
                    new Vector2(width, height), BossRushUIColors.SurfaceRaised,
                    data.IsAnomaly ? BossRushUIColors.Warning : BossRushUIColors.Accent, true);
                BuildChampionCard(card.transform, data, width, height, i);
                // 五张卡错峰升起：只动 alpha 与位置，按钮从第一帧就能点（见 BossRushUIEntranceAnimation）
                if (!content.Refresh) BossRushUIEntranceAnimation.Play(card, 0.05f * i, 0.28f, 18f);
            }
        }

        private static void BuildChampionCard(Transform card, ModeHCardData data, float width, float height, int index)
        {
            float inner = width - ChampionCardPadding * 2f;
            float portrait = Mathf.Min(inner, height * (data.Stats.Count > 0 ? 0.13f : 0.40f));
            float y = -ChampionCardPadding;
            CreatePortrait(card, data, new Vector2(0f, y), portrait);
            y -= portrait + 10f;

            // 单行框高按 1.45×字号 + 4 留足：TMP 的 Ellipsis 在框比一行还矮时会把整串清空
            y = CreateChampionText(card, "Name", data.Title, 26f, BossRushUIColors.TextPrimary,
                TextAlignmentOptions.Center, inner, y, ChampionNameHeight);
            y = CreateChampionText(card, "Role", data.Subtitle, 18f, BossRushUIColors.Accent,
                TextAlignmentOptions.Center, inner, y, ChampionRoleHeight);
            y -= 6f;

            float bottom = data.OnClick != null
                ? ChampionCardPadding + ChampionButtonHeight + 8f
                : ChampionCardPadding;
            float bodyHeight = Mathf.Max(ChampionRoleHeight, height + y - bottom);
            if (data.Stats.Count > 0)
                CreateFighterMeasurements(card, data, inner, y, bodyHeight, true);
            else CreateChampionText(card, "Body", data.Body, 17f, BossRushUIColors.TextSecondary,
                TextAlignmentOptions.TopLeft, inner, y, bodyHeight);

            if (data.IsSelected) AddSelectedBadge(card, data.SelectedBadge);

            if (data.OnClick == null) return;
            UnityEngine.Events.UnityAction pick = new UnityEngine.Events.UnityAction(data.OnClick);
            MakeCardClickable(card, pick);
            // 名字沿用 ModeH_CardAction_<i>：F3 验收按这个名字找按钮。样式是描边按钮（整卡已可点，这颗只是把「能点」说清楚）
            Button action = ZombieModeUIHelper.CreateButton(
                "ModeH_CardAction_" + index, card,
                data.ActionLabel != null
                    ? data.ActionLabel
                    : L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                new Vector2(0.5f, 0f),
                new Vector2(0f, ChampionCardPadding + ChampionButtonHeight * 0.5f),
                new Vector2(inner, ChampionButtonHeight), BossRushUIColors.SurfaceRaised, 19f,
                new Vector2(inner - 16f, ChampionButtonHeight - 12f),
                pick, true);
            StyleOutlineButton(action, BossRushUIColors.Accent);
        }

        /// <summary>从卡片顶边往下排一个文字块，返回下一块的顶边。</summary>
        private static float CreateChampionText(Transform card, string name, string value, float fontSize,
            Color color, TextAlignmentOptions alignment, float width, float top, float height)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(
                name, card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, top), new Vector2(width, height), new Vector2(0.5f, 1f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                obj, value != null ? value : string.Empty, fontSize, alignment, color);
            BossRushUI.ApplyGameFont(text);
            return top - height;
        }

        /// <summary>
        /// 立绘块：图鉴立绘 → 官方角色图标 → 模式徽记淡显 → 名字首字。与 CodexView_Grid.CreatePortraitBlock 同一条链，
        /// 第三级从名字首字换成徽记（审查 UB-13：首字与下面的名字重复，像占位）；徽记也取不到时才退回首字。
        /// </summary>
        private static void CreatePortrait(Transform card, ModeHCardData data, Vector2 anchoredTop, float size)
        {
            GameObject holder = ZombieModeUIHelper.CreateRect(
                "Portrait", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                anchoredTop, new Vector2(size, size), new Vector2(0.5f, 1f));
            Image backing = holder.AddComponent<Image>();
            backing.color = BossRushUIColors.Header;
            backing.raycastTarget = false;
            BossRushUI.ApplyFramedPanelSkin(backing, 10, BossRushUISkinPart.Card);

            Sprite sprite = data.Icon;
            if (sprite == null && !string.IsNullOrEmpty(data.PortraitKey))
            {
                sprite = CodexPortraitCache.GetPortrait(data.PortraitKey);
                if (sprite == null) sprite = CodexPortraitCache.GetOfficialIcon(data.PortraitKey);
            }
            float inset = 12f;
            Color tint = data.Icon != null ? Color.white : BossRushUIHero.ArtTint;
            if (sprite == null)
            {
                sprite = ModeHPresentationAssetCache.GetEmblemSprite();
                inset = Mathf.Round(size * 0.22f);
                tint = PortraitEmblemTint;
            }
            if (sprite != null)
            {
                GameObject art = ZombieModeUIHelper.CreateRect(
                    "Art", holder.transform, Vector2.zero, Vector2.one, Vector2.zero,
                    new Vector2(-inset, -inset), new Vector2(0.5f, 0.5f));
                Image image = art.AddComponent<Image>();
                image.sprite = sprite;
                image.color = tint;
                image.preserveAspect = true;
                image.raycastTarget = false;
                return;
            }

            // 最后一级：名字首字。官方 characterIconType == none 且徽记也缺时，这一级必须有，否则是一块空底。
            string name = data.Title ?? string.Empty;
            GameObject initial = ZombieModeUIHelper.CreateRect(
                "Initial", holder.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                initial, name.Length > 0 ? name.Substring(0, 1) : "?", size * 0.42f,
                TextAlignmentOptions.Center, BossRushUIColors.Accent);
            text.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(text);
        }

        #endregion

        #region 卡片栅格

        /// <summary>
        /// 卡片栅格：一张卡固定读出原型图标位、底色、怪癖/异常、招牌口令、传闻。
        /// 异常用 Warning/Danger token 区分于普通怪癖，不与普通词条同级展示。
        /// </summary>
        private static void CreateCardGrid(
            Transform surface, Vector2 panelSize, ModeHPageContent content, float topY, bool showBody = true)
        {
            // 先出正文：入口页的选秀操作说明、名人堂的席位数都写在 page.Body 上，
            // 而这条渲染分支此前从不读它，那些文字对玩家完全不存在。
            // 不能用 ModeHUI.CreateBody——它铺满整个面板高度，会盖在卡片上；
            // 这里按行列表同款写法给一条限高的说明行。
            if (showBody && !string.IsNullOrEmpty(content.Body))
            {
                GameObject bodyRow = ZombieModeUIHelper.CreateRect(
                    "ModeH_CardGridBody", surface, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, topY - CardBodyHeight * 0.5f),
                    new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f - 24f, CardBodyHeight),
                    new Vector2(0.5f, 0.5f));
                TextMeshProUGUI bodyText = ZombieModeUIHelper.CreateTMPText(
                    bodyRow, content.Body, 18f, TextAlignmentOptions.Left,
                    BossRushUIColors.TextSecondary);
                BossRushUI.ApplyGameFont(bodyText);
                topY -= CardBodyHeight + CardGap;
            }

            int count = content.Cards.Count;
            if (count == 0) return;

            float usableWidth = panelSize.x - ModeHUI.SafeMargin * 2f - 20f;
            int columns = count <= 3 ? Math.Max(1, count) : 3;

            // 行数必须放得下，否则末行会压到底部动作行上。
            // 入口页就是这么翻车的：ShowRealStakeNotice 把 topY 压到 226，
            // 5 张选秀卡按 3 列排成两行，第 2 行落在 y∈[-398,-98]，
            // 而动作行在 y∈[-382,-326] —— 第 4、5 张卡直接被画在按钮底下。
            // 放不下就加列（卡片变窄），而不是继续往下堆。
            float floorY = -panelSize.y * 0.5f + GetFloorReserve(panelSize, content, GetActionBandReserve(panelSize, content));
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

            // 加列到极限仍放不下时进滚动容器。名人堂就是这种情形：32 席上限，
            // 约 10 条之后剩下的全被画到面板与屏幕之外，玩家既看不到也点不到
            // （模态 surface 上没有 Mask，越界不会被裁掉）。
            // 复用同文件 CreateLineList 已经在用的 CreateScrollHost：官方 ScrollRect
            // prefab 优先，不可用时回退手搓 RectMask2D + ScrollRect。
            int finalRows = (count + columns - 1) / columns;
            float gridHeight = finalRows * CardHeight + Mathf.Max(0, finalRows - 1) * CardGap;
            Transform host = surface;
            if (gridHeight > availableHeight)
            {
                host = CreateScrollHost(
                    surface, panelSize, topY, availableHeight, gridHeight).transform;
                topY = gridHeight * 0.5f;
            }

            for (int i = 0; i < count; i++)
            {
                ModeHCardData data = content.Cards[i];
                if (data == null) continue;
                int column = i % columns;
                int row = i / columns;
                Vector2 position = new Vector2(
                    startX + column * (cardWidth + CardGap),
                    topY - CardHeight * 0.5f - row * (CardHeight + CardGap));

                Color accent = data.IsSelected
                    ? BossRushUIColors.WarningText
                    : data.IsAnomaly
                        ? BossRushUIColors.Warning
                        : ModeHUI.ResolveRarityColor(data.GameQuality);
                GameObject card = BossRushUI.CreateCard(
                    "ModeH_Card_" + i, host, position,
                    new Vector2(cardWidth, CardHeight),
                    BossRushUIColors.SurfaceRaised, accent, true);

                if (data.IsSelected) AddSelectedBadge(card.transform, data.SelectedBadge);
                // 有立绘键（名人堂冠军，审查 B-18）或物品图标时卡片顶上画一小块图，文字整体下移；立绘链与选人卡同一条
                bool hasPortrait = !string.IsNullOrEmpty(data.PortraitKey) || data.Icon != null;
                float shift = hasPortrait ? GridPortraitSize + 10f : 0f;
                if (hasPortrait) CreatePortrait(card.transform, data, new Vector2(0f, -14f), GridPortraitSize);
                // 标题 24 号配 40 高（至少 1.45×24+4≈39）：旧版 26 号塞 34 高，自动缩字缩到和副标题同级（UB-35）
                CreateCardText(card.transform, "Title", data.Title, 24f,
                    BossRushUIColors.TextPrimary, cardWidth, 134f - shift, 40f);
                CreateCardText(card.transform, "Subtitle", data.Subtitle, 18f,
                    BossRushUIColors.Accent, cardWidth, 90f - shift, 30f);
                // 正文下沿让开卡底的按钮（没有按钮时只留边距）
                float bodyHeight = hasPortrait
                    ? (52f - shift) + CardHeight * 0.5f - (data.OnClick != null ? 60f : 14f)
                    : 136f;
                CreateCardText(card.transform, "Body", data.Body, 17f,
                    BossRushUIColors.TextSecondary, cardWidth, 52f - shift, bodyHeight);
                if (!content.Refresh) BossRushUIEntranceAnimation.Play(card, 0.04f * Mathf.Min(i, MaxAnimatedRows), 0.26f, 14f);

                if (data.OnClick == null) continue;
                UnityEngine.Events.UnityAction pick = new UnityEngine.Events.UnityAction(data.OnClick);
                MakeCardClickable(card.transform, pick);
                Button action = ZombieModeUIHelper.CreateButton(
                    "ModeH_CardAction_" + i, card.transform,
                    data.ActionLabel != null
                        ? data.ActionLabel
                        : L10n.T(ModeHConfig.LocalizationKeyPrefix + "Button_Confirm"),
                    new Vector2(0.5f, 0f), new Vector2(0f, 34f),
                    new Vector2(cardWidth - 40f, 44f), BossRushUIColors.SurfaceRaised, 18f,
                    new Vector2(cardWidth - 56f, 32f),
                    pick, true);
                StyleOutlineButton(action, BossRushUIColors.Accent);
            }
        }

        private static void CreateCardText(
            Transform parent, string name, string value, float fontSize, Color color,
            float cardWidth, float offsetY, float height)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(
                name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, offsetY), new Vector2(cardWidth - 40f, height),
                new Vector2(0.5f, 1f));
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
        /// 大字只放赔率本身（小字标签 + 警示金大数字），双方公开分、筹码与当前下注降到第三行次级小字（审查 UB-34）。
        /// </summary>
        private static void CreateOddsPage(
            Transform surface, Vector2 panelSize, ModeHPageContent content, float topY)
        {
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;
            float listTop;
            if (!string.IsNullOrEmpty(content.HeadlineValue))
            {
                // 单行框高 ≥ 1.45×字号+4 再加共享的上下 2px 边距：18 号 → 34，17 号 → 32（审查 B-12：旧版都是 28）
                CreateOddsText(surface, "ModeH_OddsHeadline", content.Headline, 18f,
                    BossRushUIColors.TextSecondary, width, topY - 17f, 34f);
                CreateOddsText(surface, "ModeH_OddsValue", content.HeadlineValue, 46f,
                    BossRushUIColors.WarningText, width, topY - 62f, 72f);
                CreateOddsText(surface, "ModeH_OddsNote", content.Body, 17f,
                    BossRushUIColors.TextSecondary, width, topY - 114f, 32f);
                listTop = topY - 140f;
            }
            else
            {
                // 整备不可用这类兜底：只有一句说明，按正文排（可以折行）
                TextMeshProUGUI note = CreateOddsText(surface, "ModeH_OddsHeadline", content.Body, 20f,
                    BossRushUIColors.TextSecondary, width, topY - 36f, 72f);
                note.enableWordWrapping = true;
                listTop = topY - 84f;
            }

            // 押品选择器不可用（出击地图上仓库不存在，2026-09-24 起押注改押钱）时整块不画、赔率拆解占满宽：
            // 不挂点不动的占位区（§4.14）
            if (!content.RealStakeSelectorEnabled)
            {
                CreateLineList(surface, panelSize, content, listTop, float.PositiveInfinity, 0f, 0f, false);
                return;
            }

            // 赔率拆解与押品选择器各占一列，长列表不能画到押品按钮底下。
            CreateLineList(surface, panelSize, content, listTop, float.PositiveInfinity,
                panelSize.x - ModeHUI.SafeMargin * 2f - StakeSelectorSize.x - CardGap,
                -(StakeSelectorSize.x + CardGap) * 0.5f, false);

            // 押品选择器：与虚拟下注并排，禁用时在原位显示具体原因。卡片档底图 + 描边（旧版按钮档、无边，对面板底约 1.03:1）
            GameObject selector = ZombieModeUIHelper.CreateRect(
                "ModeH_RealStakeSelector", surface,
                new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-(ModeHUI.SafeMargin + StakeSelectorSize.x * 0.5f),
                    ModeHUI.SafeMargin + StakeSelectorSize.y * 0.5f + 72f),
                StakeSelectorSize, new Vector2(0.5f, 0.5f));
            Image selectorBackground = selector.AddComponent<Image>();
            selectorBackground.color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyFramedPanelSkin(selectorBackground, 10, BossRushUISkinPart.Card);

            string selectorText = content.RealStakeSelectorEnabled
                ? L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_NotSelected")
                : (content.RealStakeDisabledReason != null
                    ? content.RealStakeDisabledReason
                    : L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_Disabled"));

            // 标题行固定在选择器顶部（分区标题主色 + 一行次级小字），余下高度留给押品格滚动区
            GameObject selectorTextObj = ZombieModeUIHelper.CreateRect(
                "Text", selector.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(SelectorHeaderHeight * 0.5f + 8f)),
                new Vector2(StakeSelectorSize.x - 28f, SelectorHeaderHeight),
                new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                selectorTextObj,
                L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_Selector") + "\n" + SelectorNoteOpen
                    + selectorText + SelectorNoteClose,
                18f, TextAlignmentOptions.TopLeft, BossRushUIColors.TextPrimary);
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
        /// 每格是深色底 + 描边的次级按钮；选中格底色染一点危险色、描边与字换危险亮色（旧版一整列实心红，选中只差一个 ●）。
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
                // 空白处滚轮 + 可拖动滑块与其它滚动区一致；40 个押品候选靠这条才够得到。
                BossRushUI.ConfigureScrollRect(scrollRect);

                host = scrollContent.transform;
                topY = 0f;
            }

            for (int i = 0; i < count; i++)
            {
                ModeHActionData slot = content.RealStakeSlots[i];
                if (slot == null) continue;
                float y = topY - SlotButtonHeight * 0.5f - i * (SlotButtonHeight + SlotGap);
                Button button = ZombieModeUIHelper.CreateButton(
                    "ModeH_RealStakeSlot_" + i, host, slot.Label,
                    new Vector2(0.5f, 1f), new Vector2(0f, y),
                    new Vector2(slotWidth, SlotButtonHeight),
                    BossRushUIColors.SurfaceRaised,
                    16f, new Vector2(slotWidth - 16f, SlotButtonHeight - 8f),
                    slot.OnClick != null
                        ? new UnityEngine.Events.UnityAction(slot.OnClick)
                        : null,
                    slot.Interactable);
                if (slot.IsSelected)
                {
                    StyleSelectedButton(button, BossRushUIColors.Danger, BossRushUIColors.DangerText);
                    SetLabelColor(button, BossRushUIColors.DangerText);
                }
                else
                {
                    BossRushUIKit.StyleSecondaryButton(button);
                    if (!slot.Interactable) SetLabelColor(button, BossRushUIColors.TextSecondary);
                }
            }
        }

        #endregion

        #region 行列表

        /// <summary>
        /// 逐行列表。行数超出可视范围时放进复用的 `ScrollRect`，
        /// 不按内容实时扩容，也不每帧重建布局。
        ///
        /// 整张列表是一张浮起来的「战报单」（卡片底 + 描边），行与行之间一条细分隔线；
        /// 「键：值」的行按两栏排（左栏次级小字、右栏主色正文），其余按段落排；
        /// 自动处理说明行（NoteLines）行首一道警示色细条（审查 UB-03：旧版一列同号灰字）。
        /// </summary>
        private static void CreateLineList(
            Transform surface, Vector2 panelSize, ModeHPageContent content, float topY,
            float maximumHeight = float.PositiveInfinity, float width = 0f, float offsetX = 0f,
            bool bodyAsLead = true)
        {
            float viewportHeight = Mathf.Min(maximumHeight, Mathf.Max(48f,
                topY - (-panelSize.y * 0.5f + GetFloorReserve(panelSize, content, GetActionBandReserve(panelSize, content)))));
            if (width <= 0f) width = panelSize.x - ModeHUI.SafeMargin * 2f;

            GameObject sheet = BossRushUI.CreateCard("ModeH_LineSheet", surface,
                new Vector2(offsetX, topY - viewportHeight * 0.5f), new Vector2(width, viewportHeight),
                BossRushUIColors.SurfaceRaised, BossRushUIColors.Accent, false);
            Image sheetImage = sheet.GetComponent<Image>();
            if (sheetImage != null) sheetImage.raycastTarget = false;

            Vector2 listSize = new Vector2(width + ModeHUI.SafeMargin * 2f, panelSize.y);
            GameObject host = CreateScrollHost(surface, listSize, topY, viewportHeight, viewportHeight);
            // 官方 prefab 的 content 可能位于嵌套 viewport 下，用 ScrollRect 根定位。
            ScrollRect scroll = host.GetComponentInParent<ScrollRect>();
            if (scroll != null)
            {
                RectTransform scrollRect = scroll.GetComponent<RectTransform>();
                scrollRect.anchoredPosition += new Vector2(offsetX, 0f);
            }

            float rowWidth = width - 56f;
            bool animate = !content.Refresh;
            float used = ListPadding;
            int index = 0;
            // 看盘页的「第 N 场 / 6」这类一句话正文作首行；结算页的胜负已经画在横幅上，不重复
            if (bodyAsLead && content.ResultTone == ModeHResultTone.None && !string.IsNullOrEmpty(content.Body))
            {
                used = CreateListRow(host.transform, content.Body, rowWidth, used, index++, animate, RowKind.Lead);
            }
            for (int i = 0; i < content.Lines.Count; i++)
            {
                used = CreateListRow(host.transform, content.Lines[i], rowWidth, used, index++, animate, RowKind.Line);
            }
            for (int i = 0; i < content.NoteLines.Count; i++)
            {
                used = CreateListRow(host.transform, content.NoteLines[i], rowWidth, used, index++, animate, RowKind.Note);
            }
            host.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, Mathf.Max(viewportHeight, used + ListPadding));
        }

        private enum RowKind { Line, Lead, Note }

        /// <summary>排一行（两栏键值 / 段落 / 说明），返回下一行的顶边。行名沿用 ModeH_Line_&lt;i&gt;。</summary>
        private static float CreateListRow(Transform host, string line, float width, float top, int index,
            bool animate, RowKind kind)
        {
            if (index > 0)
            {
                GameObject rule = ZombieModeUIHelper.CreateSeparator("ModeH_LineRule_" + index, host,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(top - LineRowGap * 0.5f)),
                    1f, BossRushUIColors.Divider);
                RectTransform ruleRect = rule.GetComponent<RectTransform>();
                ruleRect.sizeDelta = new Vector2(width, ruleRect.sizeDelta.y);
            }

            GameObject row = ZombieModeUIHelper.CreateRect(
                "ModeH_Line_" + index, host, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -top), new Vector2(width, LineHeight), new Vector2(0.5f, 1f));
            float height;
            string key;
            string value;
            if (kind == RowKind.Line && SplitKeyValue(line, out key, out value))
            {
                float keyWidth = Mathf.Min(KeyColumnMaxWidth, width * 0.34f);
                float valueWidth = width - keyWidth - 16f;
                TextMeshProUGUI keyText = CreateRowText(row.transform, "Key", key, 16f,
                    BossRushUIColors.TextSecondary, 0f, -2f, keyWidth);
                TextMeshProUGUI valueText = CreateRowText(row.transform, "Value", value, 18f,
                    BossRushUIColors.TextPrimary, keyWidth + 16f, 0f, valueWidth);
                height = Mathf.Max(BossRushUI.MeasureTextHeight(keyText, keyWidth, LineHeight) + 2f,
                    BossRushUI.MeasureTextHeight(valueText, valueWidth, LineHeight));
            }
            else
            {
                float inset = 0f;
                if (kind == RowKind.Note)
                {
                    inset = NoteRailInset;
                    GameObject rail = ZombieModeUIHelper.CreateRect("Rail", row.transform,
                        new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(3f, -4f),
                        new Vector2(0f, 0.5f));
                    Image railImage = rail.AddComponent<Image>();
                    railImage.color = BossRushUIColors.WarningText;
                    railImage.raycastTarget = false;
                    BossRushUI.ApplyPanelSkin(railImage, 2, BossRushUISkinPart.Hairline);
                }
                TextMeshProUGUI text = CreateRowText(row.transform, "Text", line,
                    kind == RowKind.Lead ? 18f : 16f,
                    kind == RowKind.Lead ? BossRushUIColors.TextPrimary : BossRushUIColors.TextSecondary,
                    inset, 0f, width - inset);
                height = BossRushUI.MeasureTextHeight(text, width - inset, LineHeight);
            }
            row.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            if (animate && index < MaxAnimatedRows)
            {
                BossRushUIEntranceAnimation.Play(row, 0.04f * index, 0.24f, 8f);
            }
            return top + height + LineRowGap;
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
                    official.content.anchorMin = official.content.anchorMax = official.content.pivot = new Vector2(0.5f, 1f);
                    official.content.anchoredPosition = new Vector2(-10f, 0f);
                    official.content.sizeDelta = new Vector2(rect.sizeDelta.x - 20f, contentHeight);
                    BossRushUI.ConfigureScrollRect(official);
                    return official.content.gameObject;
                }
                UnityEngine.Object.Destroy(official.gameObject);
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
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-10f, 0f),
                new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f - 20f, contentHeight),
                new Vector2(0.5f, 1f));
            scroll.content = contentRoot.GetComponent<RectTransform>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            BossRushUI.ConfigureScrollRect(scroll);
            return contentRoot;
        }

        /// <summary>
        /// 整备选项：每行一张可点的卡（深色底 + 描边）；标签按「\n」拆成标题（主色左对齐）+ 说明（次级小字左对齐）；
        /// 选中行底色染一点主色、描边换主色，右上角「√ 已选」（审查 UB-11：旧版按钮与面板同色无边、两行字居中漂着，选中只差行首一个 √）。
        /// </summary>
        private static void CreatePreparationOptions(Transform surface, Vector2 panelSize,
            ModeHPageContent content, float topY)
        {
            const float rowHeight = 112f;
            float height = topY + panelSize.y * 0.5f - GetFloorReserve(panelSize, content, GetActionBandReserve(panelSize, content));
            GameObject host = CreateScrollHost(surface, panelSize, topY, height,
                Math.Max(height, content.PreparationOptions.Count * rowHeight));
            RectTransform rect = host.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(-10f, 0f);
            float width = panelSize.x - ModeHUI.SafeMargin * 2f - 40f;
            for (int i = 0; i < content.PreparationOptions.Count; i++)
            {
                CreatePreparationRow(host.transform, content.PreparationOptions[i], i, width, rowHeight, !content.Refresh);
            }
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

        private static int GetActionRows(Vector2 panelSize, ModeHPageContent content)
        {
            float width = panelSize.x - ModeHUI.SafeMargin * 2f;
            int perRow = Mathf.Min(MaxSingleRowActions, Mathf.Max(1,
                Mathf.FloorToInt((width + CardGap) / (ActionSize.x + CardGap))));
            return Mathf.Max(1, (content.Actions.Count + perRow - 1) / perRow);
        }

        private static float GetActionBandReserve(Vector2 panelSize, ModeHPageContent content)
        {
            int rows = GetActionRows(panelSize, content);
            return ActionBandReserve + (rows - 1) * (ActionSize.y + CardGap);
        }

        /// <summary>按钮带顶边到面板底边的距离。</summary>
        private static float GetActionBandTop(Vector2 panelSize, ModeHPageContent content)
        {
            int rows = GetActionRows(panelSize, content);
            return ModeHUI.SafeMargin + rows * ActionSize.y + (rows - 1) * CardGap;
        }

        /// <summary>
        /// 底部动作按钮。按钮的可交互性由调用方按当前状态与 owner token 决定。
        ///
        /// 超过 `MaxSingleRowActions` 时自动换行向上堆叠，而不是继续往两侧铺开。
        /// 模态 surface 上没有 Mask，越界按钮不会被裁掉而是画到屏幕外点不到；
        /// 排在最后的往往正是「锁盘」「确认」这类唯一出口，一旦推出屏幕
        /// 玩家就困在 timeScale=0 的模态页里了。
        /// 常规页面本来就该 ≤7 个按钮（数量随玩家状态变化的走独立滚动区），
        /// 这里的换行是**兜底**，不是让调用方随便加按钮的许可。
        ///
        /// 主次（审查 UB-09）：主操作 AccentFill 实心、危险操作 Danger 实心、选中档染主色 + 主色描边，
        /// 其余一律次级（深色底 + 描边）；不可点的按次级画、字用次级色。
        /// </summary>
        private static void CreateActions(
            Transform surface, Vector2 panelSize, ModeHPageContent content)
        {
            int count = content.Actions.Count;
            if (count == 0) return;
            // 顺序（UI 制作共识第 4 节）：危险的次级操作排最左、与安全按钮拉开；主操作排最右；其余居中保持原序
            List<ModeHActionData> actions = OrderActions(content.Actions);

            // 每行容量按**本页面板宽度**算，而不是写死一个数：结算页用的是更窄的
            // ReportPanelSize(1180)，同样 5 个按钮在主面板(1480)里放得下、在结算页就出框。
            // 再与 MaxSingleRowActions 取小，兜住"面板比屏幕还宽"的假设失效。
            float step = ActionSize.x + CardGap;
            float usableWidth = panelSize.x - ModeHUI.SafeMargin * 2f;
            int fitPerRow = Mathf.Max(1, Mathf.FloorToInt((usableWidth + CardGap) / step));
            int perRow = Mathf.Min(count, Mathf.Min(fitPerRow, MaxSingleRowActions));
            float rowStride = ActionSize.y + CardGap;
            int primary = ResolvePrimaryAction(actions);

            for (int i = 0; i < count; i++)
            {
                ModeHActionData action = actions[i];
                if (action == null) continue;
                int column = i % perRow;
                int row = i / perRow;
                // 末行按本行实际按钮数居中（审查 B-17：旧版按整行网格左对齐，末尾那颗落在最左边）
                int inRow = Mathf.Min(perRow, count - row * perRow);
                float startX = -((inRow - 1) * step) * 0.5f;
                // 末行贴底，早先的行往上堆：最后一颗按钮永远在最容易够到的位置
                int totalRows = (count + perRow - 1) / perRow;
                float y = ModeHUI.SafeMargin + ActionSize.y * 0.5f
                    + (totalRows - 1 - row) * rowStride;
                bool isPrimary = i == primary;
                Button button = ZombieModeUIHelper.CreateButton(
                    "ModeH_Action_" + i, surface, action.Label,
                    new Vector2(0.5f, 0f),
                    new Vector2(startX + column * step, y),
                    ActionSize,
                    ResolveActionFill(action, isPrimary),
                    20f, new Vector2(ActionSize.x - 16f, ActionSize.y - 16f),
                    action.OnClick != null
                        ? new UnityEngine.Events.UnityAction(action.OnClick)
                        : null,
                    action.Interactable);
                StyleAction(button, action, isPrimary);
            }
        }

        #endregion

        #region 布局常量

        private const float NoticeHeight = 64f;
        /// <summary>页脚风险行：14 号字两行（1.45×14×2+4≈45）。</summary>
        private const float CompactNoticeHeight = 46f;
        private const float CompactNoticeBottom = 20f;
        private const float ChampionCardMaxWidth = 360f;
        private const float ChampionCardMaxHeight = 560f;
        private const float ChampionCardGap = 16f;
        private const float ChampionCardPadding = 16f;
        /// <summary>选人卡名字行：26 号字单行至少 1.45×26+4≈42。</summary>
        private const float ChampionNameHeight = 44f;
        /// <summary>选人卡定位行：18 号字单行至少 1.45×18+4≈30。</summary>
        private const float ChampionRoleHeight = 32f;
        private const float ChampionButtonHeight = 44f;
        /// <summary>整卡悬停时底色向描边色提亮的比例（大面积卡片不能像按钮那样往白里提 0.22）。</summary>
        private const float CardHoverLift = 0.22f;
        /// <summary>选中档底色染主色的比例（与 Mode G 契约卡同口径）。</summary>
        private const float SelectedTint = 0.16f;
        /// <summary>立绘缺失时徽记的淡显乘色。</summary>
        private static readonly Color PortraitEmblemTint = new Color(1f, 1f, 1f, 0.45f);
        private const float CardMaxWidth = 420f;
        /// <summary>卡片最窄宽度：再窄标题就折行折到不可读，宁可继续往下排。</summary>
        private const float CardMinWidth = 220f;
        private const float CardHeight = 300f;
        /// <summary>卡片栅格里的小立绘（名人堂冠军）边长。</summary>
        private const float GridPortraitSize = 72f;

        /// <summary>页头横幅：距面板顶边的内缩、圆角与插图焦点（原图从上往下 0.44 是两只鸭子的头与看台灯光之间）。</summary>
        private const float HeroInset = 10f;
        private const int HeroRadius = 10;
        private const float HeroFocusY = 0.44f;
        private const float EntryHeroHeight = 100f;
        private const float ResultHeroHeight = 140f;
        /// <summary>结算页胜负大字：40 号。</summary>
        private const float ResultFontSize = 40f;
        /// <summary>横幅缺失时的胜负卡高度（36 号字单行至少 1.45×36+4≈56）。</summary>
        private const float ResultCardHeight = 72f;

        /// <summary>
        /// 底部动作行的保留高度。行列表与卡片网格都按这个数留白，
        /// 三处必须用同一个常量，否则又会出现"某一处以为下面是空的"的重叠。
        /// </summary>
        private const float ActionBandReserve = ModeHUI.SafeMargin + 96f;
        private const float CardGap = 24f;
        /// <summary>列表单行最小高度（18 号字单行至少 1.45×18+4≈30）。</summary>
        private const float LineHeight = 32f;
        /// <summary>列表行间距（分隔线画在正中）。</summary>
        private const float LineRowGap = 12f;
        /// <summary>列表卡片上下内边距。</summary>
        private const float ListPadding = 14f;
        /// <summary>两栏键值行左栏的最大宽度。</summary>
        private const float KeyColumnMaxWidth = 240f;
        /// <summary>「键」最多几个字：再长就是一句话，按段落排。</summary>
        private const int KeyMaxChars = 18;
        /// <summary>自动处理说明行：细条之后文字的缩进。</summary>
        private const float NoteRailInset = 14f;
        /// <summary>入场动画最多给前几行 / 前几张卡（再往后错峰就太慢了）。</summary>
        private const int MaxAnimatedRows = 10;
        /// <summary>整备选项标题行框高：19 号字单行至少 1.45×19+4≈32。</summary>
        private const float PrepTitleHeight = 32f;
        /// <summary>「√ 已选」角标高度：13 号字单行至少 1.45×13+4≈23。</summary>
        private const float SelectedBadgeHeight = 24f;

        /// <summary>卡片栅格顶部说明行的高度（page.Body 用）。</summary>
        private const float CardBodyHeight = 56f;
        private static readonly Vector2 ActionSize = new Vector2(240f, 56f);
        private static readonly Vector2 StakeSelectorSize = new Vector2(420f, 220f);
        /// <summary>押品选择器顶部标题行高度，余下高度归押品格滚动区。</summary>
        private const float SelectorHeaderHeight = 56f;
        private const float SlotButtonHeight = 34f;
        private const float SlotGap = 6f;
        /// <summary>押品选择器第二行（状态 / 禁用原因）的次级小字（按 token 预先转好）。</summary>
        private static readonly string SelectorNoteOpen =
            "<size=15><color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary) + ">";
        private const string SelectorNoteClose = "</color></size>";

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
