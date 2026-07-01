// ============================================================================
// WavesArena.cs - 波次与竞技场管理
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的波次系统和竞技场逻辑，包括：
//   - 波次敌人生成和管理
//   - 玩家传送到官方挑战场景
//   - 波次间隔倒计时
//
// 主要功能：
//   - StartBossRush: 开始 BossRush 模式
//   - TeleportToBossRushAsync: 异步传送到竞技场
//   - SpawnNextEnemy: 生成下一波敌人
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;

namespace BossRush
{
    /// <summary>
    /// 波次与竞技场管理模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 前期波次Boss排除

        /// <summary>
        /// 前期波次需要排除的强力 Boss 名称列表
        /// 包括：口口口口、四骑士、龙裔遗族和焚天龙皇
        /// </summary>
        private static readonly HashSet<string> EarlyWaveExcludedBosses = new HashSet<string>
        {
            "Cname_StormBoss1",    // 口口口口 或 四骑士
            "Cname_StormBoss2",    // 口口口口 或 四骑士
            "Cname_StormBoss3",    // 口口口口 或 四骑士
            "Cname_StormBoss4",    // 口口口口 或 四骑士
            "Cname_StormBoss5",    // 口口口口 或 四骑士
            "DragonDescendant",    // 龙裔遗族
            "boss_dragonking",     // 焚天龙皇
        };

        /// <summary>
        /// 检查是否是前期波次需要排除的强力Boss
        /// </summary>
        private bool IsEarlyWaveExcludedBoss(string bossName)
        {
            if (string.IsNullOrEmpty(bossName)) return false;
            return EarlyWaveExcludedBosses.Contains(bossName);
        }

        /// <summary>
        /// 预处理：确保前20波不出现强力Boss
        /// 在挑战开始时调用一次，将前20位中的强力Boss与后面的普通Boss交换
        /// </summary>
        private void EnsureEarlyWavesNoStrongBoss()
        {
            if (enemyPresets == null || enemyPresets.Count <= 20) return;

            int swapCount = 0;
            int nextSwapTarget = 20; // 从第20位开始找可交换的普通Boss

            for (int i = 0; i < 20 && i < enemyPresets.Count; i++)
            {
                if (!IsEarlyWaveExcludedBoss(enemyPresets[i].name)) continue;

                // 找一个第10位之后的普通Boss来交换
                while (nextSwapTarget < enemyPresets.Count &&
                       IsEarlyWaveExcludedBoss(enemyPresets[nextSwapTarget].name))
                {
                    nextSwapTarget++;
                }

                if (nextSwapTarget >= enemyPresets.Count) break; // 没有可交换的了

                // 交换
                var tmp = enemyPresets[i];
                enemyPresets[i] = enemyPresets[nextSwapTarget];
                enemyPresets[nextSwapTarget] = tmp;
                nextSwapTarget++;
                swapCount++;
            }

            if (swapCount > 0)
            {
                DevLog("[BossRush] 前20波强力Boss预处理完成，交换了 " + swapCount + " 个Boss");
            }
        }

        #endregion

