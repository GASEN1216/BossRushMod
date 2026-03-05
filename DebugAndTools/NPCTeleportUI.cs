// ============================================================================
// NPCTeleportUI.cs - NPC传送UI系统
// ============================================================================
// 模块说明：
//   按F12弹出UI面板，显示当前地图上所有NPC，可选择传送到指定NPC身边
//   - 列出快递员、哥布林等NPC
//   - 未在当前地图的NPC显示为灰色不可选
//   - 点击NPC名称传送到该NPC身边
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>
    /// NPC传送UI系统 - ModBehaviour 的 partial class
    /// </summary>
    public partial class ModBehaviour
    {
        // UI相关变量
        private static GameObject npcTeleportUIRoot = null;
        private static bool npcTeleportUIVisible = false;
        
        /// <summary>
        /// 切换NPC传送UI显示状态
        /// </summary>
        private void ToggleNPCTeleportUI()
        {
            if (npcTeleportUIVisible)
            {
                HideNPCTeleportUI();
            }
            else
            {
                ShowNPCTeleportUI();
            }
        }
        
        /// <summary>
        /// 显示NPC传送UI
        /// </summary>
        private void ShowNPCTeleportUI()
        {
            try
            {
                // 如果UI不存在，创建它
                if (npcTeleportUIRoot == null)
                {
                    CreateNPCTeleportUI();
                }
                
                if (npcTeleportUIRoot != null)
                {
                    npcTeleportUIRoot.SetActive(true);
                    npcTeleportUIVisible = true;
                    
                    // 暂停游戏
                    Time.timeScale = 0f;
                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;
                    
                    // 禁用游戏输入，阻止 InputManager 更新鼠标状态和玩家射击
                    InputManager.DisableInput(npcTeleportUIRoot);
                    
                    // 刷新NPC列表
                    RefreshNPCList();
                    
                    DevLog("[NPCTeleportUI] UI已显示，游戏已暂停，输入已禁用");
                }
            }
            catch (Exception e)
            {
                DevLog("[NPCTeleportUI] 显示UI失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 隐藏NPC传送UI
        /// </summary>
        private void HideNPCTeleportUI()
        {
            try
            {
                if (npcTeleportUIRoot != null)
                {
                    // 恢复游戏输入（必须在隐藏UI之前调用）
                    InputManager.ActiveInput(npcTeleportUIRoot);
                    
                    npcTeleportUIRoot.SetActive(false);
                    npcTeleportUIVisible = false;
                    
                    // 恢复游戏
                    Time.timeScale = 1f;
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    
                    DevLog("[NPCTeleportUI] UI已隐藏，游戏已恢复，输入已启用");
                }
            }
            catch (Exception e)
            {
                DevLog("[NPCTeleportUI] 隐藏UI失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 创建NPC传送UI
        /// </summary>
        private void CreateNPCTeleportUI()
        {
            try
            {
                // 创建Canvas
                npcTeleportUIRoot = new GameObject("NPCTeleportUI");
                Canvas canvas = npcTeleportUIRoot.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000; // 确保在最上层
                
                CanvasScaler scaler = npcTeleportUIRoot.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                
                npcTeleportUIRoot.AddComponent<GraphicRaycaster>();
                
                // 创建背景遮罩
                GameObject bgMask = new GameObject("BackgroundMask");
                bgMask.transform.SetParent(npcTeleportUIRoot.transform, false);
                
                RectTransform bgRect = bgMask.AddComponent<RectTransform>();
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.sizeDelta = Vector2.zero;
                
                Image bgImage = bgMask.AddComponent<Image>();
                bgImage.color = new Color(0, 0, 0, 0.7f); // 半透明黑色
                
                // 点击背景关闭UI
                Button bgButton = bgMask.AddComponent<Button>();
                bgButton.onClick.AddListener(() => HideNPCTeleportUI());
                
                // 创建面板
                GameObject panel = new GameObject("Panel");
                panel.transform.SetParent(npcTeleportUIRoot.transform, false);
                
                RectTransform panelRect = panel.AddComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = new Vector2(400, 500);
                
                Image panelImage = panel.AddComponent<Image>();
                panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
                
                // 创建标题
                GameObject title = new GameObject("Title");
                title.transform.SetParent(panel.transform, false);
                
                RectTransform titleRect = title.AddComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0, 1);
                titleRect.anchorMax = new Vector2(1, 1);
                titleRect.pivot = new Vector2(0.5f, 1);
                titleRect.anchoredPosition = new Vector2(0, -10);
                titleRect.sizeDelta = new Vector2(-20, 40);
                
                Text titleText = title.AddComponent<Text>();
                titleText.text = "传送到NPC";
                titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                titleText.fontSize = 24;
                titleText.alignment = TextAnchor.MiddleCenter;
                titleText.color = Color.white;
                
                // 创建滚动视图
                GameObject scrollView = new GameObject("ScrollView");
                scrollView.transform.SetParent(panel.transform, false);
                
                RectTransform scrollRect = scrollView.AddComponent<RectTransform>();
                scrollRect.anchorMin = new Vector2(0, 0);
                scrollRect.anchorMax = new Vector2(1, 1);
                scrollRect.pivot = new Vector2(0.5f, 0.5f);
                scrollRect.anchoredPosition = new Vector2(0, -30);
                scrollRect.sizeDelta = new Vector2(-20, -80);
                
                ScrollRect scrollComponent = scrollView.AddComponent<ScrollRect>();
                scrollComponent.horizontal = false;
                scrollComponent.vertical = true;
                
                // 创建内容容器
                GameObject content = new GameObject("Content");
                content.transform.SetParent(scrollView.transform, false);
                
                RectTransform contentRect = content.AddComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0, 1);
                contentRect.anchorMax = new Vector2(1, 1);
                contentRect.pivot = new Vector2(0.5f, 1);
                contentRect.sizeDelta = new Vector2(0, 0);
                
                VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 10;
                layout.padding = new RectOffset(10, 10, 10, 10);
                layout.childControlHeight = false;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
                
                ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                
                scrollComponent.content = contentRect;
                
                // 创建关闭按钮
                GameObject closeBtn = new GameObject("CloseButton");
                closeBtn.transform.SetParent(panel.transform, false);
                
                RectTransform closeBtnRect = closeBtn.AddComponent<RectTransform>();
                closeBtnRect.anchorMin = new Vector2(0.5f, 0);
                closeBtnRect.anchorMax = new Vector2(0.5f, 0);
                closeBtnRect.pivot = new Vector2(0.5f, 0);
                closeBtnRect.anchoredPosition = new Vector2(0, 10);
                closeBtnRect.sizeDelta = new Vector2(150, 40);
                
                Image closeBtnImage = closeBtn.AddComponent<Image>();
                closeBtnImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                
                Button closeBtnButton = closeBtn.AddComponent<Button>();
                closeBtnButton.onClick.AddListener(() => HideNPCTeleportUI());
                
                GameObject closeBtnText = new GameObject("Text");
                closeBtnText.transform.SetParent(closeBtn.transform, false);
                
                RectTransform closeBtnTextRect = closeBtnText.AddComponent<RectTransform>();
                closeBtnTextRect.anchorMin = Vector2.zero;
                closeBtnTextRect.anchorMax = Vector2.one;
                closeBtnTextRect.sizeDelta = Vector2.zero;
                
                Text closeBtnTextComponent = closeBtnText.AddComponent<Text>();
                closeBtnTextComponent.text = "关闭 (ESC)";
                closeBtnTextComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                closeBtnTextComponent.fontSize = 18;
                closeBtnTextComponent.alignment = TextAnchor.MiddleCenter;
                closeBtnTextComponent.color = Color.white;
                
                DontDestroyOnLoad(npcTeleportUIRoot);
                npcTeleportUIRoot.SetActive(false);
                
                DevLog("[NPCTeleportUI] UI创建成功");
            }
            catch (Exception e)
            {
                DevLog("[NPCTeleportUI] 创建UI失败: " + e.Message);
            }
        }
        
        // NPC字段名 → 显示名称的映射（已知NPC的友好名称）
        private static readonly Dictionary<string, string> npcDisplayNames = new Dictionary<string, string>
        {
            { "courierNPCInstance", "快递员 (阿稳)" },
            { "goblinNPCInstance", "哥布林 (叮当)" },
            { "nurseNPCInstance", "护士 (羽织)" },
        };

        /// <summary>
        /// 根据字段名生成NPC显示名称
        /// 优先使用已知映射，未知的则从字段名提取（去掉NPCInstance后缀，首字母大写）
        /// </summary>
        private static string GetNPCDisplayName(string fieldName)
        {
            if (npcDisplayNames.TryGetValue(fieldName, out string displayName))
            {
                return displayName;
            }
            // 从字段名提取: 例如 "someNewNPCInstance" → "SomeNew"
            string name = fieldName;
            if (name.EndsWith("NPCInstance"))
            {
                name = name.Substring(0, name.Length - "NPCInstance".Length);
            }
            if (name.Length > 0)
            {
                name = char.ToUpper(name[0]) + name.Substring(1);
            }
            return name + " NPC";
        }

        /// <summary>
        /// 动态刷新NPC列表 - 通过反射扫描所有以NPCInstance结尾的GameObject字段
        /// 新增NPC只需遵循 xxxNPCInstance 命名约定即可自动出现
        /// </summary>
        private void RefreshNPCList()
        {
            try
            {
                if (npcTeleportUIRoot == null) return;

                // 查找Content容器
                Transform content = npcTeleportUIRoot.transform.Find("Panel/ScrollView/Content");
                if (content == null)
                {
                    DevLog("[NPCTeleportUI] 未找到Content容器");
                    return;
                }

                // 清空现有按钮
                foreach (Transform child in content)
                {
                    Destroy(child.gameObject);
                }

                // 通过反射动态查找所有 *NPCInstance 字段
                FieldInfo[] fields = typeof(ModBehaviour).GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                int totalCount = 0;
                int activeCount = 0;

                foreach (FieldInfo field in fields.OrderBy(f => f.Name))
                {
                    // 匹配命名约定: xxxNPCInstance，类型为 GameObject
                    if (!field.Name.EndsWith("NPCInstance")) continue;
                    if (field.FieldType != typeof(GameObject)) continue;

                    GameObject npcObj = field.GetValue(this) as GameObject;
                    bool exists = npcObj != null;
                    string displayName = GetNPCDisplayName(field.Name);

                    // 捕获当前循环变量，避免闭包问题
                    GameObject capturedObj = npcObj;
                    string capturedName = displayName;

                    CreateNPCButton(content, displayName, exists, () => TeleportToNPC(capturedObj, capturedName));

                    totalCount++;
                    if (exists) activeCount++;

                    DevLog("[NPCTeleportUI] 检测到NPC: " + displayName + " (" + field.Name + ") - " + (exists ? "存在" : "未刷新"));
                }

                DevLog("[NPCTeleportUI] NPC列表已刷新 - 共" + totalCount + "个NPC，" + activeCount + "个在线");
            }
            catch (Exception e)
            {
                DevLog("[NPCTeleportUI] 刷新NPC列表失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 创建NPC按钮
        /// </summary>
        private void CreateNPCButton(Transform parent, string npcName, bool isActive, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btnObj = new GameObject("NPCButton_" + npcName);
            btnObj.transform.SetParent(parent, false);
            
            RectTransform btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(0, 50);
            
            Image btnImage = btnObj.AddComponent<Image>();
            btnImage.color = isActive ? new Color(0.2f, 0.5f, 0.2f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
            
            Button btn = btnObj.AddComponent<Button>();
            btn.interactable = isActive;
            
            if (isActive)
            {
                btn.onClick.AddListener(() =>
                {
                    onClick?.Invoke();
                    HideNPCTeleportUI();
                });
            }
            
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            
            Text text = textObj.AddComponent<Text>();
            text.text = npcName + (isActive ? "" : " (未刷新)");
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 20;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = isActive ? Color.white : new Color(0.5f, 0.5f, 0.5f, 1f);
        }
        
        /// <summary>
        /// 通用传送方法 - 传送玩家到指定NPC身边
        /// </summary>
        private void TeleportToNPC(GameObject npcInstance, string npcName)
        {
            try
            {
                if (npcInstance == null)
                {
                    DevLog("[NPCTeleportUI] " + npcName + " NPC不存在");
                    return;
                }

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                {
                    DevLog("[NPCTeleportUI] 未找到玩家 CharacterMainControl");
                    return;
                }

                // 获取NPC位置
                Vector3 npcPos = npcInstance.transform.position;

                // 计算传送位置（NPC前方2米）
                Vector3 targetPos = npcPos + npcInstance.transform.forward * 2f;

                // 使用Raycast修正到地面
                RaycastHit hit;
                if (Physics.Raycast(targetPos + Vector3.up * 1f, Vector3.down, out hit, 5f))
                {
                    targetPos = hit.point + new Vector3(0f, 0.1f, 0f);
                }

                // 执行传送
                main.transform.position = targetPos;
                DevLog("[NPCTeleportUI] 已将玩家传送到" + npcName + "身边，位置: " + targetPos);
            }
            catch (Exception e)
            {
                DevLog("[NPCTeleportUI] 传送到" + npcName + "失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// LateUpdate 中强制暂停和鼠标状态（在所有 Update 之后执行，确保覆盖游戏的设置）
        /// </summary>
        private void NPCTeleportUILateUpdate()
        {
            if (npcTeleportUIVisible)
            {
                Time.timeScale = 0f;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }
    }
}
