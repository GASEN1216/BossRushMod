// ============================================================================
// EnergyShieldWeaponConfig.cs - 能量盾装备工厂配置器
// ============================================================================
// 模块说明：
//   在 EquipmentFactory 加载 AssetBundle 后，自动为能量盾 Prefab 配置：
//   - 图腾标签和属性
//   - BodyArmor +3 modifier
//   - 本地化注入
// ============================================================================

using System;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>
    /// 能量盾装备工厂配置器
    /// </summary>
    public static class EnergyShieldWeaponConfig
    {
        /// <summary>
        /// 尝试配置能量盾
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;
            // 接受两种 baseName：
            //   - "EnergyShield"（占位符 / ConfigureNewWeaponsAfterLoad 直接传入）
            //   - "EnergyShield_Totem"（EquipmentFactory.LoadBundleInternal 从 prefab 名 EnergyShield_Totem_Item 提取）
            if (!baseName.Equals(NewWeaponIds.EnergyShieldBaseName, StringComparison.OrdinalIgnoreCase) &&
                !baseName.Equals(NewWeaponIds.EnergyShieldBaseName + "_Totem", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                ModBehaviour.DevLog(EnergyShieldConfig.LogPrefix + " 开始配置能量盾...");

                // 1. 配置标签（作为图腾类装备）
                ConfigureTags(item);

                // 2. 添加护甲 modifier
                TryBindLoadedModel(item);
                ConfigureModifiers(item);

                // 3. 注入本地化
                InjectLocalization(item);

                ModBehaviour.DevLog(EnergyShieldConfig.LogPrefix + " 配置完成 (TypeID=" + item.TypeID + ")");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(EnergyShieldConfig.LogPrefix + " 配置失败: " + e.Message);
                return false;
            }
        }

        private static void ConfigureTags(Item item)
        {
            EquipmentHelper.AddTagToItem(item, "Totem");
            EquipmentHelper.AddTagToItem(item, "DontDropOnDeadInSlot");
            EquipmentHelper.AddTagToItem(item, "Special");
        }

        private static void TryBindLoadedModel(Item item)
        {
            try
            {
                EquipmentFactory.TryBindLoadedEquipmentModel(item, NewWeaponIds.EnergyShieldModelBaseName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(EnergyShieldConfig.LogPrefix + " 绑定模型失败: " + e.Message);
            }
        }

        private static void ConfigureModifiers(Item item)
        {
            try
            {
                EquipmentHelper.AddModifierToItem(item, "BodyArmor", ModifierType.Add, EnergyShieldConfig.BodyArmorBonus, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(EnergyShieldConfig.LogPrefix + " 添加 modifier 失败: " + e.Message);
            }
        }

        private static void InjectLocalization(Item item)
        {
            try
            {
                string displayName = L10n.T(EnergyShieldConfig.DisplayNameCN, EnergyShieldConfig.DisplayNameEN);
                string description = L10n.T(EnergyShieldConfig.DescriptionCN, EnergyShieldConfig.DescriptionEN);

                string itemKey = "Item_" + item.TypeID;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(EnergyShieldConfig.LogPrefix + " 本地化注入失败: " + e.Message);
            }
        }

        public static void ResetStaticCaches()
        {
            // 当前无需清理的静态缓存
        }
    }
}
