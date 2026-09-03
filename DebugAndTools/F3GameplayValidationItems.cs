using System;
using System.Collections;
using System.Diagnostics;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        // 每帧最多一个临时实例；不穿戴、不插入背包，不触发装备预热。
        private IEnumerator RunPublishedItemCases()
        {
            int[] ids = BossRushDynamicItemRegistry.GetPublishedTypeIds();
            int passed = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                string caseId = "ITEM_FACTORY_" + ids[i];
                if (ShouldAbort()) { Record(caseId, "SKIP", 0L, string.Empty, DescribeAbortReason()); continue; }
                Stopwatch sw = Stopwatch.StartNew();
                Item probe = null;
                string reason = null;
                string metrics = "type_id=" + ids[i];
                try
                {
                    bool registered = BossRushDynamicItemRegistry.EnsureRegistered(ids[i]);
                    if (registered) probe = ItemAssetsCollection.InstantiateSync(ids[i]);
                    if (probe == null) reason = "item_factory_missing";
                    else if (probe.TypeID != ids[i]) reason = "item_factory_wrong_type";
                    else if (string.IsNullOrWhiteSpace(probe.DisplayName)
                        || probe.DisplayName.IndexOf("BossRush_", StringComparison.Ordinal) >= 0)
                        reason = "item_name_unlocalized";
                    else if (probe.Icon == null) reason = "item_icon_missing";
                    if (probe != null) metrics += ",name=" + probe.DisplayName + ",icon=" + (probe.Icon != null);
                }
                catch (Exception e) { reason = e.ToString(); }
                finally { DestroyProbeItem(probe); }
                if (reason == null) passed++;
                Record(caseId, reason == null ? "PASS" : "FAIL", sw.ElapsedMilliseconds, metrics, reason);
                yield return null;
            }
            bool aborted = ShouldAbort();
            Record("ITEM_FACTORY_ALL", aborted ? "SKIP" : (ids.Length > 0 && passed == ids.Length ? "PASS" : "FAIL"), 0L,
                "published=" + ids.Length + ",passed=" + passed,
                aborted ? DescribeAbortReason() : "仅验证工厂、TypeID、名称和图标；战斗效果见人工清单");
        }
    }
}
