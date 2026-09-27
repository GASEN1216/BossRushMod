using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 任务提交物品的纯判据：给定「每种物品身上有几个」，算出每条提交还差多少、交付时每种实际收走几个。
    /// 无 Unity / Duckov 依赖，tests/fixtures/JeffQuestFlow 直接执行。
    /// </summary>
    internal static class OfficialQuestItemRules
    {
        /// <summary>一条提交此刻身上凑得出几个（多种可选物品相加，同一种只算一次）。</summary>
        internal static int Held(OfficialQuestSubmission submission, Func<int, int> held)
        {
            if (submission == null || submission.TypeIds == null || held == null) return 0;
            int total = 0;
            for (int i = 0; i < submission.TypeIds.Length; i++)
            {
                int typeId = submission.TypeIds[i];
                if (typeId <= 0 || IndexOf(submission.TypeIds, typeId) != i) continue;
                total += Math.Max(0, held(typeId));
            }
            return total;
        }

        /// <summary>
        /// 排出整份收取计划：按提交表顺序、每条按可选物品顺序取；几条提交共用同一种物品时按累计扣。
        /// 任何一条凑不够返回 false，<paramref name="missing"/> 指出第一条差的，<paramref name="plan"/> 不可使用。
        /// </summary>
        internal static bool TryPlan(OfficialQuestSubmission[] submissions, Func<int, int> held,
            List<OfficialQuestItemStack> plan, out OfficialQuestSubmission missing)
        {
            missing = null;
            if (plan == null) return false;
            plan.Clear();
            if (submissions == null || submissions.Length == 0) return true;
            if (held == null) return false;
            var used = new Dictionary<int, int>();
            for (int s = 0; s < submissions.Length; s++)
            {
                OfficialQuestSubmission submission = submissions[s];
                if (submission == null || submission.Count <= 0 || submission.TypeIds == null) { missing = submission; return false; }
                int left = submission.Count;
                for (int i = 0; i < submission.TypeIds.Length && left > 0; i++)
                {
                    int typeId = submission.TypeIds[i];
                    if (typeId <= 0 || IndexOf(submission.TypeIds, typeId) != i) continue;
                    int already;
                    used.TryGetValue(typeId, out already);
                    int take = Math.Min(left, Math.Max(0, held(typeId)) - already);
                    if (take <= 0) continue;
                    used[typeId] = already + take;
                    left -= take;
                }
                if (left > 0) { missing = submission; plan.Clear(); return false; }
            }
            foreach (KeyValuePair<int, int> entry in used) plan.Add(new OfficialQuestItemStack(entry.Key, entry.Value));
            plan.Sort((a, b) => a.TypeId.CompareTo(b.TypeId));
            return true;
        }

        private static int IndexOf(int[] values, int value)
        {
            for (int i = 0; i < values.Length; i++) if (values[i] == value) return i;
            return -1;
        }
    }
}
