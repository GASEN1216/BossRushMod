using System;

namespace BossRush
{
    /// <summary>一件岛上纪念品：手记里记它的 id、发哪件物品、发到哪里、怎么得到，以及发放时的字幕。</summary>
    internal sealed class SkyIslandKeepsake
    {
        internal string NoteId;
        internal int TypeId;
        /// <summary>直接寄回基地仓库（纪念品不该跟着这一趟的背包进墓碑）；false 表示先放背包、放不下再寄回。</summary>
        internal bool ToStorage;
        internal string HowCn, HowEn, CaptionCn, CaptionEn;
        internal string How { get { return L10n.T(HowCn, HowEn); } }
        internal string Caption { get { return L10n.T(CaptionCn, CaptionEn); } }
    }

    /// <summary>
    /// COMPAT：天空岛物品的纯规则——物品显示名、纪念品发放台账、搜刮箱里的岛上特产、风标罗盘的读数。
    ///
    /// 纪念品只发一次：发放记录与见闻、来信共用本槽手记（`SkyIslandStoryData.discoveredNotes`，id 前缀 <see cref="NotePrefix"/>），
    /// 不加存档字段。顺序是先准备物品实例、再记手记、最后转移：资源缺失不耗掉领取资格，
    /// 写屏障下也不能每趟重发一件能卖钱的东西。转移后不撤销台账，避免异常后重复领取。
    /// 纯逻辑、无 Unity 依赖，隔离回归直接执行。
    /// </summary>
    internal static class SkyIslandItemRules
    {
        internal const string NotePrefix = "Keepsake_";

        /// <summary>罗盘离目标这么近（米）就不再报方位，只说「就在附近」。</summary>
        internal const double NearDistance = 8.0;

        /// <summary>第一封信捎来的风标罗盘在手记里的 id：纪念品台账与「重做一只罗盘」的配方门槛共用。</summary>
        internal const string CompassKeepsake = "Keepsake_Compass";

        /// <summary>晴岚航徽在背包里时，渡口整备与眠苔苔药按这个比例收钱：码头的人都认得它。</summary>
        internal const double BadgeServiceRate = 0.5;

        /// <summary>全部天空岛物品，按 TypeID 递增（批次二 500068–500072，批次三 500073–500082，批次四 500083–500085）。</summary>
        internal static readonly int[] AllTypeIds =
        {
            BossRushItemIds.SkyIslandHomecomingBadge, BossRushItemIds.SkyIslandWindeaterCore,
            BossRushItemIds.SkyIslandWindVaneCompass, BossRushItemIds.SkyIslandHomecomingBento,
            BossRushItemIds.SkyIslandStarmossSalve,
            BossRushItemIds.SkyIslandCloudmossFiber, BossRushItemIds.SkyIslandGreenearSheaf,
            BossRushItemIds.SkyIslandDriftwood, BossRushItemIds.SkyIslandBrassScrap,
            BossRushItemIds.SkyIslandWindcrystalShard, BossRushItemIds.SkyIslandStardust,
            BossRushItemIds.SkyIslandQinglanWindcrystal, BossRushItemIds.SkyIslandWindLantern,
            BossRushItemIds.SkyIslandWindwardIncense, BossRushItemIds.SkyIslandQinglanCharm,
            BossRushItemIds.SkyIslandCloudmossVeil, BossRushItemIds.SkyIslandGnatZapper,
            BossRushItemIds.SkyIslandSmokeFan
        };

        /// <summary>
        /// 截信人会下手的东西：岛上的耗材与随身工具（便当、药膏、风灯、驱风香、护符、纱笠、灭蚊灯、蒲扇）。
        /// 纪念品、材料与官方物品一律不偷——被抢的只该是「这一趟拿来用的东西」，倒下时原物进它的尸体箱。
        /// </summary>
        internal static readonly int[] StealableTypeIds =
        {
            BossRushItemIds.SkyIslandHomecomingBento, BossRushItemIds.SkyIslandStarmossSalve,
            BossRushItemIds.SkyIslandWindLantern, BossRushItemIds.SkyIslandWindwardIncense,
            BossRushItemIds.SkyIslandQinglanCharm, BossRushItemIds.SkyIslandCloudmossVeil,
            BossRushItemIds.SkyIslandGnatZapper, BossRushItemIds.SkyIslandSmokeFan
        };

