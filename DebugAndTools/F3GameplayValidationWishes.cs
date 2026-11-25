// ============================================================================
// F3GameplayValidationWishes.cs - 2026-11-24 玩家许愿七项与同日尸潮 / 龙裔 / 叮当修复的实机核对
// ============================================================================
// 整份只在 Dev 构建里编译。结论输出成文字，AI 读报告不看截图；不写存档。
// 内存探针（假崽、假好感档、手写 JSON）用完即丢；钟罩探针插进空图腾槽、测完拔下销毁；
// 叮当只演一遍「关店 → 停留 → 召唤」，出征令那一局结束后收掉本局状态。
//
// 基地阶段（RunSuite 2/7，排在 2026-10-18 审查修复之后）：
//   INK_SKIN_ASSETS                    水墨套件八张程序贴图都生成、十一声界面音效已部署、官方音效入口在
//   ARENA_EXPEDITION_TUNING            战帖四档倍率与休整、战规 3/0/5/8 条（轻松全是宽限，借用的词条都在池里、竞技场可用）、
//                                      急行军 / 盲战的档位、征程波数、通关箱件数与品质下限、噩梦通关成就已登记
//   MUTATOR_SWARM_WEIGHT               群鼠权重 0.5、与巨灵兵同一互斥组；生产加权抽取 20 万次，群鼠约为权重 1 词条的一半
//   WISH_FOUNTAIN_RULES                4 字下限（界面计数与提交同口径）、12 宿都对得上奖励类别、星灯清洗压重复 / 拦链接 / 去重
//   PETNEST_SOUL_FEED_RULES            献魂花费 = 缺的经验 × 2、满级 0、当天献过的判定、日戳存档往返、旧档缺字段读成 0
//   AFFINITY_GIFT_JOURNAL_ROUNDTRIP    心意簿两字段存档往返、旧档缺字段读成空、损坏字段整条拒收
//   NPC_NAMEPLATE_SETTING              名牌：恒为走近显示（无配置项），场上只有一份画布；走近 8 米亮、走出 10 米灭；名牌是官方血条那行名字（同字体同字号），别的图形不画
//   MODCONFIG_OPTION_LABELS            ModConfig 下拉项标题：官方表里 Options_<配置键> 就是当前语言那句；切到另一种语言跟着变，切回原语言
//                                      （用例体在 F3GameplayValidationModConfigLabels.cs）
//   GOBLIN_POST_DIALOGUE_STAY          叮当关店后至少站 150 秒（过 10 秒仍在原地），召唤立刻打断停留
// 竞技场阶段（5/7 模式深度流程，排在丧尸奖励修复之后）：
//   ZOMBIE_CROWD_NAVIGATION            尸潮：寻路节流补丁挂上、每只丧尸带碰撞行为回调、12 秒采样无持续叠罗汉、远处丧尸重算频率受限
//   ARENA_EXPEDITION_ORDER             出征令「单挑 · 困难 · 短征」开一局：队列按短征定长、末波是压轴、Boss 生命 ×1.3、战帖角标在、
//                                      困难战规借用的三条词条已作为「战帖」词条上身
//   BOSS_MAP_BEACON                    同一局：烽烟圈挂上、半径只取 35 / 20 / 10、40 米内已显形、显形后钉在 Boss 身上
//   EQ_SILENCE_BELL_WARD               同一局：钟罩插进空图腾槽后驱动与常驻金钟罩出现（钟在主角脚下），敌方 20 点先扣护罩；拔下后驱动连钟撤掉、余量不补满
//   ARENA_EXPEDITION_CLEANUP           清场后烽烟圈全收、战帖角标不再显示
//   DRAGON_DESCENDANT_SPAWN_PLACEMENT  龙裔落点：玩家脚下与贴墙处各取 16 次，都在 5 米圈内、同层、与起点之间没有墙、可达
// 「只跑本次改动」专项（F3GameplayValidationWishFocus.cs）只编排这一组用例，加用例时同步那边的表。
// ============================================================================

