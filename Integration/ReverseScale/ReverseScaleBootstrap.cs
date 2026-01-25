// ============================================================================
// ReverseScaleBootstrap.cs - 逆鳞图腾系统启动集成
// ============================================================================
// 模块说明：
//   将逆鳞图腾系统集成到 ModBehaviour 中
//   使用 AbilitySystemHelper 提供通用逻辑
// ============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 逆鳞图腾系统启动模块 - 使用 partial class 扩展 ModBehaviour
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 初始化 ==========

        /// <summary>
        /// 初始化逆鳞图腾系统（在 Start_Integration 中调用）
        /// </summary>
        private void InitializeReverseScaleSystem()
        {
            AbilitySystemHelper.InitializeSystem(
                config: ReverseScaleConfig.Instance,
                ensureManagerInstance: () => ReverseScaleAbilityManager.EnsureInstance(),
                ensureEffectManagerInstance: () => ReverseScaleEffectManager.EnsureInstance(),
                initializeItem: InitializeReverseScaleItem,
                injectLocalization: InjectReverseScaleLocalization
            );
        }

        // ========== 场景管理 ==========

        /// <summary>
        /// 在场景加载后设置逆鳞图腾（场景切换时调用）
        /// </summary>
        private void SetupReverseScaleForScene(Scene scene)
        {
            AbilitySystemHelper.HandleSceneChange(
                config: ReverseScaleConfig.Instance,
                onSceneChanged: () =>
                {
                    if (ReverseScaleAbilityManager.Instance != null)
                    {
                        ReverseScaleAbilityManager.Instance.OnSceneChanged();
                    }
                },
                delayedCheckEquipment: DelayedCheckReverseScaleEquipment,
                monoBehaviour: this
            );
        }

        /// <summary>
        /// 延迟检查逆鳞图腾装备状态
        /// </summary>
        private IEnumerator DelayedCheckReverseScaleEquipment()
        {
            yield return new WaitForSeconds(0.5f);

            if (ReverseScaleEffectManager.Instance != null)
            {
                ReverseScaleEffectManager.Instance.CheckCurrentEquipment();
            }
        }

        // ========== 清理 ==========

        /// <summary>
        /// 清理逆鳞图腾系统
        /// </summary>
        private void CleanupReverseScaleSystem()
        {
            AbilitySystemHelper.CleanupSystem(
                config: ReverseScaleConfig.Instance,
                cleanupManager: () => ReverseScaleAbilityManager.Cleanup(),
                destroyEffectManager: () =>
                {
                    if (ReverseScaleEffectManager.Instance != null)
                    {
                        Destroy(ReverseScaleEffectManager.Instance.gameObject);
                    }
                }
            );
        }
    }
}
