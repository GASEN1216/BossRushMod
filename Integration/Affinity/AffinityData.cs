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
        public string marriageDateText = "";
        public int cheatingIncidentCount = 0;
        public bool hasPendingCheatingRebuke = false;
        public int lastDecayCheckDay = -1;

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
                marriageDateText = this.marriageDateText,
                cheatingIncidentCount = this.cheatingIncidentCount,
                hasPendingCheatingRebuke = this.hasPendingCheatingRebuke,
                lastDecayCheckDay = this.lastDecayCheckDay
            };
        }
    }
}
