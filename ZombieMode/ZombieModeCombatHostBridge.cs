using System.Collections;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;
using Duckov.Utilities;
using Duckov.UI;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Duckov.MiniMaps;
using Duckov.Scenes;
using Duckov.Economy;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using Duckov.Buffs;

namespace BossRush
{
    public partial class ModBehaviour
    {
        // ZombieModeBossController.cs
        private void RegisterZombieModeBossRuntime(int runId, CharacterMainControl boss, ZombieModeBossKind kind)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RegisterZombieModeBossRuntime(runId, boss, kind);
        }

        private void TickZombieModeBossController(float deltaTime)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeBossController(deltaTime);
        }

        internal void TickZombieModeTitanState(ZombieModeTitanState state, ZombieModeBossInstance instance, float now)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeTitanState(state, instance, now);
        }

        internal void TickZombieModeHunterState(ZombieModeHunterState state, ZombieModeBossInstance instance, float now)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeHunterState(state, instance, now);
        }

        internal void TickZombieModeSplitterState(ZombieModeSplitterState state, ZombieModeBossInstance instance, float now)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeSplitterState(state, instance, now);
        }

        internal void TickZombieModeShielderState(ZombieModeShielderState state, ZombieModeBossInstance instance, float now)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeShielderState(state, instance, now);
        }

        internal void TickZombieModeCorruptorState(ZombieModeCorruptorState state, ZombieModeBossInstance instance, float now)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeCorruptorState(state, instance, now);
        }

        private void HandleZombieModeBossHurt(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl victim)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.HandleZombieModeBossHurt(runId, marker, victim);
        }

        public float AbsorbZombieModeBossFinalDamage(CharacterMainControl boss, ZombieModeEnemyRuntimeMarker bossMarker, float finalDamage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.AbsorbZombieModeBossFinalDamage(boss, bossMarker, finalDamage) : 0f;
        }

        public float ApplyZombieModeShielderAuraFinalDamageReduction(CharacterMainControl target, float finalDamage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.ApplyZombieModeShielderAuraFinalDamageReduction(target, finalDamage) : 0f;
        }

        public bool TryApplyZombieModeShielderAuraReductionPublic(CharacterMainControl target, ref float damageValue)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryApplyZombieModeShielderAuraReductionPublic(target, ref damageValue);
        }

        private void HandleZombieModeBossDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.HandleZombieModeBossDeathEffects(runId, marker, character);
        }

        public void DealZombieModeRuntimeAreaDamageToPlayer(int runId, Vector3 origin, float radius, float damage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.DealZombieModeRuntimeAreaDamageToPlayer(runId, origin, radius, damage);
        }

        public void DealZombieModeRuntimeAreaDamageToPlayer(int runId, CharacterMainControl source, Vector3 origin, float radius, float damage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.DealZombieModeRuntimeAreaDamageToPlayer(runId, source, origin, radius, damage);
        }

        internal void DealZombieModeRuntimeAreaDamageToPlayer(int runId, CharacterMainControl source, Vector3 origin, float radius, float damage, Buff buff)
        {
            if (zombieModeRuntimeModule != null) zombieModeRuntimeModule.DealZombieModeAreaDamageToPlayer(runId, source, origin, radius, damage, buff);
        }

        public void TryApplyZombieModePlayerSlow(int runId, float percent, float duration)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TryApplyZombieModePlayerSlow(runId, percent, duration);
        }

        private void ClearZombieModeSupportSpawnQueue()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ClearZombieModeSupportSpawnQueue();
        }

        private void RemoveZombieModeHunterFrenzyModifiers(ZombieModeHunterState hunter)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RemoveZombieModeHunterFrenzyModifiers(hunter);
        }

        internal Coroutine StartZombieModeBossCoroutineForRuntimeModule(IEnumerator routine, int runId)
        {
            return StartZombieModeCoroutine(routine, runId);
        }

        internal AICharacterController GetZombieModeEnemyAIForBossRuntimeModule(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker)
        {
            return GetZombieModeEnemyAI(enemyObject, marker);
        }

        internal void SetZombieModeEnemyTargetToMainPlayerForBossRuntimeModule(AICharacterController ai)
        {
            SetZombieModeEnemyTargetToMainPlayer(ai);
        }

        internal int CollectZombieModeRuntimeEnemyMarkersForBossRuntimeModule(
            int runId,
            System.Collections.Generic.List<ZombieModeEnemyRuntimeMarker> results,
            bool includeBosses)
        {
            return CollectZombieModeRuntimeEnemyMarkers(runId, results, includeBosses);
        }

        internal float GetZombieModeBossDamageScaleForRuntimeModule(int wave)
        {
            return GetZombieModeBossDamageScale(wave);
        }


        // ZombieModeEnemyRuntime.cs
        internal bool IsZombieModeKnownEnemy(CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.IsZombieModeKnownEnemy(character);
        }

        internal bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl character, out ZombieModeEnemyRuntimeMarker marker)
        {
            marker = null;
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryGetZombieModeKnownEnemyMarker(character, out marker);
        }

        internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RegisterZombieModeEnemyInstanceId(character);
        }

        internal void RegisterZombieModeEnemyInstanceId(CharacterMainControl character, ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RegisterZombieModeEnemyInstanceId(character, marker);
        }

        internal void UnregisterZombieModeEnemyInstanceId(CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.UnregisterZombieModeEnemyInstanceId(character);
        }

        internal void ClearZombieModeEnemyInstanceIds()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ClearZombieModeEnemyInstanceIds();
        }

        private ZombieModeEnemyRuntimeMarker RegisterZombieModeEnemyRuntimeShell(
            int runId,
            CharacterMainControl enemy,
            bool isBoss = false,
            ZombieModeBossKind bossKind = ZombieModeBossKind.Titan,
            int overridePointValue = -1,
            ZombieModeEnemyKind enemyKind = ZombieModeEnemyKind.Normal,
            ZombieModeSpecialKind specialKind = ZombieModeSpecialKind.None,
            System.Collections.Generic.List<ZombieModeEliteAffix> eliteAffixes = null)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null
                ? module.RegisterZombieModeEnemyRuntimeShell(
                    runId,
                    enemy,
                    isBoss,
                    bossKind,
                    overridePointValue,
                    enemyKind,
                    specialKind,
                    eliteAffixes)
                : null;
        }

        internal int CalculateZombieModeEnemyPurificationPointsForRuntimeModule(bool isBoss, ZombieModeEnemyKind enemyKind)
        {
            return CalculateZombieModeEnemyPurificationPoints(isBoss, enemyKind);
        }

        internal void RestoreZombieModeVisualScaleForRuntimeModule(ZombieModeEnemyRuntimeMarker marker)
        {
            RestoreZombieModeVisualScale(marker);
        }

        internal void ReleaseZombieModeFootMarkerForRuntimeModule(ZombieModeEnemyRuntimeMarker marker)
        {
            ReleaseZombieModeFootMarker(marker);
        }

        private static void RestoreZombieModeVisualScale(ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule.RestoreZombieModeVisualScale(marker);
        }

        private static void ReleaseZombieModeFootMarker(ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule.ReleaseZombieModeFootMarker(marker);
        }

        private static AICharacterController GetZombieModeEnemyAI(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker)
        {
            return ZombieModeRuntimeModule.GetZombieModeEnemyAI(enemyObject, marker);
        }

        private bool ShouldSuppressZombieModeEnemyAggroForSafeZone()
        {
            return zombieModeRuntimeModule.ShouldSuppressZombieModeEnemyAggroForSafeZone();
        }

        private bool TryMoveZombieModeEnemyOutsideSafeZone(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker, bool suppressThreat)
        {
            return zombieModeRuntimeModule.TryMoveZombieModeEnemyOutsideSafeZone(enemyObject, marker, suppressThreat);
        }

        private void SetZombieModeEnemyThreatSuppressed(GameObject enemyObject, ZombieModeEnemyRuntimeMarker marker, bool suppressed)
        {
            zombieModeRuntimeModule.SetZombieModeEnemyThreatSuppressed(enemyObject, marker, suppressed);
        }

        private bool ShouldZombieModeEnemyAggroPlayerNow()
        {
            return zombieModeRuntimeModule.ShouldZombieModeEnemyAggroPlayerNow();
        }

        // ZombieModePollution.cs
        private void SetZombieModeRendererColor(Renderer renderer, Color color)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.SetZombieModeRendererColor(renderer, color);
        }

        private GameObject CreateZombieModeFlatZoneVisual(string name, Vector3 origin, float radius, float height, Color color)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(GameObject);
            }
            return module.CreateZombieModeFlatZoneVisual(name, origin, radius, height, color);
        }

        private ZombieModeEnemyKind RollZombieModeEnemyKind()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(ZombieModeEnemyKind);
            }
            return module.RollZombieModeEnemyKind();
        }

        private ZombieModeSpecialKind RollZombieModeSpecialKind()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(ZombieModeSpecialKind);
            }
            return module.RollZombieModeSpecialKind();
        }

        private List<ZombieModeEliteAffix> RollZombieModeEliteAffixes()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(List<ZombieModeEliteAffix>);
            }
            return module.RollZombieModeEliteAffixes();
        }

        private int CalculateZombieModeEnemyPurificationPoints(bool isBoss, ZombieModeEnemyKind enemyKind)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(int);
            }
            return module.CalculateZombieModeEnemyPurificationPoints(isBoss, enemyKind);
        }

        private void ApplyZombieModeEnemyTuning(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ApplyZombieModeEnemyTuning(enemy, marker);
        }

        internal void ApplyZombieModeEnemyDefense(
            Health health,
            ref DamageInfo damageInfo,
            ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ApplyZombieModeEnemyDefense(health, ref damageInfo, marker);
        }

        private bool IsZombieModeDamageFromMeleeWeapon(DamageInfo damageInfo)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(bool);
            }
            return module.IsZombieModeDamageFromMeleeWeapon(damageInfo);
        }

        private bool ItemHasZombieModeTag(ItemStatsSystem.Item item, string tagName)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(bool);
            }
            return module.ItemHasZombieModeTag(item, tagName);
        }

        private void ApplyZombieModeHealthOnlyMultiplier(
            CharacterMainControl character,
            float healthMultiplier,
            ZombieModeEnemyRuntimeMarker marker = null)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ApplyZombieModeHealthOnlyMultiplier(character, healthMultiplier, marker);
        }

        private void ApplyZombieModeEnemyCombatStatMultipliers(
            CharacterMainControl enemy,
            float damageMultiplier,
            float speedMultiplier,
            ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ApplyZombieModeEnemyCombatStatMultipliers(enemy, damageMultiplier, speedMultiplier, marker);
        }

        // RuntimeModule 通过窄桥读取仍由波次与入口 partial 持有的策略计算。
        internal int GetZombieModePacingWaveForRuntimeModule()
        {
            return GetZombieModePacingWave();
        }

        internal float GetZombieModeWaveSpeedMultiplierForRuntimeModule(int wave)
        {
            return GetZombieModeWaveSpeedMultiplier(wave);
        }

        internal Duckov.Utilities.Tag FindZombieModeTagByNameForRuntimeModule(string tagName)
        {
            return FindZombieModeTagByName(tagName);
        }

        // ZombieModePollution_RuntimeSkills.cs
        private void HandleZombieModeSpecialDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.HandleZombieModeSpecialDeathEffects(runId, marker, character);
        }

        private void HandleZombieModeEliteDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.HandleZombieModeEliteDeathEffects(runId, marker, character);
        }

        private Cysharp.Threading.Tasks.UniTask SpawnZombieModeSmallSplitAsync(int runId, Vector3 position)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null)
            {
                return default(Cysharp.Threading.Tasks.UniTask);
            }
            return module.SpawnZombieModeSmallSplitAsync(runId, position);
        }

        internal void TryExecuteZombieModeEnemyRuntimeSkill(ZombieModeEnemyRuntimeMarker marker)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TryExecuteZombieModeEnemyRuntimeSkill(marker);
        }

        internal void RefreshZombieModeCommanderAuraTargets(
            int runId,
            CharacterMainControl commander,
            float radius,
            Dictionary<int, ZombieModeCommanderAuraTargetRuntime> trackedTargets)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefreshZombieModeCommanderAuraTargets(runId, commander, radius, trackedTargets);
        }

        private void StartZombieModeTelegraphedAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage,
            float delay,
            string label,
            bool followSourcePosition = false)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.StartZombieModeTelegraphedAreaDamage(runId, source, origin, radius, damage, delay, label, followSourcePosition);
        }

        public void SpawnZombieModeDamageCloud(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float duration,
            float damagePerSecond,
            float tickInterval,
            string cloudName,
            Color cloudColor,
            bool followSourcePosition)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.SpawnZombieModeDamageCloud(runId, source, origin, radius, duration, damagePerSecond, tickInterval, cloudName, cloudColor, followSourcePosition);
        }

        public void TryExecuteZombieModeHarasserProjectileImpact(
            int runId,
            CharacterMainControl source,
            Vector3 impactPosition,
            float damage,
            float slowRadius,
            float slowPercent,
            float slowDuration)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TryExecuteZombieModeHarasserProjectileImpact(runId, source, impactPosition, damage, slowRadius, slowPercent, slowDuration);
        }

        public void TryExecuteZombieModeTelegraphedAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TryExecuteZombieModeTelegraphedAreaDamage(runId, source, origin, radius, damage);
        }

        public void TryApplyZombieModePlayerSlowInArea(int runId, Vector3 origin, float radius, float percent, float duration)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TryApplyZombieModePlayerSlowInArea(runId, origin, radius, percent, duration);
        }

        private void DealZombieModeAreaDamageToPlayer(int runId, Vector3 origin, float radius, float damage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.DealZombieModeAreaDamageToPlayer(runId, origin, radius, damage);
        }

        private void DealZombieModeAreaDamageToPlayer(int runId, CharacterMainControl source, Vector3 origin, float radius, float damage)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.DealZombieModeAreaDamageToPlayer(runId, source, origin, radius, damage);
        }

        public void DealZombieModeExplosionAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage,
            bool canHurtSelf = false)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.DealZombieModeExplosionAreaDamage(runId, source, origin, radius, damage, canHurtSelf);
        }

        // Compatibility entry for callers outside the Zombie runtime module.
        internal Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieForRuntimeModule(
            int runId,
            Vector3 position,
            ZombieModeEnemyKind forcedEnemyKind,
            bool forceEnemyKind,
            System.Func<bool> isSpawnPhaseStillAllowed)
        {
            return TrySpawnZombieModeNormalZombieAsync(
                runId,
                position,
                forcedEnemyKind,
                forceEnemyKind,
                isSpawnPhaseStillAllowed);
        }


        // ZombieModeWaveController.cs
        private void RegisterZombieModeEventListeners(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RegisterZombieModeEventListeners(runId);
        }

        private void BeginZombieModePreparation(int runId, bool initial, bool extractionOpportunity)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.BeginZombieModePreparation(runId, initial, extractionOpportunity);
        }

        private void TickZombieModeWaveController(float deltaTime)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieModeWaveController(deltaTime);
        }

        private void StartZombieModeWave(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.StartZombieModeWave(runId);
        }

        private bool IsZombieModeAmbientZombieSpawnPhase(ZombieModeCombatPhase phase)
        {
            return ZombieModeRuntimeModule.IsZombieModeAmbientZombieSpawnPhase(phase);
        }

        private int GetZombieModeAmbientPressureTarget()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeAmbientPressureTarget() : 0;
        }

        private int GetZombieModeWavePressureTarget(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeWavePressureTarget(wave);
        }

        private int GetZombieModePacingWave()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null
                ? module.GetZombieModePacingWave()
                : Mathf.Max(1, zombieModeRunState.CurrentWave + (zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat ? 0 : 1));
        }

        private static int GetZombieModeWaveCycleIndex(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeWaveCycleIndex(wave);
        }

        private static float GetZombieModeBossHealthScale(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeBossHealthScale(wave);
        }

        private static int GetZombieModeBossCountForWave(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeBossCountForWave(wave);
        }

        private static float GetZombieModeBossDamageScale(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeBossDamageScale(wave);
        }

        private static float GetZombieModeBossRewardScale(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeBossRewardScale(wave);
        }

        private static int GetZombieModeBossRewardSelectionCount(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeBossRewardSelectionCount(wave);
        }

        private static int GetZombieModeNormalWaveStageIndex(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeNormalWaveStageIndex(wave);
        }

        private float GetZombieModeWaveSpeedMultiplier(int wave)
        {
            return ZombieModeRuntimeModule.GetZombieModeWaveSpeedMultiplier(wave);
        }

        private float GetZombieModeSpawnPointMinPlayerDistance()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeSpawnPointMinPlayerDistance() : ZombieModeTuning.EarlyWaveSpawnPointMinPlayerDistance;
        }

        private bool IsZombieModeBossWave(int wave)
        {
            return ZombieModeRuntimeModule.IsZombieModeBossWave(wave);
        }

        private void BeginZombieModeExtractionOpportunity(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.BeginZombieModeExtractionOpportunity(runId);
        }

        private void SpawnZombieModeDeathStars(int runId, Vector3 position, int totalValue, int starCount)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.SpawnZombieModeDeathStars(runId, position, totalValue, starCount);
        }

        internal bool IsZombieModePlayerInsideActiveSafeZoneForWaveRuntimeModule()
        {
            return zombieModeRuntimeModule.IsZombieModePlayerInsideActiveSafeZone();
        }

        internal void HandleZombieModeOptionHealthHurtForWaveRuntimeModule(
            int runId,
            Health health,
            DamageInfo damageInfo,
            CharacterMainControl victim,
            ZombieModeEnemyRuntimeMarker marker)
        {
            HandleZombieModeOptionHealthHurt(runId, health, damageInfo, victim, marker);
        }

        internal void HandleZombieModeOptionHealthDeadForWaveRuntimeModule(
            int runId,
            Health health,
            DamageInfo damageInfo,
            CharacterMainControl victim,
            ZombieModeEnemyRuntimeMarker marker)
        {
            HandleZombieModeOptionHealthDead(runId, health, damageInfo, victim, marker);
        }

        internal void TrySpawnZombieModeBossDropForWaveRuntimeModule(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)
        {
            TrySpawnZombieModeBossDrop(runId, marker, position);
        }

        internal void TrySpawnZombieModeEnemyDropForWaveRuntimeModule(int runId, ZombieModeEnemyRuntimeMarker marker, Vector3 position)
        {
            TrySpawnZombieModeEnemyDrop(runId, marker, position);
        }

        internal void CleanupZombieModeEnemiesNearPlayerSafeZoneForWaveRuntimeModule(int runId, string reason)
        {
            CleanupZombieModeEnemiesNearPlayerSafeZone(runId, reason);
        }

        internal void CleanupZombieModeExpiredDropCandidatesForWaveRuntimeModule(bool forceWaveCleanup)
        {
            CleanupZombieModeExpiredDropCandidates(forceWaveCleanup);
        }

        internal void SpawnPendingZombieModeEliteSquadForWaveRuntimeModule(int runId)
        {
            SpawnPendingZombieModeEliteSquad(runId);
        }

        internal Vector3 GetZombieModeSpawnPositionForWaveRuntimeModule()
        {
            return GetZombieModeSpawnPosition();
        }

        internal Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieForWaveRuntimeModule(
            int runId,
            Vector3 position,
            Func<bool> isSpawnPhaseStillAllowed)
        {
            return TrySpawnZombieModeNormalZombieAsync(
                runId,
                position,
                isSpawnPhaseStillAllowed: isSpawnPhaseStillAllowed);
        }

        internal ZombieModeBossKind GetZombieModeBossKindForWaveRuntimeModule(int bossIndex)
        {
            return GetZombieModeBossKindForIndex(bossIndex);
        }

        internal Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieForWaveRuntimeModule(
            int runId, Vector3 position, ZombieModeEnemyKind forcedEnemyKind, bool forceEnemyKind, Func<bool> isSpawnPhaseStillAllowed)
        {
            return TrySpawnZombieModeNormalZombieAsync(runId, position, forcedEnemyKind, forceEnemyKind, isSpawnPhaseStillAllowed);
        }

        internal Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeBossForWaveRuntimeModule(
            int runId,
            Vector3 position,
            ZombieModeBossKind kind)
        {
            return TrySpawnZombieModeBossAsync(runId, position, kind);
        }

        internal Vector3 GetZombieModeBossSpawnPositionForWaveRuntimeModule(int bossIndex)
        {
            return GetZombieModeBossSpawnPosition(bossIndex);
        }

        internal int CollectZombieModeRuntimeEnemyMarkersForWaveRuntimeModule(
            int runId,
            List<ZombieModeEnemyRuntimeMarker> results,
            bool includeBosses)
        {
            return CollectZombieModeRuntimeEnemyMarkers(runId, results, includeBosses);
        }

        internal bool TryGetZombieModeReliableSpawnPositionForWaveRuntimeModule(out Vector3 position)
        {
            return TryGetZombieModeReliableSpawnPosition(out position);
        }

        internal void RecycleZombieModeTemporaryNpcsForWaveRuntimeModule(int runId)
        {
            RecycleZombieModeTemporaryNpcs(runId);
        }

        internal void RecycleZombieModeTemporaryRealNpcsForWaveRuntimeModule(int runId)
        {
            RecycleZombieModeTemporaryRealNpcs(runId);
        }

        internal bool TryGiveZombieModeWaveClearHealingItemForWaveRuntimeModule()
        {
            return TryGiveZombieModeWaveClearHealingItem();
        }

        internal bool HasZombieModePendingPurificationStarsForWaveRuntimeModule()
        {
            return HasZombieModePendingPurificationStars();
        }

        internal void ForceCollectZombieModePendingPurificationStarsForWaveRuntimeModule(int runId)
        {
            ForceCollectZombieModePendingPurificationStars(runId);
        }

        internal bool CreateZombieModePurificationPointForWaveRuntimeModule(int runId, Vector3 position, int value)
        {
            return CreateZombieModePurificationPoint(runId, position, value);
        }
        private bool CollectZombieModeSpawnPoints(int runId)
        {
            return zombieModeRuntimeModule.CollectZombieModeSpawnPoints(runId);
        }

        private Vector3 GetZombieModeSpawnPosition()
        {
            return zombieModeRuntimeModule.GetZombieModeSpawnPosition();
        }

        private bool TryGetZombieModeReliableSpawnPosition(out Vector3 position)
        {
            return zombieModeRuntimeModule.TryGetZombieModeReliableSpawnPosition(out position);
        }

        private bool TryGetNearestZombieModeMapSpawnPositionToPlayer(out Vector3 position)
        {
            return zombieModeRuntimeModule.TryGetNearestZombieModeMapSpawnPositionToPlayer(out position);
        }

        private Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieAsync(
            int runId,
            Vector3 position,
            ZombieModeEnemyKind forcedEnemyKind = ZombieModeEnemyKind.Normal,
            bool forceEnemyKind = false,
            System.Func<bool> isSpawnPhaseStillAllowed = null)
        {
            return zombieModeRuntimeModule.TrySpawnZombieModeNormalZombieAsync(runId, position, forcedEnemyKind, forceEnemyKind, isSpawnPhaseStillAllowed);
        }

        private Cysharp.Threading.Tasks.UniTask<CharacterMainControl> TrySpawnZombieModeBossAsync(int runId, Vector3 position, ZombieModeBossKind kind)
        {
            return zombieModeRuntimeModule.TrySpawnZombieModeBossAsync(runId, position, kind);
        }

        private ZombieModeBossKind GetZombieModeBossKindForIndex(int bossIndex)
        {
            return zombieModeRuntimeModule.GetZombieModeBossKindForIndex(bossIndex);
        }

        private Vector3 GetZombieModeBossSpawnPosition(int bossIndex)
        {
            return zombieModeRuntimeModule.GetZombieModeBossSpawnPosition(bossIndex);
        }

    }
}
