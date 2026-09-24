using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal ZombieModeEnemyKind RollZombieModeEnemyKind()
        {
            int pollution = runState.TotalPollution;
            int pacingWave = owner.GetZombieModePacingWaveForRuntimeModule();
            float eliteWeight;
            float specialWeight;
            float normalWeight;
            if (pacingWave <= 1)
            {
                eliteWeight = 0f;
                specialWeight = 0f;
                normalWeight = 100f;
            }
            else if (pacingWave == 2)
            {
                eliteWeight = 0f;
                specialWeight = 3f;
                normalWeight = 97f;
            }
            else if (pacingWave == 3)
            {
                eliteWeight = 0f;
                specialWeight = 5f;
                normalWeight = 95f;
            }
            else if (pacingWave <= 5)
            {
                eliteWeight = 1f;
                specialWeight = 8f;
                normalWeight = 91f;
            }
            else
            {
                int lateWave = pacingWave - 5;
                eliteWeight = GetZombieModeEliteBaseWeight(pollution) +
                              lateWave * (float)ZombieModeTuning.LateWaveEliteWeightPerWave;
                specialWeight = GetZombieModeSpecialBaseWeight(pollution) +
                                lateWave * (float)ZombieModeTuning.LateWaveSpecialWeightPerWave;
                normalWeight = ZombieModeTuning.LateWaveNormalEnemyWeight;
            }

            float roll = Random.value * (eliteWeight + specialWeight + normalWeight);
            if (roll < eliteWeight)
            {
                return ZombieModeEnemyKind.Elite;
            }

            if (roll < eliteWeight + specialWeight)
            {
                return ZombieModeEnemyKind.Special;
            }

            return ZombieModeEnemyKind.Normal;
        }

        private int GetZombieModeSpecialBaseWeight(int pollution)
        {
            if (pollution >= 25) return 30;
            if (pollution >= 20) return 25;
            if (pollution >= 15) return 20;
            if (pollution >= 10) return 15;
            if (pollution >= 5) return 10;
            return 5;
        }

        private int GetZombieModeEliteBaseWeight(int pollution)
        {
            if (pollution >= 25) return 10;
            if (pollution >= 20) return 8;
            if (pollution >= 15) return 6;
            if (pollution >= 10) return 4;
            if (pollution >= 5) return 2;
            return 1;
        }

        // 静态读取以避免每次刷怪 new[] 装箱（审查 §3.7）。
        private static readonly ZombieModeSpecialKind[] s_zombieModeSpecialKindOrder = new ZombieModeSpecialKind[]
        {
            ZombieModeSpecialKind.Sprinter,
            ZombieModeSpecialKind.Exploder,
            ZombieModeSpecialKind.OfficialExploder,
            ZombieModeSpecialKind.Plague,
            ZombieModeSpecialKind.Summoner,
            ZombieModeSpecialKind.Harasser
        };

        private static readonly ZombieModeSpecialKind[] s_zombieModeEarlySpecialKindOrder = new ZombieModeSpecialKind[]
        {
            ZombieModeSpecialKind.Sprinter,
            ZombieModeSpecialKind.Plague,
            ZombieModeSpecialKind.Summoner,
            ZombieModeSpecialKind.Harasser
        };

        // System.Enum.GetValues 返回 Array 会装箱；缓存为强类型数组，避免每次 Roll 触发 GC。
        private static readonly ZombieModeEliteAffix[] s_zombieModeEliteAffixAll = (ZombieModeEliteAffix[])
            System.Enum.GetValues(typeof(ZombieModeEliteAffix));

        // RollZombieModeEliteAffixes 内的两个临时容器；Clear() + 复用避免每次 new。
        private readonly List<ZombieModeEliteAffix> rollEliteAffixesScratchSelected = new List<ZombieModeEliteAffix>();
        private readonly List<ZombieModeEliteAffix> rollEliteAffixesScratchPool = new List<ZombieModeEliteAffix>();

        internal ZombieModeSpecialKind RollZombieModeSpecialKind()
        {
            ZombieModeSpecialKind[] order = runState.CurrentWave <= 5
                ? s_zombieModeEarlySpecialKindOrder
                : s_zombieModeSpecialKindOrder;
            return order[Random.Range(0, order.Length)];
        }

        internal List<ZombieModeEliteAffix> RollZombieModeEliteAffixes()
        {
            int pollution = runState.TotalPollution;
            int desiredCount = 1;
            if (pollution >= 25)
            {
                desiredCount = Random.Range(2, 4);
            }
            else if (pollution >= 15)
            {
                desiredCount = 2;
            }
            else if (pollution >= 5 && Random.value < 0.35f)
            {
                desiredCount = 2;
            }

            // 注：调用方拿到 list 后会持久持有（marker.EliteAffixes），所以 selected 必须 new 出独立副本；
            // 但 pool 是临时枚举池，可以复用 scratch 容器。
            rollEliteAffixesScratchPool.Clear();
            for (int i = 0; i < s_zombieModeEliteAffixAll.Length; i++)
            {
                ZombieModeEliteAffix affix = s_zombieModeEliteAffixAll[i];
                if (GetZombieModeAffixUnlockTier(affix) <= runState.PollutionTier)
                {
                    rollEliteAffixesScratchPool.Add(affix);
                }
            }

            List<ZombieModeEliteAffix> selected = new List<ZombieModeEliteAffix>();
            while (selected.Count < desiredCount && rollEliteAffixesScratchPool.Count > 0)
            {
                int index = Random.Range(0, rollEliteAffixesScratchPool.Count);
                ZombieModeEliteAffix candidate = rollEliteAffixesScratchPool[index];
                rollEliteAffixesScratchPool.RemoveAt(index);
                selected.Add(candidate);
                if (!IsZombieModeAffixCombinationAllowed(selected))
                {
                    selected.Remove(candidate);
                }
            }

            if (selected.Count <= 0)
            {
                selected.Add(ZombieModeEliteAffix.Tough);
            }

            rollEliteAffixesScratchPool.Clear();
            return selected;
        }

        private int GetZombieModeAffixUnlockTier(ZombieModeEliteAffix affix)
        {
            switch (affix)
            {
                case ZombieModeEliteAffix.Swift:
                case ZombieModeEliteAffix.Frenzied:
                case ZombieModeEliteAffix.Tough:
                    return 0;
                case ZombieModeEliteAffix.Stalwart:
                case ZombieModeEliteAffix.Regenerating:
                case ZombieModeEliteAffix.Burst:
                case ZombieModeEliteAffix.Plague:
                    return 1;
                case ZombieModeEliteAffix.Commander:
                case ZombieModeEliteAffix.ToxicAura:
                case ZombieModeEliteAffix.Splitting:
                case ZombieModeEliteAffix.Shielded:
                    return 3;
                default:
                    return 5;
            }
        }

        private bool IsZombieModeAffixCombinationAllowed(List<ZombieModeEliteAffix> affixes)
        {
            if (affixes == null)
            {
                return true;
            }

            bool stalwart = affixes.Contains(ZombieModeEliteAffix.Stalwart);
            bool shielded = affixes.Contains(ZombieModeEliteAffix.Shielded);
            bool regenerating = affixes.Contains(ZombieModeEliteAffix.Regenerating);
            bool swift = affixes.Contains(ZombieModeEliteAffix.Swift);
            bool toxicAura = affixes.Contains(ZombieModeEliteAffix.ToxicAura);
            bool plague = affixes.Contains(ZombieModeEliteAffix.Plague);

            if (stalwart && shielded && regenerating)
            {
                return false;
            }

            if (stalwart && swift && runState.TotalPollution < 15)
            {
                return false;
            }

            if (toxicAura && plague && swift && runState.TotalPollution < 15)
            {
                return false;
            }

            return true;
        }

        internal int CalculateZombieModeEnemyPurificationPoints(bool isBoss, ZombieModeEnemyKind enemyKind)
        {
            if (isBoss)
            {
                return Random.Range(300, 801);
            }

            int min = ZombieModeTuning.NormalPurificationMin;
            int max = ZombieModeTuning.NormalPurificationMax;
            if (enemyKind == ZombieModeEnemyKind.Special)
            {
                min = ZombieModeTuning.SpecialPurificationMin;
                max = ZombieModeTuning.SpecialPurificationMax;
            }
            else if (enemyKind == ZombieModeEnemyKind.Elite)
            {
                min = ZombieModeTuning.ElitePurificationMin;
                max = ZombieModeTuning.ElitePurificationMax;
            }

            int baseValue = Random.Range(min, max + 1);
            int pollutionSteps = Mathf.FloorToInt(runState.TotalPollution / 10f);
            float multiplier = Mathf.Min(1f + pollutionSteps * 0.10f, ZombieModeTuning.PurificationPollutionScaleMax);
            return Mathf.Max(1, Mathf.FloorToInt(baseValue * multiplier));
        }

        internal void ApplyZombieModeEnemyTuning(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null || marker.IsBoss)
            {
                return;
            }

            float healthMultiplier = 1f;
            float damageMultiplier = 1f;
            float speedMultiplier = 1f;
            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                healthMultiplier = ZombieModeTuning.SpecialHealthMultiplier;
                damageMultiplier = ZombieModeTuning.SpecialDamageMultiplier;
                speedMultiplier = ZombieModeTuning.SpecialMoveSpeedMultiplier;
                ApplyZombieModeSpecialKindTuning(marker.SpecialKind, ref healthMultiplier, ref damageMultiplier, ref speedMultiplier);
                if (marker.SpecialKind == ZombieModeSpecialKind.Plague)
                {
                    ApplyZombieModePlagueFrostmourneAura(enemy);
                }
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                bool enhanced = runState.TotalPollution >= 15;
                healthMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteHealthMultiplier : ZombieModeTuning.EliteHealthMultiplier;
                damageMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteDamageMultiplier : ZombieModeTuning.EliteDamageMultiplier;
                speedMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteMoveSpeedMultiplier : ZombieModeTuning.EliteMoveSpeedMultiplier;
                ApplyZombieModeEliteAffixTuning(enemy, marker, ref healthMultiplier, ref damageMultiplier, ref speedMultiplier);
            }

            speedMultiplier *= owner.GetZombieModeWaveSpeedMultiplierForRuntimeModule(owner.GetZombieModePacingWaveForRuntimeModule());
            float pollutionHealthScale = 1f + runState.TotalPollution * ZombieModeTuning.PollutionHealthScalePerPoint;
            float pollutionDamageScale = 1f + runState.TotalPollution * ZombieModeTuning.PollutionDamageScalePerPoint;
            marker.HealthMultiplier = healthMultiplier * pollutionHealthScale;
            marker.DamageMultiplier = damageMultiplier * pollutionDamageScale;
            marker.MoveSpeedMultiplier = speedMultiplier;
            ApplyZombieModeHealthMultiplier(enemy, marker.HealthMultiplier, marker);
            ApplyZombieModeEnemyCombatStatMultipliers(enemy, marker.DamageMultiplier, marker.MoveSpeedMultiplier, marker);
            ApplyZombieModeMutationVisualIdentity(enemy, marker);
            ApplyZombieModeEnemyName(enemy, marker);
            EnsureZombieModeThreatRuntime(enemy, marker);
        }

        private void ApplyZombieModeSpecialKindTuning(
            ZombieModeSpecialKind specialKind,
            ref float healthMultiplier,
            ref float damageMultiplier,
            ref float speedMultiplier)
        {
            switch (specialKind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    speedMultiplier *= 1.20f;
                    break;
                case ZombieModeSpecialKind.Exploder:
                case ZombieModeSpecialKind.OfficialExploder:
                    healthMultiplier *= 1.30f / ZombieModeTuning.SpecialHealthMultiplier;
                    break;
                case ZombieModeSpecialKind.Plague:
                    healthMultiplier *= 1.50f / ZombieModeTuning.SpecialHealthMultiplier;
                    speedMultiplier *= 0.95f / ZombieModeTuning.SpecialMoveSpeedMultiplier;
                    break;
                case ZombieModeSpecialKind.Summoner:
                    healthMultiplier *= 1.50f / ZombieModeTuning.SpecialHealthMultiplier;
                    speedMultiplier *= 0.95f / ZombieModeTuning.SpecialMoveSpeedMultiplier;
                    break;
                case ZombieModeSpecialKind.Harasser:
                    healthMultiplier *= 1.30f / ZombieModeTuning.SpecialHealthMultiplier;
                    break;
            }
        }

        private void ApplyZombieModeEliteAffixTuning(
            CharacterMainControl enemy,
            ZombieModeEnemyRuntimeMarker marker,
            ref float healthMultiplier,
            ref float damageMultiplier,
            ref float speedMultiplier)
        {
            for (int i = 0; i < marker.EliteAffixes.Count; i++)
            {
                ZombieModeEliteAffix affix = marker.EliteAffixes[i];
                if (affix == ZombieModeEliteAffix.Swift)
                {
                    speedMultiplier *= 1.30f;
                }
                else if (affix == ZombieModeEliteAffix.Tough)
                {
                    healthMultiplier *= 1.40f;
                }
                else if (affix == ZombieModeEliteAffix.Frenzied)
                {
                    damageMultiplier *= 1.15f;
                    speedMultiplier *= 1.10f;
                }
                else if (affix == ZombieModeEliteAffix.Stalwart)
                {
                    healthMultiplier *= 1.15f;
                }
                else if (affix == ZombieModeEliteAffix.Regenerating)
                {
                    ZombieModeRegenerationAffixRuntime regen = enemy.gameObject.GetComponent<ZombieModeRegenerationAffixRuntime>();
                    if (regen == null)
                    {
                        regen = enemy.gameObject.AddComponent<ZombieModeRegenerationAffixRuntime>();
                    }
                    regen.Initialize(marker.RunId);
                }
                else if (affix == ZombieModeEliteAffix.Shielded)
                {
                    healthMultiplier *= 1.25f;
                }
            }
        }

        internal void ApplyZombieModeEnemyHurtAffixes(
            int runId,
            Health health,
            DamageInfo damageInfo,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (!IsZombieModeRunValid(runId) ||
                health == null ||
                damageInfo.fromCharacter == null ||
                marker == null ||
                marker.EnemyKind != ZombieModeEnemyKind.Elite)
            {
                return;
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Shielded))
            {
                ZombieModeShieldedAffixRuntime shield = marker.ShieldedAffix;
                if (shield != null)
                {
                    float dmg = damageInfo.damageValue;
                    if (shield.AbsorbDamage(ref dmg))
                    {
                        float absorbed = damageInfo.damageValue - dmg;
                        if (absorbed > 0f && health.CurrentHealth > 0f)
                        {
                            health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + absorbed));
                        }
                    }
                }
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Stalwart) &&
                damageInfo.fromCharacter.IsMainCharacter &&
                !IsZombieModeDamageFromMeleeWeapon(damageInfo))
            {
                float restore = Mathf.Max(0f, damageInfo.damageValue * (1f - ZombieModeTuning.StalwartRangedDamageMultiplier));
                if (restore > 0f && health.CurrentHealth > 0f)
                {
                    health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + restore));
                }
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Adaptive) &&
                damageInfo.fromCharacter.IsMainCharacter)
            {
                float now = GetZombieModeRuntimeNow();
                bool isMelee = IsZombieModeDamageFromMeleeWeapon(damageInfo);
                if (now > marker.AdaptiveReductionEndTime)
                {
                    marker.AdaptiveRangedActive = false;
                    marker.AdaptiveMeleeActive = false;
                }

                if (isMelee)
                {
                    marker.AdaptiveMeleeHitCount++;
                    marker.AdaptiveRangedHitCount = 0;
                    if (marker.AdaptiveMeleeHitCount >= ZombieModeTuning.AdaptiveAffixHitThreshold && !marker.AdaptiveMeleeActive)
                    {
                        marker.AdaptiveMeleeActive = true;
                        marker.AdaptiveRangedActive = false;
                        marker.AdaptiveReductionEndTime = GetZombieModeRuntimeNow() + ZombieModeTuning.AdaptiveAffixDurationSeconds;
                        marker.AdaptiveMeleeHitCount = 0;
                        CharacterMainControl ch = marker.Owner;
                        if (ch != null) ch.PopText(L10n.T("BossRush_ZombieMode_Affix_Adaptive"));
                    }
                    if (marker.AdaptiveMeleeActive)
                    {
                        float reduced = damageInfo.damageValue * ZombieModeTuning.AdaptiveAffixReductionPercent;
                        if (reduced > 0f && health.CurrentHealth > 0f)
                        {
                            health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + reduced));
                        }
                    }
                }
                else
                {
                    marker.AdaptiveRangedHitCount++;
                    marker.AdaptiveMeleeHitCount = 0;
                    if (marker.AdaptiveRangedHitCount >= ZombieModeTuning.AdaptiveAffixHitThreshold && !marker.AdaptiveRangedActive)
                    {
                        marker.AdaptiveRangedActive = true;
                        marker.AdaptiveMeleeActive = false;
                        marker.AdaptiveReductionEndTime = GetZombieModeRuntimeNow() + ZombieModeTuning.AdaptiveAffixDurationSeconds;
                        marker.AdaptiveRangedHitCount = 0;
                        CharacterMainControl ch = marker.Owner;
                        if (ch != null) ch.PopText(L10n.T("BossRush_ZombieMode_Affix_Adaptive"));
                    }
                    if (marker.AdaptiveRangedActive)
                    {
                        float reduced = damageInfo.damageValue * ZombieModeTuning.AdaptiveAffixReductionPercent;
                        if (reduced > 0f && health.CurrentHealth > 0f)
                        {
                            health.SetHealth(Mathf.Min(health.MaxHealth, health.CurrentHealth + reduced));
                        }
                    }
                }
            }
        }

        internal bool IsZombieModeDamageFromMeleeWeapon(DamageInfo damageInfo)
        {
            if (damageInfo.fromWeaponItemID <= 0)
            {
                return false;
            }

            bool cached;
            if (zombieModeMeleeWeaponTypeCache.TryGetValue(damageInfo.fromWeaponItemID, out cached))
            {
                return cached;
            }

            return CacheZombieModeMeleeWeaponType(damageInfo.fromWeaponItemID);
        }

        private bool CacheZombieModeMeleeWeaponType(int typeId)
        {
            bool melee = false;
            bool resolved = false;
            try
            {
                ItemStatsSystem.Item item = null;
                try { item = ItemStatsSystem.ItemAssetsCollection.GetPrefab(typeId); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] GetPrefab(" + typeId + ") 失败，回退 ItemFactory: " + e.Message); }
                if (item == null)
                {
                    try { item = ItemFactory.GetLoadedItem(typeId); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] ItemFactory.GetLoadedItem(" + typeId + ") 失败: " + e.Message); }
                }

                if (item != null)
                {
                    melee = ItemHasZombieModeTag(item, "MeleeWeapon") ||
                            item.GetComponent<ItemAgent_MeleeWeapon>() != null;
                    resolved = true;
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] 近战武器类型缓存失败: " + e.Message);
            }

            if (resolved)
            {
                zombieModeMeleeWeaponTypeCache[typeId] = melee;
            }
            return melee;
        }

        internal bool ItemHasZombieModeTag(ItemStatsSystem.Item item, string tagName)
        {
            if (item == null || string.IsNullOrEmpty(tagName))
            {
                return false;
            }

            try
            {
                Duckov.Utilities.Tag target = owner.FindZombieModeTagByNameForRuntimeModule(tagName);
                if (target == null || item.Tags == null)
                {
                    return false;
                }
                if (item.Tags.Contains(target))
                {
                    return true;
                }
                // 名称回退：避免 Tag 实例不一致（hot reload / 不同程序集）导致 Contains 漏判
                foreach (Duckov.Utilities.Tag tag in item.Tags)
                {
                    if (tag != null && string.Equals(tag.name, target.name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyZombieModeHealthMultiplier(
            CharacterMainControl enemy,
            float healthMultiplier,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || enemy.Health == null)
            {
                return;
            }

            marker.BaseMaxHealth = enemy.Health.MaxHealth;

            ApplyZombieModeHealthOnlyMultiplier(enemy, healthMultiplier, marker);

            enemy.Health.showHealthBar = marker.EnemyKind == ZombieModeEnemyKind.Elite;
            if (enemy.Health.MaxHealth > 0f)
            {
                enemy.Health.CurrentHealth = enemy.Health.MaxHealth;
            }
        }

        internal void ApplyZombieModeHealthOnlyMultiplier(
            CharacterMainControl character,
            float healthMultiplier,
            ZombieModeEnemyRuntimeMarker marker = null)
        {
            if (character == null || character.CharacterItem == null || Mathf.Approximately(healthMultiplier, 1f))
            {
                return;
            }

            try
            {
                if (marker != null && character.Health != null)
                {
                    marker.BaseMaxHealth = character.Health.MaxHealth;
                }

                if (marker == null)
                {
                    return;
                }

                RuntimeStatModifierTracker.TryAdd(
                    character,
                    ZombieModeStatNames.MaxHealth,
                    healthMultiplier - 1f,
                    marker,
                    marker.RuntimeModifierRecords,
                    "ZombieMode Enemy MaxHealth");

                if (character.Health != null && character.Health.MaxHealth > 0f)
                {
                    character.Health.SetHealth(character.Health.MaxHealth);
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] Health-only MaxHealth 倍率应用失败: " + e.Message);
            }
        }

        internal void ApplyZombieModeEnemyCombatStatMultipliers(
            CharacterMainControl enemy,
            float damageMultiplier,
            float speedMultiplier,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || enemy.CharacterItem == null || marker == null)
            {
                return;
            }

            TryApplyZombieModeEnemyStatMultiplier(enemy, ZombieModeStatNames.WalkSpeed, speedMultiplier, marker);
            TryApplyZombieModeEnemyStatMultiplier(enemy, ZombieModeStatNames.RunSpeed, speedMultiplier, marker);
            TryApplyZombieModeEnemyStatMultiplier(enemy, ZombieModeStatNames.MeleeDamageMultiplier, damageMultiplier, marker);
            TryApplyZombieModeEnemyStatMultiplier(enemy, ZombieModeStatNames.GunDamageMultiplier, damageMultiplier, marker);
        }

        private void TryApplyZombieModeEnemyStatMultiplier(
            CharacterMainControl character,
            string statName,
            float multiplier,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (character == null || marker == null || string.IsNullOrEmpty(statName) || Mathf.Approximately(multiplier, 1f))
            {
                return;
            }

            try
            {
                RuntimeStatModifierTracker.TryAdd(
                    character,
                    statName,
                    multiplier - 1f,
                    marker,
                    marker.RuntimeModifierRecords,
                    "ZombieMode Enemy Stat");
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] AI 倍率 Modifier 应用失败: " + e.Message);
            }
        }


        private void ApplyZombieModeEnemyName(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null)
            {
                return;
            }

            string label = string.Empty;
            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                label = GetZombieModeSpecialDisplayName(marker.SpecialKind);
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                label = GetZombieModeEliteAffixLabel(marker);
            }

            if (!string.IsNullOrEmpty(label))
            {
                enemy.gameObject.name = enemy.gameObject.name + "_" + label;
                enemy.PopText(label);
            }
        }

        private string GetZombieModeSpecialDisplayName(ZombieModeSpecialKind specialKind)
        {
            switch (specialKind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    return L10n.T("BossRush_ZombieMode_Special_Sprinter");
                case ZombieModeSpecialKind.Exploder:
                    return L10n.T("BossRush_ZombieMode_Special_Exploder");
                case ZombieModeSpecialKind.OfficialExploder:
                    return L10n.T("BossRush_ZombieMode_Special_OfficialExploder");
                case ZombieModeSpecialKind.Plague:
                    return L10n.T("BossRush_ZombieMode_Special_Plague");
                case ZombieModeSpecialKind.Summoner:
                    return L10n.T("BossRush_ZombieMode_Special_Summoner");
                case ZombieModeSpecialKind.Harasser:
                    return L10n.T("BossRush_ZombieMode_Special_Harasser");
                default:
                    return string.Empty;
            }
        }

        private string GetZombieModeEliteAffixLabel(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null || marker.EliteAffixes.Count <= 0)
            {
                return L10n.T("BossRush_ZombieMode_Elite");
            }

            string label = string.Empty;
            for (int i = 0; i < marker.EliteAffixes.Count; i++)
            {
                if (i > 0)
                {
                    label += "\u00B7";
                }
                label += GetZombieModeEliteAffixDisplayName(marker.EliteAffixes[i]);
            }
            return "[" + label + "]" + L10n.T("BossRush_ZombieMode_Elite");
        }

        private string GetZombieModeEliteAffixDisplayName(ZombieModeEliteAffix affix)
        {
            switch (affix)
            {
                case ZombieModeEliteAffix.Swift:
                    return L10n.T("BossRush_ZombieMode_Affix_Swift");
                case ZombieModeEliteAffix.Frenzied:
                    return L10n.T("BossRush_ZombieMode_Affix_Frenzied");
                case ZombieModeEliteAffix.Tough:
                    return L10n.T("BossRush_ZombieMode_Affix_Tough");
                case ZombieModeEliteAffix.Stalwart:
                    return L10n.T("BossRush_ZombieMode_Affix_Stalwart");
                case ZombieModeEliteAffix.Regenerating:
                    return L10n.T("BossRush_ZombieMode_Affix_Regenerating");
                case ZombieModeEliteAffix.Burst:
                    return L10n.T("BossRush_ZombieMode_Affix_Burst");
                case ZombieModeEliteAffix.Plague:
                    return L10n.T("BossRush_ZombieMode_Affix_Plague");
                case ZombieModeEliteAffix.Commander:
                    return L10n.T("BossRush_ZombieMode_Affix_Commander");
                case ZombieModeEliteAffix.ToxicAura:
                    return L10n.T("BossRush_ZombieMode_Affix_ToxicAura");
                case ZombieModeEliteAffix.Splitting:
                    return L10n.T("BossRush_ZombieMode_Affix_Splitting");
                case ZombieModeEliteAffix.Shielded:
                    return L10n.T("BossRush_ZombieMode_Affix_Shielded");
                default:
                    return L10n.T("BossRush_ZombieMode_Affix_Adaptive");
            }
        }

    }
}
