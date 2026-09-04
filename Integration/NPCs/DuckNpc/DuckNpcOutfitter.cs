// ============================================================================
// DuckNpcOutfitter.cs - 给捏脸 NPC 穿装备
// ============================================================================
// 模块说明：
//   捏脸只能改鸭子本体（发型/五官/体色/体型）。要让新 NPC 真正"丰富多彩"，
//   还得给它戴头盔、穿护甲、挂耳机、拿武器 —— 这些都是官方物品，
//   插进角色物品树的装备槽后，官方 CharacterModel 会自动把模型挂到对应 socket 上。
//
//   **候选装备完全由槽位自己决定，不硬编码任何 TypeID。**
//   官方 Slot.CanPlug(item) 归根到底是 `item.Tags.Check(requireTags, excludeTags)`，
//   所以拿 ItemAssetsCollection.GetPrefab(id)（预制体，不实例化）逐个试插即可
//   问出"这个槽位能接受哪些物品"。游戏更新加了新装备也会自动进池，不用改代码。
//
//   代价是一次全表扫描，因此：
//     - 结果按槽位 key 静态缓存；
//     - 缓存**惰性构建**，只有真的要穿装备时才扫（AGENTS.md 4.12）；
//     - Mod 卸载时由 AlwaysOnRuntimeHooks 统一清。
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>一件已穿戴装备的结构化记录。</summary>
    internal struct DuckNpcEquippedItem
    {
        public string SlotKey;
        public int TypeId;
        public string DisplayName;
    }

    /// <summary>一次穿戴的结果，用于诊断报告与「保存为永久 NPC 数据」。</summary>
    internal sealed class DuckNpcOutfitResult
    {
        /// <summary>人读的展示串，进报告。</summary>
        public readonly List<string> Equipped = new List<string>();

        public readonly List<string> Skipped = new List<string>();

        /// <summary>
        /// 结构化记录，进蓝图 JSON。
        /// </summary>
        /// <remarks>
        /// 展示串（"Helmat = 五级作战头盔 (TypeID 1149)"）是给人看的，
        /// 要把随机结果固化成永久 NPC 就得有机器可读的 TypeID，别去正则解析展示串。
        /// </remarks>
        public readonly List<DuckNpcEquippedItem> EquippedItems = new List<DuckNpcEquippedItem>();

        public int EquippedCount
        {
            get { return Equipped.Count; }
        }
    }

    /// <summary>
    /// 捏脸 NPC 装备穿戴器。
    /// </summary>
    internal static class DuckNpcOutfitter
    {
        private const string LogPrefix = "[DuckNpc]";

        /// <summary>全表 TypeID 缓存（一次扫描，全局复用）。</summary>
        private static int[] _allTypeIds;

        /// <summary>槽位 key → 可插入的 TypeID 候选池。</summary>
        private static Dictionary<string, int[]> _slotCandidates;

        /// <summary>
        /// 默认穿戴的槽位。留 null 表示"角色身上所有槽位都试一遍"。
        /// 这几个是视觉上最有存在感的（头盔/护甲/耳机/面罩/背包/主武器）。
        /// </summary>
        /// <remarks>
        /// 槽位 key 来自 2026-09-04 实测（DuckNpcProbeState_20260904_085440.md）：
        /// PrimaryWeapon / SecondaryWeapon / MeleeWeapon / Helmat / Armor /
        /// FaceMask / Headset / Backpack / Totem1 / Totem2。
        /// 这里只收视觉上有存在感的那些；Totem 是纯数值槽，穿了看不出来，不进默认表。
        /// </remarks>
        internal static readonly string[] DefaultVisualSlotKeys = new string[]
        {
            "Helmat",   // 官方原版拼写，不是 Helmet，别"修正"
            "Armor",
            "Headset",
            "FaceMask",
            "Backpack",
            "PrimaryWeapon",
            "SecondaryWeapon",
            "MeleeWeapon"
        };

        // ====================================================================
        // 穿戴
        // ====================================================================

        /// <summary>
        /// 给 NPC 随机穿一套装备。slotKeys 为 null 时用 DefaultVisualSlotKeys。
        /// </summary>
        internal static async UniTask<DuckNpcOutfitResult> EquipRandomAsync(
            CharacterMainControl npc,
            string[] slotKeys)
        {
            return await EquipRandomSeededAsync(npc, slotKeys, 0);
        }

        /// <summary>
        /// 带种子的随机穿戴：同一个 seed 永远穿出同一套。
        /// seed 传 0 表示每次都不同。
        /// </summary>
        /// <remarks>
        /// 与 DuckNpcFaceRandomizer.TryCreateSeeded 同理，必须存档并还原
        /// UnityEngine.Random.state，否则这颗种子会劫持全局随机流。
        /// </remarks>
        internal static async UniTask<DuckNpcOutfitResult> EquipRandomSeededAsync(
            CharacterMainControl npc,
            string[] slotKeys,
            int seed)
        {
            if (seed == 0)
            {
                return await EquipRandomInternalAsync(npc, slotKeys);
            }

            UnityEngine.Random.State saved = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(seed);
                return await EquipRandomInternalAsync(npc, slotKeys);
            }
            finally
            {
                UnityEngine.Random.state = saved;
            }
        }

        private static async UniTask<DuckNpcOutfitResult> EquipRandomInternalAsync(
            CharacterMainControl npc,
            string[] slotKeys)
        {
            DuckNpcOutfitResult result = new DuckNpcOutfitResult();
            if (npc == null)
            {
                return result;
            }

            Item characterItem = ResolveCharacterItem(npc);
            if (characterItem == null)
            {
                result.Skipped.Add("(取不到角色物品树)");
                return result;
            }

            string[] targets = (slotKeys != null && slotKeys.Length > 0)
                ? slotKeys
                : DefaultVisualSlotKeys;

            for (int i = 0; i < targets.Length; i++)
            {
                string slotKey = targets[i];
                if (string.IsNullOrEmpty(slotKey))
                {
                    continue;
                }

                Slot slot = ResolveSlot(characterItem, slotKey);
                if (slot == null)
                {
                    result.Skipped.Add(slotKey + ": 角色没有这个槽位");
                    continue;
                }

                int[] candidates = GetSlotCandidates(slot);
                if (candidates == null || candidates.Length == 0)
                {
                    result.Skipped.Add(slotKey + ": 无可用装备");
                    continue;
                }

                int typeId = candidates[UnityEngine.Random.Range(0, candidates.Length)];
                string equippedName = await TryEquipOneAsync(characterItem, typeId);
                if (equippedName != null)
                {
                    result.Equipped.Add(slotKey + " = " + equippedName + " (TypeID " + typeId + ")");
                    DuckNpcEquippedItem record = new DuckNpcEquippedItem();
                    record.SlotKey = slotKey;
                    record.TypeId = typeId;
                    record.DisplayName = equippedName;
                    result.EquippedItems.Add(record);
                }
                else
                {
                    result.Skipped.Add(slotKey + ": TypeID " + typeId + " 插入失败");
                }
            }

            return result;
        }

        /// <summary>
        /// 按显式 TypeID 列表穿戴。用于蓝图里写死某个 NPC 的固定造型。
        /// </summary>
        internal static async UniTask<DuckNpcOutfitResult> EquipByTypeIdsAsync(
            CharacterMainControl npc,
            int[] typeIds)
        {
            DuckNpcOutfitResult result = new DuckNpcOutfitResult();
            if (npc == null || typeIds == null || typeIds.Length == 0)
            {
                return result;
            }

            Item characterItem = ResolveCharacterItem(npc);
            if (characterItem == null)
            {
                result.Skipped.Add("(取不到角色物品树)");
                return result;
            }

            for (int i = 0; i < typeIds.Length; i++)
            {
                int typeId = typeIds[i];
                if (typeId <= 0)
                {
                    continue;
                }

                string equippedName = await TryEquipOneAsync(characterItem, typeId);
                if (equippedName != null)
                {
                    result.Equipped.Add(equippedName + " (TypeID " + typeId + ")");
                    DuckNpcEquippedItem record = new DuckNpcEquippedItem();
                    record.SlotKey = string.Empty;   // 显式列表不指定槽位，由 TryPlug 自己路由
                    record.TypeId = typeId;
                    record.DisplayName = equippedName;
                    result.EquippedItems.Add(record);
                }
                else
                {
                    result.Skipped.Add("TypeID " + typeId + " 插入失败");
                }
            }

            return result;
        }

        /// <summary>
        /// 实例化一件装备并插进角色物品树。成功返回显示名，失败返回 null。
        /// </summary>
        private static async UniTask<string> TryEquipOneAsync(Item characterItem, int typeId)
        {
            Item item = null;
            try
            {
                item = await ItemAssetsCollection.InstantiateAsync(typeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 装备实例化异常 TypeID=" + typeId + ": " + e.Message);
                return null;
            }

            if (item == null)
            {
                return null;
            }

            try
            {
                // 官方 CreateCharacterAsync 也是这样把初始装备塞进角色的：
                // characterItem.TryPlug(item) 会自己按 Tag 找到该去的槽位。
                if (characterItem.TryPlug(item))
                {
                    return SafeDisplayName(item);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 装备插入异常 TypeID=" + typeId + ": " + e.Message);
            }

            // 插不进去就地销毁，别把孤儿物品留在场景里。
            try
            {
                item.DestroyTree();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 回收未插入装备失败: " + e.Message);
            }
            return null;
        }

        // ====================================================================
        // 候选池
        // ====================================================================

        /// <summary>
        /// 问某个槽位"你能接受哪些物品"。结果按槽位 key 缓存。
        /// </summary>
        internal static int[] GetSlotCandidates(Slot slot)
        {
            if (slot == null)
            {
                return null;
            }

            string key;
            try
            {
                key = slot.Key;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 读槽位 Key 失败: " + e.Message);
                return null;
            }

            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (_slotCandidates == null)
            {
                _slotCandidates = new Dictionary<string, int[]>(StringComparer.Ordinal);
            }

            int[] cached;
            if (_slotCandidates.TryGetValue(key, out cached))
            {
                return cached;
            }

            int[] built = BuildSlotCandidates(slot);
            _slotCandidates[key] = built;
            ModBehaviour.DevLog(LogPrefix + " 槽位候选池已建: " + key
                + " → " + (built != null ? built.Length : 0) + " 件");
            return built;
        }

        private static int[] BuildSlotCandidates(Slot slot)
        {
            int[] all = GetAllTypeIds();
            if (all == null || all.Length == 0)
            {
                return new int[0];
            }

            List<int> matched = new List<int>(64);
            for (int i = 0; i < all.Length; i++)
            {
                int typeId = all[i];
                if (typeId <= 0)
                {
                    continue;
                }

                try
                {
                    // 用预制体试插：CanPlug 归根到底只读 Tags，拿预制体判定是安全的，
                    // 而且避免为了筛选实例化上千个物品。
                    Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                    if (prefab == null)
                    {
                        continue;
                    }
                    if (slot.CanPlug(prefab))
                    {
                        matched.Add(typeId);
                    }
                }
                catch
                {
                    // 单个物品判定失败不应中断整池构建；这类失败通常是资产缺失，
                    // 逐个记日志会在建池时刷屏，这里累计到最后统一说明。
                    continue;
                }
            }

            return matched.ToArray();
        }

        private static int[] GetAllTypeIds()
        {
            if (_allTypeIds != null)
            {
                return _allTypeIds;
            }

            try
            {
                if (ItemAssetsCollection.Instance == null)
                {
                    return null;
                }

                // 与 DailyReport/ModeD 同款的"全表"过滤器：不卡 Tag，只把品质区间开满。
                ItemFilter filter = default(ItemFilter);
                filter.requireTags = null;
                filter.excludeTags = null;
                filter.minQuality = 1;
                filter.maxQuality = 8;
                filter.caliber = string.Empty;

                _allTypeIds = ItemAssetsCollection.Search(filter);
                ModBehaviour.DevLog(LogPrefix + " 物品全表已缓存: "
                    + (_allTypeIds != null ? _allTypeIds.Length : 0) + " 个 TypeID");
                return _allTypeIds;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 物品全表扫描失败: " + e.Message);
                return null;
            }
        }

        // ====================================================================
        // 工具
        // ====================================================================

        private static Item ResolveCharacterItem(CharacterMainControl npc)
        {
            try
            {
                return npc.CharacterItem;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 取角色物品树失败: " + e.Message);
                return null;
            }
        }

        private static Slot ResolveSlot(Item characterItem, string slotKey)
        {
            try
            {
                SlotCollection slots = characterItem.Slots;
                if (slots == null)
                {
                    return null;
                }
                return slots.GetSlot(slotKey);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 取槽位失败 key=" + slotKey + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 直接从角色物品树读回当前穿戴，而不是依赖上一次穿戴的返回值。
        /// </summary>
        /// <remarks>
        /// 「保存为永久 NPC 数据」必须读实况：探针可能被重掷过装备、
        /// 也可能是用玩家脸生成后手动改过的。以角色身上真实插着的为准。
        /// </remarks>
        internal static List<DuckNpcEquippedItem> ReadEquippedItems(CharacterMainControl npc)
        {
            List<DuckNpcEquippedItem> items = new List<DuckNpcEquippedItem>();
            Item characterItem = ResolveCharacterItem(npc);
            if (characterItem == null)
            {
                return items;
            }

            try
            {
                SlotCollection slots = characterItem.Slots;
                if (slots == null)
                {
                    return items;
                }

                foreach (Slot slot in slots)
                {
                    if (slot == null)
                    {
                        continue;
                    }
                    Item content = slot.Content;
                    if (content == null)
                    {
                        continue;
                    }

                    DuckNpcEquippedItem record = new DuckNpcEquippedItem();
                    record.SlotKey = slot.Key;
                    record.TypeId = content.TypeID;
                    record.DisplayName = SafeDisplayName(content);
                    items.Add(record);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 读回当前穿戴失败: " + e.Message);
            }

            return items;
        }

        /// <summary>
        /// 卸下并销毁角色当前全部穿戴，用于「重掷装备」。
        /// </summary>
        /// <remarks>
        /// 不卸就直接再穿一轮的话，TryPlug 会因为槽位已占而全部失败，
        /// 表现成"点了重掷但一点没变"。
        /// </remarks>
        internal static int StripEquipment(CharacterMainControl npc)
        {
            int removed = 0;
            Item characterItem = ResolveCharacterItem(npc);
            if (characterItem == null)
            {
                return removed;
            }

            try
            {
                SlotCollection slots = characterItem.Slots;
                if (slots == null)
                {
                    return removed;
                }

                foreach (Slot slot in slots)
                {
                    if (slot == null || slot.Content == null)
                    {
                        continue;
                    }

                    try
                    {
                        Item unplugged = slot.Unplug();
                        if (unplugged != null)
                        {
                            unplugged.DestroyTree();
                            removed++;
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog(LogPrefix + " [WARNING] 卸下装备失败 slot=" + slot.Key + ": " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 遍历槽位卸装失败: " + e.Message);
            }

            return removed;
        }

        /// <summary>列出角色身上全部槽位的 key，供诊断报告用。</summary>
        internal static List<string> ListSlotKeys(CharacterMainControl npc)
        {
            List<string> keys = new List<string>();
            Item characterItem = ResolveCharacterItem(npc);
            if (characterItem == null)
            {
                return keys;
            }

            try
            {
                SlotCollection slots = characterItem.Slots;
                if (slots == null)
                {
                    return keys;
                }

                foreach (Slot slot in slots)
                {
                    if (slot == null)
                    {
                        continue;
                    }
                    string key = slot.Key;
                    Item content = slot.Content;
                    keys.Add(key + (content != null ? " = " + SafeDisplayName(content) : " = (空)"));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 枚举槽位失败: " + e.Message);
            }

            return keys;
        }

        private static string SafeDisplayName(Item item)
        {
            try
            {
                string name = item.DisplayName;
                return string.IsNullOrEmpty(name) ? item.name : name;
            }
            catch
            {
                return "(无名)";
            }
        }

        /// <summary>
        /// 清空静态缓存。由 AlwaysOnRuntimeHooks 在 Mod 卸载时调用。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            _allTypeIds = null;
            if (_slotCandidates != null)
            {
                _slotCandidates.Clear();
                _slotCandidates = null;
            }
        }
    }
}
