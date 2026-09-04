// ============================================================================
// BackMountainItems.cs - 后山种子与出击餐物品
// ============================================================================
// TypeID 500062-500067（台账见 docs/Bossrush使用物品ID表.md 与 AGENTS.md 4.3）。
//
// 【零新增 bundle】没有专属模型，全部走克隆注册（形态照
// Integration/Items/RelicEggConfig.cs 的 EnsureRuntimeRegistration）：
// 从既有 mod 物品克隆一个 prefab、改 TypeID、配好属性、AddDynamicEntry。
// 图标缺 PNG 时保持克隆源的图标，不 fail。
//
// 【为什么种子和食材要分成六个 TypeID，而不是像遗种蛋那样一个号 + KV】
//   遗种蛋能用 KV 是因为蛋只有一种玩法（孵化），血脉只是个标签。
//   而这里的六件东西在官方系统眼里是**不同的东西**：
//   官方种植系统按 `SeedInfo.itemTypeID` 认种子、按 `CropInfo.resultNormal` 发产物，
//   两边都是 int 而不是带 KV 的实例。共用一个号会让三种作物互相顶掉。
//
// 【出击餐为什么不是普通 Buff 物品】
//   官方 Buff 不跨场景（CharacterBuffManager 没有存档，角色每场景重建）。
//   所以「吃了下一局生效」必须落存档：食用时经 RaidMealService 登记，
//   下一局 LevelInitialized 时再挂 Modifier。详见 RaidMealService。
// ============================================================================

