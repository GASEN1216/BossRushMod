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
    /// <summary>飞行图腾 bootstrap 入口的唯一运行时 owner。</summary>
    internal sealed class FlightTotemRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour _owner;

        public override string ModuleName { get { return "FlightTotem"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            _owner = owner;
        }

        public override void OnDestroy()
        {
            _owner = null;
        }

        // ========== 初始化 ==========

        /// <summary>
        /// 初始化飞行图腾系统（在 Start_Integration 中调用）
        /// </summary>
        internal void InitializeFlightTotemSystem()
        {
            AbilitySystemHelper.InitializeSystem(
                config: FlightConfig.Instance,
                ensureManagerInstance: () => FlightAbilityManager.EnsureInstance(),
                ensureEffectManagerInstance: () => FlightTotemEffectManager.EnsureInstance(),
                initializeItem: () => _owner.InitializeFlightTotemItemFromRuntimeModule(),
                injectLocalization: () => _owner.InjectFlightTotemLocalizationFromRuntimeModule()
            );
        }

        // ========== 场景管理 ==========

        /// <summary>
        /// 在场景加载后设置飞行图腾（场景切换时调用）
        /// </summary>
        internal void SetupFlightTotemForScene(Scene scene)
        {
            if (ModBehaviour.IsGameplaySceneName(scene.name))
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
                    monoBehaviour: _owner
                );
                return;
            }

            try
            {
                if (FlightAbilityManager.Instance != null)
                {
                    FlightAbilityManager.Instance.OnSceneChanged();
                }
            }
            catch (System.Exception e)
            {
                DevLog($"{FlightConfig.Instance.LogPrefix} 场景设置失败: {e.Message}");
            }
        }

        /// <summary>
        /// 延迟检查飞行图腾装备状态
        /// </summary>
        private IEnumerator DelayedCheckFlightTotemEquipment()
        {
            yield return ModBehaviour.FlightTotemSharedWait05sForRuntime;

            if (FlightTotemEffectManager.Instance != null)
            {
                FlightTotemEffectManager.Instance.CheckCurrentEquipment();
            }
        }

        // ========== 清理 ==========

        /// <summary>
        /// 清理飞行图腾系统
        /// </summary>
        internal void CleanupFlightTotemSystem()
        {
            AbilitySystemHelper.CleanupSystem(
                config: FlightConfig.Instance,
                cleanupManager: () => FlightAbilityManager.Cleanup(),
                destroyEffectManager: () =>
                {
                    if (FlightTotemEffectManager.Instance != null)
                    {
                        UnityEngine.Object.Destroy(FlightTotemEffectManager.Instance.gameObject);
                    }
                }
            );
        }

        [System.Diagnostics.Conditional("BOSSRUSH_DEV")]
        private static void DevLog(string message)
        {
            ModBehaviour.DevLog(message);
        }
    }
}
