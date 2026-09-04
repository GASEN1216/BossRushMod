// ============================================================================
// PermanentDuckNpcData.cs - 永久捏脸 NPC 的扩展数据
// ============================================================================
// 模块说明：
//   一次性随机 NPC 只需要「长什么样」；永久 NPC 还需要名字、对话、送礼偏好。
//   这些全部作为蓝图里的一个 `permanent` 子对象存在 Assets/Data/DuckNpcs.json，
//   与 DuckNpcBlueprint 同一条记录。
//
//   **与羽织/叮当的结构差异**：那两位是「一个 NPC 一套 C# 类」
//   （NurseAffinityConfig 923 行、GoblinAffinityConfig 1175 行，约 90% 是对话字符串）。
//   永久捏脸 NPC 走「一套类 + N 份数据」：本文件是数据，
//   PermanentDuckNpcAffinityConfig 是唯一那套逻辑。
//   于是第二只、第三只永久 NPC 的增量仍然是「往 JSON 加一条」，而不是再写 900 行。
//
//   对话支持**按好感度分档**（羽织/叮当也是这么做的，是"像个真 NPC"的关键）：
//     "greeting": [
//        { "minLevel": 0, "lines": ["...", "..."] },
//        { "minLevel": 5, "lines": ["...", "..."] }
//     ]
//   取 minLevel 不超过当前等级的最高一档。
//
//   解析同样走 ModeHJsonParser，不用 Unity JsonUtility —— 理由见 DuckNpcBlueprint 文件头。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>一档按好感度分级的对话。</summary>
    internal sealed class PermanentDuckNpcDialogueTier
    {
        public int minLevel;
        public string[] lines;
    }

    /// <summary>永久捏脸 NPC 的扩展数据（蓝图里的 `permanent` 子对象）。</summary>
    internal sealed class PermanentDuckNpcData
    {
        // —— 身份 ——
        public string displayNameCn;
        public string displayNameEn;

        // —— 好感度 ——
        public int dailyChatAffinity;
        public int[] positiveItemTypeIds;
        public int[] negativeItemTypeIds;
        public string[] positiveTags;

        // —— 对话（按 category 分组，每组若干档）——
        private Dictionary<string, List<PermanentDuckNpcDialogueTier>> _dialogues;

        /// <summary>婚后专属台词，key 是 NPCDialogueSystem 喂进来的 eventKey。</summary>
        private Dictionary<string, string[]> _marriedDialogues;

        // —— 气泡 ——
        public string[] positiveBubbles;
        public string[] negativeBubbles;
        public string[] normalBubbles;

        public float dialogueBubbleHeight;
        public float defaultDialogueDuration;

        // ====================================================================
        // 查询
        // ====================================================================

        /// <summary>
        /// 取某个类别在指定等级下的一句随机台词。没有配就返回 null，
        /// 由调用方回落到 NPCDialogueSystem 的通用默认文案。
        /// </summary>
        internal string GetDialogue(string category, int level)
        {
            if (_dialogues == null || string.IsNullOrEmpty(category))
            {
                return null;
            }

            List<PermanentDuckNpcDialogueTier> tiers;
            if (!_dialogues.TryGetValue(category, out tiers) || tiers == null || tiers.Count == 0)
            {
                return null;
            }

            // 取 minLevel 不超过当前等级的最高一档；tiers 已在解析时按 minLevel 升序排好。
            PermanentDuckNpcDialogueTier chosen = null;
            for (int i = 0; i < tiers.Count; i++)
            {
                if (tiers[i].minLevel <= level)
                {
                    chosen = tiers[i];
                }
            }

            // 等级低于所有档位时用最低那档兜底，而不是什么都不说。
            if (chosen == null)
            {
                chosen = tiers[0];
            }

            return PickRandom(chosen.lines);
        }

        /// <summary>取婚后专属台词。没配返回 null（回落普通台词）。</summary>
        internal string GetMarriedDialogue(string eventKey)
        {
            if (_marriedDialogues == null || string.IsNullOrEmpty(eventKey))
            {
                return null;
            }

            string[] lines;
            if (!_marriedDialogues.TryGetValue(eventKey, out lines))
            {
                return null;
            }
            return PickRandom(lines);
        }

        private static string PickRandom(string[] lines)
        {
            if (lines == null || lines.Length == 0)
            {
                return null;
            }
            return lines[UnityEngine.Random.Range(0, lines.Length)];
        }

        // ====================================================================
        // 解析
        // ====================================================================

        /// <summary>
        /// 从蓝图行里的 `permanent` 子对象解析。缺该子对象返回 null。
        /// </summary>
        internal static PermanentDuckNpcData Parse(ModeHJsonValue row, string blueprintId)
        {
            ModeHJsonValue node;
            if (row == null || !row.TryGetObject("permanent", out node))
            {
                return null;
            }

            PermanentDuckNpcData data = new PermanentDuckNpcData();

            if (!node.TryGetString("displayNameCn", out data.displayNameCn))
            {
                data.displayNameCn = string.Empty;
            }
            if (!node.TryGetString("displayNameEn", out data.displayNameEn))
            {
                // 英文名缺省时退回中文名，好过显示空字符串
                data.displayNameEn = data.displayNameCn;
            }

            if (!node.TryGetInt("dailyChatAffinity", out data.dailyChatAffinity) || data.dailyChatAffinity < 0)
            {
                // 与羽织一致的默认值
                data.dailyChatAffinity = 30;
            }

            if (!node.TryGetFloat("dialogueBubbleHeight", out data.dialogueBubbleHeight)
                || data.dialogueBubbleHeight <= 0f)
            {
                data.dialogueBubbleHeight = 2.5f;
            }
            if (!node.TryGetFloat("defaultDialogueDuration", out data.defaultDialogueDuration)
                || data.defaultDialogueDuration <= 0f)
            {
                data.defaultDialogueDuration = 4f;
            }

            data.positiveItemTypeIds = ReadIntArray(node, "positiveItemTypeIds", blueprintId);
            data.negativeItemTypeIds = ReadIntArray(node, "negativeItemTypeIds", blueprintId);
            data.positiveTags = ReadStringArray(node, "positiveTags", blueprintId);
            data.positiveBubbles = ReadStringArray(node, "positiveBubbles", blueprintId);
            data.negativeBubbles = ReadStringArray(node, "negativeBubbles", blueprintId);
            data.normalBubbles = ReadStringArray(node, "normalBubbles", blueprintId);

            data._dialogues = ParseDialogues(node, blueprintId);
            data._marriedDialogues = ParseMarriedDialogues(node, blueprintId);

            return data;
        }

        private static Dictionary<string, List<PermanentDuckNpcDialogueTier>> ParseDialogues(
            ModeHJsonValue node, string blueprintId)
        {
            ModeHJsonValue dialogues;
            if (!node.TryGetObject("dialogues", out dialogues) || dialogues.Properties == null)
            {
                return null;
            }

            Dictionary<string, List<PermanentDuckNpcDialogueTier>> result =
                new Dictionary<string, List<PermanentDuckNpcDialogueTier>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < dialogues.Properties.Count; i++)
            {
                ModeHJsonProperty prop = dialogues.Properties[i];
                if (prop == null || string.IsNullOrEmpty(prop.Name) || prop.Value == null)
                {
                    continue;
                }

                List<PermanentDuckNpcDialogueTier> tiers = ParseTiers(prop.Value, blueprintId, prop.Name);
                if (tiers != null && tiers.Count > 0)
                {
                    result[prop.Name] = tiers;
                }
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// 一个 category 的值可以是两种形态：
        ///   1. 纯字符串数组         → 视作单一档位（minLevel = 0）
        ///   2. {minLevel, lines} 数组 → 多档位
        /// 两种都支持，简单 NPC 不必写档位包装。
        /// </summary>
        private static List<PermanentDuckNpcDialogueTier> ParseTiers(
            ModeHJsonValue value, string blueprintId, string category)
        {
            if (value.Kind != ModeHJsonKind.Array || value.Items == null || value.Items.Count == 0)
            {
                return null;
            }

            List<PermanentDuckNpcDialogueTier> tiers = new List<PermanentDuckNpcDialogueTier>();

            // 形态 1：整个数组都是字符串
            if (value.Items[0] != null && value.Items[0].Kind == ModeHJsonKind.String)
            {
                List<string> lines = new List<string>(value.Items.Count);
                for (int i = 0; i < value.Items.Count; i++)
                {
                    ModeHJsonValue item = value.Items[i];
                    if (item != null && item.Kind == ModeHJsonKind.String)
                    {
                        lines.Add(item.StringValue);
                    }
                }
                if (lines.Count == 0)
                {
                    return null;
                }

                PermanentDuckNpcDialogueTier flat = new PermanentDuckNpcDialogueTier();
                flat.minLevel = 0;
                flat.lines = lines.ToArray();
                tiers.Add(flat);
                return tiers;
            }

            // 形态 2：{minLevel, lines} 对象数组
            for (int i = 0; i < value.Items.Count; i++)
            {
                ModeHJsonValue item = value.Items[i];
                if (item == null || item.Kind != ModeHJsonKind.Object)
                {
                    continue;
                }

                PermanentDuckNpcDialogueTier tier = new PermanentDuckNpcDialogueTier();
                if (!item.TryGetInt("minLevel", out tier.minLevel))
                {
                    tier.minLevel = 0;
                }

                List<string> lines;
                if (!item.TryGetStringList("lines", out lines) || lines.Count == 0)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId
                        + " 的对话 " + category + " 有一档缺少 lines，已跳过");
                    continue;
                }

                tier.lines = lines.ToArray();
                tiers.Add(tier);
            }

            if (tiers.Count == 0)
            {
                return null;
            }

            // 按 minLevel 升序，GetDialogue 依赖这个顺序取"不超过当前等级的最高一档"
            tiers.Sort(CompareTierByLevel);
            return tiers;
        }

        private static int CompareTierByLevel(PermanentDuckNpcDialogueTier a, PermanentDuckNpcDialogueTier b)
        {
            return a.minLevel.CompareTo(b.minLevel);
        }

        private static Dictionary<string, string[]> ParseMarriedDialogues(
            ModeHJsonValue node, string blueprintId)
        {
            ModeHJsonValue married;
            if (!node.TryGetObject("marriedDialogues", out married) || married.Properties == null)
            {
                return null;
            }

            Dictionary<string, string[]> result =
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < married.Properties.Count; i++)
            {
                ModeHJsonProperty prop = married.Properties[i];
                if (prop == null || string.IsNullOrEmpty(prop.Name))
                {
                    continue;
                }

                List<string> lines;
                if (!married.TryGetStringList(prop.Name, out lines) || lines.Count == 0)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId
                        + " 的婚后台词 " + prop.Name + " 不是非空字符串数组，已跳过");
                    continue;
                }
                result[prop.Name] = lines.ToArray();
            }

            return result.Count > 0 ? result : null;
        }

        private static int[] ReadIntArray(ModeHJsonValue node, string key, string blueprintId)
        {
            if (node.GetProperty(key) == null)
            {
                return null;
            }

            List<ModeHJsonValue> items;
            if (!node.TryGetArray(key, out items))
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId + " 的 " + key + " 不是数组，已忽略");
                return null;
            }

            List<int> result = new List<int>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Integer)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId
                        + " 的 " + key + " 含非整数项，已忽略整个数组");
                    return null;
                }
                result.Add((int)item.IntegerValue);
            }
            return result.ToArray();
        }

        private static string[] ReadStringArray(ModeHJsonValue node, string key, string blueprintId)
        {
            if (node.GetProperty(key) == null)
            {
                return null;
            }

            List<string> values;
            if (!node.TryGetStringList(key, out values))
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId
                    + " 的 " + key + " 不是字符串数组，已忽略");
                return null;
            }
            return values.ToArray();
        }
    }
}
