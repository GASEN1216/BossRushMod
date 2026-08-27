using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H 生产池注册表（设计提案 §17.2、§25.1）。
    ///
    /// 冻结契约：
    /// - 先加载静态候选审计（ModeHProfileRegistry + BossProfiles.json），
    ///   随后**只**把当前 runtime 生产认证中状态为 Passed 的 stable key 物化为本次生产池；
    /// - 未通过、未认证或签名变化的 key 一律不得进入生产池；
    /// - 生产池随 (game, mod, content) 三签名绑定，签名变化即失效；
    /// - EnemyPresetInfo 字段有限，不能单独作为资格判断，实际 preset 由认证阶段回查。
    /// </summary>
    public static class ModeHPresetRegistry
    {
        #region 状态

        private static readonly object _lock = new object();
        private static readonly List<string> _productionKeys = new List<string>();
        private static readonly Dictionary<string, CharacterRandomPreset> _auditedPresets =
            new Dictionary<string, CharacterRandomPreset>(StringComparer.Ordinal);

        private static string _boundSignature;

        #endregion

        #region 只读

        /// <summary>当前 runtime 已物化的生产池 stable key（ordinal 升序）。</summary>
        public static List<string> ProductionKeys
        {
            get
            {
                lock (_lock)
                {
                    return new List<string>(_productionKeys);
                }
            }
        }

        /// <summary>生产池是否已物化。</summary>
        public static bool HasProductionPool
        {
            get
            {
                lock (_lock)
                {
                    return _productionKeys.Count > 0;
                }
            }
        }

        /// <summary>当前绑定的三签名组合。</summary>
        public static string BoundSignature { get { return _boundSignature; } }

        #endregion

        #region 物化

        /// <summary>
        /// 按认证报告物化生产池。只接受 status=Passed 的 key，且必须同时通过静态目录审计。
        /// </summary>
        internal static void MaterializeFromReport(ModeHProductionCertificationDto report)
        {
            lock (_lock)
            {
                _productionKeys.Clear();
                _auditedPresets.Clear();
                _boundSignature = null;

                if (report == null || report.records == null) return;

                _boundSignature = (report.gameBuildSignature != null ? report.gameBuildSignature : string.Empty)
                    + "|" + (report.modBuildSignature != null ? report.modBuildSignature : string.Empty)
                    + "|" + (report.contentCatalogSignature != null ? report.contentCatalogSignature : string.Empty);

                for (int i = 0; i < report.records.Count; i++)
                {
                    ModeHPresetCertificationRecordDto record = report.records[i];
                    if (record == null) continue;
                    if (record.status != (int)ModeHCertificationStatus.Passed) continue;
                    if (string.IsNullOrEmpty(record.stableKey)) continue;

                    // 双重条件：认证通过 + 仍在签名静态目录内
                    ModeHProfileTemplate template = ModeHProfileRegistry.GetByStableKey(record.stableKey);
                    if (template == null || !template.ProductionCandidate) continue;
                    if (ModeHProfileRegistry.IsExcludedStableKey(record.stableKey)) continue;

                    _productionKeys.Add(record.stableKey);
                }

                _productionKeys.Sort(StringComparer.Ordinal);
            }
        }

        /// <summary>清空生产池（签名变化、离场、Mod 卸载）。</summary>
        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _productionKeys.Clear();
                _auditedPresets.Clear();
                _boundSignature = null;
            }
        }

        #endregion

        #region 查询

        /// <summary>该 key 是否在当前 runtime 生产池内。</summary>
        public static bool IsProductionKey(string stableKey)
        {
            if (string.IsNullOrEmpty(stableKey)) return false;
            lock (_lock)
            {
                return _productionKeys.Contains(stableKey);
            }
        }

        /// <summary>
        /// 取生产池中该 key 的审计 preset（按需回查并缓存）。
        /// 不在生产池内一律返回 null，禁止绕过认证结果生成角色。
        /// </summary>
        internal static CharacterRandomPreset GetAuditedPreset(string stableKey)
        {
            if (!IsProductionKey(stableKey)) return null;

            lock (_lock)
            {
                CharacterRandomPreset cached;
                if (_auditedPresets.TryGetValue(stableKey, out cached) && cached != null) return cached;
            }

            CharacterRandomPreset preset = ModeHProductionCertification.ResolveAuditedPreset(stableKey);
            if (preset == null) return null;

            string failureReasonId;
            if (!ModeHProductionCertification.PassesStaticAudit(preset, out failureReasonId))
            {
                ModBehaviour.DevLog("[ModeH] 生产池 preset 复核失败 " + stableKey + ": " + failureReasonId);
                return null;
            }

            lock (_lock)
            {
                _auditedPresets[stableKey] = preset;
            }
            return preset;
        }

        /// <summary>按原型取生产池中的 stable key（ordinal 升序）。</summary>
        public static List<string> GetProductionKeysByArchetype(string archetypeId)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(archetypeId)) return result;

            List<string> keys = ProductionKeys;
            for (int i = 0; i < keys.Count; i++)
            {
                ModeHProfileTemplate template = ModeHProfileRegistry.GetByStableKey(keys[i]);
                if (template != null
                    && string.Equals(template.ArchetypeId, archetypeId, StringComparison.Ordinal))
                {
                    result.Add(keys[i]);
                }
            }
            return result;
        }

        /// <summary>生产池是否覆盖全部五种公开原型。</summary>
        public static bool CoversAllArchetypes()
        {
            for (int i = 0; i < ModeHStableIds.AllArchetypes.Length; i++)
            {
                if (GetProductionKeysByArchetype(ModeHStableIds.AllArchetypes[i]).Count == 0) return false;
            }
            return true;
        }

        #endregion
    }
}
