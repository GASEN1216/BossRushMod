// ============================================================================
// NurseNPCController.cs - 护士NPC控制器（核心控制）
// ============================================================================
// 模块说明：
//   护士NPC"羽织"的核心控制器，包括：
//   - 动画状态管理（Idle）
//   - 与玩家的距离检测和面向玩家
//   - 名字标签显示
//   - 闲置自言自语气泡
//   - 对话气泡显示
//   
//   交互菜单由 NurseInteractable 处理（使用游戏原生 interactableGroup）
//   遵循 KISS/YAGNI/SOLID 原则，参考 GoblinNPCController 实现
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using ItemStatsSystem;
using Duckov.UI.DialogueBubbles;
using BossRush.Constants;
using BossRush.Utils;
using Cysharp.Threading.Tasks;
using Dialogues;
using Duckov.UI;

namespace BossRush
{
    /// <summary>
    /// 护士NPC控制器
    /// 管理动画、面向玩家、名字标签、对话气泡
    /// 交互菜单由 NurseInteractable（InteractableBase）处理
    /// </summary>
    public class NurseNPCController : MonoBehaviour, INPCController
    {
        // ============================================================================
        // 组件引用
        // ============================================================================
        
        private Animator animator;
        private Transform playerTransform;
        private NurseMovement movement;
        private bool hasAnimator = false;
        private float nextPlayerLookupTime = 0f;
        
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
        // 名字标签
        // ============================================================================
        
        private GameObject nameTagObject;
        private TMPro.TextMeshPro nameTagText;

        // ============================================================================
        // 状态标志
        // ============================================================================

        private bool isInDialogue = false;

        // ============================================================================
        // 协程引用
        // ============================================================================

        /// <summary>当前停留协程</summary>
        private Coroutine currentStayCoroutine = null;

        // ============================================================================
        // 闲置气泡计时
        // ============================================================================

        private float idleBubbleTimer = 0f;

        // ============================================================================
        // 动画参数哈希值
        // ============================================================================

        private static readonly int hash_MoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int hash_IsTalking = Animator.StringToHash("IsTalking");
        private static readonly int hash_IsIdle = Animator.StringToHash("IsIdle");
        private static readonly int hash_IsRunning = Animator.StringToHash("IsRunning");
        private bool hasIsTalkingParam = false;
        private bool hasIsRunningParam = false;
        private bool hasIsIdleParam = false;
        private bool animatorParamsCached = false;

        // 配置引用（私有，仅供控制器内部使用）
        private NurseAffinityConfig _config;
        private NurseAffinityConfig config
        {
            get
            {
                if (_config == null) _config = NurseAffinityConfig.Instance;
                return _config;
            }
        }
        
        // ============================================================================
        // 生命周期方法
        // ============================================================================
        
        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            hasAnimator = animator != null;
            
