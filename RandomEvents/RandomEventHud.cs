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

        private static Canvas _canvas;
        private static GameObject _badge;
        private static Image _iconImage;
        private static TextMeshProUGUI _nameText;
        private static TextMeshProUGUI _timerText;

        private static RandomEventId _shownId = RandomEventId.None;
        private static int _shownSeconds = -1;
        private static bool _buildFailed;

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
                    HideImmediate();
                    return;
                }

                RandomEventId activeId = director.ActiveEventId;
                if (activeId == RandomEventId.None)
                {
                    HideImmediate();
                    return;
                }

                EnsureBuilt();
                if (_badge == null)
                {
                    return;
                }

                if (!_badge.activeSelf)
                {
                    _badge.SetActive(true);
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
                }

                // 秒数只在整数值变化时写文本，避免每帧字符串分配
                int seconds = Mathf.Max(0, Mathf.CeilToInt(director.ActiveRemainingSeconds));
                if (seconds != _shownSeconds && _timerText != null)
                {
                    _shownSeconds = seconds;
                    _timerText.text = seconds + "s";
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
                if (_badge != null && _badge.activeSelf)
                {
                    _badge.SetActive(false);
                }
            }
            catch (Exception) { }
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
            _iconImage = null;
            _nameText = null;
            _timerText = null;
            _shownId = RandomEventId.None;
            _shownSeconds = -1;
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

                // 图标槽（缺图时 enabled=false，名称文本自然左移不了，但不影响可读性）
                GameObject iconObj = ZombieModeUIHelper.CreateRect(
                    "Icon",
                    _badge.transform,
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(14f + IconSize * 0.5f, 0f),
                    new Vector2(IconSize, IconSize),
                    new Vector2(0.5f, 0.5f));
                _iconImage = iconObj.AddComponent<Image>();
                _iconImage.raycastTarget = false;
                _iconImage.preserveAspect = true;
                _iconImage.enabled = false;

                float textLeft = 14f + IconSize + 10f;
                float textWidth = Mathf.Max(60f, RandomEventsTuning.HudBadgeWidth - textLeft - 58f);

                _nameText = ZombieModeUIHelper.CreateText(
                    "EventName",
                    _badge.transform,
                    string.Empty,
                    22f,
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(textLeft + textWidth * 0.5f, 0f),
                    new Vector2(textWidth, RandomEventsTuning.HudBadgeHeight - 12f),
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
                    22f,
                    new Vector2(1f, 0.5f),
                    new Vector2(1f, 0.5f),
                    new Vector2(-32f, 0f),
                    new Vector2(56f, RandomEventsTuning.HudBadgeHeight - 12f),
                    TextAlignmentOptions.Right,
                    BossRushUIColors.Accent);
                if (_timerText != null)
                {
                    _timerText.raycastTarget = false;
                }

                _shownId = RandomEventId.None;
                _shownSeconds = -1;
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

                    if (File.Exists(path))
                    {
                        byte[] bytes = File.ReadAllBytes(path);
                        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        texture.hideFlags = HideFlags.HideAndDontSave;
                        if (texture.LoadImage(bytes))
                        {
                            sprite = Sprite.Create(
                                texture,
                                new Rect(0f, 0f, texture.width, texture.height),
                                new Vector2(0.5f, 0.5f));
                            sprite.name = "evt_" + ((int)id);
                            sprite.hideFlags = HideFlags.HideAndDontSave;
                            _ownedAssets.Add(texture);
                            _ownedAssets.Add(sprite);
                        }
                        else
                        {
                            UnityEngine.Object.Destroy(texture);
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
