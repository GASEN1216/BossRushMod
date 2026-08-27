using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H Boss 档案与生产目录注册表（设计提案 §17.2、§25.1）。
    ///
    /// 职责：
    /// - 从 ModeHContentCatalog 取 BossProfiles.json 的静态档案；
    /// - 校验签名生产目录严格为 8 至 12 个唯一 stable key / productionOrder；
    /// - 校验五种公开原型可覆盖；
    /// - 提供 stableKey / templateId 查询与硬排除判断。
    ///
    /// 本注册表只做静态内容审计，不代表 runtime 已通过生产认证；
    /// 真正的生产池由 ModeHPresetRegistry 在认证通过后物化。
    /// </summary>
    public static class ModeHProfileRegistry
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _validated;
        private static bool _validationAttempted;
        private static string _lastError;
        private static List<ModeHProfileTemplate> _productionCatalog;
        private static Dictionary<string, ModeHProfileTemplate> _byStableKey;
        private static Dictionary<string, ModeHProfileTemplate> _byTemplateId;
        private static HashSet<string> _excludedKeys;

        #endregion

        #region 只读访问

        /// <summary>静态内容审计是否通过。</summary>
        public static bool IsValidated { get { return _validated; } }

        /// <summary>最后一次审计失败原因。</summary>
        public static string LastError { get { return _lastError; } }

        /// <summary>签名生产目录（按 productionOrder 升序）。</summary>
        public static List<ModeHProfileTemplate> ProductionCatalog { get { return _productionCatalog; } }

        #endregion

        #region 审计

        /// <summary>清空缓存。</summary>
        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _validated = false;
                _validationAttempted = false;
                _lastError = null;
                _productionCatalog = null;
                _byStableKey = null;
                _byTemplateId = null;
                _excludedKeys = null;
            }
        }

        /// <summary>幂等执行静态审计；失败返回 false 并保留 LastError。</summary>
        public static bool EnsureValidated()
        {
            lock (_lock)
            {
                if (_validated) return true;
                if (_validationAttempted) return false;
                _validationAttempted = true;
            }

            try
            {
                return ValidateInternal();
            }
            catch (Exception e)
            {
                _lastError = "profile_registry_exception:" + e.GetType().Name;
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

            List<ModeHProfileTemplate> templates = ModeHContentCatalog.ProfileTemplates;
            if (templates == null || templates.Count == 0)
            {
                _lastError = "profile_catalog_empty";
                return false;
            }

            HashSet<string> excluded = new HashSet<string>(StringComparer.Ordinal);
            List<string> excludedList = ModeHContentCatalog.ExcludedStableKeys;
            if (excludedList != null)
            {
                for (int i = 0; i < excludedList.Count; i++)
                {
                    excluded.Add(excludedList[i]);
                }
            }

            List<ModeHProfileTemplate> production = new List<ModeHProfileTemplate>();
            Dictionary<string, ModeHProfileTemplate> byKey =
                new Dictionary<string, ModeHProfileTemplate>(StringComparer.Ordinal);
            Dictionary<string, ModeHProfileTemplate> byId =
                new Dictionary<string, ModeHProfileTemplate>(StringComparer.Ordinal);

            for (int i = 0; i < templates.Count; i++)
            {
                ModeHProfileTemplate t = templates[i];
                if (t == null) continue;
                byKey[t.StableKey] = t;
                byId[t.ProfileTemplateId] = t;
                if (!t.ProductionCandidate) continue;
                if (excluded.Contains(t.StableKey))
                {
                    _lastError = "profile_production_key_excluded:" + t.StableKey;
                    return false;
                }
                production.Add(t);
            }

            if (production.Count < ModeHConfig.MinProductionCandidateCount)
            {
                _lastError = "profile_production_below_min:" + production.Count.ToString();
                return false;
            }
            if (production.Count > ModeHConfig.MaxProductionCandidateCount)
            {
                _lastError = "profile_production_above_max:" + production.Count.ToString();
                return false;
            }

            production.Sort(delegate (ModeHProfileTemplate a, ModeHProfileTemplate b)
            {
                return a.ProductionOrder.CompareTo(b.ProductionOrder);
            });

            HashSet<string> archetypes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < production.Count; i++)
            {
                archetypes.Add(production[i].ArchetypeId);
            }
            if (archetypes.Count < ModeHConfig.RequiredArchetypeCoverage)
            {
                _lastError = "profile_archetype_coverage:" + archetypes.Count.ToString();
                return false;
            }

            // 每种原型至少两名候选，保证换将与市场窗口有真实选择
            for (int i = 0; i < ModeHStableIds.AllArchetypes.Length; i++)
            {
                string archetype = ModeHStableIds.AllArchetypes[i];
                int count = 0;
                for (int j = 0; j < production.Count; j++)
                {
                    if (string.Equals(production[j].ArchetypeId, archetype, StringComparison.Ordinal)) count++;
                }
                if (count < 2)
                {
                    _lastError = "profile_archetype_thin:" + archetype;
                    return false;
                }
            }

            _productionCatalog = production;
            _byStableKey = byKey;
            _byTemplateId = byId;
            _excludedKeys = excluded;
            _validated = true;
            _lastError = null;
            return true;
        }

        #endregion

        #region 查询

        /// <summary>按官方预设 stable key 取模板。</summary>
        public static ModeHProfileTemplate GetByStableKey(string stableKey)
        {
            if (_byStableKey == null || string.IsNullOrEmpty(stableKey)) return null;
            ModeHProfileTemplate template;
            return _byStableKey.TryGetValue(stableKey, out template) ? template : null;
        }

        /// <summary>按模板 ID 取模板。</summary>
        public static ModeHProfileTemplate GetByTemplateId(string templateId)
        {
            if (_byTemplateId == null || string.IsNullOrEmpty(templateId)) return null;
            ModeHProfileTemplate template;
            return _byTemplateId.TryGetValue(templateId, out template) ? template : null;
        }

        /// <summary>该 stable key 是否被硬排除（managed Boss、特殊预设、Character_Ming 等）。</summary>
        public static bool IsExcludedStableKey(string stableKey)
        {
            if (string.IsNullOrEmpty(stableKey)) return true;
            if (_excludedKeys != null && _excludedKeys.Contains(stableKey)) return true;
            return false;
        }

        /// <summary>生产目录中的全部 stable key（按 productionOrder 升序）。</summary>
        public static List<string> GetProductionStableKeys()
        {
            List<string> keys = new List<string>();
            if (_productionCatalog == null) return keys;
            for (int i = 0; i < _productionCatalog.Count; i++)
            {
                keys.Add(_productionCatalog[i].StableKey);
            }
            return keys;
        }

        /// <summary>按原型取生产目录中的模板（保持 productionOrder 顺序）。</summary>
        public static List<ModeHProfileTemplate> GetProductionByArchetype(string archetypeId)
        {
            List<ModeHProfileTemplate> result = new List<ModeHProfileTemplate>();
            if (_productionCatalog == null || string.IsNullOrEmpty(archetypeId)) return result;
            for (int i = 0; i < _productionCatalog.Count; i++)
            {
                ModeHProfileTemplate t = _productionCatalog[i];
                if (string.Equals(t.ArchetypeId, archetypeId, StringComparison.Ordinal)) result.Add(t);
            }
            return result;
        }

        /// <summary>该模板是否携带公开异常。</summary>
        public static bool HasAnomaly(ModeHProfileTemplate template)
        {
            return template != null && !string.IsNullOrEmpty(template.AnomalyId);
        }

        /// <summary>该模板底色是否属于稳定型（§17.2 五席至少两名稳定底色）。</summary>
        public static bool IsStableTemperament(ModeHProfileTemplate template)
        {
            if (template == null) return false;
            for (int i = 0; i < ModeHStableIds.StableTemperaments.Length; i++)
            {
                if (string.Equals(ModeHStableIds.StableTemperaments[i], template.TemperamentId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}
