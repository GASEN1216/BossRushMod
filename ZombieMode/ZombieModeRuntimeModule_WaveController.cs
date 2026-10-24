using System.Collections;
using System.Collections.Generic;
using System;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule : BossRushRuntimeModuleBase
    {
        private Action<Health, DamageInfo> zombieModeOnDeadHandler;
        private Action<Health, DamageInfo> zombieModeOnHurtHandler;
        private readonly List<ZombieModeEnemyRuntimeMarker> waveEnemyMarkerScratch = new List<ZombieModeEnemyRuntimeMarker>();
        private float zombieModeLastWaveFailureWarningTime = float.NegativeInfinity;
        private bool zombieModeSettlementRunning;
        private int zombieModeSettlementRunId;
        private int zombieModeSettlementWave;
        private float zombieModeSettlementRetryTime;

        private void LogZombieModeWaveFailure(string step, Exception exception)
        {
            float now = Time.unscaledTime;
            if (now - zombieModeLastWaveFailureWarningTime < 10f) return;
            zombieModeLastWaveFailureWarningTime = now;
            Debug.LogWarning("[ZombieMode] wave operation failed: run=" + runState.RunId +
                " wave=" + runState.CurrentWave + " phase=" + runState.CombatPhase +
                " step=" + step + " error=" + exception);
        }

        private void RunZombieModeWaveSideEffect(string step, Action action)
        {
            try { action(); }
            catch (Exception exception) { LogZombieModeWaveFailure(step, exception); }
        }

        internal void RegisterZombieModeEventListeners(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            UnregisterZombieModeEventListeners();
            zombieModeOnDeadHandler = delegate(Health health, DamageInfo damageInfo)
            {
                // 不能让 Mod 的死亡副作用打断官方 OnDead 后的禁用 / 销毁流程。
                try { HandleZombieModeHealthDead(runId, health, damageInfo); }
                catch (Exception exception) { LogZombieModeWaveFailure("DeathEvent", exception); }
            };
            zombieModeOnHurtHandler = delegate(Health health, DamageInfo damageInfo)
            {
                HandleZombieModeHealthHurt(runId, health, damageInfo);
            };
            Health.OnDead += zombieModeOnDeadHandler;
            Health.OnHurt += zombieModeOnHurtHandler;
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.EventListener, null, null, UnregisterZombieModeEventListeners);
        }

        private void UnregisterZombieModeEventListeners()
        {
            if (zombieModeOnDeadHandler != null)
            {
                Health.OnDead -= zombieModeOnDeadHandler;
                zombieModeOnDeadHandler = null;
            }

            if (zombieModeOnHurtHandler != null)
            {
                Health.OnHurt -= zombieModeOnHurtHandler;
                zombieModeOnHurtHandler = null;
            }
        }

        private void HandleZombieModeHealthHurt(int runId, Health health, DamageInfo damageInfo)
        {
            // 全局事件 hot path 早返：丧尸模式未激活时直接 return，
            // 避免对所有非丧尸伤害事件做 marker 查询。
            if (runState.LifecyclePhase == ZombieModeLifecyclePhase.None)
            {
                return;
            }
            if (!IsZombieModeRunValid(runId) || health == null || damageInfo.fromCharacter == null)
            {
                return;
            }

            CharacterMainControl victim = health.TryGetCharacter();
            // O(1) HashSet 早返替代 GetComponent<ZombieModeEnemyRuntimeMarker>（审查 §3.1）。
            // 非丧尸模式敌人不走 marker 路径，也不能误触发安全区取消。
            ZombieModeEnemyRuntimeMarker marker;
            if (victim == null || !TryGetZombieModeKnownEnemyMarker(victim, out marker))
            {
                return;
            }

            if (marker != null && marker.RunId == runId)
            {
                TryHandleZombieModeSafeZonePlayerAttack(runId, damageInfo, victim);
                HandleZombieModeOptionHealthHurt(runId, health, damageInfo, victim, marker);
                if (marker.IsBoss)
                {
                    HandleZombieModeBossHurt(runId, marker, victim);
                }
            }

        }

        // 玩家在任意阶段的当前安全区内直接伤害丧尸时，立即取消整个安全区。
        // 单纯开枪、装填、投掷、误伤非丧尸目标、出圈攻击都不触发。
        private void TryHandleZombieModeSafeZonePlayerAttack(int runId, DamageInfo damageInfo, CharacterMainControl victim)
        {
            if (damageInfo.fromCharacter == null ||
                !damageInfo.fromCharacter.IsMainCharacter ||
                damageInfo.isFromBuffOrEffect ||
                !IsZombieModeSafeZoneCancellingWeapon(damageInfo) ||
                victim == null ||
                !AnyZombieModeSafeZoneActive ||
                !IsZombieModePlayerInsideActiveSafeZone() ||
                !ZombieModePhaseGuards.AllowsSafeZone(runState.CombatPhase))
            {
                return;
            }

            CancelZombieModeSafeZone(runId, "PlayerAttack");
        }

        /// <summary>
        /// 只有枪械与近战武器的直接伤害才会取消安全区。手雷、投掷物和以玩家为来源的
        /// 奖励弹道不算，否则被动型奖励会让玩家在准备期完全保不住安全区。
        /// </summary>
        private bool IsZombieModeSafeZoneCancellingWeapon(DamageInfo damageInfo)
        {
            if (damageInfo.fromWeaponItemID <= 0)
            {
                return false;
            }

            ItemStatsSystem.ItemMetaData metaData = ItemStatsSystem.ItemAssetsCollection.GetMetaData(damageInfo.fromWeaponItemID);
            if (metaData.id <= 0 || metaData.tags == null)
            {
                return false;
            }

            for (int i = 0; i < metaData.tags.Length; i++)
            {
                Duckov.Utilities.Tag tag = metaData.tags[i];
                if (tag != null &&
                    (tag.name == "Gun" ||
                     tag.name == "Weapon" ||
                     tag.name == "MeleeWeapon" ||
                     tag.name == "Melee"))
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleZombieModeHealthDead(int runId, Health health, DamageInfo damageInfo)
        {
            // 全局事件 hot path 早返：丧尸模式未激活时直接 return。
            if (runState.LifecyclePhase == ZombieModeLifecyclePhase.None)
            {
                return;
            }
            if (!IsZombieModeRunValid(runId) || health == null)
            {
                return;
            }

            CharacterMainControl character = health.TryGetCharacter();
            if (character != null && character.IsMainCharacter)
            {
                FailZombieModeActive(runId);
                return;
            }

            // O(1) HashSet 早返；非丧尸模式敌人死亡直接 ignore（审查 §3.1）。
            ZombieModeEnemyRuntimeMarker marker;
            if (character == null || !TryGetZombieModeKnownEnemyMarker(character, out marker))
            {
                return;
            }

            if (marker == null || marker.RunId != runId)
            {
                return;
            }

            if (marker.DeathSettled || marker.RemovedFromRuntime)
            {
                return;
            }

            // 官方 Health.Hurt 的致死顺序是 OnDead -> SetActive(false) -> OnHurt。
            // 必须在 DeathSettled 和 hot-path marker 注销前处理，否则致死一击不会取消安全区。
            try { TryHandleZombieModeSafeZonePlayerAttack(runId, damageInfo, character); }
            catch (Exception exception) { LogZombieModeWaveFailure("LethalSafeZoneAttack", exception); }
            marker.DeathSettled = true;
            // 一旦 DeathSettled 就从 hot path 集合移除——后续技能命中尸体不会重新进入 marker 路径。
            UnregisterZombieModeEnemyInstanceId(character);

            runState.LivingZombieCount = Mathf.Max(0, runState.LivingZombieCount - 1);
            if (!marker.IsBoss)
            {
                runState.LivingNormalZombieCount = Mathf.Max(0, runState.LivingNormalZombieCount - 1);
                runState.CurrentWaveKills++;
            }

            int pointValue = Mathf.Max(1, marker.PurificationPointValue);
            int starCount = GetZombieModeDeathStarCount(marker);
            try { SpawnZombieModeDeathStars(runId, character.transform.position, pointValue, starCount); }
            catch (Exception exception) { LogZombieModeWaveFailure("DeathStars", exception); }

            // 选项死效可能触发连锁爆炸、重入其他死亡事件；先去重、计数并生成本体星点。
            // 各副作用分别隔离，任何掉落 / 特效异常都不能吞掉击杀或阻止达标结算。
            if (marker.IsBoss)
            {
                try { HandleZombieModeBossDefeated(runId, marker, character); }
                catch (Exception exception) { LogZombieModeWaveFailure("BossDefeated", exception); }
            }
            try { HandleZombieModeOptionHealthDead(runId, health, damageInfo, character, marker); }
            catch (Exception exception) { LogZombieModeWaveFailure("OptionDeath", exception); }

            if (marker.IsBoss)
            {
                try { HandleZombieModeBossDeathEffects(runId, marker, character); }
                catch (Exception exception) { LogZombieModeWaveFailure("BossDeathEffects", exception); }
                try { TrySpawnZombieModeBossDrop(runId, marker, character.transform.position); }
                catch (Exception exception) { LogZombieModeWaveFailure("BossDrop", exception); }
                if (runState.CombatPhase == ZombieModeCombatPhase.Combat &&
                    runState.CurrentWaveBossesRemaining <= 0)
                {
                    CompleteZombieModeWave(runId);
                }
                PruneZombieModeRunOnlyEnemyRecords(runId);
                return;
            }

            if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                try { HandleZombieModeEliteDeathEffects(runId, marker, character); }
                catch (Exception exception) { LogZombieModeWaveFailure("EliteDeathEffects", exception); }
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                try { HandleZombieModeSpecialDeathEffects(runId, marker, character); }
                catch (Exception exception) { LogZombieModeWaveFailure("SpecialDeathEffects", exception); }
            }

            try { TrySpawnZombieModeEnemyDrop(runId, marker, character.transform.position); }
            catch (Exception exception) { LogZombieModeWaveFailure("EnemyDrop", exception); }
            if (runState.CombatPhase == ZombieModeCombatPhase.Combat &&
                runState.CurrentWaveKillTarget > 0 &&
                runState.CurrentWaveKills >= runState.CurrentWaveKillTarget)
            {
                CompleteZombieModeWave(runId);
            }
            PruneZombieModeRunOnlyEnemyRecords(runId);
        }

        internal void BeginZombieModePreparation(int runId, bool initial, bool extractionOpportunity)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            CleanupZombieModePreparationObjects(runId);
            runState.CombatPhase = initial
                ? ZombieModeCombatPhase.InitialPreparation
                : (extractionOpportunity ? ZombieModeCombatPhase.ExtractionOpportunity : ZombieModeCombatPhase.Preparation);
            runState.PreparationTimer = initial
                ? ZombieModeTuning.PreparationCountdownSeconds
                : GetZombieModeSelectedPreparationDuration(runId);
            runState.PeriodicSpawnTimer = 0f;
            runState.BeaconChanneling = false;
            runState.BeaconChannelStartTime = 0f;
            runState.ExtractionChanneling = false;
            CreateZombieModeSafeZone(runId);
            CleanupZombieModeEnemiesNearPlayerSafeZone(runId, "BeginPreparation");
            EnsureZombieModeAmbientZombiePopulation(runId);
            if (extractionOpportunity)
            {
                EnsureZombieModeExtractionArea(runId);
                ShowZombieModeExtractionOpportunityUi(runId);
            }

            string text = initial
                ? L10n.T("BossRush_ZombieMode_Banner_PreparationStarted")
                : L10n.T("BossRush_ZombieMode_Banner_PreparationNextWave");
            owner.ShowBigBanner(text);
        }

        internal void TickZombieModeWaveController(float deltaTime)
        {
            if (!IsZombieModeActive || runState.CombatPhase == ZombieModeCombatPhase.None)
            {
                return;
            }

            // 达标推进不能依赖又一次死亡回调，也不能被安全区表现 / 驱逐异常挡住。
            if (ZombieModePhaseGuards.IsCombatRunning(runState.CombatPhase) &&
                ((runState.CurrentWaveKillTarget > 0 && runState.CurrentWaveKills >= runState.CurrentWaveKillTarget) ||
                 (IsZombieModeBossWave(runState.CurrentWave) && runState.CurrentWaveBossesRemaining <= 0)))
            {
                CompleteZombieModeWave(runState.RunId);
                return;
            }
            if (runState.CombatPhase == ZombieModeCombatPhase.Settling)
            {
                TryStartZombieModeSettlement(runState.RunId);
                return;
            }

            if (AnyZombieModeSafeZoneActive)
            {
                TickZombieModeSafeZone();
            }

            if (ZombieModePhaseGuards.IsCombatRunning(runState.CombatPhase))
            {
                TickZombieModeAmbientZombiePressure(runState.RunId, deltaTime);
                return;
            }

            if (ZombieModePhaseGuards.AllowsBeacon(runState.CombatPhase))
            {
                TickZombieModeAmbientZombiePressure(runState.RunId, deltaTime);
                if (runState.BeaconChanneling || runState.ExtractionChanneling)
                {
                    return;
                }

                runState.PreparationTimer -= deltaTime;
                if (runState.PreparationTimer <= 0f)
                {
                    StartZombieModeWave(runState.RunId);
                }
            }
        }

        internal void StartZombieModeWave(int runId)
        {
            if (!IsZombieModeRunValid(runId) || !ZombieModePhaseGuards.AllowsBeacon(runState.CombatPhase))
            {
                return;
            }

            CleanupZombieModePreparationObjects(runId);
            // 普通散落物在玩家完成奖励选择和休整后、下一波正式开始时清理；Boss 奖励箱由清理函数保留。
            CleanupZombieModeExpiredDropCandidates(true);
            runState.CurrentWave++;
            runState.CurrentWaveKills = 0;
            runState.CurrentWaveBossInstances.Clear();
            runState.CurrentWaveBossesRemaining = 0;
            runState.PeriodicSpawnTimer = 0f;
            runState.NextSpawnPointIndex = 0;
            runState.PreparationTimer = 0f;
            runState.BeaconChanneling = false;
            runState.BeaconChannelStartTime = 0f;
            runState.ExtractionChanneling = false;
            runState.CombatPhase = ZombieModeCombatPhase.Combat;
            bool bossWave = IsZombieModeBossWave(runState.CurrentWave);
            if (bossWave)
            {
                runState.CurrentWaveKillTarget = 0;
                runState.CurrentWaveBossesRemaining = GetZombieModeBossCountForWave(runState.CurrentWave);
            }
            else
            {
                runState.CurrentWaveKillTarget = Mathf.Max(1, GetZombieModeBaseWaveKillTarget());
            }
            ReleaseZombieModeSafeZoneThreatSuppression();
            RunZombieModeWaveSideEffect("PendingEliteSquad", () => { SpawnPendingZombieModeEliteSquad(runId); });

            RunZombieModeWaveSideEffect("WaveIncomingBanner", () =>
                owner.ShowBigBanner(string.Format(L10n.T("BossRush_ZombieMode_Banner_WaveIncoming"), runState.CurrentWave)));
            if (bossWave)
            {
                SpawnZombieModeBossWaveAsync(runId, runState.CurrentWaveBossesRemaining).Forget();
                return;
            }
        }

        private int GetZombieModeBaseWaveKillTarget()
        {
            int wave = Mathf.Max(1, runState.CurrentWave);
            int cycle = GetZombieModeWaveCycleIndex(wave);
            int stage = GetZombieModeNormalWaveStageIndex(wave);
            return ZombieModeTuning.NormalWaveKillTargetBase +
                   cycle * ZombieModeTuning.NormalWaveKillTargetPerCycle +
                   ZombieModeTuning.NormalWaveKillTargetStageOffsets[stage];
        }

        private async UniTask SpawnZombieModeWaveAsync(int runId, int count, bool adjustKillTargetOnFailure = true)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.Combat)
                {
                    return;
                }

                CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(
                    runId,
                    GetZombieModeSpawnPosition(),
                    isSpawnPhaseStillAllowed: () => runState.CombatPhase == ZombieModeCombatPhase.Combat);
                if (zombie == null &&
                    adjustKillTargetOnFailure &&
                    IsZombieModeRunValid(runId) &&
                    runState.CombatPhase == ZombieModeCombatPhase.Combat)
                {
                    runState.CurrentWaveKillTarget = Mathf.Max(runState.CurrentWaveKills, runState.CurrentWaveKillTarget - 1);
                    if (runState.CurrentWaveKills >= runState.CurrentWaveKillTarget)
                    {
                        CompleteZombieModeWave(runId);
                        return;
                    }
                }

                await UniTask.Yield();
            }
        }

        private async UniTask SpawnZombieModeBossWaveAsync(int runId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.Combat)
                {
                    return;
                }

                ZombieModeBossKind kind = GetZombieModeBossKindForIndex(i);
                CharacterMainControl boss = null;
                // 生成失败（落点解析不到可达点、生成核心重试耗尽等）先换落点重试，全部失败才从本波扣掉
                for (int attempt = 0; attempt < ZombieModeTuning.BossSpawnAttempts && boss == null; attempt++)
                {
                    if (attempt > 0)
                    {
                        ModBehaviour.DevLog("[ZombieMode] [WARNING] 第 " + runState.CurrentWave + " 波第 " + (i + 1)
                            + " 只 Boss 生成失败，换落点重试（第 " + attempt + " 次）");
                        await UniTask.DelayFrame(ZombieModeTuning.BossSpawnRetryDelayFrames);
                        if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.Combat) return;
                    }
                    Vector3 position = attempt == 0 ? GetZombieModeBossSpawnPosition(i) : GetZombieModeSpawnPosition();
                    boss = await TrySpawnZombieModeBossAsync(runId, position, kind);
                    if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.Combat) return;
                }
                if (boss == null)
                {
                    ModBehaviour.DevLog("[ZombieMode] [WARNING] 第 " + runState.CurrentWave + " 波第 " + (i + 1)
                        + " 只 Boss 连续 " + ZombieModeTuning.BossSpawnAttempts + " 次生成失败，本波少算一只");
                    runState.CurrentWaveBossesRemaining = Mathf.Max(0, runState.CurrentWaveBossesRemaining - 1);
                    if (runState.CurrentWaveBossesRemaining <= 0)
                    {
                        CompleteZombieModeWave(runId);
                        return;
                    }
                }

                await UniTask.Yield();
            }
        }

        private void TickZombieModeAmbientZombiePressure(int runId, float deltaTime)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            if (!IsZombieModeAmbientZombieSpawnPhase(runState.CombatPhase))
            {
                return;
            }

            if (runState.CombatPhase == ZombieModeCombatPhase.Combat)
            {
                bool bossWave = IsZombieModeBossWave(runState.CurrentWave);
                int remainingToKill = runState.CurrentWaveKillTarget - runState.CurrentWaveKills;
                if (!bossWave && remainingToKill <= 0)
                {
                    return;
                }
            }

            runState.PeriodicSpawnTimer += deltaTime;
            if (runState.PeriodicSpawnTimer < GetZombieModeSpawnIntervalSeconds())
            {
                return;
            }

            runState.PeriodicSpawnTimer = 0f;
            ReconcileZombieModeLivingEnemyCounts(runId);
            int spawnCount = GetZombieModePeriodicSpawnCount();
            if (spawnCount <= 0)
            {
                return;
            }

            SpawnZombieModeWaveAcrossMapAsync(runId, spawnCount, false).Forget();
        }

        private void EnsureZombieModeAmbientZombiePopulation(int runId)
        {
            if (!IsZombieModeRunValid(runId) ||
                !IsZombieModeAmbientZombieSpawnPhase(runState.CombatPhase))
            {
                return;
            }

            ReconcileZombieModeLivingEnemyCounts(runId);
            int spawnCount = GetZombieModePeriodicSpawnCount();
            if (spawnCount <= 0)
            {
                return;
            }

            runState.PeriodicSpawnTimer = 0f;
            SpawnZombieModeWaveAcrossMapAsync(runId, spawnCount, false).Forget();
        }

        internal static bool IsZombieModeAmbientZombieSpawnPhase(ZombieModeCombatPhase phase)
        {
            return phase == ZombieModeCombatPhase.InitialPreparation ||
                   phase == ZombieModeCombatPhase.Preparation ||
                   phase == ZombieModeCombatPhase.ExtractionOpportunity ||
                   phase == ZombieModeCombatPhase.Combat;
        }

        private int GetZombieModeNormalZombieSpawnSlots()
        {
            int activeOrPending = runState.LivingNormalZombieCount + runState.PendingNormalZombieSpawns;
            return Mathf.Max(0, ZombieModeTuning.MaxNormalZombieCount - activeOrPending);
        }

        private void ReconcileZombieModeLivingEnemyCounts(int runId)
        {
            int livingTotal = CollectZombieModeRuntimeEnemyMarkers(runId, waveEnemyMarkerScratch, true);
            int livingNormal = 0;
            for (int i = 0; i < waveEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = waveEnemyMarkerScratch[i];
                if (marker != null && !marker.IsBoss)
                {
                    livingNormal++;
                }
            }

            runState.LivingZombieCount = livingTotal;
            runState.LivingNormalZombieCount = livingNormal;
            waveEnemyMarkerScratch.Clear();
        }

        private int GetZombieModePeriodicSpawnCount()
        {
            int slots = GetZombieModeNormalZombieSpawnSlots();
            if (slots <= 0)
            {
                return 0;
            }

            int activeOrPending = runState.LivingNormalZombieCount + runState.PendingNormalZombieSpawns;
            int desiredSlots = Mathf.Max(0, GetZombieModeAmbientPressureTarget() - activeOrPending);
            int batchSize = GetZombieModeSpawnBatchSize();
            return Mathf.Clamp(
                Mathf.Min(slots, Mathf.Min(desiredSlots, batchSize)),
                0,
                ZombieModeTuning.MaxNormalZombieCount);
        }

        internal int GetZombieModeAmbientPressureTarget()
        {
            int pacingWave = GetZombieModePacingWave();
            int target = GetZombieModeWavePressureTarget(pacingWave);
            if (runState.CombatPhase == ZombieModeCombatPhase.Combat)
            {
                if (!IsZombieModeBossWave(runState.CurrentWave))
                {
                    int remainingToKill = Mathf.Max(
                        0,
                        runState.CurrentWaveKillTarget - runState.CurrentWaveKills);
                    int preparationFloor = GetZombieModePreparationPressureTarget(runState.CurrentWave + 1);
                    int ebbTarget = Mathf.Max(
                        preparationFloor,
                        remainingToKill * ZombieModeTuning.NormalWavePressurePerRemainingKill);
                    target = Mathf.Min(target, ebbTarget);
                }

                return Mathf.Clamp(target, 0, ZombieModeTuning.MaxNormalZombieCount);
            }

            return GetZombieModePreparationPressureTarget(pacingWave);
        }

        private int GetZombieModePreparationPressureTarget(int wave)
        {
            int preparationTarget = Mathf.CeilToInt(
                GetZombieModeWavePressureTarget(wave) * ZombieModeTuning.PreparationPressureFraction);
            return Mathf.Clamp(
                preparationTarget,
                ZombieModeTuning.PreparationPressureMinimum,
                ZombieModeTuning.PreparationPressureMaximum);
        }

        internal static int GetZombieModeWavePressureTarget(int wave)
        {
            wave = Mathf.Max(1, wave);
            int cycle = GetZombieModeWaveCycleIndex(wave);
            if (IsZombieModeBossWave(wave))
            {
                return Mathf.Min(
                    ZombieModeTuning.BossWaveSupportPressureMaximum,
                    ZombieModeTuning.BossWaveSupportPressureBase +
                    cycle * ZombieModeTuning.BossWaveSupportPressurePerCycle);
            }

            int stage = GetZombieModeNormalWaveStageIndex(wave);
            return Mathf.Min(
                ZombieModeTuning.MaxNormalZombieCount,
                ZombieModeTuning.NormalWavePressureBase +
                cycle * ZombieModeTuning.NormalWavePressurePerCycle +
                ZombieModeTuning.NormalWavePressureStageOffsets[stage]);
        }

        private int GetZombieModeSpawnBatchSize()
        {
            if (runState.CombatPhase != ZombieModeCombatPhase.Combat)
            {
                return ZombieModeTuning.PreparationSpawnBatchSize;
            }

            int wave = Mathf.Max(1, runState.CurrentWave);
            int cycle = GetZombieModeWaveCycleIndex(wave);
            if (IsZombieModeBossWave(wave))
            {
                return Mathf.Clamp(1 + cycle / 2, 1, ZombieModeTuning.BossWaveSpawnBatchMaximum);
            }

            int stage = GetZombieModeNormalWaveStageIndex(wave);
            return Mathf.Clamp(
                ZombieModeTuning.NormalWaveSpawnBatchBase + stage / 2 + cycle / 2,
                1,
                ZombieModeTuning.NormalWaveSpawnBatchMaximum);
        }

        private float GetZombieModeSpawnIntervalSeconds()
        {
            if (runState.CombatPhase != ZombieModeCombatPhase.Combat)
            {
                return ZombieModeTuning.PreparationSpawnIntervalSeconds;
            }

            int wave = Mathf.Max(1, runState.CurrentWave);
            int cycle = GetZombieModeWaveCycleIndex(wave);
            if (IsZombieModeBossWave(wave))
            {
                return Mathf.Max(
                    ZombieModeTuning.BossWaveSpawnIntervalMinSeconds,
                    ZombieModeTuning.BossWaveSpawnIntervalStartSeconds -
                    cycle * ZombieModeTuning.BossWaveSpawnIntervalCycleStepSeconds);
            }

            int stage = GetZombieModeNormalWaveStageIndex(wave);
            return Mathf.Max(
                ZombieModeTuning.NormalWaveSpawnIntervalMinSeconds,
                ZombieModeTuning.NormalWaveSpawnIntervalStartSeconds -
                stage * ZombieModeTuning.NormalWaveSpawnIntervalStageStepSeconds -
                cycle * ZombieModeTuning.NormalWaveSpawnIntervalCycleStepSeconds);
        }

        internal int GetZombieModePacingWave()
        {
            return runState.CombatPhase == ZombieModeCombatPhase.Combat
                ? Mathf.Max(1, runState.CurrentWave)
                : Mathf.Max(1, runState.CurrentWave + 1);
        }

        internal static int GetZombieModeWaveCycleIndex(int wave)
        {
            return Mathf.Max(0, (Mathf.Max(1, wave) - 1) / 5);
        }

        internal static float GetZombieModeBossHealthScale(int wave)
        {
            return Mathf.Min(
                ZombieModeTuning.BossHealthScaleMaximum,
                1f + GetZombieModeWaveCycleIndex(wave) * ZombieModeTuning.BossHealthScalePerCycle);
        }

        internal static int GetZombieModeBossCountForWave(int wave)
        {
            return ZombieModeTuning.BossWaveCountBase +
                   GetZombieModeWaveCycleIndex(wave) * ZombieModeTuning.BossWaveCountPerCycle;
        }

        internal static float GetZombieModeBossDamageScale(int wave)
        {
            return Mathf.Min(
                ZombieModeTuning.BossDamageScaleMaximum,
                1f + GetZombieModeWaveCycleIndex(wave) * ZombieModeTuning.BossDamageScalePerCycle);
        }

        internal static float GetZombieModeBossRewardScale(int wave)
        {
            return Mathf.Min(
                ZombieModeTuning.BossRewardScaleMaximum,
                1f + GetZombieModeWaveCycleIndex(wave) * ZombieModeTuning.BossRewardScalePerCycle);
        }

        internal static int GetZombieModeBossRewardSelectionCount(int wave)
        {
            return GetZombieModeWaveCycleIndex(wave) >= ZombieModeTuning.BossBonusSelectionStartCycle
                ? ZombieModeTuning.BossRewardSelectionMaximum
                : 1;
        }

        internal static int GetZombieModeNormalWaveStageIndex(int wave)
        {
            return Mathf.Clamp((Mathf.Max(1, wave) - 1) % 5, 0, 3);
        }

        internal static float GetZombieModeWaveSpeedMultiplier(int wave)
        {
            return Mathf.Clamp(
                ZombieModeTuning.WaveSpeedMultiplierStart +
                Mathf.Max(0, wave - 1) * ZombieModeTuning.WaveSpeedMultiplierPerWave,
                ZombieModeTuning.WaveSpeedMultiplierStart,
                ZombieModeTuning.WaveSpeedMultiplierMaximum);
        }

        internal float GetZombieModeSpawnPointMinPlayerDistance()
        {
            int wave = GetZombieModePacingWave();
            if (wave <= 2)
            {
                return ZombieModeTuning.EarlyWaveSpawnPointMinPlayerDistance;
            }

            if (wave <= 5)
            {
                return ZombieModeTuning.MidWaveSpawnPointMinPlayerDistance;
            }

            return ZombieModeTuning.LateWaveSpawnPointMinPlayerDistance;
        }

        private async UniTask SpawnZombieModeWaveAcrossMapAsync(int runId, int count, bool adjustKillTargetOnFailure = true)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsZombieModeRunValid(runId) ||
                    !IsZombieModeAmbientZombieSpawnPhase(runState.CombatPhase))
                {
                    return;
                }

                if (GetZombieModeNormalZombieSpawnSlots() <= 0)
                {
                    return;
                }

                Vector3 spawnPosition;
                if (!TryGetNextZombieModeMapSpawnPosition(out spawnPosition))
                {
                    await UniTask.Yield();
                    continue;
                }

                CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(
                    runId,
                    spawnPosition,
                    isSpawnPhaseStillAllowed: () => IsZombieModeAmbientZombieSpawnPhase(runState.CombatPhase));
                if (zombie == null &&
                    adjustKillTargetOnFailure &&
                    IsZombieModeRunValid(runId) &&
                    runState.CombatPhase == ZombieModeCombatPhase.Combat)
                {
                    runState.CurrentWaveKillTarget = Mathf.Max(runState.CurrentWaveKills, runState.CurrentWaveKillTarget - 1);
                    if (runState.CurrentWaveKills >= runState.CurrentWaveKillTarget)
                    {
                        CompleteZombieModeWave(runId);
                        return;
                    }
                }

                await UniTask.Yield();
            }
        }

        private bool TryGetNextZombieModeMapSpawnPosition(out Vector3 position)
        {
            return TryGetZombieModeReliableSpawnPosition(out position);
        }

        private void HandleZombieModeBossDefeated(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            runState.CurrentWaveBossesRemaining = Mathf.Max(0, runState.CurrentWaveBossesRemaining - 1);
            for (int i = 0; i < runState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance instance = runState.CurrentWaveBossInstances[i];
                if (instance == null || instance.Character != character)
                {
                    continue;
                }

                ZombieModeHunterState hunterState = instance.SkillState as ZombieModeHunterState;
                if (hunterState != null)
                {
                    RemoveZombieModeHunterFrenzyModifiers(hunterState);
                }

                instance.Lifecycle.Alive = false;
                break;
            }

        }

        internal static bool IsZombieModeBossWave(int wave)
        {
            return wave > 0 && wave % 5 == 0;
        }

        internal void BeginZombieModeExtractionOpportunity(int runId)
        {
            BeginZombieModePreparation(runId, false, true);
        }

        private void CompleteZombieModeWave(int runId)
        {
            if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.Combat)
            {
                return;
            }

            runState.CombatPhase = ZombieModeCombatPhase.Settling;
            bool bossNode = IsZombieModeBossWave(runState.CurrentWave);
            if (bossNode)
            {
                runState.PollutionFromNatural++;
            }

            RunZombieModeWaveSideEffect("WaveClearSafeZoneCleanup",
                () => { CleanupZombieModeEnemiesNearPlayerSafeZone(runId, "CompleteWave"); });
            RunZombieModeWaveSideEffect("WaveClearTemporaryNpcs", () => { RecycleZombieModeTemporaryNpcs(runId); });
            RunZombieModeWaveSideEffect("WaveClearTemporaryRealNpcs", () => { RecycleZombieModeTemporaryRealNpcs(runId); });
            RunZombieModeWaveSideEffect("WaveClearHealing", () =>
            {
                if (!TryGiveZombieModeWaveClearHealingItem())
                    ModBehaviour.DevLog("[ZombieMode] 波次结束治疗补给发放失败");
            });

            RunZombieModeWaveSideEffect("WaveClearedBanner", () =>
                owner.ShowBigBanner(string.Format(L10n.T("BossRush_ZombieMode_Banner_WaveCleared"), runState.CurrentWave)));
            TryStartZombieModeSettlement(runId);
        }

        private void TryStartZombieModeSettlement(int runId)
        {
            if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.Settling) return;
            bool sameWave = zombieModeSettlementRunId == runId && zombieModeSettlementWave == runState.CurrentWave;
            if (sameWave && (zombieModeSettlementRunning || Time.unscaledTime < zombieModeSettlementRetryTime)) return;
            zombieModeSettlementRunId = runId;
            zombieModeSettlementWave = runState.CurrentWave;
            zombieModeSettlementRetryTime = Time.unscaledTime + 1f;
            zombieModeSettlementRunning = true;
            try
            {
                // StartCoroutine 会同步推进 iterator，故 running 必须先置位；启动失败留给 Tick 重试。
                if (owner.StartZombieModeCoroutineForRuntimeModule(
                    ZombieModeSettlementCoroutine(runId, IsZombieModeBossWave(runState.CurrentWave)), runId) == null)
                    zombieModeSettlementRunning = false;
            }
            catch (Exception exception)
            {
                zombieModeSettlementRunning = false;
                LogZombieModeWaveFailure("SettlementStart", exception);
            }
        }

        private IEnumerator ZombieModeSettlementCoroutine(int runId, bool bossNode)
        {
            int wave = runState.CurrentWave;
            try
            {
                float remaining = ZombieModeTuning.SettlementMaxWaitSeconds;
                while (IsZombieModeRunValid(runId) && runState.CombatPhase == ZombieModeCombatPhase.Settling)
                {
                    if (!HasZombieModePendingPurificationStars())
                    {
                        break;
                    }

                    if (!IsZombieModeRuntimePaused())
                    {
                        remaining -= Time.unscaledDeltaTime;
                    }

                    if (remaining <= 0f)
                    {
                        ForceCollectZombieModePendingPurificationStars(runId);
                        break;
                    }

                    yield return null;
                }

                if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.Settling)
                {
                    yield break;
                }

                ForceCollectZombieModePendingPurificationStars(runId);
                ShowZombieModeRewardSelection(runId, bossNode);
            }
            finally
            {
                // 异常 / run-only 取消也释放所有权，避免留下 Settling 且永远没有结算协程。
                if (zombieModeSettlementRunId == runId && zombieModeSettlementWave == wave)
                    zombieModeSettlementRunning = false;
            }
        }

        private int GetZombieModeDeathStarCount(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null)
            {
                return 1;
            }

            if (marker.IsBoss)
            {
                return 8;
            }

            switch (marker.EnemyKind)
            {
                case ZombieModeEnemyKind.Elite:
                    return 5;
                case ZombieModeEnemyKind.Special:
                    return 3;
                default:
                    return 1;
            }
        }

        internal void SpawnZombieModeDeathStars(int runId, Vector3 position, int totalValue, int starCount)
        {
            starCount = Mathf.Max(1, starCount);
            int perStar = Mathf.Max(1, Mathf.FloorToInt(totalValue / (float)starCount));
            int remainder = Mathf.Max(0, totalValue - perStar * starCount);
            int created = 0;
            for (int i = 0; i < starCount; i++)
            {
                int value = perStar + (i == 0 ? remainder : 0);
                Vector3 offset = starCount > 1
                    ? Quaternion.Euler(0f, 360f * i / starCount, 0f) * Vector3.forward * 0.4f
                    : Vector3.zero;
                if (CreateZombieModePurificationPoint(runId, position + offset, value))
                {
                    created++;
                }
            }

            if (created <= 0)
            {
                runState.PurificationPoints += totalValue;
            }
        }

        private void FailZombieModeActive(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            runState.CombatPhase = ZombieModeCombatPhase.FailedExit;
            owner.ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_Failed"));
            owner.CleanupZombieModeForRuntimeModule(ZombieModeFailureReason.PlayerDeath);
            try
            {
                if (SceneLoader.Instance != null)
                {
                    UniTaskExtensions.Forget(SceneLoader.Instance.LoadBaseScene(null, true));
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] [WARNING] 死亡后回主场景失败: " + e.Message);
            }
        }
    }
}
