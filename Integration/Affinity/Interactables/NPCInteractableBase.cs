// ============================================================================
// NPCInteractableBase.cs - NPC交互组件基类
// ============================================================================
// 模块说明：
//   所有NPC交互组件的基类，提供通用功能和NPC ID配置。
//   子类只需实现具体的交互逻辑。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// NPC交互组件基类
    /// </summary>
    public abstract class NPCInteractableBase : InteractableBase
    {
        // ============================================================================
        // 配置字段
        // ============================================================================
        
        /// <summary>
        /// NPC标识符（在Inspector中配置或由父对象提供）
        /// </summary>
        [SerializeField]
        protected string npcId;
        
        /// <summary>
        /// 是否已初始化
        /// </summary>
        protected bool isInitialized = false;
        
        /// <summary>
        /// NPC控制器引用（可选）
        /// </summary>
        protected GoblinNPCController npcController;
        
        // ============================================================================
        // 公共属性
        // ============================================================================
        
        /// <summary>
        /// 获取或设置NPC ID
        /// </summary>
        public string NpcId
        {
            get => npcId;
            set => npcId = value;
        }
        
        // ============================================================================
        // Unity生命周期
        // ============================================================================
        
        protected override void Awake()
        {
            try
            {
                // 设置交互名称
                SetupInteractName();
                
                // 设置交互标记偏移
                try { this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f); } catch { }
                
                // 调用基类
                try { base.Awake(); } catch { }
                
                // 获取NPC控制器
                try { npcController = GetComponentInParent<GoblinNPCController>(); } catch { }
                
                // 尝试从父对象获取NPC ID
                if (string.IsNullOrEmpty(npcId))
                {
                    TryGetNpcIdFromParent();
                }
                
                // 子选项不需要自己的 Collider，隐藏交互标记
                try { this.MarkerActive = false; } catch { }
                
                isInitialized = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCInteractable] Awake异常: " + e.Message);
            }
        }
        
        protected override void Start()
        {
            try { base.Start(); } catch { }
        }
        
        // ============================================================================
        // 抽象方法
        // ============================================================================
        
        /// <summary>
        /// 设置交互名称（子类实现）
        /// </summary>
        protected abstract void SetupInteractName();
        
        /// <summary>
        /// 执行交互逻辑（子类实现）
        /// </summary>
        protected abstract void DoInteract(CharacterMainControl character);
        
        // ============================================================================
        // 交互处理
        // ============================================================================
        
        protected override bool IsInteractable()
        {
            return isInitialized && !string.IsNullOrEmpty(npcId);
        }
        
        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                
                // 播放交互音效
                BossRushAudioManager.Instance?.PlayGoblinInteractSFX();
                
                // 获取控制器
                if (npcController == null)
                {
                    npcController = GetComponentInParent<GoblinNPCController>();
                }
                
                // 执行具体交互逻辑
                DoInteract(interactCharacter);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCInteractable] 交互异常: " + e.Message);
                EndInteraction();
            }
        }
        
        protected override void OnInteractStop()
        {
            try { base.OnInteractStop(); } catch { }
        }
        
        // ============================================================================
        // 辅助方法
        // ============================================================================
        
        /// <summary>
        /// 尝试从父对象获取NPC ID
        /// </summary>
        protected virtual void TryGetNpcIdFromParent()
        {
            // 尝试从父对象的其他组件获取NPC ID
            var parentInteractables = GetComponentsInParent<NPCInteractableBase>();
            foreach (var interactable in parentInteractables)
            {
                if (interactable != this && !string.IsNullOrEmpty(interactable.npcId))
                {
                    npcId = interactable.npcId;
                    return;
                }
            }
            
            // 尝试从 GoblinNPCController 获取（如果是哥布林）
            if (npcController != null)
            {
                npcId = GoblinAffinityConfig.NPC_ID;
            }
        }
        
        /// <summary>
        /// 让NPC进入对话状态
        /// </summary>
        protected void StartNPCDialogue()
        {
            if (npcController != null)
            {
                npcController.StartDialogue();
            }
        }
        
        /// <summary>
        /// 结束NPC对话状态（带停留时间）
        /// </summary>
        protected void EndNPCDialogue(float stayDuration = 10f)
        {
            if (npcController != null)
            {
                npcController.EndDialogueWithStay(stayDuration);
            }
        }
        
        /// <summary>
        /// 结束交互
        /// </summary>
        protected void EndInteraction()
        {
            EndNPCDialogue();
        }
        
        /// <summary>
        /// 获取NPC配置
        /// </summary>
        protected INPCAffinityConfig GetNPCConfig()
        {
            return AffinityManager.GetNPCConfig(npcId);
        }
        
        /// <summary>
        /// 获取当前好感度等级
        /// </summary>
        protected int GetCurrentLevel()
        {
            return AffinityManager.GetLevel(npcId);
        }
    }
}
