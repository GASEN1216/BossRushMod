// ============================================================================
// GoblinReforgeInteractable.cs - 哥布林重铸交互组件
// ============================================================================
// 模块说明：
//   为哥布林NPC添加"重铸"交互选项
//   玩家交互后打开重铸UI
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 哥布林重铸交互组件
    /// 作为哥布林的子交互选项，玩家选择后打开重铸UI
    /// </summary>
    public class GoblinReforgeInteractable : InteractableBase
    {
        private GoblinNPCController controller;
        private bool isInitialized = false;
        
        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Reforge";
                this.InteractName = "BossRush_Reforge";
            }
            catch { }
            
            try { this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f); } catch { }
            
            try { base.Awake(); } catch { }
            
            try { controller = GetComponentInParent<GoblinNPCController>(); } catch { }
            
            // 子选项不需要自己的 Collider，隐藏交互标记
            try { this.MarkerActive = false; } catch { }
            
            isInitialized = true;
        }
        
        protected override void Start()
        {
            try { base.Start(); } catch { }
        }
        
        protected override bool IsInteractable()
        {
            return isInitialized;
        }
        
        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                ModBehaviour.DevLog("[GoblinNPC] 玩家选择重铸服务");
                
                // 让哥布林进入对话状态
                if (controller != null)
                {
                    controller.StartDialogue();
                }
                
                // 打开重铸UI
                ReforgeUIManager.OpenUI(controller);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 重铸服务交互出错: " + e.Message);
            }
        }
        
        protected override void OnInteractStop()
        {
            try { base.OnInteractStop(); } catch { }
        }
    }
    
    /// <summary>
    /// 哥布林主交互组件
    /// 直接交互打开重铸UI，不使用交互组模式
    /// </summary>
    public class GoblinInteractable : InteractableBase
    {
        private GoblinNPCController controller;
        
        protected override void Awake()
        {
            try
            {
                // 不使用交互组模式，直接交互
                this.interactableGroup = false;
                
                // 设置交互名称为重铸
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_Reforge";
                this.InteractName = "BossRush_Reforge";
                
                // 设置交互标记偏移
                this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] GoblinInteractable.Awake 设置属性失败: " + e.Message);
            }
            
            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] GoblinInteractable base.Awake 异常: " + e.Message);
            }
            
            // 确保有 Collider
            try
            {
                Collider col = GetComponent<Collider>();
                if (col == null)
                {
                    CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
                    capsule.height = 1.5f;  // 哥布林较矮
                    capsule.radius = 0.6f;
                    capsule.center = new Vector3(0f, 0.75f, 0f);
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
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] GoblinInteractable 设置 Collider 失败: " + e.Message);
            }
            
            // 获取控制器
            controller = GetComponent<GoblinNPCController>();
            
            ModBehaviour.DevLog("[GoblinNPC] GoblinInteractable.Awake 完成");
        }
        
        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] GoblinInteractable base.Start 异常: " + e.Message);
            }
            
            // 确保获取到控制器
            if (controller == null)
            {
                controller = GetComponent<GoblinNPCController>();
            }
        }
        
        protected override bool IsInteractable()
        {
            return true;
        }
        
        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            base.OnInteractStart(interactCharacter);
            ModBehaviour.DevLog("[GoblinNPC] 玩家开始与哥布林交互");
        }
        
        protected override void OnTimeOut()
        {
            // 直接打开重铸UI
            try
            {
                ModBehaviour.DevLog("[GoblinNPC] 玩家选择重铸服务");
                
                // 让哥布林进入对话状态
                if (controller != null)
                {
                    controller.StartDialogue();
                }
                
                // 打开重铸UI
                ReforgeUIManager.OpenUI(controller);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [ERROR] 重铸服务交互出错: " + e.Message);
            }
        }
    }
}
