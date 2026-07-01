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
    public static partial class ReforgeUIManager
    {
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
                        MethodInfo setupMethod = detailsDisplayObj.GetType().GetMethod("Setup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (setupMethod != null) setupMethod.Invoke(detailsDisplayObj, new object[] { playerItem });
                        return;
                    }

                    // 注意：GetCachedPrefab 已经调用了 ConfigureCustomEquipmentPrefab

                    // 2. 缓存预制体属性
                    cachedPrefabModifiers.Clear();
                    cachedPrefabStats.Clear();
                    cachedPrefabVariables.Clear();
                    Dictionary<string, int> prefabModifierOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
                    Dictionary<string, int> prefabStatOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
                    Dictionary<string, int> prefabVariableOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);

                    if (prefabItem.Modifiers != null)
                    {
                        foreach (var mod in prefabItem.Modifiers)
                        {
                            if (mod == null || string.IsNullOrEmpty(mod.Key)) continue;
                            cachedPrefabModifiers[BuildSnapshotKey(mod.Key, PropertyType.Modifier, GetNextEntryOrdinal(prefabModifierOrdinals, mod.Key))] = mod.Value;
                        }
                    }
                    if (prefabItem.Stats != null)
                    {
                        foreach (var stat in prefabItem.Stats)
                        {
                            if (stat == null || string.IsNullOrEmpty(stat.Key)) continue;
                            cachedPrefabStats[BuildSnapshotKey(stat.Key, PropertyType.Stat, GetNextEntryOrdinal(prefabStatOrdinals, stat.Key))] = stat.Value;
                        }
                    }
                    if (prefabItem.Variables != null)
                    {
                        foreach (var variable in prefabItem.Variables)
                        {
                            if (variable == null || string.IsNullOrEmpty(variable.Key)) continue;
                            if (variable.DataType == Duckov.Utilities.CustomDataType.Float)
                            {
                                try { cachedPrefabVariables[BuildSnapshotKey(variable.Key, PropertyType.Variable, GetNextEntryOrdinal(prefabVariableOrdinals, variable.Key))] = variable.GetFloat(); } catch { }
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
                Dictionary<string, int> playerModifierOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
                Dictionary<string, int> playerStatOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
                Dictionary<string, int> playerVariableOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);

                if (playerItem.Modifiers != null)
                {
                    foreach (var mod in playerItem.Modifiers)
                    {
                        if (mod == null || string.IsNullOrEmpty(mod.Key)) continue;
                        _reusablePlayerModifiers[BuildSnapshotKey(mod.Key, PropertyType.Modifier, GetNextEntryOrdinal(playerModifierOrdinals, mod.Key))] = mod.Value;
                    }
                }
                if (playerItem.Stats != null)
                {
                    foreach (var stat in playerItem.Stats)
                    {
                        if (stat == null || string.IsNullOrEmpty(stat.Key)) continue;
                        _reusablePlayerStats[BuildSnapshotKey(stat.Key, PropertyType.Stat, GetNextEntryOrdinal(playerStatOrdinals, stat.Key))] = stat.Value;
                    }
                }
                if (playerItem.Variables != null)
                {
                    foreach (var variable in playerItem.Variables)
                    {
                        if (variable == null || string.IsNullOrEmpty(variable.Key)) continue;
                        if (variable.DataType == Duckov.Utilities.CustomDataType.Float)
                        {
                            try { _reusablePlayerVariables[BuildSnapshotKey(variable.Key, PropertyType.Variable, GetNextEntryOrdinal(playerVariableOrdinals, variable.Key))] = variable.GetFloat(); } catch { }
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
                    MethodInfo setup = detailsDisplayObj.GetType().GetMethod("Setup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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
                        playerItem,
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
                    MethodInfo setupMethod = detailsDisplayObj.GetType().GetMethod("Setup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (setupMethod != null) setupMethod.Invoke(detailsDisplayObj, new object[] { playerItem });
                }
                catch { }
            }
        }

        /// <summary>
        /// 延迟修改属性文本以显示差异（使用缓存的反射字段，避免重复反射）
        /// </summary>
        private static System.Collections.IEnumerator ModifyPropertyTextsDelayed(
            Item playerItem,
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

                    string key;
                    PropertyType propType;
                    int entryOrdinal;
                    TextMeshProUGUI valueText;
                    if (!TryGetDisplayedEntryInfo(child, out key, out propType, out entryOrdinal, out valueText))
                    {
                        continue;
                    }

                    float prefabValue;
                    if (!TryGetComparisonValue(key, propType, entryOrdinal, prefabModifiers, prefabStats, prefabVariables, out prefabValue))
                    {
                        continue;
                    }

                    float playerValue;
                    if (!TryGetComparisonValue(key, propType, entryOrdinal, playerModifiers, playerStats, playerVariables, out playerValue))
                    {
                        continue;
                    }

                    float diff = playerValue - prefabValue;
                    ModBehaviour.DevLog(string.Format("[ReforgeUI] 属性 {0}: player={1}, prefab={2}, diff={3}",
                        key, playerValue, prefabValue, diff));

                    if (Mathf.Abs(diff) < DIFF_THRESHOLD) continue;

                    string baseText;
                    if (!TryGetCurrentItemPropertyDisplayText(playerItem, key, propType, entryOrdinal, out baseText))
                    {
                        baseText = valueText.text;
                    }

                    int colorTagIndex = baseText.IndexOf(" <color=");
                    if (colorTagIndex > 0)
                    {
                        baseText = baseText.Substring(0, colorTagIndex);
                    }

                    // 显示差异: 预制体值 (↑/↓ xx)，保留两位小数
                    string colorHex = diff > 0 ? "#66FF66" : "#FF6666";

                    string newText = baseText + BuildPropertyDiffMarkup(key, prefabValue, playerValue, diff, colorHex, true);
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
                CustomItemRuntimeStateHelper.EnsureCustomItemConfigured(prefabItem);

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
            StopCurrentDiffCoroutine();
            if (delayedUiBuildCoroutine != null && ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StopCoroutine(delayedUiBuildCoroutine);
                delayedUiBuildCoroutine = null;
            }

            // 恢复原版事件监听器
            RestoreOriginalEventListeners();

            // 恢复原版结果显示
            RestoreOriginalEventListeners();
            if (resultDisplayObj != null)
            {
                resultDisplayObj.SetActive(true);
            }

            // 清理概率显示
            if (probabilityText != null && probabilityText.gameObject != null)
            {
                probabilityText.gameObject.SetActive(false);
            }
            if (tendencySliderRoot != null)
            {
                tendencySliderRoot.SetActive(false);
            }


            originalProperties.Clear();
            selectedItem = null;
            decomposeView = null;
            countSliderObj = null;
            moneySlider = null;
            probabilityText = null;
            reforgeButton = null;
            tendencySliderRoot = null;
            tendencySlider = null;
            tendencyText = null;
            currentTendencyChance = 0.5f;
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

        public static void ResetStaticCaches()
        {
            Cleanup();

            currentController = null;
            delayedUiBuildCoroutine = null;
            originalOnFastPick = null;
            currentDiffCoroutine = null;

            _detailsDisplayField = null;
            _propertiesParentField = null;
            _cannotDecomposeField = null;
            _modEntryTargetField = null;
            _modEntryValueField = null;
            _modDescKeyProp = null;
            _modDescValueProp = null;
            _varEntryTargetField = null;
            _varEntryValueField = null;
            _customDataKeyProp = null;
            _statEntryTargetField = null;
            _statEntryValueField = null;
            _statKeyProp = null;
            _reflectionCacheInitialized = false;
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
                    coldQuenchFluidContainer.SetActive(true);
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

                    // 检查 ItemVariableEntry（用于逆鳞等自定义装备的 Variables 属性）
                    // 只有数字类型（Float）的 Variable 才能被固定
                    if (key == null)
                    {
                        Component varEntry = child.GetComponent("ItemVariableEntry");
                        if (varEntry != null)
                        {
                            key = GetPropertyKeyFromEntry(varEntry, "ItemVariableEntry");
                            propType = PropertyType.Variable;
                            // 检查 Variable 是否为数字类型（Float）
                            hasNumericValue = IsVariableNumeric(selectedItem, key);
                        }
                    }

                    // 跳过无效条目
                    if (string.IsNullOrEmpty(key) || !hasNumericValue) continue;

                    // 跳过系统变量
                    if (key == "Count" || key == "ReforgeCount" || key.StartsWith("RF_")) continue;
                    if (!ReforgeSystem.IsPropertySupportedForReforge(key, propType)) continue;

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
        /// 检查 Variable 是否为数字类型（Float）
        /// 只有数字类型的属性才能被固定
        /// </summary>
        /// <param name="item">物品</param>
        /// <param name="variableKey">Variable 键名</param>
        /// <returns>是否为数字类型</returns>
        private static bool IsVariableNumeric(Item item, string variableKey)
        {
            if (item == null || item.Variables == null || string.IsNullOrEmpty(variableKey))
            {
                return false;
            }

            try
            {
                foreach (var variable in item.Variables)
                {
                    if (variable.Key == variableKey)
                    {
                        // 只有 Float 类型才是数字类型
                        return variable.DataType == Duckov.Utilities.CustomDataType.Float;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] 检查 Variable 类型失败: " + e.Message);
            }

            return false;
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
                coldQuenchFluidContainer.SetActive(false);
            }
            coldQuenchFluidCountText = null;
        }
    }

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
