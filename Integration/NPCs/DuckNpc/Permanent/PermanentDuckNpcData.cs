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
//   解析同样走 BossRushJsonParser，不用 Unity JsonUtility —— 理由见 DuckNpcBlueprint 文件头。
//
//   **台词的中英对照（SCHEMA+，CR-2026-09-12-016）**：每一句既可以是裸字符串（只有中文，老写法），
//   也可以是 {"cn": "...", "en": "..."}。两种形态在同一个数组里可以混用：
//     "lines": ["只有中文的一句", {"cn": "中文", "en": "English"}]
//   老数据一个字不用改、行为完全不变（缺 en 时 L10n.T 自动回落中文）；新数据把两半写在一起，
//   漏译一句是肉眼可见的，和全 Mod 其它 2700 处 `L10n.T(cn, en)` 是同一种形状。
//   语言在**取用时**解析而不是解析时定死：`LocalizationManager.CurrentLanguage` 可以在游戏里切，
//   而蓝图只在载入时解析一次，定死会让切语言之后台词还是旧语言。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 一句台词的中英两半。英文缺省时 <see cref="L10n.T(string,string)"/> 自动回落中文，
    /// 所以老蓝图（只写中文裸串）的行为一个字不变。
    /// </summary>
    internal sealed class PermanentDuckNpcLine
    {
        public string cn;
        public string en;

        /// <summary>按**当前**语言取这一句。不缓存：游戏里可以切语言，缓存会把台词钉死在旧语言上。</summary>
        internal string Text { get { return L10n.T(cn, en); } }

        internal static PermanentDuckNpcLine Of(string chinese, string english)
        {
            PermanentDuckNpcLine line = new PermanentDuckNpcLine();
            line.cn = chinese;
            line.en = english;
            return line;
        }
    }

    /// <summary>一档按好感度分级的对话。</summary>
    internal sealed class PermanentDuckNpcDialogueTier
    {
        public int minLevel;
        public PermanentDuckNpcLine[] lines;
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
        private Dictionary<string, PermanentDuckNpcLine[]> _marriedDialogues;

        // —— 气泡（中英对照；对外仍是 string[]，见下面三个属性）——
        public PermanentDuckNpcLine[] positiveBubbleLines;
        public PermanentDuckNpcLine[] negativeBubbleLines;
        public PermanentDuckNpcLine[] normalBubbleLines;

        // 气泡整组交给官方气泡系统，所以这里必须给出 string[]。按语言缓存一份：
        // 送礼事件才会读，频率很低，但每次都新建数组没有必要；语言切换时 _bubbleChinese 不符即重建。
        private string[] _positiveBubbles, _negativeBubbles, _normalBubbles;
        private bool _bubbleChinese;
        private bool _bubblesBuilt;

        /// <summary>按当前语言取气泡整组。语言变了就重建，不会把文本钉死在旧语言上。</summary>
        public string[] PositiveBubbles { get { EnsureBubbles(); return _positiveBubbles; } }
        public string[] NegativeBubbles { get { EnsureBubbles(); return _negativeBubbles; } }
        public string[] NormalBubbles { get { EnsureBubbles(); return _normalBubbles; } }

        private void EnsureBubbles()
        {
            bool chinese = L10n.IsChinese;
            if (_bubblesBuilt && chinese == _bubbleChinese)
            {
                return;
            }
            _bubbleChinese = chinese;
            _bubblesBuilt = true;
            _positiveBubbles = Localize(positiveBubbleLines);
            _negativeBubbles = Localize(negativeBubbleLines);
            _normalBubbles = Localize(normalBubbleLines);
        }

        private static string[] Localize(PermanentDuckNpcLine[] lines)
        {
            if (lines == null || lines.Length == 0)
            {
                return null;
            }
            string[] result = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                result[i] = lines[i] == null ? string.Empty : lines[i].Text;
            }
            return result;
        }

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

            PermanentDuckNpcLine[] lines;
            if (!_marriedDialogues.TryGetValue(eventKey, out lines))
            {
                return null;
            }
            return PickRandom(lines);
        }

        private static string PickRandom(PermanentDuckNpcLine[] lines)
        {
            if (lines == null || lines.Length == 0)
            {
                return null;
            }
            PermanentDuckNpcLine line = lines[UnityEngine.Random.Range(0, lines.Length)];
            return line == null ? null : line.Text;
        }

        // ====================================================================
        // 解析
        // ====================================================================

        /// <summary>
        /// 从蓝图行里的 `permanent` 子对象解析。缺该子对象返回 null。
        /// </summary>
        internal static PermanentDuckNpcData Parse(BossRushJsonValue row, string blueprintId)
        {
            BossRushJsonValue node;
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
            data.positiveBubbleLines = ReadLineArray(node, "positiveBubbles", blueprintId);
            data.negativeBubbleLines = ReadLineArray(node, "negativeBubbles", blueprintId);
            data.normalBubbleLines = ReadLineArray(node, "normalBubbles", blueprintId);

            data._dialogues = ParseDialogues(node, blueprintId);
            data._marriedDialogues = ParseMarriedDialogues(node, blueprintId);

            return data;
        }

        private static Dictionary<string, List<PermanentDuckNpcDialogueTier>> ParseDialogues(
            BossRushJsonValue node, string blueprintId)
        {
            BossRushJsonValue dialogues;
            if (!node.TryGetObject("dialogues", out dialogues) || dialogues.Properties == null)
            {
                return null;
            }

            Dictionary<string, List<PermanentDuckNpcDialogueTier>> result =
                new Dictionary<string, List<PermanentDuckNpcDialogueTier>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < dialogues.Properties.Count; i++)
            {
                BossRushJsonProperty prop = dialogues.Properties[i];
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
            BossRushJsonValue value, string blueprintId, string category)
        {
            if (value.Kind != BossRushJsonKind.Array || value.Items == null || value.Items.Count == 0)
            {
                return null;
            }

            List<PermanentDuckNpcDialogueTier> tiers = new List<PermanentDuckNpcDialogueTier>();

            // 形态 1：整个数组都是台词（裸字符串，或 {cn, en} 对照）。
            // 判据用「第一项不是带 lines 的档位对象」而不是「第一项是字符串」：
            // 只认字符串的话，一个写成 [{"cn":..,"en":..}] 的单档会被当成形态 2，逐项找不到 minLevel/lines 而整组丢失。
            if (!IsTierObject(value.Items[0]))
            {
                PermanentDuckNpcLine[] flatLines = ReadLines(value.Items, blueprintId, category);
                if (flatLines == null)
                {
                    return null;
                }

                PermanentDuckNpcDialogueTier flat = new PermanentDuckNpcDialogueTier();
                flat.minLevel = 0;
                flat.lines = flatLines;
                tiers.Add(flat);
                return tiers;
            }

            // 形态 2：{minLevel, lines} 对象数组
            for (int i = 0; i < value.Items.Count; i++)
            {
                BossRushJsonValue item = value.Items[i];
                if (item == null || item.Kind != BossRushJsonKind.Object)
                {
                    continue;
                }

                PermanentDuckNpcDialogueTier tier = new PermanentDuckNpcDialogueTier();
                if (!item.TryGetInt("minLevel", out tier.minLevel))
                {
                    tier.minLevel = 0;
                }

                List<BossRushJsonValue> items;
                PermanentDuckNpcLine[] lines = null;
                if (item.TryGetArray("lines", out items))
                {
                    lines = ReadLines(items, blueprintId, category);
                }
                if (lines == null)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId
                        + " 的对话 " + category + " 有一档缺少 lines，已跳过");
                    continue;
                }

                tier.lines = lines;
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

        private static Dictionary<string, PermanentDuckNpcLine[]> ParseMarriedDialogues(
            BossRushJsonValue node, string blueprintId)
        {
            BossRushJsonValue married;
            if (!node.TryGetObject("marriedDialogues", out married) || married.Properties == null)
            {
                return null;
            }

            Dictionary<string, PermanentDuckNpcLine[]> result =
                new Dictionary<string, PermanentDuckNpcLine[]>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < married.Properties.Count; i++)
            {
                BossRushJsonProperty prop = married.Properties[i];
                if (prop == null || string.IsNullOrEmpty(prop.Name))
                {
                    continue;
                }

                List<BossRushJsonValue> items;
                PermanentDuckNpcLine[] lines = null;
                if (married.TryGetArray(prop.Name, out items))
                {
                    lines = ReadLines(items, blueprintId, "marriedDialogues." + prop.Name);
                }
                if (lines == null)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId
                        + " 的婚后台词 " + prop.Name + " 不是非空台词数组，已跳过");
                    continue;
                }
                result[prop.Name] = lines;
            }

            return result.Count > 0 ? result : null;
        }

        private static int[] ReadIntArray(BossRushJsonValue node, string key, string blueprintId)
        {
            if (node.GetProperty(key) == null)
            {
                return null;
            }

            List<BossRushJsonValue> items;
            if (!node.TryGetArray(key, out items))
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId + " 的 " + key + " 不是数组，已忽略");
                return null;
            }

            List<int> result = new List<int>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                BossRushJsonValue item = items[i];
                if (item == null || item.Kind != BossRushJsonKind.Integer)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId
                        + " 的 " + key + " 含非整数项，已忽略整个数组");
                    return null;
                }
                result.Add((int)item.IntegerValue);
            }
            return result.ToArray();
        }

        /// <summary>
        /// 这一项是不是「档位对象」（{minLevel, lines}）而不是一句台词。
        /// 台词的对照形态也是对象（{cn, en}），所以必须按 `lines` 这个键区分，不能只看 Kind。
        /// </summary>
        private static bool IsTierObject(BossRushJsonValue item)
        {
            return item != null && item.Kind == BossRushJsonKind.Object && item.GetProperty("lines") != null;
        }

        /// <summary>
        /// 读一句台词：裸字符串就是「只有中文」，{cn, en} 是中英对照。
        /// 两种都不是（数字、null、空对象）返回 null，由调用方跳过并记一行 DevLog。
        /// </summary>
        private static PermanentDuckNpcLine ReadLine(BossRushJsonValue item)
        {
            if (item == null)
            {
                return null;
            }
            if (item.Kind == BossRushJsonKind.String)
            {
                return string.IsNullOrEmpty(item.StringValue) ? null : PermanentDuckNpcLine.Of(item.StringValue, null);
            }
            if (item.Kind != BossRushJsonKind.Object)
            {
                return null;
            }
            string chinese, english;
            if (!item.TryGetString("cn", out chinese) || string.IsNullOrEmpty(chinese))
            {
                return null;
            }
            if (!item.TryGetString("en", out english))
            {
                english = null;
            }
            return PermanentDuckNpcLine.Of(chinese, english);
        }

        /// <summary>读一整组台词。一句都读不出来时返回 null（调用方据此跳过整组）。</summary>
        private static PermanentDuckNpcLine[] ReadLines(List<BossRushJsonValue> items, string blueprintId, string label)
        {
            if (items == null || items.Count == 0)
            {
                return null;
            }
            List<PermanentDuckNpcLine> lines = new List<PermanentDuckNpcLine>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                PermanentDuckNpcLine line = ReadLine(items[i]);
                if (line == null)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId + " 的 " + label
                        + " 第 " + (i + 1) + " 项既不是字符串也不是 {cn, en}，已跳过这一句");
                    continue;
                }
                lines.Add(line);
            }
            return lines.Count > 0 ? lines.ToArray() : null;
        }

        /// <summary>读一组台词字段（气泡）。缺该键返回 null，与 <see cref="ReadStringArray"/> 同一条口径。</summary>
        private static PermanentDuckNpcLine[] ReadLineArray(BossRushJsonValue node, string key, string blueprintId)
        {
            if (node.GetProperty(key) == null)
            {
                return null;
            }

            List<BossRushJsonValue> items;
            if (!node.TryGetArray(key, out items))
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId + " 的 " + key + " 不是数组，已忽略");
                return null;
            }
            return ReadLines(items, blueprintId, key);
        }

        private static string[] ReadStringArray(BossRushJsonValue node, string key, string blueprintId)
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
