using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BossRush.Utils;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string ZombieModeAttributeMaxHealthKey = "MaxHealth";
        private const string ZombieModeAttributeMoveSpeedKey = "MoveSpeed";
        private const string ZombieModeAttributeWalkSpeedKey = "WalkSpeed";
        private const string ZombieModeAttributeRunSpeedKey = "RunSpeed";
        private const string ZombieModeAttributeMeleeDamageKey = "MeleeDamageMultiplier";
        private const string ZombieModeAttributeRangedDamageKey = "GunDamageMultiplier";
        private const string ZombieModeAttributeReloadSpeedKey = "ReloadSpeedMultiplier";
        private const string ZombieModeAttributeDamageReductionKey = "ElementFactor_Physics";
        private const int ZombieModeContractGearDealMinQuality = 5;

        private GameObject zombieModeRewardUiRoot;

        private void ShowZombieModeRewardSelection(int runId, bool bossNode)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            zombieModeRunState.CombatPhase = ZombieModeCombatPhase.RewardSelection;
            EnsureZombieModeRewardNode(bossNode);
            ClearZombieModeRewardShell();
            zombieModeRewardUiRoot = new GameObject("ZombieMode_RewardSelection");
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.RewardUi, zombieModeRewardUiRoot, zombieModeRewardUiRoot, null);
            ZombieModeRewardSelectionView view = zombieModeRewardUiRoot.AddComponent<ZombieModeRewardSelectionView>();
            view.Initialize(runId, this);
        }

        private void EnsureZombieModeRewardNode(bool bossNode)
        {
            if (zombieModeRunState.CurrentRewardNode == null || zombieModeRunState.CurrentRewardNode.Wave != zombieModeRunState.CurrentWave)
            {
                zombieModeRunState.CurrentRewardNode = new ZombieModeRewardNode();
                zombieModeRunState.CurrentRewardNode.Wave = zombieModeRunState.CurrentWave;
                zombieModeRunState.CurrentRewardNode.BossNode = bossNode;
                zombieModeRunState.FreeRefreshesRemainingCurrentNode = Mathf.Clamp(
                    ZombieModeTuning.FreeRefreshCapPerNode + zombieModeRunState.PendingFreeRefreshNextNode,
                    0,
                    ZombieModeTuning.FreeRefreshCapPerNode);
                zombieModeRunState.PaidRefreshIndexCurrentNode = 0;
                zombieModeRunState.PendingFreeRefreshNextNode = 0;
                RollZombieModeRewardOptions();
                return;
            }

            zombieModeRunState.CurrentRewardNode.BossNode = bossNode;
            if (zombieModeRunState.CurrentRewardNode.Options.Count <= 0)
            {
                RollZombieModeRewardOptions();
            }
        }

        private void RollZombieModeRewardOptions()
        {
            ZombieModeRewardNode node = zombieModeRunState.CurrentRewardNode;
            if (node == null)
            {
                return;
            }

            node.Options.Clear();
            int optionCount = node.BossNode ? 4 : 3;
            int minimumCategoryCount = GetZombieModeMinimumRewardCategoryCount(node.BossNode);
            List<ZombieModeRewardCatalogEntry> catalog = BuildZombieModeRewardCatalogEntries(node.BossNode);
            List<ZombieModeRewardCategory> categories = new List<ZombieModeRewardCategory>();
            while (node.Options.Count < optionCount && catalog.Count > 0)
            {
                ZombieModeRewardCategory category = RollZombieModeRewardCategory(catalog, categories, node.Options.Count, minimumCategoryCount);
                ZombieModeRewardCatalogEntry entry = SelectZombieModeRewardEntryForCategory(catalog, category, node.Options);
                if (entry == null)
                {
                    entry = SelectZombieModeRewardEntryForCategory(catalog, ZombieModeRewardCategory.Economy, node.Options);
                }

                if (entry == null)
                {
                    break;
                }

                node.Options.Add(entry.RewardType);
                if (!categories.Contains(entry.Category))
                {
                    categories.Add(entry.Category);
                }
                catalog.Remove(entry);
            }

            EnsureZombieModeRewardCategoryDiversity(node, catalog, minimumCategoryCount);
        }

        private int GetZombieModeMinimumRewardCategoryCount(bool bossNode)
        {
            return bossNode ? 3 : 2;
        }

        private List<ZombieModeRewardCatalogEntry> BuildZombieModeRewardCatalogEntries(bool bossNode)
        {
            List<ZombieModeRewardCatalogEntry> entries = new List<ZombieModeRewardCatalogEntry>();
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.AttributeMaxHealth, ZombieModeRewardCategory.Attribute, 15);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.AttributeMoveSpeed, ZombieModeRewardCategory.Attribute, 12);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.AttributeMeleeDamage, ZombieModeRewardCategory.Attribute, 15);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.AttributeRangedDamage, ZombieModeRewardCategory.Attribute, 15);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.AttributeReloadSpeed, ZombieModeRewardCategory.Attribute, 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.AttributeDamageReduction, ZombieModeRewardCategory.Attribute, 10);

            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.RandomMeleeWeapon, ZombieModeRewardCategory.Equipment, 10);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.RandomGunWithAmmo, ZombieModeRewardCategory.Equipment, 10);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.AmmoSupply, ZombieModeRewardCategory.Equipment, 15);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.MedicalSupply, ZombieModeRewardCategory.Equipment, 15);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ArmorOrHelmet, ZombieModeRewardCategory.Equipment, 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.RandomSupply, ZombieModeRewardCategory.Equipment, 15);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.RandomHighQualityItem, ZombieModeRewardCategory.Equipment, bossNode ? 14 : 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.StarterReroll, ZombieModeRewardCategory.Equipment, 10);

            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.PurificationPoints, ZombieModeRewardCategory.Economy, 25);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.CurrentNodeFreeRefresh, ZombieModeRewardCategory.Economy, 12);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.NextNodeFreeRefresh, ZombieModeRewardCategory.Economy, 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.HalfPricePaidRefresh, ZombieModeRewardCategory.Economy, 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.Heal, ZombieModeRewardCategory.Economy, 8);

            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempMerchant, ZombieModeRewardCategory.Npc, 10);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempNurse, ZombieModeRewardCategory.Npc, 10);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempGoblinNpc, ZombieModeRewardCategory.Npc, 3);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempNurseNpc, ZombieModeRewardCategory.Npc, 3);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempCourierNpc, ZombieModeRewardCategory.Npc, 3);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.FortificationPack, ZombieModeRewardCategory.Fortification, 10);

            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractPollutionDeal, ZombieModeRewardCategory.Contract, zombieModeRunState.TotalPollution >= 15 ? 13 : 5);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractGearDeal, ZombieModeRewardCategory.Contract, 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractHugePurification, ZombieModeRewardCategory.Contract, 5);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractInsurance, ZombieModeRewardCategory.Contract, 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.InsuranceKeepOne, ZombieModeRewardCategory.Insurance, 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.InsuranceRandom10, ZombieModeRewardCategory.Insurance, 10);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.InsuranceRandom20, ZombieModeRewardCategory.Insurance, 6);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.InsuranceNearFull, ZombieModeRewardCategory.Insurance, 1);

            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.MapEventHighValueAirdrop, ZombieModeRewardCategory.MapEvent, 5);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.MapEventEliteSquad, ZombieModeRewardCategory.MapEvent, 5);

            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectilePenetration, ZombieModeRewardCategory.ProjectileMod, bossNode ? 8 : 10);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileBurn, ZombieModeRewardCategory.ProjectileMod, bossNode ? 8 : 10);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileCold, ZombieModeRewardCategory.ProjectileMod, bossNode ? 8 : 9);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectilePoison, ZombieModeRewardCategory.ProjectileMod, bossNode ? 8 : 10);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileArmorBreak, ZombieModeRewardCategory.ProjectileMod, 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.MutatorCritFocus, ZombieModeRewardCategory.Mutator, 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TriggerLifesteal, ZombieModeRewardCategory.Trigger, bossNode ? 7 : 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TriggerLifestealMedium, ZombieModeRewardCategory.Trigger, bossNode ? 5 : 6);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TriggerLifestealLarge, ZombieModeRewardCategory.Trigger, bossNode ? 3 : 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TriggerCritBurst, ZombieModeRewardCategory.Trigger, 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TriggerPurificationSiphon, ZombieModeRewardCategory.Trigger, 9);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TriggerSecondWind, ZombieModeRewardCategory.Trigger, bossNode ? 7 : 9);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TriggerDoomPulse, ZombieModeRewardCategory.Trigger, bossNode ? 8 : 6);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.MutatorBulletTime, ZombieModeRewardCategory.Mutator, 5);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.MutatorGuardianShield, ZombieModeRewardCategory.Mutator, bossNode ? 6 : 7);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.MutatorQuickReload, ZombieModeRewardCategory.Mutator, bossNode ? 7 : 8);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.MutatorDashBoost, ZombieModeRewardCategory.Mutator, bossNode ? 6 : 7);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.BattlefieldAmmoRain, ZombieModeRewardCategory.Battlefield, bossNode ? 5 : 6);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractDevilBargain, ZombieModeRewardCategory.Contract, 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractCursedReload, ZombieModeRewardCategory.Contract, 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractBloodPrice, ZombieModeRewardCategory.Contract, 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractCursePool, ZombieModeRewardCategory.Contract, 3);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileTrident, ZombieModeRewardCategory.ProjectileMod, 6);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileShotgunSpray, ZombieModeRewardCategory.ProjectileMod, 5);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileStasis, ZombieModeRewardCategory.ProjectileMod, 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileRicochet, ZombieModeRewardCategory.ProjectileMod, 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileFork, ZombieModeRewardCategory.ProjectileMod, 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileReturn, ZombieModeRewardCategory.ProjectileMod, 3);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileHelix, ZombieModeRewardCategory.ProjectileMod, 3);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileTrail, ZombieModeRewardCategory.ProjectileMod, 3);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.BattlefieldPurgeAura, ZombieModeRewardCategory.Battlefield, 5);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.BattlefieldCurseTrap, ZombieModeRewardCategory.Battlefield, 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.BattlefieldBlackHole, ZombieModeRewardCategory.Battlefield, 4);
            AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.BattlefieldGravityDrag, ZombieModeRewardCategory.Battlefield, 4);
            return entries;
        }

        private void AddZombieModeRewardCatalogEntry(
            List<ZombieModeRewardCatalogEntry> entries,
            ZombieModeRewardType rewardType,
            ZombieModeRewardCategory category,
            int weight)
        {
            if (entries == null || weight <= 0)
            {
                return;
            }

            if (IsZombieModeRewardAtSelectionCap(rewardType))
            {
                return;
            }

            if (IsZombieModeRewardUnaffordable(rewardType))
            {
                return;
            }

            float currentBonus = 0f;
            if (category == ZombieModeRewardCategory.Attribute)
            {
                string attributeKey = GetZombieModeAttributeKey(rewardType);
                if (!string.IsNullOrEmpty(attributeKey) && zombieModeRunState.AttributeBonuses.TryGetValue(attributeKey, out currentBonus))
                {
                    if (currentBonus >= GetZombieModeAttributeCap(rewardType))
                    {
                        weight = Mathf.Max(1, Mathf.FloorToInt(weight * 0.2f));
                    }
                }
            }

            ZombieModeRewardCatalogEntry entry = new ZombieModeRewardCatalogEntry();
            entry.RewardType = rewardType;
            entry.Category = category;
            entry.Weight = Mathf.Max(1, weight);
            entries.Add(entry);
        }

        private bool IsZombieModeRewardAtSelectionCap(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.TempGoblinNpc:
                    return FindZombieModeTemporaryRealNpc("Goblin") != null;
                case ZombieModeRewardType.TempNurseNpc:
                    return FindZombieModeTemporaryRealNpc("NurseNpc") != null;
                case ZombieModeRewardType.TempCourierNpc:
                    return FindZombieModeTemporaryRealNpc("Courier") != null;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            switch (rewardType)
            {
                case ZombieModeRewardType.ProjectilePenetration:
                    return options.ProjectilePenetrationStacks >= 3;
                case ZombieModeRewardType.ProjectileBurn:
                    return options.ProjectileBurnStacks >= 3;
                case ZombieModeRewardType.ProjectileCold:
                    return options.ProjectileColdStacks >= 3;
                case ZombieModeRewardType.ProjectilePoison:
                    return options.ProjectilePoisonStacks >= 3;
                case ZombieModeRewardType.ProjectileArmorBreak:
                    return options.ProjectileArmorBreakStacks >= 3;
                case ZombieModeRewardType.TriggerLifesteal:
                case ZombieModeRewardType.TriggerLifestealMedium:
                case ZombieModeRewardType.TriggerLifestealLarge:
                    return options.TriggerLifestealChancePercent + GetZombieModeLifestealRewardChanceGain(rewardType) > ZombieModeLifestealChanceCapPercent;
                case ZombieModeRewardType.TriggerCritBurst:
                    return options.TriggerCritBurstStacks >= 3;
                case ZombieModeRewardType.TriggerPurificationSiphon:
                    return options.TriggerPurificationSiphonStacks >= 2;
                case ZombieModeRewardType.TriggerSecondWind:
                    return options.TriggerSecondWindStacks >= 5;
                case ZombieModeRewardType.TriggerDoomPulse:
                    return options.TriggerDoomPulseStacks >= 3;
                case ZombieModeRewardType.MutatorCritFocus:
                    return options.MutatorCritFocusStacks >= 3;
                case ZombieModeRewardType.MutatorBulletTime:
                    return options.MutatorBulletTimeEnabled;
                case ZombieModeRewardType.MutatorGuardianShield:
                    return options.MutatorGuardianShieldEnabled;
                case ZombieModeRewardType.MutatorQuickReload:
                    return options.MutatorQuickReloadStacks >= 3;
                case ZombieModeRewardType.MutatorDashBoost:
                    return options.MutatorDashBoostStacks >= 2;
                case ZombieModeRewardType.BattlefieldAmmoRain:
                    return options.BattlefieldAmmoRainStacks >= 2;
                case ZombieModeRewardType.ProjectileTrident:
                    return options.ProjectileTridentStacks >= 1;
                case ZombieModeRewardType.ProjectileShotgunSpray:
                    return options.ProjectileShotgunSprayStacks >= 1;
                case ZombieModeRewardType.ProjectileStasis:
                    return options.ProjectileStasisStacks >= 2;
                case ZombieModeRewardType.ProjectileRicochet:
                    return options.ProjectileRicochetStacks >= 1;
                case ZombieModeRewardType.ProjectileFork:
                    return options.ProjectileForkStacks >= 1;
                case ZombieModeRewardType.ProjectileReturn:
                    return options.ProjectileReturnStacks >= 1;
                case ZombieModeRewardType.ProjectileHelix:
                    return options.ProjectileHelixStacks >= 1;
                case ZombieModeRewardType.ProjectileTrail:
                    return options.ProjectileTrailStacks >= 1;
                case ZombieModeRewardType.BattlefieldPurgeAura:
                    return options.BattlefieldPurgeAuraStacks >= 2;
                case ZombieModeRewardType.BattlefieldCurseTrap:
                    return options.BattlefieldCurseTrapStacks >= 2;
                case ZombieModeRewardType.BattlefieldBlackHole:
                    return options.BattlefieldBlackHoleStacks >= 2;
                case ZombieModeRewardType.BattlefieldGravityDrag:
                    return options.BattlefieldGravityDragStacks >= 2;
                default:
                    return false;
            }
        }

        private bool IsZombieModeRewardUnaffordable(ZombieModeRewardType rewardType)
        {
            int purificationCost = GetZombieModeOptionTradeoffPurificationCost(rewardType);
            return purificationCost > 0 && zombieModeRunState.PurificationPoints < purificationCost;
        }

        private int GetZombieModeRewardCategoryWeight(ZombieModeRewardCategory category)
        {
            int pollution = Mathf.Max(0, zombieModeRunState.TotalPollution);
            if (pollution >= 25)
            {
                switch (category)
                {
                    case ZombieModeRewardCategory.Attribute: return 20;
                    case ZombieModeRewardCategory.Equipment: return 15;
                    case ZombieModeRewardCategory.Economy: return 15;
                    case ZombieModeRewardCategory.Npc: return 12;
                    case ZombieModeRewardCategory.Fortification: return 10;
                    case ZombieModeRewardCategory.Contract: return 13;
                    case ZombieModeRewardCategory.Insurance: return 8;
                    case ZombieModeRewardCategory.ProjectileMod: return 9;
                    case ZombieModeRewardCategory.Trigger: return 11;
                    case ZombieModeRewardCategory.Mutator: return 8;
                    case ZombieModeRewardCategory.Battlefield: return 7;
                    default: return 7;
                }
            }

            if (pollution >= 15)
            {
                switch (category)
                {
                    case ZombieModeRewardCategory.Attribute: return 25;
                    case ZombieModeRewardCategory.Equipment: return 20;
                    case ZombieModeRewardCategory.Economy: return 15;
                    case ZombieModeRewardCategory.Npc: return 12;
                    case ZombieModeRewardCategory.Fortification: return 10;
                    case ZombieModeRewardCategory.Contract: return 8;
                    case ZombieModeRewardCategory.Insurance: return 5;
                    case ZombieModeRewardCategory.ProjectileMod: return 11;
                    case ZombieModeRewardCategory.Trigger: return 12;
                    case ZombieModeRewardCategory.Mutator: return 9;
                    case ZombieModeRewardCategory.Battlefield: return 6;
                    default: return 5;
                }
            }

            if (pollution >= 5)
            {
                switch (category)
                {
                    case ZombieModeRewardCategory.Attribute: return 30;
                    case ZombieModeRewardCategory.Equipment: return 25;
                    case ZombieModeRewardCategory.Economy: return 15;
                    case ZombieModeRewardCategory.Npc: return 12;
                    case ZombieModeRewardCategory.Fortification: return 8;
                    case ZombieModeRewardCategory.Contract: return 5;
                    case ZombieModeRewardCategory.Insurance: return 3;
                    case ZombieModeRewardCategory.ProjectileMod: return 13;
                    case ZombieModeRewardCategory.Trigger: return 12;
                    case ZombieModeRewardCategory.Mutator: return 10;
                    case ZombieModeRewardCategory.Battlefield: return 6;
                    default: return 2;
                }
            }

            switch (category)
            {
                case ZombieModeRewardCategory.Attribute: return 35;
                case ZombieModeRewardCategory.Equipment: return 25;
                case ZombieModeRewardCategory.Economy: return 15;
                case ZombieModeRewardCategory.Npc: return 10;
                case ZombieModeRewardCategory.Fortification: return 5;
                case ZombieModeRewardCategory.Contract: return 5;
                case ZombieModeRewardCategory.Insurance: return 3;
                case ZombieModeRewardCategory.ProjectileMod: return 14;
                case ZombieModeRewardCategory.Trigger: return 12;
                case ZombieModeRewardCategory.Mutator: return 10;
                case ZombieModeRewardCategory.Battlefield: return 5;
                default: return 2;
            }
        }

        private ZombieModeRewardCategory RollZombieModeRewardCategory(
            List<ZombieModeRewardCatalogEntry> catalog,
            List<ZombieModeRewardCategory> selectedCategories,
            int selectedCount,
            int minimumCategoryCount)
        {
            List<ZombieModeRewardCategory> categories = new List<ZombieModeRewardCategory>();
            for (int i = 0; i < catalog.Count; i++)
            {
                ZombieModeRewardCategory category = catalog[i].Category;
                if (!categories.Contains(category))
                {
                    categories.Add(category);
                }
            }

            int remainingSlots = zombieModeRunState.CurrentRewardNode != null
                ? (zombieModeRunState.CurrentRewardNode.BossNode ? 4 : 3) - selectedCount
                : 1;
            int missingCategories = minimumCategoryCount - (selectedCategories != null ? selectedCategories.Count : 0);
            bool mustPickNewCategory = missingCategories > 0 && remainingSlots <= missingCategories;
            int totalWeight = 0;
            for (int i = 0; i < categories.Count; i++)
            {
                ZombieModeRewardCategory category = categories[i];
                if (mustPickNewCategory && selectedCategories != null && selectedCategories.Contains(category))
                {
                    continue;
                }

                totalWeight += Mathf.Max(1, GetZombieModeRewardCategoryWeight(category));
            }

            if (totalWeight <= 0 && categories.Count > 0)
            {
                return categories[0];
            }

            int roll = Random.Range(0, totalWeight);
            for (int i = 0; i < categories.Count; i++)
            {
                ZombieModeRewardCategory category = categories[i];
                if (mustPickNewCategory && selectedCategories != null && selectedCategories.Contains(category))
                {
                    continue;
                }

                roll -= Mathf.Max(1, GetZombieModeRewardCategoryWeight(category));
                if (roll < 0)
                {
                    return category;
                }
            }

            return categories.Count > 0 ? categories[0] : ZombieModeRewardCategory.Economy;
        }

        private ZombieModeRewardCatalogEntry SelectZombieModeRewardEntryForCategory(
            List<ZombieModeRewardCatalogEntry> catalog,
            ZombieModeRewardCategory category,
            List<ZombieModeRewardType> existingOptions)
        {
            int totalWeight = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                ZombieModeRewardCatalogEntry entry = catalog[i];
                if (entry == null || entry.Category != category || existingOptions.Contains(entry.RewardType))
                {
                    continue;
                }

                if (GetZombieModeRewardCategoryCount(existingOptions, category) >= 2)
                {
                    continue;
                }

                totalWeight += Mathf.Max(1, entry.Weight);
            }

            if (totalWeight <= 0)
            {
                return null;
            }

            int roll = Random.Range(0, totalWeight);
            for (int i = 0; i < catalog.Count; i++)
            {
                ZombieModeRewardCatalogEntry entry = catalog[i];
                if (entry == null || entry.Category != category || existingOptions.Contains(entry.RewardType))
                {
                    continue;
                }

                if (GetZombieModeRewardCategoryCount(existingOptions, category) >= 2)
                {
                    continue;
                }

                roll -= Mathf.Max(1, entry.Weight);
                if (roll < 0)
                {
                    return entry;
                }
            }

            return null;
        }

        private void EnsureZombieModeRewardCategoryDiversity(
            ZombieModeRewardNode node,
            List<ZombieModeRewardCatalogEntry> remainingCatalog,
            int minimumCategoryCount)
        {
            if (node == null || node.Options.Count <= 1 || remainingCatalog == null)
            {
                return;
            }

            List<ZombieModeRewardCategory> categories = new List<ZombieModeRewardCategory>();
            for (int i = 0; i < node.Options.Count; i++)
            {
                ZombieModeRewardCategory category = GetZombieModeRewardCategory(node.Options[i]);
                if (!categories.Contains(category))
                {
                    categories.Add(category);
                }
            }

            while (categories.Count < minimumCategoryCount)
            {
                ZombieModeRewardCatalogEntry replacement = null;
                for (int i = 0; i < remainingCatalog.Count; i++)
                {
                    ZombieModeRewardCatalogEntry entry = remainingCatalog[i];
                    if (entry != null && !categories.Contains(entry.Category) && !node.Options.Contains(entry.RewardType))
                    {
                        replacement = entry;
                        break;
                    }
                }

                if (replacement == null)
                {
                    return;
                }

                int replaceIndex = -1;
                for (int i = node.Options.Count - 1; i >= 0; i--)
                {
                    if (GetZombieModeRewardCategoryCount(node.Options, GetZombieModeRewardCategory(node.Options[i])) > 1)
                    {
                        replaceIndex = i;
                        break;
                    }
                }

                if (replaceIndex < 0)
                {
                    return;
                }

                node.Options[replaceIndex] = replacement.RewardType;
                categories.Add(replacement.Category);
                remainingCatalog.Remove(replacement);
            }
        }

        private ZombieModeRewardCategory GetZombieModeRewardCategory(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.AttributeMaxHealth:
                case ZombieModeRewardType.AttributeMoveSpeed:
                case ZombieModeRewardType.AttributeMeleeDamage:
                case ZombieModeRewardType.AttributeRangedDamage:
                case ZombieModeRewardType.AttributeReloadSpeed:
                case ZombieModeRewardType.AttributeDamageReduction:
                    return ZombieModeRewardCategory.Attribute;
                case ZombieModeRewardType.RandomSupply:
                case ZombieModeRewardType.RandomMeleeWeapon:
                case ZombieModeRewardType.RandomGunWithAmmo:
                case ZombieModeRewardType.AmmoSupply:
                case ZombieModeRewardType.MedicalSupply:
                case ZombieModeRewardType.ArmorOrHelmet:
                case ZombieModeRewardType.RandomHighQualityItem:
                case ZombieModeRewardType.StarterReroll:
                    return ZombieModeRewardCategory.Equipment;
                case ZombieModeRewardType.TempMerchant:
                case ZombieModeRewardType.TempNurse:
                case ZombieModeRewardType.TempGoblinNpc:
                case ZombieModeRewardType.TempNurseNpc:
                case ZombieModeRewardType.TempCourierNpc:
                    return ZombieModeRewardCategory.Npc;
                case ZombieModeRewardType.FortificationPack:
                    return ZombieModeRewardCategory.Fortification;
                case ZombieModeRewardType.ContractPollutionDeal:
                case ZombieModeRewardType.ContractGearDeal:
                case ZombieModeRewardType.ContractHugePurification:
                case ZombieModeRewardType.ContractInsurance:
                case ZombieModeRewardType.ContractDevilBargain:
                case ZombieModeRewardType.ContractCursedReload:
                case ZombieModeRewardType.ContractBloodPrice:
                case ZombieModeRewardType.ContractCursePool:
                    return ZombieModeRewardCategory.Contract;
                case ZombieModeRewardType.InsuranceKeepOne:
                case ZombieModeRewardType.InsuranceRandom10:
                case ZombieModeRewardType.InsuranceRandom20:
                case ZombieModeRewardType.InsuranceNearFull:
                    return ZombieModeRewardCategory.Insurance;
                case ZombieModeRewardType.MapEventHighValueAirdrop:
                case ZombieModeRewardType.MapEventEliteSquad:
                    return ZombieModeRewardCategory.MapEvent;
                case ZombieModeRewardType.ProjectilePenetration:
                case ZombieModeRewardType.ProjectileBurn:
                case ZombieModeRewardType.ProjectileCold:
                case ZombieModeRewardType.ProjectilePoison:
                case ZombieModeRewardType.ProjectileArmorBreak:
                case ZombieModeRewardType.ProjectileTrident:
                case ZombieModeRewardType.ProjectileShotgunSpray:
                case ZombieModeRewardType.ProjectileStasis:
                case ZombieModeRewardType.ProjectileRicochet:
                case ZombieModeRewardType.ProjectileFork:
                case ZombieModeRewardType.ProjectileReturn:
                case ZombieModeRewardType.ProjectileHelix:
                case ZombieModeRewardType.ProjectileTrail:
                    return ZombieModeRewardCategory.ProjectileMod;
                case ZombieModeRewardType.TriggerLifesteal:
                case ZombieModeRewardType.TriggerLifestealMedium:
                case ZombieModeRewardType.TriggerLifestealLarge:
                case ZombieModeRewardType.TriggerCritBurst:
                case ZombieModeRewardType.TriggerPurificationSiphon:
                case ZombieModeRewardType.TriggerSecondWind:
                case ZombieModeRewardType.TriggerDoomPulse:
                    return ZombieModeRewardCategory.Trigger;
                case ZombieModeRewardType.MutatorCritFocus:
                case ZombieModeRewardType.MutatorBulletTime:
                case ZombieModeRewardType.MutatorGuardianShield:
                case ZombieModeRewardType.MutatorQuickReload:
                case ZombieModeRewardType.MutatorDashBoost:
                    return ZombieModeRewardCategory.Mutator;
                case ZombieModeRewardType.BattlefieldAmmoRain:
                case ZombieModeRewardType.BattlefieldPurgeAura:
                case ZombieModeRewardType.BattlefieldCurseTrap:
                case ZombieModeRewardType.BattlefieldBlackHole:
                case ZombieModeRewardType.BattlefieldGravityDrag:
                    return ZombieModeRewardCategory.Battlefield;
                default:
                    return ZombieModeRewardCategory.Economy;
            }
        }

        private int GetZombieModeRewardCategoryCount(IList<ZombieModeRewardType> options, ZombieModeRewardCategory category)
        {
            if (options == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < options.Count; i++)
            {
                if (GetZombieModeRewardCategory(options[i]) == category)
                {
                    count++;
                }
            }
            return count;
        }

        public IList<ZombieModeRewardType> GetZombieModeRewardOptions(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.CurrentRewardNode == null)
            {
                return new List<ZombieModeRewardType>();
            }

            return zombieModeRunState.CurrentRewardNode.Options;
        }

        public int GetZombieModeRewardFreeRefreshes(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.CurrentRewardNode == null)
            {
                return 0;
            }

            return zombieModeRunState.FreeRefreshesRemainingCurrentNode;
        }

        public int GetZombieModePurificationPoints(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return 0;
            }

            return zombieModeRunState.PurificationPoints;
        }

        public int GetZombieModeRewardPaidRefreshCost(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.CurrentRewardNode == null)
            {
                return 0;
            }

            int index = Mathf.Clamp(
                zombieModeRunState.PaidRefreshIndexCurrentNode,
                0,
                ZombieModeTuning.PaidRefreshCosts.Length - 1);
            int cost = ZombieModeTuning.PaidRefreshCosts[index];
            if (zombieModeRunState.HalfPriceNextPaidRefresh)
            {
                cost = Mathf.Max(1, Mathf.FloorToInt(cost * 0.5f));
            }
            return cost;
        }

        public bool IsZombieModeBossRewardNode(int runId)
        {
            return IsZombieModeRunValid(runId) &&
                   zombieModeRunState.CurrentRewardNode != null &&
                   zombieModeRunState.CurrentRewardNode.BossNode;
        }

        public string GetZombieModeRewardTitle(int runId)
        {
            return IsZombieModeBossRewardNode(runId)
                ? string.Format(L10n.T("BossRush_ZombieMode_Reward_Title_Boss"), zombieModeRunState.CurrentWave)
                : string.Format(L10n.T("BossRush_ZombieMode_Reward_Title_Normal"), zombieModeRunState.CurrentWave);
        }

        public string GetZombieModeRewardDisplayText(int runId, ZombieModeRewardType rewardType)
        {
            bool bossNode = IsZombieModeBossRewardNode(runId);
            switch (rewardType)
            {
                case ZombieModeRewardType.PurificationPoints:
                    return string.Format(
                        L10n.T("BossRush_ZombieMode_Reward_PurificationPoints"),
                        CalculateZombieModePurificationRewardPoints(bossNode));
                case ZombieModeRewardType.Heal:
                    return L10n.T("BossRush_ZombieMode_Reward_Heal");
                case ZombieModeRewardType.RandomSupply:
                    return L10n.T("BossRush_ZombieMode_Reward_RandomSupply");
                case ZombieModeRewardType.RandomMeleeWeapon:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Equipment", "BossRush_ZombieMode_Reward_RandomMeleeWeapon");
                case ZombieModeRewardType.RandomGunWithAmmo:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Equipment", "BossRush_ZombieMode_Reward_RandomGunWithAmmo");
                case ZombieModeRewardType.AmmoSupply:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Equipment", "BossRush_ZombieMode_Reward_AmmoSupply");
                case ZombieModeRewardType.MedicalSupply:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Equipment", "BossRush_ZombieMode_Reward_MedicalSupply");
                case ZombieModeRewardType.ArmorOrHelmet:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Equipment", "BossRush_ZombieMode_Reward_ArmorOrHelmet");
                case ZombieModeRewardType.RandomHighQualityItem:
                    return L10n.T("BossRush_ZombieMode_Reward_RandomHighQualityItem");
                case ZombieModeRewardType.StarterReroll:
                    return L10n.T("BossRush_ZombieMode_Reward_StarterReroll");
                case ZombieModeRewardType.CurrentNodeFreeRefresh:
                    return L10n.T("BossRush_ZombieMode_Reward_CurrentNodeFreeRefresh");
                case ZombieModeRewardType.NextNodeFreeRefresh:
                    return L10n.T("BossRush_ZombieMode_Reward_NextNodeFreeRefresh");
                case ZombieModeRewardType.HalfPricePaidRefresh:
                    return L10n.T("BossRush_ZombieMode_Reward_HalfPricePaidRefresh");
                case ZombieModeRewardType.AttributeMaxHealth:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Attribute", "BossRush_ZombieMode_Reward_Attribute_MaxHealth");
                case ZombieModeRewardType.AttributeMoveSpeed:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Attribute", "BossRush_ZombieMode_Reward_Attribute_MoveSpeed");
                case ZombieModeRewardType.AttributeMeleeDamage:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Attribute", "BossRush_ZombieMode_Reward_Attribute_MeleeDamage");
                case ZombieModeRewardType.AttributeRangedDamage:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Attribute", "BossRush_ZombieMode_Reward_Attribute_RangedDamage");
                case ZombieModeRewardType.AttributeReloadSpeed:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Attribute", "BossRush_ZombieMode_Reward_Attribute_ReloadSpeed");
                case ZombieModeRewardType.AttributeDamageReduction:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Attribute", "BossRush_ZombieMode_Reward_Attribute_DamageReduction");
                case ZombieModeRewardType.TempMerchant:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Npc", "BossRush_ZombieMode_Reward_TempMerchant");
                case ZombieModeRewardType.TempNurse:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Npc", "BossRush_ZombieMode_Reward_TempNurse");
                case ZombieModeRewardType.TempGoblinNpc:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Npc", "BossRush_ZombieMode_Reward_TempGoblinNpc");
                case ZombieModeRewardType.TempNurseNpc:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Npc", "BossRush_ZombieMode_Reward_TempNurseNpc");
                case ZombieModeRewardType.TempCourierNpc:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Npc", "BossRush_ZombieMode_Reward_TempCourierNpc");
                case ZombieModeRewardType.FortificationPack:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Fortification", "BossRush_ZombieMode_Reward_FortificationPack");
                case ZombieModeRewardType.ContractPollutionDeal:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Curse", "BossRush_ZombieMode_Reward_ContractPollutionDeal");
                case ZombieModeRewardType.ContractGearDeal:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Curse", "BossRush_ZombieMode_Reward_ContractGearDeal");
                case ZombieModeRewardType.ContractHugePurification:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Curse", "BossRush_ZombieMode_Reward_ContractHugePurification");
                case ZombieModeRewardType.ContractInsurance:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Curse", "BossRush_ZombieMode_Reward_ContractInsurance");
                case ZombieModeRewardType.ContractDevilBargain:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Curse", "BossRush_ZombieMode_Reward_ContractDevilBargain");
                case ZombieModeRewardType.ContractCursedReload:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Curse", "BossRush_ZombieMode_Reward_ContractCursedReload");
                case ZombieModeRewardType.ContractBloodPrice:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Curse", "BossRush_ZombieMode_Reward_ContractBloodPrice");
                case ZombieModeRewardType.ContractCursePool:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Curse", "BossRush_ZombieMode_Reward_ContractCursePool");
                case ZombieModeRewardType.InsuranceKeepOne:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Insurance", "BossRush_ZombieMode_Reward_InsuranceKeepOne");
                case ZombieModeRewardType.InsuranceRandom10:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Insurance", "BossRush_ZombieMode_Reward_InsuranceRandom10");
                case ZombieModeRewardType.InsuranceRandom20:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Insurance", "BossRush_ZombieMode_Reward_InsuranceRandom20");
                case ZombieModeRewardType.InsuranceNearFull:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Insurance", "BossRush_ZombieMode_Reward_InsuranceNearFull");
                case ZombieModeRewardType.MapEventHighValueAirdrop:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_MapEvent", "BossRush_ZombieMode_Reward_MapEventHighValueAirdrop");
                case ZombieModeRewardType.MapEventEliteSquad:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_MapEvent", "BossRush_ZombieMode_Reward_MapEventEliteSquad");
                case ZombieModeRewardType.ProjectilePenetration:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectilePenetration");
                case ZombieModeRewardType.ProjectileBurn:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileBurn");
                case ZombieModeRewardType.ProjectileCold:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileCold");
                case ZombieModeRewardType.ProjectilePoison:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectilePoison");
                case ZombieModeRewardType.ProjectileArmorBreak:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileArmorBreak");
                case ZombieModeRewardType.ProjectileTrident:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileTrident");
                case ZombieModeRewardType.ProjectileShotgunSpray:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileShotgunSpray");
                case ZombieModeRewardType.ProjectileStasis:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileStasis");
                case ZombieModeRewardType.ProjectileRicochet:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileRicochet");
                case ZombieModeRewardType.ProjectileFork:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileFork");
                case ZombieModeRewardType.ProjectileReturn:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileReturn");
                case ZombieModeRewardType.ProjectileHelix:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileHelix");
                case ZombieModeRewardType.ProjectileTrail:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_ProjectileMod", "BossRush_ZombieMode_Reward_ProjectileTrail");
                case ZombieModeRewardType.TriggerLifesteal:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Trigger", "BossRush_ZombieMode_Reward_TriggerLifesteal");
                case ZombieModeRewardType.TriggerLifestealMedium:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Trigger", "BossRush_ZombieMode_Reward_TriggerLifestealMedium");
                case ZombieModeRewardType.TriggerLifestealLarge:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Trigger", "BossRush_ZombieMode_Reward_TriggerLifestealLarge");
                case ZombieModeRewardType.TriggerCritBurst:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Trigger", "BossRush_ZombieMode_Reward_TriggerCritBurst");
                case ZombieModeRewardType.TriggerPurificationSiphon:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Trigger", "BossRush_ZombieMode_Reward_TriggerPurificationSiphon");
                case ZombieModeRewardType.TriggerSecondWind:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Trigger", "BossRush_ZombieMode_Reward_TriggerSecondWind");
                case ZombieModeRewardType.TriggerDoomPulse:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Trigger", "BossRush_ZombieMode_Reward_TriggerDoomPulse");
                case ZombieModeRewardType.MutatorCritFocus:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Mutator", "BossRush_ZombieMode_Reward_MutatorCritFocus");
                case ZombieModeRewardType.MutatorBulletTime:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Mutator", "BossRush_ZombieMode_Reward_MutatorBulletTime");
                case ZombieModeRewardType.MutatorGuardianShield:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Mutator", "BossRush_ZombieMode_Reward_MutatorGuardianShield");
                case ZombieModeRewardType.MutatorQuickReload:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Mutator", "BossRush_ZombieMode_Reward_MutatorQuickReload");
                case ZombieModeRewardType.MutatorDashBoost:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Mutator", "BossRush_ZombieMode_Reward_MutatorDashBoost");
                case ZombieModeRewardType.BattlefieldAmmoRain:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Battlefield", "BossRush_ZombieMode_Reward_BattlefieldAmmoRain");
                case ZombieModeRewardType.BattlefieldPurgeAura:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Battlefield", "BossRush_ZombieMode_Reward_BattlefieldPurgeAura");
                case ZombieModeRewardType.BattlefieldCurseTrap:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Battlefield", "BossRush_ZombieMode_Reward_BattlefieldCurseTrap");
                case ZombieModeRewardType.BattlefieldBlackHole:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Battlefield", "BossRush_ZombieMode_Reward_BattlefieldBlackHole");
                case ZombieModeRewardType.BattlefieldGravityDrag:
                    return FormatZombieModeRewardDisplay("BossRush_ZombieMode_RewardCat_Battlefield", "BossRush_ZombieMode_Reward_BattlefieldGravityDrag");
                default:
                    return rewardType.ToString();
            }
        }

        private string FormatZombieModeRewardDisplay(string categoryKey, string rewardKey)
        {
            return "[" + L10n.T(categoryKey) + "] " + L10n.T(rewardKey);
        }

        public void SelectZombieModeReward(int runId, ZombieModeRewardType rewardType)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.RewardSelection)
            {
                return;
            }

            if (IsZombieModeRewardUnaffordable(rewardType))
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_RefreshNoPoints"));
                if (zombieModeRunState.CurrentRewardNode != null)
                {
                    bool currentBossNode = zombieModeRunState.CurrentRewardNode.BossNode;
                    RollZombieModeRewardOptions();
                    ShowZombieModeRewardSelection(runId, currentBossNode);
                }

                return;
            }

            bool extractionOpportunity = zombieModeRunState.CurrentRewardNode != null && zombieModeRunState.CurrentRewardNode.BossNode;
            string pendingTemporaryNpcServiceType = GetZombieModePendingTemporaryNpcServiceType(rewardType);
            if (!ApplyZombieModeReward(rewardType))
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_RewardDeliveryFailed"));
                return;
            }

            zombieModeRunState.CurrentRewardNode = null;
            zombieModeRunState.FreeRefreshesRemainingCurrentNode = 0;
            zombieModeRunState.PaidRefreshIndexCurrentNode = 0;
            ClearZombieModeRewardShell();
            if (extractionOpportunity)
            {
                BeginZombieModeExtractionOpportunity(runId);
            }
            else
            {
                BeginZombieModePreparation(runId, false, false);
            }

            if (!string.IsNullOrEmpty(pendingTemporaryNpcServiceType) &&
                !string.Equals(pendingTemporaryNpcServiceType, "Merchant", System.StringComparison.Ordinal))
            {
                SpawnZombieModeTemporaryNpc(runId, pendingTemporaryNpcServiceType, extractionOpportunity);
            }

            if (rewardType == ZombieModeRewardType.TempGoblinNpc)
            {
                SpawnZombieModeTemporaryRealNpc(runId, "Goblin");
            }
            else if (rewardType == ZombieModeRewardType.TempNurseNpc)
            {
                SpawnZombieModeTemporaryRealNpc(runId, "NurseNpc");
            }
            else if (rewardType == ZombieModeRewardType.TempCourierNpc)
            {
                SpawnZombieModeTemporaryRealNpc(runId, "Courier");
            }
        }

        public void RefreshZombieModeRewardSelection(int runId, bool paid)
        {
            if (!IsZombieModeRunValid(runId) ||
                zombieModeRunState.CombatPhase != ZombieModeCombatPhase.RewardSelection ||
                zombieModeRunState.CurrentRewardNode == null)
            {
                return;
            }

            if (paid)
            {
                int cost = GetZombieModeRewardPaidRefreshCost(runId);
                if (!SpendZombieModePurificationPoints(cost, "RefreshRewardSelection"))
                {
                    return;
                }

                zombieModeRunState.PaidRefreshIndexCurrentNode++;
                zombieModeRunState.HalfPriceNextPaidRefresh = false;
            }
            else
            {
                if (zombieModeRunState.FreeRefreshesRemainingCurrentNode <= 0)
                {
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_RefreshNoPoints"));
                    return;
                }

                zombieModeRunState.FreeRefreshesRemainingCurrentNode--;
            }

            RollZombieModeRewardOptions();
            ShowZombieModeRewardSelection(runId, zombieModeRunState.CurrentRewardNode.BossNode);
        }

        public bool SpendZombieModePurificationPoints(int cost, string reason)
        {
            cost = Mathf.Max(0, cost);
            if (cost <= 0)
            {
                return true;
            }

            if (zombieModeRunState.PurificationPoints < cost)
            {
                string notificationKey = string.Equals(reason, "RefreshRewardSelection", System.StringComparison.Ordinal)
                    ? "BossRush_ZombieMode_Notify_RefreshNoPoints"
                    : "BossRush_ZombieMode_Notify_NpcServiceNoPoints";
                NotificationText.Push(L10n.T(notificationKey));
                return false;
            }

            zombieModeRunState.PurificationPoints -= cost;
            return true;
        }

        private void RefundZombieModePurificationPoints(int amount, string reason)
        {
            amount = Mathf.Max(0, amount);
            if (amount <= 0)
            {
                return;
            }

            zombieModeRunState.PurificationPoints += amount;
            DevLog("[ZombieMode] Refund purification points: +" + amount + ", reason=" + reason);
        }

        private bool ApplyZombieModeReward(ZombieModeRewardType rewardType)
        {
            bool bossNode = zombieModeRunState.CurrentRewardNode != null && zombieModeRunState.CurrentRewardNode.BossNode;
            switch (rewardType)
            {
                case ZombieModeRewardType.PurificationPoints:
                {
                    int points = CalculateZombieModePurificationRewardPoints(bossNode);
                    zombieModeRunState.PurificationPoints += points;
                    NotificationText.Push(string.Format(L10n.T("BossRush_ZombieMode_Notify_RewardGranted"), points));
                    return true;
                }

                case ZombieModeRewardType.Heal:
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null && player.Health != null)
                    {
                        player.Health.SetHealth(player.Health.MaxHealth);
                    }
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_Heal"));
                    return true;
                }

                case ZombieModeRewardType.AttributeMaxHealth:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeMaxHealthKey, 0.10f, 1.00f);
                    return true;
                case ZombieModeRewardType.AttributeMoveSpeed:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeMoveSpeedKey, 0.05f, 0.30f);
                    return true;
                case ZombieModeRewardType.AttributeMeleeDamage:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeMeleeDamageKey, 0.12f, 1.20f);
                    return true;
                case ZombieModeRewardType.AttributeRangedDamage:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeRangedDamageKey, 0.10f, 1.00f);
                    return true;
                case ZombieModeRewardType.AttributeReloadSpeed:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeReloadSpeedKey, 0.10f, 0.80f);
                    return true;
                case ZombieModeRewardType.AttributeDamageReduction:
                    ApplyZombieModeAttributeReward(ZombieModeAttributeDamageReductionKey, -0.05f, -0.40f);
                    return true;

                case ZombieModeRewardType.TempMerchant:
                    GrantZombieModeMerchantPurchaseGuarantee();
                    return true;
                case ZombieModeRewardType.TempNurse:
                case ZombieModeRewardType.TempGoblinNpc:
                case ZombieModeRewardType.TempNurseNpc:
                case ZombieModeRewardType.TempCourierNpc:
                    return true;

                case ZombieModeRewardType.RandomMeleeWeapon:
                    return GrantZombieModeRandomMeleeReward(bossNode);
                case ZombieModeRewardType.RandomGunWithAmmo:
                    return GrantZombieModeRandomGunWithAmmoReward(bossNode);
                case ZombieModeRewardType.AmmoSupply:
                    return GrantZombieModeAmmoSupplyReward();
                case ZombieModeRewardType.MedicalSupply:
                    return GrantZombieModeMedicalSupplyReward();
                case ZombieModeRewardType.ArmorOrHelmet:
                    return GrantZombieModeArmorOrHelmetReward(bossNode);
                case ZombieModeRewardType.FortificationPack:
                    return GrantZombieModeFortificationPack(bossNode);

                case ZombieModeRewardType.ContractPollutionDeal:
                    return ApplyZombieModeContractPollutionDeal(bossNode);
                case ZombieModeRewardType.ContractGearDeal:
                    return ApplyZombieModeContractGearDeal(bossNode);
                case ZombieModeRewardType.ContractHugePurification:
                    return ApplyZombieModeContractHugePurification();
                case ZombieModeRewardType.ContractInsurance:
                    return ApplyZombieModeContractInsurance();
                case ZombieModeRewardType.ContractDevilBargain:
                case ZombieModeRewardType.ContractCursedReload:
                case ZombieModeRewardType.ContractBloodPrice:
                case ZombieModeRewardType.ContractCursePool:
                    return ApplyZombieModePhase2ContractReward(rewardType, bossNode);

                case ZombieModeRewardType.InsuranceKeepOne:
                    ApplyZombieModeInsuranceReward(0.10f, true);
                    return true;
                case ZombieModeRewardType.InsuranceRandom10:
                    ApplyZombieModeInsuranceReward(0.10f, false);
                    return true;
                case ZombieModeRewardType.InsuranceRandom20:
                    ApplyZombieModeInsuranceReward(0.20f, false);
                    return true;
                case ZombieModeRewardType.InsuranceNearFull:
                    zombieModeRunState.PollutionFromContracts += 5;
                    ApplyZombieModeInsuranceReward(0.80f, false);
                    return true;

                case ZombieModeRewardType.MapEventHighValueAirdrop:
                case ZombieModeRewardType.MapEventEliteSquad:
                    ApplyZombieModeMapEventReward(rewardType);
                    return true;

                case ZombieModeRewardType.ProjectilePenetration:
                case ZombieModeRewardType.ProjectileBurn:
                case ZombieModeRewardType.ProjectileCold:
                case ZombieModeRewardType.ProjectilePoison:
                case ZombieModeRewardType.ProjectileArmorBreak:
                case ZombieModeRewardType.ProjectileTrident:
                case ZombieModeRewardType.ProjectileShotgunSpray:
                case ZombieModeRewardType.ProjectileStasis:
                case ZombieModeRewardType.ProjectileRicochet:
                case ZombieModeRewardType.ProjectileFork:
                case ZombieModeRewardType.ProjectileReturn:
                case ZombieModeRewardType.ProjectileHelix:
                case ZombieModeRewardType.ProjectileTrail:
                case ZombieModeRewardType.TriggerLifesteal:
                case ZombieModeRewardType.TriggerLifestealMedium:
                case ZombieModeRewardType.TriggerLifestealLarge:
                case ZombieModeRewardType.TriggerCritBurst:
                case ZombieModeRewardType.TriggerPurificationSiphon:
                case ZombieModeRewardType.TriggerSecondWind:
                case ZombieModeRewardType.TriggerDoomPulse:
                case ZombieModeRewardType.MutatorCritFocus:
                case ZombieModeRewardType.MutatorBulletTime:
                case ZombieModeRewardType.MutatorGuardianShield:
                case ZombieModeRewardType.MutatorQuickReload:
                case ZombieModeRewardType.MutatorDashBoost:
                case ZombieModeRewardType.BattlefieldAmmoRain:
                case ZombieModeRewardType.BattlefieldPurgeAura:
                case ZombieModeRewardType.BattlefieldCurseTrap:
                case ZombieModeRewardType.BattlefieldBlackHole:
                case ZombieModeRewardType.BattlefieldGravityDrag:
                    return ApplyZombieModeOptionReward(rewardType);

                case ZombieModeRewardType.NextNodeFreeRefresh:
                    zombieModeRunState.PendingFreeRefreshNextNode = Mathf.Clamp(
                        zombieModeRunState.PendingFreeRefreshNextNode + 1,
                        0,
                        ZombieModeTuning.FreeRefreshCapPerNode);
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_NextNodeFreeRefresh"));
                    return true;
                case ZombieModeRewardType.HalfPricePaidRefresh:
                    zombieModeRunState.HalfPriceNextPaidRefresh = true;
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_HalfPricePaidRefresh"));
                    return true;
                case ZombieModeRewardType.CurrentNodeFreeRefresh:
                {
                    if (zombieModeRunState.FreeRefreshesRemainingCurrentNode >= ZombieModeTuning.FreeRefreshCapPerNode)
                    {
                        zombieModeRunState.PurificationPoints += 30;
                        NotificationText.Push(string.Format(L10n.T("BossRush_ZombieMode_Notify_RewardGranted"), 30));
                    }
                    else
                    {
                        zombieModeRunState.FreeRefreshesRemainingCurrentNode = Mathf.Clamp(
                            zombieModeRunState.FreeRefreshesRemainingCurrentNode + 1,
                            0,
                            ZombieModeTuning.FreeRefreshCapPerNode);
                        NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_CurrentNodeFreeRefresh"));
                    }
                    return true;
                }

                case ZombieModeRewardType.RandomHighQualityItem:
                {
                    int typeId = FindRandomItemTypeByTags(null, 4, ZombieModeTuning.StarterMaxQuality + 1);
                    if (TryGiveZombieModeItemToPlayerOrDrop(typeId))
                    {
                        NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomHighQualityItem"));
                        return true;
                    }
                    GrantZombieModeFallbackPurificationReward("RandomHighQualityItem", 120);
                    return true;
                }

                case ZombieModeRewardType.StarterReroll:
                {
                    bool granted = false;
                    if (zombieModeRunState.StarterLoadout == ZombieModeStarterLoadout.Gunner)
                    {
                        granted = TryGiveRandomItemByTags(new string[] { "Gun" }, 2, ZombieModeTuning.StarterMaxQuality);
                    }
                    else if (zombieModeRunState.StarterLoadout == ZombieModeStarterLoadout.Melee)
                    {
                        granted = TryGiveRandomItemByTags(new string[] { "MeleeWeapon" }, 2, ZombieModeTuning.StarterMaxQuality);
                    }

                    granted |= TryGiveRandomItemByTags(new string[] { "Weapon" }, 2, ZombieModeTuning.StarterMaxQuality);
                    if (granted)
                    {
                        NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_StarterReroll"));
                        return true;
                    }
                    GrantZombieModeFallbackPurificationReward("StarterReroll", 80);
                    return true;
                }

                case ZombieModeRewardType.RandomSupply:
                default:
                    bool supplyGranted = false;
                    supplyGranted |= TryGiveRandomItemByTags(new string[] { "Medic" }, 1, 4);
                    supplyGranted |= TryGiveRandomItemByTags(new string[] { "Medical" }, 1, 4);
                    supplyGranted |= TryGiveRandomItemByTags(new string[] { "Healing" }, 1, 4);
                    supplyGranted |= TryGiveRandomItemByTags(new string[] { "Ammo" }, 1, 4);
                    supplyGranted |= TryGiveRandomItemByTags(new string[] { "Bullet" }, 1, 4);
                    if (supplyGranted)
                    {
                        NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomSupply"));
                    }
                    else
                    {
                        GrantZombieModeFallbackPurificationReward("RandomSupply", 60);
                    }
                    return true;
            }
        }

        private string GetZombieModePendingTemporaryNpcServiceType(ZombieModeRewardType rewardType)
        {
            if (rewardType == ZombieModeRewardType.TempMerchant)
            {
                return "Merchant";
            }

            if (rewardType == ZombieModeRewardType.TempNurse)
            {
                return "Nurse";
            }

            return string.Empty;
        }

        private void SpawnZombieModeTemporaryRealNpc(int runId, string npcType)
        {
            if (!IsZombieModeRunValid(runId) || string.IsNullOrEmpty(npcType))
            {
                return;
            }

            if (FindZombieModeTemporaryRealNpc(npcType) != null)
            {
                return;
            }

            GameObject npc = CreateZombieModeTemporaryRealNpc(npcType);
            if (npc == null)
            {
                GrantZombieModeFallbackPurificationReward("TempRealNpcSpawnFail_" + npcType, 120);
                return;
            }

            AttachZombieModeTemporaryRealNpcMarker(npc, runId, npcType);
            ApplyZombieModeTemporaryNpcProtection(npc, runId, npcType);

            ZombieModeTemporaryRealNpcRecord record = new ZombieModeTemporaryRealNpcRecord();
            record.GameObject = npc;
            record.NpcType = npcType;
            record.SpawnWave = zombieModeRunState.CurrentWave;
            record.SafeZoneBound = zombieModeRunState.ActiveSafeZoneActive;
            zombieModeRunState.TemporaryRealNpcs.Add(record);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.TemporaryNpc, npc, npc, () => CloseZombieModeTemporaryRealNpcServices(npc));

            string key = "BossRush_ZombieMode_Npc_TempGoblinNpc";
            if (string.Equals(npcType, "NurseNpc", System.StringComparison.Ordinal))
            {
                key = "BossRush_ZombieMode_Npc_TempNurseNpcReal";
            }
            else if (string.Equals(npcType, "Courier", System.StringComparison.Ordinal))
            {
                key = "BossRush_ZombieMode_Npc_TempCourierNpc";
            }

            NotificationText.Push(L10n.T(key));
        }

        private GameObject CreateZombieModeTemporaryRealNpc(string npcType)
        {
            Vector3 spawnPos = GetZombieModeTemporaryRealNpcAnchorPosition();

            if (string.Equals(npcType, "Goblin", System.StringComparison.Ordinal))
            {
                return CreateZombieModeTemporaryGoblinNpc(spawnPos);
            }

            if (string.Equals(npcType, "NurseNpc", System.StringComparison.Ordinal))
            {
                return CreateZombieModeTemporaryNurseNpc(spawnPos);
            }

            if (string.Equals(npcType, "Courier", System.StringComparison.Ordinal))
            {
                return CreateZombieModeTemporaryCourierNpc(spawnPos);
            }

            return null;
        }

        private Vector3 GetZombieModeTemporaryRealNpcAnchorPosition()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 center = zombieModeRunState.ActiveSafeZoneActive
                ? zombieModeRunState.ActiveSafeZoneCenter
                : (player != null ? player.transform.position + player.transform.forward * 3f : Vector3.zero);
            int existingCount = zombieModeRunState.TemporaryNpcs.Count + zombieModeRunState.TemporaryRealNpcs.Count;
            float angle = existingCount * 72f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2.8f;
            Vector3 spawnPos = center + offset + Vector3.up * 0.05f;

            Vector3 resolved;
            if (SpawnPositionHelper.TrySampleNavMesh(
                    spawnPos,
                    out resolved,
                    ZombieModeTuning.NavMeshLiftOffset,
                    ZombieModeTuning.NavMeshSafeZoneRadius))
            {
                spawnPos = resolved;
            }
            else
            {
                RaycastHit hit;
                if (Physics.Raycast(spawnPos + Vector3.up * 1f, Vector3.down, out hit, 8f))
                {
                    spawnPos = hit.point + new Vector3(0f, 0.1f, 0f);
                }
            }

            return spawnPos;
        }

        private GameObject CreateZombieModeTemporaryGoblinNpc(Vector3 spawnPos)
        {
            if (!LoadGoblinAssetBundle() || goblinPrefab == null)
            {
                return null;
            }

            GameObject npc = UnityEngine.Object.Instantiate(goblinPrefab, spawnPos, Quaternion.identity);
            npc.name = "ZombieMode_TemporaryRealNpc_Goblin";
            npc.SetActive(true);
            foreach (Transform child in npc.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.SetActive(true);
            }

            NPCCommonUtils.FixShaders(npc, "[ZombieModeTempGoblin]");
            NPCCommonUtils.SetLayerRecursively(npc, LayerMask.NameToLayer("Default"));

            GoblinNPCController controller = npc.GetComponent<GoblinNPCController>();
            if (controller == null)
            {
                controller = npc.AddComponent<GoblinNPCController>();
            }

            GoblinMovement movement = npc.GetComponent<GoblinMovement>();
            if (movement == null)
            {
                movement = npc.AddComponent<GoblinMovement>();
            }

            movement.StopMove();
            movement.enabled = false;
            controller.EnterStationaryIdleState();

            GoblinInteractable interactable = npc.GetComponent<GoblinInteractable>();
            if (interactable == null)
            {
                interactable = npc.AddComponent<GoblinInteractable>();
            }

            return npc;
        }

        private GameObject CreateZombieModeTemporaryNurseNpc(Vector3 spawnPos)
        {
            if (!LoadNurseAssetBundle() || nursePrefab == null)
            {
                return null;
            }

            GameObject npc = UnityEngine.Object.Instantiate(nursePrefab, spawnPos, Quaternion.identity);
            npc.name = "ZombieMode_TemporaryRealNpc_Nurse";
            npc.SetActive(true);
            foreach (Transform child in npc.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.SetActive(true);
            }

            NPCCommonUtils.FixShaders(npc, "[ZombieModeTempNurse]");
            NPCCommonUtils.SetLayerRecursively(npc, LayerMask.NameToLayer("Default"));

            NurseNPCController controller = npc.GetComponent<NurseNPCController>();
            if (controller == null)
            {
                controller = npc.AddComponent<NurseNPCController>();
            }

            NurseMovement movement = npc.GetComponent<NurseMovement>();
            if (movement == null)
            {
                movement = npc.AddComponent<NurseMovement>();
            }

            movement.StopMove();
            movement.enabled = false;

            NurseInteractable interactable = npc.GetComponent<NurseInteractable>();
            if (interactable == null)
            {
                interactable = npc.AddComponent<NurseInteractable>();
            }

            return npc;
        }

        private GameObject CreateZombieModeTemporaryCourierNpc(Vector3 spawnPos)
        {
            if (!LoadCourierAssetBundle() || courierPrefab == null)
            {
                return null;
            }

            GameObject npc = UnityEngine.Object.Instantiate(courierPrefab, spawnPos, Quaternion.identity);
            npc.name = "ZombieMode_TemporaryRealNpc_Courier";
            npc.SetActive(true);
            foreach (Transform child in npc.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.SetActive(true);
            }

            NPCCommonUtils.FixShaders(npc, "[ZombieModeTempCourier]");
            NPCCommonUtils.SetLayerRecursively(npc, LayerMask.NameToLayer("Default"));

            CourierNPCController controller = npc.GetComponent<CourierNPCController>();
            if (controller == null)
            {
                controller = npc.AddComponent<CourierNPCController>();
            }

            CourierMovement movement = npc.GetComponent<CourierMovement>();
            if (movement == null)
            {
                movement = npc.AddComponent<CourierMovement>();
            }

            movement.SetStationary(true);
            controller.SetStationary(true);
            controller.StartTalking(false);
            AddCourierInteraction(npc);
            return npc;
        }

        private void AttachZombieModeTemporaryRealNpcMarker(GameObject npc, int runId, string npcType)
        {
            if (npc == null)
            {
                return;
            }

            ZombieModeTemporaryRealNpcMarker marker = npc.GetComponent<ZombieModeTemporaryRealNpcMarker>();
            if (marker == null)
            {
                marker = npc.AddComponent<ZombieModeTemporaryRealNpcMarker>();
            }

            marker.RunId = runId;
            marker.NpcType = npcType ?? string.Empty;
            marker.UsesPurificationPayment = true;
        }

        private ZombieModeTemporaryRealNpcRecord FindZombieModeTemporaryRealNpc(string npcType)
        {
            for (int i = 0; i < zombieModeRunState.TemporaryRealNpcs.Count; i++)
            {
                ZombieModeTemporaryRealNpcRecord npc = zombieModeRunState.TemporaryRealNpcs[i];
                if (npc != null &&
                    npc.GameObject != null &&
                    string.Equals(npc.NpcType, npcType, System.StringComparison.Ordinal))
                {
                    return npc;
                }
            }

            return null;
        }

        public bool IsZombieModeTemporaryRealNpc(Component component)
        {
            if (component == null)
            {
                return false;
            }

            ZombieModeTemporaryRealNpcMarker marker = component.GetComponentInParent<ZombieModeTemporaryRealNpcMarker>();
            return marker != null &&
                   marker.UsesPurificationPayment &&
                   IsZombieModeRunValid(marker.RunId);
        }

        public bool CanAffordZombieModePurificationPointsForRealNpc(Component component, int cost)
        {
            if (!IsZombieModeTemporaryRealNpc(component))
            {
                return false;
            }

            return cost <= 0 || zombieModeRunState.PurificationPoints >= cost;
        }

        public bool TrySpendZombieModePurificationPointsForRealNpc(Component component, int cost, string reason)
        {
            if (!IsZombieModeTemporaryRealNpc(component))
            {
                return false;
            }

            return SpendZombieModePurificationPoints(cost, reason);
        }

        public void RefundZombieModePurificationPointsForRealNpc(Component component, int cost, bool shouldRefund)
        {
            if (!shouldRefund || cost <= 0 || !IsZombieModeTemporaryRealNpc(component))
            {
                return;
            }

            zombieModeRunState.PurificationPoints += cost;
        }

        public int GetZombieModePurificationPointsForRealNpcUi(Component component)
        {
            return IsZombieModeTemporaryRealNpc(component)
                ? zombieModeRunState.PurificationPoints
                : 0;
        }

        public string GetZombieModeNpcHealCurrencyLabel(Component component, int cost)
        {
            return IsZombieModeTemporaryRealNpc(component)
                ? L10n.T("治疗（净化点 " + cost + "）", "Heal (Purification " + cost + ")")
                : L10n.T("治疗（￥" + cost + "）", "Heal ($" + cost + ")");
        }

        private void ApplyZombieModeAttributeReward(string key, float increment, float cap)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            float current = 0f;
            zombieModeRunState.AttributeBonuses.TryGetValue(key, out current);
            float next = increment < 0f
                ? Mathf.Max(cap, current + increment)
                : Mathf.Min(cap, current + increment);
            zombieModeRunState.AttributeBonuses[key] = next;
            ApplyZombieModePlayerAttributeModifiers();

            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_AttributeBonus"),
                GetZombieModeAttributeDisplayName(key),
                Mathf.RoundToInt(Mathf.Abs(next) * 100f)));
        }

        private string GetZombieModeAttributeDisplayName(string key)
        {
            if (key == ZombieModeAttributeMaxHealthKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_MaxHealth");
            }
            if (key == ZombieModeAttributeMoveSpeedKey ||
                key == ZombieModeAttributeWalkSpeedKey ||
                key == ZombieModeAttributeRunSpeedKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_MoveSpeed");
            }
            if (key == ZombieModeAttributeMeleeDamageKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_MeleeDamage");
            }
            if (key == ZombieModeAttributeRangedDamageKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_RangedDamage");
            }
            if (key == ZombieModeAttributeReloadSpeedKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_ReloadSpeed");
            }
            if (key == ZombieModeAttributeDamageReductionKey)
            {
                return L10n.T("BossRush_ZombieMode_Reward_AttributeName_DamageReduction");
            }
            return key;
        }

        private void ApplyZombieModePlayerAttributeModifiers()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null)
            {
                return;
            }

            float oldMaxHealth = player.Health != null ? player.Health.MaxHealth : -1f;
            RemoveZombieModeAttributeModifiers();
            foreach (KeyValuePair<string, float> pair in zombieModeRunState.AttributeBonuses)
            {
                if (Mathf.Approximately(pair.Value, 0f))
                {
                    continue;
                }

                if (pair.Key == ZombieModeAttributeMoveSpeedKey)
                {
                    AddZombieModeAttributeModifier(player, ZombieModeAttributeMoveSpeedKey, pair.Value);
                    AddZombieModeAttributeModifier(player, ZombieModeAttributeWalkSpeedKey, pair.Value);
                    AddZombieModeAttributeModifier(player, ZombieModeAttributeRunSpeedKey, pair.Value);
                    continue;
                }

                AddZombieModeAttributeModifier(player, pair.Key, pair.Value);
            }

            if (player.Health != null && oldMaxHealth > 0f)
            {
                float delta = player.Health.MaxHealth - oldMaxHealth;
                if (delta > 0f)
                {
                    player.Health.SetHealth(Mathf.Min(player.Health.MaxHealth, player.Health.CurrentHealth + delta));
                }
            }
        }

        private void AddZombieModeAttributeModifier(CharacterMainControl player, string statName, float percent)
        {
            if (player == null || player.CharacterItem == null || string.IsNullOrEmpty(statName) || Mathf.Approximately(percent, 0f))
            {
                return;
            }

            try
            {
                bool added = RuntimeStatModifierTracker.TryAdd(
                    player,
                    statName,
                    percent,
                    this,
                    zombieModeRunState.AttributeModifierRecords,
                    "ZombieMode Reward Attribute");
                if (!added)
                {
                    return;
                }

                if (!zombieModeRunState.AttributeModifierCleanupRegistered)
                {
                    zombieModeRunState.AttributeModifierCleanupRegistered = true;
                    RegisterZombieModeRunOnlyObject(zombieModeRunState.RunId, ZombieModeRunOnlyObjectKind.Buff, null, player.CharacterItem, RemoveZombieModeAttributeModifiers);
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] [WARNING] Attribute Modifier 注册失败: " + e.Message);
            }
        }

        private void RemoveZombieModeAttributeModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(
                zombieModeRunState.AttributeModifierRecords,
                "ZombieMode Reward Attribute");
            zombieModeRunState.AttributeModifierCleanupRegistered = false;
        }

        private void GrantZombieModeMerchantPurchaseGuarantee()
        {
            zombieModeRunState.GuaranteedMerchantPurchasePending = true;
            zombieModeRunState.GuaranteedMerchantPurchaseMinQuality = 6;
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_TempMerchantGuarantee"));
        }

        private string[] GetZombieModeMerchantGrantTagAliases(string grantTag)
        {
            if (string.IsNullOrEmpty(grantTag))
            {
                return new string[0];
            }

            if (string.Equals(grantTag, "Medical", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Medic", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Healing", System.StringComparison.Ordinal))
            {
                return new string[] { "Medic", "Medical", "Consumable", "Healing", "Injector" };
            }

            if (string.Equals(grantTag, "Armor", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Helmet", System.StringComparison.Ordinal))
            {
                return new string[] { "Armor", "Helmat", "Helmet" };
            }

            if (string.Equals(grantTag, "Mask", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "FaceMask", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Headset", System.StringComparison.Ordinal))
            {
                return new string[] { "Headset", "Mask", "FaceMask" };
            }

            return new string[] { grantTag };
        }

        private string GetZombieModeMerchantModeECategorySuffix(string grantTag)
        {
            if (string.IsNullOrEmpty(grantTag))
            {
                return string.Empty;
            }

            if (string.Equals(grantTag, "Gun", System.StringComparison.Ordinal))
            {
                return "Gun";
            }

            if (string.Equals(grantTag, "MeleeWeapon", System.StringComparison.Ordinal))
            {
                return "Melee";
            }

            if (string.Equals(grantTag, "Ammo", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Bullet", System.StringComparison.Ordinal))
            {
                return "Bullet";
            }

            if (string.Equals(grantTag, "Armor", System.StringComparison.Ordinal))
            {
                return "Armor";
            }

            if (string.Equals(grantTag, "Helmet", System.StringComparison.Ordinal))
            {
                return "Helmat";
            }

            if (string.Equals(grantTag, "Food", System.StringComparison.Ordinal))
            {
                return "Food";
            }

            if (string.Equals(grantTag, "Medic", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Medical", System.StringComparison.Ordinal) ||
                string.Equals(grantTag, "Healing", System.StringComparison.Ordinal))
            {
                return "Medical";
            }

            return string.Empty;
        }

        private void SpawnZombieModeTemporaryNpc(int runId, string serviceType, bool bossNodeStock)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            GameObject npc = CreateZombieModeTemporaryServiceTerminal(runId, serviceType);
            if (npc == null)
            {
                return;
            }

            ZombieModeTemporaryNpcInteractable interactable = npc.GetComponent<ZombieModeTemporaryNpcInteractable>();
            ApplyZombieModeTemporaryNpcProtection(npc, runId, serviceType);

            ZombieModeTemporaryNpc record = CreateZombieModeTemporaryNpcRecord(npc, serviceType, bossNodeStock);
            zombieModeRunState.TemporaryNpcs.Add(record);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.TemporaryNpc, npc, interactable, null);

            string key = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? "BossRush_ZombieMode_Npc_TempNurse"
                : "BossRush_ZombieMode_Npc_TempMerchant";
            NotificationText.Push(L10n.T(key));
        }

        private GameObject CreateZombieModeTemporaryServiceTerminal(int runId, string serviceType)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 center = zombieModeRunState.ActiveSafeZoneActive
                ? zombieModeRunState.ActiveSafeZoneCenter
                : (player != null ? player.transform.position + player.transform.forward * 3f : Vector3.zero);
            int existingCount = zombieModeRunState.TemporaryNpcs.Count;
            float angle = ZombieModeNpcCatalog.NpcAngleArrangement[existingCount % ZombieModeNpcCatalog.NpcAngleArrangement.Length];
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 2.4f;

            GameObject terminal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            terminal.name = "ZombieMode_TemporaryNpc_" + serviceType;
            terminal.transform.position = center + offset + Vector3.up * 0.05f;
            if (player != null)
            {
                Vector3 look = player.transform.position - terminal.transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                {
                    terminal.transform.rotation = Quaternion.LookRotation(look.normalized);
                }
            }

            Renderer renderer = terminal.GetComponent<Renderer>();
            if (renderer != null)
            {
                SetZombieModeRendererColor(
                    renderer,
                    string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                        ? new Color(0.85f, 0.25f, 0.28f, 0.95f)
                        : new Color(0.25f, 0.65f, 0.35f, 0.95f));
            }

            ZombieModeTemporaryNpcInteractable interactable = terminal.GetComponent<ZombieModeTemporaryNpcInteractable>();
            if (interactable == null)
            {
                interactable = terminal.AddComponent<ZombieModeTemporaryNpcInteractable>();
            }
            interactable.Initialize(runId, serviceType);
            return terminal;
        }

        private ZombieModeTemporaryNpc CreateZombieModeTemporaryNpcRecord(GameObject npc, string serviceType, bool bossNodeStock)
        {
            ZombieModeTemporaryNpc record = new ZombieModeTemporaryNpc();
            record.GameObject = npc;
            record.ServiceType = serviceType;
            record.SpawnWave = zombieModeRunState.CurrentWave;
            record.ServiceState = CreateZombieModeNpcServiceState(serviceType, bossNodeStock, zombieModeRunState.ActiveSafeZoneActive);
            return record;
        }

        private void ApplyZombieModeTemporaryNpcProtection(GameObject npc, int runId, string serviceType)
        {
            if (npc == null)
            {
                return;
            }

            ZombieModeTemporaryNpcProtectionMarker marker = npc.GetComponent<ZombieModeTemporaryNpcProtectionMarker>();
            if (marker == null)
            {
                marker = npc.AddComponent<ZombieModeTemporaryNpcProtectionMarker>();
            }

            marker.RunId = runId;
            marker.ServiceType = serviceType ?? string.Empty;
            TrySetZombieModeTemporaryNpcInvincible(npc);
            ClearZombieModeTemporaryNpcThreatTargets();
        }

        private void TrySetZombieModeTemporaryNpcInvincible(GameObject npc)
        {
            if (npc == null)
            {
                return;
            }

            Health[] healths = npc.GetComponentsInChildren<Health>(true);
            for (int i = 0; i < healths.Length; i++)
            {
                Health health = healths[i];
                if (health == null)
                {
                    continue;
                }

                try
                {
                    health.SetInvincible(true);
                    health.SetHealth(health.MaxHealth);
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] Heal 设置无敌+血量失败: " + e.Message);
                }
            }
        }

        private void TickZombieModeTemporaryNpcProtection()
        {
            if (!IsZombieModeActive ||
                (zombieModeRunState.TemporaryNpcs.Count <= 0 && zombieModeRunState.TemporaryRealNpcs.Count <= 0))
            {
                return;
            }

            if (Time.unscaledTime - zombieModeRunState.LastTemporaryNpcProtectionTickTime <
                ZombieModeTuning.TemporaryNpcProtectionTickIntervalSeconds)
            {
                return;
            }
            zombieModeRunState.LastTemporaryNpcProtectionTickTime = Time.unscaledTime;

            for (int i = zombieModeRunState.TemporaryNpcs.Count - 1; i >= 0; i--)
            {
                ZombieModeTemporaryNpc npc = zombieModeRunState.TemporaryNpcs[i];
                if (npc == null || npc.GameObject == null)
                {
                    zombieModeRunState.TemporaryNpcs.RemoveAt(i);
                    continue;
                }

                ApplyZombieModeTemporaryNpcProtection(npc.GameObject, zombieModeRunState.RunId, npc.ServiceType);
            }

            for (int i = zombieModeRunState.TemporaryRealNpcs.Count - 1; i >= 0; i--)
            {
                ZombieModeTemporaryRealNpcRecord npc = zombieModeRunState.TemporaryRealNpcs[i];
                if (npc == null || npc.GameObject == null)
                {
                    zombieModeRunState.TemporaryRealNpcs.RemoveAt(i);
                    continue;
                }

                ApplyZombieModeTemporaryNpcProtection(npc.GameObject, zombieModeRunState.RunId, npc.NpcType);
            }

            ClearZombieModeTemporaryNpcThreatTargets();
        }

        private void ClearZombieModeTemporaryNpcThreatTargets()
        {
            for (int i = 0; i < zombieModeRunState.RunOnlyObjects.Count; i++)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null ||
                    (record.Kind != ZombieModeRunOnlyObjectKind.Enemy && record.Kind != ZombieModeRunOnlyObjectKind.Boss) ||
                    record.GameObject == null)
                {
                    continue;
                }

                AICharacterController ai = record.GameObject.GetComponentInChildren<AICharacterController>();
                if (ai == null || !IsZombieModeTemporaryNpcDamageReceiver(ai.searchedEnemy))
                {
                    continue;
                }

                ai.searchedEnemy = null;
                ai.noticed = false;
                if (ShouldZombieModeEnemyAggroPlayerNow())
                {
                    SetZombieModeEnemyTargetToMainPlayer(ai);
                }
            }
        }

        private bool IsZombieModeTemporaryNpcDamageReceiver(DamageReceiver receiver)
        {
            if (receiver == null)
            {
                return false;
            }

            try
            {
                if (receiver.GetComponentInParent<ZombieModeTemporaryNpcProtectionMarker>() != null)
                {
                    return true;
                }

                if (receiver.health != null)
                {
                    CharacterMainControl character = receiver.health.TryGetCharacter();
                    return character != null &&
                           character.GetComponentInParent<ZombieModeTemporaryNpcProtectionMarker>() != null;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] TemporaryNpcProtection 判定失败: " + e.Message);
            }

            return false;
        }

        private void SetZombieModeEnemyTargetToMainPlayer(AICharacterController ai)
        {
            if (ai == null)
            {
                return;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null || main.mainDamageReceiver == null)
            {
                ai.searchedEnemy = null;
                ai.noticed = false;
                return;
            }

            ai.searchedEnemy = main.mainDamageReceiver;
            ai.SetTarget(main.mainDamageReceiver.transform);
            ai.SetNoticedToTarget(main.mainDamageReceiver);
            ai.noticed = true;
        }

        private ZombieModeNpcServiceState CreateZombieModeNpcServiceState(string serviceType, bool bossNodeStock, bool safeZoneBound)
        {
            ZombieModeNpcServiceState state = new ZombieModeNpcServiceState();
            state.BossNodeStock = bossNodeStock;
            state.SafeZoneBound = safeZoneBound;
            if (string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal))
            {
                ZombieModeNpcCatalog.NurseServiceEntry[] services = ZombieModeNpcCatalog.NurseServices;
                for (int i = 0; i < services.Length; i++)
                {
                    state.NurseUsesRemaining.Add(services[i].Uses);
                }
            }
            else
            {
                ZombieModeNpcCatalog.MerchantStockEntry[] stock = bossNodeStock
                    ? ZombieModeNpcCatalog.BossNodeStock
                    : ZombieModeNpcCatalog.NormalWaveStock;
                for (int i = 0; i < stock.Length; i++)
                {
                    state.MerchantStockRemaining.Add(stock[i].StockCount);
                }
            }

            return state;
        }

        private bool GrantZombieModeRandomMeleeReward(bool bossNode)
        {
            int maxQuality = GetZombieModeRewardMaxQuality(bossNode);
            if (TryGiveRandomItemByTags(new string[] { "MeleeWeapon" }, 1, maxQuality))
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomMeleeWeapon"));
                return true;
            }

            GrantZombieModeFallbackPurificationReward("RandomMeleeRewardFail", bossNode ? 140 : 90);
            return true;
        }

        private bool GrantZombieModeRandomGunWithAmmoReward(bool bossNode)
        {
            int maxQuality = GetZombieModeRewardMaxQuality(bossNode);
            int gunTypeId = FindRandomItemTypeByTags(new string[] { "Gun" }, 1, maxQuality);
            if (gunTypeId <= 0)
            {
                GrantZombieModeFallbackPurificationReward("RandomGunWithAmmoRewardFail_NoType", bossNode ? 160 : 110);
                return true;
            }

            Item gun = null;
            try
            {
                gun = ItemAssetsCollection.InstantiateSync(gunTypeId);
                if (gun == null)
                {
                    GrantZombieModeFallbackPurificationReward("RandomGunWithAmmoRewardFail_Instantiate", bossNode ? 160 : 110);
                    return true;
                }

                string caliber = TryReadZombieModeItemCaliber(gun);
                if (!TryDeliverZombieModeItemToPlayerOrDrop(gun, "RandomGunWithAmmoReward"))
                {
                    GrantZombieModeFallbackPurificationReward("RandomGunWithAmmoRewardFail_Deliver", bossNode ? 160 : 110);
                    return true;
                }

                gun = null;
                if (!string.IsNullOrEmpty(caliber))
                {
                    TryGiveZombieModeStarterAmmo(caliber, 60);
                }
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomGunWithAmmo"));
                return true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] RandomGunWithAmmo reward failed: " + e.Message);
                try
                {
                    if (gun != null)
                    {
                        gun.DestroyTree();
                    }
                }
                catch (System.Exception destroyEx)
                {
                    DevLog("[ZombieMode] RandomGunWithAmmo reward cleanup failed: " + destroyEx.Message);
                }

                GrantZombieModeFallbackPurificationReward("RandomGunWithAmmoRewardFail_Exception", bossNode ? 160 : 110);
                return true;
            }
        }

        private bool GrantZombieModeAmmoSupplyReward()
        {
            string caliber = !string.IsNullOrEmpty(zombieModeRunState.StarterAmmoCaliber)
                ? zombieModeRunState.StarterAmmoCaliber
                : string.Empty;
            if (!string.IsNullOrEmpty(caliber) && TryGiveZombieModeStarterAmmo(caliber, 120))
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_AmmoSupply"));
                return true;
            }

            bool granted = TryGiveRandomItemByTags(new string[] { "Ammo" }, 1, 4);
            granted |= TryGiveRandomItemByTags(new string[] { "Bullet" }, 1, 4);
            if (granted)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_AmmoSupply"));
                return true;
            }

            GrantZombieModeFallbackPurificationReward("AmmoSupplyRewardFail", 70);
            return true;
        }

        private bool GrantZombieModeMedicalSupplyReward()
        {
            int count = UnityEngine.Random.Range(3, 6);
            int granted = TryGiveRandomItemByTagsTimes(new string[] { "Medic", "Medical", "Healing" }, 1, 4, count);
            if (granted > 0)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_MedicalSupply"));
                return true;
            }

            GrantZombieModeFallbackPurificationReward("MedicalSupplyRewardFail", 70);
            return true;
        }

        private bool GrantZombieModeArmorOrHelmetReward(bool bossNode)
        {
            int maxQuality = GetZombieModeRewardMaxQuality(bossNode);
            string tag = UnityEngine.Random.value < 0.5f ? "Armor" : "Helmet";
            bool granted = TryGiveRandomItemByTags(new string[] { tag }, 1, maxQuality);
            if (!granted)
            {
                granted = TryGiveRandomItemByTags(new string[] { "Armor" }, 1, maxQuality) ||
                          TryGiveRandomItemByTags(new string[] { "Helmet" }, 1, maxQuality);
            }

            if (granted)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_ArmorOrHelmet"));
                return true;
            }

            GrantZombieModeFallbackPurificationReward("ArmorOrHelmetRewardFail", bossNode ? 160 : 100);
            return true;
        }

        private int GetZombieModeRewardMaxQuality(bool bossNode)
        {
            return Mathf.Clamp(zombieModeRunState.PollutionTier + (bossNode ? 2 : 1), 1, ZombieModeTuning.StarterMaxQuality + (bossNode ? 1 : 0));
        }

        private bool GrantZombieModeFortificationPack(bool bossNode)
        {
            int granted = 0;
            granted += GrantZombieModeItemRepeated(FoldableCoverPackConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackFoldableCoverBoss : ZombieModeNpcCatalog.RepairPackFoldableCoverNormal);
            granted += GrantZombieModeItemRepeated(ReinforcedRoadblockPackConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackReinforcedRoadblockBoss : ZombieModeNpcCatalog.RepairPackReinforcedRoadblockNormal);
            granted += GrantZombieModeItemRepeated(BarbedWirePackConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackBarbedWireBoss : ZombieModeNpcCatalog.RepairPackBarbedWireNormal);
            granted += GrantZombieModeItemRepeated(EmergencyRepairSprayConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackEmergencyRepairSprayBoss : ZombieModeNpcCatalog.RepairPackEmergencyRepairSprayNormal);

            if (granted > 0)
            {
                ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_RepairPackReceived"));
                return true;
            }

            GrantZombieModeFallbackPurificationReward("FortificationPackRewardFail", bossNode ? 160 : 100);
            return true;
        }

        private int GrantZombieModeItemRepeated(int typeId, int count)
        {
            int granted = 0;
            for (int i = 0; i < Mathf.Max(0, count); i++)
            {
                if (TryGiveZombieModeItemToPlayerOrDrop(typeId))
                {
                    granted++;
                }
            }

            return granted;
        }

        private bool ApplyZombieModeContractPollutionDeal(bool bossNode)
        {
            int pollution = bossNode ? 2 : 1;
            int pointsCost = bossNode ? 150 : 80;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractPollutionDeal"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += pollution;
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractPollutionCost"),
                pollution,
                pointsCost));
            return true;
        }

        private bool ApplyZombieModeContractGearDeal(bool bossNode)
        {
            int pollution = bossNode ? 3 : 2;
            int pointsCost = bossNode ? 120 : 60;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractGearDeal"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += pollution;
            GrantZombieModeContractGearDealRewardOnly();
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractGearCost"),
                pollution,
                pointsCost));
            return true;
        }

        private bool GrantZombieModeContractGearDealRewardOnly()
        {
            bool granted = GrantZombieModeContractGearDealGunWithAmmo();
            granted |= GrantZombieModeContractGearDealArmorOrHelmet();
            if (!granted)
            {
                GrantZombieModeFallbackPurificationReward("ContractGearDeal", 180);
            }

            return granted;
        }

        private bool GrantZombieModeContractGearDealGunWithAmmo()
        {
            int minQuality = ZombieModeContractGearDealMinQuality;
            int maxQuality = GetZombieModeContractGearDealMaxQuality();
            int gunTypeId = PickZombieModeStrictQualityCandidate(
                GetZombieModeRewardCandidateIds(new string[] { "Gun" }, minQuality, maxQuality),
                minQuality,
                maxQuality);
            if (gunTypeId <= 0)
            {
                return false;
            }

            Item gun = ItemAssetsCollection.InstantiateSync(gunTypeId);
            if (gun == null)
            {
                return false;
            }

            string caliber = TryReadZombieModeItemCaliber(gun);
            ItemUtilities.SendToPlayer(gun, false, false);
            if (!string.IsNullOrEmpty(caliber))
            {
                TryGiveZombieModeAmmo(caliber, 60, minQuality, maxQuality);
            }

            NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomGunWithAmmo"));
            return true;
        }

        private bool GrantZombieModeContractGearDealArmorOrHelmet()
        {
            int minQuality = ZombieModeContractGearDealMinQuality;
            int maxQuality = GetZombieModeContractGearDealMaxQuality();
            string tag = UnityEngine.Random.value < 0.5f ? "Armor" : "Helmet";
            bool granted = TryGiveZombieModeContractGearDealItemByTags(new string[] { tag }, minQuality, maxQuality);
            if (!granted)
            {
                granted = TryGiveZombieModeContractGearDealItemByTags(new string[] { "Armor" }, minQuality, maxQuality) ||
                          TryGiveZombieModeContractGearDealItemByTags(new string[] { "Helmet" }, minQuality, maxQuality);
            }

            if (granted)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_ArmorOrHelmet"));
            }

            return granted;
        }

        private bool TryGiveZombieModeContractGearDealItemByTags(string[] tags, int minQuality, int maxQuality)
        {
            int typeId = PickZombieModeStrictQualityCandidate(
                GetZombieModeRewardCandidateIds(tags, minQuality, maxQuality),
                minQuality,
                maxQuality);
            return TryGiveZombieModeItemToPlayerOrDrop(typeId);
        }

        private int GetZombieModeContractGearDealMaxQuality()
        {
            return Mathf.Max(ZombieModeContractGearDealMinQuality, ZombieModeTuning.StarterMaxQuality + 1);
        }

        private bool ApplyZombieModeContractHugePurification()
        {
            int pointsCost = 200;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractHugePurification"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += 3;
            ApplyZombieModeInsuranceReward(0.30f, true);
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractHugePurificationCost"),
                3,
                pointsCost));
            return true;
        }

        private bool ApplyZombieModeContractInsurance()
        {
            int pointsCost = 80;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractInsurance"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += 2;
            ApplyZombieModeInsuranceReward(0.20f, true);
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractInsuranceCost"),
                2,
                pointsCost));
            return true;
        }

        private void ApplyZombieModeInsuranceReward(float randomKeepRatio, bool includeSpecifiedKeep)
        {
            List<Item> candidates = CollectZombieModeInsuranceCandidates();
            if (includeSpecifiedKeep && zombieModeRunState.InsuranceState.SpecifiedKeepItem == null && candidates.Count > 0)
            {
                zombieModeRunState.InsuranceState.SpecifiedKeepItem = candidates[0];
            }

            zombieModeRunState.InsuranceState.RandomKeepRatio = Mathf.Clamp(
                zombieModeRunState.InsuranceState.RandomKeepRatio + Mathf.Max(0f, randomKeepRatio),
                0f,
                0.80f);
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_InsuranceKeepOne"),
                Mathf.RoundToInt(zombieModeRunState.InsuranceState.RandomKeepRatio * 100f)));
        }

        private void ApplyZombieModeMapEventReward(ZombieModeRewardType rewardType)
        {
            if (rewardType == ZombieModeRewardType.MapEventHighValueAirdrop)
            {
                zombieModeRunState.PendingMapEvent = ZombieModePendingMapEventType.HighValueAirdrop;
                CreateZombieModeHighValueAirdrop(zombieModeRunState.RunId);
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_MapEventHighValueAirdrop"));
                return;
            }

            zombieModeRunState.PendingMapEvent = ZombieModePendingMapEventType.EliteSquad;
            zombieModeRunState.PendingEliteSquadCount += 3;
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_MapEventEliteSquad"));
        }

        private void SpawnPendingZombieModeEliteSquad(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.PendingEliteSquadCount <= 0)
            {
                return;
            }

            int count = zombieModeRunState.PendingEliteSquadCount;
            zombieModeRunState.PendingEliteSquadCount = 0;
            zombieModeRunState.PendingMapEvent = ZombieModePendingMapEventType.None;
            SpawnZombieModeEliteSquadAsync(runId, count).Forget();
        }

        private async UniTask SpawnZombieModeEliteSquadAsync(int runId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat)
                {
                    return;
                }

                await TrySpawnZombieModeNormalZombieAsync(
                    runId,
                    GetZombieModeSpawnPosition(),
                    ZombieModeEnemyKind.Elite,
                    true,
                    () => zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat);
                await UniTask.Yield();
            }
        }

        private void CreateZombieModeHighValueAirdrop(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            bool granted = false;
            granted |= TryGiveRandomItemByTags(new string[] { "Gun" }, 4, ZombieModeTuning.StarterMaxQuality + 1);
            granted |= TryGiveRandomItemByTags(new string[] { "MeleeWeapon" }, 4, ZombieModeTuning.StarterMaxQuality + 1);
            granted |= TryGiveRandomItemByTags(new string[] { "Armor" }, 4, ZombieModeTuning.StarterMaxQuality + 1);
            if (!granted)
            {
                GrantZombieModeFallbackPurificationReward("HighValueSupply", 180);
            }
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Reward_MapEventHighValueAirdrop"));
        }

        private void SettleZombieModeFailureInsuranceShell(int runId)
        {
            if (runId <= 0 || zombieModeRunState.RunId != runId)
            {
                return;
            }

            float keepRatio = Mathf.Clamp(zombieModeRunState.InsuranceState.RandomKeepRatio, 0f, 0.80f);
            Item specified = zombieModeRunState.InsuranceState.SpecifiedKeepItem;
            zombieModeRunState.PurificationPoints = 0;
            if (keepRatio <= 0f && specified == null)
            {
                zombieModeRunState.InsuranceState.Reset();
                return;
            }

            List<Item> candidates = CollectZombieModeInsuranceCandidates();
            if (candidates.Count <= 0)
            {
                zombieModeRunState.InsuranceState.Reset();
                return;
            }

            int saved = 0;
            if (specified != null && candidates.Contains(specified))
            {
                if (TryMoveZombieModeInsuredItemToStorage(specified))
                {
                    candidates.Remove(specified);
                    saved++;
                }
            }

            int randomKeepCount = Mathf.FloorToInt(candidates.Count * keepRatio);
            for (int i = 0; i < randomKeepCount && candidates.Count > 0; i++)
            {
                int index = UnityEngine.Random.Range(0, candidates.Count);
                Item item = candidates[index];
                candidates.RemoveAt(index);
                if (TryMoveZombieModeInsuredItemToStorage(item))
                {
                    saved++;
                }
            }

            if (saved > 0)
            {
                NotificationText.Push(string.Format(L10n.T("BossRush_ZombieMode_Settle_InsuranceSaved"), saved));
            }
            zombieModeRunState.InsuranceState.Reset();
        }

        private List<Item> CollectZombieModeInsuranceCandidates()
        {
            return CollectZombieModeTopLevelPlayerItems();
        }

        private bool TryMoveZombieModeInsuredItemToStorage(Item item)
        {
            if (item == null || item.IsBeingDestroyed)
            {
                return false;
            }

            try
            {
                item.Detach();
                ReforgeDataPersistence.SyncCurrentReforgeState(item);
                PlayerStorage.Push(item, true);
                return true;
            }
            catch
            {
                try
                {
                    if (PlayerStorage.Inventory != null)
                    {
                        return PlayerStorage.Inventory.AddItem(item);
                    }
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] PlayerStorage.AddItem 失败: " + e.Message);
                }
            }

            return false;
        }

        private string GetZombieModeAttributeKey(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.AttributeMaxHealth:
                    return ZombieModeAttributeMaxHealthKey;
                case ZombieModeRewardType.AttributeMoveSpeed:
                    return ZombieModeAttributeMoveSpeedKey;
                case ZombieModeRewardType.AttributeMeleeDamage:
                    return ZombieModeAttributeMeleeDamageKey;
                case ZombieModeRewardType.AttributeRangedDamage:
                    return ZombieModeAttributeRangedDamageKey;
                case ZombieModeRewardType.AttributeReloadSpeed:
                    return ZombieModeAttributeReloadSpeedKey;
                case ZombieModeRewardType.AttributeDamageReduction:
                    return ZombieModeAttributeDamageReductionKey;
                default:
                    return string.Empty;
            }
        }

        private float GetZombieModeAttributeCap(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.AttributeMaxHealth:
                    return 1.00f;
                case ZombieModeRewardType.AttributeMoveSpeed:
                    return 0.30f;
                case ZombieModeRewardType.AttributeMeleeDamage:
                    return 1.20f;
                case ZombieModeRewardType.AttributeRangedDamage:
                    return 1.00f;
                case ZombieModeRewardType.AttributeReloadSpeed:
                    return 0.80f;
                case ZombieModeRewardType.AttributeDamageReduction:
                    return 0.40f;
                default:
                    return 1f;
            }
        }

        public void OpenZombieModeTemporaryNpcServiceUi(int runId, string serviceType)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            ZombieModeTemporaryNpc npc = FindZombieModeTemporaryNpc(serviceType);
            if (npc == null || npc.ServiceState == null)
            {
                return;
            }

            ZombieModeTemporaryNpcServiceView[] existingViews = UnityEngine.Object.FindObjectsOfType<ZombieModeTemporaryNpcServiceView>(true);
            for (int i = 0; i < existingViews.Length; i++)
            {
                if (existingViews[i] == null || existingViews[i].gameObject == null)
                {
                    continue;
                }

                Destroy(existingViews[i].gameObject);
            }

            GameObject root = new GameObject("ZombieMode_TemporaryNpcServiceUi_" + serviceType);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.RewardUi, root, root, null);
            ZombieModeTemporaryNpcServiceView view = root.AddComponent<ZombieModeTemporaryNpcServiceView>();
            view.Initialize(runId, this, serviceType);
        }

        private ZombieModeTemporaryNpc FindZombieModeTemporaryNpc(string serviceType)
        {
            for (int i = 0; i < zombieModeRunState.TemporaryNpcs.Count; i++)
            {
                ZombieModeTemporaryNpc npc = zombieModeRunState.TemporaryNpcs[i];
                if (npc != null &&
                    zombieModeRunState.TemporaryNpcs[i].ServiceState != null &&
                    string.Equals(npc.ServiceType, serviceType, System.StringComparison.Ordinal))
                {
                    return npc;
                }
            }

            return null;
        }

        public ZombieModeNpcCatalog.MerchantStockEntry[] GetZombieModeMerchantStock(int runId, string serviceType)
        {
            ZombieModeTemporaryNpc npc = IsZombieModeRunValid(runId) ? FindZombieModeTemporaryNpc(serviceType) : null;
            if (npc == null || npc.ServiceState == null)
            {
                return new ZombieModeNpcCatalog.MerchantStockEntry[0];
            }

            return npc.ServiceState.BossNodeStock || zombieModeRunState.CurrentWave > 0 && IsZombieModeBossWave(zombieModeRunState.CurrentWave)
                ? ZombieModeNpcCatalog.BossNodeStock
                : ZombieModeNpcCatalog.NormalWaveStock;
        }

        public ZombieModeNpcCatalog.NurseServiceEntry[] GetZombieModeNurseServices(int runId, string serviceType)
        {
            return IsZombieModeRunValid(runId) ? ZombieModeNpcCatalog.NurseServices : new ZombieModeNpcCatalog.NurseServiceEntry[0];
        }

        public int GetZombieModeNpcServiceRemaining(int runId, string serviceType, int index)
        {
            ZombieModeTemporaryNpc npc = IsZombieModeRunValid(runId) ? FindZombieModeTemporaryNpc(serviceType) : null;
            if (npc == null || npc.ServiceState == null || index < 0)
            {
                return 0;
            }

            if (string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal))
            {
                return index < npc.ServiceState.NurseUsesRemaining.Count ? npc.ServiceState.NurseUsesRemaining[index] : 0;
            }

            return index < npc.ServiceState.MerchantStockRemaining.Count ? npc.ServiceState.MerchantStockRemaining[index] : 0;
        }

        public int GetZombieModeNpcServicePrice(int runId, int basePrice)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.FloorToInt(basePrice * GetZombieModeNpcServicePriceMultiplier()));
        }

        public bool TryPurchaseZombieModeMerchantStock(int runId, string serviceType, int stockIndex)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            ZombieModeTemporaryNpc npc = FindZombieModeTemporaryNpc(serviceType);
            ZombieModeNpcCatalog.MerchantStockEntry[] stock = GetZombieModeMerchantStock(runId, serviceType);
            if (npc == null || npc.ServiceState == null || stockIndex < 0 || stockIndex >= stock.Length || stockIndex >= npc.ServiceState.MerchantStockRemaining.Count)
            {
                return false;
            }

            if (npc.ServiceState.MerchantStockRemaining[stockIndex] <= 0)
            {
                return false;
            }

            ZombieModeNpcCatalog.MerchantStockEntry entry = stock[stockIndex];
            int cost = GetZombieModeNpcServicePrice(runId, entry.BasePrice);
            if (!SpendZombieModePurificationPoints(cost, "ZombieModeMerchantService"))
            {
                return false;
            }

            bool granted = false;
            if (entry.TypeId > 0)
            {
                granted = TryGiveZombieModeItemToPlayerOrDrop(entry.TypeId);
            }
            else if (!string.IsNullOrEmpty(entry.GrantTag))
            {
                if (IsZombieModeMerchantBulletStock(entry))
                {
                    granted = TryGiveZombieModeMerchantAmmoForEquippedWeapon(entry);
                }

                if (!granted)
                {
                    granted = TryPurchaseZombieModeGuaranteedMerchantStockFromPool(entry);
                }
                if (!granted)
                {
                    granted = TryGiveRandomZombieModeMerchantItemFromModeEPool(entry);
                }
            }

            if (!granted)
            {
                RefundZombieModePurificationPoints(cost, "ZombieModeMerchantServiceFailed");
                return false;
            }

            npc.ServiceState.MerchantStockRemaining[stockIndex]--;
            NotificationText.Push(L10n.T(entry.DisplayKey));
            return true;
        }

        private bool IsZombieModeMerchantBulletStock(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            return entry != null &&
                   (string.Equals(entry.GrantTag, "Bullet", System.StringComparison.Ordinal) ||
                    string.Equals(entry.GrantTag, "Ammo", System.StringComparison.Ordinal));
        }

        private bool TryGiveZombieModeMerchantAmmoForEquippedWeapon(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (zombieModeRunState.GuaranteedMerchantPurchasePending)
            {
                int guaranteedMinQuality = Mathf.Max(entry.GrantMinQuality, zombieModeRunState.GuaranteedMerchantPurchaseMinQuality);
                int guaranteedMaxQuality = Mathf.Max(entry.GrantMaxQuality, zombieModeRunState.GuaranteedMerchantPurchaseMinQuality);
                if (TryGiveZombieModeMerchantAmmoForEquippedWeapon(entry, guaranteedMinQuality, guaranteedMaxQuality))
                {
                    zombieModeRunState.GuaranteedMerchantPurchasePending = false;
                    zombieModeRunState.GuaranteedMerchantPurchaseMinQuality = 0;
                    return true;
                }
            }

            return TryGiveZombieModeMerchantAmmoForEquippedWeapon(entry, entry.GrantMinQuality, entry.GrantMaxQuality);
        }

        private bool TryGiveZombieModeMerchantAmmoForEquippedWeapon(ZombieModeNpcCatalog.MerchantStockEntry entry, int minQuality, int maxQuality)
        {
            Item weapon = TryGetZombieModePreferredWeaponForMerchantAmmo();
            string caliber = TryReadZombieModeItemCaliber(weapon);
            if (string.IsNullOrEmpty(caliber))
            {
                return false;
            }

            return TryGiveZombieModeAmmo(caliber, 100, minQuality, maxQuality);
        }

        private Item TryGetZombieModePreferredWeaponForMerchantAmmo()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return null;
            }

            Slot primarySlot = player.PrimWeaponSlot();
            Item primary = primarySlot != null ? primarySlot.Content : null;
            if (IsZombieModeAmmoTargetWeapon(primary))
            {
                return primary;
            }

            Slot secondarySlot = player.SecWeaponSlot();
            Item secondary = secondarySlot != null ? secondarySlot.Content : null;
            return IsZombieModeAmmoTargetWeapon(secondary) ? secondary : null;
        }

        private bool IsZombieModeAmmoTargetWeapon(Item item)
        {
            return item != null &&
                   item.GetComponent<ItemSetting_Gun>() != null &&
                   !string.IsNullOrEmpty(TryReadZombieModeItemCaliber(item));
        }

        private bool TryPurchaseZombieModeGuaranteedMerchantStockFromPool(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            if (entry == null ||
                string.IsNullOrEmpty(entry.GrantTag) ||
                !zombieModeRunState.GuaranteedMerchantPurchasePending)
            {
                return false;
            }

            int maxQuality = Mathf.Min(entry.GrantMaxQuality, zombieModeRunState.GuaranteedMerchantPurchaseMinQuality);
            maxQuality = Mathf.Max(maxQuality, zombieModeRunState.GuaranteedMerchantPurchaseMinQuality);
            int minQuality = Mathf.Max(entry.GrantMinQuality, 1);
            if (maxQuality < minQuality)
            {
                return false;
            }

            int[] poolIds = GetZombieModeMerchantModeECategoryPoolIds(entry);
            for (int quality = maxQuality; quality >= minQuality; quality--)
            {
                int typeId = PickZombieModeStrictQualityCandidate(poolIds, quality, quality);
                if (typeId <= 0)
                {
                    string[] grantTags = GetZombieModeMerchantGrantTagAliases(entry.GrantTag);
                    typeId = FindRandomItemTypeByTags(grantTags, quality, quality);
                }

                if (!TryGiveZombieModeItemToPlayerOrDrop(typeId))
                {
                    continue;
                }

                zombieModeRunState.GuaranteedMerchantPurchasePending = false;
                zombieModeRunState.GuaranteedMerchantPurchaseMinQuality = 0;
                return true;
            }

            return false;
        }

        private bool TryGiveRandomZombieModeMerchantItemFromModeEPool(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            int[] poolIds = GetZombieModeMerchantModeECategoryPoolIds(entry);
            int typeId = PickZombieModeStrictQualityCandidate(poolIds, entry.GrantMinQuality, entry.GrantMaxQuality);
            if (TryGiveZombieModeItemToPlayerOrDrop(typeId))
            {
                return true;
            }

            string[] grantTags = GetZombieModeMerchantGrantTagAliases(entry.GrantTag);
            return TryGiveRandomItemByTags(grantTags, entry.GrantMinQuality, entry.GrantMaxQuality);
        }

        private int[] GetZombieModeMerchantModeECategoryPoolIds(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            if (entry == null)
            {
                return new int[0];
            }

            string suffix = GetZombieModeMerchantModeECategorySuffix(entry.GrantTag);
            if (string.IsNullOrEmpty(suffix))
            {
                return new int[0];
            }

            return GetModeEMerchantCategoryPoolIds(suffix);
        }

        public bool TryUseZombieModeNurseService(int runId, string serviceType, int serviceIndex)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            ZombieModeTemporaryNpc npc = FindZombieModeTemporaryNpc(serviceType);
            ZombieModeNpcCatalog.NurseServiceEntry[] services = ZombieModeNpcCatalog.NurseServices;
            if (npc == null || npc.ServiceState == null || serviceIndex < 0 || serviceIndex >= services.Length || serviceIndex >= npc.ServiceState.NurseUsesRemaining.Count)
            {
                return false;
            }

            if (npc.ServiceState.NurseUsesRemaining[serviceIndex] <= 0)
            {
                return false;
            }

            int cost = GetZombieModeNpcServicePrice(runId, services[serviceIndex].BasePrice);
            if (!SpendZombieModePurificationPoints(cost, "ZombieModeNurseService"))
            {
                return false;
            }

            if (!ApplyZombieModeNurseServiceEffect(serviceIndex))
            {
                RefundZombieModePurificationPoints(cost, "ZombieModeNurseServiceFailed");
                return false;
            }

            npc.ServiceState.NurseUsesRemaining[serviceIndex]--;
            NotificationText.Push(L10n.T(services[serviceIndex].ServiceKey));
            return true;
        }

        private bool ApplyZombieModeNurseServiceEffect(int serviceIndex)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null)
            {
                return false;
            }

            float missing = Mathf.Max(0f, player.Health.MaxHealth - player.Health.CurrentHealth);
            if (serviceIndex == 0)
            {
                float heal = Mathf.Min(missing, player.Health.MaxHealth * 0.5f);
                player.Health.SetHealth(player.Health.CurrentHealth + Mathf.Max(1f, heal));
                return true;
            }

            if (serviceIndex == 1)
            {
                player.Health.SetHealth(player.Health.MaxHealth);
                return true;
            }

            if (serviceIndex == 2)
            {
                return ClearZombieModeNurseNegativeBuffs(player, true, false, false);
            }

            if (serviceIndex == 3)
            {
                return ClearZombieModeNurseNegativeBuffs(player, false, true, false);
            }

            if (serviceIndex == 4)
            {
                player.Health.SetHealth(player.Health.MaxHealth);
                return ClearZombieModeNurseNegativeBuffs(player, true, true, true);
            }

            return false;
        }

        private bool ClearZombieModeNurseNegativeBuffs(CharacterMainControl player, bool clearPoison, bool clearBleeding, bool clearExtendedNegative)
        {
            if (player == null)
            {
                return false;
            }

            bool changed = false;
            if (clearPoison)
            {
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Poison);
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Nauseous);
            }

            if (clearBleeding)
            {
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Bleeding);
            }

            if (clearExtendedNegative)
            {
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Pain);
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Burning);
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Electric);
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Freeze);
            }

            return changed || clearPoison || clearBleeding || clearExtendedNegative;
        }

        private bool TryRemoveZombieModeBuffsByTag(CharacterMainControl player, Buff.BuffExclusiveTags tag)
        {
            if (player == null || tag == Buff.BuffExclusiveTags.NotExclusive)
            {
                return false;
            }

            try
            {
                player.RemoveBuffsByTag(tag, false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private float GetZombieModeNpcServicePriceMultiplier()
        {
            return ZombieModeNpcCatalog.GetPollutionPriceMultiplier(zombieModeRunState.TotalPollution);
        }

        private bool TryGiveZombieModeItemToPlayerOrDrop(int typeId)
        {
            if (typeId <= 0)
            {
                return false;
            }

            Item item = null;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null)
                {
                    return false;
                }

                return TryDeliverZombieModeItemToPlayerOrDrop(item, "TypeId_" + typeId);
            }
            catch
            {
                try
                {
                    if (item != null)
                    {
                        item.DestroyTree();
                    }
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] reward item DestroyTree 失败: " + e.Message);
                }
                return false;
            }
        }

        private bool TryDeliverZombieModeItemToPlayerOrDrop(Item item, string logContext)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                bool sent = false;
                try
                {
                    sent = ItemUtilities.SendToPlayerCharacterInventory(item, false);
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] SendToPlayerCharacterInventory 失败: " + e.Message);
                }

                if (sent)
                {
                    return true;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                Vector3 dropPosition = player != null
                    ? player.transform.position + Vector3.up * 0.3f
                    : (zombieModeRunState.ActiveSafeZoneActive ? zombieModeRunState.ActiveSafeZoneCenter + Vector3.up * 0.3f : Vector3.up * 0.3f);
                item.Drop(dropPosition, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                return true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] reward item deliver failed: " + logContext + ", " + e.Message);
                try
                {
                    item.DestroyTree();
                }
                catch (System.Exception destroyEx)
                {
                    DevLog("[ZombieMode] reward item DestroyTree 失败: " + destroyEx.Message);
                }

                return false;
            }
        }

        private void GrantZombieModeFallbackPurificationReward(string reason, int points)
        {
            points = Mathf.Max(1, points);
            zombieModeRunState.PurificationPoints += points;
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_RewardFallbackPurification"),
                points));
            DevLog("[ZombieMode] Reward fallback purification: " + reason + ", points=" + points);
        }

        private int CalculateZombieModePurificationRewardPoints(bool bossNode)
        {
            int basePoints = bossNode ? ZombieModeTuning.InstantPurificationBossBase : ZombieModeTuning.InstantPurificationNormalBase;
            int pollutionBonus = (bossNode ? 25 : 10) * zombieModeRunState.TotalPollution;
            return Mathf.Max(1, basePoints + pollutionBonus);
        }

        private void ClearZombieModeRewardShell()
        {
            if (zombieModeRewardUiRoot != null)
            {
                Destroy(zombieModeRewardUiRoot);
                zombieModeRewardUiRoot = null;
            }
        }
    }

    public sealed class ZombieModeRewardSelectionView : MonoBehaviour
    {
        // ==================== 配色方案 ====================
        private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color PanelOuterColor = new Color(0.12f, 0.16f, 0.24f, 0.98f);
        private static readonly Color PanelBorderColor = new Color(0.22f, 0.30f, 0.44f, 0.45f);
        private static readonly Color PanelInnerColor = new Color(0.10f, 0.12f, 0.16f, 0.98f);
        private static readonly Color HeaderColor = new Color(0.14f, 0.20f, 0.32f, 1.00f);
        private static readonly Color AccentLineColor = new Color(0.35f, 0.55f, 0.85f, 0.70f);

        // 奖励卡片
        private static readonly Color RewardCardColor = new Color(0.12f, 0.16f, 0.22f, 0.98f);
        private static readonly Color RewardCardAccentColor = new Color(0.44f, 0.82f, 0.92f, 0.95f);
        private static readonly Color RewardCardHoverColor = new Color(0.18f, 0.24f, 0.32f, 1.00f);

        // 免费刷新
        private static readonly Color FreeRefreshColor = new Color(0.14f, 0.36f, 0.28f, 1.00f);
        private static readonly Color FreeRefreshHoverColor = new Color(0.20f, 0.48f, 0.36f, 1.00f);
        private static readonly Color FreeRefreshDisabledColor = new Color(0.18f, 0.20f, 0.20f, 0.70f);
        // 付费刷新
        private static readonly Color PaidRefreshColor = new Color(0.38f, 0.30f, 0.14f, 1.00f);
        private static readonly Color PaidRefreshHoverColor = new Color(0.50f, 0.40f, 0.20f, 1.00f);

        private static readonly Color InfoTextColor = new Color(0.72f, 0.78f, 0.86f, 0.95f);

        private int runId;
        private ModBehaviour owner;
        private ZombieModeUIHelper.ModalInputLease inputLease;

        public void Initialize(int newRunId, ModBehaviour newOwner)
        {
            runId = newRunId;
            owner = newOwner;
            Build();
            ClaimInputAndPause();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            // ── 全屏遮罩 ──
            GameObject backdrop = ZombieModeUIHelper.CreateRect("Backdrop", transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, Vector2.zero);
            Image backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = BackdropColor;
            backdropImage.raycastTarget = true;

            // ── 外框 ──
            GameObject outer = ZombieModeUIHelper.CreateRect("PanelOuter", transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(860f, 540f), new Vector2(0.5f, 0.5f));
            Image outerImage = outer.AddComponent<Image>();
            outerImage.color = PanelOuterColor;

            // ── 亮边层 ──
            GameObject borderGlow = ZombieModeUIHelper.CreateRect("BorderGlow", outer.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image borderImg = borderGlow.AddComponent<Image>();
            borderImg.color = PanelBorderColor;

            // ── 主面板 ──
            GameObject panel = ZombieModeUIHelper.CreateRect("Panel", borderGlow.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = PanelInnerColor;

            // ── 标题栏 ──
            float yPos = 0f;
            float headerH = 64f;
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH * 0.5f)), new Vector2(0f, headerH), new Vector2(0.5f, 0.5f));
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = HeaderColor;

            ZombieModeUIHelper.CreateText("Title", header.transform,
                owner.GetZombieModeRewardTitle(runId), 26,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Center, Color.white);
            yPos += headerH;

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -yPos), 2f, AccentLineColor);
            yPos += 6f;

            // ── 信息栏 ──
            float infoH = 34f;
            ZombieModeUIHelper.CreateText("Info", panel.transform,
                string.Format(
                    L10n.T("BossRush_ZombieMode_Reward_Info"),
                    owner.GetZombieModePurificationPoints(runId),
                    owner.GetZombieModeRewardFreeRefreshes(runId),
                    owner.GetZombieModeRewardPaidRefreshCost(runId).ToString("N0")),
                16,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(yPos + infoH * 0.5f)), new Vector2(-40f, infoH),
                TextAlignmentOptions.Center, InfoTextColor);
            yPos += infoH + 10f;

            // ── 奖励卡片 ──
            IList<ZombieModeRewardType> options = owner.GetZombieModeRewardOptions(runId);
            bool bossNode = owner.IsZombieModeBossRewardNode(runId);
            float cardW = bossNode ? 220f : 240f;
            float cardH = bossNode ? 100f : 120f;

            if (bossNode)
            {
                // 4 选项：2×2 网格
                Vector2[] positions = new Vector2[]
                {
                    new Vector2(-120f, -(yPos + cardH * 0.5f)),
                    new Vector2(120f, -(yPos + cardH * 0.5f)),
                    new Vector2(-120f, -(yPos + cardH + 12f + cardH * 0.5f)),
                    new Vector2(120f, -(yPos + cardH + 12f + cardH * 0.5f))
                };
                for (int i = 0; i < options.Count && i < positions.Length; i++)
                {
                    CreateRewardCard("Reward_" + options[i].ToString(), panel.transform,
                        owner.GetZombieModeRewardDisplayText(runId, options[i]),
                        positions[i], new Vector2(cardW, cardH), options[i]);
                }
                yPos += cardH * 2f + 12f + 16f;
            }
            else
            {
                // 3 选项：横排
                float totalW = cardW * options.Count + 16f * (options.Count - 1);
                float startX = -totalW * 0.5f + cardW * 0.5f;
                for (int i = 0; i < options.Count; i++)
                {
                    float x = startX + i * (cardW + 16f);
                    CreateRewardCard("Reward_" + options[i].ToString(), panel.transform,
                        owner.GetZombieModeRewardDisplayText(runId, options[i]),
                        new Vector2(x, -(yPos + cardH * 0.5f)), new Vector2(cardW, cardH), options[i]);
                }
                yPos += cardH + 16f;
            }

            // ── 分隔线 ──
            ZombieModeUIHelper.CreateSeparator("Sep", panel.transform,
                new Vector2(0.08f, 1f), new Vector2(0.92f, 1f),
                new Vector2(0f, -yPos), 1f, new Color(0.25f, 0.35f, 0.50f, 0.35f));
            yPos += 14f;

            // ── 刷新按钮行（固定底部） ──
            GameObject refreshRow = ZombieModeUIHelper.CreateRect("RefreshRow", panel.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 42f), new Vector2(-40f, 56f), new Vector2(0.5f, 0.5f));
            HorizontalLayoutGroup refreshLayout = refreshRow.AddComponent<HorizontalLayoutGroup>();
            refreshLayout.spacing = 30f;
            refreshLayout.childAlignment = TextAnchor.MiddleCenter;
            refreshLayout.childControlWidth = false;
            refreshLayout.childControlHeight = false;
            refreshLayout.childForceExpandWidth = false;
            refreshLayout.childForceExpandHeight = false;

            bool hasFreeRefresh = owner.GetZombieModeRewardFreeRefreshes(runId) > 0;
            CreateStyledRefreshButton(refreshRow.transform, "FreeRefresh",
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RefreshFree"), owner.GetZombieModeRewardFreeRefreshes(runId)),
                FreeRefreshColor, FreeRefreshHoverColor, FreeRefreshDisabledColor,
                hasFreeRefresh, false);
            CreateStyledRefreshButton(refreshRow.transform, "PaidRefresh",
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RefreshPaid"), owner.GetZombieModeRewardPaidRefreshCost(runId).ToString("N0")),
                PaidRefreshColor, PaidRefreshHoverColor, FreeRefreshDisabledColor,
                true, true);
        }

        private void CreateRewardCard(string name, Transform parent, string text, Vector2 position, Vector2 size, ZombieModeRewardType rewardType)
        {
            // ── 卡片底板 ──
            GameObject card = ZombieModeUIHelper.CreateRect(name, parent,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                position, size, new Vector2(0.5f, 0.5f));
            Image cardImage = card.AddComponent<Image>();
            cardImage.color = RewardCardColor;

            // ── 顶部高亮条 ──
            GameObject topAccent = ZombieModeUIHelper.CreateRect("TopAccent", card.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -2f), new Vector2(0f, 4f), new Vector2(0.5f, 1f));
            Image topAccentImg = topAccent.AddComponent<Image>();
            topAccentImg.color = RewardCardAccentColor;
            topAccentImg.raycastTarget = false;

            // ── 文本 ──
            ZombieModeUIHelper.CreateText("Text", card.transform, text, 18,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, -4f), new Vector2(-18f, -14f),
                TextAlignmentOptions.Center, Color.white);

            // ── 按钮 ──
            Button button = card.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = RewardCardColor;
            colors.highlightedColor = RewardCardHoverColor;
            colors.pressedColor = RewardCardColor * 0.85f;
            colors.selectedColor = RewardCardHoverColor;
            colors.disabledColor = RewardCardColor * 0.6f;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = cardImage;

            button.onClick.AddListener(delegate
            {
                if (owner != null)
                {
                    owner.SelectZombieModeReward(runId, rewardType);
                }
            });
        }

        private void CreateStyledRefreshButton(Transform parent, string name, string text,
            Color baseColor, Color hoverColor, Color disabledColor,
            bool interactable, bool paid)
        {
            float btnW = 240f;
            float btnH = 44f;
            Button button = ZombieModeUIHelper.CreateButton(
                name, parent, text,
                new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(btnW, btnH),
                interactable ? baseColor : disabledColor, 16,
                new Vector2(btnW - 14f, btnH - 8f),
                null, interactable);

            LayoutElement layoutElement = button.gameObject.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = button.gameObject.AddComponent<LayoutElement>();
            }
            layoutElement.minWidth = btnW;
            layoutElement.preferredWidth = btnW;
            layoutElement.minHeight = btnH;
            layoutElement.preferredHeight = btnH;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            Image image = button.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = interactable ? baseColor : disabledColor;
            colors.highlightedColor = hoverColor;
            colors.pressedColor = baseColor * 0.85f;
            colors.selectedColor = hoverColor;
            colors.disabledColor = disabledColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = image;

            if (interactable)
            {
                bool capturedPaid = paid;
                button.onClick.AddListener(delegate
                {
                    if (owner != null)
                    {
                        owner.RefreshZombieModeRewardSelection(runId, capturedPaid);
                    }
                });
            }
        }

        private void ClaimInputAndPause()
        {
            inputLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "RewardSelection");
        }

        private void RestoreInputState()
        {
            if (inputLease != null)
            {
                inputLease.Release();
                inputLease = null;
            }
        }

        private void OnDestroy()
        {
            RestoreInputState();
        }
    }

    public sealed class ZombieModeTemporaryNpcInteractable : InteractableBase
    {
        private int runId;
        private string serviceType = string.Empty;

        public void Initialize(int newRunId, string newServiceType)
        {
            runId = newRunId;
            serviceType = newServiceType ?? string.Empty;
            ApplyInteractName();
        }

        protected override void Awake()
        {
            ApplyInteractName();
            try
            {
                interactCollider = GetComponent<Collider>();
                interactMarkerOffset = new Vector3(0f, 1.4f, 0f);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc Awake collider 获取失败: " + e.Message);
            }

            try { base.Awake(); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.Awake 失败: " + e.Message); }
        }

        protected override void Start()
        {
            try { base.Start(); } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.Start 失败: " + e.Message); }
            ApplyInteractName();
        }

        protected override bool IsInteractable()
        {
            return ModBehaviour.Instance != null && runId > 0;
        }

        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.OnInteractStart 失败: " + e.Message);
            }

            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.OpenZombieModeTemporaryNpcServiceUi(runId, serviceType);
            }
        }

        protected override void OnTimeOut()
        {
            try
            {
                base.OnTimeOut();
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc base.OnTimeOut 失败: " + e.Message);
            }
        }

        private void ApplyInteractName()
        {
            string key = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? "BossRush_ZombieMode_Npc_InteractNurse"
                : "BossRush_ZombieMode_Npc_InteractMerchant";
            try
            {
                overrideInteractName = true;
                _overrideInteractNameKey = key;
                InteractName = key;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] TemporaryNpc InteractName 设置失败: " + e.Message);
            }
        }
    }

    public sealed class ZombieModeTemporaryNpcServiceView : MonoBehaviour
    {
        private int runId;
        private ModBehaviour owner;
        private string serviceType = string.Empty;
        private ZombieModeUIHelper.ModalInputLease inputLease;

        public void Initialize(int newRunId, ModBehaviour newOwner, string newServiceType)
        {
            runId = newRunId;
            owner = newOwner;
            serviceType = newServiceType ?? string.Empty;
            Build();
            ClaimInputAndPause();
        }

        private void Build()
        {
            Canvas canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30500;
            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // ── 全屏遮罩 ──
            GameObject backdrop = ZombieModeUIHelper.CreateRect("Backdrop", transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, Vector2.zero);
            Image backdropImage = backdrop.AddComponent<Image>();
            backdropImage.color = new Color(0f, 0f, 0f, 0.72f);
            backdropImage.raycastTarget = true;

            // ── 外框 ──
            GameObject outer = ZombieModeUIHelper.CreateRect("PanelOuter", transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(820f, 620f), new Vector2(0.5f, 0.5f));
            Image outerImage = outer.AddComponent<Image>();
            outerImage.color = new Color(0.12f, 0.16f, 0.24f, 0.98f);

            // ── 亮边层 ──
            GameObject borderGlow = ZombieModeUIHelper.CreateRect("BorderGlow", outer.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image borderImg = borderGlow.AddComponent<Image>();
            borderImg.color = new Color(0.22f, 0.30f, 0.44f, 0.45f);

            // ── 主面板 ──
            GameObject panel = ZombieModeUIHelper.CreateRect("Panel", borderGlow.transform,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-3f, -3f), new Vector2(0.5f, 0.5f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.10f, 0.12f, 0.16f, 0.98f);

            // ── 标题栏 ──
            float headerH = 64f;
            GameObject header = ZombieModeUIHelper.CreateRect("Header", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH * 0.5f)), new Vector2(0f, headerH), new Vector2(0.5f, 0.5f));
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = new Color(0.14f, 0.20f, 0.32f, 1.00f);

            string titleKey = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? "BossRush_ZombieMode_Npc_TempNurse"
                : "BossRush_ZombieMode_Npc_TempMerchant";
            ZombieModeUIHelper.CreateText("Title", header.transform,
                L10n.T(titleKey), 26,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(700f, 60f),
                TextAlignmentOptions.Center, Color.white);

            // ── 标题装饰线 ──
            ZombieModeUIHelper.CreateSeparator("AccentLine", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -headerH), 2f, new Color(0.35f, 0.55f, 0.85f, 0.70f));

            // ── 副标题 ──
            ZombieModeUIHelper.CreateText(
                "Subtitle",
                panel.transform,
                string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                    ? "治疗 / 解毒 / 止血"
                    : "丧尸模式终端分类抽取",
                15,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -(headerH + 22f)), new Vector2(-40f, 30f),
                TextAlignmentOptions.Center,
                new Color(0.62f, 0.70f, 0.82f, 0.90f));

            Transform body = CreateScrollableBody(panel.transform);
            if (string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal))
            {
                BuildNurseServices(body);
            }
            else
            {
                BuildMerchantStock(body);
            }

            CreateCloseButton(panel.transform);
        }

        private Transform CreateScrollableBody(Transform parent)
        {
            // 使用 anchor-based 布局适配新的分层面板
            // 标题栏64 + 装饰线2 + 副标题30 + 间距 = 约 110px 顶部偏移
            // 底部留 70px 给关闭按钮
            GameObject body = ZombieModeUIHelper.CreateRect(
                "Body",
                parent,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.5f, 0.5f));
            RectTransform bodyRect = body.GetComponent<RectTransform>();
            bodyRect.offsetMin = new Vector2(20f, 70f);   // 底部留给关闭按钮
            bodyRect.offsetMax = new Vector2(-20f, -110f); // 顶部留给标题栏+副标题

            Image bodyImage = body.AddComponent<Image>();
            bodyImage.color = new Color(0.06f, 0.08f, 0.12f, 0.60f);

            ScrollRect scrollRect = body.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            GameObject viewport = ZombieModeUIHelper.CreateRect(
                "Viewport",
                body.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.5f, 0.5f));
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.offsetMin = new Vector2(10f, 10f);
            viewportRect.offsetMax = new Vector2(-10f, -10f);
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            Mask viewportMask = viewport.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            GameObject content = ZombieModeUIHelper.CreateRect(
                "Content",
                viewport.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(0f, 0f),
                new Vector2(0.5f, 1f));
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(0f, 0f);

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            return content.transform;
        }

        private void BuildMerchantStock(Transform parent)
        {
            GridLayoutGroup grid = parent.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.cellSize = new Vector2(168f, 102f);
            grid.spacing = new Vector2(12f, 12f);
            grid.childAlignment = TextAnchor.UpperCenter;

            ContentSizeFitter fitter = parent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ZombieModeNpcCatalog.MerchantStockEntry[] stock = owner != null
                ? owner.GetZombieModeMerchantStock(runId, serviceType)
                : new ZombieModeNpcCatalog.MerchantStockEntry[0];
            for (int i = 0; i < stock.Length && i < ZombieModeNpcCatalog.MaxMerchantStockButtons; i++)
            {
                ZombieModeNpcCatalog.MerchantStockEntry entry = stock[i];
                int index = i;
                int remaining = owner != null ? owner.GetZombieModeNpcServiceRemaining(runId, serviceType, index) : 0;
                int price = owner != null ? owner.GetZombieModeNpcServicePrice(runId, entry.BasePrice) : entry.BasePrice;
                string label = L10n.T(entry.DisplayKey) +
                    "\n<size=80%>" + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServicePrice"), price) +
                    "  " + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServiceRemaining"), remaining) + "</size>";
                CreateServiceButton(parent, "Merchant_" + i, label, Vector2.zero, remaining > 0, delegate
                {
                    if (owner != null && owner.TryPurchaseZombieModeMerchantStock(runId, serviceType, index))
                    {
                        Rebuild();
                    }
                });
            }
        }

        private void BuildNurseServices(Transform parent)
        {
            VerticalLayoutGroup layout = parent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.spacing = 14f;
            layout.padding = new RectOffset(18, 18, 6, 6);

            ContentSizeFitter fitter = parent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ZombieModeNpcCatalog.NurseServiceEntry[] services = owner != null
                ? owner.GetZombieModeNurseServices(runId, serviceType)
                : new ZombieModeNpcCatalog.NurseServiceEntry[0];
            for (int i = 0; i < services.Length; i++)
            {
                ZombieModeNpcCatalog.NurseServiceEntry entry = services[i];
                int index = i;
                int remaining = owner != null ? owner.GetZombieModeNpcServiceRemaining(runId, serviceType, index) : 0;
                int price = owner != null ? owner.GetZombieModeNpcServicePrice(runId, entry.BasePrice) : entry.BasePrice;
                string label = L10n.T(entry.ServiceKey) +
                    "\n<size=80%>" + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServicePrice"), price) +
                    "  " + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServiceRemaining"), remaining) + "</size>";
                CreateServiceButton(parent, "Nurse_" + i, label, Vector2.zero, remaining > 0, delegate
                {
                    if (owner != null && owner.TryUseZombieModeNurseService(runId, serviceType, index))
                    {
                        Rebuild();
                    }
                });
            }
        }

        private void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
            Build();
        }

        private void CreateServiceButton(Transform parent, string name, string text, Vector2 position, bool interactable, UnityEngine.Events.UnityAction action)
        {
            Color normalColor = interactable ? new Color(0.12f, 0.18f, 0.26f, 0.98f) : new Color(0.14f, 0.14f, 0.14f, 0.70f);
            Color hoverColor = new Color(0.18f, 0.28f, 0.38f, 1.00f);
            Color accentColor = interactable ? new Color(0.44f, 0.82f, 0.92f, 0.95f) : new Color(0.30f, 0.30f, 0.30f, 0.70f);

            GameObject obj = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(168f, 102f));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            LayoutElement layoutElement = obj.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 168f;
            layoutElement.preferredHeight = 102f;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
            Image image = obj.AddComponent<Image>();
            image.color = normalColor;
            Button button = obj.AddComponent<Button>();
            button.interactable = interactable;

            // 悬停色
            ColorBlock colors = button.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = hoverColor;
            colors.pressedColor = normalColor * 0.85f;
            colors.selectedColor = hoverColor;
            colors.disabledColor = normalColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = image;

            // 顶部高亮条
            GameObject accent = ZombieModeUIHelper.CreateRect(
                "Accent",
                obj.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -2f),
                new Vector2(0f, 4f),
                new Vector2(0.5f, 1f));
            Image accentImage = accent.AddComponent<Image>();
            accentImage.color = accentColor;
            accentImage.raycastTarget = false;
            ZombieModeUIHelper.CreateText("Text", obj.transform, text, 14, Vector2.zero, new Vector2(154f, 86f), TextAlignmentOptions.Center, Color.white);
            if (interactable && action != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private void CreateCloseButton(Transform parent)
        {
            Color closeNormal = new Color(0.35f, 0.16f, 0.18f, 1.00f);
            Color closeHover = new Color(0.48f, 0.22f, 0.24f, 1.00f);

            // 固定到面板底部
            Button button = ZombieModeUIHelper.CreateButton(
                "Close",
                parent,
                L10n.T("BossRush_ZombieMode_Npc_Close"),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 36f),
                new Vector2(180f, 44f),
                closeNormal,
                17,
                new Vector2(168f, 36f),
                null,
                true);

            Image btnImage = button.GetComponent<Image>();
            ColorBlock colors = button.colors;
            colors.normalColor = closeNormal;
            colors.highlightedColor = closeHover;
            colors.pressedColor = closeNormal * 0.85f;
            colors.selectedColor = closeHover;
            colors.disabledColor = closeNormal * 0.6f;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = btnImage;

            button.onClick.AddListener(delegate
            {
                RestoreInputState();
                Destroy(gameObject);
            });
        }

        private void ClaimInputAndPause()
        {
            inputLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "TemporaryNpcService");
        }

        private void RestoreInputState()
        {
            if (inputLease != null)
            {
                inputLease.Release();
                inputLease = null;
            }
        }

        private void OnDestroy()
        {
            RestoreInputState();
        }
    }
}
