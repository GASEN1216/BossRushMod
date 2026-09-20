// ============================================================================
// DailyReportLayoutTable.cs - 日报面板版面表（底图与文字的唯一坐标真值）
// ============================================================================
// 底图 Assets/ui/DailyReport/daily_report_bg.png 与本表 Assets/Data/DailyReportLayout.json
// 由同一个脚本 tools/gen_daily_report_ui.py 一次产出。文字按本表摆位，于是
// 「卡片画在哪」与「字写在哪」天生对齐——改版面只改脚本，不用两处手抄坐标。
//
// 坐标系：左上原点、像素单位、与底图同尺寸（1333 × 1013）。
// UI 侧用 ToAnchored() 转成 Unity 的中心原点 + Y 向上。
//
// 硬约束（AGENTS 4.8 第 3 层：大型数据表 = JSON + Registry + guard + 硬编码 fallback）：
//   - 读表失败一律回落硬编码版面（与 JSON 同源，DailyReportLayoutGuard 交叉核对），
//     面板绝不因为少一个数据文件就打不开；
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
        private static bool _loadedFromJson;

        // 网格与图例
        private static float _gridX, _gridY, _cellWidth, _cellHeight, _cellGap;
        private static int _columns, _rows;
        private static float _legendSwatch, _legendItemWidth;
        private static float _iconSize, _iconInset, _iconTextIndent;

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
        internal static float IconSize { get { EnsureBuilt(); return _iconSize; } }
        internal static float IconInset { get { EnsureBuilt(); return _iconInset; } }
        /// <summary>带图标的块里，文字相对块左边的缩进（让开徽章）。</summary>
        internal static float IconTextIndent { get { EnsureBuilt(); return _iconTextIndent; } }

        /// <summary>取一块矩形（左上原点、像素）。未知名字返回全零矩形。</summary>
        internal static Rect Get(string name)
        {
            EnsureBuilt();
            Rect rect;
            return _rects.TryGetValue(name, out rect) ? rect : new Rect(0f, 0f, 0f, 0f);
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
                bool fromJson = false;
                try
                {
                    string json;
                    if (JsonDataRegistry.TryReadDataFile(DataFileName, out json))
                    {
                        BossRushJsonValue root = BossRushJsonParser.ParseOrNull(json);
                        if (root != null && root.Kind == BossRushJsonKind.Object)
                        {
                            fromJson = ReadTable(root, rects);
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
                    ApplyFallback(rects);
                }

                _rects = rects;
                _loadedFromJson = fromJson;
            }
        }

        private static bool ReadTable(BossRushJsonValue root, Dictionary<string, Rect> rects)
        {
            BossRushJsonValue rectNode = root.GetObject("rects");
            if (rectNode == null) return false;

            for (int i = 0; i < FallbackNames.Length; i++)
            {
                string name = FallbackNames[i];
                Rect rect;
                if (!TryReadRect(rectNode, name, out rect)) return false;
                rects[name] = rect;
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
            _iconSize = icon.GetInt("size", 0);
            _iconInset = icon.GetInt("inset", 0);
            _iconTextIndent = icon.GetInt("textIndent", 0);

            return _cellWidth > 0f && _cellHeight > 0f && _columns > 0 && _rows > 0 && _iconTextIndent > 0f;
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
            "header", "mascot", "title", "infoMeta", "infoWeather",
            "income", "incomePill", "incomeLeft", "incomeRight", "incomeTip", "incomeNote",
            "status", "statusPill", "statusLeft", "statusRight", "statusLuck", "statusTaboo",
            "signin", "signinPill", "button", "sideText", "legend",
        };

        private static readonly float[,] FallbackRects =
        {
            { 22, 20, 1289, 150 }, { 40, 32, 126, 126 }, { 182, 46, 430, 98 },
            { 682, 36, 330, 118 }, { 1026, 36, 269, 118 },
            { 22, 186, 766, 402 }, { 42, 200, 232, 46 }, { 46, 260, 351, 132 },
            { 413, 260, 351, 132 }, { 46, 404, 718, 56 }, { 46, 472, 718, 98 },
            { 804, 186, 507, 402 }, { 824, 200, 300, 46 }, { 828, 260, 221, 132 },
            { 1065, 260, 222, 132 }, { 828, 404, 459, 68 }, { 828, 482, 459, 88 },
            { 22, 604, 1289, 316 }, { 42, 618, 232, 46 }, { 780, 680, 505, 62 },
            { 780, 754, 505, 102 }, { 48, 872, 710, 30 },
        };

        private static void ApplyFallback(Dictionary<string, Rect> rects)
        {
            for (int i = 0; i < FallbackNames.Length; i++)
            {
                rects[FallbackNames[i]] = new Rect(
                    FallbackRects[i, 0], FallbackRects[i, 1], FallbackRects[i, 2], FallbackRects[i, 3]);
            }
            _gridX = 48f; _gridY = 680f;
            _cellWidth = 62f; _cellHeight = 52f; _cellGap = 10f;
            _columns = 10; _rows = 3;
            _legendSwatch = 16f; _legendItemWidth = 177f;
            _iconSize = 34f; _iconInset = 12f; _iconTextIndent = 56f;
        }

        #endregion
    }
}
