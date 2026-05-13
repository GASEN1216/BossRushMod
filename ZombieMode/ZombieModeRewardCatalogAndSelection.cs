// ============================================================================
// ZombieModeRewardCatalogAndSelection.cs - 丧尸模式奖励候选与选择
// ============================================================================

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
    }
}
