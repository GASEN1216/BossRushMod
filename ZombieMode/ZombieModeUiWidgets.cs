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
//   - ZombieModeClickableCard：撤离抉择与开局流派的整卡可点（2026-09-24 UI 共识对照审查 B-28）。
//   - ZombieModeHudBar：HUD 上 4px 的圆头读条，长度按 unscaled 时间 MoveTowards 过渡，与帧率无关（B-30）。
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
    /// 整张卡当按钮（UI 制作共识第 7 节第 4 条，照 Mode H 选人卡 MakeCardClickable 的写法；UI 共识对照审查 B-28）：
    /// 卡片底图就是按钮图（ColorBlock 绝对色、共享音效与按下回弹），悬停时描边提亮；卡里的按钮只是把「能点」说清楚，
    /// 点它只触发它自己。卡片必须是 BossRushUI.CreateCard 建的（带名为 Stroke 的描边子物体）。
    /// </summary>
    internal static class ZombieModeClickableCard
    {
        internal static Button Make(GameObject card, UnityEngine.Events.UnityAction onClick)
        {
            Image image = card != null ? card.GetComponent<Image>() : null;
            if (image == null || onClick == null)
            {
                return null;
            }
            Button button = card.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ZombieModeUIHelper.SetButtonBaseColor(button, BossRushUIColors.SurfaceRaised);
            button.onClick.AddListener(onClick);
            Transform stroke = card.transform.Find("Stroke");
            if (stroke != null)
            {
                ZombieModeCardHover.Attach(card, stroke.GetComponent<Image>());
            }
            return button;
        }
    }

    /// <summary>
    /// HUD 读条：细轨 + 圆头填充。<see cref="SetTarget"/> 只记目标，<see cref="Tick"/> 由宿主的 Update 每帧调（O(1)、无分配）。
    /// 读条跳得比较远（新一轮准备期从 0 回到满）时直接落位，不从 0 慢慢爬上去。
    /// 填充长度由 anchorMax.x 驱动、走细条档圆角（UI 共识对照审查 B-30，照血猎追击状态卡 AnimateBar）：
    /// Filled 配不了九宫格圆角，方头会戳出圆角轨道；几乎空的时候圆角细条会缩成一个点，干脆不画。
    /// </summary>
    internal sealed class ZombieModeHudBar
    {
        private const float UnitsPerSecond = 2.5f;
        private const float MinVisibleFill = 0.02f;
        private readonly GameObject root;
        private readonly RectTransform fillRect;
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

            GameObject fillObject = ZombieModeUIHelper.CreateRect("Fill", root.transform, Vector2.zero, new Vector2(0f, 1f),
                Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f));
            fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fill = fillObject.AddComponent<Image>();
            fill.color = BossRushUIColors.Accent;
            fill.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(fill, 2, BossRushUISkinPart.Hairline);
            fillObject.SetActive(false);
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
                    ApplyFill(Mathf.Clamp01(value));
                }
            }
            target = Mathf.Clamp01(value);
            if (Mathf.Abs(target - current) > 0.5f)
            {
                ApplyFill(target);
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
            ApplyFill(Mathf.MoveTowards(current, target, UnitsPerSecond * unscaledDeltaTime));
        }

        private void ApplyFill(float value)
        {
            current = value;
            fillRect.anchorMax = new Vector2(value, 1f);
            bool show = value > MinVisibleFill;
            if (fill.gameObject.activeSelf != show)
            {
                fill.gameObject.SetActive(show);
            }
        }
    }
}
