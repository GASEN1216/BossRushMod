// ============================================================================
// WishFountainUI_Feel.cs - 许愿面板的观感与节奏（WishFountainView 的 partial）
// ============================================================================
// 2026-09-23 审美审查 UD-17 / UD-19 / UD-20 / UD-21。放在独立文件是因为 WishFountainUI.cs 有冻结行数上限。
//   - 配色只用 BossRushUIColors token：面板 Surface、内容卡 SurfaceRaised、输入框 Surface / 焦点 Header，
//     描边环 Stroke / 焦点 Accent / 成功 SuccessText（旧版是 token 之外的一整套藏青与亮天蓝）。
//   - 层级靠留白与细线，不再框里套框：顶边左段 Accent 细线、状态行左侧状态色细竖条、滚动条胶囊。
//   - 匿名勾选框：圆角框 + ColorBlock 三态（悬停提亮、按下压暗）+ 程序化对勾 + 官方 hover / click 音效。
//   - 许愿成功：状态淡入、官方 UI/confirm 音，停 0.6 秒再淡出关闭并开始抽奖（旧版同一帧就关，成功反馈没人看得到）。
//     抽奖只在 OnClose 里经 ReleaseSuccessBeatOnClose 触发一次：自动关、按取消、按 Esc、回车都走同一出口。
// ============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class WishFountainView
    {
        /// <summary>许愿成功后停顿多久再关闭（秒，unscaled，暂停菜单开着时不计）。</summary>
        private const float SuccessBeatSeconds = 0.6f;

        private Image statusRail;
        private bool rewardPendingOnClose;
        // 对勾贴图（DontSave），随 View 的 OnDestroy 销毁；下次新建 View 时按需重画。
        private static Sprite checkmarkSprite;

        // ====================================================================
        // 层级：细线与竖条
        // ====================================================================

        /// <summary>顶边左段 36% 宽、2px 的 Accent 细线（与 CreateModalSurface 的 topTrace 同一视觉语言），避开左上圆角。</summary>
        private static void AddTopTrace(RectTransform panel)
        {
            GameObject trace = new GameObject("TopTrace", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            trace.transform.SetParent(panel, false);
            trace.GetComponent<LayoutElement>().ignoreLayout = true;
            RectTransform rect = trace.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0.36f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(18f, -3f);
            rect.offsetMax = new Vector2(0f, -1f);
            Image image = trace.GetComponent<Image>();
            image.color = WithAlpha(BossRushUIColors.Accent, 0.45f);
            image.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(image, 1, BossRushUISkinPart.Hairline);
        }

        /// <summary>状态行左侧 3px 状态色竖条；没有状态文字时透明。</summary>
        private static Image CreateStatusRail(RectTransform statusCard)
        {
            GameObject rail = new GameObject("StatusRail", typeof(RectTransform), typeof(Image));
            rail.transform.SetParent(statusCard, false);
            RectTransform rect = rail.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(0f, 10f);
            rect.offsetMax = new Vector2(3f, -10f);
            Image image = rail.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(image, 1, BossRushUISkinPart.Hairline);
            return image;
        }

        private void ApplyStatusRail(string text, Color color)
        {
            if (statusRail != null)
            {
                statusRail.color = string.IsNullOrWhiteSpace(text) ? Color.clear : color;
            }
        }

        /// <summary>滚动条：轨道与滑块都用胶囊档（与 BossRushUI.ConfigureScrollRect 同口径），滑块三态由 ColorBlock 承担。</summary>
        private static void StyleInputScrollbar(Image track, Image handle, Scrollbar scrollbar)
        {
            if (track != null)
            {
                track.color = BossRushUIColors.SurfaceRaised;
                BossRushUI.ApplyPanelSkin(track, 4, BossRushUISkinPart.ScrollHandle);
            }

            if (handle != null)
            {
                handle.color = Color.white;   // 绝对色住在 ColorBlock，Graphic 置白，不做平方乘色
                BossRushUI.ApplyPanelSkin(handle, 4, BossRushUISkinPart.ScrollHandle);
            }

            if (scrollbar == null)
            {
                return;
            }

            Color normal = BossRushUIColors.TextSecondary;
            ColorBlock colors = scrollbar.colors;
            colors.normalColor = normal;
            colors.highlightedColor = BossRushUI.GetHoverColor(normal);
            colors.pressedColor = BossRushUI.GetPressedColor(normal);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = BossRushUI.GetDisabledColor(normal);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            scrollbar.colors = colors;
        }

        // ====================================================================
        // 输入框状态：底色 + 描边环 + 占位字
        // ====================================================================

        private void ApplyInputVisualState(bool inputFocused)
        {
            bool focusedAndEditable = inputFocused && !sending && !successDisplayed;
            if (inputContainerImage != null)
            {
                inputContainerImage.color = focusedAndEditable ? BossRushUIColors.Header : BossRushUIColors.Surface;
            }

            if (inputContainerStroke != null)
            {
                // 描边环只换颜色、不换粗细：旧版 UI.Outline 焦点时偏移 (2,-2)，边框变成一条歪的粗边
                inputContainerStroke.color = successDisplayed
                    ? BossRushUIColors.SuccessText
                    : (sending ? WithAlpha(BossRushUIColors.Accent, 0.55f)
                        : (inputFocused ? BossRushUIColors.Accent : BossRushUIColors.Stroke));
            }

            if (placeholderText != null && !successDisplayed)
            {
                placeholderText.color = inputFocused
                    ? BossRushUIColors.TextSecondary
                    : WithAlpha(BossRushUIColors.TextSecondary, 0.7f);
            }

            if (inputFocusHintText != null)
            {
                inputFocusHintText.text = L10n.T(
                    "请不要输入无效/垃圾内容哦~",
                    "Please don't enter invalid or spam content~");
                inputFocusHintText.color = BossRushUIColors.WarningText;
            }
        }

        // ====================================================================
        // 匿名勾选框
        // ====================================================================

        private static void StyleAnonymousToggle(Toggle toggle, Image background)
        {
            if (toggle == null || background == null)
            {
                return;
            }

            BossRushUI.ApplyFramedPanelSkin(background, 4, BossRushUISkinPart.Button);
            background.color = Color.white;   // 与 ApplyButtonColors 同一套路：底色住在 ColorBlock

            Color normal = BossRushUIColors.Surface;
            ColorBlock colors = toggle.colors;
            colors.normalColor = normal;
            colors.highlightedColor = BossRushUI.GetHoverColor(normal);
            colors.pressedColor = BossRushUI.GetPressedColor(normal);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = BossRushUI.GetDisabledColor(normal);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            toggle.colors = colors;
            toggle.transition = Selectable.Transition.ColorTint;
            toggle.targetGraphic = background;
            // 即时落到常态色：否则新建时先按默认 ColorBlock 白一下再渐变回来
            background.CrossFadeColor(normal, 0f, true, true);

            GameObject check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(background.transform, false);
            RectTransform checkRect = check.GetComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = new Vector2(3f, 3f);
            checkRect.offsetMax = new Vector2(-3f, -3f);
            Image checkImage = check.GetComponent<Image>();
            checkImage.sprite = GetCheckmarkSprite();
            checkImage.preserveAspect = true;
            checkImage.color = BossRushUIColors.Accent;
            checkImage.raycastTarget = false;
            toggle.graphic = checkImage;

            // 官方 UI/hover、UI/click 音效与按下回弹（共享手感组件接受任意 Selectable）
            BossRushButtonFeel.Attach(toggle);
        }

        /// <summary>程序化对勾：两段圆头折线的距离场，抗锯齿，32×32 白色，着色交给 Image。</summary>
        private static Sprite GetCheckmarkSprite()
        {
            if (checkmarkSprite != null)
            {
                return checkmarkSprite;
            }

            const int size = 32;
            const float halfWidth = 2.3f;
            Vector2 a = new Vector2(7f, 16.5f);
            Vector2 b = new Vector2(13.5f, 10f);
            Vector2 c = new Vector2(25.5f, 23f);
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.name = "BossRushWish_Checkmark";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    float distance = Mathf.Min(DistanceToSegment(point, a, b), DistanceToSegment(point, b, c));
                    float alpha = Mathf.Clamp01(halfWidth + 0.5f - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            checkmarkSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            checkmarkSprite.name = texture.name;
            checkmarkSprite.hideFlags = HideFlags.HideAndDontSave;
            return checkmarkSprite;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / Mathf.Max(segment.sqrMagnitude, 0.0001f));
            return Vector2.Distance(point, start + segment * t);
        }

        // ====================================================================
        // 许愿成功的节奏：停一拍 → 淡出关闭 → 开始抽奖
        // ====================================================================

        private void BeginSuccessBeat()
        {
            rewardPendingOnClose = true;
            if (!open)
            {
                // 发送途中面板已被关掉（淡出期间回包）：没有停顿可看，直接开抽，别让协程随根节点失活而丢掉
                ReleaseSuccessBeatOnClose();
                return;
            }

            IntegrationUIFeedback.PlaySound(IntegrationUIFeedback.SoundConfirm);
            if (statusText != null)
            {
                BossRushUIEntranceAnimation.Play(statusText.gameObject, 0f, 0.25f, 0f);
            }

            if (autoCloseCoroutine != null)
            {
                StopCoroutine(autoCloseCoroutine);
            }
            autoCloseCoroutine = StartCoroutine(SuccessBeatThenClose());
        }

        private IEnumerator SuccessBeatThenClose()
        {
            float waited = 0f;
            while (waited < SuccessBeatSeconds)
            {
                if (!BossRushUI.IsGamePaused())
                {
                    waited += Time.unscaledDeltaTime;
                }
                yield return null;
            }

            autoCloseCoroutine = null;
            if (open)
            {
                Close();   // OnClose → ReleaseSuccessBeatOnClose 开始抽奖
            }
        }

        /// <summary>
        /// 关闭（或成功停顿中被重开）时把抽奖发出去。幂等：只在成功后的第一次调用生效，
        /// 自动关闭、取消按钮、Esc、回车都走这里，不会重复开抽。
        /// </summary>
        private void ReleaseSuccessBeatOnClose()
        {
            if (autoCloseCoroutine != null)
            {
                StopCoroutine(autoCloseCoroutine);
                autoCloseCoroutine = null;
            }

            if (!rewardPendingOnClose)
            {
                return;
            }

            rewardPendingOnClose = false;
            NotifyClosedAfterSuccessfulWish();
        }

        private void DestroyFeelResources()
        {
            // OnDestroy 里 Instance 已先置空；还有别的存活实例（Awake 判重销毁的那个副本）时不能把共用贴图拆掉
            if (checkmarkSprite == null || Instance != null)
            {
                return;
            }

            Texture2D texture = checkmarkSprite.texture;
            Destroy(checkmarkSprite);
            if (texture != null)
            {
                Destroy(texture);
            }
            checkmarkSprite = null;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
