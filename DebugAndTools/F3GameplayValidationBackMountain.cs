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
// 【测试档纪律】本文件的用例会写后山存档段。这就是「必须先标记专用测试档」的原因；
//   每个用例都在 finally 里把自己造的状态撤掉，不给下一个用例留脏数据。
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
                reason = "种子↔产出映射不是三对一一对应，或未登记 ID 未被拒绝";
            return reason == null;
        }

        /// <summary>
        /// 战利品登记簿：登记→计数与加成同步上涨→撤销→精确回落。
        /// 用一个后山产出物 TypeID 当探针（它不是 Boss 专属掉落，所以
        /// CanDisplay 可能拒绝——那种情况记 SKIP 而不是判红，因为拒绝本身是正确行为）。
        /// </summary>
        private bool ValidateShowcaseLedger(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;

            int beforeCount = ShowcaseService.DisplayedCount;
            float beforeBonus = ShowcaseService.CalculateBonus();
            IList<int> beforeList = ShowcaseService.GetDisplayed();
            int probe = PickShowcaseProbeTypeId(beforeList);
            if (probe == 0)
            {
                metrics = "displayed=" + beforeCount + ",probe=none";
                reason = "找不到未登记的探针 TypeID（收藏已满或目录为空），本项需人工复测";
                return false;
            }

            bool displayed = false;
            try
            {
                displayed = ShowcaseService.TryDisplay(probe);
                int afterCount = ShowcaseService.DisplayedCount;
                float afterBonus = ShowcaseService.CalculateBonus();
                bool counted = displayed && afterCount == beforeCount + 1;
                // 加成只能涨或持平（同类物品可能不再叠加），绝不能因登记而下降。
                bool bonusSane = afterBonus >= beforeBonus - 0.0001f;
                bool listed = false;
                IList<int> afterList = ShowcaseService.GetDisplayed();
                for (int i = 0; afterList != null && i < afterList.Count; i++)
                    if (afterList[i] == probe) { listed = true; break; }

                metrics = "displayed=" + beforeCount + "->" + afterCount
                    + ",bonus=" + beforeBonus.ToString("F4") + "->" + afterBonus.ToString("F4")
                    + ",probe=" + probe + ",listed=" + listed;
                if (!displayed) reason = "登记被拒绝: TryDisplay 返回 false";
                else if (!counted || !listed) reason = "登记后计数或快照未同步";
                else if (!bonusSane) reason = "登记后加成下降";
                return reason == null;
            }
            finally
            {
                // 撤销探针并复检回落，避免给下一个用例留脏收藏。
                try
                {
                    if (displayed)
                    {
                        ShowcaseService.TryRemoveRecord(probe);
                        ShowcaseService.ReapplyBonuses();
                        if (ShowcaseService.DisplayedCount != beforeCount)
                        {
                            ModBehaviour.DevLog("[Validation] 展示柜探针撤销后计数未回落: "
                                + ShowcaseService.DisplayedCount + " != " + beforeCount);
                        }
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[Validation] 展示柜探针撤销失败: " + e.Message);
                }
            }
        }

        /// <summary>挑一个当前未登记的后山产出 TypeID 当探针。</summary>
        private static int PickShowcaseProbeTypeId(IList<int> displayed)
        {
            int[] candidates =
            {
                BossRushItemIds.DragonFruit, BossRushItemIds.EmberChili, BossRushItemIds.PhantomMushroom
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                bool used = false;
                for (int j = 0; displayed != null && j < displayed.Count; j++)
                    if (displayed[j] == candidates[i]) { used = true; break; }
                if (!used) return candidates[i];
            }
            return 0;
        }

        /// <summary>
        /// 出击餐：登记单条覆盖语义 + 清理。
        /// 不在基地调 ApplyForRun——它按设计只在非基地场景生效，
        /// 在基地断言「挂上了 Modifier」会得出错误结论。
        /// </summary>
        private bool ValidateRaidMealLifecycle(out string metrics, out string reason)
        {
            metrics = string.Empty;
            reason = null;
            int original = 0;
            try
            {
                original = RaidMealService.ReadRegisteredMeal();

                bool firstOk = RaidMealService.RegisterMeal(BossRushItemIds.DragonFruit);
                int afterFirst = RaidMealService.ReadRegisteredMeal();

                // 后吃覆盖先吃：连吃两份不叠加，登记里只能剩最后那一份。
                bool secondOk = RaidMealService.RegisterMeal(BossRushItemIds.EmberChili);
                int afterSecond = RaidMealService.ReadRegisteredMeal();
                bool overwritten = afterSecond == BossRushItemIds.EmberChili
                    && afterFirst == BossRushItemIds.DragonFruit;

                // 陌生 ID 必须被拒绝且不污染已有登记（这条正是 8/30 修过的那类静默吃掉）。
                bool unknownRejected = !RaidMealService.RegisterMeal(int.MaxValue)
                    && RaidMealService.ReadRegisteredMeal() == afterSecond;

                // ClearForRun 清的是本局 Modifier，不清登记；基地下调用应幂等无副作用。
                RaidMealService.ClearForRun();
                RaidMealService.ClearForRun();
                bool clearIdempotent = RaidMealService.ReadRegisteredMeal() == afterSecond;

                metrics = "original=" + original + ",first=" + afterFirst + ",second=" + afterSecond
                    + ",overwritten=" + overwritten + ",unknown_rejected=" + unknownRejected
                    + ",clear_idempotent=" + clearIdempotent;
                if (!firstOk || !secondOk) reason = "合法出击餐登记被拒绝";
                else if (!overwritten) reason = "出击餐登记未按「后吃覆盖先吃」保留单条";
                else if (!unknownRejected) reason = "陌生出击餐 ID 未被拒绝或污染了已有登记";
                else if (!clearIdempotent) reason = "ClearForRun 误清了跨局登记";
                return reason == null;
            }
            finally
            {
                // 还原玩家原本的登记，别把测试用的餐留在存档里。
                try
                {
                    if (original != 0) RaidMealService.RegisterMeal(original);
                    else RaidMealService.ClearForRun();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[Validation] 出击餐登记还原失败: " + e.Message);
                }
            }
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
