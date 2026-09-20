// ============================================================================
// DailyReportBackground.cs - 日报面板底图加载
// ============================================================================
// 底图是随 Mod 部署的散图（Assets/ui/DailyReport/daily_report_bg.png），不是 bundle：
// 它只有一张、只在面板打开时用一次，为它单开一个 AssetBundle 不划算，
// 形态与 Campaign 的 raw PNG 一致（compile_official.bat 的部署步骤一起拷）。
//
// fail-open：读不到就返回 null，面板退回纯纸色底，文字位置不变。
// 缓存：程序化创建的 Sprite / Texture 带 DontSave，切场景不会自动回收，
// 必须由 ResetStaticCaches 显式销毁，否则每次重建宿主都漏一份贴图。
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
