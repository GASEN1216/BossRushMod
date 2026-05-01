using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
        private const string ZombieModeContractAffixWeightKey = "ContractPollutionDeal";

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
                    return ZombieModeRewardCategory.Npc;
                case ZombieModeRewardType.FortificationPack:
                    return ZombieModeRewardCategory.Fortification;
                case ZombieModeRewardType.ContractPollutionDeal:
                case ZombieModeRewardType.ContractGearDeal:
                case ZombieModeRewardType.ContractHugePurification:
                case ZombieModeRewardType.ContractInsurance:
                    return ZombieModeRewardCategory.Contract;
                case ZombieModeRewardType.InsuranceKeepOne:
                case ZombieModeRewardType.InsuranceRandom10:
                case ZombieModeRewardType.InsuranceRandom20:
                case ZombieModeRewardType.InsuranceNearFull:
                    return ZombieModeRewardCategory.Insurance;
                case ZombieModeRewardType.MapEventHighValueAirdrop:
                case ZombieModeRewardType.MapEventEliteSquad:
                    return ZombieModeRewardCategory.MapEvent;
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

            bool extractionOpportunity = zombieModeRunState.CurrentRewardNode != null && zombieModeRunState.CurrentRewardNode.BossNode;
            string pendingTemporaryNpcServiceType = GetZombieModePendingTemporaryNpcServiceType(rewardType);
            ApplyZombieModeReward(rewardType);
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

            if (!string.IsNullOrEmpty(pendingTemporaryNpcServiceType))
            {
                SpawnZombieModeTemporaryNpc(runId, pendingTemporaryNpcServiceType, extractionOpportunity);
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
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_RefreshNoPoints"));
                return false;
            }

            zombieModeRunState.PurificationPoints -= cost;
            return true;
        }

        private void ApplyZombieModeReward(ZombieModeRewardType rewardType)
        {
            bool bossNode = zombieModeRunState.CurrentRewardNode != null && zombieModeRunState.CurrentRewardNode.BossNode;
            if (rewardType == ZombieModeRewardType.PurificationPoints)
            {
                int points = CalculateZombieModePurificationRewardPoints(bossNode);
                zombieModeRunState.PurificationPoints += points;
                NotificationText.Push(string.Format(L10n.T("BossRush_ZombieMode_Notify_RewardGranted"), points));
                return;
            }

            if (rewardType == ZombieModeRewardType.Heal)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.Health != null)
                {
                    player.Health.SetHealth(player.Health.MaxHealth);
                }
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_Heal"));
                return;
            }

            if (rewardType == ZombieModeRewardType.AttributeMaxHealth)
            {
                ApplyZombieModeAttributeReward(ZombieModeAttributeMaxHealthKey, 0.10f, 1.00f);
                return;
            }

            if (rewardType == ZombieModeRewardType.AttributeMoveSpeed)
            {
                ApplyZombieModeAttributeReward(ZombieModeAttributeMoveSpeedKey, 0.05f, 0.30f);
                return;
            }

            if (rewardType == ZombieModeRewardType.AttributeMeleeDamage)
            {
                ApplyZombieModeAttributeReward(ZombieModeAttributeMeleeDamageKey, 0.12f, 1.20f);
                return;
            }

            if (rewardType == ZombieModeRewardType.AttributeRangedDamage)
            {
                ApplyZombieModeAttributeReward(ZombieModeAttributeRangedDamageKey, 0.10f, 1.00f);
                return;
            }

            if (rewardType == ZombieModeRewardType.AttributeReloadSpeed)
            {
                ApplyZombieModeAttributeReward(ZombieModeAttributeReloadSpeedKey, 0.10f, 0.80f);
                return;
            }

            if (rewardType == ZombieModeRewardType.AttributeDamageReduction)
            {
                ApplyZombieModeAttributeReward(ZombieModeAttributeDamageReductionKey, 0.05f, 0.40f);
                return;
            }

            if (rewardType == ZombieModeRewardType.TempMerchant)
            {
                return;
            }

            if (rewardType == ZombieModeRewardType.TempNurse)
            {
                return;
            }

            if (rewardType == ZombieModeRewardType.RandomMeleeWeapon)
            {
                GrantZombieModeRandomMeleeReward(bossNode);
                return;
            }

            if (rewardType == ZombieModeRewardType.RandomGunWithAmmo)
            {
                GrantZombieModeRandomGunWithAmmoReward(bossNode);
                return;
            }

            if (rewardType == ZombieModeRewardType.AmmoSupply)
            {
                GrantZombieModeAmmoSupplyReward();
                return;
            }

            if (rewardType == ZombieModeRewardType.MedicalSupply)
            {
                GrantZombieModeMedicalSupplyReward();
                return;
            }

            if (rewardType == ZombieModeRewardType.ArmorOrHelmet)
            {
                GrantZombieModeArmorOrHelmetReward(bossNode);
                return;
            }

            if (rewardType == ZombieModeRewardType.FortificationPack)
            {
                GrantZombieModeFortificationPack(bossNode);
                return;
            }

            if (rewardType == ZombieModeRewardType.ContractPollutionDeal)
            {
                ApplyZombieModeContractPollutionDeal(bossNode);
                return;
            }

            if (rewardType == ZombieModeRewardType.ContractGearDeal)
            {
                ApplyZombieModeContractGearDeal(bossNode);
                return;
            }

            if (rewardType == ZombieModeRewardType.ContractHugePurification)
            {
                ApplyZombieModeContractHugePurification();
                return;
            }

            if (rewardType == ZombieModeRewardType.ContractInsurance)
            {
                ApplyZombieModeContractInsurance();
                return;
            }

            if (rewardType == ZombieModeRewardType.InsuranceKeepOne)
            {
                ApplyZombieModeInsuranceReward(0.10f, true);
                return;
            }

            if (rewardType == ZombieModeRewardType.InsuranceRandom10)
            {
                ApplyZombieModeInsuranceReward(0.10f, false);
                return;
            }

            if (rewardType == ZombieModeRewardType.InsuranceRandom20)
            {
                ApplyZombieModeInsuranceReward(0.20f, false);
                return;
            }

            if (rewardType == ZombieModeRewardType.InsuranceNearFull)
            {
                zombieModeRunState.PollutionFromContracts += 5;
                ApplyZombieModeInsuranceReward(0.80f, false);
                return;
            }

            if (rewardType == ZombieModeRewardType.MapEventHighValueAirdrop || rewardType == ZombieModeRewardType.MapEventEliteSquad)
            {
                ApplyZombieModeMapEventReward(rewardType);
                return;
            }

            if (rewardType == ZombieModeRewardType.NextNodeFreeRefresh)
            {
                zombieModeRunState.PendingFreeRefreshNextNode = Mathf.Clamp(
                    zombieModeRunState.PendingFreeRefreshNextNode + 1,
                    0,
                    ZombieModeTuning.FreeRefreshCapPerNode);
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_NextNodeFreeRefresh"));
                return;
            }

            if (rewardType == ZombieModeRewardType.HalfPricePaidRefresh)
            {
                zombieModeRunState.HalfPriceNextPaidRefresh = true;
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_HalfPricePaidRefresh"));
                return;
            }

            if (rewardType == ZombieModeRewardType.CurrentNodeFreeRefresh)
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
                return;
            }

            if (rewardType == ZombieModeRewardType.RandomHighQualityItem)
            {
                int typeId = FindRandomItemTypeByTags(null, 4, ZombieModeTuning.StarterMaxQuality + 1);
                if (TryGiveZombieModeItemToPlayerOrDrop(typeId))
                {
                    NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomHighQualityItem"));
                }
                return;
            }

            if (rewardType == ZombieModeRewardType.StarterReroll)
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
                }
                return;
            }

            TryGiveRandomItemByTags(new string[] { "Medic" }, 1, 4);
            TryGiveRandomItemByTags(new string[] { "Medical" }, 1, 4);
            TryGiveRandomItemByTags(new string[] { "Healing" }, 1, 4);
            TryGiveRandomItemByTags(new string[] { "Ammo" }, 1, 4);
            TryGiveRandomItemByTags(new string[] { "Bullet" }, 1, 4);
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomSupply"));
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

        private void ApplyZombieModeAttributeReward(string key, float increment, float cap)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            float current = 0f;
            zombieModeRunState.AttributeBonuses.TryGetValue(key, out current);
            float next = Mathf.Min(cap, current + Mathf.Max(0f, increment));
            zombieModeRunState.AttributeBonuses[key] = next;
            ApplyZombieModePlayerAttributeModifiers();

            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_AttributeMaxHealth"),
                Mathf.RoundToInt(next * 100f)));
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
                if (pair.Value <= 0f)
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
            if (player == null || player.CharacterItem == null || string.IsNullOrEmpty(statName) || percent <= 0f)
            {
                return;
            }

            try
            {
                Stat stat = player.CharacterItem.GetStat(statName);
                if (stat == null)
                {
                    return;
                }

                Modifier modifier = new Modifier(ModifierType.Add, stat.BaseValue * percent, this);
                stat.AddModifier(modifier);
                ZombieModeAttributeModifierRecord record = new ZombieModeAttributeModifierRecord();
                record.CharacterItem = player.CharacterItem;
                record.Stat = stat;
                record.Modifier = modifier;
                record.StatName = statName;
                zombieModeRunState.AttributeModifierRecords.Add(record);
                RegisterZombieModeRunOnlyObject(zombieModeRunState.RunId, ZombieModeRunOnlyObjectKind.Buff, null, player.CharacterItem, RemoveZombieModeAttributeModifiers);
            }
            catch { }
        }

        private void RemoveZombieModeAttributeModifiers()
        {
            for (int i = zombieModeRunState.AttributeModifierRecords.Count - 1; i >= 0; i--)
            {
                ZombieModeAttributeModifierRecord record = zombieModeRunState.AttributeModifierRecords[i];
                if (record == null || record.Modifier == null)
                {
                    continue;
                }

                try
                {
                    Stat stat = record.Stat;
                    if (stat == null && record.CharacterItem != null && !string.IsNullOrEmpty(record.StatName))
                    {
                        stat = record.CharacterItem.GetStat(record.StatName);
                    }

                    if (stat != null)
                    {
                        stat.RemoveModifier(record.Modifier);
                    }
                }
                catch { }
            }

            zombieModeRunState.AttributeModifierRecords.Clear();
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
                renderer.material.color = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                    ? new Color(0.85f, 0.25f, 0.28f, 0.95f)
                    : new Color(0.25f, 0.65f, 0.35f, 0.95f);
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
            record.ServiceState = CreateZombieModeNpcServiceState(serviceType, bossNodeStock);
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
                catch { }
            }
        }

        private void TickZombieModeTemporaryNpcProtection()
        {
            if (!IsZombieModeActive || zombieModeRunState.TemporaryNpcs.Count <= 0)
            {
                return;
            }

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
            catch { }

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

        private ZombieModeNpcServiceState CreateZombieModeNpcServiceState(string serviceType, bool bossNodeStock)
        {
            ZombieModeNpcServiceState state = new ZombieModeNpcServiceState();
            state.BossNodeStock = bossNodeStock;
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

        private void GrantZombieModeRandomMeleeReward(bool bossNode)
        {
            int maxQuality = GetZombieModeRewardMaxQuality(bossNode);
            if (TryGiveRandomItemByTags(new string[] { "MeleeWeapon" }, 1, maxQuality))
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomMeleeWeapon"));
            }
        }

        private void GrantZombieModeRandomGunWithAmmoReward(bool bossNode)
        {
            int maxQuality = GetZombieModeRewardMaxQuality(bossNode);
            int gunTypeId = FindRandomItemTypeByTags(new string[] { "Gun" }, 1, maxQuality);
            if (gunTypeId <= 0)
            {
                return;
            }

            Item gun = ItemAssetsCollection.InstantiateSync(gunTypeId);
            if (gun == null)
            {
                return;
            }

            string caliber = TryReadZombieModeItemCaliber(gun);
            ItemUtilities.SendToPlayer(gun, false, false);
            if (!string.IsNullOrEmpty(caliber))
            {
                TryGiveZombieModeStarterAmmo(caliber, 60);
            }
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomGunWithAmmo"));
        }

        private void GrantZombieModeAmmoSupplyReward()
        {
            string caliber = !string.IsNullOrEmpty(zombieModeRunState.StarterAmmoCaliber)
                ? zombieModeRunState.StarterAmmoCaliber
                : string.Empty;
            if (!string.IsNullOrEmpty(caliber) && TryGiveZombieModeStarterAmmo(caliber, 120))
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_AmmoSupply"));
                return;
            }

            bool granted = TryGiveRandomItemByTags(new string[] { "Ammo" }, 1, 4);
            granted |= TryGiveRandomItemByTags(new string[] { "Bullet" }, 1, 4);
            if (granted)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_AmmoSupply"));
            }
        }

        private void GrantZombieModeMedicalSupplyReward()
        {
            int count = UnityEngine.Random.Range(3, 6);
            int granted = TryGiveRandomItemByTagsTimes(new string[] { "Medic", "Medical", "Healing" }, 1, 4, count);
            if (granted > 0)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_MedicalSupply"));
            }
        }

        private void GrantZombieModeArmorOrHelmetReward(bool bossNode)
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
            }
        }

        private int GetZombieModeRewardMaxQuality(bool bossNode)
        {
            return Mathf.Clamp(zombieModeRunState.PollutionTier + (bossNode ? 2 : 1), 1, ZombieModeTuning.StarterMaxQuality + (bossNode ? 1 : 0));
        }

        private void GrantZombieModeFortificationPack(bool bossNode)
        {
            GrantZombieModeItemRepeated(FoldableCoverPackConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackFoldableCoverBoss : ZombieModeNpcCatalog.RepairPackFoldableCoverNormal);
            GrantZombieModeItemRepeated(ReinforcedRoadblockPackConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackReinforcedRoadblockBoss : ZombieModeNpcCatalog.RepairPackReinforcedRoadblockNormal);
            GrantZombieModeItemRepeated(BarbedWirePackConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackBarbedWireBoss : ZombieModeNpcCatalog.RepairPackBarbedWireNormal);
            GrantZombieModeItemRepeated(EmergencyRepairSprayConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackEmergencyRepairSprayBoss : ZombieModeNpcCatalog.RepairPackEmergencyRepairSprayNormal);

            ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_RepairPackReceived"));
        }

        private void GrantZombieModeItemRepeated(int typeId, int count)
        {
            for (int i = 0; i < Mathf.Max(0, count); i++)
            {
                TryGiveZombieModeItemToPlayerOrDrop(typeId);
            }
        }

        private void ApplyZombieModeContractPollutionDeal(bool bossNode)
        {
            int pollution = bossNode ? 2 : 1;
            int points = bossNode ? 650 : 300;
            zombieModeRunState.PollutionFromContracts += pollution;
            zombieModeRunState.PurificationPoints += points;
            zombieModeRunState.ContractAffixWeights[ZombieModeContractAffixWeightKey] = 1.25f;
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractPollutionDeal"),
                points,
                pollution));
        }

        private void ApplyZombieModeContractGearDeal(bool bossNode)
        {
            zombieModeRunState.PollutionFromContracts += bossNode ? 2 : 1;
            GrantZombieModeRandomGunWithAmmoReward(true);
            GrantZombieModeArmorOrHelmetReward(true);
            zombieModeRunState.ContractAffixWeights[ZombieModeContractAffixWeightKey] = 1.15f;
        }

        private void ApplyZombieModeContractHugePurification()
        {
            zombieModeRunState.PollutionFromContracts += 3;
            int points = 900 + 25 * Mathf.Max(0, zombieModeRunState.TotalPollution);
            zombieModeRunState.PurificationPoints += points;
            zombieModeRunState.ContractAffixWeights[ZombieModeContractAffixWeightKey] = 1.35f;
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractPollutionDeal"),
                points,
                3));
        }

        private void ApplyZombieModeContractInsurance()
        {
            zombieModeRunState.PollutionFromContracts += 2;
            ApplyZombieModeInsuranceReward(0.20f, true);
            zombieModeRunState.ContractAffixWeights[ZombieModeContractAffixWeightKey] = 1.20f;
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

                await TrySpawnZombieModeNormalZombieAsync(runId, GetZombieModeSpawnPosition(), ZombieModeEnemyKind.Elite, true);
                await UniTask.Yield();
            }
        }

        private void CreateZombieModeHighValueAirdrop(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 center = player != null ? player.transform.position : zombieModeRunState.ActiveSafeZoneCenter;
            Vector3 position = center + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(6f, 15f);
            position.y = center.y + 0.4f;

            GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            drop.name = "ZombieMode_HighValueAirdrop";
            drop.transform.position = position;
            drop.transform.localScale = new Vector3(1.4f, 1.0f, 1.4f);
            Renderer renderer = drop.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0.92f, 0.64f, 0.22f, 0.95f);
            }

            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.MapEvent, drop, drop, null);
            TryGiveRandomItemByTags(new string[] { "Gun" }, 4, ZombieModeTuning.StarterMaxQuality + 1);
            TryGiveRandomItemByTags(new string[] { "MeleeWeapon" }, 4, ZombieModeTuning.StarterMaxQuality + 1);
            TryGiveRandomItemByTags(new string[] { "Armor" }, 4, ZombieModeTuning.StarterMaxQuality + 1);
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
                catch { }
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

            return npc.ServiceState.BossNodeStock ? ZombieModeNpcCatalog.BossNodeStock : ZombieModeNpcCatalog.NormalWaveStock;
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
            if (zombieModeRunState.PurificationPoints < cost)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_NpcServiceNoPoints"));
                return false;
            }

            bool granted = false;
            if (entry.TypeId > 0)
            {
                granted = TryGiveZombieModeItemToPlayerOrDrop(entry.TypeId);
            }
            else if (!string.IsNullOrEmpty(entry.GrantTag))
            {
                granted = TryGiveRandomItemByTags(new string[] { entry.GrantTag }, entry.GrantMinQuality, entry.GrantMaxQuality);
            }

            if (!granted)
            {
                return false;
            }

            zombieModeRunState.PurificationPoints -= cost;
            npc.ServiceState.MerchantStockRemaining[stockIndex]--;
            NotificationText.Push(L10n.T(entry.DisplayKey));
            return true;
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
            if (zombieModeRunState.PurificationPoints < cost)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Notify_NpcServiceNoPoints"));
                return false;
            }

            if (!ApplyZombieModeNurseServiceEffect(serviceIndex))
            {
                return false;
            }

            zombieModeRunState.PurificationPoints -= cost;
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

                bool sent = false;
                try { sent = ItemUtilities.SendToPlayerCharacterInventory(item, false); } catch { }
                if (!sent)
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    Vector3 dropPosition = player != null
                        ? player.transform.position + Vector3.up * 0.3f
                        : (zombieModeRunState.ActiveSafeZoneActive ? zombieModeRunState.ActiveSafeZoneCenter + Vector3.up * 0.3f : Vector3.up * 0.3f);
                    item.Drop(dropPosition, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                    sent = true;
                }

                return sent;
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
                catch { }
                return false;
            }
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
        private int runId;
        private ModBehaviour owner;

        public void Initialize(int newRunId, ModBehaviour newOwner)
        {
            runId = newRunId;
            owner = newOwner;
            Build();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject panel = CreateRect("Panel", transform, new Vector2(0.5f, 0.5f), new Vector2(760f, 420f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.86f);
            CreateText("Title", panel.transform, owner.GetZombieModeRewardTitle(runId), 28, new Vector2(0f, 150f), new Vector2(680f, 60f));
            CreateText(
                "Info",
                panel.transform,
                string.Format(
                    L10n.T("BossRush_ZombieMode_Reward_Info"),
                    owner.GetZombieModePurificationPoints(runId),
                    owner.GetZombieModeRewardFreeRefreshes(runId),
                    owner.GetZombieModeRewardPaidRefreshCost(runId).ToString("N0")),
                18,
                new Vector2(0f, 102f),
                new Vector2(700f, 36f));

            IList<ZombieModeRewardType> options = owner.GetZombieModeRewardOptions(runId);
            bool bossNode = owner.IsZombieModeBossRewardNode(runId);
            Vector2[] optionPositions = bossNode
                ? new Vector2[]
                {
                    new Vector2(-180f, 28f),
                    new Vector2(180f, 28f),
                    new Vector2(-180f, -72f),
                    new Vector2(180f, -72f)
                }
                : new Vector2[]
                {
                    new Vector2(-220f, -12f),
                    new Vector2(0f, -12f),
                    new Vector2(220f, -12f)
                };

            for (int i = 0; i < options.Count && i < optionPositions.Length; i++)
            {
                ZombieModeRewardType option = options[i];
                CreateRewardButton(
                    "Reward_" + option.ToString(),
                    panel.transform,
                    owner.GetZombieModeRewardDisplayText(runId, option),
                    optionPositions[i],
                    option);
            }

            CreateRefreshButton(
                "FreeRefresh",
                panel.transform,
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RefreshFree"), owner.GetZombieModeRewardFreeRefreshes(runId)),
                new Vector2(-160f, -160f),
                false,
                owner.GetZombieModeRewardFreeRefreshes(runId) > 0);
            CreateRefreshButton(
                "PaidRefresh",
                panel.transform,
                string.Format(L10n.T("BossRush_ZombieMode_Reward_RefreshPaid"), owner.GetZombieModeRewardPaidRefreshCost(runId).ToString("N0")),
                new Vector2(160f, -160f),
                true,
                true);
        }

        private GameObject CreateRect(string name, Transform parent, Vector2 anchor, Vector2 size)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return obj;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, string text, int fontSize, Vector2 position, Vector2 size)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            return ZombieModeUIHelper.CreateTMPText(
                obj,
                text,
                fontSize,
                TextAlignmentOptions.Center,
                Color.white);
        }

        private void CreateRewardButton(string name, Transform parent, string text, Vector2 position, ZombieModeRewardType rewardType)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(220f, 82f));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            Image image = obj.AddComponent<Image>();
            image.color = new Color(0.18f, 0.28f, 0.34f, 0.95f);
            Button button = obj.AddComponent<Button>();
            CreateText("Text", obj.transform, text, 19, Vector2.zero, new Vector2(206f, 72f));
            button.onClick.AddListener(delegate
            {
                if (owner != null)
                {
                    owner.SelectZombieModeReward(runId, rewardType);
                }
            });
        }

        private void CreateRefreshButton(string name, Transform parent, string text, Vector2 position, bool paid, bool interactable)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(230f, 68f));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            Image image = obj.AddComponent<Image>();
            image.color = interactable
                ? (paid ? new Color(0.34f, 0.26f, 0.14f, 0.95f) : new Color(0.14f, 0.32f, 0.26f, 0.95f))
                : new Color(0.22f, 0.22f, 0.22f, 0.75f);
            Button button = obj.AddComponent<Button>();
            button.interactable = interactable;
            CreateText("Text", obj.transform, text, 18, Vector2.zero, new Vector2(220f, 58f));
            if (!interactable)
            {
                return;
            }

            button.onClick.AddListener(delegate
            {
                if (owner != null)
                {
                    owner.RefreshZombieModeRewardSelection(runId, paid);
                }
            });
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
            catch { }

            try { base.Awake(); } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
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
            catch { }

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
            catch { }

            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.OpenZombieModeTemporaryNpcServiceUi(runId, serviceType);
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
            catch { }
        }
    }

    public sealed class ZombieModeTemporaryNpcServiceView : MonoBehaviour
    {
        private int runId;
        private ModBehaviour owner;
        private string serviceType = string.Empty;

        public void Initialize(int newRunId, ModBehaviour newOwner, string newServiceType)
        {
            runId = newRunId;
            owner = newOwner;
            serviceType = newServiceType ?? string.Empty;
            Build();
        }

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30500;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject panel = CreateRect("Panel", transform, new Vector2(0.5f, 0.5f), new Vector2(760f, 560f));
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.88f);
            string titleKey = string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal)
                ? "BossRush_ZombieMode_Npc_TempNurse"
                : "BossRush_ZombieMode_Npc_TempMerchant";
            CreateText("Title", panel.transform, L10n.T(titleKey), 28, new Vector2(0f, 230f), new Vector2(700f, 48f));

            if (string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal))
            {
                BuildNurseServices(panel.transform);
            }
            else
            {
                BuildMerchantStock(panel.transform);
            }

            CreateCloseButton(panel.transform);
        }

        private void BuildMerchantStock(Transform parent)
        {
            ZombieModeNpcCatalog.MerchantStockEntry[] stock = owner != null
                ? owner.GetZombieModeMerchantStock(runId, serviceType)
                : new ZombieModeNpcCatalog.MerchantStockEntry[0];
            for (int i = 0; i < stock.Length && i < 12; i++)
            {
                ZombieModeNpcCatalog.MerchantStockEntry entry = stock[i];
                int index = i;
                int remaining = owner != null ? owner.GetZombieModeNpcServiceRemaining(runId, serviceType, index) : 0;
                int price = owner != null ? owner.GetZombieModeNpcServicePrice(runId, entry.BasePrice) : entry.BasePrice;
                string label = L10n.T(entry.DisplayKey) +
                    "\n" + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServicePrice"), price) +
                    "  " + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServiceRemaining"), remaining);
                CreateServiceButton(parent, "Merchant_" + i, label, GetGridPosition(i), remaining > 0, delegate
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
                    "\n" + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServicePrice"), price) +
                    "  " + string.Format(L10n.T("BossRush_ZombieMode_Npc_ServiceRemaining"), remaining);
                CreateServiceButton(parent, "Nurse_" + i, label, new Vector2(0f, 150f - i * 72f), remaining > 0, delegate
                {
                    if (owner != null && owner.TryUseZombieModeNurseService(runId, serviceType, index))
                    {
                        Rebuild();
                    }
                });
            }
        }

        private Vector2 GetGridPosition(int index)
        {
            int col = index % 3;
            int row = index / 3;
            return new Vector2(-230f + col * 230f, 145f - row * 92f);
        }

        private void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
            Build();
        }

        private GameObject CreateRect(string name, Transform parent, Vector2 anchor, Vector2 size)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return obj;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, string text, int fontSize, Vector2 position, Vector2 size)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            return ZombieModeUIHelper.CreateTMPText(
                obj,
                text,
                fontSize,
                TextAlignmentOptions.Center,
                Color.white);
        }

        private void CreateServiceButton(Transform parent, string name, string text, Vector2 position, bool interactable, UnityEngine.Events.UnityAction action)
        {
            GameObject obj = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(214f, 74f));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            Image image = obj.AddComponent<Image>();
            image.color = interactable ? new Color(0.18f, 0.30f, 0.34f, 0.95f) : new Color(0.18f, 0.18f, 0.18f, 0.70f);
            Button button = obj.AddComponent<Button>();
            button.interactable = interactable;
            CreateText("Text", obj.transform, text, 15, Vector2.zero, new Vector2(204f, 66f));
            if (interactable && action != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private void CreateCloseButton(Transform parent)
        {
            CreateServiceButton(parent, "Close", L10n.T("BossRush_ZombieMode_Npc_Close"), new Vector2(0f, -240f), true, delegate
            {
                Destroy(gameObject);
            });
        }
    }
}
