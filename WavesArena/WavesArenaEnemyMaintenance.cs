using System;
using System.Collections;
using UnityEngine;
using Duckov;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 强制杀死所有敌人（用于F10调试，忽略范围限制）
        /// 直接调用Health.Kill()而不是Destroy，确保触发死亡事件
        /// </summary>
        private void ForceKillAllEnemies()
        {
            try
            {
                RefreshCharacterCache();
                _cachedCharacters.RemoveAll(c => c == null);

                if (_cachedCharacters.Count == 0)
                {
                    DevLog("[BossRush] ForceKillAllEnemies: 没有找到任何角色");
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

                    if (IsDeathWraithCharacter_DeathWraith(c))
                    {
                        continue;
                    }

                    bool isPet = false;
                    try
                    {
                        if (c.characterPreset != null && c.characterPreset.team == Teams.player)
                        {
                            isPet = c.GetComponent<PetAI>() != null;
                        }
                    }
                    catch {}
                    if (isPet) continue;

                    bool isEggDuck = false;
                    try
                    {
                        if (eggSpawnPreset != null && c.characterPreset == eggSpawnPreset)
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
                DevLog("[BossRush] ForceKillAllEnemies: 已杀死 " + killedCount + " 个敌人");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] ForceKillAllEnemies 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 清理场景中的敌人（用于 BossRush 初始化阶段）
        /// [性能优化] 使用角色缓存而非 FindObjectsOfType，并缓存 PetAI 检查结果
        /// [性能优化] 只清理竞技场范围内（50米）的敌人
        /// </summary>
        private void ClearEnemiesForBossRush()
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

                DevLog("[BossRush] ClearEnemiesForBossRush: 开始清理，缓存角色数=" + _cachedCharacters.Count + ", 竞技场中心已设置=" + _arenaCenterSet);

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                int clearedCount = 0;
                _reusableDestroyList.Clear();

                bool useRangeLimit = _arenaCenterSet;
                float radiusSq = ARENA_RADIUS * ARENA_RADIUS;

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

                    if (IsDeathWraithCharacter_DeathWraith(c))
                    {
                        continue;
                    }

                    bool isEggDuck = false;
                    try
                    {
                        if (eggSpawnPreset != null && c.characterPreset == eggSpawnPreset)
                        {
                            isEggDuck = true;
                        }
                    }
                    catch {}

                    if (isEggDuck)
                    {
                        continue;
                    }

                    bool isPet = false;
                    try
                    {
                        if (c.characterPreset != null && c.characterPreset.team == Teams.player)
                        {
                            isPet = c.GetComponent<PetAI>() != null;
                        }
                    }
                    catch {}

                    if (isPet)
                    {
                        continue;
                    }

                    if (modeEActive && modeEAliveEnemySet.Contains(c))
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
                    DevLog("[BossRush] ClearEnemiesForBossRush: 已清理 " + clearedCount + " 个敌人");
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] ClearEnemiesForBossRush 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 持续清理敌人直到波次开始
        /// [性能优化] 使用渐进式间隔，前期快速清理，后期降低频率
        /// [低端机优化] 减少持续清理的 CPU 开销
        /// </summary>
        private IEnumerator ContinuousClearEnemiesUntilWaveStart()
        {
            DevLog("[BossRush] ContinuousClearEnemiesUntilWaveStart: 协程已启动");

            RefreshCharacterCache();

            int loopCount = 0;
            const int MAX_SPAWNER_DISABLE_ATTEMPTS = 5;

            while (!IsActive && bossRushArenaActive && !modeEActive)
            {
                loopCount++;

                if (loopCount <= MAX_SPAWNER_DISABLE_ATTEMPTS)
                {
                    spawnersDisabled = false;
                    DisableAllSpawners();
                }

                RefreshCharacterCache();
                int enemyCount = _cachedCharacters.Count;
                ClearEnemiesForBossRush();

                if (loopCount <= 5 || loopCount % 10 == 0)
                {
                    DevLog("[BossRush] ContinuousClearEnemiesUntilWaveStart: 第 " + loopCount + " 次清理，缓存角色数=" + enemyCount);
                }

                yield return new WaitForSeconds(0.5f);
            }

            DevLog("[BossRush] ContinuousClearEnemiesUntilWaveStart: 协程结束，IsActive=" + IsActive + ", bossRushArenaActive=" + bossRushArenaActive + ", 总循环次数=" + loopCount);
        }
    }
}