#if BOSSRUSH_DEV
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using BossRush.Constants;
using BossRush.Utils;
using Duckov.MiniMaps;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private const string WishBeaconObjectName = "BossRush_MapBeacon";
        private const string WishExpeditionHudName = "BossRush_ArenaExpeditionHud";
        private const string WishBellDriverName = "SilenceBell_Runtime";

        // ------------------------------------------------------------------
        // 基地阶段
        // ------------------------------------------------------------------

        /// <summary>基地阶段：七项许愿与叮当停留。除叮当那条要等 11 秒外都是同步核对。</summary>
        private IEnumerator RunWishBaseCases()
        {
            RunSyncCase("INK_SKIN_ASSETS", ValidateInkSkinAssets);
            RunSyncCase("ARENA_EXPEDITION_TUNING", ValidateArenaExpeditionTuning);
            RunSyncCase("MUTATOR_SWARM_WEIGHT", ValidateMutatorSwarmWeight);
            RunSyncCase("WISH_FOUNTAIN_RULES", ValidateWishFountainRules);
            RunSyncCase("PETNEST_SOUL_FEED_RULES", ValidatePetNestSoulFeedRules);
            RunSyncCase("AFFINITY_GIFT_JOURNAL_ROUNDTRIP", ValidateAffinityGiftJournalRoundTrip);
            RunSyncCase("NPC_NAMEPLATE_SETTING", ValidateNpcNameplateSetting);
            RunSyncCase("MODCONFIG_OPTION_LABELS", ValidateModConfigOptionLabels);
            yield return RunGoblinPostDialogueStay();
        }

        private static readonly string[] WishInkSfxFiles =
        {
            BossRushInkSkin.SfxSealStamp, BossRushInkSkin.SfxPaperTurn, BossRushInkSkin.SfxBrushSweep,
            BossRushInkSkin.SfxTempleBell, BossRushInkSkin.SfxChimeStone, BossRushInkSkin.SfxWarDrum,
            BossRushInkSkin.SfxBellHum, BossRushInkSkin.SfxBellShatter, BossRushInkSkin.SfxBellDing,
            BossRushInkSkin.SfxEmberFlare, BossRushInkSkin.SfxSoulRise,
        };

        private static bool ValidateInkSkinAssets(out string metrics, out string reason)
        {
            reason = null;
            Sprite[] sprites =
            {
                BossRushInkSkin.GetSurfaceSprite(), BossRushInkSkin.GetFiberSprite(), BossRushInkSkin.GetFrameSprite(),
                BossRushInkSkin.GetBrushSprite(), BossRushInkSkin.GetSealSprite(), BossRushInkSkin.GetBlotSprite(),
                BossRushInkSkin.GetRingSprite(), BossRushInkSkin.GetGlowSprite(),
            };
            int spritesOk = 0;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null && sprites[i].texture != null && sprites[i].rect.width > 0f) spritesOk++;
            }
            string folder = Path.Combine(Path.Combine(Path.Combine(ModBehaviour.GetModPath(), "Assets"), "Sounds"), BossRushInkSkin.SoundFolder);
            List<string> missing = new List<string>();
            for (int i = 0; i < WishInkSfxFiles.Length; i++)
            {
                if (!File.Exists(Path.Combine(folder, WishInkSfxFiles[i]))) missing.Add(WishInkSfxFiles[i]);
            }
            bool officialAudio = BossRushOfficialAudio.HasPostCustomSfx;
            metrics = "sprites=" + spritesOk + "/" + sprites.Length
                + ",sfx=" + (WishInkSfxFiles.Length - missing.Count) + "/" + WishInkSfxFiles.Length
                + ",post_custom_sfx=" + officialAudio
                + (missing.Count > 0 ? ",missing=" + string.Join("+", missing.ToArray()) : string.Empty);
            if (spritesOk != sprites.Length) reason = "ink_sprite_generation_failed";
            else if (missing.Count > 0) reason = "ink_sfx_not_deployed";
            else if (!officialAudio) reason = "official_custom_sfx_entry_missing";
            return reason == null;
        }

        private bool ValidateArenaExpeditionTuning(out string metrics, out string reason)
        {
            reason = null;
            // 2026-11-25 难度由战规叠出来；噩梦按 owner「大胆加加」再抬到生命 ×2.5、伤害 ×1.75、出手快五成、无间奖金 ×2。
            // 休整：轻松 ×1.5，困难 / 噩梦急行军 ×0.5。
            float[] hp = { 0.6f, 1f, 1.3f, 2.5f };
            float[] damage = { 0.7f, 1f, 1.15f, 1.75f };
            float[] reaction = { 1f, 1f, 1.15f, 1.5f };
            float[] hellCash = { 0.5f, 1f, 1.25f, 2f };
            float[] rest = { 1.5f, 1f, 0.5f, 0.5f };
            int[] ruleCounts = { 3, 0, 5, 8 };
            List<string> wrong = new List<string>();
            int borrowed = 0;
            for (int i = 0; i < 4; i++)
            {
                ArenaExpeditionTier tier = (ArenaExpeditionTier)i;
                if (!WishNear(ArenaExpeditionTuning.HealthMultiplier(tier), hp[i])) wrong.Add("hp_" + tier);
                if (!WishNear(ArenaExpeditionTuning.DamageMultiplier(tier), damage[i])) wrong.Add("dmg_" + tier);
                if (!WishNear(ArenaExpeditionTuning.ReactionDivisor(tier), reaction[i])) wrong.Add("react_" + tier);
                if (!WishNear(ArenaExpeditionTuning.HellCashMultiplier(tier), hellCash[i])) wrong.Add("cash_" + tier);
                if (ArenaExpeditionTuning.ScalesBosses(tier) == (tier == ArenaExpeditionTier.Standard)) wrong.Add("scales_" + tier);
                if (!WishNear(ArenaExpeditionTuning.RestMultiplier(tier), rest[i])) wrong.Add("rest_" + tier);
                if (ArenaExpeditionTuning.KeepsMilestoneRest(tier) != (tier <= ArenaExpeditionTier.Standard)) wrong.Add("milestone_" + tier);
                if (ArenaExpeditionTuning.HidesMapBeacons(tier) != (tier == ArenaExpeditionTier.Nightmare)) wrong.Add("blind_" + tier);
                IList<ArenaExpeditionRule> rules = ArenaExpeditionTuning.RulesFor(tier);
                if (rules.Count != ruleCounts[i]) wrong.Add("rules_" + tier + "=" + rules.Count);
                for (int r = 0; r < rules.Count; r++)
                {
                    ArenaExpeditionRule rule = rules[r];
                    if (rule == null || string.IsNullOrEmpty(rule.Name) || string.IsNullOrEmpty(rule.Line)) { wrong.Add("rule_text_" + tier + r); continue; }
                    if (rule.Grace != (tier == ArenaExpeditionTier.Easy)) wrong.Add("grace_" + tier + r);
                    if (string.IsNullOrEmpty(rule.MutatorId)) continue;
                    borrowed++;
                    MutatorDefinition definition = rule.Definition();
                    if (definition == null || !definition.AllowsMode("BossRush") || !definition.AllowsMode("InfiniteHell")) wrong.Add("mutator_" + rule.MutatorId);
                }
                if ((ArenaExpeditionTuning.ForcedMutatorIds(tier) == null) != (tier == ArenaExpeditionTier.Standard)) wrong.Add("forced_" + tier);
            }
            if (ArenaExpeditionTuning.WavesFor(ArenaExpeditionLength.Short, 40) != 12
                || ArenaExpeditionTuning.WavesFor(ArenaExpeditionLength.Long, 40) != 24
                || ArenaExpeditionTuning.WavesFor(ArenaExpeditionLength.Full, 40) != 40
                || ArenaExpeditionTuning.WavesFor(ArenaExpeditionLength.Short, 8) != 8) wrong.Add("waves");
            // 通关箱：ceil(10 × √(12/40)) = 6；轻松再减半 = 3；噩梦全征 10 × 1.5 = 15；轻松至少 1 件。
            int standardShort = ArenaExpeditionTuning.VictoryChestCount(10, ArenaExpeditionTier.Standard, 12, 40);
            int easyShort = ArenaExpeditionTuning.VictoryChestCount(10, ArenaExpeditionTier.Easy, 12, 40);
            int nightmareFull = ArenaExpeditionTuning.VictoryChestCount(10, ArenaExpeditionTier.Nightmare, 40, 40);
            int easyFloor = ArenaExpeditionTuning.VictoryChestCount(1, ArenaExpeditionTier.Easy, 40, 40);
            if (standardShort != 6 || easyShort != 3 || nightmareFull != 15 || easyFloor != 1) wrong.Add("chests");
            if (ArenaExpeditionTuning.VictoryQualityMin(ArenaExpeditionTier.Hard, 5) != 6
                || ArenaExpeditionTuning.VictoryQualityMin(ArenaExpeditionTier.Nightmare, 5) != 6
                || ArenaExpeditionTuning.VictoryQualityMin(ArenaExpeditionTier.Standard, 5) != 5
                || ArenaExpeditionTuning.VictoryQualityMin(ArenaExpeditionTier.Easy, 5) != 5) wrong.Add("quality_floor");
            bool achievement = BossRushAchievementManager.GetAchievement(ArenaExpeditionTuning.NightmareClearAchievementId) != null;
            if (!achievement) wrong.Add("nightmare_achievement_missing");

            List<EnemyPresetInfo> pool = null;
            try { pool = _host.GetFilteredEnemyPresets(); }
            catch (Exception e) { wrong.Add("pool_read_threw:" + e.GetType().Name); }
            int poolCount = pool != null ? pool.Count : 0;
            metrics = "chests_std_short=" + standardShort + ",easy_short=" + easyShort + ",nightmare_full=" + nightmareFull
                + ",easy_floor=" + easyFloor + ",achievement=" + achievement + ",borrowed_mutators=" + borrowed + ",enabled_pool=" + poolCount
                + ",short_waves=" + ArenaExpeditionTuning.WavesFor(ArenaExpeditionLength.Short, poolCount)
                + ",long_waves=" + ArenaExpeditionTuning.WavesFor(ArenaExpeditionLength.Long, poolCount)
                + (wrong.Count > 0 ? ",wrong=" + string.Join("+", wrong.ToArray()) : string.Empty);
            if (wrong.Count > 0) reason = "expedition_tuning_mismatch";
            return reason == null;
        }

        private static bool ValidateMutatorSwarmWeight(out string metrics, out string reason)
        {
            reason = null;
            MutatorDefinition swarm = null;
            MutatorDefinition giant = null;
            List<MutatorDefinition> pool = new List<MutatorDefinition>();
            for (int i = 0; i < MutatorPool.All.Length; i++)
            {
                MutatorDefinition definition = MutatorPool.All[i];
                if (definition == null) continue;
                pool.Add(definition);
                if (definition.Id == "enemy_swarm") swarm = definition;
                else if (definition.Id == "enemy_giant") giant = definition;
            }
            if (swarm == null || giant == null)
            {
                metrics = "pool=" + pool.Count;
                reason = "swarm_or_giant_missing";
                return false;
            }

            // 生产加权抽取跑 20 万次（固定种子）：群鼠频率 / 权重 1 词条的平均频率应接近 0.5。
            int[] hits = new int[pool.Count];
            System.Random rng = new System.Random(20261124);
            const int draws = 200000;
            for (int n = 0; n < draws; n++) hits[MutatorManager.PickWeightedIndex(pool, rng)]++;
            double unitSum = 0d;
            int unitCount = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if (Math.Abs(pool[i].Weight - 1f) < 0.001f)
                {
                    unitSum += hits[i];
                    unitCount++;
                }
            }
            double unitMean = unitCount > 0 ? unitSum / unitCount : 0d;
            int swarmHits = hits[pool.IndexOf(swarm)];
            double ratio = unitMean > 0d ? swarmHits / unitMean : 0d;
            bool data = WishNear(swarm.Weight, 0.5f) && swarm.ExclusiveGroup == "enemy_size" && giant.ExclusiveGroup == "enemy_size";
            metrics = "pool=" + pool.Count + ",swarm_weight=" + swarm.Weight.ToString("0.##", CultureInfo.InvariantCulture)
                + ",swarm_group=" + (swarm.ExclusiveGroup ?? "null") + ",giant_group=" + (giant.ExclusiveGroup ?? "null")
                + ",swarm_hits=" + swarmHits + ",unit_mean=" + unitMean.ToString("0", CultureInfo.InvariantCulture)
                + ",ratio=" + ratio.ToString("0.000", CultureInfo.InvariantCulture);
            if (!data) reason = "swarm_weight_or_group_wrong";
            else if (ratio < 0.4d || ratio > 0.6d) reason = "weighted_pick_ratio_off";
            return reason == null;
        }

        private static bool ValidateWishFountainRules(out string metrics, out string reason)
        {
            reason = null;
            List<string> wrong = new List<string>();
            string error;
            bool fourAccepted = WishFountainService.ValidateWishText("愿你安好", out error);
            bool threeRejected = !WishFountainService.ValidateWishText("愿安好", out error);
            int uiCount = WishFountainService.CountWishChars("  愿你 安好！ ");
            int minChars = WishFountainService.MIN_CHARS;
            if (minChars != 4) wrong.Add("min_chars_" + minChars);
            if (!fourAccepted) wrong.Add("four_chars_rejected");
            if (!threeRejected) wrong.Add("three_chars_accepted");
            if (uiCount != 4) wrong.Add("ui_count_" + uiCount);

            // 12 宿：id 不重复、Find 找得回来、都是许愿奖励类别表里的类别（类别表是私有的，只读反射）。
            HashSet<string> categoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            FieldInfo categoriesField = typeof(WishFountainService).GetField("WishRewardCategories", BindingFlags.NonPublic | BindingFlags.Static);
            Array categories = categoriesField != null ? categoriesField.GetValue(null) as Array : null;
            if (categories == null) wrong.Add("category_table_unreadable");
            else
            {
                foreach (object category in categories)
                {
                    FieldInfo idField = category != null ? category.GetType().GetField("categoryId") : null;
                    string id = idField != null ? idField.GetValue(category) as string : null;
                    if (!string.IsNullOrEmpty(id)) categoryIds.Add(id);
                }
            }
            HashSet<string> constellationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int unmapped = 0;
            for (int i = 0; i < WishConstellations.All.Length; i++)
            {
                WishConstellation constellation = WishConstellations.All[i];
                if (constellation == null || string.IsNullOrEmpty(constellation.Id)) { unmapped++; continue; }
                constellationIds.Add(constellation.Id);
                if (WishConstellations.Find(constellation.Id) != constellation || !categoryIds.Contains(constellation.Id)) unmapped++;
            }
            if (WishConstellations.All.Length != 12 || constellationIds.Count != 12) wrong.Add("constellations_" + constellationIds.Count);
            if (unmapped > 0) wrong.Add("unmapped_" + unmapped);

            // 星灯清洗：叠字压成两份、带链接的拦下、规范化后相同的只留第一条、不足 4 字的不上墙。
            List<string> raw = new List<string>
            {
                "更新更新更新更新更新",
                "看这里 https://abc.example 有惊喜",
                "愿你安好",
                "愿你 安好",
                "好好",
                "希望多出新地图",
            };
            List<string> lanterns = WishFountainService.FilterLanternWishes(raw);
            string joined = string.Join("|", lanterns.ToArray());
            if (joined != "更新更新|愿你安好|希望多出新地图") wrong.Add("lantern_filter");
            metrics = "min_chars=" + minChars + ",ui_count=" + uiCount + ",constellations=" + constellationIds.Count
                + ",categories=" + categoryIds.Count + ",lanterns=" + joined
                + (wrong.Count > 0 ? ",wrong=" + string.Join("+", wrong.ToArray()) : string.Empty);
            if (wrong.Count > 0) reason = "wish_rule_mismatch";
            return reason == null;
        }

        private static bool ValidatePetNestSoulFeedRules(out string metrics, out string reason)
        {
            reason = null;
            List<string> wrong = new List<string>();
            PetNestPetRecord fresh = new PetNestPetRecord { id = "f3_soulfeed_fresh", lineageKey = "f3_probe", level = 1, exp = 0 };
            PetNestPetRecord almost = new PetNestPetRecord { id = "f3_soulfeed_almost", lineageKey = "f3_probe", level = 3, exp = PetNestTuning.PetExpPerLevel - 1 };
            PetNestPetRecord capped = new PetNestPetRecord { id = "f3_soulfeed_capped", lineageKey = "f3_probe", level = PetNestTuning.PetMaxLevel, exp = 0 };
            int costFresh = PetNestService.SoulFeedCost(fresh);
            int costAlmost = PetNestService.SoulFeedCost(almost);
            int costCapped = PetNestService.SoulFeedCost(capped);
            int soulsPerExp = PetNestTuning.SoulFeedSoulsPerExp;
            if (soulsPerExp != 2) wrong.Add("souls_per_exp_" + soulsPerExp);
            if (costFresh != PetNestTuning.PetExpPerLevel * PetNestTuning.SoulFeedSoulsPerExp) wrong.Add("cost_fresh_" + costFresh);
            if (costAlmost != PetNestTuning.SoulFeedSoulsPerExp) wrong.Add("cost_almost_" + costAlmost);
            if (costCapped != 0) wrong.Add("cost_capped_" + costCapped);

            long today = PetNestService.SoulFeedDayStamp();
            bool fedToday = false;
            bool fedYesterdayFree = false;
            if (today <= 0L) wrong.Add("game_clock_missing");
            else
            {
                fresh.lastSoulFeedDay = today;
                fedToday = PetNestService.SoulFedToday(fresh);
                fresh.lastSoulFeedDay = today - 1L;
                fedYesterdayFree = !PetNestService.SoulFedToday(fresh);
                if (!fedToday) wrong.Add("fed_today_not_detected");
                if (!fedYesterdayFree) wrong.Add("yesterday_blocks_today");
            }

            // 存档往返：日戳写出去能读回来；旧档没有这个字段读成 0（从没献过）。
            long roundTrip = -1L;
            long oldDay = -1L;
            PetNestNestData nest = PetNestCodec.CreateDefaultNest();
            if (nest.pets == null) nest.pets = new List<PetNestPetRecord>();
            PetNestPetRecord saved = new PetNestPetRecord { id = "f3_soulfeed_saved", lineageKey = "f3_probe", level = 2, exp = 40, lastSoulFeedDay = 42L };
            saved.Normalize();
            nest.pets.Add(saved);
            PetNestNestData back = PetNestCodec.DecodeNest(BossRushJsonParser.ParseOrNull(PetNestCodec.EncodeNest(nest)));
            if (back != null && back.pets != null && back.pets.Count == 1) roundTrip = back.pets[0].lastSoulFeedDay;
            PetNestNestData old = PetNestCodec.DecodeNest(BossRushJsonParser.ParseOrNull(
                "{\"pets\":[{\"id\":\"f3_old_pet\",\"lineageKey\":\"f3_probe\",\"level\":3,\"exp\":5}],\"soulLedger\":[]}"));
            if (old != null && old.pets != null && old.pets.Count == 1) oldDay = old.pets[0].lastSoulFeedDay;
            if (roundTrip != 42L) wrong.Add("roundtrip_" + roundTrip);
            if (oldDay != 0L) wrong.Add("old_save_" + oldDay);
            metrics = "cost_fresh=" + costFresh + ",cost_almost=" + costAlmost + ",cost_capped=" + costCapped
                + ",today=" + today + ",fed_today=" + fedToday + ",yesterday_free=" + fedYesterdayFree
                + ",roundtrip=" + roundTrip + ",old_save=" + oldDay
                + (wrong.Count > 0 ? ",wrong=" + string.Join("+", wrong.ToArray()) : string.Empty);
            if (wrong.Count > 0) reason = "soul_feed_rule_mismatch";
            return reason == null;
        }

        private static bool ValidateAffinityGiftJournalRoundTrip(out string metrics, out string reason)
        {
            reason = null;
            List<string> wrong = new List<string>();
            AffinityData probe = new AffinityData("f3_journal_probe");
            probe.knownLikes = AffinityManager.GiftItemToken(BossRushItemIds.SilenceBell) + "," + AffinityManager.GiftTagToken("Food");
            probe.knownDislikes = AffinityManager.GiftItemToken(1);
            Dictionary<string, AffinityData> map = new Dictionary<string, AffinityData>();
            map[probe.npcId] = probe;
            Dictionary<string, AffinityData> back = new Dictionary<string, AffinityData>();
            bool parsed = AffinityJsonSerializer.Deserialize(AffinityJsonSerializer.Serialize(map), back);
            AffinityData read;
            if (!parsed || !back.TryGetValue(probe.npcId, out read) || read == null) wrong.Add("roundtrip_parse");
            else if (read.knownLikes != probe.knownLikes || read.knownDislikes != probe.knownDislikes) wrong.Add("roundtrip_value");

            Dictionary<string, AffinityData> old = new Dictionary<string, AffinityData>();
            AffinityData oldRead;
            bool oldParsed = AffinityJsonSerializer.Deserialize("{\"npcDataList\":[{\"npcId\":\"f3_old_npc\",\"points\":12}]}", old);
            if (!oldParsed || !old.TryGetValue("f3_old_npc", out oldRead) || oldRead == null) wrong.Add("old_save_parse");
            else if (oldRead.knownLikes != string.Empty || oldRead.knownDislikes != string.Empty || oldRead.points != 12) wrong.Add("old_save_value");

            bool corruptRejected = !AffinityJsonSerializer.Deserialize("{\"npcDataList\":[{\"npcId\":\"f3_bad_npc\",\"knownLikes\":5}]}",
                new Dictionary<string, AffinityData>());
            if (!corruptRejected) wrong.Add("corrupt_field_accepted");

            // 实机读数：好感页签每位 NPC 心意簿已摸清 / 未摸清的条数（只读）。
            int npcs = 0;
            int known = 0;
            int unknown = 0;
            List<AffinityTabEntry> entries = AffinityTabData.Build();
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                if (entries[i] == null) continue;
                npcs++;
                known += AffinityManager.GetKnownGiftPreferences(entries[i].NpcId, true).Count
                    + AffinityManager.GetKnownGiftPreferences(entries[i].NpcId, false).Count;
                unknown += Math.Max(0, entries[i].UnknownLikes);
            }
            metrics = "roundtrip=" + parsed + ",old_save=" + oldParsed + ",corrupt_rejected=" + corruptRejected
                + ",tab_npcs=" + npcs + ",journal_known=" + known + ",likes_unknown=" + unknown
                + (wrong.Count > 0 ? ",wrong=" + string.Join("+", wrong.ToArray()) : string.Empty);
            if (wrong.Count > 0) reason = "gift_journal_mismatch";
            return reason == null;
        }

        /// <summary>
        /// 名牌（owner 2026-11-25 定案：没有配置项，恒为走近显示，只显示名字）：基地里场上恰好一份名牌画布；
        /// 距离回滞对；已经亮着的名牌就是官方血条预制体那行名字（同字体同字号，owner 2026-11-25「样式和原版不一致」），名字以外的图形都不画。
        /// </summary>
        private static bool ValidateNpcNameplateSetting(out string metrics, out string reason)
        {
            reason = null;
            FriendlyNpcNameplates[] roots = UnityEngine.Object.FindObjectsOfType<FriendlyNpcNameplates>();
            bool hysteresis = FriendlyNpcNameplates.NextShown(false, 8f) && !FriendlyNpcNameplates.NextShown(false, 8.5f)
                && FriendlyNpcNameplates.NextShown(true, 9.5f) && !FriendlyNpcNameplates.NextShown(true, 10f);
            int plates, stray;
            bool officialStyle;
            FriendlyNpcNameplates.DescribeForValidation(out plates, out stray, out officialStyle);
            List<NpcNameplateTarget> targets = new List<NpcNameplateTarget>();
            NPCNameTagHelper.CopyNameplateTargets(targets);
            metrics = "canvases=" + roots.Length + ",targets=" + targets.Count + ",plates=" + plates + ",stray_graphics=" + stray
                + ",official_style=" + officialStyle + ",hysteresis=" + hysteresis;
            if (roots.Length != 1) reason = "nameplate_canvas_count_wrong";
            else if (!hysteresis) reason = "nameplate_distance_rule_wrong";
            else if (stray > 0) reason = "nameplate_has_graphics_besides_name";
            else if (!officialStyle) reason = "nameplate_not_official_name_style";
            return reason == null;
        }

        /// <summary>
        /// 叮当关店后的停留：照商店 / 重铸关闭的真实调用（停留 10 秒、带告别）演一遍，等过 10 秒看他还站着
        /// （至少 150 秒的下限生效），再模拟召唤看停留被立刻打断、开始跑向玩家。
        /// </summary>
        private IEnumerator RunGoblinPostDialogueStay()
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (ShouldAbort())
            {
                Record("GOBLIN_POST_DIALOGUE_STAY", "SKIP", 0L, string.Empty, DescribeAbortReason());
                yield break;
            }
            GoblinNPCController goblin;
            string skip;
            string failure = BeginGoblinStayProbe(out goblin, out skip);
            if (failure != null || skip != null)
            {
                Record("GOBLIN_POST_DIALOGUE_STAY", failure != null ? "FAIL" : "SKIP", sw.ElapsedMilliseconds, string.Empty, failure ?? skip);
                yield break;
            }
            yield return WaitSeconds(11.5f);
            if (ShouldAbort())
            {
                Record("GOBLIN_POST_DIALOGUE_STAY", "SKIP", sw.ElapsedMilliseconds, string.Empty, DescribeAbortReason());
                yield break;
            }
            string metrics;
            failure = FinishGoblinStayProbe(goblin, out metrics);
            Record("GOBLIN_POST_DIALOGUE_STAY", failure == null ? "PASS" : "FAIL", sw.ElapsedMilliseconds, metrics, failure);
        }

        private static readonly BindingFlags WishPrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private string BeginGoblinStayProbe(out GoblinNPCController goblin, out string skip)
        {
            skip = null;
            goblin = null;
            Type type = typeof(GoblinNPCController);
            FieldInfo storyField = type.GetField("isInStoryDialogue", WishPrivateInstance);
            FieldInfo stayField = type.GetField("currentStayCoroutine", WishPrivateInstance);
            FieldInfo dialogueField = type.GetField("isInDialogue", WishPrivateInstance);
            FieldInfo runField = type.GetField("isRunningToPlayer", WishPrivateInstance);
            FieldInfo playerField = type.GetField("playerTransform", WishPrivateInstance);
            MethodInfo chapel = type.GetMethod("ShouldRemainStationaryAtWeddingChapel", WishPrivateInstance);
            if (storyField == null || stayField == null || dialogueField == null || runField == null || playerField == null || chapel == null)
                return "goblin_reflection_target_missing";
            try
            {
                goblin = UnityEngine.Object.FindObjectOfType<GoblinNPCController>();
                if (goblin == null || !goblin.isActiveAndEnabled) { skip = "goblin_not_in_base"; return null; }
                if ((bool)storyField.GetValue(goblin)) { skip = "goblin_in_story_dialogue"; return null; }
                if ((bool)dialogueField.GetValue(goblin) && stayField.GetValue(goblin) == null) { skip = "goblin_in_live_dialogue"; return null; }
                if (playerField.GetValue(goblin) as Transform == null) { skip = "goblin_player_ref_missing"; return null; }
                if ((bool)chapel.Invoke(goblin, null)) { skip = "goblin_at_wedding_chapel"; return null; }
                float expectedStay = _host.AdjustDialogueStayDurationForSpouseFollow(GoblinAffinityConfig.NPC_ID, goblin.transform,
                    GoblinNPCConstants.DEFAULT_DIALOGUE_STAY_DURATION);
                if (expectedStay < 12f) { skip = "spouse_follow_short_stay"; return null; }
                // 与商店 / 重铸关闭时的调用一致：通用商店只传 10 秒，带告别时由叮当自己抬到 150 秒。
                goblin.StartDialogue();
                goblin.EndDialogueWithStay(10f, true);
                if (stayField.GetValue(goblin) == null) return "stay_coroutine_not_started";
                return null;
            }
            catch (Exception e)
            {
                return "goblin_probe_threw:" + e.GetType().Name + ":" + e.Message;
            }
        }

        private static string FinishGoblinStayProbe(GoblinNPCController goblin, out string metrics)
        {
            metrics = string.Empty;
            if (goblin == null) return "goblin_destroyed_during_stay";
            Type type = typeof(GoblinNPCController);
            FieldInfo stayField = type.GetField("currentStayCoroutine", WishPrivateInstance);
            FieldInfo dialogueField = type.GetField("isInDialogue", WishPrivateInstance);
            FieldInfo runField = type.GetField("isRunningToPlayer", WishPrivateInstance);
            try
            {
                bool stillStaying = stayField.GetValue(goblin) != null && (bool)dialogueField.GetValue(goblin);
                goblin.RunToPlayer();
                bool stayCleared = stayField.GetValue(goblin) == null;
                bool dialogueCleared = !(bool)dialogueField.GetValue(goblin);
                bool running = (bool)runField.GetValue(goblin);
                metrics = "still_staying_after_11s=" + stillStaying + ",stay_cleared_by_summon=" + stayCleared
                    + ",dialogue_cleared=" + dialogueCleared + ",running_to_player=" + running;
                if (!stillStaying) return "stay_ended_before_floor";
                if (!stayCleared || !dialogueCleared) return "summon_did_not_interrupt_stay";
                if (!running) return "summon_not_running";
                return null;
            }
            catch (Exception e)
            {
                return "goblin_finish_threw:" + e.GetType().Name + ":" + e.Message;
            }
        }

        // ------------------------------------------------------------------
        // 竞技场阶段
        // ------------------------------------------------------------------

        /// <summary>竞技场阶段：尸潮、出征令一局（含烽烟圈与钟罩）、龙裔落点。</summary>
        private IEnumerator RunWishArenaCases()
        {
            yield return RunIsolatedCase("ZOMBIE_CROWD_NAVIGATION", RunZombieCrowdNavigation);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_ZOMBIE_CROWD");

            yield return RunIsolatedCase("ARENA_EXPEDITION_ORDER", RunArenaExpeditionOrder);
            yield return VerifyArenaCleanup("CLEANUP_AFTER_EXPEDITION");

            yield return EnsureArenaForCase("DRAGON_DESCENDANT_SPAWN_PLACEMENT");
            if (_operationSucceeded) RunSyncCaseGated("DRAGON_DESCENDANT_SPAWN_PLACEMENT", ValidateDragonDescendantSpawnPlacement);
            else Record("DRAGON_DESCENDANT_SPAWN_PLACEMENT", "SKIP", 0L, string.Empty, "arena_runtime_not_ready");
        }

        private sealed class WishZombieTrack
        {
            internal CharacterMainControl Character;
            internal ZombieModeEnemyRuntimeMarker Marker;
            internal float FirstSeen;
            internal float LastRequest;
            internal int Requests;
            internal float DistanceSum;
            internal int DistanceSamples;
            internal int StackRun;
            internal int MaxStackRun;
        }

        /// <summary>
        /// 尸潮：开一局丧尸模式等尸潮成形，逐帧数每只丧尸的重新寻路次数，每 0.5 秒看一次脚下是不是别的角色。
        /// 玩家由套件每帧无敌保护，原地不动，所以目标基本不挪，远处丧尸的重算频率只受节流决定。
        /// </summary>
        private IEnumerator RunZombieCrowdNavigation()
        {
            Stopwatch sw = Stopwatch.StartNew();
            string reason;
            try
            {
                if (!_host.ValidationStartZombie(out reason))
                {
                    Record("ZOMBIE_CROWD_NAVIGATION", "FAIL", sw.ElapsedMilliseconds, string.Empty, reason);
                    yield break;
                }
                // ValidationStartZombie 直接替玩家选了初始流派，但「初始流派选择」面板还开着、占着模态输入，
                // 丧尸模式在模态期间整体暂停（不倒计时、不刷怪）。2026-11-24 首跑因此 57 秒一只没刷。
                // 照玩家点卡片时的收尾把面板关掉，输入租约随面板 OnDestroy 释放；销毁在帧末生效，等一帧再看。
                int starterPanelsClosed = CloseWishZombieStarterChoice();
                yield return null;
                bool modalPaused = ZombieModeUIHelper.IsModalInputPaused;
                if (modalPaused)
                {
                    Record("ZOMBIE_CROWD_NAVIGATION", "FAIL", sw.ElapsedMilliseconds,
                        "starter_closed=" + starterPanelsClosed + ",modal_leases=" + ZombieModeUIHelper.ModalInputLeaseCount,
                        "zombie_runtime_paused_by_modal");
                    yield break;
                }
                Dictionary<int, WishZombieTrack> tracks = new Dictionary<int, WishZombieTrack>();
                float formDeadline = Time.realtimeSinceStartup + 45f;
                while (Time.realtimeSinceStartup < formDeadline && !ShouldAbort())
                {
                    if (ScanWishZombies(tracks) >= 6) break;
                    yield return WaitSeconds(1f);
                }
                if (ShouldAbort())
                {
                    Record("ZOMBIE_CROWD_NAVIGATION", "SKIP", sw.ElapsedMilliseconds, string.Empty, DescribeAbortReason());
                    yield break;
                }
                bool prefixInstalled;
                string patchDetail = DescribeZombiePathPatch(out prefixInstalled);

                int maxSimultaneousLifted = 0;
                int callbackMissing = 0;
                int callbackForeign = 0;
                float sampleEnd = Time.realtimeSinceStartup + 12f;
                float nextScan = 0f;
                while (Time.realtimeSinceStartup < sampleEnd && !ShouldAbort())
                {
                    float now = Time.realtimeSinceStartup;
                    if (now >= nextScan)
                    {
                        nextScan = now + 0.5f;
                        ScanWishZombies(tracks);
                        int lifted = SampleWishZombieStacks(tracks, ref callbackMissing, ref callbackForeign);
                        if (lifted > maxSimultaneousLifted) maxSimultaneousLifted = lifted;
                    }
                    CountWishZombiePathRequests(tracks);
                    yield return null;
                }
                if (ShouldAbort())
                {
                    Record("ZOMBIE_CROWD_NAVIGATION", "SKIP", sw.ElapsedMilliseconds, string.Empty, DescribeAbortReason());
                    yield break;
                }

                float end = Time.realtimeSinceStartup;
                int persistentStacks = 0;
                int farZombies = 0;
                double farRateSum = 0d;
                double allRateSum = 0d;
                int requesting = 0;
                foreach (WishZombieTrack track in tracks.Values)
                {
                    if (track.MaxStackRun >= 6) persistentStacks++;
                    float observed = end - track.FirstSeen;
                    if (observed < 3f || track.Requests <= 0) continue;
                    double rate = track.Requests / observed;
                    requesting++;
                    allRateSum += rate;
                    float meanDistance = track.DistanceSamples > 0 ? track.DistanceSum / track.DistanceSamples : 0f;
                    if (meanDistance >= 20f)
                    {
                        farZombies++;
                        farRateSum += rate;
                    }
                }
                double farRate = farZombies > 0 ? farRateSum / farZombies : 0d;
                double allRate = requesting > 0 ? allRateSum / requesting : 0d;
                string metrics = "starter_closed=" + starterPanelsClosed + ",zombies_seen=" + tracks.Count + ",prefix=" + patchDetail
                    + ",callback_missing=" + callbackMissing + ",callback_foreign=" + callbackForeign
                    + ",max_lifted_stack=" + maxSimultaneousLifted + ",persistent_stacks=" + persistentStacks
                    + ",requesting=" + requesting + ",repath_per_s_all=" + allRate.ToString("0.00", CultureInfo.InvariantCulture)
                    + ",far_20m=" + farZombies + ",repath_per_s_far=" + farRate.ToString("0.00", CultureInfo.InvariantCulture)
                    + ",elapsed_s=" + (sw.ElapsedMilliseconds / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
                string failure = null;
                if (tracks.Count == 0) failure = "no_zombies_registered";
                else if (!prefixInstalled) failure = "path_throttle_prefix_missing";
                else if (callbackMissing > 0) failure = "zombie_collision_callback_missing";
                else if (persistentStacks > 0) failure = "zombie_stacked_over_3s";
                else if (farZombies > 0 && farRate > 3.5d) failure = "far_zombies_repath_unthrottled";
                Record("ZOMBIE_CROWD_NAVIGATION", failure == null ? "PASS" : "FAIL", sw.ElapsedMilliseconds, metrics, failure);
            }
            finally { _host.ValidationSafeCleanup(); }
        }

        /// <summary>关掉还开着的「初始流派选择」面板（它的 OnDestroy 释放模态输入租约），返回关掉的个数。</summary>
        private static int CloseWishZombieStarterChoice()
        {
            int closed = 0;
            ZombieModeStarterChoiceView[] views = UnityEngine.Object.FindObjectsOfType<ZombieModeStarterChoiceView>();
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] == null) continue;
                UnityEngine.Object.Destroy(views[i].gameObject);
                closed++;
            }
            return closed;
        }

        /// <summary>登记过、本局、活着的丧尸并进追踪表，返回当前活着的只数。</summary>
        private int ScanWishZombies(Dictionary<int, WishZombieTrack> tracks)
        {
            int alive = 0;
            int runId = _host.ZombieModeCurrentRunId;
            CharacterMainControl[] characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
            for (int i = 0; i < characters.Length; i++)
            {
                CharacterMainControl character = characters[i];
                ZombieModeEnemyRuntimeMarker marker;
                if (character == null || character.Health == null || character.Health.IsDead
                    || !_host.TryGetZombieModeKnownEnemyMarker(character, out marker) || marker == null
                    || marker.RunId != runId || marker.DeathSettled || marker.RemovedFromRuntime) continue;
                alive++;
                int key = character.GetInstanceID();
                if (!tracks.ContainsKey(key))
                {
                    tracks.Add(key, new WishZombieTrack
                    {
                        Character = character,
                        Marker = marker,
                        FirstSeen = Time.realtimeSinceStartup,
                        LastRequest = marker.LastPathRequestTime,
                    });
                }
            }
            return alive;
        }

        /// <summary>每只丧尸：碰撞行为回调是不是尸潮那一个；脚下是别的角色且高出半米算一次叠罗汉。返回此刻叠着的只数。</summary>
        private static int SampleWishZombieStacks(Dictionary<int, WishZombieTrack> tracks, ref int callbackMissing, ref int callbackForeign)
        {
            int lifted = 0;
            CharacterMainControl main = CharacterMainControl.Main;
            foreach (WishZombieTrack track in tracks.Values)
            {
                CharacterMainControl self = track.Character;
                ZombieModeEnemyRuntimeMarker marker = track.Marker;
                if (self == null || marker == null || marker.DeathSettled || marker.RemovedFromRuntime
                    || self.Health == null || self.Health.IsDead) { track.StackRun = 0; continue; }
                ECM2.CharacterMovement movement = marker.CachedMovement;
                if (movement == null || movement.collisionBehaviourCallback == null)
                {
                    if (track.DistanceSamples == 0) callbackMissing++;
                }
                else if (movement.collisionBehaviourCallback != ZombieModeCrowdNavigation.ZombieCollisionBehaviour)
                {
                    if (track.DistanceSamples == 0) callbackForeign++;
                }
                if (main != null)
                {
                    track.DistanceSum += WishFlatDistance(self.transform.position, main.transform.position);
                    track.DistanceSamples++;
                }
                Collider ground = movement != null ? movement.groundCollider : null;
                Rigidbody body = ground != null ? ground.attachedRigidbody : null;
                CharacterMainControl below = body != null ? body.GetComponent<CharacterMainControl>() : null;
                if (below != null && below != self && self.transform.position.y - below.transform.position.y > 0.5f)
                {
                    lifted++;
                    track.StackRun++;
                    if (track.StackRun > track.MaxStackRun) track.MaxStackRun = track.StackRun;
                }
                else track.StackRun = 0;
            }
            return lifted;
        }

        private static void CountWishZombiePathRequests(Dictionary<int, WishZombieTrack> tracks)
        {
            foreach (WishZombieTrack track in tracks.Values)
            {
                ZombieModeEnemyRuntimeMarker marker = track.Marker;
                if (marker == null || !marker.HasPathRequest) continue;
                if (marker.LastPathRequestTime != track.LastRequest)
                {
                    track.LastRequest = marker.LastPathRequestTime;
                    track.Requests++;
                }
            }
        }

        private static string DescribeZombiePathPatch(out bool installed)
        {
            installed = false;
            try
            {
                MethodBase original = HarmonyLib.AccessTools.Method(typeof(AI_PathControl), "MoveToPos", new[] { typeof(Vector3) });
                if (original == null) return "target_missing";
                HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(original);
                if (info == null) return "no_patches";
                foreach (HarmonyLib.Patch patch in info.Prefixes)
                {
                    if (patch != null && patch.PatchMethod != null && patch.PatchMethod.DeclaringType == typeof(ZombieModePathRequestThrottlePatch))
                    {
                        installed = true;
                        return "installed";
                    }
                }
                return "prefixes_" + info.Prefixes.Count + "_without_throttle";
            }
            catch (Exception e)
            {
                return "error_" + e.GetType().Name;
            }
        }

        /// <summary>
        /// 出征令一局：照面板立誓的调用顺序（放下选择 → 配置阵容 → 开第一波）开「单挑 · 困难 · 短征」，
        /// 同一局里接着核对烽烟圈和止语钟罩，最后清场看烽烟圈和角标收掉。
        /// </summary>
        private IEnumerator RunArenaExpeditionOrder()
        {
            Stopwatch sw = Stopwatch.StartNew();
            Item bell = null;
            Slot bellSlot = null;
            try
            {
                try
                {
                    List<EnemyPresetInfo> pool = _host.GetFilteredEnemyPresets();
                    ArenaExpeditionState.SetPending(new ArenaExpeditionOrder(ArenaExpeditionLineup.Single, ArenaExpeditionTier.Hard, ArenaExpeditionLength.Short));
                    _host.ConfigureBossRushMode(1, false);
                    _host.StartFirstWave();

                    CharacterMainControl boss = null;
                    float deadline = Time.realtimeSinceStartup + 20f;
                    while (Time.realtimeSinceStartup < deadline && !ShouldAbort())
                    {
                        boss = _host.ValidationGetCommittedStandardBoss();
                        if (boss != null && ArenaExpeditionBossScaling.HealthFactorOf(boss) > 1.01f) break;
                        yield return WaitSeconds(0.5f);
                    }
                    if (ShouldAbort())
                    {
                        Record("ARENA_EXPEDITION_ORDER", "SKIP", sw.ElapsedMilliseconds, string.Empty, DescribeAbortReason());
                        yield break;
                    }
                    string metrics;
                    string failure = JudgeArenaExpeditionRun(pool, boss, out metrics);
                    Record("ARENA_EXPEDITION_ORDER", failure == null ? "PASS" : "FAIL", sw.ElapsedMilliseconds, metrics, failure);

                    // 烽烟圈每 0.5 秒扫一次登记表，给它两轮。
                    yield return WaitSeconds(1.5f);
                    Stopwatch beaconWatch = Stopwatch.StartNew();
                    failure = JudgeBossMapBeacons(_host.ValidationGetCommittedStandardBoss(), out metrics);
                    Record("BOSS_MAP_BEACON", failure == null ? "PASS" : "FAIL", beaconWatch.ElapsedMilliseconds, metrics, failure);

                    Stopwatch bellWatch = Stopwatch.StartNew();
                    string plug = PlugSilenceBellProbe(out bell, out bellSlot);
                    if (plug != null)
                    {
                        bool skipped = plug.StartsWith("skip:", StringComparison.Ordinal);
                        Record("EQ_SILENCE_BELL_WARD", skipped ? "SKIP" : "FAIL", bellWatch.ElapsedMilliseconds, string.Empty,
                            skipped ? plug.Substring(5) : plug);
                    }
                    else
                    {
                        yield return null;
                        yield return null;
                        string bellMetrics;
                        string bellFailure = HitSilenceBellProbe(_host.ValidationGetCommittedStandardBoss(), out bellMetrics);
                        float shieldAfterHit = SilenceBellRuntime.Shield;
                        if (bellFailure == null)
                        {
                            try { bellSlot.Unplug(); }
                            catch (Exception e) { bellFailure = "unplug_threw:" + e.GetType().Name; }
                        }
                        if (bellFailure == null)
                        {
                            yield return null;
                            yield return null;
                            bool driverGone = !SilenceBellRuntime.IsActive && GameObject.Find(WishBellDriverName) == null;
                            bool kept = WishNear(SilenceBellRuntime.Shield, shieldAfterHit, 0.01f);
                            bellMetrics += ",after_unplug_driver_gone=" + driverGone + ",after_unplug_shield="
                                + SilenceBellRuntime.Shield.ToString("0.0", CultureInfo.InvariantCulture);
                            if (!driverGone) bellFailure = "driver_survives_unplug";
                            else if (!kept) bellFailure = "unplug_changed_shield";
                        }
                        Record("EQ_SILENCE_BELL_WARD", bellFailure == null ? "PASS" : "FAIL", bellWatch.ElapsedMilliseconds, bellMetrics, bellFailure);
                    }
                }
                finally
                {
                    RemoveSilenceBellProbe(bell, bellSlot);
                    bell = null;
                    bellSlot = null;
                    _host.ValidationSafeCleanup();
                }

                // 清场之后：烽烟圈全收、战帖角标不再显示（本局状态要到切图才清，角标靠「本局是否在跑」自己藏）。
                Stopwatch cleanupWatch = Stopwatch.StartNew();
                yield return WaitSeconds(1f);
                int beaconsLeft = CountWishBeacons();
                GameObject hudRoot = GameObject.Find(WishExpeditionHudName);
                Canvas hudCanvas = hudRoot != null ? hudRoot.GetComponent<Canvas>() : null;
                bool hudHidden = hudCanvas == null || !hudCanvas.enabled;
                string cleanupMetrics = "beacons_left=" + beaconsLeft + ",hud_hidden=" + hudHidden + ",mode_active=" + _host.IsActive;
                string cleanupFailure = beaconsLeft > 0 ? "beacons_survive_cleanup" : (!hudHidden ? "expedition_seal_survives_cleanup" : null);
                Record("ARENA_EXPEDITION_CLEANUP", cleanupFailure == null ? "PASS" : "FAIL", cleanupWatch.ElapsedMilliseconds, cleanupMetrics, cleanupFailure);
            }
            finally
            {
                // 与切图时 WavesArenaRuntimeModule.EndExpeditionRun 同一收尾：后面的用例按旧入口开局，不能带着困难战帖。
                ArenaExpeditionState.End();
                // 战帖词条随这一局走：清掉，后面的用例不带着困难战规跑。
                try { _host.ClearMutatorsForMode("BossRush"); }
                catch (Exception e) { ModBehaviour.DevLog("[Validation] 出征令词条清理失败: " + e.Message); }
            }
        }

        private string JudgeArenaExpeditionRun(List<EnemyPresetInfo> pool, CharacterMainControl boss, out string metrics)
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null)
            {
                metrics = "active=" + _host.IsActive;
                return "expedition_run_missing";
            }
            int expectedWaves = ArenaExpeditionTuning.WavesFor(ArenaExpeditionLength.Short, run.PoolCount);
            bool queueExpected = expectedWaves >= 2 && expectedWaves < run.PoolCount;
            bool orderOk = run.Order.Lineup == ArenaExpeditionLineup.Single && run.Order.Tier == ArenaExpeditionTier.Hard
                && run.Order.Length == ArenaExpeditionLength.Short;
            bool queueOk;
            bool finaleOk = true;
            string finaleName = run.Finale != null ? run.Finale.name : "none";
            bool finaleStrong = run.Finale != null && WavesArenaRuntimeModule.EarlyWaveExcludedBosses.Contains(run.Finale.name);
            if (!queueExpected) queueOk = !run.HasQueue;
            else
            {
                queueOk = run.HasQueue && run.Queue.Count == expectedWaves && run.Finale != null
                    && ReferenceEquals(run.Queue[run.Queue.Count - 1], run.Finale) && run.IsFinaleWave(expectedWaves - 1);
                // 前面的波里没用到的启用 Boss 中有强 Boss，压轴就必须是强 Boss；一只都没有时退到血最厚的。
                HashSet<string> usedBeforeFinale = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; queueOk && i < run.Queue.Count - 1; i++)
                {
                    if (run.Queue[i] != null && run.Queue[i].name != null) usedBeforeFinale.Add(run.Queue[i].name);
                }
                bool strongAvailable = false;
                for (int i = 0; queueOk && pool != null && i < pool.Count; i++)
                {
                    EnemyPresetInfo preset = pool[i];
                    if (preset == null || preset.name == null || usedBeforeFinale.Contains(preset.name)) continue;
                    if (WavesArenaRuntimeModule.EarlyWaveExcludedBosses.Contains(preset.name)) strongAvailable = true;
                }
                for (int i = 0; queueOk && i < run.Queue.Count - 1; i++)
                {
                    if (ReferenceEquals(run.Queue[i], run.Finale)) queueOk = false;
                }
                finaleOk = !strongAvailable || finaleStrong;
            }
            float factor = boss != null ? ArenaExpeditionBossScaling.HealthFactorOf(boss) : 0f;
            bool scaled = WishNear(factor, ArenaExpeditionTuning.HealthMultiplier(ArenaExpeditionTier.Hard), 0.01f);
            GameObject hudRoot = GameObject.Find(WishExpeditionHudName);
            Canvas hudCanvas = hudRoot != null ? hudRoot.GetComponent<Canvas>() : null;
            bool hudShown = hudCanvas != null && (hudCanvas.enabled || BossRushUI.IsOfficialHudHidden());
            // 困难战规借用的三条词条（神枪敌军、伤口难愈、嗜血）随开波先上；出征令开的局不抽随机词条（owner 2026-11-25），
            // 所以场上的词条恰好就是这三条，变异词条栏也不出现。
            List<string> writIds = ArenaExpeditionTuning.ForcedMutatorIds(ArenaExpeditionTier.Hard);
            IReadOnlyList<MutatorDefinition> activeMutators = MutatorManager.GetActiveMutators();
            int writOn = 0;
            for (int i = 0; writIds != null && i < activeMutators.Count; i++)
            {
                if (activeMutators[i] != null && writIds.Contains(activeMutators[i].Id) && MutatorManager.IsForcedMutator(activeMutators[i])) writOn++;
            }
            int writWanted = writIds != null ? writIds.Count : -1;
            metrics = "active=" + _host.IsActive + ",pool=" + run.PoolCount + ",waves=" + run.Waves + ",expected=" + expectedWaves
                + ",queue=" + run.HasQueue + ",finale=" + finaleName + ",finale_strong=" + finaleStrong
                + ",boss=" + (boss != null ? boss.name : "none") + ",hp_factor=" + factor.ToString("0.00", CultureInfo.InvariantCulture)
                + ",seal_hud=" + hudShown + ",writ_mutators=" + writOn + "/" + writWanted + ",active_mutators=" + activeMutators.Count;
            if (!orderOk) return "order_not_taken";
            if (!queueOk) return queueExpected ? "short_queue_wrong" : "queue_built_when_pool_small";
            if (!finaleOk) return "finale_not_strong";
            if (boss == null) return "boss_not_spawned";
            if (!scaled) return "hard_health_factor_missing";
            if (!hudShown) return "expedition_seal_missing";
            if (writOn != writWanted) return "writ_mutators_missing";
            if (activeMutators.Count != writOn) return "random_mutators_rolled_in_expedition";
            return null;
        }

        private static int CountWishBeacons()
        {
            int count = 0;
            SimplePointOfInterest[] pois = UnityEngine.Object.FindObjectsOfType<SimplePointOfInterest>();
            for (int i = 0; i < pois.Length; i++)
            {
                if (pois[i] != null && pois[i].gameObject.name == WishBeaconObjectName) count++;
            }
            return count;
        }

        private static string JudgeBossMapBeacons(CharacterMainControl boss, out string metrics)
        {
            CharacterMainControl main = CharacterMainControl.Main;
            int beacons = 0;
            int areas = 0;
            int revealed = 0;
            int badRadius = 0;
            int offPin = 0;
            int staleArea = 0;
            float maxOffset = 0f;
            float bossDistance = boss != null && main != null ? WishFlatDistance(boss.transform.position, main.transform.position) : -1f;
            SimplePointOfInterest[] pois = UnityEngine.Object.FindObjectsOfType<SimplePointOfInterest>();
            for (int i = 0; i < pois.Length; i++)
            {
                SimplePointOfInterest poi = pois[i];
                if (poi == null || poi.gameObject.name != WishBeaconObjectName) continue;
                beacons++;
                float offset = boss != null ? WishFlatDistance(poi.transform.position, boss.transform.position) : 0f;
                if (poi.IsArea)
                {
                    areas++;
                    float radius = poi.AreaRadius;
                    if (!WishNear(radius, BossMapBeaconRules.RadiusOf(0)) && !WishNear(radius, BossMapBeaconRules.RadiusOf(1))
                        && !WishNear(radius, BossMapBeaconRules.RadiusOf(2))) badRadius++;
                    if (offset > maxOffset) maxOffset = offset;
                    // 玩家离 Boss 已在显形距离以内（留 2 米余量）却还是烽烟，说明显形没生效。
                    if (bossDistance >= 0f && bossDistance < BossMapBeaconRules.RevealDistance - 2f) staleArea++;
                }
                else
                {
                    revealed++;
                    if (boss != null && offset > 1.5f) offPin++;
                    if (!WishNear(poi.ScaleFactor, BossMapBeaconRules.RevealedScale)) offPin++;
                }
            }
            metrics = "beacons=" + beacons + ",areas=" + areas + ",revealed=" + revealed + ",bad_radius=" + badRadius
                + ",off_pin=" + offPin + ",stale_area=" + staleArea
                + ",boss_distance=" + bossDistance.ToString("0.0", CultureInfo.InvariantCulture)
                + ",max_area_offset=" + maxOffset.ToString("0.0", CultureInfo.InvariantCulture);
            if (boss == null) return "boss_not_spawned";
            if (beacons == 0) return "beacon_missing";
            if (badRadius > 0) return "beacon_radius_wrong";
            if (staleArea > 0) return "beacon_not_revealed_within_40m";
            if (offPin > 0) return "revealed_beacon_not_on_boss";
            return null;
        }

        /// <summary>找一个空图腾槽插一件钟罩。玩家已经戴着、或没有空槽时返回 skip:，不动玩家自己的装备。</summary>
        private static string PlugSilenceBellProbe(out Item bell, out Slot slot)
        {
            bell = null;
            slot = null;
            CharacterMainControl main = CharacterMainControl.Main;
            Item characterItem = main != null ? main.CharacterItem : null;
            if (characterItem == null || characterItem.Slots == null) return "player_item_missing";
            if (NewWeaponEquipState.IsTotemEquipped(BossRushItemIds.SilenceBell)) return "skip:player_already_wears_bell";
            foreach (Slot candidate in characterItem.Slots)
            {
                if (candidate == null || candidate.Key == null || candidate.Content != null
                    || !candidate.Key.StartsWith("Totem", StringComparison.Ordinal)) continue;
                slot = candidate;
                break;
            }
            if (slot == null) return "skip:no_free_totem_slot";
            try
            {
                bell = ItemAssetsCollection.InstantiateSync(BossRushItemIds.SilenceBell);
                if (bell == null)
                {
                    slot = null;
                    return "bell_instantiate_failed";
                }
                Item replaced;
                if (!slot.Plug(bell, out replaced))
                {
                    try { bell.DestroyTree(); } catch (Exception) { /* 探针没插上，直接丢 */ }
                    bell = null;
                    slot = null;
                    return "totem_slot_rejected_bell";
                }
                return null;
            }
            catch (Exception e)
            {
                return "bell_plug_threw:" + e.GetType().Name + ":" + e.Message;
            }
        }

        /// <summary>
        /// 驱动在不在、敌方 20 点直接伤害先扣护罩。走受击扣血前的共享入口（残日盾片 → 钟罩 → 丧尸门），
        /// 套件开着无敌，Health.Hurt 在无敌判定就返回了，所以直接调这个入口，同时核对 Hurt 的转写补丁装着。
        /// </summary>
        private static string HitSilenceBellProbe(CharacterMainControl boss, out string metrics)
        {
            metrics = string.Empty;
            CharacterMainControl main = CharacterMainControl.Main;
            bool driver = SilenceBellRuntime.IsActive && GameObject.Find(WishBellDriverName) != null;
            bool transpiler = false;
            try
            {
                MethodBase hurt = HarmonyLib.AccessTools.Method(typeof(Health), "Hurt", new[] { typeof(DamageInfo) });
                HarmonyLib.Patches info = hurt != null ? HarmonyLib.Harmony.GetPatchInfo(hurt) : null;
                if (info != null)
                {
                    foreach (HarmonyLib.Patch patch in info.Transpilers)
                    {
                        if (patch != null && patch.PatchMethod != null && patch.PatchMethod.DeclaringType == typeof(ZombieModeHealthHurtDamagePatch)) transpiler = true;
                    }
                }
            }
            catch (Exception) { transpiler = false; }
            if (!driver)
            {
                metrics = "driver=false,transpiler=" + transpiler;
                return "bell_driver_missing_after_equip";
            }
            // 金钟罩（owner 2026-11-25）：戴上后驱动下常驻一口金钟，跟着主角走
            GameObject driverObject = GameObject.Find(WishBellDriverName);
            Transform aura = driverObject != null ? driverObject.transform.Find("SilenceBell_Fx/GoldenBell") : null;
            bool auraOn = aura != null && aura.gameObject.activeInHierarchy;
            float auraOffset = auraOn && main != null ? Vector3.Distance(aura.position, main.transform.position) : -1f;
            string auraMetrics = ",aura=" + auraOn + ",aura_offset=" + auraOffset.ToString("0.00", CultureInfo.InvariantCulture);
            if (boss == null || main == null || main.Health == null)
            {
                metrics = "driver=true,transpiler=" + transpiler + auraMetrics;
                return "no_hostile_source_for_hit";
            }
            float before = SilenceBellRuntime.Shield;
            DamageInfo hit = new DamageInfo(boss);
            hit.damageValue = 20f;
            hit.finalDamage = 20f;
            ZombieModeDamageRuntime.ReduceFinalDamage(main.Health, ref hit);
            float after = SilenceBellRuntime.Shield;
            float expectedAbsorb = Mathf.Min(20f, before);
            metrics = "driver=true,transpiler=" + transpiler
                + ",shield_before=" + before.ToString("0.0", CultureInfo.InvariantCulture)
                + ",shield_after=" + after.ToString("0.0", CultureInfo.InvariantCulture)
                + ",final_after=" + hit.finalDamage.ToString("0.0", CultureInfo.InvariantCulture) + auraMetrics;
            if (!transpiler) return "hurt_transpiler_missing";
            if (!auraOn) return "bell_aura_missing_while_worn";
            if (auraOffset > 0.75f) return "bell_aura_not_on_player";
            if (!(before > 0f)) return "bell_shield_empty_at_equip";
            if (!WishNear(before - after, expectedAbsorb, 0.01f)) return "bell_absorb_wrong";
            if (!WishNear(hit.finalDamage, 20f - expectedAbsorb, 0.01f)) return "damage_not_reduced_by_bell";
            return null;
        }

        private static void RemoveSilenceBellProbe(Item bell, Slot slot)
        {
            if (bell == null) return;
            try { if (slot != null && slot.Content == bell) slot.Unplug(); }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 钟罩探针拔下失败: " + e.Message); }
            try { bell.DestroyTree(); }
            catch (Exception e) { ModBehaviour.DevLog("[Validation] 钟罩探针销毁失败: " + e.Message); }
        }

        /// <summary>
        /// 龙裔落点：玩家脚下（空地）与玩家某个方向第一面墙前 1 米（贴墙）各取 16 次。
        /// 落点必须在 5 米圈（加吸附 1.5 米）内、高差不超过 1.5 米、与起点之间没有墙、从起点可达。
        /// </summary>
        private static bool ValidateDragonDescendantSpawnPlacement(out string metrics, out string reason)
        {
            reason = null;
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
            {
                metrics = string.Empty;
                reason = "player_missing";
                return false;
            }
            int wallMask = GameplayDataSettings.Layers.wallLayerMask.value;
            Vector3 lift = Vector3.up * 0.8f;
            Vector3 open = main.transform.position;
            Vector3 nearWall = open;
            bool hasWall = false;
            float wallDistance = -1f;
            for (int i = 0; i < 8 && !hasWall && wallMask != 0; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                RaycastHit hitInfo;
                if (!Physics.Raycast(open + lift, direction, out hitInfo, 25f, wallMask, QueryTriggerInteraction.Ignore) || hitInfo.distance <= 2f) continue;
                Vector3 resolved;
                if (!SpawnPositionHelper.TryResolveReachableFrom(open + direction * (hitInfo.distance - 1f), open, 1.5f, 0.05f, out resolved)) continue;
                nearWall = resolved;
                hasWall = true;
                wallDistance = hitInfo.distance;
            }

            Vector3[] anchors = hasWall ? new[] { open, nearWall } : new[] { open };
            int samples = 0;
            int bad = 0;
            int walled = 0;
            int unreachable = 0;
            int openFallbacks = 0;
            float maxRadius = 0f;
            float maxDy = 0f;
            for (int a = 0; a < anchors.Length; a++)
            {
                Vector3 anchor = anchors[a];
                for (int k = 0; k < 16; k++)
                {
                    Vector3 point = DragonKingAbilityController.ResolveDescendantSpawnNear(anchor);
                    samples++;
                    float radius = WishFlatDistance(point, anchor);
                    float dy = Mathf.Abs(point.y - anchor.y);
                    if (radius > maxRadius) maxRadius = radius;
                    if (dy > maxDy) maxDy = dy;
                    if (a == 0 && radius < 0.05f) openFallbacks++;
                    bool wall = wallMask != 0 && Physics.Linecast(anchor + lift, point + lift, wallMask, QueryTriggerInteraction.Ignore);
                    Vector3 snapped;
                    bool reachable = SpawnPositionHelper.TryResolveReachableFrom(point, anchor, 1.5f, 0.05f, out snapped);
                    if (wall) walled++;
                    if (!reachable) unreachable++;
                    if (wall || !reachable || radius > 5f + 1.5f + 0.1f || dy > 1.5f + 0.01f) bad++;
                }
            }
            metrics = "samples=" + samples + ",near_wall_anchor=" + hasWall
                + ",wall_distance=" + wallDistance.ToString("0.0", CultureInfo.InvariantCulture)
                + ",bad=" + bad + ",walled=" + walled + ",unreachable=" + unreachable + ",open_fallbacks=" + openFallbacks
                + ",max_radius=" + maxRadius.ToString("0.00", CultureInfo.InvariantCulture)
                + ",max_dy=" + maxDy.ToString("0.00", CultureInfo.InvariantCulture);
            if (wallMask == 0) reason = "wall_layer_mask_empty";
            else if (bad > 0) reason = walled > 0 ? "descendant_spawn_behind_wall" : "descendant_spawn_out_of_bounds";
            else if (openFallbacks == 16) reason = "open_ground_always_falls_back";
            return reason == null;
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        private static bool WishNear(float a, float b, float epsilon = 0.001f)
        {
            return Math.Abs(a - b) <= epsilon;
        }

        private static float WishFlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
#endif
