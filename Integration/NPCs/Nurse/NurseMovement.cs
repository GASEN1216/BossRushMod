// ============================================================================
// NurseMovement.cs - 护士移动控制
// ============================================================================
// 模块说明：
//   护士NPC漫步移动控制（使用 A* Pathfinding Seeker）。
//   目标点来源与哥布林/快递员一致：NPCSpawnConfig 配置的场景刷新点。
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using Pathfinding;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 护士漫步移动控制
    /// </summary>
    public class NurseMovement : NPCFollowMovementBase
    {
        private NurseNPCController controller;
        private Animator animator;
        private bool hasMoveSpeedParam = false;
        private bool hasIsRunningParam = false;
        private bool hasIsIdleParam = false;
        private bool animatorParamsChecked = false;

        // A* Pathfinding
        public Seeker seeker;
        public Pathfinding.Path path;
        public float nextWaypointDistance = 0.5f;
        private int currentWaypoint;
        private bool reachedEndOfPath;
        private bool moving;
        private bool waitingForPathResult;
        private int activePathRequestId;

        /// <summary>是否正在移动</summary>
        public bool IsMoving { get { return moving; } }

        /// <summary>是否正在等待路径计算</summary>
        public bool IsWaitingForPath { get { return waitingForPathResult; } }

        // 移动参数
        public float walkSpeed = 1.8f;
        public float runSpeed = 4.5f;
        public float turnSpeed = 360f;

        // 漫步参数
        private float wanderTimer = 0f;
        private const float WANDER_INTERVAL = 6f;
        private const float MIN_TARGET_DISTANCE = 5f;
        private const float MIN_TARGET_DISTANCE_SQR = MIN_TARGET_DISTANCE * MIN_TARGET_DISTANCE;
        private const float FOLLOW_REPATH_INTERVAL = 0.15f;
        private const float FOLLOW_STOP_DISTANCE = 2.8f;
        private const float FOLLOW_RUN_DISTANCE = 10f;
        private const float FOLLOW_TELEPORT_DISTANCE = 40f;
        private const float FOLLOW_SPEED_BOOST_DISTANCE = 10f;
        private const float FOLLOW_SPEED_RESET_DISTANCE = 8f;

        // 场景名
        private string sceneName;

        // 物理组件
        private CharacterController characterController;
        private float verticalVelocity = 0f;
        private const float GRAVITY = -9.8f;

        private bool isInitialized = false;

        // 动画参数
        private static readonly int hash_MoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int hash_IsRunning = Animator.StringToHash("IsRunning");
        private static readonly int hash_IsIdle = Animator.StringToHash("IsIdle");

        /// <summary>
        /// 设置当前场景名（用于读取配置刷新点）
        /// </summary>
        public void SetSceneName(string name)
        {
            sceneName = name;
        }

        private void Start()
        {
            ModBehaviour.DevLog("[NurseNPC] NurseMovement.Start 开始");

            controller = GetComponent<NurseNPCController>();
            animator = GetComponentInChildren<Animator>();
            CacheAnimatorParameters();
            InitializeFollowDefaults();

            StartCoroutine(InitializeDelayed());
        }

        private void Update()
        {
            if (!isInitialized) return;

            UpdateGravityVelocity();

            if (controller != null && controller.IsInDialogue)
            {
                UpdateMoveAnimation(0f);
                ApplyGravityOnly();
                return;
            }

            if (IsFollowingPlayer)
            {
                UpdateFollowDecision(moving, waitingForPathResult, path != null);
                if (!UpdatePathFollowing(ShouldRunWhileFollowing()))
                {
                    ApplyGravityOnly();
                }
                return;
            }

            UpdateWanderDecision();

            if (!UpdatePathFollowing(false))
            {
                ApplyGravityOnly();
            }
        }

        private IEnumerator InitializeDelayed()
        {
            yield return new WaitForSeconds(0.5f);

            ModBehaviour.DevLog("[NurseNPC] 开始初始化移动系统，当前位置: " + transform.position);

            characterController = GetComponent<CharacterController>();
            if (characterController == null)
            {
                characterController = gameObject.AddComponent<CharacterController>();
                // 与哥布林一致的碰撞体尺寸，避免护士模型小而显得悬空
                characterController.height = 1.2f;
                characterController.radius = 0.25f;
                characterController.center = new Vector3(0f, 0.6f, 0f);
                characterController.slopeLimit = 45f;
                characterController.stepOffset = 0.3f;
                characterController.skinWidth = 0.08f;
                ModBehaviour.DevLog("[NurseNPC] 添加 CharacterController 组件");
            }

            seeker = GetComponent<Seeker>();
            if (seeker == null)
            {
                seeker = gameObject.AddComponent<Seeker>();
                ModBehaviour.DevLog("[NurseNPC] 添加 Seeker 组件");
            }

            if (AstarPath.active == null)
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] A* Pathfinding 未激活，护士将无法寻路");
            }
            else
            {
                ModBehaviour.DevLog("[NurseNPC] A* Pathfinding 已激活");
            }

            isInitialized = true;
            ModBehaviour.DevLog("[NurseNPC] 移动系统初始化完成");
        }

        private void UpdateWanderDecision()
        {
            wanderTimer += Time.deltaTime;

            if (moving || waitingForPathResult)
            {
                return;
            }

            if (wanderTimer >= WANDER_INTERVAL)
            {
                wanderTimer = 0f;
                Vector3 targetPos = GetSharedRandomSpawnPoint();
                if (targetPos != Vector3.zero)
                {
                    MoveToPos(targetPos);
                }
            }
        }

        /// <returns>true 表示本帧已通过 CharacterController.Move 应用了水平移动（含重力）</returns>
        private bool UpdatePathFollowing(bool isRunning)
        {
            if (waitingForPathResult && path == null) return false;

            if (path == null || path.vectorPath == null || path.vectorPath.Count == 0)
            {
                NPCPathingHelper.StopMovement(
                    ref path, ref currentWaypoint, ref moving,
                    ref waitingForPathResult, UpdateMoveAnimation);
                return false;
            }

            if (currentWaypoint >= path.vectorPath.Count)
            {
                reachedEndOfPath = true;
                NPCPathingHelper.StopMovement(
                    ref path, ref currentWaypoint, ref moving,
                    ref waitingForPathResult, UpdateMoveAnimation,
                    "[NurseNPC]");
                return false;
            }

            moving = true;
            reachedEndOfPath = false;

            float distanceToWaypoint;
            while (true)
            {
                Vector3 toWaypoint = path.vectorPath[currentWaypoint] - transform.position;
                toWaypoint.y = 0f;
                distanceToWaypoint = toWaypoint.magnitude;

                if (distanceToWaypoint < nextWaypointDistance)
                {
                    if (currentWaypoint + 1 < path.vectorPath.Count)
                    {
                        currentWaypoint++;
                    }
                    else
                    {
                        reachedEndOfPath = true;
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            Vector3 moveDirection = path.vectorPath[currentWaypoint] - transform.position;
            moveDirection.y = 0f;
            moveDirection = moveDirection.normalized;
            if (moveDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }

            float speedMultiplier = 1f;
            if (reachedEndOfPath)
            {
                speedMultiplier = Mathf.Sqrt(distanceToWaypoint / nextWaypointDistance);

                if (distanceToWaypoint < FOLLOW_STOP_DISTANCE)
                {
                    path = null;
                    moving = false;
                    UpdateMoveAnimation(0f);
                    return false;
                }
            }

            float currentSpeed = (isRunning ? runSpeed : walkSpeed) * speedMultiplier;
            Vector3 moveVector = moveDirection * currentSpeed * Time.deltaTime;
            moveVector.y = verticalVelocity * Time.deltaTime;

            if (characterController != null)
            {
                characterController.Move(moveVector);
            }
            else
            {
                transform.position += moveDirection * currentSpeed * Time.deltaTime;
            }

            UpdateMoveAnimation(currentSpeed);
            return true;
        }

        /// <summary>
        /// 发起寻路请求
        /// </summary>
        public void MoveToPos(Vector3 pos)
        {
            if (seeker == null)
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] MoveToPos 失败：Seeker 为空");
                return;
            }

            if (waitingForPathResult || moving)
            {
                return;
            }

            reachedEndOfPath = false;
            int requestId = ++activePathRequestId;
            waitingForPathResult = true;
            seeker.StartPath(transform.position, pos, p => OnPathComplete(p, requestId));
            ModBehaviour.DevLog("[NurseNPC] 开始寻路到: " + pos);
        }

        /// <summary>
        /// 立即停止当前移动
        /// </summary>
        public void StopMove()
        {
            reachedEndOfPath = true;
            activePathRequestId++;
            NPCPathingHelper.StopMovement(
                ref path,
                ref currentWaypoint,
                ref moving,
                ref waitingForPathResult,
                UpdateMoveAnimation);
        }

        /// <summary>
        /// 寻路回调
        /// </summary>
        private void OnPathComplete(Pathfinding.Path p, int requestId)
        {
            bool shouldDiscard = controller != null &&
                (controller.IsInDialogue || controller.IsInStoryDialogue);

            NPCPathingHelper.HandlePathComplete(
                p,
                requestId,
                activePathRequestId,
                shouldDiscard,
                "NPC处于剧情/聊天状态",
                ref path,
                ref currentWaypoint,
                ref moving,
                ref waitingForPathResult,
                UpdateMoveAnimation,
                "[NurseNPC]");
        }

        private Vector3 GetSharedRandomSpawnPoint()
        {
            try
            {
                string targetScene = sceneName;
                if (string.IsNullOrEmpty(targetScene))
                {
                    targetScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                }

                Vector3[] spawnPoints = ModBehaviour.GetSharedCommonNPCSpawnPointsForScene(targetScene);

                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    return Vector3.zero;
                }

                int attempts = Mathf.Max(8, spawnPoints.Length * 2);
                for (int i = 0; i < attempts; i++)
                {
                    int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                    Vector3 targetPos = CorrectTargetHeight(spawnPoints[randomIndex]);

                    Vector3 diff = targetPos - transform.position;
                    diff.y = 0f;
                    if (diff.sqrMagnitude < MIN_TARGET_DISTANCE_SQR)
                    {
                        continue;
                    }

                    ModBehaviour.DevLog("[NurseNPC] 选择刷新点 [" + randomIndex + "/" + spawnPoints.Length + "] 作为目标: " + targetPos);
                    return targetPos;
                }

                return CorrectTargetHeight(spawnPoints[0]);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] 获取随机目标点失败: " + e.Message);
                return Vector3.zero;
            }
        }

        private Vector3 GetRandomSpawnPoint()
        {
            return GetSharedRandomSpawnPoint();
        }

        private Vector3 CorrectTargetHeight(Vector3 point)
        {
            return NPCExceptionHandler.TryExecute(() =>
            {
                RaycastHit hit;
                if (Physics.Raycast(point + Vector3.up * 5f, Vector3.down, out hit, 20f))
                {
                    // 与哥布林逻辑保持一致
                    return hit.point + new Vector3(0f, 0.1f, 0f);
                }
                return point;
            }, "NurseMovement.CorrectTargetHeight", point, false);
        }

        private void UpdateGravityVelocity()
        {
            if (characterController == null) return;

            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -0.5f;
            }
            else
            {
                verticalVelocity += GRAVITY * Time.deltaTime;
            }
        }

        private void ApplyGravityOnly()
        {
            if (characterController == null) return;
            characterController.Move(new Vector3(0f, verticalVelocity * Time.deltaTime, 0f));
        }

        private void UpdateMoveAnimation(float speed)
        {
            if (animator == null) return;
            if (!animatorParamsChecked) CacheAnimatorParameters();

            try
            {
                if (hasMoveSpeedParam)
                {
                    animator.SetFloat(hash_MoveSpeed, speed);
                }

                bool isIdle = speed < 0.01f;
                if (hasIsRunningParam)
                {
                    bool isRunning = runSpeed > walkSpeed && speed > walkSpeed + 0.05f;
                    animator.SetBool(hash_IsRunning, isRunning);
                }

                if (hasIsIdleParam)
                {
                    animator.SetBool(hash_IsIdle, isIdle);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] UpdateMoveAnimation 失败: " + e.Message);
            }
        }

        protected override float WalkSpeed
        {
            get { return walkSpeed; }
            set { walkSpeed = value; }
        }

        protected override float RunSpeed
        {
            get { return runSpeed; }
            set { runSpeed = value; }
        }

        protected override float FollowRepathInterval
        {
            get { return FOLLOW_REPATH_INTERVAL; }
        }

        protected override float FollowStopDistance
        {
            get { return FOLLOW_STOP_DISTANCE; }
        }

        protected override float FollowRunDistance
        {
            get { return FOLLOW_RUN_DISTANCE; }
        }

        protected override float FollowTeleportDistance
        {
            get { return FOLLOW_TELEPORT_DISTANCE; }
        }

        protected override float FollowSpeedBoostDistance
        {
            get { return FOLLOW_SPEED_BOOST_DISTANCE; }
        }

        protected override float FollowSpeedResetDistance
        {
            get { return FOLLOW_SPEED_RESET_DISTANCE; }
        }

        protected override Seeker FollowSeeker
        {
            get { return seeker; }
        }

        protected override CharacterController FollowCharacterController
        {
            get { return characterController; }
        }

        protected override int FollowActivePathRequestId
        {
            get { return activePathRequestId; }
            set { activePathRequestId = value; }
        }

        protected override bool FollowWaitingForPathResult
        {
            get { return waitingForPathResult; }
            set { waitingForPathResult = value; }
        }

        protected override bool FollowReachedEndOfPath
        {
            get { return reachedEndOfPath; }
            set { reachedEndOfPath = value; }
        }

        protected override Vector3 GetFollowDestination(Transform target)
        {
            if (target == null)
            {
                return transform.position;
            }

            return CorrectTargetHeight(target.position);
        }

        protected override void StopCurrentFollowMovement()
        {
            StopMove();
        }

        protected override void HandleFollowPathComplete(Path pathResult, int requestId)
        {
            OnPathComplete(pathResult, requestId);
        }

        private void CacheAnimatorParameters()
        {
            animatorParamsChecked = true;
            hasMoveSpeedParam = false;
            hasIsRunningParam = false;
            hasIsIdleParam = false;

            if (animator == null) return;

            foreach (var param in animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Float && param.nameHash == hash_MoveSpeed)
                {
                    hasMoveSpeedParam = true;
                }
                else if (param.type == AnimatorControllerParameterType.Bool)
                {
                    if (param.nameHash == hash_IsRunning)
                    {
                        hasIsRunningParam = true;
                    }
                    else if (param.nameHash == hash_IsIdle)
                    {
                        hasIsIdleParam = true;
                    }
                }
            }
        }
    }
}
