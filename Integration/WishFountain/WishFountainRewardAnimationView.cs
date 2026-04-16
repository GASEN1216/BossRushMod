// ============================================================================
// WishFountainRewardAnimationView.cs - 星愿许愿台抽奖动画运行时遮罩层
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public class WishFountainRewardAnimationView : MonoBehaviour
    {
        private const float OverlayAlpha = 0.9f;
        private const float FastSpinDurationSeconds = 3f;
        private const float SlowSpinDurationSeconds = 2f;
        private const float ResultDisplayDurationSeconds = 3f;
        private const float SlotWidth = 132f;
        private const float SlotHeight = 132f;
        private const float SlotSpacing = 18f;
        private const float ViewportWidth = 1728f;
        private const float ViewportHeight = 184f;
        private const float MarkerWidth = 10f;
        private const float MarkerHeight = 184f;
        private const float SlotHighlightScale = 1.1f;
        private const float HighlightAnimDuration = 0.35f;
        private const float DimmedAlpha = 0.3f;
        private const float QualityBarHeight = 6f;
        private const float FastSpinTargetProgress = 0.86f;
        private const float SafetyTimeoutSeconds = 15f;

        private static WishFountainRewardAnimationView activeInstance;
        private static readonly Dictionary<int, Sprite> iconCache = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, string> displayNameCache = new Dictionary<int, string>();
        private static readonly Dictionary<int, int> qualityCache = new Dictionary<int, int>();
        /// <summary>反射缓存：Item 子类型 → 返回 Sprite 的 MemberInfo（null 表示该类型无可用成员）</summary>
        private static readonly Dictionary<Type, MemberInfo> spriteMemberCache = new Dictionary<Type, MemberInfo>();

        private RectTransform reelContentRect;
        private readonly List<RectTransform> slotRects = new List<RectTransform>();
        private readonly List<CanvasGroup> slotCanvasGroups = new List<CanvasGroup>();
        private readonly List<int> sequenceTypeIds = new List<int>();
        private Action<int, string> finishedCallback;
        private int rewardTypeId;
        private string rewardDisplayName;
        private int winnerIndex;
        private bool finished;
        private float createdTime;

        // ====================================================================
        // 品质颜色映射 (Q1~Q8) — 灵感来源 CS:GO 稀有度配色
        // ====================================================================
        private static readonly Color[] QualityColors =
        {
            new Color(0.62f, 0.62f, 0.62f, 1f),   // Q1 - 灰色 (消费级)
            new Color(0.42f, 0.60f, 0.80f, 1f),    // Q2 - 浅蓝 (工业级)
            new Color(0.30f, 0.45f, 0.85f, 1f),    // Q3 - 蓝色 (军规级)
            new Color(0.55f, 0.30f, 0.85f, 1f),    // Q4 - 紫色 (受限)
            new Color(0.85f, 0.30f, 0.60f, 1f),    // Q5 - 粉红 (保密)
            new Color(0.90f, 0.30f, 0.30f, 1f),    // Q6 - 红色 (隐秘)
            new Color(1.00f, 0.70f, 0.15f, 1f),    // Q7 - 金色 (非凡)
            new Color(1.00f, 0.84f, 0.00f, 1f)     // Q8 - 亮金 (传奇)
        };

        private static readonly Color[] QualityBackgroundColors =
        {
            new Color(0.14f, 0.14f, 0.14f, 0.96f),   // Q1
            new Color(0.12f, 0.15f, 0.22f, 0.96f),   // Q2
            new Color(0.10f, 0.13f, 0.24f, 0.96f),   // Q3
            new Color(0.16f, 0.10f, 0.24f, 0.96f),   // Q4
            new Color(0.24f, 0.10f, 0.18f, 0.96f),   // Q5
            new Color(0.24f, 0.10f, 0.10f, 0.96f),   // Q6
            new Color(0.24f, 0.18f, 0.06f, 0.96f),   // Q7
            new Color(0.26f, 0.22f, 0.04f, 0.96f)    // Q8
        };

        public static void PlayRuntime(
            int rewardTypeId,
            string rewardDisplayName,
            List<int> sequenceTypeIds,
            int winnerIndex,
            Action<int, string> onFinished)
        {
            if (sequenceTypeIds == null || sequenceTypeIds.Count <= 0)
            {
                if (onFinished != null)
                {
                    onFinished(rewardTypeId, rewardDisplayName);
                }
                return;
            }

            try
            {
                if (activeInstance != null)
                {
                    UnityEngine.Object.Destroy(activeInstance.gameObject);
                    activeInstance = null;
                }

                GameObject root = new GameObject(
                    "WishFountainRewardAnimationView",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster),
                    typeof(WishFountainRewardAnimationView));

                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000;

                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                WishFountainRewardAnimationView view = root.GetComponent<WishFountainRewardAnimationView>();
                activeInstance = view;
                view.rewardTypeId = rewardTypeId;
                view.rewardDisplayName = string.IsNullOrEmpty(rewardDisplayName) ? "Unknown Reward" : rewardDisplayName;
                view.finishedCallback = onFinished;
                view.sequenceTypeIds.AddRange(sequenceTypeIds);
                view.winnerIndex = Mathf.Clamp(winnerIndex, 0, sequenceTypeIds.Count - 1);
                view.Initialize();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountainAnimation] [WARNING] 创建抽奖动画遮罩失败: " + e.Message);
                if (onFinished != null)
                {
                    onFinished(rewardTypeId, rewardDisplayName);
                }
            }
        }

        private void Initialize()
        {
            createdTime = Time.realtimeSinceStartup;
            CreateOverlayUI();
            StartCoroutine(PlayAnimationCoroutine());
        }

        private void Update()
        {
            // Escape 键跳过动画
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Complete();
                return;
            }

            // 安全超时：防止协程异常中断导致遮罩卡死
            if (Time.realtimeSinceStartup - createdTime > SafetyTimeoutSeconds)
            {
                ModBehaviour.DevLog("[WishFountainAnimation] 安全超时触发，强制关闭动画遮罩");
                Complete();
            }
        }

        private void CreateOverlayUI()
        {
            RectTransform rootRect = GetComponent<RectTransform>();
            StretchRect(rootRect);

            GameObject background = CreateUiObject("Background", rootRect, typeof(Image));
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            StretchRect(backgroundRect);
            Image backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = new Color(0f, 0f, 0f, OverlayAlpha);
            backgroundImage.raycastTarget = true;

            GameObject title = CreateUiObject("Title", rootRect, typeof(TextMeshProUGUI));
            RectTransform titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 144f);
            titleRect.sizeDelta = new Vector2(720f, 54f);
            TextMeshProUGUI titleText = title.GetComponent<TextMeshProUGUI>();
            titleText.text = L10n.T("星愿抽奖", "Starwish Draw");
            titleText.fontSize = 32f;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;
            titleText.raycastTarget = false;

            GameObject viewport = CreateUiObject("Viewport", rootRect, typeof(Image), typeof(Mask));
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = new Vector2(0.5f, 0.5f);
            viewportRect.anchorMax = new Vector2(0.5f, 0.5f);
            viewportRect.pivot = new Vector2(0.5f, 0.5f);
            viewportRect.anchoredPosition = Vector2.zero;
            viewportRect.sizeDelta = new Vector2(ViewportWidth, ViewportHeight);
            Image viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(0.08f, 0.1f, 0.14f, 0.96f);
            viewportImage.raycastTarget = false;
            Mask viewportMask = viewport.GetComponent<Mask>();
            viewportMask.showMaskGraphic = true;

            GameObject reelContent = CreateUiObject("ReelContent", viewportRect, typeof(RectTransform));
            reelContentRect = reelContent.GetComponent<RectTransform>();
            reelContentRect.anchorMin = new Vector2(0.5f, 0.5f);
            reelContentRect.anchorMax = new Vector2(0.5f, 0.5f);
            reelContentRect.pivot = new Vector2(0.5f, 0.5f);
            reelContentRect.anchoredPosition = Vector2.zero;
            reelContentRect.sizeDelta = new Vector2(GetSequenceTotalWidth(), ViewportHeight);

            // 细线激光指示器 (发光层)
            GameObject markerGlow = CreateUiObject("MarkerGlow", rootRect, typeof(Image));
            RectTransform markerGlowRect = markerGlow.GetComponent<RectTransform>();
            markerGlowRect.anchorMin = new Vector2(0.5f, 0.5f);
            markerGlowRect.anchorMax = new Vector2(0.5f, 0.5f);
            markerGlowRect.pivot = new Vector2(0.5f, 0.5f);
            markerGlowRect.anchoredPosition = Vector2.zero;
            markerGlowRect.sizeDelta = new Vector2(16f, MarkerHeight + 16f);
            Image markerGlowImage = markerGlow.GetComponent<Image>();
            markerGlowImage.color = new Color(1f, 0.85f, 0.3f, 0.15f);
            markerGlowImage.raycastTarget = false;

            // 细线激光指示器 (黑色背衬)
            GameObject markerShadow = CreateUiObject("MarkerShadow", rootRect, typeof(Image));
            RectTransform markerShadowRect = markerShadow.GetComponent<RectTransform>();
            markerShadowRect.anchorMin = new Vector2(0.5f, 0.5f);
            markerShadowRect.anchorMax = new Vector2(0.5f, 0.5f);
            markerShadowRect.pivot = new Vector2(0.5f, 0.5f);
            markerShadowRect.anchoredPosition = Vector2.zero;
            markerShadowRect.sizeDelta = new Vector2(4f, MarkerHeight + 8f);
            Image markerShadowImage = markerShadow.GetComponent<Image>();
            markerShadowImage.color = new Color(0f, 0f, 0f, 0.6f);
            markerShadowImage.raycastTarget = false;

            // 细线激光指示器 (核心高亮层)
            GameObject marker = CreateUiObject("Marker", rootRect, typeof(Image));
            RectTransform markerRect = marker.GetComponent<RectTransform>();
            markerRect.anchorMin = new Vector2(0.5f, 0.5f);
            markerRect.anchorMax = new Vector2(0.5f, 0.5f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.anchoredPosition = Vector2.zero;
            markerRect.sizeDelta = new Vector2(2f, MarkerHeight + 8f);
            Image markerImage = marker.GetComponent<Image>();
            markerImage.color = new Color(1f, 0.88f, 0.42f, 0.96f);
            markerImage.raycastTarget = false;

            // 顶部小指示定点
            GameObject topDot = CreateUiObject("MarkerTopLocator", rootRect, typeof(Image));
            RectTransform topDotRect = topDot.GetComponent<RectTransform>();
            topDotRect.anchorMin = new Vector2(0.5f, 0.5f);
            topDotRect.anchorMax = new Vector2(0.5f, 0.5f);
            topDotRect.pivot = new Vector2(0.5f, 0.5f);
            topDotRect.anchoredPosition = new Vector2(0f, MarkerHeight * 0.5f + 6f);
            topDotRect.sizeDelta = new Vector2(12f, 4f);
            topDot.GetComponent<Image>().color = markerImage.color;
            topDot.GetComponent<Image>().raycastTarget = false;

            // 底部小指示定点
            GameObject botDot = CreateUiObject("MarkerBotLocator", rootRect, typeof(Image));
            RectTransform botDotRect = botDot.GetComponent<RectTransform>();
            botDotRect.anchorMin = new Vector2(0.5f, 0.5f);
            botDotRect.anchorMax = new Vector2(0.5f, 0.5f);
            botDotRect.pivot = new Vector2(0.5f, 0.5f);
            botDotRect.anchoredPosition = new Vector2(0f, -MarkerHeight * 0.5f - 6f);
            botDotRect.sizeDelta = new Vector2(12f, 4f);
            botDot.GetComponent<Image>().color = markerImage.color;
            botDot.GetComponent<Image>().raycastTarget = false;

            BuildSlots();
        }

        private void BuildSlots()
        {
            slotRects.Clear();
            slotCanvasGroups.Clear();

            float totalWidth = GetSequenceTotalWidth();
            float startX = -0.5f * totalWidth + SlotWidth * 0.5f;
            float stepX = SlotWidth + SlotSpacing;

            for (int i = 0; i < sequenceTypeIds.Count; i++)
            {
                int typeId = sequenceTypeIds[i];
                int quality = GetItemQuality(typeId);
                Color qualityColor = GetQualityColor(quality);
                Color qualityBgColor = GetQualityBackgroundColor(quality);

                GameObject slot = CreateUiObject("Slot_" + i, reelContentRect, typeof(Image), typeof(CanvasGroup));
                RectTransform slotRect = slot.GetComponent<RectTransform>();
                slotRect.anchorMin = new Vector2(0.5f, 0.5f);
                slotRect.anchorMax = new Vector2(0.5f, 0.5f);
                slotRect.pivot = new Vector2(0.5f, 0.5f);
                slotRect.anchoredPosition = new Vector2(startX + stepX * i, 0f);
                slotRect.sizeDelta = new Vector2(SlotWidth, SlotHeight);

                Image slotImage = slot.GetComponent<Image>();
                slotImage.color = qualityBgColor;
                slotImage.raycastTarget = false;

                CanvasGroup slotCanvasGroup = slot.GetComponent<CanvasGroup>();
                slotCanvasGroup.alpha = 0.95f;

                // 品质颜色底部边框条
                GameObject qualityBar = CreateUiObject("QualityBar", slotRect, typeof(Image));
                RectTransform qualityBarRect = qualityBar.GetComponent<RectTransform>();
                qualityBarRect.anchorMin = new Vector2(0f, 0f);
                qualityBarRect.anchorMax = new Vector2(1f, 0f);
                qualityBarRect.pivot = new Vector2(0.5f, 0f);
                qualityBarRect.anchoredPosition = Vector2.zero;
                qualityBarRect.sizeDelta = new Vector2(0f, QualityBarHeight);
                Image qualityBarImage = qualityBar.GetComponent<Image>();
                qualityBarImage.color = qualityColor;
                qualityBarImage.raycastTarget = false;

                // 品质颜色左侧边框条
                GameObject leftBorder = CreateUiObject("LeftBorder", slotRect, typeof(Image));
                RectTransform leftBorderRect = leftBorder.GetComponent<RectTransform>();
                leftBorderRect.anchorMin = new Vector2(0f, 0f);
                leftBorderRect.anchorMax = new Vector2(0f, 1f);
                leftBorderRect.pivot = new Vector2(0f, 0.5f);
                leftBorderRect.anchoredPosition = Vector2.zero;
                leftBorderRect.sizeDelta = new Vector2(3f, 0f);
                Image leftBorderImage = leftBorder.GetComponent<Image>();
                leftBorderImage.color = new Color(qualityColor.r, qualityColor.g, qualityColor.b, 0.5f);
                leftBorderImage.raycastTarget = false;

                // 品质颜色右侧边框条
                GameObject rightBorder = CreateUiObject("RightBorder", slotRect, typeof(Image));
                RectTransform rightBorderRect = rightBorder.GetComponent<RectTransform>();
                rightBorderRect.anchorMin = new Vector2(1f, 0f);
                rightBorderRect.anchorMax = new Vector2(1f, 1f);
                rightBorderRect.pivot = new Vector2(1f, 0.5f);
                rightBorderRect.anchoredPosition = Vector2.zero;
                rightBorderRect.sizeDelta = new Vector2(3f, 0f);
                Image rightBorderImage = rightBorder.GetComponent<Image>();
                rightBorderImage.color = new Color(qualityColor.r, qualityColor.g, qualityColor.b, 0.5f);
                rightBorderImage.raycastTarget = false;

                // 品质颜色顶部边框条
                GameObject topBorder = CreateUiObject("TopBorder", slotRect, typeof(Image));
                RectTransform topBorderRect = topBorder.GetComponent<RectTransform>();
                topBorderRect.anchorMin = new Vector2(0f, 1f);
                topBorderRect.anchorMax = new Vector2(1f, 1f);
                topBorderRect.pivot = new Vector2(0.5f, 1f);
                topBorderRect.anchoredPosition = Vector2.zero;
                topBorderRect.sizeDelta = new Vector2(0f, 3f);
                Image topBorderImage = topBorder.GetComponent<Image>();
                topBorderImage.color = new Color(qualityColor.r, qualityColor.g, qualityColor.b, 0.5f);
                topBorderImage.raycastTarget = false;

                GameObject iconObject = CreateUiObject("Icon", slotRect, typeof(Image));
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = new Vector2(0f, 8f);
                iconRect.sizeDelta = new Vector2(84f, 84f);
                Image iconImage = iconObject.GetComponent<Image>();
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;

                Sprite sprite = GetItemIconSprite(typeId);
                if (sprite != null)
                {
                    iconImage.sprite = sprite;
                    iconImage.color = Color.white;
                }
                else
                {
                    iconImage.enabled = false;

                    GameObject fallbackLabel = CreateUiObject("FallbackLabel", iconRect, typeof(TextMeshProUGUI));
                    RectTransform fallbackRect = fallbackLabel.GetComponent<RectTransform>();
                    StretchRect(fallbackRect);
                    TextMeshProUGUI fallbackText = fallbackLabel.GetComponent<TextMeshProUGUI>();
                    fallbackText.text = GetCompactName(typeId);
                    fallbackText.fontSize = 18f;
                    fallbackText.alignment = TextAlignmentOptions.Center;
                    fallbackText.color = new Color(0.92f, 0.95f, 1f, 0.95f);
                    fallbackText.enableWordWrapping = true;
                    fallbackText.raycastTarget = false;
                }

                GameObject nameObject = CreateUiObject("Name", slotRect, typeof(TextMeshProUGUI));
                RectTransform nameRect = nameObject.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0.5f, 0f);
                nameRect.anchorMax = new Vector2(0.5f, 0f);
                nameRect.pivot = new Vector2(0.5f, 0f);
                nameRect.anchoredPosition = new Vector2(0f, 12f);
                nameRect.sizeDelta = new Vector2(SlotWidth - 18f, 28f);
                TextMeshProUGUI nameText = nameObject.GetComponent<TextMeshProUGUI>();
                nameText.text = GetCompactName(typeId);
                nameText.fontSize = 16f;
                nameText.alignment = TextAlignmentOptions.Center;
                nameText.color = new Color(0.9f, 0.93f, 1f, 0.92f);
                nameText.enableWordWrapping = false;
                nameText.overflowMode = TextOverflowModes.Ellipsis;
                nameText.raycastTarget = false;

                slotRects.Add(slotRect);
                slotCanvasGroups.Add(slotCanvasGroup);
            }
        }

        private IEnumerator PlayAnimationCoroutine()
        {
            yield return null;

            float initialX = 0f;
            float finalX = CalculateFinalContentX();
            float rollDuration = FastSpinDurationSeconds + SlowSpinDurationSeconds;
            float elapsed = 0f;

            int lastSlotIndex = -1;
            float stepX = SlotWidth + SlotSpacing;
            float totalWidth = GetSequenceTotalWidth();
            float startX = -0.5f * totalWidth + SlotWidth * 0.5f;
            string soundPath = System.IO.Path.Combine(ModBehaviour.GetModPath(), "Assets", "Sounds", "lottery", "tick.wav");

            while (elapsed < rollDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = EvaluateRollProgress(elapsed, rollDuration);
                float animatedX = Mathf.LerpUnclamped(initialX, finalX, progress);
                if (reelContentRect != null)
                {
                    reelContentRect.anchoredPosition = new Vector2(animatedX, 0f);
                }

                int currentSlotIndex = Mathf.FloorToInt((-animatedX - startX + stepX * 0.5f) / stepX);
                if (currentSlotIndex != lastSlotIndex && currentSlotIndex >= 0 && currentSlotIndex < sequenceTypeIds.Count)
                {
                    lastSlotIndex = currentSlotIndex;
                    if (System.IO.File.Exists(soundPath))
                    {
                        ModBehaviour.Instance?.PlaySoundEffect(soundPath);
                    }
                }

                yield return null;
            }

            if (reelContentRect != null)
            {
                reelContentRect.anchoredPosition = new Vector2(finalX, 0f);
            }

            TryPlayWinningRewardResultSfx();
            yield return StartCoroutine(HighlightWinningSlotAnimated());
            float remainingDisplayTime = Mathf.Max(0f, ResultDisplayDurationSeconds - HighlightAnimDuration);
            if (remainingDisplayTime > 0f)
            {
                yield return new WaitForSecondsRealtime(remainingDisplayTime);
            }
            Complete();
        }

        private float EvaluateRollProgress(float elapsed, float rollDuration)
        {
            if (elapsed <= 0f)
            {
                return 0f;
            }

            if (elapsed >= rollDuration)
            {
                return 1f;
            }

            if (elapsed <= FastSpinDurationSeconds)
            {
                float fastT = Mathf.Clamp01(elapsed / FastSpinDurationSeconds);
                // ease-in 二次曲线，模拟加速启动感
                float easedFastT = fastT * fastT;
                return FastSpinTargetProgress * easedFastT;
            }

            float slowT = Mathf.Clamp01((elapsed - FastSpinDurationSeconds) / SlowSpinDurationSeconds);
            float easedSlowT = 1f - Mathf.Pow(1f - slowT, 3f);
            return Mathf.LerpUnclamped(FastSpinTargetProgress, 1f, easedSlowT);
        }

        private void TryPlayWinningRewardResultSfx()
        {
            if (rewardTypeId <= 0)
            {
                return;
            }

            int rewardQuality = GetItemQuality(rewardTypeId);
            if (rewardQuality < 7)
            {
                return;
            }

            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath))
            {
                return;
            }

            string specialSoundPath = System.IO.Path.Combine(modPath, "Assets", "Sounds", "lottery", "special.mp3");
            if (!System.IO.File.Exists(specialSoundPath))
            {
                return;
            }

            ModBehaviour.Instance?.PlaySoundEffect(specialSoundPath);
        }

        /// <summary>
        /// 获奖物品高亮动画 — 其他物品变暗 + 获奖物品弹出缩放
        /// </summary>
        private IEnumerator HighlightWinningSlotAnimated()
        {
            int slotCount = Mathf.Min(slotRects.Count, slotCanvasGroups.Count);
            if (slotCount <= 0)
            {
                yield break;
            }

            int safeWinnerIndex = winnerIndex;
            if (safeWinnerIndex < 0 || safeWinnerIndex >= slotCount)
            {
                safeWinnerIndex = slotCount - 1;
            }

            // 将获奖 Slot 提至最顶层渲染，确保缩放动画不被相邻 Slot 遮挡
            // 注意：位置靠 anchoredPosition 控制，SetAsLastSibling 仅影响渲染层级
            if (slotRects[safeWinnerIndex] != null)
            {
                slotRects[safeWinnerIndex].SetAsLastSibling();
            }

            // 阶段1: 其他物品变暗 + 获奖物品缩放弹出
            float elapsed = 0f;
            while (elapsed < HighlightAnimDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / HighlightAnimDuration);
                float easedT = Mathf.SmoothStep(0f, 1f, t);

                for (int i = 0; i < slotCount; i++)
                {
                    if (i == safeWinnerIndex)
                    {
                        // 获奖物品: 从1.0 放大到 SlotHighlightScale
                        if (slotRects[i] != null)
                        {
                            float scale = Mathf.Lerp(1f, SlotHighlightScale, easedT);
                            slotRects[i].localScale = Vector3.one * scale;
                        }
                        if (slotCanvasGroups[i] != null)
                        {
                            slotCanvasGroups[i].alpha = 1f;
                        }
                    }
                    else
                    {
                        // 其他物品: alpha 从 0.95 降到 DimmedAlpha
                        if (slotCanvasGroups[i] != null)
                        {
                            slotCanvasGroups[i].alpha = Mathf.Lerp(0.95f, DimmedAlpha, easedT);
                        }
                    }
                }

                yield return null;
            }

            // 确保最终状态精确
            for (int i = 0; i < slotCount; i++)
            {
                if (i == safeWinnerIndex)
                {
                    if (slotRects[i] != null)
                    {
                        slotRects[i].localScale = Vector3.one * SlotHighlightScale;
                    }
                    if (slotCanvasGroups[i] != null)
                    {
                        slotCanvasGroups[i].alpha = 1f;
                    }
                }
                else
                {
                    if (slotCanvasGroups[i] != null)
                    {
                        slotCanvasGroups[i].alpha = DimmedAlpha;
                    }
                }
            }
        }

        private float CalculateFinalContentX()
        {
            if (sequenceTypeIds.Count <= 0)
            {
                return 0f;
            }

            float stepX = SlotWidth + SlotSpacing;
            float totalWidth = GetSequenceTotalWidth();
            float startX = -0.5f * totalWidth + SlotWidth * 0.5f;

            int safeWinnerIndex = winnerIndex;
            if (safeWinnerIndex < 0 || safeWinnerIndex >= sequenceTypeIds.Count)
            {
                safeWinnerIndex = sequenceTypeIds.Count - 1;
            }

            float winnerX = startX + stepX * safeWinnerIndex;
            float randomOffset = UnityEngine.Random.Range(-SlotWidth * 0.3f, SlotWidth * 0.3f);
            return -(winnerX + randomOffset);
        }

        private float GetSequenceTotalWidth()
        {
            if (sequenceTypeIds.Count <= 0)
            {
                return ViewportWidth;
            }

            return sequenceTypeIds.Count * SlotWidth + Mathf.Max(0, sequenceTypeIds.Count - 1) * SlotSpacing;
        }

        // ====================================================================
        // 品质颜色工具方法
        // ====================================================================

        private static Color GetQualityColor(int quality)
        {
            int index = Mathf.Clamp(quality - 1, 0, QualityColors.Length - 1);
            return QualityColors[index];
        }

        private static Color GetQualityBackgroundColor(int quality)
        {
            int index = Mathf.Clamp(quality - 1, 0, QualityBackgroundColors.Length - 1);
            return QualityBackgroundColors[index];
        }

        private static int GetItemQuality(int typeId)
        {
            int cached;
            if (qualityCache.TryGetValue(typeId, out cached))
            {
                return cached;
            }

            int quality = 1;
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab != null)
                {
                    try
                    {
                        quality = Mathf.Clamp(prefab.Quality, 1, 8);
                    }
                    catch
                    {
                        quality = 1;
                    }
                }
            }
            catch
            {
            }

            qualityCache[typeId] = quality;
            return quality;
        }

        // ====================================================================
        // UI 工具方法
        // ====================================================================

        private static GameObject CreateUiObject(string name, Transform parent, params Type[] types)
        {
            List<Type> finalTypes = new List<Type>();
            bool hasRectTransform = false;
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == typeof(RectTransform))
                {
                    hasRectTransform = true;
                }
                finalTypes.Add(types[i]);
            }

            if (!hasRectTransform)
            {
                finalTypes.Insert(0, typeof(RectTransform));
            }

            GameObject go = new GameObject(name, finalTypes.ToArray());
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            return go;
        }

        private static void StretchRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static string GetCompactName(int typeId)
        {
            string displayName;
            if (displayNameCache.TryGetValue(typeId, out displayName))
            {
                return displayName;
            }

            displayName = "Item " + typeId;
            Item prefab = null;
            try
            {
                prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab != null)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(prefab.DisplayName))
                        {
                            displayName = prefab.DisplayName;
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (displayName == "Item " + typeId && !string.IsNullOrEmpty(prefab.name))
                        {
                            displayName = prefab.name;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            displayNameCache[typeId] = displayName;
            return displayName;
        }

        private static Sprite GetItemIconSprite(int typeId)
        {
            Sprite cached;
            if (iconCache.TryGetValue(typeId, out cached))
            {
                return cached;
            }

            Sprite sprite = null;
            Item prefab = null;
            try
            {
                prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab != null)
                {
                    sprite = TryResolveItemSprite(prefab);
                    if (sprite == null)
                    {
                        SpriteRenderer renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
                        if (renderer != null)
                        {
                            sprite = renderer.sprite;
                        }
                    }
                }
            }
            catch
            {
            }

            iconCache[typeId] = sprite;
            return sprite;
        }

        private static Sprite TryResolveItemSprite(Item item)
        {
            if (item == null)
            {
                return null;
            }

            Type itemType = item.GetType();
            MemberInfo cachedMember;
            if (spriteMemberCache.TryGetValue(itemType, out cachedMember))
            {
                if (cachedMember == null)
                {
                    return null;
                }

                try
                {
                    FieldInfo cachedField = cachedMember as FieldInfo;
                    if (cachedField != null)
                    {
                        return cachedField.GetValue(item) as Sprite;
                    }

                    PropertyInfo cachedProperty = cachedMember as PropertyInfo;
                    if (cachedProperty != null)
                    {
                        return cachedProperty.GetValue(item, null) as Sprite;
                    }
                }
                catch
                {
                }

                return null;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
            string[] candidateMemberNames =
            {
                "Icon",
                "icon",
                "Sprite",
                "sprite",
                "IconSprite",
                "iconSprite",
                "iconReference"
            };

            for (int i = 0; i < candidateMemberNames.Length; i++)
            {
                string memberName = candidateMemberNames[i];

                try
                {
                    PropertyInfo property = itemType.GetProperty(memberName, flags);
                    if (property != null && typeof(Sprite).IsAssignableFrom(property.PropertyType))
                    {
                        Sprite sprite = property.GetValue(item, null) as Sprite;
                        if (sprite != null)
                        {
                            spriteMemberCache[itemType] = property;
                            return sprite;
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    FieldInfo field = itemType.GetField(memberName, flags);
                    if (field != null && typeof(Sprite).IsAssignableFrom(field.FieldType))
                    {
                        Sprite sprite = field.GetValue(item) as Sprite;
                        if (sprite != null)
                        {
                            spriteMemberCache[itemType] = field;
                            return sprite;
                        }
                    }
                }
                catch
                {
                }
            }

            spriteMemberCache[itemType] = null;
            return null;
        }

        private static void ClearItemCaches()
        {
            iconCache.Clear();
            displayNameCache.Clear();
            qualityCache.Clear();
        }

        private void Complete()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            try
            {
                if (finishedCallback != null)
                {
                    finishedCallback(rewardTypeId, rewardDisplayName);
                }
            }
            finally
            {
                activeInstance = null;
                ClearItemCaches();
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            // 安全保底：如果动画被意外中断（协程异常、对象被外部 Destroy），
            // 确保 finishedCallback 仍然被调用，防止奖励发放路径丢失
            if (!finished && finishedCallback != null)
            {
                try
                {
                    finishedCallback(rewardTypeId, rewardDisplayName);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[WishFountainAnimation] [WARNING] OnDestroy 保底回调异常: " + e.Message);
                }
                finally
                {
                    finished = true;
                }
            }

            if (activeInstance == this)
            {
                activeInstance = null;
            }
        }
    }
}
