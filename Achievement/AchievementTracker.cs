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
        
        // ========== 收藏类成就追踪 ==========
        public static HashSet<int> CollectedDragonDescendantLoot = new HashSet<int>(); // 已收集的龙裔掉落物
        public static HashSet<int> CollectedDragonKingLoot = new HashSet<int>();       // 已收集的龙王掉落物
        
        // ========== 特殊成就追踪 ==========
        public static bool HasUsedFlightTotem = false;        // 是否使用过腾云驾雾图腾
        public static bool HasTriggeredReverseScale = false;  // 是否触发过逆鳞效果

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
            // 注意：收藏类和特殊成就追踪不重置，因为它们是累计的
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
            CollectedDragonDescendantLoot.Clear();
            CollectedDragonKingLoot.Clear();
            HasUsedFlightTotem = false;
            HasTriggeredReverseScale = false;
            lastSavedBossKills = 0;
            lastSavedClears = 0;
            SaveStats();
            Debug.Log("[Achievement] 统计数据已重置");
        }
        
        /// <summary>
        /// 记录收集龙裔掉落物
        /// </summary>
        public static void OnCollectDragonDescendantLoot(int itemTypeId)
        {
            if (CollectedDragonDescendantLoot.Add(itemTypeId))
            {
                Debug.Log("[Achievement] 收集龙裔掉落物: TypeID=" + itemTypeId + ", 已收集数量=" + CollectedDragonDescendantLoot.Count);
                TryAutoSave();
            }
        }
        
        /// <summary>
        /// 记录收集龙王掉落物
        /// </summary>
        public static void OnCollectDragonKingLoot(int itemTypeId)
        {
            if (CollectedDragonKingLoot.Add(itemTypeId))
            {
                Debug.Log("[Achievement] 收集龙王掉落物: TypeID=" + itemTypeId + ", 已收集数量=" + CollectedDragonKingLoot.Count);
                TryAutoSave();
            }
        }
        
        /// <summary>
        /// 记录使用腾云驾雾图腾
        /// </summary>
        public static void OnUseFlightTotem()
        {
            if (!HasUsedFlightTotem)
            {
                HasUsedFlightTotem = true;
                Debug.Log("[Achievement] 首次使用腾云驾雾图腾");
                SaveStats();
            }
        }
        
        /// <summary>
        /// 记录触发逆鳞效果
        /// </summary>
        public static void OnTriggerReverseScale()
        {
            if (!HasTriggeredReverseScale)
            {
                HasTriggeredReverseScale = true;
                Debug.Log("[Achievement] 首次触发逆鳞效果");
                SaveStats();
            }
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
                    { "MaxInfiniteHellWave", MaxInfiniteHellWave },
                    { "HasUsedFlightTotem", HasUsedFlightTotem ? 1 : 0 },
                    { "HasTriggeredReverseScale", HasTriggeredReverseScale ? 1 : 0 }
                };
                SavesSystem.SaveGlobal(SAVE_KEY_STATS, stats);
                
                // 保存收藏类数据
                SavesSystem.SaveGlobal(SAVE_KEY_STATS + "_DragonDescendantLoot", new List<int>(CollectedDragonDescendantLoot));
                SavesSystem.SaveGlobal(SAVE_KEY_STATS + "_DragonKingLoot", new List<int>(CollectedDragonKingLoot));
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
                HasUsedFlightTotem = stats.ContainsKey("HasUsedFlightTotem") && stats["HasUsedFlightTotem"] == 1;
                HasTriggeredReverseScale = stats.ContainsKey("HasTriggeredReverseScale") && stats["HasTriggeredReverseScale"] == 1;
                
                // 加载收藏类数据
                try
                {
                    var ddLoot = SavesSystem.LoadGlobal<List<int>>(SAVE_KEY_STATS + "_DragonDescendantLoot", new List<int>());
                    CollectedDragonDescendantLoot = new HashSet<int>(ddLoot);
                }
                catch { CollectedDragonDescendantLoot = new HashSet<int>(); }
                
                try
                {
                    var dkLoot = SavesSystem.LoadGlobal<List<int>>(SAVE_KEY_STATS + "_DragonKingLoot", new List<int>());
                    CollectedDragonKingLoot = new HashSet<int>(dkLoot);
                }
                catch { CollectedDragonKingLoot = new HashSet<int>(); }
            }
            catch
            {
                TotalBossKills = 0;
                TotalClears = 0;
                TotalDragonKingKills = 0;
                MaxInfiniteHellWave = 0;
                HasUsedFlightTotem = false;
                HasTriggeredReverseScale = false;
                CollectedDragonDescendantLoot = new HashSet<int>();
                CollectedDragonKingLoot = new HashSet<int>();
            }
        }

        #endregion
    }
}