        private static readonly SkyIslandKeepsake[] keepsakes =
        {
            Keepsake(CompassKeepsake, BossRushItemIds.SkyIslandWindVaneCompass, false,
                "收下第一封信鸽来信", "keep your first pigeon letter",
                "浮舟托信鸽捎来一只风标罗盘（已放进背包，放不下就寄回基地仓库）：在群岛上使用，它会指向信鸽或下一个目标。",
                "Fuzhou sent a wind-vane compass along with the pigeon (in your pack, or in base storage if it was full). Use it on the isles and it points to the pigeon or your next objective."),
            Keepsake("Keepsake_Badge", BossRushItemIds.SkyIslandHomecomingBadge, true,
                "敲响归航钟", "ring the Homecoming Bell",
                "归航钟的回声还没散：一枚晴岚航徽已寄回基地仓库。带着它上岛，码头的人都认得你：渡口整备与眠苔的苔药半价，在岛上用它还能拉缆绳回到码头（每趟一次）。",
                "The bell is still echoing: a Qinglan Homecoming Badge has been sent to your base storage. Carry it on the isles and the islanders know you: the dock refit and Miantai's moss remedy cost half, and using it on the isles pulls you back to the dock (once per raid)."),
            Keepsake("Keepsake_Core", BossRushItemIds.SkyIslandWindeaterCore, true,
                "击败鸣风栈道上的噬风", "defeat the Windeater on Windsong Boardwalk",
                "噬风散去的地方留下一枚噬风之核，已寄回基地仓库。带着它上岛，核里那团风会吃掉身边的风：大风对你只算微风。",
                "Where the Windeater broke apart it left a Windeater Core behind; it has been sent to your base storage. Carry it on the isles and the whirl inside eats into the wind around you: a gale only counts as a breeze.")
        };

        internal static SkyIslandKeepsake[] Keepsakes { get { return keepsakes; } }

        /// <summary>物品显示名（中文）：物品配置、手记与字幕共用这一份对照。</summary>
        internal static string NameCn(int typeId)
        {
            switch (typeId)
            {
                case BossRushItemIds.SkyIslandHomecomingBadge: return "晴岚航徽";
                case BossRushItemIds.SkyIslandWindeaterCore: return "噬风之核";
                case BossRushItemIds.SkyIslandWindVaneCompass: return "风标罗盘";
                case BossRushItemIds.SkyIslandHomecomingBento: return "归航菜便当";
                case BossRushItemIds.SkyIslandStarmossSalve: return "星苔药膏";
                case BossRushItemIds.SkyIslandCloudmossFiber: return "云苔纤维";
                case BossRushItemIds.SkyIslandGreenearSheaf: return "青穗草";
                case BossRushItemIds.SkyIslandDriftwood: return "浮木";
                case BossRushItemIds.SkyIslandBrassScrap: return "残铜片";
                case BossRushItemIds.SkyIslandWindcrystalShard: return "风晶碎片";
                case BossRushItemIds.SkyIslandStardust: return "星屑";
                case BossRushItemIds.SkyIslandQinglanWindcrystal: return "晴岚风晶";
                case BossRushItemIds.SkyIslandWindLantern: return "风灯";
                case BossRushItemIds.SkyIslandWindwardIncense: return "驱风香";
                case BossRushItemIds.SkyIslandQinglanCharm: return "晴岚护符";
                case BossRushItemIds.SkyIslandCloudmossVeil: return "云苔纱笠";
                case BossRushItemIds.SkyIslandGnatZapper: return "风晶灭蚊灯";
                case BossRushItemIds.SkyIslandSmokeFan: return "药烟蒲扇";
                // 头目 / 岛主的专属装备（SkyIslandBossRules.AllGearTypeIds）：名字与价值同样只在这里一处，但不进 AllTypeIds。
                case BossRushItemIds.SkyIslandStarbrassVisorHelm: return "星铜护目盔";
                case BossRushItemIds.SkyIslandStarfurnaceHarness: return "星炉背甲";
                case BossRushItemIds.SkyIslandStarfurnacePack: return "星炉背囊";
                case BossRushItemIds.SkyIslandStargazerLensHelm: return "观星镜盔";
                case BossRushItemIds.SkyIslandRootweaveMask: return "根须面罩";
                case BossRushItemIds.SkyIslandVinewovenCuirass: return "藤编甲";
                case BossRushItemIds.SkyIslandHangrootQuiver: return "悬根箭囊";
                case BossRushItemIds.SkyIslandOldMailbag: return "旧邮包";
                case BossRushItemIds.SkyIslandGreenearStrawHat: return "青穗斗笠";
                case BossRushItemIds.SkyIslandStrawRaincoat: return "蓑衣甲";
                case BossRushItemIds.SkyIslandGrainSack: return "谷囊";
                case BossRushItemIds.SkyIslandRainhushEarmuffs: return "静听耳罩";
                case BossRushItemIds.SkyIslandMossgauzeMask: return "苔纱面罩";
                case BossRushItemIds.SkyIslandMirrorgrainPlate: return "镜纹甲";
                case BossRushItemIds.SkyIslandWindbreakHood: return "断风兜帽";
                case BossRushItemIds.SkyIslandWindbreakMantle: return "断风披甲";
                case BossRushItemIds.SkyIslandWindbreakPack: return "断风行囊";
                default: return "天空岛物品";
            }
        }

