// ============================================================================
// BossRushUIFeel.cs - 共享 UI 库的「质感」层：投影、斜面高光、按钮手感与官方 UI 音效
// ============================================================================
// 模块说明：
//   2026-09-23 owner 验收：「全面检查我们 mod 里的 UI 以及交互，确保符合审美，而不是塑料感」。
//   塑料感的共同根因在共享层——图集规格 §1 当初就把「质感（描边、内阴影、材质、图标）」留给了后续，
//   只做了形状：面板 / 卡片 / 按钮全是平涂圆角块 + 一圈 1.5px 描边，没有投影、没有受光面，
//   按钮按下去没有任何回馈、也没有声音（官方按钮挂 ButtonAnimation，悬停 / 按下都有 UI 音效）。
//
//   这里补三件事，全部挂在已有的共享入口上，调用方不用改：
//   1. 外投影（Depth_Shadow）：柔和的暗晕，只画在面板形状**之外**，略向下偏，让面板从背景上浮起来。
//   2. 斜面（Depth_Bevel）：顶边一道内高光 + 顶部极淡的受光，按钮另加底边暗线——平涂块有了「面」。
//   3. 按钮手感（BossRushButtonFeel）：官方 UI/hover、UI/click 音效；按下微缩、投影收紧，松开 ease-out 回弹。
//
//   挂载点：BossRushUI.ApplyPanelStroke（面板、卡片、CreateCard、CreateModalSurface、ApplyFramedPanelSkin 都经过它）
//   与 ZombieModeUIHelper.ApplyButtonColors（CreateButton、SetButtonBaseColor 与各界面手搓按钮都经过它）。
//   两处都幂等：按子物体名判重，重复调用不叠层。
//
//   【为什么投影做成「挖空的环」而不是垫在面板下面的整块】uGUI 的子物体一定画在父物体之后。
//   投影要跟着面板动（入场动画、滚动、HUD 位移），只能做子物体；整块投影会盖住面板自己。
//   所以投影贴图在面板形状内部是全透明的，只在外面有暗晕——子物体画在上面也不会压暗面板。
//   向下偏移用九宫格的不对称 border 实现（上下 border 不同），贴图里的「洞」永远严丝合缝对齐面板。
//
//   【强度为什么这么小】游戏是 Linear 色彩空间，UI 在线性光里混合：近黑面板上叠 α=0.03 的白，
//   sRGB 读数就从 0.05 跳到 0.20。高光的 α 按线性光定，不能照 sRGB 的手感给。
// ============================================================================

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// 面板 / 卡片 / 按钮的投影与斜面。全部程序化生成、按 (弧半径, 档位) 缓存共享。
    /// </summary>
    internal static class BossRushUIDepth
    {
        internal const string ShadowName = "Depth_Shadow";
        internal const string BevelName = "Depth_Bevel";

        /// <summary>底色低于这个不透明度就不投影：半透明浮层（HUD 字底、淡底行）加一圈暗晕像一个黑框。</summary>
        private const float MinShadowSurfaceAlpha = 0.75f;
        /// <summary>底色低于这个不透明度就不画斜面：幽灵按钮 / 纯文字按钮没有「面」。</summary>
        private const float MinBevelSurfaceAlpha = 0.5f;

        private enum Profile { Panel = 0, Card = 1, Button = 2 }

        // 三档的投影：外扩（画布单位）、下沉、强度。卡片之间常见 8–12 的间距，卡片档外扩压到 9，
        // 尾部在 6 个单位处已低于 1%，不会把相邻卡片的边压暗。
        private static readonly int[] ShadowExtent = { 22, 9, 7 };
        private static readonly int[] ShadowDrop = { 7, 3, 2 };
        private static readonly float[] ShadowStrength = { 0.50f, 0.34f, 0.32f };

        // 斜面：顶边内高光（1.5px 线）、顶部受光（只在上 border 内）、底边暗线（只给按钮）。
        // 面板与卡片是近黑底，按线性光混合，高光给 0.07、受光 0.010 已经看得见。
        // 按钮档烤的是**悬停**时的强度，常态由 Image alpha 乘 ButtonBevelRestAlpha（0.6）：常态高光 0.12、暗线 0.24。
        private static readonly float[] BevelHighlight = { 0.07f, 0.08f, 0.20f };
        private static readonly float[] BevelGlow = { 0.010f, 0.012f, 0.06f };
        private static readonly float[] BevelShade = { 0f, 0f, 0.40f };
        /// <summary>
        /// 高光线在边内的深度区间（画布单位）。面板 / 卡片有 1.5 宽的描边环（BossRushUI.StrokeThickness）占着 0–1.5，
        /// 高光要落在描边**里面**一圈（1.5–3），否则被描边盖掉；按钮没有描边，高光贴着外沿（0–1.5）。
        /// </summary>
        private static readonly float[] BevelLineInner = { 1.5f, 1.5f, 0f };
        private const float BevelLineWidth = 1.5f;
        /// <summary>按钮斜面常态的 Image alpha：悬停提到 1，按下压到约 0.33。</summary>
        internal const float ButtonBevelRestAlpha = 0.6f;

        private static readonly Dictionary<int, Sprite> shadowSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> bevelSprites = new Dictionary<int, Sprite>();

        /// <summary>
        /// 给一块已套好底图（与描边）的面板 / 卡片加投影与斜面。细条、分隔线、滚动条不加。
        /// 幂等：已有 <see cref="ShadowName"/> 或 <see cref="BevelName"/> 子物体就跳过对应那层。
        /// </summary>
        internal static void ApplySurfaceDepth(Image surface, int radius, BossRushUISkinPart part)
        {
            if (surface == null)
            {
                return;
            }
            ApplyDepth(surface, radius, part, surface.color.a);
        }

        /// <summary>
        /// 按钮的投影与斜面。按钮的底色住在 ColorBlock 里（Graphic 恒白），所以不透明度由调用方给。
        /// 底图没有九宫格 border（直角 quad、官方 prefab 的非切片图）时不加——圆角投影配直角按钮会错位。
        /// </summary>
        internal static void ApplyButtonDepth(Button button, Color baseColor)
        {
            if (button == null)
            {
                return;
            }
            Image image = button.targetGraphic as Image;
            if (image == null || image.sprite == null || image.type != Image.Type.Sliced || image.sprite.border.x < 1f)
            {
                return;
            }
            // 整张卡片当按钮用时（选人卡、契约卡）底图是卡片 / 面板图：按卡片档取弧与投影，不能套按钮的小圆角。
            BossRushUISkinPart part = BossRushUISkinPart.Button;
            if (image.sprite == BossRushUISkin.GetInjected(BossRushUISkinPart.Card)) part = BossRushUISkinPart.Card;
            else if (image.sprite == BossRushUISkin.GetInjected(BossRushUISkinPart.Panel)) part = BossRushUISkinPart.Panel;
            ApplyDepth(image, Mathf.RoundToInt(image.sprite.border.x), part, baseColor.a);
        }

        private static void ApplyDepth(Image surface, int radius, BossRushUISkinPart part, float surfaceAlpha)
        {
            Profile profile;
            if (!TryGetProfile(radius, part, out profile))
            {
                return;
            }

            int arc = BossRushUI.GetSkinCornerRadius(radius, part);
            Transform host = surface.transform;
            try
            {
                if (surfaceAlpha >= MinShadowSurfaceAlpha && host.Find(ShadowName) == null)
                {
                    int p = (int)profile;
                    int extent = ShadowExtent[p];
                    int drop = ShadowDrop[p];
                    Image shadow = CreateLayer(host, ShadowName, GetShadowSprite(arc, profile));
                    RectTransform rect = shadow.rectTransform;
                    rect.offsetMin = new Vector2(-extent, -(extent + drop));
                    rect.offsetMax = new Vector2(extent, extent - drop);
                    shadow.color = Color.white;   // 投影色烤在贴图里（黑），alpha 走贴图
                    shadow.transform.SetSiblingIndex(0);
                }

                if (surfaceAlpha >= MinBevelSurfaceAlpha && host.Find(BevelName) == null)
                {
                    Image bevel = CreateLayer(host, BevelName, GetBevelSprite(arc, profile));
                    bevel.color = new Color(1f, 1f, 1f, profile == Profile.Button ? ButtonBevelRestAlpha : 1f);
                    // 紧跟在投影之后：内容（标题、正文、图标）都画在斜面上面。
                    Transform shadowTransform = host.Find(ShadowName);
                    bevel.transform.SetSiblingIndex(shadowTransform != null ? shadowTransform.GetSiblingIndex() + 1 : 0);
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRushUIDepth] [WARNING] 挂投影 / 斜面失败: " + e.Message);
            }
        }

        private static bool TryGetProfile(int radius, BossRushUISkinPart part, out Profile profile)
        {
            profile = Profile.Card;
            if (part == BossRushUISkinPart.Auto)
            {
                if (radius <= 3) return false;
                part = radius >= 12 ? BossRushUISkinPart.Panel : BossRushUISkinPart.Button;
            }
            switch (part)
            {
                case BossRushUISkinPart.Panel: profile = Profile.Panel; return true;
                case BossRushUISkinPart.Card: profile = Profile.Card; return true;
                case BossRushUISkinPart.Button: profile = Profile.Button; return true;
                default: return false;   // Hairline / Rule / ScrollHandle 没有「面」
            }
        }

        private static Image CreateLayer(Transform host, string name, Sprite sprite)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(host, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            // 与描边同理：宿主带 LayoutGroup 时不能被排成一行（2026-09-14 审核 F-16）。
            obj.AddComponent<LayoutElement>().ignoreLayout = true;
            Image image = obj.AddComponent<Image>();
            image.raycastTarget = false;
            image.sprite = sprite;
            if (sprite != null)
            {
                image.type = Image.Type.Sliced;
                image.fillCenter = false;           // 两张图的中心区都是全透明
                image.pixelsPerUnitMultiplier = 1f;  // 1 纹素 = 1 画布单位，外扩与下沉按画布单位算
            }
            return image;
        }

        /// <summary>
        /// 投影贴图：挖空的柔和暗晕。纹素坐标 y 向上。
        ///   洞 = 面板形状（圆角矩形，弧半径 arc）；投影源 = 洞向下平移 drop；
        ///   alpha = 强度 × (1 - SmoothStep(源外距离 / 外扩))² × (1 - 洞覆盖率)。
        /// 九宫格 border：左右 extent+arc，下 extent+drop+arc，上 extent+arc；中心 2×2 可拉伸区落在洞里（全透明）。
        /// 洞的高度取 2·arc+drop+2，保证「洞的直边段」与「投影源的直边段」有 2 行重叠——纵向拉伸那两行时左右两条暗晕才是纯横向剖面。
        /// </summary>
        private static Sprite GetShadowSprite(int arc, Profile profile)
        {
            int key = arc * 4 + (int)profile;
            Sprite cached;
            if (shadowSprites.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            int p = (int)profile;
            int extent = ShadowExtent[p];
            int drop = ShadowDrop[p];
            float strength = ShadowStrength[p];

            float holeW = 2 * arc + 2;
            float holeH = 2 * arc + drop + 2;
            int width = extent * 2 + (int)holeW;
            int height = (extent + drop) + (int)holeH + (extent - drop);
            Vector2 holeMin = new Vector2(extent, extent + drop);
            Vector2 holeMax = holeMin + new Vector2(holeW, holeH);
            Vector2 casterMin = holeMin - new Vector2(0f, drop);
            Vector2 casterMax = holeMax - new Vector2(0f, drop);

            Texture2D texture = NewTexture("BossRushUI_Shadow_" + key, width, height);
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    float casterDistance = Mathf.Max(0f, RoundedRectDistance(point, casterMin, casterMax, arc));
                    float falloff = 1f - BossRushUI.SmoothStep(casterDistance / extent);
                    float holeCoverage = Mathf.Clamp01(0.5f - RoundedRectDistance(point, holeMin, holeMax, arc));
                    float alpha = strength * falloff * falloff * (1f - holeCoverage);
                    pixels[y * width + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            Sprite sprite = NewSprite(texture, "BossRushUI_Shadow_" + key,
                new Vector4(extent + arc, extent + drop + arc, extent + arc, extent + arc));
            shadowSprites[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 斜面贴图：与底图同形（边长 2·arc+2、border=arc）。沿边一圈 1.5px 的内线按边的朝向分色——
        /// 朝上的边白（高光），朝下的边黑（暗线，只有按钮档）；顶部 border 内再铺一层由上往下淡出的受光。
        /// 侧边（法线水平）两者都是 0，纵向拉伸的左右两列全透明。
        /// </summary>
        private static Sprite GetBevelSprite(int arc, Profile profile)
        {
            int key = arc * 4 + (int)profile;
            Sprite cached;
            if (bevelSprites.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            int p = (int)profile;
            float highlight = BevelHighlight[p];
            float glow = BevelGlow[p];
            float shade = BevelShade[p];
            float lineInner = BevelLineInner[p];
            float lineOuter = lineInner + BevelLineWidth;

            int size = arc * 2 + 2;
            Vector2 min = Vector2.zero;
            Vector2 max = new Vector2(size, size);
            Texture2D texture = NewTexture("BossRushUI_Bevel_" + key, size, size);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    float distance = RoundedRectDistance(point, min, max, arc);   // 内部为负
                    float coverage = Mathf.Clamp01(0.5f - distance);
                    if (coverage <= 0f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    float depth = -distance;
                    float line = Mathf.Clamp01(depth - lineInner + 0.5f) * Mathf.Clamp01(lineOuter - depth + 0.5f) * coverage;
                    float normalY = EdgeNormalY(point, min, max, arc);
                    float up = Mathf.Clamp01(normalY);
                    float down = Mathf.Clamp01(-normalY);

                    // 顶部受光：只在上 border（arc 行）内，离顶边越远越淡；拉伸的中间行不受影响。
                    float fromTop = size - point.y;
                    float topLight = fromTop < arc ? glow * (1f - fromTop / arc) * (1f - fromTop / arc) * coverage : 0f;

                    float white = Mathf.Max(line * up * highlight, topLight);
                    float black = line * down * shade;
                    pixels[y * size + x] = black > white
                        ? new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(black) * 255f))
                        : new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(white) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            Sprite sprite = NewSprite(texture, "BossRushUI_Bevel_" + key, new Vector4(arc, arc, arc, arc));
            bevelSprites[key] = sprite;
            return sprite;
        }

        /// <summary>圆角矩形的有向距离：外正内负。</summary>
        private static float RoundedRectDistance(Vector2 point, Vector2 min, Vector2 max, float radius)
        {
            Vector2 center = (min + max) * 0.5f;
            Vector2 half = (max - min) * 0.5f;
            float qx = Mathf.Abs(point.x - center.x) - half.x + radius;
            float qy = Mathf.Abs(point.y - center.y) - half.y + radius;
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }

        /// <summary>离这一点最近的那段边的法线的 y 分量（+1 朝上，-1 朝下，0 侧边）。</summary>
        private static float EdgeNormalY(Vector2 point, Vector2 min, Vector2 max, float radius)
        {
            float cx = Mathf.Clamp(point.x, min.x + radius, max.x - radius);
            float cy = Mathf.Clamp(point.y, min.y + radius, max.y - radius);
            Vector2 offset = new Vector2(point.x - cx, point.y - cy);
            if (offset.sqrMagnitude > 0.0001f)
            {
                return offset.normalized.y;   // 四角：沿圆心方向
            }
            // 直边段：看离上下左右哪条边近
            float toTop = max.y - point.y;
            float toBottom = point.y - min.y;
            float toSide = Mathf.Min(point.x - min.x, max.x - point.x);
            if (toTop <= toBottom && toTop <= toSide) return 1f;
            if (toBottom < toTop && toBottom <= toSide) return -1f;
            return 0f;
        }

        private static Texture2D NewTexture(string name, int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        private static Sprite NewSprite(Texture2D texture, string name, Vector4 border)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>销毁程序化投影 / 斜面贴图（带 DontSave，切场景不会自动回收）。经 BossRushUI.ResetStaticCaches 调用。</summary>
        internal static void ResetStaticCaches()
        {
            DestroySprites(shadowSprites);
            DestroySprites(bevelSprites);
        }

        private static void DestroySprites(Dictionary<int, Sprite> sprites)
        {
            foreach (KeyValuePair<int, Sprite> pair in sprites)
            {
                if (pair.Value == null)
                {
                    continue;
                }
                Texture2D texture = pair.Value.texture;
                Object.Destroy(pair.Value);
                if (texture != null)
                {
                    Object.Destroy(texture);
                }
            }
            sprites.Clear();
        }
    }

    /// <summary>
    /// 官方 UI 音效。官方按钮（<c>Duckov.UI.Animations.ButtonAnimation</c>）悬停播 <c>UI/hover</c>、按下播 <c>UI/click</c>，
    /// 本 Mod 的按钮照同一口径，玩家在官方界面和我们的界面之间来回切，手感一致。
    ///
    /// 【为什么走反射】<c>AudioManager.Post</c> 的返回值是 FMOD 类型，编译清单没有 FMOD 引用（见 RandomEventEffectsBridge 的注释）。
    /// MethodInfo 解析一次缓存；只在玩家悬停 / 点击时调用，不在每帧路径上。
    /// </summary>
    internal static class BossRushUISound
    {
        private static readonly object[] HoverArgs = { "UI/hover" };
        private static readonly object[] ClickArgs = { "UI/click" };
        /// <summary>鼠标快速扫过一列按钮时的悬停音最小间隔（秒）。</summary>
        private const float HoverInterval = 0.06f;

        private static MethodInfo postMethod;
        private static bool postResolved;
        private static float nextHoverTime;

        internal static void PlayHover()
        {
            float now = Time.unscaledTime;
            if (now < nextHoverTime)
            {
                return;
            }
            nextHoverTime = now + HoverInterval;
            Post(HoverArgs);
        }

        internal static void PlayClick()
        {
            Post(ClickArgs);
        }

        private static void Post(object[] args)
        {
            try
            {
                if (!postResolved)
                {
                    postResolved = true;
                    postMethod = typeof(global::Duckov.AudioManager).GetMethod("Post",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                }
                if (postMethod != null)
                {
                    postMethod.Invoke(null, args);
                }
            }
            catch (System.Exception)
            {
                // 音效不是关键路径：没有 AudioManager 实例或 FMOD 未就绪时静默跳过
            }
        }

        internal static void ResetStaticCaches()
        {
            postMethod = null;
            postResolved = false;
            nextHoverTime = 0f;
        }
    }

    /// <summary>
    /// 按钮手感：悬停 / 按下的官方音效，按下时微缩、投影收紧、斜面变暗，松开 ease-out 回弹。
    /// 只收指针事件、没有 Update；动效由 <see cref="BossRushButtonMotion"/> 播，播完自己停，常态零开销。
    /// 不可点（<c>IsInteractable()</c> 为假，含 CanvasGroup 禁用）时不出声、不动。
    /// </summary>
    internal sealed class BossRushButtonFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Selectable selectable;
        private BossRushButtonMotion motion;
        private bool hovered;
        private bool pressed;

        /// <summary>挂上（幂等）。只在建按钮 / 改按钮配色时调，不是每帧路径。</summary>
        internal static void Attach(Selectable target)
        {
            if (target == null)
            {
                return;
            }
            BossRushButtonFeel feel = target.GetComponent<BossRushButtonFeel>();
            if (feel == null)
            {
                feel = target.gameObject.AddComponent<BossRushButtonFeel>();
            }
            feel.selectable = target;
        }

        private bool Interactable
        {
            get { return selectable == null || selectable.IsInteractable(); }
        }

        /// <summary>指针此刻是否停在按钮上（ApplyButtonColors 原地改色时据此落到悬停色）。</summary>
        internal bool IsHovered
        {
            get { return hovered; }
        }

        private BossRushButtonMotion Motion
        {
            get
            {
                if (motion == null)
                {
                    motion = GetComponent<BossRushButtonMotion>();
                    if (motion == null)
                    {
                        motion = gameObject.AddComponent<BossRushButtonMotion>();
                    }
                }
                return motion;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            if (!Interactable)
            {
                return;
            }
            BossRushUISound.PlayHover();
            Motion.Play(pressed ? BossRushButtonMotion.State.Pressed : BossRushButtonMotion.State.Hover);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
            if (motion != null)
            {
                motion.Play(BossRushButtonMotion.State.Rest);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            if (!Interactable)
            {
                return;
            }
            pressed = true;
            BossRushUISound.PlayClick();
            Motion.Play(BossRushButtonMotion.State.Pressed);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pressed)
            {
                return;
            }
            pressed = false;
            if (motion != null)
            {
                motion.Play(hovered && Interactable ? BossRushButtonMotion.State.Hover : BossRushButtonMotion.State.Rest);
            }
        }

        private void OnDisable()
        {
            // 按着的时候被隐藏 / 整页重建：下次出现必须是常态，不能停在缩小的样子。
            hovered = false;
            pressed = false;
            if (motion != null)
            {
                motion.Snap(BossRushButtonMotion.State.Rest);
            }
        }
    }

    /// <summary>
    /// 按钮三态的动效：缩放、投影与斜面的强度。按下 0.06 秒、回弹 0.14 秒，都用 <see cref="BossRushUI.EaseOut"/>。
    /// 走 unscaled 时间（模态会把 timeScale 置 0），暂停菜单开着时停推进。播完 <c>enabled = false</c>，常态不跑 Update。
    ///
    /// 【缩放只在轴心居中时做】轴心在角上的按钮（锚在左上的列表行）按轴心缩会整块往一个角缩，比不动更怪；
    /// 这类按钮只做投影与斜面的变化。缩放幅度按尺寸算成「每边内收约 3 个单位」：小按钮 0.96，大卡片接近 0.99，
    /// 大卡片不会整张「塌」下去。
    /// </summary>
    internal sealed class BossRushButtonMotion : MonoBehaviour
    {
        internal enum State { Rest, Hover, Pressed }

        private const float PressSeconds = 0.06f;
        private const float ReleaseSeconds = 0.14f;
        private const float PressInsetUnits = 3f;
        private const float MinPressScale = 0.96f;

        private RectTransform rect;
        private Image shadow;
        private Image bevel;
        private bool resolved;
        private bool scales;
        private Vector3 baseScale = Vector3.one;
        private float pressScale = 1f;
        // 两层装饰的常态 alpha 在第一次交互时取一次：按钮档斜面常态是 0.6、卡片档是 1，不能写死一个数。
        private float restShadowAlpha = 1f;
        private float restBevelAlpha = 1f;

        // 动效在「系数」上插值：缩放系数、投影系数、斜面系数，常态都是 1。
        private float fromScale = 1f, toScale = 1f;
        private float fromShadow = 1f, toShadow = 1f;
        private float fromBevel = 1f, toBevel = 1f;
        private float elapsed, duration;

        private void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;
            rect = transform as RectTransform;
            Transform graphicHost = transform;
            Button button = GetComponent<Button>();
            if (button != null && button.targetGraphic != null)
            {
                graphicHost = button.targetGraphic.transform;
            }
            Transform shadowTransform = graphicHost.Find(BossRushUIDepth.ShadowName);
            Transform bevelTransform = graphicHost.Find(BossRushUIDepth.BevelName);
            shadow = shadowTransform != null ? shadowTransform.GetComponent<Image>() : null;
            bevel = bevelTransform != null ? bevelTransform.GetComponent<Image>() : null;
            restShadowAlpha = shadow != null ? shadow.color.a : 1f;
            restBevelAlpha = bevel != null ? bevel.color.a : 1f;

            baseScale = transform.localScale;
            scales = rect != null
                && Mathf.Abs(rect.pivot.x - 0.5f) < 0.01f && Mathf.Abs(rect.pivot.y - 0.5f) < 0.01f
                && GetComponent<Canvas>() == null;
            if (scales)
            {
                float span = Mathf.Max(rect.rect.width, rect.rect.height);
                pressScale = span > 1f ? Mathf.Clamp(1f - 2f * PressInsetUnits / span, MinPressScale, 1f) : 1f;
            }
        }

        internal void Play(State state)
        {
            Resolve();
            fromScale = scales && baseScale.x != 0f ? transform.localScale.x / baseScale.x : 1f;
            fromShadow = shadow != null && restShadowAlpha > 0f ? shadow.color.a / restShadowAlpha : 1f;
            fromBevel = bevel != null && restBevelAlpha > 0f ? bevel.color.a / restBevelAlpha : 1f;
            Targets(state, out toScale, out toShadow, out toBevel);
            duration = state == State.Pressed ? PressSeconds : ReleaseSeconds;
            elapsed = 0f;
            enabled = true;
        }

        internal void Snap(State state)
        {
            Resolve();
            float scale, shadowFactor, bevelFactor;
            Targets(state, out scale, out shadowFactor, out bevelFactor);
            Apply(scale, shadowFactor, bevelFactor);
            enabled = false;
        }

        private void Targets(State state, out float scale, out float shadowFactor, out float bevelFactor)
        {
            switch (state)
            {
                case State.Pressed:
                    scale = scales ? pressScale : 1f;
                    shadowFactor = 0.45f;   // 按下去贴近底面，投影收紧
                    bevelFactor = 0.55f;    // 受光面转开，高光变暗
                    return;
                case State.Hover:
                    scale = 1f;
                    shadowFactor = 1f;
                    // 斜面高光提亮：按钮档常态 0.6 → 悬停 1（烤图时就按悬停强度烤）；卡片档常态已是 1，被 Clamp01 截住、不变。
                    bevelFactor = restBevelAlpha > 0f ? 1f / restBevelAlpha : 1f;
                    return;
                default:
                    scale = 1f;
                    shadowFactor = 1f;
                    bevelFactor = 1f;
                    return;
            }
        }

        private void Apply(float scale, float shadowFactor, float bevelFactor)
        {
            if (scales)
            {
                transform.localScale = baseScale * scale;
            }
            if (shadow != null)
            {
                Color c = shadow.color;
                c.a = Mathf.Clamp01(restShadowAlpha * shadowFactor);
                shadow.color = c;
            }
            if (bevel != null)
            {
                Color c = bevel.color;
                c.a = Mathf.Clamp01(restBevelAlpha * bevelFactor);
                bevel.color = c;
            }
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused())
            {
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            float eased = BossRushUI.EaseOut(t);
            Apply(Mathf.Lerp(fromScale, toScale, eased),
                Mathf.Lerp(fromShadow, toShadow, eased),
                Mathf.Lerp(fromBevel, toBevel, eased));
            if (t >= 1f)
            {
                enabled = false;
            }
        }
    }
    /// <summary>
    /// 共享 UI 小件：关闭淡出、次级按钮样式、HUD 文字描边材质。
    /// 放在这里而不是 BossRushUI.cs：那边有行数预算（AGENTS §4.15），且不是 partial。
    /// </summary>
    internal static class BossRushUIKit
    {
        /// <summary>关闭淡出的默认时长。打开是 0.18 秒 SmoothStep，关闭更快一点：没人在看东西离开。</summary>
        internal const float CloseSeconds = 0.12f;

        /// <summary>
        /// 模态 / 面板关闭：立刻停止接收点击，<see cref="CloseSeconds"/> 秒内淡出后销毁。
        /// 之前全 Mod 的关闭都是一帧 Destroy，打开有淡入、关闭没有淡出，前后不对称（2026-09-23 审美审查 UB-32）。
        ///
        /// 【输入不能等动画】调用方在调这里**之前**就该释放输入租约、把自己的引用置空；
        /// 这里只负责「看起来」怎么走，不影响任何状态。重开同一界面会新建根物体，不复用正在淡出的这个。
        /// </summary>
        internal static void PlayCloseAndDestroy(GameObject root, float seconds = CloseSeconds)
        {
            if (root == null)
            {
                return;
            }
            if (!root.activeInHierarchy || seconds <= 0f)
            {
                Object.Destroy(root);
                return;
            }
            BossRushUICloseAnimation close = root.GetComponent<BossRushUICloseAnimation>();
            if (close == null)
            {
                close = root.AddComponent<BossRushUICloseAnimation>();
            }
            close.Begin(seconds);
        }

        /// <summary>
        /// 次级按钮：深色底（SurfaceRaised）+ 一圈 Stroke 描边，标签自动走浅字。
        /// 全 Mod 的按钮口径（2026-09-23）：主操作 Accent，危险操作 Danger，其余一律次级；
        /// Success 只表示「已完成 / 已达成」这类状态，不当主按钮底色。
        /// 幂等：已有描边子物体就不再加一圈。
        /// </summary>
        internal static void StyleSecondaryButton(Button button)
        {
            if (button == null)
            {
                return;
            }
            ZombieModeUIHelper.SetButtonBaseColor(button, BossRushUIColors.SurfaceRaised);
            Image image = button.targetGraphic as Image;
            if (image != null && image.transform.Find("Stroke") == null)
            {
                int radius = image.sprite != null && image.sprite.border.x >= 1f ? Mathf.RoundToInt(image.sprite.border.x) : 8;
                BossRushUI.ApplyPanelStroke(image, radius, BossRushUISkinPart.Button, BossRushUIColors.Stroke);
            }
        }

        private static Sprite vignetteSprite;

        /// <summary>遮罩淡入时长：比面板的 0.16 秒淡入略快，面板长出来时背景已经压暗。</summary>
        private const float BackdropFadeSeconds = 0.15f;

        /// <summary>
        /// 全屏遮罩的「暗角」与淡入（2026-09-23 审美审查 UD-04 / UD-10）。
        /// 旧遮罩是一层均匀灰纱、而且第一帧就是满 alpha：面板还在淡入，背后先「啪」地整片压黑。
        /// 现在遮罩中心取 token alpha 的 0.74、四角取满值，把视线往中间收；0.15 秒淡入。
        /// 颜色仍由调用方的 token 决定（Backdrop / BackdropStrong），这里只给形状与节奏。
        /// </summary>
        internal static void StyleBackdrop(Image backdrop)
        {
            if (backdrop == null)
            {
                return;
            }
            backdrop.sprite = GetVignetteSprite();
            backdrop.type = Image.Type.Simple;
            // 只在「屏幕上本来没有遮罩」时淡入。整页重建（旧根 Destroy、新根同帧建好）和模态叠模态时，
            // 屏幕早已压暗，再从 0 淡入就是背景闪一下（2026-09-23 天空岛修复代理实测回报）。
            bool alreadyDimmed = BossRushBackdropMarker.Alive > 0
                || Time.unscaledTime - BossRushBackdropMarker.LastHiddenTime < BackdropRebuildWindow;
            if (backdrop.GetComponent<BossRushBackdropMarker>() == null)
            {
                backdrop.gameObject.AddComponent<BossRushBackdropMarker>();
            }
            if (!alreadyDimmed)
            {
                BossRushUIEntranceAnimation.Play(backdrop.gameObject, 0f, BackdropFadeSeconds, 0f);
            }
        }

        /// <summary>上一张遮罩消失后这么久之内新建的遮罩视为「重建」，不淡入（秒）。</summary>
        private const float BackdropRebuildWindow = 0.25f;

        private static Sprite GetVignetteSprite()
        {
            if (vignetteSprite != null)
            {
                return vignetteSprite;
            }
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.name = "BossRushUI_Vignette";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;
            float maxDistance = Mathf.Sqrt(2f) * half;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / maxDistance;
                    float alpha = Mathf.Lerp(0.74f, 1f, BossRushUI.SmoothStep(r));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            vignetteSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            vignetteSprite.name = "BossRushUI_Vignette";
            vignetteSprite.hideFlags = HideFlags.HideAndDontSave;
            return vignetteSprite;
        }

        private static readonly Dictionary<int, Material> outlinedMaterials = new Dictionary<int, Material>();

        /// <summary>
        /// 压在游戏世界上的 HUD 字（雷达距离、头顶提示一类）的描边材质：TMP 距离场自带的 Outline + Underlay，
        /// 按字体缓存一份共享材质，所有文本 <c>fontSharedMaterial</c> 指向它，不按文本各开一份。
        ///
        /// 【为什么不用 UnityEngine.UI.Outline】那是 BaseMeshEffect，改的是 uGUI 的 VertexHelper；
        /// TextMeshProUGUI 自己 SetMesh，不走 IMeshModifier，挂上去什么都不发生（审查 UB-24）。
        /// </summary>
        internal static Material GetOutlinedFontMaterial(TMPro.TMP_FontAsset font)
        {
            if (font == null || font.material == null)
            {
                return null;
            }
            int key = font.GetInstanceID();
            Material cached;
            if (outlinedMaterials.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }
            Material material = new Material(font.material);
            material.name = font.material.name + " (BossRush Outline)";
            material.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                material.EnableKeyword("OUTLINE_ON");
                material.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, 0.18f);
                material.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.85f));
                material.EnableKeyword("UNDERLAY_ON");
                material.SetColor(TMPro.ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.55f));
                material.SetFloat(TMPro.ShaderUtilities.ID_UnderlayOffsetX, 0.6f);
                material.SetFloat(TMPro.ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
                material.SetFloat(TMPro.ShaderUtilities.ID_UnderlaySoftness, 0.35f);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRushUIKit] [WARNING] 描边材质参数写入失败: " + e.Message);
            }
            outlinedMaterials[key] = material;
            return material;
        }

        /// <summary>给一段 HUD 字或世界空间 3D 字套上共享描边材质（字体取不到时保持原样）。</summary>
        internal static void ApplyWorldTextOutline(TMPro.TMP_Text text)
        {
            if (text == null)
            {
                return;
            }
            Material material = GetOutlinedFontMaterial(text.font);
            if (material != null)
            {
                text.fontSharedMaterial = material;
            }
        }

        internal static void ResetStaticCaches()
        {
            if (vignetteSprite != null)
            {
                Texture2D vignetteTexture = vignetteSprite.texture;
                Object.Destroy(vignetteSprite);
                if (vignetteTexture != null)
                {
                    Object.Destroy(vignetteTexture);
                }
                vignetteSprite = null;
            }
            foreach (KeyValuePair<int, Material> pair in outlinedMaterials)
            {
                if (pair.Value != null)
                {
                    Object.Destroy(pair.Value);
                }
            }
            outlinedMaterials.Clear();
            BossRushBackdropMarker.Alive = 0;
            BossRushBackdropMarker.LastHiddenTime = -10f;
        }
    }

    /// <summary>
    /// 关闭淡出：CanvasGroup alpha 按 <see cref="BossRushUI.SmoothStep"/> 从当前值落到 0，然后销毁根物体。
    /// 开始时就关掉 blocksRaycasts / interactable，淡出中的面板不再吃点击。
    /// 走 unscaled 时间（模态会把 timeScale 置 0），暂停菜单开着时停推进。
    /// </summary>
    internal sealed class BossRushUICloseAnimation : MonoBehaviour
    {
        private CanvasGroup canvasGroup;
        private float from = 1f;
        private float elapsed;
        private float duration = BossRushUIKit.CloseSeconds;

        internal void Begin(float seconds)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            // 正在播打开动画时接着关：别让打开动画把 alpha 又拉回去。
            BossRushUIOpenAnimation open = GetComponent<BossRushUIOpenAnimation>();
            if (open != null)
            {
                open.enabled = false;
            }
            from = canvasGroup.alpha;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            duration = Mathf.Max(0.01f, seconds);
            elapsed = 0f;
            enabled = true;
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused())
            {
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(from, 0f, BossRushUI.SmoothStep(t));
            }
            if (t >= 1f)
            {
                enabled = false;
                Destroy(gameObject);
            }
        }
    }
    /// <summary>
    /// 让面板描边始终画在最上面（2026-09-23 审美审查 UD-01）。
    /// 描边在 ApplyPanelStroke 时挂上，那时面板还是空的；之后调用方再加的全宽标题栏、页脚、底图
    /// 都排在它后面、画在它上面，面板顶边 / 底边的框线被整段盖掉（图鉴、成就页四角还露出直角）。
    /// 子物体列表一变（增删、重排）就把描边挪回最后一个——只在层级变化时触发，常态零开销。
    /// 投影与斜面不跟着挪：它们是「面」的一部分，本来就该在内容下面。
    /// </summary>
    internal sealed class BossRushStrokeOnTop : MonoBehaviour
    {
        private Transform stroke;

        internal static void Track(Image surface, Image strokeImage)
        {
            if (surface == null || strokeImage == null)
            {
                return;
            }
            BossRushStrokeOnTop keeper = surface.GetComponent<BossRushStrokeOnTop>();
            if (keeper == null)
            {
                keeper = surface.gameObject.AddComponent<BossRushStrokeOnTop>();
            }
            keeper.stroke = strokeImage.transform;
            keeper.Raise();
        }

        private void OnTransformChildrenChanged()
        {
            Raise();
        }

        private void Raise()
        {
            // 已经在最后就什么都不做：SetAsLastSibling 本身也会触发一次 OnTransformChildrenChanged，靠这条判断收敛。
            if (stroke == null || stroke.parent != transform || stroke.GetSiblingIndex() == transform.childCount - 1)
            {
                return;
            }
            stroke.SetAsLastSibling();
        }
    }
    /// <summary>
    /// 标记「这是一张已套暗角的全屏遮罩」，数着屏幕上还活着几张，供 <see cref="BossRushUIKit.StyleBackdrop"/>
    /// 判断新遮罩是整页重建 / 模态叠模态（不淡入）还是真的从无到有（淡入）。只有 OnEnable / OnDisable，没有 Update。
    /// </summary>
    internal sealed class BossRushBackdropMarker : MonoBehaviour
    {
        internal static int Alive;
        internal static float LastHiddenTime = -10f;

        private void OnEnable()
        {
            Alive++;
        }

        private void OnDisable()
        {
            Alive = Mathf.Max(0, Alive - 1);
            LastHiddenTime = Time.unscaledTime;
        }
    }
}
