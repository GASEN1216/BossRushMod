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
        private static int CalculateMoneyForBonus(float targetBonus, float itemValue)
        {
            if (targetBonus <= 0) return 0;
            if (targetBonus >= 1.0f) return Mathf.RoundToInt(itemValue * ReforgeSystem.MONEY_BONUS_TIER3_MULTIPLIER);

            float tier1 = itemValue * ReforgeSystem.MONEY_BONUS_TIER1_MULTIPLIER;
            float tier2 = itemValue * ReforgeSystem.MONEY_BONUS_TIER2_MULTIPLIER;
            float tier3 = itemValue * ReforgeSystem.MONEY_BONUS_TIER3_MULTIPLIER;

            if (targetBonus <= 0.10f)
            {
                // B = 0.10 * (m / tier1) => m = B * tier1 / 0.10
                return Mathf.RoundToInt(targetBonus * tier1 / 0.10f);
            }
            else if (targetBonus <= 0.30f)
            {
                // B = 0.10 + 0.20 * (logM - logT1) / (logT2 - logT1)
                // (B - 0.10) / 0.20 = (logM - logT1) / (logT2 - logT1)
                float t = (targetBonus - 0.10f) / 0.20f;
                float logT1 = Mathf.Log10(tier1);
                float logT2 = Mathf.Log10(tier2);
                float logM = logT1 + t * (logT2 - logT1);
                return Mathf.RoundToInt(Mathf.Pow(10f, logM));
            }
            else
            {
                // B = 0.30 + 0.70 * (logM - logT2) / (logT3 - logT2)
                float t = (targetBonus - 0.30f) / 0.70f;
                float logT2 = Mathf.Log10(tier2);
                float logT3 = Mathf.Log10(tier3);
                float logM = logT2 + t * (logT3 - logT2);
                return Mathf.RoundToInt(Mathf.Pow(10f, logM));
            }
        }

        /// <summary>
        /// 重置UI状态（滑块、按钮、概率显示）
        /// </summary>
        private static void ResetUIState(long maxMoney)
        {
            // 计算基础费用（物品价值的1/10，应用哥布林好感度折扣）
            int baseCost = selectedItem != null ? ReforgeSystem.GetDiscountedCost(selectedItem) : ReforgeSystem.MIN_REFORGE_COST;

            long optimalMagMaxSliderValue = maxMoney;
            if (selectedItem != null)
            {
                float itemValue = ReforgeSystem.GetItemValue(selectedItem);
                int rarity = selectedItem.Quality;
                // 计算当前物品不用投钱时的原始概率
                float pItem = ReforgeSystem.BASE_PROBABILITY * ReforgeSystem.RarityFactor(rarity) * ReforgeSystem.ValueFactor(itemValue);
                // 需要的金钱加成才能达到100% (即1.0)
                float requiredBonusToMax = 1.0f - pItem;

                if (requiredBonusToMax <= 0)
                {
                    optimalMagMaxSliderValue = baseCost;
                }
                else
                {
                    int requiredMoney = CalculateMoneyForBonus(requiredBonusToMax, itemValue);
                    optimalMagMaxSliderValue = Mathf.Max(baseCost, requiredMoney);
                }
            }

            // 重置滑块
            if (moneySlider != null)
            {
                moneySlider.minValue = baseCost;
                moneySlider.maxValue = optimalMagMaxSliderValue;
                moneySlider.value = baseCost;
                moneySlider.wholeNumbers = true;
                currentMoney = baseCost;

                if (sliderValueText != null)
                    sliderValueText.text = baseCost.ToString();
                if (sliderMinText != null)
                    sliderMinText.text = baseCost.ToString();
                if (sliderMaxText != null)
                    sliderMaxText.text = optimalMagMaxSliderValue.ToString();
            }

            UpdateUIStateCommon();
        }

        /// <summary>
        /// 更新UI状态但保留滑块值（用于重铸完成后，方便玩家多次快速重铸）
        /// </summary>
        private static void UpdateUIStateKeepSlider(long maxMoney)
        {
            // 计算基础费用（物品价值的1/10，应用哥布林好感度折扣）
            int baseCost = selectedItem != null ? ReforgeSystem.GetDiscountedCost(selectedItem) : ReforgeSystem.MIN_REFORGE_COST;

            long optimalMagMaxSliderValue = maxMoney;
            if (selectedItem != null)
            {
                float itemValue = ReforgeSystem.GetItemValue(selectedItem);
                int rarity = selectedItem.Quality;
                // 计算当前物品不用投钱时的原始概率
                float pItem = ReforgeSystem.BASE_PROBABILITY * ReforgeSystem.RarityFactor(rarity) * ReforgeSystem.ValueFactor(itemValue);
                // 需要的金钱加成才能达到100% (即1.0)
                float requiredBonusToMax = 1.0f - pItem;

                if (requiredBonusToMax <= 0)
                {
                    optimalMagMaxSliderValue = baseCost;
                }
                else
                {
                    int requiredMoney = CalculateMoneyForBonus(requiredBonusToMax, itemValue);
                    optimalMagMaxSliderValue = Mathf.Max(baseCost, requiredMoney);
                }
            }

            if (moneySlider != null)
            {
                // 保留当前滑块值，只更新最大值
                float currentValue = moneySlider.value;
                moneySlider.minValue = baseCost;
                moneySlider.maxValue = optimalMagMaxSliderValue;

                // 如果当前值超过新的最大值，则调整为最大值
                if (currentValue > optimalMagMaxSliderValue)
                {
                    moneySlider.value = optimalMagMaxSliderValue;
                    currentMoney = (int)optimalMagMaxSliderValue;
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
                    sliderMaxText.text = optimalMagMaxSliderValue.ToString();
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

            // 计算基础费用（应用哥布林好感度折扣）
            int baseCost = ReforgeSystem.GetDiscountedCost(selectedItem);
            long playerMoney = GetPlayerMoney();
            float discount = ReforgeSystem.GetCurrentDiscount();
            int tendencyCost = GetTendencyCost();
            int totalCost = currentMoney + tendencyCost;

            // 检查玩家金钱是否足够支付基础费用
            bool canAfford = playerMoney >= totalCost && playerMoney >= baseCost;
            reforgeButton.interactable = canAfford;

            // 如果金钱不足，更新概率显示提示
            if (!canAfford && probabilityText != null)
            {
                bool usePurification = currentController != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(currentController);
                string currencyName = usePurification ? "净化点" : "金钱";
                string discountInfo = discount > 0 ? string.Format(" ({0:P0}折扣)", discount) : "";
                probabilityText.text = string.Format("<color=#FF4D4D>{6}不足！\n基础费用: {0}{1}\n投入: {2}\n极性滑块花费: {3}\n所需总额: {4}\n你的当前总{6}: {5}</color>",
                    baseCost, discountInfo, currentMoney, tendencyCost, totalCost, playerMoney, currencyName);
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
        private static System.Collections.IEnumerator ResetUIStateDelayed(long maxMoney, int frameDelay = 1)
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
            // 极性费用: XX
            // 负向概率: XX%  正向概率: XX%
            // 概率: 0.20×X.XX×X.XX+X.XX = XX%
            // 总计花费: XX

            int tendencyCost = GetTendencyCost();
            string tendencyLine = string.Format("极性费用: {0}\n", tendencyCost);
            bool usePurification = currentController != null &&
                ModBehaviour.Instance != null &&
                ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(currentController);
            string investLabel = usePurification ? "净化点投入" : "投入";
            string totalCostLabel = usePurification ? "总计花费(净化点)" : "总计花费";

            float posProb = currentTendencyChance;
            float negProb = 1.0f - currentTendencyChance;
            string polarityProbLine = string.Format("<color=#00FFFF>负向概率: {0:P0}   正向概率: {1:P0}</color>\n", negProb, posProb);

            int totalCost = currentMoney + tendencyCost;
            string totalCostLine = string.Format("<color=#FFFF00>{0}: {1}</color>", totalCostLabel, totalCost);

            probabilityText.text = string.Format(
                "品质: {0} (系数: {1:F2})\n" +
                "价值: {2:F0} (系数: {3:F2})\n" +
                "{11}: {4} (加成: {5:F2})\n" +
                "{8}" +
                "{9}" +
                "<color={6}>幅度乘数参数: 0.20×{1:F2}×{3:F2}+{5:F2}={7:P0}</color>\n" +
                "{10}",
                rarity, rarityFactor,           // {0}, {1}
                itemValue, valueFactor,         // {2}, {3}
                currentMoney, moneyBonus,       // {4}, {5}
                probColorHex, p,                // {6}, {7}
                tendencyLine, polarityProbLine, // {8}, {9}
                totalCostLine,                  // {10}
                investLabel                     // {11}
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

            int totalCost = currentMoney + GetTendencyCost();
            if (totalCost <= 0 && currentTendencyChance == 0.5f)
            {
                // Can be 0 if both sliders are 0 and 50%
                ModBehaviour.DevLog("[ReforgeUI] 投入金钱为0，正常重铸");
            }
            else if (totalCost < 0)
            {
                return;
            }

            ModBehaviour.DevLog("[ReforgeUI] 点击重铸按钮: " + selectedItem.DisplayName + ", 总费用: " + totalCost);

            // 播放重铸音效
            BossRushAudioManager.Instance.PlayReforgeSFX();

            isReforging = true;

            bool paidWithPurification = false;
            bool reforgeCompleted = false;

            try
            {
                // 扣除金钱
                if (totalCost > 0)
                {
                    bool paid;
                    bool usePurificationPayment = currentController != null &&
                        ModBehaviour.Instance != null &&
                        ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(currentController);

                    if (usePurificationPayment)
                    {
                        paid = ModBehaviour.Instance.TrySpendZombieModePurificationPointsForRealNpc(
                            currentController,
                            totalCost,
                            "ZombieModeTempGoblinReforge");
                        paidWithPurification = paid;
                    }
                    else
                    {
                        Cost cost = new Cost((long)totalCost);
                        paid = EconomyManager.Pay(cost, true, true);
                    }

                    if (!paid)
                    {
                        ModBehaviour.DevLog("[ReforgeUI] 金钱不足");
                        isReforging = false;
                        return;
                    }
                }

                // 执行重铸，传入当前的倾向几率
                var result = ReforgeSystem.Reforge(selectedItem, currentMoney, "player", currentTendencyChance);
                reforgeCompleted = true;

                // 显示属性变化（现在重铸必定成功）
                ShowPropertyChanges();

                // 立即更新UI状态（金钱已扣除，保留滑块值方便多次快速重铸）
                long newMax = GetPlayerMoney();
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
                if (ModBehaviour.Instance != null)
                {
                    ModBehaviour.Instance.RefundZombieModePurificationPointsForRealNpc(currentController, totalCost, paidWithPurification && !reforgeCompleted);
                }

                if (paidWithPurification && !reforgeCompleted)
                {
                    try
                    {
                        UpdateUIStateKeepSlider(GetPlayerMoney());
                    }
                    catch (Exception refreshError)
                    {
                        ModBehaviour.DevLog("[ReforgeUI] [WARNING] 重铸失败后刷新净化点 UI 失败: " + refreshError.Message);
                    }

                    ModBehaviour.DevLog("[ReforgeUI] 重铸异常失败，已回退净化点: " + totalCost);
                }

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
                if (item == null) return;
                Item prefab = GetCachedPrefab(item.TypeID);
                Dictionary<string, int> modifierOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
                Dictionary<string, int> statOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
                Dictionary<string, int> variableOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);

                if (item.Modifiers != null)
                {
                    foreach (var mod in item.Modifiers)
                    {
                        if (mod == null || string.IsNullOrEmpty(mod.Key)) continue;

                        int entryOrdinal = GetNextEntryOrdinal(modifierOrdinals, mod.Key);
                        if (!ReforgeSystem.IsModifierEligibleForReforge(item, prefab, mod)) continue;

                        PropertySnapshot snapshot = new PropertySnapshot
                        {
                            Key = mod.Key,
                            Value = mod.Value,
                            PropType = PropertyType.Modifier,
                            EntryOrdinal = entryOrdinal
                        };
                        originalProperties.Add(snapshot);
                    }
                }

                if (item.Stats != null)
                {
                    foreach (var stat in item.Stats)
                    {
                        if (stat == null || string.IsNullOrEmpty(stat.Key)) continue;

                        int entryOrdinal = GetNextEntryOrdinal(statOrdinals, stat.Key);
                        if (!ReforgeSystem.IsStatEligibleForReforge(stat)) continue;

                        PropertySnapshot snapshot = new PropertySnapshot
                        {
                            Key = stat.Key,
                            Value = stat.BaseValue,
                            PropType = PropertyType.Stat,
                            EntryOrdinal = entryOrdinal
                        };
                        originalProperties.Add(snapshot);
                    }
                }

                if (item.Variables != null)
                {
                    foreach (var variable in item.Variables)
                    {
                        if (variable == null || string.IsNullOrEmpty(variable.Key)) continue;

                        int entryOrdinal = GetNextEntryOrdinal(variableOrdinals, variable.Key);
                        if (!ReforgeSystem.IsVariableEligibleForReforge(variable)) continue;

                        float value;
                        try
                        {
                            value = variable.GetFloat();
                        }
                        catch
                        {
                            continue;
                        }

                        PropertySnapshot snapshot = new PropertySnapshot
                        {
                            Key = variable.Key,
                            Value = value,
                            PropType = PropertyType.Variable,
                            EntryOrdinal = entryOrdinal
                        };
                        originalProperties.Add(snapshot);
                    }
                }

                ModBehaviour.DevLog("[ReforgeUI] 保存了 " + originalProperties.Count + " 个属性快照");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ReforgeUI] [ERROR] 保存属性快照失败: " + e.Message);
            }
        }

        private static int GetNextEntryOrdinal(Dictionary<string, int> ordinalMap, string key)
        {
            if (ordinalMap == null || string.IsNullOrEmpty(key))
            {
                return 0;
            }

            int nextOrdinal;
            if (!ordinalMap.TryGetValue(key, out nextOrdinal))
            {
                ordinalMap[key] = 1;
                return 0;
            }

            ordinalMap[key] = nextOrdinal + 1;
            return nextOrdinal;
        }

        private static string BuildSnapshotKey(string key, PropertyType propType, int entryOrdinal)
        {
            return ((int)propType).ToString() + ":" + key + ":" + entryOrdinal.ToString();
        }

        private static bool TryGetDisplayedEntryIdentity(Transform child, out string key, out PropertyType propType, out TextMeshProUGUI valueText)
        {
            key = null;
            propType = PropertyType.Modifier;
            valueText = null;

            if (child == null)
            {
                return false;
            }

            Component modEntry = child.GetComponent("ItemModifierEntry");
            if (modEntry != null)
            {
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

                propType = PropertyType.Modifier;
                return !string.IsNullOrEmpty(key) && valueText != null;
            }

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

                propType = PropertyType.Variable;
                return !string.IsNullOrEmpty(key) && valueText != null;
            }

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

                propType = PropertyType.Stat;
                return !string.IsNullOrEmpty(key) && valueText != null;
            }

            return false;
        }

        private static bool TryGetDisplayedEntryInfo(Transform child, out string key, out PropertyType propType, out int entryOrdinal, out TextMeshProUGUI valueText)
        {
            entryOrdinal = 0;
            if (!TryGetDisplayedEntryIdentity(child, out key, out propType, out valueText))
            {
                return false;
            }

            Transform parent = child.parent;
            if (parent == null)
            {
                return true;
            }

            foreach (Transform sibling in parent)
            {
                if (sibling == child)
                {
                    break;
                }

                if (sibling == null || !sibling.gameObject.activeInHierarchy)
                {
                    continue;
                }

                string siblingKey;
                PropertyType siblingType;
                TextMeshProUGUI siblingValueText;
                if (!TryGetDisplayedEntryIdentity(sibling, out siblingKey, out siblingType, out siblingValueText))
                {
                    continue;
                }

                if (siblingType == propType && siblingKey == key)
                {
                    entryOrdinal++;
                }
            }

            return true;
        }

        private static bool TryGetCurrentItemPropertyValue(Item item, string key, PropertyType propType, int entryOrdinal, out float value)
        {
            value = 0f;
            if (item == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            switch (propType)
            {
                case PropertyType.Modifier:
                    if (item.Modifiers == null) return false;
                    int modifierIndex = 0;
                    foreach (var mod in item.Modifiers)
                    {
                        if (mod.Key == key)
                        {
                            if (modifierIndex != entryOrdinal)
                            {
                                modifierIndex++;
                                continue;
                            }

                            value = mod.Value;
                            return true;
                        }
                    }
                    break;

                case PropertyType.Stat:
                    if (item.Stats == null) return false;
                    int statIndex = 0;
                    foreach (var stat in item.Stats)
                    {
                        if (stat.Key == key)
                        {
                            if (statIndex != entryOrdinal)
                            {
                                statIndex++;
                                continue;
                            }

                            value = stat.BaseValue;
                            return true;
                        }
                    }
                    break;

                case PropertyType.Variable:
                    if (item.Variables == null) return false;
                    int variableIndex = 0;
                    foreach (var variable in item.Variables)
                    {
                        if (variable.Key != key) continue;

                        if (variableIndex != entryOrdinal)
                        {
                            variableIndex++;
                            continue;
                        }

                        try
                        {
                            value = variable.GetFloat();
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    break;
            }

            return false;
        }

        private static bool TryGetCachedPrefabValue(string key, PropertyType propType, int entryOrdinal, out float value)
        {
            string snapshotKey = BuildSnapshotKey(key, propType, entryOrdinal);
            switch (propType)
            {
                case PropertyType.Modifier:
                    return cachedPrefabModifiers.TryGetValue(snapshotKey, out value);

                case PropertyType.Stat:
                    return cachedPrefabStats.TryGetValue(snapshotKey, out value);

                case PropertyType.Variable:
                    return cachedPrefabVariables.TryGetValue(snapshotKey, out value);

                default:
                    value = 0f;
                    return false;
            }
        }

        private static bool TryGetComparisonValue(
            string key,
            PropertyType propType,
            int entryOrdinal,
            Dictionary<string, float> modifiers,
            Dictionary<string, float> stats,
            Dictionary<string, float> variables,
            out float value)
        {
            string snapshotKey = BuildSnapshotKey(key, propType, entryOrdinal);
            switch (propType)
            {
                case PropertyType.Modifier:
                    return modifiers.TryGetValue(snapshotKey, out value);

                case PropertyType.Stat:
                    return stats.TryGetValue(snapshotKey, out value);

                case PropertyType.Variable:
                    return variables.TryGetValue(snapshotKey, out value);

                default:
                    value = 0f;
                    return false;
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
                    oldProps[BuildSnapshotKey(snap.Key, snap.PropType, snap.EntryOrdinal)] = snap;
                }

                // 获取propertiesParent来查找属性条目
                var propsParentField = PropertiesParentField;
                if (propsParentField != null)
                {
                    Transform propsParent = propsParentField.GetValue(detailsDisplay) as Transform;
                    if (propsParent != null)
                    {
                        foreach (Transform child in propsParent)
                        {
                            string key;
                            PropertyType propType;
                            int entryOrdinal;
                            TextMeshProUGUI valueText;
                            if (!TryGetDisplayedEntryInfo(child, out key, out propType, out entryOrdinal, out valueText))
                            {
                                continue;
                            }

                            PropertySnapshot oldSnapshot;
                            if (!oldProps.TryGetValue(BuildSnapshotKey(key, propType, entryOrdinal), out oldSnapshot))
                            {
                                continue;
                            }

                            float newValue;
                            if (!TryGetCurrentItemPropertyValue(selectedItem, key, propType, entryOrdinal, out newValue))
                            {
                                continue;
                            }

                            float diff = newValue - oldSnapshot.Value;
                            if (Mathf.Abs(diff) > 0.001f)
                            {
                                string baseText = valueText.text;
                                int colorTagIndex = baseText.IndexOf(" <color=");
                                if (colorTagIndex > 0)
                                {
                                    baseText = baseText.Substring(0, colorTagIndex);
                                }

                                string colorHex = diff > 0 ? "#66FF66" : "#FF6666";
                                float prefabValue;
                                string diffMarkup = TryGetCachedPrefabValue(key, propType, entryOrdinal, out prefabValue)
                                    ? BuildPropertyDiffMarkup(key, prefabValue, newValue, diff, colorHex, false)
                                    : string.Format(" <color={0}>({1}{2})</color>",
                                        colorHex,
                                        diff > 0 ? "↑" : "↓",
                                        Mathf.Abs(diff).ToString("F2"));

                                valueText.text = baseText + diffMarkup;

                                string logArrow = diff > 0 ? "↑" : "↓";
                                string logColor = diff > 0 ? "绿" : "红";
                                ModBehaviour.DevLog("[ReforgeUI] 属性变化: " + key + " " + logArrow + " " + Mathf.Abs(diff).ToString("F2") + " (" + logColor + ")");
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
        /// 生成属性差值的富文本标记，并在触达上下限时追加 Max/Min 标签。
        /// </summary>
        private static string BuildPropertyDiffMarkup(string key, float prefabValue, float currentValue, float diff, string diffColorHex, bool insertSpaceBetweenArrowAndValue)
        {
            string arrow = diff > 0 ? "↑" : "↓";
            string diffStr = Mathf.Abs(diff).ToString("F2");
            string separator = insertSpaceBetweenArrowAndValue ? " " : string.Empty;

            return string.Format(" <color={0}>({1}{2}{3})</color>{4}",
                diffColorHex,
                arrow,
                separator,
                diffStr,
                GetReforgeBoundLabelMarkup(key, prefabValue, currentValue, diff));
        }

        /// <summary>
        /// 如果当前数值已经达到重铸边界，则返回对应的 Max/Min 标签。
        /// </summary>
        private static string GetReforgeBoundLabelMarkup(string key, float prefabValue, float currentValue, float diff)
        {
            if (diff > 0 && ReforgeSystem.IsValueAtUpperBound(key, prefabValue, currentValue))
            {
                return string.Format(" <color={0}>Max</color>", MAX_BOUND_LABEL_COLOR);
            }

            if (diff < 0 && ReforgeSystem.IsValueAtLowerBound(key, prefabValue, currentValue))
            {
                return string.Format(" <color={0}>Min</color>", MIN_BOUND_LABEL_COLOR);
            }

            return string.Empty;
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
        private static long GetPlayerMoney()
        {
            try
            {
                if (currentController != null &&
                    ModBehaviour.Instance != null &&
                    ModBehaviour.Instance.IsZombieModeTemporaryRealNpc(currentController))
                {
                    return ModBehaviour.Instance.GetZombieModePurificationPointsForRealNpcUi(currentController);
                }

                // EconomyManager.Money 是 long；玩家余额超过 int.MaxValue(约21.4亿)时
                // 强转 int 会溢出为负数，导致重铸页面显示负数金额。此处保持 long。
                return EconomyManager.Money;
            }
            catch { }
            return 0;
        }

    }
}
