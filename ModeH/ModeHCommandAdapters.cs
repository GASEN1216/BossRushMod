using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 一条正在生效的字段调制记录：保存原值以便幂等还原（设计提案 §17.6.1）。
    /// </summary>
    internal sealed class ModeHControlPointModulation
    {
        /// <summary>effectId。</summary>
        public string EffectId;
        /// <summary>控制点 ID。</summary>
        public string ControlPointId;
        /// <summary>原浮点值。</summary>
        public float OriginalFloat;
        /// <summary>原 Vector2 值（skillCoolTimeRange）。</summary>
        public Vector2 OriginalVector;
        /// <summary>原布尔值。</summary>
        public bool OriginalBool;
        /// <summary>调制后的目标值（重申时写回）。</summary>
        public float TargetFloat;
        /// <summary>调制后的目标 Vector2。</summary>
        public Vector2 TargetVector;
        /// <summary>调制后的目标布尔。</summary>
        public bool TargetBool;
        /// <summary>是否需要在窗口结束时还原（nextReleaseSkillTimeMarker 固定 false）。</summary>
        public bool Restore;
    }

    /// <summary>
    /// Mode H 口令 adapter（设计提案 §17.6.1、§17.6.2、§25.1）。
    ///
    /// 冻结实现模型：口令 = 窗口内持续重申的实例字段调制 + 可选重申式点火。
    /// - 一次性写值必然失效：TraceTarget 每 0.15 秒重发 MoveToPos，
    ///   SearchEnemyAround.OnUpdate 持续重搜并回写目标；
    /// - 因此每条 adapter 由 Apply（保存原值并写入调制值）、Reassert（每 0.1 秒重申，
    ///   严格高于行为树 0.15 秒的重算频率）、Restore（按保存原值幂等还原）、
    ///   Validate（逐 effect 遥测校验）四段组成；
    /// - 只允许 §17.6.2 白名单控制点；nextReleaseSkillTimeMarker 写入后**不还原**，
    ///   只把所有权交还原版，否则可能提前解禁技能；
    /// - 重申循环只作用于当前场上唯一一名选手，O(1)、零分配。
    /// </summary>
    internal sealed class ModeHCommandAdapter
    {
        #region 状态

        private readonly List<ModeHControlPointModulation> _modulations =
            new List<ModeHControlPointModulation>();

        private AICharacterController _ai;
        private ModeHCommandSpec _spec;
        private float _windowRemaining;
        private float _reassertAccumulator;
        private float _commandScale = 1f;
        private bool _applied;
        private string _lastError;

        #endregion

        #region 只读

        /// <summary>当前是否处于生效窗口。</summary>
        public bool IsActive { get { return _applied && _windowRemaining > 0f; } }

        /// <summary>窗口剩余秒数。</summary>
        public float WindowRemainingSeconds { get { return _windowRemaining; } }

        /// <summary>当前口令 ID。</summary>
        public string CommandId { get { return _spec != null ? _spec.CommandId : null; } }

        /// <summary>最后一次失败原因。</summary>
        public string LastError { get { return _lastError; } }

        /// <summary>本次已实际写入的 effectId（供逐 effect 遥测与文案过滤）。</summary>
        public List<string> AppliedEffectIds
        {
            get
            {
                List<string> ids = new List<string>(_modulations.Count);
                for (int i = 0; i < _modulations.Count; i++)
                {
                    ids.Add(_modulations[i].EffectId);
                }
                return ids;
            }
        }

        #endregion

        #region Apply

        /// <summary>
        /// 应用一条口令：保存原值、写入调制值、执行可选点火。
        /// commandScale 由伤病/战痕的 Mode H 自结算分量提供（如 spirit x0.85、bell_dependence x1.2）。
        /// </summary>
        public bool Apply(
            AICharacterController ai,
            ModeHCommandSpec spec,
            float commandScale,
            ModeHCommandFireContext fireContext,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (ai == null || spec == null || spec.Effects == null)
            {
                failureReasonId = "command_apply_invalid_input";
                return false;
            }
            if (_applied)
            {
                failureReasonId = "command_already_applied";
                return false;
            }

            _ai = ai;
            _spec = spec;
            _commandScale = commandScale > 0f ? commandScale : 1f;
            _windowRemaining = ModeHConfig.CommandWindowSeconds;
            _reassertAccumulator = 0f;
            _modulations.Clear();

            try
            {
                for (int i = 0; i < spec.Effects.Count; i++)
                {
                    ModeHEffectSpec effect = spec.Effects[i];
                    if (effect == null) continue;
                    if (effect.SelfSettled) continue; // Mode H 自结算分量不写原版字段
                    ApplyEffect(effect, fireContext);
                }
                _applied = true;
                _lastError = null;
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "command_apply_exception:" + e.GetType().Name;
                _lastError = failureReasonId;
                Restore();
                return false;
            }
        }

        private void ApplyEffect(ModeHEffectSpec effect, ModeHCommandFireContext fireContext)
        {
            switch (effect.ControlPointId)
            {
                case "skillSuccessChance":
                    ApplyFloat(effect, _ai.skillSuccessChance, delegate (float v) { _ai.skillSuccessChance = v; });
                    break;
                case "itemSkillChance":
                    ApplyFloat(effect, _ai.itemSkillChance, delegate (float v) { _ai.itemSkillChance = v; });
                    break;
                case "itemSkillCoolTime":
                    ApplyFloat(effect, _ai.itemSkillCoolTime, delegate (float v) { _ai.itemSkillCoolTime = v; });
                    break;
                case "sightDistance":
                    ApplyFloat(effect, _ai.sightDistance, delegate (float v) { _ai.sightDistance = v; });
                    break;
                case "sightAngle":
                    ApplyFloat(effect, _ai.sightAngle, delegate (float v) { _ai.sightAngle = v; });
                    break;
                case "combatTurnSpeed":
                    ApplyFloat(effect, _ai.combatTurnSpeed, delegate (float v) { _ai.combatTurnSpeed = v; });
                    break;
                case "patrolTurnSpeed":
                    ApplyFloat(effect, _ai.patrolTurnSpeed, delegate (float v) { _ai.patrolTurnSpeed = v; });
                    break;
                case "baseReactionTime":
                    // 禁止直接改 reactionTime：它会在 1 秒内被 AICharacterController 覆盖。
                    ApplyFloat(effect, _ai.baseReactionTime, delegate (float v) { _ai.baseReactionTime = v; });
                    break;
                case "shootCanMove":
                    ApplyBool(effect, _ai.shootCanMove, delegate (bool v) { _ai.shootCanMove = v; });
                    break;
                case "skillCoolTimeRange":
                    ApplyVector(effect, _ai.skillCoolTimeRange, delegate (Vector2 v) { _ai.skillCoolTimeRange = v; });
                    break;
                case "nextReleaseSkillTimeMarker":
                    ApplyMarker(effect);
                    break;
                case "searchedEnemy":
                case "setNoticedToTarget":
                case "moveToPos":
                    ApplyFire(effect, fireContext);
                    break;
                default:
                    // 白名单之外的控制点在内容加载阶段已被拒绝，这里保持无操作
                    break;
            }
        }

        private float ResolveMultiplier(ModeHEffectSpec effect)
        {
            float milli = effect.MultiplierMilli > 0 ? effect.MultiplierMilli : 1000;
            float multiplier = milli / 1000f;
            // Mode H 自结算的调制幅度缩放（伤病 spirit、战痕 bell_dependence 等）
            if (_commandScale != 1f)
            {
                multiplier = 1f + (multiplier - 1f) * _commandScale;
            }
            return multiplier;
        }

        private void ApplyFloat(ModeHEffectSpec effect, float original, Action<float> setter)
        {
            float target = original;
            if (string.Equals(effect.Op, "multiply", StringComparison.Ordinal))
            {
                target = original * ResolveMultiplier(effect);
            }
            else if (string.Equals(effect.Op, "multiply_capped", StringComparison.Ordinal))
            {
                target = original * ResolveMultiplier(effect);
                float cap = effect.CapMilli > 0 ? effect.CapMilli / 1000f : float.MaxValue;
                if (target > cap) target = cap;
            }
            else if (string.Equals(effect.Op, "set_value", StringComparison.Ordinal))
            {
                target = effect.ValueMilli / 1000f;
            }
            else if (string.Equals(effect.Op, "add_seconds_milli", StringComparison.Ordinal))
            {
                target = original + effect.AddMilli / 1000f;
            }
            else
            {
                return;
            }

            ModeHControlPointModulation modulation = new ModeHControlPointModulation();
            modulation.EffectId = effect.EffectId;
            modulation.ControlPointId = effect.ControlPointId;
            modulation.OriginalFloat = original;
            modulation.TargetFloat = target;
            modulation.Restore = effect.Restore;
            _modulations.Add(modulation);
            setter(target);
        }

        private void ApplyBool(ModeHEffectSpec effect, bool original, Action<bool> setter)
        {
            if (!string.Equals(effect.Op, "set_bool", StringComparison.Ordinal)) return;

            ModeHControlPointModulation modulation = new ModeHControlPointModulation();
            modulation.EffectId = effect.EffectId;
            modulation.ControlPointId = effect.ControlPointId;
            modulation.OriginalBool = original;
            modulation.TargetBool = effect.BoolValue;
            modulation.Restore = effect.Restore;
            _modulations.Add(modulation);
            setter(effect.BoolValue);
        }

        private void ApplyVector(ModeHEffectSpec effect, Vector2 original, Action<Vector2> setter)
        {
            if (!string.Equals(effect.Op, "multiply", StringComparison.Ordinal)) return;
            float multiplier = ResolveMultiplier(effect);
            Vector2 target = new Vector2(original.x * multiplier, original.y * multiplier);

            ModeHControlPointModulation modulation = new ModeHControlPointModulation();
            modulation.EffectId = effect.EffectId;
            modulation.ControlPointId = effect.ControlPointId;
            modulation.OriginalVector = original;
            modulation.TargetVector = target;
            modulation.Restore = effect.Restore;
            _modulations.Add(modulation);
            setter(target);
        }

        private void ApplyMarker(ModeHEffectSpec effect)
        {
            // nextReleaseSkillTimeMarker：写入后不还原，只把所有权交还原版（§17.6.2）
            float target;
            if (string.Equals(effect.Op, "set_marker_window_end", StringComparison.Ordinal))
            {
                target = Time.time + ModeHConfig.CommandWindowSeconds;
            }
            else if (string.Equals(effect.Op, "set_marker_past", StringComparison.Ordinal))
            {
                target = -1f;
            }
            else
            {
                return;
            }

            ModeHControlPointModulation modulation = new ModeHControlPointModulation();
            modulation.EffectId = effect.EffectId;
            modulation.ControlPointId = effect.ControlPointId;
            modulation.OriginalFloat = _ai.nextReleaseSkillTimeMarker;
            modulation.TargetFloat = target;
            modulation.Restore = false; // 冻结例外：绝不还原
            _modulations.Add(modulation);
            _ai.nextReleaseSkillTimeMarker = target;
        }

        private void ApplyFire(ModeHEffectSpec effect, ModeHCommandFireContext fireContext)
        {
            // 点火只能作为窗口开始时的一次性动作，并且必须被同一重申循环持续重发；
            // 这里只记录点火意图，实际重发在 Reassert 中执行。
            ModeHControlPointModulation modulation = new ModeHControlPointModulation();
            modulation.EffectId = effect.EffectId;
            modulation.ControlPointId = effect.ControlPointId;
            modulation.Restore = false;
            _modulations.Add(modulation);
            Fire(effect, fireContext);
        }

        private void Fire(ModeHEffectSpec effect, ModeHCommandFireContext fireContext)
        {
            if (fireContext == null || _ai == null) return;
            try
            {
                if (string.Equals(effect.Op, "fire_move_to_arena_center", StringComparison.Ordinal))
                {
                    _ai.MoveToPos(fireContext.ArenaCenter);
                }
                else if (string.Equals(effect.Op, "fire_notice_nearest", StringComparison.Ordinal))
                {
                    if (fireContext.NearestEnemy != null)
                    {
                        _ai.searchedEnemy = fireContext.NearestEnemy;
                        _ai.SetNoticedToTarget(fireContext.NearestEnemy);
                    }
                }
                else if (string.Equals(effect.Op, "fire_lowest_health_target", StringComparison.Ordinal))
                {
                    if (fireContext.LowestHealthEnemy != null)
                    {
                        _ai.searchedEnemy = fireContext.LowestHealthEnemy;
                    }
                }
                else if (string.Equals(effect.Op, "fire_notice_current_target", StringComparison.Ordinal))
                {
                    if (_ai.searchedEnemy != null)
                    {
                        _ai.SetNoticedToTarget(_ai.searchedEnemy);
                    }
                }
            }
            catch (Exception)
            {
                // 点火失败不撤销已生效的持续调制
            }
        }

        #endregion

        #region Reassert

        /// <summary>
        /// 每帧推进窗口；每 CommandReassertIntervalSeconds 重申一次调制与点火。
        /// 窗口结束时自动 Restore。O(1)、零分配。
        /// </summary>
        public void Tick(float deltaTime, ModeHCommandFireContext fireContext)
        {
            if (!_applied) return;
            if (_windowRemaining <= 0f)
            {
                Restore();
                return;
            }

            _windowRemaining -= deltaTime;
            _reassertAccumulator += deltaTime;
            if (_reassertAccumulator < ModeHConfig.CommandReassertIntervalSeconds) return;
            _reassertAccumulator = 0f;

            Reassert(fireContext);

            if (_windowRemaining <= 0f)
            {
                Restore();
            }
        }

        /// <summary>重申一次全部调制与点火（行为树会周期性抹掉一次性写值）。</summary>
        public void Reassert(ModeHCommandFireContext fireContext)
        {
            if (!_applied || _ai == null) return;
            try
            {
                for (int i = 0; i < _modulations.Count; i++)
                {
                    ModeHControlPointModulation m = _modulations[i];
                    switch (m.ControlPointId)
                    {
                        case "skillSuccessChance": _ai.skillSuccessChance = m.TargetFloat; break;
                        case "itemSkillChance": _ai.itemSkillChance = m.TargetFloat; break;
                        case "itemSkillCoolTime": _ai.itemSkillCoolTime = m.TargetFloat; break;
                        case "sightDistance": _ai.sightDistance = m.TargetFloat; break;
                        case "sightAngle": _ai.sightAngle = m.TargetFloat; break;
                        case "combatTurnSpeed": _ai.combatTurnSpeed = m.TargetFloat; break;
                        case "patrolTurnSpeed": _ai.patrolTurnSpeed = m.TargetFloat; break;
                        case "baseReactionTime": _ai.baseReactionTime = m.TargetFloat; break;
                        case "shootCanMove": _ai.shootCanMove = m.TargetBool; break;
                        case "skillCoolTimeRange": _ai.skillCoolTimeRange = m.TargetVector; break;
                        case "nextReleaseSkillTimeMarker":
                            // 只在窗口内维持，不还原
                            _ai.nextReleaseSkillTimeMarker = m.TargetFloat;
                            break;
                        default:
                            break;
                    }
                }

                // 重申式点火：没有重申的点火不得计为该口令的效果来源
                if (_spec != null && _spec.Effects != null)
                {
                    for (int i = 0; i < _spec.Effects.Count; i++)
                    {
                        ModeHEffectSpec effect = _spec.Effects[i];
                        if (effect == null || effect.SelfSettled) continue;
                        if (effect.Op != null && effect.Op.StartsWith("fire_", StringComparison.Ordinal))
                        {
                            Fire(effect, fireContext);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _lastError = "command_reassert_exception:" + e.GetType().Name;
            }
        }

        #endregion

        #region Restore / Validate

        /// <summary>
        /// 幂等还原：按保存的原值恢复，nextReleaseSkillTimeMarker 例外（不还原）。
        /// 窗口结束、倒地、接力、技术中止、切图与 shutdown 都调用同一入口。
        /// </summary>
        public void Restore()
        {
            if (!_applied)
            {
                _modulations.Clear();
                return;
            }
            _applied = false;
            _windowRemaining = 0f;

            if (_ai != null)
            {
                for (int i = _modulations.Count - 1; i >= 0; i--)
                {
                    ModeHControlPointModulation m = _modulations[i];
                    if (!m.Restore) continue;
                    try
                    {
                        switch (m.ControlPointId)
                        {
                            case "skillSuccessChance": _ai.skillSuccessChance = m.OriginalFloat; break;
                            case "itemSkillChance": _ai.itemSkillChance = m.OriginalFloat; break;
                            case "itemSkillCoolTime": _ai.itemSkillCoolTime = m.OriginalFloat; break;
                            case "sightDistance": _ai.sightDistance = m.OriginalFloat; break;
                            case "sightAngle": _ai.sightAngle = m.OriginalFloat; break;
                            case "combatTurnSpeed": _ai.combatTurnSpeed = m.OriginalFloat; break;
                            case "patrolTurnSpeed": _ai.patrolTurnSpeed = m.OriginalFloat; break;
                            case "baseReactionTime": _ai.baseReactionTime = m.OriginalFloat; break;
                            case "shootCanMove": _ai.shootCanMove = m.OriginalBool; break;
                            case "skillCoolTimeRange": _ai.skillCoolTimeRange = m.OriginalVector; break;
                            default: break;
                        }
                    }
                    catch (Exception)
                    {
                        // 单条还原失败不阻断其余还原
                    }
                }
            }

            _modulations.Clear();
            _spec = null;
            _ai = null;
            _commandScale = 1f;
        }

        /// <summary>
        /// 逐 effect 校验：确认调制值在窗口内没有被原版回写。
        /// 返回仍然保持的 effectId 集合，供逐 effect 遥测判定。
        /// </summary>
        public List<string> Validate()
        {
            List<string> held = new List<string>();
            if (!_applied || _ai == null) return held;

            for (int i = 0; i < _modulations.Count; i++)
            {
                ModeHControlPointModulation m = _modulations[i];
                bool ok = false;
                try
                {
                    switch (m.ControlPointId)
                    {
                        case "skillSuccessChance": ok = Approximately(_ai.skillSuccessChance, m.TargetFloat); break;
                        case "itemSkillChance": ok = Approximately(_ai.itemSkillChance, m.TargetFloat); break;
                        case "itemSkillCoolTime": ok = Approximately(_ai.itemSkillCoolTime, m.TargetFloat); break;
                        case "sightDistance": ok = Approximately(_ai.sightDistance, m.TargetFloat); break;
                        case "sightAngle": ok = Approximately(_ai.sightAngle, m.TargetFloat); break;
                        case "combatTurnSpeed": ok = Approximately(_ai.combatTurnSpeed, m.TargetFloat); break;
                        case "patrolTurnSpeed": ok = Approximately(_ai.patrolTurnSpeed, m.TargetFloat); break;
                        case "baseReactionTime": ok = Approximately(_ai.baseReactionTime, m.TargetFloat); break;
                        case "shootCanMove": ok = _ai.shootCanMove == m.TargetBool; break;
                        case "skillCoolTimeRange":
                            ok = Approximately(_ai.skillCoolTimeRange.x, m.TargetVector.x)
                                && Approximately(_ai.skillCoolTimeRange.y, m.TargetVector.y);
                            break;
                        case "nextReleaseSkillTimeMarker": ok = true; break; // 原版会自行重写，不作保持率判定
                        case "searchedEnemy": ok = _ai.searchedEnemy != null; break;
                        case "setNoticedToTarget": ok = _ai.noticed; break;
                        case "moveToPos": ok = true; break; // 路径保持率由遥测采样，不在此判定
                        default: ok = false; break;
                    }
                }
                catch (Exception)
                {
                    ok = false;
                }
                if (ok) held.Add(m.EffectId);
            }
            return held;
        }

        private static bool Approximately(float a, float b)
        {
            return Mathf.Abs(a - b) <= Mathf.Max(0.0001f, Mathf.Abs(b) * 0.001f);
        }

        #endregion
    }

    /// <summary>口令点火所需的场上上下文（每次重申由调用方刷新，零分配复用）。</summary>
    internal sealed class ModeHCommandFireContext
    {
        /// <summary>擂台中心。</summary>
        public Vector3 ArenaCenter;
        /// <summary>当前最近敌人的受击体。</summary>
        public DamageReceiver NearestEnemy;
        /// <summary>当前生命最低敌人的受击体。</summary>
        public DamageReceiver LowestHealthEnemy;
        /// <summary>当前敌方同时在场数量。</summary>
        public int EnemyCount;
    }
}
