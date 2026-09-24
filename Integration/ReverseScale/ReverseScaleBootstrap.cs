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
    /// <summary>逆鳞 bootstrap 入口的唯一运行时 owner。</summary>
    internal sealed class ReverseScaleRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour _owner;

        public override string ModuleName { get { return "ReverseScale"; } }

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
        /// 初始化逆鳞图腾系统（在 Start_Integration 中调用）
        /// </summary>
        internal void InitializeReverseScaleSystem()
        {
            AbilitySystemHelper.InitializeSystem(
                config: ReverseScaleConfig.Instance,
                ensureManagerInstance: () => ReverseScaleAbilityManager.EnsureInstance(),
                ensureEffectManagerInstance: () => ReverseScaleEffectManager.EnsureInstance(),
                initializeItem: () => _owner.InitializeReverseScaleItemFromRuntimeModule(),
                injectLocalization: () => _owner.InjectReverseScaleLocalizationFromRuntimeModule()
            );
        }

        // ========== 场景管理 ==========

        /// <summary>
        /// 在场景加载后设置逆鳞图腾（场景切换时调用）
        /// </summary>
        internal void SetupReverseScaleForScene(Scene scene)
        {
            if (ModBehaviour.IsGameplaySceneName(scene.name))
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
                    monoBehaviour: _owner
                );
                return;
            }

            try
            {
                if (ReverseScaleAbilityManager.Instance != null)
                {
                    ReverseScaleAbilityManager.Instance.OnSceneChanged();
                }
            }
            catch (System.Exception e)
            {
                DevLog($"{ReverseScaleConfig.Instance.LogPrefix} 场景设置失败: {e.Message}");
            }
        }

        /// <summary>
        /// 延迟检查逆鳞图腾装备状态
        /// </summary>
        private IEnumerator DelayedCheckReverseScaleEquipment()
        {
            yield return ModBehaviour.ReverseScaleSharedWait05sForRuntime;

            if (ReverseScaleEffectManager.Instance != null)
            {
                ReverseScaleEffectManager.Instance.CheckCurrentEquipment();
            }
        }

        // ========== 清理 ==========

        /// <summary>
        /// 清理逆鳞图腾系统
        /// </summary>
        internal void CleanupReverseScaleSystem()
        {
            AbilitySystemHelper.CleanupSystem(
                config: ReverseScaleConfig.Instance,
                cleanupManager: () => ReverseScaleAbilityManager.Cleanup(),
                destroyEffectManager: () =>
                {
                    if (ReverseScaleEffectManager.Instance != null)
                    {
                        UnityEngine.Object.Destroy(ReverseScaleEffectManager.Instance.gameObject);
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
