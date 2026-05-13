// ============================================================================
// CourierNPC partial source - extracted from CourierNPC.cs
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Duckov.Utilities;
using Pathfinding;
using Cysharp.Threading.Tasks;
using Saves;
using ItemStatsSystem;
using Dialogues;
using NodeCanvas.DialogueTrees;
using SodaCraft.Localizations;
using BossRush.Utils;

namespace BossRush
{
    public class CourierMovement : MonoBehaviour
    {
        private CourierNPCController controller;
        private Transform playerTransform;
        private Animator animator;
        private bool hasMoveSpeedParam = false;
        private bool animatorParamsChecked = false;

        // A* Pathfinding 组件（与原版 AI_PathControl 完全一致）
        public Seeker seeker;
        public Pathfinding.Path path;
        public float nextWaypointDistance = 0.5f;  // 减小到0.5米，避免过早跳过路点
        private int currentWaypoint;
        private bool reachedEndOfPath;
        public float stopDistance = 0.3f;  // 停止距离
        private bool moving;
        private bool waitingForPathResult;
        private int activePathRequestId;

        // 移动参数
        public float walkSpeed = 2f;
        public float runSpeed = 5f;
        public float turnSpeed = 360f;

        // 漫步参数
        public float wanderRadius = 10f;
        public float safeDistance = 5f;  // 安全距离5米，玩家靠近时触发逃跑

        private bool isBossFight = false;
        private bool isCompleted = false;
        private float wanderTimer = 0f;
        private const float WANDER_INTERVAL = 4f;
        private bool isInitialized = false;

        // 待机状态（到达目标后播放待机动画）
        private bool isIdling = false;
        private float idleTimer = 0f;
        private const float IDLE_DURATION = 5f;  // 待机5秒

        // 快递服务状态（服务期间停止移动）
        private bool isInService = false;

        // 普通模式标志和刷新点缓存（非BossRush模式时使用NPCSpawnConfig中的刷新点）
        private bool isNormalMode = false;
        private Vector3[] normalModeSpawnPoints = null;

        // 气泡显示计时器
        private float cheerBubbleTimer = 0f;
        private float victoryBubbleTimer = 0f;
        private const float CHEER_BUBBLE_INTERVAL = 5f;  // 加油气泡间隔5秒
        private const float VICTORY_BUBBLE_INTERVAL = 5f;  // 胜利气泡间隔5秒

        // CharacterController 用于物理碰撞（因为快递员没有 CharacterMainControl）
        private CharacterController characterController;
        private float verticalVelocity = 0f;
        private float gravity = -9.8f;

        // 动画参数哈希值
        private static readonly int hash_MoveSpeed = Animator.StringToHash("MoveSpeed");

        // 属性（与原版 AI_PathControl 一致）
        public bool ReachedEndOfPath { get { return reachedEndOfPath; } }
        public bool Moving { get { return moving; } }
        public bool WaitingForPathResult { get { return waitingForPathResult; } }

        // 延迟恢复移动的协程引用（用于取消）
        private Coroutine delayedResumeCoroutine = null;

        // Mode E 固定模式（站在原地不移动）
        private bool isStationary = false;
        private bool scriptedOverrideActive = false;
        private bool scriptedRunMode = false;

        /// <summary>
        /// 设置固定模式（Mode E 使用，快递员站在原地不移动）
        /// </summary>
        public void SetStationary(bool stationary)
        {
            isStationary = stationary;
            if (stationary)
            {
                StopMove();
                ModBehaviour.DevLog("[CourierNPC] 已设置为固定模式，不会移动");
            }
        }

        public void SetScriptedOverride(bool active)
        {
            if (scriptedOverrideActive == active)
            {
                return;
            }

            scriptedOverrideActive = active;
            if (delayedResumeCoroutine != null)
            {
                StopCoroutine(delayedResumeCoroutine);
                delayedResumeCoroutine = null;
            }

            isInService = false;
            isIdling = false;
            idleTimer = 0f;
            StopMove();
            ModBehaviour.DevLog("[CourierNPC] SetScriptedOverride: " + active);
        }

