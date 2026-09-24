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
            AddStatsAndTalents(map);
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
                "你打过的 Boss 都可能留下遗种。孵出来、起个名字、带着打下一场，"
                + "或者派去你自己都不敢去的天灾区。",
                "Every boss you have killed may leave a relic behind. Hatch them, name them, "
                + "take them along for the next run, or send them into disasters you would not face yourself.");
            Add(map, "Page_Nest", "巢", "Nest");
            Add(map, "Page_Hatch", "孵化", "Hatch");
            Add(map, "Page_Expedition", "天灾远征", "Disaster Expedition");
            Add(map, "Page_Museum", "遗种博物馆", "Relic Museum");
            Add(map, "Page_Memorial", "阵亡纪念碑", "Memorial");

            // 命名弹窗（SystemDesc 对玩家承诺过"起个名字"，入口在 PetNestRenameModal）
            Add(map, "Rename_Title", "给它起个名字", "Name this cub");
            Add(map, "Rename_Hint", "点「用回血脉名」会把默认称呼填回框里，再点「就叫这个」确认。",
                "\"Use bloodline name\" puts the default back in the box; then press \"Confirm\".");
            Add(map, "Rename_Confirm", "就叫这个", "Confirm");
            Add(map, "Rename_Reset", "用回血脉名", "Use bloodline name");

            // 放生与巢扩建
            Add(map, "Release_Action", "放生选中的崽", "Release selected cub");
            Add(map, "Release_Title", "放生确认", "Release this cub?");
            Add(map, "Release_Warn",
                "放生不可逆：它将永远离开巢，也不会进纪念碑。你会收回一部分同血脉遗魂。",
                "Releasing is permanent: the cub leaves the nest for good and is not memorialized. "
                + "You get back some relic souls of its bloodline.");
            Add(map, "Release_Confirm", "放生", "Release");
            Add(map, "Release_Cancel", "再想想", "Keep it");
            Add(map, "CapacityMilestoneHint",
                "解锁更多血脉可以扩建巢", "Unlock more bloodlines to expand the nest");
            Add(map, "Level_Adult", "成年", "Adult");
        }

        #endregion

        #region 掉落与遗魂

        private static void AddDropAndSouls(Dictionary<string, string> map)
        {
            // 说明：遗魂没有实体道具，只有账本，因此不需要单独的物品名 key。
            Add(map, "SoulDesc",
                "每杀一个 Boss 必得的遗魂。只能在巢里凝成同血脉的遗种蛋，不能当钱花，商店也不收。",
                "Every boss kill leaves some of these behind. They only condense into a relic egg "
                + "of the same bloodline. Not currency; no shop will take them.");
            Add(map, "SoulGained", "获得遗魂", "Relic souls gained");
            Add(map, "SoulLedger", "遗魂账本", "Relic Soul Ledger");
            Add(map, "CondenseEgg", "凝成遗种蛋", "Condense Relic Egg");
            Add(map, "CondenseProgress", "凝蛋进度", "Condense Progress");
        }

        #endregion

        #region 失败原因（面板提示用；口径与服务层 failureReasonId 一一对应）

        /// <summary>
        /// 失败原因文案。**覆盖面是硬要求**：`DescribeFailure` 查不到键时会原样返回 id，
        /// 而 `PetNestCompanionRuntime.NotifyBlockReasonOnce` 会把它直接 `ShowMessage` 给玩家——
        /// 漏一个键，玩家就会在屏幕上读到 `mode_g_banned` 这种裸串（违反 AGENTS §4.4）。
        /// 带 `:异常名` 后缀的 id（`depart_failed:NullReferenceException` 等）由
        /// `DescribeFailure` 截断到冒号前再查，因此这里只登记前缀。
        /// 覆盖面由 `tests/PetNestUILayerGuard.py` 逐条核对。
        /// </summary>
        private static void AddFailureReasons(Dictionary<string, string> map)
        {
            // —— 巢 CRUD 与席位 ——
            Add(map, "Fail_nest_full", "巢已经满了", "The nest is full");
            Add(map, "Fail_pet_not_found", "找不到这只崽", "That cub cannot be found");
            Add(map, "Fail_pet_duplicate", "这只崽已经在巢里了", "That cub is already in the nest");
            Add(map, "Fail_pet_locked_by_expedition", "它正在远征途中", "It is away on an expedition");
            Add(map, "Fail_pet_downed", "它本局已经重伤退场", "It has already been carried off this run");
            Add(map, "Fail_pet_invalid", "这只崽的数据不完整，无法入巢",
                "This cub's data is incomplete and cannot enter the nest");
            Add(map, "Fail_souls_insufficient", "遗魂不够", "Not enough relic souls");
            Add(map, "Fail_invalid_request", "这个操作的参数不对", "That request was not valid");

            // —— 孵化 ——
            Add(map, "Fail_lineage_unknown", "这枚蛋的血脉无法识别，已原样保留",
                "This egg's bloodline cannot be identified; the egg is left untouched");
            Add(map, "Fail_egg_missing", "找不到这枚蛋", "That egg is gone");
            Add(map, "Fail_egg_owner_missing", "这枚蛋不在背包或仓库里，先把它放回容器",
                "That egg is not in your inventory or storage; put it back into a container first");
            Add(map, "Fail_egg_detach_failed", "取出这枚蛋失败，已原样保留",
                "The egg could not be taken out; it is left untouched");
            Add(map, "Fail_roll_failed", "孵化失败，蛋已原样保留",
                "Hatching failed; the egg is left untouched");
            Add(map, "Fail_hatch_stats_failed", "图鉴统计写入失败，本次孵化已撤销",
                "Codex stats could not be written; this hatch was rolled back");
            Add(map, "Fail_add_pet_failed", "入巢失败，本次改动未保存",
                "The cub could not enter the nest; nothing was saved");

            // —— 远征 ——
            Add(map, "Fail_destination_unknown", "没有这个远征目的地", "No such expedition destination");
            Add(map, "Fail_record_missing", "找不到这条远征记录", "That expedition record is gone");
            Add(map, "Fail_not_settled", "这趟远征还没结算", "That expedition has not been settled yet");
            Add(map, "Fail_not_due", "还没到回来的时间", "It is not due back yet");
            Add(map, "Fail_depart_failed", "派遣失败，本次改动未保存",
                "Dispatch failed; nothing was saved");
            Add(map, "Fail_settle_failed", "远征结算失败，稍后会重试",
                "Settling the expedition failed; it will be retried");

            // —— 随从进局（模式门控与入场）——
            Add(map, "Fail_mode_g_banned", "宿命回响不能带崽：宿敌的账只算你自己打出来的伤害",
                "No cubs in Echoes of Fate: the nemesis duel only counts damage you deal yourself");
            Add(map, "Fail_zombie_mode_banned", "末日丧尸模式不能带崽：它有自己的一套局内奖励",
                "No cubs in the Zombie mode: it runs its own in-run reward system");
            Add(map, "Fail_mode_h_banned", "百战留痕是观战模式，你不下场，崽也不上场",
                "Blackmarket Cup is a spectator mode: you stay out of the ring, and so does your cub");
            Add(map, "Fail_no_run_active", "这张图不支持带崽出战",
                "Cubs cannot be deployed on this map");
            Add(map, "Fail_mode_query_failed", "没能判断当前模式，本局不带崽",
                "Could not identify the current mode; the cub stays home this run");
            Add(map, "Fail_pet_downed_this_run", "它本局已经重伤退场，回基地才会恢复",
                "It was carried off this run and only recovers back at base");
            Add(map, "Fail_lineage_preset_missing", "这条血脉的角色模板不可用，本局不带崽",
                "This bloodline's character preset is unavailable; the cub stays home this run");
            Add(map, "Fail_companion_handle_invalid", "随从生成失败，本局不带崽",
                "The cub could not be created; it stays home this run");
            Add(map, "Fail_companion_activate_failed", "随从入场失败，本局不带崽",
                "The cub failed to enter the field; it stays home this run");

            // —— 存档 ——
            Add(map, "Fail_save_write_barrier", "存档为只读状态，本次改动未保存",
                "The save is read-only; this change was not stored");
            Add(map, "Fail_save_store_faulted", "存档写入故障，本次改动未保存",
                "The save failed to write; this change was not stored");
            Add(map, "Fail_transaction_missing", "没有进行中的存档事务，本次改动未保存",
                "No save transaction was open; this change was not stored");
            Add(map, "Fail_nested_transaction", "上一次存档操作还没结束，请稍后再试",
                "The previous save operation is still running; try again in a moment");
            Add(map, "Fail_transaction_clone_failed", "存档快照失败，本次改动未保存",
                "The save snapshot failed; this change was not stored");
            Add(map, "Fail_asset_save_not_ready", "背包与仓库还没就绪，请稍后再试",
                "Inventory and storage are not ready yet; try again in a moment");
            Add(map, "Fail_commit_failed", "本次改动未能保存", "This change could not be stored");
            Add(map, "Fail_remove_pet_failed", "本次改动未能保存", "This change could not be stored");
            Add(map, "Fail_release_pet_failed", "放生未能保存", "The release could not be stored");
            Add(map, "Fail_release_failed", "放生未能保存", "The release could not be stored");
            Add(map, "Fail_rename_pet_failed", "改名未能保存", "The new name could not be stored");
            Add(map, "Fail_rename_failed", "改名未能保存", "The new name could not be stored");
            Add(map, "Fail_set_deployed_failed", "设为出战未能保存",
                "Deploying the cub could not be stored");
            Add(map, "Fail_clear_deployed_failed", "清空出战席位未能保存",
                "Clearing the deploy slot could not be stored");
            Add(map, "Fail_spend_souls_failed", "扣除遗魂失败，本次改动未保存",
                "Spending relic souls failed; nothing was stored");
        }

        #endregion

        #region 性格

        private static void AddPersonalities(Dictionary<string, string> map)
        {
            Add(map, "Personality_reckless", "莽撞", "Reckless");
            Add(map, "Personality_cautious", "谨慎", "Cautious");
            Add(map, "Personality_lazy", "懒散", "Lazy");
            Add(map, "Personality_loyal", "忠诚", "Loyal");
            Add(map, "Personality_unknown", "未知性格", "Unknown temperament");

            // 效果说明：性格此前只是个词，接上实际效果之后必须让玩家知道它在做什么。
            // 文案与 PetNest/PetNestPersonality.cs 的那张表一一对应，改表要同步改这里。
            Add(map, "Personality_reckless_Desc",
                "看得更远、扑得更凶，近战更疼；代价是皮更薄。",
                "Spots trouble further out and dives in hard; hits harder in melee, but takes more.");
            Add(map, "Personality_cautious_Desc",
                "不轻易扑上去，更耐揍；代价是枪械输出略低。",
                "Picks its fights and soaks more damage; slightly weaker with guns.");
            Add(map, "Personality_lazy_Desc",
                "攻击欲最低、跑得慢；但多带一格捡漏背包。",
                "Least eager to fight and slower on its feet; carries one extra scavenger slot.");
            Add(map, "Personality_loyal_Desc",
                "贴着主人不乱跑，命也更硬。",
                "Sticks close to you and is tougher to put down.");
        }

        #endregion

        #region 属性与出身天赋（展示文案）

        /// <summary>
        /// 属性与天赋文案。
        ///
        /// 此前面板与孵化揭晓直接拼英文 statKey，中文玩家看到的是 `PetCapcity+2`
        /// （还是官方拼错的那个词）与 `GunScatterMultiplier-6%`。
        /// `PetNestTalentEntry.id` 的注释一直写着"本地化 key 后缀"，但从来没有对应的键。
        /// </summary>
        private static void AddStatsAndTalents(Dictionary<string, string> map)
        {
            // stat key -> 玩家可读属性名。key 必须与 PetNestHatchService.TalentPool、
            // PetNestDownedHandler.PickScarStatKey、PetNestPersonality 用的 statKey 完全一致。
            Add(map, "Stat_WalkSpeed", "步行速度", "Walk speed");
            Add(map, "Stat_RunSpeed", "奔跑速度", "Run speed");
            Add(map, "Stat_MaxHealth", "生命上限", "Max health");
            Add(map, "Stat_GunDamageMultiplier", "枪械伤害", "Gun damage");
            Add(map, "Stat_MeleeDamageMultiplier", "近战伤害", "Melee damage");
            Add(map, "Stat_BodyArmor", "护甲", "Body armor");
            Add(map, "Stat_GunScatterMultiplier", "枪械散布", "Gun spread");
            Add(map, "Stat_PetCapcity", "捡漏背包格", "Scavenger slots");

            // 出身天赋名。id 与 PetNestHatchService.TalentPool 的第一列一一对应。
            Add(map, "Talent_swift", "轻步", "Light Step");
            Add(map, "Talent_sprinter", "疾奔", "Sprinter");
            Add(map, "Talent_tough", "结实", "Hardy");
            Add(map, "Talent_fierce", "凶性", "Ferocious");
            Add(map, "Talent_packmule", "驮兽", "Pack Mule");
            Add(map, "Talent_brawler", "好斗", "Brawler");
            Add(map, "Talent_thickhide", "厚皮", "Thick Hide");
            Add(map, "Talent_steady", "稳手", "Steady Hands");
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
                    "把 Boss 留下的遗种孵成幼崽，带着打下一场，或者派去天灾区远征。",
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

        /// <summary>取一个本模块前缀下的本地化文本。</summary>
        private static string T(string suffix)
        {
            return LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + suffix);
        }

        /// <summary>
        /// Modifier 数值的统一格式化。百分比项在数据层存的是**小数**（0.08 = 8%，
        /// 官方 PercentageAdd 口径），直接拼 "%" 会显示成 "+0.08%"，因此展示侧一律走这里。
        /// 非百分比项（PetCapcity 一类格子数）按整数显示。
        /// </summary>
        internal static string FormatModifierValue(float value, bool percentage)
        {
            if (!percentage)
            {
                return (value >= 0f ? "+" : "") + ((int)Math.Round(value)).ToString();
            }
            float percent = value * 100f;
            return (percent >= 0f ? "+" : "") + percent.ToString("0.#") + "%";
        }

        /// <summary>
        /// 一条属性增减的完整文案，例如「生命上限 +12%」。
        /// **唯一入口**：巢页天赋、巢页战痕、孵化揭晓三处共用，避免哪一处又拼回英文 statKey。
        /// 未登记的 statKey 回落原样显示，宁可露出 key 也不吞掉这一条。
        /// </summary>
        internal static string DescribeStatDelta(string statKey, float value, bool percentage)
        {
            return DescribeStatName(statKey) + " " + FormatModifierValue(value, percentage);
        }

        /// <summary>stat key -> 玩家可读属性名。未登记时原样返回 key。</summary>
        internal static string DescribeStatName(string statKey)
        {
            if (string.IsNullOrEmpty(statKey)) return string.Empty;
            string text = T("Stat_" + statKey);
            return IsMissingKey(text, PetNestTuning.LocalizationPrefix + "Stat_" + statKey)
                ? statKey
                : text;
        }

        /// <summary>一条出身天赋的完整文案，例如「结实 · 生命上限 +12%」。</summary>
        internal static string DescribeTalent(PetNestTalentEntry entry)
        {
            if (entry == null) return string.Empty;
            string delta = DescribeStatDelta(entry.statKey, entry.value, entry.percentage);
            if (string.IsNullOrEmpty(entry.id)) return delta;

            string key = PetNestTuning.LocalizationPrefix + "Talent_" + entry.id;
            string name = LocalizationHelper.GetLocalizedText(key);
            return IsMissingKey(name, key) ? delta : name + " · " + delta;
        }

        /// <summary>性格名。空 / 未登记时回落到「未知性格」，不把裸 key 摆给玩家。</summary>
        internal static string DescribePersonality(string personalityId)
        {
            if (string.IsNullOrEmpty(personalityId)) return T("Personality_unknown");
            string key = PetNestTuning.LocalizationPrefix + "Personality_" + personalityId;
            string text = LocalizationHelper.GetLocalizedText(key);
            return IsMissingKey(text, key) ? T("Personality_unknown") : text;
        }

        /// <summary>性格的效果说明。未登记时返回空串（调用方不显示这一行）。</summary>
        internal static string DescribePersonalityEffect(string personalityId)
        {
            if (string.IsNullOrEmpty(personalityId)) return string.Empty;
            string key = PetNestTuning.LocalizationPrefix + "Personality_" + personalityId + "_Desc";
            string text = LocalizationHelper.GetLocalizedText(key);
            return IsMissingKey(text, key) ? string.Empty : text;
        }

        /// <summary>
        /// 风险档位 -> 稳定 key 后缀。**唯一实现**：面板与翻牌演出共用，
        /// 此前两边各写了一份 switch，加档位时必然漏改一处。
        /// </summary>
        internal static string GetRiskSuffix(int riskTier)
        {
            switch ((PetNestRiskTier)riskTier)
            {
                case PetNestRiskTier.Rough: return "rough";
                case PetNestRiskTier.Desperate: return "desperate";
                default: return "safe";
            }
        }

        /// <summary>风险档位的玩家可读名。</summary>
        internal static string DescribeRisk(int riskTier)
        {
            return T("Risk_" + GetRiskSuffix(riskTier));
        }

        /// <summary>目的地的玩家可读名。未登记时原样返回 id。</summary>
        internal static string DescribeDestination(string destinationId)
        {
            if (string.IsNullOrEmpty(destinationId)) return string.Empty;
            string key = PetNestTuning.LocalizationPrefix + "Dest_" + destinationId;
            string text = LocalizationHelper.GetLocalizedText(key);
            return IsMissingKey(text, key) ? destinationId : text;
        }

        /// <summary>0-1 概率 -> 整数百分比文本。面板与翻牌演出共用同一口径。</summary>
        internal static string FormatPercent(float rate)
        {
            return ((int)Math.Round(rate * 100f)) + "%";
        }

        /// <summary>
        /// 失败原因 id -> 玩家可读文案。
        ///
        /// 服务层的失败码有两种形态：纯 id（`nest_full`）与带异常名的
        /// `xxx_failed:ExceptionName`。后者的异常名是诊断信息、不进文案，
        /// 因此先按冒号截断再查键，让整族 `depart_failed:*` 落到同一条 `Fail_depart_failed`。
        /// 仍然查不到时原样返回 id（fail-open，宁可露出 id 也不吞掉失败反馈）。
        /// </summary>
        public static string DescribeFailure(string failureReasonId)
        {
            if (string.IsNullOrEmpty(failureReasonId)) return string.Empty;

            string lookupId = failureReasonId;
            int colon = lookupId.IndexOf(':');
            if (colon > 0) lookupId = lookupId.Substring(0, colon);

            string key = PetNestTuning.LocalizationPrefix + "Fail_" + lookupId;
            string text = LocalizationHelper.GetLocalizedText(key);
            return IsMissingKey(text, key) ? failureReasonId : text;
        }

        /// <summary>
        /// 官方 LocalizationManager.GetPlainText 查不到时返回的是 **"*" + key + "*"**，
        /// 不是 key 本身（源码 :132-139）。按 `text == key` 判据会恒不成立，
        /// 未登记的键会以 `*BossRush_PetNest_xxx*` 的形态直接显示给玩家。
        /// 这里按真实的星号包裹形态判定；两种形态都认，空串也算缺失。
        /// </summary>
        private static bool IsMissingKey(string text, string key)
        {
            if (string.IsNullOrEmpty(text)) return true;
            if (text == key) return true;
            return text.Length >= 2 && text[0] == '*' && text[text.Length - 1] == '*'
                && text.Substring(1, text.Length - 2) == key;
        }

        #endregion
    }
}
