using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Duckov;
using Duckov.UI;
using Duckov.Economy;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>
    /// 属性快照，用于记录重铸前的属性值
    /// </summary>
    public class PropertySnapshot
    {
        public string Key;
        public float Value;
        public ModifierType Type;
    }

    /// <summary>
    /// 重铸UI管理器 - 通过修改原版分解UI实现
    /// </summary>
    public static class ReforgeUIManager
    {
        // ============================================================================
        // 缓存的反射 FieldInfo（性能优化）
        // ============================================================================
        private static FieldInfo _detailsDisplayField;
        private static FieldInfo _propertiesParentField;
        private static FieldInfo _cannotDecomposeField;
        
        private static FieldInfo DetailsDisplayField
        {
            get
            {
                if (_detailsDisplayField == null)
                    _detailsDisplayField = typeof(ItemDecomposeView).GetField("detailsDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                return _detailsDisplayField;
            }
        }
        
        private static FieldInfo PropertiesParentField
        {
            get
            {
                if (_propertiesParentField == null)
                    _propertiesParentField = typeof(ItemDetailsDisplay).GetField("propertiesParent", BindingFlags.NonPublic | BindingFlags.Instance);
                return _propertiesParentField;
            }
        }
        
        private static FieldInfo CannotDecomposeField
        {
            get
            {
                if (_cannotDecomposeField == null)
                    _cannotDecomposeField = typeof(ItemDecomposeView).GetField("cannotDecomposeIndicator", BindingFlags.NonPublic | BindingFlags.Instance);
                return _cannotDecomposeField;
            }
        }
        
        // 是否处于重铸模式
        private static bool isReforgeMode = false;
        
        // 当前哥布林控制器
        private static GoblinNPCController currentController;
        
        // 原始属性快照
        private static List<PropertySnapshot> originalProperties = new List<PropertySnapshot>();
        
        // 当前选中的物品
        private static Item selectedItem;
        
        // 当前投入金钱
        private static int currentMoney = 0;
        
        // UI组件引用（通过反射获取）
        private static ItemDecomposeView decomposeView;
        private static object countSliderObj;  // DecomposeSlider对象
        private static Slider moneySlider;
        private static TextMeshProUGUI probabilityText;
        private static Button reforgeButton;
        private static GameObject resultDisplayObj;
        private static TextMeshProUGUI sliderValueText;
        private static TextMeshProUGUI sliderMinText;
        private static TextMeshProUGUI sliderMaxText;
        private static TextMeshProUGUI targetNameDisplay;
        private static object detailsDisplayObj;  // ItemDetailsDisplay对象
        private static GameObject noItemSelectedIndicator;
        
        // 是否正在重铸
        private static bool isReforging = false;
        
        // 缓存的预制体属性（选择物品时获取，避免重铸时重复获取）
        private static Dictionary<string, float> cachedPrefabModifiers = new Dictionary<string, float>();
        private static Dictionary<string, float> cachedPrefabStats = new Dictionary<string, float>();
        private static Dictionary<string, float> cachedPrefabVariables = new Dictionary<string, float>();
        
        /// <summary>
        /// 打开重铸UI（复用原版分解UI）
        /// </summary>
        public static void OpenUI(GoblinNPCController controller)
        {
            ModBehaviour.DevLog("[ReforgeUI] OpenUI 调用");
            
            currentController = controller;
            isReforgeMode = true;
            originalProperties.Clear();
            selectedItem = null;
            currentMoney = 0;
            
            // 打开原版分解UI
            var instance = ItemDecomposeView.Instance;
            if (instance != null)
            {
                instance.Open(null);
            }
            
            // 延迟修改UI（等待UI初始化完成）
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StartCoroutine(ModifyUIDelayed());
            }
            
            ModBehaviour.DevLog("[ReforgeUI] UI已打开（重铸模式）");
        }
        
        /// <summary>
        /// 关闭UI
        /// </summary>
        public static void CloseUI()
        {
            ModBehaviour.DevLog("[ReforgeUI] CloseUI 调用");
            
            isReforgeMode = false;
            originalProperties.Clear();
            selectedItem = null;
            
            // 关闭原版UI
            if (decomposeView != null && decomposeView.open)
            {
                decomposeView.Close();
            }
            
            // 通知哥布林对话结束
            if (currentController != null)
            {
                currentController.EndDialogue();
                currentController = null;
            }
            
            ModBehaviour.DevLog("[ReforgeUI] UI已关闭");
        }
        
        // UI监控器
        private static ReforgeUIMonitor uiMonitor;
        
        /// <summary>
        /// 延迟修改UI
        /// </summary>
        private static System.Collections.IEnumerator ModifyUIDelayed()
        {
            yield return new WaitForSeconds(0.1f);

            ModifyDecomposeUI();

            // 创建UI监控器
            if (decomposeView != null)
            {
                if (uiMonitor == null)
                {
                    GameObject monitorObj = new GameObject("ReforgeUIMonitor");
                    uiMonitor = monitorObj.AddComponent<ReforgeUIMonitor>();
                    GameObject.DontDestroyOnLoad(monitorObj);
                }
                uiMonitor.SetWatchedView(decomposeView);
            }

            // 再等一帧，确保原版OnOpen已经执行完毕，然后再次应用我们的修改
            yield return null;
            ReapplyModifications();

            // DevMode: 输出UI详细信息
            if (ModBehaviour.DevModeEnabled)
            {
                DumpUIInfo();
            }
        }
        
        /// <summary>
        /// [DevMode] 输出UI详细信息，用于调试和查找需要修改的文本
        /// </summary>
        private static void DumpUIInfo()
        {
            if (decomposeView == null)
            {
                ModBehaviour.DevLog("[ReforgeUI] [DUMP] decomposeView 为空，无法输出UI信息");
                return;
            }

            ModBehaviour.DevLog("================================================================================");
            ModBehaviour.DevLog("[ReforgeUI] [DUMP] 开始输出重铸UI详细信息");
            ModBehaviour.DevLog("================================================================================");

            try
            {
                // 1. 输出UI根节点信息
                ModBehaviour.DevLog("[ReforgeUI] [DUMP] UI根节点: " + decomposeView.gameObject.name);
                ModBehaviour.DevLog("[ReforgeUI] [DUMP] UI路径: " + GetGameObjectPath(decomposeView.transform));

                // 2. 输出所有反射获取的字段
                ModBehaviour.DevLog("--------------------------------------------------------------------------------");
                ModBehaviour.DevLog("[ReforgeUI] [DUMP] === 反射字段信息 ===");
                DumpReflectionFields();

                // 3. 输出所有文本组件（重点）
                ModBehaviour.DevLog("--------------------------------------------------------------------------------");
                ModBehaviour.DevLog("[ReforgeUI] [DUMP] === 所有文本组件 (TextMeshProUGUI) ===");
                DumpAllTexts(decomposeView.transform, 0);

                // 4. 输出所有按钮
                ModBehaviour.DevLog("--------------------------------------------------------------------------------");
                ModBehaviour.DevLog("[ReforgeUI] [DUMP] === 所有按钮 (Button) ===");
                DumpAllButtons(decomposeView.transform);

                // 5. 输出UI层级结构
                ModBehaviour.DevLog("--------------------------------------------------------------------------------");
                ModBehaviour.DevLog("[ReforgeUI] [DUMP] === UI层级结构 ===");
                DumpUIHierarchy(decomposeView.transform, 0);

                ModBehaviour.DevLog("================================================================================");
                ModBehaviour.DevLog("[ReforgeUI] [DUMP] UI信息输出完成");
                ModBehaviour.DevLog("================================================================================");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [DUMP] [ERROR] 输出UI信息失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取GameObject的完整路径
        /// </summary>
        private static string GetGameObjectPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        /// <summary>
        /// [DevMode] 输出反射获取的字段信息
        /// </summary>
        private static void DumpReflectionFields()
        {
            Type viewType = typeof(ItemDecomposeView);
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

            FieldInfo[] fields = viewType.GetFields(flags);
            foreach (var field in fields)
            {
                try
                {
                    object value = field.GetValue(decomposeView);
                    string valueStr = value == null ? "null" : value.ToString();

                    // 如果是Unity组件，尝试获取更多信息
                    if (value is Component comp)
                    {
                        valueStr = string.Format("{0} (GameObject: {1})", value.GetType().Name, comp.gameObject.name);
                    }
                    else if (value is GameObject go)
                    {
                        valueStr = string.Format("GameObject: {0}", go.name);
                    }

                    ModBehaviour.DevLog(string.Format("[ReforgeUI] [DUMP]   {0} ({1}): {2}", field.Name, field.FieldType.Name, valueStr));
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(string.Format("[ReforgeUI] [DUMP]   {0}: [读取失败: {1}]", field.Name, e.Message));
                }
            }
        }

        /// <summary>
        /// [DevMode] 递归输出所有文本组件
        /// </summary>
        private static void DumpAllTexts(Transform parent, int depth)
        {
            string indent = new string(' ', depth * 2);

            // 检查TMP文本
            var tmp = parent.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                string text = string.IsNullOrEmpty(tmp.text) ? "[空]" : tmp.text;
                bool isActive = tmp.gameObject.activeInHierarchy;
                string activeStr = isActive ? "" : " [隐藏]";
                ModBehaviour.DevLog(string.Format("[ReforgeUI] [DUMP] {0}[TMP] {1}{2}: \"{3}\"",
                    indent, parent.name, activeStr, text));
                ModBehaviour.DevLog(string.Format("[ReforgeUI] [DUMP] {0}       路径: {1}",
                    indent, GetGameObjectPath(parent)));
            }

            // 检查普通Text
            var uiText = parent.GetComponent<Text>();
            if (uiText != null)
            {
                string text = string.IsNullOrEmpty(uiText.text) ? "[空]" : uiText.text;
                bool isActive = uiText.gameObject.activeInHierarchy;
                string activeStr = isActive ? "" : " [隐藏]";
                ModBehaviour.DevLog(string.Format("[ReforgeUI] [DUMP] {0}[Text] {1}{2}: \"{3}\"",
                    indent, parent.name, activeStr, text));
                ModBehaviour.DevLog(string.Format("[ReforgeUI] [DUMP] {0}       路径: {1}",
                    indent, GetGameObjectPath(parent)));
            }

            // 递归子物体
            for (int i = 0; i < parent.childCount; i++)
            {
                DumpAllTexts(parent.GetChild(i), depth + 1);
            }
        }

        /// <summary>
        /// [DevMode] 输出所有按钮
        /// </summary>
        private static void DumpAllButtons(Transform parent)
        {
            Button[] buttons = parent.GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons)
            {
                string btnName = btn.gameObject.name;
                bool isActive = btn.gameObject.activeInHierarchy;
                bool isInteractable = btn.interactable;
                string activeStr = isActive ? "激活" : "隐藏";
                string interactStr = isInteractable ? "可交互" : "不可交互";

                ModBehaviour.DevLog(string.Format("[ReforgeUI] [DUMP]   按钮: {0} [{1}, {2}]",
                    btnName, activeStr, interactStr));
                ModBehaviour.DevLog(string.Format("[ReforgeUI] [DUMP]         路径: {0}",
                    GetGameObjectPath(btn.transform)));

                // 输出按钮下的文本
                var btnTexts = btn.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var t in btnTexts)
                {
                    if (!string.IsNullOrEmpty(t.text))
                    {
                        ModBehaviour.DevLog(string.Format("[ReforgeUI] [DUMP]         文本: \"{0}\" (组件: {1})",
                            t.text, t.gameObject.name));
                    }
                }
            }
        }

        /// <summary>
        /// [DevMode] 输出UI层级结构（简化版，只输出前3层）
        /// </summary>
        private static void DumpUIHierarchy(Transform parent, int depth)
        {
            if (depth > 3) return; // 限制深度避免输出过多

            string indent = new string(' ', depth * 2);
            bool isActive = parent.gameObject.activeSelf;
            string activeStr = isActive ? "" : " [inactive]";

            // 收集组件信息
            List<string> components = new List<string>();
            if (parent.GetComponent<Button>() != null) components.Add("Button");
            if (parent.GetComponent<TextMeshProUGUI>() != null) components.Add("TMP");
            if (parent.GetComponent<Text>() != null) components.Add("Text");
            if (parent.GetComponent<Image>() != null) components.Add("Image");
            if (parent.GetComponent<Slider>() != null) components.Add("Slider");

            string compStr = components.Count > 0 ? " [" + string.Join(", ", components.ToArray()) + "]" : "";

            ModBehaviour.DevLog(string.Format("[ReforgeUI] [DUMP] {0}{1}{2}{3}",
                indent, parent.name, activeStr, compStr));

            for (int i = 0; i < parent.childCount; i++)
            {
                DumpUIHierarchy(parent.GetChild(i), depth + 1);
            }
        }

        /// <summary>
        /// 重新应用所有修改（在原版OnOpen之后调用）
        /// </summary>
        private static void ReapplyModifications()
        {
            try
            {
                // 重新设置过滤条件
                ModifyInventoryFilter();
                
                // 重新设置滑块
                ModifySlider();
                
                // 重新设置按钮文本
                if (reforgeButton != null)
                {
                    TextMeshProUGUI buttonText = reforgeButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonText != null)
                    {
                        buttonText.text = "重铸";
                    }
                }
                
                // 更新概率显示
                UpdateProbabilityDisplay();
                
                ModBehaviour.DevLog("[ReforgeUI] 已重新应用所有修改");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 重新应用修改失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 修改分解UI为重铸UI
        /// </summary>
        private static void ModifyDecomposeUI()
        {
            try
            {
                decomposeView = ItemDecomposeView.Instance;
                if (decomposeView == null)
                {
                    ModBehaviour.DevLog("[ReforgeUI] [ERROR] 无法获取ItemDecomposeView实例");
                    return;
                }
                
                ModBehaviour.DevLog("[ReforgeUI] 开始修改UI");
                
                // 0. 修改物品过滤条件 - 只有可重铸物品才能点击
                ModifyInventoryFilter();
                
                // 1. 获取并修改滑块 - 改为金钱滑块
                ModifySlider();
                
                // 2. 隐藏原版结果显示，添加概率显示
                ModifyResultDisplay();
                
                // 3. 修改分解按钮为重铸按钮
                ModifyDecomposeButton();
                
                // 4. 修改标题文本
                ModifyTitleText();
                
                // 5. 移除原版事件监听器，防止原版方法被调用
                RemoveOriginalEventListeners();
                
                // 6. 获取其他UI组件引用
                GetAdditionalUIReferences();
                
                // 7. 订阅我们自己的物品选择事件
                ItemUIUtilities.OnSelectionChanged += OnItemSelectionChanged;
                
                ModBehaviour.DevLog("[ReforgeUI] UI修改完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 修改UI失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        // 保存原版事件处理器引用，用于恢复
        private static Action<UIInputEventData> originalOnFastPick;
        
        // 当前正在运行的差异显示协程（用于避免重复）
        private static Coroutine currentDiffCoroutine;
        
        /// <summary>
        /// 移除原版事件监听器（只移除FastPick，保留OnSelectionChanged让UI正常显示）
        /// </summary>
        private static void RemoveOriginalEventListeners()
        {
            try
            {
                // 只移除FastPick，防止快速分解
                MethodInfo onFastPickMethod = typeof(ItemDecomposeView).GetMethod("OnFastPick", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onFastPickMethod != null && decomposeView != null)
                {
                    originalOnFastPick = (Action<UIInputEventData>)Delegate.CreateDelegate(typeof(Action<UIInputEventData>), decomposeView, onFastPickMethod);
                    UIInputManager.OnFastPick -= originalOnFastPick;
                    ModBehaviour.DevLog("[ReforgeUI] 已移除原版OnFastPick监听器");
                }
                
                // 注意：不移除OnSelectionChanged，让原版的Refresh/Setup正常工作来显示UI
                // 我们在延迟协程中覆盖需要修改的部分
                ModBehaviour.DevLog("[ReforgeUI] 保留原版OnSelectionChanged以显示UI");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 移除原版事件监听器失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 恢复原版事件监听器（关闭UI时调用）
        /// </summary>
        private static void RestoreOriginalEventListeners()
        {
            try
            {
                if (originalOnFastPick != null)
                {
                    UIInputManager.OnFastPick += originalOnFastPick;
                    originalOnFastPick = null;
                }
            }
            catch { }
        }
        
        /// <summary>
        /// 获取其他UI组件引用
        /// </summary>
        private static void GetAdditionalUIReferences()
        {
            try
            {
                // 获取targetNameDisplay
                FieldInfo nameField = typeof(ItemDecomposeView).GetField("targetNameDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                if (nameField != null)
                {
                    targetNameDisplay = nameField.GetValue(decomposeView) as TextMeshProUGUI;
                }
                
                // 获取detailsDisplay
                FieldInfo detailsField = typeof(ItemDecomposeView).GetField("detailsDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                if (detailsField != null)
                {
                    detailsDisplayObj = detailsField.GetValue(decomposeView);
                }
                
                // 获取noItemSelectedIndicator
                FieldInfo noItemField = typeof(ItemDecomposeView).GetField("noItemSelectedIndicator", BindingFlags.NonPublic | BindingFlags.Instance);
                if (noItemField != null)
                {
                    noItemSelectedIndicator = noItemField.GetValue(decomposeView) as GameObject;
                }
                
                ModBehaviour.DevLog("[ReforgeUI] 额外UI引用获取完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 获取额外UI引用失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 修改物品过滤条件 - 只有可重铸物品才能点击
        /// </summary>
        private static void ModifyInventoryFilter()
        {
            try
            {
                // 获取characterInventoryDisplay
                FieldInfo charInvField = typeof(ItemDecomposeView).GetField("characterInventoryDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                if (charInvField != null)
                {
                    var charInv = charInvField.GetValue(decomposeView) as InventoryDisplay;
                    if (charInv != null && CharacterMainControl.Main != null)
                    {
                        var inventory = CharacterMainControl.Main.CharacterItem.Inventory;
                        
                        // 诊断日志已移除以提升性能
                        
                        // 重新设置，使用ReforgeSystem.CanReforge作为过滤条件
                        charInv.Setup(
                            inventory, 
                            null, 
                            (Item e) => e == null || ReforgeSystem.CanReforge(e),  // 只有可重铸物品才能操作
                            false, 
                            null
                        );
                        ModBehaviour.DevLog("[ReforgeUI] 角色背包过滤条件已修改");
                    }
                }
                
                // 获取storageDisplay
                FieldInfo storageField = typeof(ItemDecomposeView).GetField("storageDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                if (storageField != null)
                {
                    var storageInv = storageField.GetValue(decomposeView) as InventoryDisplay;
                    if (storageInv != null && PlayerStorage.Inventory != null)
                    {
                        storageInv.Setup(
                            PlayerStorage.Inventory, 
                            null, 
                            (Item e) => e == null || ReforgeSystem.CanReforge(e),
                            false, 
                            null
                        );
                        ModBehaviour.DevLog("[ReforgeUI] 仓库过滤条件已修改");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 修改物品过滤失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 修改滑块为金钱滑块
        /// </summary>
        private static void ModifySlider()
        {
            try
            {
                // 通过反射获取countSlider (DecomposeSlider类型)
                FieldInfo sliderField = typeof(ItemDecomposeView).GetField("countSlider", BindingFlags.NonPublic | BindingFlags.Instance);
                if (sliderField == null)
                {
                    ModBehaviour.DevLog("[ReforgeUI] [WARNING] 无法找到countSlider字段");
                    return;
                }
                
                countSliderObj = sliderField.GetValue(decomposeView);
                if (countSliderObj == null)
                {
                    ModBehaviour.DevLog("[ReforgeUI] [WARNING] countSlider为空");
                    return;
                }
                
                // DecomposeSlider继承自MonoBehaviour
                Component sliderComponent = countSliderObj as Component;
                if (sliderComponent == null) return;
                
                // 获取DecomposeSlider的内部字段
                Type decomposeSliderType = countSliderObj.GetType();
                
                // 获取slider字段
                FieldInfo internalSliderField = decomposeSliderType.GetField("slider", BindingFlags.NonPublic | BindingFlags.Instance);
                if (internalSliderField != null)
                {
                    moneySlider = internalSliderField.GetValue(countSliderObj) as Slider;
                }
                
                // 获取文本字段 (public字段)
                FieldInfo valueTextField = decomposeSliderType.GetField("valueText", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo minTextField = decomposeSliderType.GetField("minText", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo maxTextField = decomposeSliderType.GetField("maxText", BindingFlags.Public | BindingFlags.Instance);
                
                if (valueTextField != null)
                    sliderValueText = valueTextField.GetValue(countSliderObj) as TextMeshProUGUI;
                if (minTextField != null)
                    sliderMinText = minTextField.GetValue(countSliderObj) as TextMeshProUGUI;
                if (maxTextField != null)
                    sliderMaxText = maxTextField.GetValue(countSliderObj) as TextMeshProUGUI;
                
                if (moneySlider != null)
                {
                    // 设置滑块范围为玩家金钱
                    int playerMoney = GetPlayerMoney();
                    moneySlider.minValue = 0;
                    moneySlider.maxValue = playerMoney;
                    moneySlider.value = 0;
                    moneySlider.wholeNumbers = true;
                    
                    // 移除原有监听器，添加新的
                    moneySlider.onValueChanged.RemoveAllListeners();
                    moneySlider.onValueChanged.AddListener(OnMoneySliderChanged);
                    
                    // 设置文本显示
                    if (sliderValueText != null)
                        sliderValueText.text = "0";
                    if (sliderMinText != null)
                        sliderMinText.text = "0";
                    if (sliderMaxText != null)
                        sliderMaxText.text = playerMoney.ToString();
                    
                    ModBehaviour.DevLog("[ReforgeUI] 滑块已修改为金钱滑块，最大值: " + playerMoney);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 修改滑块失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 修改结果显示为概率显示（复用resultDisplay的位置）
        /// </summary>
        private static void ModifyResultDisplay()
        {
            try
            {
                // 通过反射获取resultDisplay
                FieldInfo resultField = typeof(ItemDecomposeView).GetField("resultDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                if (resultField != null)
                {
                    var resultDisplay = resultField.GetValue(decomposeView) as Component;
                    if (resultDisplay != null)
                    {
                        resultDisplayObj = resultDisplay.gameObject;
                        
                        // 在resultDisplay的位置创建概率显示（复用其位置）
                        Transform resultParent = resultDisplay.transform.parent;
                        
                        // 查找或创建概率显示
                        Transform existingProb = resultParent.Find("ReforgeProbability");
                        if (existingProb != null)
                        {
                            probabilityText = existingProb.GetComponent<TextMeshProUGUI>();
                        }
                        else
                        {
                            GameObject probObj = new GameObject("ReforgeProbability");
                            probObj.transform.SetParent(resultParent, false);
                            
                            // 复制resultDisplay的RectTransform设置
                            RectTransform resultRect = resultDisplay.GetComponent<RectTransform>();
                            RectTransform rect = probObj.AddComponent<RectTransform>();
                            if (resultRect != null)
                            {
                                rect.anchorMin = resultRect.anchorMin;
                                rect.anchorMax = resultRect.anchorMax;
                                rect.anchoredPosition = resultRect.anchoredPosition;
                                rect.sizeDelta = resultRect.sizeDelta;
                                rect.pivot = resultRect.pivot;
                            }
                            else
                            {
                                rect.anchoredPosition = Vector2.zero;
                                rect.sizeDelta = new Vector2(300, 50);
                            }
                            
                            // 设置在resultDisplay的同一位置
                            probObj.transform.SetSiblingIndex(resultDisplay.transform.GetSiblingIndex());
                            
                            probabilityText = probObj.AddComponent<TextMeshProUGUI>();
                            probabilityText.fontSize = 24; // 大字体
                            probabilityText.alignment = TextAlignmentOptions.Center;
                            probabilityText.color = Color.white; // 白色字体
                            probabilityText.enableWordWrapping = false;
                            probabilityText.overflowMode = TextOverflowModes.Overflow;
                        }
                        
                        // 隐藏原版结果显示
                        resultDisplayObj.SetActive(false);
                        
                        UpdateProbabilityDisplay();
                        ModBehaviour.DevLog("[ReforgeUI] 概率显示已创建（复用resultDisplay位置）");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 修改结果显示失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 修改分解按钮为重铸按钮
        /// </summary>
        private static void ModifyDecomposeButton()
        {
            try
            {
                // 通过反射获取decomposeButton
                FieldInfo buttonField = typeof(ItemDecomposeView).GetField("decomposeButton", BindingFlags.NonPublic | BindingFlags.Instance);
                if (buttonField != null)
                {
                    reforgeButton = buttonField.GetValue(decomposeView) as Button;
                    if (reforgeButton != null)
                    {
                        // 移除原有点击事件
                        reforgeButton.onClick.RemoveAllListeners();
                        
                        // 添加重铸点击事件
                        reforgeButton.onClick.AddListener(OnReforgeButtonClick);
                        
                        // 修改按钮文字
                        TextMeshProUGUI buttonText = reforgeButton.GetComponentInChildren<TextMeshProUGUI>();
                        if (buttonText != null)
                        {
                            buttonText.text = "重铸";
                        }
                        
                        ModBehaviour.DevLog("[ReforgeUI] 分解按钮已修改为重铸按钮");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 修改按钮失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 修改标题文本
        /// </summary>
        private static void ModifyTitleText()
        {
            try
            {
                // 诊断日志已移除以提升性能
                
                // 查找所有文本组件，替换"分解"为"重铸"
                TextMeshProUGUI[] texts = decomposeView.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var text in texts)
                {
                    string originalText = text.text;
                    // 更全面的替换，包括纯"分解"文本
                    if (originalText == "分解" || originalText == "Decompose")
                    {
                        text.text = originalText == "分解" ? "重铸" : "Reforge";
                        ModBehaviour.DevLog("[ReforgeUI] 按钮文本已修改: " + originalText + " -> " + text.text);
                    }
                    else if (originalText.Contains("分解") || originalText.Contains("Decompose") || 
                        originalText.Contains("选择") || originalText.Contains("Select"))
                    {
                        string newText = originalText
                            .Replace("无法分解", "无法重铸")
                            .Replace("分解", "重铸")
                            .Replace("Decompose", "Reforge")
                            .Replace("请选择要分解的物品", "请选择要重铸的物品")
                            .Replace("选择要分解", "选择要重铸");
                        text.text = newText;
                        ModBehaviour.DevLog("[ReforgeUI] 文本已修改: " + originalText + " -> " + newText);
                    }
                }
                
                // 修改cannotDecomposeIndicator的文本
                FieldInfo cannotField = typeof(ItemDecomposeView).GetField("cannotDecomposeIndicator", BindingFlags.NonPublic | BindingFlags.Instance);
                if (cannotField != null)
                {
                    var indicator = cannotField.GetValue(decomposeView) as GameObject;
                    if (indicator != null)
                    {
                        TextMeshProUGUI[] indicatorTexts = indicator.GetComponentsInChildren<TextMeshProUGUI>(true);
                        foreach (var t in indicatorTexts)
                        {
                            string origText = t.text;
                            if (origText.Contains("分解") || origText.Contains("Decompose") || origText.Contains("无法"))
                            {
                                t.text = origText.Replace("分解", "重铸").Replace("Decompose", "Reforge").Replace("无法分解", "无法重铸");
                                ModBehaviour.DevLog("[ReforgeUI] 指示器文本已修改: " + origText + " -> " + t.text);
                            }
                        }
                    }
                }
                
                // 修改noItemSelectedIndicator的文本
                if (noItemSelectedIndicator != null)
                {
                    TextMeshProUGUI[] noItemTexts = noItemSelectedIndicator.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var t in noItemTexts)
                    {
                        string origText = t.text;
                        if (origText.Contains("分解") || origText.Contains("选择"))
                        {
                            t.text = origText.Replace("分解", "重铸").Replace("请选择要分解的物品", "请选择要重铸的物品");
                            ModBehaviour.DevLog("[ReforgeUI] noItem文本已修改: " + origText + " -> " + t.text);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 修改标题失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 递归输出所有文本组件
        /// </summary>
        private static void LogAllTextsRecursive(Transform parent, int depth)
        {
            string indent = new string(' ', depth * 2);
            
            // 检查TMP文本
            var tmp = parent.GetComponent<TextMeshProUGUI>();
            if (tmp != null && !string.IsNullOrEmpty(tmp.text))
            {
                ModBehaviour.DevLog(string.Format("[ReforgeUI] {0}[TMP] {1}: \"{2}\"", indent, parent.name, tmp.text));
            }
            
            // 检查普通Text
            var text = parent.GetComponent<Text>();
            if (text != null && !string.IsNullOrEmpty(text.text))
            {
                ModBehaviour.DevLog(string.Format("[ReforgeUI] {0}[Text] {1}: \"{2}\"", indent, parent.name, text.text));
            }
            
            // 递归子物体
            for (int i = 0; i < parent.childCount; i++)
            {
                LogAllTextsRecursive(parent.GetChild(i), depth + 1);
            }
        }
        
        /// <summary>
        /// 物品选择改变
        /// </summary>
        private static void OnItemSelectionChanged()
        {
            if (!isReforgeMode) return;
            
            Item newItem = ItemUIUtilities.SelectedItem;
            
            // 切换了物品，清除原属性快照
            originalProperties.Clear();
            selectedItem = newItem;
            
            if (selectedItem != null)
            {
                // 保存新物品的属性快照
                SavePropertySnapshot(selectedItem);
                
                // 检查是否可重铸
                bool canReforge = ReforgeSystem.CanReforge(selectedItem);
                if (reforgeButton != null)
                {
                    reforgeButton.gameObject.SetActive(canReforge);
                }
                
                // 设置物品名称显示
                if (targetNameDisplay != null)
                {
                    targetNameDisplay.text = selectedItem.DisplayName;
                }
                
                // 延迟设置物品详情显示 - 原版会先调用Setup(playerItem)，我们需要在之后覆盖为预制体显示
                // 停止之前的协程以避免重复显示差异
                StopCurrentDiffCoroutine();
                if (ModBehaviour.Instance != null)
                {
                    currentDiffCoroutine = ModBehaviour.Instance.StartCoroutine(SetupDetailsWithPrefabComparisonDelayed(selectedItem));
                }
                
                // 隐藏"请选择物品"提示
                if (noItemSelectedIndicator != null)
                {
                    noItemSelectedIndicator.SetActive(false);
                }
            }
            else
            {
                // 未选中物品
                if (targetNameDisplay != null)
                {
                    targetNameDisplay.text = "-";
                }
                if (noItemSelectedIndicator != null)
                {
                    noItemSelectedIndicator.SetActive(true);
                }
                if (reforgeButton != null)
                {
                    reforgeButton.gameObject.SetActive(false);
                }
            }
            
            // 延迟重置UI状态（原版会在选择后重置滑块，我们需要在之后再次设置）
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StartCoroutine(ResetUIStateDelayed(GetPlayerMoney()));
            }
            
            UpdateProbabilityDisplay();
        }
        
        /// <summary>
        /// 重置UI状态（滑块、按钮、概率显示）
        /// </summary>
        private static void ResetUIState(int maxMoney)
        {
            // 重置滑块
            if (moneySlider != null)
            {
                moneySlider.minValue = 0;
                moneySlider.maxValue = maxMoney;
                moneySlider.value = 0;
                moneySlider.wholeNumbers = true;
                currentMoney = 0;
                
                if (sliderValueText != null)
                    sliderValueText.text = "0";
                if (sliderMinText != null)
                    sliderMinText.text = "0";
                if (sliderMaxText != null)
                    sliderMaxText.text = maxMoney.ToString();
            }
            
            UpdateUIStateCommon();
        }
        
        /// <summary>
        /// 更新UI状态但保留滑块值（用于重铸完成后，方便玩家多次快速重铸）
        /// </summary>
        private static void UpdateUIStateKeepSlider(int maxMoney)
        {
            if (moneySlider != null)
            {
                // 保留当前滑块值，只更新最大值
                float currentValue = moneySlider.value;
                moneySlider.minValue = 0;
                moneySlider.maxValue = maxMoney;
                
                // 如果当前值超过新的最大值，则调整为最大值
                if (currentValue > maxMoney)
                {
                    moneySlider.value = maxMoney;
                    currentMoney = maxMoney;
                }
                else
                {
                    moneySlider.value = currentValue;
                    currentMoney = Mathf.RoundToInt(currentValue);
                }
                
                if (sliderValueText != null)
                    sliderValueText.text = currentMoney.ToString();
                if (sliderMinText != null)
                    sliderMinText.text = "0";
                if (sliderMaxText != null)
                    sliderMaxText.text = maxMoney.ToString();
            }
            
            UpdateUIStateCommon();
        }
        
        /// <summary>
        /// UI状态更新的公共部分（按钮、概率显示等）
        /// </summary>
        private static void UpdateUIStateCommon()
        {
            ResetButtonState();
        }
        
        /// <summary>
        /// 重置按钮状态
        /// </summary>
        private static void ResetButtonState()
        {
            if (reforgeButton != null)
            {
                if (selectedItem != null)
                {
                    bool canReforge = ReforgeSystem.CanReforge(selectedItem);
                    reforgeButton.gameObject.SetActive(canReforge);

                    // 修复按钮下所有文本组件（按钮有两个文本：主文本和InputIndicator中的文本）
                    TextMeshProUGUI[] buttonTexts = reforgeButton.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var buttonText in buttonTexts)
                    {
                        if (buttonText != null && (buttonText.text == "分解" || buttonText.text == "Decompose"))
                        {
                            buttonText.text = "重铸";
                        }
                    }

                    // 隐藏原版"无法分解"提示，并修复其文本
                    try
                    {
                        var cannotField = CannotDecomposeField;
                        if (cannotField != null && decomposeView != null)
                        {
                            var indicator = cannotField.GetValue(decomposeView) as GameObject;
                            if (indicator != null)
                            {
                                indicator.SetActive(!canReforge);

                                // 修复"无法分解"文本
                                TextMeshProUGUI[] indicatorTexts = indicator.GetComponentsInChildren<TextMeshProUGUI>(true);
                                foreach (var t in indicatorTexts)
                                {
                                    if (t.text.Contains("分解"))
                                    {
                                        t.text = t.text.Replace("分解", "重铸");
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    reforgeButton.gameObject.SetActive(false);
                }
            }

            // 修复"请选择要分解的物品"文本
            if (noItemSelectedIndicator != null)
            {
                TextMeshProUGUI[] noItemTexts = noItemSelectedIndicator.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var t in noItemTexts)
                {
                    if (t.text.Contains("分解"))
                    {
                        t.text = t.text.Replace("分解", "重铸");
                    }
                }
            }

            // 确保结果显示被隐藏，概率显示可见
            if (resultDisplayObj != null)
            {
                resultDisplayObj.SetActive(false);
            }
            if (probabilityText != null)
            {
                probabilityText.gameObject.SetActive(true);
            }

            UpdateProbabilityDisplay();
        }
        
        /// <summary>
        /// 延迟重置UI状态（防止原版覆盖）
        /// </summary>
        private static System.Collections.IEnumerator ResetUIStateDelayed(int maxMoney, int frameDelay = 1)
        {
            for (int i = 0; i < frameDelay; i++)
            {
                yield return null;
            }
            ResetUIState(maxMoney);
        }
        
        /// <summary>
        /// 金钱滑块值改变
        /// </summary>
        private static void OnMoneySliderChanged(float value)
        {
            currentMoney = Mathf.RoundToInt(value);
            
            // 更新滑块文本（只显示金额）
            if (sliderValueText != null)
            {
                sliderValueText.text = currentMoney.ToString();
            }
            
            UpdateProbabilityDisplay();
        }
        
        /// <summary>
        /// 更新概率显示
        /// </summary>
        private static void UpdateProbabilityDisplay()
        {
            if (probabilityText == null) return;

            if (selectedItem == null)
            {
                probabilityText.text = "请选择物品";
                probabilityText.color = Color.gray;
                return;
            }

            if (!ReforgeSystem.CanReforge(selectedItem))
            {
                probabilityText.text = "该物品无法重铸";
                probabilityText.color = new Color(1f, 0.5f, 0.5f);
                return;
            }

            // 使用新概率公式计算
            int rarity = GetItemQuality(selectedItem);
            float itemValue = ReforgeSystem.GetItemValue(selectedItem);

            // 计算各个系数
            float rarityFactor = ReforgeSystem.RarityFactor(rarity);      // 品质系数
            float valueFactor = ReforgeSystem.ValueFactor(itemValue);     // 价值系数
            float moneyBonus = ReforgeSystem.MoneyBonus(currentMoney);    // 金钱加成

            // 计算最终概率
            float p = ReforgeSystem.FinalProbability(rarity, itemValue, currentMoney);

            // 根据概率获取颜色
            string probColorHex;
            if (p >= 0.8f)
                probColorHex = "#4DFF4D";  // 绿色
            else if (p >= 0.5f)
                probColorHex = "#FFFF4D";  // 黄色
            else if (p >= 0.3f)
                probColorHex = "#FF994D";  // 橙色
            else
                probColorHex = "#FF4D4D";  // 红色

            // 显示详细公式（每行一个参数，白字显示参数，最后概率公式带颜色）
            // 品质: X (系数: X.XX)
            // 价值: XXXX (系数: X.XX)
            // 投入: XXXX (加成: X.XX)
            // 概率: 0.20×X.XX×X.XX+X.XX = XX%
            probabilityText.text = string.Format(
                "品质: {0} (系数: {1:F2})\n" +
                "价值: {2:F0} (系数: {3:F2})\n" +
                "投入: {4} (加成: {5:F2})\n" +
                "<color={6}>概率: 0.20×{1:F2}×{3:F2}+{5:F2}={7:P0}</color>",
                rarity, rarityFactor,
                itemValue, valueFactor,
                currentMoney, moneyBonus,
                probColorHex, p
            );

            // 整体文本使用白色
            probabilityText.color = Color.white;
        }
        
        /// <summary>
        /// 重铸按钮点击
        /// </summary>
        private static void OnReforgeButtonClick()
        {
            if (!isReforgeMode) return;
            if (selectedItem == null) return;
            if (isReforging) return;
            if (currentMoney <= 0)
            {
                ModBehaviour.DevLog("[ReforgeUI] 没有投入金钱");
                return;
            }
            
            ModBehaviour.DevLog("[ReforgeUI] 点击重铸按钮: " + selectedItem.DisplayName + ", 投入: " + currentMoney);
            
            isReforging = true;
            
            try
            {
                // 扣除金钱
                Cost cost = new Cost((long)currentMoney);
                if (!EconomyManager.Pay(cost, true, true))
                {
                    ModBehaviour.DevLog("[ReforgeUI] 金钱不足");
                    isReforging = false;
                    return;
                }
                
                // 执行重铸
                var result = ReforgeSystem.Reforge(selectedItem, currentMoney, "player");
                
                if (result.Success)
                {
                    ModBehaviour.DevLog("[ReforgeUI] 重铸成功!");
                    
                    // 显示属性变化
                    ShowPropertyChanges();
                }
                else
                {
                    ModBehaviour.DevLog("[ReforgeUI] 重铸失败!");
                    // 可以添加失败提示
                }
                
                // 立即更新UI状态（金钱已扣除，保留滑块值方便多次快速重铸）
                int newMax = GetPlayerMoney();
                UpdateUIStateKeepSlider(newMax);
                
                // 延迟刷新物品详情显示（等待属性值同步后再刷新，2帧延迟）
                if (ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.StartCoroutine(RefreshItemDetailsDisplayDelayed(2));
                }
                
                ModBehaviour.DevLog("[ReforgeUI] 重铸完成，已更新UI");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 重铸失败: " + e.Message);
            }
            finally
            {
                isReforging = false;
            }
        }
        
        /// <summary>
        /// 保存属性快照
        /// </summary>
        private static void SavePropertySnapshot(Item item)
        {
            originalProperties.Clear();
            
            try
            {
                if (item.Modifiers == null) return;
                
                foreach (var mod in item.Modifiers)
                {
                    PropertySnapshot snapshot = new PropertySnapshot
                    {
                        Key = mod.Key,
                        Value = mod.Value,
                        Type = mod.Type
                    };
                    originalProperties.Add(snapshot);
                }
                
                ModBehaviour.DevLog("[ReforgeUI] 保存了 " + originalProperties.Count + " 个属性快照");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 保存属性快照失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 显示属性变化（修改详情显示）
        /// </summary>
        private static void ShowPropertyChanges()
        {
            if (selectedItem == null) return;
            
            try
            {
                // 获取详情显示组件
                var detailsField = DetailsDisplayField;
                if (detailsField == null) return;
                
                var detailsDisplay = detailsField.GetValue(decomposeView) as ItemDetailsDisplay;
                if (detailsDisplay == null) return;
                
                // 先刷新详情显示（Setup是internal方法，需要反射调用）
                MethodInfo setupMethod = typeof(ItemDetailsDisplay).GetMethod("Setup", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (setupMethod != null)
                {
                    setupMethod.Invoke(detailsDisplay, new object[] { selectedItem });
                }
                
                // 查找属性文本并添加变化标记
                Dictionary<string, PropertySnapshot> oldProps = new Dictionary<string, PropertySnapshot>();
                foreach (var snap in originalProperties)
                {
                    oldProps[snap.Key] = snap;
                }
                
                // 获取propertiesParent来查找属性条目
                var propsParentField = PropertiesParentField;
                if (propsParentField != null)
                {
                    Transform propsParent = propsParentField.GetValue(detailsDisplay) as Transform;
                    if (propsParent != null)
                    {
                        // 遍历所有ItemModifierEntry子组件
                        foreach (Transform child in propsParent)
                        {
                            Component modEntry = child.GetComponent("ItemModifierEntry");
                            if (modEntry == null) continue;
                            
                            // 获取target字段
                            FieldInfo targetField = modEntry.GetType().GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (targetField == null) continue;
                            
                            var modDesc = targetField.GetValue(modEntry);
                            if (modDesc == null) continue;
                            
                            // 获取Key属性
                            PropertyInfo keyProp = modDesc.GetType().GetProperty("Key");
                            if (keyProp == null) continue;
                            
                            string key = keyProp.GetValue(modDesc, null) as string;
                            if (string.IsNullOrEmpty(key)) continue;
                            
                            // 获取当前Value
                            PropertyInfo valueProp = modDesc.GetType().GetProperty("Value");
                            if (valueProp == null) continue;
                            
                            float newValue = (float)valueProp.GetValue(modDesc, null);
                            
                            // 检查是否有旧值
                            if (oldProps.ContainsKey(key))
                            {
                                float oldValue = oldProps[key].Value;
                                float diff = newValue - oldValue;
                                
                                if (Mathf.Abs(diff) > 0.001f)
                                {
                                    // 获取value文本组件
                                    FieldInfo valueTextField = modEntry.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (valueTextField != null)
                                    {
                                        TextMeshProUGUI valueText = valueTextField.GetValue(modEntry) as TextMeshProUGUI;
                                        if (valueText != null)
                                        {
                                            // 添加差异显示
                                            string arrow = diff > 0 ? "↑" : "↓";
                                            string diffStr = Mathf.Abs(diff).ToString("F2");
                                            string colorHex = diff > 0 ? "#FF6666" : "#66FF66";
                                            
                                            // 显示格式: 原值 (<color>↑/↓+差异</color>)
                                            valueText.text = valueText.text + string.Format(" <color={0}>({1}{2})</color>", colorHex, arrow, diffStr);
                                        }
                                    }
                                    
                                    // 日志记录
                                    string logArrow = diff > 0 ? "↑" : "↓";
                                    string logColor = diff > 0 ? "红" : "绿";
                                    ModBehaviour.DevLog("[ReforgeUI] 属性变化: " + key + " " + logArrow + " " + Mathf.Abs(diff).ToString("F2") + " (" + logColor + ")");
                                }
                            }
                        }
                    }
                }
                
                // 保存新的属性快照
                SavePropertySnapshot(selectedItem);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 显示属性变化失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取物品品质
        /// </summary>
        private static int GetItemQuality(Item item)
        {
            if (item == null) return 1;
            try
            {
                return Mathf.Clamp(item.Quality, 1, 8);
            }
            catch { }
            return 1;
        }
        
        /// <summary>
        /// 获取玩家金钱
        /// </summary>
        private static int GetPlayerMoney()
        {
            try
            {
                return (int)EconomyManager.Money;
            }
            catch { }
            return 0;
        }
        
        /// <summary>
        /// 延迟设置物品详情显示 - 等待原版设置完成后覆盖为预制体显示
        /// </summary>
        private static System.Collections.IEnumerator SetupDetailsWithPrefabComparisonDelayed(Item playerItem)
        {
            // 等待一帧，让原版的 ItemDecomposeView.Setup(selectedItem) 先执行
            yield return null;
            
            SetupDetailsWithPrefabComparison(playerItem);
        }
        
        /// <summary>
        /// 设置物品详情显示 - 显示预制体属性并标记与玩家物品的差异
        /// </summary>
        /// <param name="playerItem">玩家物品</param>
        /// <param name="cachePrefab">是否缓存预制体属性（首次选择物品时为true，重铸后刷新时为false使用缓存）</param>
        private static void SetupDetailsWithPrefabComparison(Item playerItem, bool cachePrefab = true)
        {
            if (playerItem == null || detailsDisplayObj == null) return;
            
            try
            {
                Item prefabItem = null;
                
                // 根据是否需要缓存决定是否获取预制体
                if (cachePrefab || cachedPrefabModifiers.Count == 0)
                {
                    // 1. 获取预制体物品
                    prefabItem = ItemAssetsCollection.InstantiateSync(playerItem.TypeID);
                    if (prefabItem == null)
                    {
                        ModBehaviour.DevLog("[ReforgeUI] 无法获取预制体，使用玩家物品显示");
                        MethodInfo setupMethod = detailsDisplayObj.GetType().GetMethod("Setup", BindingFlags.Public | BindingFlags.Instance);
                        if (setupMethod != null) setupMethod.Invoke(detailsDisplayObj, new object[] { playerItem });
                        return;
                    }
                    
                    // 1.5 对自定义装备预制体进行配置（因为预制体没有经过Mod配置）
                    ConfigureCustomEquipmentPrefab(prefabItem);
                    
                    // 2. 缓存预制体属性
                    cachedPrefabModifiers.Clear();
                    cachedPrefabStats.Clear();
                    cachedPrefabVariables.Clear();
                    
                    if (prefabItem.Modifiers != null)
                    {
                        foreach (var mod in prefabItem.Modifiers)
                        {
                            cachedPrefabModifiers[mod.Key] = mod.Value;
                        }
                    }
                    if (prefabItem.Stats != null)
                    {
                        foreach (var stat in prefabItem.Stats)
                        {
                            cachedPrefabStats[stat.Key] = stat.Value;
                        }
                    }
                    if (prefabItem.Variables != null)
                    {
                        foreach (var variable in prefabItem.Variables)
                        {
                            if (variable.DataType == Duckov.Utilities.CustomDataType.Float)
                            {
                                try { cachedPrefabVariables[variable.Key] = variable.GetFloat(); } catch { }
                            }
                        }
                    }
                    
                    ModBehaviour.DevLog(string.Format("[ReforgeUI] 已缓存预制体属性: Modifiers={0}, Stats={1}, Variables={2}", 
                        cachedPrefabModifiers.Count, cachedPrefabStats.Count, cachedPrefabVariables.Count));
                }
                
                // 3. 收集玩家物品的最新属性值（每次都需要重新获取）
                Dictionary<string, float> playerModifiers = new Dictionary<string, float>();
                Dictionary<string, float> playerStats = new Dictionary<string, float>();
                Dictionary<string, float> playerVariables = new Dictionary<string, float>();
                
                if (playerItem.Modifiers != null)
                {
                    foreach (var mod in playerItem.Modifiers)
                    {
                        playerModifiers[mod.Key] = mod.Value;
                    }
                }
                if (playerItem.Stats != null)
                {
                    foreach (var stat in playerItem.Stats)
                    {
                        playerStats[stat.Key] = stat.Value;
                    }
                }
                if (playerItem.Variables != null)
                {
                    foreach (var variable in playerItem.Variables)
                    {
                        if (variable.DataType == Duckov.Utilities.CustomDataType.Float)
                        {
                            try { playerVariables[variable.Key] = variable.GetFloat(); } catch { }
                        }
                    }
                }
                
                // 4. 使用预制体设置详情显示（如果有预制体实例则用它，否则需要重新获取一个用于显示）
                if (prefabItem == null)
                {
                    prefabItem = ItemAssetsCollection.InstantiateSync(playerItem.TypeID);
                    if (prefabItem != null)
                    {
                        ConfigureCustomEquipmentPrefab(prefabItem);
                    }
                }
                
                if (prefabItem != null)
                {
                    MethodInfo setup = detailsDisplayObj.GetType().GetMethod("Setup", BindingFlags.Public | BindingFlags.Instance);
                    if (setup != null)
                    {
                        setup.Invoke(detailsDisplayObj, new object[] { prefabItem });
                    }
                    
                    // 销毁临时预制体实例
                    if (prefabItem.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(prefabItem.gameObject);
                    }
                }
                
                // 5. 延迟修改属性文本以显示差异（使用缓存的预制体属性）
                if (ModBehaviour.Instance != null)
                {
                    currentDiffCoroutine = ModBehaviour.Instance.StartCoroutine(ModifyPropertyTextsDelayed(
                        playerModifiers, playerStats, playerVariables,
                        cachedPrefabModifiers, cachedPrefabStats, cachedPrefabVariables));
                }
                
                ModBehaviour.DevLog("[ReforgeUI] 已设置预制体属性对比显示");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] 设置预制体对比失败: " + e.Message);
                try
                {
                    MethodInfo setupMethod = detailsDisplayObj.GetType().GetMethod("Setup", BindingFlags.Public | BindingFlags.Instance);
                    if (setupMethod != null) setupMethod.Invoke(detailsDisplayObj, new object[] { playerItem });
                }
                catch { }
            }
        }
        
        /// <summary>
        /// 延迟修改属性文本以显示差异（使用传入的预制体属性值而非从UI读取）
        /// </summary>
        private static System.Collections.IEnumerator ModifyPropertyTextsDelayed(
            Dictionary<string, float> playerModifiers,
            Dictionary<string, float> playerStats,
            Dictionary<string, float> playerVariables,
            Dictionary<string, float> prefabModifiers,
            Dictionary<string, float> prefabStats,
            Dictionary<string, float> prefabVariables)
        {
            yield return null; // 等待一帧让UI更新
            
            ModBehaviour.DevLog("[ReforgeUI] ModifyPropertyTextsDelayed 开始执行");
            ModBehaviour.DevLog(string.Format("[ReforgeUI] 玩家属性: Modifiers={0}, Stats={1}, Variables={2}", 
                playerModifiers.Count, playerStats.Count, playerVariables.Count));
            ModBehaviour.DevLog(string.Format("[ReforgeUI] 预制体属性: Modifiers={0}, Stats={1}, Variables={2}", 
                prefabModifiers.Count, prefabStats.Count, prefabVariables.Count));
            
            try
            {
                var detailsDisplay = detailsDisplayObj as ItemDetailsDisplay;
                if (detailsDisplay == null)
                {
                    ModBehaviour.DevLog("[ReforgeUI] detailsDisplay 为 null");
                    yield break;
                }
                
                // 获取propertiesParent
                FieldInfo propsParentField = typeof(ItemDetailsDisplay).GetField("propertiesParent", BindingFlags.NonPublic | BindingFlags.Instance);
                if (propsParentField == null)
                {
                    ModBehaviour.DevLog("[ReforgeUI] propertiesParent 字段未找到");
                    yield break;
                }
                
                Transform propsParent = propsParentField.GetValue(detailsDisplay) as Transform;
                if (propsParent == null)
                {
                    ModBehaviour.DevLog("[ReforgeUI] propertiesParent 为 null");
                    yield break;
                }
                
                ModBehaviour.DevLog(string.Format("[ReforgeUI] propertiesParent 子对象数: {0}", propsParent.childCount));
                
                // 遍历所有属性条目，只获取key和valueText，预制体值从传入参数获取
                foreach (Transform child in propsParent)
                {
                    if (!child.gameObject.activeInHierarchy) continue;
                    
                    string key = null;
                    TextMeshProUGUI valueText = null;
                    
                    // 1. 尝试ItemModifierEntry
                    Component modEntry = child.GetComponent("ItemModifierEntry");
                    if (modEntry != null)
                    {
                        FieldInfo targetField = modEntry.GetType().GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (targetField != null)
                        {
                            var modDesc = targetField.GetValue(modEntry);
                            if (modDesc != null)
                            {
                                PropertyInfo keyProp = modDesc.GetType().GetProperty("Key");
                                if (keyProp != null)
                                {
                                    key = keyProp.GetValue(modDesc, null) as string;
                                }
                            }
                        }
                        FieldInfo valueTextField = modEntry.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (valueTextField != null)
                        {
                            valueText = valueTextField.GetValue(modEntry) as TextMeshProUGUI;
                        }
                    }
                    
                    // 2. 尝试ItemVariableEntry
                    if (key == null)
                    {
                        Component varEntry = child.GetComponent("ItemVariableEntry");
                        if (varEntry != null)
                        {
                            FieldInfo targetField = varEntry.GetType().GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (targetField != null)
                            {
                                var customData = targetField.GetValue(varEntry);
                                if (customData != null)
                                {
                                    PropertyInfo keyProp = customData.GetType().GetProperty("Key");
                                    if (keyProp != null)
                                    {
                                        key = keyProp.GetValue(customData, null) as string;
                                    }
                                }
                            }
                            FieldInfo valueTextField = varEntry.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (valueTextField != null)
                            {
                                valueText = valueTextField.GetValue(varEntry) as TextMeshProUGUI;
                            }
                        }
                    }
                    
                    // 3. 尝试ItemStatEntry
                    if (key == null)
                    {
                        Component statEntry = child.GetComponent("ItemStatEntry");
                        if (statEntry != null)
                        {
                            FieldInfo targetField = statEntry.GetType().GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (targetField != null)
                            {
                                var stat = targetField.GetValue(statEntry);
                                if (stat != null)
                                {
                                    PropertyInfo keyProp = stat.GetType().GetProperty("Key");
                                    if (keyProp != null)
                                    {
                                        key = keyProp.GetValue(stat, null) as string;
                                    }
                                }
                            }
                            FieldInfo valueTextField = statEntry.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (valueTextField != null)
                            {
                                valueText = valueTextField.GetValue(statEntry) as TextMeshProUGUI;
                            }
                        }
                    }
                    
                    if (string.IsNullOrEmpty(key) || valueText == null) continue;
                    
                    // 从传入的预制体属性字典获取预制体值
                    float prefabValue = 0f;
                    bool hasPrefabValue = false;
                    
                    if (prefabModifiers.ContainsKey(key))
                    {
                        prefabValue = prefabModifiers[key];
                        hasPrefabValue = true;
                    }
                    else if (prefabStats.ContainsKey(key))
                    {
                        prefabValue = prefabStats[key];
                        hasPrefabValue = true;
                    }
                    else if (prefabVariables.ContainsKey(key))
                    {
                        prefabValue = prefabVariables[key];
                        hasPrefabValue = true;
                    }
                    
                    if (!hasPrefabValue) continue;
                    
                    // 从传入的玩家属性字典获取玩家值
                    float playerValue = prefabValue;
                    bool hasPlayerValue = false;
                    
                    if (playerModifiers.ContainsKey(key))
                    {
                        playerValue = playerModifiers[key];
                        hasPlayerValue = true;
                    }
                    else if (playerStats.ContainsKey(key))
                    {
                        playerValue = playerStats[key];
                        hasPlayerValue = true;
                    }
                    else if (playerVariables.ContainsKey(key))
                    {
                        playerValue = playerVariables[key];
                        hasPlayerValue = true;
                    }
                    
                    if (!hasPlayerValue) continue;
                    
                    float diff = playerValue - prefabValue;
                    ModBehaviour.DevLog(string.Format("[ReforgeUI] 属性 {0}: player={1}, prefab={2}, diff={3}", 
                        key, playerValue, prefabValue, diff));
                    
                    if (Mathf.Abs(diff) < 0.01f) continue;
                    
                    // 获取基础文本（移除旧的差异标记）
                    string baseText = valueText.text;
                    int colorTagIndex = baseText.IndexOf(" <color=");
                    if (colorTagIndex > 0)
                    {
                        baseText = baseText.Substring(0, colorTagIndex);
                    }
                    
                    // 显示差异: 预制体值 (↑/↓ xx)，保留两位小数
                    string arrow = diff > 0 ? "↑" : "↓";
                    string diffStr = Mathf.Abs(diff).ToString("F2");
                    string colorHex = diff > 0 ? "#66FF66" : "#FF6666";
                    
                    string newText = baseText + string.Format(" <color={0}>({1} {2})</color>", colorHex, arrow, diffStr);
                    valueText.text = newText;
                    ModBehaviour.DevLog(string.Format("[ReforgeUI] 已修改: {0}", newText));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] 修改属性文本失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 配置自定义装备预制体（逆鳞、腾云驾雾等）
        /// 因为 InstantiateSync 获取的预制体没有经过 Mod 配置，需要手动设置默认属性
        /// </summary>
        private static void ConfigureCustomEquipmentPrefab(Item prefabItem)
        {
            if (prefabItem == null) return;
            
            try
            {
                int typeId = prefabItem.TypeID;
                
                // 逆鳞图腾
                if (typeId == ReverseScaleConfig.TotemTypeId)
                {
                    var config = ReverseScaleConfig.Instance;
                    // 恢复生命值：存储为百分比整数（50 = 50%）
                    float healPercentDisplay = config.HealPercent * 100f;
                    prefabItem.Variables.Set(ReverseScaleConfig.VAR_HEAL_PERCENT, healPercentDisplay);
                    prefabItem.Variables.SetDisplay(ReverseScaleConfig.VAR_HEAL_PERCENT, true);
                    
                    // 棱彩弹数量
                    prefabItem.Variables.Set(ReverseScaleConfig.VAR_BOLT_COUNT, (float)config.PrismaticBoltCount);
                    prefabItem.Variables.SetDisplay(ReverseScaleConfig.VAR_BOLT_COUNT, true);
                    
                    ModBehaviour.DevLog("[ReforgeUI] 已配置逆鳞预制体: HealPercent=" + healPercentDisplay + ", BoltCount=" + config.PrismaticBoltCount);
                }
                // 腾云驾雾图腾 - 使用Float存储，支持重铸
                else if (typeId == FlightConfig.Instance.ItemTypeId)
                {
                    var config = FlightConfig.Instance;
                    
                    // 最大向上速度
                    prefabItem.Variables.Set(FlightConfig.VAR_MAX_UPWARD_SPEED, config.MaxUpwardSpeed);
                    prefabItem.Variables.SetDisplay(FlightConfig.VAR_MAX_UPWARD_SPEED, true);
                    
                    // 加速时间
                    prefabItem.Variables.Set(FlightConfig.VAR_ACCELERATION_TIME, config.AccelerationTime);
                    prefabItem.Variables.SetDisplay(FlightConfig.VAR_ACCELERATION_TIME, true);
                    
                    // 滑翔水平系数
                    prefabItem.Variables.Set(FlightConfig.VAR_GLIDING_MULTIPLIER, config.GlidingHorizontalSpeedMultiplier);
                    prefabItem.Variables.SetDisplay(FlightConfig.VAR_GLIDING_MULTIPLIER, true);
                    
                    // 缓慢下落速度（取绝对值）
                    prefabItem.Variables.Set(FlightConfig.VAR_DESCENT_SPEED, UnityEngine.Mathf.Abs(config.SlowDescentSpeed));
                    prefabItem.Variables.SetDisplay(FlightConfig.VAR_DESCENT_SPEED, true);
                    
                    // 启动体力消耗
                    prefabItem.Variables.Set(FlightConfig.VAR_STARTUP_STAMINA, config.StartupStaminaCost);
                    prefabItem.Variables.SetDisplay(FlightConfig.VAR_STARTUP_STAMINA, true);
                    
                    // 飞行体力消耗
                    prefabItem.Variables.Set(FlightConfig.VAR_FLIGHT_STAMINA_DRAIN, config.StaminaDrainPerSecond);
                    prefabItem.Variables.SetDisplay(FlightConfig.VAR_FLIGHT_STAMINA_DRAIN, true);
                    
                    // 滑翔体力消耗
                    prefabItem.Variables.Set(FlightConfig.VAR_GLIDING_STAMINA_DRAIN, config.SlowDescentStaminaDrainPerSecond);
                    prefabItem.Variables.SetDisplay(FlightConfig.VAR_GLIDING_STAMINA_DRAIN, true);
                    
                    ModBehaviour.DevLog("[ReforgeUI] 已配置腾云驾雾预制体: MaxSpeed=" + config.MaxUpwardSpeed);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] 配置自定义装备预制体失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 停止当前正在运行的差异显示协程
        /// </summary>
        private static void StopCurrentDiffCoroutine()
        {
            if (currentDiffCoroutine != null && ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StopCoroutine(currentDiffCoroutine);
                currentDiffCoroutine = null;
            }
        }
        
        /// <summary>
        /// 刷新物品详情显示（属性栏）- 使用缓存的预制体属性
        /// </summary>
        private static void RefreshItemDetailsDisplay()
        {
            if (selectedItem == null) return;
            
            try
            {
                // 停止之前的协程以避免重复显示差异
                StopCurrentDiffCoroutine();
                // 使用缓存的预制体属性进行对比显示（cachePrefab=false）
                SetupDetailsWithPrefabComparison(selectedItem, false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] 刷新物品详情失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 延迟刷新物品详情显示（等待属性值同步后再刷新）
        /// </summary>
        private static System.Collections.IEnumerator RefreshItemDetailsDisplayDelayed(int frameDelay = 1)
        {
            for (int i = 0; i < frameDelay; i++)
            {
                yield return null;
            }
            RefreshItemDetailsDisplay();
        }
        
        /// <summary>
        /// 是否处于重铸模式
        /// </summary>
        public static bool IsReforgeMode
        {
            get { return isReforgeMode; }
        }
        
        /// <summary>
        /// UI关闭通知
        /// </summary>
        public static void NotifyUIClosed()
        {
            // 通知哥布林对话结束
            if (currentController != null)
            {
                currentController.EndDialogue();
                currentController = null;
            }
        }
        
        /// <summary>
        /// 清理（当UI关闭时调用）
        /// </summary>
        public static void Cleanup()
        {
            if (isReforgeMode)
            {
                ItemUIUtilities.OnSelectionChanged -= OnItemSelectionChanged;
                isReforgeMode = false;
            }
            
            // 停止正在运行的协程
            StopCurrentDiffCoroutine();
            
            // 恢复原版事件监听器
            RestoreOriginalEventListeners();
            
            // 恢复原版结果显示
            if (resultDisplayObj != null)
            {
                resultDisplayObj.SetActive(true);
            }
            
            // 清理概率显示
            if (probabilityText != null && probabilityText.gameObject != null)
            {
                GameObject.Destroy(probabilityText.gameObject);
                probabilityText = null;
            }
            
            originalProperties.Clear();
            selectedItem = null;
            decomposeView = null;
            countSliderObj = null;
            moneySlider = null;
            reforgeButton = null;
            sliderValueText = null;
            sliderMinText = null;
            sliderMaxText = null;
            targetNameDisplay = null;
            detailsDisplayObj = null;
            noItemSelectedIndicator = null;
            
            // 清理缓存的预制体属性
            cachedPrefabModifiers.Clear();
            cachedPrefabStats.Clear();
            cachedPrefabVariables.Clear();
        }
    }
    
    /// <summary>
    /// UI状态监控器 - 检测UI关闭
    /// </summary>
    public class ReforgeUIMonitor : MonoBehaviour
    {
        private ItemDecomposeView watchedView;
        private bool wasOpen = false;
        
        public void SetWatchedView(ItemDecomposeView view)
        {
            watchedView = view;
            wasOpen = view != null && view.open;
        }
        
        void Update()
        {
            if (!ReforgeUIManager.IsReforgeMode) return;
            
            if (watchedView != null)
            {
                bool isOpen = watchedView.open;
                
                // 检测UI关闭
                if (wasOpen && !isOpen)
                {
                    ModBehaviour.DevLog("[ReforgeUI] 检测到UI关闭");
                    ReforgeUIManager.Cleanup();
                    ReforgeUIManager.NotifyUIClosed();
                }
                
                wasOpen = isOpen;
            }
        }
    }
}