using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>后山物品的 TypeID 台账。与 Config/ConfigItemIds.cs 是同一 partial 类。</summary>
    public static partial class BossRushItemIds
    {
        /// <summary>龙裔之种（龙裔遗族掉落，种出龙息果）。</summary>
        public const int DragonSeed = 500062;

        /// <summary>龙皇焰种（焚天龙皇掉落，种出焚心椒）。</summary>
        public const int EmberSeed = 500063;

        /// <summary>幽魂孢子（幽灵女巫掉落，种出幽影蘑菇）。</summary>
        public const int PhantomSpore = 500064;

        /// <summary>龙息果（出击餐：下一局攻击提升）。</summary>
        public const int DragonFruit = 500065;

        /// <summary>焚心椒（出击餐：下一局移速与换弹提升）。</summary>
        public const int EmberChili = 500066;

        /// <summary>幽影蘑菇（出击餐：下一局减伤）。</summary>
        public const int PhantomMushroom = 500067;
    }

    /// <summary>后山种子与出击餐的物品注册。</summary>
    public static class BackMountainItems
    {
        #region 定义表

        /// <summary>一件后山物品的静态定义。</summary>
        internal sealed class Definition
        {
            internal int TypeId;
            internal string PrefabName;
            internal string LocKey;
            internal string NameCN;
            internal string NameEN;
            internal string DescCN;
            internal string DescEN;
            internal string IconName;
            internal int Value;
            internal int Quality;
            internal bool IsSeed;
        }

        /// <summary>
        /// 六件后山物品的堆叠上限。种子与出击餐都是消耗品，理应可堆叠；
        /// 不显式赋值会继承克隆兜底源（遗种蛋）的不可堆叠设定。
        /// 取值与 <c>Integration/Items/CalmingDropsConfig.cs</c> 等既有可堆叠消耗品一致。
        /// </summary>
        private const int ItemMaxStackCount = 20;

        private static Definition[] _definitions;

        /// <summary>全部后山物品定义。种子在前，出击餐在后。</summary>
        internal static Definition[] Definitions
        {
            get
            {
                if (_definitions == null) _definitions = BuildDefinitions();
                return _definitions;
            }
        }

        private static Definition[] BuildDefinitions()
        {
            return new Definition[]
            {
                Make(BossRushItemIds.DragonSeed, "BossRush_DragonSeed", "BossRush_DragonSeed",
                    "龙裔之种", "Dragon Seed",
                    "从龙裔遗族的余烬里捡到的一粒硬核。种在菜地里能长出龙息果。",
                    "A hard kernel picked from the embers of a fallen Dragon Descendant. "
                    + "Plant it in the garden to grow Dragonbreath Fruit.",
                    "dragon_seed", 900, 4, true),

                Make(BossRushItemIds.EmberSeed, "BossRush_EmberSeed", "BossRush_EmberSeed",
                    "龙皇焰种", "Ember Seed",
                    "焚天龙皇陨落处仍在发烫的种子。种在菜地里能长出焚心椒。",
                    "A seed still warm from where the Ember Dragon King fell. "
                    + "Plant it in the garden to grow Emberheart Chili.",
                    "ember_seed", 1100, 4, true),

                Make(BossRushItemIds.PhantomSpore, "BossRush_PhantomSpore", "BossRush_PhantomSpore",
                    "幽魂孢子", "Phantom Spore",
                    "幽灵女巫散去后飘落的孢子，摸上去是凉的。种在菜地里能长出幽影蘑菇。",
                    "A spore drifting down where the Phantom Witch dissolved. It feels cold to the touch. "
                    + "Plant it in the garden to grow Umbral Mushroom.",
                    "phantom_spore", 1000, 4, true),

                Make(BossRushItemIds.DragonFruit, "BossRush_DragonFruit", "BossRush_DragonFruit",
                    "龙息果", "Dragonbreath Fruit",
                    "咬一口喉咙发烫。出击前吃下，**下一局**的攻击会更狠。效果只持续一局。",
                    "One bite and your throat burns. Eat it before heading out and your **next run** hits harder. "
                    + "Lasts one run only.",
                    "dragon_fruit", 2400, 5, false),

                Make(BossRushItemIds.EmberChili, "BossRush_EmberChili", "BossRush_EmberChili",
                    "焚心椒", "Emberheart Chili",
                    "辣得人坐不住。出击前吃下，**下一局**跑得更快、换弹更利索。效果只持续一局。",
                    "Too hot to sit still. Eat it before heading out and your **next run** is quicker on the "
                    + "feet and the reload. Lasts one run only.",
                    "ember_chili", 2400, 5, false),

                Make(BossRushItemIds.PhantomMushroom, "BossRush_PhantomMushroom", "BossRush_PhantomMushroom",
                    "幽影蘑菇", "Umbral Mushroom",
                    "吃下去像披了层影子。出击前吃下，**下一局**受到的物理伤害更少。效果只持续一局。",
                    "Eating it feels like pulling a shadow over yourself. Eat it before heading out and your "
                    + "**next run** takes less physical damage. Lasts one run only.",
                    "phantom_mushroom", 2400, 5, false)
            };
        }

        private static Definition Make(
            int typeId, string prefabName, string locKey,
            string nameCN, string nameEN, string descCN, string descEN,
            string iconName, int value, int quality, bool isSeed)
        {
            Definition def = new Definition();
            def.TypeId = typeId;
            def.PrefabName = prefabName;
            def.LocKey = locKey;
            def.NameCN = nameCN;
            def.NameEN = nameEN;
            def.DescCN = descCN;
            def.DescEN = descEN;
            def.IconName = iconName;
            def.Value = value;
            def.Quality = quality;
            def.IsSeed = isSeed;
            return def;
        }

        /// <summary>按 TypeID 找定义；不存在返回 null。</summary>
        internal static Definition GetDefinition(int typeId)
        {
            Definition[] all = Definitions;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].TypeId == typeId) return all[i];
            }
            return null;
        }

        /// <summary>种子 TypeID → 对应产出的食材 TypeID。无对应返回 0。</summary>
        internal static int GetHarvestResultFor(int seedTypeId)
        {
            switch (seedTypeId)
            {
                case BossRushItemIds.DragonSeed: return BossRushItemIds.DragonFruit;
                case BossRushItemIds.EmberSeed: return BossRushItemIds.EmberChili;
                case BossRushItemIds.PhantomSpore: return BossRushItemIds.PhantomMushroom;
                default: return 0;
            }
        }

        #endregion

        #region 注册

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
            ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "物品配置器已注册 " + all.Length + " 件");
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
                // 不设 MaxStackCount 就会**隐式继承克隆兜底源**（遗种蛋，不可堆叠）的值，
                // 六件种子/出击餐因此全都一格一个，换个兜底源还会跟着变。
                // 取值与 CalmingDropsConfig 等既有可堆叠消耗品一致。
                item.MaxStackCount = ItemMaxStackCount;
                item.StackCount = 1;
                item.Value = def.Value;
                item.Quality = def.Quality;
                ModeFItemConfigHelper.SetHiddenMember(item, "description", L10n.T(def.DescCN, def.DescEN));
                ModeFItemConfigHelper.SetHiddenMember(item, "DescriptionRaw", L10n.T(def.DescCN, def.DescEN));
                EquipmentHelper.AddTagToItem(item, "Special");
                EquipmentHelperIcon.TryInjectIcon(item, null, def.IconName);

                // 出击餐要能「吃」；种子不挂使用行为——它是给官方种植 UI 用的，
                // 挂上反而会在背包里多出一个没有意义的「使用」按钮。
                if (!def.IsSeed) AttachMealUsage(item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "配置物品失败 " + typeId + ": " + e.Message);
            }
        }

        /// <summary>挂上出击餐的使用行为。形态照 Integration/Items/BrickStoneConfig.cs。</summary>
        private static void AttachMealUsage(Item item)
        {
            try
            {
                UsageUtilities usageUtils = item.GetComponent<UsageUtilities>();
                if (usageUtils == null)
                {
                    usageUtils = item.gameObject.AddComponent<UsageUtilities>();
                    SetUsageUtilitiesMaster(usageUtils, item);
                }

                RaidMealUsageBehavior usage = item.GetComponent<RaidMealUsageBehavior>();
                if (usage == null)
                {
                    usage = item.gameObject.AddComponent<RaidMealUsageBehavior>();
                }

                if (usageUtils.behaviors == null)
                {
                    usageUtils.behaviors = new System.Collections.Generic.List<UsageBehavior>();
                }
                if (!usageUtils.behaviors.Contains(usage))
                {
                    usageUtils.behaviors.Add(usage);
                }

                SetUsageUtilitiesMaster(usageUtils, item);
                SetItemUsageUtilities(item, usageUtils);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "挂载出击餐使用行为失败: " + e.Message);
            }
        }

        /// <summary>UsageUtilities.master 是私有字段，只能反射写。</summary>
        private static void SetUsageUtilitiesMaster(UsageUtilities usageUtils, Item item)
        {
            try
            {
                System.Reflection.FieldInfo masterField = typeof(UsageUtilities).BaseType.GetField(
                    "master",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (masterField != null) masterField.SetValue(usageUtils, item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 回填 UsageUtilities master 失败: " + e.Message);
            }
        }

        /// <summary>Item.usageUtilities 同样是私有字段。</summary>
        private static void SetItemUsageUtilities(Item item, UsageUtilities usageUtils)
        {
            try
            {
                System.Reflection.FieldInfo field = typeof(Item).GetField(
                    "usageUtilities",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null) field.SetValue(item, usageUtils);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 回填物品 UsageUtilities 失败: " + e.Message);
            }
        }

        #endregion

        #region 运行时兜底注册（零新增 bundle）

        private static readonly System.Collections.Generic.HashSet<int> _runtimeRegistered =
            new System.Collections.Generic.HashSet<int>();

        /// <summary>
        /// 从既有物品克隆一个 prefab 顶上。形态照 RelicEggConfig.EnsureRuntimeRegistration。
        /// 供 BossRushDynamicItemRegistry 的 FallbackLoader 调用。
        /// </summary>
        public static bool EnsureRuntimeRegistration(int typeId)
        {
            try
            {
                Definition def = GetDefinition(typeId);
                if (def == null) return false;

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
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "找不到可克隆的兜底物品: " + typeId);
                    return false;
                }

                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null) return false;

                clone.gameObject.name = def.PrefabName;
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);
                clone.SetTypeID(typeId);
                ConfigureItem(typeId, clone);
                ItemAssetsCollection.AddDynamicEntry(clone);
                _runtimeRegistered.Add(typeId);
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "运行时兜底物品已注册: " + typeId);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "兜底注册失败 " + typeId + ": " + e.Message);
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
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "[WARNING] 查找兜底模板物品失败: " + e.Message);
                }
            }
            return null;
        }

        #endregion

        #region 本地化

        /// <summary>
        /// 注入全部物品名。DisplayNameRaw 设了就必须注入，
        /// 否则游戏里会显示 *BossRush_DragonSeed*（AGENTS.md 4.4）。
        /// </summary>
        public static void InjectLocalization()
        {
            try
            {
                System.Collections.Generic.Dictionary<string, string> map =
                    new System.Collections.Generic.Dictionary<string, string>();

                Definition[] all = Definitions;
                for (int i = 0; i < all.Length; i++)
                {
                    map[all[i].LocKey] = L10n.T(all[i].NameCN, all[i].NameEN);
                }

                LocalizationHelper.InjectLocalizations(map);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "本地化注入失败: " + e.Message);
            }
        }

        #endregion

        #region 清理

        internal static void ResetStaticCaches()
        {
            _runtimeRegistered.Clear();
        }

        #endregion
    }
}