        public void SetScriptedRunMode(bool active)
        {
            scriptedRunMode = active;
        }

        public bool IsIdling
        {
            get { return isIdling; }
        }

        public void ClearIdleState()
        {
            isIdling = false;
            idleTimer = 0f;
            if (controller != null)
            {
                controller.StopTalking();
            }
        }

        /// <summary>
        /// 设置普通模式（非BossRush模式）
        /// 普通模式下使用 NPCSpawnConfig 中配置的刷新点作为漫步目标
        /// </summary>
        /// <param name="normalMode">是否为普通模式</param>
        /// <param name="sceneName">场景名称，用于获取对应的刷新点配置</param>
        public void SetNormalMode(bool normalMode, string sceneName = null)
        {
            isNormalMode = normalMode;
            normalModeSpawnPoints = null;

            if (normalMode && !string.IsNullOrEmpty(sceneName))
            {
                // 从 NPCSpawnConfig 获取普通模式刷新点
                if (NPCSpawnConfig.CourierNormalModeConfigs.TryGetValue(sceneName, out NPCSceneSpawnConfig config))
                {
                    normalModeSpawnPoints = config.spawnPoints;
                    ModBehaviour.DevLog("[CourierNPC] 设置普通模式，场景: " + sceneName + ", 刷新点数: " + (normalModeSpawnPoints?.Length ?? 0));
                }
                else
                {
                    ModBehaviour.DevLog("[CourierNPC] 普通模式场景 " + sceneName + " 未配置刷新点");
                }
            }
            else
            {
                ModBehaviour.DevLog("[CourierNPC] 设置为BossRush模式");
            }
        }

        /// <summary>
        /// 设置快递服务状态（服务期间停止移动）
        /// </summary>
        public void SetInService(bool inService)
        {
            if (inService)
            {
                // 进入服务状态：取消之前的延迟恢复协程，立即停止移动
                if (delayedResumeCoroutine != null)
                {
                    StopCoroutine(delayedResumeCoroutine);
                    delayedResumeCoroutine = null;
                    ModBehaviour.DevLog("[CourierNPC] 取消之前的延迟恢复协程");
                }
                isInService = true;
                StopMove();
                ModBehaviour.DevLog("[CourierNPC] 进入快递服务状态，停止移动");
            }
            else
            {
                // 退出服务状态
                // 如果正在待机期间，重置待机计时器并重新触发待机动画，避免滑步
                if (isIdling)
                {
                    idleTimer = 0f;
                    if (controller != null)
                    {
                        controller.StartTalking();  // 重新触发待机动画
                    }
                    ModBehaviour.DevLog("[CourierNPC] 退出服务状态，继续待机动画");
                }
                // 延迟1秒后恢复移动
                ModBehaviour.DevLog("[CourierNPC] 退出快递服务状态，1秒后恢复移动");
                delayedResumeCoroutine = StartCoroutine(DelayedResumeMovement());
            }
        }

        /// <summary>
        /// 延迟恢复移动（UI关闭后等待1秒再开始走动）
        /// </summary>
        private IEnumerator DelayedResumeMovement()
        {
            yield return new WaitForSeconds(1f);
            // 只有在仍处于非服务状态时才恢复移动（双重保险）
            if (!isInService)
            {
                ModBehaviour.DevLog("[CourierNPC] 延迟结束，恢复移动");
            }
            isInService = false;
            delayedResumeCoroutine = null;
        }

        void Start()
        {
            ModBehaviour.DevLog("[CourierNPC] CourierMovement.Start 开始");

            controller = GetComponent<CourierNPCController>();
            animator = GetComponentInChildren<Animator>();
            CacheAnimatorParameters();

            // 获取玩家引用
            try
            {
                if (CharacterMainControl.Main != null)
                {
                    playerTransform = CharacterMainControl.Main.transform;
                    ModBehaviour.DevLog("[CourierNPC] 获取到玩家引用");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 获取玩家引用失败: " + e.Message);
            }

            if (playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                    ModBehaviour.DevLog("[CourierNPC] 通过 Tag 获取到玩家引用");
                }
            }

            // 延迟初始化（等待 A* 图准备好）
            StartCoroutine(InitializeDelayed());
        }

