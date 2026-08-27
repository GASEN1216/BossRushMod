using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H 静态内容目录（设计提案 §23.2）。
    ///
    /// 职责：
    /// - 通过唯一入口 JsonDataRegistry 读取 Assets/Data/ModeH/ 的七个文件；
    /// - 逐文件核对 contentSignature（§20.2），任何一条不匹配即 fail-closed；
    /// - 生成 contentCatalogSignature；
    /// - 只把纯数值权重（OddsWeights）允许退回同版本内置 fallback，
    ///   preset / command / kit 审计表没有跨构建 fallback。
    ///
    /// 全部方法 no-throw；失败只置 LastError 并让 IsLoaded 保持 false。
    /// </summary>
    public static partial class ModeHContentCatalog
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _loaded;
        private static bool _loadAttempted;
        private static string _lastError;
        private static string _contentCatalogSignature;

        private static List<ModeHProfileTemplate> _profileTemplates;
        private static List<string> _excludedStableKeys;
        private static List<ModeHCommandSpec> _commands;
        private static List<string> _controlPointWhitelist;
        private static List<ModeHEffectSpec> _effectCatalog;
        private static List<string> _selfSettledEffectIds;
        private static List<ModeHKitSpec> _kits;
        private static List<ModeHInjurySpec> _injuries;
        private static List<ModeHScarSpec> _scars;
        private static List<ModeHMatchCorridor> _corridors;
        private static List<ModeHSkeletonSpec> _skeletons;
        private static List<ModeHEntryScriptSpec> _entryScripts;
        private static List<ModeHArenaConditionSpec> _arenaConditions;
        private static List<ModeHArchetypeCapability> _archetypeCapabilities;
        private static List<ModeHOddsTier> _oddsTiers;
        private static List<ModeHCommandTagMapping> _commandTagMap;
        private static List<ModeHOddsTestVector> _oddsTestVectors;
        private static ModeHJsonValue _playerWeights;
        private static ModeHJsonValue _enemyWeights;
        private static List<string> _archetypeMatrixPairs;
        private static bool _usedOddsFallback;

        #endregion

        #region 只读访问

        /// <summary>全部数据文件是否已加载并通过签名核对。</summary>
        public static bool IsLoaded { get { return _loaded; } }

        /// <summary>最后一次失败原因（fail-closed 展示用）。</summary>
        public static string LastError { get { return _lastError; } }

        /// <summary>内容目录签名（§20.2）。</summary>
        public static string ContentCatalogSignature { get { return _contentCatalogSignature; } }

        /// <summary>赔率权重是否使用了同版本内置 fallback。</summary>
        public static bool UsedOddsFallback { get { return _usedOddsFallback; } }

        /// <summary>全部 Boss 档案模板。</summary>
        public static List<ModeHProfileTemplate> ProfileTemplates { get { return _profileTemplates; } }

        /// <summary>硬排除的 stable key。</summary>
        public static List<string> ExcludedStableKeys { get { return _excludedStableKeys; } }

        /// <summary>全部口令（通用 + 招牌）。</summary>
        public static List<ModeHCommandSpec> Commands { get { return _commands; } }

        /// <summary>允许使用的控制点白名单。</summary>
        public static List<string> ControlPointWhitelist { get { return _controlPointWhitelist; } }

        /// <summary>逐 effect 目录（兼容矩阵基线）。</summary>
        public static List<ModeHEffectSpec> EffectCatalog { get { return _effectCatalog; } }

        /// <summary>Mode H 自结算的 effect（对任何 key 恒为 VerifiedBehavior）。</summary>
        public static List<string> SelfSettledEffectIds { get { return _selfSettledEffectIds; } }

        /// <summary>全部虚拟整备套装。</summary>
        public static List<ModeHKitSpec> Kits { get { return _kits; } }

        /// <summary>全部伤病定义。</summary>
        public static List<ModeHInjurySpec> Injuries { get { return _injuries; } }

        /// <summary>全部战痕定义。</summary>
        public static List<ModeHScarSpec> Scars { get { return _scars; } }

        /// <summary>六场威胁走廊。</summary>
        public static List<ModeHMatchCorridor> MatchCorridors { get { return _corridors; } }

        /// <summary>编制骨架。</summary>
        public static List<ModeHSkeletonSpec> Skeletons { get { return _skeletons; } }

        /// <summary>进场剧本。</summary>
        public static List<ModeHEntryScriptSpec> EntryScripts { get { return _entryScripts; } }

        /// <summary>擂台条件。</summary>
        public static List<ModeHArenaConditionSpec> ArenaConditions { get { return _arenaConditions; } }

        /// <summary>原型能力矩阵。</summary>
        public static List<ModeHArchetypeCapability> ArchetypeCapabilities { get { return _archetypeCapabilities; } }

        /// <summary>赔率档位。</summary>
        public static List<ModeHOddsTier> OddsTiers { get { return _oddsTiers; } }

        /// <summary>口令标签映射。</summary>
        public static List<ModeHCommandTagMapping> CommandTagMap { get { return _commandTagMap; } }

        /// <summary>赔率测试向量。</summary>
        public static List<ModeHOddsTestVector> OddsTestVectors { get { return _oddsTestVectors; } }

        /// <summary>玩家侧权重（原始 token，按需取整数）。</summary>
        public static ModeHJsonValue PlayerWeights { get { return _playerWeights; } }

        /// <summary>敌方侧权重（原始 token，按需取整数）。</summary>
        public static ModeHJsonValue EnemyWeights { get { return _enemyWeights; } }

        /// <summary>原型克制对（"attacker&gt;defender" 形式，反向取负值）。</summary>
        public static List<string> ArchetypeMatrixPairs { get { return _archetypeMatrixPairs; } }

        #endregion

        #region 加载

        /// <summary>清空缓存并允许下次重新加载。</summary>
        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _loaded = false;
                _loadAttempted = false;
                _lastError = null;
                _contentCatalogSignature = null;
                _profileTemplates = null;
                _excludedStableKeys = null;
                _commands = null;
                _controlPointWhitelist = null;
                _effectCatalog = null;
                _selfSettledEffectIds = null;
                _kits = null;
                _injuries = null;
                _scars = null;
                _corridors = null;
                _skeletons = null;
                _entryScripts = null;
                _arenaConditions = null;
                _archetypeCapabilities = null;
                _oddsTiers = null;
                _commandTagMap = null;
                _oddsTestVectors = null;
                _playerWeights = null;
                _enemyWeights = null;
                _archetypeMatrixPairs = null;
                _usedOddsFallback = false;
            }
        }

        /// <summary>幂等加载并核对全部数据文件；失败返回 false 并保留 LastError。</summary>
        public static bool EnsureLoaded()
        {
            lock (_lock)
            {
                if (_loaded) return true;
                if (_loadAttempted) return false;
                _loadAttempted = true;
            }

            try
            {
                return LoadInternal();
            }
            catch (Exception e)
            {
                _lastError = "content_load_exception:" + e.GetType().Name;
                return false;
            }
        }

        private static bool LoadInternal()
        {
            List<string> paths = new List<string>();
            List<string> signatures = new List<string>();
            Dictionary<string, ModeHJsonValue> roots = new Dictionary<string, ModeHJsonValue>(StringComparer.Ordinal);

            for (int i = 0; i < ModeHConfig.RequiredDataFileNames.Length; i++)
            {
                string fileName = ModeHConfig.RequiredDataFileNames[i];
                string raw;
                if (!JsonDataRegistry.TryReadDataFile(ModeHConfig.DataSubDirectoryName, fileName, out raw))
                {
                    _lastError = "content_file_missing:" + fileName;
                    return false;
                }
                ModeHJsonValue root;
                string signature;
                string error;
                if (!ModeHCanonicalDigest.TryParseAndVerifyContent(raw, out root, out signature, out error))
                {
                    _lastError = "content_signature_failed:" + fileName + ":" + error;
                    return false;
                }
                int schemaVersion;
                if (!root.TryGetInt("schemaVersion", out schemaVersion)
                    || schemaVersion != ModeHConfig.CurrentSchemaVersion)
                {
                    _lastError = "content_schema_version:" + fileName;
                    return false;
                }
                if (!VerifyOptionalBuildPin(root, "gameBuildSignature", true, fileName)) return false;
                if (!VerifyOptionalBuildPin(root, "modBuildSignature", false, fileName)) return false;

                roots[fileName] = root;
                paths.Add(fileName);
                signatures.Add(signature);
            }

            string catalogSignature;
            string catalogError;
            if (!ModeHCanonicalDigest.TryComputeContentCatalogSignature(
                    paths, signatures, out catalogSignature, out catalogError))
            {
                _lastError = "content_catalog_failed:" + catalogError;
                return false;
            }

            if (!ParseBossProfiles(roots[ModeHConfig.BossProfilesFileName])) return false;
            if (!ParseCommands(roots[ModeHConfig.CommandsFileName])) return false;
            if (!ParseCommandCompatibility(roots[ModeHConfig.CommandCompatibilityFileName])) return false;
            if (!ParseLoadoutKits(roots[ModeHConfig.LoadoutKitsFileName])) return false;
            if (!ParseScars(roots[ModeHConfig.ScarsFileName])) return false;
            if (!ParseThreatPlans(roots[ModeHConfig.ThreatPlansFileName])) return false;
            if (!ParseOddsWeights(roots[ModeHConfig.OddsWeightsFileName])) return false;

            _contentCatalogSignature = catalogSignature;
            _loaded = true;
            _lastError = null;
            return true;
        }

        /// <summary>
        /// 数据文件里的 game/mod 构建签名是“可选绑定”：空字符串表示不绑定具体构建；
        /// 非空时必须与当前实际加载的程序集摘要一致，否则 fail-closed。
        /// </summary>
        private static bool VerifyOptionalBuildPin(ModeHJsonValue root, string field, bool isGame, string fileName)
        {
            string declared;
            if (!root.TryGetString(field, out declared))
            {
                _lastError = "content_build_pin_missing:" + fileName + ":" + field;
                return false;
            }
            if (string.IsNullOrEmpty(declared)) return true;

            string current;
            string error;
            bool ok = isGame
                ? ModeHCanonicalDigest.TryGetGameBuildSignature(out current, out error)
                : ModeHCanonicalDigest.TryGetModBuildSignature(out current, out error);
            if (!ok)
            {
                _lastError = "content_build_pin_unresolved:" + fileName + ":" + field;
                return false;
            }
            if (!string.Equals(current, declared, StringComparison.Ordinal))
            {
                _lastError = "content_build_pin_mismatch:" + fileName + ":" + field;
                return false;
            }
            return true;
        }

        #endregion

        #region 工具

        /// <summary>取整数权重；缺失时返回 fallbackValue。</summary>
        public static int GetWeight(ModeHJsonValue weights, string field, int fallbackValue)
        {
            int value;
            if (weights != null && weights.TryGetInt(field, out value)) return value;
            return fallbackValue;
        }

        /// <summary>取整数权重数组中的一项；越界返回 fallbackValue。</summary>
        public static int GetWeightAt(ModeHJsonValue weights, string field, int index, int fallbackValue)
        {
            List<ModeHJsonValue> items;
            if (weights == null || !weights.TryGetArray(field, out items)) return fallbackValue;
            if (index < 0 || index >= items.Count) return fallbackValue;
            ModeHJsonValue item = items[index];
            if (item == null || item.Kind != ModeHJsonKind.Integer) return fallbackValue;
            return (int)item.IntegerValue;
        }

        /// <summary>原型克制关系：命中返回 +1，反向返回 -1，其余 0。</summary>
        public static int GetArchetypeMatchup(string attacker, string defender)
        {
            if (_archetypeMatrixPairs == null || string.IsNullOrEmpty(attacker) || string.IsNullOrEmpty(defender))
            {
                return 0;
            }
            string forward = attacker + ">" + defender;
            string reverse = defender + ">" + attacker;
            for (int i = 0; i < _archetypeMatrixPairs.Count; i++)
            {
                string pair = _archetypeMatrixPairs[i];
                if (string.Equals(pair, forward, StringComparison.Ordinal)) return 1;
                if (string.Equals(pair, reverse, StringComparison.Ordinal)) return -1;
            }
            return 0;
        }

        private static bool IsKnown(string[] pool, string id)
        {
            if (pool == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < pool.Length; i++)
            {
                if (string.Equals(pool[i], id, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        #endregion
    }
}
