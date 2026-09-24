// ============================================================================
// ImageViewerUI.cs - 全屏图片查看器UI
// ============================================================================
// 模块说明：
//   用于全屏显示图片的UI管理器。
//   支持从AssetBundle加载图片，点击任意位置关闭。
//
//   2026-09-23 审美审查 UD-45：叮当的涂鸦、生日贺图这类「礼物」旧版一帧弹满整屏、一帧消失，
//   图片直接悬在 0.9 黑底上。现在：
//   - 图片装进一张圆角卡（SurfaceRaised + 描边 + 外投影，内衬 12px），卡片按图片宽高比贴合（AspectRatioFitter）；
//   - 打开：遮罩暗角 + 0.15 秒淡入，卡片走共享打开动画，标题 / 提示错峰淡入；
//   - 关闭：0.12 秒淡出后再 SetActive(false)。本界面是复用的单例根，不能用 PlayCloseAndDestroy，
//     淡出由自己的 Update 推进（unscaled 时间 + IsGamePaused 门），淡出中再次打开会直接取消淡出。
//
//   2026-09-24：ItemFactory.GetSprite 取不到图时的回退路径，旧写法每次都 AssetBundle.LoadFromFile、从不 Unload；
//   Unity 不允许同一个 bundle 文件同时加载两份，第二次看同一张图就返回 null，只剩「图片暂不可用」占位。
//   现在同一个 bundle 只打开一次并缓存（先查自己的缓存、再借别处已打开的同名 bundle、都没有才打开），
//   Mod 卸载时经 ResetStaticCaches 释放（Unload(false)，不销毁已经取出来的图）。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
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
        private CanvasGroup rootGroup;
        private Image backgroundImage;
        private GameObject imageFrame;
        private AspectRatioFitter frameFitter;
        private Image mainImage;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI hintText;

        // ============================================================================
        // 状态
        // ============================================================================

        private bool isOpen = false;
        private ZombieModeUIHelper.ModalInputLease modalLease;
        private Sprite currentSprite = null;
        private bool closing = false;
        private float closeElapsed = 0f;

        // ============================================================================
        // 回退路径的 bundle 与现造 Sprite 缓存（只在 ItemFactory.GetSprite 取不到图时用）
        // ============================================================================

        /// <summary>回退路径拿到的一个 bundle。Owned = 由本界面打开，卸载时归本界面 Unload；借别处的不动。</summary>
        private sealed class FallbackBundle
        {
            internal AssetBundle Bundle;
            internal bool Owned;
        }

        /// <summary>同一个 bundle 文件只打开一次：键是 bundle 文件的完整路径（大小写不敏感）。</summary>
        private static readonly Dictionary<string, FallbackBundle> fallbackBundles =
            new Dictionary<string, FallbackBundle>(StringComparer.OrdinalIgnoreCase);
        /// <summary>从 Texture2D 现造的 Sprite：同一张图只造一次（旧写法每次打开都 Sprite.Create 一个新的）。</summary>
        private static readonly Dictionary<string, Sprite> fallbackCreatedSprites =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        // ============================================================================
        // 常量
        // ============================================================================

        private const float IMAGE_MAX_SCALE = 0.85f;  // 图片最大占屏幕比例
        private const int TITLE_FONT_SIZE = 34;
        private const int HINT_FONT_SIZE = 16;
        /// <summary>图片与卡片边缘之间的内衬。</summary>
        private const float FRAME_PADDING = 12f;
        private const float BACKDROP_FADE_SECONDS = 0.15f;

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
            // 关闭淡出用：整个根一起淡（复用的单例根，淡完 SetActive(false)，见 Update）。
            rootGroup = uiRoot.AddComponent<CanvasGroup>();

            // 2. 创建背景（半透明黑色，接收点击）
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(uiRoot.transform, false);

            backgroundImage = bgObj.AddComponent<Image>();
            // 全屏看图要压住背后的战斗画面，用强遮罩 token，不另起一套魔法数字。
            backgroundImage.color = BossRushUIColors.BackdropStrong;
            backgroundImage.raycastTarget = true;
            // 暗角把视线往中间收（共享遮罩口径）；它顺带起播一次淡入，每次打开再重播（ShowRoot）。
            BossRushUIKit.StyleBackdrop(backgroundImage);

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

            // 3. 创建主图片：可用区域 → 按图片宽高比贴合的圆角卡 → 卡内留 12px 的图片
            GameObject areaObj = new GameObject("ImageArea");
            areaObj.transform.SetParent(uiRoot.transform, false);
            RectTransform imgRect = areaObj.AddComponent<RectTransform>();
            // 在 Canvas 逻辑坐标内留出标题/关闭提示区，卡片与 Image.preserveAspect 负责适配。
            // 不把 Screen 像素直接写入 sizeDelta，否则 4K 会被 CanvasScaler 再放大一遍。
            float sideMargin = (1f - IMAGE_MAX_SCALE) * 0.5f;
            imgRect.anchorMin = new Vector2(sideMargin, 0f);
            imgRect.anchorMax = new Vector2(1f - sideMargin, 1f);
            imgRect.pivot = new Vector2(0.5f, 0.5f);
            imgRect.offsetMin = new Vector2(0f, 110f);
            imgRect.offsetMax = new Vector2(0f, -110f);

            imageFrame = new GameObject("ImageFrame");
            imageFrame.transform.SetParent(areaObj.transform, false);
            RectTransform frameRect = imageFrame.AddComponent<RectTransform>();
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = Vector2.zero;
            frameRect.offsetMax = Vector2.zero;
            Image frameImage = imageFrame.AddComponent<Image>();
            frameImage.color = BossRushUIColors.SurfaceRaised;
            frameImage.raycastTarget = false;
            BossRushUI.ApplyFramedPanelSkin(frameImage, 10, BossRushUISkinPart.Card);
            // 卡片在可用区域内按图片宽高比贴合：宽图贴左右、竖图贴上下，不会出现一大块空卡底。
            frameFitter = imageFrame.AddComponent<AspectRatioFitter>();
            frameFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            frameFitter.aspectRatio = 1f;

            GameObject imgObj = new GameObject("MainImage");
            imgObj.transform.SetParent(imageFrame.transform, false);

            mainImage = imgObj.AddComponent<Image>();
            mainImage.preserveAspect = true;
            mainImage.raycastTarget = false;

            RectTransform mainRect = imgObj.GetComponent<RectTransform>();
            mainRect.anchorMin = Vector2.zero;
            mainRect.anchorMax = Vector2.one;
            mainRect.offsetMin = new Vector2(FRAME_PADDING, FRAME_PADDING);
            mainRect.offsetMax = new Vector2(-FRAME_PADDING, -FRAME_PADDING);

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
            // 单行框高 ≥ 字号×1.45+4（34 号 → 54），框不够时 TMP Ellipsis 会清空整行。
            titleRect.sizeDelta = new Vector2(0f, 54f);

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

            // 先放输入，再播淡出：动画不能变成输入延迟。淡出中不再吃点击，淡完由 Update 收起根物体。
            if (modalLease != null) { modalLease.Release(); modalLease = null; }
            if (uiRoot != null && uiRoot.activeInHierarchy && rootGroup != null)
            {
                closing = true;
                closeElapsed = 0f;
                rootGroup.blocksRaycasts = false;
                rootGroup.interactable = false;
            }
            else if (uiRoot != null)
            {
                uiRoot.SetActive(false);
            }

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

            // 设置图片：卡片按图片宽高比贴合
            mainImage.sprite = sprite;
            mainImage.enabled = true;
            imageFrame.SetActive(true);
            Rect spriteRect = sprite.rect;
            frameFitter.aspectRatio = spriteRect.height > 0f ? spriteRect.width / spriteRect.height : 1f;
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
            ShowRoot();

            // 暂停游戏
            if (modalLease == null) modalLease = ZombieModeUIHelper.ClaimModalInput(uiRoot, "ImageViewer");

            ModBehaviour.DevLog("[ImageViewer] 显示图片: " + title);
        }

        private void ShowPlaceholder(string title)
        {
            // 加载已失败，不显示永远不会结束的“加载中”，也不再为失败分配一张大纹理。
            currentSprite = null;
            mainImage.sprite = null;
            mainImage.enabled = false;
            imageFrame.SetActive(false);   // 没有图就不留一张空卡
            string placeholderTitle = string.IsNullOrEmpty(title) ?
                L10n.T("图片暂不可用", "Image unavailable") : title;
            titleText.text = placeholderTitle;
            titleText.gameObject.SetActive(true);
            hintText.text = L10n.T("图片未能加载 · 点击任意位置关闭", "Image could not be loaded · Click anywhere to close");

            ShowRoot();
            if (modalLease == null) modalLease = ZombieModeUIHelper.ClaimModalInput(uiRoot, "ImageViewer");

            ModBehaviour.DevLog("[ImageViewer] 显示占位图: " + placeholderTitle);
        }

        /// <summary>
        /// 打开（或在淡出途中重新打开）：取消淡出、恢复点击，遮罩淡入，卡片走共享打开动画，标题 / 提示错峰淡入。
        /// 同页已开着时换图不重播（换内容不该整屏再闪一次）。
        /// </summary>
        private void ShowRoot()
        {
            bool wasVisible = isOpen && !closing && uiRoot.activeSelf;
            closing = false;
            closeElapsed = 0f;
            if (rootGroup != null)
            {
                rootGroup.alpha = 1f;
                rootGroup.blocksRaycasts = true;
                rootGroup.interactable = true;
            }
            uiRoot.SetActive(true);
            isOpen = true;
            if (wasVisible)
            {
                return;
            }

            BossRushUIEntranceAnimation.Play(backgroundImage.gameObject, 0f, BACKDROP_FADE_SECONDS, 0f);
            if (imageFrame.activeSelf)
            {
                BossRushUI.PlayOpenAnimation(imageFrame);
            }
            if (titleText.gameObject.activeSelf)
            {
                BossRushUIEntranceAnimation.Play(titleText.gameObject, 0.06f, 0.2f, 8f);
            }
            BossRushUIEntranceAnimation.Play(hintText.gameObject, 0.12f, 0.2f, 0f);
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

                // 回退：自己在 Assets/items、Assets/ui 下找 bundle
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

                AssetBundle bundle = AcquireFallbackBundle(bundlePath, bundleName);
                if (bundle == null) return null;

                // 尝试加载 Sprite（bundle 里原生的 Sprite 归 bundle 管，不进现造缓存）
                sprite = bundle.LoadAsset<Sprite>(imageName);
                if (sprite != null) return sprite;

                // 尝试加载 Texture2D 并转换：同一张图只造一次
                string createdKey = Path.GetFullPath(bundlePath) + "|" + imageName;
                Sprite created;
                if (fallbackCreatedSprites.TryGetValue(createdKey, out created) && created != null) return created;
                Texture2D tex = bundle.LoadAsset<Texture2D>(imageName);
                if (tex != null)
                {
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    if (sprite != null) fallbackCreatedSprites[createdKey] = sprite;
                    return sprite;
                }

                return null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ImageViewer] 加载Sprite失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 回退路径拿 bundle，同一个文件只打开一次：先查自己的缓存；再借别处已经打开的同名 bundle
        /// （ItemFactory、图鉴、成就图标都可能先开过，再 LoadFromFile 一次 Unity 会拒绝并返回 null）；
        /// 都没有才经共享的 ResourceBundleLoader 打开，并记为本界面持有。
        /// </summary>
        private static AssetBundle AcquireFallbackBundle(string bundlePath, string bundleName)
        {
            string key = Path.GetFullPath(bundlePath);
            FallbackBundle entry;
            if (fallbackBundles.TryGetValue(key, out entry))
            {
                if (entry != null && entry.Bundle != null) return entry.Bundle;
                // 借来的 bundle 被原主人 Unload 了（Unity 判空为真）：丢掉失效句柄，重新找
                fallbackBundles.Remove(key);
            }

            AssetBundle bundle = ItemFactory.FindAlreadyLoadedAssetBundle(bundleName);
            bool owned = false;
            if (bundle == null)
            {
                bundle = ResourceBundleLoader.LoadFromFile(bundlePath);
                owned = bundle != null;
            }
            if (bundle == null)
            {
                ModBehaviour.DevLog("[ImageViewer] 打开 AssetBundle 失败: " + bundlePath);
                return null;
            }

            fallbackBundles[key] = new FallbackBundle { Bundle = bundle, Owned = owned };
            return bundle;
        }

        // ============================================================================
        // Unity事件
        // ============================================================================

        private void Update()
        {
            if (closing)
            {
                TickCloseFade();
                return;
            }
            if (!isOpen) return;

            // ESC键关闭
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseUI();
            }
        }

        /// <summary>关闭淡出：0.12 秒 SmoothStep，走 unscaled 时间；暂停菜单开着时停推进。淡完收起根物体、复位 alpha。</summary>
        private void TickCloseFade()
        {
            if (BossRushUI.IsGamePaused())
            {
                return;
            }
            closeElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(closeElapsed / BossRushUIKit.CloseSeconds);
            if (rootGroup != null)
            {
                rootGroup.alpha = 1f - BossRushUI.SmoothStep(t);
            }
            if (t < 1f)
            {
                return;
            }
            closing = false;
            if (uiRoot != null)
            {
                uiRoot.SetActive(false);
            }
            if (rootGroup != null)
            {
                rootGroup.alpha = 1f;
                rootGroup.blocksRaycasts = true;
                rootGroup.interactable = true;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            CloseUI();
        }

        private void OnDestroy()
        {
            if (modalLease != null) { modalLease.Release(); modalLease = null; }
            if (_instance == this)
            {
                _instance = null;
            }
        }

        internal static void Shutdown()
        {
            if (_instance == null) return;
            _instance.CloseUI();
            Destroy(_instance.gameObject);
            _instance = null;
        }

        /// <summary>
        /// Mod 卸载（IntegrationRuntimeHooks.CleanupIntegrationRuntimeOnDestroy）：先收掉看图器本身，再放掉回退缓存。
        /// 本界面打开的 bundle 用 Unload(false)：只放 bundle 句柄，已经取出来的 Sprite / Texture 不跟着销毁，
        /// 别处借这个 bundle 取出的资源也不会被连带清掉；借别处的 bundle 不动。现造的 Sprite 随看图器一起销毁。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            try
            {
                Shutdown();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ImageViewer] 关闭看图器失败: " + e.Message);
            }

            foreach (FallbackBundle entry in fallbackBundles.Values)
            {
                if (entry == null || !entry.Owned || entry.Bundle == null) continue;
                try
                {
                    entry.Bundle.Unload(false);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[ImageViewer] 卸载 AssetBundle 失败: " + e.Message);
                }
            }
            fallbackBundles.Clear();

            foreach (Sprite created in fallbackCreatedSprites.Values)
            {
                if (created != null) Destroy(created);
            }
            fallbackCreatedSprites.Clear();
        }
    }
}
