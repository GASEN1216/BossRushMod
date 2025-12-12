// ============================================================================
// ModeDWaves.cs - Mode D 波次管理
// ============================================================================
// 模块说明：
//   管理 Mode D 模式的波次系统，包括敌人生成、波次进度、难度递增等。
//   
// 波次规则：
//   - 第 1-5 波：全小怪
//   - 第 6-10 波：1 个 Boss + 小怪
//   - 第 11-15 波：2 个 Boss + 小怪
//   - 第 16+ 波：全 Boss
//   
// 主要功能：
//   - 开始下一波敌人
//   - 计算每波的 Boss/小怪配比
//   - 生成敌人并应用波次强化
//   - 处理敌人死亡和波次完成事件
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// Mode D 波次管理模块
    /// <para>无限波次，按波次决定小怪/Boss配比，每波提升 3% 属性</para>
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// Mode D: 开始下一波
        /// </summary>
        public void ModeDStartNextWave()
        {
            try
            {
                if (!modeDActive)
                {
                    DevLog("[ModeD] ModeDStartNextWave: Mode D 未激活");
                    return;
                }

                SetBossRushRuntimeActive(true);
                
                // 检查当前波是否还有敌人存活
                CleanupDeadEnemies();
                if (modeDCurrentWaveEnemies.Count > 0)
                {
                    ShowMessage("当前波次还有 " + modeDCurrentWaveEnemies.Count + " 个敌人存活！");
                    return;
                }
                
                modeDWaveIndex++;
                // 重置波次完成标志，允许下一波完成时触发
                modeDWaveCompletePending = false;
                DevLog("[ModeD] 开始第 " + modeDWaveIndex + " 波");

                // 每次开新波前动态刷新每波敌人数
                if (config != null && config.modeDEnemiesPerWave > 0)
                {
                    modeDEnemiesPerWave = Mathf.Clamp(config.modeDEnemiesPerWave, 1, 10);
                }
                
                // 计算本波的 Boss 数量和小怪数量
                int totalEnemies = modeDEnemiesPerWave;
                int bossCount = GetModeDWaveBossCount(modeDWaveIndex, totalEnemies);
                int minionCount = totalEnemies - bossCount;
                
                DevLog("[ModeD] 波次 " + modeDWaveIndex + ": 总敌人=" + totalEnemies + 
                       ", Boss=" + bossCount + ", 小怪=" + minionCount);
                
                // 显示波次横幅
                ShowBigBanner("第 <color=yellow>" + modeDWaveIndex + "</color> 波开始！");
                
                // 生成敌人
                SpawnModeDWaveEnemies(bossCount, minionCount);
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] ModeDStartNextWave 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 根据波次计算 Boss 数量
        /// </summary>
        /// <remarks>
        /// 波次规则：
        /// <list type="bullet">
        /// <item>第 1-5 波：全小怪（0 个 Boss）</item>
        /// <item>第 6-10 波：1 个 Boss</item>
        /// <item>第 11-15 波：2 个 Boss</item>
        /// <item>第 16+ 波：全 Boss</item>
        /// </list>
        /// </remarks>
        /// <param name="waveIndex">当前波次索引</param>
        /// <param name="totalEnemies">本波总敌人数</param>
        /// <returns>Boss 数量</returns>
        private int GetModeDWaveBossCount(int waveIndex, int totalEnemies)
        {
            if (waveIndex <= 5)
            {
                // 第1-5波：全小怪
                return 0;
            }
            else if (waveIndex <= 10)
            {
                // 第6-10波：1个Boss（或按比例）
                return Mathf.Min(1, totalEnemies);
            }
            else if (waveIndex <= 15)
            {
                // 第11-15波：2个Boss（或按比例）
                return Mathf.Min(2, totalEnemies);
            }
            else
            {
                // 第16+波：全Boss
                return totalEnemies;
            }
        }
        
        /// <summary>
        /// 生成 Mode D 波次敌人
        /// </summary>
        private void SpawnModeDWaveEnemies(int bossCount, int minionCount)
        {
            try
            {
                modeDCurrentWaveEnemies.Clear();
                
                CharacterMainControl playerMain = CharacterMainControl.Main;
                if (playerMain == null)
                {
                    Debug.LogError("[ModeD] 未找到玩家");
                    return;
                }
                
                List<Vector3> usedPositions = new List<Vector3>();
                bool bannerShown = false;
                Vector3 bannerPos = Vector3.zero;
                
                // 生成 Boss
                for (int i = 0; i < bossCount; i++)
                {
                    EnemyPresetInfo bossPreset = GetRandomBossPreset();
                    if (bossPreset != null)
                    {
                        Vector3 spawnPos = GetUniqueSpawnPosition(usedPositions);
                        usedPositions.Add(spawnPos);
                        
                        // 第一只敌人作为方位横幅代表点
                        if (!bannerShown)
                        {
                            bannerPos = spawnPos;
                            bannerShown = true;
                        }

                        SpawnModeDEnemy(bossPreset, spawnPos, true);
                    }
                }
                
                // 生成小怪
                for (int i = 0; i < minionCount; i++)
                {
                    EnemyPresetInfo minionPreset = GetRandomMinionPreset();
                    if (minionPreset != null)
                    {
                        Vector3 spawnPos = GetUniqueSpawnPosition(usedPositions);
                        usedPositions.Add(spawnPos);
                        
                        // 如果本波没有 Boss，则用第一只小怪作为代表点
                        if (!bannerShown)
                        {
                            bannerPos = spawnPos;
                            bannerShown = true;
                        }

                        SpawnModeDEnemy(minionPreset, spawnPos, false);
                    }
                }
                
                // 显示敌人方位横幅（类似弹指可灭/无间炼狱）
                if (bannerShown)
                {
                    try
                    {
                        // 以“敌人”作为名称，使用无限波次样式（x/∞ 波）
                        ShowEnemyBanner_UIAndSigns("敌人", bannerPos, playerMain.transform.position,
                            0, 1, true, Mathf.Max(0, modeDWaveIndex - 1), 1);
                    }
                    catch {}
                }
                
                DevLog("[ModeD] 本波敌人生成完成");
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] SpawnModeDWaveEnemies 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取随机 Boss 预设
        /// </summary>
        private EnemyPresetInfo GetRandomBossPreset()
        {
            if (modeDBossPool == null || modeDBossPool.Count == 0)
            {
                DevLog("[ModeD] Boss池为空");
                return null;
            }
            
            return modeDBossPool[UnityEngine.Random.Range(0, modeDBossPool.Count)];
        }
        
        /// <summary>
        /// 获取随机小怪预设
        /// </summary>
        private EnemyPresetInfo GetRandomMinionPreset()
        {
            if (modeDMinionPool == null || modeDMinionPool.Count == 0)
            {
                DevLog("[ModeD] 小怪池为空，使用Boss池替代");
                return GetRandomBossPreset();
            }

            List<EnemyPresetInfo> pool = modeDMinionPool;

            // 第 1~2 波：只刷血量最低的敌人（拾荒者等最普通的鸭鸭敌人）
            if (modeDWaveIndex <= 2)
            {
                // 先过滤掉"???"名字的敌人
                List<EnemyPresetInfo> validMinions = new List<EnemyPresetInfo>();
                for (int i = 0; i < modeDMinionPool.Count; i++)
                {
                    EnemyPresetInfo info = modeDMinionPool[i];
                    if (info == null) continue;

                    string name = info.displayName;
                    if (name == null) name = string.Empty;

                    if (name == "???" || name == "？？？")
                    {
                        continue;
                    }

                    validMinions.Add(info);
                }

                if (validMinions.Count > 0)
                {
                    // 找出血量最低值
                    float minHealth = float.MaxValue;
                    for (int i = 0; i < validMinions.Count; i++)
                    {
                        if (validMinions[i].baseHealth < minHealth)
                        {
                            minHealth = validMinions[i].baseHealth;
                        }
                    }

                    // 筛选出血量最低的敌人（允许 10% 的误差范围，以包含同级别的敌人）
                    float healthThreshold = minHealth * 1.1f;
                    List<EnemyPresetInfo> lowestHealthMinions = new List<EnemyPresetInfo>();
                    for (int i = 0; i < validMinions.Count; i++)
                    {
                        if (validMinions[i].baseHealth <= healthThreshold)
                        {
                            lowestHealthMinions.Add(validMinions[i]);
                        }
                    }

                    if (lowestHealthMinions.Count > 0)
                    {
                        pool = lowestHealthMinions;
                        DevLog("[ModeD] 前两波限制：只刷血量最低的敌人，可选数量=" + pool.Count + ", 血量阈值=" + healthThreshold);
                    }
                    else
                    {
                        pool = validMinions;
                    }
                }
            }
            // 第 3~5 波：过滤掉"???"名字的敌人，但不限制血量
            else if (modeDWaveIndex <= 5)
            {
                List<EnemyPresetInfo> filtered = new List<EnemyPresetInfo>();
                for (int i = 0; i < modeDMinionPool.Count; i++)
                {
                    EnemyPresetInfo info = modeDMinionPool[i];
                    if (info == null) continue;

                    string name = info.displayName;
                    if (name == null) name = string.Empty;

                    if (name == "???" || name == "？？？")
                    {
                        continue; // 过滤掉"???" 名字的小怪
                    }

                    filtered.Add(info);
                }

                if (filtered.Count > 0)
                {
                    pool = filtered;
                }
            }
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        /// <summary>
        /// 获取不重复的刷新位置
        /// </summary>
        private Vector3 GetUniqueSpawnPosition(List<Vector3> usedPositions)
        {
            const int maxAttempts = 20;
            const float minDistance = 3f;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int index = UnityEngine.Random.Range(0, ArenaSpawnPoints.Length);
                Vector3 candidate = GetSafeBossSpawnPosition(ArenaSpawnPoints[index]);

                bool tooClose = false;
                foreach (Vector3 used in usedPositions)
                {
                    if (Vector3.Distance(candidate, used) < minDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    return candidate;
                }
            }

            // 实在找不到，随机返回一个
            int fallbackIndex = UnityEngine.Random.Range(0, ArenaSpawnPoints.Length);
            return GetSafeBossSpawnPosition(ArenaSpawnPoints[fallbackIndex]);
        }

        /// <summary>
        /// 生成 Mode D 敌人（带最多 5 次重试）
        /// </summary>
        private async void SpawnModeDEnemy(EnemyPresetInfo preset, Vector3 position, bool isBoss)
        {
            const int maxAttempts = 5;
            EnemyPresetInfo currentPresetInfo = preset;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (currentPresetInfo == null)
                    {
                        currentPresetInfo = isBoss ? GetRandomBossPreset() : GetRandomMinionPreset();
                    }

                    if (currentPresetInfo == null)
                    {
                        DevLog("[ModeD] SpawnModeDEnemy: 预设为空, attempt=" + attempt + ", isBoss=" + isBoss);
                        continue;
                    }

                    DevLog("[ModeD] 生成敌人尝试: " + currentPresetInfo.displayName + " (isBoss=" + isBoss + ", attempt=" + attempt + ")");

                    // 查找预设
                    var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                    CharacterRandomPreset targetPreset = null;

                    foreach (var p in allPresets)
                    {
                        if (p == null) continue;
                        if (p.nameKey == currentPresetInfo.name)
                        {
                            targetPreset = p;
                            break;
                        }
                    }

                    if (targetPreset == null)
                    {
                        DevLog("[ModeD] 未找到预设: " + currentPresetInfo.name + " (attempt=" + attempt + ")");
                        currentPresetInfo = null;
                        continue;
                    }

                    // 生成敌人
                    int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                    var character = await targetPreset.CreateCharacterAsync(position, Vector3.forward, relatedScene, null, false);

                    if (character == null)
                    {
                        DevLog("[ModeD] 生成敌人失败: " + currentPresetInfo.displayName + " (attempt=" + attempt + ")");
                        currentPresetInfo = null;
                        continue;
                    }

                    character.gameObject.name = "ModeD_" + currentPresetInfo.displayName;

                    // 统一伤害倍率为1，避免不同Boss预设的damageMultiplier差异导致伤害过高
                    NormalizeDamageMultiplier(character);

                    // 应用 Mode D 配装
                    EquipEnemyForModeD(character, modeDWaveIndex, currentPresetInfo.baseHealth);

                    // 应用数值强化（复用现有逻辑）
                    if (config != null && config.bossStatMultiplier != 1f)
                    {
                        ApplyBossStatMultiplierModeD(character, config.bossStatMultiplier);
                    }

                    // 渐进式难度：按波次提升敌人属性
                    ApplyModeDWaveScaling(character, modeDWaveIndex);

                    // 激活敌人
                    character.gameObject.SetActive(true);

                    // 设置AI仇恨
                    try
                    {
                        CharacterMainControl playerMain = CharacterMainControl.Main;
                        if (playerMain != null && playerMain.mainDamageReceiver != null)
                        {
                            AICharacterController ai = character.GetComponentInChildren<AICharacterController>();
                            if (ai != null)
                            {
                                ai.searchedEnemy = playerMain.mainDamageReceiver;
                                ai.SetTarget(playerMain.mainDamageReceiver.transform);
                                ai.SetNoticedToTarget(playerMain.mainDamageReceiver);
                                ai.noticed = true;
                            }
                        }
                    }
                    catch {}

                    // 加入当前波敌人列表
                    modeDCurrentWaveEnemies.Add(character);

                    // 注册死亡事件
                    RegisterModeDEnemyDeath(character);

                    DevLog("[ModeD] 敌人生成成功: " + currentPresetInfo.displayName + " (attempt=" + attempt + ")");
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogError("[ModeD] SpawnModeDEnemy 尝试失败: " + e.Message);
                    // 下次重试时重新随机预设
                    currentPresetInfo = null;
                }
            }

            Debug.LogError("[ModeD] SpawnModeDEnemy 多次尝试仍然失败 (isBoss=" + isBoss + ")");
        }

        /// <summary>
        /// 将敌人的伤害倍率统一设置为 1
        /// </summary>
        /// <remarks>
        /// 原版游戏中不同 Boss 有不同的 damageMultiplier（如 1.0、1.5、2.0 等），
        /// 在白手起家模式下给玩家发放武器后，高倍率 Boss 伤害会过高，
        /// 因此需要统一为 1 以保持游戏平衡。
        /// </remarks>
        /// <param name="character">需要调整的敌人角色</param>
        private void NormalizeDamageMultiplier(CharacterMainControl character)
        {
            try
            {
                if (character == null) return;

                var item = character.CharacterItem;
                if (item == null) return;

                // 将枪械伤害倍率设置为1
                try
                {
                    Stat gunDmg = item.GetStat("GunDamageMultiplier");
                    if (gunDmg != null)
                    {
                        float oldValue = gunDmg.BaseValue;
                        gunDmg.BaseValue = 1f;
                        DevLog("[ModeD] 统一枪械伤害倍率: " + oldValue + " -> 1.0");
                    }
                }
                catch {}

                // 将近战伤害倍率设置为1
                try
                {
                    Stat meleeDmg = item.GetStat("MeleeDamageMultiplier");
                    if (meleeDmg != null)
                    {
                        float oldValue = meleeDmg.BaseValue;
                        meleeDmg.BaseValue = 1f;
                        DevLog("[ModeD] 统一近战伤害倍率: " + oldValue + " -> 1.0");
                    }
                }
                catch {}
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] NormalizeDamageMultiplier 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 应用 Mode D 波次数值强化
        /// </summary>
        private void ApplyModeDWaveScaling(CharacterMainControl character, int waveIndex)
        {
            try
            {
                // 每波提升 3% 属性
                float scale = 1f + 0.03f * Mathf.Max(0, waveIndex - 1);

                // 通过 Health 组件修改生命值
                try
                {
                    Health health = character.Health;
                    if (health != null)
                    {
                        float maxHp = health.MaxHealth;
                        float newHp = maxHp * scale;
                        // 直接设置当前血量为放大后的最大值
                        health.CurrentHealth = newHp;
                    }
                }
                catch {}

                DevLog("[ModeD] 应用波次强化: wave=" + waveIndex + ", scale=" + scale);
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] ApplyModeDWaveScaling 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 应用Boss数值倍率（暂不修改，使用现有的敌人基础属性）
        /// </summary>
        private void ApplyBossStatMultiplierModeD(CharacterMainControl character, float multiplier)
        {
            try
            {
                if (multiplier == 1f) return;

                // 通过 Health 组件修改生命值
                try
                {
                    Health health = character.Health;
                    if (health != null)
                    {
                        float maxHp = health.MaxHealth;
                        float newHp = maxHp * multiplier;
                        health.CurrentHealth = newHp;
                    }
                }
                catch {}
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] ApplyBossStatMultiplierModeD 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注册敌人死亡事件
        /// </summary>
        private void RegisterModeDEnemyDeath(CharacterMainControl enemy)
        {
            try
            {
                Health health = enemy.GetComponent<Health>();
                if (health != null)
                {
                    health.OnDeadEvent.AddListener((dmgInfo) => OnModeDEnemyDeath(enemy));
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] RegisterModeDEnemyDeath 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode D 敌人死亡处理
        /// </summary>
        private void OnModeDEnemyDeath(CharacterMainControl enemy)
        {
            try
            {
                DevLog("[ModeD] 敌人死亡: " + enemy.name);

                Vector3 deathPosition = Vector3.zero;
                try
                {
                    if (enemy != null)
                    {
                        deathPosition = enemy.transform.position;
                    }
                }
                catch {}

                try
                {
                    if (deathPosition != Vector3.zero)
                    {
                        StartCoroutine(MarkModeDLootboxAtPosition(deathPosition));
                    }
                }
                catch {}

                // 从当前波列表移除
                modeDCurrentWaveEnemies.Remove(enemy);

                // 清理已死亡的敌人引用
                CleanupDeadEnemies();

                // 检查是否波次完成
                if (modeDCurrentWaveEnemies.Count == 0)
                {
                    OnModeDWaveComplete();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] OnModeDEnemyDeath 失败: " + e.Message);
            }
        }

        private System.Collections.IEnumerator MarkModeDLootboxAtPosition(Vector3 deathPosition)
        {
            // 等待一帧，确保尸体掉落箱已经生成
            yield return null;

            InteractableLootbox[] boxes = null;
            try
            {
                boxes = UnityEngine.Object.FindObjectsOfType<InteractableLootbox>();
            }
            catch {}

            if (boxes == null)
            {
                yield break;
            }

            const float radius = 3f;

            for (int i = 0; i < boxes.Length; i++)
            {
                InteractableLootbox box = boxes[i];
                if (box == null)
                {
                    continue;
                }

                try
                {
                    try
                    {
                        if (box.GetComponentInParent<PetAI>() != null)
                        {
                            continue;
                        }
                    }
                    catch {}

                    try
                    {
                        if (box.GetComponentInParent<PetProxy>() != null)
                        {
                            continue;
                        }
                    }
                    catch {}

                    if (Vector3.Distance(box.transform.position, deathPosition) > radius)
                    {
                        continue;
                    }

                    BossRushLootboxMarker marker = box.GetComponent<BossRushLootboxMarker>();
                    if (marker == null)
                    {
                        box.gameObject.AddComponent<BossRushLootboxMarker>();
                    }

                    BossRushDeleteLootboxInteractable deleteInteract = null;
                    try
                    {
                        deleteInteract = box.gameObject.GetComponent<BossRushDeleteLootboxInteractable>();
                    }
                    catch {}

                    if (deleteInteract == null)
                    {
                        try
                        {
                            deleteInteract = box.gameObject.AddComponent<BossRushDeleteLootboxInteractable>();
                        }
                        catch {}
                    }

                    try
                    {
                        box.interactableGroup = true;

                        System.Type baseType = typeof(InteractableBase);
                        System.Reflection.FieldInfo othersField = baseType.GetField("otherInterablesInGroup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (othersField != null)
                        {
                            System.Collections.Generic.List<InteractableBase> hostList = othersField.GetValue(box) as System.Collections.Generic.List<InteractableBase>;
                            if (hostList == null)
                            {
                                hostList = new System.Collections.Generic.List<InteractableBase>();
                                othersField.SetValue(box, hostList);
                            }
                            if (deleteInteract != null && !hostList.Contains(deleteInteract))
                            {
                                hostList.Add(deleteInteract);
                            }
                        }
                    }
                    catch {}
                }
                catch {}
            }
        }

        /// <summary>
        /// 清理已死亡的敌人引用
        /// </summary>
        private void CleanupDeadEnemies()
        {
            try
            {
                modeDCurrentWaveEnemies.RemoveAll(e => e == null || e.Health.IsDead);
            }
            catch {}
        }

        /// <summary>
        /// Mode D 波次完整性自检：定期检查是否有敌人存活，如果没有则自动推进波次
        /// </summary>
        /// <remarks>
        /// 解决问题：敌人死亡事件可能丢失（瞬杀、事件触发时机等），导致波次卡住无法推进。
        /// 此方法作为兜底机制，每隔一段时间检查一次敌人存活状态。
        /// </remarks>
        private void TryFixStuckWaveIfNoModeDEnemyAlive()
        {
            try
            {
                if (!modeDActive)
                {
                    return;
                }

                // 如果当前波次索引为0，说明还没开始第一波，不需要自检
                if (modeDWaveIndex <= 0)
                {
                    return;
                }

                // 清理已死亡的敌人引用
                CleanupDeadEnemies();

                // 如果列表为空，说明当前波所有敌人都已死亡
                if (modeDCurrentWaveEnemies.Count == 0)
                {
                    DevLog("[ModeD] 自检：当前波没有任何存活敌人，自动触发波次完成");
                    OnModeDWaveComplete();
                }
                else
                {
                    // 额外检查：遍历列表中的敌人，确认是否真的存活
                    int aliveCount = 0;
                    for (int i = 0; i < modeDCurrentWaveEnemies.Count; i++)
                    {
                        CharacterMainControl enemy = modeDCurrentWaveEnemies[i];
                        if (enemy == null)
                        {
                            continue;
                        }

                        try
                        {
                            Health h = enemy.Health;
                            if (h != null && !h.IsDead)
                            {
                                aliveCount++;
                            }
                        }
                        catch {}
                    }

                    if (aliveCount <= 0)
                    {
                        DevLog("[ModeD] 自检：列表中有 " + modeDCurrentWaveEnemies.Count + " 个引用，但实际存活数为0，自动触发波次完成");
                        modeDCurrentWaveEnemies.Clear();
                        OnModeDWaveComplete();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] TryFixStuckWaveIfNoModeDEnemyAlive 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode D 波次完成
        /// </summary>
        private void OnModeDWaveComplete()
        {
            try
            {
                // 防止重复触发（敌人死亡事件和自检可能同时触发）
                if (modeDWaveCompletePending)
                {
                    return;
                }
                modeDWaveCompletePending = true;

                DevLog("[ModeD] 第 " + modeDWaveIndex + " 波完成！");

                ShowBigBanner("第 <color=yellow>" + modeDWaveIndex + "</color> 波完成！");

                bool useInteract = false;
                try
                {
                    if (config != null)
                    {
                        useInteract = config.useInteractBetweenWaves;
                    }
                }
                catch {}

                if (useInteract)
                {
                    ShowMessage("可以通过路牌开始下一波");

                    // 显示 Mode D 专用的"冲下一波"选项
                    ShowModeDNextWaveOption();
                }
                else
                {
                    float interval = 15f;
                    try
                    {
                        interval = GetWaveIntervalSeconds();
                    }
                    catch {}

                    if (interval <= 0f)
                    {
                        if (modeDActive)
                        {
                            ModeDStartNextWave();
                        }
                    }
                    else
                    {
                        int secondsInt = Mathf.RoundToInt(interval);
                        if (secondsInt < 1)
                        {
                            secondsInt = 1;
                        }

                        ShowMessage("下一波将在 " + secondsInt + " 秒后自动开始");

                        StartCoroutine(ModeDAutoNextWave(interval));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[ModeD] OnModeDWaveComplete 失败: " + e.Message);
            }
        }

        private System.Collections.IEnumerator ModeDAutoNextWave(float delay)
        {
            if (delay <= 0f)
            {
                if (modeDActive)
                {
                    ModeDStartNextWave();
                }
                yield break;
            }

            float timer = delay;
            while (timer > 0f)
            {
                try
                {
                    if (!modeDActive)
                    {
                        yield break;
                    }
                }
                catch {}

                timer -= Time.deltaTime;
                yield return null;
            }

            if (modeDActive)
            {
                ModeDStartNextWave();
            }
        }
    }
}

