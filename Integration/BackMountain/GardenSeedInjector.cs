// ============================================================================
// GardenSeedInjector.cs - 把后山作物注入官方种植系统
// ============================================================================
// 官方 CropDatabase 的两张表都是 public List，可以直接运行时追加，零 Harmony：
//   - `entries`（List<CropInfo>）：作物定义，resultNormal 指向收获得到的物品 TypeID
//   - `seedInfos`（List<SeedInfo>）：种子定义，itemTypeID 指向种子物品 TypeID
// 注入后官方种植 UI 会自动把我们的种子列进可选列表（GardenViewCropSelector 用
// CropDatabase.IsSeed 过滤玩家背包），完全不需要碰官方 UI。
//
// 【为什么不需要植物模型】
//   `Crop.RefreshDisplayInstance` 用的是 `ItemAssetsCollection.GetPrefab(resultNormal)`
//   的 ItemGraphic——**作物在地里长什么样，就是它产出物品的样子**。
//   所以只要食材物品注册好了，作物外观自动有了。这是整个菜地方案成本极低的根本原因。
//
// 【棘轮策略：一旦注入过，之后每次启动都注入】
//   `Crop.RefreshDisplayInstance` 对 GetPrefab 的结果**没有空检查**，
//   产物 TypeID 未注册时会在官方代码里 NRE。玩家一旦种下过我们的作物，
//   存档里就有了引用；此后若因为关开关而不再注入，读档时官方就会踩空。
//   因此解锁过一次之后，注入不再受开关回退影响——把破档面缩到「卸载整个 mod」。
//   （即使那样也不会崩：`Crop.Initialize` 找不到 CropInfo 会 LogError 后早退，
//   只是那个格子空着。）
//
// 【顺序硬约束】食材物品必须先于任何菜园场景加载完成注册，见上一条。
// EnsureInjected 内部保证了这个顺序：先注册物品，再注入作物表。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Crops;
using Duckov.Utilities;
using Saves;

namespace BossRush
{
    /// <summary>后山作物与种子的官方系统注入器。</summary>
    internal static class GardenSeedInjector
    {
        #region 常量

        /// <summary>
        /// 棘轮标记的存档键。一旦置位就代表「这个档里可能已经种下过 mod 作物」，
        /// 此后无条件注入。跨槽隔离由 SavesSystem 天然保证。
        /// </summary>
        private const string RatchetSaveKey = "BossRush_BackMountain_GardenRatchet_v1";

        /// <summary>作物成熟所需时间（现实分钟）。草案值，待 owner 审定。</summary>
        private const int GrowMinutes = 20;

        /// <summary>每次收获产出数量。</summary>
        private const int HarvestAmount = 2;

        #endregion

        #region 状态

        private static bool _injected;

        #endregion

        /// <summary>本会话是否已完成注入。</summary>
        internal static bool IsInjected { get { return _injected; } }

        #region 注入

        /// <summary>
        /// 幂等注入。菜地未解锁且棘轮未置位时跳过（dormant）。
        /// 每次进基地调一次即可——CropDatabase 是长寿 ScriptableObject，
        /// 但场景重载后本会话标记仍在，重复调用会在表里判重后早返。
        /// </summary>
        internal static void EnsureInjected()
        {
            try
            {
                if (_injected) return;

                bool unlocked = BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Garden);
                bool ratcheted = ReadRatchet();
                if (!unlocked && !ratcheted) return;

                // 顺序不能颠倒：作物外观与收获产物都指向食材物品，
                // 物品没注册好就注入作物表 = 官方 Crop 显示路径踩空 NRE
                if (!EnsureItemsRegistered()) return;

                CropDatabase database = GameplayDataSettings.CropDatabase;
                if (database == null)
                {
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "CropDatabase 尚不可用，稍后重试");
                    return;
                }

                InjectCrops(database);
                InjectSeeds(database);