        /// <summary>物品显示名（英文）。</summary>
        internal static string NameEn(int typeId)
        {
            switch (typeId)
            {
                case BossRushItemIds.SkyIslandHomecomingBadge: return "Qinglan Homecoming Badge";
                case BossRushItemIds.SkyIslandWindeaterCore: return "Windeater Core";
                case BossRushItemIds.SkyIslandWindVaneCompass: return "Wind-Vane Compass";
                case BossRushItemIds.SkyIslandHomecomingBento: return "Homecoming Bento";
                case BossRushItemIds.SkyIslandStarmossSalve: return "Starmoss Salve";
                case BossRushItemIds.SkyIslandCloudmossFiber: return "Cloudmoss Fiber";
                case BossRushItemIds.SkyIslandGreenearSheaf: return "Greenear Sheaf";
                case BossRushItemIds.SkyIslandDriftwood: return "Driftwood";
                case BossRushItemIds.SkyIslandBrassScrap: return "Brass Scrap";
                case BossRushItemIds.SkyIslandWindcrystalShard: return "Windcrystal Shard";
                case BossRushItemIds.SkyIslandStardust: return "Stardust";
                case BossRushItemIds.SkyIslandQinglanWindcrystal: return "Qinglan Windcrystal";
                case BossRushItemIds.SkyIslandWindLantern: return "Wind Lantern";
                case BossRushItemIds.SkyIslandWindwardIncense: return "Windward Incense";
                case BossRushItemIds.SkyIslandQinglanCharm: return "Qinglan Charm";
                case BossRushItemIds.SkyIslandCloudmossVeil: return "Cloudmoss Veil";
                case BossRushItemIds.SkyIslandGnatZapper: return "Windcrystal Gnat Zapper";
                case BossRushItemIds.SkyIslandSmokeFan: return "Remedy-Smoke Fan";
                case BossRushItemIds.SkyIslandStarbrassVisorHelm: return "Starbrass Visor Helm";
                case BossRushItemIds.SkyIslandStarfurnaceHarness: return "Starfurnace Harness";
                case BossRushItemIds.SkyIslandStarfurnacePack: return "Starfurnace Pack";
                case BossRushItemIds.SkyIslandStargazerLensHelm: return "Stargazer's Lens Helm";
                case BossRushItemIds.SkyIslandRootweaveMask: return "Rootweave Mask";
                case BossRushItemIds.SkyIslandVinewovenCuirass: return "Vinewoven Cuirass";
                case BossRushItemIds.SkyIslandHangrootQuiver: return "Hanging-Root Quiver";
                case BossRushItemIds.SkyIslandOldMailbag: return "Old Mailbag";
                case BossRushItemIds.SkyIslandGreenearStrawHat: return "Greenear Straw Hat";
                case BossRushItemIds.SkyIslandStrawRaincoat: return "Straw Raincoat";
                case BossRushItemIds.SkyIslandGrainSack: return "Grain Sack";
                case BossRushItemIds.SkyIslandRainhushEarmuffs: return "Rainhush Earmuffs";
                case BossRushItemIds.SkyIslandMossgauzeMask: return "Mossgauze Mask";
                case BossRushItemIds.SkyIslandMirrorgrainPlate: return "Mirrorgrain Plate";
                case BossRushItemIds.SkyIslandWindbreakHood: return "Galebreaker Hood";
                case BossRushItemIds.SkyIslandWindbreakMantle: return "Galebreaker Mantle";
                case BossRushItemIds.SkyIslandWindbreakPack: return "Galebreaker Pack";
                default: return "Sky Islands item";
            }
        }

        internal static string Name(int typeId) { return L10n.T(NameCn(typeId), NameEn(typeId)); }

