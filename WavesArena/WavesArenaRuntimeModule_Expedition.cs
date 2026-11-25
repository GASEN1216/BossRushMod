// 出征令在竞技场模块里的接线：本局快照的唯一 owner（Begin / End）、短征长征的出场队列、
// 战帖倍率的两个落点（普通 Boss 在全局倍率之后乘，内容 Boss 在 RegisterContentWaveBoss 登记时乘）、
// 战帖战规（2026-11-25）：指定的变异词条随开波抽词条先上，休整倍率进倒计时，盲战挡掉烽烟圈来源；
// 通关箱件数 / 品质、无间奖金，以及压轴 / 噩梦的水墨标题幕。规则与数值见 ArenaExpeditionTuning.cs。
using System.Collections.Generic;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        /// <summary>StartFirstWave 在洗牌、前 20 波保护之后调用。没走出征令时按已配置的模式补一份「标准 · 全征」。</summary>
        private void BeginExpeditionRun(ArenaExpeditionOrder order, List<EnemyPresetInfo> filtered)
        {
            if (order == null || order.IsHell != InfiniteHellMode)
            {
                order = ArenaExpeditionOrder.FromLegacyMode(BossesPerWave, InfiniteHellMode);
            }
            ArenaExpeditionRun run = ArenaExpeditionRun.Create(order, filtered,
                preset => preset != null && IsEarlyWaveExcludedBoss(preset.name));
            ArenaExpeditionState.Begin(run);
            ModBehaviour.DevLog("[BossRush] 出征令：阵容=" + order.Lineup + "，战帖=" + order.Tier + "，征程=" + order.Length
                + "，波数=" + run.Waves + (run.Finale != null ? "，压轴=" + run.Finale.displayName : string.Empty));
        }

        /// <summary>本局要按顺序打的 Boss：短征 / 长征用开波时定好的队列，全征照旧读过滤池。</summary>
        private List<EnemyPresetInfo> ResolveRunPresets(List<EnemyPresetInfo> filtered)
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (InfiniteHellMode || run == null || !run.HasQueue) return filtered;
            return run.Queue;
        }

        /// <summary>当前这局是否在出征令战帖下（Mode D 借用本模块时不算）。</summary>
        private ArenaExpeditionTier LiveExpeditionTier()
        {
            if (ArenaExpeditionState.Current == null || !IsActive || owner == null || owner.IsModeDActive)
            {
                return ArenaExpeditionTier.Standard;
            }
            return ArenaExpeditionState.CurrentTier;
        }

        /// <summary>
        /// 战帖指定的变异词条（开波抽词条时交给 MutatorManager 先上）。出征令与当前模式对不上（BeginExpeditionRun
        /// 会按旧模式补「标准」）或 Mode D 借用本模块时不指定。
        /// </summary>
        private List<string> ExpeditionForcedMutators(ArenaExpeditionOrder order)
        {
            if (order == null || order.IsHell != InfiniteHellMode || owner == null || owner.IsModeDActive) return null;
            return ArenaExpeditionTuning.ForcedMutatorIds(order.Tier);
        }

        /// <summary>波间休整：轻松 ×1.5，困难 / 噩梦急行军 ×0.5。</summary>
        private float ExpeditionRestSeconds(float seconds)
        {
            return seconds * ArenaExpeditionTuning.RestMultiplier(LiveExpeditionTier());
        }

        /// <summary>每 5 波的额外休整：急行军取消。</summary>
        private float ExpeditionMilestoneRestSeconds(float seconds)
        {
            return ArenaExpeditionTuning.KeepsMilestoneRest(LiveExpeditionTier()) ? seconds : 0f;
        }

        /// <summary>普通 Boss：全局倍率之后当场乘战帖。</summary>
        private void ApplyExpeditionTierToSpawnedBoss(CharacterMainControl character)
        {
            ArenaExpeditionBossScaling.ApplyNow(character, LiveExpeditionTier());
        }

        /// <summary>内容 Boss（龙裔、龙皇、女巫、冠军之影、蚀日花冠）作为竞技场波次登记时乘（修饰器不怕它们之后改 BaseValue）。</summary>
        private void ApplyExpeditionTierToContentBoss(CharacterMainControl character)
        {
            ArenaExpeditionBossScaling.ApplyNow(character, LiveExpeditionTier());
        }

        /// <summary>压轴波（短征 / 长征末波）出长版标题幕；噩梦档其余每波出短版。</summary>
        private void PlayExpeditionTitleCard(EnemyPresetInfo preset)
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null || preset == null || InfiniteHellMode) return;
            if (run.IsFinaleWave(CurrentEnemyIndex))
            {
                ArenaInkTitleCard.Play(preset.displayName, true);
            }
            else if (run.Order.Tier == ArenaExpeditionTier.Nightmare)
            {
                ArenaInkTitleCard.Play(preset.displayName, false);
            }
        }

        /// <summary>通关箱件数：按征程折算、按战帖增减（全征标准档与旧版一致）。</summary>
        private int ExpeditionVictoryChestCount(int baseCount)
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null) return baseCount;
            return ArenaExpeditionTuning.VictoryChestCount(baseCount, run.Order.Tier, run.Waves, run.PoolCount);
        }

        private int ExpeditionVictoryQualityMin(int baseMin)
        {
            return ArenaExpeditionTuning.VictoryQualityMin(ArenaExpeditionState.CurrentTier, baseMin);
        }

        /// <summary>无间奖金：先把战帖乘上去的生命除掉（奖金不该因为 Boss 更肉再翻一倍），再乘战帖的奖金倍率。</summary>
        private float ExpeditionHellCashFactor(CharacterMainControl boss)
        {
            float healthFactor = ArenaExpeditionBossScaling.HealthFactorOf(boss);
            float cash = ArenaExpeditionTuning.HellCashMultiplier(ArenaExpeditionState.CurrentTier);
            return healthFactor > 0f ? cash / healthFactor : cash;
        }

        /// <summary>Boss 掉落按「未乘战帖」的生命算：每只 Boss 自带的掉落在所有战帖下都不变。</summary>
        private static float ExpeditionLootHealth(CharacterMainControl boss, float maxHealth)
        {
            float healthFactor = ArenaExpeditionBossScaling.HealthFactorOf(boss);
            return healthFactor > 0f ? maxHealth / healthFactor : maxHealth;
        }

        /// <summary>旧通关 / 无伤 / 速通 / 累计成就只认全征 + 标准及以上（没走出征令按旧版照算）。</summary>
        private bool ExpeditionCountsForClearAchievements()
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            return run == null || run.CountsForClearAchievements;
        }

        /// <summary>噩梦 · 全征通关：解锁噩梦通关成就。</summary>
        private void UnlockExpeditionNightmareClear()
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run != null && run.EarnsNightmareClear)
            {
                BossRushAchievementManager.TryUnlock(ArenaExpeditionTuning.NightmareClearAchievementId);
            }
        }

        /// <summary>通关横幅难度名后面补「 · 战帖 · 征程」（标准 · 全征不补，与旧版一致）。</summary>
        private string ExpeditionBannerSuffix()
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null) return string.Empty;
            string suffix = string.Empty;
            if (run.Order.Tier != ArenaExpeditionTier.Standard) suffix += " · " + ArenaExpeditionTuning.TierName(run.Order.Tier);
            if (run.HasQueue) suffix += " · " + ArenaExpeditionTuning.LengthName(run.Order.Length);
            return suffix;
        }

        private bool ExpeditionCountsForHellAchievements()
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            return run == null || run.CountsForHellAchievements;
        }

        /// <summary>切图 / 模块销毁：本局快照与角标一起收掉。</summary>
        private static void EndExpeditionRun()
        {
            ArenaExpeditionState.End();
            ArenaInkTitleCard.Stop();
            ArenaExpeditionOrderPanel.Close();
        }
    }
}
