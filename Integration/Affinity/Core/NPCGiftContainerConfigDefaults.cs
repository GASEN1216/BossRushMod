// ============================================================================
// NPCGiftContainerConfigDefaults.cs - NPC礼物容器配置默认值
// ============================================================================
// 提供 INPCGiftContainerConfig 接口的默认本地化键值。
// 当NPC配置未指定特定键值时，使用这些默认值。
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// NPC礼物容器配置默认值
    /// </summary>
    public static class NPCGiftContainerConfigDefaults
    {
        // ============================================================================
        // 默认本地化键值
        // ============================================================================
        
        /// <summary>
        /// 默认容器标题本地化键
        /// <para>对应文本："赠送礼物" / "Give Gift"</para>
        /// </summary>
        public const string DEFAULT_CONTAINER_TITLE_KEY = "BossRush_GiftContainer_DefaultTitle";
        
        /// <summary>
        /// 默认赠送按钮文本本地化键
        /// <para>对应文本："赠送" / "Give"</para>
        /// </summary>
        public const string DEFAULT_GIFT_BUTTON_KEY = "BossRush_GiftContainer_DefaultButton";
        
        /// <summary>
        /// 默认空槽位提示文本本地化键
        /// <para>对应文本："放入礼物" / "Place Gift"</para>
        /// </summary>
        public const string DEFAULT_EMPTY_SLOT_KEY = "BossRush_GiftContainer_DefaultEmptySlot";
        
        // ============================================================================
        // 默认行为配置
        // ============================================================================
        
        /// <summary>
        /// 默认是否使用容器式UI
        /// <para>默认为 true，使用新的容器式UI</para>
        /// </summary>
        public const bool DEFAULT_USE_CONTAINER_UI = true;
        
        // ============================================================================
        // 辅助方法
        // ============================================================================
        
        /// <summary>
        /// 获取容器标题键（如果配置为空则返回默认值）
        /// </summary>
        /// <param name="config">NPC礼物容器配置</param>
        /// <returns>本地化键</returns>
        public static string GetContainerTitleKey(INPCGiftContainerConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.ContainerTitleKey))
            {
                return DEFAULT_CONTAINER_TITLE_KEY;
            }
            return config.ContainerTitleKey;
        }
        
        /// <summary>
        /// 获取赠送按钮文本键（如果配置为空则返回默认值）
        /// </summary>
        /// <param name="config">NPC礼物容器配置</param>
        /// <returns>本地化键</returns>
        public static string GetGiftButtonTextKey(INPCGiftContainerConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.GiftButtonTextKey))
            {
                return DEFAULT_GIFT_BUTTON_KEY;
            }
            return config.GiftButtonTextKey;
        }
        
        /// <summary>
        /// 获取空槽位提示文本键（如果配置为空则返回默认值）
        /// </summary>
        /// <param name="config">NPC礼物容器配置</param>
        /// <returns>本地化键</returns>
        public static string GetEmptySlotTextKey(INPCGiftContainerConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.EmptySlotTextKey))
            {
                return DEFAULT_EMPTY_SLOT_KEY;
            }
            return config.EmptySlotTextKey;
        }
        
        /// <summary>
        /// 获取是否使用容器式UI（如果配置为null则返回默认值）
        /// </summary>
        /// <param name="config">NPC礼物容器配置</param>
        /// <returns>是否使用容器式UI</returns>
        public static bool GetUseContainerUI(INPCGiftContainerConfig config)
        {
            if (config == null)
            {
                return DEFAULT_USE_CONTAINER_UI;
            }
            return config.UseContainerUI;
        }
    }
}
