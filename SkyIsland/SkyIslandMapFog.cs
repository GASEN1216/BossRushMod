using System;
using System.Collections.Generic;
using System.Reflection;
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
        /// <summary>官方 `MiniMapSettings.Instance` 是私有 setter 的静态属性，只在 Awake 里「为空才赋值」。</summary>
        private static readonly MethodInfo InstanceSetter = InstanceSetterOrNull();

        private MiniMapSettings settings;
        private bool bound, disposed, centerWarned;
        private int appliedMask = -1;
        private float nextCenterCheck;

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
            settings = FindSettings();
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
            KeepOfficialCenter();
            return bound;
        }

        /// <summary>
        /// 让官方地图的换算原点等于底图的烘焙中心。会话每帧推进里调用，自己按 0.5 秒节流，一次只比一个引用和 13 个向量。
        ///
        /// 官方地图上的区域圈、撤离点、玩家自己的标记与右键标点，都按 `MiniMapCenter.GetCenter` 换算：
        /// 取 `MiniMapSettings.Instance` 里首条同场景条目的 `mapWorldCenter`，找不到就取它的 `combinedCenter`。
        /// 底图却只按条目的 Offset 摆在画布正中，对应的世界点是烘焙中心（构建器写进本图的 `combinedCenter`）。
        /// 两边原点不是同一个点时，所有标记相对底图整体平移，圈落在画出来的建筑旁边（owner 2026-10-02 实测「圈与实际位置不符」）。
        /// 可能让两边分开的两处官方行为（哪一处是那次的成因没有实机采样，UNVERIFIED）：
        /// - `Instance` 只在 Awake 里「为空才赋值」、从不清空：上一张图那份还活着时，本图这份当不上 Instance；
        /// - `MiniMapCenter.Cache` 拿自己的 transform 覆盖首条同场景条目的 `mapWorldCenter`：本图不放 MiniMapCenter
        ///   （`SkyIslandMiniMapGuard`），运行时实例化的官方关卡预制体里有没有不归场景包管。
        /// 两处都按本图那份数据校回来；`Instance` 随岛一起销毁后官方下一张图的 Awake 会重新赋值，不用还原。
        /// </summary>
        internal void KeepOfficialCenter()
        {
            if (disposed || settings == null || settings.maps == null || Time.unscaledTime < nextCenterCheck) return;
            nextCenterCheck = Time.unscaledTime + 0.5f;
            try
            {
                Vector3 center = settings.combinedCenter;
                bool moved = false;
                foreach (MiniMapSettings.MapEntry entry in settings.maps)
                {
                    if (entry == null || entry.mapWorldCenter == center) continue;
                    entry.mapWorldCenter = center;
                    moved = true;
                }
                bool foreign = MiniMapSettings.Instance != settings;
                if (foreign && InstanceSetter != null) InstanceSetter.Invoke(null, new object[] { settings });
                if ((moved || foreign) && !centerWarned)
                {
                    centerWarned = true;
                    Debug.LogWarning("[SkyIslandMap] 官方地图原点与底图烘焙中心不一致，已校回 center=" + center
                        + " entryMoved=" + moved + " instanceForeign=" + foreign + " setter=" + (InstanceSetter != null));
                }
            }
            catch (Exception e)
            {
                if (centerWarned) return;
                centerWarned = true;
                Debug.LogWarning("[SkyIslandMap] 地图原点校准失败（标记可能偏移）：" + e.Message);
            }
        }

        private static MethodInfo InstanceSetterOrNull()
        {
            try
            {
                PropertyInfo property = BossRush.Common.Utils.ReflectionCache.GetProperty(
                    typeof(MiniMapSettings), "Instance", BindingFlags.Public | BindingFlags.Static);
                return property != null ? property.GetSetMethod(true) : null;
            }
            catch (Exception) { return null; }
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
            settings = null;
            bound = false;
        }
    }
}
