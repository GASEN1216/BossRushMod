// ============================================================================
// AffinityUIManager.cs - 好感度UI管理器
// ============================================================================
// 模块说明：
//   管理好感度相关的UI显示，包括好感度面板、变化动画、等级提升通知等。
//   复用游戏原版UI组件和样式。
// ============================================================================

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Duckov.UI;

namespace BossRush
{
    /// <summary>
    /// 好感度UI管理器
    /// </summary>
    public static class AffinityUIManager
    {
        // UI状态
        private static bool isUIOpen = false;
        private static GameObject affinityPanel = null;
        private static string currentNpcId = null;
        
        // UI组件引用
        private static TextMeshProUGUI levelText = null;
        private static Image progressBar = null;
        private static TextMeshProUGUI npcNameText = null;
        private static Image heartIcon = null;
        
        // 变化动画
        private static GameObject changePopup = null;
        private static Coroutine changeAnimCoroutine = null;
        
        // 红心图标资源
        private static Sprite heartSprite = null;
        private static bool heartSpriteLoaded = false;
        
        // UI配置
        private const float PANEL_WIDTH = 200f;
        private const float PANEL_HEIGHT = 60f;
        private const float CHANGE_POPUP_DURATION = 1.5f;
        private const float HEART_ICON_SIZE = 24f;
        
        /// <summary>
        /// 显示好感度面板
        /// </summary>
        public static void ShowAffinityPanel(string npcId, Transform parent)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            currentNpcId = npcId;
            
            // 如果面板已存在，只更新内容
            if (affinityPanel != null)
            {
                UpdateAffinityDisplay(npcId);
                affinityPanel.SetActive(true);
                isUIOpen = true;
                return;
            }
            
            // 创建新面板
            CreateAffinityPanel(parent);
            UpdateAffinityDisplay(npcId);
            isUIOpen = true;
        }
        
        /// <summary>
        /// 隐藏好感度面板
        /// </summary>
        public static void HideAffinityPanel()
        {
            if (affinityPanel != null)
            {
                affinityPanel.SetActive(false);
            }
            isUIOpen = false;
            currentNpcId = null;
        }
        
        /// <summary>
        /// 更新好感度显示
        /// </summary>
        public static void UpdateAffinityDisplay(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            // 获取NPC配置
            INPCAffinityConfig config = AffinityManager.GetNPCConfig(npcId);
            
            // 更新NPC名称
            if (npcNameText != null && config != null)
            {
                npcNameText.text = config.DisplayName;
            }
            
            // 更新等级文本
            if (levelText != null)
            {
                int level = AffinityManager.GetLevel(npcId);
                int maxLevel = config?.MaxLevel ?? AffinityConfig.DEFAULT_MAX_LEVEL;
                levelText.text = L10n.T("好感度", "Affinity") + ": Lv." + level + "/" + maxLevel;
            }
            
            // 更新进度条
            if (progressBar != null)
            {
                float progress = AffinityManager.GetLevelProgress(npcId);
                progressBar.fillAmount = progress;
            }
        }
        
