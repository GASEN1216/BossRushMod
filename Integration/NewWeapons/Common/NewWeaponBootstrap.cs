// ============================================================================
// NewWeaponBootstrap.cs - P0 五把新武器在宿主上的生命周期挂点
// ============================================================================
// 模块说明：
//   宿主 ModBehaviour 只保留四个挂点，实现全部在 NewWeaponRuntime（AGENTS §4.15：
//   新子系统的状态与算法放自己的类型，宿主只做生命周期分发，兼容转发尽量一行）。
//   这四个方法名被其它 partial 文件与守卫引用，**不要改名**：
//     InitializeNewWeaponSystems       <- Integration/EquipmentContentRegistry.cs
//     SetupNewWeaponsForScene          <- BossRushIntegration_StartAndScene / IntegrationDeferredBootstrap
//     ConfigureNewWeaponsAfterLoad     <- Integration/EquipmentContentRegistry.cs
//     CleanupNewWeaponSystemsOnDestroy <- Integration/EquipmentContentRegistry.cs
// ============================================================================

using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// P0 新武器扩展在宿主上的四个挂点
    /// </summary>
    public partial class ModBehaviour
    {
        /// <summary>初始化新武器系统（在 InitializeLateEquipmentAbilitySystems 中调用）</summary>
        private void InitializeNewWeaponSystems()
        {
            NewWeaponRuntime.Initialize();
        }

        /// <summary>场景加载后设置新武器系统</summary>
        private void SetupNewWeaponsForScene(Scene scene)
        {
            NewWeaponRuntime.SetupForScene(this, scene);
        }

        /// <summary>在 LoadEquipmentContent 中调用，补配置新武器</summary>
        private void ConfigureNewWeaponsAfterLoad()
        {
            NewWeaponRuntime.ConfigureAfterLoad();
        }

        /// <summary>清理新武器系统</summary>
        private void CleanupNewWeaponSystemsOnDestroy()
        {
            NewWeaponRuntime.CleanupOnDestroy();
        }
    }
}
