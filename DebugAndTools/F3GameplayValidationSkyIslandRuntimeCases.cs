// ============================================================================
// F3GameplayValidationSkyIslandRuntimeCases.cs - 天空岛岛内验收：运行时状态的只读用例（2026-09-14）
// ============================================================================
// 「实机前减负」一轮补的九条：选项门、遭遇上限、灯与风、官方图鉴（岛上 / 基地各一次）、纪念品（岛上 / 基地各一次）、
// 采集点、信鸽、云蚋运行时。目标是把最后那次实机里「开面板数一数 / 翻图鉴看一眼 / 数仓库里几枚航徽」这类人工行
// 改成读报告。
//
// 纪律与 F3GameplayValidationSkyIsland.cs 头注释一致，而且只会更严：
//   1. **只读**。不写存档、不收录、不采集、不点灯、不合成、不捧放蛙卵、不注册图鉴条目、不解码插图、不发物品。
//      `tests/SkyIslandValidationSuiteGuard.py` 按名单扫本文件。
//   2. **判据与取数分开**：每条用例先在 Unity 侧把状态读成普通值，再交给「纯判据」区里的 `Judge*` 函数判。
//      那一区不碰 Unity，隔离回归 `tests/fixtures/SkyIslandValidationJudges` 把它逐字抽出来执行、喂红样本。
//   3. 判据在当前状态下不成立（白天没有云蚋、一个采集点都还没建、不在基地）记 SKIP，不记 PASS。
//   4. 不引用 Dev 演练套件（F3GameplayValidationSkyIslandDrill.cs）的任何东西——那一套会改状态。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Duckov.NoteIndexs;
using ItemStatsSystem;
using Saves;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        #region 纯判据（隔离回归逐字抽出执行：这一区不许引用 Unity）

        internal const int ExpectedEncounterGroups = 22;
        internal const int ExpectedEncounterEnemies = 64;
        /// <summary>活体上限。生产里是 SkyIslandEncounters 两处内联的 12（BeginChallenge / Tick），没有常量可引。</summary>
        internal const int LivingEnemyCap = 12;
        /// <summary>活敌达到这个数才算「密集段」，帧时间才有判据。</summary>
        internal const int DenseLivingThreshold = 6;
        internal const int JournalHomeExpected = 2;
        internal const int BriefMaxChars = 40;
        /// <summary>第三轮（CR-2026-09-13-010）补的 5 组：三个中继平台、栈道与钟庭各一组。</summary>
        internal static readonly string[] NewAutoEncounterIds = { "K1_Relay", "K2_Relay", "K3_Relay", "E_03", "H_02" };

        /// <summary>
        /// SKY_BOUNTY_GATING 的判据。
        ///
        /// 前三类（清理威胁 / 搜刮补给 / 巡视区域）是**硬门**：可完成量只随本趟进度单调递减，接单时 available ≥ target
        /// 就保证做得完，所以「在手的单做不完」一定是死单，判红。
        ///
        /// 驱蚋是**软门**（`SkyIslandSessionGnatBounty.AvailableGnatCull` 的注释写明）：刷新是概率事件、白天可完成量归零、
        /// 玩家焚香或站在灶火烟里就能把供给压没，而退单出口一直挂着。白天带着没做完的驱蚋单是合法状态，
        /// 只记进 metrics（`soft=…`），不判红——旧判据对四类一律硬判，白天跑 F3 必定假红。
        /// </summary>
        internal static bool JudgeBountyGating(SkyIslandBountyKind[] kinds, Func<SkyIslandBountyKind, int> targetFor,
            Func<SkyIslandBountyKind, int> availableFor, bool hasActive, SkyIslandBountyKind active, int progress, int target,
            int completedRounds, int groundRegions, out string metrics, out string reason)
        {
            reason = null;
            List<string> parts = new List<string>();
            List<string> errors = new List<string>();
            string soft = "none";
            for (int i = 0; i < kinds.Length; i++)
            {
                int kindTarget = targetFor(kinds[i]);
                int available = availableFor(kinds[i]);
                parts.Add(kinds[i] + "=avail " + available + "/target " + kindTarget);
                if (kindTarget <= 0) errors.Add(kinds[i] + ":target_not_positive");
            }
            // 派单门控的全部意义就是「只派做得完的单」：接了单却没有可完成量就是死单（驱蚋除外，见上）。
            if (hasActive && availableFor(active) + progress < target)
            {
                if (active == SkyIslandBountyKind.Gnats) soft = "gnats_unfinishable_now(abandon_available)";
                else errors.Add("active_contract_unfinishable");
            }
            if (completedRounds > SkyIslandBounty.MaxRounds) errors.Add("rounds_over_max");
            // 巡岛可完成量不得超过本局真正索引到的区域数。旧口径按 POI_ 节点计数，装饰节点 POI_B_Mural 让它
            // 永久多算 1——剩 3 个真区域时算成 4，正好派得出一张做不完的「巡视群岛区域 ×4」。
            if (availableFor(SkyIslandBountyKind.Survey) > groundRegions) errors.Add("survey_available_exceeds_regions");
            metrics = "ground_regions=" + groundRegions + ",rounds=" + completedRounds + "/" + SkyIslandBounty.MaxRounds
                + ",active=" + active + ",progress=" + progress + "/" + target + ",soft=" + soft
                + " | " + string.Join(" ", parts.ToArray());
            if (errors.Count > 0) reason = "委托门控不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        /// <summary>
        /// SKY_CHOICE_GATES 的判据。
        ///
        /// ① 全部剧情动作 × {当前进度, 新档}：<c>CanApply</c> 与 <c>TryApply</c> 的判决逐条一致。两者都经过私有的
        ///    <c>SkyIslandStoryRules.Describe</c>（结构由 <c>tests/SkyIslandChoiceGateGuard.py</c> 钉），这里在真实存档上把行为跑一遍：
        ///    能做时 TryApply 必须真的改了旗标；给了「还差什么」时它必须与 TryApply 的拒绝回话逐字相同。
        /// ② 选项类型没有「灰掉」这一态：字段只有 Label 与 Select。
        /// ③ 本趟打开过手记时，首页实际挂出的项数必须是 2；没打开过只记 not_opened_this_raid，不判。
        /// ④ 20 处见闻都有自己的导语（不是兜底文案）；中文语境下每句 ≤40 字。英文长度另有版式把关，这里不判。
        /// </summary>
        internal static bool JudgeChoiceGates(SkyIslandStoryData current, int journalHomeChoices, bool chinese,
            string[] choiceFields, out string metrics, out string reason)
        {
            reason = null;
            List<string> errors = new List<string>();
            int pairs = 0, applicable = 0, hinted = 0;
            SkyIslandStoryData[] samples = { current, SkyIslandStoryRules.CreateDefault() };
            for (int s = 0; s < samples.Length; s++)
            {
                SkyIslandStoryData sample = samples[s];
                if (sample == null) { errors.Add("story_data_missing"); continue; }
                foreach (SkyIslandStoryAction action in Enum.GetValues(typeof(SkyIslandStoryAction)))
                {
                    string blocker;
                    bool can = SkyIslandStoryRules.CanApply(sample, action, out blocker);
                    SkyIslandStoryData candidate;
                    string message;
                    bool tried = SkyIslandStoryRules.TryApply(sample, action, out candidate, out message);
                    pairs++;
                    string tag = (s == 0 ? "current:" : "default:") + action;
                    if (can != tried) { errors.Add(tag + ":can_apply=" + can + "/try_apply=" + tried); continue; }
                    if (can)
                    {
                        applicable++;
                        if (candidate == null || candidate.flags == sample.flags) errors.Add(tag + ":apply_changed_nothing");
                    }
                    else if (blocker != null)
                    {
                        hinted++;
                        if (!string.Equals(blocker, message, StringComparison.Ordinal)) errors.Add(tag + ":hint_differs_from_refusal");
                    }
                }
            }
            bool shapeOk = choiceFields != null && choiceFields.Length == 2
                && Array.IndexOf(choiceFields, "Label") >= 0 && Array.IndexOf(choiceFields, "Select") >= 0;
            string fields = choiceFields == null ? "null" : string.Join("+", choiceFields);
            if (!shapeOk) errors.Add("choice_type_fields=" + fields);
            if (journalHomeChoices >= 0 && journalHomeChoices != JournalHomeExpected)
                errors.Add("journal_home_items=" + journalHomeChoices + "/" + JournalHomeExpected);

            string fallback = SkyIslandPointText.Brief("__sky_island_not_a_point__");
            List<string> fallbackBriefs = new List<string>();
            List<string> longBriefs = new List<string>();
            int briefs = 0, longest = 0;
            string[][] chapters = SkyIslandJournal.Chapters;
            for (int c = 0; c < chapters.Length; c++)
            {
                for (int i = 0; i < chapters[c].Length; i++)
                {
                    string id = chapters[c][i];
                    string brief = SkyIslandPointText.Brief(id);
                    briefs++;
                    if (string.IsNullOrEmpty(brief) || string.Equals(brief, fallback, StringComparison.Ordinal))
                    {
                        fallbackBriefs.Add(id);
                        continue;
                    }
                    if (brief.Length > longest) longest = brief.Length;
                    if (chinese && brief.Length > BriefMaxChars) longBriefs.Add(id + "=" + brief.Length);
                }
            }
            if (briefs != SkyIslandJournal.NoteCount) errors.Add("brief_points=" + briefs + "/" + SkyIslandJournal.NoteCount);
            if (fallbackBriefs.Count > 0) errors.Add("brief_fallback:" + string.Join("+", fallbackBriefs.ToArray()));
            if (longBriefs.Count > 0) errors.Add("brief_over_" + BriefMaxChars + ":" + string.Join("+", longBriefs.ToArray()));

            metrics = "action_pairs=" + pairs + ",applicable=" + applicable + ",hinted=" + hinted
                + ",journal_home=" + (journalHomeChoices < 0 ? "not_opened_this_raid" : journalHomeChoices.ToString())
                + ",briefs=" + briefs + ",longest_brief=" + longest + (chinese ? "(zh)" : "(en,length_not_judged)")
                + ",choice_fields=" + fields;
            if (errors.Count > 0) reason = "选项门不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        /// <summary>
        /// SKY_ENCOUNTER_CAP 的内容表半边：遭遇组 21、敌人 61；第三轮新增的 5 组都是自动组；
        /// 码头 A 与风铃集 B 没有自动组（刻意的安全枢纽）。区域按「id 前缀」与「标记名第二段」两头认，任一头落在 A / B 都算。
        /// </summary>
        internal static bool JudgeEncounterTable(SkyIslandContentData content, out string metrics, out string reason)
        {
            reason = null;
            SkyIslandEncounterDefinition[] encounters = content == null ? null : content.Encounters;
            if (encounters == null) { metrics = "encounters=null"; reason = "内容表没有遭遇组"; return false; }
            List<string> errors = new List<string>();
            int enemies = 0, auto = 0;
            List<string> safeHubAuto = new List<string>();
            for (int i = 0; i < encounters.Length; i++)
            {
                SkyIslandEncounterDefinition encounter = encounters[i];
                enemies += encounter.Count;
                if (encounter.Manual) continue;
                auto++;
                string byId = RegionToken(encounter.Id, false), byMarker = RegionToken(encounter.Marker, true);
                if (byId == "A" || byId == "B" || byMarker == "A" || byMarker == "B")
                    safeHubAuto.Add(encounter.Id + "@" + encounter.Marker);
            }
            List<string> newGroups = new List<string>();
            for (int i = 0; i < NewAutoEncounterIds.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < encounters.Length; j++)
                {
                    if (!string.Equals(encounters[j].Id, NewAutoEncounterIds[i], StringComparison.Ordinal)) continue;
                    found = true;
                    if (encounters[j].Manual) newGroups.Add(NewAutoEncounterIds[i] + ":manual");
                }
                if (!found) newGroups.Add(NewAutoEncounterIds[i] + ":missing");
            }
            if (encounters.Length != ExpectedEncounterGroups) errors.Add("groups=" + encounters.Length + "/" + ExpectedEncounterGroups);
            if (enemies != ExpectedEncounterEnemies) errors.Add("enemies=" + enemies + "/" + ExpectedEncounterEnemies);
            if (newGroups.Count > 0) errors.Add("new_auto_groups:" + string.Join("+", newGroups.ToArray()));
            if (safeHubAuto.Count > 0) errors.Add("auto_group_in_safe_hub:" + string.Join("+", safeHubAuto.ToArray()));
            metrics = "table_groups=" + encounters.Length + ",table_enemies=" + enemies + ",auto_groups=" + auto
                + ",new_auto_ok=" + (newGroups.Count == 0) + ",safe_hub_auto=" + safeHubAuto.Count;
            if (errors.Count > 0) reason = "遭遇内容表不合格：" + string.Join(",", errors.ToArray()) + "；";
            return errors.Count == 0;
        }

        /// <summary>`C_02` → C、`K1_Relay` → K1（按 id 取第一段）；`EnemySpawn_S1` → S1、`Relay_K1` → K1（按标记取第二段）。</summary>
        internal static string RegionToken(string name, bool marker)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string[] parts = name.Split('_');
            if (marker) return parts.Length > 1 ? parts[1] : string.Empty;
            return parts[0];
        }

        /// <summary>
        /// 只能经过某道剧情门才走得到的岛：{岛, 门}。门关着时这些岛上的点「走不到」是对的，SKY_GATE_REACHABILITY
        /// 不把它们算必到点，反过来核对「确实被挡住」。按 layout.json 的桥，目前只有归航钟庭 H：它只接 E–H 一座桥，门就在桥上；
        /// 三条捷径门与折翎门只挡近路，挡不住任何一座岛（中继平台从远端岛走得过去）。
        /// 这张表由 tests/SkyIslandGateNavigationPropertyTest.py 用真实导航面在 32 种开闭组合下逐条复算、比对，
        /// 新增自动遭遇组或改桥时对不上就红——2026-09-14 实机报告里 H_02 的假红，就是 09-13 补密后缺了这层分类。
        /// </summary>
        internal static readonly string[][] GateLockedIslands = { new[] { "H", "BellCourt" } };

        /// <summary>标记所在的岛只能经过某道门才到得了时返回那道门的 id，否则 null。岛按标记名第二段认（`Search_H_02` → H）。</summary>
        internal static string ReachabilityGateFor(string marker)
        {
            string island = RegionToken(marker, true);
            for (int i = 0; i < GateLockedIslands.Length; i++)
                if (string.Equals(GateLockedIslands[i][0], island, StringComparison.Ordinal)) return GateLockedIslands[i][1];
            return null;
        }

        /// <summary>探路终点离目标的水平容差（米）。离线属性测试证明全部标记都落在导航面上，真走到了应当几乎贴合；门后的点离门这一侧至少 8 m。</summary>
        internal const float ProbeReachHorizontalMeters = 2f;
        /// <summary>探路终点离目标的竖直容差（米）：挡住「正上方 / 正下方另一层」的误判。</summary>
        internal const float ProbeReachVerticalMeters = 2.5f;

        /// <summary>
        /// 探路「真的走到了目标」，而不只是「路算完了」。
        /// 2026-09-14 实机报告：五门全关时钟庭地标 POI_H（离门约 73 m）被记成可达，而离线属性测试证明它此时走不到；
        /// 只有离门约 119 m 的 Search_H_02 报了错。看起来是 A* 在目标走不到时把路算到出发一侧离目标最近的点、照样报完成，
        /// 所以只看 <c>path.error</c> 的旧判据，PASS 抓不到真正的软锁。
        /// </summary>
        internal static bool ProbeReachedTarget(float endX, float endY, float endZ, float targetX, float targetY, float targetZ, out float gap)
        {
            float dx = endX - targetX, dz = endZ - targetZ;
            gap = (float)Math.Sqrt(dx * dx + dz * dz);
            return gap <= ProbeReachHorizontalMeters && Math.Abs(endY - targetY) <= ProbeReachVerticalMeters;
        }

        /// <summary>
        /// SKY_ENCOUNTER_CAP 的运行时半边：采样窗口里活敌峰值 ≤12；本局装配出的遭遇组数与内容表一致；
        /// 活敌达到密集段时帧时间 p95 不越阈值（与 SKY_PERF_FINAL_5S 同一条：基线 p95 × 1.75，至少 50 ms）。
        /// 不在密集段时帧时间只进 metrics，不判。
        /// </summary>
        internal static bool JudgeEncounterRuntime(int groupsBuilt, int livingStart, int livingPeak, int samples, float p95Ms,
            float peakMs, float thresholdMs, out string metrics, out string reason)
        {
            reason = null;
            List<string> errors = new List<string>();
            bool dense = livingPeak >= DenseLivingThreshold;
            if (groupsBuilt != ExpectedEncounterGroups) errors.Add("groups_built=" + groupsBuilt + "/" + ExpectedEncounterGroups);
            if (livingPeak > LivingEnemyCap) errors.Add("living_peak=" + livingPeak + ">" + LivingEnemyCap);
            if (dense && samples > 0 && p95Ms > thresholdMs)
                errors.Add("dense_p95_ms=" + p95Ms.ToString("F2") + ">" + thresholdMs.ToString("F2"));
            metrics = "groups_built=" + groupsBuilt + ",living_start=" + livingStart + ",living_peak=" + livingPeak + "/" + LivingEnemyCap
                + ",dense=" + dense + "(>=" + DenseLivingThreshold + "),samples=" + samples + ",p95_ms=" + p95Ms.ToString("F2")
                + ",peak_ms=" + peakMs.ToString("F2") + ",threshold_ms=" + thresholdMs.ToString("F2");
            if (errors.Count > 0) reason = "遭遇上限或密集段帧时间不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        /// <summary>
        /// SKY_LAMPS_WIND 的判据。
        /// ① 三处灶火锚点都在且都亮着；七盏风晶灯「存档里点过」⇔「场景里有那盏灯」，锚点一个不缺；
        ///    owner 记的缺失盏数为 0，owner 手里的盏数与存档算出来的一致。
        /// ② 风级读回：最近一次夜风采样按纯规则复算，大风档必须对得上；实际施加的风级要么等于大风档，
        ///    要么是噬风之核把大风压成的微风；参与判定的盏数必须是「存档盏数 − 缺失锚点」。还没采过样只记 not_sampled_yet。
        /// </summary>
        internal static bool JudgeLampsWind(SkyIslandStoryData data, Func<string, bool> anchorPresent, Func<string, bool> fireBuilt,
            int missingFireAnchors, int lightsLit, SkyIslandWindSample wind, out string metrics, out string reason)
        {
            reason = null;
            List<string> errors = new List<string>();
            List<string> missingAnchors = new List<string>();
            List<string> mismatch = new List<string>();
            int lampsLit = 0;
            string[] hearths = SkyIslandLights.HearthMarkers;
            for (int i = 0; i < hearths.Length; i++)
            {
                if (!anchorPresent(hearths[i])) missingAnchors.Add(hearths[i]);
                else if (!fireBuilt(hearths[i])) mismatch.Add(hearths[i] + ":hearth_dark");
            }
            SkyIslandLight[] lamps = SkyIslandLights.All;
            for (int i = 0; i < lamps.Length; i++)
            {
                bool lit = SkyIslandLights.Lit(data, lamps[i].Id);
                if (lit) lampsLit++;
                if (!anchorPresent(lamps[i].Marker)) { missingAnchors.Add(lamps[i].Marker); continue; }
                bool shown = fireBuilt(lamps[i].Marker);
                if (lit != shown) mismatch.Add(lamps[i].Id + (lit ? ":saved_lit_but_dark" : ":dark_in_save_but_lit"));
            }
            if (missingAnchors.Count > 0) errors.Add("anchor_missing:" + string.Join("+", missingAnchors.ToArray()));
            if (missingFireAnchors != 0) errors.Add("missing_fire_anchors=" + missingFireAnchors);
            if (mismatch.Count > 0) errors.Add("lamp_vs_save:" + string.Join("+", mismatch.ToArray()));
            int saved = SkyIslandLights.LitCount(data);
            if (lightsLit != saved) errors.Add("lights_lit=" + lightsLit + "/save=" + saved);

            string windText = "not_sampled_yet";
            if (wind.Sampled)
            {
                int gale = SkyIslandFieldcraftRules.WindLevel(SkyIslandFieldcraftRules.NightWind(wind.Night, wind.EffectiveLights),
                    wind.OnBridge, wind.OnBoardwalk, wind.StormPending);
                bool eased = wind.Level == SkyIslandFieldcraftRules.CoreEased(wind.Gale, true);
                if (wind.Gale != gale) errors.Add("wind_gale_readback=" + wind.Gale + "/rule=" + gale);
                if (wind.Level != wind.Gale && !eased) errors.Add("wind_level_readback=" + wind.Level + "/gale=" + wind.Gale);
                if (wind.Level < 0 || wind.Level > 2) errors.Add("wind_level_out_of_range=" + wind.Level);
                if (wind.EffectiveLights != lightsLit - missingFireAnchors)
                    errors.Add("wind_effective_lights=" + wind.EffectiveLights + "/" + (lightsLit - missingFireAnchors));
                windText = "level=" + wind.Level + ",gale=" + wind.Gale + ",night=" + wind.Night + ",bridge=" + wind.OnBridge
                    + ",boardwalk=" + wind.OnBoardwalk + ",storm_pending=" + wind.StormPending + ",effective_lights=" + wind.EffectiveLights;
            }
            metrics = "lamps_saved=" + lampsLit + "/" + lamps.Length + ",lights_lit=" + lightsLit + "/" + SkyIslandLights.Target
                + ",hearths=" + hearths.Length + ",missing_fire_anchors=" + missingFireAnchors + ",wind(" + windText + ")";
            if (errors.Count > 0) reason = "灯与风不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        /// <summary>
        /// SKY_OFFICIAL_NOTES 的判据（岛上、基地各跑一次）。口径同 <c>SkyIslandNoteBridge</c>：**我们的存档是唯一权威，官方图鉴只做镜像**。
        /// 20 处见闻在官方 NoteIndex 的 notes 列表里各恰好一条（只写字典不写列表的话界面一条也看不见）；
        /// 官方解锁状态与我们的存档逐条一致；标题取得到、且不是裸 key（本地化没注入时官方显示的就是 key）。
        /// </summary>
        internal static bool JudgeOfficialNotes(SkyIslandStoryData data, IList<string> officialKeys, Func<string, bool> unlocked,
            Func<string, string> titleOf, string where, out string metrics, out string reason)
        {
            reason = null;
            List<string> errors = new List<string>();
            List<string> missing = new List<string>(), duplicated = new List<string>(), state = new List<string>(),
                rawTitles = new List<string>();
            int total = 0, recorded = 0, lit = 0;
            string[][] chapters = SkyIslandJournal.Chapters;
            for (int c = 0; c < chapters.Length; c++)
            {
                for (int i = 0; i < chapters[c].Length; i++)
                {
                    string id = chapters[c][i];
                    string key = SkyIslandNoteBridge.BuildNoteKey(id);
                    total++;
                    int count = 0;
                    for (int n = 0; n < officialKeys.Count; n++)
                        if (string.Equals(officialKeys[n], key, StringComparison.Ordinal)) count++;
                    if (count == 0) { missing.Add(id); continue; }
                    if (count > 1) duplicated.Add(id + "x" + count);
                    bool ours = data != null && SkyIslandJournal.Recorded(data, id);
                    bool theirs = unlocked(key);
                    if (ours) recorded++;
                    if (theirs) lit++;
                    if (ours != theirs) state.Add(id + (ours ? ":recorded_but_locked" : ":not_recorded_but_unlocked"));
                    string title = titleOf(key);
                    if (string.IsNullOrEmpty(title) || title.IndexOf("Note_" + key + "_Title", StringComparison.Ordinal) >= 0)
                        rawTitles.Add(id);
                }
            }
            if (data == null) errors.Add("story_data_missing");
            if (total != SkyIslandJournal.NoteCount) errors.Add("points=" + total + "/" + SkyIslandJournal.NoteCount);
            if (missing.Count > 0) errors.Add("not_in_notes_list:" + string.Join("+", missing.ToArray()));
            if (duplicated.Count > 0) errors.Add("duplicated:" + string.Join("+", duplicated.ToArray()));
            if (state.Count > 0) errors.Add("unlock_vs_save:" + string.Join("+", state.ToArray()));
            if (rawTitles.Count > 0) errors.Add("raw_or_empty_title:" + string.Join("+", rawTitles.ToArray()));
            metrics = "where=" + where + ",listed=" + (total - missing.Count) + "/" + total + ",recorded_in_save=" + recorded
                + ",unlocked_in_official=" + lit + ",duplicated=" + duplicated.Count + ",raw_titles=" + rawTitles.Count;
            if (errors.Count > 0) reason = "官方图鉴镜像不合格（" + where + "）：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        /// <summary>
        /// SKY_KEEPSAKE_ITEMS 的判据（岛上、基地各跑一次）。晴岚航徽、噬风之核各至多一件（背包含容器 + 基地仓库）；
        /// 两件的图标取得到、且不是运行时兜底克隆源的图标。在基地时再核对岛上耗材一样都没跟回来：
        /// 采集 / 合成 / 夜风 owner、云蚋 owner 与头顶那盏风灯都已经不在。
        /// </summary>
        internal static bool JudgeKeepsakes(int badgeCount, int coreCount, string badgeIcon, string coreIcon, bool atBase,
            bool fieldcraftLeft, bool gnatsLeft, bool lanternLeft, out string metrics, out string reason)
        {
            reason = null;
            List<string> errors = new List<string>();
            if (badgeCount > 1) errors.Add("homecoming_badge=" + badgeCount);
            if (coreCount > 1) errors.Add("windeater_core=" + coreCount);
            if (!string.Equals(badgeIcon, "ok", StringComparison.Ordinal)) errors.Add("badge_icon:" + badgeIcon);
            if (!string.Equals(coreIcon, "ok", StringComparison.Ordinal)) errors.Add("core_icon:" + coreIcon);
            if (atBase)
            {
                if (fieldcraftLeft) errors.Add("fieldcraft_owner_left_on_base");
                if (gnatsLeft) errors.Add("gnat_owner_left_on_base");
                if (lanternLeft) errors.Add("wind_lantern_light_left_on_base");
            }
            metrics = "where=" + (atBase ? "base" : "island") + ",badge=" + badgeCount + "/1,core=" + coreCount + "/1"
                + ",badge_icon=" + badgeIcon + ",core_icon=" + coreIcon
                + (atBase ? ",fieldcraft_left=" + fieldcraftLeft + ",gnats_left=" + gnatsLeft + ",lantern_left=" + lanternLeft : string.Empty);
            if (errors.Count > 0) reason = "纪念品或离岛残留不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        /// <summary>
        /// SKY_GATHER_NODES 的判据。30 处采集点全部落位（离线交互竞争属性测试已按真实几何算过都落得下，少一处就是真缺陷）；
        /// 已经建出来的每一处都有贴地光斑精灵，读条时长读回来等于规则值（官方字段改名时会变成秒采）。
        /// 交互体走进 60 m 才建：一个都还没建时后两条判据不适用，记 SKIP。
        /// </summary>
        internal static bool JudgeGatherNodes(int nodes, int placed, int built, int harvested, IList<string> glowMissing,
            IList<string> timeMismatch, out string metrics, out string reason)
        {
            reason = null;
            metrics = "nodes=" + nodes + ",placed=" + placed + ",built=" + built + ",harvested=" + harvested
                + ",glow_missing=" + glowMissing.Count + ",time_mismatch=" + timeMismatch.Count;
            if (placed != nodes)
            {
                reason = "有采集点没有落位（运行时静默跳过）：placed=" + placed + "/" + nodes;
                return false;
            }
            if (built == 0) throw new SkyIslandSkipCase("no_gather_node_built_yet", metrics);
            List<string> errors = new List<string>();
            if (glowMissing.Count > 0) errors.Add("glow_disc_missing:" + JoinList(glowMissing));
            if (timeMismatch.Count > 0) errors.Add("interact_time_mismatch:" + JoinList(timeMismatch));
            if (errors.Count > 0) reason = "采集点不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        /// <summary>
        /// SKY_LETTER_PIGEON 的判据。
        /// 信鸽在场 ⇔ 手里有信，且在场时一次性落点闩一定是合上的；带的信没收过、前置已满足，并且就是
        /// <c>SkyIslandLetters.NextFor</c> 算出来的那一封——唯一的例外是放下之后剧情推进、一封有前置的信排到了前面
        /// （它会等这只信鸽被收下之后再来，TickPigeon 的注释写明「未收的信不被替换」）。
        /// 闩合上而信鸽不在是合法的（这一趟找不到净空、或存档写不进去就不放），只记 metrics。
        /// </summary>
        internal static bool JudgeLetterPigeon(SkyIslandStoryData data, string carriedLetterId, bool latch, bool present,
            bool objectNamed, bool canWrite, out string metrics, out string reason)
        {
            reason = null;
            List<string> errors = new List<string>();
            SkyIslandLetter next = SkyIslandLetters.NextFor(data);
            SkyIslandLetter sameRaid = SkyIslandLetters.NextSameRaidFor(data);
            if (data == null) errors.Add("story_data_missing");
            if (present && !latch) errors.Add("pigeon_present_without_latch");
            if (present && carriedLetterId == null) errors.Add("pigeon_without_letter");
            if (!present && carriedLetterId != null) errors.Add("letter_without_pigeon");
            if (present && carriedLetterId != null)
            {
                if (!objectNamed) errors.Add("pigeon_object_not_named_after_letter");
                SkyIslandLetter carried = SkyIslandLetters.Find(carriedLetterId);
                if (carried == null) errors.Add("unknown_letter:" + carriedLetterId);
                else
                {
                    if (SkyIslandLetters.Collected(data, carried.Id)) errors.Add("carrying_collected_letter:" + carried.Id);
                    if (!SkyIslandLetters.Unlocked(data, carried)) errors.Add("carrying_locked_letter:" + carried.Id);
                    bool isNext = next != null && string.Equals(next.Id, carried.Id, StringComparison.Ordinal);
                    bool preemptedByGated = next != null && next.Requires != SkyIslandStoryFlag.None;
                    if (!isNext && !preemptedByGated)
                        errors.Add("carrying_" + carried.Id + "_but_next_is_" + (next == null ? "none" : next.Id));
                }
            }
            metrics = "present=" + present + ",latch=" + latch + ",carried=" + (carriedLetterId ?? "none")
                + ",next_for=" + (next == null ? "none" : next.Id) + ",next_same_raid=" + (sameRaid == null ? "none" : sameRaid.Id)
                + ",collected=" + SkyIslandLetters.CollectedCount(data) + ",can_write=" + canWrite;
            if (errors.Count > 0) reason = "信鸽不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        /// <summary>
        /// SKY_STORM_ECHO 的判据（2026-09-14 B 轮）。噬风·回响按本趟计、不进存档，所以这里核的是「本趟状态与规则一致、而且没漏进存档」：
        /// ① 本趟引风次数在 [0, MaxPerRaid] 里；引过就一定已经敲钟、打过噬风；回响组「打响过」⇔ 本趟引过；清场了就一定打响过；
        /// ② 会话此刻给出的「能不能引 / 还差什么」与纯规则 <c>SkyIslandStoryRules.CanSummonStormEcho</c> 按同一组输入复算逐字一致
        ///    （装置面板挂不挂与点下去成不成走的都是会话那一处）；
        /// ③ 回响的遭遇 id 从不出现在存档的清场表里（进了就会被遭遇 owner 当成永久已清）。
        /// 这份存档还没解锁（没敲钟或没打噬风）而且本趟没引过时，开启状态没有判据：先核完 ①③，再记 SKIP。
        /// </summary>
        internal static bool JudgeStormEcho(SkyIslandStoryData data, int starts, bool groupStarted, bool groupCleared, bool coreCarried,
            int windcrystals, bool canSummonNow, string blockerNow, out string metrics, out string reason)
        {
            reason = null;
            List<string> errors = new List<string>();
            string ruleBlocker;
            bool rule = SkyIslandStoryRules.CanSummonStormEcho(data, starts >= SkyIslandStormEchoRules.MaxPerRaid, coreCarried,
                windcrystals, out ruleBlocker);
            bool unlocked = SkyIslandStormEchoRules.UnlockedBySave(data);
            if (data == null) errors.Add("story_data_missing");
            if (starts < 0 || starts > SkyIslandStormEchoRules.MaxPerRaid)
                errors.Add("starts_this_raid=" + starts + "/" + SkyIslandStormEchoRules.MaxPerRaid);
            if (starts > 0 && !unlocked) errors.Add("started_without_ending_and_storm");
            if (groupStarted != (starts > 0)) errors.Add("group_started=" + groupStarted + "/starts=" + starts);
            if (groupCleared && !groupStarted) errors.Add("cleared_without_start");
            if (canSummonNow != rule) errors.Add("can_summon_now=" + canSummonNow + "/rule=" + rule);
            if (!string.Equals(blockerNow, ruleBlocker, StringComparison.Ordinal)) errors.Add("blocker_differs_from_rule");
            if (data != null && data.EncounterCleared(SkyIslandStormEchoRules.EncounterId)) errors.Add("echo_clear_written_to_save");
            metrics = "unlocked=" + unlocked + ",ending=" + (data != null && data.Has(SkyIslandStoryFlag.Ending))
                + ",storm_resolved=" + (data != null && data.StormResolved) + ",starts=" + starts + "/" + SkyIslandStormEchoRules.MaxPerRaid
                + ",group_started=" + groupStarted + ",group_cleared=" + groupCleared + ",core=" + coreCarried
                + ",windcrystals=" + windcrystals + ",can_summon_now=" + canSummonNow + ",blocker=" + (ruleBlocker == null ? "none" : "shown");
            if (errors.Count > 0)
            {
                reason = "噬风·回响不合格：" + string.Join(",", errors.ToArray());
                return false;
            }
            if (!unlocked && starts == 0) throw new SkyIslandSkipCase("echo_locked_by_story", metrics);
            return true;
        }

        /// <summary>
        /// SKY_LOOT_BANDS 的预热半边（CR-2026-09-14-014）。物资池应当在进岛装配时（读条画面下）就建好：
        /// 用例在第一次调用 Get 之前先读缓存，每个预热带都必须已经在缓存里；这一趟的预热必须真的跑过，
        /// 新建的带数 + 命中缓存的带数 = 预热带数，而且新建了几个带就至少分了几帧。
        /// </summary>
        internal static bool JudgeLootPrewarm(int[][] bands, bool[] cachedBeforeCase, bool ran, int built, int cacheHits, int frames,
            double totalMs, double maxBandMs, out string metrics, out string reason)
        {
            reason = null;
            List<string> errors = new List<string>();
            int count = bands == null ? 0 : bands.Length;
            List<string> missing = new List<string>();
            int cached = 0;
            for (int i = 0; i < count; i++)
            {
                bool hit = cachedBeforeCase != null && i < cachedBeforeCase.Length && cachedBeforeCase[i];
                if (hit) cached++;
                else missing.Add(bands[i][0] + "-" + bands[i][1]);
            }
            if (count == 0) errors.Add("no_prewarm_bands");
            if (!ran) errors.Add("prewarm_never_ran");
            else
            {
                if (built + cacheHits != count) errors.Add("prewarm_bands=" + (built + cacheHits) + "/" + count);
                if (frames < built) errors.Add("prewarm_frames=" + frames + "<built=" + built);
            }
            if (missing.Count > 0) errors.Add("not_cached_before_case:" + string.Join("+", missing.ToArray()));
            metrics = "prewarm_ran=" + ran + ",prewarm_bands=" + count + ",built=" + built + ",cache_hits=" + cacheHits
                + ",frames=" + frames + ",total_ms=" + totalMs.ToString("F0") + ",max_band_ms=" + maxBandMs.ToString("F0")
                + ",cached_before_case=" + cached + "/" + count;
            if (errors.Count > 0) reason = "物资池预热不合格：" + string.Join(",", errors.ToArray()) + "；";
            return errors.Count == 0;
        }

        /// <summary>分项计时报告里列出 p95 最高的前几段。</summary>
        internal const int FrameProfileTopSegments = 3;

        /// <summary>
        /// SKY_PERF_BASELINE_5S / SKY_PERF_FINAL_5S 的分项计时半边（Dev 构建，<c>SkyIslandFrameProfile</c>）。只诊断，不另设帧时间阈值：
        /// ① 录到了帧（会话在跑而一帧都没录到，说明 Start / Mark 没接上或录制没开）；② 每帧的段数与段名表一致、没有负数；
        /// ③ 各段 p95 与最大值、各段合计、p95 最高的前三段，连同活动灯数、开阴影的灯数与可见 renderer 数写进 metrics。
        /// </summary>
        internal static bool JudgeFrameProfile(string[] segments, IList<float[]> frames, int lightsActive, int lightsShadowed,
            int renderersVisible, int renderersTotal, out string metrics, out string reason)
        {
            reason = null;
            string scene = ",lights_active=" + lightsActive + ",lights_shadowed=" + lightsShadowed
                + ",renderers_visible=" + renderersVisible + "/" + renderersTotal;
            int width = segments == null ? 0 : segments.Length;
            if (frames == null)
            {
                metrics = "profile=unavailable" + scene;
                reason = "分项计时不可用：不是 Dev 构建，或录制没有交出来";
                return false;
            }
            List<string> errors = new List<string>();
            if (width == 0) errors.Add("segment_table_empty");
            List<float>[] columns = new List<float>[width];
            for (int s = 0; s < width; s++) columns[s] = new List<float>(frames.Count);
            List<float> totals = new List<float>(frames.Count);
            int badShape = 0, negative = 0;
            for (int f = 0; f < frames.Count; f++)
            {
                float[] row = frames[f];
                if (row == null || row.Length != width)
                {
                    badShape++;
                    continue;
                }
                float total = 0f;
                for (int s = 0; s < width; s++)
                {
                    float value = row[s];
                    if (value < 0f || float.IsNaN(value) || float.IsInfinity(value))
                    {
                        negative++;
                        value = 0f;
                    }
                    columns[s].Add(value);
                    total += value;
                }
                totals.Add(total);
            }
            if (frames.Count == 0) errors.Add("profile_no_frames");
            if (badShape > 0) errors.Add("frame_shape_mismatch=" + badShape);
            if (negative > 0) errors.Add("negative_or_nan_segment=" + negative);
            float[] p95 = new float[width];
            List<string> parts = new List<string>(width);
            for (int s = 0; s < width; s++)
            {
                float max;
                p95[s] = FrameP95(columns[s], out max);
                parts.Add(segments[s] + "=" + p95[s].ToString("F2") + "/" + max.ToString("F2"));
            }
            float totalMax;
            float totalP95 = FrameP95(totals, out totalMax);
            int[] order = new int[width];
            for (int s = 0; s < width; s++) order[s] = s;
            Array.Sort(order, (a, b) => p95[b].CompareTo(p95[a]));
            List<string> top = new List<string>();
            for (int i = 0; i < width && i < FrameProfileTopSegments; i++) top.Add(segments[order[i]] + ":" + p95[order[i]].ToString("F2"));
            metrics = "profile_frames=" + totals.Count + ",instrumented_p95_ms=" + totalP95.ToString("F2")
                + ",instrumented_max_ms=" + totalMax.ToString("F2") + ",top_p95=" + string.Join("+", top.ToArray())
                + ",seg_p95_max_ms(" + string.Join(";", parts.ToArray()) + ")" + scene;
            if (errors.Count > 0) reason = "分项计时不合格：" + string.Join(",", errors.ToArray());
            return errors.Count == 0;
        }

        /// <summary>与 SamplePerformance 同一个 p95 口径（升序第 ceil(n×0.95) 个），顺带给出最大值。空列表返回 0。</summary>
        internal static float FrameP95(List<float> values, out float max)
        {
            max = 0f;
            if (values == null || values.Count == 0) return 0f;
            List<float> sorted = new List<float>(values);
            sorted.Sort();
            max = sorted[sorted.Count - 1];
            int index = (int)Math.Ceiling(sorted.Count * 0.95) - 1;
            if (index < 0) index = 0;
            if (index > sorted.Count - 1) index = sorted.Count - 1;
            return sorted[index];
        }

        private static string JoinList(IList<string> values)
        {
            string[] array = new string[values.Count];
            for (int i = 0; i < values.Count; i++) array[i] = values[i];
            return string.Join("+", array);
        }

        #endregion

        #region 取数（Unity 侧，只读）

        /// <summary>SKY_ENCOUNTER_CAP 的帧时间采样窗口（秒）。</summary>
        private const float EncounterSampleSeconds = 3f;

        /// <summary>面板整屏底图的资源名前缀，与 SkyIslandUiArt 私有的 BackgroundPrefix 同值；F3 只探测、不解码。</summary>
        private const string PanelBackgroundAssetPrefix = "skyisland_bg_";

        private bool ValidateSkyIslandChoiceGates(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            if (story == null) { reason = "story_service_missing"; return false; }
            FieldInfo[] fields = typeof(SkyIslandStoryPresentation.Choice).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            string[] names = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++) names[i] = fields[i].Name;
            return JudgeChoiceGates(story.Current, session.ValidationJournalHomeChoices, L10n.IsChinese, names,
                out metrics, out reason);
        }

        /// <summary>
        /// 活敌上限与密集段帧时间。钩子是 <c>SkyIslandEncounters.LivingEnemyCount</c>（经 ValidationLivingEnemies）。
        /// 采样 3 秒：只读 unscaledDeltaTime 与活敌数，不刷怪、不搬人，所以「密集段」取决于跑的时候玩家站在哪儿——
        /// 要验帧时间，就站到三个中继平台连着的那一段再按（清单写明）。
        /// </summary>
        private IEnumerator RunSkyIslandEncounterCap()
        {
            Stopwatch sw = Stopwatch.StartNew();
            SkyIslandSession session = SkyIslandSessionOrNull();
            if (session == null)
            {
                Record("SKY_ENCOUNTER_CAP", "FAIL", 0L, string.Empty, "session_missing");
                yield break;
            }
            string tableMetrics, tableReason;
            bool table = JudgeEncounterTable(SkyIslandContent.CreateFallback(), out tableMetrics, out tableReason);
            int groups = session.ValidationSnapshot().EncounterGroups;
            int livingStart = session.ValidationLivingEnemies;
            int livingPeak = livingStart;
            List<float> frames = new List<float>(256);
            float until = Time.realtimeSinceStartup + EncounterSampleSeconds;
            while (Time.realtimeSinceStartup < until && !ShouldAbort())
            {
                yield return null;
                string gone;
                if (!SkyIslandSessionStillValid(out gone))
                {
                    Record("SKY_ENCOUNTER_CAP", "SKIP", sw.ElapsedMilliseconds, tableMetrics, gone);
                    yield break;
                }
                float ms = Time.unscaledDeltaTime * 1000f;
                if (ms > 0f) frames.Add(ms);
                int living = session.ValidationLivingEnemies;
                if (living > livingPeak) livingPeak = living;
            }
            frames.Sort();
            float p95 = frames.Count > 0 ? frames[Mathf.Clamp(Mathf.CeilToInt(frames.Count * 0.95f) - 1, 0, frames.Count - 1)] : 0f;
            float peak = frames.Count > 0 ? frames[frames.Count - 1] : 0f;
            float threshold = Mathf.Max(50f, _baselineP95Ms * 1.75f);
            string runtimeMetrics, runtimeReason;
            bool runtime = JudgeEncounterRuntime(groups, livingStart, livingPeak, frames.Count, p95, peak, threshold,
                out runtimeMetrics, out runtimeReason);
            string metrics = tableMetrics + "," + runtimeMetrics;
            if (table && runtime) Record("SKY_ENCOUNTER_CAP", "PASS", sw.ElapsedMilliseconds, metrics, string.Empty);
            else Record("SKY_ENCOUNTER_CAP", "FAIL", sw.ElapsedMilliseconds, metrics, (tableReason ?? string.Empty) + (runtimeReason ?? string.Empty));
        }

        private bool ValidateSkyIslandLampsWind(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            if (root == null || story == null) { reason = "world_root_or_story_missing"; return false; }
            SkyIslandFieldcraft field = SkyIslandFieldcraft.Current;
            if (field == null) { reason = "fieldcraft_owner_missing：营火、风晶灯与夜风都没有装配"; return false; }
            Transform world = root.transform;
            return JudgeLampsWind(story.Current, marker => world.Find(marker) != null,
                marker => world.Find("SkyIslandFire_" + marker) != null, field.MissingFireAnchors, field.LightsLit,
                field.WindSample, out metrics, out reason);
        }

        private bool ValidateSkyIslandOfficialNotes(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            if (story == null) { reason = "story_service_missing"; return false; }
            return JudgeOfficialNotesNow(story.Current, "island", out metrics, out reason);
        }

        /// <summary>
        /// 主套件在基地跑的那一次。权威副本从存档里读——与 <c>SkyIslandNoteBridge.Tick</c> 在基地的口径一致
        /// （`SavesSystem.Load` 只读缓存，不初始化、不改写剧情存档）。回基地之后条目还在、状态还对，才算镜像落了盘。
        /// </summary>
        private bool ValidateSkyIslandOfficialNotesAtBase(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            if (LevelManager.Instance == null || !LevelManager.Instance.IsBaseLevel)
                throw new SkyIslandSkipCase("not_in_base", "is_base_level=false");
            SkyIslandStoryData data;
            if (!SavesSystem.KeyExisits(SkyIslandStoryRules.StorageKey)) data = SkyIslandStoryRules.CreateDefault();
            else
            {
                data = SkyIslandStoryCodec.Decode(SavesSystem.Load<string>(SkyIslandStoryRules.StorageKey));
                if (data == null) { reason = "群岛记录无法解码：官方图鉴镜像没有可比对的权威副本"; return false; }
            }
            return JudgeOfficialNotesNow(data, "base", out metrics, out reason);
        }

        private static bool JudgeOfficialNotesNow(SkyIslandStoryData data, string where, out string metrics, out string reason)
        {
            NoteIndex index = NoteIndex.Instance;
            List<Note> notes = index == null ? null : index.Notes;
            if (notes == null)
            {
                metrics = "where=" + where + ",note_index=" + (index != null);
                reason = "官方 NoteIndex 或它的 notes 列表不可用";
                return false;
            }
            List<string> keys = new List<string>(notes.Count);
            Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < notes.Count; i++)
            {
                Note note = notes[i];
                if (note == null || note.key == null) continue;
                keys.Add(note.key);
                if (!note.key.StartsWith(SkyIslandNoteBridge.NoteKeyPrefix, StringComparison.Ordinal) || titles.ContainsKey(note.key))
                    continue;
                string title;
                try { title = note.Title; }
                catch (Exception e) { title = "!" + e.GetType().Name; }
                titles[note.key] = title;
            }
            return JudgeOfficialNotes(data, keys, NoteIndex.GetNoteUnlocked,
                key => { string title; return titles.TryGetValue(key, out title) ? title : null; },
                where, out metrics, out reason);
        }

        private bool ValidateSkyIslandKeepsakes(out string metrics, out string reason)
        {
            return JudgeKeepsakesNow(false, out metrics, out reason);
        }

        private bool ValidateSkyIslandKeepsakesAtBase(out string metrics, out string reason)
        {
            if (LevelManager.Instance == null || !LevelManager.Instance.IsBaseLevel)
                throw new SkyIslandSkipCase("not_in_base", "is_base_level=false");
            return JudgeKeepsakesNow(true, out metrics, out reason);
        }

        private static bool JudgeKeepsakesNow(bool atBase, out string metrics, out string reason)
        {
            string badgeWhere, coreWhere;
            int badge = CountOwnedItems(BossRushItemIds.SkyIslandHomecomingBadge, out badgeWhere);
            int core = CountOwnedItems(BossRushItemIds.SkyIslandWindeaterCore, out coreWhere);
            bool ok = JudgeKeepsakes(badge, core, DescribeKeepsakeIcon(BossRushItemIds.SkyIslandHomecomingBadge),
                DescribeKeepsakeIcon(BossRushItemIds.SkyIslandWindeaterCore), atBase,
                SkyIslandFieldcraft.Current != null, SkyIslandGnats.Current != null,
                GameObject.Find("SkyIslandLanternLight") != null, out metrics, out reason);
            metrics += ",badge_where=" + badgeWhere + ",core_where=" + coreWhere;
            return ok;
        }

        /// <summary>背包（含背包里的容器）与基地仓库里某一件的数量。仓库在当前场景不可用时记 n/a、按 0 算。</summary>
        private static int CountOwnedItems(int typeId, out string where)
        {
            int pack = 0, storage = -1;
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null && main.CharacterItem != null) pack = CountInInventory(main.CharacterItem.Inventory, typeId, 0);
            }
            catch (Exception) { pack = -1; }
            try
            {
                if (PlayerStorage.Inventory != null) storage = CountInInventory(PlayerStorage.Inventory, typeId, 0);
            }
            catch (Exception) { storage = -1; }
            where = "pack:" + (pack < 0 ? "error" : pack.ToString()) + "/storage:" + (storage < 0 ? "n/a" : storage.ToString());
            return Math.Max(0, pack) + Math.Max(0, storage);
        }

        private static int CountInInventory(Inventory inventory, int typeId, int depth)
        {
            if (inventory == null || inventory.Content == null || depth > 4) return 0;
            int total = 0;
            foreach (Item item in inventory.Content)
            {
                if (item == null) continue;
                if (item.TypeID == typeId) total += item.Stackable ? Math.Max(1, item.StackCount) : 1;
                if (item.Inventory != null && !ReferenceEquals(item.Inventory, inventory))
                    total += CountInInventory(item.Inventory, typeId, depth + 1);
            }
            return total;
        }

        /// <summary>
        /// 纪念品图标：prefab 取得到、图标不为空、且不是运行时兜底克隆源（遗种蛋 / 便携安全区 / 尸潮信标 / 尸潮邀请函，
        /// 顺序照 `SkyIslandItems.FindRuntimeFallbackSource`）的图标。只读 prefab，不实例化。
        /// </summary>
        private static string DescribeKeepsakeIcon(int typeId)
        {
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                if (prefab == null) return "prefab_missing";
                Sprite icon = prefab.Icon;
                if (icon == null) return "icon_missing";
                int[] cloneSources =
                {
                    BossRushItemIds.RelicEgg, BossRushItemIds.PortableSafeZoneDevice,
                    BossRushItemIds.ZombieTideBeacon, BossRushItemIds.ZombieTideInvitation
                };
                for (int i = 0; i < cloneSources.Length; i++)
                {
                    Item source = ItemAssetsCollection.GetPrefab(cloneSources[i]);
                    Sprite sourceIcon = source == null ? null : source.Icon;
                    if (sourceIcon == null) continue;
                    if (ReferenceEquals(sourceIcon, icon) || (icon.texture != null && ReferenceEquals(sourceIcon.texture, icon.texture)))
                        return "same_as_clone_source_" + cloneSources[i];
                }
                return "ok";
            }
            catch (Exception e)
            {
                return "error_" + e.GetType().Name;
            }
        }

        private bool ValidateSkyIslandGatherNodes(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            if (root == null) { reason = "world_root_missing"; return false; }
            SkyIslandFieldcraft field = SkyIslandFieldcraft.Current;
            if (field == null) { reason = "fieldcraft_owner_missing：采集点没有装配"; return false; }
            SkyIslandGatherNode[] nodes = SkyIslandFieldcraftRules.Nodes;
            int built = 0;
            List<string> glowMissing = new List<string>();
            List<string> timeMismatch = new List<string>();
            for (int i = 0; i < nodes.Length; i++)
            {
                Transform point = root.transform.Find("SkyIslandGather_" + nodes[i].Id);
                if (point == null) continue;
                built++;
                Transform disc = point.Find("GatherGlowDisc");
                SpriteRenderer glow = disc == null ? null : disc.GetComponent<SpriteRenderer>();
                if (glow == null || glow.sprite == null) glowMissing.Add(nodes[i].Id);
                SkyIslandGatherPoint interact = point.GetComponent<SkyIslandGatherPoint>();
                float expected = SkyIslandFieldcraftRules.InteractSeconds(nodes[i].Kind);
                if (interact == null) timeMismatch.Add(nodes[i].Id + ":no_interactable");
                else if (Mathf.Abs(interact.InteractTime - expected) > 0.01f)
                    timeMismatch.Add(nodes[i].Id + "=" + interact.InteractTime.ToString("F2") + "/" + expected.ToString("F2"));
            }
            return JudgeGatherNodes(nodes.Length, field.GatherPlaced, built, field.GatherHarvested, glowMissing, timeMismatch,
                out metrics, out reason);
        }

        private bool ValidateSkyIslandLetterPigeon(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            if (root == null || story == null) { reason = "world_root_or_story_missing"; return false; }
            SkyIslandLetter letter = session.ValidationPigeonLetter;
            bool named = letter != null && root.transform.Find("SkyIslandPigeon_" + letter.Id) != null;
            return JudgeLetterPigeon(story.Current, letter == null ? null : letter.Id, session.ValidationPigeonPlaced,
                session.ValidationPigeonPresent, named, story.CanWrite, out metrics, out reason);
        }

        /// <summary>
        /// 云蚋运行时。白天记 SKIP；夜里先查「这一趟到底会不会有蚋」——精灵表不可用或开枪补丁没挂上时
        /// 场上永远是 0 只，若先按「没有蚋」记 SKIP，这两个缺陷就永远不会红，所以它们排在 SKIP 之前；
        /// 之后场上没有蚋才记 SKIP。有蚋时：活蚋 ≤6；每只与主角移动碰撞体的接触都已屏蔽（否则会把人顶开或卡住）。
        /// </summary>
        private bool ValidateSkyIslandGnatRuntime(out string metrics, out string reason)
        {
            reason = null;
            double hours = SkyIslandLighting.ClockHours();
            bool night = SkyIslandNight.IsNight(hours);
            SkyIslandFieldcraft field = SkyIslandFieldcraft.Current;
            SkyIslandGnats swarm = field == null ? null : field.Gnats;
            string patchDetail;
            bool patched = GnatProjectilePatchInstalled(out patchDetail);
            int alive = swarm == null ? 0 : swarm.Alive;
            metrics = "night=" + night + ",hours=" + hours.ToString("F2") + ",fieldcraft=" + (field != null)
                + ",swarm=" + (swarm != null) + ",usable=" + (swarm != null && swarm.Usable) + ",alive=" + alive
                + "/" + SkyIslandMosquitoRules.MaxAlive + ",projectile_patch=" + patchDetail;
            if (!night) throw new SkyIslandSkipCase("daytime", metrics);
            if (field == null) { reason = "fieldcraft_owner_missing：云蚋挂在它下面"; return false; }
            if (swarm == null) { reason = "云蚋装配失败（SkyIslandFieldcraft.Gnats 为空）：这一趟不会刷蚋"; return false; }
            if (!swarm.Usable) { reason = "云蚋精灵表不可用：这一趟不会刷蚋"; return false; }
            if (!patched) { reason = "开枪补丁没有挂在 Projectile.Init(ProjectileContext) 上：云蚋躲不了子弹"; return false; }
            if (alive == 0) throw new SkyIslandSkipCase("no_gnats_on_field", metrics);
            if (alive > SkyIslandMosquitoRules.MaxAlive) { reason = "活蚋超过上限"; return false; }
            int inspected;
            bool colliderFound;
            int notIgnored = swarm.CountPlayerContactsNotIgnored(out inspected, out colliderFound);
            metrics += ",inspected=" + inspected + ",player_collider=" + colliderFound + ",contact_not_ignored=" + notIgnored;
            if (!colliderFound) { reason = "取不到主角的移动碰撞体：接触屏蔽无从核对"; return false; }
            if (notIgnored > 0) { reason = "有云蚋与主角的接触没有屏蔽：会把主角顶开或卡住"; return false; }
            return true;
        }

        /// <summary>
        /// 噬风·回响的开启状态与本趟计数。取数全部只读：会话的「能不能引」（数背包，不预留、不开战）、观测面上的本趟计数与清场、
        /// 遭遇 owner 的「打响过没有」，外加背包顶层的噬风之核与晴岚风晶件数（与会话同一口径）。
        /// </summary>
        private bool ValidateSkyIslandStormEcho(out string metrics, out string reason)
        {
            reason = null;
            metrics = string.Empty;
            SkyIslandSession session = SkyIslandSessionOrNull();
            SkyIslandStoryService story = session == null ? null : session.ValidationStory;
            if (story == null) { reason = "story_service_missing"; return false; }
            string blocker;
            bool canSummon = session.CanSummonStormEcho(out blocker);
            bool core = ItemFactory.GetItemCountInInventory(BossRushItemIds.SkyIslandWindeaterCore) > 0;
            int crystals = ItemFactory.GetItemCountInInventory(BossRushItemIds.SkyIslandQinglanWindcrystal);
            return JudgeStormEcho(story.Current, session.ValidationStormEchoStarts,
                session.HasStoryChallengeStarted(SkyIslandStormEchoRules.EncounterId), session.ValidationStormEchoCleared,
                core, crystals, canSummon, blocker, out metrics, out reason);
        }

        /// <summary>
        /// SKY_PERF_* 开窗（SamplePerformance 在采样循环之前调用）：岛内套件开始录分项计时，主套件什么都不做——
        /// 主套件没人打分段标记，开了录制会被记成 profile_no_frames。正式构建里 BeginRecording 这句调用不存在。
        /// 岛内模式门写在这里而不在宿主 partial 里：F3GameplayValidationRunner.cs 计入 ModBehaviourPartialBudgetGuard 的行数预算。
        /// </summary>
        private void BeginSkyIslandFrameProfile()
        {
            if (!_skyIslandMode) return;
            SkyIslandFrameProfile.BeginRecording();
        }

        /// <summary>SKY_PERF_* 关窗（采样循环之后）：岛内套件把分项计时拼到 metrics 末尾并返回不合格原因；主套件原样返回 null。</summary>
        private string AppendSkyIslandFrameProfile(ref string metrics)
        {
            if (!_skyIslandMode) return null;
            string reason;
            metrics += SkyIslandFrameProfileMetrics(out reason);
            return reason;
        }

        /// <summary>
        /// SKY_PERF_* 关窗：交出分项计时，再在采样窗口之外只读地数一次场景里的灯与岛上的 renderer（不算进采样帧）。
        /// 返回拼到 metrics 末尾的一段；分项计时本身不合格时给出 reason。
        /// </summary>
        private string SkyIslandFrameProfileMetrics(out string reason)
        {
            List<float[]> frames;
            bool recorded = SkyIslandFrameProfile.TryTakeRecording(out frames);
            int lightsActive = 0, lightsShadowed = 0;
            foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (light == null || !light.enabled) continue;
                lightsActive++;
                if (light.shadows != LightShadows.None) lightsShadowed++;
            }
            int visible = 0, total = 0;
            SkyIslandSession session = SkyIslandSessionOrNull();
            GameObject root = session == null ? null : session.ValidationWorldRoot;
            if (root != null)
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (renderer == null) continue;
                    total++;
                    if (renderer.isVisible) visible++;
                }
            }
            string metrics;
            JudgeFrameProfile(SkyIslandFrameProfile.SegmentNames, recorded ? frames : null, lightsActive, lightsShadowed,
                visible, total, out metrics, out reason);
            return "," + metrics;
        }

        /// <summary>开枪补丁是否真的挂在官方 Projectile.Init(ProjectileContext) 的后缀上。只读 Harmony 的补丁表。</summary>
        private static bool GnatProjectilePatchInstalled(out string detail)
        {
            try
            {
                MethodBase original = HarmonyLib.AccessTools.Method(typeof(Projectile), "Init", new[] { typeof(ProjectileContext) });
                if (original == null) { detail = "target_missing"; return false; }
                HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(original);
                if (info == null) { detail = "no_patches"; return false; }
                foreach (HarmonyLib.Patch patch in info.Postfixes)
                {
                    if (patch != null && patch.PatchMethod != null && patch.PatchMethod.DeclaringType == typeof(SkyIslandGnatProjectilePatch))
                    {
                        detail = "postfix_owner_" + patch.owner;
                        return true;
                    }
                }
                detail = "postfixes_" + info.Postfixes.Count + "_without_sky_island";
                return false;
            }
            catch (Exception e)
            {
                detail = "error_" + e.GetType().Name;
                return false;
            }
        }

        #endregion
    }
}