        /// <summary>
        /// 物品价值（官方 `Item.Value`，商店售价 = Value × 耐久比 × priceFactor）。物品配置、配方经济与报告共用这一份，
        /// 不在 `SkyIslandItems` 定义表里另写一份数字。批次三的口径：材料按「采一处约一两件」定低价；
        /// 凑整与合成只给小幅溢价（晴岚风晶 +16%、便当 +88%、药膏 +48%，护符与罗盘基本持平），不当换钱的路子。
        /// 批次四（云蚋的对策）同一口径：纱笠 +20%、蒲扇 +62%、灭蚊灯一次两盏基本持平（+2%）。
        /// </summary>
        internal static int ValueOf(int typeId)
        {
            switch (typeId)
            {
                case BossRushItemIds.SkyIslandCloudmossVeil: return 1400;
                case BossRushItemIds.SkyIslandGnatZapper: return 1600;
                case BossRushItemIds.SkyIslandSmokeFan: return 420;
                // 头目 / 岛主的专属装备：品质 5 的战利品，头盔 / 背包 1.2 万、护甲 1.5 万、观星镜盔 0.9 万（不进岛上物资池，价值上限不适用）。
                case BossRushItemIds.SkyIslandStarbrassVisorHelm: return 12000;
                case BossRushItemIds.SkyIslandStarfurnaceHarness: return 15000;
                case BossRushItemIds.SkyIslandStarfurnacePack: return 12000;
                case BossRushItemIds.SkyIslandStargazerLensHelm: return 9000;
                // R2–R4：岛主三件同 R1（1.2 万 / 1.5 万 / 1.2 万）；头目单件 0.8–1.0 万；断风套三位头目各四成掉一件，单件 0.7 万。
                case BossRushItemIds.SkyIslandRootweaveMask: return 12000;
                case BossRushItemIds.SkyIslandVinewovenCuirass: return 15000;
                case BossRushItemIds.SkyIslandHangrootQuiver: return 12000;
                case BossRushItemIds.SkyIslandOldMailbag: return 8000;
                case BossRushItemIds.SkyIslandGreenearStrawHat: return 12000;
                case BossRushItemIds.SkyIslandStrawRaincoat: return 15000;
                case BossRushItemIds.SkyIslandGrainSack: return 12000;
                case BossRushItemIds.SkyIslandRainhushEarmuffs: return 9000;
                case BossRushItemIds.SkyIslandMossgauzeMask: return 9000;
                case BossRushItemIds.SkyIslandMirrorgrainPlate: return 10000;
                case BossRushItemIds.SkyIslandWindbreakHood: return 7000;
                case BossRushItemIds.SkyIslandWindbreakMantle: return 7000;
                case BossRushItemIds.SkyIslandWindbreakPack: return 7000;
                case BossRushItemIds.SkyIslandHomecomingBadge: return 5000;
                case BossRushItemIds.SkyIslandWindeaterCore: return 12000;
                case BossRushItemIds.SkyIslandWindVaneCompass: return 1500;
                case BossRushItemIds.SkyIslandHomecomingBento: return 600;
                case BossRushItemIds.SkyIslandStarmossSalve: return 1200;
                case BossRushItemIds.SkyIslandCloudmossFiber: return 90;
                case BossRushItemIds.SkyIslandGreenearSheaf: return 60;
                case BossRushItemIds.SkyIslandDriftwood: return 80;
                case BossRushItemIds.SkyIslandBrassScrap: return 180;
                case BossRushItemIds.SkyIslandWindcrystalShard: return 450;
                case BossRushItemIds.SkyIslandStardust: return 900;
                case BossRushItemIds.SkyIslandQinglanWindcrystal: return 2600;
                case BossRushItemIds.SkyIslandWindLantern: return 320;
                case BossRushItemIds.SkyIslandWindwardIncense: return 360;
                case BossRushItemIds.SkyIslandQinglanCharm: return 2400;
                default: return 0;
            }
        }

        /// <summary>服务的实际收费：带着晴岚航徽按 <see cref="BadgeServiceRate"/> 收（服务费下限之后再打折，向上取整）。</summary>
        internal static int ServicePrice(int price, bool badgeCarried)
        {
            if (price <= 0 || !badgeCarried) return price;
            return (int)Math.Ceiling(price * BadgeServiceRate);
        }

        /// <summary>带着航徽时服务回话末尾的那一句。</summary>
        internal static string BadgeDiscountNote
        { get { return L10n.T("（带着晴岚航徽：半价）", " (Homecoming Badge: half price)"); } }

        internal static SkyIslandKeepsake FindKeepsake(string noteId)
        {
            for (int i = 0; i < keepsakes.Length; i++)
                if (string.Equals(keepsakes[i].NoteId, noteId, StringComparison.Ordinal)) return keepsakes[i];
            return null;
        }

