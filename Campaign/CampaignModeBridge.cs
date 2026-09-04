// ============================================================================
// CampaignModeBridge.cs - 战役与五个既有模式之间的桥
// ============================================================================
// 这是「对模式代码零重构」的关键：战役不改任何模式的状态机，只做两件事——
//   1. 作为 partial ModBehaviour **直接读**各模式的私有运行状态（同一个类，天然可见）；
//   2. 在五条胜利/撤离漏斗上各插一行 NotifyCampaign*，把「这一局赢了」告诉战役。
//
// 【为什么是轮询 + 漏斗两种，而不是统一订阅】
//   波次推进、模式开局这类状态没有现成事件，各模式的状态机也不该为战役新增事件
//   （那就是侵入了）。整数比较的轮询每帧成本可忽略，且模式侧零改动。
//   而「通关 / 撤离成功」是一次性的、有明确落点的，插一行漏斗比轮询可靠得多。
//
// 【所有入口在战役未启用时必须零成本早返】
//   ModBehaviour.Update 每帧都会走到 TickCampaignModeBridge，
//   开关关闭时它必须是一次 bool 判断就返回。
// ============================================================================

using System;

namespace BossRush
{
    public partial class ModBehaviour
    {
        #region 轮询状态

        /// <summary>上一帧观察到的波次，用于做边沿检测，避免每帧重复上报。</summary>
        private int campaignLastObservedWave;

        /// <summary>上一帧观察到的模式标识，用于识别「刚进入某模式」。</summary>
        private string campaignLastObservedMode;

        #endregion

        #region 每帧轮询

        /// <summary>
        /// 战役桥每帧 tick：识别当前模式、武装追踪、上报波次、驱动计时。
        /// 战役未启用时一次 bool 判断即返回。
        /// </summary>
        internal void TickCampaignModeBridge(float deltaTime)
        {
            if (!IsCampaignConfiguredEnabled()) return;

            try
            {
                // 终章不依赖任何模式标志，单独维护：召唤石按需生成、决战按需让路
                TickCampaignFinalBossAltar();
                TickCampaignFinalBossYield();
                if (campaignFinalBossActive)
                {
                    // 决战进行中：目标追踪已由 StartCampaignFinalBoss 武装，
                    // 这里只驱动计时，不要再走模式识别（会把 mode 判成 null 而清掉追踪）
                    CampaignObjectiveTracker.Tick(deltaTime);
                    return;
                }

                string mode = ResolveCampaignCurrentMode();
                if (string.IsNullOrEmpty(mode))
                {
                    // 回到基地或不在任何模式里：清掉本局追踪
                    if (campaignLastObservedMode != null)
                    {
                        campaignLastObservedMode = null;
                        campaignLastObservedWave = 0;
                        CampaignObjectiveTracker.ResetSession();
                    }
                    return;
                }

                if (!string.Equals(campaignLastObservedMode, mode, StringComparison.Ordinal))
                {
                    campaignLastObservedMode = mode;
                    campaignLastObservedWave = 0;
                }

                CampaignObjectiveTracker.EnsureArmedFor(mode);
                if (!CampaignObjectiveTracker.IsArmed) return;

                int wave = GetCampaignCurrentWave();
                if (wave > campaignLastObservedWave)
                {
                    campaignLastObservedWave = wave;
                    CampaignObjectiveTracker.ReportWaveReached(wave);
                }

                CampaignObjectiveTracker.Tick(deltaTime);
            }
            catch (Exception)
            {
                // 每帧路径：不抛也不打日志
            }
        }

        #endregion

        #region 模式状态读取

