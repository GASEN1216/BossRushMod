using System;
using System.Collections.Generic;

namespace BossRush
{
    #region 内容模型（只读，来自 Assets/Data/ModeH/*.json）

    /// <summary>单条口令 effect 的调制描述（Commands.json / Scars.json 共用）。</summary>
    public sealed class ModeHEffectSpec
    {
        /// <summary>effectId，命名固定为 &lt;commandId&gt;.&lt;controlPointId&gt;。</summary>
        public string EffectId;
        /// <summary>控制点 ID，必须落在 §17.6.2 白名单内。</summary>
        public string ControlPointId;
        /// <summary>操作类别：multiply / multiply_capped / set_bool / set_value / add_seconds_milli /
        /// set_marker_window_end / set_marker_past / fire_* / self_settled*。</summary>
        public string Op;
        /// <summary>千分之一整数倍率。</summary>
        public int MultiplierMilli;
        /// <summary>千分之一整数上限。</summary>
        public int CapMilli;
        /// <summary>千分之一整数绝对值。</summary>
        public int ValueMilli;
        /// <summary>千分之一秒增量。</summary>
        public int AddMilli;
        /// <summary>布尔目标值。</summary>
        public bool BoolValue;
        /// <summary>是否由 Mode H 自结算（对任何 stable key 恒为 VerifiedBehavior）。</summary>
        public bool SelfSettled;
        /// <summary>窗口结束时是否还原（nextReleaseSkillTimeMarker 固定 false）。</summary>
        public bool Restore;
        /// <summary>收益/代价角色（战痕使用）。</summary>
        public string Role;
        /// <summary>生效条件标签（战痕/伤病使用）。</summary>
        public string AppliesWhen;
        /// <summary>目标口令 ID（self_settled_command_scale 使用）。</summary>
        public string TargetCommandId;
        /// <summary>目标装备槽（self_settled_kit_slot_disabled 使用）。</summary>
        public string TargetSlot;
        /// <summary>局部窗口秒数（0 表示沿用条目窗口）。</summary>
        public int WindowSeconds;
    }

    /// <summary>一条口令定义。</summary>
    public sealed class ModeHCommandSpec
    {
        /// <summary>口令稳定 ID。</summary>
        public string CommandId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>说明 key。</summary>
        public string DescKey;
        /// <summary>基础意图。</summary>
        public string Intent;
        /// <summary>是否为招牌口令。</summary>
        public bool IsSignature;
        /// <summary>招牌口令所属原型。</summary>
        public string ArchetypeId;
        /// <summary>要求敌方同时在场数量下限（0 表示无要求）。</summary>
        public int RequiresEnemyCountAtLeast;
        /// <summary>是否只有接力者实际登场后才有效（handoff）。</summary>
        public bool RequiresRelayEntered;
        /// <summary>逐 effect 调制。</summary>
        public List<ModeHEffectSpec> Effects;
    }

    /// <summary>一条伤病定义。</summary>
    public sealed class ModeHInjurySpec
    {
        /// <summary>伤病稳定 ID。</summary>
        public string InjuryId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>说明 key。</summary>
        public string DescKey;
        /// <summary>作用域：whole_match / triggered_once / self_settled。</summary>
        public string Scope;
        /// <summary>触发生命比例（千分之一整数，0 表示不适用）。</summary>
        public int TriggerHealthFractionMilli;
        /// <summary>要求敌方同时在场数量下限。</summary>
        public int RequiresEnemyCountAtLeast;
        /// <summary>逐分量调制。</summary>
        public List<ModeHEffectSpec> Components;
    }

    /// <summary>一条战痕定义。</summary>
    public sealed class ModeHScarSpec
    {
        /// <summary>战痕稳定 ID。</summary>
        public string ScarId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>说明 key。</summary>
        public string DescKey;
        /// <summary>触发条件 ID。</summary>
        public string Trigger;
        /// <summary>窗口秒数（0 表示条件型常驻判断）。</summary>
        public int WindowSeconds;
        /// <summary>兼容原型。</summary>
        public List<string> CompatibleArchetypeIds;
        /// <summary>收益赔率标签。</summary>
        public string BenefitTag;
        /// <summary>代价赔率标签。</summary>
        public string CostTag;
        /// <summary>收益赔率分。</summary>
        public int BenefitOdds;
        /// <summary>代价赔率分。</summary>
        public int CostOdds;
        /// <summary>逐分量调制（收益与代价必须同时可用）。</summary>
        public List<ModeHEffectSpec> Components;
    }

    /// <summary>Boss 档案模板。</summary>
    public sealed class ModeHProfileTemplate
    {
        /// <summary>模板稳定 ID。</summary>
        public string ProfileTemplateId;
        /// <summary>官方预设 nameKey（EnemyPresetInfo.name）。</summary>
        public string StableKey;
        /// <summary>显示名 key。</summary>
        public string DisplayNameKey;
        /// <summary>传闻 key。</summary>
        public string RumorKey;
        /// <summary>公开原型。</summary>
        public string ArchetypeId;
        /// <summary>固有底色。</summary>
        public string TemperamentId;
        /// <summary>普通怪癖（与异常互斥）。</summary>
        public string QuirkId;
        /// <summary>公开异常（与怪癖互斥）。</summary>
        public string AnomalyId;
        /// <summary>招牌口令。</summary>
        public string SignatureCommandId;
        /// <summary>看台表演模式。</summary>
        public string StandInPatternId;
        /// <summary>是否进入生产目录。</summary>
        public bool ProductionCandidate;
        /// <summary>生产目录顺序（唯一）。</summary>
        public int ProductionOrder;
        /// <summary>单体威胁评分。</summary>
        public int ThreatScore;
        /// <summary>能力标签（供计划 veto 使用）。</summary>
        public List<string> CapabilityTags;
    }

