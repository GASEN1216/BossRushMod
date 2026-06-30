// ============================================================================
// ThunderRingWeaponConfig.cs - 雷电戒指装备工厂配置器
// ============================================================================
// 模块说明：
//   在 EquipmentFactory 加载 AssetBundle 后，自动为雷电戒指 Prefab 配置：
//   - 饰品标签
//   - 本地化注入
// ============================================================================

using System;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>
    /// 雷电戒指装备工厂配置器
    /// </summary>
    public static class ThunderRingWeaponConfig
    {
        /// <summary>
        /// 尝试配置雷电戒指
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;
            // 接受两种 baseName：
            //   - "ThunderRing"（占位符 / ConfigureNewWeaponsAfterLoad 直接传入）
            //   - "ThunderRing_Totem"（EquipmentFactory.LoadBundleInternal 从 prefab 名 ThunderRing_Totem_Item 提取）
            if (!baseName.Equals(NewWeaponIds.ThunderRingBaseName, StringComparison.OrdinalIgnoreCase) &&
                !baseName.Equals(NewWeaponIds.ThunderRingBaseName + "_Totem", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                ModBehaviour.DevLog(ThunderRingConfig.LogPrefix + " 开始配置雷电戒指...");

                // 1. 配置标签（作为图腾类装备）
                ConfigureTags(item);

                // 2. 注入本地化
                InjectLocalization(item);

                ModBehaviour.DevLog(ThunderRingConfig.LogPrefix + " 配置完成 (TypeID=" + item.TypeID + ")");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ThunderRingConfig.LogPrefix + " 配置失败: " + e.Message);
                return false;
            }
        }

        private static void ConfigureTags(Item item)
        {
            EquipmentHelper.AddTagToItem(item, "Totem");
            EquipmentHelper.AddTagToItem(item, "DontDropOnDeadInSlot");
            EquipmentHelper.AddTagToItem(item, "Special");
        }

        private static void InjectLocalization(Item item)
        {
            try
            {
                string displayName = L10n.T(ThunderRingConfig.DisplayNameCN, ThunderRingConfig.DisplayNameEN);
                string description = L10n.T(ThunderRingConfig.DescriptionCN, ThunderRingConfig.DescriptionEN);

                string itemKey = "Item_" + item.TypeID;
                LocalizationHelper.InjectLocalization(itemKey, displayName);
                LocalizationHelper.InjectLocalization(itemKey + "_Desc", description);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ThunderRingConfig.LogPrefix + " 本地化注入失败: " + e.Message);
            }
        }

        public static void ResetStaticCaches()
        {
            // 当前无需清理的静态缓存
        }
    }
}
