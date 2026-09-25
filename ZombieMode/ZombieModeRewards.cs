using Duckov.Utilities;
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
    internal sealed partial class ZombieModeRuntimeModule
    {
        private const string ZombieModeAttributeMaxHealthKey = "MaxHealth";
        private const string ZombieModeAttributeMoveSpeedKey = "MoveSpeed";
        private const string ZombieModeAttributeWalkSpeedKey = "WalkSpeed";
        private const string ZombieModeAttributeRunSpeedKey = "RunSpeed";
        private const string ZombieModeAttributeMeleeDamageKey = "MeleeDamageMultiplier";
        private const string ZombieModeAttributeRangedDamageKey = "GunDamageMultiplier";
        // 官方 stat 名是 ReloadSpeedGain（CharacterMainControl.reloadSpeedGainHash）；
        // 曾误写成 "ReloadSpeedMultiplier"，该 stat 不存在，奖励被静默丢弃。
        // AttributeBonuses 是纯运行时字典、不落盘，改 key 不影响存档。
        private const string ZombieModeAttributeReloadSpeedKey = "ReloadSpeedGain";
        private const string ZombieModeAttributeDamageReductionKey = "ElementFactor_Physics";
        private const int ZombieModeContractGearDealMinQuality = 5;

        private GameObject zombieModeRewardUiRoot;

        internal static readonly int[] ZombieModeMedicalExcludedTypeIds = { 1243, 1244, 1245, 1246 };
        internal static readonly int[] ZombieModeMeleeExcludedTypeIds = { 1, 305, 343, 1095, 1096 };
        internal static readonly string[] ZombieModeRewardTagAmmo = { "Ammo" };
        internal static readonly string[] ZombieModeRewardTagArmor = { "Armor" };
        internal static readonly string[] ZombieModeRewardTagBodyArmor = { "BodyArmor" };
        internal static readonly string[] ZombieModeRewardTagBullet = { "Bullet" };
        internal static readonly string[] ZombieModeRewardTagDrink = { "Drink" };
        internal static readonly string[] ZombieModeRewardTagFood = { "Food" };
        internal static readonly string[] ZombieModeRewardTagGun = { "Gun" };
        internal static readonly string[] ZombieModeRewardTagHeadset = { "Headset" };
        internal static readonly string[] ZombieModeRewardTagHealing = { "Healing" };
        internal static readonly string[] ZombieModeRewardTagHelmet = { "Helmet" };
        internal static readonly string[] ZombieModeRewardTagMedical = { "Medical" };
        internal static readonly string[] ZombieModeRewardTagMedic = { "Medic" };
        internal static readonly string[] ZombieModeRewardTagMeleeWeapon = { "MeleeWeapon" };
        internal static readonly string[] ZombieModeRewardTagWeapon = { "Weapon" };
        internal static readonly string[] ZombieModeRewardTagsMedicMedicalHealing = { "Medic", "Medical", "Healing" };
        internal static readonly string[] ZombieModeTagAliasesBodyArmor = { "Armor" };
        internal static readonly string[] ZombieModeTagAliasesArmor = { "Armor", "Helmat", "Helmet" };
        internal static readonly string[] ZombieModeTagAliasesHelmet = { "Helmat", "Helmet" };

        internal int FindRandomItemTypeByTags(string[] requiredTags, int minQuality, int maxQuality)
        {
            try
            {
                int[] candidates = GetZombieModeRewardCandidateIds(requiredTags, minQuality, maxQuality);
                // 过滤后的候选可能为空，但原始 Search 结果曾经非空；只在同一标签上下文内逐级降低品质，
                // 不退化成无标签搜索，也不把被排除物品重新放回池中。
                if ((candidates == null || candidates.Length <= 0) &&
                    (HasZombieModeRequestedTag(requiredTags, "Medic") ||
                     HasZombieModeRequestedTag(requiredTags, "Medical") ||
                     HasZombieModeRequestedTag(requiredTags, "Healing") ||
                     HasZombieModeRequestedTag(requiredTags, "MeleeWeapon")))
                {
                    for (int fallbackQuality = minQuality - 1; fallbackQuality >= 0; fallbackQuality--)
                    {
                        candidates = GetZombieModeRewardCandidateIds(requiredTags, fallbackQuality, fallbackQuality);
                        if (candidates != null && candidates.Length > 0)
                        {
                            break;
                        }
                    }
                }
                if (candidates == null || candidates.Length <= 0)
                {
                    return -1;
                }

                return candidates[UnityEngine.Random.Range(0, candidates.Length)];
            }
            catch
            {
                return -1;
            }
        }

        private int PickZombieModeStrictQualityCandidate(int[] candidates, int minQuality, int maxQuality)
        {
            if (candidates == null || candidates.Length <= 0)
            {
                return -1;
            }

            minQuality = Mathf.Max(0, minQuality);
            maxQuality = Mathf.Max(minQuality, maxQuality);
            int eligibleCount = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (IsZombieModeItemQualityInRange(candidates[i], minQuality, maxQuality))
                {
                    eligibleCount++;
                }
            }

            if (eligibleCount <= 0)
            {
                return -1;
            }

            int target = UnityEngine.Random.Range(0, eligibleCount);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!IsZombieModeItemQualityInRange(candidates[i], minQuality, maxQuality))
                {
                    continue;
                }

                if (target <= 0)
                {
                    return candidates[i];
                }

                target--;
            }

            return -1;
        }

        private bool IsZombieModeItemQualityInRange(int typeId, int minQuality, int maxQuality)
        {
            try
            {
                ItemMetaData metaData = ItemAssetsCollection.GetMetaData(typeId);
                return metaData.id > 0 && metaData.quality >= minQuality && metaData.quality <= maxQuality;
            }
            catch
            {
                return false;
            }
        }

        internal int[] GetZombieModeRewardCandidateIds(string[] requiredTags, int minQuality, int maxQuality)
        {
            if (ItemAssetsCollection.Instance == null)
            {
                return new int[0];
            }

            string cacheKey = BuildZombieModeRewardCandidateCacheKey(requiredTags, minQuality, maxQuality);
            int[] cached;
            if (rewardCandidateCache.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            rewardCandidateScratch.Clear();
            EnsureZombieModeOpaqueFilterDiagnosticsLogged(requiredTags);

            if (requiredTags == null || requiredTags.Length <= 0)
            {
                ItemFilter filter = new ItemFilter();
                filter.requireTags = null;
                filter.minQuality = minQuality;
                filter.maxQuality = maxQuality;
                filter.caliber = string.Empty;
                int[] candidates = ItemAssetsCollection.Search(filter);
                AddZombieModeRewardCandidates(candidates, requiredTags);
            }
            else
            {
                bool searchedAnyTag = false;
                for (int i = 0; i < requiredTags.Length; i++)
                {
                    Tag[] tags = ResolveZombieModeTags(requiredTags[i]);
                    if (tags == null || tags.Length <= 0)
                    {
                        continue;
                    }

                    for (int tagIndex = 0; tagIndex < tags.Length; tagIndex++)
                    {
                        Tag tag = tags[tagIndex];
                        if (tag == null)
                        {
                            continue;
                        }

                        searchedAnyTag = true;
                        ItemFilter filter = new ItemFilter();
                        filter.requireTags = new Tag[] { tag };
                        filter.minQuality = minQuality;
                        filter.maxQuality = maxQuality;
                        filter.caliber = string.Empty;
                        int[] candidates = ItemAssetsCollection.Search(filter);
                        AddZombieModeRewardCandidates(candidates, requiredTags);
                    }
                }

                if (!searchedAnyTag)
                {
                    cached = new int[0];
                    rewardCandidateCache[cacheKey] = cached;
                    return cached;
                }
            }

            cached = rewardCandidateScratch.ToArray();
            rewardCandidateScratch.Clear();
            rewardCandidateCache[cacheKey] = cached;
            return cached;
        }

        private void AddZombieModeRewardCandidates(int[] candidates, string[] requiredTags)
        {
            if (candidates == null || candidates.Length <= 0)
            {
                return;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (IsZombieModeRewardCandidateAllowed(candidates[i], requiredTags) &&
                    !rewardCandidateScratch.Contains(candidates[i]))
                {
                    rewardCandidateScratch.Add(candidates[i]);
                }
            }
        }

        private string BuildZombieModeRewardCandidateCacheKey(string[] requiredTags, int minQuality, int maxQuality)
        {
            string tagsKey = "*";
            if (requiredTags != null && requiredTags.Length > 0)
            {
                tagsKey = string.Join("|", requiredTags);
            }

            return tagsKey + "#" + minQuality.ToString() + "#" + maxQuality.ToString();
        }

        private Tag[] ResolveZombieModeTags(string[] tagNames)
        {
            if (tagNames == null || tagNames.Length == 0)
            {
                return null;
            }

            List<Tag> tags = new List<Tag>();
            for (int i = 0; i < tagNames.Length; i++)
            {
                AddZombieModeTagsByName(tags, tagNames[i]);
            }

            return tags.ToArray();
        }

        private Tag[] ResolveZombieModeTags(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
            {
                return null;
            }

            List<Tag> tags = new List<Tag>();
            AddZombieModeTagsByName(tags, tagName);
            return tags.ToArray();
        }

        private void AddZombieModeTagsByName(List<Tag> tags, string tagName)
        {
            try
            {
                if (tags == null)
                {
                    return;
                }

                if (GameplayDataSettings.Tags == null || GameplayDataSettings.Tags.AllTags == null)
                {
                    return;
                }

                string[] aliases = GetZombieModeTagAliases(tagName);
                for (int i = 0; i < aliases.Length; i++)
                {
                    string alias = aliases[i];
                    foreach (Tag tag in GameplayDataSettings.Tags.AllTags)
                    {
                        if (tag != null && tag.name == alias)
                        {
                            if (!tags.Contains(tag))
                            {
                                tags.Add(tag);
                            }
                            break;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] Tag.AllTags 扫描失败: " + e.Message);
            }

        }

        internal Tag FindZombieModeTagByName(string tagName)
        {
            try
            {
                if (GameplayDataSettings.Tags == null || GameplayDataSettings.Tags.AllTags == null)
                {
                    return null;
                }

                string[] aliases = GetZombieModeTagAliases(tagName);
                for (int i = 0; i < aliases.Length; i++)
                {
                    string alias = aliases[i];
                    foreach (Tag tag in GameplayDataSettings.Tags.AllTags)
                    {
                        if (tag != null && tag.name == alias)
                        {
                            return tag;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] Tag.AllTags 扫描失败: " + e.Message);
            }

            return null;
        }

        private static string[] GetZombieModeTagAliases(string tagName)
        {
            if (string.Equals(tagName, "BodyArmor", System.StringComparison.Ordinal))
            {
                return ZombieModeTagAliasesBodyArmor;
            }

            if (string.Equals(tagName, "Armor", System.StringComparison.Ordinal))
            {
                return ZombieModeTagAliasesArmor;
            }

            if (string.Equals(tagName, "Helmet", System.StringComparison.Ordinal))
            {
                return ZombieModeTagAliasesHelmet;
            }

            return new string[] { tagName };
        }

        internal bool IsZombieModeRewardCandidateAllowed(int typeId, string[] requiredTags)
        {
            if (typeId <= 0 ||
                typeId == BossRushItemIds.ZombieTideInvitation ||
                typeId == BossRushItemIds.ZombieTideBeacon)
            {
                return false;
            }

            bool medicalContext = HasZombieModeRequestedTag(requiredTags, "Medic") ||
                                  HasZombieModeRequestedTag(requiredTags, "Medical") ||
                                  HasZombieModeRequestedTag(requiredTags, "Healing");
            bool meleeContext = HasZombieModeRequestedTag(requiredTags, "MeleeWeapon");
            if (!medicalContext && !meleeContext)
            {
                return true;
            }

            if (medicalContext && ContainsZombieModeTypeId(ZombieModeMedicalExcludedTypeIds, typeId))
            {
                LogZombieModeOpaqueFilterCandidate(typeId);
                return false;
            }

            if (meleeContext && ContainsZombieModeTypeId(ZombieModeMeleeExcludedTypeIds, typeId))
            {
                LogZombieModeOpaqueFilterCandidate(typeId);
                return false;
            }

            try
            {
                ItemMetaData metaData = ItemAssetsCollection.GetMetaData(typeId);
                if (medicalContext)
                {
                    if (metaData.id <= 0 || metaData.tags == null)
                    {
                        DevLogOnceZombieModeOpaqueFilterFailure(typeId, "metadata/tags unavailable");
                        return false;
                    }

                    Tag advancedDebuffTag = GameplayDataSettings.Tags != null
                        ? GameplayDataSettings.Tags.AdvancedDebuffMode
                        : null;
                    for (int i = 0; i < metaData.tags.Length; i++)
                    {
                        Tag tag = metaData.tags[i];
                        if (tag == null)
                        {
                            DevLogOnceZombieModeOpaqueFilterFailure(typeId, "null tag");
                            return false;
                        }

                        if ((advancedDebuffTag != null && tag == advancedDebuffTag) ||
                            string.Equals(tag.name, "AdvancedDebuffMode", System.StringComparison.Ordinal))
                        {
                            LogZombieModeOpaqueFilterCandidate(typeId);
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (System.Exception e)
            {
                if (medicalContext)
                {
                    DevLogOnceZombieModeOpaqueFilterFailure(typeId, e.Message);
                    return false;
                }
                return true;
            }
        }

        private static bool HasZombieModeRequestedTag(string[] requiredTags, string tagName)
        {
            if (requiredTags == null || string.IsNullOrEmpty(tagName))
            {
                return false;
            }

            for (int i = 0; i < requiredTags.Length; i++)
            {
                if (string.Equals(requiredTags[i], tagName, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsZombieModeTypeId(int[] values, int typeId)
        {
            if (values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == typeId)
                {
                    return true;
                }
            }
            return false;
        }

        private void LogZombieModeOpaqueFilterCandidate(int typeId)
        {
            if (!ModBehaviour.DevModeEnabled || !opaqueFilterLogIds.Add(typeId))
            {
                return;
            }

            try
            {
                ItemMetaData metaData = ItemAssetsCollection.GetMetaData(typeId);
                string tags = metaData.tags == null
                    ? "<null>"
                    : string.Join(",", System.Array.ConvertAll(metaData.tags, tag => tag == null ? "<null>" : tag.name));
                ModBehaviour.DevLog("[ZombieMode] opaque filter candidate id=" + typeId +
                    " name=" + metaData.Name +
                    " displayNameKey=" + metaData.DisplayNameKey +
                    " tags=" + tags);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] opaque filter candidate id=" + typeId + " metadata log failed: " + e.Message);
            }
        }

        private void EnsureZombieModeOpaqueFilterDiagnosticsLogged(string[] requiredTags)
        {
            bool medicalContext = HasZombieModeRequestedTag(requiredTags, "Medic") ||
                                  HasZombieModeRequestedTag(requiredTags, "Medical") ||
                                  HasZombieModeRequestedTag(requiredTags, "Healing");
            bool meleeContext = HasZombieModeRequestedTag(requiredTags, "MeleeWeapon");
            if (!medicalContext && !meleeContext)
            {
                return;
            }

            if (medicalContext)
            {
                for (int i = 0; i < ZombieModeMedicalExcludedTypeIds.Length; i++)
                {
                    LogZombieModeOpaqueFilterCandidate(ZombieModeMedicalExcludedTypeIds[i]);
                }
            }
            if (meleeContext)
            {
                for (int i = 0; i < ZombieModeMeleeExcludedTypeIds.Length; i++)
                {
                    LogZombieModeOpaqueFilterCandidate(ZombieModeMeleeExcludedTypeIds[i]);
                }
            }
        }

        private void DevLogOnceZombieModeOpaqueFilterFailure(int typeId, string reason)
        {
            if (!ModBehaviour.DevModeEnabled || !opaqueFilterLogIds.Add(typeId))
            {
                return;
            }

            ModBehaviour.DevLog("[ZombieMode] medical candidate fail-closed id=" + typeId + " reason=" + reason);
        }

        internal void ShowZombieModeRewardSelectionPresentationForRuntimeModule(int runId, bool restEditorExpanded)
        {
            ZombieModeRewardSelectionView current = zombieModeRewardUiRoot != null
                ? zombieModeRewardUiRoot.GetComponent<ZombieModeRewardSelectionView>()
                : null;
            if (current != null && current.TryRebuild(runId, restEditorExpanded)) return;
            ClearZombieModeRewardShell();
            zombieModeRewardUiRoot = new GameObject("ZombieMode_RewardSelection");
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.RewardUi, zombieModeRewardUiRoot, zombieModeRewardUiRoot, null);
            ZombieModeRewardSelectionView view = zombieModeRewardUiRoot.AddComponent<ZombieModeRewardSelectionView>();
            view.Initialize(runId, owner, restEditorExpanded);
        }
    }
    // 奖励选择面板在 ZombieModeRewardSelectionView.cs，补给 / 医疗终端的交互体与服务面板在
    // ZombieModeTemporaryNpcServiceView.cs（2026-09-23 审美审查时从宿主 partial 拆出：partial 行数预算没有余量，
    // 视图与交互体也不是宿主职责，AGENTS §4.15）。
}
