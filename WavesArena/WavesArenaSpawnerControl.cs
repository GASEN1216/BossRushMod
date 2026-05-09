using System;
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
                int destroyedCount = 0;
                int preservedLightsCount = 0;

                if (!_createdFieldCached)
                {
                    _cachedCreatedField = typeof(CharacterSpawnerRoot).GetField("created",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    _createdFieldCached = true;
                }

                CharacterSpawnerRoot[] spawnerRoots = UnityEngine.Object.FindObjectsOfType<CharacterSpawnerRoot>();

                if (spawnerRoots != null)
                {
                    foreach (var root in spawnerRoots)
                    {
                        if (root == null || root.gameObject == null) continue;

                        if (_cachedCreatedField != null)
                        {
                            try
                            {
                                _cachedCreatedField.SetValue(root, true);
                                disabledCount++;
                            }
                            catch {}
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
                        catch {}

                        try
                        {
                            UnityEngine.Object.Destroy(root.gameObject);
                            destroyedCount++;
                        }
                        catch {}
                    }
                }

                spawnersDisabled = true;
                DevLog("[BossRush] 已禁用并销毁 " + destroyedCount + " 个 CharacterSpawnerRoot，保留了 " + preservedLightsCount + " 个灯光");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 销毁spawner时出错: " + e.Message);
            }
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
                            catch {}
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
                    catch {}

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
                    catch {}
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
                    catch {}

                    if (bossesPerWave > 1)
                    {
                        bossesInCurrentWaveRemaining = 0;
                    }

                    ProceedAfterWaveFinished();
                }
            }
            catch {}
        }
    }
}
