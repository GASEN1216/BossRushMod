using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string ZOMBIE_MODE_NORMAL_PRESET_NAME = "Cname_Zombie";
        private readonly Dictionary<long, List<ZombieModeSpawnPoint>> zombieModeSpawnPointDedupGrid =
            new Dictionary<long, List<ZombieModeSpawnPoint>>();
        private float zombieModeSpawnPointDedupCellSize = 1f;

        // 注：本模式之前自维护的"丧尸预设缓存字段 + Resources.FindObjectsOfTypeAll 查找方法"
        // 已删除（审查 §1.1）。SpawnEnemyCore 通过共享的 cachedCharacterPresets 自动 fallback；
        // 入口处调用 EnsureCharacterPresetsCacheReady() 确保字典就绪。

        private bool CollectZombieModeSpawnPoints(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            zombieModeRunState.SpawnPoints.Clear();
            ResetZombieModeSpawnPointDedupGrid();
            if (zombieModeRunState.MapProfile != null)
            {
                AddZombieModeSpawnPointArray(zombieModeRunState.MapProfile.StaticSpawnPoints, false);
            }

            if (zombieModeRunState.SpawnPoints.Count <= 0)
            {
                TryPopulateZombieModeSpawnPointsFromCachedOriginalSpawnerPositions();
            }

            DevLog("[ZombieMode] 收集刷怪点: " + zombieModeRunState.SpawnPoints.Count);
            zombieModeRunState.EffectiveSpawnPoints.Clear();
            for (int i = 0; i < zombieModeRunState.SpawnPoints.Count; i++)
            {
                zombieModeRunState.EffectiveSpawnPoints.Add(zombieModeRunState.SpawnPoints[i]);
            }
            return zombieModeRunState.SpawnPoints.Count > 0;
        }

        private void ResetZombieModeSpawnPointDedupGrid()
        {
            zombieModeSpawnPointDedupGrid.Clear();
            zombieModeSpawnPointDedupCellSize = Mathf.Max(0.01f, ZombieModeTuning.SpawnPointDuplicateDistance);
        }

        private void TryPopulateZombieModeSpawnPointsFromCachedOriginalSpawnerPositions()
        {
            if (modeECachedSpawnerPositions == null || modeECachedSpawnerPositions.Length <= 0)
            {
                return;
            }

            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!string.Equals(modeECachedSpawnerSceneName, currentSceneName, System.StringComparison.Ordinal))
            {
                return;
            }

            AddZombieModeSpawnPointArray(modeECachedSpawnerPositions, false);
        }

        private void AddZombieModeSpawnPointArray(Vector3[] points, bool virtualPoint)
        {
            if (points == null)
            {
                return;
            }

            for (int i = 0; i < points.Length; i++)
            {
                AddZombieModeSpawnPoint(points[i], virtualPoint);
            }
        }

        private void AddZombieModeSpawnPoint(Vector3 position, bool virtualPoint)
        {
            Vector3 snapped;
            if (!TryResolveZombieModeSpawnPoint(position, virtualPoint, out snapped))
            {
                return;
            }

            if (HasZombieModeDuplicateSpawnPoint(snapped))
            {
                return;
            }

            ZombieModeSpawnPoint spawnPoint = new ZombieModeSpawnPoint(snapped, virtualPoint);
            zombieModeRunState.SpawnPoints.Add(spawnPoint);
            RegisterZombieModeSpawnPointDedupCell(spawnPoint);
        }

        private bool HasZombieModeDuplicateSpawnPoint(Vector3 snapped)
        {
            float duplicateDistanceSqr = ZombieModeTuning.SpawnPointDuplicateDistance * ZombieModeTuning.SpawnPointDuplicateDistance;
            int cellX = Mathf.FloorToInt(snapped.x / zombieModeSpawnPointDedupCellSize);
            int cellZ = Mathf.FloorToInt(snapped.z / zombieModeSpawnPointDedupCellSize);

            for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                for (int zOffset = -1; zOffset <= 1; zOffset++)
                {
                    List<ZombieModeSpawnPoint> candidates;
                    if (!zombieModeSpawnPointDedupGrid.TryGetValue(GetZombieModeSpawnPointDedupCellKey(cellX + xOffset, cellZ + zOffset), out candidates))
                    {
                        continue;
                    }

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        Vector3 delta = candidates[i].Position - snapped;
                        delta.y = 0f;
                        if (delta.sqrMagnitude < duplicateDistanceSqr)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void RegisterZombieModeSpawnPointDedupCell(ZombieModeSpawnPoint spawnPoint)
        {
            int cellX = Mathf.FloorToInt(spawnPoint.Position.x / zombieModeSpawnPointDedupCellSize);
            int cellZ = Mathf.FloorToInt(spawnPoint.Position.z / zombieModeSpawnPointDedupCellSize);
            long key = GetZombieModeSpawnPointDedupCellKey(cellX, cellZ);
            List<ZombieModeSpawnPoint> cellPoints;
            if (!zombieModeSpawnPointDedupGrid.TryGetValue(key, out cellPoints))
            {
                cellPoints = new List<ZombieModeSpawnPoint>();
                zombieModeSpawnPointDedupGrid[key] = cellPoints;
            }

            cellPoints.Add(spawnPoint);
        }

        private static long GetZombieModeSpawnPointDedupCellKey(int cellX, int cellZ)
        {
            return ((long)cellX << 32) ^ (uint)cellZ;
        }

        private Vector3 GetZombieModeSpawnPosition()
        {
            Vector3 storedPoint;
            if (TryGetNearestZombieModeMapSpawnPositionToPlayer(out storedPoint))
            {
                return storedPoint;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            if (main != null)
            {
                Vector3 nearby;
                if (TryFindZombieModeVirtualSpawnAroundPlayer(main.transform.position, out nearby))
                {
                    return nearby;
                }
            }

            if (zombieModeRunState.SpawnPoints.Count <= 0)
            {
                return main != null ? main.transform.position : Vector3.zero;
            }

            Vector3 playerPos = main != null ? main.transform.position : Vector3.zero;
            float minPlayerDistance = ZombieModeTuning.SpawnPointMinPlayerDistance;
            float minPlayerDistanceSqr = minPlayerDistance * minPlayerDistance;
            float bestScore = float.MinValue;
            Vector3 best = zombieModeRunState.SpawnPoints[0].Position;
            for (int i = 0; i < zombieModeRunState.SpawnPoints.Count; i++)
            {
                Vector3 point = zombieModeRunState.SpawnPoints[i].Position;
                Vector3 delta = point - playerPos;
                delta.y = 0f;
                float distanceSqr = delta.sqrMagnitude;
                if (distanceSqr < minPlayerDistanceSqr)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSqr);
                float score = distance > 80f ? 80f - (distance - 80f) : distance;
                score += Random.Range(0f, 8f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = point;
                }
            }

            if (bestScore == float.MinValue && main != null)
            {
                Vector3 fallback;
                if (TryFindZombieModeVirtualSpawnAroundPlayer(playerPos, out fallback))
                {
                    return fallback;
                }
            }

            return best;
        }

        private bool TryGetNearestZombieModeMapSpawnPositionToPlayer(out Vector3 position)
        {
            position = Vector3.zero;
            List<ZombieModeSpawnPoint> points = zombieModeRunState.EffectiveSpawnPoints.Count > 0
                ? zombieModeRunState.EffectiveSpawnPoints
                : zombieModeRunState.SpawnPoints;
            if (points == null || points.Count <= 0)
            {
                return false;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            Vector3 playerPos = main != null ? main.transform.position : Vector3.zero;
            float minDistanceSqr = ZombieModeTuning.SpawnPointMinPlayerDistance * ZombieModeTuning.SpawnPointMinPlayerDistance;
            float bestDistanceSqr = float.MaxValue;
            int bestIndex = -1;
            int startIndex = Mathf.Abs(zombieModeRunState.NextSpawnPointIndex) % points.Count;
            for (int offset = 0; offset < points.Count; offset++)
            {
                int index = (startIndex + offset) % points.Count;
                Vector3 point = points[index].Position;
                Vector3 delta = point - playerPos;
                delta.y = 0f;
                float distanceSqr = main != null ? delta.sqrMagnitude : offset;
                if (main != null && distanceSqr < minDistanceSqr)
                {
                    continue;
                }

                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestIndex = index;
                }
            }

            if (bestIndex < 0 && main != null)
            {
                bestDistanceSqr = float.MaxValue;
                for (int offset = 0; offset < points.Count; offset++)
                {
                    int index = (startIndex + offset) % points.Count;
                    Vector3 point = points[index].Position;
                    Vector3 delta = point - playerPos;
                    delta.y = 0f;
                    float distanceSqr = delta.sqrMagnitude;
                    if (distanceSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = distanceSqr;
                        bestIndex = index;
                    }
                }
            }

            if (bestIndex < 0)
            {
                return false;
            }

            position = points[bestIndex].Position;
            zombieModeRunState.NextSpawnPointIndex = (bestIndex + 1) % points.Count;
            return true;
        }

        private bool TryFindZombieModeVirtualSpawnAroundPlayer(Vector3 playerPos, out Vector3 resolved)
        {
            return SpawnPositionHelper.TryFindAroundPlayer(
                playerPos,
                ringCount: 12,
                radius: 24f,
                resolved: out resolved,
                liftOffset: ZombieModeTuning.NavMeshLiftOffset,
                minPlayerDistance: ZombieModeTuning.SpawnPointMinPlayerDistance,
                navMeshSampleRadius: ZombieModeTuning.NavMeshVirtualSpawnRadius);
        }

        private bool TryResolveZombieModeSpawnPoint(Vector3 position, bool virtualPoint, out Vector3 resolved)
        {
            // 虚拟点（玩家附近回退环）：raw 是几何构造点，需要 NavMesh 优先 + 通过 minPlayerDistance。
            // 预设点（地图配置 / 原 spawner）：raw 已是预设位置，Raycast 优先且不再做 minPlayerDistance 过滤。
            if (virtualPoint)
            {
                if (!SpawnPositionHelper.TrySampleNavMesh(
                        position,
                        out resolved,
                        liftOffset: ZombieModeTuning.NavMeshLiftOffset,
                        navMeshSampleRadius: ZombieModeTuning.NavMeshVirtualSpawnRadius))
                {
                    return false;
                }
                return SpawnPositionHelper.PassesMinPlayerDistance(resolved, ZombieModeTuning.SpawnPointMinPlayerDistance);
            }

            return SpawnPositionHelper.TrySnapToGround(
                position,
                out resolved,
                liftOffset: ZombieModeTuning.NavMeshLiftOffset,
                navMeshSampleRadius: ZombieModeTuning.SpawnPointNavMeshSampleRadius);
        }

        private async UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieAsync(
            int runId,
            Vector3 position,
            ZombieModeEnemyKind forcedEnemyKind = ZombieModeEnemyKind.Normal,
            bool forceEnemyKind = false,
            System.Func<bool> isSpawnPhaseStillAllowed = null)
        {
            while (true)
            {
                if (!IsZombieModeNormalSpawnStillAllowed(runId, isSpawnPhaseStillAllowed))
                {
                    return null;
                }

                if (!await WaitForZombieModeRuntimeResumeAsync(runId))
                {
                    return null;
                }

                if (!IsZombieModeNormalSpawnStillAllowed(runId, isSpawnPhaseStillAllowed))
                {
                    return null;
                }

                if (!TryReserveZombieModeNormalSpawnSlot(runId))
                {
                    return null;
                }

                if (!IsZombieModeNormalSpawnStillAllowed(runId, isSpawnPhaseStillAllowed))
                {
                    ReleaseZombieModeNormalSpawnSlot();
                    return null;
                }

                // 入口确保 cachedCharacterPresets 已构建（避免依赖 Mode D 先初始化，§1.1）。
                EnsureCharacterPresetsCacheReady();

                bool abortedByPause = false;
                UniTaskCompletionSource<CharacterMainControl> tcs = new UniTaskCompletionSource<CharacterMainControl>();
                EnemyPresetInfo info = new EnemyPresetInfo
                {
                    name = ZOMBIE_MODE_NORMAL_PRESET_NAME,
                    displayName = "ZombieMode_NormalZombie",
                    baseHealth = 100f,
                };

                SpawnEnemyCore(
                    info,
                    position,
                    isBoss: false,
                    isActiveCheck: () => IsZombieModeNormalSpawnStillAllowed(runId, isSpawnPhaseStillAllowed),
                    onSpawned: ctx =>
                    {
                        CharacterMainControl zombie = ctx.character;
                        bool phaseStillAllowed = IsZombieModeNormalSpawnStillAllowed(runId, isSpawnPhaseStillAllowed);
                        bool runtimePaused = IsZombieModeRuntimePaused();
                        if (!phaseStillAllowed || runtimePaused)
                        {
                            abortedByPause = phaseStillAllowed && runtimePaused;
                            ReleaseZombieModeNormalSpawnSlot();
                            DestroyZombieModePausedSpawnCandidate(zombie);
                            tcs.TrySetResult(null);
                            return;
                        }

                        ZombieModeEnemyKind enemyKind = forceEnemyKind ? forcedEnemyKind : RollZombieModeEnemyKind();
                        ZombieModeSpecialKind specialKind = enemyKind == ZombieModeEnemyKind.Special
                            ? RollZombieModeSpecialKind()
                            : ZombieModeSpecialKind.None;
                        List<ZombieModeEliteAffix> eliteAffixes = enemyKind == ZombieModeEnemyKind.Elite
                            ? RollZombieModeEliteAffixes()
                            : null;

                        zombie.gameObject.name = "ZombieMode_NormalZombie_Run" + runId;
                        ReleaseZombieModeNormalSpawnSlot();
                        ZombieModeEnemyRuntimeMarker marker = RegisterZombieModeEnemyRuntimeShell(runId, zombie, false, ZombieModeBossKind.Titan, -1, enemyKind, specialKind, eliteAffixes);
                        PrepareZombieModeSpawnedEnemy(zombie, marker, ZombieModeTuning.NormalZombieForceTraceDistance);
                        ApplyZombieModeEnemyTuning(zombie, marker);
                        RegisterEnemyRecoveryAnchor(zombie, ctx.position);
                        zombieModeRunState.LivingZombieCount++;
                        zombieModeRunState.LivingNormalZombieCount++;
                        tcs.TrySetResult(zombie);
                    },
                    onFailed: () =>
                    {
                        ReleaseZombieModeNormalSpawnSlot();
                        tcs.TrySetResult(null);
                    },
                    applyEquipment: false,
                    applyBossMultiplier: false,
                    normalizeDamageMultiplier: false);

                CharacterMainControl result = await tcs.Task;
                if (result != null || !abortedByPause)
                {
                    return result;
                }
            }
        }

        private bool IsZombieModeNormalSpawnStillAllowed(int runId, System.Func<bool> isSpawnPhaseStillAllowed)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            return isSpawnPhaseStillAllowed == null || isSpawnPhaseStillAllowed();
        }

        private bool TryReserveZombieModeNormalSpawnSlot(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            int activeOrPending = zombieModeRunState.LivingNormalZombieCount + zombieModeRunState.PendingNormalZombieSpawns;
            if (activeOrPending >= ZombieModeTuning.MaxNormalZombieCount)
            {
                return false;
            }

            zombieModeRunState.PendingNormalZombieSpawns++;
            return true;
        }

        private void ReleaseZombieModeNormalSpawnSlot()
        {
            zombieModeRunState.PendingNormalZombieSpawns = Mathf.Max(0, zombieModeRunState.PendingNormalZombieSpawns - 1);
        }

        private async UniTask<CharacterMainControl> TrySpawnZombieModeBossAsync(int runId, Vector3 position, ZombieModeBossKind kind)
        {
            while (true)
            {
                if (!IsZombieModeRunValid(runId))
                {
                    return null;
                }

                if (!await WaitForZombieModeRuntimeResumeAsync(runId))
                {
                    return null;
                }

                // 入口确保 cachedCharacterPresets 已构建（§1.1）。
                EnsureCharacterPresetsCacheReady();

                bool abortedByPause = false;
                UniTaskCompletionSource<CharacterMainControl> tcs = new UniTaskCompletionSource<CharacterMainControl>();
                EnemyPresetInfo info = new EnemyPresetInfo
                {
                    name = ZOMBIE_MODE_NORMAL_PRESET_NAME,
                    displayName = "ZombieMode_Boss_" + kind.ToString(),
                    baseHealth = 180f,
                };

                SpawnEnemyCore(
                    info,
                    position,
                    isBoss: true,
                    isActiveCheck: () => IsZombieModeRunValid(runId),
                    onSpawned: ctx =>
                    {
                        CharacterMainControl boss = ctx.character;
                        if (IsZombieModeRuntimePaused())
                        {
                            abortedByPause = true;
                            DestroyZombieModePausedSpawnCandidate(boss);
                            tcs.TrySetResult(null);
                            return;
                        }

                        boss.gameObject.name = "ZombieMode_Boss_" + kind.ToString() + "_Run" + runId;
                        ZombieModeEnemyRuntimeMarker bossMarker = RegisterZombieModeEnemyRuntimeShell(runId, boss, true, kind, GetZombieModeBossPointValue(kind));
                        PrepareZombieModeSpawnedEnemy(boss, bossMarker, 180f);
                        ApplyZombieModeBossTuning(boss, kind, bossMarker);
                        RegisterEnemyRecoveryAnchor(boss, ctx.position);
                        zombieModeRunState.LivingZombieCount++;

                        ZombieModeBossInstance instance = new ZombieModeBossInstance();
                        instance.Character = boss;
                        instance.Kind = kind;
                        instance.Marker = bossMarker;
                        instance.Lifecycle.Alive = true;
                        instance.Lifecycle.LastKnownPosition = boss.transform.position;
                        instance.Lifecycle.LastReachableTime = GetZombieModeRuntimeNow();
                        instance.Lifecycle.LastHurtTime = GetZombieModeRuntimeNow();
                        zombieModeRunState.CurrentWaveBossInstances.Add(instance);
                        RegisterZombieModeBossRuntime(runId, boss, kind);
                        tcs.TrySetResult(boss);
                    },
                    onFailed: () => tcs.TrySetResult(null),
                    applyEquipment: false,
                    applyBossMultiplier: false,
                    skipBossRushLootTracking: true,
                    normalizeDamageMultiplier: false);

                CharacterMainControl result = await tcs.Task;
                if (result != null || !abortedByPause)
                {
                    return result;
                }
            }
        }

        private void DestroyZombieModePausedSpawnCandidate(CharacterMainControl character)
        {
            if (character == null || character.gameObject == null)
            {
                return;
            }

            try { Destroy(character.gameObject); } catch (System.Exception e) { DevLog("[ZombieMode] Destroy paused spawn candidate failed: " + e.Message); }
        }

        private void PrepareZombieModeSpawnedEnemy(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker, float forceTraceDistance)
        {
            if (enemy == null)
            {
                return;
            }

            enemy.dropBoxOnDead = false;
            // 默认 Teams.scav；若与玩家不敌对则切换到 Teams.wolf
            enemy.SetTeam(Teams.scav);
            try
            {
                CharacterMainControl playerForTeam = CharacterMainControl.Main;
                if (playerForTeam != null && !Team.IsEnemy(playerForTeam.Team, enemy.Team))
                {
                    enemy.SetTeam(Teams.wolf);
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] Team.IsEnemy 校验失败: " + e.Message);
            }

            if (enemy.Health != null)
            {
                enemy.Health.SetHealth(enemy.Health.MaxHealth);
            }

            AICharacterController ai = GetZombieModeEnemyAI(enemy.gameObject, marker);
            if (ai != null)
            {
                ai.forceTracePlayerDistance = Mathf.Max(ai.forceTracePlayerDistance, forceTraceDistance);
                if (ShouldSuppressZombieModeEnemyAggroForSafeZone())
                {
                    SetZombieModeEnemyThreatSuppressed(enemy.gameObject, marker, true);
                    return;
                }

                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null)
                {
                    SetZombieModeEnemyTargetToMainPlayer(ai);
                }
                ai.noticed = true;
            }
        }

        private int GetZombieModeBossCount()
        {
            int effectiveSpawnPointCount = zombieModeRunState.EffectiveSpawnPoints.Count > 0
                ? zombieModeRunState.EffectiveSpawnPoints.Count
                : zombieModeRunState.SpawnPoints.Count;
            return Mathf.Max(1, 1 + Mathf.FloorToInt(effectiveSpawnPointCount / 10f));
        }

        // 静态读取以避免每波刷怪 new[] 装箱（审查 §3.7）。
        private static readonly ZombieModeBossKind[] s_zombieModeBossKindOrder = new ZombieModeBossKind[]
        {
            ZombieModeBossKind.Titan,
            ZombieModeBossKind.Hunter,
            ZombieModeBossKind.Splitter,
            ZombieModeBossKind.Shielder,
            ZombieModeBossKind.Corruptor
        };

        private ZombieModeBossKind GetZombieModeBossKindForIndex(int bossIndex)
        {
            int offset = Mathf.Max(0, zombieModeRunState.CurrentWave / 5 - 1);
            return s_zombieModeBossKindOrder[(offset + bossIndex) % s_zombieModeBossKindOrder.Length];
        }

        private Vector3 GetZombieModeBossSpawnPosition(int bossIndex)
        {
            if (zombieModeRunState.SpawnPoints.Count <= 0)
            {
                return GetZombieModeSpawnPosition();
            }

            int index = Mathf.Abs(zombieModeRunState.CurrentWave + bossIndex) % zombieModeRunState.SpawnPoints.Count;
            Vector3 candidate = zombieModeRunState.SpawnPoints[index].Position;
            for (int i = 0; i < zombieModeRunState.CurrentWaveBossInstances.Count; i++)
            {
                ZombieModeBossInstance existing = zombieModeRunState.CurrentWaveBossInstances[i];
                if (existing == null || existing.Character == null)
                {
                    continue;
                }

                Vector3 delta = existing.Character.transform.position - candidate;
                delta.y = 0f;
                if (delta.sqrMagnitude < ZombieModeTuning.BossSpreadMinDistance * ZombieModeTuning.BossSpreadMinDistance)
                {
                    return GetZombieModeSpawnPosition();
                }
            }

            return candidate;
        }

        private int GetZombieModeBossPointValue(ZombieModeBossKind kind)
        {
            // 入门数值表收口在 ZombieModeTuning.GetBossKind（见审查 §1.2）。
            BossKindTuning tuning = ZombieModeTuning.GetBossKind(kind);
            int baseValue = Random.Range(tuning.PointMin, tuning.PointMax + 1);
            int pollutionSteps = Mathf.FloorToInt(zombieModeRunState.TotalPollution / 10f);
            float multiplier = Mathf.Min(
                1f + pollutionSteps * ZombieModeTuning.PurificationPollutionScalePerStep,
                ZombieModeTuning.PurificationPollutionScaleMax);
            return Mathf.Max(1, Mathf.FloorToInt(baseValue * multiplier));
        }

        private void ApplyZombieModeBossTuning(CharacterMainControl boss, ZombieModeBossKind kind, ZombieModeEnemyRuntimeMarker marker)
        {
            if (boss == null || boss.Health == null)
            {
                return;
            }

            BossKindTuning tuning = ZombieModeTuning.GetBossKind(kind);
            float healthMultiplier = tuning.HealthMultiplier;
            float damageMultiplier = tuning.DamageMultiplier;
            float scaleMultiplier = tuning.ScaleMultiplier;
            float speedMultiplier = tuning.SpeedMultiplier;

            ApplyZombieModeHealthOnlyMultiplier(boss, healthMultiplier, marker);

            boss.Health.showHealthBar = true;
            if (boss.Health.MaxHealth > 0f)
            {
                boss.Health.CurrentHealth = boss.Health.MaxHealth;
            }
            boss.transform.localScale = boss.transform.localScale * scaleMultiplier;
            ApplyZombieModeEnemyCombatStatMultipliers(boss, damageMultiplier, speedMultiplier, marker);
            AICharacterController ai = GetZombieModeEnemyAI(boss.gameObject, marker);
            if (ai != null)
            {
                ai.forceTracePlayerDistance = Mathf.Max(ai.forceTracePlayerDistance, 220f * speedMultiplier);
                SetZombieModeEnemyTargetToMainPlayer(ai);
            }
            boss.PopText(GetZombieModeBossDisplayName(kind));
        }

        private string GetZombieModeBossDisplayName(ZombieModeBossKind kind)
        {
            // 简化 5-case switch 为字符串拼接（审查 §2.4）。L10n key 由
            // LocalizationInjector 注册，5 个 BossRush_ZombieMode_Boss_<Kind> 全部存在。
            return L10n.T("BossRush_ZombieMode_Boss_" + kind.ToString());
        }
    }
}
