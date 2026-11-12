using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 自研出击地图的半掩体（`SkyIsland/AGENTS.md` §6.1 第 2 条）：离地 0.8–1.4 m 的墙层盒 / 胶囊碰撞体挂一个贴身触发区，
    /// 主角站进去时这块掩体登记进官方 `nearByHalfObsticles`，`BossRushProjectileInitPatch` 让主角的子弹越过它；
    /// 远处射来的子弹照旧被它挡住。触发与登记复用 ModeF 工事的 `ModeFHalfObstacleTrigger`（只跟踪主角，进出计数）。
    /// 只在场景装配时跑一遍，触发区挂在碰撞体自己身上、随场景销毁，不订阅任何静态事件。
    /// </summary>
    internal static class RaidHalfCover
    {
        /// <summary>半掩体的离地顶高区间；低于下限的按通则不该有实体，高于上限的是全掩体。</summary>
        internal const float MinTop = 0.8f, MaxTop = 1.4f;
        /// <summary>触发区向四周外扩的水平距离：角色半径约 0.3 m，贴着掩体蹲守时胶囊中心在 0.9 m 内。</summary>
        internal const float Reach = 0.9f;
        /// <summary>触发区高度（从碰撞体底部算），覆盖角色整个胶囊。</summary>
        private const float TriggerHeight = 2.2f;
        /// <summary>水平最长边超过这个长度的不登记：贴着一端时整段都会被子弹越过，读起来不对。</summary>
        private const float MaxSpan = 8f;
        internal const string TriggerName = "BR_HalfCoverTrigger";

        internal delegate bool GroundProbe(Vector3 position, out float y);

        /// <summary>
        /// 扫描 <paramref name="root"/> 下的墙层盒 / 胶囊碰撞体，按离地顶高登记半掩体，返回登记数。
        /// 网格碰撞体默认不处理：一块合并网格里可能有整片建筑，贴着一处就会让子弹越过整片；
        /// 只有 <paramref name="accept"/> 明确放行的单件网格（天空岛 ArenaCover）才收。
        /// </summary>
        internal static int Register(Transform root, int wallLayer, GroundProbe ground, Func<Collider, bool> accept = null)
        {
            if (root == null) return 0;
            int registered = 0;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider.isTrigger || collider.gameObject.layer != wallLayer) continue;
                // 网格碰撞体只在调用方显式放行时才收（天空岛按块单独成组的 ArenaCover），合并网格一律不收
                bool mesh = collider is MeshCollider;
                if (!(collider is BoxCollider) && !(collider is CapsuleCollider) && !mesh) continue;
                if (accept != null ? !accept(collider) : mesh) continue;
                if (collider.transform.Find(TriggerName) != null) continue;
                Bounds bounds = collider.bounds;
                if (Mathf.Max(bounds.size.x, bounds.size.z) > MaxSpan) continue;
                float baseY;
                if (ground == null || !ground(bounds.center, out baseY)) baseY = bounds.min.y;
                float top = bounds.max.y - Mathf.Max(baseY, bounds.min.y);
                if (top < MinTop || top > MaxTop) continue;
                if (Attach(collider)) registered++;
            }
            return registered;
        }

        private static bool Attach(Collider collider)
        {
            try
            {
                Transform owner = collider.transform;
                Vector3 scale = owner.lossyScale;
                float sx = Mathf.Max(0.001f, Mathf.Abs(scale.x)), sy = Mathf.Max(0.001f, Mathf.Abs(scale.y)), sz = Mathf.Max(0.001f, Mathf.Abs(scale.z));
                Vector3 center, size;
                BoxCollider box = collider as BoxCollider;
                MeshCollider meshCollider = collider as MeshCollider;
                if (box != null) { center = box.center; size = box.size; }
                else if (meshCollider != null)
                {
                    if (meshCollider.sharedMesh == null) return false;
                    Bounds local = meshCollider.sharedMesh.bounds; center = local.center; size = local.size;
                }
                else
                {
                    CapsuleCollider capsule = (CapsuleCollider)collider;
                    center = capsule.center;
                    float d = capsule.radius * 2f;
                    size = new Vector3(d, d, d);
                    size[capsule.direction] = Mathf.Max(d, capsule.height);
                }
                float bottom = center.y - size.y * 0.5f;
                var trigger = new GameObject(TriggerName);
                trigger.transform.SetParent(owner, false);
                BoxCollider area = trigger.AddComponent<BoxCollider>();
                area.isTrigger = true;
                area.size = new Vector3(size.x + 2f * Reach / sx, TriggerHeight / sy, size.z + 2f * Reach / sz);
                area.center = new Vector3(center.x, bottom + area.size.y * 0.5f, center.z);
                trigger.AddComponent<ModeFHalfObstacleTrigger>().SetRegisteredParts(collider.gameObject);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RaidHalfCover] 登记半掩体失败 " + collider.name + "：" + e.Message);
                return false;
            }
        }
    }
}
