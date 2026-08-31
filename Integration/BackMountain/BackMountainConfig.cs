// ============================================================================
// BackMountainConfig.cs - 竞技场后山常量单点（M0 骨架）
// ============================================================================
// 归位依据 AGENTS.md 4.8 第 2 层：玩法强耦合常量放模块配置类。
// 只有两个入口开关走 Config/Config.cs + ModConfig（第 1 层），
// 曲目映射等数据表走 Assets/Data/BackMountain/*.json（第 3 层）。
//
// 【冻结契约】建筑 ID、cropID 字符串、存档键发布后不得改名（docs/contracts.md）。
//
// 【设施与章节的映射在这里，不在战役侧】
//   战役只发「第 N 章通行 token」，token → 具体设施由后山自持，两侧解耦。
//   将来调整哪一章解锁什么，只改本文件，战役侧零改动。
// ============================================================================

namespace BossRush
{
    /// <summary>后山设施标识。</summary>
    internal enum BackMountainFacility
    {
        /// <summary>未知/解析失败。</summary>
        None = 0,

        /// <summary>菜地：Boss 掉种子 → 官方种植系统 → 出击餐。</summary>
        Garden = 1,

        /// <summary>战利品展示柜：摆 Boss 专属掉落 → 跨局属性小加成。</summary>
        Showcase = 2,

        /// <summary>点唱机战歌：基地点唱机追加 mod 曲目。</summary>
        Jukebox = 3
    }

    /// <summary>后山常量。无状态、无逻辑，只有 const / static readonly。</summary>
    internal static class BackMountainConfig
    {
        #region 模块标识

        /// <summary>模块名（host 日志与 owner label 使用）。</summary>
        internal const string ModuleName = "BackMountain";

        /// <summary>日志前缀。</summary>
        internal const string LogPrefix = "[BackMountain] ";

        #endregion

        #region 冻结契约常量（发布后不得改名）

        /// <summary>展示柜建筑字符串 ID。不占 TypeID 序列。</summary>
        internal const string ShowcaseBuildingId = "bossrush_backmountain_showcase";

        /// <summary>展示柜收藏的存档键。</summary>
        internal const string ShowcaseSaveKey = "BossRush_BackMountain_Showcase_v1";

        /// <summary>出击餐待生效登记的存档键。</summary>
        internal const string RaidMealSaveKey = "BossRush_BackMountain_RaidMeal_v1";

        /// <summary>自定义作物 ID 前缀。官方 CropInfo.id 是字符串，不占 TypeID。</summary>
        internal const string CropIdPrefix = "BossRush_Crop_";

        #endregion

        #region 设施解锁映射（章节 → 设施）

        /// <summary>菜地解锁所需章节。</summary>
        internal const int GardenUnlockChapter = 1;

        /// <summary>展示柜解锁所需章节。</summary>
        internal const int ShowcaseUnlockChapter = 2;

        /// <summary>点唱机战歌解锁所需章节。</summary>
        internal const int JukeboxUnlockChapter = 3;

        /// <summary>
        /// 设施 → 所需章节。未登记的设施返回 0，调用方一律视为「不可解锁」。
        /// </summary>
        internal static int GetRequiredChapter(BackMountainFacility facility)
        {
            switch (facility)
            {
                case BackMountainFacility.Garden:
                    return GardenUnlockChapter;
                case BackMountainFacility.Showcase:
                    return ShowcaseUnlockChapter;
                case BackMountainFacility.Jukebox:
                    return JukeboxUnlockChapter;
                default:
                    return 0;
            }
        }

        #endregion

        #region 数据表文件名（Assets/Data/BackMountain/）

        /// <summary>Boss 战 BGM 与 stinger 的曲目映射表。</summary>
        internal const string BgmTracksDataFile = "BgmTracks.json";

        #endregion

        #region 展示柜（草案，待 owner 审定）

        /// <summary>展示柜建造费用。</summary>
        internal const int ShowcaseBuildCost = 800;

        /// <summary>展示柜收藏格数上限。</summary>
        internal const int ShowcaseSlotCount = 8;

        #endregion
    }
}
