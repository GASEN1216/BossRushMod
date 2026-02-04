// ============================================================================
// GoblinNPCController.cs - 哥布林NPC控制器（核心控制）
// ============================================================================
// 模块说明：
//   哥布林NPC控制器的核心部分，使用 partial class 机制
//   包含状态字段、Awake/Start/Update、公共接口
//   遵循 KISS/YAGNI/SOLID 原则
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using BossRush.Constants;
using BossRush.Utils;
using Cysharp.Threading.Tasks;
using Dialogues;
using Duckov.UI;

namespace BossRush
{
    /// <summary>
    /// 哥布林NPC动画控制器
    /// 管理 Walking、Running、Stop 三种动画状态
    /// </summary>
    public partial class GoblinNPCController : MonoBehaviour
    {
        // ============================================================================
        // 组件引用
        // ============================================================================
        
        private Animator animator;
        private Transform playerTransform;
        private GoblinMovement movement;
        private bool hasAnimator = false;
        
        // ============================================================================
        // 大对话系统组件
        // ============================================================================
        
        /// <summary>DuckovDialogueActor 组件引用（用于大对话显示）</summary>
        private DuckovDialogueActor dialogueActor = null;
        
        /// <summary>是否正在播放故事对话</summary>
        private bool isInStoryDialogue = false;
        
        /// <summary>故事对话检测冷却时间（避免频繁检测）</summary>
        private float storyDialogueCheckCooldown = 0f;
        
        // ============================================================================
        // 状态标志
        // ============================================================================
        
        private bool isRunningToPlayer = false;
        private bool isInDialogue = false;      // 是否在对话中
        private bool isIdling = false;          // 是否在待机
        private bool isBraking = false;         // 是否正在播放急停动画（但还在移动）
        private bool showDialogueOnArrival = false;  // 召唤时，到达后是否显示对话
        private bool isPositiveSummon = false;       // 是否是正面召唤（钻石=true，砖石=false）
        
        // ============================================================================
        // 协程引用
        // ============================================================================
        
        /// <summary>
        /// 当前停留协程（用于防止多个协程同时运行）
        /// </summary>
        private Coroutine currentStayCoroutine = null;
        
        // ============================================================================
        // 动画参数哈希值（与文档定义一致）
        // ============================================================================
        
        private static readonly int hash_IsRunning = Animator.StringToHash("IsRunning");
        private static readonly int hash_DoStop = Animator.StringToHash("DoStop");
        private static readonly int hash_MoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int hash_IsIdle = Animator.StringToHash("IsIdle");
        
        // ============================================================================
        // 生命周期方法
        // ============================================================================
        
        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            hasAnimator = animator != null;
            
