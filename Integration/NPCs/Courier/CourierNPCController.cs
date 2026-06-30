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
    public class CourierNPCController : MonoBehaviour
    {
        private Animator animator;
        private Transform playerTransform;
        private Transform cachedTransform;
        private bool hasAnimator = false;
        private readonly HashSet<int> floatParams = new HashSet<int>();
        private readonly HashSet<int> boolParams = new HashSet<int>();
        private readonly HashSet<int> intParams = new HashSet<int>();
        private bool animatorParamsCached = false;

        // Mode E 固定模式标志（站在原地，IsTalking 始终为 true）
        private bool isStationary = false;

        /// <summary>
        /// 设置固定模式（Mode E 使用，IsTalking 始终保持 true）
        /// </summary>
        public void SetStationary(bool stationary)
        {
            isStationary = stationary;
        }

        private const float NAME_TAG_HEIGHT = 2.3f;  // 名字标签高度（头顶上方）

        // 距离阈值（米）
        private const float NEAR_DISTANCE = 5f;

        // ============================================================================
        // 首次见面对话功能
        // ============================================================================

        // 存档持久化 Key（每个存档独立）
        private const string FIRST_MEET_SAVE_KEY = "BossRush_CourierFirstMeet";

        // Wiki Book 物品 TypeID
        private const int WIKI_BOOK_TYPE_ID = BossRushItemIds.AdventureJournal;

        // 对话进行中标志
        private bool isInFirstMeetDialogue = false;

        // DuckovDialogueActor 组件引用（用于大对话显示）
        private DuckovDialogueActor dialogueActor = null;

        // 注：首次见面对话内容已移至 LocalizationInjector.COURIER_FIRST_MEET_DIALOGUES
        // 使用 LocalizationInjector.GetCourierFirstMeetDialogueKeys() 获取本地化键

        /// <summary>
        /// 检查是否已触发首次见面（从存档读取）
        /// </summary>
        private bool HasTriggeredFirstMeet
        {
            get
            {
                try
                {
                    if (Saves.SavesSystem.KeyExisits(FIRST_MEET_SAVE_KEY))
                    {
                        return Saves.SavesSystem.Load<bool>(FIRST_MEET_SAVE_KEY);
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 设置首次见面状态（保存到存档）
        /// </summary>
        private void SetFirstMeetTriggered()
        {
            try
            {
                Saves.SavesSystem.Save<bool>(FIRST_MEET_SAVE_KEY, true);
                ModBehaviour.DevLog("[CourierNPC] 首次见面状态已保存到存档");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 保存首次见面状态失败: " + e.Message);
            }
        }

        // ============================================================================
        // 游戏原生动画参数哈希值（与 CharacterAnimationControl 一致）
        // ============================================================================
        private static readonly int hash_MoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int hash_MoveDirX = Animator.StringToHash("MoveDirX");
        private static readonly int hash_MoveDirY = Animator.StringToHash("MoveDirY");
        private static readonly int hash_RightHandOut = Animator.StringToHash("RightHandOut");
        private static readonly int hash_HandState = Animator.StringToHash("HandState");
        private static readonly int hash_Dashing = Animator.StringToHash("Dashing");
        private static readonly int hash_Attack = Animator.StringToHash("Attack");

        // 快递员专用动画参数哈希值
        private static readonly int hash_IsTalking = Animator.StringToHash("IsTalking");
        private static readonly int hash_IsBossFight = Animator.StringToHash("IsBossFight");
        private static readonly int hash_IsCompleted = Animator.StringToHash("IsCompleted");
        private static readonly int hash_IsNearPlayer = Animator.StringToHash("IsNearPlayer");
        private static readonly int hash_IsArrived = Animator.StringToHash("IsArrived");
        private static readonly int hash_NoBoss = Animator.StringToHash("NoBoss");

        private Transform CachedTransform
        {
            get
            {
                if (cachedTransform == null)
                {
                    cachedTransform = transform;
                }

                return cachedTransform;
            }
        }

        void Awake()
        {
            cachedTransform = transform;
            animator = GetComponentInChildren<Animator>();
            hasAnimator = animator != null;

            if (hasAnimator)
            {
                ModBehaviour.DevLog("[CourierNPC] Controller.Awake: 找到 Animator 组件");
                if (animator.runtimeAnimatorController != null)
                {
                    ModBehaviour.DevLog("[CourierNPC] Animator Controller: " + animator.runtimeAnimatorController.name);
                    // 列出所有参数
                    foreach (var param in animator.parameters)
                    {
                        ModBehaviour.DevLog("[CourierNPC]   参数: " + param.name + " (" + param.type + ")");
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[CourierNPC] [WARNING] 警告：Animator 没有 RuntimeAnimatorController！");
                }

                CacheAnimatorParameters();
            }
            else
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] Controller.Awake: 未找到 Animator 组件！");
            }
        }

        void Start()
        {
            Transform foundPlayerTransform;
            NPCPlayerLookupSource playerLookupSource;
            if (NPCPlayerLookupCache.TryGetPlayerTransform(out foundPlayerTransform, out playerLookupSource))
            {
                playerTransform = foundPlayerTransform;
                if (playerLookupSource == NPCPlayerLookupSource.PlayerTag)
                {
                    ModBehaviour.DevLog("[CourierNPC] Controller.Start: 通过 Tag 获取到玩家引用");
                }
                else
                {
                    ModBehaviour.DevLog("[CourierNPC] Controller.Start: 获取到玩家引用");
                }
            }

            // 初始化游戏原生参数默认值
            InitializeDefaultAnimatorParams();

            // 创建名字标签
            CreateNameTag();

            // 设置对话Actor组件（用于大对话显示）- 使用新的工厂类
            SetupDialogueActor();
        }

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
                    "courier_npc",           // Actor ID
                    "阿稳",                   // 中文名称
                    "Awen",                  // 英文名称
                    new Vector3(0, 2f, 0)    // 对话指示器偏移量
                );

                if (dialogueActor != null)
                {
                    ModBehaviour.DevLog("[CourierNPC] DuckovDialogueActor 组件已通过工厂创建");
                }
                else
                {
                    ModBehaviour.DevLog("[CourierNPC] [WARNING] DialogueActorFactory 创建失败");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 设置 DuckovDialogueActor 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 创建头顶名字标签
        /// </summary>
        private void CreateNameTag()
        {
            string courierName = "阿稳";
            try
            {
                // 直接使用 L10n.T 获取本地化名称（不依赖注入）
                courierName = L10n.T("阿稳", "Awen");
            }
            catch
            {
                // 本地化失败时使用默认中文名
            }

            if (NPCNameTagHelper.RegisterOriginalHealthBarName(
                CachedTransform,
                courierName,
                NAME_TAG_HEIGHT,
                "[CourierNPC]"))
            {
                ModBehaviour.DevLog("[CourierNPC] 原版名字组件注册成功: " + courierName);
            }
        }

        void LateUpdate()
        {
            NPCNameTagHelper.RefreshOriginalHealthBarName(CachedTransform);
        }

        void OnDestroy()
        {
            NPCNameTagHelper.UnregisterOriginalHealthBarName(CachedTransform);
        }

        /// <summary>
        /// 初始化游戏原生动画参数的默认值
        /// </summary>
        private void InitializeDefaultAnimatorParams()
        {
            if (!hasAnimator || animator == null) return;

            try
            {
                if (!animatorParamsCached)
                {
                    CacheAnimatorParameters();
                }

                // 安全地设置参数（检查参数是否存在）
                SafeSetFloat(hash_MoveSpeed, 0f);
                SafeSetFloat(hash_MoveDirX, 0f);
                SafeSetFloat(hash_MoveDirY, 0f);
                SafeSetBool(hash_RightHandOut, false);
                SafeSetInteger(hash_HandState, 0);
                SafeSetBool(hash_Dashing, false);

                // 初始化自定义参数
                SafeSetBool(hash_IsTalking, false);
                SafeSetBool(hash_IsBossFight, false);
                SafeSetBool(hash_IsCompleted, false);
                SafeSetBool(hash_IsNearPlayer, false);
                SafeSetBool(hash_IsArrived, true);  // 初始状态为已到达（静止）
                SafeSetBool(hash_NoBoss, true);  // 初始状态为没有Boss

                ModBehaviour.DevLog("[CourierNPC] 动画参数初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 初始化动画参数出错: " + e.Message);
            }
        }

        /// <summary>
        /// 缓存 Animator 参数类型，避免每次 Set 都走异常路径
        /// </summary>
        private void CacheAnimatorParameters()
        {
            if (!hasAnimator || animator == null || animatorParamsCached) return;

            floatParams.Clear();
            boolParams.Clear();
            intParams.Clear();

            foreach (var param in animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Float)
                {
                    floatParams.Add(param.nameHash);
                }
                else if (param.type == AnimatorControllerParameterType.Bool)
                {
                    boolParams.Add(param.nameHash);
                }
                else if (param.type == AnimatorControllerParameterType.Int)
                {
                    intParams.Add(param.nameHash);
                }
            }

            animatorParamsCached = true;
        }

        // 安全设置参数的辅助方法
        private void SafeSetFloat(int hash, float value)
        {
            if (!hasAnimator || animator == null) return;
            if (!animatorParamsCached) CacheAnimatorParameters();
            if (!floatParams.Contains(hash)) return;

            NPCExceptionHandler.TryExecute(
                () => animator.SetFloat(hash, value),
                "CourierNPCController.SafeSetFloat",
                false);
        }

        private void SafeSetBool(int hash, bool value)
        {
            if (!hasAnimator || animator == null) return;
            if (!animatorParamsCached) CacheAnimatorParameters();
            if (!boolParams.Contains(hash)) return;

            NPCExceptionHandler.TryExecute(
                () => animator.SetBool(hash, value),
                "CourierNPCController.SafeSetBool",
                false);
        }

        private void SafeSetInteger(int hash, int value)
        {
            if (!hasAnimator || animator == null) return;
            if (!animatorParamsCached) CacheAnimatorParameters();
            if (!intParams.Contains(hash)) return;

            NPCExceptionHandler.TryExecute(
                () => animator.SetInteger(hash, value),
                "CourierNPCController.SafeSetInteger",
                false);
        }

        void Update()
        {
            // 实时更新与玩家的距离状态
            UpdateDistanceState();

            // 检查首次见面触发
            CheckFirstMeetTrigger();
        }

        /// <summary>
        /// 检查并触发首次见面对话
        /// </summary>
        private void CheckFirstMeetTrigger()
        {
            // 如果已经在对话中，跳过
            if (isInFirstMeetDialogue) return;

            // 如果已经触发过首次见面，永久跳过（DevMode 和非 DevMode 统一逻辑）
            if (HasTriggeredFirstMeet) return;

            // 如果玩家引用为空，跳过
            if (playerTransform == null) return;

            // 检测玩家距离
            float nearDistanceSqr = NEAR_DISTANCE * NEAR_DISTANCE;
            if ((CachedTransform.position - playerTransform.position).sqrMagnitude <= nearDistanceSqr)
            {
                // 触发首次见面对话
                ModBehaviour.DevLog("[CourierNPC] 玩家进入范围，触发首次见面对话");
                TriggerFirstMeetDialogue().Forget();
            }
        }

        /// <summary>
        /// 触发首次见面对话序列（异步）
        /// 使用 DialogueManager 统一管理，符合官方实现方式
        /// </summary>
        private async Cysharp.Threading.Tasks.UniTaskVoid TriggerFirstMeetDialogue()
        {
            // 获取移动组件引用（在 try 外部，以便 finally 中使用）
            CourierMovement movement = GetComponent<CourierMovement>();

            try
            {
                // 标记对话进行中
                isInFirstMeetDialogue = true;

                // 立即保存状态到存档（防止中途退出后重复触发）
                SetFirstMeetTriggered();

                // 停止移动
                if (movement != null)
                {
                    movement.SetInService(true);
                }

                // 面向玩家
                FacePlayer();

                // 开始对话动画
                StartTalking();

                // 使用 DialogueManager 显示对话序列（使用本地化键）
                // 获取首次见面对话的本地化键数组
                string[] dialogueKeys = LocalizationInjector.GetCourierFirstMeetDialogueKeys();

                ModBehaviour.DevLog("[CourierNPC] 开始首次见面对话序列，共 " + dialogueKeys.Length + " 条对话");

                // 使用 DialogueManager 显示对话序列
                await DialogueManager.ShowDialogueSequence(dialogueActor, dialogueKeys);

                ModBehaviour.DevLog("[CourierNPC] 对话序列完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 首次见面对话出错: " + e.Message + "\n" + e.StackTrace);
                // 确保对话系统状态正确
                DialogueManager.ForceEndDialogue();
            }

            // 无论对话是否成功，都执行后续逻辑（生成物品等）
            try
            {
                // 对话结束，停止对话动画
                StopTalking();

                // 显示"给你"气泡（使用本地化键）
                string giveText = "BossRush_CourierGive".ToPlainText();
                float yOffset = 1.5f;
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(giveText, transform, yOffset, false, false, -1f, 3f)
                );
                ModBehaviour.DevLog("[CourierNPC] 显示气泡: " + giveText);

                // 等待一小段时间让气泡显示
                await Cysharp.Threading.Tasks.UniTask.Delay(500);

                // 生成 Wiki Book 物品
                SpawnWikiBook();

                // 恢复移动
                if (movement != null)
                {
                    movement.SetInService(false);
                }

                ModBehaviour.DevLog("[CourierNPC] 首次见面对话序列完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 后续处理出错: " + e.Message);
            }
            finally
            {
                isInFirstMeetDialogue = false;
            }
        }

        /// <summary>
        /// 面向玩家
        /// </summary>
        private void FacePlayer()
        {
            if (playerTransform == null) return;

            NPCExceptionHandler.TryExecute(() =>
            {
                Transform selfTransform = CachedTransform;
                Vector3 direction = playerTransform.position - selfTransform.position;
                direction.y = 0;  // 只在水平面上旋转
                if (direction.sqrMagnitude > 0.01f)
                {
                    selfTransform.rotation = Quaternion.LookRotation(direction);
                }
            }, "CourierNPCController.FacePlayer", false);
        }

        /// <summary>
        /// 生成 Wiki Book 物品（在NPC脚下）
        /// </summary>
        private void SpawnWikiBook()
        {
            try
            {
                // 使用 ItemAssetsCollection 生成物品
                Item wikiBook = ItemAssetsCollection.InstantiateSync(WIKI_BOOK_TYPE_ID);

                if (wikiBook == null)
                {
                    ModBehaviour.DevLog("[CourierNPC] [ERROR] 无法生成 Wiki Book 物品，TypeID=" + WIKI_BOOK_TYPE_ID);
                    return;
                }

                // 在NPC脚下生成物品（而不是直接发送给玩家）
                Vector3 dropPosition = CachedTransform.position;
                Vector3 dropDirection = Vector3.forward;
                wikiBook.Drop(dropPosition, true, dropDirection, 0f);

                ModBehaviour.DevLog("[CourierNPC] Wiki Book 物品已生成在NPC脚下，位置=" + dropPosition);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 生成 Wiki Book 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 更新与玩家的距离状态
        /// </summary>
        private void UpdateDistanceState()
        {
            if (playerTransform == null)
            {
                Transform foundPlayerTransform;
                if (NPCPlayerLookupCache.TryGetPlayerTransform(out foundPlayerTransform))
                {
                    playerTransform = foundPlayerTransform;
                }
            }

            if (playerTransform == null || !hasAnimator) return;

            float nearDistanceSqr = NEAR_DISTANCE * NEAR_DISTANCE;
            bool isNear = (CachedTransform.position - playerTransform.position).sqrMagnitude <= nearDistanceSqr;

            SafeSetBool(hash_IsNearPlayer, isNear);
        }

        /// <summary>
        /// 开始与玩家交互/对话
        /// </summary>
        public void StartTalking()
        {
            StartTalking(true);
        }

        public void StartTalking(bool showDialogue)
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsTalking, true);
                ModBehaviour.DevLog("[CourierNPC] 开始对话动画");
            }

            if (showDialogue)
            {
                // 显示随机对话气泡
                ShowRandomDialogue();
            }
        }

        /// <summary>
        /// 显示随机对话气泡（使用原版 DialogueBubblesManager）
        /// </summary>
        private void ShowRandomDialogue()
        {
            try
            {
                // 随机选择一句对话（直接使用 L10n.T）
                string dialogue = GetRandomCourierDialogue();

                // 使用原版气泡系统显示对话（speed=-1表示一次性显示全部文字）
                // yOffset 设置为名字标签高度附近（1.5f，比名字标签稍低）
                float yOffset = 1.5f;
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(dialogue, transform, yOffset, false, false, -1f, 3f)
                );

                ModBehaviour.DevLog("[CourierNPC] 显示对话: " + dialogue);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 显示对话气泡失败: " + e.Message);
            }
        }

        /// <summary>
        /// 获取随机快递员对话（优先使用 LocalizationInjector 注入的索引键）
        /// </summary>
        private string GetRandomCourierDialogue()
        {
            try
            {
                int dialogueCount = LocalizationInjector.GetCourierDialogueCount();
                if (dialogueCount > 0)
                {
                    int index = UnityEngine.Random.Range(0, dialogueCount);
                    string key = "BossRush_CourierDialogue_" + index;
                    string localized = LocalizationHelper.GetLocalizedText(key);
                    if (!string.IsNullOrEmpty(localized) && localized != key)
                    {
                        return localized;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 读取随机对话键失败: " + e.Message);
            }

            // 回退：保证在本地化注入异常时依然有可显示内容
            return L10n.T("补给到了……先把伞可乐灌了，灵魂别掉地上。", "Supplies arrived... drink your Umbrella Cola first, don't let your soul drop.");
        }

        /// <summary>
        /// 结束交互，返回之前的状态
        /// </summary>
        public void StopTalking()
        {
            // Mode E 固定模式下保持 IsTalking = true，不允许关闭
            if (isStationary) return;

            if (hasAnimator)
            {
                SafeSetBool(hash_IsTalking, false);
                ModBehaviour.DevLog("[CourierNPC] 结束对话动画");
            }
        }

        /// <summary>
        /// 设置是否已到达目标点（控制加油动画）
        /// </summary>
        public void SetArrived(bool arrived)
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsArrived, arrived);
                ModBehaviour.DevLog("[CourierNPC] 设置 IsArrived: " + arrived);
            }
        }

        /// <summary>
        /// 设置是否正在打Boss
        /// </summary>
        public void SetBossFight(bool isFighting)
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsBossFight, isFighting);
                ModBehaviour.DevLog("[CourierNPC] 设置 BossFight: " + isFighting);
            }
        }

        /// <summary>
        /// 设置是否没有Boss（召唤间隔期间）
        /// </summary>
        public void SetNoBoss(bool noBoss)
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_NoBoss, noBoss);
                ModBehaviour.DevLog("[CourierNPC] 设置 NoBoss: " + noBoss);
            }
        }

        /// <summary>
        /// 设置BossRush已通关（开始庆祝）
        /// </summary>
        public void SetCompleted()
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsCompleted, true);
                SafeSetBool(hash_IsBossFight, false);
                ModBehaviour.DevLog("[CourierNPC] 设置通关状态");
            }
        }

        /// <summary>
        /// 重置状态（新一轮BossRush）
        /// </summary>
        public void ResetState()
        {
            if (hasAnimator)
            {
                // Mode E 固定模式下保持 IsTalking = true
                if (!isStationary)
                {
                    SafeSetBool(hash_IsTalking, false);
                }
                SafeSetBool(hash_IsBossFight, false);
                SafeSetBool(hash_IsCompleted, false);
                ModBehaviour.DevLog("[CourierNPC] 重置状态" + (isStationary ? "（固定模式，保持IsTalking）" : ""));
            }
        }
    }

    /// <summary>
    /// 快递员移动控制（使用 A* Pathfinding Seeker）
    /// 严格模仿游戏原生的 AI_PathControl 实现
    /// </summary>
}
