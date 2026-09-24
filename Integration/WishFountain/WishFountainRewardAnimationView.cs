// ============================================================================
// WishFountainRewardAnimationView.cs - 星愿许愿台抽奖动画运行时遮罩层
// ============================================================================
// 2026-09-23 审美审查 UD-11…UD-16：
//   - 轮带：一条 ease-out 曲线从最高速连续减速（旧版前 3 秒加速、第 3 秒速度突降到 37%）；
//     起点放在中奖格前约 22 格，中奖格不会在第一秒就滑进视窗。
//   - 格子：卡片底图 + 品质色描边 + 内缩品质细条（旧版是直角块 + 四根边条拼的框）；
//     视窗用 RectMask2D 软边，两端自然淡出；指示器是程序化柔光竖条。
//   - 品质色读官方 DisplayQualityLook（与背包同一套），取不到退回 BossRushUIColors.Rarity*。
//   - 入场淡入 + 轮带升入；停轮后的揭晓演出（弹出、柔光、结果横幅、闪白、音效）在
//     WishFountainRewardAnimationView_Reveal.cs；收场走共享淡出。
//   - Esc：滚动中按一下直接跳到终点进入揭晓；揭晓阶段再按 Esc 或点击才收下关闭。
//     奖励只在 Complete() 里发一次（finished 标志 + OnDestroy 保底），淡出期间不会重复发奖。
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
    public partial class WishFountainRewardAnimationView : MonoBehaviour
    {
        /// <summary>轮带从起步到停稳的总时长（秒）。曲线见 <see cref="EvaluateRollProgress"/>。</summary>
        private const float RollDurationSeconds = 5f;
        /// <summary>起点放在中奖格前多少格：曲线起手最快，路程太短时中奖格在第一秒就进视窗，悬念没了。</summary>
        private const int RollTravelSlots = 22;
        private const float SlotWidth = 132f;
        private const float SlotHeight = 132f;
        private const float SlotSpacing = 18f;
        private const float ViewportWidth = 1728f;
        private const float ViewportHeight = 184f;
        /// <summary>视窗两端的软边宽度（RectMask2D.softness），滚到边上的格子淡出而不是被一刀切掉。</summary>
        private const int ViewportEdgeSoftness = 160;
        private const float MarkerHeight = 184f;
        private const float DimmedAlpha = 0.3f;
        private const float QualityBarHeight = 3f;
        private const float SafetyTimeoutSeconds = 15f;
        /// <summary>收场淡出（秒）。</summary>
        private const float CloseFadeSeconds = 0.25f;

        private static WishFountainRewardAnimationView activeInstance;
        private static readonly Dictionary<int, Sprite> iconCache = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, string> displayNameCache = new Dictionary<int, string>();
        private static readonly Dictionary<int, int> qualityCache = new Dictionary<int, int>();
        /// <summary>反射缓存：Item 子类型 → 返回 Sprite 的 MemberInfo（null 表示该类型无可用成员）</summary>
        private static readonly Dictionary<Type, MemberInfo> spriteMemberCache = new Dictionary<Type, MemberInfo>();

        private RectTransform reelContentRect;
        private readonly List<RectTransform> slotRects = new List<RectTransform>();
        private readonly List<CanvasGroup> slotCanvasGroups = new List<CanvasGroup>();
        private readonly List<Image> slotStrokes = new List<Image>();
        private readonly List<int> sequenceTypeIds = new List<int>();
        private Action<int, string> finishedCallback;
        private int rewardTypeId;
        private string rewardDisplayName;
        private int winnerIndex;
        private bool finished;
        private float safetyElapsed;
        private float rollFromX;
        private float rollToX;

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
                canvas.sortingOrder = BossRushUILayers.Modal;

                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                // screenMatchMode 原本显式写成 MatchWidthOrHeight，而 CanvasScaler 是随
                // new GameObject(typeof(CanvasScaler)) 一起新建的，默认值本就是它，行为不变。
                ZombieModeUIHelper.ConfigureCanvasScaler(scaler);

                WishFountainRewardAnimationView view = root.GetComponent<WishFountainRewardAnimationView>();
                activeInstance = view;
                view.rewardTypeId = rewardTypeId;
                view.rewardDisplayName = string.IsNullOrEmpty(rewardDisplayName) ? L10n.T("未知奖励", "Unknown reward") : rewardDisplayName;
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
            safetyElapsed = 0f;
            CreateOverlayUI();
            // 起止位置在第一帧渲染前定好，否则第一帧会先画在中间、下一帧再跳到起点。
            rollToX = CalculateFinalContentX();
            rollFromX = CalculateInitialContentX();
            if (reelContentRect != null)
            {
                reelContentRect.anchoredPosition = new Vector2(rollFromX, 0f);
            }

            // 入场（UD-15）：整层淡入（根画布只淡入不缩放），轮带台面从下方升入。
            BossRushUI.PlayOpenAnimation(gameObject);
            if (stageRect != null)
            {
                BossRushUIEntranceAnimation.Play(stageRect.gameObject, 0.05f, 0.25f, 24f);
            }
            StartCoroutine(PlayAnimationCoroutine());
        }

        private void Update()
        {
            if (finished)
            {
                return;
            }

            // 暂停菜单开着：演出停推进，超时也不计（走 unscaled 时间的表现层统一的暂停门）
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            HandleRevealInput();

            // 安全超时：防止协程异常中断导致遮罩卡死
            safetyElapsed += Time.unscaledDeltaTime;
            if (!finished && safetyElapsed > SafetyTimeoutSeconds)
            {
                ModBehaviour.DevLog("[WishFountainAnimation] 安全超时触发，强制关闭动画遮罩");
                Complete();
            }
        }

        private void CreateOverlayUI()
        {
            RectTransform rootRect = GetComponent<RectTransform>();
            StretchRect(rootRect);

            // 遮罩：强遮罩 token + 共享暗角与 0.15 秒淡入（UD-14 / UD-15）
            Image backgroundImage = BossRushUI.CreateBackdrop(rootRect);
            backgroundImage.color = BossRushUIColors.BackdropStrong;
            backgroundImage.raycastTarget = true;

            // 台面：标题、轮带、指示器、提示一起入场，揭晓柔光也挂在这里（夹在底板与格子之间）
            GameObject stage = CreateUiObject("Stage", rootRect, typeof(RectTransform));
            stageRect = stage.GetComponent<RectTransform>();
            StretchRect(stageRect);

            titleText = CreateRevealLabel("Title", stageRect, 32f, BossRushUIColors.TextPrimary, new Vector2(0f, 150f), new Vector2(720f, 54f));
            titleText.fontStyle = FontStyles.Bold;
            titleText.text = L10n.T("星愿抽奖", "Starwish Draw");

            GameObject plate = CreateUiObject("ReelPlate", stageRect, typeof(Image));
            PlaceCentered(plate.GetComponent<RectTransform>(), Vector2.zero, new Vector2(ViewportWidth + 24f, ViewportHeight + 24f));
            Image plateImage = plate.GetComponent<Image>();
            plateImage.color = BossRushUIColors.Surface;
            plateImage.raycastTarget = false;
            BossRushUI.ApplyFramedPanelSkin(plateImage, 12, BossRushUISkinPart.Panel);

            winnerGlowImage = CreateUiObject("WinnerGlow", stageRect, typeof(Image)).GetComponent<Image>();
            PlaceCentered(winnerGlowImage.rectTransform, Vector2.zero, new Vector2(WinnerGlowSize, WinnerGlowSize));
            winnerGlowImage.sprite = GetRevealGlowSprite();
            winnerGlowImage.color = Color.clear;
            winnerGlowImage.raycastTarget = false;

            // 视窗：RectMask2D 软边代替 Mask 硬裁（UD-14）；不再自带底色，底色由上面的台面板承担
            GameObject viewport = CreateUiObject("Viewport", stageRect, typeof(RectMask2D));
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            PlaceCentered(viewportRect, Vector2.zero, new Vector2(ViewportWidth, ViewportHeight));
            viewport.GetComponent<RectMask2D>().softness = new Vector2Int(ViewportEdgeSoftness, 0);

            GameObject reelContent = CreateUiObject("ReelContent", viewportRect, typeof(RectTransform));
            reelContentRect = reelContent.GetComponent<RectTransform>();
            PlaceCentered(reelContentRect, Vector2.zero, new Vector2(GetSequenceTotalWidth(), ViewportHeight));

            // 指示器：柔光竖条（程序化横向渐隐）+ 暗色背衬 + 亮芯，上下两个圆角定位点
            Color markerColor = BossRushUIColors.WarningText;
            Image markerGlow = CreateMarkerPiece("MarkerGlow", Vector2.zero, new Vector2(24f, MarkerHeight + 16f), WithAlpha(markerColor, 0.32f));
            markerGlow.sprite = GetMarkerBeamSprite();
            ApplyHairline(CreateMarkerPiece("MarkerShadow", Vector2.zero, new Vector2(4f, MarkerHeight + 8f), WithAlpha(BossRushUIColors.BackdropStrong, 0.6f)));
            ApplyHairline(CreateMarkerPiece("Marker", Vector2.zero, new Vector2(2f, MarkerHeight + 8f), WithAlpha(markerColor, 0.96f)));
            ApplyHairline(CreateMarkerPiece("MarkerTopLocator", new Vector2(0f, MarkerHeight * 0.5f + 7f), new Vector2(14f, 4f), markerColor));
            ApplyHairline(CreateMarkerPiece("MarkerBotLocator", new Vector2(0f, -MarkerHeight * 0.5f - 7f), new Vector2(14f, 4f), markerColor));

            hintText = CreateRevealLabel("Hint", stageRect, 15f, BossRushUIColors.TextSecondary, new Vector2(0f, -262f), new Vector2(720f, 28f));
            hintText.text = L10n.T("按 Esc 直接揭晓", "Press Esc to reveal now");

            BuildSlots();
        }

        private Image CreateMarkerPiece(string name, Vector2 position, Vector2 size, Color color)
        {
            GameObject piece = CreateUiObject(name, stageRect, typeof(Image));
            PlaceCentered(piece.GetComponent<RectTransform>(), position, size);
            Image image = piece.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void ApplyHairline(Image image)
        {
            BossRushUI.ApplyPanelSkin(image, 1, BossRushUISkinPart.Hairline);
        }

        private void BuildSlots()
        {
            slotRects.Clear();
            slotCanvasGroups.Clear();
            slotStrokes.Clear();

            float totalWidth = GetSequenceTotalWidth();
            float startX = -0.5f * totalWidth + SlotWidth * 0.5f;
            float stepX = SlotWidth + SlotSpacing;

            for (int i = 0; i < sequenceTypeIds.Count; i++)
            {
                int typeId = sequenceTypeIds[i];
                Color qualityColor = GetQualityColor(typeId);

                GameObject slot = CreateUiObject("Slot_" + i, reelContentRect, typeof(Image), typeof(CanvasGroup));
                RectTransform slotRect = slot.GetComponent<RectTransform>();
                PlaceCentered(slotRect, new Vector2(startX + stepX * i, 0f), new Vector2(SlotWidth, SlotHeight));

                // 卡片底图 + 品质色描边（UD-14 / UD-16）：底色是品质色淡淡叠在卡片底上，不再维护第二张底色表
                Image slotImage = slot.GetComponent<Image>();
                slotImage.color = GetSlotSurfaceColor(qualityColor);
                slotImage.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(slotImage, 8, BossRushUISkinPart.Card);
                slotStrokes.Add(BossRushUI.ApplyPanelStroke(slotImage, 8, BossRushUISkinPart.Card, WithAlpha(qualityColor, 0.55f)));

                CanvasGroup slotCanvasGroup = slot.GetComponent<CanvasGroup>();
                slotCanvasGroup.alpha = 0.95f;

                // 品质细条：内缩的圆角细条，贴在卡片底部、不碰圆角
                GameObject qualityBar = CreateUiObject("QualityBar", slotRect, typeof(Image));
                RectTransform qualityBarRect = qualityBar.GetComponent<RectTransform>();
                qualityBarRect.anchorMin = new Vector2(0f, 0f);
                qualityBarRect.anchorMax = new Vector2(1f, 0f);
                qualityBarRect.pivot = new Vector2(0.5f, 0f);
                qualityBarRect.anchoredPosition = new Vector2(0f, 7f);
                qualityBarRect.sizeDelta = new Vector2(-28f, QualityBarHeight);
                Image qualityBarImage = qualityBar.GetComponent<Image>();
                qualityBarImage.color = qualityColor;
                qualityBarImage.raycastTarget = false;
                ApplyHairline(qualityBarImage);

                GameObject iconObject = CreateUiObject("Icon", slotRect, typeof(Image));
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                PlaceCentered(iconRect, new Vector2(0f, 8f), new Vector2(84f, 84f));
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
                    BossRushUI.ApplyGameFont(fallbackText);
                    fallbackText.text = GetCompactName(typeId);
                    fallbackText.fontSize = 18f;
                    fallbackText.alignment = TextAlignmentOptions.Center;
                    fallbackText.color = BossRushUIColors.TextPrimary;
                    fallbackText.enableWordWrapping = true;
                    fallbackText.raycastTarget = false;
                }

                GameObject nameObject = CreateUiObject("Name", slotRect, typeof(TextMeshProUGUI));
                RectTransform nameRect = nameObject.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0.5f, 0f);
                nameRect.anchorMax = new Vector2(0.5f, 0f);
                nameRect.pivot = new Vector2(0.5f, 0f);
                nameRect.anchoredPosition = new Vector2(0f, 13f);
                nameRect.sizeDelta = new Vector2(SlotWidth - 18f, 28f);
                TextMeshProUGUI nameText = nameObject.GetComponent<TextMeshProUGUI>();
                BossRushUI.ApplyGameFont(nameText);
                nameText.text = GetCompactName(typeId);
                nameText.fontSize = 16f;
                nameText.alignment = TextAlignmentOptions.Center;
                nameText.color = BossRushUIColors.TextPrimary;
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

            float elapsed = 0f;
            int lastSlotIndex = -1;
            float stepX = SlotWidth + SlotSpacing;
            float totalWidth = GetSequenceTotalWidth();
            float startX = -0.5f * totalWidth + SlotWidth * 0.5f;
            string soundPath = System.IO.Path.Combine(ModBehaviour.GetModPath(), "Assets", "Sounds", "lottery", "tick.wav");
            // 只查一次盘：旧写法每经过一格都 File.Exists 一次
            bool tickAvailable = System.IO.File.Exists(soundPath);

            while (elapsed < RollDurationSeconds && !skipRequested)
            {
                if (!BossRushUI.IsGamePaused())
                {
                    elapsed += Time.unscaledDeltaTime;
                }
                float progress = EvaluateRollProgress(elapsed);
                float animatedX = Mathf.LerpUnclamped(rollFromX, rollToX, progress);
                if (reelContentRect != null)
                {
                    reelContentRect.anchoredPosition = new Vector2(animatedX, 0f);
                }

                int currentSlotIndex = Mathf.FloorToInt((-animatedX - startX + stepX * 0.5f) / stepX);
                if (currentSlotIndex != lastSlotIndex && currentSlotIndex >= 0 && currentSlotIndex < sequenceTypeIds.Count)
                {
                    lastSlotIndex = currentSlotIndex;
                    if (tickAvailable)
                    {
                        ModBehaviour.Instance?.PlaySoundEffect(soundPath);
                    }
                }

                yield return null;
            }

            // 跑完或按了 Esc：都落到同一个终点，再进揭晓
            if (reelContentRect != null)
            {
                reelContentRect.anchoredPosition = new Vector2(rollToX, 0f);
            }

            yield return StartCoroutine(PlayRevealSequence());
        }

        /// <summary>
        /// 轮带进度：<c>EaseOut(EaseOut(t))</c> = 1-(1-t)⁴。起手就是最高速，之后连续减速到停（UD-13）。
        /// 旧版前 3 秒 ease-in 加速、第 3 秒换成另一条曲线，速度一瞬间掉到 37%，轮带像被卡了一下。
        /// 只用共享缓动，不另写曲线。
        /// </summary>
        private static float EvaluateRollProgress(float elapsed)
        {
            if (elapsed <= 0f)
            {
                return 0f;
            }

            if (elapsed >= RollDurationSeconds)
            {
                return 1f;
            }

            return BossRushUI.EaseOut(BossRushUI.EaseOut(elapsed / RollDurationSeconds));
        }

        /// <summary>
        /// 揭晓音效（UD-11）：普通品质 UI/pop；Q≥5 UI/level_up；Q≥7 保留原有的 special.mp3（缺文件时退回 level_up）。
        /// </summary>
        private void TryPlayWinningRewardResultSfx(int rewardQuality)
        {
            if (rewardTypeId <= 0)
            {
                return;
            }

            if (rewardQuality >= 7)
            {
                string modPath = ModBehaviour.GetModPath();
                string specialSoundPath = string.IsNullOrEmpty(modPath)
                    ? null
                    : System.IO.Path.Combine(modPath, "Assets", "Sounds", "lottery", "special.mp3");
                if (specialSoundPath != null && System.IO.File.Exists(specialSoundPath))
                {
                    ModBehaviour.Instance?.PlaySoundEffect(specialSoundPath);
                    return;
                }
            }

            IntegrationUIFeedback.PlaySound(rewardQuality >= 5
                ? IntegrationUIFeedback.SoundLevelUp
                : IntegrationUIFeedback.SoundPop);
        }

        private int GetSafeWinnerIndex(int count)
        {
            if (count <= 0)
            {
                return -1;
            }

            return winnerIndex < 0 || winnerIndex >= count ? count - 1 : winnerIndex;
        }

        private float GetSlotCenterX(int index)
        {
            float totalWidth = GetSequenceTotalWidth();
            float startX = -0.5f * totalWidth + SlotWidth * 0.5f;
            return startX + (SlotWidth + SlotSpacing) * index;
        }

        private float CalculateFinalContentX()
        {
            if (sequenceTypeIds.Count <= 0)
            {
                return 0f;
            }

            float winnerX = GetSlotCenterX(GetSafeWinnerIndex(sequenceTypeIds.Count));
            float randomOffset = UnityEngine.Random.Range(-SlotWidth * 0.3f, SlotWidth * 0.3f);
            return -(winnerX + randomOffset);
        }

        private float CalculateInitialContentX()
        {
            if (sequenceTypeIds.Count <= 0)
            {
                return 0f;
            }

            int startIndex = Mathf.Max(0, GetSafeWinnerIndex(sequenceTypeIds.Count) - RollTravelSlots);
            return -GetSlotCenterX(startIndex);
        }

        private float GetSequenceTotalWidth()
        {
            if (sequenceTypeIds.Count <= 0)
            {
                return ViewportWidth;
            }

            return sequenceTypeIds.Count * SlotWidth + Mathf.Max(0, sequenceTypeIds.Count - 1) * SlotSpacing;
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

        private static void PlaceCentered(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
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
            qualityColorCache.Clear();
        }

        /// <summary>
        /// 静态缓存兜底清理 — 由 IBossRushRuntimeModule.OnDestroy 统一调用。
        /// 作为 ClearItemCaches 的上位兜底，确保模组/场景销毁时所有静态缓存被完整释放。
        /// 揭晓用的两张程序化贴图（柔光、指示器竖条）带 DontSave，也在这里销毁。
        /// </summary>
        public static void ResetStaticCaches()
        {
            if (iconCache != null)
            {
                iconCache.Clear();
            }

            if (displayNameCache != null)
            {
                displayNameCache.Clear();
            }

            if (qualityCache != null)
            {
                qualityCache.Clear();
            }

            if (qualityColorCache != null)
            {
                qualityColorCache.Clear();
            }

            if (spriteMemberCache != null)
            {
                spriteMemberCache.Clear();
            }

            DestroyRevealSprites();
        }

        private void Complete()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            StopAllCoroutines();
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
                // 收场淡出（UD-15）。奖励已在上面同步发放且 finished=true，
                // 淡出后 Destroy 触发的 OnDestroy 不会再走保底回调，不会重复发奖。
                BossRushUIKit.PlayCloseAndDestroy(gameObject, CloseFadeSeconds);
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
