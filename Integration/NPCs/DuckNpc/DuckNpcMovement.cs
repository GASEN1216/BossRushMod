// ============================================================================
// DuckNpcMovement.cs - 捏脸 NPC 的移动（官方寻路，零战斗面）
// ============================================================================
// 模块说明：
//   给捏脸 NPC 加"会自己走动"的能力。走的是**官方 A\* 寻路 + 官方移动管线**，
//   既不自己造 A\*，也不引入官方战斗 AI。
//
//   为什么不用整个 AICharacterController：
//     - 它的 pathControl / 四棵行为树都是 private SerializeField，只能由 preset 的
//       prefab 提供 —— 必须反射 CharacterRandomPreset.aiController 才拿得到
//       （DeathWraith 就是这么干的），凭空多一个易碎绑定面。
//     - Init() 有一串对交互 NPC 全是负资产的副作用：覆写音色、占用技能槽、
//       给角色物品挂 GunScatterMultiplier Modifier、注册 OnHurt 监听。
//     - 拿到的是一整棵战斗行为树（SearchEnemyAround / Shoot / TraceTarget…），
//       然后要逐项压制十几个字段再反射禁用四棵 BT。这是负工作量。
//
//   本文件的做法：只取官方寻路组件 AI_PathControl + A\* 的 Seeker。
//   AI_PathControl 的字段（seeker / controller / nextWaypointDistance / stopDistance）
//   **全是 public 且有合理默认值**，可以直接 AddComponent 后接线，零反射。
//   它内部每帧算出方向后调 controller.SetMoveInput(...)，
//   于是 ECM2 位移、重力、涉水减速、走路动画参数全部白送
//   （CharacterMainControl.Update 每帧本来就在跑 movementControl.UpdateMovement）。
//
//   一个必须自己补的坑见 UpdateFacing()。
// ============================================================================

