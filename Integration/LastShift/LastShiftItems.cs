// ============================================================================
// LastShiftItems.cs - 末班编组站物品：押运钢印、末班票根
// ============================================================================
// TypeID 500117-500118（台账见 docs/reference/Bossrush使用物品ID表.md、docs/contracts.md §1 与 AGENTS.md §4.3；
// 常量在 Config/ConfigItemIds.cs 的 BossRushItemIds）。设计只允许这两件新物品（2026-11-07 第五批「就做钢印和票根」）。
//
// 【零新增 bundle】与潮闸镇物品同一套克隆注册（形态照 Integration/Tidegate/TidegateItems.cs）：从既有 Mod 物品克隆 prefab、
// 改 TypeID、配属性、AddDynamicEntry；图标读 Assets/Items/<IconName>.png，缺图时保持克隆源的图标，不 fail。
//
// 【来路 / 用途 / 串到哪】
//   - 押运钢印：打倒押运长（编组站专属头目）后从他身上取得。下一趟凭它在押运库开一次铅封侧柜（头格保底 Q4 的贵重物），开柜即消耗。
//     身上已带着一枚再打倒押运长，得到的是他的私货（Q5 / Q6）而不是第二枚。可以卖，但卖了就少一次侧柜。
//   - 末班票根：在编组站读齐三条线索（司机室打孔钳、维修棚旧录音、押运库退件簿）后得到。带出去，下次在仓库区剪票时交给余响，
//     她打完最后一孔，「没人来领的末班票」收进图鉴；之后每次剪票她都会说出下一班的事件和加价台。剧情物品，不可出售。
// 【数值口径】官方 2–5 品质带中位 810、均值 2116。钢印 1200 / 品质 4（一次侧柜的钥匙，卖价不高于它开出的东西）；
//   票根 50 / 品质 2（剧情纪念，不可出售）。两件都登记掉落黑名单，不进许愿台、日报与各类品质池。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>末班编组站物品的注册、配置与本地化。获取途径与局内用途由 LastShiftYard/ 的运行时接线。</summary>
    public static class LastShiftItems
    {
        private const string LogPrefix = "[LastShiftItems] ";

        private sealed class Definition
        {
            internal int TypeId;
            internal string LocKey, NameCN, NameEN, DescCN, DescEN, IconName;
            internal int Value, Quality;
            internal float Weight;
            internal bool NotSellable;
        }

        private static Definition[] _definitions;

        private static Definition[] Definitions
        {
            get
            {
                if (_definitions == null)
                {
                    _definitions = new[]
                    {
                        new Definition
                        {
                            TypeId = BossRushItemIds.LastShiftStamp, LocKey = "BossRush_LastShift_Stamp", NameCN = "押运钢印", NameEN = "Escort Steel Stamp",
                            DescCN = "押运长挂在腰上的钢印，压铅封用的，印面磨得发亮。\n下一趟去末班编组站，凭它在押运库开一次铅封侧柜。开柜就收走。",
                            DescEN = "The escort chief's steel stamp for pressing lead seals, its face worn bright.\nOn your next trip to Last Shift Yard, it opens the sealed side cabinet in the bond store once. Opening the cabinet uses it up.",
                            IconName = "lastshift_stamp", Value = 1200, Quality = 4, Weight = 0.4f,
                        },
                        new Definition
                        {
                            TypeId = BossRushItemIds.LastShiftTicketStub, LocKey = "BossRush_LastShift_TicketStub", NameCN = "末班票根", NameEN = "Last Shift Ticket Stub",
                            DescCN = "一张没人来领的末班车票，票面还差最后一个孔。\n带出编组站，下次在仓库区剪票时交给余响。",
                            DescEN = "A last-shift ticket nobody ever came to collect, one hole short of punched.\nCarry it out of the yard and hand it to Yu Xiang the next time you board at the warehouse district.",
                            IconName = "lastshift_ticket_stub", Value = 50, Quality = 2, Weight = 0.02f, NotSellable = true,
                        },
                    };
                }
                return _definitions;
            }
        }

        private static Definition GetDefinition(int typeId)
        {
            foreach (Definition def in Definitions) if (def.TypeId == typeId) return def;
            return null;
        }

        internal static string Name(int typeId)
        {
            Definition def = GetDefinition(typeId);
            return def == null ? string.Empty : L10n.T(def.NameCN, def.NameEN);
        }

        #region 注册与配置

        private static bool _configuratorsRegistered;

        /// <summary>幂等注册全部物品配置器（Integration/Items/ItemContentRegistry.cs 调用）。</summary>
        public static void RegisterConfigurators()
        {
            if (_configuratorsRegistered) return;
            _configuratorsRegistered = true;
            foreach (Definition def in Definitions)
            {
                int typeId = def.TypeId;
                ItemFactory.RegisterConfigurator(typeId, delegate(Item item) { ConfigureItem(typeId, item); });
            }
            ModBehaviour.DevLog(LogPrefix + "物品配置器已注册 " + Definitions.Length + " 件");
        }

        private static void ConfigureItem(int typeId, Item item)
        {
            if (item == null) return;
            Definition def = GetDefinition(typeId);
            if (def == null) return;
            try
            {
                ModeFItemConfigHelper.ClearInheritedUsage(item);
                item.DisplayNameRaw = def.LocKey;
                item.name = def.NameEN;
                item.MaxStackCount = 1;
                item.StackCount = 1;
                item.Value = def.Value;
                item.Quality = def.Quality;
                ModeFItemConfigHelper.SetHiddenMember(item, "weight", def.Weight);
                EquipmentHelper.AddTagToItem(item, "Special");
                if (def.NotSellable) EquipmentHelper.AddTagToItem(item, "NotSellable");
                EquipmentHelperIcon.TryInjectIcon(item, null, def.IconName);
                // 两件都没有使用行为，也不带克隆源的耐久条。
                item.MaxDurability = 0f;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "配置物品失败 " + typeId + ": " + e.Message);
            }
        }

        #endregion

        #region 运行时兜底注册（零新增 bundle）

        private static readonly HashSet<int> _runtimeRegistered = new HashSet<int>();

        /// <summary>从既有物品克隆一个 prefab 顶上（形态照 TidegateItems.EnsureRuntimeRegistration）；供 BossRushDynamicItemRegistry 调用。</summary>
        public static bool EnsureRuntimeRegistration(int typeId)
        {
            try
            {
                if (GetDefinition(typeId) == null) return false;
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(typeId); }
                catch (Exception)
                {
                    // prefab 查询失败按「尚未注册」处理，继续走克隆路径
                }
                if (existing != null) { ConfigureItem(typeId, existing); return true; }
                existing = ItemFactory.GetLoadedItem(typeId);
                if (existing != null)
                {
                    ConfigureItem(typeId, existing);
                    try { ItemAssetsCollection.AddDynamicEntry(existing); }
                    catch (Exception)
                    {
                        // 已在表内时重复登记会抛，忽略即可
                    }
                    return true;
                }
                if (_runtimeRegistered.Contains(typeId))
                {
                    try { return ItemAssetsCollection.GetPrefab(typeId) != null; }
                    catch (Exception) { return false; }
                }
                Item source = FindRuntimeFallbackSource();
                if (source == null) { ModBehaviour.DevLog(LogPrefix + "找不到可克隆的兜底物品: " + typeId); return false; }
                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null) return false;
                clone.gameObject.name = GetDefinition(typeId).LocKey;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);
                clone.SetTypeID(typeId);
                ConfigureItem(typeId, clone);
                ItemAssetsCollection.AddDynamicEntry(clone);
                _runtimeRegistered.Add(typeId);
                ModBehaviour.DevLog(LogPrefix + "运行时兜底物品已注册: " + typeId);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "兜底注册失败 " + typeId + ": " + e.Message);
                return false;
            }
        }

        /// <summary>克隆源与潮闸镇物品同一份顺序：都是零新增 bundle 的小件 Mod 物品，没有专属模型。</summary>
        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds = { BossRushItemIds.RelicEgg, BossRushItemIds.PortableSafeZoneDevice, BossRushItemIds.ZombieTideBeacon, BossRushItemIds.ZombieTideInvitation };
            foreach (int id in fallbackIds)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(id);
                    if (prefab != null) return prefab;
                }
                catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "[WARNING] 查找兜底模板物品失败: " + e.Message); }
            }
            return null;
        }

        #endregion

        #region 本地化与清理

        /// <summary>官方描述固定查询 DisplayNameRaw + "_Desc"，名称和描述一起随语言刷新（挂在 InjectLocalization_Extra_Integration）。</summary>
        public static void InjectLocalization()
        {
            try
            {
                var map = new Dictionary<string, string>();
                foreach (Definition def in Definitions)
                {
                    map[def.LocKey] = L10n.T(def.NameCN, def.NameEN);
                    map[def.LocKey + "_Desc"] = L10n.T(def.DescCN, def.DescEN);
                }
                LocalizationHelper.InjectLocalizations(map);
            }
            catch (Exception e) { ModBehaviour.DevLog(LogPrefix + "本地化注入失败: " + e.Message); }
        }

        internal static void ResetStaticCaches()
        {
            _runtimeRegistered.Clear();
            _configuratorsRegistered = false;
        }

        #endregion
    }
}
