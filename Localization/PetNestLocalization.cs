// ============================================================================
// PetNestLocalization.cs - 遗种巢本地化的唯一 source of truth（实施计划 步骤 4 / 10）
// ============================================================================
// 形态照 Localization/ModeHLocalization.cs：独立文件而不是继续膨胀
// LocalizationInjector.cs。接线点是
// Integration/BossRushIntegration_StartAndScene.cs 的 InjectLocalization_Extra_Integration()。
//
// 冻结要求（AGENTS.md 4.4）：凡设置 DisplayNameRaw = "BossRush_PetNest_*" 的物品、
// 面板文案、演出提示都必须在这里注入；缺失会在游戏内显示 *raw key*。
//
// 前缀统一 PetNestTuning.LocalizationPrefix = "BossRush_PetNest_"。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>遗种巢全部 `BossRush_PetNest_` 键的注入入口。</summary>
    public static class PetNestLocalization
    {
        /// <summary>把全部遗种巢键注入官方本地化表。</summary>
        public static void Inject()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            AddCore(map);
            AddDropAndSouls(map);
            AddFailureReasons(map);
            AddPersonalities(map);
            AddDestinationsAndRisk(map);
            LocalizationHelper.InjectLocalizations(map);

            // 遗种蛋物品自身的键（含 Var_ 展示名）由物品配置注入
            RelicEggConfig.InjectLocalization();
            // 建筑与交互点的键（官方 Building_ 前缀，不带本模块前缀）
            InjectBuildingKeys();
        }

        private static void Add(Dictionary<string, string> map, string suffix, string cn, string en)
        {
            map[PetNestTuning.LocalizationPrefix + suffix] = L10n.T(cn, en);
        }

        #region 核心

        private static void AddCore(Dictionary<string, string> map)
        {
            Add(map, "SystemName", "遗种巢", "PetNest");
            Add(map, "SystemDesc",
                "打过的每一个 Boss 都可能留下遗种。把它们孵出来、起个名字、带在身边打下一场，"
                + "或者派它们去你不敢去的天灾深处。",
                "Every boss you have killed may leave a relic behind. Hatch them, name them, "
                + "take them along for the next run, or send them into disasters you would not face yourself.");
            Add(map, "Page_Nest", "巢", "Nest");
            Add(map, "Page_Hatch", "孵化", "Hatch");
            Add(map, "Page_Expedition", "天灾远征", "Disaster Expedition");
            Add(map, "Page_Museum", "遗种博物馆", "Relic Museum");
            Add(map, "Page_Memorial", "阵亡纪念碑", "Memorial");

            // 命名弹窗（SystemDesc 对玩家承诺过"起个名字"，入口在 PetNestRenameModal）
            Add(map, "Rename_Title", "给它起个名字", "Name this cub");
            Add(map, "Rename_Hint", "留空并点「用回血脉名」可以恢复默认称呼。",
                "Leave it empty and pick \"Use bloodline name\" to restore the default.");
            Add(map, "Rename_Confirm", "就叫这个", "Confirm");
            Add(map, "Rename_Reset", "用回血脉名", "Use bloodline name");
        }

        #endregion

        #region 掉落与遗魂

        private static void AddDropAndSouls(Dictionary<string, string> map)
        {
            Add(map, "Soul", "Boss 遗魂", "Boss Relic Soul");
            Add(map, "SoulDesc",
                "击杀 Boss 必得的血脉残响。只用于在巢中定向凝成该血脉的遗种蛋，不作货币、不进商店。",
                "A bloodline echo guaranteed by every boss kill. It is only used to condense a relic egg "
                + "of that bloodline; it is not currency and never enters shops.");
            Add(map, "SoulGained", "获得遗魂", "Relic souls gained");
            Add(map, "SoulLedger", "遗魂账本", "Relic Soul Ledger");
            Add(map, "CondenseEgg", "凝成遗种蛋", "Condense Relic Egg");
            Add(map, "CondenseProgress", "凝蛋进度", "Condense Progress");
        }

        #endregion

        #region 失败原因（面板提示用；口径与服务层 failureReasonId 一一对应）

        private static void AddFailureReasons(Dictionary<string, string> map)
        {
            Add(map, "Fail_nest_full", "巢已经满了", "The nest is full");
            Add(map, "Fail_pet_not_found", "找不到这只崽", "That cub cannot be found");
            Add(map, "Fail_pet_duplicate", "这只崽已经在巢里了", "That cub is already in the nest");
            Add(map, "Fail_pet_locked_by_expedition", "它正在远征途中", "It is away on an expedition");
            Add(map, "Fail_pet_downed", "它本局已经重伤退场", "It has already been carried off this run");
            Add(map, "Fail_souls_insufficient", "遗魂不够", "Not enough relic souls");
            Add(map, "Fail_lineage_unknown", "这枚蛋的血脉无法识别，已原样保留",
                "This egg's bloodline cannot be identified; the egg is left untouched");
            Add(map, "Fail_save_write_barrier", "存档为只读状态，本次改动未保存",
                "The save is read-only; this change was not stored");
            Add(map, "Fail_save_store_faulted", "存档写入故障，本次改动未保存",
                "The save failed to write; this change was not stored");
        }

        #endregion

        #region 性格

        private static void AddPersonalities(Dictionary<string, string> map)
        {
            Add(map, "Personality_reckless", "莽撞", "Reckless");
            Add(map, "Personality_cautious", "谨慎", "Cautious");
            Add(map, "Personality_lazy", "懒散", "Lazy");
            Add(map, "Personality_loyal", "忠诚", "Loyal");
        }

        #endregion

        #region 远征目的地与档位

        /// <summary>
        /// 建筑与交互点的键。官方约定 BuildingInfo.DisplayNameKey = "Building_" + id，
        /// DescriptionKey = "Building_" + id + "_Desc"，因此这一组不带 BossRush_PetNest_ 前缀。
        /// 由建筑注入器在注入前调用（建造 UI 会立刻读这些键）。
        /// </summary>
        public static void InjectBuildingKeys()
        {
            try
            {
                string name = L10n.T("遗种巢", "PetNest");
                string desc = L10n.T(
                    "把 Boss 留下的遗种孵成幼体、带在身边打下一场，或者派它们去天灾深处。",
                    "Hatch the relics bosses leave behind, take a cub along for your next run, "
                    + "or send it into a disaster you would not face yourself.");

                LocalizationHelper.InjectLocalization("Building_petnest_relic_nest", name);
                LocalizationHelper.InjectLocalization("Building_petnest_relic_nest_Desc", desc);

                LocalizationHelper.InjectLocalization(
                    "BossRush_PetNest_Interact", L10n.T("查看遗种巢", "Open PetNest"));
                LocalizationHelper.InjectLocalization(
                    "BossRush_PetNest_Interact_Hatch", L10n.T("孵化遗种蛋", "Hatch Relic Egg"));
                LocalizationHelper.InjectLocalization(
                    "BossRush_PetNest_Interact_Expedition", L10n.T("派遣天灾远征", "Send on Expedition"));
                LocalizationHelper.InjectLocalization(
                    "BossRush_PetNest_Interact_Museum", L10n.T("遗种博物馆", "Relic Museum"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 建筑本地化注入失败: " + e.Message);
            }
        }

        private static void AddDestinationsAndRisk(Dictionary<string, string> map)
        {
            Add(map, "Dest_storm_sea", "风暴海域", "Storm Sea");
            Add(map, "Dest_acid_ruins", "酸雨废墟", "Acid Rain Ruins");
            Add(map, "Dest_frozen_waste", "极寒荒原", "Frozen Waste");

            Add(map, "Risk_safe", "平安", "Safe");
            Add(map, "Risk_rough", "风浪", "Rough");
            Add(map, "Risk_desperate", "亡命", "Desperate");

            Add(map, "DeathRateLabel", "死亡率", "Death rate");
            Add(map, "ElementAffinity", "元素亲和", "Element affinity");
        }

        #endregion

        #region 查询辅助

        /// <summary>
        /// 失败原因 id -> 玩家可读文案。未登记的 id 原样返回 id 本身
        /// （GetLocalizedText 找不到时会回传 key，这里据此判定"未登记"）。
        /// </summary>
        public static string DescribeFailure(string failureReasonId)
        {
            if (string.IsNullOrEmpty(failureReasonId)) return string.Empty;
            string key = PetNestTuning.LocalizationPrefix + "Fail_" + failureReasonId;
            string text = LocalizationHelper.GetLocalizedText(key);
            if (string.IsNullOrEmpty(text) || text == key) return failureReasonId;
            return text;
        }

        #endregion
    }
}
