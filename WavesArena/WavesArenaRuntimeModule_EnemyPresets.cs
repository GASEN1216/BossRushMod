using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Duckov;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        /// <summary>
        /// 初始化敌人预设列表 - 动态识别所有显示名字的敌人
        /// [性能优化] 添加初始化标记，避免每次传送都重复扫描
        /// </summary>
        internal void InitializeEnemyPresets()
        {
            // [性能优化] 如果已经初始化过，跳过重复扫描
            if (EnemyPresetsInitialized && EnemyPresets != null && EnemyPresets.Count > 0)
            {
                if (!owner.IsActive && !owner.IsModeDActive && !owner.IsModeEActive && !owner.IsModeFActive)
                {
                    int removed = PruneNonBossEnemyPresetsFromCache();
                    if (removed > 0)
                    {
                        owner.ResetBossPoolFilterStateForArena();
                    }
                }

                ModBehaviour.DevLog("[BossRush] 敌人预设已初始化，跳过重复扫描 (共 " + EnemyPresets.Count + " 个)");
                return;
            }

            EnemyPresetInitializationScanCount++;
            EnemyPresets.Clear();;

            // 获取所有可能的敌人类型
            var enemyTypes = new List<EnemyPresetInfo>();

            // 仅通过游戏内的角色预设动态发现敌人类型
            TryDiscoverAdditionalEnemies(enemyTypes);

            // 按团队类型和基础生命值排序，使用排除法过滤（排除玩家和中立阵营）
            // 这样可以兼容其他mod添加的自定义敌对阵营
            EnemyPresets = enemyTypes
                .Where(e =>
                    e.team != (int)Teams.player    // 排除玩家阵营
                    && e.team != (int)Teams.middle // 排除中立阵营
                    && e.baseHealth > 100f)
                .OrderBy(e => e.team)
                .ThenBy(e => e.baseHealth)
                .ToList();

            // 注册龙裔遗族Boss
            owner.RegisterDragonDescendantPresetForArena();

            // 注册龙王Boss
            owner.RegisterDragonKingPresetForArena();

            // 注册幽灵女巫Boss
            owner.RegisterPhantomWitchPresetForArena();

            PruneNonBossEnemyPresetsFromCache();

            // 计算 Boss 池基础血量范围
            try
            {
                if (EnemyPresets != null && EnemyPresets.Count > 0)
                {
                    float minH = float.MaxValue;
                    float maxH = 0f;
                    for (int i = 0; i < EnemyPresets.Count; i++)
                    {
                        float h = EnemyPresets[i].baseHealth;
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
                        MinBossBaseHealth = minH;
                        MaxBossBaseHealth = maxH;
                        ModBehaviour.DevLog("[BossRush] Boss池基础血量范围: " + MinBossBaseHealth + " ~ " + MaxBossBaseHealth);
                    }
                }
            }
            catch {}

            ModBehaviour.DevLog("[BossRush] 初始化完成，共发现 " + EnemyPresets.Count + " 个敌人类型");

            // [性能优化] 标记初始化完成，后续传送不再重复扫描
            EnemyPresetsInitialized = true;

            // 遗种巢与图鉴目录都可能早于这张表构建。池填满后必须并联刷新，
            // 否则官方 Boss 会从血脉目录或图鉴分母中缺失。
            try
            {
                owner.NotifyArenaPresetCatalogsRefreshed();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 预设初始化后刷新玩法目录失败: " + e.Message);
            }
        }

        /// <summary>
        /// 内容目录共用：确保 Boss 预设池在**基地**就已填充。
        ///
        /// 为什么必须有它（CR-2026-08-29-015）：InitializeEnemyPresets 的全部调用点都在
        /// 进竞技场路径与调试面板，基地启动一处都不触发；而血脉目录的资格口径正是这张池
        /// （GetFilteredEnemyPresets 在 EnemyPresets==null 时返回空表）。于是每次重启会话后、
        /// 进第一次竞技场之前，官方血脉在基地全面不可用：蛋孵出 lineage_unknown、
        /// 巢页显示裸 Cname_* key、遗魂账本官方血脉整行缺失、凝蛋按钮消失。
        ///
        /// 幂等：池已填充时零成本返回，绝不重复扫描；未填充时走 InitializeEnemyPresets
        /// 自身的完整流程（它填完会回调 PetNestRuntime.NotifyEnemyPresetsRefreshed 重建目录，
        /// 因此这里不需要、也不应该再手动建一次目录）。
        ///
        /// 门控在调用侧（AGENTS.md 4.12）：只有图鉴或遗种巢至少一个消费者已启用时
        /// 才请求预热；池已填充后为 O(1) 早返，同一进程只做一次实际扫描。
        /// </summary>
        internal bool EnsureEnemyPresetsReadyForGameplayCatalogs()
        {
            try
            {
                if (EnemyPresetsInitialized && EnemyPresets != null && EnemyPresets.Count > 0)
                {
                    return true;
                }

                InitializeEnemyPresets();

                // 与进竞技场路径、Boss 池窗口路径同一套接法：池填好后必须把玩家配置的
                // 禁用名单加载进来，否则基地侧目录会包含玩家已禁用的 Boss，
                // 直到进一次竞技场才收敛（它自身幂等，并会 Invalidate 触发目录重建）。
                if (!owner.IsBossPoolFilterInitializedForArena && EnemyPresets != null && EnemyPresets.Count > 0)
                {
                    owner.InitializeBossPoolFilterForArena();
                }

                return EnemyPresets != null && EnemyPresets.Count > 0;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 基地侧 Boss 预设池预热失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 无间炼狱模式下按权重随机选取一个敌人预设
        /// 权重根据基础血量与波次线性放大，高血量Boss在后期权重更高
        /// 同时应用用户设置的无间炼狱因子作为权重乘数
        /// </summary>
        internal EnemyPresetInfo PickRandomEnemyForInfiniteHell()
        {
            // 使用过滤后的 Boss 列表
            var filteredPresets = owner.GetFilteredEnemyPresets();
            if (filteredPresets == null || filteredPresets.Count == 0)
            {
                return null;
            }

            float refMin = MinBossBaseHealth;
            float refMax = MaxBossBaseHealth;

            // 如果没有有效范围，退化为按因子权重随机
            if (!(refMax > refMin && refMin > 0f))
            {
                // 即使没有血量范围，也应用用户设置的因子
                float totalFactorWeight = 0f;
                float[] factorWeights = new float[filteredPresets.Count];
                for (int i = 0; i < filteredPresets.Count; i++)
                {
                    float factor = owner.GetBossInfiniteHellFactor(filteredPresets[i].name);
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
            float waveTerm = (float)InfiniteHellWaveIndex / 50f;

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
                float userFactor = owner.GetBossInfiniteHellFactor(filteredPresets[i].name);
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


        internal static bool IsRuntimeCharacterPresetClone(CharacterRandomPreset preset)
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
            if (EnemyPresets == null || EnemyPresets.Count == 0)
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
                for (int i = EnemyPresets.Count - 1; i >= 0; i--)
                {
                    EnemyPresetInfo preset = EnemyPresets[i];
                    if (preset == null || string.IsNullOrEmpty(preset.name))
                    {
                        continue;
                    }

                    if (owner.IsManagedBossPresetForArena(preset))
                    {
                        continue;
                    }

                    if (IsBossPoolHardExcludedPresetName(preset.name))
                    {
                        EnemyPresets.RemoveAt(i);
                        removed++;
                        ModBehaviour.DevLog("[BossRush] 已从 Boss 池缓存中移除预设名硬排除的非 Boss 预设: " + preset.name + " (" + preset.displayName + ")");
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

                    EnemyPresets.RemoveAt(i);
                    removed++;
                }

                if (removed > 0)
                {
                    ModBehaviour.DevLog("[BossRush] 已从 Boss 池缓存中移除 " + removed + " 个非 Boss 预设");
                }

                return removed;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 清理 Boss 池缓存中的误判小怪失败: " + e.Message);
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
                            ModBehaviour.DevLog("[BossRush] 已跳过预设名硬排除的非 Boss 预设: " + nameKey + " (" + displayName + ")");
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
                        ModBehaviour.DevLog("[BossRush] 发现额外敌人类型: " + nameKey + " (team=" + team + ", health=" + health + ")");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] 动态发现敌人时出现异常: " + e.Message);
            }
        }

        // [性能优化] 本地化 ToPlainText 的反射结果缓存：此前每个 preset 都做一次
        // Type.GetType + GetMethod（过图进竞技场时逐 preset 调用），现解析一次复用。
        private static System.Reflection.MethodInfo _cachedToPlainTextMethod;
        private static bool _toPlainTextResolved;

        internal string GetLocalizedCharacterName(string nameKey)
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
