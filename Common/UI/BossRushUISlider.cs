using System;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>共享整数滑条，沿用丧尸休息时间的轨道、填充与手柄样式。</summary>
    internal static class BossRushUISlider
    {
        internal static Slider Create(string name, Transform parent, Vector2 anchor, Vector2 position,
            float width, int minimum, int maximum, int value, Action<float> onChanged)
        {
            GameObject sliderObject = ZombieModeUIHelper.CreateRect(name, parent, anchor, anchor,
                position, new Vector2(width, 24f), new Vector2(0.5f, 0.5f));
            Slider slider = sliderObject.AddComponent<Slider>();
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.wholeNumbers = true;
            slider.direction = Slider.Direction.LeftToRight;

            GameObject track = ZombieModeUIHelper.CreateRect("Track", sliderObject.transform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(0f, 8f), new Vector2(0.5f, 0.5f));
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(trackImage, 4, BossRushUISkinPart.ScrollHandle);
            GameObject fillArea = ZombieModeUIHelper.CreateRect("FillArea", sliderObject.transform,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(-12f, 8f), new Vector2(0.5f, 0.5f));
            GameObject fill = ZombieModeUIHelper.CreateRect("Fill", fillArea.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = BossRushUIColors.Accent;
            BossRushUI.ApplyPanelSkin(fillImage, 4, BossRushUISkinPart.ScrollHandle);
            slider.fillRect = fill.GetComponent<RectTransform>();
            GameObject handleArea = ZombieModeUIHelper.CreateRect("HandleArea", sliderObject.transform,
                Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-12f, 0f), new Vector2(0.5f, 0.5f));
            GameObject handle = ZombieModeUIHelper.CreateRect("Handle", handleArea.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(20f, 20f), new Vector2(0.5f, 0.5f));
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;
            BossRushUI.ApplyPanelSkin(handleImage, 10, BossRushUISkinPart.Button);
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handleImage;
            ColorBlock handleColors = slider.colors;
            handleColors.normalColor = BossRushUIColors.TextPrimary;
            handleColors.highlightedColor = Color.Lerp(BossRushUIColors.TextPrimary, BossRushUIColors.Accent, 0.45f);
            handleColors.pressedColor = BossRushUIColors.Accent;
            handleColors.selectedColor = BossRushUIColors.TextPrimary;
            handleColors.disabledColor = BossRushUI.GetDisabledColor(BossRushUIColors.TextPrimary);
            handleColors.colorMultiplier = 1f;
            handleColors.fadeDuration = 0.08f;
            slider.colors = handleColors;
            slider.value = Mathf.Clamp(value, minimum, maximum);
            if (onChanged != null) slider.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<float>(onChanged));
            return slider;
        }
    }
}
