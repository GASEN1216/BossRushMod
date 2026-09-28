using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 天空岛的密集角色、云蚋和头目道具会超过官方爆炸的 8 个接收体槽。
    /// 只借换本次调用的工作缓冲，官方阵营、遮挡、Health 去重、冲刺豁免、伤害与特效照常执行。
    /// Finalizer 在成功、异常和嵌套调用后都恢复原引用；不把扩容带到其他地图。
    /// </summary>
    [HarmonyPatch(typeof(ExplosionManager), "CreateExplosion")]
    internal static class SkyIslandExplosionBufferPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(ExplosionManager __instance, Vector3 center, float radius,
            ref Collider[] ___colliders, ref List<Health> ___damagedHealth, ref LayerMask ___damageReceiverLayers,
            ref SkyIslandExplosionBuffers.Frame __state)
        {
            __state = null;
            if (!SkyIslandExplosionObstaclePatch.IsArmedFor(SceneManager.GetActiveScene())) return;
            try
            {
                SkyIslandExplosionBuffers owner = __instance.GetComponent<SkyIslandExplosionBuffers>();
                if (owner == null) owner = __instance.gameObject.AddComponent<SkyIslandExplosionBuffers>();
                __state = owner.Borrow(___colliders, ___damagedHealth, ___damageReceiverLayers);
                // 官方只在 damagedHealth == null 时初始化层掩码；首用也遵守同一份来源。
                LayerMask mask = ___damagedHealth == null
                    ? Duckov.Utilities.GameplayDataSettings.Layers.damageReceiverLayerMask : ___damageReceiverLayers;
                __state.Prepare(center, radius, mask);
                ___colliders = __state.Colliders;
                ___damagedHealth = __state.Damaged;
                ___damageReceiverLayers = mask;
            }
            catch (Exception error)
            {
                // 换入发生在 Prepare 成功后，失败时字段仍是完整的官方状态。
                // 已借出的 frame 留给 Finalizer 归还，连官方回退自身抛异常也能恢复原引用。
                Debug.LogWarning("[SkyIsland] 爆炸目标缓冲准备失败，沿用官方处理：" + error.Message);
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, SkyIslandExplosionBuffers.Frame __state,
            ref Collider[] ___colliders, ref List<Health> ___damagedHealth, ref LayerMask ___damageReceiverLayers)
        {
            if (__state != null) __state.Restore(ref ___colliders, ref ___damagedHealth, ref ___damageReceiverLayers);
            return __exception;
        }
    }

    /// <summary>
    /// 只挂在实际发生天空岛爆炸的 manager 上，没有 Update 和静态容器。
    /// 按同步调用深度复用缓冲；容量和深度稳定后不再分配，manager 随场景销毁时一起回收。
    /// </summary>
    internal sealed class SkyIslandExplosionBuffers : MonoBehaviour
    {
        private readonly List<Frame> frames = new List<Frame>();
        private int depth;

        internal Frame Borrow(Collider[] colliders, List<Health> damaged, LayerMask mask)
        {
            if (depth == frames.Count) frames.Add(new Frame(this));
            Frame frame = frames[depth++];
            frame.Begin(colliders, damaged, mask);
            return frame;
        }

        internal sealed class Frame
        {
            private readonly SkyIslandExplosionBuffers owner;
            internal Collider[] Colliders = new Collider[16];
            internal readonly List<Health> Damaged = new List<Health>();
            private Collider[] previousColliders;
            private List<Health> previousDamaged;
            private LayerMask previousMask;
            private bool borrowed;

            internal Frame(SkyIslandExplosionBuffers owner) { this.owner = owner; }

            internal void Begin(Collider[] colliders, List<Health> damaged, LayerMask mask)
            {
                previousColliders = colliders;
                previousDamaged = damaged;
                previousMask = mask;
                borrowed = true;
            }

            internal void Prepare(Vector3 center, float radius, LayerMask mask)
            {
                // NonAlloc 返回数组长度时，可能还有目标没放进来；继续扩到查询不饱和，不按人数硬截断。
                while (Physics.OverlapSphereNonAlloc(center, radius, Colliders, mask) == Colliders.Length)
                    Colliders = new Collider[checked(Colliders.Length * 2)];
            }

            internal void Restore(ref Collider[] colliders, ref List<Health> damaged, ref LayerMask mask)
            {
                if (!borrowed) return;
                colliders = previousColliders;
                damaged = previousDamaged;
                mask = previousMask;
                previousColliders = null;
                previousDamaged = null;
                borrowed = false;
                // 不能跨调用留着角色引用；内层有自己的 frame，不会清掉外层正在遍历的对象。
                Array.Clear(Colliders, 0, Colliders.Length);
                Damaged.Clear();
                owner.depth--;
            }
        }
    }
}
