using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode H 看台表演驱动（设计提案 §17.6.5 部件三、§25.1）。
    ///
    /// owner 要求“选手 AI 同时进入玩家身体并自行行动”。首发**不**在玩家身体上挂
    /// `AICharacterController` 或行为树：那会引入索敌、开火、拾取和技能释放，
    /// 与“看台身体不参与伤害与胜负结算”的裁决直接冲突，也无法保证不碰玩家真实装备。
    ///
    /// 冻结契约：
    /// - 只产生**移动意图**：不写瞄准、不写攻击、不写技能、不写交互、不触碰 inventory；
    ///   `CanUseHand`/`CanControlAim` 的原版 `false` 是第二道保险；
    /// - 目标点落在以 `modeHSpectatorPos` 为圆心、`StandInRadiusMeters = 3.0` 的圆内；
    /// - 每 `StandInRepathIntervalSeconds = 0.5` 秒最多重设一次目标点，O(1)、零分配；
    ///   不做寻路查询以外的任何场景扫描；
    /// - 越界（超过半径 * `StandInLeashMultiplier`）立即拉回圆心；
    /// - 连续 `StandInMaxConsecutiveFailures = 3` 次失败即停止表演并让身体静止；
    ///   表演失败**绝不**升级为技术中止，比赛照常进行；
    /// - 表演层不提供任何战斗增益、不改变敌军威胁、不产生遥测事实。
    ///
    /// 六种表演模式按被互换选手的 `temperamentId` 选定（§17.6.5 表）。
    /// </summary>
    internal sealed class ModeHStandInPerformer
    {
        #region 状态

        private CharacterMainControl _body;
        private Vector3 _center;
        private string _patternId;

        private float _repathAccumulator;
        private float _elapsedSeconds;
        private int _consecutiveFailures;
        private bool _active;
        private bool _stopped;
        private Vector3 _currentTarget;
        private int _phase;

        #endregion

        #region 只读

        /// <summary>表演是否进行中。</summary>
        public bool IsPerforming { get { return _active && !_stopped; } }

        /// <summary>当前表演模式 ID。</summary>
        public string PatternId { get { return _patternId; } }

        /// <summary>连续失败次数（达到上限即停演）。</summary>
        public int ConsecutiveFailures { get { return _consecutiveFailures; } }

        #endregion

        #region 模式选择

        /// <summary>§17.6.5 冻结的底色 -> 表演模式映射。未知底色回落到最保守的原地站立。</summary>
        public static string ResolvePatternId(string temperamentId)
        {
            if (string.Equals(temperamentId, ModeHStableIds.TemperamentAggressive, StringComparison.Ordinal))
            {
                return "rail_charge";
            }
            if (string.Equals(temperamentId, ModeHStableIds.TemperamentCautious, StringComparison.Ordinal))
            {
                return "wall_hug";
            }
            if (string.Equals(temperamentId, ModeHStableIds.TemperamentHunter, StringComparison.Ordinal))
            {
                return "slow_circle";
            }
            if (string.Equals(temperamentId, ModeHStableIds.TemperamentBulwark, StringComparison.Ordinal))
            {
                return "anchor_stand";
            }
            if (string.Equals(temperamentId, ModeHStableIds.TemperamentTrickster, StringComparison.Ordinal))
            {
                return "erratic_dart";
            }
            if (string.Equals(temperamentId, ModeHStableIds.TemperamentPack, StringComparison.Ordinal))
            {
                return "gate_pace";
            }
            return "anchor_stand";
        }

        #endregion

        #region 生命周期

        /// <summary>
        /// 开始表演。必须在 `IsModeHStandInActive=true` 之后调用
        /// （顺序不可颠倒：先中立无敌，再解冻移动，最后才启动表演）。
        /// </summary>
        public bool TryStart(
            CharacterMainControl body, Vector3 spectatorCenter, string patternId, out string failureReasonId)
        {
            failureReasonId = null;
            if (body == null)
            {
                failureReasonId = "standin_body_missing";
                return false;
            }
            if (!ModeHRuntimeGates.IsModeHStandInActive)
            {
                failureReasonId = "standin_gate_inactive";
                return false;
            }

            _body = body;
            _center = spectatorCenter;
            _patternId = !string.IsNullOrEmpty(patternId) ? patternId : "anchor_stand";
            _repathAccumulator = ModeHConfig.StandInRepathIntervalSeconds; // 立刻设一次目标
            _elapsedSeconds = 0f;
            _consecutiveFailures = 0;
            _phase = 0;
            _currentTarget = spectatorCenter;
            _active = true;
            _stopped = false;
            return true;
        }

        /// <summary>
        /// 每帧推进。O(1)、零分配：只在重设间隔到达时算一个目标点，
        /// 其余帧只做越界检查。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!IsPerforming || _body == null) return;

            _elapsedSeconds += deltaTime;

            // 越界拉回：距离超过半径 * leash 立即回圆心
            if (IsOutOfLeash())
            {
                _currentTarget = _center;
                if (!TryMoveTo(_currentTarget)) RegisterFailure();
                return;
            }

            _repathAccumulator += deltaTime;
            if (_repathAccumulator < ModeHConfig.StandInRepathIntervalSeconds) return;
            _repathAccumulator = 0f;

            _currentTarget = ComputeTarget();
            if (TryMoveTo(_currentTarget)) _consecutiveFailures = 0;
            else RegisterFailure();
        }

        /// <summary>
        /// 停止表演并让身体静止。幂等：恢复顺序的第一步与所有异常路径共用同一入口。
        /// 停演绝不升级为技术中止。
        /// </summary>
        public void Stop()
        {
            _active = false;
            _stopped = true;
            if (_body != null)
            {
                // 只清移动意图，不触碰阵营、无敌、持有物——那些由恢复顺序的后续步骤负责
                try
                {
                    _body.SetMoveInput(Vector3.zero);
                    _body.SetRunInput(false);
                }
                catch (Exception)
                {
                    // 身体已被回收：静止目标已自然达成
                }
            }
            _body = null;
        }

        private void RegisterFailure()
        {
            _consecutiveFailures++;
            if (_consecutiveFailures < ModeHConfig.StandInMaxConsecutiveFailures) return;
            ModBehaviour.DevLog("[ModeH] 看台表演连续失败达到上限，停演；比赛继续");
            Stop();
        }

        private bool IsOutOfLeash()
        {
            try
            {
                if (_body == null || _body.transform == null) return false;
                float leash = ModeHConfig.StandInRadiusMeters * ModeHConfig.StandInLeashMultiplier;
                return Vector3.Distance(_body.transform.position, _center) > leash;
            }
            catch (Exception)
            {
                // 身体已被回收：交由下一次 TryMoveTo 计入失败并停演
                return false;
            }
        }

        #endregion

        #region 表演模式

        /// <summary>
        /// 按模式在半径内算一个目标点。全部使用 `_elapsedSeconds` 的确定性函数，
        /// 不使用全局随机，也不做场景扫描。
        /// </summary>
        private Vector3 ComputeTarget()
        {
            float radius = ModeHConfig.StandInRadiusMeters;
            switch (_patternId)
            {
                case "rail_charge":
                    // 沿护栏快步来回，间歇冲向擂台方向再退回
                    _phase = (_phase + 1) % 4;
                    return _center + new Vector3(
                        (_phase < 2 ? 1f : -1f) * radius * 0.9f, 0f,
                        (_phase % 2 == 0 ? 0.6f : -0.2f) * radius);

                case "wall_hug":
                    // 贴边小幅移动，长时间静止观察
                    _phase = (_phase + 1) % 6;
                    if (_phase >= 3) return _center + new Vector3(-radius * 0.9f, 0f, 0f);
                    return _center + new Vector3(-radius * 0.9f, 0f, (_phase - 1) * radius * 0.25f);

                case "slow_circle":
                    // 缓慢绕圆周巡走，朝擂台方向停顿
                    _phase = (_phase + 1) % 12;
                    if (_phase == 0) return _center + new Vector3(0f, 0f, radius * 0.8f);
                    return PointOnCircle(radius * 0.8f, _phase / 12f);

                case "anchor_stand":
                    // 几乎不移动，只原地缓慢转向
                    _phase = (_phase + 1) % 8;
                    return _center + PointOnCircle(radius * 0.15f, _phase / 8f) - _center;

                case "erratic_dart":
                    // 无规律短距折返：用固定序列而不是随机数，保证可重放
                    _phase = (_phase + 5) % 7;
                    return PointOnCircle(radius * (0.4f + 0.5f * (_phase % 3) / 2f), _phase / 7f);

                case "gate_pace":
                    // 在出入口方向来回，像在等同伴
                    _phase = (_phase + 1) % 2;
                    return _center + new Vector3(0f, 0f, (_phase == 0 ? 1f : -1f) * radius * 0.85f);

                default:
                    return _center;
            }
        }

        private Vector3 PointOnCircle(float radius, float turns)
        {
            float angle = turns * Mathf.PI * 2f;
            return _center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// 只写移动意图：`SetMoveInput` 取归一化方向，`SetRunInput` 只在快步模式为真。
        /// 绝不调用瞄准、攻击、技能、交互或 inventory 相关 API，也不做寻路查询。
        /// 到点后写零向量让身体自然停住。
        /// </summary>
        private bool TryMoveTo(Vector3 target)
        {
            if (_body == null) return false;
            try
            {
                Transform bodyTransform = _body.transform;
                if (bodyTransform == null) return false;

                Vector3 delta = target - bodyTransform.position;
                delta.y = 0f;
                Vector3 moveInput = delta.sqrMagnitude <= ArrivalSqrDistance
                    ? Vector3.zero
                    : delta.normalized;

                _body.SetMoveInput(moveInput);
                _body.SetRunInput(
                    moveInput != Vector3.zero
                    && string.Equals(_patternId, "rail_charge", StringComparison.Ordinal));
                return true;
            }
            catch (Exception)
            {
                // 身体已被回收或组件缺失：计入连续失败，达到上限即停演
                return false;
            }
        }

        /// <summary>到点判定阈值（平方距离），避免每帧开方。</summary>
        private const float ArrivalSqrDistance = 0.09f;

        #endregion
    }
}