        /// <summary>
        /// 当前处于哪个战役可识别的模式；都不在时返回 null。
        /// 判定顺序与入场道具优先级一致（E &gt; F &gt; G &gt; D &gt; 标准）。
        /// 无间炼狱**不算**标准竞技场：第 1 章的通关目标只认标准档。
        /// </summary>
        internal string ResolveCampaignCurrentMode()
        {
            try
            {
                if (modeEActive) return CampaignContentCatalog.ModeModeE;
                if (modeFActive) return CampaignContentCatalog.ModeModeF;
                if (modeDActive) return CampaignContentCatalog.ModeModeD;

                // 用丧尸模式自己的权威判据（IsZombieModeActive 用的就是它），
                // 而不是 LifecyclePhase != None：后者从 SelectingMap 就为真，
                // 那会让第 5 章在**玩家还在基地点地图选择界面**时就武装。
                if (zombieModeRunState != null
                    && ZombieModePhaseGuards.IsRunActive(zombieModeRunState.LifecyclePhase))
                {
                    return CampaignContentCatalog.ModeZombie;
                }

                // 必须带上 IsActive：bossRushArenaActive 从进场就为真，而开波要等玩家去点路牌，
                // 「已进竞技场、还没开波」是一个可以任意长的一等状态。只看 bossRushArenaActive
                // 会让第 1 章的无伤目标在大厅里挨一下伤就被判死，且胜利后（IsActive 已复位、
                // bossRushArenaActive 仍为真）追踪还赖着不走。
                // Mode D 也会置 IsActive，但它在上面 :109 已先行 return，不会落到这一支。
                if (bossRushArenaActive && IsActive && !infiniteHellMode)
                {
                    return CampaignContentCatalog.ModeStandard;
                }

                // 终章决战不依赖任何模式标志，由 CampaignFinalBoss 自行武装
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 当前模式的波次号。没有波次概念的模式返回 0。
        ///
        /// 标准竞技场没有 currentWave 字段：它记的是「第几个敌人」（currentEnemyIndex）
        /// 与每波 Boss 数（bossesPerWave），波次要现算——口径与 WavesArena 里
        /// completedWave 的算法一致。
        /// </summary>
        internal int GetCampaignCurrentWave()
        {
            try
            {
                if (modeDActive) return ModeDWaveIndex;

                // 与 ResolveCampaignCurrentMode 同一判据，两处必须一致，
                // 否则会出现「模式解析成标准、波次号却报丧尸的」错配。
                if (zombieModeRunState != null
                    && ZombieModePhaseGuards.IsRunActive(zombieModeRunState.LifecyclePhase))
                {
                    return zombieModeRunState.CurrentWave;
                }

                if (bossRushArenaActive)
                {
                    if (infiniteHellMode) return infiniteHellWaveIndex;
                    // currentEnemyIndex 本身就是**已完成波数**：它只在
                    // WavesArena.ProceedAfterWaveFinished（本波全部 Boss 阵亡时）自增一次，
                    // 与 bossesPerWave 无关。此前又除了一次 bossesPerWave，
                    // 多 Boss 难度下第 1 章「前 2 波无伤」实际被放大成「前 6 波无伤」。
                    // +1 是把 0 基的已完成数换算成 1 基的当前波次。
                    return currentEnemyIndex + 1;
                }

                return 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 该 Boss 是否带血猎悬赏印记。非 Mode F 或查不到时返回 false。
        /// </summary>
        internal bool HasCampaignBountyMark(CharacterMainControl boss)
        {
            try
            {
                if (!modeFActive) return false;
                if (boss == null) return false;

                // 先读瞬时闩：ModeF 的 OnDeadEvent 已经在几行前把印记从字典里移除了，
                // 此刻只有闩还记得这一杀带印记。命中即消费，避免重复计数。
                if (ConsumeModeFPlayerBountyKillLatch(boss.GetInstanceID())) return true;

                if (modeFState == null || modeFState.BountyMarksByCharacterId == null) return false;

                int marks;
                if (!modeFState.BountyMarksByCharacterId.TryGetValue(boss.GetInstanceID(), out marks)) return false;
                return marks > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 胜利 / 撤离漏斗（各模式一行调用）

        /// <summary>标准竞技场通关。由 OnAllEnemiesDefeated_LootAndRewards 调用。</summary>
        internal void NotifyCampaignStandardCleared()
        {
            if (!IsCampaignConfiguredEnabled()) return;
            try
            {
                // 无间炼狱不算标准通关
                if (infiniteHellMode) return;
                CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeStandard);
                CampaignObjectiveTracker.ReportStandardClear();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 标准通关通知失败: " + e.Message);
            }
        }

        /// <summary>白手起家完成一波。由 OnModeDWaveComplete 调用。</summary>
        internal void NotifyCampaignModeDWaveComplete(int waveIndex)
        {
            if (!IsCampaignConfiguredEnabled()) return;
            try
            {
                CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeModeD);
                CampaignObjectiveTracker.ReportWaveReached(waveIndex);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 白手起家波次通知失败: " + e.Message);
            }
        }

        /// <summary>血猎追击撤离成功。由 OnModeFExtractionSuccess 调用。</summary>
        internal void NotifyCampaignModeFExtracted()
        {
            if (!IsCampaignConfiguredEnabled()) return;
            try
            {
                CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeModeF);
                CampaignObjectiveTracker.ReportExtract();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 血猎撤离通知失败: " + e.Message);
            }
        }

        /// <summary>末日丧尸撤离成功。由 CompleteZombieModeExtractionSuccess 调用。</summary>
        internal void NotifyCampaignZombieExtracted()
        {
            if (!IsCampaignConfiguredEnabled()) return;
            try
            {
                CampaignObjectiveTracker.EnsureArmedFor(CampaignContentCatalog.ModeZombie);
                CampaignObjectiveTracker.ReportExtract();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "[WARNING] 丧尸撤离通知失败: " + e.Message);
            }
        }

        #endregion
    }
}
