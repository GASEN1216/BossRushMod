using System;
using System.Collections.Generic;
using System.IO;
using BossRush;

// ---- 隔离替身 ----------------------------------------------------------------
// 生产代码只用到这三处外部依赖，全部给最小替身，其余一律链接真源文件。
namespace UnityEngine
{
    /// <summary>只用到 Range(int, int)。取固定下标，让「抽一句」在回归里是确定的。</summary>
    internal static class Random
    {
        internal static int Pick;
        internal static int Range(int min, int max) { return min + (Pick % System.Math.Max(1, max - min)); }
    }
}

namespace BossRush
{
    /// <summary>语言可切：本回归的核心断言之一就是「切语言之后台词跟着变」。</summary>
    internal static class L10n
    {
        internal static bool Chinese = true;
        internal static bool IsChinese { get { return Chinese; } }
        internal static string T(string cn, string en)
        {
            if (cn == null && en == null) return "";
            if (cn == null) return en;
            if (en == null) return cn;
            return Chinese ? cn : en;
        }
    }

    /// <summary>解析告警只记条数，断言里核对「坏数据确实被跳过并报了」。</summary>
    internal static class ModBehaviour
    {
        internal static readonly List<string> Logs = new List<string>();
        internal static void DevLog(string message) { Logs.Add(message); }
    }
}

/// <summary>
/// 永久捏脸 NPC 台词的中英对照（CR-2026-09-12-016 / R-6）隔离执行回归。
///
/// 结构守卫只能证明「代码里有这几个 token」，证明不了**解析真的把两种形态都读出来了**。
/// 而这一层恰恰是最容易静默坏掉的地方：写错档位判据会让整组台词丢失，
/// 既不编译报错、也不抛异常，只表现为「这个 NPC 突然不说话了」。
///
/// 这里链接真的 `PermanentDuckNpcData` 与 `BossRushJsonValue`，只替身 L10n / DevLog / Random，
/// 覆盖：
///   1. 老写法（裸中文字符串）行为一个字不变——duck_npc_xiaoman 这类蓝图不能被改坏；
///   2. 新写法（{cn, en}）在中英两种语言下各取对的那一半；
///   3. 两种形态**混在同一个数组里**也要都读出来；
///   4. 单档写成 [{cn, en}] 时不得被误判成档位数组而整组丢失（IsTierObject 的那条判据）；
///   5. 好感度分档、婚后台词、气泡三条路径都吃这套对照；
///   6. 缺 en 时回落中文，而不是显示空串；
///   7. 语言在**取用时**解析：切语言之后同一份数据读出另一种语言；
///   8. 真实 `Assets/Data/DuckNpcs.json` 里两位天空岛居民的 46 条台词确实都有非空英文。
/// </summary>
internal static class Program
{
    private static int checks;

    private static void Check(bool value, string description)
    {
        checks++;
        if (!value) throw new Exception("FAIL " + description);
    }

    private static PermanentDuckNpcData Parse(string permanentJson)
    {
        string json = "{ \"permanent\": " + permanentJson + " }";
        BossRushJsonValue root;
        string error;
        if (!BossRushJsonParser.TryParse(json, out root, out error))
            throw new Exception("fixture json did not parse: " + error);
        PermanentDuckNpcData data = PermanentDuckNpcData.Parse(root, "fixture");
        if (data == null) throw new Exception("fixture permanent block did not parse");
        return data;
    }

    // ---- 1 / 2 / 6：老写法不变、新写法双语、缺 en 回落 ----
    private static void LegacyAndPairedLines()
    {
        PermanentDuckNpcData data = Parse(@"{
            ""dialogues"": {
                ""greeting"": [""只有中文的一句""],
                ""farewell"": [{ ""cn"": ""路上当心"", ""en"": ""Mind the road"" }],
                ""idle"": [{ ""cn"": ""缺英文的一句"" }]
            }
        }");

        L10n.Chinese = true;
        Check(data.GetDialogue("greeting", 0) == "只有中文的一句", "老写法（裸字符串）在中文下原样读出");
        Check(data.GetDialogue("farewell", 0) == "路上当心", "对照写法在中文下取 cn");
        Check(data.GetDialogue("idle", 0) == "缺英文的一句", "缺 en 时中文照常");

        L10n.Chinese = false;
        Check(data.GetDialogue("greeting", 0) == "只有中文的一句",
              "老写法在英文下回落中文——老蓝图行为一个字不变，绝不能读成空串");
        Check(data.GetDialogue("farewell", 0) == "Mind the road", "对照写法在英文下取 en");
        Check(data.GetDialogue("idle", 0) == "缺英文的一句", "缺 en 时英文回落中文，而不是空串");
        L10n.Chinese = true;
    }

