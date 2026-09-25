// ============================================================================
// CampaignContentCatalog.cs - 鸭王征程章节内容目录
// ============================================================================
// 归位依据 AGENTS.md 4.8 第 3 层：大型数据表走 Assets/Data/Campaign/*.json
// + Registry + **全量硬编码 fallback**。
//
// 【为什么必须有硬编码 fallback】
//   数据表读不到就没有章节，杰夫的任务页上一条征程任务都没有——这是玩家可见故障。
//   硬编码兜底保证「即使 JSON 丢了/坏了，战役照样能玩」，JSON 与硬编码签名必须同步更新，避免部署旧表。校验不过时**整表回退**，不做逐条挑拣：半张表比没有表更难排查。
//
// 【现行数值】
//   章节奖金 2 万 → 20 万递增（参照成就系统约 1676 万的总奖金量级取的保守值）。
//   2026-09-18 保留奖金与战斗门槛，第三章移除无额外决策价值的等待门。
//   2026-09-22 换故事《册子上的名字》：ch2 加基地侧目标「建好菜地」、ch3 加「摆上一件战利品」，
//   ch5 波次门 4→5（撤离本就在第 5 波 Boss 后，目标行与动作对齐，难度不增）；ID、token、奖金不变。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace BossRush
{
    /// <summary>章节内容目录。JSON 优先，校验不过整表回退硬编码。</summary>
    internal static class CampaignContentCatalog
    {
        #region 常量

        private const string DataSubDirectory = "Campaign";
        private const string ChaptersFileName = "Chapters.json";

        /// <summary>模式标识。与 CampaignModeBridge 的漏斗一一对应。</summary>
        internal const string ModeStandard = "standard";
        internal const string ModeModeD = "modeD";
        internal const string ModeModeE = "modeE";
        internal const string ModeModeF = "modeF";
        internal const string ModeZombie = "zombie";
        internal const string ModeFinal = "final";

        #endregion

        #region 状态

        private static List<CampaignChapterDef> _chapters;
        private static bool _loadAttempted;
        private static string _source = "Unloaded";
        private static string _contentSignature = string.Empty;

        #endregion

        /// <summary>章节列表（按 order 升序）。永不返回 null。</summary>
        internal static IList<CampaignChapterDef> Chapters
        {
            get
            {
                EnsureLoaded();
                return _chapters;
            }
        }

        /// <summary>诊断来源。正式部署验收要求为 Json，Fallback 只保证可玩。</summary>
        internal static string Source
        {
            get { EnsureLoaded(); return _source; }
        }

        /// <summary>规范化章节表签名，供 F3 报告与部署核对。</summary>
        internal static string ContentSignature
        {
            get { EnsureLoaded(); return _contentSignature; }
        }

        internal static string ExpectedContentSignature
        {
            get { return ComputeContentSignature(BuildHardcodedChapters()); }
        }

        /// <summary>按 ID 取章节；不存在返回 null。</summary>
        internal static CampaignChapterDef GetChapter(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId)) return null;
            EnsureLoaded();
            for (int i = 0; i < _chapters.Count; i++)
            {
                if (string.Equals(_chapters[i].ChapterId, chapterId, StringComparison.Ordinal))
                {
                    return _chapters[i];
                }
            }
            return null;
        }

        /// <summary>按序号取章节（order 从 1 起）；不存在返回 null。</summary>
        internal static CampaignChapterDef GetChapterByOrder(int order)
        {
            EnsureLoaded();
            for (int i = 0; i < _chapters.Count; i++)
            {
                if (_chapters[i].Order == order) return _chapters[i];
            }
            return null;
        }

        internal static string GetEntryHint(string mode)
        {
            switch (mode)
            {
                case CampaignContentCatalog.ModeStandard:
                    return L10n.T("带装备和船票进标准竞技场，路牌选标准那一档，前两波稳着打，通关回基地找杰夫交任务。",
                        "Bring gear and a ticket into the Standard Arena, pick a standard tier at the sign, play the first two waves safe, then hand in to Jeff at base.");
                case CampaignContentCatalog.ModeModeD:
                    return L10n.T("先带铲子 ×1、粑粑 ×9 去基地的菜地工地交钱动工，再只带船票空手进白手起家，用开局发的近战刀砍 5 个并打到第 5 波。",
                        "Pay for the garden site at base first (Shovel ×1, Poop ×9), then enter From Scratch with only a ticket, no gear. Use the starter melee knife for 5 kills and push to wave 5.");
                case CampaignContentCatalog.ModeModeE:
                    return L10n.T("带船票和营旗裸装进划地为营，选阵营后干掉 8 个敌方头目，回基地再把 1 件 Boss 战利品摆上枪械展示架或假人。",
                        "Enter Faction War with a ticket and a faction banner, no gear. Pick a side, drop 8 hostile bosses, then put 1 Boss trophy on a weapon rack or a dummy back at base.");
                case CampaignContentCatalog.ModeModeF:
                    return L10n.T("带船票和血猎收发器裸装进血猎追击，干掉 3 个带悬赏印记的目标，再从撤离点走。",
                        "Enter Blood Hunt with a ticket and a Bloodhunt Transponder, no gear. Kill 3 marked targets, then leave through the extraction point.");
                case CampaignContentCatalog.ModeZombie:
                    return L10n.T("用尸潮邀请函出发，撑到第 5 波，Boss 打完撤离点就开，站上去走。",
                        "Set out with a Zombie Tide Invitation, hold to wave 5, and the extraction opens once that Boss is down. Step on it and leave.");
                case CampaignContentCatalog.ModeFinal:
                    return L10n.T("带装备和船票进竞技场，不带其它模式信物，别点路牌，按住身边的报名石开打。",
                        "Enter the arena with gear and a ticket, no other mode tokens. Don't start the sign. Hold the sign-up stone beside you to begin.");
                default: return string.Empty;
            }
        }

        /// <summary>章节交付后的解锁飘字（对话播完再弹）。明确说解锁了什么、去哪用。</summary>
        internal static string GetDeliveredNotice(string chapterId)
        {
            switch (chapterId)
            {
                case "ch1":
                    return L10n.T("已解锁菜地。基地的菜地工地开放了，带铲子 ×1、粑粑 ×9 走过去交钱动工。起步种子放进了背包，之后基地售货机有卖，龙裔遗族、焚天龙皇、幽灵女巫也会掉；种出来的收成能让你短暂变身。",
                        "Garden unlocked. The garden site at base is open: bring Shovel ×1 and Poop ×9, walk up and pay. Starter seeds are in your backpack; the base vendor sells more, and the Dragon Descendant, Dragon King and Phantom Witch drop them. Your harvest gives you a temporary Boss form.");
                case "ch2":
                    return L10n.T("已解锁陈列加成。把 Boss 战利品摆上基地的枪械展示架或假人，每件给生命上限加成。",
                        "Display bonus unlocked. Put Boss trophies on the base weapon display rack or on a dummy. Each one raises your max health.");
                case "ch3":
                    return L10n.T("已解锁点唱机战歌。基地点唱机里多了「龙裔挽歌」和「幽影回廊」。",
                        "Jukebox tracks unlocked. \"Dragon Elegy\" and \"Umbral Corridors\" are now in the base jukebox.");
                case "ch4":
                    return L10n.T("第四行已入册。下一章是疫区那场，带一份菜地收成（龙息果、焚心椒或幽影蘑菇），战斗时吃下能变成对应头目三十秒。",
                        "Line four is in the ledger. Next up is the quarantine match. Take a garden harvest (Dragonbreath Fruit, Emberheart Chili or Umbral Mushroom) and eat it during combat for thirty seconds in its Boss form.");
                case "ch5":
                    return L10n.T("第五行已入册。报名石会立在竞技场里等你，别先点路牌。",
                        "Line five is in the ledger. A sign-up stone will be waiting in the arena. Don't start the sign first.");
                case "ch6":
                    return L10n.T("鸭王征程完成。菜地、陈列加成、点唱机战歌都留着，随时用。",
                        "Duck King Campaign complete. The garden, the display bonus and the jukebox tracks are yours to keep.");
                default:
                    return L10n.T("契约已交付。", "Contract handed in.");
            }
        }

        #region 装载

        private static void EnsureLoaded()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            List<CampaignChapterDef> parsed = TryLoadFromJson();
            if (parsed != null)
            {
                _chapters = parsed;
                _source = "Json";
                _contentSignature = ComputeContentSignature(_chapters);
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "章节表已从 JSON 装载: " + _chapters.Count + " 章");
                return;
            }

            _chapters = BuildHardcodedChapters();
            _source = "Fallback";
            _contentSignature = ComputeContentSignature(_chapters);
            ModBehaviour.DevLog(CampaignTuning.LogPrefix + "章节表使用硬编码兜底: " + _chapters.Count + " 章");
        }

        private static List<CampaignChapterDef> TryLoadFromJson()
        {
            try
            {
                string json;
                if (!JsonDataRegistry.TryReadDataFile(DataSubDirectory, ChaptersFileName, out json))
                {
                    return null;
                }

                // 不使用 Unity JsonUtility：实机 Unity 2022.3 会在这个包含二级对象数组的
                // internal DTO 上只填 version、静默把 chapters 留成 null。Mode H 的 token
                // parser 已经承担七张生产表并支持 BOM/严格类型，这里复用同一实现。
                BossRushJsonValue root;
                string parseError;
                if (!ModeHCanonicalDigest.TryParse(json, out root, out parseError)
                    || root == null || root.Kind != BossRushJsonKind.Object)
                {
                    LogTableRejected("JSON 解析失败: " + (parseError ?? "root_not_object"));
                    return null;
                }

                int version;
                if (!root.TryGetInt("version", out version) || version != 1)
                {
                    LogTableRejected("version 不受支持");
                    return null;
                }

                List<BossRushJsonValue> chapterRows;
                if (!root.TryGetArray("chapters", out chapterRows)
                    || chapterRows == null || chapterRows.Count == 0)
                {
                    LogTableRejected("空表或解析失败");
                    return null;
                }

                List<CampaignChapterDef> result = new List<CampaignChapterDef>();
                HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 0; i < chapterRows.Count; i++)
                {
                    BossRushJsonValue row = chapterRows[i];
                    string chapterId;
                    if (row == null || row.Kind != BossRushJsonKind.Object
                        || !row.TryGetString("chapterId", out chapterId)
                        || string.IsNullOrEmpty(chapterId))
                    {
                        LogTableRejected("第 " + i + " 行缺 chapterId");
                        return null;
                    }
                    if (!seenIds.Add(chapterId))
                    {
                        LogTableRejected("chapterId 重复: " + chapterId);
                        return null;
                    }

                    CampaignChapterDef def = new CampaignChapterDef();
                    def.ChapterId = chapterId;
                    if (!row.TryGetInt("order", out def.Order))
                    {
                        LogTableRejected("章节 order 缺失: " + def.ChapterId);
                        return null;
                    }
                    if (!row.TryGetString("mode", out def.Mode)) def.Mode = string.Empty;
                    if (!row.TryGetString("titleCN", out def.TitleCN)) def.TitleCN = def.ChapterId;
                    if (!row.TryGetString("titleEN", out def.TitleEN)) def.TitleEN = def.ChapterId;
                    if (!row.TryGetInt("rewardCash", out def.RewardCash)) def.RewardCash = -1;
                    if (!row.TryGetString("facilityToken", out def.FacilityToken)) def.FacilityToken = string.Empty;
                    if (!row.TryGetString("clueId", out def.ClueId)) def.ClueId = string.Empty;

                    if (!IsKnownMode(def.Mode))
                    {
                        LogTableRejected("未知模式: " + def.Mode + "（" + def.ChapterId + "）");
                        return null;
                    }
                    if (def.RewardCash < 0 || string.IsNullOrEmpty(def.FacilityToken)
                        || string.IsNullOrEmpty(def.ClueId))
                    {
                        LogTableRejected("奖励/token/clue 非法: " + def.ChapterId);
                        return null;
                    }

                    List<BossRushJsonValue> objectiveRows;
                    if (row.TryGetArray("objectives", out objectiveRows) && objectiveRows != null)
                    {
                        for (int j = 0; j < objectiveRows.Count; j++)
                        {
                            BossRushJsonValue objRow = objectiveRows[j];
                            if (objRow == null || objRow.Kind != BossRushJsonKind.Object)
                            {
                                LogTableRejected("目标格式非法: " + def.ChapterId + "#" + j);
                                return null;
                            }

                            string objectiveType;
                            if (!objRow.TryGetString("type", out objectiveType)) objectiveType = string.Empty;
                            CampaignObjectiveKind kind = ParseObjectiveKind(objectiveType);
                            if (kind == CampaignObjectiveKind.Unknown)
                            {
                                // 未知目标类型永远无法完成，会让整章卡死——整表回退比放行安全
                                LogTableRejected("未知目标类型: " + objectiveType + "（" + def.ChapterId + "）");
                                return null;
                            }

                            CampaignObjectiveDef obj = new CampaignObjectiveDef();
                            obj.Kind = kind;
                            if (!objRow.TryGetInt("threshold", out obj.Threshold)) obj.Threshold = 0;
                            if (!objRow.TryGetString("descCN", out obj.DescCN)) obj.DescCN = string.Empty;
                            if (!objRow.TryGetString("descEN", out obj.DescEN)) obj.DescEN = string.Empty;
                            def.Objectives.Add(obj);
                            if (obj.Threshold <= 0)
                            {
                                LogTableRejected("目标阈值必须为正数: " + def.ChapterId);
                                return null;
                            }
                        }
                    }

                    if (def.Objectives.Count == 0)
                    {
                        // 没有目标的章节会在接取瞬间就"完成"，属数据错误
                        LogTableRejected("章节没有任何目标: " + def.ChapterId);
                        return null;
                    }

                    result.Add(def);
                }

                result.Sort(CompareByOrder);

                // order 必须从 1 起连续：断号会让解锁链在缺口处永久卡住
                for (int i = 0; i < result.Count; i++)
                {
                    if (result[i].Order != i + CampaignTuning.FirstChapter)
                    {
                        LogTableRejected("order 不连续，期望 " + (i + CampaignTuning.FirstChapter)
                            + " 实际 " + result[i].Order);
                        return null;
                    }
                }

                if (result.Count != CampaignTuning.ChapterCount
                    || !string.Equals(result[result.Count - 1].Mode, ModeFinal, StringComparison.Ordinal))
                {
                    LogTableRejected("章节数量或终章位置不符合冻结契约");
                    return null;
                }
                for (int i = 0; i < result.Count - 1; i++)
                {
                    if (string.Equals(result[i].Mode, ModeFinal, StringComparison.Ordinal))
                    {
                        LogTableRejected("final 只能出现在终章");
                        return null;
                    }
                }

                HashSet<string> tokens = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> clues = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < result.Count; i++)
                {
                    if (!tokens.Add(result[i].FacilityToken) || !clues.Add(result[i].ClueId))
                    {
                        LogTableRejected("facilityToken 或 clueId 重复");
                        return null;
                    }
                }

                List<CampaignChapterDef> fallback = BuildHardcodedChapters();
                if (!string.Equals(ComputeContentSignature(result), ComputeContentSignature(fallback),
                        StringComparison.Ordinal))
                {
                    LogTableRejected("JSON 与当前六章冻结内容不一致");
                    return null;
                }

                return result;
            }
            catch (Exception e)
            {
                LogTableRejected("异常: " + e.Message);
                return null;
            }
        }

        private static int CompareByOrder(CampaignChapterDef a, CampaignChapterDef b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            return a.Order.CompareTo(b.Order);
        }

        private static void LogTableRejected(string reason)
        {
            ModBehaviour.CriticalLog(
                "campaign-chapters-fallback",
                CampaignTuning.LogPrefix + "[WARNING] Chapters.json 校验不通过，整表回退硬编码: " + reason);
        }

        internal static CampaignObjectiveKind ParseObjectiveKind(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return CampaignObjectiveKind.Unknown;
            switch (raw)
            {
                case "standard_clear": return CampaignObjectiveKind.StandardClear;
                case "no_damage_until_wave": return CampaignObjectiveKind.NoDamageUntilWave;
                case "melee_kills": return CampaignObjectiveKind.MeleeKills;
                case "reach_wave": return CampaignObjectiveKind.ReachWave;
                case "faction_boss_kills": return CampaignObjectiveKind.FactionBossKills;
                case "survive_minutes": return CampaignObjectiveKind.SurviveMinutes;
                case "bounty_kills": return CampaignObjectiveKind.BountyKills;
                case "mode_extract": return CampaignObjectiveKind.ModeExtract;
                case "final_boss_kill": return CampaignObjectiveKind.FinalBossKill;
                case "garden_built": return CampaignObjectiveKind.GardenBuilt;
                case "trophy_displayed": return CampaignObjectiveKind.TrophyDisplayed;
                default: return CampaignObjectiveKind.Unknown;
            }
        }

        private static bool IsKnownMode(string mode)
        {
            return string.Equals(mode, ModeStandard, StringComparison.Ordinal)
                || string.Equals(mode, ModeModeD, StringComparison.Ordinal)
                || string.Equals(mode, ModeModeE, StringComparison.Ordinal)
                || string.Equals(mode, ModeModeF, StringComparison.Ordinal)
                || string.Equals(mode, ModeZombie, StringComparison.Ordinal)
                || string.Equals(mode, ModeFinal, StringComparison.Ordinal);
        }

        private static string ComputeContentSignature(IList<CampaignChapterDef> chapters)
        {
            StringBuilder value = new StringBuilder(1024);
            if (chapters != null)
            {
                for (int i = 0; i < chapters.Count; i++)
                {
                    CampaignChapterDef def = chapters[i];
                    if (def == null) continue;
                    value.Append(def.ChapterId).Append('|').Append(def.Order).Append('|')
                        .Append(def.Mode).Append('|').Append(def.TitleCN).Append('|')
                        .Append(def.TitleEN).Append('|').Append(def.RewardCash).Append('|')
                        .Append(def.FacilityToken).Append('|').Append(def.ClueId).Append(';');
                    for (int j = 0; j < def.Objectives.Count; j++)
                    {
                        CampaignObjectiveDef obj = def.Objectives[j];
                        value.Append((int)obj.Kind).Append(':').Append(obj.Threshold).Append(':')
                            .Append(obj.DescCN).Append(':').Append(obj.DescEN).Append(',');
                    }
                    value.Append('#');
                }
            }
            return ModeHSeedStream.Fnv1a64(value.ToString()).ToString("x16");
        }

        #endregion

        #region 硬编码兜底

        private static List<CampaignChapterDef> BuildHardcodedChapters()
        {
            List<CampaignChapterDef> list = new List<CampaignChapterDef>();

            list.Add(MakeChapter(
                "ch1", 1, ModeStandard, "报个名", "Sign Us Up", 20000, 1, "clue_ch1",
                MakeObjective(CampaignObjectiveKind.StandardClear, 1,
                    "通关一局标准竞技场", "Clear one standard arena run"),
                MakeObjective(CampaignObjectiveKind.NoDamageUntilWave, 2,
                    "前 2 波一滴血不掉", "Take zero damage through wave 2")));

            list.Add(MakeChapter(
                "ch2", 2, ModeModeD, "种地的选手", "The Fighter With a Garden", 35000, 2, "clue_ch2",
                MakeObjective(CampaignObjectiveKind.GardenBuilt, 1,
                    "在基地建好菜地", "Build the garden at base"),
                MakeObjective(CampaignObjectiveKind.ReachWave, 5,
                    "白手起家打到第 5 波", "Reach wave 5 from nothing"),
                MakeObjective(CampaignObjectiveKind.MeleeKills, 5,
                    "用近战武器击杀 5 个", "Kill 5 with a melee weapon")));

            list.Add(MakeChapter(
                "ch3", 3, ModeModeE, "门面", "A Proper Front", 50000, 3, "clue_ch3",
                MakeObjective(CampaignObjectiveKind.FactionBossKills, 8,
                    "击败 8 名敌方头目", "Defeat 8 hostile bosses"),
                MakeObjective(CampaignObjectiveKind.TrophyDisplayed, 1,
                    "把 1 件 Boss 战利品摆上枪械展示架或假人", "Display 1 Boss trophy on a weapon rack or a dummy")));

            list.Add(MakeChapter(
                "ch4", 4, ModeModeF, "收钱走人", "Collect and Leave", 75000, 4, "clue_ch4",
                MakeObjective(CampaignObjectiveKind.BountyKills, 3,
                    "击杀 3 个带悬赏印记的目标", "Kill 3 targets marked for bounty"),
                MakeObjective(CampaignObjectiveKind.ModeExtract, 1,
                    "成功撤离", "Extract successfully")));

            list.Add(MakeChapter(
                "ch5", 5, ModeZombie, "没人肯去的那场", "The Match Nobody Takes", 100000, 5, "clue_ch5",
                MakeObjective(CampaignObjectiveKind.ReachWave, 5,
                    "在尸潮里撑到第 5 波", "Hold the tide to wave 5"),
                MakeObjective(CampaignObjectiveKind.ModeExtract, 1,
                    "成功撤离", "Extract successfully")));

            list.Add(MakeChapter(
                "ch6", 6, ModeFinal, "守擂的那个", "The One Holding the Ring", 200000, 6, "clue_ch6",
                MakeObjective(CampaignObjectiveKind.FinalBossKill, 1,
                    "打赢冠军之影", "Beat the Shadow of the Champion")));

            return list;
        }

        private static CampaignChapterDef MakeChapter(
            string id, int order, string mode, string titleCN, string titleEN,
            int rewardCash, int tokenChapter, string clueId,
            params CampaignObjectiveDef[] objectives)
        {
            CampaignChapterDef def = new CampaignChapterDef();
            def.ChapterId = id;
            def.Order = order;
            def.Mode = mode;
            def.TitleCN = titleCN;
            def.TitleEN = titleEN;
            def.RewardCash = rewardCash;
            def.FacilityToken = CampaignFacilityUnlocks.BuildTokenForChapter(tokenChapter);
            def.ClueId = clueId;
            if (objectives != null)
            {
                for (int i = 0; i < objectives.Length; i++)
                {
                    if (objectives[i] == null) continue;
                    def.Objectives.Add(objectives[i]);
                }
            }
            return def;
        }

        private static CampaignObjectiveDef MakeObjective(
            CampaignObjectiveKind kind, int threshold, string descCN, string descEN)
        {
            CampaignObjectiveDef obj = new CampaignObjectiveDef();
            obj.Kind = kind;
            obj.Threshold = threshold;
            obj.DescCN = descCN;
            obj.DescEN = descEN;
            return obj;
        }

        #endregion

        #region 清理

        internal static void ResetStaticCaches()
        {
            _chapters = null;
            _loadAttempted = false;
            _source = "Unloaded";
            _contentSignature = string.Empty;
        }

        #endregion
    }
}
