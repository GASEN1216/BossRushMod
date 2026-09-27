// ============================================================================
// ModeHBetRevealView.cs - 鸭王杯押钱的「开盘」揭晓（2026-09-24）
// ============================================================================
// owner：「进场时弄个像许愿台那样的抽奖动画，格子就是压上去的赌注」。押的是钱之后，格子是五档赔率，
// 每格写「赔率 xN · 赢了拿回多少」；高亮像许愿台的轮带一样从快到慢扫过去，停在这一场开出来的赔率上。
// 押金与赔率已在锁盘时确定；生成阶段等待 IsPlaying 结束，避免开打后遮挡视野。
// 画布不接管输入，约 3.5 秒后淡出；暂停菜单开着时停住。
// 不自建 Update（ModeHPerformanceGuard）：由宿主每帧回调 OnUpdateInternal 调 Tick，没在播时 O(1) 早返。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class ModeHBetRevealView : MonoBehaviour
    {
        private const string RootName = "BossRush_ModeHBetReveal";
        private const float SweepSeconds = 2.2f;
        private const float HoldSeconds = 1.3f;
        private const float CellWidth = 160f;
        private const float CellHeight = 112f;
        private const float CellGap = 14f;
        /// <summary>高亮在五格间来回扫的圈数（落点之前）：两圈 + 落点。</summary>
        private const int SweepLaps = 2;

        private static ModeHBetRevealView _instance;

        /// <summary>开盘动画是否仍在屏幕上。锁盘流程用它把生成阶段顺延到揭晓结束之后。</summary>
        internal static bool IsPlaying { get { return _instance != null; } }

        private Canvas _canvas;
        private CanvasGroup _group;
        private readonly Image[] _cells = new Image[ModeHConfig.MaxOdds];
        private readonly Image[] _strokes = new Image[ModeHConfig.MaxOdds];
        private int _target;
        private float _elapsed;
        private int _lastLit = -1;
        private bool _landed;

        /// <summary>
        /// 播一次开盘揭晓。再次调用会先收掉上一个。失败只记日志，不影响比赛。
        /// <paramref name="items"/> 为真时是押背包物品：<paramref name="amount"/> 是估值合计，格子里写「奖品约值多少」（东西本来就留着）。
        /// </summary>
        internal static void Play(long amount, int odds, bool items)
        {
            try
            {
                Stop();
                GameObject host = new GameObject(RootName + "_Host");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _instance = host.AddComponent<ModeHBetRevealView>();
                _instance._target = Mathf.Clamp(odds, ModeHConfig.MinOdds, ModeHConfig.MaxOdds) - 1;
                _instance.Build(amount, items);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeH] 开盘揭晓失败: " + e.Message);
                Stop();
            }
        }

        /// <summary>立即收掉（切图、模式关停、卸载）。幂等。</summary>
        internal static void Stop()
        {
            ModeHBetRevealView closing = _instance;
            _instance = null;
            if (closing != null && closing.gameObject != null) UnityEngine.Object.Destroy(closing.gameObject);
        }

        private void Build(long amount, bool items)
        {
            // 不接管输入；宿主等待揭晓收场后才开始生成战场。
            _canvas = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.Modal, false);
            _canvas.transform.SetParent(transform, false);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            float width = ModeHConfig.MaxOdds * CellWidth + (ModeHConfig.MaxOdds - 1) * CellGap + 64f;
            GameObject surface = ZombieModeUIHelper.CreateRect("Surface", _canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(width, 300f));
            surface.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 120f);
            Image surfaceImage = surface.AddComponent<Image>();
            surfaceImage.color = BossRushUIColors.Surface;
            surfaceImage.raycastTarget = false;
            BossRushUI.ApplyFramedPanelSkin(surfaceImage, 14, BossRushUISkinPart.Panel);

            TextMeshProUGUI title = ZombieModeUIHelper.CreateText("Title", surface.transform,
                L10n.T("开盘", "Odds are in"), 28f, new Vector2(0f, 110f), new Vector2(width - 48f, 44f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            title.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(title);
            TextMeshProUGUI stake = ZombieModeUIHelper.CreateText("Stake", surface.transform,
                items
                    ? L10n.T("押你的选手赢：背包物品，估值 ", "You bet on your fighter: backpack items worth ") + ModeHRuntimeModule.FormatMoney(amount)
                    : L10n.T("押你的选手赢：", "You bet on your fighter: ") + ModeHRuntimeModule.FormatMoney(amount),
                18f, new Vector2(0f, 74f), new Vector2(width - 48f, 32f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(stake);

            float startX = -((ModeHConfig.MaxOdds - 1) * (CellWidth + CellGap)) * 0.5f;
            for (int i = 0; i < ModeHConfig.MaxOdds; i++)
            {
                int odds = i + 1;
                GameObject cell = BossRushUI.CreateCard("Cell_" + odds, surface.transform,
                    new Vector2(startX + i * (CellWidth + CellGap), -20f), new Vector2(CellWidth, CellHeight),
                    BossRushUIColors.SurfaceRaised, BossRushUIColors.Accent, false);
                _cells[i] = cell.GetComponent<Image>();
                _cells[i].raycastTarget = false;
                Transform stroke = cell.transform.Find("Stroke");
                _strokes[i] = stroke != null ? stroke.GetComponent<Image>() : null;
                TextMeshProUGUI oddsText = ZombieModeUIHelper.CreateText("Odds", cell.transform, ModeHRuntimeModule.FormatPayoutMultiplier(odds), 30f,
                    new Vector2(0f, 18f), new Vector2(CellWidth - 16f, 48f), TextAlignmentOptions.Center,
                    BossRushUIColors.WarningText);
                oddsText.fontStyle = FontStyles.Bold;
                BossRushUI.ApplyGameFont(oddsText);
                long payout = ModeHCashBetService.ComputePayout(amount, odds);
                TextMeshProUGUI payText = ZombieModeUIHelper.CreateText("Pay", cell.transform,
                    items
                        ? L10n.T("奖品约值 ", "Prizes ~") + ModeHRuntimeModule.FormatMoney(Math.Max(0L, payout - amount))
                        : L10n.T("拿回 ", "Pays ") + ModeHRuntimeModule.FormatMoney(payout),
                    15f, new Vector2(0f, -26f), new Vector2(CellWidth - 16f, 28f), TextAlignmentOptions.Center,
                    BossRushUIColors.TextSecondary);
                BossRushUI.ApplyGameFont(payText);
            }
            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>宿主每帧调用（ModeHRuntimeModule.OnUpdateInternal）。没在播时第一行就返回。</summary>
        internal static void Tick()
        {
            if (_instance != null) _instance.Advance();
        }

        private void Advance()
        {
            if (BossRushUI.IsGamePaused()) return;
            _elapsed += Time.unscaledDeltaTime;
            if (!_landed)
            {
                // 与许愿台轮带同一种手感：一条 ease-out 曲线从最快减到停，总步数 = 若干整圈 + 落点
                float t = Mathf.Clamp01(_elapsed / SweepSeconds);
                float eased = BossRushUI.EaseOut(t);
                int totalSteps = SweepLaps * ModeHConfig.MaxOdds + _target;
                int lit = Mathf.Min(totalSteps, Mathf.FloorToInt(eased * totalSteps)) % ModeHConfig.MaxOdds;
                if (lit != _lastLit)
                {
                    Highlight(lit, false);
                    _lastLit = lit;
                }
                if (t >= 1f)
                {
                    _landed = true;
                    Highlight(_target, true);
                }
                return;
            }
            float hold = _elapsed - SweepSeconds;
            if (hold > HoldSeconds)
            {
                if (_group != null) _group.alpha = Mathf.Clamp01(1f - (hold - HoldSeconds) / 0.3f);
                if (hold > HoldSeconds + 0.3f) Stop();
            }
        }

        private void Highlight(int index, bool final)
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                bool on = i == index;
                if (_cells[i] != null)
                {
                    _cells[i].color = on
                        ? Color.Lerp(BossRushUIColors.SurfaceRaised, BossRushUIColors.Accent, final ? 0.3f : 0.18f)
                        : BossRushUIColors.SurfaceRaised;
                }
                if (_strokes[i] != null)
                {
                    _strokes[i].color = on ? (final ? BossRushUIColors.WarningText : BossRushUIColors.Accent) : BossRushUIColors.Stroke;
                }
            }
            if (final && index >= 0 && index < _cells.Length && _cells[index] != null)
            {
                BossRushUIEntranceAnimation.Play(_cells[index].gameObject, 0f, 0.18f, 8f);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
