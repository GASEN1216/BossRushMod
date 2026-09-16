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
                case BossRushItemIds.SkyIslandRootweaveMask:
                    return L10n.T(
                        "悬根猎首的半截面罩：暗色的树根与树皮条编成，遮住喙的上半与双眼，编缝里塞着苔藓。头部护甲 +1。在晴岚群岛上，与藤编甲或悬根箭囊任意两件一起穿，翻搜刮箱时出岛上特产（便当、药膏、罗盘）的机会翻倍。只从悬根林的岛主身上得到：它每次都穿着全套，倒下时只留下其中一件。它戴着这副面罩时根洞亮 1 秒就钻出来，面罩被暴击打穿之后要亮 2 秒。",
                        "The Hanging-Root Huntmaster's half mask: dark roots and bark strips woven over the top of the bill and around the eyes, moss tucked into the weave. Head armor +1. On the Qinglan isles, wearing any two rootweave pieces (with the Vinewoven Cuirass or Hanging-Root Quiver) doubles the chance of island goods (bentos, salves, compasses) turning up in crates you search. Only from the island lord of the Hanging Root Wood: it always wears the full set and leaves one piece behind when it falls. While it wears this mask a root hollow glows for 1 second before it bursts out; crit the mask through and the glow lasts 2.");
                case BossRushItemIds.SkyIslandVinewovenCuirass:
                    return L10n.T(
                        "粗藤编在弯曲的硬树皮板上，两条交叉的皮带，腰间挂着小皮囊。身体护甲 +3。在晴岚群岛上，与根须面罩或悬根箭囊任意两件一起穿，翻搜刮箱时出岛上特产的机会翻倍。只从悬根林的岛主身上得到：它的绊索就系在这件甲的皮带扣上，甲被打穿之后绊索只能让你慢一半。",
                        "Thick braided vines over hard curved bark plates, two crossed leather belts and small pouches at the waist. Body armor +3. On the Qinglan isles, wearing any two rootweave pieces (with the Rootweave Mask or Hanging-Root Quiver) doubles the chance of island goods turning up in crates you search. Only from the island lord of the Hanging Root Wood: its tripwires hitch to this cuirass's buckles, and once the cuirass is shot through a tripwire only slows you half as much.");
                case BossRushItemIds.SkyIslandHangrootQuiver:
                    return L10n.T(
                        "空心的老树根捆着藤绳，插满削尖的根桩，侧边挂着一卷藤编绊索。背包容量 +6。在晴岚群岛上，与根须面罩或藤编甲任意两件一起穿，翻搜刮箱时出岛上特产的机会翻倍。只从悬根林的岛主身上得到：它拉绊索用的根桩就是从这只箭囊里拔出来的。",
                        "A hollow gnarled root bound with vine rope, stuffed with sharpened root stakes, a coil of vine tripwire hanging at the side. Backpack capacity +6. On the Qinglan isles, wearing any two rootweave pieces (with the Rootweave Mask or Vinewoven Cuirass) doubles the chance of island goods turning up in crates you search. Only from the island lord of the Hanging Root Wood: the stakes it strings its tripwires between come out of this quiver.");
                case BossRushItemIds.SkyIslandOldMailbag:
                    return L10n.T(
                        "褪色的奶白帆布邮包，翻盖上钉着一枚铜邮号，里面塞着一捆没写字的封好的信封。背包容量 +4。在晴岚群岛上背着它，信鸽那一趟会多送一封信（每趟一次）。只从倒挂邮亭的头目身上得到：它倒下时有三成机会留下这只邮包；邮包还在它背上时它会贴身抢你的岛上耗材，血线低于四成才把抢来的东西丢下。",
                        "A faded cream canvas mailbag with a brass post-horn badge on the flap, stuffed with a bundle of sealed blank envelopes. Backpack capacity +4. Carry it on the Qinglan isles and the pigeons bring one extra letter that raid (once per raid). Only from the chief of the Upturned Post Hut: it leaves this bag behind three times in ten; while the bag is on its back it snatches island supplies off you up close, and only drops what it took once it falls below 40% health.");
                case BossRushItemIds.SkyIslandGreenearStrawHat:
                    return L10n.T(
                        "宽檐的草编斗笠，铜边箍，帽带上插着几支青穗。头部护甲 +2。在晴岚群岛上，与蓑衣甲或谷囊任意两件一起穿，割青穗草时一次多割一份。只从青穗梯田的岛主身上得到：它每次都穿着全套，倒下时只留下其中一件。它戴着这顶斗笠时开闸冲出三块泥，斗笠被爆头打穿之后只冲出一块。",
                        "A wide-brimmed conical straw hat with a brass rim band and a few green grain ears tucked into the band. Head armor +2. On the Qinglan isles, wearing any two straw-cloak pieces (with the Straw Raincoat or Grain Sack) yields one more sheaf each time you cut greenear. Only from the island lord of the Green Terraces: it always wears the full set and leaves one piece behind when it falls. While it wears this hat its sluices flood three patches of mud; shoot the hat through and only one floods.");
                case BossRushItemIds.SkyIslandStrawRaincoat:
                    return L10n.T(
                        "一层层金黄的蓑草披在肩上和胸前，胸口铆了几块铜片，腰间一根草绳配月牙铜扣。身体护甲 +3。在晴岚群岛上，与青穗斗笠或谷囊任意两件一起穿，割青穗草时一次多割一份。只从青穗梯田的岛主身上得到：甲还完好时它的镰扫只亮 0.9 秒圈，甲被打穿之后要亮 1.6 秒。",
                        "Layer upon layer of golden straw over the shoulders and chest, a few brass plates riveted at the chest, a rope belt with a crescent brass clasp. Body armor +3. On the Qinglan isles, wearing any two straw-cloak pieces (with the Greenear Straw Hat or Grain Sack) yields one more sheaf each time you cut greenear. Only from the island lord of the Green Terraces: while the raincoat holds its sickle sweep glows for only 0.9 seconds; shoot it through and the sweep takes 1.6.");
                case BossRushItemIds.SkyIslandGrainSack:
                    return L10n.T(
                        "鼓囊囊的麻布谷袋背在身后，袋口扎着麻绳，冒出几支青穗，侧边捆着木瓢和收鞘的镰刀。背包容量 +7。在晴岚群岛上，与青穗斗笠或蓑衣甲任意两件一起穿，割青穗草时一次多割一份。只从青穗梯田的岛主身上得到：第一次开闸时它会把谷仓那边还站着的帮手喊过来。",
                        "A plump burlap grain sack tied with twine, green grain ears poking out, a wooden scoop and a sheathed sickle strapped to the side. Backpack capacity +7. On the Qinglan isles, wearing any two straw-cloak pieces (with the Greenear Straw Hat or Straw Raincoat) yields one more sheaf each time you cut greenear. Only from the island lord of the Green Terraces: the first time it opens a sluice it calls over whatever hands are still standing by the barn.");
                case BossRushItemIds.SkyIslandRainhushEarmuffs:
                    return L10n.T(
                        "铜头箍连着两只厚实的圆耳罩，耳罩衬着青色毛毡，铜网面上刻着一滴雨，一侧卷出一只小铜听筒。听觉 +0.5。在晴岚群岛上戴着它，所有头目与岛主的预警圈都亮得更久，听雨人要你开 16 枪才引一次落石。只从听雨洞的头目身上得到：它倒下时有三成机会留下这副耳罩；耳罩被暴击打穿之前，它会循着你的枪声让洞顶落石。",
                        "Two chunky round earmuffs on a brass headband, teal felt padding behind brass mesh grilles engraved with a rain drop, a little brass ear trumpet curling off one side. Hearing +0.5. Wear it on the Qinglan isles and every chief's and island lord's warning rings stay lit longer, and the Rain Listener needs 16 of your shots to bring rocks down. Only from the chief of the Rainlisten Grotto: it leaves these earmuffs behind three times in ten; until they are crit through it follows your gunfire and drops rocks from the overhang.");
                case BossRushItemIds.SkyIslandMossgauzeMask:
                    return L10n.T(
                        "细竹框上绷着淡绿的苔纱，遮住脸和喙，边上别着干草药，下巴底下挂着一支小铜笛。头部护甲 +1。在晴岚群岛上戴着它，身边的云蚋认不出你在瞄它，躲不开你的枪口。只从蛙鸣池的头目身上得到：它只在夜里出来，倒下时有三成机会留下这副面罩；面罩被暴击打穿之前，它会吹笛把云蚋全引到你身上。",
                        "Pale green moss gauze stretched over a thin bamboo frame covering the face and bill, dried herbs tucked at the rim, a little brass flute hanging below the chin. Head armor +1. Wear it on the Qinglan isles and the cloud gnats around you cannot tell you are aiming at them — they stop dodging your shots. Only from the chief of Frogsong Pool: it comes out only at night and leaves this mask behind three times in ten; until the mask is crit through, its flute draws every gnat onto you.");
                case BossRushItemIds.SkyIslandMirrorgrainPlate:
                    return L10n.T(
                        "打磨光亮的银青漆甲片层层叠压，刻着水面倒影的波纹，胸口嵌一面圆铜镜，肩上是云纹护肩。身体护甲 +2。在晴岚群岛上穿着它去见折翎，他认得这身纹路：不带旧信与航路图也能和解。只从镜水寺的头目身上得到：它只在夜里出来，倒下时有三成机会留下这件甲；甲还完好时它翻到你背后会留下一个倒影，甲被打穿之后就留不下了。",
                        "Overlapping polished silver-teal lacquered plates etched with rippling reflections, a round bronze mirror at the chest, cloud-shaped shoulder guards. Body armor +2. Wear it on the Qinglan isles to see Zheling — he knows the pattern and will reconcile without the old letter or the route chart. Only from the chief of Mirrorwater Temple: it comes out only at night and leaves this plate behind three times in ten; while the plate holds it leaves a reflection behind when it flips round to your back, and once the plate is shot through it cannot.");
                case BossRushItemIds.SkyIslandWindbreakHood:
                    return L10n.T(
                        "旧青帆布兜帽罩着一顶轻铜盔，布帘朝后飘着，顶上一片小铜风翼，额前推着一副铜护目镜。头部护甲 +2。在晴岚群岛上，与断风披甲或断风行囊任意两件一起穿，走桥与中继平台时移动更快。只从守着中央回程中继平台的断风游猎 · 守身上得到：它倒下时有四成机会留下这顶兜帽；兜帽被爆头打穿之后，它冲锋前地上的线要亮两倍久。",
                        "A weathered teal canvas hood over a light brass skull cap, cloth flaps streaming back, a little brass wind fin on top and brass goggles pushed up on the brow. Head armor +2. On the Qinglan isles, wearing any two Galebreaker pieces (with the Galebreaker Mantle or Pack) makes you faster on bridges and relay platforms. Only from the Galebreaker Ranger (Warden) on the central return relay platform: it leaves this hood behind four times in ten; shoot the hood through and the line before its lunge stays lit twice as long.");
                case BossRushItemIds.SkyIslandWindbreakMantle:
                    return L10n.T(
                        "薄皮鳞片层层压成的轻甲，肩后飘着一截青布短披风，铜扣做成鸟翼的样子。身体护甲 +2。在晴岚群岛上，与断风兜帽或断风行囊任意两件一起穿，走桥与中继平台时移动更快。只从守着西北回程中继平台的断风游猎 · 追身上得到：它倒下时有四成机会留下这件披甲；披甲被打穿之后，它冲锋前地上的线要亮两倍久。",
                        "Light armor of overlapping thin leather scales, a short teal mantle streaming back from the shoulders, brass buckles shaped like bird wings. Body armor +2. On the Qinglan isles, wearing any two Galebreaker pieces (with the Galebreaker Hood or Pack) makes you faster on bridges and relay platforms. Only from the Galebreaker Ranger (Chaser) on the northwest return relay platform: it leaves this mantle behind four times in ten; shoot the mantle through and the line before its lunge stays lit twice as long.");
                case BossRushItemIds.SkyIslandWindbreakPack:
                    return L10n.T(
                        "细长的青帆布行囊，顶上横捆着铺盖卷，两侧各一片小铜风翼，边上竖绑着一支铜望远镜。背包容量 +5。在晴岚群岛上，与断风兜帽或断风披甲任意两件一起穿，走桥与中继平台时移动更快。只从守着东侧回程中继平台的断风游猎 · 伏身上得到：它倒下时有四成机会留下这只行囊；它血线过半之前，冲完一步就闪回平台边缘补枪。",
                        "A slim teal canvas pack with a bedroll strapped across the top, a little brass wind fin on each side and a brass spyglass tube along one edge. Backpack capacity +5. On the Qinglan isles, wearing any two Galebreaker pieces (with the Galebreaker Hood or Mantle) makes you faster on bridges and relay platforms. Only from the Galebreaker Ranger (Stalker) on the east return relay platform: it leaves this pack behind four times in ten; until it drops below half health it lunges once and then flicks back to the platform edge to shoot.");
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
