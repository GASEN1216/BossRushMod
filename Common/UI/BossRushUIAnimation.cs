// ============================================================================
// BossRushUIAnimation.cs - 共享 UI 库的两个动效组件
// ============================================================================
// 从 BossRushUI.cs 提取（AGENTS §4.15：有行数预算的大文件腾空间时拆到新文件）。
// 提取时顺带修了两处（2026-09-14 UI 优化对照审核）：
//   - BossRushUIEntranceAnimation 播放途中再次调用，不再把「目标 - 余下位移」当成新目标；
//   - BossRushUIOpenAnimation 挂在根画布上时只淡入、不写 localScale（根画布的缩放由 Canvas 按 CanvasScaler 驱动），
//     缓动改调 BossRushUI.SmoothStep，不再手写一份。
// 缓动的分工与暂停门见 BossRushUI.EaseOut / SmoothStep / IsGamePaused。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 子元素错峰入场：延迟 <c>delay</c> 秒后，用 ease-out 在 <c>duration</c> 秒内
    /// 淡入并从下方 <c>rise</c> 像素升到位。
    ///
    /// 【为什么按钮从第一帧就是可点的】只改 CanvasGroup 的 alpha 与 anchoredPosition，
    /// 不动 <c>interactable</c> / <c>blocksRaycasts</c>。玩家要是第一帧就按数字键，
    /// 照常生效——动效绝不能变成输入延迟。
    ///
    /// 【暂停时停推进】走 unscaled 时间，所以必须自己看 <see cref="BossRushUI.IsGamePaused"/>。
    /// </summary>
    internal sealed class BossRushUIEntranceAnimation : MonoBehaviour
    {
        private CanvasGroup canvasGroup;
        private RectTransform rect;
        private Vector2 target;
        private float delay, duration, rise, elapsed;
        private bool playing;

        /// <summary>挂上并立刻起播。重复调用会重置。</summary>
        internal static void Play(GameObject go, float delay, float duration, float rise)
        {
            if (go == null)
            {
                return;
            }

            BossRushUIEntranceAnimation animation = go.GetComponent<BossRushUIEntranceAnimation>();
            if (animation == null)
            {
                animation = go.AddComponent<BossRushUIEntranceAnimation>();
            }
            animation.Restart(delay, duration, rise);
        }

        private void Restart(float delaySeconds, float durationSeconds, float riseUnits)
        {
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }
            rect = transform as RectTransform;
            // 目标位置必须在第一帧之前记下来：之后每帧都是「目标 - 余下的位移」，
            // 不是「当前位置 + 增量」，否则被打断一次就再也回不到正确位置。
            // 播放途中再次调用时，anchoredPosition 正停在「目标 - 余下的位移」上，这时重取会把偏移当成目标，
            // 每重播一次就往下沉一截——所以只在没在播的时候取。
            if (!playing)
            {
                target = rect != null ? rect.anchoredPosition : Vector2.zero;
            }
            delay = Mathf.Max(0f, delaySeconds);
            duration = Mathf.Max(0.01f, durationSeconds);
            rise = riseUnits;
            elapsed = 0f;
            playing = true;
            canvasGroup.alpha = 0f;
            if (rect != null)
            {
                rect.anchoredPosition = new Vector2(target.x, target.y - rise);
            }
        }

        private void Update()
        {
            if (!playing || BossRushUI.IsGamePaused())
            {
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            if (elapsed < delay)
            {
                return;
            }

            float t = Mathf.Clamp01((elapsed - delay) / duration);
            float eased = BossRushUI.EaseOut(t);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = eased;
            }
            if (rect != null)
            {
                rect.anchoredPosition = new Vector2(target.x, Mathf.Lerp(target.y - rise, target.y, eased));
            }

            if (t < 1f)
            {
                return;
            }

            playing = false;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
            }
            if (rect != null)
            {
                rect.anchoredPosition = target;
            }
        }
    }

    /// <summary>
    /// 面板打开动画：0.16 秒淡入，0.22 秒内从 0.94 放大到 1（2026-09-23 前是 0.18 秒 / 0.96，见常量处注释）。
    /// 用 unscaledDeltaTime，模态会把 timeScale 置 0。
    ///
    /// 【时长下限】0.12 秒在 60fps 下只有 7 帧，玩家看到的基本是「面板直接出现」，等于白做；
    /// 缩放 0.22 秒（约 13 帧）、淡入 0.16 秒是仍然不拖沓、但看得出「它是长出来的」的口径。
    ///
    /// 【两条曲线】透明度是原地变化，用 SmoothStep 两头都收；缩放是尺寸真的在变，用 EaseOut——
    /// 起手快、落定慢，像东西弹出来停稳。旧版两者共用 SmoothStep，起步太慢，4% 的缩放几乎看不出。
    ///
    /// 【挂在根画布上时不缩放】根画布 RectTransform 的缩放由 Canvas 按 CanvasScaler 的缩放系数驱动，
    /// 写 localScale 要么被覆盖、要么和缩放系数打架抖一帧。这种情况只做淡入。
    /// </summary>
    internal sealed class BossRushUIOpenAnimation : MonoBehaviour
    {
        // 2026-09-23 审美审查 UD-04：旧口径 0.18 秒、0.96 起、缩放与透明度共用一条 SmoothStep，
        // 4% 的缩放配上起步很慢的 SmoothStep 几乎看不出「长出来」。现在拆成两条：
        // 透明度 0.16 秒 SmoothStep（原地淡入），缩放 0.22 秒 EaseOut 从 0.94 起（真的在变大，按位移类动效走 EaseOut）。
        private const float DurationSeconds = 0.22f;
        private const float FadeSeconds = 0.16f;
        private const float StartScale = 0.94f;

        private CanvasGroup canvasGroup;
        private float elapsed;
        private bool playing;
        private bool scales = true;

        internal void Restart()
        {
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            Canvas canvas = gameObject.GetComponent<Canvas>();
            scales = canvas == null || !canvas.isRootCanvas;
            elapsed = 0f;
            playing = true;
            canvasGroup.alpha = 0f;
            if (scales)
            {
                transform.localScale = Vector3.one * StartScale;
            }
        }

        private void Update()
        {
            if (!playing)
            {
                return;
            }

            // 暂停菜单开着时停推进：本动画走 unscaled 时间，不停下来的话它会在
            // sortingOrder 10000 的暂停菜单背后悄悄播完，玩家回来只看到一个已经就位的面板。
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / DurationSeconds);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = BossRushUI.SmoothStep(elapsed / FadeSeconds);
            }
            if (scales)
            {
                transform.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, BossRushUI.EaseOut(t));
            }

            if (t >= 1f)
            {
                playing = false;
                if (scales)
                {
                    transform.localScale = Vector3.one;
                }
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1f;
                }
            }
        }
    }
}
