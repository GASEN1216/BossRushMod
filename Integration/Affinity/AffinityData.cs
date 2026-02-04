// ============================================================================
// AffinityData.cs - 好感度数据结构
// ============================================================================
// 模块说明：
//   定义好感度系统的数据结构，用于存储单个NPC的好感度数据。
//   支持JSON序列化，通过 ModConfigAPI 进行持久化存储。
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>
    /// 单个NPC的好感度数据
    /// 使用 AffinityJsonSerializer 进行序列化，无需 Unity SerializeField 标记
    /// </summary>
    [Serializable]
    public class AffinityData
    {
        /// <summary>NPC标识符</summary>
        public string npcId = "";
        
        /// <summary>好感度点数（0-2300，使用递增式等级配置）</summary>
        public int points = 0;
        
        /// <summary>上次赠送礼物的游戏日期</summary>
        public int lastGiftDay = -1;
        
        /// <summary>上次赠送礼物的反应类型（0=普通, 1=喜欢, -1=不喜欢）</summary>
        public int lastGiftReaction = 0;
        
        /// <summary>上次对话的游戏日期（用于每日对话限制）</summary>
        public int lastChatDay = -1;
        
        /// <summary>是否已遇见此NPC</summary>
        public bool hasMet = false;
        
        /// <summary>5级故事对话是否已触发（持久化）</summary>
        public bool hasTriggeredStory5 = false;
        
        /// <summary>10级故事对话是否已触发（持久化）</summary>
        public bool hasTriggeredStory10 = false;
        
        /// <summary>上次衰减检查的游戏日期（用于每日衰减计算）</summary>
        public int lastDecayCheckDay = -1;
        
        /// <summary>
        /// 创建默认数据
        /// </summary>
        public AffinityData()
        {
            npcId = "";
            points = 0;
            lastGiftDay = -1;
            lastGiftReaction = 0;
            lastChatDay = -1;
            hasMet = false;
            hasTriggeredStory5 = false;
            hasTriggeredStory10 = false;
            lastDecayCheckDay = -1;
        }
        
        /// <summary>
        /// 创建指定NPC的默认数据
        /// </summary>
        public AffinityData(string npcId)
        {
            this.npcId = npcId;
            this.points = 0;
            this.lastGiftDay = -1;
            this.lastGiftReaction = 0;
            this.lastChatDay = -1;
            this.hasMet = false;
            this.hasTriggeredStory5 = false;
            this.hasTriggeredStory10 = false;
            this.lastDecayCheckDay = -1;
        }
        
        /// <summary>
        /// 克隆数据
        /// </summary>
        public AffinityData Clone()
        {
            return new AffinityData
            {
                npcId = this.npcId,
                points = this.points,
                lastGiftDay = this.lastGiftDay,
                lastGiftReaction = this.lastGiftReaction,
                lastChatDay = this.lastChatDay,
                hasMet = this.hasMet,
                hasTriggeredStory5 = this.hasTriggeredStory5,
                hasTriggeredStory10 = this.hasTriggeredStory10,
                lastDecayCheckDay = this.lastDecayCheckDay
            };
        }
    }
    
}
