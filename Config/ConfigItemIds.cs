// ============================================================================
// ConfigItemIds.cs - BossRush 物品 TypeID 常量表
// ============================================================================
// 从 Config/Config.cs 拆出，只为单文件行数预算（LargeFileBudgetGuard 硬上限 1200 行）；
// 语义未变，仍是同一个 `public static partial class BossRushItemIds`
// （遗种巢的 RelicEgg 在 Config/ConfigPetNest.cs 里，也是这个 partial 的一部分）。
//
// 台账见 docs/Bossrush使用物品ID表.md 与 AGENTS.md 4.3：
// TypeID 严格递增、不复用、不回填已删 ID。
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// BossRush 物品 TypeID 常量表。
    /// 仅收录当前没有独立 Config 类、但会被多个模块复用的物品 ID。
    /// </summary>
    public static partial class BossRushItemIds
    {
        public const int BossRushTicket = 500001;
        public const int BirthdayCake = 500002;
        public const int AdventureJournal = 500007;
        public const int ZombieTideInvitation = 500045;
        public const int ZombieTideBeacon = 500046;
        // 500047 是保留空洞，不回填、不复用（见 AGENTS.md §4.3 与 docs/contracts.md §1）。
        // 此前这里有一个 SpeedrunGauntletTicket = 500047 常量，属被删功能的残留：
        // 全 Mod 零使用点，且与三份契约文档「500047 为空洞」的记载直接矛盾，已移除。
        public const int PortableSafeZoneDevice = 500058;

        // 天空岛（晴岚群岛）物品 500068-500072：纪念品、道具与岛上特产（Integration/SkyIsland/SkyIslandItems.cs）。
        // 放在这张无依赖的表里，是因为岛上的纯规则（DebugAndTools/SkyIsland/SkyIslandItemRules.cs）也要引用，
        // 而那份规则由隔离回归直接链接。
        /// <summary>晴岚航徽：敲响归航钟的纪念品。</summary>
        public const int SkyIslandHomecomingBadge = 500068;
        /// <summary>噬风之核：击败噬风的纪念品。</summary>
        public const int SkyIslandWindeaterCore = 500069;
        /// <summary>风标罗盘：在群岛上指向信鸽或下一个目标的道具，使用不消耗。</summary>
        public const int SkyIslandWindVaneCompass = 500070;
        /// <summary>归航菜便当：岛上特产食物（饱食、水分与少量生命）。</summary>
        public const int SkyIslandHomecomingBento = 500071;
        /// <summary>星苔药膏：岛上特产药品（回复生命）。</summary>
        public const int SkyIslandStarmossSalve = 500072;
    }
}
