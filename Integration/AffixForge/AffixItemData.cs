// ============================================================================
// AffixItemData.cs - 装备词缀 KV（AFX_ schema）的唯一读写口
// ============================================================================
// 职责：
//   - 定义并读写装备上的 `AFX_` 自定义变量（Item.Variables）。这是词缀身份的
//     唯一存储处，**零 SavesSystem key、零 mod 类型进档**：官方
//     `ItemTreeData.FromItem` 已经把 Item.Variables 逐条深拷贝（含 Display 标志），
//     存读档 / 卖店买回 / 快递往返 / 掉落拾回一律自动带着走。
//   - 判定装备是否可承载词缀，并把装备归类成 AffixEquipMask。
//
// 硬约束（踩过就很难查，务必遵守）：
//   1. **KV 类型终身冻结**。官方 CustomData 的 SetFloat/SetInt/SetBool/SetString
//      在 dataType 不匹配时只 Debug.LogWarning 然后**静默 no-op**
//      （AllowChangeTypeWithSet = false）。所以每个 AFX_ 键只能用它首次创建时的
//      类型写：AFX_V / AFX_CAP 只用 int，AFX_SLOT_n / AFX_NAME_n 只用 string，
//      AFX_LOCK_n 只用 bool。本文件是唯一写入口，别处一律不许直接碰 AFX_ 键。
//   2. **清空槽 = 写空串，绝不 Remove 条目**。删除会让 CustomDataCollection 的
//      哈希索引重建，且已经挂在 CustomData.OnSetData 上的 UI 条目会悬空。
//   3. **只有 AFX_NAME_n 的 Display 是 true**。其余四类全 false，否则玩家会在
//      物品详情里看到 AFX_V / AFX_SLOT_1 这些技术字段。
//   4. **AFX_NAME_n 存的必须是本地化键**。官方 CustomData.GetValueDisplayString
//      对 String 类型会执行 `GetString().ToPlainText()`——值本身被当 key 再翻一次。
//      存中文字面量会显示成 *中文*。
//   5. 行为参数不进 KV，全部按 (Id, Tier) 在 AffixDefinitions 查表。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>一个词缀槽的运行时视图。不进存档，只是 KV 的解析结果。</summary>
    public struct AffixSlotView
    {
        /// <summary>槽位下标，1..<see cref="AffixDefinitions.MaxSlots"/>。</summary>
        public int SlotIndex;

        /// <summary>词缀 id。null 或空串表示空槽。</summary>
        public string AffixId;

        /// <summary>强度档 1..3；空槽为 0。</summary>
        public int Tier;

        /// <summary>是否锁定（锁定槽在重铸时跳过）。</summary>
        public bool Locked;

        /// <summary>是否空槽。</summary>
        public bool IsEmpty { get { return string.IsNullOrEmpty(AffixId); } }
    }

    /// <summary>装备词缀 KV 的读写门面。全部方法 no-throw。</summary>
    public static class AffixItemData
    {
        #region 键名

        /// <summary>schema 版本键（Int，Display=false）。存在即代表这件装备被词缀锻造过。</summary>
        public const string KEY_VERSION = "AFX_V";

        /// <summary>槽位容量键（Int，Display=false）。首铸时按 Quality 冻结。</summary>
        public const string KEY_CAPACITY = "AFX_CAP";

        /// <summary>槽位内容键前缀（String，Display=false）。值形如 "lifesteal:2"。</summary>
        public const string PREFIX_SLOT = "AFX_SLOT_";

        /// <summary>槽位锁定键前缀（Bool，Display=false）。</summary>
        public const string PREFIX_LOCK = "AFX_LOCK_";

        /// <summary>槽位展示名键前缀（String，**Display=true**）。值 = 词缀名称本地化键。</summary>
        public const string PREFIX_NAME = "AFX_NAME_";

        /// <summary>编码分隔符。id 与 tier 之间。</summary>
        private const char SLOT_SEPARATOR = ':';

        #endregion

        #region 键构造

        /// <summary>槽位内容键。</summary>
        public static string SlotKey(int slotIndex)
        {
            return PREFIX_SLOT + ClampSlotIndex(slotIndex);
        }

        /// <summary>槽位锁定键。</summary>
        public static string LockKey(int slotIndex)
        {
            return PREFIX_LOCK + ClampSlotIndex(slotIndex);
        }

        /// <summary>槽位展示名键。</summary>
        public static string NameKey(int slotIndex)
        {
            return PREFIX_NAME + ClampSlotIndex(slotIndex);
        }

        /// <summary>是否属于词缀 KV（重铸系统靠这个前缀与我们互斥）。</summary>
        public static bool IsAffixVariableKey(string key)
        {
            return !string.IsNullOrEmpty(key) &&
                   key.StartsWith(AffixDefinitions.KvPrefix, StringComparison.Ordinal);
        }

        private static int ClampSlotIndex(int slotIndex)
        {
            if (slotIndex < 1) return 1;
            if (slotIndex > AffixDefinitions.MaxSlots) return AffixDefinitions.MaxSlots;
            return slotIndex;
        }

        #endregion

        #region 装备资格与归类

        /// <summary>
        /// 该物品能否承载词缀：
        ///   - 挂了 ItemSetting_Gun / ItemSetting_MeleeWeapon 组件（枪 / 近战），或
        ///   - 带 Armor / Helmat / Helmet / FaceMask 标签（护甲 / 头盔 / 面罩）。
        /// 背包、耳机、图腾等**不在**词缀体系内：词缀的效果全部围绕战斗归因，
        /// 挂到非战斗件上既难归因也难向玩家解释。
        /// </summary>
        public static bool IsAffixEligible(Item item)
        {
            return GetEquipMask(item) != AffixEquipMask.None;
        }

        /// <summary>
        /// 把装备归成单一类型位。无法归类返回 <see cref="AffixEquipMask.None"/>。
        /// 组件判定优先于标签：官方近战武器也可能带 "Weapon" 泛标签，
        /// 组件才是"这到底是枪还是刀"的可靠依据（先例：DeathWraithCombatLoadout）。
        /// </summary>
        public static AffixEquipMask GetEquipMask(Item item)
        {
            if (item == null) return AffixEquipMask.None;

            try
            {
                if (item.GetComponent<ItemSetting_Gun>() != null)
                {
                    return AffixEquipMask.Gun;
                }
                if (item.GetComponent<ItemSetting_MeleeWeapon>() != null)
                {
                    return AffixEquipMask.Melee;
                }
            }
            catch (Exception)
            {
                // 组件查询失败（物品已销毁等）时退回标签判定
            }

            try
            {
                TagCollection tags = item.Tags;
                if (tags != null)
                {
                    if (tags.Contains("Armor")) return AffixEquipMask.Armor;
                    // "Helmat" 是官方原版拼写，"Helmet" 是部分自定义装备用的写法，两者都认
                    if (tags.Contains("Helmat") || tags.Contains("Helmet")) return AffixEquipMask.Helmet;
                    if (tags.Contains("FaceMask")) return AffixEquipMask.FaceMask;
                    // 只带泛武器标签、没有 ItemSetting 组件的自定义武器兜底
                    if (tags.Contains("Gun")) return AffixEquipMask.Gun;
                    if (tags.Contains("MeleeWeapon") || tags.Contains("Melee")) return AffixEquipMask.Melee;
                }
            }
            catch (Exception)
            {
                // 标签集合不可用，按不合格处理
            }

            return AffixEquipMask.None;
        }

        #endregion

        #region 版本与容量

        /// <summary>这件装备是否已经有词缀数据（= 被锻造过）。</summary>
        public static bool HasAffixData(Item item)
        {
            if (item == null) return false;
            try
            {
                return item.Variables != null && item.Variables.GetEntry(KEY_VERSION) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>读 schema 版本。未锻造过返回 0。</summary>
        public static int GetSchemaVersion(Item item)
        {
            if (item == null) return 0;
            try
            {
                if (item.Variables == null) return 0;
                return item.Variables.GetInt(KEY_VERSION, 0);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 读槽位容量。缺失时按 Item.Quality 推导并**不写回**——写回属于副作用，
        /// 只能发生在首铸的 <see cref="EnsureInitialized"/> 里。
        /// </summary>
        public static int GetCapacity(Item item)
        {
            if (item == null) return 0;
            try
            {
                if (item.Variables != null)
                {
                    CustomData entry = item.Variables.GetEntry(KEY_CAPACITY);
                    if (entry != null)
                    {
                        int stored = item.Variables.GetInt(KEY_CAPACITY, 0);
                        if (stored >= 1)
                        {
                            return stored > AffixDefinitions.MaxSlots ? AffixDefinitions.MaxSlots : stored;
                        }
                    }
                }
                return AffixDefinitions.GetSlotCountForQuality(item.Quality);
            }
            catch (Exception)
            {
                return 1;
            }
        }

        /// <summary>
        /// 首铸时写入 schema 版本与容量。幂等：已存在则不覆盖容量
        /// （容量一经冻结就不再变，否则老装备会因 owner 调阈值而突然增减槽位）。
        /// </summary>
        public static bool EnsureInitialized(Item item, int capacity)
        {
            if (item == null) return false;
            try
            {
                if (item.Variables == null) return false;

                if (capacity < 1) capacity = 1;
                if (capacity > AffixDefinitions.MaxSlots) capacity = AffixDefinitions.MaxSlots;

                // 首次创建必须用 int 字面量语义，类型一经确定终身冻结
                if (item.Variables.GetEntry(KEY_VERSION) == null)
                {
                    item.Variables.Set(KEY_VERSION, AffixDefinitions.KvSchemaVersion, true);
                    item.Variables.SetDisplay(KEY_VERSION, false);
                }

                if (item.Variables.GetEntry(KEY_CAPACITY) == null)
                {
                    item.Variables.Set(KEY_CAPACITY, capacity, true);
                    item.Variables.SetDisplay(KEY_CAPACITY, false);
                }

                // 容量内的槽条目一次性建齐，之后只改值不建条目（类型就此冻结）
                for (int i = 1; i <= capacity; i++)
                {
                    EnsureSlotEntries(item, i);
                    if (item.Variables.GetEntry(SlotKey(i)) == null
                        || item.Variables.GetEntry(LockKey(i)) == null
                        || item.Variables.GetEntry(NameKey(i)) == null)
                    {
                        return false;
                    }
                }
                return GetSchemaVersion(item) == AffixDefinitions.KvSchemaVersion
                    && GetCapacity(item) == capacity;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixItemData] 初始化词缀 KV 失败: " + e.Message);
                return false;
            }
        }

        private static void EnsureSlotEntries(Item item, int slotIndex)
        {
            string slotKey = SlotKey(slotIndex);
            string lockKey = LockKey(slotIndex);
            string nameKey = NameKey(slotIndex);

            if (item.Variables.GetEntry(slotKey) == null)
            {
                item.Variables.Set(slotKey, string.Empty, true);
                item.Variables.SetDisplay(slotKey, false);
            }
            if (item.Variables.GetEntry(lockKey) == null)
            {
                item.Variables.Set(lockKey, false, true);
                item.Variables.SetDisplay(lockKey, false);
            }
            if (item.Variables.GetEntry(nameKey) == null)
            {
                item.Variables.Set(nameKey, string.Empty, true);
                item.Variables.SetDisplay(nameKey, false);
            }
        }

        #endregion

        #region 读

        /// <summary>读单槽。物品无词缀数据或槽越界时返回 false，view 为默认值。</summary>
        public static bool TryReadSlot(Item item, int slotIndex, out AffixSlotView view)
        {
            view = new AffixSlotView();
            view.SlotIndex = ClampSlotIndex(slotIndex);

            if (item == null) return false;
            try
            {
                if (item.Variables == null) return false;

                string raw = item.Variables.GetString(SlotKey(slotIndex), null);
                string id;
                int tier;
                if (TryDecodeSlot(raw, out id, out tier))
                {
                    view.AffixId = id;
                    view.Tier = tier;
                }
                view.Locked = item.Variables.GetBool(LockKey(slotIndex), false);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 读全部**非空**槽到 buffer（先 Clear 再填），返回非空槽数（= buffer.Count）。
        /// buffer 由调用方复用，本方法自身零额外分配。空槽不入 buffer——需要逐槽渲染
        /// （含空槽）的 UI 请改用 <see cref="TryReadSlot"/> 按下标取。
        /// </summary>
        public static int ReadAllSlots(Item item, List<AffixSlotView> buffer)
        {
            if (buffer == null) return 0;
            buffer.Clear();
            if (item == null) return 0;

            int capacity = GetCapacity(item);
            for (int i = 1; i <= capacity; i++)
            {
                AffixSlotView view;
                if (!TryReadSlot(item, i, out view)) continue;
                if (view.IsEmpty) continue;
                buffer.Add(view);
            }
            return buffer.Count;
        }

        /// <summary>某槽是否锁定。</summary>
        public static bool IsLocked(Item item, int slotIndex)
        {
            if (item == null) return false;
            try
            {
                return item.Variables != null && item.Variables.GetBool(LockKey(slotIndex), false);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 写

        /// <summary>
        /// 写一个词缀到指定槽。同时刷新展示名 KV（值 = 名称本地化键，Display=true）。
        /// **不改锁定位**：锁定由 <see cref="SetLock"/> 独立管理。
        /// </summary>
        public static bool WriteSlot(Item item, int slotIndex, string affixId, int tier)
        {
            if (item == null || string.IsNullOrEmpty(affixId)) return false;
            try
            {
                if (item.Variables == null) return false;

                if (tier < 1) tier = 1;
                if (tier > 3) tier = 3;

                EnsureSlotEntries(item, slotIndex);
                string encoded = EncodeSlot(affixId, tier);
                item.Variables.Set(SlotKey(slotIndex), encoded, true);
                item.Variables.SetDisplay(SlotKey(slotIndex), false);

                if (!StampNameKV(item, slotIndex, affixId)) return false;
                AffixSlotView readback;
                return TryReadSlot(item, slotIndex, out readback)
                    && string.Equals(readback.AffixId, affixId, StringComparison.Ordinal)
                    && readback.Tier == tier;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixItemData] 写入词缀槽失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 刷新槽位展示名 KV。值必须是**本地化键**——官方
        /// CustomData.GetValueDisplayString 对 String 会再 ToPlainText() 一次。
        /// </summary>
        public static bool StampNameKV(Item item, int slotIndex, string affixId)
        {
            if (item == null) return false;
            try
            {
                if (item.Variables == null) return false;

                string nameKey = NameKey(slotIndex);
                AffixDefinition def = AffixDefinitions.Find(affixId);
                if (def == null)
                {
                    // 未知 id：不显示左右列，但绝不清掉 AFX_SLOT_n 原值（fail-open）
                    item.Variables.Set(nameKey, string.Empty, true);
                    item.Variables.SetDisplay(nameKey, false);
                    return false;
                }

                item.Variables.Set(nameKey, AffixDefinitions.NameLocKeyPrefix + def.Id, true);
                item.Variables.SetDisplay(nameKey, true);
                return string.Equals(
                    item.Variables.GetString(nameKey, null),
                    AffixDefinitions.NameLocKeyPrefix + def.Id,
                    StringComparison.Ordinal);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixItemData] 写入词缀展示名失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 清空一个槽：内容写空串、展示名写空串并关 Display、锁定复位。
        /// **绝不 Remove 条目**（见文件头硬约束 2）。
        /// </summary>
        public static bool ClearSlot(Item item, int slotIndex)
        {
            if (item == null) return false;
            try
            {
                if (item.Variables == null) return false;

                EnsureSlotEntries(item, slotIndex);
                item.Variables.Set(SlotKey(slotIndex), string.Empty, true);
                item.Variables.SetDisplay(SlotKey(slotIndex), false);
                item.Variables.Set(NameKey(slotIndex), string.Empty, true);
                item.Variables.SetDisplay(NameKey(slotIndex), false);
                item.Variables.Set(LockKey(slotIndex), false, true);
                item.Variables.SetDisplay(LockKey(slotIndex), false);
                AffixSlotView readback;
                return TryReadSlot(item, slotIndex, out readback)
                    && readback.IsEmpty && !readback.Locked
                    && string.IsNullOrEmpty(item.Variables.GetString(NameKey(slotIndex), null));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixItemData] 清空词缀槽失败: " + e.Message);
                return false;
            }
        }

        /// <summary>清空全部槽（保留 AFX_V / AFX_CAP，装备仍算"锻造过"）。</summary>
        public static int ClearAll(Item item)
        {
            if (item == null) return 0;
            int cleared = 0;
            for (int i = 1; i <= AffixDefinitions.MaxSlots; i++)
            {
                if (ClearSlot(item, i)) cleared++;
            }
            return cleared;
        }

        /// <summary>设置槽位锁定位。</summary>
        public static bool SetLock(Item item, int slotIndex, bool locked)
        {
            if (item == null) return false;
            try
            {
                if (item.Variables == null) return false;
                EnsureSlotEntries(item, slotIndex);
                item.Variables.Set(LockKey(slotIndex), locked, true);
                item.Variables.SetDisplay(LockKey(slotIndex), false);
                return item.Variables.GetBool(LockKey(slotIndex), !locked) == locked;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixItemData] 设置词缀锁定失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 把某物品全部 AFX_ 条目**物理删除**。仅供调试与回退，正常玩法不调用
        /// （删除条目会重建哈希索引并让已绑定的 UI 条目悬空）。返回删除条数。
        /// </summary>
        public static int StripAll(Item item)
        {
            if (item == null) return 0;
            try
            {
                if (item.Variables == null) return 0;

                List<CustomData> doomed = new List<CustomData>();
                foreach (CustomData data in item.Variables)
                {
                    if (data == null) continue;
                    if (IsAffixVariableKey(data.Key)) doomed.Add(data);
                }

                int removed = 0;
                for (int i = 0; i < doomed.Count; i++)
                {
                    if (item.Variables.Remove(doomed[i])) removed++;
                }
                return removed;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixItemData] 清除词缀 KV 失败: " + e.Message);
                return 0;
            }
        }

        #endregion

        #region 编解码

        /// <summary>编码槽位值："&lt;affixId&gt;:&lt;tier&gt;"。</summary>
        internal static string EncodeSlot(string affixId, int tier)
        {
            if (string.IsNullOrEmpty(affixId)) return string.Empty;
            return affixId + SLOT_SEPARATOR + tier.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 解码槽位值。空串 / 格式不符返回 false。
        /// **不校验 id 是否已知**——未知 id 依然解码成功，由调用方 fail-open 处理。
        /// </summary>
        internal static bool TryDecodeSlot(string raw, out string id, out int tier)
        {
            id = null;
            tier = 0;
            if (string.IsNullOrEmpty(raw)) return false;

            int sep = raw.IndexOf(SLOT_SEPARATOR);
            if (sep <= 0 || sep >= raw.Length - 1) return false;

            string idPart = raw.Substring(0, sep);
            string tierPart = raw.Substring(sep + 1);
            if (string.IsNullOrEmpty(idPart)) return false;

            int parsed;
            if (!int.TryParse(tierPart, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }

            if (parsed < 1) parsed = 1;
            if (parsed > 3) parsed = 3;

            id = idPart;
            tier = parsed;
            return true;
        }

        #endregion
    }
}
