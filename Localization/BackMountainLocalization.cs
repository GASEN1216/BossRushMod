// ============================================================================
// BackMountainLocalization.cs - 竞技场后山本地化的唯一 source of truth
// ============================================================================
// 形态照 Localization/DailyReportLocalization.cs。接线点是
// Integration/BossRushIntegration_StartAndScene.cs 的 InjectLocalization_Extra_Integration()。
//
// 范围：只注入**官方系统会主动去查表**的 key（建筑名/描述、交互提示名）。
// 面板文本走 UI 侧内联 L10n.T；物品名由 BackMountainItems.InjectLocalization 负责
// （那边与物品定义表在一起，避免同一份名字写两遍）。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>后山本地化键的注入入口。</summary>
    public static class BackMountainLocalization
    {
        /// <summary>把全部后山键注入官方本地化表。</summary>
        public static void Inject()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            map["BossRush_BackMountain_Showcase_Interact"] =
                L10n.T("查看战利品登记簿", "Check the trophy record");
            LocalizationHelper.InjectLocalizations(map);

            InjectBuildingKeys();
        }

        /// <summary>
        /// 建筑名与描述。官方按 "Building_" + id 硬编码查表，
        /// 缺这两条会在建造 UI 里显示 *Building_bossrush_backmountain_showcase*。
        /// 建筑注入器在创建 prefab 之前会先调它，顺序不能颠倒。
        /// </summary>
        public static void InjectBuildingKeys()
        {
            string buildingKey = "Building_" + BackMountainConfig.ShowcaseBuildingId;

            Dictionary<string, string> map = new Dictionary<string, string>();
            map[buildingKey] = L10n.T("战利品展示柜", "Trophy Showcase");
            map[buildingKey + "_Desc"] = L10n.T(
                "一台带玻璃罩的陈列台。把打到过的高品质战利品拿来登记，柜子会替你记着——"
                + "东西照样归你，登记得越多，你越经打。",
                "A display counter under glass. Bring the high-quality trophies you've earned and have them "
                + "recorded — you keep the gear either way, and the longer the list, the tougher you get.");

            LocalizationHelper.InjectLocalizations(map);
        }
    }
}
