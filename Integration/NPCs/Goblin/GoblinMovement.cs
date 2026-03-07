// ============================================================================
// GoblinMovement.cs - 哥布林移动控制
// ============================================================================
// 模块说明：
//   哥布林移动控制（使用 A* Pathfinding Seeker）
//   复用快递员的移动逻辑，简化版
//   遵循 KISS/YAGNI/SOLID 原则
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using Pathfinding;
using BossRush.Constants;
using BossRush.Utils;

namespace BossRush
{
    /// <summary>
    /// 哥布林移动控制（使用 A* Pathfinding Seeker）
    /// </summary>
    public class GoblinMovement : MonoBehaviour
    {
        // ============================================================================
        // 组件引用
        // ============================================================================

        private GoblinNPCController controller;
        private Transform playerTransform;
        private Animator animator;

        // ============================================================================
        // A* Pathfinding 组件
        // ============================================================================

        public Seeker seeker;
        public Pathfinding.Path path;
        public float nextWaypointDistance = GoblinMovementConstants.NEXT_WAYPOINT_DISTANCE;
        private int currentWaypoint;
        private bool reachedEndOfPath;
        public float stopDistance = GoblinMovementConstants.STOP_DISTANCE;
        private bool moving;
        private bool waitingForPathResult;
        private int activePathRequestId;

        // ============================================================================
        // 公共属性
        // ============================================================================

        /// <summary>
        /// 是否正在移动
        /// </summary>
        public bool IsMoving { get { return moving; } }

        /// <summary>
        /// 是否正在等待路径计算结果
        /// </summary>
        public bool IsWaitingForPath { get { return waitingForPathResult; } }

        // ============================================================================
        // 移动参数
        // ============================================================================

        public float walkSpeed = GoblinMovementConstants.WALK_SPEED;
        public float runSpeed = GoblinMovementConstants.RUN_SPEED;
        public float turnSpeed = GoblinMovementConstants.TURN_SPEED;

        // ============================================================================
        // 漫步参数
        // ============================================================================

        public float wanderRadius = GoblinMovementConstants.WANDER_RADIUS;
        private float wanderTimer = 0f;
        private bool isInitialized = false;

        // ============================================================================
        // 场景和物理
        // ============================================================================

        private string sceneName;
        private CharacterController characterController;
        private float verticalVelocity = 0f;
        private float gravity = GoblinMovementConstants.GRAVITY;

        // ============================================================================
        // 动画参数哈希值
        // ============================================================================

        private static readonly int hash_MoveSpeed = Animator.StringToHash("MoveSpeed");

        // ============================================================================
        // 公共方法
        // ============================================================================

        /// <summary>
        /// 设置场景名称（用于获取刷新点）
        /// </summary>
        public void SetSceneName(string name)
        {
            sceneName = name;
        }

        /// <summary>
        /// 移动到指定位置
        /// </summary>
        public void MoveToPos(Vector3 pos)
        {
            if (seeker == null)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] MoveToPos 失败：Seeker 为空");
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
        }

        /// <summary>
        /// 跑向玩家（被召唤时调用）
        /// </summary>
        public void RunToPlayer(Vector3 playerPosition)
        {
            if (seeker == null) return;

            // 强制停止当前移动
            NPCPathingHelper.StopMovement(
                ref path,
                ref currentWaypoint,
                ref moving,
                ref waitingForPathResult,
                UpdateMoveAnimation);

            // 开始寻路到玩家位置
            reachedEndOfPath = false;
            int requestId = ++activePathRequestId;
            waitingForPathResult = true;
            seeker.StartPath(transform.position, playerPosition, p => OnPathComplete(p, requestId));

            ModBehaviour.DevLog("[GoblinNPC] 开始寻路到玩家位置: " + playerPosition);
        }

        /// <summary>
        /// 停止移动
        /// </summary>
        public void StopMove()
        {
            activePathRequestId++;
            NPCPathingHelper.StopMovement(
                ref path,
                ref currentWaypoint,
                ref moving,
                ref waitingForPathResult,
                UpdateMoveAnimation);
        }

        /// <summary>
        /// 恢复走路（急停后调用）
        /// </summary>
        public void ResumeWalking()
        {
            // 继续漫步
            wanderTimer = GoblinMovementConstants.WANDER_INTERVAL;  // 立即触发下一次漫步
        }

        // ============================================================================
        // 生命周期方法
        // ============================================================================

        void Start()
        {
            ModBehaviour.DevLog("[GoblinNPC] GoblinMovement.Start 开始");

            controller = GetComponent<GoblinNPCController>();
            animator = GetComponentInChildren<Animator>();

            // 获取玩家引用
            NPCExceptionHandler.TryExecute(() =>
            {
                if (CharacterMainControl.Main != null)
                {
                    playerTransform = CharacterMainControl.Main.transform;
                    ModBehaviour.DevLog("[GoblinNPC] 获取到玩家引用");
                }
            }, "GoblinMovement.Start - 获取玩家引用");

            if (playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                    ModBehaviour.DevLog("[GoblinNPC] 通过 Tag 获取到玩家引用");
                }
            }

