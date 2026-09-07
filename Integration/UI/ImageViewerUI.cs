// ============================================================================
// ImageViewerUI.cs - 全屏图片查看器UI
// ============================================================================
// 模块说明：
//   用于全屏显示图片的UI管理器。
//   支持从AssetBundle加载图片，点击任意位置关闭。
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace BossRush
{
    /// <summary>
    /// 全屏图片查看器UI（单例）
    /// </summary>
    public class ImageViewerUI : MonoBehaviour, IPointerClickHandler
    {
        // ============================================================================
        // 单例
        // ============================================================================

        private static ImageViewerUI _instance;
        public static ImageViewerUI Instance
        {
            get
            {
                if (_instance == null)
                {
                    CreateInstance();
                }
                return _instance;
            }
        }

        // ============================================================================
        // UI组件引用
        // ============================================================================

        private GameObject uiRoot;
        private Image backgroundImage;
        private Image mainImage;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI hintText;

        // ============================================================================
        // 状态
        // ============================================================================

        private bool isOpen = false;
        private Sprite currentSprite = null;

        // ============================================================================
        // 常量
        // ============================================================================

        private const float IMAGE_MAX_SCALE = 0.85f;  // 图片最大占屏幕比例
        private const int TITLE_FONT_SIZE = 36;
        private const int HINT_FONT_SIZE = 24;

        // ============================================================================
        // 初始化
        // ============================================================================

        private static void CreateInstance()
        {
            GameObject go = new GameObject("ImageViewerUI");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ImageViewerUI>();
            _instance.Initialize();
        }

        private void Initialize()
        {
            try
            {
                CreateUI();
                ModBehaviour.DevLog("[ImageViewer] 初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ImageViewer] 初始化失败: " + e.Message);
            }
        }

        private void CreateUI()
        {
            // 1. 创建 Canvas
            uiRoot = new GameObject("ImageViewerCanvas");
            uiRoot.transform.SetParent(transform, false);

            Canvas canvas = uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.Modal;

            CanvasScaler scaler = uiRoot.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);

            uiRoot.AddComponent<GraphicRaycaster>();

            // 2. 创建背景（半透明黑色，接收点击）
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(uiRoot.transform, false);

            backgroundImage = bgObj.AddComponent<Image>();
            // 全屏看图要压住背后的战斗画面，用强遮罩 token，不另起一套魔法数字。
            backgroundImage.color = BossRushUIColors.BackdropStrong;
            backgroundImage.raycastTarget = true;

            RectTransform bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // 添加点击事件到背景
            EventTrigger trigger = bgObj.AddComponent<EventTrigger>();
            EventTrigger.Entry clickEntry = new EventTrigger.Entry();
            clickEntry.eventID = EventTriggerType.PointerClick;
            clickEntry.callback.AddListener((data) => { CloseUI(); });
            trigger.triggers.Add(clickEntry);

            // 3. 创建主图片
            GameObject imgObj = new GameObject("MainImage");
            imgObj.transform.SetParent(uiRoot.transform, false);

            mainImage = imgObj.AddComponent<Image>();
            mainImage.preserveAspect = true;
            mainImage.raycastTarget = false;

            RectTransform imgRect = imgObj.GetComponent<RectTransform>();
            // 在 Canvas 逻辑坐标内留出标题/关闭提示区，Image.preserveAspect 负责适配。
            // 不把 Screen 像素直接写入 sizeDelta，否则 4K 会被 CanvasScaler 再放大一遍。
            float sideMargin = (1f - IMAGE_MAX_SCALE) * 0.5f;
            imgRect.anchorMin = new Vector2(sideMargin, 0f);
            imgRect.anchorMax = new Vector2(1f - sideMargin, 1f);
            imgRect.pivot = new Vector2(0.5f, 0.5f);
            imgRect.offsetMin = new Vector2(0f, 110f);
            imgRect.offsetMax = new Vector2(0f, -110f);

            // 4. 创建标题文本
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(uiRoot.transform, false);

            titleText = titleObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(titleText);
            titleText.text = "";
            titleText.fontSize = TITLE_FONT_SIZE;
            titleText.color = BossRushUIColors.TextPrimary;
            titleText.enableWordWrapping = false;
            titleText.overflowMode = TextOverflowModes.Ellipsis;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.raycastTarget = false;

            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.1f, 1);
            titleRect.anchorMax = new Vector2(0.9f, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(0, -30);
            titleRect.sizeDelta = new Vector2(0f, 50f);

            // 5. 创建提示文本
            GameObject hintObj = new GameObject("Hint");
            hintObj.transform.SetParent(uiRoot.transform, false);

            hintText = hintObj.AddComponent<TextMeshProUGUI>();
            BossRushUI.ApplyGameFont(hintText);
            hintText.text = L10n.T("点击任意位置关闭", "Click anywhere to close");
            hintText.fontSize = HINT_FONT_SIZE;
            hintText.color = BossRushUIColors.TextSecondary;
            hintText.alignment = TextAlignmentOptions.Center;
            hintText.raycastTarget = false;

            RectTransform hintRect = hintObj.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0.5f, 0);
            hintRect.anchorMax = new Vector2(0.5f, 0);
            hintRect.pivot = new Vector2(0.5f, 0);
            hintRect.anchoredPosition = new Vector2(0, 30);
            hintRect.sizeDelta = new Vector2(600, 40);

            // 初始隐藏
            uiRoot.SetActive(false);
        }

        // ============================================================================
        // 公共方法
        // ============================================================================

        /// <summary>
        /// 显示图片
        /// </summary>
        /// <param name="bundleName">AssetBundle名称</param>
        /// <param name="imageName">图片资源名称</param>
        /// <param name="title">标题（可选）</param>
        public void ShowImage(string bundleName, string imageName, string title = "")
        {
            try
            {
                // 加载图片
                Sprite sprite = LoadSpriteFromBundle(bundleName, imageName);
                if (sprite == null)
                {
                    ModBehaviour.DevLog("[ImageViewer] 无法加载图片: " + bundleName + "/" + imageName);

                    // 尝试使用占位图
                    ShowPlaceholder(title);
                    return;
                }

                ShowSpriteInternal(sprite, title);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ImageViewer] 显示图片失败: " + e.Message);
            }
        }

        /// <summary>
        /// 直接显示Sprite
        /// </summary>
        public void ShowSprite(Sprite sprite, string title = "")
        {
            if (sprite == null) return;
            ShowSpriteInternal(sprite, title);
        }

        /// <summary>
        /// 关闭图片查看器
        /// </summary>
        public void CloseUI()
        {
            if (!isOpen) return;

            isOpen = false;
            uiRoot.SetActive(false);

            // 恢复游戏时间
            Time.timeScale = 1f;

            ModBehaviour.DevLog("[ImageViewer] UI已关闭");
        }

        /// <summary>
        /// 是否打开
        /// </summary>
        public bool IsOpen => isOpen;

        // ============================================================================
        // 私有方法
        // ============================================================================

        private void ShowSpriteInternal(Sprite sprite, string title)
        {
            currentSprite = sprite;

            // 设置图片
            mainImage.sprite = sprite;
            mainImage.enabled = true;
            hintText.text = L10n.T("点击任意位置关闭", "Click anywhere to close");

            // 设置标题
            if (!string.IsNullOrEmpty(title))
            {
                titleText.text = title;
                titleText.gameObject.SetActive(true);
            }
            else
            {
                titleText.gameObject.SetActive(false);
            }

            // 显示UI
            uiRoot.SetActive(true);
            isOpen = true;

            // 暂停游戏
            Time.timeScale = 0f;

            ModBehaviour.DevLog("[ImageViewer] 显示图片: " + title);
        }

        private void ShowPlaceholder(string title)
        {
            // 加载已失败，不显示永远不会结束的“加载中”，也不再为失败分配一张大纹理。
            currentSprite = null;
            mainImage.sprite = null;
            mainImage.enabled = false;
            string placeholderTitle = string.IsNullOrEmpty(title) ?
                L10n.T("图片暂不可用", "Image unavailable") : title;
            titleText.text = placeholderTitle;
            titleText.gameObject.SetActive(true);
            hintText.text = L10n.T("图片未能加载 · 点击任意位置关闭", "Image could not be loaded · Click anywhere to close");

            uiRoot.SetActive(true);
            isOpen = true;
            Time.timeScale = 0f;

            ModBehaviour.DevLog("[ImageViewer] 显示占位图: " + placeholderTitle);
        }

        private Sprite LoadSpriteFromBundle(string bundleName, string imageName)
        {
            try
            {
                // 尝试通过 ItemFactory 加载
                Sprite sprite = ItemFactory.GetSprite(bundleName, imageName);
                if (sprite != null)
                {
                    return sprite;
                }

                // 尝试直接加载 AssetBundle
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(modDir, "Assets", "items", bundleName);

                if (!File.Exists(bundlePath))
                {
                    bundlePath = Path.Combine(modDir, "Assets", "ui", bundleName);
                }

                if (!File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[ImageViewer] 未找到 AssetBundle: " + bundleName);
                    return null;
                }

                // 使用反射加载
                Type assetBundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule")
                    ?? Type.GetType("UnityEngine.AssetBundle, UnityEngine");

                if (assetBundleType == null) return null;

                MethodInfo loadFromFile = assetBundleType.GetMethod("LoadFromFile", new Type[] { typeof(string) });
                if (loadFromFile == null) return null;

                object bundle = loadFromFile.Invoke(null, new object[] { bundlePath });
                if (bundle == null) return null;

                // 尝试加载 Sprite
                MethodInfo loadAsset = assetBundleType.GetMethod("LoadAsset", new Type[] { typeof(string), typeof(Type) });
                if (loadAsset != null)
                {
                    sprite = loadAsset.Invoke(bundle, new object[] { imageName, typeof(Sprite) }) as Sprite;
                    if (sprite != null) return sprite;

                    // 尝试加载 Texture2D 并转换
                    Texture2D tex = loadAsset.Invoke(bundle, new object[] { imageName, typeof(Texture2D) }) as Texture2D;
                    if (tex != null)
                    {
                        sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                        return sprite;
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ImageViewer] 加载Sprite失败: " + e.Message);
                return null;
            }
        }

        // ============================================================================
        // Unity事件
        // ============================================================================

        private void Update()
        {
            if (!isOpen) return;

            // ESC键关闭
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseUI();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            CloseUI();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
