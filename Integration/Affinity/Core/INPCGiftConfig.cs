// ============================================================================
// INPCGiftConfig.cs - NPC礼物配置接口
// ============================================================================
// 模块说明：
//   定义NPC礼物系统的配置接口，支持自定义礼物偏好和反应对话。
//   实现此接口的NPC可以使用通用礼物系统 NPCGiftSystem。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// 礼物反应类型
    /// </summary>
    public enum GiftReactionType
    {
        /// <summary>普通反应</summary>
        Normal = 0,
        /// <summary>正向反应（喜欢的物品）</summary>
        Positive = 1,
        /// <summary>负向反应（讨厌的物品）</summary>
        Negative = -1
    }
    
    /// <summary>
    /// NPC礼物配置接口
    /// <para>实现此接口以定义NPC的礼物偏好和反应</para>
    /// </summary>
    public interface INPCGiftConfig
    {
        // ============================================================================
        // 每日礼物配置
        // ============================================================================
        
        /// <summary>
        /// 每日对话好感度增加值（默认10）
        /// </summary>
        int DailyChatAffinity { get; }
        
        // ============================================================================
        // 物品偏好配置
        // ============================================================================
        
        /// <summary>
        /// 喜爱的物品列表（TypeID -> 好感度增加值）
        /// </summary>
        Dictionary<int, int> PositiveItems { get; }
        
        /// <summary>
        /// 讨厌的物品列表（TypeID -> 好感度减少值，正数会被转为负数）
        /// </summary>
        Dictionary<int, int> NegativeItems { get; }
        
        // ============================================================================
        // 反应对话配置
        // ============================================================================
        
        /// <summary>
        /// 正向反应对话气泡列表（收到喜欢的物品时）
        /// </summary>
        string[] PositiveBubbles { get; }
        
        /// <summary>
        /// 负向反应对话气泡列表（收到讨厌的物品时）
        /// </summary>
        string[] NegativeBubbles { get; }
        
        /// <summary>
        /// 普通反应对话气泡列表（收到普通物品时）
        /// </summary>
        string[] NormalBubbles { get; }
        
        /// <summary>
        /// 今日已赠送礼物的对话列表（根据反应类型）
        /// </summary>
        string[] GetAlreadyGiftedDialogues(GiftReactionType lastReaction);
        
        // ============================================================================
        // 特殊反应配置（可选）
        // ============================================================================
        
        /// <summary>
        /// 收到喜欢的礼物时是否显示爱心动画
        /// </summary>
        bool ShowLoveHeartOnPositive { get; }
        
        /// <summary>
        /// 收到讨厌的礼物时是否显示心碎动画
        /// </summary>
        bool ShowBrokenHeartOnNegative { get; }
    }
}
