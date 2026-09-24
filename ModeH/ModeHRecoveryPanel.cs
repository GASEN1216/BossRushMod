using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// Mode H 恢复 / `Suspended` 壳（设计提案 §23.1、§22.4、§25.1）。
    ///
    /// 冻结契约：
    /// - 使用 `BossRushUILayers.ModeHRecovery` 层（压住奖励揭晓层）；
    /// - 显示前**先终止奖励揭晓层**；显示期间占住模态输入租约（审查 B-10：旧版全屏遮罩挡点击、角色却还能跑），
    ///   收起时释放；动作里恒有一颗「稍后处理」，玩家不会被锁在这一页；
    /// - 显示 report、技术中止、同场重开与 season reward operation；
    /// - txId、journal 阶段、库存核对与资产恢复操作**仅在真实资产路径存在时**显示，
    ///   无法证明安全时全部只读；
    /// - 面板尺寸固定 `1280x780`，四周安全边距至少 48。
    /// </summary>
    internal sealed class ModeHRecoveryPanel
    {
        #region 状态

        private Canvas _canvas;
        private GameObject _root;
        private TextMeshProUGUI _body;
        private RectTransform _bodyContent;
        private string _lastBody;
        private ZombieModeUIHelper.ModalInputLease _lease;
        /// <summary>ESC / 手柄取消 =「稍后处理」（标了 IsCancel 的那颗，2026-09-24）。</summary>
        private PetNestCancelKey _cancelKey;
        /// <summary>当前显示中的恢复壳（鸭王杯页面的 ESC 被它盖住时让出）。</summary>
        private static ModeHRecoveryPanel _shown;

        #endregion

        #region 只读

        /// <summary>恢复壳是否已显示。</summary>
        public bool IsVisible { get { return _root != null; } }

        /// <summary>有没有任何一个恢复壳正显示着。</summary>
        internal static bool AnyVisible { get { return _shown != null && _shown._root != null; } }

        #endregion

        #region 显示

        /// <summary>
        /// 显示恢复壳。`allowActions=false` 时全部动作按钮置灰（只读展示）。
        /// 重复调用只刷新内容，不重建面板。
        /// </summary>
        /// <param name="stopRewardAnimation">
        /// 由调用方提供的「终止奖励揭晓」回调。必须只终止**本模式自己**播放的那一个实例：
        /// 此前这里用 FindObjectsOfType 全场扫 WishFountainRewardAnimationView 并全部销毁，
        /// 会把原版许愿池正在放的奖励动画一并干掉。
        /// </param>
        public void Show(
            string headline,
            IList<string> lines,
            IList<ModeHActionData> actions,
            bool allowActions,
            Action stopRewardAnimation)
        {
            // 先终止奖励揭晓层：恢复壳必须压住它，且不允许它继续接收输入
            StopRewardAnimation(stopRewardAnimation);

            if (_root == null) CreatePanel(headline);
            UpdateLines(lines);
            RebuildActions(actions, allowActions);
        }

        private void CreatePanel(string headline)
        {
            _canvas = BossRushUI.CreateCanvasRoot(
                "ModeH_Recovery", BossRushUILayers.ModeHRecovery, true);
            _root = _canvas.gameObject;
            UnityEngine.Object.DontDestroyOnLoad(_root);
            BossRushUI.CreateBackdrop(_root.transform);

            // 根上已经有一张 Backdrop，这里不能再叠：两张 0.62 合成 0.856，
            // 恢复壳会暗到看不清后面的场景（与 ModeHUI.OpenPage 同一处理）。
            GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                "ModeH_RecoverySurface", _root.transform,
                ModeHUI.RecoverySize, BossRushUIColors.Warning, createBackdrop: false);

            ModeHUI.CreateTitle(surface.transform,
                !string.IsNullOrEmpty(headline)
                    ? headline
                    : L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Recovery"),
                ModeHUI.RecoverySize);
            _body = CreateScrollingBody(surface.transform);
            // 全屏遮罩挡住了点击，就要同时挡住角色输入：占模态租约，收起时释放（审查 B-10）
            _lease = ZombieModeUIHelper.ClaimModalInput(_root, "ModeHRecovery");
            _shown = this;
            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>
        /// 正文放进可滚动的区域（审查 B-10：旧版一整块自动缩字，战报与奖励一多字就被压得很小）。
        /// 占用与 ModeHUI.CreateBody 相同的矩形；字号固定，按实测高度撑开内容。
        /// </summary>
        private TextMeshProUGUI CreateScrollingBody(Transform surface)
        {
            Vector2 size = ModeHUI.RecoverySize;
            float top = size.y * 0.5f - ModeHUI.SafeMargin - 76f;
            float bottom = -size.y * 0.5f + ModeHUI.SafeMargin + 96f;
            float width = size.x - ModeHUI.SafeMargin * 2f;
            GameObject viewport = ZombieModeUIHelper.CreateRect("ModeH_BodyScroll", surface,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, (top + bottom) * 0.5f),
                new Vector2(width, top - bottom), new Vector2(0.5f, 0.5f));
            viewport.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            GameObject content = ZombieModeUIHelper.CreateRect("ModeH_Body", viewport.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-10f, 0f),
                new Vector2(width - 20f, top - bottom), new Vector2(0.5f, 1f));
            scroll.content = content.GetComponent<RectTransform>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            BossRushUI.ConfigureScrollRect(scroll);
            _bodyContent = scroll.content;
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(
                content, string.Empty, BodyFontSize, TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
            text.enableAutoSizing = false;
            text.fontSize = BodyFontSize;
            BossRushUI.ApplyGameFont(text);
            return text;
        }

        private void UpdateLines(IList<string> lines)
        {
            if (_body == null) return;
            string joined = lines != null && lines.Count > 0
                ? string.Join("\n", ToArray(lines))
                : string.Empty;
            if (string.Equals(joined, _lastBody, StringComparison.Ordinal)) return;
            _lastBody = joined;
            _body.text = joined;
            if (_bodyContent != null)
            {
                float height = BossRushUI.MeasureTextHeight(_body, _bodyContent.rect.width, 32f);
                _bodyContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
        }

        private static string[] ToArray(IList<string> lines)
        {
            string[] array = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                array[i] = lines[i] != null ? lines[i] : string.Empty;
            }
            return array;
        }

        private void RebuildActions(IList<ModeHActionData> actions, bool allowActions)
        {
            if (_root == null) return;
            Transform surface = _root.transform.Find("ModeH_RecoverySurface");
            if (surface == null) return;

            for (int i = surface.childCount - 1; i >= 0; i--)
            {
                Transform child = surface.GetChild(i);
                if (child != null && child.name.StartsWith("ModeH_RecoveryAction",
                        StringComparison.Ordinal))
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
            SyncCancelKey(actions, allowActions);
            if (actions == null || actions.Count == 0) return;
            // 危险的「放弃赛季」排最左、主操作「同场重开」排最右（UI 制作共识第 4 节）
            actions = ModeHUIPages.OrderActions(actions);

            // 与 ModeHUIPages.CreateActions 同一纪律：超过每行上限就换行向上堆，
            // 绝不继续往两侧铺。恢复壳是**应急界面**——「取回押品」「结束赛季」这些
            // 按钮被推出屏幕，等于玩家在资产出问题时连补救入口都点不到。
            // 面板宽 1280，步距 284，半宽减 SafeMargin 与半个按钮：
            // (n-1)*142 + 130 ≤ 640 → n ≤ 4。
            int perRow = actions.Count <= MaxSingleRowActions
                ? actions.Count
                : MaxSingleRowActions;
            float step = ActionSize.x + ActionGap;
            float startX = -((perRow - 1) * step) * 0.5f;
            float rowStride = ActionSize.y + ActionGap;
            int totalRows = (actions.Count + perRow - 1) / perRow;

            // 主次与页面动作行同一口径（审查 UB-09）：「同场重开」这类唯一的前进操作是 AccentFill 实心，
            // 「放弃赛季」是 Danger，其余次级（深色底 + 描边）；只读置灰时一律按次级画、字用次级色。
            int primary = ModeHUIPages.ResolvePrimaryAction(actions);
            for (int i = 0; i < actions.Count; i++)
            {
                ModeHActionData action = actions[i];
                if (action == null) continue;
                bool interactable = (allowActions || action.BypassReadOnly)
                    && action.Interactable && action.OnClick != null;
                int column = i % perRow;
                int row = i / perRow;
                float y = ModeHUI.SafeMargin + ActionSize.y * 0.5f
                    + (totalRows - 1 - row) * rowStride;
                // 只读置灰按「不可点」画：借一份只改可交互性的副本去取底色与样式，不改调用方的数据
                ModeHActionData shown = interactable == action.Interactable ? action : new ModeHActionData
                {
                    Label = action.Label, Interactable = interactable, IsDanger = action.IsDanger,
                    IsPrimary = action.IsPrimary, IsSelected = action.IsSelected,
                };
                bool isPrimary = i == primary;
                Button button = ZombieModeUIHelper.CreateButton(
                    "ModeH_RecoveryAction_" + i, surface, action.Label,
                    new Vector2(0.5f, 0f),
                    new Vector2(startX + column * step, y),
                    ActionSize,
                    ModeHUIPages.ResolveActionFill(shown, isPrimary),
                    20f, new Vector2(ActionSize.x - 16f, ActionSize.y - 16f),
                    interactable ? new UnityEngine.Events.UnityAction(action.OnClick) : null,
                    interactable);
                ModeHUIPages.StyleAction(button, shown, isPrimary);
            }
        }

        #endregion

        #region 内容组装

        /// <summary>
        /// 组装恢复壳内容。真实资产明细只有在存在 active journal 时才出现；
        /// 无法证明安全时调用方传 `allowActions=false`，这里只做只读展示。
        ///
        /// 分四组（技术中止 / 本赛季 / 奖励记录 / 押品），每组一行主色小标题、组间空一行（审查 UB-16：
        /// 旧版一整块同号灰字，还夹着英文枚举名「[Offered]」与完整事务号）。奖励状态走中英对照，
        /// 事务号只留末 6 位、用注脚小字——它只用来和日志对得上，玩家不需要读它。
        /// </summary>
        public static List<string> BuildLines(
            ModeHSeasonDto season, ModeHStakeJournalDto journal, string technicalReasonId)
        {
            List<string> lines = new List<string>();

            if (!string.IsNullOrEmpty(technicalReasonId))
            {
                AddGroupHeader(lines, L10n.T("技术中止", "Technical stop"));
                lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_TechnicalAbort"));
                // 正文只讲原因与后果，操作留给按钮（审查 B-20：旧版又写一遍按钮上的「同场重开，不判负」）
                lines.Add(L10n.T("这不是你的错，不算输。", "This is not on you and does not count as a loss."));
            }

            if (season != null && season.runState != null)
            {
                AddGroupHeader(lines, L10n.T("本赛季", "This season"));
                lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_Match")
                    .Replace("{0}", season.runState.matchIndex.ToString())
                    + "  " + ResolveStateLabel((ModeHLifecycle)season.runState.lifecycle));
                if (season.matchReports != null)
                {
                    for (int i = 0; i < season.matchReports.Count; i++)
                    {
                        ModeHMatchReportDto report = season.matchReports[i];
                        if (report == null) continue;
                        lines.Add(BuildReportLine(report));
                    }
                }
            }

            if (season != null && season.seasonRewardOperations != null
                && season.seasonRewardOperations.Count > 0)
            {
                AddGroupHeader(lines, L10n.T("奖励记录", "Rewards"));
                for (int i = 0; i < season.seasonRewardOperations.Count; i++)
                {
                    ModeHSeasonRewardOperationDto operation = season.seasonRewardOperations[i];
                    if (operation == null) continue;
                    lines.Add("· " + L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_Match")
                            .Replace("{0}", operation.matchIndex.ToString())
                        + L10n.T("奖励", " reward") + "  "
                        + ResolveRewardStatusLabel((ModeHSeasonRewardOperationStatus)operation.status));
                }
            }

            // 真实资产明细只在存在 journal 时显示
            if (journal != null)
            {
                AddGroupHeader(lines, L10n.T("押品", "Stake"));
                lines.Add(ResolveStakePhaseLabel((ModeHStakePhase)journal.phase));
                lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_Escrowed")
                    + ": " + (journal.escrowItems != null ? journal.escrowItems.Count : 0));
                if (!ModeHWarehouseStakeJournal.IsSlotConsistent)
                {
                    lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_ManualIntervention"));
                }
                lines.Add(FootnoteOpen + L10n.T("事务号 …", "Transaction …") + Tail(journal.txId, 6) + FootnoteClose);
            }
            return lines;
        }

        /// <summary>组标题：主色、比正文大一号；不是第一组时先空一行。</summary>
        private static void AddGroupHeader(List<string> lines, string title)
        {
            if (lines.Count > 0) lines.Add(string.Empty);
            lines.Add(GroupHeaderOpen + title + GroupHeaderClose);
        }

        private static string Tail(string value, int count)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            return value.Length <= count ? value : value.Substring(value.Length - count);
        }

        private static string ResolveRewardStatusLabel(ModeHSeasonRewardOperationStatus status)
        {
            switch (status)
            {
                case ModeHSeasonRewardOperationStatus.Offered: return L10n.T("待领取", "Not claimed yet");
                case ModeHSeasonRewardOperationStatus.Applied: return L10n.T("已领取", "Claimed");
                case ModeHSeasonRewardOperationStatus.Archived: return L10n.T("已归档", "Archived");
                default: return L10n.T("状态未知", "Unknown");
            }
        }

        /// <summary>富文本色值按 token 预先转好（不每次拼）。</summary>
        private static readonly string GroupHeaderOpen =
            "<size=19><color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextPrimary) + ">";
        private const string GroupHeaderClose = "</color></size>";
        private static readonly string FootnoteOpen = "<size=14>";
        private const string FootnoteClose = "</size>";

        private static string BuildReportLine(ModeHMatchReportDto report)
        {
            string outcome = report.winner == (int)ModeHMatchOutcome.PlayerVictory
                ? L10n.T(ModeHConfig.LocalizationKeyPrefix + "Outcome_Victory")
                : L10n.T(ModeHConfig.LocalizationKeyPrefix + "Outcome_Defeat");
            if (report.timeout) outcome = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Outcome_Timeout");
            if (!string.IsNullOrEmpty(report.cowardiceType))
            {
                outcome = L10n.T(ModeHConfig.LocalizationKeyPrefix + "Outcome_Cowardice");
            }
            return "· " + L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_Match")
                .Replace("{0}", report.matchIndex.ToString()) + "  " + outcome;
        }

        private static string ResolveStateLabel(ModeHLifecycle lifecycle)
        {
            return L10n.T(ModeHConfig.LocalizationKeyPrefix + "State_" + lifecycle);
        }

        private static string ResolveStakePhaseLabel(ModeHStakePhase phase)
        {
            return L10n.T(ModeHConfig.LocalizationKeyPrefix + "StakePhase_" + phase);
        }

        #endregion

        #region 奖励动画与销毁

        /// <summary>
        /// 终止奖励揭晓层。结果事实已提交时，跳过/销毁动画只改变表现，
        /// 不改变任何已提交的结算事实。
        /// </summary>
        /// <remarks>
        /// **不做全场扫描**：`FindObjectsOfType&lt;WishFountainRewardAnimationView&gt;` 会连原版许愿池
        /// 正在播放的奖励动画一起销毁。调用方自己记着它播的是哪一个实例，这里只执行回调。
        /// </remarks>
        private static void StopRewardAnimation(Action stopRewardAnimation)
        {
            try
            {
                if (stopRewardAnimation != null) stopRewardAnimation();
            }
            catch (Exception)
            {
                // 奖励动画已销毁或回调失败：恢复壳照常显示
            }
        }

        /// <summary>幂等收起恢复壳（淡出，审查 UB-32）。</summary>
        public void Hide()
        {
            Hide(false);
        }

        /// <summary>
        /// 幂等收起恢复壳。<paramref name="immediate"/> 为真时立即销毁（关停 / 卸载路径：展示 bundle 紧接着可能被卸载，
        /// 标题徽记不能还挂在淡出中的界面上）。引用先置空，重开会新建根，不复用淡出中的这一个。
        /// </summary>
        public void Hide(bool immediate)
        {
            if (_root == null) return;
            GameObject root = _root;
            _root = null;
            _canvas = null;
            _body = null;
            _bodyContent = null;
            _lastBody = null;
            DetachCancelKey();
            if (ReferenceEquals(_shown, this)) _shown = null;
            ReleaseLease();
            if (immediate)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }
            root.name = root.name + "_Closing";
            BossRushUIKit.PlayCloseAndDestroy(root);
        }

        #endregion

        /// <summary>ESC =「稍后处理」：只认可点的、标了 IsCancel 的那颗；共享确认框盖在上面时让给它。</summary>
        private void SyncCancelKey(IList<ModeHActionData> actions, bool allowActions)
        {
            Action cancel = null;
            for (int i = 0; actions != null && i < actions.Count; i++)
            {
                ModeHActionData action = actions[i];
                if (action == null || !action.IsCancel || action.OnClick == null || !action.Interactable) continue;
                if (!allowActions && !action.BypassReadOnly) continue;
                cancel = action.OnClick;
                break;
            }
            if (cancel == null || _root == null)
            {
                DetachCancelKey();
                return;
            }
            _cancelKey = PetNestCancelKey.Attach(_root, cancel, () => BossRushConfirmDialog.IsOpen);
        }

        private void DetachCancelKey()
        {
            if (_cancelKey == null) return;
            try { _cancelKey.Detach(); }
            catch (Exception) { /* 画布已销毁：订阅随组件 OnDestroy 退掉 */ }
            _cancelKey = null;
        }

        private void ReleaseLease()
        {
            try
            {
                if (_lease != null) _lease.Release();
            }
            catch (Exception)
            {
                // 释放失败也要丢引用，避免二次 Release
            }
            _lease = null;
        }

        #region 布局常量

        /// <summary>正文字号：固定，不自动缩（放不下就滚动）。</summary>
        private const float BodyFontSize = 18f;
        private static readonly Vector2 ActionSize = new Vector2(260f, 56f);
        private const float ActionGap = 24f;

        /// <summary>
        /// 恢复壳单行动作区的按钮数上限。
        /// 面板宽 `ModeHUI.RecoverySize.x` = 1280，步距 260+24 = 284：
        /// (n-1)*142 + 130 ≤ 640 - SafeMargin(48) → n ≤ 4。
        /// 超出即换行，由 ModeHActionLayoutGuard 守卫。
        /// </summary>
        private const int MaxSingleRowActions = 4;

        #endregion
    }
}
