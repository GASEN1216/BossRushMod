// ============================================================================
// DailyReportBackground.cs - 日报面板底图、图标与吉祥物的加载
// ============================================================================
// 取图走 ModSprites：正式资源从共享 production_icons 包按原 PNG 路径借用 Sprite，
// 修改原 PNG 后必须同步作者工程并重打该包，散图只供 Dev 或包缺席时回退。
// 只在面板装配时惰性加载，不为日报另建资源 owner。
//
// 2026-09-23 第五轮：图标和吉祥物从底图里拆出来，各自是 256px 的独立 Sprite
// （Assets/ui/DailyReport/dr_icon_*.png、dr_mascot.png）。底图进包被压到 1024 宽再放大 1.3–1.7 倍，
// 烤在里面的小图标只剩二十几个纹素；独立 Sprite 放大也清楚。
//
// fail-open：读不到就返回 null——底图缺席退回纯纸色底，图标缺席就不画那一格（不退回汉字或灰方块）。
// 缓存：这里只记引用（取不到也记 null）；借来的 Sprite 由 ModSprites / ProductionIconCache 释放，这里不销毁。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>日报底图与图标。惰性读一次，fail-open。</summary>
    internal static class DailyReportBackground
    {
        private const string ArtFolder = "Assets/ui/DailyReport/";
        private const string RelativePath = ArtFolder + "daily_report_bg.png";
        internal const string MascotFile = "dr_mascot.png";

        private static Sprite _sprite;
        private static bool _attempted;

        // 图标 / 吉祥物：按文件名缓存（取不到也缓存 null，面板重建时不再反复读盘）
        private static readonly Dictionary<string, Sprite> _art = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>取底图。读不到返回 null（调用方退回纯色底）。</summary>
        internal static Sprite Load()
        {
            if (_attempted) return _sprite;
            _attempted = true;
            _sprite = ModSprites.Get(RelativePath);
            return _sprite;
        }

        /// <summary>图标 id → Sprite（dr_icon_&lt;id&gt;.png）。取不到返回 null，调用方不画那一格。</summary>
        internal static Sprite LoadIcon(string iconId)
        {
            return string.IsNullOrEmpty(iconId) ? null : LoadArt("dr_icon_" + iconId + ".png");
        }

        /// <summary>日报目录下的一张图（图标 / 吉祥物），与底图同一个取图入口。</summary>
        internal static Sprite LoadArt(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            Sprite sprite;
            if (_art.TryGetValue(fileName, out sprite)) return sprite;
            sprite = ModSprites.Get(ArtFolder + fileName);
            _art[fileName] = sprite;
            return sprite;
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。只清引用，借来的 Sprite 不归这里销毁。</summary>
        internal static void ResetStaticCaches()
        {
            _sprite = null;
            _attempted = false;
            _art.Clear();
        }
    }
}
