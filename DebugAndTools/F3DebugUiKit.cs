// ============================================================================
// F3DebugUiKit.cs - F3 调试菜单的界面构件（2026-10-25 从 F3DebugCheatMenuUi.cs 提出）
// ============================================================================
// 宿主 partial 里只留一行转发；新页面（录制模式）直接调这里。
// 质感走共享层：面板 ApplyFramedPanelSkin，按钮 ApplyPanelSkin + SetButtonBaseColor（悬停 / 点击音效与按下回弹），
// 颜色用 BossRushUIColors token。按钮宽度按文字量自动加宽，长标签不再溢出到按钮外。
// ============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal static class F3DebugUiKit
    {
        internal const float ButtonMinWidth = 150f;
        internal const float ButtonHeight = 40f;

        internal static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>按锚点与四边偏移摆放（offsetMin = 左下，offsetMax = 右上）。</summary>
        internal static RectTransform Place(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        /// <summary>带底色与描边的整块面（标题栏、摘要、状态栏）。</summary>
        internal static Image CreateSurface(Transform parent, string name, Color color, int radius, BossRushUISkinPart part)
        {
            GameObject go = CreateUIObject(name, parent);
            Image image = go.AddComponent<Image>();
            image.color = color;
            BossRushUI.ApplyFramedPanelSkin(image, radius, part);
            return image;
        }

        /// <summary>带描边的卡片：标题 + 可选说明，子项竖排。</summary>
        internal static GameObject CreateSection(Transform parent, string title, string description, Font font)
        {
            GameObject section = CreateUIObject("Section_" + title, parent);
            Image sectionImage = section.AddComponent<Image>();
            sectionImage.color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyFramedPanelSkin(sectionImage, 10, BossRushUISkinPart.Card);
            VerticalLayoutGroup layout = section.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 16);
            layout.spacing = 10f;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            section.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            section.AddComponent<LayoutElement>().minHeight = 100f;

            CreateLabel(section.transform, font, title, 22, BossRushUIColors.TextPrimary, FontStyle.Bold);
            if (!string.IsNullOrEmpty(description))
            {
                CreateLabel(section.transform, font, description, 16, BossRushUIColors.TextSecondary, FontStyle.Normal);
            }
            return section;
        }

        /// <summary>
        /// 竖向滚动列表：遮罩视口 + 内容按子项自适应高度 + 共享滚动条（右侧 padding 至少留 20 给滑块）。
        /// 子项高度交给布局（childControlHeight=true）时按钮的 LayoutElement 才生效；
        /// 为 false 时 Unity 直接取子项 sizeDelta，新建 RectTransform 是 100×100。
        /// </summary>
        internal static Transform CreateScrollList(Transform parent, RectOffset padding, float spacing, bool controlChildHeight)
        {
            GameObject scrollView = CreateUIObject("ScrollView", parent);
            Place(scrollView, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            ScrollRect scroll = scrollView.AddComponent<ScrollRect>();

            GameObject viewport = CreateUIObject("Viewport", scrollView.transform);
            RectTransform viewportRect = Place(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            // Mask 依赖 Image 写 Stencil：默认 UI Shader 开 UNITY_UI_ALPHACLIP，a < 0.001 会 discard、子节点全被裁掉（内容区空白）。
            // 所以 alpha 必须 > 0，再用 showMaskGraphic=false 把颜色本身藏起来。
            viewport.AddComponent<Image>().color = Color.white;
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            GameObject content = CreateUIObject("Content", viewport.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childControlHeight = controlChildHeight;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            BossRushUI.ConfigureScrollRect(scroll);
            return content.transform;
        }

        /// <summary>按钮行：宽度跟随父级，放不下就折到下一行，不再向两侧溢出到导航或屏幕外。</summary>
        internal static GameObject CreateRow(Transform parent)
        {
            GameObject row = CreateUIObject("Row", parent);
            row.AddComponent<F3FlowRowLayout>();
            row.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return row;
        }

        internal static Text CreateLabel(Transform parent, Font font, string text, int fontSize, Color color, FontStyle style)
        {
            GameObject go = CreateUIObject("Label", parent);
            Text label = go.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.fontStyle = style;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return label;
        }

        internal static Text CreateLabelWithWidth(Transform parent, Font font, string text, int fontSize, float width)
        {
            GameObject go = CreateUIObject("Label", parent);
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            element.preferredHeight = 34f;
            Text label = go.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = BossRushUIColors.TextPrimary;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            return label;
        }

        internal static Text CreateCenteredText(Transform parent, Font font, string text, int fontSize, Color color)
        {
            Text label = CreateFillText(parent, font, fontSize, color, TextAnchor.MiddleCenter);
            label.rectTransform.offsetMin = new Vector2(8f, 0f);
            label.rectTransform.offsetMax = new Vector2(-8f, 0f);
            label.text = text;
            return label;
        }

        internal static Text CreateFillText(Transform parent, Font font, int fontSize, Color color, TextAnchor alignment)
        {
            GameObject textObject = CreateUIObject("Text", parent);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 6f);
            rect.offsetMax = new Vector2(-12f, -6f);
            Text label = textObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>共享质感按钮：宽度按文字量取，至少 <paramref name="minWidth"/>。</summary>
        internal static Button CreateButton(Transform parent, Font font, string text, Color color,
            UnityEngine.Events.UnityAction onClick, float minWidth, float height, int fontSize)
        {
            GameObject go = CreateUIObject("Button_" + text, parent);
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            Image image = go.AddComponent<Image>();
            BossRushUI.ApplyPanelSkin(image, 8, BossRushUISkinPart.Button);
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(onClick);
            Text label = CreateCenteredText(go.transform, font, text, fontSize, BossRushUI.GetButtonTextColor(color));
            element.preferredWidth = Mathf.Max(minWidth, Mathf.Ceil(label.preferredWidth) + 36f);
            ZombieModeUIHelper.SetButtonBaseColor(button, color);
            return button;
        }

        internal static Button CreateActionButton(Transform parent, Font font, string text, Color color, UnityEngine.Events.UnityAction onClick)
        {
            return CreateButton(parent, font, text, color, onClick, ButtonMinWidth, ButtonHeight, 17);
        }

        internal static Button CreatePresetButton(Transform parent, Font font, string text, UnityEngine.Events.UnityAction onClick)
        {
            return CreateButton(parent, font, text, BossRushUIColors.Header, onClick, 76f, 36f, 16);
        }

        /// <summary>左侧导航的选中态：AccentFill 填充；其余次级底色。</summary>
        internal static void SetNavSelected(Image image, bool selected)
        {
            Button button = image != null ? image.GetComponent<Button>() : null;
            if (button == null) return;
            ZombieModeUIHelper.SetButtonBaseColor(button, selected ? BossRushUIColors.AccentFill : BossRushUIColors.Header);
        }

        internal static InputField CreateInputField(Transform parent, Font font, string text, float width)
        {
            GameObject root = CreateUIObject("InputField", parent);
            LayoutElement element = root.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            element.preferredHeight = 40f;
            Image image = root.AddComponent<Image>();
            image.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(image, 6, BossRushUISkinPart.Button);
            InputField inputField = root.AddComponent<InputField>();

            Text placeholderText = CreateFillText(root.transform, font, 17, BossRushUIColors.TextSecondary, TextAnchor.MiddleLeft);
            placeholderText.gameObject.name = "Placeholder";
            placeholderText.text = text;
            Text fieldText = CreateFillText(root.transform, font, 17, BossRushUIColors.TextPrimary, TextAnchor.MiddleLeft);
            fieldText.text = text;

            inputField.textComponent = fieldText;
            inputField.placeholder = placeholderText;
            inputField.text = text;
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.contentType = InputField.ContentType.Standard;
            return inputField;
        }

        internal static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(parent.GetChild(i).gameObject);
            }
        }
    }

    /// <summary>
    /// 自动折行的按钮行。旧版是 HorizontalLayoutGroup + 横向 ContentSizeFitter：一行放不下时行宽按内容撑开、以中心为轴
    /// 向左右同时溢出，左半截被导航或屏幕边裁掉。这里行宽由父级给定，子项按 LayoutElement 首选尺寸排、超宽就换行。
    /// </summary>
    internal sealed class F3FlowRowLayout : LayoutGroup
    {
        private const float Spacing = 10f;

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            float min = 0f, preferred = 0f;
            for (int i = 0; i < rectChildren.Count; i++)
            {
                float width = LayoutUtility.GetPreferredWidth(rectChildren[i]);
                min = Mathf.Max(min, width);
                preferred += width + (i > 0 ? Spacing : 0f);
            }
            SetLayoutInputForAxis(min + padding.horizontal, preferred + padding.horizontal, -1f, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            float height = Arrange(-1) + padding.vertical;
            SetLayoutInputForAxis(height, height, -1f, 1);
        }

        public override void SetLayoutHorizontal() { Arrange(0); }

        public override void SetLayoutVertical() { Arrange(1); }

        /// <summary>逐行排布并返回内容总高；axis 为 0/1 时写入该轴，-1 只量不写。行内按最高子项垂直居中。</summary>
        private float Arrange(int axis)
        {
            float available = rectTransform.rect.width - padding.horizontal;
            if (available <= 0f) available = float.MaxValue;
            float y = 0f;
            int start = 0;
            while (start < rectChildren.Count)
            {
                float lineWidth = 0f, lineHeight = 0f;
                int end = start;
                while (end < rectChildren.Count)
                {
                    float width = Mathf.Min(LayoutUtility.GetPreferredWidth(rectChildren[end]), available);
                    float next = lineWidth + (end > start ? Spacing : 0f) + width;
                    if (end > start && next > available) break;
                    lineWidth = next;
                    lineHeight = Mathf.Max(lineHeight, LayoutUtility.GetPreferredHeight(rectChildren[end]));
                    end++;
                }
                float x = 0f;
                for (int i = start; i < end && axis >= 0; i++)
                {
                    RectTransform child = rectChildren[i];
                    float width = Mathf.Min(LayoutUtility.GetPreferredWidth(child), available);
                    float height = LayoutUtility.GetPreferredHeight(child);
                    if (axis == 0) SetChildAlongAxis(child, 0, padding.left + x, width);
                    else SetChildAlongAxis(child, 1, padding.top + y + (lineHeight - height) * 0.5f, height);
                    x += width + Spacing;
                }
                y += lineHeight + (end < rectChildren.Count ? Spacing : 0f);
                start = end;
            }
            return y;
        }
    }
}
