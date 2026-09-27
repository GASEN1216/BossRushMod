using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace BossRush
{
    internal sealed class EnemyRecoveryMonitor
    {
        internal delegate void AppendRecoveryCandidates(List<Vector3> candidates, ref bool prevalidated);
        internal delegate bool TryRecoveryPosition(out Vector3 position);
        private Func<bool> getModeDActive;
        private Func<bool> getModeEActive;
        private Func<bool> getModeFActive;
        private Func<bool> getArenaActive;
        private Func<bool> getZombieActive;
        private Func<bool> getModeGRunActive;
        private Func<List<CharacterMainControl>> getModeDEnemies;
        private Func<List<CharacterMainControl>> getModeEEnemies;
        private Func<List<CharacterMainControl>> getModeFBosses;
        private Func<List<CharacterMainControl>> getModeGBosses;
        private Action<CharacterMainControl> MonitorZombieModeEnemyRecovery;
        private Action<CharacterMainControl> MonitorNormalBossRushRecovery;
        private Func<Dictionary<Teams, List<Vector3>>> getSpawnAllocation;
        private Func<Vector3[]> GetModeEFlattenedSpawnPoints;
        private Func<Vector3[]> GetCurrentSceneSpawnPoints;
        private Func<Vector3, Vector3[]> GenerateFallbackSpawnPointsAroundPlayer;
        private AppendRecoveryCandidates appendZombieRecoveryCandidates;
        private TryRecoveryPosition TryGetZombieModeReliableSpawnPosition;
        private Func<Component, bool> isDistantRecoveryBoss;
        private Func<float> getDistantRecoveryDistance;
        private Func<float> getDistantRecoveryDelay;
        private Func<GameObject, Component, AICharacterController> GetZombieModeEnemyAI;
        private Action<CharacterMainControl> ApplyModeFPressureToBoss;
        private bool modeDActive { get { return getModeDActive(); } }
        private bool modeEActive { get { return getModeEActive(); } }
        private bool modeFActive { get { return getModeFActive(); } }
        private bool IsActive { get { return getArenaActive(); } }
        private bool IsZombieModeActive { get { return getZombieActive(); } }
        private List<CharacterMainControl> modeDCurrentWaveEnemies { get { return getModeDEnemies(); } }
        private List<CharacterMainControl> modeEAliveEnemies { get { return getModeEEnemies(); } }
        private Dictionary<Teams, List<Vector3>> modeESpawnAllocation { get { return getSpawnAllocation(); } }

        internal void BindModeQueries(
            Func<bool> getModeDActive,
            Func<bool> getModeEActive,
            Func<bool> getModeFActive,
            Func<bool> getArenaActive,
            Func<bool> getZombieActive,
            Func<bool> getModeGRunActive)
        {
            this.getModeDActive = getModeDActive;
            this.getModeEActive = getModeEActive;
            this.getModeFActive = getModeFActive;
            this.getArenaActive = getArenaActive;
            this.getZombieActive = getZombieActive;
            this.getModeGRunActive = getModeGRunActive;
        }

        internal void BindTrackedEnemies(
            Func<List<CharacterMainControl>> getModeDEnemies,
            Func<List<CharacterMainControl>> getModeEEnemies,
            Func<List<CharacterMainControl>> getModeFBosses,
            Func<List<CharacterMainControl>> getModeGBosses,
            Action<CharacterMainControl> MonitorZombieModeEnemyRecovery,
            Action<CharacterMainControl> MonitorNormalBossRushRecovery)
        {
            this.getModeDEnemies = getModeDEnemies;
            this.getModeEEnemies = getModeEEnemies;
            this.getModeFBosses = getModeFBosses;
            this.getModeGBosses = getModeGBosses;
            this.MonitorZombieModeEnemyRecovery = MonitorZombieModeEnemyRecovery;
            this.MonitorNormalBossRushRecovery = MonitorNormalBossRushRecovery;
        }

        internal void BindSpawnPositions(
            Func<Dictionary<Teams, List<Vector3>>> getSpawnAllocation,
            Func<Vector3[]> GetModeEFlattenedSpawnPoints,
            Func<Vector3[]> GetCurrentSceneSpawnPoints,
            Func<Vector3, Vector3[]> GenerateFallbackSpawnPointsAroundPlayer,
            AppendRecoveryCandidates appendZombieRecoveryCandidates,
            TryRecoveryPosition TryGetZombieModeReliableSpawnPosition)
        {
            this.getSpawnAllocation = getSpawnAllocation;
            this.GetModeEFlattenedSpawnPoints = GetModeEFlattenedSpawnPoints;
            this.GetCurrentSceneSpawnPoints = GetCurrentSceneSpawnPoints;
            this.GenerateFallbackSpawnPointsAroundPlayer = GenerateFallbackSpawnPointsAroundPlayer;
            this.appendZombieRecoveryCandidates = appendZombieRecoveryCandidates;
            this.TryGetZombieModeReliableSpawnPosition = TryGetZombieModeReliableSpawnPosition;
        }

        internal void BindRecoveryPolicies(
            Func<Component, bool> isDistantRecoveryBoss,
            Func<float> getDistantRecoveryDistance,
            Func<float> getDistantRecoveryDelay,
            Func<GameObject, Component, AICharacterController> GetZombieModeEnemyAI,
            Action<CharacterMainControl> ApplyModeFPressureToBoss)
        {
            this.isDistantRecoveryBoss = isDistantRecoveryBoss;
            this.getDistantRecoveryDistance = getDistantRecoveryDistance;
            this.getDistantRecoveryDelay = getDistantRecoveryDelay;
            this.GetZombieModeEnemyAI = GetZombieModeEnemyAI;
            this.ApplyModeFPressureToBoss = ApplyModeFPressureToBoss;
        }

        private void AppendZombieModeRecoverySpawnCandidates()
        {
            appendZombieRecoveryCandidates(enemyRecoverySpawnCandidates, ref enemyRecoverySpawnCandidatesArePrevalidated);
        }


        private sealed class EnemyRecoveryState
        {
            public Vector3 lastSamplePosition;
            public float lastMovedTime;
            public float lastRecoveryTime;
            public float farFromPlayerSince;
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
        private const string ZombieModeDistantRecoveryReason = "distant";

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

        internal void ClearEnemyRecoveryMonitorState()
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

        internal void RegisterEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchorPosition)
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
                        ModBehaviour.DevLog("[EnemyRecovery] [WARNING] RegisterEnemyRecoveryAnchor 无法读取敌人位置: " + positionEx.Message);
                    }

                    state = new EnemyRecoveryState
                    {
                        lastSamplePosition = currentPos,
                        lastMovedTime = Time.time,
                        lastRecoveryTime = -EnemyRecoveryCooldown,
                        farFromPlayerSince = -1f,
                        continuousFallSamples = 0
                    };

                    enemyRecoveryStates[enemy] = state;
                }

                state.excludedAnchorPosition = anchorPosition;
                state.hasExcludedAnchorPosition = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EnemyRecovery] [ERROR] RegisterEnemyRecoveryAnchor failed: " + e.Message);
            }
        }

        internal void UnregisterEnemyRecovery(CharacterMainControl enemy)
        {
            if (enemy == null)
            {
                return;
            }

            enemyRecoveryStates.Remove(enemy);
        }

        internal void UpdateEnemyRecoveryMonitor()
        {
            try
            {
                // Mode G 门控（加法分支）：no-throw 读取 Mode G 运行状态，
                // 未运行时为 false，活跃判据与分支链其他模式顺序不变。
                bool modeGRunActive = false;
                try
                {
                    modeGRunActive = getModeGRunActive();
                }
                catch
                {
                    modeGRunActive = false;
                }

                if (!modeDActive && !modeEActive && !modeFActive && !IsActive && !IsZombieModeActive && !modeGRunActive)
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
                    MonitorEnemyRecoveryList(getModeFBosses(), player);
                }
                else if (IsZombieModeActive)
                {
                    MonitorZombieModeEnemyRecovery(player);
                }
                else if (modeGRunActive)
                {
                    // Mode G 私有 Boss 列表（Felix 提供的只读访问器，no-throw）
                    List<CharacterMainControl> modeGBosses = null;
                    try
                    {
                        modeGBosses = getModeGBosses();
                    }
                    catch
                    {
                        modeGBosses = null;
                    }

                    if (modeGBosses != null)
                    {
                        MonitorEnemyRecoveryList(modeGBosses, player);
                    }
                }
                else
                {
                    MonitorNormalBossRushRecovery(player);
                }

                CleanupEnemyRecoveryStates();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EnemyRecovery] [ERROR] UpdateEnemyRecoveryMonitor failed: " + e.Message);
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


        internal void MonitorEnemyRecovery(CharacterMainControl enemy, CharacterMainControl player, Component zombieMarker = null)
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
                        farFromPlayerSince = -1f,
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
                    bool distantZombie = !fallingOut &&
                                          !stuckUnderground &&
                                          ShouldRecoverDistantZombie(state, currentPos, player, zombieMarker, now);

                    if (fallingOut || stuckUnderground || distantZombie)
                    {
                        string reason = fallingOut
                            ? "falling"
                            : (stuckUnderground ? "stuck" : ZombieModeDistantRecoveryReason);
                        Vector3 recoveredPos;
                        if (TryRecoverEnemyToNearestSpawnPoint(enemy, state, player, reason, zombieMarker, out recoveredPos))
                        {
                            currentPos = recoveredPos;
                            now = Time.time;
                            state.lastMovedTime = now;
                            state.lastRecoveryTime = now;
                            state.lastSamplePosition = recoveredPos;
                            state.continuousFallSamples = 0;
                            state.farFromPlayerSince = -1f;
                        }
                    }
                }

                state.lastSamplePosition = currentPos;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EnemyRecovery] [ERROR] MonitorEnemyRecovery failed: " + e.Message);
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

        private bool ShouldRecoverDistantZombie(
            EnemyRecoveryState state,
            Vector3 currentPos,
            CharacterMainControl player,
            Component zombieMarker,
            float now)
        {
            if (state == null || player == null || zombieMarker == null || isDistantRecoveryBoss(zombieMarker))
            {
                if (state != null)
                {
                    state.farFromPlayerSince = -1f;
                }
                return false;
            }

            float recoveryDistance = getDistantRecoveryDistance();
            if (GetHorizontalSqrDistance(currentPos, player.transform.position) <= recoveryDistance * recoveryDistance)
            {
                state.farFromPlayerSince = -1f;
                return false;
            }

            if (state.farFromPlayerSince < 0f)
            {
                state.farFromPlayerSince = now;
                return false;
            }

            return now - state.farFromPlayerSince >= getDistantRecoveryDelay();
        }

        private bool TryRecoverEnemyToNearestSpawnPoint(
            CharacterMainControl enemy,
            EnemyRecoveryState state,
            CharacterMainControl player,
            string reason,
            Component zombieMarker,
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

                Vector3 targetPos = Vector3.zero;
                bool recoveredNearPlayer = reason == ZombieModeDistantRecoveryReason &&
                                           player != null &&
                                           TryGetZombieModeReliableSpawnPosition(out targetPos);
                if (!recoveredNearPlayer && !TryGetNearestAlternateSpawnPoint(currentPos, state, player, out targetPos))
                {
                    ModBehaviour.DevLog("[EnemyRecovery] [WARNING] No valid recovery spawn found for " + enemy.name + " reason=" + reason);
                    return false;
                }

                try
                {
                    enemy.SetPosition(targetPos);
                }
                catch (Exception setPositionEx)
                {
                    ModBehaviour.DevLog("[EnemyRecovery] [WARNING] SetPosition 恢复敌人失败，改用 transform.position: " + setPositionEx.Message);
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
                    ModBehaviour.DevLog("[EnemyRecovery] [WARNING] 重置敌人物理状态失败: " + rigidbodyEx.Message);
                }

                RestoreRecoveredEnemyAggro(enemy, player, zombieMarker);
                RestoreEnemyHealthAfterRecovery(enemy, preservedCurrentHealth, hasPreservedCurrentHealth);

                state.excludedAnchorPosition = targetPos;
                state.hasExcludedAnchorPosition = true;

                recoveredPos = targetPos;

                ModBehaviour.DevLog("[EnemyRecovery] Recovered " + enemy.name + " reason=" + reason + " from " + currentPos + " to " + targetPos);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EnemyRecovery] [ERROR] TryRecoverEnemyToNearestSpawnPoint failed: " + e.Message);
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
                    ModBehaviour.DevLog("[EnemyRecovery] Preserved damaged health after recovery for " + enemy.name + ": " + clampedHealth);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EnemyRecovery] [WARNING] RestoreEnemyHealthAfterRecovery failed: " + e.Message);
            }
        }

        private void RestoreRecoveredEnemyAggro(CharacterMainControl enemy, CharacterMainControl player, Component zombieMarker)
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

                AICharacterController ai = null;
                if (zombieMarker != null && zombieMarker.gameObject == enemy.gameObject)
                {
                    ai = GetZombieModeEnemyAI(enemy.gameObject, zombieMarker);
                }

                if (ai == null)
                {
                    ai = enemy.GetComponentInChildren<AICharacterController>();
                }

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
                ModBehaviour.DevLog("[EnemyRecovery] [WARNING] 恢复敌人仇恨失败: " + aggroEx.Message);
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
                ModBehaviour.DevLog("[EnemyRecovery] [WARNING] TryResolveGroundAlignedPosition failed: " + e.Message);
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
        internal void ValidateAndFixBossPosition(CharacterMainControl boss)
        {
            if (boss == null) return;

            try
            {
                Vector3 currentPos = boss.transform.position;

                bool needsRecovery = false;
                string reason = null;

                Vector3 groundAlignedPos;
                if (TryResolveGroundAlignedPosition(currentPos, 8f, 5f, out groundAlignedPos))
                {
                    if (groundAlignedPos.y - currentPos.y >= 0.75f)
                    {
                        needsRecovery = true;
                        reason = "spawn_below_ground";
                    }
                }
                else
                {
                    EnemyRecoveryState recoveryState;
                    if (enemyRecoveryStates.TryGetValue(boss, out recoveryState) &&
                        recoveryState.hasExcludedAnchorPosition &&
                        recoveryState.excludedAnchorPosition.y - currentPos.y >= 6f)
                    {
                        needsRecovery = true;
                        reason = "spawn_void";
                    }
                }

                if (!needsRecovery)
                {
                    return;
                }

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                {
                    return;
                }

                EnemyRecoveryState state;
                if (!enemyRecoveryStates.TryGetValue(boss, out state))
                {
                    state = new EnemyRecoveryState
                    {
                        lastSamplePosition = currentPos,
                        lastMovedTime = Time.time,
                        lastRecoveryTime = -4f,
                        excludedAnchorPosition = currentPos,
                        hasExcludedAnchorPosition = true,
                        continuousFallSamples = 0
                    };
                }

                Vector3 recoveredPos;
                if (TryRecoverEnemyToNearestSpawnPoint(boss, state, main, reason, null, out recoveredPos))
                {
                    state.lastMovedTime = Time.time;
                    state.lastRecoveryTime = Time.time;
                    state.lastSamplePosition = recoveredPos;
                    enemyRecoveryStates[boss] = state;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ValidateAndFixBossPosition 异常: " + e.Message);
            }
        }

        internal IEnumerator DelayedBossPositionValidation(CharacterMainControl boss, float delay)
        {
            if (boss == null) yield break;

            yield return new WaitForSeconds(delay);

            if (boss != null && boss.gameObject != null)
            {
                ValidateAndFixBossPosition(boss);
            }
        }
    }
}
