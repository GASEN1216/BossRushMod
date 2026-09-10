// ============================================================================
// SkyIslandItems.cs - 天空岛（晴岚群岛）物品：纪念品、道具与岛上特产
// ============================================================================
// TypeID 500068-500072（台账见 docs/Bossrush使用物品ID表.md 与 AGENTS.md 4.3；常量在 Config/ConfigItemIds.cs）。
//
// 【零新增 bundle】与后山物品同一套克隆注册（形态照 Integration/BackMountain/BackMountainItems.cs）：
// 从既有 mod 物品克隆 prefab、改 TypeID、配属性、AddDynamicEntry；图标读 Assets/Items/<IconName>.png，
// 缺图时保持克隆源的图标，不 fail。
//
// 【三种形态】
//   - 纪念品（晴岚航徽、噬风之核）：不可使用、不可堆叠，只在剧情节点发一次
//     （发放台账在 DebugAndTools/SkyIsland/SkyIslandItemRules.cs，发放记录写进本槽群岛手记）。
//   - 道具（风标罗盘）：使用不消耗（耐久 999，形态照鸭皇图鉴），在岛上指向信鸽或下一个目标。
//   - 岛上特产（归航菜便当、星苔药膏）：可堆叠消耗品，直接复用官方 FoodDrink / Drug 使用行为，不写自定义效果代码；
//     只从天空岛的箱子里出（SkyIslandItemRules.IslandExtraFor）。
// 五件全部登记掉落黑名单（Config/LootBlacklistRegistry.cs + Assets/Data/LootBlacklist.json），不进许愿台、日报与各类品质池。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.ItemUsage;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>天空岛物品的注册、配置、本地化与发放。</summary>
    public static class SkyIslandItems
    {
        private const string LogPrefix = "[SkyIslandItems] ";

        /// <summary>风标罗盘的耐久：使用后不消耗（官方 CA_UseItem 对不可堆叠又没有耐久的物品用完即销毁）。</summary>
        private const float NonConsumableDurability = 999f;

        private enum Kind { Keepsake, Compass, Food, Medicine }

        private sealed class Definition
        {
            internal int TypeId;
            internal Kind Kind;
            internal string LocKey;
            internal string DescCN;
            internal string DescEN;
            internal string IconName;
            internal int Value;
            internal int Quality;
            internal int MaxStack;
            internal float UseTime;
            internal float Energy;
            internal float Water;
            internal int Heal;
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
                Make(BossRushItemIds.SkyIslandHomecomingBadge, Kind.Keepsake, "BossRush_SkyIsland_HomecomingBadge",
                    "晴岚群岛的纪念航徽：铜铃形的徽面上是一朵云和一只小帆船。只有敲响归航钟的人才拿得到——浮舟说，戴着它回来，码头永远留着一条缆绳给你。",
                    "A keepsake badge of the Qinglan isles: a bell-shaped face with a cloud and a little sailboat. Only those who rang the Homecoming Bell receive one — Fuzhou says that if you come back wearing it, the dock will always keep a mooring line for you.",
                    "sky_island_homecoming_badge", 5000, 5, 1, 0f, 0f, 0f, 0),
                Make(BossRushItemIds.SkyIslandWindeaterCore, Kind.Keepsake, "BossRush_SkyIsland_WindeaterCore",
                    "噬风散去时留下的核心，玻璃般的球壳里还锁着一小团打转的风，握在手里能感觉到它轻轻推着掌心。收藏品，也能卖个好价钱。",
                    "The heart the Windeater left behind when it broke apart. A small whirl of wind still turns inside its glassy shell, and you can feel it nudging your palm. A collector's piece that also sells for a good price.",
                    "sky_island_windeater_core", 12000, 6, 1, 0f, 0f, 0f, 0),
                Make(BossRushItemIds.SkyIslandWindVaneCompass, Kind.Compass, "BossRush_SkyIsland_WindVaneCompass",
                    "浮舟的旧罗盘，指针换成了一枚小风标。在晴岚群岛上使用：先指向还没收下的信鸽，再指向当前目标或还没了结的支线，并报出大致距离。使用不消耗；离开群岛它只会乱转。",
                    "Fuzhou's old compass, its needle replaced with a tiny wind vane. Use it on the Qinglan isles: it points to an uncollected carrier pigeon first, then to your current objective or an unfinished side path, with a rough distance. Not consumed on use; away from the isles it just spins.",
                    "sky_island_wind_vane_compass", 1500, 4, 1, 0.6f, 0f, 0f, 0),
                Make(BossRushItemIds.SkyIslandHomecomingBento, Kind.Food, "BossRush_SkyIsland_HomecomingBento",
                    "晴禾装的便当：米饭上铺着归航菜，插着一只纸风车。回复饱食与水分，还能回一点生命。只在天空岛的箱子里找得到。",
                    "A lunch box packed by Qinghe: rice topped with homecoming greens and a little paper pinwheel. Restores energy and water, and a little health. Found only in crates on the Sky Islands.",
                    "sky_island_homecoming_bento", 600, 3, 10, 2.5f, 35f, 20f, 15),
                Make(BossRushItemIds.SkyIslandStarmossSalve, Kind.Medicine, "BossRush_SkyIsland_StarmossSalve",
                    "眠苔用星苔熬的药膏，抹上去凉丝丝的，伤口很快就不疼了。回复生命。只在天空岛的箱子里找得到。",
                    "A salve Miantai boils down from star moss. It goes on cool and the wound stops hurting almost at once. Restores health. Found only in crates on the Sky Islands.",
                    "sky_island_starmoss_salve", 1200, 4, 10, 3f, 0f, 0f, 40)
            };
        }

        private static Definition Make(int typeId, Kind kind, string locKey, string descCN, string descEN, string iconName,
            int value, int quality, int maxStack, float useTime, float energy, float water, int heal)
        {
            return new Definition
            {
                TypeId = typeId, Kind = kind, LocKey = locKey, DescCN = descCN, DescEN = descEN, IconName = iconName,
                Value = value, Quality = quality, MaxStack = maxStack, UseTime = useTime, Energy = energy, Water = water, Heal = heal
            };
        }

        private static Definition GetDefinition(int typeId)
        {
            Definition[] all = Definitions;
            for (int i = 0; i < all.Length; i++)
                if (all[i].TypeId == typeId) return all[i];
            return null;
        }

        #region 注册与配置

        private static bool _configuratorsRegistered;

        /// <summary>幂等注册全部物品配置器。</summary>
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
                item.name = SkyIslandItemRules.NameEn(typeId);
                // 不设就会隐式继承克隆兜底源的堆叠上限。
                item.MaxStackCount = def.MaxStack;
                item.StackCount = 1;
                item.Value = def.Value;
                item.Quality = def.Quality;
                string description = L10n.T(def.DescCN, def.DescEN);
                ModeFItemConfigHelper.SetHiddenMember(item, "description", description);
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", description);
                EquipmentHelper.AddTagToItem(item, "Special");
                EquipmentHelperIcon.TryInjectIcon(item, null, def.IconName);
                switch (def.Kind)
                {
                    case Kind.Compass:
                        item.MaxDurability = NonConsumableDurability;
                        item.Durability = NonConsumableDurability;
                        AttachUsage(item, def.UseTime, Component<SkyIslandCompassUsage>(item));
                        break;
                    case Kind.Food:
                        FoodDrink food = Component<FoodDrink>(item);
                        food.energyValue = def.Energy;
                        food.waterValue = def.Water;
                        Drug snack = Component<Drug>(item);
                        snack.healValue = def.Heal;
                        // 官方 UsageUtilities 只要有一个行为可用就能吃：满血时 Drug 不可用，照样能吃饱喝足。
                        AttachUsage(item, def.UseTime, food, snack);
                        break;
                    case Kind.Medicine:
                        Drug drug = Component<Drug>(item);
                        drug.healValue = def.Heal;
                        AttachUsage(item, def.UseTime, drug);
                        break;
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
        private static void AttachUsage(Item item, float useTime, params UsageBehavior[] behaviors)
        {
            UsageUtilities usage = item.GetComponent<UsageUtilities>();
            if (usage == null) usage = item.gameObject.AddComponent<UsageUtilities>();
            if (usage.behaviors == null) usage.behaviors = new List<UsageBehavior>();
            else usage.behaviors.Clear();
            for (int i = 0; i < behaviors.Length; i++)
                if (behaviors[i] != null && !usage.behaviors.Contains(behaviors[i])) usage.behaviors.Add(behaviors[i]);
            ModeFItemConfigHelper.BindUsageUtilitiesToItem(item, usage, useTime);
        }

        #endregion

        #region 运行时兜底注册（零新增 bundle）

        private static readonly HashSet<int> _runtimeRegistered = new HashSet<int>();

        /// <summary>从既有物品克隆一个 prefab 顶上。形态照 BackMountainItems.EnsureRuntimeRegistration；供 BossRushDynamicItemRegistry 调用。</summary>
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

        #region 发放

        /// <summary>
        /// 把一件天空岛物品发给玩家：<paramref name="toStorage"/> 直接寄回基地仓库，否则先放背包、放不下再寄回（官方 SendToPlayer）。
        /// 实例化之前先问 prefab：缺资源时官方给的空壳带着同一个 TypeID，回读分辨不出来（与天空岛箱子同一条纪律）。
        /// </summary>
        internal static bool TryGive(int typeId, bool toStorage)
        {
            Item item = null;
            try
            {
                if (GetDefinition(typeId) == null || ItemAssetsCollection.GetPrefab(typeId) == null) return false;
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null || item.TypeID != typeId) return false;
                if (toStorage) ItemUtilities.SendToPlayerStorage(item);
                else ItemUtilities.SendToPlayer(item);
                item = null;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "发放物品失败 " + typeId + ": " + e.Message);
                return false;
            }
            finally
            {
                if (item != null)
                {
                    try { item.DestroyTree(); }
                    catch (Exception)
                    {
                        // 没送出去的实例清理失败不影响返回值
                    }
                }
            }
        }

        #endregion

        #region 本地化与清理

        /// <summary>注入全部物品名。DisplayNameRaw 设了就必须注入，否则游戏里会显示 *BossRush_SkyIsland_...*（AGENTS.md 4.4）。</summary>
        public static void InjectLocalization()
        {
            try
            {
                var map = new Dictionary<string, string>();
                Definition[] all = Definitions;
                for (int i = 0; i < all.Length; i++)
                    map[all[i].LocKey] = SkyIslandItemRules.Name(all[i].TypeId);
                LocalizationHelper.InjectLocalizations(map);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "本地化注入失败: " + e.Message);
            }
        }

        internal static void ResetStaticCaches()
        {
            _runtimeRegistered.Clear();
        }

        #endregion
    }
}
