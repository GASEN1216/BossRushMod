// ============================================================================
// PetNestExpeditionSlots.cs - 天灾远征同时在途队数
// ============================================================================
// 2026-10-17 加上限：此前整窝 24 只能同时出发，亡命档一批的产出压过主循环，
// 而且「蛋 → 崽 → 再多派一队」会自我加速。上限是纯派生值（基础 + 图鉴里程碑），
// 不写档；旧档里超过上限的在途远征照常结算，只是回来前不能再派新的。
// 独立成文件是因为 PetNestExpeditionService.cs 贴着 1200 行预算（LargeFileBudgetGuard）。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>远征队数上限与占用。出发事务与远征页共用这一份判据。</summary>
    internal static class PetNestExpeditionSlots
    {
        /// <summary>
        /// 队数上限：基础 + 图鉴解锁血脉数每达一个里程碑 +1（2 → 3 → 4）。
        /// 图鉴不可读时退回基础队数，不因统计故障锁死远征。
        /// </summary>
        internal static int GetCap()
        {
            int cap = PetNestTuning.ExpeditionBaseSlots;
            try
            {
                int unlocked = PetNestMuseumStats.UnlockedLineageCount;
                int[] milestones = PetNestTuning.ExpeditionSlotMilestoneLineageCounts;
                for (int i = 0; i < milestones.Length; i++)
                {
                    if (unlocked >= milestones[i]) cap++;
                }
            }
            catch (Exception)
            {
                // 图鉴不可读：退回基础队数
            }
            return cap;
        }

        /// <summary>占队数：只数未结算的记录。已结算待翻牌的崽已经回巢，不占队。</summary>
        internal static int CountActive()
        {
            int count = 0;
            List<PetNestExpeditionRecord> records = PetNestExpeditionService.Records;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null && !records[i].settled) count++;
            }
            return count;
        }

        /// <summary>还有没有空队；顺带给出占用与上限，页面直接拿去显示。</summary>
        internal static bool HasFreeSlot(out int used, out int cap)
        {
            used = CountActive();
            cap = GetCap();
            return used < cap;
        }
    }
}
