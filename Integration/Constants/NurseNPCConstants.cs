// ============================================================================
// NurseNPCConstants.cs - 护士NPC相关常量
// ============================================================================
// 模块说明：
//   统一管理护士NPC相关的魔法数字，便于修改和维护
//   与 GoblinNPCConstants 保持一致的风格
// ============================================================================

namespace BossRush.Constants
{
    /// <summary>
    /// 护士NPC相关常量
    /// </summary>
    public static class NurseNPCConstants
    {
        // ============================================================================
        // 名字标签配置
        // ============================================================================

        /// <summary>名字标签高度</summary>
        public const float NAME_TAG_HEIGHT = 2.5f;

        // ============================================================================
        // 距离阈值配置（单位：米）
        // ============================================================================

        /// <summary>玩家靠近距离（在此距离内护士会面向玩家）</summary>
        public const float NEAR_DISTANCE = 5f;

        /// <summary>故事对话触发距离（玩家在此距离内时触发）</summary>
        public const float STORY_DIALOGUE_TRIGGER_DISTANCE = 3f;

        /// <summary>故事对话检测间隔（秒）</summary>
        public const float STORY_DIALOGUE_CHECK_INTERVAL = 0.5f;

        /// <summary>故事对话触发失败后的重试间隔（秒）</summary>
        public const float STORY_DIALOGUE_RETRY_INTERVAL = 30f;

        /// <summary>玩家引用重试间隔（秒）</summary>
        public const float PLAYER_LOOKUP_INTERVAL = 0.5f;

        // ============================================================================
        // 待机时间配置（单位：秒）
        // ============================================================================

        /// <summary>默认对话停留时间</summary>
        public const float DEFAULT_DIALOGUE_STAY_DURATION = 10f;

        /// <summary>异常/失败提示停留时间</summary>
        public const float SHORT_DIALOGUE_STAY_DURATION = 5f;

        // ============================================================================
        // 闲置气泡配置
        // ============================================================================

        /// <summary>闲置气泡最小间隔（秒）</summary>
        public const float IDLE_BUBBLE_MIN_INTERVAL = 15f;

        /// <summary>闲置气泡最大间隔（秒）</summary>
        public const float IDLE_BUBBLE_MAX_INTERVAL = 30f;

        /// <summary>初始闲置气泡最小延迟（秒）</summary>
        public const float IDLE_BUBBLE_INITIAL_MIN_DELAY = 10f;

        /// <summary>初始闲置气泡最大延迟（秒）</summary>
        public const float IDLE_BUBBLE_INITIAL_MAX_DELAY = 20f;

        // ============================================================================
        // 特效气泡配置
        // ============================================================================

        /// <summary>气泡Y轴偏移（文字气泡）</summary>
        public const float BUBBLE_OFFSET_Y = 0.3f;

        /// <summary>气泡动画Y轴偏移（序列帧动画）</summary>
        public const float BUBBLE_ANIMATION_OFFSET_Y = 0.8f;

        /// <summary>气泡显示时长（秒）</summary>
        public const float BUBBLE_DURATION = 2f;
    }
}
