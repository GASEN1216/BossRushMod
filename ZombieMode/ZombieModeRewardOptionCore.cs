// ============================================================================
// ZombieModeRewardOptionCore.cs - 丧尸模式奖励选项核心效果
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
        private bool ApplyZombieModeOptionReward(ZombieModeRewardType rewardType)
        {
            if (!IsZombieModeRunValid(zombieModeRunState.RunId))
            {
                return false;
            }

            if (IsZombieModePhase2ContractReward(rewardType))
            {
                Debug.Assert(false, "[ZombieMode] Phase-2 contracts must route through ApplyZombieModePhase2ContractReward.");
                return false;
            }

            if (!TrySpendZombieModeOptionPurificationCost(rewardType))
            {
                return false;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            switch (rewardType)
            {
                case ZombieModeRewardType.ProjectilePenetration:
                    options.ProjectilePenetrationStacks = Mathf.Min(3, options.ProjectilePenetrationStacks + 1);
                    break;
                case ZombieModeRewardType.ProjectileBurn:
                    options.ProjectileBurnStacks = Mathf.Min(3, options.ProjectileBurnStacks + 1);
                    break;
                case ZombieModeRewardType.ProjectileCold:
                    options.ProjectileColdStacks = Mathf.Min(3, options.ProjectileColdStacks + 1);
                    break;
                case ZombieModeRewardType.ProjectilePoison:
                    options.ProjectilePoisonStacks = Mathf.Min(3, options.ProjectilePoisonStacks + 1);
                    break;
                case ZombieModeRewardType.ProjectileArmorBreak:
                    options.ProjectileArmorBreakStacks = Mathf.Min(3, options.ProjectileArmorBreakStacks + 1);
                    break;
                case ZombieModeRewardType.ProjectileTrident:
                    options.ProjectileTridentStacks = Mathf.Min(1, options.ProjectileTridentStacks + 1);
                    RebuildZombieModeProjectileSpreadState();
                    break;
                case ZombieModeRewardType.ProjectileShotgunSpray:
                    options.ProjectileShotgunSprayStacks = Mathf.Min(1, options.ProjectileShotgunSprayStacks + 1);
                    RebuildZombieModeProjectileSpreadState();
                    break;
                case ZombieModeRewardType.ProjectileStasis:
                    options.ProjectileStasisStacks = Mathf.Min(2, options.ProjectileStasisStacks + 1);
                    break;
                case ZombieModeRewardType.ProjectileRicochet:
                    options.ProjectileRicochetStacks = Mathf.Min(1, options.ProjectileRicochetStacks + 1);
                    break;
                case ZombieModeRewardType.ProjectileFork:
                    options.ProjectileForkStacks = Mathf.Min(1, options.ProjectileForkStacks + 1);
                    break;
                case ZombieModeRewardType.ProjectileReturn:
                    options.ProjectileReturnStacks = Mathf.Min(1, options.ProjectileReturnStacks + 1);
                    break;
                case ZombieModeRewardType.ProjectileHelix:
                    options.ProjectileHelixStacks = Mathf.Min(1, options.ProjectileHelixStacks + 1);
                    break;
                case ZombieModeRewardType.ProjectileTrail:
                    options.ProjectileTrailStacks = Mathf.Min(1, options.ProjectileTrailStacks + 1);
                    break;
                case ZombieModeRewardType.BattlefieldPurgeAura:
                    options.BattlefieldPurgeAuraStacks = Mathf.Min(2, options.BattlefieldPurgeAuraStacks + 1);
                    StartZombieModeBattlefieldAreaRuntimeIfNeeded(zombieModeRunState.RunId);
                    break;
                case ZombieModeRewardType.BattlefieldCurseTrap:
                    options.BattlefieldCurseTrapStacks = Mathf.Min(2, options.BattlefieldCurseTrapStacks + 1);
                    StartZombieModeBattlefieldAreaRuntimeIfNeeded(zombieModeRunState.RunId);
                    break;
                case ZombieModeRewardType.BattlefieldBlackHole:
                    options.BattlefieldBlackHoleStacks = Mathf.Min(2, options.BattlefieldBlackHoleStacks + 1);
                    StartZombieModeBattlefieldGravityRuntimeIfNeeded(zombieModeRunState.RunId);
                    break;
                case ZombieModeRewardType.BattlefieldGravityDrag:
                    options.BattlefieldGravityDragStacks = Mathf.Min(2, options.BattlefieldGravityDragStacks + 1);
                    StartZombieModeBattlefieldGravityRuntimeIfNeeded(zombieModeRunState.RunId);
                    break;
                case ZombieModeRewardType.TriggerLifesteal:
                    ApplyZombieModeLifestealReward(10, 1);
                    break;
                case ZombieModeRewardType.TriggerLifestealMedium:
                    ApplyZombieModeLifestealReward(20, 1);
                    break;
                case ZombieModeRewardType.TriggerLifestealLarge:
                    ApplyZombieModeLifestealReward(30, 1);
                    break;
                case ZombieModeRewardType.TriggerCritBurst:
                    options.TriggerCritBurstStacks = Mathf.Min(3, options.TriggerCritBurstStacks + 1);
                    break;
                case ZombieModeRewardType.TriggerPurificationSiphon:
                    options.TriggerPurificationSiphonStacks = Mathf.Min(2, options.TriggerPurificationSiphonStacks + 1);
                    break;
                case ZombieModeRewardType.TriggerSecondWind:
                    options.TriggerSecondWindStacks = Mathf.Min(5, options.TriggerSecondWindStacks + 1);
                    break;
                case ZombieModeRewardType.TriggerDoomPulse:
                    options.TriggerDoomPulseStacks = Mathf.Min(3, options.TriggerDoomPulseStacks + 1);
                    break;
                case ZombieModeRewardType.MutatorCritFocus:
                    options.MutatorCritFocusStacks = Mathf.Min(3, options.MutatorCritFocusStacks + 1);
                    RebuildZombieModeOptionPersistentModifiers();
                    break;
                case ZombieModeRewardType.MutatorBulletTime:
                    options.MutatorBulletTimeEnabled = true;
                    EnsureZombieModeOptionPlayerHealthListener();
                    break;
                case ZombieModeRewardType.MutatorGuardianShield:
                    options.MutatorGuardianShieldEnabled = true;
                    EnsureZombieModeOptionPlayerHealthListener();
                    CharacterMainControl mainPlayer = CharacterMainControl.Main;
                    HandleZombieModePlayerHealthChangedForOptions(mainPlayer != null ? mainPlayer.Health : null);
                    break;
                case ZombieModeRewardType.MutatorQuickReload:
                    options.MutatorQuickReloadStacks = Mathf.Min(3, options.MutatorQuickReloadStacks + 1);
                    RebuildZombieModeOptionPersistentModifiers();
                    break;
                case ZombieModeRewardType.MutatorDashBoost:
                    options.MutatorDashBoostStacks = Mathf.Min(2, options.MutatorDashBoostStacks + 1);
                    RebuildZombieModeOptionPersistentModifiers();
                    break;
                case ZombieModeRewardType.BattlefieldAmmoRain:
                    options.BattlefieldAmmoRainStacks = Mathf.Min(2, options.BattlefieldAmmoRainStacks + 1);
                    StartZombieModeAmmoRainIfNeeded(zombieModeRunState.RunId);
                    break;
                default:
                    return false;
            }

            ApplyZombieModeOptionTradeoff(rewardType);
            NotificationText.Push(GetZombieModeRewardDisplayText(zombieModeRunState.RunId, rewardType));
            return true;
        }

        private bool IsZombieModePhase2ContractReward(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.ContractDevilBargain:
                case ZombieModeRewardType.ContractCursedReload:
                case ZombieModeRewardType.ContractBloodPrice:
                case ZombieModeRewardType.ContractCursePool:
                    return true;
                default:
                    return false;
            }
        }

        private int GetZombieModeLifestealRewardChanceGain(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.TriggerLifesteal:
                    return 10;
                case ZombieModeRewardType.TriggerLifestealMedium:
                    return 20;
                case ZombieModeRewardType.TriggerLifestealLarge:
                    return 30;
                default:
                    return 0;
            }
        }

        private void ApplyZombieModeLifestealReward(int chanceGainPercent, int healAmount)
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            int acceptedGain = Mathf.Clamp(
                chanceGainPercent,
                0,
                Mathf.Max(0, ZombieModeLifestealChanceCapPercent - options.TriggerLifestealChancePercent));
            if (acceptedGain <= 0)
            {
                return;
            }

            options.TriggerLifestealStacks++;
            options.TriggerLifestealChancePercent = Mathf.Min(
                ZombieModeLifestealChanceCapPercent,
                options.TriggerLifestealChancePercent + acceptedGain);
            options.TriggerLifestealHealAmount = Mathf.Max(options.TriggerLifestealHealAmount, healAmount);
            RebuildZombieModeOptionPersistentModifiers();
        }

        private void ApplyZombieModeOptionTradeoff(ZombieModeRewardType rewardType)
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            float moveSpeedPenalty = GetZombieModeOptionTradeoffMoveSpeedPenalty(rewardType);
            float gunDamagePenalty = GetZombieModeOptionTradeoffGunDamagePenalty(rewardType);
            float reloadSpeedPenalty = GetZombieModeOptionTradeoffReloadSpeedPenalty(rewardType);
            float damageTakenPenalty = GetZombieModeOptionTradeoffDamageTakenPenalty(rewardType);
            float maxHealthPenalty = GetZombieModeOptionTradeoffMaxHealthPenalty(rewardType);
            int pollutionGain = GetZombieModeOptionTradeoffPollutionGain(rewardType);

            if (moveSpeedPenalty > 0f)
            {
                options.OptionTradeoffMoveSpeedPenalty = Mathf.Clamp01(options.OptionTradeoffMoveSpeedPenalty + moveSpeedPenalty);
            }

            if (gunDamagePenalty > 0f)
            {
                options.OptionTradeoffGunDamagePenalty = Mathf.Clamp01(options.OptionTradeoffGunDamagePenalty + gunDamagePenalty);
            }

            if (reloadSpeedPenalty > 0f)
            {
                options.OptionTradeoffReloadSpeedPenalty = Mathf.Clamp01(options.OptionTradeoffReloadSpeedPenalty + reloadSpeedPenalty);
            }

            if (damageTakenPenalty > 0f)
            {
                options.OptionTradeoffDamageTakenPenalty = Mathf.Clamp01(options.OptionTradeoffDamageTakenPenalty + damageTakenPenalty);
            }

            if (maxHealthPenalty > 0f)
            {
                options.OptionTradeoffMaxHealthPenalty = Mathf.Clamp01(options.OptionTradeoffMaxHealthPenalty + maxHealthPenalty);
            }

            if (pollutionGain > 0)
            {
                zombieModeRunState.PollutionFromContracts += pollutionGain;
            }

            bool hasPercentTradeoff =
                moveSpeedPenalty > 0f ||
                gunDamagePenalty > 0f ||
                reloadSpeedPenalty > 0f ||
                damageTakenPenalty > 0f ||
                maxHealthPenalty > 0f;
            if (hasPercentTradeoff && GetZombieModeOptionTradeoffDisplayPercent(rewardType) <= 0)
            {
                DevLog("[ZombieMode] option reward tradeoff display percent missing: " + rewardType);
            }

            if (hasPercentTradeoff)
            {
                RebuildZombieModeOptionPersistentModifiers();
            }
        }

        private float GetZombieModeOptionTradeoffMoveSpeedPenalty(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.TriggerSecondWind:
                    return 0.08f;
                case ZombieModeRewardType.ProjectileStasis:
                    return 0.05f;
                case ZombieModeRewardType.ProjectileHelix:
                    return 0.06f;
                case ZombieModeRewardType.BattlefieldGravityDrag:
                    return 0.07f;
                case ZombieModeRewardType.TriggerLifesteal:
                    return 0.11f;
                case ZombieModeRewardType.TriggerLifestealMedium:
                    return 0.22f;
                case ZombieModeRewardType.TriggerLifestealLarge:
                    return 0.33f;
                default:
                    return 0f;
            }
        }

        private float GetZombieModeOptionTradeoffGunDamagePenalty(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.ProjectileBurn:
                    return 0.04f;
                case ZombieModeRewardType.MutatorGuardianShield:
                case ZombieModeRewardType.MutatorQuickReload:
                case ZombieModeRewardType.MutatorDashBoost:
                    return 0.05f;
                case ZombieModeRewardType.ProjectileShotgunSpray:
                case ZombieModeRewardType.ProjectileFork:
                    return 0.05f;
                default:
                    return 0f;
            }
        }

        private float GetZombieModeOptionTradeoffReloadSpeedPenalty(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.ProjectilePenetration:
                    return 0.06f;
                case ZombieModeRewardType.ProjectileCold:
                    return 0.05f;
                case ZombieModeRewardType.ProjectileTrident:
                    return 0.07f;
                case ZombieModeRewardType.ProjectileRicochet:
                    return 0.06f;
                case ZombieModeRewardType.MutatorCritFocus:
                    return 0.08f;
                default:
                    return 0f;
            }
        }

        private float GetZombieModeOptionTradeoffDamageTakenPenalty(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.TriggerCritBurst:
                    return 0.08f;
                case ZombieModeRewardType.ProjectileArmorBreak:
                    return 0.06f;
                case ZombieModeRewardType.ProjectileTrail:
                    return 0.06f;
                case ZombieModeRewardType.TriggerDoomPulse:
                    return 0.10f;
                case ZombieModeRewardType.MutatorBulletTime:
                    return 0.12f;
                default:
                    return 0f;
            }
        }

        private float GetZombieModeOptionTradeoffMaxHealthPenalty(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.ProjectilePoison:
                    return 0.05f;
                case ZombieModeRewardType.TriggerSecondWind:
                    return 0.06f;
                case ZombieModeRewardType.ProjectileReturn:
                    return 0.05f;
                case ZombieModeRewardType.BattlefieldCurseTrap:
                    return 0.08f;
                default:
                    return 0f;
            }
        }

        private int GetZombieModeOptionTradeoffPollutionGain(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.TriggerPurificationSiphon:
                case ZombieModeRewardType.BattlefieldPurgeAura:
                    return 1;
                default:
                    return 0;
            }
        }

        private int GetZombieModeOptionTradeoffPurificationCost(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.BattlefieldAmmoRain:
                    return 120;
                case ZombieModeRewardType.BattlefieldBlackHole:
                    return 180;
                // 契约需要消耗净化点数
                case ZombieModeRewardType.ContractPollutionDeal:
                    return zombieModeRunState.CurrentWave > 0 && zombieModeRunState.CurrentWave % 5 == 0 ? 150 : 80;
                case ZombieModeRewardType.ContractGearDeal:
                    return zombieModeRunState.CurrentWave > 0 && zombieModeRunState.CurrentWave % 5 == 0 ? 120 : 60;
                case ZombieModeRewardType.ContractHugePurification:
                    return 200;
                case ZombieModeRewardType.ContractInsurance:
                    return 80;
                case ZombieModeRewardType.ContractDevilBargain:
                    return zombieModeRunState.CurrentWave > 0 && zombieModeRunState.CurrentWave % 5 == 0 ? 200 : 120;
                case ZombieModeRewardType.ContractCursedReload:
                    return zombieModeRunState.CurrentWave > 0 && zombieModeRunState.CurrentWave % 5 == 0 ? 100 : 60;
                case ZombieModeRewardType.ContractBloodPrice:
                    return zombieModeRunState.CurrentWave > 0 && zombieModeRunState.CurrentWave % 5 == 0 ? 80 : 50;
                case ZombieModeRewardType.ContractCursePool:
                    return zombieModeRunState.CurrentWave > 0 && zombieModeRunState.CurrentWave % 5 == 0 ? 150 : 100;
                default:
                    return 0;
            }
        }

        private bool TrySpendZombieModeOptionPurificationCost(ZombieModeRewardType rewardType)
        {
            int purificationCost = GetZombieModeOptionTradeoffPurificationCost(rewardType);
            if (purificationCost <= 0)
            {
                return true;
            }

            return SpendZombieModePurificationPoints(purificationCost, "OptionRewardTradeoff");
        }

        private int GetZombieModeOptionTradeoffDisplayPercent(ZombieModeRewardType rewardType)
        {
            float percent = Mathf.Max(
                GetZombieModeOptionTradeoffMoveSpeedPenalty(rewardType),
                GetZombieModeOptionTradeoffGunDamagePenalty(rewardType),
                GetZombieModeOptionTradeoffReloadSpeedPenalty(rewardType),
                GetZombieModeOptionTradeoffDamageTakenPenalty(rewardType),
                GetZombieModeOptionTradeoffMaxHealthPenalty(rewardType));
            return Mathf.RoundToInt(percent * 100f);
        }


        private void RemoveZombieModeOptionRuntimeEffects()
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            RemoveZombieModePhase2ContractRuntimeEffects();
            RestoreZombieModeProjectileSpreadState();
            UnregisterZombieModeOptionPlayerHealthListener();
            if (zombieModeSpreadSubscribedPlayer != null)
            {
                try
                {
                    zombieModeSpreadSubscribedPlayer.OnHoldAgentChanged -= OnZombieModeSpreadHoldAgentChanged;
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] spread hold listener remove failed: " + e.Message);
                }
            }

            zombieModeSpreadSubscribedPlayer = null;
            zombieModeOptionRuntimeCleanupRegistered = false;
            zombieModeOptionExplosionSkipLogTime = -999f;
            RuntimeStatModifierTracker.RemoveAll(options.GuardianShieldRecords, "ZombieMode Option GuardianShield");
            RuntimeStatModifierTracker.RemoveAll(options.ModifierRecords, "ZombieMode Option Persistent");
            zombieModeRunState.OptionRuntime.Reset();
        }

        private bool ApplyZombieModePhase2ContractReward(ZombieModeRewardType rewardType, bool bossNode)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.ContractDevilBargain:
                    return ApplyZombieModeContractDevilBargain(bossNode);
                case ZombieModeRewardType.ContractCursedReload:
                    return ApplyZombieModeContractCursedReload(bossNode);
                case ZombieModeRewardType.ContractBloodPrice:
                    return ApplyZombieModeContractBloodPrice(bossNode);
                case ZombieModeRewardType.ContractCursePool:
                    return ApplyZombieModeContractCursePool(bossNode);
            }

            return false;
        }

        private void RemoveZombieModePhase2ContractRuntimeEffects()
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            RuntimeStatModifierTracker.RemoveAll(
                options.ContractRuntimeModifierRecords,
                "ZombieMode Contract Runtime");
        }

        private bool ApplyZombieModeContractDevilBargain(bool bossNode)
        {
            int pollution = bossNode ? 4 : 3;
            int pointsCost = bossNode ? 200 : 120;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractDevilBargain"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += pollution;
            ApplyZombieModeInsuranceReward(0.25f, true);
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractDevilBargainCost"),
                pollution,
                pointsCost));
            return true;
        }

        private bool ApplyZombieModeContractCursedReload(bool bossNode)
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            int pollution = bossNode ? 3 : 2;
            int pointsCost = bossNode ? 100 : 60;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractCursedReload"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += pollution;
            TryAddZombieModeOptionModifier(
                CharacterMainControl.Main,
                ZombieModeStatNames.ReloadSpeedGain,
                bossNode ? 0.45f : 0.35f,
                ModifierType.Add,
                options.ContractRuntimeModifierRecords,
                "ZombieMode Contract CursedReload");
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractCursedReloadCost"),
                pollution,
                pointsCost));
            return true;
        }

        private bool ApplyZombieModeContractBloodPrice(bool bossNode)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            int pollution = bossNode ? 3 : 2;
            int pointsCost = bossNode ? 80 : 50;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractBloodPrice"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += pollution;
            if (player != null && player.Health != null)
            {
                float heal = bossNode ? player.Health.MaxHealth * 0.45f : player.Health.MaxHealth * 0.30f;
                player.Health.AddHealth(Mathf.Max(10f, heal));
            }

            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractBloodPriceCost"),
                pollution,
                pointsCost));
            return true;
        }

        private bool ApplyZombieModeContractCursePool(bool bossNode)
        {
            int pollution = bossNode ? 4 : 3;
            int pointsCost = bossNode ? 150 : 100;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractCursePool"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += pollution;
            if (UnityEngine.Random.value < 0.5f)
            {
                ApplyZombieModeInsuranceReward(0.15f, false);
                GrantZombieModeMerchantPurchaseGuarantee();
            }
            else
            {
                ApplyZombieModeInsuranceReward(0.10f, false);
                GrantZombieModeContractGearDealRewardOnly();
            }

            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractCursePoolCost"),
                pollution,
                pointsCost));
            return true;
        }
    }
}
