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
//
// 【起步种子（2026-09-23 owner 实测第 15 条）】菜地开放后第一次在基地就绪时，每种种子送 2 颗进背包，
//   让玩家建好菜地就能种；每个存档槽只送一次（可选存档键，旧档没有这个键 = 还没送，SCHEMA+）。
//   先写标记并回读核对、再发物品：写不进去就不发（下次再试），发放中途出错也不会重复送。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Crops;
using Duckov.Utilities;
using ItemStatsSystem;
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

        /// <summary>作物成熟所需时间（现实分钟），保留既有平衡。</summary>
        private const int GrowMinutes = 20;

        /// <summary>每次收获产出数量。</summary>
        private const int HarvestAmount = 2;

        /// <summary>起步种子已发放的存档键（按槽，可选；缺键 = 未发放）。</summary>
        internal const string StarterSeedsSaveKey = "BossRush_BackMountain_StarterSeeds_v1";

        /// <summary>起步种子每种几颗。</summary>
        internal const int StarterSeedsPerType = 2;

        #endregion

        #region 状态

        private static bool _injected;

        /// <summary>起步种子发放后排队的一条飘字；由运行时模块在对话结束后与工地飘字一起取走。</summary>
        internal static string PendingStarterNotice;

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
                if (database == null || database.entries == null || database.seedInfos == null)
                {
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "CropDatabase 尚不可用，稍后重试");
                    return;
                }

                // 先保证下次启动能恢复这些作物，再把可种条目交给官方 UI。
                if (unlocked && !ratcheted && !WriteRatchet()) return;
                InjectCrops(database);
                InjectSeeds(database);
                _injected = true;

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
            return ReadFlag(RatchetSaveKey);
        }

        private static bool WriteRatchet()
        {
            return WriteFlag(RatchetSaveKey, "棘轮标记");
        }

        private static bool ReadFlag(string key)
        {
            try
            {
                if (!SavesSystem.KeyExisits(key)) return false;
                return SavesSystem.Load<bool>(key);
            }
            catch (Exception)
            {
                // 读不到就当没置位：棘轮由解锁状态兜底；起步种子则因写前回读核对而不会重复发
                return false;
            }
        }

        private static bool WriteFlag(string key, string label)
        {
            try
            {
                if (SavesSystem.IsSaving || SavesSystem.CurrentSlot < 0) return false;
                SavesSystem.Save<bool>(key, true);
                return SavesSystem.Load<bool>(key);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] " + label + "写入失败: " + e.Message);
                return false;
            }
        }

        /// <summary>撤回一个未经证实 / 未兑现的标记（尽力而为；撤不回就宁可不发，也不重复发）。</summary>
        private static void ClearFlag(string key)
        {
            try
            {
                if (SavesSystem.IsSaving || SavesSystem.CurrentSlot < 0 || !SavesSystem.KeyExisits(key)) return;
                SavesSystem.Save<bool>(key, false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 撤回标记失败: " + key + ", " + e.Message);
            }
        }

        /// <summary>
        /// 菜地是否对这个槽开放：现查征程解锁，或本槽写过棘轮（征程解锁表还没读进来时靠它）。
        /// 基地售货机的种子条目按它挂。
        /// </summary>
        internal static bool IsGardenAvailable()
        {
            return BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Garden) || ReadRatchet();
        }

        #endregion

        #region 起步种子

        /// <summary>
        /// 菜地开放、作物已注入、主角已在基地就绪时，每个槽送一次起步种子（三种各 StarterSeedsPerType 颗）进背包。
        /// 事件驱动（场景 / 关卡就绪 / 实时解锁），不在 tick 里轮询。已送过的槽读一次存档键即返回。
        /// </summary>
        /// <returns>本次是否发放了（至少一种）种子。</returns>
        internal static bool TryGrantStarterSeeds(bool playerReadyInBase)
        {
            try
            {
                if (!_injected || !playerReadyInBase) return false;
                if (ReadFlag(StarterSeedsSaveKey)) return false;
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.CharacterItem == null) return false;

                // 先落标记再发：写不进去就这次不发（下次就绪时重试），绝不重复送。
                if (!WriteFlag(StarterSeedsSaveKey, "起步种子标记"))
                {
                    ClearFlag(StarterSeedsSaveKey);
                    return false;
                }

                List<string> namesCN = new List<string>();
                List<string> namesEN = new List<string>();
                BackMountainItems.Definition[] all = BackMountainItems.Definitions;
                for (int i = 0; i < all.Length; i++)
                {
                    BackMountainItems.Definition def = all[i];
                    if (!def.IsSeed) continue;
                    if (TryGiveStarterSeed(def.TypeId, StarterSeedsPerType))
                    {
                        namesCN.Add(def.NameCN + " ×" + StarterSeedsPerType);
                        namesEN.Add(def.NameEN + " ×" + StarterSeedsPerType);
                    }
                }

                if (namesCN.Count <= 0)
                {
                    // 一颗都没送出去：撤回标记，下次就绪时再试（送出去的判据见 TryGiveStarterSeed，不会重复送）
                    ClearFlag(StarterSeedsSaveKey);
                    return false;
                }
                PendingStarterNotice = L10n.T(
                    "菜地起步种子已放进背包：" + string.Join("、", namesCN.ToArray()) + "。用完了去基地售货机买，龙裔遗族、焚天龙皇、幽灵女巫也会掉。",
                    "Starter garden seeds are in your backpack: " + string.Join(", ", namesEN.ToArray())
                    + ". Buy more from the base vendor; the Dragon Descendant, Dragon King and Phantom Witch also drop them.");
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "已发放菜地起步种子: " + namesEN.Count + " 种");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 发放起步种子失败: " + e.Message);
                return false;
            }
        }

        private static bool TryGiveStarterSeed(int typeId, int count)
        {
            Item seed = null;
            try
            {
                if (!BackMountainItems.EnsureRuntimeRegistration(typeId)) return false;
                seed = ItemAssetsCollection.InstantiateSync(typeId);
                if (seed == null) return false;
                seed.StackCount = count;
                // 进背包，放不下走官方仓库 / 快递（基地里仓库一定在）
                ItemUtilities.SendToPlayer(seed, false, true);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 起步种子 " + typeId + " 发放失败: " + e.Message);
                if (seed == null) return false;
                // 官方交付之后的回调抛错时物品其实已经到手：按已送出算，绝不销毁
                if (seed.InInventory != null) return true;
                try { seed.DestroyTree(); }
                catch (Exception destroyError)
                {
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 回收未送出的起步种子失败: " + destroyError.Message);
                }
                return false;
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
            PendingStarterNotice = null;
        }

        #endregion
    }
}