    /// <summary>虚拟整备套装定义（静态部分；typeId 解析在运行时完成）。</summary>
    public sealed class ModeHKitSpec
    {
        /// <summary>套装稳定 ID。</summary>
        public string KitId;
        /// <summary>是否 starter。</summary>
        public bool IsStarterKit;
        /// <summary>starter 顺序。</summary>
        public int StarterOrder;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>说明 key。</summary>
        public string DescKey;
        /// <summary>替换槽位。</summary>
        public string ReplaceSlot;
        /// <summary>固定官方 typeId（0 表示按 tag 解析）。</summary>
        public int TypeId;
        /// <summary>解析标签。</summary>
        public List<string> ResolveTags;
        /// <summary>解析品质下界。</summary>
        public int ResolveMinQuality;
        /// <summary>解析品质上界。</summary>
        public int ResolveMaxQuality;
        /// <summary>解析序号（候选按 typeId 升序后取该下标）。</summary>
        public int ResolveOrdinal;
        /// <summary>声明品质（1..8）。</summary>
        public int GameQuality;
        /// <summary>固定弹药 typeId（0 表示按口径解析）。</summary>
        public int AmmoTypeId;
        /// <summary>冻结弹药数量。</summary>
        public int AmmoCount;
        /// <summary>是否按枪械口径解析弹药。</summary>
        public bool ResolveAmmoByCaliber;
        /// <summary>兼容原型。</summary>
        public List<string> CompatibleArchetypeIds;
        /// <summary>兼容 profile 模板。</summary>
        public List<string> CompatibleProfileIds;
        /// <summary>公开克制标签。</summary>
        public List<string> PublicTags;
    }

    /// <summary>单场威胁走廊。</summary>
    public sealed class ModeHMatchCorridor
    {
        /// <summary>比赛编号。</summary>
        public int MatchIndex;
        /// <summary>总威胁预算。</summary>
        public int ThreatBudget;
        /// <summary>同时在场上限。</summary>
        public int SimultaneousCap;
        /// <summary>最低填充百分比（防止后期计划过弱）。</summary>
        public int MinFillPercent;
        /// <summary>可用编制骨架。</summary>
        public List<string> SkeletonIds;
    }

    /// <summary>编制骨架。</summary>
    public sealed class ModeHSkeletonSpec
    {
        /// <summary>骨架 ID。</summary>
        public string SkeletonId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>最少单位数。</summary>
        public int MinUnits;
        /// <summary>最多单位数。</summary>
        public int MaxUnits;
        /// <summary>公开标签。</summary>
        public List<string> PublicTags;
        /// <summary>是否含已公开的高威胁核心。</summary>
        public bool HasHighThreatCore;
        /// <summary>带伤单位数。</summary>
        public int WoundedUnits;
        /// <summary>是否需要落选回响核心。</summary>
        public bool RequiresEchoReturn;
    }

    /// <summary>进场剧本。</summary>
    public sealed class ModeHEntryScriptSpec
    {
        /// <summary>剧本 ID。</summary>
        public string EntryScriptId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>公开提示 key。</summary>
        public string HintKey;
        /// <summary>分批入场人数序列。</summary>
        public List<int> BatchPattern;
        /// <summary>公开标签。</summary>
        public List<string> PublicTags;
        /// <summary>核心是否压轴。</summary>
        public bool CoreEntersLast;
        /// <summary>是否保留一个未知席位。</summary>
        public bool HiddenSeat;
    }

    /// <summary>擂台条件。</summary>
    public sealed class ModeHArenaConditionSpec
    {
        /// <summary>条件 ID。</summary>
        public string ConditionId;
        /// <summary>显示名 key。</summary>
        public string NameKey;
        /// <summary>公开标签。</summary>
        public List<string> PublicTags;
        /// <summary>受益原型。</summary>
        public List<string> FavoredArchetypeIds;
        /// <summary>受损原型。</summary>
        public List<string> DisfavoredArchetypeIds;
    }

    /// <summary>原型能力矩阵条目（roster-level veto 使用）。</summary>
    public sealed class ModeHArchetypeCapability
    {
        /// <summary>原型 ID。</summary>
        public string ArchetypeId;
        /// <summary>主要手段标签。</summary>
        public List<string> PrimaryAnswers;
        /// <summary>会被硬封死的敌方能力标签。</summary>
        public List<string> HardLockedBy;
    }

    /// <summary>赔率档位。</summary>
    public sealed class ModeHOddsTier
    {
        /// <summary>净赔率倍数。</summary>
        public int Odds;
        /// <summary>公开分差下界。</summary>
        public int MinPublicEdge;
        /// <summary>公开分差上界。</summary>
        public int MaxPublicEdge;
        /// <summary>盘口称呼 key。</summary>
        public string ToneKey;
    }

    /// <summary>口令与公开标签的相合/冲突映射。</summary>
    public sealed class ModeHCommandTagMapping
    {
        /// <summary>口令 ID。</summary>
        public string CommandId;
        /// <summary>相合标签。</summary>
        public List<string> AlignedTags;
        /// <summary>冲突标签。</summary>
        public List<string> ConflictedTags;
    }

    /// <summary>赔率公开分差测试向量。</summary>
    public sealed class ModeHOddsTestVector
    {
        /// <summary>向量 ID。</summary>
        public string VectorId;
        /// <summary>玩家公开分。</summary>
        public int PlayerPublicScore;
        /// <summary>敌方公开分。</summary>
        public int EnemyPublicScore;
        /// <summary>期望赔率档。</summary>
        public int ExpectedOdds;
    }

    #endregion

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
    public static class ModeHContentCatalog
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

        #region 解析：BossProfiles

        private static bool ParseBossProfiles(ModeHJsonValue root)
        {
            List<ModeHJsonValue> items;
            if (!root.TryGetArray("profileTemplates", out items) || items.Count == 0)
            {
                _lastError = "boss_profiles_empty";
                return false;
            }

            List<ModeHProfileTemplate> templates = new List<ModeHProfileTemplate>(items.Count);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> orders = new HashSet<int>();

            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    _lastError = "boss_profile_not_object";
                    return false;
                }
                ModeHProfileTemplate t = new ModeHProfileTemplate();
                if (!item.TryGetString("profileTemplateId", out t.ProfileTemplateId)
                    || !ModeHStateModel.IsValidStableId(t.ProfileTemplateId))
                {
                    _lastError = "boss_profile_id_invalid";
                    return false;
                }
                if (!ids.Add(t.ProfileTemplateId))
                {
                    _lastError = "boss_profile_id_duplicate:" + t.ProfileTemplateId;
                    return false;
                }
                if (!item.TryGetString("stableKey", out t.StableKey) || string.IsNullOrEmpty(t.StableKey))
                {
                    _lastError = "boss_profile_stable_key_missing:" + t.ProfileTemplateId;
                    return false;
                }
                if (!keys.Add(t.StableKey))
                {
                    _lastError = "boss_profile_stable_key_duplicate:" + t.StableKey;
                    return false;
                }
                item.TryGetString("displayNameKey", out t.DisplayNameKey);
                item.TryGetString("rumorKey", out t.RumorKey);
                item.TryGetString("archetypeId", out t.ArchetypeId);
                item.TryGetString("temperamentId", out t.TemperamentId);
                item.TryGetString("quirkId", out t.QuirkId);
                item.TryGetString("anomalyId", out t.AnomalyId);
                item.TryGetString("signatureCommandId", out t.SignatureCommandId);
                item.TryGetString("standInPatternId", out t.StandInPatternId);
                item.TryGetBool("productionCandidate", out t.ProductionCandidate);
                item.TryGetInt("productionOrder", out t.ProductionOrder);
                item.TryGetInt("threatScore", out t.ThreatScore);
                item.TryGetStringList("capabilityTags", out t.CapabilityTags);