            if (hasAnimator)
            {
                ModBehaviour.DevLog("[GoblinNPC] Controller.Awake: 找到 Animator 组件");
                if (animator.runtimeAnimatorController != null)
                {
                    ModBehaviour.DevLog("[GoblinNPC] Animator Controller: " + animator.runtimeAnimatorController.name);
                    // 列出所有参数
                    foreach (var param in animator.parameters)
                    {
                        ModBehaviour.DevLog("[GoblinNPC]   参数: " + param.name + " (" + param.type + ")");
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[GoblinNPC] [WARNING] 警告：Animator 没有 RuntimeAnimatorController！");
                }
            }
            else
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] Controller.Awake: 未找到 Animator 组件！");
            }
        }
        
        void Start()
        {
            // 获取玩家引用
            NPCExceptionHandler.TryExecute(() =>
            {
                if (CharacterMainControl.Main != null)
                {
                    playerTransform = CharacterMainControl.Main.transform;
                    ModBehaviour.DevLog("[GoblinNPC] Controller.Start: 获取到玩家引用");
                }
            }, "GoblinNPCController.Start - 获取 CharacterMainControl.Main");
            
            if (playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                    ModBehaviour.DevLog("[GoblinNPC] Controller.Start: 通过 Tag 获取到玩家引用");
                }
            }
            
            // 获取移动组件引用
            movement = GetComponent<GoblinMovement>();
            
            // 初始化动画参数（初始为走路状态）
            SafeSetBool(hash_IsRunning, false);
            SafeSetBool(hash_IsIdle, false);
            
            // 创建名字标签
            CreateNameTag();
            
            // 设置对话Actor组件（用于大对话显示）
            SetupDialogueActor();
            
            ModBehaviour.DevLog("[GoblinNPC] Controller.Start 完成");
        }
        
        void LateUpdate()
        {
            // 让名字标签始终面向相机
            if (nameTagObject != null && Camera.main != null)
            {
                nameTagObject.transform.rotation = Camera.main.transform.rotation;
            }
        }
        
        void Update()
        {
            // 更新玩家引用
            if (playerTransform == null)
            {
                NPCExceptionHandler.TryExecute(() =>
                {
                    if (CharacterMainControl.Main != null)
                    {
                        playerTransform = CharacterMainControl.Main.transform;
                    }
                }, "GoblinNPCController.Update - 更新玩家引用");
            }
            
            // 如果正在跑向玩家，检查距离
            if (isRunningToPlayer && playerTransform != null)
            {
                float distance = Vector3.Distance(transform.position, playerTransform.position);
                
                // 距离4米时播放急停动画（但继续移动）
                if (!isBraking && distance <= GoblinNPCConstants.BRAKE_ANIMATION_DISTANCE)
                {
                    StartBrakeAnimation();
                }
                
                // 距离1米时真正停下来
                if (distance <= GoblinNPCConstants.STOP_DISTANCE)
                {
                    StopAndIdle();
                }
                // 如果移动已停止但距离玩家还较远，说明玩家移动了，需要重新寻路
                else if (movement != null && !movement.IsMoving && !movement.IsWaitingForPath && !isIdling)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 玩家移动了，重新寻路到玩家位置");
                    isBraking = false;  // 重置急停状态
                    movement.RunToPlayer(playerTransform.position);
                }
            }
            
            // 检测故事对话触发条件
            CheckStoryDialogueTrigger();
        }
        
        /// <summary>
        /// 检测故事对话触发条件
        /// 条件：玩家在3米内、没有UI打开、达到对应好感度等级、未触发过（持久化检查）
        /// </summary>
        private void CheckStoryDialogueTrigger()
        {
            // 冷却时间检测（每0.5秒检测一次，避免性能问题）
            if (storyDialogueCheckCooldown > 0f)
            {
                storyDialogueCheckCooldown -= Time.deltaTime;
                return;
            }
            storyDialogueCheckCooldown = 0.5f;
            
            // 基本条件检查
            if (playerTransform == null) return;
            if (isInStoryDialogue) return;
            if (isInDialogue) return;
            if (isRunningToPlayer) return;
            
            // 检查玩家距离（3米内）
            float distance = Vector3.Distance(transform.position, playerTransform.position);
            if (distance > GoblinNPCConstants.STORY_DIALOGUE_TRIGGER_DISTANCE) return;
            
            // 检查是否有UI打开（使用游戏的 View 系统）
            if (IsAnyUIOpen()) return;
            
            // 获取当前好感度等级
            int level = AffinityManager.GetLevel(GoblinAffinityConfig.NPC_ID);
            
            // 检查10级故事（优先级更高）- 使用持久化状态
            if (level >= 10 && !AffinityManager.HasTriggeredStory10(GoblinAffinityConfig.NPC_ID))
            {
                ModBehaviour.DevLog("[GoblinNPC] 触发10级故事对话，等级: " + level);
                TriggerStoryDialogue(10);
                return;
            }
            
            // 检查5级故事 - 使用持久化状态
            if (level >= 5 && !AffinityManager.HasTriggeredStory5(GoblinAffinityConfig.NPC_ID))
            {
                ModBehaviour.DevLog("[GoblinNPC] 触发5级故事对话，等级: " + level);
                TriggerStoryDialogue(5);
                return;
            }
        }
        
        /// <summary>
        /// 检查是否有任何UI打开
        /// </summary>
        private bool IsAnyUIOpen()
        {
            try
            {
                // 检查游戏的 View 系统是否有活动的 View
                if (View.ActiveView != null)
                {
                    return true;
                }
                
                // 检查暂停菜单
                if (PauseMenu.Instance != null && PauseMenu.Instance.Shown)
                {
                    return true;
                }
                
                return false;
            }
            catch
            {
                // 如果检测失败，保守起见返回 true（不触发对话）
                return true;
            }
        }
        
        // ============================================================================
        // 公共接口
        // ============================================================================
        
        /// <summary>
        /// 玩家使用物品召唤哥布林 - 哥布林跑向玩家
        /// </summary>
        public void RunToPlayer()
        {
            if (playerTransform == null)
            {
                ModBehaviour.DevLog("[GoblinNPC] RunToPlayer: 玩家引用为空");
                return;
            }
            
            isRunningToPlayer = true;
            showDialogueOnArrival = false;  // 普通召唤不显示对话
            
            // 设置跑步动画
            SafeSetBool(hash_IsRunning, true);
            
            // 通知移动组件跑向玩家
            if (movement != null)
            {
                movement.RunToPlayer(playerTransform.position);
            }
            
            ModBehaviour.DevLog("[GoblinNPC] 开始跑向玩家");
        }
        
        /// <summary>
        /// 砖石召唤哥布林 - 哥布林跑向玩家，到达后先停下来再显示对话（负面反应）
        /// </summary>
        public void RunToPlayerWithDialogue()
        {
            RunToPlayerWithDialogue(false);  // 砖石召唤，负面反应
        }
        
        /// <summary>
        /// 召唤哥布林 - 哥布林跑向玩家，到达后先停下来再显示对话
        /// </summary>
        /// <param name="positive">是否是正面召唤（钻石=true显示爱心，砖石=false显示碎心）</param>
        public void RunToPlayerWithDialogue(bool positive)
        {
            if (playerTransform == null)
            {
                ModBehaviour.DevLog("[GoblinNPC] RunToPlayerWithDialogue: 玩家引用为空");
                return;
            }
            
            isRunningToPlayer = true;
            showDialogueOnArrival = true;  // 需要显示对话
            isPositiveSummon = positive;   // 记录召唤类型
            
            // 设置跑步动画
            SafeSetBool(hash_IsRunning, true);
            
            // 通知移动组件跑向玩家
            if (movement != null)
            {
                movement.RunToPlayer(playerTransform.position);
            }
            
            ModBehaviour.DevLog("[GoblinNPC] 开始跑向玩家（" + (positive ? "钻石" : "砖石") + "召唤，到达后显示对话）");
        }
        
        /// <summary>
        /// 更新移动速度动画（由 GoblinMovement 调用）
        /// </summary>
        public void UpdateMoveSpeed(float speed)
        {
            SafeSetFloat(hash_MoveSpeed, speed);
        }
        
        /// <summary>
        /// 获取是否正在跑向玩家
        /// </summary>
        public bool IsRunningToPlayer
        {
            get { return isRunningToPlayer; }
        }
        
        /// <summary>
        /// 获取是否在对话中
        /// </summary>
        public bool IsInDialogue
        {
            get { return isInDialogue; }
        }
        
        /// <summary>
        /// 获取是否在待机
        /// </summary>
        public bool IsIdling
        {
            get { return isIdling; }
        }
        
        // ============================================================================
        // 私有辅助方法
        // ============================================================================
        
        /// <summary>
        /// 到达玩家1米范围内，真正停下来
        /// </summary>
        private void StopAndIdle()
        {
            isRunningToPlayer = false;
            isBraking = false;
            
            // 停止移动
            if (movement != null)
            {
                movement.StopMove();
            }
            
            // 面向玩家
            FacePlayer();
            
            // 确保动画状态正确
            SafeSetBool(hash_IsRunning, false);
            SafeSetBool(hash_IsIdle, true);
            
            ModBehaviour.DevLog("[GoblinNPC] 到达玩家1米范围，停止移动");
            
            // 进入待机并显示气泡，3秒后恢复
            StartCoroutine(IdleAndShowBubble());
        }
        
        // ============================================================================
        // 大对话系统方法
        // ============================================================================
        
        /// <summary>
        /// 设置 DuckovDialogueActor 组件（用于大对话显示）
        /// 使用 DialogueActorFactory 统一创建，符合官方实现方式
        /// </summary>
        private void SetupDialogueActor()
        {
            try
            {
                // 使用工厂类创建 Actor（自动处理反射和本地化）
                dialogueActor = DialogueActorFactory.CreateBilingual(
                    gameObject,
                    "goblin_dingdang",       // Actor ID
                    "叮当",                   // 中文名称
                    "Dingdang",              // 英文名称
                    new Vector3(0, 2f, 0)    // 对话指示器偏移量
                );
                
                if (dialogueActor != null)
                {
                    ModBehaviour.DevLog("[GoblinNPC] DuckovDialogueActor 组件已通过工厂创建");
                }
                else
                {
                    ModBehaviour.DevLog("[GoblinNPC] [WARNING] DialogueActorFactory 创建失败");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] 设置 DuckovDialogueActor 失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 触发故事对话（5级或10级）
        /// 使用大对话系统显示多段对话
        /// </summary>
        /// <param name="storyLevel">故事等级（5或10）</param>
        public void TriggerStoryDialogue(int storyLevel)
        {
            if (isInStoryDialogue)
            {
                ModBehaviour.DevLog("[GoblinNPC] 已在故事对话中，跳过");
                return;
            }
            
            TriggerStoryDialogueAsync(storyLevel).Forget();
        }
        
        /// <summary>
        /// 异步触发故事对话
        /// </summary>
        private async UniTaskVoid TriggerStoryDialogueAsync(int storyLevel)
        {
            // 记录是否是10级故事（用于决定结束后的停留时间）
            bool isLevel10Story = (storyLevel == 10);
            
            try
            {
                isInStoryDialogue = true;
                
                // 【修复】设置对话状态，阻止 GoblinMovement 的漫步决策
                isInDialogue = true;
                isIdling = true;
                
                // 停止之前的停留协程（防止之前的协程结束时恢复走路）
                if (currentStayCoroutine != null)
                {
                    StopCoroutine(currentStayCoroutine);
                    currentStayCoroutine = null;
                    ModBehaviour.DevLog("[GoblinNPC] TriggerStoryDialogue: 停止之前的停留协程");
                }
                
                // 停止移动，进入对话状态
                if (movement != null)
                {
                    movement.StopMove();
                }
                
                // 面向玩家
                FacePlayer();
                
                // 播放待机动画
                StartIdleAnimation();
                
                // 获取对话键数组，并立即标记故事已触发（防止好感度波动时重复触发）
                string[] dialogueKeys;
                if (storyLevel == 5)
                {
                    // 【重要】在对话开始前就标记已触发，防止重复触发
                    AffinityManager.MarkStory5Triggered(GoblinAffinityConfig.NPC_ID);
                    dialogueKeys = LocalizationInjector.GetGoblinStory5DialogueKeys();
                    ModBehaviour.DevLog("[GoblinNPC] 开始5级故事对话，共 " + dialogueKeys.Length + " 条");
                }
                else if (storyLevel == 10)
                {
                    // 【重要】在对话开始前就标记已触发，防止重复触发
                    AffinityManager.MarkStory10Triggered(GoblinAffinityConfig.NPC_ID);
                    dialogueKeys = LocalizationInjector.GetGoblinStory10DialogueKeys();
                    ModBehaviour.DevLog("[GoblinNPC] 开始10级故事对话，共 " + dialogueKeys.Length + " 条");
                }
                else
                {
                    ModBehaviour.DevLog("[GoblinNPC] 无效的故事等级: " + storyLevel);
                    return;
                }
                
                // 使用 DialogueManager 显示对话序列
                if (dialogueActor != null)
                {
                    await DialogueManager.ShowDialogueSequence(dialogueActor, dialogueKeys);
                    ModBehaviour.DevLog("[GoblinNPC] 故事对话序列完成");

                    // 10级故事对话完成后，赠送叮当涂鸦礼物
                    if (storyLevel == 10)
                    {
                        GiveDrawingGift();
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[GoblinNPC] [WARNING] dialogueActor 为空，无法显示故事对话");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 故事对话出错: " + e.Message);
                DialogueManager.ForceEndDialogue();
            }
            finally
            {
                isInStoryDialogue = false;
                
                // 恢复待机状态
                StopIdleAnimation();
                
                // 【修复】10级故事对话后，停留10秒再恢复走路
                if (isLevel10Story)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 10级故事对话结束，停留10秒后再离开");
                    // 使用 EndDialogueWithStay 来处理停留和恢复走路
                    // 不显示告别对话（showFarewell=false），因为已经有礼物对话了
                    EndDialogueWithStay(10f, false);
                }
                else
                {
                    // 非10级故事，直接重置状态并恢复走路
                    isInDialogue = false;
                    isIdling = false;
                    
                    // 恢复移动
                    if (movement != null)
                    {
                        movement.ResumeWalking();
                    }
                }
            }
        }
        
        /// <summary>
        /// 获取是否正在播放故事对话
        /// </summary>
        public bool IsInStoryDialogue
        {
            get { return isInStoryDialogue; }
        }

        /// <summary>
        /// 10级故事对话完成后赠送叮当涂鸦礼物
        /// 显示气泡对话、冒爱心特效，然后在脚下掉落礼物
        /// </summary>
        private void GiveDrawingGift()
        {
            try
            {
                // 1. 显示冒爱心特效
                ShowLoveHeartBubble();

                // 2. 显示气泡对话 "叮当送给你一份礼物！"
                string giftMessage = L10n.T("叮当送给你一份礼物！", "Dingdang gives you a gift!");
                NPCDialogueSystem.ShowDialogue(GoblinAffinityConfig.NPC_ID, transform, giftMessage);

                // 3. 在哥布林脚下掉落叮当涂鸦
                Vector3 dropPosition = transform.position;
                if (DingdangDrawingConfig.SpawnAtPosition(dropPosition))
                {
                    ModBehaviour.DevLog("[GoblinNPC] 10级礼物已掉落：叮当涂鸦");
                }
                else
                {
                    ModBehaviour.DevLog("[GoblinNPC] [WARNING] 叮当涂鸦掉落失败");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 赠送礼物失败: " + e.Message);
            }
        }
    }
}
