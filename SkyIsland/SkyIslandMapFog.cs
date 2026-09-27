using System;
using System.Collections.Generic;
using Duckov.MiniMaps;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：官方地图上的「未探索」灰显。
    ///
    /// 完全靠**数据**实现，不自绘任何界面、也不重绘贴图：
    /// 场景包里的 `MiniMapSettings.maps` 是 1 张底图（全岛暗灰剪影，永远显示）
    /// + 12 张分区彩色图层。官方 `MiniMapDisplay.Setup` 里有
    /// `if (cur.Hide) showGraphics = false;`，而 `MiniMapView.OnOpen()` 每次开图都会重跑
    /// `AutoSetup()` —— 所以这里只要按存档里的到访位翻 `hide`，下次开图就是新的样子。
    ///
    /// 图层靠 **sprite 名字**认领（`sky_island_minimap_<区域>`），不靠列表下标：
    /// 下标会随烘焙脚本的输出顺序悄悄漂移，名字不会。
    /// </summary>
    internal sealed class SkyIslandMapFog : IDisposable
    {
        internal const string SpritePrefix = "sky_island_minimap_";

        private readonly Dictionary<string, MiniMapSettings.MapEntry> layers =
            new Dictionary<string, MiniMapSettings.MapEntry>(StringComparer.Ordinal);
        private bool bound, disposed;
        private int appliedMask = -1;

        /// <summary>区域 id -> 图层数量；给守卫与验收日志用。</summary>
        internal int LayerCount { get { return layers.Count; } }

        /// <summary>
        /// 认领场景包里的分区图层。找不到 `MiniMapSettings` 不是致命错误：
        /// 地图仍然可用，只是全岛都是彩色（相当于没有迷雾），不该因此拖垮整场出击。
        /// </summary>
        internal bool Bind()
        {
            if (disposed || bound) return bound;
            // 不只认 `MiniMapSettings.Instance`：官方那个静态只在 Awake 里
            // `if (Instance == null) Instance = this;`，**从不清空**，指向别的实例时
            // 就会认领不到本图的图层。按「谁身上带着本图的分区贴图」找，最稳。
            MiniMapSettings settings = FindSettings();
            if (settings == null) return false;
            foreach (MiniMapSettings.MapEntry entry in settings.maps)
            {
                if (entry == null || entry.sprite == null) continue;
                string name = entry.sprite.name;
                if (name == null || !name.StartsWith(SpritePrefix, StringComparison.Ordinal)) continue;
                string region = name.Substring(SpritePrefix.Length);
                if (region.Length == 0 || layers.ContainsKey(region)) continue;
                layers[region] = entry;
            }
            bound = layers.Count > 0;
            if (!bound) Debug.LogWarning("[SkyIslandMap] 场景包没有分区图层，未探索灰显不可用");
            return bound;
        }

        /// <summary>找到真正带着本图分区图层的那份设置。</summary>
        private static MiniMapSettings FindSettings()
        {
            MiniMapSettings instance = MiniMapSettings.Instance;
            if (Owns(instance)) return instance;
            foreach (MiniMapSettings candidate in
                UnityEngine.Object.FindObjectsOfType<MiniMapSettings>(true))
                if (Owns(candidate)) return candidate;
            return null;
        }

        private static bool Owns(MiniMapSettings settings)
        {
            if (settings == null || settings.maps == null) return false;
            foreach (MiniMapSettings.MapEntry entry in settings.maps)
                if (entry != null && entry.sprite != null && entry.sprite.name != null
                    && entry.sprite.name.StartsWith(SpritePrefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// 按到访位放开图层。<paramref name="visitedMask"/> 就是存档里的 `visitedRegions`。
        /// 只在掩码真的变了时写，避免每帧改场景数据。
        /// </summary>
        internal void Apply(int visitedMask)
        {
            if (disposed) return;
            if (!bound && !Bind()) return;
            if (visitedMask == appliedMask) return;
            appliedMask = visitedMask;
            foreach (KeyValuePair<string, MiniMapSettings.MapEntry> pair in layers)
            {
                int bit = SkyIslandStoryService.RegionBit(pair.Key);
                // 未登记的区域一律当作未到访：宁可少点亮，也不要凭空泄露没走过的地方。
                pair.Value.hide = bit == 0 || (visitedMask & bit) == 0;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // `MiniMapSettings` 随天空岛场景一起销毁，不需要还原 hide；
            // 这里只放开引用，避免会话结束后还抓着场景对象。
            layers.Clear();
            bound = false;
        }
    }
}
