// ============================================================================
// DragonFlameMarkTracker.cs - 龙焰印记追踪系统
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public struct DragonFlameMarkData
    {
        public int stacks;
        public float expireTime;
    }

    public static class DragonFlameMarkTracker
    {
        private static readonly Dictionary<int, DragonFlameMarkData> marks = new Dictionary<int, DragonFlameMarkData>();
        private static readonly Dictionary<int, DamageReceiver> receiverCache = new Dictionary<int, DamageReceiver>();
        private static readonly List<int> keysToRemoveCache = new List<int>();

        private static float lastCleanupTime = 0f;
        private const float CLEANUP_INTERVAL = 2f;

        public static void AddMark(DamageReceiver target)
        {
            AddMark(target, 1, FenHuangHalberdConfig.MaxMarkStacks, FenHuangHalberdConfig.MarkDuration);
        }

        public static void AddMark(DamageReceiver target, int addCount, int maxStacks, float duration)
        {
            if (target == null || addCount <= 0)
            {
                return;
            }

            int id = target.GetInstanceID();
            DragonFlameMarkData data;
            int requestedMax = Mathf.Max(1, maxStacks);
            float expireTime = Time.time + Mathf.Max(0.1f, duration);

            if (marks.TryGetValue(id, out data))
            {
                int effectiveMax = Mathf.Max(data.stacks, requestedMax);
                data.stacks = Mathf.Min(data.stacks + addCount, effectiveMax);
                data.expireTime = expireTime;
                marks[id] = data;
                return;
            }

            data = new DragonFlameMarkData
            {
                stacks = Mathf.Min(addCount, requestedMax),
                expireTime = expireTime
            };

            marks[id] = data;
            receiverCache[id] = target;
        }

        public static int GetMarkCount(DamageReceiver target)
        {
            if (target == null)
            {
                return 0;
            }

            int id = target.GetInstanceID();
            DragonFlameMarkData data;
            if (marks.TryGetValue(id, out data))
            {
                if (Time.time < data.expireTime)
                {
                    return data.stacks;
                }

                marks.Remove(id);
                receiverCache.Remove(id);
            }

            return 0;
        }

        public static int ConsumeMark(DamageReceiver target)
        {
            if (target == null)
            {
                return 0;
            }

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

        public static void CleanupExpired()
        {
            if (Time.time - lastCleanupTime < CLEANUP_INTERVAL)
            {
                return;
            }

            lastCleanupTime = Time.time;
            float now = Time.time;
            keysToRemoveCache.Clear();

            foreach (var kvp in marks)
            {
                if (now >= kvp.Value.expireTime)
                {
                    keysToRemoveCache.Add(kvp.Key);
                    continue;
                }

                DamageReceiver receiver;
                if (receiverCache.TryGetValue(kvp.Key, out receiver) && receiver == null)
                {
                    keysToRemoveCache.Add(kvp.Key);
                }
            }

            for (int i = 0; i < keysToRemoveCache.Count; i++)
            {
                marks.Remove(keysToRemoveCache[i]);
                receiverCache.Remove(keysToRemoveCache[i]);
            }
        }

        public static void ClearAll()
        {
            marks.Clear();
            receiverCache.Clear();
        }
    }
}
