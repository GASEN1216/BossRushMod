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
        private const int ZombieModeLifestealChanceCapPercent = 50;

        private sealed class ZombieModeProjectileSpreadSnapshot
        {
            public Item Item;
            public readonly System.Collections.Generic.List<ZombieModeAttributeModifierRecord> ModifierRecords =
                new System.Collections.Generic.List<ZombieModeAttributeModifierRecord>();
        }

        private UnityEngine.Events.UnityAction<Health> zombieModeOptionPlayerHealthChangeHandler;
        private Health zombieModeOptionPlayerHealth;
        private float zombieModeOptionExplosionSkipLogTime = -999f;
        private CharacterMainControl zombieModeSpreadSubscribedPlayer;
        private bool zombieModeOptionRuntimeCleanupRegistered;
        private readonly System.Collections.Generic.Dictionary<int, ZombieModeProjectileSpreadSnapshot> zombieModeProjectileSpreadSnapshots =
            new System.Collections.Generic.Dictionary<int, ZombieModeProjectileSpreadSnapshot>();
        private readonly System.Collections.Generic.List<int> zombieModeProjectileSpreadRestoreScratch =
            new System.Collections.Generic.List<int>();

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
                    HandleZombieModePlayerHealthChangedForOptions(CharacterMainControl.Main != null ? CharacterMainControl.Main.Health : null);
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

        private void RebuildZombieModeProjectileSpreadState()
        {
            EnsureZombieModeProjectileSpreadListener();
            CharacterMainControl player = CharacterMainControl.Main;
            DuckovItemAgent holdAgent = player != null ? player.CurrentHoldItemAgent : null;
            Item holdItem = holdAgent != null ? holdAgent.Item : null;
            if (holdItem == null)
            {
                RestoreZombieModeProjectileSpreadState();
                return;
            }

            ItemSetting_Gun gunSetting = holdItem.GetComponent<ItemSetting_Gun>();
            if (gunSetting == null || holdItem.Stats == null)
            {
                RestoreZombieModeProjectileSpreadState();
                return;
            }

            RestoreZombieModeProjectileSpreadStateExcept(holdItem);
            RestoreExistingZombieModeProjectileSpreadSnapshot(holdItem);
            ZombieModeProjectileSpreadSnapshot snapshot = CaptureZombieModeProjectileSpreadSnapshot(holdItem);
            if (snapshot == null)
            {
                return;
            }

            Stat shotCountStat = holdItem.Stats.GetStat("ShotCount");
            Stat shotAngleStat = holdItem.Stats.GetStat("ShotAngle");
            Stat damageStat = holdItem.Stats.GetStat("Damage");
            if (shotCountStat == null || shotAngleStat == null || damageStat == null)
            {
                RestoreZombieModeProjectileSpreadSnapshot(snapshot);
                zombieModeProjectileSpreadSnapshots.Remove(holdItem.GetInstanceID());
                return;
            }

            float currentShotCount = shotCountStat.Value;
            float currentShotAngle = shotAngleStat.Value;
            int shotCount = Mathf.Max(1, Mathf.RoundToInt(currentShotCount));
            float shotAngle = Mathf.Max(0f, currentShotAngle);
            if (zombieModeRunState.OptionRuntime.ProjectileTridentStacks > 0)
            {
                shotCount = Mathf.Max(shotCount, 3);
                shotAngle = Mathf.Max(shotAngle, 8f);
            }

            if (zombieModeRunState.OptionRuntime.ProjectileShotgunSprayStacks > 0)
            {
                shotCount = Mathf.Max(shotCount, 5);
                shotAngle = Mathf.Max(shotAngle, 18f);
            }

            float damageSplitMultiplier = CalculateZombieModeProjectileSpreadDamageMultiplier(currentShotCount, shotCount);
            TryAddZombieModeGunStatRuntimeModifier(snapshot, holdItem, "ShotCount", shotCount - currentShotCount);
            TryAddZombieModeGunStatRuntimeModifier(snapshot, holdItem, "ShotAngle", shotAngle - currentShotAngle);
            TryAddZombieModeGunStatRuntimePercentageModifier(snapshot, holdItem, "Damage", damageSplitMultiplier - 1f);
        }

        private float CalculateZombieModeProjectileSpreadDamageMultiplier(float originalShotCount, int appliedShotCount)
        {
            float safeOriginalShotCount = Mathf.Max(1f, originalShotCount);
            int safeAppliedShotCount = Mathf.Max(1, appliedShotCount);
            return Mathf.Clamp(safeOriginalShotCount / safeAppliedShotCount, 0.05f, 1f);
        }

        internal bool CanTriggerZombieModeProjectileTrailDamage(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            float now = GetZombieModeRuntimeNow();
            if (now - options.LastProjectileTrailDamageTime < 0.06f)
            {
                return false;
            }

            options.LastProjectileTrailDamageTime = now;
            return true;
        }

        private void RestoreZombieModeProjectileSpreadState()
        {
            foreach (var pair in zombieModeProjectileSpreadSnapshots)
            {
                RestoreZombieModeProjectileSpreadSnapshot(pair.Value);
            }

            zombieModeProjectileSpreadSnapshots.Clear();
        }

        private void RestoreZombieModeProjectileSpreadStateExcept(Item exceptItem)
        {
            zombieModeProjectileSpreadRestoreScratch.Clear();
            foreach (var pair in zombieModeProjectileSpreadSnapshots)
            {
                ZombieModeProjectileSpreadSnapshot snapshot = pair.Value;
                if (snapshot != null && object.ReferenceEquals(snapshot.Item, exceptItem))
                {
                    continue;
                }

                RestoreZombieModeProjectileSpreadSnapshot(snapshot);
                zombieModeProjectileSpreadRestoreScratch.Add(pair.Key);
            }

            for (int i = 0; i < zombieModeProjectileSpreadRestoreScratch.Count; i++)
            {
                zombieModeProjectileSpreadSnapshots.Remove(zombieModeProjectileSpreadRestoreScratch[i]);
            }

            zombieModeProjectileSpreadRestoreScratch.Clear();
        }

        private void RestoreZombieModeProjectileSpreadSnapshot(ZombieModeProjectileSpreadSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            RuntimeStatModifierTracker.RemoveAll(snapshot.ModifierRecords, "ZombieMode Projectile Spread");
        }

        private void EnsureZombieModeProjectileSpreadListener()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            if (zombieModeSpreadSubscribedPlayer == player)
            {
                return;
            }

            if (zombieModeSpreadSubscribedPlayer != null)
            {
                try
                {
                    zombieModeSpreadSubscribedPlayer.OnHoldAgentChanged -= OnZombieModeSpreadHoldAgentChanged;
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] spread hold listener swap failed: " + e.Message);
                }
            }

            zombieModeSpreadSubscribedPlayer = player;
            player.OnHoldAgentChanged += OnZombieModeSpreadHoldAgentChanged;
        }

        private void OnZombieModeSpreadHoldAgentChanged(DuckovItemAgent newAgent)
        {
            if (!IsZombieModeActive)
            {
                return;
            }

            if (zombieModeRunState.OptionRuntime.ProjectileTridentStacks <= 0 &&
                zombieModeRunState.OptionRuntime.ProjectileShotgunSprayStacks <= 0)
            {
                return;
            }

            RebuildZombieModeProjectileSpreadState();
        }

        private void TryAddZombieModeGunStatRuntimeModifier(ZombieModeProjectileSpreadSnapshot snapshot, Item item, string statName, float delta)
        {
            if (snapshot == null || item == null || item.Stats == null || string.IsNullOrEmpty(statName) || Mathf.Abs(delta) < 0.0001f)
            {
                return;
            }

            try
            {
                Stat stat = item.Stats.GetStat(statName);
                if (stat == null)
                {
                    return;
                }

                // Order 300 is intentional: spread overlays are a late runtime delta; revisit if ShotCount/ShotAngle gain multiplicative gear affixes.
                Modifier modifier = new Modifier(ModifierType.Add, delta, true, 300, snapshot);
                stat.AddModifier(modifier);
                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = item;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = statName;
                snapshot.ModifierRecords.Add(record);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] spread stat apply failed: " + statName + ", " + e.Message);
            }
        }

        private void TryAddZombieModeGunStatRuntimePercentageModifier(ZombieModeProjectileSpreadSnapshot snapshot, Item item, string statName, float percent)
        {
            if (snapshot == null || item == null || item.Stats == null || string.IsNullOrEmpty(statName) || Mathf.Abs(percent) < 0.0001f)
            {
                return;
            }

            try
            {
                Stat stat = item.Stats.GetStat(statName);
                if (stat == null)
                {
                    return;
                }

                // Order 300 is intentional: spread overlays are a late runtime delta; revisit if per-pellet Damage needs to compose differently with gun affixes.
                Modifier modifier = new Modifier(ModifierType.PercentageAdd, percent, true, 300, snapshot);
                stat.AddModifier(modifier);
                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = item;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = statName;
                snapshot.ModifierRecords.Add(record);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] spread percent stat apply failed: " + statName + ", " + e.Message);
            }
        }

        private void StartZombieModeBattlefieldAreaRuntimeIfNeeded(int runId)
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (!IsZombieModeRunValid(runId) ||
                (options.BattlefieldPurgeAuraStacks <= 0 && options.BattlefieldCurseTrapStacks <= 0) ||
                options.BattlefieldAreaRuntimeStarted)
            {
                return;
            }

            options.BattlefieldAreaRuntimeStarted = true;
            StartZombieModeCoroutine(ZombieModeBattlefieldAreaCoroutine(runId), runId);
        }

        private IEnumerator ZombieModeBattlefieldAreaCoroutine(int runId)
        {
            float nextAuraTime = GetZombieModeRuntimeNow() + 2.5f;
            float nextTrapTime = GetZombieModeRuntimeNow() + 10f;
            while (IsZombieModeRunValid(runId) &&
                   (zombieModeRunState.OptionRuntime.BattlefieldPurgeAuraStacks > 0 ||
                    zombieModeRunState.OptionRuntime.BattlefieldCurseTrapStacks > 0))
            {
                if (IsZombieModeRuntimePaused())
                {
                    yield return null;
                    continue;
                }

                ZombieModeCombatPhase phase = zombieModeRunState.CombatPhase;
                bool allowedPhase = phase == ZombieModeCombatPhase.InitialPreparation ||
                                    phase == ZombieModeCombatPhase.Preparation ||
                                    phase == ZombieModeCombatPhase.ExtractionOpportunity ||
                                    phase == ZombieModeCombatPhase.Combat;
                if (!allowedPhase)
                {
                    yield return null;
                    continue;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    yield return null;
                    continue;
                }

                float now = GetZombieModeRuntimeNow();
                int auraStacks = Mathf.Min(2, zombieModeRunState.OptionRuntime.BattlefieldPurgeAuraStacks);
                if (auraStacks > 0 && now >= nextAuraTime)
                {
                    float auraRadius = auraStacks >= 2 ? 4.5f : 3.2f;
                    float auraDamage = auraStacks >= 2 ? 35f : 20f;
                    DealZombieModeExplosionAreaDamage(runId, player, player.transform.position, auraRadius, auraDamage, false);
                    nextAuraTime = now + (auraStacks >= 2 ? 2f : 3f);
                }

                int trapStacks = Mathf.Min(2, zombieModeRunState.OptionRuntime.BattlefieldCurseTrapStacks);
                if (trapStacks > 0 && now >= nextTrapTime)
                {
                    Vector3 origin = player.transform.position + player.transform.forward * (trapStacks >= 2 ? 4f : 3f);
                    float radius = trapStacks >= 2 ? 4.5f : 3.5f;
                    float damage = trapStacks >= 2 ? 70f : 45f;
                    StartZombieModeTelegraphedAreaDamage(
                        runId,
                        player,
                        origin,
                        radius,
                        damage,
                        trapStacks >= 2 ? 0.8f : 1.2f,
                        L10n.T("BossRush_ZombieMode_Reward_BattlefieldCurseTrap"));
                    nextTrapTime = now + (trapStacks >= 2 ? 14f : 18f);
                }

                yield return null;
            }

            zombieModeRunState.OptionRuntime.BattlefieldAreaRuntimeStarted = false;
        }

        private void StartZombieModeBattlefieldGravityRuntimeIfNeeded(int runId)
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (!IsZombieModeRunValid(runId) ||
                (options.BattlefieldBlackHoleStacks <= 0 && options.BattlefieldGravityDragStacks <= 0) ||
                options.BattlefieldGravityRuntimeStarted)
            {
                return;
            }

            options.BattlefieldGravityRuntimeStarted = true;
            StartZombieModeCoroutine(ZombieModeBattlefieldGravityCoroutine(runId), runId);
        }

        private IEnumerator ZombieModeBattlefieldGravityCoroutine(int runId)
        {
            float nextWellTime = GetZombieModeRuntimeNow() + 4f;
            while (IsZombieModeRunValid(runId) &&
                   (zombieModeRunState.OptionRuntime.BattlefieldBlackHoleStacks > 0 ||
                    zombieModeRunState.OptionRuntime.BattlefieldGravityDragStacks > 0))
            {
                if (IsZombieModeRuntimePaused())
                {
                    yield return null;
                    continue;
                }

                ZombieModeCombatPhase phase = zombieModeRunState.CombatPhase;
                bool allowedPhase = phase == ZombieModeCombatPhase.InitialPreparation ||
                                    phase == ZombieModeCombatPhase.Preparation ||
                                    phase == ZombieModeCombatPhase.ExtractionOpportunity ||
                                    phase == ZombieModeCombatPhase.Combat;
                if (!allowedPhase)
                {
                    yield return null;
                    continue;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    yield return null;
                    continue;
                }

                float now = GetZombieModeRuntimeNow();
                if (now >= nextWellTime)
                {
                    ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
                    float radius = options.BattlefieldBlackHoleStacks > 0 ? 5.5f : 4.5f;
                    float duration = options.BattlefieldBlackHoleStacks > 0 ? 4.5f : 3.2f;
                    float pullStrength = options.BattlefieldGravityDragStacks > 0 ? 2.4f : 1.4f;
                    Vector3 origin = player.transform.position + player.transform.forward * (options.BattlefieldBlackHoleStacks > 0 ? 4.5f : 3.2f);

                    GameObject zone = CreateZombieModeFlatZoneVisual(
                        "ZombieMode_GravityWell",
                        origin + Vector3.up * 0.03f,
                        radius,
                        0.02f,
                        new Color(0.25f, 0.25f, 0.25f, 0.40f));
                    ZombieModeGravityWellRuntime runtime = zone.AddComponent<ZombieModeGravityWellRuntime>();
                    runtime.Initialize(runId, origin, radius, pullStrength, duration);
                    RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, zone, runtime, null);

                    nextWellTime = now + (options.BattlefieldBlackHoleStacks > 0 ? 12f : 16f);
                }

                yield return null;
            }

            zombieModeRunState.OptionRuntime.BattlefieldGravityRuntimeStarted = false;
        }

        internal void RefreshZombieModeGravityWellTargets(int runId, Vector3 origin, float radius, float pullStrength)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            int count = CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, false);
            if (count <= 0)
            {
                return;
            }

            for (int i = 0; i < zombieModeEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = zombieModeEnemyMarkerScratch[i];
                if (marker == null || marker.RunId != runId || marker.DeathSettled || marker.RemovedFromRuntime || marker.IsBoss)
                {
                    continue;
                }

                CharacterMainControl enemy = marker.Owner != null ? marker.Owner : marker.GetComponent<CharacterMainControl>();
                if (enemy == null || enemy.transform == null)
                {
                    continue;
                }

                Vector3 delta = origin - enemy.transform.position;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance <= 0.4f || distance > radius)
                {
                    continue;
                }

                Vector3 step = delta.normalized * Mathf.Min(distance - 0.35f, pullStrength);
                enemy.transform.position += step;

                AICharacterController ai = enemy.GetComponentInChildren<AICharacterController>();
                if (ai != null && player != null && player.mainDamageReceiver != null)
                {
                    ai.searchedEnemy = player.mainDamageReceiver;
                    try { ai.SetTarget(player.mainDamageReceiver.transform); } catch { }
                }
            }

            zombieModeEnemyMarkerScratch.Clear();
        }

        private ZombieModeProjectileSpreadSnapshot CaptureZombieModeProjectileSpreadSnapshot(Item item)
        {
            if (item == null || item.Stats == null)
            {
                return null;
            }

            ZombieModeProjectileSpreadSnapshot existing;
            int snapshotKey = item.GetInstanceID();
            if (zombieModeProjectileSpreadSnapshots.TryGetValue(snapshotKey, out existing))
            {
                if (existing != null && object.ReferenceEquals(existing.Item, item))
                {
                    RestoreZombieModeProjectileSpreadSnapshot(existing);
                    return existing;
                }

                RestoreZombieModeProjectileSpreadSnapshot(existing);
                zombieModeProjectileSpreadSnapshots.Remove(snapshotKey);
            }

            try
            {
                Stat shotCountStat = item.Stats.GetStat("ShotCount");
                Stat shotAngleStat = item.Stats.GetStat("ShotAngle");
                if (shotCountStat == null || shotAngleStat == null)
                {
                    return null;
                }

                ZombieModeProjectileSpreadSnapshot snapshot = new ZombieModeProjectileSpreadSnapshot();
                snapshot.Item = item;
                zombieModeProjectileSpreadSnapshots[snapshotKey] = snapshot;
                return snapshot;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] spread snapshot failed: " + e.Message);
                return null;
            }
        }

        private void RestoreExistingZombieModeProjectileSpreadSnapshot(Item item)
        {
            if (item == null)
            {
                return;
            }

            ZombieModeProjectileSpreadSnapshot existing;
            int snapshotKey = item.GetInstanceID();
            if (zombieModeProjectileSpreadSnapshots.TryGetValue(snapshotKey, out existing) &&
                existing != null &&
                object.ReferenceEquals(existing.Item, item))
            {
                RestoreZombieModeProjectileSpreadSnapshot(existing);
            }
        }

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

        private void HandleZombieModeOptionHealthHurt(
            int runId,
            Health health,
            DamageInfo damageInfo,
            CharacterMainControl victim,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (!IsZombieModeRunValid(runId) ||
                health == null ||
                victim == null ||
                marker == null ||
                marker.RunId != zombieModeRunState.RunId ||
                marker.DeathSettled ||
                marker.RemovedFromRuntime ||
                damageInfo.fromCharacter != CharacterMainControl.Main ||
                damageInfo.isFromBuffOrEffect ||
                !IsZombieModeKnownEnemy(victim))
            {
                return;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (options.TriggerLifestealChancePercent > 0)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                int chance = Mathf.Min(ZombieModeLifestealChanceCapPercent, options.TriggerLifestealChancePercent);
                if (player != null &&
                    player.Health != null &&
                    UnityEngine.Random.value <= chance / 100f)
                {
                    player.Health.AddHealth(Mathf.Max(1, options.TriggerLifestealHealAmount));
                }
            }

            if (options.TriggerCritBurstStacks > 0 && damageInfo.crit > 0)
            {
                float now = GetZombieModeRuntimeNow();
                if (now - options.LastCritBurstTriggerTime >= 0.15f)
                {
                    int stacks = Mathf.Min(3, options.TriggerCritBurstStacks);
                    options.LastCritBurstTriggerTime = now;
                    CreateZombieModeOptionExplosion(
                        runId,
                        victim.transform.position,
                        1.5f + 0.25f * (stacks - 1),
                        Mathf.Max(1f, damageInfo.finalDamage * (0.30f + 0.10f * (stacks - 1))));
                }
            }

            if (options.ProjectileStasisStacks > 0 && IsZombieModePlayerProjectileDamage(damageInfo))
            {
                int stacks = Mathf.Min(2, options.ProjectileStasisStacks);
                TryApplyZombieModeEnemyStasis(victim, marker, 0.65f + 0.15f * (stacks - 1), 1.0f + 0.4f * (stacks - 1));
            }

            if (IsZombieModePlayerProjectileDamage(damageInfo))
            {
                float now = GetZombieModeRuntimeNow();
                if (now - options.LastTrajectorySupportTriggerTime >= 0.08f)
                {
                    bool spawnedSupportProjectile = false;
                    if (options.ProjectileRicochetStacks > 0)
                    {
                        CharacterMainControl nearest = TryFindZombieModeNearestEnemyTarget(runId, victim, 9f);
                        if (nearest != null)
                        {
                            Vector3 direction = (nearest.transform.position - victim.transform.position).normalized;
                            spawnedSupportProjectile |= TrySpawnZombieModePlayerSupportProjectile(victim.transform.position + Vector3.up * 0.2f, direction, 0.40f, 0.65f);
                        }
                    }

                    if (options.ProjectileForkStacks > 0)
                    {
                        Vector3 baseDirection = (victim.transform.position - CharacterMainControl.Main.transform.position);
                        baseDirection.y = 0f;
                        if (baseDirection.sqrMagnitude <= 0.001f)
                        {
                            baseDirection = CharacterMainControl.Main.transform.forward;
                        }
                        baseDirection.Normalize();
                        spawnedSupportProjectile |= TrySpawnZombieModePlayerSupportProjectile(victim.transform.position + Vector3.up * 0.2f, Quaternion.Euler(0f, -20f, 0f) * baseDirection, 0.35f, 0.55f);
                        spawnedSupportProjectile |= TrySpawnZombieModePlayerSupportProjectile(victim.transform.position + Vector3.up * 0.2f, Quaternion.Euler(0f, 20f, 0f) * baseDirection, 0.35f, 0.55f);
                    }

                    if (options.ProjectileReturnStacks > 0)
                    {
                        Vector3 returnDirection = (CharacterMainControl.Main.transform.position - victim.transform.position);
                        returnDirection.y = 0f;
                        if (returnDirection.sqrMagnitude > 0.001f)
                        {
                            spawnedSupportProjectile |= TrySpawnZombieModePlayerSupportProjectile(victim.transform.position + Vector3.up * 0.2f, returnDirection.normalized, 0.45f, 0.70f);
                        }
                    }

                    if (spawnedSupportProjectile)
                    {
                        options.LastTrajectorySupportTriggerTime = now;
                    }
                }
            }
        }

        private void HandleZombieModeOptionHealthDead(
            int runId,
            Health health,
            DamageInfo damageInfo,
            CharacterMainControl victim,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (!IsZombieModeRunValid(runId) ||
                health == null ||
                victim == null ||
                marker == null ||
                marker.RunId != zombieModeRunState.RunId ||
                damageInfo.fromCharacter != CharacterMainControl.Main ||
                damageInfo.isFromBuffOrEffect)
            {
                return;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (options.TriggerPurificationSiphonStacks > 0)
            {
                int stacks = Mathf.Min(2, options.TriggerPurificationSiphonStacks);
                int bonus = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(1, marker.PurificationPointValue) * 0.20f * stacks));
                SpawnZombieModeDeathStars(runId, victim.transform.position, bonus, 1);
            }

            if (options.TriggerSecondWindStacks > 0)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.Health != null)
                {
                    player.Health.AddHealth(Mathf.Min(10f, 2f * Mathf.Min(5, options.TriggerSecondWindStacks)));
                }
            }

            if (options.TriggerDoomPulseStacks > 0)
            {
                int stacks = Mathf.Min(3, options.TriggerDoomPulseStacks);
                options.DoomPulseKillCounter++;
                int interval = Mathf.Max(20, 40 - 6 * (stacks - 1));
                if (options.DoomPulseKillCounter >= interval)
                {
                    options.DoomPulseKillCounter = 0;
                    TriggerZombieModeDoomPulse(runId, stacks);
                }
            }
        }

        private void TriggerZombieModeDoomPulse(int runId, int stacks)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (!IsZombieModeRunValid(runId) || player == null)
            {
                return;
            }

            Vector3 center = player.transform.position;
            Vector3 forward = player.transform.forward;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.forward;
            }

            float radius = 2.75f;
            float damage = 30f + 10f * (stacks - 1);
            float offsetDistance = 1.5f + 0.25f * (stacks - 1);
            for (int i = 0; i < 3; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 120f * i, 0f) * forward * offsetDistance;
                CreateZombieModeOptionExplosion(runId, center + offset, radius, damage);
            }
        }

        private void CreateZombieModeOptionExplosion(int runId, Vector3 position, float radius, float damage)
        {
            if (!IsZombieModeRunValid(runId) || CharacterMainControl.Main == null)
            {
                return;
            }

            if (LevelManager.Instance == null || LevelManager.Instance.ExplosionManager == null)
            {
                float now = GetZombieModeRuntimeNow();
                if (now - zombieModeOptionExplosionSkipLogTime >= 5f)
                {
                    zombieModeOptionExplosionSkipLogTime = now;
                    DevLog("[ZombieMode] option explosion skipped: ExplosionManager unavailable");
                }
                return;
            }

            try
            {
                DamageInfo info = new DamageInfo(CharacterMainControl.Main);
                info.damageValue = damage;
                info.damagePoint = position;
                Vector3 normal = CharacterMainControl.Main.transform.position - position;
                info.damageNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
                info.isExplosion = true;
                info.isFromBuffOrEffect = true;
                LevelManager.Instance.ExplosionManager.CreateExplosion(
                    position,
                    radius,
                    info,
                    ExplosionFxTypes.normal,
                    0.35f,
                    false);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] option explosion failed: " + e.Message);
            }
        }

        private void StartZombieModeAmmoRainIfNeeded(int runId)
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (!IsZombieModeRunValid(runId) || options.BattlefieldAmmoRainStacks <= 0 || options.AmmoRainCoroutineStarted)
            {
                return;
            }

            options.AmmoRainCoroutineStarted = true;
            StartZombieModeCoroutine(ZombieModeAmmoRainCoroutine(runId), runId);
        }

        private IEnumerator ZombieModeAmmoRainCoroutine(int runId)
        {
            int stacks = Mathf.Min(2, zombieModeRunState.OptionRuntime.BattlefieldAmmoRainStacks);
            float nextGrantDelay = stacks >= 2 ? 35f : 45f;
            float countdownRemaining = nextGrantDelay;
            float previousNow = GetZombieModeRuntimeNow();
            while (IsZombieModeRunValid(runId) && zombieModeRunState.OptionRuntime.BattlefieldAmmoRainStacks > 0)
            {
                float now = GetZombieModeRuntimeNow();
                float deltaTime = Mathf.Max(0f, now - previousNow);
                previousNow = now;
                // 暂停帧丢弃 deltaTime；previousNow 必须先更新，避免恢复时一次性补扣。
                if (IsZombieModeRuntimePaused())
                {
                    yield return null;
                    continue;
                }

                ZombieModeCombatPhase phase = zombieModeRunState.CombatPhase;
                bool allowedPhase = phase == ZombieModeCombatPhase.InitialPreparation ||
                                    phase == ZombieModeCombatPhase.Preparation ||
                                    phase == ZombieModeCombatPhase.ExtractionOpportunity ||
                                    phase == ZombieModeCombatPhase.Combat;
                if (!allowedPhase)
                {
                    yield return null;
                    continue;
                }

                stacks = Mathf.Min(2, zombieModeRunState.OptionRuntime.BattlefieldAmmoRainStacks);
                nextGrantDelay = stacks >= 2 ? 35f : 45f;
                countdownRemaining = Mathf.Min(countdownRemaining, nextGrantDelay);
                int amount = stacks >= 2 ? 90 : 60;
                countdownRemaining -= deltaTime;
                if (countdownRemaining <= 0f)
                {
                    GrantZombieModeAmmoRainSupply(amount);
                    countdownRemaining = nextGrantDelay;
                }

                yield return null;
            }
        }

        private void GrantZombieModeAmmoRainSupply(int amount)
        {
            string caliber = !string.IsNullOrEmpty(zombieModeRunState.StarterAmmoCaliber)
                ? zombieModeRunState.StarterAmmoCaliber
                : string.Empty;
            if (!string.IsNullOrEmpty(caliber) && TryGiveZombieModeStarterAmmo(caliber, amount))
            {
                return;
            }

            if (!TryGiveRandomItemByTags(new string[] { "Ammo" }, 1, 4))
            {
                TryGiveRandomItemByTags(new string[] { "Bullet" }, 1, 4);
            }
        }

        private void TryApplyZombieModeEnemyStasis(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker, float slowPercent, float duration)
        {
            if (enemy == null || marker == null || marker.IsBoss || enemy.CharacterItem == null)
            {
                return;
            }

            ZombieModeEnemyStasisRuntime runtime = enemy.gameObject.GetComponent<ZombieModeEnemyStasisRuntime>();
            if (runtime == null)
            {
                runtime = enemy.gameObject.AddComponent<ZombieModeEnemyStasisRuntime>();
            }

            runtime.Apply(marker.RunId, slowPercent, duration);
        }

        private CharacterMainControl TryFindZombieModeNearestEnemyTarget(int runId, CharacterMainControl exclude, float radius)
        {
            CharacterMainControl result = null;
            float bestSqr = radius * radius;
            int count = CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, false);
            if (count <= 0)
            {
                return null;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            Vector3 origin = exclude != null ? exclude.transform.position : (main != null ? main.transform.position : Vector3.zero);
            for (int i = 0; i < zombieModeEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = zombieModeEnemyMarkerScratch[i];
                if (marker == null || marker.RunId != runId || marker.DeathSettled || marker.RemovedFromRuntime)
                {
                    continue;
                }

                CharacterMainControl enemy = marker.Owner != null ? marker.Owner : marker.GetComponent<CharacterMainControl>();
                if (enemy == null || enemy == exclude)
                {
                    continue;
                }

                float sqr = (enemy.transform.position - origin).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    result = enemy;
                }
            }

            zombieModeEnemyMarkerScratch.Clear();
            return result;
        }

        private bool TrySpawnZombieModePlayerSupportProjectile(Vector3 origin, Vector3 direction, float damageFactor, float distanceFactor)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || LevelManager.Instance == null || LevelManager.Instance.BulletPool == null)
            {
                return false;
            }

            ItemAgent_Gun gun = player.GetGun();
            if (gun == null || gun.GunItemSetting == null)
            {
                return false;
            }

            Projectile bulletPrefab = gun.GunItemSetting.bulletPfb;
            if (bulletPrefab == null && GameplayDataSettings.Prefabs != null)
            {
                bulletPrefab = GameplayDataSettings.Prefabs.DefaultBullet;
            }

            if (bulletPrefab == null)
            {
                return false;
            }

            Projectile bullet = LevelManager.Instance.BulletPool.GetABullet(bulletPrefab);
            if (bullet == null)
            {
                return false;
            }

            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : player.transform.forward;
            bullet.transform.position = origin;
            bullet.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

            ProjectileContext ctx = default(ProjectileContext);
            ctx.direction = direction;
            ctx.speed = gun.BulletSpeed;
            ctx.distance = gun.BulletDistance * distanceFactor;
            ctx.halfDamageDistance = ctx.distance * 0.5f;
            ctx.damage = Mathf.Max(1f, gun.Damage * damageFactor);
            ctx.penetrate = 0;
            ctx.critRate = 0f;
            ctx.critDamageFactor = gun.CritDamageFactor;
            ctx.armorPiercing = gun.ArmorPiercing;
            ctx.armorBreak = gun.ArmorBreak;
            ctx.fromCharacter = player;
            ctx.realFromCharacter = player;
            ctx.team = player.Team;
            ctx.fromWeaponItemID = 0;
            ctx.firstFrameCheck = false;
            bullet.Init(ctx);
            return true;
        }
    }

    public sealed class ZombieModeGravityWellRuntime : MonoBehaviour
    {
        private int runId;
        private Vector3 origin;
        private float radius;
        private float pullStrength;
        private float endTime;
        private float nextTickTime;

        public void Initialize(int newRunId, Vector3 newOrigin, float newRadius, float newPullStrength, float duration)
        {
            runId = newRunId;
            origin = newOrigin;
            radius = Mathf.Max(1f, newRadius);
            pullStrength = Mathf.Max(0.1f, newPullStrength);
            ModBehaviour inst = ModBehaviour.Instance;
            float now = inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime;
            endTime = now + Mathf.Max(0.5f, duration);
            nextTickTime = now;
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                Destroy(gameObject);
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            float now = inst.GetZombieModeRuntimeNow();
            if (now >= endTime)
            {
                Destroy(gameObject);
                return;
            }

            if (now < nextTickTime)
            {
                return;
            }

            nextTickTime = now + 0.2f;
            inst.RefreshZombieModeGravityWellTargets(runId, origin, radius, pullStrength);
        }
    }

    public sealed class ZombieModeEnemyStasisRuntime : MonoBehaviour
    {
        private int runId;
        private float slowPercent;
        private float endTime;
        private bool active;
        private readonly System.Collections.Generic.List<ZombieModeAttributeModifierRecord> stasisModifierRecords =
            new System.Collections.Generic.List<ZombieModeAttributeModifierRecord>();

        public void Apply(int newRunId, float newSlowPercent, float duration)
        {
            runId = newRunId;
            slowPercent = Mathf.Clamp01(newSlowPercent);
            ModBehaviour inst = ModBehaviour.Instance;
            float now = inst != null ? inst.GetZombieModeRuntimeNow() : Time.unscaledTime;
            endTime = Mathf.Max(endTime, now + Mathf.Max(0.1f, duration));
            EnsureModifiers();
        }

        private void Update()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            CharacterMainControl enemy = GetComponent<CharacterMainControl>();
            if (inst == null || inst.ZombieModeCurrentRunId != runId || enemy == null || enemy.Health == null || enemy.Health.CurrentHealth <= 0f)
            {
                ReleaseModifiers();
                Destroy(this);
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            if (inst.GetZombieModeRuntimeNow() >= endTime)
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
            if (active)
            {
                return;
            }

            CharacterMainControl enemy = GetComponent<CharacterMainControl>();
            if (enemy == null || enemy.CharacterItem == null)
            {
                return;
            }

            float debuff = -slowPercent;
            RuntimeStatModifierTracker.TryAdd(enemy, ZombieModeStatNames.MoveSpeed, debuff, this, stasisModifierRecords, "Enemy Stasis MoveSpeed");
            RuntimeStatModifierTracker.TryAdd(enemy, ZombieModeStatNames.WalkSpeed, debuff, this, stasisModifierRecords, "Enemy Stasis WalkSpeed");
            RuntimeStatModifierTracker.TryAdd(enemy, ZombieModeStatNames.RunSpeed, debuff, this, stasisModifierRecords, "Enemy Stasis RunSpeed");
            active = true;
        }

        private void ReleaseModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(stasisModifierRecords, "Enemy Stasis");
            active = false;
            endTime = 0f;
        }
    }

    public sealed class ZombieModePlayerProjectileRuntime : MonoBehaviour
    {
        private int runId;
        private bool helixEnabled;
        private float helixAmplitude;
        private float helixFrequency;
        private bool trailEnabled;
        private float trailRadius;
        private float trailDamage;
        private float nextTrailTime;
        private float elapsed;
        private Vector3 lastHelixOffset = Vector3.zero;

        public void Initialize(
            int newRunId,
            bool enableHelix,
            float amplitude,
            float frequency,
            bool enableTrail,
            float newTrailRadius,
            float newTrailDamage)
        {
            ResetRuntimeState();
            ClearRuntimeConfiguration();
            runId = newRunId;
            helixEnabled = enableHelix;
            helixAmplitude = amplitude;
            helixFrequency = frequency;
            trailEnabled = enableTrail;
            trailRadius = newTrailRadius;
            trailDamage = newTrailDamage;
        }

        public void ResetRuntimeState()
        {
            elapsed = 0f;
            nextTrailTime = 0f;
            lastHelixOffset = Vector3.zero;
        }

        public void ClearRuntimeConfiguration()
        {
            runId = 0;
            helixEnabled = false;
            helixAmplitude = 0f;
            helixFrequency = 0f;
            trailEnabled = false;
            trailRadius = 0f;
            trailDamage = 0f;
        }

        private void OnDisable()
        {
            ResetRuntimeState();
            ClearRuntimeConfiguration();
        }

        private void LateUpdate()
        {
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst == null || inst.ZombieModeCurrentRunId != runId)
            {
                Destroy(this);
                return;
            }

            if (inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            if (helixEnabled)
            {
                Vector3 forward = transform.forward;
                if (forward.sqrMagnitude <= 0.001f)
                {
                    forward = Vector3.forward;
                }

                forward.Normalize();
                Vector3 lateral = Vector3.Cross(Vector3.up, forward);
                if (lateral.sqrMagnitude <= 0.001f)
                {
                    lateral = Vector3.Cross(Vector3.forward, forward);
                }

                lateral.Normalize();
                Vector3 vertical = Vector3.Cross(forward, lateral).normalized;
                float phase = elapsed * helixFrequency;
                Vector3 offset = lateral * (Mathf.Sin(phase) * helixAmplitude);
                offset += vertical * (Mathf.Cos(phase) * helixAmplitude * 0.55f);
                transform.position += offset - lastHelixOffset;
                lastHelixOffset = offset;
            }

            if (trailEnabled)
            {
                float now = inst.GetZombieModeRuntimeNow();
                if (now >= nextTrailTime)
                {
                    nextTrailTime = now + 0.15f;
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null && inst.CanTriggerZombieModeProjectileTrailDamage(runId))
                    {
                        inst.DealZombieModeExplosionAreaDamage(runId, player, transform.position, trailRadius, trailDamage, false);
                    }
                }
            }
        }
    }
}
