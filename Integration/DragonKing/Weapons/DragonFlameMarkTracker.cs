// ============================================================================
// DragonFlameMarkTracker.cs - 龙焰印记追踪系统
// ============================================================================
// 模块说明：
//   静态管理器，追踪敌人身上的龙焰印记层数和过期时间
//   用于焚皇断界戟的普攻叠加 + 右键爆燃机制
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 龙焰印记数据
    /// </summary>
    public struct DragonFlameMarkData
    {
        /// <summary>
        /// 当前印记层数
        /// </summary>
        public int stacks;

        /// <summary>
        /// 印记过期的 Time.time
        /// </summary>
        public float expireTime;
    }

    /// <summary>
    /// 龙焰印记追踪器 - 管理所有敌人身上的印记
    /// </summary>
    public static class DragonFlameMarkTracker
    {
        // 使用 DamageReceiver 的 InstanceID 作为 key，避免引用泄漏
        private static readonly Dictionary<int, DragonFlameMarkData> marks = new Dictionary<int, DragonFlameMarkData>();

        // DamageReceiver 引用缓存（用于快速查找，弱引用语义通过定期清理实现）
        private static readonly Dictionary<int, DamageReceiver> receiverCache = new Dictionary<int, DamageReceiver>();

        // 清理计时
        private static float lastCleanupTime = 0f;
        private const float CLEANUP_INTERVAL = 2f;

        // 清理用临时列表（复用避免 GC）
        private static readonly List<int> keysToRemoveCache = new List<int>();

        /// <summary>
        /// 给目标叠加一层龙焰印记
        /// </summary>
        public static void AddMark(DamageReceiver target)
        {
            if (target == null) return;

            int id = target.GetInstanceID();
            DragonFlameMarkData data;

            if (marks.TryGetValue(id, out data))
            {
                // 已有印记，叠加（不超过上限）
                data.stacks = Mathf.Min(data.stacks + 1, FenHuangHalberdConfig.MaxMarkStacks);
                data.expireTime = Time.time + FenHuangHalberdConfig.MarkDuration;
                marks[id] = data;
            }
            else
            {
                // 新印记
                data = new DragonFlameMarkData
                {
                    stacks = 1,
                    expireTime = Time.time + FenHuangHalberdConfig.MarkDuration
                };
                marks[id] = data;
                receiverCache[id] = target;
            }
        }

        /// <summary>
        /// 获取目标当前印记层数
        /// </summary>
        public static int GetMarkCount(DamageReceiver target)
        {
            if (target == null) return 0;

            int id = target.GetInstanceID();
            DragonFlameMarkData data;
            if (marks.TryGetValue(id, out data))
            {
                if (Time.time < data.expireTime)
                {
                    return data.stacks;
                }
                // 已过期，清除
                marks.Remove(id);
                receiverCache.Remove(id);
            }
            return 0;
        }

        /// <summary>
        /// 消耗目标所有印记（爆燃时调用），返回消耗的层数
        /// </summary>
        public static int ConsumeMark(DamageReceiver target)
        {
            if (target == null) return 0;

            int id = target.GetInstanceID();
            DragonFlameMarkData data;
            if (marks.TryGetValue(id, out data))
            {
                int stackCount = data.stacks;
                marks.Remove(id);
                receiverCache.Remove(id);
                return stackCount;
            }
            return 0;
        }

        /// <summary>
        /// 定期清理过期印记（应在 Update 中调用）
        /// </summary>
        public static void CleanupExpired()
        {
            if (Time.time - lastCleanupTime < CLEANUP_INTERVAL) return;
            lastCleanupTime = Time.time;

            float now = Time.time;
            keysToRemoveCache.Clear();

            foreach (var kvp in marks)
            {
                // 检查是否过期或目标已被销毁
                if (now >= kvp.Value.expireTime)
                {
                    keysToRemoveCache.Add(kvp.Key);
                    continue;
                }

                // 检查 DamageReceiver 是否仍然有效
                DamageReceiver receiver;
                if (receiverCache.TryGetValue(kvp.Key, out receiver))
                {
                    if (receiver == null)
                    {
                        keysToRemoveCache.Add(kvp.Key);
                    }
                }
            }

            for (int i = 0; i < keysToRemoveCache.Count; i++)
            {
                marks.Remove(keysToRemoveCache[i]);
                receiverCache.Remove(keysToRemoveCache[i]);
            }
        }

        /// <summary>
        /// 清空所有印记（场景切换时调用）
        /// </summary>
        public static void ClearAll()
        {
            marks.Clear();
            receiverCache.Clear();
        }
    }
}
