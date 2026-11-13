using System;
using System.Reflection;
using Duckov.MiniMaps;
using Duckov.MiniMaps.UI;
using HarmonyLib;
using SodaCraft.Localizations;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的官方地图校准（口径同 `SkyIslandMapFog.KeepOfficialCenter`）：官方 `MiniMapSettings.Instance` 只在 Awake 里
    /// 「为空才赋值」、从不清空，上一张图那份还活着时本图这份当不上 Instance；标记按 `mapWorldCenter` 换算，
    /// 与底图烘焙中心不一致时所有标记整体平移。按「谁身上带着本图底图」找到设置，0.5 秒节流校回中心与 Instance。
    /// </summary>
    internal sealed partial class TidegateMap : IDisposable
    {
        internal const string SpriteName = "tidegate_minimap";
        private static readonly MethodInfo InstanceSetter = FindInstanceSetter();
        private static readonly FieldInfo CompassArrow = AccessTools.Field(typeof(MiniMapCompass), "arrow");
        private static TidegateMap current;
        private MiniMapCompass compass;
        private Transform compassArrow;
        private Quaternion savedArrowRotation;
        private bool compassWasEnabled;
        private MiniMapSettings settings;
        private float nextCheck;
        private bool warned, disposed;
        private SystemLanguage mapLanguage = LocalizationManager.CurrentLanguage;
        private readonly System.Collections.Generic.List<GameObject> points = new System.Collections.Generic.List<GameObject>();
        private sealed class ExitPin
        {
            internal string Id;
            internal SimplePointOfInterest Poi;
            internal TidegateExitState State;
        }
        private readonly System.Collections.Generic.List<ExitPin> exitPins = new System.Collections.Generic.List<ExitPin>();

        internal bool Bound { get { return settings != null; } }

        internal TidegateMap(TidegateWorld world)
        {
            AddExitPoints(world);
            if (Owns(MiniMapSettings.Instance)) settings = MiniMapSettings.Instance;
            else
                foreach (MiniMapSettings candidate in UnityEngine.Object.FindObjectsOfType<MiniMapSettings>(true))
                    if (Owns(candidate)) { settings = candidate; break; }
            if (settings == null) Debug.LogWarning(TidegateRaidScene.LogTag + " 场景包里没有潮闸镇地图设置，官方地图不可用");
            current = this;
            Tick();
        }

        /// <summary>四处撤离点在官方地图上的圈与标签（标签写明什么时候能走；官方出口图标随 ExitCreator，拿不到就用默认图标）。</summary>
        private void AddExitPoints(TidegateWorld world)
        {
            if (world == null) return;
            Sprite exitIcon = SkyIslandMapMarkers.ExitIcon();
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
                    poi.Setup(exitIcon, TidegateText.ExitMapKey(exit.Key));
                    exitPins.Add(new ExitPin { Id = exit.Key, Poi = poi });
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

        // 官方 M 图的 AutoSetup 会任取一个已加载的设置，Setup 则把展示根按相机方位转一遍。
        // 潮闸镇的街道与正射底图使用世界北向。只在官方 M 界面接管整张本图（连同所有标记和点选坐标）。
        [HarmonyPatch(typeof(MiniMapDisplay), "Setup", new Type[] { typeof(IMiniMapDataProvider) })]
        private static class NorthUp
        {
            private static void Prefix(MiniMapDisplay __instance, ref IMiniMapDataProvider dataProvider)
            {
                TidegateMap owner = current;
                if (owner == null || owner.settings == null || !(dataProvider is MiniMapSettings) || !TidegateRaidScene.IsActiveScene) return;
                if (__instance.GetComponentInParent<MiniMapView>() != null) dataProvider = owner.settings;
            }

            private static void Postfix(MiniMapDisplay __instance, IMiniMapDataProvider dataProvider)
            {
                TidegateMap owner = current;
                if (owner == null || owner.settings == null) return;
                MiniMapView view = __instance.GetComponentInParent<MiniMapView>();
                if (view == null) return;
                if (!ReferenceEquals(owner.settings, dataProvider) || !TidegateRaidScene.IsActiveScene) { owner.ReleaseCompass(); return; }
                __instance.transform.localRotation = Quaternion.identity;
                owner.PinCompass(view);
            }
        }

        private void PinCompass(MiniMapView view)
        {
            MiniMapCompass next = view.GetComponentInChildren<MiniMapCompass>(true);
            if (next == null || CompassArrow == null) return;
            Transform arrow = CompassArrow.GetValue(next) as Transform;
            if (arrow == null) return;
            if (compass != next)
            {
                ReleaseCompass();
                compass = next;
                compassArrow = arrow;
                savedArrowRotation = arrow.localRotation;
                compassWasEnabled = next.enabled;
                next.enabled = false; // 官方组件逐帧按镜头重转指南针。
            }
            arrow.rotation = Quaternion.identity;
        }

        private void ReleaseCompass()
        {
            if (compassArrow != null) compassArrow.localRotation = savedArrowRotation;
            if (compass != null) compass.enabled = compassWasEnabled;
            compassArrow = null;
            compass = null;
        }

        internal void Tick(TidegateExtraction exits = null, bool beaconLit = false)
        {
            if (disposed || settings == null || settings.maps == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            try
            {
                SystemLanguage language = LocalizationManager.CurrentLanguage;
                bool languageChanged = language != mapLanguage;
                if (languageChanged) TidegateText.InjectMapLabels();
                foreach (ExitPin pin in exitPins)
                {
                    var state = exits != null ? exits.StateForMap(pin.Id, beaconLit) : pin.State;
                    if (pin.Poi == null || (state == pin.State && !languageChanged)) continue;
                    pin.Poi.Color = state == TidegateExitState.Open ? new Color(0.62f, 0.88f, 0.74f)
                        : state == TidegateExitState.LightLoad || state == TidegateExitState.Inbound ? new Color(0.95f, 0.79f, 0.53f)
                        : new Color(0.64f, 0.69f, 0.73f);
                    pin.Poi.Setup(null, state == TidegateExitState.Unknown ? TidegateText.ExitMapKey(pin.Id) : TidegateText.ExitStateKey(pin.Id, state));
                    pin.State = state;
                }
                mapLanguage = language;
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
            if (current == this) current = null;
            ReleaseCompass();
            settings = null;
            foreach (GameObject point in points) if (point != null) UnityEngine.Object.Destroy(point);
            points.Clear();
            exitPins.Clear();
        }
    }
}