        /// <summary>
        /// 延迟初始化，等待 A* 图准备好
        /// </summary>
        private IEnumerator InitializeDelayed()
        {
            yield return new WaitForSeconds(0.5f);

            ModBehaviour.DevLog("[CourierNPC] 开始初始化移动系统，当前位置: " + transform.position);

            // 1. 添加 CharacterController（用于物理碰撞，因为快递员没有 CharacterMainControl）
            characterController = gameObject.GetComponent<CharacterController>();
            if (characterController == null)
            {
                characterController = gameObject.AddComponent<CharacterController>();
                characterController.height = 2f;
                characterController.radius = 0.3f;
                characterController.center = new Vector3(0f, 1f, 0f);
                characterController.slopeLimit = 45f;
                characterController.stepOffset = 0.3f;
                characterController.skinWidth = 0.08f;
                ModBehaviour.DevLog("[CourierNPC] 添加 CharacterController 组件");
            }

            // 2. 添加 Seeker 组件（A* Pathfinding 核心组件，与原版 AI_PathControl 一致）
            seeker = gameObject.GetComponent<Seeker>();
            if (seeker == null)
            {
                seeker = gameObject.AddComponent<Seeker>();
                ModBehaviour.DevLog("[CourierNPC] 添加 Seeker 组件");
            }
            else
            {
                ModBehaviour.DevLog("[CourierNPC] Seeker 组件已存在");
            }

            // 3. 检查 A* 是否可用
            if (AstarPath.active == null)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 警告：A* Pathfinding 未激活！快递员将无法寻路");
            }
            else
            {
                ModBehaviour.DevLog("[CourierNPC] A* Pathfinding 已激活，图数量: " + AstarPath.active.graphs.Length);

                // 列出所有图的类型
                for (int i = 0; i < AstarPath.active.graphs.Length; i++)
                {
                    var graph = AstarPath.active.graphs[i];
                    if (graph != null)
                    {
                        ModBehaviour.DevLog("[CourierNPC]   图[" + i + "]: " + graph.GetType().Name);
                    }
                }
            }

            isInitialized = true;
            ModBehaviour.DevLog("[CourierNPC] 移动系统初始化完成（使用 A* Seeker）");

            // 固定模式下设置 IsTalking 并跳过寻路测试
            if (isStationary)
            {
                if (controller != null)
                {
                    controller.SetStationary(true);
                    controller.StartTalking();
                }
                ModBehaviour.DevLog("[CourierNPC] 固定模式，已设置 IsTalking=true，跳过寻路测试");
                yield break;
            }

            // 立即尝试一次寻路测试
            yield return new WaitForSeconds(0.5f);
            if (seeker != null && AstarPath.active != null)
            {
                Vector3 testTarget = transform.position + new Vector3(2f, 0f, 2f);
                ModBehaviour.DevLog("[CourierNPC] 测试寻路到: " + testTarget);
                MoveToPos(testTarget);
            }
        }

