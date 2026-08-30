// ============================================================================
// CodexTuning.cs - 鸭皇图鉴数值/身份常量单点
// ============================================================================
// 归位依据 AGENTS.md 4.8「Config 三层归位」第 2 层：玩法强耦合常量放模块配置类。
// 只有入口总开关 codexEnabled 走 Config/ConfigCodex.cs + ModConfig（第 1 层），
// 其余数值一律在这里，owner 审定后单点改。
//
// 硬约束：
//   - 本类无状态、无逻辑，只有 const / static readonly，禁止在这里缓存开关或存档；
//   - StorageKey / CurrentSchemaVersion / ModeId* / 丧尸合成 key 属于**存档兼容面**
//     （AGENTS.md §5），发布后冻结：只增不改、不改语义、不复用；
//   - 里程碑现金数额是 Achievement/BossRushAchievementManager.cs 注册值的镜像，
//     真正发奖由成就系统负责，这里只做单点台账，改动必须两侧同步。
// ============================================================================

namespace BossRush
{
    /// <summary>图鉴数值/身份常量。无状态、无逻辑，只有 const / static readonly。</summary>
    internal static class CodexTuning
    {
        #region 身份

        /// <summary>运行时模块名（宿主模块表用）。</summary>
        internal const string ModuleName = "Codex";

        /// <summary>日志前缀。所有图鉴日志统一带它，方便过滤。</summary>
        internal const string LogPrefix = "[Codex] ";

        /// <summary>本地化 key 前缀（BossRush_Codex_*）。</summary>
        internal const string LocalizationPrefix = "BossRush_Codex_";

        #endregion

        #region 存档（v1 冻结，只增不改）

        /// <summary>存档 key。整存 JSON 字符串，跟槽位，不是 SaveGlobal。</summary>
        internal const string StorageKey = "BossRush_Codex_v1";

        /// <summary>当前 schema 版本。读到不等于它的一律进写屏障（只读不覆盖）。</summary>
        internal const int CurrentSchemaVersion = 1;

        /// <summary>存档条目硬上限。超出后不再新增条目（已有条目照常累计）。</summary>
        internal const int MaxEntries = 256;

        #endregion

        #region 采集

        /// <summary>
        /// 最快击杀计时表容量上限（防泄漏）。满了就整表清空重来：
        /// 计时只是锦上添花的统计，宁可丢一批计时，也不能让字典无限膨胀。
        /// </summary>
        internal const int MaxFightStartTracked = 64;

        /// <summary>丧尸 Boss 合成 key 前缀。存档兼容面，冻结。</summary>
        internal const string ZombieBossKeyPrefix = "zombie_boss_";

        /// <summary>
        /// 五个丧尸 Boss 的合成 key 常量。
        /// 运行时统一由 CodexBossCatalog.BuildZombieBossKey(kind) 生成，
        /// 这里只是把已冻结的取值域显式列一份，供台账核对与将来 guard 断言。
        /// </summary>
        internal const string ZombieBossKeyTitan = ZombieBossKeyPrefix + "Titan";
        internal const string ZombieBossKeyHunter = ZombieBossKeyPrefix + "Hunter";
        internal const string ZombieBossKeySplitter = ZombieBossKeyPrefix + "Splitter";
        internal const string ZombieBossKeyShielder = ZombieBossKeyPrefix + "Shielder";
        internal const string ZombieBossKeyCorruptor = ZombieBossKeyPrefix + "Corruptor";

        #endregion

        #region 模式 id（存档字段 fm 的取值域，发布后冻结）

        /// <summary>标准 BossRush 竞技场。</summary>
        internal const string ModeIdArena = "arena";
        internal const string ModeIdModeD = "modeD";
        internal const string ModeIdModeE = "modeE";
        internal const string ModeIdModeF = "modeF";
        internal const string ModeIdModeG = "modeG";
        internal const string ModeIdModeH = "modeH";
        internal const string ModeIdZombie = "zombie";
        /// <summary>原版撤离图（不在任何 Mod 模式内时的回落值）。</summary>
        internal const string ModeIdRaid = "raid";
        /// <summary>无间炼狱（可解析时；解析不到回落 arena）。</summary>
        internal const string ModeIdHell = "hell";

        #endregion

        #region 立绘

        /// <summary>立绘 AssetBundle 名。</summary>
        internal const string PortraitBundleName = "codex_portraits";

        /// <summary>立绘 bundle 相对 Mod 根目录的路径。</summary>
        internal const string PortraitBundleRelativePath = "Assets/ui/codex_portraits";

        /// <summary>立绘资产名前缀。完整名 = 前缀 + bossKey 全小写。</summary>
        internal const string PortraitAssetPrefix = "codex_portrait_";

        /// <summary>开发期散图目录（仅开发构建走，缺失即跳过）。</summary>
        internal const string PortraitDevRawRelativeDir = "Assets/ui/Codex";

        #endregion

        #region 里程碑（成就 id 在 BossRushAchievementManager 注册）

        internal const string AchievementFirstEntry = "codex_first_entry";
        internal const string AchievementTen = "codex_10";
        internal const string AchievementTwenty = "codex_20";
        internal const string AchievementAll = "codex_all";
        internal const string AchievementFastKill = "codex_fast_kill";

        /// <summary>首条阈值：解锁条目数 &gt;= 1。</summary>
        internal const int MilestoneFirstThreshold = 1;
        internal const int MilestoneTenThreshold = 10;
        internal const int MilestoneTwentyThreshold = 20;

        /// <summary>速杀成就阈值（秒）。本次击杀有效计时且 &lt;= 它才算。</summary>
        internal const float FastKillSeconds = 10f;

        /// <summary>
        /// 里程碑现金数额台账（镜像 Achievement/BossRushAchievementManager.cs 的注册值）。
        /// 图鉴侧**不**发奖，只调 BossRushAchievementManager.TryUnlock；
        /// 这里存一份是为了数值评审时能在单点看全，改动必须两侧同步。
        /// </summary>
        internal const long MilestoneCashFirstEntry = 50000L;
        internal const long MilestoneCashTen = 150000L;
        internal const long MilestoneCashTwenty = 400000L;
        internal const long MilestoneCashAll = 1000000L;
        internal const long MilestoneCashFastKill = 200000L;

        #endregion

        #region UI

        /// <summary>网格列数。</summary>
        internal const int GridColumns = 4;
        internal const float CardWidth = 168f;
        internal const float CardHeight = 210f;
        internal const float CardSpacing = 12f;

        /// <summary>锁定条目立绘 tint（同图压黑当剪影）。</summary>
        internal static readonly UnityEngine.Color LockedPortraitTint =
            new UnityEngine.Color(0.06f, 0.07f, 0.08f, 1f);

        #endregion
    }
}
