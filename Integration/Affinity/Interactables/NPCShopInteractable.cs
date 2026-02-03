// ============================================================================
// NPCShopInteractable.cs - 通用NPC商店交互组件
// ============================================================================
// 模块说明：
//   通用的NPC商店交互组件，支持任意配置了商店系统的NPC。
//   通过 npcId 参数化，无需为每个NPC创建单独的交互组件。
//   使用事件驱动更新可见性，避免每帧轮询，优化低端机性能。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 通用NPC商店交互组件
    /// </summary>
    public class NPCShopInteractable : NPCInteractableBase
    {
        // 缓存的可见性状态，避免重复计算
        private bool cachedVisibility = false;
        private bool visibilityCached = false;
        
        // ============================================================================
        // 交互设置
        // ============================================================================
        
        protected override void SetupInteractName()
        {
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_NPCShop";
                this.InteractName = "BossRush_NPCShop";
            }
            catch { }
        }
        
        protected override void Awake()
        {
            base.Awake();
            
            // 初始化时检查好感度，决定是否显示
            UpdateVisibility();
        }
        
        protected override void Start()
        {
            base.Start();
            UpdateVisibility();
        }
        
        protected virtual void OnEnable()
        {
            // 订阅好感度变化事件（事件驱动，避免每帧轮询）
            AffinityManager.OnAffinityChanged += OnAffinityChanged;
            AffinityManager.OnLevelUp += OnLevelUp;
        }
        
        protected virtual void OnDisable()
        {
            // 取消订阅事件，防止内存泄漏
            AffinityManager.OnAffinityChanged -= OnAffinityChanged;
            AffinityManager.OnLevelUp -= OnLevelUp;
        }
        
        /// <summary>
        /// 好感度变化时更新可见性
        /// </summary>
        private void OnAffinityChanged(string changedNpcId, int oldPoints, int newPoints)
        {
            // 只响应当前NPC的变化
            if (changedNpcId == npcId)
            {
                UpdateVisibility();
            }
        }
        
        /// <summary>
        /// 等级提升时更新可见性
        /// </summary>
        private void OnLevelUp(string changedNpcId, int newLevel)
        {
            // 只响应当前NPC的变化
            if (changedNpcId == npcId)
            {
                UpdateVisibility();
            }
        }
        
        /// <summary>
        /// 根据好感度等级更新商店选项的可见性
        /// </summary>
        private void UpdateVisibility()
        {
            if (string.IsNullOrEmpty(npcId)) return;
            
            bool shouldBeVisible = NPCShopSystem.IsShopUnlocked(npcId);
            
            // 更新缓存
            cachedVisibility = shouldBeVisible;
            visibilityCached = true;
            
            // 只在状态变化时设置，避免频繁调用
            if (gameObject.activeSelf != shouldBeVisible)
            {
                gameObject.SetActive(shouldBeVisible);
                ModBehaviour.DevLog("[NPCShop] 商店选项可见性更新: " + shouldBeVisible + " (NPC: " + npcId + ")");
            }
        }
        
        protected override bool IsInteractable()
        {
            // 使用缓存的可见性状态，避免重复计算
            if (visibilityCached)
            {
                return isInitialized && !string.IsNullOrEmpty(npcId) && cachedVisibility;
            }
            return isInitialized && !string.IsNullOrEmpty(npcId) && NPCShopSystem.IsShopUnlocked(npcId);
        }
        
        // ============================================================================
        // 交互逻辑
        // ============================================================================
        
        protected override void DoInteract(CharacterMainControl character)
        {
            ModBehaviour.DevLog("[NPCShop] 玩家选择商店服务: " + npcId);
            
            // 打开商店UI
            NPCShopSystem.OpenShop(npcId, transform.parent, npcController);
        }
    }
}
