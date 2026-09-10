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
    /// 不加存档字段。顺序是**先记手记、再发物品**：写屏障下宁可这趟不发，也不能每趟重发一件能卖钱的东西；
    /// 物品实例化失败（资源缺失）只记日志——那是部署损坏，不是玩家重试就能好的状态。
    /// 纯逻辑、无 Unity 依赖，隔离回归直接执行。
    /// </summary>
    internal static class SkyIslandItemRules
    {
        internal const string NotePrefix = "Keepsake_";

        /// <summary>罗盘离目标这么近（米）就不再报方位，只说「就在附近」。</summary>
        internal const double NearDistance = 8.0;

        /// <summary>全部天空岛物品，按 TypeID 递增（批次二 500068–500072，批次三 500073–500082）。</summary>
        internal static readonly int[] AllTypeIds =
        {
            BossRushItemIds.SkyIslandHomecomingBadge, BossRushItemIds.SkyIslandWindeaterCore,
            BossRushItemIds.SkyIslandWindVaneCompass, BossRushItemIds.SkyIslandHomecomingBento,
            BossRushItemIds.SkyIslandStarmossSalve,
            BossRushItemIds.SkyIslandCloudmossFiber, BossRushItemIds.SkyIslandGreenearSheaf,
            BossRushItemIds.SkyIslandDriftwood, BossRushItemIds.SkyIslandBrassScrap,
            BossRushItemIds.SkyIslandWindcrystalShard, BossRushItemIds.SkyIslandStardust,
            BossRushItemIds.SkyIslandQinglanWindcrystal, BossRushItemIds.SkyIslandWindLantern,
            BossRushItemIds.SkyIslandWindwardIncense, BossRushItemIds.SkyIslandQinglanCharm
        };

        private static readonly SkyIslandKeepsake[] keepsakes =
        {
            Keepsake("Keepsake_Compass", BossRushItemIds.SkyIslandWindVaneCompass, false,
                "收下第一封信鸽来信", "keep your first pigeon letter",
                "浮舟托信鸽捎来一只风标罗盘（已放进背包，放不下就寄回基地仓库）：在群岛上使用，它会指向信鸽或下一个目标。",
                "Fuzhou sent a wind-vane compass along with the pigeon (in your pack, or in base storage if it was full). Use it on the isles and it points to the pigeon or your next objective."),
            Keepsake("Keepsake_Badge", BossRushItemIds.SkyIslandHomecomingBadge, true,
                "敲响归航钟", "ring the Homecoming Bell",
                "归航钟的回声还没散：一枚晴岚航徽已寄回基地仓库。",
                "The bell is still echoing: a Qinglan Homecoming Badge has been sent to your base storage."),
            Keepsake("Keepsake_Core", BossRushItemIds.SkyIslandWindeaterCore, true,
                "击败鸣风栈道上的噬风", "defeat the Windeater on Windsong Boardwalk",
                "噬风散去的地方留下一枚噬风之核，已寄回基地仓库。",
                "Where the Windeater broke apart it left a Windeater Core behind; it has been sent to your base storage.")
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
                default: return "Sky Islands item";
            }
        }

        internal static string Name(int typeId) { return L10n.T(NameCn(typeId), NameEn(typeId)); }

        /// <summary>
        /// 物品价值（官方 `Item.Value`，商店售价 = Value × 耐久比 × priceFactor）。物品配置、配方经济与报告共用这一份，
        /// 不在 `SkyIslandItems` 定义表里另写一份数字。批次三的口径：材料按「采一处约一两件」定低价；
        /// 凑整与合成只给小幅溢价（晴岚风晶 +16%、便当 +88%、药膏 +48%，护符与罗盘基本持平），不当换钱的路子。
        /// </summary>
        internal static int ValueOf(int typeId)
        {
            switch (typeId)
            {
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
                case "Keepsake_Compass": return SkyIslandLetters.CollectedCount(data) > 0;
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
