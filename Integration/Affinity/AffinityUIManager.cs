// ============================================================================
// AffinityUIManager.cs - 好感度UI管理器
// ============================================================================
// 模块说明：
//   管理好感度相关的UI显示，包括好感度面板、变化动画、等级提升通知等。
//   复用游戏原版UI组件和样式。
//
//   2026-09-23 审美审查 UD-39 / UD-41 / UD-42（owner 拍板：加分只给头顶浮字这类轻反馈，送礼三路反馈合一）：
//   - 好感变化浮字挂在 NPC 头侧（世界坐标投到自建 HUD 画布），不再随手找第一个 Overlay 画布、放屏幕正中；
//     找不到 NPC 时才退回屏幕中上方。浮字下方一行 14 号「Lv.3 · 120/300」，取代聊天 / 送礼后那条大横幅。
//   - 动效：0.15 秒从 1.25 倍 EaseOut 落回 1（蹦出来），0.9 秒 EaseOut 上浮 56px，0.6 秒起 SmoothStep 淡出；
//     unscaled 时间 + IsGamePaused 门（模态把 timeScale 压到 0 时旧版浮字会卡在屏幕上），官方界面打开时跟着隐藏。
//   - 升级：1–3 条通知合成一条多行消息，配官方 UI/level_up 音效，NPC 头顶冒心。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Duckov.UI;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 好感度UI管理器
    /// </summary>
    public static class AffinityUIManager
    {
        // UI状态
        private static GameObject affinityPanel = null;
        
        // UI组件引用
        private static TextMeshProUGUI levelText = null;
        private static Image progressBar = null;
        private static TextMeshProUGUI npcNameText = null;
        
        // 红心图标资源
        private static Sprite heartSprite = null;
        private static bool heartSpriteLoaded = false;
        
        // UI配置
        private const float PANEL_WIDTH = 320f;
        private const float PANEL_HEIGHT = 104f;
        private const float HEART_ICON_SIZE = 32f;

        // 好感变化浮字（UD-41）。锚点取 NPC 胸口高度、屏幕上向右偏一截：
        // 头顶正上方是官方对话气泡与爱心序列帧的位置，浮字放在侧边才不互相压。
        private const float FLOAT_ANCHOR_HEIGHT = 1.3f;
        private static readonly Vector2 FloatScreenOffset = new Vector2(70f, 0f);
        /// <summary>找不到 NPC 时的退路：屏幕中上方（本库参考画布单位，相对画布中心）。</summary>
        private static readonly Vector2 FloatFallbackPosition = new Vector2(-40f, 130f);
        private const float FLOAT_VALUE_FONT_SIZE = 28f;
        private const float FLOAT_PROGRESS_FONT_SIZE = 14f;
        private const float FLOAT_HEART_SIZE = 24f;

        /// <summary>浮字专用画布（HUD 附属层、不吃点击）。随 Cleanup 销毁。</summary>
        private static Canvas _floatCanvas;

        /// <summary>
        /// 当前交互对象（一格，不是按 npcId 增长的缓存）。好感事件只带 npcId，浮字位置与升级冒心要找到那只 NPC：
        /// 送礼入口手里有控制器，直接登记（<see cref="NoteController"/>）；聊天等其余路径按类型查一次后记在这里，
        /// 同一只 NPC 接下来的事件直接命中。只在好感事件时用，不在每帧路径上；Cleanup 时清空。
        /// </summary>
        private static MonoBehaviour _interactionController;
        
        /// <summary>
        /// 显示好感度面板
        /// </summary>
        public static void ShowAffinityPanel(string npcId, Transform parent)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            // 如果面板已存在，只更新内容
            if (affinityPanel != null)
            {
                UpdateAffinityDisplay(npcId);
                affinityPanel.SetActive(true);
                return;
            }
            
            // 创建新面板
            CreateAffinityPanel(parent);
            UpdateAffinityDisplay(npcId);
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

            // 创建变化浮字（UD-41）：挂在 NPC 头侧；NPC 不在画面里（例如刷出时结算的每日衰减）就不弹。
            try
            {
                INPCController controller = ResolveController(npcId);
                Transform anchor = controller != null ? controller.NpcTransform : null;
                if (anchor != null && !IsAnchorOnScreen(anchor))
                {
                    return;
                }

                Canvas canvas = GetFloatCanvas();
                if (canvas == null)
                {
                    // 回退：使用通知系统
                    string msg = (delta > 0 ? "+" : "") + delta + " " + L10n.T("好感度", "Affinity");
                    NotificationText.Push(msg);
                    return;
                }

                GameObject popup = BuildFloat(canvas.transform, npcId, delta);
                AffinityFloatText floatText = popup.AddComponent<AffinityFloatText>();
                floatText.Begin(canvas.transform as RectTransform, anchor, FLOAT_ANCHOR_HEIGHT,
                    FloatScreenOffset, FloatFallbackPosition);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffinityUI] 显示变化动画失败: " + e.Message);
            }
        }

        /// <summary>
        /// 送礼 / 交互入口已经拿着控制器时先登记，好感事件回调里就不必再按类型查找。
        /// </summary>
        internal static void NoteController(string npcId, INPCController controller)
        {
            MonoBehaviour behaviour = controller as MonoBehaviour;
            if (string.IsNullOrEmpty(npcId) || behaviour == null)
            {
                return;
            }
            _interactionController = behaviour;
        }

        /// <summary>
        /// 按 npcId 找场景里的 NPC 控制器（叮当、羽织、捏脸 NPC）。命中当前交互对象时 O(1)；
        /// 否则按三个控制器类型各查一次——只在好感变化 / 升级这类事件里调用，不在每帧路径上。
        /// </summary>
        internal static INPCController ResolveController(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return null;
            }

            MonoBehaviour current = _interactionController;
            INPCController currentController = current as INPCController;
            if (current != null && current.isActiveAndEnabled && currentController != null
                && string.Equals(currentController.NpcId, npcId, StringComparison.Ordinal))
            {
                return currentController;
            }

            INPCController found = FindController<GoblinNPCController>(npcId);
            if (found == null) found = FindController<NurseNPCController>(npcId);
            if (found == null) found = FindController<DuckNpcRuntimeMarker>(npcId);
            if (found != null)
            {
                _interactionController = (MonoBehaviour)found;
            }
            return found;
        }

        private static INPCController FindController<T>(string npcId) where T : MonoBehaviour, INPCController
        {
            T[] all = UnityEngine.Object.FindObjectsOfType<T>();
            for (int i = 0; i < all.Length; i++)
            {
                T candidate = all[i];
                if (candidate != null && candidate.isActiveAndEnabled
                    && string.Equals(candidate.NpcId, npcId, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static bool IsAnchorOnScreen(Transform anchor)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return false;
            }
            Vector3 viewport = camera.WorldToViewportPoint(anchor.position + Vector3.up * FLOAT_ANCHOR_HEIGHT);
            return viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        }

        private static Canvas GetFloatCanvas()
        {
            if (_floatCanvas != null)
            {
                return _floatCanvas;
            }
            // HUD 附属层、不吃点击；浮字本身每帧跟随官方界面隐藏与暂停（见 AffinityFloatText）。
            _floatCanvas = BossRushUI.CreateCanvasRoot("BossRush_AffinityFloat", BossRushUILayers.HudOverlay, false);
            UnityEngine.Object.DontDestroyOnLoad(_floatCanvas.gameObject);
            return _floatCanvas;
        }

        /// <summary>
        /// 浮字的形：[心形] +10（28 号粗体，成功 / 危险字色）/ 下方一行 14 号次级字「Lv.3 · 120/300」。
        /// 压在游戏世界上的字统一走 TMP 描边 + 底影共享材质。
        /// </summary>
        private static GameObject BuildFloat(Transform parent, string npcId, int delta)
        {
            GameObject popup = ZombieModeUIHelper.CreateRect("AffinityChangePopup", parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 72f), new Vector2(0f, 0.5f));

            float textLeft = 0f;
            if (delta > 0)
            {
                LoadHeartSprite();
                if (heartSprite != null)
                {
                    GameObject heartObj = ZombieModeUIHelper.CreateRect("Heart", popup.transform,
                        new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 10f),
                        new Vector2(FLOAT_HEART_SIZE, FLOAT_HEART_SIZE), new Vector2(0f, 0.5f));
                    Image heartIcon = heartObj.AddComponent<Image>();
                    heartIcon.sprite = heartSprite;
                    heartIcon.preserveAspect = true;
                    heartIcon.raycastTarget = false;
                    textLeft = FLOAT_HEART_SIZE + 6f;
                }
            }

            // 单行框高 ≥ 字号×1.45+4：28 号 → 45，14 号 → 25（框不够时 TMP Ellipsis 会清空整行）。
            TextMeshProUGUI valueText = CreateFloatLabel("Value", popup.transform, (delta > 0 ? "+" : "") + delta,
                FLOAT_VALUE_FONT_SIZE, new Vector2(textLeft, 10f), new Vector2(180f, 46f),
                delta > 0 ? BossRushUIColors.SuccessText : BossRushUIColors.DangerText);
            valueText.fontStyle = FontStyles.Bold;

            CreateFloatLabel("Progress", popup.transform, BuildProgressLine(npcId),
                FLOAT_PROGRESS_FONT_SIZE, new Vector2(textLeft, -19f), new Vector2(200f, 25f),
                BossRushUIColors.TextSecondary);
            return popup;
        }

        private static TextMeshProUGUI CreateFloatLabel(string name, Transform parent, string text, float fontSize,
            Vector2 position, Vector2 size, Color color)
        {
            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), position, size, new Vector2(0f, 0.5f));
            TextMeshProUGUI label = ZombieModeUIHelper.CreateTMPText(obj, text, fontSize, TextAlignmentOptions.MidlineLeft, color);
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.margin = Vector4.zero;
            BossRushUIKit.ApplyWorldTextOutline(label);
            return label;
        }

        /// <summary>「Lv.3 · 120/300」；满级写「Lv.10 · 满级」。</summary>
        private static string BuildProgressLine(string npcId)
        {
            int level = AffinityManager.GetLevel(npcId);
            int progress;
            int required;
            AffinityManager.GetLevelProgressDetails(npcId, out progress, out required);
            if (required <= 0)
            {
                return L10n.T("Lv." + level + " · 满级", "Lv." + level + " · MAX");
            }
            return "Lv." + level + " · " + progress + "/" + required;
        }

        /// <summary>
        /// 显示等级提升通知：升级、解锁、折扣合成一条多行消息（UD-39 / UD-42），配官方升级音效与 NPC 冒心。
        /// </summary>
        public static void ShowLevelUpNotification(string npcId, int newLevel)
        {
            try
            {
                INPCAffinityConfig config = AffinityManager.GetNPCConfig(npcId);
                string npcName = config?.DisplayName ?? npcId;
                HashSet<string> shownMessages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<string> lines = new List<string>();

                Action<string> pushUnique = (notification) =>
                {
                    if (string.IsNullOrWhiteSpace(notification))
                    {
                        return;
                    }

                    string normalized = notification.Trim();
                    if (shownMessages.Add(normalized))
                    {
                        lines.Add(normalized);
                    }
                };

                string message = L10n.T(
                    npcName + " 好感度提升到 Lv." + newLevel + "！",
                    npcName + " affinity increased to Lv." + newLevel + "!"
                );
                pushUnique(message);

                bool story3AlreadyTriggered = newLevel == 3 && AffinityManager.HasTriggeredStory(npcId, 3);
                bool story5AlreadyTriggered = newLevel == 5 && AffinityManager.HasTriggeredStory(npcId, 5);
                bool story8AlreadyTriggered = newLevel == 8 && AffinityManager.HasTriggeredStory(npcId, 8);
                bool story10AlreadyTriggered = newLevel == 10 && AffinityManager.HasTriggeredStory(npcId, 10);
                int? discountPercent = null;
                if (config?.DiscountsByLevel != null && config.DiscountsByLevel.TryGetValue(newLevel, out float discount))
                {
                    discountPercent = (int)(discount * 100);
                }
                
                // 检查是否有解锁内容
                if (config?.UnlocksByLevel != null && config.UnlocksByLevel.TryGetValue(newLevel, out string[] unlocks))
                {
                    foreach (string unlock in unlocks)
                    {
                        if (string.IsNullOrWhiteSpace(unlock))
                        {
                            continue;
                        }

                        if (discountPercent.HasValue && unlock.Contains(discountPercent.Value + "%"))
                        {
                            continue;
                        }

                        if ((newLevel == 3 && story3AlreadyTriggered)
                            || (newLevel == 5 && story5AlreadyTriggered)
                            || (newLevel == 8 && story8AlreadyTriggered)
                            || (newLevel == 10 && story10AlreadyTriggered))
                        {
                            continue;
                        }
                        pushUnique(L10n.T("解锁: " + unlock, "Unlocked: " + unlock));
                    }
                }

                // 检查是否有折扣
                if (discountPercent.HasValue)
                {
                    pushUnique(L10n.T(
                        "获得 " + discountPercent.Value + "% 折扣！",
                        "Got " + discountPercent.Value + "% discount!"
                    ));
                }

                // 升级 / 解锁 / 折扣合成一条多行消息：旧版 1–3 条通知排队轮播，和「背包已满」长得一样。
                string merged = string.Join("\n", lines.ToArray());
                try
                {
                    NotificationText.Push(merged);
                }
                catch
                {
                    ModBehaviour.DevLog("[AffinityUI] " + merged);
                }

                // 养成线的节点：官方结算页的升级音（ClosureView 同款），NPC 头顶冒心（同帧重复的心由 NPCBubbleAnimator 去重）。
                IntegrationUIFeedback.PlaySound(IntegrationUIFeedback.SoundLevelUp);
                INPCController controller = ResolveController(npcId);
                if (controller != null)
                {
                    try { controller.ShowLoveHeartBubble(); }
                    catch (Exception heartError) { ModBehaviour.DevLog("[AffinityUI] [WARNING] 升级冒心失败: " + heartError.Message); }
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
                bg.color = BossRushUIColors.Surface;
                bg.raycastTarget = false;
                BossRushUI.ApplyFramedPanelSkin(bg, 12, BossRushUISkinPart.Card);
                
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
                    Image heartIcon = heartObj.AddComponent<Image>();
                    heartIcon.sprite = heartSprite;
                    heartIcon.preserveAspect = true;
                    heartIcon.raycastTarget = false;
                    
                    RectTransform heartRect = heartObj.GetComponent<RectTransform>();
                    heartRect.anchorMin = new Vector2(0, 0.5f);
                    heartRect.anchorMax = new Vector2(0, 0.5f);
                    heartRect.pivot = new Vector2(0, 0.5f);
                    heartRect.anchoredPosition = new Vector2(16f, 8f);
                    heartRect.sizeDelta = new Vector2(HEART_ICON_SIZE, HEART_ICON_SIZE);
                }
                
                // 创建NPC名称文本
                GameObject nameObj = new GameObject("NpcName");
                nameObj.transform.SetParent(affinityPanel.transform, false);
                npcNameText = nameObj.AddComponent<TextMeshProUGUI>();
                BossRushUI.ApplyGameFont(npcNameText);
                npcNameText.fontSize = 20;
                npcNameText.alignment = TextAlignmentOptions.Left;
                npcNameText.color = BossRushUIColors.TextPrimary;
                npcNameText.raycastTarget = false;
                npcNameText.enableWordWrapping = false;
                npcNameText.overflowMode = TextOverflowModes.Ellipsis;
                
                RectTransform nameRect = nameObj.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0, 0.55f);
                nameRect.anchorMax = new Vector2(1, 0.90f);
                nameRect.offsetMin = new Vector2(64f, 0f);
                nameRect.offsetMax = new Vector2(-16f, 0f);
                
                // 创建等级文本（红心图标右侧）
                GameObject levelObj = new GameObject("LevelText");
                levelObj.transform.SetParent(affinityPanel.transform, false);
                levelText = levelObj.AddComponent<TextMeshProUGUI>();
                BossRushUI.ApplyGameFont(levelText);
                levelText.fontSize = 16;
                levelText.alignment = TextAlignmentOptions.Left;
                levelText.color = BossRushUIColors.TextSecondary;
                levelText.raycastTarget = false;
                levelText.enableWordWrapping = false;
                levelText.overflowMode = TextOverflowModes.Ellipsis;
                
                RectTransform levelRect = levelObj.GetComponent<RectTransform>();
                levelRect.anchorMin = new Vector2(0, 0.28f);
                levelRect.anchorMax = new Vector2(1, 0.55f);
                // 如果有红心图标，文本向右偏移
                float leftOffset = 64f;
                levelRect.offsetMin = new Vector2(leftOffset, 0);
                levelRect.offsetMax = new Vector2(-16f, 0);
                
                // 创建进度条背景
                GameObject progressBgObj = new GameObject("ProgressBg");
                progressBgObj.transform.SetParent(affinityPanel.transform, false);
                Image progressBg = progressBgObj.AddComponent<Image>();
                progressBg.color = BossRushUIColors.SurfaceRaised;
                progressBg.raycastTarget = false;
                BossRushUI.ApplyPanelSkin(progressBg, 3);
                
                RectTransform progressBgRect = progressBgObj.GetComponent<RectTransform>();
                progressBgRect.anchorMin = new Vector2(0.05f, 0.12f);
                progressBgRect.anchorMax = new Vector2(0.95f, 0.20f);
                progressBgRect.offsetMin = Vector2.zero;
                progressBgRect.offsetMax = Vector2.zero;
                
                // 创建进度条（使用粉红色，与红心呼应）
                GameObject progressObj = new GameObject("ProgressBar");
                progressObj.transform.SetParent(progressBgObj.transform, false);
                progressBar = progressObj.AddComponent<Image>();
                progressBar.color = new Color(1f, 0.4f, 0.5f, 1f);  // 粉红色
                progressBar.raycastTarget = false;
                // Filled 必须有 sprite：sprite 为 null 时 Image.OnPopulateMesh 会退回整块矩形，
                // fillAmount 被完全忽略——好感度条会永远显示满格。
                // 这里只能赋纯色底图，不能走 ApplyPanelSkin：那会把 type 改回 Sliced。
                progressBar.sprite = BossRushUI.GetSolidSprite();
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
                if (NPCUIAssetCache.TryGetSprite("broken_heart", out heartSprite, "heart_0", "heart_0.png"))
                {
                    ModBehaviour.DevLog("[AffinityUI] 红心图标加载成功");
                }
                else
                {
                    ModBehaviour.DevLog("[AffinityUI] 未能加载红心图标 heart_0（broken_heart）");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffinityUI] 加载红心图标失败: " + e.Message);
            }
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
            if (_floatCanvas != null)
            {
                UnityEngine.Object.Destroy(_floatCanvas.gameObject);
                _floatCanvas = null;
            }
            _interactionController = null;
            levelText = null;
            progressBar = null;
            npcNameText = null;
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

    /// <summary>
    /// 好感变化浮字的动效（UD-41）。世界锚点每帧投到画布上，NPC 走动时浮字跟着走。
    /// 0–0.15 秒从 1.25 倍 EaseOut 落回 1；0.9 秒内 EaseOut 上浮 56px；0.6 秒起 0.6 秒 SmoothStep 淡出，然后销毁。
    /// 走 unscaled 时间：暂停菜单开着时停推进；官方界面（背包 / 地图 / 对话）打开时隐藏，关掉后接着播。
    /// 一次性、约 1.2 秒，不是常驻 HUD。
    /// </summary>
    internal sealed class AffinityFloatText : MonoBehaviour
    {
        private const float PopSeconds = 0.15f;
        private const float PopScale = 1.25f;
        private const float RiseSeconds = 0.9f;
        private const float RisePixels = 56f;
        private const float FadeStart = 0.6f;
        private const float FadeSeconds = 0.6f;

        private RectTransform rect;
        private RectTransform canvasRect;
        private CanvasGroup group;
        private Transform anchor;
        private bool followsAnchor;
        private float anchorHeight;
        private Vector2 screenOffset;
        private Vector2 fallbackPosition;
        private Camera cachedCamera;
        private float elapsed;

        internal void Begin(RectTransform canvasRectTransform, Transform worldAnchor, float height, Vector2 offset, Vector2 fallback)
        {
            rect = transform as RectTransform;
            canvasRect = canvasRectTransform;
            anchor = worldAnchor;
            followsAnchor = worldAnchor != null;
            anchorHeight = height;
            screenOffset = offset;
            fallbackPosition = fallback;
            group = gameObject.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = gameObject.AddComponent<CanvasGroup>();
            }
            group.blocksRaycasts = false;
            group.interactable = false;
            elapsed = 0f;
            Apply();
        }

        private void Update()
        {
            if (BossRushUI.IsGamePaused())
            {
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            if (elapsed >= FadeStart + FadeSeconds)
            {
                Destroy(gameObject);
                return;
            }
            Apply();
        }

        private void Apply()
        {
            if (rect == null || group == null)
            {
                return;
            }

            bool visible = !BossRushUI.IsOfficialHudHidden();
            Vector2 basePosition = fallbackPosition;
            if (followsAnchor)
            {
                if (anchor == null)
                {
                    // NPC 在浮字播放途中被销毁（切图 / 离婚重刷）：直接收掉
                    Destroy(gameObject);
                    return;
                }
                if (!TryProjectAnchor(out basePosition))
                {
                    visible = false;
                }
                else
                {
                    basePosition += screenOffset;
                }
            }

            float rise = RisePixels * BossRushUI.EaseOut(elapsed / RiseSeconds);
            rect.anchoredPosition = basePosition + new Vector2(0f, rise);
            float pop = BossRushUI.EaseOut(elapsed / PopSeconds);
            rect.localScale = Vector3.one * Mathf.Lerp(PopScale, 1f, pop);
            float alpha = 1f - BossRushUI.SmoothStep((elapsed - FadeStart) / FadeSeconds);
            group.alpha = visible ? alpha : 0f;
        }

        private bool TryProjectAnchor(out Vector2 local)
        {
            local = Vector2.zero;
            if (cachedCamera == null)
            {
                cachedCamera = Camera.main;
                if (cachedCamera == null)
                {
                    return false;
                }
            }
            Vector3 screen = cachedCamera.WorldToScreenPoint(anchor.position + Vector3.up * anchorHeight);
            if (screen.z <= 0f || canvasRect == null)
            {
                return false;
            }
            // 总览画布：屏幕像素 → 画布本地坐标不需要相机。画布根的轴心在中心，与浮字的中心锚点同一原点。
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
        }
    }
}
