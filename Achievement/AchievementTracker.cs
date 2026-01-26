// ============================================================================
// AchievementTracker.cs - 运行时状态追踪
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using Saves;

namespace BossRush
{
    /// <summary>
    /// 成就状态追踪器 - 追踪本局和累计统计数据
    /// </summary>
    public static class AchievementTracker
    {
        #region 常量

        private const string SAVE_KEY_STATS = "BossRush_AchievementStats";
        private const float AUTO_SAVE_INTERVAL = 30f;

        #endregion

        #region 本局状态（每次进入竞技场重置）

        public static bool HasTakenDamage = false;              // 是否受到过伤害
        public static float ArenaEnterTime = 0f;                // 进入竞技场的时间戳
        public static bool HasPickedUpItem = false;             // 是否拾取过装备（Mode D专用）
        public static bool HasUsedHealItem = false;             // 是否使用过治疗物品
        public static bool DragonKingKilledFlawless = true;     // 击杀龙王时是否无伤
        public static bool KilledDragonDescendant = false;      // 是否击杀过龙裔遗族
        public static bool KilledDragonKing = false;            // 是否击杀过龙王

        #endregion

        #region 持久化统计（存档保存）

        public static int TotalBossKills = 0;       // 累计击杀Boss数量
        public static int TotalClears = 0;          // 累计通关次数
        public static int TotalDragonKingKills = 0; // 累计击杀龙王次数
        public static int MaxInfiniteHellWave = 0;  // 最高无间炼狱波次

        #endregion

        #region 私有字段

        private static bool isInitialized = false;
        private static float lastSaveTime = 0f;
        private static int lastSavedBossKills = 0;
        private static int lastSavedClears = 0;

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化（加载持久化统计）
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            LoadStats();
            isInitialized = true;
            Debug.Log("[Achievement] 追踪器初始化完成 - 累计击杀: " + TotalBossKills + ", 累计通关: " + TotalClears);
        }

        /// <summary>
        /// 重置本局状态（进入竞技场时调用）
        /// </summary>
        public static void ResetSessionStats()
        {
            HasTakenDamage = false;
            ArenaEnterTime = Time.time;
            HasPickedUpItem = false;
            HasUsedHealItem = false;
            DragonKingKilledFlawless = true;
            KilledDragonDescendant = false;
            KilledDragonKing = false;
        }

        #endregion

        #region 事件记录

        /// <summary>
        /// 记录玩家受伤
        /// </summary>
        public static void OnPlayerTakeDamage(float damage)
        {
            if (damage > 0)
            {
                HasTakenDamage = true;
                DragonKingKilledFlawless = false;
            }
        }

        /// <summary>
        /// 记录玩家拾取物品
        /// </summary>
        public static void OnPlayerPickupItem()
        {
            HasPickedUpItem = true;
        }

        /// <summary>
        /// 记录玩家使用治疗物品
        /// </summary>
        public static void OnPlayerUseHealItem()
        {
            HasUsedHealItem = true;
        }

        /// <summary>
        /// 记录Boss击杀
        /// </summary>
        public static void OnBossKilled(string bossType)
        {
            TotalBossKills++;

            if (bossType == "DragonDescendant")
                KilledDragonDescendant = true;
            else if (bossType == "DragonKing")
            {
                KilledDragonKing = true;
                TotalDragonKingKills++;
            }

            TryAutoSave();
        }

        /// <summary>
        /// 记录无间炼狱波次
        /// </summary>
        public static void OnInfiniteHellWaveComplete(int waveNumber)
        {
            if (waveNumber > MaxInfiniteHellWave)
            {
                MaxInfiniteHellWave = waveNumber;
                TryAutoSave();
            }
        }

        /// <summary>
        /// 记录通关
        /// </summary>
        public static void OnClear()
        {
            TotalClears++;
            SaveStats();
        }

        /// <summary>
        /// 尝试自动保存（定期保存，避免每次击杀都保存）
        /// </summary>
        public static void TryAutoSave()
        {
            float currentTime = Time.realtimeSinceStartup;
            bool hasChanges = (TotalBossKills != lastSavedBossKills) || (TotalClears != lastSavedClears);
            
            if (hasChanges && (currentTime - lastSaveTime >= AUTO_SAVE_INTERVAL))
            {
                SaveStats();
                lastSaveTime = currentTime;
                lastSavedBossKills = TotalBossKills;
                lastSavedClears = TotalClears;
            }
        }

        /// <summary>
        /// 强制保存（用于退出游戏或离开BossRush时调用）
        /// </summary>
        public static void ForceSave()
        {
            if ((TotalBossKills != lastSavedBossKills) || (TotalClears != lastSavedClears))
            {
                SaveStats();
                lastSavedBossKills = TotalBossKills;
                lastSavedClears = TotalClears;
            }
        }

        /// <summary>
        /// 重置所有统计数据（用于调试）
        /// </summary>
        public static void ResetAllStats()
        {
            TotalBossKills = 0;
            TotalClears = 0;
            TotalDragonKingKills = 0;
            MaxInfiniteHellWave = 0;
            lastSavedBossKills = 0;
            lastSavedClears = 0;
            SaveStats();
            Debug.Log("[Achievement] 统计数据已重置");
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 获取本局已用时间（秒）
        /// </summary>
        public static float GetElapsedTime()
        {
            return Time.time - ArenaEnterTime;
        }

        #endregion

        #region 存档

        /// <summary>
        /// 保存持久化统计
        /// </summary>
        public static void SaveStats()
        {
            try
            {
                var stats = new Dictionary<string, int>
                {
                    { "TotalBossKills", TotalBossKills },
                    { "TotalClears", TotalClears },
                    { "TotalDragonKingKills", TotalDragonKingKills },
                    { "MaxInfiniteHellWave", MaxInfiniteHellWave }
                };
                SavesSystem.SaveGlobal(SAVE_KEY_STATS, stats);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Achievement] 保存统计失败: " + e.Message);
            }
        }

        /// <summary>
        /// 加载持久化统计
        /// </summary>
        public static void LoadStats()
        {
            try
            {
                var stats = SavesSystem.LoadGlobal<Dictionary<string, int>>(
                    SAVE_KEY_STATS,
                    new Dictionary<string, int>()
                );

                TotalBossKills = stats.ContainsKey("TotalBossKills") ? stats["TotalBossKills"] : 0;
                TotalClears = stats.ContainsKey("TotalClears") ? stats["TotalClears"] : 0;
                TotalDragonKingKills = stats.ContainsKey("TotalDragonKingKills") ? stats["TotalDragonKingKills"] : 0;
                MaxInfiniteHellWave = stats.ContainsKey("MaxInfiniteHellWave") ? stats["MaxInfiniteHellWave"] : 0;
            }
            catch
            {
                TotalBossKills = 0;
                TotalClears = 0;
                TotalDragonKingKills = 0;
                MaxInfiniteHellWave = 0;
            }
        }

        #endregion
    }
}
