using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Duckov;
using Duckov.UI;
using Duckov.Economy;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>
    /// 属性条目交互组件 - 通过文字颜色变化实现固定功能
    /// 白色=普通，蓝色=悬停可固定，金色=已固定
    /// </summary>
    public class PropertyEntryInteractable : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public string PropertyKey { get; set; }
        public PropertyType PropType { get; set; }
        public Item TargetItem { get; set; }
        public TextMeshProUGUI[] TextComponents { get; set; }  // 属性条目的所有文本组件
        public bool IsLocked { get; set; }
        public bool CanLock { get; set; }  // 是否可以固定（有冷淬液）
        
        // 颜色定义
        private static readonly Color normalColor = Color.white;  // 白色 - 普通状态
        private static readonly Color hoverColor = new Color(0.4f, 0.7f, 1f, 1f);  // 蓝色 - 悬停可固定
        private static readonly Color lockedColor = new Color(1f, 0.84f, 0f, 1f);  // 金色 - 已固定
        
        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsLocked || !CanLock) return;
            
            ModBehaviour.DevLog("[PropertyEntry] 点击固定属性: " + PropertyKey);
            
            if (TargetItem == null) return;
            
            // 检查冷淬液数量
            int fluidCount = ItemFactory.GetItemCountInInventory(ColdQuenchFluidConfig.TYPE_ID);
            if (fluidCount <= 0) return;
            
            // 消耗冷淬液
            if (!ItemFactory.ConsumeItem(ColdQuenchFluidConfig.TYPE_ID, 1))
            {
                ModBehaviour.DevLog("[PropertyEntry] 消耗冷淬液失败");
                return;
            }
            
            // 固定属性
            if (PropertyLockSystem.LockProperty(TargetItem, PropertyKey, PropType))
            {
                ModBehaviour.DevLog("[PropertyEntry] 属性已固定: " + PropertyKey);
                IsLocked = true;
                CanLock = false;
                
                // 更新为金色
                SetTextColor(lockedColor);
                
                // 通知UI刷新
                ReforgeUIManager.NotifyPropertyLocked();
            }
        }
        
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (IsLocked) return;  // 已固定不变色
            
            if (CanLock)
            {
                // 有冷淬液，显示蓝色表示可固定
                SetTextColor(hoverColor);
            }
        }
        
        public void OnPointerExit(PointerEventData eventData)
        {
            if (IsLocked) return;  // 已固定保持金色
            
            // 恢复白色
            SetTextColor(normalColor);
        }
        
        /// <summary>
        /// 设置所有文本组件的颜色
        /// </summary>
        private void SetTextColor(Color color)
        {
            if (TextComponents == null) return;
            foreach (var text in TextComponents)
            {
                if (text != null)
                {
                    text.color = color;
                }
            }
        }
        
        /// <summary>
        /// 初始化颜色状态
        /// </summary>
        public void InitializeColor()
        {
            if (IsLocked)
            {
                SetTextColor(lockedColor);
            }
            else
            {
                SetTextColor(normalColor);
            }
        }
    }

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
    /// 性能优化版本：添加预制体缓存、协程合并、UI组件缓存
    /// </summary>
    public static class ReforgeUIManager
    {
        // ============================================================================
        // 常量定义（避免魔法数字）
        // ============================================================================
        private const float UI_INIT_DELAY = 0.1f;
        private const int DEFAULT_FRAME_DELAY = 1;
        private const int REFORGE_REFRESH_FRAME_DELAY = 2;
        private const float DIFF_THRESHOLD = 0.01f;
        
        // 冷淬液UI常量
        private const float COLD_QUENCH_UI_OFFSET_Y = -10f;
        private const int COLD_QUENCH_ICON_SIZE = 56;
        private const int COLD_QUENCH_CONTAINER_WIDTH = 200;
        private const int COLD_QUENCH_CONTAINER_HEIGHT = 70;
        private const int COLD_QUENCH_SPACING = 10;
        private const int COLD_QUENCH_FONT_SIZE = 32;
        private const int COLD_QUENCH_ICON_FONT_SIZE = 40;
        
        // 预制体缓存常量
        private const int MAX_PREFAB_CACHE_SIZE = 10;
        
        // ============================================================================
        // 预制体缓存（性能优化：避免重复实例化）
        // ============================================================================
        private static Dictionary<int, Item> _prefabCache = new Dictionary<int, Item>();
        
        /// <summary>
        /// 获取缓存的预制体（避免重复实例化）
        /// </summary>
        private static Item GetCachedPrefab(int typeId)
        {
            // 检查缓存
            if (_prefabCache.TryGetValue(typeId, out Item cached))
            {
                if (cached != null && cached.gameObject != null)
                {
                    return cached;
                }
                // 缓存失效，移除
                _prefabCache.Remove(typeId);
            }
            
            // 缓存满时清理
            if (_prefabCache.Count >= MAX_PREFAB_CACHE_SIZE)
            {
                ClearPrefabCache();
            }
            
            // 实例化新预制体
            Item prefab = ItemAssetsCollection.InstantiateSync(typeId);
            if (prefab != null)
            {
                _prefabCache[typeId] = prefab;
                // 配置自定义装备
                ConfigureCustomEquipmentPrefab(prefab);
            }
            return prefab;
        }
        
        /// <summary>
        /// 清理预制体缓存
        /// </summary>
        private static void ClearPrefabCache()
        {
            foreach (var kvp in _prefabCache)
            {
                if (kvp.Value != null && kvp.Value.gameObject != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value.gameObject);
                }
            }
            _prefabCache.Clear();
        }
        
        // ============================================================================
        // 缓存的反射 FieldInfo（性能优化）
        // ============================================================================
        private static FieldInfo _detailsDisplayField;
        private static FieldInfo _propertiesParentField;
        private static FieldInfo _cannotDecomposeField;
        
        // ItemModifierEntry 反射缓存
        private static FieldInfo _modEntryTargetField;
        private static FieldInfo _modEntryValueField;
        private static PropertyInfo _modDescKeyProp;
        private static PropertyInfo _modDescValueProp;
        
        // ItemVariableEntry 反射缓存
        private static FieldInfo _varEntryTargetField;
        private static FieldInfo _varEntryValueField;
        private static PropertyInfo _customDataKeyProp;
        
        // ItemStatEntry 反射缓存
        private static FieldInfo _statEntryTargetField;
        private static FieldInfo _statEntryValueField;
        private static PropertyInfo _statKeyProp;
        
        // 反射缓存初始化标志
        private static bool _reflectionCacheInitialized = false;
        
        // 复用的字典对象（避免频繁分配）
        private static readonly Dictionary<string, float> _reusablePlayerModifiers = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> _reusablePlayerStats = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> _reusablePlayerVariables = new Dictionary<string, float>();
        
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
        
        /// <summary>
        /// 初始化反射缓存（只执行一次）
        /// </summary>
        private static void InitializeReflectionCache()
        {
            if (_reflectionCacheInitialized) return;
            
            try
            {
                // 获取 ItemModifierEntry 类型
                Type modEntryType = Type.GetType("ItemStatsSystem.ItemModifierEntry, ItemStatsSystem") 
                    ?? Type.GetType("ItemModifierEntry, Assembly-CSharp");
                if (modEntryType != null)
                {
                    _modEntryTargetField = modEntryType.GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                    _modEntryValueField = modEntryType.GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                }
                
                // 获取 ModifierDescription 类型的属性
                Type modDescType = typeof(ModifierDescription);
                _modDescKeyProp = modDescType.GetProperty("Key");
                _modDescValueProp = modDescType.GetProperty("Value");
                
                // 获取 ItemVariableEntry 类型
                Type varEntryType = Type.GetType("ItemStatsSystem.ItemVariableEntry, ItemStatsSystem")
                    ?? Type.GetType("ItemVariableEntry, Assembly-CSharp");
                if (varEntryType != null)
                {
                    _varEntryTargetField = varEntryType.GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                    _varEntryValueField = varEntryType.GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                }
                
                // 获取 CustomData 类型的属性
                Type customDataType = typeof(Duckov.Utilities.CustomData);
                _customDataKeyProp = customDataType.GetProperty("Key");
                
                // 获取 ItemStatEntry 类型
                Type statEntryType = Type.GetType("ItemStatsSystem.ItemStatEntry, ItemStatsSystem")
                    ?? Type.GetType("ItemStatEntry, Assembly-CSharp");
                if (statEntryType != null)
                {
                    _statEntryTargetField = statEntryType.GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                    _statEntryValueField = statEntryType.GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                }
                
                // 获取 Stat 类型的属性
                Type statType = typeof(Stat);
                _statKeyProp = statType.GetProperty("Key");
                
                _reflectionCacheInitialized = true;
                ModBehaviour.DevLog("[ReforgeUI] 反射缓存初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [WARNING] 反射缓存初始化失败: " + e.Message);
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
        
        // ============================================================================
        // 冷淬液UI相关变量
        // ============================================================================
        
        // 冷淬液数量显示容器
        private static GameObject coldQuenchFluidContainer;
        // 冷淬液数量文本
        private static TextMeshProUGUI coldQuenchFluidCountText;
        // 属性锁定图标列表（key: 属性条目Transform的InstanceID）
        private static Dictionary<int, PropertyLockIcon> propertyLockIcons = new Dictionary<int, PropertyLockIcon>();
        
        /// <summary>
        /// 属性交互信息（用于跟踪已设置交互的属性条目）
        /// </summary>
        private class PropertyLockIcon
        {
            public GameObject IconObject;      // 属性条目GameObject
            public Image IconImage;            // 用于接收点击的Image组件
            public string PropertyKey;         // 属性键名
            public PropertyType PropertyType;  // 属性类型
            public bool IsLocked;              // 是否已固定
        }
        
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
            
            // 通知哥布林对话结束，显示告别对话
            if (currentController != null)
            {
                currentController.EndDialogueWithStay(10f, true);  // 重铸UI关闭时显示告别对话
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
            yield return new WaitForSeconds(UI_INIT_DELAY);

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
                
                // 初始化反射缓存（只执行一次）
                InitializeReflectionCache();
                
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
                
                // 8. 创建冷淬液数量显示UI
                CreateColdQuenchFluidUI();
                
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
                    // 设置滑块范围为玩家金钱，最小值为基础费用
                    int playerMoney = GetPlayerMoney();
                    int baseCost = ReforgeSystem.MIN_REFORGE_COST; // 初始时没有选中物品，使用最低费用
                    moneySlider.minValue = baseCost;
                    moneySlider.maxValue = Mathf.Max(baseCost, playerMoney);
                    moneySlider.value = baseCost;
                    moneySlider.wholeNumbers = true;
                    
                    // 移除原有监听器，添加新的
                    moneySlider.onValueChanged.RemoveAllListeners();
                    moneySlider.onValueChanged.AddListener(OnMoneySliderChanged);
                    
                    // 设置文本显示
                    if (sliderValueText != null)
                        sliderValueText.text = baseCost.ToString();
                    if (sliderMinText != null)
                        sliderMinText.text = baseCost.ToString();
                    if (sliderMaxText != null)
                        sliderMaxText.text = playerMoney.ToString();
                    
                    ModBehaviour.DevLog("[ReforgeUI] 滑块已修改为金钱滑块，最大值: " + playerMoney + "，基础费用: " + baseCost);
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
        /// 物品选择改变
        /// </summary>
        private static void OnItemSelectionChanged()
        {
            if (!isReforgeMode) return;
            
            Item newItem = ItemUIUtilities.SelectedItem;
            
            // 切换了物品，清除原属性快照和锁定图标
            originalProperties.Clear();
            ClearPropertyLockIcons();
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
                    // 延迟添加锁定图标（等待属性条目创建完成）
                    ModBehaviour.Instance.StartCoroutine(AddPropertyLockIconsDelayed());
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
            // 滑块最小值设为基础费用
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StartCoroutine(ResetUIStateDelayed(GetPlayerMoney()));
            }
            
            // 更新冷淬液数量显示
            UpdateColdQuenchFluidCount();
            
            UpdateProbabilityDisplay();
            UpdateReforgeButtonInteractable();
        }
        
        /// <summary>
        /// 延迟添加锁定图标（等待属性条目创建完成）
        /// </summary>
        private static System.Collections.IEnumerator AddPropertyLockIconsDelayed()
        {
            // 等待2帧，确保属性条目已创建
            yield return null;
            yield return null;
            AddPropertyLockIcons();
        }
        
        /// <summary>
        /// 重置UI状态（滑块、按钮、概率显示）
        /// </summary>
        private static void ResetUIState(int maxMoney)
        {
            // 计算基础费用（物品价值的1/10）
            int baseCost = selectedItem != null ? ReforgeSystem.GetBaseCost(selectedItem) : ReforgeSystem.MIN_REFORGE_COST;
            
            // 重置滑块
            if (moneySlider != null)
            {
                moneySlider.minValue = baseCost;
                moneySlider.maxValue = Mathf.Max(baseCost, maxMoney);
                moneySlider.value = baseCost;
                moneySlider.wholeNumbers = true;
                currentMoney = baseCost;
                
                if (sliderValueText != null)
                    sliderValueText.text = baseCost.ToString();
                if (sliderMinText != null)
                    sliderMinText.text = baseCost.ToString();
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
            // 计算基础费用（物品价值的1/10）
            int baseCost = selectedItem != null ? ReforgeSystem.GetBaseCost(selectedItem) : ReforgeSystem.MIN_REFORGE_COST;
            
            if (moneySlider != null)
            {
                // 保留当前滑块值，只更新最大值
                float currentValue = moneySlider.value;
                moneySlider.minValue = baseCost;
                moneySlider.maxValue = Mathf.Max(baseCost, maxMoney);
                
                // 如果当前值超过新的最大值，则调整为最大值
                if (currentValue > maxMoney)
                {
                    moneySlider.value = maxMoney;
                    currentMoney = maxMoney;
                }
                else if (currentValue < baseCost)
                {
                    // 如果当前值低于基础费用，调整为基础费用
                    moneySlider.value = baseCost;
                    currentMoney = baseCost;
                }
                else
                {
                    moneySlider.value = currentValue;
                    currentMoney = Mathf.RoundToInt(currentValue);
                }
                
                if (sliderValueText != null)
                    sliderValueText.text = currentMoney.ToString();
                if (sliderMinText != null)
                    sliderMinText.text = baseCost.ToString();
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
            UpdateReforgeButtonInteractable();
        }
        
        /// <summary>
        /// 更新重铸按钮的可交互状态（金钱不足时禁用）
        /// </summary>
        private static void UpdateReforgeButtonInteractable()
        {
            if (reforgeButton == null) return;
            
            if (selectedItem == null)
            {
                reforgeButton.interactable = false;
                return;
            }
            
            // 计算基础费用
            int baseCost = ReforgeSystem.GetBaseCost(selectedItem);
            int playerMoney = GetPlayerMoney();
            
            // 检查玩家金钱是否足够支付基础费用
            bool canAfford = playerMoney >= baseCost;
            reforgeButton.interactable = canAfford;
            
            // 如果金钱不足，更新概率显示提示
            if (!canAfford && probabilityText != null)
            {
                probabilityText.text = string.Format("<color=#FF4D4D>金钱不足！\n基础费用: {0}\n当前金钱: {1}</color>", baseCost, playerMoney);
                probabilityText.color = Color.white;
            }
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
            UpdateReforgeButtonInteractable();
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
            int itemId = selectedItem.GetInstanceID();

            // 计算各个系数
            float rarityFactor = ReforgeSystem.RarityFactor(rarity);      // 品质系数
            float valueFactor = ReforgeSystem.ValueFactor(itemValue);     // 价值系数
            float moneyBonus = ReforgeSystem.MoneyBonus(currentMoney, itemValue);    // 金钱加成（基于物品价值）
            float pityBonus = ReforgeSystem.GetPityBonus(itemId);         // 保底加成
            int pityCount = ReforgeSystem.GetPityCount(itemId);           // 保底计数

            // 计算最终概率（含保底）
            float p = ReforgeSystem.FinalProbabilityWithPity(rarity, itemValue, currentMoney, itemId);

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
            // 保底: X次 (加成: X.XX)  -- 仅当有保底时显示
            // 概率: 0.20×X.XX×X.XX+X.XX+X.XX = XX%
            string pityLine = "";
            string pityFormula = "";
            if (pityCount > 0)
            {
                pityLine = string.Format("<color=#FFD700>保底: {0}次 (加成: {1:F2})</color>\n", pityCount, pityBonus);
                pityFormula = string.Format("+{0:F2}", pityBonus);
            }
            
            probabilityText.text = string.Format(
                "品质: {0} (系数: {1:F2})\n" +
                "价值: {2:F0} (系数: {3:F2})\n" +
                "投入: {4} (加成: {5:F2})\n" +
                "{6}" +
                "<color={7}>概率: 0.20×{1:F2}×{3:F2}+{5:F2}{8}={9:P0}</color>",
                rarity, rarityFactor,
                itemValue, valueFactor,
                currentMoney, moneyBonus,
                pityLine,
                probColorHex, pityFormula, p
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

            // 播放重铸音效
            BossRushAudioManager.Instance.PlayReforgeSFX();

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
                
                // 合并协程：延迟刷新物品详情和锁定图标（性能优化：减少协程调度开销）
                if (ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.StartCoroutine(RefreshUIAfterReforgeDelayed());
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
        /// 性能优化：减少预制体实例化次数
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
                    // 1. 获取预制体物品（使用缓存）
                    prefabItem = GetCachedPrefab(playerItem.TypeID);
                    if (prefabItem == null)
                    {
                        ModBehaviour.DevLog("[ReforgeUI] 无法获取预制体，使用玩家物品显示");
                        MethodInfo setupMethod = detailsDisplayObj.GetType().GetMethod("Setup", BindingFlags.Public | BindingFlags.Instance);
                        if (setupMethod != null) setupMethod.Invoke(detailsDisplayObj, new object[] { playerItem });
                        return;
                    }
                    
                    // 注意：GetCachedPrefab 已经调用了 ConfigureCustomEquipmentPrefab
                    
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
                
                // 3. 收集玩家物品的最新属性值（复用字典对象，避免频繁分配）
                _reusablePlayerModifiers.Clear();
                _reusablePlayerStats.Clear();
                _reusablePlayerVariables.Clear();
                
                if (playerItem.Modifiers != null)
                {
                    foreach (var mod in playerItem.Modifiers)
                    {
                        _reusablePlayerModifiers[mod.Key] = mod.Value;
                    }
                }
                if (playerItem.Stats != null)
                {
                    foreach (var stat in playerItem.Stats)
                    {
                        _reusablePlayerStats[stat.Key] = stat.Value;
                    }
                }
                if (playerItem.Variables != null)
                {
                    foreach (var variable in playerItem.Variables)
                    {
                        if (variable.DataType == Duckov.Utilities.CustomDataType.Float)
                        {
                            try { _reusablePlayerVariables[variable.Key] = variable.GetFloat(); } catch { }
                        }
                    }
                }
                
                // 4. 使用预制体设置详情显示
                // 如果之前没有获取预制体（cachePrefab=false且有缓存），需要获取一个用于UI显示
                if (prefabItem == null)
                {
                    prefabItem = GetCachedPrefab(playerItem.TypeID);
                }
                
                if (prefabItem != null)
                {
                    MethodInfo setup = detailsDisplayObj.GetType().GetMethod("Setup", BindingFlags.Public | BindingFlags.Instance);
                    if (setup != null)
                    {
                        setup.Invoke(detailsDisplayObj, new object[] { prefabItem });
                    }
                    
                    // 注意：不再销毁预制体，因为它被缓存了
                    // 缓存会在 Cleanup() 时统一清理
                }
                
                // 5. 延迟修改属性文本以显示差异（使用缓存的预制体属性和复用的玩家属性字典）
                if (ModBehaviour.Instance != null)
                {
                    currentDiffCoroutine = ModBehaviour.Instance.StartCoroutine(ModifyPropertyTextsDelayed(
                        _reusablePlayerModifiers, _reusablePlayerStats, _reusablePlayerVariables,
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
        /// 延迟修改属性文本以显示差异（使用缓存的反射字段，避免重复反射）
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
                
                // 获取propertiesParent（使用缓存的字段）
                var propsParentField = PropertiesParentField;
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
                
                // 遍历所有属性条目，使用缓存的反射字段
                foreach (Transform child in propsParent)
                {
                    if (!child.gameObject.activeInHierarchy) continue;
                    
                    string key = null;
                    TextMeshProUGUI valueText = null;
                    
                    // 1. 尝试ItemModifierEntry（使用缓存的反射字段）
                    Component modEntry = child.GetComponent("ItemModifierEntry");
                    if (modEntry != null)
                    {
                        // 动态获取反射字段（如果缓存未初始化）
                        if (_modEntryTargetField == null)
                        {
                            _modEntryTargetField = modEntry.GetType().GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                        }
                        if (_modEntryValueField == null)
                        {
                            _modEntryValueField = modEntry.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                        }
                        
                        if (_modEntryTargetField != null)
                        {
                            var modDesc = _modEntryTargetField.GetValue(modEntry);
                            if (modDesc != null)
                            {
                                if (_modDescKeyProp == null)
                                {
                                    _modDescKeyProp = modDesc.GetType().GetProperty("Key");
                                }
                                if (_modDescKeyProp != null)
                                {
                                    key = _modDescKeyProp.GetValue(modDesc, null) as string;
                                }
                            }
                        }
                        if (_modEntryValueField != null)
                        {
                            valueText = _modEntryValueField.GetValue(modEntry) as TextMeshProUGUI;
                        }
                    }
                    
                    // 2. 尝试ItemVariableEntry（使用缓存的反射字段）
                    if (key == null)
                    {
                        Component varEntry = child.GetComponent("ItemVariableEntry");
                        if (varEntry != null)
                        {
                            if (_varEntryTargetField == null)
                            {
                                _varEntryTargetField = varEntry.GetType().GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                            }
                            if (_varEntryValueField == null)
                            {
                                _varEntryValueField = varEntry.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                            }
                            
                            if (_varEntryTargetField != null)
                            {
                                var customData = _varEntryTargetField.GetValue(varEntry);
                                if (customData != null)
                                {
                                    if (_customDataKeyProp == null)
                                    {
                                        _customDataKeyProp = customData.GetType().GetProperty("Key");
                                    }
                                    if (_customDataKeyProp != null)
                                    {
                                        key = _customDataKeyProp.GetValue(customData, null) as string;
                                    }
                                }
                            }
                            if (_varEntryValueField != null)
                            {
                                valueText = _varEntryValueField.GetValue(varEntry) as TextMeshProUGUI;
                            }
                        }
                    }
                    
                    // 3. 尝试ItemStatEntry（使用缓存的反射字段）
                    if (key == null)
                    {
                        Component statEntry = child.GetComponent("ItemStatEntry");
                        if (statEntry != null)
                        {
                            if (_statEntryTargetField == null)
                            {
                                _statEntryTargetField = statEntry.GetType().GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                            }
                            if (_statEntryValueField == null)
                            {
                                _statEntryValueField = statEntry.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance);
                            }
                            
                            if (_statEntryTargetField != null)
                            {
                                var stat = _statEntryTargetField.GetValue(statEntry);
                                if (stat != null)
                                {
                                    if (_statKeyProp == null)
                                    {
                                        _statKeyProp = stat.GetType().GetProperty("Key");
                                    }
                                    if (_statKeyProp != null)
                                    {
                                        key = _statKeyProp.GetValue(stat, null) as string;
                                    }
                                }
                            }
                            if (_statEntryValueField != null)
                            {
                                valueText = _statEntryValueField.GetValue(statEntry) as TextMeshProUGUI;
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
                    
                    if (Mathf.Abs(diff) < DIFF_THRESHOLD) continue;
                    
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
        /// 合并协程：重铸后延迟刷新UI（性能优化：减少协程调度开销）
        /// 合并了 RefreshItemDetailsDisplayDelayed 和 RefreshLockIconsDelayed 的功能
        /// </summary>
        private static System.Collections.IEnumerator RefreshUIAfterReforgeDelayed()
        {
            // 等待2帧让属性值同步
            yield return null;
            yield return null;
            
            // 刷新物品详情显示
            RefreshItemDetailsDisplay();
            
            // 再等1帧确保UI更新完成
            yield return null;
            
            // 刷新锁定图标
            ClearPropertyLockIcons();
            AddPropertyLockIcons();
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
            // 通知哥布林对话结束，显示告别对话
            if (currentController != null)
            {
                currentController.EndDialogueWithStay(10f, true);  // 重铸UI关闭时显示告别对话
                currentController = null;
            }
        }
        
        /// <summary>
        /// 清理（当UI关闭时调用）
        /// 性能优化：添加预制体缓存清理
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
            
            // 清理复用的玩家属性字典
            _reusablePlayerModifiers.Clear();
            _reusablePlayerStats.Clear();
            _reusablePlayerVariables.Clear();
            
            // 清理预制体缓存（性能优化：释放内存）
            ClearPrefabCache();
            
            // 清理冷淬液UI
            CleanupColdQuenchFluidUI();
        }
        
        // ============================================================================
        // 冷淬液UI相关方法
        // ============================================================================
        
        /// <summary>
        /// 创建冷淬液数量显示UI（在属性栏上方）
        /// 代码规范优化：使用常量替代魔法数字
        /// </summary>
        private static void CreateColdQuenchFluidUI()
        {
            try
            {
                if (decomposeView == null)
                {
                    ModBehaviour.DevLog("[ReforgeUI] 无法创建冷淬液UI: decomposeView 为空");
                    return;
                }
                
                // 在 ItemDecomposeView 的顶部创建冷淬液显示
                Transform contentTransform = decomposeView.transform.Find("Content");
                if (contentTransform == null)
                {
                    contentTransform = decomposeView.transform;
                }
                
                ModBehaviour.DevLog("[ReforgeUI] 创建冷淬液UI - parent: " + contentTransform.name);
                
                // 检查是否已存在
                Transform existing = decomposeView.transform.Find("ColdQuenchFluidDisplay");
                if (existing != null)
                {
                    coldQuenchFluidContainer = existing.gameObject;
                    coldQuenchFluidCountText = coldQuenchFluidContainer.GetComponentInChildren<TextMeshProUGUI>();
                    UpdateColdQuenchFluidCount();
                    ModBehaviour.DevLog("[ReforgeUI] 冷淬液UI已存在，复用");
                    return;
                }
                
                // 创建容器
                coldQuenchFluidContainer = new GameObject("ColdQuenchFluidDisplay");
                coldQuenchFluidContainer.transform.SetParent(decomposeView.transform, false);
                
                // 设置RectTransform - 使用常量
                RectTransform containerRect = coldQuenchFluidContainer.AddComponent<RectTransform>();
                containerRect.anchorMin = new Vector2(0.5f, 1);
                containerRect.anchorMax = new Vector2(0.5f, 1);
                containerRect.pivot = new Vector2(0.5f, 1);
                containerRect.anchoredPosition = new Vector2(0, COLD_QUENCH_UI_OFFSET_Y);
                containerRect.sizeDelta = new Vector2(COLD_QUENCH_CONTAINER_WIDTH, COLD_QUENCH_CONTAINER_HEIGHT);
                
                // 添加水平布局
                HorizontalLayoutGroup layout = coldQuenchFluidContainer.AddComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.spacing = COLD_QUENCH_SPACING;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
                layout.padding = new RectOffset(5, 5, 5, 5);
                
                // 创建图标 - 使用常量
                GameObject iconObj = new GameObject("FluidIcon");
                iconObj.transform.SetParent(coldQuenchFluidContainer.transform, false);
                RectTransform iconRect = iconObj.AddComponent<RectTransform>();
                iconRect.sizeDelta = new Vector2(COLD_QUENCH_ICON_SIZE, COLD_QUENCH_ICON_SIZE);
                
                // 添加 LayoutElement 确保尺寸
                LayoutElement iconLayout = iconObj.AddComponent<LayoutElement>();
                iconLayout.minWidth = COLD_QUENCH_ICON_SIZE;
                iconLayout.minHeight = COLD_QUENCH_ICON_SIZE;
                iconLayout.preferredWidth = COLD_QUENCH_ICON_SIZE;
                iconLayout.preferredHeight = COLD_QUENCH_ICON_SIZE;
                
                // 尝试加载图标
                Sprite iconSprite = ItemFactory.GetSprite(ColdQuenchFluidConfig.BUNDLE_NAME, ColdQuenchFluidConfig.ICON_NAME);
                
                if (iconSprite != null)
                {
                    Image iconImage = iconObj.AddComponent<Image>();
                    iconImage.sprite = iconSprite;
                    iconImage.preserveAspect = true;
                }
                else
                {
                    // 使用文本作为后备
                    TextMeshProUGUI iconText = iconObj.AddComponent<TextMeshProUGUI>();
                    iconText.text = "❄";
                    iconText.fontSize = COLD_QUENCH_ICON_FONT_SIZE;
                    iconText.color = new Color(0.5f, 0.8f, 1f);
                    iconText.alignment = TextAlignmentOptions.Center;
                    ModBehaviour.DevLog("[ReforgeUI] 冷淬液图标加载失败，使用文本图标");
                }
                
                // 创建数量文本
                GameObject countObj = new GameObject("FluidCount");
                countObj.transform.SetParent(coldQuenchFluidContainer.transform, false);
                coldQuenchFluidCountText = countObj.AddComponent<TextMeshProUGUI>();
                coldQuenchFluidCountText.text = "x0";
                coldQuenchFluidCountText.fontSize = COLD_QUENCH_FONT_SIZE;
                coldQuenchFluidCountText.color = Color.white;
                coldQuenchFluidCountText.alignment = TextAlignmentOptions.Left;
                RectTransform countRect = countObj.GetComponent<RectTransform>();
                countRect.sizeDelta = new Vector2(80, COLD_QUENCH_ICON_SIZE);
                
                // 更新数量显示
                UpdateColdQuenchFluidCount();
                
                ModBehaviour.DevLog("[ReforgeUI] 冷淬液UI已创建");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] 创建冷淬液UI失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 更新冷淬液数量显示
        /// </summary>
        private static void UpdateColdQuenchFluidCount()
        {
            if (coldQuenchFluidCountText == null) return;
            
            int count = ItemFactory.GetItemCountInInventory(ColdQuenchFluidConfig.TYPE_ID);
            coldQuenchFluidCountText.text = "x" + count;
            
            // 根据数量改变颜色
            if (count > 0)
            {
                coldQuenchFluidCountText.color = new Color(0.5f, 1f, 0.5f);  // 绿色
            }
            else
            {
                coldQuenchFluidCountText.color = new Color(0.7f, 0.7f, 0.7f);  // 灰色
            }
        }
        
        /// <summary>
        /// 为属性条目添加交互功能（通过文字颜色变化实现固定）
        /// </summary>
        private static void AddPropertyLockIcons()
        {
            ModBehaviour.DevLog("[ReforgeUI] AddPropertyLockIcons 开始执行");
            
            if (selectedItem == null)
            {
                ModBehaviour.DevLog("[ReforgeUI] AddPropertyLockIcons: selectedItem 为空");
                return;
            }
            
            if (detailsDisplayObj == null)
            {
                ModBehaviour.DevLog("[ReforgeUI] AddPropertyLockIcons: detailsDisplayObj 为空");
                return;
            }
            
            try
            {
                // 清理旧的交互组件
                ClearPropertyLockIcons();
                
                var detailsDisplay = detailsDisplayObj as ItemDetailsDisplay;
                if (detailsDisplay == null)
                {
                    ModBehaviour.DevLog("[ReforgeUI] AddPropertyLockIcons: detailsDisplay 转换失败");
                    return;
                }
                
                var propsParentField = PropertiesParentField;
                if (propsParentField == null)
                {
                    ModBehaviour.DevLog("[ReforgeUI] AddPropertyLockIcons: PropertiesParentField 为空");
                    return;
                }
                
                Transform propsParent = propsParentField.GetValue(detailsDisplay) as Transform;
                if (propsParent == null)
                {
                    ModBehaviour.DevLog("[ReforgeUI] AddPropertyLockIcons: propsParent 为空");
                    return;
                }
                
                // 获取冷淬液数量（决定是否可以固定）
                int fluidCount = ItemFactory.GetItemCountInInventory(ColdQuenchFluidConfig.TYPE_ID);
                bool canLock = fluidCount > 0;
                
                ModBehaviour.DevLog("[ReforgeUI] 冷淬液数量: " + fluidCount + ", 可固定: " + canLock);
                
                int addedCount = 0;
                
                // 遍历所有属性条目
                foreach (Transform child in propsParent)
                {
                    if (!child.gameObject.activeInHierarchy) continue;
                    
                    string key = null;
                    PropertyType propType = PropertyType.Modifier;
                    bool hasNumericValue = false;
                    
                    // 尝试获取属性键名和类型
                    Component modEntry = child.GetComponent("ItemModifierEntry");
                    if (modEntry != null)
                    {
                        key = GetPropertyKeyFromEntry(modEntry, "ItemModifierEntry");
                        propType = PropertyType.Modifier;
                        hasNumericValue = true;
                    }
                    
                    if (key == null)
                    {
                        Component statEntry = child.GetComponent("ItemStatEntry");
                        if (statEntry != null)
                        {
                            key = GetPropertyKeyFromEntry(statEntry, "ItemStatEntry");
                            propType = PropertyType.Stat;
                            hasNumericValue = true;
                        }
                    }
                    
                    // 跳过无效条目
                    if (string.IsNullOrEmpty(key) || !hasNumericValue) continue;
                    
                    // 跳过系统变量
                    if (key == "Count" || key == "ReforgeCount" || key.StartsWith("RF_")) continue;
                    
                    // 为属性条目添加交互功能
                    SetupPropertyEntryInteraction(child, key, propType, canLock);
                    addedCount++;
                }
                
                ModBehaviour.DevLog("[ReforgeUI] 已设置 " + addedCount + " 个属性条目的交互功能");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] 添加属性交互失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 为属性条目设置交互功能（文字颜色变化方案）
        /// 白色=普通，蓝色=悬停可固定，金色=已固定
        /// </summary>
        private static void SetupPropertyEntryInteraction(Transform propertyEntry, string propertyKey, PropertyType propType, bool canLock)
        {
            try
            {
                int entryId = propertyEntry.GetInstanceID();
                
                // 检查是否已存在
                if (propertyLockIcons.ContainsKey(entryId)) return;
                
                // 检查属性是否已固定
                bool isLocked = PropertyLockSystem.IsPropertyLocked(selectedItem, propertyKey, propType);
                
                // 获取属性条目上的所有文本组件
                TextMeshProUGUI[] textComponents = propertyEntry.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (textComponents == null || textComponents.Length == 0)
                {
                    ModBehaviour.DevLog("[ReforgeUI] 属性条目没有文本组件: " + propertyKey);
                    return;
                }
                
                // 确保属性条目有Image组件用于接收点击（如果没有则添加）
                Image entryImage = propertyEntry.GetComponent<Image>();
                if (entryImage == null)
                {
                    entryImage = propertyEntry.gameObject.AddComponent<Image>();
                    entryImage.color = new Color(0, 0, 0, 0);  // 完全透明
                }
                entryImage.raycastTarget = true;  // 确保可以接收点击
                
                // 添加交互组件
                PropertyEntryInteractable interactable = propertyEntry.gameObject.AddComponent<PropertyEntryInteractable>();
                interactable.PropertyKey = propertyKey;
                interactable.PropType = propType;
                interactable.TargetItem = selectedItem;
                interactable.TextComponents = textComponents;
                interactable.IsLocked = isLocked;
                interactable.CanLock = canLock && !isLocked;  // 有冷淬液且未固定才能固定
                
                // 初始化颜色
                interactable.InitializeColor();
                
                // 保存引用
                PropertyLockIcon lockIcon = new PropertyLockIcon
                {
                    IconObject = propertyEntry.gameObject,
                    IconImage = entryImage,
                    PropertyKey = propertyKey,
                    PropertyType = propType,
                    IsLocked = isLocked
                };
                propertyLockIcons[entryId] = lockIcon;
                
                ModBehaviour.DevLog("[ReforgeUI] 属性交互已设置: " + propertyKey + ", isLocked=" + isLocked + ", canLock=" + interactable.CanLock);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] 设置属性交互失败: " + propertyKey + " - " + e.Message);
            }
        }
        
        /// <summary>
        /// 从属性条目获取属性键名
        /// </summary>
        private static string GetPropertyKeyFromEntry(Component entry, string entryType)
        {
            try
            {
                FieldInfo targetField = entry.GetType().GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                if (targetField == null) return null;
                
                object target = targetField.GetValue(entry);
                if (target == null) return null;
                
                PropertyInfo keyProp = target.GetType().GetProperty("Key");
                if (keyProp == null) return null;
                
                return keyProp.GetValue(target, null) as string;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// 刷新所有锁定高亮效果（重新添加）
        /// </summary>
        private static void RefreshPropertyLockIcons()
        {
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StartCoroutine(RefreshLockIconsDelayed());
            }
        }
        
        /// <summary>
        /// 通知属性已被锁定，刷新UI
        /// </summary>
        public static void NotifyPropertyLocked()
        {
            // 更新冷淬液数量显示
            UpdateColdQuenchFluidCount();
            
            // 延迟刷新交互组件
            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StartCoroutine(RefreshLockIconsDelayed());
            }
        }
        
        /// <summary>
        /// 延迟刷新属性交互（等待属性条目更新完成）
        /// </summary>
        private static System.Collections.IEnumerator RefreshLockIconsDelayed()
        {
            ModBehaviour.DevLog("[ReforgeUI] RefreshLockIconsDelayed 开始执行...");
            
            // 等待3帧，确保属性条目已更新
            yield return null;
            yield return null;
            yield return null;
            
            ModBehaviour.DevLog("[ReforgeUI] 等待完成，开始刷新属性交互...");
            
            // 重新添加交互组件
            ClearPropertyLockIcons();
            AddPropertyLockIcons();
            
            ModBehaviour.DevLog("[ReforgeUI] 属性交互刷新完成，共 " + propertyLockIcons.Count + " 个属性");
        }
        
        /// <summary>
        /// 清理属性条目的交互组件
        /// </summary>
        private static void ClearPropertyLockIcons()
        {
            foreach (var kvp in propertyLockIcons)
            {
                if (kvp.Value != null && kvp.Value.IconObject != null)
                {
                    // 移除交互组件（不销毁属性条目本身）
                    PropertyEntryInteractable interactable = kvp.Value.IconObject.GetComponent<PropertyEntryInteractable>();
                    if (interactable != null)
                    {
                        // 恢复文字颜色为白色
                        if (interactable.TextComponents != null)
                        {
                            foreach (var text in interactable.TextComponents)
                            {
                                if (text != null)
                                {
                                    text.color = Color.white;
                                }
                            }
                        }
                        GameObject.Destroy(interactable);
                    }
                    
                    // 移除我们添加的透明Image（如果有）
                    // 注意：只移除我们添加的，不要移除原有的
                    Image img = kvp.Value.IconObject.GetComponent<Image>();
                    if (img != null && img.color.a == 0)  // 只移除完全透明的（我们添加的）
                    {
                        GameObject.Destroy(img);
                    }
                }
            }
            propertyLockIcons.Clear();
        }
        
        /// <summary>
        /// 清理冷淬液UI
        /// </summary>
        private static void CleanupColdQuenchFluidUI()
        {
            // 清理属性交互组件
            ClearPropertyLockIcons();
            
            // 清理冷淬液数量显示
            if (coldQuenchFluidContainer != null)
            {
                GameObject.Destroy(coldQuenchFluidContainer);
                coldQuenchFluidContainer = null;
            }
            coldQuenchFluidCountText = null;
        }
    }
    
    /// <summary>
    /// UI状态监控器 - 检测UI关闭（优化：减少Update调用频率）
    /// </summary>
    public class ReforgeUIMonitor : MonoBehaviour
    {
        private ItemDecomposeView watchedView;
        private bool wasOpen = false;
        private float checkInterval = 0.1f;  // 检查间隔（秒）
        private float nextCheckTime = 0f;
        
        public void SetWatchedView(ItemDecomposeView view)
        {
            watchedView = view;
            wasOpen = view != null && view.open;
            nextCheckTime = Time.time + checkInterval;
        }
        
        void Update()
        {
            // 不在重铸模式时跳过
            if (!ReforgeUIManager.IsReforgeMode) return;
            
            // 降低检查频率（每0.1秒检查一次，而非每帧）
            if (Time.time < nextCheckTime) return;
            nextCheckTime = Time.time + checkInterval;
            
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
