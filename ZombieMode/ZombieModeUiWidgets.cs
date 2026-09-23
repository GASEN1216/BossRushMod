// ============================================================================
// ZombieModeUiWidgets.cs - 丧尸模式界面的小部件：奖励卡文案拆分、就地反馈、卡片悬停描边、HUD 读条
// ============================================================================
// 模块说明：
//   2026-09-23 审美审查（UC-02 / UC-04 / UC-17 / UC-18 / UC-27）。只放表现层的小类型，
//   状态与判据仍在 ModBehaviour 的丧尸模式 partial 里；这里不读写任何局内状态。
//   - ZombieModeRewardCardText：把「[类别] 收益（代价：…）」拆成类别 chip、收益、代价三段，
//     只在显示时切分，本地化值不动（ZombieModeRewardPlainTextGuard 仍按原文核对「代价：」）。
//   - ZombieModeUiNudge：点了但做不成（净化点不够）时，按钮上方浮一行「还差 N 净化点」并横向抖一下。
//     官方 Toast 画在官方 HUD 画布上，被 30000 层的遮罩压着，玩家看不见（UC-27）。
//   - ZombieModeCardHover：奖励卡悬停时描边提亮（事件驱动，不逐帧）。
//   - ZombieModeHudBar：HUD 上 4px 的读条，fillAmount 按 unscaled 时间 MoveTowards 过渡，与帧率无关。
//   动效都走 unscaled 时间并过 BossRushUI.IsGamePaused() 门，播完 enabled = false。
// ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossRush
{
    internal static class ZombieModeRewardCardText
    {
        private static readonly string[] CostMarkers = { "代价：", "Cost:" };
        private static readonly char[] HeadTrim = { ' ', '，', ',', '（', '(' };
        private static readonly char[] TailTrim = { ' ', '）', ')' };

        /// <summary>
        /// 「[属性] 最大生命 +10%（本局）」→ 类别「属性」、收益「最大生命 +10%（本局）」、代价空；
        /// 「[触发] 累计击杀触发 3 次爆炸（代价：承受伤害 +10%）」→ 收益「累计击杀触发 3 次爆炸」、代价「代价：承受伤害 +10%」；
        /// 括号里代价前还有说明的（「…（2发斜向子弹，代价：…）」）补回被切掉的右括号。
        /// </summary>
        internal static void Split(string display, out string category, out string benefit, out string cost)
        {
            category = string.Empty;
            cost = string.Empty;
            benefit = display ?? string.Empty;
            if (benefit.StartsWith("[", System.StringComparison.Ordinal))
            {
                int close = benefit.IndexOf("] ", System.StringComparison.Ordinal);
                if (close > 0)
                {
                    category = benefit.Substring(1, close - 1);
                    benefit = benefit.Substring(close + 2);
                }
            }

            int costAt = -1;
            for (int i = 0; i < CostMarkers.Length && costAt < 0; i++)
            {
                costAt = benefit.IndexOf(CostMarkers[i], System.StringComparison.Ordinal);
            }
            if (costAt <= 0)
            {
                return;
            }

            cost = benefit.Substring(costAt).TrimEnd(TailTrim);
            string head = benefit.Substring(0, costAt).TrimEnd(HeadTrim);
            if (head.LastIndexOf('（') > head.LastIndexOf('）'))
            {
                head += "）";
            }
            if (head.LastIndexOf('(') > head.LastIndexOf(')'))
            {
                head += ")";
            }
            benefit = head;
        }
    }

    /// <summary>
    /// 就地反馈：宿主上方浮一行短字（默认 DangerText），1.2 s 后 0.3 s 淡出；同时宿主横向抖 0.22 s（±6 单位，EaseOut 衰减）。
    /// 重复触发只重置计时，不叠第二行。抖动改的是 anchoredPosition，结束时写回原位；布局组重排时以布局为准。
    /// </summary>
    internal sealed class ZombieModeUiNudge : MonoBehaviour
    {
        private const float HoldSeconds = 1.2f;
        private const float FadeSeconds = 0.3f;
        private const float ShakeSeconds = 0.22f;
        private const float ShakeUnits = 6f;

        private RectTransform rect;
        private TextMeshProUGUI label;
        private CanvasGroup labelGroup;
        private Vector2 restPosition;
        private float age;
        private bool shaking;

        internal static void Flash(Component host, string text)
        {
            if (host == null)
            {
                return;
            }
            ZombieModeUiNudge nudge = host.GetComponent<ZombieModeUiNudge>();
            if (nudge == null)
            {
                nudge = host.gameObject.AddComponent<ZombieModeUiNudge>();
            }
            nudge.Play(text);
        }

        private void Play(string text)
        {
            rect = transform as RectTransform;
            if (label == null)
            {
                GameObject obj = ZombieModeUIHelper.CreateRect("Feedback", transform,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 14f), new Vector2(40f, 24f), new Vector2(0.5f, 0f));
                obj.AddComponent<LayoutElement>().ignoreLayout = true;
                labelGroup = obj.AddComponent<CanvasGroup>();
                labelGroup.blocksRaycasts = false;
                label = ZombieModeUIHelper.CreateTMPText(obj, string.Empty, 14f, TextAlignmentOptions.Bottom, BossRushUIColors.DangerText);
                label.enableAutoSizing = false;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
                label.fontStyle = FontStyles.Bold;
                BossRushUIKit.ApplyWorldTextOutline(label);
            }
            label.text = text ?? string.Empty;
            labelGroup.alpha = 1f;
            if (!shaking && rect != null)
            {
                restPosition = rect.anchoredPosition;
            }
            shaking = rect != null;
            age = 0f;
            enabled = true;
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            age += Time.unscaledDeltaTime;
            if (shaking && rect != null)
            {
                float t = Mathf.Clamp01(age / ShakeSeconds);
                float offset = Mathf.Sin(t * Mathf.PI * 5f) * ShakeUnits * (1f - BossRushUI.EaseOut(t));
                rect.anchoredPosition = new Vector2(restPosition.x + offset, restPosition.y);
                if (t >= 1f)
                {
                    rect.anchoredPosition = restPosition;
                    shaking = false;
                }
            }

            if (labelGroup != null)
            {
                float fade = Mathf.Clamp01((age - HoldSeconds) / FadeSeconds);
                labelGroup.alpha = 1f - BossRushUI.SmoothStep(fade);
                if (fade >= 1f && !shaking)
                {
                    enabled = false;
                }
            }
        }

        private void OnDisable()
        {
            if (shaking && rect != null)
            {
                rect.anchoredPosition = restPosition;
                shaking = false;
            }
        }
    }

    /// <summary>奖励卡悬停：描边从常态 α 提到 0.95。事件驱动，没有 Update。</summary>
    internal sealed class ZombieModeCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Image stroke;
        private Color rest;

        internal static void Attach(GameObject card, Image strokeImage)
        {
            if (card == null || strokeImage == null)
            {
                return;
            }
            ZombieModeCardHover hover = card.GetComponent<ZombieModeCardHover>();
            if (hover == null)
            {
                hover = card.AddComponent<ZombieModeCardHover>();
            }
            hover.stroke = strokeImage;
            hover.rest = strokeImage.color;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (stroke != null)
            {
                stroke.color = new Color(rest.r, rest.g, rest.b, 0.95f);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (stroke != null)
            {
                stroke.color = rest;
            }
        }
    }

    /// <summary>
    /// HUD 读条：细轨 + Filled 填充。<see cref="SetTarget"/> 只记目标，<see cref="Tick"/> 由宿主的 Update 每帧调（O(1)、无分配）。
    /// 读条跳得比较远（新一轮准备期从 0 回到满）时直接落位，不从 0 慢慢爬上去。
    /// </summary>
    internal sealed class ZombieModeHudBar
    {
        private const float UnitsPerSecond = 2.5f;
        private readonly GameObject root;
        private readonly Image fill;
        private float current;
        private float target;
        private bool visible;

        internal ZombieModeHudBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            root = ZombieModeUIHelper.CreateRect(name, parent, anchorMin, anchorMax, position, size, new Vector2(0.5f, 0.5f));
            root.AddComponent<LayoutElement>().ignoreLayout = true;
            Image track = root.AddComponent<Image>();
            track.color = new Color(BossRushUIColors.Stroke.r, BossRushUIColors.Stroke.g, BossRushUIColors.Stroke.b, 0.28f);
            track.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(track, 2, BossRushUISkinPart.Hairline);

            GameObject fillObject = ZombieModeUIHelper.CreateRect("Fill", root.transform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            fill = fillObject.AddComponent<Image>();
            fill.sprite = BossRushUI.GetSolidSprite();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            fill.color = BossRushUIColors.Accent;
            fill.raycastTarget = false;
            root.SetActive(false);
        }

        internal void SetTarget(bool show, float value, Color color)
        {
            if (show != visible)
            {
                visible = show;
                root.SetActive(show);
                if (show)
                {
                    current = Mathf.Clamp01(value);
                    fill.fillAmount = current;
                }
            }
            target = Mathf.Clamp01(value);
            if (Mathf.Abs(target - current) > 0.5f)
            {
                current = target;
                fill.fillAmount = current;
            }
            if (fill.color != color)
            {
                fill.color = color;
            }
        }

        internal void Tick(float unscaledDeltaTime)
        {
            if (!visible || Mathf.Approximately(current, target))
            {
                return;
            }
            current = Mathf.MoveTowards(current, target, UnitsPerSecond * unscaledDeltaTime);
            fill.fillAmount = current;
        }
    }
}
