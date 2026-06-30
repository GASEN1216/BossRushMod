using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 禁用场景中的所有spawner
        /// [性能优化] 只扫描 CharacterSpawnerRoot，因为它是所有 spawner 的根控制器
        /// [性能优化] 使用缓存的反射字段，避免重复获取
        /// [修复] 先通过反射设置 created=true 阻止生成，再销毁 spawner
        /// [Bug修复] 移除范围检查，禁用所有 spawner，因为 spawner root 位置可能与实际刷怪点位置不同
        /// [Bug修复] 销毁前保留灯光组件，避免场景变暗
        /// [性能优化] 把「阻止生成」与「批量销毁」拆开：标记 created=true 这一步同步完成、
        ///   立即阻止刷怪（过图帧必须保证），但灯光重挂 + Destroy 大量 GameObject 这一帧尖峰
        ///   挪到跨帧协程，避免场景加载帧卡顿。
        /// </summary>
        private void DisableAllSpawners()
        {
            if (spawnersDisabled)
            {
                return;
            }

            try
            {
                int disabledCount = 0;

                if (!_createdFieldCached)
                {
                    _cachedCreatedField = typeof(CharacterSpawnerRoot).GetField("created",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    _createdFieldCached = true;
                }

                CharacterSpawnerRoot[] spawnerRoots = ObjectCache.GetCharacterSpawnerRoots();

                // 收集待销毁的 root，销毁动作延后到协程分帧执行。
                List<CharacterSpawnerRoot> rootsToDestroy = new List<CharacterSpawnerRoot>();

                if (spawnerRoots != null)
                {
                    foreach (var root in spawnerRoots)
                    {
                        if (root == null || root.gameObject == null) continue;

                        // [关键] 立即标记 created=true，同步阻止刷怪——这一步极轻量，必须在本帧完成。
                        if (_cachedCreatedField != null)
                        {
                            try
                            {
                                _cachedCreatedField.SetValue(root, true);
                                disabledCount++;
                            }
                            catch (Exception e)
                            {
                                DevLog("[BossRush] [WARNING] DisableAllSpawners 标记 spawner created 失败: " + e.Message);
                            }
                        }

                        rootsToDestroy.Add(root);
                    }
                }

                // 立即置位：刷怪已被阻止，重复调用会直接返回；销毁是收尾清理，可异步。
                spawnersDisabled = true;
                DevLog("[BossRush] 已标记禁用 " + disabledCount + " 个 CharacterSpawnerRoot，销毁将分帧进行");

                if (rootsToDestroy.Count > 0)
                {
                    StartCoroutine(DestroySpawnerRootsAcrossFrames(rootsToDestroy));
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 销毁spawner时出错: " + e.Message);
            }
        }

        /// <summary>
        /// 分帧销毁已禁用的 spawner root：保留其灯光后销毁，每帧处理少量，
        /// 避免在过图帧一次性 Destroy 大量 GameObject 造成尖峰。
        /// </summary>
        private IEnumerator DestroySpawnerRootsAcrossFrames(List<CharacterSpawnerRoot> rootsToDestroy)
        {
            const int batchSize = 4;       // 每帧处理的 root 数量
            int destroyedCount = 0;
            int preservedLightsCount = 0;
            int processedInBatch = 0;

            for (int i = 0; i < rootsToDestroy.Count; i++)
            {
                CharacterSpawnerRoot root = rootsToDestroy[i];
                if (root == null || root.gameObject == null)
                {
                    continue;
                }

                try
                {
                    Light[] lights = root.gameObject.GetComponentsInChildren<Light>(true);
                    if (lights != null && lights.Length > 0)
                    {
                        foreach (var light in lights)
                        {
                            if (light == null || light.gameObject == null) continue;
                            light.transform.SetParent(null);
                            preservedLightsCount++;
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] DisableAllSpawners 保留灯光失败: " + e.Message);
                }

                try
                {
                    UnityEngine.Object.Destroy(root.gameObject);
                    destroyedCount++;
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] DisableAllSpawners 销毁 spawner 失败: " + e.Message);
                }

                processedInBatch++;
                if (processedInBatch >= batchSize)
                {
                    processedInBatch = 0;
                    yield return null;  // 下一帧继续
                }
            }

            DevLog("[BossRush] 分帧销毁完成：销毁 " + destroyedCount + " 个 CharacterSpawnerRoot，保留了 " + preservedLightsCount + " 个灯光");
        }

        /// <summary>
        /// 定期自检：如果当前波计数大于0但场上已没有任何由BossRush生成的存活Boss，则强制修正并推进波次
        /// </summary>
        private void TryFixStuckWaveIfNoBossAlive()
        {
            try
            {
                if (!IsActive)
                {
                    return;
                }

                int aliveBossCount = 0;
                bool hasWaveToCheck = false;

                if (bossesPerWave > 1)
                {
                    if (bossesInCurrentWaveRemaining <= 0)
                    {
                        return;
                    }

                    if (currentWaveBosses != null && currentWaveBosses.Count > 0)
                    {
                        hasWaveToCheck = true;

                        for (int i = 0; i < currentWaveBosses.Count; i++)
                        {
                            MonoBehaviour boss = currentWaveBosses[i];
                            if (boss == null)
                            {
                                continue;
                            }

                            try
                            {
                                Health h = boss.GetComponent<Health>();
                                if (h != null && !h.IsDead)
                                {
                                    aliveBossCount++;
                                }
                            }
                            catch (Exception e)
                            {
                                DevLog("[BossRush] [WARNING] TryFixStuckWaveIfNoBossAlive 读取多Boss Health失败: " + e.Message);
                            }
                        }
                    }
                }
                else
                {
                    MonoBehaviour bossMb = null;
                    try
                    {
                        bossMb = currentBoss as MonoBehaviour;
                    }
                    catch (Exception e)
                    {
                        DevLog("[BossRush] [WARNING] TryFixStuckWaveIfNoBossAlive 读取当前Boss失败: " + e.Message);
                    }

                    if (bossMb == null)
                    {
                        return;
                    }

                    hasWaveToCheck = true;

                    try
                    {
                        Health h = bossMb.GetComponent<Health>();
                        if (h != null && !h.IsDead)
                        {
                            aliveBossCount = 1;
                        }
                    }
                    catch (Exception e)
                    {
                        DevLog("[BossRush] [WARNING] TryFixStuckWaveIfNoBossAlive 读取当前Boss Health失败: " + e.Message);
                    }
                }

                if (!hasWaveToCheck)
                {
                    return;
                }

                if (aliveBossCount <= 0)
                {
                    try
                    {
                        DevLog("[BossRush] 自检：当前波没有任何存活 Boss，自动修正并推进下一波");
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogWarning("[BossRush] TryFixStuckWaveIfNoBossAlive 日志记录失败: " + e.Message);
                    }

                    if (bossesPerWave > 1)
                    {
                        bossesInCurrentWaveRemaining = 0;
                    }

                    ProceedAfterWaveFinished();
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] TryFixStuckWaveIfNoBossAlive 错误: " + e.Message);
            }
        }
    }
}
