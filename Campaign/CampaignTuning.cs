// ============================================================================
// CampaignTuning.cs - 鸭王征程数值与标识常量单点（M0 骨架）
// ============================================================================
// 归位依据 AGENTS.md 4.8「Config 三层归位」第 2 层：玩法强耦合常量放模块配置类。
// 只有入口总开关 campaignEnabled 走 Config/Config.cs + ModConfig（第 1 层），
// 章节内容表走 Assets/Data/Campaign/*.json（第 3 层），其余常量一律在这里。
//
// 【冻结契约】以下常量进入存档键、跨系统 token 与官方笔记键，发布后不得改名：
//   - ProgressSaveKey、FacilityTokenPrefix、BoardBuildingId、NoteKeyPrefix
//   登记见 docs/contracts.md。
//
// 【数值待 owner 审定】终章倍率、章节奖金、公告板造价均为草案，改动只需改本文件。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    /// <summary>鸭王征程常量。无状态、无逻辑，只有 const / static readonly。</summary>
    internal static class CampaignTuning
    {
        #region 模块标识

        /// <summary>模块名（host 日志与 owner label 使用）。</summary>
        internal const string ModuleName = "Campaign";

        /// <summary>日志前缀。全模块统一，便于 Player.log 过滤。</summary>
        internal const string LogPrefix = "[Campaign] ";

        #endregion

        #region 冻结契约常量（发布后不得改名）

        /// <summary>战役进度存档键。槽位隔离由 SavesSystem 天然保证。</summary>
        internal const string ProgressSaveKey = "BossRush_Campaign_Progress_v1";

        /// <summary>
        /// 设施解锁 token 前缀。完整形态 `BossRush_Campaign_Unlock_Ch1`..`_Ch6`。
        /// 后山系统按 token 自持映射，两侧解耦；见 CampaignFacilityUnlocks。
        /// </summary>
        internal const string FacilityTokenPrefix = "BossRush_Campaign_Unlock_Ch";

        /// <summary>公告板建筑字符串 ID。不占 TypeID 序列（照 wedding_chapel 先例）。</summary>
        internal const string BoardBuildingId = "bossrush_campaign_board";

        /// <summary>
        /// 线索在官方 NoteIndex 里的键前缀。官方按 `Note_{key}_Title/_Content`
        /// 取本地化，因此这个前缀同时进本地化键，双重冻结。
        /// </summary>
        internal const string NoteKeyPrefix = "BossRushCampaign_";

        #endregion

        #region 章节结构

        /// <summary>章节总数。第 1-5 章派往现有模式，第 6 章竞技场决战。</summary>
        internal const int ChapterCount = 6;

        /// <summary>最小章节序号（章节 order 从 1 起，0 保留为「无」）。</summary>
        internal const int FirstChapter = 1;

        #endregion

        #region 公告板（草案，待 owner 审定）

        /// <summary>公告板建造费用。</summary>
        internal const int BoardBuildCost = 500;

        #endregion

        #region 终章 Boss 变体（草案，待 owner 审定）

        /// <summary>终章 Boss 在其自身生成倍率之上再叠加的战役倍率。</summary>
        internal const float FinalBossStatMultiplier = 1.6f;

        /// <summary>终章 Boss 体型放大系数。纯视觉，不影响碰撞判定口径。</summary>
        internal const float FinalBossScale = 1.15f;

        /// <summary>终章 Boss 染色（绯红）。走 MaterialPropertyBlock，不改共享材质。</summary>
        internal static readonly Color FinalBossTint = new Color(0.85f, 0.15f, 0.2f, 1f);

        #endregion
    }
}