                _injected = true;
                if (unlocked && !ratcheted) WriteRatchet();

                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "菜地作物已注入官方种植系统");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 作物注入失败: " + e.Message);
            }
        }

        /// <summary>确保六件物品都已注册。任一失败即整体放弃本次注入。</summary>
        private static bool EnsureItemsRegistered()
        {
            try
            {
                BackMountainItems.RegisterConfigurators();
                BackMountainItems.InjectLocalization();

                BackMountainItems.Definition[] all = BackMountainItems.Definitions;
                for (int i = 0; i < all.Length; i++)
                {
                    if (!BackMountainItems.EnsureRuntimeRegistration(all[i].TypeId))
                    {
                        ModBehaviour.DevLog(BackMountainConfig.LogPrefix
                            + "物品注册未完成，推迟作物注入: " + all[i].TypeId);
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 物品注册失败: " + e.Message);
                return false;
            }
        }

        private static void InjectCrops(CropDatabase database)
        {
            List<CropInfo> entries = database.entries;
            if (entries == null) return;

            BackMountainItems.Definition[] all = BackMountainItems.Definitions;
            for (int i = 0; i < all.Length; i++)
            {
                BackMountainItems.Definition def = all[i];
                if (!def.IsSeed) continue;

                int resultTypeId = BackMountainItems.GetHarvestResultFor(def.TypeId);
                if (resultTypeId <= 0) continue;

                string cropId = BuildCropId(def.TypeId);
                if (ContainsCrop(entries, cropId)) continue;

                CropInfo info = new CropInfo();
                info.id = cropId;
                info.resultPoor = resultTypeId;
                info.resultNormal = resultTypeId;
                info.resultGood = resultTypeId;
                info.resultAmount = HarvestAmount;
                info.totalGrowTicks = TimeSpan.FromMinutes(GrowMinutes).Ticks;

                entries.Add(info);
            }
        }

        private static void InjectSeeds(CropDatabase database)
        {
            List<SeedInfo> seeds = database.seedInfos;
            if (seeds == null) return;

            BackMountainItems.Definition[] all = BackMountainItems.Definitions;
            for (int i = 0; i < all.Length; i++)
            {
                BackMountainItems.Definition def = all[i];
                if (!def.IsSeed) continue;
                if (ContainsSeed(seeds, def.TypeId)) continue;

                SeedInfo seed = new SeedInfo();
                seed.itemTypeID = def.TypeId;
                seed.cropIDs = new RandomContainer<string>();
                // 一颗种子只对应一种作物，权重随便给个正数即可
                seed.cropIDs.AddEntry(BuildCropId(def.TypeId), 1f);

                seeds.Add(seed);
            }
        }

        private static bool ContainsCrop(List<CropInfo> entries, string cropId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].id, cropId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool ContainsSeed(List<SeedInfo> seeds, int typeId)
        {
            for (int i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].itemTypeID == typeId) return true;
            }
            return false;
        }

        /// <summary>种子 TypeID → 作物字符串 ID。冻结契约：进玩家存档的菜地格子。</summary>
        internal static string BuildCropId(int seedTypeId)
        {
            return BackMountainConfig.CropIdPrefix + seedTypeId;
        }

        #endregion

        #region 棘轮标记

        private static bool ReadRatchet()
        {
            try
            {
                if (!SavesSystem.KeyExisits(RatchetSaveKey)) return false;
                return SavesSystem.Load<bool>(RatchetSaveKey);
            }
            catch (Exception)
            {
                // 读不到就当没置位：注入与否由解锁状态决定，仍然安全
                return false;
            }
        }

        private static void WriteRatchet()
        {
            try
            {
                if (SavesSystem.IsSaving) return;
                SavesSystem.Save<bool>(RatchetSaveKey, true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 棘轮标记写入失败: " + e.Message);
            }
        }

        #endregion

        #region 清理

        /// <summary>
        /// 换槽时复位本会话标记：新槽的棘轮状态不同，必须重新判定。
        /// **不从官方表里摘条目**——摘掉会让另一个还在用它的槽读档踩空。
        /// </summary>
        internal static void NotifySlotChanged()
        {
            _injected = false;
        }

        internal static void ResetStaticCaches()
        {
            _injected = false;
        }

        #endregion
    }
}
