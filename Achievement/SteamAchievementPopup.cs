// ============================================================================
// SteamAchievementPopup.cs - Steam风格成就弹窗
// ============================================================================
// 模块说明：
//   精确复刻Steam成就解锁弹窗的视觉效果和动画
//   包含图标方形边框、从下方滑入动画、多弹窗堆叠管理
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// Steam风格成就弹窗 - 支持多弹窗堆叠，从下方滑入
    /// </summary>
    public class SteamAchievementPopup : MonoBehaviour
    {
        #region 常量定义

        // ========== 尺寸常量 ==========
        private const float POPUP_WIDTH = 400f;
        // 108：标题 18 / 说明 15 号（旧 16 / 13 号在 1080p 下偏小，审美审查 UD-38），单行框高按 字号×1.45+4 给足
        private const float POPUP_HEIGHT = 108f;
        private const float ICON_SIZE = 56f;
        private const float FRAME_SIZE = 64f;
        private const float FRAME_BORDER = 4f;
        private const float GLOW_SIZE = 140f;
        private const float PADDING_LEFT = 12f;
        private const float ICON_TEXT_GAP = 14f;
        private const float POPUP_SPACING = 8f;
        private const float MARGIN_RIGHT = 10f;
        private const float MARGIN_BOTTOM = 10f;

        // ========== 颜色常量 ==========
        private static readonly Color FRAME_COLOR_STANDARD = BossRushUIColors.Stroke;           // 描边色（旧 Divider α0.32 几乎看不见）
        private static readonly Color FRAME_COLOR_RARE = BossRushUIColors.RarityLegendary;     // 稀有：传说金
        private static readonly Color TITLE_COLOR = BossRushUIColors.TextPrimary;              // 统一主文本色
        private static readonly Color DESC_COLOR = BossRushUIColors.TextSecondary;             // 统一次文本色
        private static readonly Color BG_COLOR = BossRushUIColors.Surface;                     // 统一面板底色
        private static readonly Color INNER_BG_COLOR = BossRushUIColors.SurfaceRaised;         // 统一抬升面色

        // ========== 动画常量 ==========
        private const float SLIDE_IN_DURATION = 0.5f;
        private const float DISPLAY_DURATION = 3.0f;
        /// <summary>退场：原地淡出 + 向右滑开（线性位移、SmoothStep 透明度）。旧版往下滑出屏幕，会从下面几条弹窗身上穿过去。</summary>
        private const float SLIDE_OUT_DURATION = 0.35f;
        private const float SLIDE_OUT_DISTANCE = 40f;
        private const float STACK_MOVE_DURATION = 0.25f;
        private const float GLOW_PULSE_SPEED = 3.0f;

        #endregion

        #region 私有字段

        private static SteamAchievementPopup instance;
        private static Canvas sharedCanvas;
        private static List<PopupInstance> activePopups = new List<PopupInstance>();
        private static List<PopupInstance> toRemoveBuffer = new List<PopupInstance>(); // 复用缓冲区避免每帧分配
        private static Texture2D glowTexture;

        // 音效反射缓存
        private static System.Reflection.MethodInfo cachedPostCustomSFXMethod;
        private static bool audioReflectionCached;

        #endregion

        #region 内部类

        /// <summary>
        /// 单个弹窗实例的数据
        /// </summary>
        private class PopupInstance
        {
            public GameObject panelObj;
            public RectTransform panelRect;
            public Image frameImage;      // 图标框的描边环（用于稀有成就的脉冲动画）
            public Image glowImage;       // 光效图片（稀有成就）
            public CanvasGroup group;     // 退场淡出
            public float targetY;
            public float currentY;
            // 纵向位移统一成「fromY → targetY 的一段补间」：滑入、给新弹窗让位都走它。
            // 旧版让位是每帧 currentY += diff * min(1, dt/0.25*8)，速度随帧率变（UD-38）。
            public float fromY;
            public float moveElapsed;
            public float moveDuration;
            public float offsetX;
            public float displayTimer;
            public bool isSlideIn;
            public bool isSlideOut;
            public float slideProgress;

            public void MoveTo(float y, float duration)
            {
                fromY = currentY;
                targetY = y;
                moveElapsed = 0f;
                moveDuration = Mathf.Max(0.01f, duration);
            }
        }

        #endregion

        #region 生命周期

        void Awake()
        {
            instance = this;
            CreateSharedCanvas();
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                foreach (var popup in activePopups)
                    if (popup.glowImage != null && popup.glowImage.sprite != null) Destroy(popup.glowImage.sprite);
                if (glowTexture != null && !ProductionIconCache.IsBorrowed(glowTexture)) Destroy(glowTexture);
                glowTexture = null;
                instance = null;
                sharedCanvas = null;
                activePopups.Clear();
                toRemoveBuffer.Clear();
            }
        }

        void Update()
        {
            if (activePopups.Count == 0)
            {
                return;
            }

            UpdateAllPopups();
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 显示成就弹窗
        /// </summary>
        public static void Show(BossRushAchievementDef achievement)
        {
            if (achievement == null || instance == null) return;
            instance.CreateAndShowPopup(achievement);
        }

        /// <summary>
        /// 显示成就弹窗（简化版，用于测试）
        /// </summary>
        public static void Show(string title, string description, Texture2D icon = null)
        {
            var temp = new BossRushAchievementDef("temp", title, title, description, description, AchievementCategory.Basic, 0);
            Show(temp);
        }

        /// <summary>
        /// 确保弹窗实例存在
        /// </summary>
        public static void EnsureInstance()
        {
            if (instance == null)
            {
                GameObject obj = new GameObject("SteamAchievementPopup");
                instance = obj.AddComponent<SteamAchievementPopup>();
                DontDestroyOnLoad(obj);
            }
        }

        internal static void Shutdown()
        {
            if (instance == null) return;
            instance.gameObject.SetActive(false);
            Destroy(instance.gameObject);
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 创建共享Canvas
        /// </summary>
        private void CreateSharedCanvas()
        {
            if (sharedCanvas != null) return;

            GameObject canvasObj = new GameObject("AchievementPopupCanvas");
            canvasObj.transform.SetParent(transform);

            sharedCanvas = canvasObj.AddComponent<Canvas>();
            sharedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            sharedCanvas.sortingOrder = BossRushUILayers.Toast;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);

            canvasObj.AddComponent<GraphicRaycaster>();

            // 加载稀有成就光效纹理
            if (glowTexture == null)
            {
                glowTexture = LoadTexture("steam_rare_glow.png");
            }
        }

        #endregion

        #region 弹窗创建

        /// <summary>
        /// 创建并显示新弹窗
        /// </summary>
        private void CreateAndShowPopup(BossRushAchievementDef achievement)
        {
            PlayUnlockSound();

            // 将现有弹窗上移（0.25 秒 EaseOut 补间；还在滑入的那条改成让位补间，从当前位置接着走）
            foreach (var p in activePopups)
            {
                p.isSlideIn = false;
                p.MoveTo(p.targetY + POPUP_HEIGHT + POPUP_SPACING, STACK_MOVE_DURATION);
            }

            // 判断是否为稀有成就
            bool isRare = achievement.difficultyRating >= 4 || achievement.category == AchievementCategory.Ultimate;

            PopupInstance newPopup = CreatePopupPanel(achievement, isRare);
            newPopup.currentY = -POPUP_HEIGHT - MARGIN_BOTTOM;
            newPopup.MoveTo(MARGIN_BOTTOM, SLIDE_IN_DURATION);
            newPopup.isSlideIn = true;
            newPopup.slideProgress = 0f;
            newPopup.displayTimer = 0f;
            newPopup.panelRect.anchoredPosition = new Vector2(-MARGIN_RIGHT, newPopup.currentY);

            activePopups.Insert(0, newPopup);

            // 启动光效动画（仅稀有成就）
            if (isRare)
            {
                StartCoroutine(GlowPulseAnimation(newPopup));
            }
        }

        /// <summary>
        /// 创建弹窗面板
        /// </summary>
        private PopupInstance CreatePopupPanel(BossRushAchievementDef achievement, bool isRare)
        {
            PopupInstance popup = new PopupInstance();

            // 创建面板容器
            popup.panelObj = new GameObject("PopupPanel");
            // worldPositionStays=false：画布根按 CanvasScaler 缩放，保留世界缩放会让弹窗在 1440p / 4K 下按 1/scaleFactor 缩小
            popup.panelObj.transform.SetParent(sharedCanvas.transform, false);

            popup.panelRect = popup.panelObj.AddComponent<RectTransform>();
            popup.panelRect.anchorMin = new Vector2(1, 0);
            popup.panelRect.anchorMax = new Vector2(1, 0);
            popup.panelRect.pivot = new Vector2(1, 0);
            popup.panelRect.sizeDelta = new Vector2(POPUP_WIDTH, POPUP_HEIGHT);
            popup.group = popup.panelObj.AddComponent<CanvasGroup>();
            popup.group.blocksRaycasts = false;   // 提示不吃点击

            // 背景：圆角卡片 + 描边（共享层顺带给外投影与顶边高光）。旧版是没有 sprite 的直角深色板、没有边（UD-38）
            Image panelImage = popup.panelObj.AddComponent<Image>();
            panelImage.color = BG_COLOR;
            BossRushUI.ApplyFramedPanelSkin(panelImage, 12, BossRushUISkinPart.Card);
            panelImage.raycastTarget = false;

            // 图标先取：取不到就不画图标框，文字贴左（不留一块灰方块占位）
            Texture2D iconTex = LoadAchievementIcon(achievement.iconFile);
            bool hasIcon = iconTex != null;

            // 稀有成就光效（在边框后面）
            if (hasIcon && isRare && glowTexture != null)
            {
                CreateGlowEffect(popup);
            }

            // 创建边框和图标
            if (hasIcon)
            {
                CreateFrameAndIcon(popup, iconTex, isRare);
            }

            // 创建文字
            CreateTexts(popup, achievement, hasIcon);

            return popup;
        }

        /// <summary>
        /// 创建稀有成就光效
        /// </summary>
        private void CreateGlowEffect(PopupInstance popup)
        {
            GameObject glowObj = new GameObject("GlowOverlay");
            glowObj.transform.SetParent(popup.panelObj.transform, false);
            
            RectTransform glowRect = glowObj.AddComponent<RectTransform>();
            glowRect.anchorMin = new Vector2(0, 0.5f);
            glowRect.anchorMax = new Vector2(0, 0.5f);
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.anchoredPosition = new Vector2(PADDING_LEFT + ICON_SIZE / 2f, 0);
            glowRect.sizeDelta = new Vector2(GLOW_SIZE, GLOW_SIZE);

            popup.glowImage = glowObj.AddComponent<Image>();
            popup.glowImage.sprite = Sprite.Create(glowTexture, new Rect(0, 0, glowTexture.width, glowTexture.height), new Vector2(0.5f, 0.5f));
            popup.glowImage.color = new Color(1f, 0.8f, 0f, 0f);
        }

        /// <summary>
        /// 创建边框和图标
        /// </summary>
        private void CreateFrameAndIcon(PopupInstance popup, Texture2D iconTex, bool isRare)
        {
            // 边框容器
            GameObject frameObj = new GameObject("Frame");
            frameObj.transform.SetParent(popup.panelObj.transform, false);
            
            RectTransform frameRect = frameObj.AddComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0, 0.5f);
            frameRect.anchorMax = new Vector2(0, 0.5f);
            frameRect.pivot = new Vector2(0, 0.5f);
            frameRect.anchoredPosition = new Vector2(PADDING_LEFT - FRAME_BORDER, 0);
            frameRect.sizeDelta = new Vector2(FRAME_SIZE, FRAME_SIZE);

            // 图标框：圆角凹槽 + 描边环（普通 Stroke、稀有传说金）。旧版是「整块色当边 + 直角内底」拼的方框，
            // 普通档的 Divider(α0.32) 边几乎看不见（UD-38）。脉冲动画改的是描边环的颜色。
            Image wellImage = frameObj.AddComponent<Image>();
            wellImage.color = INNER_BG_COLOR;
            BossRushUI.ApplyPanelSkin(wellImage, 8, BossRushUISkinPart.Card);
            wellImage.raycastTarget = false;
            popup.frameImage = BossRushUI.ApplyPanelStroke(
                wellImage, 8, BossRushUISkinPart.Card, isRare ? FRAME_COLOR_RARE : FRAME_COLOR_STANDARD);

            // 图标（内缩 FRAME_BORDER，四角落在圆角里面）
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(frameObj.transform, false);

            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(FRAME_BORDER, FRAME_BORDER);
            iconRect.offsetMax = new Vector2(-FRAME_BORDER, -FRAME_BORDER);

            RawImage iconImage = iconObj.AddComponent<RawImage>();
            iconImage.texture = iconTex;
            iconImage.color = Color.white;
            iconImage.raycastTarget = false;
        }

        /// <summary>
        /// 创建标题和描述文字
        /// </summary>
        private void CreateTexts(PopupInstance popup, BossRushAchievementDef achievement, bool hasIcon)
        {
            float textStartX = hasIcon ? PADDING_LEFT + FRAME_SIZE + ICON_TEXT_GAP : PADDING_LEFT + 6f;
            bool isChinese = IsChinese();

            // 标题
            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(popup.panelObj.transform, false);
            
            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.5f);
            titleRect.anchorMax = new Vector2(0, 0.5f);
            titleRect.pivot = new Vector2(0, 0.5f);
            titleRect.anchoredPosition = new Vector2(textStartX, 26f);
            titleRect.sizeDelta = new Vector2(POPUP_WIDTH - textStartX - PADDING_LEFT, 30f);

            TMPro.TextMeshProUGUI titleText = titleObj.AddComponent<TMPro.TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(titleText);
            titleText.fontSize = 18;
            titleText.fontStyle = TMPro.FontStyles.Bold;
            titleText.color = TITLE_COLOR;
            titleText.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            titleText.enableWordWrapping = false;
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 16f;
            titleText.fontSizeMax = 18f;
            titleText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            titleText.raycastTarget = false;
            titleText.text = isChinese ? achievement.nameCN : achievement.nameEN;

            // 描述
            GameObject descObj = new GameObject("DescriptionText");
            descObj.transform.SetParent(popup.panelObj.transform, false);
            
            RectTransform descRect = descObj.AddComponent<RectTransform>();
            descRect.anchorMin = new Vector2(0, 0.5f);
            descRect.anchorMax = new Vector2(0, 0.5f);
            descRect.pivot = new Vector2(0, 0.5f);
            descRect.anchoredPosition = new Vector2(textStartX, -14f);
            descRect.sizeDelta = new Vector2(POPUP_WIDTH - textStartX - PADDING_LEFT, 48f);

            TMPro.TextMeshProUGUI descText = descObj.AddComponent<TMPro.TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(descText);
            descText.fontSize = 15;
            descText.color = DESC_COLOR;
            descText.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            descText.enableWordWrapping = true;
            descText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            descText.raycastTarget = false;
            descText.text = isChinese ? achievement.descCN : achievement.descEN;
        }

        #endregion

        #region 动画更新

        /// <summary>
        /// 更新所有弹窗状态
        /// </summary>
        private void UpdateAllPopups()
        {
            // 暂停菜单盖在上面时停推进：走 unscaled 时间，不停下来的话玩家回来弹窗已经播完了
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            toRemoveBuffer.Clear(); // 复用缓冲区

            foreach (var popup in activePopups)
            {
                // 纵向位移：滑入（EaseOut 叠两次，起手更快、落定更稳）或让位（EaseOut），都是与帧率无关的补间
                if (popup.moveElapsed < popup.moveDuration)
                {
                    popup.moveElapsed += dt;
                    float t = Mathf.Clamp01(popup.moveElapsed / popup.moveDuration);
                    float eased = popup.isSlideIn ? BossRushUI.EaseOut(BossRushUI.EaseOut(t)) : BossRushUI.EaseOut(t);
                    popup.currentY = Mathf.Lerp(popup.fromY, popup.targetY, eased);
                    if (t >= 1f)
                    {
                        popup.currentY = popup.targetY;
                        popup.isSlideIn = false;
                    }
                }

                if (popup.isSlideOut)
                {
                    // 退场：原地淡出 + 向右滑开
                    popup.slideProgress += dt / SLIDE_OUT_DURATION;
                    if (popup.slideProgress >= 1f)
                    {
                        toRemoveBuffer.Add(popup);
                        continue;
                    }
                    if (popup.group != null)
                    {
                        popup.group.alpha = 1f - BossRushUI.SmoothStep(popup.slideProgress);
                    }
                    popup.offsetX = SLIDE_OUT_DISTANCE * popup.slideProgress;
                }
                else if (!popup.isSlideIn)
                {
                    popup.displayTimer += dt;
                    if (popup.displayTimer >= DISPLAY_DURATION)
                    {
                        popup.isSlideOut = true;
                        popup.slideProgress = 0f;
                    }
                }

                popup.panelRect.anchoredPosition = new Vector2(-MARGIN_RIGHT + popup.offsetX, popup.currentY);
            }

            // 移除已完成的弹窗
            foreach (var popup in toRemoveBuffer)
            {
                activePopups.Remove(popup);
                if (popup.glowImage != null && popup.glowImage.sprite != null) Destroy(popup.glowImage.sprite);
                Destroy(popup.panelObj);
            }
        }

        /// <summary>
        /// 稀有成就光效脉冲动画
        /// </summary>
        private IEnumerator GlowPulseAnimation(PopupInstance popup)
        {
            float time = 0f;
            while (popup != null && popup.panelObj != null && !popup.isSlideOut)
            {
                if (BossRushUI.IsGamePaused())
                {
                    yield return null;
                    continue;
                }
                time += Time.unscaledDeltaTime * GLOW_PULSE_SPEED;
                float pulse = (Mathf.Sin(time * Mathf.PI) + 1f) * 0.5f;

                // 光效透明度动画
                if (popup.glowImage != null)
                {
                    Color c = popup.glowImage.color;
                    c.a = Mathf.Lerp(0.3f, 0.7f, pulse);
                    popup.glowImage.color = c;
                    popup.glowImage.rectTransform.Rotate(Vector3.forward, Time.unscaledDeltaTime * 10f);
                }

                // 边框亮度动画
                if (popup.frameImage != null)
                {
                    float brightness = Mathf.Lerp(0.85f, 1.0f, pulse);
                    popup.frameImage.color = new Color(
                        FRAME_COLOR_RARE.r * brightness,
                        FRAME_COLOR_RARE.g * brightness,
                        FRAME_COLOR_RARE.b * brightness,
                        1f
                    );
                }

                yield return null;
            }
        }

        #endregion

        #region 资源加载

        /// <summary>
        /// 加载UI纹理（用于光效等非成就图标资源）
        /// </summary>
        private Texture2D LoadTexture(string filename)
        {
            Sprite compressed = ProductionIconCache.Get("Assets/Textures/UI/" + filename);
            if (compressed != null) return compressed.texture;
            if (!ProductionIconCache.AllowRawFallback) return null;
            Texture2D texture = null;
            bool retained = false;
            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return null;

                string path = System.IO.Path.Combine(modPath, "Assets", "Textures", "UI", filename);
                if (System.IO.File.Exists(path))
                {
                    byte[] fileData = System.IO.File.ReadAllBytes(path);
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(fileData, true))
                    {
                        retained = true;
                        return texture;
                    }
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.LogError("[Achievement] 加载纹理失败 " + filename + ": " + e.Message);
            }
            finally
            {
                if (!retained && texture != null) Destroy(texture);
            }
            return null;
        }

        /// <summary>
        /// 加载成就图标（使用共享的 AchievementIconLoader）
        /// </summary>
        private Texture2D LoadAchievementIcon(string iconFile)
        {
            string iconName = !string.IsNullOrEmpty(iconFile) ? System.IO.Path.GetFileNameWithoutExtension(iconFile) : null;
            
            // 尝试加载指定图标
            Texture2D tex = AchievementIconLoader.GetTexture(iconName);
            
            // 回退到 first_clear 图标
            if (tex == null && iconName != "first_clear")
            {
                tex = AchievementIconLoader.GetTexture("first_clear");
            }
            
            return tex;
        }

        /// <summary>
        /// 清除图标缓存（供外部调用）
        /// </summary>
        public static void ClearIconCache()
        {
            AchievementIconLoader.ClearCache();
        }

        #endregion

        #region 音效

        /// <summary>
        /// 缓存音频管理器反射
        /// </summary>
        private static void CacheAudioManagerReflection()
        {
            if (audioReflectionCached) return;
            try
            {
                var type = System.Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                if (type != null)
                {
                    cachedPostCustomSFXMethod = type.GetMethod("PostCustomSFX",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                }
            }
            catch { }
            audioReflectionCached = true;
        }

        /// <summary>
        /// 播放解锁音效
        /// </summary>
        private void PlayUnlockSound()
        {
            try
            {
                if (!audioReflectionCached) CacheAudioManagerReflection();
                if (cachedPostCustomSFXMethod == null) return;

                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return;

                string soundPath = System.IO.Path.Combine(modPath, "Assets", "Sounds", "Achievement", "default.wav");
                if (!System.IO.File.Exists(soundPath))
                {
                    soundPath = System.IO.Path.Combine(modPath, "Assets", "Sounds", "Achievement", "default.mp3");
                }
                if (!System.IO.File.Exists(soundPath)) return;

                cachedPostCustomSFXMethod.Invoke(null, new object[] { soundPath, null, false });
            }
            catch { }
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 判断当前语言是否为中文
        /// </summary>
        private static bool IsChinese()
        {
            try
            {
                var lang = SodaCraft.Localizations.LocalizationManager.CurrentLanguage;
                return lang == SystemLanguage.ChineseSimplified ||
                       lang == SystemLanguage.ChineseTraditional ||
                       lang == SystemLanguage.Chinese;
            }
            catch { }
            return true;
        }

        #endregion
    }
}
