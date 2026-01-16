// ============================================================================
// AbilitySystemHelper.cs - 装备能力系统辅助类
// ============================================================================
// 模块说明：
//   为所有装备能力系统提供通用的集成逻辑
//   包括初始化、场景切换处理、清理等
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush.Common.Equipment
{
    /// <summary>
    /// 装备能力系统辅助类 - 提供通用的初始化和管理逻辑
    /// 所有装备能力系统都可以使用这个类来集成到 Mod 中
    /// </summary>
    public static class AbilitySystemHelper
    {
        /// <summary>
        /// 初始化能力系统
        /// </summary>
        /// <param name="config">能力配置</param>
        /// <param name="ensureManagerInstance">确保管理器实例存在的回调</param>
        /// <param name="ensureEffectManagerInstance">确保效果管理器实例存在的回调</param>
        /// <param name="initializeItem">初始化物品的回调（可选）</param>
        /// <param name="injectLocalization">注入本地化的回调（可选）</param>
        public static void InitializeSystem(
            EquipmentAbilityConfig config,
            Action ensureManagerInstance,
            Action ensureEffectManagerInstance,
            Action initializeItem = null,
            Action injectLocalization = null)
        {
            try
            {
                // 1. 确保管理器实例存在
                ensureManagerInstance?.Invoke();

                // 2. 确保效果管理器实例存在
                ensureEffectManagerInstance?.Invoke();

                // 3. 初始化物品（如果有）
                initializeItem?.Invoke();

                // 4. 注入本地化（如果有）
                injectLocalization?.Invoke();

                ModBehaviour.DevLog($"{config.LogPrefix} 系统初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} 系统初始化失败: {e.Message}");
            }
        }

        /// <summary>
        /// 处理场景切换
        /// </summary>
        /// <param name="config">能力配置</param>
        /// <param name="onSceneChanged">场景变化回调</param>
        /// <param name="delayedCheckEquipment">延迟检查装备的回调</param>
        /// <param name="monoBehaviour">用于启动协程的 MonoBehaviour</param>
        public static void HandleSceneChange(
            EquipmentAbilityConfig config,
            Action onSceneChanged,
            Func<IEnumerator> delayedCheckEquipment,
            MonoBehaviour monoBehaviour)
        {
            try
            {
                // 调用场景变化回调
                onSceneChanged?.Invoke();

                // 启动延迟检查装备协程
                if (delayedCheckEquipment != null && monoBehaviour != null)
                {
                    monoBehaviour.StartCoroutine(DelayedCheckEquipmentCoroutine(
                        delayedCheckEquipment,
                        config
                    ));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} 场景设置失败: {e.Message}");
            }
        }

        /// <summary>
        /// 延迟检查装备的协程
        /// </summary>
        private static IEnumerator DelayedCheckEquipmentCoroutine(
            Func<IEnumerator> checkAction,
            EquipmentAbilityConfig config)
        {
            yield return new WaitForSeconds(0.5f);

            var enumerator = checkAction?.Invoke();
            if (enumerator != null)
            {
                while (enumerator.MoveNext())
                {
                    yield return enumerator.Current;
                }
            }
        }

        /// <summary>
        /// 清理能力系统
        /// </summary>
        /// <param name="config">能力配置</param>
        /// <param name="cleanupManager">清理管理器的回调</param>
        /// <param name="destroyEffectManager">销毁效果管理器的回调</param>
        public static void CleanupSystem(
            EquipmentAbilityConfig config,
            Action cleanupManager,
            Action destroyEffectManager)
        {
            try
            {
                cleanupManager?.Invoke();
                destroyEffectManager?.Invoke();

                ModBehaviour.DevLog($"{config.LogPrefix} 系统清理完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} 系统清理失败: {e.Message}");
            }
        }
    }
}
