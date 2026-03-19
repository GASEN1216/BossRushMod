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

            string result = markdown;

            try
            {
                // 先处理特殊标记（在标题处理之前，避免被标题正则误匹配）
                // 处理警告提示 [warn]text[/warn] -> 红色
                result = Regex.Replace(result, @"\[warn\](.+?)\[/warn\]", "\n<color=#CC0000><b>[!]</b> $1</color>\n", RegexOptions.Singleline);

                // 处理提示信息 [tip]text[/tip] -> 深蓝色
                result = Regex.Replace(result, @"\[tip\](.+?)\[/tip\]", "\n<color=#0066CC><b>[i]</b> $1</color>\n", RegexOptions.Singleline);

                // 处理分隔线 --- -> 用短横线模拟
                result = Regex.Replace(result, @"^---+$", "<color=#C0B090>----------------</color>", RegexOptions.Multiline);

                // 处理标题（注意顺序：先处理 #### 再 ### 再 ##，避免 ## 匹配到 ###）
                // 四级标题 #### -> 小号粗体，带左侧标记
                result = Regex.Replace(result, @"^#### (.+)$", "  <color=#5C4033><b>> $1</b></color>", RegexOptions.Multiline);

                // 三级标题 ### -> 粗体
                result = Regex.Replace(result, @"^### (.+)$", "\n<color=#6B4226><b># $1</b></color>", RegexOptions.Multiline);

                // 二级标题 ## -> 适中大小，不用 size 避免换行
                result = Regex.Replace(result, @"^## (.+)$", "\n<color=#5B3010><b>== $1 ==</b></color>\n", RegexOptions.Multiline);

                // 处理粗体 **text** -> <b>text</b>
                result = Regex.Replace(result, @"\*\*(.+?)\*\*", "<b>$1</b>");

                // 处理无序列表 - item -> 带缩进的短横线
                result = Regex.Replace(result, @"^- (.+)$", "  - $1", RegexOptions.Multiline);

                // 处理代码块 `code` -> 灰色
                result = Regex.Replace(result, @"`(.+?)`", "<color=#A0A0A0>$1</color>");

                // 处理Markdown链接 [text](url) -> TMP link标签
                result = Regex.Replace(result, @"\[([^\]]+)\]\(([^)]+)\)", "<color=#0066CC><u><link=\"$2\">$1</link></u></color>");

                // 处理纯URL链接（http://或https://开头）
                result = Regex.Replace(result, @"(?<![\"">])((https?://)[^\s<>\[\]]+)", "<color=#0066CC><u><link=\"$1\">$1</link></u></color>");

                // 清理多余空行（最多保留1个空行）
                result = Regex.Replace(result, @"\n{3,}", "\n\n");

                // 去掉开头的空行
                result = result.TrimStart('\n', '\r');
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WikiContentManager] Markdown 解析失败: " + e.Message);
            }

            return result;
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
