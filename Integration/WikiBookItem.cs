// ============================================================================
// WikiBookItem.cs - Wiki 百科全书物品
// ============================================================================
// 模块说明：
//   管理 Wiki Book 物品的加载、配置和注册，包括：
//   - 从 AssetBundle 加载 Wiki UI 和书物品预制体
//   - 动态添加使用行为（打开 Wiki UI，不消耗物品）
//   - 本地化注入
// ============================================================================

using System;
using UnityEngine;
using Duckov.ItemUsage;
using ItemStatsSystem;

namespace BossRush
{

    
    // ============================================================================
    // WikiBookUsageBehavior - Wiki Book 使用行为
    // ============================================================================
    
    /// <summary>
    /// Wiki Book 使用行为：打开 Wiki UI，不消耗物品
    /// </summary>
    public class WikiBookUsageBehavior : UsageBehavior
    {
        /// <summary>
        /// 检查物品是否可以使用
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            // Wiki Book 始终可以使用
            return true;
        }
        
        /// <summary>
        /// 使用物品时调用
        /// </summary>
        protected override void OnUse(Item item, object user)
        {
            try
            {
                // 打开 Wiki UI
                WikiUIManager.Instance.OpenUI();
                ModBehaviour.DevLog("[WikiBook] 打开 Wiki UI");
                
                // 注意：这里不调用 Consume() 或任何消耗物品的方法
                // 所以书不会被消耗，可以无限次使用
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiBook] 打开 UI 失败: " + e.Message);
            }
        }
    }
}
