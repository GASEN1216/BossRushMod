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
                "你不是选手，是经理人。挑一只斗士上擂台，另一只自动当接力；你在看台上看，关键时刻拍一次铃，让它们替你打完六场。",
                "You are not a fighter but a manager. Pick one contender for the ring and another joins as the relay; "
                + "watch from the stands, ring the bell once when it counts, and let them fight all six matches for you.");
            // 入口页就是唯一的选人页（2026-09-23 owner 实测第 6 条：「只弄一个选择武将的页面，选完后就开始」）。
            Add(map, "Page_Entry", "挑一位选手出战", "Pick your fighter");
            Add(map, "Page_Brief", "赛前看盘", "Match Brief");
            Add(map, "Page_Odds", "赔率与下注", "Odds & Stake");
            Add(map, "Page_Hud", "观战", "Spectate");
            Add(map, "Page_Settlement", "结算战报", "Match Report");
            Add(map, "Page_Transfer", "转会窗口", "Transfer Window");
            Add(map, "Page_HallOfFame", "名人堂", "Hall of Fame");
            // 逐项认证只在 F3 开发测试入口展示。
            Add(map, "Page_Diagnostics", "鸭王杯逐项认证", "Duck Cup Certification");
            Add(map, "Page_Recovery", "恢复", "Recovery");

            Add(map, "Button_Confirm", "确认", "Confirm");
            Add(map, "Button_Cancel", "取消", "Cancel");
            Add(map, "Button_Retry", "重试", "Retry");
            Add(map, "Match_NotWiredYet",
                "敌军已按计划就位，但本场战斗的驱动尚未接线；赛季已退回看盘，不计败场。",
                "The lineup spawned as planned, but match combat is not wired yet; "
                + "the season returned to the brief and no loss was recorded.");
            Add(map, "Summary_NoOffer", "本次转会窗口没有报价", "No offers in this transfer window");
            Add(map, "Summary_Draft",
                "挑一位替你上擂台。另一位会自动当接力：先上场的倒下了，它顶上。选好马上开打，你在看台上看，关键时刻可以拍一次铃。",
                "Pick one to fight for you. Another joins as the relay and steps in if your fighter goes down. "
                + "The match starts right away; you watch from the stands and may ring the bell once when it counts.");
            Add(map, "Button_CancelAndRefund", "停止测试，返回选人", "Stop Test and Return to Selection");
            Add(map, "Button_Sign", "选他出战", "Send this one in");
            Add(map, "Button_StartMatch", "开打", "Start the match");
            Add(map, "Button_CustomSetup", "自己调整再开打", "Adjust first");
            Add(map, "Button_NextMatch", "下一场", "Next match");
            Add(map, "Button_Continue", "继续", "Continue");
            // 选人页撞上这句时会挂出「退出本赛季」按钮（2026-09-23 复核 V6-1：旧版叫玩家退出，页面上却一个按钮都没有）
            Add(map, "Draft_NoViablePair",
                "这位选手凑不齐六场赛程的搭档。换一位试试；都不行就点下方「退出本赛季」，下次进场会重抽候选。",
                "This fighter can't be paired for all six matches. Try another one; if none work, press \"Leave season\" below and a fresh lineup is drawn next time.");
            Add(map, "Transfer_Summary",
                "有位选手想加入。签下他会换掉你现在的接力替补；不想换就直接下一场。",
                "A fighter wants to join. Signing them replaces your current relay; otherwise just go to the next match.");
            Add(map, "Transfer_Accept", "签下他（换掉接力）", "Sign (replaces relay)");
            Add(map, "Transfer_Keep", "不换人，下一场", "Keep roster, next match");
            Add(map, "Settle_AutoKit", "新装备到手：{0}（之后自动配上）", "New gear: {0} (equipped automatically from now on)");
            Add(map, "Settle_AutoScarTaken", "{0} 留下了战痕：{1}。{2}", "{0} earned a scar: {1}. {2}");
            Add(map, "Settle_AutoScarDeclined", "{0} 这次没有留下新战痕（战痕已满或重复），换成了名声 +1",
                "{0} took no new scar this time (full or duplicate) and gained +1 fame instead");
            Add(map, "Button_Recon", "免费侦察一次", "Scout Once (Free)");
            Add(map, "Button_LockIn", "锁定并开打", "Lock in and start");
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
            // 选人卡副标题：用大白话说它是哪一类打法（原型名「消耗」「残局」玩家看不懂）。
            Add(map, "Archetype_assault_Plain", "近身猛冲", "Brawler");
            Add(map, "Archetype_ranged_Plain", "远程射手", "Shooter");
            Add(map, "Archetype_tank_Plain", "重甲肉盾", "Tank");
            Add(map, "Archetype_sustain_Plain", "打持久战", "Grinder");
            Add(map, "Archetype_finisher_Plain", "专收残血", "Closer");

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
            Add(map, "Command_press_Desc", "扩大索敌范围、加快转身并允许移动射击，持续 6 秒。",
                "Extends detection range, speeds up turning and enables mobile fire for 6 seconds.");
            Add(map, "Command_center", "回到中间", "Center");
            Add(map, "Command_center_Desc", "缩短索敌距离，并尝试回到擂台中央，持续 6 秒。",
                "Shortens detection range and attempts to move to ring center for 6 seconds.");
            Add(map, "Command_spread", "清掉旁边", "Spread");
            Add(map, "Command_spread_Desc", "至少两名敌军在场时，扩大索敌视角、加快转身并允许移动射击。",
                "Requires at least two enemies; widens detection angle, speeds turning and enables mobile fire.");
            Add(map, "Command_finish", "收割", "Finish");
            Add(map, "Command_finish_Desc", "锁定最残的敌人补刀。",
                "Locks onto the weakest enemy for the kill.");
            Add(map, "Command_hold", "留一手", "Hold");
            Add(map, "Command_hold_Desc", "把技能留给后续增援。",
                "Saves skills for the reinforcements still to come.");
            Add(map, "Command_guard", "护替补", "Guard");
            Add(map, "Command_guard_Desc", "停止移动射击，缩短反应耗时并加快转身，持续 6 秒。",
                "Stops mobile fire, reduces reaction delay and speeds turning for 6 seconds.");
            Add(map, "Command_all_in", "拼了", "All In");
            Add(map, "Command_all_in_Desc", "全部压上，不留后手。",
                "Everything forward, nothing held back.");

            // 招牌口令：Commands.json 会引用 `_Desc`，缺了就在盘口页显示 raw key
            Add(map, "Command_weakness", "打弱点", "Weak Point");
            Add(map, "Command_weakness_Desc", "优先索敌残血敌军，并扩大索敌距离，持续 6 秒。",
                "Prioritizes wounded enemies and extends detection range for 6 seconds.");
            Add(map, "Command_anchor", "钉住", "Anchor");
            Add(map, "Command_anchor_Desc", "停止移动射击并加快战斗转身，持续 6 秒。",
                "Stops mobile fire and speeds combat turning for 6 seconds.");
            Add(map, "Command_last_mag", "最后一梭", "Last Mag");
            Add(map, "Command_last_mag_Desc", "停止移动射击，提高物品技能施放概率，持续 6 秒。",
                "Stops mobile fire and raises item-skill chance for 6 seconds.");
            Add(map, "Command_together", "一起上", "Together");
            Add(map, "Command_together_Desc", "提高技能与物品技能的施放概率，持续 6 秒。",
                "Raises skill and item-skill chances for 6 seconds.");
            Add(map, "Command_handoff", "交给你", "Hand Off");
            Add(map, "Command_handoff_Desc", "仅持有此招牌的接力者实际登场后可用：提高技能施放概率与转身速度，持续 6 秒。",
                "Only usable after its signature owner relays in: raises skill chance and turn speed for 6 seconds.");

            // 拍铃卡上的一句白话：这条口令让选手干什么（观战 HUD 用，2026-09-23）。
            AddCommandPlain(map, "steady", "让它稳住，别临阵退缩", "Keep it from losing its nerve");
            AddCommandPlain(map, "press", "让它主动压上去打", "Make it push forward");
            AddCommandPlain(map, "center", "让它回到擂台中间", "Send it back to the middle");
            AddCommandPlain(map, "spread", "先清掉身边的敌人", "Clear out nearby enemies");
            AddCommandPlain(map, "finish", "专打最残的那个敌人", "Finish off the weakest enemy");
            AddCommandPlain(map, "hold", "把技能留给后面的增援", "Save skills for the reinforcements");
            AddCommandPlain(map, "guard", "站稳反击，出手更快", "Stand firm and react faster");
            AddCommandPlain(map, "all_in", "不留后手，全力猛攻", "Go all out");
            AddCommandPlain(map, "weakness", "集中打残血的敌人，看得更远", "Focus wounded enemies and look farther");
            AddCommandPlain(map, "anchor", "站定别乱跑，转身更快", "Hold position and turn faster");
            AddCommandPlain(map, "last_mag", "站定开火，多用道具技能", "Stand and fire; use more item skills");
            AddCommandPlain(map, "together", "更频繁地放技能", "Use skills more often");
            AddCommandPlain(map, "handoff", "接力上场后：技能更勤、转身更快", "After the relay enters: more skills, faster turns");

            Add(map, "CommandStatus_VerifiedBehavior", "已验证", "Verified");
            Add(map, "CommandStatus_PartiallyVerified", "部分验证", "Partially Verified");
            Add(map, "CommandStatus_ReportOnly", "仅显示", "Report Only");
            Add(map, "CommandStatus_Unavailable", "不可用", "Unavailable");
            Add(map, "Command_BellConsumed", "本场拍铃已用完", "Bell already used this match");
            Add(map, "Command_WindowActive", "正在照做", "Following your call");
            Add(map, "Hud_BellOncePerMatch", "每场一次", "Once per match");
            Add(map, "Hud_BellUsedHint", "下一场还能再拍一次", "You can ring again next match");
            Add(map, "Hud_EnemiesLeft", "场上敌人", "Enemies left");
        }

        private static void AddCommandPlain(Dictionary<string, string> map, string id, string cn, string en)
        {
            Add(map, "Command_" + id + "_Plain", cn, en);
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
                "每场有一次机会交换控制权：你操作选手，原身体在看台表演；结束后还原。此模式击杀不计入鸭皇图鉴。",
                "Once per match, may swap control: you play the fighter while your body performs in the stands. Control is restored afterward. Mode H kills do not count toward the Duck Codex.");

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
            Add(map, "Condition_center_cover_Desc", "蓝圈内获得中央掩护：物理伤害系数降低 25%，离圈解除。双方同等适用。", "Inside the blue ring: physical damage factor reduced by 25%; removed on exit. Applies to both sides.");
            Add(map, "Condition_danger_edge_Desc", "橙圈外每秒受到最大生命 2% 的穿甲伤害。每名参赛者入场后有 5 秒宽限；回到中间可避开。", "Outside the orange ring: 2% max-health armor-bypassing damage per second. Each entrant has 5 seconds of grace; return to center to avoid it.");
            Add(map, "Condition_medical_limited_Desc", "双方所有实际治疗量减半，直接伤害不变。", "All actual healing is halved for both sides; direct damage is unchanged.");
            Add(map, "Condition_narrow_cage_Desc", "近战规则：双方近战伤害系数 +20%，枪械伤害系数 -20%。", "Close-combat rules: melee damage factor +20%, gun damage factor -20% for both sides.");
            Add(map, "Condition_open_field_Desc", "远射规则：双方枪械伤害系数 +15%。", "Ranged-combat rules: gun damage factor +15% for both sides.");
            Add(map, "Condition_residual_might_Desc", "每名参赛者实际入场后的前 8 秒，枪械与近战伤害系数 +20%；首发、接力和增援均适用。", "For 8 seconds after each actual entry, gun and melee damage factors +20%; applies to starters, relays and reinforcements.");

            Add(map, "Recon_hidden_quirk", "隐藏坏习惯", "Hidden Quirk");
            Add(map, "Recon_current_injury", "当前伤病", "Current Injuries");
            Add(map, "Recon_member_order", "成员与顺序线索", "Members & Order");
            Add(map, "Recon_second_equipment", "核心作战特点", "Core combat traits");
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
            Add(map, "OddsTone_x1", "优势盘", "Heavy Favorite");
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
            Add(map, "Odds_EnemyOfficialAttributes", "敌方装备实力", "Opponent equipped power");
            Add(map, "Odds_PlayerOfficialAttributes", "我方装备实力", "Your equipped power");
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

            // 选人卡正文：两三句白话讲它怎么打、强在哪、怕什么（2026-09-23 owner：「描述改得说人话」）。
            // 与 BossProfiles.json 的原型、能力标签、怪癖 / 异常一一对应，改数据时同步这里。
            AddFighterPlain(map, "shotgun_brawler",
                "端着霰弹枪往脸上冲，贴身一枪伤害极高。离远了基本打不着。",
                "Charges in with a shotgun; a point-blank blast hits very hard. Almost useless at range.");
            AddFighterPlain(map, "frost_marshal",
                "冲得快，贴上去打一轮就绕开，很难被抓住。缺点是胆小：自己残血时可能直接认输。",
                "Rushes in fast, hits, then slips away; hard to pin down. Downside: timid, and may give up when badly hurt.");
            AddFighterPlain(map, "snow_sharpshooter",
                "站在远处稳稳点射，对付站着不动的敌人很拿手。被敌人贴身会很吃力。",
                "Steady shots from range; great against enemies who stand still. Struggles once enemies get up close.");
            AddFighterPlain(map, "long_lens",
                "远程火力压制，站得远、打得准。敌人冲到脸上时会比较被动。",
                "Long-range fire support: stays back and hits hard. Less comfortable once enemies close in.");
            AddFighterPlain(map, "triple_tap",
                "中远距离一口气连开三枪，爆发很猛。偶尔会突然把操作权交给你，让你亲手打一段。",
                "Rapid three-round bursts at mid range for big damage. Now and then it hands control to you for a while.");
            AddFighterPlain(map, "great_xing",
                "皮糙肉厚，正面硬碰硬非常稳。碰上边跑边打的敌人，会打得比较久。",
                "Heavily armored and rock-solid head-on. Against enemies who shoot on the move, fights take a while.");
            AddFighterPlain(map, "mech_snowman",
                "重甲机体，非常扛打，适合顶在前面硬吃伤害。出手不算凶，靠耐打把对手慢慢磨下来。",
                "Heavily plated and very hard to put down; built to stand in front and soak damage. Not a heavy hitter, it wins by outlasting.");
            AddFighterPlain(map, "big_ice",
                "块头大，范围攻击一砸一大片，专克扎堆的敌人。碰上特别强、久攻不下的对手，可能会认怂弃赛。",
                "Huge, with wide area attacks that crush groups. Against a very strong foe it can't beat, it may lose heart and forfeit.");
            AddFighterPlain(map, "bomb_maniac",
                "到处扔炸弹封住一片地方，打扎堆的敌人、拖长战线都拿手。",
                "Lobs bombs to lock down whole areas; great against groups and in long fights.");
            AddFighterPlain(map, "goose_leader",
                "稳扎稳打，能磨也能控场。被三个以上的敌人围住时容易慌，可能直接弃赛。",
                "A steady grinder that controls the fight. Panics when three or more surround it, and may forfeit.");
            AddFighterPlain(map, "orion_hunter",
                "擅长边走边打，拉开距离追着残血的敌人收尾。",
                "Shoots on the move and keeps its distance while finishing off wounded enemies.");
            AddFighterPlain(map, "warden",
                "全场最能打的老手之一，又稳又耐打，收拾残局尤其拿手。",
                "One of the toughest veterans: steady, durable, and especially good at cleaning up a fight.");
        }

        private static void AddFighterPlain(Dictionary<string, string> map, string id, string cn, string en)
        {
            Add(map, "Fighter_" + id + "_Plain", cn, en);
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
                "护甲整备；品质 3。", "Armor kit; quality 3.");
            AddKit(map, "starter_field_helmet", "战地头盔", "Field Helmet",
                "头盔整备；品质 3。", "Helmet kit; quality 3.");
            AddKit(map, "starter_assault_rifle", "突击步枪", "Assault Rifle",
                "主武器整备；品质 4；配发弹药 120 发。", "Primary weapon kit; quality 4; 120 rounds supplied.");
            AddKit(map, "starter_assault_blade", "近身钝器", "Close-quarters Club",
                "近身武器整备；品质 3。", "Melee weapon kit; quality 3.");
            AddKit(map, "starter_marksman_rifle", "精确射手步枪", "Marksman Rifle",
                "主武器整备；品质 5；配发弹药 40 发。", "Primary weapon kit; quality 5; 40 rounds supplied.");
            AddKit(map, "starter_sidearm", "副武器手枪", "Sidearm",
                "副武器整备；品质 2；配发弹药 60 发。", "Secondary weapon kit; quality 2; 60 rounds supplied.");
            AddKit(map, "starter_heavy_plate", "重型防弹衣", "Heavy Plate",
                "护甲整备；品质 5。", "Armor kit; quality 5.");
            AddKit(map, "starter_scout_helmet", "侦察头盔", "Scout Helmet",
                "头盔整备；品质 4。", "Helmet kit; quality 4.");
            AddKit(map, "reward_breacher_gun", "破门枪", "Breacher",
                "主武器整备；品质 5；配发弹药 150 发。", "Primary weapon kit; quality 5; 150 rounds supplied.");
            AddKit(map, "reward_long_barrel", "长弓", "Long Bow",
                "主武器整备；品质 7；配发弹药 30 发。", "Primary weapon kit; quality 7; 30 rounds supplied.");
            AddKit(map, "reward_riot_plate", "防暴甲", "Riot Plate",
                "护甲整备；品质 6。", "Armor kit; quality 6.");
            AddKit(map, "reward_command_helmet", "指挥头盔", "Command Helmet",
                "头盔整备；品质 6。", "Helmet kit; quality 6.");
            AddKit(map, "reward_executioner_blade", "行刑刃", "Executioner Blade",
                "近身武器整备；品质 6。", "Melee weapon kit; quality 6.");
            AddKit(map, "reward_hold_out_pistol", "大口径手枪", "Hold-out Magnum",
                "副武器整备；品质 4；配发弹药 32 发。", "Secondary weapon kit; quality 4; 32 rounds supplied.");
            AddKit(map, "reward_bulwark_plate", "壁垒甲", "Bulwark Plate",
                "护甲整备；品质 7。", "Armor kit; quality 7.");
            AddKit(map, "reward_skirmisher_blade", "游击刃", "Skirmisher Blade",
                "近身武器整备；品质 5。", "Melee weapon kit; quality 5.");
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
            Add(map, "Recovery_ReturnEscrow", "取回托管押品", "Retrieve escrowed stake");
            Add(map, "Recovery_ReturnEscrow_Done",
                "押品已退回仓库", "Escrowed stake returned to your warehouse");
            // 这句原本写死「仓库需要空位」，但退回失败不止这一个成因（阶段机、落盘、
            // 缓冲区未就绪都会走到这里）。仓库满现在由官方溢出缓冲区兜住，不再是主因，
            // 所以文案改为不预设成因，具体 reasonId 由恢复面板原位展示。
            Add(map, "Recovery_ReturnEscrow_Failed",
                "押品退回未完成，请查看恢复面板上的原因说明",
                "Could not return the stake; see the reason shown on the recovery panel");
            Add(map, "Settle_Failed",
                "本场押品结算未完成，押品已保留；请到恢复面板取回",
                "Stake settlement did not finish; your stake is held. Retrieve it from the recovery panel");
            // 中断赛季的「不玩了」出路。此前只有 Suspended 给了同场重开，停在
            // Intermission / MatchBrief 的赛季一个动作都没有，而 recovery-only 闸已立起，
            // 新赛季也开不了——玩家除删档外无路可走。
            Add(map, "Recovery_AbandonSeason", "放弃本赛季并结清押品",
                "Abandon this season and settle the stake");
            Add(map, "Recovery_AbandonSeason_Done",
                "赛季已放弃，可以开新赛季了", "Season abandoned; you can start a new one");
            Add(map, "Recovery_AbandonSeason_Failed",
                "赛季记录未能写入，请稍后再试", "Could not write the season record; try again later");

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
            Add(map, "State_ProductionCertifying", "擂台准备中", "Getting the ring ready");
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
            Add(map, "Unavailable_modeh_certification_failed", "选手热身没通过",
                "The fighters' warm-up failed");
            Add(map, "Unavailable_modeh_owner_missing", "运行实例缺失", "Runtime owner missing");
            Add(map, "Unavailable_TicketRefunded", "已退还船票", "Ticket refunded");

            // 拍铃失败原因：key 由 ModeHCommandController.GetBellFailureLocalizationKey
            // 用 "BellFailed_" + failureReasonId 拼出，后缀必须与 TryRingBell 里的
            // failureReasonId 字面量一一对应。失败不消耗次数，文案统一提示可再试。
            Add(map, "BellFailed_Generic", "拍铃未生效", "The bell had no effect");
            Add(map, "BellFailed_command_owner_mismatch", "拍铃未生效：本场比赛已不由你主持",
                "Bell failed: you no longer host this match");
            Add(map, "BellFailed_command_bell_consumed", "本场拍铃次数已用完",
                "No bell uses left this match");
            Add(map, "BellFailed_command_not_locked", "赛前没有锁定口令，拍铃无口令可下",
                "No command was locked before the match, so the bell has nothing to issue");
            Add(map, "BellFailed_command_no_active_fighter", "场上没有可接令的选手，未消耗拍铃次数",
                "No fighter can take the command; no bell use was consumed");
            Add(map, "BellFailed_command_spec_missing", "锁定的口令已失效，未消耗拍铃次数",
                "The locked command is no longer valid; no bell use was consumed");
            Add(map, "BellFailed_command_signature_owner_absent", "招牌口令的持有者不在场上",
                "The signature command's owner is not in the arena");
            Add(map, "BellFailed_command_requires_relay", "该招牌口令要接力者上场后才能下",
                "That signature command requires the relay fighter to enter first");
            Add(map, "BellFailed_command_requires_enemy_count", "场上敌人数量不满足该口令的条件",
                "The enemy count does not meet that command's condition");
            // command_lock_empty 来自 LockCommand（锁盘时口令为空），与拍铃共用
            // failureReasonId 通道，因此同样需要文案，避免回落到 Generic。
            Add(map, "BellFailed_command_lock_empty", "没有选择要锁定的口令",
                "No command was selected to lock");

            // 开局中止原因：AbortSetup 的 reasonId 是内部标识（不带 modeh_ 前缀），
            // 由 ModeHRuntimeModule.ResolveAbortMessageKey 归类到下面这几条，
            // 保证玩家被传回基地时永远看得到一句解释而不是静默（CR-2026-08-29-013）。
            Add(map, "Abort_Generic", "开局失败，已退回基地", "Setup failed; returned to base");
            Add(map, "Abort_MapUnsupported", "这张地图缺少本模式的点位",
                "This map lacks the mode's spawn points");
            Add(map, "Abort_Lease", "无法接管擂台场地，已退回基地",
                "Could not take over the arena; returned to base");
            Add(map, "Abort_Certification", "开赛前选手热身没通过，已退回基地",
                "The fighters' warm-up failed before the season; returned to base");
            Add(map, "Abort_Cancelled", "已取消入场", "Entry cancelled");
            Add(map, "Abort_Save", "赛季存档写入失败，已退回基地",
                "Season save failed; returned to base");
            Add(map, "Abort_Content", "选手或敌军内容不可用，已退回基地",
                "Fighter or enemy content unavailable; returned to base");

            Add(map, "Diag_Passed", "通过", "Passed");
            Add(map, "Diag_Rejected", "拒绝", "Rejected");
            // F3 逐项认证进度：{0} 当前选手，{1} 总人数。
            Add(map, "Diag_Progress", "正在测试选手（{0}/{1}）", "Testing fighters ({0}/{1})");
            Add(map, "Diag_Signatures", "构建签名", "Build Signatures");
            // 仅 F3 显式启动；普通入场不测试，结果不写入玩家赛季或认证缓存。
            Add(map, "Diag_ReadOnlyNotice",
                "F3 开发测试：逐个检查选手和口令，结果写入日志。结束或停止后回到原选人页，不改赛季和认证缓存。",
                "F3 developer test: checks each fighter and command and logs the results. Finish or stop to return to the same selection page without changing the season or certification cache.");
            Add(map, "Diag_Finishing", "正在整理测试结果", "Finishing test results");
        }

        #endregion

        #region 真实押品

        private static void AddRealStake(Dictionary<string, string> map)
        {
            // §22.1 冻结：入口页、模式说明与 ModeHInteractable 三处都必须显示这一行。
            Add(map, "RealStakeRiskNotice",
                "押注押的是你的钱或背包里的东西：押你的选手赢，输了押上的归庄家。长期来看，庄家总是赢的。",
                "Bets are your real money or backpack items: you back your fighter, and if it loses the house keeps the stake. "
                + "In the long run the house always wins.");

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

            // 押品选择被拒的两条分因。原先只写 DevLog，玩家点了装备毫无反应。
            Add(map, "RealStake_Reject_LimitReached",
                "本场押品已达上限，先取消一件再换",
                "Stake limit reached for this match; deselect one first");
            Add(map, "RealStake_Reject_Unstakeable",
                "这件装备无法押注，换一件试试",
                "This item cannot be staked; try another one");

            // 锁盘被拒的三条分因。原先同样只写 DevLog，而 DevLog 在正式构建里被
            // [Conditional] 整个剥离——玩家点锁盘会毫无反应，堵死在赔率页。
            Add(map, "LockReject_CommandUnavailable",
                "这名选手没有可用口令，无法锁盘",
                "This fighter has no usable command; cannot lock in");
            Add(map, "LockReject_RosterMissing",
                "本场阵容不可用，无法锁盘",
                "The roster for this match is unavailable; cannot lock in");
            Add(map, "LockReject_Generic",
                "暂时无法锁盘，请稍后再试",
                "Cannot lock in right now; try again shortly");

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
