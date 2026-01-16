// ============================================================================
// FlightTotemBootstrap.cs - 飞行图腾系统启动集成
// ============================================================================
// 模块说明：
//   将飞行图腾系统集成到 ModBehaviour 中
//   使用 AbilitySystemHelper 提供通用逻辑
// ============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 飞行图腾系统启动模块 - 使用 partial class 扩展 ModBehaviour
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 初始化 ==========

        /// <summary>
        /// 初始化飞行图腾系统（在 Start_Integration 中调用）
        /// </summary>
        private void InitializeFlightTotemSystem()
        {
            AbilitySystemHelper.InitializeSystem(
                config: FlightConfig.Instance,
                ensureManagerInstance: () => FlightAbilityManager.EnsureInstance(),
                ensureEffectManagerInstance: () => FlightTotemEffectManager.EnsureInstance(),
                initializeItem: InitializeFlightTotemItem,
                injectLocalization: InjectFlightTotemLocalization
            );
        }

        // ========== 场景管理 ==========

        /// <summary>
        /// 在场景加载后设置飞行图腾（场景切换时调用）
        /// </summary>
        private void SetupFlightTotemForScene(Scene scene)
        {
            AbilitySystemHelper.HandleSceneChange(
                config: FlightConfig.Instance,
                onSceneChanged: () =>
                {
                    if (FlightAbilityManager.Instance != null)
                    {
                        FlightAbilityManager.Instance.OnSceneChanged();
                    }
                },
                delayedCheckEquipment: DelayedCheckFlightTotemEquipment,
                monoBehaviour: this
            );
        }

        /// <summary>
        /// 延迟检查飞行图腾装备状态
        /// </summary>
        private IEnumerator DelayedCheckFlightTotemEquipment()
        {
            yield return new WaitForSeconds(0.5f);

            if (FlightTotemEffectManager.Instance != null)
            {
                FlightTotemEffectManager.Instance.CheckCurrentEquipment();
            }
        }

        // ========== 清理 ==========

        /// <summary>
        /// 清理飞行图腾系统
        /// </summary>
        private void CleanupFlightTotemSystem()
        {
            AbilitySystemHelper.CleanupSystem(
                config: FlightConfig.Instance,
                cleanupManager: () => FlightAbilityManager.Cleanup(),
                destroyEffectManager: () =>
                {
                    if (FlightTotemEffectManager.Instance != null)
                    {
                        Destroy(FlightTotemEffectManager.Instance.gameObject);
                    }
                }
            );
        }
    }
}
