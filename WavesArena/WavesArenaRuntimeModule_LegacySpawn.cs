using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.Utilities;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal delegate UniTask<CharacterMainControl> SpawnArenaDragonDescendant(Vector3 position, bool isChildProtectionSummon, bool notifyBossRushOnFailure, Func<bool> isActiveCheck);
        internal delegate UniTask<CharacterMainControl> SpawnArenaSpecialBoss(Vector3 position, bool notifyBossRushOnFailure, Func<bool> isActiveCheck);
        private Func<EnemyPresetInfo, bool> IsDragonDescendantPreset;
        private Func<EnemyPresetInfo, bool> IsDragonKingPreset;
        private Func<EnemyPresetInfo, bool> IsPhantomWitchPreset;
        private SpawnArenaDragonDescendant SpawnDragonDescendant;
        private SpawnArenaSpecialBoss SpawnDragonKing;
        private SpawnArenaSpecialBoss SpawnPhantomWitch;
        private Action<CharacterMainControl> ApplyBossStatMultiplier;

        internal void BindLegacySpawnServices(
            Func<EnemyPresetInfo, bool> descendant, Func<EnemyPresetInfo, bool> king, Func<EnemyPresetInfo, bool> witch,
            SpawnArenaDragonDescendant spawnDescendant,
            SpawnArenaSpecialBoss spawnKing, SpawnArenaSpecialBoss spawnWitch,
            Action<CharacterMainControl> multiplier)
        {
            IsDragonDescendantPreset = descendant;
            IsDragonKingPreset = king;
            IsPhantomWitchPreset = witch;
            SpawnDragonDescendant = spawnDescendant;
            SpawnDragonKing = spawnKing;
            SpawnPhantomWitch = spawnWitch;
            ApplyBossStatMultiplier = multiplier;
        }

        internal async UniTask<CharacterMainControl> SpawnEnemyAtPositionAsync(EnemyPresetInfo preset, Vector3 position, Func<bool> isActiveCheck = null)
        {
            ModBehaviour coroutineHost = owner;
            try
            {
                if (isActiveCheck != null && !isActiveCheck()) return null;
                // 检查是否是龙裔遗族Boss，使用专门的生成方法
                if (IsDragonDescendantPreset(preset))
                {
                    // 龙裔遗族使用独立生成逻辑，等待生成完成并返回结果
                    var dragonBoss = await SpawnDragonDescendant(
                        position,
                        isChildProtectionSummon: false,
                        notifyBossRushOnFailure: false,
                        isActiveCheck: isActiveCheck);
                    MutatorManager.ApplyToEnemy(dragonBoss);
                    return dragonBoss;
                }

                // 检查是否是龙王Boss，使用专门的生成方法
                if (IsDragonKingPreset(preset))
                {
                    // 龙王使用独立生成逻辑
                    var dragonKing = await SpawnDragonKing(position, notifyBossRushOnFailure: false, isActiveCheck: isActiveCheck);
                    MutatorManager.ApplyToEnemy(dragonKing);
                    return dragonKing;
                }

                // 检查是否是幽灵女巫Boss，使用专门的生成方法
                if (IsPhantomWitchPreset(preset))
                {
                    var phantomWitch = await SpawnPhantomWitch(position, notifyBossRushOnFailure: false, isActiveCheck: isActiveCheck);
                    MutatorManager.ApplyToEnemy(phantomWitch);
                    return phantomWitch;
                }

                // 查找所有CharacterRandomPreset（从Resources中查找）
                var allPresets = ObjectCache.GetCharacterPresets();
                CharacterRandomPreset targetPreset = null;

                // 优先通过本地化键（nameKey）精确匹配预设
                foreach (var p in allPresets)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(p.nameKey) && p.nameKey == preset.name)
                    {
                        targetPreset = p;
                        ModBehaviour.DevLog("[BossRush] 找到匹配的预设: " + p.name + " (nameKey=" + p.nameKey + ")");
                        break;
                    }
                }

                // 如果没找到精确匹配，找同阵营且会显示名字的预设（强敌）
                if (targetPreset == null)
                {
                    foreach (var p in allPresets)
                    {
                        if (p == null)
                        {
                            continue;
                        }

                        if (!p.showName)
                        {
                            continue;
                        }

                        var presetTeam = GetPresetTeam(p);
                        if (presetTeam == preset.team)
                        {
                            targetPreset = p;
                            ModBehaviour.DevLog("[BossRush] 使用同阵营强敌预设: " + p.name + " (nameKey=" + p.nameKey + ")");
                            break;
                        }
                    }
                }

                if (targetPreset == null)
                {
                    ModBehaviour.DevLog("[BossRush] 未找到合适的CharacterRandomPreset");
                    return null;
                }

                // 使用CharacterRandomPreset的CreateCharacterAsync方法生成敌人
                Vector3 dir = Vector3.forward;
                // 先生成非激活状态，以便修改属性
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                var character = await targetPreset.CreateCharacterAsync(position, dir, relatedScene, null, false);
                if (isActiveCheck != null && !isActiveCheck())
                {
                    if (character != null) UnityEngine.Object.Destroy(character.gameObject);
                    return null;
                }

                if (character == null)
                {
                    ModBehaviour.DevLog("[BossRush] 生成敌人失败");
                    return null;
                }

                // 海岛更新后，部分 Boss 预设的 team 变为 Teams.middle（中立），
                // 导致 Team.IsEnemy(player, middle)==false：玩家无法对其造成伤害、AI 强制追踪被忽略，
                // Boss 永不死亡 → 波次无法推进。此处兜底：若生成的 Boss 对玩家非敌对，
                // 强制改为 Teams.wolf（与龙王/龙裔 Boss 阵营一致，Mode E/F 走独立生成路径不受影响）。
                try
                {
                    // 遗种巢随从豁免（防御性不变式）：随从走自家生成桥
                    // PetNestCompanionSpawner，正常不会经过这条标准生成路径；
                    // 万一将来有人把生成改道到这里，安全网也不能把玩家方随从改成敌对。
                    if (PetNestCompanionAgent.IsCompanionCharacter(character))
                    {
                        ModBehaviour.DevLog("[BossRush] 敌对性安全网豁免遗种巢随从");
                    }
                    else if (!Team.IsEnemy(Teams.player, character.Team))
                    {
                        ModBehaviour.DevLog("[BossRush] 检测到非敌对 Boss (team=" + character.Team + ")，强制设为 Teams.wolf");
                        character.SetTeam(Teams.wolf);
                    }
                }
                catch (Exception teamEx)
                {
                    ModBehaviour.DevLog("[BossRush] 强制 Boss 阵营失败: " + teamEx.Message);
                }

                CurrentBoss = character;
                character.gameObject.name = "BossRush_" + preset.displayName;

                // 标记由 BossRush 自己生成的大兴兴 Boss，后续清理时保留
                try
                {
                    if (IsDaXingXingPreset(preset))
                    {
                        if (bossRushOwnedDaXingXing != null && !bossRushOwnedDaXingXing.Contains(character))
                        {
                            bossRushOwnedDaXingXing.Add(character);
                        }
                    }
                }
                catch {}

                // 无间炼狱：在角色生成后按当前波次进行生命值和伤害强化
                if (InfiniteHellMode)
                {
                    ApplyInfiniteHellScaling(character, preset);
                }

                // 应用全局 Boss 数值倍率（所有模式生效）
                ApplyBossStatMultiplier(character);

                // 多Boss模式下，将本次生成的敌人加入当前波列表，便于统一统计死亡
                if (BossesPerWave > 1)
                {
                    if (CurrentWaveBosses != null && !CurrentWaveBosses.Contains(character))
                    {
                        CurrentWaveBosses.Add(character);
                    }
                }

                // 激活敌人
                character.gameObject.SetActive(true);
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);

                // 应用变异词条效果到新生成的敌人
                MutatorManager.ApplyToEnemy(character);

                // 记录 Boss 生成时间和原始掉落数量（用于掉落随机化）
                try
                {
                    if (character != null)
                    {
                        int originalLootCount = 0;
                        if (character.CharacterItem != null && character.CharacterItem.Inventory != null)
                        {
                            // 记录原始库存大小作为基础掉落规模参考
                            originalLootCount = 3; // 默认基础掉落数量
                        }
                        RegisterBossRandomLootTracking(character, originalLootCount);

                        ModBehaviour.DevLog("[BossRush] 记录 Boss 生成信息并订阅掉落事件 - 时间: " + Time.time + ", 原始掉落数量: " + originalLootCount);
                    }
                }
                catch (Exception recordEx)
                {
                    ModBehaviour.DevLog("[BossRush] 记录 Boss 生成信息失败: " + recordEx.Message);
                }

                // 强制设置 AI 仇恨到玩家
                // 设置 forceTracePlayerDistance 为较大值，确保远距离生成的敌人也会追踪玩家
                var main = CharacterMainControl.Main;
                if (main != null)
                {
                    var ai = character.GetComponentInChildren<AICharacterController>();
                    if (ai != null && main.mainDamageReceiver != null)
                    {
                        // 设置强制追踪距离为 500，确保无论多远都会追踪玩家
                        ai.forceTracePlayerDistance = 500f;
                        ai.searchedEnemy = main.mainDamageReceiver;
                        ai.SetTarget(main.mainDamageReceiver.transform);
                        ai.SetNoticedToTarget(main.mainDamageReceiver);
                        ai.noticed = true;
                    }
                }

                // 延迟校验Boss位置，防止低配玩家地形加载慢导致Boss卡在地下
                coroutineHost.StartCoroutine(enemyRecoveryMonitor.DelayedBossPositionValidation(character, 0.5f));
                enemyRecoveryMonitor.RegisterEnemyRecoveryAnchor(character, position);

                ModBehaviour.DevLog("[BossRush] 成功生成敌人: " + preset.displayName + " at " + position);

                return character;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] SpawnEnemyAtPositionAsync 错误: " + e.Message + "\n" + e.StackTrace);
                return null;
            }
        }

        internal void ApplyInfiniteHellScaling(CharacterMainControl character, EnemyPresetInfo preset)
        {
            if (character == null)
            {
                return;
            }

            try
            {
                // 每波提升 2%：第 1 波为 1.00，第 2 波为 1.02，以此类推
                float scale = 1f + 0.02f * Mathf.Max(0, InfiniteHellWaveIndex);

                // 1. 提升 MaxHealth
                try
                {
                    var item = character.CharacterItem;
                    if (item != null)
                    {
                        Stat hpStat = null;
                        try
                        {
                            hpStat = item.GetStat("MaxHealth");
                        }
                        catch {}

                        if (hpStat != null)
                        {
                            hpStat.BaseValue *= scale;
                        }
                    }
                }
                catch {}

                try
                {
                    if (character.Health != null)
                    {
                        // 让当前血量等于新的最大生命
                        character.Health.SetHealth(character.Health.MaxHealth);
                    }
                }
                catch {}

                // 2. 提升攻击伤害（枪械与近战）
                try
                {
                    var item = character.CharacterItem;
                    if (item != null)
                    {
                        Stat gunDmg = null;
                        Stat meleeDmg = null;

                        try { gunDmg = item.GetStat("GunDamageMultiplier"); } catch {}
                        try { meleeDmg = item.GetStat("MeleeDamageMultiplier"); } catch {}

                        if (gunDmg != null)
                        {
                            gunDmg.BaseValue *= scale;
                        }
                        if (meleeDmg != null)
                        {
                            meleeDmg.BaseValue *= scale;
                        }
                    }
                }
                catch {}
            }
            catch {}
        }

        internal int GetPresetTeam(CharacterRandomPreset preset)
        {
            if (preset == null) return 0;
            // team是public字段，直接访问
            return (int)preset.team;
        }
    }
}