        public void StartNextWaveCountdown(bool showInitialBanner = true, bool suppressImmediateRepeatBanner = false)
        {
            float interval = GetWaveIntervalSeconds();
            bool milestoneBonusApplied = false;

            // 每5波额外休息时间
            float milestoneBonus = GetMilestoneRestBonusSeconds();
            if (milestoneBonus > 0f)
            {
                // 模式A/B: currentEnemyIndex 已在 ProceedAfterWaveFinished 中自增，代表已完成波数
                // 模式C: infiniteHellWaveIndex 已在 OnInfiniteHellWaveCompleted 中自增，代表已完成波数
                int completedWave = infiniteHellMode ? infiniteHellWaveIndex : currentEnemyIndex;
                if (completedWave > 0 && completedWave % 5 == 0)
                {
                    interval += milestoneBonus;
                    milestoneBonusApplied = true;
                    DevLog("[BossRush] 第 " + completedWave + " 波完成，额外休息 " + milestoneBonus + " 秒");
                }
            }

            if (!infiniteHellMode)
            {
                try
                {
                    nextWaveBossName = null;
                    // 使用过滤后的 Boss 列表，确保预告的 Boss 与实际生成的一致
                    var filteredPresets = GetFilteredEnemyPresets();
                    int presetCount = (filteredPresets != null) ? filteredPresets.Count : 0;
                    if (currentEnemyIndex >= 0 && currentEnemyIndex < presetCount)
                    {
                        EnemyPresetInfo nextPreset = filteredPresets[currentEnemyIndex];
                        if (nextPreset != null)
                        {
                            nextWaveBossName = nextPreset.displayName;
                        }
                    }
                }
                catch
                {
                    nextWaveBossName = null;
                }
            }
            else
            {
                nextWaveBossName = null;
            }
            if (interval <= 0f)
            {
                waitingForNextWave = false;
                lastWaveCountdownSeconds = -1;
                SpawnNextEnemy();
                return;
            }

            // 重置上一轮倒计时状态
            waitingForNextWave = true;
            waveCountdown = interval;
            int secondsInt = Mathf.RoundToInt(interval);
            if (secondsInt < 1)
            {
                secondsInt = 1;
            }

            if (showInitialBanner && (interval <= 5f || milestoneBonusApplied))
            {
                ShowNextWaveCountdownBanner(secondsInt);
                lastWaveCountdownSeconds = secondsInt;
            }
            else if (suppressImmediateRepeatBanner)
            {
                lastWaveCountdownSeconds = secondsInt;
            }
            else
            {
                lastWaveCountdownSeconds = -1;
            }
        }

        private void ShowNextWaveCountdownBanner(int secondsInt)
        {
            if (secondsInt < 1)
            {
                secondsInt = 1;
            }

            if (!infiniteHellMode && !string.IsNullOrEmpty(nextWaveBossName))
            {
                ShowBigBanner(L10n.T(
                    "<color=red>" + nextWaveBossName + "</color> 将在 <color=yellow>" + secondsInt + "</color> 秒后抵达战场...",
                    "<color=red>" + nextWaveBossName + "</color> arriving in <color=yellow>" + secondsInt + "</color> seconds..."
                ));
            }
            else
            {
                ShowBigBanner(L10n.T(
                    "下一波将在 <color=yellow>" + secondsInt + "</color> 秒后开始...",
                    "Next wave in <color=yellow>" + secondsInt + "</color> seconds..."
                ));
            }
        }

        /// <summary>
        /// 敌人死亡事件处理（带DamageInfo参数）
        /// <para>仅用于普通模式（弹指可灭/有点意思/无间炼狱），Mode D 有独立的死亡处理逻辑</para>
        /// </summary>
        private void OnEnemyDiedWithDamageInfo(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                // Mode D 有独立的敌人死亡处理（RegisterModeDEnemyDeath），不走普通模式逻辑
                // 避免 Mode D 打死敌人时误触发普通模式的通关判定
                if (modeDActive)
                {
                    return;
                }

                if (!IsActive || deadHealth == null)
                {
                    return;
                }

                CharacterMainControl deadCharacter = null;
                try
                {
                    deadCharacter = deadHealth.TryGetCharacter();
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] OnEnemyDied 读取死亡角色失败: " + e.Message);
                }

