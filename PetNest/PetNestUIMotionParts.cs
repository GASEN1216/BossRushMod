// ============================================================================
// PetNestUIMotionParts.cs - 遗种巢界面的小动效组件（2026-10-17 UI 与交互对照审查 A 区）
// ============================================================================
// owner：「整体 UI 交互要有高级感、流畅感，不要有塑料感，该加动画的地方就加」。
// 共同纪律（Common/UI/AGENTS.md §4.14）：
//   - 缓动只用 BossRushUI.EaseOut（位移）与 BossRushUI.SmoothStep（原地淡变），不引第三方补间库；
//   - 一律走 unscaled 时间（模态租约把 timeScale 压到 0），暂停菜单开着时不推进；
//   - 播完即停（enabled = false 或销毁自身），常态零开销；不做回弹过冲，只有奖励揭晓允许一次轻微放大。
// 这些组件挂在遗种巢自己的界面物体上、随界面销毁；不建 canvas、不碰 sortingOrder、不订阅静态事件
// （PetNestPageKeys 例外：订阅官方翻页事件，私有布尔防重、OnDisable / OnDestroy 退订，AGENTS §4.6）。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>Graphic 颜色从 A 渐变到 B（选中描边淡入 / 淡回、成功强调）。</summary>
    internal sealed class PetNestColorTween : MonoBehaviour
    {
        private Graphic _graphic;
        private Color _from;
        private Color _to;
        private float _seconds;
        private float _elapsed;

        internal static void Play(Graphic graphic, Color from, Color to, float seconds)
        {
            if (graphic == null) return;
            PetNestColorTween tween = graphic.GetComponent<PetNestColorTween>();
            if (tween == null) tween = graphic.gameObject.AddComponent<PetNestColorTween>();
            tween._graphic = graphic;
            tween._from = from;
            tween._to = to;
            tween._seconds = Mathf.Max(0.01f, seconds);
            tween._elapsed = 0f;
            graphic.color = from;
            tween.enabled = true;
        }

        private void Update()
        {
            if (_graphic == null) { enabled = false; return; }
            if (BossRushUI.IsGamePaused()) return;
            _elapsed += Time.unscaledDeltaTime;
            float t = BossRushUI.SmoothStep(Mathf.Clamp01(_elapsed / _seconds));
            _graphic.color = Color.LerpUnclamped(_from, _to, t);
            if (_elapsed >= _seconds) enabled = false;
        }
    }

    /// <summary>CanvasGroup 透明度淡入（右栏详情切换、浮层卡出现）。从 startAlpha 起，避免整块闪黑。</summary>
    internal sealed class PetNestFadeIn : MonoBehaviour
    {
        private CanvasGroup _group;
        private float _from;
        private float _seconds;
        private float _elapsed;

        internal static void Play(GameObject target, float startAlpha, float seconds)
        {
            if (target == null) return;
            PetNestFadeIn fade = target.GetComponent<PetNestFadeIn>();
            if (fade == null) fade = target.AddComponent<PetNestFadeIn>();
            fade._group = target.GetComponent<CanvasGroup>();
            if (fade._group == null) fade._group = target.AddComponent<CanvasGroup>();
            fade._from = Mathf.Clamp01(startAlpha);
            fade._seconds = Mathf.Max(0.01f, seconds);
            fade._elapsed = 0f;
            fade._group.alpha = fade._from;
            fade.enabled = true;
        }

        private void Update()
        {
            if (_group == null) { enabled = false; return; }
            if (BossRushUI.IsGamePaused()) return;
            _elapsed += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(_from, 1f, BossRushUI.SmoothStep(Mathf.Clamp01(_elapsed / _seconds)));
            if (_elapsed >= _seconds)
            {
                _group.alpha = 1f;
                enabled = false;
            }
        }
    }

    /// <summary>横向滑到目标 x（页签下的指示条，审查 A-2）。</summary>
    internal sealed class PetNestSlideX : MonoBehaviour
    {
        private RectTransform _rect;
        private float _from;
        private float _to;
        private float _seconds;
        private float _elapsed;

        internal static void Play(RectTransform rect, float targetX, float seconds, bool instant)
        {
            if (rect == null) return;
            PetNestSlideX slide = rect.GetComponent<PetNestSlideX>();
            if (slide == null) slide = rect.gameObject.AddComponent<PetNestSlideX>();
            slide._rect = rect;
            slide._from = rect.anchoredPosition.x;
            slide._to = targetX;
            slide._seconds = Mathf.Max(0.01f, seconds);
            slide._elapsed = 0f;
            if (instant || Mathf.Approximately(slide._from, targetX))
            {
                rect.anchoredPosition = new Vector2(targetX, rect.anchoredPosition.y);
                slide.enabled = false;
                return;
            }
            slide.enabled = true;
        }

        private void Update()
        {
            if (_rect == null) { enabled = false; return; }
            if (BossRushUI.IsGamePaused()) return;
            _elapsed += Time.unscaledDeltaTime;
            float x = Mathf.Lerp(_from, _to, BossRushUI.EaseOut(Mathf.Clamp01(_elapsed / _seconds)));
            _rect.anchoredPosition = new Vector2(x, _rect.anchoredPosition.y);
            if (_elapsed >= _seconds) enabled = false;
        }
    }

    /// <summary>缩放脉冲 1 → peak → 1（升级的等级字、揭晓结果）。只动 localScale，到位即停。</summary>
    internal sealed class PetNestPulse : MonoBehaviour
    {
        private Transform _target;
        private Vector3 _base;
        private float _peak;
        private float _seconds;
        private float _elapsed;
        private const float RisePortion = 0.35f;

        internal static void Play(Transform target, float peak, float seconds)
        {
            if (target == null) return;
            PetNestPulse pulse = target.GetComponent<PetNestPulse>();
            if (pulse == null)
            {
                pulse = target.gameObject.AddComponent<PetNestPulse>();
                pulse._base = target.localScale;
            }
            else if (!pulse.enabled)
            {
                pulse._base = target.localScale;
            }
            pulse._target = target;
            pulse._peak = peak;
            pulse._seconds = Mathf.Max(0.05f, seconds);
            pulse._elapsed = 0f;
            pulse.enabled = true;
        }

        /// <summary>
        /// 别处给 target 写了新的绝对缩放（崽升级换体型）：脉冲改以新值为基准，播完回到新值，
        /// 不把升级后的体型还原成旧值（审查 CR-2026-10-18-017）。没在播时什么都不做。
        /// </summary>
        internal static void Rebase(Transform target)
        {
            if (target == null) return;
            PetNestPulse pulse = target.GetComponent<PetNestPulse>();
            if (pulse != null && pulse.enabled) pulse._base = target.localScale;
        }

        private void Update()
        {
            if (_target == null) { enabled = false; return; }
            if (BossRushUI.IsGamePaused()) return;
            _elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_elapsed / _seconds);
            // 前 35% 冲到峰值（EaseOut），后 65% 原地缓回（SmoothStep），不过冲；只用共享的两条缓动
            float k = t < RisePortion
                ? BossRushUI.EaseOut(t / RisePortion)
                : 1f - BossRushUI.SmoothStep((t - RisePortion) / (1f - RisePortion));
            _target.localScale = _base * Mathf.Lerp(1f, _peak, k);
            if (_elapsed >= _seconds)
            {
                _target.localScale = _base;
                enabled = false;
            }
        }
    }

    /// <summary>数字滚动（审查 A-3）：TMP 上的整数从旧值滚到新值，格式由调用方给。</summary>
    internal sealed class PetNestCountUp : MonoBehaviour
    {
        private TMP_Text _text;
        private int _from;
        private int _to;
        private Func<int, string> _format;
        private float _seconds;
        private float _elapsed;

        internal static void Play(TMP_Text text, int from, int to, Func<int, string> format, float seconds)
        {
            if (text == null || format == null) return;
            if (from == to)
            {
                text.text = format(to);
                return;
            }
            PetNestCountUp count = text.GetComponent<PetNestCountUp>();
            if (count == null) count = text.gameObject.AddComponent<PetNestCountUp>();
            count._text = text;
            count._from = from;
            count._to = to;
            count._format = format;
            count._seconds = Mathf.Max(0.05f, seconds);
            count._elapsed = 0f;
            text.text = format(from);
            count.enabled = true;
        }

        private void Update()
        {
            if (_text == null || _format == null) { enabled = false; return; }
            if (BossRushUI.IsGamePaused()) return;
            _elapsed += Time.unscaledDeltaTime;
            float t = BossRushUI.EaseOut(Mathf.Clamp01(_elapsed / _seconds));
            int value = Mathf.RoundToInt(Mathf.Lerp(_from, _to, t));
            string next = _format(value);
            if (!string.Equals(_text.text, next, StringComparison.Ordinal)) _text.text = next;
            if (_elapsed >= _seconds) enabled = false;
        }
    }

    /// <summary>
    /// 底部轻提示（审查 A-4，Android Snackbar 口径）：成功操作的一句确认，从下往上淡入、停 2.2 秒、淡出后收起。
    /// 一块面板只有一个实例，连续成功时直接换字重播。不吃点击。
    /// </summary>
    internal sealed class PetNestToast : MonoBehaviour
    {
        private const float InSeconds = 0.16f;
        private const float HoldSeconds = 2.2f;
        private const float OutSeconds = 0.3f;
        private const float Rise = 10f;

        private CanvasGroup _group;
        private RectTransform _rect;
        private TextMeshProUGUI _label;
        private float _baseY;
        private float _elapsed;

        internal static PetNestToast Create(Transform parent, Vector2 position)
        {
            GameObject root = ZombieModeUIHelper.CreateRect("PetNestToast", parent, new Vector2(0.5f, 0.5f), new Vector2(520f, 44f));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            Image plate = root.AddComponent<Image>();
            plate.color = BossRushUIColors.SurfaceRaised;
            plate.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(plate, 10, BossRushUISkinPart.Card);
            BossRushUI.ApplyPanelStroke(plate, 10, BossRushUISkinPart.Card, BossRushUIColors.SuccessText);

            PetNestToast toast = root.AddComponent<PetNestToast>();
            toast._rect = rect;
            toast._baseY = position.y;
            toast._group = root.AddComponent<CanvasGroup>();
            toast._group.blocksRaycasts = false;
            toast._group.interactable = false;
            toast._label = ZombieModeUIHelper.CreateText("Label", root.transform, string.Empty, 17f,
                Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-28f, 0f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            toast._label.raycastTarget = false;
            toast._label.enableWordWrapping = false;
            toast._label.enableAutoSizing = true;
            toast._label.fontSizeMin = 13f;
            toast._label.fontSizeMax = 17f;
            BossRushUI.ApplyGameFont(toast._label);
            root.SetActive(false);
            return toast;
        }

        internal void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _label.text = text;
            _elapsed = 0f;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Apply();
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused()) return;
            _elapsed += Time.unscaledDeltaTime;
            Apply();
            if (_elapsed >= InSeconds + HoldSeconds + OutSeconds) gameObject.SetActive(false);
        }

        private void Apply()
        {
            float alpha;
            float offset;
            if (_elapsed < InSeconds)
            {
                float t = BossRushUI.EaseOut(_elapsed / InSeconds);
                alpha = t;
                offset = -Rise * (1f - t);
            }
            else if (_elapsed < InSeconds + HoldSeconds)
            {
                alpha = 1f;
                offset = 0f;
            }
            else
            {
                alpha = 1f - BossRushUI.SmoothStep(Mathf.Clamp01((_elapsed - InSeconds - HoldSeconds) / OutSeconds));
                offset = 0f;
            }
            _group.alpha = alpha;
            _rect.anchoredPosition = new Vector2(_rect.anchoredPosition.x, _baseY + offset);
        }
    }

    /// <summary>
    /// 揭晓爆发（审查 A-5）：一圈扩散的光环 + 若干颗向外飞的星光，0.5 秒播完自毁。
    /// 颜色由稀有度决定（普通 Accent、炫彩两色、异色金）。贴图取共享粒子纹理里的星光与光点，Sprite 随自身销毁。
    /// </summary>
    internal sealed class PetNestUIBurst : MonoBehaviour
    {
        private const float Seconds = 0.55f;

        private RectTransform _ring;
        private Image _ringImage;
        private RectTransform[] _stars;
        private Image[] _starImages;
        private Vector2[] _directions;
        private float _radius;
        private float _ringScale = 1f;
        private float _elapsed;
        private Sprite _starSprite;

        internal static void Play(Transform parent, Vector2 position, float size, Color colorA, Color colorB, int starCount)
        {
            if (parent == null) return;
            GameObject root = ZombieModeUIHelper.CreateRect("PetNestBurst", parent, new Vector2(0.5f, 0.5f), new Vector2(size, size));
            root.GetComponent<RectTransform>().anchoredPosition = position;
            PetNestUIBurst burst = root.AddComponent<PetNestUIBurst>();
            burst._radius = size * 0.75f;

            // 描边贴图圆角上限 32：环本身做成 64 见方的正圆，靠缩放放大到目标尺寸
            GameObject ring = ZombieModeUIHelper.CreateRect("Ring", root.transform, new Vector2(0.5f, 0.5f), new Vector2(64f, 64f));
            burst._ringScale = size / 64f;
            burst._ring = ring.GetComponent<RectTransform>();
            burst._ringImage = ring.AddComponent<Image>();
            burst._ringImage.sprite = BossRushUI.GetStrokeSprite(32);
            burst._ringImage.type = Image.Type.Sliced;
            burst._ringImage.color = colorA;
            burst._ringImage.raycastTarget = false;

            try
            {
                Texture2D texture = BossRushParticleTextures.Get(BossRushParticleShape.Star);
                if (texture != null)
                {
                    burst._starSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
            }
            catch (Exception)
            {
                burst._starSprite = null;
            }

            int count = Mathf.Clamp(starCount, 0, 16);
            burst._stars = new RectTransform[count];
            burst._starImages = new Image[count];
            burst._directions = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                // 方向均匀分布（确定性，不用随机数：演出层不 roll）
                float angle = (i / (float)Mathf.Max(1, count)) * Mathf.PI * 2f + 0.3f;
                burst._directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                GameObject star = ZombieModeUIHelper.CreateRect("Star", root.transform, new Vector2(0.5f, 0.5f), new Vector2(26f, 26f));
                burst._stars[i] = star.GetComponent<RectTransform>();
                Image image = star.AddComponent<Image>();
                image.sprite = burst._starSprite;
                image.color = i % 2 == 0 ? colorA : colorB;
                image.raycastTarget = false;
                if (burst._starSprite == null) image.enabled = false;
                burst._starImages[i] = image;
            }
            burst.Apply();
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused()) return;
            _elapsed += Time.unscaledDeltaTime;
            Apply();
            if (_elapsed >= Seconds) Destroy(gameObject);
        }

        private void Apply()
        {
            float t = Mathf.Clamp01(_elapsed / Seconds);
            float grow = BossRushUI.EaseOut(t);
            float fade = 1f - BossRushUI.SmoothStep(t);
            if (_ring != null)
            {
                _ring.localScale = Vector3.one * (_ringScale * Mathf.Lerp(0.55f, 1.6f, grow));
                Color c = _ringImage.color;
                c.a = fade;
                _ringImage.color = c;
            }
            for (int i = 0; i < _stars.Length; i++)
            {
                if (_stars[i] == null) continue;
                _stars[i].anchoredPosition = _directions[i] * _radius * grow;
                _stars[i].localScale = Vector3.one * Mathf.Lerp(1.2f, 0.4f, t);
                Color c = _starImages[i].color;
                c.a = fade;
                _starImages[i].color = c;
            }
        }

        private void OnDestroy()
        {
            if (_starSprite != null) Destroy(_starSprite);
        }
    }

    /// <summary>
    /// 官方翻页键（Q / E 与手柄肩键，官方商店与搜刮界面同一组事件）切遗种巢的页签（审查 G-1）。
    /// 订阅幂等（私有布尔），OnDisable / OnDestroy 退订；面板上压着弹窗或演出时不响应。
    /// </summary>
    internal sealed class PetNestPageKeys : MonoBehaviour
    {
        private Action<int> _onStep;
        private Func<bool> _isCovered;
        private bool _subscribed;

        internal static PetNestPageKeys Attach(GameObject root, Action<int> onStep, Func<bool> isCovered)
        {
            if (root == null || onStep == null) return null;
            PetNestPageKeys keys = root.GetComponent<PetNestPageKeys>();
            if (keys == null) keys = root.AddComponent<PetNestPageKeys>();
            keys._onStep = onStep;
            keys._isCovered = isCovered;
            keys.Subscribe();
            return keys;
        }

        private void OnEnable()
        {
            if (_onStep != null) Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            try
            {
                global::UIInputManager.OnNextPage += HandleNext;
                global::UIInputManager.OnPreviousPage += HandlePrevious;
                _subscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 订阅翻页键失败（只能点页签切换）: " + e.Message);
            }
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            try
            {
                global::UIInputManager.OnNextPage -= HandleNext;
                global::UIInputManager.OnPreviousPage -= HandlePrevious;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 退订翻页键失败: " + e.Message);
            }
        }

        private void HandleNext(global::UIInputEventData data) { Step(data, 1); }

        private void HandlePrevious(global::UIInputEventData data) { Step(data, -1); }

        private void Step(global::UIInputEventData data, int delta)
        {
            Action<int> onStep = _onStep;
            if (onStep == null || !isActiveAndEnabled) return;
            if (data != null && data.Used) return;
            try
            {
                if (_isCovered != null && _isCovered()) return;
            }
            catch (Exception)
            {
                return;
            }
            if (data != null) data.Use();
            try { onStep(delta); }
            catch (Exception e) { ModBehaviour.DevLog("[PetNest] [WARNING] 翻页回调失败: " + e.Message); }
        }
    }
}
