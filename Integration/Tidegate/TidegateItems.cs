// ============================================================================
// TidegateItems.cs - 潮闸镇物品：阀门手轮、潮汐怀表、防水袋、鸭蹼脚掌、闸伯的信
// ============================================================================
// TypeID 500111-500115（台账见 docs/reference/Bossrush使用物品ID表.md、docs/contracts.md §1 与 AGENTS.md §4.3；
// 常量在 Config/ConfigItemIds.cs 的 BossRushItemIds）。
//
// 【零新增 bundle】与天空岛物品同一套克隆注册（形态照 Integration/SkyIsland/SkyIslandItems.cs）：
// 从既有 Mod 物品克隆 prefab、改 TypeID、配属性、AddDynamicEntry；图标读 Assets/Items/<IconName>.png，
// 缺图时保持克隆源的图标，不 fail。
//
// 【本文件只管物品本体】定义、配置、注册、本地化与数值。获取途径（挖沙船船舱、所长的保险柜、闸伯的谢礼与商店、
// 滩涂埋货、阿稳快递）和局内效果（潮位站阀门、涨落倒计时、游泳负重上限、游速）都在 Tidegate/ 的运行时里接：
//   - 「带在身上」统一按背包顶层数：ItemFactory.GetItemCountInInventory(typeId) > 0（天空岛航徽、噬风之核同一口径）。
//   - 闸伯的信是唯一有使用行为的一件（TidegateLetterUsage，使用不消耗），读信面板与解锁航线在 TidegateStory.ReadLetter。
//
// 【数值口径】官方物品价值带（离线解码 1569 件，天空岛池口径）：1–3 品质带中位 500、均值 842；2–5 带中位 810、
// 均值 2116；4–8 带中位 2400、均值 7196。天空岛同类：晴岚航徽（纪念品）5000 / 品质 5，风标罗盘 1500 / 品质 4。
//   - 阀门手轮 800 / 品质 2：任务零件，拿去潮位站就装掉了，卖价落在低品质带均值附近，不当换钱的路子。
//   - 潮汐怀表 6000 / 品质 5：剧情纪念品，比晴岚航徽略贵。金库最深处的保险柜每趟放一块，但身上已经带着一块时不放，
//     所以它是「三阀一闸 + 等低潮」这条主线的回报，不是白拿的第二块；交给闸伯之后想带着看潮，再打一趟金库。
//   - 防水袋 2500 / 品质 4、鸭蹼脚掌 4000 / 品质 4：带在身上才生效的随身装备，闸伯商店有售；
//     鸭蹼更难得（滩涂埋货稀有掉落），定价高一档。两者都在 4–8 带中位与均值之间。
//   - 闸伯的信 50 / 品质 1：剧情钥匙，不可出售（NotSellable）；送过没读又弄丢了，阿稳每个游戏日最多补寄一封。
// 五件全部登记掉落黑名单（Config/LootBlacklistRegistry.cs + Assets/Data/LootBlacklist.json），不进许愿台、日报与各类品质池。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.ItemUsage;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>潮闸镇物品的注册、配置与本地化。获取途径与局内效果由 Tidegate/ 的运行时接线。</summary>
    public static class TidegateItems
    {
        private const string LogPrefix = "[TidegateItems] ";

        /// <summary>闸伯的信的耐久：读了不消耗（官方 CA_UseItem 对不可堆叠又没有耐久的物品用完即销毁），形态照风标罗盘。</summary>
        private const float NonConsumableDurability = 999f;

        private sealed class Definition
        {
            internal int TypeId;
            internal string LocKey;
            internal string NameCN;
            internal string NameEN;
            internal string DescCN;
            internal string DescEN;
            internal string IconName;
            internal int Value;
            internal int Quality;
            internal float Weight;
            /// <summary>大于 0 才挂使用行为（只有闸伯的信），值是读条秒数。</summary>
            internal float UseTime;
        }

        private static Definition[] _definitions;

        private static Definition[] Definitions
        {
            get
            {
                if (_definitions == null) _definitions = BuildDefinitions();
                return _definitions;
            }
        }

        private static Definition[] BuildDefinitions()
        {
            return new[]
            {
                Make(BossRushItemIds.TidegateHandwheel, "BossRush_Tidegate_Handwheel", "阀门手轮", "Valve Handwheel",
                    "潮位站阀门上的铁手轮，被淘沙帮拆下来，扔在挖沙船的船舱里。\n拿到潮位站装回去，就能拧开第三个阀门。装上以后手轮留在阀门上。",
                    "The iron handwheel off the tide gauge station valve. The sand gang pulled it off and dumped it in their dredger's hold.\nFit it back at the tide gauge station to open the third valve. It stays on the valve once fitted.",
                    "tidegate_handwheel", 800, 2, 1.5f, 0f),
                Make(BossRushItemIds.TidegateTideWatch, "BossRush_Tidegate_TideWatch", "潮汐怀表", "Tide Pocket Watch",
                    "储蓄所所长锁在保险柜里的老怀表。表盘外面多一圈潮位刻度，是闸伯一刀一刀刻上去的。\n带进潮闸镇，能看准下一次涨潮、落潮还有多久。\n闸伯一直想把它要回去。",
                    "The old pocket watch the bank manager kept locked in his safe. An extra ring of tide marks runs around the dial; Old Zha cut every one by hand.\nCarry it into Tidegate to see exactly how long until the tide turns.\nOld Zha has always wanted it back.",
                    "tidegate_tide_watch", 6000, 5, 0.2f, 0f),
                Make(BossRushItemIds.TidegateDryBag, "BossRush_Tidegate_DryBag", "防水袋", "Dry Bag",
                    "卷口的防水袋，东西塞进去扎紧，泡在水里也进不了水。\n带在身上：在潮闸镇游泳时，背着游的负重上限多 6 公斤。",
                    "A roll-top dry bag. Pack it, roll it shut, and the water stays out.\nCarry it: in Tidegate you can swim with 6 kg more load before you sink.",
                    "tidegate_dry_bag", 2500, 4, 0.3f, 0f),
                Make(BossRushItemIds.TidegateFlippers, "BossRush_Tidegate_Flippers", "鸭蹼脚掌", "Duck Flippers",
                    "套在脚上的橡胶蹼，上了岸走路啪嗒啪嗒响。\n带在身上：在潮闸镇游得更快。",
                    "Rubber webbing that straps over your feet. Slaps on dry land.\nCarry it: you swim faster in Tidegate.",
                    "tidegate_flippers", 4000, 4, 0.6f, 0f),
                Make(BossRushItemIds.TidegateLetter, "BossRush_Tidegate_Letter", "闸伯的信", "Old Zha's Letter",
                    "阿稳转来的湿包裹里夹着的一封信，字被水泡得有点花。\n拿出来读一读，就知道闸伯的竹筏停在哪。读完信还在，随时能再看。",
                    "A letter tucked in the soggy parcel Awen passed along. The ink has run a little.\nRead it to find out where Old Zha's raft is tied up. Reading doesn't use it up.",
                    "tidegate_letter", 50, 1, 0.05f, 1f),
            };
        }

        private static Definition Make(int typeId, string locKey, string nameCN, string nameEN, string descCN, string descEN,
            string iconName, int value, int quality, float weight, float useTime)
        {
            return new Definition
            {
                TypeId = typeId, LocKey = locKey, NameCN = nameCN, NameEN = nameEN, DescCN = descCN, DescEN = descEN,
                IconName = iconName, Value = value, Quality = quality, Weight = weight, UseTime = useTime
            };
        }

        private static Definition GetDefinition(int typeId)
        {
            Definition[] all = Definitions;
            for (int i = 0; i < all.Length; i++)
                if (all[i].TypeId == typeId) return all[i];
            return null;
        }

        /// <summary>是不是潮闸镇的五件物品之一。</summary>
        internal static bool IsTidegateItem(int typeId)
        {
            return GetDefinition(typeId) != null;
        }

        /// <summary>物品名（按当前语言），给接线方的提示与对话用；不是潮闸镇物品时返回空串。</summary>
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
            Definition[] all = Definitions;
            for (int i = 0; i < all.Length; i++)
            {
                int typeId = all[i].TypeId;
                ItemFactory.RegisterConfigurator(typeId, delegate(Item item) { ConfigureItem(typeId, item); });
            }
            ModBehaviour.DevLog(LogPrefix + "物品配置器已注册 " + all.Length + " 件");
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
                // 五件都不可堆叠；不设就会隐式继承克隆兜底源的堆叠上限。
                item.MaxStackCount = 1;
                item.StackCount = 1;
                item.Value = def.Value;
                item.Quality = def.Quality;
                ModeFItemConfigHelper.SetHiddenMember(item, "weight", def.Weight);
                EquipmentHelper.AddTagToItem(item, "Special");
                // 闸伯的信是剧情钥匙：读了才开航线，不让顺手卖给商人（口径同鸭皇图鉴；丢了还有补寄，见 TidegateStory.TryDeliverLetter）。
                if (typeId == BossRushItemIds.TidegateLetter) EquipmentHelper.AddTagToItem(item, "NotSellable");
                EquipmentHelperIcon.TryInjectIcon(item, null, def.IconName);
                if (def.UseTime > 0f)
                {
                    // 闸伯的信：读了不消耗（耐久同风标罗盘；ClearInheritedUsage 已关掉官方按次扣耐久）。
                    item.MaxDurability = NonConsumableDurability;
                    item.Durability = NonConsumableDurability;
                    AttachUsage(item, def.UseTime, Component<TidegateLetterUsage>(item));
                }
                else
                {
                    // 其余四件没有使用行为，也不带克隆源的耐久条。
                    item.MaxDurability = 0f;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "配置物品失败 " + typeId + ": " + e.Message);
            }
        }

        private static T Component<T>(Item item) where T : Component
        {
            T existing = item.GetComponent<T>();
            return existing != null ? existing : item.gameObject.AddComponent<T>();
        }

        /// <summary>挂使用行为：克隆源带过来的行为先清掉，再挂本物品自己的；私有成员绑定复用 ModeFItemConfigHelper。</summary>
        private static void AttachUsage(Item item, float useTime, UsageBehavior behavior)
        {
            UsageUtilities usage = item.GetComponent<UsageUtilities>();
            if (usage == null) usage = item.gameObject.AddComponent<UsageUtilities>();
            if (usage.behaviors == null) usage.behaviors = new List<UsageBehavior>();
            else usage.behaviors.Clear();
            if (behavior != null) usage.behaviors.Add(behavior);
            ModeFItemConfigHelper.BindUsageUtilitiesToItem(item, usage, useTime);
        }

        #endregion

        #region 运行时兜底注册（零新增 bundle）

        private static readonly HashSet<int> _runtimeRegistered = new HashSet<int>();

        /// <summary>从既有物品克隆一个 prefab 顶上。形态照 SkyIslandItems.EnsureRuntimeRegistration；供 BossRushDynamicItemRegistry 调用。</summary>
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
                if (existing != null)
                {
                    ConfigureItem(typeId, existing);
                    return true;
                }

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
                if (source == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "找不到可克隆的兜底物品: " + typeId);
                    return false;
                }

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

        /// <summary>克隆源与天空岛物品同一份顺序：都是零新增 bundle 的小件 Mod 物品，没有专属模型。</summary>
        private static Item FindRuntimeFallbackSource()
        {
            int[] fallbackIds =
            {
                BossRushItemIds.RelicEgg,
                BossRushItemIds.PortableSafeZoneDevice,
                BossRushItemIds.ZombieTideBeacon,
                BossRushItemIds.ZombieTideInvitation,
            };
            for (int i = 0; i < fallbackIds.Length; i++)
            {
                try
                {
                    Item prefab = ItemAssetsCollection.GetPrefab(fallbackIds[i]);
                    if (prefab != null) return prefab;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LogPrefix + "[WARNING] 查找兜底模板物品失败: " + e.Message);
                }
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
                Definition[] all = Definitions;
                for (int i = 0; i < all.Length; i++)
                {
                    map[all[i].LocKey] = L10n.T(all[i].NameCN, all[i].NameEN);
                    map[all[i].LocKey + "_Desc"] = L10n.T(all[i].DescCN, all[i].DescEN);
                }
                LocalizationHelper.InjectLocalizations(map);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "本地化注入失败: " + e.Message);
            }
        }

        /// <summary>Integration 运行时模块销毁时调用：下次装载要重新登记配置器与兜底注册。</summary>
        internal static void ResetStaticCaches()
        {
            _runtimeRegistered.Clear();
            _configuratorsRegistered = false;
        }

        #endregion
    }
}