                // 多Boss模式：检查是否是当前波的其中一名Boss
                if (bossesPerWave > 1 && currentWaveBosses != null && currentWaveBosses.Count > 0)
                {
                    MonoBehaviour matchedBoss = null;
                    for (int i = 0; i < currentWaveBosses.Count; i++)
                    {
                        MonoBehaviour boss = currentWaveBosses[i];
                        if (boss == null) continue;

                        bool isDeadBoss = false;

                        try
                        {
                            CharacterMainControl bossCharacter = boss as CharacterMainControl;
                            if (bossCharacter != null && deadCharacter != null)
                            {
                                isDeadBoss = (bossCharacter == deadCharacter);
                            }
                        }
                        catch (Exception e)
                        {
                            DevLog("[BossRush] [WARNING] OnEnemyDied 比对多Boss角色失败: " + e.Message);
                        }

                        if (!isDeadBoss)
                        {
                            try
                            {
                                Health bossHealth = boss.GetComponent<Health>();
                                if (bossHealth == deadHealth || boss.gameObject == deadHealth.gameObject)
                                {
                                    isDeadBoss = true;
                                }
                            }
                            catch (Exception e)
                            {
                                DevLog("[BossRush] [WARNING] OnEnemyDied 比对多Boss Health失败: " + e.Message);
                            }
                        }

                        if (isDeadBoss)
                        {
                            matchedBoss = boss;
                            break;
                        }
                    }

                    if (matchedBoss != null)
                    {
                        DevLog("[BossRush] 当前波有一名Boss被击败");

                        // 处理Boss掉落随机化
                        CharacterMainControl bossMainControl = matchedBoss as CharacterMainControl;
                        if (bossMainControl != null)
                        {
                            HandleBossDeath(bossMainControl, damageInfo);
                        }
                    }
                }
                else
                {
                    // 单Boss模式：保持原有逻辑
                    bool isCurrentBossDead = false;

                    if (currentBoss != null)
                    {
                        try
                        {
                            CharacterMainControl currentBossCharacter = currentBoss as CharacterMainControl;
                            if (currentBossCharacter != null && deadCharacter != null)
                            {
                                isCurrentBossDead = (currentBossCharacter == deadCharacter);
                            }
                        }
                        catch (Exception e)
                        {
                            DevLog("[BossRush] [WARNING] OnEnemyDied 比对当前Boss角色失败: " + e.Message);
                        }

                        if (!isCurrentBossDead)
                        {
                            try
                            {
                                isCurrentBossDead = (deadHealth.gameObject == ((MonoBehaviour)currentBoss).gameObject);
                            }
                            catch (Exception e)
                            {
                                DevLog("[BossRush] [WARNING] OnEnemyDied 比对当前Boss对象失败: " + e.Message);
                            }
                        }
                    }

                    if (currentBoss != null && isCurrentBossDead)
                    {
                        DevLog("[BossRush] 当前敌人已击败");
                        CharacterMainControl bossMainControl = currentBoss as CharacterMainControl;
                        if (bossMainControl != null)
                        {
                            HandleBossDeath(bossMainControl, damageInfo);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] OnEnemyDied 错误: " + e.Message);
            }
        }

        private void HandleBossDeath(CharacterMainControl bossMain, DamageInfo damageInfo)
        {
            try
            {
                if (!IsActive || bossMain == null)
                {
                    return;
                }

                if (countedDeadBosses.Contains(bossMain))
                {
                    return;
                }

                countedDeadBosses.Add(bossMain);
                UnregisterEnemyRecovery(bossMain);

                // 识别 Boss 类型并触发成就（同一角色实例只计一次，避免专用死亡回调和通用死亡流重复计数）
                CheckBossKillAchievementsOnce(bossMain);

                // 无间炼狱：先累加现金池
                if (infiniteHellMode)
                {
                    try
                    {
                        float maxHp = 0f;
                        if (bossMain.Health != null)
                        {
                            maxHp = bossMain.Health.MaxHealth;
                        }
                        if (maxHp < 0f) maxHp = 0f;
                        long reward = (long)Mathf.Round(maxHp * 10f);
                        if (reward < 0L) reward = 0L;
                        infiniteHellCashPool += reward;
                        infiniteHellWaveCashThisWave += reward;
                    }
                    catch (Exception e)
                    {
                        DevLog("[BossRush] [WARNING] HandleBossDeath 计算无间炼狱现金池失败: " + e.Message);
                    }
                }

                if (bossesPerWave > 1 && currentWaveBosses != null && currentWaveBosses.Count > 0)
                {
                    for (int i = 0; i < currentWaveBosses.Count; i++)
                    {
                        MonoBehaviour boss = currentWaveBosses[i];
                        if (boss == null)
                        {
                            continue;
                        }

                        CharacterMainControl bossCharacter = null;
                        try
                        {
                            bossCharacter = boss as CharacterMainControl;
                        }
                        catch (Exception e)
                        {
                            DevLog("[BossRush] [WARNING] HandleBossDeath 读取多Boss角色失败: " + e.Message);
                        }

                        if (bossCharacter == bossMain)
                        {
                            currentWaveBosses.RemoveAt(i);
                            break;
                        }
                    }
                }

                defeatedEnemies++;

                if (bossesPerWave > 1)
                {
                    bossesInCurrentWaveRemaining = Mathf.Max(0, bossesInCurrentWaveRemaining - 1);

                    if (bossesInCurrentWaveRemaining <= 0)
                    {
                        ProceedAfterWaveFinished();
                        return;
                    }
                }
                else
                {
                    // 单Boss模式：击杀后直接推进到下一波
                    ProceedAfterWaveFinished();
                    return;
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] HandleBossDeath 错误: " + e.Message);
            }
        }

