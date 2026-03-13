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

        private float enemyRecoveryCheckTimer = 0f;

        private void ClearEnemyRecoveryMonitorState()
        {
            enemyRecoveryCheckTimer = 0f;
            enemyRecoveryStates.Clear();
            enemyRecoverySeenEnemies.Clear();
            enemyRecoveryRemovalBuffer.Clear();
            enemyRecoverySpawnCandidates.Clear();
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
                    catch {}

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
                if (!modeDActive && !modeEActive && !IsActive)
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
                    CharacterMainControl enemy = null;
                    try
                    {
                        enemy = currentWaveBosses[i] as CharacterMainControl;
                    }
                    catch {}

                    MonitorEnemyRecovery(enemy, player);
                }

                return;
            }

            CharacterMainControl singleBoss = null;
            try
            {
                singleBoss = currentBoss as CharacterMainControl;
            }
            catch {}

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
                                            ShouldRecoverStationaryEnemy(currentPos, player);

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

        private bool ShouldRecoverStationaryEnemy(Vector3 currentPos, CharacterMainControl player)
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

            if (player == null)
            {
                return !hasNearbyGround;
            }

            if (player.transform.position.y - currentPos.y < EnemyBelowPlayerThreshold)
            {
                return false;
            }

            return !hasNearbyGround;
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
                catch
                {
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
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }
                catch {}

                RestoreRecoveredEnemyAggro(enemy, player);

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

        private void RestoreRecoveredEnemyAggro(CharacterMainControl enemy, CharacterMainControl player)
        {
            if (modeEActive || enemy == null || player == null || player.mainDamageReceiver == null)
            {
                return;
            }

            try
            {
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
            catch {}
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
            if (modeEActive && modeESpawnAllocation != null && modeESpawnAllocation.Count > 0)
            {
                foreach (KeyValuePair<Teams, List<Vector3>> pair in modeESpawnAllocation)
                {
                    AppendRecoverySpawnCandidates(pair.Value);
                }
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

                if (GetHorizontalSqrDistance(candidate, currentPos) <= EnemyCurrentPointExclusionRadius * EnemyCurrentPointExclusionRadius)
                {
                    continue;
                }

                if (state.hasExcludedAnchorPosition &&
                    GetHorizontalSqrDistance(candidate, state.excludedAnchorPosition) <= EnemySpawnPointExclusionRadius * EnemySpawnPointExclusionRadius)
                {
                    continue;
                }

                Vector3 validatedPos;
                if (!TryResolveGroundAlignedPosition(candidate, 12f, EnemyNavMeshProbeDistance, out validatedPos))
                {
                    continue;
                }

                float distance = GetHorizontalSqrDistance(validatedPos, currentPos);
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
    }
}
