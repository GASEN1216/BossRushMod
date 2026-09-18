// ============================================================================
// ThunderRingWeaponConfig.cs - 雷电戒指装备工厂配置器
// ============================================================================
// 模块说明：
//   声明雷电戒指与共享图腾配置流程的差异项（模型、文案），
//   流程本身在 NewWeaponConfiguratorCore。蓄雷 / 释放逻辑在 ThunderRingRuntime。
//   本件不挂常驻 modifier：它的全部价值在「挨打攒电、下一击放出去」这条主动循环上。
// ============================================================================

using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 雷电戒指装备工厂配置器
    /// </summary>
    public static class ThunderRingWeaponConfig
    {
        private static readonly NewWeaponTotemSpec Spec = new NewWeaponTotemSpec
        {
            TypeId = NewWeaponIds.ThunderRingTypeId,
            BaseName = NewWeaponIds.ThunderRingBaseName,
            ModelBaseName = NewWeaponIds.ThunderRingModelBaseName,
            LogPrefix = ThunderRingConfig.LogPrefix,
            DisplayLabelCN = "雷电戒指",

            DisplayNameCN = ThunderRingConfig.DisplayNameCN,
            DisplayNameEN = ThunderRingConfig.DisplayNameEN,
            DescriptionCN = ThunderRingConfig.DescriptionCN,
            DescriptionEN = ThunderRingConfig.DescriptionEN
        };

        /// <summary>
        /// 尝试配置雷电戒指
        /// </summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            return NewWeaponConfiguratorCore.ConfigureTotem(item, baseName, Spec);
        }

        public static void ResetStaticCaches()
        {
            // 当前无需清理的静态缓存（Spec 是只读数据）
        }
    }
}