            if (hasAnimator)
            {
                ModBehaviour.DevLog("[NurseNPC] Controller.Awake: 找到 Animator 组件");
                if (animator.runtimeAnimatorController != null)
                {
                    ModBehaviour.DevLog("[NurseNPC] Animator Controller: " + animator.runtimeAnimatorController.name);
                    foreach (var param in animator.parameters)
                    {
                        ModBehaviour.DevLog("[NurseNPC]   参数: " + param.name + " (" + param.type + ")");
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[NurseNPC] [WARNING] Animator 没有 RuntimeAnimatorController！");
                }
                CacheAnimatorParameters();
            }
            else
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] Controller.Awake: 未找到 Animator 组件！");
            }
        }
        
        void Start()
        {
            TryRefreshPlayerTransform(true);

            movement = GetComponent<NurseMovement>();
            
            // 初始化动画参数
            InitializeDefaultAnimatorParams();
            
            // 创建名字标签
            CreateNameTag();
            
            // 设置对话Actor组件
            SetupDialogueActor();
            
            // 初始化闲置气泡计时
            idleBubbleTimer = UnityEngine.Random.Range(
                NurseNPCConstants.IDLE_BUBBLE_INITIAL_MIN_DELAY,
                NurseNPCConstants.IDLE_BUBBLE_INITIAL_MAX_DELAY);
        }
        
        void Update()
        {
            // 更新玩家引用（节流，避免每帧查找）
            TryRefreshPlayerTransform();

            // 更新名字标签朝向
            UpdateNameTagRotation();

            // 玩家距离检测（只算一次）
            if (!isInDialogue && playerTransform != null)
            {
                float nearDistanceSqr = NurseNPCConstants.NEAR_DISTANCE * NurseNPCConstants.NEAR_DISTANCE;
                float distanceSqr = (transform.position - playerTransform.position).sqrMagnitude;
                bool isMovingNow = movement != null && (movement.IsMoving || movement.IsWaitingForPath);
                if (distanceSqr <= nearDistanceSqr && !isMovingNow)
                {
                    FacePlayer();
                }
                else if (!isMovingNow)
                {
                    UpdateIdleBubble();
                }
            }

            // 检测故事对话触发条件（5级）
            CheckStoryDialogueTrigger();
        }
        
        // ============================================================================
        // 动画控制
        // ============================================================================
        
        /// <summary>
        /// 初始化默认动画参数
        /// </summary>
        private void InitializeDefaultAnimatorParams()
        {
            if (!hasAnimator) return;
            if (!animatorParamsCached) CacheAnimatorParameters();
            
            try
            {
                animator.SetFloat(hash_MoveSpeed, 0f);

                if (hasIsTalkingParam)
                {
                    animator.SetBool(hash_IsTalking, false);
                }
                if (hasIsRunningParam)
                {
                    animator.SetBool(hash_IsRunning, false);
                }
                if (hasIsIdleParam)
                {
                    animator.SetBool(hash_IsIdle, true);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] 初始化动画参数失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 播放待机动画
        /// </summary>
        public void StartIdleAnimation()
        {
            if (!hasAnimator) return;
            if (!animatorParamsCached) CacheAnimatorParameters();
            
            try
            {
                animator.SetFloat(hash_MoveSpeed, 0f);
                if (hasIsRunningParam)
                {
                    animator.SetBool(hash_IsRunning, false);
                }
                if (hasIsIdleParam)
                {
                    animator.SetBool(hash_IsIdle, true);
                }
            }
            catch (Exception e)
            {
                NPCExceptionHandler.LogAndIgnore(e, "NurseNPCController.StartIdleAnimation");
            }
        }

        private void CacheAnimatorParameters()
        {
            animatorParamsCached = true;
            hasIsTalkingParam = false;
            hasIsRunningParam = false;
            hasIsIdleParam = false;

            if (animator == null)
            {
                return;
            }

            foreach (var param in animator.parameters)
            {
                if (param.nameHash == hash_IsTalking && param.type == AnimatorControllerParameterType.Bool)
                {
                    hasIsTalkingParam = true;
                }
                else if (param.nameHash == hash_IsRunning && param.type == AnimatorControllerParameterType.Bool)
                {
                    hasIsRunningParam = true;
                }
                else if (param.nameHash == hash_IsIdle && param.type == AnimatorControllerParameterType.Bool)
                {
                    hasIsIdleParam = true;
                }
            }
        }

        private void TryRefreshPlayerTransform(bool force = false)
        {
            if (playerTransform != null) return;

            float now = Time.realtimeSinceStartup;
            if (!force && now < nextPlayerLookupTime) return;

            nextPlayerLookupTime = now + NurseNPCConstants.PLAYER_LOOKUP_INTERVAL;

            if (CharacterMainControl.Main != null)
            {
                playerTransform = CharacterMainControl.Main.transform;
                ModBehaviour.DevLog("[NurseNPC] Controller: 获取到玩家引用");
                return;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
                ModBehaviour.DevLog("[NurseNPC] Controller: 通过 Tag 获取到玩家引用");
            }
        }
        
        // ============================================================================
        // 面向玩家
        // ============================================================================
        
        /// <summary>
        /// 让护士面向玩家
        /// </summary>
        public void FacePlayer()
        {
            if (playerTransform == null) return;

            Vector3 direction = playerTransform.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.01f)
            {
                // 与哥布林一致：仅在停留/对话时朝向玩家，避免和移动旋转抢占
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }
        
        // ============================================================================
        // 名字标签
        // ============================================================================
        
        /// <summary>
        /// 创建名字标签
        /// </summary>
        private void CreateNameTag()
        {
            if (NPCNameTagHelper.CreateNameTag(
                transform,
                "NurseNameTag",
                config.DisplayName,
                NurseNPCConstants.NAME_TAG_HEIGHT,
                out nameTagObject,
                out nameTagText,
                "[NurseNPC]"))
            {
                ModBehaviour.DevLog("[NurseNPC] 名字标签创建成功: " + config.DisplayName);
            }
        }
        
        /// <summary>
        /// 更新名字标签朝向摄像机
        /// </summary>
        private void UpdateNameTagRotation()
        {
            NPCNameTagHelper.UpdateNameTagRotation(nameTagObject);
        }
        
        // ============================================================================
        // 对话Actor设置
        // ============================================================================
        
        /// <summary>
        /// 设置 DuckovDialogueActor 组件（用于大对话显示）
        /// 使用 DialogueActorFactory 统一创建，符合官方实现方式
        /// </summary>
        private void SetupDialogueActor()
        {
            try
            {
                dialogueActor = DialogueActorFactory.CreateBilingual(
                    gameObject,
                    "nurse_npc",             // Actor ID
                    "羽织",                   // 中文名称
                    "Yu Zhi",                // 英文名称
                    new Vector3(0, 2f, 0)    // 对话指示器偏移量
                );
                
                if (dialogueActor != null)
                {
                    ModBehaviour.DevLog("[NurseNPC] DuckovDialogueActor 设置成功");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] 设置 DialogueActor 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 检测故事对话触发条件
        /// 条件：玩家在3米内、没有UI打开、达到5级、未触发过（持久化检查）
        /// </summary>
        private void CheckStoryDialogueTrigger()
        {
            if (storyDialogueCheckCooldown > 0f)
            {
                storyDialogueCheckCooldown -= Time.deltaTime;
                return;
            }
            storyDialogueCheckCooldown = NurseNPCConstants.STORY_DIALOGUE_CHECK_INTERVAL;

            if (playerTransform == null) return;
            if (isInStoryDialogue) return;
            if (isInDialogue) return;
            if (movement != null && (movement.IsMoving || movement.IsWaitingForPath)) return;

            float storyTriggerDistanceSqr = NurseNPCConstants.STORY_DIALOGUE_TRIGGER_DISTANCE * NurseNPCConstants.STORY_DIALOGUE_TRIGGER_DISTANCE;
            if ((transform.position - playerTransform.position).sqrMagnitude > storyTriggerDistanceSqr) return;

            if (IsAnyUIOpen()) return;

            int level = AffinityManager.GetLevel(NurseAffinityConfig.NPC_ID);
            if (level >= 5 && !AffinityManager.HasTriggeredStory5(NurseAffinityConfig.NPC_ID))
            {
                ModBehaviour.DevLog("[NurseNPC] 触发5级故事对话，等级: " + level);
                TriggerStoryDialogue(5);
            }
        }

        /// <summary>
        /// 检查是否有任何UI打开
        /// </summary>
        private bool IsAnyUIOpen()
        {
            try
            {
                if (View.ActiveView != null)
                {
                    return true;
                }

                if (PauseMenu.Instance != null && PauseMenu.Instance.Shown)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                // 保守策略：检测失败时不触发
                return true;
            }
        }

        /// <summary>
        /// 触发故事对话（当前仅支持5级）
        /// </summary>
        public void TriggerStoryDialogue(int storyLevel)
        {
            if (isInStoryDialogue)
            {
                ModBehaviour.DevLog("[NurseNPC] 已在故事对话中，跳过");
                return;
            }

            TriggerStoryDialogueAsync(storyLevel).Forget();
        }

        /// <summary>
        /// 异步触发故事对话
        /// </summary>
        private async UniTaskVoid TriggerStoryDialogueAsync(int storyLevel)
        {
            bool storyPlayed = false;
            try
            {
                isInStoryDialogue = true;
                isInDialogue = true;

                if (currentStayCoroutine != null)
                {
                    StopCoroutine(currentStayCoroutine);
                    currentStayCoroutine = null;
                    ModBehaviour.DevLog("[NurseNPC] TriggerStoryDialogue: stop previous stay coroutine");
                }

                if (movement != null)
                {
                    movement.StopMove();
                }

                FacePlayer();
                StartIdleAnimation();

                string[] dialogueKeys = GetStoryDialogueKeys(storyLevel);
                if (dialogueKeys == null || dialogueKeys.Length <= 0)
                {
                    ModBehaviour.DevLog("[NurseNPC] [WARNING] Story dialogue keys missing, storyLevel=" + storyLevel);
                    storyDialogueCheckCooldown = NurseNPCConstants.STORY_DIALOGUE_RETRY_INTERVAL;
                    return;
                }

                ModBehaviour.DevLog("[NurseNPC] Start story dialogue, level=" + storyLevel + ", lines=" + dialogueKeys.Length);

                if (dialogueActor != null)
                {
                    await DialogueManager.ShowDialogueSequence(dialogueActor, dialogueKeys);
                    storyPlayed = true;

                    bool marked = NPCExceptionHandler.TryExecute(
                        () => AffinityManager.MarkStoryTriggered(NurseAffinityConfig.NPC_ID, storyLevel),
                        GetStoryMarkLogKey(storyLevel));
                    if (!marked)
                    {
                        storyDialogueCheckCooldown = NurseNPCConstants.STORY_DIALOGUE_RETRY_INTERVAL;
                    }
                    else
                    {
                        TryGrantStoryReward(storyLevel);
                    }

                    ModBehaviour.DevLog("[NurseNPC] Story dialogue finished, level=" + storyLevel);
                }
                else
                {
                    ModBehaviour.DevLog("[NurseNPC] [WARNING] dialogueActor is null, cannot show story dialogue");
                    storyDialogueCheckCooldown = NurseNPCConstants.STORY_DIALOGUE_RETRY_INTERVAL;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] Story dialogue failed: " + e.Message);
                storyDialogueCheckCooldown = NurseNPCConstants.STORY_DIALOGUE_RETRY_INTERVAL;
                DialogueManager.ForceEndDialogue();
            }
            finally
            {
                isInStoryDialogue = false;
                EndDialogueWithStay(storyPlayed
                    ? NurseNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION
                    : NurseNPCConstants.SHORT_DIALOGUE_STAY_DURATION);
            }
        }

        private static int GetNextPendingStoryLevel(int level)
        {
            if (level >= 3 && !AffinityManager.HasTriggeredStory(NurseAffinityConfig.NPC_ID, 3))
            {
                return 3;
            }

            if (level >= 5 && !AffinityManager.HasTriggeredStory(NurseAffinityConfig.NPC_ID, 5))
            {
                return 5;
            }

            if (level >= 8 && !AffinityManager.HasTriggeredStory(NurseAffinityConfig.NPC_ID, 8))
            {
                return 8;
            }

            if (level >= 10 && !AffinityManager.HasTriggeredStory(NurseAffinityConfig.NPC_ID, 10))
            {
                return 10;
            }

            return 0;
        }

        private static string[] GetStoryDialogueKeys(int storyLevel)
        {
            switch (storyLevel)
            {
                case 3:
                    return LocalizationInjector.GetNurseStory3DialogueKeys();
                case 5:
                    return LocalizationInjector.GetNurseStory5DialogueKeys();
                case 8:
                    return LocalizationInjector.GetNurseStory8DialogueKeys();
                case 10:
                    return LocalizationInjector.GetNurseStory10DialogueKeys();
                default:
                    return null;
            }
        }

        private static string GetStoryMarkLogKey(int storyLevel)
        {
            return "NurseNPCController.TriggerStoryDialogue.MarkStoryTriggered_" + storyLevel;
        }

        private void TryGrantStoryReward(int storyLevel)
        {
            string rewardKey = null;
            int rewardTypeId = 0;
            int rewardCount = 1;
            string rewardDialogue = null;

            switch (storyLevel)
            {
                case 3:
                    rewardKey = NurseAffinityConfig.LEVEL3_REWARD_KEY;
                    rewardTypeId = CalmingDropsConfig.TYPE_ID;
                    rewardCount = CalmingDropsConfig.REWARD_COUNT;
                    rewardDialogue = L10n.T("这些安神滴剂你拿着，撑不住的时候记得用。", "Take these calming drops. Use them when things become too much.");
                    break;
                case 8:
                    rewardKey = NurseAffinityConfig.LEVEL8_REWARD_KEY;
                    rewardTypeId = PeaceCharmConfig.TYPE_ID;
                    rewardDialogue = L10n.T("这个平安护身符给你。别嫌我多事，我只是想让你平安回来。", "This peace charm is for you. Call me overprotective if you want, I just want you to come back safe.");
                    break;
                default:
                    return;
            }

            if (AffinityManager.HasClaimedReward(NurseAffinityConfig.NPC_ID, rewardKey))
            {
                return;
            }

            if (!GiveRewardItem(rewardTypeId, rewardCount))
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] Failed to grant story reward: storyLevel=" + storyLevel + ", typeId=" + rewardTypeId);
                return;
            }

            AffinityManager.MarkRewardClaimed(NurseAffinityConfig.NPC_ID, rewardKey);
            ShowLoveHeartBubble();
            if (!string.IsNullOrEmpty(rewardDialogue))
            {
                NPCDialogueSystem.ShowDialogue(NurseAffinityConfig.NPC_ID, transform, rewardDialogue, 4f);
            }
        }

        private bool GiveRewardItem(int typeId, int stackCount)
        {
            try
            {
                Item rewardItem = ItemAssetsCollection.InstantiateSync(typeId);
                if (rewardItem == null)
                {
                    return false;
                }

                if (stackCount > 1)
                {
                    int maxStackCount = rewardItem.MaxStackCount > 0 ? rewardItem.MaxStackCount : stackCount;
                    rewardItem.StackCount = Mathf.Clamp(stackCount, 1, maxStackCount);
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.CharacterItem != null && player.CharacterItem.Inventory != null)
                {
                    bool added = player.CharacterItem.Inventory.AddAndMerge(rewardItem, 0);
                    if (added)
                    {
                        return true;
                    }

                    rewardItem.Drop(player, true);
                    return true;
                }

                rewardItem.transform.position = transform.position;
                rewardItem.gameObject.SetActive(true);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] Failed to grant story reward: " + e.Message);
                return false;
            }
        }

        public void StartDialogue()
        {
            // 停止之前的停留协程
            if (currentStayCoroutine != null)
            {
                StopCoroutine(currentStayCoroutine);
                currentStayCoroutine = null;
                ModBehaviour.DevLog("[NurseNPC] StartDialogue: 停止之前的停留协程");
            }
            
            isInDialogue = true;

            // 停止移动
            if (movement != null)
            {
                movement.StopMove();
            }
            
            // 面向玩家
            FacePlayer();
            
            // 播放待机动画
            StartIdleAnimation();
            
            ModBehaviour.DevLog("[NurseNPC] 开始对话，进入待机状态");
        }
        
        /// <summary>
        /// 结束对话（UI关闭）
        /// 护士停留一段时间后恢复
        /// </summary>
        public void EndDialogue()
        {
            EndDialogueWithStay(NurseNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION);
        }
        
        /// <summary>
        /// 结束对话并在原地停留指定时间
        /// </summary>
        public void EndDialogueWithStay(float stayDuration, bool showFarewell = false)
        {
            // 停止之前的停留协程
            if (currentStayCoroutine != null)
            {
                StopCoroutine(currentStayCoroutine);
                currentStayCoroutine = null;
            }
            
            currentStayCoroutine = StartCoroutine(StayAfterDialogue(stayDuration));
        }
        
        /// <summary>
        /// 对话后停留协程
        /// </summary>
        private IEnumerator StayAfterDialogue(float duration)
        {
            ModBehaviour.DevLog("[NurseNPC] 对话后停留 " + duration + " 秒");
            yield return new WaitForSeconds(duration);
            
            isInDialogue = false;
            currentStayCoroutine = null;
            ModBehaviour.DevLog("[NurseNPC] 停留结束，恢复正常状态");
        }
        
        /// <summary>
        /// 延迟结束对话
        /// </summary>
        public void EndDialogueDelayed(float delay)
        {
            StartCoroutine(EndDialogueDelayedCoroutine(delay));
        }
        
        private IEnumerator EndDialogueDelayedCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            EndDialogue();
        }
        
        // ============================================================================
        // 对话气泡显示
        // ============================================================================
        
        /// <summary>
        /// 显示对话气泡
        /// </summary>
        public void ShowDialogueBubble(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            try
            {
                float yOffset = config.DialogueBubbleHeight;
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    DialogueBubblesManager.Show(text, transform, yOffset, false, false, -1f, config.DefaultDialogueDuration)
                );
                ModBehaviour.DevLog("[NurseNPC] 显示气泡: " + text);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] 显示气泡失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 显示冒爱心特效气泡（好感度等级>=8时使用）
        /// 使用通用 NPCHeartBubbleHelper，优先序列帧动画，回退文字气泡
        /// </summary>
        public void ShowLoveHeartBubble()
        {
            NPCHeartBubbleHelper.ShowLoveHeart(
                transform,
                NurseNPCConstants.NAME_TAG_HEIGHT + NurseNPCConstants.BUBBLE_OFFSET_Y,
                NurseNPCConstants.NAME_TAG_HEIGHT + NurseNPCConstants.BUBBLE_ANIMATION_OFFSET_Y,
                NurseNPCConstants.BUBBLE_DURATION,
                "[NurseNPC]");
        }

        /// <summary>
        /// 显示心碎特效气泡（收到不喜欢的礼物时使用）
        /// 使用通用 NPCHeartBubbleHelper，优先序列帧动画，回退文字气泡
        /// </summary>
        public void ShowBrokenHeartBubble()
        {
            NPCHeartBubbleHelper.ShowBrokenHeart(
                transform,
                NurseNPCConstants.NAME_TAG_HEIGHT + NurseNPCConstants.BUBBLE_OFFSET_Y,
                NurseNPCConstants.NAME_TAG_HEIGHT + NurseNPCConstants.BUBBLE_ANIMATION_OFFSET_Y,
                NurseNPCConstants.BUBBLE_DURATION,
                "[NurseNPC]");
        }
        
        // ============================================================================
        // 闲置自言自语
        // ============================================================================
        
        /// <summary>
        /// 更新闲置气泡显示
        /// </summary>
        private void UpdateIdleBubble()
        {
            idleBubbleTimer -= Time.deltaTime;
            if (idleBubbleTimer <= 0f)
            {
                var idleBubbles = config.IdleBubbles;
                if (idleBubbles != null && idleBubbles.Length > 0)
                {
                    int index = UnityEngine.Random.Range(0, idleBubbles.Length);
                    ShowDialogueBubble(idleBubbles[index]);
                }
                
                // 重置计时器
                idleBubbleTimer = UnityEngine.Random.Range(
                    NurseNPCConstants.IDLE_BUBBLE_MIN_INTERVAL,
                    NurseNPCConstants.IDLE_BUBBLE_MAX_INTERVAL);
            }
        }
        
        // ============================================================================
        // 公共接口
        // ============================================================================
        
        /// <summary>是否正在对话中</summary>
        public bool IsInDialogue => isInDialogue;

        /// <summary>是否正在播放故事对话</summary>
        public bool IsInStoryDialogue => isInStoryDialogue;

        // ============================================================================
        // INPCController 接口实现
        // ============================================================================

        /// <summary>NPC标识符</summary>
        public string NpcId => NurseAffinityConfig.NPC_ID;

        /// <summary>NPC的Transform</summary>
        public Transform NpcTransform => transform;
        
        /// <summary>
        /// 获取与玩家的距离
        /// </summary>
        public float GetDistanceToPlayer()
        {
            if (playerTransform == null) return float.MaxValue;
            return Vector3.Distance(transform.position, playerTransform.position);
        }
        
        void OnDestroy()
        {
            if (nameTagObject != null)
            {
                Destroy(nameTagObject);
            }
        }
    }
}
