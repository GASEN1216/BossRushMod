// ============================================================================
// ZombieModeRewardRuntimeModifiers.cs - 丧尸模式奖励运行时属性与弹体效果
// ============================================================================

using System.Collections;
using Duckov.Buffs;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void RebuildZombieModeOptionPersistentModifiers()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            RuntimeStatModifierTracker.RemoveAll(options.ModifierRecords, "ZombieMode Option Persistent");

            if (player == null || player.CharacterItem == null)
            {
                return;
            }

            float optionTradeoffPenalty = -Mathf.Clamp01(options.OptionTradeoffMoveSpeedPenalty);
            if (!Mathf.Approximately(optionTradeoffPenalty, 0f))
            {
                TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.MoveSpeed,
                    optionTradeoffPenalty,
                    ModifierType.PercentageAdd,
                    options.ModifierRecords,
                    "ZombieMode Option Tradeoff MoveSpeed");
                TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.WalkSpeed,
                    optionTradeoffPenalty,
                    ModifierType.PercentageAdd,
                    options.ModifierRecords,
                    "ZombieMode Option Tradeoff WalkSpeed");
                TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.RunSpeed,
                    optionTradeoffPenalty,
                    ModifierType.PercentageAdd,
                    options.ModifierRecords,
                    "ZombieMode Option Tradeoff RunSpeed");
            }

            float gunDamagePenalty = -Mathf.Clamp01(options.OptionTradeoffGunDamagePenalty);
            if (!Mathf.Approximately(gunDamagePenalty, 0f))
            {
                TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.GunDamageMultiplier,
                    gunDamagePenalty,
                    ModifierType.PercentageAdd,
                    options.ModifierRecords,
                    "ZombieMode Option Tradeoff GunDamage");
            }

            float reloadSpeedPenalty = -Mathf.Clamp01(options.OptionTradeoffReloadSpeedPenalty);
            if (!Mathf.Approximately(reloadSpeedPenalty, 0f))
            {
                TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.ReloadSpeedGain,
                    reloadSpeedPenalty,
                    ModifierType.Add,
                    options.ModifierRecords,
                    "ZombieMode Option Tradeoff ReloadSpeed");
            }

            float damageTakenPenalty = Mathf.Clamp01(options.OptionTradeoffDamageTakenPenalty);
            if (!Mathf.Approximately(damageTakenPenalty, 0f))
            {
                TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.ElementFactorPhysics,
                    damageTakenPenalty,
                    ModifierType.PercentageAdd,
                    options.ModifierRecords,
                    "ZombieMode Option Tradeoff DamageTaken");
            }

            float maxHealthPenalty = -Mathf.Clamp01(options.OptionTradeoffMaxHealthPenalty);
            if (!Mathf.Approximately(maxHealthPenalty, 0f))
            {
                TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.MaxHealth,
                    maxHealthPenalty,
                    ModifierType.PercentageAdd,
                    options.ModifierRecords,
                    "ZombieMode Option Tradeoff MaxHealth");
                if (player.Health != null)
                {
                    player.Health.SetHealth(Mathf.Min(player.Health.CurrentHealth, player.Health.MaxHealth));
                }
            }

            if (options.MutatorCritFocusStacks > 0)
            {
                TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.GunCritRateGain,
                    0.15f * Mathf.Min(3, options.MutatorCritFocusStacks),
                    ModifierType.Add,
                    options.ModifierRecords,
                    "ZombieMode Option CritFocus");
            }

            if (options.MutatorQuickReloadStacks > 0)
            {
                TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.ReloadSpeedGain,
                    0.25f * Mathf.Min(3, options.MutatorQuickReloadStacks),
                    ModifierType.Add,
                    options.ModifierRecords,
                    "ZombieMode Option QuickReload");
            }

            if (options.MutatorDashBoostStacks > 0)
            {
                TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.DashSpeed,
                    0.25f * Mathf.Min(2, options.MutatorDashBoostStacks),
                    ModifierType.PercentageAdd,
                    options.ModifierRecords,
                    "ZombieMode Option DashBoost");
            }
        }

        private bool TryAddZombieModeOptionModifier(
            CharacterMainControl character,
            string statName,
            float value,
            ModifierType type,
            System.Collections.Generic.List<ZombieModeAttributeModifierRecord> records,
            string context)
        {
            if (type == ModifierType.PercentageAdd)
            {
                return RuntimeStatModifierTracker.TryAdd(character, statName, value, this, records, context);
            }

            if (character == null || character.CharacterItem == null ||
                string.IsNullOrEmpty(statName) || records == null || Mathf.Approximately(value, 0f))
            {
                return false;
            }

            try
            {
                ItemStatsSystem.Stat stat = character.CharacterItem.GetStat(statName);
                if (stat == null)
                {
                    return false;
                }

                Modifier modifier = new Modifier(ModifierType.Add, value, this);
                stat.AddModifier(modifier);

                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = character.CharacterItem;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = statName;
                records.Add(record);
                return true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] option modifier add failed: " + context + ", " + e.Message);
                return false;
            }
        }

        private void EnsureZombieModeOptionPlayerHealthListener()
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (!options.MutatorBulletTimeEnabled && !options.MutatorGuardianShieldEnabled)
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null)
            {
                return;
            }

            if (zombieModeOptionPlayerHealth == player.Health && zombieModeOptionPlayerHealthChangeHandler != null)
            {
                options.PlayerHealthListenerRegistered = true;
                return;
            }

            if (zombieModeOptionPlayerHealth != null && zombieModeOptionPlayerHealthChangeHandler != null)
            {
                try { zombieModeOptionPlayerHealth.OnHealthChange.RemoveListener(zombieModeOptionPlayerHealthChangeHandler); }
                catch (System.Exception e) { DevLog("[ZombieMode] option health listener swap failed: " + e.Message); }
            }

            zombieModeOptionPlayerHealth = player.Health;
            zombieModeOptionPlayerHealthChangeHandler = HandleZombieModePlayerHealthChangedForOptions;
            zombieModeOptionPlayerHealth.OnHealthChange.AddListener(zombieModeOptionPlayerHealthChangeHandler);
            options.PlayerHealthListenerRegistered = true;
            if (!zombieModeOptionRuntimeCleanupRegistered)
            {
                zombieModeOptionRuntimeCleanupRegistered = true;
                RegisterZombieModeRunOnlyObject(zombieModeRunState.RunId, ZombieModeRunOnlyObjectKind.EventListener, null, zombieModeOptionPlayerHealth, UnregisterZombieModeOptionPlayerHealthListener);
            }
        }

        private void UnregisterZombieModeOptionPlayerHealthListener()
        {
            if (zombieModeOptionPlayerHealth != null && zombieModeOptionPlayerHealthChangeHandler != null)
            {
                try
                {
                    zombieModeOptionPlayerHealth.OnHealthChange.RemoveListener(zombieModeOptionPlayerHealthChangeHandler);
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] option health listener remove failed: " + e.Message);
                }
            }

            zombieModeOptionPlayerHealth = null;
            zombieModeOptionPlayerHealthChangeHandler = null;
            zombieModeRunState.OptionRuntime.PlayerHealthListenerRegistered = false;
        }

        private void HandleZombieModePlayerHealthChangedForOptions(Health health)
        {
            if (!IsZombieModeRunValid(zombieModeRunState.RunId) || health == null)
            {
                return;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (options.MutatorBulletTimeEnabled)
            {
                TryTriggerZombieModeBulletTime(health);
            }

            if (options.MutatorGuardianShieldEnabled)
            {
                UpdateZombieModeGuardianShield(health);
            }
        }

        private void TryTriggerZombieModeBulletTime(Health health)
        {
            if (health == null || health.MaxHealth <= 0f || health.CurrentHealth <= 0f)
            {
                return;
            }

            ZombieModeCombatPhase phase = zombieModeRunState.CombatPhase;
            if (phase == ZombieModeCombatPhase.None ||
                phase == ZombieModeCombatPhase.RewardSelection ||
                phase == ZombieModeCombatPhase.Settling ||
                phase == ZombieModeCombatPhase.SuccessExit ||
                phase == ZombieModeCombatPhase.FailedExit)
            {
                return;
            }

            float ratio = health.CurrentHealth / health.MaxHealth;
            if (ratio > 0.25f)
            {
                return;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            float now = GetZombieModeRuntimeNow();
            if (now - options.LastBulletTimeTriggerTime < 20f)
            {
                return;
            }

            try
            {
                if (GameManager.TimeScaleManager != null)
                {
                    GameManager.TimeScaleManager.EnterBulletTime(1f);
                    options.LastBulletTimeTriggerTime = now;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] bullet time trigger failed: " + e.Message);
            }
        }

        private void UpdateZombieModeGuardianShield(Health health)
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            bool fullHealth = health != null && health.MaxHealth > 0f && health.CurrentHealth >= health.MaxHealth - 0.01f;
            if (fullHealth && !options.GuardianShieldActive)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (TryAddZombieModeOptionModifier(
                    player,
                    ZombieModeStatNames.ElementFactorPhysics,
                    -0.25f,
                    ModifierType.PercentageAdd,
                    options.GuardianShieldRecords,
                    "ZombieMode Option GuardianShield"))
                {
                    options.GuardianShieldActive = true;
                }
                return;
            }

            if (!fullHealth && options.GuardianShieldActive)
            {
                RuntimeStatModifierTracker.RemoveAll(options.GuardianShieldRecords, "ZombieMode Option GuardianShield");
                options.GuardianShieldActive = false;
            }
        }

        public void ApplyZombieModeProjectileRewardEffects(Projectile projectile)
        {
            if (projectile == null)
            {
                return;
            }

            if (!IsZombieModeActive)
            {
                RemoveZombieModePlayerProjectileRuntime(projectile);
                return;
            }

            ProjectileContext context = projectile.context;
            if (context.fromCharacter == null || context.fromCharacter != CharacterMainControl.Main)
            {
                RemoveZombieModePlayerProjectileRuntime(projectile);
                return;
            }

            if (context.fromWeaponItemID <= 0)
            {
                RemoveZombieModePlayerProjectileRuntime(projectile);
                return;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (options.ProjectilePenetrationStacks <= 0 &&
                options.ProjectileBurnStacks <= 0 &&
                options.ProjectileColdStacks <= 0 &&
                options.ProjectilePoisonStacks <= 0 &&
                options.ProjectileArmorBreakStacks <= 0 &&
                options.ProjectileHelixStacks <= 0 &&
                options.ProjectileTrailStacks <= 0)
            {
                RemoveZombieModePlayerProjectileRuntime(projectile);
                return;
            }

            if (options.ProjectilePenetrationStacks > 0)
            {
                context.penetrate += Mathf.Min(3, options.ProjectilePenetrationStacks);
            }

            if (options.ProjectileArmorBreakStacks > 0)
            {
                int stacks = Mathf.Min(3, options.ProjectileArmorBreakStacks);
                context.armorPiercing += 0.25f * stacks;
                context.armorBreak += 0.10f * stacks;
            }

            TryApplyZombieModeElementalProjectileEffect(ref context, options);

            bool enableHelixRuntime = options.ProjectileHelixStacks > 0;
            bool enableTrailRuntime = options.ProjectileTrailStacks > 0;
            if (context.fromWeaponItemID > 0 && (enableHelixRuntime || enableTrailRuntime))
            {
                ZombieModePlayerProjectileRuntime runtime = projectile.GetComponent<ZombieModePlayerProjectileRuntime>();
                if (runtime == null)
                {
                    runtime = projectile.gameObject.AddComponent<ZombieModePlayerProjectileRuntime>();
                }

                runtime.ResetRuntimeState();
                runtime.Initialize(
                    zombieModeRunState.RunId,
                    enableHelixRuntime,
                    0.18f,
                    14f,
                    enableTrailRuntime,
                    1.4f,
                    9f);
            }
            else
            {
                RemoveZombieModePlayerProjectileRuntime(projectile);
            }

            projectile.context = context;
        }

        private void RemoveZombieModePlayerProjectileRuntime(Projectile projectile)
        {
            if (projectile == null)
            {
                return;
            }

            ZombieModePlayerProjectileRuntime runtime = projectile.GetComponent<ZombieModePlayerProjectileRuntime>();
            if (runtime != null)
            {
                runtime.ResetRuntimeState();
                runtime.ClearRuntimeConfiguration();
                Destroy(runtime);
            }
        }

        private bool IsZombieModePlayerProjectileDamage(DamageInfo damageInfo)
        {
            return damageInfo.fromCharacter == CharacterMainControl.Main &&
                   !damageInfo.isFromBuffOrEffect &&
                   damageInfo.fromWeaponItemID > 0 &&
                   !IsZombieModeDamageFromMeleeWeapon(damageInfo);
        }

        private bool TryApplyZombieModeElementalProjectileEffect(ref ProjectileContext context, ZombieModeOptionRuntimeState options)
        {
            int activeCount = 0;
            if (options.ProjectileBurnStacks > 0) activeCount++;
            if (options.ProjectileColdStacks > 0) activeCount++;
            if (options.ProjectilePoisonStacks > 0) activeCount++;
            if (activeCount <= 0)
            {
                return false;
            }

            int cursor = Mathf.Abs(options.ElementalShotCursor++);
            for (int i = 0; i < activeCount; i++)
            {
                int selected = (cursor + i) % activeCount;
                int index = 0;

                if (options.ProjectileBurnStacks > 0)
                {
                    if (index == selected)
                    {
                        return TryApplyZombieModeSelectedElementalProjectileEffect(
                            ref context,
                            ZombieModeRewardType.ProjectileBurn,
                            options.ProjectileBurnStacks);
                    }
                    index++;
                }

                if (options.ProjectileColdStacks > 0)
                {
                    if (index == selected)
                    {
                        return TryApplyZombieModeSelectedElementalProjectileEffect(
                            ref context,
                            ZombieModeRewardType.ProjectileCold,
                            options.ProjectileColdStacks);
                    }
                    index++;
                }

                if (options.ProjectilePoisonStacks > 0 && index == selected)
                {
                    return TryApplyZombieModeSelectedElementalProjectileEffect(
                        ref context,
                        ZombieModeRewardType.ProjectilePoison,
                        options.ProjectilePoisonStacks);
                }
            }

            return false;
        }

        private bool TryApplyZombieModeSelectedElementalProjectileEffect(
            ref ProjectileContext context,
            ZombieModeRewardType rewardType,
            int stacks)
        {
            float chance = GetZombieModeProjectileBuffChance(rewardType, stacks);
            if (chance <= 0f || UnityEngine.Random.value > chance)
            {
                return false;
            }

            Buff buff = null;
            switch (rewardType)
            {
                case ZombieModeRewardType.ProjectileBurn:
                    context.element_Fire = Mathf.Max(context.element_Fire, 1f);
                    buff = GameplayDataSettings.Buffs != null ? GameplayDataSettings.Buffs.Burn : null;
                    break;
                case ZombieModeRewardType.ProjectileCold:
                    context.element_Ice = Mathf.Max(context.element_Ice, 1f);
                    buff = GameplayDataSettings.Buffs != null ? GameplayDataSettings.Buffs.Cold : null;
                    break;
                case ZombieModeRewardType.ProjectilePoison:
                    context.element_Poison = Mathf.Max(context.element_Poison, 1f);
                    buff = GameplayDataSettings.Buffs != null ? GameplayDataSettings.Buffs.Poison : null;
                    break;
            }

            if (context.buff == null && buff != null)
            {
                context.buff = buff;
                context.buffChance = 1f;
            }

            return true;
        }

        private float GetZombieModeProjectileBuffChance(ZombieModeRewardType rewardType, int stacks)
        {
            stacks = Mathf.Max(0, stacks);
            switch (rewardType)
            {
                case ZombieModeRewardType.ProjectileBurn:
                    return Mathf.Min(0.75f, 0.35f * stacks);
                case ZombieModeRewardType.ProjectileCold:
                    return Mathf.Min(0.60f, 0.25f * stacks);
                case ZombieModeRewardType.ProjectilePoison:
                    return Mathf.Min(0.75f, 0.35f * stacks);
                default:
                    return 0f;
            }
        }
    }
}
