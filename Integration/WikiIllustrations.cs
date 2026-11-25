// ============================================================================
// WikiIllustrations.cs - 冒险家日志插画清单加载与按章节分段器
// ============================================================================
// 从 WikiContent/illustrations.json 读取单一事实源（与在线 Wiki 共用），
// 通过 ModSprites.Get 按当前语言取图，并把正文按 heading / section-end
// 拆分成交替的文本段与插画槽位，供 WikiUIManager 做高度感知的分页排版。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BossRush
{
    /// <summary>单张插画资源描述（与 WikiContent/illustrations.json assets 条目对应）。</summary>
    internal sealed class WikiIllustrationAsset
    {
        internal string Key;
        internal string AssetPath;
        internal string AssetPathEn;
        internal string CaptionZh;
        internal string CaptionEn;

        internal string GetCaption()
        {
            if (L10n.IsChinese)
            {
                return !string.IsNullOrEmpty(CaptionZh) ? CaptionZh : (CaptionEn ?? Key);
            }
            return !string.IsNullOrEmpty(CaptionEn) ? CaptionEn : (CaptionZh ?? Key);
        }

        internal string GetAssetPath()
        {
            if (!L10n.IsChinese && !string.IsNullOrEmpty(AssetPathEn))
            {
                return AssetPathEn;
            }
            return AssetPath;
        }

        internal Sprite ResolveSprite()
        {
            string path = GetAssetPath();
            return !string.IsNullOrEmpty(path) ? ModSprites.Get(path) : null;
        }
    }

    /// <summary>illustrations.json entries 中一条配图放置声明。</summary>
    internal sealed class WikiIllustrationSpec
    {
        internal int AtHeading;
        internal bool AfterHeading;
        internal string Kind;
        internal List<string> Keys;
    }

    /// <summary>拆分后的条目片段：文本块或已解析精灵的插画块。</summary>
    internal sealed class WikiContentSegment
    {
        internal bool IsIllustration;
        internal string MarkdownChunk;
        internal bool StripLeadingTitle;

        internal int AtHeading;
        internal bool AfterHeading;
        internal string HeadingLine;
        internal string Kind;
        internal List<WikiIllustrationAsset> Assets;
    }

    /// <summary>绑定在单个物理页面（左栏或右栏）上的插画槽位。</summary>
    internal sealed class WikiPageVisualSlot
    {
        internal bool AnchorTop;
        internal string HeadingRichText;
        internal string Kind;
        internal List<WikiIllustrationAsset> Assets;
        internal float HeadingHeight;
        internal float CardHeight;
        internal float ReservedHeight;
        internal float BottomTextUsedHeight;
    }

    /// <summary>
    /// 百科插画清单一站式加载与分段服务。
    /// 不缓存 Sprite（交由 ModSprites 统一管理），只缓存 JSON 解析后的轻量元数据。
    /// </summary>
    internal static class WikiIllustrations
    {
        private static readonly Regex RxHeadingLine = new Regex(@"^#{1,6}[ \t]+", RegexOptions.Compiled);

        private static bool loaded = false;
        private static string loadedModPath = null;
        private static readonly Dictionary<string, WikiIllustrationAsset> assetsByKey =
            new Dictionary<string, WikiIllustrationAsset>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<WikiIllustrationSpec>> placementsByEntry =
            new Dictionary<string, List<WikiIllustrationSpec>>(StringComparer.Ordinal);

        internal static void ResetStaticCaches()
        {
            loaded = false;
            loadedModPath = null;
            assetsByKey.Clear();
            placementsByEntry.Clear();
        }

        private static void EnsureLoaded()
        {
            string modPath = ModBehaviour.GetModPath();
            if (loaded && string.Equals(loadedModPath, modPath, StringComparison.Ordinal))
            {
                return;
            }

            loaded = true;
            loadedModPath = modPath;
            assetsByKey.Clear();
            placementsByEntry.Clear();

            if (string.IsNullOrEmpty(modPath)) return;
            string jsonPath = Path.Combine(Path.Combine(modPath, "WikiContent"), "illustrations.json");
            if (!File.Exists(jsonPath))
            {
                ModBehaviour.DevLog("[WikiIllustrations] 未找到配图清单，按纯文本显示: " + jsonPath);
                return;
            }

            try
            {
                string raw = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
                BossRushJsonValue root;
                string err;
                if (!BossRushJsonParser.TryParse(raw, out root, out err) || root == null)
                {
                    ModBehaviour.DevLog("[WikiIllustrations] 配图清单解析失败: " + err);
                    return;
                }

                LoadAssets(root);
                LoadEntries(root);
                ModBehaviour.DevLog("[WikiIllustrations] 已加载配图清单: assets=" + assetsByKey.Count
                    + ", entries=" + placementsByEntry.Count);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiIllustrations] 读取配图清单异常: " + e.Message);
            }
        }

        private static void LoadAssets(BossRushJsonValue root)
        {
            BossRushJsonValue assetsObj;
            if (!root.TryGetObject("assets", out assetsObj) || assetsObj.Properties == null) return;

            for (int i = 0; i < assetsObj.Properties.Count; i++)
            {
                BossRushJsonProperty prop = assetsObj.Properties[i];
                if (prop == null || string.IsNullOrEmpty(prop.Name) || prop.Value == null) continue;
                if (prop.Value.Kind != BossRushJsonKind.Object) continue;

                string assetPath;
                if (!prop.Value.TryGetString("asset", out assetPath) || string.IsNullOrEmpty(assetPath)) continue;

                string assetEn;
                prop.Value.TryGetString("assetEn", out assetEn);
                string zh;
                prop.Value.TryGetString("zh", out zh);
                string en;
                prop.Value.TryGetString("en", out en);

                assetsByKey[prop.Name] = new WikiIllustrationAsset
                {
                    Key = prop.Name,
                    AssetPath = assetPath,
                    AssetPathEn = assetEn,
                    CaptionZh = zh,
                    CaptionEn = en
                };
            }
        }

        private static void LoadEntries(BossRushJsonValue root)
        {
            BossRushJsonValue entriesObj;
            if (!root.TryGetObject("entries", out entriesObj) || entriesObj.Properties == null) return;

            for (int i = 0; i < entriesObj.Properties.Count; i++)
            {
                BossRushJsonProperty prop = entriesObj.Properties[i];
                if (prop == null || string.IsNullOrEmpty(prop.Name) || prop.Value == null) continue;

                List<BossRushJsonValue> arr;
                if (!entriesObj.TryGetArray(prop.Name, out arr) || arr == null || arr.Count == 0) continue;

                var list = new List<WikiIllustrationSpec>(arr.Count);
                for (int j = 0; j < arr.Count; j++)
                {
                    BossRushJsonValue item = arr[j];
                    if (item == null || item.Kind != BossRushJsonKind.Object) continue;

                    int atHeading;
                    if (!item.TryGetInt("at", out atHeading) || atHeading < 0) continue;

                    string mode;
                    if (!item.TryGetString("mode", out mode)) mode = "section-end";
                    string kind;
                    if (!item.TryGetString("kind", out kind)) kind = "figure";

                    List<string> keys;
                    if (!item.TryGetStringList("keys", out keys) || keys == null || keys.Count == 0) continue;

                    list.Add(new WikiIllustrationSpec
                    {
                        AtHeading = atHeading,
                        AfterHeading = string.Equals(mode, "after-heading", StringComparison.Ordinal),
                        Kind = kind,
                        Keys = keys
                    });
                }

                if (list.Count > 0)
                {
                    list.Sort((a, b) =>
                    {
                        int cmp = a.AtHeading.CompareTo(b.AtHeading);
                        if (cmp != 0) return cmp;
                        return (a.AfterHeading ? 0 : 1).CompareTo(b.AfterHeading ? 0 : 1);
                    });
                    placementsByEntry[prop.Name] = list;
                }
            }
        }

        /// <summary>
        /// 按 entryId 与原始 Markdown 构造分段列表；若该条目无有效插画则返回 null，调用方走纯文本快路径。
        /// </summary>
        internal static List<WikiContentSegment> BuildContentSegments(string entryId, string rawMarkdown)
        {
            if (string.IsNullOrEmpty(entryId) || string.IsNullOrEmpty(rawMarkdown)) return null;

            EnsureLoaded();
            List<WikiIllustrationSpec> specs;
            if (!placementsByEntry.TryGetValue(entryId, out specs) || specs == null || specs.Count == 0)
            {
                return null;
            }

            string normalized = rawMarkdown.Replace("\r\n", "\n");
            string[] lines = normalized.Split('\n');
            var headingLineIndices = new List<int>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (RxHeadingLine.IsMatch(lines[i])) headingLineIndices.Add(i);
            }
            if (headingLineIndices.Count == 0) return null;

            var anchors = new List<ResolvedAnchor>(specs.Count);
            for (int i = 0; i < specs.Count; i++)
            {
                WikiIllustrationSpec spec = specs[i];
                if (spec.AtHeading < 0 || spec.AtHeading >= headingLineIndices.Count) continue;

                List<WikiIllustrationAsset> resolved = ResolveAssets(spec.Keys);
                if (resolved.Count == 0) continue;

                int headLine = headingLineIndices[spec.AtHeading];
                int splitLine;
                int resumeLine;
                string headingLine = null;

                if (spec.AfterHeading)
                {
                    if (spec.AtHeading == 0)
                    {
                        splitLine = headLine + 1;
                        resumeLine = headLine + 1;
                    }
                    else
                    {
                        splitLine = headLine;
                        resumeLine = headLine + 1;
                        headingLine = lines[headLine];
                    }
                }
                else
                {
                    int nextHead = spec.AtHeading + 1 < headingLineIndices.Count
                        ? headingLineIndices[spec.AtHeading + 1]
                        : lines.Length;
                    while (nextHead > headLine + 1 && string.IsNullOrWhiteSpace(lines[nextHead - 1]))
                    {
                        nextHead--;
                    }
                    splitLine = nextHead;
                    resumeLine = nextHead;
                }

                anchors.Add(new ResolvedAnchor
                {
                    SplitLine = splitLine,
                    ResumeLine = resumeLine,
                    Spec = spec,
                    HeadingLine = headingLine,
                    Assets = resolved
                });
            }

            if (anchors.Count == 0) return null;

            anchors.Sort((a, b) => a.SplitLine.CompareTo(b.SplitLine));

            var segments = new List<WikiContentSegment>(anchors.Count * 2 + 1);
            int cursor = 0;
            for (int i = 0; i < anchors.Count; i++)
            {
                ResolvedAnchor anchor = anchors[i];
                int end = Math.Max(cursor, Math.Min(anchor.SplitLine, lines.Length));
                AddTextSegmentIfNonEmpty(segments, lines, cursor, end, cursor == 0);

                segments.Add(new WikiContentSegment
                {
                    IsIllustration = true,
                    AtHeading = anchor.Spec.AtHeading,
                    AfterHeading = anchor.Spec.AfterHeading,
                    HeadingLine = anchor.HeadingLine,
                    Kind = anchor.Spec.Kind,
                    Assets = anchor.Assets
                });

                cursor = Math.Max(end, Math.Min(anchor.ResumeLine, lines.Length));
            }

            AddTextSegmentIfNonEmpty(segments, lines, cursor, lines.Length, cursor == 0);

            // 条首导语很短时，把 at=0 的 section-end 徽章/总览图提到首段之前（卡片置顶、下文连续排满），
            // 避免只放两三句导语 + 一张小图就强制翻页留下大块空白。
            if (segments.Count >= 3
                && !segments[0].IsIllustration
                && segments[1].IsIllustration
                && !segments[2].IsIllustration
                && segments[1].AtHeading == 0
                && !segments[1].AfterHeading
                && IsCompactIntroChunk(segments[0].MarkdownChunk))
            {
                WikiContentSegment topBadge = segments[1];
                string mergedMarkdown = MergeLeadingAndFollowupMarkdown(segments[0].MarkdownChunk, segments[2].MarkdownChunk);
                segments[0] = topBadge;
                segments[1] = new WikiContentSegment
                {
                    IsIllustration = false,
                    MarkdownChunk = mergedMarkdown,
                    StripLeadingTitle = true
                };
                segments.RemoveAt(2);
            }

            return segments;
        }

        private static List<WikiIllustrationAsset> ResolveAssets(List<string> keys)
        {
            var result = new List<WikiIllustrationAsset>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                WikiIllustrationAsset asset;
                if (!assetsByKey.TryGetValue(keys[i], out asset) || asset == null) continue;
                if (asset.ResolveSprite() == null) continue;
                result.Add(asset);
            }
            return result;
        }

        private static void AddTextSegmentIfNonEmpty(
            List<WikiContentSegment> target,
            string[] lines,
            int startLine,
            int endLine,
            bool isFromTopOfFile)
        {
            if (endLine <= startLine) return;
            string chunk = string.Join("\n", lines, startLine, endLine - startLine);
            if (string.IsNullOrWhiteSpace(chunk)) return;

            target.Add(new WikiContentSegment
            {
                IsIllustration = false,
                MarkdownChunk = chunk,
                StripLeadingTitle = isFromTopOfFile
            });
        }

        private static bool IsCompactIntroChunk(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return true;
            string[] lines = chunk.Split('\n');
            int nonEmpty = 0;
            int totalChars = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (string.IsNullOrEmpty(trimmed) || RxHeadingLine.IsMatch(trimmed)) continue;
                nonEmpty++;
                totalChars += trimmed.Length;
            }
            return nonEmpty <= 4 && totalChars <= 240;
        }

        private static string MergeLeadingAndFollowupMarkdown(string lead, string follow)
        {
            string a = (lead ?? string.Empty).TrimEnd('\n', '\r');
            string b = (follow ?? string.Empty).TrimStart('\n', '\r');
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "\n\n" + b;
        }

        private struct ResolvedAnchor
        {
            public int SplitLine;
            public int ResumeLine;
            public WikiIllustrationSpec Spec;
            public string HeadingLine;
            public List<WikiIllustrationAsset> Assets;
        }
    }
}
