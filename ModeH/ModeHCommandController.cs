using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode H 口令控制器（设计提案 §17.6、§25.1）。
    ///
    /// 冻结契约：
    /// - 赛前只能从通用口令、先发招牌口令、接力者招牌口令中锁定一条；
    /// - 战中每场只有一次拍铃，用 CAS 保证唯一性；拍铃立即触发预选口令，
    ///   不暂停战斗、不弹菜单；
    /// - 招牌口令只有其持有者在场时才响应；handoff 只有 matchRelay 实际接力后才有效；
    /// - 口令窗口结束、倒地、接力、技术中止、切图与 shutdown 都调用同一幂等 Restore；
    /// - ReportOnly / Unavailable 口令不得进入可选择列表。
    /// </summary>
    internal sealed class ModeHCommandController
    {
        #region 状态

        private readonly ModeHCommandAdapter _adapter = new ModeHCommandAdapter();
        private readonly ModeHCommandFireContext _fireContext = new ModeHCommandFireContext();

        private string _lockedCommandId;
        private string _lockedOwnerProfileId;
        private int _bellUsesRemaining;
        private bool _bellConsumed;
        private string _activeCommandId;
        private long _ownerToken;
        private string _lastError;

        #endregion

        #region 只读

        /// <summary>本场锁定的口令 ID。</summary>
        public string LockedCommandId { get { return _lockedCommandId; } }

        /// <summary>拍铃是否已消耗。</summary>
        public bool BellConsumed { get { return _bellConsumed; } }

        /// <summary>当前生效口令 ID（空表示无）。</summary>
        public string ActiveCommandId { get { return _activeCommandId; } }

        /// <summary>当前口令窗口剩余秒数。</summary>
        public float CommandWindowRemainingSeconds { get { return _adapter.WindowRemainingSeconds; } }

        /// <summary>是否仍可拍铃。</summary>
        public bool CanRingBell { get { return !_bellConsumed && _bellUsesRemaining > 0; } }

        /// <summary>最后一次失败原因。</summary>
        public string LastError { get { return _lastError; } }

        #endregion

        #region 赛前锁定

        /// <summary>
        /// 取本场可选口令：全部可用通用口令 + 先发/接力者的招牌口令。
        /// 只返回 VerifiedBehavior 或 PartiallyVerified 的条目（§17.6.4）。
        /// </summary>
        public static List<string> GetSelectableCommands(
            string starterStableKey, string relayStableKey,
            string starterSignatureCommandId, string relaySignatureCommandId)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < ModeHStableIds.AllCommonCommands.Length; i++)
            {
                string commandId = ModeHStableIds.AllCommonCommands[i];
                if (ModeHCommandCompatibilityRegistry.IsCommandSelectable(starterStableKey, commandId))
                {
                    result.Add(commandId);
                }
            }
            if (!string.IsNullOrEmpty(starterSignatureCommandId)
                && ModeHCommandCompatibilityRegistry.IsCommandSelectable(
                    starterStableKey, starterSignatureCommandId))
            {
                result.Add(starterSignatureCommandId);
            }
            if (!string.IsNullOrEmpty(relayStableKey)
                && !string.IsNullOrEmpty(relaySignatureCommandId)
                && ModeHCommandCompatibilityRegistry.IsCommandSelectable(
                    relayStableKey, relaySignatureCommandId))
            {
                if (!result.Contains(relaySignatureCommandId)) result.Add(relaySignatureCommandId);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>锁盘：冻结本场口令与拍铃次数。</summary>
        public bool LockCommand(string commandId, string ownerProfileId, long ownerToken, out string failureReasonId)
        {
            failureReasonId = null;
            if (string.IsNullOrEmpty(commandId))
            {
                failureReasonId = "command_lock_empty";
                return false;
            }
            _lockedCommandId = commandId;
            _lockedOwnerProfileId = ownerProfileId;
            _ownerToken = ownerToken;
            _bellUsesRemaining = ModeHConfig.BellUsesPerMatch;
            _bellConsumed = false;
            _activeCommandId = null;
            return true;
        }

        #endregion

        #region 拍铃

        /// <summary>拍铃失败原因 ID 到本地化键。未收录的 ID 落到通用文案。</summary>
        internal static string GetBellFailureLocalizationKey(string failureReasonId)
        {
            if (IsLocalizedBellFailure(failureReasonId))
            {
                return ModeHConfig.LocalizationKeyPrefix + "BellFailed_" + failureReasonId;
            }
            return ModeHConfig.LocalizationKeyPrefix + "BellFailed_Generic";
        }

        /// <summary>该拍铃失败 ID 是否有对应的已注入文案。</summary>
        internal static bool IsLocalizedBellFailure(string failureReasonId)
        {
            if (string.IsNullOrEmpty(failureReasonId)) return false;
            return failureReasonId == "command_owner_mismatch"
                || failureReasonId == "command_bell_consumed"
                || failureReasonId == "command_not_locked"
                || failureReasonId == "command_no_active_fighter"
                || failureReasonId == "command_spec_missing"
                || failureReasonId == "command_signature_owner_absent"
                || failureReasonId == "command_requires_relay"
                || failureReasonId == "command_requires_enemy_count"
                || failureReasonId == "command_lock_empty";
        }

        /// <summary>
        /// 拍铃：每场唯一一次，CAS 保证；招牌口令只有持有者在场时才响应。
        /// </summary>
        public bool TryRingBell(
            AICharacterController activeAi,
            string activeProfileId,
            float commandScale,
            Vector3 arenaCenter,
            DamageReceiver nearestEnemy,
            DamageReceiver lowestHealthEnemy,
            int enemyCount,
            long ownerToken,
            bool relayEntered,
            out string failureReasonId)
        {
            failureReasonId = null;

            if (ownerToken == 0 || ownerToken != _ownerToken)
            {
                failureReasonId = "command_owner_mismatch";
                return false;
            }
            // 下方任一 return false 的 reasonId 都必须在 GetBellFailureLocalizationKey
            // 与 ModeHLocalization 里有对应文案，否则玩家看到的是原始 ID。
            if (_bellConsumed || _bellUsesRemaining <= 0)
            {
                failureReasonId = "command_bell_consumed";
                return false;
            }
            if (string.IsNullOrEmpty(_lockedCommandId))
            {
                failureReasonId = "command_not_locked";
                return false;
            }
            if (activeAi == null)
            {
                failureReasonId = "command_no_active_fighter";
                return false;
            }

            ModeHCommandSpec spec = ResolveSpec(_lockedCommandId);
            if (spec == null)
            {
                failureReasonId = "command_spec_missing";
                return false;
            }

            // 招牌口令：只有其持有者在场时才响应
            if (spec.IsSignature)
            {
                if (!string.IsNullOrEmpty(_lockedOwnerProfileId)
                    && !string.Equals(_lockedOwnerProfileId, activeProfileId, StringComparison.Ordinal))
                {
                    failureReasonId = "command_signature_owner_absent";
                    return false;
                }
                if (spec.RequiresRelayEntered && !relayEntered)
                {
                    failureReasonId = "command_requires_relay";
                    return false;
                }
            }
            if (spec.RequiresEnemyCountAtLeast > 0 && enemyCount < spec.RequiresEnemyCountAtLeast)
            {
                failureReasonId = "command_requires_enemy_count";
                return false;
            }

            // CAS：先消耗次数，再应用；应用失败也不退还（每场唯一一次）
            _bellUsesRemaining--;
            _bellConsumed = true;

            _fireContext.ArenaCenter = arenaCenter;
            _fireContext.NearestEnemy = nearestEnemy;
            _fireContext.LowestHealthEnemy = lowestHealthEnemy;
            _fireContext.EnemyCount = enemyCount;

            if (!_adapter.Apply(activeAi, spec, commandScale, _fireContext, out failureReasonId))
            {
                _lastError = failureReasonId;
                return false;
            }

            _activeCommandId = spec.CommandId;
            ModBehaviour.DevLog("[ModeH] 拍铃触发口令: " + spec.CommandId);
            return true;
        }

        #endregion

        #region 每帧驱动

        /// <summary>推进窗口与重申循环。</summary>
        public void Tick(
            float deltaTime,
            Vector3 arenaCenter,
            DamageReceiver nearestEnemy,
            DamageReceiver lowestHealthEnemy,
            int enemyCount)
        {
            if (!_adapter.IsActive)
            {
                if (_activeCommandId != null && !_adapter.IsActive) _activeCommandId = null;
                return;
            }
            _fireContext.ArenaCenter = arenaCenter;
            _fireContext.NearestEnemy = nearestEnemy;
            _fireContext.LowestHealthEnemy = lowestHealthEnemy;
            _fireContext.EnemyCount = enemyCount;
            _adapter.Tick(deltaTime, _fireContext);
            if (!_adapter.IsActive) _activeCommandId = null;
        }

        /// <summary>逐 effect 保持率校验（供遥测与兼容矩阵写入）。</summary>
        public List<string> ValidateHeldEffects()
        {
            return _adapter.Validate();
        }

        #endregion

        #region 还原与恢复

        /// <summary>
        /// 幂等还原：窗口结束、倒地、接力、技术中止、切图与 shutdown 共用同一入口。
        /// </summary>
        public void RestoreAll()
        {
            _adapter.Restore();
            _activeCommandId = null;
        }

        /// <summary>从战场快照恢复窗口状态（§17.4）。</summary>
        public void RestoreFromSnapshot(
            string lockedCommandId, bool bellConsumed, string activeCommandId, float windowRemaining)
        {
            _lockedCommandId = lockedCommandId;
            _bellConsumed = bellConsumed;
            _bellUsesRemaining = bellConsumed ? 0 : ModeHConfig.BellUsesPerMatch;
            _activeCommandId = windowRemaining > 0f ? activeCommandId : null;
        }

        /// <summary>清空本场状态（进入下一场前）。</summary>
        public void ResetForNextMatch()
        {
            RestoreAll();
            _lockedCommandId = null;
            _lockedOwnerProfileId = null;
            _bellConsumed = false;
            _bellUsesRemaining = 0;
            _ownerToken = 0;
        }

        #endregion

        #region 查询

        private static ModeHCommandSpec ResolveSpec(string commandId)
        {
            List<ModeHCommandSpec> commands = ModeHContentCatalog.Commands;
            if (commands == null || string.IsNullOrEmpty(commandId)) return null;
            for (int i = 0; i < commands.Count; i++)
            {
                if (string.Equals(commands[i].CommandId, commandId, StringComparison.Ordinal))
                {
                    return commands[i];
                }
            }
            return null;
        }

        #endregion
    }
}
