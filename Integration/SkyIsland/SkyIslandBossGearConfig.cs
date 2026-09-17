// ============================================================================
// SkyIslandBossGearConfig.cs - 天空岛头目 / 岛主的专属装备（R1 500086-500089，R2–R4 500090-500102）
// ============================================================================
// 真实资源走 EquipmentFactory 主管线：Assets/Equipment/skyisland_boss_gear 里的 {名}_{类型}_Item / _Model
// （命名规则见 EquipmentFactory；基名在 SkyIslandBossRules.GearSpecs，发布后不改）。
//
// bundle 缺失时 BossRushDynamicItemRegistry 调 EnsureRegistered：克隆一件**同槽位的官方装备**顶上
// （模型本来就对齐官方挂点，不会像克隆道具那样把图标模型挂到头上），再由本配置器补齐名字、品质、数值、
// 耐久、价值与图标。玩家背包与仓库里存着这些 TypeID，漏兜底会让重启后它们退化成官方 FallbackItem。
//
// 单一事实源：数值只在 SkyIslandBossRules.GearSpecs，价值与中英名只在 SkyIslandItemRules，本文件不另写数字。
// 唯一来源：头目 / 岛主身上——每次穿全套、死后按权重只留一件（SkyIslandBossLoot）；全部登记掉落黑名单。
// 槽位有头盔 / 护甲 / 背包 / 面罩（FaceMask）/ 耳机（Headset）五种：克隆兜底按槽位标签名找官方同槽件，五种都认。
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

        /// <summary>同槽位标签（Helmat / Armor / Backpack / FaceMask / Headset）的第一件官方装备，按 TypeID 排序保证跨机器稳定。</summary>
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
                        "匠首的铜盔，面罩熏成了琥珀色。\n头部护甲 +3。\n群岛效果：星工套任穿2件，渡口配方少耗1片残铜，最低仍需1片。\n来源：残星工坊的残星匠首，整套随机留1件。\n打穿它的头盔，星焰落点由3处减为1处。",
                        "The Foreman's helm, its visor stained amber with soot.\nHead armor +3.\nOn Qinglan: any 2 Starworks pieces save 1 brass scrap per dock recipe, minimum 1 scrap.\nSource: Starforge Foreman, Fallen Star Workshop. Drops 1 set piece.\nBreaking its helm reduces starfire rings from 3 to 1.");
                case BossRushItemIds.SkyIslandStarfurnaceHarness:
                    return L10n.T(
                        "铜板围裙的下摆，怎么擦都带着烟灰。\n身体护甲 +3。\n群岛效果：星工套任穿2件，渡口配方少耗1片残铜，最低仍需1片。\n来源：残星工坊的残星匠首。\n打穿它的背甲，供能桩提供的护甲减半。",
                        "A brass-plated apron with soot ground into the hem.\nBody armor +3.\nOn Qinglan: any 2 Starworks pieces save 1 brass scrap per dock recipe, minimum 1 scrap.\nSource: Starforge Foreman, Fallen Star Workshop.\nBreaking its harness halves the armor its pylons provide.");
                case BossRushItemIds.SkyIslandStarfurnacePack:
                    return L10n.T(
                        "背得走的小星炉，底下还捆着工具卷。\n背包容量 +6。\n群岛效果：星工套任穿2件，渡口配方少耗1片残铜，最低仍需1片。\n来源：残星工坊的残星匠首。\n它每放2次星焰就过热，停手时更容易受伤。",
                        "A portable star furnace with tools strapped underneath.\nBackpack capacity +6.\nOn Qinglan: any 2 Starworks pieces save 1 brass scrap per dock recipe, minimum 1 scrap.\nSource: Starforge Foreman, Fallen Star Workshop.\nIt overheats every 2 starfires, stops attacking and takes more damage.");
                case BossRushItemIds.SkyIslandStargazerLensHelm:
                    return L10n.T(
                        "皮帽上装了三层镜片，看人比看星还清楚。\n头部护甲 +2。\n群岛效果：戴着站定2秒，标出40米内的敌人。\n来源：残星瞭台的观星手，掉落率30%。\n打穿它的镜盔，可阻止远距离标记。",
                        "Three lenses on a leather cap. Better for spotting people than stars.\nHead armor +2.\nOn Qinglan: stand still for 2s to mark enemies within 40m.\nSource: Stargazer, Starfall Overlook. Drop chance: 30%.\nBreaking its helm stops its ranged marks.");
                case BossRushItemIds.SkyIslandRootweaveMask:
                    return L10n.T(
                        "树根编的面罩，缝里还长着苔。\n头部护甲 +1。\n群岛效果：悬根套任穿2件，搜到便当、药膏、罗盘的概率翻倍。\n来源：悬根林的悬根猎首，整套随机留1件。\n打穿它的面罩，根洞预警由1秒延长至2秒。",
                        "A woven-root mask with moss in the seams.\nHead armor +1.\nOn Qinglan: any 2 Rootweave pieces double the chance of bentos, salves and compasses in crates.\nSource: Hanging-Root Huntmaster, Hanging Root Wood. Drops 1 set piece.\nBreaking its mask extends root-hollow warnings from 1s to 2s.");
                case BossRushItemIds.SkyIslandVinewovenCuirass:
                    return L10n.T(
                        "硬树皮和粗藤缠成的甲，腰带磨得发亮。\n身体护甲 +3。\n群岛效果：悬根套任穿2件，搜到岛上特产的概率翻倍。\n来源：悬根林的悬根猎首。\n打穿它的藤编甲，绊索减速减半。",
                        "Bark and vines bound into armor, with a worn leather belt.\nBody armor +3.\nOn Qinglan: any 2 Rootweave pieces double the chance of local goods in crates.\nSource: Hanging-Root Huntmaster, Hanging Root Wood.\nBreaking its cuirass halves the tripwire slow.");
                case BossRushItemIds.SkyIslandHangrootQuiver:
                    return L10n.T(
                        "老树根掏成的箭囊，装的却是绊索桩。\n背包容量 +6。\n群岛效果：悬根套任穿2件，搜到岛上特产的概率翻倍。\n来源：悬根林的悬根猎首。",
                        "A hollow root quiver filled with tripwire stakes.\nBackpack capacity +6.\nOn Qinglan: any 2 Rootweave pieces double the chance of local goods in crates.\nSource: Hanging-Root Huntmaster, Hanging Root Wood.");
                case BossRushItemIds.SkyIslandOldMailbag:
                    return L10n.T(
                        "邮包里塞满信封，一封也没写地址。\n背包容量 +4。\n群岛效果：背着上岛，信鸽每趟多送1封信。\n来源：倒挂邮亭的截信人，掉落率30%。\n它会贴身抢岛上耗材，生命低于40%时丢下赃物。",
                        "Envelopes fill the mailbag. None has an address.\nBackpack capacity +4.\nOn Qinglan: wear it for 1 extra pigeon letter per raid.\nSource: Waylayer, Upturned Post Hut. Drop chance: 30%.\nIt steals island supplies up close and drops them below 40% HP.");
                case BossRushItemIds.SkyIslandGreenearStrawHat:
                    return L10n.T(
                        "帽带里插着青穗，草檐已经磨白了。\n头部护甲 +2。\n群岛效果：穗镰套任穿2件，采青穗草多得1份。\n来源：青穗梯田的穗镰，整套随机留1件。\n打穿它的斗笠，开闸泥地由3块减为1块。",
                        "Green ears tucked into the band of a worn straw hat.\nHead armor +2.\nOn Qinglan: any 2 Grain Sickle pieces give 1 extra greenear per harvest.\nSource: Grain Sickle, Green Terraces. Drops 1 set piece.\nBreaking its hat reduces flood patches from 3 to 1.");
                case BossRushItemIds.SkyIslandStrawRaincoat:
                    return L10n.T(
                        "蓑草盖住铜片，晃动时能听到轻响。\n身体护甲 +3。\n群岛效果：穗镰套任穿2件，采青穗草多得1份。\n来源：青穗梯田的穗镰。\n打穿它的蓑衣甲，镰扫预警由0.9秒延长至1.6秒。",
                        "Brass plates rattle under layers of straw.\nBody armor +3.\nOn Qinglan: any 2 Grain Sickle pieces give 1 extra greenear per harvest.\nSource: Grain Sickle, Green Terraces.\nBreaking its armor extends sweep warnings from 0.9s to 1.6s.");
                case BossRushItemIds.SkyIslandGrainSack:
                    return L10n.T(
                        "鼓鼓的谷袋，边上拴着木瓢和镰刀。\n背包容量 +7。\n群岛效果：穗镰套任穿2件，采青穗草多得1份。\n来源：青穗梯田的穗镰。\n它首次开闸时会召来谷仓里还活着的帮手。",
                        "A full grain sack with a scoop and sickle tied to the side.\nBackpack capacity +7.\nOn Qinglan: any 2 Grain Sickle pieces give 1 extra greenear per harvest.\nSource: Grain Sickle, Green Terraces.\nIts first flood calls any surviving barn hands.");
                case BossRushItemIds.SkyIslandRainhushEarmuffs:
                    return L10n.T(
                        "铜耳罩衬着厚毛毡，壳上刻着一滴雨。\n听觉 +0.5。\n群岛效果：延长头目预警，听雨人每16次枪声才引发落石。\n来源：听雨洞的听雨人，掉落率30%。\n打穿它的耳罩，可阻止枪声触发落石。",
                        "Thick felt lines brass cups engraved with a raindrop.\nHearing +0.5.\nOn Qinglan: longer boss warnings; the Listener needs 16 shots to trigger rockfall.\nSource: Rain Listener, Rainlisten Grotto. Drop chance: 30%.\nBreaking its earmuffs stops gunfire-triggered rockfalls.");
                case BossRushItemIds.SkyIslandMossgauzeMask:
                    return L10n.T(
                        "苔纱遮住脸，下方挂着一支小铜笛。\n头部护甲 +1。\n群岛效果：戴着瞄准云蚋，它们不再预先闪避。\n来源：夜间蛙鸣池的蚋笛翁，掉落率30%。\n打穿它的面罩，可阻止吹笛引蚋。",
                        "Mossgauze over a bamboo frame, a brass flute hanging below.\nHead armor +1.\nOn Qinglan: gnats no longer dodge your aim in advance.\nSource: Gnat Piper, Frogsong Pool at night. Drop chance: 30%.\nBreaking its mask stops its gnat-calling tune.");
                case BossRushItemIds.SkyIslandMirrorgrainPlate:
                    return L10n.T(
                        "胸前的圆镜，映出层层水纹甲片。\n身体护甲 +2。\n群岛效果：穿着见折翎，无需旧信和航路图也能和解。\n来源：夜间镜水寺的镜中客，掉落率30%。\n打穿它的镜纹甲，换位后不再留下倒影。",
                        "A round chest mirror reflects rippled plates.\nBody armor +2.\nOn Qinglan: Zheling will reconcile without the old letter or route chart.\nSource: Mirror Guest, Mirrorwater Temple at night. Drop chance: 30%.\nBreaking its plate stops it leaving decoys when it shifts.");
                case BossRushItemIds.SkyIslandWindbreakHood:
                    return L10n.T(
                        "兜帽裹着轻铜盔，顶上一片小风翼。\n头部护甲 +2。\n群岛效果：断风套任穿2件，走桥和中继平台更快。\n来源：中央回程中继的断风游猎·守，掉落率40%。\n打穿它的兜帽，冲锋预警延长一倍。",
                        "A canvas hood over a light helm with a small wind fin.\nHead armor +2.\nOn Qinglan: any 2 Galebreaker pieces boost speed on bridges and relays.\nSource: Galebreaker Warden, central return relay. Drop chance: 40%.\nBreaking its hood doubles its charge warning.");
                case BossRushItemIds.SkyIslandWindbreakMantle:
                    return L10n.T(
                        "轻皮甲后拖着短披风，铜扣像一对鸟翼。\n身体护甲 +2。\n群岛效果：断风套任穿2件，走桥和中继平台更快。\n来源：西北回程中继的断风游猎·追，掉落率40%。\n打穿它的披甲，冲锋预警延长一倍。",
                        "Light scales and a short mantle, fastened with wing-shaped buckles.\nBody armor +2.\nOn Qinglan: any 2 Galebreaker pieces boost speed on bridges and relays.\nSource: Galebreaker Chaser, northwest return relay. Drop chance: 40%.\nBreaking its mantle doubles its charge warning.");
                case BossRushItemIds.SkyIslandWindbreakPack:
                    return L10n.T(
                        "行囊顶上是铺盖卷，边上绑着望远镜。\n背包容量 +5。\n群岛效果：断风套任穿2件，走桥和中继平台更快。\n来源：东侧回程中继的断风游猎·伏，掉落率40%。\n它生命过半时，冲锋后还会闪回平台边缘射击。",
                        "A slim pack with a bedroll and spyglass strapped on.\nBackpack capacity +5.\nOn Qinglan: any 2 Galebreaker pieces boost speed on bridges and relays.\nSource: Galebreaker Stalker, east return relay. Drop chance: 40%.\nAbove half HP, it shifts back to the edge to shoot after lunging.");
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
