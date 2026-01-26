// ============================================================================
// BossRushAchievementManager.cs - 成就管理器
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Saves;

namespace BossRush
{
    /// <summary>
    /// BossRush 成就管理器 - 管理成就定义、解锁状态和奖励
    /// </summary>
    public static class BossRushAchievementManager
    {
        #region 常量

        private const string SAVE_KEY_UNLOCKED = "BossRush_Achievements";
        private const string SAVE_KEY_CLAIMED = "BossRush_AchievementRewards";

        #endregion

        #region 私有字段

        private static Dictionary<string, BossRushAchievementDef> allAchievements = new Dictionary<string, BossRushAchievementDef>();
        private static HashSet<string> unlockedAchievements = new HashSet<string>();
        private static HashSet<string> claimedRewards = new HashSet<string>();
        private static bool isInitialized = false;

        #endregion

        #region 事件

        public static event Action<BossRushAchievementDef> OnAchievementUnlocked;
        public static event Action<BossRushAchievementDef> OnRewardClaimed;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化成就系统
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized)
            {
                Debug.Log("[Achievement] 成就系统已初始化，跳过");
                return;
            }

            RegisterAllAchievements();
            LoadData();
            AchievementTracker.Initialize();

            isInitialized = true;
            Debug.Log($"[Achievement] 成就系统初始化完成 - 共 {allAchievements.Count} 个成就，已解锁 {unlockedAchievements.Count} 个");
        }

        /// <summary>
        /// 注册所有成就定义
        /// </summary>
        private static void RegisterAllAchievements()
        {
            allAchievements.Clear();

            // 基础通关成就
            Register("first_clear", "初出茅庐", "First Steps",
                "首次在 BossRush 中成功撤离", "Complete BossRush for the first time",
                AchievementCategory.Basic, 1000, 1);

            Register("easy_clear", "弹指可灭", "Easy Peasy",
                "在[弹指可灭]难度成功撤离", "Complete BossRush on Easy difficulty",
                AchievementCategory.Basic, 2000, 1);

            Register("normal_clear", "有点意思", "Getting Serious",
                "在[有点意思]难度成功撤离", "Complete BossRush on Normal difficulty",
                AchievementCategory.Basic, 3000, 2);

            Register("hell_10", "无间炼狱·十", "Hell Wave 10",
                "在无间炼狱模式完成10波后撤离", "Survive 10 waves in Infinite Hell mode",
                AchievementCategory.Basic, 5000, 3);

            Register("hell_100", "无间炼狱·百", "Hell Wave 100",
                "在无间炼狱模式完成100波后撤离", "Survive 100 waves in Infinite Hell mode",
                AchievementCategory.Basic, 10000, 5);

            Register("mode_d_clear", "白手起家", "From Nothing",
                "在白手起家模式成功撤离", "Complete Mode D (start with nothing)",
                AchievementCategory.Basic, 3000, 2);

            // 无伤通关成就
            Register("flawless_easy", "完美弹指", "Flawless Easy",
                "在[弹指可灭]难度无伤通关", "Complete Easy difficulty without taking damage",
                AchievementCategory.Flawless, 5000, 3);

            Register("flawless_normal", "完美有点意思", "Flawless Normal",
                "在[有点意思]难度无伤通关", "Complete Normal difficulty without taking damage",
                AchievementCategory.Flawless, 8000, 4);

            // 速通成就
            Register("speedrun_5min", "闪电战", "Lightning Run",
                "5分钟内完成任意模式", "Complete any mode within 5 minutes",
                AchievementCategory.Speedrun, 4000, 2);

            Register("speedrun_3min", "光速通关", "Speed Demon",
                "3分钟内完成任意模式", "Complete any mode within 3 minutes",
                AchievementCategory.Speedrun, 8000, 4);

            // Boss击杀成就
            Register("kill_dragon_descendant", "龙裔猎手", "Dragon Slayer",
                "首次击杀龙裔遗族", "Defeat the Dragon Descendant for the first time",
                AchievementCategory.BossKill, 3000, 2);

            Register("kill_dragon_king", "弑龙者", "Kingslayer",
                "首次击杀焚天龙皇", "Defeat the Dragon King for the first time",
                AchievementCategory.BossKill, 6000, 3);

            Register("kill_dragon_king_flawless", "完美弑龙", "Perfect Kingslayer",
                "无伤击杀焚天龙皇", "Defeat the Dragon King without taking damage",
                AchievementCategory.BossKill, 15000, 5);

            // 累计击杀成就
            Register("kill_100_bosses", "百战老兵", "Veteran",
                "累计击杀100个Boss", "Defeat 100 bosses in total",
                AchievementCategory.Cumulative, 5000, 2);

            Register("kill_500_bosses", "屠龙勇士", "Dragon Hunter",
                "累计击杀500个Boss", "Defeat 500 bosses in total",
                AchievementCategory.Cumulative, 20000, 4);

            // 特殊挑战成就（隐藏）
            Register("mode_d_no_pickup", "极简主义", "Minimalist",
                "Mode D中不拾取任何装备通关", "Complete Mode D without picking up any equipment",
                AchievementCategory.Special, 10000, 4, true);

            Register("hell_no_heal", "铁人挑战", "Iron Man",
                "无间炼狱中不使用治疗物品完成10波", "Survive 10 waves in Infinite Hell without using healing items",
                AchievementCategory.Special, 8000, 4, true);

            Debug.Log($"[Achievement] 已注册 {allAchievements.Count} 个成就定义");
        }

        /// <summary>
        /// 注册单个成就（简化方法）
        /// </summary>
        private static void Register(string id, string nameCN, string nameEN, string descCN, string descEN,
            AchievementCategory category, long cashReward, int difficulty, bool isHidden = false)
        {
            if (allAchievements.ContainsKey(id))
            {
                Debug.LogWarning($"[Achievement] 成就ID重复: {id}");
                return;
            }
            allAchievements[id] = new BossRushAchievementDef(id, nameCN, nameEN, descCN, descEN, category, cashReward, difficulty, isHidden);
        }

        #endregion

        #region 核心功能

        /// <summary>
        /// 尝试解锁成就
        /// </summary>
        public static bool TryUnlock(string achievementId)
        {
            if (!isInitialized) return false;
            if (!allAchievements.TryGetValue(achievementId, out var achievement)) return false;
            if (unlockedAchievements.Contains(achievementId)) return false;

            unlockedAchievements.Add(achievementId);
            SaveData();

            Debug.Log($"[Achievement] 成就解锁: {achievement.nameCN} ({achievementId})");

            OnAchievementUnlocked?.Invoke(achievement);
            SteamAchievementPopup.Show(achievement);

            return true;
        }

        /// <summary>
        /// 检查成就是否已解锁
        /// </summary>
        public static bool IsUnlocked(string achievementId)
        {
            return unlockedAchievements.Contains(achievementId);
        }

        /// <summary>
        /// 检查奖励是否已领取
        /// </summary>
        public static bool IsRewardClaimed(string achievementId)
        {
            return claimedRewards.Contains(achievementId);
        }

        /// <summary>
        /// 领取成就奖励
        /// </summary>
        public static bool ClaimReward(string achievementId)
        {
            if (!isInitialized) return false;
            if (!allAchievements.TryGetValue(achievementId, out var achievement)) return false;
            if (!unlockedAchievements.Contains(achievementId)) return false;
            if (claimedRewards.Contains(achievementId)) return false;

            // 发放现金奖励
            if (achievement.reward != null && achievement.reward.cashReward > 0)
            {
                try
                {
                    Duckov.Economy.EconomyManager.Add(achievement.reward.cashReward);
                    Debug.Log($"[Achievement] 发放现金奖励: {achievement.reward.cashReward}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Achievement] 发放现金奖励失败: {e.Message}");
                }
            }

            // 发放物品奖励
            if (achievement.reward?.itemIds != null)
            {
                for (int i = 0; i < achievement.reward.itemIds.Length; i++)
                {
                    int itemId = achievement.reward.itemIds[i];
                    int count = (achievement.reward.itemCounts != null && i < achievement.reward.itemCounts.Length)
                        ? achievement.reward.itemCounts[i] : 1;
                    Debug.Log($"[Achievement] 发放物品奖励: ID={itemId}, 数量={count}");
                }
            }

            claimedRewards.Add(achievementId);
            SaveData();

            OnRewardClaimed?.Invoke(achievement);
            return true;
        }

        #endregion

        #region 查询功能

        public static List<BossRushAchievementDef> GetAllAchievements() => allAchievements.Values.ToList();

        public static List<BossRushAchievementDef> GetAchievementsByCategory(AchievementCategory category)
            => allAchievements.Values.Where(a => a.category == category).ToList();

        public static BossRushAchievementDef GetAchievement(string achievementId)
            => allAchievements.TryGetValue(achievementId, out var def) ? def : null;

        public static List<string> GetUnlockedAchievementIds() => unlockedAchievements.ToList();

        public static (int unlocked, int total) GetStats() => (unlockedAchievements.Count, allAchievements.Count);

        public static long GetTotalRewardCash() => allAchievements.Values.Sum(a => a.reward?.cashReward ?? 0);

        public static long GetClaimedRewardCash() => claimedRewards
            .Where(id => allAchievements.ContainsKey(id))
            .Sum(id => allAchievements[id].reward?.cashReward ?? 0);

        #endregion

        #region 存档功能

        public static void SaveData()
        {
            try
            {
                SavesSystem.SaveGlobal(SAVE_KEY_UNLOCKED, unlockedAchievements.ToList());
                SavesSystem.SaveGlobal(SAVE_KEY_CLAIMED, claimedRewards.ToList());
            }
            catch (Exception e)
            {
                Debug.LogError($"[Achievement] 保存数据失败: {e.Message}");
            }
        }

        public static void LoadData()
        {
            try
            {
                var unlockedList = SavesSystem.LoadGlobal<List<string>>(SAVE_KEY_UNLOCKED, new List<string>());
                unlockedAchievements = new HashSet<string>(unlockedList);

                var claimedList = SavesSystem.LoadGlobal<List<string>>(SAVE_KEY_CLAIMED, new List<string>());
                claimedRewards = new HashSet<string>(claimedList);

                Debug.Log($"[Achievement] 数据已加载 - 解锁: {unlockedAchievements.Count}, 领取: {claimedRewards.Count}");
            }
            catch
            {
                unlockedAchievements = new HashSet<string>();
                claimedRewards = new HashSet<string>();
            }
        }

        #endregion

        #region 调试功能

        public static void DebugUnlockAll()
        {
            foreach (var id in allAchievements.Keys)
                unlockedAchievements.Add(id);
            SaveData();
            Debug.Log("[Achievement] [DEBUG] 已解锁所有成就");
        }

        public static void DebugResetAll()
        {
            unlockedAchievements.Clear();
            claimedRewards.Clear();
            SaveData();
            AchievementTracker.TotalBossKills = 0;
            AchievementTracker.TotalClears = 0;
            AchievementTracker.SaveStats();
            Debug.Log("[Achievement] [DEBUG] 已重置所有成就");
        }

        #endregion
    }
}
