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
        private const float POPUP_WIDTH = 340f;
        private const float POPUP_HEIGHT = 80f;
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
        private static readonly Color FRAME_COLOR_STANDARD = new Color32(210, 195, 170, 255);  // 米色边框
        private static readonly Color FRAME_COLOR_RARE = new Color32(220, 200, 160, 255);      // 金米色边框（稀有）
        private static readonly Color TITLE_COLOR = new Color32(235, 235, 235, 255);           // 标题白色
        private static readonly Color DESC_COLOR = new Color32(185, 185, 185, 255);            // 描述灰色
        private static readonly Color BG_COLOR = new Color32(22, 25, 29, 245);                 // 背景深灰色
        private static readonly Color INNER_BG_COLOR = new Color32(30, 30, 35, 255);           // 图标内部背景

        // ========== 动画常量 ==========
        private const float SLIDE_IN_DURATION = 0.5f;
        private const float DISPLAY_DURATION = 3.0f;
        private const float SLIDE_OUT_DURATION = 0.5f;
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
            public Image frameImage;      // 边框图片（用于动画）
            public Image glowImage;       // 光效图片（稀有成就）
            public float targetY;
            public float currentY;
            public float displayTimer;
            public bool isSlideIn;
            public bool isSlideOut;
            public float slideProgress;
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
            if (instance == this) instance = null;
        }

        void Update()
        {
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
            sharedCanvas.sortingOrder = 1000;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

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

            // 将现有弹窗上移
            foreach (var p in activePopups)
            {
                p.targetY += POPUP_HEIGHT + POPUP_SPACING;
            }

            // 判断是否为稀有成就
            bool isRare = achievement.difficultyRating >= 4 || achievement.category == AchievementCategory.Ultimate;

            PopupInstance newPopup = CreatePopupPanel(achievement, isRare);
            newPopup.currentY = -POPUP_HEIGHT - MARGIN_BOTTOM;
            newPopup.targetY = MARGIN_BOTTOM;
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
            popup.panelObj.transform.SetParent(sharedCanvas.transform);

            popup.panelRect = popup.panelObj.AddComponent<RectTransform>();
            popup.panelRect.anchorMin = new Vector2(1, 0);
            popup.panelRect.anchorMax = new Vector2(1, 0);
            popup.panelRect.pivot = new Vector2(1, 0);
            popup.panelRect.sizeDelta = new Vector2(POPUP_WIDTH, POPUP_HEIGHT);

            // 背景
            Image panelImage = popup.panelObj.AddComponent<Image>();
            panelImage.color = BG_COLOR;

            // 稀有成就光效（在边框后面）
            if (isRare && glowTexture != null)
            {
                CreateGlowEffect(popup);
            }

            // 创建边框和图标
            CreateFrameAndIcon(popup, achievement, isRare);

            // 创建文字
            CreateTexts(popup, achievement, isRare);

            return popup;
        }

        /// <summary>
        /// 创建稀有成就光效
        /// </summary>
        private void CreateGlowEffect(PopupInstance popup)
        {
            GameObject glowObj = new GameObject("GlowOverlay");
            glowObj.transform.SetParent(popup.panelObj.transform);
            
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
        private void CreateFrameAndIcon(PopupInstance popup, BossRushAchievementDef achievement, bool isRare)
        {
            // 边框容器
            GameObject frameObj = new GameObject("Frame");
            frameObj.transform.SetParent(popup.panelObj.transform);
            
            RectTransform frameRect = frameObj.AddComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0, 0.5f);
            frameRect.anchorMax = new Vector2(0, 0.5f);
            frameRect.pivot = new Vector2(0, 0.5f);
            frameRect.anchoredPosition = new Vector2(PADDING_LEFT - FRAME_BORDER, 0);
            frameRect.sizeDelta = new Vector2(FRAME_SIZE, FRAME_SIZE);

            // 米色边框
            popup.frameImage = frameObj.AddComponent<Image>();
            popup.frameImage.color = isRare ? FRAME_COLOR_RARE : FRAME_COLOR_STANDARD;

            // 内部黑色背景
            GameObject innerObj = new GameObject("InnerBg");
            innerObj.transform.SetParent(frameObj.transform);
            
            RectTransform innerRect = innerObj.AddComponent<RectTransform>();
            innerRect.anchorMin = Vector2.zero;
            innerRect.anchorMax = Vector2.one;
            innerRect.offsetMin = new Vector2(FRAME_BORDER, FRAME_BORDER);
            innerRect.offsetMax = new Vector2(-FRAME_BORDER, -FRAME_BORDER);

            Image innerImage = innerObj.AddComponent<Image>();
            innerImage.color = INNER_BG_COLOR;

            // 图标（作为内部背景的子对象）
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(innerObj.transform);
            
            RectTransform iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            RawImage iconImage = iconObj.AddComponent<RawImage>();
            Texture2D iconTex = LoadAchievementIcon(achievement.iconFile);
            
            if (iconTex != null)
            {
                iconImage.texture = iconTex;
                iconImage.color = Color.white;
            }
            else
            {
                iconImage.texture = null;
                iconImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            }
        }

        /// <summary>
        /// 创建标题和描述文字
        /// </summary>
        private void CreateTexts(PopupInstance popup, BossRushAchievementDef achievement, bool isRare)
        {
            float textStartX = PADDING_LEFT + FRAME_SIZE + ICON_TEXT_GAP;
            bool isChinese = IsChinese();

            // 标题
            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(popup.panelObj.transform, false);
            
            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.5f);
            titleRect.anchorMax = new Vector2(0, 0.5f);
            titleRect.pivot = new Vector2(0, 0.5f);
            titleRect.anchoredPosition = new Vector2(textStartX, 12f);
            titleRect.sizeDelta = new Vector2(220f, 24f);

            Text titleText = titleObj.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 16;
            titleText.color = TITLE_COLOR;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleText.verticalOverflow = VerticalWrapMode.Overflow;
            titleText.raycastTarget = false;
            titleText.text = isChinese ? achievement.nameCN : achievement.nameEN;

            // 描述
            GameObject descObj = new GameObject("DescriptionText");
            descObj.transform.SetParent(popup.panelObj.transform, false);
            
            RectTransform descRect = descObj.AddComponent<RectTransform>();
            descRect.anchorMin = new Vector2(0, 0.5f);
            descRect.anchorMax = new Vector2(0, 0.5f);
            descRect.pivot = new Vector2(0, 0.5f);
            descRect.anchoredPosition = new Vector2(textStartX, -12f);
            descRect.sizeDelta = new Vector2(220f, 20f);

            Text descText = descObj.AddComponent<Text>();
            descText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            descText.fontSize = 13;
            descText.color = DESC_COLOR;
            descText.alignment = TextAnchor.MiddleLeft;
            descText.horizontalOverflow = HorizontalWrapMode.Overflow;
            descText.verticalOverflow = VerticalWrapMode.Overflow;
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
            float dt = Time.unscaledDeltaTime;
            toRemoveBuffer.Clear(); // 复用缓冲区

            foreach (var popup in activePopups)
            {
                if (popup.isSlideIn)
                {
                    // 滑入动画
                    popup.slideProgress += dt / SLIDE_IN_DURATION;
                    if (popup.slideProgress >= 1f)
                    {
                        popup.slideProgress = 1f;
                        popup.isSlideIn = false;
                    }
                    float t = 1f - Mathf.Pow(1f - popup.slideProgress, 3f);
                    popup.currentY = Mathf.Lerp(-POPUP_HEIGHT - MARGIN_BOTTOM, popup.targetY, t);
                }
                else if (popup.isSlideOut)
                {
                    // 滑出动画
                    popup.slideProgress += dt / SLIDE_OUT_DURATION;
                    if (popup.slideProgress >= 1f)
                    {
                        toRemoveBuffer.Add(popup);
                        continue;
                    }
                    float t = popup.slideProgress * popup.slideProgress * popup.slideProgress;
                    popup.currentY = Mathf.Lerp(popup.targetY, -POPUP_HEIGHT - MARGIN_BOTTOM, t);
                }
                else
                {
                    // 正常显示 - 平滑移动到目标位置
                    float diff = popup.targetY - popup.currentY;
                    if (Mathf.Abs(diff) > 0.5f)
                    {
                        popup.currentY += diff * Mathf.Min(1f, dt / STACK_MOVE_DURATION * 8f);
                    }
                    else
                    {
                        popup.currentY = popup.targetY;
                    }

                    popup.displayTimer += dt;
                    if (popup.displayTimer >= DISPLAY_DURATION)
                    {
                        popup.isSlideOut = true;
                        popup.slideProgress = 0f;
                    }
                }

                popup.panelRect.anchoredPosition = new Vector2(-MARGIN_RIGHT, popup.currentY);
            }

            // 移除已完成的弹窗
            foreach (var popup in toRemoveBuffer)
            {
                activePopups.Remove(popup);
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
        /// 加载UI纹理
        /// </summary>
        private Texture2D LoadTexture(string filename)
        {
            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return null;

                string path = System.IO.Path.Combine(modPath, "Assets", "Textures", "UI", filename);
                if (System.IO.File.Exists(path))
                {
                    byte[] fileData = System.IO.File.ReadAllBytes(path);
                    Texture2D texture = new Texture2D(2, 2);
                    if (texture.LoadImage(fileData))
                    {
                        return texture;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Achievement] 加载纹理失败 " + filename + ": " + e.Message);
            }
            return null;
        }

        // 图标缓存（避免重复加载）
        private static Dictionary<string, Texture2D> popupIconCache = new Dictionary<string, Texture2D>();
        
        // AssetBundle 缓存
        private static object achievementIconBundle = null;
        private static bool bundleLoadAttempted = false;

        /// <summary>
        /// 加载成就图标（带缓存和默认图标回退）
        /// 优先从 AssetBundle 加载，回退到 PNG 文件
        /// </summary>
        private Texture2D LoadAchievementIcon(string iconFile)
        {
            // 检查缓存
            if (!string.IsNullOrEmpty(iconFile) && popupIconCache.ContainsKey(iconFile))
            {
                return popupIconCache[iconFile];
            }

            Texture2D tex = null;
            string iconName = !string.IsNullOrEmpty(iconFile) ? System.IO.Path.GetFileNameWithoutExtension(iconFile) : null;

            // 从 AssetBundle 加载
            tex = LoadIconFromAssetBundle(iconName);
            
            // 回退到默认图标
            if (tex == null)
            {
                tex = LoadIconFromAssetBundle("default");
            }

            // 缓存结果
            if (tex != null && !string.IsNullOrEmpty(iconFile))
            {
                popupIconCache[iconFile] = tex;
            }

            return tex;
        }

        /// <summary>
        /// 从 AssetBundle 加载图标
        /// </summary>
        private Texture2D LoadIconFromAssetBundle(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return null;

            try
            {
                // 延迟加载 AssetBundle
                if (!bundleLoadAttempted)
                {
                    bundleLoadAttempted = true;
                    string modPath = ModBehaviour.GetModPath();
                    if (!string.IsNullOrEmpty(modPath))
                    {
                        string bundlePath = System.IO.Path.Combine(modPath, "Assets", "achievement", "achievement_icons");
                        if (System.IO.File.Exists(bundlePath))
                        {
                            // 通过反射加载 AssetBundle
                            System.Type abType = System.Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule");
                            if (abType == null)
                                abType = System.Type.GetType("UnityEngine.AssetBundle, UnityEngine");
                            
                            if (abType != null)
                            {
                                var loadMethod = abType.GetMethod("LoadFromFile", new System.Type[] { typeof(string) });
                                if (loadMethod != null)
                                {
                                    achievementIconBundle = loadMethod.Invoke(null, new object[] { bundlePath });
                                    if (achievementIconBundle != null)
                                    {
                                        Debug.Log("[Achievement] 已加载成就图标 AssetBundle");
                                    }
                                }
                            }
                        }
                    }
                }

                // 从 bundle 加载纹理
                if (achievementIconBundle != null)
                {
                    System.Type abType = achievementIconBundle.GetType();
                    var loadAssetMethod = abType.GetMethod("LoadAsset", new System.Type[] { typeof(string), typeof(System.Type) });
                    if (loadAssetMethod != null)
                    {
                        // 尝试加载 Sprite
                        object asset = loadAssetMethod.Invoke(achievementIconBundle, new object[] { iconName, typeof(Sprite) });
                        if (asset is Sprite sprite && sprite.texture != null)
                        {
                            return sprite.texture;
                        }

                        // 尝试加载 Texture2D
                        asset = loadAssetMethod.Invoke(achievementIconBundle, new object[] { iconName, typeof(Texture2D) });
                        if (asset is Texture2D tex)
                        {
                            return tex;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Achievement] AssetBundle 加载图标失败: " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// 清除图标缓存（供外部调用）
        /// </summary>
        public static void ClearIconCache()
        {
            popupIconCache.Clear();
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
