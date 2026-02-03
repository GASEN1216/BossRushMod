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
            seeker.StartPath(transform.position, pos, new OnPathDelegate(OnPathComplete));
            waitingForPathResult = true;
        }
        
        /// <summary>
        /// 跑向玩家（被召唤时调用）
        /// </summary>
        public void RunToPlayer(Vector3 playerPosition)
        {
            if (seeker == null) return;
            
            // 强制停止当前移动
            path = null;
            moving = false;
            waitingForPathResult = false;
            
            // 开始寻路到玩家位置
            reachedEndOfPath = false;
            seeker.StartPath(transform.position, playerPosition, new OnPathDelegate(OnPathComplete));
            waitingForPathResult = true;
            
            ModBehaviour.DevLog("[GoblinNPC] 开始寻路到玩家位置: " + playerPosition);
        }
        
        /// <summary>
        /// 停止移动
        /// </summary>
        public void StopMove()
        {
            path = null;
            moving = false;
            waitingForPathResult = false;
            UpdateMoveAnimation(0f);
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
            
            // 如果在对话中，停止所有移动
            if (controller != null && controller.IsInDialogue)
            {
                UpdateMoveAnimation(0f);
                ApplyGravity();
                return;
            }
            
            // 如果在待机中（急停后的3秒待机），不做任何移动
            if (controller != null && controller.IsIdling)
            {
                UpdateMoveAnimation(0f);
                ApplyGravity();
                return;
            }
            
            // 更新玩家引用
            if (playerTransform == null)
            {
                NPCExceptionHandler.TryExecute(() =>
                {
                    if (CharacterMainControl.Main != null)
                    {
                        playerTransform = CharacterMainControl.Main.transform;
                    }
                }, "GoblinMovement.Update - 更新玩家引用");
            }
            
            // 如果正在跑向玩家，不做漫步决策
            if (controller != null && controller.IsRunningToPlayer)
            {
                UpdatePathFollowing(true);  // 使用跑步速度
                ApplyGravity();
                return;
            }
            
            // 更新漫步决策
            UpdateWanderDecision();
            
            // 沿路径移动
            UpdatePathFollowing(false);  // 使用走路速度
            
            // 应用重力
            ApplyGravity();
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
        public void OnPathComplete(Pathfinding.Path p)
        {
            if (!p.error)
            {
                path = p;
                currentWaypoint = 0;
                moving = true;
                ModBehaviour.DevLog("[GoblinNPC] 路径计算成功，路点数: " + p.vectorPath.Count);
            }
            else
            {
                NPCExceptionHandler.LogAndIgnore(
                    new Exception(p.errorLog),
                    "GoblinMovement.OnPathComplete - 路径计算失败"
                );
            }
            waitingForPathResult = false;
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
        private void UpdatePathFollowing(bool isRunning)
        {
            if (waitingForPathResult) return;
            
            if (path == null)
            {
                moving = false;
                UpdateMoveAnimation(0f);
                return;
            }
            
            moving = true;
            reachedEndOfPath = false;
            
            // 检查是否到达当前路点
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
            
            // 计算移动方向
            Vector3 direction = path.vectorPath[currentWaypoint] - transform.position;
            direction.y = 0;
            direction = direction.normalized;
            
            // 计算移动速度
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
                    return;
                }
            }
            else
            {
                speedMultiplier = 1f;
            }
            
            // 计算实际移动速度
            float currentSpeed = (isRunning ? runSpeed : walkSpeed) * speedMultiplier;
            
            // 使用 CharacterController 移动
            Vector3 moveVector = direction * currentSpeed * Time.deltaTime;
            moveVector.y = verticalVelocity * Time.deltaTime;
            
            if (characterController != null && characterController.enabled)
            {
                characterController.Move(moveVector);
            }
            
            // 平滑转向
            Vector3 horizontalDir = direction;
            horizontalDir.y = 0;
            if (horizontalDir != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(horizontalDir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }
            
            // 更新动画
            UpdateMoveAnimation(currentSpeed);
        }
        
        /// <summary>
        /// 应用重力
        /// </summary>
        private void ApplyGravity()
        {
            if (characterController == null || !characterController.enabled) return;
            
            if (characterController.isGrounded)
            {
                verticalVelocity = GoblinMovementConstants.GROUNDED_VELOCITY;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }
        }
        
        /// <summary>
        /// 更新移动动画参数
        /// </summary>
        private void UpdateMoveAnimation(float speed)
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                if (animator != null)
                {
                    animator.SetFloat(hash_MoveSpeed, speed);
                    // 当速度为0时，设置 IsIdle 为 true，避免原地播放走路动画
                    animator.SetBool("IsIdle", speed < 0.01f);
                }
            }, "GoblinMovement.UpdateMoveAnimation");

            // 通知控制器更新动画
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
            Vector3 rayStart = pos + Vector3.up * 50f;
            
            RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, 100f);
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
            
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 100f))
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
