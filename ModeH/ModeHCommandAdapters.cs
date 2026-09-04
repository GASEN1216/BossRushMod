using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 一条正在生效的字段调制记录：保存原值以便幂等还原（设计提案 §17.6.1）。
    /// </summary>
    /// <summary>
    /// 分量生效条件（`appliesWhen`）的求值器。
    ///
    /// 【为什么是持续求值而不是开窗时算一次】
    ///   2026-09-03 之前 `appliesWhen` 被解析进 `ModeHEffectSpec.AppliesWhen` 之后**零读者**，
    ///   9 个分量一律无条件生效。最明显的是 `crowd_favorite`：收益写着"敌军≥3 才给"、
    ///   代价写着"单核战才吃"，两个互斥条件同时恒真。
    ///
    ///   开窗时算一次是不够的：常驻战痕在选手登场那一刻开窗，那时敌军尚未生成，
    ///   `enemy_count_at_least_3` 恒假，收益反而永远拿不到。所以按重申节奏
    ///   （CommandReassertIntervalSeconds，0.1 秒）持续求值，分量随条件真伪上下线。
    ///   —— owner 2026-09-03 拍板选此口径。
    ///
    /// 【未知取值一律按"无条件生效"处理】
    ///   fail-open 而不是 fail-closed：认不出的条件如果按假处理，就会静默禁掉一个分量，
    ///   那正是本次要消灭的失败形态。数据侧的拼写错误由
    ///   `ModeHScarTriggerWiringGuard` 在构建期抓，不留给运行时。
    /// </summary>
    internal static class ModeHEffectConditions
    {
        /// <summary>擂台条件类前缀：这一类整场不变，因而允许被一次性求值。</summary>
        internal const string ArenaConditionPrefix = "condition_";

        /// <summary>该条件是否整场恒定（自结算分量只允许用这一类，见守卫）。</summary>
        internal static bool IsStaticCondition(string appliesWhen)
        {
            return string.IsNullOrEmpty(appliesWhen)
                || appliesWhen.StartsWith(ArenaConditionPrefix, StringComparison.Ordinal);
        }

        /// <summary>条件是否成立。空条件表示无条件生效。no-throw。</summary>
        internal static bool IsSatisfied(string appliesWhen, ModeHCommandFireContext context)
        {
            if (string.IsNullOrEmpty(appliesWhen)) return true;
            if (context == null) return true;
            try
            {
                switch (appliesWhen)
                {
                    // 拍铃之前。窗口若由拍铃开启，这一条在窗口内恒假——这正是
                    // bell_dependence 的代价分量应有的表现（它写的是"拍铃前才吃"）。
                    case "before_bell":
                        return !context.BellConsumed;

                    // 先发的开场：接力者上场之后即不再成立。
                    case "starter_opening":
                        return !context.ActiveFighterIsRelay;

                    case "enemy_count_at_least_3":
                        return context.EnemyCount >= ModeHConfig.CowardCrowdEnemyThreshold;

                    // 单核战：场上只剩一个（或没有）敌人时成立。
                    case "single_core_fight":
                        return context.EnemyCount <= 1;

                    case "reinforcement_pending":
                        return context.ReinforcementPending;

                    case "first_wave_alive":
                        return context.FirstWaveAlive;

                    default:
                        break;
                }

                // condition_<conditionId>：与本场擂台条件逐字比对。
                // ThreatPlans.json 的 arenaConditions 里确有 danger_edge / open_field 两个 id，
                // 所以这里不需要另建映射表。
                if (appliesWhen.StartsWith(ArenaConditionPrefix, StringComparison.Ordinal))
                {
                    string conditionId = appliesWhen.Substring(ArenaConditionPrefix.Length);
                    return string.Equals(context.ArenaConditionId, conditionId, StringComparison.Ordinal);
                }
            }
            catch (Exception)
            {
                // 求值本身出问题时按无条件生效处理，理由同类头的 fail-open 说明
                return true;
            }
            return true;
        }
    }
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
        private List<ModeHEffectSpec> _effects;
        private string _ownerEntryId;
        private float _windowRemaining;
        private float _reassertAccumulator;
        private float _commandScale = 1f;
        private bool _applied;
        private string _lastError;

        #endregion

        #region 只读

        /// <summary>当前是否处于生效窗口。</summary>
        public bool IsActive { get { return _applied && _windowRemaining > 0f; } }

        /// <summary>
        /// 调制仍挂在 AI 上、尚未还原（窗口可能已经到期）。
        ///
        /// `IsActive` 的定义是「已施加 **且** 窗口未到期」，控制器若只用它当早退判据，
        /// 窗口一归零就再也不驱动本 adapter，而 `Restore()` 只写在 `Tick` 内部——
        /// 于是绝大多数情况下调制永不还原，一直挂到本场结束。
        /// </summary>
        public bool NeedsFinalize { get { return _applied; } }

        /// <summary>窗口剩余秒数。</summary>
        public float WindowRemainingSeconds { get { return _windowRemaining; } }

        /// <summary>当前口令 ID。</summary>
        public string CommandId { get { return _spec != null ? _spec.CommandId : null; } }

        /// <summary>
        /// 当前窗口的归属条目 ID：口令为 commandId，伤病/战痕为其稳定 ID。
        /// 伤病与战痕复用同一套调制设施，因此需要一个统一的归属字段。
        /// </summary>
        public string OwnerEntryId { get { return _ownerEntryId; } }

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
            if (spec == null || spec.Effects == null)
            {
                failureReasonId = "command_apply_invalid_input";
                return false;
            }

            _spec = spec;
            return ApplyEffects(
                ai, spec.CommandId, spec.Effects, ModeHConfig.CommandWindowSeconds,
                commandScale, fireContext, out failureReasonId);
        }

        /// <summary>
        /// 通用调制入口：口令、伤病与战痕共用同一条字段修改路径（§17.4 明令禁止另建第二条）。
        /// windowSeconds 由调用方给出——口令固定 6 秒，伤病为整场，战痕为其自有窗口。
        /// </summary>
        public bool ApplyEffects(
            AICharacterController ai,
            string ownerEntryId,
            IList<ModeHEffectSpec> effects,
            float windowSeconds,
            float commandScale,
            ModeHCommandFireContext fireContext,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (ai == null || effects == null)
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
            _ownerEntryId = ownerEntryId;
            _effects = new List<ModeHEffectSpec>(effects);
            _commandScale = commandScale > 0f ? commandScale : 1f;
            _windowRemaining = windowSeconds > 0f ? windowSeconds : 0f;
            _reassertAccumulator = 0f;
            _modulations.Clear();

            try
            {
                for (int i = 0; i < _effects.Count; i++)
                {
                    ModeHEffectSpec effect = _effects[i];
                    if (effect == null) continue;
                    if (effect.SelfSettled) continue; // Mode H 自结算分量不写原版字段
                    // 条件不成立的分量此刻不施加；成立之后由 Reassert 补上。
                    if (!ModeHEffectConditions.IsSatisfied(effect.AppliesWhen, fireContext)) continue;
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
                // Restore 现在会把已写入的部分调制逐条写回原值（见 Restore 的注释），
                // 因此这里的回滚是真回滚，不再是空操作。留一行日志便于事后定位：
                // 这条路径意味着官方 AI 字段结构变了，属于需要人工复查的契约面事故。
                ModBehaviour.CriticalLog("[ModeH] 口令施加异常，已回滚部分调制: " + failureReasonId);
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

        /// <summary>
        /// 按当前场况让带 `appliesWhen` 的分量上下线（owner 拍板的"随战斗持续求值"口径）。
        ///
        /// 条件真伪翻转时才动手：真→假还原那一条并摘掉，假→真按当前值重新施加。
        /// 重新施加时捕获的是**此刻**的原值而不是开窗时的原值——这与本适配器一贯的
        /// 嵌套语义一致（口令、伤病、战痕三套窗口可能同时在改同一个控制点，
        /// 每一层只负责还原到自己接手时看到的值）。
        ///
        /// `Restore == false` 的分量（当前只有 nextReleaseSkillTimeMarker）一旦施加就不摘：
        /// 它的契约是"写入后把所有权交还原版，绝不还原"。
        ///
        /// 无条件分量在此方法里完全不被触碰，热路径成本是一次 O(分量数) 的字符串判空。
        /// </summary>
        private void SyncConditionalEffects(ModeHCommandFireContext fireContext)
        {
            if (_effects == null) return;
            for (int i = 0; i < _effects.Count; i++)
            {
                ModeHEffectSpec effect = _effects[i];
                if (effect == null || effect.SelfSettled) continue;
                if (string.IsNullOrEmpty(effect.AppliesWhen)) continue;

                bool shouldApply = ModeHEffectConditions.IsSatisfied(effect.AppliesWhen, fireContext);
                int index = FindModulationIndex(effect.EffectId);
                if (shouldApply)
                {
                    if (index < 0) ApplyEffect(effect, fireContext);
                }
                else if (index >= 0)
                {
                    RestoreSingleModulation(index);
                }
            }
        }

        /// <summary>按 effectId 找当前在线的调制；不在线返回 -1。分量数是个位数，线性扫即可。</summary>
        private int FindModulationIndex(string effectId)
        {
            if (string.IsNullOrEmpty(effectId)) return -1;
            for (int i = 0; i < _modulations.Count; i++)
            {
                ModeHControlPointModulation m = _modulations[i];
                if (m != null && string.Equals(m.EffectId, effectId, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>还原并摘掉单条调制（条件转假时用）。Restore=false 的不摘。</summary>
        private void RestoreSingleModulation(int index)
        {
            if (index < 0 || index >= _modulations.Count) return;
            ModeHControlPointModulation m = _modulations[index];
            if (m == null)
            {
                _modulations.RemoveAt(index);
                return;
            }
            if (!m.Restore) return; // 契约：写入后不还原的分量不下线
            WriteOriginal(m);
            _modulations.RemoveAt(index);
        }

        /// <summary>把一条调制写回原值。Restore() 与条件下线共用，避免两份 switch 漂移。</summary>
        private void WriteOriginal(ModeHControlPointModulation m)
        {
            if (_ai == null || m == null) return;
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

        /// <summary>重申一次全部调制与点火（行为树会周期性抹掉一次性写值）。</summary>
        public void Reassert(ModeHCommandFireContext fireContext)
        {
            if (!_applied || _ai == null) return;
            try
            {
                // 先按当前场况让条件分量上下线，再重申仍在线的那些。
                SyncConditionalEffects(fireContext);

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

                // 重申式点火：没有重申的点火不得计为该条目的效果来源
                if (_effects != null)
                {
                    for (int i = 0; i < _effects.Count; i++)
                    {
                        ModeHEffectSpec effect = _effects[i];
                        if (effect == null || effect.SelfSettled) continue;
                        // 条件不成立时也不重发点火：否则"下线"只对调制类分量生效，
                        // 点火类分量会绕过条件继续每 0.1 秒把 AI 的目标掰回去。
                        if (!ModeHEffectConditions.IsSatisfied(effect.AppliesWhen, fireContext)) continue;
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
            // **不能**因为 `!_applied` 就直接丢弃 _modulations：`ApplyEffects` 是在
            // 整个循环跑完之后才置 `_applied = true` 的，中途抛异常时 `_applied`
            // 仍为 false，而循环里已经写进 AI 的那几条调制全都记在 _modulations 里。
            // 旧写法在这条路径上等于「既不还原、也不留记录」——部分调制永久挂在
            // 选手身上直到本场结束，且没有任何痕迹可查。
            // 统一走同一条还原路径：_modulations 为空时行为与旧写法逐字相同。
            _applied = false;
            _windowRemaining = 0f;

            if (_ai != null)
            {
                // 逆序还原（LIFO），与条件下线共用同一个 WriteOriginal，
                // 避免两处 switch 各自漂移出不同的控制点集合。
                for (int i = _modulations.Count - 1; i >= 0; i--)
                {
                    ModeHControlPointModulation m = _modulations[i];
                    if (!m.Restore) continue;
                    WriteOriginal(m);
                }
            }

            _modulations.Clear();
            _spec = null;
            _effects = null;
            _ownerEntryId = null;
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
        /// <summary>拍铃是否已消耗（条件 before_bell 取它的反面）。</summary>
        public bool BellConsumed;
        /// <summary>当前登场者是否为接力者（条件 starter_opening 取它的反面）。</summary>
        public bool ActiveFighterIsRelay;
        /// <summary>本场擂台条件 ID（条件 condition_* 与它逐字比对）。整场不变。</summary>
        public string ArenaConditionId;
        /// <summary>是否还有后续入场批次未到场（条件 reinforcement_pending）。</summary>
        public bool ReinforcementPending;
        /// <summary>第一批敌军是否仍有活口（条件 first_wave_alive）。</summary>
        public bool FirstWaveAlive;

        /// <summary>当前最近敌人的受击体。</summary>
        public DamageReceiver NearestEnemy;
        /// <summary>当前生命最低敌人的受击体。</summary>
        public DamageReceiver LowestHealthEnemy;
        /// <summary>当前敌方同时在场数量。</summary>
        public int EnemyCount;
    }
}
