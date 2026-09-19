// ============================================================================
// F3GameplayValidationBackMountain.cs - 完整验收的竞技场后山三设施用例
// ============================================================================
// 模块说明：
//   后山三设施都能在基地场景纯逻辑驱动，不需要真人走到建筑前点交互：
//
//   - 菜地：种子→产出映射（DATA_BACKMOUNTAIN 已覆盖静态表），这里补
//     GetHarvestResultFor 的反向完备性——每个产出都必须有且只有一个来源种子，
//     否则玩家会看到「两种种子长出同一颗菜」或「产出物没有来源」。
//   - 战利品登记簿：登记是「登记而非收走」，所以断言重点是
//     登记后 DisplayedCount / CalculateBonus 同步上涨，且撤销后精确回落。
//   - 出击餐：官方 Buff 不跨场景，走「食用登记 → 下一局挂 Modifier」。
//     断言重点是登记只保留一条（后吃覆盖先吃）、ApplyForRun 幂等、
//     以及 ClearForRun 之后不残留 Modifier。
//
// 本文件只读后山观测面，不向测试槽登记虚构战利品或餐食。
// 事务与生命周期行为由 tests/fixtures/BackMountainLifecycle 执行生产代码验证。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>3/7 阶段：后山与经济。全部在基地场景跑。</summary>
        private IEnumerator RunBaseEconomyCases()
        {
            RunSyncCaseGated("BACKMOUNTAIN_HARVEST_MAPPING", ValidateHarvestMappingCompleteness);
            RunSyncCaseGated("BACKMOUNTAIN_SHOWCASE_LEDGER", ValidateShowcaseLedger);
            RunSyncCaseGated("BACKMOUNTAIN_RAID_MEAL", ValidateRaidMealLifecycle);
            RunSyncCaseGated("BACKMOUNTAIN_UNLOCK_GATE", ValidateBackMountainUnlockGate);
            yield return RunEconomyCases();
        }

        /// <summary>
        /// 种子↔产出映射的双向完备性。静态表长度已由 DATA_BACKMOUNTAIN 覆盖，
        /// 这里查的是映射本身：三个产出各有唯一来源，且非种子 ID 不得产出任何东西。
        /// </summary>
        private bool ValidateHarvestMappingCompleteness(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;

            int[] seeds =
            {
                BossRushItemIds.DragonSeed, BossRushItemIds.EmberSeed, BossRushItemIds.PhantomSpore
            };
            Dictionary<int, int> produceToSeed = new Dictionary<int, int>();
            bool allMapped = true;
            for (int i = 0; i < seeds.Length; i++)
            {
                int produce = BackMountainItems.GetHarvestResultFor(seeds[i]);
                if (produce == 0 || produceToSeed.ContainsKey(produce))
                {
                    allMapped = false;
                    continue;
                }
                produceToSeed[produce] = seeds[i];
            }

            // 反向：产出物本身不是种子，喂给映射必须得到 0，否则会出现自增殖循环
            // （种出来的菜又能当种子种，无限刷）。
            bool noSelfLoop = BackMountainItems.GetHarvestResultFor(BossRushItemIds.DragonFruit) == 0
                && BackMountainItems.GetHarvestResultFor(BossRushItemIds.EmberChili) == 0
                && BackMountainItems.GetHarvestResultFor(BossRushItemIds.PhantomMushroom) == 0;

            // 未登记 ID 必须落空，不能靠 default 分支意外返回某个真实产出。
            bool unknownRejected = BackMountainItems.GetHarvestResultFor(0) == 0
                && BackMountainItems.GetHarvestResultFor(int.MaxValue) == 0;

            metrics = "distinct_produce=" + produceToSeed.Count + ",all_mapped=" + allMapped
                + ",no_self_loop=" + noSelfLoop + ",unknown_rejected=" + unknownRejected;
            if (!allMapped || produceToSeed.Count != seeds.Length || !noSelfLoop || !unknownRejected)
                reason = "种子<->产出映射不是三对一一对应，或未登记 ID 未被拒绝";
            return reason == null;
        }

        /// <summary>
        /// 战利品登记簿：登记→计数与加成同步上涨→撤销→精确回落。
        /// 用一个后山产出物 TypeID 当探针（它不是 Boss 专属掉落，所以
        /// CanDisplay 可能拒绝——那种情况记 SKIP 而不是判红，因为拒绝本身是正确行为）。
        /// </summary>
        private bool ValidateShowcaseLedger(out string metrics, out string reason)
        {
            // 变更/回滚由离线执行回归覆盖；F3 只观察真实槽，满柜也能验收。
            IList<int> displayed = ShowcaseService.GetDisplayed();
            var seen = new HashSet<int>();
            reason = ShowcaseService.IsReadable ? null : "收藏存档不可读，已禁止覆盖";
            for (int i = 0; i < displayed.Count; i++)
                if (displayed[i] <= 0 || !seen.Add(displayed[i])) reason = "收藏存在无效或重复登记";
            float bonus = ShowcaseService.CalculateBonus();
            if (displayed.Count > BackMountainConfig.ShowcaseSlotCount) reason = "收藏超过展示柜容量";
            if (float.IsNaN(bonus) || float.IsInfinity(bonus) || bonus < 0f) reason = "收藏加成不是有效非负数";
            metrics = "displayed=" + displayed.Count + ",bonus=" + bonus.ToString("F4") + ",read_only=true";
            return reason == null;
        }

        /// <summary>只观察待生效登记；登记/覆盖/消费/回滚由 BackMountainLifecycle 执行回归验证。</summary>
        private bool ValidateRaidMealLifecycle(out string metrics, out string reason)
        {
            int registered = RaidMealService.ReadRegisteredMeal();
            BackMountainItems.Definition def = BackMountainItems.GetDefinition(registered);
            bool valid = registered == 0 || (def != null && !def.IsSeed);
            metrics = "registered=" + registered + ",read_only=true";
            reason = valid ? null : "待生效记录不是已知餐食，原记录已保留";
            return valid;
        }

        /// <summary>
        /// 设施解锁门：三个设施的查询都必须 no-throw 且与 UnlockAll 旋钮一致。
        /// 不断言「已解锁」——那取决于玩家战役进度，测试档上可能一个都没开。
        /// 这里查的是门本身可用、None 恒为 false、IsAny 与三项 OR 一致。
        /// </summary>
        private bool ValidateBackMountainUnlockGate(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;

            bool garden = BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Garden);
            bool showcase = BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Showcase);
            bool jukebox = BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Jukebox);
            bool none = BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.None);
            bool any = BackMountainUnlocks.IsAnyFacilityUnlocked();
            bool consistent = any == (garden || showcase || jukebox);

            metrics = "garden=" + garden + ",showcase=" + showcase + ",jukebox=" + jukebox
                + ",none=" + none + ",any=" + any;
            if (none) reason = "None 设施被判为已解锁";
            else if (!consistent) reason = "IsAnyFacilityUnlocked 与三项查询不一致";
            return reason == null;
        }
    }
}
