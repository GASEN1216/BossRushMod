// ============================================================================
// BossRushAchievementDef.cs - 成就定义数据结构
// ============================================================================

using System;

namespace BossRush
{
    /// <summary>
    /// 成就分类
    /// </summary>
    public enum AchievementCategory
    {
        Basic,      // 基础通关
        Flawless,   // 无伤通关
        Speedrun,   // 速通
        BossKill,   // Boss击杀
        Cumulative, // 累计击杀
        Special,    // 特殊挑战
        Ultimate    // 终极成就
    }

    /// <summary>
    /// 成就奖励定义
    /// </summary>
    [Serializable]
    public class AchievementReward
    {
        public long cashReward;     // 现金奖励
        public int[] itemIds;       // 物品ID列表
        public int[] itemCounts;    // 物品数量列表

        public AchievementReward(long cash)
        {
            cashReward = cash;
            itemIds = null;
            itemCounts = null;
        }

        public AchievementReward(long cash, int[] items, int[] counts)
        {
            cashReward = cash;
            itemIds = items;
            itemCounts = counts;
        }
    }

    /// <summary>
    /// 成就定义
    /// </summary>
    [Serializable]
    public class BossRushAchievementDef
    {
        public string id;                       // 唯一标识
        public string nameCN;                   // 中文名称
        public string nameEN;                   // 英文名称
        public string descCN;                   // 中文描述
        public string descEN;                   // 英文描述
        public string iconFile;                 // 图标文件名
        public AchievementCategory category;    // 成就分类
        public AchievementReward reward;        // 成就奖励
        public bool isHidden;                   // 是否为隐藏成就
        public int difficultyRating;            // 难度评级（1-5星）

        public BossRushAchievementDef(
            string id,
            string nameCN,
            string nameEN,
            string descCN,
            string descEN,
            AchievementCategory category,
            long cashReward,
            int difficultyRating = 1,
            bool isHidden = false)
        {
            this.id = id;
            this.nameCN = nameCN;
            this.nameEN = nameEN;
            this.descCN = descCN;
            this.descEN = descEN;
            this.iconFile = id + ".png";
            this.category = category;
            this.reward = new AchievementReward(cashReward);
            this.difficultyRating = difficultyRating;
            this.isHidden = isHidden;
        }
    }
}