        internal static bool Granted(SkyIslandStoryData data, string noteId)
        {
            return data != null && data.discoveredNotes != null && Array.IndexOf(data.discoveredNotes, noteId) >= 0;
        }

        internal static int GrantedCount(SkyIslandStoryData data)
        {
            int count = 0;
            for (int i = 0; i < keepsakes.Length; i++) if (Granted(data, keepsakes[i].NoteId)) count++;
            return count;
        }

        /// <summary>这件纪念品现在该不该发：条件已满足、且手记里还没有发放记录。</summary>
        internal static bool Due(SkyIslandStoryData data, SkyIslandKeepsake keepsake)
        {
            if (data == null || keepsake == null || Granted(data, keepsake.NoteId)) return false;
            switch (keepsake.NoteId)
            {
                // 第一封信送到时一起捎来：之后的信都不上地图，罗盘就是找它们的办法。
                case CompassKeepsake: return SkyIslandLetters.CollectedCount(data) > 0;
                case "Keepsake_Badge": return data.Has(SkyIslandStoryFlag.Ending);
                case "Keepsake_Core": return data.StormResolved;
                default: return false;
            }
        }

        /// <summary>
        /// 搜刮箱与奖励箱里的岛上特产：每箱至多一件，由档次与一次独立抽样决定——不占该箱原本的件数，也不动原有随机流。
        /// 概率刻意压低：一趟 39 个搜刮点期望约 3.4 份便当、3.6 罐药膏、0.2 只罗盘（委托谢礼与噬风战利品另算）。
        /// </summary>
        internal static int IslandExtraFor(SkyIslandLootTier tier, double roll)
        {
            switch (tier)
            {
                case SkyIslandLootTier.Supply:
                    return roll < 0.12 ? BossRushItemIds.SkyIslandHomecomingBento : 0;
                case SkyIslandLootTier.Voyage:
                    if (roll < 0.10) return BossRushItemIds.SkyIslandStarmossSalve;
                    return roll < 0.18 ? BossRushItemIds.SkyIslandHomecomingBento : 0;
                case SkyIslandLootTier.Starworks:
                    if (roll < 0.18) return BossRushItemIds.SkyIslandStarmossSalve;
                    if (roll < 0.24) return BossRushItemIds.SkyIslandHomecomingBento;
                    return roll < 0.26 ? BossRushItemIds.SkyIslandWindVaneCompass : 0;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 八方位。dx、dz 是从玩家指向目标的水平位移；场景坐标按布局表的 Unity 轴向读作 +z 北、+x 东
        /// （秘境谜题与信里的方位也用同一口径；官方地图朝向需实机核对，见待人工验证清单）。
        /// </summary>
        internal static string Bearing(double dx, double dz)
        {
            double angle = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            if (angle < 0) angle += 360.0;
            switch ((int)Math.Floor((angle + 22.5) / 45.0) % 8)
            {
                case 0: return L10n.T("北", "north");
                case 1: return L10n.T("东北", "northeast");
                case 2: return L10n.T("东", "east");
                case 3: return L10n.T("东南", "southeast");
                case 4: return L10n.T("南", "south");
                case 5: return L10n.T("西南", "southwest");
                case 6: return L10n.T("西", "west");
                default: return L10n.T("西北", "northwest");
            }
        }

        /// <summary>罗盘读数：没有目标时说没有要找的；很近时说就在附近；否则「转向某方位，约 N 米：是什么」（10 米取整）。</summary>
        internal static string CompassReading(bool hasTarget, double dx, double dz, string what)
        {
            if (!hasTarget)
                return L10n.T("风标静静停着：群岛上暂时没有要找的东西了。", "The vane rests still: there is nothing left to find on the isles for now.");
            double distance = Math.Sqrt(dx * dx + dz * dz);
            if (distance < NearDistance)
                return L10n.T("风标垂了下来——就在这附近：", "The vane droops — it is right around here: ") + what;
            int rounded = (int)(Math.Round(distance / 10.0, MidpointRounding.AwayFromZero) * 10.0);
            return L10n.T("风标转向", "The vane swings ") + Bearing(dx, dz) + L10n.T("，约 ", ", about ") + rounded +
                L10n.T(" 米：", " m: ") + what;
        }

        private static SkyIslandKeepsake Keepsake(string noteId, int typeId, bool toStorage, string howCn, string howEn,
            string captionCn, string captionEn)
        {
            return new SkyIslandKeepsake
            {
                NoteId = noteId, TypeId = typeId, ToStorage = toStorage, HowCn = howCn, HowEn = howEn,
                CaptionCn = captionCn, CaptionEn = captionEn
            };
        }
    }
}