                if (!IsKnown(ModeHStableIds.AllArchetypes, t.ArchetypeId))
                {
                    _lastError = "boss_profile_archetype_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                if (!IsKnown(ModeHStableIds.AllTemperaments, t.TemperamentId))
                {
                    _lastError = "boss_profile_temperament_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                bool hasQuirk = !string.IsNullOrEmpty(t.QuirkId);
                bool hasAnomaly = !string.IsNullOrEmpty(t.AnomalyId);
                if (hasQuirk && hasAnomaly)
                {
                    _lastError = "boss_profile_quirk_anomaly_conflict:" + t.ProfileTemplateId;
                    return false;
                }
                if (hasQuirk && !IsKnown(ModeHStableIds.AllQuirks, t.QuirkId))
                {
                    _lastError = "boss_profile_quirk_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                if (hasAnomaly && !IsKnown(ModeHStableIds.AllAnomalies, t.AnomalyId))
                {
                    _lastError = "boss_profile_anomaly_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                if (!IsKnown(ModeHStableIds.AllSignatureCommands, t.SignatureCommandId))
                {
                    _lastError = "boss_profile_signature_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                if (!IsKnown(ModeHStableIds.AllStandInPatterns, t.StandInPatternId))
                {
                    _lastError = "boss_profile_standin_unknown:" + t.ProfileTemplateId;
                    return false;
                }
                if (t.ThreatScore <= 0)
                {
                    _lastError = "boss_profile_threat_invalid:" + t.ProfileTemplateId;
                    return false;
                }
                if (t.ProductionCandidate)
                {
                    if (t.ProductionOrder <= 0 || !orders.Add(t.ProductionOrder))
                    {
                        _lastError = "boss_profile_order_invalid:" + t.ProfileTemplateId;
                        return false;
                    }
                }
                templates.Add(t);
            }

            List<string> excluded;
            root.TryGetStringList("excludedStableKeys", out excluded);
            _excludedStableKeys = excluded != null ? excluded : new List<string>();
            _profileTemplates = templates;
            return true;
        }

        #endregion

        #region 解析：Commands 与兼容矩阵

        private static bool ParseCommands(ModeHJsonValue root)
        {
            List<string> whitelist;
            if (!root.TryGetStringList("controlPointWhitelist", out whitelist) || whitelist.Count == 0)
            {
                _lastError = "commands_whitelist_missing";
                return false;
            }
            _controlPointWhitelist = whitelist;

            List<ModeHCommandSpec> commands = new List<ModeHCommandSpec>();
            if (!ParseCommandArray(root, "commonCommands", false, commands)) return false;
            if (!ParseCommandArray(root, "signatureCommands", true, commands)) return false;

            int commonCount = 0;
            for (int i = 0; i < commands.Count; i++)
            {
                if (!commands[i].IsSignature) commonCount++;
            }
            if (commonCount != ModeHStableIds.AllCommonCommands.Length)
            {
                _lastError = "commands_common_count_mismatch";
                return false;
            }
            _commands = commands;
            return true;
        }

        private static bool ParseCommandArray(
            ModeHJsonValue root, string field, bool isSignature, List<ModeHCommandSpec> output)
        {
            List<ModeHJsonValue> items;
            if (!root.TryGetArray(field, out items) || items.Count == 0)
            {
                _lastError = "commands_section_missing:" + field;
                return false;
            }
            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    _lastError = "command_not_object:" + field;
                    return false;
                }
                ModeHCommandSpec spec = new ModeHCommandSpec();
                spec.IsSignature = isSignature;
                if (!item.TryGetString("commandId", out spec.CommandId)
                    || !ModeHStateModel.IsValidStableId(spec.CommandId))
                {
                    _lastError = "command_id_invalid:" + field;
                    return false;
                }
                item.TryGetString("nameKey", out spec.NameKey);
                item.TryGetString("descKey", out spec.DescKey);
                item.TryGetString("intent", out spec.Intent);
                item.TryGetString("archetypeId", out spec.ArchetypeId);
                item.TryGetInt("requiresEnemyCountAtLeast", out spec.RequiresEnemyCountAtLeast);
                item.TryGetBool("requiresRelayEntered", out spec.RequiresRelayEntered);

                if (isSignature && !IsKnown(ModeHStableIds.AllArchetypes, spec.ArchetypeId))
                {
                    _lastError = "signature_command_archetype_unknown:" + spec.CommandId;
                    return false;
                }
                if (!isSignature && !IsKnown(ModeHStableIds.AllCommonCommands, spec.CommandId))
                {
                    _lastError = "common_command_unknown:" + spec.CommandId;
                    return false;
                }
                if (isSignature && !IsKnown(ModeHStableIds.AllSignatureCommands, spec.CommandId))
                {
                    _lastError = "signature_command_unknown:" + spec.CommandId;
                    return false;
                }

                if (!ParseEffects(item, "effects", spec.CommandId, out spec.Effects)) return false;
                output.Add(spec);
            }
            return true;
        }

        private static bool ParseEffects(
            ModeHJsonValue owner, string field, string ownerId, out List<ModeHEffectSpec> effects)
        {
            effects = new List<ModeHEffectSpec>();
            List<ModeHJsonValue> items;
            if (!owner.TryGetArray(field, out items) || items.Count == 0)
            {
                _lastError = "effects_missing:" + ownerId;
                return false;
            }
            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    _lastError = "effect_not_object:" + ownerId;
                    return false;
                }
                ModeHEffectSpec effect = new ModeHEffectSpec();
                if (!item.TryGetString("effectId", out effect.EffectId) || string.IsNullOrEmpty(effect.EffectId))
                {
                    _lastError = "effect_id_missing:" + ownerId;
                    return false;
                }
                if (!item.TryGetString("controlPointId", out effect.ControlPointId)
                    || string.IsNullOrEmpty(effect.ControlPointId))
                {
                    _lastError = "effect_control_point_missing:" + effect.EffectId;
                    return false;
                }
                if (_controlPointWhitelist != null && !_controlPointWhitelist.Contains(effect.ControlPointId))
                {
                    _lastError = "effect_control_point_not_whitelisted:" + effect.EffectId;
                    return false;
                }
                if (!item.TryGetString("op", out effect.Op) || string.IsNullOrEmpty(effect.Op))
                {
                    _lastError = "effect_op_missing:" + effect.EffectId;
                    return false;
                }
                item.TryGetInt("multiplierMilli", out effect.MultiplierMilli);
                item.TryGetInt("capMilli", out effect.CapMilli);
                item.TryGetInt("valueMilli", out effect.ValueMilli);
                item.TryGetInt("addMilli", out effect.AddMilli);
                item.TryGetBool("boolValue", out effect.BoolValue);
                item.TryGetBool("selfSettled", out effect.SelfSettled);
                item.TryGetInt("windowSeconds", out effect.WindowSeconds);
                item.TryGetString("role", out effect.Role);
                item.TryGetString("appliesWhen", out effect.AppliesWhen);
                item.TryGetString("commandId", out effect.TargetCommandId);
                item.TryGetString("slot", out effect.TargetSlot);
                if (!item.TryGetBool("restore", out effect.Restore))
                {
                    // 默认还原；nextReleaseSkillTimeMarker 必须显式写 restore=false（§17.6.2）
                    effect.Restore = true;
                }
                if (string.Equals(effect.ControlPointId, "nextReleaseSkillTimeMarker", StringComparison.Ordinal)
                    && effect.Restore)
                {
                    _lastError = "effect_marker_must_not_restore:" + effect.EffectId;
                    return false;
                }
                effects.Add(effect);
            }
            return true;
        }

        private static bool ParseCommandCompatibility(ModeHJsonValue root)
        {
            List<string> selfSettled;
            root.TryGetStringList("selfSettledEffects", out selfSettled);
            _selfSettledEffectIds = selfSettled != null ? selfSettled : new List<string>();

            List<ModeHJsonValue> items;
            if (!root.TryGetArray("effectCatalog", out items) || items.Count == 0)
            {
                _lastError = "effect_catalog_empty";
                return false;
            }

            List<ModeHEffectSpec> catalog = new List<ModeHEffectSpec>(items.Count);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    _lastError = "effect_catalog_not_object";
                    return false;
                }
                ModeHEffectSpec entry = new ModeHEffectSpec();
                string commandId;
                if (!item.TryGetString("commandId", out commandId)
                    || !item.TryGetString("effectId", out entry.EffectId)
                    || !item.TryGetString("controlPointId", out entry.ControlPointId))
                {
                    _lastError = "effect_catalog_field_missing";
                    return false;
                }
                if (!seen.Add(entry.EffectId))
                {
                    _lastError = "effect_catalog_duplicate:" + entry.EffectId;
                    return false;
                }
                if (!entry.EffectId.StartsWith(commandId + ".", StringComparison.Ordinal))
                {
                    _lastError = "effect_catalog_id_shape:" + entry.EffectId;
                    return false;
                }
                entry.Op = "catalog";
                entry.SelfSettled = _selfSettledEffectIds.Contains(entry.EffectId);
                catalog.Add(entry);
            }

            // 目录必须覆盖 Commands.json 中的每一条 effect
            if (_commands != null)
            {
                for (int i = 0; i < _commands.Count; i++)
                {
                    List<ModeHEffectSpec> effects = _commands[i].Effects;
                    if (effects == null) continue;
                    for (int j = 0; j < effects.Count; j++)
                    {
                        if (!seen.Contains(effects[j].EffectId))
                        {
                            _lastError = "effect_catalog_missing_effect:" + effects[j].EffectId;
                            return false;
                        }
                    }
                }
            }

            _effectCatalog = catalog;
            return true;
        }

        #endregion

        #region 解析：LoadoutKits

        private static bool ParseLoadoutKits(ModeHJsonValue root)
        {
            List<ModeHJsonValue> items;
            if (!root.TryGetArray("kits", out items) || items.Count == 0)
            {
                _lastError = "kits_empty";
                return false;
            }
            List<ModeHKitSpec> kits = new List<ModeHKitSpec>(items.Count);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> starterOrders = new HashSet<int>();

            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    _lastError = "kit_not_object";
                    return false;
                }
                ModeHKitSpec kit = new ModeHKitSpec();
                if (!item.TryGetString("kitId", out kit.KitId) || !ModeHStateModel.IsValidStableId(kit.KitId))
                {
                    _lastError = "kit_id_invalid";
                    return false;
                }
                if (!ids.Add(kit.KitId))
                {
                    _lastError = "kit_id_duplicate:" + kit.KitId;
                    return false;
                }
                item.TryGetBool("isStarterKit", out kit.IsStarterKit);
                item.TryGetInt("starterOrder", out kit.StarterOrder);
                item.TryGetString("nameKey", out kit.NameKey);
                item.TryGetString("descKey", out kit.DescKey);
                item.TryGetString("replaceSlot", out kit.ReplaceSlot);
                item.TryGetInt("typeId", out kit.TypeId);
                item.TryGetStringList("resolveTags", out kit.ResolveTags);
                item.TryGetInt("resolveMinQuality", out kit.ResolveMinQuality);
                item.TryGetInt("resolveMaxQuality", out kit.ResolveMaxQuality);
                item.TryGetInt("resolveOrdinal", out kit.ResolveOrdinal);
                item.TryGetInt("gameQuality", out kit.GameQuality);
                item.TryGetInt("ammoTypeId", out kit.AmmoTypeId);
                item.TryGetInt("ammoCount", out kit.AmmoCount);
                item.TryGetBool("resolveAmmoByCaliber", out kit.ResolveAmmoByCaliber);
                item.TryGetStringList("compatibleArchetypeIds", out kit.CompatibleArchetypeIds);
                item.TryGetStringList("compatibleProfileIds", out kit.CompatibleProfileIds);
                item.TryGetStringList("publicTags", out kit.PublicTags);

                if (!IsKnown(ModeHStableIds.AllowedKitSlots, kit.ReplaceSlot))
                {
                    _lastError = "kit_slot_not_allowed:" + kit.KitId;
                    return false;
                }
                if (kit.GameQuality < ModeHConfig.MinGameQuality || kit.GameQuality > ModeHConfig.MaxGameQuality)
                {
                    _lastError = "kit_quality_out_of_range:" + kit.KitId;
                    return false;
                }
                bool hasPinnedType = kit.TypeId > 0;
                bool hasResolver = kit.ResolveTags != null && kit.ResolveTags.Count > 0;
                if (!hasPinnedType && !hasResolver)
                {
                    _lastError = "kit_type_unresolvable:" + kit.KitId;
                    return false;
                }
                if (hasResolver)
                {
                    if (kit.ResolveMinQuality < ModeHConfig.MinGameQuality
                        || kit.ResolveMaxQuality > ModeHConfig.MaxGameQuality
                        || kit.ResolveMinQuality > kit.ResolveMaxQuality)
                    {
                        _lastError = "kit_resolve_quality_invalid:" + kit.KitId;
                        return false;
                    }
                    if (kit.ResolveOrdinal < 0)
                    {
                        _lastError = "kit_resolve_ordinal_invalid:" + kit.KitId;
                        return false;
                    }
                }
                bool isWeaponSlot = string.Equals(kit.ReplaceSlot, "PrimaryWeapon", StringComparison.Ordinal)
                    || string.Equals(kit.ReplaceSlot, "SecondaryWeapon", StringComparison.Ordinal);
                if (isWeaponSlot && kit.AmmoCount <= 0 && !kit.ResolveAmmoByCaliber && kit.AmmoTypeId <= 0)
                {
                    _lastError = "kit_ammo_missing:" + kit.KitId;
                    return false;
                }
                if (kit.IsStarterKit)
                {
                    if (kit.StarterOrder <= 0 || !starterOrders.Add(kit.StarterOrder))
                    {
                        _lastError = "kit_starter_order_invalid:" + kit.KitId;
                        return false;
                    }
                }
                if (kit.CompatibleArchetypeIds == null) kit.CompatibleArchetypeIds = new List<string>();
                if (kit.CompatibleProfileIds == null) kit.CompatibleProfileIds = new List<string>();
                if (kit.PublicTags == null) kit.PublicTags = new List<string>();
                kits.Add(kit);
            }

            _kits = kits;
            return true;
        }

        #endregion

        #region 解析：Scars（伤病 + 战痕）

        private static bool ParseScars(ModeHJsonValue root)
        {
            List<ModeHJsonValue> injuryItems;
            if (!root.TryGetArray("injuries", out injuryItems)
                || injuryItems.Count != ModeHStableIds.AllInjuries.Length)
            {
                _lastError = "injuries_count_mismatch";
                return false;
            }
            List<ModeHInjurySpec> injuries = new List<ModeHInjurySpec>(injuryItems.Count);
            for (int i = 0; i < injuryItems.Count; i++)
            {
                ModeHJsonValue item = injuryItems[i];
                ModeHInjurySpec spec = new ModeHInjurySpec();
                if (item == null || !item.TryGetString("injuryId", out spec.InjuryId)
                    || !IsKnown(ModeHStableIds.AllInjuries, spec.InjuryId))
                {
                    _lastError = "injury_id_unknown";
                    return false;
                }
                item.TryGetString("nameKey", out spec.NameKey);
                item.TryGetString("descKey", out spec.DescKey);
                item.TryGetString("scope", out spec.Scope);
                item.TryGetInt("triggerHealthFractionMilli", out spec.TriggerHealthFractionMilli);
                item.TryGetInt("requiresEnemyCountAtLeast", out spec.RequiresEnemyCountAtLeast);
                if (!ParseEffects(item, "components", spec.InjuryId, out spec.Components)) return false;
                injuries.Add(spec);
            }

            List<ModeHJsonValue> scarItems;
            if (!root.TryGetArray("scars", out scarItems) || scarItems.Count != ModeHStableIds.AllScars.Length)
            {
                _lastError = "scars_count_mismatch";
                return false;
            }
            List<ModeHScarSpec> scars = new List<ModeHScarSpec>(scarItems.Count);
            for (int i = 0; i < scarItems.Count; i++)
            {
                ModeHJsonValue item = scarItems[i];
                ModeHScarSpec spec = new ModeHScarSpec();
                if (item == null || !item.TryGetString("scarId", out spec.ScarId)
                    || !IsKnown(ModeHStableIds.AllScars, spec.ScarId))
                {
                    _lastError = "scar_id_unknown";
                    return false;
                }
                item.TryGetString("nameKey", out spec.NameKey);
                item.TryGetString("descKey", out spec.DescKey);
                item.TryGetString("trigger", out spec.Trigger);
                item.TryGetInt("windowSeconds", out spec.WindowSeconds);
                item.TryGetStringList("compatibleArchetypeIds", out spec.CompatibleArchetypeIds);
                item.TryGetString("benefitTag", out spec.BenefitTag);
                item.TryGetString("costTag", out spec.CostTag);
                item.TryGetInt("benefitOdds", out spec.BenefitOdds);
                item.TryGetInt("costOdds", out spec.CostOdds);
                if (!ParseEffects(item, "components", spec.ScarId, out spec.Components)) return false;

                bool hasBenefit = false;
                bool hasCost = false;
                for (int j = 0; j < spec.Components.Count; j++)
                {
                    string role = spec.Components[j].Role;
                    if (string.Equals(role, "benefit", StringComparison.Ordinal)) hasBenefit = true;
                    else if (string.Equals(role, "cost", StringComparison.Ordinal)) hasCost = true;
                }
                if (!hasBenefit || !hasCost)
                {
                    _lastError = "scar_missing_benefit_or_cost:" + spec.ScarId;
                    return false;
                }
                if (spec.CompatibleArchetypeIds == null || spec.CompatibleArchetypeIds.Count == 0)
                {
                    _lastError = "scar_archetype_missing:" + spec.ScarId;
                    return false;
                }
                scars.Add(spec);
            }

            _injuries = injuries;
            _scars = scars;
            return true;
        }

        #endregion

        #region 解析：ThreatPlans

        private static bool ParseThreatPlans(ModeHJsonValue root)
        {
            List<ModeHJsonValue> corridorItems;
            if (!root.TryGetArray("matchCorridor", out corridorItems)
                || corridorItems.Count != ModeHConfig.SeasonMatchCount)
            {
                _lastError = "corridor_count_mismatch";
                return false;
            }
            List<ModeHMatchCorridor> corridors = new List<ModeHMatchCorridor>(corridorItems.Count);
            for (int i = 0; i < corridorItems.Count; i++)
            {
                ModeHJsonValue item = corridorItems[i];
                ModeHMatchCorridor c = new ModeHMatchCorridor();
                if (item == null
                    || !item.TryGetInt("matchIndex", out c.MatchIndex)
                    || !item.TryGetInt("threatBudget", out c.ThreatBudget)
                    || !item.TryGetInt("simultaneousCap", out c.SimultaneousCap)
                    || !item.TryGetInt("minFillPercent", out c.MinFillPercent)
                    || !item.TryGetStringList("skeletonIds", out c.SkeletonIds))
                {
                    _lastError = "corridor_field_missing";
                    return false;
                }
                if (c.MatchIndex != i + 1)
                {
                    _lastError = "corridor_order_mismatch";
                    return false;
                }
                if (c.ThreatBudget != ModeHConfig.GetThreatBudget(c.MatchIndex)
                    || c.SimultaneousCap != ModeHConfig.GetSimultaneousEnemyCap(c.MatchIndex))
                {
                    _lastError = "corridor_conflicts_with_config:" + c.MatchIndex;
                    return false;
                }
                corridors.Add(c);
            }

            List<ModeHJsonValue> skeletonItems;
            if (!root.TryGetArray("skeletons", out skeletonItems) || skeletonItems.Count == 0)
            {
                _lastError = "skeletons_empty";
                return false;
            }
            List<ModeHSkeletonSpec> skeletons = new List<ModeHSkeletonSpec>(skeletonItems.Count);
            HashSet<string> skeletonIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < skeletonItems.Count; i++)
            {
                ModeHJsonValue item = skeletonItems[i];
                ModeHSkeletonSpec s = new ModeHSkeletonSpec();
                if (item == null || !item.TryGetString("skeletonId", out s.SkeletonId)
                    || !skeletonIds.Add(s.SkeletonId))
                {
                    _lastError = "skeleton_id_invalid";
                    return false;
                }
                item.TryGetString("nameKey", out s.NameKey);
                item.TryGetInt("minUnits", out s.MinUnits);
                item.TryGetInt("maxUnits", out s.MaxUnits);
                item.TryGetStringList("publicTags", out s.PublicTags);
                item.TryGetBool("hasHighThreatCore", out s.HasHighThreatCore);
                item.TryGetInt("woundedUnits", out s.WoundedUnits);
                item.TryGetBool("requiresEchoReturn", out s.RequiresEchoReturn);
                if (s.MinUnits <= 0 || s.MaxUnits < s.MinUnits)
                {
                    _lastError = "skeleton_units_invalid:" + s.SkeletonId;
                    return false;
                }
                skeletons.Add(s);
            }
            for (int i = 0; i < corridors.Count; i++)
            {
                List<string> ids = corridors[i].SkeletonIds;
                for (int j = 0; j < ids.Count; j++)
                {
                    if (!skeletonIds.Contains(ids[j]))
                    {
                        _lastError = "corridor_skeleton_unknown:" + ids[j];
                        return false;
                    }
                }
            }

            List<ModeHJsonValue> entryItems;
            if (!root.TryGetArray("entryScripts", out entryItems) || entryItems.Count == 0)
            {
                _lastError = "entry_scripts_empty";
                return false;
            }
            List<ModeHEntryScriptSpec> entryScripts = new List<ModeHEntryScriptSpec>(entryItems.Count);
            for (int i = 0; i < entryItems.Count; i++)
            {
                ModeHJsonValue item = entryItems[i];
                ModeHEntryScriptSpec e = new ModeHEntryScriptSpec();
                if (item == null || !item.TryGetString("entryScriptId", out e.EntryScriptId))
                {
                    _lastError = "entry_script_id_missing";
                    return false;
                }
                item.TryGetString("nameKey", out e.NameKey);
                item.TryGetString("hintKey", out e.HintKey);
                item.TryGetStringList("publicTags", out e.PublicTags);
                item.TryGetBool("coreEntersLast", out e.CoreEntersLast);
                item.TryGetBool("hiddenSeat", out e.HiddenSeat);
                List<ModeHJsonValue> batch;
                if (!item.TryGetArray("batchPattern", out batch) || batch.Count == 0)
                {
                    _lastError = "entry_script_batch_missing:" + e.EntryScriptId;
                    return false;
                }
                e.BatchPattern = new List<int>(batch.Count);
                for (int j = 0; j < batch.Count; j++)
                {
                    if (batch[j] == null || batch[j].Kind != ModeHJsonKind.Integer || batch[j].IntegerValue <= 0)
                    {
                        _lastError = "entry_script_batch_invalid:" + e.EntryScriptId;
                        return false;
                    }
                    e.BatchPattern.Add((int)batch[j].IntegerValue);
                }
                entryScripts.Add(e);
            }

            List<ModeHJsonValue> conditionItems;
            if (!root.TryGetArray("arenaConditions", out conditionItems) || conditionItems.Count == 0)
            {
                _lastError = "arena_conditions_empty";
                return false;
            }
            List<ModeHArenaConditionSpec> conditions = new List<ModeHArenaConditionSpec>(conditionItems.Count);
            for (int i = 0; i < conditionItems.Count; i++)
            {
                ModeHJsonValue item = conditionItems[i];
                ModeHArenaConditionSpec c = new ModeHArenaConditionSpec();
                if (item == null || !item.TryGetString("conditionId", out c.ConditionId))
                {
                    _lastError = "arena_condition_id_missing";
                    return false;
                }
                item.TryGetString("nameKey", out c.NameKey);
                item.TryGetStringList("publicTags", out c.PublicTags);
                item.TryGetStringList("favoredArchetypeIds", out c.FavoredArchetypeIds);
                item.TryGetStringList("disfavoredArchetypeIds", out c.DisfavoredArchetypeIds);
                conditions.Add(c);
            }

            List<ModeHJsonValue> capabilityItems;
            if (!root.TryGetArray("archetypeCapabilityMatrix", out capabilityItems)
                || capabilityItems.Count != ModeHStableIds.AllArchetypes.Length)
            {
                _lastError = "archetype_capability_count_mismatch";
                return false;
            }
            List<ModeHArchetypeCapability> capabilities =
                new List<ModeHArchetypeCapability>(capabilityItems.Count);
            for (int i = 0; i < capabilityItems.Count; i++)
            {
                ModeHJsonValue item = capabilityItems[i];
                ModeHArchetypeCapability c = new ModeHArchetypeCapability();
                if (item == null || !item.TryGetString("archetypeId", out c.ArchetypeId)
                    || !IsKnown(ModeHStableIds.AllArchetypes, c.ArchetypeId))
                {
                    _lastError = "archetype_capability_unknown";
                    return false;
                }
                item.TryGetStringList("primaryAnswers", out c.PrimaryAnswers);
                item.TryGetStringList("hardLockedBy", out c.HardLockedBy);
                capabilities.Add(c);
            }

            _corridors = corridors;
            _skeletons = skeletons;
            _entryScripts = entryScripts;
            _arenaConditions = conditions;
            _archetypeCapabilities = capabilities;
            return true;
        }

        #endregion

        #region 解析：OddsWeights（唯一允许同版本内置 fallback 的纯数值表）

        private static bool ParseOddsWeights(ModeHJsonValue root)
        {
            if (TryParseOddsWeightsCore(root))
            {
                _usedOddsFallback = false;
                return true;
            }
            // 纯数值权重允许同版本内置 fallback（§23.2）
            ApplyBuiltInOddsFallback();
            _usedOddsFallback = true;
            return true;
        }

        private static bool TryParseOddsWeightsCore(ModeHJsonValue root)
        {
            List<ModeHJsonValue> tierItems;
            if (!root.TryGetArray("oddsTiers", out tierItems) || tierItems.Count != 5) return false;
            List<ModeHOddsTier> tiers = new List<ModeHOddsTier>(5);
            for (int i = 0; i < tierItems.Count; i++)
            {
                ModeHJsonValue item = tierItems[i];
                ModeHOddsTier tier = new ModeHOddsTier();
                if (item == null
                    || !item.TryGetInt("odds", out tier.Odds)
                    || !item.TryGetInt("minPublicEdge", out tier.MinPublicEdge)
                    || !item.TryGetInt("maxPublicEdge", out tier.MaxPublicEdge))
                {
                    return false;
                }
                item.TryGetString("toneKey", out tier.ToneKey);
                if (tier.Odds != i + 1) return false;
                tiers.Add(tier);
            }

            ModeHJsonValue player;
            ModeHJsonValue enemy;
            if (!root.TryGetObject("playerWeights", out player)) return false;
            if (!root.TryGetObject("enemyWeights", out enemy)) return false;

            List<ModeHJsonValue> matrixItems;
            if (!root.TryGetArray("archetypeMatrix", out matrixItems) || matrixItems.Count == 0) return false;
            List<string> pairs = new List<string>(matrixItems.Count);
            for (int i = 0; i < matrixItems.Count; i++)
            {
                ModeHJsonValue item = matrixItems[i];
                string attacker;
                string defender;
                if (item == null || !item.TryGetString("attacker", out attacker)
                    || !item.TryGetString("defender", out defender))
                {
                    return false;
                }
                if (!IsKnown(ModeHStableIds.AllArchetypes, attacker)
                    || !IsKnown(ModeHStableIds.AllArchetypes, defender))
                {
                    return false;
                }
                pairs.Add(attacker + ">" + defender);
            }

            List<ModeHJsonValue> mapItems;
            if (!root.TryGetArray("commandTagMap", out mapItems)
                || mapItems.Count != ModeHStableIds.AllCommonCommands.Length)
            {
                return false;
            }
            List<ModeHCommandTagMapping> map = new List<ModeHCommandTagMapping>(mapItems.Count);
            for (int i = 0; i < mapItems.Count; i++)
            {
                ModeHJsonValue item = mapItems[i];
                ModeHCommandTagMapping m = new ModeHCommandTagMapping();
                if (item == null || !item.TryGetString("commandId", out m.CommandId)) return false;
                if (!IsKnown(ModeHStableIds.AllCommonCommands, m.CommandId)) return false;
                item.TryGetStringList("alignedTags", out m.AlignedTags);
                item.TryGetStringList("conflictedTags", out m.ConflictedTags);
                if (m.AlignedTags == null) m.AlignedTags = new List<string>();
                if (m.ConflictedTags == null) m.ConflictedTags = new List<string>();
                map.Add(m);
            }

            List<ModeHJsonValue> vectorItems;
            if (!root.TryGetArray("testVectors", out vectorItems) || vectorItems.Count < 3) return false;
            List<ModeHOddsTestVector> vectors = new List<ModeHOddsTestVector>(vectorItems.Count);
            for (int i = 0; i < vectorItems.Count; i++)
            {
                ModeHJsonValue item = vectorItems[i];
                ModeHOddsTestVector v = new ModeHOddsTestVector();
                if (item == null
                    || !item.TryGetString("vectorId", out v.VectorId)
                    || !item.TryGetInt("playerPublicScore", out v.PlayerPublicScore)
                    || !item.TryGetInt("enemyPublicScore", out v.EnemyPublicScore)
                    || !item.TryGetInt("expectedOdds", out v.ExpectedOdds))
                {
                    return false;
                }
                if (ModeHStateModel.ResolveOddsTier(v.PlayerPublicScore - v.EnemyPublicScore) != v.ExpectedOdds)
                {
                    return false;
                }
                vectors.Add(v);
            }

            _oddsTiers = tiers;
            _playerWeights = player;
            _enemyWeights = enemy;
            _archetypeMatrixPairs = pairs;
            _commandTagMap = map;
            _oddsTestVectors = vectors;
            return true;
        }

        private static void ApplyBuiltInOddsFallback()
        {
            _oddsTiers = new List<ModeHOddsTier>();
            _oddsTiers.Add(MakeTier(1, ModeHConfig.OddsThresholdX1MinEdge, 9999));
            _oddsTiers.Add(MakeTier(2, ModeHConfig.OddsThresholdX2MinEdge, ModeHConfig.OddsThresholdX1MinEdge - 1));
            _oddsTiers.Add(MakeTier(3, ModeHConfig.OddsThresholdX3MinEdge, ModeHConfig.OddsThresholdX2MinEdge - 1));
            _oddsTiers.Add(MakeTier(4, ModeHConfig.OddsThresholdX4MinEdge, ModeHConfig.OddsThresholdX3MinEdge - 1));
            _oddsTiers.Add(MakeTier(5, -9999, ModeHConfig.OddsThresholdX4MinEdge - 1));

            ModeHJsonValue player = ModeHJsonValue.NewObject();
            player.AddProperty("relayAvailable", ModeHJsonValue.NewInteger(5));
            player.AddProperty("relayEmpty", ModeHJsonValue.NewInteger(-12));
            player.AddProperty("starterCounters", ModeHJsonValue.NewInteger(8));
            player.AddProperty("starterCountered", ModeHJsonValue.NewInteger(-8));
            player.AddProperty("relayCounters", ModeHJsonValue.NewInteger(4));
            player.AddProperty("relayCountered", ModeHJsonValue.NewInteger(-4));
            player.AddProperty("kitQualityTotalCap", ModeHJsonValue.NewInteger(12));
            player.AddProperty("equipmentTagCounters", ModeHJsonValue.NewInteger(4));
            player.AddProperty("equipmentTagCountered", ModeHJsonValue.NewInteger(-4));
            player.AddProperty("starterInjured", ModeHJsonValue.NewInteger(-5));
            player.AddProperty("relayInjured", ModeHJsonValue.NewInteger(-3));
            player.AddProperty("anomalyBlood", ModeHJsonValue.NewInteger(-5));
            player.AddProperty("anomalyCrowd", ModeHJsonValue.NewInteger(-7));
            player.AddProperty("anomalyStrong", ModeHJsonValue.NewInteger(-4));
            player.AddProperty("anomalyError", ModeHJsonValue.NewInteger(-2));
            player.AddProperty("scarBenefit", ModeHJsonValue.NewInteger(3));
            player.AddProperty("scarCost", ModeHJsonValue.NewInteger(-3));
            player.AddProperty("scarTotalMin", ModeHJsonValue.NewInteger(-8));
            player.AddProperty("scarTotalMax", ModeHJsonValue.NewInteger(8));
            player.AddProperty("commandAligned", ModeHJsonValue.NewInteger(4));
            player.AddProperty("commandConflicted", ModeHJsonValue.NewInteger(-3));
            player.AddProperty("signatureCommandStarter", ModeHJsonValue.NewInteger(5));
            player.AddProperty("signatureCommandRelay", ModeHJsonValue.NewInteger(2));
            player.AddProperty("arenaFavorable", ModeHJsonValue.NewInteger(4));
            player.AddProperty("arenaUnfavorable", ModeHJsonValue.NewInteger(-4));
            ModeHJsonValue kitQuality = ModeHJsonValue.NewArray();
            int[] qualityScores = new int[] { 0, 1, 2, 3, 4, 5, 5, 5 };
            for (int i = 0; i < qualityScores.Length; i++)
            {
                kitQuality.Items.Add(ModeHJsonValue.NewInteger(qualityScores[i]));
            }
            player.AddProperty("kitQualityByGameQuality", kitQuality);
            _playerWeights = player;

            ModeHJsonValue enemy = ModeHJsonValue.NewObject();
            ModeHJsonValue stage = ModeHJsonValue.NewArray();
            int[] stageScores = new int[] { 0, 2, 5, 8, 12, 16 };
            for (int i = 0; i < stageScores.Length; i++)
            {
                stage.Items.Add(ModeHJsonValue.NewInteger(stageScores[i]));
            }
            enemy.AddProperty("stageByMatchIndex", stage);
            ModeHJsonValue counts = ModeHJsonValue.NewArray();
            int[] countScores = new int[] { 0, 4, 8 };
            for (int i = 0; i < countScores.Length; i++)
            {
                counts.Items.Add(ModeHJsonValue.NewInteger(countScores[i]));
            }
            enemy.AddProperty("countUpperBound", counts);
            enemy.AddProperty("highThreatCore", ModeHJsonValue.NewInteger(10));
            enemy.AddProperty("synergyPerCategory", ModeHJsonValue.NewInteger(5));
            enemy.AddProperty("synergyCap", ModeHJsonValue.NewInteger(10));
            enemy.AddProperty("woundedEnemy", ModeHJsonValue.NewInteger(-5));
            enemy.AddProperty("anomalyBlood", ModeHJsonValue.NewInteger(-5));
            enemy.AddProperty("anomalyCrowd", ModeHJsonValue.NewInteger(-7));
            enemy.AddProperty("anomalyStrong", ModeHJsonValue.NewInteger(-4));
            enemy.AddProperty("anomalyError", ModeHJsonValue.NewInteger(-2));
            _enemyWeights = enemy;

            _archetypeMatrixPairs = new List<string>();
            _archetypeMatrixPairs.Add("assault>ranged");
            _archetypeMatrixPairs.Add("ranged>sustain");
            _archetypeMatrixPairs.Add("sustain>tank");
            _archetypeMatrixPairs.Add("tank>assault");
            _archetypeMatrixPairs.Add("finisher>sustain");
            _archetypeMatrixPairs.Add("ranged>finisher");

            _commandTagMap = new List<ModeHCommandTagMapping>();
            AddFallbackMapping("steady", new string[] { "early_burst", "coward_pressure" },
                new string[] { "healer_core", "late_reinforcement" });
            AddFallbackMapping("press", new string[] { "healer_core", "slow_start" },
                new string[] { "attrition", "danger_edge" });
            AddFallbackMapping("center", new string[] { "danger_edge" }, new string[0]);
            AddFallbackMapping("spread", new string[] { "crowd", "crossfire" }, new string[] { "single_core" });
            AddFallbackMapping("finish", new string[] { "wounded_core", "reinforcement" },
                new string[] { "escort_screen" });
            AddFallbackMapping("hold", new string[] { "late_reinforcement" }, new string[] { "early_burst" });
            AddFallbackMapping("guard", new string[] { "early_burst", "crossfire" }, new string[] { "healer_core" });
            AddFallbackMapping("all_in", new string[] { "healer_core", "slow_start" },
                new string[] { "attrition", "danger_edge" });

            _oddsTestVectors = new List<ModeHOddsTestVector>();
            AddFallbackVector("public_edge_19_to_x2", 23, 4, 2);
            AddFallbackVector("public_edge_minus47_to_x5", -21, 26, 5);
            AddFallbackVector("public_edge_minus1_to_x3", 13, 14, 3);
        }

        private static ModeHOddsTier MakeTier(int odds, int minEdge, int maxEdge)
        {
            ModeHOddsTier tier = new ModeHOddsTier();
            tier.Odds = odds;
            tier.MinPublicEdge = minEdge;
            tier.MaxPublicEdge = maxEdge;
            tier.ToneKey = ModeHConfig.LocalizationKeyPrefix + "OddsTone_x" + odds.ToString();
            return tier;
        }

        private static void AddFallbackMapping(string commandId, string[] aligned, string[] conflicted)
        {
            ModeHCommandTagMapping m = new ModeHCommandTagMapping();
            m.CommandId = commandId;
            m.AlignedTags = new List<string>(aligned);
            m.ConflictedTags = new List<string>(conflicted);
            _commandTagMap.Add(m);
        }

        private static void AddFallbackVector(string id, int playerScore, int enemyScore, int expected)
        {
            ModeHOddsTestVector v = new ModeHOddsTestVector();
            v.VectorId = id;
            v.PlayerPublicScore = playerScore;
            v.EnemyPublicScore = enemyScore;
            v.ExpectedOdds = expected;
            _oddsTestVectors.Add(v);
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