        /// <summary>
        /// 当当前波所有Boss被击杀或因生成失败/异常被跳过时，推进到下一波或结束挑战
        /// </summary>
        private void ProceedAfterWaveFinished()
        {
            try
            {
                // 通知快递员 Boss 战结束
                NotifyCourierBossFightEnd();

                // 通知快递员当前没有Boss（召唤间隔期间）
                NotifyCourierNoBoss(true);

                currentEnemyIndex++;
                currentBoss = null;

                if (infiniteHellMode)
                {
                    // 无间炼狱：统一走专用逻辑
                    OnInfiniteHellWaveCompleted();
                    return;
                }

                // 使用过滤后的 Boss 列表判断是否还有下一波
                var filteredPresets = GetFilteredEnemyPresets();
                int presetCount = (filteredPresets != null) ? filteredPresets.Count : 0;

                if (currentEnemyIndex >= presetCount)
                {
                    OnAllEnemiesDefeated();
                    return;
                }

                if (currentEnemyIndex < presetCount)
                {
                    if (config != null && config.useInteractBetweenWaves)
                    {
                        try
                        {
                            if (bossRushSignInteract != null)
                            {
                                bossRushSignInteract.SetNextWaveMode();
                            }
                        }
                        catch (Exception e)
                        {
                            DevLog("[BossRush] [WARNING] ProceedAfterWaveFinished 设置下一波交互失败: " + e.Message);
                        }
                    }
                    else
                    {
                        StartNextWaveCountdown();
                    }
                }
                else
                {
                    OnAllEnemiesDefeated();
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] ProceedAfterWaveFinished 错误: " + e.Message);
            }
        }

        /// <summary>
        /// Boss 在生成阶段失败时的统一处理：修正当前波计数并在必要时推进波次
        /// </summary>
        private void OnBossSpawnFailed(EnemyPresetInfo preset)
        {
            try
            {
                // 记录日志方便排查
                try
                {
                    string name = (preset != null ? preset.displayName : "<null>");
                    DevLog("[BossRush] OnBossSpawnFailed: Boss 生成失败, preset=" + name);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("[BossRush] OnBossSpawnFailed 日志记录失败: " + e.Message);
                }

                // 递增已击败敌人数，保持总数一致
                defeatedEnemies++;

                if (bossesPerWave > 1)
                {
                    // 多Boss模式：减少当前波剩余Boss数量
                    bossesInCurrentWaveRemaining = Mathf.Max(0, bossesInCurrentWaveRemaining - 1);

                    if (bossesInCurrentWaveRemaining <= 0)
                    {
                        ProceedAfterWaveFinished();
                    }
                }
                else
                {
                    // 单Boss模式：视为跳过该敌人，直接进入下一波
                    ProceedAfterWaveFinished();
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] OnBossSpawnFailed 错误: " + e.Message);
            }
        }