        /// <summary>
        /// 移动到指定位置（与原版 AI_PathControl.MoveToPos 完全一致）
        /// </summary>
        public void MoveToPos(Vector3 pos)
        {
            if (seeker == null)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] MoveToPos 失败：Seeker 为空");
                return;
            }
            if (waitingForPathResult || moving)
            {
                return;  // 正在等待路径结果或正在移动，不重复请求
            }

            reachedEndOfPath = false;
            // 开始移动时，设置 IsArrived 为 false
            if (controller != null)
            {
                controller.SetArrived(false);
            }
            // 注意：不要在这里清空 path，否则会导致 UpdatePathFollowing 立即将 moving 设为 false
            int requestId = ++activePathRequestId;
            waitingForPathResult = true;
            seeker.StartPath(transform.position, pos, p => OnPathComplete(p, requestId));
            ModBehaviour.DevLog("[CourierNPC] 开始寻路到: " + pos);
        }

        /// <summary>
        /// 路径计算完成回调
        /// </summary>
        private void OnPathComplete(Pathfinding.Path p, int requestId)
        {
            bool shouldDiscard = isInService || isStationary || isCompleted;

            NPCPathingHelper.HandlePathComplete(
                p,
                requestId,
                activePathRequestId,
                shouldDiscard,
                "NPC处于服务/固定/通关状态",
                ref path,
                ref currentWaypoint,
                ref moving,
                ref waitingForPathResult,
                UpdateMoveAnimation,
                "[CourierNPC]");
        }

        /// <summary>
        /// 停止移动（与原版 AI_PathControl.StopMove 一致）
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

        void Update()
        {
            if (!isInitialized) return;

            UpdateGravityVelocity();

            if (isStationary && !scriptedOverrideActive)
            {
                FacePlayer();
                ApplyGravityOnly();
                return;
            }

            if (isInService && !scriptedOverrideActive)
            {
                UpdateMoveAnimation(0f);
                ApplyGravityOnly();
                return;
            }

            if (isCompleted)
            {
                StopMove();
                victoryBubbleTimer += Time.deltaTime;
                if (victoryBubbleTimer >= VICTORY_BUBBLE_INTERVAL)
                {
                    victoryBubbleTimer = 0f;
                    FacePlayer();
                    ShowBubble(LocalizationHelper.GetLocalizedText("BossRush_CourierVictory"), 3f);
                }
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

            if (!scriptedOverrideActive)
            {
                UpdateMovementDecision();
            }

            if (!UpdatePathFollowing())
            {
                ApplyGravityOnly();
            }
        }

        /// <summary>
        /// 更新移动决策（决定去哪里）
        /// </summary>
        private void UpdateMovementDecision()
        {
            // 如果正在待机，更新待机计时器
            if (isIdling)
            {
                idleTimer += Time.deltaTime;
                if (idleTimer >= IDLE_DURATION)
                {
                    // 待机结束，停止待机动画
                    isIdling = false;
                    idleTimer = 0f;
                    if (controller != null)
                    {
                        controller.StopTalking();
                    }
                    ModBehaviour.DevLog("[CourierNPC] 待机结束，准备继续移动");
                }
                return;  // 待机期间不做移动决策
            }

            wanderTimer += Time.deltaTime;

            // 如果正在移动或等待路径结果，不做新的决策
            if (moving || waitingForPathResult)
            {
                return;
            }

            if (isBossFight && playerTransform != null)
            {
                // 打Boss时：保持安全距离，如果玩家太近则跑到远离玩家的 Boss 刷新点
                float safeDistanceSqr = safeDistance * safeDistance;
                if ((transform.position - playerTransform.position).sqrMagnitude < safeDistanceSqr)
                {
                    // 触发逃跑，显示逃跑气泡
                    ShowBubble(LocalizationHelper.GetLocalizedText("BossRush_CourierFlee"), 3f);

                    // 从 Boss 刷新点中选择一个远离玩家的点
                    Vector3 targetPos = GetSpawnPointAwayFromPlayer();
                    if (targetPos != Vector3.zero)
                    {
                        MoveToPos(targetPos);
                    }
                }
                else
                {
                    // 玩家距离大于安全距离，显示加油气泡（每5秒一次）
                    cheerBubbleTimer += Time.deltaTime;
                    if (cheerBubbleTimer >= CHEER_BUBBLE_INTERVAL)
                    {
                        cheerBubbleTimer = 0f;
                        FacePlayer();  // 面向玩家
                        ShowBubble(LocalizationHelper.GetLocalizedText("BossRush_CourierCheer"), 3f);
                    }
                }
            }
            else
            {
                // 没打Boss时：在Boss刷新点之间随机走动
                bool needNewTarget = wanderTimer >= WANDER_INTERVAL;
                bool reachedTarget = path == null || reachedEndOfPath;

                if (needNewTarget || reachedTarget)
                {
                    wanderTimer = 0f;

                    // 从 ModBehaviour 获取 Boss 刷新点
                    Vector3 targetPos = GetRandomSpawnPoint();
                    if (targetPos != Vector3.zero)
                    {
                        MoveToPos(targetPos);
                    }
                }
            }
        }

        /// <returns>true 表示本帧已通过 CharacterController.Move 应用了水平移动（含重力）</returns>
        private bool UpdatePathFollowing()
        {
            if (waitingForPathResult)
            {
                return false;
            }

            if (path == null)
            {
                moving = false;
                UpdateMoveAnimation(0f);
                return false;
            }

            moving = true;
            reachedEndOfPath = false;

            // 检查是否到达当前路点（使用水平距离，忽略Y轴差异）
            float distanceToWaypoint;
            while (true)
            {
                // 使用水平距离，避免因高度差导致卡住
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

            // 计算移动方向（只在水平面上移动，忽略Y轴）
            Vector3 direction = path.vectorPath[currentWaypoint] - transform.position;
            direction.y = 0;
            direction = direction.normalized;

            // 计算移动输入（与原版逻辑一致：接近终点时减速）
            float speedMultiplier;
            if (reachedEndOfPath)
            {
                speedMultiplier = Mathf.Sqrt(distanceToWaypoint / nextWaypointDistance);

                // 到达停止距离时完全停止（与原版一致）
                if (distanceToWaypoint < stopDistance)
                {
                    path = null;
                    moving = false;
                    UpdateMoveAnimation(0f);

                    // 到达目标点，设置 IsArrived 为 true
                    if (controller != null)
                    {
                        controller.SetArrived(true);
                    }

                    // 注意：不再调用 SnapToGround()，因为目标点已在 GetRandomSpawnPoint 中预先修正高度

                    isIdling = true;
                    idleTimer = 0f;
                    if (controller != null)
                    {
                        controller.StartTalking(!scriptedOverrideActive);
                    }
                    ModBehaviour.DevLog("[CourierNPC] 到达目标点，开始待机动画");
                    return false;
                }
            }
            else
            {
                speedMultiplier = 1f;
            }

            // 计算实际移动速度
            float currentSpeed = ((isBossFight || scriptedRunMode) ? runSpeed : walkSpeed) * speedMultiplier;

            // 使用 CharacterController 移动（因为快递员没有 CharacterMainControl）
            Vector3 moveVector = direction * currentSpeed * Time.deltaTime;
            moveVector.y = verticalVelocity * Time.deltaTime;

            if (characterController != null && characterController.enabled)
            {
                CollisionFlags flags = characterController.Move(moveVector);
                // [性能优化] 调试日志：每5秒输出一次位置（降低日志频率）
                if (Time.frameCount % 300 == 0)
                {
                    ModBehaviour.DevLog("[CourierNPC] 移动中: 位置=" + transform.position + ", 速度=" + currentSpeed + ", 方向=" + direction + ", 路点=" + currentWaypoint + "/" + path.vectorPath.Count);
                }
            }

            // 平滑转向
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
                verticalVelocity = -0.5f;
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
        /// 将快递员位置修正到地面（只在到达目标点时调用一次）
        /// </summary>
        private void SnapToGround()
        {
            // 从当前位置向下发射射线，找到地面
            RaycastHit hit;
            Vector3 rayStart = transform.position + Vector3.up * 1f;
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 5f))
            {
                // 临时禁用 CharacterController 以直接设置位置
                if (characterController != null)
                {
                    characterController.enabled = false;
                }

                // 设置位置到地面上方一点（CharacterController 的中心在 1 米高）
                Vector3 newPos = hit.point + new Vector3(0f, 0.1f, 0f);
                transform.position = newPos;

                // 重新启用 CharacterController
                if (characterController != null)
                {
                    characterController.enabled = true;
                }

                // 重置垂直速度
                verticalVelocity = 0f;

                ModBehaviour.DevLog("[CourierNPC] 位置修正到地面: " + newPos);
            }
        }

        /// <summary>
        /// 更新移动动画参数
        /// </summary>
        private void UpdateMoveAnimation(float speed)
        {
            if (animator == null) return;
            if (!animatorParamsChecked) CacheAnimatorParameters();
            if (!hasMoveSpeedParam) return;

            NPCExceptionHandler.TryExecute(
                () => animator.SetFloat(hash_MoveSpeed, speed),
                "CourierMovement.UpdateMoveAnimation",
                false);
        }

        private void CacheAnimatorParameters()
        {
            animatorParamsChecked = true;
            hasMoveSpeedParam = false;

            if (animator == null) return;

            foreach (var param in animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Float &&
                    param.nameHash == hash_MoveSpeed)
                {
                    hasMoveSpeedParam = true;
                    break;
                }
            }
        }

        public void SetBossFight(bool fighting)
        {
            isBossFight = fighting;
            if (fighting)
            {
                wanderTimer = WANDER_INTERVAL;  // 立即触发移动决策
            }
            ModBehaviour.DevLog("[CourierNPC] SetBossFight: " + fighting);
        }

        public void SetCompleted(bool completed)
        {
            isCompleted = completed;
            if (completed)
            {
                StopMove();
            }
            ModBehaviour.DevLog("[CourierNPC] SetCompleted: " + completed);
        }

        /// <summary>
        /// 设置是否没有Boss（召唤间隔期间）
        /// </summary>
        public void SetNoBoss(bool noBoss)
        {
            // CourierMovement 不需要存储这个状态，只需要通知 Controller
            if (controller != null)
            {
                controller.SetNoBoss(noBoss);
            }
            ModBehaviour.DevLog("[CourierNPC] Movement.SetNoBoss: " + noBoss);
        }

        /// <summary>
        /// 修正目标点的Y坐标到地面（使用Raycast预先计算，避免到达后下沉）
        /// [修复] 从更高位置发射射线，并使用多次射线检测找到最低的地面点
        /// 这样可以避免在室内场景中错误地返回房顶高度
        /// </summary>
        private Vector3 CorrectTargetHeight(Vector3 pos)
        {
            RaycastHit hit;
            // [修复] 从更高位置发射射线（1米，防止卡到屋顶）
            Vector3 rayStart = pos + Vector3.up * 1f;

            // 使用 RaycastAll 获取所有碰撞点，然后选择最低的地面点
            RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, 5f);
            if (hits != null && hits.Length > 0)
            {
                // 找到最低的碰撞点（最接近配置的 Y 坐标）
                float lowestY = float.MaxValue;
                float configY = pos.y;
                float bestY = pos.y;

                foreach (var h in hits)
                {
                    // 优先选择接近配置 Y 坐标的点（允许 1 米误差）
                    if (Mathf.Abs(h.point.y - configY) < 1f)
                    {
                        bestY = h.point.y + 0.1f;
                        break;
                    }
                    // 否则选择最低的点
                    if (h.point.y < lowestY)
                    {
                        lowestY = h.point.y;
                        bestY = h.point.y + 0.1f;
                    }
                }

                return new Vector3(pos.x, bestY, pos.z);
            }

            // 如果没有碰撞，使用单次射线检测
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 5f))
            {
                return new Vector3(pos.x, hit.point.y + 0.1f, pos.z);
            }

            return pos;
        }

        /// <summary>
        /// 让快递员面向玩家
        /// </summary>
        private void FacePlayer()
        {
            if (playerTransform == null) return;

            // 计算朝向玩家的方向（只在水平面上）
            Vector3 direction = playerTransform.position - transform.position;
            direction.y = 0;

            if (direction != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }

        /// <summary>
        /// 显示气泡对话
        /// </summary>
        private void ShowBubble(string dialogue, float duration)
        {
            try
            {
                // 使用原版气泡系统显示对话（speed=-1表示一次性显示全部文字）
                float yOffset = 1.5f;
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(dialogue, transform, yOffset, false, false, -1f, duration)
                );
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 显示气泡失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取随机的刷新点作为目标（预先修正Y坐标到地面）
        /// 普通模式使用 NPCSpawnConfig 中的刷新点，BossRush模式使用 Boss 刷新点
        /// </summary>
        private Vector3 GetRandomSpawnPoint()
        {
            try
            {
                Vector3[] spawnPoints = null;

                // 根据模式选择刷新点来源
                if (isNormalMode && normalModeSpawnPoints != null && normalModeSpawnPoints.Length > 0)
                {
                    // 普通模式：使用 NPCSpawnConfig 中配置的刷新点
                    spawnPoints = normalModeSpawnPoints;
                    ModBehaviour.DevLog("[CourierNPC] 使用普通模式刷新点，共 " + spawnPoints.Length + " 个");
                }
                else if (ModBehaviour.Instance != null)
                {
                    // BossRush模式：使用 Boss 刷新点
                    spawnPoints = ModBehaviour.Instance.GetCurrentSceneSpawnPoints();
                }

                if (spawnPoints != null && spawnPoints.Length > 0)
                {
                    // 随机选择一个刷新点（排除当前位置附近的点）
                    int maxAttempts = 5;
                    for (int i = 0; i < maxAttempts; i++)
                    {
                        int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                        Vector3 targetPos = spawnPoints[randomIndex];

                        // 使用 Raycast 修正目标点的 Y 坐标到地面
                        targetPos = CorrectTargetHeight(targetPos);

                        // 确保目标点与当前位置有一定距离（使用水平距离）
                        Vector3 diff = targetPos - transform.position;
                        diff.y = 0;
                        float distance = diff.magnitude;
                        if (distance > 3f)
                        {
                            ModBehaviour.DevLog("[CourierNPC] 选择刷新点 [" + randomIndex + "/" + spawnPoints.Length + "] 作为目标: " + targetPos);
                            return targetPos;
                        }
                    }

                    // 如果多次尝试都找不到合适的点，就用第一个（也要修正高度）
                    Vector3 defaultPos = CorrectTargetHeight(spawnPoints[0]);
                    ModBehaviour.DevLog("[CourierNPC] 使用默认刷新点: " + defaultPos);
                    return defaultPos;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 获取刷新点失败: " + e.Message);
            }

            // 如果获取失败，返回零向量（不移动）
            ModBehaviour.DevLog("[CourierNPC] [WARNING] 无法获取刷新点，跳过移动");
            return Vector3.zero;
        }

        /// <summary>
        /// 获取远离玩家的 Boss 刷新点（Boss 战时使用）
        /// 找到第一个距离玩家大于5米的点即返回，避免遍历全部点浪费资源
        /// </summary>
        private Vector3 GetSpawnPointAwayFromPlayer()
        {
            try
            {
                if (ModBehaviour.Instance == null || playerTransform == null)
                {
                    return GetRandomSpawnPoint();  // 回退到随机选择
                }

                Vector3[] spawnPoints = ModBehaviour.Instance.GetCurrentSceneSpawnPoints();
                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    return Vector3.zero;
                }

                // 找到第一个距离玩家大于5米的刷新点
                for (int i = 0; i < spawnPoints.Length; i++)
                {
                    Vector3 point = CorrectTargetHeight(spawnPoints[i]);

                    // 计算该点到玩家的水平距离
                    Vector3 diff = point - playerTransform.position;
                    diff.y = 0;
                    float distToPlayerSqr = diff.sqrMagnitude;

                    // 找到第一个距离玩家大于12米的点就返回
                    if (distToPlayerSqr > 12f * 12f)
                    {
                        float distToPlayer = Mathf.Sqrt(distToPlayerSqr);
                        ModBehaviour.DevLog("[CourierNPC] Boss战逃跑：选择刷新点 [" + i + "]，距离玩家: " + distToPlayer.ToString("F1") + "米");
                        return point;
                    }
                }

                // 如果没找到合适的点，回退到随机选择
                ModBehaviour.DevLog("[CourierNPC] Boss战逃跑：未找到距离玩家>12米的点，使用随机刷新点");
                return GetRandomSpawnPoint();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 获取远离玩家的刷新点失败: " + e.Message);
                return GetRandomSpawnPoint();
            }
        }
    }

    /// <summary>
    /// 快递员交互组件 - 寄存服务选项
    /// 直接打开原版 PlayerStorage（玩家仓库）
    /// </summary>
}
