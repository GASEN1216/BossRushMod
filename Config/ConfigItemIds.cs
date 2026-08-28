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
    }
}
