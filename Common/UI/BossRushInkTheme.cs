// ============================================================================
// BossRushInkTheme.cs - 水墨主题接管共享 UI 库：全项目统一成出征令的风格
// ============================================================================
// 模块说明：
//   2026-11-25 owner：「我们项目已有的 UI，你觉得可以改的都改成出征令这种 UI……统一一下我们项目的风格，
//   ……高级高端大气感，极简化。但是已有的功能都要有」。
//   出征令用的是 BossRushInkSkin（宣纸底、墨笔边、鎏金、朱印）。全项目 100 多个文件的界面都经过共享库的几个入口，
//   所以这里只接管入口，调用方一行不改，功能自然全在：
//
//   1. 底图（BossRushUISkin.GetInjected → BossRushUI.ApplyPanelSkin）：面板 / 卡片 / 按钮三档换成崩边墨块。
//      同一张贴图按档换 pixelsPerUnit（100 / 160 / 240），等于 ApplyInkPanel / StyleInkButton 的 ppum 1 / 1.6 / 2.4，
//      调用方照旧写 ppum = 1。分隔线档换成一笔淡金横笔（与 CreateInkDivider 同形）。细条、滚动滑块照旧。
//   2. 描边（BossRushUI.ApplyPanelStroke）：圆角描边环换成墨笔边。描边色仍由调用方给（Stroke / Accent / 选中色），
//      之后的重着色（焦点、选中）照常生效。卡片档不另调淡：天空岛剧情面板的选项行压在亮插图上，
//      墨笔边要过非文本 3:1 才看得出「这一行能点」（SkyIslandUiContrastGuard 按墨笔边的真实覆盖率复算）。
//   3. 投影与斜面（BossRushUIDepth）：不挂。圆角高光压在崩边墨块上正是塑料感的来源，挖空的圆角投影也对不上崩边。
//      按钮改按角色挂墨笔边：主操作（AccentFill 底）金边；危险（Danger 底）改墨底 + 朱边 + 浅朱字，
//      和主操作的朱砂底分得开（UI 共识：危险是红边红字，不是红块）；其余用描边色。
//   4. 打开动效（BossRushUIOpenAnimation）：慢而沉，背后一滴墨洇开再散，不回弹；面板里的墨笔标题金笔随后从左写出。
//
//   配色在 BossRushUIColors 里同步换成水墨 token，与 BossRushInkColors 同值：宣纸字、鎏金强调、朱砂主操作。
//   贴图全部复用 BossRushInkSkin 的程序化贴图，只另建换了 pixelsPerUnit 的 Sprite 壳（不复制纹理）。
// ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BossRush
{
    internal static class BossRushInkTheme
    {
        /// <summary>全局开关。true = 共享库整体走水墨主题。</summary>
        internal static readonly bool Active = true;

        private const float PanelPixelsPerUnit = 100f;
        private const float CardPixelsPerUnit = 160f;
        private const float ButtonPixelsPerUnit = 240f;
        /// <summary>分隔线：64 高的笔触压进 8 高的 rect，与 CreateInkDivider 同比例。</summary>
        private const float RulePixelsPerUnit = 800f;
        /// <summary>墨笔边外扩：笔画落在底图崩边附近（与 ApplyInkPanel 同口径）。</summary>
        private const float FrameOutset = 4f;
        /// <summary>面板上的宣纸纤维（与 ApplyInkPanel 同口径）。</summary>
        private static readonly Color FiberTint = new Color(1f, 0.93f, 0.80f, 0.045f);

        /// <summary>
        /// 危险按钮的墨底：近黑、带一点朱。浅朱字（DangerText）常态约 9:1、悬停 4.6:1；
        /// 由 <see cref="ZombieModeUIHelper.ApplyButtonColors"/> 把 Danger 底换成它，再按它认出危险角色。
        /// </summary>
        internal static readonly Color DangerFill = new Color(0.110f, 0.055f, 0.048f, 0.96f);

        private const string TitleStrokeName = "InkTitleStroke";

        private static Sprite panelSurface;
        private static Sprite cardSurface;
        private static Sprite buttonSurface;
        private static Sprite ruleSprite;
        private static Sprite panelFrame;
        private static Sprite cardFrame;
        private static Sprite buttonFrame;

        // ------------------------------------------------------------------
        // 底图与墨笔边
        // ------------------------------------------------------------------

        /// <summary>这一档的水墨底图；不归水墨主题管的档（细条、滚动滑块）返回 null。</summary>
        internal static Sprite GetSurface(BossRushUISkinPart part)
        {
            if (!Active) return null;
            switch (part)
            {
                case BossRushUISkinPart.Panel: return Variant(ref panelSurface, BossRushInkSkin.GetSurfaceSprite(), PanelPixelsPerUnit, "Panel");
                case BossRushUISkinPart.Card: return Variant(ref cardSurface, BossRushInkSkin.GetSurfaceSprite(), CardPixelsPerUnit, "Card");
                case BossRushUISkinPart.Button: return Variant(ref buttonSurface, BossRushInkSkin.GetSurfaceSprite(), ButtonPixelsPerUnit, "Button");
                case BossRushUISkinPart.Rule: return Variant(ref ruleSprite, BossRushInkSkin.GetBrushSprite(), RulePixelsPerUnit, "Rule");
                default: return null;
            }
        }

        private static Sprite GetFrame(BossRushUISkinPart part)
        {
            switch (part)
            {
                case BossRushUISkinPart.Panel: return Variant(ref panelFrame, BossRushInkSkin.GetFrameSprite(), PanelPixelsPerUnit, "PanelFrame");
                case BossRushUISkinPart.Card: return Variant(ref cardFrame, BossRushInkSkin.GetFrameSprite(), CardPixelsPerUnit, "CardFrame");
                case BossRushUISkinPart.Button: return Variant(ref buttonFrame, BossRushInkSkin.GetFrameSprite(), ButtonPixelsPerUnit, "ButtonFrame");
                default: return null;
            }
        }

        /// <summary>
        /// <see cref="BossRushUI.ApplyPanelStroke"/> 的水墨分支：把描边 Image 换成这一档的墨笔边。
        /// 面板档顺带铺一层宣纸纤维。返回 false 表示这一档不归水墨主题管，调用方照旧画圆角描边环。
        /// </summary>
        internal static bool TryApplyFrame(Image stroke, int radius, BossRushUISkinPart part)
        {
            if (!Active || stroke == null) return false;
            BossRushUISkinPart resolved = BossRushUI.ResolvePart(radius, part);
            Sprite frame = GetFrame(resolved);
            if (frame == null) return false;
            ShapeFrame(stroke, frame);
            Transform host = stroke.transform.parent;
            if (host != null)
            {
                // 同一块面先被当按钮挂过角色墨笔边、又被调用方描了边：留描边这一道，免得双框。
                Transform duplicate = host.Find(BossRushInkSkin.FrameName);
                if (duplicate != null && duplicate != stroke.transform) Object.Destroy(duplicate.gameObject);
                if (resolved == BossRushUISkinPart.Panel) AddFiber(host);
            }
            return true;
        }

        /// <summary>
        /// <see cref="BossRushUIDepth.ApplyButtonDepth"/> 的水墨分支：按按钮角色挂墨笔边（幂等，重着色时只换色）。
        /// 已经被 <see cref="BossRushUI.ApplyPanelStroke"/> 描过边的按钮不再加第二道。
        /// </summary>
        internal static bool TryApplyButtonFrame(Image image, BossRushUISkinPart part, Color baseColor)
        {
            if (!Active || image == null) return false;
            Sprite sprite = GetFrame(part);
            if (sprite == null) return false;
            Transform host = image.transform;
            if (host.Find("Stroke") != null) return true;
            Transform existing = host.Find(BossRushInkSkin.FrameName);
            Image frame = existing != null ? existing.GetComponent<Image>() : null;
            if (frame == null)
            {
                GameObject layer = new GameObject(BossRushInkSkin.FrameName, typeof(RectTransform));
                layer.transform.SetParent(host, false);
                layer.AddComponent<LayoutElement>().ignoreLayout = true;
                frame = layer.AddComponent<Image>();
                frame.raycastTarget = false;
                ShapeFrame(frame, sprite);
            }
            frame.color = RoleFrameColor(baseColor);
            return true;
        }

        private static void ShapeFrame(Image frame, Sprite sprite)
        {
            frame.sprite = sprite;
            frame.type = Image.Type.Sliced;
            frame.fillCenter = false;
            frame.pixelsPerUnitMultiplier = 1f;
            RectTransform rect = frame.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-FrameOutset, -FrameOutset);
            rect.offsetMax = new Vector2(FrameOutset, FrameOutset);
        }

        private static void AddFiber(Transform host)
        {
            if (host.Find(BossRushInkSkin.FiberName) != null) return;
            GameObject layer = new GameObject(BossRushInkSkin.FiberName, typeof(RectTransform));
            layer.transform.SetParent(host, false);
            layer.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform rect = layer.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 10f);
            rect.offsetMax = new Vector2(-10f, -10f);
            Image fiber = layer.AddComponent<Image>();
            fiber.sprite = BossRushInkSkin.GetFiberSprite();
            fiber.type = Image.Type.Tiled;
            fiber.color = FiberTint;
            fiber.raycastTarget = false;
            layer.transform.SetSiblingIndex(0);
        }

        // ------------------------------------------------------------------
        // 按钮角色
        // ------------------------------------------------------------------

        /// <summary>调用方给的底色是不是「危险」（Danger 实色；幽灵关闭键那种透明 / 半透明的 Danger 不算）。</summary>
        internal static bool IsDangerBase(Color color)
        {
            return Active && color.a > 0.95f && SameHue(color, BossRushUIColors.Danger);
        }

        /// <summary>按钮底色是不是被换过的危险墨底。</summary>
        internal static bool IsDangerFill(Color color)
        {
            return Active && SameHue(color, DangerFill) && Mathf.Abs(color.a - DangerFill.a) < 0.01f;
        }

        private static Color RoleFrameColor(Color baseColor)
        {
            if (IsDangerFill(baseColor)) return BossRushUIColors.Danger;
            if (SameHue(baseColor, BossRushUIColors.AccentFill)) return BossRushUIColors.Accent;
            return BossRushUIColors.Stroke;
        }

        private static bool SameHue(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;
        }

        // ------------------------------------------------------------------
        // 打开动效：背后一滴墨洇开再散
        // ------------------------------------------------------------------

        /// <summary>
        /// 面板打开时在它背后洇开一滴墨（0.55 秒）再散去（0.5 秒），之后自毁，不留在屏幕上——
        /// 面板被 SetActive(false) 复用、或者布局重排，都不会剩下一块墨斑。
        /// 只是表现：没有父物体（根画布）或面板太小（按钮、小卡）时不放。
        /// </summary>
        internal static void SpawnOpenBloom(RectTransform panel)
        {
            if (!Active || panel == null) return;
            Transform parent = panel.parent;
            if (parent == null) return;
            Vector2 size = panel.rect.size;
            if (size.x < 240f || size.y < 160f) return;
            GameObject bloomObject = new GameObject(BossRushInkSkin.BloomName, typeof(RectTransform));
            bloomObject.transform.SetParent(parent, false);
            bloomObject.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform bloom = bloomObject.GetComponent<RectTransform>();
            // 固定中心锚点、按面板实际矩形定位：照抄拉伸锚点时 sizeDelta 是相对父物体的增量，墨晕会变成全屏暗斑；
            // 照抄非中心轴心时墨晕偏到一角。
            bloom.anchorMin = bloom.anchorMax = bloom.pivot = new Vector2(0.5f, 0.5f);
            RectTransform parentRect = parent as RectTransform;
            Vector2 panelCenter = (Vector2)panel.localPosition + panel.rect.center;
            bloom.anchoredPosition = parentRect != null ? panelCenter - parentRect.rect.center : panelCenter;
            float side = Mathf.Max(size.x, size.y) * 1.22f;
            bloom.sizeDelta = new Vector2(side, side * Mathf.Clamp(size.y / Mathf.Max(1f, size.x), 0.55f, 1f));
            bloom.SetSiblingIndex(panel.GetSiblingIndex());
            Image blot = bloomObject.AddComponent<Image>();
            blot.sprite = BossRushInkSkin.GetBlotSprite();
            blot.raycastTarget = false;
            Color ink = BossRushInkColors.InkWash;
            blot.color = new Color(ink.r, ink.g, ink.b, 0f);
            BossRushInkMotion motion = BossRushInkMotion.For(bloomObject);
            motion.Add(0f, 0.55f, true, t =>
            {
                if (bloom == null) return;
                bloom.localScale = Vector3.one * Mathf.Lerp(0.35f, 1f, t);
                blot.color = new Color(ink.r, ink.g, ink.b, 0.5f * Mathf.Min(1f, t * 1.6f));
            });
            motion.Add(0.55f, 0.5f, false, t =>
            {
                if (blot != null) blot.color = new Color(ink.r, ink.g, ink.b, 0.5f * (1f - t));
            }, () => { if (bloomObject != null) Object.Destroy(bloomObject); });
        }

        /// <summary>
        /// 面板打开时把里面的墨笔标题金笔（<see cref="BossRushInkRevealStroke"/>）从左写出，
        /// 与 <see cref="BossRushInkSkin.PlayInkReveal"/> 同一节奏。没有墨笔标题的面板什么都不做。
        /// </summary>
        internal static void WriteRevealStrokes(RectTransform panel)
        {
            if (!Active || panel == null) return;
            BossRushInkRevealStroke[] strokes = panel.GetComponentsInChildren<BossRushInkRevealStroke>(true);
            if (strokes.Length == 0) return;
            BossRushInkMotion motion = BossRushInkMotion.For(panel.gameObject);
            for (int i = 0; i < strokes.Length; i++)
            {
                BossRushInkSkin.PlayBrushWrite(strokes[i].transform as RectTransform, 0.24f + i * 0.08f, 0.42f, motion);
            }
        }

        /// <summary>
        /// 给已有的面板标题配一笔金（出征令同款）：压在字下约 6 像素，宽 = 字宽 + 0.6 字号（不短于 3 个字号、不宽于标题框），
        /// 跟标题的水平对齐走；面板打开时随共享打开动效从左写出。字号、字体、粗细定好之后调一次，幂等。
        /// 返回金笔伸出标题框底边的高度，竖排布局的调用方据此让出间距。标题下面已经有分隔线的面板不要再加。
        /// </summary>
        internal static float AddTitleStroke(TextMeshProUGUI title)
        {
            if (!Active || title == null || string.IsNullOrEmpty(title.text)) return 0f;
            if (title.transform.Find(TitleStrokeName) != null) return 0f;
            title.ForceMeshUpdate(true, false);
            Bounds glyphs = title.textBounds;
            if (glyphs.size.x <= 0f) return 0f;
            RectTransform rect = title.rectTransform;
            float size = title.fontSize;
            float width = Mathf.Min(rect.rect.width, Mathf.Max(size * 3f, glyphs.size.x + size * 0.6f));
            float x;
            switch (title.horizontalAlignment)
            {
                case HorizontalAlignmentOptions.Left: x = glyphs.min.x + width * 0.5f - size * 0.3f; break;
                case HorizontalAlignmentOptions.Right: x = glyphs.max.x - width * 0.5f + size * 0.3f; break;
                default: x = glyphs.center.x; break;
            }
            float y = glyphs.min.y - 6f;
            Vector2 center = rect.rect.center;
            RectTransform stroke = BossRushInkSkin.CreateBrushStroke(title.transform, TitleStrokeName, new Vector2(width, 8f),
                new Vector2(x - center.x, y - center.y), BossRushUIColors.Accent);
            stroke.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            stroke.gameObject.AddComponent<BossRushInkRevealStroke>();
            return Mathf.Max(0f, rect.rect.yMin - (y - 4f));
        }

        // ------------------------------------------------------------------
        // 缓存
        // ------------------------------------------------------------------

        /// <summary>同一张纹理换 pixelsPerUnit 的 Sprite 壳。底图重建过（纹理已销毁）就换新壳。</summary>
        private static Sprite Variant(ref Sprite cache, Sprite source, float pixelsPerUnit, string name)
        {
            if (cache != null && cache.texture != null) return cache;
            if (cache != null) Object.Destroy(cache);
            cache = null;
            if (source == null || source.texture == null) return null;
            cache = Sprite.Create(source.texture, source.rect, new Vector2(0.5f, 0.5f), pixelsPerUnit, 0,
                SpriteMeshType.FullRect, source.border);
            cache.name = "BossRushInkTheme_" + name;
            cache.hideFlags = HideFlags.DontSave;
            return cache;
        }

        private static void DestroyVariant(ref Sprite sprite)
        {
            if (sprite != null) Object.Destroy(sprite);
            sprite = null;
        }

        /// <summary>只销毁 Sprite 壳（纹理归 BossRushInkSkin）。由 <see cref="BossRushInkSkin.ResetStaticCaches"/> 先调。</summary>
        internal static void ResetStaticCaches()
        {
            DestroyVariant(ref panelSurface);
            DestroyVariant(ref cardSurface);
            DestroyVariant(ref buttonSurface);
            DestroyVariant(ref ruleSprite);
            DestroyVariant(ref panelFrame);
            DestroyVariant(ref cardFrame);
            DestroyVariant(ref buttonFrame);
        }
    }
}
