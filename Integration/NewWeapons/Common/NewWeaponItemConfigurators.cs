// ============================================================================
// NewWeaponItemConfigurators.cs - 五把新武器的 ItemFactory 配置器登记
// ============================================================================
// 模块说明：
//   这五把武器的 Item prefab 住在 Assets/Items/{name}_item bundle 里，由 ItemFactory 注册；
//   而 EquipmentFactory 那条 TryConfigure 只覆盖 Assets/Equipment 里的 Item——
//   这五把在 Assets/Equipment 下只有模型，没有 Item。
//
//   不登记配置器的话，写 Stats / 标签 / Value / 可维修标签的唯一动作，就是延迟 bootstrap 里
//   一次性的 ConfigureNewWeaponsAfterLoad()，它比 prefab 注册晚若干帧。后果有两层：
//     1. 那个窗口内开店/读档，读到的是 bundle 里烤进去的旧 Value（约 8600~12600），不是设计价；
//     2. 该调用一旦抛异常或被跳过，这五把连 Stats 和可维修标签都不会有——而编译与守卫都查不出来。
//   登记之后变成「注册即配置」，与焚皇断界戟 / 霜之哀伤 / 噬魂挽歌三把老武器口径一致。
//
//   放在本模块而不是 partial ModBehaviour：ModBehaviourPartialBudgetGuard 的文件数已顶格
//   （204/204）、行数余量也很紧，宿主只留一行调用（AGENTS.md 4.15）。
//
//   幂等：各 XxxWeaponConfig.TryConfigure 可重复调用，与 ConfigureNewWeaponsAfterLoad
//   或占位符路径重复执行不会叠加效果（modifier 走 EquipmentHelper.EnsureModifierOnItem）。
// ============================================================================

using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>五把新武器的 ItemFactory 配置器登记入口</summary>
    internal static class NewWeaponItemConfigurators
    {
        /// <summary>
        /// 由 ModBehaviour.RegisterItemContentConfigurators() 调用一次。
        /// ItemFactory.LoadBundleInternal 在把 prefab 加进 ItemAssetsCollection 之前会先跑这些配置器。
        /// </summary>
        public static void RegisterAll()
        {
            try
            {
                ItemFactory.RegisterConfigurator(NewWeaponIds.ViperDaggerTypeId, ConfigureViperDagger);
                ItemFactory.RegisterConfigurator(NewWeaponIds.SummonStaffTypeId, ConfigureSummonStaff);
                ItemFactory.RegisterConfigurator(NewWeaponIds.EnergyShieldTypeId, ConfigureEnergyShield);
                ItemFactory.RegisterConfigurator(NewWeaponIds.FrostSpearTypeId, ConfigureFrostSpear);
                ItemFactory.RegisterConfigurator(NewWeaponIds.ThunderRingTypeId, ConfigureThunderRing);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeapons] 登记 ItemFactory 配置器失败: " + e.Message);
            }
        }

        private static void ConfigureViperDagger(Item itemPrefab)
        {
            ViperDaggerWeaponConfig.TryConfigure(itemPrefab, NewWeaponIds.ViperDaggerBaseName);
        }

        private static void ConfigureSummonStaff(Item itemPrefab)
        {
            SummonStaffWeaponConfig.TryConfigure(itemPrefab, NewWeaponIds.SummonStaffBaseName);
        }

        private static void ConfigureEnergyShield(Item itemPrefab)
        {
            EnergyShieldWeaponConfig.TryConfigure(itemPrefab, NewWeaponIds.EnergyShieldBaseName);
        }

        private static void ConfigureFrostSpear(Item itemPrefab)
        {
            FrostSpearWeaponConfig.TryConfigure(itemPrefab, NewWeaponIds.FrostSpearBaseName);
        }

        private static void ConfigureThunderRing(Item itemPrefab)
        {
            ThunderRingWeaponConfig.TryConfigure(itemPrefab, NewWeaponIds.ThunderRingBaseName);
        }
    }
}
