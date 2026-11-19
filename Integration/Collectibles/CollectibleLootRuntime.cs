// ============================================================================
// CollectibleLootRuntime.cs - 开箱时判一次、额外多一格放收集品
// ============================================================================
// 接点是官方静态事件 InteractableLootbox.OnStartLoot（开箱读条结束、搜刮界面打开的那一刻），不加 Harmony。
// 物品必须同步加进库存：进入搜索状态后，找不到未搜物品官方会立刻结束交互（InteractableLootbox.OnUpdate）。
// 新物品 Inspected = false，玩家要像原版一样把它搜出来；搜索时长官方按 Quality 查表。
//
// 哪些箱子参与（owner 2026-11-19）：
//   - 官方出击图的场景箱：有 LootBoxLoader，且库存挂在 LevelManager.LootBoxInventoriesParent 下
//     （官方 GetOrCreateInventory 的产物）。尸体箱、墓碑、空投、奖励箱、快递箱和送礼容器用的都是
//     挂在箱子自身上的本地库存，靠这一条就排除了。再按箱子名键排除宠物背包、快递箱、爱心箱和任务箱。
//   - 自研三图的场景箱：由各图自己的「第一次开箱」回调调 NotifyModBoxFirstOpened，带上档次与品质上限。
//   - 不参与：基地（仓库）、BossRush 各模式、丧尸模式、鸭王杯、征程竞技场。
// 每只箱子只判一次（官方 hasBeenInspectedInLootBox + 本趟已判集合），每趟最多 3 件。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed class CollectibleLootRuntime : IDisposable
    {
        private static CollectibleLootRuntime current;

        private readonly ModBehaviour owner;
        private readonly CollectibleRaidLedger ledger = new CollectibleRaidLedger();
        private LevelManager raidOwner;
        private bool subscribed;

        /// <summary>F3 Dev：出现倍率与「下一只箱子必出」。正式构建里永远是 1 / false。</summary>
        internal static float DevChanceMultiplier = 1f;
        internal static bool DevForceNext;

        internal CollectibleLootRuntime(ModBehaviour host)
        {
            owner = host;
            current = this;
            if (!subscribed)
            {
                InteractableLootbox.OnStartLoot += OnStartLoot;
                subscribed = true;
            }
        }

        public void Dispose()
        {
            if (subscribed)
            {
                InteractableLootbox.OnStartLoot -= OnStartLoot;
                subscribed = false;
            }
            ledger.Reset();
            raidOwner = null;
            if (current == this) current = null;
        }

        internal static void ResetStaticCaches()
        {
            DevChanceMultiplier = 1f;
            DevForceNext = false;
        }

        /// <summary>本趟计数跟着关卡 owner 走：同一趟的子场景切换不清零，换一趟才清零。</summary>
        private void RefreshRaid()
        {
            LevelManager level = LevelManager.Instance;
            if (level == raidOwner) return;
            raidOwner = level;
            ledger.Reset();
        }

        internal int DeliveredThisRaid { get { RefreshRaid(); return ledger.Delivered; } }

        // ---------------- 官方出击图 ----------------

        private void OnStartLoot(InteractableLootbox box)
        {
            try
            {
                if (box == null || !LevelManager.AfterInit) return;
                // 自研三图的箱子由各图自己的开箱回调交给 NotifyModBoxFirstOpened。
                // 活动场景之外再看箱子自己在哪个场景：官方场景都在 Build Settings 里（buildIndex ≥ 0），Mod 地图和
                // DontDestroyOnLoad 都是 -1。这样即使活动场景一时不是自研图，自研图的箱子也不会按官方口径（8 档上限）判。
                if (ModRaidSceneBridge.ActiveIdentity != null || box.gameObject.scene.buildIndex < 0) return;
                if (!IsOfficialRaid()) return;
                if (box.GetComponent<LootBoxLoader>() == null) return;
                Inventory inventory = box.Inventory;
                if (inventory == null || inventory.transform.parent != InteractableLootbox.LootBoxInventoriesParent) return;
                CollectibleBoxTier tier;
                if (!CollectibleTuning.TryResolveOfficialBoxTier(inventory.DisplayNameKey, out tier)) return;
                CollectibleSeries series;
                CollectibleSeries? local = CollectibleTuning.TryResolveOfficialSeries(ReadMainSceneId(), out series)
                    ? series : (CollectibleSeries?)null;
                TryDeliver(inventory, tier, local, CollectibleTuning.MaxQuality);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "开箱判定异常: " + e.Message);
            }
        }

        private bool IsOfficialRaid()
        {
            LevelManager level = LevelManager.Instance;
            if (level == null || level.IsBaseLevel || owner == null) return false;
            return !CollectiblesRuntimeModule.OtherModeActive(owner);
        }

        private static string ReadMainSceneId()
        {
            try { return Duckov.Scenes.MultiSceneCore.MainSceneID; }
            catch (Exception) { return null; }
        }

        // ---------------- 自研三图 ----------------

        /// <summary>
        /// 自研图的场景箱第一次被打开（各图开箱回调里、已过本图自己的有效性检查之后调用）。
        /// <paramref name="qualityCap"/> 守住各图已有的价值承诺（天空岛 6、潮闸镇水下箱 4）。
        /// </summary>
        internal static void NotifyModBoxFirstOpened(InteractableLootbox box, CollectibleSeries series, CollectibleBoxTier tier, int qualityCap)
        {
            CollectibleLootRuntime runtime = current;
            if (runtime == null || box == null) return;
            try
            {
                if (runtime.owner != null && CollectiblesRuntimeModule.OtherModeActive(runtime.owner)) return;
                runtime.TryDeliver(box.Inventory, tier, series, qualityCap);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "自研图开箱判定异常: " + e.Message);
            }
        }

        // ---------------- 交付 ----------------

        private void TryDeliver(Inventory inventory, CollectibleBoxTier tier, CollectibleSeries? local, int qualityCap)
        {
            if (inventory == null) return;
            RefreshRaid();
            if (!ledger.TryClaim(inventory.GetInstanceID(), inventory.hasBeenInspectedInLootBox)) return;

            // 新件优先要查当前槽的收藏册；事实没就绪（写屏障等）就不偏向，照常挑。
            CollectiblesRuntimeModule module = CollectiblesRuntimeModule.Current;
            Func<int, bool> registered = module != null && module.Data != null ? (Func<int, bool>)module.IsRegistered : null;
            int typeId;
            if (DevForceNext)
            {
                DevForceNext = false;
                int quality = CollectibleLootRoller.RollQuality(tier, local, qualityCap, NextRandom);
                typeId = quality > 0 ? CollectibleLootRoller.PickInQuality(quality, local, NextRandom, registered) : 0;
            }
            else
            {
                typeId = CollectibleLootRoller.Roll(tier, local, qualityCap, DevChanceMultiplier, NextRandom, registered);
            }
            if (typeId <= 0) return;

            Item item = CollectibleItems.InstantiateChecked(typeId);
            if (item == null) return;
            item.Inspected = false;
            if (!InteractableLootboxInventoryHelper.TryAddExtraItem(inventory, item)) return;
            ledger.MarkDelivered();
            CollectibleSlotFrame.MarkFreshDrop(item);   // 6–8 档搜出来的那一刻放出货演出
            ModBehaviour.DevLog(CollectibleTuning.LogPrefix + "开箱额外一格：" + typeId + " tier=" + tier
                + " local=" + (local.HasValue ? local.Value.ToString() : "-") + " 本趟 " + ledger.Delivered + "/" + CollectibleTuning.PerRaidCap);
        }

        private static double NextRandom()
        {
            float value = UnityEngine.Random.value;
            return value >= 1f ? 0.9999999d : value;
        }
    }
}
