// ============================================================================
// F3GameplayValidationBackMountain.cs - 完整验收的竞技场后山三设施用例
// ============================================================================
// 模块说明：
//   后山三设施都能在基地场景纯逻辑驱动，不需要真人走到建筑前点交互：
//
//   - 菜地：种子→产出映射（DATA_BACKMOUNTAIN 已覆盖静态表），这里补
//     GetHarvestResultFor 的反向完备性——每个产出都必须有且只有一个来源种子，
//     否则玩家会看到「两种种子长出同一颗菜」或「产出物没有来源」。
//   - 陈列加成（2026-09-22 改接官方枪械展示架 / 假人）：只观察缓存快照的合法性；
//     SHOWCASE_OFFICIAL_PROBE 打印各官方展示建筑槽位的标签与 Mod 物品 CanPlug（只读，DevLog 明细）；
//   - 菜地工地：GARDEN_SITE_GATE 只读工地门的状态（不激活、不付款、不写键）。
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
            RunSyncCaseGated("BACKMOUNTAIN_SHOWCASE_DISPLAY", ValidateShowcaseDisplay);
            RunSyncCaseGated("BACKMOUNTAIN_RAID_MEAL", ValidateRaidMealLifecycle);
            RunSyncCaseGated("BACKMOUNTAIN_UNLOCK_GATE", ValidateBackMountainUnlockGate);
            RunSyncCaseGated("GARDEN_SITE_GATE", ValidateGardenSiteGate);
            RunSyncCaseGated("SHOWCASE_OFFICIAL_PROBE", ValidateOfficialShowcaseProbe);
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

        /// <summary>陈列缓存快照的合法性：无重复、不超容量、加成有效非负、来源版本可解释。</summary>
        private bool ValidateShowcaseDisplay(out string metrics, out string reason)
        {
            // 变更/回滚由离线执行回归覆盖；F3 只观察真实槽。
            IList<int> displayed = ShowcaseService.GetDisplayed();
            var seen = new HashSet<int>();
            reason = ShowcaseService.IsReadable ? null : "陈列存档不可读，已禁止覆盖";
            for (int i = 0; i < displayed.Count; i++)
                if (displayed[i] <= 0 || !seen.Add(displayed[i])) reason = "陈列存在无效或重复条目";
            float bonus = ShowcaseService.CalculateBonus();
            if (displayed.Count > BackMountainConfig.ShowcaseSlotCount) reason = "陈列超过加成容量";
            if (float.IsNaN(bonus) || float.IsInfinity(bonus) || bonus < 0f) reason = "陈列加成不是有效非负数";
            int source = ShowcaseService.SourceVersion;
            if (source < 1 || source > 2) reason = "陈列来源版本不可解释";
            metrics = "displayed=" + displayed.Count + ",bonus=" + bonus.ToString("F4") + ",source=" + source
                + ",subscriptions=" + ShowcaseDisplayScanner.SubscriptionCount + ",read_only=true";
            return reason == null;
        }

        /// <summary>官方菜地工地的门：只读工地 / 父物体 / CostTaker / 建成状态（不激活、不付款、不写键）。</summary>
        private bool ValidateGardenSiteGate(out string metrics, out string reason)
        {
            return GardenConstructionSite.ProbeSiteGate(
                BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Garden), GardenSeedInjector.IsInjected,
                out metrics, out reason);
        }

        /// <summary>官方展示建筑探针：槽位标签与 Mod 物品 CanPlug 明细进 DevLog，metrics 只放计数（只读）。</summary>
        private bool ValidateOfficialShowcaseProbe(out string metrics, out string reason)
        {
            return ShowcaseDisplayScanner.ProbeOfficialShowcases(out metrics, out reason);
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
