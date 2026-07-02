using System;
using System.Collections.Generic;
using HarmonyLib;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    internal static class ItemModifierDisplayTextFormatter
    {
        private const string ElementFactorPrefix = "ElementFactor_";

        internal static bool ShouldFormatAsElementFactorPercent(ModifierDescription modifier)
        {
            return modifier != null
                && modifier.Type == ModifierType.Add
                && !string.IsNullOrEmpty(modifier.Key)
                && modifier.Key.StartsWith(ElementFactorPrefix, StringComparison.Ordinal);
        }

        internal static string GetDisplayText(ModifierDescription modifier)
        {
            if (modifier == null)
            {
                return string.Empty;
            }

            if (ShouldFormatAsElementFactorPercent(modifier))
            {
                return FormatSignedPercent(modifier.Value);
            }

            return modifier.GetDisplayValueString(StatInfoDatabase.Get(modifier.Key).DisplayFormat);
        }

        private static string FormatSignedPercent(float value)
        {
            float percentValue = value * 100f;
            return string.Format("{0}{1:0.##}%", percentValue > 0f ? "+" : "", percentValue);
        }
    }
}

namespace BossRush.Patches.UI
{
    /// <summary>
    /// 修正悬浮提示里的 ElementFactor 固定抗性显示。
    /// 原版 GetPropertyValueTextPair() 对 Add 类型 modifier 直接显示原始值，
    /// 导致 -0.20 这类固定承伤倍率显示成小数。这里只把 ElementFactor_* 的 Add
    /// modifier 转成百分比文本，避免影响其他 Add 词条。
    /// </summary>
    [HarmonyPatch(typeof(ItemUIUtilities), "GetPropertyValueTextPair", new Type[] { typeof(Item) })]
    internal static class ItemUIUtilitiesElementFactorFormatPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Item item, ref List<ValueTuple<string, string, Polarity>> __result)
        {
            if (item == null || __result == null || item.Modifiers == null || __result.Count <= 0)
            {
                return;
            }

            int displayedModifierCount = CountDisplayedModifiers(item);
            if (displayedModifierCount <= 0 || displayedModifierCount > __result.Count)
            {
                return;
            }

            int resultIndex = __result.Count - displayedModifierCount;
            foreach (ModifierDescription modifier in item.Modifiers)
            {
                if (modifier == null || !modifier.Display)
                {
                    continue;
                }

                if (resultIndex >= __result.Count)
                {
                    break;
                }

                if (ItemModifierDisplayTextFormatter.ShouldFormatAsElementFactorPercent(modifier))
                {
                    ValueTuple<string, string, Polarity> current = __result[resultIndex];
                    __result[resultIndex] = new ValueTuple<string, string, Polarity>(
                        current.Item1,
                        ItemModifierDisplayTextFormatter.GetDisplayText(modifier),
                        current.Item3);
                }

                resultIndex++;
            }
        }

        private static int CountDisplayedModifiers(Item item)
        {
            int count = 0;
            foreach (ModifierDescription modifier in item.Modifiers)
            {
                if (modifier != null && modifier.Display)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
