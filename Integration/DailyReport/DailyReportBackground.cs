// ============================================================================
// DailyReportBackground.cs - 日报面板底图加载
// ============================================================================
// 正式底图优先从共享 production_icons 包按原 PNG 路径借用 Sprite。
// 修改原 PNG 后必须同步作者工程并重打该包，散图只供包缺席时回退。
// 只在面板打开时惰性加载，不为单张背景另建资源 owner。
//
// fail-open：读不到就返回 null，面板退回纯纸色底，文字位置不变。
// 缓存：借用 Sprite 由 ProductionIconCache 卸载；raw 回退创建的 Sprite / Texture
// 由 ResetStaticCaches 显式销毁，避免重复释放共享纹理或泄漏自造资源。
// ============================================================================

using System;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>日报底图。惰性读一次，fail-open。</summary>
    internal static class DailyReportBackground
    {
        private const string RelativePath = "Assets/ui/DailyReport/daily_report_bg.png";

        private static Sprite _sprite;
        private static Texture2D _texture;
        private static bool _attempted;

        /// <summary>诊断：底图是否已加载。</summary>
        internal static bool IsLoaded { get { return _sprite != null; } }

        /// <summary>取底图。读不到返回 null（调用方退回纯色底）。</summary>
        internal static Sprite Load()
        {
            if (_attempted) return _sprite;
            _attempted = true;
            _sprite = ProductionIconCache.Get(RelativePath);
            if (_sprite != null || !ProductionIconCache.AllowRawFallback) return _sprite;
            _sprite = RawImageLoader.LoadSprite(Path.Combine(ModBehaviour.GetModPath(), RelativePath), "daily_report_bg");
            if (_sprite != null) _texture = _sprite.texture;
            return _sprite;
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。显式销毁 DontSave 资源。</summary>
        internal static void ResetStaticCaches()
        {
            try
            {
                if (_sprite != null && !ProductionIconCache.IsBorrowed(_sprite)) UnityEngine.Object.Destroy(_sprite);
                if (_texture != null) UnityEngine.Object.Destroy(_texture);
            }
            catch (Exception)
            {
                // 销毁失败只丢引用
            }
            finally
            {
                _sprite = null;
                _texture = null;
                _attempted = false;
            }
        }
    }
}
