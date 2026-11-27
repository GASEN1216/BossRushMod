// ============================================================================
// ArenaExpeditionOrder.cs - 出征令：玩家的选择、本局快照、Boss 战帖倍率
// ============================================================================
// 归属（AGENTS §4.15）：状态 owner 是 WavesArenaRuntimeModule——只有它在 StartFirstWave 里 Begin、
// 在切图 / 销毁时 End；出征令面板只往 Pending 里放一份选择，HUD、通关结算、成就只读 Current。
//
// 队列：全征（或启用池不超过征程长度）不建队列，沿用旧版逐波读 Boss 池过滤缓存的行为；
// 短征 / 长征在开波那一刻从洗过牌、已做过「前 20 波不出强 Boss」处理的过滤池里取前 L-1 只，
// 末波从剩下的强 Boss 里抽一只压轴（一个强 Boss 都没启用就取剩下里基础生命最高的）。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    /// <summary>出征令上的一次选择。</summary>
    internal sealed class ArenaExpeditionOrder
    {
        private const string PrefLineup = "arena.expedition.lineup";
        private const string PrefTier = "arena.expedition.tier";
        private const string PrefLength = "arena.expedition.length";

        internal readonly ArenaExpeditionLineup Lineup;
        internal readonly ArenaExpeditionTier Tier;
        internal readonly ArenaExpeditionLength Length;
        /// <summary>加码级数（2026-11-27，见 ArenaExpeditionRaise）；按这一档的梯子长度夹取，轻松 / 噩梦恒为 0。</summary>
        internal readonly int Raise;

        internal ArenaExpeditionOrder(ArenaExpeditionLineup lineup, ArenaExpeditionTier tier, ArenaExpeditionLength length,
            int raise = 0)
        {
            Lineup = lineup;
            Tier = tier;
            Length = lineup == ArenaExpeditionLineup.Hell ? ArenaExpeditionLength.Full : length;
            Raise = ArenaExpeditionRaise.Clamp(tier, raise);
        }

        internal bool IsHell { get { return Lineup == ArenaExpeditionLineup.Hell; } }

        /// <summary>没走出征令（旧交互点、Dev 验收）时：按已配置的模式补一份「标准 · 全征」，行为与旧版一致。</summary>
        internal static ArenaExpeditionOrder FromLegacyMode(int bossesPerWave, bool infiniteHell)
        {
            ArenaExpeditionLineup lineup = infiniteHell ? ArenaExpeditionLineup.Hell
                : bossesPerWave > 1 ? ArenaExpeditionLineup.Triple : ArenaExpeditionLineup.Single;
            return new ArenaExpeditionOrder(lineup, ArenaExpeditionTier.Standard, ArenaExpeditionLength.Full);
        }

        /// <summary>面板打开时：上次怎么选的（本地偏好，丢了就是「弹指可灭 · 标准 · 全征」）。</summary>
        internal static ArenaExpeditionOrder LoadRemembered()
        {
            int lineup = BossRushLocalPrefs.GetInt(PrefLineup, (int)ArenaExpeditionLineup.Single);
            int tier = BossRushLocalPrefs.GetInt(PrefTier, (int)ArenaExpeditionTier.Standard);
            int length = BossRushLocalPrefs.GetInt(PrefLength, (int)ArenaExpeditionLength.Full);
            ArenaExpeditionTier chosen = (ArenaExpeditionTier)Mathf.Clamp(tier, 0, 3);
            return new ArenaExpeditionOrder(
                (ArenaExpeditionLineup)Mathf.Clamp(lineup, 0, 2),
                chosen,
                (ArenaExpeditionLength)Mathf.Clamp(length, 0, 2),
                ArenaExpeditionRaise.Remembered(chosen));
        }

        /// <summary>征程一栏上次停在哪一格（选无间时它是灰的，但仍记着）。</summary>
        internal static ArenaExpeditionLength LoadRememberedLength()
        {
            return (ArenaExpeditionLength)Mathf.Clamp(BossRushLocalPrefs.GetInt(PrefLength, (int)ArenaExpeditionLength.Full), 0, 2);
        }

        internal void Remember(ArenaExpeditionLength shownLength)
        {
            BossRushLocalPrefs.SetInt(PrefLineup, (int)Lineup);
            BossRushLocalPrefs.SetInt(PrefTier, (int)Tier);
            // 选无间时征程一栏是灰的，记的是玩家最后看到的那一格，下次切回单挑 / 三连还在原处。
            BossRushLocalPrefs.SetInt(PrefLength, (int)shownLength);
            ArenaExpeditionRaise.Remember(Tier, Raise);
        }
    }

    /// <summary>一局出征的快照：开波时建好，结束前不再变。</summary>
    internal sealed class ArenaExpeditionRun
    {
        internal readonly ArenaExpeditionOrder Order;
        /// <summary>开波时启用池的大小（全征的波数，也是通关箱折算的分母）。</summary>
        internal readonly int PoolCount;
        /// <summary>短征 / 长征的出场队列；null 表示全征（逐波读 Boss 池过滤缓存）。</summary>
        internal readonly List<EnemyPresetInfo> Queue;
        /// <summary>压轴 Boss（只有建了队列才有）。</summary>
        internal readonly EnemyPresetInfo Finale;

        private ArenaExpeditionRun(ArenaExpeditionOrder order, int poolCount, List<EnemyPresetInfo> queue, EnemyPresetInfo finale)
        {
            Order = order;
            PoolCount = poolCount;
            Queue = queue;
            Finale = finale;
        }

        internal bool HasQueue { get { return Queue != null; } }
        internal int Waves { get { return Queue != null ? Queue.Count : PoolCount; } }

        /// <summary>旧通关 / 无伤 / 速通 / 累计成就：全征 + 标准及以上。</summary>
        internal bool CountsForClearAchievements
        {
            get { return !Order.IsHell && Queue == null && Order.Tier >= ArenaExpeditionTier.Standard; }
        }

        /// <summary>无间波次成就：标准及以上。</summary>
        internal bool CountsForHellAchievements
        {
            get { return Order.IsHell && Order.Tier >= ArenaExpeditionTier.Standard; }
        }

        internal bool EarnsNightmareClear
        {
            get { return !Order.IsHell && Queue == null && Order.Tier == ArenaExpeditionTier.Nightmare; }
        }

        /// <summary>
        /// 建一局。<paramref name="filtered"/> 必须是开波这一刻、已洗牌并做过前 20 波保护的过滤池。
        /// </summary>
        internal static ArenaExpeditionRun Create(ArenaExpeditionOrder order, List<EnemyPresetInfo> filtered,
            Func<EnemyPresetInfo, bool> isStrong)
        {
            int poolCount = filtered != null ? filtered.Count : 0;
            if (order == null || order.IsHell || order.Length == ArenaExpeditionLength.Full || filtered == null)
            {
                return new ArenaExpeditionRun(order, poolCount, null, null);
            }
            int waves = ArenaExpeditionTuning.WavesFor(order.Length, poolCount);
            if (waves >= poolCount || waves < 2)
            {
                return new ArenaExpeditionRun(order, poolCount, null, null);
            }

            List<EnemyPresetInfo> queue = new List<EnemyPresetInfo>(waves);
            HashSet<EnemyPresetInfo> used = new HashSet<EnemyPresetInfo>();
            for (int i = 0; i < filtered.Count && queue.Count < waves - 1; i++)
            {
                EnemyPresetInfo preset = filtered[i];
                if (preset == null || used.Contains(preset)) continue;
                queue.Add(preset);
                used.Add(preset);
            }

            List<EnemyPresetInfo> strong = new List<EnemyPresetInfo>();
            EnemyPresetInfo toughest = null;
            for (int i = 0; i < filtered.Count; i++)
            {
                EnemyPresetInfo preset = filtered[i];
                if (preset == null || used.Contains(preset)) continue;
                bool strongBoss = false;
                try { strongBoss = isStrong != null && isStrong(preset); }
                catch (Exception) { strongBoss = false; }
                if (strongBoss) strong.Add(preset);
                if (toughest == null || preset.baseHealth > toughest.baseHealth) toughest = preset;
            }
            EnemyPresetInfo finale = strong.Count > 0 ? strong[UnityEngine.Random.Range(0, strong.Count)] : toughest;
            if (finale == null)
            {
                return new ArenaExpeditionRun(order, poolCount, null, null);
            }
            queue.Add(finale);
            return new ArenaExpeditionRun(order, poolCount, queue, finale);
        }

        internal bool IsFinaleWave(int waveIndex)
        {
            return Queue != null && Finale != null && waveIndex == Queue.Count - 1;
        }
    }

    /// <summary>
    /// 出征令的全局读点。唯一写者是 <see cref="WavesArenaRuntimeModule"/>（Begin / End）与出征令面板（SetPending）。
    /// </summary>
    internal static class ArenaExpeditionState
    {
        private static ArenaExpeditionOrder pending;
        private static ArenaExpeditionRun current;

        internal static ArenaExpeditionRun Current { get { return current; } }

        internal static ArenaExpeditionTier CurrentTier
        {
            get { return current != null ? current.Order.Tier : ArenaExpeditionTier.Standard; }
        }

        /// <summary>本局加码级数（没走出征令是 0）。</summary>
        internal static int CurrentRaise
        {
            get { return current != null ? current.Order.Raise : 0; }
        }

        internal static void SetPending(ArenaExpeditionOrder order) { pending = order; }

        internal static ArenaExpeditionOrder TakePending()
        {
            ArenaExpeditionOrder order = pending;
            pending = null;
            return order;
        }

        internal static void Begin(ArenaExpeditionRun run)
        {
            current = run;
            ArenaExpeditionHud.Show();
        }

        internal static void End()
        {
            current = null;
            pending = null;
            ArenaExpeditionHud.Hide();
        }

        /// <summary>WavesArenaRuntimeModule.OnDestroy 调用。</summary>
        internal static void ResetStaticCaches()
        {
            End();
        }
    }

    /// <summary>
    /// 战帖倍率挂在 Boss 身上：生命、枪械 / 近战伤害走 PercentageMultiply（来源是本类型，不和全局倍率的
    /// BossStatScaling 修饰抢同一个来源），反应时间直接除。挂了这个组件就算处理过（幂等）。
    /// 修饰器不怕之后改 BaseValue：龙裔、龙皇、女巫在 RegisterContentWaveBoss 之后才设自己的生命并 SetHealth(MaxHealth)，
    /// 读到的已经是乘过战帖的上限；冠军之影、蚀日花冠登记在生成完成之后，这里按原比例把当前血量补到新上限。
    /// </summary>
    internal sealed class ArenaExpeditionBossScaling : MonoBehaviour
    {
        private CharacterMainControl character;
        private ArenaExpeditionTier tier;
        private bool applied;
        private float healthFactor = 1f;

        internal static void ApplyNow(CharacterMainControl character, ArenaExpeditionTier tier)
        {
            ArenaExpeditionBossScaling scaling = Attach(character, tier);
            if (scaling != null) scaling.Apply();
        }

        /// <summary>这只 Boss 被战帖乘过的生命倍率（掉落、无间奖金按「未乘战帖」算时用）。</summary>
        internal static float HealthFactorOf(CharacterMainControl character)
        {
            if (character == null) return 1f;
            ArenaExpeditionBossScaling scaling = character.GetComponent<ArenaExpeditionBossScaling>();
            return scaling != null && scaling.applied ? scaling.healthFactor : 1f;
        }

        private static ArenaExpeditionBossScaling Attach(CharacterMainControl character, ArenaExpeditionTier tier)
        {
            if (character == null || !ArenaExpeditionTuning.ScalesBosses(tier)) return null;
            if (character.GetComponent<ArenaExpeditionBossScaling>() != null) return null;
            ArenaExpeditionBossScaling scaling = character.gameObject.AddComponent<ArenaExpeditionBossScaling>();
            scaling.character = character;
            scaling.tier = tier;
            return scaling;
        }

        private void Apply()
        {
            if (applied || character == null) return;
            applied = true;
            float hpMult = ArenaExpeditionTuning.HealthMultiplier(tier);
            float dmgMult = ArenaExpeditionTuning.DamageMultiplier(tier);
            float reaction = ArenaExpeditionTuning.ReactionDivisor(tier);
            try
            {
                Item item = character.CharacterItem;
                Health health = character.Health;
                float oldMax = health != null ? health.MaxHealth : 0f;
                float ratio = oldMax > 0f && health != null ? Mathf.Clamp01(health.CurrentHealth / oldMax) : 1f;
                if (item != null)
                {
                    MultiplyStat(item, "MaxHealth", hpMult);
                    MultiplyStat(item, "GunDamageMultiplier", dmgMult);
                    MultiplyStat(item, "MeleeDamageMultiplier", dmgMult);
                    healthFactor = hpMult;
                }
                if (health != null && health.MaxHealth > 0f)
                {
                    health.SetHealth(health.MaxHealth * (ratio > 0f ? ratio : 1f));
                }
                if (reaction > 1f)
                {
                    AICharacterController ai = character.GetComponentInChildren<AICharacterController>();
                    if (ai != null)
                    {
                        ai.baseReactionTime /= reaction;
                        ai.reactionTime /= reaction;
                        ai.shootDelay /= reaction;
                    }
                }
                ModBehaviour.DevLog("[BossRush] 出征令战帖 " + tier + " 已作用于 " + character.name
                    + "：生命 x" + hpMult + "，伤害 x" + dmgMult + "，反应 /" + reaction);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BossRush] 出征令战帖倍率应用失败（该 Boss 按原样）: " + e.Message);
            }
        }

        private static void MultiplyStat(Item item, string statName, float multiplier)
        {
            if (Mathf.Approximately(multiplier, 1f)) return;
            Stat stat = item.GetStat(statName);
            if (stat == null) return;
            stat.RemoveAllModifiersFromSource(typeof(ArenaExpeditionBossScaling));
            stat.AddModifier(new Modifier(ModifierType.PercentageMultiply, multiplier - 1f, true, int.MaxValue,
                typeof(ArenaExpeditionBossScaling)));
        }
    }
}
