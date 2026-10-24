using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>宝箱怪扑跳落点与炮弹撞墙的地形查询。只在战斗中按招式调用，没有逐帧扫描。</summary>
    internal static class GreedyMimicTerrain
    {
        /// <summary>
        /// 从 from 往 target 扑跳：超出 maxLeap 先截到最远距离，再依次退到 100% / 75% / 50% 处找一块
        /// 平整、头顶有身位的实体地面。都不行就放弃这次扑跳。
        /// </summary>
        internal static bool TryLanding(Vector3 from, Vector3 target, float bodyRadius, float maxLeap, out Vector3 landing)
        {
            landing = from;
            Vector3 delta = target - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 0.5f) return false;
            Vector3 direction = delta / distance;
            float reach = Mathf.Min(distance, maxLeap);
            float[] fractions = { 1f, 0.75f, 0.5f };
            for (int i = 0; i < fractions.Length; i++)
            {
                Vector3 candidate = from + direction * (reach * fractions[i]);
                candidate.y = target.y;
                RaycastHit ground;
                if (!Physics.Raycast(candidate + Vector3.up * 3f, Vector3.down, out ground, 6f,
                        GameplayDataSettings.Layers.groundLayerMask, QueryTriggerInteraction.Ignore)
                    || ground.normal.y < 0.75f || Mathf.Abs(ground.point.y - from.y) > 3f) continue;
                Vector3 bottom = ground.point + Vector3.up * (bodyRadius + 0.2f);
                if (Physics.CheckCapsule(bottom, ground.point + Vector3.up * 2.4f, bodyRadius,
                        GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) continue;
                landing = ground.point + Vector3.up * 0.04f;
                return true;
            }
            return false;
        }

        /// <summary>炮弹本帧位移是否撞到墙。</summary>
        internal static bool SegmentBlocked(Vector3 from, Vector3 to)
        {
            return Physics.Linecast(from, to, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore);
        }
    }
}
