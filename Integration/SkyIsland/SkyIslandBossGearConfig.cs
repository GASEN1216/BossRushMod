// ============================================================================
// SkyIslandBossGearConfig.cs - 天空岛头目 / 岛主的专属装备（500086-500089）
// ============================================================================
// 真实资源走 EquipmentFactory 主管线：Assets/Equipment/skyisland_boss_gear 里的 {名}_{类型}_Item / _Model
// （命名规则见 EquipmentFactory；基名在 SkyIslandBossRules.GearSpecs，发布后不改）。
//
// bundle 缺失时 BossRushDynamicItemRegistry 调 EnsureRegistered：克隆一件**同槽位的官方装备**顶上
// （模型本来就对齐官方挂点，不会像克隆道具那样把图标模型挂到头上），再由本配置器补齐名字、品质、数值、
// 耐久、价值与图标。玩家背包与仓库里存着这些 TypeID，漏兜底会让重启后它们退化成官方 FallbackItem。
//
// 单一事实源：数值只在 SkyIslandBossRules.GearSpecs，价值与中英名只在 SkyIslandItemRules，本文件不另写数字。
// 唯一来源：头目 / 岛主身上——每次穿全套、死后按权重只留一件（SkyIslandBossLoot）；四件全部登记掉落黑名单。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    public static class SkyIslandBossGearConfig
    {
        private const string LogPrefix = "[SkyIslandBossGear] ";

        /// <summary>官方物品 TypeID 都远小于这个数；克隆兜底只找官方装备（模型对齐官方挂点）。</summary>
        private const int OfficialTypeIdCeiling = 100000;

        private static readonly HashSet<int> placeholders = new HashSet<int>();

        /// <summary>EquipmentFactory 主路径：bundle 里的 Item 预制体按基名配置。</summary>
        public static bool TryConfigure(Item item, string baseName)
        {
            if (item == null || string.IsNullOrEmpty(baseName)) return false;
            SkyIslandBossGearSpec[] specs = SkyIslandBossRules.GearSpecs;
            for (int i = 0; i < specs.Length; i++)
            {
                if (!baseName.Equals(specs[i].ModelBaseName, StringComparison.OrdinalIgnoreCase)) continue;
                Configure(item, specs[i], false);
                return true;
            }
            return false;
        }

        /// <summary>已注册 prefab 或占位克隆按 TypeID 重配（幂等：modifier 用 EnsureModifierOnItem）。</summary>
        public static bool TryConfigureByTypeId(Item item)
        {
            if (item == null) return false;
            SkyIslandBossGearSpec spec = SkyIslandBossRules.GearSpec(item.TypeID);
            if (spec == null) return false;
            Configure(item, spec, true);
            return true;
        }

        /// <summary>ItemFactory 配置器：官方按 TypeID 实例化（尸体箱、仓库、读档）时也补齐配置。挂在 ItemContentRegistry。</summary>
        public static void RegisterConfigurators()
        {
            int[] all = SkyIslandBossRules.AllGearTypeIds;
            for (int i = 0; i < all.Length; i++)
                ItemFactory.RegisterConfigurator(all[i], delegate(Item item) { TryConfigureByTypeId(item); });
            ModBehaviour.DevLog(LogPrefix + "物品配置器已注册 " + all.Length + " 件");
        }

        private static void Configure(Item item, SkyIslandBossGearSpec spec, bool bindLoadedModel)
        {
            try
            {
                item.SetTypeID(spec.TypeId);
                item.DisplayNameRaw = spec.LocKey;
                item.Quality = spec.Quality;
                // 先写上限再写当前值：Durability 的 setter 会钳到 MaxDurability。
                if (spec.Durability > 0f)
                {
                    item.MaxDurability = spec.Durability;
                    item.Durability = spec.Durability;
                }
                else if (item.UseDurability)
                {
                    // 克隆源若自带耐久（官方背包），清掉 Variables 之后当前值是 0：补满，别让它一出生就是坏的。
                    item.Durability = item.MaxDurability;
                }
                item.MaxStackCount = 1;
                if (item.StackCount <= 0) item.StackCount = 1;
                EquipmentHelper.AddTagToItem(item, spec.Slot);
                EquipmentHelper.EnsureModifierOnItem(item, spec.StatKey, ModifierType.Add, spec.StatValue, true);
                // 不设 Value，NPC 商店标价 0（contracts §7.1）。
                item.Value = SkyIslandItemRules.ValueOf(spec.TypeId);
                // 官方 Item.Repairable = UseDurability && Tags.Contains("Repairable")：不打这个标签维修台会显示「无法维修」。
                if (spec.Durability > 0f) EquipmentHelper.AddRepairableTag(item);
                EquipmentHelperIcon.TryInjectIcon(item, SkyIslandBossRules.GearBundle, spec.IconName);
                if (bindLoadedModel) EquipmentFactory.TryBindLoadedEquipmentModel(item, spec.ModelBaseName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "配置失败 " + spec.TypeId + ": " + e.Message);
            }
        }

        /// <summary>
        /// 按 TypeID 确保这件专属装备已注册（供 BossRushDynamicItemRegistry 的 FallbackLoader 调用）。
        /// 已经有 prefab（bundle 已加载）就只重配；否则克隆同槽位的官方装备当占位。
        /// </summary>
        public static bool EnsureRegistered(int typeId)
        {
            SkyIslandBossGearSpec spec = SkyIslandBossRules.GearSpec(typeId);
            if (spec == null) return false;
            try
            {
                Item existing = null;
                try { existing = ItemAssetsCollection.GetPrefab(typeId); }
                catch (Exception)
                {
                    // prefab 查询失败按「尚未注册」处理，继续走克隆路径
                    existing = null;
                }
                if (existing != null)
                {
                    Configure(existing, spec, true);
                    return true;
                }
                if (placeholders.Contains(typeId)) return false;

                Item source = FindOfficialSource(spec.Slot);
                if (source == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "找不到同槽位的官方装备当克隆源，跳过: " + spec.Slot + " / " + typeId);
                    return false;
                }
                Item clone = UnityEngine.Object.Instantiate(source);
                if (clone == null || clone.gameObject == null) return false;
                clone.gameObject.name = spec.ModelBaseName + "_Placeholder";
                clone.gameObject.SetActive(false);
                clone.gameObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(clone.gameObject);
                clone.SetTypeID(typeId);
                // 清掉官方源继承下来的属性修饰与显示变量，否则占位件会带着官方那件的护甲、负重等全部数值。
                try { if (clone.Modifiers != null) clone.Modifiers.Clear(); }
                catch (Exception)
                {
                    // 清理失败时由 EnsureModifierOnItem 保证至少有本件自己的那一条
                }
                try { clone.Variables.Clear(); }
                catch (Exception)
                {
                    // 同上，耐久由配置器重写
                }
                Configure(clone, spec, true);
                ItemAssetsCollection.AddDynamicEntry(clone);
                placeholders.Add(typeId);
                ModBehaviour.DevLog(LogPrefix + "占位装备已注册: " + typeId + "（克隆源 " + source.TypeID + "）");
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "占位注册失败 " + typeId + ": " + e.Message);
                return false;
            }
        }

        /// <summary>全部专属装备的预注册（EquipmentFactory.LoadAllEquipment 之后调用；幂等）。</summary>
        public static void EnsureAllRegistered()
        {
            int[] ids = SkyIslandBossRules.AllGearTypeIds;
            for (int i = 0; i < ids.Length; i++) EnsureRegistered(ids[i]);
        }

        /// <summary>同槽位标签（Helmat / Armor / Backpack）的第一件官方装备，按 TypeID 排序保证跨机器稳定。</summary>
        private static Item FindOfficialSource(string slotTag)
        {
            try
            {
                GameplayDataSettings.TagsData tags = GameplayDataSettings.Tags;
                if (tags == null || tags.AllTags == null) return null;
                Tag slot = null;
                foreach (Tag tag in tags.AllTags)
                {
                    if (tag != null && tag.name == slotTag) { slot = tag; break; }
                }
                if (slot == null) return null;
                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new[] { slot };
                filter.excludeTags = new Tag[0];
                filter.minQuality = 0;
                filter.maxQuality = 8;
                filter.caliber = string.Empty;
                int[] ids = ItemAssetsCollection.GetAllTypeIds(filter);
                if (ids == null || ids.Length == 0) return null;
                Array.Sort(ids);
                for (int i = 0; i < ids.Length; i++)
                {
                    if (ids[i] <= 0 || ids[i] >= OfficialTypeIdCeiling) continue;
                    Item prefab = ItemAssetsCollection.GetPrefab(ids[i]);
                    if (prefab != null) return prefab;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 查找官方同槽装备失败: " + e.Message);
            }
            return null;
        }

        /// <summary>注入四件的名字与描述（挂在 EquipmentLocalization.InjectAllEquipmentLocalizations 里；语言在取用时解析）。</summary>
        public static void InjectLocalization()
        {
            try
            {
                SkyIslandBossGearSpec[] specs = SkyIslandBossRules.GearSpecs;
                for (int i = 0; i < specs.Length; i++)
                {
                    int typeId = specs[i].TypeId;
                    string name = SkyIslandItemRules.Name(typeId);
                    string description = Description(typeId);
                    LocalizationHelper.InjectLocalization(specs[i].LocKey, name);
                    LocalizationHelper.InjectLocalization(specs[i].LocKey + "_Desc", description);
                    LocalizationHelper.InjectLocalization("Item_" + typeId, name);
                    LocalizationHelper.InjectLocalization("Item_" + typeId + "_Desc", description);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "本地化注入失败: " + e.Message);
            }
        }

        /// <summary>玩家看得到的描述：从哪来、穿上做什么、串到岛上的哪条线（卖钱不算用处）。</summary>
        internal static string Description(int typeId)
        {
            switch (typeId)
            {
                case BossRushItemIds.SkyIslandStarbrassVisorHelm:
                    return L10n.T(
                        "残星匠首戴的铜盔：顶上铆着一圈星盘环，额前翻着一片烟琥珀色的焊光面罩。头部护甲 +3。在晴岚群岛上，与星炉背甲或星炉背囊任意两件一起穿，渡口工台的配方少耗 1 片残铜片（至少还要 1 片）。只从残星工坊的岛主身上得到：它每次都穿着全套，倒下时只留下其中一件。它戴着这顶盔时星焰落三处，盔被爆头打穿之后只落一处。",
                        "The brass helm the Starforge Foreman wears: an astrolabe ring riveted on top and a smoked-amber welding visor flipped up at the brow. Head armor +3. On the Qinglan isles, wearing any two Starworks pieces together (with the Starfurnace Harness or Pack) makes dock workbench recipes take one less brass scrap (never below one). Only from the island lord of the Fallen Star Workshop: it always wears the full set and leaves one piece behind when it falls. While it wears this helm its starfire lands in three rings; shoot the helm through and only one lands.");
                case BossRushItemIds.SkyIslandStarfurnaceHarness:
                    return L10n.T(
                        "层层黄铜板铆在厚帆布围裙上，胸口嵌着一只压力表，下摆熏得发黑。身体护甲 +3。在晴岚群岛上，与星铜护目盔或星炉背囊任意两件一起穿，渡口工台的配方少耗 1 片残铜片（至少还要 1 片）。只从残星工坊的岛主身上得到：它的星炉供能桩就接在这件背甲的接头上，背甲被打穿之后桩只能给它一半的护甲。",
                        "Layered brass plates riveted onto a heavy canvas apron, a pressure gauge set in the chest, the hem blackened by soot. Body armor +3. On the Qinglan isles, wearing any two Starworks pieces together (with the Starbrass Visor Helm or Starfurnace Pack) makes dock workbench recipes take one less brass scrap (never below one). Only from the island lord of the Fallen Star Workshop: its furnace pylons feed through this harness's couplings, and once the harness is shot through the pylons give it only half their plating.");
                case BossRushItemIds.SkyIslandStarfurnacePack:
                    return L10n.T(
                        "背在身后的一台小星炉：矮胖的铜锅炉、两根短烟囱、底下捆着一卷工具。背包容量 +6。在晴岚群岛上，与星铜护目盔或星炉背甲任意两件一起穿，渡口工台的配方少耗 1 片残铜片（至少还要 1 片）。只从残星工坊的岛主身上得到：它每放两次星焰，这台炉子就过热一次，那几秒它停手、挨打更疼。",
                        "A small star furnace worn on the back: a squat brass boiler, two stubby chimneys and a tool roll strapped underneath. Backpack capacity +6. On the Qinglan isles, wearing any two Starworks pieces together (with the Starbrass Visor Helm or Starfurnace Harness) makes dock workbench recipes take one less brass scrap (never below one). Only from the island lord of the Fallen Star Workshop: every second starfire makes this furnace overheat, and for those few seconds the Foreman stops and takes harder hits.");
                case BossRushItemIds.SkyIslandStargazerLensHelm:
                    return L10n.T(
                        "瞭台观星手的皮帽，右眼前挂着一副三层伸缩镜片的铜目镜，侧边别着一卷星图。头部护甲 +2。在晴岚群岛上戴着它站定 2 秒，40 米内的敌人会被标出来。只从残星瞭台的头目身上得到：它倒下时有三成机会留下这顶盔；镜片被爆头打穿之前，它会从瞭台上远远标记你。",
                        "The overlook stargazer's leather cap, a three-lens telescoping brass eyepiece hanging over the right eye and a star chart tucked at the side. Head armor +2. On the Qinglan isles, stand still for 2 seconds while wearing it and enemies within 40 m are marked. Only from the chief of the Starfall Overlook: it leaves this helm behind three times in ten; until its lens is shot through, it marks you from the platform at range.");
                default:
                    return string.Empty;
            }
        }

        internal static void ResetStaticCaches()
        {
            placeholders.Clear();
        }
    }
}
