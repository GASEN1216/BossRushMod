using System.Collections.Generic;
using System.Collections;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private ZombieModeEnemyKind RollZombieModeEnemyKind()
        {
            int pollution = zombieModeRunState.TotalPollution;
            int eliteChance = GetZombieModeEliteChancePercent(pollution);
            int specialChance = GetZombieModeSpecialChancePercent(pollution);
            int roll = Random.Range(0, 100);
            if (roll < eliteChance)
            {
                return ZombieModeEnemyKind.Elite;
            }

            if (roll < eliteChance + specialChance)
            {
                return ZombieModeEnemyKind.Special;
            }

            return ZombieModeEnemyKind.Normal;
        }

        private int GetZombieModeSpecialChancePercent(int pollution)
        {
            if (pollution >= 25) return 30;
            if (pollution >= 20) return 25;
            if (pollution >= 15) return 20;
            if (pollution >= 10) return 15;
            if (pollution >= 5) return 10;
            return 5;
        }

        private int GetZombieModeEliteChancePercent(int pollution)
        {
            if (pollution >= 25) return 10;
            if (pollution >= 20) return 8;
            if (pollution >= 15) return 6;
            if (pollution >= 10) return 4;
            if (pollution >= 5) return 2;
            return 1;
        }

        private ZombieModeSpecialKind RollZombieModeSpecialKind()
        {
            ZombieModeSpecialKind[] kinds = new ZombieModeSpecialKind[]
            {
                ZombieModeSpecialKind.Sprinter,
                ZombieModeSpecialKind.Exploder,
                ZombieModeSpecialKind.Plague,
                ZombieModeSpecialKind.Summoner,
                ZombieModeSpecialKind.Harasser
            };
            return kinds[Random.Range(0, kinds.Length)];
        }

        private List<ZombieModeEliteAffix> RollZombieModeEliteAffixes()
        {
            int pollution = zombieModeRunState.TotalPollution;
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

            List<ZombieModeEliteAffix> selected = new List<ZombieModeEliteAffix>();
            List<ZombieModeEliteAffix> pool = new List<ZombieModeEliteAffix>();
            foreach (ZombieModeEliteAffix affix in System.Enum.GetValues(typeof(ZombieModeEliteAffix)))
            {
                if (GetZombieModeAffixUnlockTier(affix) <= zombieModeRunState.PollutionTier)
                {
                    pool.Add(affix);
                }
            }

            while (selected.Count < desiredCount && pool.Count > 0)
            {
                int index = Random.Range(0, pool.Count);
                ZombieModeEliteAffix candidate = pool[index];
                pool.RemoveAt(index);
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
            bool splitting = affixes.Contains(ZombieModeEliteAffix.Splitting);
            bool burst = affixes.Contains(ZombieModeEliteAffix.Burst);

            if (stalwart && shielded && regenerating)
            {
                return false;
            }

            if (stalwart && swift && zombieModeRunState.TotalPollution < 15)
            {
                return false;
            }

            if (toxicAura && plague && swift && zombieModeRunState.TotalPollution < 15)
            {
                return false;
            }

            if (splitting && burst && zombieModeRunState.PerformanceTier >= ZombieModePerformanceTier.SoftProtect)
            {
                return false;
            }

            return true;
        }

        private int CalculateZombieModeEnemyPurificationPoints(bool isBoss, ZombieModeEnemyKind enemyKind)
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
            int pollutionSteps = Mathf.FloorToInt(zombieModeRunState.TotalPollution / 10f);
            float multiplier = Mathf.Min(1f + pollutionSteps * 0.10f, ZombieModeTuning.PurificationPollutionScaleMax);
            return Mathf.Max(1, Mathf.FloorToInt(baseValue * multiplier));
        }

        private void ApplyZombieModeEnemyTuning(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
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
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                bool enhanced = zombieModeRunState.TotalPollution >= 15;
                healthMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteHealthMultiplier : ZombieModeTuning.EliteHealthMultiplier;
                damageMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteDamageMultiplier : ZombieModeTuning.EliteDamageMultiplier;
                speedMultiplier = enhanced ? ZombieModeTuning.EnhancedEliteMoveSpeedMultiplier : ZombieModeTuning.EliteMoveSpeedMultiplier;
                ApplyZombieModeEliteAffixTuning(enemy, marker, ref healthMultiplier, ref damageMultiplier, ref speedMultiplier);
            }

            float pollutionHealthScale = 1f + zombieModeRunState.TotalPollution * ZombieModeTuning.PollutionHealthScalePerPoint;
            float pollutionDamageScale = 1f + zombieModeRunState.TotalPollution * ZombieModeTuning.PollutionDamageScalePerPoint;
            marker.HealthMultiplier = healthMultiplier * pollutionHealthScale;
            marker.DamageMultiplier = damageMultiplier * pollutionDamageScale;
            marker.MoveSpeedMultiplier = speedMultiplier;
            ApplyZombieModeHealthMultiplier(enemy, marker.HealthMultiplier, marker);
            ApplyZombieModeEnemyCombatStatMultipliers(enemy, marker.DamageMultiplier, marker.MoveSpeedMultiplier);
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

        private void ApplyZombieModeEnemyHurtAffixes(
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
                ZombieModeShieldedAffixRuntime shield = health.gameObject.GetComponent<ZombieModeShieldedAffixRuntime>();
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
                bool isMelee = IsZombieModeDamageFromMeleeWeapon(damageInfo);
                if (Time.unscaledTime > marker.AdaptiveReductionEndTime)
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
                        marker.AdaptiveReductionEndTime = Time.unscaledTime + ZombieModeTuning.AdaptiveAffixDurationSeconds;
                        marker.AdaptiveMeleeHitCount = 0;
                        CharacterMainControl ch = marker.GetComponent<CharacterMainControl>();
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
                        marker.AdaptiveReductionEndTime = Time.unscaledTime + ZombieModeTuning.AdaptiveAffixDurationSeconds;
                        marker.AdaptiveRangedHitCount = 0;
                        CharacterMainControl ch = marker.GetComponent<CharacterMainControl>();
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

        private bool IsZombieModeDamageFromMeleeWeapon(DamageInfo damageInfo)
        {
            if (damageInfo.fromWeaponItemID <= 0)
            {
                return false;
            }

            try
            {
                ItemStatsSystem.Item item = ItemStatsSystem.ItemAssetsCollection.InstantiateSync(damageInfo.fromWeaponItemID);
                if (item == null)
                {
                    return false;
                }

                bool melee = ItemHasZombieModeTag(item, "MeleeWeapon") || item.GetComponent<ItemAgent_MeleeWeapon>() != null;
                try { Destroy(item.gameObject); } catch { }
                return melee;
            }
            catch
            {
                return false;
            }
        }

        private bool ItemHasZombieModeTag(ItemStatsSystem.Item item, string tagName)
        {
            if (item == null || string.IsNullOrEmpty(tagName))
            {
                return false;
            }

            try
            {
                Duckov.Utilities.Tag target = FindZombieModeTagByName(tagName);
                return target != null && item.Tags != null && item.Tags.Contains(target);
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

            // 通过 ApplyBossStatMultiplier 调整 MaxHealth Stat（避免反射写 Health 私有字段）。
            ApplyBossStatMultiplier(enemy, healthMultiplier);

            enemy.Health.showHealthBar = marker.EnemyKind == ZombieModeEnemyKind.Elite;
            if (enemy.Health.MaxHealth > 0f)
            {
                enemy.Health.CurrentHealth = enemy.Health.MaxHealth;
            }
        }

        private void ApplyZombieModeEnemyCombatStatMultipliers(CharacterMainControl enemy, float damageMultiplier, float speedMultiplier)
        {
            if (enemy == null || enemy.CharacterItem == null)
            {
                return;
            }

            TryApplyZombieModeEnemyStatMultiplier(enemy.CharacterItem, "WalkSpeed", speedMultiplier);
            TryApplyZombieModeEnemyStatMultiplier(enemy.CharacterItem, "RunSpeed", speedMultiplier);
            TryApplyZombieModeEnemyStatMultiplier(enemy.CharacterItem, "MeleeDamageMultiplier", damageMultiplier);
            TryApplyZombieModeEnemyStatMultiplier(enemy.CharacterItem, "GunDamageMultiplier", damageMultiplier);
        }

        private void TryApplyZombieModeEnemyStatMultiplier(ItemStatsSystem.Item characterItem, string statName, float multiplier)
        {
            if (characterItem == null || string.IsNullOrEmpty(statName) || Mathf.Approximately(multiplier, 1f))
            {
                return;
            }

            try
            {
                Stat stat = characterItem.GetStat(statName);
                if (stat == null)
                {
                    return;
                }

                Modifier modifier = new Modifier(ModifierType.Add, stat.BaseValue * (multiplier - 1f), this);
                stat.AddModifier(modifier);
            }
            catch { }
        }

        private void ApplyZombieModeAiSpeedMultiplier(CharacterMainControl enemy, float speedMultiplier)
        {
            if (enemy == null || Mathf.Approximately(speedMultiplier, 1f))
            {
                return;
            }

            enemy.transform.localScale = enemy.transform.localScale * Mathf.Clamp(speedMultiplier, 0.85f, 1.35f);
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

        private void HandleZombieModeSpecialDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            if (marker == null || marker.SpecialKind != ZombieModeSpecialKind.Exploder || character == null)
            {
                return;
            }

            DealZombieModeAreaDamageToPlayer(
                runId,
                character.transform.position,
                ZombieModeTuning.ExploderDeathRadius,
                ZombieModeTuning.ExploderDeathDamage);
        }

        private void HandleZombieModeEliteDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            if (marker == null || character == null)
            {
                return;
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Burst))
            {
                DealZombieModeAreaDamageToPlayer(
                    runId,
                    character.transform.position,
                    ZombieModeTuning.BurstAffixDeathRadius,
                    ZombieModeTuning.BurstAffixDeathDamage);
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Splitting) &&
                zombieModeRunState.PerformanceTier < ZombieModePerformanceTier.SoftProtect)
            {
                int count = ZombieModeTuning.SplittingAffixSpawnCount;
                for (int i = 0; i < count; i++)
                {
                    Vector3 offset = Quaternion.Euler(0f, 360f * i / count, 0f) * Vector3.forward * 1.5f;
                    SpawnZombieModeSmallSplitAsync(runId, character.transform.position + offset).Forget();
                }
            }
        }

        private async Cysharp.Threading.Tasks.UniTask SpawnZombieModeSmallSplitAsync(int runId, Vector3 position)
        {
            CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(runId, position, ZombieModeEnemyKind.Normal, true);
            if (zombie != null)
            {
                zombie.transform.localScale = zombie.transform.localScale * 0.6f;
            }
        }

        private void EnsureZombieModeThreatRuntime(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null || marker.IsBoss ||
                (marker.EnemyKind != ZombieModeEnemyKind.Special && marker.EnemyKind != ZombieModeEnemyKind.Elite))
            {
                return;
            }

            ZombieModeThreatRuntime runtime = enemy.gameObject.GetComponent<ZombieModeThreatRuntime>();
            if (runtime == null)
            {
                runtime = enemy.gameObject.AddComponent<ZombieModeThreatRuntime>();
            }

            float cooldown = marker.EnemyKind == ZombieModeEnemyKind.Elite
                ? ZombieModeTuning.EliteSkillCooldownSeconds
                : GetZombieModeSpecialCooldown(marker.SpecialKind);
            runtime.Initialize(marker.RunId, cooldown);

            if (marker.EnemyKind == ZombieModeEnemyKind.Elite &&
                marker.EliteAffixes.Contains(ZombieModeEliteAffix.Commander))
            {
                ZombieModeCommanderAuraRuntime commanderAura = enemy.gameObject.GetComponent<ZombieModeCommanderAuraRuntime>();
                if (commanderAura == null)
                {
                    commanderAura = enemy.gameObject.AddComponent<ZombieModeCommanderAuraRuntime>();
                }

                commanderAura.Initialize(
                    marker.RunId,
                    ZombieModeTuning.CommanderAffixAuraRadius,
                    ZombieModeTuning.CommanderAuraTickIntervalSeconds);
            }
        }

        private float GetZombieModeSpecialCooldown(ZombieModeSpecialKind kind)
        {
            switch (kind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    return ZombieModeTuning.SprinterCooldownSeconds;
                case ZombieModeSpecialKind.Exploder:
                    return ZombieModeTuning.ExploderCooldownSeconds;
                case ZombieModeSpecialKind.Plague:
                    return ZombieModeTuning.PoisonCooldownSeconds;
                case ZombieModeSpecialKind.Summoner:
                    return ZombieModeTuning.SummonerCooldownSeconds;
                case ZombieModeSpecialKind.Harasser:
                    return ZombieModeTuning.HarasserCooldownSeconds;
                default:
                    return ZombieModeTuning.ExploderCooldownSeconds;
            }
        }

        internal void TryExecuteZombieModeEnemyRuntimeSkill(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null ||
                !IsZombieModeRunValid(marker.RunId) ||
                zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat ||
                ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase) ||
                marker.RecycledForPerformance ||
                marker.DeathSettled)
            {
                return;
            }

            CharacterMainControl character = marker.GetComponent<CharacterMainControl>();
            CharacterMainControl player = CharacterMainControl.Main;
            if (character == null || player == null)
            {
                return;
            }

            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                TryExecuteZombieModeSpecialSkill(marker.RunId, character, marker, player);
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                TryExecuteZombieModeEliteSkill(marker.RunId, character, marker, player);
            }
        }

        private void TryExecuteZombieModeSpecialSkill(
            int runId,
            CharacterMainControl character,
            ZombieModeEnemyRuntimeMarker marker,
            CharacterMainControl player)
        {
            switch (marker.SpecialKind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    character.PopText(L10n.T("BossRush_ZombieMode_Special_Sprinter"));
                    Vector3 dashTarget = Vector3.MoveTowards(
                        character.transform.position,
                        player.transform.position,
                        ZombieModeTuning.SprinterDashDistance);
                    dashTarget.y = character.transform.position.y;
                    character.transform.position = dashTarget;
                    break;
                case ZombieModeSpecialKind.Exploder:
                    StartZombieModeTelegraphedAreaDamage(
                        runId,
                        character,
                        character.transform.position,
                        ZombieModeTuning.ExploderDeathRadius,
                        ZombieModeTuning.ExploderDeathDamage,
                        ZombieModeTuning.ExploderDetonationDelaySeconds,
                        L10n.T("BossRush_ZombieMode_Special_Exploder"));
                    break;
                case ZombieModeSpecialKind.Plague:
                    StartZombieModeTelegraphedAreaDamage(
                        runId,
                        character,
                        character.transform.position,
                        ZombieModeTuning.PlagueCloudRadius,
                        ZombieModeTuning.PlagueCloudDamagePerSecond * ZombieModeTuning.PlagueCloudDurationSeconds,
                        ZombieModeTuning.ThreatTelegraphDelaySeconds,
                        L10n.T("BossRush_ZombieMode_Special_Plague"));
                    break;
                case ZombieModeSpecialKind.Summoner:
                    character.PopText(L10n.T("BossRush_ZombieMode_Special_Summoner"));
                    if (zombieModeRunState.PerformanceTier < ZombieModePerformanceTier.SoftProtect)
                    {
                        for (int i = 0; i < ZombieModeTuning.SummonerSpawnCount; i++)
                        {
                            Vector3 offset = Quaternion.Euler(0f, 360f * i / ZombieModeTuning.SummonerSpawnCount, 0f) * Vector3.forward * 1.5f;
                            SpawnZombieModeSmallSplitAsync(runId, character.transform.position + offset).Forget();
                        }
                    }
                    break;
                case ZombieModeSpecialKind.Harasser:
                    StartZombieModeTelegraphedAreaDamage(
                        runId,
                        character,
                        player.transform.position,
                        3.5f,
                        ZombieModeTuning.HarasserProjectileDamage,
                        ZombieModeTuning.ThreatTelegraphDelaySeconds,
                        L10n.T("BossRush_ZombieMode_Special_Harasser"));
                    break;
            }
        }

        private void TryExecuteZombieModeEliteSkill(
            int runId,
            CharacterMainControl character,
            ZombieModeEnemyRuntimeMarker marker,
            CharacterMainControl player)
        {
            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Commander))
            {
                character.PopText(L10n.T("BossRush_ZombieMode_Affix_Commander"));
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.ToxicAura) ||
                marker.EliteAffixes.Contains(ZombieModeEliteAffix.Plague))
            {
                StartZombieModeTelegraphedAreaDamage(
                    runId,
                    character,
                    character.transform.position,
                    5.5f,
                    26f,
                    ZombieModeTuning.ThreatTelegraphDelaySeconds,
                    L10n.T("BossRush_ZombieMode_Affix_ToxicAura"));
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Shielded))
            {
                character.PopText(L10n.T("BossRush_ZombieMode_Affix_Shielded"));
                if (character.Health != null && character.Health.CurrentHealth > 0f)
                {
                    float shieldAmount = Mathf.Max(1f, character.Health.MaxHealth * ZombieModeTuning.ShieldedAffixShieldPercent);
                    ZombieModeShieldedAffixRuntime shield = character.gameObject.GetComponent<ZombieModeShieldedAffixRuntime>();
                    if (shield == null)
                    {
                        shield = character.gameObject.AddComponent<ZombieModeShieldedAffixRuntime>();
                    }
                    shield.ActivateShield(marker.RunId, shieldAmount, ZombieModeTuning.ShieldedAffixDurationSeconds);
                }
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Adaptive))
            {
                character.PopText(L10n.T("BossRush_ZombieMode_Affix_Adaptive"));
            }
        }

        private void ApplyZombieModeCommanderPulse(int runId, CharacterMainControl commander)
        {
            RefreshZombieModeCommanderAuraTargets(runId, commander, ZombieModeTuning.CommanderAffixAuraRadius, null);
        }

        internal void RefreshZombieModeCommanderAuraTargets(
            int runId,
            CharacterMainControl commander,
            float radius,
            Dictionary<int, ZombieModeCommanderAuraTargetRuntime> trackedTargets)
        {
            if (!IsZombieModeRunValid(runId) || commander == null || commander.gameObject == null)
            {
                return;
            }

            float radiusSqr = radius * radius;
            int sourceId = commander.gameObject.GetInstanceID();
            HashSet<int> currentTargets = trackedTargets != null ? new HashSet<int>() : null;
            for (int i = 0; i < zombieModeRunState.RunOnlyObjects.Count; i++)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null ||
                    record.Kind != ZombieModeRunOnlyObjectKind.Enemy ||
                    record.GameObject == null ||
                    record.GameObject == commander.gameObject)
                {
                    continue;
                }

                ZombieModeEnemyRuntimeMarker target = record.GameObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
                if (target == null ||
                    target.RunId != runId ||
                    target.IsBoss ||
                    target.EnemyKind != ZombieModeEnemyKind.Normal ||
                    target.RecycledForPerformance ||
                    target.DeathSettled)
                {
                    continue;
                }

                Vector3 delta = target.transform.position - commander.transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                CharacterMainControl targetCharacter = target.GetComponent<CharacterMainControl>();
                if (targetCharacter == null ||
                    targetCharacter.Health == null ||
                    targetCharacter.Health.CurrentHealth <= 0f)
                {
                    continue;
                }

                int targetId = targetCharacter.gameObject.GetInstanceID();
                if (currentTargets != null)
                {
                    currentTargets.Add(targetId);
                }

                ZombieModeCommanderAuraTargetRuntime targetRuntime = targetCharacter.gameObject.GetComponent<ZombieModeCommanderAuraTargetRuntime>();
                if (targetRuntime == null)
                {
                    targetRuntime = targetCharacter.gameObject.AddComponent<ZombieModeCommanderAuraTargetRuntime>();
                }

                targetRuntime.ApplySource(runId, sourceId);
                if (trackedTargets != null)
                {
                    trackedTargets[targetId] = targetRuntime;
                }
            }

            if (trackedTargets == null)
            {
                return;
            }

            List<int> staleTargetIds = null;
            foreach (KeyValuePair<int, ZombieModeCommanderAuraTargetRuntime> entry in trackedTargets)
            {
                if (currentTargets.Contains(entry.Key))
                {
                    continue;
                }

                if (entry.Value != null)
                {
                    entry.Value.RemoveSource(sourceId);
                }

                if (staleTargetIds == null)
                {
                    staleTargetIds = new List<int>();
                }

                staleTargetIds.Add(entry.Key);
            }

            if (staleTargetIds == null)
            {
                return;
            }

            for (int i = 0; i < staleTargetIds.Count; i++)
            {
                trackedTargets.Remove(staleTargetIds[i]);
            }
        }

        private void StartZombieModeTelegraphedAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage,
            float delay,
            string label)
        {
            if (!IsZombieModeRunValid(runId) || ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase))
            {
                return;
            }

            if (source != null && !string.IsNullOrEmpty(label))
            {
                source.PopText(label);
            }

            GameObject telegraph = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            telegraph.name = "ZombieMode_Telegraph";
            telegraph.transform.position = origin + Vector3.up * 0.03f;
            telegraph.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
            Collider collider = telegraph.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            try
            {
                Renderer renderer = telegraph.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = new Color(1f, 0.16f, 0.08f, 0.35f);
                }
            }
            catch { }

            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, telegraph, telegraph, null);
            StartZombieModeCoroutine(
                ZombieModeTelegraphedAreaDamageCoroutine(runId, source, origin, radius, damage, delay, telegraph),
                runId);
        }

        private IEnumerator ZombieModeTelegraphedAreaDamageCoroutine(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage,
            float delay,
            GameObject telegraph)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, delay));

            if (IsZombieModeRunValid(runId) && !ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase))
            {
                DealZombieModeAreaDamageToPlayer(runId, source, origin, radius, damage);
            }

            if (telegraph != null)
            {
                try { Destroy(telegraph); } catch { }
            }
        }

        private void DealZombieModeAreaDamageToPlayer(int runId, Vector3 origin, float radius, float damage)
        {
            DealZombieModeAreaDamageToPlayer(runId, null, origin, radius, damage);
        }

        private void DealZombieModeAreaDamageToPlayer(int runId, CharacterMainControl source, Vector3 origin, float radius, float damage)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null)
            {
                return;
            }

            if ((player.transform.position - origin).sqrMagnitude > radius * radius)
            {
                return;
            }

            DamageInfo damageInfo = source != null ? new DamageInfo(source) : new DamageInfo();
            damageInfo.damageType = DamageTypes.normal;
            damageInfo.damageValue = damage;
            damageInfo.damagePoint = player.transform.position;
            damageInfo.damageNormal = (player.transform.position - origin).normalized;
            player.Health.Hurt(damageInfo);
        }
    }

    public sealed class ZombieModeThreatRuntime : MonoBehaviour
    {
        private int runId;
        private float cooldown;
        private float nextSkillTime;
        private ZombieModeEnemyRuntimeMarker marker;
        private ModBehaviour owner;

        public void Initialize(int newRunId, float newCooldown)
        {
            runId = newRunId;
            cooldown = Mathf.Max(1f, newCooldown);
            nextSkillTime = Time.unscaledTime + UnityEngine.Random.Range(1.5f, 4f);
            marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            owner = ModBehaviour.Instance;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextSkillTime)
            {
                return;
            }

            nextSkillTime = Time.unscaledTime + cooldown + UnityEngine.Random.Range(0f, 2f);
            ModBehaviour inst = owner;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                inst = ModBehaviour.Instance;
                owner = inst;
            }
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                return;
            }

            if (marker == null)
            {
                marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            }

            inst.TryExecuteZombieModeEnemyRuntimeSkill(marker);
        }
    }

    public sealed class ZombieModeCommanderAuraRuntime : MonoBehaviour
    {
        private int runId;
        private float radius;
        private float tickInterval;
        private float nextTickTime;
        private CharacterMainControl owner;
        private ZombieModeEnemyRuntimeMarker marker;
        private readonly Dictionary<int, ZombieModeCommanderAuraTargetRuntime> trackedTargets =
            new Dictionary<int, ZombieModeCommanderAuraTargetRuntime>();

        public void Initialize(int newRunId, float newRadius, float newTickInterval)
        {
            runId = newRunId;
            radius = Mathf.Max(0.5f, newRadius);
            tickInterval = Mathf.Max(0.2f, newTickInterval);
            nextTickTime = 0f;
            owner = GetComponent<CharacterMainControl>();
            marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                ClearTargets();
                Destroy(this);
                return;
            }

            if (owner == null)
            {
                owner = GetComponent<CharacterMainControl>();
            }

            if (marker == null)
            {
                marker = GetComponent<ZombieModeEnemyRuntimeMarker>();
            }

            if (owner == null ||
                marker == null ||
                marker.RunId != runId ||
                marker.DeathSettled ||
                marker.RecycledForPerformance ||
                !marker.EliteAffixes.Contains(ZombieModeEliteAffix.Commander) ||
                owner.Health == null ||
                owner.Health.CurrentHealth <= 0f)
            {
                ClearTargets();
                Destroy(this);
                return;
            }

            if (Time.unscaledTime < nextTickTime)
            {
                return;
            }

            nextTickTime = Time.unscaledTime + tickInterval;
            inst.RefreshZombieModeCommanderAuraTargets(runId, owner, radius, trackedTargets);
        }

        private void OnDisable()
        {
            ClearTargets();
        }

        private void OnDestroy()
        {
            ClearTargets();
        }

        private void ClearTargets()
        {
            int sourceId = gameObject != null ? gameObject.GetInstanceID() : 0;
            if (trackedTargets.Count <= 0)
            {
                return;
            }

            foreach (KeyValuePair<int, ZombieModeCommanderAuraTargetRuntime> entry in trackedTargets)
            {
                if (entry.Value != null)
                {
                    entry.Value.RemoveSource(sourceId);
                }
            }

            trackedTargets.Clear();
        }
    }

    public sealed class ZombieModeCommanderAuraTargetRuntime : MonoBehaviour
    {
        private int runId;
        private Stat walkSpeedStat;
        private Stat runSpeedStat;
        private Stat meleeDamageStat;
        private Stat gunDamageStat;
        private Modifier walkSpeedModifier;
        private Modifier runSpeedModifier;
        private Modifier meleeDamageModifier;
        private Modifier gunDamageModifier;
        private readonly HashSet<int> sourceIds = new HashSet<int>();

        public void ApplySource(int newRunId, int sourceId)
        {
            if (sourceId == 0)
            {
                return;
            }

            runId = newRunId;
            sourceIds.Add(sourceId);
            EnsureModifiers();
        }

        public void RemoveSource(int sourceId)
        {
            if (sourceId != 0)
            {
                sourceIds.Remove(sourceId);
            }

            if (sourceIds.Count > 0)
            {
                return;
            }

            ReleaseModifiers();
            Destroy(this);
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            CharacterMainControl character = GetComponent<CharacterMainControl>();
            if (inst == null ||
                inst.ZombieModeCurrentRunId != runId ||
                character == null ||
                character.CharacterItem == null ||
                character.Health == null ||
                character.Health.CurrentHealth <= 0f ||
                sourceIds.Count <= 0)
            {
                ReleaseModifiers();
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            ReleaseModifiers();
        }

        private void EnsureModifiers()
        {
            CharacterMainControl character = GetComponent<CharacterMainControl>();
            if (character == null || character.CharacterItem == null)
            {
                return;
            }

            EnsureModifier(character.CharacterItem, "WalkSpeed", ref walkSpeedStat, ref walkSpeedModifier, ZombieModeTuning.CommanderAffixMoveSpeedBonus);
            EnsureModifier(character.CharacterItem, "RunSpeed", ref runSpeedStat, ref runSpeedModifier, ZombieModeTuning.CommanderAffixMoveSpeedBonus);
            EnsureModifier(character.CharacterItem, "MeleeDamageMultiplier", ref meleeDamageStat, ref meleeDamageModifier, ZombieModeTuning.CommanderAffixDamageBonus);
            EnsureModifier(character.CharacterItem, "GunDamageMultiplier", ref gunDamageStat, ref gunDamageModifier, ZombieModeTuning.CommanderAffixDamageBonus);
        }

        private void EnsureModifier(ItemStatsSystem.Item characterItem, string statName, ref Stat stat, ref Modifier modifier, float value)
        {
            if (modifier != null)
            {
                return;
            }

            stat = characterItem.GetStat(statName);
            if (stat == null)
            {
                return;
            }

            modifier = new Modifier(ModifierType.PercentageAdd, value, this);
            stat.AddModifier(modifier);
        }

        private void ReleaseModifiers()
        {
            RemoveModifier(ref walkSpeedStat, ref walkSpeedModifier);
            RemoveModifier(ref runSpeedStat, ref runSpeedModifier);
            RemoveModifier(ref meleeDamageStat, ref meleeDamageModifier);
            RemoveModifier(ref gunDamageStat, ref gunDamageModifier);
            sourceIds.Clear();
        }

        private void RemoveModifier(ref Stat stat, ref Modifier modifier)
        {
            if (stat != null && modifier != null)
            {
                stat.RemoveModifier(modifier);
            }

            stat = null;
            modifier = null;
        }
    }

    public sealed class ZombieModeRegenerationAffixRuntime : MonoBehaviour
    {
        private int runId;
        private float nextTick;

        public void Initialize(int newRunId)
        {
            runId = newRunId;
            nextTick = Time.time + 1f;
        }

        private void Update()
        {
            if (Time.time < nextTick)
            {
                return;
            }

            nextTick = Time.time + 1f;
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                return;
            }

            CharacterMainControl character = GetComponent<CharacterMainControl>();
            if (character == null || character.Health == null || character.Health.CurrentHealth <= 0)
            {
                return;
            }

            float heal = Mathf.Max(1f, character.Health.MaxHealth * 0.025f);
            character.Health.SetHealth(Mathf.Min(character.Health.MaxHealth, character.Health.CurrentHealth + heal));
        }
    }

    public sealed class ZombieModeShieldedAffixRuntime : MonoBehaviour
    {
        private int runId;
        private float shieldRemaining;
        private float shieldEndTime;
        private bool shieldActive;

        public void ActivateShield(int newRunId, float amount, float duration)
        {
            runId = newRunId;
            shieldRemaining = amount;
            shieldEndTime = Time.unscaledTime + duration;
            shieldActive = true;
        }

        private void Update()
        {
            if (!shieldActive)
            {
                return;
            }

            if (Time.unscaledTime >= shieldEndTime)
            {
                shieldActive = false;
                shieldRemaining = 0f;
                return;
            }

            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                shieldActive = false;
                return;
            }
        }

        public bool AbsorbDamage(ref float damageValue)
        {
            if (!shieldActive || shieldRemaining <= 0f)
            {
                return false;
            }

            if (damageValue <= shieldRemaining)
            {
                shieldRemaining -= damageValue;
                damageValue = 0f;
            }
            else
            {
                damageValue -= shieldRemaining;
                shieldRemaining = 0f;
                shieldActive = false;
            }
            return true;
        }
    }
}
