// ============================================================================
// ModeFStatusHud.cs - 血猎追击的常驻状态卡
// ============================================================================
// 2026-09-23 审美审查 UB-06：阶段、剩余时间、命火进度这些**持续**信息，旧版只能靠每 15 秒一条
// 彩色长横幅（「血猎追击 | 悬赏阶段 | 命火 62/100 | 剩余 87 秒」，纯红纯绿纯黄五种颜色加竖线）闪一下——
// 官方提示条只停 1.2–2 秒，中间 13 秒玩家什么都看不到。现在改成一张常驻小卡片，横幅只留阶段切换、
// 榜首变更与胜负这类「事件」。
//
// 版式：左上 (16, -140)，300×92。左上角是官方时间显示，照随机事件徽章（RandomEventsTuning.HudBadgeMarginY）
// 的口径让开它；Mode F 会 roll 变异词条，词条面板从左上 -240 往下长（MutatorUI.PanelTopOffset），本卡底边停在 -232。
// 不放右上：征程契约追踪条（CampaignHud）在 Mode F 章节武装时就在右上，两张卡会叠在一起。
//   第 1 行：阶段名（18，金）……………………剩余 mm:ss（24，等宽；最后 10 秒转金；撤离阶段写「速速撤离」）
//   第 2 行：命火 / 命火过载（13，次级字）…………62/100 或 12 秒（13）
//   第 3 行：6px 进度条（命火充能，过载时换红色、显示过载剩余）
//
// 常驻 HUD 口径（AGENTS §4.17）：每帧入口 Tick 先过 IsOfficialHudHidden + IsGamePaused，登记在
// tests/PersistentHudVisibilityGuard.py；层级走 BossRushUILayers.Hud；独立类型，不新增 ModBehaviour partial（§4.15）。
//
// 【性能】Tick 是每帧路径：文本只在整数值（秒、命火点数）或阶段 / 语言变化时才写；进度条只在显示值
// 追目标值（MoveTowards，unscaled 时间，与帧率无关）的那几帧写 anchorMax。Mode F 不在跑时宿主根本不调这里。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>血猎追击常驻状态卡。非交互，点击穿透。由 ModeFPhases.TickModeF 每帧驱动，ExitModeF / 模块销毁时 Dispose。</summary>
    internal static class ModeFStatusHud
    {
        private const string RootName = "ModeF_StatusHud";
        private const float CardLeft = 16f;
        private const float CardTop = 140f;
        private const float CardWidth = 300f;
        private const float CardHeight = 92f;
        /// <summary>文字左边距：卡片左侧 3–7 是强调竖条，再留 11。</summary>
        private const float PadLeft = 18f;
        private const float PadRight = 14f;
        private const float BarTop = 76f;
        private const float BarHeight = 6f;
        /// <summary>进度条显示值每秒最多走满格的比例：一次击杀涨十几点，约 0.1 秒追上，看得出「涨了一截」。</summary>
        private const float BarSpeed = 1.5f;
        /// <summary>剩余多少秒起计时转金色（阶段即将切换、压力要升级）。</summary>
        private const int TimerWarnSeconds = 10;

        private static readonly string WarningTag = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.WarningText) + ">";
        private static readonly string DangerTag = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText) + ">";

        private static Canvas _canvas;
        private static TextMeshProUGUI _phaseText;
        private static TextMeshProUGUI _timerText;
        private static TextMeshProUGUI _fireLabel;
        private static TextMeshProUGUI _fireValue;
        private static RectTransform _barFill;
        private static Image _barFillImage;
        private static bool _buildFailed;

        // 上一次写进 TMP 的值：只比整数 / 枚举 / bool，零分配脏检查。
        private static ModeFPhase _shownPhase = ModeFPhase.None;
        private static bool _shownChinese;
        private static int _shownSeconds = int.MinValue;
        private static int _shownFire = int.MinValue;
        private static int _shownOverload = -1;
        private static float _barShown;
        /// <summary>本次过载见过的最长剩余时间：进度条按它归一，从满格往下走；赏金击杀续燃时跟着涨。</summary>
        private static float _overloadPeak;

        /// <summary>
        /// 每帧入口（宿主 TickModeF 调）。<paramref name="maxCharge"/> 由宿主传入命火上限常量，这里不另存一份。
        /// </summary>
        internal static void Tick(ModeFState state, float maxCharge)
        {
            try
            {
                if (state == null || !state.IsActive || state.CurrentPhase == ModeFPhase.None)
                {
                    Hide();
                    return;
                }

                EnsureBuilt();
                if (_canvas == null) return;

                // 跟随官方界面与暂停菜单收起（常驻 HUD 同一口径）；隐藏期间不刷新，回来时按当前值补上。
                bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
                if (_canvas.enabled != visible) _canvas.enabled = visible;
                if (!visible) return;

                RefreshTexts(state, maxCharge);
                AnimateBar();
            }
            catch (Exception)
            {
                // 每帧路径：不抛也不打日志
            }
        }

        private static void RefreshTexts(ModeFState state, float maxCharge)
        {
            bool chinese = L10n.IsChinese;
            if (state.CurrentPhase != _shownPhase || chinese != _shownChinese)
            {
                _shownPhase = state.CurrentPhase;
                _shownChinese = chinese;
                _shownSeconds = int.MinValue;
                _shownFire = int.MinValue;
                _shownOverload = -1;
                if (_phaseText != null) _phaseText.text = ModBehaviour.GetModeFPhaseName(state.CurrentPhase);
            }

            // 撤离阶段没有时限（PhaseDuration = float.MaxValue）：计时位写「速速撤离」。
            bool timed = state.CurrentPhase != ModeFPhase.Extraction && state.PhaseDuration < 100000f;
            int seconds = timed ? Mathf.Max(0, Mathf.CeilToInt(state.PhaseDuration - state.PhaseElapsed)) : -1;
            if (seconds != _shownSeconds && _timerText != null)
            {
                _shownSeconds = seconds;
                if (!timed)
                {
                    _timerText.text = "<size=18>" + DangerTag + L10n.T("速速撤离", "Evacuate!") + "</color></size>";
                }
                else
                {
                    string clock = "<mspace=0.56em>" + (seconds / 60) + ":" + (seconds % 60).ToString("00") + "</mspace>";
                    _timerText.text = seconds <= TimerWarnSeconds ? WarningTag + clock + "</color>" : clock;
                }
            }

            bool overload = state.BloodfireOverloadActive;
            int overloadFlag = overload ? 1 : 0;
            int fire = overload
                ? Mathf.CeilToInt(state.BloodfireOverloadRemaining)
                : Mathf.RoundToInt(state.BloodfireCharge);
            if (overloadFlag != _shownOverload)
            {
                _shownOverload = overloadFlag;
                _shownFire = int.MinValue;
                _overloadPeak = 0f;
                if (_fireLabel != null)
                {
                    _fireLabel.text = overload ? L10n.T("命火过载", "Overload") : L10n.T("命火", "Bloodfire");
                    _fireLabel.color = overload ? BossRushUIColors.DangerText : BossRushUIColors.TextSecondary;
                }
                if (_barFillImage != null)
                {
                    _barFillImage.color = overload ? BossRushUIColors.DangerText : BossRushUIColors.WarningText;
                }
            }
            if (fire != _shownFire && _fireValue != null)
            {
                _shownFire = fire;
                _fireValue.text = overload
                    ? fire + L10n.T(" 秒", "s")
                    : fire + "/" + Mathf.RoundToInt(maxCharge);
            }

            if (overload)
            {
                _overloadPeak = Mathf.Max(_overloadPeak, state.BloodfireOverloadRemaining);
                _barTarget = _overloadPeak > 0.01f ? Mathf.Clamp01(state.BloodfireOverloadRemaining / _overloadPeak) : 0f;
            }
            else
            {
                _barTarget = maxCharge > 0.01f ? Mathf.Clamp01(state.BloodfireCharge / maxCharge) : 0f;
            }
        }

        private static float _barTarget;

        /// <summary>显示值追目标值：MoveTowards + unscaled 时间，与帧率无关；追上之后不再写 RectTransform。</summary>
        private static void AnimateBar()
        {
            if (_barFill == null || Mathf.Approximately(_barShown, _barTarget)) return;
            _barShown = Mathf.MoveTowards(_barShown, _barTarget, Time.unscaledDeltaTime * BarSpeed);
            _barFill.anchorMax = new Vector2(_barShown, 1f);
            // 圆角细条宽度小于两端圆角时会画成一个点，几乎空的时候干脆不画。
            bool show = _barShown > 0.02f;
            if (_barFill.gameObject.activeSelf != show) _barFill.gameObject.SetActive(show);
        }

        private static void EnsureBuilt()
        {
            if (_canvas != null || _buildFailed) return;
            try
            {
                // 不 DontDestroyOnLoad：跟场景走。切图时 ExitModeF 会先 Dispose；万一漏了，场景卸载也会带走它，
                // 这里按 Unity 判空重建。
                _canvas = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.Hud, false);

                Color surface = BossRushUIColors.Surface;
                surface.a = 0.85f;
                GameObject card = BossRushUI.CreateCard("ModeF_StatusCard", _canvas.transform, Vector2.zero,
                    new Vector2(CardWidth, CardHeight), surface, BossRushUIColors.DangerText, true);
                RectTransform cardRect = card.GetComponent<RectTransform>();
                cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(0f, 1f);
                cardRect.anchoredPosition = new Vector2(CardLeft, -CardTop);
                card.GetComponent<Image>().raycastTarget = false;

                float inner = CardWidth - PadLeft - PadRight;
                _phaseText = CreateLine("Phase", card.transform, 18f, TextAlignmentOptions.MidlineLeft,
                    BossRushUIColors.WarningText, PadLeft, 10f, inner - 110f, 30f, false);
                _timerText = CreateLine("Timer", card.transform, 24f, TextAlignmentOptions.MidlineRight,
                    BossRushUIColors.TextPrimary, CardWidth - PadRight, 6f, 120f, 39f, true);
                _fireLabel = CreateLine("FireLabel", card.transform, 13f, TextAlignmentOptions.MidlineLeft,
                    BossRushUIColors.TextSecondary, PadLeft, 49f, inner * 0.5f, 23f, false);
                _fireValue = CreateLine("FireValue", card.transform, 13f, TextAlignmentOptions.MidlineRight,
                    BossRushUIColors.TextPrimary, CardWidth - PadRight, 49f, inner * 0.5f, 23f, true);

                // 进度条：细轨 + 圆角填充，填充长度由 anchorMax.x 驱动（Filled 配不了九宫格圆角，方头会戳出轨道）。
                GameObject track = ZombieModeUIHelper.CreateRect("FireTrack", card.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(PadLeft, -BarTop),
                    new Vector2(inner, BarHeight), new Vector2(0f, 1f));
                Image trackImage = track.AddComponent<Image>();
                trackImage.color = BossRushUIColors.Divider;
                trackImage.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(trackImage, 3, BossRushUISkinPart.Hairline);

                GameObject fill = ZombieModeUIHelper.CreateRect("FireFill", track.transform,
                    Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f));
                _barFill = fill.GetComponent<RectTransform>();
                _barFill.offsetMin = Vector2.zero;
                _barFill.offsetMax = Vector2.zero;
                _barFillImage = fill.AddComponent<Image>();
                _barFillImage.color = BossRushUIColors.WarningText;
                _barFillImage.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(_barFillImage, 3, BossRushUISkinPart.Hairline);
                fill.SetActive(false);
                _barShown = 0f;
                _barTarget = 0f;
            }
            catch (Exception e)
            {
                // 构建失败只记一次，之后不再重试——每帧重试会刷爆日志
                _buildFailed = true;
                ModBehaviour.DevLog("[ModeF] [WARNING] 状态卡构建失败（降级无状态卡）: " + e.Message);
                Dispose();
                _buildFailed = true;
            }
        }

        /// <summary>单行文本：不缩字、不换行；框高按字号 ×1.45+4 给足（TMP Ellipsis 首行放不下会整行清空）。</summary>
        private static TextMeshProUGUI CreateLine(string name, Transform parent, float fontSize,
            TextAlignmentOptions alignment, Color color, float x, float top, float width, float height, bool anchorRight)
        {
            TextMeshProUGUI text = ZombieModeUIHelper.CreateText(name, parent, string.Empty, fontSize,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -top), new Vector2(width, height),
                alignment, color);
            text.rectTransform.pivot = new Vector2(anchorRight ? 1f : 0f, 1f);
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.margin = Vector4.zero;
            text.richText = true;
            return text;
        }

        /// <summary>Mode F 不在跑（或刚结束）：收起画布，快照作废，下次出现时无条件重写一遍。</summary>
        private static void Hide()
        {
            if (_canvas != null && _canvas.enabled) _canvas.enabled = false;
            _shownPhase = ModeFPhase.None;
            _shownSeconds = int.MinValue;
            _shownFire = int.MinValue;
            _shownOverload = -1;
        }

        /// <summary>ExitModeF 与 Mode F 模块销毁时调用。幂等。</summary>
        internal static void Dispose()
        {
            try
            {
                if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeF] [WARNING] 销毁状态卡失败: " + e.Message);
            }
            _canvas = null;
            _phaseText = null;
            _timerText = null;
            _fireLabel = null;
            _fireValue = null;
            _barFill = null;
            _barFillImage = null;
            _buildFailed = false;
            _shownPhase = ModeFPhase.None;
            _shownSeconds = int.MinValue;
            _shownFire = int.MinValue;
            _shownOverload = -1;
            _barShown = 0f;
            _barTarget = 0f;
            _overloadPeak = 0f;
        }
    }
}