        /// <summary>
        /// 初始化敌人预设列表 - 动态识别所有显示名字的敌人
        /// [性能优化] 添加初始化标记，避免每次传送都重复扫描
        /// </summary>
        private void InitializeEnemyPresets()
        {
            // [性能优化] 如果已经初始化过，跳过重复扫描
            if (_enemyPresetsInitialized && enemyPresets != null && enemyPresets.Count > 0)
            {
                if (!IsActive && !modeDActive && !modeEActive && !modeFActive)
                {
                    int removed = PruneNonBossEnemyPresetsFromCache();
                    if (removed > 0)
                    {
                        ResetBossPoolFilterStateForEnemyPresetRefresh();
                    }
                }

                DevLog("[BossRush] 敌人预设已初始化，跳过重复扫描 (共 " + enemyPresets.Count + " 个)");
                return;
            }

            enemyPresets.Clear();;

            // 获取所有可能的敌人类型
            var enemyTypes = new List<EnemyPresetInfo>();

            // 仅通过游戏内的角色预设动态发现敌人类型
            TryDiscoverAdditionalEnemies(enemyTypes);

            // 按团队类型和基础生命值排序，使用排除法过滤（排除玩家和中立阵营）
            // 这样可以兼容其他mod添加的自定义敌对阵营
            enemyPresets = enemyTypes
                .Where(e =>
                    e.team != (int)Teams.player    // 排除玩家阵营
                    && e.team != (int)Teams.middle // 排除中立阵营
                    && e.baseHealth > 100f)
                .OrderBy(e => e.team)
                .ThenBy(e => e.baseHealth)
                .ToList();

            // 注册龙裔遗族Boss
            RegisterDragonDescendantPreset();

            // 注册龙王Boss
            RegisterDragonKingPreset();

            // 注册幽灵女巫Boss
            RegisterPhantomWitchPreset();

            PruneNonBossEnemyPresetsFromCache();

            // 计算 Boss 池基础血量范围
            try
            {
                if (enemyPresets != null && enemyPresets.Count > 0)
                {
                    float minH = float.MaxValue;
                    float maxH = 0f;
                    for (int i = 0; i < enemyPresets.Count; i++)
                    {
                        float h = enemyPresets[i].baseHealth;
                        if (h <= 0f)
                        {
                            continue;
                        }
                        if (h < minH)
                        {
                            minH = h;
                        }
                        if (h > maxH)
                        {
                            maxH = h;
                        }
                    }

                    if (minH < float.MaxValue && maxH > 0f && maxH >= minH)
                    {
                        minBossBaseHealth = minH;
                        maxBossBaseHealth = maxH;
                        DevLog("[BossRush] Boss池基础血量范围: " + minBossBaseHealth + " ~ " + maxBossBaseHealth);
                    }
                }
            }
            catch {}

            DevLog("[BossRush] 初始化完成，共发现 " + enemyPresets.Count + " 个敌人类型");

            // [性能优化] 标记初始化完成，后续传送不再重复扫描
            _enemyPresetsInitialized = true;
        }

        /// <summary>
        /// 无间炼狱模式下按权重随机选取一个敌人预设
        /// 权重根据基础血量与波次线性放大，高血量Boss在后期权重更高
        /// 同时应用用户设置的无间炼狱因子作为权重乘数
        /// </summary>
        private EnemyPresetInfo PickRandomEnemyForInfiniteHell()
        {
            // 使用过滤后的 Boss 列表
            var filteredPresets = GetFilteredEnemyPresets();
            if (filteredPresets == null || filteredPresets.Count == 0)
            {
                return null;
            }

            float refMin = minBossBaseHealth;
            float refMax = maxBossBaseHealth;

            // 如果没有有效范围，退化为按因子权重随机
            if (!(refMax > refMin && refMin > 0f))
            {
                // 即使没有血量范围，也应用用户设置的因子
                float totalFactorWeight = 0f;
                float[] factorWeights = new float[filteredPresets.Count];
                for (int i = 0; i < filteredPresets.Count; i++)
                {
                    float factor = GetBossInfiniteHellFactor(filteredPresets[i].name);
                    factorWeights[i] = factor;
                    totalFactorWeight += factor;
                }

                if (totalFactorWeight <= 0f)
                {
                    int idx = UnityEngine.Random.Range(0, filteredPresets.Count);
                    return filteredPresets[idx];
                }

                float rFactor = UnityEngine.Random.value * totalFactorWeight;
                float accFactor = 0f;
                for (int i = 0; i < filteredPresets.Count; i++)
                {
                    accFactor += factorWeights[i];
                    if (rFactor <= accFactor)
                    {
                        return filteredPresets[i];
                    }
                }
                return filteredPresets[filteredPresets.Count - 1];
            }

            // 计算每个Boss的权重
            float totalWeight = 0f;
            float[] weights = new float[filteredPresets.Count];
            // 基础系数：t * baseK + (wave/50)*t，t 为基础血量归一化
            const float baseK = 4f;
            float waveTerm = (float)infiniteHellWaveIndex / 50f;

            for (int i = 0; i < filteredPresets.Count; i++)
            {
                float h = filteredPresets[i].baseHealth;
                if (h <= 0f)
                {
                    h = refMin;
                }

                float t = Mathf.Clamp01((h - refMin) / (refMax - refMin));
                float w = 1f + t * baseK + waveTerm * t;
                if (w < 0.01f)
                {
                    w = 0.01f;
                }

                // 应用用户设置的无间炼狱因子作为权重乘数
                float userFactor = GetBossInfiniteHellFactor(filteredPresets[i].name);
                w *= userFactor;

                weights[i] = w;
                totalWeight += w;
            }

            if (totalWeight <= 0f)
            {
                int idx = UnityEngine.Random.Range(0, filteredPresets.Count);
                return filteredPresets[idx];
            }

            // 按累计权重抽样
            float r = UnityEngine.Random.value * totalWeight;
            float acc = 0f;
            for (int i = 0; i < filteredPresets.Count; i++)
            {
                acc += weights[i];
                if (r <= acc)
                {
                    return filteredPresets[i];
                }
            }

            // 理论上不会到这里，兜底返回最后一个
            return filteredPresets[filteredPresets.Count - 1];
        }


