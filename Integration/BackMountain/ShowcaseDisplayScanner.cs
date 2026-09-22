// ============================================================================
// ShowcaseDisplayScanner.cs - 采集官方枪械展示架 / 假人上实际摆放的 Mod 战利品
// ============================================================================
// 官方 Duckov.Buildings.Showcase：item 容器的 SlotCollection 就是展示槽，物品真被搬进去、
// 官方自己持久化（Item.Save("Showcase_"+id)）、用物品自己的模型陈列。Showcase.Load() 是异步的，
// 单次扫描不够：逐柜订阅 Item.onSlotContentChanged（与官方 ShowcaseAltar.Start 同款），只置脏，
// 由运行时模块每帧 FlushIfDirty（首句 O(1) 早返）整体重算并交给 ShowcaseService.ApplyDisplaySnapshot。
//
// 门控（AGENTS §4.12）：只在基地且陈列加成已解锁时订阅；未解锁 / 局内零订阅零扫描；
// 场景切换、换槽、关开关、销毁都退订（命名方法，lambda 退不掉）。基地里新建的枪架 / 假人经官方
// BuildingManager.OnBuildingBuilt 触发重扫。局外 ShowcaseService 只读缓存挂加成。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Buildings;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>官方展示建筑（枪械展示架 / 假人）陈列采集器（Unity 侧，不进执行回归；判据在 ShowcaseDisplayJudges）。</summary>
    internal static class ShowcaseDisplayScanner
    {
        private static readonly List<Item> _subscribed = new List<Item>();
        private static bool _dirty, _buildSubscribed, _anyShowcaseFound;

        /// <summary>本场景是否找到过至少一个官方展示建筑（老档迁移判据用）。</summary>
        internal static bool AnyShowcaseFound { get { return _anyShowcaseFound; } }

        internal static int SubscriptionCount { get { return _subscribed.Count; } }

        /// <summary>
        /// 进基地 / 实时解锁 / 新柜建成：先整体退订再重建订阅，立刻重算一次。
        /// 不在基地或未解锁：只退订，不扫描。
        /// </summary>
        internal static void RefreshForScene(bool inBaseScene, bool unlocked)
        {
            ClearSubscriptions();
            if (!inBaseScene || !unlocked) return;
            try
            {
                Showcase[] showcases = UnityEngine.Object.FindObjectsOfType<Showcase>(true);
                for (int i = 0; i < showcases.Length; i++)
                {
                    Showcase showcase = showcases[i];
                    if (showcase == null) continue;
                    Item item = showcase.Item;
                    if (item == null) continue;
                    item.onSlotContentChanged += HandleSlotContentChanged;
                    _subscribed.Add(item);
                }
                _anyShowcaseFound = _subscribed.Count > 0;
                EnsureBuildSubscription();
                _dirty = true;
                FlushIfDirty();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 扫描官方展示建筑失败: " + e.Message);
            }
        }

        private static void HandleSlotContentChanged(Item item, Slot slot)
        {
            _dirty = true;
        }

        /// <summary>基地里新建了建筑：可能是新柜子，下一拍重扫（幂等）。</summary>
        private static void HandleBuildingBuilt(int guid)
        {
            _dirty = true;
            _needRescan = true;
        }

        private static bool _needRescan;

        /// <summary>由运行时模块每帧调用；无变化时 O(1) 早返。</summary>
        internal static void FlushIfDirty()
        {
            if (!_dirty) return;
            _dirty = false;
            if (_needRescan)
            {
                _needRescan = false;
                RefreshForScene(true, true);
                return;
            }
            int[] displayed = CollectDisplayedTrophies();
            ShowcaseService.ApplyDisplaySnapshot(displayed, _anyShowcaseFound);
        }

        private static int[] CollectDisplayedTrophies()
        {
            var ids = new List<int>();
            for (int i = 0; i < _subscribed.Count; i++)
            {
                Item item = _subscribed[i];
                if (item == null) continue;
                SlotCollection slots;
                try { slots = item.Slots; }
                catch (Exception) { continue; }
                if (slots == null) continue;
                foreach (Slot slot in slots)
                {
                    Item content = slot != null ? slot.Content : null;
                    if (content == null) continue;
                    int typeId = content.TypeID;
                    if (ShowcaseTrophyCatalog.IsShowcaseTrophy(typeId)) ids.Add(typeId);
                }
            }
            return ids.ToArray();
        }

        private static void EnsureBuildSubscription()
        {
            if (_buildSubscribed) return;
            BuildingManager.OnBuildingBuilt += HandleBuildingBuilt;
            _buildSubscribed = true;
        }

        /// <summary>逐个退订（已销毁的 Item 上 -= 是无害 no-op），清表。场景切换 / 换槽 / 关开关 / 销毁都调。</summary>
        internal static void ClearSubscriptions()
        {
            for (int i = 0; i < _subscribed.Count; i++)
            {
                try { if (_subscribed[i] != null) _subscribed[i].onSlotContentChanged -= HandleSlotContentChanged; }
                catch (Exception) { }
            }
            _subscribed.Clear();
            if (_buildSubscribed)
            {
                BuildingManager.OnBuildingBuilt -= HandleBuildingBuilt;
                _buildSubscribed = false;
            }
            _dirty = false;
            _needRescan = false;
            _anyShowcaseFound = false;
        }

        internal static void ResetStaticCaches()
        {
            ClearSubscriptions();
        }

        #region F3 只读探针

        /// <summary>
        /// 枚举场上官方展示建筑（枪架 / 假人 / 皮肤柜）：DevLog 每件 ID、每槽 key / requireTags / excludeTags，以及 Mod 物品对每槽的 CanPlug；
        /// metrics 只放计数。只读：Slot.CanPlug 不改状态。
        /// </summary>
        internal static bool ProbeOfficialShowcases(out string metrics, out string reason)
        {
            int showcases = 0, slots = 0, slotsWithTags = 0, modTypes = 0, plugOk = 0;
            reason = null;
            try
            {
                Showcase[] all = UnityEngine.Object.FindObjectsOfType<Showcase>(true);
                int[] published = BossRushDynamicItemRegistry.GetPublishedTypeIds();
                var loaded = new List<Item>();
                for (int i = 0; i < published.Length; i++)
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(published[i]);
                    if (prefab != null) loaded.Add(prefab);
                }
                modTypes = loaded.Count;
                for (int i = 0; i < all.Length; i++)
                {
                    Showcase showcase = all[i];
                    if (showcase == null) continue;
                    showcases++;
                    SlotCollection collection = showcase.Slots;
                    int slotCount = collection != null ? collection.Count : 0;
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[PROBE] Showcase id=" + showcase.ID + " slots=" + slotCount);
                    if (collection == null) continue;
                    int index = 0;
                    foreach (Slot slot in collection)
                    {
                        if (slot == null) continue;
                        slots++;
                        string require = DescribeTags(slot.requireTags);
                        string exclude = DescribeTags(slot.excludeTags);
                        if (slot.requireTags != null && slot.requireTags.Count > 0) slotsWithTags++;
                        ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[PROBE]   slot[" + index + "] key=" + slot.Key
                            + " require=[" + require + "] exclude=[" + exclude + "] content=" + (slot.Content != null ? slot.Content.TypeID : 0));
                        for (int m = 0; m < loaded.Count; m++)
                        {
                            bool ok;
                            try { ok = slot.CanPlug(loaded[m]); }
                            catch (Exception) { ok = false; }
                            if (ok) plugOk++;
                            else if (index == 0)
                                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[PROBE]   CanPlug " + loaded[m].TypeID + " -> " + showcase.ID + "/" + slot.Key + " = false");
                        }
                        index++;
                    }
                }
                if (showcases == 0) reason = "场上没有官方展示建筑：先在建造界面造一个「枪械展示架」或「假人」再跑探针";
            }
            catch (Exception e)
            {
                reason = "探针异常: " + e.Message;
            }
            metrics = "showcases=" + showcases + ",slots=" + slots + ",slots_with_tags=" + slotsWithTags
                + ",modtypes=" + modTypes + ",plug_ok=" + plugOk + ",trophies=" + ShowcaseTrophyCatalog.TrophyCount + ",read_only=true";
            return reason == null;
        }

        private static string DescribeTags(List<Tag> tags)
        {
            if (tags == null || tags.Count == 0) return string.Empty;
            var names = new List<string>();
            for (int i = 0; i < tags.Count; i++) names.Add(tags[i] != null ? tags[i].name : "null");
            return string.Join(",", names.ToArray());
        }

        #endregion
    }
}
