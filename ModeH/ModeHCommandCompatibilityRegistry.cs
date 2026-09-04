using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H 口令兼容矩阵注册表（设计提案 §17.6.4、§25.1）。
    ///
    /// 冻结规则：
    /// - 兼容状态的键是 (gameBuildSignature, modBuildSignature, contentCatalogSignature,
    ///   stableKey, commandId, effectId)，不是 commandId 整体；
    /// - 口令级状态由 effect 派生：全通过=VerifiedBehavior，部分通过=PartiallyVerified，
    ///   控制点存在但全未通过=ReportOnly，控制点缺失=Unavailable；
    /// - Mode H 自结算 effect（如 steady.coward_mitigation）对任何 key 恒为 VerifiedBehavior，
    ///   但不得用它把整条口令标成 VerifiedBehavior；
    /// - 每个生产 stable key 至少 3 条通用口令达到 VerifiedBehavior 或 PartiallyVerified，
    ///   否则 IsModeHContentReady=false；
    /// - ReportOnly / Unavailable 不进入选择 UI 与赔率；未通过 effect 不进入候选卡文案。
    /// </summary>
    public static class ModeHCommandCompatibilityRegistry
    {
        #region 状态

        private static readonly object _lock = new object();
        private static bool _validated;
        private static bool _validationAttempted;
        private static string _lastError;

        /// <summary>(stableKey|effectId) -> 状态。运行时认证结果写入这里。</summary>
        private static Dictionary<string, ModeHCommandCompatibilityStatus> _effectStatuses;

        private static Dictionary<string, List<string>> _effectIdsByCommand;

        /// <summary>
        /// 伤病 / 战痕条目 ID -> 其**非自结算**分量 effectId（ordinal 升序）。
        ///
        /// 为什么单独一张表而不并进 _effectIdsByCommand：口令的条目 ID 与分量 ID 是
        /// `cmd` / `cmd.controlPoint` 的父子关系，伤病战痕也是（`leg` / `leg.sightDistance`），
        /// 但两族的条目 ID 不能混在同一张字典里——GetCommandStatus 只允许口令派生
        /// PartiallyVerified，伤病战痕按 §17.4 line 1118 不设该档（任一分量不可用整条不进抽池）。
        ///
        /// 自结算分量不进表：它们对任何 key 恒可用，参与条目级判定只会把结论稀释。
        /// </summary>
        private static Dictionary<string, List<string>> _effectIdsByBehaviorEntry;

        /// <summary>伤病 / 战痕条目 ID -> entryKind（"injury" / "scar"）。落盘 DTO 用。</summary>
        private static Dictionary<string, string> _behaviorEntryKinds;

        /// <summary>
        /// Mode H 自结算的 effect / 行为 ID。这里同时收两类东西：
        /// - `steady.coward_mitigation` 这种「口令的自结算分量」；
        /// - `blood` / `crowd` / `strong` / `error` 四个**公开异常 ID**。
        ///
        /// 异常为什么在这里：它们完全不写原版 AI 控制点，后果整个由 Mode H 自己结算
        /// （胆怯 = 整队弃赛，ERROR = 走 ControlOtherCharacter 的完整互换），
        /// 没有任何可实测的字段，因此对任何 stable key 恒为 VerifiedBehavior。
        /// 依据 §17.6.4 line 1308「三种胆怯对任何 key 恒为 VerifiedBehavior」。
        ///
        /// `error` 同列是 owner 裁决（2026-09-03）：§17.6.5 原本要一份逐角色白名单，
        /// 但互换自带 2 秒 deadline + 完整回滚（TickErrorSwap :528-533），
        /// 切换不成功就整体还原、比赛照常，运行时已经 fail-safe，白名单加不了安全性。
        /// 真正的实测改放 F3 验收（DebugAndTools/F3GameplayValidationModeHErrorSwap.cs）。
        /// </summary>
        private static HashSet<string> _selfSettledEffectIds;
        private static string _matrixSignature;

        #endregion

        #region 只读访问

        /// <summary>静态目录是否已就绪。</summary>
        public static bool IsValidated { get { return _validated; } }

        /// <summary>最后一次失败原因。</summary>
        public static string LastError { get { return _lastError; } }

        /// <summary>当前矩阵绑定的三签名组合。</summary>
        public static string MatrixSignature { get { return _matrixSignature; } }

        #endregion

        #region 生命周期

        /// <summary>清空缓存（含运行时认证结果）。</summary>
        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _validated = false;
                _validationAttempted = false;
                _lastError = null;
                _effectStatuses = null;
                _effectIdsByCommand = null;
                _effectIdsByBehaviorEntry = null;
                _behaviorEntryKinds = null;
                _selfSettledEffectIds = null;
                _matrixSignature = null;
            }
        }

        /// <summary>幂等加载 effect 目录并建立初始状态（自结算恒通过，其余待实测）。</summary>
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
                _lastError = "compatibility_registry_exception:" + e.GetType().Name;
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

            List<ModeHEffectSpec> catalog = ModeHContentCatalog.EffectCatalog;
            if (catalog == null || catalog.Count == 0)
            {
                _lastError = "effect_catalog_empty";
                return false;
            }

            Dictionary<string, List<string>> byCommand = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            HashSet<string> selfSettled = new HashSet<string>(StringComparer.Ordinal);
            List<string> selfSettledList = ModeHContentCatalog.SelfSettledEffectIds;
            if (selfSettledList != null)
            {
                for (int i = 0; i < selfSettledList.Count; i++)
                {
                    selfSettled.Add(selfSettledList[i]);
                }
            }

            List<ModeHCommandSpec> commands = ModeHContentCatalog.Commands;
            if (commands != null)
            {
                for (int i = 0; i < commands.Count; i++)
                {
                    ModeHCommandSpec command = commands[i];
                    if (command == null || command.Effects == null) continue;
                    List<string> effectIds = new List<string>(command.Effects.Count);
                    for (int j = 0; j < command.Effects.Count; j++)
                    {
                        effectIds.Add(command.Effects[j].EffectId);
                    }
                    effectIds.Sort(StringComparer.Ordinal);
                    byCommand[command.CommandId] = effectIds;
                }
            }

            // 伤病与战痕：与口令同构地建「条目 -> 非自结算分量」表。
            // 目录在本方法开头已由 EnsureLoaded 保证加载，这里直接读。
            Dictionary<string, List<string>> byBehaviorEntry =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Dictionary<string, string> behaviorEntryKinds =
                new Dictionary<string, string>(StringComparer.Ordinal);

            List<ModeHInjurySpec> injuries = ModeHContentCatalog.Injuries;
            if (injuries != null)
            {
                for (int i = 0; i < injuries.Count; i++)
                {
                    ModeHInjurySpec injury = injuries[i];
                    if (injury == null || string.IsNullOrEmpty(injury.InjuryId)) continue;
                    byBehaviorEntry[injury.InjuryId] = CollectEngineEffectIds(injury.Components);
                    behaviorEntryKinds[injury.InjuryId] = "injury";
                }
            }

            List<ModeHScarSpec> scars = ModeHContentCatalog.Scars;
            if (scars != null)
            {
                for (int i = 0; i < scars.Count; i++)
                {
                    ModeHScarSpec scar = scars[i];
                    if (scar == null || string.IsNullOrEmpty(scar.ScarId)) continue;
                    byBehaviorEntry[scar.ScarId] = CollectEngineEffectIds(scar.Components);
                    behaviorEntryKinds[scar.ScarId] = "scar";
                }
            }

            _effectIdsByCommand = byCommand;
            _effectIdsByBehaviorEntry = byBehaviorEntry;
            _behaviorEntryKinds = behaviorEntryKinds;
            _selfSettledEffectIds = selfSettled;
            _effectStatuses = new Dictionary<string, ModeHCommandCompatibilityStatus>(StringComparer.Ordinal);
            _matrixSignature = null;
            _validated = true;
            _lastError = null;
            return true;
        }

        /// <summary>
        /// 取一组分量里**需要实测**的那些 effectId（ordinal 升序）。
        /// 自结算分量被排除：它们不写原版字段，没有可测量的对象，
        /// 恒可用，参与条目级判定只会把结论稀释。
        /// </summary>
        private static List<string> CollectEngineEffectIds(List<ModeHEffectSpec> components)
        {
            List<string> ids = new List<string>();
            if (components == null) return ids;
            for (int i = 0; i < components.Count; i++)
            {
                ModeHEffectSpec component = components[i];
                if (component == null || component.SelfSettled) continue;
                if (string.IsNullOrEmpty(component.EffectId)) continue;
                ids.Add(component.EffectId);
            }
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        #endregion

        #region 认证结果写入

        /// <summary>
        /// 绑定当前三签名组合。签名变化时清空已记录的实测结果，
        /// 避免把上一次构建的结论当作本次构建的证据。
        /// </summary>
        public static void BindBuildSignature(
            string gameBuildSignature, string modBuildSignature, string contentCatalogSignature)
        {
            string signature = (gameBuildSignature != null ? gameBuildSignature : string.Empty)
                + "|" + (modBuildSignature != null ? modBuildSignature : string.Empty)
                + "|" + (contentCatalogSignature != null ? contentCatalogSignature : string.Empty);
            lock (_lock)
            {
                if (!string.Equals(_matrixSignature, signature, StringComparison.Ordinal))
                {
                    _matrixSignature = signature;
                    _effectStatuses = new Dictionary<string, ModeHCommandCompatibilityStatus>(StringComparer.Ordinal);
                }
            }
        }

        /// <summary>写入一条逐 effect 实测结论（只由生产认证调用）。</summary>
        public static void RecordEffectStatus(
            string stableKey, string effectId, ModeHCommandCompatibilityStatus status)
        {
            if (string.IsNullOrEmpty(stableKey) || string.IsNullOrEmpty(effectId)) return;
            if (_effectStatuses == null)
            {
                _effectStatuses = new Dictionary<string, ModeHCommandCompatibilityStatus>(StringComparer.Ordinal);
            }
            _effectStatuses[MakeKey(stableKey, effectId)] = status;
        }

        /// <summary>清空某个 stable key 的全部实测结论（认证重跑用）。</summary>
        public static void ClearStableKey(string stableKey)
        {
            if (_effectStatuses == null || string.IsNullOrEmpty(stableKey)) return;
            string prefix = stableKey + "|";
            List<string> doomed = new List<string>();
            foreach (KeyValuePair<string, ModeHCommandCompatibilityStatus> pair in _effectStatuses)
            {
                if (pair.Key != null && pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    doomed.Add(pair.Key);
                }
            }
            for (int i = 0; i < doomed.Count; i++)
            {
                _effectStatuses.Remove(doomed[i]);
            }
        }

        private static string MakeKey(string stableKey, string effectId)
        {
            return stableKey + "|" + effectId;
        }

        /// <summary>三签名匹配后恢复逐 effect 证据；口令级聚合状态必须重新派生，不能直接信任缓存字段。</summary>
        internal static void RestoreCertificationEffects(List<ModeHPresetCertificationRecordDto> records)
        {
            if (!EnsureValidated() || records == null) return;
            for (int i = 0; i < records.Count; i++)
            {
                ModeHPresetCertificationRecordDto record = records[i];
                if (record == null || string.IsNullOrEmpty(record.stableKey)) continue;
                ClearStableKey(record.stableKey);
                if (record.status != (int)ModeHCertificationStatus.Passed || record.commandStatuses == null) continue;
                for (int j = 0; j < record.commandStatuses.Count; j++)
                {
                    ModeHCommandCertificationStatusDto command = record.commandStatuses[j];
                    if (command == null || command.effectStatuses == null) continue;
                    // 条目级查询：commandId 字段现在同时承载口令 ID 与伤病 / 战痕条目 ID
                    // （见 BuildCommandStatuses）。仍用 GetEffectIds 的话，
                    // 伤病战痕那批行会因为「不是已知口令」被整批丢弃——
                    // 缓存命中的那一局伤病又变无名、战痕又无候选，而口令层看起来毫无异常。
                    List<string> knownEffects = GetBehaviorEffectIds(command.commandId);
                    if (knownEffects == null) continue;
                    for (int k = 0; k < command.effectStatuses.Count; k++)
                    {
                        ModeHBehaviorStatusDto effect = command.effectStatuses[k];
                        if (effect == null || effect.entryKind != "effect" || !knownEffects.Contains(effect.entryId)
                            || effect.status < (int)ModeHCommandCompatibilityStatus.Unknown
                            || effect.status > (int)ModeHCommandCompatibilityStatus.Unavailable) continue;
                        RecordEffectStatus(record.stableKey, effect.entryId, (ModeHCommandCompatibilityStatus)effect.status);
                    }
                }
            }
        }

        #endregion

        #region 状态查询与派生

        /// <summary>
        /// 取一条 effect 在指定 stable key 上的状态。
        /// Mode H 自结算 effect 恒为 VerifiedBehavior；未实测的 AI 侧 effect 为 Unknown。
        /// </summary>
        public static ModeHCommandCompatibilityStatus GetEffectStatus(string stableKey, string effectId)
        {
            if (string.IsNullOrEmpty(effectId)) return ModeHCommandCompatibilityStatus.Unavailable;
            if (_selfSettledEffectIds != null && _selfSettledEffectIds.Contains(effectId))
            {
                return ModeHCommandCompatibilityStatus.VerifiedBehavior;
            }
            if (_effectStatuses == null || string.IsNullOrEmpty(stableKey))
            {
                return ModeHCommandCompatibilityStatus.Unknown;
            }
            ModeHCommandCompatibilityStatus status;
            if (_effectStatuses.TryGetValue(MakeKey(stableKey, effectId), out status)) return status;
            return ModeHCommandCompatibilityStatus.Unknown;
        }

        /// <summary>
        /// 由 effect 状态派生口令级状态（§17.6.4）：
        /// 全部通过 -> VerifiedBehavior；部分通过 -> PartiallyVerified；
        /// 控制点存在但全未通过 -> ReportOnly；控制点缺失（全部 Unavailable）-> Unavailable。
        /// </summary>
        public static ModeHCommandCompatibilityStatus GetCommandStatus(string stableKey, string commandId)
        {
            List<string> effectIds = GetEffectIds(commandId);
            if (effectIds == null || effectIds.Count == 0) return ModeHCommandCompatibilityStatus.Unavailable;

            int passed = 0;
            int unavailable = 0;
            for (int i = 0; i < effectIds.Count; i++)
            {
                ModeHCommandCompatibilityStatus status = GetEffectStatus(stableKey, effectIds[i]);
                if (status == ModeHCommandCompatibilityStatus.VerifiedBehavior) passed++;
                else if (status == ModeHCommandCompatibilityStatus.Unavailable) unavailable++;
            }

            if (passed == effectIds.Count) return ModeHCommandCompatibilityStatus.VerifiedBehavior;
            if (passed > 0) return ModeHCommandCompatibilityStatus.PartiallyVerified;
            if (unavailable == effectIds.Count) return ModeHCommandCompatibilityStatus.Unavailable;
            return ModeHCommandCompatibilityStatus.ReportOnly;
        }

        /// <summary>该口令是否可以出现在选令 UI 与赔率里。</summary>
        public static bool IsCommandSelectable(string stableKey, string commandId)
        {
            ModeHCommandCompatibilityStatus status = GetCommandStatus(stableKey, commandId);
            return status == ModeHCommandCompatibilityStatus.VerifiedBehavior
                || status == ModeHCommandCompatibilityStatus.PartiallyVerified;
        }

        /// <summary>取一条口令的全部 effectId（ordinal 升序）。</summary>
        public static List<string> GetEffectIds(string commandId)
        {
            if (_effectIdsByCommand == null || string.IsNullOrEmpty(commandId)) return null;
            List<string> effectIds;
            return _effectIdsByCommand.TryGetValue(commandId, out effectIds) ? effectIds : null;
        }

        /// <summary>全部伤病 / 战痕条目 ID（ordinal 升序）。缓存落盘与诊断遍历用。</summary>
        public static List<string> GetBehaviorEntryIds()
        {
            List<string> ids = new List<string>();
            if (_effectIdsByBehaviorEntry == null) return ids;
            foreach (KeyValuePair<string, List<string>> pair in _effectIdsByBehaviorEntry)
            {
                ids.Add(pair.Key);
            }
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>条目的 entryKind："command" / "injury" / "scar"；未知返回空串。</summary>
        public static string GetBehaviorEntryKind(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return string.Empty;
            if (_effectIdsByCommand != null && _effectIdsByCommand.ContainsKey(entryId)) return "command";
            string kind;
            if (_behaviorEntryKinds != null && _behaviorEntryKinds.TryGetValue(entryId, out kind)) return kind;
            return string.Empty;
        }

        /// <summary>
        /// 取一条条目的实测分量：口令取其 effects，伤病 / 战痕取其非自结算分量。
        ///
        /// 口令优先查：两族条目 ID 之间不存在碰撞（口令 ID 与伤病战痕 ID 分属
        /// ModeHStableIds 的不同冻结表），这里的顺序只为让口令走最短路径。
        /// </summary>
        public static List<string> GetBehaviorEffectIds(string entryId)
        {
            List<string> effectIds = GetEffectIds(entryId);
            if (effectIds != null) return effectIds;
            if (_effectIdsByBehaviorEntry == null || string.IsNullOrEmpty(entryId)) return null;
            return _effectIdsByBehaviorEntry.TryGetValue(entryId, out effectIds) ? effectIds : null;
        }

        /// <summary>
        /// 条目级状态。口令沿用既有派生；伤病 / 战痕**不设 PartiallyVerified**——
        /// §17.4 line 1118：任一分量对该 key 不可用，整条就不进抽池，
        /// 不允许「收益生效、代价失效」或反之。
        ///
        /// 全部分量都是自结算（如伤病 armor / spirit）时分量表为空，恒 VerifiedBehavior：
        /// 这类条目不写原版字段，没有可失败的对象。
        ///
        /// 都不是条目 ID 时回落到 effect 级查询，让四个公开异常这种「裸 ID」
        /// 能经 _selfSettledEffectIds 拿到 VerifiedBehavior。
        /// </summary>
        public static ModeHCommandCompatibilityStatus GetBehaviorEntryStatus(string stableKey, string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return ModeHCommandCompatibilityStatus.Unavailable;

            if (_effectIdsByCommand != null && _effectIdsByCommand.ContainsKey(entryId))
            {
                return GetCommandStatus(stableKey, entryId);
            }

            List<string> componentIds;
            if (_effectIdsByBehaviorEntry != null
                && _effectIdsByBehaviorEntry.TryGetValue(entryId, out componentIds))
            {
                if (componentIds == null || componentIds.Count == 0)
                {
                    return ModeHCommandCompatibilityStatus.VerifiedBehavior;
                }
                for (int i = 0; i < componentIds.Count; i++)
                {
                    if (GetEffectStatus(stableKey, componentIds[i])
                        != ModeHCommandCompatibilityStatus.VerifiedBehavior)
                    {
                        return ModeHCommandCompatibilityStatus.ReportOnly;
                    }
                }
                return ModeHCommandCompatibilityStatus.VerifiedBehavior;
            }

            return GetEffectStatus(stableKey, entryId);
        }

        /// <summary>
        /// 候选卡/选令文案只能由已通过的 effect 生成（§17.6.4）：
        /// 返回该口令中状态为 VerifiedBehavior 的 effectId。
        /// </summary>
        public static List<string> GetVerifiedEffectIds(string stableKey, string commandId)
        {
            List<string> result = new List<string>();
            List<string> effectIds = GetEffectIds(commandId);
            if (effectIds == null) return result;
            for (int i = 0; i < effectIds.Count; i++)
            {
                if (GetEffectStatus(stableKey, effectIds[i]) == ModeHCommandCompatibilityStatus.VerifiedBehavior)
                {
                    result.Add(effectIds[i]);
                }
            }
            return result;
        }

        /// <summary>该 stable key 当前可用的通用口令数量。</summary>
        public static int CountUsableCommonCommands(string stableKey)
        {
            int count = 0;
            for (int i = 0; i < ModeHStableIds.AllCommonCommands.Length; i++)
            {
                if (IsCommandSelectable(stableKey, ModeHStableIds.AllCommonCommands[i])) count++;
            }
            return count;
        }

        /// <summary>该 stable key 是否满足“至少 3 条可用通用口令”的内容门槛。</summary>
        public static bool MeetsCommandGate(string stableKey)
        {
            return CountUsableCommonCommands(stableKey) >= ModeHConfig.MinUsableCommonCommandsPerKey;
        }

        /// <summary>
        /// 通用行为查询：伤病/战痕/异常与 effect 共用同一张实测表，
        /// 只有 VerifiedBehavior 才允许进入战斗结算与赔率（§17.5、§17.6.4）。
        /// </summary>
        public static bool HasVerifiedBehavior(string stableKey, string behaviorId)
        {
            if (string.IsNullOrEmpty(stableKey) || string.IsNullOrEmpty(behaviorId)) return false;
            // 走条目级查询：behaviorId 既可能是分量 ID（leg.sightDistance，
            // ModeHInjuryAndScarSystem.IsEntryUsableForKey 逐分量查这一种），
            // 也可能是条目 ID（leg，HasVerifiedInjuryBehavior 查这一种），
            // 还可能是裸异常 ID（error，经自结算集合命中）。三种都要能答。
            return GetBehaviorEntryStatus(stableKey, behaviorId)
                == ModeHCommandCompatibilityStatus.VerifiedBehavior;
        }

        /// <summary>该 stable key 是否至少有一条伤病行为通过实测（敌方带伤分的前置）。</summary>
        public static bool HasVerifiedInjuryBehavior(string stableKey)
        {
            List<ModeHInjurySpec> injuries = ModeHContentCatalog.Injuries;
            if (injuries == null || string.IsNullOrEmpty(stableKey)) return false;
            for (int i = 0; i < injuries.Count; i++)
            {
                if (injuries[i] == null) continue;
                if (HasVerifiedBehavior(stableKey, injuries[i].InjuryId)) return true;
            }
            return false;
        }

        /// <summary>该 stable key 的指定公开异常是否通过实测（异常分的前置）。</summary>
        public static bool HasVerifiedAnomalyBehavior(string stableKey, string anomalyId)
        {
            return HasVerifiedBehavior(stableKey, anomalyId);
        }

        /// <summary>
        /// 构造随选手档案落盘的行为状态快照（按 entryId ordinal 升序，canonical digest 会再排一次）。
        ///
        /// 【只产出赔率真正查询的三类】伤病、战痕、公开异常。
        /// ModeHOddsController.IsVerified 只按 profile.injuryId / anomalyId / scarId 三种
        /// 去 behaviorStatuses 里找，塞进 13 条口令 + 35 条 effect 只会让每个选手档案
        /// 白白胖 48 行，还全都没人查。
        ///
        /// 【绝不在读档时刷新】本表进赛季 canonical digest，抽签时一次写定。
        /// 事后改写会让已存赛季 VerifyDigest 失败并进写屏障——
        /// 这也是老赛季保持空表、不追溯改赔率的原因。
        /// </summary>
        public static List<ModeHBehaviorStatusDto> BuildBehaviorSnapshot(string stableKey)
        {
            List<ModeHBehaviorStatusDto> result = new List<ModeHBehaviorStatusDto>();
            if (string.IsNullOrEmpty(stableKey)) return result;

            List<string> entryIds = GetBehaviorEntryIds();
            for (int i = 0; i < entryIds.Count; i++)
            {
                string entryId = entryIds[i];
                ModeHBehaviorStatusDto dto = new ModeHBehaviorStatusDto();
                dto.entryId = entryId;
                dto.entryKind = GetBehaviorEntryKind(entryId);
                dto.status = (int)GetBehaviorEntryStatus(stableKey, entryId);
                result.Add(dto);
            }

            string[] anomalies = ModeHStableIds.AllAnomalies;
            if (anomalies != null)
            {
                for (int i = 0; i < anomalies.Length; i++)
                {
                    string anomalyId = anomalies[i];
                    if (string.IsNullOrEmpty(anomalyId)) continue;
                    ModeHBehaviorStatusDto dto = new ModeHBehaviorStatusDto();
                    dto.entryId = anomalyId;
                    dto.entryKind = "anomaly";
                    dto.status = (int)GetBehaviorEntryStatus(stableKey, anomalyId);
                    result.Add(dto);
                }
            }

            result.Sort(CompareBehaviorStatusByEntryId);
            return result;
        }

        /// <summary>按 entryId ordinal 升序。显式比较器：C# 7.3 下不用 lambda 省一次闭包分配。</summary>
        private static int CompareBehaviorStatusByEntryId(ModeHBehaviorStatusDto a, ModeHBehaviorStatusDto b)
        {
            string left = a != null && a.entryId != null ? a.entryId : string.Empty;
            string right = b != null && b.entryId != null ? b.entryId : string.Empty;
            return string.CompareOrdinal(left, right);
        }

        #endregion
    }
}
