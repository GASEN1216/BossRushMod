// ============================================================================
// NPCInteractableBase.cs - NPC交互组件基类
// ============================================================================
// 模块说明：
//   所有NPC交互组件的基类，提供通用功能和NPC ID配置。
//   子类只需实现具体的交互逻辑。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using BossRush.Utils;

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
        protected INPCController npcController;
        
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
                NPCExceptionHandler.TryExecute(
                    () => this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f),
                    "NPCInteractableBase.Awake.SetInteractMarkerOffset",
                    false);
                
                // 调用基类
                NPCExceptionHandler.TryExecute(
                    () => base.Awake(),
                    "NPCInteractableBase.Awake.BaseAwake",
                    false);
                
                // 获取NPC控制器（通用接口）
                NPCExceptionHandler.TryExecute(
                    () => npcController = GetComponentInParent<INPCController>(),
                    "NPCInteractableBase.Awake.GetController",
                    false);
                
                // 尝试从父对象获取NPC ID
                if (string.IsNullOrEmpty(npcId))
                {
                    TryGetNpcIdFromParent();
                }
                
                // 子选项不需要自己的 Collider，隐藏交互标记
                NPCExceptionHandler.TryExecute(
                    () => this.MarkerActive = false,
                    "NPCInteractableBase.Awake.SetMarkerInactive",
                    false);
                
                isInitialized = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NPCInteractable] Awake异常: " + e.Message);
            }
        }
        
        protected override void Start()
        {
            NPCExceptionHandler.TryExecute(
                () => base.Start(),
                "NPCInteractableBase.Start.BaseStart",
                false);
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
                
                // 按 NPC ID 分发交互音效，避免跨 NPC 误播
                BossRushAudioManager.Instance?.PlayNPCInteractSFX(npcId);
                
                // 获取控制器
                if (npcController == null)
                {
                    npcController = GetComponentInParent<INPCController>();
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
            NPCExceptionHandler.TryExecute(
                () => base.OnInteractStop(),
                "NPCInteractableBase.OnInteractStop.BaseStop",
                false);
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
            
            // 尝试从控制器获取NPC ID
            if (npcController != null)
            {
                npcId = npcController.NpcId;
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

    /// <summary>
    /// NPC交互组构建辅助（主交互 + 子交互注入）
    /// </summary>
    public static class NPCInteractionGroupHelper
    {
        private static readonly BindingFlags GroupFieldBindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// 获取或创建交互组子项列表 otherInterablesInGroup
        /// </summary>
        public static List<InteractableBase> GetOrCreateGroupList(InteractableBase owner, string logPrefix)
        {
            if (owner == null)
            {
                ModBehaviour.DevLog(logPrefix + " [ERROR] 交互组构建失败：owner 为空");
                return null;
            }

            try
            {
                FieldInfo field = typeof(InteractableBase).GetField("otherInterablesInGroup", GroupFieldBindingFlags);
                if (field == null)
                {
                    ModBehaviour.DevLog(logPrefix + " [ERROR] 无法获取 otherInterablesInGroup 字段");
                    return null;
                }

                var list = field.GetValue(owner) as List<InteractableBase>;
                if (list == null)
                {
                    list = new List<InteractableBase>();
                    field.SetValue(owner, list);
                }

                return list;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + " [ERROR] 获取交互组列表失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 向交互组添加子交互组件
        /// </summary>
        public static T AddSubInteractable<T>(
            Transform parent,
            string childName,
            List<InteractableBase> groupList,
            Action<T> setup = null)
            where T : InteractableBase
        {
            if (parent == null || groupList == null)
            {
                return null;
            }

            GameObject childObj = new GameObject(childName);
            childObj.transform.SetParent(parent);
            childObj.transform.localPosition = Vector3.zero;

            T component = childObj.AddComponent<T>();
            if (setup != null)
            {
                setup(component);
            }

            groupList.Add(component);
            return component;
        }
    }
}
