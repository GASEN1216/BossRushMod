using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string ZOMBIE_MODE_NORMAL_PRESET_NAME = "Cname_Zombie";
        private CharacterRandomPreset zombieModeCachedNormalZombiePreset;
        private bool zombieModeNormalZombiePresetSearched;

        private bool CollectZombieModeSpawnPoints(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            zombieModeRunState.SpawnPoints.Clear();
            BossRushMapConfig mapConfig = GetCurrentMapConfig();
            if (mapConfig != null)
            {
                AddZombieModeSpawnPointArray(mapConfig.modeESpawnPoints, false);
                AddZombieModeSpawnPointArray(mapConfig.spawnPoints, false);
                if (mapConfig.customSpawnPos.HasValue)
                {
                    AddZombieModeSpawnPoint(mapConfig.customSpawnPos.Value, true);
                }
            }

            CollectZombieModeOriginalSpawnerPoints();

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

        private void CollectZombieModeOriginalSpawnerPoints()
        {
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                CharacterSpawnerRoot[] spawners = UnityEngine.Object.FindObjectsOfType<CharacterSpawnerRoot>(true);
                if (spawners == null || spawners.Length == 0)
                {
                    return;
                }

                for (int i = 0; i < spawners.Length; i++)
                {
                    CharacterSpawnerRoot spawner = spawners[i];
                    if (spawner == null || spawner.gameObject == null || spawner.gameObject.scene != scene)
                    {
                        continue;
                    }

                    Points pointsComponent = spawner.GetComponentInChildren<Points>(true);
                    if (pointsComponent != null && pointsComponent.points != null && pointsComponent.points.Count > 0)
                    {
                        for (int j = 0; j < pointsComponent.points.Count; j++)
                        {
                            Vector3 worldPoint = pointsComponent.GetPoint(j);
                            if (worldPoint != Vector3.zero)
                            {
                                AddZombieModeSpawnPoint(worldPoint, false);
                            }
                        }
                        continue;
                    }

                    if (spawner.transform != null)
                    {
                        AddZombieModeSpawnPoint(spawner.transform.position, false);
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 收集原地图刷怪点失败: " + e.Message);
            }
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
            if (zombieModeRunState.SpawnPoints.Count <= 0)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                Vector3 fallback = player != null ? player.transform.position + Vector3.forward * 20f : Vector3.zero;
                Vector3 resolvedFallback;
                return TryResolveZombieModeSpawnPoint(fallback, true, out resolvedFallback) ? resolvedFallback : fallback;
            }

            CharacterMainControl main = CharacterMainControl.Main;
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

        private bool TryFindZombieModeVirtualSpawnAroundPlayer(Vector3 playerPos, out Vector3 resolved)
        {
            return SpawnPointGeometryHelper.TryFindAroundPlayer(
                playerPos,
                ringCount: 12,
                radius: 24f,
                navSampleRadius: ZombieModeTuning.NavMeshVirtualSpawnRadius,
                liftOffset: ZombieModeTuning.NavMeshLiftOffset,
                minPlayerDistance: ZombieModeTuning.SpawnPointMinPlayerDistance,
                out resolved);
        }

        private bool TryResolveZombieModeSpawnPoint(Vector3 position, bool virtualPoint, out Vector3 resolved)
        {
            float sampleRadius = virtualPoint
                ? ZombieModeTuning.NavMeshVirtualSpawnRadius
                : ZombieModeTuning.SpawnPointNavMeshSampleRadius;
            return SpawnPointGeometryHelper.TryResolve(
                position,
                sampleRadius,
                ZombieModeTuning.NavMeshLiftOffset,
                virtualPoint,
                ZombieModeTuning.SpawnPointMinPlayerDistance,
                out resolved);
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

            CharacterRandomPreset preset = FindZombieModeNormalZombiePreset();
            if (preset == null)
            {
                return UniTask.FromResult<CharacterMainControl>(null);
            }

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
                    PrepareZombieModeSpawnedEnemy(zombie, 100f);

                    ZombieModeEnemyRuntimeMarker marker = RegisterZombieModeEnemyRuntimeShell(runId, zombie, false, ZombieModeBossKind.Titan, -1, enemyKind, specialKind, eliteAffixes);
                    ApplyZombieModeEnemyTuning(zombie, marker);
                    RegisterEnemyRecoveryAnchor(zombie, ctx.position);
                    zombieModeRunState.LivingZombieCount++;
                    tcs.TrySetResult(zombie);
                },
                onFailed: () => tcs.TrySetResult(null),
                applyEquipment: false,
                applyBossMultiplier: false,
                directPreset: preset);

            return tcs.Task;
        }

        private UniTask<CharacterMainControl> TrySpawnZombieModeBossAsync(int runId, Vector3 position, ZombieModeBossKind kind)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return UniTask.FromResult<CharacterMainControl>(null);
            }

            CharacterRandomPreset preset = FindZombieModeNormalZombiePreset();
            if (preset == null)
            {
                return UniTask.FromResult<CharacterMainControl>(null);
            }

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
                    RegisterZombieModeEnemyRuntimeShell(runId, boss, true, kind, GetZombieModeBossPointValue(kind));
                    RegisterEnemyRecoveryAnchor(boss, ctx.position);
                    zombieModeRunState.LivingZombieCount++;

                    ZombieModeBossInstance instance = new ZombieModeBossInstance();
                    instance.Character = boss;
                    instance.Kind = kind;
                    instance.Alive = true;
                    instance.LootSettled = false;
                    instance.PointsSettled = false;
                    instance.LastKnownPosition = boss.transform.position;
                    instance.LastReachableTime = Time.unscaledTime;
                    instance.LastHurtTime = Time.unscaledTime;
                    instance.NextSkillTime = Time.unscaledTime + UnityEngine.Random.Range(2f, 5f);
                    instance.SkillSequence = 0;
                    zombieModeRunState.CurrentWaveBossInstances.Add(instance);
                    RegisterZombieModeBossRuntime(runId, boss, kind);
                    tcs.TrySetResult(boss);
                },
                onFailed: () => tcs.TrySetResult(null),
                applyEquipment: false,
                applyBossMultiplier: false,
                directPreset: preset);

            return tcs.Task;
        }

        private void PrepareZombieModeSpawnedEnemy(CharacterMainControl enemy, float forceTraceDistance)
        {
            if (enemy == null)
            {
                return;
            }

            enemy.dropBoxOnDead = false;
            // SPEC 16 §4.3: 默认 Teams.scav；若与玩家不敌对则切换到 Teams.wolf
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
                ai.forceTracePlayerDistance = forceTraceDistance;
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

        private ZombieModeBossKind GetZombieModeBossKindForIndex(int bossIndex)
        {
            ZombieModeBossKind[] kinds = new ZombieModeBossKind[]
            {
                ZombieModeBossKind.Titan,
                ZombieModeBossKind.Hunter,
                ZombieModeBossKind.Splitter,
                ZombieModeBossKind.Shielder,
                ZombieModeBossKind.Corruptor
            };
            int offset = Mathf.Max(0, zombieModeRunState.CurrentWave / 5 - 1);
            return kinds[(offset + bossIndex) % kinds.Length];
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
            int min = 300;
            int max = 800;
            switch (kind)
            {
                case ZombieModeBossKind.Titan:
                    min = 400;
                    max = 600;
                    break;
                case ZombieModeBossKind.Hunter:
                    min = 300;
                    max = 500;
                    break;
                case ZombieModeBossKind.Splitter:
                    min = 400;
                    max = 700;
                    break;
                case ZombieModeBossKind.Shielder:
                    min = 400;
                    max = 600;
                    break;
                case ZombieModeBossKind.Corruptor:
                    min = 400;
                    max = 650;
                    break;
            }

            int baseValue = Random.Range(min, max + 1);
            int pollutionSteps = Mathf.FloorToInt(zombieModeRunState.TotalPollution / 10f);
            float multiplier = Mathf.Min(1f + pollutionSteps * 0.10f, ZombieModeTuning.PurificationPollutionScaleMax);
            return Mathf.Max(1, Mathf.FloorToInt(baseValue * multiplier));
        }

        private void ApplyZombieModeBossTuning(CharacterMainControl boss, ZombieModeBossKind kind)
        {
            if (boss == null || boss.Health == null)
            {
                return;
            }

            float healthMultiplier = 2.2f;
            float damageMultiplier = 1.2f;
            float scaleMultiplier = 1.18f;
            float speedMultiplier = 1f;
            switch (kind)
            {
                case ZombieModeBossKind.Titan:
                    healthMultiplier = 35f;
                    damageMultiplier = 1.8f;
                    scaleMultiplier = 1.8f;
                    speedMultiplier = 0.7f;
                    break;
                case ZombieModeBossKind.Hunter:
                    healthMultiplier = 18f;
                    damageMultiplier = 1.4f;
                    scaleMultiplier = 1.2f;
                    speedMultiplier = 1.6f;
                    break;
                case ZombieModeBossKind.Splitter:
                    healthMultiplier = 25f;
                    damageMultiplier = 1.1f;
                    scaleMultiplier = 1.5f;
                    speedMultiplier = 0.95f;
                    break;
                case ZombieModeBossKind.Shielder:
                    healthMultiplier = 28f;
                    damageMultiplier = 1.3f;
                    scaleMultiplier = 1.3f;
                    speedMultiplier = 0.9f;
                    break;
                case ZombieModeBossKind.Corruptor:
                    healthMultiplier = 26f;
                    damageMultiplier = 1.2f;
                    scaleMultiplier = 1.4f;
                    speedMultiplier = 1.0f;
                    break;
            }

            // 通过 ApplyBossStatMultiplier 调整 MaxHealth Stat（与项目其它 Boss 一致），
            // 避免反射写 Health 私有字段（鸭科夫一改字段名就静默失效）。
            ApplyBossStatMultiplier(boss, healthMultiplier);

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
            switch (kind)
            {
                case ZombieModeBossKind.Titan:
                    return L10n.T("BossRush_ZombieMode_Boss_Titan");
                case ZombieModeBossKind.Hunter:
                    return L10n.T("BossRush_ZombieMode_Boss_Hunter");
                case ZombieModeBossKind.Splitter:
                    return L10n.T("BossRush_ZombieMode_Boss_Splitter");
                case ZombieModeBossKind.Shielder:
                    return L10n.T("BossRush_ZombieMode_Boss_Shielder");
                default:
                    return L10n.T("BossRush_ZombieMode_Boss_Corruptor");
            }
        }

        private CharacterRandomPreset FindZombieModeNormalZombiePreset()
        {
            if (zombieModeCachedNormalZombiePreset != null)
            {
                return zombieModeCachedNormalZombiePreset;
            }

            if (zombieModeNormalZombiePresetSearched)
            {
                return null;
            }

            zombieModeNormalZombiePresetSearched = true;
            try
            {
                CharacterRandomPreset[] presets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                for (int i = 0; i < presets.Length; i++)
                {
                    CharacterRandomPreset preset = presets[i];
                    if (preset != null && preset.nameKey == ZOMBIE_MODE_NORMAL_PRESET_NAME)
                    {
                        zombieModeCachedNormalZombiePreset = preset;
                        return preset;
                    }
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] 查找丧尸预设失败: " + e.Message);
            }

            return null;
        }
    }
}