        private static bool IsRuntimeCharacterPresetClone(CharacterRandomPreset preset)
        {
            if (preset == null)
            {
                return false;
            }

            string runtimeName = null;
            try { runtimeName = preset.name; } catch { }

            return !string.IsNullOrEmpty(runtimeName) &&
                   runtimeName.IndexOf("(Clone)", StringComparison.Ordinal) >= 0;
        }

        private static bool IsBossPoolSpecialNoShowNamePreset(string nameKey)
        {
            return string.Equals(nameKey, "Cname_Boss_Red", StringComparison.Ordinal) ||
                   string.Equals(nameKey, "Cname_Boss_Blue", StringComparison.Ordinal);
        }

        private static bool IsBossPoolHardExcludedPresetName(string presetName)
        {
            if (string.IsNullOrEmpty(presetName))
            {
                return false;
            }

            return string.Equals(presetName, "Character_Ming", StringComparison.Ordinal);
        }

        private int PruneNonBossEnemyPresetsFromCache()
        {
            if (enemyPresets == null || enemyPresets.Count == 0)
            {
                return 0;
            }

            try
            {
                var allPresets = ObjectCache.GetCharacterPresets();
                if (allPresets == null || allPresets.Length == 0)
                {
                    return 0;
                }

                var showNameByKey = new Dictionary<string, bool>(StringComparer.Ordinal);
                for (int i = 0; i < allPresets.Length; i++)
                {
                    CharacterRandomPreset preset = allPresets[i];
                    if (preset == null || IsRuntimeCharacterPresetClone(preset))
                    {
                        continue;
                    }

                    string nameKey = preset.nameKey;
                    if (string.IsNullOrEmpty(nameKey))
                    {
                        continue;
                    }

                    bool existingShowName = false;
                    if (showNameByKey.TryGetValue(nameKey, out existingShowName))
                    {
                        showNameByKey[nameKey] = existingShowName || preset.showName;
                    }
                    else
                    {
                        showNameByKey[nameKey] = preset.showName;
                    }
                }

                int removed = 0;
                for (int i = enemyPresets.Count - 1; i >= 0; i--)
                {
                    EnemyPresetInfo preset = enemyPresets[i];
                    if (preset == null || string.IsNullOrEmpty(preset.name))
                    {
                        continue;
                    }

                    if (IsManagedBossPreset(preset))
                    {
                        continue;
                    }

                    if (IsBossPoolHardExcludedPresetName(preset.name))
                    {
                        enemyPresets.RemoveAt(i);
                        removed++;
                        DevLog("[BossRush] 已从 Boss 池缓存中移除预设名硬排除的非 Boss 预设: " + preset.name + " (" + preset.displayName + ")");
                        continue;
                    }

                    bool canonicalShowName = false;
                    if (!showNameByKey.TryGetValue(preset.name, out canonicalShowName) || canonicalShowName)
                    {
                        continue;
                    }

                    if (IsBossPoolSpecialNoShowNamePreset(preset.name))
                    {
                        continue;
                    }

                    enemyPresets.RemoveAt(i);
                    removed++;
                }

                if (removed > 0)
                {
                    DevLog("[BossRush] 已从 Boss 池缓存中移除 " + removed + " 个非 Boss 预设");
                }

                return removed;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 清理 Boss 池缓存中的误判小怪失败: " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// 尝试发现额外的敌人类型
        /// </summary>
        private void TryDiscoverAdditionalEnemies(List<EnemyPresetInfo> enemyList)
        {
            try
            {
                var allPresets = ObjectCache.GetCharacterPresets();
                if (allPresets != null && allPresets.Length > 0)
                {
                    foreach (var preset in allPresets)
                    {
                        if (preset == null)
                        {
                            continue;
                        }

                        if (IsRuntimeCharacterPresetClone(preset))
                        {
                            continue;
                        }

                        string nameKey = preset.nameKey;
                        if (string.IsNullOrEmpty(nameKey))
                        {
                            continue;
                        }

                        string displayName = GetLocalizedCharacterName(nameKey);
                        bool isSpecialUnknownBoss = IsBossPoolSpecialNoShowNamePreset(nameKey);

                        if (!preset.showName && !isSpecialUnknownBoss)
                        {
                            continue;
                        }

                        if (IsBossPoolHardExcludedPresetName(nameKey))
                        {
                            DevLog("[BossRush] 已跳过预设名硬排除的非 Boss 预设: " + nameKey + " (" + displayName + ")");
                            continue;
                        }

                        if (enemyList.Any(e => e.name == nameKey))
                        {
                            continue;
                        }

                        int team = (int)preset.team;
                        float health = (preset.health > 0f) ? preset.health : 100f;
                        float damage = preset.damageMultiplier;

                        var newEnemy = new EnemyPresetInfo
                        {
                            name = nameKey,
                            displayName = displayName,
                            team = team,
                            baseHealth = health,
                            baseDamage = damage
                        };

                        enemyList.Add(newEnemy);
                        DevLog("[BossRush] 发现额外敌人类型: " + nameKey + " (team=" + team + ", health=" + health + ")");
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 动态发现敌人时出现异常: " + e.Message);
            }
        }

        // [性能优化] 本地化 ToPlainText 的反射结果缓存：此前每个 preset 都做一次
        // Type.GetType + GetMethod（过图进竞技场时逐 preset 调用），现解析一次复用。
        private static System.Reflection.MethodInfo _cachedToPlainTextMethod;
        private static bool _toPlainTextResolved;

        private string GetLocalizedCharacterName(string nameKey)
        {
            if (string.IsNullOrEmpty(nameKey))
            {
                return nameKey;
            }

            try
            {
                System.Reflection.MethodInfo method = ResolveToPlainTextMethod();
                if (method != null)
                {
                    object result = method.Invoke(null, new object[] { nameKey });
                    string str = result as string;
                    if (!string.IsNullOrEmpty(str))
                    {
                        return str;
                    }
                }
            }
            catch
            {
            }

            return nameKey;
        }

        private static System.Reflection.MethodInfo ResolveToPlainTextMethod()
        {
            if (_toPlainTextResolved)
            {
                return _cachedToPlainTextMethod;
            }

            _toPlainTextResolved = true;

            string[] types = new string[]
            {
                "SodaCraft.Localizations.LocalizationManager, SodaLocalization",
                "SodaCraft.Localizations.LocalizationManager, TeamSoda.Duckov.Core",
                "LocalizationManager, Assembly-CSharp"
            };

            Type locType = null;
            for (int i = 0; i < types.Length; i++)
            {
                locType = Type.GetType(types[i]);
                if (locType != null)
                {
                    break;
                }
            }

            if (locType != null)
            {
                _cachedToPlainTextMethod = locType.GetMethod(
                    "ToPlainText", BindingFlags.Static | BindingFlags.Public);
            }

            return _cachedToPlainTextMethod;
        }

    }
}
