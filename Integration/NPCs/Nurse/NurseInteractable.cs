// ============================================================================
// NurseInteractable.cs - 护士NPC交互组件
// ============================================================================
// 模块说明：
//   护士NPC"羽织"的交互组件，使用游戏原生 interactableGroup 模式
//   参考 GoblinInteractable 实现：
//   - 主选项为"聊天"（由 OnTimeOut 处理）
//   - 子选项：赠送礼物、治疗
//   
//   交互选项：聊天、赠送礼物、治疗（￥xxx）
//   无需"离开"选项（游戏原生交互系统自动处理）
// ============================================================================

using System;
using UnityEngine;
using BossRush.Utils;
using BossRush.Constants;
using Duckov;
using Duckov.Buffs;

namespace BossRush
{
    // ========================================================================
    // 护士治疗交互组件（子选项）
    // ========================================================================
    
    /// <summary>
    /// 护士治疗交互组件
    /// 作为护士的子交互选项，玩家选择后执行治疗服务
    /// </summary>
    public class NurseHealInteractable : InteractableBase
    {
        private const string HEAL_INTERACT_KEY = "BossRush_NurseHeal";

        private NurseNPCController controller;
        private NurseAffinityConfig _config;
        private NurseAffinityConfig config { get { if (_config == null) _config = new NurseAffinityConfig(); return _config; } }
        private bool isInitialized = false;
        private string _lastInjectedHealText = string.Empty;
        private CharacterBuffManager observedBuffManager;
        private bool stateListenersRegistered = false;
        private bool _handledDialogueEndThisInteraction = false;
        private float _nextOptionRefreshTime = 0f;
        private const float OPTION_REFRESH_INTERVAL = 2f;
        private const float INTERACT_STOP_FALLBACK_STAY_DURATION = 0.05f;
        
        protected override void Awake()
        {
            NPCExceptionHandler.TryExecute(() =>
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = HEAL_INTERACT_KEY;
                this.InteractName = HEAL_INTERACT_KEY;
                // 动态计算治疗费用显示在选项名称中
                UpdateHealOptionName();
                
                this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f);
            }, "NurseHealInteractable.Awake.Setup", false);
            
            NPCExceptionHandler.TryExecute(
                () => base.Awake(),
                "NurseHealInteractable.Awake.BaseAwake",
                false);
            NPCExceptionHandler.TryExecute(
                () => controller = GetComponentInParent<NurseNPCController>(),
                "NurseHealInteractable.Awake.GetController",
                false);
            
            // 子选项不需要自己的 Collider，隐藏交互标记
            NPCExceptionHandler.TryExecute(
                () => this.MarkerActive = false,
                "NurseHealInteractable.Awake.SetMarkerInactive",
                false);
            
