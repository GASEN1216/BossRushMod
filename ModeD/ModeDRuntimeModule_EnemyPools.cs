using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Duckov.Utilities;

namespace BossRush
{
    internal sealed partial class ModeDRuntimeModule
    {
        internal void InitializeModeDEnemyPools(List<EnemyPresetInfo> bossPresets, Func<string, string> localize)
        {
            try
            {
                bool logEnabled = ModBehaviour.VerboseStartupDebugLogsEnabled;
                if (logEnabled)
                {
                    ModBehaviour.DevLog("[ModeD] 开始初始化敌人池...");
                }

                modeDBossPool = bossPresets;
                if (modeDEnemyPoolsInitialized &&
                    cachedCharacterPresets != null &&
                    cachedCharacterPresets.Count > 0 &&
                    modeDMinionPool.Count > 0)
                {
                    return;
                }

                modeDMinionPool.Clear();

                // 扫描所有 CharacterRandomPreset
                var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                if (allPresets == null || allPresets.Length == 0)
                {
                    if (logEnabled)
                    {
                        ModBehaviour.DevLog("[ModeD] [WARNING] 未找到任何 CharacterRandomPreset");
                    }
                    return;
                }

                // 构建缓存字典（一次性 O(N) 操作，后续 SpawnModeDEnemy 可 O(1) 查询）
                cachedCharacterPresets = new System.Collections.Generic.Dictionary<string, CharacterRandomPreset>();
                foreach (var preset in allPresets)
                {
                    if (preset == null || string.IsNullOrEmpty(preset.nameKey) || WavesArenaRuntimeModule.IsRuntimeCharacterPresetClone(preset)) continue;
                    if (!cachedCharacterPresets.ContainsKey(preset.nameKey))
                    {
                        cachedCharacterPresets[preset.nameKey] = preset;
                    }
                }
                if (logEnabled)
                {
                    ModBehaviour.DevLog("[ModeD] 缓存了 " + cachedCharacterPresets.Count + " 个 CharacterRandomPreset");
                }

                foreach (var preset in allPresets)
                {
                    if (preset == null || WavesArenaRuntimeModule.IsRuntimeCharacterPresetClone(preset)) continue;

                    string nameKey = preset.nameKey;
                    if (string.IsNullOrEmpty(nameKey)) continue;

                    int team = (int)preset.team;
                    // 只收集敌对阵营，排除玩家和中立阵营
                    if (team == (int)Teams.player || team == (int)Teams.middle) continue;

                    // 雇佣兵在运行时会被设置为友方（leader.IsMainCharacter），无法被玩家击杀
                    if (nameKey == "Cname_Usec")
                    {
                        if (logEnabled)
                        {
                            ModBehaviour.DevLog("[ModeD] 排除雇佣兵预设: " + nameKey);
                        }
                        continue;
                    }

                    // 排除自动炮台（固定敌人，无法移动）
                    if (nameKey == "Cname_GunTurret")
                    {
                        if (logEnabled)
                        {
                            ModBehaviour.DevLog("[ModeD] 排除自动炮台: " + nameKey);
                        }
                        continue;
                    }

                    // 排除商人和宠物类型
                    bool shouldExclude = false;

                    // 方法1：通过反射获取 characterIconType 字段
                    try
                    {
                        var iconField = typeof(CharacterRandomPreset).GetField("characterIconType",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (iconField != null)
                        {
                            object iconValue = iconField.GetValue(preset);
                            if (iconValue != null)
                            {
                                int iconType = (int)iconValue;
                                // CharacterIconTypes: merchant = 4, pet = 5
                                if (iconType == 4)
                                {
                                    shouldExclude = true;
                                    if (logEnabled)
                                    {
                                        ModBehaviour.DevLog("[ModeD] 排除商人(iconType): " + nameKey);
                                    }
                                }
                                else if (iconType == 5)
                                {
                                    shouldExclude = true;
                                    if (logEnabled)
                                    {
                                        ModBehaviour.DevLog("[ModeD] 排除宠物(iconType): " + nameKey);
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    // 方法2：通过 GetCharacterIcon() 返回的图标来判断
                    if (!shouldExclude)
                    {
                        try
                        {
                            Sprite icon = preset.GetCharacterIcon();
                            if (icon != null)
                            {
                                Sprite merchantIcon = GameplayDataSettings.UIStyle.MerchantCharacterIcon;
                                Sprite petIcon = GameplayDataSettings.UIStyle.PetCharacterIcon;
                                if (merchantIcon != null && icon == merchantIcon)
                                {
                                    shouldExclude = true;
                                    if (logEnabled)
                                    {
                                        ModBehaviour.DevLog("[ModeD] 排除商人(icon): " + nameKey);
                                    }
                                }
                                else if (petIcon != null && icon == petIcon)
                                {
                                    shouldExclude = true;
                                    if (logEnabled)
                                    {
                                        ModBehaviour.DevLog("[ModeD] 排除宠物(icon): " + nameKey);
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // 方法3：通过名字关键词排除（作为最后的保险）
                    if (!shouldExclude)
                    {
                        string lowerName = nameKey.ToLower();
                        if (lowerName.Contains("merchant") || lowerName.Contains("trader") ||
                            lowerName.Contains("shop") || lowerName.Contains("vendor") ||
                            nameKey.Contains("商人") || nameKey.Contains("商贩"))
                        {
                            shouldExclude = true;
                            if (logEnabled)
                            {
                                ModBehaviour.DevLog("[ModeD] 排除商人(name): " + nameKey);
                            }
                        }
                        else if (lowerName.Contains("pet") || nameKey.Contains("宠物"))
                        {
                            shouldExclude = true;
                            if (logEnabled)
                            {
                                ModBehaviour.DevLog("[ModeD] 排除宠物(name): " + nameKey);
                            }
                        }
                    }

                    if (shouldExclude) continue;

                    // 排除载具类型（包括炮台）
                    if (preset.isVehicle)
                    {
                        if (logEnabled)
                        {
                            ModBehaviour.DevLog("[ModeD] 排除载具/炮台: " + nameKey);
                        }
                        continue;
                    }

                    float health = (preset.health > 0f) ? preset.health : 100f;
                    float damage = preset.damageMultiplier;

                    // showName == false 的是小怪
                    if (!preset.showName)
                    {
                        // 排除已存在的
                        if (modeDMinionPool.Any(e => e.name == nameKey)) continue;

                        string displayName = localize(nameKey);

                        var minionInfo = new EnemyPresetInfo
                        {
                            name = nameKey,
                            displayName = displayName,
                            team = team,
                            baseHealth = health,
                            baseDamage = damage
                        };

                        modeDMinionPool.Add(minionInfo);
                        if (logEnabled)
                        {
                            ModBehaviour.DevLog("[ModeD] 添加小怪: " + nameKey + " (health=" + health + ")");
                        }
                    }
                }

                // Boss池复用现有的 bossPresets（已在 TryDiscoverAdditionalEnemies 中填充）
                modeDBossPool = bossPresets;

                int bossCount = (modeDBossPool != null) ? modeDBossPool.Count : 0;
                modeDEnemyPoolsInitialized = cachedCharacterPresets != null &&
                    cachedCharacterPresets.Count > 0 &&
                    (modeDMinionPool.Count > 0 || bossCount > 0);
                if (logEnabled)
                {
                    ModBehaviour.DevLog("[ModeD] 敌人池初始化完成: 小怪=" + modeDMinionPool.Count + ", Boss=" + bossCount);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] InitializeModeDEnemyPools 失败: " + e.Message);
            }
        }

    }
}
