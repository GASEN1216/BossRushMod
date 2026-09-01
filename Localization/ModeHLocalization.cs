using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// Mode H 本地化的**唯一** source of truth（设计提案 §23.2）。
    ///
    /// 这里刻意与 Mode G 的做法分歧：`BossRush_ModeG_` 的键直接写在
    /// `Localization/LocalizationInjector.cs` 里，而 Mode H 用独立文件，
    /// 形态与 `AwenCourierTokenConfig.InjectLocalization()`、
    /// `WildHornConfig.InjectLocalization()` 等既有静态注入方法一致，
    /// 避免继续膨胀那个已经很大的注入器。
    ///
    /// 接线点是 `Integration/BossRushIntegration_StartAndScene.cs` 的
    /// `InjectLocalization_Extra_Integration()`，由 `ModBehaviour.InjectLocalization()`
    /// 统一触发；不要把这个 partial 方法误写到 `LocalizationInjector`。
    ///
    /// 冻结要求：所有命令、异常、伤病、战痕、状态、侦察、恢复错误和押品明细
    /// 都必须有中英文 key；资源缺失时显示已注入文本，不显示 raw key。
    /// </summary>
    public static class ModeHLocalization
    {
        /// <summary>把全部 `BossRush_ModeH_` 键注入官方本地化表。</summary>
        public static void Inject()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            AddCore(map);
            AddArchetypesAndTemperaments(map);
            AddCommands(map);
            AddAnomaliesAndInjuries(map);
            AddScars(map);
            AddPlanContent(map);
            AddOddsAndStake(map);
            AddFighters(map);
            AddKits(map);
            AddStatesAndRecovery(map);
            AddRealStake(map);
            LocalizationHelper.InjectLocalizations(map);
        }

        private static void Add(Dictionary<string, string> map, string suffix, string cn, string en)
        {
            map[ModeHConfig.LocalizationKeyPrefix + suffix] = L10n.T(cn, en);
        }

        #region 核心与页面

        private static void AddCore(Dictionary<string, string> map)
        {
            Add(map, "ModeName", "百战留痕：黑市鸭王杯", "Black Market Duck Cup");
            Add(map, "ModeDesc",
                "你不是选手，是经理人。签两只斗士，看懂盘口，喊一嗓子，让它们替你打完六场。",
                "You are not a fighter but a manager. Sign two contenders, read the odds, "
                + "call one order, and let them fight all six matches for you.");
            Add(map, "Page_Entry", "入口与试棚", "Entry & Tryout");
            Add(map, "Page_Brief", "赛前看盘", "Match Brief");
            Add(map, "Page_Odds", "赔率与下注", "Odds & Stake");
            Add(map, "Page_Hud", "观战", "Spectate");
            Add(map, "Page_Settlement", "结算战报", "Match Report");
            Add(map, "Page_Transfer", "转会窗口", "Transfer Window");
            Add(map, "Page_HallOfFame", "名人堂", "Hall of Fame");
            Add(map, "Page_Diagnostics", "兼容性诊断", "Compatibility Diagnostics");
            Add(map, "Page_Recovery", "恢复", "Recovery");

            Add(map, "Button_Confirm", "确认", "Confirm");
            Add(map, "Button_Cancel", "取消", "Cancel");
            Add(map, "Button_Retry", "重试", "Retry");
            Add(map, "Match_NotWiredYet",
                "敌军已按计划就位，但本场战斗的驱动尚未接线；赛季已退回看盘，不计败场。",
                "The lineup spawned as planned, but match combat is not wired yet; "
                + "the season returned to the brief and no loss was recorded.");
            Add(map, "Summary_NoOffer", "本次转会窗口没有报价", "No offers in this transfer window");
            Add(map, "Summary_Draft", "五席试棚：先点主将，再点替补，落选者按回响去向分流。",
                "Five try-outs: pick your starter first, then the relay; "
                + "the rest are routed by echo destination.");
            Add(map, "Button_CancelAndRefund", "取消并退款", "Cancel & Refund");
            Add(map, "Button_Sign", "签约", "Sign");
            Add(map, "Button_Recon", "免费侦察一次", "Scout Once (Free)");
            Add(map, "Button_LockIn", "锁盘", "Lock In");
            Add(map, "Button_RingBell", "拍铃", "Ring the Bell");
            Add(map, "Button_Accept", "接受", "Accept");
            Add(map, "Button_Decline", "拒绝", "Decline");
            Add(map, "Button_ForceRecertify", "强制重新认证", "Force Re-certification");

            Add(map, "Label_ContractMain", "主将", "Main Contract");
            Add(map, "Label_ContractSub", "替补", "Sub Contract");
            Add(map, "Label_MatchStarter", "先发", "Starter");
            Add(map, "Label_MatchRelay", "接力", "Relay");
            Add(map, "Label_RelayEmpty", "无接力", "No Relay");
            Add(map, "Label_Rumor", "传闻", "Rumor");
            Add(map, "Label_SignatureCommand", "招牌口令", "Signature Order");
            Add(map, "Label_Match", "第 {0} 场", "Match {0}");
            Add(map, "Label_TimeRemaining", "剩余时间", "Time Left");
            Add(map, "Label_Fame", "稳定名声", "Steady Fame");
        }

        #endregion

        #region 原型与底色

        private static void AddArchetypesAndTemperaments(Dictionary<string, string> map)
        {
            Add(map, "Archetype_assault", "突进", "Assault");
            Add(map, "Archetype_ranged", "远程", "Ranged");
            Add(map, "Archetype_tank", "重装", "Tank");
            Add(map, "Archetype_sustain", "消耗", "Sustain");
            Add(map, "Archetype_finisher", "残局", "Finisher");

            Add(map, "Temperament_aggressive", "莽攻", "Aggressive");
            Add(map, "Temperament_cautious", "谨慎", "Cautious");
            Add(map, "Temperament_hunter", "猎手", "Hunter");
            Add(map, "Temperament_bulwark", "坚守", "Bulwark");
            Add(map, "Temperament_trickster", "诡术", "Trickster");
            Add(map, "Temperament_pack", "群性", "Pack");

            Add(map, "Quirk_center_keeper", "守中", "Center Keeper");
            Add(map, "Quirk_clutch", "关键时刻", "Clutch");
            Add(map, "Quirk_protect_sub", "护替补", "Protect the Sub");
            Add(map, "Quirk_reload_first", "先换弹", "Reload First");
            Add(map, "Quirk_revenge", "记仇", "Revenge");
            Add(map, "Quirk_skill_saver", "留技能", "Skill Saver");
            Add(map, "Quirk_slow_start", "热身慢", "Slow Start");
            Add(map, "Quirk_soft_target", "挑软柿子", "Soft Target");
        }

        #endregion

        #region 口令

        private static void AddCommands(Dictionary<string, string> map)
        {
            Add(map, "Command_steady", "稳住", "Steady");
            Add(map, "Command_steady_Desc", "压住慌乱，降低胆怯发作概率。",
                "Hold the nerves; lowers the chance of a cowardice check firing.");
            Add(map, "Command_press", "压上", "Press");
            Add(map, "Command_press_Desc", "缩短出招间隔，抢在对面成型前打。",
                "Shortens skill intervals to strike before the enemy sets up.");
            Add(map, "Command_center", "回到中间", "Center");
            Add(map, "Command_center_Desc", "把交战面拉回擂台中央。",
                "Pulls the engagement back to the middle of the ring.");
            Add(map, "Command_spread", "清掉旁边", "Spread");
            Add(map, "Command_spread_Desc", "先处理侧翼，避免被围。",
                "Clears the flank first to avoid getting surrounded.");
            Add(map, "Command_finish", "收割", "Finish");
            Add(map, "Command_finish_Desc", "锁定最残的敌人补刀。",
                "Locks onto the weakest enemy for the kill.");
            Add(map, "Command_hold", "留一手", "Hold");
            Add(map, "Command_hold_Desc", "把技能留给后续增援。",
                "Saves skills for the reinforcements still to come.");
            Add(map, "Command_guard", "护替补", "Guard");
            Add(map, "Command_guard_Desc", "拉开距离，给接力者留出场时间。",
                "Backs off to buy the relay fighter time.");
            Add(map, "Command_all_in", "拼了", "All In");
            Add(map, "Command_all_in_Desc", "全部压上，不留后手。",
                "Everything forward, nothing held back.");

            // 招牌口令：Commands.json 会引用 `_Desc`，缺了就在盘口页显示 raw key
            Add(map, "Command_weakness", "打弱点", "Weak Point");
            Add(map, "Command_weakness_Desc", "命中判定更狠，技能更容易生效。",
                "Hits land harder and skills connect more often.");
            Add(map, "Command_anchor", "钉住", "Anchor");
            Add(map, "Command_anchor_Desc", "咬住当前目标不放，转火更慢。",
                "Stays locked on the current target and switches more slowly.");
            Add(map, "Command_last_mag", "最后一梭", "Last Mag");
            Add(map, "Command_last_mag_Desc", "残弹期倾泻输出，几乎不再走位。",
                "Dumps everything while the mag lasts, barely repositioning.");
            Add(map, "Command_together", "一起上", "Together");
            Add(map, "Command_together_Desc", "贴着队友推进，视野与反应一起拉高。",
                "Pushes alongside teammates, raising sight and reaction together.");
            Add(map, "Command_handoff", "交给你", "Hand Off");
            Add(map, "Command_handoff_Desc", "主动让位给接力者，自己转为拉扯。",
                "Yields the front to the relay fighter and switches to pulling aggro.");

            Add(map, "CommandStatus_VerifiedBehavior", "已验证", "Verified");
            Add(map, "CommandStatus_PartiallyVerified", "部分验证", "Partially Verified");
            Add(map, "CommandStatus_ReportOnly", "仅显示", "Report Only");
            Add(map, "CommandStatus_Unavailable", "不可用", "Unavailable");
            Add(map, "Command_BellConsumed", "本场拍铃已用完", "Bell already used this match");
            Add(map, "Command_WindowActive", "口令生效中", "Order active");
        }

        #endregion

        #region 异常与伤病

        private static void AddAnomaliesAndInjuries(Dictionary<string, string> map)
        {
            Add(map, "Anomaly_blood", "见血胆怯", "Blood Shy");
            Add(map, "Anomaly_blood_Desc", "自身残血时可能整队弃赛。",
                "May forfeit the whole match when badly wounded.");
            Add(map, "Anomaly_crowd", "惧众胆怯", "Crowd Shy");
            Add(map, "Anomaly_crowd_Desc", "被三人以上围住时可能整队弃赛。",
                "May forfeit when facing three or more at once.");
            Add(map, "Anomaly_strong", "畏强胆怯", "Awe Struck");
            Add(map, "Anomaly_strong_Desc", "强敌久战不下时可能整队弃赛。",
                "May forfeit when a strong core refuses to fall.");
            Add(map, "Anomaly_error", "控制权异常", "ERROR");
            Add(map, "Anomaly_error_Desc",
                "有几率把控制权交到你手上，同时它的性格进入你的身体在看台自行行动。"
                + "接管期间的击杀按原版规则计入你的击杀统计与经验。",
                "May hand you direct control while its temperament walks your body around "
                + "the stands. Kills during the takeover count toward your own kill stats "
                + "and experience, as vanilla does.");

            Add(map, "Injury_leg", "腿伤", "Leg Injury");
            Add(map, "Injury_leg_Desc", "追不远、转身慢。", "Cannot chase far; turns slowly.");
            Add(map, "Injury_hand", "手伤", "Hand Injury");
            Add(map, "Injury_hand_Desc", "出招更慢，无法边打边走。",
                "Slower skills; cannot move while shooting.");
            Add(map, "Injury_armor", "护具受损", "Damaged Armor");
            Add(map, "Injury_armor_Desc", "开场防护更差（本场护甲槽整备不生效）。",
                "Starts with worse protection (the Armor kit slot does not apply).");
            Add(map, "Injury_old_wound", "旧伤", "Old Wound");
            Add(map, "Injury_old_wound_Desc", "残血后反应变钝。",
                "Reactions dull once badly wounded.");
            Add(map, "Injury_spirit", "心气受挫", "Broken Spirit");
            Add(map, "Injury_spirit_Desc", "被围时听不进指令。",
                "Barely follows orders when surrounded.");
            Add(map, "Injury_Retired", "赛季退役", "Retired for the Season");
            Add(map, "Injury_Rested", "完整休息", "Fully Rested");
        }

        #endregion

        #region 战痕

        private static void AddScars(Dictionary<string, string> map)
        {
            Add(map, "Scar_broken_shield_charge", "破盾突进", "Broken Shield Charge");
            Add(map, "Scar_broken_shield_charge_Desc",
                "护具首次破裂后短时间视野与转身变快，但同窗口无法边退边打。",
                "After armor first breaks: sharper sight and turning, but no shooting on the move.");
            Add(map, "Scar_blood_rush", "闻血而动", "Blood Rush");
            Add(map, "Scar_blood_rush_Desc",
                "敌人首次残血后死盯该目标，但同窗口“回到中间”效果减半。",
                "Locks onto the first wounded enemy, but halves the Center order in that window.");
            Add(map, "Scar_longshot_memory", "远射记忆", "Longshot Memory");
            Add(map, "Scar_longshot_memory_Desc",
                "首次挨远程后收缩交战面，但同窗口转身变慢。",
                "Tightens the engagement after the first ranged hit, but turns slower.");
            Add(map, "Scar_relay_expert", "接力老手", "Relay Expert");
            Add(map, "Scar_relay_expert_Desc",
                "作为接力者登场后短时间更强，但作为先发时开场偏软。",
                "Stronger right after relaying in, softer in the opening as a starter.");
            Add(map, "Scar_bell_dependence", "依赖拍铃", "Bell Dependence");
            Add(map, "Scar_bell_dependence_Desc",
                "拍铃后口令效果更强，但拍铃前技能成功率更低。",
                "Orders hit harder after the bell, weaker skills before it.");
            Add(map, "Scar_center_keeper", "守中习惯", "Center Keeper");
            Add(map, "Scar_center_keeper_Desc",
                "危险边缘擂台上“回到中间”更强，但开阔场上视野变差。",
                "Center works better on danger-edge rings, worse sight on open fields.");
            Add(map, "Scar_skill_saver_scar", "留手", "Skill Saver");
            Add(map, "Scar_skill_saver_scar_Desc",
                "增援未到时压着技能不放，错过速杀窗口也在所不惜。",
                "Holds skills back until reinforcements arrive, even at the cost of fast kills.");
            Add(map, "Scar_crowd_favorite", "人越多越来劲", "Crowd Favorite");
            Add(map, "Scar_crowd_favorite_Desc",
                "三人围攻时视野与反应更好，但单挑核心时技能成功率更低。",
                "Better sight and reactions against three, weaker skills in a duel.");

            Add(map, "Scar_OfferTitle", "战痕候选", "Scar Offer");
            Add(map, "Scar_AcceptHint", "接受这条战痕（利弊绑定，不可拆）",
                "Take this scar (upside and downside are bound together)");
            Add(map, "Scar_DeclineHint", "拒绝，换取稳定名声 +1",
                "Decline for +1 Steady Fame");
            Add(map, "Scar_ReplaceHint", "已有三条，必须替换一条", "Three already; replace one");
        }

        #endregion

        #region 计划与侦察

        private static void AddPlanContent(Dictionary<string, string> map)
        {
            Add(map, "Skeleton_single_beast", "独兽", "Lone Beast");
            Add(map, "Skeleton_duo", "双煞", "Duo");
            Add(map, "Skeleton_core_and_escort", "头领与护卫", "Core & Escort");
            Add(map, "Skeleton_pack", "猎群", "Pack");
            Add(map, "Skeleton_relay_squad", "接力队", "Relay Squad");
            Add(map, "Skeleton_mixed_range", "远近交替", "Mixed Range");
            Add(map, "Skeleton_wounded_line", "残阵", "Wounded Line");
            Add(map, "Skeleton_echo_core_and_escort", "回场核心与护卫", "Echo Core & Escort");
            Add(map, "Skeleton_champion_beast", "冠军独兽", "Champion Beast");
            Add(map, "Skeleton_late_surge", "后程增援", "Late Surge");

            Add(map, "Entry_scout_first", "斥候先行", "Scout First");
            Add(map, "EntryHint_scout_first", "先来一个探路的。", "One comes ahead to probe.");
            Add(map, "Entry_front_loaded", "开场压上", "Front Loaded");
            Add(map, "EntryHint_front_loaded", "一上来就是主力。", "The bulk arrives immediately.");
            Add(map, "Entry_late_reinforcement", "后程增援", "Late Reinforcement");
            Add(map, "EntryHint_late_reinforcement", "后面还有人。", "More are still coming.");
            Add(map, "Entry_alternating_range", "远近交替", "Alternating Range");
            Add(map, "EntryHint_alternating_range", "一个一个来，远近轮换。",
                "They come one at a time, alternating range.");
            Add(map, "Entry_core_last", "核心压轴", "Core Last");
            Add(map, "EntryHint_core_last", "最能打的最后出场。", "The strongest enters last.");
            Add(map, "Entry_unknown_seat", "未知席位", "Unknown Seat");
            Add(map, "EntryHint_unknown_seat", "有一个席位没有公开。", "One seat is not disclosed.");

            Add(map, "Condition_center_cover", "中央掩体", "Center Cover");
            Add(map, "Condition_danger_edge", "危险边缘", "Danger Edge");
            Add(map, "Condition_medical_limited", "医疗受限", "Limited Medical");
            Add(map, "Condition_narrow_cage", "窄笼", "Narrow Cage");
            Add(map, "Condition_open_field", "开阔场", "Open Field");
            Add(map, "Condition_residual_might", "余威", "Residual Might");

            Add(map, "Recon_hidden_quirk", "隐藏坏习惯", "Hidden Quirk");
            Add(map, "Recon_current_injury", "当前伤病", "Current Injuries");
            Add(map, "Recon_member_order", "成员与顺序线索", "Members & Order");
            Add(map, "Recon_second_equipment", "第二装备", "Secondary Gear");
            Add(map, "Recon_Consumed", "本场侦察已用", "Scouting already used this match");

            Add(map, "Summary_EnemyCount", "人数区间", "Enemy Count");
            Add(map, "Summary_PrimaryArchetype", "主要身份", "Primary Role");
            Add(map, "Summary_EntryScript", "进场节奏", "Entry Pace");
            Add(map, "Summary_Condition", "擂台条件", "Ring Condition");
            Add(map, "Summary_HighThreatCore", "已知高威胁核心", "Known High-threat Core");
        }

        #endregion

        #region 赔率与筹码

        private static void AddOddsAndStake(Dictionary<string, string> map)
        {
            Add(map, "OddsTone_x1", "稳赢盘", "Heavy Favorite");
            Add(map, "OddsTone_x2", "小优盘", "Slight Favorite");
            Add(map, "OddsTone_x3", "五五盘", "Even Money");
            Add(map, "OddsTone_x4", "劣势盘", "Underdog");
            Add(map, "OddsTone_x5", "冷门盘", "Long Shot");

            Add(map, "Odds_PublicEdge", "公开分差", "Public Edge");
            Add(map, "Odds_PlayerScore", "我方公开分", "Your Public Score");
            Add(map, "Odds_EnemyScore", "敌方公开分", "Enemy Public Score");
            Add(map, "Odds_RelayAvailable", "有可用接力", "Relay Available");
            Add(map, "Odds_RelayEmpty", "无接力", "No Relay");
            Add(map, "Odds_StarterMatchup", "先发克制关系", "Starter Matchup");
            Add(map, "Odds_RelayMatchup", "接力克制关系", "Relay Matchup");
            Add(map, "Odds_Equipment", "虚拟整备", "Virtual Loadout");
            Add(map, "Odds_Injury", "伤病", "Injuries");
            Add(map, "Odds_Anomaly", "公开异常", "Disclosed Anomalies");
            Add(map, "Odds_Scar", "战痕", "Scars");
            Add(map, "Odds_Command", "口令", "Order");
            Add(map, "Odds_Arena", "擂台条件", "Ring Condition");
            Add(map, "Odds_EnemyStage", "场次", "Stage");
            Add(map, "Odds_EnemyCount", "公开人数上限", "Disclosed Count");
            Add(map, "Odds_EnemyCore", "高威胁核心", "High-threat Core");
            Add(map, "Odds_EnemySynergy", "协同", "Synergy");
            Add(map, "Odds_EnemyStatus", "公开状态", "Disclosed Status");

            Add(map, "Stake_Credits", "虚拟筹码", "Virtual Credits");
            Add(map, "Stake_Amount", "本场下注", "Stake This Match");
            Add(map, "Stake_Preview", "胜负预览", "Outcome Preview");
            Add(map, "Stake_WinBalance", "胜利后余额", "Balance on Win");
            Add(map, "Stake_LoseBalance", "失败后余额", "Balance on Loss");
            Add(map, "Stake_RewardCandidates", "本场奖励候选数", "Reward Candidates");
            Add(map, "Stake_ZeroAlwaysLegal", "0 点下注始终合法", "Staking 0 is always allowed");
        }

        #endregion

        #region 选手与套装

        private static void AddFighters(Dictionary<string, string> map)
        {
            AddFighter(map, "shotgun_brawler", "喷子", "Buckshot",
                "近身一发定生死，据说从没在十步之外赢过。",
                "Settles things in one blast up close; never won anything past ten paces.");
            AddFighter(map, "frost_marshal", "急冻团长", "Frost Marshal",
                "见血就抖，但抖着抖着就冲上来了。",
                "Trembles at the sight of blood, then charges anyway.");
            AddFighter(map, "snow_sharpshooter", "弗里兹", "Freeze",
                "换弹永远比开枪认真。",
                "Takes reloading far more seriously than shooting.");
            AddFighter(map, "long_lens", "观测者", "Observer",
                "技能攒到最后一刻，经常攒到比赛结束。",
                "Saves its skill for the perfect moment, often past the final bell.");
            AddFighter(map, "triple_tap", "三枪哥", "Three-Shot",
                "三发之内解决问题，超过三发就开始自作主张。",
                "Solves it in three rounds, or starts improvising.");
            AddFighter(map, "great_xing", "大兴兴", "Big Xing",
                "站在中间就不肯挪窝，像是把擂台当自家客厅。",
                "Plants itself in the middle as if the ring were its living room.");
            AddFighter(map, "mech_snowman", "机械雪人", "Mech Snowman",
                "护着替补比护着自己积极。",
                "Protects the sub more eagerly than itself.");
            AddFighter(map, "big_ice", "大冰冰", "Big Ice",
                "遇强则怂，但怂之前会先砸塌半个场子。",
                "Backs down from the strong, right after flattening half the ring.");
            AddFighter(map, "bomb_maniac", "炸弹狂人", "Bomb Maniac",
                "开场三十秒像在散步，之后整个擂台都是它的。",
                "Strolls for the first thirty seconds, then owns the whole ring.");
            AddFighter(map, "goose_leader", "呆头鹅", "Goose Leader",
                "人一多就慌，可它偏偏总带着一群人。",
                "Panics in a crowd, yet always brings one.");
            AddFighter(map, "orion_hunter", "猎户", "Orion",
                "谁打过它，它就记谁一辈子。",
                "Never forgets whoever hit it first.");
            AddFighter(map, "warden", "典狱长", "Warden",
                "越到最后越冷静，收尾的活它最熟。",
                "Colder as the clock runs down; closing is what it does.");
        }

        private static void AddFighter(
            Dictionary<string, string> map, string id, string cn, string en,
            string rumorCn, string rumorEn)
        {
            Add(map, "Fighter_" + id, cn, en);
            Add(map, "Rumor_" + id, rumorCn, rumorEn);
        }

        private static void AddKits(Dictionary<string, string> map)
        {
            AddKit(map, "starter_field_armor", "战地防弹衣", "Field Armor",
                "标准三级防护，够挡两下。", "Standard tier-3 plate; good for a couple of hits.");
            AddKit(map, "starter_field_helmet", "战地头盔", "Field Helmet",
                "特警制式，保住脑袋。", "Police-issue; keeps the head attached.");
            AddKit(map, "starter_assault_rifle", "突击步枪", "Assault Rifle",
                "配一百二十发普通弹。", "Comes with 120 standard rounds.");
            AddKit(map, "starter_assault_blade", "近身钝器", "Close-quarters Club",
                "近身补刀用。", "For finishing at arm's length.");
            AddKit(map, "starter_marksman_rifle", "精确射手步枪", "Marksman Rifle",
                "配一梭狙击弹。", "Comes with a stack of sniper rounds.");
            AddKit(map, "starter_sidearm", "副武器手枪", "Sidearm",
                "主武器哑火时的保险。", "Insurance when the primary jams.");
            AddKit(map, "starter_heavy_plate", "重型防弹衣", "Heavy Plate",
                "五级防护，慢但硬。", "Tier-5 plate; slow but solid.");
            AddKit(map, "starter_scout_helmet", "侦察头盔", "Scout Helmet",
                "四级防护，视野更好。", "Tier-4 with a better field of view.");
            AddKit(map, "reward_breacher_gun", "破门枪", "Breacher",
                "高射速，弹药管够。", "High rate of fire, plenty of ammo.");
            AddKit(map, "reward_long_barrel", "长弓", "Long Bow",
                "远距离压制，箭矢有限。", "Long-range pressure with limited arrows.");
            AddKit(map, "reward_riot_plate", "防暴甲", "Riot Plate",
                "六级防护。", "Tier-6 protection.");
            AddKit(map, "reward_command_helmet", "指挥头盔", "Command Helmet",
                "六级防护，视野最好。", "Tier-6 with the best field of view.");
            AddKit(map, "reward_executioner_blade", "行刑刃", "Executioner Blade",
                "近身收割。", "For close-range harvesting.");
            AddKit(map, "reward_hold_out_pistol", "大口径手枪", "Hold-out Magnum",
                "一发换一条命。", "One round, one life.");
            AddKit(map, "reward_bulwark_plate", "壁垒甲", "Bulwark Plate",
                "七级防护，全场最硬。", "Tier-7; the hardest thing in the ring.");
            AddKit(map, "reward_skirmisher_blade", "游击刃", "Skirmisher Blade",
                "轻快的近身武器。", "A light, quick close-range blade.");
        }

        private static void AddKit(
            Dictionary<string, string> map, string id, string cn, string en,
            string descCn, string descEn)
        {
            Add(map, "Kit_" + id, cn, en);
            Add(map, "Kit_" + id + "_Desc", descCn, descEn);
        }

        #endregion

        #region 状态与恢复

        private static void AddStatesAndRecovery(Dictionary<string, string> map)
        {
            Add(map, "State_Drafting", "五席试棚", "Tryout");
            Add(map, "State_RosterLocked", "阵容已定", "Roster Locked");
            Add(map, "State_MatchBrief", "赛前看盘", "Match Brief");
            Add(map, "State_LoadoutEditing", "整备中", "Editing Loadout");
            Add(map, "State_OddsPreview", "看赔率", "Odds Preview");
            Add(map, "State_LoadoutLocked", "已锁盘", "Locked In");
            Add(map, "State_MatchSpawning", "入场中", "Entering");
            Add(map, "State_MatchFighting", "比赛进行中", "Fighting");
            Add(map, "State_RelayPending", "等待接力", "Relay Pending");
            Add(map, "State_MatchSettling", "结算中", "Settling");
            Add(map, "State_Intermission", "幕间", "Intermission");
            Add(map, "State_TransferWindow", "转会窗口", "Transfer Window");
            Add(map, "State_HallOfFame", "名人堂", "Hall of Fame");
            Add(map, "State_SeasonEnded", "赛季结束", "Season Over");
            Add(map, "State_Recovering", "恢复中", "Recovering");
            Add(map, "State_Suspended", "已挂起", "Suspended");

            Add(map, "Outcome_Victory", "胜利", "Victory");
            Add(map, "Outcome_Defeat", "失败", "Defeat");
            Add(map, "Outcome_Timeout", "时间到，判负", "Time out — loss");
            Add(map, "Outcome_Cowardice", "整队弃赛", "Team forfeited");

            Add(map, "Recovery_TechnicalAbort", "技术中止，本场按同一看盘重开",
                "Technical abort; the match restarts from the same brief");
            Add(map, "Recovery_SameMatchRestart", "同场重开，不判负",
                "Restarting the same match; this is not a loss");
            Add(map, "Recovery_ManualIntervention", "需要人工介入，当前只读",
                "Manual intervention required; read-only for now");
            Add(map, "Recovery_Suspended", "已挂起，环境恢复后可从同一场继续",
                "Suspended; you can resume the same match once the environment recovers");
            Add(map, "Recovery_SnapshotUnusable", "战场快照不可用，已回落到同场重开",
                "Battle snapshot unusable; fell back to restarting the same match");
            Add(map, "Recovery_RetryScan", "重试风险扫描", "Retry risk scan");

            // 旧模式入口被拒时的两句文案：扫描失败与真实风险是两回事，
            // 用同一句会把「读档出错」说成「你有笔押品没结算」。
            Add(map, "LegacyBlocked_Scan",
                "黑市鸭王杯的资产风险扫描未能完成（读档异常），正在重试；稍后再试其他模式。",
                "Mode H's asset risk scan could not complete (save read error); retrying. "
                + "Try other modes again shortly.");
            Add(map, "LegacyBlocked_ActiveJournal",
                "黑市鸭王杯仍有未结算的真实资产事务，暂时无法开始其他模式。",
                "Mode H has unsettled real-asset transactions; other modes are blocked.");

            // 恢复面板会按 ModeHLifecycle / ModeHStakePhase 的枚举名拼 key，
            // 因此**所有**枚举值都要有对应条目，缺一个就会在面板上显示 raw key。
            Add(map, "State_Unknown", "未知状态", "Unknown state");
            Add(map, "State_None", "无进行中的赛季", "No active season");
            Add(map, "State_EntryIntent", "已冻结入场意图", "Entry intent frozen");
            Add(map, "State_SceneLoading", "等待场景就绪", "Waiting for the arena");
            Add(map, "State_ProductionCertifying", "正在做生产认证", "Running production certification");
            Add(map, "State_ErrorRecoveryPending", "等待恢复屏障", "Awaiting recovery barrier");
            Add(map, "State_StakePrepared", "押品已锁盘", "Stake locked in escrow");
            Add(map, "StakePhase_Unknown", "押品阶段未知", "Stake phase unknown");
            Add(map, "StakePhase_None", "无押品事务", "No stake transaction");

            Add(map, "EntryInteract", "黑市鸭王杯", "Black Market Duck Cup");

            // 不可用原因：key 由 ModeHAvailability 用 "Unavailable_" + reasonId 拼出，
            // 因此这里的后缀必须与 ModeHAvailability 的 Reason* 常量字面量一一对应。
            Add(map, "Unavailable_Generic", "当前无法进入本模式", "The mode cannot be entered now");
            Add(map, "Unavailable_modeh_disabled", "本模式未启用", "The mode is disabled");
            Add(map, "Unavailable_modeh_risk_scan_pending", "押品风险扫描未完成",
                "Stake risk scan has not finished");
            Add(map, "Unavailable_modeh_external_asset_risk", "存在未终结的押品事务",
                "An unfinished stake transaction is present");
            Add(map, "Unavailable_modeh_recovery_only", "有赛季恢复壳待处理",
                "A season recovery shell is still pending");
            Add(map, "Unavailable_modeh_content_not_ready", "内容未就绪", "Content not ready");
            Add(map, "Unavailable_modeh_run_owner_active", "本模式已在进行中",
                "The mode is already running");
            Add(map, "Unavailable_modeh_other_mode_active", "有其它模式正在进行",
                "Another mode is already running");
            Add(map, "Unavailable_modeh_map_unsupported", "当前地图不支持本模式",
                "This map does not support the mode");
            Add(map, "Unavailable_modeh_presentation_missing", "展示资源缺失",
                "Presentation assets missing");
            Add(map, "Unavailable_modeh_certification_failed", "生产认证未通过",
                "Production certification failed");
            Add(map, "Unavailable_modeh_owner_missing", "运行实例缺失", "Runtime owner missing");
            Add(map, "Unavailable_TicketRefunded", "已退还船票", "Ticket refunded");

            // 开局中止原因：AbortSetup 的 reasonId 是内部标识（不带 modeh_ 前缀），
            // 由 ModeHRuntimeModule.ResolveAbortMessageKey 归类到下面这几条，
            // 保证玩家被传回基地时永远看得到一句解释而不是静默（CR-2026-08-29-013）。
            Add(map, "Abort_Generic", "开局失败，已退回基地", "Setup failed; returned to base");
            Add(map, "Abort_MapUnsupported", "这张地图缺少本模式的点位",
                "This map lacks the mode's spawn points");
            Add(map, "Abort_Lease", "无法接管擂台场地，已退回基地",
                "Could not take over the arena; returned to base");
            Add(map, "Abort_Certification", "开赛前的生产认证未通过，已退回基地",
                "Pre-match production certification failed; returned to base");
            Add(map, "Abort_Cancelled", "已取消入场", "Entry cancelled");
            Add(map, "Abort_Save", "赛季存档写入失败，已退回基地",
                "Season save failed; returned to base");
            Add(map, "Abort_Content", "选手或敌军内容不可用，已退回基地",
                "Fighter or enemy content unavailable; returned to base");

            Add(map, "Diag_Passed", "通过", "Passed");
            Add(map, "Diag_Rejected", "拒绝", "Rejected");
            Add(map, "Diag_Progress", "认证进度", "Certification Progress");
            Add(map, "Diag_Signatures", "构建签名", "Build Signatures");
            Add(map, "Diag_ReadOnlyNotice", "诊断结果只读，不提供绕过或手工改写",
                "Diagnostics are read-only; no bypass or manual override is offered");
        }

        #endregion

        #region 真实押品

        private static void AddRealStake(Dictionary<string, string> map)
        {
            // §22.1 冻结：入口页、模式说明与 ModeHInteractable 三处都必须显示这一行。
            Add(map, "RealStakeRiskNotice",
                "本模式允许你押上真实仓库物品。失败会永久没收，唯一装备也不豁免。",
                "This mode lets you stake real warehouse items. A loss confiscates them "
                + "permanently, and your only copy of a piece of gear is not exempt.");

            Add(map, "RealStake_Selector", "真实押品", "Real Stake");
            Add(map, "RealStake_NotSelected", "默认不押", "Not staked by default");
            Add(map, "RealStake_WorstCaseLoss", "最坏损失件数", "Worst-case losses");
            Add(map, "RealStake_QualityRange", "可清算品质范围", "Settleable quality range");
            Add(map, "RealStake_PlannedLosses", "预冻结损失清单", "Frozen loss list");
            Add(map, "RealStake_Escrowed", "临时托管中", "Held in escrow");
            Add(map, "RealStake_UniqueNotExempt", "唯一装备不豁免", "Your only copy is not exempt");
            Add(map, "RealStake_Disabled", "当前存档槽无法证明资产安全，押品已禁用",
                "This save slot cannot prove asset safety; staking is disabled");
            // 三条分因文案。旧的 RealStake_Disabled 只说「无法证明资产安全」，
            // 玩家会误以为存档坏了；实际最常见的原因是上一笔押品还没结算完。
            Add(map, "RealStake_Disabled_PendingTx",
                "上一笔押品事务尚未结算，先去恢复面板处理完再押",
                "A previous stake transaction is unsettled; resolve it in Recovery first");
            Add(map, "RealStake_Disabled_ManualIntervention",
                "押品事务需要人工介入，当前只读",
                "The stake transaction needs manual intervention; read-only for now");
            Add(map, "RealStake_Disabled_StorageUnavailable",
                "仓库尚未就绪，押品暂不可用（进基地后再试）",
                "Storage is not ready yet; staking is unavailable for now");
            Add(map, "RealStake_WorstCasePreview", "最坏损失", "Worst case");
            Add(map, "RealStake_RewardPreview", "胜利可得同品质", "On win, same quality");
            Add(map, "RealStake_SelectedCount", "已押件数", "Staked items");

            Add(map, "StakePhase_Prepared", "已冻结计划", "Plan frozen");
            Add(map, "StakePhase_EscrowSnapshotDurable", "托管快照已落盘", "Escrow snapshot durable");
            Add(map, "StakePhase_EscrowRemovedDurable", "托管已脱离仓库", "Escrow removed from storage");
            Add(map, "StakePhase_MatchLocked", "比赛已锁定", "Match locked");
            Add(map, "StakePhase_ResultCommitted", "结果已提交", "Result committed");
            Add(map, "StakePhase_SettlementPending", "结算未完成", "Settlement pending");
            Add(map, "StakePhase_Terminal", "已结算", "Settled");
            Add(map, "StakePhase_CancelledTerminal", "已取消（未动仓库）",
                "Cancelled (storage untouched)");
            Add(map, "StakePhase_AbortReturnCommitted", "退还已提交", "Refund committed");
            Add(map, "StakePhase_RefundedTerminal", "已完整退还", "Fully refunded");
            Add(map, "StakePhase_ManualIntervention", "需要人工介入", "Manual intervention");
        }

        #endregion
    }
}
