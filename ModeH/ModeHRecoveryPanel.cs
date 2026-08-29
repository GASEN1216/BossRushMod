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
    /// - 显示前**先终止奖励揭晓层**，保留同一 modal lease，
    ///   不允许背景页面继续接收按钮；
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
        private string _lastBody;

        #endregion

        #region 只读

        /// <summary>恢复壳是否已显示。</summary>
        public bool IsVisible { get { return _root != null; } }

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

            GameObject surface = ZombieModeUIHelper.CreateModalSurface(
                "ModeH_RecoverySurface", _root.transform,
                ModeHUI.RecoverySize, BossRushUIColors.Warning);

            ModeHUI.CreateTitle(surface.transform,
                !string.IsNullOrEmpty(headline)
                    ? headline
                    : L10n.T(ModeHConfig.LocalizationKeyPrefix + "Page_Recovery"),
                ModeHUI.RecoverySize);
            _body = ModeHUI.CreateBody(surface.transform, string.Empty, ModeHUI.RecoverySize, 0f);
            BossRushUI.PlayOpenAnimation(surface);
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
            if (actions == null || actions.Count == 0) return;

            float startX = -((actions.Count - 1) * (ActionSize.x + ActionGap)) * 0.5f;
            for (int i = 0; i < actions.Count; i++)
            {
                ModeHActionData action = actions[i];
                if (action == null) continue;
                bool interactable = allowActions && action.Interactable && action.OnClick != null;
                ZombieModeUIHelper.CreateButton(
                    "ModeH_RecoveryAction_" + i, surface, action.Label,
                    new Vector2(0.5f, 0f),
                    new Vector2(startX + i * (ActionSize.x + ActionGap),
                        ModeHUI.SafeMargin + ActionSize.y * 0.5f),
                    ActionSize,
                    interactable
                        ? (action.IsDanger ? BossRushUIColors.Danger : BossRushUIColors.Accent)
                        : BossRushUIColors.Disabled,
                    22f, new Vector2(ActionSize.x - 16f, ActionSize.y - 16f),
                    interactable ? new UnityEngine.Events.UnityAction(action.OnClick) : null,
                    interactable);
            }
        }

        #endregion

        #region 内容组装

        /// <summary>
        /// 组装恢复壳内容。真实资产明细只有在存在 active journal 时才出现；
        /// 无法证明安全时调用方传 `allowActions=false`，这里只做只读展示。
        /// </summary>
        public static List<string> BuildLines(
            ModeHSeasonDto season, ModeHStakeJournalDto journal, string technicalReasonId)
        {
            List<string> lines = new List<string>();

            if (!string.IsNullOrEmpty(technicalReasonId))
            {
                lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_TechnicalAbort"));
                lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_SameMatchRestart"));
            }

            if (season != null && season.runState != null)
            {
                lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Label_Match")
                    .Replace("{0}", season.runState.matchIndex.ToString()));
                lines.Add(ResolveStateLabel((ModeHLifecycle)season.runState.lifecycle));
            }

            if (season != null && season.matchReports != null)
            {
                for (int i = 0; i < season.matchReports.Count; i++)
                {
                    ModeHMatchReportDto report = season.matchReports[i];
                    if (report == null) continue;
                    lines.Add(BuildReportLine(report));
                }
            }

            if (season != null && season.seasonRewardOperations != null)
            {
                for (int i = 0; i < season.seasonRewardOperations.Count; i++)
                {
                    ModeHSeasonRewardOperationDto operation = season.seasonRewardOperations[i];
                    if (operation == null) continue;
                    lines.Add("· " + operation.operationId + "  ["
                        + (ModeHSeasonRewardOperationStatus)operation.status + "]");
                }
            }

            // 真实资产明细只在存在 journal 时显示
            if (journal != null)
            {
                lines.Add("txId: " + journal.txId);
                lines.Add(ResolveStakePhaseLabel((ModeHStakePhase)journal.phase));
                lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "RealStake_Escrowed")
                    + ": " + (journal.escrowItems != null ? journal.escrowItems.Count : 0));
                if (!ModeHWarehouseStakeJournal.IsSlotConsistent)
                {
                    lines.Add(L10n.T(ModeHConfig.LocalizationKeyPrefix + "Recovery_ManualIntervention"));
                }
            }
            return lines;
        }

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

        /// <summary>幂等销毁恢复壳。</summary>
        public void Hide()
        {
            if (_root == null) return;
            UnityEngine.Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _body = null;
            _lastBody = null;
        }

        #endregion

        #region 布局常量

        private static readonly Vector2 ActionSize = new Vector2(260f, 56f);
        private const float ActionGap = 24f;

        #endregion
    }
}
