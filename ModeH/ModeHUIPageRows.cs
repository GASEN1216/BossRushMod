// ============================================================================
// ModeHUIPageRows.cs - Mode H 模态页的分段按钮行、就地失败提示与动作排序（2026-09-24 UI 共识对照审查）
// ============================================================================
// 与 ModeHUIPages.cs 是同一个 partial 类，拆开只为单文件行数预算。
//
// 为什么要有分段行（UI 制作共识第 3、5、6 节）：
//   旧版把「下注 0 / 1 / 2」、三个侦察选项、整备的四个分区都塞进底部动作条，和「开打」「锁盘」挤在一排，
//   玩家分不清哪颗是选项、哪颗是结论（审查 B-06 / B-07 / B-08）。现在同一组并列选项画成一排分段按钮：
//   AtTop 的排在页头下面（整备页签、侦察），其余排在动作带上方（下注档）；底部动作带只留结论。
// 就地失败提示（审查 B-11）：锁盘被拒、押品被拒这类失败原来走官方全局提示，现在红字画在按钮带正上方。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>一排分段按钮。</summary>
    internal sealed class ModeHOptionRow
    {
        /// <summary>行首的小标签（「下注」「免费侦察」）；空表示不画。</summary>
        public string Label;
        /// <summary>分段按钮；IsSelected 的那颗染主色并加粗。</summary>
        public List<ModeHActionData> Options = new List<ModeHActionData>();
        /// <summary>分段按钮下面一行说明（14 号次色）；空表示不画。</summary>
        public string Caption;
        /// <summary>true 排在页头下面，false 排在动作带上方。</summary>
        public bool AtTop;
    }

    internal static partial class ModeHUIPages
    {
        #region 分段行

        /// <summary>页头下面的分段行，返回下一块内容的顶边。</summary>
        private static float CreateTopOptionRows(Transform surface, Vector2 panelSize, ModeHPageContent content, float cursorY)
        {
            if (content.OptionRows == null) return cursorY;
            for (int i = 0; i < content.OptionRows.Count; i++)
            {
                ModeHOptionRow row = content.OptionRows[i];
                if (row == null || !row.AtTop || row.Options.Count == 0) continue;
                float height = GetOptionRowHeight(row);
                GameObject host = ZombieModeUIHelper.CreateRect("ModeH_OptionRow_Top_" + i, surface,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, cursorY - height * 0.5f),
                    new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f, height), new Vector2(0.5f, 0.5f));
                BuildOptionRow(host.transform, row, panelSize.x - ModeHUI.SafeMargin * 2f, height);
                cursorY -= height + OptionRowGap;
            }
            return cursorY;
        }

        /// <summary>动作带上方的分段行（从下往上排，最后一行离按钮带最远）。</summary>
        private static void CreateFooterOptionRows(Transform surface, Vector2 panelSize, ModeHPageContent content)
        {
            if (content.OptionRows == null) return;
            float bottom = GetFooterBase(panelSize, content) + GetFailureLineReserve(content);
            for (int i = 0; i < content.OptionRows.Count; i++)
            {
                ModeHOptionRow row = content.OptionRows[i];
                if (row == null || row.AtTop || row.Options.Count == 0) continue;
                float height = GetOptionRowHeight(row);
                GameObject host = ZombieModeUIHelper.CreateRect("ModeH_OptionRow_" + i, surface,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, bottom + height * 0.5f),
                    new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f, height), new Vector2(0.5f, 0.5f));
                BuildOptionRow(host.transform, row, panelSize.x - ModeHUI.SafeMargin * 2f, height);
                bottom += height + OptionRowGap;
            }
        }

        /// <summary>
        /// 动作带上方这一摞（失败提示 + 页脚分段行）的顶边到面板底边的距离；这一摞为空时返回 0（调用方维持旧排法）。
        /// </summary>
        private static float GetFooterStackTop(Vector2 panelSize, ModeHPageContent content)
        {
            float extra = GetFailureLineReserve(content);
            if (content.OptionRows != null)
            {
                for (int i = 0; i < content.OptionRows.Count; i++)
                {
                    ModeHOptionRow row = content.OptionRows[i];
                    if (row == null || row.AtTop || row.Options.Count == 0) continue;
                    extra += GetOptionRowHeight(row) + OptionRowGap;
                }
            }
            return extra > 0f ? GetFooterBase(panelSize, content) + extra : 0f;
        }

        /// <summary>这一摞的底边：有动作按钮时在按钮带上面，否则在安全边距上。</summary>
        private static float GetFooterBase(Vector2 panelSize, ModeHPageContent content)
        {
            return (content.Actions.Count > 0 ? GetActionBandTop(panelSize, content) : ModeHUI.SafeMargin) + OptionRowGap;
        }

        private static float GetOptionRowHeight(ModeHOptionRow row)
        {
            return OptionPillHeight + (string.IsNullOrEmpty(row.Caption) ? 0f : OptionCaptionHeight + 4f);
        }

        /// <summary>一排：[标签] [分段…] 整体居中；说明行在下面居中。</summary>
        private static void BuildOptionRow(Transform host, ModeHOptionRow row, float width, float height)
        {
            List<Button> pills = new List<Button>(row.Options.Count);
            List<float> widths = new List<float>(row.Options.Count);
            float total = 0f;

            TextMeshProUGUI label = null;
            float labelWidth = 0f;
            if (!string.IsNullOrEmpty(row.Label))
            {
                label = ZombieModeUIHelper.CreateText("Label", host, row.Label, 18f,
                    Vector2.zero, new Vector2(200f, OptionPillHeight), TextAlignmentOptions.MidlineRight,
                    BossRushUIColors.TextSecondary);
                label.enableAutoSizing = false;
                label.enableWordWrapping = false;
                BossRushUI.ApplyGameFont(label);
                labelWidth = Mathf.Ceil(label.GetPreferredValues(row.Label).x) + 12f;
                total += labelWidth + OptionPillGap;
            }

            for (int i = 0; i < row.Options.Count; i++)
            {
                ModeHActionData option = row.Options[i];
                if (option == null) continue;
                bool enabled = option.Interactable && option.OnClick != null;
                Button pill = ZombieModeUIHelper.CreateButton("ModeH_Option_" + i, host, option.Label ?? string.Empty,
                    new Vector2(0.5f, 1f), Vector2.zero, new Vector2(160f, OptionPillHeight),
                    BossRushUIColors.SurfaceRaised, 18f, new Vector2(144f, OptionPillHeight - 12f),
                    enabled ? new UnityEngine.Events.UnityAction(option.OnClick) : null, enabled);
                TextMeshProUGUI text = FindLabel(pill);
                float pillWidth = 160f;
                if (text != null)
                {
                    text.enableAutoSizing = false;
                    text.fontSize = 18f;
                    text.enableWordWrapping = false;
                    text.fontStyle = option.IsSelected ? FontStyles.Bold : FontStyles.Normal;
                    pillWidth = Mathf.Clamp(Mathf.Ceil(text.GetPreferredValues(text.text).x) + 40f, 112f, 320f);
                    // 量完宽度再打开缩字：极长的英文标签缩小，而不是被 Ellipsis 整串清空
                    text.enableAutoSizing = true;
                }
                if (option.IsSelected && enabled) StyleSelectedButton(pill, BossRushUIColors.Accent, BossRushUIColors.Accent);
                else if (option.IsDanger && enabled) StyleOutlineButton(pill, BossRushUIColors.DangerText);
                else BossRushUIKit.StyleSecondaryButton(pill);
                pills.Add(pill);
                widths.Add(pillWidth);
                total += pillWidth;
                if (i > 0) total += OptionPillGap;
            }

            // 放不下就等比压窄分段（压到下限还放不下由按钮自己缩字）
            float available = width;
            float scale = total > available ? Mathf.Max(0.5f, available / total) : 1f;
            float x = -total * scale * 0.5f;
            if (label != null)
            {
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 1f);
                labelRect.pivot = new Vector2(0f, 1f);
                labelRect.sizeDelta = new Vector2(labelWidth * scale, OptionPillHeight);
                labelRect.anchoredPosition = new Vector2(x, 0f);
                x += (labelWidth + OptionPillGap) * scale;
            }
            for (int i = 0; i < pills.Count; i++)
            {
                RectTransform rect = pills[i].GetComponent<RectTransform>();
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(widths[i] * scale, OptionPillHeight);
                rect.anchoredPosition = new Vector2(x, 0f);
                x += (widths[i] + OptionPillGap) * scale;
            }

            if (string.IsNullOrEmpty(row.Caption)) return;
            TextMeshProUGUI caption = ZombieModeUIHelper.CreateText("Caption", host, row.Caption, 14f,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, OptionCaptionHeight * 0.5f),
                new Vector2(0f, OptionCaptionHeight), TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            caption.enableAutoSizing = false;
            caption.fontSize = 14f;
            BossRushUI.ApplyGameFont(caption);
        }

        #endregion

        #region 就地失败提示

        private static float GetFailureLineReserve(ModeHPageContent content)
        {
            return string.IsNullOrEmpty(content.FailureText) ? 0f : FailureLineHeight + OptionRowGap;
        }

        /// <summary>刚才那一下为什么没成：红字画在按钮带正上方（审查 B-11）。</summary>
        private static void CreateFailureLine(Transform surface, Vector2 panelSize, ModeHPageContent content)
        {
            if (string.IsNullOrEmpty(content.FailureText)) return;
            float bottom = GetFooterBase(panelSize, content);
            GameObject row = ZombieModeUIHelper.CreateRect("ModeH_FailureLine", surface,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, bottom + FailureLineHeight * 0.5f),
                new Vector2(panelSize.x - ModeHUI.SafeMargin * 2f, FailureLineHeight), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI text = ZombieModeUIHelper.CreateTMPText(row, content.FailureText, 17f,
                TextAlignmentOptions.Center, BossRushUIColors.DangerText);
            BossRushUI.ApplyGameFont(text);
            if (!content.Refresh) BossRushUIEntranceAnimation.Play(row, 0f, 0.2f, 6f);
        }

        #endregion

        #region 动作排序

        /// <summary>
        /// 动作带的顺序（UI 制作共识第 4 节）：危险的次级操作排最左、与安全按钮拉开；主操作排最右；其余保持原序。
        /// 主操作的判据与 ResolvePrimaryAction 同一份。返回新列表，不改调用方的数据。
        /// </summary>
        internal static List<ModeHActionData> OrderActions(IList<ModeHActionData> actions)
        {
            List<ModeHActionData> ordered = new List<ModeHActionData>(actions.Count);
            int primary = ResolvePrimaryAction(actions);
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && i != primary && actions[i].IsDanger) ordered.Add(actions[i]);
            }
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] != null && i != primary && !actions[i].IsDanger) ordered.Add(actions[i]);
            }
            if (primary >= 0) ordered.Add(actions[primary]);
            return ordered;
        }

        #endregion

        #region 常量

        /// <summary>分段按钮高度（18 号字一行 + 上下内边距）。</summary>
        private const float OptionPillHeight = 44f;
        private const float OptionPillGap = 12f;
        /// <summary>分段行说明：14 号字单行至少 1.45×14+4≈25。</summary>
        private const float OptionCaptionHeight = 26f;
        private const float OptionRowGap = 10f;
        /// <summary>失败提示：17 号字单行至少 1.45×17+4≈29，加共享的上下 2px 边距。</summary>
        private const float FailureLineHeight = 34f;

        #endregion
    }
}
