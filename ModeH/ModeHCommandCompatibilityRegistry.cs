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

            _effectIdsByCommand = byCommand;
            _selfSettledEffectIds = selfSettled;
            _effectStatuses = new Dictionary<string, ModeHCommandCompatibilityStatus>(StringComparer.Ordinal);
            _matrixSignature = null;
            _validated = true;
            _lastError = null;
            return true;
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
                    List<string> knownEffects = GetEffectIds(command.commandId);
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
            return GetEffectStatus(stableKey, behaviorId) == ModeHCommandCompatibilityStatus.VerifiedBehavior;
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
        /// 构造用于持久化的逐 effect 状态快照（按 entryId ordinal 升序，由 canonical digest 再排一次）。
        /// </summary>
        public static List<ModeHBehaviorStatusDto> BuildBehaviorSnapshot(string stableKey)
        {
            List<ModeHBehaviorStatusDto> result = new List<ModeHBehaviorStatusDto>();
            if (_effectIdsByCommand == null) return result;

            List<string> commandIds = new List<string>(_effectIdsByCommand.Keys);
            commandIds.Sort(StringComparer.Ordinal);
            for (int i = 0; i < commandIds.Count; i++)
            {
                string commandId = commandIds[i];
                ModeHBehaviorStatusDto commandDto = new ModeHBehaviorStatusDto();
                commandDto.entryId = commandId;
                commandDto.entryKind = "command";
                commandDto.status = (int)GetCommandStatus(stableKey, commandId);
                result.Add(commandDto);

                List<string> effectIds = _effectIdsByCommand[commandId];
                for (int j = 0; j < effectIds.Count; j++)
                {
                    ModeHBehaviorStatusDto effectDto = new ModeHBehaviorStatusDto();
                    effectDto.entryId = effectIds[j];
                    effectDto.entryKind = "effect";
                    effectDto.status = (int)GetEffectStatus(stableKey, effectIds[j]);
                    result.Add(effectDto);
                }
            }
            return result;
        }

        #endregion
    }
}
