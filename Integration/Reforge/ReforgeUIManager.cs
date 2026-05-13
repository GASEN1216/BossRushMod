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
        public PropertyType PropType;
        public int EntryOrdinal;
    }

    /// <summary>
    /// 重铸UI管理器 - 通过修改原版分解UI实现
    /// 性能优化版本：添加预制体缓存、协程合并、UI组件缓存
    /// </summary>
    public static partial class ReforgeUIManager
    {
        // ============================================================================
        // 常量定义（避免魔法数字）
        // ============================================================================
        private const float UI_INIT_DELAY = 0.1f;
        private const float DIFF_THRESHOLD = 0.01f;
        private const string MAX_BOUND_LABEL_COLOR = "#FF4D4D";
        private const string MIN_BOUND_LABEL_COLOR = "#9AD8FF";

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
        private static Coroutine delayedUiBuildCoroutine;

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

        // 倾向滑块控件
        private static GameObject tendencySliderRoot;
        private static Slider tendencySlider;
        private static TextMeshProUGUI tendencyText;
        private static float currentTendencyChance = 0.5f;

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

        public static void CloseUIIfOwnedBy(Transform npcTransform)
        {
            if (!IsReforgeUIOwnedBy(npcTransform))
            {
                return;
            }

            CloseUI();
        }

        private static bool IsReforgeUIOwnedBy(Transform npcTransform)
        {
            if (npcTransform == null || currentController == null)
            {
                return false;
            }

            Transform currentTransform = currentController.transform;
            return currentTransform != null &&
                   (ReferenceEquals(currentTransform, npcTransform) ||
                    currentTransform.IsChildOf(npcTransform) ||
                    npcTransform.IsChildOf(currentTransform));
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
            ScheduleDelayedUIBuild();
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

        private static void ScheduleDelayedUIBuild()
        {
            if (ModBehaviour.Instance == null)
            {
                return;
            }

            if (delayedUiBuildCoroutine != null)
            {
                ModBehaviour.Instance.StopCoroutine(delayedUiBuildCoroutine);
            }

            delayedUiBuildCoroutine = ModBehaviour.Instance.StartCoroutine(BuildDelayedUIElements());
        }

        private static System.Collections.IEnumerator BuildDelayedUIElements()
        {
            yield return new WaitForEndOfFrame();
            yield return null;

            delayedUiBuildCoroutine = null;

            if (!isReforgeMode || decomposeView == null || !decomposeView.open)
            {
                yield break;
            }

            CreateTendencySliderUI();
            CreateColdQuenchFluidUI();
            UpdateProbabilityDisplay();
            UpdateColdQuenchFluidCount();
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

                // 3. 创建倾向滑块

                // 4. 修改分解按钮为重铸按钮
                ModifyDecomposeButton();

                // 5. 修改标题文本
                ModifyTitleText();

                // 6. 移除原版事件监听器，防止原版方法被调用
                RemoveOriginalEventListeners();

                // 7. 获取其他UI组件引用
                GetAdditionalUIReferences();

                // 8. 订阅我们自己的物品选择事件
                ItemUIUtilities.OnSelectionChanged += OnItemSelectionChanged;

                // 9. 创建冷淬液数量显示UI

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
                            existingProb.gameObject.SetActive(true);
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
        /// 计算当前倾向滑块所需的费用
        /// 0% 或 100% 需要 10000金币，50% 为0金币
        /// </summary>
        private static int GetTendencyCost()
        {
            float itemValue = selectedItem != null ? ReforgeSystem.GetItemValue(selectedItem) : 10000f;
            float maxCost = itemValue * 0.5f;
            float percentage = Mathf.Abs(currentTendencyChance - 0.5f) / 0.4f;
            return Mathf.RoundToInt(maxCost * Math.Min(1f, percentage));
        }

        /// <summary>
        /// 创建词条正负号倾向滑块

        /// </summary>
        private static void CreateTendencySliderUI()
        {
            try
            {
                if (moneySlider == null || probabilityText == null) return;
                Transform moneySliderParent = moneySlider.transform.parent;
                if (moneySliderParent == null) return;
                Transform tendencyParent = moneySliderParent.parent;
                if (tendencyParent == null) return;
                Transform existingTendency = tendencyParent.Find("ReforgeTendencySlider");
                GameObject tendencyObj;
                bool reusedExisting = existingTendency != null;
                if (reusedExisting)
                {
                    tendencyObj = existingTendency.gameObject;
                    tendencyObj.SetActive(true);
                }
                else
                {
                    tendencyObj = GameObject.Instantiate(moneySliderParent.gameObject);
                    tendencyObj.name = "ReforgeTendencySlider";
                    tendencyObj.SetActive(false);
                    tendencyObj.transform.SetParent(tendencyParent, false);
                    // Remove cloned gameplay scripts so the custom slider only keeps UI components.
                    MonoBehaviour[] allScripts = tendencyObj.GetComponentsInChildren<MonoBehaviour>(true);
                    foreach (var script in allScripts)
                    {
                        if (script is Slider || script is UnityEngine.UI.Image || script is TextMeshProUGUI)
                        {
                            continue;
                        }
                        GameObject.DestroyImmediate(script);
                    }
                }
                tendencySliderRoot = tendencyObj;
                tendencyObj.transform.SetSiblingIndex(moneySliderParent.GetSiblingIndex() + 1);
                if (probabilityText != null)
                {
                    probabilityText.transform.SetSiblingIndex(tendencyObj.transform.GetSiblingIndex() + 1);
                }
                Slider newSlider = tendencyObj.GetComponentInChildren<Slider>(true);
                if (newSlider != null)
                {
                    tendencySlider = newSlider;
                    tendencySlider.minValue = -40f;
                    tendencySlider.maxValue = 40f;
                    tendencySlider.wholeNumbers = true;
                    tendencySlider.onValueChanged.RemoveAllListeners();
                    tendencySlider.onValueChanged.AddListener(OnTendencySliderChanged);
                    tendencySlider.SetValueWithoutNotify(0f);
                    currentTendencyChance = 0.5f;
                }
                tendencyText = null;
                TextMeshProUGUI[] texts = tendencyObj.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var txt in texts)
                {
                    if (txt.gameObject.name.Contains("Title") || txt.text.Contains("\u6295\u5165") || txt.text.Contains("\u5206\u89e3") || txt.text.Contains("Decompose") || txt.text == "\u6295\u5165" || txt.text == "\u6b63\u8d1f\u6781\u6027\u503e\u5411")
                    {
                        txt.text = "\u6b63\u8d1f\u6781\u6027\u503e\u5411";
                        txt.gameObject.SetActive(true);
                        tendencyText = txt;
                    }
                    else if (txt.gameObject.name.IndexOf("value", StringComparison.OrdinalIgnoreCase) >= 0 || txt.text == "100")
                    {
                        txt.gameObject.SetActive(false);
                        txt.text = "";
                        if (txt.transform.parent != null && txt.transform.parent != tendencyObj.transform)
                        {
                            UnityEngine.UI.Image parentImg = txt.transform.parent.GetComponent<UnityEngine.UI.Image>();
                            if (parentImg != null)
                            {
                                parentImg.enabled = false;
                            }
                        }
                    }
                    else if (txt.gameObject.name.Contains("Min") || txt.gameObject.name.Contains("Max"))
                    {
                        txt.gameObject.SetActive(false);
                    }
                }
                if (tendencyText == null)
                {
                    foreach (var txt in texts)
                    {
                        if (txt.gameObject.name.Contains("Title"))
                        {
                            tendencyText = txt;
                            break;
                        }
                    }
                }
                OnTendencySliderChanged(0f);
                tendencyObj.SetActive(true);
                ModBehaviour.DevLog(reusedExisting
                    ? "[ReforgeUI] Tendency slider reused"
                    : "[ReforgeUI] Tendency slider created");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] Failed to create tendency slider: " + e.Message);
            }
        }
        private static void OnTendencySliderChanged(float value)
        {
            currentTendencyChance = 0.5f + (value / 40f) * 0.4f; // 转换为 0.1 ~ 0.9 的几率

            if (tendencyText != null)
            {
                if (value < -10f)
                    tendencyText.text = string.Format("<color=#FF4D4D>偏向负面 ({0})</color>", (int)value);
                else if (value > 10f)
                    tendencyText.text = string.Format("<color=#4DFF4D>偏向正面 (+{0})</color>", (int)value);
                else
                    tendencyText.text = string.Format("平衡 (0)");
            }

            UpdateReforgeButtonInteractable();
            UpdateProbabilityDisplay();
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
                CustomItemRuntimeStateHelper.EnsureCustomItemConfigured(selectedItem);
                ReforgeDataPersistence.CleanupUnsupportedReforgeData(selectedItem);

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
        /// 计算达到指定金钱加成所需的金钱量（MoneyBonus的逆向计算）
        /// </summary>
    }
}
