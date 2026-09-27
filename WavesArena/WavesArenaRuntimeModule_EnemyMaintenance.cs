using System;
using System.Collections;
using UnityEngine;
using Duckov;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal void ForceKillAllEnemies()
        {
            try
            {
                RefreshCharacterCache();
                _cachedCharacters.RemoveAll(c => c == null);

                if (_cachedCharacters.Count == 0)
                {
                    ModBehaviour.DevLog("[BossRush] ForceKillAllEnemies: 没有找到任何角色");
                    return;
                }

                CharacterMainControl main = null;
                try { main = CharacterMainControl.Main; } catch {}

                int killedCount = 0;

                foreach (var c in _cachedCharacters)
                {
                    if (c == null) continue;

                    bool isMain = false;
                    try
                    {
                        if (main != null && c == main) isMain = true;
                        else isMain = CharacterMainControlExtensions.IsMainCharacter(c);
                    }
                    catch {}
                    if (isMain) continue;

                    if (owner.IsDeathWraithCharacterForArena(c))
                    {
                        continue;
                    }

                    // 遗种巢随从豁免（加法分支）：走静态身份表而不是 preset.team，
                    // 因为运行时 SetTeam 不会改写 characterPreset.team（Mode E 换阵营时
                    // 下面那段 isPet 的前置条件会失效）。
                    if (PetNestCompanionAgent.IsCompanionCharacter(c)) continue;

                    bool isPet = false;
                    try
                    {
                        if (c.characterPreset != null && c.characterPreset.team == Teams.player)
                        {
                            isPet = c.GetComponent<PetAI>() != null
                                || c.GetComponent<PetNestCompanionAgent>() != null;
                        }
                    }
                    catch {}
                    if (isPet) continue;

                    bool isEggDuck = false;
                    try
                    {
                        if (owner.ArenaEggSpawnPreset != null && c.characterPreset == owner.ArenaEggSpawnPreset)
                        {
                            isEggDuck = true;
                        }
                    }
                    catch {}
                    if (isEggDuck) continue;

                    bool isEnemy = false;
                    try
                    {
                        if (c.characterPreset != null)
                        {
                            Teams team = c.characterPreset.team;
                            isEnemy = (team == Teams.scav || team == Teams.usec ||
                                      team == Teams.bear || team == Teams.lab || team == Teams.wolf);
                        }
                    }
                    catch {}
                    if (!isEnemy) continue;

                    try
                    {
                        if (c.Health != null && !c.Health.IsDead)
                        {
                            DamageInfo dmgInfo = new DamageInfo(main);
                            dmgInfo.damageValue = c.Health.MaxHealth * 10f;
                            dmgInfo.ignoreArmor = true;
                            c.Health.Hurt(dmgInfo);
                            killedCount++;
                        }
                    }
                    catch
                    {
                        try
                        {
                            if (c.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(c.gameObject);
                                killedCount++;
                            }
                        }
                        catch {}
                    }
                }

                _characterCacheNeedsRefresh = true;
                ModBehaviour.DevLog("[BossRush] ForceKillAllEnemies: 已杀死 " + killedCount + " 个敌人");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ForceKillAllEnemies 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 清理场景中的敌人（用于 BossRush 初始化阶段）
        /// [性能优化] 使用角色缓存而非 FindObjectsOfType，并缓存 PetAI 检查结果
        /// [性能优化] 只清理竞技场范围内（50米）的敌人
        /// </summary>
        internal void ClearEnemiesForBossRush()
        {
            try
            {
                if (_characterCacheNeedsRefresh || _cachedCharacters.Count == 0)
                {
                    RefreshCharacterCache();
                }

                _cachedCharacters.RemoveAll(c => c == null);

                if (_cachedCharacters.Count == 0)
                {
                    return;
                }

                ModBehaviour.DevLog("[BossRush] ClearEnemiesForBossRush: 开始清理，缓存角色数=" + _cachedCharacters.Count + ", 竞技场中心已设置=" + _arenaCenterSet);

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                int clearedCount = 0;
                _reusableDestroyList.Clear();

                bool useRangeLimit = _arenaCenterSet;
                float radiusSq = ModBehaviour.ARENA_RADIUS * ModBehaviour.ARENA_RADIUS;

                foreach (var c in _cachedCharacters)
                {
                    if (c == null)
                    {
                        continue;
                    }

                    bool isMain = false;
                    try
                    {
                        if (main != null && c == main)
                        {
                            isMain = true;
                        }
                        else
                        {
                            isMain = CharacterMainControlExtensions.IsMainCharacter(c);
                        }
                    }
                    catch {}

                    if (isMain)
                    {
                        continue;
                    }

                    if (owner.IsDeathWraithCharacterForArena(c))
                    {
                        continue;
                    }

                    bool isEggDuck = false;
                    try
                    {
                        if (owner.ArenaEggSpawnPreset != null && c.characterPreset == owner.ArenaEggSpawnPreset)
                        {
                            isEggDuck = true;
                        }
                    }
                    catch {}

                    if (isEggDuck)
                    {
                        continue;
                    }

                    // 遗种巢随从豁免（加法分支），理由同 ForceKillAllEnemies 处
                    if (PetNestCompanionAgent.IsCompanionCharacter(c))
                    {
                        continue;
                    }

                    bool isPet = false;
                    try
                    {
                        if (c.characterPreset != null && c.characterPreset.team == Teams.player)
                        {
                            isPet = c.GetComponent<PetAI>() != null
                                || c.GetComponent<PetNestCompanionAgent>() != null;
                        }
                    }
                    catch {}

                    if (isPet)
                    {
                        continue;
                    }

                    if (owner.IsModeEActive && owner.IsModeETrackedEnemyForArena(c))
                    {
                        continue;
                    }

                    bool isEnemy = false;
                    try
                    {
                        if (c.characterPreset != null)
                        {
                            Teams team = c.characterPreset.team;
                            isEnemy = (team == Teams.scav || team == Teams.usec ||
                                      team == Teams.bear || team == Teams.lab || team == Teams.wolf);
                        }
                    }
                    catch {}

                    if (!isEnemy)
                    {
                        continue;
                    }

                    if (useRangeLimit && c.transform != null)
                    {
                        float distSq = (c.transform.position - _arenaCenter).sqrMagnitude;
                        if (distSq > radiusSq)
                        {
                            continue;
                        }
                    }

                    if (c.gameObject != null)
                    {
                        _reusableDestroyList.Add(c.gameObject);
                    }
                }

                foreach (var go in _reusableDestroyList)
                {
                    if (go != null)
                    {
                        UnityEngine.Object.Destroy(go);
                        clearedCount++;
                    }
                }

                if (clearedCount > 0)
                {
                    _characterCacheNeedsRefresh = true;
                    ModBehaviour.DevLog("[BossRush] ClearEnemiesForBossRush: 已清理 " + clearedCount + " 个敌人");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ClearEnemiesForBossRush 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 持续清理敌人直到波次开始
        /// [性能优化] 使用渐进式间隔，前期快速清理，后期降低频率
        /// [低端机优化] 减少持续清理的 CPU 开销
        /// </summary>
        internal IEnumerator ContinuousClearEnemiesUntilWaveStart()
        {
            ModBehaviour.DevLog("[BossRush] ContinuousClearEnemiesUntilWaveStart: 协程已启动");

            RefreshCharacterCache();

            int loopCount = 0;
            const int MAX_SPAWNER_DISABLE_ATTEMPTS = 5;
            const int MAX_LOOP_COUNT = 600;

            // Mode G 门控（加法分支）：Mode G run 期间（含 Starting）停止 Legacy 持续清怪，
            // 避免误删 Mode G 首波；查询 no-throw、未运行时条件与当前完全相同。
            while (!owner.IsActive && owner.IsBossRushArenaActive && !owner.IsModeEActive && !ModBehaviour.IsModeGRunInProgressSafe() && !ModBehaviour.IsModeHRunInProgressSafe() && loopCount < MAX_LOOP_COUNT)
            {
                loopCount++;

                if (loopCount <= MAX_SPAWNER_DISABLE_ATTEMPTS)
                {
                    SpawnersDisabled = false;
                    DisableAllSpawners();
                }

                RefreshCharacterCache();
                int enemyCount = _cachedCharacters.Count;
                ClearEnemiesForBossRush();

                if (loopCount <= 5 || loopCount % 10 == 0)
                {
                    ModBehaviour.DevLog("[BossRush] ContinuousClearEnemiesUntilWaveStart: 第 " + loopCount + " 次清理，缓存角色数=" + enemyCount);
                }

                yield return owner.ArenaSharedWait05s;
            }

            ModBehaviour.DevLog("[BossRush] ContinuousClearEnemiesUntilWaveStart: 协程结束，IsActive=" + owner.IsActive + ", owner.IsBossRushArenaActive=" + owner.IsBossRushArenaActive + ", 总循环次数=" + loopCount);
        }
    }
}
