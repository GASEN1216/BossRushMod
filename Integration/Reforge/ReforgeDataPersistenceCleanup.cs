using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    public static partial class ReforgeDataPersistence
    {
        private static bool TryParseReforgeDataKey(string variableKey, out PropertyType propertyType, out string propertyKey)
        {
            propertyType = PropertyType.Modifier;
            propertyKey = null;

            if (string.IsNullOrEmpty(variableKey))
            {
                return false;
            }

            if (variableKey.StartsWith(MODIFIER_PREFIX, StringComparison.Ordinal))
            {
                propertyType = PropertyType.Modifier;
                propertyKey = variableKey.Substring(MODIFIER_PREFIX.Length);
                return !string.IsNullOrEmpty(propertyKey);
            }

            if (variableKey.StartsWith(STAT_PREFIX, StringComparison.Ordinal))
            {
                propertyType = PropertyType.Stat;
                propertyKey = variableKey.Substring(STAT_PREFIX.Length);
                return !string.IsNullOrEmpty(propertyKey);
            }

            if (variableKey.StartsWith(VARIABLE_PREFIX, StringComparison.Ordinal))
            {
                propertyType = PropertyType.Variable;
                propertyKey = variableKey.Substring(VARIABLE_PREFIX.Length);
                return !string.IsNullOrEmpty(propertyKey);
            }

            return false;
        }

        /// <summary>
        /// 清理已经持久化但当前不再允许参与重铸的 RF_ 数据，
        /// 同时将对应属性回滚到预制体默认值。
        /// </summary>
        public static void CleanupUnsupportedReforgeData(Item item)
        {
            if (item == null || item.Variables == null)
            {
                return;
            }

            List<CustomData> entriesToRemove = null;

            foreach (var variable in item.Variables)
            {
                if (variable == null)
                {
                    continue;
                }

                PropertyType propertyType;
                string propertyKey;
                if (!TryParseReforgeDataKey(variable.Key, out propertyType, out propertyKey))
                {
                    continue;
                }

                if (ReforgeSystem.IsPropertySupportedForReforge(propertyKey, propertyType))
                {
                    continue;
                }

                ResetPropertyToPrefabValue(item, propertyKey, propertyType);

                if (entriesToRemove == null)
                {
                    entriesToRemove = new List<CustomData>();
                }

                entriesToRemove.Add(variable);
                ModBehaviour.DevLog($"[ReforgeData] 已清理不支持的重铸数据: {item.DisplayName}.{variable.Key}");
            }

            if (entriesToRemove == null)
            {
                return;
            }

            for (int i = 0; i < entriesToRemove.Count; i++)
            {
                item.Variables.Remove(entriesToRemove[i]);
            }
        }

        private static void ResetPropertyToPrefabValue(Item item, string propertyKey, PropertyType propertyType)
        {
            if (item == null || string.IsNullOrEmpty(propertyKey))
            {
                return;
            }

            Item prefab = GetItemPrefab(item);

            switch (propertyType)
            {
                case PropertyType.Modifier:
                    if (item.Modifiers == null)
                    {
                        return;
                    }

                    float prefabModifierValue = GetPrefabModifierValue(prefab, propertyKey);
                    foreach (var mod in item.Modifiers)
                    {
                        if (mod != null && mod.Key == propertyKey)
                        {
                            ReforgeSystem.ApplyModifierValueChangePublic(mod, prefabModifierValue);
                            return;
                        }
                    }
                    break;

                case PropertyType.Stat:
                    if (item.Stats == null)
                    {
                        return;
                    }

                    float prefabStatValue = GetPrefabStatValue(prefab, propertyKey);
                    foreach (var stat in item.Stats)
                    {
                        if (stat != null && stat.Key == propertyKey)
                        {
                            ReforgeSystem.ApplyStatValueChangePublic(stat, prefabStatValue);
                            return;
                        }
                    }
                    break;

                case PropertyType.Variable:
                    if (item.Variables == null)
                    {
                        return;
                    }

                    float prefabVariableValue = GetPrefabVariableValue(prefab, propertyKey);
                    foreach (var variable in item.Variables)
                    {
                        if (variable != null && variable.Key == propertyKey)
                        {
                            try
                            {
                                variable.SetFloat(prefabVariableValue);
                            }
                            catch
                            {
                            }
                            return;
                        }
                    }
                    break;
            }
        }

        private static HashSet<string> GetTrackedPropertyKeys(Item item, string prefix)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);

            if (item == null || item.Variables == null || string.IsNullOrEmpty(prefix))
            {
                return result;
            }

            foreach (var variable in item.Variables)
            {
                if (variable == null || string.IsNullOrEmpty(variable.Key))
                {
                    continue;
                }

                if (!variable.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string propertyKey = variable.Key.Substring(prefix.Length);
                if (!string.IsNullOrEmpty(propertyKey))
                {
                    result.Add(propertyKey);
                }
            }

            return result;
        }

        /// <summary>
        /// 获取物品的预制体
        /// </summary>
        private static Item GetItemPrefab(Item item)
        {
            if (item == null) return null;
            try
            {
                int typeId = item.TypeID;
                if (typeId <= 0) return null;
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab != null)
                {
                    CustomItemRuntimeStateHelper.EnsureCustomItemConfigured(prefab);
                }
                return prefab;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取预制体的 Modifier 值
        /// </summary>
        private static float GetPrefabModifierValue(Item prefab, string key)
        {
            if (prefab == null || prefab.Modifiers == null) return 0f;
            foreach (var mod in prefab.Modifiers)
            {
                if (mod.Key == key) return mod.Value;
            }
            return 0f;
        }

        /// <summary>
        /// 获取预制体的 Stat 值
        /// </summary>
        private static float GetPrefabStatValue(Item prefab, string key)
        {
            if (prefab == null || prefab.Stats == null) return 0f;
            foreach (var stat in prefab.Stats)
            {
                if (stat.Key == key) return stat.BaseValue;
            }
            return 0f;
        }

        /// <summary>
        /// 获取预制体的 Variable 值
        /// </summary>
        private static float GetPrefabVariableValue(Item prefab, string key)
        {
            if (prefab == null || prefab.Variables == null) return 0f;
            foreach (var variable in prefab.Variables)
            {
                if (variable.Key == key)
                {
                    try
                    {
                        return variable.GetFloat();
                    }
                    catch (Exception variableEx)
                    {
                        string prefabName = prefab != null ? prefab.DisplayName : "null";
                        ModBehaviour.DevLog($"[ReforgeData] [WARNING] 读取预制体Variable失败: {prefabName}.{key}, {variableEx.Message}");
                    }
                }
            }
            return 0f;
        }
    }
}