        /// <summary>
        /// 显示好感度变化动画
        /// </summary>
        public static void ShowAffinityChange(string npcId, int delta)
        {
            if (delta == 0) return;
            
            // 创建变化弹出文本
            try
            {
                // 查找Canvas（优先使用游戏主Canvas）
                Canvas canvas = GetMainCanvas();
                if (canvas == null)
                {
                    // 回退：使用通知系统
                    string msg = (delta > 0 ? "+" : "") + delta + " " + L10n.T("好感度", "Affinity");
                    NotificationText.Push(msg);
                    return;
                }
                
                // 创建弹出文本
                GameObject popup = new GameObject("AffinityChangePopup");
                popup.transform.SetParent(canvas.transform, false);
                
                TextMeshProUGUI text = popup.AddComponent<TextMeshProUGUI>();
                text.text = (delta > 0 ? "+" : "") + delta;
                text.fontSize = 24;
                text.alignment = TextAlignmentOptions.Center;
                text.color = delta > 0 ? Color.green : Color.red;
                
                // 设置位置（屏幕中央偏上）
                RectTransform rect = popup.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.6f);
                rect.anchorMax = new Vector2(0.5f, 0.6f);
                rect.sizeDelta = new Vector2(100, 50);
                
                // 启动动画协程
                if (ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.StartCoroutine(AnimateChangePopup(popup));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffinityUI] 显示变化动画失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 显示等级提升通知
        /// </summary>
        public static void ShowLevelUpNotification(string npcId, int newLevel)
        {
            try
            {
                INPCAffinityConfig config = AffinityManager.GetNPCConfig(npcId);
                string npcName = config?.DisplayName ?? npcId;
                
                string message = L10n.T(
                    npcName + " 好感度提升到 Lv." + newLevel + "！",
                    npcName + " affinity increased to Lv." + newLevel + "!"
                );
                
                // 使用游戏的通知系统
                try
                {
                    NotificationText.Push(message);
                }
                catch
                {
                    // 回退：使用Debug日志
                    ModBehaviour.DevLog("[AffinityUI] " + message);
                }
                
                // 检查是否有解锁内容
                if (config?.UnlocksByLevel != null && config.UnlocksByLevel.TryGetValue(newLevel, out string[] unlocks))
                {
                    foreach (string unlock in unlocks)
                    {
                        string unlockMsg = L10n.T("解锁: " + unlock, "Unlocked: " + unlock);
                        try
                        {
                            NotificationText.Push(unlockMsg);
                        }
                        catch
                        {
                            ModBehaviour.DevLog("[AffinityUI] " + unlockMsg);
                        }
                    }
                }
                
                // 检查是否有折扣
                if (config?.DiscountsByLevel != null && config.DiscountsByLevel.TryGetValue(newLevel, out float discount))
                {
                    int discountPercent = (int)(discount * 100);
                    string discountMsg = L10n.T(
                        "获得 " + discountPercent + "% 折扣！",
                        "Got " + discountPercent + "% discount!"
                    );
                    try
                    {
                        NotificationText.Push(discountMsg);
                    }
                    catch
                    {
                        ModBehaviour.DevLog("[AffinityUI] " + discountMsg);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffinityUI] 显示等级提升通知失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 创建好感度面板
        /// </summary>
        private static void CreateAffinityPanel(Transform parent)
        {
            try
            {
                // 查找Canvas（优先使用游戏主Canvas）
                Canvas canvas = GetMainCanvas();
                if (canvas == null)
                {
                    ModBehaviour.DevLog("[AffinityUI] 无法找到Canvas，跳过创建面板");
                    return;
                }
                
                // 加载红心图标
                LoadHeartSprite();
                
                // 创建面板
                affinityPanel = new GameObject("AffinityPanel");
                affinityPanel.transform.SetParent(canvas.transform, false);
                
                // 添加背景
                Image bg = affinityPanel.AddComponent<Image>();
                bg.color = new Color(0, 0, 0, 0.7f);
                
                // 设置位置和大小
                RectTransform rect = affinityPanel.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.85f);
                rect.anchorMax = new Vector2(0.5f, 0.85f);
                rect.sizeDelta = new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
                
                // 创建红心图标
                if (heartSprite != null)
                {
                    GameObject heartObj = new GameObject("HeartIcon");
                    heartObj.transform.SetParent(affinityPanel.transform, false);
                    heartIcon = heartObj.AddComponent<Image>();
                    heartIcon.sprite = heartSprite;
                    heartIcon.preserveAspect = true;
                    
                    RectTransform heartRect = heartObj.GetComponent<RectTransform>();
                    heartRect.anchorMin = new Vector2(0, 0.5f);
                    heartRect.anchorMax = new Vector2(0, 0.5f);
                    heartRect.pivot = new Vector2(0, 0.5f);
                    heartRect.anchoredPosition = new Vector2(8f, 5f);
                    heartRect.sizeDelta = new Vector2(HEART_ICON_SIZE, HEART_ICON_SIZE);
                }
                
                // 创建NPC名称文本
                GameObject nameObj = new GameObject("NpcName");
                nameObj.transform.SetParent(affinityPanel.transform, false);
                npcNameText = nameObj.AddComponent<TextMeshProUGUI>();
                npcNameText.fontSize = 14;
                npcNameText.alignment = TextAlignmentOptions.Center;
                npcNameText.color = Color.white;
                
                RectTransform nameRect = nameObj.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0, 0.6f);
                nameRect.anchorMax = new Vector2(1, 1);
                nameRect.offsetMin = Vector2.zero;
                nameRect.offsetMax = Vector2.zero;
                
                // 创建等级文本（红心图标右侧）
                GameObject levelObj = new GameObject("LevelText");
                levelObj.transform.SetParent(affinityPanel.transform, false);
                levelText = levelObj.AddComponent<TextMeshProUGUI>();
                levelText.fontSize = 12;
                levelText.alignment = TextAlignmentOptions.Left;
                levelText.color = Color.white;
                
                RectTransform levelRect = levelObj.GetComponent<RectTransform>();
                levelRect.anchorMin = new Vector2(0, 0.3f);
                levelRect.anchorMax = new Vector2(1, 0.6f);
                // 如果有红心图标，文本向右偏移
                float leftOffset = heartSprite != null ? (HEART_ICON_SIZE + 12f) : 10f;
                levelRect.offsetMin = new Vector2(leftOffset, 0);
                levelRect.offsetMax = new Vector2(-10f, 0);
                
                // 创建进度条背景
                GameObject progressBgObj = new GameObject("ProgressBg");
                progressBgObj.transform.SetParent(affinityPanel.transform, false);
                Image progressBg = progressBgObj.AddComponent<Image>();
                progressBg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                
                RectTransform progressBgRect = progressBgObj.GetComponent<RectTransform>();
                progressBgRect.anchorMin = new Vector2(0.1f, 0.1f);
                progressBgRect.anchorMax = new Vector2(0.9f, 0.25f);
                progressBgRect.offsetMin = Vector2.zero;
                progressBgRect.offsetMax = Vector2.zero;
                
                // 创建进度条（使用粉红色，与红心呼应）
                GameObject progressObj = new GameObject("ProgressBar");
                progressObj.transform.SetParent(progressBgObj.transform, false);
                progressBar = progressObj.AddComponent<Image>();
                progressBar.color = new Color(1f, 0.4f, 0.5f, 1f);  // 粉红色
                progressBar.type = Image.Type.Filled;
                progressBar.fillMethod = Image.FillMethod.Horizontal;
                
                RectTransform progressRect = progressObj.GetComponent<RectTransform>();
                progressRect.anchorMin = Vector2.zero;
                progressRect.anchorMax = Vector2.one;
                progressRect.offsetMin = Vector2.zero;
                progressRect.offsetMax = Vector2.zero;
                
                ModBehaviour.DevLog("[AffinityUI] 好感度面板创建成功");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffinityUI] 创建好感度面板失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 加载红心图标（从 broken_heart AssetBundle 中加载 heart_0）
        /// </summary>
        private static void LoadHeartSprite()
        {
            if (heartSpriteLoaded) return;
            heartSpriteLoaded = true;
            
            try
            {
                string modDir = Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                string bundlePath = Path.Combine(modDir, "Assets", "ui", "broken_heart");
                
                if (!File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[AffinityUI] 红心图标AssetBundle不存在: " + bundlePath);
                    return;
                }
                
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    ModBehaviour.DevLog("[AffinityUI] 加载红心图标AssetBundle失败");
                    return;
                }
                
                // 加载 heart_0 作为红心图标
                heartSprite = bundle.LoadAsset<Sprite>("heart_0");
                
                if (heartSprite == null)
                {
                    // 尝试其他可能的名称
                    heartSprite = bundle.LoadAsset<Sprite>("heart_0.png");
                }
                
                if (heartSprite == null)
                {
                    // 尝试从 Texture2D 创建
                    Texture2D texture = bundle.LoadAsset<Texture2D>("heart_0");
                    if (texture == null)
                    {
                        texture = bundle.LoadAsset<Texture2D>("heart_0.png");
                    }
                    if (texture != null)
                    {
                        heartSprite = Sprite.Create(
                            texture,
                            new Rect(0, 0, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f)
                        );
                    }
                }
                
                bundle.Unload(false);
                
                if (heartSprite != null)
                {
                    ModBehaviour.DevLog("[AffinityUI] 红心图标加载成功");
                }
                else
                {
                    ModBehaviour.DevLog("[AffinityUI] 未能加载红心图标 heart_0");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffinityUI] 加载红心图标失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 变化弹出动画协程
        /// </summary>
        private static IEnumerator AnimateChangePopup(GameObject popup)
        {
            if (popup == null) yield break;
            
            float elapsed = 0f;
            RectTransform rect = popup.GetComponent<RectTransform>();
            TextMeshProUGUI text = popup.GetComponent<TextMeshProUGUI>();
            Vector2 startPos = rect.anchoredPosition;
            
            while (elapsed < CHANGE_POPUP_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / CHANGE_POPUP_DURATION;
                
                // 向上移动
                rect.anchoredPosition = startPos + new Vector2(0, t * 50f);
                
                // 淡出
                if (text != null)
                {
                    Color c = text.color;
                    c.a = 1f - t;
                    text.color = c;
                }
                
                yield return null;
            }
            
            UnityEngine.Object.Destroy(popup);
        }
        
        /// <summary>
        /// 清理UI资源
        /// </summary>
        public static void Cleanup()
        {
            if (affinityPanel != null)
            {
                UnityEngine.Object.Destroy(affinityPanel);
                affinityPanel = null;
            }
            
            if (changePopup != null)
            {
                UnityEngine.Object.Destroy(changePopup);
                changePopup = null;
            }
            
            levelText = null;
            progressBar = null;
            npcNameText = null;
            heartIcon = null;
            isUIOpen = false;
            currentNpcId = null;
            _cachedCanvas = null;
        }
        
        /// <summary>
        /// 场景切换时清理（由 ModBehaviour 调用）
        /// </summary>
        public static void OnSceneUnload()
        {
            // 清理所有UI资源和缓存
            Cleanup();
            
            // 重置心形图标加载状态，下次需要时重新加载
            heartSpriteLoaded = false;
            heartSprite = null;
            
            ModBehaviour.DevLog("[AffinityUI] 场景切换，已清理UI资源");
        }
        
        // 缓存的Canvas引用
        private static Canvas _cachedCanvas = null;
        
        /// <summary>
        /// 获取游戏主Canvas（带缓存）
        /// </summary>
        private static Canvas GetMainCanvas()
        {
            // 检查缓存是否有效
            if (_cachedCanvas != null && _cachedCanvas.gameObject.activeInHierarchy)
            {
                return _cachedCanvas;
            }
            
            // 尝试查找游戏的主Canvas
            try
            {
                // 优先查找名为 "Canvas" 或 "MainCanvas" 的Canvas
                Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
                foreach (var canvas in canvases)
                {
                    if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        _cachedCanvas = canvas;
                        return canvas;
                    }
                }
                
                // 回退：使用任意Canvas
                if (canvases.Length > 0)
                {
                    _cachedCanvas = canvases[0];
                    return canvases[0];
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffinityUI] 查找Canvas失败: " + e.Message);
            }
            
            return null;
        }
    }
}
