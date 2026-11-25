using System;

namespace BossRush
{
    [Serializable]
    public class AffinityData
    {
        public string npcId = "";
        public int points = 0;
        public int lastGiftDay = -1;
        public int lastGiftReaction = 0;
        public int lastChatDay = -1;
        public string interactionHistoryDays = "";
        public bool hasMet = false;
        public bool hasTriggeredStory5 = false;
        public bool hasTriggeredStory10 = false;
        public string triggeredEventKeys = "";
        public string claimedRewardKeys = "";
        public bool isMarriedToPlayer = false;
        public bool isFollowingPlayer = false;
        public string marriageDateText = "";
        public int cheatingIncidentCount = 0;
        public bool hasPendingCheatingRebuke = false;
        public int lastDecayCheckDay = -1;
        // NPC 商店库存（NPCShopStockLedger 编码）；旧档没有这个字段，按满库存处理。
        public string shopStockTokens = "";
        // 心意簿（2026-11-24 玩家许愿第 6 项）：送礼时摸清的喜好，逗号分隔的 item:<TypeID> / tag:<标签名>。
        // 旧档没有这两个字段，按「什么都还没摸清」处理。
        public string knownLikes = "";
        public string knownDislikes = "";

        public AffinityData()
        {
        }

        public AffinityData(string npcId)
        {
            this.npcId = npcId;
        }

        public AffinityData Clone()
        {
            return new AffinityData
            {
                npcId = this.npcId,
                points = this.points,
                lastGiftDay = this.lastGiftDay,
                lastGiftReaction = this.lastGiftReaction,
                lastChatDay = this.lastChatDay,
                interactionHistoryDays = this.interactionHistoryDays,
                hasMet = this.hasMet,
                hasTriggeredStory5 = this.hasTriggeredStory5,
                hasTriggeredStory10 = this.hasTriggeredStory10,
                triggeredEventKeys = this.triggeredEventKeys,
                claimedRewardKeys = this.claimedRewardKeys,
                isMarriedToPlayer = this.isMarriedToPlayer,
                isFollowingPlayer = this.isFollowingPlayer,
                marriageDateText = this.marriageDateText,
                cheatingIncidentCount = this.cheatingIncidentCount,
                hasPendingCheatingRebuke = this.hasPendingCheatingRebuke,
                lastDecayCheckDay = this.lastDecayCheckDay,
                shopStockTokens = this.shopStockTokens,
                knownLikes = this.knownLikes,
                knownDislikes = this.knownDislikes
            };
        }
    }
}