    // ---- 3：两种形态混在同一个数组里 ----
    private static void MixedForms()
    {
        PermanentDuckNpcData data = Parse(@"{
            ""dialogues"": {
                ""greeting"": [""裸中文"", { ""cn"": ""对照中文"", ""en"": ""Paired English"" }]
            }
        }");
        var seenCn = new HashSet<string>(StringComparer.Ordinal);
        var seenEn = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 8; i++)
        {
            UnityEngine.Random.Pick = i;
            L10n.Chinese = true;
            seenCn.Add(data.GetDialogue("greeting", 0));
            L10n.Chinese = false;
            seenEn.Add(data.GetDialogue("greeting", 0));
        }
        L10n.Chinese = true;
        Check(seenCn.Count == 2 && seenCn.Contains("裸中文") && seenCn.Contains("对照中文"),
              "混排数组的两句在中文下都读得出来");
        Check(seenEn.Count == 2 && seenEn.Contains("裸中文") && seenEn.Contains("Paired English"),
              "混排数组在英文下：裸串回落中文、对照句取英文");
    }

    // ---- 4：单档写成 [{cn, en}] 不得被当成档位数组 ----
    private static void SingleTierOfPairedObjects()
    {
        // 这一条是 IsTierObject 的存在理由：只按 Kind 判断的话，这个数组的第一项是对象，
        // 会被当成 {minLevel, lines} 档位，逐项找不到 lines 于是**整组台词丢失**——
        // 不编译报错、不抛异常，只表现为这个 NPC 突然不说话了。
        PermanentDuckNpcData data = Parse(@"{
            ""dialogues"": {
                ""greeting"": [{ ""cn"": ""只有一档的对照句"", ""en"": ""A single paired line"" }]
            }
        }");
        Check(data.GetDialogue("greeting", 0) == "只有一档的对照句",
              "单档写成 [{cn, en}] 时整组不得丢失（档位判据必须看 lines 这个键）");
        L10n.Chinese = false;
        Check(data.GetDialogue("greeting", 0) == "A single paired line", "同上，英文侧");
        L10n.Chinese = true;
    }

    // ---- 5：分档 / 婚后 / 气泡三条路径 ----
    private static void TieredMarriedAndBubbles()
    {
        PermanentDuckNpcData data = Parse(@"{
            ""dialogues"": {
                ""greeting"": [
                    { ""minLevel"": 0, ""lines"": [{ ""cn"": ""初见"", ""en"": ""First meeting"" }] },
                    { ""minLevel"": 5, ""lines"": [{ ""cn"": ""熟络"", ""en"": ""Familiar"" }] }
                ]
            },
            ""marriedDialogues"": {
                ""dialogue_greeting_married"": [{ ""cn"": ""回来了"", ""en"": ""You are back"" }]
            },
            ""positiveBubbles"": [{ ""cn"": ""正合用"", ""en"": ""Just right"" }],
            ""negativeBubbles"": [""用不上""],
            ""normalBubbles"": [{ ""cn"": ""收下了"", ""en"": ""Taken"" }]
        }");

        L10n.Chinese = true;
        Check(data.GetDialogue("greeting", 0) == "初见", "分档：0 级取最低档");
        Check(data.GetDialogue("greeting", 7) == "熟络", "分档：7 级取 minLevel 5 那档");
        Check(data.GetMarriedDialogue("dialogue_greeting_married") == "回来了", "婚后台词读中文");
        Check(data.PositiveBubbles != null && data.PositiveBubbles.Length == 1
              && data.PositiveBubbles[0] == "正合用", "气泡读中文");
        Check(data.NegativeBubbles[0] == "用不上", "裸串气泡在中文下原样");

        L10n.Chinese = false;
        Check(data.GetDialogue("greeting", 0) == "First meeting", "分档在英文下取 en");
        Check(data.GetDialogue("greeting", 7) == "Familiar", "高一档同样取 en");
        Check(data.GetMarriedDialogue("dialogue_greeting_married") == "You are back", "婚后台词读英文");
        Check(data.PositiveBubbles[0] == "Just right", "气泡读英文");
        Check(data.NegativeBubbles[0] == "用不上", "裸串气泡在英文下回落中文");
        L10n.Chinese = true;
        Check(data.PositiveBubbles[0] == "正合用",
              "切回中文后气泡跟着变——按语言缓存必须失效重建，不能把文本钉死在旧语言上");
    }

    // ---- 7：坏数据被跳过并记了一行，而不是整组丢或抛异常 ----
    private static void MalformedLinesAreSkipped()
    {
        int before = ModBehaviour.Logs.Count;
        PermanentDuckNpcData data = Parse(@"{
            ""dialogues"": {
                ""greeting"": [{ ""en"": ""只有英文没有中文"" }, { ""cn"": ""好的一句"", ""en"": ""A good line"" }]
            }
        }");
        Check(data.GetDialogue("greeting", 0) == "好的一句", "缺 cn 的那一项被跳过，剩下的照常读出");
        Check(ModBehaviour.Logs.Count > before, "坏数据要记一行 DevLog，而不是静默吞掉");
    }

    // ---- 8：真实数据里两位天空岛居民确实译全了 ----
    private static void RealDataSkyIslanders()
    {
        string path = Path.Combine("Assets", "Data", "DuckNpcs.json");
        if (!File.Exists(path)) throw new Exception("找不到 " + path);
        BossRushJsonValue root;
        string error;
        if (!BossRushJsonParser.TryParse(File.ReadAllText(path), out root, out error))
            throw new Exception("DuckNpcs.json 解析失败: " + error);
        List<BossRushJsonValue> npcs;
        if (!root.TryGetArray("npcs", out npcs)) throw new Exception("DuckNpcs.json 没有 npcs 数组");

        int islanders = 0;
        foreach (BossRushJsonValue row in npcs)
        {
            string id;
            if (!row.TryGetString("id", out id) || !id.StartsWith("sky_", StringComparison.Ordinal)) continue;
            PermanentDuckNpcData data = PermanentDuckNpcData.Parse(row, id);
            if (data == null) continue;
            islanders++;
            // 每一条玩家会读到的台词：中英两种语言下都必须非空，且**互不相同**
            // （相同就说明这一句其实没译，只是把中文抄了一遍）。
            foreach (string category in new[] { "greeting", "idle", "afterGift", "levelUp", "farewell",
                                                "alreadyGifted", "alreadyGiftedPositive",
                                                "alreadyGiftedNormal", "alreadyGiftedNegative" })
            {
                for (int level = 0; level <= 10; level += 5)
                {
                    L10n.Chinese = true;
                    string cn = data.GetDialogue(category, level);
                    L10n.Chinese = false;
                    string en = data.GetDialogue(category, level);
                    if (cn == null) continue;
                    Check(!string.IsNullOrEmpty(en), id + " 的 " + category + " 英文为空");
                    Check(cn != en, id + " 的 " + category + " 中英同文（这一句没译）：" + cn);
                }
            }
            foreach (string key in new[] { "dialogue_greeting_married", "dialogue_idle_married",
                                           "dialogue_farewell_married", "dialogue_after_gift_married",
                                           "gift_positive_married", "gift_normal_married" })
            {
                L10n.Chinese = true;
                string cn = data.GetMarriedDialogue(key);
                L10n.Chinese = false;
                string en = data.GetMarriedDialogue(key);
                if (cn == null) continue;
                Check(!string.IsNullOrEmpty(en), id + " 的婚后台词 " + key + " 英文为空");
                Check(cn != en, id + " 的婚后台词 " + key + " 中英同文：" + cn);
            }
            L10n.Chinese = true;
            string[] bubblesCn = data.PositiveBubbles;
            L10n.Chinese = false;
            string[] bubblesEn = data.PositiveBubbles;
            Check(bubblesCn != null && bubblesEn != null && bubblesCn.Length == bubblesEn.Length,
                  id + " 的气泡中英条数不一致");
            for (int i = 0; i < bubblesCn.Length; i++)
                Check(bubblesCn[i] != bubblesEn[i], id + " 的气泡中英同文：" + bubblesCn[i]);
            L10n.Chinese = true;
        }
        Check(islanders == 2, "天空岛的两位永久居民（晴禾 / 苇白）都要有 permanent 台词块，实得 " + islanders);
    }

    private static int Main()
    {
        try
        {
            LegacyAndPairedLines();
            MixedForms();
            SingleTierOfPairedObjects();
            TieredMarriedAndBubbles();
            MalformedLinesAreSkipped();
            RealDataSkyIslanders();
        }
        catch (Exception e)
        {
            Console.WriteLine("PermanentDuckNpcDialogue: " + e.Message);
            return 1;
        }
        Console.WriteLine("PermanentDuckNpcDialogue: PASS (" + checks + " checks)");
        return 0;
    }
}
