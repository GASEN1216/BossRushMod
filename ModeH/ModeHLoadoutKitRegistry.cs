using System;
using System.Collections.Generic;
using ItemStatsSystem;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// 一件虚拟整备套装的运行时解析结果（§17.7）。
    /// 未解析成功的 kit 不可用，但不会退化为玩家真实装备。
    /// </summary>
    public sealed class ModeHResolvedKit
    {
        /// <summary>静态定义。</summary>
        public ModeHKitSpec Spec;
        /// <summary>实际使用的官方 typeId。</summary>
        public int ResolvedTypeId;
        /// <summary>实际读到的原版 ItemMetaData.quality（1..8）。</summary>
        public int ResolvedQuality;
        /// <summary>是否可用。</summary>
        public bool Available;
        /// <summary>不可用原因（fail-closed 展示用）。</summary>
        public string FailureReason;
    }

    /// <summary>
    /// Mode H 虚拟整备套装注册表（设计提案 §17.7、§25.1）。
    ///
    /// 边界：
    /// - 只使用 LoadoutKits.json 的审计套装，不枚举、不读取、不复制玩家背包或仓库；
    /// - typeId 为 0 时按 resolveTags + 品质区间在官方 ItemAssetsCollection 中确定性解析
    ///   （候选按 typeId 升序后取 resolveOrdinal），解析结果被固定并审计；
    /// - 每名选手最多 4 个互不冲突槽位的 kit；
    /// - 新赛季默认解锁全部 starter kit，且每个生产原型至少两件槽位不冲突的 starter kit。
    ///
    /// 本类不访问 CharacterMainControl.Main、PlayerStorage 或玩家 ItemTreeData。
    /// </summary>
    public static class ModeHLoadoutKitRegistry
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _staticValidated;
        private static bool _staticAttempted;
        private static string _lastError;
        private static Dictionary<string, ModeHResolvedKit> _kits;
        private static List<string> _starterKitIds;
        private static bool _runtimeResolved;

        #endregion

        #region 只读访问

        /// <summary>静态审计是否通过。</summary>
        public static bool IsValidated { get { return _staticValidated; } }

        /// <summary>运行时 typeId 解析是否已执行。</summary>
        public static bool IsRuntimeResolved { get { return _runtimeResolved; } }

        /// <summary>最后一次失败原因。</summary>
        public static string LastError { get { return _lastError; } }

        #endregion

        #region 生命周期

        /// <summary>清空缓存（含运行时解析结果）。</summary>
        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _staticValidated = false;
                _staticAttempted = false;
                _runtimeResolved = false;
                _lastError = null;
                _kits = null;
                _starterKitIds = null;
            }
        }

        /// <summary>幂等静态审计：schema、槽位、品质、弹药、starter 覆盖。</summary>
        public static bool EnsureValidated()
        {
            lock (_lock)
            {
                if (_staticValidated) return true;
                if (_staticAttempted) return false;
                _staticAttempted = true;
            }

            try
            {
                return ValidateInternal();
            }
            catch (Exception e)
            {
                _lastError = "kit_registry_exception:" + e.GetType().Name;
                return false;
            }
        }

        private static bool ValidateInternal()
        {
            if (!ModeHContentCatalog.EnsureLoaded())
            {
                _lastError = ModeHContentCatalog.LastError;
                return false;
            }

            List<ModeHKitSpec> specs = ModeHContentCatalog.Kits;
            if (specs == null || specs.Count == 0)
            {
                _lastError = "kit_catalog_empty";
                return false;
            }

            Dictionary<string, ModeHResolvedKit> kits =
                new Dictionary<string, ModeHResolvedKit>(StringComparer.Ordinal);
            List<ModeHKitSpec> starters = new List<ModeHKitSpec>();

            for (int i = 0; i < specs.Count; i++)
            {
                ModeHKitSpec spec = specs[i];
                ModeHResolvedKit kit = new ModeHResolvedKit();
                kit.Spec = spec;
                kit.ResolvedTypeId = spec.TypeId;
                kit.ResolvedQuality = spec.GameQuality;
                kit.Available = spec.TypeId > 0; // 未固定 typeId 的 kit 需要运行时解析
                kit.FailureReason = kit.Available ? null : "pending_runtime_resolve";
                kits[spec.KitId] = kit;
                if (spec.IsStarterKit) starters.Add(spec);
            }

            if (starters.Count == 0)
            {
                _lastError = "kit_starter_set_empty";
                return false;
            }

            // 每个生产原型至少两件槽位不冲突的 starter kit
            for (int i = 0; i < ModeHStableIds.AllArchetypes.Length; i++)
            {
                string archetype = ModeHStableIds.AllArchetypes[i];
                HashSet<string> slots = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < starters.Count; j++)
                {
                    ModeHKitSpec spec = starters[j];
                    if (!IsArchetypeCompatible(spec, archetype)) continue;
                    slots.Add(spec.ReplaceSlot);
                }
                if (slots.Count < ModeHConfig.MinStarterKitsPerArchetype)
                {
                    _lastError = "kit_starter_coverage_thin:" + archetype;
                    return false;
                }
            }

            starters.Sort(delegate (ModeHKitSpec a, ModeHKitSpec b)
            {
                return a.StarterOrder.CompareTo(b.StarterOrder);
            });
            List<string> starterIds = new List<string>(starters.Count);
            for (int i = 0; i < starters.Count; i++)
            {
                starterIds.Add(starters[i].KitId);
            }

            _kits = kits;
            _starterKitIds = starterIds;
            _staticValidated = true;
            _lastError = null;
            return true;
        }

        #endregion

        #region 运行时解析（官方 ItemAssetsCollection）

        /// <summary>
        /// 把 typeId 为 0 的 kit 解析成确定性官方 typeId：
        /// 按 resolveTags + 品质区间搜索候选，按 typeId 升序取 resolveOrdinal，再核对实际品质。
        /// 解析失败只让该 kit 不可用，不抛出、不回退到玩家真实物品。
        /// </summary>
        public static bool TryResolveRuntimeTypeIds(out string error)
        {
            error = null;
            if (!EnsureValidated())
            {
                error = _lastError;
                return false;
            }
            if (_runtimeResolved) return true;

            try
            {
                foreach (KeyValuePair<string, ModeHResolvedKit> pair in _kits)
                {
                    ResolveOne(pair.Value);
                }
                _runtimeResolved = true;
                return true;
            }
            catch (Exception e)
            {
                error = "kit_resolve_exception:" + e.GetType().Name;
                return false;
            }
        }

        private static void ResolveOne(ModeHResolvedKit kit)
        {
            if (kit == null || kit.Spec == null) return;
            ModeHKitSpec spec = kit.Spec;

            if (spec.TypeId > 0)
            {
                int quality;
                if (!TryReadQuality(spec.TypeId, out quality))
                {
                    kit.Available = false;
                    kit.FailureReason = "pinned_type_missing";
                    return;
                }
                kit.ResolvedTypeId = spec.TypeId;
                kit.ResolvedQuality = quality;
                kit.Available = true;
                kit.FailureReason = null;
                return;
            }

            int[] candidates = SearchCandidates(spec);
            if (candidates == null || candidates.Length == 0)
            {
                kit.Available = false;
                kit.FailureReason = "resolve_no_candidate";
                return;
            }

            List<int> sorted = new List<int>(candidates.Length);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] > 0) sorted.Add(candidates[i]);
            }
            sorted.Sort();
            if (sorted.Count == 0)
            {
                kit.Available = false;
                kit.FailureReason = "resolve_no_candidate";
                return;
            }

            int ordinal = spec.ResolveOrdinal;
            if (ordinal >= sorted.Count) ordinal = sorted.Count - 1;
            int typeId = sorted[ordinal];

            int resolvedQuality;
            if (!TryReadQuality(typeId, out resolvedQuality))
            {
                kit.Available = false;
                kit.FailureReason = "resolve_metadata_missing";
                return;
            }
            if (resolvedQuality < spec.ResolveMinQuality || resolvedQuality > spec.ResolveMaxQuality)
            {
                kit.Available = false;
                kit.FailureReason = "resolve_quality_out_of_range";
                return;
            }

            kit.ResolvedTypeId = typeId;
            kit.ResolvedQuality = resolvedQuality;
            kit.Available = true;
            kit.FailureReason = null;
        }

        private static int[] SearchCandidates(ModeHKitSpec spec)
        {
            try
            {
                if (ItemAssetsCollection.Instance == null) return null;
                Tag[] tags = ResolveTags(spec.ResolveTags);
                if (tags == null || tags.Length == 0) return null;

                ItemFilter filter = new ItemFilter();
                filter.requireTags = tags;
                filter.minQuality = spec.ResolveMinQuality;
                filter.maxQuality = spec.ResolveMaxQuality;
                filter.caliber = string.Empty;
                return ItemAssetsCollection.Search(filter);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Tag[] ResolveTags(List<string> tagNames)
        {
            if (tagNames == null || tagNames.Count == 0) return null;
            try
            {
                if (GameplayDataSettings.Tags == null || GameplayDataSettings.Tags.AllTags == null) return null;
                List<Tag> tags = new List<Tag>();
                for (int i = 0; i < tagNames.Count; i++)
                {
                    string wanted = tagNames[i];
                    foreach (Tag tag in GameplayDataSettings.Tags.AllTags)
                    {
                        if (tag != null && string.Equals(tag.name, wanted, StringComparison.Ordinal))
                        {
                            if (!tags.Contains(tag)) tags.Add(tag);
                            break;
                        }
                    }
                }
                return tags.ToArray();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool TryReadQuality(int typeId, out int quality)
        {
            quality = 0;
            try
            {
                ItemMetaData metaData = ItemAssetsCollection.GetMetaData(typeId);
                if (metaData.id <= 0) return false;
                quality = metaData.quality;
                return quality >= ModeHConfig.MinGameQuality && quality <= ModeHConfig.MaxGameQuality;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 查询

        /// <summary>取一件 kit 的解析结果。</summary>
        public static ModeHResolvedKit GetKit(string kitId)
        {
            if (_kits == null || string.IsNullOrEmpty(kitId)) return null;
            ModeHResolvedKit kit;
            return _kits.TryGetValue(kitId, out kit) ? kit : null;
        }

        /// <summary>新赛季确定性初始解锁集合：全部 starter kit（按 starterOrder 升序）。</summary>
        public static List<string> GetStarterKitIds()
        {
            return _starterKitIds != null ? new List<string>(_starterKitIds) : new List<string>();
        }

        /// <summary>该 kit 是否与指定原型兼容。</summary>
        public static bool IsArchetypeCompatible(ModeHKitSpec spec, string archetypeId)
        {
            if (spec == null || string.IsNullOrEmpty(archetypeId)) return false;
            if (spec.CompatibleArchetypeIds == null || spec.CompatibleArchetypeIds.Count == 0) return true;
            for (int i = 0; i < spec.CompatibleArchetypeIds.Count; i++)
            {
                if (string.Equals(spec.CompatibleArchetypeIds[i], archetypeId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>该 kit 是否与指定 profile 模板兼容。</summary>
        public static bool IsProfileCompatible(ModeHKitSpec spec, string profileTemplateId)
        {
            if (spec == null) return false;
            if (spec.CompatibleProfileIds == null || spec.CompatibleProfileIds.Count == 0) return true;
            if (string.IsNullOrEmpty(profileTemplateId)) return false;
            for (int i = 0; i < spec.CompatibleProfileIds.Count; i++)
            {
                if (string.Equals(spec.CompatibleProfileIds[i], profileTemplateId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 取当前可选的 kit：必须已解锁、可用、与原型/模板兼容。
        /// 结果按 kitId ordinal 升序，保证 UI 与抽取顺序确定。
        /// </summary>
        public static List<ModeHResolvedKit> GetSelectableKits(
            ICollection<string> unlockedKitIds, string archetypeId, string profileTemplateId)
        {
            List<ModeHResolvedKit> result = new List<ModeHResolvedKit>();
            if (_kits == null) return result;
            List<string> ids = new List<string>(_kits.Keys);
            ids.Sort(StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                ModeHResolvedKit kit = _kits[ids[i]];
                if (kit == null || !kit.Available || kit.Spec == null) continue;
                if (unlockedKitIds != null && !unlockedKitIds.Contains(kit.Spec.KitId)) continue;
                if (!IsArchetypeCompatible(kit.Spec, archetypeId)) continue;
                if (!IsProfileCompatible(kit.Spec, profileTemplateId)) continue;
                result.Add(kit);
            }
            return result;
        }

        /// <summary>
        /// 取奖励候选来源：非 starter、尚未解锁、可用且与目标 profile 兼容，按 kitId ordinal 升序。
        /// </summary>
        public static List<string> GetRewardCandidatePool(
            ICollection<string> unlockedKitIds, string archetypeId, string profileTemplateId)
        {
            List<string> result = new List<string>();
            if (_kits == null) return result;
            List<string> ids = new List<string>(_kits.Keys);
            ids.Sort(StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                ModeHResolvedKit kit = _kits[ids[i]];
                if (kit == null || kit.Spec == null || !kit.Available) continue;
                if (kit.Spec.IsStarterKit) continue;
                if (unlockedKitIds != null && unlockedKitIds.Contains(kit.Spec.KitId)) continue;
                if (!IsArchetypeCompatible(kit.Spec, archetypeId)) continue;
                if (!IsProfileCompatible(kit.Spec, profileTemplateId)) continue;
                result.Add(kit.Spec.KitId);
            }
            return result;
        }

        /// <summary>
        /// 校验一组 kit 选择：数量上限、槽位互不冲突、全部可用且兼容。
        /// </summary>
        public static bool ValidateSelection(
            IList<string> kitIds,
            ICollection<string> unlockedKitIds,
            string archetypeId,
            string profileTemplateId,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (kitIds == null || kitIds.Count == 0) return true; // 空整备合法（保留预设审计基线）
            if (kitIds.Count > ModeHConfig.MaxKitsPerFighter)
            {
                failureReasonId = "kit_count_exceeded";
                return false;
            }
            HashSet<string> slots = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < kitIds.Count; i++)
            {
                string kitId = kitIds[i];
                if (!seen.Add(kitId))
                {
                    failureReasonId = "kit_duplicate";
                    return false;
                }
                ModeHResolvedKit kit = GetKit(kitId);
                if (kit == null || kit.Spec == null)
                {
                    failureReasonId = "kit_unknown";
                    return false;
                }
                if (!kit.Available)
                {
                    failureReasonId = "kit_unavailable";
                    return false;
                }
                if (unlockedKitIds != null && !unlockedKitIds.Contains(kitId))
                {
                    failureReasonId = "kit_locked";
                    return false;
                }
                if (!IsArchetypeCompatible(kit.Spec, archetypeId) || !IsProfileCompatible(kit.Spec, profileTemplateId))
                {
                    failureReasonId = "kit_incompatible";
                    return false;
                }
                if (!slots.Add(kit.Spec.ReplaceSlot))
                {
                    failureReasonId = "kit_slot_conflict";
                    return false;
                }
            }
            return true;
        }

        #endregion
    }
}
