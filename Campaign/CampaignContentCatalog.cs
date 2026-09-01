// ============================================================================
// CampaignContentCatalog.cs - 鸭王征程章节内容目录
// ============================================================================
// 归位依据 AGENTS.md 4.8 第 3 层：大型数据表走 Assets/Data/Campaign/*.json
// + Registry + **全量硬编码 fallback**。
//
// 【为什么必须有硬编码 fallback】
//   数据表读不到就没有章节，玩家点开公告板会看到空面板——这是玩家可见故障。
//   硬编码兜底保证「即使 JSON 丢了/坏了，战役照样能玩」，JSON 只负责让策划改数值
//   不用重新编译。校验不过时**整表回退**，不做逐条挑拣：半张表比没有表更难排查。
//
// 【数值均为草案，待 owner 审定】
//   章节奖金 2 万 → 20 万递增（参照成就系统约 1676 万的总奖金量级取的保守值）。
//   目标阈值取自方案的六章设计。改动只需改本文件或 JSON，不影响任何结构。
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
                ModeHJsonValue root;
                string parseError;
                if (!ModeHCanonicalDigest.TryParse(json, out root, out parseError)
                    || root == null || root.Kind != ModeHJsonKind.Object)
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

                List<ModeHJsonValue> chapterRows;
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
                    ModeHJsonValue row = chapterRows[i];
                    string chapterId;
                    if (row == null || row.Kind != ModeHJsonKind.Object
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

                    List<ModeHJsonValue> objectiveRows;
                    if (row.TryGetArray("objectives", out objectiveRows) && objectiveRows != null)
                    {
                        for (int j = 0; j < objectiveRows.Count; j++)
                        {
                            ModeHJsonValue objRow = objectiveRows[j];
                            if (objRow == null || objRow.Kind != ModeHJsonKind.Object)
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
                "ch1", 1, ModeStandard, "擂台旧影", "Echoes of the Ring", 20000, 1, "clue_ch1",
                MakeObjective(CampaignObjectiveKind.StandardClear, 1,
                    "通关一局标准竞技场", "Clear one standard arena run"),
                MakeObjective(CampaignObjectiveKind.NoDamageUntilWave, 2,
                    "前 2 波一滴血不掉", "Take zero damage through wave 2")));

            list.Add(MakeChapter(
                "ch2", 2, ModeModeD, "白手起家的誓言", "Vow of the Empty-Handed", 35000, 2, "clue_ch2",
                MakeObjective(CampaignObjectiveKind.ReachWave, 5,
                    "裸装打到第 5 波", "Reach wave 5 from nothing"),
                MakeObjective(CampaignObjectiveKind.MeleeKills, 5,
                    "近战送走 5 个", "Put down 5 with melee")));

            list.Add(MakeChapter(
                "ch3", 3, ModeModeE, "立旗为界", "Planting the Banner", 50000, 3, "clue_ch3",
                MakeObjective(CampaignObjectiveKind.FactionBossKills, 8,
                    "击败 8 名头目", "Defeat 8 bosses"),
                MakeObjective(CampaignObjectiveKind.SurviveMinutes, 10,
                    "在场上撑满 10 分钟", "Stay on the field a full 10 minutes")));

            list.Add(MakeChapter(
                "ch4", 4, ModeModeF, "猎杀名单", "The Kill List", 75000, 4, "clue_ch4",
                MakeObjective(CampaignObjectiveKind.BountyKills, 3,
                    "拿下 3 个带悬赏印记的", "Take down 3 marked for bounty"),
                MakeObjective(CampaignObjectiveKind.ModeExtract, 1,
                    "成功撤离", "Extract successfully")));

            list.Add(MakeChapter(
                "ch5", 5, ModeZombie, "末日信标", "The Last Beacon", 100000, 5, "clue_ch5",
                MakeObjective(CampaignObjectiveKind.ReachWave, 4,
                    "在尸潮里熬到第 4 波", "Ride the tide to wave 4"),
                MakeObjective(CampaignObjectiveKind.ModeExtract, 1,
                    "成功撤离", "Extract successfully")));

            list.Add(MakeChapter(
                "ch6", 6, ModeFinal, "冠军之影", "Shadow of the Champion", 200000, 6, "clue_ch6",
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