            // 延迟初始化（等待 A* 图准备好）
            StartCoroutine(InitializeDelayed());
        }

        void Update()
        {
            if (!isInitialized) return;

            UpdateGravityVelocity();

            if (controller != null && controller.IsInDialogue)
            {
                UpdateMoveAnimation(0f);
                ApplyGravityOnly();
                return;
            }

            if (controller != null && controller.IsIdling)
            {
                UpdateMoveAnimation(0f);
                ApplyGravityOnly();
                return;
            }

            if (playerTransform == null)
            {
                try
                {
                    if (CharacterMainControl.Main != null)
                        playerTransform = CharacterMainControl.Main.transform;
                }
                catch { }
            }

            if (controller != null && controller.IsRunningToPlayer)
            {
                if (!UpdatePathFollowing(true))
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

        // ============================================================================
        // 私有方法
        // ============================================================================

        /// <summary>
        /// 延迟初始化，等待 A* 图准备好
        /// </summary>
        private IEnumerator InitializeDelayed()
        {
            yield return new WaitForSeconds(GoblinMovementConstants.INIT_DELAY);

            ModBehaviour.DevLog("[GoblinNPC] 开始初始化移动系统，当前位置: " + transform.position);

            // 1. 添加 CharacterController
            characterController = gameObject.GetComponent<CharacterController>();
            if (characterController == null)
            {
                characterController = gameObject.AddComponent<CharacterController>();
                characterController.height = GoblinMovementConstants.CHARACTER_HEIGHT;
                characterController.radius = GoblinMovementConstants.CHARACTER_RADIUS;
                characterController.center = new Vector3(0f, GoblinMovementConstants.CHARACTER_CENTER_Y, 0f);
                characterController.slopeLimit = GoblinMovementConstants.SLOPE_LIMIT;
                characterController.stepOffset = GoblinMovementConstants.STEP_OFFSET;
                characterController.skinWidth = GoblinMovementConstants.SKIN_WIDTH;
                ModBehaviour.DevLog("[GoblinNPC] 添加 CharacterController 组件");
            }

            // 2. 添加 Seeker 组件
            seeker = gameObject.GetComponent<Seeker>();
            if (seeker == null)
            {
                seeker = gameObject.AddComponent<Seeker>();
                ModBehaviour.DevLog("[GoblinNPC] 添加 Seeker 组件");
            }

            // 3. 检查 A* 是否可用
            if (AstarPath.active == null)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] 警告：A* Pathfinding 未激活！哥布林将无法寻路");
            }
            else
            {
                ModBehaviour.DevLog("[GoblinNPC] A* Pathfinding 已激活");
            }

            isInitialized = true;
            ModBehaviour.DevLog("[GoblinNPC] 移动系统初始化完成");
        }

        /// <summary>
        /// 路径计算完成回调
        /// </summary>
        private void OnPathComplete(Pathfinding.Path p, int requestId)
        {
            bool shouldDiscard = controller != null &&
                (controller.IsInDialogue || controller.IsInStoryDialogue ||
                 (controller.IsIdling && !controller.IsRunningToPlayer));

            NPCPathingHelper.HandlePathComplete(
                p,
                requestId,
                activePathRequestId,
                shouldDiscard,
                "NPC处于剧情/聊天/待机状态",
                ref path,
                ref currentWaypoint,
                ref moving,
                ref waitingForPathResult,
                UpdateMoveAnimation,
                "[GoblinNPC]");
        }

        /// <summary>
        /// 更新漫步决策
        /// </summary>
        private void UpdateWanderDecision()
        {
            wanderTimer += Time.deltaTime;

            // 如果正在移动或等待路径结果，不做新的决策
            if (moving || waitingForPathResult)
            {
                return;
            }

            // 定时漫步
            if (wanderTimer >= GoblinMovementConstants.WANDER_INTERVAL)
            {
                wanderTimer = 0f;

                // 获取随机刷新点作为目标
                Vector3 targetPos = GetRandomSpawnPoint();
                if (targetPos != Vector3.zero)
                {
                    MoveToPos(targetPos);
                }
            }
        }

        /// <summary>
        /// 沿路径移动
        /// </summary>
        /// <returns>true 表示本帧已通过 CharacterController.Move 应用了水平移动（含重力）</returns>
        private bool UpdatePathFollowing(bool isRunning)
        {
            if (waitingForPathResult) return false;

            if (path == null)
            {
                moving = false;
                UpdateMoveAnimation(0f);
                return false;
            }

            moving = true;
            reachedEndOfPath = false;

            float distanceToWaypoint;
            while (true)
            {
                Vector3 toWaypoint = path.vectorPath[currentWaypoint] - transform.position;
                toWaypoint.y = 0;
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

            Vector3 direction = path.vectorPath[currentWaypoint] - transform.position;
            direction.y = 0;
            direction = direction.normalized;

            float speedMultiplier;
            if (reachedEndOfPath)
            {
                speedMultiplier = Mathf.Sqrt(distanceToWaypoint / nextWaypointDistance);

                if (distanceToWaypoint < stopDistance)
                {
                    path = null;
                    moving = false;
                    UpdateMoveAnimation(0f);
                    ModBehaviour.DevLog("[GoblinNPC] 到达目标点");
                    return false;
                }
            }
            else
            {
                speedMultiplier = 1f;
            }

            float currentSpeed = (isRunning ? runSpeed : walkSpeed) * speedMultiplier;

            Vector3 moveVector = direction * currentSpeed * Time.deltaTime;
            moveVector.y = verticalVelocity * Time.deltaTime;

            if (characterController != null && characterController.enabled)
            {
                characterController.Move(moveVector);
            }

            Vector3 horizontalDir = direction;
            horizontalDir.y = 0;
            if (horizontalDir != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(horizontalDir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }

            UpdateMoveAnimation(currentSpeed);
            return true;
        }

        private void UpdateGravityVelocity()
        {
            if (characterController == null || !characterController.enabled) return;

            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = GoblinMovementConstants.GROUNDED_VELOCITY;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }
        }

        private void ApplyGravityOnly()
        {
            if (characterController == null || !characterController.enabled) return;
            characterController.Move(new Vector3(0f, verticalVelocity * Time.deltaTime, 0f));
        }

        /// <summary>
        /// 更新移动动画参数
        /// </summary>
        private void UpdateMoveAnimation(float speed)
        {
            try
            {
                if (animator != null)
                {
                    animator.SetFloat(hash_MoveSpeed, speed);
                    animator.SetBool("IsIdle", speed < 0.01f);
                }
            }
            catch (Exception e)
            {
                NPCExceptionHandler.LogAndIgnore(e, "GoblinMovement.UpdateMoveAnimation");
            }

            if (controller != null)
            {
                controller.UpdateMoveSpeed(speed);
            }
        }

        /// <summary>
        /// 修正目标点的Y坐标到地面
        /// </summary>
        private Vector3 CorrectTargetHeight(Vector3 pos)
        {
            RaycastHit hit;
            Vector3 rayStart = pos + Vector3.up * 1f;

            RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, 5f);
            if (hits != null && hits.Length > 0)
            {
                float lowestY = float.MaxValue;
                float configY = pos.y;
                float bestY = pos.y;

                foreach (var h in hits)
                {
                    if (Mathf.Abs(h.point.y - configY) < 1f)
                    {
                        bestY = h.point.y + 0.1f;
                        break;
                    }
                    if (h.point.y < lowestY)
                    {
                        lowestY = h.point.y;
                        bestY = h.point.y + 0.1f;
                    }
                }

                return new Vector3(pos.x, bestY, pos.z);
            }

            if (Physics.Raycast(rayStart, Vector3.down, out hit, 5f))
            {
                return new Vector3(pos.x, hit.point.y + 0.1f, pos.z);
            }

            return pos;
        }

        /// <summary>
        /// 获取随机的刷新点作为目标
        /// </summary>
        private Vector3 GetRandomSpawnPoint()
        {
            return NPCExceptionHandler.TryExecute(() =>
            {
                // 从 NPCSpawnConfig 获取哥布林刷新点
                if (!string.IsNullOrEmpty(sceneName) &&
                    NPCSpawnConfig.GoblinSpawnConfigs.TryGetValue(sceneName, out NPCSceneSpawnConfig config))
                {
                    Vector3[] spawnPoints = config.spawnPoints;
                    if (spawnPoints != null && spawnPoints.Length > 0)
                    {
                        // 随机选择一个刷新点（排除当前位置附近的点）
                        int maxAttempts = 5;
                        for (int i = 0; i < maxAttempts; i++)
                        {
                            int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                            Vector3 targetPos = spawnPoints[randomIndex];

                            // 修正高度
                            targetPos = CorrectTargetHeight(targetPos);

                            // 确保目标点与当前位置有一定距离
                            Vector3 diff = targetPos - transform.position;
                            diff.y = 0;
                            float distance = diff.magnitude;
                            if (distance > 3f)
                            {
                                return targetPos;
                            }
                        }

                        // 多次尝试都找不到合适的点，用第一个
                        return CorrectTargetHeight(spawnPoints[0]);
                    }
                }

                // 如果没有配置，使用 Boss 刷新点（回退逻辑）
                if (ModBehaviour.Instance != null)
                {
                    Vector3[] bossSpawnPoints = ModBehaviour.Instance.GetCurrentSceneSpawnPoints();
                    if (bossSpawnPoints != null && bossSpawnPoints.Length > 0)
                    {
                        int randomIndex = UnityEngine.Random.Range(0, bossSpawnPoints.Length);
                        return CorrectTargetHeight(bossSpawnPoints[randomIndex]);
                    }
                }

                return Vector3.zero;
            }, "GoblinMovement.GetRandomSpawnPoint", Vector3.zero);
        }
    }
}
