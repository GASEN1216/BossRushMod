using System;
using System.Reflection;
using Duckov.MiniMaps;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的官方地图校准（口径同 `SkyIslandMapFog.KeepOfficialCenter`）：官方 `MiniMapSettings.Instance` 只在 Awake 里
    /// 「为空才赋值」、从不清空，上一张图那份还活着时本图这份当不上 Instance；标记按 `mapWorldCenter` 换算，
    /// 与底图烘焙中心不一致时所有标记整体平移。按「谁身上带着本图底图」找到设置，0.5 秒节流校回中心与 Instance。
    /// </summary>
    internal sealed class TidegateMap : IDisposable
    {
        internal const string SpriteName = "tidegate_minimap";
        private static readonly MethodInfo InstanceSetter = FindInstanceSetter();
        private MiniMapSettings settings;
        private float nextCheck;
        private bool warned, disposed;
        private readonly System.Collections.Generic.List<GameObject> points = new System.Collections.Generic.List<GameObject>();

        internal bool Bound { get { return settings != null; } }

        internal TidegateMap(TidegateWorld world)
        {
            AddExitPoints(world);
            if (Owns(MiniMapSettings.Instance)) settings = MiniMapSettings.Instance;
            else
                foreach (MiniMapSettings candidate in UnityEngine.Object.FindObjectsOfType<MiniMapSettings>(true))
                    if (Owns(candidate)) { settings = candidate; break; }
            if (settings == null) Debug.LogWarning(TidegateRaidScene.LogTag + " 场景包里没有潮闸镇地图设置，官方地图不可用");
            Tick();
        }

        /// <summary>四处撤离点在官方地图上的圈与标签（标签写明什么时候能走；官方出口图标随 ExitCreator，拿不到就用默认图标）。</summary>
        private void AddExitPoints(TidegateWorld world)
        {
            if (world == null) return;
            foreach (System.Collections.Generic.KeyValuePair<string, Transform> exit in world.Exits)
            {
                try
                {
                    var go = new GameObject("TidegateMapExit_" + exit.Key);
                    go.transform.SetParent(world.Root.transform, false);
                    go.transform.position = exit.Value.position;
                    SimplePointOfInterest poi = go.AddComponent<SimplePointOfInterest>();
                    poi.Color = exit.Key == "road" ? new Color(0.60f, 0.84f, 0.86f, 1f) : new Color(0.62f, 0.88f, 0.74f, 1f);
                    poi.ScaleFactor = 1f;
                    poi.IsArea = true;
                    poi.AreaRadius = 2.5f;
                    poi.Setup(null, TidegateText.ExitMapKey(exit.Key));
                    points.Add(go);
                }
                catch (Exception e) { Debug.LogWarning(TidegateRaidScene.LogTag + " 撤离点地图标记创建失败 " + exit.Key + "：" + e.Message); }
            }
        }

        private static bool Owns(MiniMapSettings value)
        {
            if (value == null || value.maps == null) return false;
            foreach (MiniMapSettings.MapEntry entry in value.maps)
                if (entry != null && entry.sprite != null && string.Equals(entry.sprite.name, SpriteName, StringComparison.Ordinal)) return true;
            return false;
        }

        private static MethodInfo FindInstanceSetter()
        {
            try
            {
                PropertyInfo property = BossRush.Common.Utils.ReflectionCache.GetProperty(
                    typeof(MiniMapSettings), "Instance", BindingFlags.Public | BindingFlags.Static);
                return property != null ? property.GetSetMethod(true) : null;
            }
            catch (Exception) { return null; }
        }

        internal void Tick()
        {
            if (disposed || settings == null || settings.maps == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
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
                if ((moved || foreign) && !warned)
                {
                    warned = true;
                    Debug.Log(TidegateRaidScene.LogTag + " 官方地图原点 / 实例已校回 center=" + center + " moved=" + moved + " foreign=" + foreign);
                }
            }
            catch (Exception e)
            {
                if (warned) return;
                warned = true;
                Debug.LogWarning(TidegateRaidScene.LogTag + " 地图原点校准失败（标记可能偏移）：" + e.Message);
            }
        }

        public void Dispose()
        {
            disposed = true;
            settings = null;
            foreach (GameObject point in points) if (point != null) UnityEngine.Object.Destroy(point);
            points.Clear();
        }
    }
}
