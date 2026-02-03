// ============================================================================
// INPCDialogueConfig.cs - NPC对话配置接口
// ============================================================================
// 模块说明：
//   定义NPC对话系统的配置接口，支持根据好感度等级返回不同对话。
//   实现此接口的NPC可以使用通用对话系统 NPCDialogueSystem。
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// 对话类型枚举（扩展版）
    /// </summary>
    public enum DialogueCategory
    {
        /// <summary>问候</summary>
        Greeting,
        /// <summary>收到礼物后</summary>
        AfterGift,
        /// <summary>等级提升</summary>
        LevelUp,
        /// <summary>购物时</summary>
        Shopping,
        /// <summary>今日已赠送礼物</summary>
        AlreadyGifted,
        /// <summary>闲聊</summary>
        Idle,
        /// <summary>告别</summary>
        Farewell,
        /// <summary>特殊事件（NPC自定义）</summary>
        Special
    }
    
    /// <summary>
    /// NPC对话配置接口
    /// <para>实现此接口以定义NPC的对话内容</para>
    /// </summary>
    public interface INPCDialogueConfig
    {
        // ============================================================================
        // 对话获取
        // ============================================================================
        
        /// <summary>
        /// 获取指定类型和等级的对话内容
        /// </summary>
        /// <param name="category">对话类型</param>
        /// <param name="level">当前好感度等级</param>
        /// <returns>本地化后的对话文本</returns>
        string GetDialogue(DialogueCategory category, int level);
        
        /// <summary>
        /// 获取特殊事件对话（NPC自定义事件）
        /// </summary>
        /// <param name="eventKey">事件标识符</param>
        /// <param name="level">当前好感度等级</param>
        /// <returns>本地化后的对话文本，如果事件不存在返回null</returns>
        string GetSpecialDialogue(string eventKey, int level);
        
        // ============================================================================
        // 对话气泡配置
        // ============================================================================
        
        /// <summary>
        /// 对话气泡显示高度（相对于NPC位置）
        /// </summary>
        float DialogueBubbleHeight { get; }
        
        /// <summary>
        /// 默认对话显示时长（秒）
        /// </summary>
        float DefaultDialogueDuration { get; }
    }
}
