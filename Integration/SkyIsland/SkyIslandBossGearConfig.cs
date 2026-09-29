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
        public static void RegisterEquipmentConfigurator()
        {
            EquipmentFactory.RegisterConfigurator("SkyIslandBossGearConfig", (item, baseName) => { TryConfigure(item, baseName); });
        }

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
                // 官方 AIMainBrain.FilterPlayerHearSound 先检查 SoundVisable >= 0.2，
                // 再用 HearingAbility 计算距离。只加听力数值仍不会触发官方声音提示。
                // 与官方耳机一致：两条都挂 Character、都显示（官方 StatInfo 的听力显示格式是 "0"，
                // 小数会被四舍五入显示，所以数值取整数，说明文字与物品页一致）。
                if (spec.Slot == "Headset")
                    EquipmentHelper.EnsureModifierOnItem(item, "SoundVisable", ModifierType.Add, 1f, true);
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
                        "匠首的铜盔，面罩被炉火熏成了琥珀色。\n星工装备（头盔、背甲、背囊）穿任意两件，在渡口工台做东西就少耗 1 片残铜片，最少仍要 1 片。\n残星匠首每趟都穿着整套，倒下时只留一件。它戴着头盔时星焰有 3 处落点，头盔被打穿就只剩 1 处。",
                        "The Foreman's brass helm, its visor smoked amber by the furnace.\nWear any two pieces of the Starworks set (helm, harness, pack) and the dock workbench uses 1 less brass scrap per recipe, never below 1.\nThe Foreman wears the full set every raid and leaves one piece when it falls. While its helm holds, its starfire lands on 3 spots; shoot the helm through and it drops to 1.");
                case BossRushItemIds.SkyIslandStarfurnaceHarness:
                    return L10n.T(
                        "铜板缝的围裙，下摆怎么擦都带着烟灰。\n星工装备穿任意两件，渡口工台做东西少耗 1 片残铜片，最少仍要 1 片。\n出自残星匠首，整套穿在它身上，每次倒下只留一件。背甲被打穿，它立的供能桩只能给一半护甲。",
                        "A brass-plated apron, its hem black with soot no matter how you scrub.\nAny two Starworks pieces: 1 less brass scrap per dock workbench recipe, never below 1.\nFrom the Starforge Foreman, who wears the whole set and drops one piece per fall. Shoot the harness through and its pylons give only half the armor.");
                case BossRushItemIds.SkyIslandStarfurnacePack:
                    return L10n.T(
                        "一座能背走的小星炉，底下还捆着工具卷。\n背包容量 +6。星工装备穿任意两件，渡口工台做东西少耗 1 片残铜片，最少仍要 1 片。\n出自残星匠首。它背着这只炉子，每放 2 次星焰就过热，停手散热时更容易受伤。",
                        "A small star furnace you can carry on your back, a tool roll lashed underneath.\nBackpack capacity +6. Any two Starworks pieces: 1 less brass scrap per dock workbench recipe, never below 1.\nFrom the Starforge Foreman. With this furnace on its back it overheats after every 2 starfires, and takes more damage while it vents.");
                case BossRushItemIds.SkyIslandStargazerLensHelm:
                    return L10n.T(
                        "皮帽上装了三层镜片，看人比看星还清楚。\n在晴岚群岛戴着站定 2 秒，40 米内的敌人脚下会亮起星标。\n残星瞭台的观星手戴着它，倒下时有 30% 留下。观星手的镜片被打穿，就再也标记不了你。",
                        "Three lenses on a leather cap, better for spotting people than stars.\nOn Qinglan, stand still for 2 seconds while wearing it and enemies within 40 m get a star mark at their feet.\nThe stargazer on Starfall Overlook wears one; 30% chance it stays behind when it falls. Shoot its lens through and it can no longer mark you.");
                case BossRushItemIds.SkyIslandRootweaveMask:
                    return L10n.T(
                        "树根编的面罩，缝里还长着苔。\n悬根猎装（面罩、藤编甲、箭囊）穿任意两件，翻搜刮箱时出岛上特产（便当、药膏、罗盘）的机会翻倍。\n悬根猎首每趟穿着整套，倒下时只留一件。它的面罩被打穿，钻出根洞前的预警圈会由 1 秒延长到 2 秒。",
                        "A mask woven from roots, moss still growing in the seams.\nWear any two pieces of the Rootweave gear (mask, cuirass, quiver) and crates you search turn up island goods (bentos, salves, compasses) twice as often.\nThe Huntmaster wears the full set every raid and leaves one piece when it falls. Shoot its mask through and the warning ring before it bursts from a root hollow lasts 2 seconds instead of 1.");
                case BossRushItemIds.SkyIslandVinewovenCuirass:
                    return L10n.T(
                        "硬树皮和粗藤缠成的甲，腰带磨得发亮。\n悬根猎装穿任意两件，翻搜刮箱时出岛上特产（便当、药膏、罗盘）的机会翻倍。\n出自悬根猎首。它的藤编甲被打穿，它拉的绊索只能让你慢一半。",
                        "Armor of hard bark and coarse vine, the belt worn shiny.\nAny two Rootweave pieces: crates you search turn up island goods (bentos, salves, compasses) twice as often.\nFrom the Hanging-Root Huntmaster. Shoot its cuirass through and its tripwire slows you by half as much.");
                case BossRushItemIds.SkyIslandHangrootQuiver:
                    return L10n.T(
                        "老树根掏空做的箭囊，里头装的却是绊索桩。\n背包容量 +6。悬根猎装穿任意两件，翻搜刮箱时出岛上特产（便当、药膏、罗盘）的机会翻倍。\n出自悬根猎首。",
                        "A quiver hollowed out of an old root, and what it holds is tripwire stakes.\nBackpack capacity +6. Any two Rootweave pieces: crates you search turn up island goods (bentos, salves, compasses) twice as often.\nFrom the Hanging-Root Huntmaster.");
                case BossRushItemIds.SkyIslandOldMailbag:
                    return L10n.T(
                        "邮包里塞满信封，一封也没写地址。\n背包容量 +4。背着它上岛，信鸽每趟多送 1 封信。\n倒挂邮亭的截信人背着它，倒下时有 30% 留下。截信人贴身会抢走岛上耗材，血量低于 40% 就把赃物丢在脚下。",
                        "A mailbag stuffed with envelopes, none of them addressed.\nBackpack capacity +4. Carry it onto the isles and the pigeons bring 1 extra letter each raid.\nThe Waylayer at the Upturned Post Hut carries it; 30% chance it stays behind when it falls. Up close the Waylayer snatches island supplies, and below 40% health it drops its loot at its feet.");
                case BossRushItemIds.SkyIslandGreenearStrawHat:
                    return L10n.T(
                        "帽带里插着青穗，草檐已经磨白了。\n穗镰农装（斗笠、蓑衣甲、谷囊）穿任意两件，割青穗草时每次多得 1 份。\n穗镰每趟穿着整套，倒下时只留一件。它的斗笠被打穿，开闸时冲出的泥地由 3 块减为 1 块。",
                        "Green ears tucked into the band of a straw hat whose brim has worn white.\nWear any two pieces of the Grain Sickle gear (hat, raincoat, sack) and each cut of greenear yields 1 extra.\nGrain Sickle wears the full set every raid and leaves one piece when it falls. Shoot its hat through and its sluice floods 1 patch of mud instead of 3.");
                case BossRushItemIds.SkyIslandStrawRaincoat:
                    return L10n.T(
                        "蓑草底下盖着铜片，一动就轻轻响。\n穗镰农装穿任意两件，割青穗草时每次多得 1 份。\n出自穗镰。它的蓑衣甲被打穿，镰扫前的预警圈会由 0.9 秒延长到 1.6 秒。",
                        "Straw laid over brass plates that rattle softly when you move.\nAny two Grain Sickle pieces: each cut of greenear yields 1 extra.\nFrom Grain Sickle. Shoot its raincoat through and the warning before its sweep lasts 1.6 seconds instead of 0.9.");
                case BossRushItemIds.SkyIslandGrainSack:
                    return L10n.T(
                        "鼓鼓的谷袋，边上拴着木瓢和镰刀。\n背包容量 +7。穗镰农装穿任意两件，割青穗草时每次多得 1 份。\n出自穗镰。它背着谷囊，第一次开闸就会喊来谷仓里还活着的帮手。",
                        "A bulging grain sack with a scoop and a sickle tied to its side.\nBackpack capacity +7. Any two Grain Sickle pieces: each cut of greenear yields 1 extra.\nFrom Grain Sickle. With the sack on its back, its first sluice calls in any barn hands still alive.");
                case BossRushItemIds.SkyIslandRainhushEarmuffs:
                    return L10n.T(
                        "铜耳罩里衬着厚毛毡，壳上刻着一滴雨。\n听力 +1，听声辨位 +1：屏幕外的敌人出声，会标出方向。戴着它，所有头目与岛主的预警圈亮得更久；听雨人要听到 16 声枪响才引一次落石。\n听雨洞的听雨人戴着它，倒下时有 30% 留下。听雨人的耳罩被打穿，就再也听不见枪声，引不下落石。",
                        "Brass earcups lined with thick felt, a raindrop engraved on each shell.\nHearing +1, Sound Localization +1: off-screen enemy noises are marked with a direction. While you wear it, every chief's and lord's warning ring stays lit longer, and the Rain Listener needs 16 shots to bring rocks down.\nThe Rain Listener in Rainlisten Grotto wears a pair; 30% chance they stay behind when it falls. Shoot its earmuffs through and it can no longer hear gunfire, so no more rockfall.");
                case BossRushItemIds.SkyIslandMossgauzeMask:
                    return L10n.T(
                        "苔纱遮住整张脸，下面挂着一支小铜笛。\n戴着它瞄准云蚋，它们不会再提前闪躲。\n只在夜里出来的蚋笛翁（蛙鸣池）戴着它，倒下时有 30% 留下。它的面罩被打穿，就吹不响笛子，招不来云蚋。",
                        "Mossgauze covers the whole face, a small brass flute hanging below.\nAim at cloud gnats while wearing it and they stop dodging ahead of your shots.\nThe Gnat Piper, who only comes out at night at Frogsong Pool, wears it; 30% chance it stays behind when it falls. Shoot its mask through and it can no longer play its flute to call the gnats.");
                case BossRushItemIds.SkyIslandMirrorgrainPlate:
                    return L10n.T(
                        "胸前嵌着一面圆镜，映着层层水纹似的甲片。\n穿着它去见折翎，不带旧信和航路图也能和解。\n只在夜里出现的镜水寺镜中客穿着它，倒下时有 30% 留下。镜纹甲被打穿，它换位后就不再留下倒影。",
                        "A round mirror set in the chest, reflecting plates like rippling water.\nWear it when you meet Zheling and you can make peace without the old letter or the route chart.\nThe Mirror Guest of Mirrorwater Temple, who only appears at night, wears it; 30% chance it stays behind when it falls. Shoot the plate through and it leaves no reflection when it swaps places.");
                case BossRushItemIds.SkyIslandWindbreakHood:
                    return L10n.T(
                        "帆布兜帽下是一顶轻铜盔，顶上立着一片小风翼。\n在晴岚群岛，断风装备（兜帽、披甲、行囊）穿任意两件，走桥和中继平台更快。\n中央回程中继的断风游猎·守戴着它，倒下时有 40% 留下。兜帽被打穿，它冲锋前的预警线会亮得长一倍。",
                        "A canvas hood over a light brass helm, a small wind fin on top.\nOn Qinglan, wear any two Galebreaker pieces (hood, mantle, pack) and you move faster on bridges and relay platforms.\nThe Galebreaker Warden at the central return relay wears it; 40% chance it stays behind when it falls. Shoot the hood through and its charge warning line stays lit twice as long.");
                case BossRushItemIds.SkyIslandWindbreakMantle:
                    return L10n.T(
                        "轻皮甲后拖着一件短披风，铜扣像一对鸟翼。\n在晴岚群岛，断风装备穿任意两件，走桥和中继平台更快。\n西北回程中继的断风游猎·追穿着它，倒下时有 40% 留下。披甲被打穿，它冲锋前的预警线会亮得长一倍。",
                        "Light leather scales with a short mantle behind, the brass clasps shaped like a pair of wings.\nOn Qinglan, any two Galebreaker pieces: you move faster on bridges and relay platforms.\nThe Galebreaker Chaser at the northwest return relay wears it; 40% chance it stays behind when it falls. Shoot the mantle through and its charge line stays lit twice as long.");
                case BossRushItemIds.SkyIslandWindbreakPack:
                    return L10n.T(
                        "行囊顶上是铺盖卷，边上绑着一支望远镜。\n背包容量 +5。在晴岚群岛，断风装备穿任意两件，走桥和中继平台更快。\n东侧回程中继的断风游猎·伏背着它，倒下时有 40% 留下。它生命过半、行囊散开之前，每次冲锋后都会闪回平台边缘补枪。",
                        "A bedroll on top, a spyglass strapped to the side.\nBackpack capacity +5. On Qinglan, any two Galebreaker pieces: you move faster on bridges and relay platforms.\nThe Galebreaker Stalker at the east return relay carries it; 40% chance it stays behind when it falls. Until it drops below half health and the pack bursts open, it blinks back to the platform edge to shoot after every lunge.");
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
