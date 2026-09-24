// ============================================================================
// DailyReportLayoutTable.cs - 日报面板版面表（底图、图标与文字的唯一坐标真值）
// ============================================================================
// 底图 Assets/ui/DailyReport/daily_report_bg.png、图标 dr_icon_*.png 与本表
// Assets/Data/DailyReportLayout.json 由同一个脚本 tools/gen_daily_report_ui.py 一次产出。
// 文字与图标按本表摆位，于是「卡片画在哪」「图标在哪」「字写在哪」天生对齐——
// 改版面只改脚本，不用几处手抄坐标。
//
// 坐标系：左上原点、像素单位、与底图同尺寸（1333 × 1013）。
// UI 侧用 ToAnchored() 转成 Unity 的中心原点 + Y 向上。
//
// 2026-09-23 第五轮：图标不再烤进底图（底图进包被压到 1024 宽再放大，小图标会糊），
// 版面表多了一段 "icons"（每个图标一块矩形）；schemaVersion 2。
//
// 硬约束（AGENTS 4.8 第 3 层：大型数据表 = JSON + Registry + guard + 硬编码 fallback）：
//   - 读表失败一律回落硬编码版面（与 JSON 同源，DailyReportPresentationGuard.check_layout_fallback_sync
//     交叉核对），面板绝不因为少一个数据文件就打不开；
//   - 纯查询、无 Unity 依赖以外的副作用，惰性读一次。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>日报面板的版面表。惰性读 JSON，失败回落硬编码。</summary>
    internal static class DailyReportLayoutTable
    {
        private const string DataFileName = "DailyReportLayout.json";

        /// <summary>底图尺寸；面板 RectTransform 用同一组数字。</summary>
        internal const float PanelWidth = 1333f;
        internal const float PanelHeight = 1013f;

        private static readonly object _lock = new object();
        private static Dictionary<string, Rect> _rects;
        private static Dictionary<string, Rect> _icons;
        private static bool _loadedFromJson;

        // 网格与图例
        private static float _gridX, _gridY, _cellWidth, _cellHeight, _cellGap;
        private static int _columns, _rows;
        private static float _legendSwatch, _legendItemWidth;
        private static int _legendCount;
        private static float _pillTextIndent, _iconTextGap;

        internal static bool LoadedFromJson { get { EnsureBuilt(); return _loadedFromJson; } }

        internal static float GridX { get { EnsureBuilt(); return _gridX; } }
        internal static float GridY { get { EnsureBuilt(); return _gridY; } }
        internal static float CellWidth { get { EnsureBuilt(); return _cellWidth; } }
        internal static float CellHeight { get { EnsureBuilt(); return _cellHeight; } }
        internal static float CellGap { get { EnsureBuilt(); return _cellGap; } }
        internal static int Columns { get { EnsureBuilt(); return _columns; } }
        internal static int Rows { get { EnsureBuilt(); return _rows; } }
        internal static float LegendSwatch { get { EnsureBuilt(); return _legendSwatch; } }
        internal static float LegendItemWidth { get { EnsureBuilt(); return _legendItemWidth; } }
        /// <summary>图例项数（2026-09-22 起 5 项；旧表没有这个字段按 4 项读）。</summary>
        internal static int LegendCount { get { EnsureBuilt(); return _legendCount; } }
        /// <summary>缎带标题字相对缎带左沿的缩进（让开缎带头上的图标）。</summary>
        internal static float PillTextIndent { get { EnsureBuilt(); return _pillTextIndent; } }
        /// <summary>图标右沿到正文的间距。</summary>
        internal static float IconTextGap { get { EnsureBuilt(); return _iconTextGap; } }

        /// <summary>取一块矩形（左上原点、像素）。未知名字返回全零矩形。</summary>
        internal static Rect Get(string name)
        {
            EnsureBuilt();
            Rect rect;
            return _rects.TryGetValue(name, out rect) ? rect : new Rect(0f, 0f, 0f, 0f);
        }

        /// <summary>取一个图标的矩形（左上原点、像素）。未知名字返回全零矩形，调用方据此不画。</summary>
        internal static Rect GetIcon(string name)
        {
            EnsureBuilt();
            Rect rect;
            return _icons.TryGetValue(name, out rect) ? rect : new Rect(0f, 0f, 0f, 0f);
        }

        /// <summary>
        /// 左上原点矩形 → Unity 的 anchoredPosition（父节点中心原点、Y 向上）。
        /// 调用方把 sizeDelta 设成 rect.width / rect.height 即可。
        /// </summary>
        internal static Vector2 ToAnchored(Rect rect)
        {
            return new Vector2(
                rect.x + rect.width * 0.5f - PanelWidth * 0.5f,
                PanelHeight * 0.5f - (rect.y + rect.height * 0.5f));
        }

        /// <summary>第 index 个签到格（0 起）的矩形。越界返回全零。</summary>
        internal static Rect GetCell(int index)
        {
            EnsureBuilt();
            if (index < 0 || index >= _columns * _rows) return new Rect(0f, 0f, 0f, 0f);
            int row = index / _columns;
            int col = index % _columns;
            return new Rect(
                _gridX + col * (_cellWidth + _cellGap),
                _gridY + row * (_cellHeight + _cellGap),
                _cellWidth, _cellHeight);
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _rects = null;
                _icons = null;
                _loadedFromJson = false;
            }
        }

        private static void EnsureBuilt()
        {
            if (_rects != null) return;
            lock (_lock)
            {
                if (_rects != null) return;

                Dictionary<string, Rect> rects = new Dictionary<string, Rect>(StringComparer.Ordinal);
                Dictionary<string, Rect> icons = new Dictionary<string, Rect>(StringComparer.Ordinal);
                bool fromJson = false;
                try
                {
                    string json;
                    if (JsonDataRegistry.TryReadDataFile(DataFileName, out json))
                    {
                        BossRushJsonValue root = BossRushJsonParser.ParseOrNull(json);
                        if (root != null && root.Kind == BossRushJsonKind.Object)
                        {
                            fromJson = ReadTable(root, rects, icons);
                        }
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(DailyReportTuning.LogPrefix
                        + "[WARNING] 版面表读取失败，使用硬编码兜底: " + e.Message);
                    fromJson = false;
                }

                if (!fromJson)
                {
                    ModBehaviour.CriticalLog(
                        "daily-report-layout-fallback",
                        "[DailyReport] [WARNING] " + DataFileName + " 无效，使用硬编码版面兜底");
                    rects.Clear();
                    icons.Clear();
                    ApplyFallback(rects, icons);
                }

                _icons = icons;
                _rects = rects;
                _loadedFromJson = fromJson;
            }
        }

        private static bool ReadTable(BossRushJsonValue root, Dictionary<string, Rect> rects,
            Dictionary<string, Rect> icons)
        {
            BossRushJsonValue rectNode = root.GetObject("rects");
            BossRushJsonValue iconNode = root.GetObject("icons");
            if (rectNode == null || iconNode == null) return false;

            for (int i = 0; i < FallbackNames.Length; i++)
            {
                string name = FallbackNames[i];
                Rect rect;
                if (!TryReadRect(rectNode, name, out rect)) return false;
                rects[name] = rect;
            }
            for (int i = 0; i < IconNames.Length; i++)
            {
                string name = IconNames[i];
                Rect rect;
                if (!TryReadRect(iconNode, name, out rect)) return false;
                icons[name] = rect;
            }

            BossRushJsonValue grid = root.GetObject("grid");
            BossRushJsonValue legend = root.GetObject("legend");
            BossRushJsonValue icon = root.GetObject("icon");
            if (grid == null || legend == null || icon == null) return false;

            _gridX = grid.GetInt("x", 0);
            _gridY = grid.GetInt("y", 0);
            _cellWidth = grid.GetInt("cellWidth", 0);
            _cellHeight = grid.GetInt("cellHeight", 0);
            _cellGap = grid.GetInt("gap", 0);
            _columns = grid.GetInt("columns", 0);
            _rows = grid.GetInt("rows", 0);
            _legendSwatch = legend.GetInt("swatch", 0);
            _legendItemWidth = legend.GetInt("itemWidth", 0);
            _legendCount = legend.GetInt("count", 4);
            _pillTextIndent = icon.GetInt("pillTextIndent", 0);
            _iconTextGap = icon.GetInt("textGap", 0);

            return _cellWidth > 0f && _cellHeight > 0f && _columns > 0 && _rows > 0 && _pillTextIndent > 0f;
        }

        private static bool TryReadRect(BossRushJsonValue node, string name, out Rect rect)
        {
            rect = new Rect(0f, 0f, 0f, 0f);
            List<BossRushJsonValue> values;
            if (!node.TryGetArray(name, out values) || values == null || values.Count != 4) return false;
            float[] parsed = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (values[i] == null) return false;
                parsed[i] = values[i].AsInt(int.MinValue);
                if (parsed[i] <= int.MinValue) return false;
            }
            if (parsed[2] <= 0f || parsed[3] <= 0f) return false;
            rect = new Rect(parsed[0], parsed[1], parsed[2], parsed[3]);
            return true;
        }

        #region 硬编码兜底（与 Assets/Data/DailyReportLayout.json 同源）

        private static readonly string[] FallbackNames =
        {
            "header", "mascot", "title", "info", "infoMeta", "infoWeather",
            "income", "incomePill", "incomeLeft", "incomeRight", "incomeTip", "incomeNote",
            "status", "statusPill", "statusLeft", "statusRight", "statusLuck", "statusTaboo",
            "signin", "signinPill", "button", "sideText", "legend",
        };

        private static readonly float[,] FallbackRects =
        {
            { 22, 14, 1289, 154 }, { 26, 6, 166, 166 }, { 200, 18, 460, 144 }, { 676, 30, 635, 126 },
            { 688, 35, 330, 116 }, { 1042, 35, 257, 116 }, { 22, 182, 766, 464 }, { 38, 200, 280, 46 },
            { 46, 262, 351, 136 }, { 413, 262, 351, 136 }, { 46, 410, 718, 116 }, { 46, 540, 718, 90 },
            { 804, 182, 507, 464 }, { 820, 200, 336, 46 }, { 824, 262, 467, 85 }, { 824, 356, 467, 85 },
            { 824, 450, 467, 85 }, { 824, 544, 467, 85 }, { 22, 660, 1289, 326 }, { 38, 678, 256, 46 },
            { 798, 742, 489, 62 }, { 798, 818, 489, 100 }, { 48, 932, 710, 36 },
        };

        /// <summary>图标 id；Sprite 路径是 Assets/ui/DailyReport/dr_icon_&lt;id&gt;.png。</summary>
        internal static readonly string[] IconNames =
        {
            "issue", "deadline", "weather", "income", "bounty", "tip", "headline", "broadcast",
            "fortune", "gossip", "ribbon_income", "ribbon_status", "ribbon_signin", "gift",
        };

        private static readonly float[,] FallbackIconRects =
        {
            { 692, 48, 36, 36 }, { 692, 106, 36, 36 }, { 1046, 64, 58, 58 }, { 50, 295, 70, 70 },
            { 417, 295, 70, 70 }, { 60, 453, 30, 30 }, { 826, 276, 56, 56 }, { 826, 370, 56, 56 },
            { 826, 464, 56, 56 }, { 826, 558, 56, 56 }, { 44, 195, 54, 54 }, { 828, 197, 50, 50 },
            { 47, 677, 46, 46 }, { 944, 755, 36, 36 },
        };

        private static void ApplyFallback(Dictionary<string, Rect> rects, Dictionary<string, Rect> icons)
        {
            for (int i = 0; i < FallbackNames.Length; i++)
            {
                rects[FallbackNames[i]] = new Rect(
                    FallbackRects[i, 0], FallbackRects[i, 1], FallbackRects[i, 2], FallbackRects[i, 3]);
            }
            for (int i = 0; i < IconNames.Length; i++)
            {
                icons[IconNames[i]] = new Rect(
                    FallbackIconRects[i, 0], FallbackIconRects[i, 1], FallbackIconRects[i, 2], FallbackIconRects[i, 3]);
            }
            _gridX = 48f; _gridY = 742f;
            _cellWidth = 62f; _cellHeight = 52f; _cellGap = 10f;
            _columns = 10; _rows = 3;
            _legendSwatch = 16f; _legendItemWidth = 142f; _legendCount = 5;
            _pillTextIndent = 68f; _iconTextGap = 12f;
        }

        #endregion
    }
}
