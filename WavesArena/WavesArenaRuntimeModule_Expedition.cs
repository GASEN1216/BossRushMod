// 出征令在竞技场模块里的接线：本局快照的唯一 owner（Begin / End）、短征长征的出场队列、
// 战帖倍率的两个落点（普通 Boss 在全局倍率之后乘，内容 Boss 在 RegisterContentWaveBoss 登记时乘）、
// 战帖战规（2026-11-25）：休整倍率进倒计时，失明挡掉烽烟圈来源；天色 / 天象、天灾、词缀、厄运与常驻诅咒、
// 焚城由局内服务对象 ArenaTrials 承担（本文件是它唯一的 owner：开局 Begin、收局 End，开波 / 收波 / 首领登记转进去）；
// 首领词缀的分身 / 双生压着收波（HandleBossDeath 推波前问、战规放开时交回来，推波仍在本模块）与词缀身体的刷怪；
// 加码（2026-11-27，ArenaExpeditionRaise）：休整、烽烟圈、通关箱、无间奖金、横幅与通关记录读本局的加码级数；
// 通关箱件数 / 品质、无间奖金，以及压轴 / 噩梦的水墨标题幕。规则与数值见 ArenaExpeditionTuning.cs、Trials/ArenaTrialsTuning.cs。
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

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
            expeditionWaveHeld = false;
            ArenaTrials.Begin(this, run, IsExpeditionTrialLive);
            ModBehaviour.DevLog("[BossRush] 出征令：阵容=" + order.Lineup + "，战帖=" + order.Tier + "，加码=" + order.Raise + "，征程=" + order.Length
                + "，波数=" + run.Waves + (run.Finale != null ? "，压轴=" + run.Finale.displayName : string.Empty));
        }

        /// <summary>战规对象每帧问一次：竞技场还在打、不是 Mode D 借用、快照还在。任何一条不满足它当帧自毁。</summary>
        private bool IsExpeditionTrialLive()
        {
            return IsActive && owner != null && !owner.IsModeDActive && ArenaExpeditionState.Current != null;
        }

        /// <summary>当前地图的首领刷新点（战规的焚城圈心、天灾落点用）。</summary>
        internal Vector3[] ExpeditionSpawnPoints()
        {
            return GetCurrentSpawnPoints();
        }

        /// <summary>
        /// SpawnNextEnemy 定好这一波的预设、出完标题幕之后调用：通知战规开波。压轴 = 短征 / 长征的末波，
        /// 全征（非无间）的最后一波在战规里也按压轴算（词缀条数、焚城收得更紧）。
        /// </summary>
        private void BeginExpeditionWave()
        {
            expeditionWaveHeld = false;
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null) return;
            bool finale = run.IsFinaleWave(CurrentEnemyIndex)
                || (!InfiniteHellMode && !run.HasQueue && TotalEnemies > 0 && CurrentEnemyIndex == TotalEnemies - 1);
            ArenaTrials.NotifyWaveStart(CurrentEnemyIndex, finale);
        }

        /// <summary>ProceedAfterWaveFinished 推进波次之前调用：通知战规收波（天灾收掉、血债恢复、焚城退去）。</summary>
        private static void EndExpeditionWave()
        {
            ArenaTrials.NotifyWaveEnd();
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

        /// <summary>当前这局的加码级数（不在出征令局里、Mode D 借用本模块时是 0）。</summary>
        private int LiveExpeditionRaise()
        {
            if (ArenaExpeditionState.Current == null || !IsActive || owner == null || owner.IsModeDActive) return 0;
            return ArenaExpeditionState.CurrentRaise;
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

        /// <summary>波间休整：轻松 ×1.5，困难 / 噩梦（以及标准加码到急行军）急行军 ×0.5。</summary>
        private float ExpeditionRestSeconds(float seconds)
        {
            return seconds * ArenaExpeditionTuning.RestMultiplier(LiveExpeditionTier(), LiveExpeditionRaise());
        }

        /// <summary>每 5 波的额外休整：急行军取消。</summary>
        private float ExpeditionMilestoneRestSeconds(float seconds)
        {
            return ArenaExpeditionTuning.KeepsMilestoneRest(LiveExpeditionTier(), LiveExpeditionRaise()) ? seconds : 0f;
        }

        /// <summary>普通 Boss：全局倍率之后当场乘战帖。preset 交给战规，分身 / 双生 / 护卫照它再叫。</summary>
        private void ApplyExpeditionTierToSpawnedBoss(CharacterMainControl character, EnemyPresetInfo preset)
        {
            ArenaExpeditionBossScaling.ApplyNow(character, LiveExpeditionTier());
            ArenaTrials.NotifyBossJoined(character, preset);
        }

        /// <summary>
        /// 内容 Boss（龙裔、龙皇、女巫、冠军之影、蚀日花冠）作为竞技场波次登记时乘（修饰器不怕它们之后改 BaseValue）。
        /// 它们自带阶段与专属刷怪，不给 preset：战规只给它们挂身法 / 死相两类词缀，不另叫身体。
        /// </summary>
        private void ApplyExpeditionTierToContentBoss(CharacterMainControl character)
        {
            ArenaExpeditionBossScaling.ApplyNow(character, LiveExpeditionTier());
            ArenaTrials.NotifyBossJoined(character, null);
        }

        // ------------------------------------------------------------------
        // 首领词缀：分身 / 双生压着收波
        // ------------------------------------------------------------------

        /// <summary>本波的收波被词缀压着（首领倒了，分身 / 双生还有身体要打）。开局、开波清零。</summary>
        private bool expeditionWaveHeld;

        /// <summary>
        /// HandleBossDeath 两处推波之前调用：这只首领死后还有分身 / 双生要打（或正在叫出来），就先不收波。
        /// 死亡结算（叫分身、开双生计时）在 HoldWaveFor 里幂等地做掉——实例 OnDeadEvent 先于静态 Health.OnDead，
        /// 走到这里时战规可能还没收到这次死亡。
        /// </summary>
        private bool ExpeditionHoldsWaveEnd(CharacterMainControl boss)
        {
            ArenaTrials trials = ArenaTrials.Active;
            if (trials == null || trials.Affixes == null) return false;
            if (!trials.Affixes.HoldWaveFor(boss)) return false;
            expeditionWaveHeld = true;
            ModBehaviour.DevLog("[BossRush] 出征令：首领已倒，分身 / 双生还在，先不收波");
            return true;
        }

        /// <summary>战规说压着的身体都打完了（或压满上限散掉了）：照常推波。没压着时什么都不做。</summary>
        internal void ExpeditionReleaseHeldWave()
        {
            if (!expeditionWaveHeld) return;
            expeditionWaveHeld = false;
            ModBehaviour.DevLog("[BossRush] 出征令：分身 / 双生已清，收波");
            ProceedAfterWaveFinished();
        }

        /// <summary>10 秒自检之前问：压着收波时不强推。战规异常没了却还压着的，当场放开、照常推波。</summary>
        private bool ExpeditionHoldsWave()
        {
            if (!expeditionWaveHeld) return false;
            if (ArenaTrials.Active != null) return true;
            ExpeditionReleaseHeldWave();
            return true;
        }

        /// <summary>
        /// 词缀叫出来的身体（分身、双生、护卫、召唤物）：照这只首领的角色预设再刷一只，走与标准刷怪相同的阵营安全网
        /// （跟本体同阵营，仍不与玩家敌对的兜到 wolf，AGENTS §4.5）、无间强化、全局倍率与战帖、变异词条、解除距离休眠与落地校验；
        /// 不进本波座位（不写 CurrentBoss / CurrentWaveBosses）、不登记掉落、死了不掉箱。体型、生命与身份由战规在拿到之后定。
        /// template / team 由战规在首领登记时记下（本体死后会被官方延时销毁，双生要隔 10 秒才拉起）。
        /// </summary>
        internal async UniTask<CharacterMainControl> SpawnExpeditionAffixBodyAsync(EnemyPresetInfo preset, CharacterRandomPreset template,
            Teams team, Vector3 position, Func<bool> isCurrent)
        {
            if (preset == null || template == null || owner == null) return null;
            CharacterMainControl character = null;
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                Vector3 facing = player != null ? player.transform.position - position : Vector3.forward;
                facing.y = 0f;
                if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                character = await template.CreateCharacterAsync(position, facing.normalized, relatedScene, null, false);
                if (character == null) return null;
                if (!IsActive || (isCurrent != null && !isCurrent()))
                {
                    UnityEngine.Object.Destroy(character.gameObject);
                    return null;
                }
                try
                {
                    if (character.Team != team) character.SetTeam(team);
                    if (!Team.IsEnemy(Teams.player, character.Team)) character.SetTeam(Teams.wolf);
                }
                catch (Exception teamEx)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] 出征令词缀身体阵营兜底失败: " + teamEx.Message);
                }
                character.gameObject.name = "BossRush_Affix_" + preset.displayName;
                character.dropBoxOnDead = false;
                if (InfiniteHellMode) ApplyInfiniteHellScaling(character, preset);
                ApplyBossStatMultiplier(character);
                ArenaExpeditionBossScaling.ApplyNow(character, LiveExpeditionTier());
                character.gameObject.SetActive(true);
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);
                MutatorManager.ApplyToEnemy(character);
                CharacterMainControl main = CharacterMainControl.Main;
                AICharacterController ai = character.GetComponentInChildren<AICharacterController>();
                if (ai != null && main != null && main.mainDamageReceiver != null)
                {
                    ai.forceTracePlayerDistance = 500f;
                    ai.searchedEnemy = main.mainDamageReceiver;
                    ai.SetTarget(main.mainDamageReceiver.transform);
                    ai.SetNoticedToTarget(main.mainDamageReceiver);
                    ai.noticed = true;
                }
                if (enemyRecoveryMonitor != null)
                {
                    owner.StartCoroutine(enemyRecoveryMonitor.DelayedBossPositionValidation(character, 0.5f));
                }
                return character;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令词缀身体生成失败: " + e.Message);
                if (character != null)
                {
                    try { UnityEngine.Object.Destroy(character.gameObject); }
                    catch (Exception) { }
                }
                return null;
            }
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

        /// <summary>通关箱件数：按征程折算、按战帖增减、每级加码 +5%（全征标准档不加码与旧版一致）。</summary>
        private int ExpeditionVictoryChestCount(int baseCount)
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null) return baseCount;
            return ArenaExpeditionTuning.VictoryChestCount(baseCount, run.Order.Tier, run.Waves, run.PoolCount, run.Order.Raise);
        }

        private int ExpeditionVictoryQualityMin(int baseMin)
        {
            return ArenaExpeditionTuning.VictoryQualityMin(ArenaExpeditionState.CurrentTier, baseMin);
        }

        /// <summary>无间奖金：先把战帖乘上去的生命除掉（奖金不该因为 Boss 更肉再翻一倍），再乘战帖的奖金倍率。</summary>
        private float ExpeditionHellCashFactor(CharacterMainControl boss)
        {
            float healthFactor = ArenaExpeditionBossScaling.HealthFactorOf(boss);
            float cash = ArenaExpeditionTuning.HellCashMultiplier(ArenaExpeditionState.CurrentTier, ArenaExpeditionState.CurrentRaise);
            return healthFactor > 0f ? cash / healthFactor : cash;
        }

        /// <summary>Boss 掉落按「未乘战帖」的生命算：每只 Boss 自带的掉落在所有战帖下都不变。</summary>
        private static float ExpeditionLootHealth(CharacterMainControl boss, float maxHealth)
        {
            float healthFactor = ArenaExpeditionBossScaling.HealthFactorOf(boss);
            return healthFactor > 0f ? maxHealth / healthFactor : maxHealth;
        }

        /// <summary>
        /// 战规的额外掉落件数：首领词缀（1–2 条 +1、3–4 条 +2）+ 血月下倒下的波次首领 +1（2026-11-27，尸身上演血月献祭）。
        /// 不是出征令、分身 / 召唤物都是 0；只在填掉落时问一次。
        /// </summary>
        private static int ExpeditionAffixBonusLoot(CharacterMainControl boss)
        {
            ArenaTrials trials = ArenaTrials.Active;
            if (trials == null) return 0;
            int bonus = trials.Affixes != null ? trials.Affixes.BonusLootFor(boss) : 0;
            if (trials.Sky != null) bonus += trials.Sky.ClaimBloodMoonLoot(boss);
            return bonus;
        }

        /// <summary>首领掉落填完之后：断补把弹药堆叠减半。</summary>
        private static void ExpeditionAfterBossLoot(CharacterMainControl boss)
        {
            ArenaTrials trials = ArenaTrials.Active;
            if (trials != null && trials.Curses != null) trials.Curses.HalveAmmo(boss);
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

        /// <summary>通关一局（非无间）：记下这一档通关过的最高加码（本地偏好，出征令上标「最高」）。</summary>
        private void RecordExpeditionRaiseCleared()
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null || run.Order.IsHell || owner == null || owner.IsModeDActive) return;
            if (ArenaExpeditionRaise.RecordCleared(run.Order.Tier, run.Order.Raise))
            {
                ModBehaviour.DevLog("[BossRush] 出征令：" + run.Order.Tier + " 最高加码记为 " + run.Order.Raise);
            }
        }

        /// <summary>通关横幅难度名后面补「 · 战帖 · 加码 · 征程」（标准 · 不加码 · 全征不补，与旧版一致）。</summary>
        private string ExpeditionBannerSuffix()
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null) return string.Empty;
            string suffix = string.Empty;
            if (run.Order.Tier != ArenaExpeditionTier.Standard) suffix += " · " + ArenaExpeditionTuning.TierName(run.Order.Tier);
            if (run.Order.Raise > 0) suffix += " · " + ArenaExpeditionRaise.Label(run.Order.Raise);
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
            ArenaTrials.End();
            ArenaExpeditionState.End();
            ArenaInkTitleCard.Stop();
            ArenaExpeditionOrderPanel.Close();
        }
    }
}
