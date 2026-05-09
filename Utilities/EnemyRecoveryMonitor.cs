using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private sealed class EnemyRecoveryState
        {
            public Vector3 lastSamplePosition;
            public float lastMovedTime;
            public float lastRecoveryTime;
            public Vector3 excludedAnchorPosition;
            public bool hasExcludedAnchorPosition;
            public int continuousFallSamples;
        }

        private const float EnemyRecoveryCheckInterval = 1f;
        private const float EnemyStationaryRecoveryDelay = 10f;
        private const float EnemyMovementThreshold = 0.6f;
        private const float EnemyMovementThresholdSqr = EnemyMovementThreshold * EnemyMovementThreshold;
        private const float EnemyRecoveryCooldown = 4f;
        private const float EnemyBelowGroundThreshold = 0.75f;
        private const float EnemyBelowPlayerThreshold = 6f;
        private const float EnemyRapidFallDeltaPerCheck = 2.5f;
        private const int EnemyRapidFallSamplesRequired = 3;
        private const float EnemyVoidRelativePlayerYThreshold = 12f;
        private const float EnemyVoidRelativeAnchorYThreshold = 6f;
        private const float EnemyGroundProbeDistance = 8f;
        private const float EnemyGroundProbeHeight = 1f;
        private const float EnemyNavMeshProbeDistance = 5f;
        private const float EnemySpawnPointExclusionRadius = 1.5f;
        private const float EnemyCurrentPointExclusionRadius = 1f;
        private const float EnemyGroundLiftOffset = 0.15f;

        private readonly Dictionary<CharacterMainControl, EnemyRecoveryState> enemyRecoveryStates
            = new Dictionary<CharacterMainControl, EnemyRecoveryState>();

        private readonly HashSet<CharacterMainControl> enemyRecoverySeenEnemies
            = new HashSet<CharacterMainControl>();

        private readonly List<CharacterMainControl> enemyRecoveryRemovalBuffer
            = new List<CharacterMainControl>();

        private static readonly List<Vector3> enemyRecoverySpawnCandidates
            = new List<Vector3>();

        private static readonly List<Vector3> enemyRecoveryModeEValidatedSpawnCandidates
            = new List<Vector3>();

        private static readonly List<Vector3> enemyRecoveryModeEValidatedRawCandidates
            = new List<Vector3>();

        private Vector3[] enemyRecoveryModeEValidatedSourcePoints = null;
        private bool enemyRecoverySpawnCandidatesArePrevalidated = false;

        private float enemyRecoveryCheckTimer = 0f;

        private void ClearEnemyRecoveryMonitorState()
        {
            enemyRecoveryCheckTimer = 0f;
            enemyRecoveryStates.Clear();
            enemyRecoverySeenEnemies.Clear();
            enemyRecoveryRemovalBuffer.Clear();
            enemyRecoverySpawnCandidates.Clear();
            enemyRecoveryModeEValidatedSpawnCandidates.Clear();
            enemyRecoveryModeEValidatedRawCandidates.Clear();
            enemyRecoveryModeEValidatedSourcePoints = null;
            enemyRecoverySpawnCandidatesArePrevalidated = false;
        }

        private void RegisterEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchorPosition)
        {
            try
            {
                if (enemy == null)
                {
                    return;
                }

                EnemyRecoveryState state;
                if (!enemyRecoveryStates.TryGetValue(enemy, out state))
                {
                    Vector3 currentPos = anchorPosition;
                    try
                    {
                        currentPos = enemy.transform.position;
                    }
                    catch (Exception positionEx)
                    {
                        DevLog("[EnemyRecovery] [WARNING] RegisterEnemyRecoveryAnchor 无法读取敌人位置: " + positionEx.Message);
                    }

                    state = new EnemyRecoveryState
                    {
                        lastSamplePosition = currentPos,
                        lastMovedTime = Time.time,
                        lastRecoveryTime = -EnemyRecoveryCooldown,
                        continuousFallSamples = 0
                    };

                    enemyRecoveryStates[enemy] = state;
                }

                state.excludedAnchorPosition = anchorPosition;
                state.hasExcludedAnchorPosition = true;
            }
            catch (Exception e)
            {
                DevLog("[EnemyRecovery] [ERROR] RegisterEnemyRecoveryAnchor failed: " + e.Message);
            }
        }

        private void UnregisterEnemyRecovery(CharacterMainControl enemy)
        {
            if (enemy == null)
            {
                return;
            }

            enemyRecoveryStates.Remove(enemy);
        }

        private void UpdateEnemyRecoveryMonitor()
        {
            try
            {
                if (!modeDActive && !modeEActive && !modeFActive && !IsActive && !IsZombieModeActive)
                {
                    ClearEnemyRecoveryMonitorState();
                    return;
                }

                enemyRecoveryCheckTimer += Time.deltaTime;
                if (enemyRecoveryCheckTimer < EnemyRecoveryCheckInterval)
                {
                    return;
                }
                enemyRecoveryCheckTimer = 0f;

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    return;
                }

                enemyRecoverySeenEnemies.Clear();

                if (modeDActive)
                {
                    MonitorEnemyRecoveryList(modeDCurrentWaveEnemies, player);
                }
                else if (modeEActive)
                {
                    MonitorEnemyRecoveryList(modeEAliveEnemies, player);
                }
                else if (modeFActive)
                {
                    MonitorEnemyRecoveryList(modeFState.ActiveBosses, player);
                }
                else if (IsZombieModeActive)
                {
                    MonitorZombieModeEnemyRecovery(player);
                }
                else
                {
                    MonitorNormalBossRushRecovery(player);
                }

                CleanupEnemyRecoveryStates();
            }
            catch (Exception e)
            {
                DevLog("[EnemyRecovery] [ERROR] UpdateEnemyRecoveryMonitor failed: " + e.Message);
            }
        }

        private void MonitorZombieModeEnemyRecovery(CharacterMainControl player)
        {
            if (zombieModeRunState == null || zombieModeRunState.RunOnlyObjects.Count <= 0)
            {
                return;
            }

            for (int i = zombieModeRunState.RunOnlyObjects.Count - 1; i >= 0; i--)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null ||
                    (record.Kind != ZombieModeRunOnlyObjectKind.Enemy && record.Kind != ZombieModeRunOnlyObjectKind.Boss) ||
                    record.GameObject == null)
                {
                    continue;
                }

                CharacterMainControl enemy = record.GameObject.GetComponent<CharacterMainControl>();
                if (enemy == null)
                {
                    enemy = record.GameObject.GetComponentInChildren<CharacterMainControl>(true);
                }

                MonitorEnemyRecovery(enemy, player);
            }
        }

        private void MonitorEnemyRecoveryList(List<CharacterMainControl> enemies, CharacterMainControl player)
        {
            if (enemies == null || enemies.Count <= 0)
            {
                return;
            }

            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                MonitorEnemyRecovery(enemies[i], player);
            }
        }

        private void MonitorNormalBossRushRecovery(CharacterMainControl player)
        {
            if (bossesPerWave > 1)
            {
                for (int i = currentWaveBosses.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl enemy = currentWaveBosses[i] as CharacterMainControl;
                    MonitorEnemyRecovery(enemy, player);
                }

                return;
            }

            CharacterMainControl singleBoss = currentBoss as CharacterMainControl;
            MonitorEnemyRecovery(singleBoss, player);
        }

        private void MonitorEnemyRecovery(CharacterMainControl enemy, CharacterMainControl player)
        {
            if (enemy == null)
            {
                return;
            }

            try
            {
                if (enemy.gameObject == null || enemy.Health == null || enemy.Health.IsDead)
                {
                    UnregisterEnemyRecovery(enemy);
                    return;
                }

                enemyRecoverySeenEnemies.Add(enemy);

                EnemyRecoveryState state;
                if (!enemyRecoveryStates.TryGetValue(enemy, out state))
                {
                    Vector3 spawnPos = enemy.transform.position;
                    state = new EnemyRecoveryState
                    {
                        lastSamplePosition = spawnPos,
                        lastMovedTime = Time.time,
                        lastRecoveryTime = -EnemyRecoveryCooldown,
                        excludedAnchorPosition = spawnPos,
                        hasExcludedAnchorPosition = true,
                        continuousFallSamples = 0
                    };

                    enemyRecoveryStates[enemy] = state;
                }

                Vector3 currentPos = enemy.transform.position;
                float now = Time.time;

                if (GetHorizontalSqrDistance(currentPos, state.lastSamplePosition) >= EnemyMovementThresholdSqr)
                {
                    state.lastMovedTime = now;
                }

                UpdateEnemyFallState(state, currentPos);

                if (now - state.lastRecoveryTime >= EnemyRecoveryCooldown)
                {
                    bool fallingOut = ShouldRecoverFallingEnemy(state, currentPos, player);
                    bool stuckUnderground = !fallingOut &&
                                            now - state.lastMovedTime >= EnemyStationaryRecoveryDelay &&
                                            ShouldRecoverStationaryEnemy(state, currentPos, player);

                    if (fallingOut || stuckUnderground)
                    {
                        string reason = fallingOut ? "falling" : "stuck";
                        Vector3 recoveredPos;
                        if (TryRecoverEnemyToNearestSpawnPoint(enemy, state, player, reason, out recoveredPos))
                        {
                            currentPos = recoveredPos;
                            now = Time.time;
                            state.lastMovedTime = now;
                            state.lastRecoveryTime = now;
                            state.lastSamplePosition = recoveredPos;
                            state.continuousFallSamples = 0;
                        }
                    }
                }

                state.lastSamplePosition = currentPos;
            }
            catch (Exception e)
            {
                DevLog("[EnemyRecovery] [ERROR] MonitorEnemyRecovery failed: " + e.Message);
            }
        }

        private void UpdateEnemyFallState(EnemyRecoveryState state, Vector3 currentPos)
        {
            if (currentPos.y <= state.lastSamplePosition.y - EnemyRapidFallDeltaPerCheck)
            {
                state.continuousFallSamples = Mathf.Min(state.continuousFallSamples + 1, EnemyRapidFallSamplesRequired + 2);
            }
            else if (currentPos.y >= state.lastSamplePosition.y - 0.25f)
            {
                state.continuousFallSamples = 0;
            }
        }

        private bool ShouldRecoverFallingEnemy(EnemyRecoveryState state, Vector3 currentPos, CharacterMainControl player)
        {
            if (state.continuousFallSamples < EnemyRapidFallSamplesRequired)
            {
                return false;
            }

            Vector3 groundAlignedPos;
            bool hasNearbyGround = TryResolveGroundAlignedPosition(
                currentPos,
                EnemyGroundProbeDistance,
                EnemyNavMeshProbeDistance,
                out groundAlignedPos);

            if (!hasNearbyGround)
            {
                return true;
            }

            if (groundAlignedPos.y - currentPos.y >= EnemyBelowGroundThreshold * 2f)
            {
                return true;
            }

            if (player == null)
            {
                return false;
            }

            return player.transform.position.y - currentPos.y >= EnemyVoidRelativePlayerYThreshold;
        }

        private bool ShouldRecoverStationaryEnemy(EnemyRecoveryState state, Vector3 currentPos, CharacterMainControl player)
        {
            Vector3 groundAlignedPos;
            bool hasNearbyGround = TryResolveGroundAlignedPosition(
                currentPos,
                EnemyGroundProbeDistance,
                EnemyNavMeshProbeDistance,
                out groundAlignedPos);

            if (hasNearbyGround && groundAlignedPos.y - currentPos.y >= EnemyBelowGroundThreshold)
            {
                return true;
            }

            if (hasNearbyGround)
            {
                return false;
            }

            if (state != null &&
                state.hasExcludedAnchorPosition &&
                state.excludedAnchorPosition.y - currentPos.y >= EnemyVoidRelativeAnchorYThreshold)
            {
                return true;
            }

            if (player == null)
            {
                return false;
            }

            // 仅当玩家明显更高，且敌人曾经出现过连续下坠趋势时，才把“无地面采样”视为掉出有效区域。
            return player.transform.position.y - currentPos.y >= EnemyBelowPlayerThreshold &&
                   state != null &&
                   state.continuousFallSamples > 0;
        }

        private bool TryRecoverEnemyToNearestSpawnPoint(
            CharacterMainControl enemy,
            EnemyRecoveryState state,
            CharacterMainControl player,
            string reason,
            out Vector3 recoveredPos)
        {
            recoveredPos = Vector3.zero;

            try
            {
                Vector3 currentPos = enemy.transform.position;
                float preservedCurrentHealth = 0f;
                bool hasPreservedCurrentHealth = false;
                try
                {
                    if (enemy.Health != null && !enemy.Health.IsDead)
                    {
                        preservedCurrentHealth = enemy.Health.CurrentHealth;
                        hasPreservedCurrentHealth = true;
                    }
                }
                catch {}

                Vector3 targetPos;
                if (!TryGetNearestAlternateSpawnPoint(currentPos, state, player, out targetPos))
                {
                    DevLog("[EnemyRecovery] [WARNING] No valid recovery spawn found for " + enemy.name + " reason=" + reason);
                    return false;
                }

                try
                {
                    enemy.SetPosition(targetPos);
                }
                catch (Exception setPositionEx)
                {
                    DevLog("[EnemyRecovery] [WARNING] SetPosition 恢复敌人失败，改用 transform.position: " + setPositionEx.Message);
                    enemy.transform.position = targetPos;
                }

                try
                {
                    Rigidbody rb = enemy.GetComponent<Rigidbody>();
                    if (rb == null)
                    {
                        rb = enemy.GetComponentInChildren<Rigidbody>();
                    }

                    if (rb != null)
                    {
                        if (!rb.isKinematic)
                        {
                            rb.velocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }
                    }
                }
                catch (Exception rigidbodyEx)
                {
                    DevLog("[EnemyRecovery] [WARNING] 重置敌人物理状态失败: " + rigidbodyEx.Message);
                }

                RestoreRecoveredEnemyAggro(enemy, player);
                RestoreEnemyHealthAfterRecovery(enemy, preservedCurrentHealth, hasPreservedCurrentHealth);

                state.excludedAnchorPosition = targetPos;
                state.hasExcludedAnchorPosition = true;

                recoveredPos = targetPos;

                DevLog("[EnemyRecovery] Recovered " + enemy.name + " reason=" + reason + " from " + currentPos + " to " + targetPos);
                return true;
            }
            catch (Exception e)
            {
                DevLog("[EnemyRecovery] [ERROR] TryRecoverEnemyToNearestSpawnPoint failed: " + e.Message);
                return false;
            }
        }

        private void RestoreEnemyHealthAfterRecovery(CharacterMainControl enemy, float preservedCurrentHealth, bool hasPreservedCurrentHealth)
        {
            if (!hasPreservedCurrentHealth || enemy == null)
            {
                return;
            }

            try
            {
                Health health = enemy.Health;
                if (health == null || health.IsDead)
                {
                    return;
                }

                float clampedHealth = Mathf.Clamp(preservedCurrentHealth, 1f, health.MaxHealth);
                if (health.CurrentHealth > clampedHealth + 0.01f)
                {
                    health.SetHealth(clampedHealth);
                    DevLog("[EnemyRecovery] Preserved damaged health after recovery for " + enemy.name + ": " + clampedHealth);
                }
            }
            catch (Exception e)
            {
                DevLog("[EnemyRecovery] [WARNING] RestoreEnemyHealthAfterRecovery failed: " + e.Message);
            }
        }

        private void RestoreRecoveredEnemyAggro(CharacterMainControl enemy, CharacterMainControl player)
        {
            if (modeEActive || enemy == null || player == null || player.mainDamageReceiver == null)
            {
                return;
            }

            try
            {
                if (modeFActive)
                {
                    ApplyModeFPressureToBoss(enemy);
                    return;
                }

                AICharacterController ai = enemy.GetComponentInChildren<AICharacterController>();
                if (ai == null)
                {
                    return;
                }

                ai.forceTracePlayerDistance = Mathf.Max(ai.forceTracePlayerDistance, 500f);
                ai.searchedEnemy = player.mainDamageReceiver;
                ai.SetTarget(player.mainDamageReceiver.transform);
                ai.SetNoticedToTarget(player.mainDamageReceiver);
                ai.noticed = true;
            }
            catch (Exception aggroEx)
            {
                DevLog("[EnemyRecovery] [WARNING] 恢复敌人仇恨失败: " + aggroEx.Message);
            }
        }

        private bool TryGetNearestAlternateSpawnPoint(
            Vector3 currentPos,
            EnemyRecoveryState state,
            CharacterMainControl player,
            out Vector3 targetPos)
        {
            targetPos = Vector3.zero;

            enemyRecoverySpawnCandidates.Clear();
            CollectPrimaryRecoverySpawnCandidates(player);

            if (TrySelectNearestRecoverySpawnPoint(currentPos, state, out targetPos))
            {
                return true;
            }

            if (player != null)
            {
                AppendRecoverySpawnCandidates(GenerateFallbackSpawnPointsAroundPlayer(player.transform.position));
                if (TrySelectNearestRecoverySpawnPoint(currentPos, state, out targetPos))
                {
                    return true;
                }
            }

            return false;
        }

        private void CollectPrimaryRecoverySpawnCandidates(CharacterMainControl player)
        {
            enemyRecoverySpawnCandidatesArePrevalidated = false;

            if (IsZombieModeActive)
            {
                AppendZombieModeRecoverySpawnCandidates();
            }

            if ((modeEActive || modeFActive) && modeESpawnAllocation != null && modeESpawnAllocation.Count > 0)
            {
                AppendModeERecoverySpawnCandidates();
            }

            if (enemyRecoverySpawnCandidates.Count > 0)
            {
                return;
            }

            Vector3[] sceneSpawnPoints = GetCurrentSceneSpawnPoints();
            if ((sceneSpawnPoints == null || sceneSpawnPoints.Length == 0) &&
                DemoChallengeSpawnPoints != null &&
                DemoChallengeSpawnPoints.Length > 0)
            {
                sceneSpawnPoints = DemoChallengeSpawnPoints;
            }

            AppendRecoverySpawnCandidates(sceneSpawnPoints);

            if (enemyRecoverySpawnCandidates.Count == 0 && player != null)
            {
                AppendRecoverySpawnCandidates(GenerateFallbackSpawnPointsAroundPlayer(player.transform.position));
            }
        }

        private void AppendModeERecoverySpawnCandidates()
        {
            Vector3[] sourcePoints = GetModeEFlattenedSpawnPoints();
            if (sourcePoints == null || sourcePoints.Length == 0)
            {
                return;
            }

            if (!object.ReferenceEquals(enemyRecoveryModeEValidatedSourcePoints, sourcePoints))
            {
                enemyRecoveryModeEValidatedSpawnCandidates.Clear();
                enemyRecoveryModeEValidatedRawCandidates.Clear();
                enemyRecoveryModeEValidatedSourcePoints = sourcePoints;

                for (int i = 0; i < sourcePoints.Length; i++)
                {
                    Vector3 validatedPos;
                    if (!TryResolveGroundAlignedPosition(sourcePoints[i], 12f, EnemyNavMeshProbeDistance, out validatedPos))
                    {
                        continue;
                    }

                    enemyRecoveryModeEValidatedRawCandidates.Add(sourcePoints[i]);
                    enemyRecoveryModeEValidatedSpawnCandidates.Add(validatedPos);
                }
            }

            if (enemyRecoveryModeEValidatedSpawnCandidates.Count <= 0)
            {
                AppendRecoverySpawnCandidates(sourcePoints);
                return;
            }

            bool canUsePrevalidatedSelection = enemyRecoverySpawnCandidates.Count == 0;
            for (int i = 0; i < enemyRecoveryModeEValidatedRawCandidates.Count; i++)
            {
                enemyRecoverySpawnCandidates.Add(enemyRecoveryModeEValidatedRawCandidates[i]);
            }

            enemyRecoverySpawnCandidatesArePrevalidated =
                canUsePrevalidatedSelection &&
                enemyRecoverySpawnCandidates.Count == enemyRecoveryModeEValidatedSpawnCandidates.Count;
        }

        private void AppendZombieModeRecoverySpawnCandidates()
        {
            if (zombieModeRunState == null)
            {
                return;
            }

            List<ZombieModeSpawnPoint> effectivePoints = zombieModeRunState.EffectiveSpawnPoints;
            if (effectivePoints != null && effectivePoints.Count > 0)
            {
                for (int i = 0; i < effectivePoints.Count; i++)
                {
                    enemyRecoverySpawnCandidates.Add(effectivePoints[i].Position);
                }
                enemyRecoverySpawnCandidatesArePrevalidated = false;
                return;
            }

            List<ZombieModeSpawnPoint> spawnPoints = zombieModeRunState.SpawnPoints;
            if (spawnPoints == null)
            {
                return;
            }

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                enemyRecoverySpawnCandidates.Add(spawnPoints[i].Position);
            }

            enemyRecoverySpawnCandidatesArePrevalidated = false;
        }

        private void AppendRecoverySpawnCandidates(IEnumerable<Vector3> candidates)
        {
            if (candidates == null)
            {
                return;
            }

            foreach (Vector3 candidate in candidates)
            {
                enemyRecoverySpawnCandidates.Add(candidate);
            }

            enemyRecoverySpawnCandidatesArePrevalidated = false;
        }

        private bool TrySelectNearestRecoverySpawnPoint(
            Vector3 currentPos,
            EnemyRecoveryState state,
            out Vector3 targetPos)
        {
            targetPos = Vector3.zero;

            float bestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < enemyRecoverySpawnCandidates.Count; i++)
            {
                Vector3 candidate = enemyRecoverySpawnCandidates[i];

                if (GetSqrDistance(candidate, currentPos) <= EnemyCurrentPointExclusionRadius * EnemyCurrentPointExclusionRadius)
                {
                    continue;
                }

                if (state.hasExcludedAnchorPosition &&
                    GetSqrDistance(candidate, state.excludedAnchorPosition) <= EnemySpawnPointExclusionRadius * EnemySpawnPointExclusionRadius)
                {
                    continue;
                }

                Vector3 validatedPos;
                if (enemyRecoverySpawnCandidatesArePrevalidated)
                {
                    if (i >= enemyRecoveryModeEValidatedSpawnCandidates.Count)
                    {
                        continue;
                    }

                    validatedPos = enemyRecoveryModeEValidatedSpawnCandidates[i];
                }
                else if (!TryResolveGroundAlignedPosition(candidate, 12f, EnemyNavMeshProbeDistance, out validatedPos))
                {
                    continue;
                }

                float distance = GetSqrDistance(validatedPos, currentPos);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    targetPos = validatedPos;
                    found = true;
                }
            }

            return found;
        }

        private bool TryResolveGroundAlignedPosition(
            Vector3 rawPosition,
            float rayDistance,
            float navMeshDistance,
            out Vector3 alignedPosition)
        {
            alignedPosition = Vector3.zero;

            try
            {
                LayerMask groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                RaycastHit hit;
                Vector3 origin = rawPosition + Vector3.up * EnemyGroundProbeHeight;

                if (Physics.Raycast(origin, Vector3.down, out hit, rayDistance, groundMask, QueryTriggerInteraction.Ignore))
                {
                    alignedPosition = new Vector3(rawPosition.x, hit.point.y + EnemyGroundLiftOffset, rawPosition.z);
                    return true;
                }

                NavMeshHit navHit;
                if (NavMesh.SamplePosition(rawPosition, out navHit, navMeshDistance, NavMesh.AllAreas))
                {
                    alignedPosition = navHit.position + Vector3.up * EnemyGroundLiftOffset;
                    return true;
                }
            }
            catch (Exception e)
            {
                DevLog("[EnemyRecovery] [WARNING] TryResolveGroundAlignedPosition failed: " + e.Message);
            }

            return false;
        }

        private void CleanupEnemyRecoveryStates()
        {
            enemyRecoveryRemovalBuffer.Clear();

            foreach (KeyValuePair<CharacterMainControl, EnemyRecoveryState> pair in enemyRecoveryStates)
            {
                CharacterMainControl enemy = pair.Key;
                if (enemy == null || !enemyRecoverySeenEnemies.Contains(enemy))
                {
                    enemyRecoveryRemovalBuffer.Add(enemy);
                }
            }

            for (int i = 0; i < enemyRecoveryRemovalBuffer.Count; i++)
            {
                enemyRecoveryStates.Remove(enemyRecoveryRemovalBuffer[i]);
            }
        }

        private static float GetHorizontalSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static float GetSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            float dz = a.z - b.z;
            return dx * dx + dy * dy + dz * dz;
        }
    }
}
