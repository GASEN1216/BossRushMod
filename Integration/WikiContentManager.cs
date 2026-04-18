// ============================================================================
// WikiContentManager.cs - Wiki 内容管理器
// ============================================================================
// 模块说明：
//   管理 Wiki 内容的加载和解析，包括：
//   - 加载 catalog.tsv 索引文件
//   - 管理分类和条目数据
//   - 加载条目内容文件
//   - Markdown 转 TMP 富文本
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BossRush
{
    // ============================================================================
    // 数据类
    // ============================================================================
    
    /// <summary>
    /// Wiki 分类数据
    /// </summary>
    public class WikiCategory
    {
        /// <summary>
        /// 分类ID（如 "boss"）
        /// </summary>
        public string Id;
        
        /// <summary>
        /// 中文标题
        /// </summary>
        public string TitleCN;
        
        /// <summary>
        /// 英文标题
        /// </summary>
        public string TitleEN;
        
        /// <summary>
        /// 排序顺序
        /// </summary>
        public int Order;
        
        /// <summary>
        /// 获取当前语言的标题
        /// </summary>
        public string GetTitle()
        {
            return L10n.T(TitleCN, TitleEN);
        }
    }
    
    /// <summary>
    /// Wiki 条目数据
    /// </summary>
    public class WikiEntry
    {
        /// <summary>
        /// 条目ID（如 "boss__dragon_descendant"）
        /// </summary>
        public string Id;
        
        /// <summary>
        /// 所属分类ID
        /// </summary>
        public string CategoryId;
        
        /// <summary>
        /// 中文标题
        /// </summary>
        public string TitleCN;
        
        /// <summary>
        /// 英文标题
        /// </summary>
        public string TitleEN;
        
        /// <summary>
        /// 排序顺序
        /// </summary>
        public int Order;
        
        /// <summary>
        /// 获取当前语言的标题
        /// </summary>
        public string GetTitle()
        {
            return L10n.T(TitleCN, TitleEN);
        }
    }
    
    // ============================================================================
    // 内容管理器
    // ============================================================================
    
    /// <summary>
    /// Wiki 内容管理器（单例）
    /// </summary>
    public class WikiContentManager
    {
        // ============================================================================
        // 单例
        // ============================================================================

        private static WikiContentManager _instance;
        public static WikiContentManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new WikiContentManager();
                }
                return _instance;
            }
        }

        // ============================================================================
        // 预编译正则（避免每次 ParseMarkdown 重新编译）
        // ============================================================================

        // 闭合形式优先匹配（支持跨行）：[tip]...[/tip]、[warn]...[/warn]
        private static readonly Regex RxWarnBlock  = new Regex(@"\[warn\](.+?)\[/warn\]", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex RxTipBlock   = new Regex(@"\[tip\](.+?)\[/tip\]",   RegexOptions.Compiled | RegexOptions.Singleline);
        // 行级形式（md 实际主要用法）：[tip] text 匹配到该行末尾，不需闭合标签
        private static readonly Regex RxWarnLine   = new Regex(@"^\[warn\]\s*(.+?)\s*$",  RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex RxTipLine    = new Regex(@"^\[tip\]\s*(.+?)\s*$",   RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex RxHr         = new Regex(@"^---+$", RegexOptions.Compiled | RegexOptions.Multiline);
        // 标题按 #### / ### / ## / # 顺序匹配（长的在前，避免 ## 吞掉 ###）
        private static readonly Regex RxH4         = new Regex(@"^#### +(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex RxH3         = new Regex(@"^### +(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex RxH2         = new Regex(@"^## +(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex RxH1         = new Regex(@"^# +(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex RxBold       = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        // 列表：保留多级缩进（每 2/4 空格 = 1 级），转换为视觉缩进
        private static readonly Regex RxList       = new Regex(@"^( *)- +(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex RxCode       = new Regex(@"`(.+?)`", RegexOptions.Compiled);
        private static readonly Regex RxMdLink     = new Regex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex RxRawUrl     = new Regex(@"(?<![\"">=/])((https?://)[^\s<>\[\]]+)", RegexOptions.Compiled);
        private static readonly Regex RxBlankLines = new Regex(@"\n{3,}", RegexOptions.Compiled);
        
        // ============================================================================
        // 数据缓存
        // ============================================================================
        
        /// <summary>
        /// 分类列表
        /// </summary>
        private List<WikiCategory> categories = new List<WikiCategory>();
        
        /// <summary>
        /// 条目列表（按分类ID索引）
        /// </summary>
        private Dictionary<string, List<WikiEntry>> entriesByCategory = new Dictionary<string, List<WikiEntry>>();
        
        /// <summary>
        /// 所有条目（按条目ID索引）
        /// </summary>
        private Dictionary<string, WikiEntry> entriesById = new Dictionary<string, WikiEntry>();
        
        /// <summary>
        /// 内容目录路径
        /// </summary>
        private string contentBasePath = null;
        
        /// <summary>
        /// 是否已加载
        /// </summary>
        private bool isLoaded = false;
        
        // ============================================================================
        // 公共方法
        // ============================================================================
        
        /// <summary>
        /// 重置缓存（游戏启动时调用）
        /// </summary>
        public void ResetCache()
        {
            isLoaded = false;
            categories.Clear();
            entriesByCategory.Clear();
            entriesById.Clear();
            ModBehaviour.DevLog("[WikiContentManager] 缓存已重置");
        }
        
        /// <summary>
        /// 加载目录索引
        /// </summary>
        public void LoadCatalog()
        {
            if (isLoaded)
            {
                return;
            }
            
            try
            {
                // 确定内容目录路径
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                contentBasePath = Path.Combine(modDir, "WikiContent");
                
                string catalogPath = Path.Combine(contentBasePath, "catalog.tsv");
                
                if (!File.Exists(catalogPath))
                {
                    ModBehaviour.DevLog("[WikiContentManager] 目录文件不存在: " + catalogPath);
                    CreateDefaultCatalog();
                    return;
                }
                
                // 读取并解析 catalog.tsv
                string[] lines = File.ReadAllLines(catalogPath, Encoding.UTF8);
                ParseCatalog(lines);
                
                isLoaded = true;
                ModBehaviour.DevLog("[WikiContentManager] 目录加载完成: " + categories.Count + " 个分类, " + entriesById.Count + " 个条目");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiContentManager] 加载目录失败: " + e.Message);
                CreateDefaultCatalog();
            }
        }
        
        /// <summary>
        /// 获取所有分类
        /// </summary>
        public List<WikiCategory> GetCategories()
        {
            return categories;
        }
        
        /// <summary>
        /// 获取指定分类下的条目
        /// </summary>
        public List<WikiEntry> GetEntries(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId))
            {
                return new List<WikiEntry>();
            }
            
            if (entriesByCategory.TryGetValue(categoryId, out var entries))
            {
                return entries;
            }
            
            return new List<WikiEntry>();
        }
        
        /// <summary>
        /// 获取指定条目
        /// </summary>
        public WikiEntry GetEntry(string entryId)
        {
            if (string.IsNullOrEmpty(entryId))
            {
                return null;
            }
            
            if (entriesById.TryGetValue(entryId, out var entry))
            {
                return entry;
            }
            
            return null;
        }
        
        /// <summary>
        /// 加载条目内容
        /// </summary>
        public string LoadEntryContent(string entryId)
        {
            if (string.IsNullOrEmpty(entryId))
            {
                return "[条目ID为空]";
            }
            
            try
            {
                // 根据当前语言选择目录
                string langDir = L10n.IsChinese ? "zh" : "en";
                
                // 从条目ID提取分类（格式：categoryId__entryName）
                string categoryDir = "";
                int separatorIndex = entryId.IndexOf("__");
                if (separatorIndex > 0)
                {
                    categoryDir = entryId.Substring(0, separatorIndex);
                }
                
                // 构建文件路径
                string contentPath = Path.Combine(contentBasePath, langDir, categoryDir, entryId + ".md");
                
                if (!File.Exists(contentPath))
                {
                    // 尝试不带分类目录的路径
                    contentPath = Path.Combine(contentBasePath, langDir, entryId + ".md");
                }
                
                if (!File.Exists(contentPath))
                {
                    ModBehaviour.DevLog("[WikiContentManager] 内容文件不存在: " + contentPath);
                    return "[内容文件不存在: " + entryId + "]";
                }
                
                string content = File.ReadAllText(contentPath, Encoding.UTF8);
                return content;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiContentManager] 加载内容失败: " + e.Message);
                return "[加载失败: " + e.Message + "]";
            }
        }

        // ============================================================================
        // Markdown 解析
        // ============================================================================
        
        /// <summary>
        /// 解析 Markdown 为 TMP 富文本
        /// </summary>
        public string ParseMarkdown(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return "";
            }

            // 去掉首行与 TopBar 标题重复的 H1/H2（几乎所有条目都以 `## Title` 开头，与 entry.Title 重复）
            string result = StripLeadingTitle(markdown);

            try
            {
                // 先处理特殊标记（在标题处理之前，避免被标题正则误匹配）
                // 1) 优先匹配闭合形式 [tip]...[/tip]（支持多行）
                result = RxWarnBlock.Replace(result, "\n<color=#CC0000><b>[!]</b> $1</color>\n");
                result = RxTipBlock.Replace(result,  "\n<color=#0066CC><b>[i]</b> $1</color>\n");
                // 2) 再匹配行级形式 [tip] text（无闭合，md 实际主要用法）
                result = RxWarnLine.Replace(result,  "<color=#CC0000><b>[!]</b> $1</color>");
                result = RxTipLine.Replace(result,   "<color=#0066CC><b>[i]</b> $1</color>");

                // 处理分隔线 --- -> 用短横线模拟
                result = RxHr.Replace(result, "<color=#C0B090>────────────────</color>");

                // 标题：根据标题视觉宽度动态选择字号，避免长标题（如纯中文 10+ 字）被左栏宽度逼得换行。
                // 标题内普通空格替换为 NBSP 防止中英混排在空格处断行（如 "标准 BossRush"）。
                result = RxH4.Replace(result, m => FormatHeading(4, m.Groups[1].Value));
                result = RxH3.Replace(result, m => FormatHeading(3, m.Groups[1].Value));
                result = RxH2.Replace(result, m => FormatHeading(2, m.Groups[1].Value));
                result = RxH1.Replace(result, m => FormatHeading(1, m.Groups[1].Value));

                // 处理粗体 **text** -> <b>text</b>
                result = RxBold.Replace(result, "<b>$1</b>");

                // 无序列表：保留原缩进层级 + 更精美的项目符号
                result = RxList.Replace(result, m =>
                {
                    int indentSpaces = m.Groups[1].Value.Length;
                    int level = Mathf.Clamp(indentSpaces / 2, 0, 4);
                    string bullet = level == 0 ? "•" : (level == 1 ? "◦" : "▪");
                    return new string(' ', level * 4) + bullet + " " + m.Groups[2].Value;
                });

                // 处理代码块 `code` -> 灰色
                result = RxCode.Replace(result, "<color=#A0A0A0>$1</color>");

                // 处理Markdown链接 [text](url) -> TMP link标签
                result = RxMdLink.Replace(result, "<color=#0066CC><u><link=\"$2\">$1</link></u></color>");

                // 处理纯URL链接（排除已在link标签或引号中的URL）
                result = RxRawUrl.Replace(result, "<color=#0066CC><u><link=\"$1\">$1</link></u></color>");

                // 清理多余空行（最多保留1个空行）
                result = RxBlankLines.Replace(result, "\n\n");

                // 去掉开头的空行
                result = result.TrimStart('\n', '\r');
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiContentManager] Markdown 解析失败: " + e.Message);
            }

            return result;
        }

        /// <summary>
        /// 把标题内的普通空格替换为 NBSP (U+00A0)，阻止 TMP 在空格处换行。
        /// 例：中英混排的 "标准 BossRush" 中间空格是 TMP 默认断点，导致标题被拆成两行。
        /// </summary>
        private static string NoBreakTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;
            return title.Replace(' ', '\u00A0').Replace('\t', '\u00A0');
        }

        /// <summary>
        /// 估算文本在 UI 中的视觉宽度（中文/日韩/全角符号 = 2，其它 = 1）。
        /// 用于标题自适应字号，避免长标题被窄 rect 强制换行。
        /// </summary>
        private static int EstimateVisualWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            foreach (char c in s)
            {
                // CJK 统一表意 + 标点 + 符号
                if (c >= 0x3000 && c <= 0x9FFF) w += 2;
                // 日文平/片假名
                else if (c >= 0x3040 && c <= 0x30FF) w += 2;
                // 韩文
                else if (c >= 0xAC00 && c <= 0xD7AF) w += 2;
                // 全角
                else if (c >= 0xFF00 && c <= 0xFFEF) w += 2;
                else w += 1;
            }
            return w;
        }

        /// <summary>
        /// 生成标题 rich text：根据视觉宽度动态选择字号，避免长标题换行。
        /// level: 1/2/3/4 对应 H1/H2/H3/H4。颜色由深到浅表达层级。
        /// </summary>
        private static string FormatHeading(int level, string rawContent)
        {
            if (string.IsNullOrEmpty(rawContent)) return "";

            string safeContent = NoBreakTitle(rawContent);
            int w = EstimateVisualWidth(rawContent);
            int sizeOffset;
            string color;

            switch (level)
            {
                case 1:
                    color = "#3A1F0D";
                    sizeOffset = w <= 10 ? 5 : (w <= 16 ? 3 : (w <= 22 ? 1 : 0));
                    break;
                case 2:
                    color = "#5B3010";
                    sizeOffset = w <= 12 ? 3 : (w <= 18 ? 2 : (w <= 24 ? 1 : 0));
                    break;
                case 3:
                    color = "#6B4226";
                    sizeOffset = w <= 16 ? 2 : (w <= 24 ? 1 : 0);
                    break;
                default: // level 4
                    color = "#5C4033";
                    sizeOffset = w <= 20 ? 1 : 0;
                    break;
            }

            string sizeOpen = sizeOffset > 0 ? ("<size=+" + sizeOffset + ">") : "";
            string sizeClose = sizeOffset > 0 ? "</size>" : "";
            return "\n" + sizeOpen + "<color=" + color + "><b>" + safeContent + "</b></color>" + sizeClose + "\n";
        }

        /// <summary>
        /// 若 Markdown 首个非空行为 H1/H2，去除它和紧随的空行。
        /// 原因：TopBar 已显示条目标题，md 文件约定以 `## Title` 开头会导致标题重复。
        /// </summary>
        private static string StripLeadingTitle(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return markdown;

            int idx = 0;
            int len = markdown.Length;

            // 跳过开头空行
            while (idx < len && (markdown[idx] == '\n' || markdown[idx] == '\r' || markdown[idx] == ' ' || markdown[idx] == '\t'))
            {
                idx++;
            }
            if (idx >= len) return markdown;

            // 检查是否为 H1/H2 开头
            if (markdown[idx] != '#') return markdown;

            int hashCount = 0;
            while (idx + hashCount < len && markdown[idx + hashCount] == '#') hashCount++;
            if (hashCount < 1 || hashCount > 2) return markdown;
            if (idx + hashCount >= len || markdown[idx + hashCount] != ' ') return markdown;

            // 找到该行末尾
            int lineEnd = markdown.IndexOf('\n', idx);
            if (lineEnd < 0) return "";

            // 跳过紧随的空行
            int next = lineEnd + 1;
            while (next < len && (markdown[next] == '\n' || markdown[next] == '\r'))
            {
                next++;
            }

            return markdown.Substring(next);
        }

        // ============================================================================
        // 私有方法
        // ============================================================================
        
        /// <summary>
        /// 解析目录文件
        /// </summary>
        private void ParseCatalog(string[] lines)
        {
            categories.Clear();
            entriesByCategory.Clear();
            entriesById.Clear();
            
            // 用于去重分类
            HashSet<string> categoryIds = new HashSet<string>();
            
            // 跳过标题行
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                
                // 解析 TSV 行：categoryId\tentryId\ttitleKey_zh\ttitleKey_en\torder
                string[] parts = line.Split('\t');
                if (parts.Length < 5)
                {
                    ModBehaviour.DevLog("[WikiContentManager] 跳过无效行: " + line);
                    continue;
                }
                
                string categoryId = parts[0].Trim();
                string entryId = parts[1].Trim();
                string titleCN = parts[2].Trim();
                string titleEN = parts[3].Trim();
                int order = 0;
                int.TryParse(parts[4].Trim(), out order);
                
                // 添加分类（如果不存在）
                if (!categoryIds.Contains(categoryId))
                {
                    categoryIds.Add(categoryId);
                    
                    // 从第一个条目推断分类标题
                    string categoryTitleCN = GetCategoryTitle(categoryId, true);
                    string categoryTitleEN = GetCategoryTitle(categoryId, false);
                    
                    categories.Add(new WikiCategory
                    {
                        Id = categoryId,
                        TitleCN = categoryTitleCN,
                        TitleEN = categoryTitleEN,
                        Order = categories.Count
                    });
                    
                    entriesByCategory[categoryId] = new List<WikiEntry>();
                }
                
                // 添加条目
                var entry = new WikiEntry
                {
                    Id = entryId,
                    CategoryId = categoryId,
                    TitleCN = titleCN,
                    TitleEN = titleEN,
                    Order = order
                };
                
                entriesByCategory[categoryId].Add(entry);
                entriesById[entryId] = entry;
            }
            
            // 按 Order 排序
            categories.Sort((a, b) => a.Order.CompareTo(b.Order));
            foreach (var list in entriesByCategory.Values)
            {
                list.Sort((a, b) => a.Order.CompareTo(b.Order));
            }
        }
        
        /// <summary>
        /// 获取分类标题
        /// </summary>
        private string GetCategoryTitle(string categoryId, bool isChinese)
        {
            // 预定义分类标题
            switch (categoryId.ToLower())
            {
                case "_wiki_link":
                    return isChinese ? "BossRush Wiki" : "BossRush Wiki";
                case "start":
                    return isChinese ? "入门" : "Getting Started";
                case "mechanics":
                    return isChinese ? "机制" : "Mechanics";
                case "config":
                    return isChinese ? "配置" : "Configuration";
                case "misc":
                    return isChinese ? "杂项" : "Misc";
                case "easter":
                    return isChinese ? "彩蛋" : "Easter Egg";
                case "changelog":
                    return isChinese ? "更新日志" : "Changelog";
                case "boss":
                    return isChinese ? "Boss 图鉴" : "Boss Guide";
                case "equipment":
                    return isChinese ? "装备与能力" : "Equipment & Powers";
                case "mode":
                    return isChinese ? "模式与玩法" : "Game Modes";
                case "item":
                    return isChinese ? "物品与道具" : "Items";
                case "map":
                    return isChinese ? "地图与场地" : "Maps & Arena";
                case "npc":
                    return isChinese ? "NPC 与关系" : "NPCs & Bonds";
                case "system":
                    return isChinese ? "核心系统" : "Core Systems";
                case "tips":
                    return isChinese ? "进阶技巧" : "Tips & Tricks";
                default:
                    return categoryId;
            }
        }
        
        /// <summary>
        /// 创建默认目录（当文件不存在时）
        /// </summary>
        private void CreateDefaultCatalog()
        {
            // 添加默认分类
            categories.Add(new WikiCategory
            {
                Id = "boss",
                TitleCN = "Boss 图鉴",
                TitleEN = "Boss Guide",
                Order = 1
            });
            
            categories.Add(new WikiCategory
            {
                Id = "mode",
                TitleCN = "模式攻略",
                TitleEN = "Game Modes",
                Order = 2
            });
            
            entriesByCategory["boss"] = new List<WikiEntry>();
            entriesByCategory["mode"] = new List<WikiEntry>();
            
            // 添加示例条目
            var sampleEntry = new WikiEntry
            {
                Id = "boss__sample",
                CategoryId = "boss",
                TitleCN = "示例 Boss",
                TitleEN = "Sample Boss",
                Order = 1
            };
            entriesByCategory["boss"].Add(sampleEntry);
            entriesById[sampleEntry.Id] = sampleEntry;
            
            isLoaded = true;
            ModBehaviour.DevLog("[WikiContentManager] 已创建默认目录");
        }
    }
}
