// ============================================================================
// RandomEventHud.cs — 随机事件「鸭生无常」活动事件徽章
// ============================================================================
// 模块职责：
//   在屏幕左上角显示当前活动事件的图标 + 名称 + 剩余秒数。
//   由 RandomEventsRuntimeModule.OnUpdate 每帧驱动；无活动事件时隐藏并早返。
//
// UI 硬约束（AGENTS 4.14 + tests/BossRushUISharedLibraryGuard.py）：
//   - Canvas 一律 BossRushUI.CreateCanvasRoot(..., interactive:false)：
//     内部已调 ZombieModeUIHelper.ConfigureCanvasScaler 并关掉 GraphicRaycaster，
//     本文件**不得**自己 AddComponent<CanvasScaler>，也不得手写缩放模式参数。
//   - sortingOrder 复用 BossRushUILayers.HudOverlay 常量，禁止魔法数字，
//     也禁止在共享 UI 库里新增本系统专属层级（那不是本任务的文件）。
//   - 颜色只用 BossRushUIColors 设计 token；文本一律走 TMP 与共享字体解析器，
//     禁止 legacy 文本组件、禁止内置英文字体（渲染不了中文）。
//   - 全部 Graphic 的 raycastTarget 置 false：HUD 必须让点击穿透。
//
// 图标约定：
//   Assets/ui/random_events/evt_<id>.png（id = RandomEventId 的整数值，如 evt_1.png）。
//   缺图直接 fallback 成纯文字徽章：不报错、不刷屏、不重复尝试读盘。
//   自造 Sprite/Texture 带 HideAndDontSave，必须在 ResetStaticCaches 里显式销毁。
//
// 性能：Tick 是每帧路径。只在「秒数整数值变化」或「事件切换」时才写 TMP.text，
//       其余帧只有几次引用比较与一次 int 比较，零分配、零日志。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>活动事件徽章（图标 + 名称 + 剩余秒数）。非交互 HUD，点击必须穿透。</summary>
    internal static class RandomEventHud
    {
        private const float IconSize = 44f;
        /// <summary>图标槽左边距与图标到名字的间距。缺图时名字左移 IconSize + IconGap，不留一块空白（UA-27）。</summary>
        private const float IconLeft = 14f;
        private const float IconGap = 10f;
        /// <summary>剩余秒数到这个值以下，秒数与进度条改成 WarningText（UA-28）。</summary>
        private const float WarningSeconds = 5f;
        /// <summary>事件结束时的淡出时长（UA-28：旧写法一帧消失）。</summary>
        private const float FadeOutSeconds = 0.15f;

        private static Canvas _canvas;
        private static GameObject _badge;
        private static CanvasGroup _badgeGroup;
        private static Image _iconImage;
        private static TextMeshProUGUI _nameText;
        private static TextMeshProUGUI _timerText;
        private static Image _progressFill;

        private static RandomEventId _shownId = RandomEventId.None;
        private static int _shownSeconds = -1;
        private static bool _shownWarning;
        private static bool _buildFailed;
        private static float _fadeOutElapsed = -1f;
        private static float _fadeOutFrom = 1f;

        private static readonly Dictionary<RandomEventId, Sprite> _iconCache =
            new Dictionary<RandomEventId, Sprite>();
        private static readonly List<UnityEngine.Object> _ownedAssets = new List<UnityEngine.Object>();

        /// <summary>由运行时模块 OnUpdate 调用。Director 无活动事件时隐藏并早返。</summary>
        internal static void Tick(RandomEventDirector director)
        {
            try
            {
                if (director == null)
                {
                    FadeOutThenHide();
                    return;
                }

                RandomEventId activeId = director.ActiveEventId;
                if (activeId == RandomEventId.None)
                {
                    FadeOutThenHide();
                    return;
                }

                EnsureBuilt();
                if (_badge == null)
                {
                    return;
                }

                // 从隐藏（或正在淡出）回到显示：这一帧起播入场动画（事件切换分支里判定）。
                bool entering = !_badge.activeSelf || _fadeOutElapsed >= 0f;
                if (!_badge.activeSelf)
                {
                    _badge.SetActive(true);
                }
                CancelFadeOut();

                // 常驻 HUD 跟随官方界面收起（2026-09-14）：背包 / 地图 / 对话 / 捏脸 / 拍照模式（IsOfficialHudHidden）与暂停菜单。
                // 本画布在 HudOverlay（1200），不让位就压在 sortingOrder 100 的官方界面上面。只开关画布、不走 HideImmediate：
                // 后者会清掉已显示的事件与秒数，关掉背包那一帧还要重刷一遍。
                bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
                if (_canvas != null && _canvas.enabled != visible)
                {
                    _canvas.enabled = visible;
                }

                // 事件切换才重刷图标与名称
                if (_shownId != activeId)
                {
                    _shownId = activeId;
                    _shownSeconds = -1;

                    if (_nameText != null)
                    {
                        _nameText.text = director.ActiveEventDisplayName;
                    }

                    Sprite icon = ResolveIcon(activeId);
                    if (_iconImage != null)
                    {
                        _iconImage.sprite = icon;
                        _iconImage.enabled = icon != null;
                    }
                    ApplyNameLayout(icon != null);

                    if (entering)
                    {
                        // 0.25 秒从下方 12 单位淡入升起（UA-28：旧写法一帧切出来）
                        BossRushUIEntranceAnimation.Play(_badge, 0f, 0.25f, 12f);
                    }
                }

                float remaining = director.ActiveRemainingSeconds;

                // 秒数只在整数值变化时写文本，避免每帧字符串分配；等宽数字，跳秒时不左右抖
                int seconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
                if (seconds != _shownSeconds && _timerText != null)
                {
                    _shownSeconds = seconds;
                    _timerText.text = "<mspace=0.6em>" + seconds + "</mspace>s";
                }

                // 底边进度条：每帧只写一个 float（变化超过半个像素才写，避免每帧重建网格）
                if (_progressFill != null)
                {
                    float duration = director.ActiveDurationSeconds;
                    float fill = duration > 0.01f ? Mathf.Clamp01(remaining / duration) : 0f;
                    if (Mathf.Abs(_progressFill.fillAmount - fill) > 0.002f)
                    {
                        _progressFill.fillAmount = fill;
                    }
                }

                // 临近结束：秒数与进度条转 WarningText，只在跨过阈值那一帧改色
                bool warning = remaining <= WarningSeconds;
                if (warning != _shownWarning)
                {
                    _shownWarning = warning;
                    Color tint = warning ? BossRushUIColors.WarningText : BossRushUIColors.Accent;
                    if (_timerText != null) _timerText.color = tint;
                    if (_progressFill != null) _progressFill.color = tint;
                }
            }
            catch (Exception)
            {
                // 每帧路径不刷屏；构建期异常已在 EnsureBuilt 里记过一次
            }
        }

        /// <summary>立即隐藏但保留对象（切场景 / 事件间隔用）。幂等。</summary>
        internal static void HideImmediate()
        {
            try
            {
                _shownId = RandomEventId.None;
                _shownSeconds = -1;
                _fadeOutElapsed = -1f;
                if (_badge != null && _badge.activeSelf)
                {
                    _badge.SetActive(false);
                    if (_badgeGroup != null) _badgeGroup.alpha = 1f;
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 事件结束：0.15 秒淡出后再隐藏（UA-28）。徽章本来就没显示时等同 HideImmediate，
        /// 无事件的每一帧仍是几次字段比较、零分配。走 unscaled 时间，暂停菜单开着时不推进。
        /// 切场景等需要立刻消失的路径直接调 HideImmediate，不经过这里。
        /// </summary>
        private static void FadeOutThenHide()
        {
            if (_badge == null || !_badge.activeSelf)
            {
                HideImmediate();
                return;
            }

            if (_fadeOutElapsed < 0f)
            {
                _fadeOutElapsed = 0f;
                _fadeOutFrom = _badgeGroup != null ? _badgeGroup.alpha : 1f;
            }
            if (!BossRushUI.IsGamePaused())
            {
                _fadeOutElapsed += Time.unscaledDeltaTime;
            }

            float t = Mathf.Clamp01(_fadeOutElapsed / FadeOutSeconds);
            if (_badgeGroup != null)
            {
                _badgeGroup.alpha = Mathf.Lerp(_fadeOutFrom, 0f, BossRushUI.SmoothStep(t));
            }
            if (t >= 1f)
            {
                HideImmediate();
            }
        }

        /// <summary>淡出途中又来了事件：停止淡出、恢复不透明（随后若是新事件会重播入场）。</summary>
        private static void CancelFadeOut()
        {
            if (_fadeOutElapsed < 0f)
            {
                return;
            }
            _fadeOutElapsed = -1f;
            if (_badgeGroup != null)
            {
                _badgeGroup.alpha = 1f;
            }
        }

        /// <summary>
        /// 名字文本的排版：有图标时从图标右侧起排；缺图（9–11 号事件还没出图）时左移到图标槽的位置、宽度补回，
        /// 不在徽章左边留一块像是加载失败的空白（UA-27）。只在事件切换时调用。
        /// </summary>
        private static void ApplyNameLayout(bool hasIcon)
        {
            if (_nameText == null)
            {
                return;
            }
            float textLeft = hasIcon ? IconLeft + IconSize + IconGap : IconLeft + 4f;
            float textWidth = Mathf.Max(60f, RandomEventsTuning.HudBadgeWidth - textLeft - 58f);
            RectTransform rect = _nameText.rectTransform;
            rect.anchoredPosition = new Vector2(textLeft + textWidth * 0.5f, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(textWidth, rect.sizeDelta.y);
        }

        /// <summary>销毁 canvas 与图标缓存。开关关闭 / 宿主销毁时调用。幂等。</summary>
        internal static void Destroy()
        {
            try
            {
                if (_canvas != null && _canvas.gameObject != null)
                {
                    UnityEngine.Object.Destroy(_canvas.gameObject);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 销毁事件徽章失败: " + e.Message);
            }

            _canvas = null;
            _badge = null;
            _badgeGroup = null;
            _iconImage = null;
            _nameText = null;
            _timerText = null;
            _progressFill = null;
            _shownId = RandomEventId.None;
            _shownSeconds = -1;
            _shownWarning = false;
            _fadeOutElapsed = -1f;
            _buildFailed = false;

            ResetStaticCaches();
        }

        /// <summary>释放自造的 Sprite / Texture（HideAndDontSave 不随场景回收）。幂等。</summary>
        internal static void ResetStaticCaches()
        {
            for (int i = _ownedAssets.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (_ownedAssets[i] != null)
                    {
                        UnityEngine.Object.Destroy(_ownedAssets[i]);
                    }
                }
                catch (Exception) { }
            }

            _ownedAssets.Clear();
            _iconCache.Clear();
        }

        // ====================================================================
        // 构建
        // ====================================================================

        private static void EnsureBuilt()
        {
            if (_badge != null || _buildFailed)
            {
                return;
            }

            try
            {
                _canvas = BossRushUI.CreateCanvasRoot(
                    "BossRushRandomEventHud",
                    BossRushUILayers.HudOverlay,
                    false);
                if (_canvas == null)
                {
                    _buildFailed = true;
                    return;
                }

                _badge = BossRushUI.CreateCard(
                    "EventBadge",
                    _canvas.transform,
                    Vector2.zero,
                    new Vector2(RandomEventsTuning.HudBadgeWidth, RandomEventsTuning.HudBadgeHeight),
                    BossRushUIColors.SurfaceRaised,
                    BossRushUIColors.Accent,
                    true);
                if (_badge == null)
                {
                    _buildFailed = true;
                    return;
                }

                // 贴屏幕左上角，marginY 为负值（向下偏移，让开原生 HUD）
                RectTransform badgeRect = _badge.GetComponent<RectTransform>();
                if (badgeRect != null)
                {
                    badgeRect.anchorMin = new Vector2(0f, 1f);
                    badgeRect.anchorMax = new Vector2(0f, 1f);
                    badgeRect.pivot = new Vector2(0f, 1f);
                    badgeRect.anchoredPosition = new Vector2(
                        RandomEventsTuning.HudBadgeMarginX,
                        RandomEventsTuning.HudBadgeMarginY);
                }

                Image badgeImage = _badge.GetComponent<Image>();
                if (badgeImage != null)
                {
                    badgeImage.raycastTarget = false;
                }

                // 入场 / 淡出共用一个 CanvasGroup（BossRushUIEntranceAnimation 也取这一个）
                _badgeGroup = _badge.GetComponent<CanvasGroup>();
                if (_badgeGroup == null)
                {
                    _badgeGroup = _badge.AddComponent<CanvasGroup>();
                }
                _badgeGroup.blocksRaycasts = false;
                _badgeGroup.interactable = false;

                // 图标槽（缺图时 enabled=false，名字左移到这一格，见 ApplyNameLayout）
                GameObject iconObj = ZombieModeUIHelper.CreateRect(
                    "Icon",
                    _badge.transform,
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(IconLeft + IconSize * 0.5f, 0f),
                    new Vector2(IconSize, IconSize),
                    new Vector2(0.5f, 0.5f));
                _iconImage = iconObj.AddComponent<Image>();
                _iconImage.raycastTarget = false;
                _iconImage.preserveAspect = true;
                _iconImage.enabled = false;

                float textLeft = IconLeft + IconSize + IconGap;
                float textWidth = Mathf.Max(60f, RandomEventsTuning.HudBadgeWidth - textLeft - 58f);

                // 字号梯度（UA-28）：名字 20 是主信息，秒数 18 等宽数字是辅助；旧写法两者都是 22，分不出主次。
                _nameText = ZombieModeUIHelper.CreateText(
                    "EventName",
                    _badge.transform,
                    string.Empty,
                    20f,
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(textLeft + textWidth * 0.5f, 2f),
                    new Vector2(textWidth, RandomEventsTuning.HudBadgeHeight - 16f),
                    TextAlignmentOptions.Left,
                    BossRushUIColors.TextPrimary);
                if (_nameText != null)
                {
                    _nameText.raycastTarget = false;
                }

                _timerText = ZombieModeUIHelper.CreateText(
                    "EventTimer",
                    _badge.transform,
                    string.Empty,
                    18f,
                    new Vector2(1f, 0.5f),
                    new Vector2(1f, 0.5f),
                    new Vector2(-32f, 2f),
                    new Vector2(56f, RandomEventsTuning.HudBadgeHeight - 16f),
                    TextAlignmentOptions.Right,
                    BossRushUIColors.Accent);
                if (_timerText != null)
                {
                    _timerText.raycastTarget = false;
                    _timerText.richText = true;
                }

                // 底边 3 单位进度条：剩余 / 总时长，一眼看出「还剩多少」（UA-28）。
                // Filled 必须有 sprite（GetSolidSprite），否则 fillAmount 被忽略、永远满格。
                GameObject track = ZombieModeUIHelper.CreateRect(
                    "ProgressTrack",
                    _badge.transform,
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 7f),
                    new Vector2(-28f, 3f),
                    new Vector2(0.5f, 0f));
                Image trackImage = track.AddComponent<Image>();
                trackImage.sprite = BossRushUI.GetSolidSprite();
                trackImage.color = BossRushUIColors.Divider;
                trackImage.raycastTarget = false;

                GameObject fillObj = ZombieModeUIHelper.CreateRect(
                    "ProgressFill",
                    track.transform,
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero,
                    Vector2.zero,
                    new Vector2(0.5f, 0.5f));
                _progressFill = fillObj.AddComponent<Image>();
                _progressFill.sprite = BossRushUI.GetSolidSprite();
                _progressFill.type = Image.Type.Filled;
                _progressFill.fillMethod = Image.FillMethod.Horizontal;
                _progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
                _progressFill.fillAmount = 1f;
                _progressFill.color = BossRushUIColors.Accent;
                _progressFill.raycastTarget = false;

                _shownId = RandomEventId.None;
                _shownSeconds = -1;
                _shownWarning = false;
                // 建好先收起：第一次显示也走「从隐藏到显示」，播入场动画
                _badge.SetActive(false);
            }
            catch (Exception e)
            {
                // 只记一次：_buildFailed 之后不再重试，避免每帧刷屏
                _buildFailed = true;
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 事件徽章构建失败: " + e.Message);
            }
        }

        // ====================================================================
        // 图标
        // ====================================================================

        /// <summary>
        /// 解析事件图标。缺图返回 null（调用方落文字徽章）。
        /// 结果（含「缺图」这一结论本身）一律入缓存，绝不每次事件都去读盘。
        /// </summary>
        private static Sprite ResolveIcon(RandomEventId id)
        {
            Sprite cached;
            if (_iconCache.TryGetValue(id, out cached))
            {
                return cached;
            }

            Sprite sprite = null;
            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (!string.IsNullOrEmpty(modPath))
                {
                    string relative = RandomEventsTuning.HudIconDirectory
                        .Replace('/', Path.DirectorySeparatorChar);
                    string path = Path.Combine(
                        Path.Combine(modPath, "Assets"),
                        Path.Combine(relative, "evt_" + ((int)id) + ".png"));

                    sprite = ProductionIconCache.Get("Assets/" + RandomEventsTuning.HudIconDirectory + "/evt_" + ((int)id) + ".png");
                    if (sprite == null && ProductionIconCache.AllowRawFallback && File.Exists(path))
                    {
                        sprite = RawImageLoader.LoadSprite(path, "evt_" + ((int)id));
                        if (sprite != null)
                        {
                            sprite.hideFlags = HideFlags.HideAndDontSave;
                            sprite.texture.hideFlags = HideFlags.HideAndDontSave;
                            _ownedAssets.Add(sprite.texture);
                            _ownedAssets.Add(sprite);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 事件图标加载失败 " + id + ": " + e.Message);
                sprite = null;
            }

            // null 也入缓存：这是「本局不再尝试读盘」的结论
            _iconCache[id] = sprite;
            return sprite;
        }
    }
}
