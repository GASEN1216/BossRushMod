// ============================================================================
// PetNestMuseumStats.cs - 遗种博物馆图鉴统计（实施计划 步骤 12）
// ============================================================================
// 统计四件事：击杀 / 孵化 / 异色 / 远征。
//
// 硬约束（tests/PetNestMuseumStatsGuard.py 守卫）：
//   - **按角色实例去重**：同一只 Boss 的死亡事件可能被多条路径看到
//     （掉落 handler + 死亡补丁 + 波次统计），不去重会把一次击杀记成三次。
//     去重用 HashSet<CharacterMainControl> 引用集合，照
//     Achievement/AchievementTriggers.cs 的 achievementCountedBossKills 先例；
//   - 去重集合必须在切图 / run 结束时清空，否则跨局累积死引用；
//   - 首次孵化解锁该血脉图鉴页；
//   - 高频写只入队（StageCommit），不逐次落盘。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>遗种博物馆统计。所有计数的唯一写入口。</summary>
    internal static class PetNestMuseumStats
    {
        #region 实例去重

        /// <summary>
        /// 本局已计过击杀的 Boss 实例。引用集合，O(1) 去重。
        /// 切图 / run 结束必须 Clear，否则跨局累积死引用。
        /// </summary>
        private static readonly HashSet<CharacterMainControl> _countedBossKills =
            new HashSet<CharacterMainControl>();

        /// <summary>清空本局去重集合（切图 / run 结束 / 宿主销毁）。</summary>
        internal static void ClearCountedKills()
        {
            _countedBossKills.Clear();
        }

        #endregion

        #region 统计写入

        /// <summary>
        /// 记一次血脉击杀。同一角色实例只记一次。
        /// 只入队不落盘：一局几十次击杀，逐次 SaveFile 会拖帧。
        /// </summary>
        internal static void RecordKill(CharacterMainControl boss, string lineageKey)
        {
            if (boss == null || string.IsNullOrEmpty(lineageKey)) return;
            try
            {
                if (_countedBossKills.Contains(boss)) return;
                _countedBossKills.Add(boss);

                PetNestLineageStats stats = GetOrCreate(lineageKey);
                if (stats == null) return;
                stats.kills++;
                PetNestPersistenceAccess.StageMuseum();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 击杀统计失败: " + e.Message);
            }
        }

        /// <summary>记一次孵化。首次孵化解锁该血脉图鉴页。</summary>
        internal static void RecordHatch(PetNestPetRecord pet)
        {
            if (pet == null || string.IsNullOrEmpty(pet.lineageKey)) return;
            try
            {
                PetNestLineageStats stats = GetOrCreate(pet.lineageKey);
                if (stats == null) return;
                stats.hatched++;
                if (pet.shiny) stats.shinyHatched++;
                // 首次孵化解锁图鉴页
                stats.unlocked = true;
                if (pet.level > stats.maxLevel) stats.maxLevel = pet.level;
                PetNestPersistenceAccess.StageMuseum();
                CheckTamingAchievements();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 孵化统计失败: " + e.Message);
            }
        }

        /// <summary>记一次远征出发。</summary>
        internal static void RecordExpedition(PetNestPetRecord pet)
        {
            if (pet == null || string.IsNullOrEmpty(pet.lineageKey)) return;
            try
            {
                PetNestLineageStats stats = GetOrCreate(pet.lineageKey);
                if (stats == null) return;
                stats.expeditions++;
                PetNestPersistenceAccess.StageMuseum();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 远征统计失败: " + e.Message);
            }
        }

        /// <summary>记录该血脉的最高养成等级。</summary>
        internal static void RecordLevel(PetNestPetRecord pet)
        {
            if (pet == null || string.IsNullOrEmpty(pet.lineageKey)) return;
            try
            {
                PetNestLineageStats stats = GetOrCreate(pet.lineageKey);
                if (stats == null || pet.level <= stats.maxLevel) return;
                stats.maxLevel = pet.level;
                PetNestPersistenceAccess.StageMuseum();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 等级统计失败: " + e.Message);
            }
        }

        #endregion

        #region 驯养成就

        /// <summary>
        /// 驯养成就检查。每次统计变化后调一次；TryUnlock 自身幂等，
        /// 重复调用不会重复发奖。
        /// </summary>
        private static void CheckTamingAchievements()
        {
            try
            {
                if (UnlockedLineageCount >= 1)
                {
                    BossRushAchievementManager.TryUnlock("petnest_first_hatch");
                }
                if (UnlockedLineageCount >= 10)
                {
                    BossRushAchievementManager.TryUnlock("petnest_lineage_10");
                }
                if (UnlockedLineageCount >= 30)
                {
                    BossRushAchievementManager.TryUnlock("petnest_lineage_30");
                }
                if (ShinyCount >= 1)
                {
                    BossRushAchievementManager.TryUnlock("petnest_shiny");
                }
                if (MemorialCount >= 1)
                {
                    BossRushAchievementManager.TryUnlock("petnest_memorial");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 驯养成就检查失败: " + e.Message);
            }
        }

        /// <summary>外部（纪念碑刻档后）触发一次成就检查。</summary>
        internal static void NotifyMemorialChanged()
        {
            CheckTamingAchievements();
        }

        #endregion

        #region 查询

        /// <summary>已解锁的血脉数（驯养成就的判据）。</summary>
        internal static int UnlockedLineageCount
        {
            get
            {
                try
                {
                    List<PetNestLineageStats> lineages = PetNestPersistenceAccess.Museum.lineages;
                    int count = 0;
                    for (int i = 0; i < lineages.Count; i++)
                    {
                        if (lineages[i] != null && lineages[i].unlocked) count++;
                    }
                    return count;
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        /// <summary>异色累计获得数。</summary>
        internal static int ShinyCount
        {
            get
            {
                try
                {
                    List<PetNestLineageStats> lineages = PetNestPersistenceAccess.Museum.lineages;
                    int count = 0;
                    for (int i = 0; i < lineages.Count; i++)
                    {
                        if (lineages[i] != null) count += lineages[i].shinyHatched;
                    }
                    return count;
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        /// <summary>纪念碑上的名字数（含碑林计数）。</summary>
        internal static int MemorialCount
        {
            get
            {
                try
                {
                    PetNestMuseumData museum = PetNestPersistenceAccess.Museum;
                    return museum.memorials.Count + museum.mergedMemorialCount;
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        /// <summary>按血脉查统计。查不到返回 null。</summary>
        internal static PetNestLineageStats TryGet(string lineageKey)
        {
            if (string.IsNullOrEmpty(lineageKey)) return null;
            try
            {
                List<PetNestLineageStats> lineages = PetNestPersistenceAccess.Museum.lineages;
                for (int i = 0; i < lineages.Count; i++)
                {
                    PetNestLineageStats stats = lineages[i];
                    if (stats != null && string.Equals(stats.lineageKey, lineageKey, StringComparison.Ordinal))
                    {
                        return stats;
                    }
                }
            }
            catch (Exception)
            {
                // 查询失败按"没有统计"处理
            }
            return null;
        }

        #endregion

        #region 内部

        private static PetNestLineageStats GetOrCreate(string lineageKey)
        {
            PetNestLineageStats stats = TryGet(lineageKey);
            if (stats != null) return stats;
            try
            {
                stats = new PetNestLineageStats();
                stats.lineageKey = lineageKey;
                PetNestPersistenceAccess.Museum.lineages.Add(stats);
                return stats;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 创建血脉统计失败: " + e.Message);
                return null;
            }
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            ClearCountedKills();
        }

        #endregion
    }
}
