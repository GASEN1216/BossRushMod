using System;
using System.Collections;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Mode H 编译期验证 harness（设计提案 §29.14、§25.1）。
    ///
    /// **它由编译期常量 `ModeHAvailability.AllowDevControlPointHarness` 门控，
    /// 发布构建恒为 false。** 只在实现者本地临时置 `true` 时暴露一个只读诊断入口，
    /// 用来在写完上层逻辑之前先回答这类问题：
    /// - 这个 stable key 的 `sightDistance` 到底有没有生效？
    /// - `center` 的目标保持率是多少？
    /// - 官方物品表里符合某组 tag/品质的 typeId 具体是哪些？
    ///
    /// 硬边界（每一条都由 ModeHPresetEligibilityGuard / ModeHIsolationGuard 覆盖）：
    /// - 不创建 Season、不写存档、不发奖励、不消耗船票；
    /// - 不提供任何玩法路径，因此不构成向 owner 交付样机；
    /// - 只复用生产认证与口令适配层的既有设施，不新建第二套采集通道；
    /// - 全部结果只写日志或本地文件，绝不回写内容 JSON 或认证缓存。
    /// </summary>
    internal static class ModeHControlPointHarness
    {
        #region 门

        /// <summary>harness 是否可用。发布构建恒 false。</summary>
        public static bool IsAvailable
        {
            get { return ModeHAvailability.AllowDevControlPointHarness; }
        }

        private static bool RejectIfUnavailable(out string failureReasonId)
        {
            if (IsAvailable)
            {
                failureReasonId = null;
                return false;
            }
            failureReasonId = "harness_disabled";
            return true;
        }

        #endregion

        #region 控制点保持率

        /// <summary>
        /// 对一名已生成的隔离角色施加一条口令，按重申周期采样，
        /// 报告每条 effect 的保持率。只读：不改比赛状态、不写任何事实。
        /// </summary>
        public static IEnumerator ProbeCommandHoldRate(
            AICharacterController ai,
            string stableKey,
            string commandId,
            float durationSeconds,
            List<string> report)
        {
            string failureReasonId;
            if (RejectIfUnavailable(out failureReasonId))
            {
                if (report != null) report.Add("harness disabled: " + failureReasonId);
                yield break;
            }
            if (ai == null || report == null) yield break;

            ModeHCommandSpec spec = FindCommand(commandId);
            if (spec == null)
            {
                report.Add("command not found: " + commandId);
                yield break;
            }

            // 复用生产用的同一套 adapter，不新建第二条字段修改路径
            ModeHCommandAdapter adapter = new ModeHCommandAdapter();
            ModeHCommandFireContext context = new ModeHCommandFireContext();
            string applyFailure;
            if (!adapter.Apply(ai, spec, 1f, context, out applyFailure))
            {
                report.Add("apply failed: " + applyFailure);
                yield break;
            }

            Dictionary<string, int> heldSamples = new Dictionary<string, int>(StringComparer.Ordinal);
            int totalSamples = 0;
            float elapsed = 0f;
            while (elapsed < durationSeconds && adapter.IsActive)
            {
                adapter.Tick(Time.deltaTime, context);
                List<string> held = adapter.Validate();
                for (int i = 0; i < held.Count; i++)
                {
                    int count;
                    heldSamples.TryGetValue(held[i], out count);
                    heldSamples[held[i]] = count + 1;
                }
                totalSamples++;
                elapsed += Time.deltaTime;
                yield return null;
            }
            adapter.Restore();

            List<string> effectIds = new List<string>(heldSamples.Keys);
            effectIds.Sort(StringComparer.Ordinal);
            report.Add("[" + stableKey + "] " + commandId + "  samples=" + totalSamples);
            for (int i = 0; i < effectIds.Count; i++)
            {
                int held = heldSamples[effectIds[i]];
                int percent = totalSamples > 0 ? held * 100 / totalSamples : 0;
                report.Add("  " + effectIds[i] + "  hold=" + percent + "%");
            }
        }

        private static ModeHCommandSpec FindCommand(string commandId)
        {
            List<ModeHCommandSpec> commands = ModeHContentCatalog.Commands;
            if (commands == null || string.IsNullOrEmpty(commandId)) return null;
            for (int i = 0; i < commands.Count; i++)
            {
                if (commands[i] != null
                    && string.Equals(commands[i].CommandId, commandId, StringComparison.Ordinal))
                {
                    return commands[i];
                }
            }
            return null;
        }

        #endregion

        #region 单点读数

        /// <summary>
        /// 直接读一次 §17.6.2 白名单控制点的当前值，用来确认某个写入是否被行为树抹掉。
        /// 只读：不写任何字段。
        /// </summary>
        public static string ReadControlPoint(AICharacterController ai, string controlPointId)
        {
            string failureReasonId;
            if (RejectIfUnavailable(out failureReasonId)) return failureReasonId;
            if (ai == null) return "ai_null";

            try
            {
                switch (controlPointId)
                {
                    case "skillSuccessChance": return ai.skillSuccessChance.ToString();
                    case "itemSkillChance": return ai.itemSkillChance.ToString();
                    case "itemSkillCoolTime": return ai.itemSkillCoolTime.ToString();
                    case "sightDistance": return ai.sightDistance.ToString();
                    case "sightAngle": return ai.sightAngle.ToString();
                    case "combatTurnSpeed": return ai.combatTurnSpeed.ToString();
                    case "patrolTurnSpeed": return ai.patrolTurnSpeed.ToString();
                    case "baseReactionTime": return ai.baseReactionTime.ToString();
                    case "shootCanMove": return ai.shootCanMove.ToString();
                    case "skillCoolTimeRange": return ai.skillCoolTimeRange.ToString();
                    case "nextReleaseSkillTimeMarker": return ai.nextReleaseSkillTimeMarker.ToString();
                    case "searchedEnemy": return ai.searchedEnemy != null ? "set" : "null";
                    case "setNoticedToTarget": return ai.noticed.ToString();
                    default: return "not_whitelisted";
                }
            }
            catch (Exception e)
            {
                return "read_exception:" + e.GetType().Name;
            }
        }

        #endregion

        #region 物品目录 dump

        /// <summary>
        /// 只读导出官方物品目录中与 Mode H 虚拟整备相关的条目
        /// （typeId / 品质 / tag），用于把 `LoadoutKits.json` 的固定 typeId 与
        /// 当前游戏版本重新核对。写日志，不写任何 Mod 数据文件。
        /// </summary>
        public static List<string> DumpKitCatalog(IList<string> requireTags, int minQuality, int maxQuality)
        {
            List<string> lines = new List<string>();
            string failureReasonId;
            if (RejectIfUnavailable(out failureReasonId))
            {
                lines.Add("harness disabled: " + failureReasonId);
                return lines;
            }

            try
            {
                if (ItemAssetsCollection.Instance == null)
                {
                    lines.Add("item_assets_not_ready");
                    return lines;
                }

                ItemFilter filter = new ItemFilter();
                filter.requireTags = ResolveTags(requireTags);
                filter.minQuality = minQuality;
                filter.maxQuality = maxQuality;
                filter.caliber = string.Empty;

                int[] typeIds = ItemAssetsCollection.Search(filter);
                if (typeIds == null)
                {
                    lines.Add("search_returned_null");
                    return lines;
                }

                List<int> sorted = new List<int>(typeIds);
                sorted.Sort();
                for (int i = 0; i < sorted.Count; i++)
                {
                    int typeId = sorted[i];
                    int quality = -1;
                    try
                    {
                        // ItemMetaData 是值类型：用 id 判定有效性，不能与 null 比较
                        ItemMetaData metaData = ItemAssetsCollection.GetMetaData(typeId);
                        if (metaData.id > 0) quality = metaData.quality;
                    }
                    catch (Exception)
                    {
                        // 单条读不到就标 -1，不中断整份 dump
                        quality = -1;
                    }
                    lines.Add(typeId + "\tQ" + quality);
                }
            }
            catch (Exception e)
            {
                lines.Add("dump_exception:" + e.GetType().Name);
            }
            return lines;
        }

        private static Tag[] ResolveTags(IList<string> tagNames)
        {
            if (tagNames == null || tagNames.Count == 0) return null;
            List<Tag> resolved = new List<Tag>();
            try
            {
                if (GameplayDataSettings.Tags == null || GameplayDataSettings.Tags.AllTags == null)
                {
                    return null;
                }
                for (int i = 0; i < tagNames.Count; i++)
                {
                    foreach (Tag tag in GameplayDataSettings.Tags.AllTags)
                    {
                        if (tag != null
                            && string.Equals(tag.name, tagNames[i], StringComparison.Ordinal))
                        {
                            if (!resolved.Contains(tag)) resolved.Add(tag);
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 标签表不可用：返回 null 让调用方看到 search_returned_null
                return null;
            }
            return resolved.Count > 0 ? resolved.ToArray() : null;
        }

        #endregion
    }
}
