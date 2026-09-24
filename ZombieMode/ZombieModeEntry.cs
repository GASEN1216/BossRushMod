using System.Collections;
using System.Collections.Generic;
using Duckov.Utilities;
using Duckov.UI;
using ItemStatsSystem;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const int ZOMBIE_MODE_INITIAL_PURIFICATION_POINTS = 0;

        private static readonly int[] ZombieModeMedicalExcludedTypeIds = { 1243, 1244, 1245, 1246 };
        private static readonly int[] ZombieModeMeleeExcludedTypeIds = { 1, 305, 343, 1095, 1096 };
        private static readonly string[] ZombieModeRewardTagAmmo = { "Ammo" };
        private static readonly string[] ZombieModeRewardTagArmor = { "Armor" };
        private static readonly string[] ZombieModeRewardTagBodyArmor = { "BodyArmor" };
        private static readonly string[] ZombieModeRewardTagBullet = { "Bullet" };
        private static readonly string[] ZombieModeRewardTagDrink = { "Drink" };
        private static readonly string[] ZombieModeRewardTagFood = { "Food" };
        private static readonly string[] ZombieModeRewardTagGun = { "Gun" };
        private static readonly string[] ZombieModeRewardTagHeadset = { "Headset" };
        private static readonly string[] ZombieModeRewardTagHealing = { "Healing" };
        private static readonly string[] ZombieModeRewardTagHelmet = { "Helmet" };
        private static readonly string[] ZombieModeRewardTagMedical = { "Medical" };
        private static readonly string[] ZombieModeRewardTagMedic = { "Medic" };
        private static readonly string[] ZombieModeRewardTagMeleeWeapon = { "MeleeWeapon" };
        private static readonly string[] ZombieModeRewardTagWeapon = { "Weapon" };
        private static readonly string[] ZombieModeRewardTagsMedicMedicalHealing = { "Medic", "Medical", "Healing" };
        private static readonly string[] ZombieModeTagAliasesBodyArmor = { "Armor" };
        private static readonly string[] ZombieModeTagAliasesArmor = { "Armor", "Helmat", "Helmet" };
        private static readonly string[] ZombieModeTagAliasesHelmet = { "Helmat", "Helmet" };

        public bool IsZombieModeActive
        {
            get
            {
                return ZombieModePhaseGuards.IsRunActive(zombieModeRunState.LifecyclePhase);
            }
        }

        public int ZombieModeCurrentRunId
        {
            get { return zombieModeRunState.RunId; }
        }

        public bool IsAnyBossRushLikeModeActive()
        {
            // Mode G 门控（加法分支）：只纳入 IsModeGRunInProgress（lifecycle），
            // 绝不纳入 late sink quarantine，避免永不返回的 late task 长期抑制普通地图公共 NPC。
            return IsActive || IsModeDActive || IsBossRushArenaActive || IsModeEActive || IsModeFActive || IsZombieModeActive || IsModeGRunInProgressSafe();
        }

        public bool UsesArenaSupportNpcPlacement()
        {
            return IsModeEActive || IsModeFActive || IsZombieModeActive;
        }

        public bool ShouldSuppressBaseNpcSpawnForCurrentMode()
        {
            return IsAnyBossRushLikeModeActive();
        }

        private bool IsZombieModeStartupInProgress()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.IsZombieModeStartupInProgress();
            ZombieModeLifecyclePhase phase = zombieModeRunState.LifecyclePhase;
            return pendingZombieModeEntry ||
                   (ZombieModePhaseGuards.IsBeforeActive(phase) &&
                    phase != ZombieModeLifecyclePhase.WaitingStarterChoice);
        }

        public bool CanStartZombieModeMapSelectionPhase1(out string failureReason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.CanStartZombieModeMapSelectionPhase1(out failureReason);
            failureReason = null;
            try
            {
                if (ModeGRuntimeGates.IsModeGEntryBlocked)
                {
                    failureReason = L10n.T("BossRush_ZombieMode_OtherModeActive");
                    return false;
                }
            }
            catch { }
            if (IsZombieModeStartBlocked(out failureReason)) return false;
            return true;
        }

        public bool TryBeginZombieModeMapSelectionPhase1(out string failureReason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.TryBeginZombieModeMapSelectionPhase1(out failureReason);
            failureReason = null;
            return false;
        }

        public void MarkZombieModeMapConfirmedPhase1()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.MarkZombieModeMapConfirmedPhase1();
        }

        public bool IsZombieModeMapLoadReadyPhase1()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.IsZombieModeMapLoadReadyPhase1();
        }

        public void AbortZombieModeMapLoadPhase1(ZombieModeFailureReason reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.AbortZombieModeMapLoadPhase1(reason);
        }

        // 入场预检查（邀请函/其他模式互斥）。随身物品不阻止入场，入图后统一转入仓库。
        private bool TryRunZombieModePrechecks(out ZombieModeFailureReason reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.TryRunZombieModePrechecks(out reason);
            reason = ZombieModeFailureReason.InitializationFailed;
            return false;
        }

        // 资源暂扣：丧尸模式自行提交邀请函与现金，地图选择条目只借用原版 UI 外观。
        private bool CommitZombieModeEntryResourcesShell(out ZombieModeFailureReason reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.CommitZombieModeEntryResourcesShell(out reason);
            reason = ZombieModeFailureReason.InitializationFailed;
            return false;
        }

        /// <summary>
        /// 入场回滚时退还已扣的入场现金。真正的退款与欠账落在 <see cref="ZombieModeEntryDebt.RefundCash"/>；
        /// 它返回 false 表示钱既没退出去、账也没记下，此时**保留事务状态**等下一次清理路径重试
        /// （修复前无条件 `finally` 清状态，玩家已扣的入场费会永久蒸发，CR-2026-09-11-019）。
        /// </summary>
        private void RefundZombieModeCashIfNeeded()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefundZombieModeCashIfNeeded();
        }

        public void CancelZombieModeMapSelectionPhase1()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.CancelZombieModeMapSelectionPhase1();
        }

        private bool ShouldPreserveZombieModeStartupForSceneLoad(Scene scene)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.ShouldPreserveZombieModeStartupForSceneLoad(scene);
        }

        private bool TryHandleZombieModePendingMapSceneLoaded(Scene scene, BossRushMapConfig loadedMapConfig)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.TryHandleZombieModePendingMapSceneLoaded(scene, loadedMapConfig);
        }

        private System.Collections.IEnumerator WaitForZombieModeTargetSceneActiveThenInitialize(Scene scene, Vector3? customPos)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module == null) yield break;
            System.Collections.IEnumerator routine = module.WaitForZombieModeTargetSceneActiveThenInitialize(scene, customPos);
            while (routine.MoveNext()) yield return routine.Current;
        }

        private int BeginZombieModeRunShell(int sceneBuildIndex, string sceneName)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.BeginZombieModeRunShell(sceneBuildIndex, sceneName) : 0;
        }

        private ZombieModeMapProfile BuildZombieModeMapProfile(int sceneBuildIndex, string sceneName)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.BuildZombieModeMapProfile(sceneBuildIndex, sceneName) : new ZombieModeMapProfile();
        }

        private bool IsZombieModeRunValid(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.IsZombieModeRunValid(runId);
        }

        private bool ShouldRollbackZombieModeEntryResources()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null
                ? module.ShouldRollbackZombieModeEntryResources()
                : !zombieModeRunState.EntryResourcesFinalized && !zombieModeEntryTransaction.EntryResourcesFinalized;
        }

        private void TickZombieMode(float deltaTime)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.TickZombieMode(deltaTime);
        }

        internal bool IsZombieModeGamePaused()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) return module.IsZombieModeGamePaused();
            try
            {
                return PauseMenu.Instance != null && PauseMenu.Instance.Shown;
            }
            catch
            {
                return false;
            }
        }

        internal bool IsZombieModeRuntimePaused()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.IsZombieModeRuntimePaused()
                : ZombieModeUIHelper.IsModalInputPaused || IsZombieModeGamePaused() || CameraMode.Active;
        }

        private void RefreshZombieModeRuntimePauseClock()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefreshZombieModeRuntimePauseClock();
        }

        private void ResetZombieModeRuntimePauseClock()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.ResetZombieModeRuntimePauseClock();
        }

        internal float GetZombieModeRuntimeNow()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null ? module.GetZombieModeRuntimeNow() : Time.unscaledTime;
        }

        private bool InitializeZombieModeRunAfterMapLoaded(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.InitializeZombieModeRunAfterMapLoaded(runId);
        }

        private bool GrantZombieModeBeacon(int runId)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.GrantZombieModeBeacon(runId);
        }

        private void FinalizeZombieModeEntryResources()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.FinalizeZombieModeEntryResources();
        }

        /// <summary>
        /// 入场回滚时返还已扣的尸潮邀请函。实例化与欠账落在 <see cref="ZombieModeEntryDebt.RefundInvitation"/>；
        /// 它返回 false 表示实例造不出来、账也没记下，此时保留事务状态等下一次清理路径重试。
        /// </summary>
        private void RefundZombieModeInvitationIfNeeded()
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.RefundZombieModeInvitationIfNeeded();
        }

        private void FailZombieModeBeforeActive(ZombieModeFailureReason reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            if (module != null) module.FailZombieModeBeforeActive(reason);
        }

        private bool ShouldReturnToBaseAfterZombieModePreActiveFailure(ZombieModeFailureReason reason)
        {
            ZombieModeRuntimeModule module = zombieModeRuntimeModule;
            return module != null && module.ShouldReturnToBaseAfterZombieModePreActiveFailure(reason);
        }

        private int FindRandomItemTypeByTags(string[] requiredTags, int minQuality, int maxQuality)
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

        private int[] GetZombieModeRewardCandidateIds(string[] requiredTags, int minQuality, int maxQuality)
        {
            if (ItemAssetsCollection.Instance == null)
            {
                return new int[0];
            }

            string cacheKey = BuildZombieModeRewardCandidateCacheKey(requiredTags, minQuality, maxQuality);
            int[] cached;
            if (zombieModeRewardCandidateCache.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            zombieModeRewardSafeCandidateScratch.Clear();
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
                    zombieModeRewardCandidateCache[cacheKey] = cached;
                    return cached;
                }
            }

            cached = zombieModeRewardSafeCandidateScratch.ToArray();
            zombieModeRewardSafeCandidateScratch.Clear();
            zombieModeRewardCandidateCache[cacheKey] = cached;
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
                    !zombieModeRewardSafeCandidateScratch.Contains(candidates[i]))
                {
                    zombieModeRewardSafeCandidateScratch.Add(candidates[i]);
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
                DevLog("[ZombieMode] Tag.AllTags 扫描失败: " + e.Message);
            }

        }

        private Tag FindZombieModeTagByName(string tagName)
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
                DevLog("[ZombieMode] Tag.AllTags 扫描失败: " + e.Message);
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

        private bool IsZombieModeRewardCandidateAllowed(int typeId, string[] requiredTags)
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
            if (!DevModeEnabled || !zombieModeOpaqueFilterLogIds.Add(typeId))
            {
                return;
            }

            try
            {
                ItemMetaData metaData = ItemAssetsCollection.GetMetaData(typeId);
                string tags = metaData.tags == null
                    ? "<null>"
                    : string.Join(",", System.Array.ConvertAll(metaData.tags, tag => tag == null ? "<null>" : tag.name));
                DevLog("[ZombieMode] opaque filter candidate id=" + typeId +
                    " name=" + metaData.Name +
                    " displayNameKey=" + metaData.DisplayNameKey +
                    " tags=" + tags);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] opaque filter candidate id=" + typeId + " metadata log failed: " + e.Message);
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
            if (!DevModeEnabled || !zombieModeOpaqueFilterLogIds.Add(typeId))
            {
                return;
            }

            DevLog("[ZombieMode] medical candidate fail-closed id=" + typeId + " reason=" + reason);
        }
    }

}
