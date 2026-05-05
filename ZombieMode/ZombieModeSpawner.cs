using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string ZOMBIE_MODE_NORMAL_PRESET_NAME = "Cname_Zombie";
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
            if (zombieModeRunState.MapProfile != null)
            {
                AddZombieModeSpawnPointArray(zombieModeRunState.MapProfile.StaticSpawnPoints, false);
            }

            if (zombieModeRunState.SpawnPoints.Count <= 0)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                Vector3 center = player != null ? player.transform.position : Vector3.zero;
                for (int i = 0; i < 16; i++)
                {
                    float angle = 360f * i / 16f;
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 24f;
                    AddZombieModeSpawnPoint(center + offset, true);
                }
            }

            DevLog("[ZombieMode] 收集刷怪点: " + zombieModeRunState.SpawnPoints.Count);
            zombieModeRunState.EffectiveSpawnPoints.Clear();
            for (int i = 0; i < zombieModeRunState.SpawnPoints.Count; i++)
            {
                zombieModeRunState.EffectiveSpawnPoints.Add(zombieModeRunState.SpawnPoints[i]);
            }
            return zombieModeRunState.SpawnPoints.Count > 0;
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

            float duplicateDistanceSqr = ZombieModeTuning.SpawnPointDuplicateDistance * ZombieModeTuning.SpawnPointDuplicateDistance;
            for (int i = 0; i < zombieModeRunState.SpawnPoints.Count; i++)
            {
                Vector3 delta = zombieModeRunState.SpawnPoints[i].Position - snapped;
                delta.y = 0f;
                if (delta.sqrMagnitude < duplicateDistanceSqr)
                {
                    return;
                }
            }

            zombieModeRunState.SpawnPoints.Add(new ZombieModeSpawnPoint(snapped, virtualPoint));
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
                Vector3 fallback = main != null ? main.transform.position + Vector3.forward * 20f : Vector3.zero;
                Vector3 resolvedFallback;
                return TryResolveZombieModeSpawnPoint(fallback, true, out resolvedFallback) ? resolvedFallback : fallback;
            }

            Vector3 playerPos = main != null ? main.transform.position : Vector3.zero;
            float bestScore = float.MinValue;
            Vector3 best = zombieModeRunState.SpawnPoints[0].Position;
            for (int i = 0; i < zombieModeRunState.SpawnPoints.Count; i++)
            {
                Vector3 point = zombieModeRunState.SpawnPoints[i].Position;
                Vector3 delta = point - playerPos;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance < ZombieModeTuning.SpawnPointMinPlayerDistance)
                {
                    continue;
                }

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

        private UniTask<CharacterMainControl> TrySpawnZombieModeNormalZombieAsync(
            int runId,
            Vector3 position,
            ZombieModeEnemyKind forcedEnemyKind = ZombieModeEnemyKind.Normal,
            bool forceEnemyKind = false)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return UniTask.FromResult<CharacterMainControl>(null);
            }

            if (!TryReserveZombieModeNormalSpawnSlot(runId))
            {
                return UniTask.FromResult<CharacterMainControl>(null);
            }

            // 入口确保 cachedCharacterPresets 已构建（避免依赖 Mode D 先初始化，§1.1）。
            EnsureCharacterPresetsCacheReady();

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
                isActiveCheck: () => IsZombieModeRunValid(runId),
                onSpawned: ctx =>
                {
                    CharacterMainControl zombie = ctx.character;

                    ZombieModeEnemyKind enemyKind = forceEnemyKind ? forcedEnemyKind : RollZombieModeEnemyKind();
                    ZombieModeSpecialKind specialKind = enemyKind == ZombieModeEnemyKind.Special
                        ? RollZombieModeSpecialKind()
                        : ZombieModeSpecialKind.None;
                    List<ZombieModeEliteAffix> eliteAffixes = enemyKind == ZombieModeEnemyKind.Elite
                        ? RollZombieModeEliteAffixes()
                        : null;

                    zombie.gameObject.name = "ZombieMode_NormalZombie_Run" + runId;
                    ReleaseZombieModeNormalSpawnSlot();
                    PrepareZombieModeSpawnedEnemy(zombie, ZombieModeTuning.NormalZombieForceTraceDistance);

                    ZombieModeEnemyRuntimeMarker marker = RegisterZombieModeEnemyRuntimeShell(runId, zombie, false, ZombieModeBossKind.Titan, -1, enemyKind, specialKind, eliteAffixes);
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

            return tcs.Task;
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

        private UniTask<CharacterMainControl> TrySpawnZombieModeBossAsync(int runId, Vector3 position, ZombieModeBossKind kind)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return UniTask.FromResult<CharacterMainControl>(null);
            }

            // 入口确保 cachedCharacterPresets 已构建（§1.1）。
            EnsureCharacterPresetsCacheReady();

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
                    boss.gameObject.name = "ZombieMode_Boss_" + kind.ToString() + "_Run" + runId;
                    PrepareZombieModeSpawnedEnemy(boss, 180f);
                    ApplyZombieModeBossTuning(boss, kind);
                    ZombieModeEnemyRuntimeMarker bossMarker = RegisterZombieModeEnemyRuntimeShell(runId, boss, true, kind, GetZombieModeBossPointValue(kind));
                    RegisterEnemyRecoveryAnchor(boss, ctx.position);
                    zombieModeRunState.LivingZombieCount++;

                    ZombieModeBossInstance instance = new ZombieModeBossInstance();
                    instance.Character = boss;
                    instance.Kind = kind;
                    instance.Marker = bossMarker;
                    instance.Lifecycle.Alive = true;
                    instance.Lifecycle.LastKnownPosition = boss.transform.position;
                    instance.Lifecycle.LastReachableTime = Time.unscaledTime;
                    instance.Lifecycle.LastHurtTime = Time.unscaledTime;
                    zombieModeRunState.CurrentWaveBossInstances.Add(instance);
                    RegisterZombieModeBossRuntime(runId, boss, kind);
                    tcs.TrySetResult(boss);
                },
                onFailed: () => tcs.TrySetResult(null),
                applyEquipment: false,
                applyBossMultiplier: false,
                skipBossRushLootTracking: true,
                normalizeDamageMultiplier: false);

            return tcs.Task;
        }

        private void PrepareZombieModeSpawnedEnemy(CharacterMainControl enemy, float forceTraceDistance)
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

            AICharacterController ai = enemy.GetComponentInChildren<AICharacterController>();
            if (ai != null)
            {
                ai.forceTracePlayerDistance = Mathf.Max(ai.forceTracePlayerDistance, forceTraceDistance);
                if (ShouldSuppressZombieModeEnemyAggroForSafeZone())
                {
                    ai.searchedEnemy = null;
                    ai.noticed = false;
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

        private void ApplyZombieModeBossTuning(CharacterMainControl boss, ZombieModeBossKind kind)
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

            ApplyZombieModeHealthOnlyMultiplier(boss, healthMultiplier);

            boss.Health.showHealthBar = true;
            if (boss.Health.MaxHealth > 0f)
            {
                boss.Health.CurrentHealth = boss.Health.MaxHealth;
            }
            boss.transform.localScale = boss.transform.localScale * scaleMultiplier;
            ApplyZombieModeEnemyCombatStatMultipliers(boss, damageMultiplier, speedMultiplier);
            AICharacterController ai = boss.GetComponentInChildren<AICharacterController>();
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
