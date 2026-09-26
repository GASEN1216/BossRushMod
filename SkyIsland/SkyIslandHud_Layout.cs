// ============================================================================
// SkyIslandHud_Layout.cs - 天空岛 HUD 的版式计算与走近浮现的世界标签
// ============================================================================
// 内容：SkyIslandHud 的「版式」一节，以及 SkyIslandProximityLabel。
// 从主文件原样提取（2026-09-23，AGENTS §4.15：超过 1200 行的新文件拆到同一 partial 的新文件，行为逐字不变）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Duckov.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed partial class SkyIslandHud : IDisposable
    {
        #region 版式

        /// <summary>
        /// 重排卡片：**先量后排**，卡片高度由实际内容决定。
        /// 只在内容真的变化时调用（写入方法自己短路），不是每帧路径。
        /// </summary>
        private void Apply()
        {
            if (card == null) return;
            float width = CardWidth - CardPadX * 2f - AccentBarWidth - 6f;
            float left = CardPadX + AccentBarWidth + 6f;
            float y = -CardPadY;
            // 卡片已经显着时，新出现的行单独淡入；整块入场时行跟着卡片一起走（UE-11）。
            bool live = card.activeSelf && cardVisible > 0f;

            y -= Row(0, region, width, left, y, RegionFont, 0f, live);
            y -= Row(1, ObjectiveDisplay(), width, left, y, BodyFont, BodyFont * 1.3f * ObjectiveMaxLines, live);
            y -= RuleRow(chips.Length > 0 && (region.Length > 0 || shownObjective.Length > 0), width, left, y);
            y -= Row(2, chipsDisplay, width, left, y, ChipFont, 0f, live);
            y -= Row(3, extraction, width, left, y, TitleFont, 0f, live);
            y -= Row(4, status, width, left, y, BodyFont, 0f, live);

            // 一行都没有就整张卡片收掉。装配期间（还没就绪、什么都没得说）留一块空底板在那儿，
            // 恰恰是「廉价感」的来源之一：没内容就不该有框。
            // 收与放都走 TickCard 的淡变，不再 SetActive 硬开关；真正关掉对象是淡完之后的事。
            bool anything = region.Length > 0 || objective.Length > 0 || chips.Length > 0
                || !string.IsNullOrEmpty(extraction) || !string.IsNullOrEmpty(status);
            RetargetCard(anything ? 1f : 0f);
            bool appearing = anything && !card.activeSelf;
            if (appearing)
            {
                card.SetActive(true);
                cardVisible = 0f;
                cardSlide = CardSlideIn;
                if (cardGroup != null) cardGroup.alpha = 0f;
                WriteCardTransform();
            }
            SetCardHeight(Mathf.Max(48f, -y + CardPadY), appearing || !live);
        }

        private static readonly string AccentHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.Accent);
        private static readonly string SecondaryHex = "#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextSecondary);
        private static readonly string[] ClauseSeparator = { " · " };
        /// <summary>进度行里的「3/12」：正文色 + 等宽（数字跳动时不抖），在一行灰字里跳得出来（UE-01）。</summary>
        private static readonly Regex CountPattern = new Regex(@"\d+/\d+", RegexOptions.CultureInvariant);
        private static readonly string CountMarkup =
            "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextPrimary) + "><mspace=0.55em>$0</mspace></color>";

        /// <summary>进度行此刻的显示文本（带标签）。只在 <see cref="SetChips"/> 里内容真的变了才重算。</summary>
        private string chipsDisplay = string.Empty;

        /// <summary>目标行的实际文本：目标刚变过时，在上方挂一行强调色的「目标更新」眉题；正文按分句排（<see cref="ObjectiveLines"/>）。</summary>
        private string ObjectiveDisplay()
        {
            string lines = ObjectiveLines(shownObjective);
            // 换字的淡出段里还显示着旧目标：眉题只跟新目标一起出现。
            if (objectiveUpdatedAge < 0f || shownObjective.Length == 0
                || (objectiveSwap >= 0f && objectiveSwap < ObjectiveSwapOut)) return lines;
            return "<size=85%><color=" + AccentHex + ">"
                + L10n.T("目标更新", "Objective updated") + "</color></size>\n" + lines;
        }

        /// <summary>
        /// 目标的显示排版（UE-01）。规则文案是「主句 · 分句 · 分句」一整串（<c>SkyIslandStoryRules.Objective</c>，
        /// F3 与隔离回归逐字核对，不动它）；这里按「 · 」拆开：主句一行正文色，其余每句一行、缩进、小一号、次级色——
        /// 瞄一眼先读到要做的事，细节退后一层。旧版三四句挤成一段 13px 灰字，像日志输出。
        /// </summary>
        internal static string ObjectiveLines(string objective)
        {
            if (string.IsNullOrEmpty(objective) || objective.IndexOf(ClauseSeparator[0], StringComparison.Ordinal) < 0)
                return objective ?? string.Empty;
            string[] clauses = objective.Split(ClauseSeparator, StringSplitOptions.RemoveEmptyEntries);
            var text = new StringBuilder(clauses[0]);
            for (int i = 1; i < clauses.Length; i++)
                text.Append("\n<size=87%><color=").Append(SecondaryHex).Append("><indent=0.8em>")
                    .Append(clauses[i]).Append("</indent></color></size>");
            return text.ToString();
        }

        /// <summary>目标与进度之间的分隔线：两段都有时才画，返回它占掉的高度。</summary>
        private float RuleRow(bool visible, float width, float left, float top)
        {
            if (chipRule == null) return 0f;
            if (chipRule.gameObject.activeSelf != visible) chipRule.gameObject.SetActive(visible);
            if (!visible) return 0f;
            chipRule.sizeDelta = new Vector2(width, CardRuleHeight);
            // 往上借 2 个单位的行距，线正好落在两段文字的留白中间。
            chipRule.anchoredPosition = new Vector2(left, top + 2f);
            return CardRuleHeight;
        }

        /// <summary>
        /// 摆一行并返回它占掉的高度（含行距）。内容为空时整行隐藏、不占位。
        /// <paramref name="maxHeight"/> 大于 0 时封顶（超出的行省略号收尾）；<paramref name="live"/> 为真时新出现的行淡入。
        /// </summary>
        private float Row(int index, string value, float width, float left,
            float top, float font, float maxHeight, bool live)
        {
            TextMeshProUGUI text = rowTexts[index];
            if (text == null) return 0f;
            if (string.IsNullOrEmpty(value))
            {
                if (text.gameObject.activeSelf) text.gameObject.SetActive(false);
                return 0f;
            }
            if (!text.gameObject.activeSelf)
            {
                text.gameObject.SetActive(true);
                rowReveal[index] = live ? 0f : 1f;
                if (live) rowsRevealing = true;
                SetRowAlpha(index);
            }
            text.text = value;
            float height = Mathf.Max(font * 1.3f,
                Mathf.Ceil(text.GetPreferredValues(value, width, float.PositiveInfinity).y) + 2f);
            if (maxHeight > 0f) height = Mathf.Min(height, maxHeight);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(left, top);
            return height + 4f;
        }

        #endregion
    }

    /// <summary>
    /// 世界空间提示字的「走近才浮现」。
    ///
    /// 常驻的浮空字远远就亮着，满屏都是网游式的头顶标语。主流做法是靠光、模型与地图点让人
    /// 远远注意到「那里有东西」，走近了才浮出文字说明它是什么。硬开关（SetActive）会「啪」地弹出，
    /// 这里按距离做连续的 smoothstep 淡变，完全透明时顺手关掉渲染，不白占 draw call。
    ///
    /// 纯表现层：不碰交互体，官方交互提示照常由 InteractableBase 负责。
    /// 支持两种载体：世界空间 TextMeshPro（淡 alpha + 关 Renderer），或世界空间 Canvas（淡 CanvasGroup + 关 Canvas）。
    /// </summary>
    internal sealed class SkyIslandProximityLabel : MonoBehaviour
    {
        /// <summary>
        /// 透明度量化步长。TMP 的 alpha 每改一次都要重建整块文字网格（顶点色跟着重算），
        /// 旧写法按 1% 阈值更新，玩家走过一次 5 米的淡变带要重建上百次；按 5% 一档最多 20 次，肉眼看不出台阶。
        /// </summary>
        private const float AlphaStep = 0.05f;
        /// <summary>估算玩家接近速度的上限（米/秒）：离得远时据此推迟下一次距离检查。</summary>
        private const float ApproachSpeed = 8f;
        private const float MaxRecheckSeconds = 1f;

        private float near, far;
        private TMP_Text text;
        private Renderer textRenderer;
        private Canvas worldCanvas;
        private CanvasGroup group;
        private float applied = -1f, nextCheck;
        /// <summary>最亮到多少（0–1）。翻过的搜刮箱把字压到「看过了」的暗度（UE-03），默认全亮。</summary>
        private float peak = 1f;

        /// <summary>改这块字最亮到多少；下一帧按新的上限重写。</summary>
        internal static void SetPeak(GameObject target, float value)
        {
            SkyIslandProximityLabel label = target != null ? target.GetComponent<SkyIslandProximityLabel>() : null;
            if (label == null) return;
            label.peak = Mathf.Clamp01(value);
            label.applied = -1f;
            label.nextCheck = 0f;
        }

        /// <param name="near">这个距离以内完全显形。</param>
        /// <param name="far">这个距离以外完全消失。</param>
        internal static void Attach(GameObject target, float near, float far)
        {
            if (target == null) return;
            SkyIslandProximityLabel label = target.GetComponent<SkyIslandProximityLabel>();
            if (label == null) label = target.AddComponent<SkyIslandProximityLabel>();
            label.near = Mathf.Max(0f, near);
            label.far = Mathf.Max(label.near + 0.5f, far);
            label.worldCanvas = target.GetComponent<Canvas>();
            if (label.worldCanvas != null)
            {
                label.group = target.GetComponent<CanvasGroup>();
                if (label.group == null) label.group = target.AddComponent<CanvasGroup>();
                label.group.blocksRaycasts = false;
                label.group.interactable = false;
            }
            else
            {
                label.text = target.GetComponent<TMP_Text>();
                label.textRenderer = target.GetComponent<Renderer>();
            }
            // 先按完全透明起步，第一次 LateUpdate 再按实际距离打开，不会在建出来那一帧闪一下。
            label.ApplyAlpha(0f);
        }

        private void LateUpdate()
        {
            // 完全隐形且离得远时不必每帧量距离：按「以最快接近速度走到淡变带外沿还要多久」推迟下一次检查，
            // 最多隔 1 秒。纪念物与船点招牌大部分时间离玩家很远，这里通常只是一次时间比较。
            if (applied <= 0f && Time.unscaledTime < nextCheck) return;
            float alpha = 0f;
            CharacterMainControl main = CharacterMainControl.Main;
            if (main != null)
            {
                float distance = Vector3.Distance(main.transform.position, transform.position);
                float t = Mathf.Clamp01((far - distance) / (far - near));
                alpha = Mathf.Round(t * t * (3f - 2f * t) * peak / AlphaStep) * AlphaStep;
                if (alpha <= 0f)
                    nextCheck = Time.unscaledTime + Mathf.Min(MaxRecheckSeconds, (distance - far) / ApproachSpeed);
            }
            else
            {
                nextCheck = Time.unscaledTime + MaxRecheckSeconds;
            }
            // 量化后同一档不重写；「归零」这一档照样要写下去，否则 Renderer 会以最低一档的 alpha 一直开着。
            if (Mathf.Abs(alpha - applied) < AlphaStep * 0.5f) return;
            ApplyAlpha(alpha);
        }

        private void ApplyAlpha(float alpha)
        {
            applied = alpha;
            bool visible = alpha > 0.001f;
            if (group != null) group.alpha = alpha;
            if (worldCanvas != null && worldCanvas.enabled != visible) worldCanvas.enabled = visible;
            if (text != null) text.alpha = alpha;
            if (textRenderer != null && textRenderer.enabled != visible) textRenderer.enabled = visible;
        }
    }
}
