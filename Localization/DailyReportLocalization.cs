// ============================================================================
// DailyReportLocalization.cs - 鸭科夫日报本地化的唯一 source of truth
// ============================================================================
// 形态照 Localization/PetNestLocalization.cs：独立文件而不是继续膨胀
// LocalizationInjector.cs。接线点是
// Integration/BossRushIntegration_StartAndScene.cs 的 InjectLocalization_Extra_Integration()。
//
// 范围说明（重要）：
//   日报的**正文是动态长文**，每天都不一样，不进本地化表——LocalizationManager 是
//   全局字符串表，塞进去既污染表也无法按天变。正文一律在 DailyReportContent.cs 里
//   走 L10n.T(cn, en) 双语内联。
//   这里只注入**官方系统会主动去查表**的 key：建筑名/描述与交互提示名。
//
// 前缀统一 DailyReportTuning.LocalizationPrefix = "BossRush_DailyReport_"；
// 建筑 key 例外，官方硬编码为 "Building_" + id，不带本模块前缀。
// ============================================================================

using System.Collections.Generic;

namespace BossRush
{
    /// <summary>日报本地化键的注入入口。</summary>
    public static class DailyReportLocalization
    {
        /// <summary>把全部日报键注入官方本地化表。</summary>
        public static void Inject()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            AddCore(map);
            LocalizationHelper.InjectLocalizations(map);

            InjectBuildingKeys();
        }

        private static void Add(Dictionary<string, string> map, string suffix, string cn, string en)
        {
            map[DailyReportTuning.LocalizationPrefix + suffix] = L10n.T(cn, en);
        }

        private static void AddCore(Dictionary<string, string> map)
        {
            // 只注入官方会主动查表的 key。报纸刊头、签到按钮等由 UI 侧内联 L10n.T
            // 直接给双语字面量（刊头还带刻意的字距版式），不走注入表。
            Add(map, "Interact", "阅读今日报纸", "Read today's paper");
        }

        /// <summary>
        /// 建筑名与描述。官方按 "Building_" + id 硬编码查表，
        /// 缺这两条会在建造 UI 里显示 *Building_bossrush_daily_mailbox*。
        /// 建筑注入器在创建 prefab 之前会先调它，顺序不能颠倒。
        /// </summary>
        public static void InjectBuildingKeys()
        {
            string buildingKey = "Building_" + DailyReportTuning.MailboxBuildingId;

            Dictionary<string, string> map = new Dictionary<string, string>();
            map[buildingKey] = L10n.T("报箱", "Mailbox");
            map[buildingKey + "_Desc"] = L10n.T(
                "订阅《鸭科夫日报》。每过一天，报童会把新一期塞进来："
                    + "昨日战绩、今日悬赏、天气预报，还有签到奖励。",
                "Subscribe to The Duckov Daily. Each day the paper boy drops off a new issue: "
                    + "yesterday's recap, today's bounty, the forecast, and your check-in reward.");

            LocalizationHelper.InjectLocalizations(map);
        }
    }
}
