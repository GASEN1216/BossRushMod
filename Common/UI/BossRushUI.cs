// ============================================================================
// BossRushUI.cs - 全 Mod 共享 UI 基础库
// ============================================================================
// 模块说明：
//   收口全 Mod 的 UI 视觉语言：设计 token、Canvas 层级表、圆角九宫格底图，
//   以及模态窗口/卡片/滚动列表等重复了多份的构件。
//
//   字体获取与模态输入租约仍然委托 ZombieModeUIHelper——那两块已经是全 Mod
//   事实标准（ModeF/ModeG/ZombieMode 都在用），且被多个 guard 逐行钉死，
//   不重复实现，也不搬动。
//
//   皮肤两步走：当前用运行时程序化生成的圆角九宫格 Sprite；将来出了美术图集，
//   通过 BossRushUISkin.InjectPanelSprite 等注入点替换即可，调用方无需改动。
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// Canvas 层级表。此前全 Mod 的 sortingOrder 分成 10~1001 与 28000~32000
    /// 两个互不相干的孤岛，跨模式叠加时谁压谁是碰运气；统一到这里。
    /// </summary>
    internal static class BossRushUILayers
    {
        /// <summary>世界空间标记、常驻角标等最底层装饰。</summary>
        internal const int WorldOverlay = 100;
        /// <summary>ModeF 赏金雷达画布，压住世界装饰、让位各模式 HUD。</summary>
        internal const int ModeFBountyRadar = 240;
        /// <summary>ModeG 常驻 HUD。</summary>
        internal const int ModeGHud = 900;
        /// <summary>ModeG 战后 Recap，压住自家 HUD、让位入口确认页。</summary>
        internal const int ModeGRecap = 940;
        /// <summary>ModeG 入口确认页，ModeG 自己这组里最高。</summary>
        internal const int ModeGEntry = 950;
        /// <summary>常驻 HUD（不接收点击）。</summary>
        internal const int Hud = 1000;
        /// <summary>雷达、状态角标一类的 HUD 附属层。</summary>
        internal const int HudOverlay = 1200;
        /// <summary>普通功能面板（成就、Boss 池设置等）。</summary>
        internal const int Panel = 2000;
        /// <summary>需要抢焦点的模态窗口。</summary>
        internal const int Modal = 3000;
        /// <summary>模态之上的确认框。</summary>
        internal const int ModalConfirm = 3200;
        /// <summary>成就解锁一类的瞬时弹窗。</summary>
        internal const int Toast = 4000;
        /// <summary>过场视频，压住一切。</summary>
        internal const int Cutscene = 5000;

        // ── 独立模式层 ─────────────────────────────────────────────
        // ZombieMode 是接管整局的独立模式，它的 HUD 与模态必须压住上面那些
        // 环境级浮层（许愿池动画、成就弹窗、图片查看器），因此整体抬到 28000+。
        // 这不是历史遗留的“第二个孤岛”，而是有意的分层；数值沿用既有实现，
        // 收进常量表只为消除魔法数，不改变任何叠放次序。

        /// <summary>ZombieMode 常驻 HUD。</summary>
        internal const int ZombieHud = 28000;
        /// <summary>ZombieMode 主模态（奖励选择、撤离确认、起始配装）。</summary>
        internal const int ZombieModal = 30000;
        /// <summary>ZombieMode 模态之上的输入框（现金投入）。</summary>
        internal const int ZombieModalInput = 30100;
        /// <summary>ZombieMode 临时 NPC 服务面板，压住奖励面板。</summary>
        internal const int ZombieService = 30500;
        /// <summary>婚礼过场视频，全 Mod 最高。</summary>
        internal const int WeddingCutscene = 32000;
    }

    /// <summary>
    /// 设计 token。数值沿用 ZombieModeUIHelper 已经在游戏里跑顺的那套深色调，
    /// 并把散落各处的第二套遮罩色（0,0,0,0.7）统一到这里。
    /// </summary>
    internal static class BossRushUIColors
    {
        internal static readonly Color Backdrop = new Color(0.015f, 0.02f, 0.025f, 0.62f);
        internal static readonly Color Surface = new Color(0.045f, 0.055f, 0.065f, 0.92f);
        internal static readonly Color SurfaceRaised = new Color(0.075f, 0.09f, 0.105f, 0.95f);
        internal static readonly Color Header = new Color(0.09f, 0.115f, 0.13f, 0.96f);
        internal static readonly Color Divider = new Color(0.42f, 0.52f, 0.58f, 0.32f);
        internal static readonly Color TextPrimary = new Color(0.94f, 0.96f, 0.97f, 1f);
        internal static readonly Color TextSecondary = new Color(0.67f, 0.72f, 0.75f, 1f);
        internal static readonly Color Accent = new Color(0.20f, 0.72f, 0.67f, 1f);
        internal static readonly Color Success = new Color(0.18f, 0.52f, 0.36f, 1f);
        internal static readonly Color Warning = new Color(0.58f, 0.42f, 0.17f, 1f);
        internal static readonly Color Danger = new Color(0.48f, 0.20f, 0.20f, 1f);
        internal static readonly Color Disabled = new Color(0.16f, 0.17f, 0.17f, 0.82f);

        /// <summary>稀有度描边，供奖励卡一类需要分级的构件使用。</summary>
        internal static readonly Color RarityCommon = new Color(0.55f, 0.60f, 0.64f, 0.9f);
        internal static readonly Color RarityUncommon = new Color(0.35f, 0.72f, 0.45f, 0.95f);
        internal static readonly Color RarityRare = new Color(0.32f, 0.62f, 0.92f, 0.95f);
        internal static readonly Color RarityEpic = new Color(0.68f, 0.42f, 0.92f, 0.95f);
        internal static readonly Color RarityLegendary = new Color(0.95f, 0.68f, 0.22f, 1f);
    }

    /// <summary>
    /// 皮肤注入点。默认使用程序化生成的圆角九宫格；将来打好 UI 图集
    /// （见 docs/制作教程/BossRushUI_图集规格.md）后在此注入即可全局换皮。
    /// </summary>
    internal static class BossRushUISkin
    {
        private static Sprite injectedPanelSprite;
        private static Sprite injectedButtonSprite;

        /// <summary>注入面板九宫格底图。传 null 恢复程序化默认皮肤。</summary>
        internal static void InjectPanelSprite(Sprite sprite)
        {
            injectedPanelSprite = sprite;
        }

        /// <summary>注入按钮九宫格底图。传 null 恢复程序化默认皮肤。</summary>
        internal static void InjectButtonSprite(Sprite sprite)
        {
            injectedButtonSprite = sprite;
        }

        internal static Sprite GetPanelSprite()
        {
            if (injectedPanelSprite != null)
            {
                return injectedPanelSprite;
            }

            return BossRushUI.GetRoundedSprite(12);
        }

        internal static Sprite GetButtonSprite()
        {
            if (injectedButtonSprite != null)
            {
                return injectedButtonSprite;
            }

            return BossRushUI.GetRoundedSprite(8);
        }
    }

    /// <summary>
    /// 全 Mod 共享的 UI 构件工厂。
    /// </summary>
    internal static class BossRushUI
    {
        // 半径 -> 九宫格 Sprite。全 Mod 共享，不随界面销毁而释放：
        // 这些贴图很小（最大 64x64 的 Alpha8），且下一个界面马上又要用。
        private static readonly Dictionary<int, Sprite> roundedSpriteCache = new Dictionary<int, Sprite>();

        /// <summary>
        /// 释放程序化生成的 Sprite / Texture 与字体缓存。
        /// 这些对象带 HideFlags.DontSave，切场景不会自动回收，必须显式销毁。
        /// </summary>
        public static void ResetStaticCaches()
        {
            foreach (KeyValuePair<int, Sprite> pair in roundedSpriteCache)
            {
                Sprite sprite = pair.Value;
                if (sprite == null)
                {
                    continue;
                }

                Texture2D texture = sprite.texture;
                Object.Destroy(sprite);
                if (texture != null)
                {
                    Object.Destroy(texture);
                }
            }
            roundedSpriteCache.Clear();

            cachedLegacyFont = null;
            legacyFontResolved = false;
        }

        /// <summary>
        /// 取一张指定圆角半径的九宫格 Sprite。程序化生成：贴图边长取
        /// radius*2+2，中间留 2px 可拉伸区，border 设为 radius，
        /// 这样任意尺寸拉伸后圆角都不变形。
        /// </summary>
        internal static Sprite GetRoundedSprite(int radius)
        {
            if (radius < 1)
            {
                radius = 1;
            }
            if (radius > 32)
            {
                radius = 32;
            }

            Sprite cached;
            if (roundedSpriteCache.TryGetValue(radius, out cached) && cached != null)
            {
                return cached;
            }

            Sprite sprite = BuildRoundedSprite(radius);
            roundedSpriteCache[radius] = sprite;
            return sprite;
        }

        private static Sprite BuildRoundedSprite(int radius)
        {
            int size = radius * 2 + 2;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.name = "BossRushUI_Rounded_" + radius;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;

            Color32[] pixels = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 到最近圆心的距离：四角各一个圆心，中间两列/两行是直边。
                    float dx = x < radius ? radius - x : (x > size - 1 - radius ? x - (size - 1 - radius) : 0f);
                    float dy = y < radius ? radius - y : (y > size - 1 - radius ? y - (size - 1 - radius) : 0f);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    // 1px 过渡带做抗锯齿，避免程序化圆角出现台阶。
                    float alpha = Mathf.Clamp01(r - distance + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            sprite.name = "BossRushUI_Rounded_" + radius;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>
        /// 给已创建的 TMP 文本套上游戏字体。
        /// 字体解析走 ZombieModeUIHelper.GetGameFont() 的四级回退（TMP_Settings →
        /// HealthBar 反射 → 场景内任意 TMP_Text → ObjectCache），不要另起一套。
        /// </summary>
        internal static void ApplyGameFont(TMPro.TextMeshProUGUI text)
        {
            if (text == null)
            {
                return;
            }

            TMPro.TMP_FontAsset font = ZombieModeUIHelper.GetGameFont();
            if (font != null)
            {
                text.font = font;
            }
        }

        private static Font cachedLegacyFont;
        private static bool legacyFontResolved;

        /// <summary>
        /// 给仍在用 legacy UI.Text 的界面取一个尽量能显示中文的 Font。
        ///
        /// 优先用游戏 TMP 字体的 sourceFontFile；取不到时回退内置 Arial（与改动前一致，
        /// 不构成回退）。这是调试面板的过渡方案——正解是转成 TMP，
        /// 但 F3 菜单把 Font 贯穿了十来个辅助方法且混用 legacy InputField，
        /// 不在本轮"调试 UI 只统一字体"的范围内。
        /// </summary>
        internal static Font GetLegacyChineseFont()
        {
            if (legacyFontResolved && cachedLegacyFont != null)
            {
                return cachedLegacyFont;
            }

            legacyFontResolved = true;
            try
            {
                TMPro.TMP_FontAsset tmpFont = ZombieModeUIHelper.GetGameFont();
                if (tmpFont != null && tmpFont.sourceFontFile != null)
                {
                    cachedLegacyFont = tmpFont.sourceFontFile;
                    return cachedLegacyFont;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BossRushUI] 解析 legacy 字体失败: " + e.Message);
            }

            cachedLegacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return cachedLegacyFont;
        }

        /// <summary>
        /// 给 Image 套上圆角九宫格底图。颜色由调用方决定，底图只提供形状。
        /// </summary>
        internal static void ApplyPanelSkin(Image image, int radius)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = radius >= 12 ? BossRushUISkin.GetPanelSprite() : BossRushUISkin.GetButtonSprite();
            if (image.sprite == null)
            {
                return;
            }

            image.type = Image.Type.Sliced;
            // 面板通常比九宫格贴图大得多，关掉 fillCenter 之外的自动缩放，
            // 否则小尺寸控件上 Unity 会按 pixelsPerUnit 把边角压扁。
            image.pixelsPerUnitMultiplier = 1f;
        }

        /// <summary>
        /// 创建一个带 Canvas + Scaler + Raycaster 的 UI 根。此前这三件套在
        /// 20 处各写各的，其中 9 处手写 Scaler 参数、1 处忘了配（高分屏直接缩成一小块）。
        /// </summary>
        internal static Canvas CreateCanvasRoot(string name, int sortingOrder, bool interactive)
        {
            GameObject root = new GameObject(name);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);

            GraphicRaycaster raycaster = root.AddComponent<GraphicRaycaster>();
            // HUD 类界面必须让点击穿透过去，否则会挡住游戏内的交互。
            raycaster.enabled = interactive;
            return canvas;
        }

        /// <summary>
        /// 全屏遮罩。统一使用 Backdrop token，替换掉散落各处的 (0,0,0,0.7)。
        /// </summary>
        internal static Image CreateBackdrop(Transform parent)
        {
            GameObject obj = new GameObject("Backdrop");
            obj.transform.SetParent(parent, false);
            Image image = obj.AddComponent<Image>();
            image.color = BossRushUIColors.Backdrop;
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return image;
        }

        /// <summary>
        /// 圆角卡片。左侧 accent 竖条是本 Mod 已有的视觉语言（奖励卡、服务按钮、
        /// 模态标题都在用），这里收成一份。
        /// </summary>
        internal static GameObject CreateCard(
            string name,
            Transform parent,
            Vector2 position,
            Vector2 size,
            Color surfaceColor,
            Color accentColor,
            bool showAccentRail = true)
        {
            GameObject card = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchoredPosition = position;

            Image background = card.AddComponent<Image>();
            background.color = surfaceColor;
            ApplyPanelSkin(background, 10);

            if (showAccentRail)
            {
                GameObject rail = ZombieModeUIHelper.CreateRect(
                    name + "_Accent",
                    card.transform,
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(3f, 0f),
                    new Vector2(4f, -12f),
                    new Vector2(0f, 0.5f));
                Image railImage = rail.AddComponent<Image>();
                railImage.color = accentColor;
                ApplyPanelSkin(railImage, 2);
                railImage.raycastTarget = false;
            }

            return card;
        }

        /// <summary>
        /// 面板打开时的淡入+微放大。
        ///
        /// 不用官方 Duckov.UI.Animations.ScaleFade：它的 duration/scale 全是私有
        /// [SerializeField]，运行时 AddComponent 得到的 HiddenScale 恰好等于正常
        /// 缩放（uniformScale 与 scale 都是 0），动画不可见，而且它要靠 FadeElement
        /// 的 Show/Hide 驱动。与其反射改私有字段，不如用一个自包含组件。
        /// </summary>
        internal static void PlayOpenAnimation(GameObject panel)
        {
            if (panel == null)
            {
                return;
            }

            BossRushUIOpenAnimation animation = panel.GetComponent<BossRushUIOpenAnimation>();
            if (animation == null)
            {
                animation = panel.AddComponent<BossRushUIOpenAnimation>();
            }
            animation.Restart();
        }
    }

    /// <summary>
    /// 面板打开动画：0.12 秒淡入并从 0.96 放大到 1。
    /// 用 unscaledDeltaTime，模态会把 timeScale 置 0。
    /// </summary>
    internal sealed class BossRushUIOpenAnimation : MonoBehaviour
    {
        private const float DurationSeconds = 0.12f;
        private const float StartScale = 0.96f;

        private CanvasGroup canvasGroup;
        private float elapsed;
        private bool playing;

        internal void Restart()
        {
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            elapsed = 0f;
            playing = true;
            canvasGroup.alpha = 0f;
            transform.localScale = Vector3.one * StartScale;
        }

        private void Update()
        {
            if (!playing)
            {
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / DurationSeconds);
            // SmoothStep 收尾，避免线性淡入显得生硬。
            float eased = t * t * (3f - 2f * t);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = eased;
            }
            transform.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, eased);

            if (t >= 1f)
            {
                playing = false;
                transform.localScale = Vector3.one;
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1f;
                }
            }
        }
    }
}
