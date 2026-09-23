// ============================================================================
// WishFountainRewardAnimationView_Reveal.cs - 星愿抽奖：停轮后的揭晓演出、品质色与程序化柔光
// ============================================================================
// 2026-09-23 审美审查 UD-11（P1）/ UD-12 / UD-16：
//   旧版停轮后只有中奖格放大 10%、其余变暗，屏幕上始终不出现奖品名，要等遮罩一帧消失后才从头顶冒气泡。
//   现在停轮后（全部 unscaled 时间，暂停菜单开着时停推进）：
//     0–0.12 秒   中奖格 1 → 1.18（EaseOut），0.12–0.30 秒回落到 1.10（SmoothStep）；描边换成满品质色
//     0–0.25 秒   中奖格背后的径向柔光（品质色）升到 0.6，之后在 0.45–0.6 之间以 1.2Hz 呼吸
//     0.1 秒起    轮带下方结果横幅升入：一行品质星级 + 一行 30px 粗体奖品名（品质色）；标题改成「获得」
//     Q≥5         整屏闪白 0.22 → 0（0.25 秒）
//     音效        普通 UI/pop，Q≥5 UI/level_up，Q≥7 原有 special.mp3
//   揭晓 0.4 秒后按 Esc / 点击收下关闭；不按则 3.2 秒后自动收下。
//
// 品质色（UD-16，owner 拍板与背包观感一致优先）：读官方 GameplayDataSettings.UIStyle.GetDisplayQualityLook
// (item.DisplayQuality).shadowColor——官方 ItemDisplay 的品质光晕就是它；取不到（None 档 / 官方默认黑 / 异常）
// 退回 BossRushUIColors.Rarity*。官方色可能带低 alpha 或偏暗：统一把 alpha 拉满，相对亮度不足的向白提亮到可读。
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class WishFountainRewardAnimationView
    {
        private const float RevealPopSeconds = 0.12f;
        private const float RevealSettleSeconds = 0.18f;
        private const float RevealPopScale = 1.18f;
        private const float RevealRestScale = 1.10f;
        private const float RevealDimSeconds = 0.35f;
        private const float WinnerGlowSize = 220f;
        private const float WinnerGlowRiseSeconds = 0.25f;
        private const float WinnerGlowPeak = 0.6f;
        private const float WinnerGlowBreathDepth = 0.075f;
        private const float WinnerGlowBreathHz = 1.2f;
        private const int FlashMinQuality = 5;
        private const float FlashAlpha = 0.22f;
        private const float FlashSeconds = 0.25f;
        /// <summary>揭晓开始后多久允许 Esc / 点击收下：防止跳过用的那一下连按直接把结果也关掉。</summary>
        private const float RevealDismissDelaySeconds = 0.4f;
        /// <summary>不操作时揭晓停留多久自动收下。</summary>
        private const float RevealHoldSeconds = 3.2f;
        /// <summary>品质色做文字 / 描边时的最低相对亮度（深色遮罩上 30px 粗体至少约 3:1）。</summary>
        private const float MinQualityLuminance = 0.22f;

        private static readonly Dictionary<int, Color> qualityColorCache = new Dictionary<int, Color>();
        private static Sprite revealGlowSprite;
        private static Sprite markerBeamSprite;

        private RectTransform stageRect;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI hintText;
        private Image winnerGlowImage;
        private Image flashImage;
        private bool skipRequested;
        private bool revealStarted;
        private float revealElapsed;

        // ====================================================================
        // 输入：滚动中 Esc = 跳到揭晓；揭晓中 Esc / 点击 = 收下
        // ====================================================================

        private void HandleRevealInput()
        {
            bool escape = Input.GetKeyDown(KeyCode.Escape);
            if (!revealStarted)
            {
                if (escape)
                {
                    skipRequested = true;
                }
                return;
            }

            if (revealElapsed < RevealDismissDelaySeconds)
            {
                return;
            }

            if (escape || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                Complete();
            }
        }

        // ====================================================================
        // 揭晓演出
        // ====================================================================

        private IEnumerator PlayRevealSequence()
        {
            revealStarted = true;
            revealElapsed = 0f;

            int slotCount = Mathf.Min(slotRects.Count, slotCanvasGroups.Count);
            int winner = GetSafeWinnerIndex(slotCount);
            RectTransform winnerRect = winner >= 0 ? slotRects[winner] : null;
            Color qualityColor = GetQualityColor(rewardTypeId);
            int quality = GetItemQuality(rewardTypeId);

            if (winnerRect != null)
            {
                // 提到最顶层渲染，放大时不被相邻格子盖住（位置靠 anchoredPosition，不受层级影响）
                winnerRect.SetAsLastSibling();
            }
            if (winner >= 0 && winner < slotStrokes.Count && slotStrokes[winner] != null)
            {
                slotStrokes[winner].color = qualityColor;
            }
            bool glowActive = winnerGlowImage != null && winnerRect != null && reelContentRect != null;
            if (glowActive)
            {
                // 柔光在台面坐标里：视窗居中，所以中奖格的位置 = 轮带偏移 + 格子偏移
                winnerGlowImage.rectTransform.anchoredPosition = new Vector2(
                    reelContentRect.anchoredPosition.x + winnerRect.anchoredPosition.x, 0f);
                winnerGlowImage.color = WithAlpha(qualityColor, 0f);
            }

            ShowResultBanner(qualityColor, quality);
            if (quality >= FlashMinQuality)
            {
                CreateFlash(qualityColor);
            }
            TryPlayWinningRewardResultSfx(quality);

            float t = 0f;
            bool dimSettled = false;
            while (t < RevealHoldSeconds)
            {
                if (!BossRushUI.IsGamePaused())
                {
                    t += Time.unscaledDeltaTime;
                }
                revealElapsed = t;

                if (winnerRect != null)
                {
                    float scale = t < RevealPopSeconds
                        ? Mathf.LerpUnclamped(1f, RevealPopScale, BossRushUI.EaseOut(t / RevealPopSeconds))
                        : Mathf.Lerp(RevealPopScale, RevealRestScale, BossRushUI.SmoothStep((t - RevealPopSeconds) / RevealSettleSeconds));
                    winnerRect.localScale = new Vector3(scale, scale, 1f);
                }

                if (!dimSettled)
                {
                    float dim = BossRushUI.SmoothStep(t / RevealDimSeconds);
                    for (int i = 0; i < slotCount; i++)
                    {
                        if (slotCanvasGroups[i] != null)
                        {
                            slotCanvasGroups[i].alpha = i == winner ? 1f : Mathf.Lerp(0.95f, DimmedAlpha, dim);
                        }
                    }
                    dimSettled = t >= RevealDimSeconds;
                }

                if (glowActive)
                {
                    float glow = t < WinnerGlowRiseSeconds
                        ? WinnerGlowPeak * BossRushUI.SmoothStep(t / WinnerGlowRiseSeconds)
                        : WinnerGlowPeak - WinnerGlowBreathDepth
                          + WinnerGlowBreathDepth * Mathf.Cos(2f * Mathf.PI * WinnerGlowBreathHz * (t - WinnerGlowRiseSeconds));
                    winnerGlowImage.color = WithAlpha(winnerGlowImage.color, glow);
                }

                if (flashImage != null)
                {
                    float flash = FlashAlpha * (1f - BossRushUI.SmoothStep(t / FlashSeconds));
                    flashImage.color = WithAlpha(flashImage.color, flash);
                    if (t >= FlashSeconds)
                    {
                        flashImage.enabled = false;
                        flashImage = null;
                    }
                }

                yield return null;
            }

            Complete();
        }

        private void ShowResultBanner(Color qualityColor, int quality)
        {
            if (titleText != null)
            {
                titleText.text = L10n.T("获得", "You Got");
                BossRushUIEntranceAnimation.Play(titleText.gameObject, 0f, 0.2f, 0f);
            }

            if (hintText != null)
            {
                hintText.text = L10n.T("点击或按 Esc 收下", "Click or press Esc to collect");
                BossRushUIEntranceAnimation.Play(hintText.gameObject, RevealDismissDelaySeconds, 0.25f, 0f);
            }

            if (stageRect == null)
            {
                return;
            }

            GameObject banner = CreateUiObject("ResultBanner", stageRect, typeof(RectTransform));
            PlaceCentered(banner.GetComponent<RectTransform>(), new Vector2(0f, -168f), new Vector2(1200f, 96f));

            // 品质行：「品质」用次要字色，星级用品质色；星级字符 ★ 在 GBK 内，官方中文字体有字形
            TextMeshProUGUI qualityLine = CreateRevealLabel("Quality", banner.transform, 18f, BossRushUIColors.TextSecondary,
                new Vector2(0f, 26f), new Vector2(1200f, 32f));
            qualityLine.richText = true;
            qualityLine.text = L10n.T("品质", "Quality") + "  <color=#" + ColorUtility.ToHtmlStringRGB(qualityColor) + ">"
                + new string('★', Mathf.Clamp(quality, 1, 8)) + "</color>";

            TextMeshProUGUI nameLine = CreateRevealLabel("RewardName", banner.transform, 30f, qualityColor,
                new Vector2(0f, -16f), new Vector2(1200f, 50f));
            nameLine.fontStyle = FontStyles.Bold;
            nameLine.richText = false;
            nameLine.text = rewardDisplayName;

            BossRushUIEntranceAnimation.Play(banner, 0.1f, 0.25f, 16f);
        }

        private void CreateFlash(Color qualityColor)
        {
            GameObject flash = CreateUiObject("RevealFlash", transform, typeof(Image));
            StretchRect(flash.GetComponent<RectTransform>());
            flashImage = flash.GetComponent<Image>();
            flashImage.raycastTarget = false;
            // 闪白略带品质色，不是纯白一片
            flashImage.color = WithAlpha(Color.Lerp(Color.white, qualityColor, 0.35f), FlashAlpha);
            flash.transform.SetAsLastSibling();
        }

        private static TextMeshProUGUI CreateRevealLabel(string name, Transform parent, float fontSize, Color color, Vector2 position, Vector2 size)
        {
            GameObject go = CreateUiObject(name, parent, typeof(TextMeshProUGUI));
            PlaceCentered(go.GetComponent<RectTransform>(), position, size);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(text);
            text.fontSize = fontSize;
            // 单行框：关自动缩字，框高 ≥ 字号×1.45+4，否则 TMP Ellipsis 会把整行清空
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        // ====================================================================
        // 品质色：官方外观优先，取不到退回 token
        // ====================================================================

        private static Color GetQualityColor(int typeId)
        {
            Color cached;
            if (qualityColorCache.TryGetValue(typeId, out cached))
            {
                return cached;
            }

            Color color;
            if (!TryGetOfficialQualityColor(typeId, out color))
            {
                color = GetFallbackQualityColor(GetItemQuality(typeId));
            }

            color = EnsureReadableOnDark(color);
            qualityColorCache[typeId] = color;
            return color;
        }

        private static bool TryGetOfficialQualityColor(int typeId, out Color color)
        {
            color = Color.white;
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab == null || prefab.DisplayQuality == DisplayQuality.None)
                {
                    return false;
                }

                Duckov.Utilities.GameplayDataSettings.UIStyleData style = Duckov.Utilities.GameplayDataSettings.UIStyle;
                Duckov.Utilities.GameplayDataSettings.UIStyleData.DisplayQualityLook look =
                    style != null ? style.GetDisplayQualityLook(prefab.DisplayQuality) : null;
                if (look == null)
                {
                    return false;
                }

                Color official = look.shadowColor;
                // 官方没登记这一档时回落到默认黑：当成取不到
                if (Mathf.Max(official.r, Mathf.Max(official.g, official.b)) < 0.08f)
                {
                    return false;
                }

                color = official;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Color GetFallbackQualityColor(int quality)
        {
            switch (quality)
            {
                case 1: return BossRushUIColors.RarityCommon;
                case 2: return BossRushUIColors.RarityUncommon;
                case 3: return BossRushUIColors.RarityRare;
                case 4: return BossRushUIColors.RarityEpic;
                case 5: return BossRushUIColors.RarityLegendary;
                case 6: return BossRushUIColors.DangerText;
                default: return quality > 6 ? BossRushUIColors.WarningText : BossRushUIColors.RarityCommon;
            }
        }

        /// <summary>alpha 拉满；相对亮度不够的逐步向白提亮（最多 6 步），深色遮罩上做字 / 描边都看得清。</summary>
        private static Color EnsureReadableOnDark(Color color)
        {
            color.a = 1f;
            for (int i = 0; i < 6 && BossRushUI.RelativeLuminance(color) < MinQualityLuminance; i++)
            {
                color = Color.Lerp(color, Color.white, 0.2f);
                color.a = 1f;
            }
            return color;
        }

        /// <summary>格子底色：品质色 18% 叠在卡片底上（UD-16），不透明度保持 ≥0.75 以拿到共享投影。</summary>
        private static Color GetSlotSurfaceColor(Color qualityColor)
        {
            Color surface = Color.Lerp(BossRushUIColors.SurfaceRaised, qualityColor, 0.18f);
            surface.a = 0.96f;
            return surface;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // ====================================================================
        // 程序化贴图：径向柔光 / 指示器竖条（带 DontSave，由 ResetStaticCaches 销毁）
        // ====================================================================

        private static Sprite GetRevealGlowSprite()
        {
            if (revealGlowSprite != null)
            {
                return revealGlowSprite;
            }

            const int size = 64;
            Texture2D texture = NewSoftTexture("BossRushWish_RevealGlow", size, size);
            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float falloff = 1f - BossRushUI.SmoothStep(Mathf.Sqrt(dx * dx + dy * dy) / half);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(falloff * falloff * 255f));
                }
            }
            revealGlowSprite = FinishSoftSprite(texture, pixels);
            return revealGlowSprite;
        }

        private static Sprite GetMarkerBeamSprite()
        {
            if (markerBeamSprite != null)
            {
                return markerBeamSprite;
            }

            const int width = 32;
            const int height = 64;
            Texture2D texture = NewSoftTexture("BossRushWish_MarkerBeam", width, height);
            Color32[] pixels = new Color32[width * height];
            float halfWidth = width * 0.5f;
            float endFade = height * 0.2f;
            for (int y = 0; y < height; y++)
            {
                float ends = BossRushUI.SmoothStep(Mathf.Min(y + 0.5f, height - y - 0.5f) / endFade);
                for (int x = 0; x < width; x++)
                {
                    float across = 1f - BossRushUI.SmoothStep(Mathf.Abs(x + 0.5f - halfWidth) / halfWidth);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(across * across * ends * 255f));
                }
            }
            markerBeamSprite = FinishSoftSprite(texture, pixels);
            return markerBeamSprite;
        }

        private static Texture2D NewSoftTexture(string name, int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        private static Sprite FinishSoftSprite(Texture2D texture, Color32[] pixels)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static void DestroyRevealSprites()
        {
            DestroySoftSprite(revealGlowSprite);
            revealGlowSprite = null;
            DestroySoftSprite(markerBeamSprite);
            markerBeamSprite = null;
        }

        private static void DestroySoftSprite(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            Texture2D texture = sprite.texture;
            Object.Destroy(sprite);
            if (texture != null)
            {
                Object.Destroy(texture);
            }
        }
    }
}