using System;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 捏脸 NPC 的漫步移动。挂在 CharacterMainControl 的 GameObject 上。
    /// </summary>
    internal sealed class DuckNpcMovement : MonoBehaviour
    {
        private const string LogPrefix = "[DuckNpc]";

        /// <summary>到点后停多久再选下一个目标。</summary>
        private const float MinIdleSeconds = 3f;
        private const float MaxIdleSeconds = 7f;

        /// <summary>单次寻路的最长容忍时间，超时就放弃这个目标点重选。</summary>
        private const float PathTimeoutSeconds = 12f;

        /// <summary>朝向前瞻距离。</summary>
        private const float AimLookAheadDistance = 10f;

        /// <summary>跟随时靠到这个距离内就停。</summary>
        private const float FollowStopDistance = 2.5f;

        /// <summary>跟随掉队超过这个距离直接瞬移（与现有 NPC 跟随同量级）。</summary>
        private const float FollowTeleportDistance = 40f;

        /// <summary>跟随时的重规划间隔。</summary>
        private const float FollowRepathSeconds = 0.6f;

        private CharacterMainControl _character;
        private AI_PathControl _pathControl;
        private Vector3 _homePosition;
        private float _wanderRadius = 8f;
        private float _nextMoveTime;
        private float _pathDeadline;
        private bool _ready;
        private bool _held;
        private Transform _followTarget;

        // ====================================================================
        // 装配
        // ====================================================================

        /// <summary>
        /// 接线。失败返回 false（此时组件会自我禁用，NPC 退回站桩）。
        /// </summary>
        internal bool Bind(CharacterMainControl character, Vector3 homePosition, float wanderRadius)
        {
            _character = character;
            _homePosition = homePosition;
            _wanderRadius = wanderRadius > 0f ? wanderRadius : 8f;

            if (_character == null)
            {
                enabled = false;
                return false;
            }

            // A* 图是场景级资产。基地/大厅这类场景可能压根没有图，
            // 此时 Seeker.StartPath 的回调会带 error，NPC 会"就是不走"且毫无提示 ——
            // 所以这里提前判掉并明说，别让人去查一个不存在的 bug。
            if (AstarPath.active == null)
            {
                ModBehaviour.DevLog(LogPrefix + " 当前场景没有 A* 图（AstarPath.active 为空），"
                    + "捏脸 NPC 退回站桩: " + SafeName());
                enabled = false;
                return false;
            }

            try
            {
                Seeker seeker = gameObject.GetComponent<Seeker>();
                if (seeker == null)
                {
                    seeker = gameObject.AddComponent<Seeker>();
                }

                _pathControl = gameObject.GetComponent<AI_PathControl>();
                if (_pathControl == null)
                {
                    _pathControl = gameObject.AddComponent<AI_PathControl>();
                }

                // AI_PathControl 的这两个引用是 public 字段，官方靠 prefab 序列化填，
                // 我们是运行时组装，必须手动接上，否则它每帧解引用 null。
                _pathControl.seeker = seeker;
                _pathControl.controller = _character;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 装配寻路组件失败: " + e.Message);
                enabled = false;
                return false;
            }

            _ready = true;
            _nextMoveTime = Time.time + UnityEngine.Random.Range(MinIdleSeconds, MaxIdleSeconds);
            return true;
        }

        // ====================================================================
        // 主循环
        // ====================================================================

        private void Update()
        {
            if (!_ready || _character == null)
            {
                return;
            }

            try
            {
                // 朝向必须一直刷，**即使在对话中被挂起**：
                // 官方 Movement.UpdateAiming 每帧按 aimPoint 推朝向，
                // 停刷就会被生成时那个陈旧的 aimPoint 锁死（NPC 横着站/横着走）。
                UpdateFacing();

                if (_held)
                {
                    return;
                }

                UpdateWander();
            }
            catch (Exception e)
            {
                // 移动出错不该拖垮 NPC 本身：停掉移动，NPC 退回站桩仍可交互。
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 移动更新异常，已停用移动: " + e.Message);
                _ready = false;
                enabled = false;
            }
        }

        /// <summary>
        /// 每帧把 aimPoint 推到"当前位置 + 移动方向"。
        /// </summary>
        /// <remarks>
        /// **这一步不是可选优化，是必做项。**
        /// 官方 Movement.UpdateAiming() 的朝向优先级是：
        /// 若 aimPoint 距自身 &gt; 0.6m 且在瞄准，就朝 aimPoint，而不是朝移动方向。
        /// DuckNpcFactory 生成时设过一个固定的世界点 aimPoint，NPC 一旦走开，
        /// 那个陈旧的点会把模型朝向锁死在原地方向 —— 看起来像螃蟹横着走。
        /// 官方 AI 是靠 AICharacterController.Update 每帧重写 aimPoint 规避的，
        /// 我们没有那个 AI，就得自己来。
        /// </remarks>
        private void UpdateFacing()
        {
            if (!_pathControl.Moving)
            {
                return;
            }

            Vector3 velocity = _character.movementControl != null
                ? _character.movementControl.CurrentMoveDirectionXZ
                : Vector3.zero;

            if (velocity.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            _character.SetAimPoint(transform.position + velocity.normalized * AimLookAheadDistance);
        }

        private void UpdateWander()
        {
            if (_pathControl.Moving)
            {
                // 寻路卡住（目标不可达、图有洞）时不能无限等，否则 NPC 会永久僵在原地。
                if (Time.time > _pathDeadline)
                {
                    _pathControl.StopMove();
                    ScheduleNextMove();
                }
                return;
            }

            if (Time.time < _nextMoveTime)
            {
                return;
            }

            if (_pathControl.WaitingForPathResult)
            {
                return;
            }

            if (_followTarget != null)
            {
                MoveTowardFollowTarget();
                return;
            }

            MoveToRandomPointNearHome();
        }

        /// <summary>
        /// 跟随：离得远就走过去，够近就不动。
        /// </summary>
        private void MoveTowardFollowTarget()
        {
            Vector3 targetPos = _followTarget.position;
            float distance = Vector3.Distance(transform.position, targetPos);

            if (distance <= FollowStopDistance)
            {
                ScheduleNextMove();
                return;
            }

            // 掉队太远直接瞬移跟上，否则玩家过图/跑图后配偶会永远追不上。
            if (distance >= FollowTeleportDistance)
            {
                try
                {
                    _pathControl.StopMove();
                    _character.SetPosition(targetPos);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] 跟随瞬移失败: " + e.Message);
                }
                ScheduleNextMove();
                return;
            }

            _pathControl.MoveToPos(targetPos);
            _pathDeadline = Time.time + PathTimeoutSeconds;
            // 跟随时重规划要比漫步频繁，否则玩家一直走它就一直落后
            _nextMoveTime = Time.time + FollowRepathSeconds;
        }

        private void MoveToRandomPointNearHome()
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * _wanderRadius;
            Vector3 target = _homePosition + new Vector3(offset.x, 0f, offset.y);

            _pathControl.MoveToPos(target);
            _pathDeadline = Time.time + PathTimeoutSeconds;
            ScheduleNextMove();
        }

        private void ScheduleNextMove()
        {
            _nextMoveTime = Time.time + UnityEngine.Random.Range(MinIdleSeconds, MaxIdleSeconds);
        }

        // ====================================================================
        // 外部控制
        // ====================================================================

        /// <summary>
        /// 开始跟随玩家（婚后配偶）。传 null 等同于 DisablePlayerFollow。
        /// </summary>
        /// <remarks>
        /// 跟随与漫步互斥：跟随目标非空时 UpdateWander 改为「离目标太远就走过去」。
        /// 复用同一套 AI_PathControl，不另起一套移动栈。
        /// </remarks>
        internal void EnablePlayerFollow(Transform target)
        {
            _followTarget = target;
            _nextMoveTime = 0f;
        }

        /// <summary>停止跟随，回到以 home 为中心的漫步。</summary>
        internal void DisablePlayerFollow()
        {
            _followTarget = null;
            _nextMoveTime = Time.time + MinIdleSeconds;
        }

        /// <summary>把漫步中心挪到新位置（婚礼教堂驻留、离婚复位等）。</summary>
        internal void SetHome(Vector3 home)
        {
            _homePosition = home;
        }

        /// <summary>
        /// 无限期挂起移动，直到 Release()。
        /// </summary>
        /// <remarks>
        /// 与 PauseFor(秒) 的区别：对话的时长事先不知道（玩家可能挂着 UI 不动），
        /// 用"暂停 N 秒"表达不了"停到对话结束为止"。
        /// </remarks>
        internal void Hold()
        {
            _held = true;
            if (!_ready)
            {
                return;
            }

            try
            {
                _pathControl.StopMove();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 挂起移动失败: " + e.Message);
            }
        }

        /// <summary>解除挂起，并在 idleSeconds 后才重新开始漫步。</summary>
        internal void Release(float idleSeconds)
        {
            _held = false;
            _nextMoveTime = Time.time + Mathf.Max(0f, idleSeconds);
        }

        /// <summary>当前是否被挂起。</summary>
        internal bool IsHeld
        {
            get { return _held; }
        }

        /// <summary>停下并在原地待命指定秒数（对话期间用）。</summary>
        internal void PauseFor(float seconds)
        {
            if (!_ready)
            {
                return;
            }

            try
            {
                _pathControl.StopMove();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 停止移动失败: " + e.Message);
            }

            _nextMoveTime = Time.time + Mathf.Max(0f, seconds);
        }

        /// <summary>是否正在移动。</summary>
        internal bool IsMoving
        {
            get
            {
                if (!_ready || _pathControl == null)
                {
                    return false;
                }
                try
                {
                    return _pathControl.Moving;
                }
                catch
                {
                    return false;
                }
            }
        }

        private string SafeName()
        {
            try
            {
                return gameObject.name;
            }
            catch
            {
                return "(未知)";
            }
        }
    }
}