            isInitialized = true;
        }
        
        protected override void Start()
        {
            NPCExceptionHandler.TryExecute(
                () => base.Start(),
                "NurseHealInteractable.Start.BaseStart",
                false);

            NPCExceptionHandler.TryExecute(
                () => RegisterStateListeners(),
                "NurseHealInteractable.Start.RegisterStateListeners",
                false);

            NPCExceptionHandler.TryExecute(
                () => UpdateHealOptionName(),
                "NurseHealInteractable.Start.UpdateHealOptionName",
                false);
        }

        private void OnEnable()
        {
            RegisterStateListeners();
            UpdateHealOptionName();
        }

        private void OnDisable()
        {
            UnregisterStateListeners();
        }

        protected override void OnDestroy()
        {
            UnregisterStateListeners();
            base.OnDestroy();
        }

        private void RegisterStateListeners()
        {
            if (!stateListenersRegistered)
            {
                // 治疗价格不依赖余额，仅在等级变化时刷新即可。
                EXPManager.onExpChanged += OnExpChanged;
                stateListenersRegistered = true;
            }

            BindBuffManagerEvents();
        }

        private void UnregisterStateListeners()
        {
            if (stateListenersRegistered)
            {
                EXPManager.onExpChanged -= OnExpChanged;
                stateListenersRegistered = false;
            }

            if (observedBuffManager != null)
            {
                observedBuffManager.onAddBuff -= OnBuffChanged;
                observedBuffManager.onRemoveBuff -= OnBuffChanged;
                observedBuffManager = null;
            }
        }

        private void BindBuffManagerEvents()
        {
            var player = CharacterMainControl.Main;
            var buffManager = player != null ? player.GetBuffManager() : null;
            if (buffManager == observedBuffManager)
            {
                return;
            }

            if (observedBuffManager != null)
            {
                observedBuffManager.onAddBuff -= OnBuffChanged;
                observedBuffManager.onRemoveBuff -= OnBuffChanged;
            }

            observedBuffManager = buffManager;
            if (observedBuffManager != null)
            {
                observedBuffManager.onAddBuff += OnBuffChanged;
                observedBuffManager.onRemoveBuff += OnBuffChanged;
            }
        }

        private void OnExpChanged(long newExp)
        {
            UpdateHealOptionName();
        }

        private void OnBuffChanged(CharacterBuffManager manager, Buff buff)
        {
            NurseHealingService.NotifyDebuffStateChanged();
            UpdateHealOptionName();
        }
        
        /// <summary>
        /// 更新治疗选项的显示名称（包含费用）
        /// </summary>
        public void UpdateHealOptionName()
        {
            try
            {
                int cost = NurseHealingService.CalculateHealCost();
                bool needsHealing = cost > 0;
                
                string healText;
                if (needsHealing)
                {
                    healText = L10n.T("治疗（￥" + cost + "）", "Heal ($" + cost + ")");
                }
                else
                {
                    healText = L10n.T("治疗（不需要）", "Heal (Not needed)");
                }
                
                if (_lastInjectedHealText != healText)
                {
                    LocalizationHelper.InjectLocalization(HEAL_INTERACT_KEY, healText);
                    _lastInjectedHealText = healText;
                }

                this._overrideInteractNameKey = HEAL_INTERACT_KEY;
                this.InteractName = HEAL_INTERACT_KEY;
            }
            catch
            {
                string fallbackText = L10n.T("治疗", "Heal");
                LocalizationHelper.InjectLocalization(HEAL_INTERACT_KEY, fallbackText);
                _lastInjectedHealText = fallbackText;

                this._overrideInteractNameKey = HEAL_INTERACT_KEY;
                this.InteractName = HEAL_INTERACT_KEY;
            }
        }
        
        protected override bool IsInteractable()
        {
            // 事件驱动刷新为主，低频兜底刷新为辅。
            RegisterStateListeners();
            BindBuffManagerEvents();
            float now = Time.realtimeSinceStartup;
            if (now >= _nextOptionRefreshTime)
            {
                _nextOptionRefreshTime = now + OPTION_REFRESH_INTERVAL;
                UpdateHealOptionName();
            }
            return isInitialized;
        }
        
        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                _handledDialogueEndThisInteraction = false;
                ModBehaviour.DevLog("[NurseNPC] 玩家选择治疗服务");

                // 播放护士专属交互音效（治疗）
                BossRushAudioManager.Instance?.PlayNPCInteractSFX(NurseAffinityConfig.NPC_ID);
                
                // 让护士进入对话状态
                if (controller == null)
                {
                    controller = GetComponentInParent<NurseNPCController>();
                }
                if (controller != null)
                {
                    controller.StartDialogue();
                }
                
                // 执行治疗逻辑
                DoHeal();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] 治疗服务交互出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 执行治疗逻辑
        /// </summary>
        private void DoHeal()
        {
            try
            {
                int cost;
                var status = NurseHealingService.GetHealingStatus(out cost);
                
                switch (status)
                {
                    case NurseHealingService.HealingStatus.FullHealthNoDebuff:
                    {
                        // 满血无Debuff - 显示对话并结束
                        string dialogue = NurseHealingService.GetHealingDialogue(status);
                        if (controller != null)
                        {
                            controller.ShowDialogueBubble(dialogue);
                            EndDialogueWithMark(NurseNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION);
                        }
                        break;
                    }
                    
                    case NurseHealingService.HealingStatus.InsufficientFunds:
                    {
                        // 金币不足
                        string dialogue = NurseHealingService.GetHealingDialogue(status);
                        if (controller != null)
                        {
                            controller.ShowDialogueBubble(dialogue);
                            EndDialogueWithMark(NurseNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION);
                        }
                        break;
                    }
                    
                    case NurseHealingService.HealingStatus.DebuffOnly:
                    {
                        // 仅Debuff - 显示提示后执行治疗
                        string preDialogue = NurseHealingService.GetHealingDialogue(status);
                        if (controller != null)
                        {
                            controller.ShowDialogueBubble(preDialogue);
                        }
                        
                        ExecuteHealing(cost);
                        break;
                    }
                    
                    case NurseHealingService.HealingStatus.NeedsHealing:
                    default:
                    {
                        // 需要治疗 - 直接执行
                        ExecuteHealing(cost);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] 治疗逻辑出错: " + e.Message);
                EndDialogueWithMark(NurseNPCConstants.SHORT_DIALOGUE_STAY_DURATION);
            }
        }
        
        /// <summary>
        /// 执行治疗（扣费+回血+清Debuff）
        /// </summary>
        private void ExecuteHealing(int cost)
        {
            if (cost <= 0)
            {
                if (controller != null)
                {
                    controller.ShowDialogueBubble(L10n.T("现在不需要治疗。", "No treatment needed right now."));
                    EndDialogueWithMark(NurseNPCConstants.SHORT_DIALOGUE_STAY_DURATION);
                }
                return;
            }

            bool success = NurseHealingService.PerformHealing(cost);
            
            if (success)
            {
                int level = NPCExceptionHandler.TryExecute(
                    () => AffinityManager.GetLevel(NurseAffinityConfig.NPC_ID),
                    "NurseHealInteractable.ExecuteHealing.GetAffinityLevel",
                    1,
                    false);
                string successDialogue = config.GetSpecialDialogue("heal_success", level);
                
                if (controller != null)
                {
                    controller.ShowDialogueBubble(successDialogue);
                    EndDialogueWithMark(NurseNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION);
                }

                ModBehaviour.DevLog("[NurseNPC] 治疗成功，费用: " + cost);
            }
            else
            {
                string failDialogue = L10n.T("出了点问题...再试一次吧。", "Something went wrong... try again.");
                if (controller != null)
                {
                    controller.ShowDialogueBubble(failDialogue);
                    EndDialogueWithMark(NurseNPCConstants.SHORT_DIALOGUE_STAY_DURATION);
                }
                ModBehaviour.DevLog("[NurseNPC] 治疗失败");
            }

            UpdateHealOptionName();
        }

        private void EndDialogueWithMark(float stayDuration)
        {
            if (controller == null)
            {
                controller = GetComponentInParent<NurseNPCController>();
            }

            if (controller == null)
            {
                return;
            }

            _handledDialogueEndThisInteraction = true;
            controller.EndDialogueWithStay(stayDuration);
        }

        private void EnsureDialogueEndedOnStop()
        {
            if (controller == null)
            {
                controller = GetComponentInParent<NurseNPCController>();
            }

            if (controller != null && !controller.IsInStoryDialogue)
            {
                controller.EndDialogueWithStay(INTERACT_STOP_FALLBACK_STAY_DURATION);
            }
        }

        protected override void OnInteractStop()
        {
            NPCExceptionHandler.TryExecute(
                () => base.OnInteractStop(),
                "NurseHealInteractable.OnInteractStop.BaseStop",
                false);

            if (!_handledDialogueEndThisInteraction)
            {
                NPCExceptionHandler.TryExecute(
                    () => EnsureDialogueEndedOnStop(),
                    "NurseHealInteractable.OnInteractStop.EnsureDialogueEnded",
                    false);
            }

            _handledDialogueEndThisInteraction = false;
        }
    }
    
    // ========================================================================
    // 护士主交互组件（交互组模式）
    // ========================================================================
    
    /// <summary>
    /// 护士主交互组件
    /// 使用交互组模式，支持多个交互选项（聊天、礼物、治疗）
    /// 主选项为"聊天"，子选项为赠送礼物、治疗
    /// 参考 GoblinInteractable 实现
    /// </summary>
    public class NurseInteractable : InteractableBase
    {
        private NurseNPCController controller;
        private NPCGiftInteractable giftInteractable;
        private NurseHealInteractable healInteractable;
        private bool _handledDialogueEndThisInteraction = false;
        private const float INTERACT_STOP_FALLBACK_STAY_DURATION = 0.05f;
        
        protected override void Awake()
        {
            try
            {
                // 使用交互组模式，显示多个选项
                this.interactableGroup = true;
                
                // 设置主交互名称为"聊天"（第一个选项）
                this.overrideInteractName = true;
                string chatText = L10n.T("聊天", "Chat");
                LocalizationHelper.InjectLocalization("BossRush_Chat", chatText);
                this._overrideInteractNameKey = "BossRush_Chat";
                this.InteractName = "BossRush_Chat";
                
                // 设置交互标记偏移
                this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] NurseInteractable.Awake 设置属性失败: " + e.Message);
            }
            
            // 确保有 Collider
            try
            {
                Collider col = GetComponent<Collider>();
                if (col == null)
                {
                    CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
                    capsule.height = 2.0f;   // 护士比哥布林高
                    capsule.radius = 0.6f;
                    capsule.center = new Vector3(0f, 1.0f, 0f);
                    capsule.isTrigger = false;
                    this.interactCollider = capsule;
                }
                else
                {
                    this.interactCollider = col;
                }
                
                // 设置 Layer 为 Interactable
                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer != -1)
                {
                    gameObject.layer = interactableLayer;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] NurseInteractable 设置 Collider 失败: " + e.Message);
            }

            // 在确保 Collider 就绪后再调用基类 Awake，避免 InteractableBase 内部空引用
            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] NurseInteractable base.Awake 异常: " + e.Message);
            }
            
            // 获取控制器
            controller = GetComponent<NurseNPCController>();
            
            ModBehaviour.DevLog("[NurseNPC] NurseInteractable.Awake 完成");
        }
        
        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [WARNING] NurseInteractable base.Start 异常: " + e.Message);
            }
            
            // 确保获取到控制器
            if (controller == null)
            {
                controller = GetComponent<NurseNPCController>();
            }
            
            // 创建子交互选项
            CreateSubInteractables();
        }
        
        /// <summary>
        /// 创建子交互选项（赠送礼物、治疗）
        /// 使用反射将子选项注入到 otherInterablesInGroup 列表中（与哥布林一致）
        /// 主选项为"聊天"（由 OnTimeOut 处理）
        /// 子选项顺序：1.赠送礼物 2.治疗
        /// </summary>
        private void CreateSubInteractables()
        {
            try
            {
                // 获取或创建 otherInterablesInGroup 列表（统一走公用助手）
                var list = NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[NurseNPC]");
                if (list == null)
                {
                    return;
                }

                // 1. 创建礼物赠送交互选项（使用通用组件）
                giftInteractable = NPCInteractionGroupHelper.AddSubInteractable<NPCGiftInteractable>(
                    transform,
                    "GiftInteractable",
                    list,
                    component => component.NpcId = NurseAffinityConfig.NPC_ID);
                if (giftInteractable != null)
                {
                    ModBehaviour.DevLog("[NurseNPC] 创建礼物赠送交互选项并注入到交互组（使用通用组件）");
                }

                // 2. 创建治疗交互选项
                healInteractable = NPCInteractionGroupHelper.AddSubInteractable<NurseHealInteractable>(
                    transform,
                    "HealInteractable",
                    list);
                if (healInteractable != null)
                {
                    ModBehaviour.DevLog("[NurseNPC] 创建治疗交互选项并注入到交互组");
                }

                ModBehaviour.DevLog("[NurseNPC] 所有子交互选项已注入到 otherInterablesInGroup，共 " + list.Count + " 个（主选项=聊天）");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] 创建子交互选项失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        protected override bool IsInteractable()
        {
            return true;
        }
        
        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            base.OnInteractStart(interactCharacter);
            _handledDialogueEndThisInteraction = false;
            ModBehaviour.DevLog("[NurseNPC] 玩家开始与护士交互（交互组模式）");
            
            // 让护士进入对话状态
            if (controller != null)
            {
                controller.StartDialogue();
            }
        }
        
        /// <summary>
        /// 主交互选项"聊天"被选中时调用
        /// 聊天不限制次数，每次都从话语库随机选一条
        /// 但好感度增加每天只有一次
        /// 好感度等级 >= 8 时每次聊天都冒爱心
        /// </summary>
        protected override void OnTimeOut()
        {
            try
            {
                ModBehaviour.DevLog("[NurseNPC] 玩家选择聊天（主选项）");

                // 播放护士专属交互音效（聊天）
                BossRushAudioManager.Instance?.PlayNPCInteractSFX(NurseAffinityConfig.NPC_ID);
                
                // 获取控制器
                if (controller == null)
                {
                    controller = GetComponent<NurseNPCController>();
                }
                
                // 让护士进入对话状态
                if (controller != null)
                {
                    controller.StartDialogue();
                }
                
                // 执行聊天逻辑
                DoChat();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] 聊天交互出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 执行聊天逻辑（参考 GoblinInteractable.DoChat）
        /// </summary>
        private void DoChat()
        {
            try
            {
                NPCAffinityInteractionHelper.ProcessChatAffinityAndFeedback(
                    NurseAffinityConfig.NPC_ID,
                    NurseAffinityConfig.DAILY_CHAT_AFFINITY,
                    8,
                    () =>
                    {
                        if (controller != null)
                        {
                            controller.ShowLoveHeartBubble();
                        }
                    },
                    "[NurseNPC]");
                
                // 从问候对话库随机选一条显示
                string dialogue = NPCDialogueSystem.GetDialogue(NurseAffinityConfig.NPC_ID, DialogueCategory.Greeting);
                NPCDialogueSystem.ShowDialogue(NurseAffinityConfig.NPC_ID, controller?.transform, dialogue);
                
                // 延迟结束对话（使用10秒停留）
                if (controller != null)
                {
                    EndDialogueWithMark(NurseNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NurseNPC] [ERROR] 执行对话失败: " + e.Message);

                // 结束对话
                EndDialogueWithMark(NurseNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION);
            }
        }

        private void EndDialogueWithMark(float stayDuration)
        {
            if (controller == null)
            {
                controller = GetComponent<NurseNPCController>();
            }

            if (controller == null)
            {
                return;
            }

            _handledDialogueEndThisInteraction = true;
            controller.EndDialogueWithStay(stayDuration);
        }

        private void EnsureDialogueEndedOnStop()
        {
            if (controller == null)
            {
                controller = GetComponent<NurseNPCController>();
            }

            if (controller != null && !controller.IsInStoryDialogue)
            {
                controller.EndDialogueWithStay(INTERACT_STOP_FALLBACK_STAY_DURATION);
            }
        }

        protected override void OnInteractStop()
        {
            NPCExceptionHandler.TryExecute(
                () => base.OnInteractStop(),
                "NurseInteractable.OnInteractStop.BaseStop",
                false);

            if (!_handledDialogueEndThisInteraction)
            {
                NPCExceptionHandler.TryExecute(
                    () => EnsureDialogueEndedOnStop(),
                    "NurseInteractable.OnInteractStop.EnsureDialogueEnded",
                    false);
            }

            _handledDialogueEndThisInteraction = false;
        }
    }
}
