// ============================================================================
// MagicBlendInitializationOrderPatch.cs - 动态角色首个动画状态早于 Start 的兼容修复
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KINEMATION.MagicBlend.Runtime;
using UnityEngine;

namespace BossRush.Patches.Compatibility
{
    /// <summary>
    /// 官方动态生成角色可能在 MagicBlending.Start 初始化 Playable 之前收到首个
    /// MagicBlendState.OnStateEnter。原实现会对无效 Playable 调 SetJobData 并抛异常。
    /// 这里只推迟该次状态回调；正常已初始化角色完全走官方原方法。
    /// </summary>
    [HarmonyPatch(typeof(MagicBlendState), "OnStateEnter")]
    internal static class MagicBlendInitializationOrderPatch
    {
        private const int MaxDeferredFrames = 10;
        private static readonly FieldInfo BlendingInitializedField =
            AccessTools.Field(typeof(MagicBlending), "_isInitialized");
        private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 已确认「不会再触发本竞态」的 Animator instanceID（已初始化，或压根没挂
        /// MagicBlending）。纯热路径短路表，不含 Unity 对象引用因此不钉住内存，
        /// 但跨场景会累积失效 ID，由 <see cref="ResetStaticCaches"/> 清。
        /// </summary>
        private static readonly HashSet<int> Settled = new HashSet<int>();

        [HarmonyPrefix]
        private static bool Prefix(
            MagicBlendState __instance,
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex)
        {
            try
            {
                if (__instance == null || animator == null || BlendingInitializedField == null)
                {
                    return true;
                }

                // 4.12 热路径门控：OnStateEnter 每次动画状态切换都会走，全场角色共用。
                // 竞态只发生在「动态生成角色的首个状态回调早于 MagicBlending.Start」这一
                // 窄窗口内，而 Animator 一旦初始化过就永不回退，因此用 instanceID 白名单
                // 把已初始化的 animator 一次性记住，之后连 GetComponent 都不再付。
                int animatorId = animator.GetInstanceID();
                if (Settled.Contains(animatorId))
                {
                    return true;
                }

                MagicBlending blending = animator.GetComponent<MagicBlending>();
                if (blending == null || IsInitialized(blending))
                {
                    // 没挂 MagicBlending 的 animator 也记进白名单：它永远不会进入本竞态
                    Settled.Add(animatorId);
                    return true;
                }

                string key = BuildKey(__instance, animator, layerIndex, stateInfo.fullPathHash);
                if (Pending.Add(key))
                {
                    ModBehaviour host = ModBehaviour.Instance;
                    if (host != null)
                    {
                        host.StartCoroutine(ReplayWhenInitialized(
                            key, __instance, animator, blending, stateInfo, layerIndex));
                    }
                    else
                    {
                        Pending.Remove(key);
                    }
                }

                // 不能放行：此刻官方方法必然命中未创建的 AnimationScriptPlayable。
                return false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[MagicBlendCompat] 前置检查失败，回落官方行为: " + e.Message);
                return true;
            }
        }

        private static IEnumerator ReplayWhenInitialized(
            string key,
            MagicBlendState state,
            Animator animator,
            MagicBlending blending,
            AnimatorStateInfo enteredState,
            int layerIndex)
        {
            try
            {
                for (int frame = 0; frame < MaxDeferredFrames; frame++)
                {
                    yield return null;
                    if (state == null || animator == null || blending == null) yield break;
                    if (!IsInitialized(blending)) continue;

                    // 初始化完成：登记白名单，此后该 animator 走原生路径零额外开销
                    try { Settled.Add(animator.GetInstanceID()); }
                    catch (Exception)
                    {
                        // 登记失败只是少一次短路，行为不变
                    }

                    AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layerIndex);
                    if (current.fullPathHash == enteredState.fullPathHash)
                    {
                        // 再次经过 Harmony Prefix；此时初始化已完成，会放行官方实现。
                        state.OnStateEnter(animator, current, layerIndex);
                    }
                    yield break;
                }

                ModBehaviour.DevLog("[MagicBlendCompat] 等待初始化超时，已安全跳过过期状态回调: "
                    + (animator != null ? animator.gameObject.name : "destroyed"));
            }
            finally
            {
                Pending.Remove(key);
            }
        }

        private static bool IsInitialized(MagicBlending blending)
        {
            try
            {
                object value = BlendingInitializedField.GetValue(blending);
                return value is bool && (bool)value;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 复位两张静态表（切场景 / 宿主销毁）。协程被场景卸载打断时 Pending 里会残留
        /// key，Settled 也会攒下已失效的 instanceID —— 都不钉住 Unity 对象，
        /// 但按仓库约定必须提供成对清理入口。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            try
            {
                Pending.Clear();
                Settled.Clear();
            }
            catch (Exception)
            {
                // 清表失败不影响后续：最坏是多付一次 GetComponent
            }
        }

        private static string BuildKey(
            MagicBlendState state,
            Animator animator,
            int layerIndex,
            int stateHash)
        {
            return animator.GetInstanceID() + ":" + state.GetInstanceID() + ":"
                + layerIndex + ":" + stateHash;
        }
    }
}
